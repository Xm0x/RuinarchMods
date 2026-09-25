using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Traits;

namespace RuinarchPlus.Phase3
{
	/// <summary>
	/// Gossip carries places (config: <c>gossipChance</c>, part of <c>knowledgeEnabled</c>).
	///
	/// When two villagers meet (one sees the other), the one who remembers the player's
	/// buildings talks, of what the listener's village does not know yet:
	/// - to a neighbour (same village), they pass on news they carry and have not yet
	///   brought home, so it survives if they die on the way. What the village already knows
	///   is not retold: newcomers are not taught old news;
	/// - to someone of their faction from another village, they pass on news they carry, and
	///   each other building they remember with a chance of <c>gossipChance</c> percent;
	/// - to someone of another faction that is not hostile to theirs, each building they
	///   remember with a chance of <c>gossipChance</c> percent. Enemies don't talk.
	/// A listener carries what they heard like a witness (<see cref="Knowledge.Hear"/>): their
	/// village knows it once they are home, and a faction that learns of the demons this way
	/// becomes aware of them. Each pair talks at most once a day.
	/// </summary>
	internal static class Gossip
	{
		private const long TicksPerDay = 480;

		// "tellerId|listenerId" -> the day they last talked.
		private static readonly Dictionary<string, long> LastTalk = new Dictionary<string, long>();

		internal static void Meet(Character teller, Character listener)
		{
			int chance = RuinarchPlusConfig.Current.gossipChance;
			if (!Knowledge.Enabled || chance <= 0 || !CanTalk(teller) || !CanTalk(listener) || teller == listener)
			{
				return;
			}
			bool kin = teller.faction == listener.faction;
			if (!kin && teller.faction.IsHostileWith(listener.faction))
			{
				return;
			}
			HashSet<LocationStructure> news = Knowledge.NewsOf(teller);
			if (news.Count == 0)
			{
				return;
			}
			long day = MissingPersons.Now / TicksPerDay;
			string key = teller.persistentID + "|" + listener.persistentID;
			if (LastTalk.TryGetValue(key, out long last) && last == day)
			{
				return;
			}
			LastTalk[key] = day;
			if (LastTalk.Count > 4096)
			{
				LastTalk.Clear();
			}
			foreach (LocationStructure s in news)
			{
				if (Knowledge.Remembers(listener, s) || Knowledge.VillageKnows(listener.homeSettlement as NPCSettlement, s))
				{
					continue;
				}
				// Kin share what they carry; beyond that, neighbours don't retell what their
				// village knows, and everyone else passes on only some of what they remember.
				bool neighbours = kin && listener.homeSettlement == teller.homeSettlement;
				if ((kin && Knowledge.Carries(teller, s)) || (!neighbours && UnityEngine.Random.Range(0, 100) < chance))
				{
					Knowledge.Hear(listener, s, teller);
				}
			}
		}

		private static bool CanTalk(Character c)
		{
			return c != null && !c.isDead && c.faction != null && c.faction.isMajorNonPlayer && c.race.IsSapient() && c.isNormalCharacter
				&& !c.isAlliedWithPlayer && c.limiterComponent.canWitness && c.limiterComponent.canPerform;
		}
	}

	// A character sighting (ReactionComponent.ReactTo runs CharacterTrait.OnSeePOI for every
	// character that comes into view) is a meeting.
	[HarmonyPatch(typeof(CharacterTrait), nameof(CharacterTrait.OnSeePOI))]
	internal static class Gossip_Meet
	{
		private static void Prefix(IPointOfInterest targetPOI, Character characterThatWillDoJob)
		{
			if (!(targetPOI is Character other))
			{
				return;
			}
			try
			{
				Gossip.Meet(characterThatWillDoJob, other);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Gossip failed: " + e.Message);
			}
		}
	}
}
