using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Newtonsoft.Json;
using Ruinarch.Modding;
using UnityEngine;
using UtilityScripts;

namespace TruePlanet
{
    internal sealed class NativeWork
    {
        public string Root, MissionId, RunId, CampaignId, WorldId, InputSave, Payload, TargetId;
        public int TileId;
        public int ParentId;
        public long ParentStartedTicks;
        public DEMON_RAID_TYPE RaidType;
        public MissionKind Kind;
        public string[] Actors;
        public string[] DeadActors = Array.Empty<string>();
        public string PartyId, QuestId;
        public long ConsumedTicks;
        public bool Returning, Deliver;
        public CampaignCarry Carry;
    }
    internal sealed class WorkerControl
    {
        public long AllowedTicks;
        public long CheckpointRequest;
        public bool Paused = true, Handoff;
        public PROGRESSION_SPEED Speed;
    }
    internal sealed class NativeWorkResult
    {
        public string MissionId, RunId, SaveName, SaveSha256, PayloadName, PayloadSha256, PartyId, QuestId, Error, NativeEndReason;
        public bool Returned, Returning, Handoff, Delivered, PortalHeld;
        public string[] DeadActors;
        public long ConsumedTicks, ManagedBytes;
        public long CheckpointRequest;
        internal void Validate(NativeWork request, long allowedTicks)
        {
            if (request == null || MissionId != request.MissionId || RunId != request.RunId
                || ConsumedTicks < request.ConsumedTicks || ConsumedTicks > allowedTicks || CheckpointRequest < 0
                || DeadActors == null || DeadActors.Distinct().Count() != DeadActors.Length
                || DeadActors.Any(id => !request.Actors.Contains(id)) || request.DeadActors.Any(id => !DeadActors.Contains(id))
                || Error != null || !OutputName(SaveName, "TruePlanet-expedition-", ".zip") || !Hex(SaveSha256, 64))
                throw new InvalidDataException("The expedition result does not match its owned mission, clock or native save.");
            if (Delivered != request.Deliver || Delivered && (Returned || Handoff || PayloadName != null || ConsumedTicks != request.ConsumedTicks))
                throw new InvalidDataException("The expedition delivery result does not match its native return operation.");
            if (Returned && (!Returning || Handoff) || PayloadName != null && !Returned
                || (PayloadName == null) != (PayloadSha256 == null)
                || PayloadName != null && (!OutputName(PayloadName, "return-", ".json") || !Hex(PayloadSha256, 64))
                || Returned && ((DeadActors.Length < request.Actors.Length) != (PayloadName != null)))
                throw new InvalidDataException("The expedition return result has inconsistent actor or cargo ownership.");
        }
        private static bool OutputName(string value, string prefix, string suffix) => value != null
            && value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(suffix, StringComparison.Ordinal)
            && value.Length == prefix.Length + 32 + suffix.Length && Hex(value.Substring(prefix.Length, 32), 32);
        private static bool Hex(string value, int length) => value != null && value.Length == length
            && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
    }
    internal sealed class NativeWorkerProcess : IDisposable
    {
        internal readonly string Root;
        private readonly Process _process;
        internal NativeWorkerProcess(string root, Process process) { Root = root; _process = process; }
        internal bool HasExited => _process.HasExited;
        internal void Control(WorkerControl control) => NativeWorker.Replace(Path.Combine(Root, "control.json"), JsonConvert.SerializeObject(control));
        internal NativeWorkResult Result()
        {
            string path = Path.Combine(Root, "result.json");
            return File.Exists(path) ? NativeWorker.Read<NativeWorkResult>(path) : null;
        }
        internal NativeWorkResult Checkpoint()
        {
            string path = Path.Combine(Root, "checkpoint.json");
            return File.Exists(path) ? NativeWorker.Read<NativeWorkResult>(path) : null;
        }
        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                if (!_process.WaitForExit(15000)) throw new IOException("The native expedition did not stop; its map lease is retained.");
            }
            _process.Dispose(); NativeWorker.Released(this);
        }
    }
    internal static class NativeWorker
    {
        internal const string Argument = "--trueplanet-worker";
        internal static bool IsWorker { get; private set; }
        internal static NativeWork Request { get; private set; }
        private static string _profile;
        private static NativeWorkerProcess _running;
        private static string _data;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(string link, string target, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDirectory(string path);

        internal static void Install(ModContext context, Harmony harmony)
        {
            string[] args = Environment.GetCommandLineArgs(); int flag = Array.IndexOf(args, Argument);
            if (flag < 0) return;
            try { InstallWorker(context, harmony, args, flag); }
            catch (Exception error)
            {
                context.Logger.Error("Native worker bootstrap failed before native initialization: " + error);
                Environment.Exit(1);
            }
        }
        private static void InstallWorker(ModContext context, Harmony harmony, string[] args, int flag)
        {
            if (flag + 1 >= args.Length) throw new InvalidDataException("Missing native worker request.");
            string root = Path.GetDirectoryName(Path.GetFullPath(args[flag + 1]));
            string workers = Path.GetFullPath(Path.Combine(Utilities.gameSavePath, "TruePlanet", "workers"));
            if (!string.Equals(Path.GetDirectoryName(root), workers, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(root), "N", out _)
                || !string.Equals(Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName), "TPWorker.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native worker execution is outside an owned private profile.");
            if (!string.Equals(Path.GetFullPath(context.ModsRoot), Path.Combine(root, "Mods"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native worker mod root is not private.");
            Request = JsonConvert.DeserializeObject<NativeWork>(File.ReadAllText(args[flag + 1]));
            if (Request == null || !string.Equals(Path.GetFullPath(Request.Root), root, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Request.MissionId, "N", out _) || !Guid.TryParseExact(Request.CampaignId, "N", out _)
                || !Guid.TryParseExact(Request.RunId, "N", out _) || Request.TileId < 0 || Request.ConsumedTicks < 0
                || Request.Actors == null || Request.Actors.Length == 0 || Request.Actors.Distinct().Count() != Request.Actors.Length
                || Request.Actors.Any(id => !Guid.TryParse(id, out _)) || Request.DeadActors == null || Request.DeadActors.Any(id => !Request.Actors.Contains(id)))
                throw new InvalidDataException("Invalid native worker identity.");
            Private(root, Request.InputSave); if (Request.Payload != null) Private(root, Request.Payload);
            IsWorker = true; _data = Path.Combine(root, "Data"); Directory.CreateDirectory(_data);
            foreach (string property in new[] { "gameSavePath", "tempPath", "tempZipPath", "autosavePath" })
                harmony.Patch(AccessTools.PropertyGetter(typeof(Utilities), property), prefix: new HarmonyMethod(typeof(NativeWorker), nameof(SaveDirectory)));
            foreach (MethodBase method in new MethodBase[] { AccessTools.Method(typeof(Settings.SettingsManager), "Awake"),
                AccessTools.Method(typeof(AchievementManager), "Initialize"), AccessTools.Method(typeof(ExternalFileManager), "ConstructInstalledModsData") })
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NativeWorker), nameof(PersistentPaths)));
            foreach (MethodInfo method in typeof(Steamworks.SteamUserStats).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.ReturnType == typeof(bool) && new[] { "SetAchievement", "ClearAchievement", "SetStat", "ResetAllStats", "StoreStats" }.Contains(method.Name))
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorker), nameof(NoExternalStats)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), "Update"), prefix: new HarmonyMethod(typeof(NativeMissionSimulation), nameof(NativeMissionSimulation.BeforeNativeFrame)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), "TickEnded"), postfix: new HarmonyMethod(typeof(NativeMissionSimulation), nameof(NativeMissionSimulation.AfterNativeTick)));
            harmony.Patch(AccessTools.Method(typeof(PartyQuest), nameof(PartyQuest.EndQuest)), postfix: new HarmonyMethod(typeof(NativeMissionSimulation), nameof(NativeMissionSimulation.QuestEnded)));
            var host = new GameObject("TruePlanet mission worker"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<NativeMissionSimulation>();
        }
        internal static Process OpenParent(NativeWork work)
        {
            Process parent;
            try { parent = Process.GetProcessById(work.ParentId); }
            catch (ArgumentException) { return null; }
            try
            {
                if (!parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == work.ParentStartedTicks) return parent;
            }
            catch (InvalidOperationException) { }
            parent.Dispose(); return null;
        }
        internal static NativeWorkerProcess Start(NativeWork work, string snapshot, string payload, WorkerControl control)
        {
            if (IsWorker) throw new InvalidOperationException("A native mission worker cannot launch another worker.");
            if (_running != null) throw new InvalidOperationException("Another offscreen expedition is still running.");
            if (work == null || !Guid.TryParseExact(work.MissionId, "N", out _) || !Guid.TryParseExact(work.CampaignId, "N", out _))
                throw new InvalidDataException("Invalid expedition identity.");
            if (_profile == null) _profile = PrepareProfile();
            string root = _profile; work.Root = root;
            string saves = Path.Combine(root, "Data", "Ruinarch Game Saves"); Directory.CreateDirectory(saves);
            foreach (string directory in new[] { "Temp", "Autosaves", "TruePlanet" })
            {
                string path = Path.Combine(saves, directory); if (Directory.Exists(path)) RemovePrivateTree(path);
            }
            CopyCampaign(work, saves);
            work.RunId = Guid.NewGuid().ToString("N");
            using (Process parent = Process.GetCurrentProcess())
            { work.ParentId = parent.Id; work.ParentStartedTicks = parent.StartTime.ToUniversalTime().Ticks; }
            work.InputSave = Path.Combine(root, "input.zip"); File.Copy(snapshot, work.InputSave, true);
            work.Payload = payload == null ? null : Path.Combine(root, "payload.json");
            if (payload != null) File.Copy(payload, work.Payload, true);
            foreach (string output in new[] { "result.json", "checkpoint.json", "result.json.tmp" })
                if (File.Exists(Path.Combine(root, output))) File.Delete(Path.Combine(root, output));
            Replace(Path.Combine(root, "request.json"), JsonConvert.SerializeObject(work));
            Replace(Path.Combine(root, "control.json"), JsonConvert.SerializeObject(control));
            var info = new ProcessStartInfo(Path.Combine(root, "TPWorker.exe"), "-batchmode -nographics " + Argument + " \"" + Path.Combine(root, "request.json") + "\" -logFile \"" + Path.Combine(root, "Player.log") + "\"")
                { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
            Process process = Process.Start(info) ?? throw new IOException("The native expedition process did not start.");
            _running = new NativeWorkerProcess(root, process); return _running;
        }
        private static string PrepareProfile()
        {
            string root = Path.Combine(TruePlanetMod.SaveRoot, "workers", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string game = Path.GetDirectoryName(Application.dataPath);
            foreach (string name in new[] { "UnityPlayer.dll", "SQLite.Interop.dll", "System.Data.SQLite.dll", "System.Data.SQLite.EF6.dll", "System.Data.SQLite.Linq.dll" })
                File.Copy(Path.Combine(game, name), Path.Combine(root, name));
            File.Copy(Path.Combine(game, "Ruinarch.exe"), Path.Combine(root, "TPWorker.exe"));
            Link(Path.Combine(root, "TPWorker_Data"), Application.dataPath);
            Link(Path.Combine(root, "MonoBleedingEdge"), Path.Combine(game, "MonoBleedingEdge"));
            File.WriteAllText(Path.Combine(root, "steam_appid.txt"), "909320\n");
            string mods = Path.Combine(root, "Mods"); Directory.CreateDirectory(mods);
            foreach (string name in new[] { "0Harmony.dll", "Mono.Cecil.dll", "Ruinarch.ModContent.dll" }) File.Copy(Path.Combine(ModLoader.ModsRoot, name), Path.Combine(mods, name));
            foreach (KnownMod mod in ModLoader.Known.Where(mod => mod.Loaded && mod.Origin != ModOrigin.Infrastructure))
                CopyTree(mod.Directory, Path.Combine(mods, mod.Id));
            string settings = Path.Combine(ModLoader.ModsRoot, "settings"); if (Directory.Exists(settings)) CopyTree(settings, Path.Combine(mods, "settings"));
            File.WriteAllText(Path.Combine(mods, "modloader.config.json"), "{\"disabled\":[]}\n");
            return root;
        }
        private static void CopyCampaign(NativeWork work, string saves)
        {
            if (string.IsNullOrEmpty(work.WorldId) || work.WorldId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || work.WorldId == "." || work.WorldId == "..")
                throw new InvalidDataException("Invalid expedition world path.");
            string sourceWorld = Path.Combine(TruePlanetMod.SaveRoot, work.WorldId), targetWorld = Path.Combine(saves, "TruePlanet", work.WorldId);
            Directory.CreateDirectory(targetWorld); File.Copy(Path.Combine(sourceWorld, "planet.json"), Path.Combine(targetWorld, "planet.json"));
            string source = Path.Combine(sourceWorld, "campaigns", work.CampaignId), target = Path.Combine(targetWorld, "campaigns", work.CampaignId);
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source, "*.json")) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            CopyTree(Path.Combine(source, "revisions"), Path.Combine(target, "revisions"));
            Link(Path.Combine(target, "tiles"), Path.Combine(source, "tiles"));
            if (Directory.Exists(Path.Combine(source, "missions"))) Link(Path.Combine(target, "missions"), Path.Combine(source, "missions"));
        }
        private static void Link(string path, string target)
        {
            if (!CreateSymbolicLink(path, Path.GetFullPath(target), 3)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Creating private native worker data link");
        }
        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
        private static void RemovePrivateTree(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            { if (!RemoveDirectory(directory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); return; }
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            foreach (string child in Directory.GetDirectories(directory)) RemovePrivateTree(child);
            Directory.Delete(directory);
        }
        internal static void Released(NativeWorkerProcess process) { if (ReferenceEquals(_running, process)) _running = null; }
        internal static void Stop() { _running?.Dispose(); }
        internal static void Replace(string path, string data)
        {
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new System.Text.UTF8Encoding(false))) { writer.Write(data); writer.Flush(); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal static T Read<T>(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(file))
            using (var json = new JsonTextReader(reader)) return JsonSerializer.CreateDefault().Deserialize<T>(json);
        }
        private static void Private(string root, string path)
        {
            if (path == null || !Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A worker input is outside its private profile.");
        }
        private static bool NoExternalStats(ref bool __result) { __result = true; return false; }
        private static string PersistentData() => _data;
        private static bool SaveDirectory(MethodBase __originalMethod, ref string __result)
        {
            string saves = Path.Combine(_data, "Ruinarch Game Saves");
            switch (__originalMethod.Name)
            {
                case "get_gameSavePath": __result = saves; break;
                case "get_tempPath": __result = Path.Combine(saves, "Temp"); break;
                case "get_tempZipPath": __result = Path.Combine(saves, "Temp", "Temp"); break;
                case "get_autosavePath": __result = Path.Combine(saves, "Autosaves"); break;
                default: throw new InvalidOperationException(__originalMethod.Name);
            }
            __result += Path.DirectorySeparatorChar; return false;
        }
        private static IEnumerable<CodeInstruction> PersistentPaths(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.PropertyGetter(typeof(Application), "persistentDataPath"), replacement = AccessTools.Method(typeof(NativeWorker), nameof(PersistentData));
            foreach (CodeInstruction instruction in instructions)
                if (instruction.Calls(original)) { var changed = new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = replacement }; yield return changed; }
                else yield return instruction;
        }
    }
}
