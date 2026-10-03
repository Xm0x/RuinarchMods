using HarmonyLib;
using Inner_Maps;
using UnityEngine;
using UnityEngine.UI;

namespace RuinarchPerformance
{
	/// <summary>
	/// The minimap is a second camera that draws the map's Minimap layer (one tilemap and the
	/// rectangle marking your view) into a small texture. The game leaves that camera on, so
	/// it draws every frame, and the 2D renderer's fixed work per camera costs about 1 ms a
	/// frame (measured on an Extra Large map: 1.0-1.3 ms of a 6.5 ms frame).
	///
	/// Its picture only changes when a minimap tile changes, the view rectangle moves or the
	/// texture is lost. Here the camera draws on those frames only, and only while the minimap
	/// is shown; once a second it draws anyway, for anything else on that layer. Clicking the
	/// minimap works as before: it uses the camera's projection, which needs no drawing.
	/// </summary>
	internal static class MinimapRedraw
	{
		private const float FallbackSeconds = 1f;

		private static readonly AccessTools.FieldRef<InnerTileMap, SpriteRenderer> ViewRect =
			AccessTools.FieldRefAccess<InnerTileMap, SpriteRenderer>("minimapPlayerCameraVisual");
		private static readonly AccessTools.FieldRef<MinimapUIController, MinimapUIView> MinimapView =
			AccessTools.FieldRefAccess<MinimapUIController, MinimapUIView>("m_minimapUIView");

		private static bool _changed = true;
		private static InnerTileMap _map;
		private static bool _wasShown;
		private static Vector3 _rectPosition;
		private static Vector2 _rectSize;
		private static float _lastDraw;

		/// <summary>True unless the minimap panel is known to be hidden.</summary>
		private static bool Shown()
		{
			MinimapUIController controller = UIManager.Instance?.sidebarUIController?.minimapUIController;
			if (controller == null) return true;
			RawImage image = MinimapView(controller)?.UIModel?.minimapImage?.rawImage;
			return image == null || image.isActiveAndEnabled;
		}

		[HarmonyPatch(typeof(InnerTileMap), nameof(InnerTileMap.SetMinimapTileColor))]
		internal static class Patch_TileColor
		{
			private static void Postfix() => _changed = true;
		}

		[HarmonyPatch(typeof(InnerTileMap), "SetMinimapTileVisual")]
		internal static class Patch_TileVisual
		{
			private static void Postfix() => _changed = true;
		}

		// InnerTileMap.Update moves the view rectangle every frame; cameras draw later in the
		// same frame, so switching the camera on here draws this frame's picture.
		[HarmonyPatch(typeof(InnerTileMap), nameof(InnerTileMap.Update))]
		internal static class Patch_Update
		{
			private static void Postfix(InnerTileMap __instance)
			{
				Camera camera = __instance.minimapCamera;
				if (camera == null) return;
				bool shown = Shown();
				SpriteRenderer rect = ViewRect(__instance);
				bool moved = rect != null && (rect.transform.position != _rectPosition || rect.size != _rectSize);
				RenderTexture texture = camera.targetTexture;
				bool draw = shown && (_changed || moved || !_wasShown || __instance != _map
					|| texture == null || !texture.IsCreated() || Time.unscaledTime - _lastDraw >= FallbackSeconds);
				camera.enabled = draw;
				if (draw)
				{
					_changed = false;
					_lastDraw = Time.unscaledTime;
					if (rect != null)
					{
						_rectPosition = rect.transform.position;
						_rectSize = rect.size;
					}
				}
				_wasShown = shown;
				_map = __instance;
			}
		}
	}
}
