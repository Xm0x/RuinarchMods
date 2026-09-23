# Architecture

Ruinarch modding is split across three repositories.

| Repo | What it is |
|---|---|
| [RuinarchRE](https://github.com/Xm0x/RuinarchRE) | The game's code, decompiled into a buildable C# tree. A read-only reference: you read it to find what to patch. |
| [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) | The loader that runs mods inside the game, plus the `Ruinarch.ModContent` framework for adding new content. |
| RuinarchMods (this repo) | The mods themselves: Ruinarch+ and RuinarchDebug. |

## Conventions

1. **The game stays stock.** Mods are separate DLLs loaded at runtime into the unmodified
   game; nothing ships a recompiled `Assembly-CSharp.dll`.
2. **RuinarchRE only changes to improve the decompile.** Gameplay features never go
   there; they are mods.
3. **Gameplay features belong to Ruinarch+.** It is one mod with a config flag per
   feature, rather than one mod per feature. RuinarchDebug is kept separate because it
   is a development tool.

## Adding new content

Harmony changes methods that already exist. It cannot add a new `STRUCTURE_TYPE`, a new
structure class, or a new build skill, and the Mass Grave needs all three.

The game creates such content by reflection on the enum value's name
(`Type.GetType(... + value.ToStringEnumNoSpace() ...)`, then
`Activator.CreateInstance`; see `LandmarkManager.CreateNewStructureAt` and
`PlayerSkillManager.ConstructAllDemonicStructureSkillsData` in RuinarchRE).
`Ruinarch.ModContent` gives each registered piece of content a stable enum value outside
the game's own range and patches those factories to return the mod's instance for it. A
mod calls `ModContent.RegisterStructure(...)` in `OnLoad` and gets back a
`STRUCTURE_TYPE` the game can build, save and load.

Details: [CONTENT_FRAMEWORK.md](https://github.com/Xm0x/RuinarchModLoader/blob/master/docs/CONTENT_FRAMEWORK.md).

## Build and deploy

- **RuinarchModLoader:** `tools/build.sh` builds the loader (`Ruinarch.Modding.dll`), the
  framework (`Ruinarch.ModContent.dll`), the in-game mod menu and the patcher. The
  installer patches one call to `ModLoader.Initialize()` into the game's module
  initializer.
- **Mods:** `tools/build-mod.sh` (in the loader repo) compiles a mod folder against the
  game's DLLs, the loader, the framework and Harmony, checks every Harmony patch target,
  and copies the result into the game's `Mods/` folder.
