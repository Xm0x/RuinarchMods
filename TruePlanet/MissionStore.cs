using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace TruePlanet
{
    internal enum MissionKind { Raid, Snatch, Summon }
    internal enum MissionPhase { Preparing, Departed, Fighting, Returning, Completed, Failed, ReturnPreparing }
    internal sealed class MissionReference
    {
        public string Id, Revision;
    }
    internal sealed class MissionRecord
    {
        public int FormatVersion = 1;
        public string Id, Revision, CampaignId, WorldId;
        public int SourceTile, DestinationTile;
        public MissionKind Kind;
        public MissionPhase Phase;
        public string[] Actors = Array.Empty<string>();
        public string[] Cargo = Array.Empty<string>();
        public string[] DeadActors = Array.Empty<string>();
        public string TargetId, SourceStructureId;
        public DEMON_RAID_TYPE RaidType;
        public TileSnapshot SourceBefore, SourceAfter, DestinationBefore, DestinationAfter;
        public string TransferName, TransferSha256, Failure;
        public long ForegroundTicks;
        public string PartyId, QuestId;
        public long WorkerTick;
        public bool WorkerReturning;
        public string NativeEndReason;
        public int ManaCost;
        public bool ManaPaid;
        public Dictionary<string, MINION_TYPE> Demons = new Dictionary<string, MINION_TYPE>();
        public Dictionary<string, SUMMON_TYPE> Monsters = new Dictionary<string, SUMMON_TYPE>();
    }

    // Canonical ownership lives in a campaign revision. Workers only write private candidate results.
    internal sealed class MissionStore
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
            { MissingMemberHandling = MissingMemberHandling.Error, Formatting = Formatting.Indented };
        private readonly string _directory, _campaign, _world;
        internal MissionStore(string campaignDirectory, string campaignId, string worldId)
        {
            Id(campaignId); _campaign = campaignId; _world = worldId;
            _directory = Path.Combine(campaignDirectory, "missions");
            if (string.IsNullOrEmpty(worldId)) throw new InvalidDataException("Missing mission world identity.");
        }
        internal MissionRecord Read(MissionReference reference)
        {
            if (reference == null) throw new InvalidDataException("Missing mission reference.");
            Id(reference.Id); Id(reference.Revision);
            var record = JsonConvert.DeserializeObject<MissionRecord>(File.ReadAllText(Path.Combine(_directory,
                reference.Id, reference.Revision + ".json")), Json);
            Validate(record);
            if (record.Id != reference.Id || record.Revision != reference.Revision) throw new InvalidDataException("Mission revision identity mismatch.");
            return record;
        }
        internal MissionReference Record(MissionRecord record)
        {
            Validate(record, false);
            string revision = Guid.NewGuid().ToString("N"), directory = Path.Combine(_directory, record.Id);
            Directory.CreateDirectory(directory);
            string previous = record.Revision; record.Revision = revision;
            try { WriteNew(Path.Combine(directory, revision + ".json"), JsonConvert.SerializeObject(record, Json)); }
            catch { record.Revision = previous; throw; }
            return new MissionReference { Id = record.Id, Revision = revision };
        }
        internal MissionRecord Create(MissionKind kind, int source, int destination, TileSnapshot sourceSnapshot,
            TileSnapshot destinationSnapshot, string[] actors, string target, string sourceStructure, DEMON_RAID_TYPE raidType)
        {
            var record = new MissionRecord
            {
                Id = Guid.NewGuid().ToString("N"), CampaignId = _campaign, WorldId = _world,
                SourceTile = source, DestinationTile = destination, Kind = kind, Phase = MissionPhase.Preparing,
                SourceBefore = sourceSnapshot, DestinationBefore = destinationSnapshot,
                Actors = (string[])actors.Clone(), TargetId = target, SourceStructureId = sourceStructure, RaidType = raidType
            };
            Validate(record, false); return record;
        }
        internal void ValidateOwnership(IEnumerable<MissionReference> references)
        {
            var actors = new HashSet<string>(StringComparer.Ordinal); var maps = new HashSet<int>(); var missions = new HashSet<string>();
            foreach (MissionReference reference in references)
            {
                MissionRecord mission = Read(reference);
                if (!missions.Add(mission.Id)) throw new InvalidDataException("A campaign references the same mission twice.");
                if (mission.Phase == MissionPhase.Completed || mission.Phase == MissionPhase.Failed) continue;
                if (!maps.Add(mission.DestinationTile)) throw new InvalidDataException("A destination already has an active simulator lease.");
                foreach (string actor in mission.Actors.Concat(mission.Cargo))
                    if (!actors.Add(actor)) throw new InvalidDataException("An actor or captive belongs to more than one active mission.");
            }
        }
        internal string WriteTransfer(MissionRecord record, byte[] data)
        {
            Validate(record, false);
            if (record.Phase != MissionPhase.Preparing && record.Phase != MissionPhase.ReturnPreparing && record.Phase != MissionPhase.Returning)
                throw new InvalidOperationException("Transfers can only be prepared at departure or return.");
            string directory = Path.Combine(_directory, record.Id); Directory.CreateDirectory(directory);
            string name = Guid.NewGuid().ToString("N") + ".json";
            using (var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(data, 0, data.Length); stream.Flush(true); }
            using (var sha = SHA256.Create()) record.TransferSha256 = Hex(sha.ComputeHash(data));
            record.TransferName = name;
            return Path.Combine(directory, name);
        }
        internal string ResolveTransfer(MissionRecord record)
        {
            Validate(record);
            if (record.TransferName == null) throw new InvalidDataException("Mission transfer payload is missing.");
            string path = Path.Combine(_directory, record.Id, record.TransferName);
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                if (Hex(sha.ComputeHash(stream)) != record.TransferSha256) throw new InvalidDataException("Mission transfer checksum mismatch.");
            return path;
        }
        internal static MissionRecord Transition(MissionRecord previous, MissionPhase phase)
        {
            bool allowed = previous.Phase == MissionPhase.Preparing && phase == MissionPhase.Departed
                || previous.Phase == MissionPhase.Departed && phase == MissionPhase.Fighting
                || previous.Phase == MissionPhase.Fighting && phase == MissionPhase.Returning
                || previous.Phase == MissionPhase.Fighting && phase == MissionPhase.ReturnPreparing
                || previous.Phase == MissionPhase.ReturnPreparing && phase == MissionPhase.Returning
                || previous.Phase == MissionPhase.Returning && phase == MissionPhase.Completed
                || phase == MissionPhase.Failed && previous.Phase == MissionPhase.Preparing && previous.SourceAfter == null;
            if (!allowed) throw new InvalidOperationException("Invalid mission ownership transition: " + previous.Phase + " to " + phase);
            var next = JsonConvert.DeserializeObject<MissionRecord>(JsonConvert.SerializeObject(previous, Json), Json);
            next.Phase = phase; next.Revision = null; return next;
        }
        private void Validate(MissionRecord record, bool revision = true)
        {
            if (record == null || record.FormatVersion != 1 || record.CampaignId != _campaign || record.WorldId != _world
                || record.SourceTile < 0 || record.DestinationTile < 0 || record.SourceTile == record.DestinationTile
                || !Enum.IsDefined(typeof(MissionKind), record.Kind) || !Enum.IsDefined(typeof(MissionPhase), record.Phase)
                || record.Actors == null || record.Actors.Length == 0 || record.Cargo == null || record.DeadActors == null
                || record.Demons == null || record.Monsters == null || record.ManaCost < 0)
                throw new InvalidDataException("Invalid native mission record.");
            Id(record.Id); if (revision) Id(record.Revision);
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (string actor in record.Actors.Concat(record.Cargo))
                if (!Guid.TryParse(actor, out _) || !owners.Add(actor)) throw new InvalidDataException("Invalid or duplicate mission actor identity.");
            if (record.DeadActors.Distinct().Count() != record.DeadActors.Length || record.DeadActors.Any(id => !record.Actors.Contains(id)))
                throw new InvalidDataException("Mission casualties must belong to its dispatched party.");
            if (record.Demons.Any(entry => !record.Actors.Contains(entry.Key) || !Enum.IsDefined(typeof(MINION_TYPE), entry.Value))
                || record.Monsters.Any(entry => !record.Actors.Contains(entry.Key) || !Enum.IsDefined(typeof(SUMMON_TYPE), entry.Value))
                || record.Demons.Keys.Intersect(record.Monsters.Keys).Any())
                throw new InvalidDataException("Reserved native underlings must belong to their actual dispatched actors.");
            if (record.Phase != MissionPhase.Preparing && record.Phase != MissionPhase.Failed && record.ManaCost > 0 && !record.ManaPaid)
                throw new InvalidDataException("A departed expedition has not paid its native deployment cost.");
            Snapshot(record.SourceBefore, record.SourceTile); Snapshot(record.DestinationBefore, record.DestinationTile);
            if (record.SourceAfter != null) Snapshot(record.SourceAfter, record.SourceTile);
            if (record.DestinationAfter != null) Snapshot(record.DestinationAfter, record.DestinationTile);
            if (record.Phase != MissionPhase.Preparing && record.Phase != MissionPhase.Failed && record.SourceAfter == null)
                throw new InvalidDataException("A departed mission has no durable source ownership snapshot.");
            if ((record.Phase == MissionPhase.Returning || record.Phase == MissionPhase.Completed) && record.DestinationAfter == null)
                throw new InvalidDataException("A returning mission has no durable destination outcome.");
            if (record.ForegroundTicks < 0 || record.WorkerTick < 0 || record.WorkerTick > record.ForegroundTicks)
                throw new InvalidDataException("Worker progression exceeds the foreground mission clock.");
            if (record.Phase == MissionPhase.Failed && (record.SourceAfter != null || record.DestinationAfter != null))
                throw new InvalidDataException("A departed mission cannot release ownership by failing; recover or return its actors first.");
            if ((record.TransferName == null) != (record.TransferSha256 == null)
                || record.TransferName != null && (record.TransferName.Length != 37 || !record.TransferName.EndsWith(".json", StringComparison.Ordinal)
                    || !IsHex(record.TransferName.Substring(0, 32), 32) || !IsHex(record.TransferSha256, 64)))
                throw new InvalidDataException("Invalid immutable mission transfer reference.");
        }
        private static void Snapshot(TileSnapshot value, int tile)
        {
            if (value == null || value.TileId != tile || !value.Visited || !IsHex(value.SnapshotId, 32)
                || value.ZipName != value.SnapshotId + ".zip" || !IsHex(value.Sha256, 64))
                throw new InvalidDataException("Mission snapshot does not match its geographic owner.");
        }
        private static bool IsHex(string value, int length) => value != null && value.Length == length
            && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        private static void Id(string value) { if (!IsHex(value, 32)) throw new InvalidDataException("Invalid mission revision path."); }
        private static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
        private static void WriteNew(string path, string content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(content); writer.Flush(); stream.Flush(true); }
        }
    }
}
