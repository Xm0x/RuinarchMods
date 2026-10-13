using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TruePlanet
{
    internal sealed class TileSnapshot
    {
        public int TileId;
        public string SnapshotId, ZipName, Sha256;
        public bool Visited, Held;
    }
    internal sealed class CampaignManifest
    {
        public int FormatVersion = 1;
        public string CampaignId, WorldId, Revision;
        public int ActiveTile;
        public TileSnapshot[] Tiles = new TileSnapshot[0];
        public CampaignCarry Carry;
    }
    internal sealed class TileStore
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        { MissingMemberHandling = MissingMemberHandling.Error, Formatting = Formatting.Indented };
        private readonly Planet _world;
        private readonly string _directory, _worldJson;

        internal TileStore(string root, Planet world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            world.Validate();
            if (world.FormatVersion != 3) throw new InvalidDataException("Campaign snapshots require a geographic tile world.");
            Component(world.Id); _worldJson = JsonConvert.SerializeObject(world, Json);
            _world = JsonConvert.DeserializeObject<Planet>(_worldJson, Json); _world.Validate();
            _directory = Path.Combine(Path.GetFullPath(root), world.Id);
            string path = Path.Combine(_directory, "planet.json");
            Directory.CreateDirectory(_directory);
            if (File.Exists(path))
            {
                if (!JToken.DeepEquals(JToken.Parse(File.ReadAllText(path)), JToken.Parse(_worldJson)))
                    throw new InvalidDataException("The campaign world definition is immutable and differs from planet.json.");
            }
            else WriteNew(path, _worldJson);
        }
        internal CampaignManifest Read(string campaignId, string revision = null)
        {
            GuidId(campaignId);
            string directory = CampaignDirectory(campaignId);
            string path;
            if (revision == null) path = Path.Combine(directory, "manifest.json");
            else { GuidId(revision); path = Path.Combine(directory, "revisions", revision + ".json"); }
            CampaignManifest manifest = Deserialize<CampaignManifest>(File.ReadAllText(path));
            Validate(manifest);
            if (manifest.CampaignId != campaignId || revision != null && manifest.Revision != revision)
                throw new InvalidDataException("Campaign manifest identity differs from its path.");
            return manifest;
        }
        internal TileSnapshot ImportCompletedNativeSave(TileContext context, string zipPath, bool held)
        {
            ValidateContext(context);
            var manager = SaveManager.Instance;
            if (!ReferenceEquals(manager, null) && (manager.doNotContinueSaving || manager.saveCurrentProgressManager.isSaving
                || manager.saveCurrentProgressManager.isWritingToDisk))
                throw new InvalidOperationException("The native save has not completed successfully.");
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(CampaignDirectory(context.CampaignId), "tiles");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, id + ".zip");
            try
            {
                using (var source = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { source.CopyTo(destination); destination.Flush(true); }
                ValidateArchive(path, context, requireRevision: true);
                var snapshot = new TileSnapshot { TileId = context.TileId, SnapshotId = id, ZipName = id + ".zip",
                    Sha256 = Hash(path), Visited = true, Held = held };
                WriteNew(Path.Combine(directory, id + ".json"), JsonConvert.SerializeObject(snapshot, Json));
                return snapshot;
            }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
        }
        internal string Resolve(TileContext context, TileSnapshot snapshot)
        {
            ValidateContext(context); ValidateSnapshot(context.CampaignId, snapshot);
            if (snapshot.TileId != context.TileId) throw new InvalidDataException("The snapshot belongs to another geographic tile.");
            string path = Path.Combine(CampaignDirectory(context.CampaignId), "tiles", snapshot.ZipName);
            if (Hash(path) != snapshot.Sha256) throw new InvalidDataException("Native tile snapshot checksum mismatch.");
            ValidateArchive(path, context);
            return path;
        }
        internal void Commit(CampaignManifest manifest)
        {
            string previous = manifest.Revision;
            try
            {
                RecordRevision(manifest);
                Replace(Path.Combine(CampaignDirectory(manifest.CampaignId), "manifest.json"), JsonConvert.SerializeObject(manifest, Json));
            }
            catch { manifest.Revision = previous; throw; }
        }
        internal void RecordRevision(CampaignManifest manifest)
        {
            Validate(manifest, requireRevision: false);
            string previous = manifest.Revision;
            string revisions = Path.Combine(CampaignDirectory(manifest.CampaignId), "revisions"); Directory.CreateDirectory(revisions);
            manifest.Revision = Guid.NewGuid().ToString("N");
            try { WriteNew(Path.Combine(revisions, manifest.Revision + ".json"), JsonConvert.SerializeObject(manifest, Json)); }
            catch { manifest.Revision = previous; throw; }
        }
        private void Validate(CampaignManifest manifest, bool requireRevision = true)
        {
            if (manifest == null || manifest.FormatVersion != 1 || manifest.WorldId != _world.Id || manifest.Carry == null || manifest.Tiles == null)
                throw new InvalidDataException("Unsupported or incomplete campaign manifest.");
            GuidId(manifest.CampaignId);
            if (requireRevision) GuidId(manifest.Revision);
            Tile(manifest.ActiveTile); manifest.Carry.Validate();
            var tiles = new HashSet<int>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TileSnapshot snapshot in manifest.Tiles)
            {
                ValidateSnapshot(manifest.CampaignId, snapshot);
                if (!tiles.Add(snapshot.TileId) || !ids.Add(snapshot.SnapshotId))
                    throw new InvalidDataException("Campaign contains duplicate tile or snapshot identities.");
            }
        }
        private void ValidateSnapshot(string campaignId, TileSnapshot snapshot)
        {
            if (snapshot == null) throw new InvalidDataException("Missing tile snapshot reference.");
            Tile(snapshot.TileId); GuidId(snapshot.SnapshotId);
            if (snapshot.ZipName != snapshot.SnapshotId + ".zip" || !Hex(snapshot.Sha256, 64) || !snapshot.Visited)
                throw new InvalidDataException("Invalid immutable tile snapshot reference.");
            string directory = Path.Combine(CampaignDirectory(campaignId), "tiles");
            TileSnapshot stored = Deserialize<TileSnapshot>(File.ReadAllText(Path.Combine(directory, snapshot.SnapshotId + ".json")));
            if (stored == null || stored.TileId != snapshot.TileId || stored.SnapshotId != snapshot.SnapshotId
                || stored.ZipName != snapshot.ZipName || stored.Sha256 != snapshot.Sha256)
                throw new InvalidDataException("Snapshot reference differs from its immutable metadata.");
            if (!File.Exists(Path.Combine(directory, snapshot.ZipName))) throw new FileNotFoundException("Committed native tile snapshot is missing.");
        }
        private void ValidateContext(TileContext context)
        {
            if (context == null || context.Planet == null || context.LocalMapVersion != 1
                || JsonConvert.SerializeObject(context.Planet, Json) != _worldJson)
                throw new InvalidDataException("Native tile context differs from the immutable campaign world.");
            GuidId(context.CampaignId); Tile(context.TileId);
        }
        private void ValidateArchive(string path, TileContext context, bool requireRevision = false)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                if (archive.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() != 1)
                    || archive.GetEntry("ModData/trueplanet.province.json") != null)
                    throw new InvalidDataException("Native snapshot contains conflicting or duplicate entries.");
                ZipArchiveEntry entry = archive.GetEntry("ModData/trueplanet.tile.json");
                if (entry == null || archive.GetEntry("gameDB.db") == null || archive.GetEntry("mainSave.sav") == null)
                    throw new InvalidDataException("Native tile snapshot is incomplete.");
                TileContext saved;
                using (var reader = new StreamReader(entry.Open())) saved = Deserialize<TileContext>(reader.ReadToEnd());
                ValidateContext(saved);
                if (saved.TileId != context.TileId || saved.CampaignId != context.CampaignId)
                    throw new InvalidDataException("Native snapshot has the wrong campaign or tile identity.");
                if (requireRevision && saved.CampaignRevision != context.CampaignRevision)
                    throw new InvalidDataException("Native snapshot has stale campaign revision metadata.");
                PlanetOptions.NativeDimensions(_world.Options.LocalSize, out int columns, out int rows);
                int count = columns * rows * 196;
                if (saved.GroundTypes == null || saved.GroundTypes.Length != count
                    || saved.GroundTypes.Any(v => v > (byte)Inner_Maps.LocationGridTile.Ground_Type.Water_Shore)
                    || saved.DefaultGroundAssets != null && saved.DefaultGroundAssets.Any(p => (uint)p.Key >= count
                        || p.Value != null && string.IsNullOrWhiteSpace(p.Value)))
                    throw new InvalidDataException("Native snapshot has invalid local dimensions or ground metadata.");
                try
                {
                    using (var stream = archive.GetEntry("mainSave.sav").Open())
                    using (var text = new StreamReader(stream))
                    using (var reader = new JsonTextReader(text))
                    {
                        bool settings = false, player = false, worldMap = false, objects = false, ended = false;
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonToken.EndObject && reader.Depth == 0) ended = true;
                            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1) continue;
                            string field = (string)reader.Value;
                            if (!reader.Read()) throw new InvalidDataException("Native payload ends before its field value.");
                            if (field == "worldSettingsData")
                            {
                                JObject data = JObject.Load(reader);
                                string expected = _world.Options.LocalSize == LocalMapSize.Huge ? "Extra_Large" : _world.Options.LocalSize.ToString().Replace("ExtraLarge", "Extra_Large");
                                if ((string)data["mapSettings"]?["mapSize"] != expected)
                                    throw new InvalidDataException("Native map-size settings differ from the campaign dimensions.");
                                settings = true;
                            }
                            else
                            {
                                if (field == "playerSave") player = reader.TokenType == JsonToken.StartObject;
                                if (field == "worldMapSave") worldMap = reader.TokenType == JsonToken.StartObject;
                                if (field == "objectHub") objects = reader.TokenType == JsonToken.StartObject;
                                reader.Skip();
                            }
                        }
                        if (!ended || !settings || !player || !worldMap || !objects)
                            throw new InvalidDataException("Native payload is incomplete.");
                    }
                }
                catch (JsonException error) { throw new InvalidDataException("Native payload is invalid JSON.", error); }
            }
        }
        private void Tile(int tile)
        {
            if ((uint)tile >= _world.Mesh.Centers.Length || !TileLand.For(_world).Cells[tile].Eligible)
                throw new InvalidDataException("Campaign references an invalid or ocean tile.");
        }
        private string CampaignDirectory(string campaignId) { GuidId(campaignId); return Path.Combine(_directory, "campaigns", campaignId); }
        private static T Deserialize<T>(string json)
        {
            try { return JsonConvert.DeserializeObject<T>(json, Json); }
            catch (JsonException error) { throw new InvalidDataException("Invalid campaign snapshot JSON.", error); }
        }
        private static bool Hex(string value, int count) => value != null && value.Length == count
            && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        private static void GuidId(string value) { if (!Hex(value, 32)) throw new InvalidDataException("Invalid campaign, revision or snapshot ID."); }
        private static void Component(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 180 || value.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'z'
                || c >= 'A' && c <= 'Z' || c == '-' || c == '_')))
                throw new InvalidDataException("Invalid immutable world path component.");
        }
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void WriteNew(string path, string json)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(json); writer.Flush(); stream.Flush(true); }
        }
        private static void Replace(string path, string json)
        {
            string temp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                { writer.Write(json); writer.Flush(); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
