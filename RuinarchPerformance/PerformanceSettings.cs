using Ruinarch.Modding;
using UnityEngine;

namespace RuinarchPerformance
{
	/// <summary>The Performance Mod's options, shown in the game's Settings window (Mods tab).</summary>
	[ModSettings("Performance Mod")]
	public class PerformanceSettings
	{
		[Section("Frame rate")]
		[Setting("Match screen refresh rate", "On a screen faster than 144 Hz, the frame rate cap follows the screen. Vertical sync in the Graphics tab still comes first.")]
		public bool matchScreen = true;

		[Setting("Frame rate cap", "The highest frame rate while matching the screen is off. Vertical sync in the Graphics tab still comes first."), Range(30, 360)]
		public int frameRateCap = 144;

		[Section("Fixes")]
		[Setting("Minimap redraws only when it changes", "Off: the minimap is drawn every frame, as in the base game.")]
		public bool minimapRedraw = true;

		[Setting("Tile objects share one signal listener", "Removing a creature or building no longer checks every tree and rock on the map."), RequiresRestart]
		public bool tileObjectListeners = true;

		[Setting("Finished jobs drop their crime listener", "Jobs stop leaving listeners behind that slow the game down over a long game."), RequiresRestart]
		public bool jobCrimeListeners = true;

		[Setting("Preload selected saves", "Prepare one selected save in RAM while browsing or changing scenes. Uses extra memory; oversized saves use the ordinary reader. Off: no background preparation.")]
		public bool preloadSaves = true;
	}
}
