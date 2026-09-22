using System.Collections.Generic;
using Locations.Settlements;
using UnityEngine;
using UtilityScripts;

// Declared in the game's structure namespace so every game-type reference resolves
// exactly as the decompiled source did. This type lives in the MOD assembly, not
// Assembly-CSharp, so the game's reflection factory never finds it by name - the
// Ruinarch.ModContent framework instantiates it through the registered Factory instead.
namespace Inner_Maps.Location_Structures
{
	/// <summary>
	/// A demonic buildable pit that clears corpse-litter. Registered as new content via
	/// Ruinarch.ModContent (virtual STRUCTURE_TYPE), so it runs against the STOCK game.
	/// Driven hourly by a mod-side Harmony postfix on GameManager.TickStarted (see
	/// RuinarchPlus.Phase2.MassGraveFeature) rather than the game's internal Messenger bus.
	/// </summary>
	public class MassGrave : DemonicStructure
	{
		/// <summary>Live instances the hourly tick iterates. Add on build/load, remove on destroy.</summary>
		internal static readonly List<MassGrave> Active = new List<MassGrave>();

		// How close (in tiles) a corpse must be to the pit for it to be collected.
		private const float CollectionRadius = 12f;

		// Bodies that visually "fill" the mound. The pit keeps clearing litter forever;
		// this only caps how large the Phase-D mound sprite grows.
		private const int MoundCapacity = 30;

		// Number of corpses this pit has consumed - drives the mound/fill visual (Phase D).
		public int bodyCount { get; private set; }

		// 0..1 fill level for the Phase-D mound visual. Caps at full; consumption never stops.
		public float fillRatio => Mathf.Clamp01((float)bodyCount / MoundCapacity);

		public MassGrave(STRUCTURE_TYPE type, Region location)
			: base(type, location)
		{
			SetMaxHPAndReset(1000);
			Active.Add(this);
		}

		public MassGrave(Region location, SaveDataDemonicStructure data)
			: base(location, data)
		{
			Active.Add(this);
		}

		public override void SetStructureObject(LocationStructureObject structureObj)
		{
			base.SetStructureObject(structureObj);
			Vector3 position = structureObj.transform.position;
			position.x -= 0.5f;
			position.y -= 0.5f;
			worldPosition = position;
		}

		protected override void DestroyStructure(Character p_responsibleCharacter = null, bool isPlayerSource = false, bool shouldBeCleanedUp = true)
		{
			Active.Remove(this);
			base.DestroyStructure(p_responsibleCharacter, isPlayerSource, shouldBeCleanedUp);
		}

		// Every in-game hour: pull nearby unburied corpses into the pit. Fully guarded -
		// any bad game state degrades to "collected nothing", never a crash.
		internal void OnHourStarted()
		{
			try
			{
				if (hasBeenDestroyed || region?.charactersAtLocation == null)
				{
					return;
				}
				LocationGridTile pitReference = GetPlacementTile();
				if (pitReference == null)
				{
					return;
				}
				List<Character> toCollect = RuinarchListPool<Character>.Claim();
				List<Character> regionCharacters = region.charactersAtLocation;
				for (int i = 0; i < regionCharacters.Count; i++)
				{
					Character c = regionCharacters[i];
					try
					{
						if (IsCollectableCorpse(c, pitReference))
						{
							toCollect.Add(c);
						}
					}
					catch
					{
					}
				}
				for (int i = 0; i < toCollect.Count; i++)
				{
					try
					{
						ConsumeCorpse(toCollect[i]);
					}
					catch
					{
					}
				}
				RuinarchListPool<Character>.Release(toCollect);
			}
			catch
			{
			}
		}

		private bool IsCollectableCorpse(Character c, LocationGridTile pitReference)
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
			// Don't interfere with normal village burial: a corpse a settlement with its own
			// Cemetery will handle is left to them. The pit only clears loose territory litter.
			if (c.gridTileLocation.IsNextToOrPartOfSettlement(out var settlement) && settlement is NPCSettlement npcSettlement && npcSettlement.HasStructure(STRUCTURE_TYPE.CEMETERY))
			{
				return false;
			}
			return c.gridTileLocation.GetDistanceTo(pitReference) <= CollectionRadius;
		}

		// Mirrors the game's own BuryCharacter.AfterBurySuccess disposal:
		// sapient dead get a tombstone placed inside the pit (and persist for it);
		// animals/monsters are cleared outright. Either way the litter leaves the map.
		private void ConsumeCorpse(Character corpse)
		{
			bool makeTombstone = corpse.race.IsSapient();
			if (makeTombstone)
			{
				LocationGridTile pitTile = GetPlacementTile();
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
			bodyCount++;
			Debug.Log($"[MassGrave] Consumed {corpse.name} into the pit (bodyCount={bodyCount}).");
		}

		private LocationGridTile GetPlacementTile()
		{
			if (unoccupiedTiles != null && unoccupiedTiles.Count > 0)
			{
				return CollectionUtilities.GetRandomElement(unoccupiedTiles);
			}
			return GetRandomTile();
		}
	}
}
