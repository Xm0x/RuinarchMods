using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using UnityEngine;

namespace RuinarchPlus.Phase5
{
	/// <summary>
	/// Unrest (config: <c>unrestEnabled</c>). In the base game a village never holds its ruler
	/// to account. Now each village keeps an unrest score fed every hour by what its people
	/// suffer, blamed on the ruler:
	/// - famine (<see cref="Famine"/>): 1 a hour;
	/// - plague (an outbreak in the village): 1;
	/// - an attack on the village (the game's siege state): 1;
	/// - deaths: each resident who died in the last 3 days, of anything but old age: 0.5 (at
	///   most 2);
	/// - the dead left unburied in the village: 0.5;
	/// - homelessness (a fifth of the villagers without a home): 0.5;
	/// - criminals walking free (a resident wanted by the village's faction, not held): 0.5;
	/// - buildings lost: each village building destroyed in the last 3 days: 0.5 (at most 1.5);
	/// - their rule itself: most villagers think ill of the ruler: 1.
	/// With nothing to complain about the score falls by 1 a hour. At <c>unrestRestless</c>
	/// the village is restless (announced, with its main grievances) and every day each
	/// villager thinks less of the ruler; it calms down below half of that. At
	/// <c>unrestUprising</c> an uprising breaks out: the villager who thinks least of the
	/// ruler leads everyone who dislikes them against the ruler and those who like them, a
	/// brawl in the game's own non-lethal combat. The ruler knocked out: the leader takes the
	/// rule (and the faction's leadership if the ruler held it). The rebels all knocked out,
	/// or 12 hours without a result: the ruler holds, and another uprising may come a day later.
	/// The score and the recent deaths and losses ride in the save
	/// (<c>ModData/ruinarch.plus.unrest.json</c>); an uprising in progress does not (the
	/// village rises again at the next check).
	/// </summary>
	public static class Unrest
	{
		private const string SaveId = "ruinarch.plus.unrest";
		private const long TicksPerHour = 20;
		private const long Window = 72 * TicksPerHour;
		private const int UprisingHours = 12;

		private sealed class State
		{
			internal float Points;
			internal bool Restless;
			internal int RestlessHours;
			internal long CalmUntil;
			internal readonly List<long> Deaths = new List<long>();
			internal readonly List<long> Losses = new List<long>();
		}

		private sealed class Uprising
		{
			internal Character Leader;
			internal Character Ruler;
			internal List<Character> Rebels;
			internal List<Character> Loyal;
			internal int Hours;
		}

		private static readonly Dictionary<NPCSettlement, State> States = new Dictionary<NPCSettlement, State>();
		private static readonly Dictionary<NPCSettlement, Uprising> Uprisings = new Dictionary<NPCSettlement, Uprising>();

		internal static bool Enabled => RuinarchPlusConfig.Current.unrestEnabled;

		private static long Now => Phase3.MissingPersons.Now;

		public static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		private static State StateOf(NPCSettlement s)
		{
			if (!States.TryGetValue(s, out State st))
			{
				States[s] = st = new State();
			}
			return st;
		}

		internal static float Points(NPCSettlement s) => s != null && States.TryGetValue(s, out State st) ? st.Points : 0f;

		internal static bool IsRestless(NPCSettlement s) => s != null && States.TryGetValue(s, out State st) && st.Restless;

		internal static bool HasUprising(NPCSettlement s) => s != null && Uprisings.ContainsKey(s);

		/// <summary>Debug menu / test harness: set a village's unrest score.</summary>
		internal static void SetPoints(NPCSettlement s, float points)
		{
			State st = StateOf(s);
			st.Points = Math.Max(0f, points);
			st.CalmUntil = 0;
		}

		/// <summary>What the village holds against its ruler now, and how much each weighs per hour.</summary>
		internal static List<KeyValuePair<string, float>> Grievances(NPCSettlement s)
		{
			List<KeyValuePair<string, float>> list = new List<KeyValuePair<string, float>>();
			State st = StateOf(s);
			long now = Now;
			st.Deaths.RemoveAll(t => now - t > Window);
			st.Losses.RemoveAll(t => now - t > Window);
			if (Famine.IsInFamine(s))
			{
				list.Add(new KeyValuePair<string, float>("the famine", 1f));
			}
			if (s.isPlagued || s.eventManager.HasActiveEvent(SETTLEMENT_EVENT.Plagued_Event))
			{
				list.Add(new KeyValuePair<string, float>("the plague", 1f));
			}
			if (s.isUnderSiege)
			{
				list.Add(new KeyValuePair<string, float>("the attacks on the village", 1f));
			}
			if (st.Deaths.Count > 0)
			{
				list.Add(new KeyValuePair<string, float>(st.Deaths.Count == 1 ? "a death" : $"{st.Deaths.Count} deaths", Math.Min(2f, 0.5f * st.Deaths.Count)));
			}
			if (Phase4.MigrationHealth.UnburiedDead(s) > 0)
			{
				list.Add(new KeyValuePair<string, float>("the unburied dead", 0.5f));
			}
			List<Character> people = Famine.Villagers(s);
			int homeless = people.Count(c => c.homeStructure == null || c.homeStructure.hasBeenDestroyed);
			if (homeless > 0 && homeless * 5 >= people.Count)
			{
				list.Add(new KeyValuePair<string, float>("the homeless", 0.5f));
			}
			if (s.owner != null && people.Any(c => c.crimeComponent.IsWantedBy(s.owner) && !c.traitContainer.HasTrait("Restrained")))
			{
				list.Add(new KeyValuePair<string, float>("criminals walking free", 0.5f));
			}
			if (st.Losses.Count > 0)
			{
				list.Add(new KeyValuePair<string, float>(st.Losses.Count == 1 ? "a lost building" : $"{st.Losses.Count} lost buildings", Math.Min(1.5f, 0.5f * st.Losses.Count)));
			}
			Character ruler = s.ruler;
			List<Character> judges = people.Where(c => c != ruler && !Phase4.LifeCycle.IsChild(c)).ToList();
			if (ruler != null && judges.Count >= 3 && judges.Count(c => c.relationshipContainer.GetTotalOpinion(ruler) < 0) * 2 > judges.Count)
			{
				list.Add(new KeyValuePair<string, float>("their rule", 1f));
			}
			return list;
		}

		/// <summary>"the famine, 2 deaths and the unburied dead": the heaviest grievances first.</summary>
		private static string Blame(List<KeyValuePair<string, float>> grievances)
		{
			List<string> top = grievances.OrderByDescending(g => g.Value).Take(3).Select(g => g.Key).ToList();
			return top.Count == 1 ? top[0] : string.Join(", ", top.Take(top.Count - 1)) + " and " + top[top.Count - 1];
		}

		// ---- events ------------------------------------------------------------------------

		internal static void OnDeath(NPCSettlement home)
		{
			if (Enabled && home != null && home.locationType == LOCATION_TYPE.VILLAGE && !Phase4.LifeCycle.DyingOfAge)
			{
				StateOf(home).Deaths.Add(Now);
			}
		}

		internal static void OnBuildingLost(NPCSettlement s)
		{
			if (Enabled && s != null && s.locationType == LOCATION_TYPE.VILLAGE)
			{
				StateOf(s).Losses.Add(Now);
			}
		}

		// ---- hourly ------------------------------------------------------------------------

		internal static void HourlyCheck()
		{
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (!Enabled || settlements == null)
			{
				return;
			}
			for (int i = 0; i < settlements.Count; i++)
			{
				try
				{
					if (settlements[i] is NPCSettlement s && s.locationType == LOCATION_TYPE.VILLAGE && s.owner != null && s.owner.isMajorFaction && s.cityCenter != null)
					{
						Update(s);
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Unrest check failed for {settlements[i]?.name}: {e.Message}");
				}
			}
		}

		private static void Update(NPCSettlement s)
		{
			if (Uprisings.ContainsKey(s))
			{
				Advance(s);
				return;
			}
			State st = StateOf(s);
			List<KeyValuePair<string, float>> grievances = Grievances(s);
			float pressure = grievances.Sum(g => g.Value);
			int restless = Math.Max(1, RuinarchPlusConfig.Current.unrestRestless);
			int uprising = Math.Max(restless, RuinarchPlusConfig.Current.unrestUprising);
			st.Points = pressure > 0f ? Math.Min(uprising, st.Points + pressure) : Math.Max(0f, st.Points - 1f);
			Character ruler = s.ruler;
			if (!st.Restless && st.Points >= restless && grievances.Count > 0)
			{
				st.Restless = true;
				st.RestlessHours = 0;
				if (ruler != null)
				{
					Phase2.Curfew.Announce("{0} is restless: its people blame {1} for " + Blame(grievances) + ".", s, ruler);
				}
				else
				{
					Phase2.Curfew.Announce("{0} is restless over " + Blame(grievances) + ".", s);
				}
			}
			else if (st.Restless && st.Points < restless / 2f)
			{
				st.Restless = false;
				Phase2.Curfew.Announce("{0} has calmed down.", s);
			}
			if (!st.Restless || ruler == null || ruler.isDead)
			{
				return;
			}
			st.RestlessHours++;
			if (st.RestlessHours % 24 == 0 && grievances.Count > 0)
			{
				string blame = "blamed for " + Blame(grievances);
				foreach (Character c in Famine.Villagers(s))
				{
					if (c != ruler)
					{
						c.relationshipContainer.AdjustOpinion(c, ruler, "Unrest", -10, blame, createJobsOnReduce: false);
					}
				}
			}
			if (st.Points >= uprising && Now >= st.CalmUntil)
			{
				Rise(s, st, ruler);
			}
		}

		// ---- the uprising ------------------------------------------------------------------

		private static bool CanTakePart(NPCSettlement s, Character c, Character ruler)
		{
			return c != ruler && c.faction == s.owner && c.hasMarker && !Phase4.LifeCycle.IsChild(c) && !c.isBeingSeized
				&& c.limiterComponent.canMove && c.limiterComponent.canPerform && c.carryComponent.isBeingCarriedBy == null
				&& !c.traitContainer.HasTrait("Restrained", "Enslaved", "Unconscious") && !c.crimeComponent.IsWantedBy(s.owner);
		}

		private static bool Down(Character c)
		{
			return c == null || c.isDead || !c.hasMarker || !c.limiterComponent.canPerform || c.traitContainer.HasTrait("Unconscious", "Restrained");
		}

		private static void Rise(NPCSettlement s, State st, Character ruler)
		{
			List<Character> pool = Famine.Villagers(s).Where(c => CanTakePart(s, c, ruler)).ToList();
			Character leader = pool.Where(c => c.relationshipContainer.GetTotalOpinion(ruler) < 0)
				.OrderBy(c => c.relationshipContainer.GetTotalOpinion(ruler))
				.ThenByDescending(c => (c.traitContainer.HasTrait("Ambitious") ? 1 : 0) + (c.traitContainer.HasTrait("Authoritative") ? 1 : 0))
				.FirstOrDefault();
			st.CalmUntil = Now + 24 * TicksPerHour;
			if (leader == null)
			{
				Phase2.Curfew.Note("{0} seethes, but nobody in it stands up to {1}.", s, ruler);
				return;
			}
			Uprising u = new Uprising
			{
				Leader = leader,
				Ruler = ruler,
				Rebels = pool.Where(c => c.relationshipContainer.GetTotalOpinion(ruler) < 0).ToList(),
				Loyal = pool.Where(c => c.relationshipContainer.GetTotalOpinion(ruler) > 0).ToList(),
			};
			Uprisings[s] = u;
			Phase2.Curfew.Announce($"{{0}} leads an uprising against {{1}} in {{2}}! ({u.Rebels.Count} rise against the ruler, {u.Loyal.Count} stand by them.)", leader, ruler, s);
			Engage(u);
		}

		// Everyone on each side goes for the other side (the game's own knockout brawl).
		private static void Engage(Uprising u)
		{
			List<Character> defenders = u.Loyal.Concat(new[] { u.Ruler }).Where(c => !Down(c)).ToList();
			List<Character> rebels = u.Rebels.Where(c => !Down(c)).ToList();
			foreach (Character r in rebels)
			{
				foreach (Character d in defenders)
				{
					r.combatComponent.Fight(d, CombatManager.Anger, null, isLethal: false);
				}
			}
			foreach (Character d in defenders)
			{
				foreach (Character r in rebels)
				{
					d.combatComponent.Fight(r, CombatManager.Anger, null, isLethal: false);
				}
			}
		}

		private static void Disengage(Uprising u)
		{
			List<Character> all = u.Rebels.Concat(u.Loyal).Concat(new[] { u.Ruler }).Where(c => c != null && !c.isDead).ToList();
			foreach (Character a in all)
			{
				foreach (Character b in all)
				{
					if (a != b)
					{
						a.combatComponent.RemoveHostileInRange(b);
					}
				}
			}
		}

		private static void Advance(NPCSettlement s)
		{
			Uprising u = Uprisings[s];
			u.Hours++;
			bool rulerDown = Down(u.Ruler) || s.ruler != u.Ruler;
			bool rebelsDown = u.Rebels.All(Down);
			// The ruler down: the leader takes the rule, or, knocked out too, the rebel still
			// standing who thinks least of the ruler.
			Character taker = !rulerDown ? null : !Down(u.Leader) ? u.Leader
				: u.Rebels.Where(c => !Down(c)).OrderBy(c => c.relationshipContainer.GetTotalOpinion(u.Ruler)).FirstOrDefault();
			if (taker != null)
			{
				End(s, u);
				TakeRule(s, taker, u.Ruler);
				foreach (Character c in u.Loyal.Where(c => !c.isDead))
				{
					c.relationshipContainer.AdjustOpinion(c, taker, "Uprising", -30, "overthrew the ruler", createJobsOnReduce: false);
				}
				State st = StateOf(s);
				st.Points = 0f;
				st.Restless = false;
				return;
			}
			if (rebelsDown || rulerDown || u.Hours >= UprisingHours)
			{
				End(s, u);
				if (!u.Ruler.isDead && !u.Leader.isDead && !u.Ruler.relationshipContainer.HasGrudgeAgainst(u.Leader))
				{
					u.Ruler.relationshipContainer.SetHasGrudgeAgainst(u.Ruler, u.Leader, p_state: true);
				}
				Phase2.Curfew.Announce(rebelsDown
					? "{0} has put down the uprising in {1}; {2} and the rebels are beaten."
					: "The uprising in {1} has failed: {0} keeps the rule, and {2} backs down.", u.Ruler, s, u.Leader);
				// (The ruler down with every rebel down too also lands here: nobody is left to take it.)
				StateOf(s).CalmUntil = Now + 24 * TicksPerHour;
				return;
			}
			Engage(u);
		}

		private static void End(NPCSettlement s, Uprising u)
		{
			Uprisings.Remove(s);
			Disengage(u);
		}

		/// <summary>
		/// <paramref name="leader"/> takes the rule of the village. A faction leader always
		/// rules their home village (<c>Faction.ProcessFactionLeaderAsSettlementRuler</c> puts
		/// them back), so a ruler who is the faction leader is overthrown as leader too, the way
		/// the game's own Overthrow Leader scheme does it (<c>Become_Faction_Leader</c>, a grudge).
		/// </summary>
		private static void TakeRule(NPCSettlement s, Character leader, Character ruler)
		{
			Faction faction = s.owner;
			bool factionLeader = faction != null && faction.leader == ruler;
			if (factionLeader)
			{
				leader.interruptComponent.TriggerInterrupt(INTERRUPT.Become_Faction_Leader, leader, "succession");
				if (s.ruler == ruler)
				{
					s.SetRuler(null);
				}
			}
			if (s.ruler != leader && (!leader.interruptComponent.TriggerInterrupt(INTERRUPT.Become_Settlement_Ruler, leader) || s.ruler != leader))
			{
				s.SetRuler(leader);
			}
			if (ruler.isDead)
			{
				Phase2.Curfew.Announce("{0} has taken the rule of {1} in an uprising.", leader, s);
				return;
			}
			ruler.relationshipContainer.AdjustOpinion(ruler, leader, "Deposed", -30, "took the rule of the village", createJobsOnReduce: false);
			if (!ruler.relationshipContainer.HasGrudgeAgainst(leader))
			{
				ruler.relationshipContainer.SetHasGrudgeAgainst(ruler, leader, p_state: true);
			}
			if (factionLeader)
			{
				Phase2.Curfew.Announce("{0} has overthrown {1} as leader of " + faction.name + " and taken the rule of {2} in an uprising.", leader, ruler, s);
			}
			else
			{
				Phase2.Curfew.Announce("{0} has taken the rule of {1} from {2} in an uprising.", leader, s, ruler);
			}
		}

		// ---- persistence -------------------------------------------------------------------
		// One "settlementId|points|restless|restlessHours|calmUntil|deathTicks|lossTicks" per
		// village with any unrest (tick lists comma-separated).

		private static string Save()
		{
			UnrestSaveData file = new UnrestSaveData();
			foreach (KeyValuePair<NPCSettlement, State> kv in States)
			{
				State st = kv.Value;
				if (kv.Key != null && (st.Points > 0f || st.Restless || st.Deaths.Count > 0 || st.Losses.Count > 0))
				{
					file.villages.Add(string.Join("|", kv.Key.persistentID, st.Points.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
						st.Restless ? "1" : "0", st.RestlessHours.ToString(), st.CalmUntil.ToString(), string.Join(",", st.Deaths), string.Join(",", st.Losses)));
				}
			}
			return file.villages.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			States.Clear();
			Uprisings.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			foreach (string entry in JsonUtility.FromJson<UnrestSaveData>(json)?.villages ?? new List<string>())
			{
				string[] p = entry.Split('|');
				if (p.Length < 7 || !(LandmarkManager.Instance.GetSettlementByPersistentID(p[0]) is NPCSettlement s))
				{
					continue;
				}
				State st = StateOf(s);
				float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out st.Points);
				st.Restless = p[2] == "1";
				int.TryParse(p[3], out st.RestlessHours);
				long.TryParse(p[4], out st.CalmUntil);
				st.Deaths.AddRange(p[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(long.Parse));
				st.Losses.AddRange(p[6].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(long.Parse));
			}
			RuinarchPlus.Log?.Info($"Unrest loaded: {States.Count} village(s), {States.Values.Count(v => v.Restless)} restless.");
		}
	}

	[Serializable]
	public class UnrestSaveData
	{
		public List<string> villages = new List<string>();
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class Unrest_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % 20 == 0)
				{
					Unrest.HourlyCheck();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Unrest hourly failed: " + e.Message);
			}
		}
	}

	// A resident's death is held against the ruler (old age excepted).
	[HarmonyPatch(typeof(Character), nameof(Character.Death))]
	internal static class Unrest_Death
	{
		private static void Prefix(Character __instance, out NPCSettlement __state)
		{
			__state = !__instance.isDead && __instance.isNormalCharacter && __instance.race.IsSapient() ? __instance.homeSettlement as NPCSettlement : null;
		}

		private static void Postfix(Character __instance, NPCSettlement __state)
		{
			try
			{
				if (__state != null && __instance.isDead)
				{
					Unrest.OnDeath(__state);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Unrest death failed: " + e.Message);
			}
		}
	}

	// A village building destroyed (burnt down, wrecked) is held against the ruler.
	[HarmonyPatch(typeof(ManMadeStructure), "DestroyStructure")]
	internal static class Unrest_BuildingLost
	{
		private static void Prefix(ManMadeStructure __instance, out NPCSettlement __state)
		{
			__state = !__instance.hasBeenDestroyed && __instance.structureType.IsVillageStructure() ? __instance.settlementLocation as NPCSettlement : null;
		}

		private static void Postfix(ManMadeStructure __instance, NPCSettlement __state)
		{
			try
			{
				if (__state != null && __instance.hasBeenDestroyed)
				{
					Unrest.OnBuildingLost(__state);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Unrest building loss failed: " + e.Message);
			}
		}
	}
}
