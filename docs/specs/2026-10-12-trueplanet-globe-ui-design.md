# TruePlanet: native-style globe and floating HUD

Status: implemented, natively qualified and hash-archived on 2026-10-13. `trueplanet-globe-final` passes 23 cases/1,130 native assertions against the actual released loader 0.9.0 dependencies; the real consumer suite separately passes 31 checks per Small/Large fixture without skips. Same source/probe/candidate hashes verified, final screenshots read, measured limits recorded, and owned scaffolds removed only after verified archival. Scoped publication is owner-authorized and must preserve the qualified DLL.

## 1. Goal and boundaries

Make the planet dominate the New Game and Campaign screens. Render an actual rotatable sphere, not a circular crop or perspective distortion of the flat atlas. Surround it with quiet native-style controls that look like Ruinarch, not the existing opaque grey dashboard.

This is a presentation milestone. It supersedes the earlier globe exclusion only for the planet screen. It does not implement the other TruePlanet roadmap milestones.

- Keep the existing deterministic spherical geography, topology, ownership and native map generation.
- Keep world IDs, geographic cell IDs, saved fields, FormatVersion, TopologyVersion and campaign/native snapshot formats unchanged.
- Formats one/two retain province selection and their genuine legacy execution; format three retains tile selection and playable wilderness.
- Keep one live native local map. No residency integration, deferred-travel cutover, offscreen simulation or streaming.
- Keep existing native Play/Switch tile semantics and campaign save-before-load behavior. Do not call Residency.Enable.
- RuinarchRE remains read-only. Implement in TruePlanet, using the stock game and runtime mod loader.
- Inline execution, no subagents. Initially local-only; on 2026-10-13 the owner separately approved solid orange roads plus updating and releasing the qualified TruePlanet package. Version/documentation updates and scoped source/package publication are authorized. No unrelated loader/mod publication, loader reinstall, permanent deployment or history rewrite is implied. The Performance README restoration is a separate completed request.

## 2. Observed foundation

The existing PlanetCanvas creates a 1536x768 equirectangular Texture2D, then displays it through RawImage. MapLines draws projected vector borders/roads with uGUI. PlanetView owns generation, saved-world controls, inspection and Play/Switch tile.

PlanetGeography.Sample(UnitVector) already samples the sphere. TileMesh provides stable centres, neighbours, dual edges and indexed Locate. A throwaway check of the existing compiled model passed all centre picks and seam/pole lookup samples at Standard (10,242 cells) and Detailed (40,962 cells), with twelve pentagons each. That is model evidence, not a native globe rendering/FPS qualification.

Native visual precedent exists: SettingsTab clones native controls under an inactive parent before removing localization, hover actions and callbacks. EditorCanvas owns a separate far-away render root, dedicated camera and RenderTexture-style UI preview. Reuse those patterns, not a second game UI system or a new scene manager. The new globe's disabled/manual-render camera is a planned improvement, not an observed property of the editor.

## 3. Visual direction

### Planet and layout

- Globe is the default view. Flat map remains an explicit overview option, using the same generated world, selected identity and layers.
- One full-screen viewport, without a large opaque map container or permanent full-width control rows.
- Initial globe diameter uses about 85% of the viewport height, leaving top/bottom HUD clearance. The sphere must remain round at every aspect ratio.
- World name and mode in a small floating upper-left header. Back at upper-right.
- A compact world-actions panel beneath the header in New Game: World settings, Re-generate, Save world, Load world. World settings opens the existing name/seed/generation controls in a native-styled drawer, preserving their behavior.
- A floating selection inspector at the right. Keep a compact world summary before selection; expand to real selected tile/province details after selection. Preserve a scrollable details body and an anchored primary action.
- A compact bottom control strip for Globe/Map, Layers and Reset view. Layers opens Terrain/Political, Borders, Roads and Grid controls. No permanently expanded second button row.
- A small status/legend area remains available without swallowing the viewport. Errors retain their actual reason; readiness is not falsely reported during raster/geometry generation.
- In Campaign, hide New Game generation/save-world actions as today. The primary action remains Switch tile. In New Game it remains Play.

### Skin and readability

- Reuse the actual native TMP font/material and suitable native frame/button artwork, colors and interaction states. Do not approximate them with plain grey rectangles or add external fonts.
- Resolve templates from the persistent native settings window and active main-menu/game UI. Clone under an inactive owned parent; clear native actions before activation. Preserve visual behavior without retaining gameplay references.
- Panel centre fill targets roughly 72-82% opacity. Border artwork can remain stronger; text/icons are opaque. Do not reduce an entire CanvasGroup merely to make panel backgrounds translucent.
- Native-style fine frames, dark textured surfaces and restrained gold/purple accents. No modern glass blur, neon bloom or replacement design system.
- Floaty means inset, detached overlays with restrained open/close fades. No continuous bobbing, automatic globe spinning or panels drifting during interaction.
- Owner refinement during native review: remove the noninteractive circles baked into notification-frame corners. Use the genuine ornament-free native dropdown-popup artwork instead, without editing borrowed assets. Centre button glyphs and control groups within balanced padding; keep detached panels around the unobscured globe centre.
- Typography/layout use actual preferred text sizes. Long world/settlement names and full error reasons must wrap/scroll without clipping the primary action.
- Qualify 1280x720, 1568x878, 1920x1080 and 2560x1080. The game is a desktop Unity UI; do not add a web/mobile framework.

## 4. Globe rendering and interaction

### Rendering

- One smooth spherical mesh; native maps do not become 3D terrain. Terrain relief stays in texture/shading, not displaced collision geometry.
- Use a cached equirectangular terrain/political raster shared by the two views. Preserve the current terrain sampler, palette and geography semantics; do not resample or regenerate the world on rotation, layer visibility or view-mode switches.
- Start from the existing 1536x768 raster. Assess actual fullscreen/zoom screenshots before changing its resolution; increased samples must be justified and measured, not cosmetic upscaling presented as extra detail.
- Use a native-supported opaque, backface-culled texture material; the first native preflight must prove shader availability and front/back behavior. No Shader.Find(null) construction or silent flat-view substitute for a missing globe material.
- A dedicated untagged Camera draws only the owned preview layer/root into a pixel-sized RenderTexture. Camera is disabled for automatic rendering and renders when dirty. Do not modify Camera.main, native camera settings or physics.
- Keep the preview root far from gameplay and the existing building editor preview, with narrow near/far clipping. No colliders, lights affecting the game, rigidbodies or native tile objects.
- View resources are owned by the atlas. Release texture/material/mesh/render-root/camera on Back, Play, native menu transition, regeneration replacement and construction failure. No new global static resource cache that outlives the screen.

### Picking and controls

- Drag rotates; wheel zooms; click selects. Dragging beyond the threshold suppresses the subsequent click. Off-globe/missed rays preserve the previous selection.
- Convert pointer coordinates relative to the viewport to a preview-camera ray. Intersect with the unit sphere, undo its rotation, normalize to UnitVector, then apply the exact existing geographic click gate.
- Actual sampled height must be positive. Format three resolves Mesh.Locate; formats one/two resolve Planet.Nearest and require its Land flag. Ocean clicks preserve the previous valid selection. Play/Switch tile continues to apply existing land eligibility.
- Globe and flat map share one selected ID. Switching views cannot reset selection, change the world, alter layers, regenerate pixels or trigger Play/Switch tile.
- Reset restores only the active view's orientation/zoom/pan, not selection or layer choices.
- No automatic rotation. Front/back surface behavior, texture seam and pole orientation must be qualified, not inferred from a successful mesh build.

### Input isolation

- A registered full-screen UI hit surface uses the native UI layer and prevents click-through even over visually empty/transparent portions of the globe view. An atlas-only UIManager.IsMouseOnUI gate also covers the first frame before new Canvas geometry is rendered; outside the atlas it leaves native behavior untouched. Decorative text/line overlays do not intercept selection.
- UI panel presses, drags and wheel scrolling never rotate/zoom/select the planet behind them. Details scrolling stays inside the inspector.
- While the atlas is open in a native world, snapshot and suppress native camera movement and world hotkeys. Do not change game pause/speed policy. Restore exactly the captured values on ordinary close, not an unconditional enable-all.
- Restore only the captured, still-live source objects; never apply stale settings to a destination after Play/Switch tile initiates loading. Release the source input lease before handing off to existing gameplay methods.
- Escape closes an open drawer first, then the atlas. Editing name/seed must not invoke native gameplay hotkeys.

## 5. Geographic overlays

- Reuse exact shared spherical cell edges, ownership classifications, coast clipping and road endpoints. Do not derive political edges from raster contours or create a second ownership convention.
- Owner-approved refinement: roads are continuous warm-orange 1.5-screen-pixel strokes, heavier than fine province borders but slimmer than the previous 3.4-pixel dashed/cased footprint. Keep national double-line borders and gold selection distinct. Both views use the same style; remove obsolete dash-phase bookkeeping without changing saved routes.
- Borders/grid/roads/selection remain screen-readable at different zooms. Project cached spherical segments into existing uGUI-style stroke batches; horizon clipping hides the far side. Use the same native cartographic colors/width hierarchy as the flat view.
- No chord through the sphere, full-screen seam line, far-side border bleed, z-fighting or normalized-longitude seam confusion.
- Selection identifies the geographic cell, not its enclosing political province; retain the existing coast-clipping rules for its outline. Format-one/two selection remains the stored province outline.
- Country names use real geographic anchors on owned land. Settlement/capital markers use saved positions. Hide back-side anchors and near-horizon/viewport-overflow labels.
- Screen-facing labels remain legible rather than being stretched onto the globe texture. Nation labels are shown at whole-world scale; more settlement detail appears as zoom allows. Retain collision avoidance and native font style.
- Rotation/zoom updates bounded visible overlay/label work. Do not create one GameObject per geographic cell, sample geography per frame or rebuild the terrain raster.

## 6. Verification and acceptance

Acceptance requires real native screenshots and interactions, not only geometry tests or component strings.

1. Native preflight identifies and exercises genuine skin templates and a working globe material without invoking copied native actions.
2. Default atlas shows a genuine rotatable sphere with terrain/political layers, coast-correct borders/roads, selection and labels. Flat view remains functional.
3. Same geographic samples resolve the same selectable tile/province in both views, including seams, poles, coastal water and wilderness. Missed/ocean clicks retain selection.
4. New Game name/seed/options/regeneration/save/load/Play work; Campaign inspection and actual Switch tile/native save/reload remain functional.
5. Shared view switches/layers/rotation do not mutate serialized world data or regenerate the cached raster.
6. Native artwork/font, translucent fill, opaque readable text and floating layout are visible on all four required resolutions. Actual screenshots are read.
7. Panels/drawers/scrolling and drag-to-click suppression behave correctly; native camera/world actions receive no leaked input. Previously disabled shortcuts/movement states restore exactly.
8. Formats one/two retain original identities, ownership and dimensions; format three retains tile/campaign semantics. No invented migration or native-map renderer change.
9. Repeated open/close, resize, regeneration, both view modes, Back/Play and menu return release owned rendering/UI resources. Preview camera never replaces Camera.main or leaks into native screenshots.
10. All-thread Unity error logging and Player.log pathfinder audits show zero unexpected errors. Measure frame gaps, render/overlay work and managed/Unity/process memory; report them without a pre-emptive smoothness claim.
11. Protected runners refuse a running user game, existing probe/backup/evidence, and restore complete player files, settings and temporarily staged mod packages byte-identically with mode/mtime/atime and exact directory/file sets. Preserve evidence before removing only owned scaffolds.
12. Update existing README/mod description and these documents after affected native proof. Release the owner-authorized TruePlanet version only after the final same-candidate matrix passes, including published-loader dependency qualification; do not publish unrelated working changes or private evidence.

## 7. First execution checkpoint and exclusions

The first execution checkpoint passed: `trueplanet-globe-preflight-3` identified the initial native notification frame, button_action_default controls, Eczar-Regular SDF font and working Universal Render Pipeline/Lit material. The owner later requested circle-free native popup frames and centered captions; final HUD proof uses the genuine button_minion_action_default popup artwork. The stock build lacks both initially assumed built-in shaders. The qualified opaque Universal2D pass draws a real textured sphere without a physical-lighting claim. Final native evidence supersedes the earlier incremental rendering/HUD/gameplay checkpoints.

Excluded: animated clouds, atmosphere effects, real day/night simulation, displaced mountain meshes, free-floating draggable windows, general mod UI framework, new save formats, generation redesign, travel optimization, residency integration, unrelated publication and history rewriting.

## 8. Final acceptance evidence

Final preflight/surface/globe/HUD/new-game/campaign/lifetime/regression modes
exercise criteria 1–10 end-to-end, including native Play, actual Switch tile and
return, normal native reload, exact source input restoration and forced renderer
failure cleanup. Geographic boundary checks cover seams, poles, coastal water and
wilderness in both views and all four atlas formats. No generation/save-field
cutover accompanied this presentation update.

Every final recorded image was read, including all twenty primary HUD states at
the four specified native client sizes. Solid roads retain the existing saved
routes and pass rendered-pixel continuity checks. Readability remains bounded by
the existing raster at high zoom; Detailed grid projection and instrumented
native generation/oracle stalls are reported, not presented as an FPS guarantee.
See [TruePlanet README](../../TruePlanet/README.md#verification-status) for counts,
timings, memory peaks and legacy durable-state reconstruction limits.

Criterion 11's complete protected restoration was independently verified for
80 files with contents, mode/mtime/atime and exact file/directory sets; game off,
probe/backup absent, Steam idle. Interrupted owner-pause and outer-deadline runs
are preserved separately and excluded from passing totals. The complete handoff
was hash-verified before owned-scaffold removal. Criterion 12 permits only the
approved source/package release, with tag/source and public ZIP/DLL verification;
it does not authorize private evidence, unrelated mods or permanent deployment.

