# Ruinarch Modding — Handoff

Written for the next agent picking up this work. Read `ARCHITECTURE.md` (same folder)
first — it is the source of truth for layout and the hard rules. This doc is the current
state + what to do next.

---

## TL;DR

We built the thing that was blocking everything: **a content-injection framework
(`Ruinarch.ModContent`) that lets a mod add genuinely NEW content (new buildable
structures, new skills) to the STOCK game via Harmony — no forked `Assembly-CSharp`, no
edits to the decompiled source.** As the first proof, **Mass Grave** is now a Ruinarch+
feature built on that framework.

Everything **compiles and deploys clean**. It is **not yet verified in a running game**
(the dev can't be automated on this Wayland+Proton setup) — that's the top next step.

---

## Repos (see ARCHITECTURE.md for the rules)

| Path | Role | GitHub |
|---|---|---|
| `Projects/RuinarchRE/` | pristine decompiled game — **read-only reference, never add features here** | `Xm0x/RuinarchRE` |
| `Projects/RuinarchModLoader/` | loader + **the content framework** | yes |
| `Projects/RuinarchMods/RuinarchPlus/` | the ONE umbrella mod; every gameplay feature (incl. Mass Grave) | — |
| `Projects/RuinarchMods/RuinarchDebug/` | dev overlay mod (separate on purpose) | — |
| game install `~/.local/share/Steam/steamapps/common/Ruinarch/` | deploy target; DLL stays **stock** + loader-patched | — |

---

## The content framework (`Ruinarch.ModContent`)

**Why:** Harmony patches existing methods; it can't add a new `STRUCTURE_TYPE`, a new
structure class, or a new build skill — which new content needs. Baking those into the
game DLL was tried and **rejected** (pollutes the decompiled reference + forks the DLL).

**How it works:** the game instantiates content by reflecting on an enum name —
`Type.GetType("<ns>." + enumValue.ToStringEnumNoSpace() + ", Assembly-CSharp")` then
`Activator.CreateInstance`. The framework:
1. Allocates a **virtual enum value** — a cast-int in `[100000, 1000000)` (`ContentRegistry`),
   deterministic from a string id via FNV-1a, so saves stay stable. `Enum.GetValues()`
   never returns these, so world-gen and other enum iterations ignore virtual content
   (this is why the old world-gen crash cannot recur).
2. Puts a Harmony **prefix** on each reflection factory: for a registered virtual value it
   returns the mod's instance and skips the reflection. Stock DLL untouched.

**Files** (`RuinarchModLoader/src/Ruinarch.ModContent/`):
- `ModContent.cs` — public API: `RegisterStructure(...)`, `StructureTypeFor(id)`,
  `SkillTypeFor(id)`, `Install()`. Self-installs its patches on first use.
- `StructureRegistration.cs` — the registration DTO a mod fills in.
- `ContentRegistry.cs` — store + the deterministic virtual-value allocator.
- `ContentPatches.cs` — the Harmony patch layer (table below).

**Patch table** (all grounded to `RuinarchRE/src`):
| Patch | Target | Why |
|---|---|---|
| `Patch_CreateNewStructureAt` | `LandmarkManager.CreateNewStructureAt` | build a registered structure |
| `Patch_LoadNewStructureAt` | `LandmarkManager.LoadNewStructureAt` | rebuild it from a save |
| `Patch_GetStructureData` | `LandmarkManager.GetStructureData` | reuse an existing prefab/visual |
| `Patch_ConstructDemonicSkills` | `PlayerSkillManager.ConstructAllDemonicStructureSkillsData` | inject skill data into the dict (NOT the fixed-13 array — that array drives `UnlockStructureUIController` and crashed when grown) |
| `Patch_HasDemonicStructureSkill` | `PlayerSkillManager.HasDemonicStructureSkill` | report true for virtual skills |
| `Patch_Is{Demonic,Player,Special}Structure` | `Extensions.*` | classification switches |
| `Patch_UnlockRegisteredSkill` | `PlayerSkillComponent.AddAndCategorizePlayerSkill` | grant the skill (→ build menu) when its `UnlockWith` source is gained |

Note: `AreaStructureComponent.CanBuildDemonicStructureHere`'s default case already falls
through to the generic corrupted-tile buildable check, so virtual demonic structures are
buildable with **no** patch. `Messenger` is `internal` — the broadcast in the unlock patch
goes through reflection (`AccessTools`).

**Public API a mod uses:**
```csharp
var reg = ModContent.RegisterStructure(new StructureRegistration {
    Id          = "yourmod.thing",                 // stable → deterministic virtual enum
    Factory     = (type, region)       => new Thing(type, region),
    LoadFactory = (type, region, save) => new Thing(region, (SaveDataDemonicStructure)save),
    PrefabSource= STRUCTURE_TYPE.CRYPT,             // reuse an existing visual
    Skill       = new ThingData(),                 // its `type` getter → ModContent.SkillTypeFor(Id)
    UnlockWith  = PLAYER_SKILL_TYPE.CRYPT,          // appears in build menu with the Crypt
});
```

---

## Mass Grave (first framework consumer)

Files: `RuinarchMods/RuinarchPlus/Phase2/` — `MassGrave.cs` (structure + hourly consume),
`MassGraveData.cs` (build skill), `MassGraveFeature.cs` (registration + the
`GameManager.TickStarted` postfix that drives the hourly consume). Registered from
`RuinarchPlus.OnLoad`.

Behaviour (source-verified, see `RuinarchPlus-DESIGN.md` Phase 2 #2): each in-game hour
the pit scans within 12 tiles and pulls unburied corpses in — sapient dead become
tombstones inside the pit (reusing `BuryCharacter.AfterBurySuccess` disposal:
`SetGrave` + `DestroyMarker`), animals/monsters cleared. Skips carried / player-seized /
already-being-buried corpses and villages with their own Cemetery. Fully try/catch-guarded.
Tracks `bodyCount` + `fillRatio` (0–1, `MoundCapacity=30`) for the future mound visual.

---

## Build & deploy (exact)

```bash
# 1. Loader + framework (builds Ruinarch.Modding.dll, Ruinarch.ModContent.dll, patcher)
cd Projects/RuinarchModLoader && ./tools/build.sh

# 2. Patch the STOCK game DLL with the loader (once; keeps Assembly-CSharp stock + injects Initialize)
dotnet build/patcher/RuinarchModLoader.Patcher.dll --game "$HOME/.local/share/Steam/steamapps/common/Ruinarch"

# 3. Build + deploy Ruinarch+ (also copies 0Harmony + Ruinarch.ModContent into Mods/ root)
./tools/build-mod.sh ../RuinarchMods/RuinarchPlus "$HOME/.local/share/Steam/steamapps/common/Ruinarch/Mods"

# 4. Launch Ruinarch from Steam normally.
```
Current deployed state: game DLL stock (5.55MB, 0 MassGrave) + loader injected; `Mods/`
has `0Harmony.dll`, `Ruinarch.ModContent.dll`, `RuinarchPlus/`, `RuinarchDebug/`.

---

## Verification status

- ✅ **Compile-verified** against the real game DLL: framework, RuinarchPlus + Mass Grave.
- ✅ **Deploy-verified**: stock DLL + loader + framework + mods on disk.
- ⚠️ **NOT runtime-verified**: no in-game run yet (Wayland+Proton game window isn't
  automatable here). Everything below "next steps" is unproven at runtime.

---

## Next steps (prioritized)

1. **In-game smoke test of Mass Grave** (the real proof). Launch from Steam, then in
   `Mods/mods.log` confirm `[ModContent] Content framework installed` and
   `[RuinarchPlus] Mass Grave registered (STRUCTURE_TYPE=…, PLAYER_SKILL_TYPE=…)`.
   Then, in a world:
   - Unlock/gain the **Crypt** → **Mass Grave** should appear in the demonic build menu.
   - Build it (reuses the Crypt visual/footprint) on corrupted ground.
   - Kill villagers/animals within ~12 tiles (RUIN DBG overlay) → wait an in-game hour →
     watch `Player.log` for `[MassGrave] Consumed <name> … (bodyCount=N)`.
   - **Save + reload** → the built Mass Grave must come back (tests `LoadNewStructureAt`
     prefix + deterministic virtual-value stability).
2. **Tune the fiddly runtime bits if the smoke test fails** (ranked by risk):
   - **Menu appearance / unlock** (`Patch_UnlockRegisteredSkill`): if Mass Grave doesn't
     show, verify `PLAYER_SKILL_CATEGORY` of the skill and how `BuildListUI` reads
     `demonicStructuresSkills`; may need to also seed the skill at load, not only on gain.
   - **Save stability across sessions**: confirm a Mass Grave saved in one run loads in a
     later run (the FNV-1a id→int must be identical; it is deterministic, but verify).
   - **Display name / tooltip**: virtual enums have no localization entry, so the menu
     label may be blank/a key. If so, patch the localization lookup or feed
     `StructureRegistration.DisplayName` through it.
   - **`GetSkillData(virtual)`** generic path: we populate the demonic dict; if some code
     calls the umbrella `GetSkillData` and NREs, add the entry to `allPlayerSkillsData` too.
3. **Mound sprite** — the only non-code item. `fillRatio` is wired; a Unity asset + a
   visual swap in `MassGrave.SetStructureObject` is all that's left for the mound. Needs
   the Unity editor.
4. **Grow the framework as needed**: a `GameSignals` bridge (public C# `Hour`/`Tick`
   events backed by one Harmony postfix on `GameManager.TickStarted`/`TickEnded`) would let
   any feature subscribe to game ticks without the per-feature patch Mass Grave uses.
   Skills-only / mechanics-only content mostly needs plain Harmony (already easy) — the
   framework is specifically for content that needs new enum values + classes.

---

## Gotchas already learned (don't rediscover these)

- **NEVER add features to `RuinarchRE/`** — it's the pristine decompiled reference on
  GitHub. A prior Mass Grave that baked into it was fully reverted.
- **Don't grow `PlayerSkillManager.allDemonicStructureSkills` (fixed 13)** — it drives
  `UnlockStructureUIController`'s fixed slot array and throws IndexOutOfRange. Inject into
  the **dictionary** instead (what the framework does).
- **`Messenger` is `internal`** — a mod assembly can't call it directly; go through
  reflection or Harmony-postfix the broadcast site.
- **Virtual enum values aren't returned by `Enum.GetValues()`** — so world-gen ignores
  them. That's a feature (no world-gen crash), but it also means anything that *enumerates*
  all enum values won't see your content; drive it through the specific factories instead.
