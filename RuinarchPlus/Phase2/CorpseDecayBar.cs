using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using UnityEngine;
using UnityEngine.UI;

namespace RuinarchPlus.Phase2
{
	/// <summary>
	/// While the player hovers or selects an unburied corpse, the game's own map HP bar above
	/// it shows how much of its decay time is left: full when fresh, empty just before it
	/// decomposes (see <see cref="CorpseDecay"/>). Hidden again when neither applies.
	/// </summary>
	[HarmonyPatch(typeof(InnerTileMap), nameof(InnerTileMap.Update))]
	internal static class CorpseDecayBar
	{
		// Markers whose bar we are showing, and whether we hid their attack-speed meter.
		private static readonly Dictionary<CharacterMarker, bool> Shown = new Dictionary<CharacterMarker, bool>();

		private static readonly List<CharacterMarker> Gone = new List<CharacterMarker>();

		private static void Postfix()
		{
			try
			{
				Character hovered = InnerMapManager.Instance?.currentlyHoveredPoi as Character;
				Character selected = UIManager.Instance?.GetCurrentlySelectedCharacter();
				Show(hovered);
				if (selected != hovered)
				{
					Show(selected);
				}
				Gone.Clear();
				foreach (CharacterMarker m in Shown.Keys)
				{
					Character c = m == null ? null : m.character;
					if (c == null || (c != hovered && c != selected) || CorpseDecay.Remaining(c) == null)
					{
						Gone.Add(m);
					}
				}
				foreach (CharacterMarker m in Gone)
				{
					Hide(m);
				}
			}
			catch (Exception e)
			{
				// Once per session: this runs every frame.
				if (!_warned)
				{
					_warned = true;
					RuinarchPlus.Log?.Warning("Corpse decay bar failed: " + e.Message);
				}
			}
		}

		private static bool _warned;

		private static void Show(Character c)
		{
			float? left = CorpseDecay.Remaining(c);
			CharacterMarker m = left == null || !c.hasMarker ? null : c.marker;
			if (m == null)
			{
				return;
			}
			GameObject bar = m.hpBarGO;
			if (!Shown.ContainsKey(m))
			{
				// The attack-speed meter under the HP bar means nothing on a corpse.
				bool hidMeter = m.aspeedFill != null && m.aspeedFill.gameObject.activeSelf;
				if (hidMeter)
				{
					m.aspeedFill.gameObject.SetActive(false);
				}
				Shown[m] = hidMeter;
				bar.SetActive(true);
			}
			Image fill = bar.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Fill");
			if (fill != null)
			{
				fill.fillAmount = left.Value;
			}
		}

		private static void Hide(CharacterMarker m)
		{
			bool hidMeter = Shown[m];
			Shown.Remove(m);
			if (m == null)
			{
				return;
			}
			if (m.hasHPBarGO)
			{
				m.HideHPBar();
			}
			if (hidMeter && m.aspeedFill != null)
			{
				m.aspeedFill.gameObject.SetActive(true);
			}
		}

		/// <summary>For the test harness: is a decay bar showing on this corpse, and how full.</summary>
		internal static float ShownFill(Character c)
		{
			CharacterMarker m = c != null && c.hasMarker ? c.marker : null;
			if (m == null || !Shown.ContainsKey(m) || !m.hasHPBarGO || !m.hpBarGO.activeSelf)
			{
				return -1f;
			}
			Image fill = m.hpBarGO.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Fill");
			return fill != null ? fill.fillAmount : -1f;
		}
	}
}
