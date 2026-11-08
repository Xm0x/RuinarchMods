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

## 2026-11-05 screenshot remediation and offscreen missions

Status: implementation in progress. Settlement picking, inspector classification,
sparse native caves and isolated offscreen combat feasibility are verified.
Production mission ownership, return trips and remaining acceptance cases are
not yet qualified.
This section supersedes the earlier revision's gameplay requirements where they
conflict. Requirements remain acceptance gates; only the recorded evidence below
is qualified.

### Installed version evidence

The owner's local `Mods/TruePlanet` remains 0.3.1, DLL SHA-256
`73d070dc3b9794c686f4663a79137c20eb4cc784efb6a1f4f05c7db4b062711e`.
The downloaded Workshop item is 0.4.0, DLL SHA-256
`7a3bb6bf3eccecd665834e7882cdd8ea3495dd68d621adba334a52125d43a9f3`.
The loader reserves local package IDs before loading subscribed Workshop items.
Do not diagnose the supplied 0.3.1 screens as proof that the 0.4.0 setup/copy
changes ran. Conversely, do not dismiss the remaining geography, performance,
mountain, cave, portal and mission requirements as a version problem.

### Atlas, selection and geography

- A settlement belongs to one recorded geographic cell. Its visible marker must
  select that cell, including on coasts; adjacent selections must not appear to
  share that settlement. Labels and markers must remain distinguishable.
- Profile full-globe dragging at low zoom separately from close zoom, Standard
  separately from Detailed, and default layers separately from Grid. Preserve
  coverage and picking; do not obtain a better result merely by hiding content.
- Remove discontinuities caused by switching the nearest tectonic plate pair.
  Blend compatible height/ridge contributions continuously, including underwater.
  Shore depth, ocean colour and shading must not introduce abrupt unrelated bands.
- New geography uses coherent mountain ranges and foothills rather than
  high-frequency disconnected mountain blobs. Do not split a range with artificial
  straight plain corridors to satisfy the earlier 25-percent component limit.
- Mountain interiors can be impassable and unavailable for play, settlement
  placement and expansion. Playable valleys and foothills remain distinct.
- Mountains are predominantly solid. Caves occupy a minority of mountain cells
  as small chambers or connected passages, not the entire mountain footprint.
  Cross-map cave connections share a stable geographic-edge identity and paired
  openings. An opening is left unsealed only when the adjacent map has the
  corresponding connection.
- New terrain and cave algorithms receive new saved generation versions and
  world identities. Existing visited maps and saved geography are not silently
  regenerated. UI, campaign restoration and rendering fixes also cover old worlds.

### Main base, Travel Portals and minions

The owner explicitly rejected a travelling squad as the minion model:

1. Idle minions belong at the main base and retain native on-demand summoning.
2. Native snatching summons the minion and runs the actual native job. After
   taking its target, the minion walks to the local Travel Portal. Passing through
   returns the minion and its cargo to the main base.
3. Kennel raid parties physically leave through the main Portal and arrive
   through the destination Travel Portal.
4. Dispatch is valid when the destination is not the viewed map.
5. The owner selected **Fight while offscreen**: the raid, defenders, casualties
   and return journey must progress without opening that map. Merely recording
   an arrival and waiting for a visit is not an implementation of this requirement.

There is one main Portal and campaign loadout, not a fresh main Portal/loadout
per destination. Later first visits ask the player to place a Travel Portal.
Hide native local-map regeneration during planet tile setup; keep planet
generation controls in the new-world atlas. Do not globally disable regeneration
for unrelated stock games.

Transfer ownership is transactional. A character, captive, carried object or
kennel creature must have exactly one owner across source, in-transit and
destination states. Preserve identity, health, equipment, cargo, faction,
progression and relevant native references. Save/load and interrupted transfers
must neither duplicate nor lose actors, prisoners, charges or items. Native
snatch failure, party defeat, blocked portals and destination recovery remain
real outcomes, not synthetic success.

### Offscreen execution boundary

Native raids bind to the active `GameManager`, map databases, signals and
`SchedulingManager`. The current campaign snapshot system does not simulate
inactive maps. Genuine background missions require isolated execution of those
native systems rather than concurrent mutation of their foreground singletons.

The selected investigation route is a mission-scoped native worker process:

- Prove batch/headless native boot, real map loading, combat, save/reload and
  foreground/worker isolation before depending on this architecture.
- A worker receives a private immutable snapshot and mission input, runs the
  native world, then returns validated snapshot/transfer results. Only the
  foreground campaign owner commits canonical revisions.
- Isolate saves, temporary files, settings, logs, mod configuration and process
  ownership. A worker must never launch another worker or alter Steam
  subscriptions, player settings or unrelated worlds.
- Lease each mission map to exactly one simulator. Visiting an executing map
  requires a save/handoff, not a second copy of that map.
- Pause and progression controls govern mission execution. Non-mission maps do
  not acquire invented populations, combat or passive simulation.
- Bound concurrency and measure memory/CPU use on the actual 16-GB workstation.
  Do not launch an unrestricted full game instance per raid. Feasibility must
  include a viewed map and a fighting offscreen map at the same time.
- Fail visibly and preserve the last committed state if worker launch, native
  loading, combat/save execution or result validation fails. Do not silently
  replace native combat with calculated outcomes or a dormant arrival queue.

### Player-facing text and controls

- Inspector heading is the actual biome, not a geographic number.
- Remove repeated map size, steradian area, culture disclaimers and implementation
  terminology. Temperature and moisture occupy separate lines.
- Report actual native terrain classes separately: grassland, forest, desert,
  snow, hills, mountains and water where present. Do not call every non-mountain
  cell grassland or present overlapping percentages as exclusive coverage.
- Settlement entries have three lines: name, Village/City/Capital, race.
  Classification must reflect the generated settlement, not an invented label.
- The atlas top-bar control uses the native unselected appearance without
  inherited shine. Opening it should reuse prepared data; rebuilding a texture
  on every open is not acceptable cache behavior.
- Conquest progress counts settlements, not an internal tile identifier:
  `Subjugate the Planet: x / y settlements`. The local line reports the actual
  living settler population and settlement type. Preserve native alliance and
  subjugation eligibility instead of confusing population with opposition.
- Loading, travel, selection and error messages are short player instructions.
  File paths, row/stage implementation counters, native setup terminology and
  diagnostic exceptions belong in logs, not routine gameplay messages.
- Restore charge values and cooldown subscriptions correctly across travel.
  Suppressing replenishment notifications without fixing repeated replenishment
  is not a solution.

### Acceptance evidence

Exercise marker selection at coastal cell junctions; full-globe drag profiles;
seams, poles, coasts and ocean shading; new mountain/foothill generation; blocked
mountain interiors; sparse cave coverage and paired cave openings; all inspector
classes and settlement types; main/Travel Portal setup; one-time loadout; repeated
save/load/travel cooldown behavior; settlement conquest transitions; repeated
atlas opens; and every new native deployment/return path.

Offscreen acceptance requires real native combat while another map remains
viewed, persisted casualties and cargo, successful and defeated raids, snatch
return to the main base, entering an active mission map, save/reload mid-mission,
and interruption recovery without duplicate ownership. Capture actual resource
use and independently verify protected saves/settings and owned-process cleanup.

### Remediation evidence checkpoint

- The released 0.4.0 atlas reproduced the settlement-click defect: clicking
  Sahari's visible name selected cell 7309 rather than its recorded cell 7308.
  The corrected candidate passed real EventSystem clicks on text and markers in
  both globe and flat views. All four clicks selected 7308. Both resulting
  surfaces were inspected; all 166 protected files were independently checked
  for identical bytes, mode and modification/access timestamps afterward.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/marker-ownership-before-b/`
  and `marker-ownership-after/`.
- A real-assembly continuity check reproduced a 0.01523 height jump at a nearest
  plate-pair boundary. The version-five field reduced the largest tested jump to
  about 5.63e-9. Geometry-three/four samples across four seeds remained
  byte-identical between released and candidate assemblies: 8,192 samples.
- The new cell-bounded native chart passed 39,285 sample locations across 4,365
  eligible cells with zero samples outside their recorded owner. The previous
  chart put 22,548 sampled locations outside their owner. These are geometry
  checks, not completed new-terrain/cave native qualification.
- The concurrent worker experiment loaded the actual native stock map in a
  Direct3D11 foreground process and a Null-device batch process. The foreground
  advanced from tick 160 to 213 while the worker loaded, advanced to tick 161 and
  wrote a native save. Worker saves/mods used private paths; the foreground
  persistent-path rewrite count was zero, versus four isolated worker callsites.
  Observed managed memory was 775,876,608 bytes foreground and 1,107,197,952 bytes
  worker. All 165 protected files and exact directory sets were restored.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/worker-isolation-b/`.
  This verifies concurrent loading/ticking/saving, not combat or raid recovery.
- Controlled rendering qualification completed for Standard/Detailed, zooms one
  and six, and default/Grid layers. Shared endpoint projections and direct
  scalar vertex construction preserve every measured stroke count in all eight
  cases. Line-mesh construction cost fell about 38 to 40 percent. New line
  batches explicitly remain behind settlement labels and markers; the small
  intentional foreground-order change is not reported as pixel equality.
  The default-layer native frames remain approximately 6.94 ms in this fixture.
  Grid median frame intervals were:

  | Resolution | Zoom | Released 0.4.0 | Candidate |
  |---|---:|---:|---:|
  | Standard | 1 | 19.11 ms | 15.57 ms |
  | Standard | 6 | 14.41 ms | 12.80 ms |
  | Detailed | 1 | 46.24 ms | 38.13 ms |
  | Detailed | 6 | 30.92 ms | 26.00 ms |

  These are measured fixture results, not an FPS guarantee. Detailed grids
  remain expensive. Both complete native surfaces were visually inspected;
  all four batches restored 127 protected files with identical bytes, modes and
  modification/access timestamps, independently verified after execution.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/globe-performance/`.
- Solid mountain eligibility failed before the new traversability gate:
  all 10,242 synthetic dry mountain cells were playable. It now rejects every
  solid mountain cell while preserving all 10,242 lowland cells and treating
  mountains as dry land, not ocean. The compiled permanent consumer regressions
  pass continuity, cell-footprint ownership and mountain/lowland boundaries.
  Actual new-terrain native generation and cave-mask qualification remain open.
- New-identity Small generation reached real native play, advanced, saved and
  reloaded. Its unchanged native cave algorithm made 4,346 of 4,974 mountain
  cells passable (87.37 percent), confirming the oversized-interior defect.
  The Large attempt stopped at selection: a per-pixel mountain rejection blocked
  an otherwise eligible mixed-terrain cell. Selection now uses cell eligibility;
  solid cell interiors remain excluded and native wall objects govern movement.
  The batch independently restored all 127 protected files, including timestamps.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/terrain-native-before-caves/`.
- The first deterministic replacement's compiled consumer produced 297 floor cells
  inside a 5,376-cell mountain island. All floors and both entrances were mutually
  reachable, repeated generation was exact, and other island boundaries stayed
  sealed. Twelve shared geographic edges produced reciprocal exits through native
  mountain terrain; both local chart inverses agreed with their sampled cells.
  Initial qualification also separated geometric charts from playability checks.
  Later passages use deterministic weighted routing
  rather than long axis-first corridors; the same consumer now produces 249
  connected floors. Small native generation/save/reload with that routed mask
  produced 339 passable cells and 4,613 native wall objects in the 4,974-cell
  mountain island, with no passable wall objects. Earlier Small/Large masks also
  passed native saved wall-identity checks and exact canonical boundary openings.
  Native qualification of both playable ends of a shared edge remains separate.
- The charge-notification source is `CampaignCarry.Apply` calling native
  `FinishCooldown` for every skill. Native `PlayerUI.OnSpellCooldownFinished`
  displays “Charge Replenished” for that event. Restoration now detaches the
  native tick callback without completing the cooldown, restores the saved
  counters, and resumes only categorized skills. Actual native restoration was
  repeated three times during a partial cooldown: no charge or completion
  notification was emitted, and one tick subscription remained. Five real ticks
  then earned exactly one charge and one native replenishment notification.
  Native setup also hid and guarded regeneration; real atlas-tab clicks matched
  the native sprite, interaction colors, font and height.
- Portable rules now exclude destination-local faction templates and their Unity
  Sprite references. Native skill/village rules, faction-policy flags and optional
  loadout selection are captured separately from local settlement templates;
  saved destinations receive campaign rules before native component creation.
  Old campaign records without this field retain their readable legacy defaults.
  Initial native qualification exposed the game's per-map cultist-threshold
  randomization; those two campaign rules now retain their values during travel.
  The corrected full Small campaign case passed 72 checks, including nondefault
  first-visit rules, optional loadout state, return, normal reload, and recovery
  from both a missing destination site and an unavailable destination skill.
  All 127 protected files were independently identical afterward.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/terrain-native-rules-b/`.
- A real native raid fought in an isolated Null-device worker while the viewed
  Direct3D11 world continued from tick 160 to 270 without replacement. The worker
  ran a native three-monster party quest from tick 161 to 240, recording 11
  attacker hits, 24 defender hits and one casualty through actual health changes.
  Its native save/reload preserved both surviving raiders' identities and health.
  Managed memory observed was 779,812,864 bytes foreground and 1,198,530,560 bytes
  worker. The independent audit found all 166 protected files unchanged in bytes,
  modes and timestamps. This is real combat feasibility, not production mission
  dispatch, transfer ownership, return trips or interruption recovery.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/worker-combat/`.
- The live native Whittaker oracle passed 700 climate samples, including both
  sides of every native range boundary. Actual Small and Large maps then matched
  every predicted specific biome and every exclusive terrain count: respectively
  19,600 and 47,040 cells, with zero mismatches. Native capital/village inspector
  screenshots were inspected at both sizes: biome heading, separate temperature
  and moisture, exclusive terrain percentages, settlement name/type/race and no
  repeated size or geographic IDs. Native elevation has no hill class; do not
  invent a percentage for relief that the native map does not classify as hills.
  Both complete generation/tick/save/reload cases passed 28 checks with no
  unexpected Unity errors or pathfinder faults. The runner restored 127 protected
  files and exact directory sets. New weighted caves occupied 1,119 of 18,883
  Large mountain cells (5.93%); 17,730 actual wall objects had zero passability
  leaks. Paired-map traversal remains a separate acceptance gate.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/terrain-native-climate-ui-b/`.

- The production worker reached a real native raid checkpoint while the original
  base remained viewed and advanced 90 foreground ticks. The checkpoint and
  requested terminal handoff both recorded exactly 64 worker ticks, unchanged
  mission/run identity and no worker exception. Managed worker memory at
  handoff was 1,071,464,448 bytes. This proves the production worker boundary,
  not completed controller dispatch, combat casualties or return delivery.
  The same integrated run failed its second cave actor-walk check after 143
  passing checks; that failed observation remains retained.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/production-worker-a/native-f/`.
- A focused Small-map case subsequently exercised both playable ends of shared
  geographic edge 771, on cells 130 and 7428. Each actual Troll walked a
  24-cell-connected corridor to its edge opening in 18 native ticks. Both
  maps saved/reloaded with the same native wall identities and sealed unpaired
  mountain boundaries. This focused case passed, but does not erase the prior
  intermittent walk failure or qualify Large/legacy coverage. Movement traces
  and pre-walk campaign snapshots are retained for diagnosis.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/production-worker-a/cave-diagnostic-a/`.
- Both latest runners restored 123 protected files and exact directory sets.
  An independent byte/mode/mtime/atime audit after `native-f` found no difference.
  The runner now distinguishes an actual worker command line from the transient
  inherited `Ruinarch.exe` process name. Worker parent identity uses PID plus
  process start time; offline checks exercised living, exited and mismatched
  parent identities. Native verification of the revised parent guard is pending.

- Qualification now also boots the owner's separate `RuinarchRE/testgame` copy
  directly through standalone Proton using `RuinarchRE/testprefix`, not a Steam
  launch. A headless Gamescope display keeps test windows off the desktop while
  retaining native NVIDIA Direct3D 11 rendering. The private probe selects the
  game's offline Steamworks setting; this is not a production gameplay patch.
  The standalone preflight passed 10 checks with no unexpected Unity errors
  or pathfinder faults, and its rendered screenshot was inspected.
  All 115 protected test files were restored byte-for-byte with their modes,
  timestamps and directory sets. A separate 123-file read-only guard of the
  normal Steam installation found no changes. Campaign qualification remains
  separate from this launch/preflight result.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/isolated-game/standalone-preflight/`.

- Isolated snatch qualification exposed an actual populated-relationship
  serialization bug: the native serializer emits nonempty numeric dictionaries
  as `[{Key, Value}, ...]`, but emits empty dictionaries as `{}`.
  `PortalTransport` now captures either native empty/populated representation
  and projects populated relationships back with integer keys. The actual
  native serializer consumed all 112 populated relationship/opinion entries
  from a 49-actor recorded save with destination-remapped keys.
  Offline checks also covered absent-target filtering and restoration of the
  original map-local keys and values. Qualification separately reproduced
  duplicate actor/cargo classification for a carried captive. The transfer
  producer now removes explicitly selected actor IDs from its cargo set;
  the exclusive-ownership validator remains unchanged. Complete captive return
  is not implied by the relationship-consumer result.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/isolated-game/snatch-boundary-before/`
  and `snatch-cargo-before/` under the same isolated fixture.

- The isolated worker data-link check passes with the installed Proton-GE
  runtime. The same link was not traversable under the tested Proton 10.0
  runtime, so the standalone runner now uses Proton-GE. Worker initialization
  also honors the game's native offline Steamworks setting.
- The first complete inactive-base snatch return exposed a missing action-hub
  dependency: the captive's saved rumor component referenced witnessed assault
  actions that arrival had discarded. Arrival now restores native actions and
  their references before character references, without reconnecting ended
  source jobs. In the Small native run, both living actors returned to the
  original base and its original idle minion remained present.
  The additional history assertion then failed because it queried the native
  volatile action index, which the game clears after loading. It now checks
  the captive's actual rumor objects instead; that corrected consumer and its
  subsequent save/reload are not yet qualified by this observation.
  The failed run restored all 115 protected test files and directory sets.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/isolated-game/snatch-history-after/`.

- The subsequent isolated Small-map return/history case passed all 50 checks
  with no unexpected Unity exceptions or pathfinder faults. A real native
  snatch quest delivered its living target to the Travel Portal; both actors
  then left that map while it remained viewed and arrived at the inactive
  original base. Both witnessed assault identities, their actual actor/target
  references and the base's original idle minion survived return and native
  save/reload. Ended source jobs were not resumed.
  This fixture gives only its test snatcher the native `Indestructible` trait
  to isolate transfer/history from combat balance. It does not qualify
  unmodified snatch combat or raid combat. An earlier unmodified capture
  attempt ended with a genuine snatcher death before transfer.
  Large-map return/history qualification remains separate.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/isolated-game/snatch-history-isolated/Small/`.

- The corresponding isolated Large-map return/history case subsequently passed
  all 50 checks with zero unexpected Unity exceptions or pathfinder faults,
  using the same production DLL. Test saves now use unique names across cases
  after a previous batched run collided with its earlier case's history save.
  A separate Large attempt failed with native FullSerializer's
  `Collection was modified` exception during the source-map save; the
  controller rejected the incomplete archive rather than importing it.
  That failure is retained and has not been explained or fixed. The next
  run installed a non-suppressing dictionary-converter diagnostic and passed;
  this does not erase the intermittent save risk.
  Independent byte/mode/mtime/atime and exact-set audits verified restoration
  of all 115 protected test files, plus no differences in the separate
  123-file read-only Steam-installation guard.
  Evidence: `~/ruinarch-runs/trueplanet-remediation-20261105/isolated-game/snatch-large-dictionary-trace/`
  and the retained failure in `snatch-history-large-unique/`.

The successful snatch-return runs sampled these process resources at roughly
one-second intervals. These are whole qualification runs, including atlas
generation, map loads and saves, not isolated transfer benchmarks:

| Case | Foreground sampled RSS max (MiB) | Worker sampled RSS max (MiB) | Foreground CPU (s) | Worker CPU (s) | Concurrent workers max |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small | 4523.7 | 2859.2 | 680.57 | 70.30 | 1 |
| Large | 9557.7 | 5150.2 | 2576.64 | 238.89 | 1 |

The native-worker path has a substantial memory cost; one worker was observed,
not a lightweight abstract battle simulator. Raw samples and the derived
`isolated-game/snatch-history-resource-metrics.json` remain in private evidence.

- The integrated raid probe now exercises the actual Kennel behavior dropdown,
  rather than calling the selector with an unavailable raid behavior. The
  stock Kennel exposed only snatching; campaign-base Kennels now offer the
  four native raid behaviors, refresh their remote targets and display the
  raid title and selected attacker. The native selector surface was inspected.
  The candidate compiled with all 58 Harmony target checks passing.
- The Small-map case in `isolated-game/controller-native-balance/` passed a
  601-second paused departure with zero native ticks, both original actors
  present and Mana unchanged at its actual 600 cap. After resuming, the real
  actors left through the main Portal and the 100-Mana party cost was charged
  exactly once. Offscreen raid combat, checkpoint adoption and raid return
  are not qualified by that result: the fixture then clicked the native 4x
  toggle a second time and paused progression. The redundant fixture click
  was removed; the continuing qualification retains that paused-worker evidence.

- A subsequent native trace identified an assembly race: the early-arriving
  demon completed its walk and started native idle wandering before its
  follower arrived. Dispatch now installs a scoped assembly behavior through
  the native add/remove lifecycle, then removes it before source serialization
  or on failure. A native save during assembly omits only that transient
  behavior; the original actors remain owned by the source.
- The Small diagnostic run in
  `isolated-game/controller-assembly-native-instance/` passed physical departure,
  exact deployment cost, real offscreen combat while the base remained viewed,
  mid-mission native save/reload and the worker-owned destination visit. The
  native damage observer recorded 7 raider hits and 23 defender hits. The
  test then dereferenced its mission record before awaiting foreground rebind;
  completed raid return is not qualified by this run. This diagnostic used a
  short pause, not another full 601-second pause qualification.

- Further qualification disproved the initial assumption that the missing
  handoff record was only a fixture timing issue. Native population queries
  rebound the controller during travel to a worker snapshot's older mission
  list, before the canonical travel manifest was adopted. The subsequent save
  could then publish an empty mission set. Population accounting now retains
  the foreground owner until travel commits; the handoff regression explicitly
  waits for and requires the same canonical mission on the destination.
- A separate native worker arrival exposed an ended source walk action whose
  target was a source-map floor object. Arrival now retains the dependency
  closure of referenced native action history after detaching source jobs,
  instead of loading every captured current-action node. The offline consumer
  preserved four real witnessed actions unchanged and removed the actual
  source-only walk from the retained failing raid payload. Native raid and
  snatch-history qualification of this later candidate remains in progress.

- The later `isolated-game/controller-complete-roster/Small/` run passed 80
  native checks on candidate SHA256
  `4da4b79e5c1d86596eb4f7800df12205a27146da1eda6468fc9c56433b2fe707`.
  Its production source snapshot matched the source at that checkpoint.
  The actual Kennel selector retained Wrath and all three selected Golems,
  charged 200 Mana once, and dispatched them by walking through the main Portal.
  Real offscreen combat, mid-mission save/reload, canonical destination handoff,
  explicit native retreat, and delivery to the inactive base passed. Wrath's
  actual death remained recorded; all three living Golems returned.
- `isolated-game/controller-automatic-offscreen/Small/` subsequently reached
  automatic mission completion without visiting the destination, but failed
  a fixture assertion requiring a survivor after all four raiders died.
  This is not evidence of living automatic return. The continuing native
  matrix observes death transitions and checks exact casualty preservation,
  native reload, and the full 601-second paused-departure boundary at both sizes.
- The subsequent `final-distinct-roster` matrix passed 78 native checks on
  Small, including automatic offscreen conclusion and final save/reload.
  Both sizes passed the full 601-second pause and native combat/reload.
  Large's final casualty observer omitted a death recorded before worker
  restart; the observer now combines the retained pre-reload evidence with
  subsequent worker evidence. The later `qualified-automatic` matrix passed
  that assertion and native final reload at both sizes: 78 checks each, zero
  unexpected Unity exceptions or pathfinder faults, on exact candidate SHA256
  `c255c53aa9e114c84fc39329c20bef1c3d3f2a64fad6043b9cec75bb6304e5c8`.
- Interrupted assembly qualification exposed two further boundary defects.
  `assembly-reload-root-cause/Small/` recorded a native planner rejection:
  a randomly selected passable Portal tile was not reachable by the actor,
  although another Portal tile was. Dispatch and both foreground/worker
  returns now select an actually reachable Portal tile before queuing the
  native walk. No teleport or pathfinding bypass was added.
- `assembly-reload-reachable/Small/` then reached reload and exposed the
  assembly coroutine reading an old actor's grid after native scene cleanup.
  Uncommitted assembly now ends when its source scene starts loading or is
  replaced, before inspecting old actors or creating a transfer. On that same
  candidate, `final-assembly-resumed` passed 68 checks each on Small and Large,
  including 30 actual native ticks after reload, with zero unexpected Unity
  errors or pathfinder faults. All original actors and Mana remained at source
  with no stale transfer. Independent audits verified all 115 protected test
  files and the unchanged 123-file normal Steam-installation guard.
- `qualified-handoff-return/Small/` passed real combat, destination handoff
  and native retreat but its surviving members died before reaching home.
  It does not prove living return. The continuation protects one actual
  survivor with native Indestructible only after the unmodified two-sided
  combat checkpoint and foreground handoff, isolating transfer correctness
  from combat balance without modifying production behavior.
- The protected-return trace `return-death-observer/Small/` showed a living
  survivor whose native stun cancelled its return walk. The controller's
  one-shot walk never resumed. `PortalTransport` now registers a scoped native
  behavior for dispatch assembly and foreground/worker returns. It re-plans
  cancelled walks only when native movement/job gates allow them and holds
  early arrivals at the Portal. Combat, stun and pathfinding rules are unchanged.
  Process-local behavior names are excluded from native saves; committed
  mission records restore the return intent after loading.
- `return-native-scheduler/Small/` passed 83 native checks, including actual
  two-sided combat, foreground handoff, a living return to the inactive base,
  exclusive actor ownership and final save/reload with survivor identity and
  health retained. The return fixture protects only one survivor after combat,
  so this is transfer evidence, not unmodified combat-balance evidence.
  Candidate SHA256:
  `cc3559e1701d1f1ac904da638d92b3e44651d2b4bf48279c7287bd8385cb9076`.
  The run reported zero unexpected Unity errors or pathfinder faults.
  Independent audits verified all 115 protected test files and no changes to
  the 123-file normal Steam-installation guard. Large return, automatic
  offscreen completion, interrupted assembly, worker recovery, snatch history,
  paired caves and legacy-save qualification of this exact candidate remain
  separate acceptance gates.
- The scheduler candidate also passed 84 Large-map living-return checks in
  `qualified-mobile-scheduler-handoff-return/`. An earlier Large fixture
  protected a 4-HP Wraith that native execution then killed; Indestructible
  does not prevent execution. The revised transfer-only fixture selects the
  healthiest uncaptured survivor, without releasing prisoners or changing
  native combat rules.
- `qualified-mobile-scheduler-automatic/` passed 78 Small-map checks, but
  Large exposed a separate status-transfer defect. Native `Trait.OnRemoveTrait`
  unregisters an expired singleton such as Injured while `TraitManager` keeps
  its instance cached. Arrival remapped an incoming injury to that missing
  database ID. `singleton-expiry-before/Small/` reproduced the failure through
  an actual native add/remove status transition before arrival.
- Portal arrival now restores the cached singleton's registration when absent,
  preserving the native shared instance and incoming status instead of
  discarding the injury or replaying its add effects. The deterministic
  regression and full Small living-return scenario passed 86 checks in
  `singleton-expiry-after/Small/`, with zero unexpected Unity errors or
  pathfinder faults, on candidate SHA256
  `e58454ba16aff2af2529640ecb1bf630e5ab1bfbb2252425fe720123c707a7ff`.
  Independent audits verified 115 protected fixture files after each run.
  The current 123-file normal-installation guard remains unchanged; the
  older retained guard has newer user-state metadata/log differences and
  was not restored over them. Exact-candidate cross-size qualification remains
  in progress.
- Small campaign/cave qualification passed 151 native checks in
  `qualified-singleton-campaign-caves/Small/`. Large's first complete attempt
  exceeded the private runner's overall deadline before paired traversal.
  The extended run reached a real sealing failure on the second cave map.
  `cave-large-border-trace/` identified canonical corner `(279,167)` on
  tile 28139; reaching it also opened unpaired boundary cell `(278,167)`.
  A corner cannot connect to an orthogonal interior path without another edge
  opening. Canonical exit selection now rejects corner candidates on either
  side, rather than sealing a floor cell and disconnecting the passage.
- The captured-world offline regression failed on the prior candidate and
  passed after the corner fix, retaining two valid side exits. Native
  `cave-large-corner-after/TruePlanetLarge/` then passed 58 checks: both
  reciprocal exits, actual actor walks through both corridors, disconnected
  edge sealing, wall identity/passability after save/reload and return to base.
  There were zero unexpected Unity errors or pathfinder faults. Candidate
  SHA256 is
  `962e0e16c6e97fa38a12fd26bd88ff14e35aa1842940f1f8a5cc158039666b98`.
  Independent audits verified exact restoration of all 115 fixture files and
  no changes to the current 123-file installation guard.

### 0.5.0 release qualification

The qualified DLL retains SHA256
`962e0e16c6e97fa38a12fd26bd88ff14e35aa1842940f1f8a5cc158039666b98`.
All 43 production source files match the retained successful terrain, cave,
legacy-save, campaign-travel and worker-recovery runs. Those scenarios were
not repeated for release.

The remaining Large-map automatic raid check passed from a retained native
campaign in `release-cached-automatic-large/`: actual two-sided combat,
mid-mission reload, automatic return without changing the viewed base,
observed casualty preservation and final native save/reload.
`release-focused-storage-corrected/` passed blocked arrival, destroyed Travel
Portal replacement and replacement identity through native save/reload.
Its existing storage/globe consumers passed all 19 checks.

The release checks used the exact qualified DLL and released dependencies.
Independent audits verified restoration of all 115 isolated fixture files,
including metadata and file/directory sets. The current 123-file normal Steam
installation guard is unchanged. Release evidence and package hashes are in
`~/ruinarch-runs/trueplanet-050-release-20261108/`.

Offscreen expeditions still use another native game simulation and substantial
memory. Native Windows expedition execution was not exercised; Windows users
need Developer Mode for worker data links. Publication preserves the qualified
DLL rather than rebuilding it.
