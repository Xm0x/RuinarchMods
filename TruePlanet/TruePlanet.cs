using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Ruinarch.Modding;
using UnityEngine;
using UtilityScripts;

namespace TruePlanet
{
	[ModSettings("TruePlanet")]
	public sealed class PlanetSettings
	{
		[Setting("Provinces", "Number of provinces in a new atlas."), Range(30, 400)] public int provinces = 120;
		[Setting("Land percent", "Share of the planet covered by land."), Range(20, 80)] public int landPercent = 40;
		[Setting("Nations", "Number of nations, limited by inhabited land."), Range(2, 40)] public int nations = 10;
		[Setting("Wild land percent", "Share of sampled dry land left outside national claims."), Range(0, 75)] public int wildPercent = 25;
		[Setting("Local map size", "Fixed playable tile dimensions for a new world.")] public LocalMapSize localSize = LocalMapSize.Large;
		[Setting("Tile detail", "Geographic cell detail; does not enlarge native maps.")] public TileResolution resolution = TileResolution.Standard;
		internal PlanetOptions Options() => new PlanetOptions { Provinces = provinces, LandShare = landPercent / 100.0, Nations = nations, WildShare = wildPercent / 100.0, LocalSize = localSize, Resolution = resolution };
	}
	public sealed class TruePlanetMod : IRuinarchMod
	{
		internal static ModLogger Log;
		internal static PlanetSettings Settings;
		internal static string SaveRoot => Path.Combine(Utilities.gameSavePath, "TruePlanet");
		public void OnLoad(ModContext context)
		{
			Log = context.Logger; Settings = context.Settings.Register<PlanetSettings>();
			var harmony = new Harmony(context.Info.id);
			harmony.CreateClassProcessor(typeof(PlanetEntry)).Patch();
			harmony.CreateClassProcessor(typeof(PlanetMenuReset)).Patch();
			LegacyProvinceGame.Install(harmony);
			TileGame.Install(harmony);
			TileTravel.Install(harmony);
			AtlasInputScope.Install(harmony);
			CampaignObjective.Install(harmony);
			Log.Info("TruePlanet installed: New Game opens geographic world generation; dry tiles support native play and campaign travel.");
		}
		internal static void Name(Planet planet)
		{
			string[] regions = Names("baseRegionNames"), human = Names("baseHumanKingdomNames"), elven = Names("baseElvenKingdomNames");
			var random = new System.Random(planet.Seed ^ 0x517cc1b7); var used = new HashSet<string>(StringComparer.Ordinal);
			string Unique(string value)
			{
				string candidate = value; int suffix = 2;
				while (!used.Add(candidate)) candidate = value + " " + suffix++;
				return candidate;
			}
			planet.Name = Unique(regions[random.Next(regions.Length)]) + " World";
			foreach (Nation nation in planet.Nations) { string[] pool = nation.Elven ? elven : human; nation.Name = Unique(pool[random.Next(pool.Length)]); }
			foreach (Province province in planet.Provinces)
			{
				province.Name = Unique(regions[random.Next(regions.Length)]);
				foreach (Village village in province.Villages)
				{
					string[] pool = village.Elven ? elven : human;
					village.Name = Unique(pool[random.Next(pool.Length)]);
				}
			}
			planet.Validate();
		}
		private static string[] Names(string field)
		{
			var values = AccessTools.Field(typeof(RandomNameGenerator), field)?.GetValue(null) as string[];
			if (values == null || values.Length == 0) throw new InvalidDataException("The game's name table is unavailable: " + field);
			return values;
		}
	}
	[HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.OnClickPlayGame))]
	internal static class PlanetEntry
	{
		private static bool Prefix()
		{
			PlanetView.Open();
			return false;
		}
	}
	[HarmonyPatch(typeof(MainMenuUI), "ShowMenuButtons")]
	internal static class PlanetMenuReset
	{
		private static void Postfix() { LegacyProvinceGame.Clear(); TileGame.Clear(); }
	}
}
