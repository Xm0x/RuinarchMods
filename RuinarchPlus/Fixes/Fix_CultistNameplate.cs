using HarmonyLib;
using Traits;

namespace RuinarchPlus
{
	// The trait-removed signal arrives after removal; updating only animation and
	// the action icon leaves the cached cultist icon in the map-name label.
	[HarmonyPatch(typeof(CharacterMarker), "OnCharacterLostTrait")]
	internal static class Fix_CultistNameplate
	{
		private static void Postfix(CharacterMarker __instance, Character character, Trait trait)
		{
			if (__instance.character == character && trait is DemonCultist) __instance.UpdateName();
		}
	}
}
