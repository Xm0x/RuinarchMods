using System.Collections.Generic;
using HarmonyLib;
using UtilityScripts;

namespace RuinarchPlus
{
	internal static class Fix_HarpyDragonCapture
	{
		internal static bool Select(Character actor, Region region, ref Character __result)
		{
			// Preserve stock eligibility, pooled storage and one uniform random draw.
			List<Character> candidates = RuinarchListPool<Character>.Claim();
			for (int i = 0; i < region.charactersAtLocation.Count; i++)
			{
				Character candidate = region.charactersAtLocation[i];
				if (!(candidate is Dragon) && candidate != actor && candidate.race != actor.race
					&& !candidate.isHidden && !candidate.isDead && !candidate.isBeingSeized
					&& candidate.carryComponent.IsNotBeingCarried()) candidates.Add(candidate);
			}
			__result = candidates.Count == 0 ? null : candidates[GameUtilities.RandomBetweenTwoNumbers(0, candidates.Count - 1)];
			RuinarchListPool<Character>.Release(candidates);
			return false;
		}
	}

	[HarmonyPatch(typeof(HarpyBehaviour), "GetTargetForCapture")]
	internal static class Fix_HarpyWildDragonCapture
	{
		private static bool Prefix(Character actor, Region region, ref Character __result) =>
			Fix_HarpyDragonCapture.Select(actor, region, ref __result);
	}

	[HarmonyPatch(typeof(Harpy), "GetTargetForCapture")]
	internal static class Fix_HarpyAgitatedDragonCapture
	{
		private static bool Prefix(Character actor, Region region, ref Character __result) =>
			Fix_HarpyDragonCapture.Select(actor, region, ref __result);
	}
}
