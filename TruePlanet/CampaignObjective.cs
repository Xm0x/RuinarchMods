using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
			Bind(); Refresh();
			var text = new StringBuilder("Subjugate the Planet: ");
			text.Append(TileGame.Store.CountSubjugatedSettlements(_subjugated)).Append(" / ")
				.Append(TileGame.Store.SettlementCount).Append(" settlements");
			if (!Ready) return text.Append("\nPreparing settlement...").ToString();
			int settlements = 0;
			int nativeVillages = 0;
			foreach (var location in GridMap.Instance.mainRegion.settlementsInRegion)
				if (location is NPCSettlement && location.locationType == LOCATION_TYPE.VILLAGE) nativeVillages++;
			foreach (var location in GridMap.Instance.mainRegion.settlementsInRegion)
			{
				if (!(location is NPCSettlement settlement) || settlement.locationType != LOCATION_TYPE.VILLAGE) continue;
				int population = 0;
				foreach (Character character in DatabaseManager.Instance.characterDatabase.aliveVillagersList)
					if (character.homeSettlement == settlement) population++;
				Village definition = null, only = null; int definitions = 0;
				foreach (Province province in TileGame.Active.Planet.Provinces) foreach (Village village in province.Villages)
					if (village.TileId == TileGame.Active.TileId)
					{
						only = village; definitions++;
						if (village.Name == settlement.name) definition = village;
					}
				if (definition == null && definitions == 1 && nativeVillages == 1) definition = only;
				string kind = definition == null ? "Settlement" : TilePresentation.SettlementKind(definition, TileGame.Active.Planet.Options.LocalSize);
				text.Append('\n').Append(settlement.name).Append(" (").Append(kind).Append("): ")
					.Append(population).Append(population == 1 ? " settler" : " settlers");
				settlements++;
			}
			if (settlements == 0) text.Append("\nWilderness: ").Append(DatabaseManager.Instance.characterDatabase.aliveVillagersList.Count).Append(" settlers");
			return text.ToString();
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
				__result = "The planet " + TileGame.Active.Planet.Name + " has been subjugated. Every settlement is free of opposition."; return false;
			}
		}
		[HarmonyPatch(typeof(UIManager), nameof(UIManager.ShowStartScenario))]
		private static class PlanetIntro
		{
			private static void Prefix(ref string __0)
			{
				if (TileGame.Active == null) return;
				__0 = "Subjugate all " + TileGame.Store.SettlementCount + " settlements on " + TileGame.Active.Planet.Name
					+ ".\n\nDefeat their settlers or gain their allegiance. Clearing this map alone does not win the planet.\n\nUse Planet atlas to travel.";
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
