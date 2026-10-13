using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private void TruePlanetPocketChecks(Type planetType, Type landType, Type nationalType)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object world = JsonConvert.DeserializeObject("{\"Seed\":7,\"Options\":{}}", planetType);
            object mesh = planetType.GetProperty("Mesh", fields).GetValue(world);
            Type meshType = mesh.GetType();
            var centers = (Array)meshType.GetField("Centers", fields).GetValue(mesh);
            var areas = (double[])meshType.GetField("Areas", fields).GetValue(mesh);
            var neighbours = (int[][])meshType.GetField("Neighbours", fields).GetValue(mesh);
            Type cellType = landType.Assembly.GetType("TruePlanet.TileLandSample");
            Array cells = Array.CreateInstance(cellType, centers.Length);
            var links = new int[centers.Length][];
            for (int i = 0; i < centers.Length; i++)
            {
                object cell = Activator.CreateInstance(cellType);
                cellType.GetField("Eligible", fields).SetValue(cell, true);
                cellType.GetField("LandArea", fields).SetValue(cell, areas[i]);
                cellType.GetField("DryPoint", fields).SetValue(cell, centers.GetValue(i));
                cells.SetValue(cell, i); links[i] = Array.Empty<int>();
            }
            int a = 0, b = neighbours[a][0], c = neighbours[b].First(i => i != a);
            links[a] = new[] { b }; links[b] = new[] { a, c }; links[c] = new[] { b };
            object land = landType.GetConstructors(fields).Single().Invoke(new object[] { cells, links });
            Type terrainType = landType.Assembly.GetType("TruePlanet.TerrainSample");
            Array terrain = Array.CreateInstance(terrainType, centers.Length);
            object sample = JsonConvert.DeserializeObject("{\"Height\":1,\"Moisture\":0.5}", terrainType);
            for (int i = 0; i < terrain.Length; i++) terrain.SetValue(sample, i);
            planetType.GetField("TileTerrain", fields).SetValue(world, terrain);
            var owners = Enumerable.Repeat(-1, centers.Length).ToArray(); owners[a] = owners[c] = 0;
            MethodInfo repair = nationalType.GetMethod("RepairPockets", BindingFlags.Static | BindingFlags.NonPublic);
            repair.Invoke(null, new[] { world, land, owners });
            Check("an enclosed single-nation neutral pocket joins that nation", () => (owners[b] == 0, "dry-linked interior"));
            Check("isolated dry islands remain unclaimed", () => (owners.Where((owner, tile) => tile != a && tile != b && tile != c).All(owner => owner == -1), "no national dry connection"));
            owners[b] = -1; owners[c] = 1;
            repair.Invoke(null, new[] { world, land, owners });
            Check("multi-nation pocket repair fills the gap without changing existing claims", () =>
                ((owners[b] == 0 || owners[b] == 1) && owners[a] == 0 && owners[c] == 1, "competing dry fronts"));
            object coastal = cells.GetValue(b); cellType.GetField("Coastal", fields).SetValue(coastal, true); cells.SetValue(coastal, b);
            owners[b] = -1; repair.Invoke(null, new[] { world, land, owners });
            Check("open coastal wilderness survives pocket repair", () => (owners[b] == -1 && owners[a] == 0 && owners[c] == 1, "coastal continuation"));
        }
    }
}
