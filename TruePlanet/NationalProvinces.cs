using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TruePlanet
{
	internal static class NationalProvinces
	{
		internal static void Build(Planet planet, Random random, TileLand land)
		{
			TileMesh mesh = planet.Mesh; int count = mesh.Centers.Length;
			var geography = new PlanetGeography(planet);
			planet.TileTerrain = new TerrainSample[count];
			var eligible = new List<int>();
			for (int tile = 0; tile < count; tile++)
			{
				planet.TileTerrain[tile] = geography.Sample(land.Cells[tile].Eligible ? land.Cells[tile].DryPoint : mesh.Centers[tile]);
				if (land.Cells[tile].Eligible) eligible.Add(tile);
			}
			if (eligible.Count == 0) throw new InvalidDataException("The world has no sampled dry land.");
			int nations = Math.Min(planet.Options.Nations, eligible.Count);
			var settlementTerrain = new SettlementTerrain(planet, geography);
			var capitals = new int[nations]; var owners = Enumerable.Repeat(-1, count).ToArray();
			var separation = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
			var rates = new double[nations]; double claimed = 0;
			planet.Nations = new Nation[nations];
			for (int owner = 0; owner < nations; owner++)
			{
				int capital = -1; double best = double.NegativeInfinity; bool elven = owner % 2 != 0;
				int first = owner == 0 ? random.Next(eligible.Count) : 0;
				for (int i = 0; i < eligible.Count; i++)
				{
					int tile = eligible[(first + i) % eligible.Count];
					if (owners[tile] >= 0) continue;
					TerrainSample sample = planet.TileTerrain[tile]; double score = owner == 0 ? 1 : separation[tile] * (.6 + sample.Moisture) * (1 - sample.Mountains * .5);
					if (score <= best || !settlementTerrain.Suitable(tile, elven)) continue;
					best = score; capital = tile;
					if (owner == 0) break;
				}
				if (capital < 0) throw new InvalidDataException("No native settlement terrain is available for nation " + (owner + 1) + ".");
				capitals[owner] = capital; owners[capital] = owner; claimed += land.Cells[capital].LandArea; rates[owner] = .75 + random.NextDouble() * .5;
				planet.Nations[owner] = new Nation { Id = owner, Name = "Nation " + (owner + 1), Capital = -1, Elven = owner % 2 != 0,
					Hue = (owner * .618033988749895 + .12) % 1, Emblem = "sigil-" + (owner % 8 + 1) };
				foreach (int tile in eligible) separation[tile] = Math.Min(separation[tile], 1 - UnitVector.Dot(mesh.Centers[tile], mesh.Centers[capital]));
			}
			var queue = new TileQueue();
			foreach (int capital in capitals) foreach (int next in land.Links[capital]) if (owners[next] < 0) queue.Push(next, owners[capital], Cost(planet, capital, next) * rates[owners[capital]]);
			double target = land.Cells.Sum(cell => cell.LandArea) * (1 - planet.Options.WildShare);
			while (queue.Count != 0 && claimed < target)
			{
				TileQueue.Entry entry = queue.Pop(); if (owners[entry.Tile] >= 0) continue;
				owners[entry.Tile] = entry.Owner; claimed += land.Cells[entry.Tile].LandArea;
				foreach (int next in land.Links[entry.Tile]) if (owners[next] < 0) queue.Push(next, entry.Owner, entry.Cost + Cost(planet, entry.Tile, next) * rates[entry.Owner]);
			}
			RepairPockets(planet, land, owners);
			Subdivide(planet, land, owners, capitals, random, settlementTerrain);
			TileRoads.Build(planet);
			Validate(planet, land);
		}
		private static double Cost(Planet planet, int a, int b)
		{
			UnitVector delta = planet.Mesh.Centers[a] - planet.Mesh.Centers[b]; TerrainSample x = planet.TileTerrain[a], y = planet.TileTerrain[b];
			uint variation; unchecked { variation = (uint)(planet.Seed ^ Math.Min(a,b) * 374761393 ^ Math.Max(a,b) * 668265263); variation = (variation ^ (variation >> 13)) * 1274126177; }
			return Math.Sqrt(UnitVector.Dot(delta,delta)) * (1 + (x.Mountains + y.Mountains) * 3 + (x.Biome == 2 || y.Biome == 2 ? 1 : 0)) * (.7 + (variation & 65535) / 65535.0 * .6);
		}
		internal static void RepairPockets(Planet planet, TileLand land, int[] owners)
		{
			var seen = new bool[owners.Length]; var queue = new Queue<int>(); var frontier = new TileQueue();
			for (int start = 0; start < owners.Length; start++)
			{
				if (seen[start] || owners[start] >= 0 || !land.Cells[start].Eligible) continue;
				var component = new List<int>(); bool open = false; var adjacent = new HashSet<int>();
				seen[start] = true; queue.Enqueue(start);
				while (queue.Count != 0)
				{
					int tile = queue.Dequeue(); component.Add(tile); open |= land.Cells[tile].Coastal;
					foreach (int next in land.Links[tile])
					{
						if (owners[next] >= 0) adjacent.Add(owners[next]);
						else if (!seen[next]) { seen[next] = true; queue.Enqueue(next); }
					}
				}
				if (open || adjacent.Count == 0) continue;
				if (adjacent.Count == 1) { int owner = adjacent.First(); foreach (int tile in component) owners[tile] = owner; continue; }
				frontier.Clear();
				foreach (int tile in component) foreach (int next in land.Links[tile]) if (owners[next] >= 0) frontier.Push(tile, owners[next], Cost(planet, tile, next));
				while (frontier.Count != 0)
				{
					TileQueue.Entry entry = frontier.Pop(); if (owners[entry.Tile] >= 0) continue;
					owners[entry.Tile] = entry.Owner;
					foreach (int next in land.Links[entry.Tile]) if (owners[next] < 0) frontier.Push(next, entry.Owner, entry.Cost + Cost(planet, entry.Tile, next));
				}
			}
		}
		private static int[] Allocate(int target, double[] areas, int[] capacity)
		{
			var allocation = Enumerable.Repeat(1, areas.Length).ToArray();
			target = Math.Min(Math.Max(target, areas.Length), capacity.Sum());
			double total = areas.Sum();
			while (allocation.Sum() < target)
			{
				int best = -1; double deficit = double.NegativeInfinity;
				for (int i = 0; i < areas.Length; i++)
				{
					if (allocation[i] == capacity[i]) continue;
					double score = areas[i] / total * target - allocation[i];
					if (score > deficit) { deficit = score; best = i; }
				}
				if (best < 0) throw new InvalidDataException("National province capacities are inconsistent.");
				allocation[best]++;
			}
			return allocation;
		}
		private static void Subdivide(Planet planet, TileLand land, int[] nations, int[] capitals, Random random, SettlementTerrain settlementTerrain)
		{
			int count = nations.Length; var territory = new List<int>[capitals.Length]; var areas = new double[capitals.Length];
			for (int i = 0; i < territory.Length; i++) territory[i] = new List<int>();
			for (int tile = 0; tile < count; tile++) if (nations[tile] >= 0) { territory[nations[tile]].Add(tile); areas[nations[tile]] += land.Cells[tile].LandArea; }
			int[] allocation = Allocate(planet.Options.Provinces, areas, territory.Select(tiles => tiles.Count).ToArray());
			planet.TileOwners = Enumerable.Repeat(-1, count).ToArray(); planet.Members = null;
			var provinces = new List<Province>(); var queue = new TileQueue(); var rates = new List<double>();
			for (int nation = 0; nation < capitals.Length; nation++)
			{
				var seeds = new List<int>(); var separation = new Dictionary<int, double>();
				foreach (int tile in territory[nation]) separation.Add(tile, double.PositiveInfinity);
				for (int i = 0; i < allocation[nation]; i++)
				{
					int seed = i == 0 ? capitals[nation] : -1; double best = double.NegativeInfinity;
					if (i != 0) foreach (int tile in territory[nation]) if (planet.TileOwners[tile] < 0 && separation[tile] > best) { best = separation[tile]; seed = tile; }
					int id = provinces.Count; seeds.Add(seed); planet.TileOwners[seed] = id;
					provinces.Add(new Province { Id = id, Name = "Province " + (id + 1), Nation = nation, Land = true, Size = ProvinceSize.Large });
					rates.Add(.65 + random.NextDouble() * .9);
					if (i == 0) planet.Nations[nation].Capital = id;
					foreach (int tile in territory[nation]) separation[tile] = Math.Min(separation[tile], 1 - UnitVector.Dot(planet.Mesh.Centers[tile], planet.Mesh.Centers[seed]));
				}
				foreach (int seed in seeds) foreach (int next in land.Links[seed]) if (nations[next] == nation && planet.TileOwners[next] < 0) queue.Push(next, planet.TileOwners[seed], Cost(planet,seed,next) * rates[planet.TileOwners[seed]]);
			}
			while (queue.Count != 0)
			{
				TileQueue.Entry entry = queue.Pop(); if (planet.TileOwners[entry.Tile] >= 0) continue;
				planet.TileOwners[entry.Tile] = entry.Owner; int nation = provinces[entry.Owner].Nation;
				foreach (int next in land.Links[entry.Tile]) if (nations[next] == nation && planet.TileOwners[next] < 0) queue.Push(next, entry.Owner, entry.Cost + Cost(planet,entry.Tile,next) * rates[entry.Owner]);
			}
			planet.Provinces = provinces.ToArray(); TileProvinces.Prepare(planet);
			var borders = new HashSet<int>[provinces.Count]; var dryBorders = new HashSet<int>[provinces.Count];
			for (int i = 0; i < borders.Length; i++) { borders[i] = new HashSet<int>(); dryBorders[i] = new HashSet<int>(); }
			for (int tile = 0; tile < count; tile++)
			{
				int a = planet.TileOwners[tile]; if (a < 0) continue;
				foreach (int next in planet.Mesh.Neighbours[tile]) { int b = planet.TileOwners[next]; if (b >= 0 && a != b) borders[a].Add(b); }
				foreach (int next in land.Links[tile]) { int b = planet.TileOwners[next]; if (b >= 0 && a != b) dryBorders[a].Add(b); }
			}
			foreach (Province province in provinces)
			{
				province.Neighbours = borders[province.Id].OrderBy(id => id).ToArray(); province.LandNeighbours = dryBorders[province.Id].OrderBy(id => id).ToArray();
				province.Biomes = new double[4]; UnitVector mean = default; double total = 0;
				foreach (int tile in planet.Members[province.Id])
				{
					double area = land.Cells[tile].LandArea; TerrainSample sample = planet.TileTerrain[tile]; total += area;
					province.Area += planet.Mesh.Areas[tile]; mean += land.Cells[tile].DryPoint * area; province.Height += sample.Height * area;
					province.Temperature += sample.Temperature * area; province.Moisture += sample.Moisture * area; province.Mountains += sample.Mountains * area; province.Biomes[sample.Biome] += area;
				}
				province.Height /= total; province.Temperature /= total; province.Moisture /= total; province.Mountains /= total; province.Water = Math.Max(0, 1 - total / province.Area);
				for (int i = 0; i < 4; i++) province.Biomes[i] /= total;
				mean = mean.Normalized(); int representative = planet.Members[province.Id].OrderByDescending(tile => UnitVector.Dot(mean, land.Cells[tile].DryPoint)).ThenBy(tile => tile).First();
				province.Direction = land.Cells[representative].DryPoint;
				bool isCapital = planet.Nations[province.Nation].Capital == province.Id, elven = planet.Nations[province.Nation].Elven;
				int settlement = isCapital ? capitals[province.Nation] : -1;
				if (!isCapital) foreach (int tile in planet.Members[province.Id].OrderByDescending(tile => UnitVector.Dot(mean, land.Cells[tile].DryPoint)).ThenBy(tile => tile))
					if (settlementTerrain.Suitable(tile, elven)) { settlement = tile; break; }
				province.Villages = settlement < 0 ? Array.Empty<Village>() : new[] { new Village { Name = "Settlement " + (province.Id + 1), Nation = province.Nation, Elven = elven,
					Capital = isCapital, TileId = settlement, Position = land.Cells[settlement].DryPoint } };
			}
		}
		internal static void Validate(Planet planet, TileLand land)
		{
			TileMesh mesh = planet.Mesh;
			var geography = new PlanetGeography(planet);
			if (planet.TileOwners == null || planet.TileOwners.Length != mesh.Centers.Length || planet.TileOwners.Any(id => id < -1 || id >= planet.Provinces.Length)) throw new InvalidDataException("Invalid geographic tile ownership.");
			var reached = new bool[mesh.Centers.Length]; var counts = new int[planet.Provinces.Length]; var queue = new Queue<int>();
			for (int tile = 0; tile < reached.Length; tile++)
			{
				int owner = planet.TileOwners[tile]; if (owner < 0) continue;
				if (!land.Cells[tile].Eligible) throw new InvalidDataException("A political province claims ocean.");
				if (reached[tile]) continue;
				if (++counts[owner] != 1) throw new InvalidDataException("A political province has disconnected dry cells.");
				reached[tile] = true; queue.Enqueue(tile);
				while (queue.Count != 0) foreach (int next in land.Links[queue.Dequeue()]) if (!reached[next] && planet.TileOwners[next] == owner) { reached[next] = true; queue.Enqueue(next); }
			}
			if (counts.Any(value => value != 1)) throw new InvalidDataException("A political province has no dry cells.");
			var borders = new HashSet<int>[counts.Length]; var dry = new HashSet<int>[counts.Length];
			for (int i = 0; i < borders.Length; i++) { borders[i] = new HashSet<int>(); dry[i] = new HashSet<int>(); }
			for (int tile = 0; tile < reached.Length; tile++)
			{
				int a = planet.TileOwners[tile]; if (a < 0) continue;
				foreach (int next in mesh.Neighbours[tile]) { int b = planet.TileOwners[next]; if (b >= 0 && a != b) borders[a].Add(b); }
				foreach (int next in land.Links[tile]) { int b = planet.TileOwners[next]; if (b >= 0 && a != b) dry[a].Add(b); }
			}
			for (int id = 0; id < borders.Length; id++)
			{
				Province province = planet.Provinces[id];
				if (province == null || province.Id != id || !province.Land || province.Nation < 0 || province.Nation >= planet.Nations.Length
					|| province.Neighbours == null || province.LandNeighbours == null || province.LandNeighbours.Length != dry[id].Count
					|| !borders[id].SetEquals(province.Neighbours) || !dry[id].SetEquals(province.LandNeighbours)) throw new InvalidDataException("Political borders disagree with geographic ownership.");
				foreach (Village village in province.Villages)
					if (village.TileId < 0 || village.TileId >= mesh.Centers.Length || planet.TileOwners[village.TileId] != id || mesh.Locate(village.Position) != village.TileId || geography.Sample(village.Position).Height <= 0)
						throw new InvalidDataException("A settlement is not anchored to dry ground in its province.");
			}
		}
	}
}
