using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TruePlanet
{
	internal sealed class GlobeOverlays : MonoBehaviour
	{
		private PlanetSurface _surface;
		private Planet _world;
		private Camera _camera;
		private Transform _sphere;
		private RectTransform _viewport;
		private TMP_FontAsset _font;
		private Material _labelMaterial;
		private List<SphericalSegment> _geometry;
		private int _borderStart, _roadStart;
		private UnitVector _projectX, _projectY;
		private Vector2 _projectOrigin, _projectScale;
		private readonly List<SphericalSegment> _outline = new List<SphericalSegment>();
		private readonly List<MapStroke> _strokes = new List<MapStroke>();
		private readonly List<MapLineGraphic> _batches = new List<MapLineGraphic>();
		private readonly List<Label> _labels = new List<Label>();
		private readonly List<Rect> _occupied = new List<Rect>();
		private int _selected = -1, _outlineFor = -1;
		private bool _borders = true, _roads = true, _grid;
		private float _unit, _radius;
		private Rect _bounds;
		private UnitVector _facing;
		private Quaternion _rotation;
		private Vector3 _centre;
		private sealed class Label
		{
			internal UnitVector Position;
			internal RectTransform Text, Marker;
			internal bool Nation, Capital;
		}
		internal void Configure(PlanetSurface surface, Camera camera, Transform sphere, RectTransform viewport, TMP_FontAsset font, Material material)
		{
			_surface = surface; _camera = camera; _sphere = sphere; _viewport = viewport; _font = font;
			_labelMaterial = new Material(material) { name = "TruePlanet globe label material" };
			_labelMaterial.EnableKeyword("OUTLINE_ON"); _labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .18f);
			_labelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(20, 27, 28, 255));
		}
		internal void Refresh(int selected, bool borders, bool roads, bool grid)
		{
			_selected = selected; _borders = borders; _roads = roads; _grid = grid;
		}
		internal void Project(float zoom)
		{
			if (!_surface.Ready) return;
			if (!ReferenceEquals(_world, _surface.Planet))
			{
				_world = _surface.Planet; _geometry = _surface.SphericalGeometry;
				_borderStart = 0;
				while (_borderStart < _geometry.Count && _geometry[_borderStart].Kind == 0) _borderStart++;
				_roadStart = _borderStart;
				while (_roadStart < _geometry.Count && _geometry[_roadStart].Kind < 3) _roadStart++;
				_outline.Clear(); _outlineFor = -1; BuildLabels();
			}
			if (_outlineFor != _selected)
			{
				_outlineFor = _selected; _outline.Clear();
				if (_selected >= 0) MapGeometry.SphericalOutline(_world, _surface.Geography, _selected, _outline);
			}
			_bounds = _viewport.rect; _rotation = _sphere.rotation; _centre = _sphere.position; _radius = _sphere.lossyScale.x;
			Vector3 normal = Quaternion.Inverse(_rotation) * -_camera.transform.forward;
			_facing = new UnitVector(normal.x, normal.y, normal.z);
			Vector2 origin = RectTransformUtility.WorldToScreenPoint(null, _viewport.TransformPoint(Vector3.zero));
			Vector2 right = RectTransformUtility.WorldToScreenPoint(null, _viewport.TransformPoint(Vector3.right));
			_unit = 1 / Mathf.Max(.001f, Vector2.Distance(origin, right));
			Vector3 xAxis = Quaternion.Inverse(_rotation) * _camera.transform.right, yAxis = Quaternion.Inverse(_rotation) * _camera.transform.up;
			_projectX = new UnitVector(xAxis.x, xAxis.y, xAxis.z); _projectY = new UnitVector(yAxis.x, yAxis.y, yAxis.z);
			Vector3 center = _camera.WorldToViewportPoint(_centre);
			_projectOrigin = new Vector2(_bounds.xMin + center.x * _bounds.width, _bounds.yMin + center.y * _bounds.height);
			_projectScale = new Vector2(_bounds.width * _radius / (2 * _camera.orthographicSize * _camera.aspect), _bounds.height * _radius / (2 * _camera.orthographicSize));
			_strokes.Clear();
			if (_grid) for (int i = 0; i < _borderStart; i++)
				if (Visible(_geometry[i], out Vector2 a, out Vector2 b)) Stroke(a, b, MapStyle.Grid);
			if (_borders) for (int i = _borderStart; i < _roadStart; i++)
			{
				SphericalSegment segment = _geometry[i];
				if (!Visible(segment, out Vector2 a, out Vector2 b)) continue;
				if (segment.Kind == 1) Stroke(a, b, MapStyle.Province);
				else { Stroke(a, b, MapStyle.NationOuter); Stroke(a, b, MapStyle.NationInner); }
			}
			foreach (SphericalSegment segment in _outline)
				if (Visible(segment, out Vector2 a, out Vector2 b)) { Stroke(a, b, MapStyle.SelectionOuter); Stroke(a, b, MapStyle.SelectionInner); }
			if (_roads) for (int i = _roadStart; i < _geometry.Count; i++)
				if (Visible(_geometry[i], out Vector2 a, out Vector2 b)) Stroke(a, b, MapStyle.Road);
			int needed = (_strokes.Count + MapStyle.BatchSize - 1) / MapStyle.BatchSize;
			while (_batches.Count < needed)
			{
				var go = Node("TruePlanet globe line batch"); var rect = (RectTransform)go.transform;
				rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
				var batch = go.AddComponent<MapLineGraphic>(); batch.raycastTarget = false; _batches.Add(batch);
			}
			for (int i = 0; i < _batches.Count; i++)
			{
				_batches[i].gameObject.SetActive(i < needed);
				if (i < needed) _batches[i].Show(_strokes, i * MapStyle.BatchSize, Math.Min(MapStyle.BatchSize, _strokes.Count - i * MapStyle.BatchSize), _unit);
			}
			ProjectLabels(zoom);
		}
		private bool Visible(SphericalSegment segment, out Vector2 a, out Vector2 b)
		{
			UnitVector start = segment.A, end = segment.B; a = b = default;
			double ah = UnitVector.Dot(start, _facing), bh = UnitVector.Dot(end, _facing);
			if (ah <= 0 && bh <= 0) return false;
			if ((ah > 0) != (bh > 0))
			{
				double low = 0, high = 1;
				for (int i = 0; i < 16; i++)
				{
					double t = (low + high) * .5;
					bool near = UnitVector.Dot((start * (1 - t) + end * t).Normalized(), _facing) > 0;
					if (near == (ah > 0)) low = t; else high = t;
				}
				double fraction = (low + high) * .5; UnitVector limb = (start * (1 - fraction) + end * fraction).Normalized();
				if (ah <= 0) start = limb; else end = limb;
			}
			a = Local(start); b = Local(end);
			return true;
		}
		private Vector2 Local(UnitVector direction) => _projectOrigin + new Vector2(
			(float)UnitVector.Dot(direction, _projectX) * _projectScale.x,
			(float)UnitVector.Dot(direction, _projectY) * _projectScale.y);
		private bool Clip(ref Vector2 a, ref Vector2 b)
		{
			float low = 0, high = 1; Vector2 delta = b - a;
			bool Edge(float p, float q)
			{
				if (Mathf.Abs(p) < 1e-12f) return q >= 0; float t = q / p;
				if (p < 0) { if (t > high) return false; low = Mathf.Max(low, t); }
				else { if (t < low) return false; high = Mathf.Min(high, t); } return true;
			}
			if (!Edge(-delta.x, a.x - _bounds.xMin) || !Edge(delta.x, _bounds.xMax - a.x) || !Edge(-delta.y, a.y - _bounds.yMin) || !Edge(delta.y, _bounds.yMax - a.y)) return false;
			b = a + delta * high; a += delta * low; return high > low;
		}
		private void Stroke(Vector2 a, Vector2 b, MapLineStyle style) { if (Clip(ref a, ref b)) Add(a, b, style); }
		private void Add(Vector2 a, Vector2 b, MapLineStyle style)
		{
			if ((a - b).sqrMagnitude > 1e-12f) _strokes.Add(new MapStroke { A = a, B = b, Width = style.Width * _unit, Color = style.Color });
		}
		private GameObject Node(string name)
		{
			var go = new GameObject(name, typeof(RectTransform)); go.layer = LayerMask.NameToLayer("UI"); go.transform.SetParent(transform, false); return go;
		}
		private void BuildLabels()
		{
			foreach (Label label in _labels) { Destroy(label.Text.gameObject); if (label.Marker != null) Destroy(label.Marker.gameObject); }
			_labels.Clear();
			foreach (Nation nation in _world.Nations)
				if (NationAnchor(nation.Id, out UnitVector anchor)) AddLabel(nation.Name, anchor, true, false);
			for (int priority = 0; priority < 2; priority++) foreach (Province province in _world.Provinces) foreach (Village village in province.Villages)
			{
				if (village.Capital != (priority == 0)) continue;
				UnitVector point = UnitVector.Dot(village.Position, village.Position) > .5 ? village.Position : province.Direction;
				AddLabel(village.Name, point, false, village.Capital);
			}
		}
		private bool NationAnchor(int nation, out UnitVector anchor)
		{
			UnitVector centre = default; foreach (Province p in _world.Provinces) if (p.Nation == nation) centre += p.Direction * p.Area;
			if (UnitVector.Dot(centre, centre) > 1e-20) centre = centre.Normalized();
			anchor = default; double best = -2;
			if (_world.FormatVersion == 3)
			{
				TileLand land = TileLand.For(_world);
				for (int i = 0; i < land.Cells.Length; i++)
				{
					int owner = _world.TileOwners[i]; if (owner < 0 || _world.Provinces[owner].Nation != nation || land.Cells[i].LandArea <= 0) continue;
					UnitVector point = land.Cells[i].DryPoint; double score = UnitVector.Dot(point, centre);
					if (score > best) { best = score; anchor = point; }
				}
			}
			else foreach (Province p in _world.Provinces)
			{
				if (p.Nation != nation || !p.Land) continue;
				if (_surface.Geography.Sample(p.Direction).Height > 0 && UnitVector.Dot(p.Direction, centre) > best)
				{ best = UnitVector.Dot(p.Direction, centre); anchor = p.Direction; }
			}
			return best > -2;
		}
		private void AddLabel(string name, UnitVector point, bool nation, bool capital)
		{
			var go = Node("TruePlanet globe label " + name); var text = go.AddComponent<TextMeshProUGUI>();
			text.font = _font; text.fontSharedMaterial = _labelMaterial; text.text = name; text.richText = false; text.enableWordWrapping = false;
			text.fontSize = nation ? 22 : 15; text.fontStyle = nation ? FontStyles.Bold : FontStyles.Normal;
			text.color = nation ? new Color32(249, 231, 180, 255) : new Color32(249, 246, 226, 255);
			text.raycastTarget = false; text.alignment = nation ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
			var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
			rect.sizeDelta = text.GetPreferredValues(name) + new Vector2(8, 4);
			var label = new Label { Text = rect, Position = point, Nation = nation, Capital = capital };
			if (!nation)
			{
				var marker = Node("TruePlanet globe settlement"); label.Marker = (RectTransform)marker.transform;
				label.Marker.anchorMin = label.Marker.anchorMax = new Vector2(.5f, .5f); label.Marker.sizeDelta = new Vector2(capital ? 8 : 5, capital ? 8 : 5);
				label.Marker.localEulerAngles = new Vector3(0, 0, 45);
				var image = marker.AddComponent<Image>(); image.color = capital ? new Color32(251, 205, 92, 255) : new Color32(247, 239, 202, 255); image.raycastTarget = false;
			}
			_labels.Add(label);
		}
		private void ProjectLabels(float zoom)
		{
			_occupied.Clear();
			float silhouette = _radius / _camera.orthographicSize * _bounds.height * .5f * .98f;
			foreach (Label label in _labels)
			{
				bool visible = UnitVector.Dot(label.Position, _facing) > .18 && (label.Nation || label.Capital || zoom >= 1.45f);
				Vector2 point = visible ? Local(label.Position) : default;
				visible &= _bounds.Contains(point);
				if (label.Marker != null) { label.Marker.gameObject.SetActive(visible); label.Marker.anchoredPosition = point; }
				bool placed = false;
				for (int attempt = 0; visible && attempt < (label.Nation ? 1 : 4); attempt++)
				{
					Vector2 offset = label.Nation ? Vector2.zero : new Vector2((attempt < 2 ? 1 : -1) * (label.Text.sizeDelta.x * .5f + 9), attempt % 2 == 0 ? 0 : 20);
					Rect box = new Rect(point + offset - label.Text.sizeDelta * .5f, label.Text.sizeDelta);
					if (!_bounds.Contains(box.min) || !_bounds.Contains(box.max)) continue;
					Vector2 extent = new Vector2(Mathf.Max(Mathf.Abs(box.xMin - _bounds.center.x), Mathf.Abs(box.xMax - _bounds.center.x)),
						Mathf.Max(Mathf.Abs(box.yMin - _bounds.center.y), Mathf.Abs(box.yMax - _bounds.center.y)));
					if (extent.sqrMagnitude > silhouette * silhouette) continue;
					bool overlap = false; foreach (Rect occupied in _occupied) if (occupied.Overlaps(box)) { overlap = true; break; }
					if (overlap) continue;
					label.Text.anchoredPosition = point + offset; _occupied.Add(box); placed = true; break;
				}
				label.Text.gameObject.SetActive(placed);
			}
		}
		private void OnDestroy() { if (_labelMaterial != null) Destroy(_labelMaterial); }
	}
}
