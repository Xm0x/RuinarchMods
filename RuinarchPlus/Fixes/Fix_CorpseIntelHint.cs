using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(CharacterMarker), "OnPointerEnter", new[] { typeof(Character) })]
	internal static class Fix_CorpseIntelHint
	{
		private static IIntel ForLivingCharacter(IIntel intel, Character character) => character.isDead ? null : intel;

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var getter = AccessTools.PropertyGetter(typeof(Player), nameof(Player.currentActiveIntel));
			var filter = AccessTools.Method(typeof(Fix_CorpseIntelHint), nameof(ForLivingCharacter));
			bool filtered = false;
			foreach (var instruction in instructions)
			{
				yield return instruction;
				if (!filtered && instruction.Calls(getter))
				{
					filtered = true;
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, filter);
				}
			}
		}
	}
}
