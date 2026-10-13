using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Quests;

namespace TruePlanet
{
	internal static class CampaignObjective
	{
		private static CampaignManifest _campaign;
		private static HashSet<int> _subjugated;
		private static readonly MethodInfo NativeCheck = AccessTools.Method(typeof(Eradication), "CheckForGameWin");
		private static bool Ready => TileGame.Active != null && GameManager.Instance != null && GameManager.Instance.gameHasStarted
			&& DatabaseManager.Instance != null && PlayerManager.Instance != null && !LevelLoaderManager.Instance.IsLoadingScreenActive();

		internal static void Install(Harmony harmony)
		{
			foreach (Type type in typeof(CampaignObjective).GetNestedTypes(BindingFlags.NonPublic))
				if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0) harmony.CreateClassProcessor(type).Patch();
		}
		private static void Bind()
		{
			CampaignManifest campaign = TileGame.Campaign;
			if (ReferenceEquals(_campaign, campaign)) return;
			_campaign = campaign; _subjugated = new HashSet<int>(campaign.SubjugatedTiles);
		}
		internal static void Clear() { _campaign = null; _subjugated = null; }
		internal static int Remaining()
		{
			int remaining = 0;
			foreach (Character character in DatabaseManager.Instance.characterDatabase.aliveVillagersList)
				if (!character.isAlliedWithPlayer) remaining++;
			return remaining;
		}
		internal static int Refresh()
		{
			if (!Ready) return -1;
			Bind(); int tile = TileGame.Active.TileId, remaining = Remaining();
			if (!TileGame.Store.IsSettledTile(tile)) return remaining;
			bool changed = remaining == 0 ? _subjugated.Add(tile) : _subjugated.Remove(tile);
			if (changed) _campaign.SubjugatedTiles = _subjugated.OrderBy(id => id).ToArray();
			return remaining;
		}
		internal static void CheckCurrentVictory()
		{
			if (!Ready || PlayerManager.Instance.player.hasAlreadyWon || TileTravel.Instance != null && TileTravel.Instance.Stage != TravelStage.Idle) return;
			if (QuestManager.Instance != null && QuestManager.Instance.victoryCondition is Eradication eradication) NativeCheck.Invoke(eradication, null);
		}
		internal static void WorldReady()
		{
			if (!Ready) return;
			Refresh();
			// Native post-load initialization replaces QuestManager's condition after
			// the restored condition has already been bound to the bookmark UI.
			if (PlayerManager.Instance.player.bookmarkComponent.bookmarkedObjects.TryGetValue(BOOKMARK_CATEGORY.Win_Condition, out BookmarkCategory category))
				foreach (IBookmarkable bookmark in category.bookmarked)
					if (bookmark is Eradication objective)
						objective.bookmarkEventDispatcher.ExecuteBookmarkChangedNameOrElementsEvent(objective);
			CheckCurrentVictory();
		}
		private static bool Complete()
		{
			Refresh();
			return Ready && (TileTravel.Instance == null || TileTravel.Instance.Stage == TravelStage.Idle)
				&& TileGame.Store.SettledTileCount > 0 && TileGame.Store.IsSettledTile(TileGame.Active.TileId) && _subjugated.Count == TileGame.Store.SettledTileCount;
		}
		private static string Title()
		{
			Bind(); int remaining = Refresh();
			string local = !TileGame.Store.IsSettledTile(TileGame.Active.TileId) ? "Current tile: wilderness (not a conquest target)"
				: remaining < 0 ? "Current tile: preparing native world"
				: remaining == 0 ? "Current tile: subjugated" : "Current tile: " + remaining + (remaining == 1 ? " villager opposes you" : " villagers oppose you");
			return "Subjugate the planet: " + _subjugated.Count + " / " + TileGame.Store.SettledTileCount + " settled tiles\n" + local;
		}
		[HarmonyPatch(typeof(Eradication), "CheckForGameWin")]
		private static class PlanetVictory
		{
			private static bool Prefix() => TileGame.Active == null || Complete();
		}
		[HarmonyPatch(typeof(Eradication), "GetBookmarkName")]
		private static class PlanetTitle
		{
			private static bool Prefix(ref string __result)
			{
				if (TileGame.Active == null) return true;
				__result = Title(); return false;
			}
		}
		[HarmonyPatch(typeof(Eradication), "GetWinMessage")]
		private static class PlanetWinMessage
		{
			private static bool Prefix(ref string __result)
			{
				if (TileGame.Active == null) return true;
				__result = "The planet " + TileGame.Active.Planet.Name + " has been subjugated. Every settled tile is free of opposition."; return false;
			}
		}
		[HarmonyPatch(typeof(UIManager), nameof(UIManager.ShowStartScenario))]
		private static class PlanetIntro
		{
			private static void Prefix(ref string __0)
			{
				if (TileGame.Active == null) return;
				__0 = "Subjugate every settled tile on " + TileGame.Active.Planet.Name + ".\n\nThere are " + TileGame.Store.SettledTileCount
					+ " settled conquest targets. A tile is subjugated when no living villagers oppose you; allied villagers count too. Clearing this tile alone does not win the planet.\n\nUse Planet atlas to travel. Empty wilderness is not a conquest target. Retaliation remains local to each map.";
			}
		}
		[HarmonyPatch(typeof(Eradication), "OnCharacterAddedToAliveVillagers")]
		private static class NewOpposition
		{
			private static void Postfix() { if (TileGame.Active != null) Refresh(); }
		}
		[HarmonyPatch(typeof(GameManager), nameof(GameManager.StartProgression))]
		private static class NewWorldReady
		{
			private static void Postfix() { if (TileGame.Active != null) WorldReady(); }
		}
		[HarmonyPatch(typeof(LevelLoaderManager), nameof(LevelLoaderManager.SetLoadingState))]
		private static class LoadedWorldReady
		{
			private static void Postfix(bool state) { if (!state && TileGame.Active != null) WorldReady(); }
		}
		[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.LoadVictoryCondition))]
		private static class ResumeOlderTileVictory
		{
			private static void Prefix(SaveDataVictoryCondition data)
			{
				if (TileGame.Active == null || !(data is SaveDataEradication) || !PlayerManager.Instance.player.hasAlreadyWon) return;
				Bind();
				if (_subjugated.Count < TileGame.Store.SettledTileCount) PlayerManager.Instance.player.hasAlreadyWon = false;
			}
		}
	}
}
