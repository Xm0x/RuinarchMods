using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(Assault), nameof(Assault.PopulateEmotionReactionsToActor))]
	internal static class Fix_AssaultCrimeJurisdiction
	{
		private static bool WantedHere(CrimeComponent component, CRIME_SEVERITY first, CRIME_SEVERITY second, Character witness) => component.HasWantedCrimeBy(witness.faction, first, second);

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var hasCrime = AccessTools.Method(typeof(CrimeComponent), nameof(CrimeComponent.HasCrime), new[] { typeof(CRIME_SEVERITY), typeof(CRIME_SEVERITY) });
			var wanted = AccessTools.Method(typeof(Fix_AssaultCrimeJurisdiction), nameof(WantedHere));
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(hasCrime))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
					instruction.opcode = OpCodes.Call;
					instruction.operand = wanted;
				}
				yield return instruction;
			}
		}
	}
}
