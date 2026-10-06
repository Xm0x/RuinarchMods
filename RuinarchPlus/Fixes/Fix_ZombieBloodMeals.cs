using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Traits;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(Vampire), nameof(Vampire.OnSeePOI))]
	internal static class Fix_ZombieBloodMeals
	{
		private static bool CanFeed(bool advertised, IPointOfInterest target) => advertised && !(target is Character character && character.characterClass.IsZombie());

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var contains = AccessTools.Method(typeof(List<INTERACTION_TYPE>), nameof(List<INTERACTION_TYPE>.Contains));
			var canFeed = AccessTools.Method(typeof(Fix_ZombieBloodMeals), nameof(CanFeed));
			foreach (var instruction in instructions)
			{
				yield return instruction;
				if (instruction.Calls(contains))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, canFeed);
				}
			}
		}
	}
}
