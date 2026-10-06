using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Traits;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(CharacterTrait), nameof(CharacterTrait.OnSeePOI))]
	internal static class Fix_TrespassEligibility
	{
		private static bool CanTrespass(bool interior, IPointOfInterest target)
		{
			if (target is Character character && (character.homeStructure == character.currentStructure || character.traitContainer.HasTrait("Paralyzed"))) return false;
			return interior;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var interior = AccessTools.PropertyGetter(typeof(LocationStructure), nameof(LocationStructure.isInterior));
			foreach (var instruction in instructions)
			{
				yield return instruction;
				if (instruction.Calls(interior))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Fix_TrespassEligibility), nameof(CanTrespass)));
				}
			}
		}
	}
}
