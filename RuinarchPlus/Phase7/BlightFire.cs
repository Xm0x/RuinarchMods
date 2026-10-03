using System;
using HarmonyLib;
using Inner_Maps;
using Traits;
using System.Collections.Generic;
using UtilityScripts;

namespace RuinarchPlus.Phase7
{
	// Removing Burning also runs when its fuel is destroyed or a saved fire ends.
	// Capture the tile before the native removal clears it. No transient tracking is
	// needed: TileObject.previousTile preserves the location of destroyed furniture.
	[HarmonyPatch(typeof(Burning), nameof(Burning.OnRemoveTrait))]
	internal static class Blight_FireOut
	{
		private static void Prefix(ITraitable removedFrom, out LocationGridTile __state)
		{
			__state = removedFrom is TileObject o ? o.gridTileLocation ?? o.previousTile : null;
		}

		private static void Postfix(ITraitable removedFrom, LocationGridTile __state)
		{
			if (!BlightRules.Enabled || !RuinarchPlusConfig.Current.blightFireCleanses || __state == null) return;
			List<LocationGridTile> footprint = RuinarchListPool<LocationGridTile>.Claim();
			try
			{
				// Like the game, use the footprint only for multi-tile objects: items such as an
				// Antidote or a Herb Plant have no size set (0x0).
				if (removedFrom is TileObject o && TileObjectDB.TryGetTileObjectData(o.tileObjectType, out var data)
					&& (data.occupiedSize.X > 1 || data.occupiedSize.Y > 1))
					__state.parentMap.PopulateTiles(footprint, data.occupiedSize, __state);
				else
					footprint.Add(__state);
				foreach (LocationGridTile t in footprint)
				{
					if (t.structure == null || t.structure.structureType.IsPlayerStructure() || !t.corruptionComponent.isCorrupted) continue;
					t.corruptionComponent.UncorruptTile();
					RuinarchPlus.Log?.Info($"Fire burnt the blight off {t.localPlace}.");
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Blight fire (out) failed: " + e.Message);
			}
			finally { RuinarchListPool<LocationGridTile>.Release(footprint); }
		}
	}
}
