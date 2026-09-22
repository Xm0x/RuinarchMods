# RuinarchMods

Gameplay mods for [Ruinarch](https://store.steampowered.com/app/1268820/Ruinarch/),
loaded by the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) and built
against the decompiled reference in [RuinarchRE](https://github.com/Xm0x/RuinarchRE).

These mods patch the **stock** game DLL at runtime via Harmony — no forked
`Assembly-CSharp`, no edits to the game's shipped assemblies.

## Mods

| Mod | What it does |
|---|---|
| **RuinarchPlus** | The umbrella gameplay mod. Bugfixes + new content (corpse decay, corpse-borne disease, opt-in starvation death, **Mass Grave** — a new buildable demonic structure added via the content-injection framework). Every feature is source-verified against `RuinarchRE` and defaults are conservative/opt-in where risky. |
| **RuinarchDebug** | A separate dev-only overlay mod (tile/entity debug helpers). Kept out of `RuinarchPlus` on purpose so it never ships in a normal play session. |

New enum-backed content (new `STRUCTURE_TYPE`, new build skill) is made possible by the
`Ruinarch.ModContent` framework that lives in the **RuinarchModLoader** repo — it allocates
deterministic *virtual* enum values and Harmony-prefixes the game's reflection factories so
a mod can add genuinely new content without touching the game DLL.

## Layout

```
RuinarchPlus/          # the one umbrella mod
  RuinarchPlus.cs      #   entry point (OnLoad)
  Config.cs            #   runtime config toggles
  Fixes/               #   individual bugfix patches
  Phase2/              #   new-content features (Mass Grave, corpse decay/disease, ...)
  mod.json             #   loader manifest
RuinarchDebug/         # separate dev overlay mod
ARCHITECTURE.md        # layout + the hard rules (read this first)
HANDOFF.md             # current state + next steps
```

## Building & deploying

The loader and the content framework live in the sibling **RuinarchModLoader** repo; its
`tools/build-mod.sh` compiles a mod folder against the real game DLL and deploys it into
the game's `Mods/` directory. See `HANDOFF.md` here and the RuinarchModLoader README for
the exact commands.

## Rules (see `ARCHITECTURE.md`)

- **No game binaries or assets are committed here** — ever. `.gitignore` blocks
  `*.dll`/`*.exe`/`*.pdb` and build output; only source (`*.cs`, `mod.json`, docs) is tracked.
- **Never add features to `RuinarchRE`** — it is the pristine decompiled reference.
- Patch the stock DLL at runtime; never fork `Assembly-CSharp`.
