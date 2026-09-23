using System;
using System.Collections.Generic;
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
	/// - once the faction is aware of the player, anything its villagers see is learned too
	///   (including what a counterattack party discovers on the way);
	/// - a village that borders the player's land learns what stands next door.
	/// Counterattacks go to the nearest structure the faction knows and attack only known
	/// structures; the portal is a target once someone has seen it. When nothing they know
	/// of is left standing, the party considers the job done and goes home. Rescues and
	/// bounty hunts into a player building ask the same ledger (KnowledgeTargets.cs).
	///
	/// The ledger is stored inside the player's save (Ruinarch.ModContent's ModSave). A
	/// faction that is aware but knows nothing (a save made before this feature) keeps the
	/// vanilla behaviour.
	/// </summary>
	internal static class Knowledge
	{
		private const string SaveId = "ruinarch.plus.knowledge";

		private static readonly Dictionary<Faction, HashSet<LocationStructure>> Known = new Dictionary<Faction, HashSet<LocationStructure>>();

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

		/// <summary>True if the faction has learned anything at all (else vanilla applies).</summary>
		internal static bool KnowsAnything(Faction faction)
		{
			return faction != null && Known.TryGetValue(faction, out HashSet<LocationStructure> set) && set.Count > 0;
		}

		/// <summary>
		/// Stands in for the game's faction-wide <c>isAwareOfPlayer</c> wherever it decides
		/// about one particular building: the faction must know that building. A faction that
		/// is aware but knows nothing (a save made before this feature), or the feature off,
		/// keeps the vanilla answer.
		/// </summary>
		internal static bool KnowsOf(Faction faction, LocationStructure structure)
		{
			return faction != null && faction.isAwareOfPlayer && (!Enabled || !KnowsAnything(faction) || Knows(faction, structure));
		}

		internal static void Forget(Faction faction)
		{
			if (faction != null)
			{
				Known.Remove(faction);
			}
		}

		/// <summary>The nearest structure the faction knows that is still standing, or null.</summary>
		internal static LocationStructure NearestStanding(Faction faction, LocationGridTile from)
		{
			if (faction == null || !Known.TryGetValue(faction, out HashSet<LocationStructure> set))
			{
				return null;
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

		// ---- persistence -------------------------------------------------------------------
		// One "factionId/structureId" string per known structure. JsonUtility silently drops a
		// list of a mod-defined class (it wrote "{}"), so the file sticks to strings.

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
			return file.known.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Known.Clear();
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
			RuinarchPlus.Log?.Info($"Knowledge loaded: {count} known demonic structure(s) across {Known.Count} faction(s).");
		}
	}

	[Serializable]
	public class KnowledgeSaveData
	{
		public List<string> known = new List<string>();
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

	// Once a faction is aware of the player, whatever its villagers see, it learns. (Before
	// that, a sighting only becomes knowledge through the report above.)
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
					Knowledge.Learn(f, seen);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (sighting) failed: " + e.Message);
			}
		}
	}

	// A village whose land borders the player's counterattacks without any report
	// (SettlementPartyComponent.TryCreateCounterattackQuest): it knows what stands next door.
	[HarmonyPatch(typeof(SettlementPartyComponent), "TryCreateCounterattackQuest")]
	internal static class Knowledge_Neighbours
	{
		private static void Postfix(SettlementPartyComponent __instance, Faction p_faction)
		{
			if (!Knowledge.Enabled || p_faction == null || !p_faction.partyQuestBoard.HasPartyQuest(PARTY_QUEST_TYPE.Counterattack))
			{
				return;
			}
			try
			{
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
							Knowledge.Learn(p_faction, s);
						}
					}
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Knowledge (neighbours) failed: " + e.Message);
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
				if (!Knowledge.KnowsAnything(f))
				{
					return;
				}
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
			if (party == null || !party.isActive || !(party.currentQuest is CounterattackPartyQuest quest) || !Knowledge.KnowsAnything(character.faction))
			{
				return true;
			}
			try
			{
				producedJob = null;
				__result = true;
				LocationStructure target = Knowledge.NearestStanding(character.faction, character.gridTileLocation);
				if (target == null)
				{
					// Everything they knew of is destroyed: job done (mirrors the vanilla
					// "portal destroyed" branch).
					quest.SetIsSuccessful(state: true);
					if (party.targetDestination != party.partySettlement)
					{
						party.GoBackHomeAndEndQuest();
					}
					else
					{
						quest.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Finished_Quest"));
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
