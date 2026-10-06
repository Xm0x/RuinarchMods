using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(SpawnEyeWardData), nameof(SpawnEyeWardData.CanPerformAbilityTowards), new[] { typeof(LocationStructure) })]
	internal static class Fix_EyeWardActionCost
	{
		private static void Postfix(SpawnEyeWardData __instance, ref bool __result)
		{
			__result = __result && __instance.CanPerformAbility();
		}
	}

	[HarmonyPatch(typeof(SpawnEyeWardData), nameof(SpawnEyeWardData.CanPerformAbilityTowards), new[] { typeof(LocationGridTile), typeof(string) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
	internal static class Fix_EyeWardTileCost
	{
		private static void Postfix(SpawnEyeWardData __instance, ref bool __result)
		{
			__result = __result && __instance.CanPerformAbility();
		}
	}

	[HarmonyPatch(typeof(SpawnEyeWardData), nameof(SpawnEyeWardData.ActivateAbility), new[] { typeof(LocationGridTile) })]
	internal static class Fix_EyeWardExecutionCost
	{
		private static bool Prefix(SpawnEyeWardData __instance) { return __instance.CanPerformAbility(); }
	}
}
