# TruePlanet Globe and Floating HUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task, inline. The owner prohibits subagents. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the opaque dashboard with a large real 3D globe/optional flat map and game-native translucent floating controls, without changing world or native-map semantics.

**Architecture:** One screen-owned PlanetSurface builds the existing geography raster once and supplies both renderers. PlanetGlobe owns an isolated preview camera, smooth spherical mesh, material and RenderTexture; GlobeOverlays projects shared spherical geometry and real label anchors into bounded uGUI batches. PlanetView owns one selection/layer state, native-skinned floating panels and a source-object-scoped input lease.

**Tech Stack:** C#, stock Unity Camera/Mesh/RenderTexture, uGUI/TMP, Harmony reflection for existing native templates, Newtonsoft.Json for unchanged world data; existing RuinarchModLoader build/protected native probe patterns.

**Spec:** `../specs/2026-10-12-trueplanet-globe-ui-design.md`. Visual layout and written plan approved on 2026-10-12. Implementation, final native qualification and hash-verified archive/owned-scaffold cleanup completed on 2026-10-13: `trueplanet-globe-final`, 23 passing cases/1,130 native assertions plus 31 actual consumer checks per Small/Large fixture. Source/candidate/public-loader hashes agree; final screenshot and measured-limit review completed. Scoped release publication is owner-authorized and must use the qualified DLL without rebuilding it.

## Global Constraints

- Keep the existing deterministic spherical geography, topology, ownership and native map generation.
- Keep world IDs, geographic cell IDs, saved fields, FormatVersion, TopologyVersion and campaign/native snapshot formats unchanged.
- Formats one/two retain province selection and their genuine legacy execution; format three retains tile selection and playable wilderness.
- Keep one live native local map. No residency integration, deferred-travel cutover, offscreen simulation or streaming.
- Keep existing native Play/Switch tile semantics and campaign save-before-load behavior. Do not call Residency.Enable.
- RuinarchRE remains read-only. Implement in TruePlanet, using the stock game and runtime mod loader.
- Inline execution, no subagents. Initially local-only; on 2026-10-13 the owner separately approved continuous 1.5-pixel orange roads and updating/releasing the qualified TruePlanet package. Scoped version/docs/source/package publication is authorized only after final native proof against published loader 0.9.0 binaries. No unrelated loader/mod publication, loader reinstall, permanent deployment or history rewrite is implied.
- No external fonts, generic glass-dashboard styling, new UI framework or imported art substituted for native styling.
- A missing native template/material is a failed prerequisite, not permission to deliver a grey-box skin, circular cropped map or silently selected flat-only screen.
- Preserve current pause/speed policy; only scope camera/world-hotkey input while the atlas is open.
- Public documents use anonymous evidence run IDs or `$HOME` paths, never personal home-directory paths or unsolicited workstation profiles. The owner separately authorized restoring the historical benchmark machine table in Performance README only.
- Do not execute native qualification against a running user game. Refuse before touching files and wait for an idle game/Steam launch ledger.
- No per-cell GameObjects, per-frame geography sampling, per-drag raster rebuilding or unnecessary allocations in unchanged frames. Reuse buffers and dirty-state rendering.

---

## File responsibilities and clean cutover

| File | Change and responsibility |
| --- | --- |
| New `TruePlanet/PlanetSurface.cs` | Screen-owned raster build/cancellation, geography, palette upload, pick policy and shared texture; no UI layout or saved fields |
| `TruePlanet/PlanetCanvas.cs` | Flat renderer/input/labels consuming PlanetSurface; remove the old duplicate raster/geography/texture ownership |
| `TruePlanet/MapGeometry.cs` | One edge classification/coast-clipping traversal with flat and spherical outputs; legacy edges stay genuine |
| `TruePlanet/MapLines.cs` | Retain flat stroke batching; share existing width/color style data with GlobeOverlays, without a second border policy |
| New `TruePlanet/GlobeProjection.cs` | Coordinate conversion, robust sphere-ray intersection and smooth render-mesh construction; no current-world singletons |
| New `TruePlanet/PlanetGlobe.cs` | Camera/root/mesh/material/RenderTexture ownership, drag/zoom/pick and dirty render scheduling |
| New `TruePlanet/GlobeOverlays.cs` | Horizon clipping, visible spherical strokes, cached native-font labels and screen collision layout |
| New `TruePlanet/GlobeSkin.cs` | Resolve/copy native UI visuals into inactive owned templates; clear old actions/localization before use |
| New `TruePlanet/AtlasInputScope.cs` | Capture/suppress/restore only source camera movement and world-hotkey state; no pause/speed mutation |
| `TruePlanet/PlanetView.cs` | Full viewport, one selection/layer state, mode control, floating HUD/drawers, existing generation/save/load/Play routing |
| `TruePlanet/TruePlanet.cs` | Install the atlas-only UIManager.IsMouseOnUI postfix; no changed generation/save/lifecycle installation |
| `RuinarchDebug/TruePlanetSuite.cs` | Add only consumer-visible sphere-ray boundary checks; retain generator/identity/storage checks |
| `TruePlanet/README.md`, `TruePlanet/mod.json`, this spec/plan | After native proof, describe actual globe/HUD/solid roads and measured limits; prepare owner-authorized TruePlanet 0.3.0 release |
| Development-only `RuinarchModLoader/build/trueplanet-globe-qualify/` | Probe, protected runner, fixture copies, staged candidate assemblies and pure smoke; archive then remove owned workspace |

No changes to PlanetModel, PlanetGenerator, PlanetGeography, TileMesh, TileLand, NationalProvinces, TileGame, TileTravel, TileStore, CampaignCarry or Ruinarch.ModContent production logic are planned. If a renderer change unexpectedly needs a world/save/lifecycle change, stop and present that scope change; do not bury it in the UI work.

Before changing public Unity component symbols or signatures, query available C# LSP references. If no C# server is available, record that and inspect actual callsites/probe consumers. Do not leave aliases or an unused second raster builder after the cutover.

## Development harness and command contract

All commands below run from the sibling **RuinarchModLoader** checkout. The new development workspace is `build/trueplanet-globe-qualify`, with `TruePlanetGlobeProbe/`, `stage/`, `run.py` and `smoke.py`. The probe references real candidate types through reflection; do not compile a copied TruePlanet model into the probe.

Build candidates without installing them:

```bash
./tools/build-mod.sh ../RuinarchMods/TruePlanet build/trueplanet-globe-qualify/stage
./tools/build-mod.sh build/trueplanet-globe-qualify/TruePlanetGlobeProbe build/trueplanet-globe-qualify/stage
```

`build-mod.sh` does not reference arbitrary sibling mod DLLs. The probe therefore locates `TruePlanet` from loaded assemblies and invokes its actual internal methods/components with reflection. Stage the real TruePlanet package; do not call `new TruePlanetMod().OnLoad(probeContext)` a second time or register settings under the probe ID.

Probe manifest:

```json
{
  "id": "qualification.trueplanet-globe",
  "name": "TruePlanet globe qualification",
  "version": "0.0.0",
  "author": "RuinarchRE",
  "loader": "RuinarchModLoader",
  "loaderApi": 2,
  "type": "code",
  "entryAssembly": "TruePlanetGlobeProbe.dll",
  "entryType": "TruePlanetGlobeProbe.Entry"
}
```

Entry only stores `context.ModDirectory` and attaches a persistent probe host. Build TMP/graphics from Start after native initialization, not inside the module initializer's Awake/OnLoad:

```csharp
public void OnLoad(ModContext context)
{
    Probe.Root = context.ModDirectory;
    var host = new GameObject("TruePlanet globe qualification");
    UnityEngine.Object.DontDestroyOnLoad(host);
    host.AddComponent<Probe>();
}
```

Define the probe methods `Require(bool,string)`, `Wait(Func<bool>,double,string)`, `Invoke(object,string,params object[])`, `ReadField(object,string)`, `Screenshot(string)` and `Guard(IEnumerator)` in the owned probe. Require throws/logs one criterion; Wait fails on unexpected errors or its named deadline; Guard drains nested enumerators and publishes one terminal RESULT. Screenshot waits for end-of-frame, writes actual ScreenCapture PNG and releases the Texture2D. Never count RESULT as an assertion.

`smoke.py` defines `--mode surface`, `projection` and `all`: surface exercises actual candidate geography, serialized identity and flat/spherical clipping math; projection exercises the actual sphere-ray helper and coordinate round trips; all combines them. It loads the real candidate in a throwaway C# host. Do not construct MonoBehaviour, Texture2D, Mesh or native scene objects outside Unity; all resource/mesh/material tests run in the native probe. Resolve individual managed dependencies without placing the game's mscorlib on host Mono's search path (that corlib is incompatible with the host runtime).

Use the archived deferred-milestone driver's verified snapshot/restore/Steam-ledger functions, not the older atlas runner's incomplete atime/directory cleanup. Source: `$HOME/ruinarch-runs/residency-deferred-milestone-matrix-green-1/build-scaffolds/run.py`. Retain the restoration functions; remove crash modes/DepartureProbe-specific entry logic and implement only the modes defined below. Resolve GAME/DATA from `$HOME`, with optional explicit environment overrides; no hard-coded account name.

Protect the complete native SAVES tree (including autosaves/temp/player data), DATA/Temp, Mods/settings, Settings.ini, InstalledMods.xml, modloader.config.json, mods.log, temporary framework/Harmony files if staged, and the complete existing TruePlanet/RuinarchDebug packages including DLL, manifest, art/bundles and absent/existing directory state. Capture metadata before reading bytes; hash verification must restore atime afterward. Do not protect only a hand-picked native save. A stopped game is not sufficient until Steam's AppID 909320 PID ledger is idle.

The runner must fail its own exit status on a failed/incomplete RESULT, all-thread error, pathfinder fault or restoration mismatch. Graceful SIGTERM runs final restoration; stop only the owned launched process. Persist source/stage/baseline/results/screenshots/Player.log/mods.log before removing installed probes. Retain backups on failed restoration, never delete the only recovery copy.

Use these real archived inputs, copied into the owned probe directory, never modified in place:

- Format one: `$HOME/ruinarch-runs/trueplanet-tile-capacity-4/build-scaffolds/tile-atlas-qualification/AtlasTileProbe/legacy-one.json`.
- Format two: the adjacent `legacy-world.json`.
- Ordinary Small native control: `$HOME/ruinarch-runs/native-loading-production-3/Small/fixture.zip`.
- Ordinary TruePlanetLarge native control: `$HOME/ruinarch-runs/trueplanet-loading-profile-5/TruePlanetLarge/fixture.zip`.
- Native campaign/Play flows: reuse the actual first-visit/campaign mechanics from `$HOME/ruinarch-runs/trueplanet-tile-campaign-8/probe-source/`, but load the real candidate package instead of its old copied Model directory.

The driver selects one MODE and CASES per protected batch, stages candidate TruePlanet and the probe, enables only required packages and restores the previous mod configuration. Do not install every mod/probe simultaneously. Required modes: `preflight`, `surface`, `globe`, `hud`, `new-game`, `campaign`, `lifetime`, `regression`. Cases are defined per task; unknown modes/cases fail before launch.

---

## Task 1: Prove native skin/material prerequisites and establish the protected probe

**Files:** Create only the owned development probe/runner/smoke workspace. No production renderer/UI edits yet.

**Consumes:** Native settings/main-menu/game UI, installed Unity shaders, archived protected-driver functions and fixture copies.

**Produces:** Native `preflight.json` with actual template paths/sprite/font identities and qualified material choice, rendered `preflight.png`, full restoration report, and a controlled pre-implementation missing-globe RESULT.

- [x] **Step 1: Add protected runner and preflight mode.** Refuse running native/Steam launch state, existing probe/backup/evidence and missing candidate/fixtures before snapshot. Keep a logger-on Update and Application.logMessageReceivedThreaded observer throughout the probe, including native loading. Match no broad expected-error exemptions.

  The main menu readiness predicate uses actual existing managers:

  ```csharp
  yield return Wait(() => MainMenuUI.Instance != null
      && Settings.SettingsManager.Instance != null
      && ExternalFileManager.Instance != null && ExternalFileManager.Instance.hasInitialized
      && LevelLoaderManager.Instance != null
      && !LevelLoaderManager.Instance.IsLoadingScreenActive(), 180, "native menu ready");
  ```

  Preflight enumerates native frame Images under `SettingsManager.Instance.settingsGO`, preferring a real sliced sprite with nonzero border, and the known native button `Gameplay Options/Reset Tutorials Button`. Record exact transform path, sprite name/border, Image.type, Selectable transition, TMP font/material and persistent callbacks. Use active main-menu/game font sources as PlanetView currently does. Do not activate the original settings window or trigger Reset Tutorials.

- [x] **Step 2: Render and exercise skin/material preflight in the real game.** Copy the candidate visual control under an inactive owned template parent, mute/replace callbacks, remove `CustomLocalizeStringEvent` and `HoverHandler`, detach Toggle groups and remove unrelated action-bearing scripts before activation. Show one original-art frame with translucent centre and opaque caption, plus an actual isolated textured test sphere. Prove correct opaque/front-surface rendering and record the chosen route. Do not construct a Material from a missing/unsupported Shader.

  ```csharp
  Shader shader = Shader.Find("Universal Render Pipeline/Lit");
  Require(shader != null && shader.isSupported,
      "native renderer has its qualified opaque globe material");
  ```

  Native prerequisite evidence: `trueplanet-globe-preflight-1` establishes both originally assumed shaders are unavailable. Shipped asset inspection identified URP/Lit and its opaque Universal2D pass. `trueplanet-globe-preflight-3` qualifies that shader with private opaque blend/depth/cull settings and the real `notification_container` frame, `button_action_default` control and Eczar-Regular SDF font. Ten native assertions pass with zero all-thread errors; actual screenshots inspected. Near/far colours swap on a real sphere rotation; Camera.main is unchanged. All 79 protected files and complete directory sets restore exactly with metadata. The first largest-image choice was a generic White_square; it is excluded rather than accepted as native decorative art.

  ```bash
  env MODE=preflight CASES=menu EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-preflight-1" python3 build/trueplanet-globe-qualify/run.py
  ```

  Read the screenshot. Verify copied button invocation cannot change native tutorial/settings/player data or invoke original callbacks. Output actual template/material names, not assumed names. Keep the world model untouched.

- [x] **Step 3: Add the real globe-mode red scenario.** Locate the candidate assembly from AppDomain and require the default opened atlas to own PlanetGlobe rather than only PlanetCanvas. Before implementation, expect a controlled `missing TruePlanet.PlanetGlobe` failure, not a Unity exception or loader failure.

  ```csharp
  Type worldType = AppDomain.CurrentDomain.GetAssemblies()
      .Select(a => a.GetType("TruePlanet.Planet", false)).First(t => t != null);
  Type globeType = worldType.Assembly.GetType("TruePlanet.PlanetGlobe", false);
  Require(globeType != null, "missing TruePlanet.PlanetGlobe");
  ```

  ```bash
  env MODE=globe CASES=Standard EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-red-1" python3 build/trueplanet-globe-qualify/run.py
  ```

  Preserve red evidence and protection results. Do not rerun the reported failure merely to confirm it.

**Checkpoint:** native art/material behavior and protected restoration proven. `trueplanet-globe-red-1` records the controlled missing-PlanetGlobe failure with zero Unity errors and exact restoration. Preflight is not a completed globe implementation. C# LSP is unavailable (only bashls configured); actual TruePlanet/Debug callsites inspected before component API cutover.

## Task 2: Share the actual surface/pick policy and geographic geometry

**Files:** Create PlanetSurface.cs; modify PlanetCanvas.cs, PlanetView.cs (surface ownership hookup only), MapGeometry.cs and MapLines.cs (shared style data only). Existing flat screen remains usable during this task.

**Consumes:** Existing PlanetCanvas.Build/TerrainColor/palette/labels, PlanetGeography, MapGeometry edge/coast logic and current pick semantics.

**Produces:** The shared surface and spherical-segment contracts below. No model DTO/save-field changes.

```csharp
internal sealed class PlanetSurface : MonoBehaviour
{
    internal const int Width = 1536, Height = 768;
    internal Planet Planet { get; private set; }
    internal PlanetGeography Geography { get; private set; }
    internal Texture2D Texture { get; private set; }
    internal bool Ready { get; private set; }
    internal int[] ProvinceIds { get; private set; }
    internal float[] Heights { get; private set; }
    internal Color32[] NationColors { get; private set; }
    internal Action<float> Progress;
    internal event Action Changed;
    internal event Action<Exception> Failed;
    internal void Show(Planet planet);
    internal void SetPolitical(bool political);
    internal bool TryPick(UnitVector direction, out int id);
}
internal struct SphericalSegment
{
    internal UnitVector A, B;
    internal int Left, Right;
    internal byte Kind; // 0 grid, 1 province, 2 nation, 3 road.
}
// Cut over PlanetCanvas to this same small borrowed-surface contract:
// Configure(PlanetSurface surface, TMP_FontAsset font, Material fontMaterial, Action<int> picked)
// Refresh(int selected, bool borders, bool roads, bool grid)
// SetVisible(bool visible); ResetView()
// On MapGeometry, retain the existing Build/CellOutline flat entry points:
internal static List<SphericalSegment> BuildSpherical(Planet planet, PlanetGeography geography);
internal static void SphericalOutline(Planet planet, PlanetGeography geography,
    int selected, List<SphericalSegment> output);
```

- [x] **Step 1: Add consumer smoke before extraction.** Record canonical JsonConvert world bytes and an independent raster digest on an actual generated Standard/Detailed world and both genuine legacy fixtures. Capture flat layer/picking/line screenshots as baseline. Assert ocean/miss preserves selection and successful picks resolve the actual queried direction, not a mocked ID. Preserve checks against the independent sampler and existing ownership/coast geometry.

  ```csharp
  string before = JsonConvert.SerializeObject(world);
  // Exercise real view controls and actual pointer/geographic queries here.
  Require(JsonConvert.SerializeObject(world) == before,
      "view/layer/pointer operations leave saved world fields unchanged");
  ```

  `surface` initially fails on the missing new shared owner. Baseline flat screenshots are controls, not proof of the new owner.

- [x] **Step 2: Move, do not duplicate, the raster build.** Transfer geography, ID/height/palette buffers, texture upload and coroutine cancellation from PlanetCanvas into PlanetSurface. Preserve sample centres, seed fields, palette formula and row-yield cadence. Show invalidates readiness immediately, cancels its previous build, reports progress zero and publishes completion only after full raster upload. Failure stops input/readiness and reports the real exception; close/regeneration stops owned work. PlanetSurface owns/destroys its Texture2D; renderers borrow it. SetPolitical uploads only when the palette changes.

  Keep longitude repeating and latitude clamping explicit for the shared equirectangular texture; flat UVs remain [0,1]. Configure bilinear filtering. Do not generate mipmaps or increase raster dimensions speculatively. PlanetCanvas retains its flat label/zoom/pan behavior and binds to Surface.Changed, unsubscribing on destruction.

- [x] **Step 3: Centralize the exact existing click gate.** Both renderers query this policy after their own projection; central PlanetView stores the selected ID.

  ```csharp
  internal bool TryPick(UnitVector direction, out int id)
  {
      id = -1;
      if (!Ready || Geography.Sample(direction).Height <= 0) return false;
      if (Planet.FormatVersion == 3) id = Planet.Mesh.Locate(direction);
      else
      {
          int province = Planet.Nearest(direction);
          if (!Planet.Provinces[province].Land) return false;
          id = province;
      }
      return true;
  }
  ```

  Preserve existing separate Play eligibility from TileLand. A click with positive sampled height does not manufacture a playable local world or change a legacy province ID.

- [x] **Step 4: Add spherical output to one geometry traversal.** Refactor existing edge enumeration/classification and coast subdivision/clipping once. Emit normalized clipped A/B directions before flat longitude seam splitting. Flat output retains seam splits; spherical output has no artificial seam edge. Preserve exact dual edges, versioned legacy edge reconstruction, road endpoint anchors and sentinel-safe nation lookup. The owner-approved solid-road cutover removes obsolete projected/angular dash-distance fields and calculations. No second border classifier or raster-contour implementation.

  ```csharp
  int Nation(int province) => province < 0 ? -1 : planet.Provinces[province].Nation;
  byte kind = left == right ? (byte)0
      : Nation(left) == Nation(right) ? (byte)1 : (byte)2;
  double angularLength = Math.Acos(Math.Max(-1, Math.Min(1, UnitVector.Dot(a, b))));
  ```

  Keep the existing normalized-arc subdivision and 16-step coast search. Add clipped unit directions alongside current projected endpoints rather than converting seam-split flat pixels back into approximate geography. Formats one/two outline their stored province; format three outlines the actual selected cell with existing coast clipping. Share line color/width definitions from MapLines; keep uGUI batches below its 65k vertex limit.

- [x] **Step 5: Build and exercise the changed flat path.** Run `smoke.py --mode surface` against the actual candidate, then protected `surface` Standard/Detailed/legacyOne/legacyTwo. Read actual flat screenshots. Require unchanged world bytes, topology/selection/ownership/coast semantics, correct shared palette upload and no unexpected native errors. Remove obsolete PlanetCanvas raster fields/build methods and stale texture cleanup; do not retain aliases.

  ```bash
  python3 build/trueplanet-globe-qualify/smoke.py --mode surface
  env MODE=surface CASES=Standard,Detailed,legacyOne,legacyTwo EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-surface-green-1" python3 build/trueplanet-globe-qualify/run.py
  ```

**Checkpoint passed:** `trueplanet-globe-flat-baseline-1` records all four original flat controls. `trueplanet-globe-surface-green-1` passes 56 native assertions: saved world hash, both palette hashes, selected ID and format exactly match those independent baselines. Raster heights/ownership match independent geography samples. Four changed-path screenshots inspected; zero all-thread/pathfinder errors; all 79 protected files, metadata and directory sets restore exactly. Pure real-assembly surface smoke passes both current resolutions. Raster build/texture ownership and pick gate have been removed from PlanetCanvas, not duplicated.

## Task 3: Implement the genuine globe, projection and readable overlays

**Files:** Create GlobeProjection.cs, PlanetGlobe.cs and GlobeOverlays.cs. Modify PlanetView.cs to create/route a temporary development globe viewport alongside the actual flat viewport; full floating HUD comes in Task 4. Add consumer ray-boundary checks in existing TruePlanetSuite.cs.

**Consumes:** Qualified native material choice, PlanetSurface and SphericalSegment/MapGeometry contracts.

**Produces:** Actual camera-rendered sphere and overlay view with these exact entry points:

```csharp
internal static class GlobeProjection
{
    internal static Vector3 ToUnity(UnitVector point);
    internal static Mesh BuildMesh(int latitudeSteps, int longitudeSteps);
    internal static bool TryDirection(Ray ray, Vector3 centre, Quaternion rotation,
        float radius, out UnitVector direction);
}
internal sealed class PlanetGlobe : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    internal void Configure(PlanetSurface surface, TMP_FontAsset font,
        Material fontMaterial, Action<int> picked);
    internal void Refresh(int selected, bool borders, bool roads, bool grid);
    internal void SetVisible(bool visible);
    internal void ResetView();
}
internal sealed class GlobeOverlays : MonoBehaviour
{
    internal void Configure(PlanetSurface surface, Camera camera, Transform sphere,
        RectTransform viewport, TMP_FontAsset font, Material fontMaterial);
    internal void Refresh(int selected, bool borders, bool roads, bool grid);
    internal void Project(float zoom);
}
```

- [x] **Step 1: Write sphere-ray boundary checks before the helper.** Keep permanent checks only for consumer-visible miss/behind/inside/tangent/rotation behavior; use throwaway native smoke for mesh wiring/performance. Use actual Unity Ray/Quaternion and actual candidate method, not mirrored production code. In the debug suite, find the internal helper through the real assembly as existing TruePlanetSuite does; a qualification run must not accept Skip as a passing globe check.

  Expected cases for radius one and identity rotation: origin (0,0,-3), forward ray hits (0,0,-1); origin (2,0,-3), forward misses; origin (0,0,-3), backward has no forward hit; origin zero, right exits at (1,0,0); tangent origin (1,0,-3), forward yields (1,0,0). Repeat after a nonidentity rotation and translated/scaled sphere, comparing to its inverse-rotated unit hit. A zero direction is a miss, not NaN selection.

  ```csharp
  Require(TryActualDirection(new Ray(new Vector3(0, 0, -3), Vector3.forward),
      Vector3.zero, Quaternion.identity, 1, out object hit), "front ray hits globe");
  Require(Math.Abs(ReadCoordinate(hit, "Z") + 1) < 1e-6,
      "front sphere ray selects the near surface, not the far hemisphere");
  Require(!TryActualDirection(new Ray(new Vector3(2, 0, -3), Vector3.forward),
      Vector3.zero, Quaternion.identity, 1, out hit), "off-globe ray preserves selection");
  ```

  Define `TryActualDirection(Ray,Vector3,Quaternion,float,out object)` in the probe/debug checks by invoking GlobeProjection.TryDirection with a reused reflection argument array and reading its out value. `ReadCoordinate(object,string)` reads the actual UnitVector fields. The snippet above uses the throwaway probe's Require. Permanent AutoTest checks use its existing Check callback, not a nonexistent Require or Skip:

  ```csharp
  Check("globe ray chooses the near surface", () =>
  {
      bool hit = TryActualDirection(new Ray(new Vector3(0, 0, -3), Vector3.forward),
          Vector3.zero, Quaternion.identity, 1, out object point);
      return (hit && Math.Abs(ReadCoordinate(point, "Z") + 1) < 1e-6,
          "near hemisphere, not far exit");
  });
  ```

  Before implementation, require a controlled missing-helper failure. Implement the remaining listed boundary cases with the same actual-method helper.

- [x] **Step 2: Implement robust sphere intersection and UV mesh.** Convert the ray to sphere-local unit coordinates, solve the quadratic using doubles and choose the nearest nonnegative root. Reject a negative discriminant, zero direction or a ray whose intersections are both behind its origin. Normalize the hit before sampling geography.

  ```csharp
  direction = default;
  UnitVector origin = InverseRotate(new UnitVector((double)ray.origin.x - centre.x,
      (double)ray.origin.y - centre.y, (double)ray.origin.z - centre.z) * (1.0 / radius), rotation);
  UnitVector delta = InverseRotate(new UnitVector(ray.direction.x, ray.direction.y,
      ray.direction.z) * (1.0 / radius), rotation);
  double a = UnitVector.Dot(delta, delta);
  double b = UnitVector.Dot(origin, delta);
  double c = UnitVector.Dot(origin, origin) - 1;
  double discriminant = b * b - a * c;
  if (a <= 0 || discriminant < 0) return false;
  double root = Math.Sqrt(discriminant), t = (-b - root) / a;
  if (t < 0) t = (-b + root) / a;
  if (t < 0) return false;
  direction = (origin + delta * t).Normalized();
  return true;
  ```

  `InverseRotate` is managed math for the caller's unit quaternion, so pure smoke does not invoke Unity's native Quaternion.Inverse_Injected. Keep the renderer's rotation normalized:

  ```csharp
  private static UnitVector InverseRotate(UnitVector point, Quaternion rotation)
  {
      UnitVector q = new UnitVector(-rotation.x, -rotation.y, -rotation.z);
      return point + UnitVector.Cross(q,
          UnitVector.Cross(q, point) + point * rotation.w) * 2;
  }
  ```

  Actual installed CoreModule disassembly shows Quaternion.Inverse delegates to a native injected call. Do not run that helper outside Unity or mock it into a passing projection check. Ray coordinate fields and the double vector math above remain managed; construct unit test rotations from literal normalized components/System.Math.

  The caller supplies positive radius. Build a smooth unit UV sphere with 64 latitude and 128 longitude steps, duplicating the longitude seam UVs. Positions use the existing UnitVector.FromMap orientation: x=cos(latitude)*cos(longitude), y=sin(latitude), z=cos(latitude)*sin(longitude), u=(longitude+pi)/(2*pi), v=(latitude+pi/2)/pi. Normals equal unit positions. Use outward triangle winding; skip the degenerate first/last pole triangles. This is a render mesh, not a replacement TileMesh or cell identity source.

- [x] **Step 3: Own an isolated render root/camera/material/target.** Use a screen-owned preview root at (30000,-30000,30000), distinct from the editor's (10000,10000,0), layer 31 only. Give the smooth sphere world radius 128 so far-away float precision is small relative to screen pixels. An untagged orthographic camera at local (0,0,-3.5*radius) looks at it, has near=.1*radius/far=8*radius, and starts with orthographic size=radius/.85. Do not alter Camera.main or native camera masks. No colliders/physics.

  RenderTexture follows actual viewport pixel dimensions, with each dimension capped at 2048, ARGB32 and 24-bit depth. Preserve aspect if a cap applies. Bilinear display; release the prior target before replacement, detach references before destruction. Validate Create success and the actual material from Task 1; no unqualified asset fallback.

  ```csharp
  previewCamera.enabled = false;
  previewCamera.tag = "Untagged";
  previewCamera.orthographic = true;
  previewCamera.cullingMask = 1 << 31;
  previewCamera.clearFlags = CameraClearFlags.SolidColor;
  previewCamera.backgroundColor = Color.clear;
  previewCamera.targetTexture = target;
  // On a dirty, visible, surface-ready frame only:
  previewCamera.Render();
  ```

  Use the Task 1-qualified opaque `Universal Render Pipeline/Lit` material and existing terrain relief palette. The game's Universal2D pass supplies textured, unlit spherical projection; no physical-lighting claim. Do not mutate global lights or RenderSettings or add an unqualified built-in shader fallback.

- [x] **Step 4: Implement viewport input and dirty scheduling.** Convert screen coordinates with RectTransformUtility to normalized viewport coordinates, then use the owned Camera.ViewportPointToRay. Call TryDirection followed by Surface.TryPick. Mouse drag rotates with bounded sensitivity and a nonzero screen-pixel threshold; on release suppress only the drag's associated click. Wheel changes orthographic size within a defined 1x-6x zoom range. Reset affects active-view pose, not shared selection/layers. No automatic spin.

  Mark render/overlays dirty on actual rotation/zoom, target resize, ready/palette change, selection/layer change or visible-mode reactivation. Unchanged frames do not rebuild geometry, sample terrain or call Render. Closing while building cancels only owned work. OnDestroy and construction catch/finally dispose owned target/material/mesh/root/light and unsubscribe shared events; never destroy borrowed font/sprite/texture assets.

- [x] **Step 5: Project real overlays, including horizon clipping.** Cache MapGeometry.BuildSpherical once per world and the selected outline once per selected ID. Transform unit endpoints by current sphere rotation, clip at the camera-facing hemisphere before projecting via WorldToViewportPoint, then map into viewport-local UI coordinates. Opposite-facing endpoints are never drawn through the planet. For a straddling short normalized arc, find its horizon intersection with the same normalized interpolation, using a 16-step sign bisection. No flat longitude seam split is introduced into globe geometry.

  Reuse MapLineGraphic and shared line-style data. Reuse stroke/batch buffers; update visible batches only while dirty. The approved refinement uses continuous 1.5-screen-pixel warm-orange roads in both views, with no dark casing or dash phase. Grid is off by default. Selection is distinct from political borders and supports genuine legacy provinces.

  Labels are cached native-font UI objects, not texture text or per-cell objects. Use real capital/settlement directions; nation label anchors resolve to owned land. Hide back-side/near-horizon anchors before screen layout. Show nation names first, add settlements/capitals as zoom allows, check screen rectangles for overlap/overflow and keep text opaque/screen-facing. In flat view retain its existing territory fitting, not a new globe-only anchor policy.

- [x] **Step 6: Build and prove the real changed path.** Run pure ray/coordinate/geometry smoke; actual Mesh creation, winding, material and render targets are checked inside Unity. Then run native `globe` Standard/Detailed/legacyOne/legacyTwo. Actually rotate, zoom, reset, toggle terrain/political/grid/borders/roads and click visible known land/coastal water/wilderness. Assert same queried directions resolve the same identities in the flat view; use well-defined interior points and independently checked seam/pole samples, not an allowance for mismatched clicks. Read whole-world and zoomed screenshots, including limb/texture seam and labels.

  ```bash
  python3 build/trueplanet-globe-qualify/smoke.py --mode projection
  env MODE=globe CASES=Standard,Detailed,legacyOne,legacyTwo EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-render-green-1" python3 build/trueplanet-globe-qualify/run.py
  ```

**Checkpoint passed:** `trueplanet-globe-render-green-1` passes Standard/Detailed/legacyOne/legacyTwo (162 assertions, zero unexpected Unity/pathfinder errors). Native proof covers actual outward mesh/UV seam, opaque near surface and transparent silhouette, genuine camera work, no renders on unchanged frames, rotation/drag-click suppression, zoom, ocean/miss preservation, exact flat identity agreement and unchanged saved world. Whole-world/political and zoomed tile/legacy selection screenshots inspected. Pure real-assembly surface/projection smoke passes 11 assertions. Final floating HUD and end-to-end gameplay/resource matrix remain required.

## Task 4: Native-skinned translucent floating HUD and exact input isolation

**Files:** Create GlobeSkin.cs and AtlasInputScope.cs; redesign PlanetView.cs in place; register the atlas UI input patch in TruePlanet.cs. Adjust PlanetCanvas/PlanetGlobe callbacks only to share the screen's real selected ID/layers.

**Consumes:** Qualified templates, actual globe/flat components, existing generation/save/load/inspection/Play callbacks.

**Produces:** Final approved New Game/Campaign screens with these helper contracts:

```csharp
internal sealed class GlobeSkin : IDisposable
{
    internal TMP_FontAsset Font { get; }
    internal Material FontMaterial { get; }
    internal GlobeSkin(Transform owner, bool inGame);
    internal GameObject Panel(string name, Transform parent);
    internal Button Button(string caption, Transform parent, Action action);
    internal TMP_InputField Input(string caption, Transform parent, string value);
    public void Dispose();
}
internal sealed class AtlasInputScope : IDisposable
{
    internal AtlasInputScope(); // Capture source objects, then suppress native input.
    internal static void Install(Harmony harmony); // Register only the atlas mouse/UI gate.
    public void Dispose(); // Idempotent; restore captured live source objects only.
}
```

- [x] **Step 1: Add the native HUD/input red scenario.** In `hud` mode open the real atlas and require the full viewport with floating panels and copied native artwork, not the old opaque Map panel/Province panel. Capture original source movement/hotkey values, including mixed enabled/disabled values, native camera pose, game pause/speed and serialized world. Send actual EventSystem pointer/scroll/drag actions over the world drawer, details scroller, layer drawer and empty viewport. Expect controlled missing redesigned HUD/input behavior before implementation, not a native gameplay side effect accepted as success.

  ```csharp
  ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current)
      { position = pointer, scrollDelta = new Vector2(0, -1) }, ExecuteEvents.scrollHandler);
  Require(ReadCameraPose(nativeCamera).Equals(beforePose),
      "atlas panel scrolling cannot move the native camera");
  ```

  Define `ReadCameraPose(Camera)` in the owned probe as a value tuple `(Vector3 Position, Quaternion Rotation, float Size)` captured from transform position/rotation and orthographic size. Compare with value Equals, not reference equality of separately allocated Tuple objects. Hook real native action methods with scoped probe counters to prove no world actions or copied Reset Tutorials callbacks, not only unchanged component text.

- [x] **Step 2: Implement native visual cloning safely.** Resolve Task 1's real templates from the persistent settings window and active native font source; create one inactive owned template root. Clone actual native artwork/layout/selectable state, remove localization/hover/native action-bearing scripts and replace copied UnityEvents before activation. Buttons, inputs, toggles and scrollers must not retain native targets/groups or callbacks. Do not mutate original template assets/window visibility.

  ```csharp
  var button = copy.GetComponent<Button>();
  button.onClick = new Button.ButtonClickedEvent();
  button.onClick.AddListener(() => action());
  foreach (var text in copy.GetComponentsInChildren<TMP_Text>(true))
      text.raycastTarget = false;
  ```

  Background and border are separate visuals where the sliced template supports it: native centre at about .78 alpha, native trim stronger, text alpha one. `Image.fillCenter=false` can supply the trim only for a real sliced nonzero-border sprite validated in preflight. Never lower whole-panel text opacity via CanvasGroup merely for transparency. Missing templates report their actual path/type and fail qualification; do not replace them with generic rectangles.

- [x] **Step 3: Replace dashboard layout with the approved full viewport.** Create one registered fullscreen hit surface and a viewport below the small floating HUD. Header at upper-left, Back at upper-right, New Game world actions under the header, selection inspector at right, view/layers/reset strip at bottom. Use existing actual controls inside a World settings drawer; keep name/seed/options behavior, random-seed Re-generate, saved-world list and actual error status. An open drawer is not a new world or loss of unsaved field edits.

  PlanetView alone owns `_selected`, `_political`, `_borders`, `_roads`, `_grid` and current mode. Both renderer picks call one selection handler; both views receive the same selection/layers. Mode switches borrow the same PlanetSurface texture and world. `InspectTile` reads Surface.Geography, not an arbitrary currently visible renderer. Retain existing Play/LegacyProvinceGame/TileGame and SwitchTo method calls and eligibility; release input scope before the handoff.

  Drawers/inspector use native scroll bodies with preferred-size TMP text and anchored primary buttons. Name/seed controls stay reachable at every tested resolution. New Game uses Play; Campaign hides generation/save-world controls and uses Switch tile. The compact world summary before selection is real data, not invented village/population metrics.

- [x] **Step 4: Capture/restore source native input precisely.** AtlasInputScope records the actual InputManager shortcut dictionary, BaseCameraMove movement-disabled field, source object identity and native scene identity before suppressing input. Use existing APIs to suppress, but restore captured values rather than enable-all. Do not change pause/speed or disable the preview camera's own EventSystem handlers. Escape is handled by the atlas after closing drawers in priority order.

  ```csharp
  // Captured once from the real source, never fetched again during restoration:
  var captured = new Dictionary<SHORTCUT_ACTION, bool>(allowedShortcuts);
  // During Dispose, if captured source objects are still live and scene identity matches:
  foreach (var shortcut in captured)
      sourceInput.SetSpecificHotkeyEnabledState(shortcut.Key, shortcut.Value);
  ```

  Resolve the actual native method/field names from the game/reference before editing; the source precedent is DepartureCapture's `_allowedShortcuts` and BaseCameraMove `isMovementDisabled`. The input scope must restore even after UI construction failure, Back, native menu destruction or explicit Play/Switch tile close, and must not touch replacement destination managers. Fullscreen hit geometry stays registered even where its pixels are transparent. Panels consume their own drags/scrolls; decorative globe labels/strokes have raycastTarget=false.
  Native inspection also finds that Right_Click calls CancelSpellsByPriority even when IsMouseOnUI is true, while Toggle_Console bypasses the shortcut dictionary. AtlasInputScope therefore additionally prefixes the actual TryExecuteShortcutKeyDownAction dispatcher while the atlas is open; outside it, the original runs unchanged. uGUI input is independent. Campaign qualification must prove these actual source-world paths; main-menu UIManager is absent and cannot serve as that proof. Escape uses the game's InputSystem Keyboard device independently of suppressed world hotkeys.


  All owned uGUI nodes/hit surfaces use the native UI layer (not Default); UIManager.IsMouseOnUI recognizes only UI/WorldUI or Map_Click_Blocker raycast results. Also install one atlas-scoped postfix from TruePlanetMod.OnLoad so the first frame cannot click through before new Canvas geometry has rendered:

  ```csharp
  internal static void Install(Harmony harmony) => harmony.Patch(
      AccessTools.Method(typeof(UIManager), nameof(UIManager.IsMouseOnUI)),
      postfix: new HarmonyMethod(typeof(AtlasInputScope), nameof(MouseOnUi)));
  private static void MouseOnUi(ref bool __result)
  {
      if (PlanetView.Instance != null) __result = true;
  }
  ```

  Outside an open atlas, leave the original result untouched. Do not override EventSystem raycasts for the globe's own controls. Test native first-frame click/hover suppression as well as normal registered hit surfaces.

- [x] **Step 5: Build/run native HUD and review actual images.** Exercise `hud` at all four required resolutions and at mixed source input states, both modes, both drawers and long names/reasons. Read every final screenshot. Require native sprite/font identities, readable rendered glyphs, translucent fill/opaque text, scroll reachability, primary action availability, zero leaked actions/camera movement and exact source restoration. No perpetual animation or movement of panels while interacting.

  ```bash
  env MODE=hud CASES=1280x720,1568x878,1920x1080,2560x1080 EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-hud-green-1" python3 build/trueplanet-globe-qualify/run.py
  ```

**HUD checkpoint passed:** `trueplanet-globe-hud-green-8` passes all four actual client resolutions, including rendered legend containment, full failure-reason scrolling and InputSystem Escape. Subsequent owner review requested circle-free frames and better centering: `trueplanet-globe-hud-clean-green-2` passes all four sizes with genuine native dropdown-popup artwork (`button_minion_action_default`), balanced control padding, centered native-font captions, exact source shortcut restoration and zero unexpected Unity/pathfinder errors. All twenty revised default/settings/layers/map/long-details screenshots were read. This is native style/input proof; genuine gameplay, lifetime and the final same-candidate matrix remain pending.

## Task 5: Complete native behavior, lifetime/performance qualification and local handoff

**Files:** Extend the owned probe/driver/smoke. Update existing README/mod description and these docs only after proof. No new production feature/lifecycle scope.

**Consumes:** Fully integrated candidate; real legacy fixtures/native controls and existing campaign/consumer checks.

**Produces:** Complete, hash-verified local qualification with source/stage/docs/probe/log/screenshots/timings/protection evidence and owned workspace removed.

- [x] **Step 1: Write lifetime/behavior assertions before closing implementation.** `lifetime` takes an owned-resource baseline, opens/closes both modes ten times, resizes/regenerates while building and after ready, closes drawers/atlas via Escape/Back, and returns to the native menu. Check actual native camera identity/settings/pixels, borrowed skin assets and own camera/mesh/material/RenderTexture objects after destruction has drained. No invalid stale reference after source scene replacement.

  Capture source counts by object identity/name prefix, not all Unity allocations or an incidental global object count. Require no live owned preview objects/targets after close. Forced shader/target/skin construction failure is injected only in the throwaway probe and must release already created resources and source input; do not add production fault hooks.

  ```csharp
  Require(Camera.main == nativeMainCamera,
      "globe preview never replaces the native main camera");
  Require(!OwnedGlobeObjects().Any(),
      "closed atlas releases its owned cameras, roots, meshes, materials and targets");
  ```

  Define `OwnedGlobeObjects()` in the probe from captured owner objects and the `TruePlanet globe` prefix. Allow normal deferred Destroy to finish; do not hide leaks with Resources.UnloadUnusedAssets or a forced GC.

- [x] **Step 2: Exercise New Game and genuine native Play.** `new-game` Standard/Detailed loads genuine legacy atlases, edits name/seed/generation options, regenerates, saves/reloads world JSON and verifies unchanged chosen identities/options. Pick actual eligible land via globe, invoke the real Play button and finish native setup/Portal placement using retained first-visit mechanics. Native save/reload must restore selected world/tile/province and native terrain/Portal/resident identities under the existing stock reconstruction controls. No source-text, forwarded-argument or fake callback test substitutes for native gameplay.

  ```bash
  env MODE=new-game CASES=Small,TruePlanetLarge EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-play-green-1" python3 build/trueplanet-globe-qualify/run.py
  ```

  The case determines fixed LocalMapSize Small/Large, not a changed cell ID or political province size. Also execute genuine legacy province selection/Play on preserved format-one/two inputs where existing qualification supports them.

  **Gameplay checkpoint passed:** generated Small and TruePlanet Large pass actual native Play/setup, resident movement, committed archive identity and exact all-coordinate asset-derived terrain reload in `trueplanet-globe-play-green-3` (17 assertions each). Both genuine legacy formats pass native Play/setup/save/reload and resident movement in `trueplanet-globe-legacy-play-green-1` (15 each), with original province identity/dimensions and zero unexpected Unity/pathfinder errors.

  Legacy terrain qualification must distinguish raw default TileBase reconstruction from durable fields. Native `SaveDataInnerMap.Save` omits `LocationGridTile.isDefault` coordinates; one legacy run had 17 `OutsideTile`→`GrassTile` replacements with unchanged derived Grass/type and no committed tile records. A renderer-free control using the same saved native noise inputs reproduced that mechanism at 13 native-omitted coordinates (`trueplanet-globe-legacy-matched-control-1`); native settlement placement differed, so these are not identical coordinate sets. The revised oracle keeps exact complete coordinates and derived/type checks everywhere, verifies every committed tile's ID/type/ground/wall/shore fields against source and reload, and requires exact raw assets at every explicitly persisted coordinate without an asset allowlist. The matched control passes this contract in `trueplanet-globe-legacy-matched-control-green-1`. Default reconstruction differences remain recorded, not claimed byte-identical. Production legacy/save logic is unchanged.


- [x] **Step 3: Exercise actual Campaign atlas and Switch tile.** `campaign` opens the real UI in a saved native format-three source, verifies real visited/held/current/wilderness inspection and current-tile refusal, then invokes Switch tile for a second eligible tile. Observe source native save completion, normal destination loading/setup, villagers moving and A* work, destination native save, and a real return visit preserving source state/Portal/residents/campaign progression. Normal native reload remains compatible. Renderer/input cleanup must finish without stale source settings applied to destination.

  ```bash
  env MODE=campaign CASES=Small,TruePlanetLarge EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-campaign-green-1" python3 build/trueplanet-globe-qualify/run.py
  ```

  Use the existing actual campaign flow, no residency/deferred shortcut and no artificial instantly-ready destination. Observe `TileTravel`'s established errors/refusals; do not patch them away to pass the UI check.

- [x] **Step 4: Run the full modes/limits matrix on one final candidate.** Run preflight, surface, globe, hud, new-game, campaign, lifetime and regression in fresh per-mode evidence directories. Standard/Detailed/both legacy fixtures for atlas modes; all four required screen sizes for HUD; Small/Large for actual gameplay modes. Regression runs existing TruePlanetSuite generator/identity/storage checks against an actual completed native tile save plus the added ray boundaries, not a skipped no-world suite. No shared running game; stop on first unexpected failure and diagnose retained evidence before a changed-scenario run.

  ```bash
  python3 build/trueplanet-globe-qualify/smoke.py --mode all
  env MODE=lifetime CASES=menu,campaign EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-lifetime-green-1" python3 build/trueplanet-globe-qualify/run.py
  env MODE=regression CASES=Small,TruePlanetLarge EVIDENCE="$HOME/ruinarch-runs/trueplanet-globe-regression-green-1" python3 build/trueplanet-globe-qualify/run.py
  ```

  Regression stages the existing RuinarchDebug package temporarily only for that mode and invokes its TruePlanetSuite through the actual AutoTest host/entry convention. Preserve expected storage controls and no-Skip requirements. Build/lint/test once after each integrated task's edits, never while another edit slice is in flight.

  Record first surface-build duration, mode-switch and actual draw/overlay timings, max native frame gaps, one-second managed/Unity peaks and owned-process RSS/VmHWM, target dimensions/material choice, shader/skin identities, screenshot sizes and source/candidate hashes. Idle unchanged frames must not repeatedly Render/project/sample. Report drag/grid-on and Detailed costs separately; no guaranteed FPS or unmeasured smoothness claim. If actual terrain imagery is too soft at required viewport/zoom, improve genuine raster sampling only after image evidence and measure its generation/memory cost; do not silently lower the accepted readability criterion.

- [x] **Step 5: Review all twelve spec criteria and update existing docs.** Read screenshots, not only glyph/component assertions. Verify final current-source/candidate consistency and unchanged persisted world fields/formats. Update TruePlanet README and mod description to describe genuine globe/default mode, optional flat view, native floating HUD/controls and limits. Keep unimplemented Travel Portal/offscreen/residency milestones labeled unimplemented. Link spec/plan and evidence run IDs without personal paths/machine profile. Mark this spec/plan implemented/qualified only with actual date/evidence, never merely compiled.

- [x] **Step 6: Archive and verify handoff; remove only owned scaffolds.** Copy complete production source, final staged packages, owned probe/runner/smoke sources, original/changed failing evidence, protected manifests/results/logs/screenshots and measured decisions under the final evidence root. Hash-verify before removing `build/trueplanet-globe-qualify`. Independently verify full protected hashes/existence/metadata/directory sets, no extra player files, game stopped, probe/backup absent and Steam ledger idle. Keep normal build output/release package. Review the integrated change inline; scoped publication is the separately owner-authorized release step. Preserve unrelated dirty work and do not reinstall the loader or permanently deploy without separate authorization.

---

## Plan self-review and execution gate

Coverage: Task 1 proves native visual/material dependencies and safe protected execution. Task 2 preserves shared geography, pick/identity and flat view. Task 3 supplies actual 3D projection/picking, overlays and labels. Task 4 implements the approved native translucent HUD and exact input scope. Task 5 proves real Play/Switch/native reload, legacy compatibility, resource cleanup, screenshots, performance/error audit and local handoff.

| Spec acceptance criterion | Implementing/qualifying task |
| --- | --- |
| 1. Native skin/material, no copied actions | Task 1 preflight; Task 4 HUD |
| 2. Genuine globe and working flat view | Tasks 2-3 |
| 3. Matching geographic selection, seam/poles/ocean | Tasks 2-3; Task 5 native matrix |
| 4. Generation, world saves, native Play/Switch/reload | Tasks 4-5 |
| 5. Shared state, unchanged saved world, no raster rebuild on view actions | Tasks 2, 4-5 |
| 6. Native art/transparency/readability, four resolutions | Tasks 1, 4-5, actual image review |
| 7. Panel/drag/scroll isolation and exact source input restore | Tasks 4-5, including first-frame native mouse gate |
| 8. Genuine legacy and tile/campaign compatibility | Tasks 2, 5 |
| 9. Resource lifetime, native camera unchanged | Tasks 3, 5 |
| 10. All-thread/pathfinder audit and measured runtime limits | Tasks 1, 5 and every protected mode |
| 11. Complete protected restoration and archived evidence | Task 1 runner; Task 5 handoff |
| 12. Exercised docs/description and authorized, verified release | Task 5 plus owner-approved publication |

Self-review corrections: normalized managed inverse rotation replaces Unity's native injected inverse in offline smoke; resource construction stays inside Unity; permanent checks use AutoTest.Check; camera snapshots use value equality; flat/globe surface signatures and nation colors are explicit; UI-layer recognition and the first-frame native mouse gate are in the file plan. No unresolved prototype/ownership mismatch or planned save-format change remains.

Initial gate: native template/material availability required a real preflight rather than a mathematical assumption or fake fallback. The owner approved the written spec/plan before production implementation, then explicitly authorized the road refinement and qualified release on 2026-10-13. Publication remains gated on final native evidence, not compilation alone.

## Final native qualification and acceptance review

`trueplanet-globe-final` completed eight modes and 23 cases on the same candidate,
SHA256 `4327590482d6d1403980eaea94dd2eaca6597b0d37bac9a74caf387c4dd98514`,
with actual released loader 0.9.0 dependencies. All 1,130 native assertions passed;
the consumer suite separately passed 31 checks for each Small/Large fixture
without skips. Unexpected Unity errors and pathfinder faults: zero.

The owner pause and later outer 3,600-second orchestration deadline are preserved
as interrupted evidence, not counted as passing cases. After independent cleanup,
the final regression batch ran afresh with the runner's existing per-case deadline
and no outer kill deadline. No production changes were needed for this interruption.

Inline acceptance review found no remaining implementation gap in criteria 1–10:
native skin/material and stripped callbacks; genuine globe plus flat view; exact
seam/pole/coast/wilderness selection; real generation/save/load/Play/Switch/return;
shared immutable world/raster/layer state; four-resolution visual review;
panel/native input isolation with exact restore; legacy/durable save compatibility;
owned-resource/failure/menu cleanup; all-thread error audits and measured limits.
Criterion 11's final protection was independently verified for all 80 files and
complete file/directory sets. `trueplanet-globe-handoff` was hash-verified before
removing the owned qualification directory and temporary disassembly trace.
Criterion 12 publication requires a scoped source tree/tag, exact qualified-DLL
package and verified public download; it never authorizes unrelated work.

All twenty final HUD states and every recorded final globe/surface/gameplay/
lifetime/regression image were read. Continuous roads cover 16,139 unobscured
route pixels with zero missing pixels. The README records actual first-raster,
geometry, drag/grid and render-submission timings, including Detailed costs.
It also exposes the 131.841-second maximum instrumented native frame gap,
749,858,452-byte Unity peak, 6,503,829,504-byte managed peak and 10,141,460-KiB
process VmHWM; these include native generation and exhaustive qualification work,
not a GPU/FPS or retained-mod-memory benchmark.

No generation/save/gameplay redesign: sixteen baseline model/geography/topology/
ownership/road-generation/store/campaign/native-execution source files remain
byte-identical. Seven permanent real-assembly ray-boundary checks were added;
the remaining probes are owned qualification scaffolds. C# LSP was unavailable
in this environment; actual compiler, patch resolution and native consumers
provide the exercised verification.


Final archival checkpoint: 4,320 artifacts / 947,663,024 bytes were copied and
hash-verified before owned-scaffold removal; an additional owned trace was
preserved before deleting its temporary copy. The release executes from the
archived scaffold, without rebuilding production. Publication/package evidence
is added to the handoff manifest and reverified after release; player data,
native assets, logs and private qualification scaffolds stay out of GitHub.

