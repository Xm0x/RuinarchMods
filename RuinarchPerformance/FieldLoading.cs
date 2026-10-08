using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FullSerializer.Internal;
using HarmonyLib;

namespace RuinarchPerformance
{
	// Replace only reflected instance-field access; properties and coercions stay native.
	internal static class FieldLoading
	{
		private sealed class Accessors
		{
			internal Func<object, object> Read;
			internal Action<object, object> Write;
		}
		private static readonly Accessors Unsupported = new Accessors();
		private static readonly ConcurrentDictionary<FieldInfo, Accessors> Cache = new ConcurrentDictionary<FieldInfo, Accessors>();
		private static readonly object CompileGate = new object();
		[ThreadStatic] private static FieldInfo _lastField;
		[ThreadStatic] private static Accessors _lastAccessors;
		private static readonly MethodInfo ReadField = AccessTools.Method(typeof(FieldInfo), nameof(FieldInfo.GetValue), new[] { typeof(object) });
		private static readonly MethodInfo WriteField = AccessTools.Method(typeof(FieldInfo), nameof(FieldInfo.SetValue), new[] { typeof(object), typeof(object) });

		internal static void Install(Harmony harmony)
		{
			var read = AccessTools.Method(typeof(fsMetaProperty), nameof(fsMetaProperty.Read));
			var write = AccessTools.Method(typeof(fsMetaProperty), nameof(fsMetaProperty.Write));
			if (PatchProcessor.GetOriginalInstructions(read).Count(i => i.Calls(ReadField)) != 1
				|| PatchProcessor.GetOriginalInstructions(write).Count(i => i.Calls(WriteField)) != 1)
				throw new InvalidOperationException("Unsupported native serialized-field access");
			try
			{
				harmony.Patch(read, transpiler: new HarmonyMethod(typeof(FieldLoading), nameof(Transpile)));
				harmony.Patch(write, transpiler: new HarmonyMethod(typeof(FieldLoading), nameof(Transpile)));
			}
			catch
			{
				harmony.Unpatch(read, HarmonyPatchType.Transpiler, harmony.Id);
				harmony.Unpatch(write, HarmonyPatchType.Transpiler, harmony.Id);
				throw;
			}
		}
		private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
		{
			int matches = 0;
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(ReadField) || instruction.Calls(WriteField))
				{
					instruction.operand = AccessTools.Method(typeof(FieldLoading), instruction.Calls(ReadField) ? nameof(Read) : nameof(Write));
					instruction.opcode = OpCodes.Call; matches++;
				}
				yield return instruction;
			}
			if (matches != 1) throw new InvalidOperationException("Native serialized-field access was changed by another patch");
		}
		private static Accessors For(FieldInfo field)
		{
			if (ReferenceEquals(field, _lastField)) return _lastAccessors;
			if (!Cache.TryGetValue(field, out var accessors))
			{
				lock (CompileGate)
				{
					if (!Cache.TryGetValue(field, out accessors))
					{
						accessors = Compile(field); Cache.TryAdd(field, accessors);
					}
				}
			}
			_lastField = field; _lastAccessors = accessors; return accessors;
		}
		private static Accessors Compile(FieldInfo field)
		{
			Type owner = field.DeclaringType, value = field.FieldType;
			if (field.IsStatic || field.IsLiteral || owner.ContainsGenericParameters || value.IsPointer || value.IsByRef
				|| field.GetRequiredCustomModifiers().Length != 0) return Unsupported;
			var getter = new DynamicMethod("ReadSavedField", typeof(object), new[] { typeof(object) }, typeof(FieldLoading).Module, true);
			var il = getter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0); il.Emit(owner.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, owner);
			il.Emit(OpCodes.Ldfld, field);
			if (value.IsValueType) il.Emit(OpCodes.Box, value);
			il.Emit(OpCodes.Ret);
			var accessors = new Accessors { Read = (Func<object, object>)getter.CreateDelegate(typeof(Func<object, object>)) };
			if (!field.IsInitOnly)
			{
				var setter = new DynamicMethod("WriteSavedField", typeof(void), new[] { typeof(object), typeof(object) }, typeof(FieldLoading).Module, true);
				il = setter.GetILGenerator();
				il.Emit(OpCodes.Ldarg_0); il.Emit(owner.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, owner);
				il.Emit(OpCodes.Ldarg_1); il.Emit(value.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, value);
				il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
				accessors.Write = (Action<object, object>)setter.CreateDelegate(typeof(Action<object, object>));
			}
			return accessors;
		}
		private static object Read(FieldInfo field, object context)
		{
			var accessors = For(field);
			if (accessors.Read != null)
			{
				try { return accessors.Read(context); }
				catch (InvalidCastException) { } // Native reflection owns invalid targets.
				catch (NullReferenceException) { }
			}
			return field.GetValue(context);
		}
		private static void Write(FieldInfo field, object context, object value)
		{
			var accessors = For(field);
			if (accessors.Write != null)
			{
				try { accessors.Write(context, value); return; }
				catch (InvalidCastException) { } // Coercion must use the native reflection binder.
				catch (NullReferenceException) { } // Null resets native value fields to their default.
			}
			field.SetValue(context, value);
		}
	}
}
