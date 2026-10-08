using System;
using System.IO;
using System.Text;
using BayatGames.SaveGameFree;
using BayatGames.SaveGameFree.Serializers;
using HarmonyLib;
using UtilityScripts;

namespace RuinarchPerformance
{
	internal static class SavePreloading
	{
		private static PreloadSerializer _serializer;
		private static string _loadingZip, _loadingFile;
		internal static bool Available => _serializer != null;
		internal static void Install(Harmony harmony)
		{
			if (SaveGame.Serializer.GetType() != typeof(SaveGameJsonSerializer)) return;
			SerializerMetadata.Install(harmony);
			_serializer = new PreloadSerializer(SaveGame.Serializer);
			SaveGame.Serializer = _serializer;
		}
		private static bool Enabled => RuinarchPerformance.Settings.preloadSaves && ReferenceEquals(SaveGame.Serializer, _serializer) && !SaveGame.Encode;
		internal static void Cancel() { _serializer?.Cache.Cancel(); }
		private static void Request(string path)
		{
			if (Enabled && !string.IsNullOrEmpty(path)) _serializer.Cache.Request(path, SaveGame.DefaultEncoding);
			else Cancel();
		}
		private sealed class PreloadSerializer : ISaveGameSerializer
		{
			private readonly ISaveGameSerializer _inner;
			internal readonly SavePreloadCache Cache;
			internal PreloadSerializer(ISaveGameSerializer inner) { _inner = inner; Cache = new SavePreloadCache(inner); }
			public void Serialize<T>(T value, Stream stream, Encoding encoding) => _inner.Serialize(value, stream, encoding);
			public T Deserialize<T>(Stream stream, Encoding encoding)
			{
				if (typeof(T) == typeof(SaveDataCurrentProgress) && Enabled && stream is FileStream file
					&& string.Equals(file.Name, _loadingFile, StringComparison.OrdinalIgnoreCase)
					&& Cache.TryTake(stream, _loadingZip, encoding, out var snapshot))
					return (T)(object)snapshot;
				return _inner.Deserialize<T>(stream, encoding);
			}
		}

		[HarmonyPatch(typeof(SaveWindowUIController), "OnSelectSaveItem")]
		private static class Selection
		{
			private static void Postfix(SaveWindowUIController __instance, SavedGameItem p_item)
			{
				if (__instance.windowFunction == SaveWindowUIController.Window_Function.Load) Request(p_item.savePath);
			}
		}
		[HarmonyPatch(typeof(SaveWindowUIController), nameof(SaveWindowUIController.OnClickClose))]
		private static class Close { private static void Postfix() => Cancel(); }
		[HarmonyPatch(typeof(SaveCurrentProgressManager), nameof(SaveCurrentProgressManager.SetCurrentSaveDataPath))]
		private static class Destination { private static void Postfix(string path) => Request(path); }
		[HarmonyPatch(typeof(SaveCurrentProgressManager), nameof(SaveCurrentProgressManager.LoadSaveDataCurrentProgress))]
		private static class BeginLoad
		{
			private static void Prefix(SaveCurrentProgressManager __instance)
			{
				_loadingZip = __instance.currentSaveDataPath;
				_loadingFile = Path.GetFullPath(Path.Combine(Utilities.tempPath, "mainSave.sav"));
				Request(_loadingZip);
			}
		}
		[HarmonyPatch(typeof(SaveCurrentProgressManager), "GetSaveFileData")]
		private static class FinishRead
		{
			private static void Finalizer() { _loadingZip = _loadingFile = null; Cancel(); }
		}
	}
}
