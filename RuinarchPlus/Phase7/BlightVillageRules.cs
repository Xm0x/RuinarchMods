using System;
using System.Reflection;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus.Phase7
{
	// Blight on a village's ground is the villagers' affliction, not a demonic foothold: the
	// player cannot summon, raise minions, build or extend corruption from it. (Corrupted
	// ground outside villages keeps its base-game meaning.)

	internal static class BlightVillageRules
	{
		internal const string NoSummon = "Blight in a village cannot be used to summon.";
		internal const string NoBuild = "Blight in a village cannot be built on.";

		internal static bool Applies(LocationGridTile t) => BlightRules.Enabled && BlightRules.IsVillageBlight(t);
	}

	[HarmonyPatch(typeof(MinionPlayerSkill), nameof(MinionPlayerSkill.CanPerformAbilityTowards), new Type[] { typeof(LocationGridTile), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out })]
	internal static class Blight_NoMinionInVillage
	{
		private static void Postfix(LocationGridTile targetTile, ref string o_cannotPerformReason, ref bool __result)
		{
			if (__result && BlightVillageRules.Applies(targetTile))
			{
				__result = false;
				o_cannotPerformReason = BlightVillageRules.NoSummon;
			}
		}
	}

	[HarmonyPatch(typeof(SummonPlayerSkill), nameof(SummonPlayerSkill.CanPerformAbilityTowards), new Type[] { typeof(LocationGridTile), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out })]
	internal static class Blight_NoSummonInVillage
	{
		private static void Postfix(LocationGridTile targetTile, ref string o_cannotPerformReason, ref bool __result)
		{
			if (__result && BlightVillageRules.Applies(targetTile))
			{
				__result = false;
				o_cannotPerformReason = BlightVillageRules.NoSummon;
			}
		}
	}

	// Corrupt Tile, demonic walls, decorations and demonic buildings all need corruption next
	// to them; village blight does not count as that neighbour.
	[HarmonyPatch(typeof(GridTileCorruptionComponent), nameof(GridTileCorruptionComponent.HasCorruptedNeighbour))]
	internal static class Blight_VillageNotANeighbour
	{
		private static void Postfix(GridTileCorruptionComponent __instance, ref bool __result)
		{
			if (!__result || !BlightRules.Enabled)
			{
				return;
			}
			foreach (LocationGridTile n in __instance.owner.neighbourList)
			{
				if (n.corruptionComponent.isCorrupted && !BlightRules.IsVillageBlight(n))
				{
					return;
				}
			}
			__result = false;
		}
	}

	// Demonic walls and decorations may stand on corrupted ground, but not on village blight.
	[HarmonyPatch(typeof(BaseBuildingManager), "IsTileValidFor")]
	internal static class Blight_NoWallsInVillage
	{
		private static void Postfix(LocationGridTile p_tile, SkillData p_skill, ref string p_reason, ref bool __result)
		{
			if (__result && p_skill != null && (p_skill.type == PLAYER_SKILL_TYPE.DEMONIC_WALL || p_skill.type == PLAYER_SKILL_TYPE.DECORATIONS)
				&& BlightVillageRules.Applies(p_tile))
			{
				__result = false;
				p_reason = BlightVillageRules.NoBuild;
			}
		}
	}

	// A demonic building may not stand on village blight.
	[HarmonyPatch(typeof(DemonicStructurePlayerSkill), "CanBuildDemonicStructureOn")]
	internal static class Blight_NoBuildingInVillage
	{
		private static void Postfix(LocationStructureObject structureObj, LocationGridTile centerTile, ref string o_cannotPlaceReason, ref bool __result)
		{
			if (!__result || !BlightRules.Enabled || structureObj == null || centerTile == null)
			{
				return;
			}
			InnerTileMap map = centerTile.parentMap;
			foreach (UnityEngine.Vector3Int c in structureObj.localOccupiedCoordinates)
			{
				int x = centerTile.localPlace.x + c.x - structureObj.center.x;
				int y = centerTile.localPlace.y + c.y - structureObj.center.y;
				if (x >= 0 && y >= 0 && x < map.width && y < map.height && BlightRules.IsVillageBlight(map.map[x, y]))
				{
					__result = false;
					o_cannotPlaceReason = BlightVillageRules.NoBuild;
					return;
				}
			}
		}
	}

	// Corrupted fields are not tilled again until the blight is cleaned off them.
	[HarmonyPatch(typeof(Farm), "GetUntilledFarmTile")]
	internal static class Blight_NoTillingBlight
	{
		private static readonly MethodInfo IsTilled = AccessTools.Method(typeof(Farm), "CheckIfTileIsTilled");

		private static bool Prefix(Farm __instance, ref GenericTileObject __result)
		{
			if (!BlightRules.Enabled)
			{
				return true;
			}
			__result = null;
			foreach (LocationGridTile t in __instance.farmTiles)
			{
				if (!t.corruptionComponent.isCorrupted && !(bool)IsTilled.Invoke(__instance, new object[] { t })
					&& !t.tileObjectComponent.genericTileObject.HasJobTargetingThis(JOB_TYPE.TILL_TILE))
				{
					__result = t.tileObjectComponent.genericTileObject;
					break;
				}
			}
			return false;
		}
	}

	// A job queued before corruption still reaches the action. Reject that job, and
	// recheck completion because the field may become blighted while tilling.
	[HarmonyPatch(typeof(TillTile), "AreRequirementsSatisfied")]
	internal static class Blight_NoQueuedTilling
	{
		private static void Postfix(IPointOfInterest poiTarget, ref bool __result)
		{
			if (BlightRules.Enabled && poiTarget?.gridTileLocation?.corruptionComponent.isCorrupted == true)
				__result = false;
		}
	}

	[HarmonyPatch(typeof(TillTile), nameof(TillTile.AfterTillTileSuccess))]
	internal static class Blight_NoPlantingBlight
	{
		private static bool Prefix(ActualGoapNode goapNode)
		{
			return !BlightRules.Enabled || goapNode.target.gridTileLocation?.corruptionComponent.isCorrupted != true;
		}
	}
}
