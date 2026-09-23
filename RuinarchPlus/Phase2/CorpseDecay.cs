using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RuinarchPlus
{
	// PHASE 2 - Death, Decay & Disease: corpse decomposition.
	//
	// A dead character keeps its map marker as a body lying where it fell; the game only
	// creates a Tombstone when a villager BURIES it (BuryCharacter.AfterBurySuccess is the
	// sole Tombstone creation site). So "unburied corpse" = a dead Character that still has a
	// marker and no grave. Vanilla never rots those; they lie forever unless buried.
	//
	// This wires a decay timer onto exactly those bodies: Fresh -> Bloated -> Rotting ->
	// Skeletal, then the remains are gone from the map. Anything buried (a grave in a
	// Cemetery, a Mass Grave, or anywhere else) never rots here, and a body pauses while a
	// villager is carrying it. Corpse-borne disease (CorpseDisease) reads the stage.
	//
	// Corpses are discovered by an hourly scan of the region rather than a death hook, so
	// bodies from loaded saves, spawned-and-killed creatures and every death path are all
	// covered. Decay advances per tick via a postfix on GameManager.TickEnded (Messenger is
	// internal, so the TICK_ENDED signal can't be subscribed to from a mod).
	public static class CorpseDecay
	{
		public enum Stage { Fresh, Bloated, Rotting, Skeletal }

		private class Entry
		{
			public int elapsed;
			public Stage stage = Stage.Fresh;
		}

		private const int TicksPerDay = GameManager.ticksPerDay;

		private static readonly Dictionary<Character, Entry> _corpses = new Dictionary<Character, Entry>();

		private static int _tickAccum;

		private static int TotalTicks()
		{
			int t = Mathf.RoundToInt(RuinarchPlusConfig.Current.corpseDecayDays * TicksPerDay);
			return Mathf.Max(TicksPerDay / 4, t); // never faster than a quarter day
		}

		/// <summary>An unburied body lying on the map: dead, still has its marker, no grave.</summary>
		internal static bool IsUnburiedCorpse(Character c)
		{
			return c != null && c.isDead && c.hasMarker && c.grave == null && c.gridTileLocation != null && c.minion == null;
		}

		/// <summary>Decay stage of an unburied corpse, or null if it is not being tracked.</summary>
		internal static Stage? GetStage(Character c)
		{
			return c != null && _corpses.TryGetValue(c, out Entry e) ? e.stage : (Stage?)null;
		}

		// Corpse-borne disease reads this: unburied bodies at the Rotting or Skeletal stage
		// are infectious. Returns a fresh list (safe to mutate).
		internal static List<Character> GetRottingCorpses()
		{
			List<Character> list = new List<Character>();
			foreach (KeyValuePair<Character, Entry> kv in _corpses)
			{
				if (kv.Value.stage == Stage.Rotting || kv.Value.stage == Stage.Skeletal)
				{
					list.Add(kv.Key);
				}
			}
			return list;
		}

		internal static void Tick()
		{
			if (!RuinarchPlusConfig.Current.corpseDecayEnabled)
			{
				return;
			}
			_tickAccum++;
			if (_tickAccum >= GameManager.ticksPerHour)
			{
				_tickAccum = 0;
				Discover();
			}
			if (_corpses.Count == 0)
			{
				return;
			}
			List<Character> keys = new List<Character>(_corpses.Keys);
			int total = TotalTicks();
			for (int i = 0; i < keys.Count; i++)
			{
				Character c = keys[i];
				if (!IsUnburiedCorpse(c))
				{
					// buried, raised, destroyed or otherwise gone: stop tracking
					_corpses.Remove(c);
					continue;
				}
				if (c.isBeingCarriedBy != null)
				{
					continue; // paused while a villager carries it
				}
				Entry e = _corpses[c];
				e.elapsed++;
				if (e.elapsed >= total)
				{
					if (Decompose(c))
					{
						_corpses.Remove(c);
					}
					continue;
				}
				Stage ns = StageFor(e.elapsed, total);
				if (ns != e.stage)
				{
					e.stage = ns;
					RuinarchPlus.Log?.Info($"Corpse of {c.name} is now {ns}.");
				}
			}
		}

		// Hourly: start tracking every unburied body in the region.
		private static void Discover()
		{
			List<Character> here = GridMap.Instance?.mainRegion?.charactersAtLocation;
			if (here == null)
			{
				return;
			}
			for (int i = 0; i < here.Count; i++)
			{
				Character c = here[i];
				if (IsUnburiedCorpse(c) && !_corpses.ContainsKey(c))
				{
					_corpses[c] = new Entry();
				}
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

		// The remains are gone: cancel anyone still coming to bury them, then remove the
		// body from the map. Returns false (retry next tick) while the player is seizing it.
		private static bool Decompose(Character c)
		{
			if (PlayerManager.Instance?.player != null && PlayerManager.Instance.player.seizeComponent.seizedPOI == c)
			{
				return false;
			}
			c.ForceCancelAllJobsTargetingThisCharacter(JOB_TYPE.BURY);
			if (c.hasMarker)
			{
				c.DestroyMarker();
			}
			RuinarchPlus.Log?.Info($"Corpse of {c.name} has fully decomposed and returned to the earth.");
			return true;
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
