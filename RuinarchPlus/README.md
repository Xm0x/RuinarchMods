# Ruinarch+

A bug-fix and gameplay mod for **Ruinarch**, built on the
[RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader). Each change is checked
against the decompiled game source.

It follows the [Ruinarch+ roadmap](RuinarchPlus-DESIGN.md). Shipped so far: the Phase 1
bug fixes, and the first features of Phases 2 to 5 (death and decay, knowledge and gossip,
migration, growing settlements, famine, hunting and trade). These change the game by default; each one can be switched off in
`config.json`.

## What it fixes

| # | Type | What you'll notice |
|---|------|--------------------|
| 1 | Bug | **Trait tooltips no longer show developer debug text.** The shipped build appended `GetTestingData()` (internal "Responsible Characters / gained-from" text) to every trait tooltip. Removed. |
| 2 | Bug | **Brainwash no longer offers Stalker-class prisoners.** It used to let you queue Brainwash on a Stalker, then always fail silently. Now the option is correctly unavailable. |
| 3 | Bug | **Paralyzed / Quarantined villagers can no longer be schemed into acting.** Scheme validation skipped the `canPerform` gate that the AI planner respects, so incapacitated targets could still be driven to act. Now they can't. |
| 4 | Exploit | **Infinite chaos-orb kennel closed.** "Drain Spirit" granted a chaos orb *before* applying drain damage, so an immortal target (a hibernating golem is `Indestructible`) produced an orb every tick forever. The tick now no-ops when the target can't actually be damaged. |
| 5 | Bug | **Released prisoners no longer reveal your Portal.** Freed captives were dropped on a tile beside the prison (inside your base, next to the Portal) then walked off to report it. They're now knocked **Unconscious** and relocated to their **home structure** instead. |
| 6 | Bug | **The selected character's path no longer vanishes when zoomed out.** The path line is drawn a fixed width on the map, so zoomed out it became thinner than a pixel. It now never gets thinner than 2 pixels on screen; at normal zoom it looks exactly as before. |
| 7 | Exploit | **Sacrifice and Let It Go no longer take a monster that is only flying over a Kennel.** Cast on the monster, both refuse a flying monster that isn't Restrained: it is not held, just passing over. Cast on the Kennel, they didn't check, so a monster that broke its restraints and flew above its Kennel could still be sacrificed for Chaos Orbs. The Kennel now applies the same rule. Switch off with `closeExploits`. |
| 8 | Bug | **The Snatch window always offers somewhere to drop your catch.** It only listed bookmarked buildings, so with nothing bookmarked no drop-off could be chosen and the Snatch button stayed greyed out. When you snatch a character and no bookmark will do, it now lists your own demonic buildings. |
| 9 | Bug | **Big fires no longer drop the frame rate to a crawl.** Every hit on a burning wall made the whole building recalculate its pathfinding, so a burning village kept the pathfinder busy for half of every frame (9 fps at 4x speed in a test with a village on fire, 18 with the fix). A wall that is damaged but still standing blocks the way exactly as before, so it now only recalculates when a wall breaks. |

## Phase 2: Death, Decay & Disease (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Corpse decomposition** | Bodies left lying unburied now rot over time (Fresh → Bloated → Rotting → Skeletal) and finally **decompose and vanish**, instead of littering the map forever. Anything buried (a grave in a Cemetery, a Mass Grave, or anywhere else) never rots, nor does a Mummified body, and a body being carried pauses. Hover over or select a body to see how far it has gone: the bar above it is full when fresh and empties as it rots. Tunable via `corpseDecayDays`. On by default. |
| **Corpse-borne plague** | Rotting/skeletal unburied bodies inside a settlement sicken the living present, scaled by corpse count. Uses the game's own plague (Quarantined resistance applies). Buried bodies are never infectious. On by default; switch off with `corpseDiseaseEnabled`. |
| **No more scattered graves** | A village with no Cemetery or Cult Temple no longer buries its dead in random spots around the wilderness. Bodies lie where they fell until the village has a Mass Grave. Villages with a Cemetery bury their own people there exactly as before; outsiders, monsters and creatures go to the Mass Grave if the village has one. |
| **Mass Grave** | A new village building. When a village has dead nobody else will bury (any body, if it has no Cemetery or Cult Temple; a creature's carcass even if it has one, since villagers never bury animals in a Cemetery), its villagers place a Mass Grave blueprint, gather the wood or stone, and build it themselves, like any other building (it can be damaged and destroyed like one too). It is a bare dirt pit labelled "Mass Grave" on the map, with none of the Cemetery's paving, statues, braziers or basins. A village has at most one. Once it stands, villagers carry every body in the village into it (residents, strangers and creature carcasses; once the village also has a Cemetery, its own people go there instead) and also fetch bodies from the land around the village. Bodies laid in the pit leave no gravestones; the pit just keeps count. Nobody left alive to carry them? After `massGraveFallbackHours` the pit takes nearby bodies itself. |
| **Plague curfew** | When plague breaks out and the ruler answers with a measured response (Quarantine or Exile, rather than Slay or doing nothing), they also put the village under curfew: residents give up their free time (visiting, taverns, wandering) and go home, until the plague event ends. Work goes on, so plague care, burials and food production continue. The ruler and faction leader are exempt. Announced in the event log. |
| **Closed borders** | A village under plague curfew turns visitors away: people from other villages no longer choose it for a visit or to see a friend there, and anyone already on the way gives up and goes home. Traders don't go there either. Raids, rescues and bounty hunts are not visits and still come. Switch off with `closedBordersEnabled`. |

*Starvation is not part of this mod: the base game already kills starving villagers through the Malnourished status.*

## Phase 3: Knowledge & Fog of War (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **They only attack what they know** | In the base game, one villager reporting one of your buildings makes their whole faction "aware of you": their counterattacks head for your whole domain and then march straight on the Portal, even if nobody has ever seen it. Now each faction remembers which of your buildings it actually knows about (reported by a villager, or seen by one of its people who got home to tell it). Counterattacks go for the nearest building they know and attack only those; the Portal is a target once someone has seen it. When everything they know of is destroyed, they go home. The same goes for rescues and bounty hunts: a villager held, or a criminal hiding, in one of your buildings is only gone after there once their faction knows that building (seeing them inside counts); until then their village sends people out to look for your lair. What each faction knows is stored inside your save file. |
| **News travels on foot** | Seeing one of your buildings is not the same as the faction knowing it. A villager who sees it carries the news, and their faction learns it only when they get back to one of its villages alive: kill them on the way and the news dies with them. Until then only the witness and their party act on it. A village right next to your land no longer knows (or counterattacks) what stands next door until one of its people has seen it. No more "X is looking for your location": in the base game that search heads straight for your Portal (its target *is* the Portal), so the searcher always found it or died to your minions trying. A villager held in one of your buildings is now searched for as a missing person, where they were last seen; the search party learns the building only if they see them inside. |
| **Gossip** | Villagers talk. Meeting someone of their own faction, a villager passes on news they have not yet brought home. Meeting someone of another faction that is not their enemy, they may tell them about your buildings (25% per building, `gossipChance`). The listener carries it home like a witness, and a faction that hears of you this way becomes aware of you. Enemies don't talk. |
| **Who Knows of You** | A new section in the bookmarks panel, under Major Events, shows what the world knows about you: "Your presence in the region is not known." until someone finds out, then how many factions know of you, what each one knows ("Aurenad know of your Portal and Corrupt Kennel", or "know of you, but not where you are"; hover a line for the full list, click it to open the faction) and who is on their way home with news ("Devon of Aurenad is carrying news of your Portal home"; click to select them, and maybe stop them). Part of `knowledgeEnabled`. |
| **Missing persons** | A village only knows where its people are if it has seen them. A resident none of their people has seen for a day is reported missing (people away at work, such as mining or on a party's quest, told the village where they were going and are not), and the village sends a search party to where they were last seen. The party sweeps the area and frees them, finds them dead, or comes back empty-handed; the village tries again after 24 hours, then 48, and gives up after three failed searches. Villagers who bury one of their own (in a Cemetery or the Mass Grave) have found them dead. Replaces the base game's rescues of captives nobody saw. Announced in the event log. |

## Phase 4: Living Population (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Migration follows a village's fortunes** | Settlers no longer pour into a village in crisis. Nobody moves in during a plague or a famine, while the village is under attack, or once more of its homes stand abandoned than lived in; otherwise each abandoned home (beyond the couple a growing village keeps spare) and each unburied body in the streets halves the pull; and every resident who dies sets the "Incoming Migrants" meter back. A village of ten reduced to one stops drawing settlers. Hover the migration meter to see why. Your Induce Migration power still works as before. |

## Phase 5: Settlements & Economy (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Villages grow into Towns and Cities** | A village that reaches 20 people builds a **Town Hall**: its villagers place the blueprint, gather the materials and build it like any other building (it looks like a Tavern; it can be damaged and destroyed). While it stands the village is a **Town**, and a **City** from 40 people. A Town may build 8 more homes and 4 more facilities than a village of its culture, a City 16 and 8, so it keeps growing as settlers arrive. A settlement keeps its rank until it falls to three quarters of the mark, and loses it at once if its Town Hall is destroyed (it builds a new one when it is big enough again). The rank shows under the settlement's name in its panel ("Human Empire Town"), in place of "Village" in the panel of any of its buildings, and in its center's description ("The Town Center has a Message Board..."), and is announced in the event log. The home village of a faction's leader is its **Capital** once the faction holds more than one village. |
| **Famine** | When a third of a village's people have gone starving for half a day, the village is in famine (announced in the event log). Nobody moves in while it lasts, and once a day each starving villager may pack up and move to a free home in another village of their faction that has food. The famine ends when no more than a tenth are starving for half a day. It uses the game's own hunger, so anything that empties the larder counts: lost farmers, a burnt farm, too many mouths. |
| **Unrest** | A famine that drags on turns the village against its ruler. After a day it is restless (announced) and every villager thinks a little less of the ruler each day; after three days the villager who thinks least of them takes the rule of the village. If the ruler is also the leader of their faction, they are overthrown as leader too (the game always puts a faction leader back in charge of their home village). Once per famine. Switch off with `unrestEnabled`. |
| **Hunters** | A hungry village (in famine, or a fifth of its people starving) sends up to two of its fighters, Hunters first, after wild animals nearby every six hours. They kill and butcher the animal and carry the meat to the village storage; prey that runs off is chased for up to a day. Bears are left alone. |
| **Traders** | Once a day a village with food to spare sends a trader (a Merchant if it has one) with 40 food to the village that needs it most; the trader walks there and puts it in that village's storage (called away on the road, they pick the food up again later). Never between enemies, and not to or from a village under curfew. Traders also carry news of your buildings both ways: arriving, they tell their hosts everything their faction knows of you, and take home what their hosts know. |

## Optional QOL (config)

On first launch the mod writes **`config.json`** next to `RuinarchPlus.dll` in
your `Mods/RuinarchPlus/` folder:

```json
{
    "disableTutorial": false,
    "corpseDecayEnabled": true,
    "corpseDecayDays": 3,
    "massGraveBurialEnabled": true,
    "massGraveFallbackHours": 12,
    "curfewEnabled": true,
    "knowledgeEnabled": true,
    "missingPersonsEnabled": true,
    "missingAfterHours": 24,
    "searchSweepHours": 6,
    "searchRetryHours": 24,
    "searchMaxAttempts": 3,
    "migrationHealthEnabled": true,
    "settlementTiersEnabled": true,
    "townPopulation": 20,
    "cityPopulation": 40,
    "famineEnabled": true,
    "famineHours": 12,
    "famineLeaveChance": 25,
    "corpseDiseaseEnabled": true,
    "corpseDiseaseChancePerCorpse": 3
}
```

| Flag | Default | Effect |
|------|---------|--------|
| `disableTutorial` | `false` | Set to `true` to skip the tutorial/alert bootstrap (`TutorialManager.Initialize`). Veterans get no tutorial alert hand-holding. Off = base game unchanged. |
| `closeExploits` | `true` | Close the exploits Ruinarch+ fixes (Sacrifice / Let It Go on a Kennel taking a monster only flying over it). Set `false` to keep them. |
| `corpseDecayEnabled` | `true` | Unburied corpses rot and eventually decompose away. Set `false` for vanilla (corpses persist forever). |
| `corpseDecayDays` | `3` | In-game days an unburied corpse takes to fully decompose (480 ticks/day; floor 1/4 day). |
| `massGraveBurialEnabled` | `true` | Villages without a Cemetery/Cult Temple stop scattering graves, build a Mass Grave when they have unburied dead, and carry all bodies (including creatures) into it. Set `false` for vanilla burial. |
| `massGraveFallbackHours` | `12` | In-game hours a body near a Mass Grave may go un-carried before the pit takes it directly. |
| `curfewEnabled` | `true` | A ruler who answers a plague outbreak with Quarantine or Exile also orders residents home in their free time until the plague event ends. Set `false` for vanilla. |
| `closedBordersEnabled` | `true` | A village under curfew turns visitors and traders away. Set `false` to let them in. |
| `knowledgeEnabled` | `true` | Factions attack only the demonic buildings they know about; counterattacks no longer march on a Portal nobody has seen. Set `false` for vanilla. |
| `gossipChance` | `25` | Percent chance, per building, that a villager tells someone of another (non-hostile) faction about a demonic building they know of. `0` turns gossip off. |
| `missingPersonsEnabled` | `true` | Villages search for residents they have not seen, where they were last seen, instead of always knowing where a captive is. Set `false` for vanilla rescues. |
| `missingAfterHours` | `24` | Unseen hours before a resident is reported missing. |
| `searchSweepHours` | `6` | Hours a search party sweeps around the last-seen spot. |
| `searchRetryHours` | `24` | Wait after the first failed search; doubles after each failure. |
| `searchMaxAttempts` | `3` | Failed searches before the village gives the person up. |
| `migrationHealthEnabled` | `true` | Plague, sieges, abandoned homes, unburied dead and deaths hold migration back. Set `false` for vanilla migration. |
| `settlementTiersEnabled` | `true` | Villages that grow build a Town Hall and become Towns and Cities, with room for more buildings. Set `false` for vanilla. |
| `townPopulation` | `20` | Living villagers a village needs to build a Town Hall and become a Town. |
| `cityPopulation` | `40` | Living villagers a Town needs to become a City. |
| `famineEnabled` | `true` | Villages notice when their people starve: famine, no settlers, starving villagers moving away. Set `false` for vanilla. |
| `famineHours` | `12` | Hours a third of the villagers must be starving before a famine (and a tenth or fewer before it ends). |
| `famineLeaveChance` | `25` | Percent chance, per starving villager per day of famine, to move to a village of their faction with food. |
| `unrestEnabled` | `true` | A long famine makes the village restless and, in the end, costs the ruler the rule of the village. Set `false` for none. |
| `unrestHours` | `24` | Hours of famine before the village is restless and starts thinking less of its ruler each day. |
| `challengeHours` | `72` | Hours of famine before the ruler is challenged and replaced. |
| `huntingEnabled` | `true` | Hungry villages send fighters to hunt wild animals and bring the meat home. |
| `huntersPerTrip` | `2` | Most villagers a village sends hunting at once (every 6 hours). |
| `tradeEnabled` | `true` | Villages with food to spare send traders to villages that need it. |
| `tradeAmount` | `40` | Food one trader carries. |
| `corpseDiseaseEnabled` | `true` | Rotting corpses in a settlement spread plague to nearby villagers. Requires `corpseDecayEnabled`. |
| `corpseDiseaseChancePerCorpse` | `3` | Percent infection chance, per rotting corpse, per in-game hour, per nearby villager. |

Edit the file and relaunch for changes to take effect.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader/releases), **v0.4.0 or newer** (older versions cannot store Ruinarch+'s data in your save). Its installer patches your `Assembly-CSharp.dll` and puts `0Harmony.dll` and `Ruinarch.ModContent.dll` (which Ruinarch+ needs) in `Mods/`.
2. Download `RuinarchPlus-<version>.zip` from the [releases](https://github.com/Xm0x/RuinarchMods/releases) and unzip it into your game's `Mods/` folder, so you get `Mods/RuinarchPlus/`.
3. Launch. Check `Mods/mods.log`; you should see a line like:
   ```
   [ruinarch.plus] Applied N patch class(es) over M method(s): ...
   ```

## Build from source

Requires the RuinarchModLoader checkout (for `tools/build-mod.sh`, the modding
API, and Harmony) and a legit Ruinarch install it can reference.

```bash
# from your RuinarchModLoader checkout:
tools/build-mod.sh /path/to/RuinarchMods/RuinarchPlus
# -> build/mods/RuinarchPlus/RuinarchPlus.dll (pass your game's Mods/ folder as a
#    second argument to install it there instead)
```

Each fix is one `[HarmonyPatch]` class under `Fixes/`, with a comment citing the
exact game method and behaviour it corrects.

## Notes

- Ships **no game code or assets**, source only. You need your own copy of Ruinarch.
- Ruinarch and its assets belong to their respective owners; this is a fan-made mod, not affiliated with or endorsed by them.
