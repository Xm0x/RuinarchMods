using System;
using System.IO;
using UnityEngine;

namespace RuinarchPlus
{
	/// <summary>
	/// Player-editable config for Ruinarch+. On first load a <c>config.json</c> is
	/// written next to the mod DLL with default values; the player edits it and the
	/// flags take effect on the next launch. Kept dependency-free via UnityEngine's
	/// JsonUtility (no Newtonsoft needed).
	/// </summary>
	[Serializable]
	public class RuinarchPlusConfig
	{
		// Off by default: base game behaviour is unchanged until the player opts in.
		public bool disableTutorial = false;

		// Phase 2 - Death, Decay & Disease.
		// Unburied corpses left in the open rot over time and eventually vanish,
		// instead of littering the map forever. Buried graves (in a cemetery) persist.
		public bool corpseDecayEnabled = true;
		// In-game days a corpse takes to fully decompose (480 ticks/day). Floor is 1/4 day.
		public float corpseDecayDays = 3f;


		// Corpse-borne disease (default OFF - opt in). Rotting unburied corpses in a
		// settlement sicken the living present, scaled by corpse count. Requires
		// corpseDecayEnabled (it reads the decay stage).
		public bool corpseDiseaseEnabled = false;
		// Percent infection chance, per rotting corpse, per in-game hour, per nearby villager.
		public int corpseDiseaseChancePerCorpse = 3;

		public static RuinarchPlusConfig Current { get; private set; } = new RuinarchPlusConfig();

		public static void Load(string modDirectory)
		{
			try
			{
				string path = Path.Combine(modDirectory, "config.json");
				if (File.Exists(path))
				{
					string json = File.ReadAllText(path);
					RuinarchPlusConfig loaded = JsonUtility.FromJson<RuinarchPlusConfig>(json);
					if (loaded != null)
					{
						Current = loaded;
					}
					RuinarchPlus.Log?.Info($"Config loaded: disableTutorial={Current.disableTutorial}");
				}
				else
				{
					// Seed a default file so the player has something to edit.
					File.WriteAllText(path, JsonUtility.ToJson(Current, prettyPrint: true));
					RuinarchPlus.Log?.Info($"Config not found; wrote defaults to {path}");
				}
			}
			catch (Exception e)
			{
				// Never take the game down over a bad config: fall back to defaults.
				RuinarchPlus.Log?.Warning($"Config load failed ({e.Message}); using defaults.");
				Current = new RuinarchPlusConfig();
			}
		}
	}
}
