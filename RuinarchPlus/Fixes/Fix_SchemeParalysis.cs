using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RuinarchPlus
{
	// BUG: paralyzed (and quarantined) villagers can still act - GoapPlanner exempts
	// them from the limiterComponent.canPerform gate (it cancels a job only when
	// !canPerform AND (canPerformValue != -1 OR not Paralyzed/Quarantined)).
	// SchemeData.CanPerformAbilityTowards did NOT apply that exemption, so you couldn't
	// scheme a paralyzed target even though they can leave faction/home.
	// Fix: transpile the single `return targetCharacter.limiterComponent.canPerform;`
	// to widen it with the same exemption. Every other gate (base charge/target checks,
	// player-faction, dead) is preserved.
	[HarmonyPatch(typeof(SchemeData), nameof(SchemeData.CanPerformAbilityTowards), new[] { typeof(Character) })]
	internal static class Fix_SchemeParalysis
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo getCanPerform = AccessTools.PropertyGetter(typeof(LimiterComponent), "canPerform");
			MethodInfo widen = AccessTools.Method(typeof(Fix_SchemeParalysis), nameof(SchemeWiden));
			foreach (CodeInstruction code in instructions)
			{
				yield return code;
				if (getCanPerform != null && code.Calls(getCanPerform))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1); // targetCharacter
					yield return new CodeInstruction(OpCodes.Call, widen);
				}
			}
		}

		// Mirrors GoapPlanner's exemption: allow when paralysis/quarantine is the SOLE
		// "cannot perform" source (canPerformValue == -1).
		private static bool SchemeWiden(bool canPerform, Character target)
		{
			if (canPerform)
			{
				return true;
			}
			if (target == null)
			{
				return false;
			}
			LimiterComponent lc = target.limiterComponent;
			return lc != null && lc.canPerformValue == -1
				&& (target.traitContainer.HasTrait("Paralyzed") || target.traitContainer.HasTrait("Quarantined"));
		}
	}
}
