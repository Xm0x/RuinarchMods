using System;
using System.Collections.Generic;
using System.Linq;

namespace TruePlanet
{
	internal static class SphereGraph
	{
		private struct Face
		{
			internal int A, B, C;
			internal UnitVector Normal;
			internal double Offset;
		}
		private struct Edge
		{
			internal int A, B;
			internal Edge(int a, int b) { A = a; B = b; }
		}
		internal static UnitVector Site(int i, int count, double phase)
		{
			double y = 1 - 2 * (i + .5) / count;
			double angle = i * Math.PI * (3 - Math.Sqrt(5)) + phase;
			double radius = Math.Sqrt(1 - y * y);
			return new UnitVector(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
		}
		internal static void Build(Planet planet, Random random)
		{
			int count = planet.Options.Provinces;
			planet.Provinces = new Province[count];
			double phase = random.NextDouble() * Math.PI * 2;
			double jitter = .4 / Math.Sqrt(count);
			for (int i = 0; i < count; i++)
			{
				UnitVector point = Site(i, count, phase);
				var noise = new UnitVector(random.NextDouble() - .5, random.NextDouble() - .5, random.NextDouble() - .5);
				planet.Provinces[i] = new Province { Id = i, Name = "Province " + (i + 1), Direction = (point + noise * jitter).Normalized() };
			}
			int a = 0, b = Farthest(planet, i => 1 - UnitVector.Dot(Point(planet, a), Point(planet, i)));
			UnitVector axis = Point(planet, b) - Point(planet, a);
			int c = Farthest(planet, i => { UnitVector cross = UnitVector.Cross(axis, Point(planet, i) - Point(planet, a)); return UnitVector.Dot(cross, cross); });
			UnitVector normal = UnitVector.Cross(axis, Point(planet, c) - Point(planet, a));
			int d = Farthest(planet, i => Math.Abs(UnitVector.Dot(normal, Point(planet, i) - Point(planet, a))));
			UnitVector inside = (Point(planet, a) + Point(planet, b) + Point(planet, c) + Point(planet, d)) * .25;
			var faces = new List<Face>(count * 2);
			faces.Add(Make(planet, a, b, c, inside)); faces.Add(Make(planet, a, d, b, inside));
			faces.Add(Make(planet, a, c, d, inside)); faces.Add(Make(planet, b, d, c, inside));
			var horizon = new Dictionary<long, Edge>();
			for (int i = 0; i < count; i++)
			{
				if (i == a || i == b || i == c || i == d) continue;
				horizon.Clear();
				for (int f = faces.Count - 1; f >= 0; f--)
				{
					Face face = faces[f];
					if (UnitVector.Dot(face.Normal, Point(planet, i)) - face.Offset <= 1e-12) continue;
					Toggle(horizon, face.A, face.B); Toggle(horizon, face.B, face.C); Toggle(horizon, face.C, face.A);
					faces.RemoveAt(f);
				}
				foreach (Edge edge in horizon.Values) faces.Add(Make(planet, edge.A, edge.B, i, inside));
			}
			var neighbours = new HashSet<int>[count];
			for (int i = 0; i < count; i++) neighbours[i] = new HashSet<int>();
			foreach (Face face in faces)
			{
				Join(neighbours, face.A, face.B); Join(neighbours, face.B, face.C); Join(neighbours, face.C, face.A);
			}
			for (int i = 0; i < count; i++) planet.Provinces[i].Neighbours = neighbours[i].OrderBy(n => n).ToArray();
		}
		private static UnitVector Point(Planet planet, int i) => planet.Provinces[i].Direction;
		private static int Farthest(Planet planet, Func<int, double> score)
		{
			int best = 0; double max = double.NegativeInfinity;
			for (int i = 0; i < planet.Provinces.Length; i++) { double next = score(i); if (next > max) { max = next; best = i; } }
			return best;
		}
		private static Face Make(Planet planet, int a, int b, int c, UnitVector inside)
		{
			UnitVector normal = UnitVector.Cross(Point(planet, b) - Point(planet, a), Point(planet, c) - Point(planet, a));
			if (UnitVector.Dot(normal, inside - Point(planet, a)) > 0) { int swap = b; b = c; c = swap; normal = normal * -1; }
			return new Face { A = a, B = b, C = c, Normal = normal, Offset = UnitVector.Dot(normal, Point(planet, a)) };
		}
		private static void Toggle(Dictionary<long, Edge> horizon, int a, int b)
		{
			long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
			if (!horizon.Remove(key)) horizon.Add(key, new Edge(a, b));
		}
		private static void Join(HashSet<int>[] neighbours, int a, int b) { neighbours[a].Add(b); neighbours[b].Add(a); }
	}
}
