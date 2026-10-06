using HarmonyLib;
using Logs;
using UtilityScripts;

namespace RuinarchPlus
{
	// Table meals represent their food by resource type, not a HumanMeat/ElfMeat POI.
	// Keep stock meal effects intact and add the omitted direct-meat trait/alert.
	[HarmonyPatch(typeof(Eat), nameof(Eat.AfterEatSuccess))]
	internal static class Fix_TableCannibal
	{
		private static readonly System.Action<Log, ILogFiller, string, LOG_IDENTIFIER, bool, bool> AddFiller =
			AccessTools.MethodDelegate<System.Action<Log, ILogFiller, string, LOG_IDENTIFIER, bool, bool>>(
				AccessTools.Method(typeof(Log), "AddToFillers", new[] { typeof(ILogFiller), typeof(string), typeof(LOG_IDENTIFIER), typeof(bool), typeof(bool) }));

		private static void Postfix(ActualGoapNode goapNode)
		{
			if (!(goapNode.poiTarget is Table table)) return;
			TILE_OBJECT_TYPE meatType;
			switch (table.lastAddedFoodType)
			{
				case CONCRETE_RESOURCES.Human_Meat:
					meatType = TILE_OBJECT_TYPE.HUMAN_MEAT;
					break;
				case CONCRETE_RESOURCES.Elf_Meat:
					meatType = TILE_OBJECT_TYPE.ELF_MEAT;
					break;
				default:
					return;
			}
			Character actor = goapNode.actor;
			if (actor.traitContainer.HasTrait("Cannibal") || !actor.isNotSummonAndDemon) return;
			actor.traitContainer.AddTrait(actor, "Cannibal");
			Log log = GameManager.CreateNewLogUsingNewLocalization(GameManager.Instance.Today(),
				"Character", "CharacterAlerts_Table", "became_cannibal",
				LOG_TAG.Life_Changes, LOG_TAG.Needs, LOG_TAG.Crimes, goapNode);
			AddFiller(log, actor, actor.name, LOG_IDENTIFIER.ACTIVE_CHARACTER, true, false);
			// Match TileObject.GenerateDisplayName, including the stock localization fallback.
			string meatName = meatType.LocalizedName();
			if (string.IsNullOrEmpty(meatName)) meatName = meatType.ToStringEnumWithSpace();
			AddFiller(log, null, meatName, LOG_IDENTIFIER.STRING_1, true, false);
			log.AddLogToDatabase(releaseLogAfter: true);
		}
	}
}
