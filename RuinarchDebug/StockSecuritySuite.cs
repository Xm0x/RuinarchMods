using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Traits;
using UnityEngine;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private static bool EmptyPlacementFootprint(LocationGridTile center, LocationStructureObject template)
		{
			if (center == null || !(center.structure is Wilderness)) return false;
			foreach (var coordinate in template.localOccupiedCoordinates)
			{
				var offset = coordinate - template.center;
				int x = center.localPlace.x + offset.x, y = center.localPlace.y + offset.y;
				if (x < 0 || y < 0 || x >= center.parentMap.width || y >= center.parentMap.height) return false;
				var tile = center.parentMap.map[x, y];
				if (!(tile.structure is Wilderness) || tile.tileObjectComponent.objHere != null || tile.IsPartOfSettlement(out _)) return false;
			}
			return true;
		}

		private IEnumerator StockSecuritySuite()
		{
			var fixtures = new List<Character>();
			var faction = new Faction(FACTION_TYPE.Human_Empire, RACE.HUMANS);
			TileObject objectToPlaceOver = null;
			var corruptedTiles = new List<LocationGridTile>();
			var randomState = Random.state;
			var cameraPosition = InnerMapCameraMove.Instance.camera.transform.position;
			var portal = PlayerManager.Instance.player.playerSettlement.allStructures.OfType<ThePortal>().First();
			var confirmation = (GeneralConfirmation)AccessTools.Field(typeof(PlayerUI), "_generalConfirmation").GetValue(PlayerUI.Instance);
			try
			{
				DatabaseManager.Instance.factionDatabase.RegisterFaction(faction);
				faction.factionType.SetAsDefault(faction);
				faction.factionType.AddCrime(CRIME_TYPE.Assault, CRIME_SEVERITY.Serious);
				Character actor = StockActor(fixtures, faction: faction), target = StockActor(fixtures), witness = StockActor(fixtures, faction: faction);
				actor.religionComponent.ChangeReligion(RELIGION.Demon_Worship);
				actor.traitContainer.AddTrait(actor, "Demon Cultist");
				witness.religionComponent.ChangeReligion(RELIGION.Demon_Worship);
				witness.traitContainer.AddTrait(witness, "Demon Cultist");
				CharacterManager.Instance.Teleport(witness, target.gridTileLocation.neighbourList.First(t => t.IsPassable() && !t.isOccupied));
				witness.marker.UpdatePosition();
				Physics2D.SyncTransforms();
				target.marker.AddPOIAsInVisionRange(witness);
				var stealth = AccessTools.Method(typeof(CharacterMarker), "CanDoStealthCrimeToTarget", new[] { typeof(Character), typeof(CRIME_TYPE) });
				Check("C08-42 a fellow cultist witness evaluates the attacker rather than the victim", () =>
					((bool)stealth.Invoke(actor.marker, new object[] { target, CRIME_TYPE.Assault }), "cultist assault exemption"));
				witness.traitContainer.RemoveTrait(witness, "Demon Cultist");
				witness.religionComponent.ChangeReligion(RELIGION.Divine_Worship);
				Log($"Stealth witness: visible={target.marker.inVisionCharacters.Contains(witness)}, canWitness={witness.limiterComponent.canWitness}, hostile={target.IsHostileWith(witness)}, sameSpace={target.marker.visionColliderComponent.IsTheSameStructureOrSameOpenSpaceWithPOI(witness)}, severity={CrimeManager.Instance.GetCrimeSeverity(witness, target, target, CRIME_TYPE.Assault)}");
				Check("C08-42 an ordinary visible witness still blocks the same stealth crime", () =>
					(!(bool)stealth.Invoke(actor.marker, new object[] { target, CRIME_TYPE.Assault }), "ordinary witness blocks assault"));
				target.marker.RemovePOIFromInVisionRange(witness);

				target.traitContainer.AddTrait(target, "Restrained");
				target.limiterComponent.IncreaseTargetedByDemonicSnatch();
				var intervention = AccessTools.Method(typeof(ReactionComponent), "NonHostileAliveVillagerNotHomeOrFactionmateReactionToCharacter");
				intervention.Invoke(actor.reactionComponent, new object[] { actor, target, actor, target, "" });
				Check("C11-27 allied cultists do not liberate or release the player's active snatch target", () =>
					(actor.isAlliedWithPlayer && !actor.jobQueue.HasJob(JOB_TYPE.PREACH, target) && !actor.jobQueue.HasJob(JOB_TYPE.RELEASE_CHARACTER, target), "no intervention job"));
				actor.jobQueue.CancelAllJobs();
				target.limiterComponent.DecreaseTargetedByDemonicSnatch();
				intervention.Invoke(actor.reactionComponent, new object[] { actor, target, actor, target, "" });
				Check("C11-27 cultists still liberate ordinary restrained outsiders", () =>
					(actor.jobQueue.HasJob(JOB_TYPE.PREACH, target) || actor.jobQueue.HasJob(JOB_TYPE.RELEASE_CHARACTER, target), "native rescue job"));
				actor.jobQueue.CancelAllJobs();

				Character worker = StockActor(fixtures, "Logger");
				var wolf = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Wolf, FactionManager.Instance.wildMonsterFaction, homeRegion: GridMap.Instance.mainRegion);
				fixtures.Add(wolf);
				wolf.CreateMarker();
				wolf.InitialCharacterPlacement(worker.gridTileLocation.neighbourList.First(t => t.IsPassable() && !t.isOccupied));
				wolf.marker.UpdatePosition();
				worker.marker.AddPOIAsInVisionRange(wolf);
				wolf.traitContainer.AddTrait(wolf, "Stoned");
				var hostileReaction = AccessTools.Method(typeof(ReactionComponent), "HostileFightOrFlightReaction");
				var flightState = Random.state;
				for (int seed = 0; seed < 20; seed++)
				{
					Random.InitState(seed);
					flightState = Random.state;
					if (worker.combatComponent.GetFightOrFlightReaction(wolf, "Hostility").reaction == COMBAT_REACTION.Flight) break;
				}
				Random.state = flightState;
				hostileReaction.Invoke(worker.reactionComponent, new object[] { worker, wolf, "" });
				Check("C11-90 workers do not flee from a petrified monster", () => (!worker.combatComponent.IsAvoidInRange(wolf), "petrified wolf"));
				worker.combatComponent.RemoveAvoidInRange(wolf);
				wolf.traitContainer.RemoveTrait(wolf, "Stoned");
				Random.state = flightState;
				hostileReaction.Invoke(worker.reactionComponent, new object[] { worker, wolf, "" });
				Check("C11-90 the same mobile monster still triggers native flight", () => (worker.combatComponent.IsAvoidInRange(wolf), "mobile wolf"));

				var village = Villages().First(v => v.mainStorage != null && v.allStructures.Any(s => s is Dwelling && s.passableTiles.Count > 0));
				Character captive = StockActor(fixtures, faction: village.owner);
				var home = village.allStructures.First(s => s is Dwelling && s.passableTiles.Count > 0);
				captive.MigrateHomeTo(village, home);
				CharacterManager.Instance.Teleport(captive, portal.GetCenterTile());
				captive.traitContainer.RestrainAndImprison(captive, factionThatImprisoned: PlayerManager.Instance.player.playerFaction);
				captive.jobComponent.CreateReportDemonicStructure(portal);
				captive.movementComponent.LetGo(becomeDazed: true);
				Check("C07-36 a released prisoner arrives home incapacitated without a queued base report", () =>
					(captive.currentStructure == home && !captive.limiterComponent.canWitness && !captive.jobQueue.HasJob(JOB_TYPE.REPORT_CORRUPTED_STRUCTURE), $"home={captive.currentStructure == home}, canWitness={captive.limiterComponent.canWitness}, report={captive.jobQueue.HasJob(JOB_TYPE.REPORT_CORRUPTED_STRUCTURE)}"));

				var build = new DemonicStructurePlayerSkill();
				AccessTools.Property(typeof(DemonicStructurePlayerSkill), "structureType").SetValue(build, STRUCTURE_TYPE.WATCHER);
				var template = build.structureTemplate;
				var center = GridMap.Instance.mainRegion.areas.Select(a => a.gridTileComponent.centerGridTile)
					.First(t => EmptyPlacementFootprint(t, template));
				foreach (var coordinate in template.localOccupiedCoordinates)
				{
					var footprintOffset = coordinate - template.center;
					var tile = center.parentMap.map[center.localPlace.x + footprintOffset.x, center.localPlace.y + footprintOffset.y];
					if (tile.corruptionComponent.isCorrupted) continue;
					corruptedTiles.Add(tile);
					tile.corruptionComponent.CorruptTile();
				}
				var offset = template.localOccupiedCoordinates.First() - template.center;
				var objectTile = center.parentMap.map[center.localPlace.x + offset.x, center.localPlace.y + offset.y];
				objectToPlaceOver = InnerMapManager.Instance.CreateNewTileObject<TileObject>(TILE_OBJECT_TYPE.WOOD_PILE);
				objectTile.structure.AddPOI(objectToPlaceOver, objectTile);
				var validate = AccessTools.Method(typeof(DemonicStructurePlayerSkill), "CanBuildDemonicStructureOn");
				var placementArguments = new object[] { template, center, null };
				Check("C08-102 ordinary objects still permit native demonic placement", () =>
				{
					bool allowed = (bool)validate.Invoke(build, placementArguments);
					return (allowed, $"ordinary wood pile; rejection={placementArguments[2]}");
				});
				objectToPlaceOver.traitContainer.AddTrait(objectToPlaceOver, "Indestructible");
				Check("C08-102 demonic placement rejects an indestructible object anywhere in its footprint", () =>
				{
					bool allowed = (bool)validate.Invoke(build, placementArguments);
					return (!allowed, $"protected wood pile; rejection={placementArguments[2]}");
				});
				objectToPlaceOver.traitContainer.RemoveTrait(objectToPlaceOver, "Indestructible");
				Check("C08-102 removing protection restores native demonic placement", () =>
					((bool)validate.Invoke(build, placementArguments), $"unprotected wood pile; rejection={placementArguments[2]}"));

				var farArea = GridMap.Instance.mainRegion.areas.Where(a => a.gridTileComponent.centerGridTile != null
					&& a.gridTileComponent.centerGridTile.structure is Wilderness && a.gridTileComponent.centerGridTile.IsPassable())
					.OrderByDescending(a => (a.gridTileComponent.centerGridTile.localPlace - portal.GetCenterTile().localPlace).sqrMagnitude).First();
				actor.jobQueue.CancelAllJobs();
				CharacterManager.Instance.Teleport(actor, farArea.gridTileComponent.centerGridTile);
				InnerMapCameraMove.Instance.CenterCameraOnTile(farArea, instantCenter: true);
				Check("C08-58 the attack fixture is off-camera", () => (!InnerMapCameraMove.Instance.CanSee(portal), "off-camera portal"));
				portal.AddAttacker(actor);
				Check("C08-58 the first off-camera attack opens the actual warning window", () => (confirmation.isShowing, "native warning visible"));
				yield return new WaitForSecondsRealtime(0.8f);
				ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_logPath), "stock-under-attack.png"));
				yield return new WaitForSecondsRealtime(1f);
				confirmation.OnClickOKGeneralConfirmation();
				portal.RemoveAttacker(actor);
				portal.AddAttacker(actor);
				Check("C08-58 an immediate repeated attack does not reopen the warning", () => (!confirmation.isShowing, "warning stays dismissed"));
				if (confirmation.isShowing) confirmation.OnClickOKGeneralConfirmation();
				portal.RemoveAttacker(actor);
				yield return WaitGameHours(2.1f, () => false);
				InnerMapCameraMove.Instance.CenterCameraOnTile(farArea, instantCenter: true);
				Log($"Later attack: attackers={portal.currentAttackers.Count}, visible={InnerMapCameraMove.Instance.CanSee(portal)}, majorUI={PlayerUI.Instance.IsMajorUIShowing()}, actorAlive={!actor.isDead}");
				portal.AddAttacker(actor);
				Check("C08-58 a later attack warns again after the two-hour cooldown", () => (!actor.isDead && confirmation.isShowing, $"warning={confirmation.isShowing}, attackerAlive={!actor.isDead}"));
				if (confirmation.isShowing) confirmation.OnClickOKGeneralConfirmation();
				portal.RemoveAttacker(actor);
			}
			finally
			{
				Random.state = randomState;
				InnerMapCameraMove.Instance.camera.transform.position = cameraPosition;
				foreach (var actor in fixtures)
				{
					portal.RemoveAttacker(actor);
					actor.jobQueue.CancelAllJobs();
					// Native state teardown removes signal listeners and still needs the marker.
					if (actor.stateComponent.currentState != null) actor.stateComponent.ExitCurrentState();
					actor.DestroyMarker(removeFromGame: false);
					actor.faction?.LeaveFaction(actor);
					CharacterManager.Instance.RemoveCharacter(actor);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(actor);
				}
				if (objectToPlaceOver != null) objectToPlaceOver.gridTileLocation?.structure?.RemovePOI(objectToPlaceOver);
				foreach (var tile in corruptedTiles) tile.corruptionComponent.UncorruptTile();
				DatabaseManager.Instance.factionDatabase.UnRegisterFaction(faction);
			}
		}
	}
}
