using HarmonyLib;
using Traits;

namespace RuinarchPlus
{
	// BUG: every trait tooltip shipped with developer debug text appended.
	// TraitItem.OnHover builds: localizedName + "\n" + trait.GetTestingData()
	// where GetTestingData() returns internal data ("Responsible Characters: ...",
	// "Is gained from stealth: ..."). Reproduce by hovering any trait (e.g. a Well's
	// "Wet"). Fix: show only the localized name; drop the debug append.
	[HarmonyPatch(typeof(TraitItem), "OnHover")]
	internal static class Fix_TraitTooltipDebugLeak
	{
		private static bool Prefix(TraitItem __instance)
		{
			Trait trait = Traverse.Create(__instance).Field("trait").GetValue<Trait>();
			if (trait != null)
			{
				string name = trait.localizedName;
				if (!string.IsNullOrEmpty(name))
				{
					UIManager.Instance.ShowSmallInfo(name);
				}
			}
			return false; // skip original OnHover (which appends GetTestingData)
		}
	}
}
