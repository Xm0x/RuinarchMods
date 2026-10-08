# Performance Mod

A Ruinarch mod that makes the game faster on big maps and in long games. It changes how
the game does some of its bookkeeping, never what happens in the world: the same
characters do the same things, only with less waiting between frames.

It works with or without [Ruinarch+](../RuinarchPlus/README.md).

### 0.4.1: native loading and selected-save preparation

Second-wave tile-object loading walks the native collection once instead of
restarting a HashSet scan for every object. The native loading body, yield schedule
and save format are unchanged. If the collection changes, the cursor restarts at
the requested native ordinal rather than reading a stale snapshot.

Named enum decoding reuses successful native values instead of repeatedly scanning
enum names and boxing the same value. Flags, numeric virtual IDs, invalid names,
case sensitivity and overflow keep the native conversion path.

Instance-field conversion uses cached compiled accessors instead of reflection for
each value. Boxed structs are mutated in place, not copied. Property access, readonly
writes, volatile/pointer fields, reflection coercions and invalid targets retain the
native path. Accessors cache field metadata and code, never loaded objects; concurrent
first use compiles each field once.

The optional selected-save preload prepares one destination in RAM while browsing
the Load window or changing scenes. One worker handles the latest selection, not
one worker per click. Changing selection, closing the window, starting a new game
or disabling preloading retires the previous result.

The cache is only eligible for the stock JSON serializer and unencoded saves.
Before handoff, SHA-256 and byte length must match the actual extracted native save
stream. Matching snapshots transfer exactly once: the game destructively cleans
decoded saves after loading, so consumed graphs are never reused. Missing, invalid,
changed or oversized entries fall back to ordinary native decoding; its errors
remain visible. Native loading still owns ZIP extraction, streams and callbacks.

No live scene objects are cached. The 128 MiB uncompressed-entry guard is not a
128 MiB RAM budget: decoded objects and temporary JSON can take substantially more.
Native serializer metadata operations are synchronized because its shared caches
were not designed for simultaneous background preparation and ordinary saving.

This is a source development update, not a new Workshop or release ZIP publication.
Tile-world travel, multiple resident scenes and near-seamless switching are not
implemented by these loading changes.


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
- **Preload selected saves** (on): prepare one selected destination in RAM. Off
  cancels preparation and restores ordinary decoding without a restart. Use off
  when memory pressure or background frame stalls matter more than transition time.

Frame-rate, minimap and preload settings take effect at once. The two listener fixes
take effect the next time you start the game; until then the tab shows "Restart to
apply" next to them. Values are saved in `Mods/settings/ruinarch.performance.json`.

## Measured

Every number below was measured on one machine:

| | |
|---|---|
| Laptop | Acer Nitro AN515-58 |
| CPU | Intel Core i7-12650H (10 cores, 16 threads, up to 4.7 GHz) |
| GPU | NVIDIA GeForce RTX 4060 Laptop GPU, 8 GB (driver 595.104.02) |
| Memory | 16 GB |
| Screen | 1920x1080, 165 Hz |
| System | Nobara Linux 44 (Linux 7.2), Ruinarch through Steam with Proton Experimental |

Other machines will show different numbers; what carries over is the difference with and
without the mod.

Version 0.1.0, in one world, switching the fixes off and on again (60 seconds each, 4x
speed, the game and the camera left alone):

| Map | Without the mod | With the mod |
|---|---|---|
| Extra Large (24x14 areas, the game's largest) | 117 fps; 1 frame in 20 slower than 15.7 ms | 132 fps; 1 in 20 slower than 9.7 ms |
| 40x24 areas (a test size larger than the game offers) | 28 fps; 404 frames a minute over 50 ms | 65 fps; 5 frames a minute over 50 ms |

Version 0.2.0 on an Extra Large map:

| | 0.1.0 | 0.2.0 |
|---|---|---|
| Frame rate with the game's cap | 142 fps (capped at 144) | 163 fps (capped at 165) |
| Frame rate with no cap | 132-180 fps | 181-230 fps |
| Removing one dead creature | 38-52 ms | 19-22 ms |

The bigger the map and the longer the game, the more it helps: the costs it removes grow
with the number of objects in the world.

Development 0.3.1, protected same-save native reload comparisons:

| Fixture | Stock reload | Linear reload | Stock object loop | Linear object loop | Largest object-loop step, stock / linear |
|---|---:|---:|---:|---:|---:|
| TruePlanet Large, 280x168 cells | 90.931 s | 65.630 s | 31.885 s | 4.958 s | 24.823 s / 0.173 s |
| Ordinary Small, 140x140 cells | 23.407 s | 19.905 s | 3.761 s | 0.597 s | 3.217 s / 0.102 s |

These are individual runs, not an exhaustive seed/size benchmark. The same saved
world was loaded with only the new loading patches off/on; other installed mods
remained enabled. Native saves made after resumed gameplay also matched stock
loading. A 65-second Large load is still far from seamless: scene teardown, save
decoding and reconstruction remain separate costs.

### 0.4.0: production one-use preloading

Protected native runs compared ordinary decoding against the production cache.
Linear tile-object traversal was enabled in every column; the ordinary control
disabled the new enum/preload hooks. These are same-scene transitions, not the
faster first load from the main menu:

| Fixture | Ordinary decoding | Production, preparation starts at Load | Production, selected save already prepared |
|---|---:|---:|---:|
| TruePlanet Large, 280x168 cells | 55.077 s | 46.438 s | 37.606 s |
| Ordinary Small, 140x140 cells | 17.626 s | 14.350 s | 13.319 s |

Preparation happens before the transition in the last column; it is not free:

| Fixture | Preparation including Load-window selection | Whole-process managed-memory increase during preparation, before GC | Largest observed preparation frame gap |
|---|---:|---:|---:|
| TruePlanet Large | 29.717 s | 965.1 MiB | 0.231 s |
| Ordinary Small | 7.357 s | 286.7 MiB | 0.218 s |

Those memory deltas include temporary JSON and unrelated process allocations, not
just the retained graph. Background work can hurt frame pacing: 467/534 Large and
122/131 Small preparation frames exceeded 50 ms. Disable preloading if that tradeoff
is undesirable. Advance preparation hides work; it does not eliminate it. Even a
ready Large transition still takes about 38 seconds because native scene teardown
and reconstruction remain. This is not near-seamless travel.

Every 47,040 Large and 19,600 Small terrain/biome/actual TileBase record matched.
The initial 15,101/3,134 non-generic object records and post-gameplay 15,297/3,244
records also matched. Only positioned visible ThinWall GUIDs were normalized:
stock-to-stock controls recreate those IDs; saved nonvisual wall IDs and every
other ID stayed exact.

Each fixture passed six loading-cursor regressions and thirteen cache/enum checks,
with zero game errors. The real Load-window selection, native callbacks exactly
once, concurrent player-data saving, villager movement, Portal identity and native
save/reload were exercised. Post-gameplay ordinary/production transitions were
60.594/52.740 s (Large) and 18.679/16.120 s (Small), with preparation starting at Load.
Protected runs restored all thirteen player/settings/deployed-candidate files
byte-identically and stopped the game. Screenshots were inspected at 1920x1080.

This is two fixtures on one machine, not an exhaustive seed/size benchmark.
Evidence: `/home/deniz/ruinarch-runs/native-loading-production-cache-2/`;
the red/green isolated checks are in `native-preload-red-1` and
`native-preload-green-3`. Earlier profiling found 1,646,502 native enum conversions
costing 4.397 s during one Large decode (`native-decoder-profile-1`); this is not a
standalone timing guarantee for memoization or its synchronization overhead.

### 0.4.1: compiled field access

Aggregate native profiling counted 3,311,863 field reads and as many writes in one
Large load. Read/write timings dominated the measured converter operations;
metadata lookup and locking were smaller, so their synchronization remains intact.
Profiling adds overhead: those timings are hotspot evidence, not load benchmarks.

The uninstrumented comparison below held every 0.4.0 patch constant and changed
only field access. Preparation includes real Load-window selection and a concurrent
native player-data save:

| Fixture | Preparation, 0.4.0 / 0.4.1 | In-flight scene transition, 0.4.0 / 0.4.1 | Already-prepared transition, 0.4.0 / 0.4.1 |
|---|---:|---:|---:|
| TruePlanet Large | 29.178 s / 26.723 s | 45.919 s / 44.732 s | 37.702 s / 38.666 s |
| Ordinary Small | 7.299 s / 6.538 s | 14.448 s / 14.226 s | 12.840 s / 12.730 s |

This is an incremental preparation/decoder improvement, not another drastic
reduction in total load time. Ready snapshots bypass field decoding entirely:
the Large ready transition did not improve. Scene teardown and main-thread
reconstruction remain the dominant ready-load costs.

Before-GC whole-process managed-memory increases during preparation were
1,275.4/1,150.9 MiB (Large) and 303.5/260.5 MiB (Small), old/new. These include
temporary JSON and unrelated allocations, not an exact retained-cache budget.
Largest preparation frame gaps were 0.245/0.206 s and 0.229/0.202 s. Background
preparation still causes frame-pacing costs.

A separate same-Load-window Large control observed 164.9 fps idle, 27.7 fps while
preparing and 164.3 fps once ready, with the cap fixed at 165. Its worst preparation
gap was 0.102 s. Closing that window retired the snapshot. This isolates preparation
from merely opening the Load window; it does not promise smooth preparation.

Both fixtures passed six cursor and twenty cache/enum/field checks, with zero game
errors. The seven field checks also passed with original reflection enabled:
private/inherited/nullable fields, boxed-struct mutation, coercion/null resets,
property exceptions, cycles/shared references, partial JSON and readonly assignment.
Terrain/biome/TileBase records and non-generic object records matched before and
after gameplay/save/reload, subject only to the existing native visible-wall GUID
rule. Post-gameplay transitions were 50.612/49.894 s (Large) and 15.896/15.351 s
(Small), old/new. All thirteen protected files were restored byte-identically.

Separate main-menu loads with preloading disabled exercised compiled fields on
the native reader path. All 47,040/19,600 terrain records and 15,101/3,134 object
records matched the original-reader controls, with only the same visible-wall
GUID normalization. Cold loads took 42.989 s (Large) and 15.325 s (Small); these
separate-process observations are compatibility checks, not a speedup claim.
The first two-case pacing runner then refused its own reused fixture filename
on Small, after the cold dump had completed; protected files were still restored.
With case-specific fixture names, the Small control passed cold loading, state
comparison, selection, preparation and snapshot retirement on close with zero game errors.
Its idle/preparing/ready rates were 164.3/30.0/162.4 fps, and all thirteen protected
files were restored byte-identically.

Evidence: `/home/deniz/ruinarch-runs/native-loading-fields-production-1/`,
`native-loading-decoder-metrics-1`, `native-field-access-{red,green}-1`,
and `native-loading-window-pacing-1`. Cold-reader evidence is in
`native-loading-fields-cold-1/cold-comparison.json` and `native-loading-fields-cold-2`.
A throwaway field-write timing
gate failed before the change and passed afterward; it is not a full-load speedup claim.

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
   [ruinarch.performance] Performance Mod v0.4.1: linear tile-object loading on, compiled field access on, enum decoding cache on, selected-save preload on, ...
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
