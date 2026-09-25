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
	/// Now each faction keeps a ledger of the demonic structures it actually knows about:
	/// - a villager's report of a structure (the game's own discovery report) teaches it;
	/// - once the faction is aware of the player, a villager who sees a structure carries the
	///   news: the faction learns it when the witness reaches a village of their faction
	///   alive. Until then only the witness (and their party) knows. A witness killed on the
	///   way takes the news with them;
	/// - a village that borders the player's land counterattacks (the game's own trigger)
	///   only once it knows something standing next door.
	/// Counterattacks go to the nearest structure the faction knows and attack only known
	/// structures; the portal is a target once someone has seen it. When nothing they know
	/// of is left standing, the party considers the job done and goes home. Rescues and
	/// bounty hunts into a player building ask the same ledger (KnowledgeTargets.cs).
	///
	/// The ledger is stored inside the player's save (Ruinarch.ModContent's ModSave). A save
	/// made before this feature has no ledger: at the first in-game hour after loading it,
	/// every faction already aware of the player learns all of the player's buildings, which
	/// is how the base game treated them. From then on nobody attacks what they don't know,
	/// and a party that knows nothing still standing goes home.
	/// </summary>
	internal static class Knowledge
	{
		private const string SaveId = "ruinarch.plus.knowledge";

		private static readonly Dictionary<Faction, HashSet<LocationStructure>> Known = new Dictionary<Faction, HashSet<LocationStructure>>();

		// News seen by a villager, not yet brought home.
		private static readonly Dictionary<Character, HashSet<LocationStructure>> Carried = new Dictionary<Character, HashSet<LocationStructure>>();

		// Set when a save with no ledger at all was loaded (or a new game began); see HourlyCheck.
		private static bool _legacy;

		internal static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		internal static bool Enabled => RuinarchPlusConfig.Current.knowledgeEnabled;

		internal static void Learn(Faction faction, LocationStructure structure, bool announce = true)
		{
			if (faction == null || !(structure is DemonicStructure) || structure.hasBeenDestroyed)
			{
				return;
			}
			if (!Known.TryGetValue(faction, out HashSet<LocationStructure> set))
			{
				set = new HashSet<LocationStructure>();
				Known[faction] = set;
			}
			if (set.Add(structure) && announce)
			{
				RuinarchPlus.Log?.Info($"{faction.name} now know of {structure.name}.");
			}
		}

		internal static bool Knows(Faction faction, LocationStructure structure)
		{
			return faction != null && Known.TryGetValue(faction, out HashSet<LocationStructure> set) && set.Contains(structure);
		}

		/// <summary>
		/// Stands in for the game's faction-wide <c>isAwareOfPlayer</c> wherever it decides
		/// about one particular building: the faction must know that building. With the
		/// feature off, the vanilla answer.
		/// </summary>
		internal static bool KnowsOf(Faction faction, LocationStructure structure)
		{
			return faction != null && faction.isAwareOfPlayer && (!Enabled || Knows(faction, structure));
		}

		internal static void Forget(Faction faction)
		{
			if (faction != null)
			{
				Known.Remove(faction);
				foreach (Character c in new List<Character>(Carried.Keys))
				{
					if (c.faction == faction)
					{
						Carried.Remove(c);
					}
				}
			}
		}

		/// <summary>
		/// <paramref name="witness"/> has seen <paramref name="structure"/>. Their faction learns
		/// it once they are back in one of its villages (<see cref="HourlyCheck"/>).
		/// </summary>
		internal static void Witness(Character witness, LocationStructure structure)
		{
			if (Carry(witness, structure))
			{
				RuinarchPlus.Log?.Info($"{witness.name} of {witness.faction.name} saw {structure.name}; their people will know once they are home.");
			}
		}

		/// <summary><paramref name="listener"/> was told of the structure by <paramref name="teller"/> (Gossip.cs).</summary>
		internal static void Hear(Character listener, LocationStructure structure, Character teller)
		{
			if (Carry(listener, structure))
			{
				RuinarchPlus.Log?.Info($"{teller.name} of {teller.faction?.name} told {listener.name} of {listener.faction.name} about {structure.name}.");
			}
		}

		private static bool Carry(Character c, LocationStructure structure)
		{
			if (c?.faction == null || !(structure is DemonicStructure) || structure.hasBeenDestroyed || Knows(c.faction, structure))
			{
				return false;
			}
			if (!Carried.TryGetValue(c, out HashSet<LocationStructure> set))
			{
				set = new HashSet<LocationStructure>();
				Carried[c] = set;
			}
			return set.Add(structure);
		}

		/// <summary>Everything <paramref name="c"/> could tell: their faction's ledger and their own news.</summary>
		internal static HashSet<LocationStructure> NewsOf(Character c)
		{
			HashSet<LocationStructure> news = new HashSet<LocationStructure>();
			if (c?.faction != null && Known.TryGetValue(c.faction, out HashSet<LocationStructure> known))
			{
				news.UnionWith(known);
			}
			if (c != null && Carried.TryGetValue(c, out HashSet<LocationStructure> carried))
			{
				news.UnionWith(carried);
			}
			news.RemoveWhere(s => s == null || s.hasBeenDestroyed);
			return news;
		}

		/// <summary>
		/// A traveller (a trader, Phase5/Traders.cs) standing in <paramref name="village"/>: they
		/// tell its people every building they know of, which that faction learns at once (and
		/// becomes aware of the demons if it was not), and they hear what the village knows,
		/// which they carry home.
		/// </summary>
		internal static void Exchange(Character traveller, NPCSettlement village)
		{
			Faction hosts = village?.owner;
			if (!Enabled || traveller?.faction == null || hosts == null || hosts == traveller.faction || !hosts.isMajorNonPlayer)
			{
				return;
			}
			List<LocationStructure> told = new List<LocationStructure>();
			foreach (LocationStructure s in NewsOf(traveller))
			{
				if (!Knows(hosts, s))
				{
					Learn(hosts, s, announce: false);
					told.Add(s);
				}
			}
			if (told.Count > 0)
			{
				RuinarchPlus.Log?.Info($"{traveller.name} told {village.name} of {string.Join(", ", Names(told))}; {hosts.name} now know of it.");
				if (!hosts.isAwareOfPlayer)
				{
					hosts.SetIsAwareOfPlayer(true);
					RuinarchPlus.Log?.Info($"{hosts.name} are now aware of the demons.");
				}
			}
			if (Known.TryGetValue(hosts, out HashSet<LocationStructure> theirs))
			{
				foreach (LocationStructure s in new List<LocationStructure>(theirs))
				{
					Hear(traveller, s, village.ruler ?? traveller);
				}
			}
		}

		/// <summary>True if <paramref name="c"/> carries unreported news of the structure.</summary>
		internal static bool Carries(Character c, LocationStructure structure)
		{
			return c != null && Carried.TryGetValue(c, out HashSet<LocationStructure> set) && set.Contains(structure);
		}

		/// <summary>What <paramref name="c"/>'s active party has seen and not yet brought home.</summary>
		internal static HashSet<LocationStructure> PartySightings(Character c)
		{
			HashSet<LocationStructure> seen = new HashSet<LocationStructure>();
			if (c == null)
			{
				return seen;
			}
			IEnumerable<Character> members = c.partyComponent.hasParty && c.partyComponent.currentParty.isActive
				? (IEnumerable<Character>)c.partyComponent.currentParty.members : new[] { c };
			foreach (Character m in members)
			{
				if (m != null && Carried.TryGetValue(m, out HashSet<LocationStructure> set))
				{
					seen.UnionWith(set);
				}
			}
			return seen;
		}

		/// <summary>
		/// Hourly: a witness standing in a village of their faction tells it what they saw. A
		/// dead witness's news is lost.
		/// </summary>
		internal static void HourlyCheck()
		{
			if (_legacy)
			{
				_legacy = false;
				MigrateLegacy();
			}
			if (Carried.Count == 0)
			{
				return;
			}
			foreach (Character c in new List<Character>(Carried.Keys))
			{
				if (c == null || c.isDead || c.faction == null)
				{
					if (c != null)
					{
						RuinarchPlus.Log?.Info($"{c.name} {(c.isDead ? "died" : "left their faction")} with news of {string.Join(", ", Names(Carried[c]))}; nobody else knows.");
					}
					Carried.Remove(c);
					continue;
				}
				// Where they stand: the settlement of their structure, else of their area (an area
				// can hold more than one settlement). Home is their faction's village or their own.
				NPCSettlement here = c.currentSettlement as NPCSettlement ?? c.gridTileLocation?.area?.GetFirstNPCSettlementOnArea();
				if (here == null || (here.owner != c.faction && here != c.homeSettlement))
				{
					continue;
				}
				HashSet<LocationStructure> news = Carried[c];
				Carried.Remove(c);
				List<LocationStructure> told = new List<LocationStructure>();
				foreach (LocationStructure s in news)
				{
					if (s != null && !s.hasBeenDestroyed && !Knows(c.faction, s))
					{
						Learn(c.faction, s, announce: false);
						told.Add(s);
					}
				}
				if (told.Count == 0)
				{
					continue;
				}
				RuinarchPlus.Log?.Info($"{c.name} brought word of {string.Join(", ", Names(told))} to {here.name}; {c.faction.name} now know of it.");
				// News heard from another faction is how a faction that has never met the
				// player learns of them: like the game's own report, it makes them aware.
				if (!c.faction.isAwareOfPlayer)
				{
					c.faction.SetIsAwareOfPlayer(true);
					RuinarchPlus.Log?.Info($"{c.faction.name} are now aware of the demons.");
				}
			}
		}

		internal static IEnumerable<string> Names(IEnumerable<LocationStructure> set)
		{
			foreach (LocationStructure s in set)
			{
				yield return s?.name ?? "a ruin";
			}
		}

		/// <summary>The player's buildings <paramref name="faction"/> knows that still stand.</summary>
		internal static List<LocationStructure> KnownStanding(Faction faction)
		{
			List<LocationStructure> standing = new List<LocationStructure>();
			if (faction != null && Known.TryGetValue(faction, out HashSet<LocationStructure> set))
			{
				foreach (LocationStructure s in set)
				{
					if (s != null && !s.hasBeenDestroyed)
					{
						standing.Add(s);
					}
				}
			}
			return standing;
		}

		/// <summary>Living villagers carrying news home, with the buildings they would tell of
		/// that still stand.</summary>
		internal static List<KeyValuePair<Character, List<LocationStructure>>> Couriers()
		{
			List<KeyValuePair<Character, List<LocationStructure>>> couriers = new List<KeyValuePair<Character, List<LocationStructure>>>();
			foreach (KeyValuePair<Character, HashSet<LocationStructure>> kv in Carried)
			{
				List<LocationStructure> news = kv.Value.Where(s => s != null && !s.hasBeenDestroyed).ToList();
				if (kv.Key != null && !kv.Key.isDead && news.Count > 0)
				{
					couriers.Add(new KeyValuePair<Character, List<LocationStructure>>(kv.Key, news));
				}
			}
			return couriers;
		}

		/// <summary>
		/// The nearest structure the faction knows (or <paramref name="alsoKnown"/>, what a party
		/// has seen itself) that is still standing, or null.
		/// </summary>
		internal static LocationStructure NearestStanding(Faction faction, LocationGridTile from, IEnumerable<LocationStructure> alsoKnown = null)
		{
			HashSet<LocationStructure> set = new HashSet<LocationStructure>();
			if (faction != null && Known.TryGetValue(faction, out HashSet<LocationStructure> known))
			{
				set.UnionWith(known);
			}
			if (alsoKnown != null)
			{
				set.UnionWith(alsoKnown);
			}
			LocationStructure best = null;
			float bestDistance = float.MaxValue;
			foreach (LocationStructure s in set)
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

		// A save from before the ledger: factions aware of the player knew all of the player's
		// buildings in the base game, so they keep knowing them. A new game has no aware
		// faction, so nothing happens there.
		private static void MigrateLegacy()
		{
			List<LocationStructure> buildings = new List<LocationStructure>();
			foreach (LocationStructure s in PlayerManager.Instance?.player?.playerSettlement?.allStructures ?? new List<LocationStructure>())
			{
				if (s is DemonicStructure && !s.hasBeenDestroyed)
				{
					buildings.Add(s);
				}
			}
			foreach (Faction f in FactionManager.Instance.allFactions)
			{
				if (f != null && f.isMajorNonPlayer && f.isAwareOfPlayer && !Known.ContainsKey(f))
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
		// One "factionId/structureId" string per known structure, and one
		// "characterId/structureId" per piece of news a villager is carrying. JsonUtility
		// silently drops a list of a mod-defined class (it wrote "{}"), so the file sticks to
		// strings. Written even when empty: a save without this file predates the ledger.

		private static string Save()
		{
			KnowledgeSaveData file = new KnowledgeSaveData();
			foreach (KeyValuePair<Faction, HashSet<LocationStructure>> kv in Known)
			{
				foreach (LocationStructure s in kv.Value)
				{
					if (s != null && !s.hasBeenDestroyed)
					{
						file.known.Add(kv.Key.persistentID + "/" + s.persistentID);
					}
				}
			}
			foreach (KeyValuePair<Character, HashSet<LocationStructure>> kv in Carried)
			{
				foreach (LocationStructure s in kv.Value)
				{
					if (kv.Key != null && !kv.Key.isDead && s != null && !s.hasBeenDestroyed)
					{
						file.carried.Add(kv.Key.persistentID + "/" + s.persistentID);
					}
				}
			}
			return JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Known.Clear();
			_legacy = string.IsNullOrEmpty(json);
			Carried.Clear();
			if (string.IsNullOrEmpty(json))
			{
				RuinarchPlus.Log?.Info("Knowledge loaded: the save holds no knowledge data.");
				return;
			}
			KnowledgeSaveData file = JsonUtility.FromJson<KnowledgeSaveData>(json);
			int count = 0;
			foreach (string pair in file?.known ?? new List<string>())
			{
				int slash = pair.IndexOf('/');
				if (slash <= 0)
				{
					continue;
				}
				Faction faction = FactionManager.Instance.GetFactionByPersistentID(pair.Substring(0, slash));
				LocationStructure s = DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(pair.Substring(slash + 1));
				if (faction != null && s is DemonicStructure && !s.hasBeenDestroyed)
				{
					Learn(faction, s, announce: false);
					count++;
				}
				else
				{
					RuinarchPlus.Log?.Warning($"Knowledge: dropped saved entry {pair} (faction {(faction == null ? "not found" : faction.name)}, structure {(s == null ? "not found" : s.name + (s.hasBeenDestroyed ? " destroyed" : ""))}).");
				}
			}
			int carried = 0;
			foreach (string pair in file?.carried ?? new List<string>())
			{
				int slash = pair.IndexOf('/');
				Character c = slash <= 0 ? null : CharacterManager.Instance.GetCharacterByPersistentID(pair.Substring(0, slash));
				LocationStructure s = slash <= 0 ? null : DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(pair.Substring(slash + 1));
				if (c != null && !c.isDead && s is DemonicStructure && !s.hasBeenDestroyed)
				{
					if (!Carried.TryGetValue(c, out HashSet<LocationStructure> set))
					{
						set = new HashSet<LocationStructure>();
						Carried[c] = set;
					}
					set.Add(s);
					carried++;
				}
			}
			RuinarchPlus.Log?.Info($"Knowledge loaded: {count} known demonic structure(s) across {Known.Count} faction(s); {carried} piece(s) of news on the road.");
		}
	}

	[Serializable]
	public class KnowledgeSaveData
	{
		public List<string> known = new List<string>();
		public List<string> carried = new List<string>();
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
	// to their faction.
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
					Knowledge.Learn(goapNode.actor?.faction, goapNode.otherData[0].obj as LocationStructure);
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
	// aware). Now only once the faction knows a structure standing next door: its villagers
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
				Faction f = __instance.owner.owner;
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
							if (Knowledge.Knows(f, s))
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

	// Counterattacks set out for the nearest structure the faction knows, not "the player".
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
				LocationStructure target = Knowledge.NearestStanding(f, from);
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

	// On site, a counterattack party attacks the nearest structure it knows instead of
	// marching on the portal (AttackDemonicStructureBehaviour hard-codes THE_PORTAL).
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
				LocationStructure target = Knowledge.NearestStanding(character.faction, character.gridTileLocation, Knowledge.PartySightings(character));
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
