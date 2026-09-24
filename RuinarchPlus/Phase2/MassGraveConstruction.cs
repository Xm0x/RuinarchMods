using System;
using System.Collections.Generic;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// Villagers BUILD the Mass Grave themselves, through the game's own construction
	/// pipeline (config: <c>massGraveBurialEnabled</c>; the shared machinery is
	/// <see cref="ModBuildings"/>).
	///
	/// Hourly, a village with a body lying in it that nothing else will take (any body when
	/// it has no Cemetery or Cult Temple; a creature's carcass even when it has one, since
	/// the game never buries animals there), and no Mass Grave or pending Mass Grave
	/// blueprint, queues a vanilla <c>PLACE_BLUEPRINT</c> job for a Mass Grave, built from
	/// the Cemetery's prefabs and materials.
	/// </summary>
	internal static class MassGraveConstruction
	{
		internal static ModBuilding Building;

		internal static STRUCTURE_TYPE MassGraveType => ModContent.StructureTypeFor(MassGraveFeature.Id);

		/// <summary>Hourly: queue a Mass Grave blueprint in every village that needs one.</summary>
		internal static void HourlyCheck()
		{
			if (!MassGraveBurial.Enabled || GridMap.Instance?.mainRegion?.settlementsInRegion == null)
			{
				return;
			}
			ModBuildings.PruneStale();
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

		internal static bool TryQueueBlueprint(NPCSettlement settlement)
		{
			string prefabName = ModBuildings.QueueBlueprint(settlement, Building, STRUCTURE_TYPE.CEMETERY);
			if (prefabName == null)
			{
				return false;
			}
			RuinarchPlus.Log?.Info($"{settlement.name} has dead nobody will bury: queued a Mass Grave blueprint ({prefabName}).");
			return true;
		}

		/// <summary>Build a Mass Grave instantly (debug menu, test harness). A village has at
		/// most one: if it already has one (or one is being built), that one is returned and
		/// nothing new is placed. Null if the settlement has no valid spot.</summary>
		public static MassGrave InstantBuild(NPCSettlement settlement)
		{
			MassGrave existing = MassGrave.FindFor(settlement);
			if (existing != null || HasPendingFor(settlement))
			{
				return existing;
			}
			return ModBuildings.InstantBuild(settlement, Building, STRUCTURE_TYPE.CEMETERY) as MassGrave;
		}

		internal static bool HasPendingFor(NPCSettlement settlement)
		{
			return ModBuildings.HasPendingFor(settlement, Building);
		}
	}
}
