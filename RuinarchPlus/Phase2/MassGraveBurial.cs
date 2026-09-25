using System;
using Goap.Job_Checkers;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using UnityEngine;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// Burial reroute for the Mass Grave (config: <c>massGraveBurialEnabled</c>).
	///
	/// Vanilla <c>CharacterJobTriggerComponent.TriggerBuryMe</c> targets
	/// <c>CULT_TEMPLE ?? CEMETERY ?? wilderness</c>: a village with no graveyard scatters
	/// tombstones into the wilderness, and it never buries animals or non-residents at all.
	/// With the reroute:
	/// <list type="bullet">
	/// <item>a village WITH a Cemetery/Cult Temple buries its sapient dead exactly as vanilla;</item>
	/// <item>a village WITHOUT one leaves corpses where they fell (no scattered tombstones)
	/// until it has a Mass Grave;</item>
	/// <item>once it has a Mass Grave, villagers carry every corpse in the settlement into it -
	/// residents, strangers and creatures alike (creatures are disposed, not given tombstones,
	/// exactly as vanilla <c>BuryCharacter.AfterBurySuccess</c> does for non-sapients).</item>
	/// </list>
	/// </summary>
	internal static class MassGraveBurial
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.massGraveBurialEnabled;

		/// <summary>The Mass Grave a BURY job targets, or null for vanilla jobs.</summary>
		internal static MassGrave PitOf(JobQueueItem job)
		{
			OtherData[] data = (job as GoapPlanJob)?.GetOtherDataFor(INTERACTION_TYPE.BURY_CHARACTER);
			return data != null && data.Length > 0 ? data[0]?.obj as MassGrave : null;
		}

		internal static bool HasGraveyard(NPCSettlement settlement)
		{
			return settlement.HasStructure(STRUCTURE_TYPE.CEMETERY) || settlement.HasStructure(STRUCTURE_TYPE.CULT_TEMPLE);
		}

		/// <summary>Did this person live in <paramref name="settlement"/> (at death, or still)?</summary>
		internal static bool IsResidentOf(Character corpse, NPCSettlement settlement)
		{
			return corpse.homeSettlement == settlement || corpse.previousCharacterDataComponent?.homeSettlementOnDeath == settlement;
		}

		/// <summary>Does the village's own graveyard (Cemetery / Cult Temple) take this corpse?
		/// Only its own people: outsiders, monsters and creatures go to the Mass Grave.</summary>
		internal static bool HasProperGraveFor(NPCSettlement settlement, Character corpse)
		{
			return corpse.race.IsSapient() && IsResidentOf(corpse, settlement) && HasGraveyard(settlement);
		}

		/// <summary>Queue a settlement BURY job that carries <paramref name="corpse"/> to the pit
		/// (no-op if excluded, already queued, or a real graveyard should take it).</summary>
		internal static void QueuePitJob(NPCSettlement settlement, Character corpse, MassGrave pit)
		{
			if (pit == null || corpse == null || HasProperGraveFor(settlement, corpse)
				|| IsExcluded(corpse.jobComponent, corpse, settlement) || settlement.HasJob(JOB_TYPE.BURY, corpse))
			{
				return;
			}
			GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.BURY, INTERACTION_TYPE.BURY_CHARACTER, corpse, settlement);
			job.SetCanTakeThisJobChecker(JobManager.Can_Take_Bury_Job);
			job.AddOtherData(INTERACTION_TYPE.BURY_CHARACTER, new object[1] { pit });
			job.SetStillApplicableChecker(JobManager.Bury_Settlement_Applicability);
			settlement.AddToAvailableJobs(job);
		}

		/// <summary>Corpses a Mass Grave must never take (mirrors vanilla's exclusions).</summary>
		internal static bool IsExcluded(CharacterJobTriggerComponent component, Character corpse, NPCSettlement settlement)
		{
			return corpse.minion != null
				|| corpse.grave != null
				|| !corpse.hasMarker
				|| component.IsCharacterGhost(corpse)
				|| corpse.traitContainer.HasTrait("Mummified")
				// A hunter's kill is meat for the village, not a body for the pit.
				|| Phase5.Hunters.IsPrey(corpse) || corpse.HasJobTargetingThis(JOB_TYPE.PRODUCE_FOOD)
				// Hunters skin skinnable carcasses; leave those to the Hunter Lodge as vanilla does.
				|| (corpse.race.IsSkinnable() && settlement.HasStructureOfTypeThatIsAssigned(STRUCTURE_TYPE.HUNTER_LODGE));
		}
	}

	[HarmonyPatch(typeof(CharacterJobTriggerComponent), nameof(CharacterJobTriggerComponent.TriggerBuryMe))]
	internal static class MassGrave_TriggerBuryMe
	{
		private static bool Prefix(CharacterJobTriggerComponent __instance)
		{
			try
			{
				if (!MassGraveBurial.Enabled)
				{
					return true;
				}
				Character corpse = __instance.owner;
				if (corpse == null || !corpse.isDead || corpse.gridTileLocation == null
					|| !corpse.gridTileLocation.IsNextToOrPartOfSettlement(out BaseSettlement settlement)
					|| !(settlement is NPCSettlement npcSettlement))
				{
					return true;
				}
				// The village's own people go to its Cemetery / Cult Temple: vanilla path.
				if (MassGraveBurial.HasProperGraveFor(npcSettlement, corpse))
				{
					return true;
				}
				MassGrave pit = MassGrave.FindFor(npcSettlement);
				if (pit == null && corpse.race.IsSapient() && MassGraveBurial.HasGraveyard(npcSettlement))
				{
					// An outsider, but no pit to put them in: the Cemetery takes them (vanilla).
					return true;
				}
				if (pit == null)
				{
					// No graveyard and no pit. Vanilla would scatter a tombstone into the
					// wilderness; instead the corpse lies where it fell (and rots) until the
					// settlement has a Mass Grave. Creatures/strangers: vanilla ignores them too.
					return false;
				}
				if (MassGraveBurial.IsExcluded(__instance, corpse, npcSettlement)
					|| npcSettlement.HasJob(JOB_TYPE.BURY, corpse))
				{
					return false;
				}
				MassGraveBurial.QueuePitJob(npcSettlement, corpse, pit);
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Mass Grave burial reroute failed; using vanilla: " + e);
				return true;
			}
		}
	}

	// The second vanilla scatter path: a villager who SEES a body lying outside village tiles
	// (CharacterTrait reaction) queues a personal BURY job targeting their home's Cult Temple
	// or Cemetery, else the wilderness. When the home village has neither, carry the body to
	// the village's Mass Grave instead, or leave it lying (it rots) if there is no pit yet.
	// With a real graveyard at home, the village's own dead go there (vanilla); an outsider
	// goes to the Mass Grave if there is one. Vagrants keep vanilla behaviour.
	[HarmonyPatch(typeof(CharacterJobTriggerComponent), nameof(CharacterJobTriggerComponent.TriggerPersonalOutsideVillageBuryJob))]
	internal static class MassGrave_PersonalBury
	{
		private static bool Prefix(CharacterJobTriggerComponent __instance, Character targetCharacter)
		{
			try
			{
				if (!MassGraveBurial.Enabled)
				{
					return true;
				}
				Character owner = __instance.owner;
				if (!(owner?.homeSettlement is NPCSettlement home) || targetCharacter == null)
				{
					return true;
				}
				if (MassGraveBurial.HasGraveyard(home)
					&& (MassGraveBurial.IsResidentOf(targetCharacter, home) || MassGrave.FindFor(home) == null))
				{
					return true;
				}
				// From here vanilla would bury the body in the wilderness.
				if (targetCharacter == null || owner.gridTileLocation == null
					|| owner.jobQueue.HasJob(JOB_TYPE.BURY, targetCharacter)
					|| targetCharacter.HasJobTargetingThis(JOB_TYPE.BURY, JOB_TYPE.BURY_IN_ACTIVE_PARTY)
					|| __instance.IsCharacterGhost(owner) || owner.traitComponent.IsCharacterTargetOfObsession(targetCharacter))
				{
					return false;
				}
				MassGrave pit = MassGrave.FindFor(home);
				LocationGridTile tile = pit?.GetBurialTile();
				if (tile == null || !owner.movementComponent.HasPathToEvenIfDiffRegion(tile))
				{
					return false;
				}
				GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.BURY, INTERACTION_TYPE.BURY_CHARACTER, targetCharacter, owner);
				job.AddOtherData(INTERACTION_TYPE.BURY_CHARACTER, new object[1] { pit });
				job.SetStillApplicableChecker(JobManager.Bury_Applicability);
				owner.jobQueue.AddJobInQueue(job);
				return false;
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Mass Grave personal burial reroute failed; using vanilla: " + e);
				return true;
			}
		}
	}

	// Vanilla cancels a settlement BURY job unless the village has a Cemetery or the corpse
	// was a resident. A Mass Grave job stays valid while the pit stands and the corpse still
	// lies in (or next to) the settlement.
	[HarmonyPatch(typeof(BurySettlementApplicabilityChecker), nameof(BurySettlementApplicabilityChecker.IsJobStillApplicable))]
	internal static class MassGrave_BuryApplicability
	{
		private static bool Prefix(JobQueueItem job, ref bool __result)
		{
			try
			{
				MassGrave pit = MassGraveBurial.PitOf(job);
				if (pit == null)
				{
					return true;
				}
				Character corpse = (job as GoapPlanJob).targetPOI as Character;
				NPCSettlement settlement = job.originalOwner as NPCSettlement;
				__result = !pit.hasBeenDestroyed
					&& corpse != null && settlement != null
					&& !MassGraveBurial.IsExcluded(corpse.jobComponent, corpse, settlement)
					&& MassGrave.InCatchment(settlement, corpse.gridTileLocation);
				return false;
			}
			catch
			{
				return true;
			}
		}
	}

	// BuryCharacter only knows how to pick a destination tile inside WILDERNESS or CEMETERY
	// structures and returns null for anything else; give it a tile inside the pit.
	[HarmonyPatch(typeof(BuryCharacter), nameof(BuryCharacter.GetTargetTileToGoTo))]
	internal static class MassGrave_BuryTargetTile
	{
		private static void Postfix(BuryCharacter __instance, ActualGoapNode goapNode, ref LocationGridTile __result)
		{
			try
			{
				if (__result == null && __instance.GetTargetStructure(goapNode) is MassGrave pit)
				{
					__result = pit.GetBurialTile();
				}
			}
			catch
			{
			}
		}
	}

	// Count bodies villagers lay in the pit (drives the fill level).
	[HarmonyPatch(typeof(BuryCharacter), nameof(BuryCharacter.AfterBurySuccess))]
	internal static class MassGrave_BurySuccess
	{
		private static void Prefix(BuryCharacter __instance, ActualGoapNode goapNode, out MassGrave __state)
		{
			__state = null;
			try
			{
				__state = __instance.GetTargetStructure(goapNode) as MassGrave;
			}
			catch
			{
			}
		}

		private static void Postfix(ActualGoapNode goapNode, MassGrave __state)
		{
			if (__state == null)
			{
				return;
			}
			try
			{
				__state.RecordBurial(goapNode.poiTarget as Character);
			}
			catch
			{
			}
		}
	}
}
