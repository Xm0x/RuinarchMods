using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TruePlanet
{
	internal struct MapStroke
	{
		internal Vector2 A, B;
		internal Color32 Color;
		internal float Width;
	}
	internal readonly struct MapLineStyle
	{
		internal readonly float Width;
		internal readonly Color32 Color;
		internal MapLineStyle(float width, Color32 color) { Width = width; Color = color; }
	}
	internal static class MapStyle
	{
		internal const int BatchSize = 1800;
		internal static readonly MapLineStyle Grid = new MapLineStyle(.65f, new Color32(24, 36, 40, 75));
		internal static readonly MapLineStyle Province = new MapLineStyle(.9f, new Color32(25, 35, 39, 110));
		internal static readonly MapLineStyle NationOuter = new MapLineStyle(3.2f, new Color32(25, 37, 42, 230));
		internal static readonly MapLineStyle NationInner = new MapLineStyle(1.1f, new Color32(233, 231, 210, 210));
		internal static readonly MapLineStyle SelectionOuter = new MapLineStyle(4, new Color32(48, 39, 26, 240));
		internal static readonly MapLineStyle SelectionInner = new MapLineStyle(2, new Color32(255, 218, 108, 255));
		internal static readonly MapLineStyle Road = new MapLineStyle(1.5f, new Color32(229, 151, 85, 255));
	}
	// Only visible strokes enter uGUI meshes. Batches stay below its 65k vertex limit.
	internal sealed class MapLines : MonoBehaviour
	{
		private const int BatchSize = MapStyle.BatchSize;
		private List<MapSegment> _geometry;
		private readonly List<MapStroke> _strokes = new List<MapStroke>();
		private readonly List<MapLineGraphic> _batches = new List<MapLineGraphic>();
		private List<MapSegment> _selection;
		private int _selectedTile = -1;
		private RectTransform _map;
		private Rect _visible;
		private float _unit;
		private int _borderStart, _roadStart;
		internal void Configure(RectTransform map, List<MapSegment> geometry)
		{
			_map = map; _geometry = geometry;
			_borderStart = 0; while (_borderStart < geometry.Count && geometry[_borderStart].Kind == 0) _borderStart++;
			_roadStart = _borderStart; while (_roadStart < geometry.Count && geometry[_roadStart].Kind < 3) _roadStart++;
		}
		internal void SelectTile(Planet planet, PlanetGeography geography, int tile)
		{
			if (_selectedTile == tile) return;
			_selectedTile = tile;
			if (tile < 0) _selection?.Clear();
			else
			{
				if (_selection == null) _selection = new List<MapSegment>();
				MapGeometry.CellOutline(planet, geography, tile, _selection);
			}
		}
		internal void Refresh(bool grid, bool borders, bool roads, int selected)
		{
			if (_map == null || _geometry == null || _map.rect.width <= 0) return;
			_strokes.Clear(); Rect rect = _map.rect;
			RectTransform bounds = (RectTransform)_map.parent;
			Vector2 min = _map.InverseTransformPoint(bounds.TransformPoint(bounds.rect.min)), max = _map.InverseTransformPoint(bounds.TransformPoint(bounds.rect.max));
			_visible = Rect.MinMaxRect(Mathf.Max(min.x, rect.xMin), Mathf.Max(min.y, rect.yMin), Mathf.Min(max.x, rect.xMax), Mathf.Min(max.y, rect.yMax));
			Vector2 screenA = RectTransformUtility.WorldToScreenPoint(null, _map.TransformPoint(Vector3.zero)), screenB = RectTransformUtility.WorldToScreenPoint(null, _map.TransformPoint(Vector3.right));
			_unit = 1 / Mathf.Max(.001f, Vector2.Distance(screenA, screenB));
			for (int i = grid ? 0 : _borderStart; i < _roadStart; i++)
			{
				MapSegment segment = _geometry[i];
				if (segment.Kind == 0) { if (grid) Stroke(segment, MapStyle.Grid); }
				else if (borders)
				{
					if (segment.Kind == 1) Stroke(segment, MapStyle.Province);
					else { Stroke(segment, MapStyle.NationOuter); Stroke(segment, MapStyle.NationInner); }
				}
			}
			if (selected >= 0) for (int i = _borderStart; i < _roadStart; i++)
			{
				MapSegment segment = _geometry[i];
				if (segment.Left == selected || segment.Right == selected) { Stroke(segment, MapStyle.SelectionOuter); Stroke(segment, MapStyle.SelectionInner); }
			}
			if (_selection != null) foreach (MapSegment segment in _selection)
			{
				Stroke(segment, MapStyle.SelectionOuter); Stroke(segment, MapStyle.SelectionInner);
			}
			if (roads) for (int i = _roadStart; i < _geometry.Count; i++) Stroke(_geometry[i], MapStyle.Road);
			int needed = (_strokes.Count + BatchSize - 1) / BatchSize;
			while (_batches.Count < needed)
			{
				var go = new GameObject("Vector line batch", typeof(RectTransform)); go.layer = LayerMask.NameToLayer("UI"); go.transform.SetParent(transform, false);
				var child = (RectTransform)go.transform; child.anchorMin = Vector2.zero; child.anchorMax = Vector2.one; child.offsetMin = child.offsetMax = Vector2.zero;
				var graphic = go.AddComponent<MapLineGraphic>(); graphic.raycastTarget = false; _batches.Add(graphic);
			}
			for (int i = 0; i < _batches.Count; i++) { _batches[i].gameObject.SetActive(i < needed); if (i < needed) _batches[i].Show(_strokes, i * BatchSize, Math.Min(BatchSize, _strokes.Count - i * BatchSize), _unit); }
		}
		private Vector2 Local(MapPoint point) => new Vector2((float)point.X * _map.rect.width + _map.rect.xMin, (float)point.Y * _map.rect.height + _map.rect.yMin);
		private bool Clip(ref Vector2 a, ref Vector2 b)
		{
			float low = 0, high = 1; Vector2 delta = b - a;
			bool Edge(float p, float q) { if (Mathf.Abs(p) < 1e-12f) return q >= 0; float t = q / p; if (p < 0) { if (t > high) return false; low = Mathf.Max(low, t); } else { if (t < low) return false; high = Mathf.Min(high, t); } return true; }
			if (!Edge(-delta.x, a.x - _visible.xMin) || !Edge(delta.x, _visible.xMax - a.x) || !Edge(-delta.y, a.y - _visible.yMin) || !Edge(delta.y, _visible.yMax - a.y)) return false;
			b = a + delta * high; a += delta * low; return high > low;
		}
		private void Stroke(MapSegment segment, MapLineStyle style)
		{
			Vector2 a = Local(segment.A), b = Local(segment.B); if (Clip(ref a, ref b)) Add(a, b, style.Width, style.Color);
		}
		private void Add(Vector2 a, Vector2 b, float width, Color32 color) { if ((a - b).sqrMagnitude > 1e-12f) _strokes.Add(new MapStroke { A = a, B = b, Width = width * _unit, Color = color }); }
	}
	internal sealed class MapLineGraphic : MaskableGraphic
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct LineVertex
		{
			internal Vector3 Position;
			internal Color32 Color;
			internal Vector2 UV;
		}
		private static readonly VertexAttributeDescriptor[] Layout = {
			new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
			new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
			new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
		};
		private static readonly ushort[] Indices = BuildIndices();
		private List<MapStroke> _strokes;
		private LineVertex[] _vertices = Array.Empty<LineVertex>();
		private Mesh _lineMesh;
		private int _start, _count, _meshVertices = -1;
		private float _fringe;
		internal void Show(List<MapStroke> strokes, int start, int count, float unit) { _strokes = strokes; _start = start; _count = count; _fringe = .75f * unit; SetVerticesDirty(); }
		protected override void UpdateGeometry()
		{
			if (_strokes == null) return;
			BuildVertices();
			int vertexCount = _count * 8;
			if (_lineMesh == null)
			{
				_lineMesh = new Mesh { name = "TruePlanet line mesh" };
				_lineMesh.MarkDynamic();
			}
			if (_meshVertices != vertexCount)
			{
				_lineMesh.SetVertexBufferParams(vertexCount, Layout);
				_lineMesh.SetIndices(Indices, 0, _count * 30, MeshTopology.Triangles, 0, false);
				_meshVertices = vertexCount;
			}
			// UI/Default consumes position, color and UV only. Upload one packed stream;
			// keep the exact triangle topology instead of rebuilding VertexHelper's lists.
			if (vertexCount > 0) _lineMesh.SetVertexBufferData(_vertices, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);
			_lineMesh.RecalculateBounds();
			canvasRenderer.SetMesh(_lineMesh);
		}
		private void BuildVertices()
		{
			int vertexCount = _count * 8;
			if (_vertices.Length < vertexCount)
				_vertices = new LineVertex[Math.Min(MapStyle.BatchSize * 8, Math.Max(vertexCount, _vertices.Length * 2))];
			for (int i = 0; i < _count; i++)
			{
				MapStroke stroke = _strokes[_start + i]; Vector2 along = (stroke.B - stroke.A).normalized, side = new Vector2(-along.y, along.x);
				float halfWidth = stroke.Width * .5f;
				Vector2 a = stroke.A + side * halfWidth, b = stroke.B + side * halfWidth, c = stroke.B - side * halfWidth, d = stroke.A - side * halfWidth;
				Color32 clear = stroke.Color; clear.a = 0;
				int at = i * 8;
				Vertex(at, a, stroke.Color); Vertex(at + 1, b, stroke.Color);
				Vertex(at + 2, c, stroke.Color); Vertex(at + 3, d, stroke.Color);
				Vertex(at + 4, a + (side - along) * _fringe, clear);
				Vertex(at + 5, b + (side + along) * _fringe, clear);
				Vertex(at + 6, c + (along - side) * _fringe, clear);
				Vertex(at + 7, d - (side + along) * _fringe, clear);
			}
		}
		private void Vertex(int index, Vector2 position, Color32 color)
		{
			_vertices[index].Position = position;
			_vertices[index].Color = color;
		}
		private static ushort[] BuildIndices()
		{
			var result = new ushort[MapStyle.BatchSize * 30]; int next = 0;
			for (int i = 0; i < MapStyle.BatchSize; i++)
			{
				int at = i * 8;
				Quad(result, ref next, at, at + 1, at + 2, at + 3);
				Quad(result, ref next, at, at + 1, at + 5, at + 4);
				Quad(result, ref next, at + 1, at + 2, at + 6, at + 5);
				Quad(result, ref next, at + 2, at + 3, at + 7, at + 6);
				Quad(result, ref next, at + 3, at, at + 4, at + 7);
			}
			return result;
		}
		private static void Quad(ushort[] indices, ref int next, int a, int b, int c, int d)
		{
			indices[next++] = (ushort)a; indices[next++] = (ushort)b; indices[next++] = (ushort)c;
			indices[next++] = (ushort)a; indices[next++] = (ushort)c; indices[next++] = (ushort)d;
		}
		protected override void OnDestroy()
		{
			if (_lineMesh != null) Destroy(_lineMesh);
			base.OnDestroy();
		}
	}
}
