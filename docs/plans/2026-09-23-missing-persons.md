# Missing Persons and Search Parties Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A village only knows where its people are if it has seen them: residents unseen for a day are reported missing, and the village searches the place they were last seen, retrying less and less often before giving up.

**Architecture:** One new tracker (`Phase3/MissingPersons.cs`) keeps a record per village resident (last-seen tile and time, state, failed searches, current search quest), updates it every in-game hour from the game's own vision lists, posts searches and saves itself through the loader's `ModSave`. The search vehicle is the game's own `RescuePartyQuest`; a second file (`Phase3/MissingPersonsSearch.cs`) holds the Harmony patches that point it at the last-seen spot, keep it alive while the person is missing, sweep the area when the target is out of sight, record how each search ended, and switch off the base game's omniscient rescue roll.

**Tech Stack:** C# (Roslyn via `tools/build-mod.sh`), HarmonyLib 2.2.2, `Ruinarch.ModContent` (`ModSave`), Unity `JsonUtility`; verification through the RuinarchDebug in-game harness (`tools/run-autotest.sh`).

**Spec:** `docs/specs/2026-09-23-missing-persons-design.md` (read it first; this plan implements it).

## Global Constraints

- Never edit `RuinarchRE`; it is the read-only decompiled reference. All changes live in `RuinarchMods`.
- Game code is cited against `RuinarchRE/src/Assembly-CSharp`; every patch target must pass `tools/check-patches.sh` (run automatically by `build-mod.sh`).
- Config keys and defaults (exact): `missingPersonsEnabled = true`, `missingAfterHours = 24`, `searchSweepHours = 6`, `searchRetryHours = 24`, `searchMaxAttempts = 3`.
- ModSave id (exact): `ruinarch.plus.missing`; the save entry is `ModData/ruinarch.plus.missing.json`.
- Save data is a list of strings (JsonUtility drops lists of mod-defined classes).
- Notification wording (exact): "X of V has gone missing. They were last seen P.", "V is organising a search for X." (log only), "The search for X found nothing. V will look again in N hours." (log only), "V has given up the search for X.", "X of V has been found.", "X of V has been found dead."
- No em or en dashes in code comments or docs. Tabs for indentation, matching the existing files.
- Build (from `RuinarchModLoader/`): `./tools/build-mod.sh ../RuinarchMods/RuinarchPlus` and `./tools/build-mod.sh ../RuinarchMods/RuinarchDebug`; a good build prints `N patch class(es) checked, 0 failure(s).`
- Commits use the repo's existing author identity: `git -c user.name="$(git log -1 --format=%an)" -c user.email="$(git log -1 --format=%ae)" commit ...`.
- Testing is the in-game harness only (there is no unit-test runner for game code). A full run takes about 15 minutes of real time and needs the game to itself, so the failing-first run is skipped: without the feature every new check fails trivially (the bridge returns null). Tasks 1 and 2 are verified by compiling and the patch checker; Task 3 writes the checks and runs the suite.

---

### Task 1: Tracker, config, save

**Files:**
- Create: `RuinarchPlus/Phase3/MissingPersons.cs`
- Modify: `RuinarchPlus/Config.cs` (add the five keys after `knowledgeEnabled`)
- Modify: `RuinarchPlus/Phase2/Curfew.cs:60-74` (`Announce` gains `bool notify = true`)
- Modify: `RuinarchPlus/RuinarchPlus.cs:29` (register the ModSave handler)

**Interfaces:**
- Produces (used by Task 2 and, through reflection, Task 3):
  - `internal enum MissingPersons.MissingState { Seen, Missing, Searching, Lost }`
  - `internal sealed class MissingPersons.Record` with fields `Person`, `Village`, `LastSeenTile`, `LastSeenTick`, `State`, `FailedSearches`, `NextSearchTick`, `Search`, `SweepStartedTick`, `ReachedLastSeen`
  - `internal static bool MissingPersons.Enabled`
  - `internal static long MissingPersons.Now` (game ticks since the first day)
  - `internal static MissingPersons.Record MissingPersons.Get(Character c)`
  - `internal static bool MissingPersons.IsSearch(PartyQuest quest)`
  - `internal static void MissingPersons.Saw(Character c, LocationGridTile at)`
  - `internal static void MissingPersons.OnSearchEnded(RescuePartyQuest quest)`
  - `internal static IPartyTargetDestination MissingPersons.Destination(MissingPersons.Record r)`
  - `internal static LocationGridTile MissingPersons.NextSweepTile(Character searcher, MissingPersons.Record r)`
  - harness accessors: `StateOf(Character) -> string`, `FailedSearchesOf(Character) -> int`, `HoursToNextSearch(Character) -> float`, `SearchFor(Character) -> PartyQuest`, `Clear()`
- Consumes: `Curfew.Announce(string text, bool notify = true)`.

- [ ] **Step 1: Config keys**

In `RuinarchPlus/Config.cs`, after the `knowledgeEnabled` line (line 40), add:

```csharp
		// A village only knows where its people are if it has seen them. A resident none of
		// their people has seen for missingAfterHours is reported missing, and the village
		// sends a search party to where they were last seen. A failed search is retried after
		// searchRetryHours, doubling each time; after searchMaxAttempts failures the village
		// gives them up. Replaces the base game's rescue of captives nobody saw.
		public bool missingPersonsEnabled = true;
		public int missingAfterHours = 24;
		// Hours a search party sweeps around the last-seen spot before giving up.
		public int searchSweepHours = 6;
		public int searchRetryHours = 24;
		public int searchMaxAttempts = 3;
```

- [ ] **Step 2: Log-only announcements**

Replace `Curfew.Announce` in `RuinarchPlus/Phase2/Curfew.cs` (lines 57-74) with:

```csharp
		// A notification in the game's event log, like the plague event's own announcements.
		// (Log fillers, which make names clickable, are internal to the game assembly; the
		// text is plain.) With notify false it goes to the log only, not the feed.
		internal static void Announce(string text, bool notify = true)
		{
			try
			{
				global::Log log = GameManager.CreateNewLogUsingNewLocalization(GameManager.Instance.Today(), "Settlement Event", "EventAlerts_Table", "Plagued started", LOG_TAG.Major);
				log.SetLogText(text);
				log.AddLogToDatabase();
				if (notify)
				{
					PlayerManager.Instance.player.ShowNotificationFromPlayer(log, releaseLogAfter: true);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Notification failed: " + e.Message);
			}
			RuinarchPlus.Log?.Info(text);
		}
```

- [ ] **Step 3: The tracker**

Create `RuinarchPlus/Phase3/MissingPersons.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using RuinarchPlus.Phase2;
using UnityEngine;

namespace RuinarchPlus.Phase3
{
	/// <summary>
	/// Missing persons (config: <c>missingPersonsEnabled</c>).
	///
	/// In the base game a village always knows where a captured resident is: it rescues any
	/// Restrained or Paralyzed resident outside the village with no witness, and the party
	/// walks to where the captive is now. Here a village knows only what its people have
	/// seen. Every in-game hour each resident of a village counts as seen if they are in
	/// their home village or someone of their faction (free, sapient, able to witness) has
	/// them or their grave in sight, and the place and time are remembered. A resident unseen
	/// for missingAfterHours is reported missing, and the village posts a search: the game's
	/// own rescue quest, pointed at the last-seen spot (see MissingPersonsSearch.cs). A failed
	/// search is retried after searchRetryHours, doubling, and after searchMaxAttempts the
	/// village gives the person up. A later sighting still reports them found.
	///
	/// Records live in the player's save (ModContent's ModSave). The quests are ordinary
	/// rescue quests, so a save opened without the mod loads and runs them the vanilla way.
	/// </summary>
	internal static class MissingPersons
	{
		private const string SaveId = "ruinarch.plus.missing";
		private const int TicksPerHour = GameManager.ticksPerHour;
		private const int TicksPerDay = TicksPerHour * 24;

		internal enum MissingState
		{
			Seen,
			Missing,
			Searching,
			Lost
		}

		internal sealed class Record
		{
			internal Character Person;
			internal NPCSettlement Village;
			internal LocationGridTile LastSeenTile;
			internal long LastSeenTick;
			internal MissingState State;
			internal int FailedSearches;
			internal long NextSearchTick;
			internal RescuePartyQuest Search;
			// Not saved: a sweep in progress starts over after a load.
			internal long SweepStartedTick = -1;
			internal readonly HashSet<Character> ReachedLastSeen = new HashSet<Character>();
		}

		private static readonly Dictionary<Character, Record> Records = new Dictionary<Character, Record>();

		internal static bool Enabled => RuinarchPlusConfig.Current.missingPersonsEnabled;

		private static RuinarchPlusConfig Config => RuinarchPlusConfig.Current;

		internal static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		/// <summary>Game ticks since the first day (GameDate months are 30 days).</summary>
		internal static long Now
		{
			get
			{
				GameDate d = GameManager.Instance.Today();
				return (long)d.ConvertToContinuousDays() * TicksPerDay + d.tick;
			}
		}

		internal static Record Get(Character c)
		{
			return c != null && Records.TryGetValue(c, out Record r) ? r : null;
		}

		/// <summary>Is this rescue quest a search this tracker posted (or adopted)?</summary>
		internal static bool IsSearch(PartyQuest quest)
		{
			return quest is RescuePartyQuest q && Get(q.targetCharacter)?.Search == q;
		}

		private static bool Trackable(Character c, NPCSettlement village)
		{
			return c != null && village != null && village.locationType == LOCATION_TYPE.VILLAGE && village.owner != null
				&& village.owner.isMajorNonPlayer && c.isNormalCharacter && c.race.IsSapient() && c.faction == village.owner;
		}

		private static Record Track(Character c)
		{
			Record r = Get(c);
			if (r != null)
			{
				return r;
			}
			if (c == null || c.isDead || !(c.homeSettlement is NPCSettlement village) || !Trackable(c, village))
			{
				return null;
			}
			r = new Record { Person = c, Village = village, LastSeenTile = c.gridTileLocation, LastSeenTick = Now, State = MissingState.Seen };
			Records[c] = r;
			return r;
		}

		private static bool IsHeld(Character c)
		{
			return c.traitContainer.HasTrait("Restrained", "Unconscious", "Frozen", "Ensnared", "Enslaved");
		}

		private static bool IsOpen(RescuePartyQuest quest)
		{
			return quest != null && quest.postedFaction != null && quest.postedFaction.partyQuestBoard.availablePartyQuests.Contains(quest);
		}

		private static void End(RescuePartyQuest quest, string reasonKey)
		{
			if (IsOpen(quest))
			{
				quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason(reasonKey));
			}
		}

		/// <summary>"at the Tavern", "near Iri", or "in the wilderness".</summary>
		internal static string Place(LocationGridTile t)
		{
			if (t == null)
			{
				return "nowhere anyone remembers";
			}
			if (t.structure != null && t.structure.structureType != STRUCTURE_TYPE.WILDERNESS)
			{
				return "at " + t.structure.name;
			}
			NPCSettlement near = t.area?.GetFirstNPCSettlementOnArea();
			return near != null ? "near " + near.name : "in the wilderness";
		}

		// ---- hourly ------------------------------------------------------------------------

		internal static void HourlyCheck()
		{
			if (!Enabled || GridMap.Instance?.mainRegion?.settlementsInRegion == null)
			{
				return;
			}
			long now = Now;
			foreach (BaseSettlement s in GridMap.Instance.mainRegion.settlementsInRegion)
			{
				if (s is NPCSettlement village && village.residents != null)
				{
					for (int i = 0; i < village.residents.Count; i++)
					{
						Track(village.residents[i]);
					}
				}
			}
			Dictionary<Faction, HashSet<IPointOfInterest>> sight = new Dictionary<Faction, HashSet<IPointOfInterest>>();
			foreach (Record r in Records.Values.ToList())
			{
				try
				{
					Update(r, now, sight);
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Missing persons: skipped {r.Person?.name} this hour: {e.Message}");
				}
			}
		}

		private static void Update(Record r, long now, Dictionary<Faction, HashSet<IPointOfInterest>> sight)
		{
			Character c = r.Person;
			// The game removes the dead from their village's residents and remembers the
			// village in homeSettlementOnDeath.
			NPCSettlement home = c.isDead ? c.previousCharacterDataComponent?.homeSettlementOnDeath : c.homeSettlement as NPCSettlement;
			if (home != r.Village || r.Village.owner == null || (!c.isDead && c.faction != r.Village.owner))
			{
				Records.Remove(c);
				return;
			}
			if (IsSeen(c, r.Village.owner, sight, out LocationGridTile at))
			{
				Seen(r, at, now);
				return;
			}
			if (r.State == MissingState.Seen && now - r.LastSeenTick >= (long)Config.missingAfterHours * TicksPerHour)
			{
				r.State = MissingState.Missing;
				r.NextSearchTick = now;
				Curfew.Announce($"{c.name} of {r.Village.name} has gone missing. They were last seen {Place(r.LastSeenTile)}.");
			}
			if (r.State == MissingState.Searching && !IsOpen(r.Search))
			{
				// The quest left the board without passing EndQuest.
				FailSearch(r, now);
			}
			if (r.State == MissingState.Missing && now >= r.NextSearchTick)
			{
				PostSearch(r);
			}
		}

		private static bool IsSeen(Character c, Faction faction, Dictionary<Faction, HashSet<IPointOfInterest>> sight, out LocationGridTile at)
		{
			at = null;
			if (!c.isDead && c.gridTileLocation != null && c.IsInHomeSettlement())
			{
				at = c.gridTileLocation;
				return true;
			}
			if (!sight.TryGetValue(faction, out HashSet<IPointOfInterest> seen))
			{
				seen = new HashSet<IPointOfInterest>();
				foreach (Character w in faction.characters)
				{
					if (w != null && !w.isDead && w.hasMarker && w.race.IsSapient() && w.limiterComponent.canWitness && !IsHeld(w))
					{
						foreach (IPointOfInterest poi in w.marker.inVisionPOIs)
						{
							seen.Add(poi);
						}
					}
				}
				sight[faction] = seen;
			}
			if (c.hasMarker && c.gridTileLocation != null && seen.Contains(c))
			{
				at = c.gridTileLocation;
				return true;
			}
			if (c.grave?.gridTileLocation != null && seen.Contains(c.grave))
			{
				at = c.grave.gridTileLocation;
				return true;
			}
			return false;
		}

		// Someone of their faction has them (or their grave) in sight right now.
		private static void Seen(Record r, LocationGridTile at, long now)
		{
			Character c = r.Person;
			bool wasMissing = r.State != MissingState.Seen;
			RescuePartyQuest search = r.Search;
			r.LastSeenTile = at;
			r.LastSeenTick = now;
			r.State = MissingState.Seen;
			r.FailedSearches = 0;
			r.SweepStartedTick = -1;
			r.ReachedLastSeen.Clear();
			if (c.isDead)
			{
				Records.Remove(c);
				if (wasMissing)
				{
					Curfew.Announce($"{c.name} of {r.Village.name} has been found dead.");
				}
				End(search, "Target_Dead");
				return;
			}
			if (wasMissing)
			{
				Curfew.Announce($"{c.name} of {r.Village.name} has been found.");
			}
			if (!IsHeld(c))
			{
				r.Search = null;
				End(search, "Target_Safe");
			}
			// A captive stays the search's target: the party goes on to free them, now
			// heading for where they were just seen.
		}

		/// <summary>One of their people sees <paramref name="c"/> at <paramref name="at"/> (a witness posting a rescue; the test harness).</summary>
		internal static void Saw(Character c, LocationGridTile at)
		{
			if (!Enabled || at == null)
			{
				return;
			}
			Record r = Track(c);
			if (r != null)
			{
				Seen(r, at, Now);
			}
		}

		// ---- searches ----------------------------------------------------------------------

		private static void PostSearch(Record r)
		{
			Faction f = r.Village.owner;
			// A witness rescue for them may already be on the board: that one becomes the search.
			RescuePartyQuest existing = f.partyQuestBoard.availablePartyQuests.OfType<RescuePartyQuest>().FirstOrDefault(q => q.targetCharacter == r.Person);
			RescuePartyQuest quest = existing;
			if (quest == null)
			{
				quest = PartyManager.Instance.CreateNewPartyQuest(PARTY_QUEST_TYPE.Rescue) as RescuePartyQuest;
				quest.SetMadeInLocation(r.Village);
				quest.SetTargetCharacter(r.Person);
				quest.SetQuestCreator(null);
			}
			// Set before posting: the board asks IsStillEligibleFor.
			r.Search = quest;
			r.State = MissingState.Searching;
			r.SweepStartedTick = -1;
			r.ReachedLastSeen.Clear();
			if (existing == null)
			{
				f.partyQuestBoard.AddPartyQuest(quest, null);
			}
			Curfew.Announce($"{r.Village.name} is organising a search for {r.Person.name}.", notify: false);
		}

		private static void FailSearch(Record r, long now)
		{
			r.Search = null;
			r.SweepStartedTick = -1;
			r.ReachedLastSeen.Clear();
			r.FailedSearches++;
			if (r.FailedSearches >= Config.searchMaxAttempts)
			{
				r.State = MissingState.Lost;
				Curfew.Announce($"{r.Village.name} has given up the search for {r.Person.name}.");
				return;
			}
			int wait = Config.searchRetryHours << (r.FailedSearches - 1);
			r.State = MissingState.Missing;
			r.NextSearchTick = now + (long)wait * TicksPerHour;
			Curfew.Announce($"The search for {r.Person.name} found nothing. {r.Village.name} will look again in {wait} hours.", notify: false);
		}

		/// <summary>Every end of a search passes here, while the party is still assigned.</summary>
		internal static void OnSearchEnded(RescuePartyQuest quest)
		{
			Record r = Get(quest.targetCharacter);
			if (r == null || r.Search != quest)
			{
				return;
			}
			// The quest is already ending: nothing below may end it again.
			r.Search = null;
			long now = Now;
			Party party = quest.assignedParty;
			if (party != null && SeenBy(party.members, r.Person, out LocationGridTile at))
			{
				Seen(r, at, now);
				return;
			}
			if (r.State == MissingState.Searching)
			{
				FailSearch(r, now);
			}
		}

		private static bool SeenBy(List<Character> watchers, Character c, out LocationGridTile at)
		{
			foreach (Character w in watchers)
			{
				if (w == null || w.isDead || !w.hasMarker)
				{
					continue;
				}
				if (c.hasMarker && c.gridTileLocation != null && w.marker.IsPOIInVision(c))
				{
					at = c.gridTileLocation;
					return true;
				}
				if (c.grave?.gridTileLocation != null && w.marker.IsPOIInVision(c.grave))
				{
					at = c.grave.gridTileLocation;
					return true;
				}
			}
			at = null;
			return false;
		}

		/// <summary>Where a search goes: the last-seen structure, or its area in the wilderness.</summary>
		internal static IPartyTargetDestination Destination(Record r)
		{
			LocationGridTile t = r?.LastSeenTile;
			if (t == null)
			{
				return null;
			}
			return t.structure != null && t.structure.structureType != STRUCTURE_TYPE.WILDERNESS ? (IPartyTargetDestination)t.structure : t.area;
		}

		/// <summary>Each searcher walks to the last-seen tile first, then to random reachable tiles in that area and the ones bordering it.</summary>
		internal static LocationGridTile NextSweepTile(Character searcher, Record r)
		{
			LocationGridTile last = r.LastSeenTile;
			if (last == null || searcher.gridTileLocation == null)
			{
				return null;
			}
			if (!r.ReachedLastSeen.Contains(searcher))
			{
				if (searcher.gridTileLocation.GetDistanceTo(last) > 2f && searcher.movementComponent.HasPathTo(last))
				{
					return last;
				}
				r.ReachedLastSeen.Add(searcher);
			}
			List<Area> areas = new List<Area> { last.area };
			areas.AddRange(last.area.neighbourComponent.neighbours);
			for (int i = 0; i < 6; i++)
			{
				LocationGridTile t = areas[UnityEngine.Random.Range(0, areas.Count)].GetRandomPassableTile();
				if (t != null && searcher.movementComponent.HasPathTo(t))
				{
					return t;
				}
			}
			return null;
		}

		// ---- harness accessors (RuinarchDebug reaches these by reflection) -------------------

		internal static string StateOf(Character c)
		{
			return Get(c)?.State.ToString();
		}

		internal static int FailedSearchesOf(Character c)
		{
			return Get(c)?.FailedSearches ?? 0;
		}

		internal static float HoursToNextSearch(Character c)
		{
			Record r = Get(c);
			return r == null ? -1f : (r.NextSearchTick - Now) / (float)TicksPerHour;
		}

		internal static PartyQuest SearchFor(Character c)
		{
			return Get(c)?.Search;
		}

		internal static void Clear()
		{
			Records.Clear();
		}

		// ---- persistence -------------------------------------------------------------------
		// One string per record:
		// character|village|x|y|lastSeenTick|state|failedSearches|nextSearchTick|questId

		private static string Save()
		{
			MissingSaveData file = new MissingSaveData();
			foreach (Record r in Records.Values)
			{
				if (r.Person == null || r.Village == null)
				{
					continue;
				}
				Vector3Int p = r.LastSeenTile != null ? r.LastSeenTile.localPlace : new Vector3Int(-1, -1, 0);
				file.records.Add(string.Join("|", r.Person.persistentID, r.Village.persistentID, p.x, p.y, r.LastSeenTick,
					(int)r.State, r.FailedSearches, r.NextSearchTick, IsOpen(r.Search) ? r.Search.persistentID : ""));
			}
			return file.records.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Records.Clear();
			if (string.IsNullOrEmpty(json))
			{
				RuinarchPlus.Log?.Info("Missing persons loaded: the save holds no records.");
				return;
			}
			MissingSaveData file = JsonUtility.FromJson<MissingSaveData>(json);
			LocationGridTile[,] map = GridMap.Instance.mainRegion.innerMap.map;
			int count = 0;
			foreach (string line in file?.records ?? new List<string>())
			{
				try
				{
					string[] f = line.Split('|');
					Character c = f.Length == 9 ? CharacterManager.Instance.GetCharacterByPersistentID(f[0]) : null;
					NPCSettlement village = f.Length == 9 ? DatabaseManager.Instance.settlementDatabase.GetSettlementByPersistentIDSafe(f[1]) as NPCSettlement : null;
					if (c == null || village == null)
					{
						RuinarchPlus.Log?.Warning($"Missing persons: dropped saved record {line} (character or village not found).");
						continue;
					}
					int x = int.Parse(f[2]);
					int y = int.Parse(f[3]);
					Record r = new Record
					{
						Person = c,
						Village = village,
						LastSeenTile = x >= 0 && y >= 0 && x < map.GetLength(0) && y < map.GetLength(1) ? map[x, y] : null,
						LastSeenTick = long.Parse(f[4]),
						State = (MissingState)int.Parse(f[5]),
						FailedSearches = int.Parse(f[6]),
						NextSearchTick = long.Parse(f[7]),
						Search = string.IsNullOrEmpty(f[8]) ? null : DatabaseManager.Instance.partyQuestDatabase.GetPartyQuestByPersistentID(f[8]) as RescuePartyQuest
					};
					if (r.State == MissingState.Searching && r.Search == null)
					{
						// The quest did not come back: search again at the next check.
						r.State = MissingState.Missing;
						r.NextSearchTick = 0;
					}
					Records[c] = r;
					count++;
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Missing persons: dropped saved record {line}: {e.Message}");
				}
			}
			RuinarchPlus.Log?.Info($"Missing persons loaded: {count} record(s), {Records.Values.Count(r => r.State != MissingState.Seen)} missing.");
		}
	}

	[Serializable]
	public class MissingSaveData
	{
		public List<string> records = new List<string>();
	}

	/// <summary>Once per in-game hour (20 ticks), like the Mass Grave's hourly tick.</summary>
	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class MissingPersons_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			if (__instance.Today().tick % GameManager.ticksPerHour != 0)
			{
				return;
			}
			try
			{
				MissingPersons.HourlyCheck();
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons hourly check failed: " + e.Message);
			}
		}
	}
}
```

- [ ] **Step 4: Register the save handler**

In `RuinarchPlus/RuinarchPlus.cs`, after `Phase3.Knowledge.Register();` (line 29), add:

```csharp
			Phase3.MissingPersons.Register();
```

- [ ] **Step 5: Build**

Run (from `RuinarchModLoader/`): `./tools/build-mod.sh ../RuinarchMods/RuinarchPlus 2>&1 | grep -E 'error|FAIL|checked'`
Expected: `38 patch class(es) checked, 0 failure(s).` (37 before, plus `MissingPersons_HourTick`). Any `error CS` line: fix and rebuild.

- [ ] **Step 6: Commit**

```bash
cd RuinarchMods
git add RuinarchPlus/Phase3/MissingPersons.cs RuinarchPlus/Config.cs RuinarchPlus/Phase2/Curfew.cs RuinarchPlus/RuinarchPlus.cs
git -c user.name="$(git log -1 --format=%an)" -c user.email="$(git log -1 --format=%ae)" commit -m "Ruinarch+: track who each village has seen; report the missing"
```

---

### Task 2: Search patches

**Files:**
- Create: `RuinarchPlus/Phase3/MissingPersonsSearch.cs`

**Interfaces:**
- Consumes (Task 1): `MissingPersons.Enabled`, `Get`, `IsSearch`, `Saw`, `OnSearchEnded`, `Destination`, `NextSweepTile`, `Now`, `Record.State`, `Record.SweepStartedTick`, `MissingState.Seen`.
- Produces: seven `[HarmonyPatch]` classes; nothing called by other tasks.

- [ ] **Step 1: Write the patches**

Create `RuinarchPlus/Phase3/MissingPersonsSearch.cs`:

```csharp
using System;
using HarmonyLib;
using Inner_Maps;

namespace RuinarchPlus.Phase3
{
	// The search party is the game's own RescuePartyQuest (RescuePartyQuest.cs,
	// RescueBehaviour.cs). These patches make it search where the person was last seen.

	// A search, and a witness rescue too, heads for where the person was last seen, not
	// where they are now (vanilla: the target's live structure or area).
	[HarmonyPatch(typeof(RescuePartyQuest), nameof(RescuePartyQuest.GetTargetDestination))]
	internal static class Missing_Destination
	{
		private static void Postfix(RescuePartyQuest __instance, ref IPartyTargetDestination __result)
		{
			if (!MissingPersons.Enabled)
			{
				return;
			}
			try
			{
				IPartyTargetDestination lastSeen = MissingPersons.Destination(MissingPersons.Get(__instance.targetCharacter));
				if (lastSeen != null)
				{
					__result = lastSeen;
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (destination) failed, using vanilla: " + e.Message);
			}
		}
	}

	// A search stays on the board while the person is missing, whatever their condition
	// (vanilla: only while the target is Restrained).
	[HarmonyPatch(typeof(RescuePartyQuest), nameof(RescuePartyQuest.IsStillEligibleFor))]
	internal static class Missing_Eligible
	{
		private static void Postfix(RescuePartyQuest __instance, ref bool __result)
		{
			if (!__result && MissingPersons.Enabled && MissingPersons.IsSearch(__instance)
				&& MissingPersons.Get(__instance.targetCharacter).State != MissingPersons.MissingState.Seen)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(RescuePartyQuest), nameof(RescuePartyQuest.GetPartyQuestName))]
	internal static class Missing_Name
	{
		private static void Postfix(RescuePartyQuest __instance, ref string __result)
		{
			if (MissingPersons.Enabled && MissingPersons.IsSearch(__instance))
			{
				__result = "Search for " + __instance.targetCharacter.name;
			}
		}
	}

	// The base game's omniscient roll (any Restrained or Paralyzed resident outside the
	// village, no witness needed) is replaced by the missing-person searches.
	[HarmonyPatch(typeof(SettlementPartyComponent), "TryCreateRescueQuest")]
	internal static class Missing_NoOmniscientRescue
	{
		private static bool Prefix()
		{
			return !MissingPersons.Enabled;
		}
	}

	// A witness posting a rescue is looking at the target: that is where they were seen.
	[HarmonyPatch(typeof(PartyQuestBoard), nameof(PartyQuestBoard.CreateRescuePartyQuest))]
	internal static class Missing_WitnessSaw
	{
		private static void Prefix(Character questCreator, Character targetCharacter)
		{
			if (!MissingPersons.Enabled || questCreator == null || targetCharacter?.gridTileLocation == null)
			{
				return;
			}
			try
			{
				MissingPersons.Saw(targetCharacter, targetCharacter.gridTileLocation);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (witness) failed: " + e.Message);
			}
		}
	}

	// Every way a search ends passes through EndQuest while the party is still assigned:
	// found (a member has the target or their grave in sight) or failed.
	[HarmonyPatch(typeof(PartyQuest), nameof(PartyQuest.EndQuest))]
	internal static class Missing_SearchEnded
	{
		private static void Prefix(PartyQuest __instance)
		{
			if (!MissingPersons.Enabled || !(__instance is RescuePartyQuest quest) || !MissingPersons.IsSearch(quest))
			{
				return;
			}
			try
			{
				MissingPersons.OnSearchEnded(quest);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (search ended) failed: " + e.Message);
			}
		}
	}

	// On site, a searcher who does not see the target sweeps the area around the last-seen
	// spot instead of walking to where the target is now (vanilla CreateGoToJob(target)).
	// Once the target is in sight the vanilla behaviour runs: free them, or find them dead
	// or safe.
	[HarmonyPatch(typeof(RescueBehaviour), nameof(RescueBehaviour.TryDoBehaviour))]
	internal static class Missing_Sweep
	{
		private static bool Prefix(Character character, ref JobQueueItem producedJob, ref bool __result)
		{
			if (!MissingPersons.Enabled || !character.partyComponent.hasParty)
			{
				return true;
			}
			Party party = character.partyComponent.currentParty;
			if (party == null || !party.isActive || party.partyState != PARTY_STATE.Working
				|| !(party.currentQuest is RescuePartyQuest quest) || !MissingPersons.IsSearch(quest) || !character.hasMarker)
			{
				return true;
			}
			try
			{
				Character target = quest.targetCharacter;
				if (target.hasMarker && character.marker.IsPOIInVision(target))
				{
					return true;
				}
				producedJob = null;
				__result = true;
				if (target.grave != null && character.marker.IsPOIInVision(target.grave))
				{
					quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Target_Dead"));
					return false;
				}
				MissingPersons.Record r = MissingPersons.Get(target);
				long now = MissingPersons.Now;
				if (r.SweepStartedTick < 0)
				{
					r.SweepStartedTick = now;
				}
				if (now - r.SweepStartedTick >= (long)RuinarchPlusConfig.Current.searchSweepHours * GameManager.ticksPerHour)
				{
					quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Target_Nowhere"));
					return false;
				}
				LocationGridTile next = MissingPersons.NextSweepTile(character, r);
				__result = next != null && character.jobComponent.CreateGoToSpecificTileJob(next, out producedJob);
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (sweep) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}
}
```

- [ ] **Step 2: Build and check the patch targets**

Run (from `RuinarchModLoader/`): `./tools/build-mod.sh ../RuinarchMods/RuinarchPlus 2>&1 | grep -E 'error|FAIL|checked'`
Expected: `45 patch class(es) checked, 0 failure(s).` A `FAIL` line names a target or parameter the game does not have: fix the attribute or parameter name against `RuinarchRE`, rebuild.

- [ ] **Step 3: Commit**

```bash
cd RuinarchMods
git add RuinarchPlus/Phase3/MissingPersonsSearch.cs
git -c user.name="$(git log -1 --format=%an)" -c user.email="$(git log -1 --format=%ae)" commit -m "Ruinarch+: villages search where the missing were last seen"
```

---

### Task 3: Harness suite and run

**Files:**
- Modify: `RuinarchDebug/PlusBridge.cs` (add `using Inner_Maps;` and six members after `Forget`, line 114)
- Modify: `RuinarchDebug/AutoTest.cs`:
  - `Run()` line 187: call the new suite after `KnowledgeSuite()`
  - lines 969-1009: party forming moves into `FormPartyFor`
  - lines 1062-1117: `KnowledgeSaveRoundTrip` uses the new `SaveAndRead` / `ReplayLoad`
  - add `MissingPersonsSuite`, `FormPartyFor`, `SaveAndRead`, `ReplayLoad`, `ModsLogHas`

**Interfaces:**
- Consumes (Task 1, by reflection): `MissingPersons.Saw`, `StateOf`, `FailedSearchesOf`, `HoursToNextSearch`, `SearchFor`, `Clear`.
- Produces: `PlusBridge.MissingSaw(Character, LocationGridTile)`, `MissingState(Character) -> string`, `FailedSearches(Character) -> int`, `HoursToNextSearch(Character) -> float`, `MissingSearch(Character) -> PartyQuest`, `ClearMissing()`.

- [ ] **Step 1: Bridge members**

In `RuinarchDebug/PlusBridge.cs` add `using Inner_Maps;` to the usings, and after `Forget` (line 114):

```csharp
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

		internal static PartyQuest MissingSearch(Character c)
		{
			return MissingType?.GetMethod("SearchFor", Any)?.Invoke(null, new object[] { c }) as PartyQuest;
		}

		internal static void ClearMissing()
		{
			MissingType?.GetMethod("Clear", Any)?.Invoke(null, null);
		}
```

- [ ] **Step 2: Shared helpers**

In `RuinarchDebug/AutoTest.cs`, next to `Guard` (line 1172), add:

```csharp
		// Villager parties accept quests before dawn (Party.InitialScheduleToCheckQuest, 5-7 am)
		// and set out when that day's Work shift starts; a party that accepts after the shift
		// began never leaves. So call this at 5 am, as the game would. Villagers sitting in an
		// idle party (no quest) are free to join. Null (logged) if fewer than `min` are free.
		private Party FormPartyFor(PartyQuest quest, NPCSettlement village, int min, int max)
		{
			List<Character> members = village.residents.Where(r => r != null && !r.isDead && r.marker != null
				&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive)
				&& r != village.ruler && r.limiterComponent.canMove).Take(max).ToList();
			if (members.Count < min)
			{
				Log($"  only {members.Count} free resident(s) for a party; {quest.partyQuestType} needs {min}");
				return null;
			}
			foreach (Character m in members.Where(m => m.partyComponent.hasParty).ToList())
			{
				Guard("leave idle party", () => { m.partyComponent.currentParty.RemoveMember(m); return m; });
			}
			Party formed = Guard("form a party", () =>
			{
				Party p = PartyManager.Instance.CreateNewParty(members[0]);
				foreach (Character m in members.Skip(1))
				{
					p.AddMember(m);
				}
				p.TryAcceptQuest(quest, members[0]);
				foreach (Character m in members)
				{
					p.AddMemberThatJoinedQuest(m);
				}
				return p;
			});
			Log($"  formed a party of {members.Count} for {quest.GetPartyQuestName()}: {string.Join(", ", members.Select(m => m.name))}");
			return formed;
		}

		// Saves the game for real (paused, as the game always saves), hands back the named
		// mod-data entry from the save zip (null if missing) and the zip's entry list, and
		// deletes the save.
		private IEnumerator SaveAndRead(string entry, Action<string, string> got)
		{
			const string saveName = "RuinarchPlus-autotest";
			string zip = Path.Combine(UtilityScripts.Utilities.gameSavePath, saveName + ".zip");
			SaveCurrentProgressManager saver = SaveManager.Instance.saveCurrentProgressManager;
			Try("delete an old test save", () => { if (File.Exists(zip)) File.Delete(zip); });
			// Saving a running world races its save threads against live log objects and can
			// hang the save forever, so pause like the game does.
			Try("pause for the save", () => UIManager.Instance.Pause());
			Try("save the game", () => saver.DoManualSave(saveName));
			yield return WaitReal(() => File.Exists(zip) && !saver.isSaving && !saver.isWritingToDisk, 180f, "the test save to be written");
			Try("resume after the save", () =>
			{
				UIManager.Instance.Unpause();
				UIManager.Instance.SetProgressionSpeed4X();
				Time.timeScale = TimeScale;
			});
			string json = null;
			string entries = "";
			Try("read the test save", () =>
			{
				using (ZipArchive archive = ZipFile.OpenRead(zip))
				{
					entries = string.Join(", ", archive.Entries.Select(e => e.FullName));
					ZipArchiveEntry found = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(entry));
					if (found != null)
					{
						using (StreamReader r = new StreamReader(found.Open()))
						{
							json = r.ReadToEnd();
						}
					}
				}
			});
			Try("delete the test save", () => { if (File.Exists(zip)) File.Delete(zip); });
			got(json, entries);
		}

		// Replays loading: puts the entry where the game extracts saves, then runs the game's
		// own "save finished loading" step. Every other ModSave handler gets load(null).
		private void ReplayLoad(string entry, string json)
		{
			string dir = Path.Combine(UtilityScripts.Utilities.tempPath, "ModData");
			Try("stage the extracted save data", () => { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, entry), json); });
			Try("run the game's load-finished step", () => SaveManager.Instance.DeleteSaveFilesInTempDirectory());
			Try("clean up staged data", () => { if (Directory.Exists(dir)) Directory.Delete(dir, true); });
		}

		private bool ModsLogHas(string text)
		{
			string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
			return File.Exists(mods) && File.ReadAllText(mods).Contains(text);
		}
```

- [ ] **Step 3: Move the knowledge test onto the helpers**

Replace lines 972-1008 of `AutoTest.cs` (the `if (quest.assignedParty == null) { ... }` block inside `if (quest != null)`) with:

```csharp
				if (quest.assignedParty == null)
				{
					yield return WaitForHour(5);
					FormPartyFor(quest, village, 3, 4);
				}
```

Replace `KnowledgeSaveRoundTrip` (lines 1061-1117) with:

```csharp
		// The ledger rides inside the real save zip and comes back through the real load hook.
		private IEnumerator KnowledgeSaveRoundTrip(Faction faction, LocationStructure portal)
		{
			if (!PlusBridge.Knows(faction, portal))
			{
				PlusBridge.Learn(faction, portal);
			}
			string json = null;
			string entries = null;
			yield return SaveAndRead("ruinarch.plus.knowledge.json", (j, e) => { json = j; entries = e; });
			Check("knowledge is stored inside the player's save file", () =>
				(json != null && json.Contains(portal.persistentID), json == null ? "entries: " + entries : $"{json.Length} bytes: {json.Substring(0, Math.Min(json.Length, 160))}"));
			if (json == null)
			{
				yield break;
			}
			PlusBridge.Forget(faction);
			ReplayLoad("ruinarch.plus.knowledge.json", json);
			Check("knowledge comes back when the save loads", () => (PlusBridge.Knows(faction, portal), $"known after load={PlusBridge.Knows(faction, portal)}"));
		}
```

- [ ] **Step 4: The missing-persons suite**

Add after `KnowledgeSaveRoundTrip`:

```csharp
		// Phase 3: a resident none of their people has seen for a while is reported missing,
		// and the village searches where they were last seen. Three residents are stranded in
		// the wilderness at once, each one "last seen" there by the village: a restrained
		// captive left where they were seen (found and freed), a restrained one moved far
		// away after being seen (the search fails, is retried, and is given up), and one
		// killed where they were seen (found dead). Timings are shortened for the run.
		private IEnumerator MissingPersonsSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("missing persons", "RuinarchPlus not loaded");
				yield break;
			}
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer && !v.isPlagued)
				.OrderByDescending(v => v.residents.Count(r => r != null && !r.isDead)).FirstOrDefault();
			List<Character> people = village?.residents.Where(r => r != null && !r.isDead && r.hasMarker && r != village.ruler
				&& !r.isFactionLeader && !r.partyComponent.hasParty && r.limiterComponent.canMove).ToList();
			if (people == null || people.Count < 3)
			{
				Skip("missing persons", "no village with three free residents: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			LocationGridTile centre = village.areas[0].gridTileComponent.centerGridTile;
			List<LocationGridTile> wild = GridMap.Instance.mainRegion.areas
				.Where(a => !a.IsNextToOrPartOfVillage() && !a.HasSettlementOnArea() && a.gridTileComponent.centerGridTile != null
					&& !a.gridTileComponent.centerGridTile.isOccupied && a.gridTileComponent.centerGridTile.structure is Wilderness
					&& people[0].movementComponent.HasPathTo(a.gridTileComponent.centerGridTile))
				.Select(a => a.gridTileComponent.centerGridTile)
				.OrderBy(t => t.GetDistanceTo(centre)).ToList();
			if (wild.Count < 4)
			{
				Skip("missing persons", $"only {wild.Count} reachable wilderness spot(s)");
				yield break;
			}
			LocationGridTile spotCaptive = wild[0];
			LocationGridTile spotWanderer = wild[1];
			LocationGridTile spotVictim = wild[2];
			LocationGridTile far = wild.OrderByDescending(t => t.GetDistanceTo(spotWanderer)).First();
			Log($"missing persons village: {Describe(village)}; spots captive={spotCaptive} wanderer={spotWanderer} far={far} victim={spotVictim}");

			PlusBridge.SetConfig("missingAfterHours", 4);
			PlusBridge.SetConfig("searchSweepHours", 2);
			PlusBridge.SetConfig("searchRetryHours", 4);
			PlusBridge.SetConfig("searchMaxAttempts", 3);
			Character captive = people[0];
			Character wanderer = people[1];
			Character victim = people[2];
			Guard("strand the missing", () =>
			{
				CharacterManager.Instance.Teleport(captive, spotCaptive);
				captive.traitContainer.AddTrait(captive, "Restrained");
				PlusBridge.MissingSaw(captive, spotCaptive);
				CharacterManager.Instance.Teleport(wanderer, spotWanderer);
				PlusBridge.MissingSaw(wanderer, spotWanderer);
				CharacterManager.Instance.Teleport(wanderer, far);
				wanderer.traitContainer.AddTrait(wanderer, "Restrained");
				CharacterManager.Instance.Teleport(victim, spotVictim);
				PlusBridge.MissingSaw(victim, spotVictim);
				victim.Death("autotest");
				return captive;
			});
			Log($"  captive {captive.name} restrained={captive.traitContainer.HasTrait("Restrained")}, wanderer {wanderer.name} restrained={wanderer.traitContainer.HasTrait("Restrained")}, victim {victim.name} dead={victim.isDead}");

			// 1. The base game would roll a rescue for a restrained resident nobody saw.
			yield return WaitGameHours(3f, null);
			Check("no rescue before anyone misses them", () =>
			{
				bool quest = village.owner.partyQuestBoard.availablePartyQuests.OfType<RescuePartyQuest>().Any(q => q.targetCharacter == captive);
				return (!quest, $"rescue quest={quest} state={PlusBridge.MissingState(captive)}");
			});

			// 2. Unseen for missingAfterHours: missing, announced.
			yield return WaitGameHours(4f, () => PlusBridge.MissingState(captive) is string s && s != "Seen");
			Check("an unseen resident is reported missing", () =>
			{
				string s = PlusBridge.MissingState(captive);
				bool announced = ModsLogHas($"{captive.name} of {village.name} has gone missing");
				return ((s == "Missing" || s == "Searching") && announced, $"state={s ?? "untracked"} announced={announced}");
			});

			// 3. The search goes where they were seen, not where they are.
			PartyQuest search = null;
			yield return WaitGameHours(2f, () => (search = PlusBridge.MissingSearch(wanderer)) != null);
			Check("the search heads for the last-seen spot, not where they are", () =>
			{
				IPartyTargetDestination d = search?.GetTargetDestination();
				bool atLastSeen = d == spotWanderer.area || d == spotWanderer.structure;
				bool atLive = d == far.area || d == far.structure;
				return (search != null && atLastSeen && !atLive,
					$"destination={(d as Area)?.name ?? (d as LocationStructure)?.name ?? "none"} lastSeen={spotWanderer.area.name} live={far.area.name}");
			});
			Check("the search is named as a search", () => (search?.GetPartyQuestName() == "Search for " + wanderer.name, "name=" + (search?.GetPartyQuestName() ?? "no search")));

			// 4-6. Let the searches run. Parties take quests at dawn; if none took a search by
			// 8 am, form one at the next 5 am as the game would.
			float start = GameHours;
			List<float> gaps = new List<float>();
			string wandererWas = PlusBridge.MissingState(wanderer);
			string captiveWas = PlusBridge.MissingState(captive);
			Func<bool> settled = () => PlusBridge.MissingState(wanderer) == "Lost" && PlusBridge.MissingState(victim) == null
				&& !captive.traitContainer.HasTrait("Restrained");
			while (GameHours - start < 160f && !settled())
			{
				yield return WaitGameHours(1f, () =>
				{
					string w = PlusBridge.MissingState(wanderer);
					if (w != wandererWas)
					{
						Log($"  {wanderer.name}: {wandererWas} -> {w} after {GameHours - start:F1}h (failed searches {PlusBridge.FailedSearches(wanderer)}, next in {PlusBridge.HoursToNextSearch(wanderer):F1}h)");
						if (w == "Missing" && wandererWas == "Searching")
						{
							gaps.Add(PlusBridge.HoursToNextSearch(wanderer));
						}
						wandererWas = w;
					}
					string cs = PlusBridge.MissingState(captive);
					if (cs != captiveWas)
					{
						Log($"  {captive.name}: {captiveWas} -> {cs} after {GameHours - start:F1}h");
						captiveWas = cs;
					}
					return settled();
				});
				if (GameManager.Instance.Today().tick / GameManager.ticksPerHour == 8
					&& new[] { captive, wanderer, victim }.Select(PlusBridge.MissingSearch).Any(q => q != null && q.assignedParty == null))
				{
					yield return WaitForHour(5);
					foreach (PartyQuest idle in new[] { captive, wanderer, victim }.Select(PlusBridge.MissingSearch).Where(q => q != null && q.assignedParty == null).ToList())
					{
						FormPartyFor(idle, village, 1, 2);
					}
				}
			}
			Check("a captive left where they were seen is found and freed", () =>
			{
				bool freed = !captive.traitContainer.HasTrait("Restrained");
				bool announced = ModsLogHas($"{captive.name} of {village.name} has been found.");
				return (freed && announced && PlusBridge.MissingState(captive) == "Seen", $"freed={freed} announced={announced} state={PlusBridge.MissingState(captive)}");
			});
			Check("a resident killed out of sight is found dead", () =>
			{
				bool announced = ModsLogHas($"{victim.name} of {village.name} has been found dead.");
				return (announced && PlusBridge.MissingState(victim) == null, $"announced={announced} state={PlusBridge.MissingState(victim) ?? "dropped"}");
			});
			Check("failed searches are retried less often, then given up", () =>
			{
				bool spaced = gaps.Count >= 2 && Math.Abs(gaps[0] - 4f) <= 1f && Math.Abs(gaps[1] - 8f) <= 1f;
				bool givenUp = PlusBridge.MissingState(wanderer) == "Lost" && ModsLogHas($"{village.name} has given up the search for {wanderer.name}.");
				return (spaced && givenUp, $"gaps={string.Join(", ", gaps.Select(g => g.ToString("F1")))}h state={PlusBridge.MissingState(wanderer)} failed={PlusBridge.FailedSearches(wanderer)}");
			});

			// 7. Records ride inside the real save. (Replaying the load hands every other
			// ModSave handler load(null): the knowledge ledger is emptied, which no later
			// suite reads.)
			string json = null;
			string entries = null;
			yield return SaveAndRead("ruinarch.plus.missing.json", (j, e) => { json = j; entries = e; });
			Check("missing persons are stored inside the player's save file", () =>
				(json != null && json.Contains(wanderer.persistentID), json == null ? "entries: " + entries : $"{json.Length} bytes"));
			if (json != null)
			{
				string before = PlusBridge.MissingState(wanderer);
				PlusBridge.ClearMissing();
				ReplayLoad("ruinarch.plus.missing.json", json);
				Check("missing persons come back when the save loads", () =>
					(before != null && PlusBridge.MissingState(wanderer) == before, $"before={before ?? "none"} after load={PlusBridge.MissingState(wanderer) ?? "none"}"));
			}

			PlusBridge.SetConfig("missingAfterHours", 24);
			PlusBridge.SetConfig("searchSweepHours", 6);
			PlusBridge.SetConfig("searchRetryHours", 24);
			PlusBridge.SetConfig("searchMaxAttempts", 3);
		}
```

In `Run()`, after `yield return KnowledgeSuite();` (line 187), add:

```csharp
			// Strands three residents of the fullest village in the wilderness; one never
			// comes back.
			yield return MissingPersonsSuite();
```

- [ ] **Step 5: Build both mods**

Run (from `RuinarchModLoader/`): `./tools/build-mod.sh ../RuinarchMods/RuinarchPlus 2>&1 | grep -E 'error|FAIL|checked'; ./tools/build-mod.sh ../RuinarchMods/RuinarchDebug 2>&1 | grep -E 'error|FAIL|checked'`
Expected: `45 patch class(es) checked, 0 failure(s).` and `1 patch class(es) checked, 0 failure(s).`

- [ ] **Step 6: Run the full in-game suite**

Steam must be running and the game closed. From `RuinarchModLoader/`:

```bash
GAME="$HOME/.local/share/Steam/steamapps/common/Ruinarch"
pgrep -f Ruinarch.exe >/dev/null && { echo "game still running"; exit 1; }
rm -f "$GAME/Mods/RuinarchDebug/autotest.flag" "$GAME/Mods/RuinarchDebug/"*.png
if ./tools/build-mod.sh ../RuinarchMods/RuinarchPlus "$GAME/Mods" > /tmp/bp.log 2>&1 && ./tools/build-mod.sh ../RuinarchMods/RuinarchDebug "$GAME/Mods" > /tmp/bd.log 2>&1; then
  grep -hE 'checked' /tmp/bp.log /tmp/bd.log; ./tools/run-autotest.sh 2700; echo "exit=$?"
else grep -hE 'error|FAIL' /tmp/bp.log /tmp/bd.log; fi
```

Expected: every earlier check still passes, the nine new checks print `PASS` (no rescue before missed, reported missing, last-seen destination, named as search, found and freed, found dead, retried then given up, stored in save, back after load), `AUTOTEST DONE ... fail=0`, `exit=0`.

On a failure, read the check's detail and the `missing persons` lines in `Mods/RuinarchDebug/autotest.log` and `Mods/mods.log`, fix the cause in Task 1 or 2 code (not the check), rebuild, rerun. The three open points from the spec show up here: bodies not in `inVisionPOIs` (victim never "found dead" although a party reached the spot), no party ever formed (log line "only N free resident(s)"), and the quest name (check "named as a search").

- [ ] **Step 7: Commit**

```bash
cd RuinarchMods
git add RuinarchDebug/PlusBridge.cs RuinarchDebug/AutoTest.cs
git -c user.name="$(git log -1 --format=%an)" -c user.email="$(git log -1 --format=%ae)" commit -m "RuinarchDebug: missing-persons suite; shared party and save helpers"
```

---

### Task 4: Docs, version, publish

**Files:**
- Modify: `RuinarchPlus/README.md` (feature list and config table: add the Missing persons row and the five keys)
- Modify: `RuinarchPlus/RuinarchPlus-DESIGN.md:237-241` (mark "Rescue by last-known location" and "Missing persons" shipped, naming `Phase3/MissingPersons.cs`, `Phase3/MissingPersonsSearch.cs`, config `missingPersonsEnabled`)
- Modify: `README.md` (root: the Ruinarch+ row mentions villages searching for their missing)
- Modify: `RuinarchPlus/mod.json` (`"version": "0.4.0"`; description mentions missing persons)
- Modify: `docs/specs/2026-09-23-missing-persons-design.md` (status line: implemented; record any open point resolved differently in Task 3)

- [ ] **Step 1: Write the doc changes**

Feature text for `RuinarchPlus/README.md` (same table as the other features):

```markdown
| **Missing persons** | A village only knows where its people are if it has seen them. A resident none of their people has seen for a day is reported missing, and the village sends a search party to where they were last seen. The party sweeps the area and frees them, finds them dead, or comes back empty-handed; the village tries again after 24 hours, then 48, and gives up after three failed searches. Replaces the base game's rescues of captives nobody saw. |
```

Config rows:

```markdown
| `missingPersonsEnabled` | `true` | Villages search for residents they have not seen, where they were last seen. |
| `missingAfterHours` | `24` | Unseen hours before a resident is reported missing. |
| `searchSweepHours` | `6` | Hours a search party sweeps around the last-seen spot. |
| `searchRetryHours` | `24` | Wait after the first failed search; doubles after each failure. |
| `searchMaxAttempts` | `3` | Failed searches before the village gives the person up. |
```

In `RuinarchPlus/RuinarchPlus-DESIGN.md`, replace the two bullets "**Rescue by last-known location:** ..." and "**Missing persons:** ..." (lines 237-241) with:

```markdown
- **Shipped: missing persons and searches by last-known location**
  (`Phase3/MissingPersons.cs`, `Phase3/MissingPersonsSearch.cs`, config
  `missingPersonsEnabled`). Each resident's last sighting by their own people (home village,
  or in sight of a free faction member) is kept hourly; unseen for a day, they are reported
  missing and the village posts the game's own rescue quest, pointed at the last-seen spot,
  where the party sweeps until it sees them or gives up (retried after 24 then 48 hours,
  three attempts). The omniscient settlement rescue roll is off. Records ride in the save
  through `ModSave` (`ModData/ruinarch.plus.missing.json`). Spec:
  `docs/specs/2026-09-23-missing-persons-design.md`.
```

- [ ] **Step 2: Check for dashes**

Run: `grep -rnP '[\x{2013}\x{2014}]' RuinarchPlus docs README.md || echo clean`
Expected: `clean`.

- [ ] **Step 3: Commit and push**

```bash
cd RuinarchMods
git add -A RuinarchPlus README.md docs
git -c user.name="$(git log -1 --format=%an)" -c user.email="$(git log -1 --format=%ae)" commit -m "docs: missing persons; Ruinarch+ 0.4.0"
git fetch origin && git rebase origin/master && git push origin master
```

Expected: push succeeds (fetch first: the maintainer edits files on GitHub directly).
