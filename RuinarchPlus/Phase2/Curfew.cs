using System;
using HarmonyLib;
using Locations.Settlements.Settlement_Events;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// Settlement curfew during a plague (config: <c>curfewEnabled</c>).
	///
	/// When plague breaks out in a village, the game's <see cref="PlaguedEvent"/> has the
	/// ruler pick a response from their traits: Slay (evil/ruthless), Do_Nothing (coward/lazy),
	/// or a measured one, Quarantine (with a Hospice) or Exile. A ruler who takes a measured
	/// response now also imposes a curfew for as long as the event lasts: residents give up
	/// their free time (visiting, taverns, wandering - the mingling that spreads plague) and
	/// go home instead. Work continues, because work is how villagers take the settlement's
	/// jobs: plague care, burials, food, construction. The ruler and the faction leader are
	/// exempt. Needs (eating, sleeping) and combat are driven outside the behaviour loop, so a
	/// curfew never starves anyone or stops them defending themselves.
	///
	/// The curfew is derived, never stored: a village is under curfew exactly while it has an
	/// active PlaguedEvent whose ruler decision is Quarantine or Exile. Saves are untouched
	/// and a save made with the mod loads cleanly without it.
	/// </summary>
	internal static class Curfew
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.curfewEnabled;

		internal static bool IsCurfewResponse(PLAGUE_EVENT_RESPONSE response)
		{
			return response == PLAGUE_EVENT_RESPONSE.Quarantine || response == PLAGUE_EVENT_RESPONSE.Exile;
		}

		/// <summary>Is <paramref name="settlement"/> under curfew right now?</summary>
		internal static bool IsUnderCurfew(NPCSettlement settlement)
		{
			if (!Enabled || settlement?.eventManager == null)
			{
				return false;
			}
			PlaguedEvent plague = settlement.eventManager.GetActiveEvent<PlaguedEvent>();
			return plague != null && plague.hasLeaderMadeADecision && IsCurfewResponse(plague.rulerDecision);
		}

		/// <summary>Does the curfew bind this character (a resident of a village under curfew)?</summary>
		internal static bool Binds(Character c)
		{
			return c != null && !c.isDead && c.isNormalCharacter && !c.isSettlementRuler && !c.isFactionLeader
				&& c.homeSettlement is NPCSettlement home && home.locationType == LOCATION_TYPE.VILLAGE
				&& IsUnderCurfew(home);
		}

		internal static bool IsHome(Character c)
		{
			return c.homeStructure != null && !c.homeStructure.hasBeenDestroyed ? c.isAtHomeStructure : c.IsAtHome();
		}

		// A notification in the game's event log, like the plague event's own announcements.
		// (Log fillers, which make names clickable, are internal to the game assembly; the
		// text is plain.) With notify false it goes to the log only, not the feed.
		internal static void Announce(string text, bool notify = true)
		{
			try
			{
				global::Log log = GameManager.CreateNewLogUsingNewLocalization(GameManager.Instance.Today(), "Settlement Event", "EventAlerts_Table", "Plagued started", LOG_TAG.Major);
				log.SetLogText(text);
				log.AddLogToDatabase();
				if (notify)
				{
					PlayerManager.Instance.player.ShowNotificationFromPlayer(log, releaseLogAfter: true);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Notification failed: " + e.Message);
			}
			RuinarchPlus.Log?.Info(text);
		}
	}

	// Free time under curfew: go home, and once home stay in.
	[HarmonyPatch(typeof(BehaviourComponent), nameof(BehaviourComponent.RunBehaviour))]
	internal static class Curfew_RunBehaviour
	{
		private static bool Prefix(BehaviourComponent __instance, ref string __result)
		{
			try
			{
				Character c = __instance.owner;
				if (!Curfew.Binds(c)
					|| c.dailyScheduleComponent.schedule.GetScheduleType(GameManager.Instance.currentTick) != DAILY_SCHEDULE.Free_Time)
				{
					return true;
				}
				if (Curfew.IsHome(c))
				{
					__result = "Curfew: staying in.";
				}
				else
				{
					c.jobComponent.PlanReturnHome(JOB_TYPE.IDLE_RETURN_HOME);
					__result = "Curfew: going home.";
				}
				return false;
			}
			catch
			{
				return true;
			}
		}
	}

	// The ruler's decision just took effect: announce a curfew for a measured response.
	[HarmonyPatch(typeof(PlaguedEvent), "ExecuteEffectsOfLeaderResponseToPlague")]
	internal static class Curfew_Imposed
	{
		private static void Postfix(PLAGUE_EVENT_RESPONSE p_response, NPCSettlement p_settlement)
		{
			if (Curfew.Enabled && Curfew.IsCurfewResponse(p_response) && p_settlement != null)
			{
				Curfew.Announce($"{p_settlement.ruler?.name ?? "The ruler"} has placed {p_settlement.name} under curfew: residents must stay home in their free time until the plague has passed.");
			}
		}
	}

	// The decision changed away from a measured response: the curfew ends.
	[HarmonyPatch(typeof(PlaguedEvent), "RevertEffectsOfLeaderPreviousResponseToPlague")]
	internal static class Curfew_Revoked
	{
		private static void Postfix(PLAGUE_EVENT_RESPONSE p_response, NPCSettlement p_settlement)
		{
			if (Curfew.Enabled && Curfew.IsCurfewResponse(p_response) && p_settlement != null)
			{
				Curfew.Announce($"The curfew in {p_settlement.name} has been lifted.");
			}
		}
	}

	// The plague event ended: the curfew ends with it.
	[HarmonyPatch(typeof(PlaguedEvent), nameof(PlaguedEvent.DeactivateEvent))]
	internal static class Curfew_Ended
	{
		private static void Postfix(PlaguedEvent __instance, NPCSettlement p_settlement)
		{
			if (Curfew.Enabled && __instance.hasLeaderMadeADecision && Curfew.IsCurfewResponse(__instance.rulerDecision) && p_settlement != null)
			{
				Curfew.Announce($"The plague in {p_settlement.name} has passed; the curfew is over.");
			}
		}
	}
}
