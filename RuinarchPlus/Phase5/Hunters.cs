using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Locations.Settlements;

namespace RuinarchPlus.Phase5
{
	/// <summary>
	/// Hunters feed hungry villages (config: <c>huntingEnabled</c>). In the base game the
	/// Hunter is a fighter and the Hunter Lodge only skins; villages never go out for meat.
	/// Now every 6 hours a hungry village (in famine, or a fifth of its villagers starving)
	/// sends up to <c>huntersPerTrip</c> villagers, Hunters first, then other fighters, after
	/// wild animals near the village (not Bears). The hunt is the game's own: the hunting job
	/// predators use (<c>HUNT_PREY</c> with <c>ASSAULT</c>); once the animal is dead the hunter
	/// butchers it (<c>PRODUCE_FOOD</c> with <c>BUTCHER</c>, as the game's food jobs do) and
	/// carries the meat to the village's main storage.
	/// </summary>
	internal static class Hunters
	{
		private const float Range = 60f;

		// Hunters out on a hunt Ruinarch+ sent, their prey, and when they set out.
		private static readonly Dictionary<Character, Character> Out = new Dictionary<Character, Character>();
		private static readonly Dictionary<Character, long> SetOut = new Dictionary<Character, long>();
		// Hunters whose meat is on its way home.
		private static readonly HashSet<Character> Fed = new HashSet<Character>();
		// A hunt is given up after a day.
		private const long GiveUpTicks = 24 * 20;

		internal static bool Enabled => RuinarchPlusConfig.Current.huntingEnabled;

		internal static bool IsHunting(Character c) => c != null && Out.ContainsKey(c);

		internal static bool IsPrey(Character c) => c != null && Out.ContainsValue(c);

		internal static bool IsHungry(NPCSettlement s)
		{
			int starving = Famine.Starving(s, out int villagers);
			return villagers >= 3 && (Famine.IsInFamine(s) || starving * 5 >= villagers);
		}

		private static bool Busy(Character hunter) =>
			hunter.jobQueue.HasJob(JOB_TYPE.HUNT_PREY) || hunter.jobQueue.HasJob(JOB_TYPE.PRODUCE_FOOD) || hunter.jobQueue.HasJob(JOB_TYPE.HAUL);

		/// <summary>
		/// Hourly: a hunter whose prey got away keeps after it (for a day); one whose prey is
		/// dead butchers it; one who is done, or gives up, comes home.
		/// </summary>
		internal static void Upkeep()
		{
			foreach (KeyValuePair<Character, Character> kv in Out.ToList())
			{
				Character hunter = kv.Key, prey = kv.Value;
				if (hunter != null && !hunter.isDead && Busy(hunter))
				{
					continue;
				}
				if (hunter != null && !hunter.isDead && !Fed.Contains(hunter) && prey != null && prey.hasMarker)
				{
					if (prey.isDead)
					{
						if (!prey.HasJobTargetingThis(JOB_TYPE.PRODUCE_FOOD) && Butcher(hunter, prey))
						{
							continue;
						}
					}
					else if (Phase3.MissingPersons.Now - SetOut[hunter] < GiveUpTicks && prey.gridTileLocation != null
						&& hunter.movementComponent.HasPathToEvenIfDiffRegion(prey.gridTileLocation)
						&& hunter.jobQueue.AddJobInQueue(JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.HUNT_PREY, INTERACTION_TYPE.ASSAULT, prey, hunter)))
					{
						continue;
					}
				}
				Out.Remove(hunter);
				SetOut.Remove(hunter);
				if (!Fed.Remove(hunter))
				{
					RuinarchPlus.Log?.Info($"{hunter?.name} came back from hunting empty-handed ({prey?.name} {(prey == null || !prey.hasMarker ? "gone" : prey.isDead ? "dead" : "alive")}).");
				}
			}
		}

		/// <summary>Every 6 hours: hungry villages send hunters.</summary>
		internal static void Check()
		{
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (settlements == null)
			{
				return;
			}
			foreach (NPCSettlement s in settlements.OfType<NPCSettlement>().ToList())
			{
				try
				{
					if (s.locationType == LOCATION_TYPE.VILLAGE && s.owner != null && s.owner.isMajorFaction && s.cityCenter != null && IsHungry(s))
					{
						SendHunters(s);
					}
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Hunting failed for {s?.name}: {e.Message}");
				}
			}
		}

		/// <summary>Sends up to huntersPerTrip villagers of <paramref name="s"/> hunting. Returns how many went.</summary>
		internal static int SendHunters(NPCSettlement s)
		{
			LocationGridTile centre = s.cityCenter.passableTiles.FirstOrDefault() ?? s.cityCenter.tiles.FirstOrDefault();
			if (centre == null)
			{
				return 0;
			}
			List<Character> prey = s.region.charactersAtLocation
				.Where(c => c is Animal a && !(a is Bear) && !a.isDead && a.hasMarker && a.gridTileLocation != null && !a.isTamed && a.faction?.isPlayerFaction != true
					&& a.carryComponent.isBeingCarriedBy == null && !a.HasJobTargetingThis(JOB_TYPE.PRODUCE_FOOD)
					&& a.gridTileLocation.GetDistanceTo(centre) <= Range)
				.OrderBy(a => a.gridTileLocation.GetDistanceTo(centre)).ToList();
			if (prey.Count == 0)
			{
				RuinarchPlus.Log?.Info($"Hunting: no animals within reach of {s.name}.");
				return 0;
			}
			List<Character> hunters = s.residents
				.Where(c => c != null && !c.isDead && c.isNormalCharacter && c.race.IsSapient() && c.hasMarker && c.limiterComponent.canMove && c.limiterComponent.canPerform
					&& c.characterClass.IsCombatant() && (!c.partyComponent.hasParty || !c.partyComponent.currentParty.isActive) && c.carryComponent.isBeingCarriedBy == null && !Out.ContainsKey(c)
					&& !c.jobQueue.HasJob(JOB_TYPE.HUNT_PREY) && !c.traitContainer.HasTrait("Enslaved"))
				.OrderByDescending(c => c.characterClass.className == "Hunter")
				.ToList();
			int sent = 0;
			int max = Math.Max(0, RuinarchPlusConfig.Current.huntersPerTrip);
			foreach (Character hunter in hunters)
			{
				if (sent >= max || prey.Count == 0)
				{
					break;
				}
				Character animal = prey.FirstOrDefault(a => hunter.movementComponent.HasPathToEvenIfDiffRegion(a.gridTileLocation));
				if (animal == null)
				{
					continue;
				}
				GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.HUNT_PREY, INTERACTION_TYPE.ASSAULT, animal, hunter);
				if (!hunter.jobQueue.AddJobInQueue(job))
				{
					continue;
				}
				prey.Remove(animal);
				Out[hunter] = animal;
				SetOut[hunter] = Phase3.MissingPersons.Now;
				sent++;
				Phase2.Curfew.Note("{0} went hunting to feed {1}.", hunter, s);
			}
			if (sent == 0)
			{
				RuinarchPlus.Log?.Info($"Hunting: nobody in {s.name} could go ({prey.Count} animal(s) in range, {hunters.Count} free fighter(s)).");
			}
			return sent;
		}

		/// <summary>An animal died: if a Ruinarch+ hunter was after it, they butcher it.</summary>
		internal static void PreyDied(Character animal)
		{
			Character hunter = Out.FirstOrDefault(kv => kv.Value == animal).Key;
			if (hunter != null && !hunter.isDead && animal.hasMarker)
			{
				Butcher(hunter, animal);
			}
		}

		private static bool Butcher(Character hunter, Character carcass)
		{
			GoapPlanJob butcher = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.PRODUCE_FOOD, INTERACTION_TYPE.BUTCHER, carcass, hunter);
			butcher.SetCancelOnDeath(state: false);
			if (!hunter.jobQueue.AddJobInQueue(butcher))
			{
				return false;
			}
			// A burial queued before the kill was the hunter's (the Mass Grave takes carcasses)
			// would carry the meat off to the pit.
			foreach (JobQueueItem bury in carcass.allJobsTargetingThis.Where(j => j.jobType == JOB_TYPE.BURY || j.jobType == JOB_TYPE.BURY_IN_ACTIVE_PARTY).ToList())
			{
				bury.ForceCancelJob();
			}
			RuinarchPlus.Log?.Info($"{hunter.name} is butchering {carcass.name}.");
			return true;
		}

		/// <summary>A Ruinarch+ hunter butchered their kill: carry the meat home.</summary>
		internal static void CarryHome(Character hunter, LocationGridTile at)
		{
			if (!Out.ContainsKey(hunter) || !(hunter.homeSettlement is NPCSettlement home) || home.mainStorage == null)
			{
				return;
			}
			FoodPile meat = at == null ? null : new[] { at }.Concat(at.neighbourList).Select(t => t?.tileObjectComponent.objHere).OfType<FoodPile>().FirstOrDefault();
			if (meat == null)
			{
				RuinarchPlus.Log?.Info($"{hunter.name} butchered their kill but no meat was found near {at?.localPlace}.");
				return;
			}
			// A hunter with a workplace (a Butcher's Shop, a farm...) already has the game's own
			// haul of the meat there (Butcher.ProduceMats); that is inside the village too.
			if (hunter.jobQueue.HasJob(JOB_TYPE.HAUL))
			{
				Fed.Add(hunter);
				RuinarchPlus.Log?.Info($"{hunter.name} is carrying {meat.resourceInPile} meat home to {home.name} ({hunter.structureComponent.workPlaceStructure?.name ?? "the storage"}).");
				return;
			}
			hunter.jobComponent.TryCreateHaulJob(meat, home.mainStorage, out JobQueueItem haul);
			if (haul != null && hunter.jobQueue.AddJobInQueue(haul))
			{
				Fed.Add(hunter);
				RuinarchPlus.Log?.Info($"{hunter.name} is carrying {meat.resourceInPile} meat home to {home.name}.");
			}
		}
	}

	// Animals are Summons, and Summon.Death replaces Character.Death rather than calling it.
	[HarmonyPatch(typeof(Summon), nameof(Summon.Death))]
	internal static class Hunters_PreyDied
	{
		private static void Postfix(Character __instance)
		{
			if (!Hunters.Enabled || !(__instance is Animal))
			{
				return;
			}
			try
			{
				Hunters.PreyDied(__instance);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Hunting (kill) failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(Butcher), nameof(Butcher.AfterTransformSuccess))]
	internal static class Hunters_Butchered
	{
		// The pile is made on the carcass's tile, which the node still names.
		private static void Prefix(ActualGoapNode goapNode, out LocationGridTile __state)
		{
			__state = goapNode?.poiTarget?.gridTileLocation;
		}

		private static void Postfix(ActualGoapNode goapNode, LocationGridTile __state)
		{
			if (!Hunters.Enabled || goapNode?.actor == null || !Hunters.IsHunting(goapNode.actor))
			{
				return;
			}
			try
			{
				Hunters.CarryHome(goapNode.actor, __state);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Hunting (carry home) failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class Hunters_Tick
	{
		private static void Postfix(GameManager __instance)
		{
			// Hunters' upkeep every in-game hour (20 ticks); hungry villages send more every 6.
			int tick = __instance.Today().tick;
			if (!Hunters.Enabled || tick % 20 != 0)
			{
				return;
			}
			try
			{
				Hunters.Upkeep();
				if (tick % 120 == 0)
				{
					Hunters.Check();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Hunting failed: " + e.Message);
			}
		}
	}
}
