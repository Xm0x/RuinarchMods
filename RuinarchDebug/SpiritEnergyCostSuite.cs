using System.Collections;
using UnityEngine;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		// C12-11: Half costs must halve spirit-energy prices, rounding up like mana.
		// Missing scaling fails the 7 -> 4 case; scaling -1 breaks the no-cost sentinel.
		private IEnumerator SpiritEnergyCostSuite()
		{
			WorldSettingsData settings = WorldSettings.Instance.worldSettingsData;
			VICTORY_CONDITION victory = settings.victoryCondition;
			SKILL_COST_AMOUNT costs = settings.playerSkillSettings.costAmount;
			PlayerSkillData skill = ScriptableObject.CreateInstance<PlayerSkillData>();
			skill.skillUpgradeData = new SkillUpgradeData { spiritEnergyCostPerLevel = new[] { 7, -1 } };
			try
			{
				settings.victoryCondition = VICTORY_CONDITION.Progression;
				settings.playerSkillSettings.costAmount = SKILL_COST_AMOUNT.Half;
				Check("Half costs round a seven-point spirit-energy price up to four", () =>
					(skill.GetSpiritEnergyCostBaseOnLevel(0) == 4, $"price={skill.GetSpiritEnergyCostBaseOnLevel(0)}"));
				Check("Half costs preserve the absent spirit-energy cost sentinel", () =>
					(skill.GetSpiritEnergyCostBaseOnLevel(1) == -1, $"price={skill.GetSpiritEnergyCostBaseOnLevel(1)}"));
				settings.playerSkillSettings.costAmount = SKILL_COST_AMOUNT.Normal;
				Check("Normal costs preserve the full spirit-energy price", () =>
					(skill.GetSpiritEnergyCostBaseOnLevel(0) == 7, $"price={skill.GetSpiritEnergyCostBaseOnLevel(0)}"));
				settings.playerSkillSettings.costAmount = SKILL_COST_AMOUNT.None;
				Check("None costs remove the spirit-energy price", () =>
					(skill.GetSpiritEnergyCostBaseOnLevel(0) == 0, $"price={skill.GetSpiritEnergyCostBaseOnLevel(0)}"));
				settings.playerSkillSettings.costAmount = SKILL_COST_AMOUNT.Half;
				settings.victoryCondition = VICTORY_CONDITION.Eradication;
				Check("Eradication disables spirit-energy prices even with Half costs", () =>
					(skill.GetSpiritEnergyCostBaseOnLevel(0) == 0, $"price={skill.GetSpiritEnergyCostBaseOnLevel(0)}"));
			}
			finally
			{
				settings.victoryCondition = victory;
				settings.playerSkillSettings.costAmount = costs;
				Object.Destroy(skill);
			}
			yield break;
		}
	}
}
