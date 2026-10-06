using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private static readonly List<string> TableCannibalLogs = new List<string>();
		private static string TableCannibalActorID;

		// Observe real submitted logs before the stock pool releases their fillers.
		private static void CaptureTableCannibalLog(global::Log __instance)
		{
			if (__instance.key != "became_cannibal" || !__instance.fillers.Any(f =>
				f.identifier == LOG_IDENTIFIER.ACTIVE_CHARACTER && f.objPersistentID == TableCannibalActorID)) return;
			string food = __instance.fillers.FirstOrDefault(f => f.identifier == LOG_IDENTIFIER.STRING_1)?.value;
			TableCannibalLogs.Add(__instance.category + "|" + __instance.file + "|" + food + "|" +
				string.Join(",", __instance.tags.Select(t => t.ToString()).OrderBy(t => t)));
		}

		private IEnumerator TableCannibalSuite()
		{
			var observer = new Harmony("ruinarch.debug.table-cannibal-observer");
			Character consumer = null;
			var objects = new List<TileObject>();
			try
			{
				consumer = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: FactionManager.Instance.vagrantFaction, randomizeTraits: false);
				TableCannibalActorID = consumer.persistentID;
				observer.Patch(AccessTools.Method(typeof(global::Log), nameof(global::Log.AddLogToDatabase)),
					prefix: new HarmonyMethod(AccessTools.Method(typeof(AutoTest), nameof(CaptureTableCannibalLog))));
				Eat eat = (Eat)InteractionManager.Instance.goapActionData[INTERACTION_TYPE.EAT];
				var table = new Table();
				objects.Add(table);
				var human = new HumanMeat();
				objects.Add(human);
				var elf = new ElfMeat();
				objects.Add(elf);
				var node = new ActualGoapNode();
				foreach (FoodPile meat in new FoodPile[] { human, elf })
				{
					consumer.traitContainer.RemoveTrait(consumer, "Cannibal");
					TableCannibalLogs.Clear();
					node.SetActionData(eat, consumer, meat, null, 10);
					eat.AfterEatSuccess(node);
					bool directCannibal = consumer.traitContainer.HasTrait("Cannibal");
					string directLog = TableCannibalLogs.Count == 1 ? TableCannibalLogs[0] : null;
					Check(meat.name + " directly grants Cannibal with one stock alert", () =>
						(directCannibal && directLog != null, $"Cannibal={directCannibal}, alerts={TableCannibalLogs.Count}"));
					consumer.traitContainer.RemoveTrait(consumer, "Cannibal");
					TableCannibalLogs.Clear();
					table.SetFood(meat.specificProvidedResource, 20);
					node.SetActionData(eat, consumer, table, null, 10);
					eat.AfterEatSuccess(node);
					Check(meat.name + " at a table grants Cannibal and the same stock food alert", () =>
						(consumer.traitContainer.HasTrait("Cannibal") && TableCannibalLogs.Count == 1 && TableCannibalLogs[0] == directLog,
						$"Cannibal={consumer.traitContainer.HasTrait("Cannibal")}, alerts={TableCannibalLogs.Count}, expected={directLog}, actual={string.Join(";", TableCannibalLogs)}"));
					// Explicitly seed the already-Cannibal case even while the regression is red.
					consumer.traitContainer.AddTrait(consumer, "Cannibal");
					TableCannibalLogs.Clear();
					eat.AfterEatSuccess(node);
					Check(meat.name + " at a table does not repeat the already-Cannibal alert", () =>
						(consumer.traitContainer.HasTrait("Cannibal") && TableCannibalLogs.Count == 0, $"alerts={TableCannibalLogs.Count}"));
				}
				var foods = new[] { CONCRETE_RESOURCES.Corn, CONCRETE_RESOURCES.Potato, CONCRETE_RESOURCES.Pineapple,
					CONCRETE_RESOURCES.Iceberry, CONCRETE_RESOURCES.Fish, CONCRETE_RESOURCES.Animal_Meat };
				var buffs = new[] { "Corn Fed", "Potato Fed", "Pineapple Fed", "Iceberry Fed", "Fish Fed", "Animal Fed" };
				for (int i = 0; i < foods.Length; i++)
				{
					consumer.traitContainer.RemoveTrait(consumer, "Cannibal");
					consumer.traitContainer.RemoveTrait(consumer, buffs[i]);
					TableCannibalLogs.Clear();
					table.SetFood(foods[i], 20);
					node.SetActionData(eat, consumer, table, null, 10);
					eat.AfterEatSuccess(node);
					string buff = buffs[i];
					Check(foods[i] + " table meal preserves its stock buff without Cannibal", () =>
						(!consumer.traitContainer.HasTrait("Cannibal") && consumer.traitContainer.HasTrait(buff) && TableCannibalLogs.Count == 0,
						$"buff={consumer.traitContainer.HasTrait(buff)}, Cannibal={consumer.traitContainer.HasTrait("Cannibal")}, alerts={TableCannibalLogs.Count}"));
				}
				// A real minion exercises the exact stock isNotSummonAndDemon exclusion.
				new Minion(consumer, keepData: true);
				foreach (FoodPile meat in new FoodPile[] { human, elf })
				{
					consumer.traitContainer.RemoveTrait(consumer, "Cannibal");
					TableCannibalLogs.Clear();
					node.SetActionData(eat, consumer, meat, null, 10);
					eat.AfterEatSuccess(node);
					table.SetFood(meat.specificProvidedResource, 20);
					node.SetActionData(eat, consumer, table, null, 10);
					eat.AfterEatSuccess(node);
					Check(meat.name + " direct and table meals honor the minion exclusion", () =>
						(!consumer.isNotSummonAndDemon && !consumer.traitContainer.HasTrait("Cannibal") && TableCannibalLogs.Count == 0,
						$"eligible={consumer.isNotSummonAndDemon}, Cannibal={consumer.traitContainer.HasTrait("Cannibal")}, alerts={TableCannibalLogs.Count}"));
				}
				node.Reset();
			}
			finally
			{
				observer.UnpatchAll("ruinarch.debug.table-cannibal-observer");
				TableCannibalActorID = null;
				TableCannibalLogs.Clear();
				// These real objects were never placed: remove constructor subscriptions/traits,
				// rather than call Table.OnDestroyPOI, which requires a placed map visual.
				foreach (TileObject obj in objects)
				{
					AccessTools.Method(typeof(TileObject), "UnsubscribeListeners").Invoke(obj, null);
					obj.traitContainer.RemoveAllTraitsAndStatuses(obj);
					obj.traitContainer.CleanUp();
				}
				if (consumer != null)
				{
					consumer.faction?.LeaveFaction(consumer);
					CharacterManager.Instance.RemoveCharacter(consumer);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(consumer);
				}
			}
			yield break;
		}
	}
}
