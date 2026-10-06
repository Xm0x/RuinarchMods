using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Locations.Settlements.Settlement_Events;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(IsPlagued), nameof(IsPlagued.ReactionToActor))]
	internal static class Fix_QuarantineEligibility
	{
		private static LocationEventManager Events(NPCSettlement settlement) => settlement?.eventManager;

		private static bool HasPlagueEvent(LocationEventManager events, out PlaguedEvent plagueEvent, Character actor)
		{
			plagueEvent = null;
			return actor.isNormalCharacter && events != null && events.HasActiveEvent(out plagueEvent);
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var events = AccessTools.PropertyGetter(typeof(NPCSettlement), nameof(NPCSettlement.eventManager));
			var lookup = AccessTools.GetDeclaredMethods(typeof(LocationEventManager)).Single(m => m.Name == nameof(LocationEventManager.HasActiveEvent) && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(PlaguedEvent));
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(events))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = AccessTools.Method(typeof(Fix_QuarantineEligibility), nameof(Events));
				}
				else if (instruction.Calls(lookup))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					instruction.opcode = OpCodes.Call;
					instruction.operand = AccessTools.Method(typeof(Fix_QuarantineEligibility), nameof(HasPlagueEvent));
				}
				yield return instruction;
			}
		}
	}
}
