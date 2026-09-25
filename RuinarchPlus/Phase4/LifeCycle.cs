using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Locations.Settlements;
using Ruinarch.ModContent;
using TMPro;
using UnityEngine;

namespace RuinarchPlus.Phase4
{
	/// <summary>
	/// Villagers are born, grow up, grow old and die of age (config: <c>lifeCycleEnabled</c>).
	///
	/// The game has no age at all. A year is <c>lifeDaysPerYear</c> (16) in-game days, as in
	/// Songs of Syx. Every villager (normal, sapient) has a birth tick and a personal age of
	/// death (the race's lifespan, give or take a fifth), kept in the player's save
	/// (<c>ModData/ruinarch.plus.life.json</c>). Villagers the mod meets without one (a new
	/// game, an older save, a migrant) get a random adult age, so elders exist from the start.
	///
	/// Births: once a day at 6:00, a woman of a village and her lover (the game's LOVER
	/// relationship) of the same race and village, both adults and she not an elder, may
	/// conceive (<c>birthChancePerDay</c>; halved with under 20 food per villager, none under
	/// 10 or in famine). After <c>pregnancyDays</c> the child is born in her home, created like
	/// the game's own migrants, with the game's PARENT/CHILD relationships.
	///
	/// Children (younger than the race's adult age) are drawn at 60% size (the game's own
	/// marker scale, <c>CharacterVisuals.markerVisualScale</c>), take no jobs (the limiter's
	/// <c>canTakeJobs</c>) and have the Farmer class, which does not fight. Both are patched,
	/// not saved: a save without the mod keeps plain villagers. At the adult age they come of
	/// age and get a class the game picks for them. Elders past their age of death die of old
	/// age (the game's plain death, plus an announcement).
	///
	/// Dementia: when a villager becomes an elder (or is first met as one), one in three
	/// (<c>dementiaChance</c>) grows forgetful. Every <c>dementiaForgetDays</c> a forgetful elder
	/// forgets one of the player's buildings they remember (Phase3/Knowledge.cs); the panel
	/// says "elder, forgetful". Kept in the same save record, not as a game trait, so a save
	/// without the mod keeps plain villagers.
	/// </summary>
	internal static class LifeCycle
	{
		private const string SaveId = "ruinarch.plus.life";
		private const float ChildScale = 0.6f;

		private sealed class Life
		{
			internal long Born;
			internal long DiesAt;
			internal bool Child;
			// Dementia was rolled when they became an elder; if forgetful, the next forgetting.
			internal bool ElderRolled;
			internal bool Forgetful;
			internal long NextForget;
		}

		private sealed class Pregnancy
		{
			internal Character Father;
			internal long Due;
		}

		private static readonly Dictionary<Character, Life> Lives = new Dictionary<Character, Life>();
		private static readonly Dictionary<Character, Pregnancy> Pregnancies = new Dictionary<Character, Pregnancy>();
		private static readonly HashSet<Character> Children = new HashSet<Character>();

		private static readonly System.Reflection.MethodInfo UpdateMarkerVisualSize = AccessTools.Method(typeof(CharacterVisuals), "UpdateMarkerVisualSize");

		internal static bool Enabled => RuinarchPlusConfig.Current.lifeCycleEnabled;

		internal static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		// ---- calendar ----------------------------------------------------------------------

		internal static long Now => Phase3.MissingPersons.Now;

		private static long TicksPerYear => (long)Math.Max(1, RuinarchPlusConfig.Current.lifeDaysPerYear) * GameManager.ticksPerDay;

		private static void Stages(RACE race, out int adult, out int elder, out int lifespan)
		{
			RuinarchPlusConfig cfg = RuinarchPlusConfig.Current;
			if (race == RACE.ELVES)
			{
				adult = cfg.elfAdultYears;
				elder = cfg.elfElderYears;
				lifespan = cfg.elfLifespanYears;
			}
			else
			{
				adult = cfg.humanAdultYears;
				elder = cfg.humanElderYears;
				lifespan = cfg.humanLifespanYears;
			}
		}

		// ---- queries -----------------------------------------------------------------------

		internal static bool IsChild(Character c) => c != null && Children.Contains(c);

		/// <summary>Age in years, or -1 if the mod does not know it.</summary>
		internal static float AgeYears(Character c)
		{
			return c != null && Lives.TryGetValue(c, out Life life) ? (Now - life.Born) / (float)TicksPerYear : -1f;
		}

		internal static bool IsPregnant(Character c) => c != null && Pregnancies.ContainsKey(c);

		internal static bool IsForgetful(Character c) => c != null && Lives.TryGetValue(c, out Life life) && life.Forgetful;

		/// <summary>"Child", "Adult" or "Elder", or null if the mod does not know the age.</summary>
		internal static string Stage(Character c)
		{
			float age = AgeYears(c);
			if (age < 0)
			{
				return null;
			}
			Stages(c.race, out int adult, out int elder, out _);
			return age < adult ? "Child" : age < elder ? "Adult" : "Elder";
		}

		private static bool Tracked(Character c)
		{
			return c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient();
		}

		/// <summary>Debug menu / test harness: make <paramref name="c"/> this old (years); their
		/// age of death moves with them. Takes effect at the next hourly check.</summary>
		internal static void SetAgeYears(Character c, float years)
		{
			if (!Tracked(c))
			{
				return;
			}
			if (!Lives.TryGetValue(c, out Life life))
			{
				life = NewLife(c, newborn: false);
			}
			long span = life.DiesAt - life.Born;
			life.Born = Now - (long)(years * TicksPerYear);
			life.DiesAt = life.Born + span;
		}

		/// <summary>Debug menu / test harness: <paramref name="c"/> dies of age at this age (years).</summary>
		internal static void SetDeathAgeYears(Character c, float years)
		{
			if (Tracked(c) && Lives.TryGetValue(c, out Life life))
			{
				life.DiesAt = life.Born + (long)(years * TicksPerYear);
			}
		}

		/// <summary>Debug menu / test harness: <paramref name="c"/> is forgetful (or not); a
		/// forgetful one forgets something at the next hourly check.</summary>
		internal static void SetForgetful(Character c, bool forgetful)
		{
			if (Tracked(c) && Lives.TryGetValue(c, out Life life))
			{
				life.ElderRolled = true;
				life.Forgetful = forgetful;
				life.NextForget = Now;
			}
		}

		private static long ForgetInterval => (long)Math.Max(1, RuinarchPlusConfig.Current.dementiaForgetDays) * GameManager.ticksPerDay;

		// ---- hourly ------------------------------------------------------------------------

		internal static void HourlyCheck()
		{
			if (!Enabled)
			{
				if (Children.Count > 0)
				{
					foreach (Character c in Children.ToList())
					{
						SetChild(c, false);
					}
				}
				return;
			}
			long now = Now;
			foreach (Character c in CharacterManager.Instance.allCharacters.ToList())
			{
				if (!Tracked(c))
				{
					continue;
				}
				if (!Lives.TryGetValue(c, out Life life))
				{
					life = NewLife(c, newborn: false);
				}
				Stages(c.race, out int adult, out _, out _);
				bool child = now - life.Born < adult * TicksPerYear;
				if (child != Children.Contains(c))
				{
					SetChild(c, child);
					if (!child && life.Child)
					{
						ComeOfAge(c);
					}
				}
				life.Child = child;
				if (now >= life.DiesAt)
				{
					DieOfAge(c);
					continue;
				}
				Dementia(c, life, now);
			}
			foreach (KeyValuePair<Character, Pregnancy> kv in Pregnancies.ToList())
			{
				Character mother = kv.Key;
				if (mother == null || mother.isDead || !(mother.homeSettlement is NPCSettlement))
				{
					Pregnancies.Remove(mother);
					continue;
				}
				if (now >= kv.Value.Due)
				{
					Pregnancies.Remove(mother);
					GiveBirth(mother, kv.Value.Father);
				}
			}
			if (GameManager.Instance.Today().tick == 6 * GameManager.ticksPerHour)
			{
				DailyConceptions();
			}
		}

		private static void Dementia(Character c, Life life, long now)
		{
			Stages(c.race, out _, out int elder, out _);
			if (!life.ElderRolled && now - life.Born >= elder * TicksPerYear)
			{
				life.ElderRolled = true;
				if (UnityEngine.Random.Range(0, 100) < RuinarchPlusConfig.Current.dementiaChance)
				{
					life.Forgetful = true;
					life.NextForget = now + ForgetInterval;
					Phase2.Curfew.Note("{0} has grown forgetful with age.", c);
				}
			}
			if (life.Forgetful && now >= life.NextForget)
			{
				life.NextForget = now + ForgetInterval;
				Inner_Maps.Location_Structures.LocationStructure forgotten = Phase3.Knowledge.ForgetOne(c);
				if (forgotten != null)
				{
					Phase2.Curfew.Note("{0} is forgetful and no longer remembers {1}.", c, forgotten);
				}
			}
		}

		private static Life NewLife(Character c, bool newborn)
		{
			Stages(c.race, out int adult, out int elder, out int lifespan);
			long year = TicksPerYear;
			// A personal age of death: the lifespan, give or take a fifth.
			long diesAtAge = (long)(lifespan * year * UnityEngine.Random.Range(0.8f, 1.2f));
			// Someone already grown: four in five in their adult years, the rest elders (not at
			// death's door: at most nine tenths of the way to their age of death).
			long adultAge = adult * year;
			long elderAge = Math.Min(elder * year, diesAtAge);
			long age = newborn ? 0
				: UnityEngine.Random.value < 0.8f || elderAge >= diesAtAge
					? adultAge + (long)(UnityEngine.Random.value * Math.Max(0, elderAge - adultAge))
					: elderAge + (long)(UnityEngine.Random.value * 0.9f * (diesAtAge - elderAge));
			Life life = new Life { Born = Now - age, DiesAt = Now - age + diesAtAge, Child = newborn };
			Lives[c] = life;
			return life;
		}

		private static void SetChild(Character c, bool child)
		{
			if (child)
			{
				Children.Add(c);
			}
			else
			{
				Children.Remove(c);
			}
			// Redraw at the new size (the game's own marker scale, patched below).
			if (c.visuals != null && c.hasMarker)
			{
				UpdateMarkerVisualSize?.Invoke(c.visuals, null);
			}
		}

		private static void ComeOfAge(Character c)
		{
			c.classComponent.RandomizeCurrentClassBasedOnAbleClasses();
			Phase2.Curfew.Announce("{0} has come of age and is now a " + c.characterClass.className + ".", c);
		}

		private static void DieOfAge(Character c)
		{
			int age = Mathf.FloorToInt(AgeYears(c));
			Lives.Remove(c);
			Pregnancies.Remove(c);
			Children.Remove(c);
			c.Death("normal");
			Phase2.Curfew.Announce("{0} died of old age at " + age + ".", c);
		}

		// ---- births ------------------------------------------------------------------------

		private static void DailyConceptions()
		{
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (settlements == null)
			{
				return;
			}
			foreach (NPCSettlement s in settlements.OfType<NPCSettlement>().ToList())
			{
				if (s.locationType != LOCATION_TYPE.VILLAGE || s.owner == null || !s.owner.isMajorNonPlayer || Phase5.Famine.IsInFamine(s))
				{
					continue;
				}
				List<Character> people = s.residents.Where(Tracked).ToList();
				if (people.Count == 0)
				{
					continue;
				}
				int foodEach = s.GetNumberOfFoodInWholeSettlement() / people.Count;
				int chance = foodEach >= 20 ? RuinarchPlusConfig.Current.birthChancePerDay : foodEach >= 10 ? RuinarchPlusConfig.Current.birthChancePerDay / 2 : 0;
				if (chance <= 0)
				{
					continue;
				}
				foreach (Character mother in people)
				{
					Character father = PartnerOf(mother);
					if (father != null && UnityEngine.Random.Range(0, 100) < chance)
					{
						Conceive(mother, father);
					}
				}
			}
		}

		/// <summary>Her lover, if the two may have a child now; else null.</summary>
		internal static Character PartnerOf(Character mother)
		{
			if (!Tracked(mother) || mother.gender != GENDER.FEMALE || IsPregnant(mother) || Stage(mother) != "Adult")
			{
				return null;
			}
			Character lover = mother.relationshipContainer.GetFirstCharacterWithRelationship(RELATIONSHIP_TYPE.LOVER);
			if (!Tracked(lover) || lover.gender != GENDER.MALE || lover.race != mother.race || lover.homeSettlement != mother.homeSettlement
				|| Stage(lover) == "Child" || Stage(lover) == null)
			{
				return null;
			}
			return lover;
		}

		internal static void Conceive(Character mother, Character father)
		{
			Pregnancies[mother] = new Pregnancy { Father = father, Due = Now + (long)Math.Max(1, RuinarchPlusConfig.Current.pregnancyDays) * GameManager.ticksPerDay };
			Phase2.Curfew.Note("{0} and {1} are expecting a child.", mother, father);
		}

		/// <summary>Creates the child in the mother's home (as the game creates migrants).</summary>
		internal static Character GiveBirth(Character mother, Character father)
		{
			NPCSettlement home = mother.homeSettlement as NPCSettlement;
			GENDER gender = UnityEngine.Random.Range(0, 2) == 0 ? GENDER.MALE : GENDER.FEMALE;
			Character child = CharacterManager.Instance.CreateNewCharacter("Farmer", mother.race, gender, mother.faction, home, home?.region, mother.homeStructure);
			NewLife(child, newborn: true);
			Children.Add(child);
			foreach (Character parent in new[] { mother, father })
			{
				if (parent != null && !parent.isDead)
				{
					RelationshipManager.Instance.CreateNewRelationshipBetween(child, parent, RELATIONSHIP_TYPE.PARENT);
				}
			}
			foreach (Character other in home?.residents.ToList() ?? new List<Character>())
			{
				if (other != child && !child.relationshipContainer.HasRelationshipWith(other))
				{
					RelationshipManager.Instance.CreateNewRelationshipDataBetween(child, other);
				}
			}
			child.CreateMarker();
			child.InitialCharacterPlacement(mother.gridTileLocation ?? home?.cityCenter?.tiles.FirstOrDefault());
			Generator.Map_Generation.Components.CharacterFinalization.ApplyFactionTypeRelatedEffectToMember(child.faction, child);
			if (father != null)
			{
				Phase2.Curfew.Announce("{0} and {1} of {2} had a child: {3}.", mother, father, home, child);
			}
			else
			{
				Phase2.Curfew.Announce("{0} of {1} had a child: {2}.", mother, home, child);
			}
			return child;
		}

		// ---- persistence -------------------------------------------------------------------
		// "L|characterId|born|diesAt|flags|nextForget" per villager (flags: e = dementia rolled,
		// f = forgetful; saves from before dementia have the first four fields only),
		// "P|motherId|fatherId|due" per pregnancy.

		private static string Save()
		{
			LifeSaveData file = new LifeSaveData();
			foreach (KeyValuePair<Character, Life> kv in Lives)
			{
				if (kv.Key != null && !kv.Key.isDead)
				{
					Life l = kv.Value;
					file.lives.Add($"L|{kv.Key.persistentID}|{l.Born}|{l.DiesAt}|{(l.ElderRolled ? "e" : "")}{(l.Forgetful ? "f" : "")}|{l.NextForget}");
				}
			}
			foreach (KeyValuePair<Character, Pregnancy> kv in Pregnancies)
			{
				if (kv.Key != null && !kv.Key.isDead)
				{
					file.lives.Add($"P|{kv.Key.persistentID}|{kv.Value.Father?.persistentID ?? ""}|{kv.Value.Due}");
				}
			}
			return file.lives.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Lives.Clear();
			Pregnancies.Clear();
			Children.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			foreach (string entry in JsonUtility.FromJson<LifeSaveData>(json)?.lives ?? new List<string>())
			{
				try
				{
					string[] f = entry.Split('|');
					Character c = f.Length >= 4 ? CharacterManager.Instance.GetCharacterByPersistentID(f[1]) : null;
					if (c == null)
					{
						continue;
					}
					if (f[0] == "L")
					{
						Lives[c] = new Life
						{
							Born = long.Parse(f[2]),
							DiesAt = long.Parse(f[3]),
							ElderRolled = f.Length >= 6 && f[4].Contains("e"),
							Forgetful = f.Length >= 6 && f[4].Contains("f"),
							NextForget = f.Length >= 6 ? long.Parse(f[5]) : 0
						};
						Stages(c.race, out int adult, out _, out _);
						Lives[c].Child = Now - Lives[c].Born < adult * TicksPerYear;
						if (Lives[c].Child)
						{
							Children.Add(c);
						}
					}
					else if (f[0] == "P")
					{
						Pregnancies[c] = new Pregnancy
						{
							Father = string.IsNullOrEmpty(f[2]) ? null : CharacterManager.Instance.GetCharacterByPersistentID(f[2]),
							Due = long.Parse(f[3])
						};
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Life cycle: dropped saved record {entry}: {e.Message}");
				}
			}
			RuinarchPlus.Log?.Info($"Life cycle loaded: {Lives.Count} ages, {Children.Count} child(ren), {Pregnancies.Count} pregnancy(ies), {Lives.Values.Count(l => l.Forgetful)} forgetful.");
		}
	}

	[Serializable]
	public class LifeSaveData
	{
		public List<string> lives = new List<string>();
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class LifeCycle_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % GameManager.ticksPerHour == 0)
				{
					LifeCycle.HourlyCheck();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Life cycle hourly failed: " + e.Message);
			}
		}
	}

	// Children are drawn smaller: the game's own marker scale (not saved; the game applies it
	// whenever it redraws the marker).
	[HarmonyPatch(typeof(CharacterVisuals), "get_markerVisualScale")]
	internal static class LifeCycle_ChildScale
	{
		private static readonly AccessTools.FieldRef<CharacterVisuals, Character> Owner = AccessTools.FieldRefAccess<CharacterVisuals, Character>("_owner");

		private static void Postfix(CharacterVisuals __instance, ref Vector2 __result)
		{
			if (LifeCycle.IsChild(Owner(__instance)))
			{
				__result *= 0.6f;
			}
		}
	}

	// Children take no jobs.
	[HarmonyPatch(typeof(LimiterComponent), "get_canTakeJobs")]
	internal static class LifeCycle_ChildNoJobs
	{
		private static void Postfix(LimiterComponent __instance, ref bool __result)
		{
			if (__result && LifeCycle.IsChild(__instance.owner))
			{
				__result = false;
			}
		}
	}

	// The character panel's class line gives the age: "Farmer, age 3", "Child, age 0".
	[HarmonyPatch(typeof(CharacterInfoUI), "UpdateSubTextAndIcon")]
	internal static class LifeCycle_AgeLabel
	{
		private static void Postfix(TextMeshProUGUI ___subLbl, Character ____activeCharacter)
		{
			try
			{
				Character c = ____activeCharacter;
				string stage = LifeCycle.Enabled ? LifeCycle.Stage(c) : null;
				if (___subLbl == null || stage == null)
				{
					return;
				}
				int age = Mathf.FloorToInt(LifeCycle.AgeYears(c));
				string what = stage == "Child" ? "Child" : stage == "Elder" ? ___subLbl.text + ", elder" + (LifeCycle.IsForgetful(c) ? ", forgetful" : "") : ___subLbl.text;
				___subLbl.text = $"{what}, age {age}" + (LifeCycle.IsPregnant(c) ? ", expecting" : "");
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Life cycle (age label) failed: " + e.Message);
			}
		}
	}

	// Children don't rule. While the game picks a village ruler or a faction leader, a child
	// counts as unavailable, the way someone the player holds does (both pickers skip
	// isBeingSeized: NPCSettlement.DesignateNewRuler, FactionSuccession's candidate check).
	internal static class LifeCycle_NoChildRulers
	{
		internal static int Picking;
	}

	[HarmonyPatch(typeof(NPCSettlement), nameof(NPCSettlement.DesignateNewRuler))]
	internal static class LifeCycle_RulerPick
	{
		private static void Prefix() => LifeCycle_NoChildRulers.Picking++;

		private static Exception Finalizer(Exception __exception)
		{
			LifeCycle_NoChildRulers.Picking--;
			return __exception;
		}
	}

	[HarmonyPatch(typeof(Faction), nameof(Faction.DesignateNewLeader))]
	internal static class LifeCycle_LeaderPick
	{
		private static void Prefix() => LifeCycle_NoChildRulers.Picking++;

		private static Exception Finalizer(Exception __exception)
		{
			LifeCycle_NoChildRulers.Picking--;
			return __exception;
		}
	}

	[HarmonyPatch(typeof(Character), "get_isBeingSeized")]
	internal static class LifeCycle_ChildNotCandidate
	{
		private static void Postfix(Character __instance, ref bool __result)
		{
			if (!__result && LifeCycle_NoChildRulers.Picking > 0 && LifeCycle.IsChild(__instance))
			{
				__result = true;
			}
		}
	}
}
