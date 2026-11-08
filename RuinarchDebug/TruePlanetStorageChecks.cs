using System;
using System.Collections.Generic;
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
        private void PortableCheck(string name, Func<bool> assertion)
        {
            Check(name, () => (assertion(), "portable campaign setup"));
        }

        private static void StorageSet(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, RevisionMembers);
            if (field != null) { field.SetValue(target, value); return; }
            PropertyInfo property = target.GetType().GetProperty(name, RevisionMembers);
            if (property != null) { property.SetValue(target, value, null); return; }
            throw new InvalidOperationException("Missing " + target.GetType().FullName + "." + name);
        }

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
            Type carryType = assembly.GetType("TruePlanet.CampaignCarry", true);
            MethodInfo capture = carryType.GetMethod("Capture", RevisionMembers), apply = carryType.GetMethod("Apply", RevisionMembers);
            object originalCarry = capture.Invoke(null, new object[] { null });
            PortableCheck("campaign setup preserves lord, Portal level and upgrade choices", () =>
            {
                object candidate = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(originalCarry), carryType);
                PLAYER_ARCHETYPE archetype = PlayerSkillManager.Instance.selectedArchetype;
                StorageSet(candidate, "Archetype", archetype == PLAYER_ARCHETYPE.Normal ? PLAYER_ARCHETYPE.Eradication_Puppet_Master : archetype);
                StorageSet(candidate, "PortalLevel", 2);
                Type choiceType = assembly.GetType("TruePlanet.CampaignPortalChoice", true);
                object choice = Activator.CreateInstance(choiceType);
                StorageSet(choice, "Tier", 2); StorageSet(choice, "ItemIndex", 0);
                StorageSet(choice, "ChosenPower", PLAYER_SKILL_TYPE.SNARE_TRAP);
                StorageSet(choice, "PowerChoices", new[] { PLAYER_SKILL_TYPE.SNARE_TRAP, PLAYER_SKILL_TYPE.BALL_LIGHTNING, PLAYER_SKILL_TYPE.HEAT_WAVE });
                Array choices = Array.CreateInstance(choiceType, 1); choices.SetValue(choice, 0);
                StorageSet(candidate, "PortalChoices", choices);
                object restored = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(candidate), carryType);
                return Equals(RevisionMember(restored, "Archetype"), RevisionMember(candidate, "Archetype"))
                    && Equals(RevisionMember(restored, "PortalLevel"), RevisionMember(candidate, "PortalLevel"))
                    && JsonConvert.SerializeObject(RevisionMember(restored, "PortalChoices")) == JsonConvert.SerializeObject(RevisionMember(candidate, "PortalChoices"))
                    && JsonConvert.SerializeObject(RevisionMember(restored, "Skills")) == JsonConvert.SerializeObject(RevisionMember(candidate, "Skills"))
                    && (int)RevisionMember(restored, "Mana") == (int)RevisionMember(candidate, "Mana")
                    && (int)RevisionMember(restored, "SpiritEnergy") == (int)RevisionMember(candidate, "SpiritEnergy")
                    && (int)RevisionMember(restored, "ChaoticEnergy") == (int)RevisionMember(candidate, "ChaoticEnergy");
            });
            PortableCheck("older campaign setup defaults safely without new manifest fields", () =>
            {
                object restored = JsonConvert.DeserializeObject("{\"Skills\":[]}", carryType);
                carryType.GetMethod("Validate", RevisionMembers).Invoke(restored, null);
                return (PLAYER_ARCHETYPE)RevisionMember(restored, "Archetype") == PLAYER_ARCHETYPE.Normal
                    && (int)RevisionMember(restored, "PortalLevel") == 1
                    && ((Array)RevisionMember(restored, "PortalChoices")).Length == 0;
            });
            PortableCheck("invalid portable Portal level is rejected before campaign mutation", () =>
            {
                object before = capture.Invoke(null, new object[] { null }); int mana = PlayerManager.Instance.player.currenciesComponent.mana;
                try
                {
                    object candidate = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(before), carryType);
                    StorageSet(candidate, "Mana", mana + 17); StorageSet(candidate, "PortalLevel", 0);
                    bool rejected = false;
                    try { apply.Invoke(candidate, null); }
                    catch (TargetInvocationException error) { rejected = error.InnerException is InvalidDataException; }
                    return rejected && PlayerManager.Instance.player.currenciesComponent.mana == mana;
                }
                finally { apply.Invoke(before, null); }
            });
            PortableCheck("duplicate portable Portal tier and item are rejected before campaign mutation", () =>
            {
                object before = capture.Invoke(null, new object[] { null }); int mana = PlayerManager.Instance.player.currenciesComponent.mana;
                try
                {
                    object candidate = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(before), carryType);
                    Array source = (Array)RevisionMember(candidate, "PortalChoices"); if (source.Length == 0) return false;
                    Array duplicate = Array.CreateInstance(source.GetType().GetElementType(), 2);
                    duplicate.SetValue(source.GetValue(0), 0); duplicate.SetValue(source.GetValue(0), 1);
                    StorageSet(candidate, "PortalChoices", duplicate); StorageSet(candidate, "Mana", mana + 17);
                    bool rejected = false;
                    try { apply.Invoke(candidate, null); }
                    catch (TargetInvocationException error) { rejected = error.InnerException is InvalidDataException; }
                    return rejected && PlayerManager.Instance.player.currenciesComponent.mana == mana;
                }
                finally { apply.Invoke(before, null); }
            });
            PortableCheck("unavailable portable Portal power is rejected before campaign mutation", () =>
            {
                object before = capture.Invoke(null, new object[] { null }); int mana = PlayerManager.Instance.player.currenciesComponent.mana;
                try
                {
                    object candidate = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(before), carryType);
                    Array source = (Array)RevisionMember(candidate, "PortalChoices");
                    object choice = source.Cast<object>().First();
                    StorageSet(choice, "PowerChoices", new[] { PLAYER_SKILL_TYPE.SNARE_TRAP, PLAYER_SKILL_TYPE.BALL_LIGHTNING, PLAYER_SKILL_TYPE.HEAT_WAVE });
                    var offered = new HashSet<PLAYER_SKILL_TYPE>((PLAYER_SKILL_TYPE[])RevisionMember(choice, "PowerChoices"));
                    PLAYER_SKILL_TYPE unavailable = PlayerSkillManager.Instance.allPlayerSkillsData.Keys.First(power => !offered.Contains(power));
                    StorageSet(choice, "ChosenPower", unavailable); StorageSet(candidate, "Mana", mana + 17);
                    bool rejected = false;
                    try { apply.Invoke(candidate, null); }
                    catch (TargetInvocationException error) { rejected = error.InnerException is InvalidDataException; }
                    return rejected && PlayerManager.Instance.player.currenciesComponent.mana == mana;
                }
                finally { apply.Invoke(before, null); }
            });
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
                var legacyProgress = (int[])manifestType.GetField("SubjugatedTiles").GetValue(manifest);
                Check("older campaign manifests begin with no assumed conquest", () => (legacyProgress.Length == 0, "missing progress is not a victory"));
                // A wilderness save is valid input; conquest belongs to a settled active tile.
                int settledTile = JObject.FromObject(world)["Provinces"].SelectMany(province => province["Villages"])
                    .Select(village => (int)village["TileId"]).First();
                data["ActiveTile"] = settledTile;
                data["SubjugatedTiles"] = new JArray(settledTile);
                object conquest = JsonConvert.DeserializeObject(data.ToString(), manifestType);
                commit.Invoke(store, new[] { conquest });
                string conqueredRevision = (string)manifestType.GetField("Revision").GetValue(conquest);
                data["SubjugatedTiles"] = new JArray();
                commit.Invoke(store, new[] { JsonConvert.DeserializeObject(data.ToString(), manifestType) });
                var conquered = JObject.FromObject(read.Invoke(store, new object[] { campaign, conqueredRevision }));
                var reopened = JObject.FromObject(read.Invoke(store, new object[] { campaign, null }));
                Check("subjugation persists per revision and later opposition does not rewrite history", () =>
                    (conquered["SubjugatedTiles"].Values<int>().SequenceEqual(new[] { settledTile }) && !reopened["SubjugatedTiles"].Any(), "completed and reopened settlement"));
                committed = File.ReadAllBytes(pointer);
                foreach (JToken progress in new JToken[] { JValue.CreateNull(), new JArray(settledTile, settledTile), new JArray(-1) })
                {
                    data["SubjugatedTiles"] = progress;
                    bool rejected = false;
                    try { commit.Invoke(store, new[] { JsonConvert.DeserializeObject(data.ToString(), manifestType) }); }
                    catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { rejected = true; }
                    Check("invalid subjugation leaves the committed campaign intact: " + progress.ToString(Formatting.None), () =>
                        (rejected && File.ReadAllBytes(pointer).SequenceEqual(committed), "null, duplicate or non-target progress"));
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
