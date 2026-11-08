# TruePlanet

Play a Ruinarch campaign across a whole planet. Generate a globe of nations,
settlements, roads, and wilderness, choose where to begin, and travel between
local maps as you subjugate the world.

Your lord, Portal upgrades, currencies, and skill progression follow you.
Each place keeps its own villagers, buildings, minions, and damage when you leave.
TruePlanet works without Ruinarch+.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3819054263)
| [Download 0.4.0](https://github.com/Xm0x/RuinarchMods/releases/tag/trueplanet-v0.4.0)

![TruePlanet globe with nations, settlement names, and roads](../assets/readme/trueplanet-globe.webp)

## Install

Requires [RuinarchModLoader 0.9.0 or newer](https://github.com/Xm0x/RuinarchModLoader).
Install the loader first, subscribe to TruePlanet on Steam Workshop, then restart
Ruinarch. The main-menu **Mods** window should list TruePlanet.

For a manual installation, download `TruePlanet-0.4.0.zip` from the release
and extract its `TruePlanet` folder into the game's `Mods/` folder instead
of subscribing on Workshop.

## Start a campaign

1. Choose **New Game** to open the globe.
2. Open **World settings** to choose a seed, land percentage, nations, and map size.
   Use **Re-generate** to try another world.
3. Drag to rotate the globe and scroll to zoom. Click land to inspect it, then
   click **Play** to begin there. Wilderness is playable too; oceans are not.
4. Place your Portal and choose your lord and starting loadout as in a normal game.

The same seed and world settings produce the same planet. A planet's local
map size applies everywhere in that world; it cannot be changed partway through
its campaign. Capitals have larger settlements than ordinary villages.

## Explore the atlas

Open **Planet atlas**, beside **Achievements**, during play. Select another
land tile and click **Travel**. Your current map is saved before you leave.
Return to a place to continue with its existing inhabitants and buildings.

Your initial lord and loadout choices apply throughout the campaign. On first
visits to other maps, your Portal is placed automatically. Portal level,
upgrade choices, currencies, and portable skill progression travel with you.
Villagers, local jobs, minions, corruption, and local time stay on their own maps.

Use **Globe / Map** to switch between the globe and a flat map. Dragging pans
the flat map. **Layers** lets you show terrain, national colors, borders,
roads, and the tile grid. **Reset view** returns to your selected location,
or to your current campaign location if nothing is selected.
Press **Escape** to close an open panel, then the atlas.

![Flat map view of the same planet](../assets/readme/trueplanet-map.webp)

## Win the campaign

Subjugate every settled tile on the planet. The objective tracks your progress
and the opposition at your current location. Wilderness does not count as a target.

You do not have to kill villagers who worship you or belong to allied factions.
A place becomes contested again if opposition returns. Your progress is kept
when you save, load, or travel.

If a Portal is destroyed, you can continue while you hold a Portal elsewhere.
Return to the lost location to replace its Portal. Losing your final held
Portal ends the campaign.

## Settings and saves

**World settings > Advanced** controls province count, tile detail, land
percentage, nation count, wilderness, and local map size. **Standard** tile
detail uses fewer planet tiles than **Detailed** and is the lighter option.
These choices apply when generating a world.

Set your defaults under **Settings > Mods > TruePlanet**.
**Save world / Load world** saves or opens a planet before starting play.
After starting a campaign, use Ruinarch's normal save and load controls.

## Before you install

In version 0.4.0, only the local map you are viewing runs. Other maps pause
until you return. Travel has a loading screen; Travel Portal buildings and
raids into maps you are not viewing are not available in this version.
Large local maps and the Detailed grid require more resources.

Older province-based worlds keep their original maps and victory rules.
Do not open a campaign saved with TruePlanet 0.3.1 or later in version 0.3.0;
that older version cannot preserve its planet-wide conquest progress.

Report problems through [GitHub issues](https://github.com/Xm0x/RuinarchMods/issues).
Include your TruePlanet and loader versions, what you were doing, and whether
the problem happens in a new campaign or an existing save.

TruePlanet is an independent fan-made mod. Ruinarch and its assets belong to
their respective owners.
