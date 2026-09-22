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
					IsDemonic = false,
					IsPlayerStructure = false,
					IsVillageStructure = true
				});
				Debug.Log(string.Format("[RuinarchPlus] Mass Grave registered as village building (STRUCTURE_TYPE={0}).",
					(int)reg.StructureType));
				PreloadArt();
			}
			catch (Exception e)
			{
				Debug.LogError("[RuinarchPlus] Mass Grave registration failed: " + e);
			}
		}

		// Validate the loose-PNG art pipeline end to end (deploy -> decode -> Sprite) and
		// warm the cache. Logs each stage's pixel size to mods.log so a missing/undeployed
		// asset is obvious. The sprites themselves are wired to the structure visual later
		// (Unity StructureTemplate swap); this only proves the framework art path works.
		private static void PreloadArt()
		{
			for (int i = 0; i < FillStages; i++)
			{
				string path = ArtPath(string.Format("mass_grave_{0}.png", i));
				UnityEngine.Sprite sp = ModArt.LoadSprite(path, 256f);
				if (sp != null)
				{
					Debug.Log(string.Format("[RuinarchPlus] Loaded mass_grave_{0}.png ({1}x{2}px).",
						i, (int)sp.rect.width, (int)sp.rect.height));
				}
				else
				{
					Debug.LogWarning(string.Format("[RuinarchPlus] Mass Grave art missing: {0}", path));
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
