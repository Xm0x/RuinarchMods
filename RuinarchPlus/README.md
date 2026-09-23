# Ruinarch+

A bugfix, quality-of-life and gameplay mod for **Ruinarch**, built on the
[RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) (Harmony). Every
change is verified against the decompiled game source, not guessed.

It follows the [Ruinarch+ roadmap](RuinarchPlus-DESIGN.md): Phase 1 (confirmed bug
fixes plus opt-in QOL) and the first part of Phase 2 (Death, Decay & Disease). Phase 2
changes how the dead are handled by default; every Phase 2 feature can be switched off
in `config.json`.

## What it fixes

| # | Type | What you'll notice |
|---|------|--------------------|
| 1 | Bug | **Trait tooltips no longer show developer debug text.** The shipped build appended `GetTestingData()` (internal "Responsible Characters / gained-from" text) to every trait tooltip. Removed. |
| 2 | Bug | **Brainwash no longer offers Stalker-class prisoners.** It used to let you queue Brainwash on a Stalker, then always fail silently. Now the option is correctly unavailable. |
| 3 | Bug | **Paralyzed / Quarantined villagers can no longer be schemed into acting.** Scheme validation skipped the `canPerform` gate that the AI planner respects, so incapacitated targets could still be driven to act. Now they can't. |
| 4 | Exploit | **Infinite chaos-orb kennel closed.** "Drain Spirit" granted a chaos orb *before* applying drain damage, so an immortal target (a hibernating golem is `Indestructible`) produced an orb every tick forever. The tick now no-ops when the target can't actually be damaged. |
| 5 | Bug | **Released prisoners no longer reveal your Portal.** Freed captives were dropped on a tile beside the prison (inside your base, next to the Portal) then walked off to report it. They're now knocked **Unconscious** and relocated to their **home structure** instead. |

## Phase 2: Death, Decay & Disease (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Corpse decomposition** | Bodies left lying unburied now rot over time (Fresh → Bloated → Rotting → Skeletal) and finally **decompose and vanish**, instead of littering the map forever. Anything buried (a grave in a Cemetery, a Mass Grave, or anywhere else) never rots, and a body being carried pauses. Tunable via `corpseDecayDays`. On by default. |
| **Corpse-borne plague** *(opt-in)* | Rotting/skeletal unburied bodies inside a settlement sicken the living present, scaled by corpse count: "bodies rotting in a house, outbreak." Uses the game's own plague (Quarantined resistance applies). Buried bodies are never infectious. Enable with `corpseDiseaseEnabled`. |
| **No more scattered graves** | A village with no Cemetery or Cult Temple no longer buries its dead in random spots around the wilderness. Bodies lie where they fell until the village has a Mass Grave. Villages with a Cemetery bury their people exactly as before. |
| **Mass Grave** | A new village building. When a village has unburied dead and no graveyard, its villagers place a Mass Grave blueprint, gather the wood or stone, and build it themselves, like any other building (it can be damaged and destroyed like one too). It looks like a walled burial pit that fills up as bodies are laid in it. Once it stands, villagers carry every body in the village into it: residents, strangers, and creature carcasses. Nobody left alive to carry them? After `massGraveFallbackHours` the pit takes nearby bodies itself. |
| **Plague curfew** | When plague breaks out and the ruler answers with a measured response (Quarantine or Exile, rather than Slay or doing nothing), they also put the village under curfew: residents give up their free time (visiting, taverns, wandering) and go home, until the plague event ends. Work goes on, so plague care, burials and food production continue. The ruler and faction leader are exempt. Announced in the event log. |

*The plague wire defaults OFF, so disease only appears if you enable it. Starvation-to-death is already in the base game via the Malnourished status, no mod needed. The Mass Grave art is an AI-generated placeholder.*

## Optional QOL (config)

On first launch the mod writes **`config.json`** next to `RuinarchPlus.dll` in
your `Mods/RuinarchPlus/` folder:

```json
{
    "disableTutorial": false,
    "corpseDecayEnabled": true,
    "corpseDecayDays": 3,
    "massGraveBurialEnabled": true,
    "massGraveFallbackHours": 12,
    "curfewEnabled": true,
    "corpseDiseaseEnabled": false,
    "corpseDiseaseChancePerCorpse": 3
}
```

| Flag | Default | Effect |
|------|---------|--------|
| `disableTutorial` | `false` | Set to `true` to skip the tutorial/alert bootstrap (`TutorialManager.Initialize`). Veterans get no tutorial alert hand-holding. Off = base game unchanged. |
| `corpseDecayEnabled` | `true` | Unburied corpses rot and eventually decompose away. Set `false` for vanilla (corpses persist forever). |
| `corpseDecayDays` | `3` | In-game days an unburied corpse takes to fully decompose (480 ticks/day; floor 1/4 day). |
| `massGraveBurialEnabled` | `true` | Villages without a Cemetery/Cult Temple stop scattering graves, build a Mass Grave when they have unburied dead, and carry all bodies (including creatures) into it. Set `false` for vanilla burial. |
| `massGraveFallbackHours` | `12` | In-game hours a body near a Mass Grave may go un-carried before the pit takes it directly. |
| `curfewEnabled` | `true` | A ruler who answers a plague outbreak with Quarantine or Exile also orders residents home in their free time until the plague event ends. Set `false` for vanilla. |
| `corpseDiseaseEnabled` | `false` | Opt-in. Rotting corpses in a settlement spread plague to nearby villagers. Requires `corpseDecayEnabled`. |
| `corpseDiseaseChancePerCorpse` | `3` | Percent infection chance, per rotting corpse, per in-game hour, per nearby villager. |

Edit the file and relaunch for changes to take effect.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) into your game (its installer patches your `Assembly-CSharp.dll` and drops `0Harmony.dll` in `Mods/`).
2. Copy the `RuinarchPlus/` folder into your game's `Mods/` folder.
3. Launch. Check `Mods/mods.log`; you should see a line like:
   ```
   [ruinarch.plus] Applied 19 patch class(es) over 30 method(s): ...
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

- Ships **no game code or assets**, source only. You need your own copy of Ruinarch.
- Ruinarch and its assets belong to their respective owners; this is a fan-made mod, not affiliated with or endorsed by them.
