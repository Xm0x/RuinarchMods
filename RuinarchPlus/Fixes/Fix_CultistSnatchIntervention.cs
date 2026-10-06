using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(ReactionComponent), "NonHostileAliveVillagerNotHomeOrFactionmateReactionToCharacter")]
	internal static class Fix_CultistSnatchIntervention
	{
		private static bool Prefix(Character actor, Character targetCharacter) =>
			!(targetCharacter.limiterComponent.isTargetedByDemonicSnatch && actor.isAlliedWithPlayer && actor.traitContainer.IsReligiousCultist(out _));
	}
}
