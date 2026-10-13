using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TruePlanet
{
	public sealed class PlanetCanvas : MonoBehaviour, IPointerClickHandler, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		private const int Width = PlanetSurface.Width, Height = PlanetSurface.Height;
		private PlanetSurface _surface;
		private Planet _builtPlanet;
		private Planet Planet => _surface.Planet;
		private Action<int> _picked;
		private int _selected = -1;
		private bool _borders = true, _roads = true, _grid;
		private MapLines _linework;
		private TMP_FontAsset _font;
		private Material _labelMaterial;
		private Transform _overlay;
		private float _zoom = 1;
		private bool _dragged;
		private Vector2 _dragStart, _origin;
		private readonly List<MapLabel> _labels = new List<MapLabel>();
		private readonly List<Rect> _occupied = new List<Rect>();
		private sealed class MapLabel { public RectTransform Marker, Text; public TMP_Text Caption; public UnitVector Position; public Vector2 BaseSize; public bool Nation, Capital; public int Owner; public TerritoryLabel Territory; }
		internal bool Ready => _surface != null && _surface.Ready;
		internal float Zoom => _zoom;
		internal void Configure(PlanetSurface surface, TMP_FontAsset font, Material material, Action<int> picked)
		{
			_surface = surface; _picked = picked; _surface.Changed += SurfaceChanged;
			_font = font; _labelMaterial = new Material(material);
			_labelMaterial.EnableKeyword("OUTLINE_ON"); _labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .18f);
			_labelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(20, 27, 28, 255));
		}
		internal void Refresh(int selected, bool borders, bool roads, bool grid)
		{
			_selected = selected; _borders = borders; _roads = roads; _grid = grid;
			LayoutOverlays();
		}
		internal void SetVisible(bool visible) => gameObject.SetActive(visible);
		private void SurfaceChanged()
		{
			GetComponent<RawImage>().enabled = Ready;
			if (!Ready)
			{
				_builtPlanet = null;
				if (_overlay != null) _overlay.gameObject.SetActive(false);
				if (_linework != null) _linework.gameObject.SetActive(false);
				return;
			}
			GetComponent<RawImage>().texture = _surface.Texture;
			if (!ReferenceEquals(_builtPlanet, Planet))
			{
				if (_linework != null) Destroy(_linework.gameObject);
				var lines = new GameObject("Political boundaries and roads", typeof(RectTransform));
				lines.layer = LayerMask.NameToLayer("UI"); lines.transform.SetParent(transform, false);
				var rect = (RectTransform)lines.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
				_linework = lines.AddComponent<MapLines>(); _linework.Configure((RectTransform)transform, MapGeometry.Build(Planet, _surface.Geography));
				BuildLabels(); _builtPlanet = Planet;
			}
			LayoutOverlays();
		}
		private void LayoutOverlays()
		{
			if (!Ready) return;
			LayoutLabels();
			if (Ready && _linework != null)
			{
				bool tiles = Planet.FormatVersion == 3;
				_linework.SelectTile(Planet, _surface.Geography, tiles ? _selected : -1);
				_linework.Refresh(_grid, _borders, _roads, tiles ? -1 : _selected);
			}
		}
		private static Vector2 UV(UnitVector point) => new Vector2((float)((Math.Atan2(point.Z, point.X) + Math.PI) / (2 * Math.PI)), (float)((Math.Asin(Math.Max(-1, Math.Min(1, point.Y))) + Math.PI / 2) / Math.PI));
		private void BuildLabels()
		{
			if (_overlay != null) Destroy(_overlay.gameObject); _labels.Clear();
			var root = GlobeSkin.Node("Named locations", transform); _overlay = root.transform;
			var rect = (RectTransform)_overlay; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
			var aspects = new double[Planet.Nations.Length];
			foreach (Nation nation in Planet.Nations)
			{
				MapLabel label = AddLabel(nation.Name, Planet.Provinces[nation.Capital].Direction, true, false, nation.Id);
				aspects[nation.Id] = label.Text.sizeDelta.x / label.Text.sizeDelta.y;
			}
			const int step = 4; int width = Width / step, height = Height / step;
			var owners = new int[width * height]; var land = new float[owners.Length];
			for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
			{
				int source = (y * step + step / 2) * Width + x * step + step / 2, at = y * width + x;
				int province = _surface.ProvinceIds[source]; owners[at] = province < 0 ? -1 : Planet.Provinces[province].Nation; land[at] = _surface.Heights[source];
			}
			TerritoryLabel[] territories = MapTerritories.Fit(owners, land, width, height, aspects);
			foreach (MapLabel label in _labels) if (label.Nation)
			{
				label.Territory = territories[label.Owner];
				label.Position = UnitVector.FromMap(label.Territory.Center.X * Math.PI * 2 - Math.PI, label.Territory.Center.Y * Math.PI - Math.PI / 2);
			}
			foreach (Province p in Planet.Provinces) foreach (Village village in p.Villages)
				AddLabel(village.Name, UnitVector.Dot(village.Position, village.Position) > .5 ? village.Position : p.Direction, false, village.Capital, village.Nation);
		}
		private MapLabel AddLabel(string name, UnitVector point, bool nation, bool capital, int owner)
		{
			var go = GlobeSkin.Node((nation ? "Nation " : "Village ") + name, _overlay);
			var text = go.AddComponent<TextMeshProUGUI>(); text.font = _font; text.fontSharedMaterial = _labelMaterial; text.text = name;
			text.fontSize = nation ? 22 : 15; text.fontStyle = nation ? FontStyles.Bold : FontStyles.Normal; text.color = nation ? new Color32(249, 231, 180, 255) : new Color32(249, 246, 226, 255);
			text.characterSpacing = nation ? 2 : 0;
			text.richText = false; text.enableWordWrapping = false; text.raycastTarget = false; text.alignment = nation ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
			var label = new MapLabel { Text = (RectTransform)go.transform, Caption = text, Position = point, Nation = nation, Capital = capital, Owner = owner };
			Vector2 preferred = text.GetPreferredValues(name);
			label.Text.anchorMin = label.Text.anchorMax = new Vector2(.5f,.5f); label.Text.pivot = new Vector2(nation ? .5f : 0,.5f); label.Text.sizeDelta = new Vector2(preferred.x + 8, nation ? preferred.y + 2 : 28);
			label.BaseSize = label.Text.sizeDelta;
			if (!nation)
			{
				var marker = GlobeSkin.Node("Settlement", _overlay); marker.AddComponent<Image>();
				label.Marker = (RectTransform)marker.transform; label.Marker.anchorMin = label.Marker.anchorMax = new Vector2(.5f,.5f);
				label.Marker.sizeDelta = new Vector2(capital ? 8 : 5, capital ? 8 : 5); label.Marker.localEulerAngles = new Vector3(0,0,45);
				var image = marker.GetComponent<Image>(); image.color = capital ? new Color32(251, 205, 92, 255) : owner >= 0 ? Color.Lerp(_surface.NationColors[owner], Color.white, .65f) : (Color)new Color32(247, 239, 202, 255); image.raycastTarget = false;
			}
			_labels.Add(label); return label;
		}
		private void LayoutLabels()
		{
			if (_overlay == null) return; _occupied.Clear(); Rect rect = ((RectTransform)transform).rect;
			RectTransform bounds = (RectTransform)transform.parent;
			foreach (MapLabel label in _labels)
			{
				Vector2 uv = UV(label.Position), point = new Vector2((uv.x-.5f)*rect.width, (uv.y-.5f)*rect.height);
				Vector2 local = bounds.InverseTransformPoint(transform.TransformPoint(point));
				bool visible = bounds.rect.Contains(local);
				if (label.Marker != null) { label.Marker.gameObject.SetActive(visible); label.Marker.anchoredPosition = point; label.Marker.localScale = Vector3.one / _zoom; }
				if (label.Nation)
				{
					LayoutNation(label, rect, bounds, point, local, visible); continue;
				}
				label.Text.localScale = Vector3.one / _zoom; Vector2 size = label.Text.sizeDelta;
				bool placed = false;
				for (int attempt = 0; visible && attempt < 4; attempt++)
				{
					Vector2 offset = new Vector2(attempt < 2 ? 9 : -size.x-9, attempt % 2 == 0 ? 0 : 17);
					Rect box = new Rect(local + offset - new Vector2(0,size.y*.5f), size);
					bool overlap = false; foreach (Rect used in _occupied) if (used.Overlaps(box)) { overlap = true; break; }
					if (overlap || box.xMin < bounds.rect.xMin || box.xMax > bounds.rect.xMax || box.yMin < bounds.rect.yMin || box.yMax > bounds.rect.yMax) continue;
					if (Ready)
					{
						Vector2 center = point + (offset + new Vector2(size.x * .5f, 0)) / _zoom;
						int x = Mathf.Clamp((int)((center.x / rect.width + .5f) * Width), 0, Width - 1);
						int y = Mathf.Clamp((int)((center.y / rect.height + .5f) * Height), 0, Height - 1);
						Color32 ground = _surface.Palette[y * Width + x];
						bool bright = ground.r * .299f + ground.g * .587f + ground.b * .114f > 150;
						label.Caption.color = bright ? new Color32(27, 38, 35, 255) : new Color32(249, 246, 226, 255);
					}
					label.Text.anchoredPosition = point + offset / _zoom; _occupied.Add(box); placed = true; break;
				}
				label.Text.gameObject.SetActive(placed);
			}
		}
		private void LayoutNation(MapLabel label, Rect rect, RectTransform bounds, Vector2 point, Vector2 local, bool visible)
		{
			TerritoryLabel territory = label.Territory;
			Vector2 preferred = label.BaseSize;
			float factor = Mathf.Min(Mathf.Min((float)territory.Width * rect.width * _zoom / preferred.x, (float)territory.Height * rect.height * _zoom / preferred.y), 48f / 22);
			Vector2 size = preferred * factor; float angle = (float)territory.Angle * Mathf.Deg2Rad;
			Vector2 extent = new Vector2(Mathf.Abs(Mathf.Cos(angle)) * size.x + Mathf.Abs(Mathf.Sin(angle)) * size.y, Mathf.Abs(Mathf.Sin(angle)) * size.x + Mathf.Abs(Mathf.Cos(angle)) * size.y);
			Rect box = new Rect(local - extent * .5f, extent);
			bool overlap = false; foreach (Rect used in _occupied) if (used.Overlaps(box)) { overlap = true; break; }
			bool show = territory.Valid && visible && factor * 22 >= 11 && !overlap && bounds.rect.Contains(box.min) && bounds.rect.Contains(box.max);
			label.Text.gameObject.SetActive(show); if (!show) return;
			label.Caption.fontSize = 22 * factor; label.Text.sizeDelta = size; label.Text.localScale = Vector3.one / _zoom;
			label.Text.localEulerAngles = new Vector3(0, 0, (float)territory.Angle); label.Text.anchoredPosition = point;
			int x = Mathf.Clamp((int)(territory.Center.X * Width), 0, Width - 1), y = Mathf.Clamp((int)(territory.Center.Y * Height), 0, Height - 1);
			Color32 ground = _surface.Palette[y * Width + x];
			bool bright = ground.r * .299f + ground.g * .587f + ground.b * .114f > 150;
			label.Caption.color = Color.Lerp(_surface.NationColors[label.Owner], bright ? (Color)new Color32(18, 27, 31, 255) : Color.white, bright ? .85f : .75f);
			_occupied.Add(box);
		}
		internal void ResetView() { _zoom = 1; transform.localScale = Vector3.one; ((RectTransform)transform).anchoredPosition = Vector2.zero; LayoutOverlays(); }
		public void OnScroll(PointerEventData data)
		{
			if (!Ready) return;
			float next = Mathf.Clamp(_zoom * Mathf.Pow(1.2f, data.scrollDelta.y), 1, 6), ratio = next / _zoom;
			var rect = (RectTransform)transform;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, data.position, data.pressEventCamera, out Vector2 focus)) rect.anchoredPosition = focus + (rect.anchoredPosition - focus) * ratio;
			_zoom = next; transform.localScale = Vector3.one * _zoom; ClampPan(); LayoutOverlays();
		}
		public void OnBeginDrag(PointerEventData data) { _dragged = false; _dragStart = data.position; _origin = ((RectTransform)transform).anchoredPosition; }
		public void OnDrag(PointerEventData data)
		{
			if (_zoom <= 1) return; _dragged = true;
			var parent = (RectTransform)transform.parent;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, _dragStart, data.pressEventCamera, out Vector2 start);
			RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, data.position, data.pressEventCamera, out Vector2 now);
			((RectTransform)transform).anchoredPosition = _origin + now - start; ClampPan(); LayoutOverlays();
		}
		public void OnEndDrag(PointerEventData data) { }
		private void ClampPan()
		{
			var rect = (RectTransform)transform; Rect bounds = ((RectTransform)transform.parent).rect;
			Vector2 limit = new Vector2(Mathf.Max(0,(rect.rect.width*_zoom-bounds.width)*.5f), Mathf.Max(0,(rect.rect.height*_zoom-bounds.height)*.5f));
			Vector2 at = rect.anchoredPosition; rect.anchoredPosition = new Vector2(Mathf.Clamp(at.x,-limit.x,limit.x),Mathf.Clamp(at.y,-limit.y,limit.y));
		}
		public void OnPointerClick(PointerEventData data)
		{
			if (!Ready || _dragged || data.button != PointerEventData.InputButton.Left) { _dragged = false; return; }
			var rect = (RectTransform)transform;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,data.position,data.pressEventCamera,out Vector2 point)) return;
			float u = (point.x-rect.rect.xMin)/rect.rect.width, v = (point.y-rect.rect.yMin)/rect.rect.height;
			if (u < 0 || u > 1 || v < 0 || v > 1) return;
			UnitVector location = UnitVector.FromMap(u*Math.PI*2-Math.PI,v*Math.PI-Math.PI/2);
			if (_surface.TryPick(location, out int selected)) _picked?.Invoke(selected);
		}
		private void OnRectTransformDimensionsChange() { LayoutOverlays(); }
		private void OnDestroy()
		{
			if (_surface != null) _surface.Changed -= SurfaceChanged;
			if (_labelMaterial != null) Destroy(_labelMaterial);
		}
	}
}
