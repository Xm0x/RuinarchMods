using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using Ruinarch.Modding;
using Steamworks;
using UnityEngine;

namespace WorkshopPublish
{
	/// <summary>
	/// Release tool, never shipped to players. tools/publish-workshop.sh installs it for one
	/// launch: it uploads a package folder through the game's own Steam session, then quits
	/// the game. With an item id it uploads a new version of that item: only the files and a
	/// change note are sent, the title, description, images and visibility stay as they are
	/// on Steam. With item 0 it first creates a new public item with the given title,
	/// description and tag.
	/// </summary>
	public class Publisher : IRuinarchMod
	{
		internal static ModLogger Log;
		internal static string Dir;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			Dir = context.ModDirectory;
			var go = new GameObject("Workshop publisher");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.AddComponent<Runner>();
		}
	}

	internal class Runner : MonoBehaviour
	{
		private CallResult<CreateItemResult_t> _create;
		private CallResult<SubmitItemUpdateResult_t> _submit;
		private UGCUpdateHandle_t _handle = UGCUpdateHandle_t.Invalid;
		private bool _done;

		private IEnumerator Start()
		{
			string request = Path.Combine(Publisher.Dir, "publish.json");
			JObject json;
			try { json = JObject.Parse(File.ReadAllText(request)); }
			catch (Exception e) { Finish("FAIL cannot read " + request + ": " + e.Message); yield break; }
			ulong item = (ulong)json["item"];
			string folder = (string)json["folder"];
			string note = (string)json["note"];

			float waited = 0f;
			while (!SteamReady())
			{
				if ((waited += Time.unscaledDeltaTime) > 120f) { Finish("FAIL Steam did not start within two minutes"); yield break; }
				yield return null;
			}
			if (!Directory.Exists(folder) || !File.Exists(Path.Combine(folder, "mod.json")))
			{
				Finish("FAIL not a package folder: " + folder);
				yield break;
			}
			if (item == 0)
			{
				bool created = false;
				_create = CallResult<CreateItemResult_t>.Create();
				_create.Set(SteamUGC.CreateItem(new AppId_t(909320), EWorkshopFileType.k_EWorkshopFileTypeCommunity), (result, ioFailure) =>
				{
					if (ioFailure || result.m_eResult != EResult.k_EResultOK)
						Finish($"FAIL create item: {(ioFailure ? "I/O failure" : result.m_eResult.ToString())}");
					else
					{
						item = result.m_nPublishedFileId.m_PublishedFileId;
						Publisher.Log.Info($"Created Workshop item {item}");
						created = true;
					}
				});
				while (!created && !_done) yield return null;
				if (_done) yield break;
			}

			_handle = SteamUGC.StartItemUpdate(new AppId_t(909320), new PublishedFileId_t(item));
			if (json["title"] != null)
			{
				bool set = SteamUGC.SetItemTitle(_handle, (string)json["title"])
					&& SteamUGC.SetItemDescription(_handle, (string)json["description"] ?? "")
					&& SteamUGC.SetItemTags(_handle, new System.Collections.Generic.List<string> { (string)json["tag"] })
					&& SteamUGC.SetItemVisibility(_handle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
				if (!set) { Finish($"FAIL Steam rejected the title, description, tag or visibility of item {item}"); yield break; }
			}
			if (!SteamUGC.SetItemContent(_handle, folder)) { Finish("FAIL Steam rejected the content folder " + folder); yield break; }
			Publisher.Log.Info($"Uploading {folder} to Workshop item {item}...");
			_submit = CallResult<SubmitItemUpdateResult_t>.Create();
			_submit.Set(SteamUGC.SubmitItemUpdate(_handle, note), (result, ioFailure) =>
			{
				if (ioFailure || result.m_eResult != EResult.k_EResultOK)
					Finish($"FAIL upload to item {item}: {(ioFailure ? "I/O failure" : result.m_eResult.ToString())}");
				else
					Finish($"OK item {item}" + (result.m_bUserNeedsToAcceptWorkshopLegalAgreement ? " (accept the Workshop legal agreement on the item page)" : ""));
			});

			while (!_done)
			{
				EItemUpdateStatus stage = SteamUGC.GetItemUpdateProgress(_handle, out ulong sent, out ulong total);
				Publisher.Log.Info($"{stage.ToString().Replace("k_EItemUpdateStatus", "")} {sent}/{total} bytes");
				yield return new WaitForSecondsRealtime(2f);
			}
		}

		private static bool SteamReady()
		{
			try { return SteamManager.Initialized; }
			catch { return false; }
		}

		private void Finish(string result)
		{
			_done = true;
			Publisher.Log.Info(result);
			File.WriteAllText(Path.Combine(Publisher.Dir, "publish-result.txt"), result + "\n");
			Application.Quit();
		}
	}
}
