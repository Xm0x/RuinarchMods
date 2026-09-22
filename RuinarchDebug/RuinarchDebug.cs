using Ruinarch.Modding;

namespace RuinarchDebug
{
	/// <summary>
	/// Debug/testing mod. No Harmony patches - it just spawns a persistent
	/// MonoBehaviour that draws an IMGUI overlay for spawn/kill/needs/time,
	/// and can open the game's own built-in dev console.
	/// </summary>
	public class RuinarchDebug : IRuinarchMod
	{
		internal static ModLogger Log;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			Log.Info($"{context.Info.name} v{context.Info.version} loaded - click the 'RUIN DBG' button (top-left) in a world.");
			DebugMenu.Bootstrap();
		}
	}
}
