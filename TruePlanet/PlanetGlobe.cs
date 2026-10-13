using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TruePlanet
{
	internal sealed class PlanetGlobe : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
	{
		private const float Radius = 128;
		private PlanetSurface _surface;
		private Action<int> _picked;
		private RectTransform _viewport;
		private RawImage _image;
		private GameObject _root;
		private Camera _camera;
		private Transform _sphere;
		private Mesh _mesh;
		private Material _material;
		private RenderTexture _target;
		private GlobeOverlays _overlays;
		private bool _dirty = true, _renderDirty = true, _dragged, _dragging;
		private float _zoom = 1, _dragClickTime;
		private int _dragPointer;
		private Vector2 _dragStart;
		private Quaternion _dragRotation;
		internal event Action<Exception> Failed;

		internal void Configure(PlanetSurface surface, TMP_FontAsset font, Material fontMaterial, Action<int> picked)
		{
			_surface = surface; _picked = picked; _viewport = (RectTransform)transform;
			_image = GetComponent<RawImage>(); _image.enabled = false;
			_surface.Changed += SurfaceChanged;
			try
			{
				Shader shader = Shader.Find("Universal Render Pipeline/Lit");
				if (shader == null || !shader.isSupported) throw new InvalidOperationException("The native opaque globe shader Universal Render Pipeline/Lit is unavailable.");
				_root = new GameObject("TruePlanet globe preview"); _root.transform.position = new Vector3(30000, -30000, 30000);
				var cameraObject = new GameObject("TruePlanet globe camera"); cameraObject.transform.SetParent(_root.transform, false);
				_camera = cameraObject.AddComponent<Camera>(); _camera.enabled = false; _camera.tag = "Untagged";
				_camera.orthographic = true; _camera.orthographicSize = Radius / .85f;
				_camera.cullingMask = 1 << 31; _camera.nearClipPlane = Radius * .1f; _camera.farClipPlane = Radius * 8;
				_camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.clear;
				cameraObject.transform.localPosition = new Vector3(0, 0, -3.5f * Radius);
				var sphereObject = new GameObject("TruePlanet globe sphere", typeof(MeshFilter), typeof(MeshRenderer)); sphereObject.layer = 31;
				_sphere = sphereObject.transform; _sphere.SetParent(_root.transform, false); _sphere.localScale = Vector3.one * Radius;
				_mesh = GlobeProjection.BuildMesh(64, 128); sphereObject.GetComponent<MeshFilter>().sharedMesh = _mesh;
				_material = new Material(shader) { name = "TruePlanet globe material", renderQueue = 2000 };
				_material.SetColor("_BaseColor", Color.white); _material.SetFloat("_Surface", 0); _material.SetFloat("_AlphaClip", 0);
				_material.SetFloat("_Cull", 2); _material.SetFloat("_SrcBlend", 1); _material.SetFloat("_DstBlend", 0); _material.SetFloat("_ZWrite", 1);
				_material.SetFloat("_Metallic", 0); _material.SetFloat("_Smoothness", 0); _material.SetOverrideTag("RenderType", "Opaque");
				_material.EnableKeyword("_EMISSION"); _material.SetColor("_EmissionColor", Color.white);
				sphereObject.GetComponent<MeshRenderer>().sharedMaterial = _material;
				var overlay = new GameObject("TruePlanet globe overlays", typeof(RectTransform)); overlay.layer = LayerMask.NameToLayer("UI"); overlay.transform.SetParent(transform, false);
				var rect = (RectTransform)overlay.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
				_overlays = overlay.AddComponent<GlobeOverlays>(); _overlays.Configure(surface, _camera, _sphere, _viewport, font, fontMaterial);
				SurfaceChanged();
			}
			catch { ReleaseOwned(); _surface.Changed -= SurfaceChanged; throw; }
		}
		internal void Refresh(int selected, bool borders, bool roads, bool grid)
		{
			_overlays.Refresh(selected, borders, roads, grid); _dirty = true;
		}
		internal void SetVisible(bool visible)
		{
			if (_root != null) _root.SetActive(visible);
			gameObject.SetActive(visible); if (visible) _dirty = true;
		}
		internal void ResetView()
		{
			_zoom = 1; if (_sphere != null) _sphere.localRotation = Quaternion.identity; _dirty = _renderDirty = true;
		}
		private void SurfaceChanged()
		{
			if (_material == null) return;
			_image.enabled = _surface.Ready;
			_overlays.gameObject.SetActive(_surface.Ready);
			if (_surface.Ready)
			{
				_material.SetTexture("_BaseMap", _surface.Texture); _material.SetTexture("_EmissionMap", _surface.Texture);
			}
			_dirty = _renderDirty = true;
		}
		private void OnEnable() { _dirty = _renderDirty = true; }
		private void OnDisable() { _dragging = false; }
		private void OnRectTransformDimensionsChange() { _dirty = _renderDirty = true; }
		private void LateUpdate()
		{
			if (!_dirty || _camera == null || !_surface.Ready) return;
			try
			{
				if (!EnsureTarget()) return;
				_camera.orthographicSize = Radius / (.85f * _zoom);
				if (_renderDirty) _camera.Render();
				_overlays.Project(_zoom); _dirty = _renderDirty = false;
			}
			catch (Exception error) { ReleaseOwned(); enabled = false; Failed?.Invoke(error); }
		}
		private bool EnsureTarget()
		{
			Rect bounds = _viewport.rect;
			Vector2 low = RectTransformUtility.WorldToScreenPoint(null, _viewport.TransformPoint(bounds.min));
			Vector2 high = RectTransformUtility.WorldToScreenPoint(null, _viewport.TransformPoint(bounds.max));
			float width = Mathf.Abs(high.x - low.x), height = Mathf.Abs(high.y - low.y);
			if (width < 1 || height < 1) return false;
			float scale = Mathf.Min(1, 2048 / Mathf.Max(width, height));
			int w = Mathf.Max(1, Mathf.RoundToInt(width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
			if (_target != null && _target.width == w && _target.height == h) return true;
			ReleaseTarget();
			_target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "TruePlanet globe target", filterMode = FilterMode.Bilinear };
			_target.Create(); if (!_target.IsCreated()) throw new InvalidOperationException("Cannot create the globe render target.");
			_camera.targetTexture = _target; _camera.aspect = (float)w / h; _image.texture = _target;
			_renderDirty = true;
			return true;
		}
		public void OnBeginDrag(PointerEventData data)
		{
			if (_sphere == null || !_surface.Ready || data.button != PointerEventData.InputButton.Left) return;
			_dragging = true; _dragStart = data.position; _dragRotation = _sphere.localRotation; _dragged = false;
			_dragClickTime = data.clickTime; _dragPointer = data.pointerId;
		}
		public void OnDrag(PointerEventData data)
		{
			if (!_dragging || _sphere == null || !_surface.Ready || data.button != PointerEventData.InputButton.Left) return;
			Vector2 delta = data.position - _dragStart; if (!_dragged && delta.sqrMagnitude < 16) return;
			_dragged = true;
			_sphere.localRotation = (Quaternion.Euler(delta.y * .3f, -delta.x * .3f, 0) * _dragRotation).normalized; _dirty = _renderDirty = true;
		}
		public void OnEndDrag(PointerEventData data) { _dragging = false; }
		public void OnScroll(PointerEventData data)
		{
			if (_camera == null || !_surface.Ready) return;
			float next = Mathf.Clamp(_zoom * Mathf.Pow(1.2f, data.scrollDelta.y), 1, 6);
			if (next == _zoom) return; _zoom = next; _dirty = _renderDirty = true;
		}
		public void OnPointerClick(PointerEventData data)
		{
			if (_camera == null || !_surface.Ready || data.button != PointerEventData.InputButton.Left) return;
			if (_dragged && data.pointerId == _dragPointer && data.clickTime == _dragClickTime) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, data.position, data.pressEventCamera, out Vector2 point)) return;
			Rect bounds = _viewport.rect; if (!bounds.Contains(point)) return;
			Ray ray = _camera.ViewportPointToRay(new Vector3((point.x - bounds.xMin) / bounds.width, (point.y - bounds.yMin) / bounds.height, 0));
			if (GlobeProjection.TryDirection(ray, _sphere.position, _sphere.rotation, Radius, out UnitVector direction)
				&& _surface.TryPick(direction, out int selected)) _picked?.Invoke(selected);
		}
		private void ReleaseTarget()
		{
			if (_image != null) _image.texture = null;
			if (_camera != null) _camera.targetTexture = null;
			if (_target != null) { _target.Release(); Destroy(_target); _target = null; }
		}
		private void ReleaseOwned()
		{
			ReleaseTarget();
			if (_image != null) _image.enabled = false;
			if (_overlays != null) { Destroy(_overlays.gameObject); _overlays = null; }
			if (_root != null) { Destroy(_root); _root = null; }
			if (_material != null) { Destroy(_material); _material = null; }
			if (_mesh != null) { Destroy(_mesh); _mesh = null; }
			_camera = null; _sphere = null;
		}
		private void OnDestroy()
		{
			if (_surface != null) _surface.Changed -= SurfaceChanged;
			ReleaseOwned();
		}
	}
}
