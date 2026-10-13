using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace TruePlanet
{
    internal sealed class CampaignSkill
    {
        public PLAYER_SKILL_TYPE Type;
        public int Level, MaxCharges, Charges, BonusCharges, ManaCost, SpiritEnergyCost;
        public int Cooldown, CooldownTick, UnlockCost, RemainingChaosOrbs;
        public float Pierce;
        public bool Unlocked;
    }
    internal sealed class CampaignCarry
    {
        public int Mana, SpiritEnergy, ChaoticEnergy, MaxChaoticEnergy;
        public CampaignSkill[] Skills = new CampaignSkill[0];
        public PLAYER_SKILL_TYPE[] Powers = new PLAYER_SKILL_TYPE[0];
        private static readonly AccessTools.FieldRef<CurrenciesComponent, int> Capacity =
            AccessTools.FieldRefAccess<CurrenciesComponent, int>("<maxChaoticEnergy>k__BackingField");
        private static readonly AccessTools.FieldRef<CurrenciesComponent, int> Spirit =
            AccessTools.FieldRefAccess<CurrenciesComponent, int>("<spiritEnergy>k__BackingField");

        internal static CampaignCarry Capture()
        {
            var player = PlayerManager.Instance.player; var currency = player.currenciesComponent;
            var component = player.playerSkillComponent;
            var available = new HashSet<PLAYER_SKILL_TYPE>();
            foreach (var list in new[] { component.spells, component.afflictions, component.schemes, component.raidActions,
                component.playerActions, component.demonicStructuresSkills, component.buildSkills, component.minionsSkills, component.summonsSkills })
                foreach (PLAYER_SKILL_TYPE type in list)
                    if (!PlayerSkillManager.Instance.GetSkillData(type).isTemporarilyInUse) available.Add(type);
            var carry = new CampaignCarry { Mana = currency.mana, SpiritEnergy = currency.spiritEnergy,
                ChaoticEnergy = currency.chaoticEnergy, MaxChaoticEnergy = currency.maxChaoticEnergy,
                Powers = available.OrderBy(t => (int)t).ToArray(),
                Skills = PlayerSkillManager.Instance.allPlayerSkillsData.Values.OrderBy(s => (int)s.type).Select(s => new CampaignSkill
                {
                    Type = s.type, Level = s.currentLevel, MaxCharges = s.baseMaxCharges, Charges = s.charges,
                    BonusCharges = s.bonusCharges, ManaCost = s.baseManaCost, SpiritEnergyCost = s.baseSpiritEnergyCost,
                    Cooldown = s.baseCooldown, CooldownTick = s.currentCooldownTick, UnlockCost = s.unlockCost,
                    RemainingChaosOrbs = s.remainingChaosOrbs, Pierce = s.basePierce, Unlocked = s.isUnlockedBaseOnRequirements
                }).ToArray() };
            carry.Validate(); return carry;
        }
        internal void Validate()
        {
            if (Mana < 0 || SpiritEnergy < 0 || ChaoticEnergy < 0 || MaxChaoticEnergy < ChaoticEnergy || Skills == null || Powers == null)
                throw new InvalidDataException("Invalid portable campaign progression.");
            var types = new HashSet<PLAYER_SKILL_TYPE>();
            foreach (CampaignSkill skill in Skills)
                if (skill == null || !types.Add(skill.Type) || skill.Level < 0 || skill.BonusCharges < 0
                    || float.IsNaN(skill.Pierce) || float.IsInfinity(skill.Pierce))
                    throw new InvalidDataException("Invalid or duplicate portable skill progression.");
            var powers = new HashSet<PLAYER_SKILL_TYPE>();
            foreach (PLAYER_SKILL_TYPE type in Powers)
                if (!types.Contains(type) || !powers.Add(type)) throw new InvalidDataException("Portable power has no unique skill progression record.");
        }
        internal void Apply()
        {
            Validate();
            var manager = PlayerSkillManager.Instance; var player = PlayerManager.Instance.player;
            foreach (CampaignSkill saved in Skills)
                if (!manager.allPlayerSkillsData.ContainsKey(saved.Type))
                    throw new InvalidDataException("Destination does not provide campaign skill " + saved.Type + ".");
            var powers = new HashSet<PLAYER_SKILL_TYPE>(Powers); var component = player.playerSkillComponent;
            foreach (CampaignSkill saved in Skills)
            {
                SkillData skill = manager.GetSkillData(saved.Type);
                skill.FinishCooldown(); skill.SetCurrentLevel(saved.Level);
                if (powers.Contains(saved.Type)) component.AddAndCategorizePlayerSkill(skill);
                else if (skill.isInUse) { skill.SetBonusCharges(0); component.RemovePlayerSkill(skill); }
                skill.SetIsUnlockBaseOnRequirements(saved.Unlocked);
                if (!powers.Contains(saved.Type) && saved.BonusCharges > 0) component.AddAndCategorizeTemporaryPlayerSkill(skill);
                // Categorization initializes defaults. Restore portable counters afterward;
                // destination-local character locks, targeting and scheme objects are untouched.
                skill.SetMaxCharges(saved.MaxCharges); skill.SetCharges(saved.Charges); skill.SetBonusCharges(saved.BonusCharges);
                skill.SetManaCost(saved.ManaCost); skill.SetBaseSpiritEnergyCost(saved.SpiritEnergyCost);
                skill.SetBaseCooldownOnly(saved.Cooldown); skill.SetCurrentCooldownTick(saved.CooldownTick);
                skill.SetUnlockCost(saved.UnlockCost); skill.SetRemainingChaosOrbs(saved.RemainingChaosOrbs);
                skill.SetPierce(saved.Pierce); skill.ResetAllChargesUIText(); skill.OnLoadSpell();
            }
            CurrenciesComponent currency = player.currenciesComponent;
            Capacity(currency) = MaxChaoticEnergy;
            currency.AdjustChaoticEnergyWithoutAffectingSpiritEnergy(ChaoticEnergy - currency.chaoticEnergy);
            // The native SpiritEnergy adjustment is victory-mode-gated. The saved
            // counter still follows the campaign when the destination mode hides it.
            RestoreSpiritEnergy();
            currency.AdjustManaNoLimit(Mana - currency.mana, shouldBroadcastSignal: true);
            if (currency.mana != Mana || currency.spiritEnergy != SpiritEnergy || currency.chaoticEnergy != ChaoticEnergy
                || currency.maxChaoticEnergy != MaxChaoticEnergy)
                throw new InvalidOperationException("Native currency restoration changed the carried campaign values.");
        }
        internal void RestoreSpiritEnergy()
        {
            CurrenciesComponent currency = PlayerManager.Instance.player.currenciesComponent;
            Spirit(currency) = SpiritEnergy;
            currency.AdjustSpiritEnergy(0, shouldBroadcastSignal: false);
        }
    }
}
