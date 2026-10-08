using System;
using FullSerializer;
using FullSerializer.Internal;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		[Flags] private enum LoadingFlags { None = 0, First = 1, Alias = 1, Second = 2 }
		private enum LoadingWide : ulong { Ordinary = 7, High = 0xffffffffffffffffUL }
		private void EnumLoadingChecks()
		{
			var converter = new fsEnumConverter();
			bool Value(Type type, fsData data, long expected)
			{
				object result = null;
				return converter.TryDeserialize(data, ref result, type).Succeeded && Convert.ToInt64(result) == expected;
			}
			Check("named enum aliases retain their native values", () =>
				(Value(typeof(LoadingFlags), new fsData("First"), 1) && Value(typeof(LoadingFlags), new fsData("Alias"), 1)
					&& Value(typeof(LoadingWide), new fsData("Ordinary"), 7), "exact named and aliased values survive cached conversion"));
			Check("enum flags and empty components retain native parsing", () =>
				(Value(typeof(LoadingFlags), new fsData("First,,Second,"), 3)
					&& Value(typeof(LoadingFlags), new fsData(""), 0), "comma-separated flags and empty input retain native OR/zero semantics"));
			Check("numeric virtual enums retain native integer conversion", () =>
				(Value(typeof(STRUCTURE_TYPE), new fsData(100123L), 100123)
					&& Value(typeof(LoadingFlags), new fsData(4294967297L), 1), "virtual IDs and native int32 truncation are unchanged"));
			Check("invalid enum names do not overwrite existing values", () =>
			{
				object value = LoadingFlags.Second;
				bool wrongCase = converter.TryDeserialize(new fsData("first"), ref value, typeof(LoadingFlags)).Failed;
				bool wrongType = converter.TryDeserialize(new fsData(true), ref value, typeof(LoadingFlags)).Failed;
				return (wrongCase && wrongType && (LoadingFlags)value == LoadingFlags.Second, "case-sensitive failures preserve the previous enum value");
			});
			Check("wide named enum overflow remains a native error", () =>
			{
				object value = LoadingWide.Ordinary;
				try { converter.TryDeserialize(new fsData("High"), ref value, typeof(LoadingWide)); return (false, "expected native Int64 overflow"); }
				catch (OverflowException) { return ((LoadingWide)value == LoadingWide.Ordinary, "overflow did not overwrite the previous value"); }
			});
		}
	}
}
