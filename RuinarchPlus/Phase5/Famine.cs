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
	/// Famine (config: <c>famineEnabled</c>). The game tracks hunger per villager (fullness,
	/// Starving at 20 or below, Malnourished at 0) but a village never notices that its
	/// people are going hungry. Now a village where at least a third of the villagers have
	/// been starving or malnourished for <c>famineHours</c> is in famine, until no more than
	/// a tenth have been for as long. During a famine:
	/// - it is announced in the event log, and so is its end;
	/// - nobody moves in (migration, <c>Phase4/MigrationHealth</c>);
	/// - once a day each starving villager (not the ruler or faction leader) may leave, with
	///   <c>famineLeaveChance</c>, for a free home in another village of their faction that is
	///   not in famine: the game's own move of home (<c>Character.MigrateHomeStructureTo</c>).
	/// - unrest (config <c>unrestEnabled</c>): after <c>unrestHours</c> of famine the village is
	///   restless (announced) and, once a day, every villager thinks less of the ruler
	///   (opinion "Famine"); after <c>challengeHours</c> the ruler is challenged: the villager
	///   who thinks least of them takes the rule of the village (the game's own "became
	///   ruler" interrupt). Once per famine.
	/// Uses the game's own hunger state, so it works whatever runs the village out of food
	/// (lost farmers, a burnt farm, too many mouths). Active famines ride in the save
	/// (<c>ModData/ruinarch.plus.famine.json</c>).
	/// </summary>
	public static class Famine
	{
		private const string SaveId = "ruinarch.plus.famine";

		private sealed class State
		{
			internal int HungryHours;
			internal int FedHours;
			internal bool Active;
			internal int ActiveHours;
			internal bool Challenged;
		}

		private static readonly Dictionary<NPCSettlement, State> States = new Dictionary<NPCSettlement, State>();

		internal static bool Enabled => RuinarchPlusConfig.Current.famineEnabled;

		public static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		internal static bool IsInFamine(NPCSettlement s)
		{
			return Enabled && s != null && States.TryGetValue(s, out State st) && st.Active;
		}

		private static List<Character> Villagers(NPCSettlement s)
		{
			return s.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()).ToList();
		}

		private static bool IsStarving(Character c)
		{
			return c.needsComponent.isStarving || c.traitContainer.HasTrait("Malnourished");
		}

		/// <summary>Starving villagers and all villagers of <paramref name="s"/>.</summary>
		internal static int Starving(NPCSettlement s, out int villagers)
		{
			List<Character> people = Villagers(s);
			villagers = people.Count;
			return people.Count(IsStarving);
		}

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
					if (settlements[i] is NPCSettlement s && s.locationType == LOCATION_TYPE.VILLAGE && s.owner != null && s.owner.isMajorFaction)
					{
						Update(s);
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Famine check failed for {settlements[i]?.name}: {e.Message}");
				}
			}
		}

		private static void Update(NPCSettlement s)
		{
			if (!States.TryGetValue(s, out State st))
			{
				States[s] = st = new State();
			}
			int starving = Starving(s, out int villagers);
			if (villagers < 3)
			{
				st.HungryHours = 0;
				st.FedHours = 0;
				if (st.Active)
				{
					End(s, st);
				}
				return;
			}
			st.HungryHours = starving * 3 >= villagers ? st.HungryHours + 1 : 0;
			st.FedHours = starving * 10 <= villagers ? st.FedHours + 1 : 0;
			int hours = Math.Max(1, RuinarchPlusConfig.Current.famineHours);
			if (!st.Active)
			{
				if (st.HungryHours >= hours)
				{
					st.Active = true;
					st.ActiveHours = 0;
					Phase2.Curfew.Announce($"Famine in {{0}}: {starving} of {villagers} villagers are starving.", s);
				}
				return;
			}
			if (st.FedHours >= hours)
			{
				End(s, st);
				return;
			}
			st.ActiveHours++;
			if (st.ActiveHours % 24 == 0)
			{
				Emigrate(s);
			}
			if (RuinarchPlusConfig.Current.unrestEnabled)
			{
				Unrest(s, st);
			}
		}

		// A long famine turns the village against its ruler.
		private static void Unrest(NPCSettlement s, State st)
		{
			Character ruler = s.ruler;
			int unrest = Math.Max(1, RuinarchPlusConfig.Current.unrestHours);
			if (ruler == null || ruler.isDead || st.ActiveHours < unrest)
			{
				return;
			}
			if (st.ActiveHours == unrest)
			{
				Phase2.Curfew.Announce("{0} is restless: its people blame {1} for the famine.", s, ruler);
			}
			if ((st.ActiveHours - unrest) % 24 == 0)
			{
				foreach (Character c in Villagers(s))
				{
					if (c != ruler)
					{
						c.relationshipContainer.AdjustOpinion(c, ruler, "Famine", -10, "the famine", createJobsOnReduce: false);
					}
				}
			}
			if (!st.Challenged && st.ActiveHours >= Math.Max(unrest, RuinarchPlusConfig.Current.challengeHours))
			{
				st.Challenged = true;
				Challenge(s, ruler, st.ActiveHours);
			}
		}

		/// <summary>
		/// The villager who thinks least of the ruler (the ambitious and authoritative first
		/// among equals) takes the rule of the village. A faction leader always rules their home
		/// village (<c>Faction.ProcessFactionLeaderAsSettlementRuler</c> puts them back), so a
		/// ruler who is the faction leader is overthrown as leader too, the way the game's own
		/// Overthrow Leader scheme does it (<c>Become_Faction_Leader</c>, a grudge).
		/// </summary>
		private static void Challenge(NPCSettlement s, Character ruler, int hours)
		{
			Character challenger = Villagers(s)
				.Where(c => c != ruler && c.faction == s.owner && !c.isBeingSeized && !c.crimeComponent.IsWantedBy(s.owner) && !c.traitContainer.HasTrait("Enslaved") && !Phase4.LifeCycle.IsChild(c)
					&& c.gridTileLocation != null && c.gridTileLocation.IsPartOfSettlement(s))
				.OrderBy(c => c.relationshipContainer.GetTotalOpinion(ruler))
				.ThenByDescending(c => (c.traitContainer.HasTrait("Ambitious") ? 1 : 0) + (c.traitContainer.HasTrait("Authoritative") ? 1 : 0))
				.FirstOrDefault();
			if (challenger == null)
			{
				Phase2.Curfew.Note("Nobody in {0} stands up to {1}.", s, ruler);
				return;
			}
			string how = hours >= 48 ? $"{hours / 24} days" : $"{hours} hours";
			Faction faction = s.owner;
			bool leader = faction != null && faction.leader == ruler;
			if (leader)
			{
				challenger.interruptComponent.TriggerInterrupt(INTERRUPT.Become_Faction_Leader, challenger, "succession");
				if (s.ruler == ruler)
				{
					s.SetRuler(null);
				}
			}
			if (s.ruler != challenger && (!challenger.interruptComponent.TriggerInterrupt(INTERRUPT.Become_Settlement_Ruler, challenger) || s.ruler != challenger))
			{
				s.SetRuler(challenger);
			}
			ruler.relationshipContainer.AdjustOpinion(ruler, challenger, "Deposed", -30, "took the rule of the village", createJobsOnReduce: false);
			if (leader)
			{
				if (!ruler.relationshipContainer.HasGrudgeAgainst(challenger))
				{
					ruler.relationshipContainer.SetHasGrudgeAgainst(ruler, challenger, p_state: true);
				}
				Phase2.Curfew.Announce($"{{0}} has overthrown {{1}} as leader of {{2}} and taken the rule of {{3}} after {how} of famine.", challenger, ruler, faction.name, s);
			}
			else
			{
				Phase2.Curfew.Announce($"{{0}} has taken the rule of {{1}} from {{2}} after {how} of famine.", challenger, s, ruler);
			}
		}

		private static void End(NPCSettlement s, State st)
		{
			st.Active = false;
			st.ActiveHours = 0;
			st.Challenged = false;
			Phase2.Curfew.Announce("The famine in {0} is over.", s);
		}

		// Once a day of famine: starving villagers may leave for a village of their faction
		// that has food (not in famine) and a free home.
		private static void Emigrate(NPCSettlement s)
		{
			int chance = RuinarchPlusConfig.Current.famineLeaveChance;
			foreach (Character c in Villagers(s))
			{
				if (!IsStarving(c) || c == s.ruler || c.isFactionLeader || c.faction == null || !c.limiterComponent.canMove
					|| UnityEngine.Random.Range(0, 100) >= chance)
				{
					continue;
				}
				LocationStructure home = c.faction.ownedSettlements.OfType<NPCSettlement>()
					.Where(v => v != s && v.locationType == LOCATION_TYPE.VILLAGE && !IsInFamine(v))
					.Select(v => v.GetFirstUnoccupiedStructureOfType(STRUCTURE_TYPE.DWELLING))
					.FirstOrDefault(d => d != null && !d.hasBeenDestroyed);
				if (home == null)
				{
					continue;
				}
				c.MigrateHomeStructureTo(home);
				Phase2.Curfew.Announce("{0} has left {1} for {2} to escape the famine.", c, s, home.settlementLocation);
			}
		}

		// ---- persistence -------------------------------------------------------------------
		// One "settlementId|hours of famine|challenged" per village in famine (older saves: the
		// id alone).

		private static string Save()
		{
			FamineSaveData file = new FamineSaveData();
			foreach (KeyValuePair<NPCSettlement, State> kv in States)
			{
				if (kv.Key != null && kv.Value.Active)
				{
					file.famines.Add($"{kv.Key.persistentID}|{kv.Value.ActiveHours}|{(kv.Value.Challenged ? 1 : 0)}");
				}
			}
			return file.famines.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			States.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			foreach (string entry in JsonUtility.FromJson<FamineSaveData>(json)?.famines ?? new List<string>())
			{
				string[] parts = entry.Split('|');
				if (LandmarkManager.Instance.GetSettlementByPersistentID(parts[0]) is NPCSettlement s)
				{
					State st = new State { Active = true };
					if (parts.Length >= 3 && int.TryParse(parts[1], out int hours))
					{
						st.ActiveHours = hours;
						st.Challenged = parts[2] == "1";
					}
					States[s] = st;
				}
			}
			RuinarchPlus.Log?.Info($"Famine loaded: {States.Count} village(s) in famine.");
		}
	}

	[Serializable]
	public class FamineSaveData
	{
		public List<string> famines = new List<string>();
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class Famine_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % 20 == 0)
				{
					Famine.HourlyCheck();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Famine hourly failed: " + e.Message);
			}
		}
	}
}
