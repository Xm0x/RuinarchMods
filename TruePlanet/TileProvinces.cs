using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TruePlanet
{
	internal static class TileProvinces
	{
		internal static void Prepare(Planet planet)
		{
			if (planet.Members == null)
			{
				var members = new List<int>[planet.Provinces.Length]; for (int i = 0; i < members.Length; i++) members[i] = new List<int>();
				for (int i = 0; i < planet.TileOwners.Length; i++)
				{
					int owner = planet.TileOwners[i];
					if (owner == -1 && planet.FormatVersion == 3) continue;
					if (owner < 0 || owner >= members.Length) throw new InvalidDataException("Invalid tile ownership.");
					members[owner].Add(i);
				}
				planet.Members = members.Select(m=>m.ToArray()).ToArray();
			}
			if (planet.TileTerrain == null)
			{
				var geography = new PlanetGeography(planet); planet.TileTerrain = new TerrainSample[planet.Mesh.Centers.Length];
				TileLand land = planet.FormatVersion == 3 ? TileLand.For(planet) : null;
				for (int i = 0; i < planet.TileTerrain.Length; i++)
					planet.TileTerrain[i] = geography.Sample(land != null && land.Cells[i].Eligible ? land.Cells[i].DryPoint : planet.Mesh.Centers[i]);
			}
		}
		internal static void Validate(Planet planet)
		{
			TileMesh mesh = planet.Mesh;
			if (planet.TileOwners == null || planet.TileOwners.Length != mesh.Centers.Length || planet.TileOwners.Any(p=>p<0||p>=planet.Provinces.Length)) throw new InvalidDataException("Invalid tile ownership.");
			var seen = new bool[mesh.Centers.Length]; var counts = new int[planet.Provinces.Length]; var queue = new Queue<int>();
			for (int tile = 0; tile < seen.Length; tile++) if (!seen[tile])
			{
				int owner = planet.TileOwners[tile]; if (++counts[owner] != 1) throw new InvalidDataException("A province has disconnected tiles."); seen[tile] = true; queue.Enqueue(tile);
				while (queue.Count > 0) foreach (int n in mesh.Neighbours[queue.Dequeue()]) if (!seen[n] && planet.TileOwners[n] == owner) { seen[n] = true; queue.Enqueue(n); }
			}
			if (counts.Any(c=>c != 1)) throw new InvalidDataException("A province has no tiles.");
			var borders = new HashSet<int>[counts.Length]; for (int i = 0; i < borders.Length; i++) borders[i] = new HashSet<int>();
			for (int tile = 0; tile < seen.Length; tile++) foreach (int n in mesh.Neighbours[tile]) if (planet.TileOwners[n] != planet.TileOwners[tile]) borders[planet.TileOwners[tile]].Add(planet.TileOwners[n]);
			for (int i = 0; i < borders.Length; i++) if (planet.Provinces[i] == null || planet.Provinces[i].Neighbours == null || !borders[i].SetEquals(planet.Provinces[i].Neighbours)) throw new InvalidDataException("Province borders disagree with tile ownership.");
		}
		internal static void ValidateLand(Planet planet)
		{
			Prepare(planet); TileMesh mesh = planet.Mesh; var links = new HashSet<int>[planet.Provinces.Length];
			var geography = new PlanetGeography(planet);
			for (int i = 0; i < links.Length; i++) links[i] = new HashSet<int>();
			for (int tile = 0; tile < mesh.Centers.Length; tile++) if (planet.TileTerrain[tile].Height > 0)
				foreach (int n in mesh.Neighbours[tile])
					if (planet.TileTerrain[n].Height > 0 && planet.TileOwners[tile] != planet.TileOwners[n]) links[planet.TileOwners[tile]].Add(planet.TileOwners[n]);
			foreach (Province p in planet.Provinces)
			{
				if (p.LandNeighbours == null || p.LandNeighbours.Length != links[p.Id].Count || !links[p.Id].SetEquals(p.LandNeighbours)
					|| planet.Nearest(p.Direction) != p.Id) throw new InvalidDataException("Province land geography disagrees with tiles.");
				foreach (Village v in p.Villages)
					if (geography.Sample(v.Position).Height <= 0) throw new InvalidDataException("A settlement lies in water.");
			}
		}
	}
}
