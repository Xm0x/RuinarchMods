using System;
using System.IO;
using Ruinarch.Modding;

namespace RuinarchPlus
{
	/// <summary>
	/// Ruinarch+ 0.11 and older kept the player's options in config.json next to the DLL. From
	/// 0.12 the loader keeps them in Mods/settings/ruinarch.plus.json (shown in the game's Settings
	/// window). On the first start the old file is copied there (same field names; the loader also
	/// reads JsonUtility's numeric enums) and renamed config.json.migrated, so nobody loses them.
	/// </summary>
	internal static class SettingsMigration
	{
		internal static bool Migrate(string oldFile, string newFile, ModLogger log)
		{
			try
			{
				if (!File.Exists(oldFile) || File.Exists(newFile)) return false;
				Directory.CreateDirectory(Path.GetDirectoryName(newFile));
				File.Copy(oldFile, newFile);
				string done = oldFile + ".migrated";
				if (File.Exists(done)) File.Delete(done);
				File.Move(oldFile, done);
				log?.Info($"Moved your Ruinarch+ options from {oldFile} to {newFile}; the old file is now {Path.GetFileName(done)}.");
				return true;
			}
			catch (Exception e)
			{
				log?.Warning($"Could not move the old config.json to {newFile}: {e.Message}. Ruinarch+ uses the settings file as it is.");
				return false;
			}
		}
	}
}
