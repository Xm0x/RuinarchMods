# Performance Mod

Reduce loading delays and some causes of stuttering in Ruinarch, especially
on large maps and in long-running games. The mod also lets the frame-rate cap
follow a high-refresh screen.

It does not change gameplay rules or save formats. Saves made with the mod
can be loaded without it. Works with or without Ruinarch+ and TruePlanet.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3813240118)
| [Download 0.4.1](https://github.com/Xm0x/RuinarchMods/releases/tag/ruinarch-performance-v0.4.1)

## What it does

Large saves take less work to read and load. While you browse the Load window,
the selected save can be prepared in advance to shorten the wait after you
choose it.

During play, the mod reduces repeated work when creatures and buildings are
removed, prevents finished jobs from leaving behind work that slows later
frames, and avoids redrawing an unchanged minimap every frame.

On screens faster than 144 Hz, the frame-rate cap can match the screen's
refresh rate. You can also set a fixed cap from 30 to 360. The game's vertical
sync setting still takes priority.

## Install

Requires [RuinarchModLoader 0.8.0 or newer](https://github.com/Xm0x/RuinarchModLoader).
Install the loader first, subscribe to Performance Mod on Steam Workshop,
then restart Ruinarch. The main-menu **Mods** window should list Performance Mod.

For a manual installation, download `RuinarchPerformance-0.4.1.zip` from the
release and extract its `RuinarchPerformance` folder into the game's `Mods/`
folder instead of subscribing on Workshop.

## Settings

Open **Settings > Mods > Performance Mod**.

| Setting | What to choose |
|---|---|
| Match screen refresh rate | Leave on to use your screen's refresh rate above 144 Hz. |
| Frame rate cap | Set a fixed limit when matching the screen is off. Default: 144. |
| Minimap redraws only when it changes | Leave on to avoid drawing an unchanged minimap every frame. |
| Tile objects share one signal listener | Leave on to reduce work when creatures or buildings are removed. Restart after changing it. |
| Finished jobs drop their crime listener | Leave on to prevent finished jobs from slowing later play. Restart after changing it. |
| Preload selected saves | Leave on to prepare the selected save before loading. Turn off if it causes stutters or memory pressure. |

All switches are on by default. Frame-rate, minimap, and preload options apply
immediately. The two settings that need a restart show **Restart to apply**
until you restart Ruinarch.

## Save preloading uses extra memory

Preparing a selected save uses RAM and can cause stutters while the Load
window is open. Turn off **Preload selected saves** if you prefer a longer
load to that extra work. Loading still benefits from the other fixes.

The mod cannot remove every loading pause or guarantee a particular frame rate.
Map size, save contents, and your computer affect the result.

Report problems through [GitHub issues](https://github.com/Xm0x/RuinarchMods/issues)
with your mod version, map size, and whether preloading was enabled.

Performance Mod is an independent fan-made mod. Ruinarch and its assets belong
to their respective owners.
