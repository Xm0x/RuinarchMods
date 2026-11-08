using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private const BindingFlags RevisionMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static object RevisionMember(object target, string name)
        {
            if (target == null) throw new InvalidOperationException("Missing reflection owner for " + name);
            FieldInfo field = target.GetType().GetField(name, RevisionMembers);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = target.GetType().GetProperty(name, RevisionMembers);
            if (property != null) return property.GetValue(target, null);
            throw new InvalidOperationException("Missing " + target.GetType().FullName + "." + name);
        }

        private static object RevisionStatic(Type type, string method, params object[] args) =>
            type.GetMethod(method, RevisionMembers).Invoke(null, args);

        private static object RevisionCall(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, RevisionMembers).Invoke(target, args);

        private static object GenerateRevisionWorld(Assembly assembly, int localSize = 0, int resolution = 5, int seed = 1028038769)
        {
            Type optionsType = assembly.GetType("TruePlanet.PlanetOptions", true);
            object options = Activator.CreateInstance(optionsType);
            optionsType.GetField("Provinces").SetValue(options, 30);
            optionsType.GetField("Nations").SetValue(options, 2);
            optionsType.GetField("LandShare").SetValue(options, .4);
            optionsType.GetField("WildShare").SetValue(options, .25);
            optionsType.GetField("LocalSize").SetValue(options, Enum.ToObject(optionsType.GetField("LocalSize").FieldType, localSize));
            optionsType.GetField("Resolution").SetValue(options, Enum.ToObject(optionsType.GetField("Resolution").FieldType, resolution));
            return RevisionStatic(assembly.GetType("TruePlanet.PlanetGenerator", true), "Generate", seed, options);
        }

        private static object GenerateLegacyRevisionWorld(Assembly assembly)
        {
            const int seed = 1028038769;
            Type planetType = assembly.GetType("TruePlanet.Planet", true), optionsType = assembly.GetType("TruePlanet.PlanetOptions", true);
            object options = Activator.CreateInstance(optionsType);
            optionsType.GetField("Provinces").SetValue(options, 30);
            optionsType.GetField("Nations").SetValue(options, 2);
            optionsType.GetField("LandShare").SetValue(options, .4);
            optionsType.GetField("WildShare").SetValue(options, .25);
            optionsType.GetField("LocalSize").SetValue(options, Enum.ToObject(optionsType.GetField("LocalSize").FieldType, 0));
            optionsType.GetField("Resolution").SetValue(options, Enum.ToObject(optionsType.GetField("Resolution").FieldType, 5));
            object world = Activator.CreateInstance(planetType);
            planetType.GetField("Id").SetValue(world, "planet-legacy-revision-fixture");
            planetType.GetField("Name").SetValue(world, "Legacy revision fixture");
            planetType.GetField("Seed").SetValue(world, seed);
            planetType.GetField("Options").SetValue(world, options);
            planetType.GetField("GeographyVersion").SetValue(world, 3);
            planetType.GetField("FormatVersion").SetValue(world, 3);
            planetType.GetField("TopologyVersion").SetValue(world, 1);
            planetType.GetField("NationalGenerationVersion").SetValue(world, 1);
            planetType.GetField("LocalMapVersion").SetValue(world, 1);
            object geography = Activator.CreateInstance(assembly.GetType("TruePlanet.PlanetGeography", true), world);
            planetType.GetField("SeaLevel").SetValue(world, RevisionCall(geography, "CalibrateSeaLevel", .4));
            object land = RevisionStatic(assembly.GetType("TruePlanet.TileLand", true), "For", world);
            RevisionStatic(assembly.GetType("TruePlanet.NationalProvinces", true), "Build", world, new Random(seed), land);
            RevisionCall(world, "Validate");
            return world;
        }

        private static int RevisionTile(object world, bool coastal)
        {
            object land = RevisionStatic(world.GetType().Assembly.GetType("TruePlanet.TileLand", true), "For", world);
            Array cells = (Array)RevisionMember(land, "Cells");
            for (int i = 0; i < cells.Length; i++)
            {
                object cell = cells.GetValue(i);
                if ((bool)RevisionMember(cell, "Eligible") && (!coastal || (bool)RevisionMember(cell, "Coastal"))) return i;
            }
            throw new InvalidDataException("No eligible " + (coastal ? "coastal" : "dry") + " geographic tile in test world.");
        }


        private static int NativeWaterBands(float[,] elevation)
        {
            var bands = new HashSet<int>();
            foreach (float value in elevation)
            {
                if (value >= .23f) continue;
                bands.Add(value < .1f ? 0 : value < .13f ? 1 : value < .18f ? 2 : 3);
            }
            return bands.Count;
        }

        private static double NearestCellEdgeAngle(object world, int tile, object point)
        {
            object mesh = RevisionMember(world, "Mesh");
            Array edgeIds = (Array)((Array)RevisionMember(mesh, "CellEdges")).GetValue(tile); Array edges = (Array)RevisionMember(mesh, "Edges");
            Type vectorType = point.GetType();
            double nearest = double.PositiveInfinity;
            foreach (int edgeId in edgeIds)
            {
                object edge = edges.GetValue(edgeId), a = RevisionMember(edge, "First"), b = RevisionMember(edge, "Second");
                object cross = RevisionCall(RevisionStatic(vectorType, "Cross", a, b), "Normalized");
                double dot = Math.Abs((double)RevisionStatic(vectorType, "Dot", cross, point));
                nearest = Math.Min(nearest, Math.Asin(Math.Max(0, Math.Min(1, dot))));
            }
            return nearest;
        }

        private static double InteriorMargin(object world, int tile)
        {
            object mesh = RevisionMember(world, "Mesh");
            object center = ((Array)RevisionMember(mesh, "Centers")).GetValue(tile);
            Array edgeIds = (Array)((Array)RevisionMember(mesh, "CellEdges")).GetValue(tile); Array edges = (Array)RevisionMember(mesh, "Edges");
            Type vectorType = center.GetType(); double nearest = double.PositiveInfinity;
            foreach (int edgeId in edgeIds)
            {
                object vertex = RevisionMember(edges.GetValue(edgeId), "First");
                double dot = (double)RevisionStatic(vectorType, "Dot", center, vertex);
                nearest = Math.Min(nearest, Math.Acos(Math.Max(-1, Math.Min(1, dot))));
            }
            return nearest * .05;
        }

        private IEnumerator TruePlanetRevisionSuite()
        {
            Type planetType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("TruePlanet.Planet", false)).FirstOrDefault(t => t != null);
            Type planType = planetType?.Assembly.GetType("TruePlanet.LocalTerrainPlan", false);
            if (planetType == null || planType == null)
            {
                Check("local terrain plan feeds native dimensions and exact classifications", () =>
                    (false, "TruePlanet.LocalTerrainPlan is unavailable"));
                yield break;
            }

            object world = GenerateRevisionWorld(planetType.Assembly);
            Check("atlas biome classification matches the live native climate asset at every boundary", () => NativeClimateLookup(world));
            Check("coastlines and ridges remain continuous across plate boundaries", () => GeographicContinuity(world));
            Check("local maps remain inside their geographic cells", () => LocalFootprintOwnership(world));
            Check("solid mountain interiors cannot be played or settled while lowland remains available", () => MountainInteriorEligibility(world));
            Check("mountain caves remain sparse, connected and sealed outside their entrances", () => SparseCaveConnectivity(world));
            Check("neighboring cave exits share geographic edges and native mountain terrain", () => GeographicCaveConnections(world));
            int tile = RevisionTile(world, coastal: true);
            object plan = RevisionStatic(planType, "Build", world, tile);
            Check("local terrain plan feeds native dimensions and exact classifications", () =>
            {
                int open = 0, mountains = 0, water = 0;
                int width = (int)RevisionMember(plan, "Width"), height = (int)RevisionMember(plan, "Height");
                var elevation = (float[,])RevisionMember(plan, "Elevation");
                for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
                {
                    if (elevation[x, y] < .23f) water++;
                    else if (elevation[x, y] > .78f) mountains++;
                    else open++;
                }
                return (width == 140 && height == 140 && open + mountains + water == width * height
                    && open == (int)RevisionMember(plan, "Open") && mountains == (int)RevisionMember(plan, "Mountains")
                    && water == (int)RevisionMember(plan, "Water"), $"dimensions={width}x{height}; open={open}; mountains={mountains}; water={water}");
            });
            Check("continuous coastal depth covers every native water band", () =>
            {
                Type sampleType = planetType.Assembly.GetType("TruePlanet.TerrainSample", true);
                MethodInfo continuous = planType.GetMethod("ContinuousElevation", BindingFlags.Static | BindingFlags.NonPublic);
                double[] heights = { -.01, -.15, -.28, -.5 };
                var depths = new float[4, 1];
                for (int i = 0; i < heights.Length; i++)
                {
                    object sample = Activator.CreateInstance(sampleType);
                    sampleType.GetField("Height").SetValue(sample, heights[i]);
                    depths[i, 0] = (float)continuous.Invoke(null, new[] { sample });
                }
                int bands = NativeWaterBands(depths);
                return (bands == 4, $"depths={string.Join(",", depths.Cast<float>())}; native bands={bands}");
            });

            object context = planetType.Assembly.GetType("TruePlanet.TileGame", true).GetField("Active", RevisionMembers).GetValue(null);
            object oldWorld = GenerateLegacyRevisionWorld(planetType.Assembly);
            int oldTile = (int)RevisionMember(((Array)RevisionMember(oldWorld, "Provinces")).Cast<object>()
                .SelectMany(province => ((Array)RevisionMember(province, "Villages")).Cast<object>()).First(), "TileId");
            object oldPlan = RevisionStatic(planType, "Build", oldWorld, oldTile);
            Check("existing version-one maps retain their exact legacy terrain rule", () =>
            {
                var geography = Activator.CreateInstance(planetType.Assembly.GetType("TruePlanet.PlanetGeography", true), oldWorld);
                var elevation = (float[,])RevisionMember(oldPlan, "Elevation");
                int width = elevation.GetLength(0), height = elevation.GetLength(1);
                for (int x = 0; x < 16; x++) for (int y = 0; y < 16; y++)
                {
                    int sampleX = x * width / 16, sampleY = y * height / 16;
                    object direction = RevisionCall(geography, "LocalTileDirection", oldTile, (sampleX + .5) / width, (sampleY + .5) / height);
                    object sample = RevisionCall(geography, "Sample", direction);
                    double h = (double)RevisionMember(sample, "Height"), mountains = (double)RevisionMember(sample, "Mountains");
                    float expected = h <= 0 ? .15f : mountains > .55 ? .9f : .5f;
                    if (elevation[x * width / 16, y * height / 16] != expected)
                        return (false, $"legacy mismatch at sample {x},{y}: expected={expected}; actual={elevation[x * width / 16, y * height / 16]}");
                }
                return (true, "256 independent legacy samples");
            });


            Type mapGeometryType = planetType.Assembly.GetType("TruePlanet.MapGeometry", true);
            object revisionGeography = Activator.CreateInstance(planetType.Assembly.GetType("TruePlanet.PlanetGeography", true), world);
            object fullGeometry = RevisionStatic(mapGeometryType, "BuildSpherical", world, revisionGeography);
            object lowGeometry = RevisionStatic(mapGeometryType, "BuildSphericalLod", world, revisionGeography, fullGeometry);
            Check("low-zoom globe reduces real overlay geometry", () =>
            {
                int fullCount = ((ICollection)fullGeometry).Count, lowCount = ((ICollection)lowGeometry).Count;
                return (lowCount * 3 < fullCount, $"full={fullCount}; low={lowCount}");
            });
            Type vectorType = planetType.Assembly.GetType("TruePlanet.UnitVector", true);
            Type segmentType = planetType.Assembly.GetType("TruePlanet.SphericalSegment", true);
            object a = null, b = null, c = null;
            foreach (object province in (Array)RevisionMember(world, "Provinces")) foreach (object village in (Array)RevisionMember(province, "Villages"))
            {
                object anchor = RevisionMember(village, "Position");
                object axis = Activator.CreateInstance(vectorType, 0d, 0d, 1d);
                object east = RevisionCall(RevisionStatic(vectorType, "Cross", anchor, axis), "Normalized");
                if (Math.Sqrt((double)RevisionMember(east, "X") * (double)RevisionMember(east, "X")
                    + (double)RevisionMember(east, "Y") * (double)RevisionMember(east, "Y")
                    + (double)RevisionMember(east, "Z") * (double)RevisionMember(east, "Z")) < .5)
                    east = RevisionCall(RevisionStatic(vectorType, "Cross", anchor, Activator.CreateInstance(vectorType, 0d, 1d, 0d)), "Normalized");
                double ax = (double)RevisionMember(anchor, "X"), ay = (double)RevisionMember(anchor, "Y"), az = (double)RevisionMember(anchor, "Z");
                double ex = (double)RevisionMember(east, "X"), ey = (double)RevisionMember(east, "Y"), ez = (double)RevisionMember(east, "Z");
                object Point(double angle) => Activator.CreateInstance(vectorType, ax * Math.Cos(angle) + ex * Math.Sin(angle),
                    ay * Math.Cos(angle) + ey * Math.Sin(angle), az * Math.Cos(angle) + ez * Math.Sin(angle));
                object candidateB = Point(.006), candidateC = Point(.012), middle = Point(.006);
                if (new[] { anchor, candidateB, candidateC, middle }.All(point => (double)RevisionMember(RevisionCall(revisionGeography, "Sample", point), "Height") > 0))
                { a = anchor; b = candidateB; c = candidateC; break; }
            }
            Array chain = Array.CreateInstance(segmentType, 2);
            if (a != null)
            {
                object first = Activator.CreateInstance(segmentType), second = Activator.CreateInstance(segmentType);
                StorageSet(first, "A", a); StorageSet(first, "B", b); StorageSet(first, "Left", 5); StorageSet(first, "Right", 6); StorageSet(first, "Kind", (byte)1);
                StorageSet(second, "A", b); StorageSet(second, "B", c); StorageSet(second, "Left", 5); StorageSet(second, "Right", 6); StorageSet(second, "Kind", (byte)1);
                chain.SetValue(first, 0); chain.SetValue(second, 1);
            }
            Array CopyChain()
            {
                Array copy = Array.CreateInstance(segmentType, chain.Length);
                for (int i = 0; i < chain.Length; i++)
                {
                    object source = chain.GetValue(i), segment = Activator.CreateInstance(segmentType);
                    foreach (string name in new[] { "A", "B", "Left", "Right", "Kind" }) StorageSet(segment, name, RevisionMember(source, name));
                    copy.SetValue(segment, i);
                }
                return copy;
            }
            Check("LOD joining preserves a known chain's terminals", () =>
            {
                if (a == null) return (false, "no dry fixture arc");
                object[] joined = ((IEnumerable)RevisionStatic(mapGeometryType, "BuildSphericalLod", world, revisionGeography, chain)).Cast<object>().ToArray();
                return (joined.Length == 1 && JsonConvert.SerializeObject(RevisionMember(joined[0], "A")) == JsonConvert.SerializeObject(a)
                    && JsonConvert.SerializeObject(RevisionMember(joined[0], "B")) == JsonConvert.SerializeObject(c), "coincident endpoints retained");
            });
            Check("LOD never joins disconnected or different-kind strokes", () =>
            {
                if (a == null) return (false, "no dry fixture arc");
                Array different = CopyChain(); object differentItem = different.GetValue(1); StorageSet(differentItem, "Kind", (byte)2); different.SetValue(differentItem, 1);
                Array disconnected = CopyChain(); object disconnectedItem = disconnected.GetValue(1); StorageSet(disconnectedItem, "A", c); StorageSet(disconnectedItem, "B", b); disconnected.SetValue(disconnectedItem, 1);
                int differentCount = ((ICollection)RevisionStatic(mapGeometryType, "BuildSphericalLod", world, revisionGeography, different)).Count;
                int disconnectedCount = ((ICollection)RevisionStatic(mapGeometryType, "BuildSphericalLod", world, revisionGeography, disconnected)).Count;
                return (differentCount == 2 && disconnectedCount == 2, $"different-kind={differentCount}; disconnected={disconnectedCount}");
            });
            Check("LOD does not collapse a backtracking chain", () =>
            {
                if (a == null) return (false, "no dry fixture arc");
                Array backtracking = CopyChain();
                object segment = backtracking.GetValue(1);
                StorageSet(segment, "B", a);
                backtracking.SetValue(segment, 1);
                int count = ((ICollection)RevisionStatic(mapGeometryType, "BuildSphericalLod", world, revisionGeography, backtracking)).Count;
                return (count == 2, $"retained strokes={count}");
            });
            Check("LOD bounds traversed arc length rather than endpoint distance", () =>
            {
                if (a == null) return (false, "no dry fixture arc");
                Array bent = CopyChain();
                object first = bent.GetValue(0), second = bent.GetValue(1);
                StorageSet(first, "B", c);
                StorageSet(second, "A", c);
                StorageSet(second, "B", b);
                bent.SetValue(first, 0);
                bent.SetValue(second, 1);
                int count = ((ICollection)RevisionStatic(mapGeometryType, "BuildSphericalLod", world, revisionGeography, bent)).Count;
                return (count == 2, $"retained strokes={count}; traversed arc=0.018 radians");
            });
            Check("low-zoom coast joins retain dry sampled midpoints", () =>
            {
                foreach (object segment in (IEnumerable)lowGeometry)
                {
                    object sum = RevisionStatic(vectorType, "op_Addition", RevisionMember(segment, "A"), RevisionMember(segment, "B"));
                    object midpoint = RevisionCall(sum, "Normalized");
                    if ((double)RevisionMember(RevisionCall(revisionGeography, "Sample", midpoint), "Height") <= 0) return (false, "wet midpoint");
                }
                return (true, "all LOD midpoints remain dry");
            });

            for (int seedIndex = 0; seedIndex < 8; seedIndex++) foreach (int resolution in new[] { 5, 6 })
            {
                object generated = GenerateRevisionWorld(planetType.Assembly, 0, resolution, 1028038769 + seedIndex * 7919);
                Array provinces = (Array)RevisionMember(generated, "Provinces");
                int anchors = 0, outside = 0, preferredCapitals = 0, capitalCount = 0;
                foreach (object province in provinces) foreach (object village in (Array)RevisionMember(province, "Villages"))
                {
                    object position = RevisionMember(village, "Position");
                    int villageTile = (int)RevisionMember(village, "TileId");
                    double clearance = NearestCellEdgeAngle(generated, villageTile, position);
                    double required = InteriorMargin(generated, villageTile);
                    anchors++; if (clearance <= required) outside++;
                    if (!(bool)RevisionMember(village, "Capital")) continue;
                    capitalCount++;
                    object capitalPlan = RevisionStatic(planType, "Build", generated, villageTile);
                    int open = (int)RevisionMember(capitalPlan, "Open"), mountains = (int)RevisionMember(capitalPlan, "Mountains");
                    int count = (int)RevisionMember(capitalPlan, "Count");
                    if (open * 100 >= count * 65 && mountains * 4 <= count) preferredCapitals++;
                }
                Check("settlements use dry interior anchors (seed " + seedIndex + ", resolution " + resolution + ")", () =>
                    (outside == 0, $"edge/outside anchors={outside}/{anchors}"));
                Check("capital sites prefer open, non-dominant terrain (seed " + seedIndex + ", resolution " + resolution + ")", () =>
                    (preferredCapitals == capitalCount, $"preferred capitals={preferredCapitals}/{capitalCount}"));
                yield return null;
            }

            int activeTile = (int)RevisionMember(context, "TileId");
            string[] capitalNames = ((Array)RevisionMember(RevisionMember(context, "Planet"), "Provinces")).Cast<object>()
                .SelectMany(province => ((Array)RevisionMember(province, "Villages")).Cast<object>())
                .Where(village => (int)RevisionMember(village, "TileId") == activeTile && (bool)RevisionMember(village, "Capital"))
                .Select(village => (string)RevisionMember(village, "Name")).ToArray();
            NPCSettlement nativeCapital = GridMap.Instance.mainRegion.settlementsInRegion.OfType<NPCSettlement>()
                .FirstOrDefault(v => v.locationType == LOCATION_TYPE.VILLAGE && capitalNames.Contains(v.name));
            Check("native capital generation produces developed dwellings and facilities", () =>
            {
                if (nativeCapital == null) return (false, "no native capital settlement");
                var structures = nativeCapital.structures.Values.SelectMany(list => list).Where(s => !s.hasBeenDestroyed).ToArray();
                int dwellings = structures.Count(s => s.structureType == STRUCTURE_TYPE.DWELLING);
                int facilities = structures.Count(s => s.structureType != STRUCTURE_TYPE.DWELLING && s.structureType != STRUCTURE_TYPE.CITY_CENTER);
                int localSize = (int)RevisionMember(RevisionMember(RevisionMember(context, "Planet"), "Options"), "LocalSize");
                int minimumDwellings = localSize == 0 ? 9 : 13, maximumDwellings = localSize == 0 ? 11 : 15;
                int minimumFacilities = localSize == 0 ? 4 : 5;
                return (dwellings >= minimumDwellings && dwellings <= maximumDwellings && facilities >= minimumFacilities,
                    $"dwellings={dwellings} (expected {minimumDwellings}-{maximumDwellings}); facilities={facilities} (minimum {minimumFacilities})");
            });
            yield return null;
        }
    }
}
