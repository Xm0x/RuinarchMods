using System;
using System.Collections.Generic;
using System.Linq;
using Inner_Maps;
using UnityEngine;
using HarmonyLib;

namespace RuinarchDebug
{
	// Persistent IMGUI overlay. Every action null-guards and try/catches so a bad
	// game state can never crash Ruinarch. Actions no-op until a world is running.
	public class DebugMenu : MonoBehaviour
	{
		private static DebugMenu _inst;

		public static void Bootstrap()
		{
			if (_inst != null)
			{
				return;
			}
			var go = new GameObject("RuinarchDebugMenu");
			DontDestroyOnLoad(go);
			_inst = go.AddComponent<DebugMenu>();
		}

		// True when the cursor is over the always-visible toggle button or (when
		// open) the debug window. The game's UIManager.IsMouseOnUI() is postfixed
		// with this so world clicks and hover under our IMGUI overlay are ignored.
		internal static bool IsPointerOverDebugUI()
		{
			var i = _inst;
			if (i == null)
			{
				return false;
			}
			Vector3 mp = Input.mousePosition;
			var p = new Vector2(mp.x, Screen.height - mp.y);
			if (new Rect(4f, 4f, 96f, 26f).Contains(p))
			{
				return true;
			}
			return i._open && i._win.Contains(p);
		}

		private bool _open;
		private Rect _win = new Rect(16f, 40f, 340f, 560f);
		private Vector2 _scroll;
		private string _trait = "Injured";
		private int _summonIndex = 1; // Wolf (0 = None)
		private SUMMON_TYPE[] _summons;
		private STRUCTURE_TYPE[] _structs;
		private int _structIndex;

		private void Awake()
		{
			_summons = (SUMMON_TYPE[])Enum.GetValues(typeof(SUMMON_TYPE));
			_structs = ((STRUCTURE_TYPE[])Enum.GetValues(typeof(STRUCTURE_TYPE)))
				.Where(HasStructureClass).ToArray();
		}

		private void OnGUI()
		{
			// Always-visible toggle so no keybind/Input-System dependency.
			if (GUI.Button(new Rect(4f, 4f, 96f, 26f), _open ? "DBG X" : "RUIN DBG"))
			{
				_open = !_open;
			}
			if (_open)
			{
				_win = GUILayout.Window(0x51D6B, _win, DrawWindow, "Ruinarch Debug");
			}
		}

		private bool InGame => GameManager.Instance != null && GameManager.Instance.gameHasStarted;

		private Character Selected()
		{
			try
			{
				return UIManager.Instance != null ? (UIManager.Instance.GetCurrentlySelectedPOI() as Character) : null;
			}
			catch
			{
				return null;
			}
		}

		private LocationGridTile SpawnTile(Character sel)
		{
			try
			{
				var im = InnerMapManager.Instance;
				if (im == null)
				{
					return null;
				}
				if (sel != null && sel.gridTileLocation != null)
				{
					return sel.gridTileLocation;
				}
				var t = im.GetTileFromMousePosition();
				if (t != null)
				{
					return t;
				}
				var map = im.currentlyShowingMap;
				if (map != null && map.allTiles != null && map.allTiles.Count > 0)
				{
					return map.allTiles[0];
				}
			}
			catch
			{
			}
			return null;
		}

		private void DrawWindow(int id)
		{
			_scroll = GUILayout.BeginScrollView(_scroll);

			if (!InGame)
			{
				GUILayout.Label("Enter a world to use the actions below.");
			}

			GUILayout.Label("-- Time --");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Pause")) Safe(() => GameManager.Instance.SetPausedState(true));
			if (GUILayout.Button("1x")) Safe(() => UIManager.Instance.SetProgressionSpeed1X());
			if (GUILayout.Button("2x")) Safe(() => UIManager.Instance.SetProgressionSpeed2X());
			if (GUILayout.Button("4x")) Safe(() => UIManager.Instance.SetProgressionSpeed4X());
			GUILayout.EndHorizontal();

			var c = Selected();
			GUILayout.Label("-- Selected character --");
			if (c == null)
			{
				GUILayout.Label("(click a character in-game to select)");
			}
			else
			{
				GUILayout.Label(c.name + "   HP " + c.currentHP + "/" + c.maxHP);
				try
				{
					GUILayout.Label("Fullness " + Mathf.RoundToInt(c.needsComponent.fullness) +
						"   Energy " + Mathf.RoundToInt(c.needsComponent.tiredness));
				}
				catch
				{
				}
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Kill")) Safe(() => c.Death("debug"));
				if (GUILayout.Button("Full Heal")) Safe(() => c.AdjustHP(c.maxHP, ELEMENTAL_TYPE.Normal));
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Starve (0)")) Safe(() => c.needsComponent.SetFullness(0f));
				if (GUILayout.Button("Feed (100)")) Safe(() => c.needsComponent.SetFullness(100f));
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Exhaust")) Safe(() => c.needsComponent.SetTiredness(0f));
				if (GUILayout.Button("Rest")) Safe(() => c.needsComponent.SetTiredness(100f));
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				_trait = GUILayout.TextField(_trait, GUILayout.Width(170f));
				if (GUILayout.Button("Add Trait")) Safe(() => c.traitContainer.AddTrait(c, _trait));
				GUILayout.EndHorizontal();
			}

			GUILayout.Label("-- Spawn (at selected tile / mouse) --");
			if (GUILayout.Button("Spawn Villager (vagrant)")) Safe(SpawnVillager);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("<", GUILayout.Width(30f)))
			{
				_summonIndex = Mathf.Max(1, _summonIndex - 1);
			}
			GUILayout.Label(_summons[_summonIndex].ToString(), GUILayout.Width(150f));
			if (GUILayout.Button(">", GUILayout.Width(30f)))
			{
				_summonIndex = Mathf.Min(_summons.Length - 1, _summonIndex + 1);
			}
			GUILayout.EndHorizontal();
			if (GUILayout.Button("Spawn " + _summons[_summonIndex]))
			{
				var t = _summons[_summonIndex];
				Safe(() => SpawnSummon(t));
			}

			GUILayout.Label("-- Place building (select a villager first; places at their tile) --");
			if (_structs != null && _structs.Length > 0)
			{
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("<", GUILayout.Width(30f)))
				{
					_structIndex = Mathf.Max(0, _structIndex - 1);
				}
				GUILayout.Label(_structs[_structIndex].ToString(), GUILayout.Width(230f));
				if (GUILayout.Button(">", GUILayout.Width(30f)))
				{
					_structIndex = Mathf.Min(_structs.Length - 1, _structIndex + 1);
				}
				GUILayout.EndHorizontal();
				if (GUILayout.Button("Place " + _structs[_structIndex]))
				{
					var st = _structs[_structIndex];
					Safe(() => PlaceBuilding(st));
				}
			}
			if (GUILayout.Button("Place Mass Grave (village building)"))
			{
				Safe(PlaceMassGrave);
			}

			GUILayout.Label("-- World --");
			if (GUILayout.Button("Open Full Dev Console (70+ cmds)")) Safe(() => UIManager.Instance.ToggleConsole());
			if (GUILayout.Button("Kill ALL villagers")) Safe(KillAllVillagers);

			GUILayout.Space(6f);
			GUILayout.Label("Console examples: /kill  /gain_summon Dragon\n/set_fullness name 0  /reveal_all  /coins 9999");

			GUILayout.EndScrollView();
			GUI.DragWindow(new Rect(0f, 0f, 100000f, 22f));
		}

		// Only STRUCTURE_TYPE values with a matching class under
		// Inner_Maps.Location_Structures can be instantiated; mirror the game's own
		// reflection so the selector never offers a type that would throw.
		private static readonly System.Reflection.Assembly GameAsm = typeof(LocationGridTile).Assembly;

		private static bool HasStructureClass(STRUCTURE_TYPE t)
		{
			try
			{
				string cls = string.Concat(t.ToString().Split('_')
					.Select(p => p.Length == 0 ? string.Empty : char.ToUpper(p[0]) + p.Substring(1).ToLower()));
				return GameAsm.GetType("Inner_Maps.Location_Structures." + cls) != null;
			}
			catch
			{
				return false;
			}
		}

		private void PlaceBuilding(STRUCTURE_TYPE type)
		{
			var tile = SpawnTile(Selected());
			if (tile == null)
			{
				RuinarchDebug.Log?.Info("Place building: select a villager inside a village first (click a character), then click Place.");
				return;
			}
			var area = tile.area;
			var settlement = area != null ? area.GetFirstNPCSettlementOnArea() : null;
			if (settlement == null)
			{
				RuinarchDebug.Log?.Info("Place building: the selected tile is not inside a village. Select a villager who lives in a settlement.");
				return;
			}
			var structure = LandmarkManager.Instance.CreateNewStructureAt(area.region, type, settlement);
			int assigned = 0;
			tile.SetStructure(structure);
			assigned++;
			if (tile.neighbourList != null)
			{
				foreach (var n in tile.neighbourList)
				{
					if (n != null && n.area == area)
					{
						n.SetStructure(structure);
						assigned++;
					}
				}
			}
			RuinarchDebug.Log?.Info($"Placed {type} on {assigned} tile(s) in a village.");
		}

		// Places the mod's registered non-demonic Mass Grave (via its framework-allocated
		// virtual STRUCTURE_TYPE). Requires RuinarchPlus (which registers it) to be enabled.
		private void PlaceMassGrave()
		{
			var t = Ruinarch.ModContent.ModContent.StructureTypeFor("ruinarch.plus.mass_grave");
			if ((int)t == 0)
			{
				RuinarchDebug.Log?.Info("Mass Grave not registered - is RuinarchPlus enabled?");
				return;
			}
			PlaceBuilding(t);
		}

		private void SpawnVillager()
		{
			var tile = SpawnTile(Selected());
			if (tile == null)
			{
				return;
			}
			var c = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
				homeRegion: InnerMapManager.Instance.currentlyShowingLocation,
				faction: FactionManager.Instance.vagrantFaction,
				homeLocation: null, homeStructure: null, randomizeTraits: true);
			c.CreateMarker();
			c.InitialCharacterPlacement(tile);
			c.marker.UpdatePosition();
			RuinarchDebug.Log?.Info("Spawned villager " + c.name);
		}

		private void SpawnSummon(SUMMON_TYPE type)
		{
			var tile = SpawnTile(Selected());
			if (tile == null)
			{
				return;
			}
			var s = CharacterManager.Instance.CreateNewSummon(type, null,
				homeLocation: null, homeRegion: InnerMapManager.Instance.currentlyShowingLocation,
				homeStructure: null, className: "", bypassIdeologyChecking: true);
			s.CreateMarker();
			s.InitialCharacterPlacement(tile);
			s.marker.UpdatePosition();
			RuinarchDebug.Log?.Info("Spawned summon " + type);
		}

		private void KillAllVillagers()
		{
			var snapshot = new List<Character>(CharacterManager.Instance.allCharacters);
			int n = 0;
			for (int i = 0; i < snapshot.Count; i++)
			{
				var ch = snapshot[i];
				if (ch != null && !ch.isDead && ch.isNormalCharacter)
				{
					ch.Death("debug");
					n++;
				}
			}
			RuinarchDebug.Log?.Info("Killed " + n + " villagers");
		}

		private void Safe(Action a)
		{
			if (!InGame)
			{
				return;
			}
			try
			{
				a();
			}
			catch (Exception e)
			{
				RuinarchDebug.Log?.Info("Debug action failed: " + e.Message);
			}
		}
	}

	// Suppress world click-through under the IMGUI overlay: when the cursor is
	// over our toggle button or window, report the mouse as over UI so
	// InnerMapManager.OnClickMapObject() bails out at its IsMouseOnUI() gate.
	[HarmonyPatch(typeof(UIManager), "IsMouseOnUI")]
	internal static class UIManager_IsMouseOnUI_Patch
	{
		private static void Postfix(ref bool __result)
		{
			if (!__result && DebugMenu.IsPointerOverDebugUI())
			{
				__result = true;
			}
		}
	}
}
