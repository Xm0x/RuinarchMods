using Ruinarch.Modding;

namespace RuinarchDebug
{
	/// <summary>
	/// Debug/testing mod. Spawns a persistent MonoBehaviour that draws an IMGUI
	/// overlay for spawn/kill/needs/time and can open the game's built-in dev
	/// console. It also Harmony-patches UIManager.IsMouseOnUI() so clicks on the
	/// overlay do not fall through to the world behind it.
	/// </summary>
	public class RuinarchDebug : IRuinarchMod
	{
		internal static ModLogger Log;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			try
			{
				new HarmonyLib.Harmony("ruinarch.debug").PatchAll(typeof(RuinarchDebug).Assembly);
			}
			catch (System.Exception e)
			{
				Log.Error("Debug menu Harmony patch failed: " + e);
			}
			Log.Info($"{context.Info.name} v{context.Info.version} loaded - click the 'RUIN DBG' button (top-left) in a world.");
			DebugMenu.Bootstrap();
		}
	}
}
