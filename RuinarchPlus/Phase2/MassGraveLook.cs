using System;
using System.IO;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Ruinarch.ModContent;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// The Mass Grave's own look. The structure borrows the Cemetery prefab (footprint,
	/// walls, pathing), so without this it is indistinguishable from a Cemetery. A sprite of
	/// a walled burial pit is laid over the structure's ground, and it swaps through four
	/// fill stages (empty -> full) as bodies are laid in it (<see cref="MassGrave.fillRatio"/>).
	///
	/// Art ships as loose PNGs under <c>art/mass_grave/</c> and loads through the framework's
	/// <see cref="ModArt"/> at gameplay time (never at mod load: no graphics device yet).
	/// Structure objects are pooled and shared with real Cemeteries, so the overlay is a
	/// separately named child that is removed on destruction and, as a safety net, whenever
	/// any structure object is reset for reuse.
	/// </summary>
	internal static class MassGraveLook
	{
		internal const int FillStages = 4;
		private const string OverlayName = "RuinarchPlus.MassGraveOverlay";
		private const float LoadPixelsPerUnit = 100f;

		private static readonly AccessTools.FieldRef<LocationStructureObject, TilemapRenderer> GroundRenderer =
			AccessTools.FieldRefAccess<LocationStructureObject, TilemapRenderer>("_groundTileMapRenderer");

		private static bool _warned;

		internal static int StageFor(float fillRatio)
		{
			return Mathf.Clamp(Mathf.FloorToInt(fillRatio * FillStages), 0, FillStages - 1);
		}

		private static Sprite StageSprite(int stage)
		{
			string path = Path.Combine(RuinarchPlus.ModDir ?? string.Empty, "art", "mass_grave", $"mass_grave_{stage}.png");
			return ModArt.LoadSprite(path, LoadPixelsPerUnit);
		}

		/// <summary>Create or update the overlay on <paramref name="pit"/>'s structure object.</summary>
		internal static void Refresh(MassGrave pit)
		{
			try
			{
				LocationStructureObject obj = pit.structureObj;
				if (obj == null || pit.hasBeenDestroyed)
				{
					return;
				}
				Sprite sprite = StageSprite(StageFor(pit.fillRatio));
				if (sprite == null)
				{
					return;
				}
				Transform existing = obj.transform.Find(OverlayName);
				SpriteRenderer renderer = existing != null ? existing.GetComponent<SpriteRenderer>() : Create(obj);
				if (renderer.sprite != sprite)
				{
					renderer.sprite = sprite;
				}
				FitToFootprint(pit, obj, renderer);
			}
			catch (Exception e)
			{
				if (!_warned)
				{
					_warned = true;
					RuinarchPlus.Log?.Warning("Mass Grave look could not be applied (the structure keeps its default look): " + e.Message);
				}
			}
		}

		/// <summary>Remove the overlay from a structure object (destruction / pool reuse).</summary>
		internal static void Strip(LocationStructureObject obj)
		{
			Transform overlay = obj != null ? obj.transform.Find(OverlayName) : null;
			if (overlay != null)
			{
				UnityEngine.Object.Destroy(overlay.gameObject);
			}
		}

		private static SpriteRenderer Create(LocationStructureObject obj)
		{
			GameObject go = new GameObject(OverlayName);
			go.layer = obj.gameObject.layer;
			go.transform.SetParent(obj.transform, worldPositionStays: false);
			SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
			// Directly above the structure's floor, below its walls, decorations and characters.
			TilemapRenderer ground = GroundRenderer(obj);
			renderer.sortingLayerName = ground != null ? ground.sortingLayerName : "Area Maps";
			renderer.sortingOrder = (ground != null ? ground.sortingOrder : 15) + 1;
			return renderer;
		}

		// Centre on the structure's tiles and scale the pit to cover them.
		private static void FitToFootprint(MassGrave pit, LocationStructureObject obj, SpriteRenderer renderer)
		{
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			if (pit.tiles != null)
			{
				foreach (Inner_Maps.LocationGridTile tile in pit.tiles)
				{
					Vector3 p = tile.centeredWorldLocation;
					minX = Mathf.Min(minX, p.x);
					minY = Mathf.Min(minY, p.y);
					maxX = Mathf.Max(maxX, p.x);
					maxY = Mathf.Max(maxY, p.y);
				}
			}
			Vector3 centre;
			float extent;
			if (minX <= maxX)
			{
				centre = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, obj.transform.position.z);
				extent = Mathf.Max(maxX - minX, maxY - minY) + 1f;
			}
			else
			{
				centre = obj.transform.position;
				extent = Mathf.Max(obj.size.x, obj.size.y);
			}
			Transform t = renderer.transform;
			t.position = centre;
			float spriteWidth = renderer.sprite.bounds.size.x;
			float parentScale = obj.transform.lossyScale.x == 0f ? 1f : obj.transform.lossyScale.x;
			t.localScale = Vector3.one * (extent / spriteWidth / parentScale);
		}
	}

	// Pool safety: a structure object reset for reuse (for example by a real Cemetery) must
	// never keep a Mass Grave overlay.
	[HarmonyPatch(typeof(LocationStructureObject), nameof(LocationStructureObject.Reset))]
	internal static class MassGrave_StripOverlayOnReset
	{
		private static void Postfix(LocationStructureObject __instance)
		{
			try
			{
				MassGraveLook.Strip(__instance);
			}
			catch
			{
			}
		}
	}
}
