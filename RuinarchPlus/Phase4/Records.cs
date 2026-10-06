using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using RuinarchPlus.Phase2;
using RuinarchPlus.Phase3;
using RuinarchPlus.Phase5;
using UnityEngine;

namespace RuinarchPlus.Phase4
{
	/// <summary>
	/// Records (config: <c>recordsEnabled</c>, with <c>knowledgeEnabled</c>).
	///
	/// Knowledge of the player's buildings lives in people (Phase3/Knowledge.cs) and now also in
	/// records, kept in dwellings and in a village's Library. A record is carried by the
	/// building's Book Shelves, or by Books the mod places when it has none.
	/// - In their free time, a villager at home who remembers a building the household's
	///   record does not name walks to a carrier and writes it (the Write action, an hour); a
	///   household with no carrier yet gets a Book.
	/// - At home, a villager who does not remember something the record names may read it
	///   (<c>readChance</c> % a free-time hour; the Read action, an hour).
	/// - A Town or City (Phase5/SettlementTiers.cs) builds a Library with the bundled layout
	///   and Workshop-derived behaviour; its Book Shelves carry its record.
	///   A villager with something to write there or to learn from it goes to write or read
	///   (<c>libraryVisitChance</c> % a free-time hour), never under curfew.
	/// - Each finished action is logged on the carrier and the villager (RecordActions.cs).
	///   Records never count by themselves: only a reader turns them back into knowledge.
	/// - A record whose carriers are all burned or broken is lost; a Library's loss is
	///   announced. A record is only ever rewritten from living memory.
	/// Records are stored inside the player's save (<c>ModData/ruinarch.plus.records.json</c>).
	/// </summary>
	internal static class Records
	{
		public const string LibraryId = "ruinarch.plus.library";
		private const string SaveId = "ruinarch.plus.records";

		private sealed class Record
		{
			internal LocationStructure Holder;
			internal readonly List<TileObject> Carriers = new List<TileObject>();
			internal readonly HashSet<LocationStructure> Entries = new HashSet<LocationStructure>();
			internal bool IsLibrary => Holder is Library;
		}

		internal static ModBuilding LibraryBuilding;

		private static readonly Dictionary<LocationStructure, Record> ByHolder = new Dictionary<LocationStructure, Record>();

		// The game hour each villager last decided whether to write or read.
		private static readonly Dictionary<Character, long> Decided = new Dictionary<Character, long>();

		internal static bool Enabled => Knowledge.Enabled && RuinarchPlusConfig.Current.recordsEnabled;

		private static long Hour => MissingPersons.Now / GameManager.ticksPerHour;

		internal static void Register()
		{
			try
			{
				ModContent.RegisterStructure(new StructureRegistration
				{
					Id = LibraryId,
					DisplayName = "Library",
					Factory = (type, region) => new Library(type, region),
					LoadFactory = (type, region, save) => new Library(region, (SaveDataManMadeStructure)save),
					// Native behaviour comes from the Workshop; LibraryLook supplies the authored layout.
					PrefabSource = STRUCTURE_TYPE.WORKSHOP,
					Skill = null,
					UnlockWith = PLAYER_SKILL_TYPE.NONE,
					IsPlayerStructure = false,
					IsVillageStructure = true
				});
				LibraryBuilding = ModBuildings.Add(LibraryId, "Library", STRUCTURE_TYPE.WORKSHOP);
				RecordActions.Register();
				ModSave.Register(SaveId, Save, Load);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Records registration failed: " + e);
			}
		}

		// ---- queries -----------------------------------------------------------------------

		/// <summary>The buildings the record in <paramref name="holder"/> (a dwelling or a
		/// Library) names, or null if it keeps none.</summary>
		internal static HashSet<LocationStructure> RecordOf(LocationStructure holder)
		{
			return holder != null && ByHolder.TryGetValue(holder, out Record r) ? new HashSet<LocationStructure>(r.Entries) : null;
		}

		/// <summary>The standing carriers (Book Shelves or Books) of the record in
		/// <paramref name="holder"/>.</summary>
		internal static List<TileObject> CarriersOf(LocationStructure holder)
		{
			return holder != null && ByHolder.TryGetValue(holder, out Record r) ? r.Carriers.Where(t => Stands(t, holder)).ToList() : new List<TileObject>();
		}

		/// <summary>Homes of <paramref name="faction"/> whose records name something, and the
		/// villages whose Library does (the bookmarks panel).</summary>
		internal static void Summary(Faction faction, out int homes, out List<NPCSettlement> libraries)
		{
			homes = 0;
			libraries = new List<NPCSettlement>();
			if (!Enabled || faction == null)
			{
				return;
			}
			foreach (Record r in ByHolder.Values)
			{
				if (r.Entries.Count == 0 || r.Holder.hasBeenDestroyed || !(r.Holder.settlementLocation is NPCSettlement v) || v.owner != faction)
				{
					continue;
				}
				if (r.IsLibrary)
				{
					libraries.Add(v);
				}
				else
				{
					homes++;
				}
			}
		}

		private static bool Stands(TileObject t, LocationStructure holder)
		{
			return t != null && t.gridTileLocation != null && t.gridTileLocation.structure == holder && t.mapObjectState == MAP_OBJECT_STATE.BUILT;
		}

		/// <summary>The dwelling or Library a Book Shelf or Book stands in, or null.</summary>
		private static LocationStructure HolderOf(TileObject t)
		{
			if (t == null || (t.tileObjectType != TILE_OBJECT_TYPE.SHELF_BOOKS && t.tileObjectType != TILE_OBJECT_TYPE.BOOK))
			{
				return null;
			}
			LocationStructure s = t.gridTileLocation?.structure;
			return s != null && !s.hasBeenDestroyed && (s is Library || s.structureType == STRUCTURE_TYPE.DWELLING) && Stands(t, s) ? s : null;
		}

		/// <summary>True if <paramref name="t"/> can carry a record: a built Book Shelf or Book in
		/// a dwelling or a Library.</summary>
		internal static bool IsCarrier(TileObject t)
		{
			return Enabled && HolderOf(t) != null;
		}

		// What c remembers (and has told at home) that the record does not name yet.
		private static List<LocationStructure> Unwritten(Character c, LocationStructure holder)
		{
			ByHolder.TryGetValue(holder, out Record r);
			return Knowledge.NewsOf(c).Where(s => !Knowledge.Carries(c, s) && (r == null || !r.Entries.Contains(s))).ToList();
		}

		// What the record names that c does not remember; nothing once its carriers are gone.
		private static List<LocationStructure> Unread(Character c, LocationStructure holder)
		{
			if (!ByHolder.TryGetValue(holder, out Record r) || !r.Carriers.Any(t => Stands(t, holder)))
			{
				return new List<LocationStructure>();
			}
			return r.Entries.Where(s => Knowledge.Standing(s) && !Knowledge.Remembers(c, s)).ToList();
		}

		// ---- writing and reading -----------------------------------------------------------

		/// <summary><paramref name="c"/> writes into the record in <paramref name="holder"/> every
		/// standing building they remember and have told at home. A record with no carrier
		/// left starts again on the holder's Book Shelves, or a new Book (none if there is no
		/// free spot inside). True if anything new was written.</summary>
		internal static bool Write(Character c, LocationStructure holder)
		{
			List<LocationStructure> news = Unwritten(c, holder);
			if (news.Count == 0)
			{
				return false;
			}
			Record r = Ensure(holder);
			if (r == null)
			{
				return false;
			}
			bool first = !r.IsLibrary && r.Entries.Count == 0;
			r.Entries.UnionWith(news);
			if (first && holder.settlementLocation is NPCSettlement village)
			{
				Curfew.Note("A household in {0} started keeping a record of your buildings.", village);
			}
			return true;
		}

		/// <summary><paramref name="c"/> reads the record in <paramref name="holder"/>: they
		/// remember every building it names. Returns what they learned.</summary>
		internal static List<LocationStructure> ReadFrom(Character c, LocationStructure holder)
		{
			List<LocationStructure> learned = new List<LocationStructure>();
			foreach (LocationStructure s in Unread(c, holder))
			{
				if (Knowledge.Read(c, s))
				{
					learned.Add(s);
				}
			}
			return learned;
		}

		/// <summary>The Write action finished (RecordActions.cs).</summary>
		internal static void Wrote(Character c, TileObject carrier)
		{
			LocationStructure holder = HolderOf(carrier);
			if (!Enabled || holder == null)
			{
				return;
			}
			List<LocationStructure> news = Unwritten(c, holder);
			if (Write(c, holder))
			{
				RuinarchPlus.Log?.Info($"{c.name} wrote of {Your(news)} {Where(carrier, holder, false)}.");
			}
		}

		/// <summary>The Read action finished (RecordActions.cs).</summary>
		internal static void ReadAt(Character c, TileObject carrier)
		{
			LocationStructure holder = HolderOf(carrier);
			if (!Enabled || holder == null)
			{
				return;
			}
			List<LocationStructure> learned = ReadFrom(c, holder);
			if (learned.Count > 0)
			{
				RuinarchPlus.Log?.Info($"{c.name} read of {Your(learned)} {Where(carrier, holder, false)}.");
			}
		}

		/// <summary>The Write action's log, asked for when it starts; null (no log) when there
		/// is nothing to write.</summary>
		internal static string DescribeWrite(Character c, TileObject carrier)
		{
			LocationStructure holder = HolderOf(carrier);
			List<LocationStructure> news = holder == null ? null : Unwritten(c, holder);
			return news == null || news.Count == 0 ? null : $"{c.uiString} wrote of {Your(news)} {Where(carrier, holder, true)}.";
		}

		/// <summary>The Read action's log; null when there is nothing to learn.</summary>
		internal static string DescribeRead(Character c, TileObject carrier)
		{
			LocationStructure holder = HolderOf(carrier);
			List<LocationStructure> unread = holder == null ? null : Unread(c, holder);
			return unread == null || unread.Count == 0 ? null : $"{c.uiString} read of {Your(unread)} {Where(carrier, holder, true)}.";
		}

		// "in a book on the Book Shelf", "in the Book", "... of Andorlad's Library".
		private static string Where(TileObject carrier, LocationStructure holder, bool link)
		{
			string at = carrier.tileObjectType == TILE_OBJECT_TYPE.SHELF_BOOKS ? "in a book on the " + carrier.name : "in the " + carrier.name;
			if (holder is Library && holder.settlementLocation is NPCSettlement v)
			{
				return $"{at} of {(link ? v.uiString : v.name)}'s Library";
			}
			return at + " at home";
		}

		// ---- carriers ----------------------------------------------------------------------

		// A Book goes on a free walkable tile inside, never one of the last two: villagers
		// must still be able to walk in (a Workshop-sized Library has only a few).
		private const int KeepFree = 2;

		private static List<TileObject> ShelvesIn(LocationStructure holder)
		{
			return holder.GetTileObjectsOfType(TILE_OBJECT_TYPE.SHELF_BOOKS)?.Where(t => Stands(t, holder)).ToList() ?? new List<TileObject>();
		}

		private static TileObject PlaceBook(LocationStructure holder)
		{
			List<LocationGridTile> free = holder.passableTiles?.Where(t => t != null && t.structure == holder && !t.isOccupied).ToList();
			if (free == null || free.Count <= KeepFree)
			{
				return null;
			}
			TileObject book = InnerMapManager.Instance.CreateNewTileObject<TileObject>(TILE_OBJECT_TYPE.BOOK);
			return holder.AddPOI(book, free[UnityEngine.Random.Range(0, free.Count)]) ? book : null;
		}

		/// <summary>
		/// Forget carriers that no longer stand. With none left the record is lost: a Library's
		/// loss is announced and its entries go (the Library stays known, so it is not furnished
		/// again). True if the record has no carrier.
		/// </summary>
		private static bool Bare(Record r)
		{
			r.Carriers.RemoveAll(t => !Stands(t, r.Holder));
			if (r.Carriers.Count > 0)
			{
				return false;
			}
			if (r.IsLibrary && r.Entries.Count > 0 && r.Holder.settlementLocation is NPCSettlement village)
			{
				Curfew.Announce("{0}'s Library has lost the last of its books; its records of " + Your(r.Entries.ToList()) + " are lost.", village);
			}
			r.Entries.Clear();
			return true;
		}

		/// <summary>The record in <paramref name="holder"/> with at least one carrier: its Book
		/// Shelves, else a new Book. Null if it has neither and no room for a Book.</summary>
		private static Record Ensure(LocationStructure holder)
		{
			if (!ByHolder.TryGetValue(holder, out Record r))
			{
				r = new Record { Holder = holder };
			}
			if (Bare(r))
			{
				r.Carriers.AddRange(ShelvesIn(holder));
				if (r.Carriers.Count == 0)
				{
					TileObject book = PlaceBook(holder);
					if (book == null)
					{
						return null;
					}
					r.Carriers.Add(book);
				}
			}
			ByHolder[holder] = r;
			return r;
		}

		/// <summary>A standing carrier of the record in <paramref name="holder"/>, chosen at
		/// random. With <paramref name="start"/>, a holder without one gets its Book Shelves or a
		/// new Book first (for writing). Null if there is none.</summary>
		internal static TileObject CarrierFor(LocationStructure holder, bool start)
		{
			Record r = start ? Ensure(holder) : ByHolder.TryGetValue(holder, out Record found) ? found : null;
			List<TileObject> standing = r?.Carriers.Where(t => Stands(t, r.Holder)).ToList();
			return standing == null || standing.Count == 0 ? null : standing[UnityEngine.Random.Range(0, standing.Count)];
		}

		// A Library seen built for the first time gets its carriers: its Book Shelves, or
		// libraryBooks Books.
		private static void Furnish(Library library)
		{
			if (ByHolder.ContainsKey(library))
			{
				return;
			}
			Record r = new Record { Holder = library };
			r.Carriers.AddRange(ShelvesIn(library));
			bool shelves = r.Carriers.Count > 0;
			for (int i = 0; !shelves && i < RuinarchPlusConfig.Current.libraryBooks; i++)
			{
				TileObject book = PlaceBook(library);
				if (book == null)
				{
					break;
				}
				r.Carriers.Add(book);
			}
			ByHolder[library] = r;
			RuinarchPlus.Log?.Info($"The Library of {library.settlementLocation?.name ?? "a village"} holds {r.Carriers.Count} {(shelves ? "Book Shelf(s)" : "Book(s)")}.");
		}

		// ---- the Library -------------------------------------------------------------------

		private static bool NeedsLibrary(NPCSettlement s)
		{
			return s.owner != null && s.owner.isMajorFaction && s.cityCenter != null
				&& SettlementTiers.Get(s) != SettlementTiers.Tier.Village
				&& Library.FindFor(s) == null
				&& !ModBuildings.HasPendingFor(s, LibraryBuilding)
				// The game allows one blueprint job per settlement at a time: wait our turn.
				&& !s.HasJob(JOB_TYPE.PLACE_BLUEPRINT);
		}

		/// <summary>Build a Library instantly (debug menu, test harness), with its carriers. Null
		/// if the village has no room.</summary>
		internal static LocationStructure InstantBuildLibrary(NPCSettlement settlement)
		{
			Library existing = Library.FindFor(settlement);
			if (existing != null)
			{
				return existing;
			}
			Library built = ModBuildings.InstantBuild(settlement, LibraryBuilding, STRUCTURE_TYPE.WORKSHOP) as Library;
			if (built != null)
			{
				Furnish(built);
			}
			return built;
		}

		/// <summary>Empty every record kept by <paramref name="faction"/>'s villages (test harness);
		/// the carriers stay, blank, so a Library is not furnished again.</summary>
		internal static void Forget(Faction faction)
		{
			foreach (Record r in ByHolder.Values.Where(r => r.Holder.settlementLocation is NPCSettlement v && v.owner == faction))
			{
				r.Entries.Clear();
			}
		}

		internal static void OnLibraryLost(Library library, NPCSettlement settlement)
		{
			if (!ByHolder.TryGetValue(library, out Record r))
			{
				return;
			}
			ByHolder.Remove(library);
			List<LocationStructure> lost = r.Entries.Where(Knowledge.Standing).ToList();
			if (Enabled && settlement != null && lost.Count > 0)
			{
				Curfew.Announce("{0}'s Library was destroyed; its records of " + Your(lost) + " are lost.", settlement);
			}
		}

		// "your Portal", "your Portal and Corrupt Kennel", "your Portal, Kennel and Lair".
		private static string Your(List<LocationStructure> structures)
		{
			List<string> names = Knowledge.Names(structures).ToList();
			return "your " + (names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1]);
		}

		// ---- hourly ------------------------------------------------------------------------

		internal static void HourlyCheck()
		{
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (!Enabled || settlements == null)
			{
				return;
			}
			Prune();
			foreach (NPCSettlement village in settlements.OfType<NPCSettlement>().ToList())
			{
				if (village.locationType != LOCATION_TYPE.VILLAGE || village.owner == null || !village.owner.isMajorNonPlayer)
				{
					continue;
				}
				try
				{
					Visit(village);
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Records check failed for {village.name}: {e.Message}");
				}
			}
		}

		private static void Visit(NPCSettlement village)
		{
			if (RuinarchPlusConfig.Current.settlementTiersEnabled && NeedsLibrary(village))
			{
				string prefab = ModBuildings.QueueBlueprint(village, LibraryBuilding, STRUCTURE_TYPE.WORKSHOP);
				if (prefab != null)
				{
					RuinarchPlus.Log?.Info($"{village.name} is a {SettlementTiers.Get(village)}: queued a Library blueprint ({prefab}).");
				}
			}
			Library library = Library.FindFor(village);
			if (library != null)
			{
				Furnish(library);
			}
		}

		// Holders destroyed, entries destroyed, carriers gone. A household whose record lost its
		// last carrier forgets the record; a Library keeps an empty one (see Bare).
		private static void Prune()
		{
			foreach (Character c in Decided.Keys.Where(c => c == null || c.isDead).ToList())
			{
				Decided.Remove(c);
			}
			foreach (Record r in ByHolder.Values.ToList())
			{
				if (r.Holder.hasBeenDestroyed)
				{
					ByHolder.Remove(r.Holder);
					continue;
				}
				r.Entries.RemoveWhere(s => !Knowledge.Standing(s));
				if (Bare(r) && !r.IsLibrary)
				{
					ByHolder.Remove(r.Holder);
				}
			}
		}

		// ---- free time ---------------------------------------------------------------------

		/// <summary>
		/// Called every tick for every idle villager (Records_FreeTime). In free time, at most
		/// once a game hour: at home, write what the household's record lacks, else maybe
		/// (<c>readChance</c>) read what it has that they do not remember; otherwise maybe
		/// (<c>libraryVisitChance</c>, not under curfew) go to the Library to write or read.
		/// True with the action and the carrier to do it at.
		/// </summary>
		internal static bool JobFor(Character c, out INTERACTION_TYPE action, out TileObject carrier)
		{
			action = INTERACTION_TYPE.NONE;
			carrier = null;
			if (!Enabled || !(c?.homeSettlement is NPCSettlement home)
				|| c.dailyScheduleComponent.schedule.GetScheduleType(GameManager.Instance.currentTick) != DAILY_SCHEDULE.Free_Time)
			{
				return false;
			}
			long hour = Hour;
			if (Decided.TryGetValue(c, out long decided) && decided == hour)
			{
				return false;
			}
			Decided[c] = hour;
			if (!Knowledge.CanRemember(c) || !Knowledge.Counts(c))
			{
				return false;
			}
			LocationStructure dwelling = c.homeStructure;
			if (dwelling != null && c.currentStructure == dwelling && dwelling.structureType == STRUCTURE_TYPE.DWELLING && !dwelling.hasBeenDestroyed)
			{
				if (Unwritten(c, dwelling).Count > 0)
				{
					carrier = CarrierFor(dwelling, start: true);
					action = RecordActions.Write;
					return carrier != null;
				}
				if (Unread(c, dwelling).Count > 0 && UnityEngine.Random.Range(0, 100) < RuinarchPlusConfig.Current.readChance)
				{
					carrier = CarrierFor(dwelling, start: false);
					action = RecordActions.Read;
					return carrier != null;
				}
			}
			Library library = Library.FindFor(home);
			if (library == null || Curfew.Binds(c))
			{
				return false;
			}
			bool write = Unwritten(c, library).Count > 0;
			if ((!write && Unread(c, library).Count == 0) || UnityEngine.Random.Range(0, 100) >= RuinarchPlusConfig.Current.libraryVisitChance)
			{
				return false;
			}
			carrier = CarrierFor(library, start: write);
			action = write ? RecordActions.Write : RecordActions.Read;
			return carrier != null;
		}

		/// <summary>Queue the Write or Read job for <paramref name="c"/> at <paramref name="carrier"/>
		/// (free time, test harness).</summary>
		internal static void Plan(Character c, INTERACTION_TYPE action, TileObject carrier)
		{
			c.PlanIdle(JOB_TYPE.IDLE, action, carrier);
		}

		// ---- persistence -------------------------------------------------------------------
		// One "kind|holderId|carrierId,carrierId|structureId,structureId" string per record, kind
		// H (a home) or L (a Library). Carriers are Book Shelves or Books. Strings only
		// (JsonUtility drops lists of mod classes). Always written: a missing file means a save
		// from before records.

		private static string Save()
		{
			RecordsSaveData file = new RecordsSaveData();
			foreach (Record r in ByHolder.Values)
			{
				if (r.Holder == null || r.Holder.hasBeenDestroyed)
				{
					continue;
				}
				string carriers = string.Join(",", r.Carriers.Where(t => Stands(t, r.Holder)).Select(t => t.persistentID));
				string entries = string.Join(",", r.Entries.Where(Knowledge.Standing).Select(s => s.persistentID));
				file.records.Add($"{(r.IsLibrary ? "L" : "H")}|{r.Holder.persistentID}|{carriers}|{entries}");
			}
			return JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			ByHolder.Clear();
			Decided.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			RecordsSaveData file = JsonUtility.FromJson<RecordsSaveData>(json);
			int entries = 0;
			foreach (string line in file?.records ?? new List<string>())
			{
				string[] parts = line.Split('|');
				LocationStructure holder = parts.Length == 4 ? DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(parts[1]) : null;
				if (holder == null || holder.hasBeenDestroyed)
				{
					continue;
				}
				Record r = new Record { Holder = holder };
				foreach (string id in parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					TileObject carrier = DatabaseManager.Instance.tileObjectDatabase.GetTileObjectByPersistentIDSafe(id);
					if (Stands(carrier, holder))
					{
						r.Carriers.Add(carrier);
					}
				}
				foreach (string id in parts[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					LocationStructure s = DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(id);
					if (Knowledge.Standing(s))
					{
						r.Entries.Add(s);
					}
				}
				if (r.Carriers.Count == 0 && !r.IsLibrary)
				{
					continue;
				}
				if (r.Carriers.Count == 0)
				{
					r.Entries.Clear();
				}
				ByHolder[holder] = r;
				entries += r.Entries.Count;
			}
			RuinarchPlus.Log?.Info($"Records loaded: {ByHolder.Values.Count(r => !r.IsLibrary)} home(s) and {ByHolder.Values.Count(r => r.IsLibrary)} Library(ies) keep {entries} entr(ies) in all.");
		}
	}

	[Serializable]
	public class RecordsSaveData
	{
		public List<string> records = new List<string>();
	}

	// Free time: write or read at home, or go to the Library to. Only runs while the villager
	// is idle (the game plans behaviour only then), so needs, work and combat come first. Runs
	// before the curfew's prefix (which keeps villagers in) so a villager kept home still
	// writes and reads there; the Library is never chosen under curfew.
	[HarmonyPatch(typeof(BehaviourComponent), nameof(BehaviourComponent.RunBehaviour))]
	[HarmonyPriority(Priority.High)]
	internal static class Records_FreeTime
	{
		private static bool Prefix(BehaviourComponent __instance, ref string __result)
		{
			try
			{
				Character c = __instance.owner;
				if (!Records.JobFor(c, out INTERACTION_TYPE action, out TileObject carrier))
				{
					return true;
				}
				Records.Plan(c, action, carrier);
				__result = action == RecordActions.Write ? "Going to write." : "Going to read.";
				return false;
			}
			catch
			{
				return true;
			}
		}
	}
}
