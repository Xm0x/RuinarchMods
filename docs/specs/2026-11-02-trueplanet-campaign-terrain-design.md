# TruePlanet unified campaign and terrain revision

Status: TruePlanet 0.4.0 release qualification complete. Original revision approved on 2026-11-02; Detailed-grid optimization and subsequent GitHub publication authorized on 2026-11-05. Historical checkpoints retain their original local-only boundaries. Normal installation, loader changes and Workshop publication are not part of this GitHub update.

## Goal

Make geographic tiles produce believable native maps, make capitals arrive as developed settlements, preserve one campaign setup during travel, and make the globe and its player-facing UI feel like a coherent part of Ruinarch.

## Evidence and root causes

The owner supplied seventeen native screenshots covering globe performance, misplaced settlement markers, local terrain, capitals, repeated setup, button highlighting, inspector copy, ocean lines, and verbose travel status.

| Area | Observed implementation cause |
|---|---|
| Globe rotation | `GlobeOverlays.Project` reprojects every front-facing border and road on each drag and rebuilds visible uGUI line meshes. Zooming out retains the most strokes. Existing native measurements reached 46.963 ms Standard and 87.546 ms Detailed with grid enabled; camera submission was about 0.21 ms. |
| Settlement markers | `TileLand.Build` chooses the highest sampled dry point, including polygon-edge samples. It proves dry ownership but not a visually interior marker. Existing validation checks `Mesh.Locate` and positive height, so it does not catch the presentation problem. |
| Capital terrain | Capital selection checks whether a settlement can fit somewhere. The first capital accepts the first suitable tile; later scoring uses a center sample rather than the full local footprint. A valid site can therefore be dominated by mountains. |
| Capital development | `TileGame.Play` passes every village to native generation as `VILLAGE_SIZE.Small`, including records marked `Capital`. Native generation already supports Small, Medium, and Large. |
| Mountains | `PlanetGeography.Elevation` emits only water 0.15, plain 0.5, and mountain 0.9. Native generation turns every connected mountain mask into a cave/mountain structure and applies cellular-automata walls. Large sampled masses become map-dominating structures and arbitrary local boundaries clip them. |
| Repeated campaign setup | `TileTravel.Ready` deliberately waits for Portal placement and loadout selection on every first visit. `CampaignCarry` restores currencies and skills only afterward. It does not carry archetype, Portal level, or upgrade choices. |
| Inspector and status | `PlanetView.InspectTile` independently samples a 16 by 16 footprint and exposes implementation details such as geographic IDs, sampled-terrain wording, native setup, and culture disclaimers. |
| Ocean lines | An offline probe against the owner's current planet found 83,546 border/road segments, zero wet midpoints, and no segment at or above 0.004 radians. A native reproduction facing the strongest submerged ridge exposed the long raster line. `PlanetGeography.Raw` adds tectonic ridges above and below sea level, while ocean color and shading amplify height changes. Alternative shader probes were rejected: the URP Unlit shader is absent, Particles/Unlit loses the sphere, and Sprites/Default exposes the back hemisphere through unsuitable culling/depth defaults. |

## Approaches

1. **Coherent revision, selected.** Share one local terrain plan, use terrain-aware capitals, preserve campaign setup, and add zoom-aware rendering.
2. **Minimal patches, rejected.** Lower line counts, enlarge capitals, and skip setup would leave terrain reporting and Portal progression inconsistent.
3. **Custom map generation or portal-free destinations, rejected.** Native progression, migration, retaliation, jobs, and settlement behavior depend on the stock generator and a local Portal.

## Version and save compatibility

New planets use `GeographyVersion = 4`, `NationalGenerationVersion = 2`, and `LocalMapVersion = 2`. Their immutable IDs include the new versions. Existing format-three version-one maps, format-one/two legacy worlds, native saves, and immutable campaign snapshots retain their original algorithms and terrain.

Existing campaigns receive travel, rendering, and UI fixes. Their stored worlds are not regenerated or silently migrated. Revised geography and capital placement apply to newly generated planets. This prevents a mod update from changing an existing saved map beneath the player.

The campaign manifest remains format one. New portable fields are additive and missing values in old revisions are handled as legacy defaults. Every new travel transaction captures fresh values from the live source player before use, so an old manifest does not force `PLAYER_ARCHETYPE.Normal` or Portal level zero onto a destination.

## Local terrain plan

Add a focused `LocalTerrainPlan` owned by the TruePlanet assembly. It is deterministic from the planet identity, geographic cell, and local-map version. It contains native-resolution temperature, moisture, and elevation arrays plus exact open/mountain/water counts.

Both `TileGame` and `PlanetView` consume this plan. Native noise hooks return its arrays. The inspector reports its actual classifications; there is no separate 16 by 16 display sample. Cache only bounded active/inspection plans and release them with their owning context.

For version-two maps:

- Water retains normalized depth across native water thresholds rather than using one constant.
- Open land retains relief below the native mountain threshold.
- Mountain cells retain relief above the threshold.
- Secondary mountain detail tapers before arbitrary non-water local boundaries. Coastal water boundaries remain coherent.
- A single connected mountain mass may occupy at most 25 percent of local cells. Deterministic narrow plain corridors split larger masses into separate native structures.
- Terrain changes happen before native elevation-island construction, so native paths, resources, caves, structures, save data, and the inspector all observe the same result.

The 25 percent limit applies to one connected mass, not to total mountain coverage. It prevents one structure from consuming the map without requiring every tile to have the same terrain mix.

## Settlements and capitals

Geographic anchors are chosen from dry interior candidates whose exact `Mesh.Locate` result is the recorded cell. Prefer distance from the cell edge, then elevation and determinism. Political ownership never changes the anchor, native seed, or geography.

Capital suitability evaluates the complete local plan. Preferred capital sites have at least 65 percent open land, no more than 25 percent mountains, a contiguous native-valid Large settlement footprint, and the water/mountain resources required by the faction. Deterministic fallback selects the highest-scoring native-valid site if no candidate meets every preference; generation must not loop indefinitely.

Native settings use `VILLAGE_SIZE.Large` for capitals when dimensions support it and `Medium` on constrained maps. Ordinary villages remain Small. Native generation must produce the expected larger dwelling/facility counts and a usable settlement rather than merely relabeling a village.

## Unified campaign transaction

The first map of a new campaign retains normal native Portal placement and loadout selection. Subsequent travel uses one campaign setup.

Add portable state for:

- `PLAYER_ARCHETYPE` selected by the player;
- current local Portal level;
- each native Portal upgrade item's tier, item index, chosen power, and offered power choices;
- existing currencies, powers, and skill progression.

Validate tier/item identities and skill availability before applying portable state. Do not serialize Unity objects or native structure identities into the campaign manifest.

On a first visit, `TileGame.Play` applies the carried starting Portal level before native construction. `TileTravel` then:

1. Commits the source snapshot and captures live campaign state.
2. Starts native destination generation with the same campaign identity.
3. Finds a deterministic site that passes the native Portal prefab space check and native demonic-placement check.
4. Invokes the stock Portal placement path, including its normal callbacks.
5. Runs stock loadout continuation with the carried archetype, preserving native initialization signals and progression startup.
6. Restores offered/chosen upgrade state and exact portable skills/currencies after native initialization replaces defaults.
7. Commits and saves the destination only after the complete setup succeeds.

No first-visit setup screen waits for player input. Revisited tiles keep the existing snapshot-load path. Local Portal objects and local worlds remain distinct; campaign setup and progression are unified.

Missing valid placement, unavailable archetype/skills, native errors, or commit failure abort travel. Existing source recovery restores the source snapshot and carried state. There is no fallback to a fresh campaign and no silent destination generation after a corrupt snapshot is rejected.

## Globe rendering and ocean presentation

Cache low-zoom geometry per world. Reduce subpixel segments and preserve connected endpoints before creating strokes. Cull work outside the viewport before mesh construction. Use full-detail geometry at close zoom and preserve layer toggles, picking, labels, roads, borders, and selection outlines.

Keep the proven opaque `Universal Render Pipeline/Lit` material, mesh culling, depth writes, and isolated preview camera. Do not substitute particle or sprite shaders. Remove narrow deep-water raster ridges with a 3 by 3 median height filter only where the center and all eight neighbours are below `-0.006`. Use the filtered depth for deep-ocean color and shading. Pixels near coastlines keep their original values. Geography, dry-land picking, border/road geometry, native maps, and saved terrain do not change.

Measure actual frame intervals and overlay/render phases during sustained rotation at minimum and maximum zoom, with default layers and with grid enabled. Record Standard and Detailed results separately. Unchanged visible frames must still perform zero globe renders and zero overlay projections.

Native visual checks cover coastlines, seam/pole rotations, the owner's reported ocean-line orientation, and low/high zoom. The geometry invariant that borders and roads stay on dry sampled paths remains intact.

## Player-facing UI

- The top-bar atlas entry reuses the native toggle's unselected appearance and hover behavior without inherited shine or hotkey side effects.
- `World / Tile` becomes `Land details`.
- The first inspector line is a biome name such as `Temperate forest`, derived from actual temperature and moisture.
- Temperature and moisture occupy separate lines.
- Terrain uses exact plan percentages, for example `54% open land · 46% mountains · 0% water`.
- Remove the culture/terrain disclaimer, geographic numeric ID, and steradian land-area text.
- Empty settlement text is `No settlements on this land.`
- Selection status is `Ready to travel.` during a campaign and `Ready to play.` before campaign creation. Errors and actual operation progress remain specific.
- The travel action may use the concise caption `Travel`; behavior is unchanged apart from unified setup.

No developer implementation notes belong in ordinary player-facing panels.

## Verification

Use failing-before/passing-after checks for each behavior, followed by real native execution.

1. **Geometry and terrain:** marker ownership/interior invariants across seeds and both resolutions; deterministic local plans; exact inspector/native percentages; mountain component and edge rules; water depth and native save/reload terrain equality.
2. **Capitals:** actual Large/Medium settings reach native generation; dwelling/facility counts match the native tier; settlement residents move and pathfind; preferred open/mountain limits hold on the approved fixtures.
3. **Campaign:** one initial manual setup; automatic first visit with no Portal/loadout prompt; preserved archetype, currencies, skills, Portal level, chosen/offered upgrades, source residents, source Portal identity, local clocks, and committed terrain; normal native save/reload; return travel; corrupt/missing destination and failed-placement recovery.
4. **Rendering:** measured rotation at zoom one and six, default layers and grid, Standard and Detailed; unchanged-frame invalidation; resource lifetime; no unexpected Unity/pathfinder errors.
5. **UI and ocean:** actual screenshots at 1280 by 720, 1568 by 878, 1920 by 1080, and 2560 by 1080; native atlas button comparison; concise inspector/status; before/after rendering at the measured strongest submerged ridge; proof that deep-water narrow ridges are suppressed while coastal pixels, land colors, geography, and picking remain unchanged.
6. **Compatibility:** legacy format-one/two play and reload, old geographic snapshots, ordinary native saves with TruePlanet context absent, and existing campaign manifests.

Run the existing complete native regression matrix after focused checks pass. Update TruePlanet documentation to describe actual behavior and measured limits. Preserve player saves, settings, subscriptions, and unrelated working-tree changes. Do not publish commits, tags, packages, or Workshop content without explicit owner approval.

## Implementation checkpoint: 2026-11-03

Tasks 1 and 2 are committed locally as `d7641ab` and `832124a`. The Task 2 native run passed 75 consumer checks and 20 probe assertions with zero unexpected game or pathfinder errors. It verified all 16 generation cases, zero edge/outside settlement anchors, 32 preferred capitals, a native Medium capital with nine dwellings and four native tier facilities, save/reload, and resident movement. All 112 protected files restored with identical bytes and metadata.

Tasks 3 through 5 have compiled source and offline real-assembly checks, but no completed native qualification. Portable JSON/legacy/validation checks pass. The LOD fixture reduces 80,477 full strokes to 23,968 with all 23,968 sampled midpoints dry. The ocean fixture changes the submerged ridge from -0.02 to -0.08 while coastal and land fixtures remain exact; concise inspector/status and seven biome labels pass. Automatic travel, rollback, native toggle behavior, zoom timing, and final visuals remain unverified. The owner explicitly prohibited further probes on 2026-11-03, so these are not marked complete and no measured native performance or final-release claim is made.

## Review checkpoint: 2026-11-05

The later native campaign run `final-task3-campaign-d` passed 58 assertions with zero unexpected game errors or pathfinder faults. It exercised automatic first visits, portable state equality, native reload, return travel, and source recovery after both a forced invalid Portal site and a forced unavailable destination skill. The run restored all 112 protected files, including metadata and directory sets. This supersedes the earlier statement that automatic travel and rollback had not been exercised.

The final focused regression, `final-task3-regression-b`, ended without a result marker after 84 passing consumer checks. Its logs contain no failed assertion or managed exception. Steam recorded process termination before completion; the retained records do not establish the underlying cause. This is an incomplete run, not a clean regression pass.

The code review reproduced and fixed two offline LOD edge cases: a backtracking chain could collapse into a zero-length stroke, and endpoint distance alone did not bound the traversed angular length. LOD now retains nonzero strokes and limits cumulative arc length. Permanent regression coverage checks both cases; incidental inspector/biome wording assertions were removed. The retained qualification harness now accepts only the exact armed forced error, once, rather than ignoring any travel error while an expectation is active.

These review changes are verified offline against the actual compiled assemblies. They are not native visual or timing qualification. The owner's latest instruction permits no new probes; no additional native run, installation, commit, or publication is part of this review. Zoom timing, the full compatibility matrix, and final UI/ocean visual qualification remain pending.

The final offline review sweep passed all 16 generation fixtures: 466 interior settlement anchors, 32 preferred capitals, and zero failures. Revised LOD produced 24,033 strokes from 80,477 at Standard resolution and 50,689 from 171,351 at Detailed resolution; every sampled midpoint was dry and every retained stroke was nonzero. Twelve geometry/ocean boundary checks and three error-accounting checks passed. TruePlanet, RuinarchDebug, and the retained qualification assembly compiled with zero Harmony-target failures. Evidence and scoped source snapshots are retained under `~/ruinarch-runs/trueplanet-campaign-terrain-qualification/standards-review-20261105/`.

## Release-fixture qualification resumed: 2026-11-05

The owner explicitly reauthorized native qualification. The initial resumed Small regression completed with 87 consumer checks and 20 probe assertions; focused rendering and four-size HUD runs also completed. Subsequent dependency auditing found that `build-mod.sh` had copied the local development `Ruinarch.ModContent.dll` into staging. Those runs remain supplementary behavior evidence, not qualification of the published loader fixture.

The matrix launcher was stopped at its current restored batch boundary without interrupting the native game. All 112 protected files were independently verified afterward. Staging now matches all four dependency hashes extracted from the archived, verified public loader 0.9.1 ZIP (`554ee9c36fef77355e0b3cecacbab2c333161746428619ca535715a727f2e06d`). The released framework hash is `74288320514e4f7251cc04c138b4416f6b0806f1a52a2ae668b977b7ff41b092`; local framework `a6edbb764a0bbfac86cb7bce68dbef3d62f1575fa2f8bb0b3068fcf2b9c233e8` is excluded.

The runner now rejects dependency mismatches before snapshot or launch. Negative and positive guard checks passed. The fresh 30-case matrix under `qualification-20261105/released-fixture/` completed successfully. It supersedes the supplementary resumed runs for release-fixture qualification; no publication or normal installed-mod update is implied.

## Qualified local handoff: 2026-11-05

The exact reviewed TruePlanet candidate (`8a0c8a4b5c5a43ee43df61fe347eab8df3c1c98f4baf61922edfca102b745c31`) passed all 12 batches / 30 native cases against the verified public loader 0.9.1 fixture. All 62 production/debug C# source files are byte-identical to every batch's captured source. No post-qualification production rebuild replaced the tested PE.

| Mode | Cases | Probe assertions |
|---|---:|---:|
| Regression | 2 | 40 |
| Rendering revision | 2 | 50 |
| HUD | 4 | 341 |
| Preserved surface baselines | 4 | 48 |
| Globe | 4 | 404 |
| Roads | 4 | 400 |
| Stock loading | 1 | 7 |
| Conquest | 1 | 14 |
| Responsive | 2 | 170 |
| Campaign | 2 | 116 |
| Lifetime | 2 | 155 |
| Legacy control | 2 | 28 |
| **Total** | **30** | **1,773** |

The actual consumer regressions additionally passed 87 checks per Small/Large fixture, with zero failures or skips. Unexpected Unity errors and pathfinder faults were zero. Automatic setup, exact portable progression, native reload/return, invalid-site rollback and unavailable-skill rollback passed on both native sizes. Medium capitals produced nine dwellings/four tier facilities; Large capitals produced fourteen/five. Resident activity and durable terrain equality passed.

The final visual inspection covered all 236 primary native images in 32 labelled contact sheets, plus original-resolution native top-bar and selected-inspector crops. All four client sizes retained their anchored actions and scrolling panels; no new clipping, excessive atlas-toggle glow, seam/pole, backface or route-continuity defect was observed in the exercised views. Road sampling found zero missing pixels among 20,673 unobscured samples.

### Measured rendering limits

The same-scene full/LOD comparison measured 240 rotation frames per configuration, 3,840 frames total. Values below are actual native frame intervals, not GPU timings:

| Resolution | Layers | Zoom | Full median / p95 (ms) | LOD median / p95 (ms) |
|---|---|---:|---:|---:|
| Standard | Default | 1 | 54.1154 / 61.7369 | 15.7062 / 17.6010 |
| Standard | Grid | 1 | 309.0757 / 386.4815 | 80.0961 / 93.4917 |
| Detailed | Default | 1 | 45.7979 / 52.7088 | 13.0884 / 15.7447 |
| Detailed | Grid | 1 | 653.7020 / 810.9718 | 164.8711 / 197.2982 |
| Standard | Default | 6 | 7.2579 / 9.4481 | 7.2544 / 9.4359 |
| Standard | Grid | 6 | 29.1550 / 32.9319 | 28.9817 / 31.9599 |
| Detailed | Default | 6 | 6.9817 / 9.5484 | 6.9800 / 9.3045 |
| Detailed | Grid | 6 | 58.5856 / 65.2320 | 58.7029 / 65.4147 |

Low-zoom median overlay projection times fell from 3.6965 to 1.1396 ms (Standard/default), 19.0823 to 5.7687 ms (Standard/grid), 3.2264 to 1.0183 ms (Detailed/default) and 39.5851 to 11.6579 ms (Detailed/grid). At zoom six, all paired screenshots and per-frame stroke sequences were identical. Thirty unchanged visible frames performed zero overlay projections and zero preview renders in each resolution. Detailed grid rotation remains expensive; no interactive-FPS guarantee is made.

The independent raster oracle preserved all 507,881 land/coastal pixels in the exercised geography and changed 210,400 deep-water pixels. At raster coordinate (1267, 482), the strongest raised submerged ridge changed from -0.221805111 to -0.290202975; shaded RGBA changed from (19,44,58,255) to (16,41,55,255). Both resolutions matched. Broad bathymetry remains visible and high-zoom terrain remains a filtered 1536x768 raster.

Every batch independently restored all 112 protected files, permissions, modification/access timestamps and exact file/directory sets. The game is stopped, Steam is idle and the installed probe/active backup are absent. No further probe is required. The reviewed revision remains local and unreleased; earlier local commits remain `d7641ab` and `832124a`, with later work uncommitted.

Private authoritative evidence: `~/ruinarch-runs/trueplanet-campaign-terrain-qualification/qualification-20261105/released-fixture/qualified-matrix.json`, `source-provenance.json`, `measurements.json` and `visual-review.json`. These records, source snapshots and the exact tested binaries are retained for the local handoff; native save evidence is not public release content.

## TruePlanet 0.4.0 release qualification

Native profiling showed that the remaining Detailed-grid bottleneck was line mesh construction/upload, not only globe projection. The focused baseline averaged 129.5349 ms/frame in uGUI rebuild work at zoom one, versus 11.1098 ms in overlay projection. `MapLineGraphic` now shares eight body/fringe vertices instead of twenty duplicated vertices, caches triangle indices and per-batch vertex arrays, and uploads one packed position/color/UV stream directly. Mesh bounds remain native-recalculated and each owned mesh is destroyed with its graphic. Grid coverage, stroke widths/colors, clipping, selection and picking are unchanged.

The exact release DLL is `7a3bb6bf3eccecd665834e7882cdd8ea3495dd68d621adba334a52125d43a9f3`. It passed 13 batches / 32 cases / 1,858 probe assertions, plus 87 actual consumer checks per Small/Large fixture with zero failures or skips. All 62 production/debug C# files match every batch's captured source. Every shared dependency matches the public loader 0.9.1 fixture. The separately compiled publishable source also builds against the released API/framework, excluding unrelated local Performance test changes.

| Detailed grid | Baseline median / p95 (ms) | 0.4.0 median / p95 (ms) | Baseline / 0.4.0 mean uGUI rebuild (ms/frame) |
|---|---:|---:|---:|
| Zoom 1 | 188.6762 / 207.0705 | 38.4478 / 52.6781 | 129.5349 / 17.8376 |
| Zoom 6 | 58.8862 / 62.2062 | 28.8294 / 30.9842 | 31.5318 / 4.9509 |

The focused benchmark uses 120 measured frames per configuration and byte-identical persisted baseline/candidate worlds. Standard grid measured 19.5465 / 21.9992 ms median/p95 at zoom one and 14.5078 / 16.1525 ms at zoom six. Across focused and full/LOD comparisons, 4,800 rotation frames were measured. Detailed grid remains costly; these CPU/native frame observations are not GPU timings or a portable FPS guarantee.

All eight controlled same-scene original/candidate image pairs are pixel-identical. A separate real-assembly smoke expanded the rendered triangles for batch counts 0, 1, 17, 1,799 and 1,800 from a nonzero source offset: baseline, final candidate and public-source build produced identical position/winding/color/UV bytes (`2225ad25f0ef8be349d53a90e08e1fb399e6c16e3629339bbd5b22540571d49b`). Stable batches reuse their vertex arrays. Native checks additionally verify exact colored body area/position through full, shrinking, empty and growing batches, then actual mesh release. Thirty unchanged visible frames perform no line rebuild, projection or preview render.

Across-process captures differed at some text/line intersections, so exact rendering equivalence is evaluated with the controlled same-scene renderer comparison, not by masking pixels or weakening geometry checks. The earlier diagnostic's `Graphic.workerMesh` vertex totals are excluded: that property is static/shared in this Unity build, not each batch's owned mesh. Timing/stroke data remain valid; final diagnostics read actual owned meshes.

Visual review covers all 264 primary screenshots: 192 unique decoded RGBA frames inspected in 27 labelled sheets, with 72 exact duplicates mapped by pixel hash, plus original-resolution native top-bar crops. No new clipping, backface, seam/pole, missing-route or toggle-glow defect was observed in the exercised views. Road checks sampled 20,673 unobscured pixels with zero missing. The ocean oracle preserved all 507,881 exercised land/coastal pixels. Native capitals produced 9 dwellings/4 facilities on Small and 13/5 on Large; the historical 14/5 Large observation above belongs to the preceding run.

All 13 batches independently restored 112 protected files byte-for-byte with modes, modification/access timestamps and exact file/directory sets. Unexpected Unity errors and pathfinder faults were zero. The game is stopped, Steam is idle and the installed helper/active backup are absent.

Authoritative private records: `~/ruinarch-runs/trueplanet-detailed-grid-20261105/release-qualification-b/qualified-release.json`, `visual-review.json`, `source-provenance.json`, and the sibling `performance-provenance.json` / `geometry-smoke.json`. The GitHub update is scoped to TruePlanet, its three consumer-test files and related documentation. Existing 0.3.1 assets, loader 0.9.1, unrelated mods, local Git state and Workshop remain outside the update.
