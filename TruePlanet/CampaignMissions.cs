using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Newtonsoft.Json;
using UnityEngine;
using UtilityScripts;

namespace TruePlanet
{
    [DefaultExecutionOrder(-11000)]
    internal sealed partial class CampaignMissions : MonoBehaviour
    {
        internal static CampaignMissions Instance;
        internal bool Busy { get; private set; }
        internal string LastError { get; private set; }
        private TileContext _context;
        private TileStore _tiles;
        private MissionStore _store;
        private readonly Dictionary<string, MissionRecord> _records = new Dictionary<string, MissionRecord>();
        private NativeWorkerProcess _worker;
        private NativeWork _work;
        private MissionRecord _running;
        private WorkerControl _control;
        private float _nextControl;
        private bool _faulted;
        private string _lastCheckpoint;
        private readonly HashSet<string> _foregroundReturns = new HashSet<string>();

        internal static void Install(Harmony harmony)
        {
            if (NativeWorker.IsWorker || Instance != null) return;
            var root = new GameObject("TruePlanet campaign expeditions"); DontDestroyOnLoad(root);
            Instance = root.AddComponent<CampaignMissions>();
            harmony.Patch(AccessTools.Method(typeof(GameManager), "TickEnded"), postfix: new HarmonyMethod(typeof(CampaignMissions), nameof(AfterTick)));
            foreach (Type type in typeof(CampaignMissions).GetNestedTypes(BindingFlags.NonPublic))
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
        }
        private static int Reserved<T>(T type, Func<MissionRecord, Dictionary<string, T>> entries)
        {
            if (Instance == null || TileGame.Active == null) return 0;
            // Native loading queries population before travel adopts its canonical manifest.
            // Keep the source owner until that transaction commits; worker snapshots can
            // contain an older mission list and must not replace foreground ownership.
            if (TileTravel.Instance == null || TileTravel.Instance.Stage == TravelStage.Idle) Instance.Bind();
            int count = 0;
            foreach (MissionRecord mission in Instance._records.Values)
            {
                if (!Active(mission)) continue;
                foreach (var entry in entries(mission))
                    if (EqualityComparer<T>.Default.Equals(entry.Value, type) && !mission.DeadActors.Contains(entry.Key)
                        && CharacterManager.Instance.GetCharacterByPersistentID(entry.Key) == null) count++;
            }
            return count;
        }
        [HarmonyPatch(typeof(Player), nameof(Player.GetNumberOfAliveMonstersInPlayerFaction), new Type[] { typeof(MINION_TYPE) })]
        private static class CountTravellingDemons
        {
            private static void Postfix(MINION_TYPE p_type, ref int __result) { __result += Reserved(p_type, mission => mission.Demons); }
        }
        [HarmonyPatch(typeof(Player), nameof(Player.GetNumberOfAliveMonstersInPlayerFaction), new Type[] { typeof(SUMMON_TYPE) })]
        private static class CountTravellingMonsters
        {
            private static void Postfix(SUMMON_TYPE p_type, ref int __result) { __result += Reserved(p_type, mission => mission.Monsters); }
        }
        private void OnApplicationQuit() { NativeWorker.Stop(); }
        private void OnDestroy() { StopWorker(); if (Instance == this) Instance = null; }
        private static void AfterTick()
        {
            var owner = Instance;
            if (owner == null || owner._context != TileGame.Active) return;
            foreach (MissionRecord record in owner._records.Values)
                if (record.Phase == MissionPhase.Departed || record.Phase == MissionPhase.Fighting)
                {
                    record.ForegroundTicks++;
                    if (record.Phase == MissionPhase.Fighting && record.DestinationTile == owner._context.TileId) record.WorkerTick++;
                }
        }
        internal static void BeforeSave(CampaignManifest campaign)
        {
            var owner = Instance;
            if (owner == null || owner._context != TileGame.Active) return;
            campaign.Missions = owner._records.Values.Select(owner._store.Record).ToArray();
        }
        private void Bind()
        {
            if (ReferenceEquals(_context, TileGame.Active)) return;
            StopWorker(); _records.Clear(); _foregroundReturns.Clear(); _faulted = false; LastError = null;
            _context = TileGame.Active;
            if (_context == null) return;
            _tiles = TileGame.Store; _store = _tiles.Missions(_context.CampaignId);
            foreach (MissionReference reference in TileGame.Campaign.Missions)
            { MissionRecord record = _store.Read(reference); _records.Add(record.Id, record); }
            _scanSnatches = true;
        }
        private static bool Ready => TileGame.Active != null && GameManager.Instance != null && GameManager.Instance.gameHasStarted
            && UIManager.Instance != null && !LevelLoaderManager.Instance.IsLoadingScreenActive();
        private static bool Saving => SaveManager.Instance.saveCurrentProgressManager.isSaving || SaveManager.Instance.saveCurrentProgressManager.isWritingToDisk;
        private void Update()
        {
            if (Busy || TileTravel.Instance != null && TileTravel.Instance.Stage != TravelStage.Idle) return;
            if (!Ready) { if (TileGame.Active == null) Bind(); return; }
            try
            {
                Bind(); if (_faulted || Saving) return;
                if (TrackNativeSnatches()) return;
                if (_worker != null)
                {
                    NativeWorkResult result = _worker.Result();
                    if (result != null) { StartOperation(Consume(result, true)); return; }
                    NativeWorkResult checkpoint = _worker.Checkpoint();
                    if (checkpoint != null && checkpoint.SaveName != _lastCheckpoint)
                    { StartOperation(Consume(checkpoint, false)); return; }
                    if (_worker.HasExited) throw new IOException("The expedition stopped before publishing a native checkpoint.");
                    if (Time.realtimeSinceStartup >= _nextControl)
                    {
                        _control.AllowedTicks = _running.ForegroundTicks; _control.Paused = GameManager.Instance.isPaused;
                        _control.Speed = GameManager.Instance.currProgressionSpeed; _worker.Control(_control); _nextControl = Time.realtimeSinceStartup + .1f;
                    }
                }
                foreach (MissionRecord record in _records.Values)
                {
                    if (record.Phase == MissionPhase.Preparing && record.SourceTile == _context.TileId)
                    { StartOperation(FinishDeparture(record)); return; }
                    if (record.Phase == MissionPhase.ReturnPreparing)
                    {
                        if (record.DestinationTile != _context.TileId) throw new InvalidDataException("Load the expedition's departure map to finish its return transfer.");
                        StartOperation(FinishReturnDeparture(record)); return;
                    }
                    if (record.Phase == MissionPhase.Returning)
                    {
                        if (record.DeadActors.Length == record.Actors.Length)
                        { _records[record.Id] = MissionStore.Transition(record, MissionPhase.Completed); Publish(); return; }
                        if (record.SourceTile == _context.TileId) { StartOperation(ReturnHome(record)); return; }
                        if (_worker == null) { Launch(record, true); return; }
                    }
                    if (record.Phase != MissionPhase.Departed && record.Phase != MissionPhase.Fighting) continue;
                    if (record.DestinationTile == _context.TileId) { ObserveForeground(record); if (Busy) return; }
                    else if (_worker == null) { Launch(record, false); return; }
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private void Fail(Exception error)
        {
            _faulted = true; LastError = error.Message;
            try { StopWorker(); } catch (Exception stopError) { LastError += "; " + stopError.Message; }
            TruePlanetMod.Log.Error("Expedition paused; ownership and last native checkpoint retained: " + error);
            if (PlayerUI.Instance != null) PlayerUI.Instance.ShowGeneralConfirmation("Expedition paused", "The expedition could not continue. Its last saved progress is retained. Reload your campaign to resume.");
        }
        private void StartOperation(IEnumerator operation)
        { Busy = true; StartCoroutine(Drive(operation)); }
        private IEnumerator Drive(IEnumerator operation)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(operation); Exception failure = null;
            while (stack.Count != 0)
            {
                bool more = false; object current = null;
                try { var step = stack.Peek(); more = step.MoveNext(); if (more) current = step.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator child) stack.Push(child); else yield return current;
            }
            while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
            Busy = false; if (failure != null) Fail(failure);
        }
        internal void DispatchRaid(int destination, Character[] actors, string targetId, DEMON_RAID_TYPE raidType, LocationStructure kennel)
        {
            if (!Ready || Busy || Saving || _faulted || TileTravel.Instance.Stage != TravelStage.Idle)
                throw new InvalidOperationException("Finish the current campaign operation before dispatching.");
            Bind(); CampaignManifest campaign = TileGame.Campaign;
            if (_context.TileId != campaign.BaseTile || destination == campaign.BaseTile || kennel == null || kennel.structureType != STRUCTURE_TYPE.KENNEL
                || kennel.settlementLocation != PlayerManager.Instance.player.playerSettlement)
                throw new InvalidOperationException("Dispatch kennel parties from the main base to another Travel Portal.");
            if (actors == null || actors.Length == 0 || actors.Distinct().Count() != actors.Length
                || actors.Any(actor => actor == null || actor.isDead || !actor.hasMarker || actor.faction != PlayerManager.Instance.player.playerFaction))
                throw new InvalidOperationException("Choose a living party from your main base.");
            TileSnapshot snapshot = campaign.Tiles.SingleOrDefault(tile => tile.TileId == destination);
            if (snapshot == null || !snapshot.Held || string.IsNullOrEmpty(targetId) || !Enum.IsDefined(typeof(DEMON_RAID_TYPE), raidType)
                || !CampaignRaidUI.IsNativeRaidTarget(destination, targetId))
                throw new InvalidOperationException("Visit the destination and place its Travel Portal before dispatching a raid.");
            if (_records.Values.Any(record => Active(record) && (record.DestinationTile == destination || record.SourceStructureId == kennel.persistentID
                || record.Actors.Intersect(actors.Select(actor => actor.persistentID)).Any())))
                throw new InvalidOperationException("This party or destination already belongs to an expedition.");
            if (!PlayerManager.Instance.player.currenciesComponent.CanAfford(CURRENCY.Mana, checked(actors.Length * 50)))
                throw new InvalidOperationException("Not enough Mana to send this party.");
            StartOperation(Dispatch(destination, actors, targetId, raidType, kennel));
        }
        private static bool Active(MissionRecord record) => record.Phase != MissionPhase.Completed && record.Phase != MissionPhase.Failed;
        internal bool HoldsStructure(string id) => _records.Values.Any(record => Active(record) && record.SourceStructureId == id);
        private IEnumerator AssembleAtPortal(Character[] actors, LocationStructure portal)
        {
            TileContext source = TileGame.Active;
            GameManager manager = GameManager.Instance;
            try
            {
                foreach (Character actor in actors)
                {
                    actor.partyComponent.currentParty?.RemoveMember(actor); actor.jobQueue.CancelAllJobs();
                    PortalTransport.BeginPortalWalk(actor, portal);
                }
                // A queued native walk must not expire while the player has paused time.
                while (true)
                {
                    if (!ReferenceEquals(source, TileGame.Active) || !ReferenceEquals(manager, GameManager.Instance)
                        || LevelLoaderManager.Instance.IsLoadingScreenActive()) yield break;
                    bool arrived = true;
                    foreach (Character actor in actors)
                    {
                        if (actor.isDead) throw new InvalidOperationException("A party member died before departure.");
                        if (actor.gridTileLocation != null && portal.tiles.Contains(actor.gridTileLocation)) continue;
                        arrived = false;
                    }
                    if (arrived) break;
                    yield return null;
                }
            }
            finally
            {
                foreach (Character actor in actors)
                {
                    PortalTransport.EndPortalWalk(actor);
                }
            }
        }
        private IEnumerator Dispatch(int destination, Character[] actors, string targetId, DEMON_RAID_TYPE raidType, LocationStructure kennel)
        {
            TileContext source = TileGame.Active;
            GameManager manager = GameManager.Instance;
            LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
            if (portal == null || portal is TravelPortal) throw new InvalidOperationException("The main Portal is unavailable.");
            yield return AssembleAtPortal(actors, portal);
            // An uncommitted assembly belongs only to its original native scene.
            if (!Ready || !ReferenceEquals(source, TileGame.Active) || !ReferenceEquals(manager, GameManager.Instance)) yield break;
            bool paused = GameManager.Instance.isPaused; UIManager.Instance.Pause();
            try
            {
                TileSnapshot before = null; yield return SaveActive(snapshot => before = snapshot);
                CampaignManifest campaign = TileGame.Campaign;
                MissionRecord record = _store.Create(MissionKind.Raid, _context.TileId, destination, before,
                    campaign.Tiles.Single(tile => tile.TileId == destination), actors.Select(actor => actor.persistentID).ToArray(), targetId, kennel.persistentID, raidType);
                PortalPayload payload = PortalTransport.Capture(actors); record.Cargo = payload.Cargo;
                record.ManaCost = checked(actors.Length * 50);
                record.Demons = actors.Where(actor => actor.minion != null).ToDictionary(actor => actor.persistentID, actor => actor.minion.minionType);
                record.Monsters = actors.OfType<Summon>().ToDictionary(actor => actor.persistentID, actor => actor.summonType);
                _store.WriteTransfer(record, System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
                _records.Add(record.Id, record); Publish();
                yield return FinishDeparture(record);
            }
            finally { if (!paused && UIManager.Instance != null) UIManager.Instance.Unpause(); }
        }
        private IEnumerator FinishDeparture(MissionRecord record)
        {
            Character[] actors = record.Actors.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).ToArray();
            if (actors.Any(actor => actor != null) && actors.Any(actor => actor == null)) throw new InvalidDataException("Departure has mixed native ownership; retain its source snapshot.");
            if (!record.ManaPaid)
            {
                if (!PlayerManager.Instance.player.currenciesComponent.CanAfford(CURRENCY.Mana, record.ManaCost))
                    throw new InvalidOperationException("Not enough Mana to finish this departure.");
                PlayerManager.Instance.player.currenciesComponent.AdjustMana(-record.ManaCost); record.ManaPaid = true;
            }
            if (actors.All(actor => actor != null)) PortalTransport.Depart(ReadPayload(record));
            TileSnapshot after = null; yield return SaveActive(snapshot => after = snapshot);
            MissionRecord next = MissionStore.Transition(record, MissionPhase.Departed); next.SourceAfter = after;
            _records[record.Id] = next; Publish();
        }
        private PortalPayload ReadPayload(MissionRecord record) => JsonConvert.DeserializeObject<PortalPayload>(File.ReadAllText(_store.ResolveTransfer(record)));
        private TileContext Context(int tile) => new TileContext { Planet = _context.Planet, CampaignId = _context.CampaignId,
            BaseTile = _context.BaseTile, TileId = tile };
        private void Launch(MissionRecord record, bool delivery)
        {
            int tile = delivery ? record.SourceTile : record.DestinationTile;
            TileSnapshot snapshot = delivery ? TileGame.Campaign.Tiles.Single(value => value.TileId == tile) : record.DestinationAfter ?? record.DestinationBefore;
            var work = new NativeWork
            {
                MissionId = record.Id, CampaignId = record.CampaignId, WorldId = record.WorldId, TileId = tile,
                Actors = record.Actors, DeadActors = record.DeadActors, TargetId = record.TargetId, RaidType = record.RaidType, Kind = record.Kind,
                PartyId = record.PartyId, QuestId = record.QuestId, Returning = record.WorkerReturning,
                ConsumedTicks = record.WorkerTick, Carry = TileGame.Campaign.Carry, Deliver = delivery
            };
            var control = new WorkerControl { AllowedTicks = record.ForegroundTicks, Paused = GameManager.Instance.isPaused, Speed = GameManager.Instance.currProgressionSpeed };
            string payload = delivery || record.Phase == MissionPhase.Departed ? _store.ResolveTransfer(record) : null;
            _worker = NativeWorker.Start(work, _tiles.Resolve(Context(tile), snapshot), payload, control);
            _work = work; _control = control; _running = record; _lastCheckpoint = null;
        }
        private IEnumerator Consume(NativeWorkResult result, bool terminal)
        {
            if (result.Error != null) throw new IOException(result.Error);
            result.Validate(_work, _running.ForegroundTicks);
            if (terminal != (result.Handoff || result.Returned || result.Delivered)) throw new InvalidDataException("Expedition result delivery mode is inconsistent.");
            string root = _worker.Root;
            if (terminal) { _worker.Dispose(); _worker = null; }
            string path = Path.Combine(root, "Data", "Ruinarch Game Saves", result.SaveName);
            VerifyHash(path, result.SaveSha256);
            TileContext context;
            using (var archive = ZipFile.OpenRead(path))
            using (var reader = new StreamReader(archive.GetEntry("ModData/trueplanet.tile.json").Open()))
                context = JsonConvert.DeserializeObject<TileContext>(reader.ReadToEnd());
            if (context.CampaignId != _work.CampaignId || context.TileId != _work.TileId) throw new InvalidDataException("Native worker output changed its map owner.");
            TileSnapshot snapshot = _tiles.ImportCompletedNativeSave(context, path, result.PortalHeld);
            MissionRecord next = JsonConvert.DeserializeObject<MissionRecord>(JsonConvert.SerializeObject(_running));
            next.WorkerTick = result.ConsumedTicks; next.PartyId = result.PartyId; next.QuestId = result.QuestId;
            next.DeadActors = result.DeadActors; next.WorkerReturning = result.Returning; next.NativeEndReason = result.NativeEndReason;
            if (result.Delivered)
            { next.SourceAfter = snapshot; next = MissionStore.Transition(next, MissionPhase.Completed); }
            else
            {
                next.DestinationAfter = snapshot;
                if (next.Phase == MissionPhase.Departed) next = MissionStore.Transition(next, MissionPhase.Fighting);
                if (result.Returned)
                {
                    next = MissionStore.Transition(next, MissionPhase.Returning);
                    if (result.PayloadName != null)
                    {
                        string payloadPath = Path.Combine(root, result.PayloadName); VerifyHash(payloadPath, result.PayloadSha256);
                        PortalPayload payload = JsonConvert.DeserializeObject<PortalPayload>(File.ReadAllText(payloadPath));
                        string[] survivors = next.Actors.Except(next.DeadActors).OrderBy(id => id).ToArray();
                        string[] extra = payload.Actors.Except(survivors).ToArray();
                        if (survivors.Except(payload.Actors).Any() || extra.Any(id => next.Kind != MissionKind.Snatch || id != next.TargetId)
                            || payload.SourceTile != next.DestinationTile || payload.CampaignId != next.CampaignId || payload.WorldId != next.WorldId)
                            throw new InvalidDataException("The return payload differs from its actual surviving party and captive.");
                        next.Cargo = payload.Cargo.Concat(extra).Distinct().ToArray(); _store.WriteTransfer(next, File.ReadAllBytes(payloadPath));
                    }
                    else { next.TransferName = null; next.TransferSha256 = null; next.Cargo = Array.Empty<string>(); }
                }
            }
            _records[next.Id] = next; _running = next; Publish(snapshot);
            _lastCheckpoint = result.SaveName;
            _work.ConsumedTicks = result.ConsumedTicks; _work.DeadActors = result.DeadActors;
            if (terminal) { _work = null; _running = null; }
            yield break;
        }
        private void ObserveForeground(MissionRecord record)
        {
            if (record.Phase == MissionPhase.Departed) throw new InvalidDataException("The destination must accept its worker handoff before loading.");
            Character[] actors = record.Actors.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).ToArray();
            var dead = new HashSet<string>(record.DeadActors);
            for (int i = 0; i < actors.Length; i++)
            { if (actors[i] != null && actors[i].isDead) dead.Add(record.Actors[i]); else if (actors[i] == null && !dead.Contains(record.Actors[i])) throw new InvalidDataException("A native expedition actor disappeared without a death record."); }
            record.DeadActors = dead.ToArray(); Character[] survivors = actors.Where(actor => actor != null && !actor.isDead).ToArray();
            Party party = record.PartyId == null ? null : DatabaseManager.Instance.partyDatabase.GetPartyByPersistentIDSafe(record.PartyId);
            if (!record.WorkerReturning && (survivors.Length == 0 || party == null || party.currentQuest == null || party.currentQuest.persistentID != record.QuestId)) record.WorkerReturning = true;
            if (!record.WorkerReturning) return;
            LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
            if (portal == null) throw new InvalidOperationException("Replace the destination Travel Portal to bring this party home.");
            if (_foregroundReturns.Add(record.Id)) foreach (Character actor in survivors)
            {
                actor.partyComponent.currentParty?.RemoveMember(actor); actor.jobQueue.CancelAllJobs();
                PortalTransport.BeginPortalWalk(actor, portal);
            }
            if (survivors.All(actor => actor.gridTileLocation != null && portal.tiles.Contains(actor.gridTileLocation))) StartOperation(LeaveDestination(record, survivors));
        }
        private IEnumerator LeaveDestination(MissionRecord record, Character[] survivors)
        {
            MissionRecord next = MissionStore.Transition(record, MissionPhase.ReturnPreparing);
            LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
            PortalPayload payload = PortalTransport.CaptureReturn(survivors, record.Kind == MissionKind.Snatch ? record.TargetId : null, portal);
            if (payload != null)
            {
                next.Cargo = payload.Cargo.Concat(payload.Actors.Except(survivors.Select(actor => actor.persistentID))).Distinct().ToArray();
                _store.WriteTransfer(next, System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
            }
            else { next.TransferName = null; next.TransferSha256 = null; next.Cargo = Array.Empty<string>(); }
            _records[next.Id] = next; Publish();
            yield return FinishReturnDeparture(next);
        }
        private IEnumerator FinishReturnDeparture(MissionRecord record)
        {
            if (record.TransferName != null)
            {
                PortalPayload payload = ReadPayload(record);
                Character[] present = payload.Memories.Keys.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).ToArray();
                if (present.Any(actor => actor == null) && present.Any(actor => actor != null))
                    throw new InvalidDataException("Return departure has mixed native ownership; retain its payload and source checkpoint.");
                if (present.All(actor => actor != null)) PortalTransport.Depart(payload);
            }
            TileSnapshot snapshot = null; yield return SaveActive(value => snapshot = value);
            MissionRecord next = MissionStore.Transition(record, MissionPhase.Returning); next.DestinationAfter = snapshot;
            _records[next.Id] = next; Publish(snapshot);
        }
        private IEnumerator ReturnHome(MissionRecord record)
        {
            string[] survivors = record.Actors.Except(record.DeadActors).ToArray();
            if (survivors.Length != 0)
            {
                PortalPayload payload = ReadPayload(record);
                Character[] present = payload.Memories.Keys.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).ToArray();
                if (present.Any(actor => actor == null) && present.Any(actor => actor != null)) throw new InvalidDataException("Return has mixed native ownership; retain its payload.");
                if (present.All(actor => actor == null))
                {
                    LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
                    if (portal == null || portal is TravelPortal) throw new InvalidOperationException("Replace the main Portal to receive the expedition.");
                    PortalTransport.Arrive(payload, portal);
                }
                foreach (string id in survivors)
                {
                    Character actor = CharacterManager.Instance.GetCharacterByPersistentID(id);
                    if (actor.minion != null || actor is Summon) PlayerManager.Instance.player.underlingsComponent.AddCharacterToPersistentDefendParty(actor);
                }
            }
            TileSnapshot snapshot = null; yield return SaveActive(value => snapshot = value);
            MissionRecord next = MissionStore.Transition(record, MissionPhase.Completed); next.SourceAfter = snapshot;
            _records[next.Id] = next; Publish(snapshot);
        }
        internal IEnumerator PrepareTravel(int destination)
        {
            if (Busy) throw new InvalidOperationException("Wait for the expedition transfer to finish.");
            Bind();
            if (_worker == null)
            {
                MissionRecord pending = _records.Values.FirstOrDefault(record => record.DestinationTile == destination
                    && (record.Phase == MissionPhase.Departed || record.Phase == MissionPhase.Fighting));
                if (pending != null) Launch(pending, false);
            }
            if (_worker == null) yield break;
            _control.Handoff = true; _control.Paused = true; _control.AllowedTicks = _running.ForegroundTicks; _worker.Control(_control);
            yield return Wait(() => _worker.Result() != null || _worker.HasExited, 600, "The destination expedition did not finish its handoff.");
            var result = _worker.Result() ?? throw new IOException("The destination expedition stopped without a handoff.");
            yield return Consume(result, true);
        }
        private IEnumerator SaveActive(Action<TileSnapshot> saved)
        {
            bool paused = GameManager.Instance.isPaused; UIManager.Instance.Pause();
            try
            {
                var saver = SaveManager.Instance.saveCurrentProgressManager;
                string name = "TruePlanet-expedition-" + Guid.NewGuid().ToString("N"), path = Path.Combine(Utilities.gameSavePath, name + ".zip");
                TileGame.NativeSaveError = null; saver.DoManualSave(name);
                yield return Wait(() => File.Exists(path) && !saver.isSaving && !saver.isWritingToDisk, 600, "The expedition native save did not finish.");
                if (TileGame.NativeSaveError != null) throw new IOException(TileGame.NativeSaveError);
                TileSnapshot snapshot = _tiles.ImportCompletedNativeSave(_context, path, TileTravel.HasPortal());
                Publish(snapshot); saved(snapshot); File.Delete(path);
            }
            finally { if (!paused && UIManager.Instance != null) UIManager.Instance.Unpause(); }
        }
        private void Publish(TileSnapshot snapshot = null)
        {
            var campaign = JsonConvert.DeserializeObject<CampaignManifest>(JsonConvert.SerializeObject(TileGame.Campaign));
            campaign.Missions = _records.Values.Select(_store.Record).ToArray();
            if (snapshot != null) campaign.Tiles = campaign.Tiles.Where(value => value.TileId != snapshot.TileId).Concat(new[] { snapshot }).ToArray();
            TileGame.CommitCampaign(campaign);
        }
        private void StopWorker()
        { if (_worker != null) { _worker.Dispose(); _worker = null; } _work = null; _running = null; }
        private static IEnumerator Wait(Func<bool> condition, float seconds, string error)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(error); yield return null; }
        }
        private static void VerifyHash(string path, string expected)
        {
            using (var file = File.OpenRead(path)) using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != expected) throw new InvalidDataException("The native expedition output checksum changed.");
        }
    }
}
