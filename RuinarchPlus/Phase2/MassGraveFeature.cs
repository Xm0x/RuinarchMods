using System;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Ruinarch.ModContent;
using UnityEngine;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// Ruinarch+ Mass Grave feature. Registers the structure as NEW content through the
	/// Ruinarch.ModContent framework (no game-DLL edits) and drives its hourly corpse
	/// consume. Called from <c>RuinarchPlus.OnLoad</c>.
	/// </summary>
	public static class MassGraveFeature
	{
		public const string Id = "ruinarch.plus.mass_grave";

		// Number of fill-stage sprites shipped under art/mass_grave/ (empty -> full).
		public const int FillStages = 4;

		/// <summary>Absolute path to one of this mod's shipped art files (deployed next to the DLL).</summary>
		public static string ArtPath(string relative)
		{
			return System.IO.Path.Combine(RuinarchPlus.ModDir ?? string.Empty, "art", "mass_grave", relative);
		}

		public static void Register()
		{
			try
			{
				StructureRegistration reg = ModContent.RegisterStructure(new StructureRegistration
				{
					Id = Id,
					DisplayName = "Mass Grave",
					// Framework passes the allocated virtual STRUCTURE_TYPE into these.
					Factory = (type, region) => new MassGrave(type, region),
					LoadFactory = (type, region, save) => new MassGrave(region, (SaveDataManMadeStructure)save),
					// Reuse the Cemetery's prefab/visual/footprint - no new Unity asset needed yet.
					PrefabSource = STRUCTURE_TYPE.CEMETERY,
					// A normal village building, not a demonic/player structure. No build skill:
					// it is village infrastructure, not something placed from the demonic build menu.
					Skill = null,
					UnlockWith = PLAYER_SKILL_TYPE.NONE,
					IsPlayerStructure = false,
					IsVillageStructure = true
				});
				RuinarchPlus.Log?.Info(string.Format("Mass Grave registered as village building (STRUCTURE_TYPE={0}).",
					(int)reg.StructureType));
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Mass Grave registration failed: " + e);
			}
		}

		private static bool _artChecked;

		// Validate the loose-PNG art pipeline end to end (deploy -> decode -> Sprite) and
		// warm the cache, once, on the first in-game hour. Must NOT run from OnLoad: mods load
		// inside the game assembly's module initializer, before Unity's graphics device
		// exists, and creating a Texture2D there crashes the player natively.
		internal static void PreloadArtOnce()
		{
			if (_artChecked)
			{
				return;
			}
			_artChecked = true;
			for (int i = 0; i < FillStages; i++)
			{
				string path = ArtPath(string.Format("mass_grave_{0}.png", i));
				UnityEngine.Sprite sp = ModArt.LoadSprite(path, 256f);
				if (sp != null)
				{
					RuinarchPlus.Log?.Info(string.Format("Loaded mass_grave_{0}.png ({1}x{2}px).",
						i, (int)sp.rect.width, (int)sp.rect.height));
				}
				else
				{
					RuinarchPlus.Log?.Warning(string.Format("Mass Grave art missing: {0}", path));
				}
			}
		}
	}

	/// <summary>
	/// Drives every live Mass Grave once per in-game hour (20 ticks), mirroring the game's
	/// own HOUR_STARTED cadence in <c>GameManager.TickStarted</c> - without touching the
	/// internal Messenger bus a mod assembly can't reach.
	/// </summary>
	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class MassGrave_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % 20 != 0)
				{
					return;
				}
				MassGraveFeature.PreloadArtOnce();
				MassGraveConstruction.HourlyCheck();
				System.Collections.Generic.List<MassGrave> active = MassGrave.Active;
				for (int i = active.Count - 1; i >= 0; i--)
				{
					active[i].OnHourStarted();
				}
			}
			catch
			{
			}
		}
	}
}
