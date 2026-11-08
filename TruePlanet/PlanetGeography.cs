using System;

namespace TruePlanet
{
	public struct TerrainSample
	{
		public double Height, Temperature, Moisture, Mountains;
		public int Biome => Temperature < .22 ? 3 : Moisture < .3 ? 2 : Moisture > .55 ? 1 : 0;
	}
	internal readonly struct LocalTileFootprint
	{
		private readonly UnitVector _center, _east, _north;
		private readonly double _span;
		private readonly Boundary[] _bounds;
		private readonly struct Boundary
		{
			internal readonly double X, Y, Distance;
			internal Boundary(UnitVector normal, UnitVector center, UnitVector east, UnitVector north)
			{
				if (UnitVector.Dot(normal, center) < 0) normal = normal * -1;
				X = UnitVector.Dot(normal, east); Y = UnitVector.Dot(normal, north);
				Distance = UnitVector.Dot(normal, center);
			}
		}
		internal LocalTileFootprint(UnitVector center, UnitVector east, double span)
		{
			_center = center; _east = east; _north = UnitVector.Cross(center, east).Normalized(); _span = span; _bounds = null;
		}
		internal LocalTileFootprint(TileMesh mesh, int tile, UnitVector east)
		{
			_center = mesh.Centers[tile]; _east = east; _north = UnitVector.Cross(_center, east).Normalized(); _span = 0;
			int[] edges = mesh.CellEdges[tile]; _bounds = new Boundary[edges.Length];
			for (int i = 0; i < edges.Length; i++)
			{
				TileEdge edge = mesh.Edges[edges[i]];
				_bounds[i] = new Boundary(UnitVector.Cross(edge.First, edge.Second).Normalized(), _center, _east, _north);
			}
		}
		internal UnitVector Direction(double u, double v)
		{
			if (_bounds == null)
				return (_center + _east * ((u * 2 - 1) * _span) + _north * ((v * 2 - 1) * _span)).Normalized();
			double x = u * 2 - 1, y = v * 2 - 1, extent = Math.Max(Math.Abs(x), Math.Abs(y));
			if (extent == 0) return _center;
			double radius = double.PositiveInfinity;
			foreach (Boundary boundary in _bounds)
			{
				double projection = boundary.X * x + boundary.Y * y;
				if (projection < 0) radius = Math.Min(radius, boundary.Distance / -projection);
			}
			// Map the square perimeter onto the exact cell boundary, then interpolate inward.
			double scale = radius * extent;
			return (_center + _east * (x * scale) + _north * (y * scale)).Normalized();
		}
		internal void Coordinates(UnitVector direction, out double u, out double v)
		{
			double depth = UnitVector.Dot(direction, _center);
			if (depth <= 0) throw new ArgumentException("Direction is outside the local chart.");
			double x = UnitVector.Dot(direction, _east) / depth, y = UnitVector.Dot(direction, _north) / depth;
			if (_bounds == null) { u = (x / _span + 1) * .5; v = (y / _span + 1) * .5; return; }
			double extent = Math.Max(Math.Abs(x), Math.Abs(y));
			if (extent < 1e-14) { u = v = .5; return; }
			double nx = x / extent, ny = y / extent, radius = double.PositiveInfinity;
			foreach (Boundary boundary in _bounds)
			{
				double projection = boundary.X * nx + boundary.Y * ny;
				if (projection < 0) radius = Math.Min(radius, boundary.Distance / -projection);
			}
			u = (x / radius + 1) * .5; v = (y / radius + 1) * .5;
		}
	}

	// Coordinates and terrain are independent of the flat atlas and Unity's native map.
	public sealed class PlanetGeography
	{
		private readonly Planet _planet;
		private readonly UnitVector[] _plates = new UnitVector[8], _drifts = new UnitVector[8];
		private readonly double[] _heights = new double[8], _spans;
		private readonly double[] _convergence;
		public PlanetGeography(Planet planet)
		{
			_planet = planet; var random = new Random(planet.Seed ^ 0x43e51c7b);
			for (int i = 0; i < _plates.Length; i++)
			{
				_plates[i] = Direction(random); _drifts[i] = UnitVector.Cross(_plates[i], Direction(random)).Normalized();
				_heights[i] = random.NextDouble() < .4 ? .45 + random.NextDouble() * .25 : -.45 + random.NextDouble() * .2;
			}
			if (planet.GeographyVersion >= 5)
			{
				_convergence = new double[28];
				int pair = 0;
				for (int i = 0; i < _plates.Length; i++) for (int j = i + 1; j < _plates.Length; j++)
					_convergence[pair++] = Math.Max(0, UnitVector.Dot(_drifts[i] - _drifts[j], (_plates[j] - _plates[i]).Normalized()));
			}
			// New worlds calibrate terrain before any political provinces exist.
			if (planet.FormatVersion == 3) { _spans = Array.Empty<double>(); return; }
			_spans = new double[planet.Provinces.Length];
			foreach (Province p in planet.Provinces)
			{
				double distance = Math.PI;
				foreach (int n in p.Neighbours) distance = Math.Min(distance, Math.Acos(ClampDot(UnitVector.Dot(p.Direction, planet.Provinces[n].Direction))));
				_spans[p.Id] = Math.Tan(distance * .22);
			}
			if (planet.TopologyVersion == 1 && planet.TileOwners != null)
			{
				Array.Clear(_spans, 0, _spans.Length);
				TileMesh mesh = planet.Mesh;
				for (int tile = 0; tile < planet.TileOwners.Length; tile++)
				{
					int owner = planet.TileOwners[tile];
					double angle = Math.Acos(ClampDot(UnitVector.Dot(mesh.Centers[tile], planet.Provinces[owner].Direction)));
					_spans[owner] = Math.Max(_spans[owner], Math.Tan(Math.Min(.5, angle) * .45));
				}
			}
		}
		private static UnitVector Direction(Random random) => UnitVector.FromMap(random.NextDouble() * Math.PI * 2, Math.Asin(random.NextDouble() * 2 - 1));
		private static double ClampDot(double value) => Math.Max(-1, Math.Min(1, value));
		public static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
		private static double Smooth(double value) => value * value * value * (value * (value * 6 - 15) + 10);
		private static double Mix(double a, double b, double t) => a + (b - a) * t;
		private double Hash(int x, int y, int z, int salt)
		{
			unchecked
			{
				uint h = (uint)(_planet.Seed ^ salt) ^ (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)z * 2147483647u;
				h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
				return (h >> 8) / 16777215.0 * 2 - 1;
			}
		}
		private double Noise(UnitVector p, double scale, int salt)
		{
			double px = p.X * scale + 31.7, py = p.Y * scale + 17.3, pz = p.Z * scale + 43.1;
			int x = (int)Math.Floor(px), y = (int)Math.Floor(py), z = (int)Math.Floor(pz);
			double u = Smooth(px - x), v = Smooth(py - y), w = Smooth(pz - z);
			return Mix(Mix(Mix(Hash(x,y,z,salt),Hash(x+1,y,z,salt),u), Mix(Hash(x,y+1,z,salt),Hash(x+1,y+1,z,salt),u),v),
				Mix(Mix(Hash(x,y,z+1,salt),Hash(x+1,y,z+1,salt),u), Mix(Hash(x,y+1,z+1,salt),Hash(x+1,y+1,z+1,salt),u),v),w);
		}
		private double Fractal(UnitVector point)
		{
			double value = 0, amplitude = .28, scale = 2.7;
			for (int i = 0; i < 5; i++) { value += Noise(point, scale, i * 917 + 37) * amplitude; scale *= 2.05; amplitude *= .48; }
			return value;
		}
		private TerrainSample Raw(UnitVector point)
		{
			if (_planet.GeographyVersion >= 5) return ContinuousTerrain(point);
			int first = 0, second = 1;
			if (UnitVector.Dot(point, _plates[1]) > UnitVector.Dot(point, _plates[0])) { first = 1; second = 0; }
			for (int i = 2; i < _plates.Length; i++)
			{
				double score = UnitVector.Dot(point, _plates[i]);
				if (score > UnitVector.Dot(point, _plates[first])) { second = first; first = i; }
				else if (score > UnitVector.Dot(point, _plates[second])) second = i;
			}
			double best = UnitVector.Dot(point, _plates[first]), height = 0, total = 0;
			for (int i = 0; i < _plates.Length; i++) { double weight = Math.Exp((UnitVector.Dot(point, _plates[i]) - best) * 12); height += _heights[i] * weight; total += weight; }
			double convergence = Math.Max(0, UnitVector.Dot(_drifts[first] - _drifts[second], (_plates[second] - _plates[first]).Normalized()));
			double ridge = Math.Exp(-18 * (best - UnitVector.Dot(point, _plates[second]))) * convergence;
			double mountains = Clamp(ridge * .8 + Math.Max(0, Noise(point, 9, 251)) * .2);
			return new TerrainSample { Height = height / total + ridge * .24 + Fractal(point), Mountains = mountains };
		}
		private TerrainSample ContinuousTerrain(UnitVector point)
		{
			double d0 = UnitVector.Dot(point, _plates[0]), d1 = UnitVector.Dot(point, _plates[1]);
			double d2 = UnitVector.Dot(point, _plates[2]), d3 = UnitVector.Dot(point, _plates[3]);
			double d4 = UnitVector.Dot(point, _plates[4]), d5 = UnitVector.Dot(point, _plates[5]);
			double d6 = UnitVector.Dot(point, _plates[6]), d7 = UnitVector.Dot(point, _plates[7]);
			double best = Math.Max(Math.Max(Math.Max(d0, d1), Math.Max(d2, d3)), Math.Max(Math.Max(d4, d5), Math.Max(d6, d7)));
			double w0 = Math.Exp((d0 - best) * 12), w1 = Math.Exp((d1 - best) * 12);
			double w2 = Math.Exp((d2 - best) * 12), w3 = Math.Exp((d3 - best) * 12);
			double w4 = Math.Exp((d4 - best) * 12), w5 = Math.Exp((d5 - best) * 12);
			double w6 = Math.Exp((d6 - best) * 12), w7 = Math.Exp((d7 - best) * 12);
			double total = w0 + w1 + w2 + w3 + w4 + w5 + w6 + w7;
			double height = (_heights[0] * w0 + _heights[1] * w1 + _heights[2] * w2 + _heights[3] * w3
				+ _heights[4] * w4 + _heights[5] * w5 + _heights[6] * w6 + _heights[7] * w7) / total;
			// Explicit weights keep sampling allocation-free and safe for concurrent raster/geometry work.
			double ridge = 2 * (
				w0 * (w1 * _convergence[0] + w2 * _convergence[1] + w3 * _convergence[2] + w4 * _convergence[3]
					+ w5 * _convergence[4] + w6 * _convergence[5] + w7 * _convergence[6])
				+ w1 * (w2 * _convergence[7] + w3 * _convergence[8] + w4 * _convergence[9]
					+ w5 * _convergence[10] + w6 * _convergence[11] + w7 * _convergence[12])
				+ w2 * (w3 * _convergence[13] + w4 * _convergence[14] + w5 * _convergence[15]
					+ w6 * _convergence[16] + w7 * _convergence[17])
				+ w3 * (w4 * _convergence[18] + w5 * _convergence[19] + w6 * _convergence[20] + w7 * _convergence[21])
				+ w4 * (w5 * _convergence[22] + w6 * _convergence[23] + w7 * _convergence[24])
				+ w5 * (w6 * _convergence[25] + w7 * _convergence[26])
				+ w6 * w7 * _convergence[27]) / (total * total);
			return new TerrainSample { Height = height + ridge * .24 + Fractal(point), Mountains = Clamp(ridge * 1.15) };
		}
		internal double CalibrateSeaLevel(double landShare)
		{
			var heights = new double[8192];
			for (int i = 0; i < heights.Length; i++) heights[i] = Raw(CalibrationSite(i, heights.Length)).Height;
			Array.Sort(heights);
			return heights[(int)Math.Round((1 - landShare) * (heights.Length - 1))];
		}
		private static UnitVector CalibrationSite(int i, int count)
		{
			double y = 1 - 2 * (i + .5) / count;
			double angle = i * Math.PI * (3 - Math.Sqrt(5));
			double radius = Math.Sqrt(1 - y * y);
			return new UnitVector(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
		}
		public TerrainSample Sample(UnitVector point)
		{
			if (_planet.GeographyVersion == 0)
			{
				Province p = _planet.Provinces[_planet.Nearest(point)];
				return new TerrainSample { Height = p.Land ? Math.Max(.001, p.Height) : Math.Min(-.001, p.Height), Temperature = p.Temperature, Moisture = p.Moisture, Mountains = p.Mountains };
			}
			TerrainSample sample = Raw(point); sample.Height -= _planet.SeaLevel;
			// Local relief is shared by atlas and native maps, without changing older worlds.
			if (_planet.GeographyVersion >= 3 && _planet.GeographyVersion < 5 && sample.Height > 0)
				sample.Mountains = Math.Max(sample.Mountains, Clamp((Noise(point, 90, 1837) - .1) * 3.5));
			double latitude = Math.Abs(point.Y), elevation = Math.Max(0, sample.Height);
			sample.Temperature = _planet.GeographyVersion == 1
				? Clamp(1 - latitude * 1.2 - elevation * .6 + Noise(point, 3, 471) * .06)
				: Clamp(1 - latitude * latitude * .95 - elevation * .28 + Noise(point, 3, 471) * .06);
			double subtropics = Math.Exp(-Math.Pow((Math.Abs(point.Y) - .4) * 6, 2));
			sample.Moisture = Clamp(.7 + Noise(point, 4, 927) * .35 - subtropics * .35 - sample.Mountains * .15 - Math.Max(0, sample.Height) * .25);
			return sample;
		}
		internal float Elevation(TerrainSample sample) => sample.Height <= 0 ? .15f
			: sample.Mountains > .55 && sample.Height > (_planet.GeographyVersion >= 3 ? 0 : .18) ? .9f : .5f;
		private static UnitVector East(UnitVector point)
		{
			UnitVector east = UnitVector.Cross(new UnitVector(0, 1, 0), point);
			if (UnitVector.Dot(east, east) < 1e-20) east = UnitVector.Cross(new UnitVector(0, 0, 1), point);
			return east.Normalized();
		}
		public UnitVector LocalDirection(Province province, double u, double v)
		{
			UnitVector center = province.Direction;
			UnitVector east = East(center), north = UnitVector.Cross(center, east).Normalized();
			return (center + east * ((u * 2 - 1) * _spans[province.Id]) + north * ((v * 2 - 1) * _spans[province.Id])).Normalized();
		}
		internal UnitVector LocalTileDirection(int tileId, double u, double v) => TileFootprint(tileId).Direction(u, v);
		internal LocalTileFootprint TileFootprint(int tileId)
		{
            if (_planet.FormatVersion != 3 || _planet.LocalMapVersion < 1 || _planet.LocalMapVersion > 3 || tileId < 0 || tileId >= _planet.Mesh.Centers.Length)
				throw new ArgumentException("Unsupported local tile identity.");
			// New charts describe cell geometry independently of whether the cell can be played.
			UnitVector center = _planet.Mesh.Centers[tileId];
			if (_planet.LocalMapVersion == 3) return new LocalTileFootprint(_planet.Mesh, tileId, East(center));
			TileLand land = TileLand.For(_planet);
			if (!land.Cells[tileId].Eligible) throw new ArgumentException("The selected tile has no sampled dry land.");
			if (_planet.LocalMapVersion == 1) center = land.Cells[tileId].DryPoint;
			double span = Math.Sqrt(_planet.Mesh.Areas[tileId]) * (_planet.LocalMapVersion == 1 ? .45 : .55);
			return new LocalTileFootprint(center, East(center), span);
		}
		internal int NativeTileSeed(int tileId)
		{
            if (_planet.LocalMapVersion < 1 || _planet.LocalMapVersion > 3 || tileId < 0 || tileId >= _planet.Mesh.Centers.Length) throw new ArgumentException("Unsupported local tile identity.");
			unchecked { return (_planet.Seed * 397 ^ tileId) * 397 ^ _planet.LocalMapVersion; }
		}
	}
}
