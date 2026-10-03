using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Locations.Settlements;

namespace RuinarchPlus.Phase7
{
	/// <summary>
	/// How villages answer the blight (config: <c>blightEnabled</c>). A village's pressure is
	/// the corrupted tiles on its ground and within five tiles of its border. Any pressure:
	/// it keeps a cleanup job open. Alarm (30+ tiles): up to five cleanup jobs at once,
	/// its kingdom is told of the player, and the blight weighs on its mood.
	/// Divine Church villagers purify twice as fast. Cleaners take
	/// blight on their own village's ground first. Recomputed hourly; not saved (it is
	/// derived from the map).
	/// </summary>
	internal static class BlightResponse
	{
		internal const int AlarmAt = 30;
		private const int MaxCleaners = 5;

		private static readonly Dictionary<NPCSettlement, int> PressureOf = new Dictionary<NPCSettlement, int>();
		private static readonly Dictionary<NPCSettlement, int> OwnGround = new Dictionary<NPCSettlement, int>();
		private static readonly HashSet<NPCSettlement> Alarmed = new HashSet<NPCSettlement>();
		private static readonly HashSet<Area> NearbyAreas = new HashSet<Area>();
		private static readonly List<LocationGridTile> BorderTiles = new List<LocationGridTile>();
		private static Region _region;

		internal static int Pressure(NPCSettlement v) => v != null && PressureOf.TryGetValue(v, out int p) ? p : 0;

		internal static bool InAlarm(NPCSettlement v) => Pressure(v) >= AlarmAt;

		/// <summary>Unrest weight: 1 in alarm, 0.5 with blight on its own ground.</summary>
		internal static float Grievance(NPCSettlement v)
		{
			if (!BlightRules.Enabled || v == null)
			{
				return 0f;
			}
			if (InAlarm(v))
			{
				return 1f;
			}
			return OwnGround.TryGetValue(v, out int own) && own > 0 ? 0.5f : 0f;
		}

		internal static void Hourly()
		{
			PressureOf.Clear();
			OwnGround.Clear();
			Region region = GridMap.Instance?.mainRegion;
			if (region != _region)
			{
				Alarmed.Clear();
				_region = region;
			}
			if (!BlightRules.Enabled)
			{
				return;
			}
			List<LocationGridTile> corrupted = PlayerManager.Instance?.player?.playerSettlement?.corruptedTiles;
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (corrupted == null || settlements == null)
			{
				return;
			}
			foreach (NPCSettlement v in settlements.OfType<NPCSettlement>())
			{
				if (v.owner == null || v.residents.Count == 0)
				{
					continue;
				}
				NearbyAreas.Clear();
				BorderTiles.Clear();
				foreach (Area a in v.areas)
				{
					NearbyAreas.Add(a);
					NearbyAreas.UnionWith(a.neighbourComponent.neighbours);
					foreach (LocationGridTile t in a.gridTileComponent.gridTiles)
					{
						if (!t.IsPartOfSettlement(v)) continue;
						foreach (LocationGridTile n in t.neighbourList)
						{
							if (n.IsPartOfSettlement(v)) continue;
							BorderTiles.Add(t);
							break;
						}
					}
				}
				int own = 0, pressure = 0;
				foreach (LocationGridTile t in corrupted)
				{
					if (t == null || !t.corruptionComponent.isCorrupted || !NearbyAreas.Contains(t.area)) continue;
					if (t.IsPartOfSettlement(v))
					{
						own++;
						pressure++;
						continue;
					}
					foreach (LocationGridTile border in BorderTiles)
					{
						if (t.GetDistanceTo(border) > 5) continue;
						pressure++;
						break;
					}
				}
				PressureOf[v] = pressure;
				OwnGround[v] = own;
				Answer(v, pressure);
			}
		}

		private static void Answer(NPCSettlement v, int pressure)
		{
			bool alarm = pressure >= AlarmAt;
			if (alarm && Alarmed.Add(v))
			{
				v.owner?.SetIsAwareOfPlayer(true);
				RuinarchPlus.Log?.Info($"{v.name} raised the alarm against the blight ({pressure} tiles); its people send for help and clean it.");
			}
			else if (!alarm && pressure == 0)
			{
				Alarmed.Remove(v);
			}
			if (pressure == 0)
			{
				return;
			}
			int want = 1;
			if (alarm)
			{
				want = Math.Min(MaxCleaners, 1 + pressure / 15);
			}
			int open = v.availableJobs.Count(j => j.jobType == JOB_TYPE.PURIFY_GROUND);
			for (int i = open; i < want; i++)
			{
				GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.PURIFY_GROUND, INTERACTION_TYPE.START_PURIFYING_GROUND, null, v);
				job.SetCanTakeThisJobChecker("CanTakePurifyJob");
				v.AddToAvailableJobs(job);
			}
		}

		/// <summary>The nearest blighted tile on <paramref name="v"/>'s own ground the cleaner can reach.</summary>
		internal static LocationGridTile NearestOwn(Character cleaner, NPCSettlement v)
		{
			List<LocationGridTile> corrupted = PlayerManager.Instance?.player?.playerSettlement?.corruptedTiles;
			if (cleaner?.gridTileLocation == null || corrupted == null || !OwnGround.TryGetValue(v, out int own) || own == 0)
			{
				return null;
			}
			LocationGridTile from = cleaner.gridTileLocation;
			return corrupted.Where(t => t != null && t.corruptionComponent.isCorrupted && t.IsPartOfSettlement(v) && t.IsPassable()
					&& !t.HasNeighbourStructure(STRUCTURE_TYPE.THE_PORTAL) && cleaner.movementComponent.HasPathToEvenIfDiffRegion(t))
				.OrderBy(t => t.GetDistanceTo(from)).FirstOrDefault();
		}
	}

	// Cleaners take the blight on their own village's ground before anything else.
	[HarmonyPatch(typeof(PurifyGroundBehaviour), "GetNearestCorruptedTile")]
	internal static class Blight_CleanHomeFirst
	{
		private static bool Prefix(Character p_character, ref LocationGridTile __result)
		{
			try
			{
				if (!BlightRules.Enabled)
				{
					return true;
				}
				string id = p_character?.behaviourComponent?.settlementTargetForPurifyGround;
				NPCSettlement v = string.IsNullOrEmpty(id) ? null : DatabaseManager.Instance.settlementDatabase.GetSettlementByPersistentIDSafe(id) as NPCSettlement;
				LocationGridTile own = v == null ? null : BlightResponse.NearestOwn(p_character, v);
				if (own == null)
				{
					return true;
				}
				__result = own;
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Blight cleaner target failed: " + e.Message);
				return true;
			}
		}
	}

	[HarmonyPatch(typeof(ActualGoapNode), "SetActionDuration")]
	internal static class Blight_ChurchPurification
	{
		private static readonly AccessTools.FieldRef<ActualGoapNode, int> Duration =
			AccessTools.FieldRefAccess<ActualGoapNode, int>("<expectedActionStateDuration>k__BackingField");

		private static void Postfix(ActualGoapNode __instance)
		{
			if (BlightRules.Enabled && __instance.action is PurifyGround
				&& __instance.actor?.faction?.factionType?.type == FACTION_TYPE.Divine_Church
				&& __instance.expectedActionStateDuration > 0)
			{
				Duration(__instance) = Math.Max(1, __instance.expectedActionStateDuration / 2);
			}
		}
	}
}
