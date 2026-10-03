using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using UnityEngine;

namespace RuinarchDebug
{
	/// <summary>The Performance Mod (ruinarch.performance), reached by reflection.</summary>
	internal static class PerfBridge
	{
		private static Type Table => AccessTools.TypeByName("RuinarchPerformance.TileObjectListeners");

		internal static bool Available => Table != null;

		/// <summary>Tile objects in the mod's table.</summary>
		internal static int TableCount => (int)(AccessTools.Property(Table, "Count")?.GetValue(null) ?? -1);

		/// <summary>Entries in one of the game's signal lists.</summary>
		internal static int Listeners<T>(string key) => Targets<T>(key).Count;

		/// <summary>The objects behind one of the game's signal lists, one per entry.</summary>
		internal static List<object> Targets<T>(string key)
		{
			var handles = AccessTools.Field(typeof(SignalHandler<T>), "_handles")?.GetValue(null) as Dictionary<string, List<SignalHandler<T>.SignalListener>>;
			return handles != null && handles.TryGetValue(key, out var list) ? list.Select(d => d.Target).ToList() : new List<object>();
		}
	}

	public partial class AutoTest
	{
		private const string DisconnectCharacterKey = "DisconnectFromCharacter";
		private const string DisconnectStructureKey = "DisconnectFromStructure";
		private const string CrimeRemovedKey = "CrimeRemovedFromDatabase";

		private IEnumerator PerformanceSuite()
		{
			if (!PerfBridge.Available)
			{
				Skip("tile objects sit behind one signal listener", "the Performance Mod is not loaded");
				yield break;
			}
			PerformanceListenerChecks("");
			yield return DisconnectReachesTileObjects("");

			// Characters also listen (CrimeComponent); jobs must be live and listed once.
			yield return WaitGameHours(2f, null);
			List<JobQueueItem> jobs = PerfBridge.Targets<CrimeData>(CrimeRemovedKey).OfType<JobQueueItem>().ToList();
			int stale = jobs.Count(j => j.hasBeenReset);
			int twice = jobs.GroupBy(j => j).Count(g => g.Count() > 1);
			Check("finished jobs leave no crime listeners behind", () => (stale == 0 && twice == 0,
				$"job crime listeners {jobs.Count}: {stale} from finished jobs, {twice} jobs listed more than once; live jobs {DatabaseManager.Instance.jobDatabase.allJobs.Count}"));
		}

		// Reloads the world from a save: only when asked for by name.
		private IEnumerator PerformanceReloadSuite()
		{
			if (!PerfBridge.Available)
			{
				Skip("tile objects sit behind one signal listener after loading a save", "the Performance Mod is not loaded");
				yield break;
			}
			// Its own save: SaveAndRead deletes the one it writes.
			const string saveName = "RuinarchPerformance-autotest";
			string zip = Path.Combine(UtilityScripts.Utilities.gameSavePath, saveName + ".zip");
			SaveCurrentProgressManager saver = SaveManager.Instance.saveCurrentProgressManager;
			Try("delete an old test save", () => { if (File.Exists(zip)) File.Delete(zip); });
			// Paused with the speed controls locked, as in SaveAndRead.
			Try("pause for the save", () =>
			{
				UIManager.Instance.Pause();
				UIManager.Instance.SetSpeedTogglesState(false);
			});
			_saving = true;
			Try("save the game", () => saver.DoManualSave(saveName));
			yield return WaitReal(() => File.Exists(zip) && !saver.isSaving && !saver.isWritingToDisk, 180f, "the test save to be written");
			_saving = false;
			Try("unlock the speed controls", () => UIManager.Instance.SetSpeedTogglesState(true));
			GameManager before = GameManager.Instance;
			if (!Try("load the test save", () =>
			{
				SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(zip);
				UIManager.Instance.optionsMenu.LoadSave();
			}))
			{
				yield break;
			}
			yield return WaitReal(() => GameManager.Instance != null && GameManager.Instance != before && GameManager.Instance.gameHasStarted, 600f, "the test save to load");
			// Mod save data and the last load steps finish after the game starts.
			yield return new WaitForSecondsRealtime(15f);
			Try("resume after loading", () =>
			{
				UIManager.Instance.Unpause();
				UIManager.Instance.SetProgressionSpeed4X();
				Time.timeScale = TimeScale;
			});
			PerformanceListenerChecks(" after loading a save");
			yield return DisconnectReachesTileObjects(" after loading a save");
			Try("delete the test save", () => File.Delete(zip));
		}

		private void PerformanceListenerChecks(string when)
		{
			int tileObjects = DatabaseManager.Instance.tileObjectDatabase.allTileObjectsList.Count;
			int characters = PerfBridge.Listeners<Character>(DisconnectCharacterKey);
			int structures = PerfBridge.Listeners<LocationStructure>(DisconnectStructureKey);
			int table = PerfBridge.TableCount;
			// The game's lists keep characters, jobs, parties and structures (a few
			// thousand at most); the tile objects are in the mod's table.
			Check("tile objects sit behind one signal listener" + when,
				() => (characters < 5000 && structures < 5000 && table >= tileObjects * 9 / 10,
				$"tile objects {tileObjects}, table {table}, {DisconnectCharacterKey} list {characters}, {DisconnectStructureKey} list {structures}"));
		}

		// The game's own disconnect signal (Character.CleanUp sends it) still reaches tile
		// objects: each drops the character from the ones that already assumed things there.
		private IEnumerator DisconnectReachesTileObjects(string when)
		{
			string name = "a disconnected character is dropped by tile objects" + when;
			NPCSettlement village = Villages().FirstOrDefault(v => v.residents.Count(c => !c.isDead && c.isNormalCharacter) >= 2);
			if (village == null)
			{
				Skip(name, "no village with two living villagers");
				yield break;
			}
			Character victim = Guard("kill a villager", () => KillResident(village));
			if (victim == null)
			{
				yield break;
			}
			List<TileObject> all = DatabaseManager.Instance.tileObjectDatabase.allTileObjectsList.ToList();
			// Spread across the list: early world-generation objects and recent ones.
			List<TileObject> picks = new[] { 0, all.Count / 3, all.Count * 2 / 3, all.Count - 1 }
				.Select(i => all[i]).Where(t => t != null && t.gridTileLocation != null).Distinct().ToList();
			if (!Try("mark tile objects as assumed by the villager", () => picks.ForEach(t => t.AddCharacterThatAlreadyAssumed(victim))))
			{
				yield break;
			}
			yield return null;
			Try("send the disconnect signal", () => SignalHandler<Character>.Broadcast(DisconnectCharacterKey, victim));
			Check(name, () => (picks.Count > 0 && picks.All(t => !t.HasCharacterAlreadyAssumed(victim)),
				$"{victim.name}: {string.Join(", ", picks.Select(t => $"{t.name}={(t.HasCharacterAlreadyAssumed(victim) ? "kept" : "dropped")}"))}"));
		}
	}
}
