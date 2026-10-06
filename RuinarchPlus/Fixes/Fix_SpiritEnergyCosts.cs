using HarmonyLib;
using UtilityScripts;

namespace RuinarchPlus
{
	// Spirit-energy prices obey the same cost setting and rounding as mana prices.
	// The original method still handles disabled spirit energy and None costs.
	[HarmonyPatch(typeof(PlayerSkillData), nameof(PlayerSkillData.GetSpiritEnergyCostBaseOnLevel))]
	internal static class Fix_SpiritEnergyCosts
	{
		private static void Postfix(ref int __result)
		{
			__result = SpellUtilities.GetModifiedSpellCost(__result,
				WorldSettings.Instance.worldSettingsData.playerSkillSettings.GetCostsModification());
		}
	}
}
