using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Random = System.Random;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private IEnumerator TruePlanetSuite(string nativeSave = null)
        {
            Type planetType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("TruePlanet.Planet", false)).FirstOrDefault(t => t != null);
            Type landType = planetType?.Assembly.GetType("TruePlanet.TileLand");
            Type nationalType = planetType?.Assembly.GetType("TruePlanet.NationalProvinces");
            if (landType == null || nationalType == null)
            {
                Skip("geographic tiles preserve independent world identity", "the tile-world development code is not loaded");
                yield break;
            }
            TruePlanetPocketChecks(planetType, landType, nationalType);
            TruePlanetGlobeChecks(planetType.Assembly);
            const BindingFlags methods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo landFor = landType.GetMethod("For", methods), build = nationalType.GetMethod("Build", methods);
            MethodInfo validate = planetType.GetMethod("Validate");
            Type geographyType = planetType.Assembly.GetType("TruePlanet.PlanetGeography");
            MethodInfo calibrate = geographyType.GetMethod("CalibrateSeaLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo localPoint = geographyType.GetMethod("LocalTileDirection", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo nativeSeed = geographyType.GetMethod("NativeTileSeed", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (int resolution in new[] { 5, 6 })
            {
                var data = JObject.Parse("{\"FormatVersion\":3,\"TopologyVersion\":1,\"GeographyVersion\":3,\"NationalGenerationVersion\":1,\"LocalMapVersion\":1,\"Id\":\"regression\",\"Name\":\"Regression\",\"Seed\":1028038769,\"Options\":{\"Provinces\":30,\"Nations\":2,\"LandShare\":0.2,\"WildShare\":0.75},\"Provinces\":[]}");
                data["Options"]["Resolution"] = resolution;
                object world = JsonConvert.DeserializeObject(data.ToString(), planetType);
                object geography = Activator.CreateInstance(geographyType, new[] { world });
                planetType.GetField("SeaLevel").SetValue(world, calibrate.Invoke(geography, new object[] { .2 }));
                object land = landFor.Invoke(null, new[] { world });
                build.Invoke(null, new[] { world, new Random(1028038769), land });
                validate.Invoke(world, null);
                string serialized = JsonConvert.SerializeObject(world);
                var original = JObject.Parse(serialized);
                object restored = JsonConvert.DeserializeObject(serialized, planetType);
                validate.Invoke(restored, null);
                var saved = JObject.Parse(JsonConvert.SerializeObject(restored));
                Check("wilderness ownership survives a validated world round trip (resolution " + resolution + ")", () =>
                    (JToken.DeepEquals(original["TileOwners"], saved["TileOwners"])
                        && original["TileOwners"].Any(id => (int)id == -1)
                        && JToken.DeepEquals(original["Options"], saved["Options"]), "cell ownership and fixed local options"));
                int tile = (int)original["Provinces"][0]["Villages"][0]["TileId"];
                object point = localPoint.Invoke(geography, new object[] { tile, .23, .71 });
                int seed = (int)nativeSeed.Invoke(geography, new object[] { tile });
                var changed = (JObject)original.DeepClone(); changed["TileOwners"][tile] = -1;
                object changedWorld = JsonConvert.DeserializeObject(changed.ToString(), planetType);
                object changedGeography = Activator.CreateInstance(geographyType, new[] { changedWorld });
                object nextPoint = localPoint.Invoke(changedGeography, new object[] { tile, .23, .71 });
                int nextSeed = (int)nativeSeed.Invoke(changedGeography, new object[] { tile });
                Check("changing political ownership leaves native geography and seed unchanged (resolution " + resolution + ")", () =>
                    (JsonConvert.SerializeObject(point) == JsonConvert.SerializeObject(nextPoint) && seed == nextSeed, "tile " + tile));
                build.Invoke(null, new[] { world, new Random(1028038769), land });
                Check("national subdivision repeats deterministically (resolution " + resolution + ")", () =>
                    (JsonConvert.SerializeObject(world) == serialized, "territory, settlements and road endpoints"));
                foreach (string field in new[] { "LocalMapVersion", "NationalGenerationVersion" })
                {
                    var invalid = (JObject)original.DeepClone(); invalid[field] = 2;
                    bool rejected = RejectTileWorld(invalid, planetType, validate);
                    Check("unknown " + field + " is refused (resolution " + resolution + ")", () => (rejected, field + "=2"));
                }
                foreach (int owner in new[] { -2, original["Provinces"].Count() })
                {
                    var invalid = (JObject)original.DeepClone(); invalid["TileOwners"][0] = owner;
                    bool rejected = RejectTileWorld(invalid, planetType, validate);
                    Check("invalid tile owner is refused (resolution " + resolution + ", owner " + owner + ")", () => (rejected, "cell 0"));
                }
                var unanchored = (JObject)original.DeepClone(); unanchored["Provinces"][0]["Villages"][0]["TileId"] = -1;
                bool noAnchor = RejectTileWorld(unanchored, planetType, validate);
                Check("a settlement without its geographic anchor is refused (resolution " + resolution + ")", () => (noAnchor, "Village.TileId=-1"));
                yield return null;
                Type generator = planetType.Assembly.GetType("TruePlanet.PlanetGenerator");
                object options = planetType.GetField("Options").GetValue(world);
                FieldInfo size = options.GetType().GetField("LocalSize");
                size.SetValue(options, Enum.ToObject(size.FieldType, resolution == 5 ? 0 : 4));
                object generated = generator.GetMethod("Generate").Invoke(null, new[] { (object)1028038769, options });
                validate.Invoke(generated, null);
                var output = JObject.Parse(JsonConvert.SerializeObject(generated));
                Check("new generation emits geographic tile worlds with the requested fixed dimensions (resolution " + resolution + ")", () =>
                    ((int)output["FormatVersion"] == 3 && (int)output["NationalGenerationVersion"] == 1
                        && (int)output["LocalMapVersion"] == 1 && JToken.DeepEquals(output["Options"]["LocalSize"], JToken.FromObject(size.GetValue(options)))
                        && output["Provinces"].SelectMany(p => p["Villages"]).All(v => (int)v["TileId"] >= 0),
                        "format, versions, map size and settlement anchors"));
            }
            nativeSave = nativeSave ?? SaveManager.Instance?.saveCurrentProgressManager?.currentSaveDataPath;
            if (!string.IsNullOrEmpty(nativeSave) && System.IO.File.Exists(nativeSave))
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(nativeSave))
                    if (archive.GetEntry("ModData/trueplanet.tile.json") == null) nativeSave = null;
            }
            if (!string.IsNullOrEmpty(nativeSave) && System.IO.File.Exists(nativeSave)) TruePlanetStorageChecks(planetType, nativeSave);
            else Skip("campaign storage preserves old revisions and failed-commit references", "no completed geographic native save is loaded");
        }

        private void TruePlanetGlobeChecks(Assembly assembly)
        {
            const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo intersection = assembly.GetType("TruePlanet.GlobeProjection")?.GetMethod("TryDirection", methods);
            void RayCheck(string name, Vector3 origin, Vector3 delta, Vector3 centre, Quaternion rotation, float radius, bool expected, Vector3 near)
            {
                Check(name, () =>
                {
                    if (intersection == null) return (false, "missing TruePlanet.GlobeProjection");
                    object[] args = { new Ray(origin, delta), centre, rotation, radius, null };
                    bool hit = (bool)intersection.Invoke(null, args);
                    if (hit != expected) return (false, "forward intersection=" + hit);
                    if (!hit) return (true, "no forward surface hit");
                    object point = args[4]; Type vector = point.GetType();
                    double x = (double)vector.GetField("X").GetValue(point), y = (double)vector.GetField("Y").GetValue(point), z = (double)vector.GetField("Z").GetValue(point);
                    return (Math.Abs(x - near.x) < 1e-6 && Math.Abs(y - near.y) < 1e-6 && Math.Abs(z - near.z) < 1e-6,
                        "local near surface " + x + ", " + y + ", " + z);
                });
            }
            RayCheck("globe ray selects near hemisphere", new Vector3(0, 0, -3), Vector3.forward, Vector3.zero, Quaternion.identity, 1, true, new Vector3(0, 0, -1));
            RayCheck("off-globe ray does not select a cell", new Vector3(2, 0, -3), Vector3.forward, Vector3.zero, Quaternion.identity, 1, false, Vector3.zero);
            RayCheck("globe behind ray does not select a cell", new Vector3(0, 0, -3), Vector3.back, Vector3.zero, Quaternion.identity, 1, false, Vector3.zero);
            RayCheck("inside-globe ray selects forward exit", Vector3.zero, Vector3.right, Vector3.zero, Quaternion.identity, 1, true, Vector3.right);
            RayCheck("tangent globe ray resolves limb direction", new Vector3(1, 0, -3), Vector3.forward, Vector3.zero, Quaternion.identity, 1, true, Vector3.right);
            RayCheck("zero-direction globe ray is a miss", new Vector3(0, 0, -3), Vector3.zero, Vector3.zero, Quaternion.identity, 1, false, Vector3.zero);
            float half = (float)Math.Sqrt(.5);
            RayCheck("rotated translated scaled globe preserves local identity", new Vector3(10, 20, 24), Vector3.forward,
                new Vector3(10, 20, 30), new Quaternion(0, half, 0, half), 2, true, Vector3.right);
        }

        private static bool RejectTileWorld(JObject data, Type planetType, MethodInfo validate)
        {
            try { validate.Invoke(JsonConvert.DeserializeObject(data.ToString(), planetType), null); }
            catch (TargetInvocationException e) when (e.InnerException is System.IO.InvalidDataException) { return true; }
            return false;
        }
    }
}
