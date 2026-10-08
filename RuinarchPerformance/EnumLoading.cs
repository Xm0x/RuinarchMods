using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FullSerializer;
using FullSerializer.Internal;
using HarmonyLib;

namespace RuinarchPerformance
{
	[HarmonyPatch(typeof(fsEnumConverter), nameof(fsEnumConverter.TryDeserialize))]
	internal static class EnumLoading
	{
		private static readonly ConcurrentDictionary<Type, Dictionary<string, object>> Names = new ConcurrentDictionary<Type, Dictionary<string, object>>();
		private static Dictionary<string, object> Build(Type type)
		{
			var values = new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (string name in Enum.GetNames(type))
			{
				object value = Enum.Parse(type, name);
				try { Convert.ToInt64(value); }
				catch (OverflowException) { continue; } // Preserve the native named-UInt64 overflow path.
				values.Add(name, value);
			}
			return values;
		}
		private static bool Prefix(fsData data, ref object instance, Type storageType, ref fsResult __result)
		{
			if (!data.IsString || data.AsString.Length == 0 || data.AsString.IndexOf(',') >= 0) return true;
			if (!Names.GetOrAdd(storageType, Build).TryGetValue(data.AsString, out var value)) return true;
			instance = value; __result = fsResult.Success; return false;
		}
	}
}
