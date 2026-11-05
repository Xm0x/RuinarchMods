# TruePlanet Unified Campaign and Terrain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce believable geographic native maps and developed capitals, preserve one campaign setup during travel, improve globe rotation, and simplify TruePlanet's player-facing UI.

**Architecture:** A deterministic `LocalTerrainPlan` becomes the single source for native terrain arrays and inspector percentages. New planet/local versions leave saved maps unchanged. Travel carries native setup state and uses stock Portal/loadout paths automatically. Globe overlays use a coastline-safe low-zoom LOD. Presentation fixes remain in the existing native UI and raster paths.

**Tech Stack:** C# on Ruinarch's Windows Mono runtime, Unity 2020.3.20f1, Harmony, uGUI/TextMeshPro, Newtonsoft.Json, existing RuinarchDebug native harness, Roslyn builds through `RuinarchModLoader/tools/build-mod.sh`.

**Spec:** `docs/specs/2026-11-02-trueplanet-campaign-terrain-design.md`

## Global Constraints

- Work only in `RuinarchMods`. `RuinarchRE` is read-only reference.
- Preserve every unrelated working-tree change. Stage and commit only paths listed for the current task.
- New planets use Geography 4, NationalGeneration 2, and LocalMap 2. Existing planet versions and native snapshots retain their original algorithms.
- Do not publish, push, tag, package a release, update Workshop, reinstall the loader, or change player settings/saves outside the protected native-test workflow.
- Use the released loader 0.9.1 dependencies for native qualification. Build development DLLs into staging, then let the protected runner restore player files after each batch.
- Every production behavior starts with a failing test. Observe the expected failure before implementation.
- Use synchronous `ScreenCapture.CaptureScreenshotAsTexture` at `WaitForEndOfFrame`; asynchronous screenshot filenames proved unreliable under Proton.
- Do not use URP Particles/Unlit or Sprites/Default for the globe. Keep the existing opaque URP/Lit material and its culling/depth state.
- TruePlanet internals remain internal. `TruePlanetRevisionSuite` accesses them through these helpers, matching the existing `TruePlanetSuite` convention:

```csharp
private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
private static object Member(object target, string name)
{
    FieldInfo field = target.GetType().GetField(name, Any);
    if (field != null) return field.GetValue(target);
    return target.GetType().GetProperty(name, Any).GetValue(target, null);
}
private static object Static(Type type, string method, params object[] args) =>
    type.GetMethod(method, Any).Invoke(null, args);
private static object Call(object target, string method, params object[] args) =>
    target.GetType().GetMethod(method, Any).Invoke(target, args);
```

- Native evidence root: `/home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification`.

### Execution amendments: 2026-11-05

The owner reauthorized native qualification after the review-only checkpoint.
Tasks 1 and 2 have local commits `d7641ab` and `832124a`; later source remains
uncommitted. Current authorization covers local qualification and evidence, not
publication or a normal installed-mod update. The commit commands below are
historical plan steps, not instructions to capture unrelated working-tree state.

Build scripts copy local shared dependencies into their output root. Compile into
a disposable build directory, then copy only each mod's package directory into
qualification staging. Pin all four shared DLLs to the verified public loader
0.9.1 ZIP; the runner must reject any mismatch before snapshot or launch.
Do not execute the original direct-to-stage build examples below.

Permanent checks cover behavior and geometry/pixel invariants, not exact inspector
or biome wording. Inspect the actual native UI instead of retaining the illustrative
string-pinning tests below. Surface compatibility loads preserved baseline worlds;
it does not compare newly generated Geography 4 identities with an older generator.

The final `revision` mode measures Standard and Detailed rendering; consumer
regression and campaign modes exercise Small and TruePlanetLarge native maps.
Preserve the exact tested DLL and its source provenance. A non-deterministic rebuild
is not evidence of byte equality; do not replace the tested candidate with a new PE.

### Completed acceptance

All six tasks' implementation, native behavior, compatibility, visual inspection
and documentation acceptance criteria are satisfied under the amendments above.
The final released-dependency matrix passed 12 batches / 30 cases / 1,773 probe
assertions, plus 87 actual consumer checks per Small/Large fixture with zero
failures or skips. All 236 primary native screenshots were inspected; 3,840
rotation frames and the ocean/road pixel oracles are retained. Every batch
independently restored all 112 protected files and exact directory sets.

The checked result is `qualification-20261105/released-fixture/qualified-matrix.json`
beneath the native evidence root. The design document records exact measurements
and limits. The original unchecked steps below remain the historical procedure;
they do not indicate pending implementation or authorize another native probe.
Later local commits, normal installation and publication were not performed.

### GitHub release amendment: 2026-11-05

The owner subsequently authorized Detailed-grid rendering optimization followed
by a GitHub update. This supersedes the earlier publication boundary for the
scoped TruePlanet revision only. Target version: 0.4.0. Preserve the existing
0.3.1 package and loader 0.9.1; do not publish unrelated Performance/Plus work,
alter the local index/branch or update Workshop as part of this request.

Native profiling located the dominant cost in line mesh construction/upload.
The selected change shares identical corners and caches packed per-batch mesh
buffers and triangle indices, without thinning grid cells, changing picking or
replacing the globe shader. Controlled same-scene old/new renderer comparisons
must remain pixel-identical; batch shrink/grow/empty transitions and resource
teardown must remain valid. Requalify one exact candidate against released
dependencies before publishing source and the three-file GitHub ZIP.

The additional rendering acceptance and integrated release qualification are
complete: 13 batches / 32 native cases / 1,858 probe assertions, plus 174 actual
consumer checks without failures/skips. All eight controlled same-scene renderer
pairs are pixel-identical. The final 0.4.0 DLL is
`7a3bb6bf3eccecd665834e7882cdd8ea3495dd68d621adba334a52125d43a9f3`;
the design document and player README record measured limits and visual coverage.
Publication uses scoped GitHub tree/commit/release APIs, preserving the local
index/branch and all public files outside the reviewed source/documentation set.

## Native Qualification Setup

Perform this setup once before Task 1's native check:

```bash
mkdir -p /home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification
cp /home/deniz/ruinarch-runs/trueplanet-031-publication/native/regression/run.py /home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification/run.py
cp -r /home/deniz/ruinarch-runs/trueplanet-031-publication/native/regression/probe-source /home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification/probe-source
```

Add this runner case beside the existing modes:

```python
'revision': {'Small', 'TruePlanetLarge'},
```

Add this dispatch to the retained probe's `Run` before its generic globe fallback:

```csharp
else if (File.ReadAllText(Path.Combine(Root, "mode.txt")) == "revision") yield return RevisionChecks();
```

`RevisionChecks` creates an inactive `AutoTest` host, sets `_logPath` to `revision.log`, invokes `TruePlanetRevisionSuite`, requires zero failures/skips, and destroys the host in `finally`. The suite itself drives the real menu, generation, native setup, travel, and screenshots. Build the candidate and debug mod into the qualification `stage/` without deploying them manually:

```bash
/home/deniz/Desktop/Apps/Projects/RuinarchModLoader/tools/build-mod.sh /home/deniz/Desktop/Apps/Projects/RuinarchMods/TruePlanet /home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification/stage
/home/deniz/Desktop/Apps/Projects/RuinarchModLoader/tools/build-mod.sh /home/deniz/Desktop/Apps/Projects/RuinarchMods/RuinarchDebug /home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification/stage
```

Run a focused native batch:

```bash
MODE=revision CASES=Small EVIDENCE=/home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification/evidence-small python3 /home/deniz/ruinarch-runs/trueplanet-campaign-terrain-qualification/run.py
```

Expected final runner evidence: `PASS`, zero game errors/pathfinder faults, and byte/metadata/directory-set restoration.

---

### Task 1: Versioned Local Terrain Plan

**Files:**
- Create: `TruePlanet/LocalTerrainPlan.cs`
- Modify: `TruePlanet/PlanetGenerator.cs`
- Modify: `TruePlanet/PlanetModel.cs`
- Modify: `TruePlanet/PlanetGeography.cs`
- Modify: `TruePlanet/TileGame.cs`
- Modify: `TruePlanet/PlanetView.cs`
- Test: `RuinarchDebug/TruePlanetSuite.cs`
- Test: `RuinarchDebug/TruePlanetRevisionSuite.cs`

**Interfaces:**
- Produces: `LocalTerrainPlan.Build(Planet planet, int tileId)` returning native-resolution `Temperature`, `Moisture`, and `Elevation` arrays plus `Open`, `Mountains`, and `Water` counts.
- Produces: `LocalTerrainPlan.TerrainSummary()` returning exact player-facing percentages.
- Consumes: existing `PlanetGeography.Sample`, `LocalTileFootprint`, and `PlanetOptions.NativeDimensions`.
- Later tasks consume the same plan for capital scoring; no second terrain sampler is allowed.

- [ ] **Step 1: Write failing terrain-plan tests**

Add real-assembly checks that construct version-two Small and Large worlds, build capital, coastal, and wilderness plans, and assert:

```csharp
Check("local terrain plan matches native dimensions and reports exact classifications", () =>
{
    int open = 0, mountains = 0, water = 0;
    int width = (int)Member(plan, "Width"), height = (int)Member(plan, "Height");
    var elevation = (float[,])Member(plan, "Elevation");
    for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
    {
        if (elevation[x, y] < .23f) water++;
        else if (elevation[x, y] > .78f) mountains++;
        else open++;
    }
    return width == expectedWidth && height == expectedHeight
        && open == (int)Member(plan, "Open") && mountains == (int)Member(plan, "Mountains")
        && water == (int)Member(plan, "Water") && open + mountains + water == width * height;
});
```

Add boundary checks:

```csharp
Check("a mountain mass never dominates one new local map", () =>
    LargestMountainComponent((float[,])Member(newPlan, "Elevation")) * 4
        <= (int)Member(newPlan, "Width") * (int)Member(newPlan, "Height"));
Check("coastal water depth is not collapsed to one native band", () =>
    DistinctWaterBands((float[,])Member(coastalPlan, "Elevation")) >= 2);
Check("version-one terrain keeps its original constants", () =>
    ((float[,])Member(oldPlan, "Elevation")).Cast<float>().Distinct().OrderBy(v => v)
        .SequenceEqual(new[] { .15f, .5f, .9f }));
```

- [ ] **Step 2: Verify the red build**

Build `RuinarchDebug` against the current TruePlanet assembly. Expected failure: `TruePlanet.LocalTerrainPlan` does not exist. Do not add a stub merely to change the error.

- [ ] **Step 3: Implement the minimal deterministic plan**

Create the plan with this public surface and exact classification rules:

```csharp
internal sealed class LocalTerrainPlan
{
    internal int Width, Height, Open, Mountains, Water;
    internal float[,] Temperature, Moisture, Elevation;
    internal int Count => Width * Height;
    internal string TerrainSummary() =>
        (Open * 100.0 / Count).ToString("0") + "% open land · "
        + (Mountains * 100.0 / Count).ToString("0") + "% mountains · "
        + (Water * 100.0 / Count).ToString("0") + "% water";

    internal static LocalTerrainPlan Build(Planet planet, int tileId)
    {
        PlanetOptions.NativeDimensions(planet.Options.LocalSize, out int columns, out int rows);
        var plan = new LocalTerrainPlan
        {
            Width = columns * 14, Height = rows * 14,
            Temperature = new float[columns * 14, rows * 14],
            Moisture = new float[columns * 14, rows * 14],
            Elevation = new float[columns * 14, rows * 14]
        };
        plan.Populate(planet, tileId);
        if (planet.LocalMapVersion >= 2) plan.ConstrainMountains();
        plan.CountClassifications();
        return plan;
    }
}
```

`Populate` samples `(x + .5) / Width`, `(y + .5) / Height` from the existing footprint. Version one retains the old constants. Version two maps negative height to `0.22 - 0.20 * Clamp(-Height / .5)`, open land to `0.24 + 0.53 * Clamp(Height / .8)`, and mountains to `0.79 + 0.19 * Clamp(Mountains)`.

Before classification, taper secondary mountain cells within three native tiles of a non-water map boundary to plain. Split the largest eight-connected mountain component while it exceeds `Count / 4`: choose its longest bounding-box axis, cut a two-cell-wide plain corridor through its median coordinate, and recompute components. Eight-connected checks conservatively prevent diagonal reconnection. Each iteration removes mountain cells; no retry cap or random fallback is permitted.

- [ ] **Step 4: Route native generation and inspection through the plan**

In `TileGame.Terrain`, assign the existing cached arrays directly:

```csharp
LocalTerrainPlan plan = LocalTerrainPlan.Build(Active.Planet, Active.TileId);
_temperature = plan.Temperature;
_moisture = plan.Moisture;
_elevation = plan.Elevation;
```

In `PlanetView`, replace `_terrainTile` and independently sampled `_terrainSummary` with one cached inspected `LocalTerrainPlan`. `_terrainTile` may remain as the cache key, but the summary must be `plan.TerrainSummary()`.

Bump new generation to Geography 4, NationalGeneration 2, LocalMap 2 and include `g4-n2-l2` in the immutable ID. `Planet.Validate` accepts exactly the old `(3,1,1)` and new `(4,2,2)` combinations for format three. `TileLand.For` and `PlanetGeography.TileFootprint` accept both version combinations; version-one behavior remains unchanged.

Update the existing consumer assertions: new generation expects `(4,2,2)`, and the unknown LocalMap/NationalGeneration cases now assign `3`, not the newly supported `2`:

```csharp
foreach (string field in new[] { "LocalMapVersion", "NationalGenerationVersion" })
{
    var invalid = (JObject)original.DeepClone();
    invalid[field] = 3;
    Check("unknown " + field + " is refused", () => RejectTileWorld(invalid, planetType, validate));
}
```

- [ ] **Step 5: Verify green and native terrain equality**

Build both mods, run the real-assembly checks, then run `MODE=revision CASES=Small`. The native suite must compare the inspector percentages with counts from the live native elevation array and save/reload every durable terrain coordinate.

- [ ] **Step 6: Commit the terrain-plan checkpoint**

```bash
git add -- TruePlanet/LocalTerrainPlan.cs TruePlanet/PlanetGenerator.cs TruePlanet/PlanetModel.cs TruePlanet/PlanetGeography.cs TruePlanet/TileGame.cs TruePlanet/PlanetView.cs RuinarchDebug/TruePlanetSuite.cs RuinarchDebug/TruePlanetRevisionSuite.cs
git -c user.name=RuinarchRE -c user.email=re@local commit -m "TruePlanet: share deterministic local terrain plans"
```

---

### Task 2: Interior Settlement Anchors and Developed Capitals

**Files:**
- Modify: `TruePlanet/TileLand.cs`
- Modify: `TruePlanet/NationalProvinces.cs`
- Modify: `TruePlanet/SettlementTerrain.cs`
- Modify: `TruePlanet/TileGame.cs`
- Test: `RuinarchDebug/TruePlanetSuite.cs`
- Test: `RuinarchDebug/TruePlanetRevisionSuite.cs`

**Interfaces:**
- Consumes: `LocalTerrainPlan.Build` and exact terrain counts from Task 1.
- Produces: `TileGame.NativeVillageSize(Village village, LocalMapSize mapSize)`.
- Produces: NationalGeneration 2 interior anchors and full-footprint capital selection.

- [ ] **Step 1: Add failing anchor and capital tests**

Generate at least eight seeds at both resolutions. For every new village, assert exact cell ownership, positive height, and an interior margin:

```csharp
Check("settlements use dry interior anchors in their recorded cells", () =>
    worlds.SelectMany(w => w.Provinces.SelectMany(p => p.Villages)).All(v =>
        w.Mesh.Locate(v.Position) == v.TileId
        && geographyFor(w).Sample(v.Position).Height > 0
        && NearestCellEdgeAngle(w, v.TileId, v.Position) > InteriorMargin(w, v.TileId)));
```

For this test, `NearestCellEdgeAngle` is the minimum `asin(abs(dot(normalize(cross(edge.First, edge.Second)), village.Position)))` over the recorded cell's edges. `InteriorMargin` is five percent of the cell's minimum center-to-vertex angle. These are independent geometric checks, not calls back into the anchor selector.

Add capital tests:

```csharp
VILLAGE_SIZE NativeVillageSize(object village, LocalMapSize size) =>
    (VILLAGE_SIZE)Static(tileGameType, "NativeVillageSize", village, size);
Check("capitals request a developed native settlement tier", () =>
    NativeVillageSize(capital, LocalMapSize.Large) == VILLAGE_SIZE.Large
    && NativeVillageSize(capital, LocalMapSize.Small) == VILLAGE_SIZE.Medium
    && NativeVillageSize(ordinary, LocalMapSize.Large) == VILLAGE_SIZE.Small);
Check("preferred capital footprints have usable open land and bounded mountains", () =>
    capitalPlans.All(p => (int)Member(p, "Open") * 100 >= (int)Member(p, "Count") * 65
        && (int)Member(p, "Mountains") * 4 <= (int)Member(p, "Count")));
```

If a deliberately constrained fixture requires fallback, test the deterministic highest-scoring native-valid fallback separately instead of weakening the preferred-fixture assertion.

- [ ] **Step 2: Verify red failures on the existing generator**

Run the checks against NationalGeneration 1 fixtures. Expected failures: Small is emitted for capitals and edge-selected anchors fail the interior margin. Keep the failing output as the before evidence.

- [ ] **Step 3: Implement interior anchors**

For NationalGeneration 2, collect dry candidates from cell-interior triangle samples rather than polygon vertices. Each candidate must satisfy `Mesh.Locate(candidate) == tile`. Score by distance to the nearest cell edge, then height, then stable coordinates. Store the winning point as `DryPoint`; retain existing land area, coastal, and dry-link calculations. NationalGeneration 1 fixtures retain their recorded anchors and validation behavior.

- [ ] **Step 4: Implement full-footprint capital selection and native tier**

Use existing `SettlementTerrain.Suitable` as the cheap native-validity gate. Evaluate `LocalTerrainPlan` only for suitable capital candidates. Prefer at least 65 percent open, at most 25 percent mountain, and a contiguous Large-capital footprint. Rank remaining candidates by national separation, open fraction, moisture, and lower mountain fraction with stable tile-ID tie breaking. The first nation follows the same rule instead of accepting the first suitable tile.

Use this native tier mapping:

```csharp
internal static VILLAGE_SIZE NativeVillageSize(Village village, LocalMapSize mapSize)
{
    if (!village.Capital) return VILLAGE_SIZE.Small;
    return mapSize == LocalMapSize.Small ? VILLAGE_SIZE.Medium : VILLAGE_SIZE.Large;
}
```

Replace the unconditional `VILLAGE_SIZE.Small` in `TileGame.Play` with `NativeVillageSize(village, planet.Options.LocalSize)`.

- [ ] **Step 5: Verify native capital construction**

Run `MODE=revision CASES=Small`. The suite must generate a real capital, confirm the requested native tier, count dwellings/facilities against that tier, advance at least 30 native ticks, and observe resident movement/pathfinding. Capture `capital-generated.png` synchronously.

- [ ] **Step 6: Commit the settlement checkpoint**

```bash
git add -- TruePlanet/TileLand.cs TruePlanet/NationalProvinces.cs TruePlanet/SettlementTerrain.cs TruePlanet/TileGame.cs RuinarchDebug/TruePlanetSuite.cs RuinarchDebug/TruePlanetRevisionSuite.cs
git -c user.name=RuinarchRE -c user.email=re@local commit -m "TruePlanet: generate interior settlements and developed capitals"
```

---

### Task 3: Unified Campaign Travel

**Files:**
- Modify: `TruePlanet/CampaignCarry.cs`
- Modify: `TruePlanet/TileGame.cs`
- Modify: `TruePlanet/TileTravel.cs`
- Test: `RuinarchDebug/TruePlanetStorageChecks.cs`
- Test: `RuinarchDebug/TruePlanetRevisionSuite.cs`

**Interfaces:**
- Produces: additive `CampaignCarry.Archetype`, `PortalLevel`, and `PortalChoices`.
- Produces: `CampaignPortalChoice` with `Tier`, `ItemIndex`, `ChosenPower`, and `PowerChoices`.
- Produces: `TileGame.Play(Planet planet, int tileId, string campaignId, CampaignCarry carry)`; the existing three-argument call is migrated to pass null for initial campaign creation.
- Produces: automatic first-visit setup inside the existing source-commit/source-recovery transaction.

- [ ] **Step 1: Write failing portable-setup tests**

Capture a nondefault archetype, Portal level above one, chosen optional upgrade, and pending offered choices. Assert a JSON round trip preserves all fields and currencies/skills:

```csharp
Check("campaign setup preserves lord, Portal level and upgrade choices", () =>
{
    string json = JsonConvert.SerializeObject(carry);
    object restored = JsonConvert.DeserializeObject(json, carryType);
    return Equals(Member(restored, "Archetype"), Member(carry, "Archetype"))
        && Equals(Member(restored, "PortalLevel"), Member(carry, "PortalLevel"))
        && JsonConvert.SerializeObject(Member(restored, "PortalChoices"))
            == JsonConvert.SerializeObject(Member(carry, "PortalChoices"))
        && JsonConvert.SerializeObject(Member(restored, "Skills"))
            == JsonConvert.SerializeObject(Member(carry, "Skills"));
});
```

Add validation cases for duplicate tier/item choices, unavailable powers, invalid levels, and missing old-manifest fields. Old fields default safely; the next live `Capture` replaces legacy defaults with actual state.

- [ ] **Step 2: Verify red compile/validation failures**

Expected compile failure: the new portable fields/type do not exist. Do not weaken existing currency/skill checks.

- [ ] **Step 3: Implement portable state capture, validation, and application**

Capture `PlayerSkillManager.Instance.selectedArchetype`, the live Portal level, and every `portalUpgradeItems` entry in stable tier/item order. If the source tile has no standing Portal, retain the previous campaign's Portal fields while still capturing current archetype/currencies/skills.

Validate level and tier/item identities against the destination loadout before mutating it. Restore `_powerChoices` and chosen powers through the native fields/methods, then run the existing exact skill/currency restoration so native starting-level grants cannot add or reroll progression.

- [ ] **Step 4: Implement automatic native first-visit setup**

When `carry` is nonnull, set `settings.playerSkillSettings.startingPortalLevel = carry.PortalLevel` before native Portal construction. In `TileTravel.Ready`, replace the player-input wait with a coroutine that:

```csharp
LocationGridTile site = GridMap.Instance.mainRegion.areas
    .OrderBy(a => a.gridTileComponent.centerGridTile.localPlace.x)
    .ThenBy(a => a.gridTileComponent.centerGridTile.localPlace.y)
    .Select(a => a.gridTileComponent.centerGridTile)
    .First(tile => portalPrefab.HasEnoughSpaceIfPlacedOn(tile, out _)
        && tile.area.structureComponent.CanBuildDemonicStructureHere(STRUCTURE_TYPE.THE_PORTAL, out _));
AccessTools.Method(typeof(Player_Input.PickPortalInputModule), "PlacePortal")
    .Invoke(PlayerManager.pickPortalInputModule, new object[] { site });
UIManager.Instance.initialWorldSetupMenu.OnClickConfigureLoadOut();
UIManager.Instance.initialWorldSetupMenu.loadOutMenu.OnClickContinue();
```

Scope a `SkillTreeSelector.GetSelectedArchetype` prefix to the active automatic setup and return `_carry.Archetype`; all ordinary/new-game selections remain native. Suppress `UIManager.ShowStartScenario` only while that automatic setup is active:

```csharp
[HarmonyPatch(typeof(UIManager), nameof(UIManager.ShowStartScenario))]
private static class AutomaticTravelIntro
{
    private static bool Prefix() => !AutomaticSetupActive;
}
```

Wait for actual `gameHasStarted`, completed tile-object generation, and dismissal/state restoration before applying portable state. No valid site, native error, or setup timeout throws into the existing source-recovery path.

- [ ] **Step 5: Verify native first visit, return, save/reload, and rollback**

Run `MODE=revision CASES=Small`. Update the retained probe's old `CampaignChecks` first-visit path to invoke the travel button and wait for the automatic transaction instead of calling `FinishSetup(sourceManager)`:

```csharp
Control(canvasRoot, "Switch tile").onClick.Invoke();
yield return Wait(() => Value(travel, "Stage").ToString() == "Idle" || Value(travel, "LastError") != null,
    900, "automatic wilderness travel commit");
```

Do not retain a manual Portal/loadout helper on the automatic path. Assert:

- No Portal prompt, loadout menu, or intro wait occurs on first travel.
- Archetype, currencies, skills, Portal level, chosen powers, and offered choices equal the source.
- Source residents, source Portal persistent ID, local clock, and terrain return exactly.
- Normal native save/reload retains campaign setup.
- Forced no-valid-site and unavailable-skill cases recover the original source without publishing a destination revision.

Capture `travel-automatic.png` and `travel-return.png` synchronously.

- [ ] **Step 6: Commit the campaign checkpoint**

```bash
git add -- TruePlanet/CampaignCarry.cs TruePlanet/TileGame.cs TruePlanet/TileTravel.cs RuinarchDebug/TruePlanetStorageChecks.cs RuinarchDebug/TruePlanetRevisionSuite.cs
git -c user.name=RuinarchRE -c user.email=re@local commit -m "TruePlanet: preserve campaign setup during travel"
```

---

### Task 4: Zoom-Aware Globe Overlay Performance

**Files:**
- Modify: `TruePlanet/MapGeometry.cs`
- Modify: `TruePlanet/GlobeOverlays.cs`
- Modify: `TruePlanet/PlanetGlobe.cs` only if instrumentation exposes a separate invalidation defect
- Test: `RuinarchDebug/TruePlanetSuite.cs`
- Test: `RuinarchDebug/TruePlanetRevisionSuite.cs`

**Interfaces:**
- Produces: `MapGeometry.BuildSphericalLod(Planet, PlanetGeography, IReadOnlyList<SphericalSegment>)` for low-zoom rendering.
- Full geometry remains authoritative for picking, full-zoom rendering, validation, and existing road/border tests.
- LOD changes presentation only; no planet data or geographic IDs change.

- [ ] **Step 1: Add failing LOD and native performance measurements**

Add a deterministic geometry check that low-zoom output contains fewer strokes, preserves each chain's endpoints, and never joins disconnected/different-kind strokes. Add a native rotation measurement that records actual frame intervals and `_strokes.Count` for 240 frames at zoom one and six, default layers and grid, Standard and Detailed.

```csharp
int fullCount = ((ICollection)full).Count, lodCount = ((ICollection)lod).Count;
Check("low-zoom globe reduces real overlay geometry", () => lodCount * 3 < fullCount);

object[] lowChain = ((IEnumerable)Static(mapGeometryType, "BuildSphericalLod", world, geography,
    new[] { firstSegment, secondSegment })).Cast<object>().ToArray();
Check("LOD joining preserves a known chain's terminals", () =>
    lowChain.Length == 1
    && JsonConvert.SerializeObject(Member(lowChain[0], "A")) == JsonConvert.SerializeObject(a)
    && JsonConvert.SerializeObject(Member(lowChain[0], "B")) == JsonConvert.SerializeObject(c));
```

The native acceptance gate is a measured improvement over the captured before run at zoom one, no zoom-six regression beyond timing noise, and zero work on unchanged frames. Record medians/95th percentiles rather than claiming an unsupported fixed FPS guarantee.

- [ ] **Step 2: Verify red results**

Expected failure: `BuildSphericalLod` is absent and zoom-one baseline has the existing high stroke count. Save the before measurements in the native evidence case.

- [ ] **Step 3: Implement coastline-safe cached LOD**

Build low-detail geometry once per world. Join only consecutive segments with identical kind/ownership and coincident endpoints. Limit a joined arc to 0.016 radians, preserve chain start/end, and reject a join when its sampled midpoint crosses from dry to wet. Keep the selected-tile outline at full detail. Use LOD below zoom two and full geometry at zoom two or above.

`GlobeOverlays.Project` selects the cached list before its existing visibility/clipping loops. Do not rebuild LOD during drag, zoom, selection, label projection, or layer toggles. Rebuild only when the surface's planet reference changes.

- [ ] **Step 4: Verify rendering, continuity, lifetime, and timing**

Run both `MODE=revision CASES=Small` and `CASES=TruePlanetLarge`. Inspect default and grid captures at zoom one/six. Existing road/border dry-path invariants, horizon clipping, seam/pole picks, unchanged-frame zero-render checks, and resource cleanup must pass.

- [ ] **Step 5: Commit the rendering checkpoint**

```bash
git add -- TruePlanet/MapGeometry.cs TruePlanet/GlobeOverlays.cs RuinarchDebug/TruePlanetSuite.cs RuinarchDebug/TruePlanetRevisionSuite.cs
git -c user.name=RuinarchRE -c user.email=re@local commit -m "TruePlanet: reduce low-zoom globe overlay work"
```

If `PlanetGlobe.cs` changed after instrumentation, add that exact path to the same commit; otherwise leave it untouched.

---

### Task 5: Ocean Raster and Concise Native UI

**Files:**
- Modify: `TruePlanet/PlanetSurface.cs`
- Modify: `TruePlanet/PlanetView.cs`
- Modify: `TruePlanet/TileTravel.cs`
- Create: `TruePlanet/TilePresentation.cs`
- Test: `RuinarchDebug/TruePlanetSuite.cs`
- Test: `RuinarchDebug/TruePlanetRevisionSuite.cs`

**Interfaces:**
- Produces: pure `TilePresentation.BiomeName(TerrainSample)` and selection/status text helpers.
- Consumes: Task 1's exact `LocalTerrainPlan.TerrainSummary`.
- Ocean filtering changes only deep-water display pixels; geography and picking remain unchanged.

- [ ] **Step 1: Add failing ocean and copy tests**

Use the measured strongest submerged ridge fixture. Assert the filtered deep-water pixel differs from the raw narrow ridge while a coastal fixture and every land pixel remain exact. Assert the complete player-facing strings:

```csharp
Check("tile inspector uses concise player language", () =>
    heading == "Land details"
    && inspector.StartsWith("Temperate forest")
    && inspector.Contains("Temperature: ")
    && inspector.Contains("\nMoisture: ")
    && inspector.Contains((string)Call(plan, "TerrainSummary"))
    && inspector.Contains("No settlements on this land.")
    && !inspector.Contains("Geographic tile")
    && !inspector.Contains("Village culture")
    && !inspector.Contains("Land area:")
    && status == "Ready to travel.");
```

Add representative biome cases for hot rainforest, temperate forest, boreal forest, hot desert, arid scrubland, grassland, and snow.

- [ ] **Step 2: Verify red failures**

Run the real-assembly/UI checks against the released behavior. Expected failures include the old `World / Tile`, combined climate line, sampled-terrain wording, culture disclaimer, and verbose status. Save the raw/filtered ridge values.

- [ ] **Step 3: Implement coast-safe deep-water filtering**

After the initial raster height sampling, clone `Heights` once. For each pixel whose entire 3 by 3 neighborhood is below `-0.006`, write the median of those nine values into the clone. Reuse one nine-element buffer and wrap only horizontal longitude; vertical edges are excluded. Replace `Heights` with the filtered array before shading. Recompute only fully deep-ocean colors with the same existing ocean-color formula; land/coasts and political land tints remain exact.

- [ ] **Step 4: Implement concise UI and native atlas appearance**

Create `TilePresentation` with deterministic biome labels derived from the existing sample thresholds. `PlanetView.InspectTile` starts with the biome, keeps province/nation/map size as useful secondary lines, prints temperature and moisture separately, uses the exact terrain summary, and prints `No settlements on this land.` when empty. Remove geographic IDs, steradians, and the culture disclaimer. Use `Land details`, `Travel`, `Ready to travel.`, and `Ready to play.` at the specified selection points; preserve real generation/error messages.

In `TileTravel`, retain the cloned root `RuinarchToggle` instead of replacing it with a plain Button. Clear its group/listeners/hotkey and shine dependencies, force its unselected state, open the atlas on activation, and return it to unselected without changing native Achievements or other toggles.

- [ ] **Step 5: Verify native visuals at all target sizes**

Run revision/HUD cases at 1280x720, 1568x878, 1920x1080, and 2560x1080. Update retained harness consumers from `Switch tile` to `Travel` and from a plain atlas `Button` to the retained native `RuinarchToggle`. Remove the old incidental format-three terrain-hash equality check; replace it with the independent deep-water/coastal pixel oracle from Step 1 while retaining world identity and land-color checks.

Synchronously capture the strongest-ridge before/after orientation, concise inspector, native top bar, and travel status. Compare the atlas toggle's normal sprite/colors with its native source and exercise hover/click once. Inspect every final image for clipping, backface artifacts, ocean lines, and excessive button glow.

- [ ] **Step 6: Commit the presentation checkpoint**

```bash
git add -- TruePlanet/PlanetSurface.cs TruePlanet/PlanetView.cs TruePlanet/TileTravel.cs TruePlanet/TilePresentation.cs RuinarchDebug/TruePlanetSuite.cs RuinarchDebug/TruePlanetRevisionSuite.cs
git -c user.name=RuinarchRE -c user.email=re@local commit -m "TruePlanet: clarify tile UI and smooth deep ocean raster"
```

---

### Task 6: Full Regression, Documentation, and Local Handoff

**Files:**
- Modify: `TruePlanet/README.md`
- Modify: `TruePlanet/mod.json` description to describe unified campaign setup instead of currencies/skills alone
- Modify: `docs/specs/2026-11-02-trueplanet-campaign-terrain-design.md` status with measured results
- Test: all existing native qualification modes plus `RuinarchDebug/TruePlanetRevisionSuite.cs`

**Interfaces:**
- Final local development checkpoint; no release version bump or publication is implied.
- Documentation must distinguish new-planet terrain rules from unchanged existing saved maps.

- [ ] **Step 1: Run focused and complete native matrices**

Run both revision cases with zero fail/skip. Then run the retained complete matrix: `stock-loading`, `conquest`, `responsive`, `campaign`, `regression`, `lifetime`, `legacy-control`, `globe`, `roads`, `surface`, and `hud`, with every case accepted by the existing runner for those modes. Require zero unexpected Unity errors/pathfinder faults and exact protected-file restoration after every batch.

- [ ] **Step 2: Inspect and measure final behavior**

Read every final primary screenshot. Record actual rotation median/95th-percentile frame intervals and overlay counts at zoom one/six, capital counts, campaign setup equality, ocean before/after pixels, and resource lifetime. Do not convert whole-run memory/native-generation peaks into interactive FPS claims.

- [ ] **Step 3: Update player and developer documentation**

Document the single initial Portal/loadout setup, automatic later travel, unified Portal/loadout progression, developed capitals, exact terrain percentages, mountain constraints, version compatibility, concise UI, ocean filtering, and measured rendering limits. Remove statements that every first visit requires manual native setup.

- [ ] **Step 4: Verify the final local source/build state**

Rebuild both mods from the committed source, compare the tested DLL hashes with the final staged DLLs, verify the game is stopped and probe/backup absent, and independently confirm protected player files and unrelated workspace files are unchanged. Archive the private native evidence before deleting owned qualification scaffolding.

- [ ] **Step 5: Commit the verified local handoff**

```bash
git add -- TruePlanet/README.md TruePlanet/mod.json docs/specs/2026-11-02-trueplanet-campaign-terrain-design.md RuinarchDebug/TruePlanetRevisionSuite.cs
git -c user.name=RuinarchRE -c user.email=re@local commit -m "docs: record verified TruePlanet campaign and terrain revision"
```

If no listed file changed during final verification, do not create an empty commit. Report the actual commits, checks, measurements, compatibility boundary, and local-only status. Publication requires a separate owner decision.
