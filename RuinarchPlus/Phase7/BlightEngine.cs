using System;
using System.Collections.Generic;
using System.Linq;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;

namespace RuinarchPlus.Phase7
{
	/// <summary>
	/// Which tiles the Blight may take (config: <c>blightEnabled</c>). The game's Corrupt Tile
	/// only takes wilderness away from villages; the blight also takes a village's open
	/// ground (streets, yards, fields), never the inside of a building, walls, water,
	/// mountains, demonic buildings or the Portal and the tiles around it.
	/// </summary>
	internal static class BlightRules
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.blightEnabled;

		/// <summary>The village whose ground this is, or null.</summary>
		internal static NPCSettlement VillageOf(LocationGridTile t)
		{
			return t != null && t.IsPartOfSettlement(out BaseSettlement s) ? s as NPCSettlement : null;
		}

		/// <summary>Corrupted ground that belongs to a village: blight, not a demonic foothold.</summary>
		internal static bool IsVillageBlight(LocationGridTile t)
		{
			return t != null && t.corruptionComponent.isCorrupted && VillageOf(t) != null;
		}

		internal static bool CanTake(LocationGridTile t)
		{
			if (t?.structure == null || t.corruptionComponent.isCorrupted || t.corruptionComponent.isCurrentlyBeingCorrupted || t.hasBlueprint)
			{
				return false;
			}
			if (t.IsWater() || t.elevationType == ELEVATION.MOUNTAIN || t.tileType == LocationGridTile.Tile_Type.Wall)
			{
				return false;
			}
			LocationStructure s = t.structure;
			STRUCTURE_TYPE type = s.structureType;
			if (s.isInterior || type == STRUCTURE_TYPE.THE_PORTAL || type == STRUCTURE_TYPE.OCEAN || type.IsPlayerStructure())
			{
				return false;
			}
			if (type != STRUCTURE_TYPE.WILDERNESS && !type.IsOpenSpace())
			{
				return false;
			}
			if (t.HasNeighbourStructure(STRUCTURE_TYPE.THE_PORTAL))
			{
				return false;
			}
			return VillageOf(t) == null || RuinarchPlusConfig.Current.blightInVillages;
		}
	}

	/// <summary>
	/// Grows the blight. Tiles are corrupted at once (the game's 5-tick smoke timer is not
	/// saved, so a save in between would lose the tile). In the wilderness the game's own
	/// corruption runs (trees change, small objects go); on village ground only the ground
	/// changes and crops wither, and the edges and minimap of village tiles are redrawn once
	/// per hour in <see cref="Flush"/>.
	/// </summary>
	internal static class BlightEngine
	{
		private static readonly HashSet<LocationGridTile> Dirty = new HashSet<LocationGridTile>();
		private static readonly Random Rng = new Random();

		/// <summary>Tiles the blight may still take this hour, across the whole map.</summary>
		internal static int HourBudget { get; private set; }

		internal static void StartHour()
		{
			HourBudget = Math.Max(0, RuinarchPlusConfig.Current.blightTilesPerHour);
		}

		internal static bool Corrupt(LocationGridTile t)
		{
			if (!BlightRules.CanTake(t))
			{
				return false;
			}
			if (BlightRules.VillageOf(t) == null)
			{
				t.corruptionComponent.CorruptTile();
			}
			else
			{
				if (t.tileObjectComponent.objHere is Crops crop)
				{
					t.structure.RemovePOI(crop);
				}
				PlayerManager.Instance.player.playerSettlement.AddCorruptedTile(t);
				t.SetGroundTilemapVisual(InnerMapManager.Instance.assetManager.corruptedTile);
				Dirty.Add(t);
			}
			return t.corruptionComponent.isCorrupted;
		}

		/// <summary>Takes up to <paramref name="count"/> tiles of <paramref name="frontier"/>
		/// (within the hour's budget), those with most corrupted neighbours first so the blight
		/// creeps in tongues rather than rings.</summary>
		internal static List<LocationGridTile> Grow(IEnumerable<LocationGridTile> frontier, int count)
		{
			List<LocationGridTile> done = new List<LocationGridTile>();
			int want = Math.Min(count, HourBudget);
			if (want <= 0)
			{
				return done;
			}
			List<LocationGridTile> picks = frontier.Where(BlightRules.CanTake)
				.Select(t => new { t, n = t.neighbourList.Count(x => x.corruptionComponent.isCorrupted), r = Rng.Next() })
				.OrderByDescending(x => x.n).ThenBy(x => x.r).Take(want).Select(x => x.t).ToList();
			foreach (LocationGridTile t in picks)
			{
				if (HourBudget > 0 && Corrupt(t))
				{
					done.Add(t);
					HourBudget--;
				}
			}
			return done;
		}

		/// <summary>Redraws the edges and minimap of village tiles corrupted since the last call.</summary>
		internal static void Flush()
		{
			if (Dirty.Count == 0)
			{
				return;
			}
			HashSet<LocationGridTile> edges = new HashSet<LocationGridTile>();
			foreach (LocationGridTile t in Dirty)
			{
				edges.Add(t);
				edges.UnionWith(t.neighbourList);
			}
			foreach (LocationGridTile t in edges)
			{
				t.CreateSeamlessEdgesForTile(t.parentMap);
			}
			foreach (LocationGridTile t in Dirty)
			{
				t.UpdateMinimapVisual(t.structure);
			}
			Dirty.Clear();
		}
	}
}
