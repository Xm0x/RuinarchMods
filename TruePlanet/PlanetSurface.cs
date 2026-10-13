using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TruePlanet
{
	internal sealed class PlanetSurface : MonoBehaviour
	{
		internal const int Width = 1536, Height = 768;
		internal Planet Planet { get; private set; }
		internal PlanetGeography Geography { get; private set; }
		internal Texture2D Texture { get; private set; }
		internal bool Ready { get; private set; }
		internal int[] ProvinceIds { get; private set; }
		internal float[] Heights { get; private set; }
		internal Color32[] NationColors { get; private set; }
		internal Color32[] Palette => _politicalView ? _political : _terrain;
		internal Action<float> Progress;
		internal event Action Changed;
		internal event Action<Exception> Failed;
		private Color32[] _terrain, _political, _displayedPalette;
		private bool _politicalView;
		private Coroutine _building;
		internal bool Shading { get; private set; }
		internal int CompletedRows { get; private set; }
		internal string WorkStage { get; private set; }
		internal List<MapSegment> FlatGeometry { get; private set; }
		internal List<SphericalSegment> SphericalGeometry { get; private set; }

		internal void Invalidate() { Ready = false; Changed?.Invoke(); }
		internal void Show(Planet planet)
		{
			if (_building != null) StopCoroutine(_building);
			_building = null; Ready = false; Planet = planet;
			Shading = false; CompletedRows = 0; WorkStage = "Drawing atlas terrain";
			_displayedPalette = null; Progress?.Invoke(0); Changed?.Invoke();
			try { Geography = new PlanetGeography(planet); }
			catch (Exception error) { Failed?.Invoke(error); return; }
			_building = StartCoroutine(GuardBuild(Build()));
		}

		private IEnumerator GuardBuild(IEnumerator build)
		{
			while (true)
			{
				bool next = false; object current = null; Exception failure = null;
				try { next = build.MoveNext(); if (next) current = build.Current; }
				catch (Exception error) { failure = error; }
				if (failure != null)
				{
					(build as IDisposable)?.Dispose(); _building = null; Ready = false;
					Failed?.Invoke(failure); yield break;
				}
				if (!next) { (build as IDisposable)?.Dispose(); _building = null; yield break; }
				yield return current;
			}
		}

		private IEnumerator Build()
		{
			Planet world = Planet; PlanetGeography geometryGeography = Geography;
			var geometryTask = Task.Run(() => new Tuple<List<MapSegment>, List<SphericalSegment>>(
				MapGeometry.Build(world, geometryGeography), MapGeometry.BuildSpherical(world, geometryGeography)));
			int count = Width * Height;
			if (ProvinceIds == null)
			{
				ProvinceIds = new int[count]; Heights = new float[count];
				_terrain = new Color32[count]; _political = new Color32[count];
			}
			NationColors = new Color32[Planet.Nations.Length];
			foreach (Nation nation in Planet.Nations) NationColors[nation.Id] = Color.HSVToRGB((float)nation.Hue, .62f + nation.Id % 3 * .08f, .86f - nation.Id % 2 * .12f);
			var geography = Geography; var longitudeX = new double[Width]; var longitudeZ = new double[Width];
			for (int x = 0; x < Width; x++)
			{
				double longitude = (x + .5) / Width * Math.PI * 2 - Math.PI;
				longitudeX[x] = Math.Cos(longitude); longitudeZ[x] = Math.Sin(longitude);
			}
			for (int y = 0; y < Height; y++)
			{
				double latitude = (y + .5) / Height * Math.PI - Math.PI / 2;
				double radius = Math.Cos(latitude), latitudeY = Math.Sin(latitude);
				for (int x = 0; x < Width; x++)
				{
					UnitVector point = new UnitVector(radius * longitudeX[x], latitudeY, radius * longitudeZ[x]);
					int at = y * Width + x;
					TerrainSample sample = geography.Sample(point); Heights[at] = (float)sample.Height;
					int best = Planet.Nearest(point); ProvinceIds[at] = best;
					Color terrain = TerrainColor(sample); int nation = best < 0 ? -1 : Planet.Provinces[best].Nation;
					Color politics = nation >= 0 ? NationColors[nation] : new Color32(156, 155, 137, 255);
					float land = (float)PlanetGeography.Clamp(sample.Height / .004);
					_terrain[at] = Color.Lerp(terrain, politics, land * (nation >= 0 ? .38f : .12f));
					_political[at] = Color.Lerp(terrain, politics, land * .8f);
				}
				if (y % 2 == 1) { CompletedRows = y + 1; Progress?.Invoke((float)(y + 1) / Height * .85f); yield return null; }
			}
			Shading = true; CompletedRows = 0; WorkStage = "Shading atlas terrain";
			for (int y = 1; y < Height - 1; y++)
			{
			for (int x = 0; x < Width; x++)
			{
				int at = y * Width + x;
				float slope = (Heights[y * Width + (x + 1) % Width] - Heights[y * Width + (x + Width - 1) % Width])
					+ (Heights[at + Width] - Heights[at - Width]);
				float shade = Mathf.Clamp(1 - slope * 14, .78f, 1.12f);
				Color t = _terrain[at]; t *= shade; t.a = 1; _terrain[at] = t;
				Color p = _political[at]; p *= shade; p.a = 1; _political[at] = p;
			}
				if (y % 8 == 0) { CompletedRows = y; Progress?.Invoke(.85f + (float)y / Height * .1f); yield return null; }
			}
			CompletedRows = Height; WorkStage = "Preparing borders, roads and atlas texture"; Progress?.Invoke(.95f);
			while (!geometryTask.IsCompleted) yield return null;
			if (geometryTask.IsFaulted) throw geometryTask.Exception.GetBaseException();
			FlatGeometry = geometryTask.Result.Item1; SphericalGeometry = geometryTask.Result.Item2;
			yield return null;
			if (Texture == null)
				Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
				{
					name = "TruePlanet world surface", filterMode = FilterMode.Bilinear,
					wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
				};
			Upload(); Ready = true; Changed?.Invoke(); Progress?.Invoke(1);
		}

		internal void SetPolitical(bool political)
		{
			if (_politicalView == political) return;
			_politicalView = political;
			if (!Ready) return;
			Upload(); Changed?.Invoke();
		}
		private void Upload()
		{
			Color32[] palette = Palette;
			if (ReferenceEquals(palette, _displayedPalette)) return;
			Texture.SetPixels32(palette); Texture.Apply(false, false); _displayedPalette = palette;
		}
		internal bool TryPick(UnitVector direction, out int id)
		{
			id = -1;
			if (!Ready || Geography.Sample(direction).Height <= 0) return false;
			if (Planet.FormatVersion == 3) id = Planet.Mesh.Locate(direction);
			else
			{
				int province = Planet.Nearest(direction);
				if (!Planet.Provinces[province].Land) return false;
				id = province;
			}
			return true;
		}
		private static Color TerrainColor(TerrainSample sample)
		{
			Color ocean = Color.Lerp(new Color32(18, 46, 65, 255), new Color32(53, 110, 125, 255), (float)Math.Exp(Math.Min(0, sample.Height) * 8));
			Color land = Color.Lerp(new Color32(195, 167, 112, 255), new Color32(120, 144, 84, 255), (float)PlanetGeography.Clamp((sample.Moisture - .2) * 3));
			land = Color.Lerp(land, new Color32(47, 94, 65, 255), (float)PlanetGeography.Clamp((sample.Moisture - .5) * 3));
			land = Color.Lerp(land, new Color32(145, 145, 133, 255), (float)PlanetGeography.Clamp(sample.Mountains * 1.3 + Math.Max(0, sample.Height - .3)));
			land = Color.Lerp(land, new Color32(231, 233, 222, 255), (float)PlanetGeography.Clamp((.26 - sample.Temperature) * 12));
			return Color.Lerp(ocean, land, (float)PlanetGeography.Clamp(sample.Height / .006 + .5));
		}
		private void OnDestroy()
		{
			if (_building != null) StopCoroutine(_building);
			if (Texture != null) Destroy(Texture);
		}
	}
}
