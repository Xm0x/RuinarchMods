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
					_plus = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => SafeName(a) == "RuinarchPlus");
				}
				return _plus;
			}
		}

		// An assembly's simple name, or null: dynamic assemblies are skipped, and one whose
		// name cannot be read is passed over (Mono once threw CultureNotFoundException from
		// GetName() on a loaded assembly, which ended a whole harness run).
		internal static string SafeName(System.Reflection.Assembly a)
		{
			if (a == null || a.IsDynamic)
			{
				return null;
			}
			try
			{
				return a.GetName().Name;
			}
			catch
			{
				return null;
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

		/// <summary>What the famine rule sees in <paramref name="village"/>: starving villagers of
		/// those inside it; (-1, -1) without Ruinarch+.</summary>
		internal static (int starving, int villagers) FamineCount(NPCSettlement village)
		{
			object[] args = { village, 0 };
			return Plus?.GetType("RuinarchPlus.Phase5.Famine")?.GetMethod("Starving", Any)?.Invoke(null, args) is int n ? (n, (int)args[1]) : (-1, -1);
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

		private static Type UprisingsType => Plus?.GetType("RuinarchPlus.Phase5.Uprisings");

		internal static bool UprisingKindsAvailable => UprisingsType != null;

		/// <summary>The next uprising in <paramref name="village"/> is <paramref name="kind"/> (Brawl, Assassination, Jailing, CivilWar).</summary>
		internal static void ForceUprising(NPCSettlement village, string kind) => UprisingsType?.GetMethod("ForceNext", Any)?.Invoke(null, new object[] { village, kind });

		/// <summary>The kind weights for an uprising led by <paramref name="leader"/>: kind -> weight.</summary>
		internal static Dictionary<string, float> UprisingWeights(NPCSettlement village, Character leader, Character ruler)
		{
			string text = UprisingsType?.GetMethod("WeightsText", Any)?.Invoke(null, new object[] { village, leader, ruler }) as string;
			Dictionary<string, float> w = new Dictionary<string, float>();
			foreach (string part in (text ?? "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] kv = part.Split('=');
				if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
				{
					w[kv[0]] = f;
				}
			}
			return w;
		}

		internal static Character UprisingLeader(NPCSettlement village) => UnrestType?.GetMethod("UprisingLeader", Any)?.Invoke(null, new object[] { village }) as Character;

		internal static string UprisingKind(NPCSettlement village) => UnrestType?.GetMethod("UprisingKindOf", Any)?.Invoke(null, new object[] { village }) as string;

		/// <summary>A jailed ex-ruler: "carrying" (on the way to the prison), "held", or null.</summary>
		internal static string HeldState(Character c) => UprisingsType?.GetMethod("HeldState", Any)?.Invoke(null, new object[] { c }) as string;

		internal static void SetHeldSince(Character c, long tick) => UprisingsType?.GetMethod("HeldSince", Any)?.Invoke(null, new object[] { c, tick });

		/// <summary>Ruinarch+'s clock (game ticks), the one its timers count in.</summary>
		internal static long PlusNow => UnrestType?.GetProperty("Now", Any)?.GetValue(null) is long t ? t : 0L;

		private static Type WatchType => Plus?.GetType("RuinarchPlus.Phase6.NightWatch");

		internal static bool WatchAvailable => WatchType != null;

		/// <summary>The night watch guards of <paramref name="village"/>.</summary>
		internal static List<Character> GuardsOf(NPCSettlement village) => WatchType?.GetMethod("GuardsOf", Any)?.Invoke(null, new object[] { village }) as List<Character> ?? new List<Character>();

		/// <summary>Run the night watch's hourly check now.</summary>
		internal static void WatchCheck() => WatchType?.GetMethod("Check", Any)?.Invoke(null, null);

		internal static bool IsGuard(Character c) => WatchType?.GetMethod("IsGuard", Any)?.Invoke(null, new object[] { c }) is bool b && b;

		private static Type BlightHeartType => Plus?.GetType("RuinarchPlus.Phase7.BlightHeart");

		private static Type BlightEngineType => Plus?.GetType("RuinarchPlus.Phase7.BlightEngine");

		internal static bool BlightAvailable => BlightHeartType != null && BlightEngineType != null;

		/// <summary>Corrupt <paramref name="t"/> the way the blight does; false if the rules refuse it.</summary>
		internal static bool BlightCorrupt(LocationGridTile t) => BlightEngineType?.GetMethod("Corrupt", Any)?.Invoke(null, new object[] { t }) is bool b && b;

		/// <summary>Redraw blighted village tiles now (the blight does it once an hour).</summary>
		internal static void BlightFlush() => BlightEngineType?.GetMethod("Flush", Any)?.Invoke(null, null);

		/// <summary>Standing Blight Hearts.</summary>
		internal static List<LocationStructure> BlightHearts() => (BlightHeartType?.GetMethod("All", Any)?.Invoke(null, null) as System.Collections.IEnumerable)?.Cast<LocationStructure>().ToList() ?? new List<LocationStructure>();

		internal static int BlightLevel(LocationStructure heart) => BlightHeartType?.GetProperty("Level", Any)?.GetValue(heart) is int n ? n : -1;

		internal static int BlightReach(LocationStructure heart) => BlightHeartType?.GetProperty("Reach", Any)?.GetValue(heart) is int n ? n : -1;

		/// <summary>The Heart's patch as of its last hourly refresh.</summary>
		internal static HashSet<LocationGridTile> BlightPatch(LocationStructure heart) => BlightHeartType?.GetField("Patch", Any)?.GetValue(heart) as HashSet<LocationGridTile> ?? new HashSet<LocationGridTile>();

		/// <summary>What the blight would write into a save right now.</summary>
		internal static string BlightSave() => BlightHeartType?.GetMethod("Save", Any)?.Invoke(null, null) as string;

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

		internal static bool IsTrading(Character c)
		{
			return TradersType?.GetMethod("IsTrading", Any)?.Invoke(null, new object[] { c }) is bool b && b;
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

		/// <summary>"Child", "Adult", "Elder", "Young" (a creature) or null.</summary>
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

		/// <summary>A living natural creature (not the player's) that has an age.</summary>
		internal static bool IsCreature(Character c) => Life("IsCreature", c) is bool b && b;

		/// <summary>Drawn smaller: a child or a creature's young.</summary>
		internal static bool IsSmall(Character c) => Life("IsSmall", c) is bool b && b;

		/// <summary>A creature's breeding group ("kind/place"), or null.</summary>
		internal static string GroupOf(Character c) => Life("GroupOf", c) as string;

		internal static int GroupSize(string group) => Life("GroupSize", group) is int n ? n : 0;

		/// <summary>Every breeding group with room and a pair has a young now.</summary>
		internal static List<Summon> BreedNow() => Life("BreedNow") as List<Summon> ?? new List<Summon>();

		/// <summary>A creature kind's lifespan in years, or -1.</summary>
		internal static float CreatureLifespan(RACE race) =>
			LifeType?.GetField("CreatureLifespans", Any)?.GetValue(null) is Dictionary<RACE, float> d && d.TryGetValue(race, out float years) ? years : -1f;

		/// <summary>A kind the game never replaces, which has young of its own.</summary>
		internal static bool IsBreeder(RACE race) => LifeType?.GetField("Breeders", Any)?.GetValue(null) is HashSet<RACE> h && h.Contains(race);

		/// <summary>The faction forgets the player: what its people remember and what its
		/// records hold (a record would teach it straight back).</summary>
		internal static void Forget(Faction faction)
		{
			ForgetMemory(faction);
			ForgetRecords(faction);
		}

		/// <summary>The faction's people forget the player; its records stay.</summary>
		internal static void ForgetMemory(Faction faction)
		{
			KnowledgeType?.GetMethod("Forget", Any)?.Invoke(null, new object[] { faction });
		}

		// ---- records (Phase 4) ----
		private static Type RecordsType => Plus?.GetType("RuinarchPlus.Phase4.Records");

		internal static bool RecordsAvailable => RecordsType != null;

		/// <summary>The buildings the record in a dwelling or Library names; null if it keeps none.</summary>
		internal static HashSet<LocationStructure> RecordOf(LocationStructure holder) =>
			RecordsType?.GetMethod("RecordOf", Any)?.Invoke(null, new object[] { holder }) as HashSet<LocationStructure>;

		/// <summary>The standing Book Shelves or Books that carry the record in a holder.</summary>
		internal static List<TileObject> CarriersOf(LocationStructure holder) =>
			RecordsType?.GetMethod("CarriersOf", Any)?.Invoke(null, new object[] { holder }) as List<TileObject> ?? new List<TileObject>();

		/// <summary>A carrier of the record in <paramref name="holder"/>; with <paramref name="start"/>
		/// a holder without one gets its Book Shelves or a new Book first.</summary>
		internal static TileObject CarrierFor(LocationStructure holder, bool start) =>
			RecordsType?.GetMethod("CarrierFor", Any)?.Invoke(null, new object[] { holder, start }) as TileObject;

		/// <summary>The Write or Read action type Ruinarch+ registered (NONE if missing).</summary>
		internal static INTERACTION_TYPE RecordAction(bool write) =>
			Plus?.GetType("RuinarchPlus.Phase4.RecordActions")?.GetProperty(write ? "Write" : "Read", Any)?.GetValue(null) is INTERACTION_TYPE t ? t : INTERACTION_TYPE.NONE;

		/// <summary>Give <paramref name="c"/> the Write or Read job at <paramref name="carrier"/>
		/// now. Ruinarch+ plans these as IDLE jobs (priority 250) in free time, where any work
		/// comes first; a test asks for it at once, so the job is a VISIT_STRUCTURE (1000),
		/// which outranks work and sleep.</summary>
		internal static void PlanRecordAction(Character c, bool write, TileObject carrier) =>
			c.PlanIdle(JOB_TYPE.VISIT_STRUCTURE, RecordAction(write), carrier);

		internal static LocationStructure LibraryFor(BaseSettlement settlement) =>
			Plus?.GetType("Inner_Maps.Location_Structures.Library")?.GetMethod("FindFor", Any)?.Invoke(null, new object[] { settlement }) as LocationStructure;

		internal static LocationStructure InstantBuildLibrary(NPCSettlement settlement) =>
			RecordsType?.GetMethod("InstantBuildLibrary", Any)?.Invoke(null, new object[] { settlement }) as LocationStructure;

		internal static void CheckLibraries() =>
			RecordsType?.GetMethod("HourlyCheck", Any)?.Invoke(null, null);

		/// <summary><paramref name="c"/> remembers the structure as if read at home (not carried).</summary>
		internal static void RememberAtHome(Character c, LocationStructure structure) =>
			KnowledgeType?.GetMethod("Read", Any)?.Invoke(null, new object[] { c, structure });

		internal static void ForgetRecords(Faction faction) =>
			RecordsType?.GetMethod("Forget", Any)?.Invoke(null, new object[] { faction });

		/// <summary><paramref name="c"/> writes what they remember into the record in <paramref name="holder"/> now.</summary>
		internal static bool WriteRecord(Character c, LocationStructure holder) =>
			RecordsType?.GetMethod("Write", Any)?.Invoke(null, new object[] { c, holder }) is bool b && b;

		private static Type MissingType => Plus?.GetType("RuinarchPlus.Phase3.MissingPersons");

		/// <summary>Tell Ruinarch+ that one of their people saw <paramref name="c"/> at <paramref name="at"/>.</summary>
		internal static void MissingSaw(Character c, LocationGridTile at)
		{
			MissingType?.GetMethod("Saw", Any)?.Invoke(null, new object[] { c, at });
		}

		/// <summary>Tell Ruinarch+ that <paramref name="finder"/>, one of their people, found
		/// <paramref name="corpse"/> dead (as a burial does): their village stops searching.</summary>
		internal static void MissingFoundDead(Character corpse, Character finder)
		{
			MissingType?.GetMethod("Buried", Any)?.Invoke(null, new object[] { corpse, finder });
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
