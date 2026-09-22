# Ruinarch Modding — Project Architecture

Definitive layout for the whole Ruinarch reverse-engineering + modding effort.
**If anything contradicts this doc, this doc wins.** Read it before touching any repo.

---

## 0. The four things, and where they live

| # | Thing | Path | On GitHub | Ever modified for a feature? |
|---|---|---|---|---|
| 1 | **Decompiled game source** | `Projects/RuinarchRE/` | yes (`Xm0x/RuinarchRE`) | **NO — pristine, reference only** |
| 2 | **Mod loader + content framework** | `Projects/RuinarchModLoader/` | yes | yes (framework code) |
| 3 | **The mods** | `Projects/RuinarchMods/` | (separate) | yes (all features) |
| 4 | **Design / planning / handoff docs** | `Projects/Mods/RuinarchRE/` | notes hub | docs only |

The **game install** (`~/.local/share/Steam/steamapps/common/Ruinarch/`) is only a
**deploy target**. Nothing authoritative lives there.

---

## 1. The hard rules (these caused real mistakes — do not repeat)

1. **`RuinarchRE/` is the pristine decompiled game. It is NEVER edited to add a
   feature.** It exists on GitHub so other people can read the real game source and
   write their own mods against it. Putting mod content there (new classes, new enum
   values, feature code) *litters the reference* and is forbidden. It is a **read-only
   reference** you compile mods *against*, nothing more.
   - The one and only reason to change files under `RuinarchRE/src` is to **improve the
     decompile itself** (fix a decompiler artifact so it matches the real game better).
     Never to add gameplay.

2. **All features are mods, and all mods live in `RuinarchMods/`.** A "feature" =
   gameplay content or behavior. It ships as a Harmony mod (a separate DLL loaded at
   runtime into the *stock* game), never as a forked `Assembly-CSharp.dll`.

3. **Ruinarch+ is ONE umbrella mod.** Mass graveyards, corpse decay, disease,
   bugfixes, QOL, TruePlanet-lite, all of it — every gameplay feature is a **feature
   inside `RuinarchMods/RuinarchPlus/`**. We do **not** spin up a separate mod per
   feature. (`RuinarchDebug` is the *only* other mod — a dev/testing overlay, not
   gameplay. It stays separate on purpose.)

4. **The game DLL stays stock.** Deploy = stock `Assembly-CSharp.dll` + the loader
   injection + mod DLLs in `Mods/`. We never ship a recompiled game DLL. (An earlier
   attempt baked "Mass Grave" into a recompiled `Assembly-CSharp` — that violated rules
   1 and 4 and has been fully reverted.)

---

## 2. How a mod adds NEW content (the core capability)

Harmony patches *existing* methods; on its own it cannot add a new `STRUCTURE_TYPE`,
a new structure class, or a new build skill — which is exactly what content like a Mass
Grave needs. Rather than fork the game DLL, that capability is built **into the mod
loader** as a content-injection **framework**.

- **Framework:** `Ruinarch.ModContent` (ships with `RuinarchModLoader/`, loaded before
  mods). It lets a mod *register* genuinely new content against the stock game.
- **Mechanism (proven against the source):** the game instantiates almost everything by
  **reflection on an enum name** —
  `Type.GetType("<ns>." + enumValue.ToStringEnumNoSpace() + ", Assembly-CSharp")` then
  `Activator.CreateInstance(...)` (see `LandmarkManager.CreateNewStructureAt:448-458`,
  `PlayerSkillManager.ConstructAllDemonicStructureSkillsData:583-595`). The framework
  puts a Harmony **prefix** on each such factory: if the value is a **registered
  virtual enum value** (a cast-int in a reserved high range), it returns the mod's
  instance directly and skips the reflection. Stock DLL, no source edits.
- Full technical spec (API, every patch point with file:line, virtual-enum allocation,
  save-stability, gotchas): **`RuinarchModLoader/docs/CONTENT_FRAMEWORK.md`**.

Consequence: a mod like Ruinarch+ calls `ModContent.RegisterStructure(...)` in its
`OnLoad`, gets back a usable `STRUCTURE_TYPE`, and the new structure is buildable,
saveable, and menu-visible — all while the game DLL is untouched.

---

## 3. Build & deploy pipeline (per repo)

- **`RuinarchModLoader/`** → `tools/build.sh` builds `Ruinarch.Modding.dll` (loader
  API), `Ruinarch.ModContent.dll` (framework), and the Cecil `Patcher`. The patcher
  injects `ModLoader.Initialize()` into the stock game's module initializer.
- **`RuinarchMods/RuinarchPlus/`** → `tools/build-mod.sh` compiles the mod against the
  game's Managed DLLs + `Ruinarch.Modding` + `Ruinarch.ModContent` + `0Harmony`, emits
  `RuinarchPlus.dll`, drops it in `Mods/RuinarchPlus/`.
- **Deploy** = patch the stock game once (loader), then copy mod folders into the game's
  `Mods/`. Launch from Steam normally.

---

## 4. Naming

- Umbrella mod: **Ruinarch+**, id `ruinarch.plus`, namespace `RuinarchPlus`.
- Framework: **Ruinarch.ModContent** (or "ModContent" in prose).
- Sister mod (much later, world-scale): **TruePlanet** — still just a mod in
  `RuinarchMods/`, built on the same framework.

---

## 5. One-line summary

> `RuinarchRE/` = the clean decompiled game (read-only reference, on GitHub).
> `RuinarchModLoader/` = loader **+ the framework that lets mods add new content**.
> `RuinarchMods/RuinarchPlus/` = every gameplay feature, as one umbrella mod.
> The shipped game DLL is always stock.
