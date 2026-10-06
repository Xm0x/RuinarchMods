using HarmonyLib;
using Factions.Faction_Succession;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(Recruit), "AreRequirementsSatisfied")]
	internal static class Fix_PlayerMonsterRecruitment
	{
		private static bool Prefix(IPointOfInterest poiTarget, ref bool __result)
		{
			if (poiTarget.factionOwner?.isPlayerFaction != true) return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(FactionSuccession), "CanBeCandidateForSuccession")]
	internal static class Fix_MonsterFactionSuccession
	{
		private static bool Prefix(Character character, Faction faction, ref bool __result)
		{
			if (character.isNormalCharacter && character.race.IsSapient()) return true;
			for (int i = 0; i < faction.characters.Count; i++)
			{
				var member = faction.characters[i];
				if (!member.isDead && member.isNormalCharacter && member.race.IsSapient() && (member.IsAtHome() || member.partyComponent.isMemberThatJoinedQuest))
				{
					__result = false;
					return false;
				}
			}
			return true;
		}
	}
}
