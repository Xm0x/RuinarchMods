using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ruinarch.Modding;
using UnityEngine;

namespace RuinarchDebug
{
	/// <summary>RuinarchDebug's own settings class: only for testing the loader's settings files.</summary>
	internal class SettingsTestConfig
	{
		public enum Mode { A, B, C }

		[Section("Test")]
		[Setting("Flag")] public bool flag = true;
		[Setting("Count"), Range(1, 10)] public int count = 5;
		[Setting("Scale"), Range(0f, 1f)] public float scale = 0.5f;
		[Setting("Mode")] public Mode mode = Mode.B;
		[Setting("Name")] public string name = "x";   // declaration mistake: not shown, still saved
		public int[] hidden = { 1, 2 };
	}

	public partial class AutoTest
	{
		private static RegisteredSettings SettingsOf(string id) => RegisteredSettings.All.FirstOrDefault(s => s.ModId == id);
		private static SettingField FieldOf(RegisteredSettings s, string name) => s?.Fields.FirstOrDefault(f => f.Name == name);
		private static JObject SavedJson(RegisteredSettings s) => JObject.Parse(File.ReadAllText(s.FilePath));

		private IEnumerator ModSettingsSuite()
		{
			SettingsFileChecks();
			yield break;
		}

		// The loader's settings file handling, on a test class and a scratch folder.
		private void SettingsFileChecks()
		{
			string dir = Path.Combine(ModLoader.ModsRoot, "RuinarchDebug", "settings-test");
			if (Directory.Exists(dir)) Directory.Delete(dir, true);
			Directory.CreateDirectory(dir);
			string file = Path.Combine(dir, "test.json");
			var log = new ModLogger("ruinarch.debug", ModLoader.LogFile);

			File.WriteAllText(file, "{ not json");
			var first = new SettingsTestConfig();
			RegisteredSettings.Create(first, "ruinarch.debug.test", "Test", file, log, null);
			bool setAside = File.Exists(file + ".bad") && File.ReadAllText(file + ".bad") == "{ not json";
			int rewritten = File.Exists(file) ? (int)JObject.Parse(File.ReadAllText(file))["count"] : -1;
			Check("an unreadable settings file is set aside and the mod starts on its defaults", () =>
				(setAside && first.count == 5 && rewritten == 5, $"set aside={setAside} count={first.count} rewritten count={rewritten}"));

			File.WriteAllText(file, "{\"count\": 99, \"scale\": -3, \"mode\": \"Z\", \"flag\": \"maybe\", \"retired\": 1}");
			var second = new SettingsTestConfig();
			RegisteredSettings s = RegisteredSettings.Create(second, "ruinarch.debug.test", "Test", file, log, null);
			JObject saved = JObject.Parse(File.ReadAllText(file));
			Check("a settings file clamps numbers, ignores unknown names and keeps defaults for bad or missing values", () =>
				(second.count == 10 && second.scale == 0f && second.mode == SettingsTestConfig.Mode.B && second.flag && second.name == "x"
					&& saved["retired"] == null && (string)saved["mode"] == "B" && (int)saved["count"] == 10 && saved["hidden"] != null && saved["name"] != null,
				 $"count={second.count} scale={second.scale} mode={second.mode} flag={second.flag} name={second.name} file={saved.ToString(Newtonsoft.Json.Formatting.None)}"));
			Check("a [Setting] on an unsupported type is not shown; sections carry over", () =>
				(s.Fields.Select(f => f.Name).SequenceEqual(new[] { "flag", "count", "scale", "mode" }) && s.Fields.All(f => f.Section == "Test"),
				 string.Join(", ", s.Fields.Select(f => $"{f.Name}:{f.Kind}:{f.Section}"))));

			s.Set(FieldOf(s, "count"), 0);
			s.Set(FieldOf(s, "mode"), "C");
			saved = JObject.Parse(File.ReadAllText(file));
			Check("changing a setting clamps it, writes the object and saves the file", () =>
				(second.count == 1 && (int)saved["count"] == 1 && second.mode == SettingsTestConfig.Mode.C && (string)saved["mode"] == "C",
				 $"count={second.count}/{saved["count"]} mode={second.mode}/{saved["mode"]}"));

			s.ResetToDefaults();
			Check("reset puts every shown setting back to its default", () =>
				(second.count == 5 && second.mode == SettingsTestConfig.Mode.B && second.scale == 0.5f, $"count={second.count} mode={second.mode} scale={second.scale}"));
			Directory.Delete(dir, true);
		}
	}
}
