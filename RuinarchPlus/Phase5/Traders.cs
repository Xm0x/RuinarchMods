using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;

namespace RuinarchPlus.Phase5
{
	/// <summary>
	/// Traders (config: <c>tradeEnabled</c>). Nothing in the base game moves goods between
	/// villages. Now once a day, at 8 in the morning, a village with food to spare (more than
	/// 20 per villager plus <c>tradeAmount</c>) sends one trader, a Merchant if it has one,
	/// with <c>tradeAmount</c> food to the village that needs it most: hungry (in famine, or a
	/// fifth starving) or short of food (under 10 per villager). The trader carries the food
	/// with the game's own haul job (<c>HAUL</c> / <c>DEPOSIT_RESOURCE_PILE</c>) into that
	/// village's main storage, and then goes home.
	/// - Only between villages whose factions are not hostile (none between enemies).
	/// - Not to or from a village under curfew (Phase2/ClosedBorders).
	/// - Traders carry news (Phase 3): arriving, they tell the village's people every demonic
	///   building they know of, and hear what that village knows (they carry it home).
	/// A trip under way is not saved: after a load the food still arrives (the haul job is the
	/// game's), without the announcement or the news.
	/// </summary>
	internal static class Traders
	{
		private sealed class Trip
		{
			internal NPCSettlement From;
			internal NPCSettlement To;
			internal ResourcePile Goods;
			internal int Amount;
			internal int Resumed;
		}

		private static readonly Dictionary<Character, Trip> Trips = new Dictionary<Character, Trip>();

		internal static bool Enabled => RuinarchPlusConfig.Current.tradeEnabled;

		private static List<Character> Villagers(NPCSettlement s) =>
			s.residents.Where(r => r != null && !r.isDead && r.isNormalCharacter && r.race.IsSapient()).ToList();

		private static int Food(NPCSettlement s) => s.GetNumberOfFoodInWholeSettlement();

		/// <summary>Food beyond what the village keeps for itself, or 0.</summary>
		internal static int Spare(NPCSettlement s)
		{
			return Math.Max(0, Food(s) - Villagers(s).Count * 20);
		}

		internal static bool Needs(NPCSettlement s)
		{
			int villagers = Villagers(s).Count;
			return villagers >= 1 && (Hunters.IsHungry(s) || Food(s) < villagers * 10);
		}

		private static bool Open(NPCSettlement s) =>
			s != null && s.locationType == LOCATION_TYPE.VILLAGE && s.owner != null && s.owner.isMajorFaction && s.mainStorage != null
			&& !s.mainStorage.hasBeenDestroyed && !Phase2.Curfew.IsUnderCurfew(s);

		internal static bool MayTrade(NPCSettlement from, NPCSettlement to) =>
			from != to && Open(from) && Open(to) && (from.owner == to.owner || !from.owner.IsHostileWith(to.owner));

		internal static void DailyCheck()
		{
			List<NPCSettlement> villages = GridMap.Instance?.mainRegion?.settlementsInRegion?.OfType<NPCSettlement>().Where(Open).ToList();
			if (villages == null)
			{
				return;
			}
			int amount = Math.Max(1, RuinarchPlusConfig.Current.tradeAmount);
			foreach (NPCSettlement from in villages)
			{
				try
				{
					if (Spare(from) < amount || Needs(from) || Trips.Values.Any(t => t.From == from))
					{
						continue;
					}
					NPCSettlement to = villages.Where(v => MayTrade(from, v) && Needs(v) && !Trips.Values.Any(t => t.To == v))
						.OrderBy(v => (float)Food(v) / Math.Max(1, Villagers(v).Count)).FirstOrDefault();
					if (to != null)
					{
						Send(from, to, amount);
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Trade from {from?.name} failed: {e.Message}");
				}
			}
		}

		/// <summary>
		/// Sends a trader from <paramref name="from"/> with up to <paramref name="amount"/> food
		/// to <paramref name="to"/>. Returns the trader, or null if nobody or nothing could go.
		/// </summary>
		internal static Character Send(NPCSettlement from, NPCSettlement to, int amount)
		{
			if (!MayTrade(from, to))
			{
				return null;
			}
			LocationGridTile destination = to.mainStorage.passableTiles.FirstOrDefault();
			Character trader = Villagers(from)
				.Where(c => c != from.ruler && !c.isFactionLeader && c.hasMarker && c.limiterComponent.canMove && c.limiterComponent.canPerform
					&& !c.partyComponent.hasParty && c.carryComponent.isBeingCarriedBy == null && !c.needsComponent.isStarving
					&& !c.traitContainer.HasTrait("Enslaved") && !c.jobQueue.HasJob(JOB_TYPE.HAUL) && !Trips.ContainsKey(c) && !Hunters.IsHunting(c)
					&& (destination == null || c.movementComponent.HasPathToEvenIfDiffRegion(destination)))
				.OrderByDescending(c => c.characterClass.className == "Merchant").FirstOrDefault();
			ResourcePile goods = trader == null ? null : TakeFood(from, amount);
			if (goods == null)
			{
				return null;
			}
			// The goods are the trader's until delivered: the game's haulers and stockpile
			// combiners only take piles nobody owns, so nobody else carries them off.
			goods.SetCharacterOwner(trader);
			trader.jobComponent.TryCreateHaulJob(goods, to.mainStorage, out JobQueueItem haul);
			if (haul == null || !trader.jobQueue.AddJobInQueue(haul))
			{
				goods.SetCharacterOwner(null);
				return null;
			}
			Trips[trader] = new Trip { From = from, To = to, Goods = goods, Amount = goods.resourceInPile };
			Phase2.Curfew.Note($"{{0}} set out from {{1}} with {goods.resourceInPile} food for {{2}}.", trader, from, to);
			return trader;
		}

		// A pile of about `amount` food in the village's main storage: a whole pile if one is
		// small enough, else split off the largest.
		private static ResourcePile TakeFood(NPCSettlement s, int amount)
		{
			List<ResourcePile> piles = s.mainStorage.pointsOfInterest.OfType<ResourcePile>()
				.Where(p => p.providedResource == RESOURCE.FOOD && p.resourceInPile > 0 && p.gridTileLocation != null && p.isBeingCarriedBy == null && p.characterOwner == null
					&& p.mapObjectState == MAP_OBJECT_STATE.BUILT && !p.HasJobTargetingThis(JOB_TYPE.HAUL))
				.OrderByDescending(p => p.resourceInPile).ToList();
			ResourcePile whole = piles.FirstOrDefault(p => p.resourceInPile <= amount && p.resourceInPile * 2 >= amount);
			if (whole != null)
			{
				return whole;
			}
			ResourcePile largest = piles.FirstOrDefault(p => p.resourceInPile > amount);
			LocationGridTile spot = s.mainStorage.GetRandomUnoccupiedTile();
			if (largest == null || spot == null)
			{
				return null;
			}
			ResourcePile split = InnerMapManager.Instance.CreateNewTileObject<ResourcePile>(largest.tileObjectType);
			split.SetResourceInPile(amount);
			if (!s.mainStorage.AddPOI(split, spot))
			{
				return null;
			}
			largest.AdjustResourceInPile(-amount);
			return split;
		}

		internal static bool IsTrading(Character c) => c != null && Trips.ContainsKey(c);

		internal static void Delivered(Character trader, ResourcePile goods)
		{
			if (!Trips.TryGetValue(trader, out Trip trip) || trip.Goods != goods)
			{
				return;
			}
			Trips.Remove(trader);
			goods?.SetCharacterOwner(null);
			Phase2.Curfew.Announce($"{{0}} of {{1}} brought {trip.Amount} food to {{2}}.", trader, trip.From, trip.To);
			Phase3.Knowledge.Exchange(trader, trip.To);
		}

		/// <summary>
		/// Hourly: a trader who put the goods down (called away by something more urgent)
		/// picks them up again, up to 3 times; a trip whose trader died, or whose goods are
		/// gone, ends.
		/// </summary>
		internal static void HourlyCheck()
		{
			foreach (KeyValuePair<Character, Trip> kv in Trips.ToList())
			{
				Character c = kv.Key;
				Trip trip = kv.Value;
				ResourcePile goods = trip.Goods;
				if (c != null && !c.isDead && (c.jobQueue.HasJob(JOB_TYPE.HAUL) || c.carryComponent.carriedPOI == goods))
				{
					continue;
				}
				if (c != null && !c.isDead && goods != null && goods.gridTileLocation != null && goods.isBeingCarriedBy == null && trip.Resumed < 3
					&& MayTrade(trip.From, trip.To))
				{
					c.jobComponent.TryCreateHaulJob(goods, trip.To.mainStorage, out JobQueueItem haul);
					if (haul != null && c.jobQueue.AddJobInQueue(haul))
					{
						trip.Resumed++;
						continue;
					}
				}
				Trips.Remove(c);
				goods?.SetCharacterOwner(null);
				RuinarchPlus.Log?.Info($"The trip of {c?.name} from {trip.From.name} to {trip.To.name} ended before the food arrived"
					+ $" (trader dead={c?.isDead}; food {goods?.resourceInPile} at {goods?.gridTileLocation?.structure?.name ?? "nowhere"} carriedBy={goods?.isBeingCarriedBy?.name ?? "-"}; resumed {trip.Resumed} time(s)).");
			}
		}
	}

	[HarmonyPatch(typeof(DepositResourcePile), nameof(DepositResourcePile.AfterDepositSuccess))]
	internal static class Traders_Delivered
	{
		private static void Postfix(ActualGoapNode goapNode)
		{
			if (!Traders.Enabled || goapNode?.actor == null || !Traders.IsTrading(goapNode.actor))
			{
				return;
			}
			try
			{
				Traders.Delivered(goapNode.actor, goapNode.poiTarget as ResourcePile);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Trade (delivery) failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class Traders_Tick
	{
		private static void Postfix(GameManager __instance)
		{
			int tick = __instance.Today().tick;
			if (!Traders.Enabled || tick % 20 != 0)
			{
				return;
			}
			try
			{
				Traders.HourlyCheck();
				if (tick == 8 * 20)
				{
					Traders.DailyCheck();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Trade failed: " + e.Message);
			}
		}
	}
}
