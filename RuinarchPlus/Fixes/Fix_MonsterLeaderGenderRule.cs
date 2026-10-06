using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(FactionManager), nameof(FactionManager.RerollInclusiveTypeIdeology))]
	internal static class Fix_MonsterLeaderGenderRule
	{
		private static bool CanUseLeaderGender(bool started, Character leader) => started && leader.isNormalCharacter;

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var started = AccessTools.PropertyGetter(typeof(GameManager), nameof(GameManager.gameHasStarted));
			var filter = AccessTools.Method(typeof(Fix_MonsterLeaderGenderRule), nameof(CanUseLeaderGender));
			foreach (var instruction in instructions)
			{
				yield return instruction;
				if (instruction.Calls(started))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_2);
					yield return new CodeInstruction(OpCodes.Call, filter);
				}
			}
		}
	}
}
