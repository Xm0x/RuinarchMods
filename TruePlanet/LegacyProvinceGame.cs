using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Newtonsoft.Json;
using Perlin_Noise;
using Ruinarch.ModContent;
using UnityEngine;
using UtilityScripts;

namespace TruePlanet
{
	internal sealed class LegacyProvinceContext
	{
		public Planet Planet;
		public int ProvinceId;
	}
	internal static class LegacyProvinceGame
	{
		private const string SaveId = "trueplanet.province";
		internal static LegacyProvinceContext Active;
		private static LegacyProvinceContext _pending;
		private static InnerTileMap _map, _climateMap;
		private static float[,] _temperature, _moisture, _elevation;
		private static PlanetGeography _geography;
		internal static void Install(Harmony harmony)
		{
			ModSave.Register(SaveId, Save, Restore);
			foreach (Type type in typeof(LegacyProvinceGame).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
				if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
		}
		internal static void Clear()
		{
			Active = _pending = null; ClearTerrain();
		}
		private static void ClearTerrain() { _map = _climateMap = null; _geography = null; _temperature = _moisture = _elevation = null; }
		internal static void Play(Planet planet, int provinceId)
		{
			planet.Validate();
			if (planet.FormatVersion > 2 || provinceId < 0 || provinceId >= planet.Provinces.Length || !planet.Provinces[provinceId].Land)
				throw new InvalidOperationException("Select a land province to play.");
			Province province = planet.Provinces[provinceId];
			var settings = new WorldSettingsData(); settings.SetDefaultSettings(); settings.ApplyCustomWorldSettings();
			settings.mapSettings.SetMapSize(province.Size == ProvinceSize.Large ? MAP_SIZE.Large : MAP_SIZE.Extra_Large);
			foreach (IGrouping<int, Village> group in province.Villages.GroupBy(v => v.Nation))
			{
				// Independent villages are grouped by race, so native factions never mix races.
				foreach (IGrouping<bool, Village> race in group.GroupBy(v => v.Elven))
				{
					FactionTemplate template = settings.factionSettings.AddFactionSetting(0);
					template.SetFactionType(race.Key ? FACTION_TYPE.Elven_Kingdom : FACTION_TYPE.Human_Empire);
					Nation nation = group.Key >= 0 ? planet.Nations[group.Key] : null;
					template.ChangeName(nation != null ? nation.Name : province.Name + (race.Key ? " Elves" : " Humans"));
					if (FactionEmblemRandomizer.allEmblems == null || FactionEmblemRandomizer.allEmblems.Count == 0)
						throw new InvalidOperationException("Native faction emblems are unavailable.");
					int emblem = nation != null ? nation.Id : province.Id;
					template.SetFactionEmblem(FactionEmblemRandomizer.allEmblems[emblem % FactionEmblemRandomizer.allEmblems.Count]);
					foreach (Village village in race) template.AddVillageSetting(new VillageSetting { villageName = village.Name, villageSize = VILLAGE_SIZE.Small });
				}
			}
			if (!settings.AreSettingsValid(out string reason)) throw new InvalidOperationException(reason);
			_pending = new LegacyProvinceContext { Planet = planet, ProvinceId = provinceId }; Active = null; ClearTerrain();
			WorldSettings.Instance.SetWorldSettingsData(settings);
			SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(string.Empty);
			MainMenuManager.Instance.StartGame();
		}
		private static string Save()
		{
			if (Active == null) return null;
			Province province = Active.Planet.Provinces[Active.ProvinceId]; province.Visited = true;
			province.Held = PlayerManager.Instance != null && PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL) != null;
			Active.Planet.Validate(); return JsonConvert.SerializeObject(Active);
		}
		private static void Restore(string json)
		{
			Active = null; ClearTerrain();
			if (string.IsNullOrEmpty(json)) return;
			var saved = JsonConvert.DeserializeObject<LegacyProvinceContext>(json);
			if (saved == null || saved.Planet == null) throw new InvalidDataException("Missing TruePlanet save data.");
			saved.Planet.Validate();
			if (saved.Planet.FormatVersion > 2 || saved.ProvinceId < 0 || saved.ProvinceId >= saved.Planet.Provinces.Length || !saved.Planet.Provinces[saved.ProvinceId].Land)
				throw new InvalidDataException("Invalid saved TruePlanet province.");
			Active = saved; _pending = null;
		}
		private static bool Terrain(int width, int height)
		{
			if (Active == null || GridMap.Instance == null || GridMap.Instance.mainRegion == null) return false;
			InnerTileMap map = GridMap.Instance.mainRegion.innerMap;
			if (map == null || map.width != width || map.height != height) return false;
			if (_map == map && _temperature != null) return true;
			_map = map; _geography = new PlanetGeography(Active.Planet);
			_temperature = new float[width, height]; _moisture = new float[width, height]; _elevation = new float[width, height];
			Province province = Active.Planet.Provinces[Active.ProvinceId];
			for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
			{
				TerrainSample sample = _geography.Sample(_geography.LocalDirection(province, (x + .5) / width, (y + .5) / height));
				_temperature[x,y] = (float)sample.Temperature; _moisture[x,y] = (float)sample.Moisture;
				_elevation[x,y] = _geography.Elevation(sample);
			}
			return true;
		}
		private static IEnumerator Climate(IEnumerator original, InnerTileMap map)
		{
			try
			{
				while (true)
				{
					bool next; InnerTileMap previous = _climateMap;
					try { _climateMap = map; next = original.MoveNext(); }
					finally { _climateMap = previous; }
					if (!next) yield break;
					yield return original.Current;
				}
			}
			finally { (original as IDisposable)?.Dispose(); }
		}
		[HarmonyPatch(typeof(InnerTileMap), "BiomePerlin")]
		private static class ClimateScope
		{
			private static void Postfix(InnerTileMap __instance, ref IEnumerator __result)
			{
				if (Active != null && GridMap.Instance != null && GridMap.Instance.mainRegion != null
					&& ReferenceEquals(__instance, GridMap.Instance.mainRegion.innerMap)) __result = Climate(__result, __instance);
			}
		}
		[HarmonyPatch(typeof(AreaGeneration), nameof(AreaGeneration.ExecuteRandomGeneration))]
		private static class BeginGeneration
		{
			private static void Prefix()
			{
				if (_pending == null) return;
				Active = _pending; ClearTerrain(); UnityEngine.Random.InitState(unchecked(Active.Planet.Seed * 397 ^ Active.ProvinceId));
			}
		}
		[HarmonyPatch(typeof(RandomNameGenerator), nameof(RandomNameGenerator.GetRegionName))]
		private static class ProvinceName
		{
			private static bool Prefix(ref string __result)
			{
				if (Active == null || GridMap.Instance == null || GridMap.Instance.mainRegion != null) return true;
				__result = Active.Planet.Provinces[Active.ProvinceId].Name;
				return false;
			}
		}
		[HarmonyPatch(typeof(MapSettings), nameof(MapSettings.GetMapSize))]
		private static class ProvinceDimensions
		{
			private static void Postfix(MapSettings __instance, ref Vector2 __result)
			{
				if (Active != null && WorldSettings.Instance != null && ReferenceEquals(__instance, WorldSettings.Instance.worldSettingsData.mapSettings)
					&& Active.Planet.Provinces[Active.ProvinceId].Size == ProvinceSize.Huge) __result = new Vector2(32, 20);
			}
		}
		[HarmonyPatch(typeof(RegionInnerTileMap), nameof(RegionInnerTileMap.LoadMap))]
		private static class EarlyLoad
		{
			private static void Prefix()
			{
				string path = Path.Combine(Utilities.tempPath, "ModData", SaveId + ".json");
				Restore(File.Exists(path) ? File.ReadAllText(path) : null);
			}
		}
		[HarmonyPatch(typeof(Noise), nameof(Noise.GenerateTemperatureGradient))]
		private static class Temperature
		{
			private static bool Prefix(int p_width, int p_height, ref float[,] __result)
			{
				if (_climateMap == null || GridMap.Instance == null || GridMap.Instance.mainRegion == null
					|| !ReferenceEquals(_climateMap, GridMap.Instance.mainRegion.innerMap) || !Terrain(p_width, p_height)) return true;
				__result = _temperature; return false;
			}
		}
		// Native noise settings are copied structs; compare their values and region-array identity.
		private static bool SameNoise(in PerlinNoiseSettings a, in PerlinNoiseSettings b)
		{
			return a.noiseScale == b.noiseScale && a.octaves == b.octaves
				&& a.persistance == b.persistance && a.lacunarity == b.lacunarity && a.seed == b.seed
				&& a.offset.x == b.offset.x && a.offset.y == b.offset.y && ReferenceEquals(a.regions, b.regions);
		}
		[HarmonyPatch(typeof(Noise), nameof(Noise.GenerateNoiseMap))]
		private static class MoistureAndElevation
		{
			private static bool Prefix(PerlinNoiseSettings p_settings, int p_width, int p_height, ref float[,] __result)
			{
				if (Active == null || GridMap.Instance == null || GridMap.Instance.mainRegion == null) return true;
				InnerTileMap map = GridMap.Instance.mainRegion.innerMap;
				if (map == null) return true;
				bool precipitation = SameNoise(in p_settings, in map.precipitationPerlinSettings);
				if (!precipitation && !SameNoise(in p_settings, in map.elevationPerlinSettings)) return true;
				if (!Terrain(p_width, p_height)) return true;
				__result = precipitation ? _moisture : _elevation; return false;
			}
		}
		[HarmonyPatch(typeof(GameManager), "OnDestroy")]
		private static class EndWorld
		{
			private static void Prefix() { Active = null; ClearTerrain(); }
		}
	}
}
