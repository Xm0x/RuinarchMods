using System.Collections.Generic;
using Locations.Settlements;
using RuinarchPlus;
using UnityEngine;
using UtilityScripts;

// Declared in the game's structure namespace so every game-type reference resolves
// exactly as the decompiled source did. This type lives in the MOD assembly, not
// Assembly-CSharp, so the game's reflection factory never finds it by name - the
// Ruinarch.ModContent framework instantiates it through the registered Factory instead.
namespace Inner_Maps.Location_Structures
{
	/// <summary>
	/// A NON-demonic VILLAGE building: a communal pit for the dead of a settlement that has
	/// no Cemetery. Registered as new content via Ruinarch.ModContent (virtual STRUCTURE_TYPE)
	/// and classified as a village structure (see MassGraveFeature), so the STOCK game treats
	/// it like a first-class manmade building. Mirrors the game's own <see cref="Cemetery"/>.
	///
	/// Villagers do the work: the burial reroute (MassGraveBurial) turns every corpse in the
	/// settlement - residents, strangers and creatures - into a normal BURY job that carries
	/// the body here. Hourly, the pit re-issues those jobs for any corpse still lying around,
	/// and only absorbs a nearby corpse directly when nobody has hauled it for
	/// <c>massGraveFallbackHours</c> (e.g. the whole village is dead).
	/// </summary>
	public class MassGrave : ManMadeStructure
	{
		/// <summary>Live instances the hourly tick iterates. Add on build/load, remove on destroy.</summary>
		internal static readonly List<MassGrave> Active = new List<MassGrave>();

		/// <summary>Bodies villagers carried into any pit / bodies the fallback absorbed.
		/// Session totals, read by the RuinarchDebug test harness.</summary>
		public static int HauledTotal { get; private set; }
		public static int AbsorbedTotal { get; private set; }

		// How close (in tiles) an un-hauled corpse must be for the fallback to absorb it.
		private const float FallbackRadius = 12f;

		// Bodies that visually "fill" the mound. The pit keeps accepting bodies forever;
		// this only caps the fill level used by the mound visual.
		private const int MoundCapacity = 30;

		// Hours each nearby, still-unburied corpse has been waiting (fallback timer).
		private readonly Dictionary<Character, int> _waitingHours = new Dictionary<Character, int>();

		/// <summary>Number of bodies laid in this pit (hauled by villagers or absorbed).</summary>
		public int bodyCount { get; private set; }

		/// <summary>0..1 fill level for the mound visual. Caps at full; burial never stops.</summary>
		public float fillRatio => Mathf.Clamp01((float)bodyCount / MoundCapacity);

		public MassGrave(STRUCTURE_TYPE type, Region location)
			: base(type, location)
		{
			base.wallsAreMadeOf = WALL_RESOURCE.Wood;
			Active.Add(this);
		}

		public MassGrave(Region location, SaveDataManMadeStructure data)
			: base(location, data)
		{
			base.wallsAreMadeOf = WALL_RESOURCE.Wood;
			Active.Add(this);
		}

		/// <summary>The live Mass Grave serving <paramref name="settlement"/>, or null.</summary>
		internal static MassGrave FindFor(BaseSettlement settlement)
		{
			for (int i = 0; i < Active.Count; i++)
			{
				MassGrave pit = Active[i];
				if (!pit.hasBeenDestroyed && pit.settlementLocation == settlement)
				{
					return pit;
				}
			}
			return null;
		}

		public override void OnTileDamaged(LocationGridTile tile, int amount, bool isPlayerSource)
		{
			AdjustHP(amount, null, isPlayerSource);
			OnStructureDamaged();
		}

		public override bool DoesTileContributeToDamage(LocationGridTile tile)
		{
			return true;
		}

		public override void OnBuiltNewStructure()
		{
			base.OnBuiltNewStructure();
			// Same as Cemetery: the moment the pit exists, every corpse already lying in the
			// settlement becomes a burial job.
			IssueBuryJobs();
		}

		protected override void AfterStructureDestruction(Character p_responsibleCharacter = null)
		{
			Active.Remove(this);
			_waitingHours.Clear();
			base.AfterStructureDestruction(p_responsibleCharacter);
		}

		/// <summary>A tile inside the pit to lay a body on (BURY target tile).</summary>
		internal LocationGridTile GetBurialTile()
		{
			if (unoccupiedTiles != null && unoccupiedTiles.Count > 0)
			{
				return CollectionUtilities.GetRandomElement(unoccupiedTiles);
			}
			return GetRandomTile();
		}

		/// <summary>Called when a villager's BURY job targeting this pit succeeds.</summary>
		internal void RecordBurial(Character corpse)
		{
			bodyCount++;
			HauledTotal++;
			if (corpse != null)
			{
				_waitingHours.Remove(corpse);
			}
			global::RuinarchPlus.RuinarchPlus.Log?.Info($"Mass Grave: {corpse?.name ?? "a body"} laid in the pit by a villager (bodyCount={bodyCount}).");
		}

		// Every in-game hour. Fully guarded: bad game state degrades to "did nothing".
		internal void OnHourStarted()
		{
			try
			{
				if (hasBeenDestroyed)
				{
					return;
				}
				IssueBuryJobs();
				AbsorbAbandonedCorpses();
			}
			catch
			{
			}
		}

		// Mirror of the game's private SettlementJobTriggerComponent.TryCreateBuryJobs: ask
		// every dead character in the settlement to request burial. The reroute turns each
		// request into a BURY job that targets this pit.
		private void IssueBuryJobs()
		{
			if (!(settlementLocation is NPCSettlement settlement) || settlement.areas == null)
			{
				return;
			}
			List<Character> dead = RuinarchListPool<Character>.Claim();
			for (int i = 0; i < settlement.areas.Count; i++)
			{
				List<Character> here = settlement.areas[i].locationCharacterTracker?.charactersAtLocation;
				if (here == null)
				{
					continue;
				}
				for (int j = 0; j < here.Count; j++)
				{
					if (here[j] != null && here[j].isDead)
					{
						dead.Add(here[j]);
					}
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				try
				{
					dead[i].jobComponent.TriggerBuryMe();
				}
				catch
				{
				}
			}
			RuinarchListPool<Character>.Release(dead);
		}

		// Fallback only: absorb corpses near the pit that no villager has hauled for
		// massGraveFallbackHours (nobody alive to carry them, or unreachable).
		private void AbsorbAbandonedCorpses()
		{
			if (region?.charactersAtLocation == null)
			{
				return;
			}
			LocationGridTile pitReference = GetBurialTile();
			if (pitReference == null)
			{
				return;
			}
			int limit = Mathf.Max(1, RuinarchPlusConfig.Current.massGraveFallbackHours);
			List<Character> toAbsorb = RuinarchListPool<Character>.Claim();
			List<Character> seen = RuinarchListPool<Character>.Claim();
			List<Character> regionCharacters = region.charactersAtLocation;
			for (int i = 0; i < regionCharacters.Count; i++)
			{
				Character c = regionCharacters[i];
				try
				{
					if (!IsLooseCorpse(c, pitReference))
					{
						continue;
					}
					seen.Add(c);
					_waitingHours.TryGetValue(c, out int hours);
					hours++;
					_waitingHours[c] = hours;
					if (hours >= limit)
					{
						toAbsorb.Add(c);
					}
				}
				catch
				{
				}
			}
			// Forget corpses that were hauled, buried or rotted away since last hour.
			List<Character> stale = RuinarchListPool<Character>.Claim();
			foreach (Character c in _waitingHours.Keys)
			{
				if (!seen.Contains(c))
				{
					stale.Add(c);
				}
			}
			for (int i = 0; i < stale.Count; i++)
			{
				_waitingHours.Remove(stale[i]);
			}
			for (int i = 0; i < toAbsorb.Count; i++)
			{
				try
				{
					Absorb(toAbsorb[i]);
				}
				catch
				{
				}
			}
			RuinarchListPool<Character>.Release(stale);
			RuinarchListPool<Character>.Release(seen);
			RuinarchListPool<Character>.Release(toAbsorb);
		}

		private bool IsLooseCorpse(Character c, LocationGridTile pitReference)
		{
			if (c == null || !c.isDead || !c.hasMarker || c.gridTileLocation == null)
			{
				return false;
			}
			// Already buried, being carried, or a villager is already burying it -> leave it.
			if (c.grave != null || c.isBeingCarriedBy != null)
			{
				return false;
			}
			if (c.HasJobTargetingThis(JOB_TYPE.BURY, JOB_TYPE.BURY_IN_ACTIVE_PARTY))
			{
				return false;
			}
			// Never steal a corpse the player is actively seizing (DestroyMarker would throw).
			if (PlayerManager.Instance?.player != null && PlayerManager.Instance.player.seizeComponent.seizedPOI == c)
			{
				return false;
			}
			// A settlement with its own Cemetery buries its people individually.
			if (c.race.IsSapient() && c.gridTileLocation.IsNextToOrPartOfSettlement(out BaseSettlement settlement)
				&& settlement is NPCSettlement npcSettlement && npcSettlement.HasStructure(STRUCTURE_TYPE.CEMETERY))
			{
				return false;
			}
			return c.gridTileLocation.GetDistanceTo(pitReference) <= FallbackRadius;
		}

		// Mirrors the game's own BuryCharacter.AfterBurySuccess disposal: sapient dead get a
		// tombstone inside the pit; animals/monsters are cleared. The litter leaves the map.
		private void Absorb(Character corpse)
		{
			if (corpse.race.IsSapient())
			{
				LocationGridTile pitTile = GetBurialTile();
				if (pitTile != null)
				{
					Tombstone tombstone = new Tombstone();
					tombstone.SetCharacter(corpse);
					AddPOI(tombstone, pitTile);
					corpse.SetGrave(tombstone);
				}
			}
			if (corpse.hasMarker)
			{
				corpse.DestroyMarker();
			}
			_waitingHours.Remove(corpse);
			bodyCount++;
			AbsorbedTotal++;
			global::RuinarchPlus.RuinarchPlus.Log?.Info($"Mass Grave: Absorbed un-hauled {corpse.name} into the pit (bodyCount={bodyCount}).");
		}
	}
}
