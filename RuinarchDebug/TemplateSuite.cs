using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent.Templates;
using Ruinarch.Modding;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinarchDebug
{
	// Building templates (Ruinarch.ModContent/Templates): the JSON format, export from game
	// prefabs, the builder, variants in the game's lists, packs on disk.
	public partial class AutoTest
	{
		private IEnumerator TemplateSuite()
		{
			// 1. The JSON format: written, read back, written again: the same text. A misspelled
			// field is an error, not silently dropped.
			BuildingTemplate sample = new BuildingTemplate
			{
				id = "autotest/sample",
				name = "Sample",
				kind = "TAVERN",
				cultures = new List<string> { "Human_Empire" },
				material = "WOOD",
				size = new[] { 2, 1 },
				center = new[] { 0, 0, 0 },
				bounds = new[] { 0, 0, 2, 1 },
				palette = new Dictionary<string, string> { ["a"] = "game:Floor" },
				floor = new List<string> { "a." },
				objects = new List<TemplateObject> { new TemplateObject { type = "TABLE", pos = new[] { 0.5f, 0.5f, 0f }, rot = 90f, sprite = "game:Table" } },
				entrances = new List<TemplatePoint> { new TemplatePoint { pos = new[] { 1.5f, 0.5f, 0f } } },
				rooms = new List<List<int[]>> { new List<int[]> { new[] { 0, 0, 0 } } },
			};
			string json = ModTemplates.ToJson(sample);
			string again = ModTemplates.ToJson(ModTemplates.FromJson(json));
			Check("a template survives a trip through JSON", () =>
				(json == again && json.Contains("\"kind\": \"TAVERN\""), json == again ? $"{json.Length} chars" : "differs: " + again));
			string error = null;
			try
			{
				ModTemplates.FromJson(json.Replace("\"material\"", "\"materal\""));
			}
			catch (TemplateException e)
			{
				error = e.Message;
			}
			Check("a misspelled template field is reported, not ignored", () => (error != null && error.Contains("materal"), error ?? "accepted"));
			// 2. Export: every painted cell, thin wall, object and entrance of a game building
			// ends up in its template.
			List<GameLook> picks = TemplatePicks();
			if (picks.Count == 0)
			{
				Skip("exporting a game building captures every tile, wall, object and entrance", "no game building found: " + ModTemplates.GameLooks().Count + " looks");
				yield break;
			}
			foreach (GameLook look in picks)
			{
				LocationStructureObject lso = look.Prefab.GetComponent<LocationStructureObject>();
				BuildingTemplate t = Guard($"export {look.Prefab.name}", () => ModTemplates.Export(look.Prefab, "autotest/" + look.Prefab.name.ToLowerInvariant(), look.Kind, look.Culture, look.Material));
				if (t == null)
				{
					continue;
				}
				File.WriteAllText(Path.Combine(Path.GetDirectoryName(_logPath), "template-" + look.Prefab.name + ".json"), ModTemplates.ToJson(t));
				int floor = Painted(t.floor), detail = Painted(t.detail), walls = Painted(t.walls);
				int gFloor = TilesIn(lso, "_groundTileMap"), gDetail = TilesIn(lso, "_detailTileMap"), gWalls = TilesIn(lso, "_blockWallsTilemap");
				int thin = lso.GetComponentsInChildren<ThinWallGameObject>(true).Length;
				int objects = lso.GetComponentsInChildren<StructureTemplateObjectData>(true).Length;
				int doors = lso.connectors?.Length ?? 0;
				Check($"exporting {look.Prefab.name} captures every tile, wall, object and entrance", () =>
					(floor == gFloor && detail == gDetail && walls == gWalls && t.thinWalls.Count == thin && t.objects.Count == objects && t.entrances.Count == doors,
					$"{look.Kind} {look.Culture} {look.Material}: floor {floor}/{gFloor} detail {detail}/{gDetail} walls {walls}/{gWalls} thin {t.thinWalls.Count}/{thin} objects {t.objects.Count}/{objects} entrances {t.entrances.Count}/{doors}; palette {t.palette.Count}"));
			}
			// 3. Round trip: a building rebuilt from its template exports to the same template,
			// and has the same footprint and border cells as the original.
			foreach (GameLook look in picks)
			{
				BuildingTemplate t = Guard($"export {look.Prefab.name}", () => ModTemplates.Export(look.Prefab, "autotest/rt-" + look.Prefab.name.ToLowerInvariant(), look.Kind, look.Culture, look.Material));
				GameObject built = t == null ? null : Guard($"build {look.Prefab.name} from its template", () => ModTemplates.BuildDetached(t, look.Prefab, null));
				if (built == null)
				{
					continue;
				}
				BuildingTemplate back = ModTemplates.Export(built, t.id, look.Kind, look.Culture, look.Material);
				// Identity fields name the object exported, not its look.
				back.name = t.name;
				back.behavesLike = t.behavesLike;
				string a = ModTemplates.ToJson(t), b = ModTemplates.ToJson(back);
				LocationStructureObject original = look.Prefab.GetComponent<LocationStructureObject>(), copy = built.GetComponent<LocationStructureObject>();
				bool footprint = SameCellSet(Occupied(original), Occupied(copy));
				bool border = SameCellSet(Border(original), Border(copy));
				Check($"{look.Prefab.name} rebuilt from its template matches the original", () =>
					(a == b && footprint && border, a == b ? $"{a.Length} chars; footprint same={footprint} border same={border} ({Border(original).Count}/{Border(copy).Count})" : "first difference: " + FirstDifference(a, b)));
				Check($"{look.Prefab.name} keeps thin-wall edges and colliders", () =>
					(SameWallGeometry(look.Prefab, built), "renderer offsets, sprite identity, collider offsets and sizes"));
				ModTemplates.DestroyDetached(built);
			}
			// A registered kind must instantiate its own logical structure, not the base
			// prefab's kind (a Library look borrowing a Dwelling must still be a Library).
			GameLook dwelling = picks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.DWELLING);
			if (dwelling != null && PlusBridge.Available)
			{
				STRUCTURE_TYPE libraryKind = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.library");
				BuildingTemplate libraryLook = ModTemplates.Export(dwelling.Prefab, "autotest/library-look", libraryKind, dwelling.Culture, dwelling.Material);
				IReadOnlyList<GameObject> libraryPrefabs = Guard("register a Library look", () => ModTemplates.Register(libraryLook, null));
				NPCSettlement libraryVillage = Villages().FirstOrDefault(v => v.owner != null);
				LocationGridTile librarySpot = null;
				if (libraryVillage != null && libraryPrefabs != null)
				{
					TemplatePlacement(libraryVillage, libraryPrefabs[0],
						new StructureSetting(STRUCTURE_TYPE.DWELLING, dwelling.Material), out librarySpot, out LocationGridTile _);
				}
				if (librarySpot == null || libraryPrefabs == null)
				{
					Skip("a registered-kind template creates its own kind, not its borrowed prefab's kind", "no room for the Library look");
				}
				else
				{
					LocationStructure libraryPlaced = Guard("place a registered-kind template", () =>
						librarySpot.tileObjectComponent.genericTileObject.InstantPlaceStructure(libraryPrefabs[0].name, libraryVillage));
					Check("a registered-kind template creates its own kind, not its borrowed prefab's kind", () =>
						(libraryPlaced != null && libraryPlaced.structureType == libraryKind,
						libraryPlaced == null ? "not placed" : $"expected {libraryKind}, got {libraryPlaced.structureType}"));
				}
			}
			// 4. Registered: a variant joins the game's list for its kind, culture and material,
			// stands in a village, is reachable, and saves under its own name; a save naming a
			// look whose pack is gone falls back to the building it was based on.
			GameLook tavern = picks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.TAVERN);
			if (tavern == null)
			{
				Skip("a template joins the game's list of looks for its kind and culture", "no Tavern");
				yield break;
			}
			BuildingTemplate variant = ModTemplates.Export(tavern.Prefab, "autotest/tavern-variant", tavern.Kind, tavern.Culture, tavern.Material);
			variant.name = "Autotest Tavern";
			// Resize changes center but keeps the borrowed tilemap transform. With no explicit
			// footprint, every rendered floor cell must still belong to this building.
			variant.center[0] += 4;
			variant.center[1] += 4;
			variant.footprint = null;
			variant.clickBox = null;
			IReadOnlyList<GameObject> made = Guard("register a Tavern variant", () => ModTemplates.Register(variant, null));
			List<GameObject> originals = ModTemplates.GameLooks().Where(l => l.Kind == tavern.Kind && l.Culture == tavern.Culture && l.Material == tavern.Material).Select(l => l.Prefab).ToList();
			// A village that uses this Tavern list: its culture's own, or (for the culture-neutral
			// list) any culture without a list of its own, the way the game falls back.
			Func<FACTION_TYPE, bool> usesList = c => c == tavern.Culture
				|| (tavern.Culture == FACTION_TYPE.None && !ModTemplates.GameLooks().Any(l => l.Kind == tavern.Kind && l.Culture == c && l.Material == tavern.Material));
			NPCSettlement village = Villages().FirstOrDefault(v => v.owner != null && usesList(v.owner.factionType.type));
			FACTION_TYPE culture = village?.owner.factionType.type ?? tavern.Culture;
			List<GameObject> listed = InnerMapManager.Instance.GetStructurePrefabsForStructure(culture, tavern.Kind, tavern.Material);
			Check("a template joins the game's list of looks for its kind and culture", () =>
				(made != null && made.Count == 1 && listed.Contains(made[0]) && originals.All(listed.Contains),
				$"{culture}: listed {listed.Count}: {string.Join(", ", listed.Select(g => g.name))}"));
			if (made == null || made.Count != 1)
			{
				yield break;
			}
			string poolName = made[0].name;
			LocationGridTile spot = null;
			if (village != null)
			{
				TemplatePlacement(village, made[0], new StructureSetting(tavern.Kind, tavern.Material), out spot, out LocationGridTile _);
			}
			LocationStructure placed = spot == null ? null : Guard("place the variant", () => spot.tileObjectComponent.genericTileObject.InstantPlaceStructure(poolName, village));
			if (placed == null)
			{
				Skip("a building from a template stands in a village and its entrances can be reached", village == null ? "no village using the Tavern's culture" : "no room for a Tavern");
			}
			else
			{
				LocationStructureObject obj = (placed as ManMadeStructure)?.structureObj;
				List<LocationGridTile> doors = obj?.connectors.Select(c => c.tileLocation).Where(x => x != null).ToList() ?? new List<LocationGridTile>();
				// Vanilla permits a footprint at the map edge with an entrance outside the
				// map. Compare with the original prefab's mapping at this same placement.
				List<LocationGridTile> expectedDoors = obj == null ? new List<LocationGridTile>() : tavern.Prefab.GetComponent<LocationStructureObject>().connectors
					.Select(c => spot.parentMap.GetTileFromWorldPosition(obj.transform.TransformPoint(tavern.Prefab.transform.InverseTransformPoint(c.transform.position)))).ToList();
				Character walker = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.limiterComponent.canMove);
				Check("a building from a template stands in a village and its entrances can be reached", () =>
					(obj != null && obj.name.Replace("(Clone)", "") == poolName && placed.tiles.Count == variant.floor.Sum(r => r.Count(ch => ch != '.'))
						&& obj.connectors.Length == variant.entrances.Count && obj.connectors.Select(c => c.tileLocation).SequenceEqual(expectedDoors)
						&& walker != null && doors.All(d => walker.movementComponent.HasPathToEvenIfDiffRegion(d)),
					$"{obj?.name}: tiles {placed.tiles.Count}, entrances {doors.Count}/{variant.entrances.Count} (original in-map {expectedDoors.Count(d => d != null)}), walker {walker?.name ?? "none"} reaches all={walker != null && doors.All(d => walker.movementComponent.HasPathToEvenIfDiffRegion(d))}"));
				Tilemap ground = AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(obj) as Tilemap;
				List<LocationGridTile> painted = new List<LocationGridTile>();
				foreach (Vector3Int cell in ground.cellBounds.allPositionsWithin)
					if (ground.HasTile(cell)) painted.Add(spot.parentMap.GetTileFromWorldPosition(ground.GetCellCenterWorld(cell)));
				Check("a resized template owns its rendered floor, not shifted ground beside it", () =>
					(painted.All(t => t != null && t.structure == placed) && painted.Count == placed.tiles.Count,
					$"owned painted tiles={painted.Count(t => t?.structure == placed)}/{painted.Count}, footprint={placed.tiles.Count}"));
				SaveDataManMadeStructure data = new SaveDataManMadeStructure();
				data.Save(placed);
				Check("saving a template building records its look", () => (data.structureTemplateName == poolName, data.structureTemplateName));
				InnerMapCameraMove.Instance.CenterCameraOn(obj.gameObject);
				yield return Screenshot("template-variant.png");
			}
			// The way villages get buildings: a settlement queues its native placement job,
			// a villager places the blueprint, and BUILD_BLUEPRINT consumes materials.
			Character placer = village?.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker && r.limiterComponent.canMove && r.limiterComponent.canPerform && !r.partyComponent.hasParty);
			LocationGridTile bpSpot = null, bpConnector = null;
			if (placer != null)
			{
				List<JobQueueItem> competing = new List<JobQueueItem>();
				village.PopulateJobsOfType(competing, JOB_TYPE.PLACE_BLUEPRINT);
				foreach (JobQueueItem job in competing) job.ForceCancelJob("autotest template placement");
				TemplatePlacement(village, made[0], new StructureSetting(tavern.Kind, tavern.Material), out bpSpot, out bpConnector);
			}
			bool bpQueued = bpSpot != null && Guard("give a villager the blueprint job", () =>
			{
				// A personal placement job is invisible to the village's one-blueprint-job
				// guard. Use its native settlement job so another builder cannot take the spot.
				AccessTools.Method(typeof(SettlementJobTriggerComponent), "TriggerPlaceBlueprint").Invoke(
					village.settlementJobTriggerComponent, new object[] { poolName, new StructureSetting(tavern.Kind, tavern.Material), bpSpot, bpConnector });
				List<JobQueueItem> placements = new List<JobQueueItem>();
				village.PopulateJobsOfType(placements, JOB_TYPE.PLACE_BLUEPRINT);
				JobQueueItem job = placements.FirstOrDefault(j => j.poiTarget == bpSpot.tileObjectComponent.genericTileObject);
				if (job == null) throw new Exception("the village did not queue its native placement job");
				placer = village.residents.FirstOrDefault(r => r != null && !r.isDead && r.hasMarker
					&& r.limiterComponent.canMove && r.limiterComponent.canPerform && !r.partyComponent.hasParty && job.CanCharacterDoJob(r));
				if (placer == null) return null;
				placer.jobQueue.CancelAllJobs();
				job.SetPriority(1000);
				if (!placer.jobQueue.AddJobInQueue(job)) throw new Exception("the eligible placer could not take its native placement job");
				return job;
			}) != null;
			if (!bpQueued)
			{
				Skip("a villager places a template building as a blueprint", placer == null ? "no eligible native placer" : "no room for a blueprint");
			}
			else
			{
				// Native blueprint state remains available when its visual is off-screen/inactive.
				Func<LocationStructureObject> blueprint = () => bpSpot.tileObjectComponent.genericTileObject.blueprintOnTile;
				// What the placer did meanwhile, so a missing blueprint says why: never started
				// the job, lost it from the queue, or performed it without a blueprint appearing.
				var seen = new List<string>();
				Func<bool> blueprintPlaced = () =>
				{
					string now = placer.isDead ? "dead"
						: $"{placer.currentJob?.jobType.ToString() ?? "none"}/{placer.currentActionNode?.goapType.ToString() ?? "none"}{(placer.jobQueue.HasJob(JOB_TYPE.PLACE_BLUEPRINT) ? "" : " (no PLACE_BLUEPRINT queued)")}";
					if (seen.Count == 0 || seen[seen.Count - 1] != now) seen.Add(now);
					return blueprint() != null;
				};
				yield return WaitGameHours(12f, blueprintPlaced);
				LocationStructureObject bp = blueprint();
				Check("a villager places a template building as a blueprint", () => (bp != null, bp != null ? bp.name : $"{placer.name} at {bpSpot}: " + string.Join(" > ", seen.Take(12))));
				if (bp != null)
				{
					// Supply materials to the game's existing build job. A duplicate personal
					// job lets two builders finish the same blueprint and race its cleanup.
					Character worker = null;
					Guard("supply the native template build job", () =>
					{
						List<JobQueueItem> jobs = new List<JobQueueItem>();
						village.PopulateJobsOfType(jobs, JOB_TYPE.BUILD_BLUEPRINT);
						JobQueueItem buildJob = jobs.FirstOrDefault(j => j.poiTarget == bpSpot.tileObjectComponent.genericTileObject);
						if (buildJob == null)
						{
							throw new Exception("the blueprint has no native build job");
						}
						worker = buildJob.assignedCharacter ?? placer;
						worker.StopCurrentActionNode("autotest template build");
						worker.UncarryPOI();
						WoodPile wood = InnerMapManager.Instance.CreateNewTileObject<WoodPile>(TILE_OBJECT_TYPE.WOOD_PILE);
						wood.SetResourceInPile(bp.craftCost);
						worker.ObtainItem(wood);
						worker.carryComponent.CarryPOI(wood);
						buildJob.SetPriority(1000);
						return buildJob.assignedCharacter != null || worker.jobQueue.AddJobInQueue(buildJob) ? buildJob : null;
					});
					Func<bool> built = () => village.structures.TryGetValue(tavern.Kind, out List<LocationStructure> all)
						&& all.Any(s => s is ManMadeStructure m && m.structureObj != null && m.structureObj.name.StartsWith(poolName) && m.structureObj.currentVisualMode == LocationStructureObject.Structure_Visual_Mode.Built && m != placed);
					// A builder who starts in the evening goes to bed with the job half done
					// (seen once: asleep at the deadline). Keep them rested; sleep is not under test.
					List<string> buildStates = new List<string>();
					yield return WaitGameHours(18f, () =>
					{
						worker?.needsComponent.SetTiredness(100f);
						string state = $"{worker?.currentJob?.jobType.ToString() ?? "none"}/{worker?.currentActionNode?.goapName ?? "none"} queued={worker?.jobQueue.HasJob(JOB_TYPE.BUILD_BLUEPRINT)} carrying={worker?.carryComponent.carriedPOI?.name ?? "none"}";
						if (buildStates.Count == 0 || buildStates[buildStates.Count - 1] != state) buildStates.Add(state);
						return built();
					});
					Check("villagers build a template building", () =>
						(built(), $"{worker?.name ?? placer.name}: blueprint={bpSpot.tileObjectComponent.genericTileObject.blueprintOnTile != null}; " + string.Join(" > ", buildStates)));
				}
			}
			GameObject fallback = Guard("load a building whose pack is gone", () =>
				ObjectPoolManager.Instance.InstantiateObjectFromPool(tavern.Prefab.name + "@autotest/no-such-pack", Vector3.zero, Quaternion.identity));
			Check("a save naming a missing pack falls back to the building it was based on", () =>
				(fallback != null && fallback.name.StartsWith(tavern.Prefab.name) && fallback.GetComponent<LocationStructureObject>() != null, fallback?.name ?? "nothing"));
			if (fallback != null)
			{
				ObjectPoolManager.Instance.DestroyObject(fallback);
			}
			// 5. A pack on disk: its good template loads (with its own PNG as the floor), its
			// broken ones are skipped with a reason in mods.log, and a pack switched off in the
			// mod manager does not load.
			string pack = Path.Combine(Path.GetDirectoryName(_logPath), "autotest-pack");
			if (Directory.Exists(pack))
			{
				Directory.Delete(pack, true);
			}
			Directory.CreateDirectory(Path.Combine(pack, "templates"));
			Directory.CreateDirectory(Path.Combine(pack, "art"));
			File.WriteAllText(Path.Combine(pack, "mod.json"), "{\"id\":\"autotest-pack\",\"name\":\"Autotest pack\",\"version\":\"1.0.0\",\"author\":\"autotest\",\"description\":\"\",\"loader\":\"RuinarchModLoader\",\"loaderApi\":1,\"type\":\"templates\"}");
			Texture2D red = new Texture2D(64, 64);
			red.SetPixels(Enumerable.Repeat(Color.red, 64 * 64).ToArray());
			red.Apply();
			File.WriteAllBytes(Path.Combine(pack, "art", "red.png"), red.EncodeToPNG());
			BuildingTemplate redFloor = ModTemplates.Export(tavern.Prefab, "autotest-pack/red-floor", tavern.Kind, tavern.Culture, tavern.Material);
			char redKey = TemplateKeyFree(redFloor);
			redFloor.palette[redKey.ToString()] = "art:red.png";
			redFloor.floor = redFloor.floor.Select(r => new string(r.Select(ch => ch == '.' ? '.' : redKey).ToArray())).ToList();
			File.WriteAllText(Path.Combine(pack, "templates", "red-floor.json"), ModTemplates.ToJson(redFloor));
			BuildingTemplate broken = ModTemplates.FromJson(ModTemplates.ToJson(redFloor));
			broken.id = "autotest-pack/broken";
			broken.palette[broken.palette.Keys.First()] = "game:No_Such_Tile";
			File.WriteAllText(Path.Combine(pack, "templates", "broken.json"), ModTemplates.ToJson(broken));
			File.WriteAllText(Path.Combine(pack, "templates", "notjson.json"), "{ this is not json");
			int logMark = ModsLogLength();
			PackLoadReport report = Guard("load the autotest pack", () => ModTemplates.LoadPack(pack));
			Check("a template pack on disk loads; broken templates are skipped with a reason", () =>
				(report != null && report.Loaded == 1 && report.Problems.Count == 2
					&& report.Problems.Any(x => x.Contains("broken.json") && x.Contains("No_Such_Tile")) && report.Problems.Any(x => x.Contains("notjson.json")),
				report == null ? "no report" : $"loaded {report.Loaded}; " + string.Join(" | ", report.Problems)));
			Check("a pack's problems are written to mods.log", () => (ModsLogHasSince(logMark, "broken.json") && ModsLogHasSince(logMark, "notjson.json"), "mods.log"));
			GameObject redBuilt = ModTemplates.PrefabsFor("autotest-pack/red-floor").FirstOrDefault();
			Tilemap redGround = redBuilt == null ? null : AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(redBuilt.GetComponent<LocationStructureObject>()) as Tilemap;
			Tile redTile = null;
			if (redGround != null)
			{
				foreach (Vector3Int p in redGround.cellBounds.allPositionsWithin)
				{
					if (redGround.GetTile(p) is Tile t0)
					{
						redTile = t0;
						break;
					}
				}
			}
			Check("a pack's PNG becomes a floor tile", () =>
				(redTile != null && redTile.sprite != null && redTile.sprite.texture.width == 64 && redTile.sprite.texture.GetPixel(10, 10) == Color.red,
				redTile == null ? "no tile" : $"{redTile.name} {redTile.sprite?.texture.width}px {redTile.sprite?.texture.GetPixel(10, 10)}"));
			if (village != null && redBuilt != null)
			{
				LocationGridTile redSpot = null;
				NPCSettlement redVillage = null;
				foreach (NPCSettlement candidate in Villages().Where(v => v.owner != null && usesList(v.owner.factionType.type)))
				{
					if (TemplatePlacement(candidate, redBuilt, new StructureSetting(tavern.Kind, tavern.Material),
						out redSpot, out LocationGridTile _))
					{
						redVillage = candidate;
						break;
					}
				}
				LocationStructure redPlaced = redVillage == null ? null : Guard("place the red-floor Tavern", () => redSpot.tileObjectComponent.genericTileObject.InstantPlaceStructure(redBuilt.name, redVillage));
				if (redVillage == null)
				{
					Skip("a PNG-floor building is placed with the base building's ground semantics", "no village has room for another Tavern");
				}
				else Check("a PNG-floor building is placed with the base building's ground semantics", () =>
					(redPlaced is ManMadeStructure m && m.structureObj != null && redPlaced.tiles.Count == Painted(redFloor.floor),
					redPlaced == null ? "not placed" : $"{redPlaced.tiles.Count} floor cells placed"));
				if (redPlaced is ManMadeStructure redMade && redMade.structureObj != null)
				{
					InnerMapCameraMove.Instance.CenterCameraOn(redMade.structureObj.gameObject);
					yield return Screenshot("template-png.png");
				}
			}
			// The loader, not the framework, decides which template packages load: only
			// compatible, enabled ones are handed over. Checked when such packages exist.
			List<KnownMod> withTemplates = ModLoader.Known.Where(m => m.Origin != ModOrigin.Infrastructure && Directory.Exists(Path.Combine(m.Directory, "templates"))).ToList();
			List<KnownMod> refused = withTemplates.Where(m => !m.Compatible || !m.Enabled).ToList();
			if (refused.Count == 0)
			{
				Skip("disabled or incompatible template packages are not handed to the framework", "no disabled or incompatible template package installed");
			}
			else Check("disabled or incompatible template packages are not handed to the framework", () =>
				(refused.All(m => !ModTemplates.PackDirectories.Contains(m.Directory, StringComparer.OrdinalIgnoreCase))
					&& withTemplates.Where(m => m.Compatible && m.Enabled).All(m => ModTemplates.PackDirectories.Contains(m.Directory, StringComparer.OrdinalIgnoreCase)),
				$"accepted {ModTemplates.PackDirectories.Count}: {string.Join(", ", ModTemplates.PackDirectories.Select(Path.GetFileName))}; refused: {string.Join(", ", refused.Select(m => m.Id + (m.Compatible ? " (disabled)" : " (" + m.RejectionReason + ")")))}"));
			Check("the framework reports its templates at startup", () => (ModsLogHas("[ModContent] Templates ready:") && ModsLogHas("[ModContent] Template packs:"), "mods.log"));
			Directory.Delete(pack, true);
		}

		// Validate the exact look being placed, not a random prefab of the same kind.
		private static bool TemplatePlacement(NPCSettlement village, GameObject prefab, StructureSetting setting,
			out LocationGridTile spot, out LocationGridTile connector)
		{
			List<StructureConnector> choices = new List<StructureConnector>();
			village.PopulateStructureConnectorsForStructureType(choices, setting.structureType);
			return prefab.GetComponent<LocationStructureObject>().GetFirstValidConnector(choices, village.region.innerMap, village,
				out int _, out spot, out connector, setting, out string _) != null;
		}

		// A Tavern, a Dwelling and the first special building: the kinds the suite exports and
		// rebuilds. Human and Elven buildings are the game's culture-neutral (None) lists; only
		// cult, church and Wiccan villages have lists of their own.
		private static List<GameLook> TemplatePicks()
		{
			IReadOnlyList<GameLook> looks = ModTemplates.GameLooks();
			return new[]
			{
				looks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.TAVERN && l.Culture == FACTION_TYPE.None),
				looks.FirstOrDefault(l => l.Kind == STRUCTURE_TYPE.DWELLING && l.Culture == FACTION_TYPE.None),
				looks.FirstOrDefault(l => l.Kind.IsSpecialStructure()),
			}.Where(l => l != null).ToList();
		}
		private static bool SameWallGeometry(GameObject original, GameObject rebuilt)
		{
			var a = original.GetComponentsInChildren<ThinWallGameObject>(true);
			var b = rebuilt.GetComponentsInChildren<ThinWallGameObject>(true);
			if (a.Length != b.Length) return false;
			for (int i = 0; i < a.Length; i++)
			{
				var sa = a[i].GetComponentsInChildren<SpriteRenderer>(true);
				var sb = b[i].GetComponentsInChildren<SpriteRenderer>(true);
				var ca = a[i].GetComponentsInChildren<BoxCollider2D>(true);
				var cb = b[i].GetComponentsInChildren<BoxCollider2D>(true);
				if (sa.Length != sb.Length || ca.Length != cb.Length) return false;
				for (int j = 0; j < sa.Length; j++)
					if (sa[j].sprite != sb[j].sprite || Vector3.Distance(sa[j].transform.localPosition, sb[j].transform.localPosition) > .0001f) return false;
				for (int j = 0; j < ca.Length; j++)
					if (ca[j].offset != cb[j].offset || ca[j].size != cb[j].size || Vector3.Distance(ca[j].transform.localPosition, cb[j].transform.localPosition) > .0001f) return false;
			}
			return true;
		}

		private static int Painted(List<string> rows)
		{
			return rows.Sum(r => r.Count(ch => ch != '.'));
		}

		private static int TilesIn(LocationStructureObject lso, string field)
		{
			Tilemap tm = AccessTools.Field(typeof(LocationStructureObject), field).GetValue(lso) as Tilemap;
			if (tm == null)
			{
				return 0;
			}
			int n = 0;
			foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
			{
				if (tm.GetTile(p) != null)
				{
					n++;
				}
			}
			return n;
		}

		// The game's occupied cells: the stored list, or (stored empty) the floor cells, as
		// LocationStructureObject.DetermineOccupiedTileCoordinates computes them.
		private static List<Vector2Int> Occupied(LocationStructureObject lso)
		{
			List<Vector3Int> stored = AccessTools.Field(typeof(LocationStructureObject), "_predeterminedOccupiedCoordinates").GetValue(lso) as List<Vector3Int>;
			if (stored != null && stored.Count > 0)
			{
				return stored.Select(c => new Vector2Int(c.x, c.y)).ToList();
			}
			Tilemap ground = AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(lso) as Tilemap;
			List<Vector2Int> cells = new List<Vector2Int>();
			foreach (Vector3Int p in ground.cellBounds.allPositionsWithin)
			{
				if (ground.GetTile(p) != null)
				{
					cells.Add(new Vector2Int(p.x, p.y));
				}
			}
			return cells;
		}

		private static List<Vector2Int> Border(LocationStructureObject lso)
		{
			return (AccessTools.Field(typeof(LocationStructureObject), "_borderCoordinates").GetValue(lso) as List<Vector2Int>) ?? new List<Vector2Int>();
		}

		private static bool SameCellSet(List<Vector2Int> a, List<Vector2Int> b)
		{
			return new HashSet<Vector2Int>(a).SetEquals(b);
		}

		private static string FirstDifference(string a, string b)
		{
			int i = 0;
			while (i < a.Length && i < b.Length && a[i] == b[i])
			{
				i++;
			}
			int from = System.Math.Max(0, i - 60);
			return $"at {i}: original '{a.Substring(from, System.Math.Min(120, a.Length - from))}' rebuilt '{b.Substring(from, System.Math.Min(120, b.Length - from))}'";
		}

		private static char TemplateKeyFree(BuildingTemplate t)
		{
			return "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".First(ch => !t.palette.ContainsKey(ch.ToString()));
		}
	}
}
