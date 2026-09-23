# Ruinarch+

A bug-fix and gameplay mod for **Ruinarch**, built on the
[RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader). Each change is checked
against the decompiled game source.

It follows the [Ruinarch+ roadmap](RuinarchPlus-DESIGN.md). Shipped so far: the Phase 1
bug fixes, and the first features of Phases 2 to 4 (death and decay, knowledge, and
migration). These change the game by default; each one can be switched off in
`config.json`.

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
| **Corpse-borne plague** *(opt-in)* | Rotting/skeletal unburied bodies inside a settlement sicken the living present, scaled by corpse count. Uses the game's own plague (Quarantined resistance applies). Buried bodies are never infectious. Enable with `corpseDiseaseEnabled`. |
| **No more scattered graves** | A village with no Cemetery or Cult Temple no longer buries its dead in random spots around the wilderness. Bodies lie where they fell until the village has a Mass Grave. Villages with a Cemetery bury their own people there exactly as before; outsiders, monsters and creatures go to the Mass Grave if the village has one. |
| **Mass Grave** | A new village building. When a village has unburied dead and no graveyard, its villagers place a Mass Grave blueprint, gather the wood or stone, and build it themselves, like any other building (it can be damaged and destroyed like one too). It is a bare dirt pit labelled "Mass Grave" on the map (placeholder art until real art arrives), with none of the Cemetery's paving, statues, braziers or basins. A village has at most one. Once it stands, villagers carry every body in the village into it (residents, strangers and creature carcasses; once the village also has a Cemetery, its own people go there instead) and also fetch bodies from the land around the village. Bodies laid in the pit leave no gravestones; the pit just keeps count. Nobody left alive to carry them? After `massGraveFallbackHours` the pit takes nearby bodies itself. |
| **Plague curfew** | When plague breaks out and the ruler answers with a measured response (Quarantine or Exile, rather than Slay or doing nothing), they also put the village under curfew: residents give up their free time (visiting, taverns, wandering) and go home, until the plague event ends. Work goes on, so plague care, burials and food production continue. The ruler and faction leader are exempt. Announced in the event log. |

*Corpse-borne plague is off by default. Starvation is not part of this mod: the base game already kills starving villagers through the Malnourished status. The Mass Grave art is a placeholder; real art is welcome (one `mass_grave.png`, or four fill stages `mass_grave_0..3.png`, in `art/mass_grave/`).*

## Phase 3: Knowledge & Fog of War (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **They only attack what they know** | In the base game, one villager reporting one of your buildings makes their whole faction "aware of you": their counterattacks head for your whole domain and then march straight on the Portal, even if nobody has ever seen it. Now each faction remembers which of your buildings it actually knows about (reported by a villager, seen by its people once they are hunting you, or standing right next to one of its villages). Counterattacks go for the nearest building they know and attack only those; the Portal is a target once someone has seen it. When everything they know of is destroyed, they go home. What each faction knows is stored inside your save file. |

## Phase 4: Living Population (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Migration follows a village's fortunes** | Settlers no longer pour into a village in crisis. Nobody moves in during a plague, while the village is under attack, or once more of its homes stand abandoned than lived in; otherwise each abandoned home (beyond the couple a growing village keeps spare) and each unburied body in the streets halves the pull; and every resident who dies sets the "Incoming Migrants" meter back. A village of ten reduced to one stops drawing settlers. Hover the migration meter to see why. Your Induce Migration power still works as before. |

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
    "knowledgeEnabled": true,
    "migrationHealthEnabled": true,
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
| `knowledgeEnabled` | `true` | Factions attack only the demonic buildings they know about; counterattacks no longer march on a Portal nobody has seen. Set `false` for vanilla. |
| `migrationHealthEnabled` | `true` | Plague, sieges, abandoned homes, unburied dead and deaths hold migration back. Set `false` for vanilla migration. |
| `corpseDiseaseEnabled` | `false` | Opt-in. Rotting corpses in a settlement spread plague to nearby villagers. Requires `corpseDecayEnabled`. |
| `corpseDiseaseChancePerCorpse` | `3` | Percent infection chance, per rotting corpse, per in-game hour, per nearby villager. |

Edit the file and relaunch for changes to take effect.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) into your game. Its installer patches your `Assembly-CSharp.dll` and puts `0Harmony.dll` and `Ruinarch.ModContent.dll` (which Ruinarch+ needs) in `Mods/`.
2. Copy the `RuinarchPlus/` folder into your game's `Mods/` folder.
3. Launch. Check `Mods/mods.log`; you should see a line like:
   ```
   [ruinarch.plus] Applied N patch class(es) over M method(s): ...
   ```

## Build from source

Requires the RuinarchModLoader checkout (for `tools/build-mod.sh`, the modding
API, and Harmony) and a legit Ruinarch install it can reference.

```bash
# from your RuinarchModLoader checkout:
tools/build-mod.sh /path/to/RuinarchMods/RuinarchPlus
# -> build/mods/RuinarchPlus/RuinarchPlus.dll (pass your game's Mods/ folder as a
#    second argument to install it there instead)
```

Each fix is one `[HarmonyPatch]` class under `Fixes/`, with a comment citing the
exact game method and behaviour it corrects.

## Notes

- Ships **no game code or assets**, source only. You need your own copy of Ruinarch.
- Ruinarch and its assets belong to their respective owners; this is a fan-made mod, not affiliated with or endorsed by them.
