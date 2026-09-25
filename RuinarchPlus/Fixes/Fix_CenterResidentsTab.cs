using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Locations.Settlements;
using UnityEngine;
using UnityEngine.UI;
using UtilityScripts;

namespace RuinarchPlus
{
	// BUG (UI): the village center's Residents tab is always empty. The building panel lists
	// the building's own residents (StructureInfoUI.UpdateResidents -> activeStructure.residents),
	// and nobody lives in the center: villagers live in dwellings. Fix: on a village center,
	// list everyone living in the village, with the panel's own portraits and hover. Every
	// other building still lists its own household.
	[HarmonyPatch(typeof(StructureInfoUI), "UpdateResidents")]
	internal static class Fix_CenterResidentsTab
	{
		private static readonly Action<StructureInfoUI, CharacterPortrait> HoverOver =
			AccessTools.MethodDelegate<Action<StructureInfoUI, CharacterPortrait>>(AccessTools.Method(typeof(StructureInfoUI), "OnHoverOverCharacterPortrait"));
		private static readonly Action<StructureInfoUI, CharacterPortrait> HoverOut =
			AccessTools.MethodDelegate<Action<StructureInfoUI, CharacterPortrait>>(AccessTools.Method(typeof(StructureInfoUI), "OnHoverOutCharacterPortrait"));

		// UIManager.InstantiateUIObject is internal to the game.
		private static readonly Func<UIManager, string, Transform, GameObject> Instantiate =
			AccessTools.MethodDelegate<Func<UIManager, string, Transform, GameObject>>(AccessTools.Method(typeof(UIManager), "InstantiateUIObject"));

		private static bool Prefix(StructureInfoUI __instance, GameObject ___characterItemPrefab, ScrollRect ___charactersScrollView)
		{
			try
			{
				if (__instance.activeStructure?.structureType != STRUCTURE_TYPE.CITY_CENTER || !(__instance.activeStructure.settlementLocation is NPCSettlement village))
				{
					return true;
				}
				Utilities.DestroyChildren(___charactersScrollView.content);
				foreach (Character c in village.residents.Where(c => c != null && !c.isDead).OrderBy(c => c.name).ToList())
				{
					CharacterPortrait portrait = Instantiate(UIManager.Instance, ___characterItemPrefab.name, ___charactersScrollView.content).GetComponent<CharacterPortrait>();
					portrait.GeneratePortrait(c);
					portrait.SetNameState(p_state: true);
					portrait.SetHoverActions(p => HoverOver(__instance, p), p => HoverOut(__instance, p));
				}
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Village center residents tab failed: " + e.Message);
				return true;
			}
		}
	}
}
