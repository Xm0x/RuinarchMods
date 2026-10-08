using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RuinarchPerformance
{
	// Keep the native loading body/yields; eliminate repeated HashSet.ElementAt walks.
	internal static class TileObjectLoading
	{
		[ThreadStatic] private static ScopedEnumerator _scope;
		private static readonly MethodInfo Lookup = typeof(Enumerable).GetMethods()
			.Single(m => m.Name == nameof(Enumerable.ElementAt) && m.IsGenericMethodDefinition
				&& m.GetParameters()[1].ParameterType == typeof(int)).MakeGenericMethod(typeof(TileObject));
		private static readonly MethodInfo Replacement = AccessTools.Method(typeof(TileObjectLoading), nameof(At));

		internal static void Install(Harmony harmony)
		{
			Cursor.ValidateRuntime();
			Type iterator = typeof(LoadSecondWaveTileObjectsCoroutine).GetNestedTypes(BindingFlags.NonPublic)
				.Single(t => t.Name.StartsWith("<LoadTileObjects>", StringComparison.Ordinal));
			MethodInfo move = AccessTools.Method(iterator, "MoveNext");
			MethodInfo load = AccessTools.Method(typeof(LoadSecondWaveTileObjectsCoroutine), "LoadTileObjects");
			if (PatchProcessor.GetOriginalInstructions(move).Count(i => i.Calls(Lookup)) != 1)
				throw new InvalidOperationException("Unsupported tile-object loading iterator: expected one ordinal lookup.");
			try
			{
				harmony.Patch(move, transpiler: new HarmonyMethod(typeof(TileObjectLoading), nameof(Transpile)));
				harmony.Patch(load, postfix: new HarmonyMethod(typeof(TileObjectLoading), nameof(Wrap)));
			}
			catch
			{
				harmony.Unpatch(move, HarmonyPatchType.Transpiler, harmony.Id);
				harmony.Unpatch(load, HarmonyPatchType.Postfix, harmony.Id);
				throw;
			}
		}
		private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
		{
			int matches = 0;
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.Calls(Lookup)) { instruction.operand = Replacement; matches++; }
				yield return instruction;
			}
			if (matches != 1) throw new InvalidOperationException("Tile-object ordinal lookup was changed by another patch.");
		}
		private static void Wrap(ref IEnumerator __result) { __result = new ScopedEnumerator(__result); }
		private static TileObject At(IEnumerable<TileObject> source, int index) =>
			_scope != null && source is HashSet<TileObject> set ? _scope.Read(set, index) : source.ElementAt(index);

		internal struct Cursor : IDisposable
		{
			private static readonly AccessTools.FieldRef<HashSet<TileObject>, int> Version = VersionReader();
			private HashSet<TileObject> _source;
			private HashSet<TileObject>.Enumerator _enumerator;
			private int _next, _version;
			private static AccessTools.FieldRef<HashSet<TileObject>, int> VersionReader()
			{
				FieldInfo field = typeof(HashSet<TileObject>).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
					.Single(f => f.FieldType == typeof(int) && f.Name.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0);
				return AccessTools.FieldRefAccess<HashSet<TileObject>, int>(field.Name);
			}
			internal static void ValidateRuntime()
			{
				if (Version == null) throw new InvalidOperationException("HashSet mutation version is unavailable.");
			}
			internal TileObject Read(HashSet<TileObject> source, int index)
			{
				if (source == null) throw new ArgumentNullException(nameof(source));
				if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));
				int version = Version(source);
				if (!ReferenceEquals(_source, source) || _version != version || index != _next)
				{
					_enumerator.Dispose(); _source = source; _enumerator = source.GetEnumerator();
					_version = version; _next = 0;
				}
				// A changed set restarts at the requested native ordinal, not a snapshot.
				while (_next <= index)
				{
					if (!_enumerator.MoveNext()) throw new ArgumentOutOfRangeException(nameof(index));
					_next++;
				}
				return _enumerator.Current;
			}
			public void Dispose() { _enumerator.Dispose(); this = default; }
		}
		private sealed class ScopedEnumerator : IEnumerator, IDisposable
		{
			private IEnumerator _original;
			private Cursor _cursor;
			public object Current { get; private set; }
			internal ScopedEnumerator(IEnumerator original) { _original = original; }
			internal TileObject Read(HashSet<TileObject> source, int index) => _cursor.Read(source, index);
			public bool MoveNext()
			{
				if (_original == null) return false;
				ScopedEnumerator previous = _scope; _scope = this;
				try
				{
					if (!_original.MoveNext()) { Dispose(); return false; }
					Current = _original.Current; return true;
				}
				catch { Dispose(); throw; }
				finally { _scope = previous; }
			}
			public void Reset() { throw new NotSupportedException(); }
			public void Dispose()
			{
				if (_original == null) return;
				ScopedEnumerator previous = _scope; _scope = this;
				try { (_original as IDisposable)?.Dispose(); }
				finally { _original = null; Current = null; _cursor.Dispose(); _scope = previous; }
			}
		}
	}
}
