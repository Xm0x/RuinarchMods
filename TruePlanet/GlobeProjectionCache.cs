using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruePlanet
{
    // Adjacent borders share endpoints. Project each needed endpoint once per frame,
    // including when province/nation outlines draw the same edge more than once.
    internal sealed class GlobeProjectionCache
    {
        internal readonly struct Edge
        {
            internal readonly int A, B;
            internal readonly byte Kind;
            internal Edge(int a, int b, byte kind) { A = a; B = b; Kind = kind; }
        }
        private struct Point
        {
            internal double Depth;
            internal Vector2 Position;
            internal int DepthFrame, PositionFrame;
        }
        private sealed class DirectionComparer : IEqualityComparer<UnitVector>
        {
            public bool Equals(UnitVector a, UnitVector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
            public int GetHashCode(UnitVector point) => unchecked((point.X.GetHashCode() * 397 ^ point.Y.GetHashCode()) * 397 ^ point.Z.GetHashCode());
        }
        internal readonly List<SphericalSegment> Source;
        internal readonly Edge[] Edges;
        private readonly UnitVector[] _directions;
        private readonly Point[] _points;
        private int _frame;
        private UnitVector _facing, _projectX, _projectY;
        private Vector2 _origin, _scale;

        internal GlobeProjectionCache(List<SphericalSegment> source)
        {
            Source = source;
            Edges = new Edge[source.Count];
            var indices = new Dictionary<UnitVector, int>(source.Count, new DirectionComparer());
            var directions = new List<UnitVector>(source.Count);
            int Index(UnitVector point)
            {
                if (indices.TryGetValue(point, out int index)) return index;
                index = directions.Count; directions.Add(point); indices.Add(point, index); return index;
            }
            for (int i = 0; i < source.Count; i++)
            {
                SphericalSegment segment = source[i];
                Edges[i] = new Edge(Index(segment.A), Index(segment.B), segment.Kind);
            }
            _directions = directions.ToArray();
            _points = new Point[_directions.Length];
        }
        internal void BeginFrame(UnitVector facing, UnitVector projectX, UnitVector projectY, Vector2 origin, Vector2 scale)
        {
            _frame = unchecked(_frame + 1);
            if (_frame == 0) { Array.Clear(_points, 0, _points.Length); _frame = 1; }
            _facing = facing; _projectX = projectX; _projectY = projectY; _origin = origin; _scale = scale;
        }
        internal UnitVector Direction(int index) => _directions[index];
        internal double Depth(int index)
        {
            ref Point point = ref _points[index];
            if (point.DepthFrame != _frame)
            {
                point.Depth = UnitVector.Dot(_directions[index], _facing);
                point.DepthFrame = _frame;
            }
            return point.Depth;
        }
        internal Vector2 Position(int index)
        {
            ref Point point = ref _points[index];
            if (point.PositionFrame != _frame)
            {
                UnitVector direction = _directions[index];
                point.Position = _origin + new Vector2((float)UnitVector.Dot(direction, _projectX) * _scale.x,
                    (float)UnitVector.Dot(direction, _projectY) * _scale.y);
                point.PositionFrame = _frame;
            }
            return point.Position;
        }
    }
}
