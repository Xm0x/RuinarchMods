using System;
using Ruinarch.Modding;
using UnityEngine;

namespace RuinarchPlus
{
	/// <summary>
	/// Ruinarch+ options. The loader shows every [Setting] in the game's Settings window (Mods tab)
	/// and saves them in Mods/settings/ruinarch.plus.json. Every option is read through Current when
	/// its feature runs, so a change applies at once.
	/// </summary>
	[Serializable, ModSettings("Ruinarch+")]
	public class RuinarchPlusConfig
	{
		// Off by default: base game behaviour is unchanged until the player opts in.
		[Section("General")]
		[Setting("Skip the tutorial", "Games you start or load from now on have no tutorial alerts.")]
		public bool disableTutorial = false;
		// Phase 1 exploit fixes (on by default; set false to keep the exploits): Sacrifice or
		// Let It Go cast on a Kennel no longer takes a flying monster that is only passing over.
		[Setting("Close exploits", "Sacrifice or Let It Go cast on a Kennel no longer takes a flying monster that is only passing over.")]
		public bool closeExploits = true;
		// Explosions your side sets off (poison and frozen explosions, chain lightning, from your
		// spells or your demons) no longer damage your demonic buildings, as your spells already
		// don't. Set false for the base game (the Portal's defenders can wear it down).
		[Setting("Your explosions spare your buildings", "Poison and frozen explosions and chain lightning set off by your side no longer damage your demonic buildings and walls.")]
		public bool friendlyExplosionsSpareBuildings = true;

		// Phase 2 - Death, Decay & Disease.
		// Unburied corpses left in the open rot over time and eventually vanish,
		// instead of littering the map forever. Buried graves (in a cemetery) persist.
		[Section("Corpses and burial")]
		[Setting("Unburied corpses rot", "Corpses left in the open rot and in the end vanish. Graves in a cemetery stay.")]
		public bool corpseDecayEnabled = true;
		// In-game days a corpse takes to fully decompose (480 ticks/day). Floor is 1/4 day.
		[Setting("Days for a corpse to rot away"), Range(0.25f, 30f)]
		public float corpseDecayDays = 3f;
		// Corpse-borne disease. Rotting unburied corpses in a settlement sicken the living
		// present, scaled by corpse count. Requires corpseDecayEnabled (it reads the decay stage).
		[Setting("Rotting corpses spread disease", "Rotting unburied corpses in a village can make the people around them sick. Needs rotting corpses.")]
		public bool corpseDiseaseEnabled = true;
		// Percent infection chance, per rotting corpse, per in-game hour, per nearby villager.
		[Setting("Disease chance per corpse (%)", "Chance per rotting corpse, per hour, for each villager nearby."), Range(0, 100)]
		public int corpseDiseaseChancePerCorpse = 3;
		// Mass Grave burial: villages with no Cemetery/Cult Temple stop scattering tombstones
		// into the wilderness. Corpses lie where they fell until the settlement has a Mass
		// Grave; then villagers carry every corpse (people and creatures) into it.
		[Setting("Mass Graves", "Villages without a cemetery stop scattering tombstones; once they have a Mass Grave they carry every corpse into it.")]
		public bool massGraveBurialEnabled = true;
		// In-game hours a corpse near a Mass Grave may go un-hauled (e.g. nobody left alive to
		// carry it) before the pit absorbs it directly.
		[Setting("Hours before a Mass Grave takes a corpse", "A corpse near a Mass Grave that nobody carries in this time is taken by the pit itself."), Range(1, 72)]
		public int massGraveFallbackHours = 12;

		// Settlement curfew: a ruler who answers a plague outbreak with Quarantine or Exile also
		// orders residents home in their free time until the plague event ends.
		[Section("Plague")]
		[Setting("Plague curfew", "A ruler who answers a plague with Quarantine or Exile also sends residents home in their free time until it ends.")]
		public bool curfewEnabled = true;
		// Closed borders: a village under curfew turns away visitors (free-time visits, visits
		// to friends, traders). Raids, rescues and bounty hunts still come.
		[Setting("Closed borders during a curfew", "A village under curfew turns away visitors and traders. Raids, rescues and bounty hunts still come.")]
		public bool closedBordersEnabled = true;

		// Phase 3 - Knowledge & Fog of War.
		// Factions only attack demonic structures they know about. A villager who sees one
		// carries the news home (killed on the way, it is lost); a village next door counts
		// only once it knows something there. Counterattacks no longer march on a portal
		// nobody has seen.
		[Section("Knowledge")]
		[Setting("Factions attack only what they know", "A villager must see a demonic building and carry the news home before their faction can attack it.")]
		public bool knowledgeEnabled = true;
		// Gossip: percent chance, per building, that a villager tells someone of another
		// (non-hostile) faction about a demonic building they know of; kin always pass on news
		// they carry. 0 turns gossip off.
		[Setting("Gossip chance (%)", "Chance per building that a villager tells someone of another friendly faction about a demonic building. 0 turns gossip off."), Range(0, 100)]
		public int gossipChance = 25;
		// Records (with knowledgeEnabled): households keep a record of the player's buildings
		// on their dwelling's Book Shelf (or a Book the mod places); a Town or City builds a
		// Library (with settlementTiersEnabled) whose Book Shelves, or libraryBooks Books, keep
		// the village's. In free time a villager at home writes what the record lacks (an hour
		// at the shelf) and, readChance percent per hour, reads what they do not remember;
		// libraryVisitChance percent per free-time hour they go to write or read in the
		// Library. Burning the shelves and Books loses the records.
		[Setting("Written records", "Homes and Libraries keep written records of your buildings, which villagers write and read. Needs the knowledge option.")]
		public bool recordsEnabled = true;
		[Setting("Reading chance per hour (%)", "Chance per free hour at home that a villager reads what they do not remember."), Range(0, 100)]
		public int readChance = 25;
		[Setting("Library visit chance per hour (%)", "Chance per free hour that a villager goes to the Library to write or read."), Range(0, 100)]
		public int libraryVisitChance = 10;
		[Setting("Books in a Library without shelves"), Range(1, 20)]
		public int libraryBooks = 4;

		// A village only knows where its people are if it has seen them. A resident none of
		// their people has seen for missingAfterHours is reported missing (someone away at
		// work told the village where they went and is not), and the village
		// sends a search party to where they were last seen. A failed search is retried after
		// searchRetryHours, doubling each time; after searchMaxAttempts failures the village
		// gives them up. Replaces the base game's rescue of captives nobody saw.
		[Section("Missing persons")]
		[Setting("Missing persons and searches", "A village notices when one of its people has not been seen and sends a search party where they were last seen.")]
		public bool missingPersonsEnabled = true;
		[Setting("Hours before someone is missing"), Range(1, 240)]
		public int missingAfterHours = 24;
		// Hours a search party sweeps around the last-seen spot before giving up.
		[Setting("Hours a search party searches"), Range(1, 48)]
		public int searchSweepHours = 6;
		[Setting("Hours before a failed search is retried", "Doubles after every failed search."), Range(1, 240)]
		public int searchRetryHours = 24;
		[Setting("Searches before giving up"), Range(1, 10)]
		public int searchMaxAttempts = 3;

		// Phase 4 - Living Population.
		// Migration follows the village's fortunes: no settlers during plague or siege, fewer
		// while homes stand empty or the dead lie unburied, and each death sets the migration
		// meter back. The player's Induce Migration skill is unaffected.
		[Section("Life and population")]
		[Setting("Migration follows a village's fortunes", "No settlers during a plague or siege, fewer while homes stand empty or the dead lie unburied. Your Induce Migration is unaffected.")]
		public bool migrationHealthEnabled = true;
		// Life cycle (Phase 4): a year is lifeDaysPerYear in-game days. Villagers age; a woman
		// and her lover of the same race and village may have a child (birthChancePerDay
		// percent per day, less when food runs short, none in famine; the pregnancy lasts
		// pregnancyDays). Children are drawn smaller, don't work or fight, and come of age at
		// the race's adult age; elders die of old age around the race's lifespan. Races other
		// than Elves use the Human ages.
		[Setting("Ageing, births and old age", "Villagers age, have children and die of old age.")]
		public bool lifeCycleEnabled = true;
		[Setting("Days in a year"), Range(1, 120)]
		public int lifeDaysPerYear = 16;
		[Setting("Humans come of age at (years)"), Range(0, 50)]
		public int humanAdultYears = 1;
		[Setting("Humans grow old at (years)"), Range(1, 100)]
		public int humanElderYears = 4;
		[Setting("Human lifespan (years)"), Range(1, 100)]
		public int humanLifespanYears = 6;
		[Setting("Elves come of age at (years)"), Range(0, 100)]
		public int elfAdultYears = 3;
		[Setting("Elves grow old at (years)"), Range(1, 200)]
		public int elfElderYears = 12;
		[Setting("Elf lifespan (years)"), Range(1, 200)]
		public int elfLifespanYears = 18;
		[Setting("Birth chance per day (%)", "Less when food runs short, none in famine."), Range(0, 100)]
		public int birthChancePerDay = 6;
		[Setting("Days a pregnancy lasts"), Range(1, 30)]
		public int pregnancyDays = 4;
		// Dementia: when a villager becomes an elder, dementiaChance percent of them grow
		// forgetful and forget one of the player's buildings they remember every
		// dementiaForgetDays days (with knowledgeEnabled; knowledge lives in people).
		[Setting("Elders who grow forgetful (%)", "They forget one of your buildings they remember every few days."), Range(0, 100)]
		public int dementiaChance = 33;
		[Setting("Days between forgotten buildings"), Range(1, 30)]
		public int dementiaForgetDays = 3;
		// Creatures (with lifeCycleEnabled): living natural creatures, wild or tamed (not the
		// player's), have an age and die of old age; the young are drawn smaller. Kinds the game
		// never replaces (trolls, orcs, goblins, kobolds, centaurs, mothmen, wurms, unicorns,
		// scorpions) have young now and then, up to the size their group had.
		[Setting("Creatures age and breed", "Wild and tamed creatures grow old and die; kinds the game never replaces have young now and then. Needs ageing.")]
		public bool creatureLifeEnabled = true;

		// Phase 5 - Settlements & Economy.
		// Village -> Town -> City. A village of townPopulation living villagers builds a Town
		// Hall; while it stands the village is a Town (a City from cityPopulation), and the
		// game's build planner may put up more homes and facilities. A settlement keeps its
		// tier down to 3/4 of that population, and loses it when its Town Hall is destroyed.
		[Section("Towns and Cities")]
		[Setting("Towns and Cities", "A big village builds a Town Hall and becomes a Town, then a City, and may build more.")]
		public bool settlementTiersEnabled = true;
		[Setting("Villagers for a Town"), Range(5, 200)]
		public int townPopulation = 20;
		[Setting("Villagers for a City"), Range(10, 400)]
		public int cityPopulation = 40;

		// Famine: a village where a third of the villagers have been starving (or
		// malnourished) for famineHours is in famine until no more than a tenth have been for
		// as long. No settlers move in, and once a day each starving villager may leave, with
		// famineLeaveChance percent, for a free home in a village of their faction with food.
		[Section("Food")]
		[Setting("Famine", "A village where many have starved for a while is in famine: no settlers, and the starving may leave for a village with food.")]
		public bool famineEnabled = true;
		[Setting("Hours of starving before a famine"), Range(1, 96)]
		public int famineHours = 12;
		[Setting("Chance a starving villager leaves (%)", "Rolled once a day for each starving villager during a famine."), Range(0, 100)]
		public int famineLeaveChance = 25;
		// Hunting: every 6 hours a hungry village (in famine, or a fifth of its villagers
		// starving) sends up to huntersPerTrip fighters, Hunters first, after wild animals
		// nearby; the meat is carried to the village's storage.
		[Setting("Hunting", "A hungry village sends fighters after wild animals nearby and brings the meat home.")]
		public bool huntingEnabled = true;
		[Setting("Hunters per trip"), Range(1, 10)]
		public int huntersPerTrip = 2;
		// Traders: once a day a village with food to spare (over 20 per villager plus
		// tradeAmount) sends a trader with tradeAmount food to the village that needs it most,
		// if their factions are not hostile and neither is under curfew. Traders also carry
		// news of the demons' buildings both ways.
		[Setting("Traders", "A village with food to spare sends a trader to the village that needs it most.")]
		public bool tradeEnabled = true;
		[Setting("Food a trader carries"), Range(5, 500)]
		public int tradeAmount = 40;

		// Unrest: every hour a village's unrest grows by what its people hold against the ruler
		// (famine and plague 1, an attack 1, recent deaths and lost buildings 0.5 each, the
		// unburied dead, homelessness and criminals walking free 0.5, a disliked ruler 1) and
		// falls by 1 when there is nothing. At unrestRestless the village is restless and thinks
		// less of its ruler each day; at unrestUprising it rises against the ruler.
		[Section("Unrest")]
		[Setting("Unrest", "A village's grievances against its ruler build up and can end in an uprising.")]
		public bool unrestEnabled = true;
		[Setting("Unrest for a restless village"), Range(1, 500)]
		public int unrestRestless = 24;
		[Setting("Unrest for an uprising"), Range(1, 1000)]
		public int unrestUprising = 72;
		// How an uprising plays out, a roll the people shape: a brawl, an assassination plot
		// (likelier with an Evil, Psychopath, Ruthless or Treacherous leader), jailing the ruler
		// (needs a prison; after two days the new ruler executes, exiles or releases them) or,
		// in a big village, a civil war whose losers are exiled. Set false: always a brawl.
		[Setting("Uprisings of every kind", "Brawls, assassination plots, jailing the ruler and civil wars. Off: always a brawl.")]
		public bool uprisingKindsEnabled = true;

		// Night watch (Phase 6): a Town or City with 4+ fighters keeps one guard per 8 residents
		// (1 to 3) on the night schedule; at night they walk the village and attack hostiles.
		[Section("Night watch")]
		[Setting("Night watch", "A Town or City with enough fighters keeps guards who walk the village at night.")]
		public bool nightWatchEnabled = true;

		// The Blight (Phase 7): Blight Hearts, a demonic building, grow corruption by
		// themselves. Every hour each Heart corrupts blightGrowthPerHour tiles (by level) at the
		// edge of the corruption it stands in, within blightReach tiles of it, and at most
		// blightTilesPerHour across the whole map. Deaths on a Heart's blight feed it:
		// blightFeedPerLevel deaths take it to level 2, twice as many to level 3. The blight
		// may cover a village's open ground (blightInVillages): crops there wither, but it never
		// counts for summoning or building. Fire on corrupted ground cleanses it
		// (blightFireCleanses). Up to blightHeartLimit Hearts at a time.
		[Section("Blight")]
		[Setting("The Blight", "Blight Hearts spread corruption by themselves and grow on deaths.")]
		public bool blightEnabled = true;
		[Setting("Most blighted tiles per hour", "Across the whole map."), Range(1, 1000)]
		public int blightTilesPerHour = 60;
		[Setting("Most Blight Hearts at a time"), Range(1, 20)]
		public int blightHeartLimit = 3;
		[Setting("Deaths for a Heart's next level"), Range(1, 50)]
		public int blightFeedPerLevel = 4;
		[Setting("Blight in villages", "The blight may cover a village's open ground; crops there wither.")]
		public bool blightInVillages = true;
		[Setting("Fire cleanses the blight")]
		public bool blightFireCleanses = true;
		public int[] blightReach = { 6, 9, 13 };
		public int[] blightGrowthPerHour = { 3, 5, 8 };

		public static RuinarchPlusConfig Current { get; internal set; } = new RuinarchPlusConfig();
	}
}
