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
