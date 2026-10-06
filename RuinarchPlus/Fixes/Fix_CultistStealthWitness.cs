using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(CharacterMarker), "CanDoStealthCrimeToTarget", new[] { typeof(Character), typeof(CRIME_TYPE) })]
	internal static class Fix_CultistStealthWitness
	{
		private static bool Prefix(CharacterMarker __instance, Character target, CRIME_TYPE crimeType, ref bool __result)
		{
			if (target.isDead) return true;
			var marker = target.marker;
			__result = false;
			if (!marker) return false;
			var actor = __instance.character;
			for (int i = 0; i < marker.inVisionCharacters.Count; i++)
			{
				var witness = marker.inVisionCharacters[i];
				if (witness != target && witness != actor && !(witness is Animal) && witness.petComponent.petOwner == null
					&& !target.IsHostileWith(witness) && witness.limiterComponent.canWitness
					&& (marker.visionColliderComponent.IsTheSameStructureOrSameOpenSpaceWithPOI(witness) || marker.IsCharacterInLineOfSightWith(witness))
					&& CrimeManager.Instance.GetCrimeSeverity(witness, actor, target, crimeType).IsConsideredACrime())
				{
					return false;
				}
			}
			__result = true;
			return false;
		}
	}
}
