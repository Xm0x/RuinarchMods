using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using UnityEngine;

namespace RuinarchPlus
{
	// PHASE 2 - Death, Decay & Disease: corpse decomposition.
	//
	// Vanilla corpses (Tombstone TileObjects placed where a character dies) never rot -
	// they litter the map forever unless a villager buries them. This wires a decay timer:
	// an UNBURIED corpse lying in the open advances Fresh -> Bloated -> Rotting -> Skeletal
	// and then fully decomposes (removed from the map). Buried graves (in a CEMETERY),
	// and corpses currently being carried, are left alone.
	//
	// Per-tick hook: GameManager.TickEnded() broadcasts Signals.TICK_ENDED every tick, but
	// Messenger is internal so a mod can't subscribe. Instead we Harmony-postfix TickEnded
	// itself. Corpses are tracked in a registry keyed off Tombstone.OnPlacePOI /
	// OnDestroyPOI. Later Phase 2 work (corpse-borne disease) reads the Rotting/Skeletal
	// stage from here.
	public static class CorpseDecay
	{
		public enum Stage { Fresh, Bloated, Rotting, Skeletal }

		private class Entry
		{
			public int elapsed;
			public Stage stage = Stage.Fresh;
		}

		private const int TicksPerDay = 480; // GameManager resets the day at tick > 480

		private static readonly Dictionary<Tombstone, Entry> _corpses = new Dictionary<Tombstone, Entry>();

		private static int TotalTicks()
		{
			int t = Mathf.RoundToInt(RuinarchPlusConfig.Current.corpseDecayDays * TicksPerDay);
			return Mathf.Max(TicksPerDay / 4, t); // never faster than a quarter day
		}

		// A corpse decays only if it's lying in the open (not in a cemetery).
		private static bool IsDecayable(Tombstone t)
		{
			if (t == null || t.character == null)
			{
				return false;
			}
			LocationGridTile tile = t.gridTileLocation;
			if (tile == null || tile.structure == null)
			{
				return false;
			}
			return tile.structure.structureType != STRUCTURE_TYPE.CEMETERY;
		}

		internal static void Register(Tombstone t)
		{
			if (!RuinarchPlusConfig.Current.corpseDecayEnabled)
			{
				return;
			}
			if (!IsDecayable(t))
			{
				return;
			}
			if (!_corpses.ContainsKey(t))
			{
				_corpses[t] = new Entry();
			}
		}

		internal static void Unregister(Tombstone t)
		{
			if (t != null)
			{
				_corpses.Remove(t);
			}
		}

		internal static void Tick()
		{
			if (_corpses.Count == 0)
			{
				return;
			}
			List<Tombstone> keys = new List<Tombstone>(_corpses.Keys);
			List<Tombstone> toRemove = new List<Tombstone>();
			int total = TotalTicks();
			for (int i = 0; i < keys.Count; i++)
			{
				Tombstone t = keys[i];
				Entry e;
				if (!_corpses.TryGetValue(t, out e))
				{
					continue;
				}
				// corpse already removed / raised / invalid
				if (t == null || t.character == null || t.gridTileLocation == null)
				{
					toRemove.Add(t);
					continue;
				}
				// got buried after registration -> stop decaying it
				LocationStructure structure = t.gridTileLocation.structure;
				if (structure != null && structure.structureType == STRUCTURE_TYPE.CEMETERY)
				{
					toRemove.Add(t);
					continue;
				}
				// paused while a villager is carrying it around
				if (t.isBeingCarriedBy != null)
				{
					continue;
				}

				e.elapsed++;
				if (e.elapsed >= total)
				{
					Decompose(t);
					toRemove.Add(t);
					continue;
				}
				Stage ns = StageFor(e.elapsed, total);
				if (ns != e.stage)
				{
					e.stage = ns;
					RuinarchPlus.Log?.Info($"Corpse of {t.character.name} is now {ns}.");
				}
			}
			for (int i = 0; i < toRemove.Count; i++)
			{
				_corpses.Remove(toRemove[i]);
			}
		}

		private static Stage StageFor(int elapsed, int total)
		{
			float f = (float)elapsed / total;
			if (f < 0.25f)
			{
				return Stage.Fresh;
			}
			if (f < 0.5f)
			{
				return Stage.Bloated;
			}
			if (f < 0.75f)
			{
				return Stage.Rotting;
			}
			return Stage.Skeletal;
		}

		private static void Decompose(Tombstone t)
		{
			LocationGridTile tile = t.gridTileLocation;
			string who = (t.character != null) ? t.character.name : "unknown";
			if (tile != null && tile.structure != null)
			{
				t.SetRespawnCorpseOnDestroy(false); // corpse rots away, don't re-drop / bury-me
				tile.structure.RemovePOI(t);
				RuinarchPlus.Log?.Info($"Corpse of {who} has fully decomposed and returned to the earth.");
			}
		}

		[HarmonyPatch(typeof(Tombstone), "OnPlacePOI")]
		public static class Tombstone_OnPlacePOI
		{
			private static void Postfix(Tombstone __instance)
			{
				Register(__instance);
			}
		}

		[HarmonyPatch(typeof(Tombstone), "OnDestroyPOI")]
		public static class Tombstone_OnDestroyPOI
		{
			private static void Postfix(Tombstone __instance)
			{
				Unregister(__instance);
			}
		}

		[HarmonyPatch(typeof(GameManager), "TickEnded")]
		public static class GameManager_TickEnded
		{
			private static void Postfix()
			{
				Tick();
			}
		}
	}
}
