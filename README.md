# RuinarchMods

Gameplay mods for [Ruinarch](https://store.steampowered.com/app/1268820/Ruinarch/),
loaded by the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) and built
against the decompiled reference in [RuinarchRE](https://github.com/Xm0x/RuinarchRE).

These mods patch the **stock** game DLL at runtime via Harmony. There is no forked
`Assembly-CSharp` and no edits to the game's shipped assemblies.

## Mods

| Mod | What it does |
|---|---|
| **RuinarchPlus** | The umbrella gameplay mod. Bugfixes plus new content (corpse decay, corpse-borne disease, opt-in starvation death, and **Mass Grave**, a new buildable *village* structure added via the content-injection framework). Every feature is source-verified against `RuinarchRE`, and defaults are conservative or opt-in where risky. |
| **RuinarchDebug** | A separate dev-only overlay mod (tile/entity debug helpers). Kept out of `RuinarchPlus` on purpose so it never ships in a normal play session. |

New enum-backed content (new `STRUCTURE_TYPE`, new build skill) is made possible by the
`Ruinarch.ModContent` framework that lives in the **RuinarchModLoader** repo. It allocates
deterministic *virtual* enum values and Harmony-prefixes the game's reflection factories so
a mod can add genuinely new content without touching the game DLL.

## Layout

```
RuinarchPlus/              # the one umbrella gameplay mod
  RuinarchPlus.cs          #   entry point (OnLoad): applies fixes + registers content
  Config.cs                #   runtime config toggles
  Fixes/                   #   individual bugfix Harmony patches
  Phase2/                  #   new-content features
    MassGrave.cs           #     the Mass Grave village structure
    MassGraveFeature.cs    #     registers it via Ruinarch.ModContent
    CorpseDecay.cs         #     corpse decomposition
    CorpseDisease.cs       #     corpse-borne disease
  README.md                #   mod overview
  RuinarchPlus-DESIGN.md   #   design & roadmap
  mod.json                 #   loader manifest
RuinarchDebug/             # separate dev-only overlay mod
  DebugMenu.cs             #   IMGUI overlay: spawn / kill / place / dev-console helpers
  RuinarchDebug.cs         #   entry point (OnLoad)
  mod.json                 #   loader manifest
ARCHITECTURE.md            # layout + the hard rules (read this first)
```
