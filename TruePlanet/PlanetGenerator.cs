using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TruePlanet
{
	public static class PlanetGenerator
	{
		public static Planet Generate(int seed, PlanetOptions options)
		{
			options.Validate();
			var copy = new PlanetOptions { Provinces = options.Provinces, LandShare = options.LandShare, Nations = options.Nations, WildShare = options.WildShare, LargestProvince = options.LargestProvince };
			string identity = seed.ToString("X8") + "-" + copy.Provinces + "-" + copy.Nations + "-" + (int)copy.LargestProvince
				+ "-" + BitConverter.DoubleToInt64Bits(copy.LandShare).ToString("X16") + "-" + BitConverter.DoubleToInt64Bits(copy.WildShare).ToString("X16");
			var planet = new Planet { Id = "planet-" + identity, Name = "World " + seed.ToString(CultureInfo.InvariantCulture), Seed = seed, Options = copy };
			var random = new Random(seed);
			SphereGraph.Build(planet, random);
			Geography(planet, random);
			Countries(planet, random);
			Roads(planet);
			planet.Validate();
			return planet;
		}
		private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
		private static UnitVector Direction(Random random) => UnitVector.FromMap(random.NextDouble() * Math.PI * 2, Math.Asin(random.NextDouble() * 2 - 1));
		private static void Geography(Planet planet, Random random)
		{
			const int plates = 8;
			var centers = new UnitVector[plates]; var drifts = new UnitVector[plates]; var baseHeight = new double[plates];
			for (int i = 0; i < plates; i++)
			{
				centers[i] = Direction(random); drifts[i] = UnitVector.Cross(centers[i], Direction(random)).Normalized();
				baseHeight[i] = random.NextDouble() < .45 ? .5 + random.NextDouble() * .3 : -.5 + random.NextDouble() * .3;
			}
			var waves = new UnitVector[4]; var phases = new double[4];
			for (int i = 0; i < waves.Length; i++) { waves[i] = Direction(random); phases[i] = random.NextDouble() * Math.PI * 2; }
			double Noise(UnitVector point)
			{
				double result = 0;
				for (int i = 0; i < waves.Length; i++) result += Math.Sin(UnitVector.Dot(point, waves[i]) * (4 + i * 3) + phases[i]) / (i + 1);
				return result * .13;
			}
			foreach (Province p in planet.Provinces)
			{
				int first = 0, second = 1;
				if (UnitVector.Dot(p.Direction, centers[second]) > UnitVector.Dot(p.Direction, centers[first])) { first = 1; second = 0; }
				for (int i = 2; i < plates; i++)
				{
					double score = UnitVector.Dot(p.Direction, centers[i]);
					if (score > UnitVector.Dot(p.Direction, centers[first])) { second = first; first = i; }
					else if (score > UnitVector.Dot(p.Direction, centers[second])) second = i;
				}
				double edge = Math.Exp(-12 * (UnitVector.Dot(p.Direction, centers[first]) - UnitVector.Dot(p.Direction, centers[second])));
				double convergence = Math.Max(0, UnitVector.Dot(drifts[first] - drifts[second], (centers[second] - centers[first]).Normalized()));
				p.Mountains = Clamp(edge * convergence * .6);
				p.Height = baseHeight[first] + p.Mountains * .8 + Noise(p.Direction);
			}
			int landCount = (int)Math.Round(planet.Provinces.Length * planet.Options.LandShare);
			Province[] ranked = planet.Provinces.OrderByDescending(p => p.Height).ThenBy(p => p.Id).ToArray();
			double coast = (ranked[landCount - 1].Height + ranked[landCount].Height) * .5;
			for (int i = 0; i < ranked.Length; i++) { ranked[i].Land = i < landCount; ranked[i].Height -= coast; }
			const int samples = 8192;
			for (int i = 0; i < samples; i++) planet.Provinces[planet.Nearest(SphereGraph.Site(i, samples, 0))].Area += Math.PI * 4 / samples;
			var coastDistance = new int[planet.Provinces.Length]; var queue = new Queue<int>();
			foreach (Province p in planet.Provinces) { coastDistance[p.Id] = p.Land ? int.MaxValue : 0; if (!p.Land) queue.Enqueue(p.Id); }
			while (queue.Count != 0)
			{
				int id = queue.Dequeue();
				foreach (int n in planet.Provinces[id].Neighbours)
					if (coastDistance[n] > coastDistance[id] + 1) { coastDistance[n] = coastDistance[id] + 1; queue.Enqueue(n); }
			}
			double[] areas = planet.Provinces.Where(p => p.Land).Select(p => p.Area).OrderBy(a => a).ToArray();
			foreach (Province p in planet.Provinces)
			{
				p.Temperature = Clamp(1 - Math.Abs(p.Direction.Y) * 1.15 - Math.Max(0, p.Height) * .35);
				double wind = .1 * Math.Sin(Math.Atan2(p.Direction.Z, p.Direction.X) + p.Direction.Y * 5);
				p.Moisture = Clamp(.95 - coastDistance[p.Id] * .16 - p.Mountains * .3 + wind + Noise(p.Direction));
				double snow = Clamp((.35 - p.Temperature) / .3), desert = Clamp((.4 - p.Moisture) * 2) * (1 - snow);
				double forest = Clamp((p.Moisture - .4) * 1.5) * (1 - snow) * (1 - desert), grass = Math.Max(.05, 1 - snow - desert - forest);
				double total = snow + desert + forest + grass;
				p.Biomes = new[] { grass / total, forest / total, desert / total, snow / total };
				p.Water = p.Land ? Clamp(.03 + p.Moisture * .1 + (coastDistance[p.Id] == 1 ? .12 : 0)) : 1;
				ProvinceSize size = p.Area >= areas[areas.Length * 2 / 3] ? ProvinceSize.Huge : p.Area >= areas[areas.Length / 3] ? ProvinceSize.ExtraLarge : ProvinceSize.Large;
				p.Size = (ProvinceSize)Math.Min((int)size, (int)planet.Options.LargestProvince);
			}
		}
		private static double TravelCost(Province a, Province b) => Math.Acos(Math.Max(-1, Math.Min(1, UnitVector.Dot(a.Direction, b.Direction))))
			* (1 + (a.Mountains + b.Mountains) * 4 + (a.Biomes[2] + b.Biomes[2]));
		private static int Next(double[] distance, bool[] finished)
		{
			int best = -1; double score = double.PositiveInfinity;
			for (int i = 0; i < distance.Length; i++) if (!finished[i] && distance[i] < score) { score = distance[i]; best = i; }
			return best;
		}
		private static void Countries(Planet planet, Random random)
		{
			Province[] land = planet.Provinces.Where(p => p.Land).ToArray();
			var shuffled = land.OrderBy(p => random.NextDouble()).ToArray();
			int wildCount = Math.Min(land.Length - 2, (int)Math.Round(land.Length * planet.Options.WildShare));
			var wild = new bool[planet.Provinces.Length];
			for (int i = 0; i < wildCount; i++) wild[shuffled[i].Id] = true;
			int count = Math.Min(planet.Options.Nations, land.Length - wildCount);
			planet.Nations = new Nation[count];
			var capitals = new HashSet<int>();
			for (int i = 0; i < count; i++)
			{
				Province capital = land.Where(p => !wild[p.Id] && !capitals.Contains(p.Id)).OrderByDescending(p =>
					(capitals.Count == 0 ? 1 : capitals.Min(c => 1 - UnitVector.Dot(p.Direction, planet.Provinces[c].Direction)))
					* (.6 + p.Moisture) * (1 - p.Mountains * .5)).ThenBy(p => p.Id).First();
				capitals.Add(capital.Id); capital.Nation = i;
				planet.Nations[i] = new Nation { Id = i, Capital = capital.Id, Name = "Nation " + (i + 1), Elven = i % 2 != 0,
					Hue = (i * .618033988749895 + .12) % 1, Emblem = "sigil-" + (i % 8 + 1) };
			}
			var distances = Enumerable.Repeat(double.PositiveInfinity, planet.Provinces.Length).ToArray(); var done = new bool[distances.Length];
			foreach (int c in capitals) distances[c] = 0;
			int next;
			while ((next = Next(distances, done)) >= 0)
			{
				done[next] = true; Province p = planet.Provinces[next];
				foreach (int n in p.Neighbours)
				{
					Province candidate = planet.Provinces[n];
					if (!candidate.Land || wild[n] || done[n]) continue;
					double cost = distances[next] + TravelCost(p, candidate);
					if (cost < distances[n]) { distances[n] = cost; candidate.Nation = p.Nation; }
				}
			}
			foreach (Province p in land)
			{
				if (wild[p.Id]) continue;
				int villages = Math.Max(1, Math.Min(4, (int)p.Size + 1 + (capitals.Contains(p.Id) ? 1 : 0)));
				p.Villages = new Village[villages];
				int neighbourNation = -1;
				foreach (int neighbour in p.Neighbours)
				{
					int owner = planet.Provinces[neighbour].Nation;
					if (owner >= 0 && owner != p.Nation) { neighbourNation = owner; break; }
				}
				for (int v = 0; v < villages; v++)
				{
					int nation = v == villages - 1 && villages > 1 && neighbourNation >= 0 ? neighbourNation : p.Nation;
					p.Villages[v] = new Village { Name = "Settlement " + (p.Id + 1) + "." + (v + 1), Nation = nation,
						Elven = nation >= 0 ? planet.Nations[nation].Elven : random.NextDouble() < .5, Capital = v == 0 && capitals.Contains(p.Id) };
				}
			}
		}
		private static void Roads(Planet planet)
		{
			int count = planet.Provinces.Length;
			var terminals = new HashSet<int>(planet.Provinces.Where(p => p.Villages.Length > 0).Select(p => p.Id));
			var roads = new List<Road>(); var connected = new HashSet<int>();
			var distances = new double[count]; var previous = new int[count]; var done = new bool[count];
			while (terminals.Count > 0)
			{
				if (connected.Count == 0) { int first = terminals.Min(); connected.Add(first); terminals.Remove(first); }
				for (int i = 0; i < count; i++) { distances[i] = connected.Contains(i) ? 0 : double.PositiveInfinity; previous[i] = -1; done[i] = false; }
				int next, target = -1;
				while ((next = Next(distances, done)) >= 0)
				{
					if (terminals.Contains(next)) { target = next; break; }
					done[next] = true; Province p = planet.Provinces[next];
					foreach (int n in p.Neighbours)
					{
						if (done[n] || !planet.Provinces[n].Land) continue;
						double cost = distances[next] + TravelCost(p, planet.Provinces[n]);
						if (cost < distances[n]) { distances[n] = cost; previous[n] = next; }
					}
				}
				if (target < 0) { connected.Clear(); continue; }
				var route = new List<int>();
				for (int cursor = target; cursor >= 0; cursor = previous[cursor]) { route.Add(cursor); connected.Add(cursor); terminals.Remove(cursor); }
				route.Reverse(); roads.Add(new Road { Path = route.ToArray() });
			}
			planet.Roads = roads.ToArray();
		}
	}
}
