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
					// Borrow the Cemetery's prefab (footprint, walls, pathing); MassGraveLook lays
					// the Mass Grave's own pit art over it.
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
