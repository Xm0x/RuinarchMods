using System;
using System.Collections.Generic;

namespace TruePlanet
{
	internal struct TerritoryLabel
	{
		internal bool Valid;
		internal MapPoint Center;
		internal double Width, Height, Angle;
	}
	// Label the largest connected land component; a capital can lie at its edge.
	internal static class MapTerritories
	{
		internal static TerritoryLabel[] Fit(int[] owners, float[] heights, int width, int height, double[] aspects)
		{
			int count = owners.Length; var regions = new int[count]; var unwrapped = new int[count]; var queue = new int[count];
			var largest = new int[aspects.Length]; var sizes = new int[aspects.Length]; int region = 0;
			int Wrap(int x) => (x % width + width) % width;
			int Neighbour(int at, int direction) { int x = at % width, y = at / width; return direction == 0 ? y * width + Wrap(x - 1) : direction == 1 ? y * width + Wrap(x + 1) : direction == 2 ? (y == 0 ? -1 : at - width) : (y == height - 1 ? -1 : at + width); }
			for (int start = 0; start < count; start++)
			{
				int owner = owners[start]; if (owner < 0 || heights[start] <= 0 || regions[start] != 0) continue;
				region++; int head = 0, tail = 1; queue[0] = start; regions[start] = region; unwrapped[start] = start % width;
				while (head < tail)
				{
					int at = queue[head++];
					for (int d = 0; d < 4; d++) { int next = Neighbour(at, d); if (next < 0 || regions[next] != 0 || owners[next] != owner || heights[next] <= 0) continue; regions[next] = region; unwrapped[next] = unwrapped[at] + (d == 0 ? -1 : d == 1 ? 1 : 0); queue[tail++] = next; }
				}
				if (tail > sizes[owner]) { sizes[owner] = tail; largest[owner] = region; }
			}
			var result = new TerritoryLabel[aspects.Length]; var clearance = new int[count]; var cells = new List<int>(); var candidates = new List<int>();
			for (int nation = 0; nation < result.Length; nation++)
			{
				if (sizes[nation] == 0) continue;
				cells.Clear(); candidates.Clear(); double mx = 0, my = 0; int head = 0, tail = 0, target = largest[nation];
				for (int at = 0; at < count; at++) if (regions[at] == target)
				{
					cells.Add(at); mx += unwrapped[at]; my += at / width; clearance[at] = 0;
					for (int d = 0; d < 4; d++) { int next = Neighbour(at, d); if (next < 0 || regions[next] != target) { clearance[at] = 1; queue[tail++] = at; break; } }
				}
				mx /= cells.Count; my /= cells.Count; double xx = 0, xy = 0, yy = 0;
				foreach (int at in cells) { double x = unwrapped[at] - mx, y = at / width - my; xx += x * x; xy += x * y; yy += y * y; }
				while (head < tail) { int at = queue[head++]; for (int d = 0; d < 4; d++) { int next = Neighbour(at, d); if (next >= 0 && regions[next] == target && clearance[next] == 0) { clearance[next] = clearance[at] + 1; queue[tail++] = next; } } }
				cells.Sort((a, b) => { int compare = clearance[b].CompareTo(clearance[a]); return compare != 0 ? compare : a.CompareTo(b); });
				double separation = Math.Max(2, Math.Sqrt(cells.Count) / 12), principal = Math.Max(-55, Math.Min(55, Math.Atan2(2 * xy, xx - yy) * 90 / Math.PI));
				foreach (int at in cells)
				{
					bool near = false; foreach (int chosen in candidates) { double dx = unwrapped[at] - unwrapped[chosen], dy = at / width - chosen / width; if (dx * dx + dy * dy < separation * separation) { near = true; break; } }
					if (!near) candidates.Add(at); if (candidates.Count == 32) break;
				}
				double best = 0, aspect = aspects[nation];
				foreach (int at in candidates) for (int orientation = 0; orientation < 4; orientation++)
				{
					double angle = orientation == 0 ? principal : orientation == 1 ? 0 : orientation == 2 ? -30 : 30;
					double cos = Math.Cos(angle * Math.PI / 180), sin = Math.Sin(angle * Math.PI / 180), cx = at % width + .5, cy = at / width + .5;
					double low = 0, high = Math.Min(height * .14, Math.Sqrt(cells.Count / Math.Max(1, aspect)));
					for (int step = 0; step < 10; step++) { double size = (low + high) * .5; if (Inside(cx, cy, size * aspect, size, cos, sin, target)) low = size; else high = size; }
					double score = low * aspect * (1 - Math.Abs(angle) / 300);
					if (score <= best) continue; best = score;
					result[nation] = new TerritoryLabel { Valid = low >= .5, Center = new MapPoint(cx / width, cy / height), Width = low * aspect * 1.84 / width, Height = low * 1.84 / height, Angle = angle };
				}
			}
			return result;
			bool Inside(double cx, double cy, double rx, double ry, double cos, double sin, int target)
			{
				double ex = Math.Abs(cos) * rx + Math.Abs(sin) * ry, ey = Math.Abs(sin) * rx + Math.Abs(cos) * ry;
				if (cx - ex < 0 || cx + ex > width || cy - ey < 0 || cy + ey > height) return false;
				for (int corner = 0; corner < 4; corner++) { double x = (corner % 2 == 0 ? -rx : rx), y = (corner < 2 ? -ry : ry); int at = (int)(cy + x * sin + y * cos) * width + (int)(cx + x * cos - y * sin); if (regions[Math.Min(count - 1, at)] != target) return false; }
				for (int y = (int)(cy - ey); y <= (int)(cy + ey) && y < height; y++) for (int x = (int)(cx - ex); x <= (int)(cx + ex) && x < width; x++)
				{
					double dx = x + .5 - cx, dy = y + .5 - cy;
					if (Math.Abs(dx * cos + dy * sin) <= rx && Math.Abs(-dx * sin + dy * cos) <= ry && regions[y * width + x] != target) return false;
				}
				return true;
			}
		}
	}
}
