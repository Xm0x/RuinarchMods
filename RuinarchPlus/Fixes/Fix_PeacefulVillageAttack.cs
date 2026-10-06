using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(AttackVillageBehaviour), nameof(AttackVillageBehaviour.TryDoBehaviour))]
	internal static class Fix_PeacefulVillageAttack
	{
		private static bool Prefix(Character character, ref bool __result, out JobQueueItem producedJob)
		{
			producedJob = null;
			Faction targetOwner = character.behaviourComponent.attackVillageTarget?.owner
				?? character.behaviourComponent.attackAreaTarget?.GetFirstNPCSettlementOnArea()?.owner;
			if (targetOwner == null || character.faction == null || character.faction.IsHostileWith(targetOwner)) return true;
			character.behaviourComponent.ClearAttackVillageData();
			character.necromancerTrait?.SetAttackVillageTarget(null);
			__result = false;
			return false;
		}
	}
}
