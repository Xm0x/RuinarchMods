using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(DemonicStructurePlayerSkill), "CanBuildDemonicStructureOn")]
	internal static class Fix_IndestructibleDemonicPlacement
	{
		private static bool HasProtectedObject(LocationGridTile tile)
		{
			var obj = tile.tileObjectComponent.objHere;
			return obj != null && !(obj is StructureTileObject) && obj.traitContainer.HasTrait("Indestructible");
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var corruption = AccessTools.PropertyGetter(typeof(LocationGridTile), nameof(LocationGridTile.corruptionComponent));
			var protectedObject = AccessTools.Method(typeof(Fix_IndestructibleDemonicPlacement), nameof(HasProtectedObject));
			bool inserted = false;
			foreach (var instruction in instructions)
			{
				if (!inserted && instruction.Calls(corruption))
				{
					inserted = true;
					var resume = generator.DefineLabel();
					var duplicate = new CodeInstruction(OpCodes.Dup).MoveLabelsFrom(instruction);
					yield return duplicate;
					yield return new CodeInstruction(OpCodes.Call, protectedObject);
					yield return new CodeInstruction(OpCodes.Brfalse, resume);
					yield return new CodeInstruction(OpCodes.Pop);
					yield return new CodeInstruction(OpCodes.Ldarg_3);
					yield return new CodeInstruction(OpCodes.Ldstr, "Cannot build over an indestructible object.");
					yield return new CodeInstruction(OpCodes.Stind_Ref);
					yield return new CodeInstruction(OpCodes.Ldc_I4_0);
					yield return new CodeInstruction(OpCodes.Ret);
					instruction.labels.Add(resume);
				}
				yield return instruction;
			}
		}
	}
}
