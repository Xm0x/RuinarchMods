using System.Collections.Generic;
using Locations.Settlements;

// Declared in the game's structure namespace, like MassGrave: this type lives in the MOD
// assembly, and the Ruinarch.ModContent framework instantiates it through the registered
// factory.
namespace Inner_Maps.Location_Structures
{
	/// <summary>
	/// The seat of a Town or City (see <c>SettlementTiers</c>). A village that has grown
	/// large enough builds one through the game's own construction pipeline, borrowing the
	/// Tavern's prefab (look, footprint, build cost). While it stands, the settlement is a
	/// Town (or a City); destroyed, the settlement is a village again until it rebuilds.
	/// A plain village building otherwise: it hires no worker and is damaged and destroyed
	/// like any other, with the Tavern's hit points.
	/// </summary>
	public class TownHall : ManMadeStructure
	{
		internal static readonly List<TownHall> Active = new List<TownHall>();

		public TownHall(STRUCTURE_TYPE type, Region location)
			: base(type, location)
		{
			SetMaxHPAndReset(8000);
			Active.Add(this);
		}

		public TownHall(Region location, SaveDataManMadeStructure data)
			: base(location, data)
		{
			SetMaxHP(8000);
			Active.Add(this);
		}

		/// <summary>The standing Town Hall of <paramref name="settlement"/>, or null.</summary>
		internal static TownHall FindFor(BaseSettlement settlement)
		{
			for (int i = 0; i < Active.Count; i++)
			{
				TownHall hall = Active[i];
				if (!hall.hasBeenDestroyed && hall.settlementLocation == settlement)
				{
					return hall;
				}
			}
			return null;
		}

		protected override void AfterStructureDestruction(Character p_responsibleCharacter = null)
		{
			NPCSettlement settlement = settlementLocation as NPCSettlement;
			Active.Remove(this);
			base.AfterStructureDestruction(p_responsibleCharacter);
			global::RuinarchPlus.Phase5.SettlementTiers.OnTownHallLost(settlement);
		}
	}
}
