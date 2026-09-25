using System;
using System.Collections.Generic;
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

		internal static bool IsCapital(NPCSettlement settlement)
		{
			return TiersType?.GetMethod("IsCapital", Any)?.Invoke(null, new object[] { settlement }) is bool b && b;
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

		private static Type HuntersType => Plus?.GetType("RuinarchPlus.Phase5.Hunters");
		private static Type TradersType => Plus?.GetType("RuinarchPlus.Phase5.Traders");

		private static Type UnrestType => Plus?.GetType("RuinarchPlus.Phase5.Unrest");

		/// <summary>What <paramref name="village"/> holds against its ruler now ("the famine", "2 deaths"...).</summary>
		internal static List<string> UnrestReasons(NPCSettlement village)
		{
			return (UnrestType?.GetMethod("Grievances", Any)?.Invoke(null, new object[] { village }) as List<KeyValuePair<string, float>>)?.Select(g => g.Key).ToList() ?? new List<string>();
		}

		internal static float UnrestPoints(NPCSettlement village) => UnrestType?.GetMethod("Points", Any)?.Invoke(null, new object[] { village }) is float f ? f : -1f;

		internal static void SetUnrest(NPCSettlement village, float points) => UnrestType?.GetMethod("SetPoints", Any)?.Invoke(null, new object[] { village, points });

		internal static bool IsRestless(NPCSettlement village) => UnrestType?.GetMethod("IsRestless", Any)?.Invoke(null, new object[] { village }) is bool b && b;

		internal static bool HasUprising(NPCSettlement village) => UnrestType?.GetMethod("HasUprising", Any)?.Invoke(null, new object[] { village }) is bool b && b;

		/// <summary>Whether <paramref name="village"/> counts as hungry (sends hunters).</summary>
		internal static bool IsHungry(NPCSettlement village)
		{
			return HuntersType?.GetMethod("IsHungry", Any)?.Invoke(null, new object[] { village }) is bool b && b;
		}

		/// <summary>Sends hunters from a hungry village now; how many went (-1 without Ruinarch+).</summary>
		internal static int SendHunters(NPCSettlement village)
		{
			object v = HuntersType?.GetMethod("SendHunters", Any)?.Invoke(null, new object[] { village });
			return v is int n ? n : -1;
		}

		internal static bool IsHunting(Character c)
		{
			return HuntersType?.GetMethod("IsHunting", Any)?.Invoke(null, new object[] { c }) is bool b && b;
		}

		/// <summary>Sends a trader with food from one village to another now; the trader, or null.</summary>
		internal static Character SendTrader(NPCSettlement from, NPCSettlement to, int amount)
		{
			return TradersType?.GetMethod("Send", Any)?.Invoke(null, new object[] { from, to, amount }) as Character;
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

		/// <summary>A Ruinarch+ config field's current value; null without Ruinarch+.</summary>
		internal static object Config(string field)
		{
			Type t = Plus?.GetType("RuinarchPlus.RuinarchPlusConfig");
			object current = t?.GetProperty("Current", Any)?.GetValue(null);
			return current == null ? null : t.GetField(field, Any)?.GetValue(current);
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

		/// <summary>True if <paramref name="c"/> saw the structure and has not yet brought the news home.</summary>
		internal static bool Carries(Character c, LocationStructure structure)
		{
			return KnowledgeType?.GetMethod("Carries", Any)?.Invoke(null, new object[] { c, structure }) is bool b && b;
		}

		/// <summary>True if <paramref name="c"/> remembers the structure (told at home or not).</summary>
		internal static bool Remembers(Character c, LocationStructure structure)
		{
			return KnowledgeType?.GetMethod("Remembers", Any)?.Invoke(null, new object[] { c, structure }) is bool b && b;
		}

		/// <summary>True if the village knows the structure (a living resident remembers it and has told it at home).</summary>
		internal static bool VillageKnows(NPCSettlement village, LocationStructure structure)
		{
			return KnowledgeType?.GetMethod("VillageKnows", Any)?.Invoke(null, new object[] { village, structure }) is bool b && b;
		}

		/// <summary><paramref name="c"/> sees the structure (as when it comes into view).</summary>
		internal static void Witness(Character c, LocationStructure structure)
		{
			KnowledgeType?.GetMethod("Witness", Any)?.Invoke(null, new object[] { c, structure });
		}

		/// <summary>The "Who Knows of You" bookmark section's lines as shown, or null.</summary>
		internal static List<string> KnowledgePanelLines()
		{
			return Plus?.GetType("RuinarchPlus.Phase3.KnowledgePanel")?.GetMethod("Texts", Any)?.Invoke(null, null) as List<string>;
		}

		// ---- life cycle (Phase 4) ----
		private static Type LifeType => Plus?.GetType("RuinarchPlus.Phase4.LifeCycle");

		private static object Life(string method, params object[] args) => LifeType?.GetMethod(method, Any)?.Invoke(null, args);

		/// <summary>Age in years, or -1 if unknown (or Ruinarch+ missing).</summary>
		internal static float AgeYears(Character c) => Life("AgeYears", c) is float f ? f : -1f;

		/// <summary>"Child", "Adult", "Elder" or null.</summary>
		internal static string LifeStage(Character c) => Life("Stage", c) as string;

		internal static bool IsChild(Character c) => Life("IsChild", c) is bool b && b;

		internal static bool IsPregnant(Character c) => Life("IsPregnant", c) is bool b && b;

		/// <summary>Her lover if the two may have a child now, else null.</summary>
		internal static Character PartnerOf(Character mother) => Life("PartnerOf", mother) as Character;

		internal static void Conceive(Character mother, Character father) => Life("Conceive", mother, father);

		internal static void SetAgeYears(Character c, float years) => Life("SetAgeYears", c, years);

		internal static void SetDeathAgeYears(Character c, float years) => Life("SetDeathAgeYears", c, years);

		internal static bool IsForgetful(Character c) => Life("IsForgetful", c) is bool b && b;

		/// <summary>Make <paramref name="c"/> forgetful (they forget something at the next hour) or not.</summary>
		internal static void SetForgetful(Character c, bool forgetful) => Life("SetForgetful", c, forgetful);

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
