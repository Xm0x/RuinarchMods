using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Locations.Settlements.Settlement_Types;
using Ruinarch.ModContent;
using TMPro;
using UnityEngine;

namespace RuinarchPlus.Phase5
{
	/// <summary>
	/// Settlements grow: Village -> Town -> City (config: <c>settlementTiersEnabled</c>).
	///
	/// A village of <c>townPopulation</c> living villagers builds a Town Hall, through the
	/// game's own construction pipeline (<see cref="ModBuildings"/>, borrowing the Tavern's
	/// prefab). While its Town Hall stands the village is a Town, and a City once it reaches
	/// <c>cityPopulation</c>. A settlement keeps its tier until it falls to three quarters of
	/// that tier's population, and loses Town and City the moment its Town Hall is destroyed
	/// (it rebuilds one when it is big enough again).
	///
	/// A tier raises the two limits the game's own build planner reads
	/// (<c>SettlementType.maxDwellings</c> / <c>maxFacilities</c>, hard-coded per culture and
	/// re-set on load): Town +8 dwellings and +4 facilities, City +16 and +8. The planner then
	/// builds the settlement out as it grows. No new <c>SETTLEMENT_TYPE</c>: that one is saved
	/// and drives culture-specific facility weights. The tier is saved inside the player's
	/// save (<c>ModData/ruinarch.plus.tiers.json</c>) and shown in the settlement's info panel.
	/// </summary>
	public static class SettlementTiers
	{
		public const string TownHallId = "ruinarch.plus.town_hall";
		private const string SaveId = "ruinarch.plus.tiers";

		internal enum Tier { Village, Town, City }

		private sealed class Caps
		{
			internal int Dwellings;
			internal int Facilities;
		}

		internal static ModBuilding TownHallBuilding;

		private static readonly Dictionary<NPCSettlement, Tier> Tiers = new Dictionary<NPCSettlement, Tier>();
		// Each SettlementType's own (culture) limits, captured the first time we see it.
		private static readonly ConditionalWeakTable<SettlementType, Caps> BaseCaps = new ConditionalWeakTable<SettlementType, Caps>();
		private static readonly MethodInfo SetMaxDwellings = AccessTools.PropertySetter(typeof(SettlementType), nameof(SettlementType.maxDwellings));
		private static readonly MethodInfo SetMaxFacilities = AccessTools.PropertySetter(typeof(SettlementType), nameof(SettlementType.maxFacilities));

		internal static bool Enabled => RuinarchPlusConfig.Current.settlementTiersEnabled;

		public static void Register()
		{
			try
			{
				ModContent.RegisterStructure(new StructureRegistration
				{
					Id = TownHallId,
					DisplayName = "Town Hall",
					Factory = (type, region) => new TownHall(type, region),
					LoadFactory = (type, region, save) => new TownHall(region, (SaveDataManMadeStructure)save),
					// Borrow the Tavern's prefab: a large hall. No art of its own.
					PrefabSource = STRUCTURE_TYPE.TAVERN,
					Skill = null,
					UnlockWith = PLAYER_SKILL_TYPE.NONE,
					IsPlayerStructure = false,
					IsVillageStructure = true
				});
				TownHallBuilding = ModBuildings.Add(TownHallId, "Town Hall", STRUCTURE_TYPE.TAVERN);
				ModSave.Register(SaveId, Save, Load);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Town Hall registration failed: " + e);
			}
		}

		// ---- queries -----------------------------------------------------------------------

		internal static Tier Get(NPCSettlement settlement)
		{
			return settlement != null && Tiers.TryGetValue(settlement, out Tier t) ? t : Tier.Village;
		}

		private static int Population(NPCSettlement settlement) => settlement.GetNumberOfResidentsThatIsAliveVillager();

		// A settlement keeps a tier down to three quarters of the population that earned it.
		private static int Keep(int population) => population * 3 / 4;

		private static Tier Deserved(NPCSettlement settlement, Tier current)
		{
			if (TownHall.FindFor(settlement) == null)
			{
				return Tier.Village;
			}
			int pop = Population(settlement);
			int town = RuinarchPlusConfig.Current.townPopulation;
			int city = RuinarchPlusConfig.Current.cityPopulation;
			if (pop >= city || (current == Tier.City && pop >= Keep(city)))
			{
				return Tier.City;
			}
			if (pop >= town || (current >= Tier.Town && pop >= Keep(town)))
			{
				return Tier.Town;
			}
			return Tier.Village;
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
					if (settlements[i] is NPCSettlement s && s.locationType == LOCATION_TYPE.VILLAGE && s.settlementType != null)
					{
						Evaluate(s);
						if (NeedsTownHall(s))
						{
							string prefab = ModBuildings.QueueBlueprint(s, TownHallBuilding, STRUCTURE_TYPE.TAVERN);
							if (prefab != null)
							{
								RuinarchPlus.Log?.Info($"{s.name} has grown to {Population(s)} people: queued a Town Hall blueprint ({prefab}).");
							}
						}
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Settlement tier check failed for {settlements[i]?.name}: {e.Message}");
				}
			}
		}

		internal static bool NeedsTownHall(NPCSettlement s)
		{
			return s.owner != null && s.owner.isMajorFaction && s.cityCenter != null
				&& Population(s) >= RuinarchPlusConfig.Current.townPopulation
				&& TownHall.FindFor(s) == null && !ModBuildings.HasPendingFor(s, TownHallBuilding)
				// The game allows one blueprint job per settlement at a time; wait our turn.
				&& !s.HasJob(JOB_TYPE.PLACE_BLUEPRINT);
		}

		internal static bool HasPendingTownHall(NPCSettlement s) => ModBuildings.HasPendingFor(s, TownHallBuilding);

		private static void Evaluate(NPCSettlement s)
		{
			Tier old = Get(s);
			Tier next = Deserved(s, old);
			Tiers[s] = next;
			ApplyCaps(s, next);
			if (next == old)
			{
				return;
			}
			RuinarchPlus.Log?.Info($"{s.name} is now a {next} ({Population(s)} people; was a {old}).");
			if (next > old)
			{
				Phase2.Curfew.Announce(next == Tier.Town ? "{0} has grown into a Town." : "{0} has grown into a City.", s);
			}
			else if (TownHall.FindFor(s) != null)
			{
				Phase2.Curfew.Announce(next == Tier.Town ? "{0} has dwindled back to a Town." : "{0} has dwindled back to a village.", s);
			}
		}

		/// <summary>Called when a Town Hall is destroyed.</summary>
		internal static void OnTownHallLost(NPCSettlement s)
		{
			try
			{
				if (s == null || !Enabled)
				{
					return;
				}
				Tier old = Get(s);
				Evaluate(s);
				if (old != Tier.Village && Get(s) == Tier.Village)
				{
					Phase2.Curfew.Announce("The Town Hall of {0} has been destroyed; {0} is a village again.", s);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Town Hall loss failed: " + e.Message);
			}
		}

		private static void ApplyCaps(NPCSettlement s, Tier tier)
		{
			SettlementType type = s.settlementType;
			Caps caps = BaseCaps.GetValue(type, t => new Caps { Dwellings = t.maxDwellings, Facilities = t.maxFacilities });
			int dwellings = caps.Dwellings + (tier == Tier.City ? 16 : tier == Tier.Town ? 8 : 0);
			int facilities = caps.Facilities + (tier == Tier.City ? 8 : tier == Tier.Town ? 4 : 0);
			if (type.maxDwellings != dwellings)
			{
				SetMaxDwellings.Invoke(type, new object[] { dwellings });
			}
			if (type.maxFacilities != facilities)
			{
				SetMaxFacilities.Invoke(type, new object[] { facilities });
			}
		}

		/// <summary>The faction line shown under a settlement's name, with its tier:
		/// "Human Empire" stays as is for a village, becomes "Human Empire Town".</summary>
		internal static string Label(BaseSettlement settlement, string factionLine)
		{
			Tier tier = Enabled && settlement is NPCSettlement s ? Get(s) : Tier.Village;
			if (tier == Tier.Village)
			{
				return factionLine;
			}
			return string.IsNullOrEmpty(factionLine) ? tier.ToString() : factionLine + " " + tier;
		}

		/// <summary>Build a Town Hall instantly (debug menu, test harness). A settlement has at
		/// most one; if it has one already, that one is returned. Null if there is no valid spot.</summary>
		public static TownHall InstantBuildTownHall(NPCSettlement settlement)
		{
			TownHall existing = TownHall.FindFor(settlement);
			if (existing != null || ModBuildings.HasPendingFor(settlement, TownHallBuilding))
			{
				return existing;
			}
			TownHall built = ModBuildings.InstantBuild(settlement, TownHallBuilding, STRUCTURE_TYPE.TAVERN) as TownHall;
			if (built != null)
			{
				Evaluate(settlement);
			}
			return built;
		}

		// ---- persistence -------------------------------------------------------------------
		// One "settlementId|Tier" string per Town or City.

		private static string Save()
		{
			TierSaveData file = new TierSaveData();
			foreach (KeyValuePair<NPCSettlement, Tier> kv in Tiers)
			{
				if (kv.Key != null && kv.Value != Tier.Village)
				{
					file.tiers.Add(kv.Key.persistentID + "|" + kv.Value);
				}
			}
			return file.tiers.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Tiers.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			TierSaveData file = JsonUtility.FromJson<TierSaveData>(json);
			foreach (string line in file?.tiers ?? new List<string>())
			{
				int bar = line.IndexOf('|');
				if (bar > 0 && LandmarkManager.Instance.GetSettlementByPersistentID(line.Substring(0, bar)) is NPCSettlement s
					&& Enum.TryParse(line.Substring(bar + 1), out Tier tier))
				{
					Tiers[s] = tier;
					ApplyCaps(s, tier);
				}
			}
			RuinarchPlus.Log?.Info($"Settlement tiers loaded: {Tiers.Count} town(s) and cit(ies).");
		}
	}

	[Serializable]
	public class TierSaveData
	{
		public List<string> tiers = new List<string>();
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class SettlementTiers_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % 20 == 0)
				{
					SettlementTiers.HourlyCheck();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Settlement tiers hourly failed: " + e.Message);
			}
		}
	}

	// The settlement panel's faction line names the tier.
	[HarmonyPatch(typeof(SettlementInfoUI), "UpdateBasicInfo")]
	internal static class SettlementTiers_InfoLabel
	{
		private static void Postfix(SettlementInfoUI __instance, TextMeshProUGUI ___typeLbl)
		{
			if (___typeLbl != null && __instance.activeSettlement is NPCSettlement)
			{
				___typeLbl.text = SettlementTiers.Label(__instance.activeSettlement, ___typeLbl.text);
			}
		}
	}

	// So does a settlement's nameplate in lists (faction panel and the like).
	[HarmonyPatch(typeof(SettlementNameplateItem), "UpdateVisuals")]
	internal static class SettlementTiers_NameplateLabel
	{
		private static readonly FieldInfo SubLbl = AccessTools.Field(typeof(NameplateItem<BaseSettlement>), "subLbl");

		private static void Postfix(SettlementNameplateItem __instance)
		{
			if (SubLbl?.GetValue(__instance) is TMP_Text label && __instance.settlement is NPCSettlement { locationType: LOCATION_TYPE.VILLAGE })
			{
				label.text = SettlementTiers.Label(__instance.settlement, label.text);
			}
		}
	}
}
