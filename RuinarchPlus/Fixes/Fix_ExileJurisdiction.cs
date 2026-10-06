using HarmonyLib;
using Logs;
using Traits;
using UtilityScripts;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(Exile), nameof(Exile.AfterExileSuccess))]
	internal static class Fix_ExileJurisdiction
	{
		private static readonly System.Action<Log, ILogFiller, string, LOG_IDENTIFIER, bool, bool> AddFiller =
			AccessTools.MethodDelegate<System.Action<Log, ILogFiller, string, LOG_IDENTIFIER, bool, bool>>(
				AccessTools.Method(typeof(Log), "AddToFillers", new[] { typeof(ILogFiller), typeof(string), typeof(LOG_IDENTIFIER), typeof(bool), typeof(bool) }));

		private static bool Prefix(ActualGoapNode goapNode)
		{
			Character target = (Character)goapNode.target;
			// JudgeCharacter selects crimes wanted by the judge's faction. Membership
			// can change between the original crime and its sentence (e.g. vampires).
			Faction court = goapNode.actor.faction;
			if (target.faction == court) return true;

			if (target.traitContainer.HasTrait("Criminal"))
				target.traitContainer.GetTraitOrStatus<Criminal>("Criminal").SetIsImprisoned(false);
			target.crimeComponent.SetDecisionAndJudgeToAllUnpunishedCrimesWantedBy(court, CRIME_STATUS.Exiled, goapNode.actor);
			court.AddBannedCharacter(target);
			// Stock expulsion returns early for nonmembers. Apply its grudge without
			// expelling the offender from their unrelated current faction.
			bool grudgeAdded = false;
			if (court.leader is Character leader)
			{
				int chance = 0;
				switch (target.moodComponent.moodState)
				{
					case MOOD_STATE.Normal: chance = ChanceData.GetChance(CHANCE_TYPE.Grudge_Exile_Normal_Mood); break;
					case MOOD_STATE.Bad: chance = ChanceData.GetChance(CHANCE_TYPE.Grudge_Exile_Bad_Mood); break;
					case MOOD_STATE.Critical: chance = ChanceData.GetChance(CHANCE_TYPE.Grudge_Exile_Critical_Mood); break;
				}
				if (GameUtilities.RollChance(chance) && target.relationshipContainer.SetHasGrudgeAgainst(target, leader, true))
				{
					grudgeAdded = true;
					Log log = GameManager.CreateNewLogUsingNewLocalization(GameManager.Instance.Today(),
						"Grudge", "Relationships_Table", "Grudge_Expelled", LOG_TAG.Social);
					AddFiller(log, target, target.name, LOG_IDENTIFIER.ACTIVE_CHARACTER, true, false);
					AddFiller(log, leader, leader.name, LOG_IDENTIFIER.TARGET_CHARACTER, true, false);
					log.AddLogToDatabase(releaseLogAfter: true);
				}
			}
			if (!grudgeAdded && goapNode.otherData != null && goapNode.otherData[0].obj is CrimeData crime && crime.IsCrimeFabricated())
				crime.TryTriggerGrudgeAgainstJudgeOrReporter();
			target.crimeComponent.RemoveAllCrimesWantedBy(court);
			target.traitContainer.RemoveRestrainAndImprison(target, goapNode.actor);
			// The new faction's home and territory are not this court's to clear.
			return false;
		}
	}
}
