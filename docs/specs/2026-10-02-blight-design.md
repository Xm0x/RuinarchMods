# The Blight: corruption that spreads by itself

Status: approved by the owner (2026-10-02). Ruinarch+ feature, config flag `blightEnabled`.

## Goal

In the base game the player paints corruption one tile at a time with **Corrupt Tile**, and
it can only grow outward from corruption the player already has, in the wilderness, away from
villages. The Blight turns corruption into a living thing: demonic machines grow it on their
own, the player has several ways to carry it further, it creeps into villages and spoils
their fields, and villagers fight back harder the closer it gets.

## What the game already has (decompiled source)

- **Corruption is per tile** (`GridTileCorruptionComponent`). `StartCorruption` shows smoke and
  corrupts the tile 5 ticks later; corrupting turns block walls to demon stone, removes small
  objects on the tile, and lists the tile in `PlayerSettlement.corruptedTiles`.
  `UncorruptTile` restores the ground. The game saves all of it.
- **Rules for Corrupt Tile** (`BaseBuildingManager.cs` ~380-600): wilderness only, must touch
  existing corruption, never next to a village or special building.
- **Corrupted ground is what the player builds and summons on**: demonic buildings
  (`DemonicStructurePlayerSkill.cs:131-150`), minions (`MinionPlayerSkill.cs:93`) and summons
  (`SummonPlayerSkill.cs:72`).
- **Villagers already clean it**: each village posts at most one `PURIFY_GROUND` job per hour
  (`SettlementJobTriggerComponent.cs:660-668`), starting with corruption at its own borders.
  The villager who takes it cleans up to 20 tiles (`PurifyGroundBehaviour.cs:44`, priority
  630), never next to the Portal. Cleaning a tile also tears down demonic buildings, walls and
  decorations on it (`PurifyGround.cs:29`).
- **Trees on corrupted ground** spawn Corrupt Ents instead of normal Ents (`TreeObject.cs:210`).
- **Nothing spreads by itself today**: no code grows corruption over time.
- **Farms**: crops grow tick by tick (`Crops.PerTickGrowth`) until ripe, then are harvested.
- **Ruinarch+ already has**: per-village knowledge of the player's buildings (Phase 3; parties
  attack only buildings their village knows), rotting bodies (Phase 2), famine, unrest and
  migration that follow a village's health (Phases 4 and 5).

## The design

### 1. Blight Heart (a new demonic building)

- Built from the build menu like any demonic building, on or touching corrupted ground.
  Costs mana; the number of Hearts is limited (3 at first, more as the Portal levels up).
- **Grows its patch**: every hour it corrupts a few tiles at the edge of the corruption it sits
  in, within its reach. New tiles prefer spots with more corrupted neighbours, so the blight
  creeps in organic tongues rather than perfect circles. It does not cross water or
  mountains.
- **Feeds on death**: anyone who dies on its patch (villager, monster, a minion) feeds it. Fed
  enough, it grows a level (3 levels): wider reach, faster growth, more HP. The level is shown
  in its tooltip.
- **When destroyed**, its patch stops growing but stays; villagers still have to clean it.
  Its unused charge returns, as the game does for other demonic buildings.
- Look: borrows an existing demonic building's prefab for now (`PrefabSource`).

### 2. Ways to spread it

- **Blight Seed** (a new spell): plant a small patch on any open ground next to one of the
  player's minions or summons, with no need to touch existing corruption. The patch grows on
  its own for a day and then stops unless joined to a Heart's patch. This is how the blight
  gets a foothold in or beside a village. Needs a new framework API (below).
- **Carriers**: Corrupt Ents and the player's summons slowly corrupt open ground they rest on.
  Blight that swallows a wood sometimes wakes its trees as wild Corrupt Ents, which then roam
  and carry it further.
- **The dead**: an unburied body on or next to corrupted ground corrupts the ground around it
  as it rots (ties to Ruinarch+ rotting bodies). A graveyard the blight reaches becomes a
  source.
- **Cultists**: villagers of the Demon Cult never clean the blight, and a cultist may quietly
  plant a seed in their own village.

### 3. Into the villages

- Unlike Corrupt Tile, the blight may spread onto a village's open ground: streets, yards and
  fields. It never enters the inside of a building.
- Blight in a village destroys nothing but plants: **crops on corrupted ground wither** and
  corrupted fields cannot be replanted until cleaned. Furniture, food piles and buildings
  stay.
- Blight on a village's ground does **not** count for summoning or building: minions,
  summons and demonic buildings still need corrupted ground outside villages, as in the base
  game.
- Villagers who walk on it are frightened (the game's own fear reaction), and the blight
  counts against the village's mood in Ruinarch+ unrest.

### 4. Villagers fight back

Each village measures its **blight pressure**: corrupted tiles on its own ground plus those
within a few tiles of its border. The response escalates:

1. **Notice** (any blight nearby): the game's cleanup job, as today.
2. **Alarm** (a few dozen tiles): several cleanup jobs at once (not one per village), cleaners
   take blight inside the village first, and the village reports the blight to its kingdom.
   Divine Church villagers clean faster.
3. **Purge** (a Heart they know about, or blight on their own fields): the village sends a
   party to destroy the Blight Hearts it knows of (Ruinarch+ knowledge: parties hunt only
   what someone has seen). Parties may **burn** corrupted ground: fire cleanses blight
   faster than hand cleaning, but fire spreads, and can take their own buildings with it.
4. **Despair** (most of its fields lost): failed harvests feed into Ruinarch+ famine, unrest
   rises, and families start to leave (Ruinarch+ migration). A village can empty out.

### 5. The player's view

- Log lines and notifications: a Heart levels up, a village raises the alarm, a purge party
  sets out, a Heart is destroyed, a village is abandoned to the blight.
- The Heart's tooltip shows its level, feeding progress and patch size.
- Each village's tooltip shows its blight pressure level.

## Rules that keep it fair and fast

- **Hard cap per hour** on newly corrupted tiles across the whole map (config), so growth never
  stalls the game.
- Growth works from a kept list of edge tiles per patch, not by scanning the map. Visual edges
  and the minimap are updated in one batch per hour, not per tile.
- New tiles are corrupted at once rather than with the 5-tick smoke delay, so nothing is lost
  if the game is saved mid-way (the game does not save that delay's timer).
- Corruption never spreads onto the Portal's tiles or into building interiors; cleaning rules
  near the Portal stay as in the base game.

## Save data

- Hearts are buildings: the game saves them; their level and feeding go in
  `ModData/ruinarch.plus.blight.json` (`ModSave`).
- Corruption itself is saved by the game. Seed timers, carrier state and village pressure
  levels go in the same blight file.

## Framework and loader work

- **`ModContent.RegisterSpell`** (new): registers a player spell with a stable id-based skill
  type, the way `RegisterStructure` does for buildings. Needed for Blight Seed; future spells
  reuse it. Ships in RuinarchModLoader 0.7.0, which Ruinarch+ then requires.
- No loader change is needed for the Heart (existing `RegisterStructure` with `Skill`).

## Config (`config.json`)

`blightEnabled`, `blightTilesPerHour` (map-wide cap), `blightHeartLimit`, `blightReach` per
level, `blightSeedDays`, `blightCarriers`, `blightFromCorpses`, `blightInVillages`,
`blightFireCleanses`. Off by flag means no Hearts or Seed in the menus and no spreading.

## Release slices

1. **0.11.0**: Blight Heart with growth and levels by feeding, spreading into village ground,
   withering crops, cleanup at scale (Notice and Alarm), fire cleanses, config, save.
2. **0.12.0**: Blight Seed (with loader 0.7.0), carriers, the dead, cultists, Purge parties,
   Despair (famine, unrest, migration), village tooltips.

Each slice: harness suite `BlightSuite` (growth rate and cap, no growth on water/Portal/
interiors, crops wither, cleaners at scale, Heart destroyed stops growth, save and reload
keep levels and patches), then two clean full regressions before release.

## Not in scope

- Province-level blight numbers: that is TruePlanet's summary simulation.
- New art: Hearts borrow an existing demonic look until an own look is made.

## Owner decisions (2026-10-02)

1. Names: Blight, Blight Heart, Blight Seed.
2. Blight inside a village does not count for summoning or building.
3. Fire cleansing is in.
