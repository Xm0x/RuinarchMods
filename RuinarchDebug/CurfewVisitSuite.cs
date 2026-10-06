using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements.Settlement_Events;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator CurfewVisitSuite()
		{
			var fixtures = new List<Character>();
			var village = Villages().First(v => v.owner != null && v.cityCenter != null
				&& v.eventManager.GetActiveEvent<PlaguedEvent>() == null
				&& v.allStructures.Any(s => s is Dwelling && s.passableTiles.Count > 0));
			var destination = Villages().First(v => v != village && v.cityCenter != null);
			var home = village.allStructures.First(s => s is Dwelling && s.passableTiles.Count > 0);
			var plague = new PlaguedEvent(village);
			object enabled = PlusBridge.Config("curfewEnabled");
			try
			{
				Character resident = StockActor(fixtures, faction: village.owner);
				resident.MigrateHomeTo(village, home);
				CharacterManager.Instance.Teleport(resident, home.passableTiles.First());
				AccessTools.Field(typeof(PlaguedEvent), "_rulerDecision").SetValue(plague, PLAGUE_EVENT_RESPONSE.Quarantine);
				AccessTools.Field(typeof(PlaguedEvent), "_hasLeaderMadeADecision").SetValue(plague, true);
				village.eventManager.activeEvents.Add(plague);

				void QueueVisit()
				{
					resident.jobQueue.CancelAllJobs();
					resident.behaviourComponent.ClearOutVisitVillageBehaviour();
					resident.behaviourComponent.VisitVillage(resident, destination);
					resident.jobComponent.CreateGoToJob(JOB_TYPE.VISIT_DIFFERENT_VILLAGE,
						destination.cityCenter.passableTiles.First(), out JobQueueItem visit);
					if (visit == null || !resident.jobQueue.AddJobInQueue(visit))
						throw new System.InvalidOperationException("Native village visit could not be queued");
				}

				PlusBridge.SetConfig("curfewEnabled", false);
				QueueVisit();
				yield return WaitGameHours(0.5f, () => resident.currentJob?.jobType == JOB_TYPE.VISIT_DIFFERENT_VILLAGE);
				resident.TickStarted();
				Check("disabled curfew preserves a resident's queued village visit", () =>
					(resident.jobQueue.HasJob(JOB_TYPE.VISIT_DIFFERENT_VILLAGE)
					&& resident.currentJob?.jobType == JOB_TYPE.VISIT_DIFFERENT_VILLAGE
					&& resident.behaviourComponent.targetVisitVillage == destination, $"native visit active: {resident.currentJob?.jobType}"));

				PlusBridge.SetConfig("curfewEnabled", true);
				resident.TickStarted();
				Check("curfew cancels a village visit already queued before it started", () =>
					(!resident.jobQueue.HasJob(JOB_TYPE.VISIT_DIFFERENT_VILLAGE), "native visit removed from both job queues"));
				Check("curfew clears the outgoing village visit behavior", () =>
					(resident.behaviourComponent.targetVisitVillage == null, "old destination is no longer selected"));

				AccessTools.Field(typeof(PlaguedEvent), "_rulerDecision").SetValue(plague, PLAGUE_EVENT_RESPONSE.Do_Nothing);
				QueueVisit();
				resident.TickStarted();
				Check("a plague without a curfew preserves queued village visits", () =>
					(resident.jobQueue.HasJob(JOB_TYPE.VISIT_DIFFERENT_VILLAGE), "Do Nothing response does not bind residents"));

				AccessTools.Field(typeof(PlaguedEvent), "_rulerDecision").SetValue(plague, PLAGUE_EVENT_RESPONSE.Quarantine);
				QueueVisit();
				resident.jobComponent.CreateGoToJob(JOB_TYPE.HAUL, home.passableTiles.Last(), out JobQueueItem haul);
				if (haul == null || !resident.jobQueue.AddJobInQueue(haul))
					throw new System.InvalidOperationException("Native work movement could not be queued");
				resident.TickStarted();
				Check("curfew cancels the visit without cancelling queued work", () =>
					(!resident.jobQueue.HasJob(JOB_TYPE.VISIT_DIFFERENT_VILLAGE)
					&& resident.jobQueue.HasJob(JOB_TYPE.HAUL), "work movement remains queued"));
				yield return null;
			}
			finally
			{
				village.eventManager.activeEvents.Remove(plague);
				if (enabled != null) PlusBridge.SetConfig("curfewEnabled", enabled);
				foreach (Character actor in fixtures)
				{
					actor.jobQueue.CancelAllJobs();
					actor.behaviourComponent.ClearOutVisitVillageBehaviour();
					if (actor.stateComponent.currentState != null) actor.stateComponent.ExitCurrentState();
					actor.DestroyMarker(removeFromGame: false);
					actor.faction?.LeaveFaction(actor);
					CharacterManager.Instance.RemoveCharacter(actor);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(actor);
				}
			}
		}
	}
}
