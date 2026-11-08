using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Inner_Maps;
using Newtonsoft.Json;
using UnityEngine;
using UtilityScripts;

namespace TruePlanet
{
    public sealed class NativeMissionSimulation : MonoBehaviour
    {
        private static NativeMissionSimulation _current;
        private WorkerControl _control = new WorkerControl();
        private NativeWork _work;
        private System.Diagnostics.Process _parent;
        private Party _party;
        private PartyQuest _quest;
        private volatile string _nativeError;
        private string _endReason;
        private readonly HashSet<string> _casualties = new HashSet<string>(StringComparer.Ordinal);
        private long _checkpointRequest;
        private long _ticks, _lastCheckpoint;
        private float _nextControlRead;
        private bool _ready, _saving, _returning;
        private void OnEnable() { _current = this; Application.logMessageReceivedThreaded += OnLog; }
        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            _parent?.Dispose(); _parent = null;
            if (_current == this) _current = null;
        }
        private void OnLog(string message, string trace, LogType type) { if (type == LogType.Exception) _nativeError = message + "\n" + trace; }
        internal static void BeforeNativeFrame(GameManager __instance)
        {
            NativeMissionSimulation simulation = _current;
            if (simulation == null) return;
            bool paused = !simulation._ready || simulation._saving || simulation._control.Paused || simulation._control.Handoff || simulation._ticks >= simulation._control.AllowedTicks;
            if (__instance.isPaused != paused) __instance.SetPausedState(paused);
            if (__instance.currProgressionSpeed != simulation._control.Speed) __instance.SetProgressionSpeed(simulation._control.Speed);
        }
        internal static void AfterNativeTick() { if (_current != null && _current._ready && !_current._saving) _current._ticks++; }
        internal static void QuestEnded(PartyQuest __instance, string reason)
        { if (_current != null && ReferenceEquals(__instance, _current._quest)) _current._endReason = reason; }
        private void Update()
        {
            if (_work == null || Time.realtimeSinceStartup < _nextControlRead) return;
            _nextControlRead = Time.realtimeSinceStartup + .1f;
            if (_parent != null && _parent.HasExited) { Environment.Exit(1); return; }
            _control = NativeWorker.Read<WorkerControl>(Path.Combine(_work.Root, "control.json"))
                ?? throw new InvalidDataException("The expedition control record is missing.");
            if (_control.AllowedTicks < _ticks || !Enum.IsDefined(typeof(PROGRESSION_SPEED), _control.Speed))
                throw new InvalidDataException("Expedition clock control moved behind committed native ticks.");
        }
        private IEnumerator Start()
        {
            _work = NativeWorker.Request; _ticks = _work.ConsumedTicks; _lastCheckpoint = _ticks;
            var stack = new Stack<IEnumerator>(); stack.Push(Run()); Exception failure = null;
            foreach (string id in _work.DeadActors) _casualties.Add(id);
            while (stack.Count != 0)
            {
                object current = null; bool more = false;
                try { var iterator = stack.Peek(); more = iterator.MoveNext(); if (more) current = iterator.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator child) stack.Push(child); else yield return current;
            }
            while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
            if (failure != null)
            {
                var result = Result(); result.Error = failure.ToString();
                NativeWorker.Replace(Path.Combine(_work.Root, "result.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            }
            Application.Quit();
        }
        private IEnumerator Wait(Func<bool> condition, float timeout, string description)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (_nativeError != null) throw new InvalidOperationException(_nativeError);
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(description);
                yield return null;
            }
        }
        private IEnumerator Run()
        {
            _parent = NativeWorker.OpenParent(_work) ?? throw new InvalidDataException("The expedition's owning game is no longer running.");
            if (!Application.isBatchMode || SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("The expedition did not enter native headless mode.");
            yield return Wait(() => Managers.SteamworksManager.Instance != null
                && (!Managers.SteamworksManager.Instance.allowSteamworks || SteamManager.Initialized)
                && MainMenuManager.Instance != null && MainMenuUI.Instance != null
                && ExternalFileManager.Instance != null && ExternalFileManager.Instance.hasInitialized && !LevelLoaderManager.Instance.IsLoadingScreenActive(), 180, "Native worker initialization");
            SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(_work.InputSave); MainMenuManager.Instance.StartGame();
            yield return Wait(() => GameManager.Instance != null && GameManager.Instance.gameHasStarted && UIManager.Instance != null
                && !LevelLoaderManager.Instance.IsLoadingScreenActive(), 1200, "Native expedition map load");
            UIManager.Instance.Pause();
            if (TileGame.Active == null || TileGame.Active.CampaignId != _work.CampaignId || TileGame.Active.Planet.Id != _work.WorldId || TileGame.Active.TileId != _work.TileId)
                throw new InvalidDataException("The native worker loaded a different campaign or destination.");
            var portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
            if (portal == null || (!_work.Deliver && !(portal is TravelPortal)) || _work.Deliver && portal is TravelPortal)
                throw new InvalidOperationException("This destination has no matching campaign Portal.");
            if (_work.Carry != null) _work.Carry.Apply();
            if (_work.Deliver)
            {
                if (_work.Payload != null)
                {
                    PortalPayload payload = NativeWorker.Read<PortalPayload>(_work.Payload);
                    Character[] arrivals = PortalTransport.Arrive(payload, portal);
                    string[] expected = _work.Actors.Except(_work.DeadActors).ToArray();
                    string[] roots = arrivals.Select(actor => actor.persistentID).ToArray();
                    if (expected.Except(roots).Any() || roots.Except(expected).Any(id => _work.Kind != MissionKind.Snatch || id != _work.TargetId))
                        throw new InvalidDataException("The native return delivery differs from its surviving actors and captive.");
                    foreach (Character actor in arrivals)
                        if (expected.Contains(actor.persistentID) && (actor.minion != null || actor is Summon))
                            PlayerManager.Instance.player.underlingsComponent.AddCharacterToPersistentDefendParty(actor);
                }
                yield return SaveCheckpoint(false, delivered: true); yield break;
            }
            if (_work.Payload != null)
            {
                PortalPayload payload = JsonConvert.DeserializeObject<PortalPayload>(File.ReadAllText(_work.Payload));
                Character[] arrivals = PortalTransport.Arrive(payload, portal);
                if (!arrivals.Select(actor => actor.persistentID).OrderBy(id => id).SequenceEqual(_work.Actors.OrderBy(id => id)))
                    throw new InvalidDataException("Native expedition arrivals differ from the dispatched actors.");
                var target = GridMap.Instance.mainRegion.settlementsInRegion.FirstOrDefault(settlement => settlement.persistentID == _work.TargetId);
                if (target == null || target.locationType != LOCATION_TYPE.VILLAGE) throw new InvalidDataException("The native raid target no longer exists.");
                _party = PartyManager.Instance.CreateNewParty(arrivals[0], PARTY_QUEST_TYPE.Demon_Raid);
                foreach (Character actor in arrivals.Skip(1)) _party.AddMember(actor);
                var quest = (DemonRaidPartyQuest)PlayerManager.Instance.player.playerFaction.partyQuestBoard.CreateDemonRaidPartyQuest(arrivals[0], PlayerManager.Instance.player.playerSettlement, target);
                if (quest == null) throw new InvalidOperationException("The native raid quest was rejected.");
                quest.SetRaidType(_work.RaidType); quest.SetIsDemonicQuest(true); _quest = quest;
                _party.TryAcceptQuest(quest);
                if (!ReferenceEquals(_party.currentQuest, quest)) throw new InvalidOperationException("The native party could not accept its raid.");
                foreach (Character actor in arrivals) { actor.combatComponent.SetCombatMode(COMBAT_MODE.Defend); _party.AddMemberThatJoinedQuest(actor); }
            }
            else
            {
                _party = _work.PartyId == null ? null : DatabaseManager.Instance.partyDatabase.GetPartyByPersistentIDSafe(_work.PartyId);
                _quest = _party?.currentQuest;
                if (!_work.Returning && (_party == null || _quest == null || _quest.persistentID != _work.QuestId))
                    throw new InvalidDataException("The checkpoint no longer contains its native raid party and quest.");
            }
            _returning = _work.Returning; _ready = true;
            if (_returning) foreach (Character actor in Survivors()) PortalTransport.BeginPortalWalk(actor, portal);
            while (true)
            {
                if (_nativeError != null) throw new InvalidOperationException(_nativeError);
                foreach (string id in _work.Actors)
                {
                    Character actor = CharacterManager.Instance.GetCharacterByPersistentID(id);
                    if (actor != null && actor.isDead) _casualties.Add(id);
                    else if (actor == null && !_casualties.Contains(id)) throw new InvalidDataException("A dispatched actor disappeared without a native death record.");
                }
                if (_control.Handoff)
                { yield return SaveCheckpoint(true); yield break; }
                Character[] survivors = Survivors();
                if (!_returning && (survivors.Length == 0 || _party == null || _party.currentQuest != _quest))
                {
                    _returning = true;
                    foreach (Character actor in survivors)
                    {
                        actor.partyComponent.currentParty?.RemoveMember(actor); actor.jobQueue.CancelAllJobs();
                        PortalTransport.BeginPortalWalk(actor, portal);
                    }
                }
                if (_returning && survivors.All(actor => actor.gridTileLocation != null && portal.tiles.Contains(actor.gridTileLocation)))
                {
                    PortalPayload returned = PortalTransport.CaptureReturn(survivors, _work.Kind == MissionKind.Snatch ? _work.TargetId : null, portal);
                    string payloadName = null;
                    if (returned != null)
                    {
                        payloadName = "return-" + Guid.NewGuid().ToString("N") + ".json";
                        NativeWorker.Replace(Path.Combine(_work.Root, payloadName), JsonConvert.SerializeObject(returned));
                        PortalTransport.Depart(returned);
                    }
                    yield return SaveCheckpoint(false, true, payloadName); yield break;
                }
                if (_ticks - _lastCheckpoint >= 64 || _control.CheckpointRequest > _checkpointRequest)
                { _checkpointRequest = _control.CheckpointRequest; yield return SaveCheckpoint(false); }
                yield return null;
            }
        }
        private Character[] Survivors() => _work.Actors.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).Where(actor => actor != null && !actor.isDead).ToArray();
        private IEnumerator SaveCheckpoint(bool handoff, bool returned = false, string payloadName = null, bool delivered = false)
        {
            _saving = true; UIManager.Instance.Pause();
            string name = "TruePlanet-expedition-" + Guid.NewGuid().ToString("N"), path = Path.Combine(Utilities.gameSavePath, name + ".zip");
            var saver = SaveManager.Instance.saveCurrentProgressManager; TileGame.NativeSaveError = null; saver.DoManualSave(name);
            yield return Wait(() => File.Exists(path) && !saver.isSaving && !saver.isWritingToDisk, 600, "Native expedition checkpoint save");
            if (TileGame.NativeSaveError != null) throw new IOException(TileGame.NativeSaveError);
            var result = Result(); result.SaveName = name + ".zip"; result.SaveSha256 = Hash(path);
            result.Handoff = handoff; result.Returned = returned; result.Delivered = delivered; result.PayloadName = payloadName;
            result.PayloadSha256 = payloadName == null ? null : Hash(Path.Combine(_work.Root, payloadName));
            NativeWorker.Replace(Path.Combine(_work.Root, handoff || returned || delivered ? "result.json" : "checkpoint.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            _lastCheckpoint = _ticks; _saving = false;
        }
        private NativeWorkResult Result() => new NativeWorkResult
        {
            MissionId = _work.MissionId, RunId = _work.RunId, ConsumedTicks = _ticks, ManagedBytes = GC.GetTotalMemory(false), PartyId = _party?.persistentID,
            QuestId = _party?.currentQuest?.persistentID, NativeEndReason = _endReason, Returning = _returning,
            DeadActors = _casualties.ToArray(), CheckpointRequest = _checkpointRequest, PortalHeld = TileTravel.HasPortal()
        };
        private static string Hash(string path)
        { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    }
}
