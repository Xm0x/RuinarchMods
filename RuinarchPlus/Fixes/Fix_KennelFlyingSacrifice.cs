using HarmonyLib;
using Inner_Maps.Location_Structures;

namespace RuinarchPlus
{
	// EXPLOIT: a flying monster that is not Restrained only hovers over a Kennel or a Torture
	// Chambers cell; it is not held there. Cast on the monster, Sacrifice and Let It Go refuse
	// it (SacrificeData.IsValid / LetGoData.IsValid: "isFlying && !Restrained"). Cast on the
	// Kennel, they don't check: SacrificeData.IsValid(Kennel) only asks for an
	// occupyingSummon and ActivateAbility(Kennel) sacrifices it (2 to 5 Chaos Orbs), and
	// LetGoData.ActivateAbility(Kennel / Torture Chambers) lets go of every character there
	// that passes CanPerformAbilityTowards, which has no flying check. A monster that broke
	// its restraints and flies over its Kennel stays its occupyingSummon, so it could still
	// be sacrificed. Fix (config closeExploits): the structure path applies the same rule.
	internal static class KennelFlying
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.closeExploits;

		/// <summary>Flying and not Restrained: only passing over, not held.</summary>
		internal static bool OnlyHovering(Character c)
		{
			return c != null && c.movementComponent.isFlying && !c.traitContainer.HasTrait("Restrained");
		}
	}

	[HarmonyPatch(typeof(SacrificeData), nameof(SacrificeData.IsValid))]
	internal static class Fix_KennelFlyingSacrifice_IsValid
	{
		private static void Postfix(IPlayerActionTarget target, ref bool __result)
		{
			if (__result && KennelFlying.Enabled && target is Kennel kennel && KennelFlying.OnlyHovering(kennel.occupyingSummon))
			{
				__result = false;
			}
		}
	}

	[HarmonyPatch(typeof(SacrificeData), nameof(SacrificeData.ActivateAbility), new[] { typeof(LocationStructure) })]
	internal static class Fix_KennelFlyingSacrifice_Activate
	{
		private static bool Prefix(LocationStructure targetStructure)
		{
			return !(KennelFlying.Enabled && targetStructure is Kennel kennel && KennelFlying.OnlyHovering(kennel.occupyingSummon));
		}
	}

	// Also covers LetGoData.CanPerformAbilityTowards(Kennel / Torture Chambers), which asks this
	// per character to decide whether the structure has anyone to let go.
	[HarmonyPatch(typeof(LetGoData), nameof(LetGoData.CanPerformAbilityTowards), new[] { typeof(Character) })]
	internal static class Fix_KennelFlyingLetGo
	{
		private static void Postfix(Character targetCharacter, ref bool __result)
		{
			if (__result && KennelFlying.Enabled && KennelFlying.OnlyHovering(targetCharacter)
				&& (targetCharacter.currentStructure is Kennel || targetCharacter.currentStructure is TortureChambers))
			{
				__result = false;
			}
		}
	}
}
