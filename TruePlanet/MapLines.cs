using System;
using System.Collections.Generic;
using UnityEngine;
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
		private List<MapStroke> _strokes;
		private int _start, _count;
		private float _fringe;
		internal void Show(List<MapStroke> strokes, int start, int count, float unit) { _strokes = strokes; _start = start; _count = count; _fringe = .75f * unit; SetVerticesDirty(); }
		protected override void OnPopulateMesh(VertexHelper mesh)
		{
			mesh.Clear(); if (_strokes == null) return;
			for (int i = _start; i < _start + _count; i++)
			{
				MapStroke stroke = _strokes[i]; Vector2 along = (stroke.B - stroke.A).normalized, side = new Vector2(-along.y, along.x);
				Vector2 a = stroke.A + side * (stroke.Width * .5f), b = stroke.B + side * (stroke.Width * .5f), c = stroke.B - side * (stroke.Width * .5f), d = stroke.A - side * (stroke.Width * .5f);
				Color32 clear = stroke.Color; clear.a = 0;
				Quad(a, b, c, d, stroke.Color, stroke.Color);
				Quad(a, b, b + (side + along) * _fringe, a + (side - along) * _fringe, stroke.Color, clear);
				Quad(b, c, c + (along - side) * _fringe, b + (along + side) * _fringe, stroke.Color, clear);
				Quad(c, d, d - (side + along) * _fringe, c + (along - side) * _fringe, stroke.Color, clear);
				Quad(d, a, a + (side - along) * _fringe, d - (side + along) * _fringe, stroke.Color, clear);
			}
			void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 inside, Color32 outside) { int at = mesh.currentVertCount; mesh.AddVert(a, inside, Vector2.zero); mesh.AddVert(b, inside, Vector2.zero); mesh.AddVert(c, outside, Vector2.zero); mesh.AddVert(d, outside, Vector2.zero); mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3); }
		}
	}
}
