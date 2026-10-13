using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TruePlanet
{
	internal static class TileRoads
	{
		private struct Terminal { internal int Province, Village, Tile; }
		private struct Link { internal int A, B; internal double Cost; }
		internal static void Build(Planet planet)
		{
			TileProvinces.Prepare(planet); TileMesh mesh = planet.Mesh; int count = mesh.Centers.Length;
			TileLand land = planet.FormatVersion == 3 ? TileLand.For(planet) : null;
			int[] Neighbours(int tile) => land != null ? land.Links[tile] : mesh.Neighbours[tile];
			var terminals = new List<Terminal>(); foreach (Province p in planet.Provinces) for (int v = 0; v < p.Villages.Length; v++) terminals.Add(new Terminal { Province = p.Id, Village = v, Tile = mesh.Locate(p.Villages[v].Position) });
			var distances = Enumerable.Repeat(double.PositiveInfinity,count).ToArray(); var previous = Enumerable.Repeat(-1,count).ToArray(); var source = Enumerable.Repeat(-1,count).ToArray();
			var parent = Enumerable.Range(0,terminals.Count).ToArray(); int Root(int id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
			var roads = new List<Road>(); var queue = new TileQueue();
			Road Make(int a,int b,int[] tiles) { Terminal x = terminals[a], y = terminals[b]; return new Road { Tiles = tiles, StartProvince = x.Province, StartVillage = x.Village, EndProvince = y.Province, EndVillage = y.Village }; }
			for (int i = 0; i < terminals.Count; i++)
			{
				int tile = terminals[i].Tile; if (source[tile] >= 0) { roads.Add(Make(source[tile],i,new[]{tile})); parent[Root(i)] = Root(source[tile]); continue; }
				distances[tile] = 0; source[tile] = i; queue.Push(tile,i,0);
			}
			double Cost(int a,int b) { UnitVector delta = mesh.Centers[a]-mesh.Centers[b]; TerrainSample x = planet.TileTerrain[a], y = planet.TileTerrain[b]; return Math.Sqrt(UnitVector.Dot(delta,delta)) * (1+(x.Mountains+y.Mountains)*4+(x.Biome==2||y.Biome==2?1:0)); }
			while (queue.Count > 0)
			{
				TileQueue.Entry next = queue.Pop(); if (next.Cost != distances[next.Tile] || next.Owner != source[next.Tile]) continue;
				foreach (int n in Neighbours(next.Tile))
				{
					if (planet.TileTerrain[n].Height <= 0) continue; double cost = next.Cost+Cost(next.Tile,n);
					if (cost < distances[n]) { distances[n] = cost; previous[n] = next.Tile; source[n] = next.Owner; queue.Push(n,next.Owner,cost); }
				}
			}
			var links = new List<Link>();
			for (int tile = 0; tile < count; tile++) if (source[tile] >= 0) foreach (int n in Neighbours(tile)) if (n > tile && source[n] >= 0 && source[n] != source[tile]) links.Add(new Link { A = tile, B = n, Cost = distances[tile]+Cost(tile,n)+distances[n] });
			links.Sort((a,b)=> { int c = a.Cost.CompareTo(b.Cost); if (c != 0) return c; c = a.A.CompareTo(b.A); return c != 0 ? c : a.B.CompareTo(b.B); });
			foreach (Link link in links)
			{
				int a = source[link.A], b = source[link.B]; if (Root(a) == Root(b)) continue; parent[Root(a)] = Root(b);
				var path = new List<int>(); for (int tile = link.A; tile >= 0; tile = previous[tile]) path.Add(tile); path.Reverse(); for (int tile = link.B; tile >= 0; tile = previous[tile]) path.Add(tile);
				roads.Add(Make(a,b,path.ToArray()));
			}
			planet.Roads = roads.ToArray();
		}
		internal static void Validate(Planet planet,Road road)
		{
			if (road == null || road.Tiles == null || road.Tiles.Length == 0 || road.StartProvince < 0 || road.StartProvince >= planet.Provinces.Length || road.EndProvince < 0 || road.EndProvince >= planet.Provinces.Length) throw new InvalidDataException("Invalid tile road.");
			Province a = planet.Provinces[road.StartProvince], b = planet.Provinces[road.EndProvince];
			if (road.StartVillage < 0 || road.StartVillage >= a.Villages.Length || road.EndVillage < 0 || road.EndVillage >= b.Villages.Length) throw new InvalidDataException("Invalid road settlement.");
			TileProvinces.Prepare(planet); TileMesh mesh = planet.Mesh;
			if (road.Tiles.Any(t=>t<0||t>=mesh.Centers.Length||planet.TileTerrain[t].Height<=0) || road.Tiles[0] != mesh.Locate(a.Villages[road.StartVillage].Position) || road.Tiles[road.Tiles.Length-1] != mesh.Locate(b.Villages[road.EndVillage].Position)) throw new InvalidDataException("A road leaves land or its settlement.");
			TileLand land = planet.FormatVersion == 3 ? TileLand.For(planet) : null;
			for (int i = 1; i < road.Tiles.Length; i++)
				if (!(land != null ? land.Links[road.Tiles[i - 1]] : mesh.Neighbours[road.Tiles[i - 1]]).Contains(road.Tiles[i]))
					throw new InvalidDataException("A road crosses disconnected land cells.");
		}
	}
}
