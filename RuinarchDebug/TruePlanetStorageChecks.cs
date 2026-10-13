using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private void TruePlanetStorageChecks(Type planetType, string nativeSave)
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assembly assembly = planetType.Assembly;
            Type contextType = assembly.GetType("TruePlanet.TileContext"), storeType = assembly.GetType("TruePlanet.TileStore");
            Type manifestType = assembly.GetType("TruePlanet.CampaignManifest");
            object context;
            using (var archive = ZipFile.OpenRead(nativeSave))
            using (var reader = new StreamReader(archive.GetEntry("ModData/trueplanet.tile.json").Open()))
                context = JsonConvert.DeserializeObject(reader.ReadToEnd(), contextType);
            object world = contextType.GetField("Planet").GetValue(context);
            string root = Path.Combine(Path.GetTempPath(), "ruinarch-campaign-regression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                object store = Activator.CreateInstance(storeType, instance, null, new[] { (object)root, world }, null);
                MethodInfo import = storeType.GetMethod("ImportCompletedNativeSave", instance), commit = storeType.GetMethod("Commit", instance);
                MethodInfo resolve = storeType.GetMethod("Resolve", instance), read = storeType.GetMethod("Read", instance);
                object first = import.Invoke(store, new[] { context, nativeSave, (object)true });
                string campaign = (string)contextType.GetField("CampaignId").GetValue(context);
                string worldId = (string)planetType.GetField("Id").GetValue(world);
                var data = new JObject { ["CampaignId"] = campaign, ["WorldId"] = worldId,
                    ["ActiveTile"] = JToken.FromObject(contextType.GetField("TileId").GetValue(context)),
                    ["Tiles"] = new JArray(JObject.FromObject(first)), ["Carry"] = JObject.Parse("{\"Skills\":[]}") };
                object manifest = JsonConvert.DeserializeObject(data.ToString(), manifestType);
                commit.Invoke(store, new[] { manifest });
                string revision = (string)manifestType.GetField("Revision").GetValue(manifest);
                string originalPath = (string)resolve.Invoke(store, new[] { context, first });
                byte[] original = File.ReadAllBytes(originalPath);
                string changed = Path.Combine(root, "progressed.zip"); File.Copy(nativeSave, changed);
                using (var archive = ZipFile.Open(changed, ZipArchiveMode.Update))
                {
                    JObject native;
                    using (var reader = new StreamReader(archive.GetEntry("mainSave.sav").Open())) native = JObject.Parse(reader.ReadToEnd());
                    native["tick"] = (long)native["tick"] + 30;
                    archive.GetEntry("mainSave.sav").Delete();
                    using (var writer = new StreamWriter(archive.CreateEntry("mainSave.sav").Open())) writer.Write(native.ToString(Formatting.None));
                }
                object next = import.Invoke(store, new[] { context, changed, (object)true });
                data["Tiles"] = new JArray(JObject.FromObject(next));
                object updated = JsonConvert.DeserializeObject(data.ToString(), manifestType);
                commit.Invoke(store, new[] { updated });
                var old = JObject.FromObject(read.Invoke(store, new object[] { campaign, revision }));
                Check("old campaign revision retains its original native tile state", () =>
                    (old["Tiles"][0]["SnapshotId"].ToString() == JObject.FromObject(first)["SnapshotId"].ToString()
                        && File.ReadAllBytes((string)resolve.Invoke(store, new[] { context, first })).SequenceEqual(original)
                        && !File.ReadAllBytes((string)resolve.Invoke(store, new[] { context, next })).SequenceEqual(original), "immutable revision and distinct native tick"));
                string pointer = Path.Combine(root, worldId, "campaigns", campaign, "manifest.json");
                byte[] committed = File.ReadAllBytes(pointer);
                Directory.CreateDirectory(pointer + ".tmp");
                bool failed = false;
                try { commit.Invoke(store, new[] { manifest }); }
                catch (TargetInvocationException error) when (error.InnerException is IOException || error.InnerException is UnauthorizedAccessException) { failed = true; }
                finally { Directory.Delete(pointer + ".tmp"); }
                var current = JObject.FromObject(read.Invoke(store, new object[] { campaign, null }));
                Check("failed campaign commit preserves the last usable native snapshot reference", () =>
                    (failed && File.ReadAllBytes(pointer).SequenceEqual(committed)
                        && current["Tiles"][0]["SnapshotId"].ToString() == JObject.FromObject(next)["SnapshotId"].ToString()
                        && File.ReadAllBytes((string)resolve.Invoke(store, new[] { context, next })).SequenceEqual(File.ReadAllBytes(changed)), "failed temporary-file write"));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
