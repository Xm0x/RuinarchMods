# Performance Mod

A Ruinarch mod that makes the game faster on big maps and in long games. It changes how
the game does some of its bookkeeping, never what happens in the world: the same
characters do the same things, only with less waiting between frames.

It works with or without [Ruinarch+](../RuinarchPlus/README.md).

## What it fixes

**Every tree and rock listens for the game's "disconnect" signals.** When a character
leaves the game for good, or a building is destroyed, the game tells everything that might
still point at it to let go. Every tile object (each tree, rock, crop, bed and table) is on
that list, so on a large map the list has more than 200,000 entries. Taking anything off
the list means searching it from the start, and the game does that several times a frame:
whenever a job finishes, a fight ends, a party breaks up or an object is destroyed. Each
search took about 3 ms.

The mod keeps the tile objects in a table of their own, behind a single entry on each list.
Taking one off is instant. The signal still reaches every tile object that has something to
let go of: one that made an assumption about the character, is being used or carried by
it, or has a trait that lists it or reacts to it. The rest would do nothing with it, so the
mod does not call them.

**Removing a dead creature froze the game for a moment.** When a monster's body leaves the
world, that same signal went to every tile object on the map: on the largest map about
75,000 calls, 45 ms or more for each creature, and a tick that removes five of them stalled
for a quarter of a second. It now takes about 20 ms (the time to look at each object once).

**Finished jobs leave a listener behind.** Every job the game creates listens for crimes
being removed from its records. When the job is done, the game stops all of its other
listeners but forgets this one, and since jobs are recycled, the list grows by one entry
for every job ever created: about 4,500 a minute on a large map at full speed. The mod
removes the listener when the job is done. A finished job did nothing with it anyway.

**The minimap redraws every frame.** The minimap is a second camera that draws the whole
map into a small picture, and it did so on every frame even when nothing on it changed,
which costs about 1 ms of a 6 ms frame. The mod lets it draw only when a minimap tile
changes, the rectangle showing your view moves, or once a second; while the minimap is
hidden it does not draw at all. It looks and works the same.

**The frame rate stops at 144.** The game caps itself at 144 frames a second. On a screen
that refreshes faster (165 Hz, 240 Hz), the mod raises the cap to the screen's rate. With
the default settings the cap only ever rises, to the screen's rate; to set a fixed cap
instead, lower or higher, see Settings below. Vertical sync, if you switch it on in the
game's options, still wins.

## Settings

Open the game's Settings window and pick the **Mods** tab, then **Performance Mod**.

Frame rate:

- **Match screen refresh rate** (on): on a screen faster than 144 Hz, the frame rate cap
  follows the screen. Vertical sync in the Graphics tab still comes first.
- **Frame rate cap** (144, from 30 to 360): the highest frame rate while matching the
  screen is off. Vertical sync in the Graphics tab still comes first.

Fixes:

- **Minimap redraws only when it changes** (on): off, the minimap is drawn every frame,
  as in the base game.
- **Tile objects share one signal listener** (on): removing a creature or building no
  longer checks every tree and rock on the map. Needs a restart.
- **Finished jobs drop their crime listener** (on): jobs stop leaving listeners behind
  that slow the game down over a long game. Needs a restart.

The first three take effect at once. The two listener fixes take effect the next time you
start the game; until then the tab shows "Restart to apply" next to them. The values are
saved in `Mods/settings/ruinarch.performance.json`.

## Measured

Version 0.1.0, in one world, switching the fixes off and on again (60 seconds each, 4x
speed, the game and the camera left alone):

| Map | Without the mod | With the mod |
|---|---|---|
| Extra Large (24x14 areas, the game's largest) | 117 fps; 1 frame in 20 slower than 15.7 ms | 132 fps; 1 in 20 slower than 9.7 ms |
| 40x24 areas (a test size larger than the game offers) | 28 fps; 404 frames a minute over 50 ms | 65 fps; 5 frames a minute over 50 ms |

Version 0.2.0 on an Extra Large map (a 165 Hz laptop screen, RTX 4060M):

| | 0.1.0 | 0.2.0 |
|---|---|---|
| Frame rate with the game's cap | 142 fps (capped at 144) | 163 fps (capped at 165) |
| Frame rate with no cap | 132-180 fps | 181-230 fps |
| Removing one dead creature | 38-52 ms | 19-22 ms |

The bigger the map and the longer the game, the more it helps: the costs it removes grow
with the number of objects in the world.

## What it does not change

- Saves: nothing is written to them. Saves made with the mod load without it and the
  other way round.
- Memory: Ruinarch is a 64-bit game and can already use all the memory your computer has;
  there is no limit for a mod to raise.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader/releases),
   v0.8.0 or newer (the settings need it).
2. Either subscribe to the Performance Mod on the
   [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3813240118)
   (Steam keeps it up to date), or download `RuinarchPerformance-<version>.zip` from the
   [releases](https://github.com/Xm0x/RuinarchMods/releases) and unzip it into your game's
   `Mods/` folder, so you get `Mods/RuinarchPerformance/`. Use one or the other: when both
   are present, the local copy wins.
3. Launch. `Mods/mods.log` should have a line like:
   ```
   [ruinarch.performance] Performance Mod v0.3.0: tile-object signal table on, job crime-listener cleanup on, minimap redraw on change on, frame cap matches the screen
   ```

## Build from source

```bash
# from your RuinarchModLoader checkout:
tools/build-mod.sh /path/to/RuinarchMods/RuinarchPerformance
# -> build/mods/RuinarchPerformance/RuinarchPerformance.dll (pass your game's Mods/
#    folder as a second argument to install it there instead)
```

The RuinarchDebug harness checks it: `tools/run-autotest.sh 1500 PerformanceSuite`
(add `PerformanceReloadSuite` to also reload the world from a save).

## Notes

- Ruinarch and its assets belong to their respective owners; this is a fan-made mod, not
  affiliated with or endorsed by them.
