using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(CombatComponent), nameof(CombatComponent.FightOrFlight), new[] { typeof(IPointOfInterest), typeof(CombatReaction), typeof(ActualGoapNode), typeof(bool), typeof(bool) })]
	internal static class Fix_PetrifiedMonsterFlight
	{
		private static bool Prefix(IPointOfInterest target, CombatReaction combatReaction) =>
			!(combatReaction.reaction == COMBAT_REACTION.Flight && target is Character character && character.traitContainer.HasTrait("Stoned"));
	}
}
