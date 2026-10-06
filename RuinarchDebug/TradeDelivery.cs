using HarmonyLib;
using Inner_Maps.Location_Structures;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private ResourcePile _tradeCargo;
		private LocationStructure _tradeDestination;
		private bool _tradeArrived;

		[HarmonyPatch(typeof(DepositResourcePile), nameof(DepositResourcePile.AfterDepositSuccess))]
		private static class TradeDelivery
		{
			[HarmonyPriority(Priority.First)]
			private static void Postfix(ActualGoapNode goapNode)
			{
				AutoTest test = _running;
				if (test?._tradeCargo == null || goapNode.poiTarget != test._tradeCargo) return;
				// Observe the native deposit before the mod announces it and exchanges news.
				test._tradeArrived = test._tradeCargo.isBeingCarriedBy == null
					&& test._tradeCargo.gridTileLocation?.structure == test._tradeDestination;
			}
		}
	}
}
