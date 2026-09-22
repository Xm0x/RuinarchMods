# Ruinarch+

A bugfix and quality-of-life mod for **Ruinarch**, built on the
[RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) (Harmony). Every
fix is verified against the decompiled game source, not guessed.

Phase 1 of the [Ruinarch+ roadmap](../../Mods/RuinarchRE/RuinarchPlus-DESIGN.md):
confirmed bug fixes plus opt-in QOL. No new content, no balance changes you
didn't ask for.

## What it fixes

| # | Type | What you'll notice |
|---|------|--------------------|
| 1 | Bug | **Trait tooltips no longer show developer debug text.** The shipped build appended `GetTestingData()` (internal "Responsible Characters / gained-from" text) to every trait tooltip. Removed. |
| 2 | Bug | **Brainwash no longer offers Stalker-class prisoners.** It used to let you queue Brainwash on a Stalker, then always fail silently. Now the option is correctly unavailable. |
| 3 | Bug | **Paralyzed / Quarantined villagers can no longer be schemed into acting.** Scheme validation skipped the `canPerform` gate that the AI planner respects, so incapacitated targets could still be driven to act. Now they can't. |
| 4 | Exploit | **Infinite chaos-orb kennel closed.** "Drain Spirit" granted a chaos orb *before* applying drain damage, so an immortal target (a hibernating golem is `Indestructible`) produced an orb every tick forever. The tick now no-ops when the target can't actually be damaged. |
| 5 | Bug | **Released prisoners no longer reveal your Portal.** Freed captives were dropped on a tile beside the prison — inside your base, next to the Portal — then walked off to report it. They're now knocked **Unconscious** and relocated to their **home structure** instead. |

## Optional QOL (config)

On first launch the mod writes **`config.json`** next to `RuinarchPlus.dll` in
your `Mods/RuinarchPlus/` folder:

```json
{
    "disableTutorial": false
}
```

| Flag | Default | Effect |
|------|---------|--------|
| `disableTutorial` | `false` | Set to `true` to skip the tutorial/alert bootstrap (`TutorialManager.Initialize`). Veterans get no tutorial alert hand-holding. Off = base game unchanged. |

Edit the file and relaunch for changes to take effect.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) into your game (its installer patches your `Assembly-CSharp.dll` and drops `0Harmony.dll` in `Mods/`).
2. Copy the `RuinarchPlus/` folder into your game's `Mods/` folder.
3. Launch. Check `Mods/mods.log` — you should see:
   ```
   [ruinarch.plus] Applied 6 patch(es): TraitItem.OnHover, PrisonCell.IsValidBrainwashTarget,
   SchemeData.CanPerformAbilityTowards, BeingDrained.DrainPerTick, MovementComponent.LetGo,
   TutorialManager.Initialize
   ```

## Build from source

Requires the RuinarchModLoader checkout (for `tools/build-mod.sh`, the modding
API, and Harmony) and a legit Ruinarch install it can reference.

```bash
# from your RuinarchModLoader checkout:
tools/build-mod.sh /path/to/RuinarchMods/RuinarchPlus
# -> build/mods/RuinarchPlus/RuinarchPlus.dll
```

Each fix is one `[HarmonyPatch]` class under `Fixes/`, with a comment citing the
exact game method and behaviour it corrects.

## Notes

- Ships **no game code or assets** — source only. You need your own copy of Ruinarch.
- Ruinarch and its assets belong to their respective owners; this is a fan-made mod, not affiliated with or endorsed by them.
