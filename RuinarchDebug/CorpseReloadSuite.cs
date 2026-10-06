using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator CorpseReloadSuite()
		{
			const string saveName = "RuinarchCorpse-autotest";
			string zip = Path.Combine(UtilityScripts.Utilities.gameSavePath, saveName + ".zip");
			object decayEnabled = PlusBridge.Config("corpseDecayEnabled");
			object decayDays = PlusBridge.Config("corpseDecayDays");
			var bodies = new List<Character>();
			var ids = new List<string>();
			int errorsBefore = _gameErrors;
			try
			{
				PlusBridge.SetConfig("corpseDecayEnabled", true);
				PlusBridge.SetConfig("corpseDecayDays", 2f);
				for (int i = 0; i < 3; i++)
				{
					Character body = StockActor(bodies);
					body.Death();
					ids.Add(body.persistentID);
				}
				yield return WaitGameHours(2f, () => bodies.TrueForAll(c => PlusBridge.DecayStage(c) != null));
				Check("corpse reload: unburied bodies are tracked before the save", () => (bodies.TrueForAll(c => PlusBridge.DecayStage(c) == "Fresh"), "three fresh bodies"));
				SaveCurrentProgressManager saver = SaveManager.Instance.saveCurrentProgressManager;
				if (File.Exists(zip)) File.Delete(zip);
				UIManager.Instance.Pause();
				UIManager.Instance.SetSpeedTogglesState(false);
				_saving = true;
				saver.DoManualSave(saveName);
				yield return WaitReal(() => File.Exists(zip) && !saver.isSaving && !saver.isWritingToDisk, 180f, "the corpse test save to be written");
				_saving = false;
				UIManager.Instance.SetSpeedTogglesState(true);
				GameManager before = GameManager.Instance;
				SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(zip);
				UIManager.Instance.optionsMenu.LoadSave();
				yield return WaitReal(() => GameManager.Instance != null && GameManager.Instance != before && GameManager.Instance.gameHasStarted, 600f, "the corpse test save to load");
				yield return new WaitForSecondsRealtime(15f);
				bodies.Clear();
				foreach (string id in ids) bodies.Add(CharacterManager.Instance.GetCharacterByPersistentID(id));
				Check("corpse reload: loaded corpses retain their actual map locations", () => (bodies.TrueForAll(c => c != null && c.isDead && c.hasMarker && c.gridTileLocation != null), "three loaded bodies on the map"));
				UIManager.Instance.Unpause();
				UIManager.Instance.SetProgressionSpeed4X();
				Time.timeScale = TimeScale;
				yield return WaitGameHours(2f, () => false);
				Check("corpse reload: decay rediscovers loaded bodies instead of retaining old-world references", () => (bodies.TrueForAll(c => c != null && PlusBridge.DecayStage(c) == "Fresh") && _gameErrors == errorsBefore, $"fresh loaded bodies, new game exceptions={_gameErrors - errorsBefore}"));
			}
			finally
			{
				_saving = false;
				if (decayEnabled != null) PlusBridge.SetConfig("corpseDecayEnabled", decayEnabled);
				if (decayDays != null) PlusBridge.SetConfig("corpseDecayDays", decayDays);
				if (File.Exists(zip)) File.Delete(zip);
				foreach (string id in ids)
				{
					var body = CharacterManager.Instance.GetCharacterByPersistentID(id);
					if (body == null) continue;
					body.DestroyMarker(removeFromGame: false);
					body.faction?.LeaveFaction(body);
					CharacterManager.Instance.RemoveCharacter(body);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(body);
				}
			}
		}
	}
}
