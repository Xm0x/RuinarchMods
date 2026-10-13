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
		internal LocalTileFootprint(UnitVector center, UnitVector east, double span)
		{
			_center = center; _east = east; _north = UnitVector.Cross(center, east).Normalized(); _span = span;
		}
		internal UnitVector Direction(double u, double v) =>
			(_center + _east * ((u * 2 - 1) * _span) + _north * ((v * 2 - 1) * _span)).Normalized();
	}

	// Coordinates and terrain are independent of the flat atlas and Unity's native map.
	public sealed class PlanetGeography
	{
		private readonly Planet _planet;
		private readonly UnitVector[] _plates = new UnitVector[8], _drifts = new UnitVector[8];
		private readonly double[] _heights = new double[8], _spans;
		public PlanetGeography(Planet planet)
		{
			_planet = planet; var random = new Random(planet.Seed ^ 0x43e51c7b);
			for (int i = 0; i < _plates.Length; i++)
			{
				_plates[i] = Direction(random); _drifts[i] = UnitVector.Cross(_plates[i], Direction(random)).Normalized();
				_heights[i] = random.NextDouble() < .4 ? .45 + random.NextDouble() * .25 : -.45 + random.NextDouble() * .2;
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
			if (_planet.GeographyVersion >= 3 && sample.Height > 0)
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
			if (_planet.FormatVersion != 3 || _planet.LocalMapVersion != 1 || tileId < 0 || tileId >= _planet.Mesh.Centers.Length)
				throw new ArgumentException("Unsupported local tile identity.");
			TileLand land = TileLand.For(_planet);
			if (!land.Cells[tileId].Eligible) throw new ArgumentException("The selected tile has no sampled dry land.");
			UnitVector center = land.Cells[tileId].DryPoint;
			return new LocalTileFootprint(center, East(center), Math.Sqrt(_planet.Mesh.Areas[tileId]) * .45);
		}
		internal int NativeTileSeed(int tileId)
		{
			if (_planet.LocalMapVersion != 1 || tileId < 0 || tileId >= _planet.Mesh.Centers.Length) throw new ArgumentException("Unsupported local tile identity.");
			unchecked { return (_planet.Seed * 397 ^ tileId) * 397 ^ _planet.LocalMapVersion; }
		}
	}
}
