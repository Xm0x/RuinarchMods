using System;
using HarmonyLib;

namespace RuinarchPlus
{
	[HarmonyPatch(typeof(FactionManager), nameof(FactionManager.RevalidateFactionCrimes))]
	internal static class Fix_LegalizedCrimes
	{
		private static readonly Action<FactionCrimeComponent, Character> RemoveWanted = AccessTools.MethodDelegate<Action<FactionCrimeComponent, Character>>(AccessTools.Method(typeof(FactionCrimeComponent), "RemoveWantedCharacter"));

		private static void Postfix(Faction faction)
		{
			var wanted = faction.crimeComponent.wantedCharacters;
			for (int i = wanted.Count - 1; i >= 0; i--)
			{
				var character = wanted[i];
				var component = character.crimeComponent;
				bool changed = false;
				for (int j = component.activeCrimes.Count - 1; j >= 0; j--)
				{
					var crime = component.activeCrimes[j];
					if (!crime.IsWantedBy(faction) || faction.GetCrimeSeverity(character, crime.target, crime.crimeType).IsConsideredACrime()) continue;
					crime.RemoveFactionThatConsidersWanted(faction);
					if (crime.factionsThatConsidersWanted.Count == 0) component.RemoveCrime(crime);
					changed = true;
				}
				if (!changed) continue;
				if (!component.IsWantedBy(faction)) RemoveWanted(faction.crimeComponent, character);
			}
		}
	}
}
