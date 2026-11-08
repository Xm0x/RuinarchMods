using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Inner_Maps.Location_Structures.Components;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TruePlanet
{
    // A dropdown target backed by an actual immutable native settlement, never a live fake settlement.
    internal sealed class CampaignRaidTarget : IStoredTarget
    {
        internal readonly int Tile;
        internal readonly string SettlementId, SettlementName, SnapshotId;
        private readonly string _campaign;
        private readonly BookmarkableEventDispatcher _events = new BookmarkableEventDispatcher();
        internal CampaignRaidTarget(int tile, string id, string name, string snapshot, string campaign)
        { Tile = tile; SettlementId = id; SettlementName = name; SnapshotId = snapshot; _campaign = campaign; }
        public string persistentID => SettlementId;
        public STORED_TARGET_TYPE storedTargetType => STORED_TARGET_TYPE.Village;
        public string name => SettlementName;
        public string bookmarkName => SettlementName + " (Travel Portal)";
        public BOOKMARK_TYPE bookmarkType => BOOKMARK_TYPE.Text;
        public BookmarkableEventDispatcher bookmarkEventDispatcher => _events;
        public string iconRichText => string.Empty;
        public bool isTargetted { get; set; }
        public bool isStoredAsTarget => false;
        public bool IsValidForStoreTarget() => false;
        public bool CanBeStoredAsTarget() => false;
        public void SetAsStoredTarget(bool state)
        { if (state) throw new InvalidOperationException("Remote settlements are selected from the campaign, not stored in the local map."); }
        public bool IsValidTargetForPartyStructure(LocationStructure structure) => TileGame.Active != null
            && TileGame.Active.CampaignId == _campaign && TileGame.Active.TileId == TileGame.Campaign.BaseTile
            && structure is Kennel && structure.settlementLocation == PlayerManager.Instance.player.playerSettlement
            && TileGame.Campaign.Tiles.Any(snapshot => snapshot.TileId == Tile && snapshot.Held && snapshot.SnapshotId == SnapshotId);
        public Sprite GetPortraitSprite() => LandmarkManager.Instance.GetStructureData(STRUCTURE_TYPE.CITY_CENTER).structureSprite;
        public void OnSelectBookmark() => PlanetView.Open();
        public void RemoveBookmark() => PlayerManager.Instance.player.bookmarkComponent.RemoveBookmark(this);
        public void OnHoverOverBookmarkItem(UIHoverPosition position) => UIManager.Instance.ShowSmallInfo("Raid " + SettlementName + " through its Travel Portal.", position, SettlementName);
        public void OnHoverOutBookmarkItem() => UIManager.Instance.HideSmallInfo();
    }
    internal static class CampaignRaidUI
    {
        private static readonly Dictionary<string, CampaignRaidTarget[]> Targets = new Dictionary<string, CampaignRaidTarget[]>();
        private static readonly SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[] RaidBehaviours =
        {
            SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Destroy_Supplies, SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Destroy_Structures,
            SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Harass_Villagers, SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Destroy_Defenses
        };
        private static readonly ConditionalWeakTable<SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[], SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[]> BehaviourChoices =
            new ConditionalWeakTable<SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[], SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[]>();
        private static readonly Action<SpawnPartyUIController, IStoredTarget> UpdateTargets =
            AccessTools.MethodDelegate<Action<SpawnPartyUIController, IStoredTarget>>(AccessTools.Method(typeof(SpawnPartyUIController), "UpdateTargetsDropdown"));
        private static readonly Action<SpawnPartyUIController> UpdateAttackers =
            AccessTools.MethodDelegate<Action<SpawnPartyUIController>>(AccessTools.Method(typeof(SpawnPartyUIController), "UpdateAttackerLocationUI"));
        private static readonly Action<SpawnPartyUIController> UpdateTitle =
            AccessTools.MethodDelegate<Action<SpawnPartyUIController>>(AccessTools.Method(typeof(SpawnPartyUIController), "UpdateWindowTitle"));
        internal static void Install(Harmony harmony)
        {
            if (NativeWorker.IsWorker) return;
            foreach (Type type in typeof(CampaignRaidUI).GetNestedTypes(BindingFlags.NonPublic))
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
        }
        internal static bool IsNativeRaidTarget(int tile, string id) => RemoteTargets().Any(target => target.Tile == tile && target.SettlementId == id);
        private static IEnumerable<CampaignRaidTarget> RemoteTargets()
        {
            CampaignManifest campaign = TileGame.Campaign;
            foreach (TileSnapshot snapshot in campaign.Tiles)
            {
                if (!snapshot.Held || snapshot.TileId == campaign.ActiveTile) continue;
                string key = campaign.CampaignId + "/" + snapshot.SnapshotId;
                if (!Targets.TryGetValue(key, out CampaignRaidTarget[] targets))
                {
                    string path = TileGame.Store.Resolve(new TileContext { Planet = TileGame.Active.Planet, TileId = snapshot.TileId, CampaignId = campaign.CampaignId }, snapshot);
                    var found = new List<CampaignRaidTarget>();
                    using (var archive = ZipFile.OpenRead(path))
                    using (var text = new StreamReader(archive.GetEntry("mainSave.sav").Open()))
                    using (var reader = new JsonTextReader(text))
                        while (reader.Read())
                        {
                            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 2 || (string)reader.Value != "settlementSaves") continue;
                            if (!reader.Read()) throw new InvalidDataException("The remote native settlement list is incomplete.");
                            foreach (JObject settlement in JArray.Load(reader))
                                if ((string)settlement["locationType"] == "VILLAGE")
                                {
                                    string id = (string)settlement["_persistentID"], name = (string)settlement["name"];
                                    if (!Guid.TryParse(id, out _) || string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("The remote settlement has no native identity.");
                                    found.Add(new CampaignRaidTarget(snapshot.TileId, id, name, snapshot.SnapshotId, campaign.CampaignId));
                                }
                            break;
                        }
                    targets = found.ToArray(); Targets[key] = targets;
                }
                foreach (CampaignRaidTarget target in targets) yield return target;
            }
        }
        [HarmonyPatch(typeof(PartyStructureComponent), nameof(PartyStructureComponent.GetAllPossibleTargets))]
        private static class RemoteRaidTargets
        {
            private static void Postfix(PartyStructureComponent __instance, SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR p_behaviour, ref List<IStoredTarget> __result)
            {
                if (TileGame.Active == null || TileGame.Active.TileId != TileGame.Campaign.BaseTile || !(__instance.owner is Kennel)
                    || !TryRaidType(p_behaviour, out _)) return;
                var targets = new List<IStoredTarget>(__result); targets.AddRange(RemoteTargets()); __result = targets;
            }
        }
        private static bool IsBaseKennel(LocationStructure structure) => structure is Kennel && TileGame.Active != null
            && TileGame.Active.TileId == TileGame.Campaign.BaseTile
            && structure.settlementLocation == PlayerManager.Instance.player.playerSettlement;
        private static SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[] AddRaidBehaviours(SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[] original) =>
            original.Concat(RaidBehaviours).Distinct().ToArray();
        [HarmonyPatch(typeof(PartyStructureComponent), "get_availableBehaviours")]
        private static class KennelRaidBehaviours
        {
            private static void Postfix(PartyStructureComponent __instance, ref SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR[] __result)
            {
                if (IsBaseKennel(__instance.owner) && __result != null)
                    __result = BehaviourChoices.GetValue(__result, AddRaidBehaviours);
            }
        }
        [HarmonyPatch(typeof(SpawnPartyUIController), nameof(SpawnPartyUIController.Show))]
        private static class KennelPartyTitle
        {
            private static void Postfix(SpawnPartyUIController __instance, LocationStructure ____attackerLocation,
                SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR ____chosenBehaviour, ref STRUCTURE_PARTY_TYPE ____partyType)
            {
                if (!IsBaseKennel(____attackerLocation)) return;
                ____partyType = TryRaidType(____chosenBehaviour, out _) ? STRUCTURE_PARTY_TYPE.Raid : ____attackerLocation.partyStructureComponent.structurePartyType;
                UpdateTitle(__instance);
            }
        }
        [HarmonyPatch(typeof(SpawnPartyUIController), nameof(SpawnPartyUIController.OnBehaviourDropdownValueChanged))]
        private static class RefreshKennelRaidTargets
        {
            private static void Postfix(SpawnPartyUIController __instance, LocationStructure ____attackerLocation,
                SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR ____chosenBehaviour, ref IStoredTarget ____chosenTarget, ref STRUCTURE_PARTY_TYPE ____partyType)
            {
                if (!__instance.isShowing || !IsBaseKennel(____attackerLocation)) return;
                ____chosenTarget = null;
                ____partyType = TryRaidType(____chosenBehaviour, out _) ? STRUCTURE_PARTY_TYPE.Raid : ____attackerLocation.partyStructureComponent.structurePartyType;
                UpdateTargets(__instance, null); UpdateAttackers(__instance); UpdateTitle(__instance);
            }
        }
        [HarmonyPatch(typeof(SpawnPartyUIController), "TryAddStructuresToValidAttackerLocationsList",
            new[] { typeof(LocationStructure), typeof(List<IStoredTarget>) })]
        private static class CampaignKennelAttackers
        {
            private static bool Prefix(LocationStructure p_structure, List<IStoredTarget> p_allValidAttackerLocations,
                IStoredTarget ____chosenTarget, SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR ____chosenBehaviour)
            {
                if (!(____chosenTarget is CampaignRaidTarget target) || !IsBaseKennel(p_structure)
                    || !TryRaidType(____chosenBehaviour, out _)) return true;
                if (target.IsValidTargetForPartyStructure(p_structure) && p_structure.partyStructureComponent.IsAvailable()
                    && p_structure.partyStructureComponent.party == null && !CampaignMissions.Instance.HoldsStructure(p_structure.persistentID)
                    && !p_allValidAttackerLocations.Contains(p_structure))
                    p_allValidAttackerLocations.Add(p_structure);
                return false;
            }
        }
        private static bool TryRaidType(SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR behaviour, out DEMON_RAID_TYPE type)
        {
            switch (behaviour)
            {
                case SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Destroy_Supplies: type = DEMON_RAID_TYPE.Destroy_Supplies; return true;
                case SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Destroy_Structures: type = DEMON_RAID_TYPE.Destroy_Structures; return true;
                case SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Harass_Villagers: type = DEMON_RAID_TYPE.Harass_Villagers; return true;
                case SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR.Destroy_Defenses: type = DEMON_RAID_TYPE.Destroy_Defenses; return true;
                default: type = default; return false;
            }
        }
        [HarmonyPatch(typeof(SpawnPartyUIController), nameof(SpawnPartyUIController.OnClickSpawnParty))]
        private static class DispatchThroughMainPortal
        {
            private static bool Prefix(SpawnPartyUIController __instance, IStoredTarget ____chosenTarget, LocationStructure ____attackerLocation,
                Character ____demonLeader, List<Character> ____chosenSummons, SPECIFIC_STRUCTURE_PARTY_BEHAVIOUR ____chosenBehaviour)
            {
                if (!(____chosenTarget is CampaignRaidTarget target)) return true;
                if (!target.IsValidTargetForPartyStructure(____attackerLocation) || !TryRaidType(____chosenBehaviour, out DEMON_RAID_TYPE raidType))
                    throw new InvalidOperationException("Select an available kennel and campaign settlement.");
                Character[] actors = new[] { ____demonLeader }.Concat(____chosenSummons).ToArray();
                UIManager.Instance.yesNoConfirmation.ShowYesNoConfirmation("Send raid", "Send this party through the main Portal to " + target.SettlementName + "?", () =>
                {
                    try
                    {
                        __instance.HideUI();
                        CampaignMissions.Instance.DispatchRaid(target.Tile, actors, target.SettlementId, raidType, ____attackerLocation);
                    }
                    catch (Exception error)
                    {
                        TruePlanetMod.Log.Warning("Campaign raid could not be dispatched: " + error);
                        PlayerUI.Instance.ShowGeneralConfirmation("Raid unavailable", error.Message);
                    }
                }, null, showCover: true, 150);
                return false;
            }
        }
    }
}
