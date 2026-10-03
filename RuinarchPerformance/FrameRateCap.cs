using HarmonyLib;
using Settings;
using UnityEngine;

namespace RuinarchPerformance
{
	/// <summary>
	/// The game caps the frame rate at 144 (SettingsManager.targetFrameRate, applied once in
	/// SettingsManager.Awake). On a screen that refreshes faster, the cap is raised to the
	/// screen's refresh rate; it is never lowered. Vertical sync, when switched on in the
	/// game's options, still takes precedence, as before.
	///
	/// The character tick budget follows (CharacterTickManager takes 0.9 of a frame at the
	/// cap, read when the world loads), so per second the game spends the same time on it.
	/// </summary>
	[HarmonyPatch(typeof(SettingsManager), "Awake")]
	internal static class FrameRateCap
	{
		private static void Postfix(SettingsManager __instance)
		{
			if (SettingsManager.Instance != __instance) return;
			int game = Application.targetFrameRate;
			int screen = Screen.currentResolution.refreshRate;
			if (game <= 0 || screen <= game) return;
			Application.targetFrameRate = screen;
			RuinarchPerformance.Log?.Info($"Frame rate cap raised from {game} to the screen's {screen} Hz.");
		}
	}
}
