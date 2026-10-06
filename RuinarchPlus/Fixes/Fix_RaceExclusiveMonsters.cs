using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(Faction), nameof(Faction.CheckIfCharacterStillFitsIdeology))]
	internal static class Fix_RaceExclusiveMonsters
	{
		private static bool SubjectToIdeology(bool sapient, Faction faction) => sapient ||
			(faction.factionType.HasIdeology(FACTION_IDEOLOGY.Exclusive, out var ideology) && ideology is Exclusive exclusive && exclusive.category == EXCLUSIVE_IDEOLOGY_CATEGORIES.RACE);

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var sapient = AccessTools.Method(typeof(Extensions), nameof(Extensions.IsSapient), new[] { typeof(RACE) });
			var subject = AccessTools.Method(typeof(Fix_RaceExclusiveMonsters), nameof(SubjectToIdeology));
			foreach (var instruction in instructions)
			{
				yield return instruction;
				if (instruction.Calls(sapient))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Call, subject);
				}
			}
		}
	}
}
