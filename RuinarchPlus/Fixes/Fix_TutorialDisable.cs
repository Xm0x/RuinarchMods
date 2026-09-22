using HarmonyLib;
using Tutorial;

namespace RuinarchPlus
{
	// QOL (opt-in via config.json -> "disableTutorial": true): veterans don't want the
	// tutorial alert hand-holding. TutorialManager.Initialize() is the single bootstrap
	// that instantiates pending tutorial alerts and starts the generic/building alert
	// pool loops. When the flag is set we skip it entirely, so no tutorial alerts spawn.
	//
	// Default is false: with the flag off this patch is a no-op and the base game runs
	// exactly as shipped.
	[HarmonyPatch(typeof(TutorialManager), "Initialize")]
	public static class Fix_TutorialDisable
	{
		private static bool Prefix()
		{
			if (RuinarchPlusConfig.Current.disableTutorial)
			{
				RuinarchPlus.Log?.Info("disableTutorial=true: skipping TutorialManager.Initialize()");
				return false; // skip the whole tutorial/alert bootstrap
			}
			return true; // flag off -> run vanilla
		}
	}
}
