using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements.Settlement_Events;
using Locations.Settlements;
using Player_Input;
using UnityEngine;

namespace RuinarchDebug
{
	/// <summary>
	/// Unattended in-game test harness. Runs only when <c>Mods/RuinarchDebug/autotest.flag</c>
	/// exists at launch (the flag is deleted immediately, so a normal launch never runs it).
	/// It drives the real game: main menu -> new world -> places the portal -> picks the
	/// default loadout -> runs the world at accelerated speed, exercises Ruinarch+ features,
	/// writes PASS/FAIL lines to <c>Mods/RuinarchDebug/autotest.log</c> (and Player.log),
	/// then quits the game.
	/// </summary>
	public class AutoTest : MonoBehaviour
	{
		private const float TimeScale = 4f;

		private string _logPath;
		private int _pass;
		private int _fail;
		private int _skip;
		private long _gameTicks;
		private int _lastTick = -1;

		// The running harness, if any (null in normal play).
		private static AutoTest _running;

		internal static void TryStart(string modDirectory)
		{
			string flag = Path.Combine(modDirectory, "autotest.flag");
			if (!File.Exists(flag))
			{
				return;
			}
			// The flag may name the suites to run (comma-separated, e.g. "FamineSuite,TradeSuite");
			// empty runs them all.
			string only = File.ReadAllText(flag).Trim();
			File.Delete(flag);
			var go = new GameObject("RuinarchAutoTest");
			DontDestroyOnLoad(go);
			var test = go.AddComponent<AutoTest>();
			_running = test;
			test._only = new HashSet<string>(only.Split(new[] { ',', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries));
			test._logPath = Path.Combine(modDirectory, "autotest.log");
			// The previous run's log is kept as logs/autotest-<date>_<time>.log (when it was last
			// written), newest 20 only; autotest.log is always the current run.
			try
			{
				if (File.Exists(test._logPath))
				{
					string dir = Path.Combine(modDirectory, "logs");
					Directory.CreateDirectory(dir);
					File.Copy(test._logPath, Path.Combine(dir, $"autotest-{File.GetLastWriteTime(test._logPath):yyyy-MM-dd_HH-mm-ss}.log"), overwrite: true);
					foreach (string old in Directory.GetFiles(dir, "autotest-*.log").OrderByDescending(f => f, StringComparer.Ordinal).Skip(20))
					{
						File.Delete(old);
					}
				}
			}
			catch
			{
			}
			File.WriteAllText(test._logPath, "# Ruinarch autotest " + DateTime.Now + Environment.NewLine);
		}

		private int _gameErrors;

		// Suites named in the flag file; empty = all.
		private HashSet<string> _only = new HashSet<string>();

		private bool Runs(string suite)
		{
			if (_only.Count == 0 || _only.Contains(suite))
			{
				return true;
			}
			Log($"(suite {suite} not selected)");
			return false;
		}

		private void Start()
		{
			// Release builds switch Unity logging off (WorldConfigManager.Awake); keep it on for
			// the test run and mirror game errors/exceptions into autotest.log.
			Debug.unityLogger.logEnabled = true;
			Application.logMessageReceived += OnGameLog;
			StartCoroutine(Run());
		}

		private void OnDestroy()
		{
			Application.logMessageReceived -= OnGameLog;
		}

		private void OnGameLog(string condition, string stackTrace, LogType type)
		{
			if ((type == LogType.Exception || type == LogType.Error) && _gameErrors < 40 && !condition.Contains("[autotest]"))
			{
				_gameErrors++;
				Append($"GAME-{type.ToString().ToUpperInvariant()} {condition}{Environment.NewLine}    {stackTrace?.Replace("\n", "\n    ")}");
			}
		}

		// Monotonic game clock: ticks elapsed since the world started (handles day wrap).
		private void Update()
		{
			if (!Debug.unityLogger.logEnabled)
			{
				Debug.unityLogger.logEnabled = true;
			}
			if (GameManager.Instance == null || !GameManager.Instance.gameHasStarted)
			{
				return;
			}
			int tick = GameManager.Instance.Today().tick;
			if (_lastTick >= 0 && tick != _lastTick)
			{
				int delta = tick - _lastTick;
				if (delta < 0)
				{
					delta += GameManager.ticksPerDay;
				}
				_gameTicks += delta;
			}
			_lastTick = tick;
			// Popups/events may pause the game; the test must keep time moving. Not while a save
			// runs (the harness's or the game's autosave): its threads read the world, and a world
			// running under them crashed a save (SaveDataJobQueueItem.Save, a job freed mid-read).
			SaveCurrentProgressManager saver = SaveManager.Instance?.saveCurrentProgressManager;
			if (GameManager.Instance.isPaused && UIManager.Instance != null && !_saving && saver != null && !saver.isSaving && !saver.isWritingToDisk)
			{
				UIManager.Instance.Unpause();
			}
		}

		private float GameHours => _gameTicks / (float)GameManager.ticksPerHour;

		// ---------------------------------------------------------------------------------
		private IEnumerator Run()
		{
			Log("boot: waiting for main menu");
			yield return WaitReal(() => MainMenuUI.Instance != null && WorldSettings.Instance != null, 180f, "main menu");
			yield return new WaitForSecondsRealtime(5f);
			if (!Try("open world settings", () => MainMenuUI.Instance.OnClickPlayGame()))
			{
				yield break;
			}
			yield return new WaitForSecondsRealtime(2f);
			if (!Try("start world generation", () => WorldSettings.Instance.OnClickContinue()))
			{
				yield break;
			}
			// The default preset is a small map with a single village. The suite needs three:
			// the burial tests use up one village, the knowledge test a second, and the curfew
			// and migration tests need one nobody has touched. Three villages take a large map.
			// Continue has already copied the setup UI into the data, and the world is
			// generated from that data after the scene loads.
			Try("ask for three villages", () =>
			{
				WorldSettingsData data = WorldSettings.Instance.worldSettingsData;
				if (data.mapSettings.mapSize == MAP_SIZE.Small || data.mapSettings.mapSize == MAP_SIZE.Medium)
				{
					data.mapSettings.SetMapSize(MAP_SIZE.Large);
				}
				int want = Math.Min(3, data.mapSettings.GetMaxStartingVillages());
				FactionTemplate template = data.factionSettings.factionTemplates.FirstOrDefault() ?? data.factionSettings.AddFactionSetting(0);
				while (data.factionSettings.GetCurrentTotalVillageCountBasedOnFactions() < want)
				{
					template.AddVillageSetting(VillageSetting.Default);
				}
				Log($"  map {data.mapSettings.mapSize}, villages requested: {data.factionSettings.GetCurrentTotalVillageCountBasedOnFactions()}");
			});
			Log("generating world...");
			// The portal prompt (pickPortalMessage) is only activated by OnClickPlacePortal, which
			// InitialWorldSetupMenu.Show() calls once world generation has finished.
			yield return WaitReal(() => UIManager.Instance != null && UIManager.Instance.initialWorldSetupMenu != null
				&& UIManager.Instance.initialWorldSetupMenu.pickPortalMessage != null
				&& UIManager.Instance.initialWorldSetupMenu.pickPortalMessage.gameObject.activeInHierarchy
				&& GridMap.Instance?.mainRegion?.settlementsInRegion != null && PlayerManager.pickPortalInputModule != null, 900f, "world generation");
			Log($"world generated: {GridMap.Instance?.mainRegion?.settlementsInRegion?.Count ?? 0} settlement(s)");
			yield return new WaitForSecondsRealtime(3f);
			if (!Try("place portal", PlacePortal))
			{
				yield break;
			}
			yield return WaitReal(() => PlayerManager.Instance?.player?.playerSettlement != null, 30f, "portal settlement");
			yield return new WaitForSecondsRealtime(2f);
			Try("open loadout", () => UIManager.Instance.initialWorldSetupMenu.OnClickConfigureLoadOut());
			yield return new WaitForSecondsRealtime(2f);
			// Selecting the loadout can already start progression in this build; confirming a
			// second time would run StartProgression twice (it NREs on the second cleanup).
			if (!GameManager.Instance.gameHasStarted
				&& !Try("confirm loadout", () => UIManager.Instance.initialWorldSetupMenu.loadOutMenu.OnClickContinue()))
			{
				yield break;
			}
			yield return WaitReal(() => GameManager.Instance != null && GameManager.Instance.gameHasStarted, 600f, "game start");
			if (!GameManager.Instance.gameHasStarted)
			{
				Finish("game never started");
				yield break;
			}
			Try("run at speed", () =>
			{
				UIManager.Instance.Unpause();
				UIManager.Instance.SetProgressionSpeed4X();
				Time.timeScale = TimeScale;
			});
			Log($"world running; plus bridge available={PlusBridge.Available}");
			Try("dismiss the start-of-game popup", () => DismissIntroPopup());
			// Corpse-borne plague is not under test; left on, it slowly empties the
			// villages the later tests need. The life cycle neither: old age kills the villagers
			// a test follows (a captive died of it mid-search). LifeSuite turns it on for its
			// own checks. In memory only: config.json is untouched.
			PlusBridge.SetConfig("corpseDiseaseEnabled", false);
			PlusBridge.SetConfig("lifeCycleEnabled", false);
			yield return WaitGameHours(1f, null);

			FreshWorldChecks();
			// Early, while villagers are out walking; leaves the camera as it found it.
			if (Runs("FireWallTest")) { yield return FireWallTest(); }
			if (Runs("PathLineTest")) { yield return PathLineTest(); }
			if (Runs("ExploitSuite")) { yield return ExploitSuite(); }
			// Needs a village with room for a Tavern-sized building; takes the fullest one.
			// First, while the villages still have free space (later Mass Graves and
			// Cemeteries fill it).
			if (Runs("TierSuite")) { yield return TierSuite(); }
			if (Runs("LifeSuite")) { yield return LifeSuite(); }
			// Needs three free villagers of one village, so it runs while the villages are
			// full. Strands them in the wilderness; one never comes back.
			if (Runs("MissingPersonsSuite")) { yield return MissingPersonsSuite(); }
			if (Runs("MassGraveSuite")) { yield return MassGraveSuite(); }
			// Knowledge takes the village with the most people left (the one the burial tests
			// spared).
			if (Runs("KnowledgeSuite")) { yield return KnowledgeSuite(); }
			if (Runs("FogSuite")) { yield return FogSuite(); }
			// Waits ~72 in-game hours, during which villages lose people to the world.
			if (Runs("DecayTest")) { yield return DecayTest(); }
			// Starves one village until famine, then feeds it; some villagers move away.
			if (Runs("FamineSuite")) { yield return FamineSuite(); }
			if (Runs("HuntSuite")) { yield return HuntSuite(); }
			if (Runs("TradeSuite")) { yield return TradeSuite(); }
			// After the burial and knowledge tests: a plague answered with Exile makes the
			// plagued Criminals, and Criminals take no village jobs (burial, construction).
			if (Runs("CurfewSuite")) { yield return CurfewSuite(); }
			// Last: it wipes a village out, which can end the world (player victory, and the
			// game repopulating empty villages with new factions).
			if (Runs("MigrationSuite")) { yield return MigrationSuite(); }
			// A measurement that burns a village down: only when asked for by name.
			if (_only.Contains("FireProbe")) { yield return FireProbe(); }

			Finish("done");
		}

		// ---------------------------------------------------------------------------------
		// Before any test touches the world.
		private void FreshWorldChecks()
		{
			if (!PlusBridge.Available)
			{
				return;
			}
			Check("framework names virtual structure", () =>
			{
				var t = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.mass_grave");
				string n = t.LocalizedStructureName();
				string e = t.ToStringEnum();
				return (n == "Mass Grave" && e == "MASS_GRAVE", $"LocalizedStructureName='{n}' ToStringEnum='{e}'");
			});
			// Freshly generated villages must count as healthy, or migration would be throttled
			// from day one. (A world can start with a village already under attack or plagued:
			// those are throttled on purpose, so they are left out.)
			List<NPCSettlement> villages = Villages();
			foreach (NPCSettlement v in villages)
			{
				float m = PlusBridge.MigrationMultiplier(v, out string why);
				Log($"  migration health {v.name}: x{m:0.##} emptyHomes={v.GetNumberOfUnoccupiedStructure(STRUCTURE_TYPE.DWELLING)} {why}");
			}
			List<NPCSettlement> calm = villages.Where(v => !v.isUnderSiege && !v.isPlagued).ToList();
			Check("freshly generated villages draw settlers normally", () =>
			{
				List<NPCSettlement> throttled = calm.Where(v => PlusBridge.MigrationMultiplier(v, out _) < 1f).ToList();
				return (calm.Count > 0 && throttled.Count == 0,
					(throttled.Count == 0 ? $"all {calm.Count} at x1" : "throttled: " + string.Join(", ", throttled.Select(v => v.name)))
					+ (calm.Count < villages.Count ? $" ({villages.Count - calm.Count} under attack or plagued, left out)" : ""));
			});
			// Nobody knows of the demons yet: the "Who Knows of You" section says so.
			if (!FactionManager.Instance.allFactions.Any(f => f != null && f.isMajorNonPlayer && f.isAwareOfPlayer))
			{
				List<string> lines = PlusBridge.KnowledgePanelLines() ?? new List<string>();
				Check("a new world's bookmarks panel says your presence is not known", () =>
					(lines.Count == 1 && lines[0] == "Your presence in the region is not known.", string.Join(" / ", lines)));
			}
		}

		private IEnumerator MassGraveSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("mass grave suite", "RuinarchPlus not loaded");
				yield break;
			}

			List<NPCSettlement> villages = Villages();
			Log($"villages: {string.Join(", ", villages.Select(Describe))}");
			STRUCTURE_TYPE pitType = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.mass_grave");
			int PitCount(NPCSettlement v) => v.structures.TryGetValue(pitType, out List<LocationStructure> l) ? l.Count(x => !x.hasBeenDestroyed) : 0;
			// No graveyard and no pit yet (earlier suites' deaths can already have villages
			// building one, which would collect the body the first test expects to lie still).
			// The Mass Grave borrows the Cemetery prefabs, so prefer a village with room for one:
			// in a crowded village no pit can ever be placed and the build tests measure nothing.
			NPCSettlement bare = villages.Where(v => !v.HasStructure(STRUCTURE_TYPE.CEMETERY) && !v.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE)
					&& PitCount(v) == 0 && !PlusBridge.HasPendingBlueprint(v) && !v.HasJob(JOB_TYPE.PLACE_BLUEPRINT))
				.OrderByDescending(v => HasRoomFor(v, STRUCTURE_TYPE.CEMETERY)).FirstOrDefault();
			NPCSettlement withCemetery = villages.FirstOrDefault(v => v.HasStructure(STRUCTURE_TYPE.CEMETERY));

			if (bare == null)
			{
				Skip("no-graveyard village tests", "every village has a Cemetery or Cult Temple");
			}
			else
			{
				yield return BareVillageTests(bare);
			}

			if (withCemetery == null && bare != null)
			{
				// No village starts with a Cemetery: build a real one. It goes in a village that
				// still has an owning faction (the game builds by faction type), preferably the
				// test village. That village also gets a Mass Grave, which tests precedence
				// (its people -> Cemetery, creatures and outsiders -> Mass Grave).
				if (bare.owner == null)
				{
					Log($"  {bare.name} has no owning faction any more (residents alive={bare.residents.Count(r => r != null && !r.isDead)})");
				}
				NPCSettlement host = new[] { bare }.Concat(villages).FirstOrDefault(v => v.owner != null && !v.HasStructure(STRUCTURE_TYPE.CEMETERY));
				LocationStructure cemetery = host == null ? null : Guard("build a vanilla Cemetery", () => InstantBuildVanilla(host, STRUCTURE_TYPE.CEMETERY));
				Log(cemetery != null ? $"  built a Cemetery in {host.name} for the precedence test" : $"  could not build a Cemetery (host={host?.name ?? "none"})");
				if (cemetery != null)
				{
					yield return Snapshot(cemetery, "cemetery");
					if (PlusBridge.FindFor(host) == null && Guard("instant-build a Mass Grave beside the Cemetery", () => PlusBridge.InstantBuild(host)) == null)
					{
						Log($"  {host.name} has no Mass Grave; creature/outsider checks will fail");
					}
				}
				withCemetery = cemetery != null ? host : null;
			}
			if (withCemetery == null)
			{
				Skip("cemetery village keeps vanilla burial", "no village has or could build a Cemetery");
			}
			else
			{
				yield return CemeteryVillageTest(withCemetery);
			}

			// The game never buries animals in a Cemetery, so a carcass lying in a village that
			// has one (and no pit yet) still calls for a Mass Grave.
			NPCSettlement graveyardOnly = villages.FirstOrDefault(v => v.owner != null && PitCount(v) == 0 && !PlusBridge.HasPendingBlueprint(v)
				&& (v.HasStructure(STRUCTURE_TYPE.CEMETERY) || v.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE))
				&& !v.HasStructure(STRUCTURE_TYPE.HUNTER_LODGE) && HasRoomFor(v, STRUCTURE_TYPE.CEMETERY));
			if (graveyardOnly == null)
			{
				Skip("a village with a Cemetery still plans a Mass Grave for a creature's carcass", "no village with a graveyard, no pit and room for one");
			}
			else
			{
				Guard("kill a creature in a village with a graveyard", () => SpawnAndKill(graveyardOnly, SUMMON_TYPE.Wolf));
				string planned = $"{graveyardOnly.name} has dead nobody will bury: queued a Mass Grave blueprint";
				yield return WaitGameHours(6f, () => ModsLogHas(planned) || PitCount(graveyardOnly) > 0);
				Check("a village with a Cemetery still plans a Mass Grave for a creature's carcass", () =>
					(ModsLogHas(planned) || PitCount(graveyardOnly) > 0, $"{Describe(graveyardOnly)} queued={ModsLogHas(planned)} pits={PitCount(graveyardOnly)}"));
			}

			// The debug menu's "Place Mass Grave" path. A village never gets a second pit.
			NPCSettlement withPit = villages.FirstOrDefault(v => PitCount(v) > 0);
			if (withPit != null)
			{
				int before = PitCount(withPit);
				LocationStructure again = Guard("instant-build in a village that has one", () => PlusBridge.InstantBuild(withPit));
				Check("a village never gets a second Mass Grave", () =>
					(PitCount(withPit) == before && again == PlusBridge.FindFor(withPit), $"{withPit.name}: pits {before} -> {PitCount(withPit)}"));
			}
			NPCSettlement withoutPit = villages.Where(v => PitCount(v) == 0).OrderByDescending(v => HasRoomFor(v, STRUCTURE_TYPE.CEMETERY)).FirstOrDefault();
			if (withoutPit != null && !HasRoomFor(withoutPit, STRUCTURE_TYPE.CEMETERY))
			{
				Skip("instant build creates a real Mass Grave in the village", $"no village without a pit has room for a 5x5 building (the game's own placement check; tried {withoutPit.name})");
			}
			else if (withoutPit != null)
			{
				LocationStructure built = Guard("instant-build a Mass Grave", () => PlusBridge.InstantBuild(withoutPit));
				Check("instant build creates a real Mass Grave in the village", () =>
					(built != null && PlusBridge.IsMassGrave(built) && built.settlementLocation == withoutPit && built.tiles.Count > 0,
					built == null ? $"no valid spot / null ({withoutPit.name} owner={withoutPit.owner?.name ?? "none"})" : $"settlement={built.settlementLocation?.name} tiles={built.tiles.Count} type={built.structureType.ToStringEnum()}"));
			}
		}

		private IEnumerator CurfewSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("curfew suite", "RuinarchPlus not loaded");
				yield break;
			}
			// The village with the most residents the curfew binds (see HomeShare): with few,
			// the home share measures nothing (a baseline of 0 of 0, or already 100%).
			NPCSettlement village = Villages().Where(v => !v.isPlagued && v.eventManager.GetActiveEvent<PlaguedEvent>() == null)
				.Select(v => (v, bound: HomeShareCount(v))).Where(x => x.bound >= 3)
				.OrderByDescending(x => PlusBridge.MigrationMultiplier(x.v, out _))
				.ThenByDescending(x => x.bound).Select(x => x.v).FirstOrDefault();
			if (village == null)
			{
				Skip("curfew", "no plague-free village with 3+ residents the curfew binds: " + string.Join("; ", Villages().Select(v => $"{v.name} bound={HomeShareCount(v)}")));
				yield break;
			}
			yield return CurfewTest(village);
		}

		private IEnumerator BareVillageTests(NPCSettlement village)
		{
			Log($"test village (no graveyard): {Describe(village)}");

			// 1. A death with no graveyard and no pit: the corpse stays where it fell.
			Character first = Guard("kill resident", () => KillResident(village));
			if (first == null)
			{
				Fail("no-scatter", "could not find a resident to kill");
				yield break;
			}
			yield return WaitGameHours(3f, null);
			Check("no-scatter: corpse lies where it fell (no wilderness tombstone)", () =>
				((first.grave == null && first.hasMarker) || PlusBridge.IsMassGrave(first.grave?.gridTileLocation?.structure),
				$"grave={(first.grave != null)} structure={first.grave?.gridTileLocation?.structure?.structureType} hasMarker={first.hasMarker}"));

			// 1b. A death just OUTSIDE village tiles: vanilla's personal "outside village" burial
			// would put this body in the wilderness; with no pit it must stay where it fell.
			Character border = Guard("kill border resident", () => KillResidentOnBorder(village));
			if (border == null)
			{
				Skip("no-scatter at the village border", "no resident standing just outside the village");
			}
			else
			{
				yield return WaitGameHours(6f, () => border.grave != null);
				Check("no-scatter at the village border (personal burial path)", () =>
					(border.grave == null || PlusBridge.IsMassGrave(border.grave.gridTileLocation?.structure),
					$"grave={(border.grave != null)} structure={border.grave?.gridTileLocation?.structure?.structureType}"));
			}

			// 2. The village decides to build a Mass Grave and villagers construct it. The
			// builder's action text is captured on the way: it must name the Mass Grave.
			float start = GameHours;
			bool queued = false;
			string buildText = null;
			yield return WaitGameHours(120f, () =>
			{
				if (!queued && (village.HasJob(JOB_TYPE.PLACE_BLUEPRINT) || PlusBridge.HasPendingBlueprint(village)))
				{
					queued = true;
					Log($"  blueprint job/pending seen after {GameHours - start:F1}h");
				}
				if (buildText == null)
				{
					// Only the Mass Grave's builder: its blueprint is the borrowed Cemetery prefab,
					// and this village has no Cemetery of its own being built.
					ActualGoapNode node = village.residents.Select(r => r?.currentActionNode)
						.FirstOrDefault(n => n?.action is BuildBlueprint && n.descriptionLog != null
							&& n.poiTarget is GenericTileObject g && g.blueprintOnTile != null && g.blueprintOnTile.structureType == STRUCTURE_TYPE.CEMETERY);
					buildText = node?.descriptionLog.logText;
				}
				// The game's own planner also places Cemeteries, and a blueprint nobody starts
				// within 24h expires: a village can end up with a Cemetery instead, which then
				// takes its dead (vanilla), so it no longer calls for a pit.
				return PlusBridge.FindFor(village) != null || HasGraveyard(village);
			});
			LocationStructure pit = PlusBridge.FindFor(village);
			bool ownGraveyard = pit == null && HasGraveyard(village);
			if (ownGraveyard)
			{
				Skip("villagers build a Mass Grave from materials", $"{village.name} built its own Cemetery or Cult Temple after {GameHours - start:F1}h (the game's planner; the Mass Grave blueprint expired: pending={PlusBridge.HasPendingBlueprint(village)})");
			}
			else if (pit == null && !HasRoomFor(village, STRUCTURE_TYPE.CEMETERY) && !PlusBridge.HasPendingBlueprint(village))
			{
				Skip("villagers build a Mass Grave from materials", queued
					? $"{village.name}'s blueprint expired unbuilt and there is no room left for another 5x5 building (the game's own placement check)"
					: $"{village.name} has no room for a 5x5 building (the game's own placement check)");
			}
			else
			{
				Check("villagers build a Mass Grave from materials", () =>
					(pit != null, pit != null ? $"built after {GameHours - start:F1}h" : $"not built within 120h (blueprint seen={queued}, pending={PlusBridge.HasPendingBlueprint(village)})"));
			}
			if (buildText == null || ownGraveyard)
			{
				Skip("the builder's action names the Mass Grave", ownGraveyard ? "the village built a Cemetery instead" : "no builder seen mid-build");
			}
			else
			{
				Check("the builder's action names the Mass Grave", () =>
					(buildText.Contains("Mass Grave") && !buildText.Contains("Cemetery"), $"\"{buildText}\""));
			}
			if (pit == null)
			{
				pit = Guard("instant-build fallback", () => PlusBridge.InstantBuild(village));
				Log(pit != null ? "  fallback: instant-built a Mass Grave to continue the suite" : "  fallback instant build failed");
				if (pit == null)
				{
					yield break;
				}
			}
			Check("mass grave is a village structure of the settlement", () =>
				(pit.settlementLocation == village && pit.structureType.IsVillageStructure() && !pit.structureType.IsPlayerStructure(),
				$"settlement={pit.settlementLocation?.name} village={pit.structureType.IsVillageStructure()} player={pit.structureType.IsPlayerStructure()} tiles={pit.tiles.Count}"));
			// Bare dirt, no art of its own: one ground tile over the whole pit (the Cemetery's
			// paved cross replaced), and nothing drawn on top of it.
			Check("the pit is bare dirt", () =>
			{
				List<string> ground = pit.tiles.Select(t => t.parentMap.groundTilemap.GetTile(t.localPlace)?.name ?? "none").Distinct().ToList();
				int drawn = (pit as ManMadeStructure)?.structureObj?.GetComponentsInChildren<SpriteRenderer>().Count(r => r.enabled && r.sprite != null && r.gameObject.name.StartsWith("RuinarchPlus")) ?? 0;
				return (ground.Count == 1 && drawn == 0, $"ground tiles: {string.Join(", ", ground)}; mod sprites={drawn}");
			});
			yield return Snapshot(pit, "massgrave");
			Check("the pit carries none of the Cemetery's props", () =>
			{
				List<TileObject> objs = pit.tiles.Where(t => t.tileObjectComponent.objHere != null).Select(t => t.tileObjectComponent.objHere).ToList();
				string listed = string.Join(", ", objs.GroupBy(o => $"{o.tileObjectType}{(o.isPreplaced ? " (prefab)" : "")}").Select(g => $"{g.Key} x{g.Count()}"));
				return (!objs.Any(o => o.isPreplaced && o.tileObjectType != TILE_OBJECT_TYPE.STRUCTURE_TILE_OBJECT), objs.Count == 0 ? "empty" : listed);
			});

			// 3. Villagers carry a fresh corpse into the pit.
			int hauledBefore = PlusBridge.HauledTotal;
			Character second = Guard("kill resident", () => KillResident(village));
			if (second == null)
			{
				Skip("villagers haul a resident", "no living resident left");
			}
			else
			{
				yield return WaitGameHours(48f, () => second.grave != null || !second.hasMarker);
				// The game's own planner may have built a Cemetery meanwhile: then the body rightly
				// goes there (vanilla burial), and there is nothing of the pit's to check.
				if (second.grave?.gridTileLocation?.structure?.structureType == STRUCTURE_TYPE.CEMETERY)
				{
					Skip("villagers haul a resident's corpse into the pit (no gravestone)", $"the game built a Cemetery in {village.name} meanwhile and the body was buried there");
				}
				// Laid in the pit = carried there and gone: no body, no gravestone anywhere.
				else Check("villagers haul a resident's corpse into the pit (no gravestone)", () =>
				{
					bool gone = !second.hasMarker && second.grave == null;
					bool hauled = PlusBridge.HauledTotal > hauledBefore;
					return (gone && hauled, $"gone={gone} hauledDelta={PlusBridge.HauledTotal - hauledBefore} absorbed={PlusBridge.AbsorbedTotal} jobQueued={second.HasJobTargetingThis(JOB_TYPE.BURY, JOB_TYPE.BURY_IN_ACTIVE_PARTY) || village.HasJob(JOB_TYPE.BURY, second)} {GraveWhere(second, village)}");
				});
			}

			// The corpse that was lying before the pit existed must be taken too.
			yield return WaitGameHours(24f, () => first.grave != null || !first.hasMarker);
			if (first.grave?.gridTileLocation?.structure?.structureType == STRUCTURE_TYPE.CEMETERY)
			{
				Skip("pre-existing corpse is laid in the pit once it exists", $"the game built a Cemetery in {village.name} meanwhile and the body was buried there");
			}
			else Check("pre-existing corpse is laid in the pit once it exists", () =>
			{
				bool gone = !first.hasMarker && first.grave == null;
				return (gone, $"gone={gone} hasMarker={first.hasMarker} {GraveWhere(first, village)}");
			});

			// 4. A creature carcass in the village is disposed of in the pit.
			int hauledBeforeCreature = PlusBridge.HauledTotal;
			int absorbedBeforeCreature = PlusBridge.AbsorbedTotal;
			Character beast = Guard("spawn creature", () => SpawnAndKill(village, SUMMON_TYPE.Wolf));
			if (beast == null)
			{
				Fail("creature disposal", "could not spawn a creature");
			}
			else if (beast.race.IsSkinnable() && village.HasStructureOfTypeThatIsAssigned(STRUCTURE_TYPE.HUNTER_LODGE))
			{
				Skip("creature disposal", "village has a working Hunter Lodge (hunters skin carcasses)");
			}
			else
			{
				yield return WaitGameHours(24f, () => !beast.hasMarker);
				Check("villagers carry a creature carcass into the pit", () =>
					(!beast.hasMarker && PlusBridge.HauledTotal > hauledBeforeCreature,
					$"hasMarker={beast.hasMarker} hauledDelta={PlusBridge.HauledTotal - hauledBeforeCreature} absorbedDelta={PlusBridge.AbsorbedTotal - absorbedBeforeCreature}"));
			}

			// 5. A carcass out in the surroundings (next map area over, off village land).
			LocationGridTile outside = village.areas.SelectMany(a => a.neighbourComponent.neighbours).Distinct()
				.Where(n => !village.areas.Contains(n)).Select(n => n.gridTileComponent.centerGridTile)
				.FirstOrDefault(t => t != null && !t.isOccupied && !t.IsNextToOrPartOfSettlement(village) && t.structure.structureType == STRUCTURE_TYPE.WILDERNESS);
			if (outside == null)
			{
				Skip("villagers fetch a carcass from the village's surroundings", "no free wilderness tile in a neighbouring area");
			}
			else
			{
				int hauledBeforeOut = PlusBridge.HauledTotal;
				Character far = Guard("spawn creature outside", () => SpawnAndKillAt(outside, SUMMON_TYPE.Wolf));
				if (far != null && !(far.race.IsSkinnable() && village.HasStructureOfTypeThatIsAssigned(STRUCTURE_TYPE.HUNTER_LODGE)))
				{
					float d = outside.GetDistanceTo(pit.tiles.First());
					yield return WaitGameHours(48f, () => !far.hasMarker);
					Check("villagers fetch a carcass from the village's surroundings", () =>
						(!far.hasMarker && PlusBridge.HauledTotal > hauledBeforeOut,
						$"at {outside.localPlace} ({d:F0} tiles from the pit) hasMarker={far.hasMarker} hauledDelta={PlusBridge.HauledTotal - hauledBeforeOut} jobQueued={village.HasJob(JOB_TYPE.BURY, far)}"));
				}
			}

			Check("the pit shows no gravestones", () =>
			{
				int stones = pit.tiles.Count(t => t.tileObjectComponent.objHere is Tombstone);
				return (stones == 0, $"{stones} tombstone(s) on the pit");
			});
			Log($"  pit bodyCount={PlusBridge.BodyCount(pit)} hauledTotal={PlusBridge.HauledTotal} absorbedTotal={PlusBridge.AbsorbedTotal}");
		}

		private IEnumerator CurfewTest(NPCSettlement village)
		{
			Log($"curfew test village: {Describe(village)}");
			Character decider = village.owner?.leader is Character leader && leader.homeSettlement == village ? leader : village.ruler;
			if (decider == null)
			{
				Skip("curfew", "village has no ruler to decide");
				yield break;
			}
			// Make the decision a measured one: drop the traits that pick Slay or Do_Nothing,
			// and give the village a Hospice so the ruler quarantines rather than exiles.
			// Exile makes the plagued Criminals and drives them out, which empties the village
			// for every later test; the curfew is the same for both responses.
			if (!village.HasStructure(STRUCTURE_TYPE.HOSPICE))
			{
				LocationStructure hospice = Guard("build a Hospice", () => InstantBuildVanilla(village, STRUCTURE_TYPE.HOSPICE));
				Log(hospice != null ? $"  built a Hospice in {village.name}" : "  could not build a Hospice; the ruler may exile");
			}
			foreach (string trait in new[] { "Evil", "Psychopath", "Ruthless", "Demon Cultist", "Coward", "Lazy" })
			{
				if (decider.traitContainer.HasTrait(trait))
				{
					decider.traitContainer.RemoveTrait(decider, trait);
				}
			}
			yield return WaitForHour(20);
			float baseline = HomeShare(village, out int baselineCount);
			Log($"  free-time home share before plague: {baseline:P0} of {baselineCount}");

			if (Guard("start plague event", () => { village.eventManager.AddNewActiveEvent(SETTLEMENT_EVENT.Plagued_Event); return village.eventManager.GetActiveEvent<PlaguedEvent>(); }) is PlaguedEvent plague)
			{
				yield return WaitGameHours(12f, () => plague.hasLeaderMadeADecision);
				Check("ruler's measured plague response imposes a curfew", () =>
					(PlusBridge.IsUnderCurfew(village), $"decider={decider.name} decided={plague.hasLeaderMadeADecision} response={plague.rulerDecision}"));
				Check("curfew is announced", () =>
				{
					string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
					bool announced = File.Exists(mods) && File.ReadAllText(mods).Contains($"has placed {village.name} under curfew");
					return (announced, announced ? "mods.log has the announcement" : "no announcement in mods.log");
				});
				// Free time is a stretch of hours and people walk home; take the best of three
				// hourly samples rather than one moment.
				yield return WaitForHour(20);
				float under = 0f;
				int count = 0;
				for (int sample = 0; sample < 3; sample++)
				{
					yield return WaitGameHours(1f, null);
					float share = HomeShare(village, out int n);
					if (share >= under)
					{
						under = share;
						count = n;
					}
				}
				if (baseline >= 0.6f)
				{
					Skip("under curfew, residents stay home in their free time", $"{baseline:P0} of {baselineCount} were home before the plague already; nothing to measure (under curfew {under:P0})");
				}
				else
				{
					Check("under curfew, residents stay home in their free time", () =>
						(count > 0 && under > baseline && (under >= 0.6f || under - baseline >= 0.25f), $"home share {under:P0} of {count} (before plague {baseline:P0})"));
				}
				yield return ClosedBordersTest(village);
				Guard("end plague event", () => { village.eventManager.DeactivateEvent(plague); return plague; });
				Check("curfew lifts when the plague event ends", () =>
					(!PlusBridge.IsUnderCurfew(village), $"underCurfew={PlusBridge.IsUnderCurfew(village)}"));
			}
		}

		// Phase 2: a village under curfew turns visitors away. An outsider (a villager of
		// another village, not hostile) is placed by the village: the village is not among the
		// places they may visit (it is with the feature off), and a visit already under way is
		// given up.
		private IEnumerator ClosedBordersTest(NPCSettlement village)
		{
			Character outsider = Villages().Where(v => v != village && v.owner != null && (v.owner == village.owner || !v.owner.IsHostileWith(village.owner)))
				.SelectMany(v => v.residents).FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.isNormalCharacter && r.race.IsSapient()
					&& r.limiterComponent.canMove && !r.partyComponent.hasParty && r.carryComponent.isBeingCarriedBy == null && r != r.homeSettlement?.ruler && !r.isFactionLeader
					&& !r.crimeComponent.IsWantedBy(village.owner));
			LocationGridTile edge = village.cityCenter?.passableTiles.FirstOrDefault(t => !t.isOccupied);
			if (outsider == null || edge == null)
			{
				Skip("a village under curfew turns visitors away", outsider == null ? "no free outsider from a friendly village" : "no free tile in the village");
				yield break;
			}
			NPCSettlement home = outsider.homeSettlement as NPCSettlement;
			Guard("bring an outsider to the village", () => { CharacterManager.Instance.Teleport(outsider, edge); return outsider; });
			List<NPCSettlement> open = new List<NPCSettlement>();
			List<NPCSettlement> closed = new List<NPCSettlement>();
			PlusBridge.SetConfig("closedBordersEnabled", false);
			Try("list villages to visit (feature off)", () => village.region.PopulateValidVillagesToVisit(outsider, open, outsider.faction));
			PlusBridge.SetConfig("closedBordersEnabled", true);
			Try("list villages to visit", () => village.region.PopulateValidVillagesToVisit(outsider, closed, outsider.faction));
			if (!open.Contains(village))
			{
				Skip("a village under curfew is not a place to visit", $"{village.name} is not a candidate for {outsider.name} even without the curfew ({string.Join(", ", open.Select(v => v.name))})");
			}
			else
			{
				Check("a village under curfew is not a place to visit", () =>
					(!closed.Contains(village), $"{outsider.name} of {home?.name}: candidates [{string.Join(", ", closed.Select(v => v.name))}]"));
			}
			Guard("start a visit", () => { outsider.behaviourComponent.VisitVillage(outsider, village); return outsider; });
			yield return WaitGameHours(3f, () => outsider.behaviourComponent.targetVisitVillage == null);
			Check("a visit to a village under curfew is given up", () =>
				(outsider.behaviourComponent.targetVisitVillage == null && ModsLogHas($"{outsider.name} was turned away from {village.name}"),
				$"{outsider.name} visiting={outsider.behaviourComponent.targetVisitVillage?.name ?? "nobody"} at {outsider.gridTileLocation?.area?.GetFirstNPCSettlementOnArea()?.name ?? "the wild"}"));
		}

		private IEnumerator MigrationSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("migration health", "RuinarchPlus not loaded");
				yield break;
			}
			List<NPCSettlement> villages = Villages();
			// A calm village: plague or a siege (correctly) zeroes migration and would mask
			// what is measured. Earlier tests may leave empty homes; the checks below scale
			// their expectations by the village's health factor, so that is fine.
			// Villagers, not the monsters some villages house (a Golem's death moves no meter).
			bool Candidate(NPCSettlement v) => v.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()) >= 4 && !v.isPlagued
				&& v.eventManager.GetActiveEvent<PlaguedEvent>() == null;
			NPCSettlement village = villages.Where(v => Candidate(v) && !v.isUnderSiege)
				.OrderByDescending(v => PlusBridge.MigrationMultiplier(v, out _))
				// Several homes, so killing people empties some (a village of one shared house
				// never looks abandoned).
				.ThenByDescending(v => v.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> homes) ? homes.Count(h => !h.hasBeenDestroyed) : 0)
				.FirstOrDefault();
			if (village == null)
			{
				// The knowledge test sets a faction hunting the player, which can leave its
				// village flagged as under siege. Lift that flag (the siege rule itself is not
				// what is measured here).
				village = villages.FirstOrDefault(v => Candidate(v) && v.isUnderSiege);
				if (village != null)
				{
					village.SetIsUnderSiege(false);
					Log($"  lifted the siege flag left on {village.name} by the knowledge test");
				}
			}
			if (village == null)
			{
				Skip("migration health", "no calm village with 4+ residents left: " + string.Join("; ", villages.Select(v => $"{Describe(v)} siege={v.isUnderSiege} plagued={v.isPlagued} x{PlusBridge.MigrationMultiplier(v, out string why):0.##} {why}")));
				yield break;
			}
			yield return MigrationTest(village);
		}

		// Migration follows the village's fortunes. Gains are measured by feeding the meter a
		// fixed amount through the game's own funnel, with the feature off (vanilla) and on.
		private IEnumerator MigrationTest(NPCSettlement village)
		{
			Log($"migration test village: {Describe(village)}");
			SettlementVillageMigrationComponent meter = village.migrationComponent;
			int Gain(bool on)
			{
				PlusBridge.SetConfig("migrationHealthEnabled", on);
				meter.SetVillageMigrationMeter(0);
				meter.IncreaseVillageMigrationMeter(100);
				PlusBridge.SetConfig("migrationHealthEnabled", true);
				return meter.villageMigrationMeter;
			}

			int vanilla = Gain(false);
			float health = PlusBridge.MigrationMultiplier(village, out string healthWhy);
			// Fresh villages (x1 gains exactly as vanilla) are covered at the start of the run;
			// here the gain must be vanilla scaled by whatever state the village is in.
			Check("a village gains vanilla migration times its health factor", () =>
			{
				int mod = Gain(true);
				return (vanilla > 0 && health > 0f && mod == (int)(vanilla * health), $"vanilla={vanilla} mod={mod} x{health:0.##} {healthWhy}");
			});

			if (Guard("start plague event", () => { village.eventManager.AddNewActiveEvent(SETTLEMENT_EVENT.Plagued_Event); return village.eventManager.GetActiveEvent<PlaguedEvent>(); }) is PlaguedEvent plague)
			{
				int during = Gain(true);
				string tooltip = meter.GetHoverTextOfMigrationMeter();
				Guard("end plague event", () => { village.eventManager.DeactivateEvent(plague); return plague; });
				Check("plague keeps settlers away", () => (during == 0, $"gain during plague={during} (vanilla {vanilla})"));
				Check("the migration tooltip says why", () => (tooltip.Contains("Settlers stay away: plague"), tooltip.Replace("\n", " | ")));
			}

			meter.SetVillageMigrationMeter(1000);
			Character victim = Guard("kill resident", () => KillResident(village));
			int afterDeath = meter.villageMigrationMeter;
			Check("a resident's death sets the migration meter back", () =>
				(victim != null && afterDeath == 1000 - 150, $"meter 1000 -> {afterDeath}"));
			if (victim != null && victim.gridTileLocation != null && victim.gridTileLocation.IsPartOfSettlement(village))
			{
				// The death may also have emptied the victim's home; the body must halve the
				// gain on top of whatever else applies.
				int withBody = Gain(true);
				float m = PlusBridge.MigrationMultiplier(village, out string w);
				int homesFactor = w != null && w.Contains("abandoned home") ? int.Parse(w.Split(' ')[0]) : 0;
				int expected = (int)(vanilla * Math.Pow(0.5, homesFactor + 1));
				Check("an unburied body in the village halves migration", () =>
					(withBody == expected && w != null && w.Contains("1 unburied body"), $"gain {withBody}, expected {expected} (vanilla {vanilla}); x{m:0.##} {w}"));
			}
			else
			{
				Skip("an unburied body in the village halves migration", "victim died outside the village tiles");
			}

			// Wipe the village out down to its ruler: homes empty, meter drained.
			Character keep = village.ruler ?? village.residents.FirstOrDefault(r => r != null && !r.isDead);
			List<Character> doomed = village.residents.Where(r => r != null && !r.isDead && r != keep).ToList();
			foreach (Character r in doomed)
			{
				Guard("kill " + r.name, () => { r.Death("autotest"); return r; });
			}
			yield return WaitGameHours(1f, null);
			float collapsed = PlusBridge.MigrationMultiplier(village, out string collapseWhy);
			int gainCollapsed = Gain(true);
			Log($"  killed {doomed.Count}; alive={village.residents.Count(r => r != null && !r.isDead)} emptyHomes={village.GetNumberOfUnoccupiedStructure(STRUCTURE_TYPE.DWELLING)}");
			Check("a village that lost most of its people draws no settlers", () =>
				(gainCollapsed == 0 && collapseWhy != null, $"gain {gainCollapsed} (vanilla {vanilla}); x{collapsed:0.##} {collapseWhy}"));
		}

		// Share of the village's curfew-bound residents (alive, not ruler/leader, with a home)
		// currently in their home structure. The plagued and quarantined do not count: they
		// are held or cared for elsewhere by design. (Jobs cannot be filtered out: the
		// curfew itself sends people home through an IDLE_RETURN_HOME job.)
		private static float HomeShare(NPCSettlement village, out int count)
		{
			List<Character> bound = village.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && !r.isSettlementRuler
				&& !r.isFactionLeader && r.homeStructure != null && !r.homeStructure.hasBeenDestroyed
				&& !r.traitContainer.HasTrait("Plagued", "Quarantined")).ToList();
			count = bound.Count;
			return count == 0 ? 0f : bound.Count(r => r.isAtHomeStructure) / (float)count;
		}

		private static int HomeShareCount(NPCSettlement village)
		{
			HomeShare(village, out int count);
			return count;
		}

		// Waits until the in-game clock reaches the given hour (the next time it comes round).
		private IEnumerator WaitForHour(int hour)
		{
			yield return WaitGameHours(25f, () => GameManager.Instance.Today().tick / GameManager.ticksPerHour == hour);
		}

		private IEnumerator CemeteryVillageTest(NPCSettlement village)
		{
			Log($"test village (has cemetery): {Describe(village)}");
			Character dead = Guard("kill resident", () => KillResident(village));
			if (dead == null)
			{
				Skip("cemetery village keeps vanilla burial", "no resident to kill");
				yield break;
			}
			// Record every burial job on the body (who queued it, where it goes), so a wrong
			// destination can be traced to its source.
			HashSet<string> buryJobs = new HashSet<string>();
			yield return WaitGameHours(24f, () =>
			{
				foreach (JobQueueItem j in dead.allJobsTargetingThis.Where(j => j.jobType == JOB_TYPE.BURY || j.jobType == JOB_TYPE.BURY_IN_ACTIVE_PARTY))
				{
					OtherData[] data = (j as GoapPlanJob)?.GetOtherDataFor(INTERACTION_TYPE.BURY_CHARACTER);
					string owner = j.originalOwner is Character c ? "char " + c.name + " of " + (c.homeSettlement?.name ?? "no home") : j.originalOwner is NPCSettlement s ? "settlement " + s.name : "other";
					buryJobs.Add($"{j.jobType} by {owner} -> {((data != null && data.Length > 0 ? data[0]?.obj : null) as LocationStructure)?.structureType.ToString() ?? "?"} (taker {j.assignedCharacter?.name ?? "-"})");
				}
				return dead.grave != null;
			});
			STRUCTURE_TYPE? graveIn = dead.grave?.gridTileLocation?.structure?.structureType;
			if (graveIn != null && graveIn != STRUCTURE_TYPE.CEMETERY && buryJobs.Count > 0 && buryJobs.All(j => j.Contains("-> CEMETERY"))
				&& village.structures.TryGetValue(STRUCTURE_TYPE.CEMETERY, out List<LocationStructure> cemeteries) && cemeteries.All(c => c.unoccupiedTiles.Count == 0))
			{
				// The burial went to the Cemetery; with no free tile left there the game itself
				// puts the gravestone next to it.
				Skip("cemetery village still buries its dead in the Cemetery", $"the Cemetery is full: grave in {graveIn}; jobs: {string.Join("; ", buryJobs)}");
			}
			else
			{
				Check("cemetery village still buries its dead in the Cemetery", () =>
				{
					STRUCTURE_TYPE? where = dead.grave?.gridTileLocation?.structure?.structureType;
					return (where == STRUCTURE_TYPE.CEMETERY, $"grave={(dead.grave != null)} structure={where} jobs: {(buryJobs.Count == 0 ? "none seen" : string.Join("; ", buryJobs))}");
				});
			}

			if (PlusBridge.FindFor(village) == null)
			{
				yield break;
			}
			int hauledBefore = PlusBridge.HauledTotal;
			Character beast = Guard("spawn creature", () => SpawnAndKill(village, SUMMON_TYPE.Wolf));
			if (beast == null)
			{
				yield break;
			}
			yield return WaitGameHours(24f, () => !beast.hasMarker);
			Check("with a Cemetery too, creature carcasses still go to the Mass Grave", () =>
				(!beast.hasMarker && PlusBridge.HauledTotal > hauledBefore, $"hasMarker={beast.hasMarker} hauledDelta={PlusBridge.HauledTotal - hauledBefore}"));

			// An outsider (a resident of another village) who dies here goes to the Mass Grave,
			// not the Cemetery: the Cemetery is for the village's own people.
			Character outsider = Villages().Where(v => v != village).SelectMany(v => v.residents)
				.FirstOrDefault(r => r != null && !r.isDead && r.marker != null && !r.partyComponent.hasParty && r.isNormalCharacter);
			LocationGridTile spot = village.cityCenter.tiles.FirstOrDefault(t => t != null && !t.isOccupied);
			if (outsider == null || spot == null)
			{
				Skip("an outsider who dies in a Cemetery village goes to the Mass Grave", outsider == null ? "no resident of another village" : "no free tile");
				yield break;
			}
			int hauledBeforeOutsider = PlusBridge.HauledTotal;
			Guard("bring an outsider and kill them", () => { outsider.marker.PlaceMarkerAt(spot); outsider.Death("autotest"); return outsider; });
			Log($"  killed outsider {outsider.name} of {outsider.previousCharacterDataComponent?.homeSettlementOnDeath?.name} in {village.name}");
			yield return WaitGameHours(24f, () => !outsider.hasMarker || outsider.grave != null);
			Check("an outsider who dies in a Cemetery village goes to the Mass Grave", () =>
			{
				STRUCTURE_TYPE? where = outsider.grave?.gridTileLocation?.structure?.structureType;
				bool inPit = !outsider.hasMarker && outsider.grave == null && PlusBridge.HauledTotal > hauledBeforeOutsider;
				return (inPit, $"hasMarker={outsider.hasMarker} grave={where?.ToString() ?? "-"} hauledDelta={PlusBridge.HauledTotal - hauledBeforeOutsider}");
			});
		}

		// An unburied body away from any village rots through the stages and disappears.
		private IEnumerator DecayTest()
		{
			Area wild = GridMap.Instance.mainRegion.areas.FirstOrDefault(a => !a.IsNextToOrPartOfVillage()
				&& a.gridTileComponent.centerGridTile != null && !a.gridTileComponent.centerGridTile.isOccupied
				&& a.gridTileComponent.centerGridTile.structure is Wilderness);
			if (wild == null)
			{
				Skip("corpse decay", "no free wilderness tile");
				yield break;
			}
			Character body = Guard("spawn creature in the wild", () => SpawnAndKillAt(wild.gridTileComponent.centerGridTile, SUMMON_TYPE.Wolf));
			if (body == null)
			{
				yield break;
			}
			string firstStage = null;
			yield return WaitGameHours(2f, () => (firstStage = PlusBridge.DecayStage(body)) != null);
			Check("unburied corpse is tracked by decay", () => (firstStage != null, "stage=" + (firstStage ?? "untracked")));

			// A Mummified body is preserved: it is never tracked, and outlasts the decay time.
			LocationGridTile beside = wild.gridTileComponent.centerGridTile.neighbourList.FirstOrDefault(t => t != null && !t.isOccupied && t.structure is Wilderness);
			Character mummy = beside == null ? null : Guard("spawn a creature to mummify", () => SpawnAndKillAt(beside, SUMMON_TYPE.Wolf));
			bool mummified = mummy != null && Guard("mummify it", () => { mummy.traitContainer.AddTrait(mummy, "Mummified"); return mummy; }) != null
				&& mummy.traitContainer.HasTrait("Mummified");
			float start = GameHours;
			string lastStage = firstStage;

			// Hovering the body shows its decay bar (the game's map HP bar), as full as the
			// share of decay time left. The screen is saved as decaybar.png to look at.
			yield return WaitGameHours(40f, () => PlusBridge.DecayStage(body) == "Bloated" || !body.hasMarker);
			if (body.hasMarker)
			{
				Camera cam = InnerMapCameraMove.Instance.camera;
				float zoom = cam.orthographicSize;
				Guard("hover the body", () => { body.CenterOnCharacter(); cam.orthographicSize = 4f; return body; });
				// The real mouse cursor also sets the hovered object (pointer enter/exit on
				// whatever lands under it after the camera moves), so keep hovering the body
				// until the bar is up, for at most 10 frames.
				float fill = -1f;
				object hoveredInstead = null;
				for (int frame = 0; frame < 10 && fill < 0f; frame++)
				{
					InnerMapManager.Instance.SetCurrentlyHoveredPOI(body);
					yield return null;
					fill = PlusBridge.DecayBarFill(body);
					hoveredInstead = InnerMapManager.Instance.currentlyHoveredPoi != body ? InnerMapManager.Instance.currentlyHoveredPoi : null;
				}
				float left = PlusBridge.DecayRemaining(body);
				yield return new WaitForEndOfFrame();
				Texture2D shot = null;
				try
				{
					shot = ScreenCapture.CaptureScreenshotAsTexture();
					File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(_logPath), "decaybar.png"), shot.EncodeToPNG());
					Log("  screenshot saved: decaybar.png");
				}
				catch (Exception e)
				{
					Log($"  screenshot decaybar failed: {e.Message}");
				}
				finally
				{
					if (shot != null) Destroy(shot);
				}
				Check("hovering a corpse shows how much of its decay is left", () =>
					(fill >= 0f && left >= 0f && Math.Abs(fill - left) < 0.05f && fill < 0.8f && fill > 0.3f, $"bar fill={fill:F2} decay left={left:F2} stage={PlusBridge.DecayStage(body)}{(hoveredInstead != null ? " hovered instead: " + hoveredInstead : "")}"));
				InnerMapManager.Instance.SetCurrentlyHoveredPOI(null);
				yield return null;
				Check("the decay bar goes away when the corpse is no longer hovered", () =>
				{
					float after = PlusBridge.DecayBarFill(body);
					return (after < 0f, after < 0f ? "hidden" : $"still showing ({after:F2})");
				});
				cam.orthographicSize = zoom;
			}
			yield return WaitGameHours(100f, () =>
			{
				string st = PlusBridge.DecayStage(body);
				if (st != null && st != lastStage)
				{
					Log($"  decay stage -> {st} after {GameHours - start:F1}h");
					lastStage = st;
				}
				return !body.hasMarker;
			});
			Check("unburied corpse decomposes and disappears", () =>
				(!body.hasMarker, $"hasMarker={body.hasMarker} lastStage={lastStage} after {GameHours - start:F1}h"));
			if (!mummified)
			{
				Skip("a mummified body does not decay", mummy == null ? "no creature spawned beside the first" : "the Mummified status did not take");
			}
			else
			{
				Check("a mummified body does not decay", () =>
					(mummy.hasMarker && PlusBridge.DecayStage(mummy) == null, $"hasMarker={mummy.hasMarker} stage={PlusBridge.DecayStage(mummy) ?? "untracked"}"));
			}
		}

		// Phase 5: settlements grow. The fullest village with room for a Town Hall (the
		// Tavern's footprint) is made to qualify by lowering townPopulation to just under its
		// size. Its villagers place the blueprint, gather materials and build the hall, and
		// it becomes a Town; then a City and back; finally the Town Hall is destroyed.
		private IEnumerator TierSuite()
		{
			if (!PlusBridge.Available || PlusBridge.Tier(null) == null)
			{
				Skip("settlement tiers", "RuinarchPlus (with tiers) not loaded");
				yield break;
			}
			NPCSettlement village = Villages().Where(v => PlusBridge.TownHallFor(v) == null && HasRoomFor(v, STRUCTURE_TYPE.TAVERN))
				.OrderByDescending(Villagers).FirstOrDefault();
			if (village == null)
			{
				Skip("settlement tiers", "no village has room for a Town Hall (the Tavern's footprint): " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			int baseDwellings = village.settlementType.maxDwellings;
			int baseFacilities = village.settlementType.maxFacilities;
			int townMark = Math.Max(1, Villagers(village) - 2);
			Log($"tier test village: {Describe(village)} villagers={Villagers(village)} caps={baseDwellings}/{baseFacilities} townPopulation={townMark}");
			PlusBridge.SetConfig("cityPopulation", 1000);
			PlusBridge.SetConfig("townPopulation", townMark);

			// 1. Built by villagers. A blueprint seen on the way is saved and reloaded.
			float start = GameHours;
			yield return WaitGameHours(120f, () => PlusBridge.HasPendingTownHall(village) || PlusBridge.TownHallFor(village) != null);
			if (PlusBridge.HasPendingTownHall(village))
			{
				Log($"  Town Hall blueprint placed after {GameHours - start:F1}h");
				string json = null;
				string entries = null;
				yield return SaveAndRead("ruinarch.plus.blueprints.json", (j, e) => { json = j; entries = e; });
				Check("a Town Hall under construction is stored inside the player's save file", () =>
					(json != null && json.Contains("ruinarch.plus.town_hall|"), json ?? "entries: " + entries));
				if (json != null)
				{
					ReplayLoad("ruinarch.plus.blueprints.json", json);
					Check("a Town Hall under construction is still one after the save loads", () =>
						(PlusBridge.HasPendingTownHall(village), $"pending after load={PlusBridge.HasPendingTownHall(village)}"));
				}
				yield return WaitGameHours(120f - (GameHours - start), () => PlusBridge.TownHallFor(village) != null);
			}
			LocationStructure hall = PlusBridge.TownHallFor(village);
			if (hall == null && Villagers(village) < 3)
			{
				foreach (string name in new[] { "a grown village builds a Town Hall from materials", "the village is a Town once its Town Hall stands", "the Town is announced",
					"the settlement panel names the tier", "the center's building panel names what the village is", "the tier is stored inside the player's save file",
					"a Town reaching the city mark becomes a City", "a City keeps its tier just under the mark",
					"a City far below the mark falls back to a Town", "destroying the Town Hall makes the Town a village again" })
				{
					Skip(name, $"{village.name} emptied during the build: {Describe(village)}");
				}
				RestoreTierConfig();
				yield break;
			}
			Check("a grown village builds a Town Hall from materials", () =>
				(hall != null, hall != null ? $"built after {GameHours - start:F1}h" : $"not built within 120h (pending={PlusBridge.HasPendingTownHall(village)} placeJob={village.HasJob(JOB_TYPE.PLACE_BLUEPRINT)} villagers={Villagers(village)} {BlueprintState(village)})"));
			if (hall == null)
			{
				hall = Guard("instant-build a Town Hall", () => PlusBridge.InstantBuildTownHall(village));
				Log(hall != null ? "  fallback: instant-built a Town Hall to continue the suite" : "  fallback instant build failed");
				if (hall == null)
				{
					RestoreTierConfig();
					yield break;
				}
			}

			// 2. A Town: bigger limits for the game's build planner, named in its panel.
			yield return WaitGameHours(2f, () => PlusBridge.Tier(village) == "Town");
			Check("the village is a Town once its Town Hall stands", () =>
				(PlusBridge.Tier(village) == "Town" && village.settlementType.maxDwellings == baseDwellings + 8 && village.settlementType.maxFacilities == baseFacilities + 4,
				$"tier={PlusBridge.Tier(village)} caps={village.settlementType.maxDwellings}/{village.settlementType.maxFacilities} (base {baseDwellings}/{baseFacilities}) villagers={Villagers(village)}"));
			Check("the Town is announced", () => (ModsLogHas($"{village.name} has grown into a Town"), "mods.log"));
			string panel = Guard("open the settlement panel", () =>
			{
				UIManager.Instance.ShowSettlementInfo(village);
				string text = (AccessTools.Field(typeof(SettlementInfoUI), "typeLbl").GetValue(UIManager.Instance.settlementInfoUI) as TMPro.TMP_Text)?.text;
				UIManager.Instance.settlementInfoUI.CloseMenu();
				return text;
			});
			// A faction leader's home village, in a faction of several villages, is its Capital.
			bool capital = village.owner?.leader is Character leader && leader.homeSettlement == village
				&& village.owner.ownedSettlements.Count(o => o is NPCSettlement { locationType: LOCATION_TYPE.VILLAGE }) > 1;
			Check("the settlement panel names the tier", () => (panel != null && panel.EndsWith(capital ? " Capital" : " Town"), $"\"{panel}\" (capital={capital})"));
			// The center's building panel: the "Village" line says what the village is now.
			string[] center = Guard("open the center's building panel", () =>
			{
				UIManager.Instance.ShowStructureInfo(village.cityCenter);
				StructureInfoUI ui = UIManager.Instance.structureInfoUI;
				// Open the Info tab (a toggle labelled "Info"), as a player would, for the screenshot.
				UnityEngine.UI.Toggle info = ui.GetComponentsInChildren<UnityEngine.UI.Toggle>(true)
					.FirstOrDefault(t => t.GetComponentInChildren<TMPro.TMP_Text>(true)?.text == "Info");
				if (info != null) info.isOn = true;
				TMPro.TMP_Text value = AccessTools.Field(typeof(StructureInfoUI), "villageLbl").GetValue(ui) as TMPro.TMP_Text;
				GameObject row = AccessTools.Field(typeof(StructureInfoUI), "villageParentGO").GetValue(ui) as GameObject;
				TMPro.TMP_Text headerLbl = row?.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t != value);
				string header = headerLbl?.text;
				Log("  building panel header components: " + string.Join(", ", headerLbl?.GetComponents<Component>().Select(c => c.GetType().Name + (c is Behaviour b ? (b.enabled ? "" : " (off)") : "")) ?? new string[0]));
				string description = (AccessTools.Field(typeof(StructureInfoUI), "cityCenterDescriptionLbl").GetValue(ui) as TMPro.TMP_Text)?.text;
				return new[] { header, description };
			});
			yield return new WaitForSecondsRealtime(3f);
			yield return Screenshot("centerpanel.png");
			Guard("close the building panel", () => { UIManager.Instance.structureInfoUI.CloseMenu(); return village; });
			Check("the center's building panel names what the village is", () =>
				(center != null && center[0] == (capital ? "Capital" : "Town") && center[1] != null && center[1].Contains("Town Center"),
				$"header=\"{center?[0]}\" (capital={capital}) description=\"{center?[1]}\""));

			// The center's Residents tab lists the whole village (a Ruinarch+ fix: nobody lives
			// in the center itself); a dwelling's lists its own household, as in the base game.
			Func<LocationStructure, int> residentsShown = structure =>
			{
				UIManager.Instance.ShowStructureInfo(structure);
				StructureInfoUI sui = UIManager.Instance.structureInfoUI;
				UnityEngine.UI.Toggle tab = AccessTools.Field(typeof(StructureInfoUI), "residentsTab").GetValue(sui) as UnityEngine.UI.Toggle;
				if (tab != null) tab.isOn = true;
				AccessTools.Method(typeof(StructureInfoUI), "UpdateResidents").Invoke(sui, null);
				UnityEngine.UI.ScrollRect list = AccessTools.Field(typeof(StructureInfoUI), "charactersScrollView").GetValue(sui) as UnityEngine.UI.ScrollRect;
				return list == null ? -1 : list.content.GetComponentsInChildren<CharacterPortrait>().Length;
			};
			int villageShown = Guard("open the center's Residents tab", () => (object)residentsShown(village.cityCenter)) as int? ?? -1;
			yield return new WaitForSecondsRealtime(1f);
			yield return Screenshot("residents.png");
			int alive = village.residents.Count(c => c != null && !c.isDead);
			LocationStructure dwelling = village.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> homes) ? homes.FirstOrDefault(h => h.residents.Count > 0) : null;
			int houseShown = dwelling == null ? -1 : Guard("open a dwelling's Residents tab", () => (object)residentsShown(dwelling)) as int? ?? -1;
			Guard("close the building panel", () => { UIManager.Instance.structureInfoUI.CloseMenu(); return village; });
			Check("the village center's Residents tab lists the village; a dwelling's its household", () =>
				(villageShown == alive && alive > 0 && (dwelling == null || houseShown == dwelling.residents.Count),
				$"center shows {villageShown} of {alive} villagers; {dwelling?.name ?? "no dwelling"} shows {houseShown} of {dwelling?.residents.Count}"));
			string saved = null;
			yield return SaveAndRead("ruinarch.plus.tiers.json", (j, e) => saved = j);
			Check("the tier is stored inside the player's save file", () =>
				(saved != null && saved.Contains(village.persistentID + "|Town"), saved ?? "no tiers entry"));

			// 3. A City, which it keeps down to three quarters of the mark, then back to a Town.
			// One under the count: a villager dying in the meantime must not undo the step. The
			// marks are world-wide, so a village that emptied (people move away, the game's own
			// doing) would drag the City mark down to 1 and make every village a City: stop.
			if (Villagers(village) < 3)
			{
				foreach (string name in new[] { "a Town reaching the city mark becomes a City", "a City keeps its tier just under the mark",
					"a City far below the mark falls back to a Town", "destroying the Town Hall makes the Town a village again" })
				{
					Skip(name, $"{village.name} emptied during the test: {Describe(village)}");
				}
				RestoreTierConfig();
				yield break;
			}
			PlusBridge.SetConfig("cityPopulation", Villagers(village) - 1);
			yield return WaitGameHours(2f, () => PlusBridge.Tier(village) == "City");
			Check("a Town reaching the city mark becomes a City", () =>
				(PlusBridge.Tier(village) == "City" && village.settlementType.maxDwellings == baseDwellings + 16 && village.settlementType.maxFacilities == baseFacilities + 8,
				$"tier={PlusBridge.Tier(village)} caps={village.settlementType.maxDwellings}/{village.settlementType.maxFacilities}"));
			PlusBridge.SetConfig("cityPopulation", Villagers(village) + 1);
			yield return WaitGameHours(2f, null);
			Check("a City keeps its tier just under the mark", () => (PlusBridge.Tier(village) == "City", $"tier={PlusBridge.Tier(village)} villagers={Villagers(village)}"));
			PlusBridge.SetConfig("cityPopulation", 1000);
			yield return WaitGameHours(2f, () => PlusBridge.Tier(village) == "Town");
			Check("a City far below the mark falls back to a Town", () =>
				(PlusBridge.Tier(village) == "Town" && village.settlementType.maxDwellings == baseDwellings + 8 && ModsLogHas($"{village.name} has dwindled back to a Town"),
				$"tier={PlusBridge.Tier(village)} caps={village.settlementType.maxDwellings}/{village.settlementType.maxFacilities}"));

			// 4. The Town Hall destroyed: a village again, at once.
			RestoreTierConfig();
			Guard("destroy the Town Hall", () => { hall.AdjustHP(-hall.currentHP); return hall; });
			Check("destroying the Town Hall makes the Town a village again", () =>
				(hall.hasBeenDestroyed && PlusBridge.Tier(village) == "Village" && village.settlementType.maxDwellings == baseDwellings && village.settlementType.maxFacilities == baseFacilities
					&& ModsLogHas($"The Town Hall of {village.name} has been destroyed"),
				$"destroyed={hall.hasBeenDestroyed} tier={PlusBridge.Tier(village)} caps={village.settlementType.maxDwellings}/{village.settlementType.maxFacilities}"));
		}

		private static int Villagers(NPCSettlement v) => v.GetNumberOfResidentsThatIsAliveVillager();

		private static bool HasGraveyard(NPCSettlement v) => v.HasStructure(STRUCTURE_TYPE.CEMETERY) || v.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE);

		private static void RestoreTierConfig()
		{
			PlusBridge.SetConfig("townPopulation", 20);
			PlusBridge.SetConfig("cityPopulation", 40);
		}

		// For failure details: blueprints standing in the village, its build jobs, its taverns.
		private static string BlueprintState(NPCSettlement village)
		{
			List<string> blueprints = village.areas.SelectMany(a => a.gridTileComponent.gridTiles)
				.Select(t => t.tileObjectComponent.genericTileObject)
				.Where(g => g != null && g.blueprintOnTile != null)
				.Select(g => $"{g.blueprintOnTile.structureType}@{g.gridTileLocation.localPlace}").ToList();
			List<JobQueueItem> jobs = new List<JobQueueItem>();
			village.PopulateJobsOfType(jobs, JOB_TYPE.BUILD_BLUEPRINT);
			int taverns = village.structures.TryGetValue(STRUCTURE_TYPE.TAVERN, out List<LocationStructure> t2) ? t2.Count(s => !s.hasBeenDestroyed) : 0;
			return $"blueprints=[{string.Join(", ", blueprints)}] buildJobs={jobs.Count} (taken {jobs.Count(j => j.assignedCharacter != null)}) taverns={taverns}";
		}

		// Phase 5: famine. One village's villagers are kept starving (fullness held at 5, as
		// if there were nothing to eat) until a famine is declared. With famineLeaveChance at
		// 100, the first day of famine sends them to a village of their faction with a free
		// home. Fed again, the famine ends.
		private IEnumerator FamineSuite()
		{
			if (PlusBridge.InFamine(null) == null)
			{
				Skip("famine", "RuinarchPlus (with famine) not loaded");
				yield break;
			}
			Func<NPCSettlement, List<Character>> villagers = v => v.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()).ToList();
			Func<NPCSettlement, bool> hasRefuge = v => v.owner.ownedSettlements.OfType<NPCSettlement>()
				.Any(o => o != v && o.locationType == LOCATION_TYPE.VILLAGE && o.GetFirstUnoccupiedStructureOfType(STRUCTURE_TYPE.DWELLING) != null);
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorFaction && villagers(v).Count >= 4 && !v.isPlagued)
				.OrderByDescending(hasRefuge).FirstOrDefault();
			if (village == null)
			{
				Skip("famine", "no village of a major faction with 4+ villagers: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			List<Character> people = villagers(village);
			Log($"famine test village: {Describe(village)} villagers={people.Count} refuge={hasRefuge(village)}");
			PlusBridge.SetConfig("famineLeaveChance", 100);
			// Unrest on a short clock: restless after 2 hours of famine, the ruler challenged
			// after 4 (before the first day's emigration takes people away).
			PlusBridge.SetConfig("unrestHours", 2);
			PlusBridge.SetConfig("challengeHours", 4);
			Character oldRuler = village.ruler;
			// Hunters sent out are left to eat: kept starving, they would only ever look for
			// food and never hunt.
			Action starve = () =>
			{
				foreach (Character c in people.Where(c => !c.isDead && c.homeSettlement == village && !PlusBridge.IsHunting(c)))
				{
					c.needsComponent.SetFullness(5f);
				}
			};

			float start = GameHours;
			yield return WaitGameHours(20f, () => { starve(); return PlusBridge.InFamine(village) == true; });
			Check("a village whose people go hungry falls into famine", () =>
				(PlusBridge.InFamine(village) == true, $"famine={PlusBridge.InFamine(village)} after {GameHours - start:F1}h"));
			Check("the famine is announced", () => (ModsLogHas($"Famine in {village.name}"), "mods.log"));
			Check("no settlers move into a village in famine", () =>
			{
				float m = PlusBridge.MigrationMultiplier(village, out string why);
				// Any reason for zero will do (an attack on the village also stops settlers and is
				// named first); what matters is that nobody moves in.
				return (m == 0f && (why == "famine" || village.isUnderSiege), $"x{m} {why}");
			});

			// Unrest: the village turns on its ruler.
			if (oldRuler == null || oldRuler.isDead)
			{
				Skip("a long famine turns the village against its ruler", "the village has no ruler");
			}
			else
			{
				yield return WaitGameHours(6f, () => { starve(); return village.ruler != oldRuler; });
				Check("a long famine makes the village restless", () => (ModsLogHas($"{village.name} is restless"), "mods.log"));
				bool wasLeader = village.owner?.leader == oldRuler;
				Character newRuler = village.ruler;
				Check("a long famine turns the village against its ruler", () =>
					(newRuler != null && newRuler != oldRuler
						&& (ModsLogHas($"has taken the rule of {village.name} from {oldRuler.name}") || ModsLogHas($"has overthrown {oldRuler.name} as leader of")),
					$"ruler {oldRuler.name} (faction leader={wasLeader}) -> {newRuler?.name ?? "none"}; faction leader now {(village.owner?.leader as Character)?.name ?? "-"}; opinion of the old ruler: "
					+ string.Join(", ", people.Where(c => !c.isDead && c != oldRuler).Take(5).Select(c => $"{c.name} {c.relationshipContainer.GetTotalOpinion(oldRuler)}"))));
				// The game puts a faction leader back in charge of their home village: the
				// challenge must stick.
				yield return WaitGameHours(6f, () => { starve(); return village.ruler != newRuler; });
				Check("the challenger keeps the rule", () =>
					(newRuler != null && village.ruler == newRuler, $"ruler now {village.ruler?.name ?? "none"} (was {newRuler?.name}); faction leader {(village.owner?.leader as Character)?.name ?? "-"}"));
			}

			if (!hasRefuge(village))
			{
				Skip("starving villagers leave for a village with food", "no other village of the faction has a free home");
			}
			else
			{
				// Only the famine's own moves count (announced "... has left <village> for ..."):
				// villagers also change homes for the game's own reasons.
				Func<Character, bool> moved = c => !c.isDead && c.homeSettlement != null && c.homeSettlement != village;
				yield return WaitGameHours(26f, () => { starve(); return people.Any(moved); });
				List<Character> left = people.Where(c => moved(c) && ModsLogHas($"{c.name} has left {village.name} for")).ToList();
				if (left.Count == 0 && (!hasRefuge(village) || PlusBridge.InFamine(village) == false))
				{
					Skip("starving villagers leave for a village with food", !hasRefuge(village)
						? "the free homes in the faction's other villages were taken before the day's move"
						: "the famine ended before its first day (starving villagers lost their homes for the game's own reasons, so fewer counted as starving)");
				}
				else
				{
					Check("starving villagers leave for a village with food", () =>
						(left.Count > 0 && left.All(c => c.homeSettlement.owner == village.owner),
						(left.Count == 0 ? "nobody left: " + string.Join(", ", people.Where(c => !c.isDead).Select(c =>
							$"{c.name}[starving={c.needsComponent.isStarving || c.traitContainer.HasTrait("Malnourished")} ruler={c == village.ruler} leader={c.isFactionLeader} canMove={c.limiterComponent.canMove} hunting={PlusBridge.IsHunting(c)} home={c.homeSettlement?.name ?? "-"}]"))
							+ $"; refuge now={hasRefuge(village)} famine={PlusBridge.InFamine(village)}"
						: string.Join(", ", left.Select(c => $"{c.name} -> {c.homeSettlement.name}")))));
				}
			}

			foreach (Character c in people.Where(c => !c.isDead))
			{
				c.needsComponent.SetFullness(100f);
			}
			yield return WaitGameHours(16f, () => PlusBridge.InFamine(village) == false);
			Check("fed again, the famine ends", () =>
				(PlusBridge.InFamine(village) == false && ModsLogHas($"The famine in {village.name} is over"), $"famine={PlusBridge.InFamine(village)}"));
			PlusBridge.SetConfig("famineLeaveChance", 25);
			PlusBridge.SetConfig("unrestHours", 24);
			PlusBridge.SetConfig("challengeHours", 72);
		}

		// Phase 5: hunting. A pig is put next to a village and the village sends its hunters
		// (as it does every 6 hours when hungry): they kill and butcher it and carry the meat
		// to the village storage.
		private IEnumerator HuntSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("hunting", "RuinarchPlus not loaded");
				yield break;
			}
			Func<Character, bool> fighter = c => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient() && c.characterClass.IsCombatant()
				&& (!c.partyComponent.hasParty || !c.partyComponent.currentParty.isActive);
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorFaction && v.mainStorage != null)
				.OrderByDescending(v => v.residents.Count(fighter)).FirstOrDefault();
			LocationGridTile wild = village?.areas.SelectMany(a => a.neighbourComponent.neighbours).Distinct()
				.Where(a => !a.HasSettlementOnArea() && a.gridTileComponent.centerGridTile != null)
				.Select(a => a.gridTileComponent.centerGridTile).FirstOrDefault(t => !t.isOccupied && t.IsPassable());
			if (wild == null)
			{
				Skip("a village sends hunters after wild animals", village == null ? "no village" : "no free wild tile next to the village");
				yield break;
			}
			Summon pig = Guard("put a pig near the village", () =>
			{
				Summon s = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Pig, null, homeLocation: null,
					homeRegion: GridMap.Instance.mainRegion, homeStructure: null, className: "", bypassIdeologyChecking: true);
				s.CreateMarker();
				s.InitialCharacterPlacement(wild);
				s.marker.UpdatePosition();
				return s;
			});
			int sent = pig == null ? -1 : PlusBridge.SendHunters(village);
			Check("a village sends hunters after wild animals", () =>
				(sent > 0, $"{village.name} sent={sent}; fighters: {string.Join(", ", village.residents.Where(fighter).Select(c => $"{c.name} ({c.characterClass.className} marker={c.hasMarker} move={c.limiterComponent.canMove} perform={c.limiterComponent.canPerform} party={c.partyComponent.currentParty?.isActive} carried={c.carryComponent.isBeingCarriedBy != null} hunting={c.jobQueue.HasJob(JOB_TYPE.HUNT_PREY)})"))}"));
			if (sent > 0)
			{
				yield return WaitGameHours(16f, () => ModsLogHas($"meat home to {village.name}"));
				Check("the hunters kill, butcher and carry the meat home", () =>
					(ModsLogHas($"meat home to {village.name}"), $"pig dead={pig.isDead} marker={pig.hasMarker} at {pig.gridTileLocation?.localPlace}; still hunting: {string.Join(", ", village.residents.Where(PlusBridge.IsHunting).Select(c => $"{c.name} job={c.currentJob?.jobType}"))}"));
			}
		}

		// Phase 5: traders. A village is given food to spare and sends a trader to another
		// (not hostile) village; the food arrives. If the two are of different factions, the
		// trader also tells the other village what their faction knows of the demons.
		private IEnumerator TradeSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("traders", "RuinarchPlus not loaded");
				yield break;
			}
			List<NPCSettlement> villages = Villages().Where(v => v.owner != null && v.owner.isMajorFaction && v.mainStorage != null && !PlusBridge.IsUnderCurfew(v)).ToList();
			NPCSettlement from = null, to = null;
			foreach (NPCSettlement a in villages)
			{
				// Prefer a partner of another faction (the news check needs one).
				to = villages.Where(b => b != a && (b.owner == a.owner || !a.owner.IsHostileWith(b.owner))).OrderBy(b => b.owner == a.owner).FirstOrDefault();
				if (to != null)
				{
					from = a;
					break;
				}
			}
			LocationGridTile shelf = from?.mainStorage.GetRandomUnoccupiedTile();
			if (from == null || shelf == null)
			{
				Skip("a trader brings food to another village", from == null ? "no two villages that may trade" : $"no room in {from.name}'s storage");
				yield break;
			}
			Guard("stock the village's storage", () =>
			{
				FoodPile food = InnerMapManager.Instance.CreateNewTileObject<FoodPile>(TILE_OBJECT_TYPE.ANIMAL_MEAT);
				food.SetResourceInPile(100);
				from.mainStorage.AddPOI(food, shelf);
				return food;
			});
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			bool news = from.owner != to.owner && portal != null;
			bool hostsAware = to.owner.isAwareOfPlayer;
			if (news)
			{
				PlusBridge.Forget(to.owner);
				PlusBridge.Forget(from.owner);
				PlusBridge.Learn(from.owner, portal);
			}
			Log($"trade test: {Describe(from)} -> {Describe(to)}");
			Character trader = PlusBridge.SendTrader(from, to, 40);
			Check("a village with food to spare sends a trader", () => (trader != null, trader == null ? "nobody went" : $"{trader.name} ({trader.characterClass.className})"));
			if (trader != null)
			{
				yield return WaitGameHours(30f, () => ModsLogHas($"brought 40 food to {to.name}") || trader.isDead);
				if (trader.isDead && !ModsLogHas($"{trader.name} of {from.name} brought 40 food to {to.name}"))
				{
					string cause = trader.deathLog?.logText ?? "no death log";
					Skip("the trader brings the food to the other village", $"{trader.name} died on the way: {cause}");
					if (news) Skip("the trader tells the other faction what theirs knows", $"{trader.name} died on the way");
					news = false;
				}
				else Check("the trader brings the food to the other village", () =>
					(ModsLogHas($"{trader.name} of {from.name} brought 40 food to {to.name}"),
					$"{trader.name} dead={trader.isDead} at {trader.gridTileLocation?.area?.GetFirstNPCSettlementOnArea()?.name ?? "the wild"} carrying={trader.carryComponent.carriedPOI?.name ?? "nothing"} haul={trader.jobQueue.HasJob(JOB_TYPE.HAUL)}"));
				if (news)
				{
					Check("the trader tells the other faction what theirs knows", () =>
						(PlusBridge.Knows(to.owner, portal), $"{to.owner.name} know of the portal={PlusBridge.Knows(to.owner, portal)}"));
				}
			}
			if (news)
			{
				foreach (Faction f in new[] { from.owner, to.owner })
				{
					PlusBridge.Forget(f);
					CallOffCounterattacks(f);
				}
				Guard("restore awareness", () => { to.owner.SetIsAwareOfPlayer(hostsAware); return to.owner; });
			}
		}

		// ---------------------------------------------------------------------------------
		private static List<NPCSettlement> Villages()
		{
			return GridMap.Instance.mainRegion.settlementsInRegion
				.OfType<NPCSettlement>()
				.Where(s => s.owner != null && s.cityCenter != null && s.residents != null
					&& s.residents.Count(r => r != null && !r.isDead) >= 3)
				.OrderByDescending(s => s.residents.Count(r => r != null && !r.isDead))
				.ToList();
		}

		// Where a body's grave is, for failure details.
		private static string GraveWhere(Character body, NPCSettlement village)
		{
			LocationStructure at = body.grave?.gridTileLocation?.structure;
			return $"graveAt={at?.structureType.ToStringEnum() ?? "-"}/{at?.settlementLocation?.name ?? "-"} villageCemetery={village.HasStructure(STRUCTURE_TYPE.CEMETERY)}";
		}

		private static string Describe(NPCSettlement s)
		{
			List<Character> alive = s.residents.Where(r => r != null && !r.isDead).ToList();
			return $"{s.name}[alive={alive.Count} criminals={alive.Count(r => r.traitContainer.HasTrait("Criminal"))} outOfFaction={alive.Count(r => !r.isPartOfHomeFaction)} owner={s.owner?.name ?? "none"} cemetery={s.HasStructure(STRUCTURE_TYPE.CEMETERY)} cult={s.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE)} lodge={s.HasStructure(STRUCTURE_TYPE.HUNTER_LODGE)}]";
		}

		// Kills a living resident who is currently inside (or next to) the village.
		private Character KillResident(NPCSettlement village)
		{
			// Prefer someone standing on village tiles (settlement burial path); fall back to
			// the village border (where the personal outside-village burial path applies).
			// Party members last: the game has a party bury its own fallen where they lie.
			// Villagers only: some villages house monsters (a Golem, a Scorpion).
			Character victim = village.residents.Where(r => r != null && !r.isDead && r.gridTileLocation != null && r.isNormalCharacter && r.race.IsSapient()
					&& r.gridTileLocation.IsPartOfSettlement(village) && r != village.ruler)
				.OrderBy(r => r.partyComponent.hasParty).FirstOrDefault()
				?? village.residents.Where(r => r != null && !r.isDead && r.gridTileLocation != null && r.isNormalCharacter && r.race.IsSapient()
					&& r.gridTileLocation.IsNextToOrPartOfSettlement(village) && r != village.ruler)
				.OrderBy(r => r.partyComponent.hasParty).FirstOrDefault();
			if (victim == null)
			{
				return null;
			}
			LocationGridTile at = victim.gridTileLocation;
			victim.Death("autotest");
			Log($"  killed {victim.name} at {at} (inside village={at.IsPartOfSettlement(village)})");
			return victim;
		}

		// Kills a living resident standing next to, but not on, the village's tiles.
		private Character KillResidentOnBorder(NPCSettlement village)
		{
			Character victim = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.gridTileLocation != null
				&& !r.gridTileLocation.IsPartOfSettlement() && r.gridTileLocation.IsNextToOrPartOfSettlement(village) && r != village.ruler);
			if (victim == null)
			{
				// Nobody is out there right now: move someone onto a free tile one step outside
				// the village's structures (the border ring lies in neighbouring areas, since a
				// village claims its whole area), then kill them there.
				LocationGridTile edge = GridMap.Instance.mainRegion.areas.SelectMany(a => a.gridTileComponent.gridTiles)
					.FirstOrDefault(t => t != null && !t.isOccupied && !t.IsPartOfSettlement() && t.IsNextToOrPartOfSettlement(village)
						&& t.structure is Wilderness);
				victim = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.gridTileLocation != null && r != village.ruler && r.marker != null);
				if (edge == null || victim == null)
				{
					return null;
				}
				victim.marker.PlaceMarkerAt(edge);
			}
			LocationGridTile at = victim.gridTileLocation;
			victim.Death("autotest");
			Log($"  killed {victim.name} on the border at {at} (inside village={at.IsPartOfSettlement(village)})");
			return victim;
		}

		private Character SpawnAndKill(NPCSettlement village, SUMMON_TYPE type)
		{
			LocationGridTile tile = village.cityCenter.tiles.FirstOrDefault(t => t != null && !t.isOccupied)
				?? village.cityCenter.tiles.FirstOrDefault();
			return tile == null ? null : SpawnAndKillAt(tile, type);
		}

		private Character SpawnAndKillAt(LocationGridTile tile, SUMMON_TYPE type)
		{
			Summon s = CharacterManager.Instance.CreateNewSummon(type, null, homeLocation: null,
				homeRegion: GridMap.Instance.mainRegion, homeStructure: null, className: "", bypassIdeologyChecking: true);
			s.CreateMarker();
			s.InitialCharacterPlacement(tile);
			s.marker.UpdatePosition();
			s.Death("autotest");
			Log($"  spawned and killed {type} {s.name} at {tile} (skinnable={s.race.IsSkinnable()})");
			return s;
		}


		// The selected character's path line, zoomed all the way out: at least 2 pixels wide
		// on screen (Ruinarch+ fix; the game draws it a fixed world width that vanishes when
		// zoomed out). The real screen is saved as pathline.png to look at.
		private IEnumerator PathLineTest()
		{
			InnerMapCameraMove mover = InnerMapCameraMove.Instance;
			Camera cam = mover != null ? mover.camera : null;
			Character walker = null;
			yield return WaitGameHours(3f, () => (walker = Villages().SelectMany(v => v.residents).FirstOrDefault(r => r != null && !r.isDead && r.hasMarker
				&& r.marker.isMoving && r.marker.pathfindingAI.hasPath && r.marker.pathfindingAI.currentPath != null
				&& r.marker.pathfindingAI.currentPath.vectorPath.Count > 8)) != null);
			if (walker == null || cam == null)
			{
				Skip("the selected character's path stays visible zoomed out", walker == null ? "nobody walking" : "no camera");
				yield break;
			}
			float before = cam.orthographicSize;
			float max = AccessTools.FieldRefAccess<BaseCameraMove, float>("_maxFov")(mover);
			LineRenderer line = AccessTools.FieldRefAccess<InnerTileMap, LineRenderer>("pathLineRenderer")(walker.currentRegion.innerMap);
			Guard("select the walker", () => { UIManager.Instance.ShowCharacterInfo(walker, centerOnCharacter: false); return walker; });
			bool shown = false;
			float px = -1f;
			// A few frames at full zoom-out while they are still on the move.
			for (int i = 0; i < 20 && !shown; i++)
			{
				cam.orthographicSize = max;
				// The walker in the lower third, clear of any popup in the middle of the screen.
				Vector3 at = walker.marker.transform.position;
				cam.transform.position = new Vector3(at.x, at.y + max * 0.55f, cam.transform.position.z);
				yield return null;
				shown = line != null && line.gameObject.activeSelf && walker.marker != null && walker.marker.isMoving;
				px = PlusBridge.PathLineWidth(walker.currentRegion.innerMap);
			}
			yield return new WaitForEndOfFrame();
			Texture2D shot = null;
			try
			{
				shot = ScreenCapture.CaptureScreenshotAsTexture();
				File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(_logPath), "pathline.png"), shot.EncodeToPNG());
				Log($"  screenshot saved: pathline.png (zoom {max}, {walker.name})");
			}
			catch (Exception e)
			{
				Log($"  screenshot pathline failed: {e.Message}");
			}
			finally
			{
				if (shot != null) Destroy(shot);
			}
			Check("the selected character's path stays visible zoomed out", () =>
				(shown && px >= 1.9f, $"{walker.name} zoom {cam.orthographicSize:F1} (max {max:F1}) line shown={shown} width={px:F1}px"));
			Guard("close the character panel", () => { AccessTools.FieldRefAccess<UIManager, CharacterInfoUI>("characterInfoUI")(UIManager.Instance).CloseMenu(); return walker; });
			cam.orthographicSize = before;
		}

		// ---------------------------------------------------------------------------------
		// Phase 4: the life cycle. Everyone has an age from the start (adults, elders), the old
		// die of age, lovers have a child who is drawn smaller and takes no jobs, the child
		// comes of age, and ages ride inside the save.
		private IEnumerator LifeSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("life cycle", "RuinarchPlus not loaded");
				yield break;
			}
			// Off for the other suites (see Run); on for these checks, then off again.
			PlusBridge.SetConfig("lifeCycleEnabled", true);
			yield return WaitGameHours(1f, null);
			yield return LifeChecks();
			PlusBridge.SetConfig("lifeCycleEnabled", false);
			PlusBridge.SetConfig("pregnancyDays", 4);
		}

		// The couple and child LifeSuite made: later suites leave them be.
		private readonly HashSet<Character> _lifeFamily = new HashSet<Character>();

		private IEnumerator LifeChecks()
		{
			List<Character> people = Villages().SelectMany(v => v.residents)
				.Where(c => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient()).ToList();
			Check("every villager has an age from the start, none of them a child", () =>
			{
				List<Character> unknown = people.Where(c => PlusBridge.LifeStage(c) == null).ToList();
				List<Character> children = people.Where(c => PlusBridge.LifeStage(c) == "Child").ToList();
				int elders = people.Count(c => PlusBridge.LifeStage(c) == "Elder");
				return (people.Count > 0 && unknown.Count == 0 && children.Count == 0,
					$"{people.Count} villagers, {elders} elder(s); ages {string.Join(", ", people.Take(8).Select(c => $"{c.name} {PlusBridge.AgeYears(c):F1}"))}"
					+ (unknown.Count > 0 ? "; no age: " + string.Join(", ", unknown.Select(c => c.name)) : "")
					+ (children.Count > 0 ? "; children: " + string.Join(", ", children.Select(c => c.name)) : ""));
			});

			// Old age.
			Character old = people.FirstOrDefault(c => c != c.homeSettlement?.ruler && !c.isFactionLeader && c.carryComponent.isBeingCarriedBy == null && !c.isDead);
			if (old != null)
			{
				float age = PlusBridge.AgeYears(old);
				PlusBridge.SetDeathAgeYears(old, age - 0.01f);
				yield return WaitGameHours(2f, () => old.isDead);
				Check("an elder past their age of death dies of old age", () =>
					(old.isDead && ModsLogHas($"{old.name} died of old age at {Mathf.FloorToInt(age)}."), $"{old.name} age {age:F2} dead={old.isDead}"));
			}

			// A child: lovers of one village (made lovers if none are).
			Character mother = null;
			Character father = null;
			foreach (NPCSettlement v in Villages().OrderByDescending(v => v.residents.Count))
			{
				List<Character> adults = v.residents.Where(c => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient()
					&& PlusBridge.LifeStage(c) == "Adult" && c.carryComponent.isBeingCarriedBy == null && c.hasMarker).ToList();
				mother = adults.FirstOrDefault(c => c.gender == GENDER.FEMALE && PlusBridge.PartnerOf(c) != null);
				if (mother != null)
				{
					father = PlusBridge.PartnerOf(mother);
					break;
				}
				Character f = adults.FirstOrDefault(c => c.gender == GENDER.FEMALE && c.relationshipContainer.GetFirstCharacterWithRelationship(RELATIONSHIP_TYPE.LOVER) == null);
				Character m = f == null ? null : adults.FirstOrDefault(c => c.gender == GENDER.MALE && c.race == f.race && c.relationshipContainer.GetFirstCharacterWithRelationship(RELATIONSHIP_TYPE.LOVER) == null);
				if (f != null && m != null)
				{
					Guard("make two villagers lovers", () => RelationshipManager.Instance.CreateNewRelationshipBetween(f, m, RELATIONSHIP_TYPE.LOVER));
					if (PlusBridge.PartnerOf(f) == m)
					{
						mother = f;
						father = m;
						break;
					}
				}
			}
			if (mother == null)
			{
				Skip("lovers have a child", "no village with a woman and a man of one race who can be lovers");
				yield break;
			}
			NPCSettlement home = (NPCSettlement)mother.homeSettlement;
			LocationStructure house = mother.homeStructure;
			_lifeFamily.Add(mother);
			_lifeFamily.Add(father);
			PlusBridge.SetConfig("pregnancyDays", 1);
			PlusBridge.Conceive(mother, father);
			Check("a woman and her lover can conceive", () => (PlusBridge.IsPregnant(mother), $"{mother.name} and {father.name} of {home.name}"));
			yield return WaitGameHours(26f, () => !PlusBridge.IsPregnant(mother));
			Character child = home.residents.FirstOrDefault(c => c != null && PlusBridge.IsChild(c));
			Check("the child is born in the mother's home", () =>
				(child != null && child.homeSettlement == home && child.homeStructure == house && ModsLogHas($"of {home.name} had a child: {child.name}."),
				child == null ? $"no child; pregnant={PlusBridge.IsPregnant(mother)} mother dead={mother.isDead}" : $"{child.name} home {child.homeSettlement?.name}/{child.homeStructure?.name} (mother's {house?.name})"));
			if (child == null)
			{
				PlusBridge.SetConfig("pregnancyDays", 4);
				yield break;
			}
			_lifeFamily.Add(child);
			SpriteRenderer body = AccessTools.Field(typeof(CharacterMarker), "mainImg").GetValue(child.marker) as SpriteRenderer;
			Check("a child is their parents' child, drawn smaller, takes no jobs and does not fight", () =>
				(child.relationshipContainer.GetFirstCharacterWithRelationship(RELATIONSHIP_TYPE.PARENT) != null
					&& body != null && Mathf.Abs(body.transform.localScale.x - 0.6f) < 0.01f
					&& !child.limiterComponent.canTakeJobs && !child.characterClass.IsCombatant() && PlusBridge.AgeYears(child) < 0.2f,
				$"parent={child.relationshipContainer.GetFirstCharacterWithRelationship(RELATIONSHIP_TYPE.PARENT)?.name ?? "none"} scale={body?.transform.localScale.x:F2} canTakeJobs={child.limiterComponent.canTakeJobs} class={child.characterClass.className} age={PlusBridge.AgeYears(child):F2}"));
			CharacterInfoUI panel = AccessTools.FieldRefAccess<UIManager, CharacterInfoUI>("characterInfoUI")(UIManager.Instance);
			string label = Guard("open the child's panel", () =>
			{
				UIManager.Instance.ShowCharacterInfo(child, centerOnCharacter: true);
				return (AccessTools.Field(typeof(CharacterInfoUI), "subLbl").GetValue(panel) as TMPro.TMP_Text)?.text;
			});
			// For the screenshot: child and mother side by side on the village square, close up.
			List<LocationGridTile> square = home.cityCenter.passableTiles.Where(t => !t.isOccupied).ToList();
			LocationGridTile a = square.FirstOrDefault();
			LocationGridTile b = a?.neighbourList.FirstOrDefault(t => square.Contains(t));
			Camera cam = InnerMapCameraMove.Instance.camera;
			float zoom = cam.orthographicSize;
			if (a != null && b != null && !mother.isDead)
			{
				Guard("stand the child by the mother", () =>
				{
					CharacterManager.Instance.Teleport(child, a);
					CharacterManager.Instance.Teleport(mother, b);
					cam.orthographicSize = 2.5f;
					cam.transform.position = new Vector3(child.marker.transform.position.x, child.marker.transform.position.y, cam.transform.position.z);
					return child;
				});
			}
			yield return new WaitForSecondsRealtime(0.5f);
			yield return Screenshot("child.png");
			cam.orthographicSize = zoom;
			Guard("close the child's panel", () => { panel.CloseMenu(); return child; });
			Check("the character panel gives the age", () => (label == "Child, age 0", $"\"{label}\""));

			// Children don't rule: let the game pick a village ruler 30 times.
			Character ruler = home.ruler;
			List<string> picks = new List<string>();
			Guard("let the game pick a ruler 30 times", () =>
			{
				for (int i = 0; i < 30; i++)
				{
					home.DesignateNewRuler(willLog: false);
					picks.Add(home.ruler?.name ?? "none");
				}
				if (ruler != null && !ruler.isDead) home.SetRuler(ruler);
				return picks;
			});
			int candidates = home.residents.Count(c => c != null && !c.isDead && c.faction == home.owner && c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(home));
			Check("the game never picks a child to rule the village", () =>
				(picks.Count == 30 && !picks.Contains(child.name), $"{candidates} candidates in the village; picked {string.Join(", ", picks.Distinct())}"));

			// Coming of age.
			PlusBridge.SetAgeYears(child, 3.1f);
			yield return WaitGameHours(2f, () => !PlusBridge.IsChild(child));
			Check("a child comes of age: full size, takes jobs, gets a class", () =>
				(!PlusBridge.IsChild(child) && body != null && Mathf.Abs(body.transform.localScale.x - 1f) < 0.01f && child.limiterComponent.canTakeJobs
					&& ModsLogHas($"{child.name} has come of age"),
				$"child={PlusBridge.IsChild(child)} stage={PlusBridge.LifeStage(child)} scale={body?.transform.localScale.x:F2} canTakeJobs={child.limiterComponent.canTakeJobs} class={child.characterClass.className}"));
			PlusBridge.SetConfig("pregnancyDays", 4);

			// Ages ride inside the save.
			Character sample = people.FirstOrDefault(c => !c.isDead);
			float before = PlusBridge.AgeYears(sample);
			string json = null;
			yield return SaveAndRead("ruinarch.plus.life.json", (j, e) => json = j);
			Check("ages are stored inside the player's save file", () => (json != null && json.Contains(sample.persistentID), json == null ? "no life entry" : $"{json.Length} bytes"));
			if (json != null)
			{
				ReplayLoad("ruinarch.plus.life.json", json);
				Check("ages come back when the save loads", () =>
					(Mathf.Abs(PlusBridge.AgeYears(sample) - before) < 0.05f, $"{sample.name}: {before:F2} -> {PlusBridge.AgeYears(sample):F2}"));
			}
		}

		// ---------------------------------------------------------------------------------
		// A wall hit that leaves the wall standing changes no path, so the building must not
		// rescan its pathfinding grid (Ruinarch+ fix: burning walls did it every tick); a wall
		// that breaks opens a way through, so that one must.
		private IEnumerator FireWallTest()
		{
			ThinWall wall = Villages().SelectMany(v => v.structures.Values.SelectMany(l => l))
				.OfType<ManMadeStructure>().Where(s => s.structureWalls != null)
				.SelectMany(s => s.structureWalls).FirstOrDefault(w => w != null && w.currentHP > 2 && w.gridTileLocation != null);
			if (wall == null)
			{
				Skip("a wall hit that leaves it standing does not rescan the building", "no village building with walls");
				yield break;
			}
			int Requests() => (int)GraphRequests.Where(kv => kv.Key.Contains("RescanPathfindingGridOfStructure")).Sum(kv => kv.Value[0]);
			GraphRequests.Clear();
			FireProbeTiming.On = true;
			Guard("hit the wall", () => { wall.AdjustHP(-1, ELEMENTAL_TYPE.Normal, isTrueDamage: true); return wall; });
			yield return null;
			int afterHit = Requests();
			Guard("break the wall", () => { wall.AdjustHP(-wall.currentHP, ELEMENTAL_TYPE.Normal, isTrueDamage: true); return wall; });
			yield return null;
			int afterBreak = Requests();
			FireProbeTiming.On = false;
			Guard("rebuild the wall", () => { wall.AdjustHP(wall.maxHP, ELEMENTAL_TYPE.Normal, isTrueDamage: true); return wall; });
			Check("a wall hit that leaves it standing does not rescan the building", () => (afterHit == 0, $"rescans after the hit: {afterHit}"));
			Check("a wall that breaks rescans the building", () => (afterBreak > afterHit, $"rescans after the break: {afterBreak - afterHit}; wall hp now {wall.currentHP}/{wall.maxHP}"));
		}

		// ---------------------------------------------------------------------------------
		// Fire and frame rate (run by name only: it burns a village down). A measurement, not a
		// test: frame times as more of a village burns, what the fire's own tick code costs,
		// and the frame time with the fire's effects hidden, to tell rendering from logic.
		private IEnumerator FireProbe()
		{
			NPCSettlement village = Villages().OrderByDescending(v => v.areas.Count).FirstOrDefault();
			if (village?.cityCenter == null)
			{
				Skip("fire probe", "no village");
				yield break;
			}
			GameObject prefab = Guard("find the burning effect", () =>
				(AccessTools.Field(typeof(GameManager), "particleEffectsDictionary").GetValue(GameManager.Instance) as ParticleEffectAssetDictionary)?[PARTICLE_EFFECT.Burning]);
			if (prefab != null)
			{
				ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
				Log($"burning effect '{prefab.name}': " + string.Join(", ", prefab.GetComponentsInChildren<Component>(true).GroupBy(c => c.GetType().Name).Select(g => $"{g.Key} x{g.Count()}"))
					+ "; max particles " + string.Join("/", systems.Select(p => p.main.maxParticles)) + ", emission " + string.Join("/", systems.Select(p => p.emission.rateOverTime.constant.ToString("F0"))) + "/s");
			}
			Guard("look at the village at normal speed", () =>
			{
				UIManager.Instance.ShowStructureInfo(village.cityCenter);
				UIManager.Instance.structureInfoUI.CloseMenu();
				Time.timeScale = 1f;
				UIManager.Instance.SetProgressionSpeed1X();
				return village;
			});
			yield return new WaitForSecondsRealtime(3f);

			// Everything flammable in the village, nearest the center first (on screen).
			LocationGridTile centre = village.cityCenter.tiles.FirstOrDefault();
			List<Traits.ITraitable> fuel = new List<Traits.ITraitable>();
			List<Traits.ITraitable> onTile = new List<Traits.ITraitable>();
			foreach (LocationGridTile t in village.areas.SelectMany(a => a.gridTileComponent.gridTiles).OrderBy(t => centre == null ? 0f : t.GetDistanceTo(centre)))
			{
				onTile.Clear();
				t.PopulateTraitablesOnTileThatCanHaveElementalTrait(onTile, "Burning", true, 0f, ELEMENTAL_TYPE.Fire);
				fuel.AddRange(onTile.Where(x => !(x is Character) && x.traitContainer.HasTrait("Flammable")));
			}
			Log($"fire probe in {village.name}: {fuel.Count} flammable things in {village.areas.Count} areas");
			Func<List<BaseParticleEffect>> effects = () => prefab == null ? new List<BaseParticleEffect>()
				: FindObjectsOfType<BaseParticleEffect>().Where(e => e.name.StartsWith(prefab.name)).ToList();

			Guard("time the game's per-frame methods", () => { InstallHotTimers(); return village; });
			yield return MeasureFrames("no fire, 1x", 5f, effects);
			BurningSource source = new BurningSource();
			int lit = 0;
			foreach (int stage in new[] { 100, 400, fuel.Count })
			{
				for (; lit < Math.Min(stage, fuel.Count); lit++)
				{
					Traits.ITraitable x = fuel[lit];
					if (x.gridTileLocation == null || x.traitContainer.HasTrait("Burning"))
					{
						continue;
					}
					x.traitContainer.AddTrait(x, "Burning", out Traits.Trait trait, null, bypassElementalChance: true, -1, 0f, ELEMENTAL_TYPE.Fire);
					(trait as Traits.Burning)?.SetSourceOfBurning(source, x);
				}
				yield return new WaitForSecondsRealtime(1f);
				yield return MeasureFrames($"{lit} set alight, 1x", 5f, effects);
			}
			Guard("4x speed", () => { UIManager.Instance.SetProgressionSpeed4X(); return village; });
			yield return MeasureFrames("all set alight, 4x", 5f, effects);
			List<BaseParticleEffect> hidden = effects();
			foreach (BaseParticleEffect e in hidden)
			{
				e.gameObject.SetActive(false);
			}
			yield return MeasureFrames($"all set alight, 4x, {hidden.Count} effects hidden", 5f, effects);
			foreach (BaseParticleEffect e in hidden)
			{
				if (e != null) e.gameObject.SetActive(true);
			}
			FireProbeTiming.QuietBurns = true;
			yield return MeasureFrames("all set alight, 4x, no damage numbers or hit sparks from burning", 5f, effects);
			FireProbeTiming.QuietBurns = false;
			yield return Screenshot("fire.png");
			Guard("back to test speed", () => { Time.timeScale = TimeScale; return village; });
		}

		private IEnumerator MeasureFrames(string label, float seconds, Func<List<BaseParticleEffect>> effects)
		{
			FireProbeTiming.Reset();
			HotMs.Clear();
			GraphRequests.Clear();
			FireProbeTiming.On = true;
			float end = Time.realtimeSinceStartup + seconds;
			int frames = 0;
			float sum = 0f;
			float worst = 0f;
			while (Time.realtimeSinceStartup < end)
			{
				yield return null;
				frames++;
				sum += Time.unscaledDeltaTime;
				worst = Math.Max(worst, Time.unscaledDeltaTime);
			}
			FireProbeTiming.On = false;
			int hpBars = FindObjectsOfType<BaseMapObjectVisual>().Count(v => v.hasHPBarGO && v.hpBarGO.activeSelf);
			string hot = string.Join(", ", HotMs.OrderByDescending(kv => kv.Value).Take(10)
				.Select(kv => $"{kv.Key.DeclaringType?.Name}.{kv.Key.Name} {kv.Value / Math.Max(1, frames):F2}"));
			List<BaseParticleEffect> active = effects().Where(e => e.gameObject.activeInHierarchy).ToList();
			int particles = active.SelectMany(e => e.GetComponentsInChildren<ParticleSystem>()).Sum(p => p.particleCount);
			int ticks = Math.Max(1, FireProbeTiming.Ticks);
			Log($"  fire probe [{label}]: {frames / sum:F0} fps (avg {sum / frames * 1000f:F1} ms, worst {worst * 1000f:F0} ms); "
				+ $"{FireProbeTiming.Ticks} ticks, tick start {FireProbeTiming.TickStartMs / ticks:F1} ms + tick end {FireProbeTiming.TickEndMs / ticks:F1} ms per tick, "
				+ $"of which burning {FireProbeTiming.BurningMs / ticks:F1} ms ({FireProbeTiming.BurningCalls / ticks} fires); {active.Count} fire effects, {particles} live particles, {hpBars} HP bars shown");
			Log($"    ms per frame: {hot}");
			if (GraphRequests.Count > 0)
			{
				Log("    graph rebuilds requested: " + string.Join("; ", GraphRequests.OrderByDescending(kv => kv.Value[0]).Take(6)
					.Select(kv => $"{kv.Value[0]}x {kv.Key} (avg {kv.Value[1] / kv.Value[0]:F0} tiles)")));
			}
		}

		// Who asks the pathfinder to rebuild part of its graph, how often, and how big a box.
		private static readonly Dictionary<string, double[]> GraphRequests = new Dictionary<string, double[]>();

		[HarmonyPatch(typeof(Inner_Maps.InnerMapManager), nameof(Inner_Maps.InnerMapManager.ShowHealthAdjustmentEffect))]
		internal static class FireProbe_QuietNumbers
		{
			private static bool Prefix() => !(FireProbeTiming.QuietBurns && FireProbeTiming.InBurn);
		}

		[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.CreateHitEffectAt))]
		internal static class FireProbe_QuietSparks
		{
			private static bool Prefix() => !(FireProbeTiming.QuietBurns && FireProbeTiming.InBurn);
		}

		private static void TallyGraphRequest(Bounds b)
		{
			if (!FireProbeTiming.On)
			{
				return;
			}
			string caller = string.Join(" < ", new System.Diagnostics.StackTrace(3, false).GetFrames()?.Take(3)
				.Select(f => f.GetMethod()).Where(m => m != null).Select(m => $"{m.DeclaringType?.Name}.{m.Name}") ?? new string[0]);
			if (!GraphRequests.TryGetValue(caller, out double[] tally))
			{
				GraphRequests[caller] = tally = new double[2];
			}
			tally[0]++;
			tally[1] += b.size.x * b.size.y;
		}

		[HarmonyPatch(typeof(PathfindingManager), nameof(PathfindingManager.UpdatePathfindingGraphPartialCoroutine), new[] { typeof(Bounds) })]
		internal static class FireProbe_GraphRequestBounds
		{
			private static void Prefix(Bounds bounds) => TallyGraphRequest(bounds);
		}

		[HarmonyPatch(typeof(PathfindingManager), nameof(PathfindingManager.UpdatePathfindingGraphPartialCoroutine), new[] { typeof(Pathfinding.GraphUpdateObject) })]
		internal static class FireProbe_GraphRequestObject
		{
			private static void Prefix(Pathfinding.GraphUpdateObject guo) => TallyGraphRequest(guo.bounds);
		}

		// Per-method CPU time of every Update / LateUpdate / FixedUpdate / 2D trigger callback of
		// the game's MonoBehaviours and the pathfinder's, while a measurement runs.
		private static readonly Dictionary<MethodBase, double> HotMs = new Dictionary<MethodBase, double>();

		private void InstallHotTimers()
		{
			HashSet<string> names = new HashSet<string> { "Update", "LateUpdate", "FixedUpdate", "OnTriggerEnter2D", "OnTriggerExit2D", "OnTriggerStay2D" };
			Harmony harmony = new Harmony("ruinarch.debug.fireprobe");
			HarmonyMethod pre = new HarmonyMethod(AccessTools.Method(typeof(AutoTest), nameof(HotPre)));
			HarmonyMethod post = new HarmonyMethod(AccessTools.Method(typeof(AutoTest), nameof(HotPost)));
			int patched = 0;
			int failed = 0;
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp" || a.GetName().Name == "AstarPathfindingProject"))
			{
				Type[] types;
				try { types = a.GetTypes(); }
				catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
				foreach (Type t in types.Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.ContainsGenericParameters))
				{
					foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
						.Where(m => names.Contains(m.Name) && !m.IsAbstract && !m.ContainsGenericParameters && m.GetMethodBody() != null))
					{
						try { harmony.Patch(m, pre, post); patched++; }
						catch { failed++; }
					}
				}
			}
			Log($"  timing {patched} per-frame methods ({failed} could not be patched)");
		}

		private static void HotPre(out long __state) => __state = System.Diagnostics.Stopwatch.GetTimestamp();

		private static void HotPost(MethodBase __originalMethod, long __state)
		{
			if (FireProbeTiming.On)
			{
				HotMs.TryGetValue(__originalMethod, out double ms);
				HotMs[__originalMethod] = ms + FireProbeTiming.Ms(__state);
			}
		}

		internal static class FireProbeTiming
		{
			internal static bool On;
			// Skip the damage number and hit spark of each burn (inside Burning's tick).
			internal static bool QuietBurns;
			internal static bool InBurn;
			internal static int Ticks;
			internal static long BurningCalls;
			internal static double TickStartMs, TickEndMs, BurningMs;

			internal static void Reset()
			{
				Ticks = 0;
				BurningCalls = 0;
				TickStartMs = TickEndMs = BurningMs = 0;
			}

			internal static double Ms(long since) => (System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		}

		[HarmonyPatch(typeof(GameManager), "TickStarted")]
		internal static class FireProbe_TickStarted
		{
			private static void Prefix(out long __state) => __state = System.Diagnostics.Stopwatch.GetTimestamp();
			private static void Postfix(long __state)
			{
				if (FireProbeTiming.On) FireProbeTiming.TickStartMs += FireProbeTiming.Ms(__state);
			}
		}

		[HarmonyPatch(typeof(GameManager), "TickEnded")]
		internal static class FireProbe_TickEnded
		{
			private static void Prefix(out long __state) => __state = System.Diagnostics.Stopwatch.GetTimestamp();
			private static void Postfix(long __state)
			{
				if (FireProbeTiming.On)
				{
					FireProbeTiming.TickEndMs += FireProbeTiming.Ms(__state);
					FireProbeTiming.Ticks++;
				}
			}
		}

		[HarmonyPatch(typeof(Traits.Burning), "PerTickEnded")]
		internal static class FireProbe_Burning
		{
			private static void Prefix(out long __state)
			{
				FireProbeTiming.InBurn = true;
				__state = System.Diagnostics.Stopwatch.GetTimestamp();
			}

			private static void Postfix(long __state)
			{
				FireProbeTiming.InBurn = false;
				if (FireProbeTiming.On)
				{
					FireProbeTiming.BurningMs += FireProbeTiming.Ms(__state);
					FireProbeTiming.BurningCalls++;
				}
			}
		}

		// The game's start-of-game popup ("There are N villagers that must be killed... I'm
		// ready!") sits in the middle of the screen until clicked: click it, as a player would.
		private static bool DismissIntroPopup()
		{
			foreach (UnityEngine.UI.Button b in FindObjectsOfType<UnityEngine.UI.Button>())
			{
				if (b.isActiveAndEnabled && b.interactable && b.GetComponentInChildren<TMPro.TMP_Text>()?.text?.Trim() == "I'm ready!")
				{
					b.onClick.Invoke();
					return true;
				}
			}
			return false;
		}

		// Saves the real screen next to autotest.log, at the end of this frame.
		private IEnumerator Screenshot(string file)
		{
			if (DismissIntroPopup())
			{
				yield return new WaitForSecondsRealtime(1f);
			}
			yield return new WaitForEndOfFrame();
			Texture2D shot = null;
			try
			{
				shot = ScreenCapture.CaptureScreenshotAsTexture();
				File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(_logPath), file), shot.EncodeToPNG());
				Log($"  screenshot saved: {file}");
			}
			catch (Exception e)
			{
				Log($"  screenshot {file} failed: {e.Message}");
			}
			finally
			{
				if (shot != null) Destroy(shot);
			}
		}

		// Render a structure with a temporary orthographic camera (a copy of the game camera:
		// same culling mask, background and lighting) into an offscreen texture, and save it
		// next to the log. Screen-space UI and popups are not part of a camera render, and the
		// game camera's map-edge clamping does not apply.
		private IEnumerator Snapshot(LocationStructure structure, string name)
		{
			Camera main = InnerMapCameraMove.Instance != null ? InnerMapCameraMove.Instance.camera : null;
			if (structure?.tiles == null || structure.tiles.Count == 0 || main == null)
			{
				Log($"  screenshot {name} skipped (no tiles/camera)");
				yield break;
			}
			yield return new WaitForEndOfFrame();
			const int Size = 768;
			GameObject go = null;
			RenderTexture rt = null;
			Texture2D tex = null;
			try
			{
				Vector3 c = Vector3.zero;
				foreach (LocationGridTile t in structure.tiles)
				{
					c += t.centeredWorldLocation;
				}
				c /= structure.tiles.Count;
				go = new GameObject("AutotestCaptureCamera");
				Camera cam = go.AddComponent<Camera>();
				cam.CopyFrom(main);
				cam.orthographic = true;
				cam.orthographicSize = 4.5f;
				cam.aspect = 1f;
				cam.transform.position = new Vector3(c.x, c.y, main.transform.position.z);
				rt = new RenderTexture(Size, Size, 24);
				cam.targetTexture = rt;
				cam.Render();
				RenderTexture.active = rt;
				tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
				tex.Apply();
				RenderTexture.active = null;
				cam.targetTexture = null;
				File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(_logPath), name + ".png"), tex.EncodeToPNG());
				Log($"  screenshot saved: {name}.png (centre {c.x:F1},{c.y:F1})");
			}
			catch (Exception e)
			{
				Log($"  screenshot {name} failed: {e.Message}");
			}
			finally
			{
				if (go != null) Destroy(go);
				if (rt != null) Destroy(rt);
				if (tex != null) Destroy(tex);
			}
		}

		// ---------------------------------------------------------------------------------
		// Phase 3: a faction attacks only the demonic structures it knows about.
		private IEnumerator KnowledgeSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("knowledge suite", "RuinarchPlus not loaded");
				yield break;
			}
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			// The village with the most people left (Villages() is ordered by living residents).
			NPCSettlement village = Villages().FirstOrDefault(v => v.owner != null && v.owner.isMajorNonPlayer);
			if (portal == null || village == null)
			{
				Skip("knowledge suite", portal == null ? "no portal" : "no village of a major faction");
				yield break;
			}
			Faction faction = village.owner;
			LocationStructure outpost = Guard("place a second demonic structure", () => PlaceDemonicStructure(portal, village));
			if (outpost == null)
			{
				Skip("knowledge suite", "could not place a second demonic structure");
				yield break;
			}
			Log($"knowledge test: {faction.name} from {Describe(village)}; known structure {outpost.name} hp {outpost.currentHP}/{outpost.maxHP}, portal hp {portal.currentHP}");

			// The faction has heard of the outpost (as if reported) and nothing else.
			PlusBridge.Forget(faction);
			Guard("make the faction aware of the player", () => { faction.SetIsAwareOfPlayer(true); return faction; });
			PlusBridge.Learn(faction, outpost);

			// Held inside a building: an aware faction sends a Demon Rescue there, but only to a
			// building it knows. (Instant: placed on a tile, the rescue asked for, and put back,
			// all in one frame; the ledger is reset after in case they saw anything.)
			// Not being carried: a carried character is wherever their carrier is
			// (Character.currentStructure), so teleporting their marker moves nothing.
			Character held = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.isNormalCharacter && r.race.IsSapient()
				&& r != village.ruler && !r.partyComponent.hasParty && r.carryComponent.isBeingCarriedBy == null);
			LocationGridTile heldHome = held?.gridTileLocation;
			LocationGridTile InsideOf(LocationStructure s) =>
				s.passableTiles.FirstOrDefault(t => t.structure == s && !t.isOccupied) ?? s.tiles.FirstOrDefault(t => t.structure == s);
			LocationGridTile inPortal = InsideOf(portal);
			LocationGridTile inOutpost = InsideOf(outpost);
			if (held == null || heldHome == null || inPortal == null || inOutpost == null)
			{
				Skip("rescues go only to buildings the faction knows", held == null ? "no free resident" : "no tile inside the portal or outpost");
			}
			else
			{
				string Rescue(LocationGridTile at)
				{
					CharacterManager.Instance.Teleport(held, at);
					string where = held.currentStructure?.name ?? "nowhere";
					faction.partyQuestBoard.CreateRescuePartyQuest(null, village, held);
					DemonRescuePartyQuest q = faction.partyQuestBoard.availablePartyQuests.OfType<DemonRescuePartyQuest>().FirstOrDefault(x => x.targetCharacter == held);
					if (q != null)
					{
						faction.partyQuestBoard.RemovePartyQuest(q);
					}
					return $"in {where}: {(q == null ? "no rescue" : "rescue to " + q.targetDemonicStructure?.name)}";
				}
				bool searchBefore = village.HasJob(JOB_TYPE.SEARCH_FOR_DEMONIC_AREA);
				string unknown = Guard("ask for a rescue from the portal", () => Rescue(inPortal));
				string known = Guard("ask for a rescue from the outpost", () => Rescue(inOutpost));
				bool searchAware = village.HasJob(JOB_TYPE.SEARCH_FOR_DEMONIC_AREA);
				// Unaware: the game's answer is "search for the demonic area", whose target is the
				// Portal itself: the searcher walks straight to it.
				Guard("make the faction unaware", () => { faction.SetIsAwareOfPlayer(false); return faction; });
				string unaware = Guard("ask for a rescue from the portal (unaware)", () => Rescue(inPortal));
				bool searchUnaware = village.HasJob(JOB_TYPE.SEARCH_FOR_DEMONIC_AREA);
				Guard("make the faction aware again", () => { faction.SetIsAwareOfPlayer(true); return faction; });
				Guard("put the resident back", () => { CharacterManager.Instance.Teleport(held, heldHome); return held; });
				PlusBridge.Forget(faction);
				PlusBridge.Learn(faction, outpost);
				Check("rescues go only to buildings the faction knows", () =>
					(unknown != null && unknown.EndsWith("no rescue") && known != null && known.EndsWith("rescue to " + outpost.name),
					$"{held.name} {unknown}; {known}"));
				Check("nobody is sent straight to the Portal to 'search' for it", () =>
					(!searchAware && !searchUnaware && unaware != null && unaware.EndsWith("no rescue"),
					$"search job before={searchBefore} after aware rescue={searchAware} after unaware rescue={searchUnaware}; unaware: {unaware}"));
			}

			CounterattackPartyQuest quest = Guard("create a counterattack", () =>
			{
				faction.partyQuestBoard.CreateCounterattackPartyQuest(null, village);
				return faction.partyQuestBoard.GetPartyQuest(PARTY_QUEST_TYPE.Counterattack) as CounterattackPartyQuest;
			});
			Check("a counterattack sets out for the structure they know, not the player's whole domain", () =>
			{
				IPartyTargetDestination d = quest?.GetTargetDestination();
				return (d != null && d.persistentID == outpost.persistentID, $"destination={(d as LocationStructure)?.name ?? d?.GetType().Name ?? "null"}");
			});

			// A new world has no villager parties yet (the game forms them over time). Give the
			// quest a few hours to be taken naturally, then form a party the game's own way.
			if (quest != null)
			{
				yield return WaitGameHours(6f, () => quest.assignedParty != null);
				if (quest.assignedParty == null)
				{
					yield return WaitForHour(5);
					FormPartyFor(quest, village, 3, 4);
				}
			}

			// Let it run. Invariant: nobody from this faction attacks the portal before the
			// faction knows it (they may learn it by seeing it on the way; that is allowed).
			int outpostStart = outpost.currentHP;
			string violation = null;
			DemonicStructure portalStructure = portal as DemonicStructure;
			yield return WaitGameHours(48f, () =>
			{
				if (violation == null && portalStructure != null && !PlusBridge.Knows(faction, portal))
				{
					Character attacker = portalStructure.currentAttackers.FirstOrDefault(a => a != null && a.faction == faction);
					if (attacker != null)
					{
						violation = $"{attacker.name} attacked the portal while {faction.name} did not know it";
					}
				}
				return outpost.hasBeenDestroyed || outpost.currentHP < outpostStart;
			});
			Party party = quest?.assignedParty;
			string partyState = party == null ? "no party took the quest" : $"party {party.partyState}, {party.membersThatJoinedQuest.Count} joined";
			if (party == null && !outpost.hasBeenDestroyed && outpost.currentHP >= outpostStart)
			{
				Skip("the counterattack hits the structure they know", partyState + " within 48h");
			}
			else
			{
				Check("the counterattack hits the structure they know", () =>
					(outpost.hasBeenDestroyed || outpost.currentHP < outpostStart, $"outpost hp {outpost.currentHP}/{outpostStart} destroyed={outpost.hasBeenDestroyed}; {partyState}"));
			}
			Check("no one attacks a portal their faction has never seen", () => (violation == null, violation ?? $"portal hp {portal.currentHP}; known now={PlusBridge.Knows(faction, portal)}"));
			// The party on site: its faction now knows nothing still standing (the ledger is
			// wiped, as when everything it knew has been destroyed). It must go home, not do
			// what the base game does next and march on the Portal.
			if (party != null && party.isActive && party.partyState == PARTY_STATE.Working)
			{
				int portalBefore = portal.currentHP;
				PlusBridge.Forget(faction);
				yield return WaitGameHours(6f, () => !party.isActive || party.partyState != PARTY_STATE.Working || portal.currentHP < portalBefore);
				Check("a party that knows nothing still standing goes home instead of attacking the Portal", () =>
					(portal.currentHP >= portalBefore && (!party.isActive || party.partyState != PARTY_STATE.Working),
					$"portal hp {portalBefore} -> {portal.currentHP}; party active={party.isActive} state={party.partyState} quest={party.currentQuest?.GetType().Name ?? "none"}"));
			}
			else
			{
				Skip("a party that knows nothing still standing goes home instead of attacking the Portal", partyState + " (not working on site)");
			}
			// Done with the counterattack: call it off now.
			CallOffCounterattacks(faction);

			// Seeing is not knowing: a villager of the (aware) faction who sees the portal carries
			// the news; their faction learns it only when they get home alive.
			PlusBridge.Forget(faction);
			List<Character> witnesses = Villages().Where(v => v.owner == faction).SelectMany(v => v.residents).Where(r => r != null && !r.isDead && r.marker != null && r.limiterComponent.canWitness
				&& r.race.IsSapient() && r.isNormalCharacter && !r.isAlliedWithPlayer && (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive) && r.carryComponent.isBeingCarriedBy == null
				&& r != r.homeSettlement?.ruler && !r.isFactionLeader && r.homeSettlement?.cityCenter != null).Take(2).ToList();
			LocationGridTile near = portal.tiles.SelectMany(t => t.neighbourList).FirstOrDefault(t => t != null && t.structure != portal && !t.isOccupied);
			if (witnesses.Count < 2 || near == null)
			{
				const string why = "news of a sighting travels with the witness";
				Skip(why, near == null ? "no free tile by the portal" : $"{witnesses.Count} free witness(es), need 2: "
					+ string.Join("; ", Villages().Where(v => v.owner == faction).SelectMany(v => v.residents).Where(r => r != null && !r.isDead)
						.Select(r => $"{r.name} party={r.partyComponent.currentParty?.isActive} normal={r.isNormalCharacter} witness={r.limiterComponent.canWitness} carried={r.carryComponent.isBeingCarriedBy != null}")));
			}
			else
			{
				// A witness killed before getting home: the news dies with them.
				Character doomed = witnesses[0];
				Guard("place a doomed witness by the portal", () => { doomed.marker.PlaceMarkerAt(near); return doomed; });
				yield return WaitGameHours(2f, () => PlusBridge.Carries(doomed, portal));
				bool sawIt = PlusBridge.Carries(doomed, portal);
				Guard("kill the witness", () => { doomed.Death("autotest"); return doomed; });
				yield return WaitGameHours(2f, null);
				Check("a witness killed on the way home takes the news with them", () =>
					(sawIt && !PlusBridge.Knows(faction, portal) && !PlusBridge.Carries(doomed, portal),
					$"{doomed.name} saw it={sawIt}; faction knows={PlusBridge.Knows(faction, portal)}"));

				// A witness who gets home tells their people.
				Character witness = witnesses[1];
				Guard("place a witness by the portal", () => { witness.marker.PlaceMarkerAt(near); return witness; });
				yield return WaitGameHours(2f, () => PlusBridge.Carries(witness, portal));
				Check("a villager who sees the portal carries the news; their faction does not know yet", () =>
					(PlusBridge.Carries(witness, portal) && !PlusBridge.Knows(faction, portal),
					$"{witness.name} at {witness.gridTileLocation?.localPlace} carries={PlusBridge.Carries(witness, portal)} faction knows={PlusBridge.Knows(faction, portal)}"));
				// The bookmarks panel's "Who Knows of You" section names the carrier.
				yield return WaitGameHours(0.2f, null);
				List<string> carrying = PlusBridge.KnowledgePanelLines() ?? new List<string>();
				string header = FindObjectsOfType<BookmarkCategoryItemUI>().Where(i => (int)i.category == 100)
					.Select(i => (AccessTools.Field(typeof(BookmarkCategoryItemUI), "lblHeaderName").GetValue(i) as TMPro.TMP_Text)?.text).FirstOrDefault();
				yield return Screenshot("whoknows.png");
				Check("the bookmarks panel shows who is carrying news of you", () =>
					(header == "Who Knows of You" && carrying.Any(l => l.Contains(witness.name) && l.Contains("is carrying news of your") && l.Contains(portal.name)),
					$"section header={header ?? "none"}; lines: {string.Join(" / ", carrying)}"));
				LocationGridTile home = witness.homeSettlement.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? witness.homeSettlement.cityCenter.passableTiles.FirstOrDefault();
				// Out of any party first: a party on a quest leads its members away again.
				Guard("send the witness home", () =>
				{
					witness.partyComponent.currentParty?.RemoveMember(witness);
					CharacterManager.Instance.Teleport(witness, home);
					return witness;
				});
				yield return WaitGameHours(3f, () => PlusBridge.Knows(faction, portal));
				Check("back home, the witness teaches it to their faction", () =>
					(PlusBridge.Knows(faction, portal) && !PlusBridge.Carries(witness, portal),
					$"{witness.name} of {witness.faction?.name ?? "no faction"} (home {witness.homeSettlement?.name ?? "-"}) at {witness.gridTileLocation?.localPlace} in structure {witness.currentStructure?.name}/{witness.currentSettlement?.name ?? "-"} (owner {witness.currentSettlement?.owner?.name ?? "-"}), area {witness.gridTileLocation?.area?.GetFirstNPCSettlementOnArea()?.name ?? "the wild"}; known={PlusBridge.Knows(faction, portal)} carries={PlusBridge.Carries(witness, portal)}"));
				yield return WaitGameHours(0.2f, null);
				List<string> knowing = PlusBridge.KnowledgePanelLines() ?? new List<string>();
				Check("the bookmarks panel shows what a faction knows", () =>
					(knowing.Any(l => l.Contains(faction.name) && l.Contains("know of your") && l.Contains(portal.name))
						&& !knowing.Any(l => l.Contains(witness.name) && l.Contains("is carrying")),
					string.Join(" / ", knowing)));
			}

			yield return KnowledgeSaveRoundTrip(faction, portal);

			// Leave the world as found: the faction no longer knows of the player, and its
			// counterattacks are called off. (Otherwise they destroy the Portal later in the run
			// and the world ends in defeat.)
			PlusBridge.Forget(faction);
			CallOffCounterattacks(faction);
			Guard("make the faction forget the player", () => { faction.SetIsAwareOfPlayer(false); return faction; });
		}

		private void CallOffCounterattacks(Faction faction)
		{
			Guard("call off the counterattacks", () =>
			{
				foreach (CounterattackPartyQuest q in faction.partyQuestBoard.availablePartyQuests.OfType<CounterattackPartyQuest>().ToList())
				{
					q.EndQuest(PartyQuest.GetLocalizedEndQuestReason("Finished_Quest"));
				}
				return faction;
			});
		}

		// Phase 1 exploit fixes. A monster is dropped into a Kennel (restrained, the Kennel's
		// occupant), then made to fly and freed of its restraints: it only hovers over the
		// Kennel. Cast on the Kennel, Sacrifice and Let It Go must refuse it (vanilla takes it).
		// Then the Snatch window with nothing bookmarked must still offer drop-off points.
		private IEnumerator ExploitSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("exploit fixes", "RuinarchPlus not loaded");
				yield break;
			}
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			NPCSettlement anyVillage = Villages().FirstOrDefault();
			Kennel kennel = portal == null || anyVillage == null ? null : Guard("place a Kennel", () => PlaceDemonicStructure(portal, anyVillage, STRUCTURE_TYPE.KENNEL) as Kennel);
			LocationGridTile cell = kennel?.tiles.FirstOrDefault(t => kennel.IsTilePartOfARoom(t, out _) && !t.isOccupied);
			SacrificeData sacrifice = PlayerSkillManager.Instance.GetPlayerActionData(PLAYER_SKILL_TYPE.SACRIFICE) as SacrificeData;
			LetGoData letGo = PlayerSkillManager.Instance.GetPlayerActionData(PLAYER_SKILL_TYPE.LET_GO) as LetGoData;
			if (cell == null || sacrifice == null || letGo == null)
			{
				Skip("Sacrifice or Let It Go on a Kennel skips a monster only flying over it", kennel == null ? "could not place a Kennel" : cell == null ? "no free Kennel cell tile" : "no Sacrifice / Let It Go data");
			}
			else
			{
				Summon monster = Guard("drop a monster into the Kennel", () =>
				{
					Summon s = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Wolf, null, homeLocation: null,
						homeRegion: GridMap.Instance.mainRegion, homeStructure: null, className: "", bypassIdeologyChecking: true);
					s.CreateMarker();
					s.InitialCharacterPlacement(cell);
					s.marker.UpdatePosition();
					kennel.OnSnatchedCharacterDroppedHere(s);
					return s;
				});
				bool held = monster != null && kennel.occupyingSummon == monster && sacrifice.IsValid(kennel);
				Check("a monster held in a Kennel can be sacrificed (vanilla)", () =>
					(held, $"occupant={kennel.occupyingSummon?.name ?? "none"} restrained={monster?.traitContainer.HasTrait("Restrained")} valid={sacrifice.IsValid(kennel)}"));
				if (held)
				{
					Guard("the monster flies free", () => { monster.movementComponent.SetToFlying(); monster.traitContainer.RemoveTrait(monster, "Restrained"); return monster; });
					PlusBridge.SetConfig("closeExploits", false);
					bool vanillaSacrifice = sacrifice.IsValid(kennel);
					bool vanillaLetGo = letGo.CanPerformAbilityTowards(monster);
					PlusBridge.SetConfig("closeExploits", true);
					bool fixedSacrifice = sacrifice.IsValid(kennel);
					bool fixedLetGo = letGo.CanPerformAbilityTowards(monster);
					Try("cast Sacrifice on the Kennel anyway", () => sacrifice.ActivateAbility(kennel));
					Check("Sacrifice on a Kennel skips a monster only flying over it", () =>
						(vanillaSacrifice && !fixedSacrifice && !monster.isDead,
						$"flying={monster.movementComponent.isFlying} restrained={monster.traitContainer.HasTrait("Restrained")} valid: vanilla={vanillaSacrifice} fixed={fixedSacrifice}; dead after casting={monster.isDead}"));
					Check("Let It Go on a Kennel skips a monster only flying over it", () =>
						(vanillaLetGo && !fixedLetGo, $"can let go: vanilla={vanillaLetGo} fixed={fixedLetGo}"));
				}
				Guard("remove the monster", () => { if (monster != null && !monster.isDead) { monster.Death("autotest"); } return monster; });
			}
			if (kennel != null)
			{
				Guard("destroy the Kennel", () => { kennel.AdjustHP(-kennel.currentHP); return kennel; });
			}

			SnatchObjectUIController snatch = UIManager.Instance?.snatchObjectUIController;
			Character target = anyVillage?.residents.FirstOrDefault(r => r != null && !r.isDead);
			if (snatch == null || target == null)
			{
				Skip("with nothing bookmarked, Snatch still offers drop-off points", snatch == null ? "no Snatch window" : "no villager to snatch");
				yield break;
			}
			List<IStoredTarget> bookmarks = PlayerManager.Instance.player.storedTargetsComponent.storedStructures;
			List<IStoredTarget> kept = bookmarks.ToList();
			List<IStoredTarget> offered = null;
			Try("open the Snatch drop-off list with no bookmarks", () =>
			{
				bookmarks.Clear();
				Traverse.Create(snatch).Field("_chosenTarget").SetValue(target);
				AccessTools.Method(typeof(SnatchObjectUIController), "ConstructDropLocationChoices").Invoke(snatch, null);
				offered = Traverse.Create(snatch).Field("_allValidDropLocations").GetValue<List<IStoredTarget>>().ToList();
			});
			Try("restore the bookmarks", () =>
			{
				bookmarks.Clear();
				bookmarks.AddRange(kept);
				Traverse.Create(snatch).Field("_chosenTarget").SetValue(null);
				Traverse.Create(snatch).Field("_allValidDropLocations").GetValue<List<IStoredTarget>>().Clear();
			});
			Check("with nothing bookmarked, Snatch still offers drop-off points", () =>
				(offered != null && offered.Count > 0 && offered.All(o => o is DemonicStructure),
				offered == null ? "not built" : $"{offered.Count} offered: {string.Join(", ", offered.Select(o => o.bookmarkName))} (bookmarks kept: {kept.Count})"));
		}

		// Phase 3, the rest of the fog of war.
		// Next door: a village bordering a demonic building nobody has seen neither becomes
		// aware nor counterattacks (vanilla does both); once it knows the building, it does.
		// Gossip: a villager of one faction tells a villager of a friendly faction about the
		// Portal; back home, that faction knows it and is aware of the demons.
		private IEnumerator FogSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("fog of war", "RuinarchPlus not loaded");
				yield break;
			}
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			Faction player = PlayerManager.Instance.player.playerFaction;
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer && v.owner.IsHostileWith(player))
				.OrderByDescending(v => v.residents.Count(r => r != null && !r.isDead)).FirstOrDefault();
			LocationStructure nextDoor = village == null ? null : Guard("place a demonic building next to a village", () => PlaceDemonicStructureNextTo(portal, village));
			if (nextDoor == null)
			{
				Skip("a village next to a demonic building nobody has seen stays unaware", village == null ? "no village of a major faction hostile to the player" : "no room next to the village");
			}
			else
			{
				Faction faction = village.owner;
				bool wasAware = faction.isAwareOfPlayer;
				PlusBridge.Forget(faction);
				CallOffCounterattacks(faction);
				Guard("make the faction unaware", () => { faction.SetIsAwareOfPlayer(false); return faction; });
				System.Reflection.MethodInfo neighbours = AccessTools.Method(typeof(SettlementPartyComponent), "TryCreateCounterattackQuest");
				Try("the village looks next door", () => neighbours.Invoke(village.partyComponent, new object[] { faction, "" }));
				bool unseenAware = faction.isAwareOfPlayer;
				bool unseenAttack = faction.partyQuestBoard.HasPartyQuest(PARTY_QUEST_TYPE.Counterattack);
				Check("a village next to a demonic building nobody has seen stays unaware", () =>
					(!unseenAware && !unseenAttack, $"{village.name} next to {nextDoor.name}: aware={unseenAware} counterattack={unseenAttack}"));
				PlusBridge.Learn(faction, nextDoor);
				Try("the village looks next door again", () => neighbours.Invoke(village.partyComponent, new object[] { faction, "" }));
				Check("once it knows the building next door, it counterattacks", () =>
					(faction.isAwareOfPlayer && faction.partyQuestBoard.HasPartyQuest(PARTY_QUEST_TYPE.Counterattack),
					$"aware={faction.isAwareOfPlayer} counterattack={faction.partyQuestBoard.HasPartyQuest(PARTY_QUEST_TYPE.Counterattack)}"));
				CallOffCounterattacks(faction);
				PlusBridge.Forget(faction);
				Guard("restore the faction's awareness", () => { faction.SetIsAwareOfPlayer(wasAware); return faction; });
				Guard("destroy the building next door", () => { nextDoor.AdjustHP(-nextDoor.currentHP); return nextDoor; });
			}

			// Gossip between two friendly factions.
			Func<Character, bool> free = r => r != null && !r.isDead && r.hasMarker && r.isNormalCharacter && r.race.IsSapient() && r.limiterComponent.canMove
				&& r.limiterComponent.canWitness && (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive) && r.carryComponent.isBeingCarriedBy == null && r != r.homeSettlement?.ruler
				&& !r.isFactionLeader && !r.isAlliedWithPlayer && r.homeSettlement?.cityCenter != null;
			List<NPCSettlement> majors = Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer && v.residents.Any(free)).ToList();
			NPCSettlement from = null, to = null;
			foreach (NPCSettlement a in majors)
			{
				to = majors.FirstOrDefault(b => b.owner != a.owner && !a.owner.IsHostileWith(b.owner));
				if (to != null)
				{
					from = a;
					break;
				}
			}
			if (from == null || portal == null)
			{
				Skip("a villager tells someone of a friendly faction about a demonic building", portal == null ? "no portal" : "no two friendly major factions with free villagers: "
					+ string.Join(", ", majors.Select(v => v.owner.name).Distinct()));
				yield break;
			}
			Faction tellers = from.owner, listeners = to.owner;
			bool tellersAware = tellers.isAwareOfPlayer, listenersAware = listeners.isAwareOfPlayer;
			Character teller = from.residents.First(free);
			Character listener = to.residents.First(free);
			PlusBridge.Forget(tellers);
			PlusBridge.Forget(listeners);
			Guard("make the listeners' faction unaware", () => { listeners.SetIsAwareOfPlayer(false); return listeners; });
			PlusBridge.Learn(tellers, portal);
			PlusBridge.SetConfig("gossipChance", 100);
			LocationGridTile beside = teller.gridTileLocation?.neighbourList.FirstOrDefault(t => t != null && !t.isOccupied && t.structure == teller.gridTileLocation.structure)
				?? teller.gridTileLocation?.neighbourList.FirstOrDefault(t => t != null && !t.isOccupied);
			Guard("bring the listener to the teller", () => { CharacterManager.Instance.Teleport(listener, beside); return listener; });
			yield return WaitGameHours(2f, () => PlusBridge.Carries(listener, portal));
			Check("a villager tells someone of a friendly faction about a demonic building", () =>
				(PlusBridge.Carries(listener, portal) && !PlusBridge.Knows(listeners, portal),
				$"{teller.name} of {tellers.name} at {teller.gridTileLocation?.localPlace} -> {listener.name} of {listeners.name} at {listener.gridTileLocation?.localPlace}: carries={PlusBridge.Carries(listener, portal)} their faction knows={PlusBridge.Knows(listeners, portal)}"));
			if (PlusBridge.Carries(listener, portal))
			{
				LocationGridTile home = listener.homeSettlement.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? listener.homeSettlement.cityCenter.passableTiles.FirstOrDefault();
				Guard("send the listener home", () => { CharacterManager.Instance.Teleport(listener, home); return listener; });
				yield return WaitGameHours(3f, () => PlusBridge.Knows(listeners, portal));
				Check("news heard from another faction makes the listener's faction aware", () =>
					(PlusBridge.Knows(listeners, portal) && listeners.isAwareOfPlayer,
					$"{listeners.name} knows={PlusBridge.Knows(listeners, portal)} aware={listeners.isAwareOfPlayer}"));
			}
			PlusBridge.SetConfig("gossipChance", 25);
			foreach (Faction f in new[] { tellers, listeners })
			{
				PlusBridge.Forget(f);
				CallOffCounterattacks(f);
			}
			Guard("restore awareness", () => { tellers.SetIsAwareOfPlayer(tellersAware); listeners.SetIsAwareOfPlayer(listenersAware); return tellers; });
		}

		// The ledger rides inside the real save zip and comes back through the real load hook.
		private IEnumerator KnowledgeSaveRoundTrip(Faction faction, LocationStructure portal)
		{
			if (!PlusBridge.Knows(faction, portal))
			{
				PlusBridge.Learn(faction, portal);
			}
			string json = null;
			string entries = null;
			yield return SaveAndRead("ruinarch.plus.knowledge.json", (j, e) => { json = j; entries = e; });
			Check("knowledge is stored inside the player's save file", () =>
				(json != null && json.Contains(portal.persistentID), json == null ? "entries: " + entries : $"{json.Length} bytes: {json.Substring(0, Math.Min(json.Length, 160))}"));
			if (json == null)
			{
				yield break;
			}
			PlusBridge.Forget(faction);
			ReplayLoad("ruinarch.plus.knowledge.json", json);
			Check("knowledge comes back when the save loads", () => (PlusBridge.Knows(faction, portal), $"known after load={PlusBridge.Knows(faction, portal)}"));

			// A save from before the ledger (no knowledge data): factions already aware of the
			// player know all of the player's buildings, as in the base game, from the first hour.
			if (faction.isAwareOfPlayer)
			{
				ReplayLoad("ruinarch.plus.knowledge.json", "");
				bool emptied = !PlusBridge.Knows(faction, portal);
				yield return WaitGameHours(2f, () => PlusBridge.Knows(faction, portal));
				Check("loading a save from before the ledger: aware factions know the player's buildings", () =>
					(emptied && PlusBridge.Knows(faction, portal), $"emptied by the load={emptied}; {faction.name} knows the Portal an hour later={PlusBridge.Knows(faction, portal)}"));
				// Back to what this world really knows (other aware factions were given everything too).
				ReplayLoad("ruinarch.plus.knowledge.json", json);
			}
			else
			{
				Skip("loading a save from before the ledger: aware factions know the player's buildings", $"{faction.name} is not aware of the player");
			}
		}

		// Phase 3: a resident none of their people has seen for a while is reported missing,
		// and the village searches where they were last seen. Three residents are stranded in
		// the wilderness at once, each one "last seen" there by the village: a restrained
		// captive left where they were seen (found and freed), a restrained one moved far
		// away after being seen (the search fails, is retried, and is given up), and one
		// killed where they were seen (found dead). Timings are shortened for the run.
		private IEnumerator MissingPersonsSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("missing persons", "RuinarchPlus not loaded");
				yield break;
			}
			// Sapient villagers only: a village can also house monsters (a tamed Wyvern), which
			// are not tracked. Pick the village with the most such free residents, not the
			// biggest one: the biggest may be busy (a counterattack party, a ruler's household).
			// Sitting in an idle party (no quest) is fine: they are taken out of it, as the
			// knowledge test does.
			Func<NPCSettlement, Character, bool> free = (v, r) => r != null && !r.isDead && r.hasMarker && r.isNormalCharacter
				&& r.race.IsSapient() && r != v.ruler && !r.isFactionLeader && r.limiterComponent.canMove
				&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive)
				// A carried character is wherever the carrier is: teleporting them moves nothing.
				&& r.carryComponent.isBeingCarriedBy == null;
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer && !v.isPlagued)
				.OrderByDescending(v => v.residents.Count(r => free(v, r))).FirstOrDefault();
			// Villagers in no party first: taking someone out of an idle party can empty it,
			// and an empty party disbands.
			// Not the family LifeSuite made (a lover and a child look in on each other).
			List<Character> people = village?.residents.Where(r => free(village, r) && !_lifeFamily.Contains(r)).OrderBy(r => r.partyComponent.hasParty).ToList();
			if (people == null || people.Count < 3)
			{
				Skip("missing persons", "no village with three free residents: " + string.Join("; ", Villages().Select(v =>
				{
					List<Character> alive = v.residents.Where(r => r != null && !r.isDead).ToList();
					return $"{v.name} alive={alive.Count} free={alive.Count(r => free(v, r))} noMarker={alive.Count(r => !r.hasMarker)} notNormal={alive.Count(r => !r.isNormalCharacter)} inParty={alive.Count(r => r.partyComponent.hasParty)} cantMove={alive.Count(r => !r.limiterComponent.canMove)}";
				})));
				yield break;
			}
			people = people.Take(3).ToList();
			foreach (Character p in people.Where(p => p.partyComponent.hasParty))
			{
				Guard("leave idle party", () => { p.partyComponent.currentParty.RemoveMember(p); return p; });
				Log($"  took {p.name} out of an idle party (the test needs three free villagers)");
			}
			LocationGridTile centre = village.areas[0].gridTileComponent.centerGridTile;
			// Out of everyone's way: 30+ tiles from any settlement, so nobody of their faction
			// happens to see them (a sighting is the feature working, but it hides what the
			// checks measure).
			List<LocationGridTile> settlementCentres = GridMap.Instance.mainRegion.settlementsInRegion
				.SelectMany(s => s.areas).Select(a => a.gridTileComponent.centerGridTile).Where(t => t != null).ToList();
			List<LocationGridTile> wild = GridMap.Instance.mainRegion.areas
				.Where(a => !a.IsNextToOrPartOfVillage() && !a.HasSettlementOnArea() && a.gridTileComponent.centerGridTile != null
					&& settlementCentres.All(t => t.GetDistanceTo(a.gridTileComponent.centerGridTile) >= 30f)
					&& !a.gridTileComponent.centerGridTile.isOccupied && a.gridTileComponent.centerGridTile.structure is Wilderness
					&& people[0].movementComponent.HasPathTo(a.gridTileComponent.centerGridTile))
				.Select(a => a.gridTileComponent.centerGridTile)
				.OrderBy(t => t.GetDistanceTo(centre)).ToList();
			if (wild.Count < 4)
			{
				Skip("missing persons", $"only {wild.Count} reachable wilderness spot(s)");
				yield break;
			}
			LocationGridTile spotCaptive = wild[0];
			LocationGridTile spotWanderer = wild[1];
			LocationGridTile spotVictim = wild[2];
			// The one nobody should stumble on: as far as possible from every settlement and
			// from the other three spots (the searches for them sweep the land around them).
			LocationGridTile far = wild.Skip(3).OrderByDescending(t => Math.Min(settlementCentres.Min(c => c.GetDistanceTo(t)),
				new[] { spotCaptive, spotWanderer, spotVictim }.Min(s => s.GetDistanceTo(t)))).First();
			Log($"missing persons village: {Describe(village)}; spots captive={spotCaptive} wanderer={spotWanderer} far={far} victim={spotVictim}");

			PlusBridge.SetConfig("missingAfterHours", 4);
			PlusBridge.SetConfig("searchSweepHours", 2);
			PlusBridge.SetConfig("searchRetryHours", 4);
			PlusBridge.SetConfig("searchMaxAttempts", 3);
			Character captive = people[0];
			Character wanderer = people[1];
			Character victim = people[2];
			Guard("strand the missing", () =>
			{
				CharacterManager.Instance.Teleport(captive, spotCaptive);
				captive.traitContainer.AddTrait(captive, "Restrained");
				PlusBridge.MissingSaw(captive, spotCaptive);
				CharacterManager.Instance.Teleport(wanderer, spotWanderer);
				PlusBridge.MissingSaw(wanderer, spotWanderer);
				CharacterManager.Instance.Teleport(wanderer, far);
				wanderer.traitContainer.AddTrait(wanderer, "Restrained");
				CharacterManager.Instance.Teleport(victim, spotVictim);
				PlusBridge.MissingSaw(victim, spotVictim);
				victim.Death("autotest");
				return captive;
			});
			Log($"  captive {captive.name} restrained={captive.traitContainer.HasTrait("Restrained")} at {captive.gridTileLocation?.localPlace}, wanderer {wanderer.name} restrained={wanderer.traitContainer.HasTrait("Restrained")} at {wanderer.gridTileLocation?.localPlace} (far {far.localPlace}), victim {victim.name} dead={victim.isDead}");

			// 1. The base game would roll a rescue for a restrained resident nobody saw.
			yield return WaitGameHours(3f, null);
			Check("no rescue before anyone misses them", () =>
			{
				bool quest = village.owner.partyQuestBoard.availablePartyQuests.OfType<RescuePartyQuest>().Any(q => q.targetCharacter == captive);
				return (!quest, $"rescue quest={quest} state={PlusBridge.MissingState(captive)}");
			});

			// 2. Unseen for missingAfterHours: missing, announced.
			yield return WaitGameHours(4f, () => PlusBridge.MissingState(captive) is string s && s != "Seen");
			foreach (Character who in new[] { captive, wanderer, victim })
			{
				List<string> seers = village.owner.characters.Where(w => w != null && !w.isDead && w.hasMarker && w.marker.inVisionPOIs.Contains(who))
					.Select(w => w.name).ToList();
				Log($"  {who.name}: state={PlusBridge.MissingState(who) ?? "untracked"} lastSeen={PlusBridge.MissingLastSeen(who) ?? "-"} seenBy=[{string.Join(", ", seers)}] inHome={who.IsInHomeSettlement()}");
			}
			Check("an unseen resident is reported missing", () =>
			{
				string s = PlusBridge.MissingState(captive);
				bool announced = ModsLogHas($"{captive.name} of {village.name} has gone missing");
				return ((s == "Missing" || s == "Searching") && announced, $"state={s ?? "untracked"} announced={announced}");
			});
			Check("the missing notice links to the person", () =>
			{
				// Read the notification as shown, then resolve its first link the way the game's
				// EventLabel does on click: "Type|persistentID" looked up in the game database.
				PlayerNotificationItem item = UIManager.Instance.activeNotifications
					.LastOrDefault(n => n != null && n.currentTextDisplayed.Contains("has gone missing") && n.currentTextDisplayed.Contains(captive.name));
				if (item == null)
				{
					return (false, "no notification on screen");
				}
				string text = item.currentTextDisplayed;
				int at = text.IndexOf("<link=", StringComparison.Ordinal);
				int end = at < 0 ? -1 : text.IndexOf('>', at);
				if (end < 0)
				{
					return (false, "no link: " + text);
				}
				string[] id = text.Substring(at + 6, end - at - 6).Split('|');
				Type type = id.Length == 2 ? typeof(Character).Assembly.GetType(id[0]) : null;
				object opened = type == null ? null : DatabaseManager.Instance.GetObjectFromDatabase(type, id[1]);
				return (opened == captive, $"link {id[0]}|{(id.Length == 2 ? id[1] : "?")} opens {(opened as Character)?.name ?? opened?.ToString() ?? "nothing"}");
			});

			// 3. The search goes where they were seen, not where they are.
			PartyQuest search = null;
			yield return WaitGameHours(2f, () => (search = PlusBridge.MissingSearch(wanderer)) != null);
			if (search == null && PlusBridge.MissingState(wanderer) == "Seen")
			{
				Skip("the search heads for the last-seen spot, not where they are", $"{wanderer.name} was come across before a search was organised (last seen {PlusBridge.MissingLastSeen(wanderer)})");
				Skip("the search is named as a search", "no search: the wanderer was found by chance");
			}
			else
			{
				Check("the search heads for the last-seen spot, not where they are", () =>
				{
					IPartyTargetDestination d = search?.GetTargetDestination();
					bool atLastSeen = d == spotWanderer.area || d == spotWanderer.structure;
					bool atLive = d == far.area || d == far.structure;
					return (search != null && atLastSeen && !atLive,
						$"destination={(d as Area)?.name ?? (d as LocationStructure)?.name ?? "none"} lastSeen={spotWanderer.area.name} live={far.area.name}");
				});
				Check("the search is named as a search", () => (search?.GetPartyQuestName() == "Search for " + wanderer.name, "name=" + (search?.GetPartyQuestName() ?? "no search")));
			}

			// 4-6. Let the searches run. Parties take quests at dawn; if none took a search by
			// 8 am, form one at the next 5 am as the game would.
			float start = GameHours;
			List<float> gaps = new List<float>();
			string wandererWas = PlusBridge.MissingState(wanderer);
			string captiveWas = PlusBridge.MissingState(captive);
			Func<bool> settled = () => PlusBridge.MissingState(wanderer) == "Lost" && PlusBridge.MissingState(victim) == null
				&& !captive.traitContainer.HasTrait("Restrained");
			_partyShortages = 0;
			while (GameHours - start < 220f && !settled())
			{
				yield return WaitGameHours(1f, () =>
				{
					string w = PlusBridge.MissingState(wanderer);
					if (w != wandererWas)
					{
						Log($"  {wanderer.name}: {wandererWas} -> {w} after {GameHours - start:F1}h (failed searches {PlusBridge.FailedSearches(wanderer)}, next in {PlusBridge.HoursToNextSearch(wanderer):F1}h)");
						if (w == "Missing" && wandererWas == "Searching")
						{
							gaps.Add(PlusBridge.HoursToNextSearch(wanderer));
						}
						wandererWas = w;
					}
					string cs = PlusBridge.MissingState(captive);
					if (cs != captiveWas)
					{
						Log($"  {captive.name}: {captiveWas} -> {cs} after {GameHours - start:F1}h");
						captiveWas = cs;
					}
					return settled();
				});
				if (GameManager.Instance.Today().tick / GameManager.ticksPerHour == 8
					&& new[] { captive, wanderer, victim }.Select(PlusBridge.MissingSearch).Any(q => q != null && q.assignedParty == null))
				{
					yield return WaitForHour(5);
					foreach (PartyQuest idle in new[] { captive, wanderer, victim }.Select(PlusBridge.MissingSearch).Where(q => q != null && q.assignedParty == null).ToList())
					{
						FormPartyFor(idle, village, 1, 2);
					}
				}
			}
			Check("a captive left where they were seen is found and freed", () =>
			{
				bool freed = !captive.traitContainer.HasTrait("Restrained");
				bool announced = ModsLogHas($"{captive.name} of {village.name} has been found.");
				// After being found, they may die or move away later; the record is then dropped.
				string state = PlusBridge.MissingState(captive);
				return (freed && announced && (state == "Seen" || state == null),
					$"freed={freed} announced={announced} state={state ?? $"dropped (dead={captive.isDead} home={captive.homeSettlement?.name ?? "-"})"}");
			});
			// The world can wipe the test village out mid-test (famine, monsters). A village
			// with no owner keeps no records: nothing left to check.
			if (village.owner == null || village.residents.Count(r => r != null && !r.isDead) < 2)
			{
				foreach (string name in new[] { "a resident killed out of sight is found dead", "failed searches are retried less often, then given up",
					"missing persons are stored inside the player's save file", "missing persons come back when the save loads" })
				{
					Skip(name, $"{village.name} fell apart during the test: {Describe(village)}");
				}
				ResetMissingConfig();
				yield break;
			}
			// Searches need people: a village whose residents were all busy (no party could be
			// formed morning after morning) runs out of test time with searches still pending.
			string starved = !settled() && _partyShortages >= 3
				? $"no search party could be formed {_partyShortages} times: {village.name}'s residents were busy ({Describe(village)})" : null;
			if (starved != null && PlusBridge.MissingState(victim) != null)
			{
				Skip("a resident killed out of sight is found dead", starved);
			}
			else Check("a resident killed out of sight is found dead", () =>
			{
				bool announced = ModsLogHas($"{victim.name} of {village.name} has been found dead.");
				return (announced && PlusBridge.MissingState(victim) == null, $"announced={announced} state={PlusBridge.MissingState(victim) ?? "dropped"}");
			});
			// The far spot is only far from the settlements: anyone passing by (a hunter, a
			// caravan, another search) can come across the wanderer before a search fails, and
			// then there is nothing left to retry.
			if (starved != null && PlusBridge.MissingState(wanderer) != "Lost" && PlusBridge.FailedSearches(wanderer) > 0)
			{
				Skip("failed searches are retried less often, then given up", starved);
			}
			else if (PlusBridge.FailedSearches(wanderer) == 0 && PlusBridge.MissingState(wanderer) != "Lost")
			{
				Skip("failed searches are retried less often, then given up", $"{wanderer.name} was come across before any search failed (state={PlusBridge.MissingState(wanderer) ?? "dropped"})");
			}
			else
			{
				Check("failed searches are retried less often, then given up", () =>
				{
					// The waits the mod announced for this person, in order (a poll can miss a short
					// Missing spell, e.g. while the harness waits for dawn to form a party).
					string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
					string pattern = $"The search for {System.Text.RegularExpressions.Regex.Escape(wanderer.name)} found nothing\\. {System.Text.RegularExpressions.Regex.Escape(village.name)} will look again in (\\d+) hours\\.";
					List<int> waits = File.Exists(mods)
						? System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(mods), pattern).Cast<System.Text.RegularExpressions.Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList()
						: new List<int>();
					bool spaced = waits.Count == 2 && waits[0] == 4 && waits[1] == 8;
					bool givenUp = PlusBridge.MissingState(wanderer) == "Lost" && ModsLogHas($"{village.name} has given up the search for {wanderer.name}.");
					return (spaced && givenUp, $"retry waits={string.Join(", ", waits)}h (polled {string.Join(", ", gaps.Select(g => g.ToString("F1")))}h) state={PlusBridge.MissingState(wanderer)} failed={PlusBridge.FailedSearches(wanderer)}");
				});
			}

			// 7. Records ride inside the real save. Whoever still has a record: the wanderer's is
			// gone if they were come across before any search failed.
			Character kept = new[] { wanderer, captive, victim }.Concat(village.residents)
				.Where(c => c != null && PlusBridge.MissingState(c) != null)
				// A search under way is the hardest to bring back (its quest must be found again).
				.OrderByDescending(c => PlusBridge.MissingState(c) == "Searching").FirstOrDefault();
			if (kept == null)
			{
				foreach (string name in new[] { "missing persons are stored inside the player's save file", "missing persons come back when the save loads" })
				{
					Skip(name, "nobody has a missing-person record left to save");
				}
				ResetMissingConfig();
				yield break;
			}
			string json = null;
			string entries = null;
			yield return SaveAndRead("ruinarch.plus.missing.json", (j, e) => { json = j; entries = e; });
			Check("missing persons are stored inside the player's save file", () =>
				(json != null && json.Contains(kept.persistentID), json == null ? "entries: " + entries : $"{json.Length} bytes, {kept.name} ({PlusBridge.MissingState(kept)})"));
			if (json != null)
			{
				string before = PlusBridge.MissingState(kept);
				// A real load registers every saved quest in the game's quest database before the
				// mod's data is read; a live world has none registered.
				PartyQuestDatabase quests = DatabaseManager.Instance.partyQuestDatabase;
				PartyQuest liveSearch = PlusBridge.MissingSearch(kept);
				bool registered = liveSearch != null && !quests.allPartyQuests.ContainsKey(liveSearch.persistentID);
				if (registered) quests.AddPartyQuest(liveSearch);
				PlusBridge.ClearMissing();
				ReplayLoad("ruinarch.plus.missing.json", json);
				Check("missing persons come back when the save loads", () =>
					(before != null && PlusBridge.MissingState(kept) == before, $"{kept.name}: before={before ?? "none"} after load={PlusBridge.MissingState(kept) ?? "none"}"));
				if (registered) quests.RemovePartyQuest(liveSearch);
			}

			ResetMissingConfig();
		}

		private int _partyShortages;

		private static void ResetMissingConfig()
		{
			PlusBridge.SetConfig("missingAfterHours", 24);
			PlusBridge.SetConfig("searchSweepHours", 6);
			PlusBridge.SetConfig("searchRetryHours", 24);
			PlusBridge.SetConfig("searchMaxAttempts", 3);
		}

		// A demonic structure at a spot the portal's own placement rules approve, well away
		// from the portal (so seeing one is not seeing the other) and near the village.
		private static LocationStructure PlaceDemonicStructure(LocationStructure portal, NPCSettlement village, params STRUCTURE_TYPE[] types)
		{
			LocationGridTile portalTile = portal.tiles.First();
			LocationGridTile villageTile = village.areas[0].gridTileComponent.centerGridTile;
			IEnumerable<Area> spots = GridMap.Instance.mainRegion.areas
				.Where(a => a.gridTileComponent.centerGridTile != null && !a.HasSettlementOnArea() && !a.IsNextToOrPartOfVillage()
					&& a.gridTileComponent.centerGridTile.GetDistanceTo(portalTile) >= 25f)
				.OrderBy(a => a.gridTileComponent.centerGridTile.GetDistanceTo(villageTile));
			return PlaceDemonicStructureIn(spots, portal, types.Length > 0 ? types : new[] { STRUCTURE_TYPE.KENNEL, STRUCTURE_TYPE.WATCHER, STRUCTURE_TYPE.SPIRE }, obeyPlacementRules: true);
		}

		// A demonic structure in an area bordering the village (the player may not build there;
		// the test places it anyway, as a world where the village grew up to it).
		private static LocationStructure PlaceDemonicStructureNextTo(LocationStructure portal, NPCSettlement village)
		{
			IEnumerable<Area> spots = village.areas.SelectMany(a => a.neighbourComponent.neighbours).Distinct()
				.Where(a => a.gridTileComponent.centerGridTile != null && !a.HasSettlementOnArea() && !a.HasPlayerSettlement()
					&& a.gridTileComponent.centerGridTile.GetDistanceTo(portal.tiles.First()) >= 25f);
			return PlaceDemonicStructureIn(spots, portal, new[] { STRUCTURE_TYPE.WATCHER, STRUCTURE_TYPE.SPIRE, STRUCTURE_TYPE.KENNEL }, obeyPlacementRules: false);
		}

		private static LocationStructure PlaceDemonicStructureIn(IEnumerable<Area> spots, LocationStructure portal, STRUCTURE_TYPE[] types, bool obeyPlacementRules)
		{
			List<Area> areas = spots.ToList();
			foreach (STRUCTURE_TYPE type in types)
			{
				LocationStructureObject prefab;
				try
				{
					prefab = InnerMapManager.Instance.GetStructurePrefabsForStructure(FACTION_TYPE.Demons, type, RESOURCE.NONE).First().GetComponent<LocationStructureObject>();
				}
				catch
				{
					continue;
				}
				foreach (Area area in areas)
				{
					LocationGridTile tile = area.gridTileComponent.centerGridTile;
					if (prefab.HasEnoughSpaceIfPlacedOn(tile, out string _) && (!obeyPlacementRules || area.structureComponent.CanBuildDemonicStructureHere(type, out string _)))
					{
						tile.InstantPlaceDemonicStructure(new StructureSetting(type, RESOURCE.NONE));
						if (tile.structure is DemonicStructure placed && placed != portal)
						{
							return placed;
						}
					}
				}
			}
			return null;
		}

		private static bool HasRoomFor(NPCSettlement village, STRUCTURE_TYPE type)
		{
			if (village.owner == null)
			{
				return false;
			}
			StructureSetting setting = village.owner.factionType.CreateStructureSettingForStructure(type, village);
			return setting.hasValue && LandmarkManager.Instance.CanPlaceStructureBlueprint(village.owner.factionType.type, village, setting,
				out LocationGridTile _, out string _, out int _, out LocationGridTile _);
		}

		// Build a vanilla structure instantly at a spot the game's own placement approves.
		private static LocationStructure InstantBuildVanilla(NPCSettlement village, STRUCTURE_TYPE type)
		{
			if (village.owner == null)
			{
				return null;
			}
			StructureSetting setting = village.owner.factionType.CreateStructureSettingForStructure(type, village);
			if (!setting.hasValue || !LandmarkManager.Instance.CanPlaceStructureBlueprint(village.owner.factionType.type, village, setting,
				out LocationGridTile tile, out string prefab, out int _, out LocationGridTile _))
			{
				return null;
			}
			return tile.tileObjectComponent.genericTileObject.InstantPlaceStructure(prefab, village);
		}

		// Villager parties accept quests before dawn (Party.InitialScheduleToCheckQuest, 5-7 am)
		// and set out when that day's Work shift starts; a party that accepts after the shift
		// began never leaves. So call this at 5 am, as the game would. Villagers sitting in an
		// idle party (no quest) are free to join. Null (logged) if fewer than `min` are free.
		private Party FormPartyFor(PartyQuest quest, NPCSettlement village, int min, int max)
		{
			List<Character> members = village.residents.Where(r => r != null && !r.isDead && r.marker != null
				&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive)
				&& r != village.ruler && r.limiterComponent.canMove && !PlusBridge.IsChild(r)).Take(max).ToList();
			if (members.Count < min)
			{
				Log($"  only {members.Count} free resident(s) for a party; {quest.partyQuestType} needs {min}");
				_partyShortages++;
				return null;
			}
			foreach (Character m in members.Where(m => m.partyComponent.hasParty).ToList())
			{
				Guard("leave idle party", () => { m.partyComponent.currentParty.RemoveMember(m); return m; });
			}
			Party formed = Guard("form a party", () =>
			{
				Party p = PartyManager.Instance.CreateNewParty(members[0]);
				foreach (Character m in members.Skip(1))
				{
					p.AddMember(m);
				}
				p.TryAcceptQuest(quest, members[0]);
				foreach (Character m in members)
				{
					p.AddMemberThatJoinedQuest(m);
				}
				return p;
			});
			Log($"  formed a party of {members.Count} for {quest.GetPartyQuestName()}: {string.Join(", ", members.Select(m => m.name))}");
			return formed;
		}

		// Saves the game for real (paused, as the game always saves), hands back the named
		// mod-data entry from the save zip (null if missing) and the zip's entry list, and
		// deletes the save.
		private IEnumerator SaveAndRead(string entry, Action<string, string> got)
		{
			const string saveName = "RuinarchPlus-autotest";
			string zip = Path.Combine(UtilityScripts.Utilities.gameSavePath, saveName + ".zip");
			SaveCurrentProgressManager saver = SaveManager.Instance.saveCurrentProgressManager;
			Try("delete an old test save", () => { if (File.Exists(zip)) File.Delete(zip); });
			// Saving a running world races its save threads against live objects (a job freed
			// mid-save threw inside SaveDataJobQueueItem.Save and left "Saving your progress..."
			// up forever), so pause and lock the speed controls, as the game's own autosave does:
			// otherwise a key press or a speed button resumes the world mid-save.
			Try("pause for the save", () =>
			{
				UIManager.Instance.Pause();
				UIManager.Instance.SetSpeedTogglesState(false);
			});
			_saving = true;
			Try("save the game", () => saver.DoManualSave(saveName));
			yield return WaitReal(() => File.Exists(zip) && !saver.isSaving && !saver.isWritingToDisk, 180f, "the test save to be written");
			_saving = false;
			Try("resume after the save", () =>
			{
				UIManager.Instance.SetSpeedTogglesState(true);
				UIManager.Instance.Unpause();
				UIManager.Instance.SetProgressionSpeed4X();
				Time.timeScale = TimeScale;
			});
			string json = null;
			string entries = "";
			Try("read the test save", () =>
			{
				using (ZipArchive archive = ZipFile.OpenRead(zip))
				{
					entries = string.Join(", ", archive.Entries.Select(e => e.FullName));
					ZipArchiveEntry found = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(entry));
					if (found != null)
					{
						using (StreamReader r = new StreamReader(found.Open()))
						{
							json = r.ReadToEnd();
						}
					}
				}
			});
			Try("delete the test save", () => { if (File.Exists(zip)) File.Delete(zip); });
			got(json, entries);
		}

		// Every villager death of the run by cause and killer, logged at the end: when the world
		// empties the test villages, this says what did it (the game's own dangers, or a mod).
		private static readonly Dictionary<string, int> Deaths = new Dictionary<string, int>();

		[HarmonyPatch(typeof(Character), nameof(Character.Death))]
		internal static class DeathTally
		{
			private static void Prefix(Character __instance, out bool __state) => __state = __instance.isDead;

			private static void Postfix(Character __instance, bool __state, string cause, Character responsibleCharacter, Interrupts.Interrupt interrupt)
			{
				if (_running == null || __state || !__instance.isDead || !__instance.isNormalCharacter || !__instance.race.IsSapient())
				{
					return;
				}
				string by = responsibleCharacter == null ? "" : $" by {(responsibleCharacter.isNormalCharacter ? responsibleCharacter.characterClass.className : responsibleCharacter.race.ToString())}";
				string key = cause + by + (interrupt != null ? $" ({interrupt.name})" : "") + (__instance.traitContainer.HasTrait("Plagued") ? " [plagued]" : "");
				Deaths.TryGetValue(key, out int n);
				Deaths[key] = n + 1;
			}
		}

		// A harness save in progress: anything resuming the world now is logged with its caller.
		private static bool _saving;

		[HarmonyPatch(typeof(GameManager), nameof(GameManager.SetPausedState))]
		internal static class SaveUnpauseWatch
		{
			private static void Prefix(bool isPaused)
			{
				if (_saving && !isPaused)
				{
					_running?.Log("  the world was resumed during the test save by: " + Environment.StackTrace.Replace("\n", " | "));
				}
			}
		}

		// Replays loading: stages what a real save holds right now (every ModSave handler's
		// data, through the loader's own writer), puts the entry under test over it, then runs
		// the game's own "save finished loading" step. (Staging only the entry under test gave
		// every other handler load(null), which wiped their state mid-run.)
		private void ReplayLoad(string entry, string json)
		{
			string dir = Path.Combine(UtilityScripts.Utilities.tempPath, "ModData");
			Try("stage the save data", () =>
			{
				AccessTools.Method(AccessTools.TypeByName("Ruinarch.ModContent.ModSave"), "WriteAll").Invoke(null, new object[] { UtilityScripts.Utilities.tempPath });
				Directory.CreateDirectory(dir);
				File.WriteAllText(Path.Combine(dir, entry), json);
			});
			Try("run the game's load-finished step", () => SaveManager.Instance.DeleteSaveFilesInTempDirectory());
			Try("clean up staged data", () => { if (Directory.Exists(dir)) Directory.Delete(dir, true); });
		}

		private bool ModsLogHas(string text)
		{
			string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
			return File.Exists(mods) && File.ReadAllText(mods).Contains(text);
		}

		private T Guard<T>(string what, Func<T> f) where T : class
		{
			try
			{
				return f();
			}
			catch (Exception e)
			{
				Fail(what, e.ToString());
				return null;
			}
		}

		private static void PlacePortal()
		{
			PickPortalInputModule module = PlayerManager.pickPortalInputModule;
			LocationStructureObject portal = InnerMapManager.Instance
				.GetStructurePrefabsForStructure(FACTION_TYPE.Demons, STRUCTURE_TYPE.THE_PORTAL, RESOURCE.NONE)
				.First().GetComponent<LocationStructureObject>();
			// As far from every settlement as the rules allow: nobody defends the Portal in an
			// unattended run, and a Portal that villagers stumble on early gets destroyed (the
			// world ends in defeat mid-run).
			List<LocationGridTile> settlementCentres = GridMap.Instance.mainRegion.settlementsInRegion
				.SelectMany(s => s.areas).Select(a => a.gridTileComponent.centerGridTile).Where(t => t != null).ToList();
			foreach (Area area in GridMap.Instance.mainRegion.areas.Where(a => a.gridTileComponent.centerGridTile != null)
				.OrderByDescending(a => settlementCentres.Count == 0 ? 0f : settlementCentres.Min(t => t.GetDistanceTo(a.gridTileComponent.centerGridTile))))
			{
				LocationGridTile tile = area.gridTileComponent.centerGridTile;
				if (tile != null && portal.HasEnoughSpaceIfPlacedOn(tile, out string _)
					&& area.structureComponent.CanBuildDemonicStructureHere(STRUCTURE_TYPE.THE_PORTAL, out string _))
				{
					AccessTools.Method(typeof(PickPortalInputModule), "PlacePortal").Invoke(module, new object[] { tile });
					return;
				}
			}
			throw new Exception("no valid portal tile in any area");
		}

		// ---------------------------------------------------------------------------------
		private IEnumerator WaitReal(Func<bool> done, float timeoutSeconds, string what)
		{
			float deadline = Time.realtimeSinceStartup + timeoutSeconds;
			while (Time.realtimeSinceStartup < deadline)
			{
				bool ok = false;
				try
				{
					ok = done();
				}
				catch
				{
				}
				if (ok)
				{
					yield break;
				}
				yield return new WaitForSecondsRealtime(0.5f);
			}
			Log($"timeout waiting for {what} after {timeoutSeconds}s");
		}

		// Waits until `hours` of game time pass, or `done` returns true.
		private IEnumerator WaitGameHours(float hours, Func<bool> done)
		{
			float target = GameHours + hours;
			float realDeadline = Time.realtimeSinceStartup + hours * 60f + 60f;
			while (GameHours < target && Time.realtimeSinceStartup < realDeadline)
			{
				if (done != null)
				{
					bool ok = false;
					try
					{
						ok = done();
					}
					catch
					{
					}
					if (ok)
					{
						yield break;
					}
				}
				yield return new WaitForSecondsRealtime(0.25f);
			}
		}

		private bool Try(string what, Action a)
		{
			try
			{
				a();
				Log("step ok: " + what);
				return true;
			}
			catch (Exception e)
			{
				Fail(what, e.ToString());
				Finish("aborted at " + what);
				return false;
			}
		}

		private void Check(string name, Func<(bool ok, string detail)> check)
		{
			try
			{
				(bool ok, string detail) = check();
				if (ok)
				{
					_pass++;
					Log($"PASS {name} :: {detail}");
				}
				else
				{
					Fail(name, detail);
				}
			}
			catch (Exception e)
			{
				Fail(name, "threw " + e);
			}
		}

		private void Fail(string name, string detail)
		{
			_fail++;
			Log($"FAIL {name} :: {detail}");
		}

		private void Skip(string name, string why)
		{
			_skip++;
			Log($"SKIP {name} :: {why}");
		}

		private void Finish(string reason)
		{
			if (Deaths.Count > 0)
			{
				Log($"deaths of villagers during the run ({Deaths.Values.Sum()}): " + string.Join(", ", Deaths.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}x {kv.Key}")));
			}
			Log($"AUTOTEST DONE ({reason}) pass={_pass} fail={_fail} skip={_skip} gameHours={GameHours:F1}");
			Time.timeScale = 1f;
			StopAllCoroutines();
			Application.Quit();
		}

		private void Log(string line)
		{
			Debug.Log("[autotest] " + line);
			Append(line);
		}

		private void Append(string line)
		{
			try
			{
				File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
			}
			catch
			{
			}
		}

		// The world ending (the Portal destroyed, or a win) stops the game clock, so every wait
		// would sit out the run's timeout with no word of why. End the run with the game's own
		// message instead.
		[HarmonyPatch(typeof(PlayerUI), nameof(PlayerUI.LoseGameOver))]
		internal static class GameLost
		{
			private static void Prefix(string p_gameOverMessage) => _running?.Finish("world ended in defeat: " + p_gameOverMessage);
		}

		[HarmonyPatch(typeof(PlayerUI), nameof(PlayerUI.WinGameOver))]
		internal static class GameWon
		{
			private static void Prefix(string winMessage) => _running?.Finish("world ended in victory: " + winMessage);
		}

		// Who brought the Portal down, for a run that ends in defeat.
		[HarmonyPatch(typeof(ThePortal), "DestroyStructure")]
		internal static class PortalDestroyed
		{
			private static void Prefix(ThePortal __instance, Character p_responsibleCharacter, bool isPlayerSource)
			{
				try
				{
					string who = p_responsibleCharacter == null ? "nobody named"
						: $"{p_responsibleCharacter.name} of {p_responsibleCharacter.faction?.name ?? "no faction"} (quest {p_responsibleCharacter.partyComponent.currentParty?.currentQuest?.GetType().Name ?? "none"})";
					string attackers = string.Join(", ", __instance.currentAttackers.Where(a => a != null)
						.Select(a => $"{a.name}/{a.faction?.name}/{a.partyComponent.currentParty?.currentQuest?.GetType().Name ?? "-"}"));
					_running?.Log($"the Portal is destroyed by {who}, playerSource={isPlayerSource}; attackers: [{attackers}]\n{Environment.StackTrace}");
				}
				catch
				{
				}
			}
		}
	}
}
