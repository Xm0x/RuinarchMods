using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UtilityScripts;

namespace RuinarchPlus
{
	// PHASE 2 - Death, Decay & Disease: corpse-borne plague.  (default ON)
	//
	// Wires the decay system (CorpseDecay) into the game's existing plague. Once per
	// in-game hour, UNBURIED bodies at the rotting/skeletal stage lying inside a settlement
	// structure sicken the living present, scaled by how many such corpses share that
	// structure - "3 bodies rotting in a house -> outbreak fast." Open Wilderness is skipped
	// so a lone body in the wild can't infect a whole region. Buried bodies (graves in a
	// Cemetery or Mass Grave) are never infectious: CorpseDecay stops tracking them.
	//
	// Mirrors the game's own Transmission model: Quarantined targets get the same 75%
	// resistance, and infection goes through interruptComponent.TriggerInterrupt(Plagued)
	// exactly like Plague.Transmission.Transmission.Infect, so all plague listeners fire.
	//
	// Requires corpseDecayEnabled (it reads the decay stage). Switch off via config.json.
	public static class CorpseDisease
	{
		private const int TicksPerHour = 20; // 480 ticks/day, hourly checks
		private static int _tickAccum;

		internal static void Tick()
		{
			if (!RuinarchPlusConfig.Current.corpseDiseaseEnabled)
			{
				return;
			}
			_tickAccum++;
			if (_tickAccum < TicksPerHour)
			{
				return;
			}
			_tickAccum = 0;

			List<Character> rotting = CorpseDecay.GetRottingCorpses();
			if (rotting.Count == 0)
			{
				return;
			}

			// Count infectious corpses per settlement structure.
			Dictionary<LocationStructure, int> byStructure = new Dictionary<LocationStructure, int>();
			for (int i = 0; i < rotting.Count; i++)
			{
				Character corpse = rotting[i];
				LocationStructure s = corpse?.gridTileLocation?.structure;
				if (s == null || s is Wilderness)
				{
					continue; // open ground doesn't seed a settlement outbreak
				}
				int cur;
				byStructure[s] = byStructure.TryGetValue(s, out cur) ? cur + 1 : 1;
			}

			int perCorpse = RuinarchPlusConfig.Current.corpseDiseaseChancePerCorpse;
			foreach (KeyValuePair<LocationStructure, int> kv in byStructure)
			{
				int chance = kv.Value * perCorpse;
				if (chance <= 0)
				{
					continue;
				}
				List<Character> here = kv.Key.charactersHere;
				if (here == null || here.Count == 0)
				{
					continue;
				}
				// snapshot: infecting mutates traits/interrupts
				List<Character> snapshot = new List<Character>(here);
				for (int i = 0; i < snapshot.Count; i++)
				{
					Character c = snapshot[i];
					if (!Eligible(c))
					{
						continue;
					}
					int adj = chance;
					if (c.traitContainer.HasTrait("Quarantined"))
					{
						adj -= Mathf.FloorToInt((float)adj * 0.75f); // same cut the game applies
					}
					if (adj > 0 && GameUtilities.RollChance(adj))
					{
						Infect(c);
					}
				}
			}
		}

		private static bool Eligible(Character c)
		{
			return c != null && !c.isDead && !c.traitContainer.HasTrait("Plagued");
		}

		private static void Infect(Character c)
		{
			// Same path Plague.Transmission uses for character targets; the interrupt
			// itself re-checks plague-immunity/lifespan, so ineligible targets no-op.
			c.interruptComponent.TriggerInterrupt(INTERRUPT.Plagued, c);
			RuinarchPlus.Log?.Info($"{c.name} contracted plague from a rotting corpse nearby.");
		}

		[HarmonyPatch(typeof(GameManager), "TickEnded")]
		public static class GameManager_TickEnded_Disease
		{
			private static void Postfix()
			{
				Tick();
			}
		}
	}
}
