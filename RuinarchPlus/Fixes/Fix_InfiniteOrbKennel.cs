using HarmonyLib;

namespace RuinarchPlus
{
	// EXPLOIT: Kennel "Drain Spirit" granted a chaos orb BEFORE applying the drain
	// damage (BeingDrained.DrainPerTick), so an immortal monster - a hibernating golem
	// carries "Indestructible" - yielded an orb every tick while never losing HP or
	// dying. Fix: skip the drain tick entirely when the target cannot be damaged, so no
	// orb is produced. Mortal monsters drain (and yield orbs) exactly as before.
	[HarmonyPatch(typeof(Traits.BeingDrained), "DrainPerTick")]
	internal static class Fix_InfiniteOrbKennel
	{
		// Return false to skip the vanilla tick (no orb, no no-op damage).
		private static bool Prefix(Character p_character)
		{
			return p_character == null || p_character.CanBeDamaged();
		}
	}
}
