# RuinarchMods
DISCLAIMER: For 100% honesty, help of AI was used in this project.

Gameplay mods for [Ruinarch](https://store.steampowered.com/app/909320/Ruinarch/),
loaded by the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader) and written
against the decompiled game source in [RuinarchRE](https://github.com/Xm0x/RuinarchRE).

The mods patch the **stock** game at runtime with Harmony. The game's own DLLs are never
replaced or recompiled.

## Mods

| Mod | What it does |
|---|---|
| **[Ruinarch+](RuinarchPlus/README.md)** | The gameplay mod: bug fixes, plus rotting corpses, a Mass Grave village building, a plague curfew that closes a village's borders, factions that only attack what they know about and news that travels on foot and by gossip, villages that search for their missing, migration that follows a village's fortunes, villages that grow into Towns and Cities around a Town Hall, famine and the unrest it brings, hunters and traders. Every feature can be switched off in the game's Settings window, on the Mods tab. |
| **[Performance Mod](RuinarchPerformance/README.md)** | Makes the game faster on big maps and in long games without changing how it plays. Works with or without Ruinarch+. |
| **[TruePlanet](TruePlanet/README.md)** | A separate seeded planet atlas: geography, provinces, nations, capitals, planned settlements and land roads, with native menu inspection and atlas save/load. Atlas only; playable provinces and Travel Portals are not implemented. Independent of Ruinarch+. |
| **RuinarchDebug** | A development tool, not meant for normal play: an in-game debug overlay (spawn, kill, time control, place buildings, dev console) and an unattended test harness that plays scenarios in a real world and reports PASS/FAIL. |

## Art

I don't make sprites or art myself. New buildings are made the way the game makes its
own: a floor, with walls and objects on top, using the game's own tiles and objects (the
Mass Grave is a dirt floor; the Town Hall uses the Tavern's). Only something that is a
thing of its own gets new art, loaded through the modloader's asset support.

## New content

New buildings such as the Mass Grave need a new `STRUCTURE_TYPE`, which Harmony alone
cannot add. That part comes from the `Ruinarch.ModContent` framework in the
RuinarchModLoader repo; see its
[CONTENT_FRAMEWORK.md](https://github.com/Xm0x/RuinarchModLoader/blob/master/docs/CONTENT_FRAMEWORK.md).

## Layout

```
RuinarchPlus/
  RuinarchPlus.cs            entry point: applies the patches, registers the buildings
  Config.cs                  options shown in the Settings window (Mods tab)
  SettingsMigration.cs       moves options from the old config.json into the settings file
  ModBuildings.cs            villagers build Ruinarch+ buildings (shared construction)
  Fixes/                     one Harmony patch class per bug fix
  Phase2/                    death, decay and disease
    CorpseDecay*.cs          unburied bodies rot and disappear; the decay bar
    CorpseDisease.cs         rotting bodies spread plague
    Curfew.cs                plague curfew
    MassGrave*.cs            the Mass Grave: structure, burial, construction, dirt floor
  Phase3/Knowledge*.cs       per-faction list of known demonic structures; what it gates
  Phase3/MissingPersons*.cs  missing residents and search parties
  Phase4/MigrationHealth.cs  migration follows village health
  Phase5/SettlementTiers.cs  Village, Town, City
  Phase5/TownHall.cs         the Town Hall building
  Phase5/Famine.cs           famine: starving villages, people moving away
  Phase7/                    Blight Hearts, spreading corruption, village response and fire
  RuinarchPlus-DESIGN.md     design notes and roadmap
RuinarchPerformance/
  RuinarchPerformance.cs     entry point
  TileObjectListeners.cs     tile objects behind one signal listener
  JobCrimeListeners.cs       finished jobs drop their crime listener
  MinimapRedraw.cs           the minimap redraws only when it changes
  FrameRateCap.cs            the frame rate cap follows the screen or the player's setting
  PerformanceSettings.cs     options shown in the Settings window (Mods tab)
TruePlanet/
  TruePlanet.cs             settings, native names and the main-menu Planet button
  PlanetModel.cs            atlas data and graph/reference validation
  SphereGraph.cs            seeded spherical province graph
  PlanetGenerator.cs        geography, nations, planned villages and land roads
  PlanetStore.cs            independent transactional JSON atlas saves
  PlanetCanvas.cs           cached terrain/political raster and map picking
  PlanetView.cs             native atlas controls, inspector and saved-atlas list
RuinarchDebug/
  DebugMenu.cs               in-game overlay
  AutoTest.cs                unattended in-game test harness
  TemplateSuite.cs           template export, rebuilding, packs and native construction checks
  BlightSuite.cs             blight growth, village rules, crops, feeding, save and fire checks
  PerformanceSuite.cs        Performance Mod checks (signal lists, disconnects, reload)
  ModSettingsSuite.cs        mod settings checks (files, values, the Mods tab)
  SpiritEnergyCostSuite.cs   spirit-energy cost settings, rounding and disabled-cost checks
  TableCannibalSuite.cs      direct/table meat trait and alert parity, food buffs and exclusions
  FrozenVigilantSuite.cs     frozen/thawed Vigilant stealth interception
  HarpyDragonSuite.cs        wild/agitated dragon exclusion and eligible prey
  ExileJurisdictionSuite.cs  cross-faction sentencing, bans, clan preservation and grudges
  CultistRemovalSuite.cs     actual map nameplate after cultist removal, with visual capture
  CurfewVisitSuite.cs        active/queued visit cancellation under curfew and native-rule controls
  CorpseReloadSuite.cs       native save/reload without stale corpse references
  Stock*Suite.cs             Discord-report fixes, eligibility controls and native UI checks
  PlusBridge.cs              reaches Ruinarch+ by reflection (no hard dependency)
tools/
  publish-workshop.sh        uploads a mod as a new version of its Steam Workshop item
  WorkshopPublish/           the one-launch game tool that script installs
ARCHITECTURE.md              how the three repos fit together
```

## Building

Mods are built with `tools/build-mod.sh` from a RuinarchModLoader checkout:

```bash
tools/build-mod.sh /path/to/RuinarchMods/RuinarchPlus "/path/to/Ruinarch/Mods"
```

`tools/run-autotest.sh` in the same repo runs the RuinarchDebug test harness in the game.

During development, run only the affected suites instead of generating a world for a full
regression each time:

```bash
tools/run-autotest.sh 1200 "RecordsSuite,UnrestSuite,DecayTest"
```

The first argument is a wall-clock timeout, not a requested run duration. Checks stop as
soon as their required behavior is observed. `DecayTest` temporarily uses the supported
quarter-day decay setting, verifies all four stages and the decay bar, then restores the
original setting. Missing-persons tests finish when their cases are settled; valid failed
searches still exercise all three attempts and the increasing retry intervals. Burial fixtures
bring an eligible resident home if needed; gossip fixtures start with two factions and
maintain the meeting until the native sighting occurs. The migration corpse check compares
gains with and without one body, so earlier deaths do not invalidate it. Full runs remain
the release gate.

Town construction checks require the village to remain above the configured Town threshold;
population loss is reported as an unavailable fixture, not a missing Town promotion.
Library construction checks use the authored Library's actual footprint, clear competing
placement jobs in the test world, and supply the required materials to the existing native
build job. A real villager must finish construction; the construction check never uses an
instant build as a substitute.
Template fixtures find a connector for the exact exported or resized prefab they place,
not for a randomly selected native look of the same building kind. Placement uses the
native settlement-owned job and an eligible placer, preserving the village's
one-blueprint-job guard. The build fixture keeps its worker rested; sleep is not under test.
Succession checks invoke the native faction-leader ruler reassignment directly, rather than
assuming no unrelated ruler changes occur over six hours. Jailing checks distinguish native
escape rolls, release by friends and release by outsiders, including release before delivery.
Trader delivery checks observe the native cargo deposit in the destination stockpile,
not only the mod's delivery announcement.
Blight cleanup checks allow a native work/sleep cycle and stop on actual cleanup progress;
they do not require sleeping villagers to purify ground overnight.
Stock-rule fixtures exit native character states before destroying markers and cleaning
components; otherwise combat signal listeners can outlive their owner.
No-scatter checks accept a named native Mass Grave burial with no corpse or gravestone.
Migration scaling includes an already-abandoned village's valid zero multiplier.
Missing-person discovery keeps corpse decay disabled only for that fixture, restoring its
previous setting afterward, so delayed party formation does not erase the body under test.
Portal-witness fixtures process native line-of-sight immediately, then move the living
carrier off the Portal's defended ground before advancing the simulation.

Ruinarch+ 0.12.1 qualified with two complete native full runs on 2026-10-06:
run 8 passed 303 checks (0 failed, 25 unavailable fixtures); run 9 passed 316
(0 failed, 22 unavailable fixtures). Both had no game exceptions/errors and preserved
player mod settings byte-identically. Earlier unexplained native termination and Heart
destruction remain recorded as cause-unestablished, not claimed fixed.

## Releasing on the Steam Workshop

Ruinarch+ is also published as Workshop item
[3811868047](https://steamcommunity.com/sharedfiles/filedetails/?id=3811868047), and the
Performance Mod as
[3813240118](https://steamcommunity.com/sharedfiles/filedetails/?id=3813240118). Each
release uploads the new version there too:

```bash
tools/publish-workshop.sh RuinarchPlus 3811868047 "Ruinarch+ 0.12.1: <what changed>"
tools/publish-workshop.sh RuinarchPerformance 3813240118 "Performance Mod 0.3.0: <what changed>"
```

The script builds the mod into a clean folder (the DLL, `mod.json`, `README.md`, an
optional `preview.png`, and any `art/`, `audio/`, `bundles/` or `templates/`, never a player's `config.json`), then starts Ruinarch
through Steam once with a small release tool installed. That tool uploads the folder through
the game's own Steam session, which needs no password, writes the result and quits. Only
the files and the change note change; the title, description, images and visibility stay
as they were set on the item's Steam page. It needs Steam running and logged in as the
item's owner, the game closed, and the RuinarchModLoader checkout next to this repo (or
`RUIN_LOADER_DIR`), built and installed in the game.

For a mod that is not on the Workshop yet, pass `new` instead of an item id. The script
creates a public item titled with the mod's `mod.json` name and tagged `RuinarchModLoader`
(the tag the loader's own Workshop upload uses). Its description says the mod needs the
loader and then gives the `mod.json` description. The new item's id is printed; add it here.

This repo holds source and mod-authored content only: no game binaries or redistributed stock game assets.
