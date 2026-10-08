using System;
using System.Threading;
using FullSerializer;
using FullSerializer.Internal;
using HarmonyLib;

namespace RuinarchPerformance
{
	// Native FullSerializer's process-wide dictionaries are not concurrent collections.
	// Preloading adds a reader alongside ordinary saves; lock only their short metadata operations.
	internal static class SerializerMetadata
	{
		private static readonly object Gate = new object();
		internal static void Install(Harmony harmony)
		{
			var prefix = new HarmonyMethod(typeof(SerializerMetadata), nameof(Enter));
			var finalizer = new HarmonyMethod(typeof(SerializerMetadata), nameof(Leave));
			var targets = new[]
			{
				AccessTools.Method(typeof(fsMetaType), "Get", new[] { typeof(fsConfig), typeof(Type) }),
				AccessTools.Method(typeof(fsMetaType), "EmitAotData"),
				AccessTools.Method(typeof(fsMetaType), "ClearCache"),
				AccessTools.Method(typeof(fsTypeCache), "GetType", new[] { typeof(string), typeof(string) }),
				AccessTools.Method(typeof(fsTypeCache), "Reset"),
				AccessTools.Method(typeof(fsPortableReflection), "GetAttribute", new[] { typeof(System.Reflection.MemberInfo), typeof(Type), typeof(bool) }),
				AccessTools.Method(typeof(fsVersionManager), "GetVersionedType", new[] { typeof(Type) })
			};
			foreach (var target in targets)
				if (target == null) throw new InvalidOperationException("Native serializer metadata entry is missing");
			try
			{
				foreach (var target in targets) harmony.Patch(target, prefix: prefix, finalizer: finalizer);
			}
			catch
			{
				foreach (var target in targets) harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
				throw;
			}
		}
		private static void Enter(out bool __state) { __state = false; Monitor.Enter(Gate, ref __state); }
		private static void Leave(bool __state) { if (__state) Monitor.Exit(Gate); }
	}
}
