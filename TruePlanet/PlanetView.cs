using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TruePlanet
{
	public sealed class PlanetView : MonoBehaviour
	{
		internal static PlanetView Instance;
		private static Planet _retained;
		private Planet _planet;
		private PlanetCanvas _map;
		private TMP_FontAsset _font;
		private Material _fontMaterial;
		private TMP_InputField _seed, _count, _land, _nations, _wild, _name;
		private TextMeshProUGUI _status, _inspector, _sizeLabel, _layerLabel;
		private ProvinceSize _largest;
		private Transform _saved;
		private Button _save;
		private static readonly Color Background = new Color32(23, 29, 35, 255), Panel = new Color32(38, 45, 52, 255), Control = new Color32(59, 67, 74, 255);
		internal static void Open()
		{
			if (Instance != null) return;
			var root = new GameObject("TruePlanet atlas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2600;
			var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
			root.AddComponent<Image>().color = Background;
			Instance = root.AddComponent<PlanetView>();
			try { Instance.Build(); }
			catch (Exception e) { TruePlanetMod.Log.Error("Cannot open the planet atlas: " + e); Destroy(root); }
		}
		private void Build()
		{
			TMP_Text native = MainMenuUI.Instance.GetComponentInChildren<TMP_Text>(true);
			if (native == null) throw new InvalidOperationException("The native menu font is unavailable.");
			_font = native.font; _fontMaterial = native.fontSharedMaterial;
			var title = Label(transform, "TruePlanet / Planet atlas", 30); At(title.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(28, -65), new Vector2(-200, -16));
			var back = Button(transform, "Back", () => Destroy(gameObject)); At(back.gameObject, new Vector2(1, 1), Vector2.one, new Vector2(-155, -57), new Vector2(-28, -20));
			Transform options = Row(transform); At(options.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(28, -120), new Vector2(-28, -76));
			PlanetOptions defaults = TruePlanetMod.Settings.Options(); _largest = defaults.LargestProvince;
			_seed = Field(options, "Seed", _retained == null ? "20261006" : _retained.Seed.ToString(CultureInfo.InvariantCulture), 120);
			_count = Field(options, "Provinces", defaults.Provinces.ToString(), 72);
			_land = Field(options, "Land %", (defaults.LandShare * 100).ToString("0"), 62);
			_nations = Field(options, "Nations", defaults.Nations.ToString(), 62);
			_wild = Field(options, "Wild %", (defaults.WildShare * 100).ToString("0"), 62);
			Button size = Button(options, "", () => { _largest = (ProvinceSize)(((int)_largest + 1) % 3); _sizeLabel.text = "Max: " + Size(_largest); });
			size.gameObject.GetComponent<LayoutElement>().preferredWidth = 160; _sizeLabel = size.GetComponentInChildren<TextMeshProUGUI>(); _sizeLabel.text = "Max: " + Size(_largest);
			Button(options, "Generate", Generate).gameObject.GetComponent<LayoutElement>().preferredWidth = 110;
			var left = Box("Map panel", transform, Panel); At(left, Vector2.zero, Vector2.one, new Vector2(28, 85), new Vector2(-438, -140));
			var layers = Row(left.transform); At(layers.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(14, -50), new Vector2(-14, -14));
			var layer = Button(layers, "Terrain", () => { _map.Political = !_map.Political; _layerLabel.text = _map.Political ? "Political" : "Terrain"; _map.Redraw(); }); _layerLabel = layer.GetComponentInChildren<TextMeshProUGUI>();
			Button(layers, "Borders: on", () => Toggle("Borders: ", true));
			Button(layers, "Roads: on", () => Toggle("Roads: ", false));
			var mapArea = New("Map bounds", left.transform); At(mapArea, Vector2.zero, Vector2.one, new Vector2(14, 80), new Vector2(-14, -64));
			var map = New("Planet map", mapArea.transform); Stretch(map);
			var fit = map.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 2;
			map.AddComponent<RawImage>().color = Color.white; _map = map.AddComponent<PlanetCanvas>(); _map.Picked = Inspect;
			var legend = Label(left.transform, "Ocean / grassland / forest / desert / snow / mountain\nGold diamonds: capitals. Pale diamond: selection. Ochre lines: land roads.", 17);
			At(legend.gameObject, Vector2.zero, new Vector2(1, 0), new Vector2(18, 16), new Vector2(-18, 69));
			var right = Box("Province panel", transform, Panel); At(right, new Vector2(1, 0), Vector2.one, new Vector2(-414, 85), new Vector2(-28, -140));
			_name = Input(right.transform, "Planet name", ""); At(_name.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(16, -57), new Vector2(-16, -16));
			var inspectArea = New("Province details", right.transform); At(inspectArea, new Vector2(0, .42f), Vector2.one, new Vector2(12, 4), new Vector2(-12, -70));
			_inspector = Label(Scroll(inspectArea.transform), "Generate a planet or load a saved atlas. Then select a province on the map.", 18);
			_inspector.gameObject.GetComponent<LayoutElement>().preferredHeight = -1; _inspector.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_save = Button(right.transform, "Save atlas", Save); At(_save.gameObject, new Vector2(0, .42f), new Vector2(1, .42f), new Vector2(16, -48), new Vector2(-16, -10)); _save.interactable = false;
			var savedLabel = Label(right.transform, "Saved atlases", 19); At(savedLabel.gameObject, new Vector2(0, .42f), new Vector2(1, .42f), new Vector2(16, -90), new Vector2(-16, -54));
			var saves = New("Saved atlas list", right.transform); At(saves, Vector2.zero, new Vector2(1, .42f), new Vector2(12, 12), new Vector2(-12, -96)); _saved = Scroll(saves.transform);
			_status = Label(transform, "Atlas only. Provinces have planned settlements, not live game populations. Stock New Game and Load are unchanged.", 18);
			At(_status.gameObject, Vector2.zero, new Vector2(1, 0), new Vector2(28, 18), new Vector2(-28, 69));
			RefreshSaves(); if (_retained != null) Display(_retained);
		}
		private void Toggle(string prefix, bool border)
		{
			bool enabled;
			if (border) enabled = _map.Borders = !_map.Borders; else enabled = _map.Roads = !_map.Roads;
			foreach (TextMeshProUGUI label in GetComponentsInChildren<TextMeshProUGUI>()) if (label.text.StartsWith(prefix, StringComparison.Ordinal)) label.text = prefix + (enabled ? "on" : "off");
			_map.Redraw();
		}
		private void Generate() => Attempt(() =>
		{
			int Integer(TMP_InputField field) => int.Parse(field.text, NumberStyles.Integer, CultureInfo.InvariantCulture);
			var options = new PlanetOptions { Provinces = Integer(_count), LandShare = Integer(_land) / 100.0, Nations = Integer(_nations), WildShare = Integer(_wild) / 100.0, LargestProvince = _largest };
			Planet planet = PlanetGenerator.Generate(Integer(_seed), options); TruePlanetMod.Name(planet); Display(planet);
		});
		private void Display(Planet planet)
		{
			_planet = _retained = planet; _name.text = planet.Name; _map.Show(planet); _save.interactable = true;
			_seed.text = planet.Seed.ToString(CultureInfo.InvariantCulture); _count.text = planet.Options.Provinces.ToString(); _land.text = (planet.Options.LandShare * 100).ToString("0");
			_nations.text = planet.Options.Nations.ToString(); _wild.text = (planet.Options.WildShare * 100).ToString("0"); _largest = planet.Options.LargestProvince; _sizeLabel.text = "Max: " + Size(_largest);
			_inspector.text = planet.Name + "\n\n" + planet.Provinces.Length + " provinces\n" + planet.Provinces.Count(p => p.Land) + " land provinces\n" + planet.Nations.Length + " actual nations (" + planet.Options.Nations + " requested)\n" + planet.Provinces.Sum(p => p.Villages.Length) + " planned villages\n\nSelect a province to inspect its terrain and settlements.";
			_status.text = "Seed " + planet.Seed + ". Rendering atlas. All provinces are unvisited; no playable province is loaded.";
		}
		private void Inspect(int id)
		{
			Province p = _planet.Provinces[id]; var text = new StringBuilder(); text.AppendLine(p.Name + (p.Land ? " / Land" : " / Ocean"));
			text.AppendLine(p.Visited ? "Visited" : "Unvisited: planned data only"); text.AppendLine(p.Held ? "Player held" : "Not held by the player");
			if (p.Land)
			{
				text.AppendLine("Map size: " + Size(p.Size)); text.AppendLine("Nation: " + (p.Nation >= 0 ? _planet.Nations[p.Nation].Name : "Independent"));
				if (p.Nation >= 0) { Nation n = _planet.Nations[p.Nation]; text.AppendLine((n.Elven ? "Elven" : "Human") + " / " + n.Emblem); }
				text.AppendLine("Grass / forest / desert / snow: " + string.Join(" / ", p.Biomes.Select(v => (v * 100).ToString("0") + "%")));
				text.AppendLine("Mountain: " + (p.Mountains * 100).ToString("0") + "% / Water: " + (p.Water * 100).ToString("0") + "%");
				text.AppendLine("Temperature: " + (p.Temperature * 100).ToString("0") + "% / Moisture: " + (p.Moisture * 100).ToString("0") + "%");
				text.AppendLine("\nPlanned villages: " + p.Villages.Length);
				foreach (Village v in p.Villages) text.AppendLine(v.Name + (v.Capital ? " (capital)" : "") + "\n  " + (v.Elven ? "Elven" : "Human") + " / " + (v.Nation < 0 ? "Independent" : _planet.Nations[v.Nation].Name));
				text.AppendLine("Road routes: " + _planet.Roads.Count(r => r.Path.Contains(id)));
			}
			text.AppendLine("\nNeighbours: " + string.Join(", ", p.Neighbours.Select(i => _planet.Provinces[i].Name)));
			_inspector.text = text.ToString(); _status.text = "Selected " + p.Name + ". Atlas summaries do not simulate population, buildings or armies.";
		}
		private void Save() => Attempt(() => { _planet.Name = _name.text.Trim(); string path = PlanetStore.Save(TruePlanetMod.SaveRoot, _planet); _status.text = "Saved " + _planet.Name + " to " + path; RefreshSaves(); });
		private void RefreshSaves()
		{
			for (int i = _saved.childCount - 1; i >= 0; i--) { var child = _saved.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
			foreach (string path in PlanetStore.Files(TruePlanetMod.SaveRoot))
			{
				try { Planet saved = PlanetStore.Load(path); Button(_saved, saved.Name + " / seed " + saved.Seed, () => Attempt(() => Display(PlanetStore.Load(path)))); }
				catch (Exception e) { Label(_saved, Path.GetFileName(Path.GetDirectoryName(path)) + ": " + e.Message, 16); }
			}
			if (_saved.childCount == 0) Label(_saved, "No saved atlases. Generate a planet, then Save atlas.", 16);
		}
		private void Attempt(Action action)
		{
			try { action(); } catch (Exception e) { _status.text = e.Message; TruePlanetMod.Log.Warning("Atlas operation failed: " + e.Message); }
		}
		private static string Size(ProvinceSize size) => size == ProvinceSize.Huge ? "Huge (32x20)" : size == ProvinceSize.ExtraLarge ? "Extra Large (24x14)" : "Large (20x12)";
		private GameObject New(string name, Transform parent) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
		private GameObject Box(string name, Transform parent, Color color) { var go = New(name, parent); go.AddComponent<Image>().color = color; return go; }
		private static void At(GameObject go, Vector2 min, Vector2 max, Vector2 low, Vector2 high) { var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; }
		private static void Stretch(GameObject go) => At(go, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		private TextMeshProUGUI Label(Transform parent, string text, float size = 18)
		{
			var go = New("Label", parent); var label = go.AddComponent<TextMeshProUGUI>(); label.font = _font; label.fontSharedMaterial = _fontMaterial;
			label.text = text; label.fontSize = size; label.color = Color.white; label.richText = false; label.enableWordWrapping = true; label.raycastTarget = false; label.alignment = TextAlignmentOptions.MidlineLeft;
			var layout = go.AddComponent<LayoutElement>(); layout.preferredHeight = size + 12; layout.minHeight = size + 12; return label;
		}
		private Button Button(Transform parent, string text, Action action)
		{
			var go = Box(text, parent, Control); var button = go.AddComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
			ColorBlock colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.selectedColor = new Color(1.25f, 1.2f, 1.0f); button.colors = colors;
			var layout = go.AddComponent<LayoutElement>(); layout.preferredHeight = 36; layout.minHeight = 36; layout.flexibleWidth = 1;
			var label = Label(go.transform, text, 16); Stretch(label.gameObject); label.alignment = TextAlignmentOptions.Center;
			button.onClick.AddListener(() => action()); return button;
		}
		private TMP_InputField Input(Transform parent, string name, string value)
		{
			var go = Box(name, parent, new Color32(19, 24, 29, 255)); var input = go.AddComponent<TMP_InputField>();
			var viewport = New("Text viewport", go.transform); At(viewport, Vector2.zero, Vector2.one, new Vector2(8, 3), new Vector2(-8, -3)); viewport.AddComponent<RectMask2D>();
			var text = Label(viewport.transform, "", 16); Stretch(text.gameObject); input.textViewport = (RectTransform)viewport.transform; input.textComponent = text; input.targetGraphic = go.GetComponent<Image>();
			input.text = value; input.characterLimit = 120; input.customCaretColor = true; input.caretColor = Color.white; input.caretWidth = 2;
			var outline = go.AddComponent<Outline>(); outline.effectColor = Color.clear; outline.effectDistance = new Vector2(2, 2);
			input.onSelect.AddListener(_ => outline.effectColor = new Color32(172, 149, 91, 255)); input.onDeselect.AddListener(_ => outline.effectColor = Color.clear);
			input.enabled = false; input.enabled = true; go.AddComponent<LayoutElement>().preferredHeight = 36; return input;
		}
		private TMP_InputField Field(Transform parent, string title, string value, float width)
		{
			var column = New(title, parent); var layout = column.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 5; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = false;
			var label = Label(column.transform, title, 16); label.gameObject.GetComponent<LayoutElement>().preferredWidth = title.Length * 9;
			var input = Input(column.transform, title, value); input.contentType = TMP_InputField.ContentType.IntegerNumber; input.gameObject.GetComponent<LayoutElement>().preferredWidth = width;
			var element = column.AddComponent<LayoutElement>(); element.preferredWidth = width + title.Length * 9 + 5; return input;
		}
		private Transform Row(Transform parent)
		{
			var go = New("Row", parent); var layout = go.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 10; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = false; return go.transform;
		}
		private Transform Scroll(Transform parent)
		{
			var go = New("Scroll", parent); Stretch(go); var scroll = go.AddComponent<ScrollRect>(); var viewport = New("Viewport", go.transform); Stretch(viewport); viewport.AddComponent<RectMask2D>(); viewport.AddComponent<Image>().color = Color.clear;
			var content = New("Content", viewport.transform); var rect = (RectTransform)content.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = Vector2.zero;
			var layout = content.AddComponent<VerticalLayoutGroup>(); layout.spacing = 6; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
			content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.viewport = (RectTransform)viewport.transform; scroll.content = rect; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
			return content.transform;
		}
		private void OnDestroy() { if (_planet != null) _retained = _planet; if (Instance == this) Instance = null; }
	}
}
