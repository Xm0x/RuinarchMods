# RuinarchMods
DISCLAIMER: For %100 honesty, help of AI was used in this project.

Gameplay mods for [Ruinarch](https://store.steampowered.com/app/909320/Ruinarch/),
loaded by the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) and built
against the decompiled reference in [RuinarchRE](https://github.com/Xm0x/RuinarchRE).

These mods patch the **stock** game DLL at runtime via Harmony. There is no forked
`Assembly-CSharp` and no edits to the game's shipped assemblies.

## Mods

| Mod | What it does |
|---|---|
| **RuinarchPlus** | The umbrella gameplay mod. Bugfixes plus new content: corpse decay, corpse-borne disease, a plague curfew, and the **Mass Grave**, a new *village* building (added via the content-injection framework) that villagers build from materials and carry their dead (and creature carcasses) into, instead of scattering graves. Every feature is source-verified against `RuinarchRE`, and defaults are conservative or opt-in where risky. I also don't know how to really make sprites, art and stuff in general. Until someone would offer to handle the art/sprite side of the mod there will be generic placeholders for the buildings and things that require sprites, for debugging purposes. |
| **RuinarchDebug** | A separate dev-only mod: an in-game debug overlay (spawn, kill, time, place buildings, dev console) plus an unattended test harness that plays scenarios in a real world and reports PASS/FAIL. Kept out of `RuinarchPlus` on purpose so it never ships in a normal play session. |

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
    MassGraveFeature.cs    #     registers it via Ruinarch.ModContent; hourly driver
    MassGraveBurial.cs     #     burial reroute (no scattered graves; bodies to the pit)
    MassGraveConstruction.cs #   villagers decide on, place and build a Mass Grave
    CorpseDecay.cs         #     unburied bodies rot and disappear
    CorpseDisease.cs       #     corpse-borne disease
  art/mass_grave/          #   Mass Grave fill-stage sprites (loaded via ModArt)
  README.md                #   mod overview
  RuinarchPlus-DESIGN.md   #   design & roadmap
  mod.json                 #   loader manifest
RuinarchDebug/             # separate dev-only mod
  DebugMenu.cs             #   IMGUI overlay: spawn / kill / place / dev-console helpers
  AutoTest.cs              #   unattended in-game test harness (autotest.flag -> autotest.log)
  PlusBridge.cs            #   reflection bridge to Ruinarch+ (no hard dependency)
  RuinarchDebug.cs         #   entry point (OnLoad)
  mod.json                 #   loader manifest
ARCHITECTURE.md            # layout + the hard rules (read this first)
```
