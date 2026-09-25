using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using UnityEngine;

namespace RuinarchPlus.Phase4
{
	/// <summary>
	/// Creatures age too (config: <c>creatureLifeEnabled</c>, with <c>lifeCycleEnabled</c>).
	///
	/// Living natural creatures, wild or tamed by a village (never the player's), have an age
	/// in the same years as villagers and die of old age around their kind's lifespan (give or
	/// take a fifth); undead, demons, constructs, elementals and spirits have none. They are
	/// young for the first quarter of their lifespan (drawn at 60% size, like children) and
	/// elders from three quarters. Creatures the world began with get a random age; any the
	/// game spawns or hatches later is born then. One the game grows up (Small Spider to Giant
	/// Spider, Wyvernling to Wyvern) keeps its age and is grown from then on.
	///
	/// The game already renews game animals (hunting areas), den beasts (dens) and egg layers.
	/// Kinds it never replaces (<see cref="Breeders"/>) have young of their own: a group (one
	/// kind sharing a home, a territory or a village) with a grown male and female may have a
	/// young about once a year, while it is smaller than it was when first seen (2 to 6).
	/// Ages and group sizes ride in the life cycle's save record; deaths of old age and births
	/// go to the log only.
	/// </summary>
	internal static partial class LifeCycle
	{
		// Lifespans in years.
		private static readonly Dictionary<RACE, float> CreatureLifespans = new Dictionary<RACE, float>
		{
			{ RACE.RABBIT, 2f }, { RACE.RAT, 2f }, { RACE.CHICKEN, 2f }, { RACE.MINK, 2f },
			{ RACE.PIG, 4f }, { RACE.SHEEP, 4f }, { RACE.BOAR, 4f }, { RACE.WOLF, 4f }, { RACE.MOONWALKER, 4f }, { RACE.SCORPION, 4f }, { RACE.SPIDER, 4f },
			{ RACE.BEAR, 6f }, { RACE.TROLL, 6f }, { RACE.ORC, 6f }, { RACE.GOBLIN, 6f }, { RACE.KOBOLD, 6f },
			{ RACE.CENTAUR, 8f }, { RACE.HARPY, 8f }, { RACE.TRITON, 8f }, { RACE.MOTHMAN, 8f },
			{ RACE.WYVERN, 12f }, { RACE.WURM, 12f }, { RACE.UNICORN, 12f },
			{ RACE.DRAGON, 40f },
		};

		// Kinds the game never replaces: they have young of their own.
		private static readonly HashSet<RACE> Breeders = new HashSet<RACE>
		{
			RACE.TROLL, RACE.ORC, RACE.GOBLIN, RACE.KOBOLD, RACE.CENTAUR, RACE.MOTHMAN, RACE.WURM, RACE.UNICORN, RACE.SCORPION
		};

		private const int MinGroup = 2;
		private const int MaxGroup = 6;

		private static readonly HashSet<Character> Young = new HashSet<Character>();
		// A breeding group's size when first seen: "kind/place" -> size.
		private static readonly Dictionary<string, int> Groups = new Dictionary<string, int>();
		// Set until the first hourly check of a game whose save has no creature ages.
		private static bool _seedCreatures = true;

		internal static bool CreaturesEnabled => Enabled && RuinarchPlusConfig.Current.creatureLifeEnabled;

		/// <summary>A living natural creature that is not the player's.</summary>
		internal static bool IsCreature(Character c)
		{
			return c is Summon && !c.isDead && !c.isNormalCharacter && CreatureLifespans.ContainsKey(c.race)
				&& !(c.faction != null && c.faction.isPlayerFaction);
		}

		/// <summary>Drawn smaller: a child, or a creature's young (not the game's own young kinds,
		/// Small Spider and Wyvernling, which it already draws small).</summary>
		internal static bool IsSmall(Character c)
		{
			return c != null && (Children.Contains(c) || Young.Contains(c) && !(c is Summon s && s.adultSummonType != SUMMON_TYPE.None));
		}

		private static void Stages(Character c, out float adult, out float elder, out float lifespan)
		{
			if (!c.isNormalCharacter && CreatureLifespans.TryGetValue(c.race, out lifespan))
			{
				adult = lifespan / 4f;
				elder = lifespan * 0.75f;
				return;
			}
			Stages(c.race, out int a, out int e, out int l);
			adult = a;
			elder = e;
			lifespan = l;
		}

		/// <summary>The breeding group <paramref name="c"/> belongs to, or null (a loner).</summary>
		internal static string GroupOf(Character c)
		{
			string place = c.homeStructure != null ? "s:" + c.homeStructure.persistentID
				: c.territory != null ? "a:" + c.territory.persistentID
				: c.homeSettlement != null ? "v:" + c.homeSettlement.persistentID
				: null;
			return place == null || !(c is Summon s) ? null : s.summonType + "/" + place;
		}

		/// <summary>The size a breeding group may grow back to (0 if not yet seen).</summary>
		internal static int GroupSize(string group) => group != null && Groups.TryGetValue(group, out int size) ? size : 0;

		/// <summary>Debug menu / test harness: every breeding group with room and a pair has a
		/// young now (no daily roll). Returns the young born.</summary>
		internal static List<Summon> BreedNow()
		{
			List<Summon> born = new List<Summon>();
			CreatureGroups(CharacterManager.Instance.allCharacters.Where(IsCreature).ToList(), true, born);
			return born;
		}

		// Records new groups' sizes; in the morning, groups below theirs may have a young
		// (every group with room when <paramref name="forced"/>, which collects the young).
		private static void CreatureGroups(List<Character> creatures, bool morning, List<Summon> forced = null)
		{
			foreach (Character c in Young.ToList())
			{
				if (!IsCreature(c))
				{
					SetChild(c, false);
				}
			}
			foreach (IGrouping<string, Character> group in creatures.Where(c => Breeders.Contains(c.race)).GroupBy(GroupOf))
			{
				if (group.Key == null)
				{
					continue;
				}
				List<Character> members = group.ToList();
				if (!Groups.TryGetValue(group.Key, out int size))
				{
					Groups[group.Key] = size = Mathf.Clamp(members.Count, MinGroup, MaxGroup);
				}
				if (morning && members.Count < size && (forced != null || UnityEngine.Random.value < 1f / Math.Max(1, RuinarchPlusConfig.Current.lifeDaysPerYear)))
				{
					Summon mother = ParentsIn(members);
					Summon young = mother == null ? null : BearYoung(mother);
					if (young != null)
					{
						forced?.Add(young);
					}
				}
			}
		}

		/// <summary>A grown female free to bear young, if the group also has a grown male.</summary>
		internal static Summon ParentsIn(List<Character> members)
		{
			if (!members.Any(c => c.gender == GENDER.MALE && (Stage(c) == "Adult" || Stage(c) == "Elder")))
			{
				return null;
			}
			return members.OfType<Summon>().FirstOrDefault(c => c.gender == GENDER.FEMALE && Stage(c) == "Adult"
				&& c.hasMarker && c.gridTileLocation != null && c.carryComponent.isBeingCarriedBy == null
				&& !c.traitContainer.HasTrait("Restrained") && !(c.currentStructure is DemonicStructure));
		}

		/// <summary>A young of the mother's kind, home and faction, born where she stands.</summary>
		internal static Summon BearYoung(Summon mother)
		{
			LocationGridTile tile = mother.gridTileLocation;
			if (tile == null)
			{
				return null;
			}
			Summon young = CharacterManager.Instance.CreateNewSummon(mother.summonType, mother.faction, mother.homeSettlement,
				mother.homeRegion ?? tile.parentMap.region, mother.homeStructure, "", bypassIdeologyChecking: true);
			CharacterManager.Instance.PlaceSummonInitially(young, tile);
			if (mother.homeStructure == null && mother.territory != null)
			{
				young.SetTerritory(mother.territory, returnHome: false);
			}
			NewLife(young, newborn: true);
			SetChild(young, true);
			Phase2.Curfew.Note("{0} had a young: {1}.", mother, young);
			return young;
		}

		/// <summary>The game grew <paramref name="young"/> into <paramref name="grown"/> (a new
		/// character): the grown one keeps the age, and is grown from now on.</summary>
		internal static void Inherit(Summon grown, Character young)
		{
			if (grown == null || young == null || young.isNormalCharacter || !IsCreature(grown) || !Lives.TryGetValue(young, out Life life))
			{
				return;
			}
			Stages(grown, out float adult, out _, out _);
			long born = Math.Min(life.Born, Now - (long)(adult * TicksPerYear));
			Lives[grown] = new Life { Born = born, DiesAt = Math.Max(life.DiesAt, born + 1), Child = false };
		}

		private static void SaveCreatures(List<string> lines)
		{
			lines.Add("V|2");
			foreach (KeyValuePair<string, int> kv in Groups)
			{
				lines.Add($"G|{kv.Key}|{kv.Value}");
			}
		}

		/// <summary>Reads a creature record ("V", "G"); false for any other record.</summary>
		private static bool LoadCreatures(string[] f)
		{
			if (f[0] == "V")
			{
				_seedCreatures = false;
				return true;
			}
			if (f[0] == "G")
			{
				if (f.Length >= 3)
				{
					Groups[f[1]] = int.Parse(f[2]);
				}
				return true;
			}
			return false;
		}
	}

	// A creature the game grows up (a new character of the grown kind) keeps its age.
	[HarmonyPatch(typeof(CharacterManager), nameof(CharacterManager.SpawnNewMonsterInstanceFrom),
		new[] { typeof(SUMMON_TYPE), typeof(Character), typeof(BaseSettlement), typeof(LocationStructure), typeof(LocationGridTile), typeof(Faction), typeof(bool) })]
	internal static class LifeCycle_GrownKeepsAge
	{
		private static void Postfix(Summon __result, Character p_target)
		{
			try
			{
				LifeCycle.Inherit(__result, p_target);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Life cycle (grown creature) failed: " + e.Message);
			}
		}
	}
}
