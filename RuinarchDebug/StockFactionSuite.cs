using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Factions.Faction_Succession;
using Traits;
using UnityEngine;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator StockFactionSuite()
		{
			var fixtures = new List<Character>();
			var court = new Faction(FACTION_TYPE.Human_Empire, RACE.HUMANS);
			var rival = new Faction(FACTION_TYPE.Human_Empire, RACE.HUMANS);
			var successionCourt = new Faction(FACTION_TYPE.Human_Empire, RACE.HUMANS);
			var node = new ActualGoapNode();
			var randomState = Random.state;
			try
			{
				foreach (var faction in new[] { court, rival, successionCourt }) DatabaseManager.Instance.factionDatabase.RegisterFaction(faction);
				court.factionType.SetAsDefault(court);
				rival.factionType.SetAsDefault(rival);
				var village = Villages().First(v => v.cityCenter != null);
				Character leader = StockActor(fixtures, faction: court), criminal = StockActor(fixtures, faction: court), pardoned = StockActor(fixtures, faction: court);
				criminal.traitContainer.AddTrait(criminal, "Criminal");
				pardoned.traitContainer.AddTrait(pardoned, "Criminal");
				leader.MigrateHomeTo(village);
				leader.religionComponent.ChangeReligion(RELIGION.Demon_Worship);
				leader.traitContainer.AddTrait(leader, "Demon Cultist");
				court.OnlySetLeader(leader);
				court.factionType.AddCrime(CRIME_TYPE.Demon_Worship, CRIME_SEVERITY.Heinous);
				rival.factionType.AddCrime(CRIME_TYPE.Demon_Worship, CRIME_SEVERITY.Heinous);
				var sharedCrime = criminal.crimeComponent.AddCrime(CRIME_TYPE.Demon_Worship, CRIME_SEVERITY.Heinous, null, criminal, null, court, REACTION_STATUS.WITNESSED);
				sharedCrime.AddFactionThatConsidersWanted(court);
				sharedCrime.AddFactionThatConsidersWanted(rival);
				var soleCrime = pardoned.crimeComponent.AddCrime(CRIME_TYPE.Demon_Worship, CRIME_SEVERITY.Heinous, null, pardoned, null, court, REACTION_STATUS.WITNESSED);
				soleCrime.AddFactionThatConsidersWanted(court);
				var otherOffender = StockActor(fixtures, faction: court);
				otherOffender.traitContainer.AddTrait(otherOffender, "Criminal");
				var otherReligionCrime = otherOffender.crimeComponent.AddCrime(CRIME_TYPE.Demon_Worship, CRIME_SEVERITY.Heinous, null, otherOffender, null, court, REACTION_STATUS.WITNESSED);
				otherReligionCrime.AddFactionThatConsidersWanted(court);
				var theft = otherOffender.crimeComponent.AddCrime(CRIME_TYPE.Theft, CRIME_SEVERITY.Serious, null, otherOffender, null, court, REACTION_STATUS.WITNESSED);
				theft.AddFactionThatConsidersWanted(court);
				FactionManager.Instance.RevalidateFactionCrimes(court, leader);
				Check("C08-05 legalizing a crime clears only the legalizing faction's wanted status", () =>
					(!sharedCrime.IsWantedBy(court) && sharedCrime.IsWantedBy(rival) && !sharedCrime.isRemoved && criminal.crimeComponent.activeCrimes.Contains(sharedCrime), "another faction still wants the same crime"));
				Check("C08-05 legalized crimes leave the faction wanted list but preserve other jurisdictions", () =>
					(!court.crimeComponent.wantedCharacters.Contains(criminal) && rival.crimeComponent.wantedCharacters.Contains(criminal), "court cleared, rival retained"));
				Check("C08-05 a crime with no remaining jurisdiction becomes history and removes Criminal", () =>
					(soleCrime.isRemoved && pardoned.crimeComponent.previousCrimes.Contains(soleCrime) && !pardoned.crimeComponent.activeCrimes.Contains(soleCrime) && !pardoned.traitContainer.HasTrait("Criminal"), "fully pardoned"));
				Check("C08-05 unrelated illegal crimes retain wanted status and Criminal", () =>
					(otherReligionCrime.isRemoved && theft.IsWantedBy(court) && !theft.isRemoved && court.crimeComponent.wantedCharacters.Contains(otherOffender) && otherOffender.traitContainer.HasTrait("Criminal"), $"worshipRemoved={otherReligionCrime.isRemoved}, theftWanted={theft.IsWantedBy(court)}, theftRemoved={theft.isRemoved}, wantedList={court.crimeComponent.wantedCharacters.Contains(otherOffender)}, criminal={otherOffender.traitContainer.HasTrait("Criminal")}"));

				Character witness = StockActor(fixtures, faction: court), friend = StockActor(fixtures, faction: court);
				witness.relationshipContainer.AdjustOpinion(witness, friend, "Autotest", 400, "autotest", createJobsOnReduce: false);
				var foreignCrime = friend.crimeComponent.AddCrime(CRIME_TYPE.Theft, CRIME_SEVERITY.Serious, null, friend, null, rival, REACTION_STATUS.WITNESSED);
				foreignCrime.AddFactionThatConsidersWanted(rival);
				court.SetIsMajorFaction(true);
				var assault = (Assault)InteractionManager.Instance.goapActionData[INTERACTION_TYPE.ASSAULT];
				node.SetActionData(assault, leader, friend, null, 10);
				AccessTools.Property(typeof(ActualGoapNode), "associatedJobType").SetValue(node, JOB_TYPE.APPREHEND);
				var emotions = new List<EMOTION>();
				assault.PopulateEmotionReactionsToActor(emotions, leader, friend, witness, node, REACTION_STATUS.WITNESSED);
				Check("C10-27 arresting a faction friend is not justified by another faction's crime", () =>
					(emotions.Contains(EMOTION.Disapproval) && !emotions.Contains(EMOTION.Resentment), string.Join(",", emotions)));
				foreignCrime.AddFactionThatConsidersWanted(court);
				emotions.Clear();
				assault.PopulateEmotionReactionsToActor(emotions, leader, friend, witness, node, REACTION_STATUS.WITNESSED);
				Check("C10-27 a serious crime wanted by the witness's own faction keeps its native reaction", () =>
					(emotions.Contains(EMOTION.Resentment), string.Join(",", emotions)));

				var wolf = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Wolf, successionCourt, homeRegion: GridMap.Instance.mainRegion, bypassIdeologyChecking: true);
				fixtures.Add(wolf);
				wolf.CreateMarker();
				wolf.InitialCharacterPlacement(StockActor(fixtures).gridTileLocation);
				wolf.marker.UpdatePosition();
				Character human = StockActor(fixtures, faction: successionCourt);
				human.MigrateHomeTo(village);
				CharacterManager.Instance.Teleport(human, village.cityCenter.passableTiles.First());
				var candidate = AccessTools.Method(typeof(FactionSuccession), "CanBeCandidateForSuccession");
				var succession = new Popularity();
				Check("C10-22 monsters cannot displace living villager succession candidates", () =>
					(!(bool)candidate.Invoke(succession, new object[] { wolf, successionCourt }), "normal resident remains"));
				Check("C10-22 normal residents remain succession candidates", () =>
					((bool)candidate.Invoke(succession, new object[] { human, successionCourt }), "normal candidate"));
				var skeleton = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Skeleton, successionCourt, homeRegion: GridMap.Instance.mainRegion, bypassIdeologyChecking: true);
				fixtures.Add(skeleton);
				skeleton.CreateMarker();
				skeleton.InitialCharacterPlacement(wolf.gridTileLocation.neighbourList.First(t => t.IsPassable() && !t.isOccupied));
				skeleton.marker.UpdatePosition();
				Check("C09-67 recruited skeletons cannot take over while living villager candidates remain", () =>
					(!(bool)candidate.Invoke(succession, new object[] { skeleton, successionCourt }), "skeleton excluded from villager succession"));
				human.ChangeToDefaultFaction();
				Check("C10-22 monster-only factions still have succession candidates", () =>
					((bool)candidate.Invoke(succession, new object[] { wolf, successionCourt }), "no sapient members remain"));

				var recruit = (Recruit)InteractionManager.Instance.goapActionData[INTERACTION_TYPE.RECRUIT];
				var requirements = AccessTools.Method(typeof(Recruit), "AreRequirementsSatisfied");
				wolf.ChangeFactionTo(PlayerManager.Instance.player.playerFaction, bypassIdeologyChecking: true);
				Check("C10-22 NPC recruiters cannot take player-faction monsters", () =>
					(!(bool)requirements.Invoke(recruit, new object[] { leader, wolf, null, null }), "player's wolf"));
				wolf.ChangeFactionTo(FactionManager.Instance.wildMonsterFaction, bypassIdeologyChecking: true);
				Check("C10-22 wild monsters retain native recruitment eligibility", () =>
					((bool)requirements.Invoke(recruit, new object[] { leader, wolf, null, null }), "wild wolf"));

				var exclusive = new Exclusive();
				exclusive.SetRequirement(RACE.HUMANS);
				successionCourt.factionType.AddIdeology(exclusive, successionCourt);
				wolf.ChangeFactionTo(successionCourt, bypassIdeologyChecking: true);
				successionCourt.CheckIfCharacterStillFitsIdeology(wolf, willLog: false, rollForGrudge: false);
				Check("C07-67 race-exclusive human factions do not retain incompatible wolves", () => (wolf.faction != successionCourt, $"wolf faction={wolf.faction?.name}"));
				exclusive.SetRequirement(GENDER.FEMALE);
				wolf.ChangeFactionTo(successionCourt, bypassIdeologyChecking: true);
				successionCourt.CheckIfCharacterStillFitsIdeology(wolf, willLog: false, rollForGrudge: false);
				Check("C07-67 non-race ideologies preserve native monster exemptions", () => (wolf.faction == successionCourt, "gender-exclusive monster exemption"));

				human.ChangeFactionTo(successionCourt, bypassIdeologyChecking: true);
				Character female = StockActor(fixtures, faction: successionCourt, gender: GENDER.FEMALE);
				bool unsafeGenderRule = false;
				for (int i = 0; i < 40; i++)
				{
					Random.InitState(i);
					FactionManager.Instance.RerollInclusiveTypeIdeology(successionCourt, wolf);
					if (successionCourt.factionType.HasIdeology(FACTION_IDEOLOGY.Exclusive, out var ideology) && ideology is Exclusive genderRule
						&& genderRule.category == EXCLUSIVE_IDEOLOGY_CATEGORIES.GENDER && (!genderRule.DoesCharacterFitIdeology(human) || !genderRule.DoesCharacterFitIdeology(female))) unsafeGenderRule = true;
				}
				Check("C10-76 a monster leader cannot impose an arbitrary gender rule that expels villagers", () => (!unsafeGenderRule, "40 deterministic ideology rolls with male and female residents"));
			}
			finally
			{
				court.SetIsMajorFaction(false);
				Random.state = randomState;
				node.Reset();
				foreach (var actor in fixtures)
				{
					actor.jobQueue.CancelAllJobs();
					if (actor.stateComponent.currentState != null) actor.stateComponent.ExitCurrentState();
					actor.DestroyMarker(removeFromGame: false);
					actor.faction?.LeaveFaction(actor);
					CharacterManager.Instance.RemoveCharacter(actor);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(actor);
				}
				foreach (var faction in new[] { court, rival, successionCourt }) DatabaseManager.Instance.factionDatabase.UnRegisterFaction(faction);
			}
			yield break;
		}
	}
}
