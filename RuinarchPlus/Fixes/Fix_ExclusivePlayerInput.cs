using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(Player), nameof(Player.SetCurrentlyActivePlayerSpell))]
	internal static class Fix_SpellClearsIntel
	{
		private static void Prefix(Player __instance, SkillData action)
		{
			if (action != null && __instance.currentActiveIntel != null) __instance.SetCurrentActiveIntel(null);
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.SetCurrentActiveIntel))]
	internal static class Fix_IntelClearsSpell
	{
		private static void Prefix(Player __instance, IIntel intel)
		{
			if (intel != null && __instance.currentActivePlayerSpell != null) __instance.SetCurrentlyActivePlayerSpell(null);
		}
	}
}
