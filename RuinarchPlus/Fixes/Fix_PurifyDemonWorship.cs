using HarmonyLib;

namespace RuinarchPlus
{
	// C09-69: removing Demon Cultist while subtracting only 30 belief leaves the
	// former cultist praying to demons. Purification must also renounce that religion.
	[HarmonyPatch(typeof(Purify), nameof(Purify.AfterPurifySuccess))]
	internal static class Fix_PurifyDemonWorship
	{
		private static void Prefix(ActualGoapNode goapNode, out bool __state)
		{
			__state = goapNode.poiTarget.traitContainer.HasTrait("Demon Cultist");
		}

		private static void Postfix(ActualGoapNode goapNode, bool __state)
		{
			if (__state && goapNode.poiTarget is Character character && character.religionComponent.religion == RELIGION.Demon_Worship)
				character.religionComponent.ChangeReligion(ReligionComponent.GetDefaultReligionForRace(character.race));
		}
	}
}
