using System;
using System.Reflection;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using UnityEngine;

namespace TruePlanet
{
    // The native portal progression contract is retained, but the saved content type is distinct.
    internal sealed class TravelPortal : ThePortal
    {
        internal const string ContentId = "trueplanet.travel_portal";
        internal static STRUCTURE_TYPE Kind;
        private static readonly AccessTools.FieldRef<LocationStructure, STRUCTURE_TYPE> StructureKind =
            AccessTools.FieldRefAccess<LocationStructure, STRUCTURE_TYPE>("<structureType>k__BackingField");
        private static bool _placing;
        internal static bool IsTravelMap => TileGame.Active != null && TileGame.Active.BaseTile >= 0
            && TileGame.Active.TileId != TileGame.Active.BaseTile;

        private TravelPortal(STRUCTURE_TYPE type, Region region) : base(region)
        {
            StructureKind(this) = type; name = "Travel Portal";
        }
        private TravelPortal(Region region, SaveDataDemonicStructure data) : base(region, data) { name = "Travel Portal"; }

        internal static void Install(Harmony harmony)
        {
            Kind = ModContent.RegisterStructure(new StructureRegistration
            {
                Id = ContentId, DisplayName = "Travel Portal", PrefabSource = STRUCTURE_TYPE.THE_PORTAL,
                Factory = (type, region) => new TravelPortal(type, region),
                LoadFactory = (type, region, save) => new TravelPortal(region, (SaveDataDemonicStructure)save),
                SkillDataFrom = PLAYER_SKILL_TYPE.CRYPT,
                CreateSkill = (structure, skill) => new PortalSkill(structure, skill)
            }).StructureType;
            foreach (Type type in typeof(TravelPortal).GetNestedTypes(BindingFlags.NonPublic))
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
        }
        private sealed class PortalSkill : DemonicStructurePlayerSkill
        {
            private readonly PLAYER_SKILL_TYPE _type;
            public override PLAYER_SKILL_TYPE type => _type;
            public override string name => "Travel Portal";
            public override string localizedName => name;
            public override string description => "Connects this land to the main Portal.";
            public override string localizedDescription => description;
            public override Vector2Int size => new Vector2Int(6, 4);
            internal PortalSkill(STRUCTURE_TYPE structure, PLAYER_SKILL_TYPE type) { structureType = structure; _type = type; }
        }

        [HarmonyPatch(typeof(Player_Input.PickPortalInputModule), "PlacePortal")]
        private static class PlacementScope
        {
            private static void Prefix() { _placing = IsTravelMap; }
            private static Exception Finalizer(Exception __exception) { _placing = false; return __exception; }
        }
        [HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.CreateNewStructureAt))]
        private static class CreateTravelPortal
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(ref STRUCTURE_TYPE structureType)
            {
                if (_placing && structureType == STRUCTURE_TYPE.THE_PORTAL) structureType = Kind;
            }
        }
        [HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.LoadNewStructureAt))]
        private static class MigrateDestinationPortal
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(ref STRUCTURE_TYPE structureType, SaveDataLocationStructure saveDataLocationStructure)
            {
                if (!IsTravelMap || structureType != STRUCTURE_TYPE.THE_PORTAL) return;
                structureType = Kind; saveDataLocationStructure.structureType = Kind; saveDataLocationStructure.name = "Travel Portal";
            }
        }
        // Native systems ask for their local portal anchor through these two settlement APIs.
        // They receive the genuine TravelPortal, never another THE_PORTAL entity or duplicated base.
        [HarmonyPatch(typeof(BaseSettlement), nameof(BaseSettlement.GetFirstStructureOfType))]
        private static class FirstLocalAnchor
        {
            private static void Postfix(BaseSettlement __instance, STRUCTURE_TYPE type, ref LocationStructure __result) => LocalAnchor(__instance, type, ref __result);
        }
        [HarmonyPatch(typeof(BaseSettlement), nameof(BaseSettlement.GetRandomStructureOfType))]
        private static class RandomLocalAnchor
        {
            private static void Postfix(BaseSettlement __instance, STRUCTURE_TYPE type, ref LocationStructure __result) => LocalAnchor(__instance, type, ref __result);
        }
        private static void LocalAnchor(BaseSettlement settlement, STRUCTURE_TYPE type, ref LocationStructure result)
        {
            if (result != null || type != STRUCTURE_TYPE.THE_PORTAL || !IsTravelMap
                || PlayerManager.Instance?.player?.playerSettlement != settlement) return;
            result = settlement.GetFirstStructureOfType(Kind);
        }
        [HarmonyPatch(typeof(ThePortal), nameof(ThePortal.GainPowersFromStartingLevel))]
        private static class NoNewBasePowers
        {
            private static bool Prefix(ThePortal __instance) => !(__instance is TravelPortal);
        }
        [HarmonyPatch(typeof(PlayerUnderlingsComponent), "SpawnDemonUnderlings")]
        private static class KeepIdleDemonsHome
        {
            private static bool Prefix() => !IsTravelMap;
        }
        [HarmonyPatch(typeof(PlayerUnderlingsComponent), nameof(PlayerUnderlingsComponent.TrySpawnMissingDemons))]
        private static class KeepReplenishedDemonsHome
        {
            private static bool Prefix() => !IsTravelMap;
        }
        [HarmonyPatch(typeof(PlayerUnderlingsComponent), "SpawnMonsterUnderlings")]
        private static class KeepIdleMonstersHome
        {
            private static bool Prefix() => !IsTravelMap;
        }
        [HarmonyPatch(typeof(InitialWorldSetupMenu), nameof(InitialWorldSetupMenu.Show))]
        private static class DestinationPrompt
        {
            private static void Postfix(InitialWorldSetupMenu __instance)
            {
                if (!IsTravelMap) return;
                foreach (TMPro.TMP_Text label in __instance.pickPortalMessage.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    label.text = "Choose a location for your Travel Portal";
            }
        }
    }
}
