using HarmonyLib;

namespace RuinarchPlus
{
	internal static class RatmanSpawner
	{
		internal static int Count(MonsterSpawner spawner, bool any = false, bool inside = false, bool idle = false)
		{
			int count = 0;
			for (int i = 0; i < spawner.spawnedCharacterIDs.Count; i++)
			{
				Character character = CharacterManager.Instance.GetCharacterByPersistentID(spawner.spawnedCharacterIDs[i]);
				if (character == null || character is Summon || character.isDead || character.race != RACE.RATMAN
					|| character.faction != FactionManager.Instance.ratmenFaction) continue;
				var tile = character.gridTileLocation;
				if (tile != null && tile.structure.structureType == STRUCTURE_TYPE.KENNEL) continue;
				if (inside && (spawner.gridTileLocation == null || tile?.structure != spawner.gridTileLocation.structure)) continue;
				if (idle && character.behaviourComponent.HasBehaviour(typeof(AttackVillageBehaviour))) continue;
				if (any) return 1;
				count++;
			}
			return count;
		}
	}

	[HarmonyPatch(typeof(MonsterSpawner), "GetCountOfValidSpawnedMonsters")]
	internal static class Fix_RatmanSpawnerCount
	{
		private static void Postfix(MonsterSpawner __instance, ref int __result) { __result += RatmanSpawner.Count(__instance); }
	}

	[HarmonyPatch(typeof(MonsterSpawner), nameof(MonsterSpawner.HasValidSpawnedMonster))]
	internal static class Fix_RatmanSpawnerPresent
	{
		private static void Postfix(MonsterSpawner __instance, ref bool __result)
		{
			if (!__result) __result = RatmanSpawner.Count(__instance, any: true) != 0;
		}
	}

	[HarmonyPatch(typeof(MonsterSpawner), nameof(MonsterSpawner.HasValidSpawnedMonsterThatIsNotAttackingVillage))]
	internal static class Fix_RatmanSpawnerIdle
	{
		private static void Postfix(MonsterSpawner __instance, ref bool __result)
		{
			if (!__result) __result = RatmanSpawner.Count(__instance, any: true, idle: true) != 0;
		}
	}

	[HarmonyPatch(typeof(MonsterSpawner), "IsThereAliveSpawnedMonsterInsideStructureOfSpawner")]
	internal static class Fix_RatmanSpawnerInside
	{
		private static void Postfix(MonsterSpawner __instance, ref bool __result)
		{
			if (!__result) __result = RatmanSpawner.Count(__instance, any: true, inside: true) != 0;
		}
	}
}
