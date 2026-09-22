using HarmonyLib;
using UnityEngine;

namespace RuinarchPlus
{
	// BUG (player report): you cannot place the demonic Portal on any tile whose
	// screen position falls behind the centered "Pick a tile to Place your portal."
	// banner - the ghost won't turn green / the click does nothing there.
	//
	// Cause (verified in decompiled source):
	//   PickPortalInputModule.OnUpdate + OnReceivePlayerInputAction both gate on
	//   !UIManager.IsMouseOnUI(). UIManager.IsMouseOnUI() does EventSystem.RaycastAll
	//   and returns true if any hit graphic is on the "UI" layer. The banner
	//   (InitialWorldSetupMenu.pickPortalMessage) is a UI-layer RectTransform anchored
	//   dead-center, so it eats the raycast and blocks placement on the tiles under it.
	//
	// Fix: the banner is a passive label - it must never block the placement raycast.
	// A CanvasGroup with blocksRaycasts=false removes it (and children) from RaycastAll,
	// so IsMouseOnUI() no longer trips under the banner. Alpha/position untouched, so it
	// still shows and animates exactly as before.
	[HarmonyPatch(typeof(InitialWorldSetupMenu), "OnClickPlacePortal")]
	public static class Fix_PortalPlacementBehindBanner
	{
		private static void Postfix(InitialWorldSetupMenu __instance)
		{
			RectTransform rt = __instance.pickPortalMessage;
			if (rt == null)
			{
				return;
			}
			GameObject go = rt.gameObject;
			CanvasGroup cg = go.GetComponent<CanvasGroup>();
			if (cg == null)
			{
				cg = go.AddComponent<CanvasGroup>();
			}
			cg.blocksRaycasts = false;
			cg.interactable = false;
		}
	}
}
