using HarmonyLib;
using Traits;

namespace RuinarchPlus
{
	// C09-43: ApplyTraitEffects suppresses all three needs for Recuperating, but
	// the native removal branch omits it. Release only this trait's contribution.
	[HarmonyPatch(typeof(CharacterTraitProcessor), nameof(CharacterTraitProcessor.UnapplyTraitEffects))]
	internal static class Fix_RecuperatingNeeds
	{
		private static void Postfix(Character character, Trait trait)
		{
			if (trait.name != "Recuperating") return;
			character.needsComponent.AdjustDoNotGetHungry(-1);
			character.needsComponent.AdjustDoNotGetTired(-1);
			character.needsComponent.AdjustDoNotGetBored(-1);
		}
	}
}
