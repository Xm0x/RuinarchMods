using System;
using System.Collections.Generic;

namespace TruePlanet
{
	// Necessary native village conditions, not a replacement for native generation/assignment.
	internal sealed class SettlementTerrain
	{
		private readonly Planet _planet;
		private readonly PlanetGeography _geography;
		private readonly Dictionary<int, byte> _checked = new Dictionary<int, byte>();
		internal SettlementTerrain(Planet planet, PlanetGeography geography) { _planet = planet; _geography = geography; }
		internal bool Suitable(int tile, bool elven)
		{
			if (!_checked.TryGetValue(tile, out byte suitability))
			{
				PlanetOptions.NativeDimensions(_planet.Options.LocalSize, out int columns, out int rows);
				var areas = new byte[columns * rows]; var trees = new int[areas.Length]; var caves = new int[areas.Length];
				var footprint = _geography.TileFootprint(tile); int width = columns * 14, height = rows * 14;
				for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
				{
					TerrainSample sample = _geography.Sample(footprint.Direction((x + .5) / width, (y + .5) / height));
					float elevation = _geography.Elevation(sample); int area = x / 14 * rows + y / 14;
					if (elevation < .23f) areas[area] |= 2;
					else if (elevation > .78f) { areas[area] |= 4; caves[area]++; }
					else { areas[area] |= 1; if (sample.Biome == 0 || sample.Biome == 1) trees[area]++; }
				}
				var seen = new int[areas.Length]; var queue = new int[areas.Length]; int visit = 0;
				for (int start = 0; start < areas.Length && suitability != 3; start++)
				{
					if (areas[start] != 1) continue;
					int sx = start / rows, sy = start % rows;
					byte neighbours = 0;
					if (sx > 0) neighbours |= areas[start - rows]; if (sx + 1 < columns) neighbours |= areas[start + rows];
					if (sy > 0) neighbours |= areas[start - 1]; if (sy + 1 < rows) neighbours |= areas[start + 1];
					if ((neighbours & 6) == 0) continue;
					visit++; int head = 0, tail = 1; queue[0] = start; seen[start] = visit; bool wood = false, mine = false;
					while (head < tail)
					{
						int current = queue[head++], x = current / rows, y = current % rows;
						wood |= current != start && trees[current] >= 8;
						if (x > 0) Neighbour(current - rows, visit, areas, caves, seen, queue, ref tail, ref mine);
						if (x + 1 < columns) Neighbour(current + rows, visit, areas, caves, seen, queue, ref tail, ref mine);
						if (y > 0) Neighbour(current - 1, visit, areas, caves, seen, queue, ref tail, ref mine);
						if (y + 1 < rows) Neighbour(current + 1, visit, areas, caves, seen, queue, ref tail, ref mine);
					}
					if (tail < 6) continue;
					if (mine) suitability |= 1; if (wood) suitability |= 2;
				}
				_checked.Add(tile, suitability);
			}
			return (suitability & (elven ? 2 : 1)) != 0;
		}
		private static void Neighbour(int next, int visit, byte[] areas, int[] caves, int[] seen, int[] queue, ref int tail, ref bool mine)
		{
			if (seen[next] == visit) return; seen[next] = visit;
			if (areas[next] == 1) queue[tail++] = next;
			else mine |= caves[next] > 5;
		}
	}
}
