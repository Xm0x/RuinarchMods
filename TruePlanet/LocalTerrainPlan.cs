using System;
using System.Collections.Generic;
using System.Globalization;

namespace TruePlanet
{
    // One deterministic terrain contract for native generation and player-facing inspection.
    internal sealed class LocalTerrainPlan
    {
        internal int Width, Height, Open, Mountains, Water;
        internal float[,] Temperature, Moisture, Elevation;
        internal int Count => Width * Height;

        internal string TerrainSummary() =>
            (Open * 100.0 / Count).ToString("0", CultureInfo.InvariantCulture) + "% open land · "
            + (Mountains * 100.0 / Count).ToString("0", CultureInfo.InvariantCulture) + "% mountains · "
            + (Water * 100.0 / Count).ToString("0", CultureInfo.InvariantCulture) + "% water";

        internal static LocalTerrainPlan Build(Planet planet, int tileId)
        {
            PlanetOptions.NativeDimensions(planet.Options.LocalSize, out int columns, out int rows);
            var plan = new LocalTerrainPlan
            {
                Width = columns * 14, Height = rows * 14,
                Temperature = new float[columns * 14, rows * 14],
                Moisture = new float[columns * 14, rows * 14],
                Elevation = new float[columns * 14, rows * 14]
            };
            plan.Populate(planet, tileId);
            if (planet.LocalMapVersion >= 2) plan.ConstrainMountains();
            plan.CountClassifications();
            return plan;
        }

        private void Populate(Planet planet, int tileId)
        {
            var geography = new PlanetGeography(planet);
            LocalTileFootprint footprint = geography.TileFootprint(tileId);
            for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++)
            {
                TerrainSample sample = geography.Sample(footprint.Direction((x + .5) / Width, (y + .5) / Height));
                Temperature[x, y] = (float)sample.Temperature;
                Moisture[x, y] = (float)sample.Moisture;
                Elevation[x, y] = planet.LocalMapVersion == 1 ? geography.Elevation(sample) : ContinuousElevation(sample);
            }
        }

        private static float ContinuousElevation(TerrainSample sample)
        {
            if (sample.Height <= 0) return (float)(.22 - .20 * PlanetGeography.Clamp(-sample.Height / .5));
            if (sample.Mountains <= .55) return (float)(.24 + .53 * PlanetGeography.Clamp(sample.Height / .8));
            return (float)(.79 + .19 * PlanetGeography.Clamp(sample.Mountains));
        }

        private void ConstrainMountains()
        {
            // Do not cut secondary ranges at arbitrary local-map edges. Coastal cliffs may meet water.
            for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++)
            {
                bool edge = x < 3 || y < 3 || x >= Width - 3 || y >= Height - 3;
                if (!edge || Elevation[x, y] <= .78f || IsNearWater(x, y)) continue;
                Elevation[x, y] = .5f;
            }
            while (true)
            {
                MountainComponent largest = LargestMountainComponent();
                if (largest == null || largest.Cells.Count * 4 <= Count) return;
                bool horizontal = largest.MaxX - largest.MinX >= largest.MaxY - largest.MinY;
                int start = horizontal ? (largest.MinX + largest.MaxX) / 2 : (largest.MinY + largest.MaxY) / 2;
                int removed = 0;
                foreach (int cell in largest.Cells)
                {
                    int x = cell / Height, y = cell % Height;
                    int coordinate = horizontal ? x : y;
                    if (coordinate < start || coordinate >= start + 2) continue;
                    Elevation[x, y] = .5f; removed++;
                }
                if (removed == 0) throw new InvalidOperationException("Mountain corridor did not reduce its component.");
            }
        }

        private bool IsNearWater(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && Elevation[nx, ny] < .23f) return true;
            }
            return false;
        }

        private sealed class MountainComponent
        {
            private readonly int _height;
            internal readonly List<int> Cells = new List<int>();
            internal int MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1;
            internal MountainComponent(int height) { _height = height; }
            internal void Add(int x, int y)
            {
                Cells.Add(x * _height + y);
                MinX = Math.Min(MinX, x); MinY = Math.Min(MinY, y);
                MaxX = Math.Max(MaxX, x); MaxY = Math.Max(MaxY, y);
            }
        }

        private MountainComponent LargestMountainComponent()
        {
            var seen = new bool[Width, Height];
            MountainComponent largest = null;
            for (int startX = 0; startX < Width; startX++) for (int startY = 0; startY < Height; startY++)
            {
                if (seen[startX, startY] || Elevation[startX, startY] <= .78f) continue;
                var component = new MountainComponent(Height);
                var queue = new Queue<int>(); queue.Enqueue(startX * Height + startY); seen[startX, startY] = true;
                while (queue.Count != 0)
                {
                    int current = queue.Dequeue(), x = current / Height, y = current % Height;
                    component.Add(x, y);
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (dx == 0 && dy == 0 || nx < 0 || ny < 0 || nx >= Width || ny >= Height
                            || seen[nx, ny] || Elevation[nx, ny] <= .78f) continue;
                        seen[nx, ny] = true; queue.Enqueue(nx * Height + ny);
                    }
                }
                if (largest == null || component.Cells.Count > largest.Cells.Count) largest = component;
            }
            return largest;
        }

        private void CountClassifications()
        {
            Open = Mountains = Water = 0;
            foreach (float elevation in Elevation)
            {
                if (elevation < .23f) Water++;
                else if (elevation > .78f) Mountains++;
                else Open++;
            }
        }
    }
}
