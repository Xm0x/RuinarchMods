using System;
using System.Linq;
using System.Reflection;
using Inner_Maps;
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

		/// <summary>On-screen width in pixels of the selected character's path line (Ruinarch+'s zoom fix), or -1.</summary>
		internal static float PathLineWidth(InnerTileMap map)
		{
			object v = Plus?.GetType("RuinarchPlus.Fix_PathLineZoomedOut")?.GetMethod("ScreenWidth", Any)?.Invoke(null, new object[] { map });
			return v is float f ? f : -1f;
		}

		/// <summary>Fill of the decay bar showing on this corpse, or -1 if none is showing.</summary>
		internal static float DecayBarFill(Character corpse)
		{
			object v = Plus?.GetType("RuinarchPlus.Phase2.CorpseDecayBar")?.GetMethod("ShownFill", Any)?.Invoke(null, new object[] { corpse });
			return v is float f ? f : -1f;
		}

		/// <summary>Share of its decay time the corpse has left, or -1 if not tracked.</summary>
		internal static float DecayRemaining(Character corpse)
		{
			object v = Plus?.GetType("RuinarchPlus.CorpseDecay")?.GetMethod("Remaining", Any)?.Invoke(null, new object[] { corpse });
			return v is float f ? f : -1f;
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

		private static Type TiersType => Plus?.GetType("RuinarchPlus.Phase5.SettlementTiers");

		private static Type TownHallType => Plus?.GetType("Inner_Maps.Location_Structures.TownHall");

		/// <summary>"Village", "Town" or "City"; null without Ruinarch+.</summary>
		internal static string Tier(NPCSettlement settlement)
		{
			return TiersType?.GetMethod("Get", Any)?.Invoke(null, new object[] { settlement })?.ToString();
		}

		/// <summary>The faction line under a settlement's name, as Ruinarch+ labels it.</summary>
		internal static string TierLabel(NPCSettlement settlement, string factionLine)
		{
			return TiersType?.GetMethod("Label", Any)?.Invoke(null, new object[] { settlement, factionLine }) as string;
		}

		internal static LocationStructure TownHallFor(BaseSettlement settlement)
		{
			return TownHallType?.GetMethod("FindFor", Any)?.Invoke(null, new object[] { settlement }) as LocationStructure;
		}

		internal static LocationStructure InstantBuildTownHall(NPCSettlement settlement)
		{
			return TiersType?.GetMethod("InstantBuildTownHall", Any)?.Invoke(null, new object[] { settlement }) as LocationStructure;
		}

		internal static bool HasPendingTownHall(NPCSettlement settlement)
		{
			object v = TiersType?.GetMethod("HasPendingTownHall", Any)?.Invoke(null, new object[] { settlement });
			return v is bool b && b;
		}

		/// <summary>Whether <paramref name="settlement"/> is in famine; null without Ruinarch+.</summary>
		internal static bool? InFamine(NPCSettlement settlement)
		{
			MethodInfo m = Plus?.GetType("RuinarchPlus.Phase5.Famine")?.GetMethod("IsInFamine", Any);
			return m == null ? (bool?)null : m.Invoke(null, new object[] { settlement }) is bool b && b;
		}

		/// <summary>Decay stage name of an unburied corpse ("Fresh".."Skeletal"), or null if untracked.</summary>
		internal static string DecayStage(Character corpse)
		{
			object v = Plus?.GetType("RuinarchPlus.CorpseDecay")?.GetMethod("GetStage", Any)?.Invoke(null, new object[] { corpse });
			return v?.ToString();
		}

		internal static bool IsUnderCurfew(NPCSettlement settlement)
		{
			object v = Plus?.GetType("RuinarchPlus.Phase2.Curfew")?.GetMethod("IsUnderCurfew", Any)?.Invoke(null, new object[] { settlement });
			return v is bool b && b;
		}

		/// <summary>Ruinarch+ migration factor (0..1) and its reason; (-1, null) if unavailable.</summary>
		internal static float MigrationMultiplier(NPCSettlement settlement, out string reason)
		{
			object[] args = { settlement, null };
			object v = Plus?.GetType("RuinarchPlus.Phase4.MigrationHealth")?.GetMethod("Multiplier", Any)?.Invoke(null, args);
			reason = args[1] as string;
			return v is float f ? f : -1f;
		}

		/// <summary>Set a Ruinarch+ config field at runtime (tests compare against vanilla).</summary>
		internal static void SetConfig(string field, object value)
		{
			Type t = Plus?.GetType("RuinarchPlus.RuinarchPlusConfig");
			object current = t?.GetProperty("Current", Any)?.GetValue(null);
			t?.GetField(field, Any)?.SetValue(current, value);
		}

		private static Type KnowledgeType => Plus?.GetType("RuinarchPlus.Phase3.Knowledge");

		internal static void Learn(Faction faction, LocationStructure structure)
		{
			KnowledgeType?.GetMethod("Learn", Any)?.Invoke(null, new object[] { faction, structure, true });
		}

		internal static bool Knows(Faction faction, LocationStructure structure)
		{
			return KnowledgeType?.GetMethod("Knows", Any)?.Invoke(null, new object[] { faction, structure }) is bool b && b;
		}

		internal static void Forget(Faction faction)
		{
			KnowledgeType?.GetMethod("Forget", Any)?.Invoke(null, new object[] { faction });
		}

		private static Type MissingType => Plus?.GetType("RuinarchPlus.Phase3.MissingPersons");

		/// <summary>Tell Ruinarch+ that one of their people saw <paramref name="c"/> at <paramref name="at"/>.</summary>
		internal static void MissingSaw(Character c, LocationGridTile at)
		{
			MissingType?.GetMethod("Saw", Any)?.Invoke(null, new object[] { c, at });
		}

		/// <summary>"Seen", "Missing", "Searching" or "Lost"; null if untracked.</summary>
		internal static string MissingState(Character c)
		{
			return MissingType?.GetMethod("StateOf", Any)?.Invoke(null, new object[] { c }) as string;
		}

		internal static int FailedSearches(Character c)
		{
			return MissingType?.GetMethod("FailedSearchesOf", Any)?.Invoke(null, new object[] { c }) is int n ? n : -1;
		}

		internal static float HoursToNextSearch(Character c)
		{
			return MissingType?.GetMethod("HoursToNextSearch", Any)?.Invoke(null, new object[] { c }) is float h ? h : -1f;
		}

		/// <summary>"(x, y, 0) 3.0h ago"; null if untracked.</summary>
		internal static string MissingLastSeen(Character c)
		{
			return MissingType?.GetMethod("LastSeenOf", Any)?.Invoke(null, new object[] { c }) as string;
		}

		internal static PartyQuest MissingSearch(Character c)
		{
			return MissingType?.GetMethod("SearchFor", Any)?.Invoke(null, new object[] { c }) as PartyQuest;
		}

		internal static void ClearMissing()
		{
			MissingType?.GetMethod("Clear", Any)?.Invoke(null, null);
		}

		private static int GetStaticInt(Type t, string property)
		{
			object v = t?.GetProperty(property, Any)?.GetValue(null);
			return v is int i ? i : 0;
		}
	}
}
