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
	/// them or their grave in sight, and the place and time are remembered. Someone away at
	/// work (a job, a party's quest) told their village where they went and counts as seen
	/// where they are, until they are missed. A resident unseen
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

		private static bool IsAwayAtWork(Character c)
		{
			return !c.isDead && c.gridTileLocation != null && !IsHeld(c)
				&& (c.currentJob != null || c.currentActionNode != null
					|| (c.partyComponent.hasParty && c.partyComponent.currentParty.isActive));
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

		/// <summary>"at {2}" (a building), "near {2}" (a village), or a plain phrase; <paramref name="where"/> fills {2}.</summary>
		private static string Place(LocationGridTile t, out object where)
		{
			where = null;
			if (t == null)
			{
				return "nowhere anyone remembers";
			}
			if (t.structure != null && t.structure.structureType != STRUCTURE_TYPE.WILDERNESS)
			{
				where = t.structure;
				return "at {2}";
			}
			where = t.area?.GetFirstNPCSettlementOnArea();
			return where != null ? "near {2}" : "in the wilderness";
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
			// Away at work (mining, hunting, a party's quest): they told someone where they were
			// going, so nobody worries, and the place they went counts as where they were last
			// seen. Only before they are missed: someone already missing who is out doing
			// something has told no one, and is found only by being seen.
			if (r.State == MissingState.Seen && IsAwayAtWork(c))
			{
				Seen(r, c.gridTileLocation, now);
				return;
			}
			if (r.State == MissingState.Seen && now - r.LastSeenTick >= (long)Config.missingAfterHours * TicksPerHour)
			{
				r.State = MissingState.Missing;
				r.NextSearchTick = now;
				string place = Place(r.LastSeenTile, out object where);
				Curfew.Announce("{0} of {1} has gone missing. They were last seen " + place + ".", c, r.Village, where);
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
					Curfew.Announce("{0} of {1} has been found dead.", c, r.Village);
				}
				End(search, "Target_Dead");
				return;
			}
			if (wasMissing)
			{
				Curfew.Announce("{0} of {1} has been found.", c, r.Village);
			}
			if (!IsHeld(c))
			{
				r.Search = null;
				End(search, "Target_Safe");
			}
			// A captive stays the search's target: the party goes on to free them, now
			// heading for where they were just seen.
		}

		/// <summary>
		/// <paramref name="burier"/> buried <paramref name="corpse"/>. One of their own people
		/// burying them is finding them dead, wherever the grave is: a Mass Grave leaves no
		/// gravestone to be seen later.
		/// </summary>
		internal static void Buried(Character corpse, Character burier)
		{
			if (!Enabled || corpse == null || burier == null || !Records.TryGetValue(corpse, out Record r) || r.Village?.owner == null || burier.faction != r.Village.owner)
			{
				return;
			}
			Seen(r, burier.gridTileLocation ?? r.LastSeenTile, Now);
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
			Curfew.Note("{0} is organising a search for {1}.", r.Village, r.Person);
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
				Curfew.Announce("{0} has given up the search for {1}.", r.Village, r.Person);
				return;
			}
			int wait = Config.searchRetryHours << (r.FailedSearches - 1);
			r.State = MissingState.Missing;
			r.NextSearchTick = now + (long)wait * TicksPerHour;
			Curfew.Note("The search for {0} found nothing. {1} will look again in {2} hours.", r.Person, r.Village, wait);
		}

		/// <summary>Every end of a search passes here, while the party is still assigned.</summary>
		internal static void OnSearchEnded(RescuePartyQuest quest, string reason)
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
				// For bug reports: why the game ended it and how close the searchers got.
				string members = party == null ? "no party" : string.Join(", ", party.members.Where(m => m != null).Select(m =>
					$"{m.name} {(m.gridTileLocation != null && r.LastSeenTile != null ? m.gridTileLocation.GetDistanceTo(r.LastSeenTile).ToString("F0") + " tiles from the spot" : "off map")}"
					+ (m.hasMarker && r.Person.hasMarker && m.marker.IsPOIInVision(r.Person) ? ", sees them" : "")));
				RuinarchPlus.Log?.Info($"Search for {r.Person.name} ended ({reason}); party {party?.partyState.ToString() ?? "-"}: {members}; target {(r.Person.isDead ? "dead" : "alive")}{(r.Person.gridTileLocation != null && r.LastSeenTile != null ? $", {r.Person.gridTileLocation.GetDistanceTo(r.LastSeenTile):F0} tiles from the spot" : ", off map")}.");
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

		internal static string LastSeenOf(Character c)
		{
			Record r = Get(c);
			return r == null ? null : $"{r.LastSeenTile?.ToString() ?? "nowhere"} {(Now - r.LastSeenTick) / (float)TicksPerHour:F1}h ago";
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
						// The game's lookup throws for a quest that is gone (ended before the save).
						Search = !string.IsNullOrEmpty(f[8]) && DatabaseManager.Instance.partyQuestDatabase.allPartyQuests.TryGetValue(f[8], out PartyQuest q) ? q as RescuePartyQuest : null
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

	[HarmonyPatch(typeof(BuryCharacter), nameof(BuryCharacter.AfterBurySuccess))]
	internal static class Missing_Buried
	{
		private static void Postfix(ActualGoapNode goapNode)
		{
			try
			{
				MissingPersons.Buried(goapNode?.poiTarget as Character, goapNode?.actor);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Missing persons (burial) failed: " + e.Message);
			}
		}
	}
}
