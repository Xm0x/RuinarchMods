using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Locations.Settlements;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// Closed borders (config: <c>closedBordersEnabled</c>, needs the curfew): a village under
	/// plague curfew (<see cref="Curfew"/>) turns away visitors.
	/// - Free-time visits: the village is left out of the villages a character may visit
	///   (<c>Region.PopulateValidVillagesToVisit</c>, used by villagers and bandits).
	/// - Visitors already there, or on the way, give up the visit and go home: when their visit
	///   behaviour runs (<c>VisitVillageBehaviour.TryDoBehaviour</c>) and, since a visitor busy
	///   with a job may not get to it for hours, in an hourly sweep.
	/// - Visiting a friend who lives there (<c>CharacterBehaviour.GetCharacterToVisitWeights</c>).
	/// - Ruinarch+ traders don't go there either (Phase5/Traders.cs).
	/// Its own residents come and go as the curfew allows. Raids, rescues and bounty hunts are
	/// not visits: a curfew doesn't stop them.
	/// </summary>
	internal static class ClosedBorders
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.closedBordersEnabled && Curfew.Enabled;

		/// <summary>Is <paramref name="village"/> closed to <paramref name="visitor"/>?</summary>
		internal static bool TurnsAway(NPCSettlement village, Character visitor)
		{
			return Enabled && village != null && visitor != null && visitor.homeSettlement != village && Curfew.IsUnderCurfew(village);
		}

		// Visits already turned away, so the log names each one once.
		private static readonly HashSet<string> Told = new HashSet<string>();

		internal static void TurnAway(Character visitor, NPCSettlement village)
		{
			visitor.behaviourComponent.ClearOutVisitVillageBehaviour();
			if (visitor.homeSettlement != null && visitor.currentSettlement == village)
			{
				visitor.jobComponent.PlanReturnHome(JOB_TYPE.IDLE_RETURN_HOME);
			}
			if (Told.Count > 512)
			{
				Told.Clear();
			}
			if (Told.Add(visitor.persistentID + "|" + village.persistentID))
			{
				Curfew.Note("{0} was turned away from {1}: it is under curfew.", visitor, village);
			}
		}

		/// <summary>Hourly: everyone visiting (or on the way to) a village under curfew is turned away.</summary>
		internal static void HourlyCheck()
		{
			List<Character> here = GridMap.Instance?.mainRegion?.charactersAtLocation;
			if (!Enabled || here == null)
			{
				return;
			}
			foreach (Character c in here.ToList())
			{
				if (c != null && !c.isDead && c.behaviourComponent.targetVisitVillage is NPCSettlement v && TurnsAway(v, c))
				{
					TurnAway(c, v);
				}
			}
		}
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class ClosedBorders_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			if (!ClosedBorders.Enabled || __instance.Today().tick % 20 != 0)
			{
				return;
			}
			try
			{
				ClosedBorders.HourlyCheck();
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Closed borders hourly check failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(Region), nameof(Region.PopulateValidVillagesToVisit))]
	internal static class ClosedBorders_Candidates
	{
		private static void Postfix(Character p_character, List<NPCSettlement> settlements)
		{
			if (!ClosedBorders.Enabled || settlements == null)
			{
				return;
			}
			try
			{
				settlements.RemoveAll(s => ClosedBorders.TurnsAway(s, p_character));
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Closed borders (candidates) failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(VisitVillageBehaviour), nameof(VisitVillageBehaviour.TryDoBehaviour))]
	internal static class ClosedBorders_Visit
	{
		private static bool Prefix(Character character, out JobQueueItem producedJob, ref bool __result)
		{
			producedJob = null;
			try
			{
				NPCSettlement village = character.behaviourComponent.targetVisitVillage;
				if (!ClosedBorders.TurnsAway(village, character))
				{
					return true;
				}
				ClosedBorders.TurnAway(character, village);
				__result = false;
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Closed borders (visit) failed: " + e.Message);
				return true;
			}
		}
	}

	[HarmonyPatch(typeof(CharacterBehaviour), "GetCharacterToVisitWeights")]
	internal static class ClosedBorders_Friends
	{
		private static void Postfix(Character actor, WeightedDictionary<Character> __result)
		{
			if (!ClosedBorders.Enabled || __result == null)
			{
				return;
			}
			try
			{
				foreach (Character friend in __result.dictionary.Keys.ToList())
				{
					if (friend.homeSettlement is NPCSettlement home && ClosedBorders.TurnsAway(home, actor))
					{
						__result.dictionary.Remove(friend);
					}
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Closed borders (friends) failed: " + e.Message);
			}
		}
	}
}
