using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps;
using UnityEngine;

namespace RuinarchPlus
{
	// BUG: the path line of the selected character (InnerTileMap.ShowPath, drawn from
	// InnerTileMap.Update while they move) has a fixed width in world units, set in the
	// prefab. Zoomed out, that is less than a pixel on screen and the line disappears.
	// Fix: while the line is shown, never let it get thinner than MinPixels on screen. At
	// normal zoom the prefab's width is already wider, so nothing changes there.
	[HarmonyPatch(typeof(InnerTileMap), nameof(InnerTileMap.Update))]
	internal static class Fix_PathLineZoomedOut
	{
		internal const float MinPixels = 2f;

		private static readonly AccessTools.FieldRef<InnerTileMap, LineRenderer> PathLine =
			AccessTools.FieldRefAccess<InnerTileMap, LineRenderer>("pathLineRenderer");

		// The prefab's own multiplier, per renderer (they are never destroyed mid-game).
		private static readonly Dictionary<LineRenderer, float> BaseMultiplier = new Dictionary<LineRenderer, float>();

		private static void Postfix(InnerTileMap __instance)
		{
			LineRenderer line = PathLine(__instance);
			Camera camera = InnerMapCameraMove.Instance?.camera;
			if (line == null || camera == null || !line.gameObject.activeSelf || Screen.height <= 0)
			{
				return;
			}
			if (!BaseMultiplier.TryGetValue(line, out float baseMultiplier))
			{
				baseMultiplier = line.widthMultiplier;
				BaseMultiplier[line] = baseMultiplier;
			}
			float curve = line.widthCurve.Evaluate(0f);
			if (curve <= 0f)
			{
				return;
			}
			float worldPerPixel = 2f * camera.orthographicSize / Screen.height;
			line.widthMultiplier = Mathf.Max(baseMultiplier, MinPixels * worldPerPixel / curve);
		}

		/// <summary>On-screen width of the path line in pixels right now (for the test harness).</summary>
		internal static float ScreenWidth(InnerTileMap map)
		{
			LineRenderer line = map == null ? null : PathLine(map);
			Camera camera = InnerMapCameraMove.Instance?.camera;
			if (line == null || camera == null || Screen.height <= 0)
			{
				return -1f;
			}
			return line.widthCurve.Evaluate(0f) * line.widthMultiplier / (2f * camera.orthographicSize / Screen.height);
		}
	}
}
