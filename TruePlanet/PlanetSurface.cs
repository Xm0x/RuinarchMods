using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
		private sealed class PreparedAtlas
		{
			internal string Identity;
			internal int[] ProvinceIds;
			internal float[] Heights;
			internal Color32[] NationColors, Terrain, Political;
			internal List<MapSegment> Flat;
			internal List<SphericalSegment> Spherical;
		}
		// One immutable CPU atlas survives native scene changes; every view owns its GPU texture.
		private static PreparedAtlas _prepared;
		private string _identity;
		internal bool ReusedPreparation { get; private set; }

		internal void Invalidate() { Ready = false; Changed?.Invoke(); }
		internal void Show(Planet planet)
		{
			if (_building != null) StopCoroutine(_building);
			_building = null; Ready = false; Planet = planet;
			Shading = false; CompletedRows = 0; WorkStage = "Preparing the atlas";
			_displayedPalette = null; Changed?.Invoke();
			try
			{
				Geography = new PlanetGeography(planet);
				_identity = JsonConvert.SerializeObject(planet);
				ReusedPreparation = _prepared != null && _prepared.Identity == _identity;
				if (ReusedPreparation)
				{
					ProvinceIds = _prepared.ProvinceIds; Heights = _prepared.Heights; NationColors = _prepared.NationColors;
					_terrain = _prepared.Terrain; _political = _prepared.Political;
					FlatGeometry = _prepared.Flat; SphericalGeometry = _prepared.Spherical;
					EnsureTexture(); Upload(); Ready = true; CompletedRows = Height;
					Changed?.Invoke(); Progress?.Invoke(1); return;
				}
				_prepared = null;
				ProvinceIds = null; Heights = null; _terrain = _political = null;
			}
			catch (Exception error) { Failed?.Invoke(error); return; }
			Progress?.Invoke(0);
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
			bool continuousRelief = world.GeographyVersion >= 5;
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
					Color terrain = TerrainColor(sample, continuousRelief); int nation = best < 0 ? -1 : Planet.Provinces[best].Nation;
					Color politics = nation >= 0 ? NationColors[nation] : new Color32(156, 155, 137, 255);
					float land = (float)PlanetGeography.Clamp(sample.Height / .004);
					_terrain[at] = Color.Lerp(terrain, politics, land * (nation >= 0 ? .38f : .12f));
					_political[at] = Color.Lerp(terrain, politics, land * .8f);
				}
				if (y % 2 == 1) { CompletedRows = y + 1; Progress?.Invoke((float)(y + 1) / Height * .85f); yield return null; }
			}
			Shading = true; CompletedRows = 0; WorkStage = "Adding terrain detail";
            float[] rawHeights = Heights; bool[] deepWater = null;
            if (!continuousRelief)
            {
                Heights = FilterDeepWater(rawHeights, out deepWater);
                for (int y = 1; y < Height - 1; y++) for (int x = 0; x < Width; x++)
                {
                    int at = y * Width + x; if (!deepWater[at]) continue;
                    Color ocean = OceanColor(Heights[at]); _terrain[at] = ocean; _political[at] = ocean;
                }
            }
			for (int y = 1; y < Height - 1; y++)
			{
			for (int x = 0; x < Width; x++)
			{
				int at = y * Width + x;
                float[] heights = !continuousRelief && deepWater[at] ? Heights : rawHeights;
				float slope = (heights[y * Width + (x + 1) % Width] - heights[y * Width + (x + Width - 1) % Width])
					+ (heights[at + Width] - heights[at - Width]);
				float shade = Mathf.Clamp(1 - slope * 14, .78f, 1.12f);
                // Water has a level surface. Fade land relief in continuously across the shoreline.
                if (continuousRelief) shade = Mathf.Lerp(1, shade, Mathf.SmoothStep(0, 1, Mathf.Clamp01(rawHeights[at] / .006f)));
				Color t = _terrain[at]; t *= shade; t.a = 1; _terrain[at] = t;
				Color p = _political[at]; p *= shade; p.a = 1; _political[at] = p;
			}
				if (y % 8 == 0) { CompletedRows = y; Progress?.Invoke(.85f + (float)y / Height * .1f); yield return null; }
			}
			CompletedRows = Height; WorkStage = "Drawing borders and roads"; Progress?.Invoke(.95f);
			while (!geometryTask.IsCompleted) yield return null;
			if (geometryTask.IsFaulted) throw geometryTask.Exception.GetBaseException();
			FlatGeometry = geometryTask.Result.Item1; SphericalGeometry = geometryTask.Result.Item2;
			yield return null;
			EnsureTexture(); Upload(); Ready = true;
			_prepared = new PreparedAtlas
			{
				Identity = _identity, ProvinceIds = ProvinceIds, Heights = Heights, NationColors = NationColors,
				Terrain = _terrain, Political = _political, Flat = FlatGeometry, Spherical = SphericalGeometry
			};
			Changed?.Invoke(); Progress?.Invoke(1);
		}
		private void EnsureTexture()
		{
			if (Texture == null)
				Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
				{
					name = "TruePlanet world surface", filterMode = FilterMode.Bilinear,
					wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
				};
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
			if (!Ready) return false;
			TerrainSample terrain = Geography.Sample(direction);
			if (terrain.Height <= 0) return false;
			if (Planet.FormatVersion == 3)
			{
				id = Planet.Mesh.Locate(direction);
				if (Planet.LocalMapVersion >= 3 && !TileLand.For(Planet).Cells[id].Eligible) return false;
			}
			else
			{
				int province = Planet.Nearest(direction);
				if (!Planet.Provinces[province].Land) return false;
				id = province;
			}
			return true;
		}
        internal static float[] FilterDeepWater(float[] source, out bool[] deepWater)
        {
            int count = Width * Height;
            if (source == null || source.Length != count) throw new InvalidDataException("Unexpected atlas height dimensions.");
            var filtered = (float[])source.Clone(); deepWater = new bool[count]; var neighbors = new float[9];
            for (int y = 1; y < Height - 1; y++) for (int x = 0; x < Width; x++)
            {
                int at = y * Width + x; bool deep = true;
                for (int dy = -1; dy <= 1 && deep; dy++) for (int dx = -1; dx <= 1; dx++)
                    if (source[(y + dy) * Width + (x + dx + Width) % Width] >= -.006f) { deep = false; break; }
                if (!deep) continue;
                int index = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    neighbors[index++] = source[(y + dy) * Width + (x + dx + Width) % Width];
                for (int i = 1; i < neighbors.Length; i++)
                {
                    float value = neighbors[i]; int j = i;
                    while (j > 0 && neighbors[j - 1] > value) { neighbors[j] = neighbors[j - 1]; j--; }
                    neighbors[j] = value;
                }
                filtered[at] = neighbors[4]; deepWater[at] = true;
            }
            return filtered;
        }
        private static Color OceanColor(double height) => Color.Lerp(new Color32(18, 46, 65, 255), new Color32(53, 110, 125, 255),
            (float)Math.Exp(Math.Min(0, height) * 8));
		private static Color TerrainColor(TerrainSample sample, bool continuousRelief)
		{
			Color ocean = OceanColor(sample.Height);
			Color land = Color.Lerp(new Color32(195, 167, 112, 255), new Color32(120, 144, 84, 255), (float)PlanetGeography.Clamp((sample.Moisture - .2) * 3));
			land = Color.Lerp(land, new Color32(47, 94, 65, 255), (float)PlanetGeography.Clamp((sample.Moisture - .5) * 3));
			double rock = continuousRelief ? (sample.Mountains - .35) * 3 + Math.Max(0, sample.Height - .5)
				: sample.Mountains * 1.3 + Math.Max(0, sample.Height - .3);
			land = Color.Lerp(land, new Color32(145, 145, 133, 255), (float)PlanetGeography.Clamp(rock));
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
