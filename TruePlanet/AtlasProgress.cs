using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TruePlanet
{
	internal sealed class AtlasProgress
	{
		private readonly GameObject _panel;
		private readonly TMP_Text _title, _detail;
		private readonly RectTransform _fill;
		internal AtlasProgress(GlobeSkin skin, Transform parent)
		{
			_panel = skin.Panel("Planet loading progress", parent);
			var rect = (RectTransform)_panel.transform;
			rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(620, 230); rect.anchoredPosition = Vector2.zero;
			_title = Text(skin, "Operation", new Vector2(24, -24), 24);
			_detail = Text(skin, "Progress details", new Vector2(24, -74), 18);
			var bar = GlobeSkin.Node("Completed work", _panel.transform); var image = bar.AddComponent<Image>(); image.color = new Color32(38, 34, 42, 255);
			var br = (RectTransform)bar.transform; br.anchorMin = Vector2.zero; br.anchorMax = new Vector2(1, 0); br.offsetMin = new Vector2(24, 24); br.offsetMax = new Vector2(-24, 44);
			var fill = GlobeSkin.Node("Progress fill", bar.transform); fill.AddComponent<Image>().color = new Color32(151, 92, 125, 255);
			_fill = (RectTransform)fill.transform; _fill.anchorMin = Vector2.zero; _fill.offsetMin = _fill.offsetMax = Vector2.zero;
			_panel.SetActive(false);
		}
		private TMP_Text Text(GlobeSkin skin, string name, Vector2 position, float size)
		{
			var go = GlobeSkin.Node(name, _panel.transform); var rect = (RectTransform)go.transform;
			rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1); rect.offsetMin = new Vector2(position.x, position.y - 94); rect.offsetMax = new Vector2(-24, position.y);
			var label = go.AddComponent<TextMeshProUGUI>(); label.font = skin.Font; label.fontSharedMaterial = skin.FontMaterial; label.fontSize = size; label.color = new Color32(238, 224, 196, 255); label.richText = false; label.raycastTarget = false; return label;
		}
		internal void Show(string stage, int completed, int total, int units = 0, int unitTotal = 0, string pending = null)
		{
			_panel.SetActive(true); _panel.transform.SetAsLastSibling(); _title.text = stage;
			_detail.text = completed + " / " + total + " stages complete" + (unitTotal > 0 ? "\nCurrent stage: " + units + " / " + unitTotal + " rows" : "\nCurrent stage is running") + (pending == null ? "" : "\nPending: " + pending);
			float partial = unitTotal > 0 ? (float)units / unitTotal : 0;
			_fill.anchorMax = new Vector2(Mathf.Clamp01((completed + partial) / total), 1);
		}
		internal void Hide() => _panel.SetActive(false);
	}
}
