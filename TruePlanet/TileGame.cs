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
using UnityEngine.Tilemaps;

namespace TruePlanet
{
	internal sealed class TileContext
	{
		public int LocalMapVersion = 1;
		public Planet Planet;
		public int TileId;
		public string CampaignId;
		public string CampaignRevision;
		// Native saves omit this logical state and recompute it from wall sprites.
		public byte[] GroundTypes;
		// Same-type template floors may remain native-default and lose their TileBase.
		public Dictionary<int, string> DefaultGroundAssets;
	}
	internal static class TileGame
	{
		internal const string SaveId = "trueplanet.tile";
		internal static TileContext Active;
		private static TileContext _pending;
		private static string _restoredJson;
		private static InnerTileMap _map, _climateMap;
		private static PlanetGeography _geography;
		private static float[,] _temperature, _moisture, _elevation;
		private static readonly JsonSerializerSettings Json = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
		private static TileStore _store;
		private static CampaignManifest _campaign;
		internal static string NativeSaveError;
		internal static TileStore Store { get { EnsureCampaign(); return _store; } }
		internal static CampaignManifest Campaign { get { EnsureCampaign(); return _campaign; } }
		private static void EnsureCampaign()
		{
			if (Active == null) throw new InvalidOperationException("No geographic tile is active.");
			if (_store != null) return;
			var store = new TileStore(TruePlanetMod.SaveRoot, Active.Planet);
			CampaignManifest campaign = Active.CampaignRevision == null
				? new CampaignManifest { CampaignId = Active.CampaignId, WorldId = Active.Planet.Id, ActiveTile = Active.TileId, Carry = new CampaignCarry() }
				: store.Read(Active.CampaignId, Active.CampaignRevision);
			if (campaign.ActiveTile != Active.TileId) throw new InvalidDataException("Native tile and campaign revision disagree.");
			_store = store; _campaign = campaign;
		}
		internal static void CommitCampaign(CampaignManifest campaign)
		{
			EnsureCampaign();
			if (campaign.CampaignId != Active.CampaignId || campaign.WorldId != Active.Planet.Id || campaign.ActiveTile != Active.TileId)
				throw new InvalidDataException("Campaign commit does not match the active native world.");
			_store.Commit(campaign); _campaign = campaign; Active.CampaignRevision = campaign.Revision;
		}

		internal static void Install(Harmony harmony)
		{
			ModSave.Register(SaveId, Save, Restore);
			foreach (Type type in typeof(TileGame).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
				if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
		}
		internal static Vector2Int Areas(LocalMapSize size)
		{
			PlanetOptions.NativeDimensions(size, out int columns, out int rows);
			return new Vector2Int(columns, rows);
		}
		internal static void Clear()
		{
			Active = _pending = null; _restoredJson = null; ClearTerrain();
			_store = null; _campaign = null; NativeSaveError = null;
		}
		private static void ClearTerrain()
		{
			_map = _climateMap = null; _geography = null; _temperature = _moisture = _elevation = null;
		}
		internal static void Play(Planet planet, int tileId, string campaignId = null)
		{
			planet.Validate();
			if (planet.FormatVersion != 3 || (uint)tileId >= planet.Mesh.Centers.Length || !TileLand.For(planet).Cells[tileId].Eligible)
				throw new InvalidOperationException("Select a dry geographic tile to play.");
			var settings = new WorldSettingsData(); settings.SetDefaultSettings(); settings.ApplyCustomWorldSettings();
			settings.mapSettings.SetMapSize(planet.Options.LocalSize == LocalMapSize.Huge ? MAP_SIZE.Extra_Large : (MAP_SIZE)planet.Options.LocalSize);
			foreach (var group in planet.Provinces.SelectMany(p => p.Villages).Where(v => v.TileId == tileId).GroupBy(v => new { v.Nation, v.Elven }))
			{
				FactionTemplate template = settings.factionSettings.AddFactionSetting(0);
				template.SetFactionType(group.Key.Elven ? FACTION_TYPE.Elven_Kingdom : FACTION_TYPE.Human_Empire);
				Nation nation = planet.Nations[group.Key.Nation]; template.ChangeName(nation.Name);
				if (FactionEmblemRandomizer.allEmblems == null || FactionEmblemRandomizer.allEmblems.Count == 0)
					throw new InvalidOperationException("Native faction emblems are unavailable.");
				template.SetFactionEmblem(FactionEmblemRandomizer.allEmblems[nation.Id % FactionEmblemRandomizer.allEmblems.Count]);
				foreach (Village village in group) template.AddVillageSetting(new VillageSetting { villageName = village.Name, villageSize = VILLAGE_SIZE.Small });
			}
			if (!settings.AreSettingsValid(out string reason)) throw new InvalidOperationException(reason);
			_pending = new TileContext { Planet = planet, TileId = tileId, CampaignId = campaignId ?? Guid.NewGuid().ToString("N") };
			Active = null; _restoredJson = null; ClearTerrain();
			_store = null; _campaign = null;
			WorldSettings.Instance.SetWorldSettingsData(settings);
			SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(string.Empty);
			if (MainMenuManager.Instance != null) MainMenuManager.Instance.StartGame();
			else UIManager.Instance.optionsMenu.LoadSave();
		}
		internal static string Save()
		{
			if (Active == null) return null;
			InnerTileMap map = GridMap.Instance.mainRegion.innerMap; Vector2Int areas = Areas(Active.Planet.Options.LocalSize);
			if (map.width != areas.x * 14 || map.height != areas.y * 14) throw new InvalidDataException("Active native map dimensions disagree with its tile context.");
			var types = new byte[map.width * map.height];
			Dictionary<int, string> defaults = null;
			var assets = InnerMapManager.Instance.assetManager;
			for (int x = 0; x < map.width; x++) for (int y = 0; y < map.height; y++)
			{
				LocationGridTile tile = map.map[x, y]; int index = x * map.height + y;
				types[index] = checked((byte)tile.groundType);
				if (!tile.isDefault) continue;
				var actual = map.groundTilemap.GetTile(tile.localPlace);
				if (actual == assets.GetGroundAssetForTile(tile)) continue;
				if (defaults == null) defaults = new Dictionary<int, string>();
				defaults.Add(index, actual == null ? null : actual.name);
			}
			Active.GroundTypes = types; Active.DefaultGroundAssets = defaults;
			try
			{
				EnsureCampaign();
				_campaign.Carry = CampaignCarry.Capture(); _campaign.ActiveTile = Active.TileId;
				_store.RecordRevision(_campaign); Active.CampaignRevision = _campaign.Revision;
			}
			catch (Exception error) { NativeSaveError = error.Message; throw; }
			return JsonConvert.SerializeObject(Active, Json);
		}
		internal static void Restore(string json)
		{
			if (json != null && string.Equals(json, _restoredJson, StringComparison.Ordinal)) return;
			Active = null; _restoredJson = null; ClearTerrain();
			_store = null; _campaign = null;
			if (string.IsNullOrEmpty(json)) return;
			TileContext saved = JsonConvert.DeserializeObject<TileContext>(json, Json);
			if (saved == null || saved.Planet == null || saved.LocalMapVersion != 1 || saved.Planet.FormatVersion != 3)
				throw new InvalidDataException("Unsupported native geographic tile context.");
			saved.Planet.Validate();
			if ((uint)saved.TileId >= saved.Planet.Mesh.Centers.Length || !TileLand.For(saved.Planet).Cells[saved.TileId].Eligible
				|| saved.CampaignId == null || saved.CampaignId.Length != 32 || saved.CampaignId.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
				throw new InvalidDataException("Invalid saved tile or campaign identity.");
			Vector2Int size = Areas(saved.Planet.Options.LocalSize);
			if (saved.GroundTypes == null || saved.GroundTypes.Length != size.x * size.y * 196
				|| saved.GroundTypes.Any(value => value > (byte)LocationGridTile.Ground_Type.Water_Shore))
				throw new InvalidDataException("Invalid saved tile ground classifications.");
			if (saved.DefaultGroundAssets != null)
				foreach (var item in saved.DefaultGroundAssets)
					if ((uint)item.Key >= saved.GroundTypes.Length || item.Value != null && string.IsNullOrWhiteSpace(item.Value))
						throw new InvalidDataException("Invalid saved default ground asset.");
			Active = saved; _pending = null; _restoredJson = json;
			try { if (saved.CampaignRevision != null) EnsureCampaign(); }
			catch { Clear(); throw; }
		}
		private static bool Terrain(int width, int height)
		{
			if (Active == null || GridMap.Instance == null || GridMap.Instance.mainRegion == null) return false;
			InnerTileMap map = GridMap.Instance.mainRegion.innerMap;
			if (map == null || map.width != width || map.height != height) return false;
			if (_map == map && _temperature != null) return true;
			_map = map; _geography = new PlanetGeography(Active.Planet);
			_temperature = new float[width, height]; _moisture = new float[width, height]; _elevation = new float[width, height];
			LocalTileFootprint footprint = _geography.TileFootprint(Active.TileId);
			for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
			{
				TerrainSample sample = _geography.Sample(footprint.Direction((x + .5) / width, (y + .5) / height));
				_temperature[x, y] = (float)sample.Temperature; _moisture[x, y] = (float)sample.Moisture; _elevation[x, y] = _geography.Elevation(sample);
			}
			return true;
		}
		private static IEnumerator Climate(IEnumerator original, InnerTileMap map)
		{
			try
			{
				while (true)
				{
					InnerTileMap previous = _climateMap; bool next;
					try { _climateMap = map; next = original.MoveNext(); } finally { _climateMap = previous; }
					if (!next) yield break; yield return original.Current;
				}
			}
			finally { (original as IDisposable)?.Dispose(); }
		}
		private static bool SameNoise(in PerlinNoiseSettings a, in PerlinNoiseSettings b) => a.noiseScale == b.noiseScale && a.octaves == b.octaves
			&& a.persistance == b.persistance && a.lacunarity == b.lacunarity && a.seed == b.seed && a.offset.x == b.offset.x && a.offset.y == b.offset.y && ReferenceEquals(a.regions, b.regions);
		[HarmonyPatch(typeof(AreaGeneration), nameof(AreaGeneration.ExecuteRandomGeneration))]
		private static class BeginGeneration
		{
			private static void Prefix()
			{
				if (_pending == null) return;
				Active = _pending; _pending = null; ClearTerrain();
				UnityEngine.Random.InitState(new PlanetGeography(Active.Planet).NativeTileSeed(Active.TileId));
			}
		}
		[HarmonyPatch(typeof(MapSettings), nameof(MapSettings.GetMapSize))]
		private static class Dimensions
		{
			private static void Postfix(MapSettings __instance, ref Vector2 __result)
			{
				if (Active != null && WorldSettings.Instance != null && ReferenceEquals(__instance, WorldSettings.Instance.worldSettingsData.mapSettings))
					__result = Areas(Active.Planet.Options.LocalSize);
			}
		}
		[HarmonyPatch(typeof(RegionInnerTileMap), nameof(RegionInnerTileMap.GenerateMap))]
		private static class PrepareGenerationColliders
		{
			private static void Prefix(RegionInnerTileMap __instance) { ConfigureTerrainColliders(__instance); }
		}
		[HarmonyPatch(typeof(RegionInnerTileMap), nameof(RegionInnerTileMap.LoadMap))]
		private static class EarlyLoad
		{
			private static void Prefix(RegionInnerTileMap __instance)
			{
				string path = Path.Combine(Utilities.tempPath, "ModData", SaveId + ".json");
				if (File.Exists(path) && File.Exists(Path.Combine(Utilities.tempPath, "ModData", "trueplanet.province.json")))
					throw new InvalidDataException("The native save contains both geographic tile and legacy province contexts.");
				Restore(File.Exists(path) ? File.ReadAllText(path) : null);
				ConfigureTerrainColliders(__instance);
			}
		}
		private static readonly List<TilemapCollider2D> TerrainColliders = new List<TilemapCollider2D>(2);
		private static void ConfigureTerrainColliders(InnerTileMap map)
		{
			if (Active == null) return;
			Vector2Int areas = Areas(Active.Planet.Options.LocalSize);
			map.tilemapsParent.GetComponentsInChildren(true, TerrainColliders);
			try
			{
				foreach (TilemapCollider2D collider in TerrainColliders)
				{
					collider.maximumTileChangeCount = checked((uint)(areas.x * areas.y * 196));
					Rigidbody2D body = collider.GetComponent<Rigidbody2D>();
					if (body == null)
					{
						if (collider.attachedRigidbody != null) throw new InvalidOperationException("Unsupported shared terrain Rigidbody2D.");
						body = collider.gameObject.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static;
					}
					if (body.bodyType != RigidbodyType2D.Static || !body.simulated || collider.attachedRigidbody != body)
						throw new InvalidOperationException("Terrain static body attachment failed.");
				}
			}
			finally { TerrainColliders.Clear(); }
		}
		[HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadProgression))]
		private static class RestoreGround
		{
			private static void Prefix()
			{
				if (Active == null) return;
				InnerTileMap map = GridMap.Instance.mainRegion.innerMap;
				if (Active.GroundTypes == null || Active.GroundTypes.Length != map.width * map.height)
					throw new InvalidDataException("Loaded ground classifications disagree with native dimensions.");
				if (Active.DefaultGroundAssets != null)
				{
					var assets = InnerMapManager.Instance.assetManager; var database = assets.GetFloorAndWallTileAssetDB();
					foreach (var item in Active.DefaultGroundAssets)
					{
						LocationGridTile tile = map.map[item.Key / map.height, item.Key % map.height];
						if (!tile.isDefault) throw new InvalidDataException("A saved default ground asset refers to a native non-default tile.");
						UnityEngine.Tilemaps.TileBase asset = null;
						if (item.Value != null && !database.TryGetValue(item.Value, out asset))
							throw new InvalidDataException("Saved ground asset is unavailable: " + item.Value);
						// Restore the visual without changing native save inclusion or logical state.
						tile.SetGroundTilemapTileAsset(asset);
					}
				}
				// Apply before gameplay starts: native setters cannot schedule new snow timers.
				for (int x = 0; x < map.width; x++) for (int y = 0; y < map.height; y++)
				{
					LocationGridTile tile = map.map[x, y]; var type = (LocationGridTile.Ground_Type)Active.GroundTypes[x * map.height + y];
					if (tile.groundType != type) tile.SetGroundType(type, isInitial: true);
				}
			}
            private static void Postfix()
            {
                // Native currency loading drops SpiritEnergy outside Spirit mode.
                // Restore only this saved counter, not destination-local skill state.
                if (Active != null && Active.CampaignRevision != null) Campaign.Carry.RestoreSpiritEnergy();
            }
		}
		[HarmonyPatch(typeof(Quests.Eradication), "CheckForGameWin")]
		private static class WildernessObjective
		{
			private static bool Prefix() => Active == null || Active.Planet.Provinces.Any(p => p.Villages.Any(v => v.TileId == Active.TileId));
		}
		[HarmonyPatch(typeof(InnerTileMap), "BiomePerlin")]
		private static class ClimateScope
		{
			private static void Postfix(InnerTileMap __instance, ref IEnumerator __result)
			{
				if (Active != null && GridMap.Instance != null && GridMap.Instance.mainRegion != null && ReferenceEquals(__instance, GridMap.Instance.mainRegion.innerMap))
					__result = Climate(__result, __instance);
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
		[HarmonyPatch(typeof(Noise), nameof(Noise.GenerateNoiseMap))]
		private static class MoistureAndElevation
		{
			private static bool Prefix(PerlinNoiseSettings p_settings, int p_width, int p_height, ref float[,] __result)
			{
				if (Active == null || GridMap.Instance == null || GridMap.Instance.mainRegion == null) return true;
				InnerTileMap map = GridMap.Instance.mainRegion.innerMap; if (map == null) return true;
				bool precipitation = SameNoise(in p_settings, in map.precipitationPerlinSettings);
				if (!precipitation && !SameNoise(in p_settings, in map.elevationPerlinSettings) || !Terrain(p_width, p_height)) return true;
				__result = precipitation ? _moisture : _elevation; return false;
			}
		}
		[HarmonyPatch(typeof(GameManager), "OnDestroy")]
		private static class EndWorld
		{
			private static void Prefix() { Active = null; _restoredJson = null; ClearTerrain(); }
		}
	}
}
