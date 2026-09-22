using HarmonyLib;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus
{
	// BUG: Brainwash could be queued on Stalker-class prisoners but always failed.
	// Stalker immunity was only enforced in PrisonCell.WasBrainwashSuccessful (via
	// classComponent.IsStalkerCannotBeTurned()), NOT in IsValidBrainwashTarget, so the
	// action was offered as a dead-end. Fix: reject Stalkers at target validation too.
	[HarmonyPatch(typeof(PrisonCell), nameof(PrisonCell.IsValidBrainwashTarget))]
	internal static class Fix_StalkerBrainwash
	{
		private static void Postfix(Character p_character, ref bool __result)
		{
			if (__result && p_character != null && p_character.classComponent != null
				&& p_character.classComponent.IsStalkerCannotBeTurned())
			{
				__result = false;
			}
		}
	}
}
