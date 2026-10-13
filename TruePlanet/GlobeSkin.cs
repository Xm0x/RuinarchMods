using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TruePlanet
{
	internal sealed class GlobeSkin : IDisposable
	{
		internal TMP_FontAsset Font { get; }
		internal Material FontMaterial { get; }
		private readonly GameObject _templates;
		private readonly Image _frame;
		private readonly Transform _button, _input;
		private readonly Sprite _scrollBackground, _scrollHandle;
		internal GlobeSkin(Transform owner, bool inGame)
		{
			GameObject settings = Settings.SettingsManager.Instance.settingsGO;
			_frame = settings.transform.Find("Graphics Options/Resolution/Dropdown/Template").GetComponent<Image>();
			_button = settings.transform.Find("Gameplay Options/Reset Tutorials Button");
			_input = settings.transform.Find("Gameplay Options/Misc/Log Limit/Log Limit Text Field");
			TMP_Text native = inGame ? PlayerUI.Instance.GetComponentInChildren<TMP_Text>(true) : MainMenuUI.Instance.GetComponentInChildren<TMP_Text>(true);
			if (_frame == null || _frame.sprite == null || _frame.sprite.name != "button_minion_action_default" || _frame.type != Image.Type.Sliced || _frame.sprite.border.sqrMagnitude <= 0
				|| _button == null || _button.GetComponent<Button>() == null || _input == null || _input.GetComponent<TMP_InputField>() == null || native == null || native.font == null)
				throw new InvalidOperationException("The qualified native atlas frame/button/input/font templates are unavailable.");
			Font = native.font; FontMaterial = native.fontSharedMaterial;
			var scroll = settings.transform.Find("Graphics Options/Resolution/Dropdown/Template/Scrollbar");
			_scrollBackground = scroll.GetComponent<Image>().sprite; _scrollHandle = scroll.Find("Sliding Area/Handle").GetComponent<Image>().sprite;
			_templates = Node("TruePlanet native templates", owner); _templates.SetActive(false);
		}
		internal static GameObject Node(string name, Transform parent)
		{
			var go = new GameObject(name, typeof(RectTransform)); go.layer = LayerMask.NameToLayer("UI"); go.transform.SetParent(parent, false); return go;
		}
		internal GameObject Panel(string name, Transform parent)
		{
			var go = Node(name, parent); var fill = go.AddComponent<Image>(); fill.sprite = _frame.sprite; fill.type = Image.Type.Sliced;
			fill.color = new Color(_frame.color.r, _frame.color.g, _frame.color.b, .78f);
			var trim = Node("Native frame trim", go.transform); Stretch(trim); var edge = trim.AddComponent<Image>();
			edge.sprite = _frame.sprite; edge.type = Image.Type.Sliced; edge.fillCenter = false; edge.color = _frame.color; edge.raycastTarget = false;
			go.AddComponent<AtlasPanelInput>(); return go;
		}
		private GameObject Copy(Transform source, Transform parent, string name)
		{
			var copy = UnityEngine.Object.Instantiate(source.gameObject, _templates.transform, false); copy.name = name;
			foreach (Transform child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = LayerMask.NameToLayer("UI");
			foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true))
			{
				bool known = script.GetType() == typeof(Image) || script.GetType() == typeof(TextMeshProUGUI) || script.GetType() == typeof(RuinarchText)
					|| script.GetType() == typeof(Button) || script is Ruinarch.Custom_UI.RuinarchButton || script.GetType() == typeof(TMP_InputField)
					|| script is LayoutGroup || script is LayoutElement || script is ContentSizeFitter || script.GetType() == typeof(Mask) || script.GetType() == typeof(RectMask2D);
				if (!known) UnityEngine.Object.DestroyImmediate(script);
			}
			// Replace native selectable subclasses; their OnEnable/hover/right-click paths
			// broadcast into the game's managers even after UnityEvents are cleared.
			foreach (var native in copy.GetComponentsInChildren<Ruinarch.Custom_UI.RuinarchButton>(true))
			{
				GameObject go = native.gameObject; var target = native.targetGraphic; var colours = native.colors; var sprites = native.spriteState; var transition = native.transition;
				UnityEngine.Object.DestroyImmediate(native); var plain = go.AddComponent<Button>();
				plain.targetGraphic = target; plain.colors = colours; plain.spriteState = sprites; plain.transition = transition;
			}
			foreach (var native in copy.GetComponentsInChildren<RuinarchText>(true))
			{
				GameObject go = native.gameObject; string text = native.text; float size = native.fontSize;
				var alignment = native.alignment; var margin = native.margin;
				var input = copy.GetComponent<TMP_InputField>();
				bool content = input != null && input.textComponent == native, placeholder = input != null && input.placeholder == native;
				UnityEngine.Object.DestroyImmediate(native); var plain = go.AddComponent<TextMeshProUGUI>();
				plain.text = text; plain.fontSize = size; plain.alignment = alignment; plain.margin = margin;
				if (content) input.textComponent = plain; if (placeholder) input.placeholder = plain;
			}
			foreach (var text in copy.GetComponentsInChildren<TMP_Text>(true))
			{
				text.font = Font; text.fontSharedMaterial = FontMaterial; text.richText = false; text.raycastTarget = false; text.color = new Color32(238, 224, 196, 255);
			}
			foreach (Button button in copy.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
			copy.transform.SetParent(parent, false); return copy;
		}
		internal Button Button(string caption, Transform parent, Action action)
		{
			GameObject copy = Copy(_button, parent, caption); var button = copy.GetComponent<Button>();
			button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(() => action()); button.interactable = true;
			button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
			TMP_Text label = copy.GetComponentInChildren<TMP_Text>(true); label.text = caption; label.fontSize = 18; label.enableWordWrapping = false;
			Stretch(label.gameObject); label.rectTransform.offsetMin = new Vector2(8, 0); label.rectTransform.offsetMax = new Vector2(-8, 0);
			label.margin = Vector4.zero; label.alignment = TextAlignmentOptions.Midline;
			var layout = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>(); layout.preferredHeight = 44; layout.minHeight = 44;
			copy.SetActive(true); return button;
		}
		internal TMP_InputField Input(string caption, Transform parent, string value)
		{
			GameObject copy = Copy(_input, parent, caption); var input = copy.GetComponent<TMP_InputField>();
			input.textViewport = (RectTransform)copy.transform.Find("Text Area");
			input.textComponent = copy.transform.Find("Text Area/Text").GetComponent<TextMeshProUGUI>();
			input.placeholder = copy.transform.Find("Text Area/Placeholder").GetComponent<TextMeshProUGUI>();
			input.onValueChanged = new TMP_InputField.OnChangeEvent(); input.onEndEdit = new TMP_InputField.SubmitEvent(); input.onSubmit = new TMP_InputField.SubmitEvent();
			input.onSelect = new TMP_InputField.SelectionEvent(); input.onDeselect = new TMP_InputField.SelectionEvent();
			input.onTextSelection = new TMP_InputField.TextSelectionEvent(); input.onEndTextSelection = new TMP_InputField.TextSelectionEvent();
			input.onValidateInput = null; input.inputValidator = null; input.contentType = TMP_InputField.ContentType.Standard;
			input.characterValidation = TMP_InputField.CharacterValidation.None; input.lineType = TMP_InputField.LineType.SingleLine;
			input.characterLimit = 0; input.readOnly = false; input.interactable = true; input.textComponent.fontSize = 18;
			if (input.placeholder is TMP_Text placeholder) { placeholder.text = caption; placeholder.fontSize = 18; }
			input.SetTextWithoutNotify(value);
			var layout = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>(); layout.preferredHeight = 44; layout.minHeight = 44;
			copy.SetActive(true); return input;
		}
		internal Transform Scroll(Transform parent)
		{
			var go = Node("Native scroll body", parent); Stretch(go); var scroll = go.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 28;
			scroll.movementType = ScrollRect.MovementType.Clamped; scroll.onValueChanged = new ScrollRect.ScrollRectEvent();
			var view = Node("Viewport", go.transform); Stretch(view); ((RectTransform)view.transform).offsetMax = new Vector2(-18, 0);
			var hit = view.AddComponent<Image>(); hit.color = Color.clear; view.AddComponent<RectMask2D>();
			var content = Node("Content", view.transform); var rect = (RectTransform)content.transform;
			rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.offsetMin = rect.offsetMax = Vector2.zero;
			var layout = content.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
			content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.viewport = (RectTransform)view.transform; scroll.content = rect;
			var bar = Node("Native scrollbar", go.transform); var barRect = (RectTransform)bar.transform;
			barRect.anchorMin = new Vector2(1, 0); barRect.anchorMax = Vector2.one; barRect.pivot = Vector2.one; barRect.offsetMin = new Vector2(-14, 0); barRect.offsetMax = Vector2.zero;
			var background = bar.AddComponent<Image>(); background.sprite = _scrollBackground; background.type = Image.Type.Sliced;
			var handle = Node("Handle", bar.transform); Stretch(handle); var art = handle.AddComponent<Image>(); art.sprite = _scrollHandle; art.type = Image.Type.Sliced;
			var scrollbar = bar.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.handleRect = (RectTransform)handle.transform; scrollbar.targetGraphic = art;
			scrollbar.onValueChanged = new Scrollbar.ScrollEvent(); scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
			return content.transform;
		}
		private static void Stretch(GameObject go)
		{
			var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
		}
		public void Dispose() { if (_templates != null) UnityEngine.Object.Destroy(_templates); }
	}
	internal sealed class AtlasPanelInput : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
	{
		private CanvasGroup _opacity;
		private Coroutine _fade;
		internal void Show()
		{
			if (_opacity == null) _opacity = gameObject.AddComponent<CanvasGroup>();
			_opacity.alpha = 0; gameObject.SetActive(true); FadeTo(1, null);
		}
		internal void Hide(Action finished) => FadeTo(0, finished);
		private void FadeTo(float target, Action finished)
		{
			if (_opacity == null) _opacity = gameObject.AddComponent<CanvasGroup>();
			if (_fade != null) StopCoroutine(_fade);
			_fade = StartCoroutine(Fade(target, finished));
		}
		private System.Collections.IEnumerator Fade(float target, Action finished)
		{
			float from = _opacity.alpha, elapsed = 0;
			while (elapsed < .12f)
			{
				elapsed += Time.unscaledDeltaTime; _opacity.alpha = Mathf.Lerp(from, target, elapsed / .12f);
				yield return null;
			}
			_opacity.alpha = target; _fade = null;
			if (target == 0) gameObject.SetActive(false);
			finished?.Invoke();
		}
		private void OnDisable() { _fade = null; }
		public void OnBeginDrag(PointerEventData data) { }
		public void OnDrag(PointerEventData data) { }
		public void OnEndDrag(PointerEventData data) { }
		public void OnScroll(PointerEventData data) { }
	}
}
