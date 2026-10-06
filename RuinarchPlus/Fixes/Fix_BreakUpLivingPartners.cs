using HarmonyLib;
using UtilityScripts;

namespace RuinarchPlus
{
	// C11-76: retained dead relationships and unresolved IDs are not scheme targets.
	internal static class BreakUpPartners
	{
		internal static Character FirstLiving(Character source)
		{
			foreach (var relationship in source.relationshipContainer.relationships)
			{
				if (!relationship.Value.IsLoverOrAffair()) continue;
				Character partner = CharacterManager.Instance.GetCharacterByID(relationship.Key);
				if (partner != null && !partner.isDead) return partner;
			}
			return null;
		}
	}

	[HarmonyPatch(typeof(BreakUpData), nameof(BreakUpData.CanPerformAbilityTowards))]
	internal static class Fix_BreakUpEligibility
	{
		private static void Postfix(Character targetCharacter, ref bool __result)
		{
			if (__result) __result = BreakUpPartners.FirstLiving(targetCharacter) != null;
		}
	}

	[HarmonyPatch(typeof(BreakUpData), nameof(BreakUpData.GetReasonsWhyCannotPerformAbilityTowards))]
	internal static class Fix_BreakUpReason
	{
		private static void Postfix(Character targetCharacter, ref string __result)
		{
			// Native already supplies this reason if there is no spawned partner at all.
			if (targetCharacter.relationshipContainer.HasRelationshipWithSpawnedCharacter(RELATIONSHIP_TYPE.LOVER, RELATIONSHIP_TYPE.AFFAIR)
				&& BreakUpPartners.FirstLiving(targetCharacter) == null)
				__result += LocalizationManager.Instance.GetLocalizedValue("PlayerPowerReasons_Table", "Target_No_Lover_Or_Affair") + "|";
		}
	}

	[HarmonyPatch(typeof(BreakUpData), nameof(BreakUpData.ActivateAbility))]
	internal static class Fix_BreakUpTargets
	{
		private static bool Prefix(BreakUpData __instance, IPointOfInterest targetPOI)
		{
			if (!(targetPOI is Character source) || !__instance.CanPerformAbilityTowards(source)) return false;
			var partners = RuinarchListPool<Character>.Claim();
			try
			{
				foreach (var relationship in source.relationshipContainer.relationships)
				{
					if (!relationship.Value.IsLoverOrAffair()) continue;
					Character partner = CharacterManager.Instance.GetCharacterByID(relationship.Key);
					if (partner != null && !partner.isDead) partners.Add(partner);
				}
				if (partners.Count == 1) UIManager.Instance.ShowSchemeUI(source, partners[0], __instance);
				else if (partners.Count > 1)
					UIManager.Instance.ShowClickableObjectPicker(partners, o =>
					{
						if (!(o is Character partner) || partner.isDead) return;
						UIManager.Instance.HideObjectPicker();
						UIManager.Instance.ShowSchemeUI(source, partner, __instance);
					}, null, (Character partner) => !partner.isDead, "", null, null, "", showCover: true, 25);
			}
			finally { RuinarchListPool<Character>.Release(partners); }
			return false;
		}
	}
}
