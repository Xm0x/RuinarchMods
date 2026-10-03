# TruePlanet, part 1: the planet and its provinces

Status: DRAFT for the owner's review (2026-10-03). Nothing is implemented. Choices marked
**(default)** were made while the owner was away and need a yes or a change; the questions
at the end are the ones only the owner can answer.

TruePlanet is a separate mod (id `trueplanet`, namespace `TruePlanet`, folder
`RuinarchMods/TruePlanet/`). It does not need Ruinarch+; both can be installed together.

## Goal

A Ruinarch game today is one map. TruePlanet makes it one province of a planet: many
provinces, each a full Ruinarch map when the player is there, and a summary when they are
not. The player starts in one province and spreads to others through Travel Portals.
Later parts add nations, capitals, religion, language, wars and a 3D globe on this base.

## How TruePlanet splits into parts

TruePlanet is too large for one spec. Each part gets its own spec, plan and release:

| Part | Content | Depends on |
|------|---------|------------|
| **1. Planet and provinces (this spec)** | planet generation, planet screen, provinces as real maps one at a time, Travel Portals, planet save, summary of closed provinces | none |
| 2. Life while away | closed provinces keep changing (population, growth, settlements founded or lost) and the changes are applied when the player returns | 1 |
| 3. Nations | nations owning many provinces, capitals, the settlement ladder (Outpost to Capital, building on Ruinarch+ tiers if present), governments | 1, 2 |
| 4. Religion and language | per nation, with regional drift; language slows how fast news travels | 3 |
| 5. War and diplomacy | levies, borders, wars between nations, treaties (moved here from Ruinarch+ Phase 6) | 3 |
| 6. Calendar | a real date (year, season, month) instead of "Day x" | 1 |
| 7. Globe | the planet screen as a rotating 3D globe (feasibility proven) | 1 |

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
generates only a summary, no maps:

- **Provinces** (default 12, config `provinces`, 6 to 30) as cells of a sphere-like map,
  each with neighbours, a biome mix, a map size (Medium or Large, **(default)**), a seed and
  a planned number of villages and factions within the game's limits for that size.
- **Owners**: each settled province is listed with the factions and villages it will get;
  part 3 groups these into nations. Some provinces are wild (no villages).
- Names for provinces from the game's own name lists.

### 2. Planet screen

A flat map of the provinces (the globe comes in part 7): owner colours, which provinces the
player holds, and for each province its last known summary (villages, population, factions,
the player's buildings there). Opened from the main menu (**Continue planet**) and in game
from a button next to the game's own top-bar buttons. From it the player opens a province
they hold, or picks the starting province of a new planet ("region preview": look before
committing).

### 3. Opening a province

- **First visit**: TruePlanet sets the game's world settings from the province's summary
  (map size, factions, villages, seed) and starts the native New Game flow, so the player
  places their Portal exactly as in a normal game. The new world is saved at once as the
  province's save.
- **Return visit**: TruePlanet saves the open province with the native saver, then loads
  the target province's save with the native loader.
- **Clock**: the planet has one clock. A province reopened after time has passed elsewhere
  has its date moved forward with `SetToday` **(default)**; in part 1 nothing else changes
  while it was closed (part 2 adds that). Open risk: events the game scheduled for dates
  that were skipped (a check in the plan's first task).
- **Before leaving**, TruePlanet records the province's summary (villages, population,
  factions, the player's buildings and minions there) for the planet screen.

### 4. Travel Portals

A demonic building registered through the loader's framework (as Ruinarch+ does with the
Blight Heart), buildable on corrupted ground. Once built, it lists the neighbouring
provinces; choosing one opens that province (first visit: the player places a new Portal
there). A province is "held" while the player's Portal in it stands.

### 5. What travels with the player

**(default)**: mana, Spirit Energy, chaos orbs, and the list of unlocked powers and
buildings are planet-wide: they follow the player into every province. Minions, summons,
demonic buildings and corruption stay in the province where they are. (Moving minions
between provinces is part 2.)

### 6. Losing

Losing the Portal of one province loses that province (it becomes un-held; its save stays
on the planet). The native game-over happens only when the last held province falls.

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
| `provinces` | `12` | number of provinces on a new planet (6 to 30) |
| `provinceSizes` | `["Medium","Large"]` | map sizes provinces are drawn from |
| `wildShare` | `0.25` | share of provinces without villages |

## Release slices for part 1

1. Planet generation, planet screen, start in a chosen province, planet save and continue.
2. Travel Portal, opening and returning to provinces, carried resources, losing one
   province.

## Not in scope for part 1

Closed provinces changing over time, nations, religion, language, wars, the calendar, the
globe, minions crossing provinces, several provinces live at once.

## Questions for the owner

1. **One province live at a time** (approach 1) with a loading screen when switching:
   acceptable? Your earlier note wanted every province with working minions to run in full,
   which only approach 2 gives.
2. **Planet-wide resources** (section 5): mana and unlocks follow the player; minions stay.
   Right split?
3. **Losing** (section 6): one province's Portal lost = that province lost, game over only
   when all are lost. Right?
4. **Planet size** default 12 provinces of Medium or Large maps. A different number or
   size?
5. **Travel Portals only to neighbouring provinces** in part 1, or to any province?
