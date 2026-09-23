# RuinarchMods
DISCLAIMER: For %100 honesty, help of AI was used in this project.

Gameplay mods for [Ruinarch](https://store.steampowered.com/app/909320/Ruinarch/),
loaded by the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) and written
against the decompiled game source in [RuinarchRE](https://github.com/Xm0x/RuinarchRE).

The mods patch the **stock** game at runtime with Harmony. The game's own DLLs are never
replaced or recompiled.

## Mods

| Mod | What it does |
|---|---|
| **[Ruinarch+](RuinarchPlus/README.md)** | The gameplay mod: bug fixes, plus rotting corpses, a Mass Grave village building, a plague curfew, factions that only attack what they know about, and migration that follows a village's fortunes. Risky features are opt-in, and every feature can be switched off in `config.json`. The Mass Grave art is a placeholder; real art is welcome. |
| **RuinarchDebug** | A development tool, not meant for normal play: an in-game debug overlay (spawn, kill, time control, place buildings, dev console) and an unattended test harness that plays scenarios in a real world and reports PASS/FAIL. |

New buildings such as the Mass Grave need a new `STRUCTURE_TYPE`, which Harmony alone
cannot add. That part comes from the `Ruinarch.ModContent` framework in the
RuinarchModLoader repo; see its
[CONTENT_FRAMEWORK.md](https://github.com/Xm0x/RuinarchModLoader/blob/master/docs/CONTENT_FRAMEWORK.md).

## Layout

```
RuinarchPlus/
  RuinarchPlus.cs            entry point: applies the patches, registers the Mass Grave
  Config.cs                  config.json options
  Fixes/                     one Harmony patch class per bug fix
  Phase2/                    death, decay and disease
    CorpseDecay.cs           unburied bodies rot and disappear
    CorpseDisease.cs         rotting bodies spread plague (opt-in)
    Curfew.cs                plague curfew
    MassGrave*.cs            the Mass Grave: structure, burial, construction, look
  Phase3/Knowledge.cs        per-faction list of known demonic structures
  Phase4/MigrationHealth.cs  migration follows village health
  art/mass_grave/            Mass Grave sprite
  RuinarchPlus-DESIGN.md     design notes and roadmap
RuinarchDebug/
  DebugMenu.cs               in-game overlay
  AutoTest.cs                unattended in-game test harness
  PlusBridge.cs              reaches Ruinarch+ by reflection (no hard dependency)
ARCHITECTURE.md              how the three repos fit together
```

## Building

Mods are built with `tools/build-mod.sh` from a RuinarchModLoader checkout:

```bash
tools/build-mod.sh /path/to/RuinarchMods/RuinarchPlus "/path/to/Ruinarch/Mods"
```

`tools/run-autotest.sh` in the same repo runs the RuinarchDebug test harness in the game.

This repo holds source only: no game binaries or assets.
