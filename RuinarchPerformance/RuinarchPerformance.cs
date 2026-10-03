using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ruinarch.Modding;

namespace RuinarchPerformance
{
	/// <summary>
	/// Performance Mod entry point. Every fix is a [HarmonyPatch] class in this assembly
	/// that keeps the game's behaviour and removes a cost that grows with the map or the
	/// length of the game. The two listener fixes are patched only when switched on (they
	/// take effect at the next start); the frame cap and the minimap react to changes at once.
	/// </summary>
	public class RuinarchPerformance : IRuinarchMod
	{
		internal static ModLogger Log;
		internal static PerformanceSettings Settings = new PerformanceSettings();

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			Settings = context.Settings.Register<PerformanceSettings>();
			context.Settings.Changed += field =>
			{
				if (field == nameof(PerformanceSettings.matchScreen) || field == nameof(PerformanceSettings.frameRateCap)) FrameRateCap.Apply();
			};
			var harmony = new Harmony(context.Info.id);
			Patch(harmony, typeof(FrameRateCap));
			Patch(harmony, typeof(MinimapRedraw));
			if (Settings.tileObjectListeners)
			{
				Patch(harmony, typeof(TileObjectListeners));
				TileObjectListeners.Install();
			}
			if (Settings.jobCrimeListeners) Patch(harmony, typeof(JobCrimeListeners));
			Log.Info($"{context.Info.name} v{context.Info.version}: tile-object signal table {(Settings.tileObjectListeners ? "on" : "off")}, job crime-listener cleanup {(Settings.jobCrimeListeners ? "on" : "off")}, minimap redraw on change {(Settings.minimapRedraw ? "on" : "off")}, frame cap {(Settings.matchScreen ? "matches the screen" : Settings.frameRateCap.ToString())}");
		}

		// The class and its nested [HarmonyPatch] classes.
		private static void Patch(Harmony harmony, Type owner)
		{
			foreach (Type t in new[] { owner }.Concat(owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
			{
				if (t.IsDefined(typeof(HarmonyPatch), false)) harmony.CreateClassProcessor(t).Patch();
			}
		}
	}
}
