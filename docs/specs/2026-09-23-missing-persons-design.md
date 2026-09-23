# Missing persons and search parties (Ruinarch+, Phase 3)

Status: design, approved in outline, awaiting spec review.

## Goal

A village only knows what its people have seen. In the base game a village always knows
where a captured resident is: it rescues any Restrained or Paralyzed resident outside the
village with no witness, and the rescue party walks straight to the captive's live
position. After this change:

- A resident who is not seen by their own people for a day is reported **missing**.
- The home village sends a **search party to the place they were last seen**, not to where
  they are now.
- The party sweeps the area. If it finds them, the game's own rescue logic takes over
  (free them, or find them dead or safe). If not, the village searches again later, less
  and less often, and finally gives the person up as lost.

The player feels it most when abducting villagers: their village comes looking, and a
search near the player's buildings can discover them.

## Base-game behaviour this replaces (cited against RuinarchRE)

- `SettlementPartyComponent.TryCreateRescueQuest` (`SettlementPartyComponent.cs:236-251`):
  each party-planning pass rolls 50% (100% with 2+ parties) and posts a rescue for a random
  resident from `BaseSettlement.GetRandomResidentForRescue` (`BaseSettlement.cs:397-415`):
  any resident outside the village who is Restrained or Paralyzed. No witness is needed.
- `RescuePartyQuest.GetTargetDestination` (`RescuePartyQuest.cs:31-42`): the captive's live
  structure or area.
- `RescueBehaviour.TryDoBehaviour` (`RescueBehaviour.cs:11-122`), in the Working state:
  if the captive is out of sight the party member walks to the captive's live position
  (`CreateGoToJob(targetCharacter)`); a captive being seized is followed to their live
  previous tile; a captive with no marker ends the quest at once.
- `RescuePartyQuest.IsStillEligibleFor` (`RescuePartyQuest.cs:49-56`): a rescue stays on the
  board only while the target is Restrained.

Witness rescues stay as they are: a villager who sees an abduction, a friend in danger or a
prisoner still posts a rescue (`Abduct.cs:126`, `Distraught.cs:29`, `IsCaptive.cs:303`,
`Faction.cs:1492`). They cover captives lying in plain sight, which is why removing the
omniscient roll does not leave visible captives unrescued.

## Who is tracked

A character is tracked while all of these hold:

- they are alive or not yet known to be dead, and a resident of an `NPCSettlement` whose
  `locationType` is `VILLAGE`;
- the village's owner faction is a major non-player faction (`isMajorNonPlayer`);
- they are sapient (`race.IsSapient()`) and `isNormalCharacter`;
- their faction is still the village's faction.

A character who stops meeting these (moves away, becomes a vagrant, joins the player, the
village is destroyed) is dropped from tracking silently.

## Being seen

Once per in-game hour (the existing `GameManager.TickStarted` hourly hook used by the Mass
Grave), each tracked resident R counts as **seen** if either:

1. R has a tile and `R.IsInHomeSettlement()`; or
2. some other character W of R's faction, alive, sapient and able to witness
   (`limiterComponent.canWitness`), has R in `W.marker.inVisionCharacters`, or has R's
   grave (`R.grave`) in `W.marker.inVisionTileObjects`.

When R is seen, their record stores the tile (R's tile, or the grave's tile) and the time.
A seen resident is never missing. Cost: one pass over the faction members' vision lists
per hour.

## Missing, searching, giving up

Each tracked resident has a record with: last-seen tile, last-seen time, state, failed
search count, time of the next search, and the id of the current search quest.

States and transitions:

| From | Event | To | Notification |
|---|---|---|---|
| Seen | unseen for `missingAfterHours` (24) | Missing | "X of V has gone missing. They were last seen near P." |
| Missing | next search time reached, no search quest open | Searching | log only: "V is organising a search for X." |
| Searching | sweep ends without seeing X | Missing (next search 24 hours after the 1st failure, 48 after the 2nd) | log only: "The search for X found nothing." |
| Searching | 3rd failed search (`searchMaxAttempts`) | Lost | "V has given up the search for X." |
| Missing, Searching, Lost | X is seen alive | Seen | "X of V has been found." |
| Missing, Searching, Lost | X is seen dead (body or grave) | dropped | "X of V has been found dead." |

"Near P" names the last-seen structure, or the area when that is wilderness. The first
search is posted as soon as a person is reported missing. A Lost person is no longer
searched for, but a later sighting still reports them found.

Notifications use Curfew's existing announcement helper (game log plus notification feed).
Rows marked "log only" go to the game log and `mods.log` without a feed notification, so
many villages searching at once do not flood the feed.

## The search party

The search is the game's own `RescuePartyQuest`, posted on the faction's quest board with
the home village as `madeInLocation`. It is created directly (`PartyManager.CreateNewPartyQuest`,
`SetMadeInLocation`, `SetTargetCharacter`, `PartyQuestBoard.AddPartyQuest`), not through
`PartyQuestBoard.CreateRescuePartyQuest`, because that method picks its route from the
target's live structure and so would reveal where they are.

Patches, applied only when the quest's target has a record (searches and witness rescues
alike), except where noted:

- **Destination.** Postfix `RescuePartyQuest.GetTargetDestination`: the last-seen tile's
  structure, or its area when the structure is wilderness. For a witness rescue the witness
  just saw the target, so this is where they were seen.
- **Eligibility** (searches only). Postfix `RescuePartyQuest.IsStillEligibleFor`: the quest
  stays valid while the record is Missing or Searching, whatever the target's condition.
- **Name** (searches only). Postfix `RescuePartyQuest.GetPartyQuestName`: "Search for X".
- **Working, target out of sight** (searches only). Prefix `RescueBehaviour.TryDoBehaviour`:
  when the member does not see the target, it replaces the base game's walk to the live
  position (and its seized, no-marker and live-structure checks) with a sweep:
  - the first member to start work starts the sweep clock (`searchSweepHours`, 6);
  - each idle member walks to the last-seen tile first, then to random reachable tiles in
    the last-seen area and the areas bordering it (`CreateGoToSpecificTileJob`);
  - when the clock runs out the quest ends with the game's own "Target_Nowhere" reason and
    the record counts a failed search.
- **Working, target in sight.** The prefix lets the base game run unchanged: it frees a
  restrained target, ends as "Target_Dead" for a body, or "Target_Safe" for someone free.
  The hourly check sees the target in the same hour and posts the "found" notification.
- **Base-game roll off.** Prefix `SettlementPartyComponent.TryCreateRescueQuest` (private,
  patched by name like `TryCreateCounterattackQuest` in `Knowledge.cs`) skips the method.

What the party sees on the way (demonic structures included) feeds faction knowledge through
the existing rules: the knowledge ledger if the faction is aware of the player, the game's
own report job if not.

## Config (`config.json`)

| Key | Default | Meaning |
|---|---|---|
| `missingPersonsEnabled` | true | the whole feature; off restores the base game |
| `missingAfterHours` | 24 | unseen hours before a resident is reported missing |
| `searchSweepHours` | 6 | hours a party sweeps after reaching the last-seen spot |
| `searchRetryHours` | 24 | wait after the first failed search; doubles after each failure |
| `searchMaxAttempts` | 3 | failed searches before the person is given up as lost |

## Saving

Records are saved with the loader's `ModSave` under id `ruinarch.plus.missing`, as a list
of strings (JsonUtility drops lists of mod-defined classes, as found with Knowledge): one
per record, holding character id, last-seen tile coordinates in the main region, last-seen
time, state, failed searches, next search time and quest id. A record whose character or
quest no longer resolves on load is dropped and named in `mods.log`. A save with no entry
loads with no records: everyone counts as seen from the first hourly check.

The quests themselves are ordinary rescue quests saved by the game. A save opened without
the mod keeps them as plain rescues, which the base game then runs its own way.

## Testing (RuinarchDebug harness, new suite)

The suite shortens the timings through `PlusBridge.SetConfig` (for example 4 unseen hours,
2 sweep hours, 4 retry hours) so it fits in a run. Each check names what it measured.

1. **No omniscient rescue.** A resident restrained in the wilderness, out of everyone's
   sight, gets no rescue quest before `missingAfterHours`.
2. **Reported missing.** The same resident is reported missing after `missingAfterHours`,
   with the notification in `mods.log`.
3. **Search goes to the last-seen spot.** After the resident is last seen at A, they are
   moved far away to B. The posted quest's destination is A's area, not B's.
4. **Captive found and freed.** A restrained resident left at the last-seen spot is found:
   the party frees them, the record clears, "has been found" is logged.
5. **Found dead.** A resident killed unseen in the wilderness is found dead by the search.
6. **Retry and give up.** A resident kept out of sight away from A: failed searches are
   spaced 4, then 8 hours apart, and after 3 the village gives up.
7. **Survives save and load.** A missing record comes back after the game's own save/load
   step, like the existing knowledge check.

Open points to confirm in the first implementation pass (not assumptions to build on):

- whether dead characters stay in `inVisionCharacters` (if not, bodies are matched through
  `inVisionPOIs`);
- whether a test village has a party free to take the quest (if not, the harness forms one
  the way the counterattack test does);
- how the quest name reads in the party UI with the "Search for X" override.

## Out of scope

- carrying a found body home for burial;
- gossip spreading last-seen places between villagers (a later Phase 3 item);
- a "Missing" status icon (it would make saves depend on the mod);
- searches by factions that are not major non-player factions.
