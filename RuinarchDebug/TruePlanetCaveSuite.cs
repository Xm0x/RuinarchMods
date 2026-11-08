using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private static (bool, string) SparseCaveConnectivity(object world)
        {
            Type cave = world.GetType().Assembly.GetType("TruePlanet.CavePlan", true);
            var mountain = new bool[100, 80];
            for (int x = 8; x < 92; x++) for (int y = 8; y < 72; y++) mountain[x, y] = true;
            var entrances = new List<int> { 8 * 80 + 40, 91 * 80 + 40 };
            var first = (bool[,])RevisionStatic(cave, "Carve", mountain, entrances, 12345);
            var second = (bool[,])RevisionStatic(cave, "Carve", mountain, entrances, 12345);
            var sealedRock = (bool[,])RevisionStatic(cave, "Carve", mountain, new List<int>(), 12345);
            foreach (bool open in sealedRock) if (open) return (false, "An inaccessible mountain developed a disconnected hidden chamber.");
            int floors = 0;
            for (int x = 0; x < 100; x++) for (int y = 0; y < 80; y++)
            {
                if (first[x, y] != second[x, y]) return (false, "Regeneration changed cave floors.");
                if (!first[x, y]) continue;
                floors++;
                if (!mountain[x, y]) return (false, "Cave escaped its mountain island.");
                if ((x == 8 || x == 91 || y == 8 || y == 71) && !entrances.Contains(x * 80 + y))
                    return (false, "An unpaired cave boundary was opened.");
            }
            var seen = new HashSet<int>(); var queue = new Queue<int>();
            queue.Enqueue(entrances[0]); seen.Add(entrances[0]);
            while (queue.Count != 0)
            {
                int id = queue.Dequeue(), x = id / 80, y = id % 80;
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
                void Visit(int nx, int ny)
                {
                    if ((uint)nx >= 100 || (uint)ny >= 80 || !first[nx, ny] || !seen.Add(nx * 80 + ny)) return;
                    queue.Enqueue(nx * 80 + ny);
                }
            }
            bool pass = floors >= 100 && floors <= 5376 * .22 && seen.Count == floors && seen.Contains(entrances[1]);
            return (pass, floors + "/5376 floor cells; reachable=" + seen.Count + "; both entrances=" + seen.Contains(entrances[1]));
        }

        private static (bool, string) GeographicCaveConnections(object world)
        {
            Assembly assembly = world.GetType().Assembly;
            Type cave = assembly.GetType("TruePlanet.CavePlan", true), vector = assembly.GetType("TruePlanet.UnitVector", true);
            object mesh = RevisionMember(world, "Mesh"); Array edges = (Array)RevisionMember(mesh, "Edges");
            object geography = Activator.CreateInstance(assembly.GetType("TruePlanet.PlanetGeography", true), world);
            int checkedPairs = 0;
            var cached = new Dictionary<int, IList>();
            for (int edgeId = 0; edgeId < edges.Length && checkedPairs < 12; edgeId++)
            {
                object edge = edges.GetValue(edgeId);
                object first = RevisionMember(edge, "First"), second = RevisionMember(edge, "Second");
                object midpoint = RevisionCall(RevisionStatic(vector, "op_Addition", first, second), "Normalized");
                object sample = RevisionCall(geography, "Sample", midpoint);
                if ((double)RevisionMember(sample, "Height") <= 0 || (double)RevisionMember(sample, "Mountains") <= .55) continue;
                int a = (int)RevisionMember(edge, "A"), b = (int)RevisionMember(edge, "B");
                if (!cached.TryGetValue(a, out IList exitsA)) cached.Add(a, exitsA = (IList)RevisionStatic(cave, "Exits", world, a));
                if (!cached.TryGetValue(b, out IList exitsB)) cached.Add(b, exitsB = (IList)RevisionStatic(cave, "Exits", world, b));
                object exitA = Find(exitsA), exitB = Find(exitsB);
                if ((exitA == null) != (exitB == null)) return (false, "Only one map has an opening on edge " + edgeId);
                if (exitA == null) continue;
                PlanetChart(a, exitA); PlanetChart(b, exitB); checkedPairs++;
                object Find(IList exits)
                {
                    foreach (object exit in exits) if ((int)RevisionMember(exit, "Edge") == edgeId) return exit;
                    return null;
                }
                void PlanetChart(int tile, object exit)
                {
                    object chart = RevisionCall(geography, "TileFootprint", tile);
                    int x = (int)RevisionMember(exit, "X"), y = (int)RevisionMember(exit, "Y");
                    object options = RevisionMember(world, "Options");
                    object[] dimensions = { RevisionMember(options, "LocalSize"), 0, 0 };
                    options.GetType().GetMethod("NativeDimensions", RevisionMembers).Invoke(null, dimensions);
                    int width = (int)dimensions[1] * 14, height = (int)dimensions[2] * 14;
                    if (x != 0 && x != width - 1 && y != 0 && y != height - 1) throw new InvalidOperationException("Exit is not at the map boundary.");
                    object direction = RevisionCall(chart, "Direction", (x + .5) / width, (y + .5) / height);
                    object localSample = RevisionCall(geography, "Sample", direction);
                    if ((double)RevisionMember(localSample, "Height") <= 0 || (double)RevisionMember(localSample, "Mountains") <= .55)
                        throw new InvalidOperationException("Cave exit crosses non-mountain terrain.");
                    object[] inverse = { direction, 0.0, 0.0 };
                    chart.GetType().GetMethod("Coordinates", RevisionMembers).Invoke(chart, inverse);
                    if (Math.Abs((double)inverse[1] - (x + .5) / width) > 1e-9 || Math.Abs((double)inverse[2] - (y + .5) / height) > 1e-9)
                        throw new InvalidOperationException("Local chart inverse changed the exit position.");
                }
            }
            return (checkedPairs == 12, checkedPairs + " reciprocal geographic cave edges; native terrain and chart inverse verified on both sides.");
        }
    }
}
