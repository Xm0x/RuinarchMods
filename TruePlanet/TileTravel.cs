using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ruinarch;
using UtilityScripts;

namespace TruePlanet
{
    internal enum TravelStage { Idle, Validating, SavingSource, LoadingTarget, RestoringCarry, RecoveringSource }
    [DefaultExecutionOrder(-10000)]
    internal sealed class TileTravel : MonoBehaviour
    {
        internal static TileTravel Instance;
        internal TravelStage Stage { get; private set; }
        internal string LastError { get; private set; }
        internal string RecoveryPath { get; private set; }
        private GameManager _sourceManager;
        private TileContext _source;
        private TileStore _store;
        private CampaignManifest _transaction;
        private CampaignCarry _carry;
        private bool _sourcePaused, _teardown, _loggerWasEnabled;
        private PROGRESSION_SPEED _sourceSpeed;
        private volatile string _nativeError;
        private Button _atlas;
        private int _hud;
        private Player_Input.PickPortalInputModule _replacementModule;

        internal static void Install(Harmony harmony)
        {
            if (Instance != null) return;
            foreach (Type type in typeof(TileTravel).GetNestedTypes(BindingFlags.NonPublic))
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
            var root = new GameObject("TruePlanet campaign travel"); DontDestroyOnLoad(root);
            Instance = root.AddComponent<TileTravel>();
            Application.logMessageReceivedThreaded += Instance.OnLog;
        }
        private void OnDestroy() { Application.logMessageReceivedThreaded -= OnLog; if (Instance == this) Instance = null; }
        private void OnLog(string text, string trace, LogType type)
        { if (Stage != TravelStage.Idle && (type == LogType.Error || type == LogType.Exception)) _nativeError = text; }
        private void Update()
        {
            if (Stage != TravelStage.Idle)
            {
                Debug.unityLogger.logEnabled = true;
                if (GameManager.Instance != null && GameManager.Instance.gameHasStarted && !GameManager.Instance.isPaused)
                    GameManager.Instance.SetPausedState(true);
            }
            if (UIManager.Instance == null || UIManager.Instance.canvas == null || GameManager.Instance == null
                || PlayerUI.Instance == null || !GameManager.Instance.gameHasStarted) return;
            int hud = UIManager.Instance.GetInstanceID();
            if (_hud != hud)
            {
                TMP_Text native = PlayerUI.Instance.GetComponentInChildren<TMP_Text>(true);
                if (native == null) return;
                _hud = hud;
                var button = new GameObject("TruePlanet atlas entry", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(UIManager.Instance.canvas.transform, false);
                var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(18, 100); rect.sizeDelta = new Vector2(144, 36);
                button.GetComponent<Image>().color = new Color32(38, 45, 52, 255);
                _atlas = button.GetComponent<Button>(); _atlas.onClick.AddListener(PlanetView.Open);
                var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(button.transform, false);
                var bounds = text.GetComponent<RectTransform>(); bounds.anchorMin = Vector2.zero; bounds.anchorMax = Vector2.one; bounds.offsetMin = bounds.offsetMax = Vector2.zero;
                var label = text.GetComponent<TextMeshProUGUI>(); label.font = native.font; label.fontSharedMaterial = native.fontSharedMaterial;
                label.text = "Planet atlas"; label.fontSize = 18; label.alignment = TextAlignmentOptions.Center;
            }
            if (_atlas != null)
            {
                _atlas.gameObject.SetActive(TileGame.Active != null);
                _atlas.interactable = Stage == TravelStage.Idle && !SaveManager.Instance.saveCurrentProgressManager.isSaving
                    && !SaveManager.Instance.saveCurrentProgressManager.isWritingToDisk;
            }
            if (Stage == TravelStage.Idle && TileGame.Active != null && !HasPortal() && !UIManager.Instance.IsShowingEndScreen()
                && _replacementModule != PlayerManager.pickPortalInputModule)
            {
                _replacementModule = PlayerManager.pickPortalInputModule;
                PlayerManager.Instance.AddPlayerInputModule(_replacementModule);
                PlayerManager.Instance.ShowStructurePlacementVisual(STRUCTURE_TYPE.THE_PORTAL);
                PlayerUI.Instance.ShowGeneralConfirmation("Replace Portal", "Place a new Portal to reclaim this tile. Its existing world and campaign progression remain.");
            }
        }
        internal void SwitchTo(int tileId)
        {
            if (Stage != TravelStage.Idle) return;
            TileContext source = TileGame.Active;
            if (source == null || GameManager.Instance == null || !GameManager.Instance.gameHasStarted)
                throw new InvalidOperationException("Tile travel requires an active campaign game.");
            if (tileId == source.TileId) return;
            source.Planet.Validate();
            if ((uint)tileId >= source.Planet.Mesh.Centers.Length || !TileLand.For(source.Planet).Cells[tileId].Eligible)
                throw new InvalidOperationException("Select a dry geographic tile to travel.");
            var saver = SaveManager.Instance.saveCurrentProgressManager;
            if (saver.isSaving || saver.isWritingToDisk || !saver.CanSaveCurrentProgress())
                throw new InvalidOperationException("Finish the native save or current setup before travelling.");
            Stage = TravelStage.Validating; LastError = null; RecoveryPath = null; _nativeError = null; _teardown = false;
            try
            {
                _source = source; _store = TileGame.Store;
                _transaction = JsonConvert.DeserializeObject<CampaignManifest>(JsonConvert.SerializeObject(TileGame.Campaign));
                var destination = _transaction.Tiles.FirstOrDefault(t => t.TileId == tileId);
                string path = destination == null ? null : _store.Resolve(new TileContext
                    { Planet = source.Planet, TileId = tileId, CampaignId = source.CampaignId }, destination);
                _carry = CampaignCarry.Capture(); _sourceManager = GameManager.Instance;
                _sourcePaused = _sourceManager.isPaused; _sourceSpeed = _sourceManager.currProgressionSpeed;
                _loggerWasEnabled = Debug.unityLogger.logEnabled; Debug.unityLogger.logEnabled = true;
                UIManager.Instance.Pause();
                StartCoroutine(Drive(Transition(tileId, path)));
            }
            catch { Stage = TravelStage.Idle; throw; }
        }
        private IEnumerator Drive(IEnumerator routine)
        {
            Exception failure = null;
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
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
            if (failure == null) { Finish(); yield break; }
            LastError = Stage + ": " + failure.Message;
            TruePlanetMod.Log.Error("Tile travel failed: " + LastError);
            if (!_teardown) { ResumeSource(); Finish(); ShowError(); yield break; }
            Stage = TravelStage.RecoveringSource; _nativeError = null;
            Exception recoveryError = null;
            IEnumerator recovery = Recover(); stack.Push(recovery);
            while (stack.Count != 0)
            {
                object current = null; bool more = false;
                try { var iterator = stack.Peek(); more = iterator.MoveNext(); if (more) current = iterator.Current; }
                catch (Exception error) { recoveryError = error; }
                if (recoveryError != null) break;
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator child) stack.Push(child); else yield return current;
            }
            while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
            if (recoveryError != null)
            {
                TileGame.Clear(); LastError += "; source recovery failed: " + recoveryError.Message + ". Retained native source: " + RecoveryPath;
                TruePlanetMod.Log.Error(LastError); ShowError(); yield break;
            }
            Finish(); ShowError();
        }
        private IEnumerator Transition(int tile, string path)
        {
            Stage = TravelStage.SavingSource;
            yield return SaveSnapshot(_source.TileId);
            _transaction = JsonConvert.DeserializeObject<CampaignManifest>(JsonConvert.SerializeObject(TileGame.Campaign));
            RecoveryPath = _store.Resolve(_source, _transaction.Tiles.Single(t => t.TileId == _source.TileId));
            Stage = TravelStage.LoadingTarget; _teardown = true;
            var previous = GameManager.Instance;
            if (path == null) TileGame.Play(_source.Planet, tile, _source.CampaignId);
            else { SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(path); UIManager.Instance.optionsMenu.LoadSave(); }
            yield return Ready(previous, tile, path == null);
            Stage = TravelStage.RestoringCarry; _carry.Apply();
            yield return new WaitForEndOfFrame(); yield return null;
            _transaction.ActiveTile = tile; _transaction.Carry = _carry;
            TileGame.CommitCampaign(_transaction);
            yield return SaveSnapshot(tile);
            ResumeSource();
        }
        private IEnumerator SaveSnapshot(int tile)
        {
            var saver = SaveManager.Instance.saveCurrentProgressManager;
            string name = "TruePlanet-transition-" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(Utilities.gameSavePath, name + ".zip");
            TileGame.NativeSaveError = null; saver.DoManualSave(name);
            yield return Wait(() => File.Exists(path) && !saver.isSaving && !saver.isWritingToDisk, 600, "native snapshot save");
            if (TileGame.NativeSaveError != null) throw new IOException("Campaign metadata save failed: " + TileGame.NativeSaveError);
            TileSnapshot snapshot = TileGame.Store.ImportCompletedNativeSave(TileGame.Active, path, HasPortal());
            CampaignManifest current = TileGame.Campaign;
            // Publish new references only after the disk commit succeeds.
            var manifest = new CampaignManifest
            {
                FormatVersion = current.FormatVersion, CampaignId = current.CampaignId, WorldId = current.WorldId,
                Revision = current.Revision, ActiveTile = tile, Carry = _carry,
                Tiles = current.Tiles.Where(t => t.TileId != tile).Concat(new[] { snapshot }).ToArray()
            };
            TileGame.CommitCampaign(manifest);
            File.Delete(path);
        }
        private IEnumerator Ready(GameManager previous, int tile, bool firstVisit)
        {
            yield return Wait(() => GameManager.Instance != null && !ReferenceEquals(GameManager.Instance, previous)
                && TileGame.Active != null && TileGame.Active.TileId == tile && TileGame.Active.CampaignId == _source.CampaignId
                && (firstVisit || PlayerManager.Instance != null && PlayerManager.Instance.player != null)
                && !LevelLoaderManager.Instance.IsLoadingScreenActive(), 1200, "native target generation/load");
            // First visits deliberately wait for the player's native Portal/loadout interaction.
            if (firstVisit)
                while (!GameManager.Instance.gameHasStarted || UIManager.Instance.IsShowingStartScreen())
                { CheckNativeError(); yield return null; }
            else yield return Wait(() => GameManager.Instance.gameHasStarted && !UIManager.Instance.IsShowingStartScreen(), 120, "native player restore");
            UIManager.Instance.Pause(); yield return new WaitForEndOfFrame(); yield return null; CheckNativeError();
        }
        private IEnumerator Recover()
        {
            var previous = GameManager.Instance;
            SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(RecoveryPath);
            UIManager.Instance.optionsMenu.LoadSave();
            yield return Ready(previous, _source.TileId, false);
            _carry.Apply(); _transaction.ActiveTile = _source.TileId; _transaction.Carry = _carry;
            TileGame.CommitCampaign(_transaction); ResumeSource();
        }
        private IEnumerator Wait(Func<bool> condition, float timeout, string step)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition())
            { CheckNativeError(); if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(step); yield return null; }
            CheckNativeError();
        }
        private void CheckNativeError() { if (_nativeError != null) throw new InvalidOperationException("Native error: " + _nativeError); }
        private void ResumeSource()
        {
            if (GameManager.Instance == null || UIManager.Instance == null) return;
            GameManager.Instance.SetProgressionSpeed(_sourceSpeed);
            if (_sourcePaused) UIManager.Instance.Pause(); else UIManager.Instance.Unpause();
        }
        private void Finish() { Stage = TravelStage.Idle; Debug.unityLogger.logEnabled = _loggerWasEnabled; }
        private void ShowError() { if (PlayerUI.Instance != null) PlayerUI.Instance.ShowGeneralConfirmation("Tile travel failed", LastError); }
        internal static bool HasPortal() => PlayerManager.Instance != null && PlayerManager.Instance.player != null
            && PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL) != null;
        private void CompletePortalReplacement(LocationGridTile tile)
        {
            PlayerManager.Instance.player.SetPortalTile(tile.area);
            tile.InstantPlaceDemonicStructure(new StructureSetting(STRUCTURE_TYPE.THE_PORTAL, RESOURCE.NONE));
            PlayerManager.Instance.HideStructurePlacementVisual();
            PlayerManager.Instance.RemovePlayerInputModule(_replacementModule);
            _replacementModule = null;
            InputManager.Instance.SetCursorTo(Cursor_Type.Default);
        }
        [HarmonyPatch(typeof(Player_Input.PickPortalInputModule), "PlacePortal")]
        private static class ReplacePortal
        {
            private static bool Prefix(LocationGridTile p_tile)
            {
                if (TileGame.Active == null || GameManager.Instance == null || !GameManager.Instance.gameHasStarted || HasPortal()) return true;
                Instance.CompletePortalReplacement(p_tile);
                return false;
            }
        }
        [HarmonyPatch(typeof(SkillProgressionManager), "CheckRequirement")]
        private static class UnheldRequirements
        {
            private static bool Prefix(ref int __result)
            {
                if (TileGame.Active == null || PlayerManager.Instance == null || PlayerManager.Instance.player == null || HasPortal()) return true;
                // Unlock requirements depend on the local Portal's level. An unheld
                // tile has no such level until its Portal has been replaced.
                __result = -1; return false;
            }
        }
        private static void CenterLoadedPortal(LocationStructure portal)
        {
            if (portal != null || TileGame.Active == null) { portal.CenterOnStructure(); return; }
            // A destroyed Portal has no object to center on. Keep the saved world
            // and focus its last Portal area while offering native replacement.
            InnerMapCameraMove.Instance.MoveCameraForMinimap(PlayerManager.Instance.player.portalArea.gridTileComponent.centerGridTile.centeredWorldLocation);
        }
        [HarmonyPatch(typeof(SkillTreeSelector), nameof(SkillTreeSelector.LoadLoadout))]
        private static class UnheldLoadout
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                var matches = code.Where(i => i.Calls(AccessTools.Method(typeof(LocationStructure), nameof(LocationStructure.CenterOnStructure)))).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Native loaded Portal centering call shape changed.");
                matches[0].opcode = OpCodes.Call; matches[0].operand = AccessTools.Method(typeof(TileTravel), nameof(CenterLoadedPortal));
                return code;
            }
        }
        private static void PortalLost(PlayerUI ui, string message)
        {
            if (TileGame.Active == null) { ui.LoseGameOver(message); return; }
            CampaignManifest manifest = TileGame.Campaign;
            TileSnapshot current = manifest.Tiles.FirstOrDefault(t => t.TileId == TileGame.Active.TileId);
            if (current != null) current.Held = false;
            manifest.Carry = CampaignCarry.Capture(); TileGame.CommitCampaign(manifest);
            if (!manifest.Tiles.Any(t => t.TileId != manifest.ActiveTile && t.Held)) ui.LoseGameOver(message);
            else ui.ShowGeneralConfirmation("Portal lost", "Other held tiles remain. Open Planet atlas to continue the campaign.");
        }
        [HarmonyPatch(typeof(ThePortal), "DestroyStructure")]
        private static class PortalLoss
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList(); MethodInfo native = AccessTools.Method(typeof(PlayerUI), nameof(PlayerUI.LoseGameOver));
                var matches = code.Where(i => i.Calls(native)).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Native Portal loss call shape changed.");
                matches[0].opcode = OpCodes.Call; matches[0].operand = AccessTools.Method(typeof(TileTravel), nameof(PortalLost));
                return code;
            }
        }
    }
}
