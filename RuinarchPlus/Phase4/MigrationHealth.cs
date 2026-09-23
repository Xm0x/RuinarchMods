using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus.Phase4
{
	/// <summary>
	/// Migration follows the village's fortunes (config: <c>migrationHealthEnabled</c>).
	///
	/// Vanilla fills a village's migration meter every hour, and on every building and
	/// finished quest, as long as it has at least one resident. So a village of ten reduced to
	/// one by plague or raids keeps drawing settlers as if nothing happened. Now every natural
	/// gain is scaled by how the village is doing, and every death sets the meter back:
	/// - plague (an outbreak, or recent plague deaths) or a siege: nobody moves in;
	/// - more homes abandoned than lived in: nobody moves in;
	/// - otherwise each empty home beyond the two a growing village keeps spare: gains halve;
	/// - each unburied body lying in the village: gains halve;
	/// - each resident who dies pushes the meter back by a tenth.
	/// A village that recovers (plague over, dead buried, homes filled) draws settlers again.
	/// The player's Induce Migration skill bypasses the meter and is unaffected.
	///
	/// Everything is derived from state the game already saves, so saves are untouched.
	/// </summary>
	internal static class MigrationHealth
	{
		// Out of the game's 1500-point meter.
		internal const int DeathPenalty = 150;

		// The game only builds a new dwelling while fewer than two stand empty
		// (SettlementJobTriggerComponent.CheckForPlaceBlueprint), so up to two is normal.
		private const int SpareHomes = 2;

		/// <summary>0..1 factor applied to natural migration gains, and why it is below 1.</summary>
		internal static float Multiplier(NPCSettlement village, out string reason)
		{
			if (village.isPlagued || village.eventManager.HasActiveEvent(SETTLEMENT_EVENT.Plagued_Event))
			{
				reason = "plague";
				return 0f;
			}
			if (village.isUnderSiege)
			{
				reason = "under attack";
				return 0f;
			}
			int unoccupied = village.GetNumberOfUnoccupiedStructure(STRUCTURE_TYPE.DWELLING);
			int occupied = (village.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> homes) ? homes.Count : 0) - unoccupied;
			int emptyHomes = unoccupied - SpareHomes;
			if (emptyHomes > 0 && emptyHomes > occupied)
			{
				// Halving per home only approaches zero; a ghost town must draw nobody.
				reason = $"mostly abandoned ({unoccupied} of {unoccupied + occupied} homes empty)";
				return 0f;
			}
			float m = 1f;
			List<string> why = new List<string>(2);
			if (emptyHomes > 0)
			{
				m *= (float)Math.Pow(0.5, emptyHomes);
				why.Add(emptyHomes + (emptyHomes == 1 ? " abandoned home" : " abandoned homes"));
			}
			int dead = UnburiedDead(village);
			if (dead > 0)
			{
				m *= (float)Math.Pow(0.5, dead);
				why.Add(dead + (dead == 1 ? " unburied body" : " unburied bodies"));
			}
			reason = why.Count == 0 ? null : string.Join(", ", why);
			return m;
		}

		/// <summary>Unburied dead people lying on the village's own tiles.</summary>
		internal static int UnburiedDead(NPCSettlement village)
		{
			List<Character> here = village.region?.charactersAtLocation;
			if (here == null)
			{
				return 0;
			}
			int n = 0;
			for (int i = 0; i < here.Count; i++)
			{
				Character c = here[i];
				if (c != null && c.isDead && c.hasMarker && c.grave == null && c.race.IsSapient()
					&& c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(village))
				{
					n++;
				}
			}
			return n;
		}
	}

	// Every natural gain (hourly, new building, finished quest) funnels through here.
	[HarmonyPatch(typeof(SettlementVillageMigrationComponent), nameof(SettlementVillageMigrationComponent.IncreaseVillageMigrationMeter))]
	internal static class MigrationHealth_Gain
	{
		private static void Prefix(SettlementVillageMigrationComponent __instance, ref int amount)
		{
			if (!RuinarchPlusConfig.Current.migrationHealthEnabled || amount <= 0 || __instance.owner == null)
			{
				return;
			}
			try
			{
				amount = (int)(amount * MigrationHealth.Multiplier(__instance.owner, out _));
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Migration health check failed: " + e.Message);
			}
		}
	}

	// A resident's death sets the village's migration meter back.
	[HarmonyPatch(typeof(Character), nameof(Character.Death))]
	internal static class MigrationHealth_Death
	{
		private static void Prefix(Character __instance, out NPCSettlement __state)
		{
			__state = !__instance.isDead && __instance.race.IsSapient() ? __instance.homeSettlement : null;
		}

		private static void Postfix(Character __instance, NPCSettlement __state)
		{
			if (__state == null || !__instance.isDead || !RuinarchPlusConfig.Current.migrationHealthEnabled)
			{
				return;
			}
			try
			{
				if (__state.locationType == LOCATION_TYPE.VILLAGE)
				{
					__state.migrationComponent.ReduceVillageMigrationMeter(MigrationHealth.DeathPenalty);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Migration death penalty failed: " + e.Message);
			}
		}
	}

	// The migration meter's tooltip says why settlers are staying away.
	[HarmonyPatch(typeof(SettlementVillageMigrationComponent), nameof(SettlementVillageMigrationComponent.GetHoverTextOfMigrationMeter))]
	internal static class MigrationHealth_Tooltip
	{
		private static void Postfix(SettlementVillageMigrationComponent __instance, ref string __result)
		{
			if (!RuinarchPlusConfig.Current.migrationHealthEnabled || __instance.owner == null)
			{
				return;
			}
			try
			{
				float m = MigrationHealth.Multiplier(__instance.owner, out string reason);
				if (reason != null)
				{
					__result += m <= 0f
						? $"\nSettlers stay away: {reason}."
						: $"\nFewer settlers ({m:P0}): {reason}.";
				}
			}
			catch (Exception)
			{
				// Tooltip text only; never break the UI over it.
			}
		}
	}
}
