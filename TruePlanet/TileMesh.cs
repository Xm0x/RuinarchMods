using System;
using System.Collections.Generic;
using System.Linq;

namespace TruePlanet
{
	public enum TileResolution { Standard = 5, Detailed = 6 }

	internal readonly struct TileEdge
	{
		internal readonly int A, B;
		internal readonly UnitVector First, Second;
		internal TileEdge(int a, int b, UnitVector first, UnitVector second) { A = a; B = b; First = first; Second = second; }
	}

	// Immutable geodesic dual mesh; IDs reconstruct from resolution without save bloat.
	internal sealed class TileMesh
	{
		private static readonly Dictionary<TileResolution, TileMesh> Cache = new Dictionary<TileResolution, TileMesh>();
		internal readonly UnitVector[] Centers;
		internal readonly int[][] Neighbours;
		internal readonly double[] Areas;
		internal readonly TileEdge[] Edges;
		internal readonly int[][] CellEdges;
		private readonly int[] _index;
		private struct Face { internal int A, B, C; internal Face(int a, int b, int c) { A = a; B = b; C = c; } }
		internal static TileMesh For(TileResolution resolution)
		{
			if (!Enum.IsDefined(typeof(TileResolution), resolution)) throw new ArgumentException("Unsupported tile resolution.");
			lock (Cache) { if (!Cache.TryGetValue(resolution, out TileMesh mesh)) Cache.Add(resolution, mesh = new TileMesh((int)resolution)); return mesh; }
		}
		private TileMesh(int level)
		{
			double t = (1 + Math.Sqrt(5)) / 2;
			var vertices = new List<UnitVector> { new UnitVector(-1,t,0),new UnitVector(1,t,0),new UnitVector(-1,-t,0),new UnitVector(1,-t,0),new UnitVector(0,-1,t),new UnitVector(0,1,t),new UnitVector(0,-1,-t),new UnitVector(0,1,-t),new UnitVector(t,0,-1),new UnitVector(t,0,1),new UnitVector(-t,0,-1),new UnitVector(-t,0,1) };
			for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].Normalized();
			var faces = new List<Face> { new Face(0,11,5),new Face(0,5,1),new Face(0,1,7),new Face(0,7,10),new Face(0,10,11),new Face(1,5,9),new Face(5,11,4),new Face(11,10,2),new Face(10,7,6),new Face(7,1,8),new Face(3,9,4),new Face(3,4,2),new Face(3,2,6),new Face(3,6,8),new Face(3,8,9),new Face(4,9,5),new Face(2,4,11),new Face(6,2,10),new Face(8,6,7),new Face(9,8,1) };
			for (int step = 0; step < level; step++)
			{
				var edges = new Dictionary<long,int>(); var next = new List<Face>(faces.Count * 4);
				int Mid(int a, int b) { long key = ((long)Math.Min(a,b) << 32) | (uint)Math.Max(a,b); if (!edges.TryGetValue(key,out int id)) { id = vertices.Count; vertices.Add((vertices[a] + vertices[b]).Normalized()); edges.Add(key,id); } return id; }
				foreach (Face f in faces) { int a = Mid(f.A,f.B), b = Mid(f.B,f.C), c = Mid(f.C,f.A); next.Add(new Face(f.A,a,c)); next.Add(new Face(f.B,b,a)); next.Add(new Face(f.C,c,b)); next.Add(new Face(a,b,c)); }
				faces = next;
			}
			Centers = vertices.ToArray(); Areas = new double[Centers.Length]; var neighbours = new HashSet<int>[Centers.Length];
			for (int i = 0; i < neighbours.Length; i++) neighbours[i] = new HashSet<int>();
			var dualEdges = new Dictionary<long, TileEdge>();
			void AddEdge(int a, int b, UnitVector corner)
			{
				int low = Math.Min(a, b), high = Math.Max(a, b); long key = ((long)low << 32) | (uint)high;
				if (dualEdges.TryGetValue(key, out TileEdge edge)) dualEdges[key] = new TileEdge(low, high, edge.First, corner);
				else dualEdges.Add(key, new TileEdge(low, high, corner, default));
			}
			foreach (Face f in faces)
			{
				UnitVector a = Centers[f.A], b = Centers[f.B], c = Centers[f.C];
				UnitVector corner = UnitVector.Cross(b-a,c-a).Normalized(); if (UnitVector.Dot(corner,a) < 0) corner = corner * -1;
				Add(f.A,f.B,f.C,corner); Add(f.B,f.C,f.A,corner); Add(f.C,f.A,f.B,corner);
				AddEdge(f.A, f.B, corner); AddEdge(f.B, f.C, corner); AddEdge(f.C, f.A, corner);
			}
			void Add(int a,int b,int c,UnitVector corner) { neighbours[a].Add(b); neighbours[a].Add(c); Areas[a] += Triangle(Centers[a],(Centers[a]+Centers[b]).Normalized(),corner) + Triangle(Centers[a],corner,(Centers[a]+Centers[c]).Normalized()); }
			Neighbours = neighbours.Select(n=>n.OrderBy(i=>i).ToArray()).ToArray();
			Edges = dualEdges.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
			var cellEdges = new List<int>[Centers.Length];
			for (int i = 0; i < cellEdges.Length; i++) cellEdges[i] = new List<int>(Neighbours[i].Length);
			for (int i = 0; i < Edges.Length; i++)
			{
				if (UnitVector.Dot(Edges[i].Second, Edges[i].Second) < .5) throw new InvalidOperationException("A shared tile edge has no second dual corner.");
				cellEdges[Edges[i].A].Add(i); cellEdges[Edges[i].B].Add(i);
			}
			CellEdges = cellEdges.Select(edges => edges.ToArray()).ToArray();
			_index = Enumerable.Range(0,Centers.Length).ToArray();
			var comparers = new IComparer<int>[3]; for (int axis = 0; axis < 3; axis++) { int dimension = axis; comparers[axis] = Comparer<int>.Create((a,b)=> { int cmp = Coordinate(Centers[a],dimension).CompareTo(Coordinate(Centers[b],dimension)); return cmp != 0 ? cmp : a.CompareTo(b); }); }
			void Build(int start,int count,int depth) { if (count == 0) return; Array.Sort(_index,start,count,comparers[depth%3]); int half = count/2; Build(start,half,depth+1); Build(start+half+1,count-half-1,depth+1); }
			Build(0,_index.Length,0);
		}
		internal static double Triangle(UnitVector a,UnitVector b,UnitVector c) => 2 * Math.Atan2(Math.Abs(UnitVector.Dot(a,UnitVector.Cross(b,c))),1+UnitVector.Dot(a,b)+UnitVector.Dot(b,c)+UnitVector.Dot(c,a));
		private static double Coordinate(UnitVector p,int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
		internal int Locate(UnitVector point)
		{
			int best = int.MaxValue; double distance = double.PositiveInfinity; Search(point,0,_index.Length,0,ref best,ref distance); return best;
		}
		private void Search(UnitVector point,int start,int count,int depth,ref int best,ref double distance)
		{
			if (count == 0) return; int half = count/2, id = _index[start+half]; UnitVector delta = point-Centers[id]; double score = UnitVector.Dot(delta,delta);
			if (score < distance || (score == distance && id < best)) { best = id; distance = score; }
			double plane = Coordinate(point,depth%3)-Coordinate(Centers[id],depth%3); int right = start+half+1, rightCount = count-half-1;
			if (plane < 0) { Search(point,start,half,depth+1,ref best,ref distance); if (plane*plane <= distance) Search(point,right,rightCount,depth+1,ref best,ref distance); }
			else { Search(point,right,rightCount,depth+1,ref best,ref distance); if (plane*plane <= distance) Search(point,start,half,depth+1,ref best,ref distance); }
		}
	}

	// Reusable min-heap for positive-cost graph growth and route search.
	internal sealed class TileQueue
	{
		internal struct Entry { internal int Tile, Owner; internal double Cost; }
		private Entry[] _items = new Entry[256]; private int _count;
		internal int Count => _count;
		internal void Clear() { _count = 0; }
		private static bool Before(Entry a,Entry b) => a.Cost < b.Cost || (a.Cost == b.Cost && (a.Tile < b.Tile || (a.Tile == b.Tile && a.Owner < b.Owner)));
		internal void Push(int tile,int owner,double cost)
		{
			if (_count == _items.Length) Array.Resize(ref _items,_items.Length*2); var value = new Entry { Tile = tile, Owner = owner, Cost = cost }; int i = _count++;
			while (i > 0) { int parent = (i-1)/2; if (!Before(value,_items[parent])) break; _items[i] = _items[parent]; i = parent; } _items[i] = value;
		}
		internal Entry Pop()
		{
			Entry result = _items[0], value = _items[--_count]; int i = 0;
			while (i*2+1 < _count) { int child = i*2+1; if (child+1 < _count && Before(_items[child+1],_items[child])) child++; if (!Before(_items[child],value)) break; _items[i] = _items[child]; i = child; } _items[i] = value; return result;
		}
	}
}
