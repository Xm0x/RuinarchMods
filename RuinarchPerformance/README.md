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
The signal still reaches every tile object exactly as before; taking one off is instant.

**Finished jobs leave a listener behind.** Every job the game creates listens for crimes
being removed from its records. When the job is done, the game stops all of its other
listeners but forgets this one, and since jobs are recycled, the list grows by one entry
for every job ever created: about 4,500 a minute on a large map at full speed. The mod
removes the listener when the job is done. A finished job did nothing with it anyway.

## Measured

In one world, switching the fixes off and on again (60 seconds each, 4x speed, the game
and the camera left alone):

| Map | Without the mod | With the mod |
|---|---|---|
| Extra Large (24x14 areas, the game's largest) | 117 fps; 1 frame in 20 slower than 15.7 ms | 132 fps; 1 in 20 slower than 9.7 ms |
| 40x24 areas (a test size larger than the game offers) | 28 fps; 404 frames a minute over 50 ms | 65 fps; 5 frames a minute over 50 ms |

The bigger the map and the longer the game, the more it helps: the cost it removes grows
with the number of objects in the world.

## What it does not change

- Saves: nothing is written to them. Saves made with the mod load without it and the
  other way round.
- Memory: Ruinarch is a 64-bit game and can already use all the memory your computer has;
  there is no limit for a mod to raise.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader/releases),
   v0.6.0 or newer.
2. Copy the `RuinarchPerformance` folder into your game's `Mods/` folder, so you get
   `Mods/RuinarchPerformance/`.
3. Launch. `Mods/mods.log` should have a line like:
   ```
   [ruinarch.performance] Performance Mod v0.1.0: tile-object signal table and job crime-listener cleanup active
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
