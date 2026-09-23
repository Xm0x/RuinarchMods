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
			test._logPath = Path.Combine(modDirectory, "autotest.log");
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
		private IEnumerator MassGraveSuite()
		{
			if (!PlusBridge.Available)
			{
				Skip("mass grave suite", "RuinarchPlus not loaded");
				yield break;
			}

			Check("framework names virtual structure", () =>
			{
				var t = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.mass_grave");
				string n = t.LocalizedStructureName();
				string e = t.ToStringEnum();
				return (n == "Mass Grave" && e == "MASS_GRAVE", $"LocalizedStructureName='{n}' ToStringEnum='{e}'");
			});

			List<NPCSettlement> villages = Villages();
			Log($"villages: {string.Join(", ", villages.Select(Describe))}");
			NPCSettlement bare = villages.FirstOrDefault(v => !v.HasStructure(STRUCTURE_TYPE.CEMETERY) && !v.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE));
			NPCSettlement withCemetery = villages.FirstOrDefault(v => v.HasStructure(STRUCTURE_TYPE.CEMETERY));

			// Freshly generated villages must count as healthy, or migration would be throttled
			// from day one.
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

// The debug menu's "Place Mass Grave" path. A village never gets a second pit.
			STRUCTURE_TYPE pitType = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.mass_grave");
			int PitCount(NPCSettlement v) => v.structures.TryGetValue(pitType, out List<LocationStructure> l) ? l.Count(x => !x.hasBeenDestroyed) : 0;
			NPCSettlement withPit = villages.FirstOrDefault(v => PitCount(v) > 0);
			if (withPit != null)
			{
				int before = PitCount(withPit);
				LocationStructure again = Guard("instant-build in a village that has one", () => PlusBridge.InstantBuild(withPit));
				Check("a village never gets a second Mass Grave", () =>
					(PitCount(withPit) == before && again == PlusBridge.FindFor(withPit), $"{withPit.name}: pits {before} -> {PitCount(withPit)}"));
			}
			NPCSettlement withoutPit = villages.FirstOrDefault(v => PitCount(v) == 0);
			if (withoutPit != null)
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
			// The least-touched village: most residents, not thinned out by earlier tests.
			NPCSettlement village = Villages().Where(v => !v.isPlagued && v.eventManager.GetActiveEvent<PlaguedEvent>() == null)
				.OrderByDescending(v => PlusBridge.MigrationMultiplier(v, out _))
				.ThenByDescending(v => v.residents.Count(r => r != null && !r.isDead)).FirstOrDefault();
			if (village == null)
			{
				Skip("curfew", "no village free of plague");
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
			Check("villagers build a Mass Grave from materials", () =>
				(pit != null, pit != null ? $"built after {GameHours - start:F1}h" : $"not built within 120h (blueprint seen={queued}, pending={PlusBridge.HasPendingBlueprint(village)})"));
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
					under = Math.Max(under, HomeShare(village, out count));
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
			bool Candidate(NPCSettlement v) => v.residents.Count(r => r != null && !r.isDead) >= 4 && !v.isPlagued
				&& v.eventManager.GetActiveEvent<PlaguedEvent>() == null;
			NPCSettlement village = villages.Where(v => Candidate(v) && !v.isUnderSiege)
				.OrderByDescending(v => PlusBridge.MigrationMultiplier(v, out _)).FirstOrDefault();
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
				(gainCollapsed == 0 && collapseWhy != null && collapseWhy.Contains("mostly abandoned"), $"gain {gainCollapsed} (vanilla {vanilla}); x{collapsed:0.##} {collapseWhy}"));
		}

		// Share of the village's curfew-bound residents (alive, not ruler/leader, with a home)
		// currently in their home structure. Only residents with no job and not plagued or
		// quarantined count: the curfew governs idle free time, while work (plague care,
		// burials) goes on and the sick are held or cared for elsewhere by design.
		private static float HomeShare(NPCSettlement village, out int count)
		{
			List<Character> bound = village.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && !r.isSettlementRuler
				&& !r.isFactionLeader && r.homeStructure != null && !r.homeStructure.hasBeenDestroyed
				&& r.currentJob == null && !r.traitContainer.HasTrait("Plagued", "Quarantined")).ToList();
			count = bound.Count;
			return count == 0 ? 0f : bound.Count(r => r.isAtHomeStructure) / (float)count;
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
			float start = GameHours;
			string lastStage = firstStage;
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
			Character victim = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.gridTileLocation != null
				&& r.gridTileLocation.IsPartOfSettlement(village) && r != village.ruler)
				?? village.residents.FirstOrDefault(r => r != null && !r.isDead && r.gridTileLocation != null
				&& r.gridTileLocation.IsNextToOrPartOfSettlement(village) && r != village.ruler);
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
					// Villager parties accept quests before dawn (Party.InitialScheduleToCheckQuest,
					// 5-7 am) and set out when that day's Work shift starts; a party that accepts
					// after the shift began never leaves. So form it at 5 am, as the game would.
					yield return WaitForHour(5);
					// Villagers sitting in an idle party (no quest) are free to join.
					List<Character> fighters = village.residents.Where(r => r != null && !r.isDead && r.marker != null
						&& (!r.partyComponent.hasParty || !r.partyComponent.currentParty.isActive)
						&& r != village.ruler && r.limiterComponent.canMove).Take(4).ToList();
					foreach (Character f in fighters.Where(f => f.partyComponent.hasParty).ToList())
					{
						Guard("leave idle party", () => { f.partyComponent.currentParty.RemoveMember(f); return f; });
					}
					if (fighters.Count < 3)
					{
						Log($"  only {fighters.Count} free resident(s) for a party; a counterattack needs 3");
					}
					else
					{
						Guard("form a counterattack party", () =>
						{
							Party formed = PartyManager.Instance.CreateNewParty(fighters[0]);
							foreach (Character f in fighters.Skip(1))
							{
								formed.AddMember(f);
							}
							formed.TryAcceptQuest(quest, fighters[0]);
							foreach (Character f in fighters)
							{
								formed.AddMemberThatJoinedQuest(f);
							}
							return formed;
						});
						Log($"  formed a party of {fighters.Count}: {string.Join(", ", fighters.Select(f => f.name))}");
					}
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
		}

		// The ledger rides inside the real save zip and comes back through the real load hook.
		private IEnumerator KnowledgeSaveRoundTrip(Faction faction, LocationStructure portal)
		{
			if (!PlusBridge.Knows(faction, portal))
			{
				PlusBridge.Learn(faction, portal);
			}
			const string saveName = "RuinarchPlus-autotest";
			string zip = Path.Combine(UtilityScripts.Utilities.gameSavePath, saveName + ".zip");
			SaveCurrentProgressManager saver = SaveManager.Instance.saveCurrentProgressManager;
			Try("delete an old test save", () => { if (File.Exists(zip)) File.Delete(zip); });
			// The game only ever saves paused (autosave pauses; manual saves come from the paused
			// menu). Saving a running world races its save threads against live log objects and
			// can hang the save forever, so pause like the game does.
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
					ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("ruinarch.plus.knowledge.json"));
					if (entry != null)
					{
						using (StreamReader r = new StreamReader(entry.Open()))
						{
							json = r.ReadToEnd();
						}
					}
				}
			});
			Check("knowledge is stored inside the player's save file", () =>
				(json != null && json.Contains(portal.persistentID), json == null ? "entries: " + entries : $"{json.Length} bytes: {json.Substring(0, Math.Min(json.Length, 160))}"));

			if (json != null)
			{
				// Replay the load: put the file where the game extracts saves, then run the game's
				// own "save finished loading" step.
				PlusBridge.Forget(faction);
				string dir = Path.Combine(UtilityScripts.Utilities.tempPath, "ModData");
				Try("stage the extracted save data", () => { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "ruinarch.plus.knowledge.json"), json); });
				Try("run the game's load-finished step", () => SaveManager.Instance.DeleteSaveFilesInTempDirectory());
				Check("knowledge comes back when the save loads", () => (PlusBridge.Knows(faction, portal), $"known after load={PlusBridge.Knows(faction, portal)}"));
				Try("clean up staged data", () => Directory.Delete(dir, true));
			}
			Try("delete the test save", () => { if (File.Exists(zip)) File.Delete(zip); });
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
			foreach (Area area in GridMap.Instance.mainRegion.areas)
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
	}
}
