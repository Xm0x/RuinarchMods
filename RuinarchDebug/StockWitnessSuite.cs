using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Locations.Settlements.Settlement_Events;
using Traits;
using UnityEngine;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator StockWitnessSuite()
		{
			var fixtures = new List<Character>();
			var node = new ActualGoapNode();
			NPCSettlement village = null;
			PlaguedEvent plagueEvent = null;
			var otherFaction = new Faction(FACTION_TYPE.Human_Empire, RACE.HUMANS);
			try
			{
				DatabaseManager.Instance.factionDatabase.RegisterFaction(otherFaction);
				village = Villages().First(v => v.owner != null && v.allStructures.Any(s => s is Dwelling && s.tiles.Count > 0));
				Character resident = StockActor(fixtures, faction: village.owner), visitor = StockActor(fixtures, faction: otherFaction);
				var home = village.allStructures.First(s => s is Dwelling && s.tiles.Count > 0);
				visitor.SetHomeStructure(home);
				CharacterManager.Instance.Teleport(visitor, home.passableTiles.First());
				resident.defaultCharacterTrait.OnSeePOI(visitor, resident);
				Check("C06-35 a cross-faction resident is not accused of trespassing in their own home", () =>
					(!resident.assumptionComponent.HasAlreadyAssumedTo(INTERACTION_TYPE.TRESPASSING, visitor, resident), "actual trespass assumption"));
				resident.assumptionComponent.assumptionData.Clear();
				visitor.SetHomeStructure(null);
				visitor.traitContainer.AddTrait(visitor, "Paralyzed");
				resident.defaultCharacterTrait.OnSeePOI(visitor, resident);
				Check("C06-35 an incapacitated visitor is not accused of trespassing", () =>
					(!resident.assumptionComponent.HasAlreadyAssumedTo(INTERACTION_TYPE.TRESPASSING, visitor, resident), "Paralyzed visitor"));
				resident.assumptionComponent.assumptionData.Clear();
				visitor.traitContainer.RemoveTrait(visitor, "Paralyzed");
				resident.defaultCharacterTrait.OnSeePOI(visitor, resident);
				Check("C06-35 a mobile outsider in another person's home still triggers trespassing", () =>
					(resident.assumptionComponent.HasAlreadyAssumedTo(INTERACTION_TYPE.TRESPASSING, visitor, resident), "mobile outsider"));

				Character vampire = StockActor(fixtures), zombie = StockActor(fixtures, "Walker Zombie"), human = StockActor(fixtures);
				vampire.traitContainer.AddTrait(vampire, "Vampire");
				vampire.needsComponent.SetFullness(0);
				var vampirism = vampire.traitContainer.GetTraitOrStatus<Vampire>("Vampire");
				vampirism.OnSeePOI(zombie, vampire);
				Check("C07-34 starving vampires do not enqueue zombie blood meals", () =>
					(zombie.characterClass.IsZombie() && !vampire.jobQueue.HasJob(JOB_TYPE.FULLNESS_RECOVERY_ON_SIGHT), $"zombie={zombie.characterClass.IsZombie()}, job={vampire.jobQueue.HasJob(JOB_TYPE.FULLNESS_RECOVERY_ON_SIGHT)}"));
				vampire.jobQueue.CancelAllJobs();
				vampirism.OnSeePOI(human, vampire);
				Check("C07-34 starving vampires still enqueue meals from living humans", () =>
					(vampire.jobQueue.HasJob(JOB_TYPE.FULLNESS_RECOVERY_ON_SIGHT), "native blood-drinking job"));

				node.SetActionData(InteractionManager.Instance.goapActionData[INTERACTION_TYPE.KNOCKOUT_CHARACTER], human, vampire, null, 10);
				node.SetAsIllusion();
				PlayerManager.Instance.player.SetCurrentActiveIntel(new ActionIntel(node));
				var hover = AccessTools.Method(typeof(CharacterMarker), "OnPointerEnter", new[] { typeof(Character) });
				var exit = AccessTools.Method(typeof(CharacterMarker), "OnPointerExit", new[] { typeof(Character) });
				hover.Invoke(human.marker, new object[] { human });
				var plate = (CharacterMarkerNameplate)AccessTools.Field(typeof(CharacterMarker), "_nameplate").GetValue(human.marker);
				Check("C09-57 a living intel actor shows the actual intel helper", () =>
					(plate != null && ((GameObject)AccessTools.Field(typeof(CharacterMarkerNameplate), "intelHelperGO").GetValue(plate)).activeSelf, "native nameplate helper"));
				exit.Invoke(human.marker, new object[] { human });
				human.Death();
				hover.Invoke(human.marker, new object[] { human });
				plate = (CharacterMarkerNameplate)AccessTools.Field(typeof(CharacterMarker), "_nameplate").GetValue(human.marker);
				Check("C09-57 hovering a corpse does not offer an unusable intel helper", () =>
					(plate == null || !((GameObject)AccessTools.Field(typeof(CharacterMarkerNameplate), "intelHelperGO").GetValue(plate)).activeSelf, "corpse helper hidden"));
				exit.Invoke(human.marker, new object[] { human });
				PlayerManager.Instance.player.SetCurrentActiveIntel(null);

				Character witness = StockActor(fixtures, faction: village.owner);
				witness.MigrateHomeTo(village);
				plagueEvent = new PlaguedEvent(village);
				AccessTools.Field(typeof(PlaguedEvent), "_rulerDecision").SetValue(plagueEvent, PLAGUE_EVENT_RESPONSE.Quarantine);
				village.eventManager.activeEvents.Add(plagueEvent);
				var rat = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Rat, FactionManager.Instance.wildMonsterFaction, homeRegion: GridMap.Instance.mainRegion);
				fixtures.Add(rat);
				rat.CreateMarker();
				rat.InitialCharacterPlacement(witness.gridTileLocation);
				rat.marker.UpdatePosition();
				var plagued = (IsPlagued)InteractionManager.Instance.goapActionData[INTERACTION_TYPE.IS_PLAGUED];
				plagued.ReactionToActor(rat, rat, witness, node, REACTION_STATUS.WITNESSED);
				Check("C09-06 plague rats are not assigned village quarantine jobs", () =>
					(!village.HasJob(JOB_TYPE.QUARANTINE, rat), "native village quarantine queue"));
				Character sick = StockActor(fixtures);
				plagued.ReactionToActor(sick, sick, witness, node, REACTION_STATUS.WITNESSED);
				Check("C09-06 ordinary villagers still receive quarantine jobs", () =>
					(village.HasJob(JOB_TYPE.QUARANTINE, sick), "normal character quarantine"));
				Character homeless = StockActor(fixtures), unquarantined = StockActor(fixtures);
				plagued.ReactionToActor(unquarantined, unquarantined, homeless, node, REACTION_STATUS.WITNESSED);
				Check("C09-06 a homeless plague witness does not enqueue another village's quarantine", () =>
					(!village.HasJob(JOB_TYPE.QUARANTINE, unquarantined), "no unrelated settlement quarantine job"));
			}
			finally
			{
				if (plagueEvent != null) village.eventManager.activeEvents.Remove(plagueEvent);
				PlayerManager.Instance.player.SetCurrentActiveIntel(null);
				node.Reset();
				foreach (Character actor in fixtures)
				{
					actor.jobQueue.CancelAllJobs();
					if (actor.stateComponent.currentState != null) actor.stateComponent.ExitCurrentState();
					actor.DestroyMarker(removeFromGame: false);
					actor.faction?.LeaveFaction(actor);
					CharacterManager.Instance.RemoveCharacter(actor);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(actor);
				}
				DatabaseManager.Instance.factionDatabase.UnRegisterFaction(otherFaction);
			}
			yield break;
		}
	}
}
