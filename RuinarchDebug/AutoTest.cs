using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
			File.Delete(flag);
			var go = new GameObject("RuinarchAutoTest");
			DontDestroyOnLoad(go);
			var test = go.AddComponent<AutoTest>();
			_running = test;
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
			// Popups/events may pause the game; the test must keep time moving.
			if (GameManager.Instance.isPaused && UIManager.Instance != null)
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
			// Corpse-borne plague is opt-in and not under test; left on, it slowly empties the
			// villages the later tests need. In memory only: config.json is untouched.
			PlusBridge.SetConfig("corpseDiseaseEnabled", false);
			yield return WaitGameHours(1f, null);

			FreshWorldChecks();
			// Early, while villagers are out walking; leaves the camera as it found it.
			yield return PathLineTest();
			// Needs three free villagers of one village, so it runs while the villages are
			// full. Strands them in the wilderness; one never comes back.
			yield return MissingPersonsSuite();
			yield return MassGraveSuite();
			// Knowledge takes the village with the most people left (the one the burial tests
			// spared).
			yield return KnowledgeSuite();
			// Waits ~72 in-game hours, during which villages lose people to the world.
			yield return DecayTest();
			// After the burial and knowledge tests: a plague answered with Exile makes the
			// plagued Criminals, and Criminals take no village jobs (burial, construction).
			yield return CurfewSuite();
			// Last: it wipes a village out, which can end the world (player victory, and the
			// game repopulating empty villages with new factions).
			yield return MigrationSuite();

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
			// from day one.
			List<NPCSettlement> villages = Villages();
			foreach (NPCSettlement v in villages)
			{
				float m = PlusBridge.MigrationMultiplier(v, out string why);
				Log($"  migration health {v.name}: x{m:0.##} emptyHomes={v.GetNumberOfUnoccupiedStructure(STRUCTURE_TYPE.DWELLING)} {why}");
			}
			Check("freshly generated villages draw settlers normally", () =>
			{
				List<NPCSettlement> throttled = villages.Where(v => PlusBridge.MigrationMultiplier(v, out _) < 1f).ToList();
				return (villages.Count > 0 && throttled.Count == 0,
					throttled.Count == 0 ? $"all {villages.Count} at x1" : "throttled: " + string.Join(", ", throttled.Select(v => v.name)));
			});
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
					Check("a real Cemetery never carries the Mass Grave art", () => (Overlay(cemetery) == null, Overlay(cemetery) == null ? "no overlay" : "overlay present"));
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
				return PlusBridge.FindFor(village) != null;
			});
			LocationStructure pit = PlusBridge.FindFor(village);
			if (pit == null && !queued && !HasRoomFor(village, STRUCTURE_TYPE.CEMETERY))
			{
				Skip("villagers build a Mass Grave from materials", $"{village.name} has no room for a 5x5 building (the game's own placement check)");
			}
			else
			{
				Check("villagers build a Mass Grave from materials", () =>
					(pit != null, pit != null ? $"built after {GameHours - start:F1}h" : $"not built within 120h (blueprint seen={queued}, pending={PlusBridge.HasPendingBlueprint(village)})"));
			}
			if (buildText == null)
			{
				Skip("the builder's action names the Mass Grave", "no builder seen mid-build");
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
			Check("mass grave wears its own pit art (not the Cemetery look)", () =>
			{
				SpriteRenderer overlay = Overlay(pit);
				return (overlay != null && overlay.enabled && overlay.sprite != null,
					overlay == null ? "no overlay" : $"sprite={overlay.sprite?.name} {overlay.sprite?.rect.width}px layer={overlay.sortingLayerName}/{overlay.sortingOrder} scale={overlay.transform.lossyScale.x:F3}");
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
				// Laid in the pit = carried there and gone: no body, no gravestone anywhere.
				Check("villagers haul a resident's corpse into the pit (no gravestone)", () =>
				{
					bool gone = !second.hasMarker && second.grave == null;
					bool hauled = PlusBridge.HauledTotal > hauledBefore;
					return (gone && hauled, $"gone={gone} hauledDelta={PlusBridge.HauledTotal - hauledBefore} absorbed={PlusBridge.AbsorbedTotal} jobQueued={second.HasJobTargetingThis(JOB_TYPE.BURY, JOB_TYPE.BURY_IN_ACTIVE_PARTY) || village.HasJob(JOB_TYPE.BURY, second)} {GraveWhere(second, village)}");
				});
			}

			// The corpse that was lying before the pit existed must be taken too.
			yield return WaitGameHours(24f, () => first.grave != null || !first.hasMarker);
			Check("pre-existing corpse is laid in the pit once it exists", () =>
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
				Check("under curfew, residents stay home in their free time", () =>
					(count > 0 && under > baseline && (under >= 0.6f || under - baseline >= 0.25f), $"home share {under:P0} of {count} (before plague {baseline:P0})"));
				Guard("end plague event", () => { village.eventManager.DeactivateEvent(plague); return plague; });
				Check("curfew lifts when the plague event ends", () =>
					(!PlusBridge.IsUnderCurfew(village), $"underCurfew={PlusBridge.IsUnderCurfew(village)}"));
			}
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
			Check("cemetery village still buries its dead in the Cemetery", () =>
			{
				STRUCTURE_TYPE? where = dead.grave?.gridTileLocation?.structure?.structureType;
				return (where == STRUCTURE_TYPE.CEMETERY, $"grave={(dead.grave != null)} structure={where} jobs: {(buryJobs.Count == 0 ? "none seen" : string.Join("; ", buryJobs))}");
			});

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
				Guard("hover the body", () => { body.CenterOnCharacter(); cam.orthographicSize = 4f; InnerMapManager.Instance.SetCurrentlyHoveredPOI(body); return body; });
				yield return null;
				yield return null;
				float fill = PlusBridge.DecayBarFill(body);
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
					(fill >= 0f && left >= 0f && Math.Abs(fill - left) < 0.05f && fill < 0.8f && fill > 0.3f, $"bar fill={fill:F2} decay left={left:F2} stage={PlusBridge.DecayStage(body)}"));
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

		private const string OverlayName = "RuinarchPlus.MassGraveOverlay";

		private static SpriteRenderer Overlay(LocationStructure structure)
		{
			Transform t = (structure as ManMadeStructure)?.structureObj?.transform.Find(OverlayName);
			return t != null ? t.GetComponent<SpriteRenderer>() : null;
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
			Character held = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.isNormalCharacter && r.race.IsSapient()
				&& r != village.ruler && !r.partyComponent.hasParty);
			LocationGridTile heldHome = held?.gridTileLocation;
			LocationGridTile inPortal = portal.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? portal.tiles.FirstOrDefault();
			LocationGridTile inOutpost = outpost.passableTiles.FirstOrDefault(t => !t.isOccupied) ?? outpost.tiles.FirstOrDefault();
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
				string unknown = Guard("ask for a rescue from the portal", () => Rescue(inPortal));
				string known = Guard("ask for a rescue from the outpost", () => Rescue(inOutpost));
				Guard("put the resident back", () => { CharacterManager.Instance.Teleport(held, heldHome); return held; });
				PlusBridge.Forget(faction);
				PlusBridge.Learn(faction, outpost);
				Check("rescues go only to buildings the faction knows", () =>
					(unknown != null && unknown.EndsWith("no rescue") && known != null && known.EndsWith("rescue to " + outpost.name),
					$"{held.name} {unknown}; {known}; village searching for the demonic area={village.HasJob(JOB_TYPE.SEARCH_FOR_DEMONIC_AREA)}"));
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
			// Done with the counterattack: call it off now. Once the party has seen the Portal
			// (which is allowed) it goes for it, and a destroyed Portal ends the run in defeat.
			CallOffCounterattacks(faction);

			// Seeing teaches: a villager of the (aware) faction standing by the portal.
			PlusBridge.Forget(faction);
			Character witness = Villages().Where(v => v.owner == faction).SelectMany(v => v.residents).FirstOrDefault(r => r != null && !r.isDead && r.marker != null && r.limiterComponent.canWitness
				&& r.race.IsSapient() && !r.isAlliedWithPlayer && !r.partyComponent.hasParty);
			LocationGridTile near = portal.tiles.SelectMany(t => t.neighbourList).FirstOrDefault(t => t != null && t.structure != portal && !t.isOccupied);
			if (witness == null || near == null)
			{
				Skip("a villager who sees the portal teaches it to their faction", witness == null ? "no free witness" : "no free tile by the portal");
			}
			else
			{
				Guard("place a witness by the portal", () => { witness.marker.PlaceMarkerAt(near); return witness; });
				yield return WaitGameHours(3f, () => PlusBridge.Knows(faction, portal));
				Check("a villager who sees the portal teaches it to their faction", () =>
					(PlusBridge.Knows(faction, portal), $"witness {witness.name} at {near.localPlace}; known={PlusBridge.Knows(faction, portal)}"));
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
				&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive);
			NPCSettlement village = Villages().Where(v => v.owner != null && v.owner.isMajorNonPlayer && !v.isPlagued)
				.OrderByDescending(v => v.residents.Count(r => free(v, r))).FirstOrDefault();
			// Villagers in no party first: taking someone out of an idle party can empty it,
			// and an empty party disbands.
			List<Character> people = village?.residents.Where(r => free(village, r)).OrderBy(r => r.partyComponent.hasParty).ToList();
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
			// The one nobody should stumble on: as far from every settlement as possible.
			LocationGridTile far = wild.Skip(3).OrderByDescending(t => settlementCentres.Min(c => c.GetDistanceTo(t))).First();
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
			Log($"  captive {captive.name} restrained={captive.traitContainer.HasTrait("Restrained")}, wanderer {wanderer.name} restrained={wanderer.traitContainer.HasTrait("Restrained")}, victim {victim.name} dead={victim.isDead}");

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
			Check("the search heads for the last-seen spot, not where they are", () =>
			{
				IPartyTargetDestination d = search?.GetTargetDestination();
				bool atLastSeen = d == spotWanderer.area || d == spotWanderer.structure;
				bool atLive = d == far.area || d == far.structure;
				return (search != null && atLastSeen && !atLive,
					$"destination={(d as Area)?.name ?? (d as LocationStructure)?.name ?? "none"} lastSeen={spotWanderer.area.name} live={far.area.name}");
			});
			Check("the search is named as a search", () => (search?.GetPartyQuestName() == "Search for " + wanderer.name, "name=" + (search?.GetPartyQuestName() ?? "no search")));

			// 4-6. Let the searches run. Parties take quests at dawn; if none took a search by
			// 8 am, form one at the next 5 am as the game would.
			float start = GameHours;
			List<float> gaps = new List<float>();
			string wandererWas = PlusBridge.MissingState(wanderer);
			string captiveWas = PlusBridge.MissingState(captive);
			Func<bool> settled = () => PlusBridge.MissingState(wanderer) == "Lost" && PlusBridge.MissingState(victim) == null
				&& !captive.traitContainer.HasTrait("Restrained");
			while (GameHours - start < 160f && !settled())
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
				return (freed && announced && PlusBridge.MissingState(captive) == "Seen", $"freed={freed} announced={announced} state={PlusBridge.MissingState(captive)}");
			});
			Check("a resident killed out of sight is found dead", () =>
			{
				bool announced = ModsLogHas($"{victim.name} of {village.name} has been found dead.");
				return (announced && PlusBridge.MissingState(victim) == null, $"announced={announced} state={PlusBridge.MissingState(victim) ?? "dropped"}");
			});
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

			// 7. Records ride inside the real save. (Replaying the load hands every other
			// ModSave handler load(null): the knowledge ledger is emptied, which no later
			// suite reads.)
			string json = null;
			string entries = null;
			yield return SaveAndRead("ruinarch.plus.missing.json", (j, e) => { json = j; entries = e; });
			Check("missing persons are stored inside the player's save file", () =>
				(json != null && json.Contains(wanderer.persistentID), json == null ? "entries: " + entries : $"{json.Length} bytes"));
			if (json != null)
			{
				string before = PlusBridge.MissingState(wanderer);
				PlusBridge.ClearMissing();
				ReplayLoad("ruinarch.plus.missing.json", json);
				Check("missing persons come back when the save loads", () =>
					(before != null && PlusBridge.MissingState(wanderer) == before, $"before={before ?? "none"} after load={PlusBridge.MissingState(wanderer) ?? "none"}"));
			}

			PlusBridge.SetConfig("missingAfterHours", 24);
			PlusBridge.SetConfig("searchSweepHours", 6);
			PlusBridge.SetConfig("searchRetryHours", 24);
			PlusBridge.SetConfig("searchMaxAttempts", 3);
		}

		// A demonic structure at a spot the portal's own placement rules approve, well away
		// from the portal (so seeing one is not seeing the other) and near the village.
		private static LocationStructure PlaceDemonicStructure(LocationStructure portal, NPCSettlement village)
		{
			LocationGridTile portalTile = portal.tiles.First();
			LocationGridTile villageTile = village.areas[0].gridTileComponent.centerGridTile;
			foreach (STRUCTURE_TYPE type in new[] { STRUCTURE_TYPE.KENNEL, STRUCTURE_TYPE.WATCHER, STRUCTURE_TYPE.SPIRE })
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
				IEnumerable<Area> spots = GridMap.Instance.mainRegion.areas
					.Where(a => a.gridTileComponent.centerGridTile != null && !a.HasSettlementOnArea() && !a.IsNextToOrPartOfVillage()
						&& a.gridTileComponent.centerGridTile.GetDistanceTo(portalTile) >= 25f)
					.OrderBy(a => a.gridTileComponent.centerGridTile.GetDistanceTo(villageTile));
				foreach (Area area in spots)
				{
					LocationGridTile tile = area.gridTileComponent.centerGridTile;
					if (prefab.HasEnoughSpaceIfPlacedOn(tile, out string _) && area.structureComponent.CanBuildDemonicStructureHere(type, out string _))
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
				&& r != village.ruler && r.limiterComponent.canMove).Take(max).ToList();
			if (members.Count < min)
			{
				Log($"  only {members.Count} free resident(s) for a party; {quest.partyQuestType} needs {min}");
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
			// Saving a running world races its save threads against live log objects and can
			// hang the save forever, so pause like the game does.
			Try("pause for the save", () => UIManager.Instance.Pause());
			Try("save the game", () => saver.DoManualSave(saveName));
			yield return WaitReal(() => File.Exists(zip) && !saver.isSaving && !saver.isWritingToDisk, 180f, "the test save to be written");
			Try("resume after the save", () =>
			{
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

		// Replays loading: puts the entry where the game extracts saves, then runs the game's
		// own "save finished loading" step. Every other ModSave handler gets load(null).
		private void ReplayLoad(string entry, string json)
		{
			string dir = Path.Combine(UtilityScripts.Utilities.tempPath, "ModData");
			Try("stage the extracted save data", () => { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, entry), json); });
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
	}
}
