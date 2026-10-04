# Ruinarch+

A bug-fix and gameplay mod for **Ruinarch**, built on the
[RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader). Each change is checked
against the decompiled game source.

It follows the [Ruinarch+ roadmap](RuinarchPlus-DESIGN.md). Shipped so far: the Phase 1
bug fixes, and the first features of Phases 2 to 7 (death and decay, knowledge, gossip and
records, migration, growing settlements, famine, unrest and uprisings, hunting and trade, a
night watch, and spreading blight). These change the game by default; each can be switched off in
the game's Settings window, on the Mods tab.

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
| 10 | Bug | **The village center's Residents tab lists the village.** The tab lists the people who live in that building, and nobody lives in the center (villagers live in dwellings), so it was always empty. On a village center it now shows everyone living in the village; other buildings still show their own household. |
| 11 | Bug | **Your demons no longer blow up your Portal.** The game spares your buildings from your own spells, but not from the elemental explosions your demons set off. When the Portal's defenders fought villagers or monsters right beside it, their poison explosions took up to all of the max HP of everything in range, the Portal included: in a test, one chain destroyed a full-health Portal in 17 seconds and the game was lost. Poison and frozen explosions and chain lightning set off by your side now leave your demonic buildings and their walls alone; villagers and monsters caught in them are hurt as before. A frozen explosion (something frozen and then zapped) counts as your side's when one of your demons did the freezing or the zapping: the game itself never recorded who caused one, and in a test one destroyed a full-health Portal at once. Switch off with `friendlyExplosionsSpareBuildings`. |

## Phase 2: Death, Decay & Disease (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Corpse decomposition** | Bodies left lying unburied now rot over time (Fresh → Bloated → Rotting → Skeletal) and finally **decompose and vanish**, instead of littering the map forever. Anything buried (a grave in a Cemetery, a Mass Grave, or anywhere else) never rots, nor does a Mummified body, and a body being carried pauses. Hover over or select a body to see how far it has gone: the bar above it is full when fresh and empties as it rots. Tunable via `corpseDecayDays`. On by default. |
| **Corpse-borne plague** | Rotting/skeletal unburied bodies inside a settlement sicken the living present, scaled by corpse count. Uses the game's own plague (Quarantined resistance applies). Buried bodies are never infectious. On by default; switch off with `corpseDiseaseEnabled`. |
| **No more scattered graves** | A village with no Cemetery or Cult Temple no longer buries its dead in random spots around the wilderness. Bodies lie where they fell until the village has a Mass Grave. This also applies to villagers in quest parties, who previously buried bodies wherever they stood. Villages with a Cemetery bury their own people there exactly as before; outsiders, monsters and creatures go to the Mass Grave if the village has one. |
| **Mass Grave** | A new village building. When a village has dead nobody else will bury (any body, if it has no Cemetery or Cult Temple; a creature's carcass even if it has one, since villagers never bury animals in a Cemetery), its villagers place a Mass Grave blueprint, gather the wood or stone, and build it themselves, like any other building (it can be damaged and destroyed like one too). It is a bare dirt pit labelled "Mass Grave" on the map, with none of the Cemetery's paving, statues, braziers or basins. A village has at most one. Once it stands, villagers carry every body in the village into it (residents, strangers and creature carcasses; once the village also has a Cemetery, its own people go there instead) and also fetch bodies from the land around the village. Bodies laid in the pit leave no gravestones; the pit just keeps count. Nobody left alive to carry them? After `massGraveFallbackHours` the pit takes nearby bodies itself. |
| **Plague curfew** | When plague breaks out and the ruler answers with a measured response (Quarantine or Exile, rather than Slay or doing nothing), they also put the village under curfew: residents give up their free time (visiting, taverns, wandering) and go home, until the plague event ends. Work goes on, so plague care, burials and food production continue. The ruler and faction leader are exempt. Announced in the event log. |
| **Closed borders** | A village under plague curfew turns visitors away: people from other villages no longer choose it for a visit or to see a friend there, and anyone already on the way gives up and goes home. Traders don't go there either. Raids, rescues and bounty hunts are not visits and still come. Switch off with `closedBordersEnabled`. |

*Starvation is not part of this mod: the base game already kills starving villagers through the Malnourished status.*

## Phase 3: Knowledge & Fog of War (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **They only attack what they know** | In the base game, one villager reporting one of your buildings makes their whole faction "aware of you": their counterattacks head for your whole domain and then march straight on the Portal, even if nobody has ever seen it. Now knowledge lives in people: each villager remembers the buildings they have seen or been told of, and a village knows what its people remember (reported by one of them, or seen by one who got home to tell it). Counterattacks go for the nearest building their village knows and attack only those; the Portal is a target once someone has seen it. When everything they know of is destroyed, they go home. The same goes for rescues and bounty hunts: a villager held, or a criminal hiding, in one of your buildings is only gone after there once their village knows that building (seeing them inside counts); until then their village sends people out to look for your lair. What each villager remembers is stored inside your save file. Your cultists (Demon Worship) are on your side: they keep what they remember, but it does not count for their village, they tell nobody, and they learn nothing new. |
| **News travels on foot** | Seeing one of your buildings is not the same as the village knowing it. A villager who sees it carries the news, and their village learns it only when they get back home alive: kill them on the way and the news dies with them. The faction's other villages learn it only when someone who remembers comes by (a party passing through, a villager on an errand). Until then only the witness and their party act on it. A village right next to your land no longer knows (or counterattacks) what stands next door until one of its people has seen it. No more "X is looking for your location": in the base game that search heads straight for your Portal (its target *is* the Portal), so the searcher always found it or died to your minions trying. A villager held in one of your buildings is now searched for as a missing person, where they were last seen; the search party learns the building only if they see them inside. |
| **Gossip** | Villagers talk. Meeting someone of their own faction, a villager passes on news they have not yet brought home, and may tell someone from another of its villages what they remember (25% per building). Neighbours don't retell what their village already knows, so newcomers are not taught old news. Meeting someone of another faction that is not their enemy, they may tell them about your buildings (25% per building, `gossipChance`). The listener carries it home like a witness, and a faction that hears of you this way becomes aware of you. Enemies don't talk. |
| **Who Knows of You** | A new section in the bookmarks panel, under Major Events, shows what the world knows about you: "Your presence in the region is not known." until someone finds out, then how many factions know of you, what each one knows ("Aurenad know of your Portal and Corrupt Kennel", or "know of you, but not where you are"; hover a line for the full list, with the villages that know each building and how many people there remember it, e.g. "Portal: Mysa (4), Ulric (1)"; click it to open the faction) and who is on their way home with news ("Devon of Aurenad is carrying news of your Portal home"; click to select them, and maybe stop them). Part of `knowledgeEnabled`. |
| **Missing persons** | A village only knows where its people are if it has seen them. A resident none of their people has seen for a day is reported missing (people away at work, such as mining or on a party's quest, told the village where they were going and are not), and the village sends a search party to where they were last seen. The party sweeps the area and frees them, finds them dead, or comes back empty-handed; the village tries again after 24 hours, then 48, and gives up after three failed searches. Villagers who bury one of their own (in a Cemetery or the Mass Grave) have found them dead. Replaces the base game's rescues of captives nobody saw. Announced in the event log. |

## Phase 4: Living Population (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Migration follows a village's fortunes** | Settlers no longer pour into a village in crisis. Nobody moves in during a plague or a famine, while the village is under attack, or once more of its homes stand abandoned than lived in; otherwise each abandoned home (beyond the couple a growing village keeps spare) and each unburied body in the streets halves the pull; and every resident who dies sets the "Incoming Migrants" meter back. A village of ten reduced to one stops drawing settlers. Hover the migration meter to see why. Your Induce Migration power still works as before. |
| **Villagers are born, grow up, grow old and die** | A year is 16 in-game days. Everyone has an age, shown under their name ("Farmer, age 3", "Child, age 0", "elder") and in an Age row on the Info tab of every character's panel ("Unknown" for those without one); villagers already there when a game starts are adults, some of them elders. A woman and her lover of the same race and village may have a child (rarely: about once a year per couple, half as often when food runs short, never in famine); she is expecting for a season (4 days) and the child is born in their home. Children are drawn smaller, don't work, don't fight and are never picked to rule. Humans come of age at 1 and Elves at 3, then take up a trade the game picks for them. Elders die of old age: Humans around 6 (5 to 7), Elves around 18. |
| **Creatures grow old too** | Wild and tamed creatures (animals and living monsters, not undead, demons, golems or your minions) have an age in the same years and die of old age: rabbits, rats and chickens after about 2 years, wolves, boars, pigs, sheep, scorpions and spiders 4, bears, trolls, orcs, goblins and kobolds 6, centaurs, harpies, tritons and mothmen 8, wyverns, wurms and unicorns 12, dragons 40. The young are drawn smaller for the first quarter of their life; the Age row shows "0, young" or "5, elder". Those the world began with have random ages; the ones the game spawns or hatches later are born then. The game already replaces game animals, den beasts and egg layers; the kinds it never replaces (trolls, orcs, goblins, kobolds, centaurs, mothmen, wurms, unicorns, scorpions) have young of their own, about once a year, while their group is smaller than it was. Old age and births go to the event log only. |
| **Memory fades with the people** | What a village knows of you lives in its people, and dies with them. One elder in three grows forgetful ("elder, forgetful" in their panel) and forgets one of your buildings every 3 days. Children and newcomers are not told old news, so once everyone in a village who remembers a building is dead or has forgotten it, the village no longer knows it ("Nobody in Mysa remembers your Portal any more." in the event log), unless someone who remembers comes by and tells it again. Outlive the witnesses and your buildings are forgotten. Needs `knowledgeEnabled`. |
| **Books and Libraries** | Villages keep records of you, on the Book Shelves of their homes (a home without one gets a Book). In their free time, a villager at home who remembers a building of yours that the household's record lacks walks to the shelf and writes it down, an hour's work; a villager who has forgotten something the record names may sit down and read it. Every finished writing or reading shows in the shelf's Logs tab and the villager's ("Jamie wrote of your Portal in a book on the Book Shelf at home."), and a household that starts a record says so in the event log. Once a village is a Town or City its villagers build a **Library** (it looks like a Workshop) whose shelves, or four Books, keep the village's record; villagers go there to write and to read what they have forgotten, though never under curfew. Records only matter through a reader: a village whose witnesses are all dead or forgetful learns you again from its books. Shelves and Books are ordinary objects: burn or break them all and the record is gone; a Library losing its record is announced ("Andorlad's Library was destroyed; its records of your Portal are lost."). Nobody replaces a lost record by themselves: only someone who still remembers can write it again. Needs `knowledgeEnabled`. |

## Phase 5: Settlements & Economy (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Villages grow into Towns and Cities** | A village that reaches 20 people builds a **Town Hall**: its villagers place the blueprint, gather the materials and build it like any other building (it looks like a Tavern; it can be damaged and destroyed). While it stands the village is a **Town**, and a **City** from 40 people. A Town may build 8 more homes and 4 more facilities than a village of its culture, a City 16 and 8, so it keeps growing as settlers arrive. A settlement keeps its rank until it falls to three quarters of the mark, and loses it at once if its Town Hall is destroyed (it builds a new one when it is big enough again). The rank shows under the settlement's name in its panel ("Human Empire Town"), in place of "Village" in the panel of any of its buildings, and in its center's description ("The Town Center has a Message Board..."), and is announced in the event log. A faction that holds more than one village has a **Capital** (its leader's home village when first named): a City whatever its size and whoever rules or lives there, with no Town Hall needed, until it is destroyed (nobody left alive in it). Then it is a village again, and the faction names a new capital. |
| **Famine** | When a third of a village's people have gone starving for half a day, the village is in famine (announced in the event log). Nobody moves in while it lasts, and once a day each starving villager may pack up and move to a free home in another village of their faction that has food. The famine ends when no more than a tenth are starving for half a day. It uses the game's own hunger, so anything that empties the larder counts: lost farmers, a burnt farm, too many mouths. Only the people in the village count: villagers starving in your prisons or lost in the wild don't put a well-fed village into famine. |
| **Unrest and uprisings** | Villages hold their troubles against their ruler: famine, plague, attacks on the village, deaths (not of old age), the dead left unburied, homelessness, criminals walking free, buildings lost, and a ruler most of them dislike. Unrest builds up while any of these last and fades when none do. A restless village says what it blames the ruler for (announced), and every villager thinks a little less of the ruler each day. When it boils over the village rises (at night, once those against the ruler are awake): the villager who thinks least of the ruler leads everyone who dislikes them against the ruler and those who stand by them. How it goes depends on the people: most often a **brawl** (the game's own knockout fights, nobody is killed): knock the ruler out and the leader takes the rule of the village, and of the faction if the ruler led it; knock the rebels out, or hold for half a day, and the ruler stays. A leader who is Evil, a Psychopath, Ruthless or Treacherous (or holds a grudge) may instead **plot an assassination**: they wait for the ruler to fall asleep, stay up and strike; unseen, they take the rule; seen, they are a murderer and wanted, and someone else rules; a ruler who wakes may well kill them. A village with a prison may **jail its ruler**: after the brawl the rebels carry them there, the village leaves them tied (only a friend may free them, though the game lets any prisoner break loose now and then, a Barbarian often), and after two days whoever rules executes, exiles or releases them, by what they think of them. A big village (12 or more adults, four or more on each side) may fall into **civil war**: the two sides fight to the death and the losing side's survivors are exiled. After any uprising the village is calm for a day. Switch off with `unrestEnabled`; `uprisingKindsEnabled` false keeps only the brawl. |
| **Hunters** | A hungry village (in famine, or a fifth of its people starving) sends up to two of its fighters, Hunters first, after wild animals nearby every six hours. They kill and butcher the animal and carry the meat to the village storage (nobody carries a hunter's kill off to the Mass Grave); prey that runs off is chased for up to a day. Bears are left alone. |
| **Traders** | Once a day a village with food to spare sends a trader (a Merchant if it has one) with 40 food to the village that needs it most; the trader walks there and puts it in that village's storage (called away on the road, they pick the food up again later). Never between enemies, and not to or from a village under curfew. Traders also carry news of your buildings both ways: arriving, they tell their hosts everything their faction knows of you, and take home what their hosts know. |

## Phase 6: War & Diplomacy (in progress)

| Feature | What you'll notice |
|---------|--------------------|
| **Night watch** | The game watches its villages by day (the morning patrol) but never at night. Now a Town or City (or a capital) with at least four fighters keeps a night watch: one guard for every eight residents, up to three, its best fighters, never the ruler. Guards sleep by day and at night walk the village ready to fight, so a monster or a demon that slips in after dark meets someone awake. Guards are never sent hunting or trading. A guard who dies, leaves or is needed elsewhere is replaced. Switch off with `nightWatchEnabled`. |

## Phase 7: The Blight

| Feature | What you'll notice |
|---------|--------------------|
| **Blight Heart** | A demonic building in your Build menu, using the Crypt's look and mana cost. Each hour it grows corruption at the edge of its connected patch. Deaths on that patch feed it through three levels, increasing its reach, growth and HP. Its description shows level, feeding and patch size. Destroying the Heart stops its growth but leaves the corruption; its build charge returns. |
| **Village blight** | Corruption can creep onto streets, yards and fields, but not building interiors, walls, water, mountains or the Portal's ground and neighbors. Crops wither and cannot be replanted until cleaned. Other village objects stay. Village blight never provides ground for your summons, minions, demonic buildings or further manual corruption. |
| **Notice and Alarm** | Nearby blight brings cleaners. At 30 affected tiles, a village raises the alarm, tells its kingdom about you and sends several cleaners, prioritizing its own ground. Divine Church villagers purify faster. Blight also adds unrest. |
| **Fire cleanses** | When a fire ends on corrupted ground, the blight is removed there, including when the burning object or tree burns away. Fire retains the game's normal spread and damage. Bare corrupted ground is not itself flammable; burn something on it. Your demonic buildings are not cleansed by this rule. |

Growth shares one map-wide hourly cap. Heart levels and feeding travel inside the game's
save alongside the game's own saved buildings and corruption. Blight Seed, carriers,
corpse spreading and purge parties are not included yet; they are planned for 0.13.0.

## Options

Change the options in the game: open **Settings**, pick the **Mods** tab, then **Ruinarch+**.
A change applies at once, in the menu or during a game. The options are saved in
`Mods/settings/ruinarch.plus.json`.

If you used Ruinarch+ 0.11 or older, your options were in `Mods/RuinarchPlus/config.json`.
The first start of 0.12 moves them to `Mods/settings/ruinarch.plus.json` by itself and renames
the old file `config.json.migrated`, so you keep every value.

The names below are the ones in the settings file. `blightReach` and `blightGrowthPerHour`
are lists, so they are not in the Settings window; change them in the file while the game is
closed.

| Flag | Default | Effect |
|------|---------|--------|
| `disableTutorial` | `false` | Set to `true` to skip the tutorial/alert bootstrap (`TutorialManager.Initialize`). Veterans get no tutorial alert hand-holding. Off = base game unchanged. |
| `closeExploits` | `true` | Close the exploits Ruinarch+ fixes (Sacrifice / Let It Go on a Kennel taking a monster only flying over it). Set `false` to keep them. |
| `friendlyExplosionsSpareBuildings` | `true` | Explosions your side sets off (poison, frozen, chain lightning) leave your demonic buildings alone. Set `false` for the base game. |
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
| `lifeCycleEnabled` | `true` | Villagers age, have children, grow up and die of old age. Set `false` for none (children already born stay, as adults). |
| `lifeDaysPerYear` | `16` | In-game days in a year. |
| `humanAdultYears` / `humanElderYears` / `humanLifespanYears` | `1` / `4` / `6` | Human ages (years) of coming of age, of becoming an elder, and of death by age (give or take a fifth). Other races except Elves use these too. |
| `elfAdultYears` / `elfElderYears` / `elfLifespanYears` | `3` / `12` / `18` | The same for Elves. |
| `birthChancePerDay` | `6` | Percent chance per couple per day to conceive (half with under 20 food per villager, none under 10 or in famine). |
| `pregnancyDays` | `4` | Days from conceiving to the birth. |
| `dementiaChance` | `33` | Percent of villagers who grow forgetful when they become elders. `0` turns dementia off. |
| `dementiaForgetDays` | `3` | A forgetful elder forgets one of your buildings every this many days. |
| `creatureLifeEnabled` | `true` | Creatures age, die of old age and (kinds the game never replaces) have young. Needs `lifeCycleEnabled`. |
| `recordsEnabled` | `true` | Households keep records of your buildings on their Book Shelves, and Towns and Cities build a Library; villagers write and read them in their free time. Needs `knowledgeEnabled`. |
| `readChance` | `25` | Percent per free-time hour that a villager at home reads a record naming something they do not remember. |
| `libraryVisitChance` | `10` | Percent per free-time hour that a villager goes to the Library to write what it lacks or read what they do not remember. |
| `libraryBooks` | `4` | Books placed in a Library that has no Book Shelf. |
| `settlementTiersEnabled` | `true` | Villages that grow build a Town Hall and become Towns and Cities, with room for more buildings. Set `false` for vanilla. |
| `townPopulation` | `20` | Living villagers a village needs to build a Town Hall and become a Town. |
| `cityPopulation` | `40` | Living villagers a Town needs to become a City. |
| `famineEnabled` | `true` | Villages notice when their people starve: famine, no settlers, starving villagers moving away. Set `false` for vanilla. |
| `famineHours` | `12` | Hours a third of the villagers must be starving before a famine (and a tenth or fewer before it ends). |
| `famineLeaveChance` | `25` | Percent chance, per starving villager per day of famine, to move to a village of their faction with food. |
| `unrestEnabled` | `true` | Villages grow restless over their troubles and rise against their ruler. Set `false` for none. |
| `unrestRestless` | `24` | Unrest at which a village is restless. Each trouble adds per hour: famine, plague, an attack, a disliked ruler 1; each recent death or lost building, the unburied dead, homelessness, free criminals 0.5. With no trouble it falls by 1 an hour. |
| `unrestUprising` | `72` | Unrest at which a village rises against its ruler. |
| `uprisingKindsEnabled` | `true` | An uprising may be an assassination plot, jailing the ruler (with a prison) or a civil war (in a big village), depending on the people. Set `false` for the brawl only. |
| `huntingEnabled` | `true` | Hungry villages send fighters to hunt wild animals and bring the meat home. |
| `huntersPerTrip` | `2` | Most villagers a village sends hunting at once (every 6 hours). |
| `tradeEnabled` | `true` | Villages with food to spare send traders to villages that need it. |
| `tradeAmount` | `40` | Food one trader carries. |
| `nightWatchEnabled` | `true` | Towns and Cities keep a night watch of their fighters. Set `false` for none. |
| `blightEnabled` | `true` | Enables Heart construction, spreading blight and the escalated village response. Existing buildings and corruption remain when switched off. |
| `blightTilesPerHour` | `60` | Maximum new tiles corrupted per hour across all Hearts. |
| `blightHeartLimit` | `3` | Maximum simultaneous Hearts under normal charge rules. |
| `blightReach` | `[6,9,13]` | Reach in tiles for each of the three Heart levels. |
| `blightGrowthPerHour` | `[3,5,8]` | Maximum tiles each Heart adds per hour, before the shared cap. |
| `blightFeedPerLevel` | `4` | Deaths needed for level 2; twice this number for level 3. |
| `blightInVillages` | `true` | Allows blight to spread onto village open ground. |
| `blightFireCleanses` | `true` | Fires cleanse corrupted ground when they end. |
| `corpseDiseaseEnabled` | `true` | Rotting corpses in a settlement spread plague to nearby villagers. Requires `corpseDecayEnabled`. |
| `corpseDiseaseChancePerCorpse` | `3` | Percent infection chance, per rotting corpse, per in-game hour, per nearby villager. |

The Settings window keeps every number inside the range its slider allows; the settings file
is held to the same ranges when the game starts.

## Install

1. Install the [RuinarchModLoader](https://github.com/Xm0x/RuinarchModLoader/releases), **v0.8.0 or newer**. Ruinarch+ 0.12.0 puts its options in the game's Settings window, which needs loader 0.8.0; older loaders refuse to load it. Its installer patches your local `Assembly-CSharp.dll` with the loader startup call and installs the framework in `Mods/`. If you already use loader 0.6.0 or newer, the main-menu Update notice can install 0.8.0.
2. Either subscribe to [Ruinarch+ on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3811868047) (Steam keeps it up to date), or download `RuinarchPlus-<version>.zip` from the [releases](https://github.com/Xm0x/RuinarchMods/releases) and unzip it into your game's `Mods/` folder, so you get `Mods/RuinarchPlus/`. Use one or the other: when both are present, the local copy wins.
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

## Version history

- **0.12.0**: options in the game's Settings window (Mods tab), saved in Mods/settings/; the
  old config.json moves there by itself. Requires loader 0.8.0.
- **0.11.0**: Blight Hearts, death-fed levels, spreading corruption, withering fields,
  village Notice and Alarm, unrest and fire cleansing. Requires loader 0.7.0, which also
  fixes saves with a Library, Town Hall or Mass Grave that stopped loading partway
  (older saves load again).
- **0.10.1**: package manifest compatible with loader 0.6.0 and Steam Workshop distribution.

## Notes

- Ships **no game code or assets**, source only. You need your own copy of Ruinarch.
- Ruinarch and its assets belong to their respective owners; this is a fan-made mod, not affiliated with or endorsed by them.
