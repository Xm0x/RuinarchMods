using System;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;

namespace RuinarchPlus.Phase3
{
	// Beyond counterattacks, the game decides three more things about one particular player
	// building with the faction-wide isAwareOfPlayer: once any villager has reported any of
	// the player's buildings, their faction acts on every other one as if it had seen it.
	// These patches ask Knowledge.KnowsOf instead: the village posting the quest, or the one
	// asking, must know that building.

	internal static class KnowledgeTargets
	{
		/// <summary>The player building <paramref name="c"/> is in, or null.</summary>
		internal static LocationStructure PlayerStructureOf(Character c)
		{
			LocationStructure s = c?.currentStructure;
			return s != null && s.structureType.IsPlayerStructure() ? s : null;
		}

		/// <summary>
		/// A party member looking at their target inside a player building now knows that
		/// building (their party acts on it; their village learns it once they are home).
		/// True if the party may act on it.
		/// </summary>
		internal static bool MayActOn(Character member, Character target, LocationStructure held)
		{
			Faction f = member.faction;
			if (f != null && f.isAwareOfPlayer && member.hasMarker && target.hasMarker && member.marker.IsPOIInVision(target))
			{
				Knowledge.Witness(member, held);
			}
			return Knowledge.KnowsOf(f, member.homeSettlement, member, held) || Knowledge.PartyKnowledge(member).Contains(held);
		}
	}

	// A captive held in a player building: an aware faction sends a Demon Rescue straight to
	// that building (PartyQuestBoard.CreateRescuePartyQuest). Unless it knows the building, no
	// rescue: the captive is a missing person, searched for where they were last seen
	// (MissingPersons), and a party that sees them inside the building learns it.
	[HarmonyPatch(typeof(PartyQuestBoard), nameof(PartyQuestBoard.CreateRescuePartyQuest))]
	internal static class Knowledge_RescueTarget
	{
		private static bool Prefix(PartyQuestBoard __instance, Character questCreator, BaseSettlement madeInLocation, Character targetCharacter)
		{
			try
			{
				Faction owner = __instance.owner;
				LocationStructure held = KnowledgeTargets.PlayerStructureOf(targetCharacter);
				if (!Knowledge.Enabled || held == null || owner == null || !owner.isMajorOrBandits || !owner.isAwareOfPlayer
					|| owner.factionType.type == FACTION_TYPE.Demons || owner.factionType.type == FACTION_TYPE.Demon_Cult
					|| Knowledge.KnowsOf(owner, madeInLocation, questCreator, held))
				{
					return true;
				}
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (rescue target) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}

	// The game's "search for the demonic area" (an unaware faction's answer to a captive held
	// by the demons) is no search: the job's target is the Portal itself
	// (SettlementJobTriggerComponent.CreateSearchForDemonicAreaJob), so the searcher walks
	// straight to it. With the fog of war on it is never created; the captive is searched for
	// as a missing person instead. Jobs already on a village's board (older saves) are left
	// untaken.
	[HarmonyPatch(typeof(SettlementJobTriggerComponent), nameof(SettlementJobTriggerComponent.CreateSearchForDemonicAreaJob))]
	internal static class Knowledge_NoHomingSearch
	{
		private static bool Prefix() => !Knowledge.Enabled;
	}

	[HarmonyPatch(typeof(Goap.Job_Checkers.CanTakeSearchForDemonicArea), nameof(Goap.Job_Checkers.CanTakeSearchForDemonicArea.CanTakeJob))]
	internal static class Knowledge_NoHomingSearchTaken
	{
		private static void Postfix(ref bool __result)
		{
			if (Knowledge.Enabled)
			{
				__result = false;
			}
		}
	}

	// A wanted criminal hiding in a player building: an aware faction posts a bounty hunt on
	// them (SettlementPartyComponent.TryCreateBountyHuntQuest). Only if it knows the building.
	[HarmonyPatch(typeof(PartyQuestBoard), nameof(PartyQuestBoard.CreateBountyHuntPartyQuest))]
	internal static class Knowledge_BountyTarget
	{
		private static bool Prefix(PartyQuestBoard __instance, Character questCreator, BaseSettlement madeInLocation, Character targetCharacter)
		{
			try
			{
				LocationStructure held = KnowledgeTargets.PlayerStructureOf(targetCharacter);
				return !Knowledge.Enabled || held == null || Knowledge.KnowsOf(__instance.owner, madeInLocation, questCreator, held);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (bounty target) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}

	// On site, a rescue or bounty party whose target is inside a player building attacks that
	// building if the faction is aware (RescueBehaviour, BountyHuntBehaviour), else gives up
	// ("Target_Nowhere"). Now: they attack it if they know it, and seeing their target inside
	// is how they learn it. Missing-person searches sweep on their own (MissingPersonsSearch.cs).
	[HarmonyPatch(typeof(RescueBehaviour), nameof(RescueBehaviour.TryDoBehaviour))]
	internal static class Knowledge_RescueOnSite
	{
		private static bool Prefix(Character character, ref JobQueueItem producedJob, ref bool __result)
		{
			return KnowledgeOnSite.Run(character, ref producedJob, ref __result, q => (q as RescuePartyQuest)?.targetCharacter, skipSearches: true);
		}
	}

	[HarmonyPatch(typeof(BountyHuntBehaviour), nameof(BountyHuntBehaviour.TryDoBehaviour))]
	internal static class Knowledge_BountyOnSite
	{
		private static bool Prefix(Character character, ref JobQueueItem producedJob, ref bool __result)
		{
			return KnowledgeOnSite.Run(character, ref producedJob, ref __result, q => (q as BountyHuntPartyQuest)?.targetCharacter, skipSearches: false);
		}
	}

	internal static class KnowledgeOnSite
	{
		internal static bool Run(Character character, ref JobQueueItem producedJob, ref bool __result, Func<PartyQuest, Character> targetOf, bool skipSearches)
		{
			if (!Knowledge.Enabled || !character.partyComponent.hasParty)
			{
				return true;
			}
			try
			{
				Party party = character.partyComponent.currentParty;
				PartyQuest quest = party?.currentQuest;
				Character target = quest == null ? null : targetOf(quest);
				LocationStructure held = KnowledgeTargets.PlayerStructureOf(target);
				if (!party.isActive || party.partyState != PARTY_STATE.Working || held == null || character.faction == null || !character.faction.isAwareOfPlayer
					|| (skipSearches && MissingPersons.IsSearch(quest)) || KnowledgeTargets.MayActOn(character, target, held))
				{
					return true;
				}
				quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Target_Nowhere"));
				producedJob = null;
				__result = true;
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (on site) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}
}
