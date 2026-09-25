using System;
using HarmonyLib;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus
{
	// BUG (performance): a big fire drops the frame rate to single digits. Every hit on a
	// building's wall (ThinWall.AdjustHP -> WALL_DAMAGED / WALL_DAMAGED_BY) makes the building
	// rescan its whole pathfinding grid (ManMadeStructure.OnWallDamaged(By) ->
	// LocationStructureObject.RescanPathfindingGridOfStructure), and a burning wall is hit
	// every tick. Measured with a village on fire at 4x: ~1500 rescans in 5 s, the pathfinder
	// (AstarPath.Update) taking half of every frame. A damaged wall blocks the way exactly as
	// before: the wall (and its colliders) only switches off when its HP reaches 0
	// (ThinWallGameObject.UpdateWallState). Fix: skip the rescan while the damaged wall still
	// stands; the rest of the game's handler (the repair job) runs as before.
	[HarmonyPatch(typeof(LocationStructureObject), nameof(LocationStructureObject.RescanPathfindingGridOfStructure))]
	internal static class Fix_FireWallPathfinding
	{
		// Set while a wall-damage handler runs for a wall that is still standing.
		internal static bool WallStillStands;

		private static bool Prefix() => !WallStillStands;
	}

	[HarmonyPatch(typeof(ManMadeStructure), "OnWallDamaged")]
	internal static class Fix_FireWallPathfinding_Damaged
	{
		private static void Prefix(ThinWall structureWall) => Fix_FireWallPathfinding.WallStillStands = structureWall != null && structureWall.currentHP > 0;

		private static Exception Finalizer(Exception __exception)
		{
			Fix_FireWallPathfinding.WallStillStands = false;
			return __exception;
		}
	}

	[HarmonyPatch(typeof(ManMadeStructure), "OnWallDamagedBy")]
	internal static class Fix_FireWallPathfinding_DamagedBy
	{
		private static void Prefix(ThinWall structureWall) => Fix_FireWallPathfinding.WallStillStands = structureWall != null && structureWall.currentHP > 0;

		private static Exception Finalizer(Exception __exception)
		{
			Fix_FireWallPathfinding.WallStillStands = false;
			return __exception;
		}
	}
}
