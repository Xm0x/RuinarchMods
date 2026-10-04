using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ruinarch.Modding;
using Ruinarch.ModContent;
using UnityEngine;
using UnityEngine.UI;
using SettingsManager = Settings.SettingsManager;

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
			yield return PerformanceSettingsChecks();
			BlightLimitChecks();
			PlusMigrationChecks();
			yield return ModsTabChecks("game");
		}

		// The Mods tab, driven like a player: open Settings, pick the Mods tab, check the list
		// matches the registered mods, pick the Performance Mod, click its minimap checkbox and
		// check the setting and its file follow, click it back, then switch to another tab.
		private IEnumerator ModsTabChecks(string where)
		{
			SettingsManager sm = SettingsManager.Instance;
			if (!Try($"open Settings ({where})", () => sm.OpenSettings())) yield break;
			yield return null;
			Transform root = sm.settingsGO.transform;
			Toggle tab = root.Find("Tabs/Mods Tab")?.GetComponent<Toggle>();
			GameObject panel = root.Find("Mods Options")?.gameObject;
			Check($"the Settings window has a Mods tab ({where})", () => (tab != null && panel != null, $"tab={tab != null} panel={panel != null}"));
			if (tab == null || panel == null) { sm.CloseSettings(); yield break; }
			tab.isOn = true;
			yield return null;
			Toggle[] entries = panel.GetComponentsInChildren<Toggle>(true).Where(t => t.name.StartsWith("Mod: ")).ToArray();
			string[] listed = entries.Select(t => t.name.Substring(5)).OrderBy(n => n).ToArray();
			string[] registered = RegisteredSettings.All.Select(s => s.ModId).OrderBy(n => n).ToArray();
			Check($"the Mods tab lists exactly the mods with settings ({where})", () =>
				(panel.activeInHierarchy && registered.Length > 0 && listed.SequenceEqual(registered), $"shown={panel.activeInHierarchy} listed=[{string.Join(", ", listed)}] registered=[{string.Join(", ", registered)}]"));

			RegisteredSettings perf = SettingsOf("ruinarch.performance");
			if (perf == null) Skip($"clicking a checkbox in the Mods tab changes the setting and its file ({where})", "the Performance Mod has no settings registered");
			else
			{
				Toggle entry = entries.FirstOrDefault(t => t.name == "Mod: ruinarch.performance");
				if (entry != null) entry.isOn = true;
				yield return null;
				Toggle box = panel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Setting: minimapRedraw" && t.gameObject.activeInHierarchy)?.GetComponentInChildren<Toggle>(true);
				SettingField f = FieldOf(perf, "minimapRedraw");
				bool before = (bool)perf.Get(f);
				try
				{
					if (box != null) box.isOn = !box.isOn;
					bool after = (bool)perf.Get(f);
					bool file = (bool)SavedJson(perf)["minimapRedraw"];
					Check($"clicking a checkbox in the Mods tab changes the setting and its file ({where})", () =>
						(box != null && after == !before && file == after && RuinarchPerformanceMinimap() == after, $"row={box != null} {before} -> {after}, file {file}"));
					if (box != null) box.isOn = !box.isOn;
					yield return null;
					yield return Screenshot($"settings-mods-{where}.png");
					Check($"clicking it again puts it back ({where})", () =>
						((bool)perf.Get(f) == before && (bool)SavedJson(perf)["minimapRedraw"] == before, $"now {perf.Get(f)}, file {SavedJson(perf)["minimapRedraw"]}"));
				}
				finally
				{
					// The player's real file: leave it as it was, whatever happened above.
					if ((bool)perf.Get(f) != before) perf.Set(f, before);
				}
			}
			yield return PlusPageChecks(entries, panel, where);
			Toggle gameplay = root.Find("Tabs/Gameplay Tab")?.GetComponent<Toggle>();
			if (gameplay != null) gameplay.isOn = true;
			yield return null;
			Check($"another tab hides the Mods panel ({where})", () => (!panel.activeSelf, $"panel active={panel.activeSelf}"));
			sm.CloseSettings();
		}

		// The Performance Mod's own object, read by reflection (the mod's static Settings).
		private static bool RuinarchPerformanceMinimap()
		{
			object o = AccessTools.Field(AccessTools.TypeByName("RuinarchPerformance.RuinarchPerformance"), "Settings")?.GetValue(null);
			return o != null && (bool)AccessTools.Field(o.GetType(), "minimapRedraw").GetValue(o);
		}

		// Ruinarch+ 0.11 kept config.json (JsonUtility) in its folder; the first start of 0.12 moves it.
		// Works in its own folder under RuinarchDebug, never on the player's real files.
		private void PlusMigrationChecks()
		{
			Type config = AccessTools.TypeByName("RuinarchPlus.RuinarchPlusConfig");
			MethodInfo migrate = AccessTools.Method(AccessTools.TypeByName("RuinarchPlus.SettingsMigration"), "Migrate");
			if (config == null || migrate == null)
			{
				Skip("an old Ruinarch+ config.json moves to Mods/settings with its values", "Ruinarch+ with settings is not loaded");
				return;
			}
			string dir = Path.Combine(ModLoader.ModsRoot, "RuinarchDebug", "migration-test");
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
				Directory.CreateDirectory(dir);
				string old = Path.Combine(dir, "config.json");
				string target = Path.Combine(dir, "settings", "ruinarch.plus.json");
				var log = new ModLogger("ruinarch.debug", ModLoader.LogFile);
				object custom = Activator.CreateInstance(config);
				AccessTools.Field(config, "gossipChance").SetValue(custom, 7);
				AccessTools.Field(config, "corpseDecayDays").SetValue(custom, 1.5f);
				AccessTools.Field(config, "curfewEnabled").SetValue(custom, false);
				File.WriteAllText(old, JsonUtility.ToJson(custom, true));
				bool moved = (bool)migrate.Invoke(null, new object[] { old, target, log });
				object loaded = Activator.CreateInstance(config);
				RegisteredSettings.Create(loaded, "ruinarch.plus", "Ruinarch+", target, log, null);
				int gossip = (int)AccessTools.Field(config, "gossipChance").GetValue(loaded);
				float decay = (float)AccessTools.Field(config, "corpseDecayDays").GetValue(loaded);
				bool curfew = (bool)AccessTools.Field(config, "curfewEnabled").GetValue(loaded);
				Check("an old Ruinarch+ config.json moves to Mods/settings with its values", () =>
					(moved && gossip == 7 && decay == 1.5f && !curfew && !File.Exists(old) && File.Exists(old + ".migrated"),
					 $"moved={moved} gossip={gossip} decay={decay} curfew={curfew} old left={File.Exists(old)} renamed={File.Exists(old + ".migrated")}"));
				bool again = (bool)migrate.Invoke(null, new object[] { old, target, log });
				Check("the migration runs once", () => (!again, $"second run moved={again}"));
			}
			finally
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			RegisteredSettings plus = SettingsOf("ruinarch.plus");
			Check("Ruinarch+ shows its options in the Mods tab, none restart-only", () =>
				(plus != null && plus.Fields.Count == 57 && plus.Fields.All(f => !f.RequiresRestart) && plus.Fields.All(f => f.Section != null),
				 $"registered={plus != null} shown={plus?.Fields.Count} restart={plus?.Fields.Count(f => f.RequiresRestart)} unsectioned={plus?.Fields.Count(f => f.Section == null)}"));
		}

		// The Ruinarch+ page: one row per option, scrolled partway down for a screenshot.
		private IEnumerator PlusPageChecks(Toggle[] entries, GameObject panel, string where)
		{
			Toggle entry = entries.FirstOrDefault(t => t.name == "Mod: ruinarch.plus");
			if (entry == null) { Skip($"the Ruinarch+ page shows all its options ({where})", "Ruinarch+ is not listed"); yield break; }
			entry.isOn = true;
			yield return null;
			int rows = panel.GetComponentsInChildren<Transform>(false).Count(t => t.name.StartsWith("Setting: "));
			int sections = panel.GetComponentsInChildren<Transform>(false).Count(t => t.name.StartsWith("Section: "));
			ScrollRect scroll = panel.GetComponentsInChildren<ScrollRect>(false).FirstOrDefault(s => s.content != null && s.content.GetComponentsInChildren<Transform>(false).Any(t => t.name.StartsWith("Setting: ")));
			Canvas.ForceUpdateCanvases();
			if (scroll != null) scroll.verticalNormalizedPosition = 0.6f;
			yield return null;
			yield return null;
			yield return Screenshot($"settings-mods-plus-{where}.png");
			Check($"the Ruinarch+ page shows all its options ({where})", () =>
				(rows == 57 && sections == 11 && scroll != null && scroll.content.rect.height > ((RectTransform)scroll.viewport).rect.height,
				 $"rows={rows} sections={sections} scroll={scroll != null} content={scroll?.content.rect.height} viewport={(scroll?.viewport as RectTransform)?.rect.height}"));
		}

		// The Performance Mod's real settings: a live one (frame cap) applies at once and raises
		// Changed; a restart-only one is saved and pending while the running game keeps its fix.
		private IEnumerator PerformanceSettingsChecks()
		{
			RegisteredSettings perf = SettingsOf("ruinarch.performance");
			if (perf == null)
			{
				Skip("changing the frame rate cap applies at once and is saved", "the Performance Mod has no settings registered");
				yield break;
			}
			SettingField match = FieldOf(perf, "matchScreen"), cap = FieldOf(perf, "frameRateCap"), table = FieldOf(perf, "tileObjectListeners");
			object oldMatch = perf.Get(match), oldCap = perf.Get(cap), oldTable = perf.Get(table);
			int before = Application.targetFrameRate;
			var raised = new List<string>();
			Action<string> onChanged = n => raised.Add(n);
			perf.Handle.Changed += onChanged;
			try
			{
				perf.Set(match, false);
				perf.Set(cap, 60);
				int after = Application.targetFrameRate;
				JObject saved = SavedJson(perf);
				Check("changing the frame rate cap applies at once, is saved and raises Changed", () =>
					(after == 60 && (int)saved["frameRateCap"] == 60 && !(bool)saved["matchScreen"] && raised.Contains("frameRateCap") && raised.Contains("matchScreen"),
					 $"targetFrameRate {before} -> {after}; file matchScreen={saved["matchScreen"]} cap={saved["frameRateCap"]}; raised=[{string.Join(",", raised)}]"));
				perf.Set(cap, oldCap);
				perf.Set(match, oldMatch);
				Check("putting the frame rate settings back restores the cap", () => (Application.targetFrameRate == before, $"{Application.targetFrameRate}, was {before}"));

				raised.Clear();
				int tableBefore = PerfBridge.TableCount;
				perf.Set(table, false);
				saved = SavedJson(perf);
				bool pending = perf.RestartPending(table);
				bool objectKept = (bool)table.Field.GetValue(perf.Target);
				bool patched = Harmony.GetPatchInfo(AccessTools.Method(typeof(TileObject), "SubscribeListeners"))?.Prefixes.Any(p => p.owner == "ruinarch.performance") == true;
				yield return WaitGameHours(0.5f, null);
				Check("a restart-only setting is saved and announced but leaves the running game alone", () =>
					(!(bool)saved["tileObjectListeners"] && pending && objectKept && patched && raised.Contains("tileObjectListeners") && PerfBridge.TableCount > 0,
					 $"file={saved["tileObjectListeners"]} pending={pending} object={objectKept} patched={patched} raised=[{string.Join(",", raised)}] table {tableBefore} -> {PerfBridge.TableCount}"));
				perf.Set(table, oldTable);
				Check("setting it back clears the restart note", () =>
					(!perf.RestartPending(table) && (bool)SavedJson(perf)["tileObjectListeners"] == (bool)oldTable, $"pending={perf.RestartPending(table)}"));
			}
			finally
			{
				perf.Handle.Changed -= onChanged;
				// The player's real file: leave it as it was, whatever happened above.
				if (!Equals(perf.Get(cap), oldCap)) perf.Set(cap, oldCap);
				if (!Equals(perf.Get(match), oldMatch)) perf.Set(match, oldMatch);
				if (!Equals(perf.Get(table), oldTable)) perf.Set(table, oldTable);
			}
		}

		// Ruinarch+'s Blight Heart limit is live: changing it in a running world changes the build
		// skill's charges at once.
		private void BlightLimitChecks()
		{
			const string name = "changing the Blight Heart limit changes the build skill's charges at once";
			RegisteredSettings plus = SettingsOf("ruinarch.plus");
			SettingField limit = FieldOf(plus, "blightHeartLimit");
			DemonicStructurePlayerSkill skill = PlayerSkillManager.Instance?.GetDemonicStructureSkillData(ModContent.SkillTypeFor("ruinarch.plus.blight_heart"));
			if (limit == null || skill == null || !skill.isInUse)
			{
				Skip(name, $"limit setting={limit != null} skill={skill != null} granted={skill?.isInUse}");
				return;
			}
			object old = plus.Get(limit);
			// Set saves the whole live Ruinarch+ object, which holds the harness's run-only values.
			byte[] file = File.Exists(plus.FilePath) ? File.ReadAllBytes(plus.FilePath) : null;
			try
			{
				int was = (int)old, other = was == 7 ? 8 : 7;
				int built = was - skill.charges;
				plus.Set(limit, other);
				int maxAfter = skill.maxCharges, chargesAfter = skill.charges;
				plus.Set(limit, old);
				Check(name, () => (maxAfter == other && chargesAfter == Math.Max(0, other - built) && skill.maxCharges == was,
					$"limit {was} -> {other}: max {maxAfter}, charges {chargesAfter} (built {built}); back to {was}: max {skill.maxCharges}"));
			}
			finally
			{
				if (!Equals(plus.Get(limit), old)) plus.Set(limit, old);
				if (file != null) File.WriteAllBytes(plus.FilePath, file);
			}
		}

		// The player's settings files as they were when the run began, put back when it ends:
		// checks that save a mod's settings save its whole live object, run-only values included.
		private readonly Dictionary<string, byte[]> _settingsFiles = new Dictionary<string, byte[]>();

		private void SnapshotSettings()
		{
			string dir = Path.Combine(ModLoader.ModsRoot, "settings");
			if (!Directory.Exists(dir)) return;
			foreach (string path in Directory.GetFiles(dir, "*.json"))
			{
				_settingsFiles[path] = File.ReadAllBytes(path);
				Log($"settings file kept for the end of the run: {Path.GetFileName(path)}");
			}
		}

		private void RestoreSettings()
		{
			foreach (KeyValuePair<string, byte[]> kv in _settingsFiles)
			{
				try
				{
					File.WriteAllBytes(kv.Key, kv.Value);
					Log($"settings file put back: {Path.GetFileName(kv.Key)}");
				}
				catch (Exception e)
				{
					Log($"settings file {Path.GetFileName(kv.Key)} could not be put back: {e.Message}");
				}
			}
			_settingsFiles.Clear();
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
