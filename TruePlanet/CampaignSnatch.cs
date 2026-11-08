using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;

namespace TruePlanet
{
    internal sealed partial class CampaignMissions
    {
        private bool _scanSnatches;
        private bool TrackNativeSnatches()
        {
            if (!_scanSnatches || _context.TileId == TileGame.Campaign.BaseTile) return false;
            foreach (Party party in DatabaseManager.Instance.partyDatabase.allParties.Values)
            {
                if (!(party.currentQuest is DemonSnatchPartyQuest quest) || !quest.isDemonicQuest
                    || _records.Values.Any(record => record.PartyId == party.persistentID)) continue;
                StartOperation(AdoptSnatch(party, quest)); return true;
            }
            _scanSnatches = false; return false;
        }
        private IEnumerator AdoptSnatch(Party party, DemonSnatchPartyQuest quest)
        {
            if (_records.Values.Any(record => Active(record) && record.DestinationTile == _context.TileId))
                throw new InvalidOperationException("Finish the current expedition before sending another party to this destination.");
            Character[] actors = party.membersThatJoinedQuest.Where(actor => !actor.isDead).ToArray();
            if (actors.Length == 0 || quest.targetCharacter == null) throw new InvalidOperationException("The native snatch party has no living members or target.");
            TileSnapshot destination = null; yield return SaveActive(snapshot => destination = snapshot);
            TileSnapshot source = TileGame.Campaign.Tiles.Single(snapshot => snapshot.TileId == TileGame.Campaign.BaseTile);
            MissionRecord record = _store.Create(MissionKind.Snatch, source.TileId, _context.TileId, source, destination,
                actors.Select(actor => actor.persistentID).ToArray(), quest.targetCharacter.persistentID, null, default);
            record.SourceAfter = source;
            record = MissionStore.Transition(record, MissionPhase.Departed);
            record = MissionStore.Transition(record, MissionPhase.Fighting);
            record.PartyId = party.persistentID; record.QuestId = quest.persistentID;
            record.Demons = actors.Where(actor => actor.minion != null).ToDictionary(actor => actor.persistentID, actor => actor.minion.minionType);
            record.Monsters = actors.OfType<Summon>().ToDictionary(actor => actor.persistentID, actor => actor.summonType);
            record.DestinationAfter = destination; _records.Add(record.Id, record); Publish();
        }
        [HarmonyPatch(typeof(PartyQuestBoard), nameof(PartyQuestBoard.CreateDemonSnatchPartyQuest))]
        private static class SnatchThroughTravelPortal
        {
            private static void Prefix(ref LocationStructure dropStructure)
            {
                if (NativeWorker.IsWorker || TileGame.Active == null || TileGame.Active.TileId == TileGame.Campaign.BaseTile) return;
                LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
                if (!(portal is TravelPortal)) throw new InvalidOperationException("Place a Travel Portal before sending a snatch party home.");
                dropStructure = portal;
            }
        }
        [HarmonyPatch(typeof(DemonSnatchPartyQuest), nameof(DemonSnatchPartyQuest.OnAcceptQuest))]
        private static class TrackAcceptedSnatch
        {
            private static void Postfix() { if (Instance != null) Instance._scanSnatches = true; }
        }
    }
}
