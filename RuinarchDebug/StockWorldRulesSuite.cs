using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Traits;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator StockWorldRulesSuite()
		{
			var fixtures = new List<Character>();
			TileObject trapped = null;
			var player = PlayerManager.Instance.player;
			int mana = player.currenciesComponent.mana;
			var watcher = new Watcher(GridMap.Instance.mainRegion);
			var node = new ActualGoapNode();
			try
			{
				var village = Villages().First(v => v.owner != null && v.areas.Count > 0);
				Character attacker = StockActor(fixtures, faction: village.owner);
				attacker.behaviourComponent.SetAttackVillageTarget(village);
				attacker.behaviourComponent.AddBehaviourComponent(typeof(AttackVillageBehaviour));
				string debug = string.Empty;
				new AttackVillageBehaviour().TryDoBehaviour(attacker, ref debug, out JobQueueItem attackJob);
				attackJob?.Reset();
				Check("C01-31 a non-hostile village attack is cancelled on its next behaviour tick", () =>
					(attacker.behaviourComponent.attackVillageTarget == null && !attacker.behaviourComponent.HasBehaviour(typeof(AttackVillageBehaviour)), debug));

				Character archer = StockActor(fixtures, "Archer"), trapper = StockActor(fixtures);
				foreach (var actor in new[] { archer, trapper })
				{
					actor.religionComponent.ChangeReligion(RELIGION.Demon_Worship);
					actor.traitContainer.AddTrait(actor, "Demon Cultist");
				}
				trapped = InnerMapManager.Instance.CreateNewTileObject<TileObject>(TILE_OBJECT_TYPE.ANTIDOTE);
				trapper.gridTileLocation.structure.AddPOI(trapped, trapper.gridTileLocation);
				trapped.traitContainer.AddTrait(trapped, "Booby Trapped", trapper);
				var trap = trapped.traitContainer.GetTraitOrStatus<BoobyTrapped>("Booby Trapped");
				trap.VillagerReactionToTileObjectTrait(trapped, archer, ref debug);
				Check("C06-12 cultist archers preserve traps made by fellow cultists", () =>
					(!archer.jobQueue.HasJob(JOB_TYPE.REMOVE_STATUS), $"removal={archer.jobQueue.HasJob(JOB_TYPE.REMOVE_STATUS)}"));
				archer.jobQueue.CancelAllJobs();
				archer.traitContainer.RemoveTrait(archer, "Demon Cultist");
				trap.VillagerReactionToTileObjectTrait(trapped, archer, ref debug);
				Check("C06-12 ordinary archers still enqueue trap disarming", () =>
					(archer.jobQueue.HasJob(JOB_TYPE.REMOVE_STATUS), "native REMOVE_STATUS job"));

				node.SetActionData(InteractionManager.Instance.goapActionData[INTERACTION_TYPE.KNOCKOUT_CHARACTER], attacker, trapper, null, 10);
				node.SetAsIllusion();
				var intel = new ActionIntel(node);
				var spell = PlayerSkillManager.Instance.GetSkillData(PLAYER_SKILL_TYPE.CORRUPT_TILE);
				player.SetCurrentActiveIntel(intel);
				player.SetCurrentlyActivePlayerSpell(spell);
				Check("C06-89 selecting a spell clears the active intel", () =>
					(player.currentActiveIntel == null && player.currentActivePlayerSpell == spell, "spell after intel"));
				player.SetCurrentActiveIntel(intel);
				Check("C06-89 selecting intel clears the active spell", () =>
					(player.currentActivePlayerSpell == null && player.currentActiveIntel == intel, "intel after spell"));
				player.SetCurrentlyActivePlayerSpell(null);
				player.SetCurrentActiveIntel(null);

				var eye = new SpawnEyeWardData();
				AccessTools.Property(typeof(SpawnEyeWardData), "watcherParentOfEye").SetValue(eye, watcher);
				eye.SetIsInUse(true);
				eye.SetManaCost(40);
				player.currenciesComponent.AdjustMana(-player.currenciesComponent.mana);
				bool tileAllowed = eye.CanPerformAbilityTowards(attacker.gridTileLocation, out string reason);
				Check("C11-16 Eye Ward rejects an unaffordable tile", () => (!tileAllowed, reason));
				Check("C11-16 Eye Ward rejects an unaffordable Watcher action", () =>
					(!eye.CanPerformAbilityTowards(watcher), "mana=0, cost=40"));
				eye.ActivateAbility(attacker.gridTileLocation);
				Check("C11-16 unaffordable execution places no Eye Ward", () =>
					(watcher.eyeWards.Count == 0 && player.currenciesComponent.mana == 0, $"eyes={watcher.eyeWards.Count}, mana={player.currenciesComponent.mana}"));
				player.currenciesComponent.AdjustMana(40);
				Check("C11-16 Eye Ward remains available at its exact mana cost", () =>
					(eye.CanPerformAbilityTowards(attacker.gridTileLocation, out _) && eye.CanPerformAbilityTowards(watcher), "mana=cost=40"));
				eye.ActivateAbility(attacker.gridTileLocation);
				Check("C11-16 affordable execution places an Eye Ward and pays its mana cost", () =>
					(watcher.eyeWards.Count == 1 && player.currenciesComponent.mana == 0, $"eyes={watcher.eyeWards.Count}, mana={player.currenciesComponent.mana}"));

				Character ratman = CharacterManager.Instance.GenerateRatman(StockActor(fixtures).gridTileLocation);
				fixtures.Add(ratman);
				var spawner = new MonsterSpawner();
				spawner.spawnedCharacterIDs.Add(ratman.persistentID);
				int count = (int)AccessTools.Method(typeof(MonsterSpawner), "GetCountOfValidSpawnedMonsters").Invoke(spawner, null);
				Check("C12-14 native Ratmen count toward the monster spawner limit", () => (count == 1, $"count={count}"));
				Check("C12-14 a native Ratman keeps its spawner's raid actions available", () =>
					(spawner.HasValidSpawnedMonster() && spawner.HasValidSpawnedMonsterThatIsNotAttackingVillage(), "living wild Ratman"));
				ratman.Death();
				count = (int)AccessTools.Method(typeof(MonsterSpawner), "GetCountOfValidSpawnedMonsters").Invoke(spawner, null);
				Check("C12-14 dead Ratmen do not consume spawning capacity", () => (count == 0 && !spawner.HasValidSpawnedMonster(), $"count={count}"));
				spawner.RemoveTileObject(null);
			}
			finally
			{
				player.SetCurrentlyActivePlayerSpell(null);
				player.SetCurrentActiveIntel(null);
				player.currenciesComponent.AdjustMana(mana - player.currenciesComponent.mana);
				foreach (DemonEye ward in watcher.eyeWards.ToArray())
				{
					ward.gridTileLocation?.structure?.RemovePOI(ward);
					watcher.RemoveEyeWard(ward);
				}
				node.Reset();
				trapped?.gridTileLocation?.structure?.RemovePOI(trapped);
				foreach (Character actor in fixtures)
				{
					actor.jobQueue.CancelAllJobs();
					if (actor.stateComponent.currentState != null) actor.stateComponent.ExitCurrentState();
					actor.DestroyMarker(removeFromGame: false);
					actor.faction?.LeaveFaction(actor);
					CharacterManager.Instance.RemoveCharacter(actor);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(actor);
				}
			}
			yield break;
		}
	}
}
