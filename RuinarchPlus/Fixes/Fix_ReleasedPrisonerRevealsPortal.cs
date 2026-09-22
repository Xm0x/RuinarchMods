using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using UtilityScripts;

namespace RuinarchPlus
{
	// BUG (player report): releasing a prisoner drops them on a random Wilderness tile
	// ADJACENT TO THE PRISON. The prison sits inside your demonic base, right next to
	// the Portal, so the freed villager immediately learns where your Portal is and
	// walks off to report it. See MovementComponent.LetGo: it walks currentStructure
	// (the prison) neighbour tiles and teleports to one of them.
	//
	// FIX: full-replace LetGo. If the character has a home structure, teleport them
	// straight into it and knock them Unconscious ("dropped off dazed near home"),
	// so they never path away from your base and never reveal the Portal. Characters
	// with no home fall through to vanilla behaviour (return true).
	[HarmonyPatch(typeof(MovementComponent), "LetGo")]
	public static class Fix_ReleasedPrisonerRevealsPortal
	{
		private static bool Prefix(MovementComponent __instance)
		{
			Character owner = __instance.owner;
			if (owner == null)
			{
				return true; // let vanilla run
			}
			LocationGridTile dest = PickHomeTile(owner);
			if (dest == null)
			{
				return true; // homeless captive: keep vanilla drop-near-base behaviour
			}

			// Knocked out, then relocated home. Unconscious is a NEGATIVE Status that
			// wears off, so they simply come to at home instead of scouting your base.
			owner.traitContainer.AddTrait(owner, "Unconscious");
			CharacterManager.Instance.Teleport(owner, dest);
			GameManager.Instance.CreateParticleEffectAt(dest, PARTICLE_EFFECT.Minion_Dissipate);
			owner.traitContainer.RemoveRestrainAndImprison(owner);
			if (owner.isLycanthrope)
			{
				owner.lycanData.limboForm.traitContainer.RemoveRestrainAndImprison(owner.lycanData.limboForm);
			}
			return false; // skip the vanilla drop-beside-the-prison logic
		}

		private static LocationGridTile PickHomeTile(Character owner)
		{
			LocationStructure home = owner.homeStructure;
			if (home == null)
			{
				return null;
			}
			if (home.passableTiles != null && home.passableTiles.Count > 0)
			{
				return CollectionUtilities.GetRandomElement(home.passableTiles);
			}
			if (home.tiles != null && home.tiles.Count > 0)
			{
				return CollectionUtilities.GetRandomElement(home.tiles.ToList());
			}
			return null;
		}
	}
}
