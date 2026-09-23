using System;
using System.Linq;
using System.Reflection;
using Inner_Maps.Location_Structures;
using Locations.Settlements;

namespace RuinarchDebug
{
	// Reflection bridge to Ruinarch+ (Mass Grave). RuinarchDebug has no compile-time
	// reference to RuinarchPlus, so it still loads and works when Ruinarch+ is disabled;
	// every member here degrades to null/0 in that case.
	internal static class PlusBridge
	{
		private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private static Assembly _plus;

		private static Assembly Plus
		{
			get
			{
				if (_plus == null)
				{
					_plus = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RuinarchPlus");
				}
				return _plus;
			}
		}

		private static Type MassGraveType => Plus?.GetType("Inner_Maps.Location_Structures.MassGrave");

		private static Type ConstructionType => Plus?.GetType("RuinarchPlus.Phase2.MassGraveConstruction");

		internal static bool Available => MassGraveType != null && ConstructionType != null;

		/// <summary>Instantly build a real, visible Mass Grave in <paramref name="settlement"/>.</summary>
		internal static LocationStructure InstantBuild(NPCSettlement settlement)
		{
			return ConstructionType?.GetMethod("InstantBuild", Any)?.Invoke(null, new object[] { settlement }) as LocationStructure;
		}

		/// <summary>The live Mass Grave serving <paramref name="settlement"/>, or null.</summary>
		internal static LocationStructure FindFor(BaseSettlement settlement)
		{
			return MassGraveType?.GetMethod("FindFor", Any)?.Invoke(null, new object[] { settlement }) as LocationStructure;
		}

		internal static bool IsMassGrave(LocationStructure structure)
		{
			return structure != null && MassGraveType != null && MassGraveType.IsInstanceOfType(structure);
		}

		internal static int HauledTotal => GetStaticInt(MassGraveType, "HauledTotal");

		internal static int AbsorbedTotal => GetStaticInt(MassGraveType, "AbsorbedTotal");

		internal static int BodyCount(LocationStructure pit)
		{
			object v = pit == null ? null : MassGraveType?.GetProperty("bodyCount", Any)?.GetValue(pit);
			return v is int i ? i : 0;
		}

		internal static bool HasPendingBlueprint(NPCSettlement settlement)
		{
			object v = ConstructionType?.GetMethod("HasPendingFor", Any)?.Invoke(null, new object[] { settlement });
			return v is bool b && b;
		}

		/// <summary>Decay stage name of an unburied corpse ("Fresh".."Skeletal"), or null if untracked.</summary>
		internal static string DecayStage(Character corpse)
		{
			object v = Plus?.GetType("RuinarchPlus.CorpseDecay")?.GetMethod("GetStage", Any)?.Invoke(null, new object[] { corpse });
			return v?.ToString();
		}

		private static int GetStaticInt(Type t, string property)
		{
			object v = t?.GetProperty(property, Any)?.GetValue(null);
			return v is int i ? i : 0;
		}
	}
}
