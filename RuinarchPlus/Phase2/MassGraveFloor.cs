using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinarchPlus.Phase2
{
	// The Mass Grave borrows the Cemetery prefab (footprint, walls, pathing) and is bare dirt:
	// no art of its own. The prefab carries props (gravestones, decorations) that the game
	// turns into real map objects when the structure is built; build the pit without them.
	// Tombstones of the bodies laid in it are placed later by the burials themselves.
	[HarmonyPatch(typeof(LocationStructureObject), nameof(LocationStructureObject.OnBuiltStructureObjectPlaced))]
	internal static class MassGrave_NoCemeteryProps
	{
		private static void Prefix(LocationStructureObject __instance, LocationStructure structure, ref TILE_OBJECT_TYPE[] objectTypesToNotBuild)
		{
			if (!(structure is MassGrave))
			{
				return;
			}
			try
			{
				StructureTemplateObjectData[] props = AccessTools.Method(typeof(LocationStructureObject), "GetPreplacedObjects")
					?.Invoke(__instance, null) as StructureTemplateObjectData[];
				if (props == null || props.Length == 0)
				{
					return;
				}
				List<TILE_OBJECT_TYPE> skip = new List<TILE_OBJECT_TYPE>(objectTypesToNotBuild ?? new TILE_OBJECT_TYPE[0]);
				foreach (StructureTemplateObjectData p in props)
				{
					// STRUCTURE_TILE_OBJECT is the building's own core object, not decoration:
					// without it the pit does not count as standing.
					if (p.tileObjectType != TILE_OBJECT_TYPE.STRUCTURE_TILE_OBJECT && !skip.Contains(p.tileObjectType))
					{
						skip.Add(p.tileObjectType);
					}
				}
				objectTypesToNotBuild = skip.ToArray();
				RuinarchPlus.Log?.Info("Mass Grave built without the Cemetery's props: " + string.Join(", ",
					props.Where(p => p.tileObjectType != TILE_OBJECT_TYPE.STRUCTURE_TILE_OBJECT).GroupBy(p => p.tileObjectType).Select(g => $"{g.Key} x{g.Count()}")));
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Could not strip Cemetery props from the Mass Grave: " + e.Message);
			}
		}
	}

	// The Cemetery floor is paved in a cross; a pit is bare earth. Every floor tile gets the
	// prefab's own dirt tile (the one on its corners), on build and again on load.
	internal static class MassGraveFloor
	{
		private static readonly AccessTools.FieldRef<LocationStructureObject, Tilemap> PrefabGround =
			AccessTools.FieldRefAccess<LocationStructureObject, Tilemap>("_groundTileMap");

		internal static void MakeDirt(LocationStructureObject obj, LocationStructure structure)
		{
			if (!(structure is MassGrave) || structure.tiles == null || structure.tiles.Count == 0)
			{
				return;
			}
			try
			{
				Tilemap ground = PrefabGround(obj);
				LocationGridTile corner = structure.tiles.OrderBy(t => t.localPlace.x).ThenBy(t => t.localPlace.y).First();
				TileBase dirt = ground != null ? ground.GetTile(ground.WorldToCell(corner.worldLocation)) : null;
				if (dirt == null)
				{
					return;
				}
				foreach (LocationGridTile tile in structure.tiles)
				{
					tile.SetGroundTilemapVisual(dirt);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Mass Grave dirt floor could not be applied: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(LocationStructureObject), nameof(LocationStructureObject.OnBuiltStructureObjectPlaced))]
	internal static class MassGrave_DirtFloorOnBuild
	{
		private static void Postfix(LocationStructureObject __instance, LocationStructure structure)
		{
			MassGraveFloor.MakeDirt(__instance, structure);
		}
	}

	[HarmonyPatch(typeof(LocationStructureObject), nameof(LocationStructureObject.OnLoadStructureObjectPlaced))]
	internal static class MassGrave_DirtFloorOnLoad
	{
		private static void Postfix(LocationStructureObject __instance, LocationStructure structure)
		{
			MassGraveFloor.MakeDirt(__instance, structure);
		}
	}
}
