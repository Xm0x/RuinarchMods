using System;
using System.Collections.Generic;

namespace TruePlanet
{
    // Native wall creation consumes this mask; floors are not merely a visual overlay.
    internal static class CavePlan
    {
        internal readonly struct Exit
        {
            internal readonly int Edge, X, Y;
            internal Exit(int edge, int x, int y) { Edge = edge; X = x; Y = y; }
        }

        internal static List<Exit> Exits(Planet planet, int tile)
        {
            var result = new List<Exit>();
            var geography = new PlanetGeography(planet);
            TileLand land = TileLand.For(planet);
            PlanetOptions.NativeDimensions(planet.Options.LocalSize, out int columns, out int rows);
            int width = columns * 14, height = rows * 14;
            foreach (int edgeId in planet.Mesh.CellEdges[tile])
            {
                TileEdge edge = planet.Mesh.Edges[edgeId];
                if (!land.Cells[edge.A].Eligible || !land.Cells[edge.B].Eligible) continue;
                LocalTileFootprint first = geography.TileFootprint(edge.A), second = geography.TileFootprint(edge.B);
                // Both sides evaluate the same ordered candidates and both local terrain samples.
                for (int candidate = 0; candidate < 9; candidate++)
                {
                    double fraction = .3 + .05 * ((candidate + (int)(Hash(planet.Seed, edgeId) % 9)) % 9);
                    UnitVector direction = (edge.First * (1 - fraction) + edge.Second * fraction).Normalized();
                    BoundaryCell(first, direction, width, height, out int ax, out int ay);
                    BoundaryCell(second, direction, width, height, out int bx, out int by);
                    if (!MountainEntry(geography, first, ax, ay, width, height)
                        || !MountainEntry(geography, second, bx, by, width, height)) continue;
                    result.Add(tile == edge.A ? new Exit(edgeId, ax, ay) : new Exit(edgeId, bx, by));
                    break;
                }
            }
            return result;
        }

        private static void BoundaryCell(LocalTileFootprint chart, UnitVector direction, int width, int height, out int x, out int y)
        {
            chart.Coordinates(direction, out double u, out double v);
            x = Math.Max(0, Math.Min(width - 1, (int)(u * width)));
            y = Math.Max(0, Math.Min(height - 1, (int)(v * height)));
            double left = u, right = 1 - u, bottom = v, top = 1 - v;
            double closest = Math.Min(Math.Min(left, right), Math.Min(bottom, top));
            if (closest == left) x = 0;
            else if (closest == right) x = width - 1;
            else if (closest == bottom) y = 0;
            else y = height - 1;
        }

        private static bool MountainEntry(PlanetGeography geography, LocalTileFootprint chart, int x, int y, int width, int height)
        {
            // Avoid exits through slivers that native elevation-island cleanup could absorb.
            int dx = x == 0 ? 1 : x == width - 1 ? -1 : 0;
            int dy = y == 0 ? 1 : y == height - 1 ? -1 : 0;
            // A corner has no orthogonal interior neighbor: reaching it would
            // require opening another, unpaired boundary cell.
            if (dx != 0 && dy != 0) return false;
            for (int inward = 0; inward < 20; inward++) for (int lateral = -2; lateral <= 2; lateral++)
            {
                int px = x + dx * inward + (dx == 0 ? lateral : 0);
                int py = y + dy * inward + (dy == 0 ? lateral : 0);
                if ((uint)px >= width || (uint)py >= height) return false;
                TerrainSample sample = geography.Sample(chart.Direction((px + .5) / width, (py + .5) / height));
                if (sample.Height <= 0 || sample.Mountains <= .55) return false;
            }
            return true;
        }

        internal static bool[,] Build(bool[,] mountain, LocalTerrainPlan terrain, List<Exit> exits, int seed, int offsetX, int offsetY)
        {
            int width = mountain.GetLength(0), height = mountain.GetLength(1);
            var entrances = new List<int>();
            foreach (Exit exit in exits)
            {
                int x = exit.X - offsetX, y = exit.Y - offsetY;
                if ((uint)x < width && (uint)y < height && mountain[x, y]) entrances.Add(x * height + y);
            }
            // A small number of native land-facing entrances; never cut unpaired map borders.
            int first = -1, second = -1;
            uint best = uint.MaxValue;
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                if (!mountain[x, y] || !FacesLand(terrain, x + offsetX, y + offsetY)) continue;
                uint score = Hash(seed, (x + offsetX) * terrain.Height + y + offsetY);
                if (score < best) { best = score; first = x * height + y; }
            }
            if (first >= 0)
            {
                entrances.Add(first); best = uint.MaxValue;
                for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
                {
                    if (!mountain[x, y] || Math.Abs(x - first / height) + Math.Abs(y - first % height) < 30
                        || !FacesLand(terrain, x + offsetX, y + offsetY)) continue;
                    uint score = Hash(seed ^ 0x3187, (x + offsetX) * terrain.Height + y + offsetY);
                    if (score < best) { best = score; second = x * height + y; }
                }
                if (second >= 0) entrances.Add(second);
            }
            return Carve(mountain, entrances, seed);
        }

        private static bool FacesLand(LocalTerrainPlan terrain, int x, int y)
        {
            if (x == 0 || y == 0 || x == terrain.Width - 1 || y == terrain.Height - 1) return false;
            return Land(x - 1, y) || Land(x + 1, y) || Land(x, y - 1) || Land(x, y + 1);
            bool Land(int px, int py) => terrain.Elevation[px, py] >= .23f && terrain.Elevation[px, py] <= .78f;
        }

        internal static bool[,] Carve(bool[,] mountain, List<int> entrances, int seed)
        {
            int width = mountain.GetLength(0), height = mountain.GetLength(1), count = width * height;
            var floor = new bool[width, height];
            var depth = new int[count];
            var queue = new int[count];
            int head = 0, tail = 0, area = 0;
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                int id = x * height + y;
                if (!mountain[x, y]) continue;
                area++;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1
                    || !mountain[x - 1, y] || !mountain[x + 1, y] || !mountain[x, y - 1] || !mountain[x, y + 1])
                { depth[id] = 1; queue[tail++] = id; }
            }
            while (head < tail)
            {
                int id = queue[head++];
                Visit(id, -1, 0); Visit(id, 1, 0); Visit(id, 0, -1); Visit(id, 0, 1);
                void Visit(int from, int dx, int dy)
                {
                    int x = from / height + dx, y = from % height + dy, next = x * height + y;
                    if ((uint)x >= width || (uint)y >= height || !mountain[x, y] || depth[next] != 0) return;
                    depth[next] = depth[from] + 1; queue[tail++] = next;
                }
            }
            if (area == 0) return floor;
            // Carve each four-connected island independently, including diagonally joined native islands.
            var parent = new int[count];
            var distance = new double[count];
            var passageCost = new float[count];
            var frontier = new TileQueue();
            for (int i = 0; i < count; i++) parent[i] = -1;
            var allowed = new HashSet<int>(entrances);
            for (int start = 0; start < count; start++)
            {
                if (depth[start] == 0 || parent[start] >= 0) continue;
                head = tail = 0; queue[tail++] = start; parent[start] = start;
                int anchor = start;
                bool hasEntrance = false;
                while (head < tail)
                {
                    int id = queue[head++];
                    if (allowed.Contains(id)) hasEntrance = true;
                    passageCost[id] = 1 + (Hash(seed ^ 0x6b31, (id / height / 6) * 7919 + id % height / 6) % 1024) / 256f
                        + (Hash(seed ^ 0x31f7, id) % 1024) / 2048f;
                    if (depth[id] > depth[anchor] || depth[id] == depth[anchor] && Hash(seed, id) < Hash(seed, anchor)) anchor = id;
                    Visit(-1, 0); Visit(1, 0); Visit(0, -1); Visit(0, 1);
                    void Visit(int dx, int dy)
                    {
                        int x = id / height + dx, y = id % height + dy, next = x * height + y;
                        if ((uint)x >= width || (uint)y >= height || depth[next] == 0 || parent[next] >= 0
                            || depth[next] == 1 && !allowed.Contains(next)) return;
                        parent[next] = next; queue[tail++] = next;
                    }
                }
                // Inaccessible rock stays solid; no disconnected hidden chambers.
                if (!hasEntrance) continue;
                int componentCount = tail;
                for (int i = 0; i < componentCount; i++)
                {
                    int id = queue[i]; parent[id] = -1; distance[id] = double.PositiveInfinity;
                }
                distance[anchor] = 0; parent[anchor] = anchor; frontier.Clear(); frontier.Push(anchor, 0, 0);
                while (frontier.Count != 0)
                {
                    TileQueue.Entry entry = frontier.Pop(); int id = entry.Tile;
                    if (entry.Cost != distance[id]) continue;
                    Visit(-1, 0); Visit(0, 1); Visit(1, 0); Visit(0, -1);
                    void Visit(int dx, int dy)
                    {
                        int x = id / height + dx, y = id % height + dy, next = x * height + y;
                        if ((uint)x >= width || (uint)y >= height || depth[next] == 0
                            || depth[next] == 1 && !allowed.Contains(next)) return;
                        double cost = entry.Cost + passageCost[next];
                        if (cost >= distance[next]) return;
                        distance[next] = cost; parent[next] = id; frontier.Push(next, 0, cost);
                    }
                }
                CarveChamber(anchor, 2);
                foreach (int entrance in entrances)
                    if ((uint)entrance < count && parent[entrance] >= 0) Connect(entrance);
                int chambers = Math.Min(8, tail / 1500);
                for (int chamber = 0; chamber < chambers; chamber++)
                {
                    int selected = -1; uint score = uint.MaxValue;
                    for (int i = 0; i < tail; i++)
                    {
                        int id = queue[i];
                        if (depth[id] < 4) continue;
                        uint candidate = Hash(seed ^ (chamber * 917 + 251), id);
                        if (candidate < score) { score = candidate; selected = id; }
                    }
                    if (selected >= 0) { Connect(selected); CarveChamber(selected, 2 + chamber % 2); }
                }
                void Connect(int id)
                {
                    while (true)
                    {
                        floor[id / height, id % height] = true;
                        if (parent[id] == id) break;
                        id = parent[id];
                    }
                }
                void CarveChamber(int id, int radius)
                {
                    int cx = id / height, cy = id % height;
                    for (int dx = -radius; dx <= radius; dx++) for (int dy = -radius; dy <= radius; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if ((uint)x >= width || (uint)y >= height || dx * dx + dy * dy > radius * radius
                            || parent[x * height + y] < 0 || depth[x * height + y] <= 1) continue;
                        Connect(x * height + y);
                    }
                }
            }
            return floor;
        }

        private static uint Hash(int seed, int id)
        {
            unchecked
            {
                uint value = (uint)seed ^ (uint)id * 374761393u;
                value = (value ^ (value >> 13)) * 1274126177u;
                return value ^ (value >> 16);
            }
        }
    }
}
