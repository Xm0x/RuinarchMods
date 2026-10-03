# TruePlanet, part 1: the planet and its provinces

Status: DRAFT; owner answered the open questions (2026-10-03, recorded under "Owner
decisions"). Nothing is implemented. Remaining choices marked **(default)** were made by the
author and stand unless the owner changes them.

TruePlanet is a separate mod (id `trueplanet`, namespace `TruePlanet`, folder
`RuinarchMods/TruePlanet/`). It does not need Ruinarch+; both can be installed together.

## Goal

A Ruinarch game today is one map. TruePlanet makes it one province of a planet with real
geography (continents, oceans, mountain ranges, climate and biomes), nations with capitals
and borders, and roads between them. Each province is a full Ruinarch map when the player
is there, and a summary when they are not. The player starts in one province and spreads
to others through Travel Portals. Later parts make the planet live: nations grow, build
roads, worship, talk and go to war.

## How TruePlanet splits into parts

TruePlanet is too large for one spec. Each part gets its own spec, plan and release:

| Part | Content | Depends on |
|------|---------|------------|
| **1. Planet and provinces (this spec)** | planet generation with geography, climate, nations, capitals, borders and a road network; planet screen; provinces as real maps one at a time, shaped by the planet (size, biomes, owning nations); Travel Portals; planet save | none |
| 2. Life while away | closed provinces keep changing (population, growth, settlements founded or lost, nations building new roads) and the changes are applied when the player returns | 1 |
| 3. Roads in provinces | the planet's roads become real roads in a province map; villages lay paths and roads; characters prefer walking on them; bridges | 1 |
| 4. Nations alive | the settlement ladder (Outpost to Capital, building on Ruinarch+ tiers if present), governments, a nation acting across provinces | 1, 2 |
| 5. Religion and language | per nation, with regional drift; language slows how fast news travels | 4 |
| 6. War and diplomacy | levies, wars between nations, borders moving, treaties (moved here from Ruinarch+ Phase 6) | 4 |
| 7. The underworld | underground provinces beneath the surface, reached through caves, home to dwarven nations | 1, 3 |
| 8. Calendar | a real date (year, season, month) instead of "Day x" | 1 |
| 9. Globe | the planet screen as a rotating 3D globe (feasibility proven) | 1 |

## What the game already has (decompiled source, RuinarchRE `src/Assembly-CSharp`)

- **A world is one region.** `MapGenerator.InitializeWorld` runs 13 generation steps on a
  shared `MapGenerationData` (`MapGenerator.cs:74-80`). Four of them read the one main
  region directly: inner map, villages, special buildings, monsters
  (`RegionInnerMapGeneration`, `VillageGeneration.cs:19`, `SpecialStructureGeneration.cs:110,153,246`,
  `MonsterGeneration`).
- **Map sizes** (`MapSettings.cs:6-42`): Small 10x10 areas, Medium 16x10, Large 20x12,
  Extra Large 24x14; an area is 14x14 tiles. Starting villages 1 to 4, at most 6 to 14
  villages.
- **The save holds one region** (`WorldMapSave.cs:31-34`): areas, settlements and structures
  come from global databases with no region id; tiles are bare (x, y). Loading builds
  exactly one region and puts every structure on it.
- **Loading another save while playing works natively**: the in-game Load window sets the
  path and calls `UIManager.Instance.optionsMenu.LoadSave()` (`LoadWindow.cs:51-63`).
- **The date can be set**: `GameManager.SetToday(GameDate)` (`GameManager.cs:247`).
- **Probe results (2026-10-03, a temporary in-game probe, since removed)**: a
  second region can be generated, shown and entered inside one world, but cross-region
  travel is a stub, the save cannot hold it, and taking a live region down needs manual
  cleanup of jobs, targets and pathfinding graphs. Every character in every live region
  ticks every frame (`CharacterTickManager.cs:38-66`).
- **Province size, measured (2026-10-03, temporary probe, this machine, game speed 4x)**:

  | World (areas) | Generation | Average fps | Save |
  |---|---|---|---|
  | 24x14 (Extra Large, the game's largest) | 30 s | 131 | 7 s, 3 MB |
  | 32x20 | 2 min 20 s | 52 | 22 s, 6 MB |
  | 40x24 | 5 min | 21 | 31 s, 8 MB |
  | 48x28 | 8 min | 13 | 56 s, 9 MB |

  The map size comes from one method (`MapSettings.GetMapSize`), so any size generates;
  frame rate falls steeply above 32x20. Asking for more villages than fit made the game's
  generator fail and retry forever (11 villages at 40x24).

## Approaches considered

1. **Each province is its own native save; one province is open at a time (default,
   recommended).** Opening a province loads its save with the game's own loader; leaving
   saves it with the game's own saver. A planet file outside the saves holds everything
   above province level. Everything native, and every Ruinarch+ feature, keeps working
   unchanged inside a province, because to the game it is an ordinary world. Cost: a
   loading screen when switching (about as long as loading a save), and only one province
   simulates in full at a time.
2. **One world with several live regions.** Seamless switching and several provinces in
   full detail at once (the owner's earlier "provinces with working minions run in full").
   Needs rewrites of four generation steps, a save bridge for every object type, and
   manual teardown; each missed reference is a crash on return. Highest risk; not chosen
   for part 1. Part 2 can revisit it if switching proves too slow.
3. **Summary only; regenerate a province from its summary on every visit.** Simple, but
   the player's changes (buildings destroyed, corruption, who lives where) are lost on
   leaving. Rejected.

## The design (approach 1)

### 1. Planet generation

A new planet is made from the main menu (**New planet**, next to New Game). TruePlanet
generates only a summary, no maps, in this order:

- **Provinces** (default 120, config `provinces`, 30 to 400): points spread evenly over a
  sphere; each point's surrounding cell is a province with its neighbours.
- **Land and sea**: a handful of drifting plates make continents; height comes from plate
  edges (mountain ranges where plates push together) plus noise. About 40% of provinces are
  land (config `landShare`). Sea provinces are not playable in part 1.
- **Climate and biomes**: temperature from latitude and height, moisture from distance to
  the sea and prevailing winds; each land province gets a mix of the game's biomes
  (grassland, forest, desert, snow) and its share of mountains and water.
- **Size**: each land province gets a map size from its land area: Large (20x12), Extra
  Large (24x14) or Huge (32x20, TruePlanet's own size) **(default)**. Larger sizes wait for
  performance work (see the measurements above).
- **Nations** (default 10, config `nations`): each starts from a capital province on good
  land and grows over land, slowed by mountains, deserts and sea, until the land is shared;
  some provinces stay independent or wild. A nation has a name, emblem, colour, race
  (Human or Elf in part 1) and the game's faction type for that race.
- **Borders** follow province edges. Border provinces may hold villages of two nations.
- **Settlements**: each province gets a planned list of villages (within what its map size
  can hold, so the game's generator never fails) and which nation owns each.
- **Roads**: a road network on the planet connects capitals and large settlements over land
  along the cheapest route (plains cheap, mountains dear, no sea). In part 1 roads exist on
  the planet screen and in the summary; part 3 turns them into roads inside province maps,
  and part 2 lets nations build new ones.
- Names for provinces, nations and capitals from the game's own name lists.

### 2. Planet screen

A flat map of the provinces (the globe comes in part 9): terrain, nation colours, borders,
capitals, roads, which provinces the player holds, and for each province its last known
summary (villages, population, nations, the player's buildings there). Opened from the
main menu (**Continue planet**) and in game
from a button next to the game's own top-bar buttons. From it the player opens a province
they hold, or picks the starting province of a new planet ("region preview": look before
committing).

### 3. Opening a province

- **First visit**: TruePlanet sets the game's world settings from the province's summary
  (map size, biome mix, the planned villages with their nations' factions, seed) and starts
  the native New Game flow, so the player places their Portal exactly as in a normal game.
  Each nation appears as a native faction with the same name, emblem and race in every
  province, so it reads as one nation across the planet. Steering the game's biome choice
  per province needs a patch in its map generation (located in the plan's first task).
  Generation takes 30 s (Large) to over 2 minutes (Huge) once per province; the new world is
  saved at once as the province's save.
- **Return visit**: TruePlanet saves the open province with the native saver, then loads
  the target province's save with the native loader.
- **Clock**: the planet has one clock. A province reopened after time has passed elsewhere
  has its date moved forward with `SetToday` **(default)**; in part 1 nothing else changes
  while it was closed (part 2 adds that). Open risk: events the game scheduled for dates
  that were skipped (a check in the plan's first task).
- **Before leaving**, TruePlanet records the province's summary (villages, population,
  nations, the player's buildings and minions there) for the planet screen.

### 4. Travel Portals

A demonic building registered through the loader's framework (as Ruinarch+ does with the
Blight Heart), buildable on corrupted ground. Once built, it lists every land province of
the planet; choosing one opens that province (first visit: the player places a new Portal
there). A province is "held" while the player's Portal in it stands.

### 5. What travels with the player

Mana, Spirit Energy, chaos orbs, and the list of unlocked powers and
buildings are planet-wide: they follow the player into every province. Minions, summons,
demonic buildings and corruption stay in the province where they are. (Moving minions
between provinces is part 2.)

### 6. Losing

Losing the Portal of one province loses that province (it becomes un-held; its save stays
on the planet). The player can take it back by building a Travel Portal to it again from
a held province: the province reopens from its save and the player places a new Portal.
The native game-over happens only when the last held province falls.

### 7. Saving

A planet is a folder in the game's save folder: `TruePlanet/<planet name>/planet.json` (the
summary, the clock, what the player carries, which province is open) plus one native save
per visited province (`provinces/<id>.zip`). The game's own Load window lists only the
save folder's top level, so province saves do not clutter it. Saving in game saves the
open province and `planet.json` together. The game's autosave must also write to the
planet folder (check in the plan: where `Utilities.autosavePath` is used).

## Config (`config.json`)

| Field | Default | Meaning |
|-------|---------|---------|
| `provinces` | `120` | number of provinces on a new planet (30 to 400) |
| `landShare` | `0.4` | share of the planet that is land |
| `nations` | `10` | nations at the start (2 to 40) |
| `largestProvince` | `"Huge"` | largest province map: `Large`, `ExtraLarge` or `Huge` (32x20) |
| `wildShare` | `0.25` | share of land provinces without villages |

## Release slices for part 1

1. Planet generator (geography, climate, biomes, nations, borders, capitals, roads) and the
   planet screen. No game maps yet; checked by generating many planets and by screenshots.
2. Playable provinces: map size, biome mix and the owning nations' factions taken from the
   planet; start in a chosen province; planet save and continue.
3. Travel Portals: opening and returning to provinces, carried resources, losing one
   province.

## Not in scope for part 1

Closed provinces changing over time, roads inside province maps, nations acting (growing,
trading, building, warring), religion, language, the underworld, the calendar, the globe,
minions crossing provinces, several provinces live at once, provinces larger than 32x20.

## Ideas raised that belong elsewhere (owner, 2026-10-03; assessed from the game source)

These deepen what a single map already has, so by the project's rule they are Ruinarch+
features (or separate mods), not TruePlanet. Recorded here with what the source shows.

- **Doors that lock and can be broken down.** The game already has a door object
  (`DoorTileObject`: open or closed, blocks movement when closed) and per-faction door tags
  in pathfinding (`Faction.pathfindingDoorTag`, `MovementComponent` tag masks). Missing: hit
  points (thin walls have them: wood 250, stone 500, metal 800), a lock and its owner, and
  the actions to open, lock and break a door. Feasible; the work is in making characters
  locked in or out notice it and try to break through.
- **Second floors.** The game has no height at all: one tile per (x, y), one object slot
  per tile, one pathfinding grid. Stacked floors would touch hundreds of places. A feasible
  shape: an upper floor laid out as a closed "pocket" on an unused strip of the same map,
  reached by stairs that move a character there, so it shares the map, its save and its
  pathfinding. Open problems: showing the upper floor when the player looks at the
  building, and letting villagers plan a trip upstairs. Worth a probe before a design.
- **Roads** are TruePlanet part 3 (inside maps) and part 1 (on the planet). Village paths
  today are only decoration (cobble or wood ground inside building prefabs) and do not change
  how characters walk; nothing native lays roads between villages, and there are no bridges.
  Roads that characters prefer need pathfinding penalties per tile, which the game's
  pathfinding supports.
- **The underworld** is TruePlanet part 7. Caves already exist as cellular-automata
  tunnels with stone walls and cave ground tiles, so an underground province can be carved
  the same way. Dwarves exist only as two monsters (Dwarf King, Dwarf Paladin, one sprite
  per animation). Dwarven villagers need race data made at runtime and new art for the
  game's 26 villager classes (the game's class files carry per-race sprite sets: default
  and elven today), which is the largest cost.

## Owner decisions (2026-10-03)

1. One province live at a time, with a loading screen when switching: yes.
2. Mana and unlocks follow the player; minions, buildings and corruption stay: yes.
3. Losing a province's Portal loses only that province; game over when the last falls;
   the player can always rebuild a Portal there.
4. Largest province Huge (32x20) for now. Performance becomes a separate mod
   ("Performance Mod"), investigated first; bigger provinces wait for its results.
5. Travel Portals reach any province, not only neighbours.
