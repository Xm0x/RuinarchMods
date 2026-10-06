using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(ActualGoapNode), "ShouldDoVigilantEffect")]
	internal static class Fix_FrozenVigilant
	{
		private static void Postfix(ActualGoapNode __instance, ref bool __result)
		{
			if (__result && __instance.target.traitContainer.HasTrait("Frozen")) __result = false;
		}
	}
}
