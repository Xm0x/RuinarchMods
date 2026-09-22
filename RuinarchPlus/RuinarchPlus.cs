using System.Linq;
using HarmonyLib;
using Ruinarch.Modding;

namespace RuinarchPlus
{
	/// <summary>
	/// Ruinarch+ entry point. Each fix is a [HarmonyPatch] class under Fixes/;
	/// OnLoad applies every patch in this assembly and logs what attached.
	/// </summary>
	public class RuinarchPlus : IRuinarchMod
	{
		internal static ModLogger Log;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			Log.Info($"{context.Info.name} v{context.Info.version} loading...");
			RuinarchPlusConfig.Load(context.ModDirectory);

			var harmony = new Harmony(context.Info.id);
			harmony.PatchAll(typeof(RuinarchPlus).Assembly);

			var patched = Harmony.GetAllPatchedMethods()
				.Select(m => (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "." + m.Name)
				.ToArray();
			Log.Info($"Applied {patched.Length} patch(es): {string.Join(", ", patched)}");
		}
	}
}
