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
	public partial class AutoTest : MonoBehaviour
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
				// The player's demons roam the player settlement's areas and fight whoever they
				// see there: a test building left standing near a village costs villagers.
				PlayerSettlement ps = PlayerManager.Instance?.player?.playerSettlement;
				if (ps != null)
				{
					ThePortal portal = ps.allStructures.OfType<ThePortal>().FirstOrDefault();
					Log($"  before {suite}: player buildings [{string.Join(", ", ps.allStructures.Select(s => $"{s.structureType}{(s.hasBeenDestroyed ? " (destroyed)" : "")} at {s.GetCenterTile()?.localPlace}"))}]"
						+ $"; areas [{string.Join(", ", ps.areas.Select(a => a.gridTileComponent.centerGridTile?.localPlace.ToString()))}]"
						+ $"; portal hp {portal?.currentHP}/{portal?.maxHP}{PortalDamaged.TakeTally()}");
				}
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
			SnapshotSettings();
			StartCoroutine(Run());
		}

		private void OnDestroy()
		{
			Application.logMessageReceived -= OnGameLog;
		}

		// Every way out of a run that does not pass through Finish still puts the files back.
		private void OnApplicationQuit() => RestoreSettings();

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
			if (Runs("ModSettingsSuite")) { yield return Safe("ModSettingsSuite (main menu)", ModsTabChecks("menu")); }
			if (Runs("SpiritEnergyCostSuite"))
			{
				yield return Safe("SpiritEnergyCostSuite", SpiritEnergyCostSuite());
				if (_only.Count == 1) { Finish("done"); yield break; }
			}
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
				// Cross-faction gossip needs two factions at startup, not a later secession.
				if (Runs("FogSuite") && data.factionSettings.factionTemplates.Count < 2)
					data.factionSettings.AddFactionSetting(1);
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
			// own checks. Meant for this run only: any check that saves Ruinarch+ settings writes
			// these values into Mods/settings/ruinarch.plus.json too, so the harness puts every
			// settings file back at the end of the run (RestoreSettings).
			PlusBridge.SetConfig("corpseDiseaseEnabled", false);
			PlusBridge.SetConfig("lifeCycleEnabled", false);
			// Unrest neither: the harness's own killings and wrecking would set villages rising
			// against their rulers mid-test. UnrestSuite turns it on for its own checks.
			PlusBridge.SetConfig("unrestEnabled", false);
			yield return WaitGameHours(1f, null);

			FreshWorldChecks();
			// Early, while villagers are out walking; leaves the camera as it found it.
			if (Runs("StockDiscordSuite")) { yield return Safe("StockDiscordSuite", StockDiscordSuite()); }
			if (Runs("StockWorldRulesSuite")) { yield return Safe("StockWorldRulesSuite", StockWorldRulesSuite()); }
			if (Runs("StockWitnessSuite")) { yield return Safe("StockWitnessSuite", StockWitnessSuite()); }
			if (Runs("StockFactionSuite")) yield return Safe("StockFactionSuite", StockFactionSuite());
			if (Runs("StockSecuritySuite")) yield return Safe("StockSecuritySuite", StockSecuritySuite());
			if (Runs("CultistRemovalSuite")) { yield return Safe("CultistRemovalSuite", CultistRemovalSuite()); }
			if (Runs("FrozenVigilantSuite")) { yield return Safe("FrozenVigilantSuite", FrozenVigilantSuite()); }
			if (Runs("HarpyDragonSuite")) { yield return Safe("HarpyDragonSuite", HarpyDragonSuite()); }
			if (Runs("TableCannibalSuite")) { yield return Safe("TableCannibalSuite", TableCannibalSuite()); }
			if (Runs("ExileJurisdictionSuite")) { yield return Safe("ExileJurisdictionSuite", ExileJurisdictionSuite()); }
			if (Runs("CurfewVisitSuite")) yield return Safe("CurfewVisitSuite", CurfewVisitSuite());
			if (Runs("PerformanceSuite")) { yield return Safe("PerformanceSuite", PerformanceSuite()); }
			if (Runs("ModSettingsSuite")) { yield return Safe("ModSettingsSuite", ModSettingsSuite()); }
			if (Runs("TemplateSuite")) { yield return Safe("TemplateSuite", TemplateSuite()); }
			if (Runs("BlightSuite")) { yield return Safe("BlightSuite", BlightSuite()); }
			if (Runs("FireWallTest")) { yield return Safe("FireWallTest", FireWallTest()); }
			if (Runs("PathLineTest")) { yield return Safe("PathLineTest", PathLineTest()); }
			if (Runs("ExploitSuite")) { yield return Safe("ExploitSuite", ExploitSuite()); }
			if (Runs("ExplosionTest")) { yield return Safe("ExplosionTest", ExplosionTest()); }
			// Needs a village with room for a Tavern-sized building; takes the fullest one.
			// First, while the villages still have free space (later Mass Graves and
			// Cemeteries fill it).
			if (Runs("TierSuite")) { yield return Safe("TierSuite", TierSuite()); }
			if (Runs("LifeSuite")) { yield return Safe("LifeSuite", LifeSuite()); }
			// Needs three free villagers of one village, so it runs while the villages are
			// full. Strands them in the wilderness; one never comes back.
			if (Runs("MissingPersonsSuite"))
			{
				object decayEnabled = PlusBridge.Config("corpseDecayEnabled");
				// Keep the discovery fixture's body available even when party formation is delayed.
				PlusBridge.SetConfig("corpseDecayEnabled", false);
				try { yield return Safe("MissingPersonsSuite", MissingPersonsSuite()); }
				finally
				{
					if (decayEnabled != null) PlusBridge.SetConfig("corpseDecayEnabled", decayEnabled);
				}
			}
			if (Runs("MassGraveSuite")) { yield return Safe("MassGraveSuite", MassGraveSuite()); }
			// Knowledge takes the village with the most people left (the one the burial tests
			// spared).
			if (Runs("KnowledgeSuite")) { yield return Safe("KnowledgeSuite", KnowledgeSuite()); }
			if (Runs("FogSuite")) { yield return Safe("FogSuite", FogSuite()); }
			// Records: home Books and a Library; forgets everything it teaches at the end.
			if (Runs("RecordsSuite")) { yield return Safe("RecordsSuite", RecordsSuite()); }
			// Exercise all decay stages at the supported minimum, then restore player config.
			if (Runs("DecayTest"))
			{
				object decayDays = PlusBridge.Config("corpseDecayDays");
				PlusBridge.SetConfig("corpseDecayDays", 0.25f);
				try
				{
					yield return Safe("DecayTest", DecayTest());
				}
				finally
				{
					if (decayDays != null) PlusBridge.SetConfig("corpseDecayDays", decayDays);
				}
			}
			// Starves one village until famine, then feeds it; some villagers move away.
			if (Runs("FamineSuite")) { yield return Safe("FamineSuite", FamineSuite()); }
			// Kills a villager, wrecks a building and sets the village against its ruler twice.
			if (Runs("UnrestSuite")) { yield return Safe("UnrestSuite", UnrestSuite()); }
			if (Runs("UprisingKindsSuite")) { yield return Safe("UprisingKindsSuite", UprisingKindsSuite()); }
			if (Runs("WatchSuite")) { yield return Safe("WatchSuite", WatchSuite()); }
			if (Runs("HuntSuite")) { yield return Safe("HuntSuite", HuntSuite()); }
			if (Runs("TradeSuite")) { yield return Safe("TradeSuite", TradeSuite()); }
			// After the burial and knowledge tests: a plague answered with Exile makes the
			// plagued Criminals, and Criminals take no village jobs (burial, construction).
			if (Runs("CurfewSuite")) { yield return Safe("CurfewSuite", CurfewSuite()); }
			// Last: it wipes a village out, which can end the world (player victory, and the
			// game repopulating empty villages with new factions).
			if (Runs("MigrationSuite")) { yield return Safe("MigrationSuite", MigrationSuite()); }
			// A measurement that burns a village down: only when asked for by name.
			if (_only.Contains("FireProbe")) { yield return Safe("FireProbe", FireProbe()); }
			// Kills everyone in a capital: only when asked for by name.
			if (_only.Contains("CapitalLossSuite")) { yield return Safe("CapitalLossSuite", CapitalLossSuite()); }
			// A survey of the world's Book Shelves: only when asked for by name.
			if (_only.Contains("ShelfProbe")) { yield return Safe("ShelfProbe", ShelfProbe()); }
			// Replaces the world with a reload of its save: only when asked for by name.
			if (_only.Contains("PerformanceReloadSuite")) { yield return Safe("PerformanceReloadSuite", PerformanceReloadSuite()); }
			if (_only.Contains("CorpseReloadSuite")) yield return Safe("CorpseReloadSuite", CorpseReloadSuite());

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
			// The game's save serializer writes enums by name; a virtual value has none, so a
			// saved Library or Heart came back as type 0 and the whole load stalled.
			Check("a virtual structure type survives the game's save serializer", () =>
			{
				var saved = new SaveDataManMadeStructure { structureType = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.library") };
				var serializer = new FullSerializer.fsSerializer();
				serializer.TrySerialize(saved, out FullSerializer.fsData data).AssertSuccessWithoutWarnings();
				SaveDataManMadeStructure loaded = null;
				serializer.TryDeserialize(FullSerializer.fsJsonParser.Parse(FullSerializer.fsJsonPrinter.CompressedJson(data)), ref loaded);
				return (loaded != null && loaded.structureType == saved.structureType && (int)saved.structureType != 0,
					$"saved {(int)saved.structureType} as {data.AsDictionary["structureType"]}, loaded {(int?)loaded?.structureType}");
			});
			// Saves from before that fix hold type 0; the framework finds the building again by
			// its saved name ("<noun> <type>", or type first in some languages).
			Check("a structure saved without its type is recognised by its name", () =>
			{
				var find = AccessTools.Method(AccessTools.TypeByName("Ruinarch.ModContent.StructureNames"), "FromSavedName");
				string Id(string name) => (find.Invoke(null, new object[] { name }) as Ruinarch.ModContent.StructureRegistration)?.Id ?? "none";
				var cases = new[] { ("Painted Library", "ruinarch.plus.library"), ("Library Painted", "ruinarch.plus.library"),
					("Old Town Hall", "ruinarch.plus.town_hall"), ("Quiet Mass Grave", "ruinarch.plus.mass_grave"), ("Painted Workshop", "none") };
				var wrong = cases.Where(c => Id(c.Item1) != c.Item2).Select(c => $"{c.Item1} -> {Id(c.Item1)}").ToList();
				return (wrong.Count == 0, wrong.Count == 0 ? $"{cases.Length} names" : string.Join(", ", wrong));
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
				// A faction becoming aware while the game is paused (ticks stop; the game may pause
				// on its own alert) shows in the section at once. Made unaware again, its alert goes.
				Faction f = villages.Select(v => v.owner).FirstOrDefault(o => o != null && o.isMajorNonPlayer);
				if (f != null)
				{
					bool wasPaused = GameManager.Instance.isPaused;
					GameManager.Instance.SetPausedState(true);
					f.SetIsAwareOfPlayer(true);
					List<string> aware = PlusBridge.KnowledgePanelLines() ?? new List<string>();
					f.SetIsAwareOfPlayer(false);
					List<string> after = PlusBridge.KnowledgePanelLines() ?? new List<string>();
					int alerts = PlayerManager.Instance.player.bookmarkComponent.bookmarkedObjects.TryGetValue(BOOKMARK_CATEGORY.Alerts, out BookmarkCategory cat)
						? cat.bookmarked.OfType<Quests.Alerts.FactionAwareAlert>().Count(a => a.factionName == f.name) : 0;
					GameManager.Instance.SetPausedState(wasPaused);
					Check("a faction becoming aware shows in the panel at once, even paused", () =>
						(aware.Any(l => l.Contains(f.name) && l.Contains("know of you")) && after.Count == 1 && after[0] == "Your presence in the region is not known.",
						$"aware: {string.Join(" / ", aware)}; unaware again: {string.Join(" / ", after)}"));
					Check("the harness takes back the 'is now aware' alert when it makes a faction unaware", () => (alerts == 0, $"{alerts} alert(s) left for {f.name}"));
				}
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
				.Where(v => v.residents.Any(r => r != null && !r.isDead && r.hasMarker && r.gridTileLocation != null
					&& r.isNormalCharacter && r.race.IsSapient() && r != v.ruler
					&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive)))
				.OrderByDescending(v => HasRoomFor(v, STRUCTURE_TYPE.CEMETERY))
				// The no-scatter tests kill two of its people; the rest must still build.
				.ThenByDescending(v => v.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient())).FirstOrDefault();
			NPCSettlement withCemetery = villages.FirstOrDefault(v => v.HasStructure(STRUCTURE_TYPE.CEMETERY));

			if (bare == null)
			{
				Skip("no-graveyard village tests", "no village without a graveyard or pending construction has an eligible resident");
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
			// has one (and no pit yet) still calls for a Mass Grave. Not in a village with a
			// blueprint job already: the game allows one at a time and the pit waits its turn.
			NPCSettlement graveyardOnly = villages.FirstOrDefault(v => v.owner != null && PitCount(v) == 0 && !PlusBridge.HasPendingBlueprint(v)
				&& (v.HasStructure(STRUCTURE_TYPE.CEMETERY) || v.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE))
				&& !v.HasStructure(STRUCTURE_TYPE.HUNTER_LODGE) && HasRoomFor(v, STRUCTURE_TYPE.CEMETERY) && !v.HasJob(JOB_TYPE.PLACE_BLUEPRINT));
			if (graveyardOnly == null)
			{
				Skip("a village with a Cemetery still plans a Mass Grave for a creature's carcass", "no village with a graveyard, no pit, no blueprint job and room for one");
			}
			else
			{
				Guard("kill a creature in a village with a graveyard", () => SpawnAndKill(graveyardOnly, CarcassFor(graveyardOnly)));
				string planned = $"{graveyardOnly.name} has dead nobody will bury: queued a Mass Grave blueprint";
				bool waited = false;
				yield return WaitGameHours(6f, () =>
				{
					waited |= graveyardOnly.HasJob(JOB_TYPE.PLACE_BLUEPRINT);
					return ModsLogHas(planned) || PitCount(graveyardOnly) > 0;
				});
				if (!ModsLogHas(planned) && PitCount(graveyardOnly) == 0 && waited)
				{
					Skip("a village with a Cemetery still plans a Mass Grave for a creature's carcass", $"{graveyardOnly.name} got a blueprint job of the game's own meanwhile; the pit waits its turn");
				}
				else
				{
					Check("a village with a Cemetery still plans a Mass Grave for a creature's carcass", () =>
						(ModsLogHas(planned) || PitCount(graveyardOnly) > 0, $"{Describe(graveyardOnly)} queued={ModsLogHas(planned)} pits={PitCount(graveyardOnly)} placeBlueprint job={graveyardOnly.HasJob(JOB_TYPE.PLACE_BLUEPRINT)}"));
				}
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
			// Not one with a Mass Grave blueprint waiting: the instant build hands back the pit
			// being built there (none yet) rather than place a second.
			NPCSettlement withoutPit = villages.Where(v => PitCount(v) == 0 && !PlusBridge.HasPendingBlueprint(v)).OrderByDescending(v => HasRoomFor(v, STRUCTURE_TYPE.CEMETERY)).FirstOrDefault();
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
			else
			{
				Skip("instant build creates a real Mass Grave in the village", "every village has a pit or a Mass Grave blueprint waiting");
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

		// Scattering is a tombstone outside any graveyard (the wilderness, a City Center). A
		// body left lying, laid in the Mass Grave, or buried in a real Cemetery or Cult Temple
		// (a passer-by from a village with one takes an outsider home: vanilla) is not.
		private static bool NotScattered(Character corpse)
		{
			LocationStructure where = corpse.grave?.gridTileLocation?.structure;
			return corpse.grave == null || PlusBridge.IsMassGrave(where)
				|| where?.structureType == STRUCTURE_TYPE.CEMETERY || where?.structureType == STRUCTURE_TYPE.CULT_TEMPLE;
		}

		private static string GraveWhere(Character corpse)
		{
			LocationStructure where = corpse.grave?.gridTileLocation?.structure;
			return $"grave={(corpse.grave != null)} structure={where?.structureType} of {where?.settlementLocation?.name ?? "no settlement"}";
		}

		private IEnumerator BareVillageTests(NPCSettlement village)
		{
			Log($"test village (no graveyard): {Describe(village)}");

			// 1. A death with no graveyard and no pit: the corpse stays where it fell.
			int burialLogStart = ModsLogLength();
			Character first = Guard("kill resident", () => KillResident(village));
			if (first == null)
			{
				Fail("no-scatter", "could not find a resident to kill");
				yield break;
			}
			yield return WaitGameHours(3f, null);
			Check("no-scatter: corpse lies where it fell (no wilderness tombstone)", () =>
				((first.grave == null && first.hasMarker) || PlusBridge.IsMassGrave(first.grave?.gridTileLocation?.structure)
					|| (!first.hasMarker && first.grave == null && ModsLogHasSince(burialLogStart, $"Mass Grave: {first.name} laid in the pit by a villager")),
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
				// Quest members use a different native trigger. It must obey the same
				// no-scatter policy as a villager walking past this body alone.
				Character partyBurier = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker
					&& r.isNormalCharacter && r.race.IsSapient() && !PlusBridge.IsChild(r) && r.limiterComponent.canMove && r.limiterComponent.canPerform);
				// The native action requires a Cemetery somewhere in the region, even
				// though an active-party burial ignores it and plants the stone here.
				if (village.region.GetRandomStructureOfType(STRUCTURE_TYPE.CEMETERY) == null)
				{
					NPCSettlement donor = Villages().FirstOrDefault(v => v != village && v.region == village.region && HasRoomFor(v, STRUCTURE_TYPE.CEMETERY));
					if (donor != null) Guard("build another village's Cemetery for the party burial", () => InstantBuildVanilla(donor, STRUCTURE_TYPE.CEMETERY));
				}
				if (village.region.GetRandomStructureOfType(STRUCTURE_TYPE.CEMETERY) == null) partyBurier = null;
				if (partyBurier != null)
				{
					Guard("queue a quest member's burial at the village border", () =>
					{
						partyBurier.CancelAllJobs();
						partyBurier.StopCurrentActionNode("autotest party burial");
						CharacterManager.Instance.Teleport(partyBurier, border.gridTileLocation);
						partyBurier.jobComponent.TriggerPersonalBuryInActivePartyJob(border);
						// Native party burial is idle-priority; normal village work would mask
						// the regression in this fixture. Let the real burial action run now.
						JobQueueItem burial = border.allJobsTargetingThis.FirstOrDefault(j => j.originalOwner == partyBurier
							&& (j.jobType == JOB_TYPE.BURY_IN_ACTIVE_PARTY || j.jobType == JOB_TYPE.BURY));
						burial?.SetPriority(1000);
						Log($"  party burial for {border.name}: job={burial?.jobType.ToString() ?? "none"} actor={partyBurier.name}");
						return partyBurier;
					});
				}
				if (partyBurier != null)
				{
					yield return WaitGameHours(6f, () => border.grave != null);
					Check("no-scatter at the village border (active-party burial path)", () =>
						(NotScattered(border), $"burier={partyBurier.name} {GraveWhere(border)}"));
				}
				else Skip("no-scatter at the village border (active-party burial path)", "no carrier or no regional Cemetery for the native action");
				yield return WaitGameHours(6f, () => border.grave != null);
				Check("no-scatter at the village border (personal burial path)", () => (NotScattered(border), GraveWhere(border)));
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
			else if (pit == null && village.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()) < 2)
			{
				Skip("villagers build a Mass Grave from materials", $"{village.name} is down to "
					+ $"{village.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient())} villager(s) after the no-scatter kills and the world's dangers: too few to gather and build");
			}
			else
			{
				Check("villagers build a Mass Grave from materials", () =>
					(pit != null, pit != null ? $"built after {GameHours - start:F1}h" : $"not built within 120h (blueprint seen={queued}, pending={PlusBridge.HasPendingBlueprint(village)}); "
						+ string.Join(", ", village.residents.Where(r => r != null && !r.isDead).Select(r => $"{r.name}[{r.currentJob?.jobType.ToString() ?? "-"}{(r.needsComponent.isStarving ? " starving" : "")}{(r.gridTileLocation != null && r.gridTileLocation.IsPartOfSettlement(village) ? "" : " away")}{(PlusBridge.IsHunting(r) ? " hunting" : "")}]"))));
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
				if (village.owner == null)
				{
					Skip("instant-build fallback", $"{village.name} died out while waiting (no owning faction; residents alive={village.residents.Count(r => r != null && !r.isDead)})");
					yield break;
				}
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
				Character residentBurier = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker
					&& r.isNormalCharacter && r.race.IsSapient() && !PlusBridge.IsChild(r) && r.limiterComponent.canMove && r.limiterComponent.canPerform);
				if (residentBurier != null)
				{
					Guard("send a quest member's burial to the built pit", () =>
					{
						second.ForceCancelAllJobsTargetingThisCharacter(JOB_TYPE.BURY);
						second.ForceCancelAllJobsTargetingThisCharacter(JOB_TYPE.BURY_IN_ACTIVE_PARTY);
						residentBurier.CancelAllJobs();
						residentBurier.StopCurrentActionNode("autotest party burial to pit");
						CharacterManager.Instance.Teleport(residentBurier, second.gridTileLocation);
						residentBurier.jobComponent.TriggerPersonalBuryInActivePartyJob(second);
						JobQueueItem burial = second.allJobsTargetingThis.FirstOrDefault(j => j.originalOwner == residentBurier
							&& (j.jobType == JOB_TYPE.BURY_IN_ACTIVE_PARTY || j.jobType == JOB_TYPE.BURY));
						burial?.SetPriority(1000);
						Log($"  party burial for {second.name}: job={burial?.jobType.ToString() ?? "none"} actor={residentBurier.name}");
						return residentBurier;
					});
				}
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

			// A small village can be left with nobody to carry bodies (these tests kill two of its
			// people, the world may take the rest, and the last one may have left to wander the
			// wilds): then only the fallback takes them, and hauling measures nothing. A carrier
			// is one of the village's people within reach of it.
			LocationGridTile centre = village.cityCenter?.tiles.FirstOrDefault();
			Func<bool> anyCarrier = () => village.residents.Any(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()
				&& r.limiterComponent.canMove && r.limiterComponent.canPerform && r.faction == village.owner
				&& centre != null && r.gridTileLocation != null && r.gridTileLocation.GetDistanceTo(centre) <= 30f);
			if (anyCarrier())
			{
				yield return HaulTests(village, first, pit, anyCarrier);
			}
			else
			{
				string left = string.Join(", ", village.residents.Where(r => r != null && !r.isDead).Select(r => $"{r.name} ({r.faction?.name ?? "no faction"}) at {r.gridTileLocation?.localPlace.ToString() ?? "-"}"));
				foreach (string name in new[] { "pre-existing corpse is laid in the pit once it exists", "villagers carry a creature carcass into the pit", "villagers fetch a carcass from the village's surroundings" })
				{
					Skip(name, $"nobody in or near {village.name} who can carry a body; residents: [{left}]");
				}
			}

			Check("the pit shows no gravestones", () =>
			{
				int stones = pit.tiles.Count(t => t.tileObjectComponent.objHere is Tombstone);
				return (stones == 0, $"{stones} tombstone(s) on the pit");
			});
			Log($"  pit bodyCount={PlusBridge.BodyCount(pit)} hauledTotal={PlusBridge.HauledTotal} absorbedTotal={PlusBridge.AbsorbedTotal}");
		}

		// Villagers take the corpse that lay before the pit existed, a carcass in the village and
		// one from the surroundings. A body still lying there once nobody of the village is left
		// around to carry it (<paramref name="anyCarrier"/>) measures nothing: skipped.
		private IEnumerator HaulTests(NPCSettlement village, Character first, LocationStructure pit, Func<bool> anyCarrier)
		{
			string nobody = $"nobody in or near {village.name} who can carry a body any more";
			// The corpse that was lying before the pit existed must be taken too.
			yield return WaitGameHours(24f, () => first.grave != null || !first.hasMarker);
			if (first.grave?.gridTileLocation?.structure?.structureType == STRUCTURE_TYPE.CEMETERY)
			{
				Skip("pre-existing corpse is laid in the pit once it exists", $"the game built a Cemetery in {village.name} meanwhile and the body was buried there");
			}
			else if (first.hasMarker && !anyCarrier())
			{
				Skip("pre-existing corpse is laid in the pit once it exists", nobody);
			}
			else Check("pre-existing corpse is laid in the pit once it exists", () =>
			{
				bool gone = !first.hasMarker && first.grave == null;
				return (gone, $"gone={gone} hasMarker={first.hasMarker} {GraveWhere(first, village)}");
			});

			// 4. A creature carcass in the village is disposed of in the pit.
			int hauledBeforeCreature = PlusBridge.HauledTotal;
			int absorbedBeforeCreature = PlusBridge.AbsorbedTotal;
			Character beast = Guard("spawn creature", () => SpawnAndKill(village, CarcassFor(village)));
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
				if (beast.hasMarker && !anyCarrier())
				{
					Skip("villagers carry a creature carcass into the pit", nobody);
				}
				else Check("villagers carry a creature carcass into the pit", () =>
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
				Character far = Guard("spawn creature outside", () => SpawnAndKillAt(outside, CarcassFor(village)));
				if (far != null && !(far.race.IsSkinnable() && village.HasStructureOfTypeThatIsAssigned(STRUCTURE_TYPE.HUNTER_LODGE)))
				{
					float d = outside.GetDistanceTo(pit.tiles.First());
					yield return WaitGameHours(48f, () => !far.hasMarker);
					if (far.hasMarker && !anyCarrier())
					{
						Skip("villagers fetch a carcass from the village's surroundings", nobody);
					}
					else Check("villagers fetch a carcass from the village's surroundings", () =>
						(!far.hasMarker && PlusBridge.HauledTotal > hauledBeforeOut,
						$"at {outside.localPlace} ({d:F0} tiles from the pit) hasMarker={far.hasMarker} hauledDelta={PlusBridge.HauledTotal - hauledBeforeOut} jobQueued={village.HasJob(JOB_TYPE.BURY, far)}"));
				}
			}
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
				if (baselineCount == 0 || count == 0)
				{
					Skip("under curfew, residents stay home in their free time", $"nobody the curfew binds was found at home or out in free time ({baselineCount} before the plague, {count} under curfew); nothing to measure");
				}
				else if (baseline >= 0.6f)
				{
					Skip("under curfew, residents stay home in their free time", $"{baseline:P0} of {baselineCount} were home before the plague already; nothing to measure (under curfew {under:P0})");
				}
				else
				{
					Check("under curfew, residents stay home in their free time", () =>
						(count > 0 && under > baseline && (under >= 0.6f || under - baseline >= 0.25f), $"home share {under:P0} of {count} (before plague {baseline:P0}); out now: "
						+ string.Join(", ", village.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && !r.isSettlementRuler && !r.isFactionLeader && r.homeStructure != null
							&& !r.traitContainer.HasTrait("Plagued", "Quarantined") && !r.isAtHomeStructure).Select(Busy))));
				}
				yield return CurfewRecordsTest(village);
				yield return ClosedBordersTest(village);
				Guard("end plague event", () => { village.eventManager.DeactivateEvent(plague); return plague; });
				Check("curfew lifts when the plague event ends", () =>
					(!PlusBridge.IsUnderCurfew(village), $"underCurfew={PlusBridge.IsUnderCurfew(village)}"));
			}
		}

		// Records under curfew: the records' free-time prefix runs before the curfew's, so a
		// villager kept home still writes there (and none goes to the Library).
		private IEnumerator CurfewRecordsTest(NPCSettlement village)
		{
			const string name = "under curfew, villagers kept home still write there";
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			Character teller = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !r.isAlliedWithPlayer);
			if (!PlusBridge.RecordsAvailable || !(PlusBridge.Config("recordsEnabled") is bool on) || !on || portal == null || teller == null)
			{
				Skip(name, !PlusBridge.RecordsAvailable ? "RuinarchPlus (with records) not loaded" : portal == null ? "no portal" : "no villager who can remember");
				yield break;
			}
			Faction faction = village.owner;
			PlusBridge.ForgetRecords(faction);
			Guard("teach the village", () => { PlusBridge.RememberAtHome(teller, portal); return teller; });
			int mark = ModsLogLength();
			Func<List<LocationStructure>> written = () => (village.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> ds) ? ds : new List<LocationStructure>())
				.Where(d => PlusBridge.RecordOf(d)?.Contains(portal) == true).ToList();
			yield return WaitGameHours(30f, () => written().Count > 0 || !PlusBridge.IsUnderCurfew(village));
			Check(name, () =>
				(PlusBridge.IsUnderCurfew(village) && written().Count > 0 && ModsLogHasSince(mark, $"wrote of your {portal.name}"),
				$"underCurfew={PlusBridge.IsUnderCurfew(village)} homes with the record: {string.Join(", ", written().Select(d => d.name))}"));
			PlusBridge.Forget(faction);
			PlusBridge.ForgetRecords(faction);
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
				return (vanilla > 0 && mod == (int)(vanilla * health), $"vanilla={vanilla} mod={mod} x{health:0.##} {healthWhy}");
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
				// Remove just this body without yielding: emptied homes and earlier corpses
				// stay unchanged, so this measures one corpse's contribution independently.
				int withBody = Gain(true);
				victim.DestroyMarker();
				int withoutBody = Gain(true);
				if (withoutBody == 0)
				{
					Skip("an unburied body in the village halves migration", $"{village.name} draws no settlers even without this body");
				}
				else
				{
					Check("an unburied body in the village halves migration", () =>
						(withBody == withoutBody / 2, $"with body={withBody}, without this body={withoutBody}"));
				}
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
			int empty = village.GetNumberOfUnoccupiedStructure(STRUCTURE_TYPE.DWELLING);
			int homes = village.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> dwellings) ? dwellings.Count : 0;
			Log($"  killed {doomed.Count}; alive={village.residents.Count(r => r != null && !r.isDead)} emptyHomes={empty} of {homes}");
			// The rule: more homes empty beyond the two spare than lived in. A village with few
			// homes (couples share one) can lose most of its people without meeting it.
			if (empty - 2 <= homes - empty)
			{
				Skip("a village that lost most of its people draws no settlers", $"{village.name} has too few homes: {empty} of {homes} empty; x{collapsed:0.##} {collapseWhy}");
			}
			else Check("a village that lost most of its people draws no settlers", () =>
				(gainCollapsed == 0 && collapseWhy != null, $"gain {gainCollapsed} (vanilla {vanilla}); x{collapsed:0.##} {collapseWhy}"));
		}

		// Share of the village's curfew-bound residents (alive, not ruler/leader, with a home)
		// currently in their home structure. The plagued and quarantined do not count: they
		// are held or cared for elsewhere by design; nor do hunters and traders, out on the
		// village's errands (earlier tests may leave them out). (Jobs cannot be filtered out:
		// the curfew itself sends people home through an IDLE_RETURN_HOME job.)
		private static float HomeShare(NPCSettlement village, out int count)
		{
			List<Character> bound = village.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && !r.isSettlementRuler
				&& !r.isFactionLeader && r.homeStructure != null && !r.homeStructure.hasBeenDestroyed
				&& !r.traitContainer.HasTrait("Plagued", "Quarantined") && !PlusBridge.IsHunting(r) && !PlusBridge.IsTrading(r)).ToList();
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
			if (graveIn != null && graveIn != STRUCTURE_TYPE.CEMETERY && buryJobs.Count > 0 && buryJobs.All(j => j.Contains("-> CEMETERY")))
			{
				// The burial went to the Cemetery (the mod's part). The game plants the stone on
				// the burier's own tile (BuryCharacter.AfterBurySuccess): next to a full Cemetery,
				// or wherever the burier stopped short of it.
				int free = village.structures.TryGetValue(STRUCTURE_TYPE.CEMETERY, out List<LocationStructure> cemeteries) ? cemeteries.Sum(c => c.unoccupiedTiles.Count) : 0;
				Skip("cemetery village still buries its dead in the Cemetery", $"sent to the Cemetery ({free} free tile(s)), the game placed the stone in {graveIn}; jobs: {string.Join("; ", buryJobs)}");
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
			Character beast = Guard("spawn creature", () => SpawnAndKill(village, CarcassFor(village)));
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
				string jobs = string.Join(", ", outsider.allJobsTargetingThis.Select(j => $"{j.jobType} by {j.originalOwner?.name ?? "?"} taken by {j.assignedCharacter?.name ?? "nobody"}"));
				return (inPit, $"hasMarker={outsider.hasMarker} grave={where?.ToString() ?? "-"} hauledDelta={PlusBridge.HauledTotal - hauledBeforeOutsider} jobs=[{jobs}] pit={PlusBridge.FindFor(village) != null}");
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
			Watched.Add(body);
			if (mummy != null) Watched.Add(mummy);
			float start = GameHours;
			string lastStage = firstStage;
			HashSet<string> stages = new HashSet<string>();
			if (firstStage != null) stages.Add(firstStage);

			// Hovering the body shows its decay bar (the game's map HP bar), as full as the
			// share of decay time left. The screen is saved as decaybar.png to look at.
			yield return WaitGameHours(3f, () => PlusBridge.DecayStage(body) == "Bloated" || !body.hasMarker);
			if (PlusBridge.DecayStage(body) is string bloated) stages.Add(bloated);
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
			yield return WaitGameHours(8f, () =>
			{
				string st = PlusBridge.DecayStage(body);
				if (st != null && st != lastStage)
				{
					Log($"  decay stage -> {st} after {GameHours - start:F1}h");
					stages.Add(st);
					lastStage = st;
				}
				return !body.hasMarker;
			});
			Check("unburied corpse decomposes and disappears", () =>
				(!body.hasMarker && new[] { "Fresh", "Bloated", "Rotting", "Skeletal" }.All(stages.Contains),
					$"hasMarker={body.hasMarker} stages={string.Join(",", stages)} lastStage={lastStage} after {GameHours - start:F1}h"));
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
			CapitalChecks();
			// A capital is a City without a Town Hall: the Town steps need an ordinary village.
			NPCSettlement village = Villages().Where(v => !PlusBridge.IsCapital(v) && PlusBridge.TownHallFor(v) == null && HasRoomFor(v, STRUCTURE_TYPE.TAVERN))
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
				Log("  Town Hall build prerequisites: " + BlueprintState(village));
				yield return WaitGameHours(120f - (GameHours - start), () => PlusBridge.TownHallFor(village) != null);
			}
			LocationStructure hall = PlusBridge.TownHallFor(village);
			// Emptied, or the blueprint expired unbuilt (the game drops one nobody starts within
			// 24h) and the village has no room left for another (a Mass Grave took the space):
			// nothing left to build or measure.
			string noBuild = hall != null ? null
				: Villagers(village) < townMark ? $"{village.name} fell below the Town threshold during construction: {Villagers(village)} residents, threshold {townMark}; {BlueprintState(village)}"
				: !PlusBridge.HasPendingTownHall(village) && !HasRoomFor(village, STRUCTURE_TYPE.TAVERN)
					? $"{village.name}'s Town Hall blueprint expired unbuilt and there is no room left for another (the game's own placement check; {BlueprintState(village)})"
				: null;
			if (noBuild != null)
			{
				foreach (string name in new[] { "a grown village builds a Town Hall from materials", "the village is a Town once its Town Hall stands", "the Town is announced",
					"the settlement panel names the tier", "the center's building panel names what the village is", "the tier is stored inside the player's save file",
					"a Town reaching the city mark becomes a City", "a City keeps its tier just under the mark",
					"a City far below the mark falls back to a Town", "destroying the Town Hall makes the Town a village again" })
				{
					Skip(name, noBuild);
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
			Check("the settlement panel names the tier", () => (panel != null && panel.EndsWith(" Town"), $"\"{panel}\""));
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
				(center != null && center[0] == "Town" && center[1] != null && center[1].Contains("Town Center"),
				$"header=\"{center?[0]}\" description=\"{center?[1]}\""));

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
			// Counted together: people settle in while the screenshot is taken.
			int alive = village.residents.Count(c => c != null && !c.isDead);
			int villageShown = Guard("open the center's Residents tab", () => (object)residentsShown(village.cityCenter)) as int? ?? -1;
			yield return new WaitForSecondsRealtime(1f);
			yield return Screenshot("residents.png");
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
			NPCSettlement capitalCity = GridMap.Instance.mainRegion.settlementsInRegion.OfType<NPCSettlement>().FirstOrDefault(PlusBridge.IsCapital);
			if (capitalCity != null && saved != null)
			{
				ReplayLoad("ruinarch.plus.tiers.json", saved);
				Check("a capital is still the capital after the save loads", () =>
					(saved.Contains(capitalCity.persistentID + "|Capital") && PlusBridge.IsCapital(capitalCity) && PlusBridge.Tier(capitalCity) == "City" && PlusBridge.Tier(village) == "Town",
					$"{capitalCity.name}: capital={PlusBridge.IsCapital(capitalCity)} tier={PlusBridge.Tier(capitalCity)}; {village.name} tier={PlusBridge.Tier(village)}"));
			}

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

		// The Age row Ruinarch+ adds to the character panel's Info tab: { header, value }.
		private static string[] AgeRow(Component panel)
		{
			Transform row = panel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RuinarchPlus.AgeRow");
			TMPro.TMP_Text[] texts = row?.GetComponentsInChildren<TMPro.TMP_Text>(true);
			if (texts == null || texts.Length == 0)
			{
				return null;
			}
			TMPro.TMP_Text header = texts.FirstOrDefault(t => t.text == "Age");
			TMPro.TMP_Text value = texts.FirstOrDefault(t => t != header);
			return new[] { header?.text, value?.text };
		}

		// A creature for the Mass Grave's carcass checks. A village with a Hunter Lodge skins
		// skinnable carcasses (wolves) as in the base game, so it gets a Scorpion instead.
		private static SUMMON_TYPE CarcassFor(NPCSettlement village) =>
			village.HasStructureOfTypeThatIsAssigned(STRUCTURE_TYPE.HUNTER_LODGE) ? SUMMON_TYPE.Scorpion : SUMMON_TYPE.Wolf;

		// A capital lasts until it is destroyed (nobody left alive in it). Then it is a village
		// again and its faction, if it still holds several villages, names a new one.
		private IEnumerator CapitalLossSuite()
		{
			NPCSettlement capital = GridMap.Instance.mainRegion.settlementsInRegion.OfType<NPCSettlement>()
				.Where(PlusBridge.IsCapital).OrderByDescending(c => c.owner.ownedSettlements.Count(o => o is NPCSettlement { locationType: LOCATION_TYPE.VILLAGE })).FirstOrDefault();
			if (capital == null)
			{
				Skip("a destroyed capital is a village again", "no capital in this world: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			Faction faction = capital.owner;
			Log($"capital loss: {Describe(capital)} of {faction.name}");
			// Ruled by someone else first: still the capital.
			Character ruler = capital.ruler;
			Guard("take the ruler away", () => { capital.SetRuler(null); return capital; });
			yield return WaitGameHours(2f, null);
			Check("a capital stays the capital whoever rules it", () =>
				(PlusBridge.IsCapital(capital) && PlusBridge.Tier(capital) == "City", $"ruler {ruler?.name ?? "none"} -> {capital.ruler?.name ?? "none"}; capital={PlusBridge.IsCapital(capital)} tier={PlusBridge.Tier(capital)}"));
			foreach (Character r in capital.residents.Where(r => r != null && !r.isDead).ToList())
			{
				Guard("kill " + r.name, () => { r.Death("autotest"); return r; });
			}
			yield return WaitGameHours(2f, () => !PlusBridge.IsCapital(capital));
			List<NPCSettlement> left = GridMap.Instance.mainRegion.settlementsInRegion.OfType<NPCSettlement>()
				.Where(v => v != capital && v.locationType == LOCATION_TYPE.VILLAGE && v.owner == faction && Villagers(v) > 0).ToList();
			Check("a destroyed capital is a village again", () =>
				(!PlusBridge.IsCapital(capital) && PlusBridge.Tier(capital) == "Village", $"{capital.name}: alive={Villagers(capital)} capital={PlusBridge.IsCapital(capital)} tier={PlusBridge.Tier(capital)}"));
			if (left.Count < 2)
			{
				Skip("the faction names a new capital", $"{faction.name} holds {left.Count} village(s) now (a capital needs a faction of several)");
				yield break;
			}
			NPCSettlement next = left.FirstOrDefault(PlusBridge.IsCapital);
			Check("the faction names a new capital", () =>
				(next != null && left.Count(PlusBridge.IsCapital) == 1 && PlusBridge.Tier(next) == "City" && ModsLogHas($"{next.name} is now the capital of {faction.name}"),
				$"{faction.name}: {string.Join(", ", left.Select(v => $"{v.name} capital={PlusBridge.IsCapital(v)} tier={PlusBridge.Tier(v)}"))}"));
		}

		// Every major faction of several villages has one capital (named at the first hour of
		// the world): a City whatever its size and whoever rules it, called Capital in panels.
		private void CapitalChecks()
		{
			List<IGrouping<Faction, NPCSettlement>> factions = GridMap.Instance.mainRegion.settlementsInRegion.OfType<NPCSettlement>()
				.Where(v => v.locationType == LOCATION_TYPE.VILLAGE && v.owner != null && v.owner.isMajorFaction && Villagers(v) > 0)
				.GroupBy(v => v.owner).Where(g => g.Count() > 1).ToList();
			if (factions.Count == 0)
			{
				Skip("a faction of several villages has one capital, a City", "no major faction holds two villages: " + string.Join("; ", Villages().Select(Describe)));
				return;
			}
			Check("a faction of several villages has one capital, a City", () =>
			{
				List<string> bad = new List<string>();
				List<string> seen = new List<string>();
				foreach (IGrouping<Faction, NPCSettlement> f in factions)
				{
					List<NPCSettlement> capitals = f.Where(PlusBridge.IsCapital).ToList();
					foreach (NPCSettlement c in capitals)
					{
						seen.Add($"{c.name} of {f.Key.name}: tier={PlusBridge.Tier(c)} label=\"{PlusBridge.TierLabel(c, "")}\" villagers={Villagers(c)} townHall={PlusBridge.TownHallFor(c) != null}");
					}
					if (capitals.Count != 1 || PlusBridge.Tier(capitals[0]) != "City" || PlusBridge.TierLabel(capitals[0], "") != "Capital")
					{
						bad.Add($"{f.Key.name}: {capitals.Count} capital(s)");
					}
				}
				return (bad.Count == 0, string.Join("; ", bad.Concat(seen)));
			});
			NPCSettlement capital = factions.SelectMany(f => f).FirstOrDefault(PlusBridge.IsCapital);
			if (capital == null)
			{
				return;
			}
			string description = Guard("open the capital center's building panel", () =>
			{
				UIManager.Instance.ShowStructureInfo(capital.cityCenter);
				string text = (AccessTools.Field(typeof(StructureInfoUI), "cityCenterDescriptionLbl").GetValue(UIManager.Instance.structureInfoUI) as TMPro.TMP_Text)?.text;
				UIManager.Instance.structureInfoUI.CloseMenu();
				return text;
			});
			Check("a capital's center is described as a City Center", () =>
				(description != null && description.Contains("City Center") && !description.Contains("Village Center"), $"{capital.name}: \"{description}\""));
		}

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
				.Select(g => $"{g.blueprintOnTile.structureType}@{g.gridTileLocation.localPlace} cost={g.blueprintOnTile.craftCost} {g.blueprintOnTile.thinWallResource.GetResourceForWall()} started={g.hasStartedBuildingBlueprintOnTile}").ToList();
			List<JobQueueItem> jobs = new List<JobQueueItem>();
			village.PopulateJobsOfType(jobs, JOB_TYPE.BUILD_BLUEPRINT);
			int taverns = village.structures.TryGetValue(STRUCTURE_TYPE.TAVERN, out List<LocationStructure> t2) ? t2.Count(s => !s.hasBeenDestroyed) : 0;
			LocationGridTile buildTile = jobs.Select(j => j.poiTarget?.gridTileLocation).FirstOrDefault(t => t != null);
			string workers = string.Join("; ", village.residents.Where(r => r != null && !r.isDead).Select(r =>
				$"{r.name}: job={r.currentJob?.jobType.ToString() ?? "none"} action={r.currentActionNode?.goapName ?? "none"} move={r.limiterComponent.canMove} perform={r.limiterComponent.canPerform} party={r.partyComponent.hasParty} path={(buildTile != null && r.gridTileLocation != null && r.movementComponent.HasPathToEvenIfDiffRegion(buildTile))}"));
			return $"blueprints=[{string.Join(", ", blueprints)}] buildJobs={jobs.Count} (taken {jobs.Count(j => j.assignedCharacter != null)}) taverns={taverns} accessWood={village.settlementJobTriggerComponent.HasAccessToResource(RESOURCE.WOOD)} accessStone={village.settlementJobTriggerComponent.HasAccessToResource(RESOURCE.STONE)} workers=[{workers}]";
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
			// Not a village already hungry or in famine on its own (the world can starve one before
			// the suite runs): the checks measure a famine this suite causes.
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorFaction && villagers(v).Count >= 4 && !v.isPlagued
					&& PlusBridge.InFamine(v) == false && !PlusBridge.IsHungry(v))
				.OrderByDescending(hasRefuge).FirstOrDefault();
			if (village == null)
			{
				Skip("famine", "no well-fed, unplagued village of a major faction with 4+ villagers: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			List<Character> people = villagers(village);
			Log($"famine test village: {Describe(village)} villagers={people.Count} refuge={hasRefuge(village)}");
			yield return CaptivesDoNotStarveTheVillage(village, people);
			PlusBridge.SetConfig("famineLeaveChance", 100);
			// Hunting off: hunters (HuntSuite) feed a hungry village before it falls into famine,
			// which is what they are for, and would keep this village out of it.
			PlusBridge.SetConfig("huntingEnabled", false);
			Action starve = () =>
			{
				foreach (Character c in people.Where(c => !c.isDead && c.homeSettlement == village))
				{
					c.needsComponent.SetFullness(5f);
				}
			};
			// The famine clock only runs with three or more villagers inside (Famine: a few
			// starving out in the wild do not put their home in famine). Search parties and
			// errands take them away (in one small village, two were out for 11 of the 20
			// hours), so bring them home, out of any party, whenever fewer are inside.
			Action keepHome = () =>
			{
				if (PlusBridge.FamineCount(village).villagers >= 3)
				{
					return;
				}
				LocationGridTile home = village.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? village.cityCenter.tiles.First();
				foreach (Character c in people.Where(c => !c.isDead && c.homeSettlement == village && c.hasMarker && c.carryComponent.isBeingCarriedBy == null
					&& !c.traitContainer.HasTrait("Restrained") && (c.gridTileLocation == null || !c.gridTileLocation.IsPartOfSettlement(village))).ToList())
				{
					if (c.partyComponent.hasParty)
					{
						c.partyComponent.currentParty.RemoveMember(c);
					}
					CharacterManager.Instance.Teleport(c, home);
				}
			};

			float start = GameHours;
			int loggedHour = -1;
			yield return WaitGameHours(20f, () =>
			{
				keepHome();
				starve();
				if ((int)(GameHours - start) != loggedHour)
				{
					loggedHour = (int)(GameHours - start);
					(int starving, int inside) = PlusBridge.FamineCount(village);
					Log($"  hour {loggedHour}: starving {starving} of {inside} inside; residents {string.Join(" ", people.Select(c => $"{c.name}[{(c.isDead ? "dead" : "")}{(c.homeSettlement == village ? "" : " moved")}{(c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(village) ? "" : " away")} {c.needsComponent.fullness:F0}]"))}");
				}
				return PlusBridge.InFamine(village) == true;
			});
			Check("a village whose people go hungry falls into famine", () =>
				(PlusBridge.InFamine(village) == true, $"famine={PlusBridge.InFamine(village)} after {GameHours - start:F1}h"));
			Check("the famine is announced", () => (ModsLogHas($"Famine in {village.name}"), "mods.log"));
			Check("the famine is held against the ruler (unrest)", () =>
			{
				List<string> reasons = PlusBridge.UnrestReasons(village);
				return (reasons.Contains("the famine"), string.Join(", ", reasons));
			});
			Check("no settlers move into a village in famine", () =>
			{
				float m = PlusBridge.MigrationMultiplier(village, out string why);
				// Any reason for zero will do (an attack on the village also stops settlers and is
				// named first); what matters is that nobody moves in.
				return (m == 0f && (why == "famine" || village.isUnderSiege), $"x{m} {why}");
			});

			if (!hasRefuge(village))
			{
				Skip("starving villagers leave for a village with food", "no other village of the faction has a free home");
			}
			else
			{
				// Only the famine's own moves count (announced "... has left <village> for ..."):
				// villagers also change homes for the game's own reasons.
				Func<Character, bool> moved = c => !c.isDead && c.homeSettlement != null && c.homeSettlement != village;
				// A native home change must not end the wait before famine's daily move.
				yield return WaitGameHours(26f, () =>
				{
					starve();
					return people.Any(c => moved(c) && ModsLogHas($"{c.name} has left {village.name} for"));
				});
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

			// Everyone who lives there now, not only those the test started with: some left for
			// food, and a hungry resident the test never tracked holds the fed clock back.
			foreach (Character c in people.Concat(village.residents.Where(r => r != null && r.isNormalCharacter)).Distinct().Where(c => !c.isDead))
			{
				c.needsComponent.SetFullness(100f);
			}
			yield return WaitGameHours(16f, () => PlusBridge.InFamine(village) == false);
			Check("fed again, the famine ends", () =>
				(PlusBridge.InFamine(village) == false && ModsLogHas($"The famine in {village.name} is over"), $"famine={PlusBridge.InFamine(village)}; starving/inside={PlusBridge.FamineCount(village)}; "
					+ string.Join(", ", village.residents.Where(c => c != null && !c.isDead && c.isNormalCharacter).Select(c =>
						$"{c.name}[{c.needsComponent.fullness:F0}{(c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(village) ? "" : " away")}]"))));
			PlusBridge.SetConfig("famineLeaveChance", 25);
			PlusBridge.SetConfig("huntingEnabled", true);
		}

		// Unrest: what a village holds against its ruler, restlessness, and uprisings the
		// rebels win (the ruler knocked out loses the rule) and lose (the rebels knocked out).
		// Outcomes are made certain by opinions (who takes which side) and by hurting the side
		// meant to lose before it starts.
		private IEnumerator UnrestSuite()
		{
			if (!PlusBridge.Available || PlusBridge.UnrestPoints(null) < 0f)
			{
				Skip("unrest", "RuinarchPlus (with unrest) not loaded");
				yield break;
			}
			Func<NPCSettlement, List<Character>> adults = v => v.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()
				&& r.hasMarker && !PlusBridge.IsChild(r) && r.gridTileLocation != null && r.gridTileLocation.IsPartOfSettlement(v)).ToList();
			// Residents, wherever they are now (people come and go; only those present rise).
			Func<NPCSettlement, int> residents = v => v.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !PlusBridge.IsChild(r));
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorFaction && v.ruler != null && !v.ruler.isDead && !v.isPlagued && residents(v) >= 6)
				.OrderByDescending(v => adults(v).Count).FirstOrDefault();
			if (village == null)
			{
				Skip("unrest", "no village with a ruler and 6+ adult villagers: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			BringResidentsHome(village);
			Log($"unrest test village: {Describe(village)} ruler={village.ruler.name} (faction leader={village.ruler.isFactionLeader}) adults at home={adults(village).Count}");
			PlusBridge.SetConfig("unrestEnabled", true);
			// This suite measures the brawl; the other kinds are UprisingKindsSuite's.
			PlusBridge.SetConfig("uprisingKindsEnabled", false);
			PlusBridge.SetUnrest(village, 0f);
			Log("  grievances at the start: " + string.Join(", ", PlusBridge.UnrestReasons(village)));

			// 1. Grievances, each read right after it is caused.
			Character victim = Guard("kill a villager", () => KillResident(village));
			Check("a villager's death is held against the ruler", () =>
			{
				List<string> r = PlusBridge.UnrestReasons(village);
				return (victim != null && r.Any(x => x == "a death" || x.EndsWith(" deaths")), string.Join(", ", r));
			});
			bool sieged = Guard("put the village under attack", () => { village.SetIsUnderSiege(true); return village; }) != null && village.isUnderSiege;
			List<string> duringSiege = PlusBridge.UnrestReasons(village);
			Guard("lift the attack", () => { village.SetIsUnderSiege(false); return village; });
			Check("an attack on the village is held against the ruler", () => (sieged && duringSiege.Contains("the attacks on the village"), $"siege={sieged}: {string.Join(", ", duringSiege)}"));
			LocationStructure wreck = village.structures.Values.SelectMany(l => l).FirstOrDefault(st => st is ManMadeStructure && !st.hasBeenDestroyed
				&& st.structureType.IsVillageStructure() && st.structureType != STRUCTURE_TYPE.CITY_CENTER && st.structureType != STRUCTURE_TYPE.DWELLING && st.residents.Count == 0);
			if (wreck == null)
			{
				Skip("a lost building is held against the ruler", "no empty village building to wreck");
			}
			else
			{
				Guard("wreck " + wreck.name, () => { wreck.AdjustHP(-wreck.currentHP); return wreck; });
				Check("a lost building is held against the ruler", () =>
				{
					List<string> r = PlusBridge.UnrestReasons(village);
					return (wreck.hasBeenDestroyed && r.Any(x => x.EndsWith("lost building") || x.EndsWith("lost buildings")), $"{wreck.name} destroyed={wreck.hasBeenDestroyed}: {string.Join(", ", r)}");
				});
			}
			Character ruler = village.ruler;
			bool deposedFactionLeader = village.owner.leader == ruler;
			const string successionCheck = "a deposed faction leader cannot reclaim their village";
			List<Character> people = adults(village).Where(c => c != ruler).ToList();
			Guard("turn the village against its ruler", () =>
			{
				foreach (Character c in people)
				{
					c.relationshipContainer.AdjustOpinion(c, ruler, "Autotest", -400, "autotest", createJobsOnReduce: false);
				}
				return ruler;
			});
			// The rule is judged by the adults inside the village (at least 3 besides the ruler);
			// after the death above a small village may have too few at home.
			BringResidentsHome(village);
			int judges = adults(village).Count(c => c != ruler);
			if (judges < 3)
			{
				Skip("a ruler most villagers dislike is held against", $"only {judges} adult(s) besides the ruler at home");
			}
			else
			{
				Check("a ruler most villagers dislike is held against", () =>
				{
					List<string> r = PlusBridge.UnrestReasons(village);
					return (r.Contains("their rule"), $"judges at home={judges}: {string.Join(", ", r)}");
				});
			}

			// 2. Restless, with the reasons named.
			float restlessAt = PlusBridge.Config("unrestRestless") is int rr ? rr : 24;
			PlusBridge.SetUnrest(village, restlessAt - 0.25f);
			yield return WaitGameHours(2f, () => PlusBridge.IsRestless(village));
			Check("a village with grievances enough becomes restless, and says why", () =>
				(PlusBridge.IsRestless(village) && ModsLogHas($"{village.name} is restless: its people blame {ruler.name} for"), $"restless={PlusBridge.IsRestless(village)} points={PlusBridge.UnrestPoints(village):F1}"));
			string saved = null;
			yield return SaveAndRead("ruinarch.plus.unrest.json", (j, e) => saved = j);
			Check("unrest is stored inside the player's save file", () => (saved != null && saved.Contains(village.persistentID), saved ?? "no unrest entry"));
			if (saved != null)
			{
				float before = PlusBridge.UnrestPoints(village);
				ReplayLoad("ruinarch.plus.unrest.json", saved);
				Check("unrest comes back when the save loads", () =>
					(PlusBridge.IsRestless(village) && Math.Abs(PlusBridge.UnrestPoints(village) - before) < 0.01f, $"points {before:F2} -> {PlusBridge.UnrestPoints(village):F2} restless={PlusBridge.IsRestless(village)}"));
			}

			// 3. An uprising the rebels win: everyone is against the ruler, who starts hurt.
			float uprisingAt = PlusBridge.Config("unrestUprising") is int uu ? uu : 72;
			Guard("hurt the ruler", () => { ruler.AdjustHP(-(ruler.currentHP - Math.Max(1, ruler.maxHP / 5)), ELEMENTAL_TYPE.Normal); return ruler; });
			PlusBridge.SetUnrest(village, uprisingAt);
			int riseMark = ModsLogLength();
			// Up to a night: asleep, nobody rises until morning.
			yield return WaitGameHours(12f, () =>
			{
				PlusBridge.SetUnrest(village, Math.Max(PlusBridge.UnrestPoints(village), uprisingAt));
				if (!ruler.isDead && ruler.currentHP > Math.Max(1, ruler.maxHP / 5))
				{
					ruler.AdjustHP(-(ruler.currentHP - Math.Max(1, ruler.maxHP / 5)), ELEMENTAL_TYPE.Normal);
				}
				return PlusBridge.HasUprising(village) || ruler.isDead || village.ruler != ruler;
			});
			Character newRuler = village.ruler;
			// Hurt and hated, the ruler can be killed in one of the game's own fights (a villager's
			// Rage) before the village rises: nothing of the uprising to measure then.
			if (!PlusBridge.HasUprising(village) && (ruler.isDead || village.ruler != ruler))
			{
				string gone = $"{ruler.name} {(ruler.isDead ? "was killed" : "lost the rule")} before the village rose (not by an uprising; now ruled by {village.ruler?.name ?? "nobody"})";
				Skip("an uprising breaks out and the rebels fight the ruler", gone);
				Skip("the rebels who knock the ruler out take the rule", gone);
				Skip(successionCheck, gone);
			}
			else
			{
				bool rose = PlusBridge.HasUprising(village);
				int fighting = 0;
				yield return WaitGameHours(14f, () =>
				{
					fighting = Math.Max(fighting, people.Count(c => !c.isDead && c.combatComponent.hostilesInRange.Contains(ruler)));
					return !PlusBridge.HasUprising(village);
				});
				Check("an uprising breaks out and the rebels fight the ruler", () =>
					(rose && fighting > 0 && ModsLogHas($"leads an uprising against {ruler.name} in {village.name}"), $"rose={rose} rebels fighting the ruler at once={fighting}"));
				newRuler = village.ruler;
				Log("  after the uprising: " + string.Join(", ", adults(village).Concat(new[] { ruler }).Distinct().Select(c =>
					$"{c.name}[opinion of {newRuler?.name}={(newRuler == null || c == newRuler ? 0 : c.relationshipContainer.GetTotalOpinion(newRuler))} wanted={c.crimeComponent.IsWantedBy(village.owner)} crimes={string.Join("/", c.crimeComponent.activeCrimes.Select(k => k.crimeType))} unconscious={c.traitContainer.HasTrait("Unconscious")} canPerform={c.limiterComponent.canPerform}]")));
				// Few awake to rise (one rebel against a hurt ruler) can lose the game's own fight:
				// the takeover is then not reached, which step 4 measures from the other side.
				if (newRuler == ruler && ModsLogHasSince(riseMark, $"{ruler.name} has put down the uprising in {village.name}"))
				{
					Skip("the rebels who knock the ruler out take the rule", $"the ruler won the brawl against {fighting} rebel(s) at once");
				}
				else Check("the rebels who knock the ruler out take the rule", () =>
					(newRuler != null && newRuler != ruler && ModsLogHas("in an uprising") && people.Contains(newRuler),
					$"ruler {ruler.name} -> {newRuler?.name ?? "none"}; ruler unconscious={ruler.traitContainer.HasTrait("Unconscious")} uprising={PlusBridge.HasUprising(village)}"));
				// Exercise the native reassignment that would restore an improperly deposed
				// faction leader, rather than waiting for unrelated world events to replace a ruler.
				if (newRuler == null || newRuler == ruler || !deposedFactionLeader)
				{
					Skip(successionCheck, "no takeover of a faction leader");
				}
				else
				{
					Try("reassign the faction leader's home village", village.owner.ProcessFactionLeaderAsSettlementRuler);
					Check(successionCheck, () => (village.ruler == newRuler && village.owner.leader == newRuler,
						$"village ruler={village.ruler?.name ?? "none"} faction leader={village.owner.leader?.name ?? "none"}; successor={newRuler.name}, deposed={ruler.name}"));
				}
			}

			// 4. Exercise the defeated-rebel branch, without relying on random combat rolls.
			if (newRuler == null || newRuler.isDead || newRuler != village.ruler)
			{
				Skip("a ruler who knocks the rebels out keeps the rule", "no new ruler to test with");
			}
			else
			{
				// Every resident, home or not: one left hating the ruler would lead the next rising.
				List<Character> loyal = village.residents.Where(c => c != null && !c.isDead && c != newRuler && c.isNormalCharacter && c.race.IsSapient() && !PlusBridge.IsChild(c)).ToList();
				Character rebel = adults(village).FirstOrDefault(c => c != newRuler && c != ruler && !c.traitContainer.HasTrait("Unconscious"));
				if (rebel == null || loyal.Count < 3)
				{
					Skip("a ruler who knocks the rebels out keeps the rule", $"too few villagers standing: {loyal.Count}");
				}
				else
				{
					Guard("one against a loyal village", () =>
					{
						// Step 3 left the old ruler hurt if they kept the rule.
						newRuler.ResetToFullHP();
						foreach (Character c in loyal)
						{
							int now = c.relationshipContainer.GetTotalOpinion(newRuler);
							c.relationshipContainer.AdjustOpinion(c, newRuler, "Autotest", (c == rebel ? -300 : 300) - now, "autotest", createJobsOnReduce: false);
						}
						return rebel;
					});
					// This branch measures defeated rebels, not a ruler incapacitated while waiting.
					Action defend = () =>
					{
						if (newRuler.isDead) return;
						newRuler.ResetToFullHP();
						if (newRuler.traitContainer.HasTrait("Resting"))
							newRuler.interruptComponent.TriggerInterrupt(INTERRUPT.Noise_Wake_Up, newRuler);
					};
					// Everyone home, so the loyal side is there to stand by the ruler.
					BringResidentsHome(village);
					PlusBridge.SetUnrest(village, uprisingAt);
					int riseMark4 = ModsLogLength();
					// The previous uprising enforces a day's calm, then the rebel may be asleep.
					LocationGridTile square = village.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? village.cityCenter.tiles.First();
					yield return WaitGameHours(36f, () =>
					{
						defend();
						if (!rebel.isDead && rebel.gridTileLocation != null && !rebel.gridTileLocation.IsPartOfSettlement(village))
						{
							CharacterManager.Instance.Teleport(rebel, square);
						}
						PlusBridge.SetUnrest(village, Math.Max(PlusBridge.UnrestPoints(village), uprisingAt));
						return PlusBridge.HasUprising(village);
					});
					bool rose2 = PlusBridge.HasUprising(village);
					bool knockedOut = rose2 && Guard("knock out the challenger", () =>
					{
						rebel.traitContainer.AddTrait(rebel, "Unconscious");
						return rebel.traitContainer.HasTrait("Unconscious") ? rebel : null;
					}) != null;
					yield return WaitGameHours(14f, () => { defend(); return !PlusBridge.HasUprising(village); });
					CheckAlive(newRuler, "a ruler who knocks the rebels out keeps the rule", () =>
						(rose2 && knockedOut && village.ruler == newRuler
							&& ModsLogHasSince(riseMark4, $"{newRuler.name} has put down the uprising in {village.name}")
							&& newRuler.relationshipContainer.HasGrudgeAgainst(rebel),
						$"rose={rose2} knockedOut={knockedOut} ruler={village.ruler?.name} (was {newRuler.name}); challenger {rebel.name} grudge={newRuler.relationshipContainer.HasGrudgeAgainst(rebel)}"));
				}
			}
			foreach (NPCSettlement v in Villages())
			{
				PlusBridge.SetUnrest(v, 0f);
			}
			PlusBridge.SetConfig("unrestEnabled", false);
			PlusBridge.SetConfig("uprisingKindsEnabled", true);
		}

		// Residents out at work, trading or hunting neither judge the ruler nor rise: bring
		// the free ones home, so the rebels and the ruler's side are the whole village.
		private void BringResidentsHome(NPCSettlement village)
		{
			LocationGridTile home = village.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? village.cityCenter.tiles.First();
			Guard("bring the residents home", () =>
			{
				foreach (Character c in village.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && r.hasMarker
					&& !PlusBridge.IsChild(r) && r.carryComponent.isBeingCarriedBy == null && !r.partyComponent.hasParty && !r.traitContainer.HasTrait("Restrained")
					&& r.gridTileLocation != null && !r.gridTileLocation.IsPartOfSettlement(village)).ToList())
				{
					CharacterManager.Instance.Teleport(c, home);
				}
				return village;
			});
		}

		private static List<Character> AdultsAtHome(NPCSettlement v) => v.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()
			&& r.hasMarker && !PlusBridge.IsChild(r) && r.gridTileLocation != null && r.gridTileLocation.IsPartOfSettlement(v)).ToList();

		// Turn <paramref name="village"/>'s adults against its ruler (those <paramref name="rebel"/>
		// picks; the rest for the ruler) and have it rise now as <paramref name="kind"/>.
		private IEnumerator RiseAs(NPCSettlement village, string kind, Func<Character, bool> rebel, bool hurtRuler)
		{
			Character ruler = village.ruler;
			BringResidentsHome(village);
			Guard("set the villagers' feelings for the ruler", () =>
			{
				foreach (Character c in AdultsAtHome(village).Where(c => c != ruler))
				{
					c.relationshipContainer.AdjustOpinion(c, ruler, "Autotest", rebel(c) ? -400 : 400, "autotest", createJobsOnReduce: false);
				}
				return ruler;
			});
			PlusBridge.ForceUprising(village, kind);
			float at = PlusBridge.Config("unrestUprising") is int uu ? uu : 72;
			Action hurt = () =>
			{
				if (hurtRuler && !ruler.isDead && ruler.currentHP > Math.Max(1, ruler.maxHP / 5))
				{
					ruler.AdjustHP(-(ruler.currentHP - Math.Max(1, ruler.maxHP / 5)), ELEMENTAL_TYPE.Normal);
				}
			};
			// Up to a night: asleep, nobody rises until morning.
			yield return WaitGameHours(12f, () =>
			{
				PlusBridge.SetUnrest(village, Math.Max(PlusBridge.UnrestPoints(village), at));
				hurt();
				return PlusBridge.HasUprising(village) || ruler.isDead || village.ruler != ruler;
			});
			Log($"  {village.name} rose as {PlusBridge.UprisingKind(village) ?? "nothing"} (asked {kind}): leader {PlusBridge.UprisingLeader(village)?.name ?? "none"}, ruler {ruler.name} dead={ruler.isDead} ruler now {village.ruler?.name ?? "none"}");
		}

		// Other ways a ruler falls (Phase5/Uprisings.cs): the kind weights for real people, then
		// each kind forced on a village turned against its ruler.
		private IEnumerator UprisingKindsSuite()
		{
			if (!PlusBridge.Available || !PlusBridge.UprisingKindsAvailable || PlusBridge.UnrestPoints(null) < 0f)
			{
				Skip("uprising kinds", "RuinarchPlus (with uprising kinds) not loaded");
				yield break;
			}
			PlusBridge.SetConfig("unrestEnabled", true);
			PlusBridge.SetConfig("uprisingKindsEnabled", true);
			Func<NPCSettlement, int> residents = v => v.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !PlusBridge.IsChild(r));
			List<NPCSettlement> villages = Villages().Where(v => v.owner != null && v.owner.isMajorFaction && v.ruler != null && !v.ruler.isDead && !v.isPlagued && v.cityCenter != null)
				.OrderByDescending(v => residents(v)).ToList();
			Func<Dictionary<string, float>, string, float> w = (d, k) => d.TryGetValue(k, out float f) ? f : -1f;
			Func<Dictionary<string, float>, string> show = d => string.Join(", ", d.Select(kv => $"{kv.Key}={kv.Value:0}"));
			if (villages.Count == 0)
			{
				Skip("uprising kinds", "no village of a major faction with a ruler: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}

			// 1. The weights, for real people.
			NPCSettlement wv = villages[0];
			Character wr = wv.ruler;
			Character plain = AdultsAtHome(wv).Concat(wv.residents).FirstOrDefault(c => c != null && c != wr && !c.isDead && c.isNormalCharacter
				&& !c.traitContainer.HasTrait("Evil", "Psychopath", "Ruthless", "Treacherous") && !c.relationshipContainer.HasGrudgeAgainst(wr));
			if (plain == null)
			{
				Skip("a Psychopath leader makes an assassination likelier", $"nobody in {wv.name} without such traits");
			}
			else
			{
				Dictionary<string, float> before = PlusBridge.UprisingWeights(wv, plain, wr);
				bool added = Guard("make the leader a Psychopath", () => plain.traitContainer.AddTrait(plain, "Psychopath") ? plain : null) != null;
				Dictionary<string, float> after = PlusBridge.UprisingWeights(wv, plain, wr);
				Try("take the Psychopath trait back", () => plain.traitContainer.RemoveTrait(plain, "Psychopath"));
				Check("a Psychopath leader makes an assassination likelier", () =>
					(added && w(before, "Assassination") > 0 && Math.Abs(w(after, "Assassination") - 3 * w(before, "Assassination")) < 0.5f,
					$"{plain.name}: before [{show(before)}], as a Psychopath [{show(after)}] (added={added})"));
			}
			NPCSettlement noPrison = villages.FirstOrDefault(v => v.prison == null || v.prison.hasBeenDestroyed);
			Character npLeader = noPrison?.residents.FirstOrDefault(c => c != null && c != noPrison.ruler && !c.isDead && c.isNormalCharacter);
			if (npLeader == null)
			{
				Skip("a village without a prison never jails its ruler", "every village has a prison");
			}
			else
			{
				Dictionary<string, float> d = PlusBridge.UprisingWeights(noPrison, npLeader, noPrison.ruler);
				Check("a village without a prison never jails its ruler", () => (w(d, "Jailing") == 0f, $"{noPrison.name}: [{show(d)}]"));
			}
			NPCSettlement small = villages.LastOrDefault(v => residents(v) < 12);
			Character smallLeader = small?.residents.FirstOrDefault(c => c != null && c != small.ruler && !c.isDead && c.isNormalCharacter);
			if (smallLeader == null)
			{
				Skip("a small village has no civil war", "no village under 12 people");
			}
			else
			{
				Dictionary<string, float> d = PlusBridge.UprisingWeights(small, smallLeader, small.ruler);
				Check("a small village has no civil war", () => (w(d, "CivilWar") == 0f, $"{small.name} ({residents(small)} people): [{show(d)}]"));
			}
			if (plain != null)
			{
				PlusBridge.SetConfig("uprisingKindsEnabled", false);
				Dictionary<string, float> off = PlusBridge.UprisingWeights(wv, plain, wr);
				PlusBridge.SetConfig("uprisingKindsEnabled", true);
				Check("with uprisingKindsEnabled off every uprising is a brawl", () =>
					(w(off, "Brawl") > 0 && off.Where(kv => kv.Key != "Brawl").All(kv => kv.Value == 0f), $"[{show(off)}]"));
			}

			// 2. An assassination plot.
			const string plotCheck = "a plot kills the ruler: unseen, the leader rules; seen, the leader is wanted";
			NPCSettlement av = villages.FirstOrDefault(v => AdultsAtHome(v).Count(c => c != v.ruler) >= 3) ?? villages[0];
			Character aRuler = av.ruler;
			int plotMark = ModsLogLength();
			yield return RiseAs(av, "Assassination", c => true, hurtRuler: false);
			Character plotter = PlusBridge.UprisingLeader(av);
			if (PlusBridge.UprisingKind(av) != "Assassination" || plotter == null)
			{
				Skip(plotCheck, $"no plot started in {av.name}: uprising={PlusBridge.UprisingKind(av) ?? "none"}, {aRuler.name} dead={aRuler.isDead}, ruler now {av.ruler?.name ?? "none"}");
			}
			else
			{
				// Up to two days waiting for the ruler to sleep, then a day to strike.
				yield return WaitGameHours(74f, () => !PlusBridge.HasUprising(av));
				bool unseen = ModsLogHas($"{aRuler.name} of {av.name} has been assassinated; {plotter.name} takes the rule.");
				bool seen = ModsLogHas($"{aRuler.name} of {av.name} has been murdered by {plotter.name}, who is now wanted.");
				bool wanted = av.owner != null && plotter.crimeComponent.IsWantedBy(av.owner);
				string detail = $"{plotter.name} vs {aRuler.name}: ruler dead={aRuler.isDead}, ruler now {av.ruler?.name ?? "none"}, plotter wanted={wanted}, unseen={unseen} seen={seen}";
				if (!aRuler.isDead && ModsLogHas($"The plot against {aRuler.name} in {av.name} has failed."))
				{
					Skip(plotCheck, "the plot failed: " + detail);
				}
				else if (aRuler.isDead && ModsLogHasSince(plotMark, $"{aRuler.name} died before the plot against them in {av.name} came to anything."))
				{
					Skip(plotCheck, "the ruler died before the assassination: " + detail);
				}
				else Check(plotCheck, () => ((unseen && aRuler.isDead && av.ruler == plotter && !wanted) || (seen && aRuler.isDead && av.ruler != plotter && wanted), detail));
			}

			// 3. Jailing: brawl, carried to the prison, left tied, judged after two days.
			const string jailCheck = "rebels who jail the ruler carry them to the prison, and the village leaves them tied";
			const string jailSaveCheck = "a jailed ex-ruler is kept in the save file";
			const string judgeCheck = "after two days the ruler decides the prisoner's fate by what they think of them";
			NPCSettlement jv = villages.Where(v => v != av && !v.ruler.isDead).FirstOrDefault(v => AdultsAtHome(v).Count(c => c != v.ruler) >= 3)
				?? villages.FirstOrDefault(v => v.ruler != null && !v.ruler.isDead && AdultsAtHome(v).Count(c => c != v.ruler) >= 3);
			if (jv != null && (jv.prison == null || jv.prison.hasBeenDestroyed))
			{
				Guard("build a prison", () => InstantBuildVanilla(jv, STRUCTURE_TYPE.PRISON));
			}
			if (jv == null || jv.prison == null || jv.prison.hasBeenDestroyed)
			{
				string why = jv == null ? "no village with a ruler and 3+ adults at home" : $"no prison in {jv.name} (none could be built)";
				Skip(jailCheck, why);
				Skip(jailSaveCheck, why);
				Skip(judgeCheck, why);
			}
			else
			{
				Character old = jv.ruler;
				int jailMark = ModsLogLength();
				yield return RiseAs(jv, "Jailing", c => true, hurtRuler: true);
				if (PlusBridge.UprisingKind(jv) != "Jailing")
				{
					string why = $"no jailing uprising in {jv.name}: uprising={PlusBridge.UprisingKind(jv) ?? "none"}, {old.name} dead={old.isDead}, ruler now {jv.ruler?.name ?? "none"}";
					Skip(jailCheck, why);
					Skip(jailSaveCheck, why);
					Skip(judgeCheck, why);
				}
				else
				{
					yield return WaitGameHours(14f, () => !PlusBridge.HasUprising(jv));
					bool overthrown = jv.ruler != old && ModsLogHas($"has taken the rule of {jv.name} and holds {old.name} in the prison.");
					yield return WaitGameHours(12f, () => PlusBridge.HeldState(old) != "carrying");
					bool delivered = PlusBridge.HeldState(old) == "held" && old.currentStructure == jv.prison;
					yield return WaitGameHours(3f, null);
					bool stillHeld = PlusBridge.HeldState(old) == "held" && old.traitContainer.HasTrait("Restrained");
					int friends = jv.residents.Count(c => c != null && !c.isDead && c != old && c.relationshipContainer.IsFriendsWith(old));
					string detail = $"{old.name}: overthrown={overthrown} (ruler now {jv.ruler?.name ?? "none"}), delivered={delivered}, held 3h later={stillHeld}, state={PlusBridge.HeldState(old) ?? "not held"}, in {old.currentStructure?.name ?? "nowhere"}, friends in the village={friends}";
					// The ruler can win the brawl (only one rebel at home, a strong ruler): then
					// there is nobody to jail.
					bool putDown = !overthrown && jv.ruler == old
						&& (ModsLogHasSince(jailMark, $"has put down the uprising in {jv.name}") || ModsLogHasSince(jailMark, $"The uprising in {jv.name} has failed"));
					if (putDown)
					{
						Skip(jailCheck, "the ruler won the brawl: " + detail);
					}
					// Killed before anyone could tie them up (a villager in a rage once struck
					// the ruler dead in the brawl): there is nobody to jail.
					else if (!overthrown && old.isDead)
					{
						Skip(jailCheck, $"the ruler died first ({old.deathLog?.logText ?? old.causeOfDeath.ToString()}): " + detail);
					}
					else if (overthrown && delivered && !stillHeld && friends > 0 && ModsLogHas($"{old.name}, once ruler of {jv.name}, has escaped the prison."))
					{
						Skip(jailCheck, "a friend freed them: " + detail);
					}
					// Someone from outside the village released them (a Kobold once did, with the
					// game's own release action): the village itself still left them tied.
					else if (overthrown && !stillHeld && HeldUntied.By(old) is Character by && by.homeSettlement != jv)
					{
						Skip(jailCheck, $"{by.name}, not of {jv.name}, released them: " + detail);
					}
					else if (overthrown && !stillHeld && ModsLogHasSince(jailMark, $"{old.name}, once ruler of {jv.name}, is no longer of its faction and is let go."))
					{
						Skip(jailCheck, $"the game took them out of the faction (now {old.faction?.name ?? "none"}): " + detail);
					}
					// The game's own hourly escape roll for anyone Restrained (a Barbarian 50 %, any
					// other sapient 1 %: CharacterClassComponent.PerHour) frees prisoners too.
					else if (overthrown && !stillHeld && HeldUntied.How(old) is string how && how.Contains("CharacterClassComponent.PerHour"))
					{
						Skip(jailCheck, $"they broke free by the game's escape roll ({old.characterClass?.className}): " + detail);
					}
					else Check(jailCheck, () => (overthrown && delivered && stillHeld, detail));
					if (!stillHeld)
					{
						Skip(jailSaveCheck, "not held: " + detail);
						Skip(judgeCheck, "not held: " + detail);
					}
					else
					{
						string saved = null;
						yield return SaveAndRead("ruinarch.plus.unrest.json", (j, e) => saved = j);
						if (saved != null)
						{
							ReplayLoad("ruinarch.plus.unrest.json", saved);
						}
						Check(jailSaveCheck, () => (saved != null && saved.Contains(old.persistentID) && PlusBridge.HeldState(old) == "held",
							$"saved={(saved == null ? "none" : saved.Contains(old.persistentID) ? "with them" : "without them")} after load={PlusBridge.HeldState(old) ?? "not held"}"));
						Character judge = jv.ruler != null && !jv.ruler.isDead && jv.ruler != old ? jv.ruler : jv.owner?.leader as Character;
						int opinion = judge == null ? 0 : judge.relationshipContainer.GetTotalOpinion(old);
						bool grudge = judge != null && judge.relationshipContainer.HasGrudgeAgainst(old);
						string expect = judge == null ? "released" : grudge || opinion <= -50 ? "executed" : opinion < 0 ? "exiled" : "released";
						Try("let two days pass for the prisoner", () => PlusBridge.SetHeldSince(old, PlusBridge.PlusNow - 48 * GameManager.ticksPerHour));
						yield return WaitGameHours(2f, () => PlusBridge.HeldState(old) == null);
						bool happened = expect == "executed" ? old.isDead
							: expect == "exiled" ? !old.isDead && old.faction != jv.owner && !old.traitContainer.HasTrait("Restrained")
							: !old.isDead && old.faction == jv.owner && !old.traitContainer.HasTrait("Restrained");
						Check(judgeCheck, () => (PlusBridge.HeldState(old) == null && happened,
							$"judge {judge?.name ?? "none"} (opinion {opinion}, grudge={grudge}) should have {expect} {old.name}: dead={old.isDead} faction={old.faction?.name ?? "none"} restrained={old.traitContainer.HasTrait("Restrained")} held={PlusBridge.HeldState(old) ?? "no"}"));
					}
				}
			}

			// 4. Civil war: half the village for the ruler, half against, to the death.
			const string warCheck = "a civil war ends with the losing side exiled";
			// Forced, the war skips the roll's size rule; test villages are small. All but two
			// rise (a village rises only when most dislike the ruler), the two stand by them.
			NPCSettlement cv = villages.Where(v => v.ruler != null && !v.ruler.isDead).OrderByDescending(v => AdultsAtHome(v).Count).FirstOrDefault(v => AdultsAtHome(v).Count(c => c != v.ruler) >= 5);
			if (cv == null)
			{
				Skip(warCheck, "no village with 5+ adults at home besides the ruler: " + string.Join("; ", villages.Select(v => $"{v.name} {AdultsAtHome(v).Count}")));
			}
			else
			{
				Character cRuler = cv.ruler;
				int warMark = ModsLogLength();
				List<Character> side = AdultsAtHome(cv).Where(c => c != cRuler).ToList();
				HashSet<Character> rebelSet = new HashSet<Character>(side.Skip(2));
				yield return RiseAs(cv, "CivilWar", c => rebelSet.Contains(c), hurtRuler: false);
				Character warLeader = PlusBridge.UprisingLeader(cv);
				if (PlusBridge.UprisingKind(cv) != "CivilWar" || warLeader == null)
				{
					Skip(warCheck, $"no civil war in {cv.name}: uprising={PlusBridge.UprisingKind(cv) ?? "none"}, ruler now {cv.ruler?.name ?? "none"}");
				}
				else
				{
					yield return WaitGameHours(26f, () => !PlusBridge.HasUprising(cv));
					bool won = ModsLogHasSince(warMark, $"has won the civil war in {cv.name};");
					bool loyalWon = ModsLogHasSince(warMark, $"{cRuler.name} has won the civil war in {cv.name};");
					bool rebelsWon = won && !loyalWon;
					int died = side.Concat(new[] { cRuler }).Count(c => c.isDead);
					string detail = $"leader {warLeader.name}, ruler {cRuler.name}: rebels won={rebelsWon} loyal won={loyalWon}; ruler now {cv.ruler?.name ?? "none"}; "
						+ $"{died} died; leader dead={warLeader.isDead} faction={warLeader.faction?.name ?? "none"}; old ruler dead={cRuler.isDead} faction={cRuler.faction?.name ?? "none"}";
					if (ModsLogHasSince(warMark, $"The civil war in {cv.name} ends with no winner."))
					{
						Skip(warCheck, "no winner: " + detail);
					}
					else Check(warCheck, () =>
						(rebelsWon ? rebelSet.Contains(cv.ruler) && (cRuler.isDead || cRuler.faction != cv.owner)
							: loyalWon && cv.ruler == cRuler && (warLeader.isDead || warLeader.faction != cv.owner), detail));
				}
			}

			foreach (NPCSettlement v in Villages())
			{
				PlusBridge.SetUnrest(v, 0f);
			}
			PlusBridge.SetConfig("unrestEnabled", false);
		}

		// Villagers starving away from home (held in a player building, lost in the wild) do
		// not make their village hungry: the village's food is not what they lack. Two thirds
		// of the village are held restrained and starving outside it past famineHours.
		private IEnumerator CaptivesDoNotStarveTheVillage(NPCSettlement village, List<Character> people)
		{
			// As far from every settlement as the map allows: nearby, villagers passing by free
			// restrained allies (the base game) and they walk home, where they rightly count.
			List<LocationGridTile> centres = GridMap.Instance.mainRegion.settlementsInRegion.OfType<NPCSettlement>()
				.Where(v => v.cityCenter != null).Select(v => v.cityCenter.tiles.First()).ToList();
			LocationGridTile outside = GridMap.Instance.mainRegion.areas.Select(a => a.gridTileComponent.centerGridTile)
				.Where(t => t != null && !t.isOccupied && t.structure.structureType == STRUCTURE_TYPE.WILDERNESS && !t.IsPartOfSettlement())
				.OrderByDescending(t => centres.Count == 0 ? 0f : centres.Min(c => c.GetDistanceTo(t))).FirstOrDefault();
			List<Character> held = people.Where(c => c != village.ruler && !c.isFactionLeader && c.carryComponent.isBeingCarriedBy == null)
				.Take(Math.Max(2, people.Count * 2 / 3)).ToList();
			if (outside == null || held.Count < 2)
			{
				Skip("villagers starving away from home do not put their village in famine", outside == null ? "no free wilderness tile next to the village" : "too few villagers to hold");
				yield break;
			}
			Guard("hold villagers outside the village", () =>
			{
				foreach (Character c in held)
				{
					// Cursed deals true damage every tick. This fixture measures hunger,
					// not death from an unrelated curse while the villagers are held.
					c.traitContainer.RemoveTrait(c, "Cursed");
					CharacterManager.Instance.Teleport(c, outside);
					c.traitContainer.AddTrait(c, "Restrained");
				}
				return village;
			});
			bool hungrySeen = false;
			yield return WaitGameHours(RuinarchPlusFamineHours() + 3f, () =>
			{
				foreach (Character c in held.Where(c => !c.isDead))
				{
					c.traitContainer.RemoveTrait(c, "Cursed");
					// Freed by someone after all: held again, away.
					if (!c.traitContainer.HasTrait("Restrained") || (c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(village)))
					{
						CharacterManager.Instance.Teleport(c, outside);
						c.traitContainer.AddTrait(c, "Restrained");
					}
					c.needsComponent.SetFullness(5f);
				}
				hungrySeen |= PlusBridge.IsHungry(village) || PlusBridge.InFamine(village) == true;
				return false;
			});
			Check("villagers starving away from home do not put their village in famine", () =>
				(held.All(c => !c.isDead && c.homeSettlement == village) && !hungrySeen && PlusBridge.InFamine(village) == false,
					$"{held.Count} of {people.Count} held starving at {outside.localPlace}: hungry seen={hungrySeen} famine={PlusBridge.InFamine(village)} "
					+ string.Join(", ", held.Select(c => $"{c.name}[dead={c.isDead} starving={c.needsComponent.isStarving} restrained={c.traitContainer.HasTrait("Restrained")}]"))));
			Guard("free and feed them", () =>
			{
				LocationGridTile home = village.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? village.cityCenter.tiles.First();
				foreach (Character c in held.Where(c => !c.isDead))
				{
					c.traitContainer.RemoveTrait(c, "Restrained");
					CharacterManager.Instance.Teleport(c, home);
					c.needsComponent.SetFullness(100f);
				}
				return village;
			});
			yield return WaitGameHours(1f, null);
		}

		private static float RuinarchPlusFamineHours() => PlusBridge.Config("famineHours") is int h ? h : 12;

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
			// As Ruinarch+ picks hunters (Hunters.SendHunters): one who can move and act, not carried.
			Func<Character, bool> fighter = c => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient() && c.characterClass.IsCombatant()
				&& (!c.partyComponent.hasParty || !c.partyComponent.currentParty.isActive)
				&& c.hasMarker && c.limiterComponent.canMove && c.limiterComponent.canPerform && c.carryComponent.isBeingCarriedBy == null && !PlusBridge.IsGuard(c);
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
			if (village.residents.Count(fighter) == 0)
			{
				Skip("a village sends hunters after wild animals", "no village has a free fighter left: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			Summon pig = Guard("put a pig near the village", () =>
			{
				Summon s = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Pig, null, homeLocation: null,
					homeRegion: GridMap.Instance.mainRegion, homeStructure: null, className: "", bypassIdeologyChecking: true);
				s.CreateMarker();
				s.InitialCharacterPlacement(wild);
				s.marker.UpdatePosition();
				Watched.Add(s);
				return s;
			});
			int sent = pig == null ? -1 : PlusBridge.SendHunters(village);
			Check("a village sends hunters after wild animals", () =>
				(sent > 0, $"{village.name} sent={sent}; fighters: {string.Join(", ", village.residents.Where(fighter).Select(c => $"{c.name} ({c.characterClass.className} marker={c.hasMarker} move={c.limiterComponent.canMove} perform={c.limiterComponent.canPerform} party={c.partyComponent.currentParty?.isActive} carried={c.carryComponent.isBeingCarriedBy != null} hunting={c.jobQueue.HasJob(JOB_TYPE.HUNT_PREY)})"))}"));
			if (sent > 0)
			{
				int huntMark = ModsLogLength();
				yield return WaitGameHours(16f, () => ModsLogHas($"meat home to {village.name}"));
				if (!ModsLogHas($"meat home to {village.name}") && ModsLogHasSince(huntMark, "died while hunting") && !ModsLogHasSince(huntMark, "came back from hunting"))
				{
					Skip("the hunters kill, butcher and carry the meat home", $"the hunter died on the trip (pig dead={pig.isDead})");
				}
				else Check("the hunters kill, butcher and carry the meat home", () =>
					(ModsLogHas($"meat home to {village.name}"), $"pig dead={pig.isDead} marker={pig.hasMarker} at {pig.gridTileLocation?.localPlace}; still hunting: {string.Join(", ", village.residents.Where(PlusBridge.IsHunting).Select(Busy))}"));
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
			// The sender needs someone able to go (a village of one hurt ruler once sent nobody).
			Func<NPCSettlement, int> able = v => v.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !PlusBridge.IsChild(r)
				&& r.hasMarker && r.limiterComponent.canMove && r.limiterComponent.canPerform && !PlusBridge.IsGuard(r));
			foreach (NPCSettlement a in villages.Where(v => able(v) >= 2).OrderByDescending(able))
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
			// Up to half a day: at night the village's people are asleep and nobody sets out.
			Character trader = null;
			// Hungry villagers eat from the storage while the test waits (settlers fleeing a
			// famine were seen to empty it): keep a pile the trader can take there.
			yield return WaitGameHours(12f, () =>
			{
				if (!from.mainStorage.pointsOfInterest.OfType<ResourcePile>().Any(p => p.providedResource == RESOURCE.FOOD && p.resourceInPile >= 40 && p.characterOwner == null))
				{
					LocationGridTile spot = from.mainStorage.GetRandomUnoccupiedTile();
					if (spot != null)
					{
						FoodPile more = InnerMapManager.Instance.CreateNewTileObject<FoodPile>(TILE_OBJECT_TYPE.ANIMAL_MEAT);
						more.SetResourceInPile(100);
						from.mainStorage.AddPOI(more, spot);
					}
				}
				return (trader = PlusBridge.SendTrader(from, to, 40)) != null;
			});
			LocationGridTile dest = to.mainStorage.passableTiles.FirstOrDefault();
			Check("a village with food to spare sends a trader", () => (trader != null, trader == null
				? $"nobody went; {from.name}'s storage: free tiles={from.mainStorage.tiles.Count(t => !t.isOccupied)}, food piles "
					+ string.Join(" ", from.mainStorage.pointsOfInterest.OfType<ResourcePile>().Where(p => p.providedResource == RESOURCE.FOOD)
						.Select(p => $"[{p.resourceInPile} owner={p.characterOwner?.name ?? "-"} carried={p.isBeingCarriedBy != null} state={p.mapObjectState} haulJob={p.HasJobTargetingThis(JOB_TYPE.HAUL)}]"))
					+ "; villagers: " + string.Join(", ", from.residents.Where(c => c != null && !c.isDead && c.isNormalCharacter).Select(c =>
					$"{c.name}[ruler={c == from.ruler} leader={c.isFactionLeader} move={c.limiterComponent.canMove} perform={c.limiterComponent.canPerform} party={c.partyComponent.hasParty} starving={c.needsComponent.isStarving} haul={c.jobQueue.HasJob(JOB_TYPE.HAUL)} hunting={PlusBridge.IsHunting(c)} inside={c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(from)} marker={c.hasMarker} child={PlusBridge.IsChild(c)} path={dest == null || c.movementComponent.HasPathToEvenIfDiffRegion(dest)}]"))
				: $"{trader.name} ({trader.characterClass.className})"));
			if (trader != null)
			{
				Log($"  trader set out: {Busy(trader)}");
				_tradeCargo = from.mainStorage.pointsOfInterest.OfType<ResourcePile>().FirstOrDefault(p => p.characterOwner == trader && p.providedResource == RESOURCE.FOOD);
				_tradeDestination = to.mainStorage;
				_tradeArrived = false;
				int tripMark = ModsLogLength();
				yield return WaitGameHours(30f, () => ModsLogHas($"brought 40 food to {to.name}") || trader.isDead);
				if (trader.isDead && !ModsLogHas($"{trader.name} of {from.name} brought 40 food to {to.name}"))
				{
					string cause = trader.deathLog?.logText ?? "no death log";
					Skip("the trader brings the food to the other village", $"{trader.name} died on the way: {cause}");
					if (news) Skip("the trader tells the other faction what theirs knows", $"{trader.name} died on the way");
					news = false;
				}
				else Check("the trader brings the food to the other village", () =>
					(_tradeArrived && ModsLogHas($"{trader.name} of {from.name} brought 40 food to {to.name}"),
					$"deposited in destination={_tradeArrived}; {Busy(trader)} dead={trader.isDead} carrying={trader.carryComponent.carriedPOI?.name ?? "nothing"} haul={trader.jobQueue.HasJob(JOB_TYPE.HAUL)}; {ModsLogLineSince(tripMark, $"The trip of {trader.name} from") ?? "the trip has not ended"}"));
				if (news && trader.isAlliedWithPlayer)
				{
					// Demon cultists keep the player's secrets (Knowledge.Counts).
					Skip("the trader tells the other faction what theirs knows", $"{trader.name} is on the player's side and tells nobody");
				}
				else if (news && !to.residents.Any(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !r.isAlliedWithPlayer))
				{
					// A faction knows through its people (Knowledge.CanRemember, Counts): with none
					// who can remember or who are not on the player's side, nobody there can learn.
					Skip("the trader tells the other faction what theirs knows", $"nobody in {to.name} can learn it: "
						+ string.Join(", ", to.residents.Where(r => r != null && !r.isDead).Select(r => $"{r.name}[{r.race} normal={r.isNormalCharacter} allied={r.isAlliedWithPlayer}]")));
				}
				else if (news)
				{
					Check("the trader tells the other faction what theirs knows", () =>
						(PlusBridge.Knows(to.owner, portal), $"{to.owner.name} know of the portal={PlusBridge.Knows(to.owner, portal)}; {trader.name} remembers={PlusBridge.Remembers(trader, portal)};"
							+ $" {to.name} residents who could learn: {to.residents.Count(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !r.isAlliedWithPlayer)}"));
				}
			}
			_tradeCargo = null;
			_tradeDestination = null;
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

		// What a villager is up to, for failure details: job, action, queued jobs with their
		// ranks, where they are and where home is.
		private static string Busy(Character c)
		{
			if (c == null)
			{
				return "nobody";
			}
			string where = c.gridTileLocation?.area?.GetFirstNPCSettlementOnArea()?.name ?? "the wild";
			return $"{c.name}[job={c.currentJob?.jobType.ToString() ?? "none"} doing={c.currentActionNode?.goapName ?? "nothing"}"
				+ $" queue={string.Join("/", c.jobQueue.jobsInQueue.Select(j => $"{j.jobType}:{j.priority}"))} topBehaviour={c.behaviourComponent.GetHighestBehaviourPriority()}"
				+ $" at {c.gridTileLocation?.localPlace} in {where}/{c.currentStructure?.name ?? "-"} home={c.homeSettlement?.name ?? "none"}/{c.homeStructure?.name ?? "none"}"
				+ $" class={c.characterClass?.className} schedule={c.dailyScheduleComponent.schedule?.GetScheduleType(GameManager.Instance.Today().tick)}]";
		}

		// Kills a living resident inside (or next to) the village, bringing an eligible one home if needed.
		private Character KillResident(NPCSettlement village)
		{
			// Prefer someone standing on village tiles (settlement burial path); fall back to
			// the village border (where the personal outside-village burial path applies).
			// Never a member of an active party: the game has a party bury its own fallen where
			// they lie (BURY_IN_ACTIVE_PARTY, a wilderness grave), which no burial test is about.
			// Villagers only: some villages house monsters (a Golem, a Scorpion).
			Func<Character, bool> able = r => r != null && !r.isDead && r.gridTileLocation != null && r.isNormalCharacter && r.race.IsSapient()
				&& r != village.ruler && (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive);
			Character victim = village.residents.Where(r => able(r) && r.gridTileLocation.IsPartOfSettlement(village))
				.OrderBy(r => r.partyComponent.hasParty).FirstOrDefault()
				?? village.residents.Where(r => able(r) && r.gridTileLocation.IsNextToOrPartOfSettlement(village))
				.OrderBy(r => r.partyComponent.hasParty).FirstOrDefault();
			if (victim == null)
			{
				// An errand can take every eligible resident away before the suite starts.
				victim = village.residents.Where(able).OrderBy(r => r.partyComponent.hasParty).FirstOrDefault();
				if (victim != null)
				{
					LocationGridTile home = village.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied);
					if (home == null) return null;
					CharacterManager.Instance.Teleport(victim, home);
				}
			}
			if (victim == null)
			{
				return null;
			}
			LocationGridTile at = victim.gridTileLocation;
			string party = victim.partyComponent.hasParty ? $"party {victim.partyComponent.currentParty.partyName} (inactive)" : "no party";
			victim.Death("autotest");
			Log($"  killed {victim.name} at {at} (inside village={at.IsPartOfSettlement(village)}, {party})");
			return victim;
		}

		// Kills a living resident standing next to, but not on, the village's tiles. Not a
		// member of an active party: their companions bury them where they fell (the game's
		// BURY_IN_ACTIVE_PARTY), which is not the personal burial path tested here.
		private Character KillResidentOnBorder(NPCSettlement village)
		{
			Func<Character, bool> partyless = r => !r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive;
			Character victim = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.gridTileLocation != null && partyless(r)
				&& !r.gridTileLocation.IsPartOfSettlement() && r.gridTileLocation.IsNextToOrPartOfSettlement(village) && r != village.ruler);
			if (victim == null)
			{
				// Nobody is out there right now: move someone onto a free tile one step outside
				// the village's structures (the border ring lies in neighbouring areas, since a
				// village claims its whole area), then kill them there.
				LocationGridTile edge = GridMap.Instance.mainRegion.areas.SelectMany(a => a.gridTileComponent.gridTiles)
					.FirstOrDefault(t => t != null && !t.isOccupied && !t.IsPartOfSettlement() && t.IsNextToOrPartOfSettlement(village)
						&& t.structure is Wilderness);
				victim = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.gridTileLocation != null && r != village.ruler && r.marker != null && partyless(r));
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
			// A world whose villagers have ages but whose creatures were never aged (creature
			// ageing off until now; start from no ages, the harness may have run an hour with
			// the life cycle on). A save and load in that state must not mark the creatures
			// aged, or switching creature ageing on ages every one of them as a newborn.
			ReplayLoad("ruinarch.plus.life.json", "");
			object creatureLife = PlusBridge.Config("creatureLifeEnabled");
			PlusBridge.SetConfig("creatureLifeEnabled", false);
			PlusBridge.SetConfig("lifeCycleEnabled", true);
			yield return WaitGameHours(1f, null);
			PlusBridge.SetConfig("lifeCycleEnabled", false);
			PlusBridge.SetConfig("creatureLifeEnabled", creatureLife ?? true);
			// Judged on the creatures the save held: one born or spawned after the next hourly
			// check has no age until the one after (four did in a full run), which says nothing
			// about the saved creatures being aged as newborns.
			HashSet<Character> saved = new HashSet<Character>(CharacterManager.Instance.allCharacters.Where(PlusBridge.IsCreature));
			string unaged = null;
			yield return SaveAndRead("ruinarch.plus.life.json", (j, e) => unaged = j);
			if (unaged != null)
			{
				ReplayLoad("ruinarch.plus.life.json", unaged);
			}
			PlusBridge.SetConfig("lifeCycleEnabled", true);
			yield return WaitGameHours(1f, null);
			List<Character> now = CharacterManager.Instance.allCharacters.Where(PlusBridge.IsCreature).ToList();
			List<string> seeded = now.Where(saved.Contains).Select(c => PlusBridge.LifeStage(c) ?? "none").ToList();
			int newcomers = now.Count(c => !saved.Contains(c)), newcomersUnaged = now.Count(c => !saved.Contains(c) && PlusBridge.LifeStage(c) == null);
			Check("creatures saved and loaded before the life cycle is on still get grown ages", () =>
				(seeded.Count > 0 && seeded.All(s => s != "none") && seeded.Count(s => s != "Young") * 2 >= seeded.Count,
				(unaged == null ? "no life entry; " : unaged.Contains("V|2") ? "saved as aged; " : "saved unaged; ") + $"{seeded.Count} saved creatures: " + string.Join(", ", seeded.GroupBy(s => s).Select(g => $"{g.Key} {g.Count()}"))
				+ $"; {newcomers} newer (not judged, {newcomersUnaged} without an age yet)"));
			yield return LifeChecks();
			yield return MemoryChecks();
			yield return CreatureChecks();
			PlusBridge.SetConfig("lifeCycleEnabled", false);
			PlusBridge.SetConfig("pregnancyDays", 4);
		}

		// Creatures age (Phase 4 creature life): the world's creatures start with ages, old
		// ones die of age, the young are drawn smaller, and kinds the game never replaces
		// refill their groups up to the size they had, never past it; all of it saved.
		private IEnumerator CreatureChecks()
		{
			List<Character> creatures = CharacterManager.Instance.allCharacters.Where(PlusBridge.IsCreature).ToList();
			if (creatures.Count == 0)
			{
				Skip("creatures have ages", "no living natural creature in the world");
				yield break;
			}
			List<string> stages = creatures.Select(c => PlusBridge.LifeStage(c) ?? "none").ToList();
			Check("the world's creatures have ages, most of them grown", () =>
				(stages.All(s => s != "none") && stages.Count(s => s != "Young") * 2 >= stages.Count,
				$"{creatures.Count} creatures: " + string.Join(", ", stages.GroupBy(s => s).Select(g => $"{g.Key} {g.Count()}"))));

			// The young are drawn smaller, and the creature panel's Age row says so.
			Summon pup = creatures.OfType<Summon>().FirstOrDefault(c => c.hasMarker && c.gridTileLocation != null && c.adultSummonType == SUMMON_TYPE.None && c.race != RACE.DRAGON);
			float pupAge = -1f;
			if (pup == null)
			{
				Skip("a creature's young is drawn smaller and its panel says young", "no creature on the map");
			}
			else
			{
				float span = PlusBridge.CreatureLifespan(pup.race);
				SpriteRenderer pupBody = AccessTools.Field(typeof(CharacterMarker), "mainImg").GetValue(pup.marker) as SpriteRenderer;
				PlusBridge.SetAgeYears(pup, span / 2f);
				yield return WaitGameHours(2f, () => !PlusBridge.IsSmall(pup));
				float grown = pupBody != null ? pupBody.transform.localScale.x : -1f;
				PlusBridge.SetAgeYears(pup, 0f);
				yield return WaitGameHours(2f, () => PlusBridge.IsSmall(pup));
				float small = pupBody != null ? pupBody.transform.localScale.x : -1f;
				MonsterInfoUI monsterPanel = AccessTools.FieldRefAccess<UIManager, MonsterInfoUI>("monsterInfoUI")(UIManager.Instance);
				string[] row = Guard("open the young creature's panel", () => { UIManager.Instance.ShowCharacterInfo(pup); return AgeRow(monsterPanel); });
				Guard("close the panel", () => { monsterPanel.CloseMenu(); return pup; });
				Check("a creature's young is drawn smaller and its panel says young", () =>
					(PlusBridge.LifeStage(pup) == "Young" && grown > 0f && Mathf.Abs(small / grown - 0.6f) < 0.02f && row != null && row[1] == "0, young",
					$"{pup.name} ({pup.race}): stage={PlusBridge.LifeStage(pup)} scale {grown:F2} -> {small:F2} row={(row == null ? "none" : row[1])}"));
				PlusBridge.SetAgeYears(pup, span / 2f);
				pupAge = PlusBridge.AgeYears(pup);
			}

			// Old age: a creature past its age of death dies at the next hour.
			Character old = creatures.FirstOrDefault(c => c != pup && !c.isDead && c.hasMarker && !PlusBridge.IsBreeder(c.race));
			if (old == null)
			{
				Skip("a creature dies of old age", "no second creature on the map");
			}
			else
			{
				string oldName = old.name;
				PlusBridge.SetAgeYears(old, 30f);
				PlusBridge.SetDeathAgeYears(old, 30f);
				yield return WaitGameHours(2f, () => old.isDead);
				Check("a creature dies of old age", () =>
					(old.isDead && ModsLogHas($"{oldName} died of old age at 30."), $"{oldName} ({old.race}) dead={old.isDead}"));
			}

			// Kinds the game never replaces: a group with a grown pair refills up to its size.
			// Genders are a coin toss, so a group may be all one sex (it then never refills):
			// the test makes one of the largest group the other sex.
			IGrouping<string, Character> group = creatures.Where(c => !c.isDead && PlusBridge.IsBreeder(c.race)).GroupBy(PlusBridge.GroupOf)
				.Where(g => g.Key != null && g.Count() >= 2).OrderByDescending(g => g.Count()).FirstOrDefault();
			string key = group?.Key;
			int size = PlusBridge.GroupSize(key);
			List<Character> Members() => CharacterManager.Instance.allCharacters.Where(c => PlusBridge.IsCreature(c) && PlusBridge.GroupOf(c) == key).ToList();
			if (group == null)
			{
				Skip("a creature group never grows past the size it had", "no group of two or more of a kind the game never replaces; kinds here: "
					+ string.Join(", ", creatures.GroupBy(c => c.race).Select(g => $"{g.Key} {g.Count()}")));
			}
			else
			{
				GENDER? lone = group.All(c => c.gender == GENDER.FEMALE) ? GENDER.MALE : group.All(c => c.gender == GENDER.MALE) ? GENDER.FEMALE : (GENDER?)null;
				if (lone != null)
				{
					Character changed = group.Last();
					Guard("make one of the group the other sex", () => { AccessTools.Field(typeof(Character), "_gender").SetValue(changed, lone.Value); return changed; });
				}
				Character father = group.First(c => c.gender == GENDER.MALE);
				Character mother = group.First(c => c.gender == GENDER.FEMALE);
				float half = PlusBridge.CreatureLifespan(mother.race) / 2f;
				PlusBridge.SetAgeYears(father, half);
				PlusBridge.SetAgeYears(mother, half);
				// Filled to its size first (it may have lost some already), then it must not grow.
				for (int i = 0; i < MaxGroupFill && Members().Count < size; i++)
				{
					Guard("let the group fill up", () => PlusBridge.BreedNow());
				}
				int full = Members().Count;
				List<Summon> extra = Guard("breed a group at its size", () => PlusBridge.BreedNow());
				Check("a creature group never grows past the size it had", () =>
					(size >= 2 && Members().Count == full && (extra == null || extra.All(y => PlusBridge.GroupOf(y) != key)),
					$"{key}: size {size}, {full} -> {Members().Count}"));
				Character victim = Members().FirstOrDefault(c => c != father && c != mother);
				if (victim == null)
				{
					Skip("a creature group that lost one has a young of its kind, home and faction", $"{key} is only its pair");
				}
				else
				{
					Guard("kill one of the group", () => { victim.Death("autotest"); return victim; });
					List<Summon> born = Guard("let the group breed", () => PlusBridge.BreedNow());
					Summon young = born?.FirstOrDefault(y => PlusBridge.GroupOf(y) == key);
					Check("a creature group that lost one has a young of its kind, home and faction", () =>
						(young != null && young.summonType == ((Summon)mother).summonType && young.faction == mother.faction && PlusBridge.LifeStage(young) == "Young"
							&& PlusBridge.IsSmall(young) && young.hasMarker && Members().Count == full && ModsLogHas($"had a young: {young.name}"),
						young == null ? $"{key}: no young ({born?.Count ?? 0} born elsewhere)"
							: $"{young.name} ({young.summonType}) group={PlusBridge.GroupOf(young)} faction={young.faction?.name} stage={PlusBridge.LifeStage(young)} small={PlusBridge.IsSmall(young)}; {Members().Count} of {size}"));
				}
			}

			// Creature ages and group sizes ride inside the save.
			string json = null;
			yield return SaveAndRead("ruinarch.plus.life.json", (j, e) => json = j);
			Check("creature ages and group sizes are stored inside the player's save file", () =>
				(json != null && json.Contains("V|2") && (key == null || json.Contains("\"G|")) && (pup == null || json.Contains(pup.persistentID)),
				json == null ? "no life entry" : $"{json.Length} bytes"));
			if (json != null)
			{
				ReplayLoad("ruinarch.plus.life.json", json);
				Check("creature ages and group sizes come back when the save loads", () =>
					((pup == null || Mathf.Abs(PlusBridge.AgeYears(pup) - pupAge) < 0.05f) && PlusBridge.GroupSize(key) == size,
					$"{pup?.name}: {pupAge:F2} -> {(pup == null ? -1f : PlusBridge.AgeYears(pup)):F2}; {key}: size {size} -> {PlusBridge.GroupSize(key)}"));
			}
		}

		// A group refills one young per breeding at most; its size is at most 6.
		private const int MaxGroupFill = 6;

		private static List<Character> Sapients(NPCSettlement v)
		{
			return v.residents.Where(c => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient()).ToList();
		}

		// Knowledge lives in people (Phase 3 memory, Phase 4 dementia): news comes home to one
		// village, reaches the faction's other villages only with someone who remembers it,
		// and a village whose last rememberers forget no longer knows (announced).
		private IEnumerator MemoryChecks()
		{
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			Faction faction = FactionManager.Instance.allFactions.Where(f => f != null && f.isMajorNonPlayer)
				.OrderByDescending(f => Villages().Count(v => v.owner == f && Sapients(v).Count >= 2)).FirstOrDefault();
			List<NPCSettlement> villages = Villages().Where(v => v.owner == faction && Sapients(v).Count >= 2).ToList();
			if (portal == null || villages.Count == 0)
			{
				Skip("memory", portal == null ? "no Portal" : "no village with two villagers");
				yield break;
			}
			bool aware = faction.isAwareOfPlayer;
			NPCSettlement home = villages[0];
			PlusBridge.Forget(faction);

			if (villages.Count >= 2)
			{
				NPCSettlement other = villages[1];
				Character witness = Sapients(home).FirstOrDefault(c => !_lifeFamily.Contains(c) && c.hasMarker && c.carryComponent.isBeingCarriedBy == null
					&& c != home.ruler && !c.isFactionLeader);
				LocationGridTile homeTile = home.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? home.cityCenter.passableTiles.FirstOrDefault();
				LocationGridTile otherTile = other.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? other.cityCenter.passableTiles.FirstOrDefault();
				if (witness != null && homeTile != null && otherTile != null)
				{
					Guard("a villager sees the Portal and is sent home", () =>
					{
						witness.partyComponent.currentParty?.RemoveMember(witness);
						PlusBridge.Witness(witness, portal);
						CharacterManager.Instance.Teleport(witness, homeTile);
						return witness;
					});
					yield return WaitGameHours(2f, () => PlusBridge.VillageKnows(home, portal));
					Check("news brought home is known in that village, not in the faction's others", () =>
						(PlusBridge.VillageKnows(home, portal) && !PlusBridge.VillageKnows(other, portal) && PlusBridge.Knows(faction, portal)
							&& Sapients(home).All(r => PlusBridge.Remembers(r, portal)),
						$"{home.name} knows={PlusBridge.VillageKnows(home, portal)} ({Sapients(home).Count(r => PlusBridge.Remembers(r, portal))} of {Sapients(home).Count} remember); {other.name} knows={PlusBridge.VillageKnows(other, portal)}"));
					Guard("the witness walks into another village of the faction", () =>
					{
						witness.partyComponent.currentParty?.RemoveMember(witness);
						CharacterManager.Instance.Teleport(witness, otherTile);
						return witness;
					});
					// Held there until the hourly delivery (they would walk home first otherwise).
					yield return WaitGameHours(2f, () =>
					{
						NPCSettlement at = witness.currentSettlement as NPCSettlement ?? witness.gridTileLocation?.area?.GetFirstNPCSettlementOnArea();
						if (at != other && !witness.isDead)
						{
							CharacterManager.Instance.Teleport(witness, otherTile);
						}
						return PlusBridge.VillageKnows(other, portal);
					});
					// Anyone of the witness's village who remembers may be standing there first.
					Check("someone who remembers tells the faction's other village they stand in", () =>
						(PlusBridge.VillageKnows(other, portal) && ModsLogHas($" brought word of {portal.name} to {other.name}"),
						$"{other.name} knows={PlusBridge.VillageKnows(other, portal)}; {witness.name} at {witness.gridTileLocation?.localPlace} in {witness.currentSettlement?.name ?? "the wild"}"));
				}
				else
				{
					Skip("news is known village by village", $"no free villager or square tile in {home.name} / {other.name}");
				}
			}
			else
			{
				Skip("news is known village by village", $"{faction.name} has only one village with two villagers");
			}

			// Dementia: an adult who becomes an elder may grow forgetful (certain here); the
			// panel says so; the flag rides inside the save.
			PlusBridge.SetConfig("dementiaChance", 100);
			Character elder = Sapients(home).FirstOrDefault(c => !_lifeFamily.Contains(c) && PlusBridge.LifeStage(c) == "Adult" && !PlusBridge.IsForgetful(c));
			if (elder != null)
			{
				PlusBridge.SetAgeYears(elder, elder.race == RACE.ELVES ? 12.2f : 4.2f);
				yield return WaitGameHours(2f, () => PlusBridge.IsForgetful(elder));
				Check("an adult who becomes an elder may grow forgetful", () =>
					(PlusBridge.IsForgetful(elder) && ModsLogHas($"{elder.name} has grown forgetful with age."), $"{elder.name} stage={PlusBridge.LifeStage(elder)} forgetful={PlusBridge.IsForgetful(elder)}"));
				CharacterInfoUI panel = AccessTools.FieldRefAccess<UIManager, CharacterInfoUI>("characterInfoUI")(UIManager.Instance);
				string label = Guard("open the elder's panel", () =>
				{
					UIManager.Instance.ShowCharacterInfo(elder, centerOnCharacter: false);
					return (AccessTools.Field(typeof(CharacterInfoUI), "subLbl").GetValue(panel) as TMPro.TMP_Text)?.text;
				});
				Guard("close the elder's panel", () => { panel.CloseMenu(); return elder; });
				Check("the character panel says an elder is forgetful", () => (label != null && label.Contains(", elder, forgetful, age "), $"\"{label}\""));
				string json = null;
				yield return SaveAndRead("ruinarch.plus.life.json", (j, e) => json = j);
				if (json != null)
				{
					PlusBridge.SetForgetful(elder, false);
					ReplayLoad("ruinarch.plus.life.json", json);
					Check("dementia comes back when the save loads", () => (PlusBridge.IsForgetful(elder), $"{elder.name} forgetful after load={PlusBridge.IsForgetful(elder)}"));
				}
			}
			else
			{
				Skip("an adult who becomes an elder may grow forgetful", $"no adult in {home.name}");
			}
			PlusBridge.SetConfig("dementiaChance", 33);

			// Forgetting: every villager of the faction grows forgetful (so nobody from another of
			// its villages walks in and tells it again); each forgets the one building they
			// know, and the village no longer knowing it is announced. Memory alone: with records
			// on, a villager at home reads it back from the household's Book (RecordsSuite).
			object records = PlusBridge.Config("recordsEnabled");
			PlusBridge.SetConfig("recordsEnabled", false);
			PlusBridge.Forget(faction);
			PlusBridge.Learn(faction, portal);
			yield return WaitGameHours(1.1f, null);
			List<Character> residents = Sapients(home);
			List<Character> everyone = faction.ownedSettlements.OfType<NPCSettlement>().SelectMany(Sapients).ToList();
			foreach (Character r in everyone)
			{
				PlusBridge.SetForgetful(r, true);
			}
			yield return WaitGameHours(3f, () => !PlusBridge.VillageKnows(home, portal) && ModsLogHas($"Nobody in {home.name} remembers {portal.name} any more."));
			Check("forgetful villagers forget, and a village that no longer remembers is announced", () =>
				(!PlusBridge.VillageKnows(home, portal) && ModsLogHas($"Nobody in {home.name} remembers {portal.name} any more.")
					&& residents.Any(r => ModsLogHas($"{r.name} is forgetful and no longer remembers {portal.name}.")),
				$"{home.name} knows={PlusBridge.VillageKnows(home, portal)} ({residents.Count(r => PlusBridge.Remembers(r, portal))} of {residents.Count} remember)"
					+ (villages.Count >= 2 ? $"; {villages[1].name} knows={PlusBridge.VillageKnows(villages[1], portal)}" : "")));
			foreach (Character r in everyone)
			{
				PlusBridge.SetForgetful(r, false);
			}
			if (records != null)
			{
				PlusBridge.SetConfig("recordsEnabled", records);
			}
			if (elder != null)
			{
				PlusBridge.SetForgetful(elder, false);
			}

			// Cultists (Demon Worship, on the player's side) remember but do not count: a village
			// whose every rememberer is a cultist does not know. Within one frame, then undone.
			// Not in the faction leader's village: a leader who turns cultist takes the whole
			// faction into a Demon Cult (FactionManager.FactionLeaderCultistProcessing).
			PlusBridge.Learn(faction, portal);
			NPCSettlement cultHome = villages.FirstOrDefault(v => !Sapients(v).Contains(faction.leader as Character) && Sapients(v).Any(r => PlusBridge.Remembers(r, portal)));
			if (cultHome == null)
			{
				Skip("what cultists remember does not count for their village", $"{faction.name}'s only villages with news are the leader's");
			}
			else
			{
				List<Character> converts = Sapients(cultHome).Where(r => PlusBridge.Remembers(r, portal) && !r.traitContainer.HasTrait("Demon Cultist")).ToList();
				bool knewBefore = PlusBridge.VillageKnows(cultHome, portal);
				// Only the trait: it is what makes someone a cultist on the player's side. Changing
				// the religion as well would make a faction with a religion ideology exile them
				// (ReligionComponent.ProcessOnChangeReligion). The trait brings Nocturnal; undone too.
				HashSet<Character> nocturnal = new HashSet<Character>(converts.Where(r => r.traitContainer.HasTrait("Nocturnal")));
				Guard("make everyone who remembers a cultist", () =>
				{
					foreach (Character r in converts)
					{
						r.traitContainer.AddTrait(r, "Demon Cultist");
					}
					return cultHome;
				});
				bool knewAsCult = PlusBridge.VillageKnows(cultHome, portal);
				bool allied = converts.All(r => r.isAlliedWithPlayer);
				Guard("turn them back", () =>
				{
					foreach (Character r in converts)
					{
						r.traitContainer.RemoveTrait(r, "Demon Cultist");
						if (!nocturnal.Contains(r))
						{
							r.traitContainer.RemoveTrait(r, "Nocturnal");
						}
					}
					return cultHome;
				});
				bool knewAfter = PlusBridge.VillageKnows(cultHome, portal);
				Check("what cultists remember does not count for their village", () =>
					(converts.Count > 0 && knewBefore && allied && !knewAsCult && knewAfter,
					$"{cultHome.name}: {converts.Count} remember; knows before={knewBefore}, all cultists (allied={allied})={knewAsCult}, turned back={knewAfter}; "
						+ string.Join(", ", converts.Select(r => $"{r.name}[allied={r.isAlliedWithPlayer} cultist={r.traitContainer.HasTrait("Demon Cultist")} faction={r.faction?.name} home={r.homeSettlement?.name} remembers={PlusBridge.Remembers(r, portal)}]"))));
			}

			// Leave the world as found (see KnowledgeSuite).
			PlusBridge.Forget(faction);
			CallOffCounterattacks(faction);
			Guard("restore the faction's awareness", () => { faction.SetIsAwareOfPlayer(aware); return faction; });
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
			if (child == null && mother.isDead)
			{
				Skip("the child is born in the mother's home", $"{mother.name} died while pregnant (see the death lines above)");
			}
			else Check("the child is born in the mother's home", () =>
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
			string[] childRow = AgeRow(panel);
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
			// The Info tab open (a toggle labelled "Info", as a player would click), to see the Age row.
			Guard("open the Info tab", () =>
			{
				// As if the player had paid to reveal the child's info (the tab is locked otherwise).
				child.isInfoUnlocked = true;
				UIManager.Instance.ShowCharacterInfo(child);
				UnityEngine.UI.Toggle info = panel.GetComponentsInChildren<UnityEngine.UI.Toggle>(true)
					.FirstOrDefault(t => t.GetComponentInChildren<TMPro.TMP_Text>(true)?.text == "Info");
				if (info != null) info.isOn = true;
				return info;
			});
			yield return new WaitForSecondsRealtime(1f);
			yield return Screenshot("agerow.png");
			cam.orthographicSize = zoom;
			Guard("close the child's panel", () => { panel.CloseMenu(); return child; });
			Check("the character panel gives the age", () => (label == "Child, age 0", $"\"{label}\""));
			Check("the panel's Info tab has an Age row", () =>
				(childRow != null && childRow[0] == "Age" && childRow[1] == "0, child", childRow == null ? "no Age row" : $"\"{childRow[0]}\": \"{childRow[1]}\""));
			// Someone with no age at all (undead, a demon, a construct, the player's minion): "Unknown".
			Character ageless = GridMap.Instance.mainRegion.charactersAtLocation.FirstOrDefault(c => c != null && !c.isDead && c.hasMarker && PlusBridge.LifeStage(c) == null && !PlusBridge.IsCreature(c));
			if (ageless == null)
			{
				Skip("a character without an age shows it as unknown", "no living character outside the life cycle on the map");
			}
			else
			{
				// As the game routes it (UIManager.ShowCharacterInfo): normal characters open the
				// character panel, everyone else (creatures, the player's demons) the monster panel.
				MonsterInfoUI monsterPanel = AccessTools.FieldRefAccess<UIManager, MonsterInfoUI>("monsterInfoUI")(UIManager.Instance);
				Component shown = ageless.isNormalCharacter ? (Component)panel : monsterPanel;
				string[] agelessRow = Guard("open an ageless character's panel", () => { UIManager.Instance.ShowCharacterInfo(ageless); return AgeRow(shown); });
				Guard("close the panel", () => { if (ageless.isNormalCharacter) panel.CloseMenu(); else monsterPanel.CloseMenu(); return ageless; });
				Check("a character without an age shows it as unknown", () =>
					(agelessRow != null && agelessRow[1] == "Unknown", $"{ageless.name} ({ageless.race}): {(agelessRow == null ? "no Age row" : agelessRow[1])}"));
			}

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
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies().Where(a => PlusBridge.SafeName(a) == "Assembly-CSharp" || PlusBridge.SafeName(a) == "AstarPathfindingProject"))
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
			Log($"  after learning: {string.Join(", ", faction.ownedSettlements.OfType<NPCSettlement>().Select(v => $"{v.name}: " + string.Join(" ", v.residents.Where(r => r != null && !r.isDead).Select(r => $"{r.name}[normal={r.isNormalCharacter} sapient={r.race.IsSapient()} allied={r.isAlliedWithPlayer} faction={r.faction?.name} remembers={PlusBridge.Remembers(r, outpost)}]"))))}");

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
			// Done with the counterattack: call it off now, and take the outpost down.
			CallOffCounterattacks(faction);
			Guard("remove the outpost", () => RemoveTestBuilding(outpost));
			// Its members are still by the outpost, fighting the Portal's defenders (who defend
			// the player's buildings, as in the base game): out of the fight and home, or they
			// die there and the witness checks below lose their villagers.
			Guard("send the counterattackers home", () =>
			{
				Faction demons = PlayerManager.Instance.player.playerFaction;
				foreach (Character r in Villages().Where(v => v.owner == faction).SelectMany(v => v.residents).Where(r => r != null && !r.isDead && r.hasMarker
					&& r.homeSettlement?.cityCenter != null && r.combatComponent.hostilesInRange.Any(h => h is Character c && c.faction == demons)).ToList())
				{
					r.partyComponent.currentParty?.RemoveMember(r);
					r.combatComponent.ClearHostilesInRange(false);
					r.combatComponent.ClearAvoidInRange(false);
					CharacterManager.Instance.Teleport(r, r.homeSettlement.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? r.homeSettlement.cityCenter.tiles.First());
				}
				return faction;
			});

			// Seeing is not knowing: a villager of the (aware) faction who sees the portal carries
			// the news; their faction learns it only when they get home alive.
			PlusBridge.Forget(faction);
			// Villagers at home and not fighting: the called-off counterattack's members are
			// still out by the removed outpost, in the Portal defenders' fight.
			List<Character> witnesses = Villages().Where(v => v.owner == faction).SelectMany(v => v.residents).Where(r => r != null && !r.isDead && r.marker != null && r.limiterComponent.canWitness
				&& r.race.IsSapient() && r.isNormalCharacter && !r.isAlliedWithPlayer && (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive) && r.carryComponent.isBeingCarriedBy == null
				&& r != r.homeSettlement?.ruler && !r.isFactionLeader && r.homeSettlement?.cityCenter != null
				&& !r.combatComponent.isInCombat && r.gridTileLocation != null && r.gridTileLocation.IsPartOfSettlement(r.homeSettlement)).Take(2).ToList();
			LocationGridTile near = portal.tiles.SelectMany(t => t.neighbourList).FirstOrDefault(t => t != null && t.structure != portal && !t.isOccupied && t.IsPassable());
			if (witnesses.Count < 2 || near == null)
			{
				const string why = "news of a sighting travels with the witness";
				Skip(why, near == null ? "no free tile by the portal" : $"{witnesses.Count} free witness(es), need 2: "
					+ string.Join("; ", Villages().Where(v => v.owner == faction).SelectMany(v => v.residents).Where(r => r != null && !r.isDead)
						.Select(r => $"{r.name} party={r.partyComponent.currentParty?.isActive} normal={r.isNormalCharacter} witness={r.limiterComponent.canWitness} carried={r.carryComponent.isBeingCarriedBy != null}")));
			}
			else
			{
				void SeePortal(Character viewer)
				{
					CharacterManager.Instance.Teleport(viewer, near);
					viewer.marker.UpdatePosition();
					Physics2D.SyncTransforms();
					TileObject seen = portal.tiles.Select(t => t.tileObjectComponent.objHere)
						.First(o => o != null && o.tileObjectType.IsDemonicStructureTileObject());
					viewer.marker.AddPOIAsInVisionRange(seen);
					// Finish native sighting before defenders get a combat tick.
					viewer.marker.ProcessAllUnprocessedVisionPOIs();
				}
				// A witness killed before getting home: the news dies with them.
				Character doomed = witnesses[0];
				Guard("place a doomed witness by the portal", () => { SeePortal(doomed); return doomed; });
				bool sawIt = PlusBridge.Carries(doomed, portal);
				Guard("kill the witness", () => { doomed.Death("autotest"); return doomed; });
				yield return WaitGameHours(2f, null);
				Check("a witness killed on the way home takes the news with them", () =>
					(sawIt && !PlusBridge.Knows(faction, portal) && !PlusBridge.Carries(doomed, portal),
					$"{doomed.name} saw it={sawIt}; faction knows={PlusBridge.Knows(faction, portal)}"));
				// Their village would search where they were last seen, by the Portal, and its
				// defenders would kill the searchers (the base game's patrols, not what is tested
				// here): their people have found them dead.
				PlusBridge.MissingFoundDead(doomed, witnesses[1]);

				// A witness who gets home tells their people.
				Character witness = witnesses[1];
				Guard("place a witness by the portal", () => { SeePortal(witness); return witness; });
				Check("a villager who sees the portal carries the news; their faction does not know yet", () =>
					(PlusBridge.Carries(witness, portal) && !PlusBridge.Knows(faction, portal),
					$"{witness.name} at {witness.gridTileLocation?.localPlace} carries={PlusBridge.Carries(witness, portal)} faction knows={PlusBridge.Knows(faction, portal)}"));
				// Out of the Portal defenders' reach while the panel is read: open ground next to
				// their village (not in it: back home they would tell their people at once).
				LocationGridTile road = witness.homeSettlement?.areas.SelectMany(a => a.neighbourComponent.neighbours).Distinct()
					.Where(a => !a.HasSettlementOnArea() && a.gridTileComponent.centerGridTile != null)
					.Select(a => a.gridTileComponent.centerGridTile).FirstOrDefault(t => !t.isOccupied && t.IsPassable());
				if (!witness.isDead && road != null)
				{
					Guard("take the witness away from the portal", () =>
					{
						witness.combatComponent.ClearHostilesInRange(false);
						witness.combatComponent.ClearAvoidInRange(false);
						CharacterManager.Instance.Teleport(witness, road);
						return witness;
					});
				}
				// The bookmarks panel's "Who Knows of You" section names the carrier.
				yield return WaitGameHours(0.2f, null);
				List<string> carrying = PlusBridge.KnowledgePanelLines() ?? new List<string>();
				string header = FindObjectsOfType<BookmarkCategoryItemUI>().Where(i => (int)i.category == 100)
					.Select(i => (AccessTools.Field(typeof(BookmarkCategoryItemUI), "lblHeaderName").GetValue(i) as TMPro.TMP_Text)?.text).FirstOrDefault();
				yield return Screenshot("whoknows.png");
				Check("the bookmarks panel shows who is carrying news of you", () =>
					(header == "Who Knows of You" && carrying.Any(l => l.Contains(witness.name) && l.Contains("is carrying news of your") && l.Contains(portal.name)),
					$"section header={header ?? "none"}; lines: {string.Join(" / ", carrying)}"));
				if (witness.isDead || witness.homeSettlement?.cityCenter == null)
				{
					Skip("back home, the witness teaches it to their faction", $"{witness.name} died by the portal (dead={witness.isDead})");
					Skip("the bookmarks panel shows what a faction knows", $"{witness.name} died by the portal");
				}
				else
				{
					LocationGridTile home = witness.homeSettlement.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? witness.homeSettlement.cityCenter.passableTiles.FirstOrDefault();
					// Out of any party and any fight first: a party on a quest leads its members
					// away again, and the Portal's defenders attack whoever stands by it.
					Guard("send the witness home", () =>
					{
						witness.partyComponent.currentParty?.RemoveMember(witness);
						witness.combatComponent.ClearHostilesInRange(false);
						witness.combatComponent.ClearAvoidInRange(false);
						CharacterManager.Instance.Teleport(witness, home);
						return witness;
					});
					// Telling is hourly and needs them in the village: one who walks out again
					// before the hour (a job elsewhere, a flight) is brought back.
					yield return WaitGameHours(3f, () =>
					{
						if (!witness.isDead && witness.currentSettlement != witness.homeSettlement && witness.carryComponent.isBeingCarriedBy == null)
						{
							CharacterManager.Instance.Teleport(witness, home);
						}
						return PlusBridge.Knows(faction, portal) || witness.isDead;
					});
					if (witness.isDead && !PlusBridge.Knows(faction, portal))
					{
						// The news dies with them, as the doomed witness check above expects.
						Skip("back home, the witness teaches it to their faction", $"{witness.name} was killed before telling anyone (the Portal's defenders hit them as they left)");
						Skip("the bookmarks panel shows what a faction knows", $"{witness.name} was killed before telling anyone");
					}
					else
					{
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
				}
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
				// A creature can already be in the new Kennel (a wild Pig once was): empty it first.
				if (kennel.occupyingSummon != null)
				{
					Guard("empty the Kennel", () => { Summon o = kennel.occupyingSummon; o.Death("autotest"); return o; });
				}
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
				Guard("destroy the Kennel", () => RemoveTestBuilding(kennel));
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

		// A poison explosion set off by the player's side on the Portal: vanilla (the fix
		// switched off) it damages the Portal, which proves the explosion reaches it; with
		// the fix the Portal keeps its HP. The Portal is healed and cleaned after each.
		private IEnumerator ExplosionTest()
		{
			const string name = "your side's explosions leave your Portal alone";
			ThePortal portal = PlayerManager.Instance.player.playerSettlement.allStructures.OfType<ThePortal>().FirstOrDefault(p => !p.hasBeenDestroyed);
			TileObject core = portal?.tiles.Select(t => t.tileObjectComponent.objHere).OfType<TileObject>()
				.FirstOrDefault(o => o.tileObjectType.IsDemonicStructureTileObject());
			if (!PlusBridge.Available || core == null)
			{
				Skip(name, !PlusBridge.Available ? "RuinarchPlus not loaded" : "no Portal object");
				yield break;
			}
			Character demon = CharacterManager.Instance.allCharacters.FirstOrDefault(c => !c.isDead && c.faction != null && c.faction.isPlayerFaction);
			object setting = PlusBridge.Config("friendlyExplosionsSpareBuildings");
			int[] lost = new int[2];
			// The fix first: the vanilla blast leaves poison and fire around the Portal that the
			// clean-up below (the Portal's own objects) does not reach, and would tick into the
			// next measure.
			for (int i = 0; i < 2; i++)
			{
				bool fixOn = i == 0;
				int before = portal.currentHP;
				Try($"poison explosion on the Portal (fix {(fixOn ? "on" : "off")})", () =>
				{
					PlusBridge.SetConfig("friendlyExplosionsSpareBuildings", fixOn);
					CombatManager.Instance.PoisonExplosion(core, core.gridTileLocation, 2, demon, 1, demon == null);
				});
				yield return WaitGameHours(0.5f, null);
				lost[fixOn ? 1 : 0] = before - portal.currentHP;
				Try("heal and clean the Portal", () =>
				{
					foreach (TileObject o in portal.tiles.Select(t => t.tileObjectComponent.objHere).OfType<TileObject>().Where(o => o.tileObjectType.IsDemonicStructureTileObject() || o.tileObjectType == TILE_OBJECT_TYPE.BLOCK_WALL).Distinct())
					{
						foreach (string status in new[] { "Burning", "Poisoned", "Frozen", "Freezing", "Wet", "Zapped" })
						{
							o.traitContainer.RemoveStatusAndStacks(o, status);
						}
						o.AdjustHP(o.maxHP - o.currentHP, ELEMENTAL_TYPE.Normal);
					}
					portal.AdjustHP(portal.maxHP - portal.currentHP);
				});
			}
			// A frozen explosion the way the game makes one: a demon freezes a wolf next to the
			// Portal, then zaps it (the explosion itself names nobody, only isPlayerSource).
			const string frozenName = "your side's frozen explosions leave your Portal alone";
			int[] frozenLost = new int[2];
			string frozenWhy = null;
			for (int i = 0; i < 2 && demon != null; i++)
			{
				bool fixOn = i == 0;
				List<LocationGridTile> around = new List<LocationGridTile>();
				core.gridTileLocation.PopulateTilesInRadius(around, 2, 0, includeCenterTile: false, includeTilesInDifferentStructure: true);
				LocationGridTile spot = around.FirstOrDefault(t => !t.isOccupied && t.IsPassable() && t.structure != portal);
				if (spot == null)
				{
					frozenWhy = "no free tile within 2 of the Portal's core";
					break;
				}
				int before = portal.currentHP;
				Summon wolf = Guard($"freeze and zap a wolf by the Portal (fix {(fixOn ? "on" : "off")})", () =>
				{
					PlusBridge.SetConfig("friendlyExplosionsSpareBuildings", fixOn);
					Summon s = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Wolf, FactionManager.Instance.wildMonsterFaction, homeLocation: null,
						homeRegion: GridMap.Instance.mainRegion, homeStructure: null, className: "", bypassIdeologyChecking: true);
					s.CreateMarker();
					s.InitialCharacterPlacement(spot);
					s.marker.UpdatePosition();
					s.traitContainer.AddTrait(s, "Frozen", demon);
					s.traitContainer.AddTrait(s, "Zapped", demon);
					return s;
				});
				yield return WaitGameHours(0.5f, null);
				frozenLost[fixOn ? 1 : 0] = before - portal.currentHP;
				Log($"  frozen explosion (fix {(fixOn ? "on" : "off")}): wolf at {spot.localPlace} frozen now={wolf?.traitContainer.HasTrait("Frozen")} dead={wolf?.isDead}; portal lost {before - portal.currentHP}");
				Try("remove the wolf, heal and clean the Portal", () =>
				{
					if (wolf != null && !wolf.isDead)
					{
						wolf.Death("autotest");
					}
					foreach (TileObject o in portal.tiles.Select(t => t.tileObjectComponent.objHere).OfType<TileObject>().Where(o => o.tileObjectType.IsDemonicStructureTileObject() || o.tileObjectType == TILE_OBJECT_TYPE.BLOCK_WALL).Distinct())
					{
						foreach (string status in new[] { "Burning", "Poisoned", "Frozen", "Freezing", "Wet", "Zapped" })
						{
							o.traitContainer.RemoveStatusAndStacks(o, status);
						}
						o.AdjustHP(o.maxHP - o.currentHP, ELEMENTAL_TYPE.Normal);
					}
					portal.AdjustHP(portal.maxHP - portal.currentHP);
				});
			}
			PlusBridge.SetConfig("friendlyExplosionsSpareBuildings", setting ?? true);
			string who = demon != null ? $"by {demon.name} ({demon.faction.name})" : "as a player spell";
			if (lost[0] <= 0)
			{
				Skip(name, $"the vanilla explosion {who} did not reach the Portal either (lost {lost[0]})");
			}
			else
			{
				Check(name, () => (lost[1] == 0, $"explosion {who}: vanilla lost {lost[0]} HP, with the fix {lost[1]}; portal hp now {portal.currentHP}/{portal.maxHP}"));
			}
			if (demon == null || frozenWhy != null || frozenLost[0] <= 0)
			{
				Skip(frozenName, demon == null ? "no demon to freeze and zap with" : frozenWhy ?? $"the vanilla frozen explosion did not reach the Portal either (lost {frozenLost[0]})");
			}
			else
			{
				Check(frozenName, () => (frozenLost[1] == 0, $"frozen and zapped {who}: vanilla lost {frozenLost[0]} HP, with the fix {frozenLost[1]}; portal hp now {portal.currentHP}/{portal.maxHP}"));
			}
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
				Guard("destroy the building next door", () => RemoveTestBuilding(nextDoor));
			}

			// Gossip between two friendly factions.
			Func<Character, bool> free = r => r != null && !r.isDead && r.hasMarker && r.isNormalCharacter && r.race.IsSapient() && r.limiterComponent.canMove
				&& r.limiterComponent.canWitness && (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive) && r.carryComponent.isBeingCarriedBy == null && r != r.homeSettlement?.ruler
				&& !r.isFactionLeader && !r.isAlliedWithPlayer && r.homeSettlement?.cityCenter != null
				// Gossip is a sighting: nobody reacts to what they see while fighting or unable to act.
				&& r.limiterComponent.canPerform && !r.combatComponent.isInActualCombat;
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
			// Keep the meeting in place until the native sighting is processed. Either
			// villager can otherwise walk out of view between teleport and the next tick.
			yield return WaitGameHours(3f, () =>
			{
				if (PlusBridge.Carries(listener, portal) || teller.isDead || listener.isDead) return true;
				LocationGridTile at = teller.gridTileLocation;
				if (at != null && (listener.gridTileLocation?.structure != at.structure
					|| !at.neighbourList.Contains(listener.gridTileLocation)))
				{
					LocationGridTile beside = at.neighbourList.FirstOrDefault(t => t != null && !t.isOccupied && t.structure == at.structure)
						?? at.neighbourList.FirstOrDefault(t => t != null && !t.isOccupied);
					if (beside != null) CharacterManager.Instance.Teleport(listener, beside);
				}
				return PlusBridge.Carries(listener, portal);
			});
			Check("a villager tells someone of a friendly faction about a demonic building", () =>
				(PlusBridge.Carries(listener, portal) && !PlusBridge.Knows(listeners, portal),
				$"{teller.name} of {tellers.name} at {teller.gridTileLocation?.localPlace} -> {listener.name} of {listeners.name} at {listener.gridTileLocation?.localPlace}: carries={PlusBridge.Carries(listener, portal)} their faction knows={PlusBridge.Knows(listeners, portal)}"));
			if (PlusBridge.Carries(listener, portal))
			{
				LocationGridTile home = listener.homeSettlement.cityCenter.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? listener.homeSettlement.cityCenter.passableTiles.FirstOrDefault();
				Guard("send the listener home", () => { CharacterManager.Instance.Teleport(listener, home); return listener; });
				// Telling is hourly and needs them in the village: one who walks out again before
				// the hour (a job elsewhere, a party) is brought back, as the witness is above.
				yield return WaitGameHours(3f, () =>
				{
					if (!listener.isDead && listener.gridTileLocation != null && !listener.gridTileLocation.IsPartOfSettlement(listener.homeSettlement))
					{
						if (listener.partyComponent.hasParty)
						{
							listener.partyComponent.currentParty.RemoveMember(listener);
						}
						CharacterManager.Instance.Teleport(listener, home);
					}
					return PlusBridge.Knows(listeners, portal) || listener.isDead;
				});
				Check("news heard from another faction makes the listener's faction aware", () =>
					(PlusBridge.Knows(listeners, portal) && listeners.isAwareOfPlayer,
					$"{listeners.name} knows={PlusBridge.Knows(listeners, portal)} aware={listeners.isAwareOfPlayer}; {listener.name}: dead={listener.isDead} faction={listener.faction?.name ?? "none"} "
					+ $"home={listener.homeSettlement?.name ?? "none"} in={listener.currentSettlement?.name ?? "the wild"} at {listener.gridTileLocation?.localPlace} carries={PlusBridge.Carries(listener, portal)} remembers={PlusBridge.Remembers(listener, portal)} knowledge={PlusBridge.Config("knowledgeEnabled")}"));
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
				// A 0.6 save: one faction-wide ledger, handed to the faction's villages at the first hour.
				ReplayLoad("ruinarch.plus.knowledge.json", $"{{\"known\":[\"{faction.persistentID}/{portal.persistentID}\"]}}");
				bool cleared = !PlusBridge.Knows(faction, portal);
				yield return WaitGameHours(2f, () => PlusBridge.Knows(faction, portal));
				Check("loading a 0.6 save: the faction's knowledge goes to its villagers", () =>
					(cleared && PlusBridge.Knows(faction, portal), $"emptied by the load={cleared}; known an hour later={PlusBridge.Knows(faction, portal)}"));
				// Back to what this world really knows (other aware factions were given everything too).
				ReplayLoad("ruinarch.plus.knowledge.json", json);
			}
			else
			{
				Skip("loading a save from before the ledger: aware factions know the player's buildings", $"{faction.name} is not aware of the player");
			}
		}

		// Phase 4: records. Households keep what they remember of the player's buildings on a
		// Book Shelf (or a Book) at home, a Town or City builds a Library, and villagers write
		// and read them through two real actions (an hour at the shelf, a line in its Logs tab).
		// The faction is never made aware of the player (no counterattacks), and it forgets
		// everything taught here at the end.
		// Check <paramref name="name"/> unless <paramref name="who"/>, whom it measures, died
		// first (killed by the world: a monster, a disease, the Portal's defenders); then skip.
		private void CheckAlive(Character who, string name, Func<(bool ok, string detail)> check)
		{
			if (who != null && who.isDead)
			{
				Skip(name, $"{who.name} died before it could be measured ({who.causeOfDeath})");
			}
			else
			{
				Check(name, check);
			}
		}

		private IEnumerator RecordsSuite()
		{
			if (!PlusBridge.RecordsAvailable || !(PlusBridge.Config("recordsEnabled") is bool on) || !on)
			{
				Skip("records", "RuinarchPlus (with records) not loaded");
				yield break;
			}
			LocationStructure portal = PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			Func<Character, bool> able = r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient() && !r.isAlliedWithPlayer
				&& r.marker != null && r.carryComponent.isBeingCarriedBy == null && r.limiterComponent.canMove
				&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive);
			// A household of two who can be sent home, in a village of a major faction; Towns and
			// Cities first, since they build Libraries, and homes with a Book Shelf first, so the
			// shelf (not a placed Book) carries the record where the world has one.
			LocationStructure house = null;
			NPCSettlement village = null;
			Character writer = null, reader = null;
			foreach (NPCSettlement v in Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer).OrderByDescending(v => PlusBridge.Tier(v) != "Village"))
			{
				List<LocationStructure> dwellings = v.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> ds) ? ds : new List<LocationStructure>();
				foreach (LocationStructure d in dwellings.Where(d => !d.hasBeenDestroyed && d.passableTiles.Any(t => t.structure == d && !t.isOccupied))
					.OrderByDescending(d => d.GetTileObjectsOfType(TILE_OBJECT_TYPE.SHELF_BOOKS)?.Any(t => t.mapObjectState == MAP_OBJECT_STATE.BUILT) == true))
				{
					List<Character> household = d.residents.Where(able).ToList();
					if (household.Count >= 2)
					{
						house = d;
						village = v;
						writer = household[0];
						reader = household[1];
						break;
					}
				}
				if (house != null)
				{
					break;
				}
			}
			if (portal == null || house == null)
			{
				Skip("records", portal == null ? "no portal" : "no household of two free villagers: " + string.Join("; ", Villages().Select(Describe)));
				yield break;
			}
			Faction faction = village.owner;
			Log($"records test village: {Describe(village)} tier={PlusBridge.Tier(village)}; household {house.name}: writer {writer.name}, reader {reader.name}");
			object readChance = PlusBridge.Config("readChance");
			object visitChance = PlusBridge.Config("libraryVisitChance");
			// No free-time reading unless a check asks for it.
			PlusBridge.SetConfig("readChance", 0);
			PlusBridge.SetConfig("libraryVisitChance", 0);
			PlusBridge.Forget(faction);
			PlusBridge.ForgetRecords(faction);
			Func<LocationStructure, string> record = h => PlusBridge.RecordOf(h) is HashSet<LocationStructure> set ? "[" + string.Join(", ", set.Select(s => s.name)) + "]" : "none";
			INTERACTION_TYPE writeType = PlusBridge.RecordAction(true);
			string wrote = $"wrote of your {portal.name}";
			string read = $"read of your {portal.name}";
			Func<TileObject, string> describeCarrier = t => t == null ? "none" : $"{t.name} at {t.gridTileLocation?.localPlace}; its logs: [{string.Join(" | ", LogsOf(t, null))}]";
			// Whoever of the village is writing at a carrier right now. Knowledge spreads at home,
			// so another member of the household may take up the writing first in their free
			// time: that is the same action, and it counts.
			Func<TileObject, Character> writingAt = t => t == null ? null : village.residents.FirstOrDefault(r => r != null && !r.isDead
				&& r.currentActionNode?.goapType == writeType && r.currentActionNode.poiTarget == t);
			Character scribe() => writer != null && !writer.isDead ? writer : village.residents.FirstOrDefault(able);

			// 1. A Write job at the household's carrier: the villager walks there, writes for an
			// hour, and the carrier's Logs tab says so; a household without a record starts one.
			int mark = ModsLogLength();
			TileObject carrier = Guard("teach the writer and find the household's carrier", () => { PlusBridge.RememberAtHome(writer, portal); return PlusBridge.CarrierFor(house, true); });
			Character sawWriting = null;
			// What the villager's nameplate, panel and tooltip show (CharacterVisuals.GetThoughtBubble),
			// which throws for an action without thought bubble logs.
			HashSet<string> bubbles = new HashSet<string>();
			if (carrier != null)
			{
				Log($"  household carrier: {carrier.name} ({PlusBridge.CarriersOf(house).Count} in {house.name})");
				yield return GiveRecordJob(writer, true, carrier, house);
				yield return WaitGameHours(6f, () =>
				{
					if (writingAt(carrier) is Character w)
					{
						sawWriting = w;
						try { bubbles.Add(w.visuals.GetThoughtBubble() ?? "null"); }
						catch (Exception e) { bubbles.Add(e.GetType().Name); }
					}
					return PlusBridge.RecordOf(house)?.Contains(portal) == true;
				});
				yield return WaitGameHours(0.1f, null);
			}
			CheckAlive(writer, "a villager given a Write job walks to the record and writes it down", () =>
				(PlusBridge.RecordOf(house)?.Contains(portal) == true && sawWriting != null && LogsOf(carrier, wrote).Count > 0
					&& ModsLogHasSince(mark, $"A household in {village.name} started keeping a record"),
				$"record={record(house)} writing seen: {sawWriting?.name ?? "nobody"} carrier {describeCarrier(carrier)}; writer {writer.name} in {writer.currentStructure?.name ?? "the wild"} doing {writer.currentActionNode?.goapName ?? "nothing"}"));
			CheckAlive(writer, "a writing villager's thought bubble says what they are doing", () =>
				(bubbles.Count > 0 && bubbles.All(b => b == "Going to write." || b == "Writing."), $"bubbles=[{string.Join(" | ", bubbles)}]"));
			List<string> panel = PlusBridge.KnowledgePanelLines() ?? new List<string>();
			CheckAlive(writer, "the bookmarks panel says where a faction keeps records", () =>
				(panel.Any(l => l.Contains(faction.name) && l.Contains("(written in ") && l.Contains("home")), string.Join(" / ", panel)));

			// 2. The game saves while a villager is writing: the save completes and the writing
			// goes on. (Loading that save needs a game restart, which the harness cannot do.)
			const string savedWriting = "a save made while a villager writes completes, and the writing goes on";
			Guard("empty the record and send the writer again", () =>
			{
				PlusBridge.ForgetRecords(faction);
				PlusBridge.RememberAtHome(writer, portal);
				carrier = PlusBridge.CarrierFor(house, true);
				return carrier;
			});
			yield return GiveRecordJob(writer, true, carrier, house);
			Character savingWriter = null;
			yield return WaitGameHours(4f, () => (savingWriter = writingAt(carrier)) != null || PlusBridge.RecordOf(house)?.Contains(portal) == true);
			if (savingWriter == null)
			{
				Skip(savedWriting, PlusBridge.RecordOf(house)?.Contains(portal) == true
					? "the writing was over before a check saw it"
					: $"nobody started writing within 4 hours ({writer.name} doing {writer.currentActionNode?.goapName ?? "nothing"})");
			}
			else
			{
				string saved = null;
				yield return SaveAndRead("ruinarch.plus.records.json", (j, e) => saved = j);
				yield return WaitGameHours(3f, () => PlusBridge.RecordOf(house)?.Contains(portal) == true);
				CheckAlive(savingWriter, savedWriting, () =>
					(saved != null && PlusBridge.RecordOf(house)?.Contains(portal) == true, $"saved={saved != null} record={record(house)}"));
			}

			// 3. Nobody told to: in their free time, a villager at home writes what the
			// household's record lacks (any household of the village: they all remember now).
			Guard("empty the records; the village still remembers", () => { PlusBridge.ForgetRecords(faction); PlusBridge.RememberAtHome(scribe(), portal); return writer; });
			mark = ModsLogLength();
			Func<List<LocationStructure>> writtenHomes = () => (village.structures.TryGetValue(STRUCTURE_TYPE.DWELLING, out List<LocationStructure> ds) ? ds : new List<LocationStructure>())
				.Where(d => PlusBridge.RecordOf(d)?.Contains(portal) == true).ToList();
			yield return WaitGameHours(48f, () => writtenHomes().Count > 0);
			// mods.log is written a moment after the record changes.
			yield return WaitGameHours(0.2f, null);
			Check("in their free time, a villager at home writes what the household's record lacks", () =>
				(writtenHomes().Count > 0 && ModsLogHasSince(mark, wrote), $"homes with the record: [{string.Join(", ", writtenHomes().Select(d => d.name))}]"));

			// 4. Nobody who remembers is left (dead, moved, forgotten like a forgetful elder), and
			// only this household keeps a record: a Read job gives it back.
			Guard("leave only this household's record, and nobody who remembers", () =>
			{
				Character s = scribe();
				PlusBridge.ForgetRecords(faction);
				PlusBridge.RememberAtHome(s, portal);
				PlusBridge.WriteRecord(s, house);
				PlusBridge.ForgetMemory(faction);
				return house;
			});
			TileObject readAt = PlusBridge.CarrierFor(house, false);
			if (readAt != null)
			{
				yield return GiveRecordJob(reader, false, readAt, house);
				yield return WaitGameHours(6f, () => PlusBridge.Remembers(reader, portal));
				yield return WaitGameHours(0.1f, null);
			}
			CheckAlive(reader, "a household member who does not remember reads it back", () =>
				(PlusBridge.Remembers(reader, portal) && LogsOf(readAt, read).Count > 0 && LogsOf(reader, read).Count > 0,
				$"remembers={PlusBridge.Remembers(reader, portal)} record={record(house)} carrier {describeCarrier(readAt)}; reader's lines {LogsOf(reader, read).Count}; reader in {reader.currentStructure?.name ?? "the wild"}"));

			// 5. Every carrier destroyed while nobody remembers: the record is gone, and nobody
			// learns from it even when they would read.
			List<TileObject> burned = null;
			Guard("destroy every carrier while nobody remembers", () =>
			{
				Character s = scribe();
				PlusBridge.ForgetRecords(faction);
				PlusBridge.RememberAtHome(s, portal);
				PlusBridge.WriteRecord(s, house);
				PlusBridge.ForgetMemory(faction);
				burned = PlusBridge.CarriersOf(house);
				foreach (TileObject t in burned)
				{
					t.AdjustHP(-t.currentHP, ELEMENTAL_TYPE.Normal);
				}
				return burned;
			});
			PlusBridge.SetConfig("readChance", 100);
			SendInto(reader, house);
			yield return WaitGameHours(2.05f, null);
			PlusBridge.SetConfig("readChance", 0);
			CheckAlive(reader, "destroying every carrier takes the record; nobody reads from it afterwards", () =>
				(burned?.Count > 0 && PlusBridge.RecordOf(house) == null && !PlusBridge.Remembers(reader, portal),
				$"destroyed {burned?.Count ?? 0}: [{string.Join(", ", (burned ?? new List<TileObject>()).Select(t => $"{t.name} hp={t.currentHP} on {t.gridTileLocation?.localPlace.ToString() ?? "nothing"}"))}] record={record(house)} reader remembers={PlusBridge.Remembers(reader, portal)}"));

			// 6. A Town or City queues a Library and its villagers build it. Capitals are Cities
			// from the first hour, so theirs may be queued (or built) before this suite starts.
			const string queuedCheck = "a Town or City queues a Library and its villagers build it";
			string tier = PlusBridge.Tier(village);
			if (tier == "Village")
			{
				Skip(queuedCheck, $"{village.name} is a village");
			}
			else
			{
				STRUCTURE_TYPE kind = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.library");
				var placements = new List<JobQueueItem>();
				village.PopulateJobsOfType(placements, JOB_TYPE.PLACE_BLUEPRINT);
				GoapPlanJob placement = placements.OfType<GoapPlanJob>().FirstOrDefault(j =>
					j.GetOtherDataFor(INTERACTION_TYPE.PLACE_BLUEPRINT)?[2]?.obj is StructureSetting setting && setting.structureType == kind);
				GenericTileObject blueprintTile = placement?.poiTarget as GenericTileObject
					?? village.areas.SelectMany(a => a.gridTileComponent.gridTiles).Select(t => t.tileObjectComponent.genericTileObject)
						.FirstOrDefault(g => g?.blueprintOnTile?.name.Contains("@ruinarch.plus/library") == true);
				StructureSetting material = village.owner.factionType.CreateStructureSettingForStructure(STRUCTURE_TYPE.WORKSHOP, village);
				bool room = material.hasValue && LandmarkManager.Instance.CanPlaceStructureBlueprint(village.owner.factionType.type, village,
					new StructureSetting(kind, material.resource), out LocationGridTile _, out string _, out int _, out LocationGridTile _);
				Character worker = !writer.isDead ? writer : !reader.isDead ? reader : null;
				if (PlusBridge.LibraryFor(village) == null && blueprintTile == null && !room)
				{
					Skip(queuedCheck, $"{village.name} has no room for the authored Library");
				}
				else if (PlusBridge.LibraryFor(village) == null && worker == null)
				{
					Skip(queuedCheck, "both household members died before construction");
				}
				else
				{
					if (PlusBridge.LibraryFor(village) == null)
					{
						// Native permits one placement job at a time. Do not let an unrelated
						// unassigned job block the fixture's Library queue indefinitely.
						foreach (JobQueueItem other in placements.Where(j => j != placement)) other.ForceCancelJob("autotest Library setup");
						PlusBridge.CheckLibraries();
						placements.Clear();
						village.PopulateJobsOfType(placements, JOB_TYPE.PLACE_BLUEPRINT);
						placement = placements.OfType<GoapPlanJob>().FirstOrDefault(j =>
							j.GetOtherDataFor(INTERACTION_TYPE.PLACE_BLUEPRINT)?[2]?.obj is StructureSetting setting && setting.structureType == kind);
						if (placement != null)
						{
							blueprintTile = placement.poiTarget as GenericTileObject;
							if (placement.assignedCharacter == null)
							{
								worker.jobQueue.CancelAllJobs();
								placement.SetPriority(1000);
								worker.jobQueue.AddJobInQueue(placement);
							}
						}
						yield return WaitGameHours(12f, () => blueprintTile?.blueprintOnTile != null || PlusBridge.LibraryFor(village) != null);
						if (blueprintTile?.blueprintOnTile is LocationStructureObject blueprint && PlusBridge.LibraryFor(village) == null)
						{
							Guard("supply the native Library build job", () =>
							{
								var jobs = new List<JobQueueItem>();
								village.PopulateJobsOfType(jobs, JOB_TYPE.BUILD_BLUEPRINT);
								JobQueueItem build = jobs.First(j => j.poiTarget == blueprintTile);
								worker = build.assignedCharacter ?? worker;
								worker.StopCurrentActionNode("autotest Library materials");
								worker.UncarryPOI();
								RESOURCE resource = blueprint.thinWallResource.GetResourceForWall();
								TILE_OBJECT_TYPE pileType = resource == RESOURCE.STONE ? TILE_OBJECT_TYPE.STONE_PILE : TILE_OBJECT_TYPE.WOOD_PILE;
								ResourcePile supplies = InnerMapManager.Instance.CreateNewTileObject<ResourcePile>(pileType);
								supplies.SetResourceInPile(blueprint.craftCost);
								worker.ObtainItem(supplies);
								worker.carryComponent.CarryPOI(supplies);
								build.SetPriority(1000);
								return build.assignedCharacter != null || worker.jobQueue.AddJobInQueue(build) ? build : null;
							});
						}
						yield return WaitGameHours(18f, () =>
						{
							worker?.needsComponent.SetTiredness(100f);
							return PlusBridge.LibraryFor(village) != null;
						});
					}
					Check(queuedCheck, () =>
						(PlusBridge.LibraryFor(village) != null,
						$"built={PlusBridge.LibraryFor(village)?.name ?? "no"} tier={PlusBridge.Tier(village)} villagers={Villagers(village)}; {BlueprintState(village)}"));
				}
			}

			// The Library (built at once if the villagers have not built one) has carriers.
			LocationStructure library = PlusBridge.LibraryFor(village) ?? Guard("build a Library", () => PlusBridge.InstantBuildLibrary(village));
			if (library == null)
			{
				foreach (string name in new[] { "a Library has Book Shelves or Books", "a villager writes in the Library", "a villager with nothing to remember it by goes to the Library and reads it",
					"destroying one of several carriers keeps the record", "records are stored inside the player's save file", "records come back when the save loads",
					"a destroyed Library is announced and its records are gone" })
				{
					Skip(name, $"{village.name} has no room for a Library");
				}
			}
			else
			{
				yield return WaitGameHours(1.05f, () => PlusBridge.CarriersOf(library).Count > 0);
				Check("a Library has Book Shelves or Books", () =>
					(PlusBridge.CarriersOf(library).Count > 0, $"carriers=[{string.Join(", ", PlusBridge.CarriersOf(library).Select(t => t.name))}] (libraryBooks={PlusBridge.Config("libraryBooks")})"));

				// 7. A Write job in the Library, then a free-time visit by someone who does not remember.
				TileObject libCarrier = Guard("find the Library's carrier to write in", () =>
				{
					PlusBridge.RememberAtHome(writer, portal);
					return PlusBridge.CarrierFor(library, true);
				});
				yield return GiveRecordJob(writer, true, libCarrier, null);
				yield return WaitGameHours(8f, () => PlusBridge.RecordOf(library)?.Contains(portal) == true);
				yield return WaitGameHours(0.1f, null);
				CheckAlive(writer, "a villager writes in the Library", () =>
					(PlusBridge.RecordOf(library)?.Contains(portal) == true && LogsOf(libCarrier, wrote).Count > 0,
					$"record={record(library)} carrier {describeCarrier(libCarrier)}; writer in {writer.currentStructure?.name ?? "the wild"} doing {writer.currentActionNode?.goapName ?? "nothing"}"));
				// The record is planted by someone alive: the writer, or (if the world killed them)
				// any free villager (Knowledge.Read and Records.Write refuse the dead).
				Guard("leave only the Library's record, and nobody who remembers", () =>
				{
					Character s = scribe();
					PlusBridge.ForgetRecords(faction);
					PlusBridge.RememberAtHome(s, portal);
					PlusBridge.WriteRecord(s, library);
					PlusBridge.ForgetMemory(faction);
					return library;
				});
				PlusBridge.SetConfig("libraryVisitChance", 100);
				mark = ModsLogLength();
				yield return WaitGameHours(24f, () => ModsLogHasSince(mark, read));
				PlusBridge.SetConfig("libraryVisitChance", 0);
				Check("a villager with nothing to remember it by goes to the Library and reads it", () =>
					(ModsLogHasSince(mark, read) && ModsLogHasSince(mark, $"{village.name}'s Library."),
					$"record={record(library)}; villagers remembering={village.residents.Count(r => PlusBridge.Remembers(r, portal))}"));

				// Losing one of several carriers keeps the record.
				List<TileObject> kept = PlusBridge.CarriersOf(library);
				if (kept.Count < 2)
				{
					Skip("destroying one of several carriers keeps the record", $"the Library has {kept.Count} carrier(s)");
				}
				else
				{
					Guard("destroy one of the Library's carriers", () => { kept[0].AdjustHP(-kept[0].currentHP, ELEMENTAL_TYPE.Normal); return kept[0]; });
					yield return WaitGameHours(1.05f, null);
					Check("destroying one of several carriers keeps the record", () =>
						(PlusBridge.RecordOf(library)?.Contains(portal) == true && PlusBridge.CarriersOf(library).Count == kept.Count - 1,
						$"record={record(library)} carriers {kept.Count} -> {PlusBridge.CarriersOf(library).Count}"));
				}

				// 8. Stored in the save, and back after a load.
				string json = null;
				string entries = null;
				yield return SaveAndRead("ruinarch.plus.records.json", (j, e) => { json = j; entries = e; });
				Check("records are stored inside the player's save file", () =>
					(json != null && json.Contains(library.persistentID) && json.Contains(portal.persistentID),
					json == null ? "entries: " + entries : $"{json.Length} bytes: {json.Substring(0, Math.Min(json.Length, 200))}"));
				if (json != null)
				{
					PlusBridge.ForgetRecords(faction);
					ReplayLoad("ruinarch.plus.records.json", json);
					Check("records come back when the save loads", () =>
						(PlusBridge.RecordOf(library)?.Contains(portal) == true && PlusBridge.CarriersOf(library).Count > 0,
						$"record={record(library)} carriers={PlusBridge.CarriersOf(library).Count}"));
				}
				else
				{
					Skip("records come back when the save loads", "no records in the save");
				}

				// 9. The Library destroyed: announced, its record gone.
				if (PlusBridge.RecordOf(library)?.Contains(portal) != true)
				{
					Guard("write the Library's record again", () => { Character s = scribe(); PlusBridge.RememberAtHome(s, portal); PlusBridge.WriteRecord(s, library); return library; });
				}
				mark = ModsLogLength();
				Guard("destroy the Library", () => { library.AdjustHP(-library.currentHP); return library; });
				yield return WaitGameHours(0.2f, null);
				Check("a destroyed Library is announced and its records are gone", () =>
					(PlusBridge.RecordOf(library) == null && ModsLogHasSince(mark, $"{village.name}'s Library was destroyed; its records of your"),
					$"destroyed={library.hasBeenDestroyed} record={record(library)}"));
			}

			// Leave the world as found: nobody remembers or keeps what was taught here.
			PlusBridge.Forget(faction);
			PlusBridge.ForgetRecords(faction);
			PlusBridge.SetConfig("readChance", readChance);
			PlusBridge.SetConfig("libraryVisitChance", visitChance);
		}

		// Which buildings come with Book Shelves (records are kept on them), per faction type,
		// and whether the Write and Read actions are registered with the game. Run by name.
		private IEnumerator ShelfProbe()
		{
			foreach (bool write in new[] { true, false })
			{
				INTERACTION_TYPE t = PlusBridge.RecordAction(write);
				Check($"the {(write ? "Write" : "Read")} action is registered with the game", () =>
				{
					bool made = InteractionManager.Instance.goapActionData.TryGetValue(t, out GoapAction action);
					bool states = GoapActionStateDB.goapActionStates.TryGetValue(t, out StateNameAndDuration[] s);
					return (t != INTERACTION_TYPE.NONE && made && states && action.goapType == t,
						$"type={(int)t} action={(made ? action.goapName : "missing")} states=[{(states ? string.Join(", ", s.Select(x => $"{x.name} {x.duration} ticks {x.status}")) : "missing")}]");
				});
			}
			Dictionary<string, int[]> tally = new Dictionary<string, int[]>();
			foreach (NPCSettlement v in Villages())
			{
				foreach (KeyValuePair<STRUCTURE_TYPE, List<LocationStructure>> kv in v.structures)
				{
					foreach (LocationStructure s in kv.Value.Where(s => !s.hasBeenDestroyed))
					{
						string key = $"{v.owner?.factionType?.type.ToString() ?? "no faction"} / {kv.Key}";
						if (!tally.TryGetValue(key, out int[] n))
						{
							tally[key] = n = new int[3];
						}
						List<TileObject> shelves = s.GetTileObjectsOfType(TILE_OBJECT_TYPE.SHELF_BOOKS) ?? new List<TileObject>();
						n[0]++;
						n[1] += shelves.Any(t => t.mapObjectState == MAP_OBJECT_STATE.BUILT) ? 1 : 0;
						n[2] += shelves.Count(t => t.mapObjectState == MAP_OBJECT_STATE.BUILT);
					}
				}
			}
			foreach (KeyValuePair<string, int[]> kv in tally.OrderBy(kv => kv.Key))
			{
				Log($"  shelves: {kv.Key}: {kv.Value[1]} of {kv.Value[0]} have Book Shelves ({kv.Value[2]} built shelves in all)");
			}
			yield break;
		}

		// The game's log database lines that involve <paramref name="poi"/> and contain
		// <paramref name="text"/> (any, when null): what its Logs tab lists with every filter on.
		private static List<string> LogsOf(IPointOfInterest poi, string text)
		{
			if (poi == null)
			{
				return new List<string>();
			}
			List<global::Log> logs = DatabaseManager.Instance.mainSQLDatabase.GetLogsThatMatchCriteria(poi.persistentID, text, UtilityScripts.CollectionUtilities.GetEnumValues<LOG_TAG>().ToList(), 20);
			return logs?.Select(l => l.logText).ToList() ?? new List<string>();
		}

		// Put <paramref name="c"/> on a free tile inside <paramref name="holder"/>.
		private void SendInto(Character c, LocationStructure holder)
		{
			LocationGridTile spot = holder.passableTiles.FirstOrDefault(t => t.structure == holder && !t.isOccupied);
			if (spot != null)
			{
				Try($"send {c.name} to {holder.name}", () => CharacterManager.Instance.Teleport(c, spot));
			}
		}

		// Give <paramref name="c"/> the Write or Read job at <paramref name="carrier"/> (sent into
		// <paramref name="sendTo"/> first, if given). A villager who cannot act (asleep: the
		// game's Resting trait) has a planned job refused by their queue without a word
		// (JobQueue.IsJobValidForCharacterGivenCurrentState), so wait until they can.
		private IEnumerator GiveRecordJob(Character c, bool write, TileObject carrier, LocationStructure sendTo)
		{
			string what = write ? "Write" : "Read";
			if (carrier == null || c == null)
			{
				Log($"  no {what} job given: carrier={carrier?.name ?? "none"}");
				yield break;
			}
			// Pending interactions from other villagers stop their target's current action.
			// Cancel those as well as our actor's work before assigning the fixture job.
			c.ForceCancelAllJobsTargetingThisCharacter(shouldDoAfterEffect: false);
			yield return WaitGameHours(12f, () => c.isDead || c.limiterComponent.canPerform);
			if (sendTo != null)
			{
				SendInto(c, sendTo);
			}
			INTERACTION_TYPE action = PlusBridge.RecordAction(write);
			// A villager held by another job keeps it before ours (a run once saw a writer
			// stay on one Go To Tile for the whole suite): clear their queue first.
			if (!c.isDead && c.currentActionNode != null && c.currentActionNode.goapType != action)
			{
				string was = $"{c.currentJob?.jobType.ToString() ?? "no job"} / {c.currentActionNode.goapName}";
				Try($"clear {c.name}'s jobs", () => { c.CancelAllJobs(); c.StopCurrentActionNode("autotest"); });
				Log($"  {c.name}: cleared {was} (now {c.currentActionNode?.goapName ?? "nothing"})");
			}
			Try($"give {c.name} the {what} job", () => PlusBridge.PlanRecordAction(c, write, carrier));
			bool taken = c.currentActionNode?.goapType == action
				|| c.jobQueue.jobsInQueue.OfType<GoapPlanJob>().Any(j => j.targetInteractionType == action && j.targetPOI == carrier);
			Log($"  {c.name}: {what} job at {carrier.name} {(taken ? "taken" : "REFUSED")} (canPerform={c.limiterComponent.canPerform}, doing {c.currentActionNode?.goapName ?? "nothing"})");
		}

		// Phase 6: the night watch. A Town or City (capitals count) with 4+ fighters names
		// guards on the night schedule; at night they patrol the village and fight what they see.
		private IEnumerator WatchSuite()
		{
			if (!PlusBridge.Available || !PlusBridge.WatchAvailable)
			{
				Skip("night watch", "RuinarchPlus (with the night watch) not loaded");
				yield break;
			}
			PlusBridge.SetConfig("nightWatchEnabled", true);
			Func<NPCSettlement, int> fighters = v => v.residents.Count(c => c != null && !c.isDead && c.isNormalCharacter && c.faction == v.owner && c.characterClass != null && c.characterClass.IsCombatant());
			// Who can stand guard: a fighter at home in the faction, not the ruler or leader, not
			// away with a party, not held (the rest may all be busy; then nothing to measure).
			Func<NPCSettlement, int> free = v => v.residents.Count(c => c != null && !c.isDead && c.isNormalCharacter && c.faction == v.owner && c.homeSettlement == v
				&& c.characterClass != null && c.characterClass.IsCombatant() && c != v.ruler && !c.isFactionLeader && !PlusBridge.IsChild(c)
				&& !c.traitContainer.HasTrait("Restrained") && !c.partyComponent.hasParty);
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer && v.cityCenter != null && PlusBridge.Tier(v) != "Village" && fighters(v) >= 4 && free(v) > 0)
				.OrderByDescending(free).FirstOrDefault();
			if (village == null)
			{
				Skip("night watch", "no Town or City with 4+ fighters and one free to guard: " + string.Join("; ", Villages().Select(v => $"{v.name} {PlusBridge.Tier(v)} fighters={fighters(v)} free={free(v)}")));
				yield break;
			}
			Try("run the night watch check", PlusBridge.WatchCheck);
			List<Character> guards = PlusBridge.GuardsOf(village);
			Log($"night watch village: {Describe(village)} tier={PlusBridge.Tier(village)} fighters={fighters(village)} guards=[{string.Join(", ", guards.Select(g => g.name))}]");

			// 1. Guards: fighters, never the ruler or the faction leader, and announced.
			Check("a Town or City sets a night watch of its fighters", () =>
				(guards.Count > 0 && guards.All(g => g.characterClass.IsCombatant() && g != village.ruler && !g.isFactionLeader) && ModsLogHas($"{village.name} has set a night watch"),
				$"guards=[{string.Join(", ", guards.Select(g => $"{g.name} ({g.characterClass.className})"))}] ruler={village.ruler?.name}"));
			if (guards.Count == 0)
			{
				yield break;
			}

			// 2. Guards sleep by day.
			Character other = village.residents.FirstOrDefault(c => c != null && !c.isDead && c.isNormalCharacter && !guards.Contains(c) && !c.partyComponent.hasParty && !c.traitContainer.HasTrait("Nocturnal"));
			Check("guards keep the night schedule, other villagers do not", () =>
				(guards.All(g => g.dailyScheduleComponent.schedule is NocturnalSchedule) && (other == null || !(other.dailyScheduleComponent.schedule is NocturnalSchedule)),
				$"guards: {string.Join(", ", guards.Select(g => $"{g.name}={g.dailyScheduleComponent.schedule?.GetType().Name}"))}; {other?.name ?? "nobody"}={other?.dailyScheduleComponent.schedule?.GetType().Name}"));

			// 3. At night, at home, they patrol, aggressive.
			Func<int> hour = () => GameManager.Instance.Today().tick / GameManager.ticksPerHour;
			// A guard named by day is still on a day rhythm (tired at night): a day to settle in.
			yield return WaitGameHours(24f, null);
			yield return WaitGameHours(24f, () => hour() == 23);
			BringResidentsHome(village);
			Try("run the night watch check", PlusBridge.WatchCheck);
			guards = PlusBridge.GuardsOf(village);
			Func<Character, bool> patrolling = g => g.behaviourComponent.HasBehaviour(typeof(NightPatrolBehaviour)) && g.combatComponent.combatMode == COMBAT_MODE.Aggressive;
			// Walking the rounds: a PATROL action or job seen within two night hours.
			Character walker = null;
			yield return WaitGameHours(2f, () => (walker = guards.FirstOrDefault(g => patrolling(g)
				&& (g.currentActionNode?.goapType == INTERACTION_TYPE.PATROL || g.currentJob?.jobType == JOB_TYPE.PATROL))) != null);
			if (guards.Count == 0)
			{
				string why = string.Join("; ", File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log")).Split('\n')
					.Where(l => l.Contains($"released from {village.name}'s night watch")).Select(l => l.Trim()));
				Skip("at night the guards walk the village, ready to fight", $"no guard left by night: {why}");
			}
			else Check("at night the guards walk the village, ready to fight", () =>
				(walker != null, $"hour {hour()} (tick {GameManager.Instance.Today().tick}); walking: {walker?.name ?? "nobody"}; " + string.Join(", ", guards.Select(g => $"{g.name}: patrol behaviour={g.behaviourComponent.HasBehaviour(typeof(NightPatrolBehaviour))} mode={g.combatComponent.combatMode} "
					+ $"job={g.currentJob?.jobType.ToString() ?? "none"} doing={g.currentActionNode?.goapName ?? "nothing"} in={g.currentSettlement?.name ?? "the wild"} schedule={g.dailyScheduleComponent.schedule.GetScheduleType(GameManager.Instance.Today().tick)} "
					+ $"resting={g.traitContainer.HasTrait("Resting")} canPlan={g.CanPlanGoap()} queue=[{string.Join("/", g.jobQueue.jobsInQueue.Select(j => j.jobType))}] "
					+ $"behaviours=[{string.Join(" ", g.behaviourComponent.currentBehaviourComponents.Select(b => b.GetType().Name + ":" + b.priority))}]"))));

			// 4. A wolf in the village at night is fought.
			Character guard = walker ?? guards.FirstOrDefault(patrolling) ?? guards.FirstOrDefault();
			List<LocationGridTile> around = new List<LocationGridTile>();
			guard?.gridTileLocation?.PopulateTilesInRadius(around, 4, includeCenterTile: false, includeTilesInDifferentStructure: true);
			LocationGridTile near = around.FirstOrDefault(t => !t.isOccupied && t.IsPartOfSettlement(village));
			if (guard == null || near == null)
			{
				Skip("a guard fights a wolf in the village at night", guard == null ? "no guard" : "no free tile near the guard");
			}
			else
			{
				Summon wolf = Guard("put a wolf in the village", () =>
				{
					Summon s = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Wolf, FactionManager.Instance.wildMonsterFaction, homeLocation: null,
						homeRegion: GridMap.Instance.mainRegion, homeStructure: null, className: "", bypassIdeologyChecking: true);
					s.CreateMarker();
					s.InitialCharacterPlacement(near);
					s.marker.UpdatePosition();
					// A creature placed in a village can be taken in by its faction: keep it wild.
					if (s.faction != FactionManager.Instance.wildMonsterFaction)
					{
						s.ChangeFactionTo(FactionManager.Instance.wildMonsterFaction, bypassIdeologyChecking: true);
					}
					return s;
				});
				bool fought = false;
				// What each side saw during the wait (first sighting, flights), for a failure.
				List<string> seen = new List<string>();
				Action note = () =>
				{
					if (wolf == null || wolf.isDead)
					{
						return;
					}
					foreach (Character g in guards)
					{
						string s = $"{g.name}{(g.marker != null && g.marker.inVisionCharacters.Contains(wolf) ? " sees" : "")}{(g.combatComponent.avoidInRange.Contains(wolf) ? " flees" : "")}"
							+ $"{(wolf.marker != null && wolf.marker.inVisionCharacters.Contains(g) ? " seen" : "")}{(wolf.combatComponent.avoidInRange.Contains(g) ? " fled-from" : "")}{(wolf.combatComponent.hostilesInRange.Contains(g) ? " attacked-by-wolf" : "")}";
						if (s != g.name && !seen.Contains(s))
						{
							seen.Add(s);
						}
					}
				};
				Log($"  wolf placed at {near.localPlace} next to {guard.name} at {guard.gridTileLocation?.localPlace}: wolf mode={wolf?.combatComponent.combatMode}; guard mode={guard.combatComponent.combatMode} faction warmonger={guard.faction?.factionType.HasIdeology(FACTION_IDEOLOGY.Warmonger)}");
				yield return WaitGameHours(2f, () => { note(); return fought = wolf == null || wolf.isDead || guards.Any(g => g.combatComponent.hostilesInRange.Contains(wolf)); });
				Check("a guard fights a wolf in the village at night", () =>
					(wolf != null && fought, $"wolf dead={wolf?.isDead} faction={wolf?.faction?.name} resting={wolf?.traitContainer.HasTrait("Resting")} at {wolf?.gridTileLocation?.localPlace} "
					+ $"in village={wolf?.gridTileLocation?.IsPartOfSettlement(village)}; {guard.name} hostile to it={wolf != null && guard.IsHostileWith(wolf)} "
					+ $"distance={(wolf?.gridTileLocation == null || guard.gridTileLocation == null ? -1 : guard.gridTileLocation.GetDistanceTo(wolf.gridTileLocation)):F0} doing={guard.currentActionNode?.goapName ?? "nothing"} "
					+ $"resting={guard.traitContainer.HasTrait("Resting")}; guards fighting it: {string.Join(", ", guards.Where(g => wolf != null && g.combatComponent.hostilesInRange.Contains(wolf)).Select(g => g.name))}; seen during the wait: [{string.Join("; ", seen)}]"));
				Guard("remove the wolf", () => { if (wolf != null && !wolf.isDead) { wolf.Death("autotest"); } return wolf; });
			}

			// 5. Kept in the save file.
			string saved = null;
			yield return SaveAndRead("ruinarch.plus.watch.json", (j, e) => saved = j);
			List<Character> before = PlusBridge.GuardsOf(village);
			if (saved != null)
			{
				ReplayLoad("ruinarch.plus.watch.json", saved);
			}
			Check("the night watch is stored in the save file and comes back", () =>
				(saved != null && before.All(g => saved.Contains(g.persistentID)) && PlusBridge.GuardsOf(village).Count == before.Count,
				$"saved={(saved == null ? "none" : saved.Length + " bytes")} guards before={before.Count} after load={PlusBridge.GuardsOf(village).Count}"));

			// 6. Switched off: guards go back to the usual schedule and stop patrolling.
			List<Character> was = PlusBridge.GuardsOf(village);
			PlusBridge.SetConfig("nightWatchEnabled", false);
			Try("run the night watch check", PlusBridge.WatchCheck);
			Check("switched off, the guards are released", () =>
				(PlusBridge.GuardsOf(village).Count == 0 && was.All(g => g.isDead || (!(g.dailyScheduleComponent.schedule is NocturnalSchedule) || g.traitContainer.HasTrait("Nocturnal")) && !g.behaviourComponent.HasBehaviour(typeof(NightPatrolBehaviour))),
				string.Join(", ", was.Select(g => $"{g.name}: schedule={g.dailyScheduleComponent.schedule?.GetType().Name} patrol={g.behaviourComponent.HasBehaviour(typeof(NightPatrolBehaviour))}"))));
			PlusBridge.SetConfig("nightWatchEnabled", true);
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
			float strandedAt = GameHours;
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
			// Someone may come across the captive first and free them or carry them home (the
			// base game's reaction to a restrained ally), or just pass by and see them: then
			// nobody misses them, rightly. Seen by a passer-by shows as a last sighting more
			// recent than the stranding.
			System.Text.RegularExpressions.Match ago = System.Text.RegularExpressions.Regex.Match(PlusBridge.MissingLastSeen(captive) ?? "", @"([\d.]+)h ago");
			bool seenSince = ago.Success && float.TryParse(ago.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hoursAgo)
				&& hoursAgo < GameHours - strandedAt - 0.5f;
			bool captiveFound = PlusBridge.MissingState(captive) == "Seen" && (captive.IsInHomeSettlement() || !captive.traitContainer.HasTrait("Restrained") || seenSince);
			if (captiveFound)
			{
				foreach (string name in new[] { "an unseen resident is reported missing", "the missing notice links to the person", "a captive left where they were seen is found and freed" })
				{
					Skip(name, $"{captive.name} was come across before anyone missed them (last seen {PlusBridge.MissingLastSeen(captive)}, home={captive.IsInHomeSettlement()}, restrained={captive.traitContainer.HasTrait("Restrained")})");
				}
			}
			else Check("an unseen resident is reported missing", () =>
			{
				string s = PlusBridge.MissingState(captive);
				bool announced = ModsLogHas($"{captive.name} of {village.name} has gone missing");
				return ((s == "Missing" || s == "Searching") && announced, $"state={s ?? "untracked"} announced={announced}");
			});
			if (!captiveFound) Check("the missing notice links to the person", () =>
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
			if (PlusBridge.MissingState(wanderer) == "Seen")
			{
				// Found by chance, before or just after a search was organised: a search follows
				// the latest sighting, which is now where they are.
				Skip("the search heads for the last-seen spot, not where they are", $"{wanderer.name} was come across before the search set out (last seen {PlusBridge.MissingLastSeen(wanderer)})");
				if (search == null)
				{
					Skip("the search is named as a search", "no search: the wanderer was found by chance");
				}
				else
				{
					Check("the search is named as a search", () => (search.GetPartyQuestName() == "Search for " + wanderer.name, "name=" + search.GetPartyQuestName()));
				}
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
			Func<bool> settled = () =>
				(PlusBridge.MissingState(wanderer) == "Lost"
					|| (PlusBridge.MissingState(wanderer) == "Seen" && PlusBridge.FailedSearches(wanderer) == 0))
				&& (PlusBridge.MissingState(victim) == null || (!victim.hasMarker && PlusBridge.MissingState(victim) == "Lost"))
				&& (captiveFound || !captive.traitContainer.HasTrait("Restrained") || captive.isDead);
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
			if (!captiveFound && captive.isDead && !ModsLogHas($"{captive.name} of {village.name} has been found."))
			{
				Skip("a captive left where they were seen is found and freed", $"{captive.name} died before anyone found them");
			}
			else if (!captiveFound) Check("a captive left where they were seen is found and freed", () =>
			{
				bool freed = !captive.traitContainer.HasTrait("Restrained");
				bool announced = ModsLogHas($"{captive.name} of {village.name} has been found.");
				// After being found, they may die or move away later; the record is then dropped.
				// Or they go missing again (killed out of sight, say): a new case, opened anew.
				string state = PlusBridge.MissingState(captive);
				bool again = ModsLogHasAfter($"{captive.name} of {village.name} has been found.", $"{captive.name} of {village.name} has gone missing");
				return (freed && announced && (state == "Seen" || state == null || again),
					$"freed={freed} announced={announced} missing again={again} state={state ?? $"dropped (dead={captive.isDead} home={captive.homeSettlement?.name ?? "-"})"}");
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
			// A body come across (by a passer-by, a search for someone else) or buried before
			// anyone missed the victim: they were never missing, so the record goes quietly, with
			// no "found dead".
			bool neverMissed = !ModsLogHas($"{victim.name} of {village.name} has gone missing") && PlusBridge.MissingState(victim) == null;
			// A body taken off the map before any search got there (eaten, butchered, rotted
			// away: the wild is full of monsters) cannot be found; the victim stays missing.
			// Only when every search ended with the body already gone; a search that ended
			// with the body still lying there is a real failure.
			string modsText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log"));
			List<string> searchEnds = modsText.Split('\n').Where(l => l.Contains($"Search for {victim.name} ended")).ToList();
			bool bodyGone = !victim.hasMarker && searchEnds.Count > 0 && searchEnds.All(l => l.TrimEnd().EndsWith("target dead, off map."));
			if (starved != null && PlusBridge.MissingState(victim) != null)
			{
				Skip("a resident killed out of sight is found dead", starved);
			}
			else if (neverMissed)
			{
				Skip("a resident killed out of sight is found dead", $"{victim.name}'s body was come across before anyone missed them (grave={(victim.grave != null ? "yes" : "no")})");
			}
			else if (bodyGone && PlusBridge.MissingState(victim) != null)
			{
				Skip("a resident killed out of sight is found dead", $"{victim.name}'s body left the map before any of {searchEnds.Count} search(es) got there (state={PlusBridge.MissingState(victim)})");
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

		// Destroys a building a test placed and takes out of the player settlement every area
		// no standing player building is on. Seen in runs: a destroyed building's area stayed
		// in the player settlement, and the player's demons patrol every area of it: they
		// crossed the map to it, killing villagers on the way. (A building already destroyed,
		// say by a counterattack, no longer lists its tiles: hence every empty area.) Demons
		// already out defending it keep fighting wherever they are: they are sent home.
		private static LocationStructure RemoveTestBuilding(LocationStructure building)
		{
			if (!building.hasBeenDestroyed)
			{
				building.AdjustHP(-building.currentHP);
			}
			PlayerSettlement ps = PlayerManager.Instance.player.playerSettlement;
			foreach (Area area in ps.areas.Where(a => !ps.allStructures.Any(s => !s.hasBeenDestroyed && s.HasTileOnArea(a))).ToList())
			{
				ps.RemoveAreaFromSettlement(area);
			}
			LocationStructure portal = ps.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL);
			LocationGridTile home = portal?.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? portal?.GetCenterTile();
			if (home != null)
			{
				foreach (Character demon in PlayerManager.Instance.player.playerFaction.characters.Where(c => c != null && !c.isDead && c.hasMarker
					&& c.gridTileLocation != null && !c.gridTileLocation.IsPartOfSettlement(ps)).ToList())
				{
					demon.combatComponent.ClearHostilesInRange(false);
					demon.combatComponent.ClearAvoidInRange(false);
					CharacterManager.Instance.Teleport(demon, home);
				}
			}
			return building;
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
			// Free and at home: a test's own captive or wanderer (restrained, far away; canMove
			// stays true while restrained) would take the quest and never get anywhere.
			List<Character> members = village.residents.Where(r => r != null && !r.isDead && r.marker != null
				&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive)
				&& r != village.ruler && r.limiterComponent.canMove && !PlusBridge.IsChild(r)
				&& !r.traitContainer.HasTrait("Restrained") && r.gridTileLocation != null && r.gridTileLocation.IsPartOfSettlement(village)).Take(max).ToList();
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
		// Each death is also logged as it happens; one with no killer and no interrupt names
		// the method that called Death, since "normal" alone says nothing.
		private static readonly Dictionary<string, int> Deaths = new Dictionary<string, int>();

		[HarmonyPatch(typeof(Character), nameof(Character.Death))]
		internal static class DeathTally
		{
			// The killer's combat reason is read before the death ends the fight.
			private static string _reason;

			private static void Prefix(Character __instance, Character responsibleCharacter, out bool __state)
			{
				__state = __instance.isDead;
				_reason = responsibleCharacter?.combatComponent.GetCombatData(__instance)?.reasonForCombat ?? "none";
			}

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
				string killer = responsibleCharacter == null ? "" : $" by {responsibleCharacter.name} ({responsibleCharacter.race}/{responsibleCharacter.characterClass.className}, {responsibleCharacter.faction?.name ?? "no faction"}"
					+ $", reason {_reason}, quest {responsibleCharacter.partyComponent.currentParty?.currentQuest?.GetType().Name ?? "none"}"
					+ $", from {responsibleCharacter.gridTileLocation?.localPlace}, portal {PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL)?.GetCenterTile()?.localPlace})";
				string caller = "";
				if (responsibleCharacter == null && interrupt == null)
				{
					caller = " via " + CallerChain();
				}
				_running.Log($"  death: {__instance.name} of {__instance.homeSettlement?.name ?? "no home"} ({__instance.faction?.name ?? "no faction"}) at {__instance.gridTileLocation?.localPlace} in {__instance.currentSettlement?.name ?? __instance.currentStructure?.name ?? "the wild"}: {key}{killer}{caller}");
			}
		}

		// The three game methods nearest the caller (Harmony's patched copies and the harness
		// itself left out).
		private static string CallerChain() => string.Join(" < ", new System.Diagnostics.StackTrace(2).GetFrames()
			.Select(f => f.GetMethod()).Where(m => m != null && m.Name != "Death" && !m.Name.Contains("::") && !m.Name.Contains("_Patch") && !(m.DeclaringType?.Namespace ?? "").StartsWith("RuinarchDebug"))
			.Take(3).Select(m => $"{m.DeclaringType?.Name}.{m.Name}"));

		// Creatures a test put in the world: their death (Summon.Death does not go through
		// Character.Death) and the removal of their marker are logged with who did it.
		private static readonly HashSet<Character> Watched = new HashSet<Character>();

		[HarmonyPatch(typeof(Summon), nameof(Summon.Death))]
		internal static class WatchedDeath
		{
			private static void Postfix(Summon __instance, string cause, Character responsibleCharacter)
			{
				if (_running != null && Watched.Contains(__instance))
				{
					_running.Log($"  watched {__instance.name} died ({cause}) at {__instance.gridTileLocation?.localPlace}"
						+ (responsibleCharacter != null ? $" by {responsibleCharacter.name} ({responsibleCharacter.characterClass.className}, {responsibleCharacter.faction?.name ?? "no faction"}) job {responsibleCharacter.currentJob?.jobType}" : " via " + CallerChain()));
				}
			}
		}

		[HarmonyPatch(typeof(Character), nameof(Character.DestroyMarker))]
		internal static class WatchedMarker
		{
			private static void Prefix(Character __instance)
			{
				if (_running != null && Watched.Contains(__instance) && __instance.hasMarker)
				{
					_running.Log($"  watched {__instance.name} (dead={__instance.isDead}) loses its marker at {__instance.gridTileLocation?.localPlace} via {CallerChain()}");
				}
			}
		}

		// The tests make factions unaware again (and aware again later); the game posts a new
		// "is now aware" alert each time and never takes one back. Take the faction's alerts
		// away when it becomes unaware, so the screen shows what is true now.
		[HarmonyPatch(typeof(Faction), nameof(Faction.SetIsAwareOfPlayer))]
		internal static class AwareAlertCleanup
		{
			private static void Postfix(Faction __instance, bool p_state)
			{
				if (_running == null || p_state || __instance.isAwareOfPlayer
					|| !(PlayerManager.Instance?.player?.bookmarkComponent?.bookmarkedObjects is Dictionary<BOOKMARK_CATEGORY, BookmarkCategory> all)
					|| !all.TryGetValue(BOOKMARK_CATEGORY.Alerts, out BookmarkCategory alerts))
				{
					return;
				}
				foreach (Quests.Alerts.FactionAwareAlert alert in alerts.bookmarked.OfType<Quests.Alerts.FactionAwareAlert>().Where(a => a.factionName == __instance.name).ToList())
				{
					alert.RemoveBookmark();
				}
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

		private int ModsLogLength()
		{
			string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
			return File.Exists(mods) ? File.ReadAllText(mods).Length : 0;
		}

		// Whether mods.log has <paramref name="text"/> after the first <paramref name="mark"/> characters.
		private bool ModsLogHasSince(int mark, string text)
		{
			string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
			string all = File.Exists(mods) ? File.ReadAllText(mods) : "";
			return all.Length > mark && all.IndexOf(text, mark, StringComparison.Ordinal) >= 0;
		}

		// The first mods.log line with <paramref name="text"/> after the first <paramref name="mark"/> characters, or null.
		private string ModsLogLineSince(int mark, string text)
		{
			string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
			string all = File.Exists(mods) ? File.ReadAllText(mods) : "";
			int at = all.Length > mark ? all.IndexOf(text, mark, StringComparison.Ordinal) : -1;
			if (at < 0)
			{
				return null;
			}
			int start = all.LastIndexOf('\n', at) + 1;
			int end = all.IndexOf('\n', at);
			return all.Substring(start, (end < 0 ? all.Length : end) - start).Trim();
		}

		// Whether mods.log has <paramref name="then"/> somewhere after the first <paramref name="first"/>.
		private bool ModsLogHasAfter(string first, string then)
		{
			string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_logPath)), "mods.log");
			string text = File.Exists(mods) ? File.ReadAllText(mods) : "";
			int at = text.IndexOf(first, StringComparison.Ordinal);
			return at >= 0 && text.IndexOf(then, at + first.Length, StringComparison.Ordinal) >= 0;
		}

		// Runs a suite so that an exception in it (or in a coroutine it yields) is a FAIL and
		// the run goes on with the next suite. Unity would end the whole run's coroutine, and
		// the game would carry on with nothing left testing it. Nested coroutines are stepped
		// here, since Unity runs those itself and an exception there never reaches this frame.
		private IEnumerator Safe(string suite, IEnumerator run)
		{
			Stack<IEnumerator> stack = new Stack<IEnumerator>();
			stack.Push(run);
			while (stack.Count > 0)
			{
				bool more;
				object current = null;
				try
				{
					more = stack.Peek().MoveNext();
					if (more)
					{
						current = stack.Peek().Current;
					}
				}
				catch (Exception e)
				{
					Fail($"{suite} crashed; its remaining checks did not run", e.ToString());
					yield break;
				}
				if (!more)
				{
					stack.Pop();
				}
				else if (current is IEnumerator inner)
				{
					stack.Push(inner);
				}
				else
				{
					yield return current;
				}
			}
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
			// As far from every settlement and every village spot as the rules allow: nobody
			// defends the Portal in an unattended run, and a Portal that villagers stumble on
			// early gets destroyed (the world ends in defeat mid-run). Homeless villagers found
			// new villages on the region's empty village spots mid-run, so those count too: a
			// village founded beside the Portal feeds its people to the Portal's defenders, whose
			// poison explosions wear the Portal down.
			List<LocationGridTile> settlementCentres = GridMap.Instance.mainRegion.settlementsInRegion
				.SelectMany(s => s.areas)
				.Concat(GridMap.Instance.mainRegion.villageSpots.SelectMany(v => v.reservedAreas.Append(v.coreSpot)))
				.Where(a => a != null).Select(a => a.gridTileComponent.centerGridTile).Where(t => t != null).ToList();
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
			RestoreSettings();
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

		// Why a Write or Read job left a villager's queue before it was done (a dropped job
		// shows only as "doing nothing" otherwise): the game's reason and the caller.
		[HarmonyPatch(typeof(JobQueue), nameof(JobQueue.RemoveJobInQueue))]
		internal static class RecordJobRemoved
		{
			private static void Prefix(JobQueue __instance, JobQueueItem job, string reason)
			{
				try
				{
					if (_running == null || !(job is GoapPlanJob plan)
						|| (plan.targetInteractionType != PlusBridge.RecordAction(true) && plan.targetInteractionType != PlusBridge.RecordAction(false)))
					{
						return;
					}
					string caller = string.Join(" < ", new System.Diagnostics.StackTrace().GetFrames().Skip(2).Take(9).Select(f => f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name));
					Character actor = __instance.owner;
					_running.Log($"  record job removed: {actor?.name} {plan.jobType} {plan.targetInteractionType.ToString()} at {plan.targetPOI?.name} reason='{reason}'"
						+ $" fullness={actor?.needsComponent.fullness} tiredness={actor?.needsComponent.tiredness} resting={actor?.traitContainer.HasTrait("Resting")} party={actor?.partyComponent.hasParty} via {caller}");
				}
				catch
				{
				}
			}
		}

		// Who unties a jailed ex-ruler behind the mod's back (the mod lets go of its hold before
		// removing the trait itself, so only an outside removal is logged). The last way each
		// was untied is kept for the jailing check.
		[HarmonyPatch(typeof(Traits.Restrained), nameof(Traits.Restrained.OnRemoveTrait))]
		internal static class HeldUntied
		{
			private static readonly Dictionary<Character, string> Last = new Dictionary<Character, string>();
			private static readonly Dictionary<Character, Character> Releaser = new Dictionary<Character, Character>();

			internal static string How(Character c) => c != null && Last.TryGetValue(c, out string how) ? how : null;

			internal static Character By(Character c) => c != null && Releaser.TryGetValue(c, out Character by) ? by : null;

			private static void Prefix(Traits.ITraitable sourceCharacter, Character removedBy)
			{
				try
				{
					if (_running == null || !(sourceCharacter is Character c) || PlusBridge.HeldState(c) == null)
					{
						return;
					}
					string caller = string.Join(" < ", new System.Diagnostics.StackTrace().GetFrames().Skip(2).Take(10).Select(f => f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name));
					Last[c] = caller;
					Releaser[c] = removedBy;
					_running.Log($"  held ex-ruler untied: {c.name} ({PlusBridge.HeldState(c)}, {c.characterClass?.className}) by {removedBy?.name ?? "nobody"} (friend={removedBy != null && removedBy.relationshipContainer.IsFriendsWith(c)}, home={removedBy?.homeSettlement?.name ?? "none"}) in {c.currentStructure?.name ?? "the wild"} via {caller}");
				}
				catch
				{
				}
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

		// Who hurts the Portal, tallied between suites: damage by attacker faction and quest.
		[HarmonyPatch(typeof(LocationStructure), nameof(LocationStructure.AdjustHP))]
		internal static class PortalDamaged
		{
			private static readonly Dictionary<string, int> Tally = new Dictionary<string, int>();

			private static void Prefix(LocationStructure __instance, int amount, Character p_responsibleCharacter)
			{
				if (amount >= 0 || !(__instance is ThePortal))
				{
					return;
				}
				string key = p_responsibleCharacter == null ? "nobody named"
					: $"{p_responsibleCharacter.faction?.name ?? "no faction"}/{p_responsibleCharacter.partyComponent.currentParty?.currentQuest?.GetType().Name ?? "no quest"}";
				Tally.TryGetValue(key, out int sum);
				Tally[key] = sum - amount;
			}

			/// <summary>"; damaged by ..." since the last call, or nothing; then starts over.</summary>
			internal static string TakeTally()
			{
				if (Tally.Count == 0)
				{
					return "";
				}
				string text = "; damaged by " + string.Join(", ", Tally.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
				Tally.Clear();
				return text;
			}
		}
	}
}
