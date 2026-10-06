# TruePlanet 0.1.0

A separate Ruinarch mod that generates, displays and saves a seeded planet atlas.
It works without Ruinarch+. Requires [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader).

This implements **release slice 1** of the [foundation design](../docs/specs/2026-10-03-trueplanet-foundation-design.md): the planet generator and atlas screen. It does not load playable provinces or add Travel Portals. Stock New Game, Load and province gameplay are unchanged.

## Use

1. Open **Planet** in Ruinarch's main menu.
2. Enter a seed and generation options, then click **Generate**.
3. Click the map to inspect a province's terrain, climate, nation, neighbours and planned villages. Unvisited provinces explicitly show planned data, not observed populations or buildings.
4. Switch **Terrain / Political**, **Borders** and **Roads** to inspect the layers.
5. Edit the planet name and click **Save atlas**. Select an entry under **Saved atlases** to reopen it.
6. **Back** returns to the native main menu without loading a game world.

## Generated planet

- Spherical province graph projected onto a flat 2:1 atlas, with oceans, continents, mountain influence, temperature, moisture and grassland/forest/desert/snow mixtures.
- Connected national territories, inhabited land capitals, independent islands and wild provinces without planned villages.
- Human and Elven nations and planned settlements, with names drawn from the game's own tables without consuming its mutable name pools or Unity random state.
- Land-only road routes between settlement provinces, using terrain-weighted shortest paths. Roads are atlas summaries, not physical paths inside a game map.
- Province size plans capped at Large (20x12), ExtraLarge (24x14) or Huge (32x20). These become playable maps only in subsequent foundation slices.

## Options

The atlas controls apply to the next generated planet. Initial defaults come from **Settings > Mods > TruePlanet** and are stored by the loader in `Mods/settings/trueplanet.json`.

| Option | Default | Range |
|---|---|---|
| Provinces | 120 | 30-400 |
| Land percent | 40 | 20-80 |
| Nations | 10 | 2-40 requested; limited by eligible inhabited land |
| Wild land percent | 25 | 0-75 |
| Largest province | Huge | Large, ExtraLarge, Huge |

## Atlas saves

Each atlas is stored beneath the game's native save directory:

```text
Ruinarch Game Saves/TruePlanet/<immutable-id>/planet.json
```

The editable planet name does not determine the directory. JSON stores the generated geography, graph, nations, planned villages and roads. Writes flush a temporary file before replacing the prior JSON. Loads reject unsupported formats, malformed references and invalid geography. Ordinary Ruinarch ZIP saves are separate and are not modified by atlas saving.

## Build and install

From a neighbouring RuinarchModLoader checkout:

```bash
tools/build-mod.sh ../RuinarchMods/TruePlanet
```

Without a second argument this stages the mod under the loader's `build/mod/TruePlanet/`. Copy that folder into the game's `Mods/` folder and restart the game. To build and install directly, pass the game's `Mods/` folder as the second argument. The loader must already be installed.

## Verification

Verified against the actual Steam game under Proton:

- 29 native interaction checks passed, 0 failed, with no game errors: menu insertion/reopening, visible Exit, seeded generation, native-name/random-state preservation, map-event picking, layer controls, save/rename/reload and Back.
- Actual atlas screenshots inspected at 1920x1080 and 1280x720.
- Player mod settings and native save files remained byte-identical after the probe.
- Standalone consumer smoke exercised 18 generated worlds, boundary options, deterministic geography, graph/nation/road invariants, JSON validation and failed-write preservation.

Local evidence: `/home/deniz/ruinarch-runs/trueplanet-probe.log`, `trueplanet-atlas-mods.log`, and `trueplanet-atlas-{menu,terrain,political,small}.png`. Temporary probes were removed after verification.

## Version history

### 0.1.0

Seeded generation, inspectable atlas layers, native menu integration and independent atlas save/load. Playable provinces, Travel Portals, off-map simulation, war, diplomacy and the globe remain outside this release slice.
