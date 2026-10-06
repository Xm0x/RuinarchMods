using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Newtonsoft.Json;
using UnityEngine;

namespace RuinarchPlus.Phase7
{
	// The supplied GIF is packaged as a lossless PNG atlas plus its original frame delays.
	// One texture and sprite set are shared by every Heart; no GIF decoding during play.
	internal sealed class BlightHeartVisual : MonoBehaviour
	{
		private sealed class Timing
		{
			public int frameSize;
			public int columns;
			public int[] durationsMs;
		}

		private static Texture2D _atlas;
		private static Timing _timing;
		private static float[] _ends;
		private static Sprite[] _frames;
		private static Sprite _icon;
		private SpriteRenderer _renderer;
		private Sprite _originalSprite;
		private BlightHeart _owner;
		private TileObject _tileObject;
		private float _elapsed;
		private int _frame;

		internal static Sprite Icon
		{
			get
			{
				LoadAtlas();
				if (_icon == null) _icon = MakeSprite(0, new Vector2(.5f, .5f), 64f);
				return _icon;
			}
		}

		private static void LoadAtlas()
		{
			if (_atlas != null) return;
			string path = Path.Combine(RuinarchPlus.ModDir, "art", "blight-heart-pumping");
			_timing = JsonConvert.DeserializeObject<Timing>(File.ReadAllText(path + ".json"));
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			if (!texture.LoadImage(File.ReadAllBytes(path + ".png"), true))
			{
				Destroy(texture);
				throw new InvalidOperationException("Could not load the Blight Heart animation atlas.");
			}
			texture.name = "Blight Heart pumping atlas";
			texture.filterMode = FilterMode.Bilinear;
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.hideFlags = HideFlags.HideAndDontSave;
			_ends = new float[_timing.durationsMs.Length];
			float end = 0;
			for (int i = 0; i < _ends.Length; i++) _ends[i] = end += _timing.durationsMs[i] / 1000f;
			_atlas = texture;
		}

		private static Sprite MakeSprite(int frame, Vector2 pivot, float pixelsPerUnit)
		{
			int size = _timing.frameSize;
			var rect = new Rect(frame % _timing.columns * size, _atlas.height - (frame / _timing.columns + 1) * size, size, size);
			Sprite sprite = Sprite.Create(_atlas, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
			sprite.name = "Blight Heart pumping " + frame;
			sprite.hideFlags = HideFlags.HideAndDontSave;
			return sprite;
		}

		private static void Animate(SpriteRenderer renderer, BlightHeart owner = null, TileObject tileObject = null)
		{
			LoadAtlas();
			if (_frames == null)
			{
				Sprite original = renderer.sprite;
				Vector2 pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
				float pixelsPerUnit = _timing.frameSize / original.bounds.size.x;
				_frames = new Sprite[_ends.Length];
				for (int i = 0; i < _frames.Length; i++) _frames[i] = MakeSprite(i, pivot, pixelsPerUnit);
			}
			var animation = renderer.GetComponent<BlightHeartVisual>() ?? renderer.gameObject.AddComponent<BlightHeartVisual>();
			animation._renderer = renderer;
			if (renderer.sprite.texture != _atlas) animation._originalSprite = renderer.sprite;
			animation._owner = owner;
			animation._tileObject = tileObject;
			animation._elapsed = 0;
			animation._frame = 0;
			// Assign directly: the native sprite index remains valid in save files.
			renderer.sprite = _frames[0];
		}

		internal static void Attach(BlightHeart heart)
		{
			foreach (var tile in heart.tiles)
			{
				TileObject obj = tile.tileObjectComponent.objHere;
				if (obj != null && obj.tileObjectType == TILE_OBJECT_TYPE.CRYPT_TILE_OBJECT)
				{
					Animate(obj.mapVisual.objectSpriteRenderer, heart, obj);
					return; // The native Crypt object occupies several of the Heart's tiles.
				}
			}
		}

		internal static void PreparePreview(STRUCTURE_TYPE kind)
		{
			var placement = (PlayerStructurePlacementVisual)AccessTools.Field(typeof(PlayerManager), "_structurePlacementVisual").GetValue(PlayerManager.Instance);
			var previews = (Dictionary<STRUCTURE_TYPE, LocationStructureObject>)AccessTools.Field(typeof(PlayerStructurePlacementVisual), "_structureVisuals").GetValue(placement);
			if (previews.ContainsKey(kind)) return;
			LocationStructureObject crypt = previews[STRUCTURE_TYPE.CRYPT];
			var preview = Instantiate(crypt, crypt.transform.parent);
			preview.name = "Blight Heart placement preview";
			preview.structureType = kind;
			foreach (var obj in preview.GetComponentsInChildren<StructureTemplateObjectData>(true))
				if (obj.tileObjectType == TILE_OBJECT_TYPE.CRYPT_TILE_OBJECT) Animate(obj.spriteRenderer);
			previews.Add(kind, preview);
		}

		private void LateUpdate()
		{
			// Map visuals are pooled. A visual reused for a normal Crypt must not keep pumping.
			if (_tileObject != null && (_owner.hasBeenDestroyed || _tileObject.gridTileLocation?.structure != _owner))
			{
				Destroy(this);
				RestoreNativeSprite();
				return;
			}
			if (GameManager.Instance == null || GameManager.Instance.isPaused) return;
			_elapsed += Time.unscaledDeltaTime;
			if (_elapsed < _ends[_frame]) return;
			if (_elapsed >= _ends[_ends.Length - 1]) { _elapsed %= _ends[_ends.Length - 1]; _frame = 0; }
			while (_elapsed >= _ends[_frame]) _frame++;
			_renderer.sprite = _frames[_frame];
		}

		private void RestoreNativeSprite()
		{
			if (_renderer != null && _renderer.sprite != null && _renderer.sprite.texture == _atlas)
				_renderer.sprite = _originalSprite;
		}

		private void OnDisable() => RestoreNativeSprite();

		private void OnEnable()
		{
			if (_renderer != null && (_tileObject == null || (!_owner.hasBeenDestroyed && _tileObject.gridTileLocation?.structure == _owner)))
				_renderer.sprite = _frames[_frame];
		}
	}
}
