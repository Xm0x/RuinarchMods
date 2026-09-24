using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using TMPro;

namespace RuinarchPlus
{
	// BUG: the Snatch window offers only BOOKMARKED structures as drop-off points
	// (SnatchObjectUIController.ConstructDropLocationChoices reads
	// storedTargetsComponent.storedStructures). With nothing bookmarked the list is empty, no
	// drop-off is ever chosen, and the Snatch button stays disabled
	// (UpdateSnatchObjectButtonInteractableState: _snatchDropLocation == null). Fix: when a
	// character is being snatched and no bookmark is valid, offer the player's own demonic
	// structures (a character may be dropped in any structure, IsStructureValidForTarget).
	// Snatched objects must go to a non-demonic structure, so for them the list stays as is.
	[HarmonyPatch(typeof(SnatchObjectUIController), "ConstructDropLocationChoices")]
	internal static class Fix_SnatchDropLocations
	{
		private static readonly AccessTools.FieldRef<SnatchObjectUIController, List<IStoredTarget>> Choices =
			AccessTools.FieldRefAccess<SnatchObjectUIController, List<IStoredTarget>>("_allValidDropLocations");
		private static readonly AccessTools.FieldRef<SnatchObjectUIController, IStoredTarget> Target =
			AccessTools.FieldRefAccess<SnatchObjectUIController, IStoredTarget>("_chosenTarget");
		private static readonly AccessTools.FieldRef<SnatchObjectUIController, SnatchObjectUIView> View =
			AccessTools.FieldRefAccess<SnatchObjectUIController, SnatchObjectUIView>("m_snatchObjectUIView");

		private static void Postfix(SnatchObjectUIController __instance)
		{
			try
			{
				List<IStoredTarget> choices = Choices(__instance);
				IStoredTarget target = Target(__instance);
				if (choices == null || choices.Count > 0 || !(target == null || target is Character))
				{
					return;
				}
				List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
				foreach (LocationStructure s in PlayerManager.Instance.player.playerSettlement.allStructures)
				{
					if (s is DemonicStructure && !s.hasBeenDestroyed)
					{
						options.Add(new TMP_Dropdown.OptionData(s.bookmarkName, s.GetPortraitSprite()));
						choices.Add(s);
					}
				}
				if (options.Count > 0)
				{
					View(__instance).SetTargetLocationDropdownOptions(options);
				}
			}
			catch (System.Exception e)
			{
				RuinarchPlus.Log?.Warning("Snatch drop-off fallback failed: " + e.Message);
			}
		}
	}
}
