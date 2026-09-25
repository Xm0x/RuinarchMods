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
	/// - it feeds the village's unrest (<see cref="Unrest"/>).
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

		// The villagers the village feeds: those in it. Someone held in one of the player's
		// buildings, lost in the wild or away on a quest may starve, but not for want of the
		// village's food; counted, they would put a well-fed village into famine (and it
		// would hunt, lose settlers and overthrow its ruler over its captives).
		internal static List<Character> Villagers(NPCSettlement s)
		{
			return Residents(s).Where(r => r.gridTileLocation != null && r.gridTileLocation.IsPartOfSettlement(s)).ToList();
		}

		private static IEnumerable<Character> Residents(NPCSettlement s)
		{
			return s.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient());
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
		}

		private static void End(NPCSettlement s, State st)
		{
			st.Active = false;
			st.ActiveHours = 0;
			Phase2.Curfew.Announce("The famine in {0} is over.", s);
		}

		// Once a day of famine: starving villagers may leave for a village of their faction
		// that has food (not in famine) and a free home.
		private static void Emigrate(NPCSettlement s)
		{
			int chance = RuinarchPlusConfig.Current.famineLeaveChance;
			// Anyone of the village who is free to go, including those out looking for food; not
			// someone held (restrained, carried off).
			foreach (Character c in Residents(s).ToList())
			{
				if (!IsStarving(c) || c == s.ruler || c.isFactionLeader || c.faction == null || !c.limiterComponent.canMove
					|| c.traitContainer.HasTrait("Restrained") || c.carryComponent.isBeingCarriedBy != null
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
		// One "settlementId|hours of famine" per village in famine (older saves: the id alone, or
		// a third field that is no longer read).

		private static string Save()
		{
			FamineSaveData file = new FamineSaveData();
			foreach (KeyValuePair<NPCSettlement, State> kv in States)
			{
				if (kv.Key != null && kv.Value.Active)
				{
					file.famines.Add($"{kv.Key.persistentID}|{kv.Value.ActiveHours}");
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
					if (parts.Length >= 2 && int.TryParse(parts[1], out int hours))
					{
						st.ActiveHours = hours;
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
