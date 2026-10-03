using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine.SceneManagement;

namespace RuinarchPerformance
{
	/// <summary>
	/// Every tile object (each tree, rock, crop, bed...) subscribes two signals,
	/// DisconnectFromCharacter and DisconnectFromStructure (TileObject.SubscribeListeners).
	/// SignalHandler keeps one List per signal and unsubscribes with List.Remove, a linear
	/// scan comparing delegates, so on a map with 200,000 tile objects every unsubscribe from
	/// those two signals costs about 3 ms. Finished jobs, parties, combat and destroyed
	/// objects all unsubscribe, several times a frame at 4x speed.
	///
	/// Here the tile objects sit in one table behind a single listener per signal. A
	/// broadcast still reaches every subscribed tile object (as many times as it subscribed);
	/// subscribing and unsubscribing are constant time. The game clears these signal lists when
	/// the world scene unloads, and the table is cleared at the same moment.
	/// </summary>
	internal static class TileObjectListeners
	{
		private const string CharacterKey = "DisconnectFromCharacter";
		private const string StructureKey = "DisconnectFromStructure";
		/// <summary>Build index of the world scene, the one SignalHandler.CleanUp clears on unload.</summary>
		private const int WorldScene = 1;

		private static readonly object Gate = new object();
		private static readonly Dictionary<TileObject, int> Subscribed = new Dictionary<TileObject, int>();
		private static bool _hooked;
		private static readonly SignalHandler<Character>.SignalListener CharacterListener = OnCharacter;
		private static readonly SignalHandler<LocationStructure>.SignalListener StructureListener = OnStructure;
		// Virtual calls: subclasses override these handlers.
		private static readonly Action<TileObject, Character> DisconnectCharacter =
			AccessTools.MethodDelegate<Action<TileObject, Character>>(AccessTools.Method(typeof(TileObject), CharacterKey));
		private static readonly Action<TileObject, LocationStructure> DisconnectStructure =
			AccessTools.MethodDelegate<Action<TileObject, LocationStructure>>(AccessTools.Method(typeof(TileObject), StructureKey));

		internal static void Install()
		{
			SceneManager.sceneUnloaded += OnSceneUnloaded;
		}

		/// <summary>Tile objects currently subscribed (the harness reads this).</summary>
		internal static int Count
		{
			get { lock (Gate) return Subscribed.Count; }
		}

		private static void OnSceneUnloaded(Scene scene)
		{
			if (scene.buildIndex != WorldScene) return;
			lock (Gate)
			{
				Subscribed.Clear();
				_hooked = false;
			}
		}

		private static void Add(TileObject t, bool shouldLock)
		{
			// World loading subscribes from worker threads (shouldLock true).
			lock (Gate)
			{
				if (!_hooked)
				{
					SignalHandler<Character>.AddListener(CharacterKey, CharacterListener, shouldLock);
					SignalHandler<LocationStructure>.AddListener(StructureKey, StructureListener, shouldLock);
					_hooked = true;
				}
				Subscribed.TryGetValue(t, out int n);
				Subscribed[t] = n + 1;
			}
		}

		private static void Remove(TileObject t)
		{
			lock (Gate)
			{
				if (Subscribed.TryGetValue(t, out int n))
				{
					if (n <= 1) Subscribed.Remove(t);
					else Subscribed[t] = n - 1;
				}
			}
		}

		private static TileObject[] Snapshot()
		{
			lock (Gate) return Subscribed.Keys.ToArray();
		}

		private static int CountOf(TileObject t)
		{
			lock (Gate) return Subscribed.TryGetValue(t, out int n) ? n : 0;
		}

		// A tile object that unsubscribes during the broadcast, before its turn, is skipped,
		// as it would be in the game's list.
		private static void OnCharacter(Character c)
		{
			foreach (TileObject t in Snapshot())
			{
				for (int i = CountOf(t); i > 0; i--) DisconnectCharacter(t, c);
			}
		}

		private static void OnStructure(LocationStructure s)
		{
			foreach (TileObject t in Snapshot())
			{
				for (int i = CountOf(t); i > 0; i--) DisconnectStructure(t, s);
			}
		}

		[HarmonyPatch(typeof(TileObject), "SubscribeListeners")]
		internal static class Patch_Subscribe
		{
			private static bool Prefix(TileObject __instance, bool shouldLock)
			{
				Add(__instance, shouldLock);
				return false;
			}
		}

		[HarmonyPatch(typeof(TileObject), "SubscribeListenersDuringSeize")]
		internal static class Patch_SubscribeDuringSeize
		{
			private static bool Prefix(TileObject __instance)
			{
				Add(__instance, false);
				return false;
			}
		}

		[HarmonyPatch(typeof(TileObject), "UnsubscribeListeners")]
		internal static class Patch_Unsubscribe
		{
			private static bool Prefix(TileObject __instance)
			{
				Remove(__instance);
				return false;
			}
		}

		[HarmonyPatch(typeof(TileObject), "UnsubscribeListenersDuringSeize")]
		internal static class Patch_UnsubscribeDuringSeize
		{
			private static bool Prefix(TileObject __instance)
			{
				Remove(__instance);
				return false;
			}
		}
	}
}
