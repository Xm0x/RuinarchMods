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
	/// the body here; creature carcasses are also fetched from the ring of map areas around
	/// the village. A body laid in the pit leaves no tombstone: it is gone, like a body that
	/// has fully decomposed, and the pit only keeps the count. Hourly, the pit re-issues those jobs for any corpse still lying around,
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

		// Hours each nearby, still-unburied corpse has been waiting (fallback timer).
		private readonly Dictionary<Character, int> _waitingHours = new Dictionary<Character, int>();

		/// <summary>Number of bodies laid in this pit (hauled by villagers or absorbed).</summary>
		public int bodyCount { get; private set; }

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
				RemoveTombstone(corpse.grave);
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
				StripCemeteryPropsOnce();
				IssueBuryJobs();
				AbsorbAbandonedCorpses();
			}
			catch
			{
			}
		}

		// A mass burial is anonymous: no gravestone. Removing the tombstone without a corpse
		// respawn clears the character's grave and marker for good (Tombstone.OnDestroyPOI),
		// exactly what a fully decomposed body gets.
		private void RemoveTombstone(Tombstone tombstone)
		{
			if (tombstone == null)
			{
				return;
			}
			tombstone.SetRespawnCorpseOnDestroy(false);
			LocationStructure at = tombstone.gridTileLocation?.structure;
			(at ?? this).RemovePOI(tombstone);
		}

		private bool _propsStripped;

		// Pits built before props were skipped (MassGrave_NoCemeteryProps) still carry the
		// Cemetery's pre-placed objects, restored from the save. Remove them once per session.
		// Tombstones are never pre-placed: they are the graves of the bodies laid here.
		private void StripCemeteryPropsOnce()
		{
			if (_propsStripped)
			{
				return;
			}
			_propsStripped = true;
			int removed = 0;
			foreach (LocationGridTile tile in tiles)
			{
				TileObject obj = tile.tileObjectComponent.objHere;
				if (obj is Tombstone oldGrave)
				{
					// Laid here before burials stopped leaving gravestones.
					RemoveTombstone(oldGrave);
					removed++;
				}
				else if (obj != null && obj.isPreplaced && !(obj is Tombstone) && obj.tileObjectType != TILE_OBJECT_TYPE.STRUCTURE_TILE_OBJECT && RemovePOI(obj))
				{
					removed++;
				}
			}
			if (removed > 0)
			{
				global::RuinarchPlus.RuinarchPlus.Log?.Info($"Mass Grave in {settlementLocation?.name}: cleared {removed} leftover prop(s) and gravestone(s).");
			}
		}

		// Mirror of the game's private SettlementJobTriggerComponent.TryCreateBuryJobs: ask
		// every dead character in the settlement and its surroundings to request burial. The
		// reroute turns each request into a BURY job that targets this pit. Reads the
		// region-wide list: an area's own list only gains a character when its marker moves
		// between areas, so a body that never moved there is missing from it.
		private void IssueBuryJobs()
		{
			if (!(settlementLocation is NPCSettlement settlement) || settlement.areas == null || region?.charactersAtLocation == null)
			{
				return;
			}
			HashSet<Area> catchment = new HashSet<Area>(CatchmentAreas(settlement));
			List<Character> dead = RuinarchListPool<Character>.Claim();
			List<Character> all = region.charactersAtLocation;
			for (int j = 0; j < all.Count; j++)
			{
				Character c = all[j];
				if (c != null && c.isDead && c.hasMarker && c.gridTileLocation != null && catchment.Contains(c.gridTileLocation.area))
				{
					dead.Add(c);
				}
			}
			for (int i = 0; i < dead.Count; i++)
			{
				try
				{
					Character corpse = dead[i];
					if (corpse.gridTileLocation != null && corpse.gridTileLocation.IsNextToOrPartOfSettlement(settlement))
					{
						// In the village: the game's own burial request, rerouted here.
						corpse.jobComponent.TriggerBuryMe();
					}
					else if (!global::RuinarchPlus.Phase2.MassGraveBurial.HasProperGraveFor(settlement, corpse))
					{
						// A carcass or an outsider out in the surroundings: vanilla leaves these.
						global::RuinarchPlus.Phase2.MassGraveBurial.QueuePitJob(settlement, corpse, this);
					}
				}
				catch
				{
				}
			}
			RuinarchListPool<Character>.Release(dead);
		}

		/// <summary>The village's own areas plus the ring of areas around them.</summary>
		internal static List<Area> CatchmentAreas(NPCSettlement settlement)
		{
			List<Area> areas = new List<Area>(settlement.areas);
			for (int i = 0; i < settlement.areas.Count; i++)
			{
				List<Area> neighbours = settlement.areas[i].neighbourComponent?.neighbours;
				if (neighbours == null)
				{
					continue;
				}
				for (int j = 0; j < neighbours.Count; j++)
				{
					if (!areas.Contains(neighbours[j]))
					{
						areas.Add(neighbours[j]);
					}
				}
			}
			return areas;
		}

		/// <summary>Is a body lying where this village's pit collects from?</summary>
		internal static bool InCatchment(NPCSettlement settlement, LocationGridTile tile)
		{
			return tile != null && (tile.IsNextToOrPartOfSettlement(settlement) || CatchmentAreas(settlement).Contains(tile.area));
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
			// A settlement with its own Cemetery buries its own people individually.
			if (c.gridTileLocation.IsNextToOrPartOfSettlement(out BaseSettlement settlement)
				&& settlement is NPCSettlement npcSettlement && global::RuinarchPlus.Phase2.MassGraveBurial.HasProperGraveFor(npcSettlement, c))
			{
				return false;
			}
			return c.gridTileLocation.GetDistanceTo(pitReference) <= FallbackRadius;
		}

		// The body goes into the pit and leaves the map; like a villager burial, no gravestone.
		private void Absorb(Character corpse)
		{
			corpse.ForceCancelAllJobsTargetingThisCharacter(JOB_TYPE.BURY);
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
