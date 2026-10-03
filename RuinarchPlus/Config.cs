using System;
using System.IO;
using UnityEngine;

namespace RuinarchPlus
{
	/// <summary>
	/// Player-editable config for Ruinarch+. On first load a <c>config.json</c> is
	/// written next to the mod DLL with default values; the player edits it and the
	/// flags take effect on the next launch. Kept dependency-free via UnityEngine's
	/// JsonUtility (no Newtonsoft needed).
	/// </summary>
	[Serializable]
	public class RuinarchPlusConfig
	{
		// Off by default: base game behaviour is unchanged until the player opts in.
		public bool disableTutorial = false;
		// Phase 1 exploit fixes (on by default; set false to keep the exploits): Sacrifice or
		// Let It Go cast on a Kennel no longer takes a flying monster that is only passing over.
		public bool closeExploits = true;
		// Explosions your side sets off (poison and frozen explosions, chain lightning, from your
		// spells or your demons) no longer damage your demonic buildings, as your spells already
		// don't. Set false for the base game (the Portal's defenders can wear it down).
		public bool friendlyExplosionsSpareBuildings = true;

		// Phase 2 - Death, Decay & Disease.
		// Unburied corpses left in the open rot over time and eventually vanish,
		// instead of littering the map forever. Buried graves (in a cemetery) persist.
		public bool corpseDecayEnabled = true;
		// In-game days a corpse takes to fully decompose (480 ticks/day). Floor is 1/4 day.
		public float corpseDecayDays = 3f;
		// Mass Grave burial: villages with no Cemetery/Cult Temple stop scattering tombstones
		// into the wilderness. Corpses lie where they fell until the settlement has a Mass
		// Grave; then villagers carry every corpse (people and creatures) into it.
		public bool massGraveBurialEnabled = true;
		// In-game hours a corpse near a Mass Grave may go un-hauled (e.g. nobody left alive to
		// carry it) before the pit absorbs it directly.
		public int massGraveFallbackHours = 12;

		// Settlement curfew: a ruler who answers a plague outbreak with Quarantine or Exile also
		// orders residents home in their free time until the plague event ends.
		public bool curfewEnabled = true;
		// Closed borders: a village under curfew turns away visitors (free-time visits, visits
		// to friends, traders). Raids, rescues and bounty hunts still come.
		public bool closedBordersEnabled = true;

		// Phase 3 - Knowledge & Fog of War.
		// Factions only attack demonic structures they know about. A villager who sees one
		// carries the news home (killed on the way, it is lost); a village next door counts
		// only once it knows something there. Counterattacks no longer march on a portal
		// nobody has seen.
		public bool knowledgeEnabled = true;
		// Gossip: percent chance, per building, that a villager tells someone of another
		// (non-hostile) faction about a demonic building they know of; kin always pass on news
		// they carry. 0 turns gossip off.
		public int gossipChance = 25;
		// A village only knows where its people are if it has seen them. A resident none of
		// their people has seen for missingAfterHours is reported missing (someone away at
		// work told the village where they went and is not), and the village
		// sends a search party to where they were last seen. A failed search is retried after
		// searchRetryHours, doubling each time; after searchMaxAttempts failures the village
		// gives them up. Replaces the base game's rescue of captives nobody saw.
		public bool missingPersonsEnabled = true;
		public int missingAfterHours = 24;
		// Hours a search party sweeps around the last-seen spot before giving up.
		public int searchSweepHours = 6;
		public int searchRetryHours = 24;
		public int searchMaxAttempts = 3;

		// Phase 4 - Living Population.
		// Migration follows the village's fortunes: no settlers during plague or siege, fewer
		// while homes stand empty or the dead lie unburied, and each death sets the migration
		// meter back. The player's Induce Migration skill is unaffected.
		public bool migrationHealthEnabled = true;

		// Phase 5 - Settlements & Economy.
		// Village -> Town -> City. A village of townPopulation living villagers builds a Town
		// Hall; while it stands the village is a Town (a City from cityPopulation), and the
		// game's build planner may put up more homes and facilities. A settlement keeps its
		// tier down to 3/4 of that population, and loses it when its Town Hall is destroyed.
		public bool settlementTiersEnabled = true;
		public int townPopulation = 20;
		public int cityPopulation = 40;
		// Famine: a village where a third of the villagers have been starving (or
		// malnourished) for famineHours is in famine until no more than a tenth have been for
		// as long. No settlers move in, and once a day each starving villager may leave, with
		// famineLeaveChance percent, for a free home in a village of their faction with food.
		public bool famineEnabled = true;
		public int famineHours = 12;
		public int famineLeaveChance = 25;
		// Unrest: every hour a village's unrest grows by what its people hold against the ruler
		// (famine and plague 1, an attack 1, recent deaths and lost buildings 0.5 each, the
		// unburied dead, homelessness and criminals walking free 0.5, a disliked ruler 1) and
		// falls by 1 when there is nothing. At unrestRestless the village is restless and thinks
		// less of its ruler each day; at unrestUprising it rises against the ruler.
		public bool unrestEnabled = true;
		public int unrestRestless = 24;
		public int unrestUprising = 72;
		// How an uprising plays out, a roll the people shape: a brawl, an assassination plot
		// (likelier with an Evil, Psychopath, Ruthless or Treacherous leader), jailing the ruler
		// (needs a prison; after two days the new ruler executes, exiles or releases them) or,
		// in a big village, a civil war whose losers are exiled. Set false: always a brawl.
		public bool uprisingKindsEnabled = true;
		// Hunting: every 6 hours a hungry village (in famine, or a fifth of its villagers
		// starving) sends up to huntersPerTrip fighters, Hunters first, after wild animals
		// nearby; the meat is carried to the village's storage.
		public bool huntingEnabled = true;
		public int huntersPerTrip = 2;
		// Traders: once a day a village with food to spare (over 20 per villager plus
		// tradeAmount) sends a trader with tradeAmount food to the village that needs it most,
		// if their factions are not hostile and neither is under curfew. Traders also carry
		// news of the demons' buildings both ways.
		public bool tradeEnabled = true;
		public int tradeAmount = 40;
		// Night watch (Phase 6): a Town or City with 4+ fighters keeps one guard per 8 residents
		// (1 to 3) on the night schedule; at night they walk the village and attack hostiles.
		public bool nightWatchEnabled = true;
		// The Blight (Phase 7): Blight Hearts, a demonic building, grow corruption by
		// themselves. Every hour each Heart corrupts blightGrowthPerHour tiles (by level) at the
		// edge of the corruption it stands in, within blightReach tiles of it, and at most
		// blightTilesPerHour across the whole map. Deaths on a Heart's blight feed it:
		// blightFeedPerLevel deaths take it to level 2, twice as many to level 3. The blight
		// may cover a village's open ground (blightInVillages): crops there wither, but it never
		// counts for summoning or building. Fire on corrupted ground cleanses it
		// (blightFireCleanses). Up to blightHeartLimit Hearts at a time.
		public bool blightEnabled = true;
		public int blightTilesPerHour = 60;
		public int blightHeartLimit = 3;
		public int[] blightReach = { 6, 9, 13 };
		public int[] blightGrowthPerHour = { 3, 5, 8 };
		public int blightFeedPerLevel = 4;
		public bool blightInVillages = true;
		public bool blightFireCleanses = true;

		// Life cycle (Phase 4): a year is lifeDaysPerYear in-game days. Villagers age; a woman
		// and her lover of the same race and village may have a child (birthChancePerDay
		// percent per day, less when food runs short, none in famine; the pregnancy lasts
		// pregnancyDays). Children are drawn smaller, don't work or fight, and come of age at
		// the race's adult age; elders die of old age around the race's lifespan. Races other
		// than Elves use the Human ages.
		public bool lifeCycleEnabled = true;
		public int lifeDaysPerYear = 16;
		public int humanAdultYears = 1;
		public int humanElderYears = 4;
		public int humanLifespanYears = 6;
		public int elfAdultYears = 3;
		public int elfElderYears = 12;
		public int elfLifespanYears = 18;
		public int birthChancePerDay = 6;
		public int pregnancyDays = 4;
		// Dementia: when a villager becomes an elder, dementiaChance percent of them grow
		// forgetful and forget one of the player's buildings they remember every
		// dementiaForgetDays days (with knowledgeEnabled; knowledge lives in people).
		public int dementiaChance = 33;
		public int dementiaForgetDays = 3;
		// Creatures (with lifeCycleEnabled): living natural creatures, wild or tamed (not the
		// player's), have an age and die of old age; the young are drawn smaller. Kinds the game
		// never replaces (trolls, orcs, goblins, kobolds, centaurs, mothmen, wurms, unicorns,
		// scorpions) have young now and then, up to the size their group had.
		public bool creatureLifeEnabled = true;
		// Records (with knowledgeEnabled): households keep a record of the player's buildings
		// on their dwelling's Book Shelf (or a Book the mod places); a Town or City builds a
		// Library (with settlementTiersEnabled) whose Book Shelves, or libraryBooks Books, keep
		// the village's. In free time a villager at home writes what the record lacks (an hour
		// at the shelf) and, readChance percent per hour, reads what they do not remember;
		// libraryVisitChance percent per free-time hour they go to write or read in the
		// Library. Burning the shelves and Books loses the records.
		public bool recordsEnabled = true;
		public int readChance = 25;
		public int libraryVisitChance = 10;
		public int libraryBooks = 4;

		// Corpse-borne disease. Rotting unburied corpses in a settlement sicken the living
		// present, scaled by corpse count. Requires corpseDecayEnabled (it reads the decay stage).
		public bool corpseDiseaseEnabled = true;
		// Percent infection chance, per rotting corpse, per in-game hour, per nearby villager.
		public int corpseDiseaseChancePerCorpse = 3;

		public static RuinarchPlusConfig Current { get; private set; } = new RuinarchPlusConfig();

		public static void Load(string modDirectory)
		{
			try
			{
				string path = Path.Combine(modDirectory, "config.json");
				if (File.Exists(path))
				{
					string json = File.ReadAllText(path);
					RuinarchPlusConfig loaded = JsonUtility.FromJson<RuinarchPlusConfig>(json);
					if (loaded != null)
					{
						Current = loaded;
					}
					RuinarchPlus.Log?.Info($"Config loaded: disableTutorial={Current.disableTutorial}");
				}
				else
				{
					// Seed a default file so the player has something to edit.
					File.WriteAllText(path, JsonUtility.ToJson(Current, prettyPrint: true));
					RuinarchPlus.Log?.Info($"Config not found; wrote defaults to {path}");
				}
			}
			catch (Exception e)
			{
				// Never take the game down over a bad config: fall back to defaults.
				RuinarchPlus.Log?.Warning($"Config load failed ({e.Message}); using defaults.");
				Current = new RuinarchPlusConfig();
			}
		}
	}
}
