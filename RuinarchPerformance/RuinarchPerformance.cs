using HarmonyLib;
using Ruinarch.Modding;

namespace RuinarchPerformance
{
	/// <summary>
	/// Performance Mod entry point. Every fix is a [HarmonyPatch] class in this assembly
	/// that keeps the game's behaviour and removes a cost that grows with the map or the
	/// length of the game.
	/// </summary>
	public class RuinarchPerformance : IRuinarchMod
	{
		internal static ModLogger Log;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			new Harmony(context.Info.id).PatchAll(typeof(RuinarchPerformance).Assembly);
			TileObjectListeners.Install();
			Log.Info($"{context.Info.name} v{context.Info.version}: tile-object signal table and job crime-listener cleanup active");
		}
	}
}
