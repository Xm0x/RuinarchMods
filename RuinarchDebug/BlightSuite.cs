using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;

namespace RuinarchDebug
{
	// The Blight (Ruinarch+ Phase 7): which tiles it takes, what it does to village ground,
	// and the Blight Heart (built through its real build skill, growth, reach, feeding, save,
	// destruction). Every tile the suite corrupts is cleaned again at the end.
	public partial class AutoTest
	{
		private IEnumerator BlightSuite()
		{
			if (!PlusBridge.BlightAvailable)
			{
				Skip("the blight", "Ruinarch+ has no Blight");
				yield break;
			}
			Region region = GridMap.Instance.mainRegion;
			if (!Villages().Any(v => v.HasStructure(STRUCTURE_TYPE.FARM)))
			{
				NPCSettlement host = Villages().FirstOrDefault(v => HasRoomFor(v, STRUCTURE_TYPE.FARM));
				Guard("build a farm for the blight fixture", () => host == null ? null : InstantBuildVanilla(host, STRUCTURE_TYPE.FARM));
			}
			List<LocationGridTile> made = new List<LocationGridTile>();
			try
			{
				BlightTileRules(region, made);
				BlightCapTest(region, made);
				BlightChurchTest();
			}
			catch (Exception e)
			{
				Check("blight tile fixtures execute", () => (false, e.ToString()));
			}
			PlusBridge.BlightFlush();
			// Village blight sets villagers cleaning, and cleaners also walk to distant blight:
			// clean it before the Heart is tested.
			Try("clean the tile tests' blight", () =>
			{
				foreach (LocationGridTile t in made.Where(t => t.corruptionComponent.isCorrupted))
				{
					t.corruptionComponent.UncorruptTile();
				}
			});
			NPCSettlement village = Villages().Where(v => v.GetFirstStructureOfType(STRUCTURE_TYPE.FARM) != null).OrderByDescending(v => v.residents.Count).FirstOrDefault()
				?? Villages().OrderByDescending(v => v.residents.Count).FirstOrDefault();
			if (village != null) BlightBorderPressureTest(village);
			if (village != null)
			{
				List<LocationGridTile> villageMade = new List<LocationGridTile>();
				yield return Safe("BlightVillageRules", BlightVillageRules(village, villageMade));
				yield return BlightVillageResponse(village, villageMade);
				yield return BlightFireTest(village, villageMade);
				Try("clean the village tests' blight", () =>
				{
					foreach (LocationGridTile t in villageMade.Where(t => t.corruptionComponent.isCorrupted))
					{
						t.corruptionComponent.UncorruptTile();
					}
				});
			}
			yield return BlightHeartTests(region, made);
			Try("clean the suite's blight", () =>
			{
				foreach (LocationGridTile t in made.Where(t => t.corruptionComponent.isCorrupted))
				{
					t.corruptionComponent.UncorruptTile();
				}
			});
		}

		private static bool OpenVillageGround(LocationGridTile t, NPCSettlement v)
		{
			return t != null && t.IsPartOfSettlement(v) && !t.structure.isInterior && !t.IsWater() && t.tileType != LocationGridTile.Tile_Type.Wall
				&& (t.structure.structureType == STRUCTURE_TYPE.WILDERNESS || t.structure.structureType == STRUCTURE_TYPE.CITY_CENTER)
				&& !t.corruptionComponent.isCorrupted && !t.hasBlueprint;
		}

		private void BlightTileRules(Region region, List<LocationGridTile> made)
		{
			bool Take(LocationGridTile t)
			{
				bool ok = t != null && PlusBridge.BlightCorrupt(t);
				if (ok)
				{
					made.Add(t);
				}
				return ok;
			}
			NPCSettlement village = Villages().Where(v => v.GetFirstStructureOfType(STRUCTURE_TYPE.FARM) != null).OrderByDescending(v => v.residents.Count).FirstOrDefault()
				?? Villages().FirstOrDefault();
			ThePortal portal = PlayerManager.Instance.player.playerSettlement.allStructures.OfType<ThePortal>().FirstOrDefault();
			List<LocationGridTile> villageTiles = village == null ? new List<LocationGridTile>() : village.areas.SelectMany(a => a.gridTileComponent.gridTiles).Distinct().ToList();

			LocationGridTile wild = region.areas.Where(a => !a.IsNextToOrPartOfVillage() && !a.HasSettlementOnArea())
				.Select(a => a.gridTileComponent.centerGridTile)
				.FirstOrDefault(t => t != null && t.structure is Wilderness && !t.IsWater() && t.tileType != LocationGridTile.Tile_Type.Wall
					&& !t.corruptionComponent.isCorrupted && !t.HasNeighbourStructure(STRUCTURE_TYPE.THE_PORTAL));
			LocationGridTile street = villageTiles.FirstOrDefault(t => OpenVillageGround(t, village) && t.tileObjectComponent.objHere == null);
			LocationGridTile stored = villageTiles.FirstOrDefault(t => OpenVillageGround(t, village) && t.tileObjectComponent.objHere != null && !(t.tileObjectComponent.objHere is Crops)
				&& !(t.tileObjectComponent.objHere is TreeObject));
			LocationStructure farm = village?.GetFirstStructureOfType(STRUCTURE_TYPE.FARM);
			LocationGridTile field = farm?.tiles.FirstOrDefault(t => t.tileObjectComponent.objHere is Crops && !t.corruptionComponent.isCorrupted)
				?? farm?.tiles.FirstOrDefault(t => !t.corruptionComponent.isCorrupted && t.tileType != LocationGridTile.Tile_Type.Wall);
			LocationGridTile inside = village?.GetFirstStructureOfType(STRUCTURE_TYPE.DWELLING)?.tiles.FirstOrDefault(t => t.tileType != LocationGridTile.Tile_Type.Wall);
			LocationGridTile water = region.innerMap.allTiles.FirstOrDefault(t => t.IsWater() && !t.corruptionComponent.isCorrupted);
			LocationGridTile wall = villageTiles.FirstOrDefault(t => t.tileType == LocationGridTile.Tile_Type.Wall);
			// The Portal's surroundings start corrupted: clean one neighbour so the rule is tested
			// (corrupted ground is refused anyway), and corrupt it back afterwards.
			LocationGridTile byPortal = portal?.tiles.SelectMany(t => t.neighbourList)
				.FirstOrDefault(n => n.structure != portal && n.structure is Wilderness && !n.IsWater() && n.tileType != LocationGridTile.Tile_Type.Wall);
			bool portalWasCorrupted = byPortal != null && byPortal.corruptionComponent.isCorrupted;
			if (portalWasCorrupted)
			{
				byPortal.corruptionComponent.UncorruptTile();
			}
			Log($"  blight tiles: village={village?.name} wild={wild} street={street} stored={stored} ({stored?.tileObjectComponent.objHere?.tileObjectType}) field={field} ({field?.tileObjectComponent.objHere?.tileObjectType}) inside={inside} water={water} wall={wall} byPortal={byPortal}");

			object storedObject = stored?.tileObjectComponent.objHere;
			bool cropBefore = field?.tileObjectComponent.objHere is Crops;
			if (wild != null)
			{
				ELEVATION elevation = wild.elevationType;
				try
				{
					wild.SetElevation(ELEVATION.MOUNTAIN);
					Check("the blight refuses mountain ground", () =>
						(!PlusBridge.BlightCorrupt(wild) && !wild.corruptionComponent.isCorrupted, $"mountain={wild}"));
				}
				finally { wild.SetElevation(elevation); }
			}
			bool tookWild = Take(wild), tookStreet = Take(street), tookStored = Take(stored), tookField = Take(field);
			Check("the blight takes wilderness and a village's streets and yards", () => (wild != null && street != null && stored != null && tookWild && tookStreet && tookStored,
				$"wild={tookWild} street={tookStreet} yard={tookStored}"));
			Check("blight on village ground keeps what stands there", () => (stored != null && tookStored && stored.tileObjectComponent.objHere == storedObject,
				$"{storedObject} kept={(stored?.tileObjectComponent.objHere == storedObject)}"));
			const string fieldCheck = "the blight takes a village's fields and its crops wither";
			if (field == null)
			{
				Skip(fieldCheck, "no village has a farm yet");
			}
			else
			{
				Check(fieldCheck, () => (tookField && (!cropBefore || !(field.tileObjectComponent.objHere is Crops)),
					$"field={tookField} crop before={cropBefore} after={field.tileObjectComponent.objHere is Crops}"));
			}
			bool tookInside = Take(inside), tookWater = Take(water), tookWall = Take(wall), tookPortal = Take(byPortal);
			Check("the blight never takes a building's inside, walls, water or the Portal's ground", () => (inside != null && water != null && byPortal != null
				&& !tookInside && !tookWater && !tookWall && !tookPortal,
				$"inside={tookInside} water={tookWater} wall={tookWall} by the Portal={tookPortal}"));
			if (portalWasCorrupted && !byPortal.corruptionComponent.isCorrupted)
			{
				byPortal.corruptionComponent.CorruptTile();
			}
		}

		// Village blight never helps the player: no summoning, minions, walls or extending
		// corruption from it; corrupted fields are not tilled again.
		private IEnumerator BlightVillageRules(NPCSettlement village, List<LocationGridTile> made)
		{
			const string noSummon = "village blight cannot be used to summon or raise minions";
			const string noBuild = "village blight cannot be built on or corrupted onward";
			const string noTill = "a farm does not till its blighted fields";
			List<LocationGridTile> ground = village.areas.SelectMany(a => a.gridTileComponent.gridTiles).Where(t => OpenVillageGround(t, village) && t.tileObjectComponent.objHere == null).ToList();
			LocationGridTile blighted = ground.FirstOrDefault(t => PlusBridge.BlightCorrupt(t));
			if (blighted == null)
			{
				foreach (string c in new[] { noSummon, noBuild, noTill }) Skip(c, "no open village ground to blight");
				yield break;
			}
			made.Add(blighted);
			SkillData summon = PlayerSkillManager.Instance.allPlayerSkillsData.Values.FirstOrDefault(s => s is SummonPlayerSkill);
			SkillData minion = PlayerSkillManager.Instance.allPlayerSkillsData.Values.FirstOrDefault(s => s is MinionPlayerSkill);
			string summonWhy = null, minionWhy = null;
			bool summonOk = (summon as SummonPlayerSkill)?.CanPerformAbilityTowards(blighted, out summonWhy) ?? true;
			bool minionOk = (minion as MinionPlayerSkill)?.CanPerformAbilityTowards(blighted, out minionWhy) ?? true;
			Check(noSummon, () => (summon != null && minion != null && !summonOk && !minionOk && summonWhy.Contains("Blight") && minionWhy.Contains("Blight"),
				$"{summon?.name}: {summonOk} '{summonWhy}'; {minion?.name}: {minionOk} '{minionWhy}'"));

			SkillData wall = PlayerSkillManager.Instance.GetSkillData(PLAYER_SKILL_TYPE.DEMONIC_WALL);
			string wallWhy = "";
			bool wallOk = wall != null && BaseBuildingManager.Instance.CanTarget(blighted, wall, ref wallWhy);
			LocationGridTile beside = blighted.neighbourList.FirstOrDefault(n => !n.corruptionComponent.isCorrupted && n.neighbourList.All(x => !x.corruptionComponent.isCorrupted || x.IsPartOfSettlement(village)));
			bool besideCounts = beside != null && beside.corruptionComponent.HasCorruptedNeighbour();
			Check(noBuild, () => (wall != null && !wallOk && beside != null && !besideCounts, $"wall={wallOk} '{wallWhy}', next tile counts as touching corruption={besideCounts}"));

			Farm farm = village.GetFirstStructureOfType(STRUCTURE_TYPE.FARM) as Farm;
			LocationGridTile field = farm?.farmTiles.FirstOrDefault(t => !t.corruptionComponent.isCorrupted && PlusBridge.BlightCorrupt(t));
			if (field == null)
			{
				Skip(noTill, farm == null ? $"{village.name} has no farm" : "no field could be blighted");
				yield break;
			}
			made.Add(field);
			HashSet<GenericTileObject> offered = new HashSet<GenericTileObject>();
			System.Reflection.MethodInfo untilled = HarmonyLib.AccessTools.Method(typeof(Farm), "GetUntilledFarmTile");
			offered.Add(untilled.Invoke(farm, null) as GenericTileObject);
			Check(noTill, () => (!offered.Contains(field.tileObjectComponent.genericTileObject), $"offered {string.Join(", ", offered.Select(o => o?.gridTileLocation?.ToString() ?? "none"))}, blighted field {field}"));
			TillTile till = (TillTile)InteractionManager.Instance.goapActionData[INTERACTION_TYPE.TILL_TILE];
			ActualGoapNode node = new ActualGoapNode();
			node.SetActionData(till, village.residents.First(), field.tileObjectComponent.genericTileObject, null, 10);
			till.AfterTillTileSuccess(node);
			Check("a till action already running cannot plant on blighted ground", () =>
				(!(field.tileObjectComponent.objHere is Crops), $"crop after completed till={field.tileObjectComponent.objHere is Crops}"));
			FactionRelationship relation = village.owner.GetRelationshipWith(PlayerManager.Instance.player.playerFaction);
			FACTION_RELATIONSHIP_STATUS? original = relation?.relationshipStatus;
			bool stayedBlighted = true, stayedUnplanted = true;
			try
			{
				// Native peaceful villagers cannot take PURIFY_GROUND jobs. Keep farming active
				// while preventing cleanup from removing the condition this scenario exercises.
				relation?.SetRelationshipStatus(FACTION_RELATIONSHIP_STATUS.Neutral);
				yield return WaitGameHours(24f, () =>
				{
					stayedBlighted &= field.corruptionComponent.isCorrupted;
					stayedUnplanted &= !(field.tileObjectComponent.objHere is Crops);
					return false;
				});
				Check("a blighted farm field stays unplanted for a full day", () =>
					(stayedBlighted && stayedUnplanted, $"blighted all 24h={stayedBlighted}, no crops all 24h={stayedUnplanted}"));
			}
			finally
			{
				if (original.HasValue) relation.SetRelationshipStatus(original.Value);
			}
			field.corruptionComponent.UncorruptTile();
			till.AfterTillTileSuccess(node);
			Check("cleaning a blighted field allows crops again", () =>
				(field.tileObjectComponent.objHere is Crops, $"crop after cleaned till={field.tileObjectComponent.objHere is Crops}"));
		}

		// Villages answer the blight: several cleaners, their own ground first, the kingdom
		// told, the blight among their grievances.
		private IEnumerator BlightVillageResponse(NPCSettlement village, List<LocationGridTile> made)
		{
			const string scale = "a village in alarm sends several cleaners and tells its kingdom";
			const string first = "villagers clean the blight off their own ground";
			const string mood = "the blight weighs on the village's mood";
			Faction faction = village.owner;
			List<LocationGridTile> ground = village.areas.SelectMany(a => a.gridTileComponent.gridTiles).Where(t => OpenVillageGround(t, village)).ToList();
			List<LocationGridTile> own = ground.Where(t => PlusBridge.BlightCorrupt(t)).Take(40).ToList();
			made.AddRange(own);
			PlusBridge.BlightFlush();
			if (own.Count < 30)
			{
				foreach (string c in new[] { scale, first, mood }) Skip(c, $"only {own.Count} village tiles could be blighted");
				yield break;
			}
			yield return WaitGameHours(1.2f, null);
			// Taken jobs leave the village's list: count the villagers cleaning as well.
			int jobs = village.availableJobs.Count(j => j.jobType == JOB_TYPE.PURIFY_GROUND);
			int cleaners = village.residents.Count(c => c != null && !c.isDead && c.behaviourComponent.HasBehaviour(typeof(PurifyGroundBehaviour)));
			Check(scale, () => (jobs + cleaners >= 2 && faction != null && faction.isAwareOfPlayer,
				$"{village.name}: {own.Count} blighted, open cleanup jobs {jobs}, cleaners {cleaners}, {faction?.name} aware={faction?.isAwareOfPlayer}"));
			List<string> grievances = PlusBridge.UnrestReasons(village);
			Check(mood, () => (grievances.Contains("the blight"), string.Join(", ", grievances)));
			int before = own.Count(t => t.corruptionComponent.isCorrupted);
			// A six-hour window can fall entirely outside native work hours.
			float cleanupStart = GameHours;
			yield return WaitGameHours(24f, () => own.Count(t => t.corruptionComponent.isCorrupted) < before);
			int after = own.Count(t => t.corruptionComponent.isCorrupted);
			Check(first, () => (after < before, $"own blight {before} -> {after} in {GameHours - cleanupStart:0.0}h"));
			Guard("make the faction unaware again", () => { faction?.SetIsAwareOfPlayer(false); return faction; });
		}

		// Fire on blighted ground cleans it once the fire goes out: lights the ground of one
		// blighted village tile and whatever stands on another, then waits for the fires.
		private IEnumerator BlightFireTest(NPCSettlement village, List<LocationGridTile> made)
		{
			const string fire = "a fire on blighted ground burns the blight off";
			List<LocationGridTile> ground = village.areas.SelectMany(a => a.gridTileComponent.gridTiles).Where(t => OpenVillageGround(t, village)).ToList();
			LocationGridTile bare = ground.FirstOrDefault(t => t.tileObjectComponent.objHere == null && PlusBridge.BlightCorrupt(t));
			made.Add(bare);
			// AddTrait can report success while the fire never takes (a wet object); only a
			// fire that is actually burning counts.
			bool Ignite(Traits.ITraitable x) => x != null
				&& x.traitContainer.AddTrait(x, "Burning", null, bypassElementalChance: true, -1, 0f, ELEMENTAL_TYPE.Fire)
				&& x.traitContainer.HasTrait("Burning");
			bool groundLit = bare != null && Ignite(bare.tileObjectComponent.genericTileObject);
			// A burning object removed from the map (burnt down, chopped) ends its fire away
			// from the tile; its blight must still burn off. Three kinds: a dropped item (no
			// size of its own: placed here so every world has one), whatever other object the
			// world has there, and a tree.
			foreach (string kind in new[] { "item", "object", "tree" })
			{
				LocationGridTile at;
				if (kind == "item")
				{
					at = ground.FirstOrDefault(t => t != bare && t.tileObjectComponent.objHere == null && PlusBridge.BlightCorrupt(t));
					at?.structure.AddPOI(InnerMapManager.Instance.CreateNewTileObject<TileObject>(TILE_OBJECT_TYPE.ANTIDOTE), at);
				}
				else
				{
					at = ground.FirstOrDefault(t => t.tileObjectComponent.objHere is TileObject o && (o is TreeObject) == (kind == "tree")
						&& o.traitContainer.HasTrait("Flammable") && PlusBridge.BlightCorrupt(t));
				}
				TileObject burning = at?.tileObjectComponent.objHere;
				if (!Ignite(burning))
				{
					Skip($"burning {kind} cleanses blight even when the object disappears", $"no flammable {kind} on blighted village ground");
					continue;
				}
				made.Add(at);
				Log($"  {kind}: {burning} burning={burning.traitContainer.HasTrait("Burning")} hidden={burning.isHidden} in structure={at.structure.pointsOfInterest.Contains(burning)} corrupted={at.corruptionComponent.isCorrupted}");
				FireTraced = burning;
				at.structure.RemovePOI(burning);
				FireTraced = null;
				Check($"burning {kind} cleanses blight even when the object disappears", () =>
					(burning.gridTileLocation == null && !at.corruptionComponent.isCorrupted,
					$"{burning.tileObjectType} at {at} gone={burning.gridTileLocation == null} previous={burning.previousTile} burning={burning.traitContainer.HasTrait("Burning")} structure={at.structure?.structureType} corruption={at.corruptionComponent.isCorrupted}"));
			}
			// The main case: a fire left to burn out where it is, on the ground or else on
			// something standing there.
			LocationGridTile lit = groundLit ? bare : null;
			Traits.ITraitable fuel = groundLit ? bare.tileObjectComponent.genericTileObject : null;
			if (!groundLit)
			{
				lit = ground.FirstOrDefault(t => !made.Contains(t) && t.tileObjectComponent.objHere != null
					&& t.tileObjectComponent.objHere.traitContainer.HasTrait("Flammable") && PlusBridge.BlightCorrupt(t));
				TileObject o = lit?.tileObjectComponent.objHere;
				fuel = Ignite(o) ? o : null;
				made.Add(lit);
			}
			Log($"  fire: ground {bare} lit={groundLit} flammable={bare?.tileObjectComponent.genericTileObject.traitContainer.HasTrait("Flammable")}; burning {fuel} at {lit}");
			if (fuel == null)
			{
				Skip(fire, "nothing on blighted ground would burn");
				yield break;
			}
			FireTraced = fuel;
			yield return WaitGameHours(8f, () => !fuel.traitContainer.HasTrait("Burning"));
			FireTraced = null;
			Check(fire, () => (!fuel.traitContainer.HasTrait("Burning") && !lit.corruptionComponent.isCorrupted,
				$"{fuel} at {lit} burning={fuel.traitContainer.HasTrait("Burning")} corrupted={lit.corruptionComponent.isCorrupted}"));
		}

		private void BlightBorderPressureTest(NPCSettlement village)
		{
			Type response = HarmonyLib.AccessTools.TypeByName("RuinarchPlus.Phase7.BlightResponse");
			var hourly = HarmonyLib.AccessTools.Method(response, "Hourly");
			var pressure = HarmonyLib.AccessTools.Method(response, "Pressure");
			List<LocationGridTile> border = village.areas.SelectMany(a => a.gridTileComponent.gridTiles)
				.Where(t => t.IsPartOfSettlement(village) && t.neighbourList.Any(n => !n.IsPartOfSettlement(village))).ToList();
			Func<LocationGridTile, float> distance = t => border.Min(b => t.GetDistanceTo(b));
			List<LocationGridTile> outside = village.areas.SelectMany(a => a.neighbourComponent.neighbours)
				.Distinct().SelectMany(a => a.gridTileComponent.gridTiles)
				.Where(t => !t.IsPartOfSettlement(village) && t.structure is Wilderness
					&& !t.IsWater() && !t.isOccupied && !t.corruptionComponent.isCorrupted).ToList();
			LocationGridTile near = outside.First(t => distance(t) <= 5 && PlusBridge.BlightCorrupt(t));
			near.corruptionComponent.UncorruptTile();
			LocationGridTile far = outside.First(t => distance(t) > 5 && distance(t) <= 10 && PlusBridge.BlightCorrupt(t));
			try
			{
				hourly.Invoke(null, null);
				int withFar = (int)pressure.Invoke(null, new object[] { village });
				far.corruptionComponent.UncorruptTile();
				hourly.Invoke(null, null);
				int baseline = (int)pressure.Invoke(null, new object[] { village });
				PlusBridge.BlightCorrupt(near);
				hourly.Invoke(null, null);
				int withNear = (int)pressure.Invoke(null, new object[] { village });
				Check("village pressure counts only blight within five tiles of its border", () =>
					(withFar == baseline && withNear == baseline + 1,
						$"baseline={baseline} far={withFar} near={withNear}, distances {distance(far)}/{distance(near)}"));
			}
			finally
			{
				near.corruptionComponent.UncorruptTile();
				far.corruptionComponent.UncorruptTile();
				hourly.Invoke(null, null);
			}
		}

		private void BlightCapTest(Region region, List<LocationGridTile> made)
		{
			Type engine = HarmonyLib.AccessTools.TypeByName("RuinarchPlus.Phase7.BlightEngine");
			object cap = PlusBridge.Config("blightTilesPerHour");
			try
			{
				PlusBridge.SetConfig("blightTilesPerHour", 2);
				HarmonyLib.AccessTools.Method(engine, "StartHour").Invoke(null, null);
				List<LocationGridTile> candidates = region.innerMap.allTiles.Where(t => t.structure is Wilderness
					&& !t.corruptionComponent.isCorrupted && !t.IsWater() && !t.HasNeighbourStructure(STRUCTURE_TYPE.THE_PORTAL)).Take(100).ToList();
				List<LocationGridTile> first = (List<LocationGridTile>)HarmonyLib.AccessTools.Method(engine, "Grow").Invoke(null, new object[] { candidates, 20 });
				List<LocationGridTile> second = (List<LocationGridTile>)HarmonyLib.AccessTools.Method(engine, "Grow").Invoke(null, new object[] { candidates, 20 });
				made.AddRange(first);
				made.AddRange(second);
				Check("all growth requests share the hourly tile cap", () =>
					(first.Count == 2 && second.Count == 0, $"first={first.Count} second={second.Count} cap=2"));
			}
			finally
			{
				PlusBridge.SetConfig("blightTilesPerHour", cap);
				HarmonyLib.AccessTools.Method(engine, "StartHour").Invoke(null, null);
			}
		}

		private void BlightChurchTest()
		{
			NPCSettlement village = Villages().First(v => v.residents.Any());
			Character actor = village.residents.First();
			Faction faction = actor.faction;
			var property = HarmonyLib.AccessTools.Property(typeof(Faction), "factionType");
			object original = property.GetValue(faction);
			ActualGoapNode node = new ActualGoapNode();
			node.SetActionData(InteractionManager.Instance.goapActionData[INTERACTION_TYPE.PURIFY_GROUND],
				actor, village.areas.First().gridTileComponent.centerGridTile.tileObjectComponent.genericTileObject, null, 10);
			HarmonyLib.AccessTools.Property(typeof(ActualGoapNode), "currentStateName").SetValue(node, "Purify Success");
			var duration = HarmonyLib.AccessTools.Method(typeof(ActualGoapNode), "SetActionDuration");
			try
			{
				property.SetValue(faction, FactionManager.Instance.CreateFactionType(FACTION_TYPE.Human_Empire));
				duration.Invoke(node, null);
				int normal = node.expectedActionStateDuration;
				property.SetValue(faction, FactionManager.Instance.CreateFactionType(FACTION_TYPE.Divine_Church));
				duration.Invoke(node, null);
				int church = node.expectedActionStateDuration;
				Check("Divine Church villagers purify blight in half the time", () =>
					(church == Math.Max(1, normal / 2) && church < normal, $"normal={normal} church={church} ticks"));
			}
			finally { property.SetValue(faction, original); }
		}

		private IEnumerator BlightHeartTests(Region region, List<LocationGridTile> made)
		{
			const string grows = "a Blight Heart grows its blight every hour, within its reach";
			const string feeds = "deaths on a Heart's blight feed it to the next level";
			const string saved = "a Heart's level is kept in the save file";
			const string stops = "a destroyed Heart's blight stops growing and its charge returns";
			DemonicStructurePlayerSkill skill = PlayerSkillManager.Instance.GetDemonicStructureSkillData(ModContent.SkillTypeFor("ruinarch.plus.blight_heart"));
			yield return WaitGameHours(1.5f, () => skill != null && skill.isInUse);
			Check("the player can build a Blight Heart", () => (skill != null && skill.isInUse && skill.charges > 0,
				$"skill={(skill == null ? "missing" : skill.name)} granted={skill?.isInUse} charges={skill?.charges}/{skill?.maxCharges}"));
			if (skill == null || !skill.isInUse)
			{
				foreach (string c in new[] { grows, feeds, saved, stops }) Skip(c, "no Blight Heart skill");
				yield break;
			}

			// A spot far from villages and the Portal: corrupt its middle the game's way, then
			// build through the skill as a player would.
			List<LocationGridTile> villageCentres = region.settlementsInRegion.SelectMany(s => s.areas).Select(a => a.gridTileComponent.centerGridTile).Where(t => t != null).ToList();
			LocationGridTile spot = null;
			string why = "no wilderness spot accepted the Heart";
			foreach (LocationGridTile c in region.areas.Where(a => !a.IsNextToOrPartOfVillage() && !a.HasSettlementOnArea())
				.Select(a => a.gridTileComponent.centerGridTile)
				.Where(t => t != null && t.structure is Wilderness && t.localPlace.x >= 20 && t.localPlace.y >= 20
					&& t.localPlace.x < region.innerMap.width - 20 && t.localPlace.y < region.innerMap.height - 20
					&& villageCentres.All(v => v.GetDistanceTo(t) >= 25f))
				.OrderByDescending(t => villageCentres.Min(v => v.GetDistanceTo(t))))
			{
				List<LocationGridTile> seed = new List<LocationGridTile>();
				c.PopulateTilesInRadius(seed, 1, includeCenterTile: true);
				foreach (LocationGridTile t in seed.Where(t => t.corruptionComponent.CanCorruptTile() || (!t.corruptionComponent.isCorrupted && t.structure is Wilderness && !t.IsWater())))
				{
					t.corruptionComponent.CorruptTile();
					made.Add(t);
				}
				if (skill.CanPerformAbilityTowards(c, out string reason))
				{
					spot = c;
					break;
				}
				why = reason;
			}
			if (spot == null)
			{
				foreach (string c in new[] { grows, feeds, saved, stops }) Skip(c, why);
				yield break;
			}
			int chargesBefore = skill.charges;
			Try("build a Blight Heart", () => skill.ActivateAbility(spot));
			LocationStructure heart = null;
			yield return WaitGameHours(1f, () => (heart = PlusBridge.BlightHearts().FirstOrDefault()) != null);
			if (heart == null)
			{
				foreach (string c in new[] { grows, feeds, saved, stops }) Skip(c, $"no Heart stood after building at {spot}");
				yield break;
			}
			Log($"  Blight Heart at {heart.GetCenterTile()?.localPlace}, charges {chargesBefore} -> {skill.charges}");
			LocationGridTile firstDeathTile = heart.tiles.SelectMany(t => t.neighbourList)
				.First(t => !t.isOccupied && !heart.tiles.Contains(t) && (t.corruptionComponent.isCorrupted || PlusBridge.BlightCorrupt(t)));
			made.Add(firstDeathTile);
			Guard("feed the Heart before its first hour", () => SpawnAndKillAt(firstDeathTile, SUMMON_TYPE.Wolf));
			int firstFed = (int)HarmonyLib.AccessTools.Property(heart.GetType(), "Fed").GetValue(heart);
			Check("a newly built Heart feeds before its first hourly refresh", () => (firstFed == 1, $"fed={firstFed}"));

			// Growth and reach.
			yield return WaitGameHours(1.2f, null);
			int before = PlusBridge.BlightPatch(heart).Count;
			yield return WaitGameHours(3f, null);
			HashSet<LocationGridTile> patch = PlusBridge.BlightPatch(heart);
			made.AddRange(patch);
			LocationGridTile centre = heart.GetCenterTile();
			Check("the Heart survives the growth fixture", () =>
				(!heart.hasBeenDestroyed && centre != null, $"destroyed={heart.hasBeenDestroyed} tiles={heart.tiles.Count} center={centre}"));
			if (heart.hasBeenDestroyed || centre == null) yield break;
			int reach = PlusBridge.BlightReach(heart);
			int outside = patch.Count(t => t.GetDistanceTo(centre) > reach + 0.5f);
			Check(grows, () => (patch.Count - before >= 6 && outside == 0, $"patch {before} -> {patch.Count} in 3h, reach {reach}, beyond reach {outside}"));
			InnerMapCameraMove.Instance.CenterCameraOn(((DemonicStructure)heart).structureObj.gameObject);
			yield return Snapshot(heart, "blight-heart");

			// Feeding: four deaths on its blight take it to level 2.
			int level = PlusBridge.BlightLevel(heart);
			foreach (LocationGridTile t in patch.Where(t => !t.isOccupied && !heart.tiles.Contains(t)).Take(3).ToList())
			{
				Guard("spawn and kill on the blight", () => SpawnAndKillAt(t, SUMMON_TYPE.Wolf));
			}
			Check(feeds, () => (PlusBridge.BlightLevel(heart) == level + 1, $"level {level} -> {PlusBridge.BlightLevel(heart)}"));
			Check("a fed Heart shows its level and feeding in its description", () =>
				(heart.customDescription.Contains("level 2") && heart.customDescription.Contains("0/8"), heart.customDescription));

			// The real save archive carries the Heart's state; the load hook restores it.
			string json = null;
			yield return SaveAndRead("ruinarch.plus.blight.json", (j, _) => json = j);
			string line = $"{heart.persistentID}|{PlusBridge.BlightLevel(heart)}|";
			ReplayLoad("ruinarch.plus.blight.json", (json ?? "").Replace(line, $"{heart.persistentID}|3|"));
			Check(saved, () => (json != null && json.Contains(line) && PlusBridge.BlightLevel(heart) == 3 && heart.maxHP == 3000,
				$"saved={json} level after load={PlusBridge.BlightLevel(heart)} HP={heart.currentHP}/{heart.maxHP}"));

			// Destruction.
			int charges = skill.charges;
			Try("destroy the Heart", () => heart.AdjustHP(-heart.currentHP - 1));
			yield return WaitGameHours(0.5f, () => heart.hasBeenDestroyed);
			List<LocationGridTile> area = new List<LocationGridTile>();
			centre.PopulateTilesInRadius(area, reach + 2, includeCenterTile: true);
			int corrupted = area.Count(t => t.corruptionComponent.isCorrupted);
			yield return WaitGameHours(2.2f, null);
			int later = area.Count(t => t.corruptionComponent.isCorrupted);
			made.AddRange(area.Where(t => t.corruptionComponent.isCorrupted));
			// In worlds with unlimited charges the build spends none, yet the game still hands one
			// back on destruction (DemonicStructure.AfterStructureDestruction); check charges only
			// in normal worlds.
			bool unlimited = WorldSettings.Instance.worldSettingsData.playerSkillSettings.PowerHasUnlimitedCharges(skill.type);
			Check(stops, () => (heart.hasBeenDestroyed && later <= corrupted && (unlimited || skill.charges == charges + 1),
				$"destroyed={heart.hasBeenDestroyed} corrupted {corrupted} -> {later} in 2h, charges {charges} -> {skill.charges} of {skill.maxCharges} (unlimited={unlimited})"));
		}

		// While the fire test runs: how each fire on a test object ended.
		internal static Traits.ITraitable FireTraced;
		[HarmonyLib.HarmonyPatch(typeof(Traits.Burning), nameof(Traits.Burning.OnRemoveTrait))]
		private static class FireOutTrace
		{
			private static void Prefix(Traits.ITraitable removedFrom)
			{
				if (removedFrom == null || removedFrom != FireTraced || !(removedFrom is TileObject o)) return;
				LocationGridTile at = o.gridTileLocation ?? o.previousTile;
				_running?.Log($"  fire out on {o}: tile={o.gridTileLocation} previous={o.previousTile} corrupted={at?.corruptionComponent.isCorrupted} structure={at?.structure?.structureType} by {new System.Diagnostics.StackTrace().GetFrames()?.Skip(2).Select(f => f.GetMethod()).FirstOrDefault(m => m?.DeclaringType?.Namespace != "Traits")?.Name}");
			}
		}
	}
}
