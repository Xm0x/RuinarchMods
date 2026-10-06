using HarmonyLib;
using Traits;

namespace RuinarchPlus
{
	internal static class IntrinsicPoison
	{
		private static readonly AccessTools.FieldRef<Poisoned, bool> IsVenomous =
			AccessTools.FieldRefAccess<Poisoned, bool>("<isVenomous>k__BackingField");

		internal static void Repair(Poisoned poison, ITraitable owner)
		{
			if (poison.isVenomous || !owner.traitContainer.HasTrait("Poisonous")) return;
			IsVenomous(poison) = true;
			if (owner is Character character) character.AdjustDoNotRecoverHP(-1);
		}
	}

	// C08-96: Poisonous creates Poisoned itself, but native only recognizes Venomous
	// as an intrinsic source. Preserve the poison coating without self-damage/heal suppression.
	[HarmonyPatch(typeof(Poisoned), nameof(Poisoned.OnAddTrait))]
	internal static class Fix_PoisonousNewPoison
	{
		private static void Postfix(Poisoned __instance, ITraitable addedTo) => IntrinsicPoison.Repair(__instance, addedTo);
	}

	[HarmonyPatch(typeof(Poisoned), nameof(Poisoned.LoadTraitOnLoadTraitContainer))]
	internal static class Fix_PoisonousLoadedPoison
	{
		private static void Postfix(Poisoned __instance, ITraitable addTo) => IntrinsicPoison.Repair(__instance, addTo);
	}

	[HarmonyPatch(typeof(Poisonous), nameof(Poisonous.OnAddTrait))]
	internal static class Fix_PoisonousExistingPoison
	{
		private static void Postfix(ITraitable addedTo)
		{
			Poisoned poison = addedTo.traitContainer.GetTraitOrStatus<Poisoned>("Poisoned");
			if (poison != null) IntrinsicPoison.Repair(poison, addedTo);
		}
	}
}
