using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using UnityEngine;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// Villagers BUILD the Mass Grave themselves, through the game's own construction
	/// pipeline (config: <c>massGraveBurialEnabled</c>).
	///
	/// Hourly, a village with a body lying in it that nothing else will take (any body when
	/// it has no Cemetery or Cult Temple; a creature's carcass even when it has one, since
	/// the game never buries animals there), and no Mass Grave or pending Mass Grave
	/// blueprint, queues a vanilla <c>PLACE_BLUEPRINT</c> job for a
	/// Mass Grave. From there it is the stock flow: a villager places the blueprint,
	/// villagers haul the wood/stone its <c>craftCost</c> asks for, and a builder finishes it
	/// (<c>BuildBlueprint</c>). Placement uses <c>LandmarkManager.CanPlaceStructureBlueprint</c>
	/// and the Cemetery's prefabs (the framework maps our type to them).
	///
	/// The finished structure's type comes from the PREFAB (<c>GenericTileObject.BuildBlueprint</c>
	/// passes <c>p_blueprint.structureType</c>, which a Cemetery prefab reports as CEMETERY).
	/// Pooled prefabs are shared with real cemeteries, so we never mutate them. Instead the
	/// tile whose blueprint came from a Mass Grave job is remembered, and while that tile is
	/// being built the CEMETERY argument to <c>CreateNewStructureAt</c> is swapped for the
	/// Mass Grave type, so the framework factory builds a <see cref="MassGrave"/>.
	///
	/// Limitation: the remembered tile is not saved. A Mass Grave blueprint that is saved
	/// half-built and reloaded completes as a regular Cemetery (which also ends the scattering).
	/// </summary>
	internal static class MassGraveConstruction
	{
		// GenericTileObject holding a Mass Grave blueprint -> the blueprint instance + settlement.
		private static readonly Dictionary<GenericTileObject, KeyValuePair<LocationStructureObject, NPCSettlement>> Pending =
			new Dictionary<GenericTileObject, KeyValuePair<LocationStructureObject, NPCSettlement>>();

		// Set only while BuildBlueprintOnTile runs for a Mass Grave blueprint.
		internal static bool SwapArmed;

		internal static STRUCTURE_TYPE MassGraveType => ModContent.StructureTypeFor(MassGraveFeature.Id);

		/// <summary>Hourly: queue a Mass Grave blueprint in every village that needs one.</summary>
		internal static void HourlyCheck()
		{
			if (!MassGraveBurial.Enabled || GridMap.Instance?.mainRegion?.settlementsInRegion == null)
			{
				return;
			}
			PruneStalePending();
			List<BaseSettlement> settlements = GridMap.Instance.mainRegion.settlementsInRegion;
			for (int i = 0; i < settlements.Count; i++)
			{
				try
				{
					if (settlements[i] is NPCSettlement settlement && NeedsMassGrave(settlement))
					{
						TryQueueBlueprint(settlement);
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning("Mass Grave construction check failed: " + e.Message);
				}
			}
		}

		internal static bool NeedsMassGrave(NPCSettlement settlement)
		{
			if (settlement.owner == null || settlement.cityCenter == null || settlement.residents == null || settlement.residents.Count == 0)
			{
				return false;
			}
			if (MassGrave.FindFor(settlement) != null || HasPendingFor(settlement))
			{
				return false;
			}
			// The game allows one blueprint job per settlement at a time; wait our turn.
			if (settlement.HasJob(JOB_TYPE.PLACE_BLUEPRINT))
			{
				return false;
			}
			return HasLooseCorpse(settlement);
		}

		// A body in the village that only a Mass Grave would take. Scans the region's full
		// list: an area's own list only gains a character that walked in from another area,
		// so a creature killed where it spawned is missing from it.
		private static bool HasLooseCorpse(NPCSettlement settlement)
		{
			List<Character> all = settlement.region?.charactersAtLocation;
			if (all == null)
			{
				return false;
			}
			bool graveyard = MassGraveBurial.HasGraveyard(settlement);
			for (int j = 0; j < all.Count; j++)
			{
				Character c = all[j];
				if (c != null && c.isDead && c.hasMarker && c.gridTileLocation != null && settlement.areas.Contains(c.gridTileLocation.area)
					&& !MassGraveBurial.IsExcluded(c.jobComponent, c, settlement)
					// A Cemetery or Cult Temple takes sapient dead (outsiders too, while there is no pit).
					&& (!graveyard || !c.race.IsSapient()))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Mirror of the private SettlementJobTriggerComponent.TryCreatePlaceBlueprintJob
		/// + TriggerPlaceBlueprint, for the Mass Grave type. Returns true if a job was queued.</summary>
		internal static bool TryQueueBlueprint(NPCSettlement settlement)
		{
			// Build it from whatever material this faction would build a Cemetery from.
			StructureSetting cemetery = settlement.owner.factionType.CreateStructureSettingForStructure(STRUCTURE_TYPE.CEMETERY, settlement);
			if (!cemetery.hasValue)
			{
				return false;
			}
			StructureSetting setting = new StructureSetting(MassGraveType, cemetery.resource);
			if (!LandmarkManager.Instance.CanPlaceStructureBlueprint(settlement.owner.factionType.type, settlement, setting,
				out LocationGridTile centerTile, out string prefabName, out int _, out LocationGridTile connectorTile))
			{
				return false;
			}
			GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.PLACE_BLUEPRINT, INTERACTION_TYPE.PLACE_BLUEPRINT,
				centerTile.tileObjectComponent.genericTileObject, settlement);
			job.AddOtherData(INTERACTION_TYPE.PLACE_BLUEPRINT, new object[3] { prefabName, connectorTile, setting });
			job.SetDoNotRecalculate(state: true);
			job.SetCanTakeThisJobChecker("CanTakePlaceBlueprintJob");
			settlement.AddToAvailableJobs(job);
			RuinarchPlus.Log?.Info($"{settlement.name} has dead nobody will bury: queued a Mass Grave blueprint ({prefabName}).");
			return true;
		}

		/// <summary>Build a Mass Grave instantly (no villagers, no materials) at a spot the
		/// game's own placement approves, using the real prefab so it renders like any built
		/// structure. For the debug menu and the automated test harness. A village has at most
		/// one Mass Grave: if it already has one (or one is being built), that one is returned
		/// and nothing new is placed. Returns null if the settlement has no valid spot.</summary>
		public static MassGrave InstantBuild(NPCSettlement settlement)
		{
			MassGrave existing = MassGrave.FindFor(settlement);
			if (existing != null || HasPendingFor(settlement))
			{
				return existing;
			}
			StructureSetting cemetery = settlement.owner.factionType.CreateStructureSettingForStructure(STRUCTURE_TYPE.CEMETERY, settlement);
			StructureSetting setting = new StructureSetting(MassGraveType, cemetery.hasValue ? cemetery.resource : RESOURCE.WOOD);
			if (!LandmarkManager.Instance.CanPlaceStructureBlueprint(settlement.owner.factionType.type, settlement, setting,
				out LocationGridTile centerTile, out string prefabName, out int _, out LocationGridTile _))
			{
				return null;
			}
			SwapArmed = true;
			try
			{
				return centerTile.tileObjectComponent.genericTileObject.InstantPlaceStructure(prefabName, settlement) as MassGrave;
			}
			finally
			{
				SwapArmed = false;
			}
		}

		internal static bool HasPendingFor(NPCSettlement settlement)
		{
			foreach (KeyValuePair<LocationStructureObject, NPCSettlement> entry in Pending.Values)
			{
				if (entry.Value == settlement)
				{
					return true;
				}
			}
			return false;
		}

		internal static void MarkPlaced(GenericTileObject tile, NPCSettlement settlement)
		{
			if (tile?.blueprintOnTile != null)
			{
				Pending[tile] = new KeyValuePair<LocationStructureObject, NPCSettlement>(tile.blueprintOnTile, settlement);
				RuinarchPlus.Log?.Info($"Mass Grave blueprint placed in {settlement?.name ?? "a village"}; villagers will now gather materials and build it.");
			}
		}

		/// <summary>True while <paramref name="tile"/> still holds the Mass Grave blueprint we placed.</summary>
		internal static bool IsMassGraveBlueprint(GenericTileObject tile)
		{
			return tile != null && Pending.TryGetValue(tile, out KeyValuePair<LocationStructureObject, NPCSettlement> entry)
				&& entry.Key != null && tile.blueprintOnTile == entry.Key;
		}

		internal static void Forget(GenericTileObject tile)
		{
			if (tile != null)
			{
				Pending.Remove(tile);
			}
		}

		// Drop entries whose blueprint expired, was cancelled or was replaced.
		private static void PruneStalePending()
		{
			List<GenericTileObject> stale = null;
			foreach (KeyValuePair<GenericTileObject, KeyValuePair<LocationStructureObject, NPCSettlement>> entry in Pending)
			{
				if (entry.Key == null || entry.Key.blueprintOnTile != entry.Value.Key)
				{
					(stale ?? (stale = new List<GenericTileObject>())).Add(entry.Key);
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
	}

	// A villager just placed a blueprint: remember it if it came from a Mass Grave job.
	[HarmonyPatch(typeof(PlaceBlueprint), nameof(PlaceBlueprint.PrePlaceSuccess))]
	internal static class MassGrave_BlueprintPlaced
	{
		private static void Postfix(ActualGoapNode goapNode)
		{
			try
			{
				if (goapNode.otherData == null || goapNode.otherData.Length < 3 || !(goapNode.otherData[2]?.obj is StructureSetting setting)
					|| setting.structureType != MassGraveConstruction.MassGraveType)
				{
					return;
				}
				if (goapNode.poiTarget is GenericTileObject tile)
				{
					MassGraveConstruction.MarkPlaced(tile, goapNode.actor?.homeSettlement);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Mass Grave blueprint tracking failed: " + e.Message);
			}
		}
	}

	// Construction finishing on a Mass Grave blueprint tile: arm the type swap for the
	// duration of the build, and always disarm afterwards.
	[HarmonyPatch(typeof(GenericTileObject), nameof(GenericTileObject.BuildBlueprintOnTile))]
	internal static class MassGrave_BuildBlueprint
	{
		private static void Prefix(GenericTileObject __instance, out bool __state)
		{
			__state = MassGraveConstruction.IsMassGraveBlueprint(__instance);
			MassGraveConstruction.SwapArmed = __state;
		}

		private static Exception Finalizer(GenericTileObject __instance, bool __state, Exception __exception)
		{
			MassGraveConstruction.SwapArmed = false;
			if (__state)
			{
				MassGraveConstruction.Forget(__instance);
			}
			return __exception;
		}
	}

	// Runs before the content framework's own CreateNewStructureAt prefix: while armed, the
	// Cemetery prefab's CEMETERY type becomes the Mass Grave type, which the framework's
	// factory then turns into a MassGrave instance.
	[HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.CreateNewStructureAt))]
	internal static class MassGrave_SwapBuiltType
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(ref STRUCTURE_TYPE structureType, BaseSettlement settlement)
		{
			if (MassGraveConstruction.SwapArmed && structureType == STRUCTURE_TYPE.CEMETERY)
			{
				MassGraveConstruction.SwapArmed = false;
				structureType = MassGraveConstruction.MassGraveType;
				RuinarchPlus.Log?.Info($"A Mass Grave was built in {settlement?.name ?? "a village"}.");
			}
		}
	}

	// The builder's action text ("... is building X") names the blueprint prefab's type,
	// which for our borrowed Cemetery prefab is CEMETERY. While BuildBlueprint writes that
	// text for a Mass Grave blueprint tile, the name lookup gets the Mass Grave type instead
	// (the content framework names it). Same shape as the build swap: no prefab is mutated.
	internal static class MassGrave_BuildLabel
	{
		[ThreadStatic]
		internal static bool Armed;

		internal static void Arm(ActualGoapNode goapNode)
		{
			Armed = goapNode?.poiTarget is GenericTileObject tile && MassGraveConstruction.IsMassGraveBlueprint(tile);
		}
	}

	[HarmonyPatch(typeof(BuildBlueprint), nameof(BuildBlueprint.AddFillersToLog))]
	internal static class MassGrave_BuildLabel_Fillers
	{
		private static void Prefix(ActualGoapNode goapNode) => MassGrave_BuildLabel.Arm(goapNode);

		private static Exception Finalizer(Exception __exception)
		{
			MassGrave_BuildLabel.Armed = false;
			return __exception;
		}
	}

	[HarmonyPatch(typeof(BuildBlueprint), nameof(BuildBlueprint.PreBuildSuccess))]
	internal static class MassGrave_BuildLabel_Start
	{
		private static void Prefix(ActualGoapNode goapNode) => MassGrave_BuildLabel.Arm(goapNode);

		private static Exception Finalizer(Exception __exception)
		{
			MassGrave_BuildLabel.Armed = false;
			return __exception;
		}
	}

	// Runs before the content framework's LocalizedStructureName prefix, which then names
	// the Mass Grave type.
	[HarmonyPatch(typeof(Extensions), nameof(Extensions.LocalizedStructureName), new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class MassGrave_BuildLabelName
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(ref STRUCTURE_TYPE structureType)
		{
			if (MassGrave_BuildLabel.Armed && structureType == STRUCTURE_TYPE.CEMETERY)
			{
				structureType = MassGraveConstruction.MassGraveType;
			}
		}
	}
}
