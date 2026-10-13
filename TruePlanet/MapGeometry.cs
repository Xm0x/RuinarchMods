using System;
using System.Collections.Generic;

namespace TruePlanet
{
	internal struct MapPoint
	{
		internal double X, Y;
		internal MapPoint(double x, double y) { X = x; Y = y; }
	}
	internal struct MapSegment
	{
		internal MapPoint A, B;
		internal int Left, Right;
		internal byte Kind; // Fine grid, province, nation, road.
	}
	internal struct SphericalSegment
	{
		internal UnitVector A, B;
		internal int Left, Right;
		internal byte Kind;
	}
	// Reconstruct shared spherical edges, not contours of the atlas texture.
	internal static class MapGeometry
	{
		internal static MapPoint Project(UnitVector p) => new MapPoint((Math.Atan2(p.Z, p.X) + Math.PI) / (2 * Math.PI), (Math.Asin(Math.Max(-1, Math.Min(1, p.Y))) + Math.PI / 2) / Math.PI);
		internal static List<MapSegment> Build(Planet planet, PlanetGeography geography)
		{
			var lines = new List<MapSegment>(); Traverse(planet, geography, lines, null);
			lines.Sort((a, b) => a.Kind.CompareTo(b.Kind)); return lines;
		}
		internal static List<SphericalSegment> BuildSpherical(Planet planet, PlanetGeography geography)
		{
			var lines = new List<SphericalSegment>(); Traverse(planet, geography, null, lines);
			lines.Sort((a, b) => a.Kind.CompareTo(b.Kind)); return lines;
		}
		private struct Route
		{
			internal List<MapSegment> Flat;
			internal List<SphericalSegment> Spherical;
			internal Route(List<MapSegment> flat, List<SphericalSegment> spherical)
			{
				Flat = flat; Spherical = spherical;
			}
		}
		private static void Traverse(Planet planet, PlanetGeography geography, List<MapSegment> flat, List<SphericalSegment> spherical)
		{
			TileMesh mesh = planet.TopologyVersion == 1 ? planet.Mesh : null;
			int Owner(int i) => mesh != null ? planet.TileOwners[i] : i;
			int Nation(int owner) => owner < 0 ? -1 : planet.Provinces[owner].Nation;
			foreach (TileEdge edge in mesh != null ? (IEnumerable<TileEdge>)mesh.Edges : LegacyEdges(planet))
			{
				int left = Owner(edge.A), right = Owner(edge.B);
				byte kind = left == right ? (byte)0 : Nation(left) == Nation(right) ? (byte)1 : (byte)2;
				var route = new Route(flat, spherical); Arc(edge.First, edge.Second, left, right, kind, geography, ref route);
			}
			foreach (Road road in planet.Roads)
			{
				var route = new Route(flat, spherical);
				if (mesh != null)
				{
					UnitVector previous = planet.Provinces[road.StartProvince].Villages[road.StartVillage].Position;
					foreach (int tile in road.Tiles) { UnitVector next = mesh.Centers[tile]; Arc(previous, next, -1, -1, 3, geography, ref route); previous = next; }
					Arc(previous, planet.Provinces[road.EndProvince].Villages[road.EndVillage].Position, -1, -1, 3, geography, ref route);
				}
				else for (int i = 1; i < road.Path.Length; i++) Arc(RoadPoint(planet.Provinces[road.Path[i - 1]]), RoadPoint(planet.Provinces[road.Path[i]]), -1, -1, 3, geography, ref route);
			}
		}
		internal static void CellOutline(Planet planet, PlanetGeography geography, int tile, List<MapSegment> lines)
		{
			lines.Clear(); TileMesh mesh = planet.Mesh;
			foreach (int index in mesh.CellEdges[tile])
			{
				TileEdge edge = mesh.Edges[index]; var route = new Route(lines, null);
				Arc(edge.First, edge.Second, -1, -1, 0, geography, ref route);
			}
		}
		internal static void SphericalOutline(Planet planet, PlanetGeography geography, int selected, List<SphericalSegment> lines)
		{
			lines.Clear();
			if (planet.FormatVersion == 3)
			{
				TileMesh mesh = planet.Mesh;
				foreach (int index in mesh.CellEdges[selected])
				{
					TileEdge edge = mesh.Edges[index]; var route = new Route(null, lines);
					Arc(edge.First, edge.Second, -1, -1, 0, geography, ref route);
				}
			}
			else foreach (TileEdge edge in planet.TopologyVersion == 1 ? (IEnumerable<TileEdge>)planet.Mesh.Edges : LegacyEdges(planet))
			{
				int left = planet.TopologyVersion == 1 ? planet.TileOwners[edge.A] : edge.A;
				int right = planet.TopologyVersion == 1 ? planet.TileOwners[edge.B] : edge.B;
				if ((left == selected) == (right == selected)) continue;
				var route = new Route(null, lines); Arc(edge.First, edge.Second, -1, -1, 0, geography, ref route);
			}
		}
		private static IEnumerable<TileEdge> LegacyEdges(Planet planet)
		{
			foreach (Province province in planet.Provinces) foreach (int b in province.Neighbours)
			{
				int a = province.Id; if (b <= a) continue;
				UnitVector first = default, second = default, origin = province.Direction; int corners = 0;
				foreach (int c in province.Neighbours)
				{
					if (c == b || Array.IndexOf(planet.Provinces[b].Neighbours, c) < 0) continue;
					UnitVector cross = UnitVector.Cross(planet.Provinces[b].Direction - origin, planet.Provinces[c].Direction - origin);
					if (UnitVector.Dot(cross, cross) < 1e-24) continue;
					UnitVector corner = cross.Normalized(); if (UnitVector.Dot(corner, origin) < 0) corner = corner * -1;
					bool valid = true;
					foreach (int n in province.Neighbours) if (UnitVector.Dot(corner, origin - planet.Provinces[n].Direction) < -1e-10) { valid = false; break; }
					if (!valid || (corners > 0 && UnitVector.Dot(first, corner) > 1 - 1e-12)) continue;
					if (corners++ == 0) first = corner; else { second = corner; break; }
				}
				if (corners < 2) throw new InvalidOperationException("A shared map edge has no two dual corners.");
				yield return new TileEdge(a, b, first, second);
			}
		}
		private static UnitVector RoadPoint(Province p) => p.Villages.Length > 0 && UnitVector.Dot(p.Villages[0].Position, p.Villages[0].Position) > .5 ? p.Villages[0].Position : p.Direction;
		private static double Angle(UnitVector a, UnitVector b) => Math.Acos(Math.Max(-1, Math.Min(1, UnitVector.Dot(a, b))));
		private static void Arc(UnitVector a, UnitVector b, int left, int right, byte kind, PlanetGeography geography, ref Route route)
		{
			double angle = Angle(a, b);
			int steps = Math.Max(1, (int)Math.Ceiling(angle / .004)); UnitVector previous = a; double height = geography.Sample(a).Height;
			for (int i = 1; i <= steps; i++)
			{
				double t = (double)i / steps; UnitVector next = (a * (1 - t) + b * t).Normalized(); double nextHeight = geography.Sample(next).Height;
				Piece(previous, next, height, nextHeight, left, right, kind, geography, ref route, 0); previous = next; height = nextHeight;
			}
		}
		private static double Unwrap(double x, double around) => x + Math.Round(around - x);
		private static void Piece(UnitVector a, UnitVector b, double ah, double bh, int left, int right, byte kind, PlanetGeography geography, ref Route route, int depth)
		{
			MapPoint start = Project(a), end = Project(b); end.X = Unwrap(end.X, start.X);
			UnitVector middle = (a + b).Normalized(); MapPoint mid = Project(middle); mid.X = Unwrap(mid.X, (start.X + end.X) * .5);
			double mh = geography.Sample(middle).Height;
			double error = Math.Abs(mid.X - (start.X + end.X) * .5) + Math.Abs(mid.Y - (start.Y + end.Y) * .5);
			if (depth < 10 && (error > .00001 || ((ah > 0) == (bh > 0) && (mh > 0) != (ah > 0))))
			{
				Piece(a, middle, ah, mh, left, right, kind, geography, ref route, depth + 1);
				Piece(middle, b, mh, bh, left, right, kind, geography, ref route, depth + 1); return;
			}
			if (ah <= 0 && bh <= 0) return;
			UnitVector clippedA = a, clippedB = b;
			if ((ah > 0) != (bh > 0))
			{
				double low = 0, high = 1;
				for (int i = 0; i < 16; i++) { double t = (low + high) * .5; bool land = geography.Sample((a * (1 - t) + b * t).Normalized()).Height > 0; if (land == (ah > 0)) low = t; else high = t; }
				double fraction = (low + high) * .5;
				UnitVector coastDirection = (a * (1 - fraction) + b * fraction).Normalized();
				MapPoint coast = Project(coastDirection); coast.X = Unwrap(coast.X, start.X);
				if (ah <= 0) { start = coast; clippedA = coastDirection; }
				else { end = coast; clippedB = coastDirection; }
			}
			if (route.Spherical != null && UnitVector.Dot(clippedA - clippedB, clippedA - clippedB) > 1e-24)
				route.Spherical.Add(new SphericalSegment { A = clippedA, B = clippedB, Left = left, Right = right, Kind = kind });
			if (route.Flat == null) return;
			if (end.X < 0 || end.X > 1)
			{
				double seam = end.X < 0 ? 0 : 1, t = (seam - start.X) / (end.X - start.X);
				MapPoint edge = new MapPoint(seam, start.Y + (end.Y - start.Y) * t);
				AddFlat(route.Flat, start, edge, left, right, kind);
				AddFlat(route.Flat, new MapPoint(1 - seam, edge.Y), new MapPoint(end.X + (seam == 0 ? 1 : -1), end.Y), left, right, kind);
			}
			else AddFlat(route.Flat, start, end, left, right, kind);
		}
		private static void AddFlat(List<MapSegment> lines, MapPoint from, MapPoint to, int left, int right, byte kind)
		{
			if (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) > 1e-12)
				lines.Add(new MapSegment { A = from, B = to, Left = left, Right = right, Kind = kind });
		}
	}
}
