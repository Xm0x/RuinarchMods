using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using Traits;
using UnityEngine;

namespace RuinarchPlus.Phase3
{
	/// <summary>
	/// Villagers only attack what they know (config: <c>knowledgeEnabled</c>).
	///
	/// In the base game, once any villager reports one demonic structure, their whole faction
	/// "knows about the player": its counterattacks are sent at the player's entire
	/// settlement, and once there the party ignores everything else and marches on the
	/// portal, even though nobody has ever seen it.
	///
	/// Now knowledge lives in people. Each villager remembers the player's buildings they
	/// have seen or been told of:
	/// - a villager who sees a structure (once their faction is aware of the player) or hears
	///   of it carries the news: their own village learns it when they are back there alive.
	///   A witness killed on the way takes the news with them;
	/// - a village knows what its living residents remember and have told at home: when news
	///   comes home, every resident remembers it. A faction knows what any of its villages
	///   knows;
	/// - news reaches the faction's other villages only when someone who remembers stands in
	///   one (a party passing through, a villager on an errand), and other factions through
	///   traders and gossip (Traders.cs, Gossip.cs);
	/// - memory dies with the villager, and elders with dementia forget (Phase4/LifeCycle.cs).
	///   Newcomers (children, migrants) are not told old news, so a village forgets once
	///   everyone who remembers is gone; the event log says so;
	/// - the game's own discovery report teaches the reporter's village.
	/// A village that borders the player's land counterattacks (the game's own trigger) only
	/// once it knows something standing next door. Counterattacks go to the nearest structure
	/// their village knows and attack only structures the village or the party knows; the
	/// portal is a target once someone has seen it. When nothing they know of is left
	/// standing, the party considers the job done and goes home. Rescues and bounty hunts into
	/// a player building ask the same memory (KnowledgeTargets.cs).
	///
	/// Memory is stored inside the player's save (Ruinarch.ModContent's ModSave). A save from
	/// 0.6 (a faction-wide ledger) hands each faction's knowledge to all of its villages; a
	/// save made before any ledger does the same for every faction already aware of the
	/// player, with all of the player's buildings, which is how the base game treated them.
	/// </summary>
	internal static class Knowledge
	{
		private const string SaveId = "ruinarch.plus.knowledge";

		// What each villager remembers of the player's buildings.
		private static readonly Dictionary<Character, HashSet<LocationStructure>> Memory = new Dictionary<Character, HashSet<LocationStructure>>();

		// The part of it they have not yet told at home (a subset of Memory).
		private static readonly Dictionary<Character, HashSet<LocationStructure>> Carried = new Dictionary<Character, HashSet<LocationStructure>>();

		// What each village knew at the last hourly check: what it has since forgotten is announced.
		private static readonly Dictionary<NPCSettlement, HashSet<LocationStructure>> LastKnown = new Dictionary<NPCSettlement, HashSet<LocationStructure>>();

		// A 0.6 save's faction-wide ledger, handed to the factions' villages at the first hour.
		private static readonly List<KeyValuePair<Faction, LocationStructure>> PendingFaction = new List<KeyValuePair<Faction, LocationStructure>>();

		// Set when a save with no ledger at all was loaded (or a new game began); see HourlyCheck.
		private static bool _legacy;

		internal static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		internal static bool Enabled => RuinarchPlusConfig.Current.knowledgeEnabled;

		private static bool Standing(LocationStructure s) => s is DemonicStructure && !s.hasBeenDestroyed;

		private static bool CanRemember(Character c) => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient();

		// ---- people ------------------------------------------------------------------------

		internal static bool Remembers(Character c, LocationStructure structure)
		{
			return c != null && Memory.TryGetValue(c, out HashSet<LocationStructure> set) && set.Contains(structure);
		}

		/// <summary>True if <paramref name="c"/> carries news of the structure not yet told at home.</summary>
		internal static bool Carries(Character c, LocationStructure structure)
		{
			return c != null && Carried.TryGetValue(c, out HashSet<LocationStructure> set) && set.Contains(structure);
		}

		private static bool Remember(Character c, LocationStructure s)
		{
			if (!Memory.TryGetValue(c, out HashSet<LocationStructure> set))
			{
				set = new HashSet<LocationStructure>();
				Memory[c] = set;
			}
			return set.Add(s);
		}

		private static bool Carry(Character c, LocationStructure s)
		{
			Remember(c, s);
			if (!Carried.TryGetValue(c, out HashSet<LocationStructure> set))
			{
				set = new HashSet<LocationStructure>();
				Carried[c] = set;
			}
			return set.Add(s);
		}

		/// <summary>Everything <paramref name="c"/> remembers that still stands.</summary>
		internal static HashSet<LocationStructure> NewsOf(Character c)
		{
			HashSet<LocationStructure> news = new HashSet<LocationStructure>();
			if (c != null && Memory.TryGetValue(c, out HashSet<LocationStructure> set))
			{
				news.UnionWith(set.Where(Standing));
			}
			return news;
		}

		/// <summary>
		/// <paramref name="c"/> learned of the structure away from home: they remember it, and
		/// carry it unless their village already knows. True if it is news they now carry.
		/// </summary>
		private static bool Learned(Character c, LocationStructure s)
		{
			if (!CanRemember(c) || c.faction == null || !Standing(s))
			{
				return false;
			}
			if (VillageKnows(c.homeSettlement as NPCSettlement, s))
			{
				Remember(c, s);
				return false;
			}
			return Carry(c, s);
		}

		/// <summary><paramref name="witness"/> has seen <paramref name="structure"/>; their village
		/// learns it once they are back there (<see cref="HourlyCheck"/>).</summary>
		internal static void Witness(Character witness, LocationStructure structure)
		{
			if (Learned(witness, structure))
			{
				RuinarchPlus.Log?.Info($"{witness.name} of {witness.faction.name} saw {structure.name}; their people will know once they are home.");
			}
		}

		/// <summary><paramref name="listener"/> was told of the structure by <paramref name="teller"/> (Gossip.cs).</summary>
		internal static void Hear(Character listener, LocationStructure structure, Character teller)
		{
			if (Learned(listener, structure))
			{
				RuinarchPlus.Log?.Info($"{teller.name} of {teller.faction?.name} told {listener.name} of {listener.faction.name} about {structure.name}.");
			}
		}

		/// <summary>Dementia (Phase4/LifeCycle.cs): <paramref name="c"/> forgets one building they
		/// remember. Returns it, or null if they remember nothing.</summary>
		internal static LocationStructure ForgetOne(Character c)
		{
			if (!Enabled || c == null || !Memory.TryGetValue(c, out HashSet<LocationStructure> set))
			{
				return null;
			}
			List<LocationStructure> standing = set.Where(Standing).ToList();
			if (standing.Count == 0)
			{
				return null;
			}
			LocationStructure s = standing[UnityEngine.Random.Range(0, standing.Count)];
			set.Remove(s);
			if (Carried.TryGetValue(c, out HashSet<LocationStructure> carried))
			{
				carried.Remove(s);
			}
			return s;
		}

		/// <summary>What <paramref name="c"/>'s active party remembers (each member's memory).</summary>
		internal static HashSet<LocationStructure> PartyKnowledge(Character c)
		{
			HashSet<LocationStructure> known = new HashSet<LocationStructure>();
			if (c == null)
			{
				return known;
			}
			IEnumerable<Character> members = c.partyComponent.hasParty && c.partyComponent.currentParty.isActive
				? (IEnumerable<Character>)c.partyComponent.currentParty.members : new[] { c };
			foreach (Character m in members)
			{
				known.UnionWith(NewsOf(m));
			}
			return known;
		}

		// ---- villages and factions ---------------------------------------------------------

		/// <summary>What <paramref name="village"/> knows: what its living residents remember and
		/// have told at home, still standing.</summary>
		internal static HashSet<LocationStructure> VillageKnowledge(NPCSettlement village)
		{
			HashSet<LocationStructure> known = new HashSet<LocationStructure>();
			if (village == null)
			{
				return known;
			}
			foreach (Character r in village.residents)
			{
				if (r == null || r.isDead || !Memory.TryGetValue(r, out HashSet<LocationStructure> set))
				{
					continue;
				}
				Carried.TryGetValue(r, out HashSet<LocationStructure> carried);
				foreach (LocationStructure s in set)
				{
					if (Standing(s) && (carried == null || !carried.Contains(s)))
					{
						known.Add(s);
					}
				}
			}
			return known;
		}

		internal static bool VillageKnows(NPCSettlement village, LocationStructure structure)
		{
			return village != null && village.residents.Any(r => r != null && !r.isDead && Remembers(r, structure) && !Carries(r, structure));
		}

		/// <summary>How many living residents of <paramref name="village"/> remember the structure.</summary>
		internal static int Rememberers(NPCSettlement village, LocationStructure structure)
		{
			return village?.residents.Count(r => r != null && !r.isDead && Remembers(r, structure)) ?? 0;
		}

		private static IEnumerable<NPCSettlement> VillagesOf(Faction faction)
		{
			return faction == null ? Enumerable.Empty<NPCSettlement>() : faction.ownedSettlements.OfType<NPCSettlement>().ToList();
		}

		/// <summary>True if any village of <paramref name="faction"/> knows the structure.</summary>
		internal static bool Knows(Faction faction, LocationStructure structure)
		{
			return VillagesOf(faction).Any(v => VillageKnows(v, structure));
		}

		/// <summary>The player's buildings any village of <paramref name="faction"/> knows that still stand.</summary>
		internal static List<LocationStructure> KnownStanding(Faction faction)
		{
			HashSet<LocationStructure> known = new HashSet<LocationStructure>();
			foreach (NPCSettlement v in VillagesOf(faction))
			{
				known.UnionWith(VillageKnowledge(v));
			}
			return known.ToList();
		}

		/// <summary>The villages of <paramref name="faction"/> that know the structure.</summary>
		internal static List<NPCSettlement> VillagesKnowing(Faction faction, LocationStructure structure)
		{
			return VillagesOf(faction).Where(v => VillageKnows(v, structure)).ToList();
		}

		/// <summary>
		/// Stands in for the game's faction-wide <c>isAwareOfPlayer</c> wherever it decides
		/// about one particular building: the village deciding (<paramref name="place"/>; the
		/// whole faction when there is none) or the one asking (<paramref name="who"/>) must know
		/// that building. With the feature off, the vanilla answer.
		/// </summary>
		internal static bool KnowsOf(Faction faction, BaseSettlement place, Character who, LocationStructure structure)
		{
			if (faction == null || !faction.isAwareOfPlayer)
			{
				return false;
			}
			if (!Enabled || Remembers(who, structure))
			{
				return true;
			}
			return place is NPCSettlement village ? VillageKnows(village, structure) : Knows(faction, structure);
		}

		/// <summary>Every resident of <paramref name="village"/> remembers the news. Returns what
		/// the village did not know before.</summary>
		private static List<LocationStructure> TellVillage(NPCSettlement village, IEnumerable<LocationStructure> news)
		{
			HashSet<LocationStructure> known = VillageKnowledge(village);
			List<LocationStructure> told = news.Where(s => Standing(s) && !known.Contains(s)).Distinct().ToList();
			foreach (Character r in village.residents.ToList())
			{
				if (!CanRemember(r))
				{
					continue;
				}
				foreach (LocationStructure s in news)
				{
					if (Standing(s))
					{
						Remember(r, s);
						if (Carried.TryGetValue(r, out HashSet<LocationStructure> carried))
						{
							carried.Remove(s);
						}
					}
				}
			}
			return told;
		}

		/// <summary>Every village of <paramref name="faction"/> learns the structure (test harness,
		/// older saves).</summary>
		internal static void Learn(Faction faction, LocationStructure structure, bool announce = true)
		{
			if (faction == null || !Standing(structure))
			{
				return;
			}
			bool learned = false;
			foreach (NPCSettlement v in VillagesOf(faction))
			{
				learned |= TellVillage(v, new[] { structure }).Count > 0;
			}
			if (learned && announce)
			{
				RuinarchPlus.Log?.Info($"{faction.name} now know of {structure.name}.");
			}
		}

		/// <summary>The game's discovery report: <paramref name="reporter"/>'s village learns it.</summary>
		internal static void Reported(Character reporter, LocationStructure structure)
		{
			if (!CanRemember(reporter) || !Standing(structure))
			{
				return;
			}
			if (reporter.homeSettlement is NPCSettlement home && TellVillage(home, new[] { structure }).Count > 0)
			{
				RuinarchPlus.Log?.Info($"{reporter.name} reported {structure.name}; {home.name} now know of it.");
			}
			else
			{
				Learned(reporter, structure);
			}
		}

		/// <summary>Forget everything the members of <paramref name="faction"/> remember (test harness).</summary>
		internal static void Forget(Faction faction)
		{
			if (faction == null)
			{
				return;
			}
			foreach (Character c in Memory.Keys.Where(c => c?.faction == faction).ToList())
			{
				Memory.Remove(c);
				Carried.Remove(c);
			}
			foreach (NPCSettlement v in VillagesOf(faction))
			{
				LastKnown.Remove(v);
			}
		}

		/// <summary>
		/// A traveller (a trader, Phase5/Traders.cs) standing in <paramref name="village"/> of
		/// another faction: they tell its people every building they remember, which that
		/// village learns at once (and its faction becomes aware of the demons if it was not),
		/// and they hear what the village knows, which they carry home.
		/// </summary>
		internal static void Exchange(Character traveller, NPCSettlement village)
		{
			Faction hosts = village?.owner;
			if (!Enabled || traveller?.faction == null || hosts == null || hosts == traveller.faction || !hosts.isMajorNonPlayer)
			{
				return;
			}
			List<LocationStructure> told = TellVillage(village, NewsOf(traveller));
			if (told.Count > 0)
			{
				RuinarchPlus.Log?.Info($"{traveller.name} told {village.name} of {string.Join(", ", Names(told))}; {village.name} now know of it.");
				if (!hosts.isAwareOfPlayer)
				{
					hosts.SetIsAwareOfPlayer(true);
					RuinarchPlus.Log?.Info($"{hosts.name} are now aware of the demons.");
				}
			}
			foreach (LocationStructure s in VillageKnowledge(village))
			{
				Hear(traveller, s, village.ruler ?? traveller);
			}
		}

		// ---- hourly ------------------------------------------------------------------------

		/// <summary>
		/// Hourly: the dead take what they remember with them; anyone standing in a village of
		/// their faction tells it what it does not know; what a village has forgotten since the
		/// last hour is announced.
		/// </summary>
		internal static void HourlyCheck()
		{
			if (_legacy)
			{
				_legacy = false;
				MigrateLegacy();
			}
			if (PendingFaction.Count > 0)
			{
				foreach (KeyValuePair<Faction, LocationStructure> kv in PendingFaction)
				{
					Learn(kv.Key, kv.Value, announce: false);
				}
				RuinarchPlus.Log?.Info($"Knowledge: this save's faction-wide knowledge ({PendingFaction.Count} entries) is now remembered by the villagers of those factions.");
				PendingFaction.Clear();
			}
			foreach (Character c in Memory.Keys.ToList())
			{
				if (c != null && !c.isDead)
				{
					continue;
				}
				if (c != null && Carried.TryGetValue(c, out HashSet<LocationStructure> news))
				{
					List<LocationStructure> lost = news.Where(s => Standing(s) && !Knows(c.faction, s)).ToList();
					if (lost.Count > 0)
					{
						RuinarchPlus.Log?.Info($"{c.name} died with news of {string.Join(", ", Names(lost))}; nobody at home knows.");
					}
				}
				Memory.Remove(c);
				Carried.Remove(c);
			}
			foreach (Character c in Memory.Keys.ToList())
			{
				if (c.faction == null)
				{
					continue;
				}
				// Where they stand: the settlement of their structure, else of their area (an area
				// can hold more than one settlement). A village of their faction, or their home.
				NPCSettlement here = c.currentSettlement as NPCSettlement ?? c.gridTileLocation?.area?.GetFirstNPCSettlementOnArea();
				if (here == null || (here.owner != c.faction && here != c.homeSettlement))
				{
					continue;
				}
				List<LocationStructure> told = TellVillage(here, NewsOf(c));
				if (here == c.homeSettlement)
				{
					Carried.Remove(c);
				}
				if (told.Count == 0)
				{
					continue;
				}
				RuinarchPlus.Log?.Info($"{c.name} brought word of {string.Join(", ", Names(told))} to {here.name}; {here.name} now know of it.");
				// News heard from another faction is how a faction that has never met the
				// player learns of them: like the game's own report, it makes them aware.
				if (!c.faction.isAwareOfPlayer)
				{
					c.faction.SetIsAwareOfPlayer(true);
					RuinarchPlus.Log?.Info($"{c.faction.name} are now aware of the demons.");
				}
			}
			AnnounceForgotten();
		}

		// A village that knew a building an hour ago and no longer does (its last rememberers
		// died, left or forgot) is announced. Destroyed buildings and emptied villages are not.
		private static void AnnounceForgotten()
		{
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (settlements == null)
			{
				return;
			}
			foreach (NPCSettlement v in settlements.OfType<NPCSettlement>().ToList())
			{
				if (v.owner == null || !v.owner.isMajorNonPlayer)
				{
					continue;
				}
				HashSet<LocationStructure> now = VillageKnowledge(v);
				if (LastKnown.TryGetValue(v, out HashSet<LocationStructure> before) && v.residents.Any(CanRemember))
				{
					foreach (LocationStructure s in before)
					{
						if (Standing(s) && !now.Contains(s))
						{
							Phase2.Curfew.Announce("Nobody in {0} remembers {1} any more.", v, s);
						}
					}
				}
				LastKnown[v] = now;
			}
		}

		internal static IEnumerable<string> Names(IEnumerable<LocationStructure> set)
		{
			foreach (LocationStructure s in set)
			{
				yield return s?.name ?? "a ruin";
			}
		}

		/// <summary>Living villagers carrying news home, with the buildings they would tell of
		/// that still stand.</summary>
		internal static List<KeyValuePair<Character, List<LocationStructure>>> Couriers()
		{
			List<KeyValuePair<Character, List<LocationStructure>>> couriers = new List<KeyValuePair<Character, List<LocationStructure>>>();
			foreach (KeyValuePair<Character, HashSet<LocationStructure>> kv in Carried)
			{
				List<LocationStructure> news = kv.Value.Where(Standing).ToList();
				if (kv.Key != null && !kv.Key.isDead && news.Count > 0)
				{
					couriers.Add(new KeyValuePair<Character, List<LocationStructure>>(kv.Key, news));
				}
			}
			return couriers;
		}

		/// <summary>The nearest of <paramref name="known"/> still standing (and attackable), or null.</summary>
		internal static LocationStructure NearestStanding(IEnumerable<LocationStructure> known, LocationGridTile from)
		{
			LocationStructure best = null;
			float bestDistance = float.MaxValue;
			foreach (LocationStructure s in known)
			{
				if (s == null || s.hasBeenDestroyed || s.objectsThatContributeToDamage.Count == 0 || s.tiles == null)
				{
					continue;
				}
				float d = from == null ? 0f : Distance(from, s);
				if (best == null || d < bestDistance)
				{
					best = s;
					bestDistance = d;
				}
			}
			return best;
		}

		private static float Distance(LocationGridTile from, LocationStructure s)
		{
			float min = float.MaxValue;
			foreach (LocationGridTile t in s.tiles)
			{
				float d = from.GetDistanceTo(t);
				if (d < min)
				{
					min = d;
				}
			}
			return min;
		}

		// A save from before any ledger: factions aware of the player knew all of the player's
		// buildings in the base game, so all their villages keep knowing them. A new game has
		// no aware faction, so nothing happens there.
		private static void MigrateLegacy()
		{
			List<LocationStructure> buildings = (PlayerManager.Instance?.player?.playerSettlement?.allStructures ?? new List<LocationStructure>())
				.Where(Standing).ToList();
			foreach (Faction f in FactionManager.Instance.allFactions)
			{
				if (f != null && f.isMajorNonPlayer && f.isAwareOfPlayer && KnownStanding(f).Count == 0)
				{
					foreach (LocationStructure s in buildings)
					{
						Learn(f, s, announce: false);
					}
					RuinarchPlus.Log?.Info($"Knowledge: {f.name} were already aware of the demons in this save; they know all {buildings.Count} of their buildings, as in the base game.");
				}
			}
		}

		// ---- persistence -------------------------------------------------------------------
		// One "characterId/structureId" string per building a villager remembers, and one per
		// piece of news they carry (also remembered). JsonUtility silently drops a list of a
		// mod-defined class (it wrote "{}"), so the file sticks to strings. Written even when
		// empty: a save without this file predates the ledger.

		private static string Save()
		{
			KnowledgeSaveData file = new KnowledgeSaveData();
			foreach (KeyValuePair<Character, HashSet<LocationStructure>> kv in Memory)
			{
				foreach (LocationStructure s in kv.Value)
				{
					if (kv.Key != null && !kv.Key.isDead && Standing(s))
					{
						file.memory.Add(kv.Key.persistentID + "/" + s.persistentID);
					}
				}
			}
			foreach (KeyValuePair<Character, HashSet<LocationStructure>> kv in Carried)
			{
				foreach (LocationStructure s in kv.Value)
				{
					if (kv.Key != null && !kv.Key.isDead && Standing(s))
					{
						file.carried.Add(kv.Key.persistentID + "/" + s.persistentID);
					}
				}
			}
			return JsonUtility.ToJson(file);
		}

		private static bool Parse(string pair, out string owner, out LocationStructure s)
		{
			int slash = pair?.IndexOf('/') ?? -1;
			owner = slash > 0 ? pair.Substring(0, slash) : null;
			s = slash > 0 ? DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(pair.Substring(slash + 1)) : null;
			return owner != null && Standing(s);
		}

		private static void Load(string json)
		{
			Memory.Clear();
			Carried.Clear();
			LastKnown.Clear();
			PendingFaction.Clear();
			_legacy = string.IsNullOrEmpty(json);
			if (string.IsNullOrEmpty(json))
			{
				RuinarchPlus.Log?.Info("Knowledge loaded: the save holds no knowledge data.");
				return;
			}
			KnowledgeSaveData file = JsonUtility.FromJson<KnowledgeSaveData>(json);
			int remembered = 0;
			foreach (string pair in file?.memory ?? new List<string>())
			{
				Character c = Parse(pair, out string id, out LocationStructure s) ? CharacterManager.Instance.GetCharacterByPersistentID(id) : null;
				if (CanRemember(c))
				{
					Remember(c, s);
					remembered++;
				}
			}
			int carried = 0;
			foreach (string pair in file?.carried ?? new List<string>())
			{
				Character c = Parse(pair, out string id, out LocationStructure s) ? CharacterManager.Instance.GetCharacterByPersistentID(id) : null;
				if (CanRemember(c))
				{
					Carry(c, s);
					carried++;
				}
			}
			foreach (string pair in file?.known ?? new List<string>())
			{
				Faction f = Parse(pair, out string id, out LocationStructure s) ? FactionManager.Instance.GetFactionByPersistentID(id) : null;
				if (f != null)
				{
					PendingFaction.Add(new KeyValuePair<Faction, LocationStructure>(f, s));
				}
			}
			RuinarchPlus.Log?.Info($"Knowledge loaded: {Memory.Count} villager(s) remember {remembered} building(s) in all; {carried} piece(s) of news on the road"
				+ (PendingFaction.Count > 0 ? $"; {PendingFaction.Count} faction-wide entries from an older save, handed out at the first hour." : "."));
		}
	}

	[Serializable]
	public class KnowledgeSaveData
	{
		public List<string> memory = new List<string>();
		public List<string> carried = new List<string>();

		// 0.6 saves: the faction-wide ledger ("factionId/structureId"), read once, never written.
		public List<string> known = new List<string>();
	}

	// Hourly (20 ticks), like the other Ruinarch+ hourly checks: witnesses back home tell.
	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class Knowledge_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			if (!Knowledge.Enabled || __instance.Today().tick % 20 != 0)
			{
				return;
			}
			try
			{
				Knowledge.HourlyCheck();
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge hourly failed: " + e.Message);
			}
		}
	}

	// A villager's report of a demonic structure (the game's discovery report) teaches it
	// to their village.
	[HarmonyPatch(typeof(ReportCorruptedStructure), nameof(ReportCorruptedStructure.AfterReportSuccess))]
	internal static class Knowledge_Reported
	{
		private static void Postfix(ActualGoapNode goapNode)
		{
			if (!Knowledge.Enabled)
			{
				return;
			}
			try
			{
				if (goapNode?.otherData != null && goapNode.otherData.Length > 0)
				{
					Knowledge.Reported(goapNode.actor, goapNode.otherData[0].obj as LocationStructure);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (report) failed: " + e.Message);
			}
		}
	}

	// Once a faction is aware of the player, a villager who sees a structure carries the news
	// home (Knowledge.Witness). (Before that, a sighting only becomes knowledge through the
	// game's own report above, which also walks home.)
	[HarmonyPatch(typeof(CharacterTrait), nameof(CharacterTrait.OnSeePOI))]
	internal static class Knowledge_Sighted
	{
		private static void Prefix(IPointOfInterest targetPOI, Character characterThatWillDoJob)
		{
			if (!Knowledge.Enabled || !(targetPOI is TileObject tileObject) || !tileObject.tileObjectType.IsDemonicStructureTileObject())
			{
				return;
			}
			try
			{
				Character c = characterThatWillDoJob;
				Faction f = c?.faction;
				if (f != null && f.isMajorNonPlayer && f.isAwareOfPlayer && c.limiterComponent.canWitness && c.race.IsSapient() && !c.isAlliedWithPlayer
					&& tileObject.gridTileLocation?.structure is DemonicStructure seen)
				{
					Knowledge.Witness(c, seen);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (sighting) failed: " + e.Message);
			}
		}
	}

	// A village whose land borders the player's counterattacks without any report
	// (SettlementPartyComponent.TryCreateCounterattackQuest, which also makes the faction
	// aware). Now only once the village knows a structure standing next door: its villagers
	// see it and report it like any other.
	[HarmonyPatch(typeof(SettlementPartyComponent), "TryCreateCounterattackQuest")]
	internal static class Knowledge_Neighbours
	{
		private static bool Prefix(SettlementPartyComponent __instance)
		{
			if (!Knowledge.Enabled)
			{
				return true;
			}
			try
			{
				NPCSettlement village = __instance.owner as NPCSettlement;
				foreach (Area area in __instance.owner.areas)
				{
					foreach (Area neighbour in area.neighbourComponent.neighbours)
					{
						if (!neighbour.HasPlayerSettlement())
						{
							continue;
						}
						foreach (LocationStructure s in neighbour.structureComponent.structures)
						{
							if (Knowledge.VillageKnows(village, s))
							{
								return true;
							}
						}
					}
				}
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (neighbours) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}

	// Counterattacks set out for the nearest structure their village knows, not "the player".
	[HarmonyPatch(typeof(CounterattackPartyQuest), nameof(CounterattackPartyQuest.GetTargetDestination))]
	internal static class Knowledge_CounterattackDestination
	{
		private static void Postfix(CounterattackPartyQuest __instance, ref IPartyTargetDestination __result)
		{
			if (!Knowledge.Enabled)
			{
				return;
			}
			try
			{
				BaseSettlement home = __instance.madeInLocation;
				Faction f = home?.owner;
				LocationGridTile from = home.areas.Count > 0 ? home.areas[0].gridTileComponent.centerGridTile : null;
				IEnumerable<LocationStructure> known = home is NPCSettlement village ? Knowledge.VillageKnowledge(village) : (IEnumerable<LocationStructure>)Knowledge.KnownStanding(f);
				LocationStructure target = Knowledge.NearestStanding(known, from);
				if (target != null)
				{
					__result = target;
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (destination) failed: " + e.Message);
			}
		}
	}

	// On site, a counterattack party attacks the nearest structure it knows (its village's
	// knowledge and what its members remember) instead of marching on the portal
	// (AttackDemonicStructureBehaviour hard-codes THE_PORTAL).
	[HarmonyPatch(typeof(AttackDemonicStructureBehaviour), nameof(AttackDemonicStructureBehaviour.TryDoBehaviour))]
	internal static class Knowledge_CounterattackTarget
	{
		private static bool Prefix(Character character, ref JobQueueItem producedJob, ref bool __result)
		{
			if (!Knowledge.Enabled || !character.partyComponent.hasParty)
			{
				return true;
			}
			Party party = character.partyComponent.currentParty;
			// Every active party runs this, whatever its quest: in the base game a party that gets
			// here marches on the Portal.
			if (party == null || !party.isActive)
			{
				return true;
			}
			try
			{
				producedJob = null;
				__result = true;
				HashSet<LocationStructure> known = Knowledge.PartyKnowledge(character);
				known.UnionWith(Knowledge.VillageKnowledge(party.partySettlement as NPCSettlement ?? character.homeSettlement as NPCSettlement));
				LocationStructure target = Knowledge.NearestStanding(known, character.gridTileLocation);
				if (target == null)
				{
					// Nothing they know of is left standing: job done (mirrors the vanilla
					// "portal destroyed" branch).
					PartyQuest quest = party.currentQuest;
					quest?.SetIsSuccessful(state: true);
					if (party.targetDestination != party.partySettlement)
					{
						party.GoBackHomeAndEndQuest();
					}
					else
					{
						quest?.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Finished_Quest"));
					}
					return false;
				}
				if (party.partyState != PARTY_STATE.Working)
				{
					__result = false;
					return false;
				}
				if (target.GetNearestDamageableThatContributeToHP(character.gridTileLocation) is TileObject damageable)
				{
					character.combatComponent.Fight(damageable, "Clear_Demonic_Intrusion");
				}
				else
				{
					party.GoBackHomeAndEndQuest();
				}
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (attack target) failed, using vanilla: " + e.Message);
				return true;
			}
		}
	}
}
