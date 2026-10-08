using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using UnityEngine;
using Inner_Maps;
using Traits;
using UnityEngine.UI;

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
			TileObjectLoadingChecks();
			yield return SavePreloadChecks();
			EnumLoadingChecks();
			FieldLoadingChecks();
			yield return DisconnectReachesTileObjects("");
			yield return MinimapRedrawChecks();

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

		// Mutate only a private set of existing references, never the world's database.
		private void TileObjectLoadingChecks()
		{
			Type cursorType = AccessTools.TypeByName("RuinarchPerformance.TileObjectLoading+Cursor");
			if (cursorType == null)
			{
				Skip("loading preserves native ordinals across collection changes", "linear loading is not installed");
				return;
			}
			TileObject[] objects = DatabaseManager.Instance.tileObjectDatabase.allTileObjectsList.Take(8).ToArray();
			if (objects.Length < 8)
			{
				Skip("loading preserves native ordinals across collection changes", "fewer than eight tile objects");
				return;
			}
			var read = AccessTools.Method(cursorType, "Read");
			using (var cursor = (IDisposable)Activator.CreateInstance(cursorType, true))
			{
				var set = new HashSet<TileObject>(objects.Take(6));
				bool Same(HashSet<TileObject> source, int index) =>
					ReferenceEquals(read.Invoke(cursor, new object[] { source, index }), source.ElementAt(index));
				Check("loading preserves ordinals after removing an earlier object", () =>
				{
					bool first = Same(set, 0) && Same(set, 1);
					set.Remove(objects[0]);
					return (first && Same(set, 2), "third native ordinal after removing the first object");
				});
				Check("loading preserves ordinals when a removed slot is reused", () =>
				{
					set.Remove(objects[1]); set.Add(objects[6]);
					return (Same(set, 3), "same-count removal/insertion reuses a HashSet slot");
				});
				Check("loading preserves ordinals after insertion", () =>
				{
					set.Add(objects[7]);
					return (Same(set, 4), "next ordinal after adding another object");
				});
				Check("loading preserves nonsequential and repeated ordinals", () =>
					(Same(set, 0) && Same(set, set.Count - 1) && Same(set, 1) && Same(set, 1),
					"restart, skip forward, go backward and repeat"));
				Check("loading preserves ordinals after replacing or clearing the set", () =>
				{
					var replacement = new HashSet<TileObject>(objects.Reverse());
					bool changed = Same(replacement, 0);
					replacement.Clear(); replacement.Add(objects[3]); replacement.Add(objects[5]);
					return (changed && Same(replacement, 1), "new source followed by clear/repopulate");
				});
				Check("loading preserves native invalid-index errors", () =>
				{
					bool Invalid(int index)
					{
						try { read.Invoke(cursor, new object[] { set, index }); return false; }
						catch (System.Reflection.TargetInvocationException e)
						{ return e.InnerException is ArgumentOutOfRangeException; }
					}
					return (Invalid(-1) && Invalid(set.Count) && Same(set, 0),
						"negative/end ordinals fail; a subsequent valid lookup still works");
				});
			}
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
			// One with a trait that keeps the default handler: it drops the character from its
			// responsible characters (the mod must not skip such tile objects).
			bool Plain(Trait tr) => tr.GetType().GetMethod(DisconnectCharacterKey, new[] { typeof(IPointOfInterest), typeof(Character) })?.DeclaringType == typeof(Trait);
			TileObject traited = all.FirstOrDefault(t => t?.gridTileLocation != null && t.traitContainer != null && t.traitContainer.allTraitsAndStatuses.Values.Any(Plain));
			Trait trait = traited?.traitContainer.allTraitsAndStatuses.Values.First(Plain);
			if (!Try("mark tile objects as assumed by the villager", () => picks.ForEach(t => t.AddCharacterThatAlreadyAssumed(victim))))
			{
				yield break;
			}
			Try("make the villager responsible for a tile object's trait", () =>
			{
				if (trait == null) return;
				if (trait.responsibleCharacters == null) AccessTools.Property(typeof(Trait), nameof(Trait.responsibleCharacters)).SetValue(trait, new List<Character>());
				trait.responsibleCharacters.Add(victim);
			});
			yield return null;
			Try("send the disconnect signal", () => SignalHandler<Character>.Broadcast(DisconnectCharacterKey, victim));
			Check(name, () => (picks.Count > 0 && picks.All(t => !t.HasCharacterAlreadyAssumed(victim)),
				$"{victim.name}: {string.Join(", ", picks.Select(t => $"{t.name}={(t.HasCharacterAlreadyAssumed(victim) ? "kept" : "dropped")}"))}"));
			if (trait == null)
			{
				Skip("a disconnected character is dropped from tile objects' traits" + when, "no tile object with a plain trait");
			}
			else
			{
				Check("a disconnected character is dropped from tile objects' traits" + when, () => (!trait.responsibleCharacters.Contains(victim),
					$"{traited.name} {trait.name}: responsible {string.Join(", ", trait.responsibleCharacters.Select(c => c.name))}"));
			}
		}

		// The minimap camera draws only when its picture changes, and not while hidden.
		private IEnumerator MinimapRedrawChecks()
		{
			InnerTileMap map = GridMap.Instance.mainRegion.innerMap;
			Camera mini = map.minimapCamera;
			MinimapUIController ui = UIManager.Instance.sidebarUIController.minimapUIController;
			var view = AccessTools.Field(typeof(MinimapUIController), "m_minimapUIView");
			RawImage image = (view?.GetValue(ui) as MinimapUIView)?.UIModel?.minimapImage?.rawImage;
			if (mini == null || image == null)
			{
				Skip("moving the view redraws the minimap", "no minimap camera or panel");
				yield break;
			}
			bool wasShown = image.isActiveAndEnabled;
			Try("show the minimap", () => ui.ShowUI());
			yield return new WaitForSecondsRealtime(0.5f);

			int frames = 0, drawn = 0;
			float end = Time.realtimeSinceStartup + 2f;
			while (Time.realtimeSinceStartup < end)
			{
				yield return new WaitForEndOfFrame();
				frames++;
				if (mini.enabled) drawn++;
			}
			Check("the minimap camera draws only when its picture changes", () => (drawn * 2 < frames, $"drew on {drawn} of {frames} frames"));

			Camera main = InnerMapCameraMove.Instance.camera;
			Vector3 home = main.transform.position;
			yield return new WaitForEndOfFrame();
			long before = MinimapHash(mini);
			float direction = home.x > (map.cameraBounds.x + map.cameraBounds.z) * 0.5f ? -1f : 1f;
			Try("move the view", () =>
			{
				InnerMapCameraMove.Instance.ClearOutCameraTargets();
				InnerMapCameraMove.Instance.MoveCameraForMinimap(home + new Vector3(direction * map.width / 3f, 0f, 0f));
			});
			for (int i = 0; i < 5; i++) yield return null;
			yield return new WaitForEndOfFrame();
			long after = MinimapHash(mini);
			Check("moving the view redraws the minimap", () => (Vector3.Distance(home, main.transform.position) > 0.1f && before != after, $"picture {before:X} -> {after:X}, view {home} -> {main.transform.position}"));
			Try("move the view back", () => InnerMapCameraMove.Instance.MoveCameraForMinimap(home));

			Try("hide the minimap", () => ui.HideUI());
			yield return null;
			int hiddenDraws = 0;
			end = Time.realtimeSinceStartup + 1.5f;
			while (Time.realtimeSinceStartup < end)
			{
				yield return new WaitForEndOfFrame();
				if (mini.enabled) hiddenDraws++;
			}
			Check("a hidden minimap is not drawn", () => (hiddenDraws == 0, $"drew on {hiddenDraws} frames while hidden"));
			if (wasShown) Try("show the minimap again", () => ui.ShowUI());
		}

		private static long MinimapHash(Camera mini)
		{
			RenderTexture rt = mini.targetTexture;
			RenderTexture old = RenderTexture.active;
			var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
			try
			{
				RenderTexture.active = rt;
				tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
				long h = 17;
				foreach (Color32 c in tex.GetPixels32()) h = h * 31 + (c.r | c.g << 8 | c.b << 16);
				return h;
			}
			finally
			{
				RenderTexture.active = old;
				UnityEngine.Object.Destroy(tex);
			}
		}
	}
}
