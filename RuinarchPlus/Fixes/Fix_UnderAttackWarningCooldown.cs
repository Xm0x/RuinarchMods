using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Quests;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(QuestManager), "OnSingleCharacterAttackedDemonicStructure")]
	internal static class Fix_UnderAttackWarningCooldown
	{
		private static readonly Dictionary<DemonicStructure, long> LastWarning = new Dictionary<DemonicStructure, long>();

		private static bool VisibleOrRecentlyWarned(bool visible, DemonicStructure structure)
		{
			if (visible) return true;
			var today = GameManager.Instance.Today();
			long now = (long)today.ConvertToContinuousDays() * GameManager.ticksPerDay + today.tick;
			if (LastWarning.TryGetValue(structure, out long last) && now >= last && now - last < GameManager.Instance.GetTicksBasedOnHour(2)) return true;
			LastWarning[structure] = now;
			return false;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var visible = AccessTools.Method(typeof(InnerMapCameraMove), nameof(InnerMapCameraMove.CanSee));
			var cooldown = AccessTools.Method(typeof(Fix_UnderAttackWarningCooldown), nameof(VisibleOrRecentlyWarned));
			foreach (var instruction in instructions)
			{
				yield return instruction;
				if (instruction.Calls(visible))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_2);
					yield return new CodeInstruction(OpCodes.Call, cooldown);
				}
			}
		}

		[HarmonyPatch(typeof(QuestManager), "OnDestroy")]
		private static class ClearWorldWarnings
		{
			private static void Postfix() => LastWarning.Clear();
		}
	}
}
