using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RuinarchPerformance
{
	/// <summary>
	/// JobQueueItem.Initialize subscribes OnCrimeRemovedFromDatabase, but Reset unsubscribes
	/// every other signal and not this one. Jobs come from a pool, so the list gains an entry
	/// each time a job is created, about 4,500 a minute on a large map at 4x speed, for as long
	/// as the game runs. A reset job's handler does nothing (its ForceCancelJob just returns
	/// true), so Reset now unsubscribes it as well. A job reset by the crime broadcast itself
	/// is unsubscribed once that broadcast ends: removing entries from a list the game is
	/// still walking would make it call a listener twice.
	/// </summary>
	internal static class JobCrimeListeners
	{
		private const string CrimeKey = "CrimeRemovedFromDatabase";
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private static readonly Dictionary<Type, MethodInfo> Handlers = new Dictionary<Type, MethodInfo>();
		private static readonly List<JobQueueItem> Deferred = new List<JobQueueItem>();
		private static int _broadcasting;

		private static void Unsubscribe(JobQueueItem job)
		{
			MethodInfo handler;
			lock (Handlers)
			{
				Type type = job.GetType();
				if (!Handlers.TryGetValue(type, out handler))
				{
					// The most derived override, which is what the game subscribed.
					handler = type.GetMethod("OnCrimeRemovedFromDatabase", Instance, null, new[] { typeof(CrimeData) }, null);
					Handlers[type] = handler;
				}
			}
			var listener = (SignalHandler<CrimeData>.SignalListener)Delegate.CreateDelegate(typeof(SignalHandler<CrimeData>.SignalListener), job, handler);
			SignalHandler<CrimeData>.RemoveListener(CrimeKey, listener, false);
		}

		[HarmonyPatch(typeof(JobQueueItem), nameof(JobQueueItem.Reset))]
		internal static class Patch_Reset
		{
			private static void Postfix(JobQueueItem __instance)
			{
				if (_broadcasting > 0)
				{
					Deferred.Add(__instance);
				}
				else
				{
					Unsubscribe(__instance);
				}
			}
		}

		// The game's only CrimeRemovedFromDatabase broadcast.
		[HarmonyPatch(typeof(CrimeDatabase), nameof(CrimeDatabase.RemoveCrime))]
		internal static class Patch_RemoveCrime
		{
			private static void Prefix()
			{
				_broadcasting++;
			}

			private static Exception Finalizer(Exception __exception)
			{
				if (--_broadcasting == 0 && Deferred.Count > 0)
				{
					// Once per deferred reset: a job reused from the pool during the broadcast
					// subscribed again and keeps that one entry.
					foreach (JobQueueItem job in Deferred) Unsubscribe(job);
					Deferred.Clear();
				}
				return __exception;
			}
		}
	}
}
