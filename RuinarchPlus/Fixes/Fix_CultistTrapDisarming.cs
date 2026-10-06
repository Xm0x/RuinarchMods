using HarmonyLib;
using Traits;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(BoobyTrapped), nameof(BoobyTrapped.VillagerReactionToTileObjectTrait))]
	internal static class Fix_CultistTrapDisarming
	{
		private static bool Prefix(BoobyTrapped __instance, TileObject owner, Character actor)
		{
			return __instance.responsibleCharacter == null
				|| !CharacterManager.Instance.IsCultistOfSameReligion(actor, __instance.responsibleCharacter)
				|| owner.IsOwnedBy(actor);
		}
	}
}
