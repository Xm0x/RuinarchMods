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
		internal static string ModDir;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			Log.Info($"{context.Info.name} v{context.Info.version} loading...");
			ModDir = context.ModDirectory;
			RuinarchPlusConfig.Load(context.ModDirectory);

			var harmony = new Harmony(context.Info.id);
			harmony.PatchAll(typeof(RuinarchPlus).Assembly);

			// Register Ruinarch+ new-content features against the ModContent framework.
			Phase2.MassGraveFeature.Register();

			var patched = Harmony.GetAllPatchedMethods()
				.Select(m => (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "." + m.Name)
				.ToArray();
			// Several features postfix the same GameManager.TickEnded method, so the
			// distinct-method count understates our patches - also count patch CLASSES.
			int patchClasses = typeof(RuinarchPlus).Assembly.GetTypes()
				.Count(t => t.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0);
			Log.Info($"Applied {patchClasses} patch class(es) over {patched.Length} method(s): {string.Join(", ", patched)}");
		}
	}
}
