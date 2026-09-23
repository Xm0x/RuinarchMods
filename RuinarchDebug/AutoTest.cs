using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
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
			yield return WaitGameHours(1f, null);

			yield return MassGraveSuite();

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
				// No village starts with a Cemetery: build a real one in the test village. Its
				// Mass Grave still stands, which also tests precedence (people -> Cemetery,
				// creatures -> Mass Grave).
				LocationStructure cemetery = Guard("build a vanilla Cemetery", () => InstantBuildVanilla(bare, STRUCTURE_TYPE.CEMETERY));
				Log(cemetery != null ? $"  built a Cemetery in {bare.name} for the precedence test" : "  could not build a Cemetery");
				withCemetery = cemetery != null ? bare : null;
			}
			if (withCemetery == null)
			{
				Skip("cemetery village keeps vanilla burial", "no village has or could build a Cemetery");
			}
			else
			{
				yield return CemeteryVillageTest(withCemetery);
			}

			yield return DecayTest();

			// The debug menu's "Place Mass Grave" path: an instant, real build in a village.
			NPCSettlement target = bare ?? villages.FirstOrDefault();
			if (target != null)
			{
				LocationStructure built = Guard("instant-build a Mass Grave", () => PlusBridge.InstantBuild(target));
				Check("instant build creates a real Mass Grave in the village", () =>
					(built != null && PlusBridge.IsMassGrave(built) && built.settlementLocation == target && built.tiles.Count > 0,
					built == null ? "no valid spot / null" : $"settlement={built.settlementLocation?.name} tiles={built.tiles.Count} type={built.structureType.ToStringEnum()}"));
			}
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
				(first.grave == null && first.hasMarker, $"grave={(first.grave != null)} hasMarker={first.hasMarker}"));

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

			// 2. The village decides to build a Mass Grave and villagers construct it.
			float start = GameHours;
			bool queued = false;
			yield return WaitGameHours(120f, () =>
			{
				if (!queued && (village.HasJob(JOB_TYPE.PLACE_BLUEPRINT) || PlusBridge.HasPendingBlueprint(village)))
				{
					queued = true;
					Log($"  blueprint job/pending seen after {GameHours - start:F1}h");
				}
				return PlusBridge.FindFor(village) != null;
			});
			LocationStructure pit = PlusBridge.FindFor(village);
			Check("villagers build a Mass Grave from materials", () =>
				(pit != null, pit != null ? $"built after {GameHours - start:F1}h" : $"not built within 120h (blueprint seen={queued}, pending={PlusBridge.HasPendingBlueprint(village)})"));
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

			// 3. Villagers carry a fresh corpse into the pit.
			int hauledBefore = PlusBridge.HauledTotal;
			Character second = Guard("kill resident", () => KillResident(village));
			if (second == null)
			{
				Skip("villagers haul a resident", "no living resident left");
			}
			else
			{
				yield return WaitGameHours(24f, () => second.grave != null || !second.hasMarker);
				Check("villagers haul a resident's corpse into the pit", () =>
				{
					bool inPit = second.grave != null && PlusBridge.IsMassGrave(second.grave.gridTileLocation?.structure);
					bool hauled = PlusBridge.HauledTotal > hauledBefore;
					return (inPit && hauled, $"grave={(second.grave != null)} inPit={inPit} hauledDelta={PlusBridge.HauledTotal - hauledBefore} absorbed={PlusBridge.AbsorbedTotal}");
				});
			}

			// The corpse that was lying before the pit existed must be taken too.
			yield return WaitGameHours(24f, () => first.grave != null || !first.hasMarker);
			Check("pre-existing corpse is laid in the pit once it exists", () =>
			{
				bool inPit = first.grave != null && PlusBridge.IsMassGrave(first.grave.gridTileLocation?.structure);
				return (inPit || !first.hasMarker, $"grave={(first.grave != null)} inPit={inPit} hasMarker={first.hasMarker}");
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
			Log($"  pit bodyCount={PlusBridge.BodyCount(pit)} hauledTotal={PlusBridge.HauledTotal} absorbedTotal={PlusBridge.AbsorbedTotal}");
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
			yield return WaitGameHours(24f, () => dead.grave != null);
			Check("cemetery village still buries its dead in the Cemetery", () =>
			{
				STRUCTURE_TYPE? where = dead.grave?.gridTileLocation?.structure?.structureType;
				return (where == STRUCTURE_TYPE.CEMETERY, $"grave={(dead.grave != null)} structure={where}");
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

		private static string Describe(NPCSettlement s)
		{
			return $"{s.name}[alive={s.residents.Count(r => r != null && !r.isDead)} cemetery={s.HasStructure(STRUCTURE_TYPE.CEMETERY)} cult={s.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE)} lodge={s.HasStructure(STRUCTURE_TYPE.HUNTER_LODGE)}]";
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

		// Build a vanilla structure instantly at a spot the game's own placement approves.
		private static LocationStructure InstantBuildVanilla(NPCSettlement village, STRUCTURE_TYPE type)
		{
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
