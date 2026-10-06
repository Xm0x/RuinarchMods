using System.Collections;
using System.Linq;
using HarmonyLib;
using Inner_Maps;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator ExileJurisdictionSuite()
		{
			// Native factions and initialized characters, isolated from the world's residents.
			Faction court = new Faction(FACTION_TYPE.Human_Empire, RACE.HUMANS);
			Faction clan = new Faction(FACTION_TYPE.Vampire_Clan, RACE.HUMANS);
			Character judge = null;
			Character migrated = null;
			Character ordinary = null;
			CHANCE_TYPE[] grudgeChances = { CHANCE_TYPE.Grudge_Exile_Normal_Mood, CHANCE_TYPE.Grudge_Exile_Bad_Mood, CHANCE_TYPE.Grudge_Exile_Critical_Mood };
			int[] previousChances = new int[grudgeChances.Length];
			for (int i = 0; i < grudgeChances.Length; i++)
			{
				previousChances[i] = ChanceData.integerChances[grudgeChances[i]];
				ChanceData.integerChances[grudgeChances[i]] = 100;
			}
			try
			{
				DatabaseManager.Instance.factionDatabase.RegisterFaction(court);
				DatabaseManager.Instance.factionDatabase.RegisterFaction(clan);
				LocationGridTile[] spots = Villages().SelectMany(v => v.cityCenter.passableTiles)
					.Where(t => !t.isOccupied).Take(3).ToArray();
				if (spots.Length != 3)
					throw new System.InvalidOperationException("Exile fixtures require three free village square tiles.");
				judge = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: court, homeRegion: GridMap.Instance.mainRegion, randomizeTraits: false);
				migrated = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: court, homeRegion: GridMap.Instance.mainRegion, randomizeTraits: false);
				ordinary = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: court, homeRegion: GridMap.Instance.mainRegion, randomizeTraits: false);
				Character[] fixtures = { judge, migrated, ordinary };
				for (int i = 0; i < fixtures.Length; i++)
				{
					fixtures[i].CreateMarker();
					fixtures[i].InitialCharacterPlacement(spots[i]);
					fixtures[i].marker.UpdatePosition();
					if (fixtures[i].faction != court)
						fixtures[i].ChangeFactionTo(court, bypassIdeologyChecking: true);
				}
				court.OnlySetLeader(judge);
				migrated.relationshipContainer.GetOrCreateRelationshipDataWith(migrated, judge);
				ordinary.relationshipContainer.GetOrCreateRelationshipDataWith(ordinary, judge);
				Exile exile = (Exile)InteractionManager.Instance.goapActionData[INTERACTION_TYPE.EXILE];
				CrimeData originalCrime = migrated.crimeComponent.AddCrime(CRIME_TYPE.Vampire, CRIME_SEVERITY.Serious,
					null, migrated, null, court, REACTION_STATUS.WITNESSED);
				migrated.traitContainer.AddTrait(migrated, "Vampire");
				originalCrime.AddFactionThatConsidersWanted(court);
				migrated.ChangeFactionTo(clan, bypassIdeologyChecking: true);
				CrimeData clanCrime = migrated.crimeComponent.AddCrime(CRIME_TYPE.Theft, CRIME_SEVERITY.Serious,
					null, migrated, null, clan, REACTION_STATUS.WITNESSED);
				clanCrime.AddFactionThatConsidersWanted(clan);
				migrated.traitContainer.RestrainAndImprison(migrated, judge, court, judge);
				Check("exile fixture retains the original court's crime after joining another clan", () =>
					(migrated.faction == clan && originalCrime.IsWantedBy(court)
					&& migrated.crimeComponent.GetFirstCrimeWantedBy(judge.faction, CRIME_STATUS.Unpunished) == originalCrime,
					$"currentClan={migrated.faction == clan}, courtWanted={originalCrime.IsWantedBy(court)}"));
				ActualGoapNode crossNode = new ActualGoapNode();
				crossNode.SetActionData(exile, judge, migrated, new OtherData[] { new CrimeDataOtherData(originalCrime) }, 10);
				exile.AfterExileSuccess(crossNode);
				Check("cross-faction exile records the judgment on the original court's crime", () =>
					(originalCrime.crimeStatus == CRIME_STATUS.Exiled && originalCrime.judge == judge && originalCrime.isRemoved,
					$"decision={originalCrime.crimeStatus}, judge={originalCrime.judge?.name ?? "none"}, removed={originalCrime.isRemoved}"));
				Check("cross-faction exile bans return to the judging faction, not the new clan", () =>
					(court.IsCharacterBannedFromJoining(migrated) && !court.CanCharacterJoinFactionBasedOnNonReligionIdeologiesAndBanning(migrated)
					&& !clan.IsCharacterBannedFromJoining(migrated),
					$"courtBan={court.IsCharacterBannedFromJoining(migrated)}, clanBan={clan.IsCharacterBannedFromJoining(migrated)}"));
				Check("cross-faction exile preserves unrelated clan membership and its unresolved crime", () =>
					(migrated.faction == clan && clan.characters.Contains(migrated)
					&& clanCrime.crimeStatus == CRIME_STATUS.Unpunished && !clanCrime.isRemoved && clanCrime.judge == null,
					$"clanMember={migrated.faction == clan}, clanCrime={clanCrime.crimeStatus}, removed={clanCrime.isRemoved}"));
				Check("cross-faction exile releases the court's prisoner and restraints", () =>
					(!migrated.traitContainer.HasTrait("Prisoner") && !migrated.traitContainer.HasTrait("Restrained"),
					$"prisoner={migrated.traitContainer.HasTrait("Prisoner")}, restrained={migrated.traitContainer.HasTrait("Restrained")}"));
				Check("cross-faction exile preserves the grudge against the judging faction's leader", () =>
					(migrated.relationshipContainer.HasGrudgeAgainst(judge),
					$"grudgeAgainstCourtLeader={migrated.relationshipContainer.HasGrudgeAgainst(judge)}"));

				CrimeData normalCrime = ordinary.crimeComponent.AddCrime(CRIME_TYPE.Vampire, CRIME_SEVERITY.Serious,
					null, ordinary, null, court, REACTION_STATUS.WITNESSED);
				normalCrime.AddFactionThatConsidersWanted(court);
				ordinary.traitContainer.RestrainAndImprison(ordinary, judge, court, judge);
				ActualGoapNode normalNode = new ActualGoapNode();
				normalNode.SetActionData(exile, judge, ordinary, new OtherData[] { new CrimeDataOtherData(normalCrime) }, 10);
				exile.AfterExileSuccess(normalNode);
				Check("same-faction exile still decides the crime and bans the offender", () =>
					(normalCrime.crimeStatus == CRIME_STATUS.Exiled && normalCrime.judge == judge && normalCrime.isRemoved
					&& court.IsCharacterBannedFromJoining(ordinary),
					$"decision={normalCrime.crimeStatus}, removed={normalCrime.isRemoved}, banned={court.IsCharacterBannedFromJoining(ordinary)}"));
				Check("same-faction exile still expels and releases the offender", () =>
					(ordinary.faction != court && !court.characters.Contains(ordinary)
					&& !ordinary.traitContainer.HasTrait("Prisoner") && !ordinary.traitContainer.HasTrait("Restrained"),
					$"courtMember={ordinary.faction == court}, prisoner={ordinary.traitContainer.HasTrait("Prisoner")}, restrained={ordinary.traitContainer.HasTrait("Restrained")}"));
				Check("same-faction exile preserves the grudge against the faction's leader", () =>
					(ordinary.relationshipContainer.HasGrudgeAgainst(judge),
					$"grudgeAgainstCourtLeader={ordinary.relationshipContainer.HasGrudgeAgainst(judge)}"));
			}
			finally
			{
				for (int i = 0; i < grudgeChances.Length; i++)
					ChanceData.integerChances[grudgeChances[i]] = previousChances[i];
				foreach (Character character in new[] { migrated, ordinary, judge })
				{
					if (character == null) continue;
					if (character.stateComponent.currentState != null) character.stateComponent.ExitCurrentState();
					character.DestroyMarker(removeFromGame: false);
					character.faction?.LeaveFaction(character);
					CharacterManager.Instance.RemoveCharacter(character);
					character.CleanUp();
				}
				AccessTools.Method(typeof(Faction), "RemoveListeners").Invoke(court, null);
				AccessTools.Method(typeof(Faction), "RemoveListeners").Invoke(clan, null);
				DatabaseManager.Instance.factionDatabase.UnRegisterFaction(court);
				DatabaseManager.Instance.factionDatabase.UnRegisterFaction(clan);
			}
			yield break;
		}
	}
}
