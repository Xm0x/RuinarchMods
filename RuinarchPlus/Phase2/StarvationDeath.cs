using System.Collections.Generic;
using HarmonyLib;

namespace RuinarchPlus
{
	// PHASE 2 - Death, Decay & Disease: starvation death.  (default OFF)
	//
	// Recon gap: CharacterNeedsComponent tracks isStarving (fullness < 20) and the game
	// even adds a "Starving" trait, but nothing ever kills a villager for prolonged
	// starvation. This wires it: a sapient villager who stays Starving for
	// starvationDeathHours continuous in-game hours dies ("Starvation"). Feeds Phase 5
	// famine/unrest. Monsters/summons/undead don't eat and are exempt.
	//
	// Hourly scan of CharacterManager.allCharacters; a starve-hours counter per villager
	// that resets the moment they stop starving (got fed).
	public static class StarvationDeath
	{
		private const int TicksPerHour = 20;
		private static int _tickAccum;
		private static readonly Dictionary<Character, int> _starveHours = new Dictionary<Character, int>();

		internal static void Tick()
		{
			if (!RuinarchPlusConfig.Current.starvationDeathEnabled)
			{
				return;
			}
			_tickAccum++;
			if (_tickAccum < TicksPerHour)
			{
				return;
			}
			_tickAccum = 0;

			int threshold = RuinarchPlusConfig.Current.starvationDeathHours;
			if (threshold <= 0)
			{
				return;
			}

			List<Character> all = CharacterManager.Instance.allCharacters;
			List<Character> toKill = null;
			for (int i = 0; i < all.Count; i++)
			{
				Character c = all[i];
				if (!IsStarvingVillager(c))
				{
					if (_starveHours.Count > 0)
					{
						_starveHours.Remove(c); // fed / no longer eligible -> reset
					}
					continue;
				}
				int hours;
				hours = _starveHours.TryGetValue(c, out hours) ? hours + 1 : 1;
				_starveHours[c] = hours;
				if (hours >= threshold)
				{
					if (toKill == null)
					{
						toKill = new List<Character>();
					}
					toKill.Add(c);
				}
			}

			if (toKill != null)
			{
				for (int i = 0; i < toKill.Count; i++)
				{
					Character c = toKill[i];
					_starveHours.Remove(c);
					if (c != null && !c.isDead)
					{
						RuinarchPlus.Log?.Info($"{c.name} starved to death.");
						c.Death("Starvation");
					}
				}
			}
		}

		// Only living, sapient, non-summon villagers who actually eat can starve to death.
		private static bool IsStarvingVillager(Character c)
		{
			if (c == null || c.isDead || c is Summon)
			{
				return false;
			}
			if (!c.race.IsSapient())
			{
				return false;
			}
			return c.needsComponent != null && c.needsComponent.isStarving;
		}

		[HarmonyPatch(typeof(GameManager), "TickEnded")]
		public static class GameManager_TickEnded_Starvation
		{
			private static void Postfix()
			{
				Tick();
			}
		}
	}
}
