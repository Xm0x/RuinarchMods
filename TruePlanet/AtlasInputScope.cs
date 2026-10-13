using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ruinarch;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TruePlanet
{
	internal sealed class AtlasInputScope : IDisposable
	{
		private static readonly FieldInfo Shortcuts = AccessTools.Field(typeof(InputManager), "_allowedShortcuts");
		private static readonly FieldInfo Movement = AccessTools.Field(typeof(BaseCameraMove), "isMovementDisabled");
		private readonly InputManager _input;
		private readonly InnerMapCameraMove _camera;
		private readonly Dictionary<SHORTCUT_ACTION, bool> _states;
		private readonly int _inputScene, _cameraScene;
		private readonly bool _movementDisabled;
		private bool _disposed;
		internal AtlasInputScope()
		{
			_input = InputManager.Instance; _camera = InnerMapCameraMove.Instance;
			if (_input != null)
			{
				var shortcuts = Shortcuts.GetValue(_input) as Dictionary<SHORTCUT_ACTION, bool>;
				if (shortcuts == null) throw new InvalidOperationException("Native shortcut state is not initialized.");
				_states = new Dictionary<SHORTCUT_ACTION, bool>(shortcuts); _inputScene = _input.gameObject.scene.handle;
			}
			if (_camera != null) { _movementDisabled = (bool)Movement.GetValue(_camera); _cameraScene = _camera.gameObject.scene.handle; }
			try
			{
				if (_input != null) foreach (var shortcut in _states) _input.SetSpecificHotkeyEnabledState(shortcut.Key, false);
				if (_camera != null) _camera.DisableMovement();
			}
			catch { Dispose(); throw; }
		}
		public void Dispose()
		{
			if (_disposed) return; _disposed = true;
			if (_input != null && _input == InputManager.Instance && _input.gameObject.scene.handle == _inputScene)
				foreach (var shortcut in _states) _input.SetSpecificHotkeyEnabledState(shortcut.Key, shortcut.Value);
			if (_camera != null && _camera == InnerMapCameraMove.Instance && _camera.gameObject.scene.handle == _cameraScene)
			{
				if (_movementDisabled) _camera.DisableMovement(); else _camera.EnableMovement();
			}
		}
		internal static void Install(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(UIManager), nameof(UIManager.IsMouseOnUI)), postfix: new HarmonyMethod(typeof(AtlasInputScope), nameof(MouseOnUi)));
			// Native right-click cancels spells even when IsMouseOnUI is true, and
			// Toggle_Console bypasses the shortcut dictionary. Gate the actual dispatcher
			// while this modal is open; uGUI input remains entirely independent.
			harmony.Patch(AccessTools.Method(typeof(InputManager), "TryExecuteShortcutKeyDownAction"), prefix: new HarmonyMethod(typeof(AtlasInputScope), nameof(WorldAction)));
		}
		private static void MouseOnUi(ref bool __result) { if (PlanetView.Instance != null) __result = true; }
		private static bool WorldAction() => PlanetView.Instance == null;
	}
}
