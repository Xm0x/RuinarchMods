using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TruePlanet
{
	public sealed class PlanetCanvas : MonoBehaviour, IPointerClickHandler
	{
		internal const int Width = 768, Height = 384;
		internal Planet Planet;
		internal Action<int> Picked;
		internal bool Political, Borders = true, Roads = true;
		internal int Selected = -1;
		private int[] _ids;
		private Color32[] _terrain, _political, _pixels;
		private Texture2D _texture;
		private Coroutine _building;
		internal bool Ready => _building == null && _ids != null;
		internal void Show(Planet planet)
		{
			if (_building != null) StopCoroutine(_building);
			Planet = planet; Selected = -1; _ids = null;
			_building = StartCoroutine(Build());
		}
		private IEnumerator Build()
		{
			_ids = new int[Width * Height]; _pixels = new Color32[_ids.Length];
			_terrain = new Color32[Planet.Provinces.Length]; _political = new Color32[_terrain.Length];
			Color grass = new Color32(110, 136, 85, 255), forest = new Color32(42, 89, 70, 255), desert = new Color32(177, 146, 87, 255), snow = new Color32(224, 227, 211, 255), rock = new Color32(103, 113, 118, 255);
			foreach (Province p in Planet.Provinces)
			{
				Color terrain;
				if (!p.Land) terrain = Color.Lerp(new Color32(16, 42, 61, 255), new Color32(37, 84, 104, 255), (float)Math.Exp(p.Height * 2));
				else terrain = Color.Lerp(grass * (float)p.Biomes[0] + forest * (float)p.Biomes[1] + desert * (float)p.Biomes[2] + snow * (float)p.Biomes[3], rock, (float)p.Mountains);
				terrain.a = 1; _terrain[p.Id] = terrain;
				_political[p.Id] = !p.Land ? terrain : p.Nation < 0 ? new Color32(137, 137, 122, 255) : (Color32)Color.HSVToRGB((float)Planet.Nations[p.Nation].Hue, .45f, .78f);
			}
			var longitude = new double[Width];
			for (int x = 0; x < Width; x++) longitude[x] = (x + .5) / Width * Math.PI * 2 - Math.PI;
			for (int y = 0; y < Height; y++)
			{
				double latitude = (y + .5) / Height * Math.PI - Math.PI / 2;
				for (int x = 0; x < Width; x++) _ids[y * Width + x] = Planet.Nearest(UnitVector.FromMap(longitude[x], latitude));
				if (y % 16 == 15) yield return null;
			}
			if (_texture == null)
			{
				_texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "TruePlanet atlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
				GetComponent<RawImage>().texture = _texture;
			}
			_building = null; Redraw();
		}
		internal void Redraw()
		{
			if (!Ready) return;
			Color32[] palette = Political ? _political : _terrain;
			for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
			{
				int at = y * Width + x, id = _ids[at], right = _ids[y * Width + (x + 1) % Width], up = y + 1 < Height ? _ids[at + Width] : id;
				Color32 color = palette[id];
				if (Borders && (id != right || id != up)) color = new Color32(38, 51, 53, 255);
				if (id == Selected && (id != right || id != up)) color = new Color32(245, 209, 106, 255);
				_pixels[at] = color;
			}
			if (Roads) foreach (Road road in Planet.Roads)
				for (int i = 1; i < road.Path.Length; i++) Route(Planet.Provinces[road.Path[i - 1]].Direction, Planet.Provinces[road.Path[i]].Direction);
			foreach (Nation nation in Planet.Nations) Marker(Planet.Provinces[nation.Capital].Direction, new Color32(246, 215, 132, 255), 3);
			if (Selected >= 0) Marker(Planet.Provinces[Selected].Direction, new Color32(255, 244, 207, 255), 4);
			_texture.SetPixels32(_pixels); _texture.Apply(false, false);
		}
		private static Vector2Int Pixel(UnitVector point)
		{
			return new Vector2Int((int)((Math.Atan2(point.Z, point.X) + Math.PI) / (2 * Math.PI) * Width) % Width,
				Math.Min(Height - 1, (int)((Math.Asin(Math.Max(-1, Math.Min(1, point.Y))) + Math.PI / 2) / Math.PI * Height)));
		}
		private void Ink(int x, int y, Color32 color) { if (y >= 0 && y < Height) _pixels[y * Width + (x % Width + Width) % Width] = color; }
		private void Route(UnitVector a, UnitVector b)
		{
			Vector2Int start = Pixel(a), end = Pixel(b);
			int dx = Math.Abs(start.x - end.x); dx = Math.Min(dx, Width - dx);
			int steps = Math.Max(dx, Math.Abs(start.y - end.y)) * 3 + 1;
			for (int i = 0; i <= steps; i++)
			{
				double t = (double)i / steps; Vector2Int point = Pixel((a * (1 - t) + b * t).Normalized());
				Ink(point.x, point.y, new Color32(196, 163, 99, 255));
			}
		}
		private void Marker(UnitVector direction, Color32 color, int radius)
		{
			Vector2Int p = Pixel(direction);
			for (int y = -radius; y <= radius; y++) for (int x = -radius; x <= radius; x++)
				if (Math.Abs(x) + Math.Abs(y) == radius) Ink(p.x + x, p.y + y, color);
		}
		public void OnPointerClick(PointerEventData data)
		{
			if (!Ready || data.button != PointerEventData.InputButton.Left) return;
			var rect = (RectTransform)transform;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out Vector2 point)) return;
			float u = (point.x - rect.rect.xMin) / rect.rect.width, v = (point.y - rect.rect.yMin) / rect.rect.height;
			if (u < 0 || u > 1 || v < 0 || v > 1) return;
			Selected = Planet.Nearest(UnitVector.FromMap(u * Math.PI * 2 - Math.PI, v * Math.PI - Math.PI / 2));
			Redraw(); Picked?.Invoke(Selected);
		}
		private void OnDestroy() { if (_texture != null) Destroy(_texture); }
	}
}
