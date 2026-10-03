using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Traits;
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
	/// Here the tile objects sit in one table behind a single listener per signal. Subscribing
	/// and unsubscribing are constant time. A broadcast walks the table in subscription order
	/// and calls each tile object as many times as it subscribed, skipping only calls that
	/// would do nothing:
	/// - DisconnectFromStructure is empty in TileObject, so only types that override it are
	///   called.
	/// - TileObject.DisconnectFromCharacter only touches the character where the object refers
	///   to it (an assumption about it, a slot it uses, being carried by it, or a trait that
	///   reacts or lists it as responsible). Every removed character used to call all of a map's
	///   tile objects (40-50 ms at 75,000 objects, one stutter per dead monster); now it calls
	///   those, plus every type that overrides the handler or RemoveUser.
	/// The game clears these signal lists when the world scene unloads, and the table is
	/// cleared at the same moment.
	/// </summary>
	internal static class TileObjectListeners
	{
		private const string CharacterKey = "DisconnectFromCharacter";
		private const string StructureKey = "DisconnectFromStructure";
		/// <summary>Build index of the world scene, the one SignalHandler.CleanUp clears on unload.</summary>
		private const int WorldScene = 1;
		/// <summary>Removed entries leave a gap; the list is compacted once gaps outnumber entries.</summary>
		private const int CompactAfter = 4096;

		private sealed class Entry
		{
			internal TileObject Tile;
			internal int Count;
			internal int Index;
			internal bool CharacterHandlerOverridden;
			internal bool StructureHandlerOverridden;
		}

		private static readonly object Gate = new object();
		private static readonly Dictionary<TileObject, Entry> Subscribed = new Dictionary<TileObject, Entry>();
		private static readonly List<Entry> Order = new List<Entry>();
		private static int _gaps;
		private static int _broadcasting;
		private static bool _hooked;

		private static readonly Dictionary<Type, (bool character, bool structure)> TypeOverrides = new Dictionary<Type, (bool, bool)>();
		private static readonly Dictionary<Type, bool> TraitOverrides = new Dictionary<Type, bool>();

		private static readonly SignalHandler<Character>.SignalListener CharacterListener = OnCharacter;
		private static readonly SignalHandler<LocationStructure>.SignalListener StructureListener = OnStructure;
		// Virtual calls: subclasses override these handlers.
		private static readonly Action<TileObject, Character> DisconnectCharacter =
			AccessTools.MethodDelegate<Action<TileObject, Character>>(AccessTools.Method(typeof(TileObject), CharacterKey));
		private static readonly Action<TileObject, LocationStructure> DisconnectStructure =
			AccessTools.MethodDelegate<Action<TileObject, LocationStructure>>(AccessTools.Method(typeof(TileObject), StructureKey));
		private static readonly Func<TileObject, TileObjectSlotItem[]> Slots =
			AccessTools.MethodDelegate<Func<TileObject, TileObjectSlotItem[]>>(AccessTools.PropertyGetter(typeof(TileObject), "slots"));

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
				Order.Clear();
				_gaps = 0;
				_hooked = false;
			}
		}

		private static (bool character, bool structure) Overrides(Type t)
		{
			if (!TypeOverrides.TryGetValue(t, out var o))
			{
				const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
				bool Own(string name, Type[] args) => t.GetMethod(name, flags, null, args, null)?.DeclaringType != typeof(TileObject);
				o = (Own(CharacterKey, new[] { typeof(Character) }) || Own("RemoveUser", new[] { typeof(Character) }),
					Own(StructureKey, new[] { typeof(LocationStructure) }));
				TypeOverrides[t] = o;
			}
			return o;
		}

		private static bool TraitReacts(Trait trait)
		{
			Type t = trait.GetType();
			if (!TraitOverrides.TryGetValue(t, out bool own))
			{
				own = t.GetMethod(CharacterKey, new[] { typeof(IPointOfInterest), typeof(Character) })?.DeclaringType != typeof(Trait);
				TraitOverrides[t] = own;
			}
			return own;
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
				if (!Subscribed.TryGetValue(t, out Entry e))
				{
					var o = Overrides(t.GetType());
					e = new Entry { Tile = t, Index = Order.Count, CharacterHandlerOverridden = o.character, StructureHandlerOverridden = o.structure };
					Order.Add(e);
					Subscribed[t] = e;
				}
				e.Count++;
			}
		}

		private static void Remove(TileObject t)
		{
			lock (Gate)
			{
				if (!Subscribed.TryGetValue(t, out Entry e)) return;
				if (--e.Count > 0) return;
				Subscribed.Remove(t);
				Order[e.Index] = null;
				_gaps++;
				CompactIfIdle();
			}
		}

		private static void CompactIfIdle()
		{
			if (_broadcasting > 0 || _gaps < CompactAfter || _gaps * 2 < Order.Count) return;
			int kept = 0;
			for (int i = 0; i < Order.Count; i++)
			{
				Entry e = Order[i];
				if (e == null) continue;
				e.Index = kept;
				Order[kept++] = e;
			}
			Order.RemoveRange(kept, Order.Count - kept);
			_gaps = 0;
		}

		/// <summary>False only when TileObject.DisconnectFromCharacter would leave this object unchanged.</summary>
		private static bool RefersTo(Entry e, Character c)
		{
			if (e.CharacterHandlerOverridden) return true;
			TileObject t = e.Tile;
			if (t.isBeingCarriedBy == c) return true;
			List<Character> assumed = t.charactersThatAlreadyAssumed;
			if (assumed != null && assumed.Count > 0 && assumed.Contains(c)) return true;
			TileObjectSlotItem[] slots = Slots(t);
			if (slots != null)
			{
				for (int i = 0; i < slots.Length; i++)
				{
					if (slots[i] == null || slots[i].user == c) return true;
				}
			}
			Dictionary<string, Trait> traits = t.traitContainer?.allTraitsAndStatuses;
			if (traits != null && traits.Count > 0)
			{
				foreach (Trait trait in traits.Values)
				{
					if (trait == null || TraitReacts(trait)) return true;
					List<Character> responsible = trait.responsibleCharacters;
					if (responsible != null && responsible.Contains(c)) return true;
				}
			}
			return false;
		}

		// Entries added during the broadcast are reached, as in the game's list; an entry
		// removed before its turn is skipped.
		private static void OnCharacter(Character c)
		{
			lock (Gate)
			{
				_broadcasting++;
				try
				{
					for (int i = 0; i < Order.Count; i++)
					{
						Entry e = Order[i];
						if (e == null || !RefersTo(e, c)) continue;
						for (int n = e.Count; n > 0 && e.Count > 0; n--) DisconnectCharacter(e.Tile, c);
					}
				}
				finally
				{
					_broadcasting--;
					CompactIfIdle();
				}
			}
		}

		private static void OnStructure(LocationStructure s)
		{
			lock (Gate)
			{
				_broadcasting++;
				try
				{
					for (int i = 0; i < Order.Count; i++)
					{
						Entry e = Order[i];
						if (e == null || !e.StructureHandlerOverridden) continue;
						for (int n = e.Count; n > 0 && e.Count > 0; n--) DisconnectStructure(e.Tile, s);
					}
				}
				finally
				{
					_broadcasting--;
					CompactIfIdle();
				}
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
