using HarmonyLib;
using Settings;
using UnityEngine;

namespace RuinarchPerformance
{
	/// <summary>
	/// The game caps the frame rate at 144 (SettingsManager.targetFrameRate, applied once in
	/// SettingsManager.Awake). With "Match screen refresh rate" on, a faster screen raises the
	/// cap to its refresh rate (never lower than the game's); with it off, the player's cap is
	/// used. Vertical sync, when switched on in the game's options, still takes precedence.
	///
	/// The character tick budget follows (CharacterTickManager takes 0.9 of a frame at the cap),
	/// so per second the game spends the same time on characters whatever the cap.
	/// </summary>
	[HarmonyPatch(typeof(SettingsManager), "Awake")]
	internal static class FrameRateCap
	{
		private static readonly AccessTools.FieldRef<CharacterTickManager, float> MaxTickTime =
			AccessTools.FieldRefAccess<CharacterTickManager, float>("_maxTickTime");

		private static void Postfix(SettingsManager __instance)
		{
			if (SettingsManager.Instance == __instance) Apply();
		}

		internal static void Apply()
		{
			SettingsManager game = SettingsManager.Instance;
			if (game == null) return;
			PerformanceSettings s = RuinarchPerformance.Settings;
			int want = s.matchScreen ? Mathf.Max(game.targetFrameRate, Screen.currentResolution.refreshRate) : s.frameRateCap;
			if (want <= 0 || (s.matchScreen && game.targetFrameRate <= 0) || Application.targetFrameRate == want) return;
			int was = Application.targetFrameRate;
			Application.targetFrameRate = want;
			if (CharacterTickManager.Instance != null) MaxTickTime(CharacterTickManager.Instance) = 0.9f / want;
			RuinarchPerformance.Log?.Info($"Frame rate cap {was} -> {want}{(s.matchScreen ? " (matching the screen)" : "")}.");
		}
	}
}
