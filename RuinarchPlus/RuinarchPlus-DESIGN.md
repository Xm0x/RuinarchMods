# Ruinarch+: Design & Roadmap

*A phased design for the Ruinarch+ mod, checked against the decompiled
source (`RuinarchRE/src/Assembly-CSharp`). Every "EXISTS" claim below cites a real
class. Difficulty tags: **S** (hours), **M** (a day), **L** (multi-day system),
**XL** (bigger than the base feature it touches).*

The later phases are plans, not commitments; they may be cut or reordered.

---

## 0. The core idea

**Ruinarch+** is one umbrella mod built on the ModLoader + Harmony. Two design laws:

1. **Connect what exists before building new.** The game already has burial,
   graveyards, a full plague and transmission system, quarantine, farming, hunger and
   migration, but they barely interact. Many of the target features are connections
   between those systems.
2. **Every phase ships on its own and feeds the next.** No finished fix waits on a
   large system.

The whole vision has one **realism spine**: each link is a system that already exists
or that the mod adds, feeding the next:

```mermaid
graph LR
  Death --> Decay --> Disease --> Quarantine
  Disease --> Population
  Knowledge --> SearchParties
  Knowledge --> Trade
  Population --> Settlements --> Economy --> War --> Diplomacy
  Diplomacy --> World[TruePlanet: nations, religion, language, planet map]
  Trade --> Economy
```

Because the map is small and the base game is character-scale, the **civilization-scale**
end of the vision (planet map, nations, capitals, travel portals) is split into a
**separate sister mod, `TruePlanet`** (Phase 7). Ruinarch+ stays a "deepen what's here"
mod; TruePlanet is the "replace the world" mod. They're designed to stack.

---

## 1. Release ladder (overview)

| Phase | Name | Theme | Net difficulty | Ships |
|---|---|---|---|---|
| **1** | **Ruinarch+ Core** | Bugfixes + light QOL | **S to M** | shipped |
| **2** | **Death, Decay & Disease** | corpses rot, mass grave, corpse-borne plague, curfews | **M to L** | wires existing systems |
| **3** | **Knowledge & Fog of War** | villagers only know what they've seen; gossip; search parties; portal secrecy | **L** | new knowledge model |
| **4** | **Living Population** | birth, aging, natural death, dementia/knowledge-loss, migration rework | **L to XL** | mostly net-new |
| **5** | **Settlements & Economy** | growth tiers, famine, unrest, hunters, traders (all shipped) | **L to XL** | new progression + economy |
| **6** | **War & Diplomacy** | training grounds, standing armies, real wars, curfews/borders | **L** | extends warfare |
| **7** | **TruePlanet** (sister mod) | planet-scale world, nations & capitals, religion + language, travel portals | **XL** | separate mod |

---

## 2. PHASE 1: Ruinarch+ Core

Self-contained. Each fix is independently verifiable in-game. All of Phase 1 below
(2a to 2c) has shipped.

### 2a. Bugfixes: confirmed in source

| Fix | Symptom | Root cause (file:line) | Patch |
|---|---|---|---|
| **Tooltip debug leak** | Every trait tooltip (wells, chars...) shows dev text "Responsible Characters... / Is gained from stealth..." | `TraitItem.cs:24` appends `trait.GetTestingData()` | Transpile/postfix `TraitItem.OnHover` to drop the debug append |
| **Stalker brainwash dead-end** | Brainwash can be queued on a Stalker; it silently never works | `PrisonCell.IsValidBrainwashTarget:349-352` lacks the Stalker check that only exists in `WasBrainwashSuccessful:223` | Postfix `IsValidBrainwashTarget` returns false for Stalker |
| **Paralyzed can't be schemed** | Paralyzed villagers can still leave faction/home but can't be targeted by schemes | `GoapPlanner.cs:156` exempts Paralyzed; `SchemeData.cs:81-96` doesn't | Mirror the exemption in `SchemeData` target validation |
| **Infinite chaos orbs** | Immortal monster (hibernating golem) in a Kennel produces endless orbs | `BeingDrained.cs:47-51` broadcasts the orb **before** `AdjustHP`; immune target loses no HP | Postfix so the orb only drops if damage actually landed |

### 2b. Bugfixes: confirmed with a live repro

| Fix | Report | Root cause | Patch |
|---|---|---|---|
| **Portal placement behind the banner** | The Portal cannot be placed on tiles that sit behind the "Pick a tile to place your portal" banner | Placement is gated on `UIManager.IsMouseOnUI()`, a UI raycast; the banner (`InitialWorldSetupMenu.pickPortalMessage`) is a UI element in the middle of the screen, so it swallows the raycast | A `CanvasGroup` with `blocksRaycasts = false` on the banner; it still shows (`Fix_PortalPlacementBehindBanner.cs`) |
| **Released prisoner knows the portal** | Let-go prisoners walk out of the demon base and report the Portal | `LetGoData` -> `MovementComponent.LetGo:788-817` drops them on a Wilderness tile *next to the prison*, inside the demon base | Knock them Unconscious and move them to their home structure (`Fix_ReleasedPrisonerRevealsPortal.cs`) |

### 2c. QOL

| Feature | Notes | Where |
|---|---|---|
| **Disable-tutorials toggle** | Suppresses all tutorial alerts; shipped as the `disableTutorial` config flag rather than a settings row | `TutorialManager.cs:12-27` (14 alert types) + `SaveDataPlayer.cs:9-31` (no master flag in the game) |

### 2d. Exploit fixes (shipped in 0.6.0)
- **Flying-over-kennel Sacrifice/Let-Go** (`Fixes/Fix_KennelFlyingSacrifice.cs`, config
  `closeExploits`): cast on a monster, `SacrificeData.IsValid` / `LetGoData.IsValid` refuse
  one that is flying and not Restrained; cast on the Kennel, `SacrificeData.IsValid` only
  asks for an `occupyingSummon` and `ActivateAbility(LocationStructure)` sacrifices it, and
  `LetGoData.ActivateAbility` lets go of whoever passes `CanPerformAbilityTowards` (no flying
  check). A monster that broke its restraints stays the Kennel's occupant. Postfix
  `SacrificeData.IsValid`, prefix `SacrificeData.ActivateAbility(LocationStructure)`, postfix
  `LetGoData.CanPerformAbilityTowards(Character)` apply the same rule. *(verified in game)*
- **Snatch drop-off list empty** (`Fixes/Fix_SnatchDropLocations.cs`):
  `SnatchObjectUIController.ConstructDropLocationChoices` only lists *bookmarked* structures,
  so with none the Snatch button stays disabled. Postfix: for a character target with no
  usable bookmark, list the player's demonic structures. *(verified in game)*

---

## 3. PHASE 2: Death, Decay & Disease

**Most of the pieces already exist in the game; they are not connected.**

**What EXISTS:**
- Corpses: a dead `Character` keeps its map marker and lies where it fell. A `Tombstone`
  (`Tombstone.cs`) is the **grave**: the game creates one only when a villager buries the
  body (`BuryCharacter.AfterBurySuccess`, the sole creation site).
- Burial: `BuryCharacter.cs` GOAP (`INTERACTION_TYPE.BURY_CHARACTER`) -> `Cemetery.cs`
  (`STRUCTURE_TYPE.CEMETERY`), plus `AncientGraveyard.cs`.
- Disease: `PlagueDisease.cs` (singleton), `Plagued.cs` status (`IPlaguedListener`),
  `Plague/Transmission/Transmission.cs` (spreads via **Airborne / Consumption /
  Physical_Contact / Combat**; `Quarantined` trait cuts it 75%), `PlaguedEvent.cs`
  (ruler picks Do_Nothing / Quarantine / Slay / Exile).
- Quarantine: `Quarantine.cs` GOAP (`INTERACTION_TYPE.QUARANTINE`) -> Hospice BedClinic +
  `CarePlagueBearersBehaviour.cs`.
- Starvation death: already in the base game (the Malnourished status); no mod feature needed.

**What's SHIPPED (Phase 2 so far).** Items marked *verified* pass the automated in-game test
harness (`RuinarchDebug/AutoTest.cs`, run with the loader's `tools/run-autotest.sh`), which
starts a real world and plays the scenario out at speed.
1. **Corpse decomposition** - `Phase2/CorpseDecay.cs`: unburied bodies (dead, marker still
   on the map, no grave) rot Fresh -> Bloated -> Rotting -> Skeletal, then the remains are
   gone. Anything buried never rots; a carried body pauses. Found by an hourly region scan, so
   loaded saves and every death path are covered. *(verified: stages at 18/36/54 h, gone at
   72 h with the default 3 days)*. An earlier version keyed on `Tombstone`s and so rotted
   **graves** outside cemeteries instead of bodies; replaced.
2. **Corpse-borne disease** - `Phase2/CorpseDisease.cs`: rotting/skeletal *unburied* bodies
   inside a settlement structure feed the existing plague, scaled by corpse count. Buried
   bodies are never infectious. On by default (0.5.0; was opt-in).
3. **Mass Grave** - a non-demonic **village building** (`MassGrave : ManMadeStructure`,
   mirroring `Cemetery`), new content via `Ruinarch.ModContent` (`IsVillageStructure`),
   borrowing the Cemetery prefab. Destructible like any village building.
   - **Villagers build it from materials** (`MassGraveConstruction.cs`): a village with a body
     lying in it and no Cemetery / Cult Temple / Mass Grave queues a vanilla `PLACE_BLUEPRINT`
     job; villagers place it, haul the stone/wood its `craftCost` needs, and build it. The
     finished type comes from the prefab (`GenericTileObject.BuildBlueprint`), so the Cemetery
     prefab's CEMETERY is swapped for the Mass Grave type only while a remembered Mass Grave
     blueprint is being built (pooled prefabs are never mutated). *(verified: built after 51 h)*
   - **Burial reroute** (`MassGraveBurial.cs`): both vanilla scatter paths are covered, the
     settlement job (`TriggerBuryMe`) and the personal job a villager queues on seeing a body
     outside village tiles (`TriggerPersonalOutsideVillageBuryJob`). A village with a
     Cemetery / Cult Temple buries its people exactly as vanilla; one without leaves bodies
     where they fell until it has a Mass Grave, then villagers carry every body into it.
     *(verified: no wilderness grave; resident and pre-existing corpses hauled into the pit;
     Cemetery village still uses its Cemetery)*
   - **Creature disposal**: animal and monster carcasses in the village become bury jobs to
     the pit and are disposed (no tombstone), even in a village that also has a Cemetery.
     *(verified)*
   - **Fallback**: a body near the pit that nobody hauls for `massGraveFallbackHours` (12,
     e.g. the village is dead) is absorbed directly.
   - The "this blueprint is a Mass Grave" mark is saved (`ModBuildings.cs`,
     `ModData/ruinarch.plus.blueprints.json`), so a pit saved half-built still completes as a
     Mass Grave after reload. The construction machinery is shared by every Ruinarch+
     building (Mass Grave, Town Hall).
   - **Look** (`MassGraveFloor.cs`): bare dirt, no art of its own. Like every building in
     the game it is a floor with walls and objects on top; the pit keeps the Cemetery's
     footprint and walls, and its floor is set to the prefab's own dirt tile on build and
     load (the Cemetery's paved cross removed). An earlier sprite overlay (a test of the
     framework's `ModArt`) was removed in 0.5.0. The Cemetery prefab's props
     (BRAZIER, PLINTH_BOOK, GODDESS_STATUE, WATER_BASIN, TRASH) are not built on a Mass Grave
     (prefix `LocationStructureObject.OnBuiltStructureObjectPlaced`) and are cleared from pits
     saved before this; its STRUCTURE_TILE_OBJECT is kept (removing it made the pit count as
     not standing). *(verified: one dirt ground tile over the pit, no props)*
   - **Anonymous burial:** a body laid in the pit (hauled or absorbed) leaves no Tombstone;
     it is removed like a fully decomposed body (`Tombstone.SetRespawnCorpseOnDestroy(false)`
     + `RemovePOI`), and older pits have their gravestones cleared once per session. Only the
     pit's count remains (`bodyCount`, not saved).
   - **Surroundings:** hourly, the pit also queues BURY jobs for bodies in the ring of map
     areas around the village (`MassGrave.CatchmentAreas`): creatures, outsiders, and the
     village's own dead when it has no Cemetery. Bodies are found through the region's
     character list; an area's own list only gains a character when its marker moves
     between areas, so a body that never moved is missing from it.
   - **Who goes where:** a village's own people (`homeSettlement` / `homeSettlementOnDeath`)
     go to its Cemetery / Cult Temple when it has one; outsiders, monsters and creatures go
     to the Mass Grave (to the Cemetery only if there is no pit). At most one Mass Grave per
     village, enforced for villager construction and for the debug/instant build.
4. **Settlement curfew** (`Curfew.cs`): a ruler who answers a plague outbreak
   (`PlaguedEvent`) with a measured response, Quarantine or Exile, also puts the village under
   curfew until the event ends. Residents give up free time (visiting, taverns, wandering) and
   go home; work continues, since work is how the settlement's jobs (plague care, burials,
   food) get done; the ruler and faction leader are exempt; needs and combat are untouched.
   Enforced at `BehaviourComponent.RunBehaviour` (return home, else stay in). Deliberately
   **not** a new `PLAGUE_EVENT_RESPONSE`: `ExecuteEffectsOfLeaderResponseToPlague` throws on
   unknown values and the decision is saved and localized. The curfew is derived (active
   event + measured decision), so nothing is saved. Announced in the event log.

5. **Closed borders: shipped** (`Phase2/ClosedBorders.cs`, config `closedBordersEnabled`).
   A village under curfew is dropped from `Region.PopulateValidVillagesToVisit` (free-time
   visits of villagers and bandits), a visit under way is given up
   (`VisitVillageBehaviour.TryDoBehaviour` prefix: `ClearOutVisitVillageBehaviour`), friends
   living there are dropped from `CharacterBehaviour.GetCharacterToVisitWeights`, and
   Ruinarch+ traders don't go there. Raids, rescues and bounty hunts are not visits.
   *(verified in game)* Kingdom-level border closure stays Phase 6.

*Dependency:* unlocks the disease pressure that makes Phase 5's famine/unrest meaningful.

---

## 4. PHASE 3: Knowledge & Fog of War

This is the conceptual keystone of the whole vision: **villagers should only know what they've
witnessed or been told.** It also addresses why the portal keeps getting found.

**What EXISTS (cited against `RuinarchRE`):**
- **Gossip about deeds, not places.** Each character keeps a pool of up to 40 witnessed or
  informed actions (`RumorComponent.AddAssumedWitnessedOrInformedNegativeInfo`,
  `RumorComponent.cs:57-79`, fed by `ReactionComponent.cs:120,150`). The transport is the
  `SHARE_INFORMATION` GOAP action (`ShareInformation.cs`), queued as share-negative-info,
  spread-rumor and confirm-rumor jobs (`CharacterJobTriggerComponent.cs:2001-2051`). Rumors
  are made up by characters who hate someone (`BaseRelationshipContainer.cs:1421`) and by the
  player's Spread Rumor skill (`SpreadRumorData.cs:145`); `Assumption`/`AssumptionComponent`
  hold guesses. All of it is about *what a character did* (crimes, affairs), never *where
  something is*.
- `LocationAwareness` (`Area.cs:81`, `LocationStructure.cs:157`) is a per-location index of
  objects by action type used by the planner. It is not character memory.
- **Portal discovery is one faction-wide switch.** A villager who sees a demonic structure
  (`CharacterTrait.cs:202-223`) queues a report if their faction is not yet aware; the report
  (`ReportCorruptedStructure.AfterReportSuccess`, `ReportCorruptedStructure.cs:81-93`) adds the
  structure to `InnerMapManager.worldKnownDemonicStructures`, raises threat, and calls
  `Faction.SetIsAwareOfPlayer(true)` (a saved bool, `SaveDataFaction.cs:79`). If already
  aware, the sighting rolls `CHANCE_TYPE.Counterattack` instead. A village whose area borders
  the player's also becomes aware and counterattacks with no sighting at all
  (`SettlementPartyComponent.cs:349-379`).
- **The leak:** `worldKnownDemonicStructures` is written but never read, and not saved. The
  counterattack's destination is the **whole player settlement**
  (`CounterattackPartyQuest.cs:10,57-60`), so seeing one structure reveals all of them,
  the portal included.
- **Rescue is omniscient.** Every party-processing pass rolls 50% (100% with 2+ parties) to
  rescue any resident who is Restrained/Paralyzed outside the village
  (`SettlementPartyComponent.cs:236-251`, `BaseSettlement.GetRandomResidentForRescue`
  `BaseSettlement.cs:397-415`), with no witness needed, and the party goes to the captive's
  **live** location (`RescuePartyQuest.GetTargetDestination`, `RescuePartyQuest.cs:31-42`).
  Witness-driven rescues also exist (`Abduct.cs:124-126`, `Distraught.cs:27-29`,
  `IsCaptive.cs:301-303`).
- There is **no missing-person state** anywhere in the game.

**Model ([L], extends the above rather than replacing it):**
- **Shipped: per-faction known-structures ledger** (`Phase3/Knowledge.cs`, config
  `knowledgeEnabled`). Fed by the game's own report (`AfterReportSuccess`, which walks home
  to the village's main storage) and by witnesses carrying news home. Saved inside the
  player's save through the loader's new `ModSave` (`ModData/ruinarch.plus.knowledge.json`).
- **Shipped (0.6.0): news travels on foot.** Once the faction is aware, a sighting (prefix
  `CharacterTrait.OnSeePOI`) no longer teaches the faction at once: the witness *carries* it
  (`Knowledge.Witness`), and an hourly check delivers it when they stand in a village of their
  faction; a witness who dies first takes it with them. Their party acts on it meanwhile
  (`Knowledge.PartySightings` feeds counterattack targeting and rescue/bounty on-site
  checks). Carried news is saved with the ledger. The vanilla adjacency trigger
  (`SettlementPartyComponent.TryCreateCounterattackQuest`, which also makes the faction aware
  with no sighting) now runs only once the faction knows a structure standing next door.
  *(verified in game)*
- **Shipped: counterattacks go only where the faction knows:** postfix
  `CounterattackPartyQuest.GetTargetDestination` returns the nearest known structure, and a
  prefix on `AttackDemonicStructureBehaviour.TryDoBehaviour` attacks known structures instead
  of the hard-coded portal; with nothing known left standing, the party goes home and the
  quest ends as a success. This runs for every active party on that behaviour, whatever its
  quest: 0.5.0 fell back to vanilla when the faction's ledger was empty, and vanilla marches
  on the Portal (it destroyed the Portal twice in test runs, and would after any load where
  everything a faction knew had been destroyed). A save from before the ledger (no
  `ruinarch.plus.knowledge.json`; the file is now always written) is migrated at the first
  in-game hour: every aware faction learns all standing player buildings, as the base game
  treated it. *(verified in game)*
- **Shipped: rescues and bounty hunts ask the ledger too** (`Phase3/KnowledgeTargets.cs`).
  The game decides three more things about one player building with the faction-wide
  `isAwareOfPlayer`: a Demon Rescue for a villager held inside it
  (`PartyQuestBoard.CreateRescuePartyQuest`), a bounty hunt on a criminal hiding in it
  (`CreateBountyHuntPartyQuest`, reached from `TryCreateBountyHuntQuest`), and attacking it on
  arrival (`RescueBehaviour` / `BountyHuntBehaviour.TryDoBehaviour`). Each now requires the
  faction to know that building; unknown, there is no rescue quest and the captive is
  searched for as a missing person (see below). A party that sees its target inside learns the building. Not faction
  knowledge, left as is: a dragon picks any player building (`Dragon.SetPlayerTargetStructure`)
  and Divine Intervention targets the Portal.
- **Shipped (0.6.0): no Portal-homing "search".** An unaware faction's answer to a captive
  held by the demons is `SettlementJobTriggerComponent.CreateSearchForDemonicAreaJob`, a job
  whose *target is the PortalTileObject*: the searcher walks straight to the Portal until
  they step on corruption (`ACTION_LOCATION_TYPE.ON_REACH_CORRUPTION`), then report it or die
  to its defenders (seen in play: "X is looking for your location", always killed near the
  Portal). 0.5.0 also routed aware factions' rescues into unknown buildings there. With
  `knowledgeEnabled` the job is never created (prefix) and existing ones are never taken
  (`CanTakeSearchForDemonicArea` postfix); the captive is a missing person, searched for at
  their last-seen spot. *(verified in game)*
- **Shipped: missing persons and searches by last-known location**
  (`Phase3/MissingPersons.cs`, `Phase3/MissingPersonsSearch.cs`, config
  `missingPersonsEnabled`). Each resident's last sighting by their own people (home village,
  or in sight of a free faction member) is kept hourly; unseen for a day, they are reported
  missing and the village posts the game's own rescue quest, pointed at the last-seen spot,
  where the party sweeps until it sees them or gives up (retried after 24 then 48 hours,
  three attempts). One of their own people burying the body counts as finding them dead
  (postfix `BuryCharacter.AfterBurySuccess`): a Mass Grave leaves no gravestone to spot, and
  villages used to keep searching for a neighbour they had carried into it themselves.
  The omniscient settlement rescue roll is off. Records ride in the save
  through `ModSave` (`ModData/ruinarch.plus.missing.json`). Spec:
  `docs/specs/2026-09-23-missing-persons-design.md`.
- **Shipped (0.6.0): gossip carries places** (`Phase3/Gossip.cs`, config `gossipChance` 25).
  `SHARE_INFORMATION` is not reusable (its payload must be an `IReactable` and
  `ProcessInformation` reads rumour fields), so a meeting is a character sighting (prefix
  `CharacterTrait.OnSeePOI` with a `Character` target), at most once per pair per day. Kin pass
  on news they carry; someone of another non-hostile faction hears each building the teller
  knows with `gossipChance`. The listener carries it home like a witness; a faction that
  learns of the player this way becomes aware. Traders (Phase 5) carry news both ways.
  *(verified in game)*

- **Shipped: "Who Knows of You"** (`Phase3/KnowledgePanel.cs`, with `knowledgeEnabled`). A
  section of the bookmarks panel under Major Events, read from the ledger: one line for the
  region ("Your presence in the region is not known." / "2 of 3 factions know of you."), one
  per faction that knows ("Aurenad know of your Portal and Corrupt Kennel", or "... know of
  you, but not where you are"; hover lists everything it knows, click opens the faction),
  one per villager carrying news home (up to 5; click selects them). The player acts on
  what the world knows (kill the witness before they get home). It is a bookmark category
  the game's enum does not have (value 100): the panel's sort order (which throws on
  unknown values) and header text are patched. Bookmarks are not saved, so it is rebuilt
  per game/load; it always keeps the region line, since an emptied category loses its
  section for good. The region line is the skeleton TruePlanet grows into (per region /
  nation). *(verified in game)*

**Next for Phase 3 (from play feedback):**
- **Visual fog of war and borders [L]:** settlement borders (the areas a village holds) and,
  around them, the nation's (faction's) borders, drawn on the map; each faction's knowledge
  shown on the map when it is selected (its known buildings marked, the rest dimmed, carried
  news as a marker on the witness). Needs its own map overlay (SpriteRenderers per area, as
  the Mass Grave overlay test did); national borders tie into Phase 6 and TruePlanet.

*Dependency:* feeds Trade (info = prices), War (scouting), and TruePlanet (inter-nation
knowledge).

---

## 5. PHASE 4: Living Population

**Mostly net-new: the game has no life cycle.**

**What EXISTS:** relationships/lovers (hook for pairing); migration meter
(`SettlementVillageMigrationComponent`: cap 1500, +3 to 8/hr, 8% daily auto-event,
`StifleMigrationData` resets it). Reproduction exists only for Ratmen (`BirthRatman`).

**What's NEW:**
1. **Migration rework [M]: shipped** (`Phase4/MigrationHealth.cs`, config
   `migrationHealthEnabled`). Every natural gain funnels through
   `SettlementVillageMigrationComponent.IncreaseVillageMigrationMeter`; a prefix scales it by
   village health: 0 during plague (`isPlagued` or an active Plagued event) or siege, 0 when abandoned homes (beyond
   2 spare) outnumber occupied ones, else halved per abandoned home beyond the 2 the build planner keeps spare, halved per unburied body on
   village tiles. A postfix on `Character.Death` takes 150/1500 off the home village's meter.
   The meter tooltip explains the throttle. Induce Migration bypasses the meter and is
   unaffected. In vanilla the only gate is `residents.Count > 0`; wave size scales with
   the player's portal level (`SettlementVillageMigrationComponent.cs:337-355`).
2. **Natural birth [L]:** couples procreate at a slow rate scaled by food/housing; babies
   become children then adults over game-years.
3. **Aging & natural death [L]:** age advances; elders sicken (dementia/alzheimer's as
   new flaws) and eventually die of old age. Dementia ties to Phase 3: they **forget**
   ledger facts, so knowledge decays with generations.
4. **Library [M to L]:** a structure that *persists* faction knowledge against that decay
   (Phase 3 ledger + Phase 4 aging). Villagers deposit what they learn on return from
   searches/trades; burning it is a real strategic blow.
   From play feedback: knowledge lives in two places, people and records. Each villager has a
   *memory* of what they know (the carried-news model per person, extended), which fades
   with age, dementia and Alzheimer's (item 3) and dies with them; bookshelves and books in
   dwellings, and the Library, keep it. Burn the books and kill or outlive the people who
   remember, and after a while the faction loses where you are. Needs births, aging, illness
   and death (items 2 and 3) first, so it follows them.

*Dependency:* population + food + war together drive Phase 5 settlement growth.

---

## 6. PHASE 5: Settlements & Economy

**What EXISTS (cited against `RuinarchRE`):**
- **No size tiers.** `SettlementType` (`Settlement_Types/SettlementType.cs`) is a culture
  flavour (`Human_Village`, `Elven_Hamlet`, `Capital`, `Cult_Town`...), set at founding
  (`NPCSettlement.SetSettlementType`, `NPCSettlement.cs:2339-2346`) and never changed by growth.
  Its caps are hard-coded constants, re-set on load rather than saved: `maxDwellings` 16 / 24
  (Capital) / 10 (Cult Town), `maxFacilities` 12 to 16 (`HumanVillage.cs:10-18` etc.). They
  are read in exactly two places: the build planner (`SettlementJobTriggerComponent.cs:1032,
  1041,1065`) and per-facility caps (`NPCSettlement.cs:1198`). `VILLAGE_SIZE`
  (Small/Medium/Large, `VillageSetting.cs`) only shapes world generation. `LOCATION_TYPE` is
  static.
- **Food is piles, not a stockpile.** There is no settlement food counter; totals are summed
  from `FoodPile`s on demand (`BaseSettlement.GetNumberOfFoodInWholeSettlement`,
  `BaseSettlement.cs:1328`). Producers are Farmer, Fisher and Butcher
  (`Extensions.IsFoodProducerClassName`, `Extensions.cs:1700-1707`). `PRODUCE_FOOD` jobs fire
  below a minimum (`ProduceResourceApplicabilityChecker.cs:23,43`). The build planner already
  compares residents with potential food capacity and builds a Butcher's Shop, Fishery or Farm
  when short (`SettlementJobTriggerComponent.cs:1098-1104`,
  `SettlementResourcesComponent.cs:14-38`). Hunger is per character: Hungry -> Starving, plus
  Malnourished (`CharacterNeedsComponent.cs:1236-1355`). There is **no settlement-level
  shortage signal**.
- **Hunting:** "Hunter" is a *combatant* class (`HumanEmpire.cs` and the other faction
  types), not a food producer. The Hunter Lodge employs a Skinner
  (`SettlementClassComponent.cs:605-608`); `HuntBeastPartyQuest` exists (bandits use it,
  `SettlementPartyComponent.cs:314`).
- **No trade.** "Merchant" is just the Tavern's worker class
  (`SettlementClassComponent.cs:611-614`). Nothing moves goods between settlements; there
  are no caravans or trade routes.

**What's NEW:**
1. **Settlement progression: shipped** (`Phase5/SettlementTiers.cs`, `Phase5/TownHall.cs`,
   config `settlementTiersEnabled`, `townPopulation` 20, `cityPopulation` 40).
   `Village -> Town -> City`. A village of `townPopulation` living villagers queues a
   **Town Hall** blueprint (a new village building through `Ruinarch.ModContent`, borrowing
   the Tavern's prefab; no art of its own) and its villagers build it through the game's
   own pipeline, so it is damaged and destroyed like any building. While it stands the
   village is a Town, a City from `cityPopulation`; a tier is kept down to 3/4 of its mark
   (hysteresis) and lost the moment the Town Hall is destroyed. A tier raises the two
   limits the build planner reads (set through reflection hourly and on load; the culture's
   own values are the base): Town +8 dwellings / +4 facilities, City +16 / +8. No new
   `SETTLEMENT_TYPE` (it is saved and drives culture-specific facility weights). The tier
   is saved (`ModData/ruinarch.plus.tiers.json`), announced in the event log, and named in
   the settlement panel ("Human Empire Town"), in the "Village" line of a building's panel
   and in the center's description. The faction leader's home village is labelled
   **Capital** once the faction holds more than one village (a name only, no new rules;
   TruePlanet's nation capitals build on it). Outpost (a tier *below* a vanilla village)
   is left out: it would shrink villages the game generates.
2. **Food economy & famine [M to L]:** *Famine shipped* (`Phase5/Famine.cs`, config
   `famineEnabled`, `famineHours` 12, `famineLeaveChance` 25). The signal is the game's own
   hunger rather than food piles (consumption rates are runtime-only editor values): a third
   of a village's villagers Starving or Malnourished for `famineHours` -> famine, until a
   tenth or fewer for as long. Announced; no migration (`MigrationHealth` reason "famine");
   once a day each starving villager (not ruler / faction leader) may move, via the game's
   own `Character.MigrateHomeStructureTo`, to a free Dwelling in a village of their faction
   not in famine. Active famines are saved (`ModData/ruinarch.plus.famine.json`).
   **Unrest: shipped** (0.6.0, config `unrestEnabled`, `unrestHours` 24, `challengeHours` 72):
   restless after `unrestHours` of famine (announced), each day every villager's opinion of the
   ruler drops ("Famine", -10, no opinion jobs); after `challengeHours` the villager who
   thinks least of the ruler takes the rule of the village through the game's own
   `INTERRUPT.Become_Settlement_Ruler` (the deposed ruler: "Deposed", -30). A faction leader
   always rules their home village (`Faction.ProcessFactionLeaderAsSettlementRuler` reinstated
   them, and the deposed leader-ruler then emigrated), so a ruler who leads the faction is
   overthrown as leader too, as the game's Overthrow Leader scheme does
   (`Become_Faction_Leader`, a grudge). Once per famine;
   hours and the challenge ride in the famine save. The game has no NPC coup of its own
   (Overthrow Leader and Rebellion are player schemes). *(verified in game)*
   **Hunters: shipped** (`Phase5/Hunters.cs`, config `huntingEnabled`, `huntersPerTrip` 2):
   every 6 hours a hungry village (in famine or a fifth starving) gives up to
   `huntersPerTrip` fighters (Hunters first) the hunting job predators use (`HUNT_PREY` with
   `ASSAULT`, a lethal job) on a wild animal within 60 tiles (not Bears). Prey that flees is
   hunted again hourly for a day. On the kill (postfix `Summon.Death`: animals are Summons and
   `Summon.Death` does not call `Character.Death`) the hunter gets `PRODUCE_FOOD` with
   `BUTCHER` on the carcass; a postfix on `Butcher.AfterTransformSuccess` hauls the meat to the
   main storage. (A single `PRODUCE_FOOD`/`BUTCHER` job on a live animal never got planned.)
   *(verified in game)*
   **Next for unrest (from play feedback): how a ruler falls.** Today the challenger simply
   takes over. Instead, depending on the village and the people involved: a brawl between
   the two camps (the game's own `BRAWL` job), a civil war in a larger village (residents
   pick sides by opinion of the ruler and challenger and fight; the loser's camp is exiled
   or leaves the faction), an assassination of the old ruler (the game's `ASSASSINATE` /
   murder paths, with its crime and witnesses), or the old ruler jailed (the game's
   apprehend / imprison flow into the village prison). Bigger villages and stronger
   factions within them make war more likely than a quiet handover.
3. **Traders: shipped** (`Phase5/Traders.cs`, config `tradeEnabled`, `tradeAmount` 40). Daily
   at 8:00 a village with more than 20 food per villager plus `tradeAmount` sends one
   villager (a Merchant first) with a pile of `tradeAmount` food (split off its storage) on
   the game's own `HAUL` / `DEPOSIT_RESOURCE_PILE` job to the neediest village (hungry, or
   under 10 food per villager) whose faction is not hostile; never to or from a village
   under curfew. The goods are owned by the trader until delivered (`SetCharacterOwner`: the
   game's haulers and stockpile combiners skip owned piles; before this, other villagers
   carried the trader's pile off), and a trader called away puts them down and picks them up
   again up to 3 times. On delivery (postfix `DepositResourcePile.AfterDepositSuccess`) it is
   announced and the trader exchanges news (`Knowledge.Exchange`). Trips are not saved (the
   haul job itself is the game's). Messengers as a separate role are not needed: within a
   faction the ledger is shared once news reaches any of its villages, and between factions
   traders and gossip carry it. *(verified in game: a trader delivers; the news exchange
   between factions has not come up in a test world yet, since test villages trading with
   each other have so far been of one faction)*

---

## 7. PHASE 6: War & Diplomacy

Military systems, plus curfews/borders from Phase 2.

**What EXISTS:** a warfare mechanic, but short-lived (partly because populations are tiny,
which Phases 4 and 5 fix at the root).

**What's NEW:**
- **Training grounds [M]** in Town+ settlements produce local standing armies (melee/archer/...).
- **Patrols & levies [L]:** locals patrol and kill threats; a nation at war can call them
  up; they march off-region, and win/lose/die (feeding Phase 4 population).
- **Curfews & closed borders [M]:** the settlement-curfew from Phase 2 escalates to
  kingdom-level border closure during war/plague.
- **Diplomacy [L]:** alliances, territory trade, war declarations the player can scheme into.

---

## 8. PHASE 7: TruePlanet  *(separate sister mod, XL)*

This is a **world-generation replacement**, correctly its own mod:

- **Planet generation:** continents, oceans, mountains, frozen lands, biomes; regions
  owned by **nations** with a **capital**. A region = one connected map, **bigger than
  "Very Large."**
- **Settlement hierarchy:** `Outpost -> Village -> Town -> City -> Capital` (capital unique
  per nation, special buildings: Palace/Parliament/etc. by government type).
- **Region preview:** inspect a region before committing the portal (avoid dropping into
  a packed capital with nowhere to build).
- **Two portal types:** **Main Portal** (current) + **Travel Portal**: linked network;
  demons spawned far away route home through the nearest travel portal (smart pathing, no
  manual hopping). Enables spreading the operation across regions.
- **Religion & language:** every nation has its own; a region can diverge from its nation
  (conquest, territory trade). Feeds diplomacy (Phase 6) and gossip/knowledge (Phase 3:
  language barriers throttle information spread).

TruePlanet depends on Ruinarch+ Phases 3 to 6 being in place to feel alive; it ships last.

**Another sister mod, later: a performance mod.** Optimisations that change what the player
sees belong there, not in Ruinarch+ (which only fixes outright waste, like the wall-rescan
fix in 0.6.0). First candidate, measured with RuinarchDebug's `FireProbe`: the floating
damage number and hit spark every damaged object shows on every hit. In a whole-village fire
that is thousands a second; skipping them for fire damage on objects took a test from 66 to
77 fps at 1x, about three times that gain at 4x. The game shows them for anything that takes
damage, so it is a player's choice.

---

## 9. Naming & packaging

- Display name **Ruinarch+**; mod id `ruinarch.plus` (namespace `RuinarchPlus`, since `+`
  isn't file-safe). Sister mod: **TruePlanet** (`trueplanet`).
- Ships via the ModLoader (drop in `Mods/`, no external injector). Phase 1 shipped as
  `v0.1`; later phases bump the minor version, with a config flag per feature so players
  can pick their depth.
