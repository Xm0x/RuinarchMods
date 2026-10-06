using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Ruinarch.Modding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UtilityScripts;

namespace TruePlanet
{
	[ModSettings("TruePlanet")]
	public sealed class PlanetSettings
	{
		[Setting("Provinces", "Number of provinces in a new atlas."), Range(30, 400)] public int provinces = 120;
		[Setting("Land percent", "Share of the planet covered by land."), Range(20, 80)] public int landPercent = 40;
		[Setting("Nations", "Requested nations, limited by inhabited land."), Range(2, 40)] public int nations = 10;
		[Setting("Wild land percent", "Land provinces without planned villages."), Range(0, 75)] public int wildPercent = 25;
		[Setting("Largest province", "Map size planned for later playable provinces.")] public ProvinceSize largestProvince = ProvinceSize.Huge;
		internal PlanetOptions Options() => new PlanetOptions { Provinces = provinces, LandShare = landPercent / 100.0, Nations = nations, WildShare = wildPercent / 100.0, LargestProvince = largestProvince };
	}
	public sealed class TruePlanetMod : IRuinarchMod
	{
		internal static ModLogger Log;
		internal static PlanetSettings Settings;
		internal static string SaveRoot => Path.Combine(Utilities.gameSavePath, "TruePlanet");
		public void OnLoad(ModContext context)
		{
			Log = context.Logger; Settings = context.Settings.Register<PlanetSettings>();
			new Harmony(context.Info.id).CreateClassProcessor(typeof(PlanetEntry)).Patch();
			Log.Info("TruePlanet atlas installed. Stock New Game, Load and province gameplay are unchanged.");
		}
		internal static void Name(Planet planet)
		{
			string[] regions = Names("baseRegionNames"), human = Names("baseHumanKingdomNames"), elven = Names("baseElvenKingdomNames");
			string[] prefixes = Names("baseAncientRuinPrefixes"), suffixes = Names("baseAncientRuinSuffixes");
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
				foreach (Village village in province.Villages) village.Name = Unique(prefixes[random.Next(prefixes.Length)] + suffixes[random.Next(suffixes.Length)]);
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
	[HarmonyPatch(typeof(MainMenuUI), "ShowMenuButtons")]
	[HarmonyAfter("ruinarch.modmenu")]
	internal static class PlanetEntry
	{
		private static void Postfix(MainMenuUI __instance)
		{
			try
			{
				var newGame = AccessTools.Field(typeof(MainMenuUI), "newGameButton").GetValue(__instance) as Button;
				Transform buttons = newGame?.transform.parent, exit = buttons?.Find("ExitBtn"), previous = buttons?.Find("EditorBtn") ?? buttons?.Find("ModsBtn");
				if (buttons == null || exit == null || previous == null) throw new InvalidOperationException("The native menu has no Mods/Exit row.");
				if (buttons.Find("PlanetBtn") != null) return;
				var exitRect = (RectTransform)exit; var previousRect = (RectTransform)previous;
				Vector2 step = new Vector2(0, exitRect.anchoredPosition.y - previousRect.anchoredPosition.y);
				var go = UnityEngine.Object.Instantiate(previous.gameObject, buttons, false); go.name = "PlanetBtn";
				go.transform.SetSiblingIndex(previous.GetSiblingIndex() + 1);
				((RectTransform)go.transform).anchoredPosition = exitRect.anchoredPosition;
				exitRect.anchoredPosition += step;
				var label = go.GetComponentInChildren<TMP_Text>(true);
				foreach (Component c in label.GetComponents<Component>()) if (c.GetType().Name == "CustomLocalizeStringEvent") UnityEngine.Object.DestroyImmediate(c);
				label.text = "Planet";
				var button = go.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent(); button.interactable = true;
				button.onClick.AddListener(PlanetView.Open); go.SetActive(true);
				// Fit the extra row without changing native button sizes or horizontal offsets.
				float top = float.NegativeInfinity;
				foreach (Transform child in buttons)
					if (child.GetComponent<Button>() != null) top = Mathf.Max(top, ((RectTransform)child).anchoredPosition.y);
				foreach (Transform child in buttons)
					if (child.GetComponent<Button>() != null)
					{
						var rect = (RectTransform)child;
						rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, top + (rect.anchoredPosition.y - top) * .9f);
					}
			}
			catch (Exception e) { TruePlanetMod.Log.Error("Cannot add Planet to the native menu: " + e); }
		}
	}
}
