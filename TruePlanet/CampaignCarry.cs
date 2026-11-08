using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Newtonsoft.Json;

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
    internal sealed class CampaignPortalChoice
    {
        public int Tier, ItemIndex;
        public PLAYER_SKILL_TYPE ChosenPower;
        public PLAYER_SKILL_TYPE[] PowerChoices = new PLAYER_SKILL_TYPE[0];
    }
    internal sealed class CampaignCarry
    {
        public PLAYER_ARCHETYPE Archetype = PLAYER_ARCHETYPE.Normal;
        public int PortalLevel = 1;
        public CampaignPortalChoice[] PortalChoices = new CampaignPortalChoice[0];
        public int Mana, SpiritEnergy, ChaoticEnergy, MaxChaoticEnergy;
        public CampaignSkill[] Skills = new CampaignSkill[0];
        public PLAYER_SKILL_TYPE[] Powers = new PLAYER_SKILL_TYPE[0];
        // Null only in campaigns written before portable native configuration was recorded.
        public WorldSettingsData Configuration;
        public bool MoreLoadoutOptions;
        private static readonly AccessTools.FieldRef<CurrenciesComponent, int> Capacity =
            AccessTools.FieldRefAccess<CurrenciesComponent, int>("<maxChaoticEnergy>k__BackingField");
        private static readonly AccessTools.FieldRef<CurrenciesComponent, int> Spirit =
            AccessTools.FieldRefAccess<CurrenciesComponent, int>("<spiritEnergy>k__BackingField");

        private static CampaignPortalChoice[] CopyPortalChoices(CampaignPortalChoice[] choices) => choices?.Select(choice => new CampaignPortalChoice
        {
            Tier = choice.Tier, ItemIndex = choice.ItemIndex, ChosenPower = choice.ChosenPower,
            PowerChoices = choice.PowerChoices?.ToArray() ?? new PLAYER_SKILL_TYPE[0]
        }).ToArray() ?? new CampaignPortalChoice[0];
        internal static CampaignCarry Capture(CampaignCarry previous = null)
        {
            var player = PlayerManager.Instance.player; var currency = player.currenciesComponent;
            var component = player.playerSkillComponent;
            var available = new HashSet<PLAYER_SKILL_TYPE>();
            foreach (var list in new[] { component.spells, component.afflictions, component.schemes, component.raidActions,
                component.playerActions, component.demonicStructuresSkills, component.buildSkills, component.minionsSkills, component.summonsSkills })
                foreach (PLAYER_SKILL_TYPE type in list)
                    if (!PlayerSkillManager.Instance.GetSkillData(type).isTemporarilyInUse) available.Add(type);
            ThePortal portal = player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL) as ThePortal;
            var carry = new CampaignCarry { Mana = currency.mana, SpiritEnergy = currency.spiritEnergy,
                ChaoticEnergy = currency.chaoticEnergy, MaxChaoticEnergy = currency.maxChaoticEnergy,
                Archetype = PlayerSkillManager.Instance.selectedArchetype,
                Configuration = CaptureConfiguration(),
                MoreLoadoutOptions = SaveManager.Instance.currentSaveDataPlayer.moreLoadoutOptions,
                PortalLevel = portal == null ? previous?.PortalLevel ?? 1 : portal.level,
                PortalChoices = portal == null ? CopyPortalChoices(previous?.PortalChoices) : component.portalUpgradeItems
                    .OrderBy(pair => pair.Key).SelectMany(pair => pair.Value.OrderBy(item => item.itemIndex).Select(item => new CampaignPortalChoice
                    {
                        Tier = pair.Key, ItemIndex = item.itemIndex, ChosenPower = item.chosenPowerForUpgrade,
                        PowerChoices = item.powerChoices.ToArray()
                    })).ToArray(),
                Powers = available.OrderBy(t => (int)t).ToArray(),
                Skills = PlayerSkillManager.Instance.allPlayerSkillsData.Values.OrderBy(s => (int)s.type).Select(s => new CampaignSkill
                {
                    Type = s.type, Level = s.currentLevel, MaxCharges = s.baseMaxCharges, Charges = s.charges,
                    BonusCharges = s.bonusCharges, ManaCost = s.baseManaCost, SpiritEnergyCost = s.baseSpiritEnergyCost,
                    Cooldown = s.baseCooldown, CooldownTick = s.currentCooldownTick, UnlockCost = s.unlockCost,
                    RemainingChaosOrbs = s.remainingChaosOrbs, Pierce = s.basePierce, Unlocked = s.isUnlockedBaseOnRequirements
                }).ToArray() };
            carry.Configuration.playerSkillSettings.startingPortalLevel = carry.PortalLevel;
            carry.Validate(); return carry;
        }
        private static WorldSettingsData CaptureConfiguration()
        {
            WorldSettingsData source = WorldSettings.Instance.worldSettingsData;
            // Native templates contain Unity Sprite references. Never serialize them into portable rules.
            return JsonConvert.DeserializeObject<WorldSettingsData>(JsonConvert.SerializeObject(new
            {
                source.worldType, source.victoryCondition, source.playerSkillSettings, source.mapSettings, source.villageSettings,
                factionSettings = new { source.factionSettings.disableNewFactions, source.factionSettings.disableFactionIdeologyChanges }
            }));
        }
        internal WorldSettingsData RestoreConfiguration(WorldSettingsData local)
        {
            if (Configuration == null) return local;
            WorldSettingsData copy = JsonConvert.DeserializeObject<WorldSettingsData>(JsonConvert.SerializeObject(Configuration));
            copy.mapSettings = local.mapSettings;
            copy.factionSettings.factionTemplates = local.factionSettings.factionTemplates;
            copy.playerSkillSettings.startingPortalLevel = PortalLevel;
            return copy;
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
            if (!Enum.IsDefined(typeof(PLAYER_ARCHETYPE), Archetype) || PortalLevel < 1 || PortalChoices == null)
                throw new InvalidDataException("Invalid portable Portal progression.");
            if (Configuration != null && (Configuration.playerSkillSettings == null || Configuration.villageSettings == null
                || Configuration.factionSettings == null || Configuration.mapSettings == null
                || Configuration.factionSettings.factionTemplates == null || Configuration.factionSettings.factionTemplates.Count != 0))
                throw new InvalidDataException("Invalid portable native configuration.");
            var upgradeIdentities = new HashSet<long>();
            foreach (CampaignPortalChoice choice in PortalChoices)
            {
                if (choice == null || choice.Tier < 1 || choice.ItemIndex < 0 || choice.PowerChoices == null
                    || !Enum.IsDefined(typeof(PLAYER_SKILL_TYPE), choice.ChosenPower)
                    || !upgradeIdentities.Add((long)choice.Tier << 32 | (uint)choice.ItemIndex))
                    throw new InvalidDataException("Invalid or duplicate portable Portal upgrade.");
                var offered = new HashSet<PLAYER_SKILL_TYPE>();
                foreach (PLAYER_SKILL_TYPE power in choice.PowerChoices)
                    if (!Enum.IsDefined(typeof(PLAYER_SKILL_TYPE), power) || power != PLAYER_SKILL_TYPE.NONE && !offered.Add(power))
                        throw new InvalidDataException("Invalid or duplicate portable Portal power choice.");
                if (choice.ChosenPower != PLAYER_SKILL_TYPE.NONE && offered.Count != 0 && !offered.Contains(choice.ChosenPower))
                    throw new InvalidDataException("Portable Portal power was not offered by its upgrade.");
            }
        }
        private void ValidateDestination(PlayerSkillManager manager, PlayerSkillComponent component, ThePortal portal)
        {
            PLAYER_ARCHETYPE expected = Archetype == PLAYER_ARCHETYPE.Normal ? PLAYER_ARCHETYPE.Progression_Ravager : Archetype;
            if (manager.selectedArchetype != expected) throw new InvalidDataException("Destination loadout does not match the portable lord.");
            if (component.portalUpgradeItems.Count == 0 || PortalLevel > component.portalUpgradeItems.Keys.Max())
                throw new InvalidDataException("Destination does not provide the portable Portal level.");
            if (portal != null && portal.level != PortalLevel) throw new InvalidDataException("Destination Portal level differs from portable progression.");
            if (PortalChoices.Length == 0) return;
            if (PortalChoices.Length != component.portalUpgradeItems.Values.Sum(items => items.Length))
                throw new InvalidDataException("Portable Portal upgrades do not cover the destination loadout.");
            foreach (CampaignPortalChoice saved in PortalChoices)
            {
                if (!component.portalUpgradeItems.TryGetValue(saved.Tier, out PortalUpgradeItem[] items)
                    || saved.ItemIndex >= items.Length) throw new InvalidDataException("Destination lacks a portable Portal tier/item.");
                PortalUpgradeItem native = items[saved.ItemIndex];
                if (native.itemIndex != saved.ItemIndex || saved.PowerChoices.Length != native.powerChoices.Length)
                    throw new InvalidDataException("Portable Portal tier/item identity differs from the destination.");
                if (saved.PowerChoices.Any(power => power != PLAYER_SKILL_TYPE.NONE && !manager.allPlayerSkillsData.ContainsKey(power))
                    || saved.ChosenPower != PLAYER_SKILL_TYPE.NONE && !manager.allPlayerSkillsData.ContainsKey(saved.ChosenPower))
                    throw new InvalidDataException("Destination does not provide a portable Portal power.");
                if (native.portalUpgradeType == Portal_Upgrade_Type.Essential)
                {
                    if (saved.ChosenPower != native.essentialSkillToUnlock)
                        throw new InvalidDataException("Portable essential Portal power differs from the destination.");
                }
                else if (saved.ChosenPower != PLAYER_SKILL_TYPE.NONE && !saved.PowerChoices.Contains(saved.ChosenPower))
                    throw new InvalidDataException("Portable Portal power was not offered by its upgrade.");
            }
        }
        private void ApplyPortalChoices(PlayerSkillComponent component)
        {
            if (PortalChoices.Length == 0) return;
            foreach (CampaignPortalChoice saved in PortalChoices)
            {
                PortalUpgradeItem native = component.portalUpgradeItems[saved.Tier][saved.ItemIndex];
                for (int i = 0; i < native.powerChoices.Length; i++) native.powerChoices[i] = saved.PowerChoices[i];
                native.SetChosenPowerForUpgrade(saved.ChosenPower);
            }
        }
        internal void Apply()
        {
            Validate();
            var manager = PlayerSkillManager.Instance; var player = PlayerManager.Instance.player;
            var component = player.playerSkillComponent;
            ThePortal portal = player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL) as ThePortal;
            ValidateDestination(manager, component, portal);
            foreach (CampaignSkill saved in Skills)
                if (!manager.allPlayerSkillsData.ContainsKey(saved.Type))
                    throw new InvalidDataException("Destination does not provide campaign skill " + saved.Type + ".");
            ApplyPortalChoices(component);
            var powers = new HashSet<PLAYER_SKILL_TYPE>(Powers);
            foreach (CampaignSkill saved in Skills)
            {
                SkillData skill = manager.GetSkillData(saved.Type);
                // Detach the destination timer, without pretending that a cooldown completed.
                var cooldownTick = AccessTools.MethodDelegate<SignalHandler.SignalListener>(
                    AccessTools.Method(typeof(SkillData), "PerTickCooldown"), skill);
                SignalHandler.RemoveListener(Signals.TICK_STARTED, cooldownTick, false);
                skill.SetCurrentLevel(saved.Level);
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
                skill.SetPierce(saved.Pierce); skill.ResetAllChargesUIText();
                if (skill.isInUse || skill.isTemporarilyInUse) skill.OnLoadSpell();
            }
            CurrenciesComponent currency = player.currenciesComponent;
            Capacity(currency) = MaxChaoticEnergy;
            currency.AdjustChaoticEnergyWithoutAffectingSpiritEnergy(ChaoticEnergy - currency.chaoticEnergy);
            // The native SpiritEnergy adjustment is victory-mode-gated. The saved
            // counter still follows the campaign when the destination mode hides it.
            RestoreSpiritEnergy();
            currency.AdjustManaNoLimit(Mana - currency.mana, shouldBroadcastSignal: true);
            SaveManager.Instance.currentSaveDataPlayer.SetMoreLoadoutOptions(MoreLoadoutOptions);
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
