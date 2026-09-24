using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using UnityEngine;

namespace RuinarchPlus
{
	/// <summary>A Ruinarch+ village building: a ModContent structure that borrows the prefab
	/// (look, footprint, build cost) of a vanilla building.</summary>
	internal sealed class ModBuilding
	{
		internal readonly string Id;
		internal readonly string Name;
		internal readonly STRUCTURE_TYPE Borrowed;

		internal ModBuilding(string id, string name, STRUCTURE_TYPE borrowed)
		{
			Id = id;
			Name = name;
			Borrowed = borrowed;
		}

		internal STRUCTURE_TYPE Type => ModContent.StructureTypeFor(Id);
	}

	/// <summary>
	/// Villagers build Ruinarch+ buildings themselves, through the game's own construction
	/// pipeline: a vanilla <c>PLACE_BLUEPRINT</c> job, then materials hauled and a builder's
	/// <c>BuildBlueprint</c>. Placement uses <c>LandmarkManager.CanPlaceStructureBlueprint</c>
	/// with the borrowed building's prefabs (the content framework maps our type to them).
	///
	/// The finished structure's type comes from the PREFAB (<c>GenericTileObject.BuildBlueprint</c>
	/// passes <c>p_blueprint.structureType</c>, which a borrowed prefab reports as the vanilla
	/// type). Pooled prefabs are shared with real buildings, so we never mutate them. Instead
	/// the tile holding one of our blueprints is remembered, and while that tile is being built
	/// the vanilla type passed to <c>CreateNewStructureAt</c> is swapped for ours, so the
	/// framework factory builds our class. The remembered tiles ride in the save
	/// (<c>ModData/ruinarch.plus.blueprints.json</c>), so a half-built one survives a reload.
	/// </summary>
	internal static class ModBuildings
	{
		private const string SaveId = "ruinarch.plus.blueprints";

		private struct Entry
		{
			internal LocationStructureObject Blueprint;
			internal NPCSettlement Settlement;
			internal ModBuilding Building;
		}

		private static readonly List<ModBuilding> All = new List<ModBuilding>();
		private static readonly Dictionary<GenericTileObject, Entry> Pending = new Dictionary<GenericTileObject, Entry>();

		// Set only while a tile holding one of our blueprints is being built.
		internal static ModBuilding Armed;

		internal static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		internal static ModBuilding Add(string id, string name, STRUCTURE_TYPE borrowed)
		{
			ModBuilding b = new ModBuilding(id, name, borrowed);
			All.Add(b);
			return b;
		}

		internal static ModBuilding For(STRUCTURE_TYPE type)
		{
			for (int i = 0; i < All.Count; i++)
			{
				if (All[i].Type == type)
				{
					return All[i];
				}
			}
			return null;
		}

		private static ModBuilding ById(string id)
		{
			for (int i = 0; i < All.Count; i++)
			{
				if (All[i].Id == id)
				{
					return All[i];
				}
			}
			return null;
		}

		/// <summary>Mirror of the private SettlementJobTriggerComponent.TryCreatePlaceBlueprintJob
		/// + TriggerPlaceBlueprint. The building is made of what this faction would build
		/// <paramref name="materialOf"/> from. Returns the prefab name, or null if nothing was queued.</summary>
		internal static string QueueBlueprint(NPCSettlement settlement, ModBuilding building, STRUCTURE_TYPE materialOf)
		{
			StructureSetting like = settlement.owner.factionType.CreateStructureSettingForStructure(materialOf, settlement);
			if (!like.hasValue)
			{
				return null;
			}
			StructureSetting setting = new StructureSetting(building.Type, like.resource);
			if (!LandmarkManager.Instance.CanPlaceStructureBlueprint(settlement.owner.factionType.type, settlement, setting,
				out LocationGridTile centerTile, out string prefabName, out int _, out LocationGridTile connectorTile))
			{
				return null;
			}
			GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.PLACE_BLUEPRINT, INTERACTION_TYPE.PLACE_BLUEPRINT,
				centerTile.tileObjectComponent.genericTileObject, settlement);
			job.AddOtherData(INTERACTION_TYPE.PLACE_BLUEPRINT, new object[3] { prefabName, connectorTile, setting });
			job.SetDoNotRecalculate(state: true);
			job.SetCanTakeThisJobChecker("CanTakePlaceBlueprintJob");
			settlement.AddToAvailableJobs(job);
			return prefabName;
		}

		/// <summary>Build instantly (no villagers, no materials) at a spot the game's own
		/// placement approves, using the real prefab so it renders like any built structure.
		/// For the debug menu and the test harness. Null if the settlement has no valid spot.</summary>
		internal static LocationStructure InstantBuild(NPCSettlement settlement, ModBuilding building, STRUCTURE_TYPE materialOf)
		{
			StructureSetting like = settlement.owner.factionType.CreateStructureSettingForStructure(materialOf, settlement);
			StructureSetting setting = new StructureSetting(building.Type, like.hasValue ? like.resource : RESOURCE.WOOD);
			if (!LandmarkManager.Instance.CanPlaceStructureBlueprint(settlement.owner.factionType.type, settlement, setting,
				out LocationGridTile centerTile, out string prefabName, out int _, out LocationGridTile _))
			{
				return null;
			}
			Armed = building;
			try
			{
				return centerTile.tileObjectComponent.genericTileObject.InstantPlaceStructure(prefabName, settlement);
			}
			finally
			{
				Armed = null;
			}
		}

		internal static bool HasPendingFor(NPCSettlement settlement, ModBuilding building)
		{
			foreach (KeyValuePair<GenericTileObject, Entry> kv in Pending)
			{
				if (kv.Value.Settlement == settlement && kv.Value.Building == building && kv.Key.blueprintOnTile == kv.Value.Blueprint)
				{
					return true;
				}
			}
			return false;
		}

		internal static void MarkPlaced(GenericTileObject tile, NPCSettlement settlement, ModBuilding building, bool announce = true)
		{
			if (tile?.blueprintOnTile == null)
			{
				return;
			}
			Pending[tile] = new Entry { Blueprint = tile.blueprintOnTile, Settlement = settlement, Building = building };
			if (announce)
			{
				RuinarchPlus.Log?.Info($"{building.Name} blueprint placed in {settlement?.name ?? "a village"}; villagers will now gather materials and build it.");
			}
		}

		/// <summary>Our building whose blueprint <paramref name="tile"/> still holds, or null.</summary>
		internal static ModBuilding BlueprintOn(GenericTileObject tile)
		{
			return tile != null && Pending.TryGetValue(tile, out Entry e) && e.Blueprint != null && tile.blueprintOnTile == e.Blueprint
				? e.Building
				: null;
		}

		internal static void Forget(GenericTileObject tile)
		{
			if (tile != null)
			{
				Pending.Remove(tile);
			}
		}

		/// <summary>Drop entries whose blueprint expired, was cancelled or was replaced.</summary>
		internal static void PruneStale()
		{
			List<GenericTileObject> stale = null;
			foreach (KeyValuePair<GenericTileObject, Entry> kv in Pending)
			{
				if (kv.Key == null || kv.Key.blueprintOnTile != kv.Value.Blueprint)
				{
					(stale ?? (stale = new List<GenericTileObject>())).Add(kv.Key);
				}
			}
			if (stale != null)
			{
				for (int i = 0; i < stale.Count; i++)
				{
					Pending.Remove(stale[i]);
				}
			}
		}

		// ---- persistence -------------------------------------------------------------------
		// One "id|x|y|settlementId" string per blueprint (main-region map coordinates of its
		// centre tile, and the village building it: the tile's area may belong to another).

		private static string Save()
		{
			PruneStale();
			BlueprintSaveData file = new BlueprintSaveData();
			foreach (KeyValuePair<GenericTileObject, Entry> kv in Pending)
			{
				LocationGridTile t = kv.Key.gridTileLocation;
				if (t != null)
				{
					file.blueprints.Add($"{kv.Value.Building.Id}|{t.localPlace.x}|{t.localPlace.y}|{kv.Value.Settlement?.persistentID}");
				}
			}
			return file.blueprints.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Pending.Clear();
			Armed = null;
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			BlueprintSaveData file = JsonUtility.FromJson<BlueprintSaveData>(json);
			InnerTileMap map = GridMap.Instance?.mainRegion?.innerMap;
			int count = 0;
			foreach (string line in file?.blueprints ?? new List<string>())
			{
				string[] parts = line.Split('|');
				ModBuilding building = parts.Length == 4 ? ById(parts[0]) : null;
				LocationGridTile tile = building != null && map != null && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y)
					? map.GetTileFromMapCoordinates(x, y)
					: null;
				GenericTileObject generic = tile?.tileObjectComponent?.genericTileObject;
				if (generic?.blueprintOnTile == null)
				{
					RuinarchPlus.Log?.Warning($"Blueprints: dropped saved entry {line} (no blueprint on that tile).");
					continue;
				}
				NPCSettlement settlement = LandmarkManager.Instance.GetSettlementByPersistentID(parts[3]) as NPCSettlement;
				MarkPlaced(generic, settlement ?? tile.area?.GetFirstNPCSettlementOnArea(), building, announce: false);
				count++;
			}
			RuinarchPlus.Log?.Info($"Blueprints loaded: {count} Ruinarch+ building(s) under construction.");
		}
	}

	[Serializable]
	public class BlueprintSaveData
	{
		public List<string> blueprints = new List<string>();
	}

	// A villager just placed a blueprint: remember it if it is one of ours.
	[HarmonyPatch(typeof(PlaceBlueprint), nameof(PlaceBlueprint.PrePlaceSuccess))]
	internal static class ModBuildings_BlueprintPlaced
	{
		private static void Postfix(ActualGoapNode goapNode)
		{
			try
			{
				if (goapNode.otherData == null || goapNode.otherData.Length < 3 || !(goapNode.otherData[2]?.obj is StructureSetting setting))
				{
					return;
				}
				ModBuilding building = ModBuildings.For(setting.structureType);
				if (building != null && goapNode.poiTarget is GenericTileObject tile)
				{
					ModBuildings.MarkPlaced(tile, goapNode.actor?.homeSettlement, building);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Blueprint tracking failed: " + e.Message);
			}
		}
	}

	// Construction finishing on one of our blueprint tiles: arm the type swap for the
	// duration of the build, and always disarm afterwards.
	[HarmonyPatch(typeof(GenericTileObject), nameof(GenericTileObject.BuildBlueprintOnTile))]
	internal static class ModBuildings_BuildBlueprint
	{
		private static void Prefix(GenericTileObject __instance, out bool __state)
		{
			ModBuildings.Armed = ModBuildings.BlueprintOn(__instance);
			__state = ModBuildings.Armed != null;
		}

		private static Exception Finalizer(GenericTileObject __instance, bool __state, Exception __exception)
		{
			ModBuildings.Armed = null;
			if (__state)
			{
				ModBuildings.Forget(__instance);
			}
			return __exception;
		}
	}

	// Runs before the content framework's own CreateNewStructureAt prefix: while armed, the
	// borrowed prefab's vanilla type becomes ours, which the framework's factory then turns
	// into our structure class.
	[HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.CreateNewStructureAt))]
	internal static class ModBuildings_SwapBuiltType
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(ref STRUCTURE_TYPE structureType, BaseSettlement settlement)
		{
			ModBuilding b = ModBuildings.Armed;
			if (b != null && structureType == b.Borrowed)
			{
				ModBuildings.Armed = null;
				structureType = b.Type;
				RuinarchPlus.Log?.Info($"A {b.Name} was built in {settlement?.name ?? "a village"}.");
			}
		}
	}

	// The builder's action text ("... is building X") names the blueprint prefab's type,
	// the borrowed vanilla one. While BuildBlueprint writes that text for one of our
	// blueprint tiles, the name lookup gets our type instead (the content framework names
	// it). Same shape as the build swap: no prefab is mutated.
	internal static class ModBuildings_BuildLabel
	{
		[ThreadStatic]
		internal static ModBuilding Armed;

		internal static void Arm(ActualGoapNode goapNode)
		{
			Armed = goapNode?.poiTarget is GenericTileObject tile ? ModBuildings.BlueprintOn(tile) : null;
		}
	}

	[HarmonyPatch(typeof(BuildBlueprint), nameof(BuildBlueprint.AddFillersToLog))]
	internal static class ModBuildings_BuildLabel_Fillers
	{
		private static void Prefix(ActualGoapNode goapNode) => ModBuildings_BuildLabel.Arm(goapNode);

		private static Exception Finalizer(Exception __exception)
		{
			ModBuildings_BuildLabel.Armed = null;
			return __exception;
		}
	}

	[HarmonyPatch(typeof(BuildBlueprint), nameof(BuildBlueprint.PreBuildSuccess))]
	internal static class ModBuildings_BuildLabel_Start
	{
		private static void Prefix(ActualGoapNode goapNode) => ModBuildings_BuildLabel.Arm(goapNode);

		private static Exception Finalizer(Exception __exception)
		{
			ModBuildings_BuildLabel.Armed = null;
			return __exception;
		}
	}

	// Runs before the content framework's LocalizedStructureName prefix, which then names
	// our type.
	[HarmonyPatch(typeof(Extensions), nameof(Extensions.LocalizedStructureName), new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class ModBuildings_BuildLabelName
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(ref STRUCTURE_TYPE structureType)
		{
			ModBuilding b = ModBuildings_BuildLabel.Armed;
			if (b != null && structureType == b.Borrowed)
			{
				structureType = b.Type;
			}
		}
	}
}
