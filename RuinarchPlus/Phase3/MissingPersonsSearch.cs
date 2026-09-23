using System;
using HarmonyLib;
using Inner_Maps;

namespace RuinarchPlus.Phase3
{
	// The search party is the game's own RescuePartyQuest (RescuePartyQuest.cs,
	// RescueBehaviour.cs). These patches make it search where the person was last seen.

	// A search, and a witness rescue too, heads for where the person was last seen, not
	// where they are now (vanilla: the target's live structure or area).
	[HarmonyPatch(typeof(RescuePartyQuest), nameof(RescuePartyQuest.GetTargetDestination))]
	internal static class Missing_Destination
	{
		private static void Postfix(RescuePartyQuest __instance, ref IPartyTargetDestination __result)
		{
			if (!MissingPersons.Enabled)
			{
				return;
			}
			try
			{
				IPartyTargetDestination lastSeen = MissingPersons.Destination(MissingPersons.Get(__instance.targetCharacter));
				if (lastSeen != null)
				{
					__result = lastSeen;
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (destination) failed, using vanilla: " + e.Message);
			}
		}
	}

	// A search stays on the board while the person is missing, whatever their condition
	// (vanilla: only while the target is Restrained).
	[HarmonyPatch(typeof(RescuePartyQuest), nameof(RescuePartyQuest.IsStillEligibleFor))]
	internal static class Missing_Eligible
	{
		private static void Postfix(RescuePartyQuest __instance, ref bool __result)
		{
			if (!__result && MissingPersons.Enabled && MissingPersons.IsSearch(__instance)
				&& MissingPersons.Get(__instance.targetCharacter).State != MissingPersons.MissingState.Seen)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(RescuePartyQuest), nameof(RescuePartyQuest.GetPartyQuestName))]
	internal static class Missing_Name
	{
		private static void Postfix(RescuePartyQuest __instance, ref string __result)
		{
			if (MissingPersons.Enabled && MissingPersons.IsSearch(__instance))
			{
				__result = "Search for " + __instance.targetCharacter.name;
			}
		}
	}

	// The base game's omniscient roll (any Restrained or Paralyzed resident outside the
	// village, no witness needed) is replaced by the missing-person searches.
	[HarmonyPatch(typeof(SettlementPartyComponent), "TryCreateRescueQuest")]
	internal static class Missing_NoOmniscientRescue
	{
		private static bool Prefix()
		{
			return !MissingPersons.Enabled;
		}
	}

	// A witness posting a rescue is looking at the target: that is where they were seen.
	[HarmonyPatch(typeof(PartyQuestBoard), nameof(PartyQuestBoard.CreateRescuePartyQuest))]
	internal static class Missing_WitnessSaw
	{
		private static void Prefix(Character questCreator, Character targetCharacter)
		{
			if (!MissingPersons.Enabled || questCreator == null || targetCharacter?.gridTileLocation == null)
			{
				return;
			}
			try
			{
				MissingPersons.Saw(targetCharacter, targetCharacter.gridTileLocation);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (witness) failed: " + e.Message);
			}
		}
	}

	// Every way a search ends passes through EndQuest while the party is still assigned:
	// found (a member has the target or their grave in sight) or failed.
	[HarmonyPatch(typeof(PartyQuest), nameof(PartyQuest.EndQuest))]
	internal static class Missing_SearchEnded
	{
		private static void Prefix(PartyQuest __instance, string reason)
		{
			if (!MissingPersons.Enabled || !(__instance is RescuePartyQuest quest) || !MissingPersons.IsSearch(quest))
			{
				return;
			}
			try
			{
				MissingPersons.OnSearchEnded(quest, reason);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (search ended) failed: " + e.Message);
			}
		}
	}

	// On site, a searcher who does not see the target sweeps the area around the last-seen
	// spot instead of walking to where the target is now (vanilla CreateGoToJob(target)).
	// Once the target is in sight the vanilla behaviour runs: free them, or find them dead
	// or safe.
	[HarmonyPatch(typeof(RescueBehaviour), nameof(RescueBehaviour.TryDoBehaviour))]
	internal static class Missing_Sweep
	{
		private static bool Prefix(Character character, ref JobQueueItem producedJob, ref bool __result)
		{
			if (!MissingPersons.Enabled || !character.partyComponent.hasParty)
			{
				return true;
			}
			Party party = character.partyComponent.currentParty;
			if (party == null || !party.isActive || party.partyState != PARTY_STATE.Working
				|| !(party.currentQuest is RescuePartyQuest quest) || !MissingPersons.IsSearch(quest) || !character.hasMarker)
			{
				return true;
			}
			try
			{
				Character target = quest.targetCharacter;
				if (target.hasMarker && character.marker.IsPOIInVision(target))
				{
					return true;
				}
				producedJob = null;
				__result = true;
				if (target.grave != null && character.marker.IsPOIInVision(target.grave))
				{
					quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Target_Dead"));
					return false;
				}
				MissingPersons.Record r = MissingPersons.Get(target);
				long now = MissingPersons.Now;
				LocationGridTile next = MissingPersons.NextSweepTile(character, r);
				// The sweep's clock starts once a searcher reaches the last-seen spot (or finds
				// no way to it), not on entering its area: the walk there is not the search.
				if (r.SweepStartedTick < 0 && r.ReachedLastSeen.Count > 0)
				{
					r.SweepStartedTick = now;
				}
				if (r.SweepStartedTick >= 0 && now - r.SweepStartedTick >= (long)RuinarchPlusConfig.Current.searchSweepHours * GameManager.ticksPerHour)
				{
					quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Target_Nowhere"));
					return false;
				}
				__result = next != null && character.jobComponent.CreateGoToSpecificTileJob(next, out producedJob);
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (sweep) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}
}
