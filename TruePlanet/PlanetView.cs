using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace TruePlanet
{
	public sealed class PlanetView : MonoBehaviour
	{
		internal static PlanetView Instance;
		private static readonly System.Random Seeds = new System.Random();
		private Planet _planet;
		private PlanetCanvas _map;
		private PlanetSurface _surface;
		private int _selected = -1;
		private bool _political, _borders = true, _roads = true, _grid;
		private PlanetGlobe _globe;
		private bool _flat;
		private TextMeshProUGUI _modeLabel;
		private TMP_FontAsset _font;
		private Material _fontMaterial;
		private TMP_InputField _seed, _count, _land, _nations, _wild, _name;
		private TextMeshProUGUI _status, _inspector, _sizeLabel, _layerLabel, _resolutionLabel, _gridLabel, _heading;
		private ProvinceSize _largest;
		private LocalMapSize _localSize;
		private TileResolution _resolution;
		private Transform _saved;
		private GameObject _advanced, _savedPanel, _worldPanel, _layersPanel, _provincePanel, _actions;
		private Button _save, _play, _generate, _back;
		private TextMeshProUGUI _worldTitle;
		private GlobeSkin _skin;
		private AtlasInputScope _inputScope;
		private bool _inGame;
		private AtlasProgress _progress;
		private bool _generating, _drawFinished;
		private int _terrainTile = -1;
		private LocalTerrainPlan _terrainPlan;
		private Button[] _generationControls;
		private TMP_InputField[] _inputs;
		private bool _controlsReady = true;
		private static readonly Color Background = new Color32(17, 16, 23, 255);
		internal static void Open()
		{
			if (Instance != null)
			{
				if (Instance.gameObject.activeSelf) return;
				if (Instance._inGame && TileGame.Active != null && ReferenceEquals(Instance._planet, TileGame.Active.Planet))
				{
					Instance._inputScope = new AtlasInputScope();
					Instance.gameObject.SetActive(true);
					Instance.Inspect(Instance._selected >= 0 ? Instance._selected : TileGame.Active.TileId);
					return;
				}
				Destroy(Instance.gameObject); Instance = null;
			}
			var root = new GameObject("TruePlanet atlas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			root.layer = LayerMask.NameToLayer("UI");
			var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2600;
			var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
			root.AddComponent<Image>().color = Background;
			Instance = root.AddComponent<PlanetView>();
			try { Instance.Build(); }
			catch (Exception e)
			{
				Instance._inputScope?.Dispose();
				TruePlanetMod.Log.Warning("Cannot open the planet atlas: " + e); Destroy(root);
			}
		}
		private void Build()
		{
			_inGame = TileGame.Active != null && GameManager.Instance != null && GameManager.Instance.gameHasStarted;
			_inputScope = new AtlasInputScope();
			_skin = new GlobeSkin(transform, _inGame); _font = _skin.Font; _fontMaterial = _skin.FontMaterial;
			_surface = gameObject.AddComponent<PlanetSurface>();
			var mapArea = New("Planet viewport", transform); Stretch(mapArea); mapArea.AddComponent<RectMask2D>();
			var map = New("Planet map", mapArea.transform); Stretch(map);
			var fit = map.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 2;
			map.AddComponent<RawImage>().color = Color.white; _map = map.AddComponent<PlanetCanvas>(); _map.Configure(_surface, _font, _fontMaterial, Inspect);
			var globe = New("Planet globe", mapArea.transform); Stretch(globe); globe.AddComponent<RawImage>().color = Color.white;
			_globe = globe.AddComponent<PlanetGlobe>(); _globe.Configure(_surface, _font, _fontMaterial, Inspect);

			var header = _skin.Panel("World header", transform); At(header, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -136), new Vector2(450, -24));
			var title = Label(header.transform, _inGame ? "TruePlanet / Campaign" : "TruePlanet / New Game", 19);
			At(title.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(20, -44), new Vector2(-20, -12));
			_worldTitle = Label(header.transform, "", 26); At(_worldTitle.gameObject, Vector2.zero, Vector2.one, new Vector2(20, 18), new Vector2(-20, -42));
			_worldTitle.overflowMode = TextOverflowModes.Ellipsis;
			var back = _back = Button(transform, "Back", Close); At(back.gameObject, new Vector2(1, 1), Vector2.one, new Vector2(-154, -70), new Vector2(-24, -24));

			var actions = _actions = _skin.Panel("World actions", transform);
			At(actions, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -386), new Vector2(250, -150));
			var actionBody = Column(actions.transform); At(actionBody.gameObject, Vector2.zero, Vector2.one, new Vector2(18, 18), new Vector2(-18, -18));
			Button(actionBody, "World settings", () => ToggleDrawer(_worldPanel));
			_generate = Button(actionBody, "Re-generate", Regenerate);
			_save = Button(actionBody, "Save world", Save); _save.interactable = false;
			Button(actionBody, "Load world", () => { RefreshSaves(); ToggleDrawer(_savedPanel); });
			actions.SetActive(!_inGame);

			_worldPanel = _skin.Panel("World settings drawer", transform);
			At(_worldPanel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(24, 155), new Vector2(470, -150));
			var settingsTitle = Label(_worldPanel.transform, "World settings", 24);
			At(settingsTitle.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(20, -54), new Vector2(-128, -14));
			var settingsClose = Button(_worldPanel.transform, "Close", () => ToggleDrawer(_worldPanel));
			At(settingsClose.gameObject, new Vector2(1, 1), Vector2.one, new Vector2(-115, -56), new Vector2(-20, -12));
			var settingsArea = New("World settings scroll", _worldPanel.transform);
			At(settingsArea, Vector2.zero, Vector2.one, new Vector2(20, 20), new Vector2(-20, -68));
			Transform settings = Scroll(settingsArea.transform);
			PlanetOptions defaults = _inGame ? TileGame.Active.Planet.Options : TruePlanetMod.Settings.Options();
			_largest = defaults.LargestProvince; _localSize = defaults.LocalSize; _resolution = defaults.Resolution;
			Label(settings, "World name", 17); _name = Input(settings, "World name", "");
			_seed = Field(settings, "Seed", "", 205); _seed.onSubmit.AddListener(_ => Generate());
			Button(settings, "Apply name / seed", Generate);
			Button(settings, "Advanced", Advanced);
			var advanced = Column(settings); _advanced = advanced.gameObject;
			advanced.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_count = Field(advanced, "Provinces", defaults.Provinces.ToString(), 205);
			_land = Field(advanced, "Land %", (defaults.LandShare * 100).ToString("0"), 205);
			_nations = Field(advanced, "Nations", defaults.Nations.ToString(), 205);
			_wild = Field(advanced, "Wild %", (defaults.WildShare * 100).ToString("0"), 205);
			var size = Button(advanced, "", () =>
			{
				if (_planet == null || _planet.FormatVersion == 3) { _localSize = (LocalMapSize)(((int)_localSize + 1) % 5); _sizeLabel.text = "Map: " + TilePresentation.MapSize(_localSize); }
				else { _largest = (ProvinceSize)(((int)_largest + 1) % 3); _sizeLabel.text = "Max: " + Size(_largest); }
			});
			_sizeLabel = size.GetComponentInChildren<TextMeshProUGUI>(); _sizeLabel.text = "Map: " + TilePresentation.MapSize(_localSize);
			var detail = Button(advanced, "", () => { _resolution = _resolution == TileResolution.Standard ? TileResolution.Detailed : TileResolution.Standard; _resolutionLabel.text = Detail(_resolution); });
			_resolutionLabel = detail.GetComponentInChildren<TextMeshProUGUI>(); _resolutionLabel.text = Detail(_resolution);
			_advanced.SetActive(false); _worldPanel.SetActive(false);

			var right = _provincePanel = _skin.Panel("Selection inspector", transform);
			At(right, new Vector2(1, 0), Vector2.one, new Vector2(-354, 160), new Vector2(-24, -150));
			_heading = Label(right.transform, TilePresentation.Heading, 23);
			At(_heading.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(20, -54), new Vector2(-20, -14));
			var inspectArea = New("Selection details", right.transform); At(inspectArea, Vector2.zero, Vector2.one, new Vector2(20, 80), new Vector2(-20, -66));
			_inspector = Label(Scroll(inspectArea.transform), "Select land to inspect this world.", 18);
			_inspector.gameObject.GetComponent<LayoutElement>().preferredHeight = -1;
			_inspector.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_play = Button(right.transform, _inGame ? "Travel" : "Play", Play);
			At(_play.gameObject, Vector2.zero, new Vector2(1, 0), new Vector2(20, 20), new Vector2(-20, 64)); _play.interactable = false;

			var controls = _skin.Panel("View controls", transform);
			At(controls, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-250, 85), new Vector2(250, 153));
			var controlBody = Row(controls.transform); At(controlBody.gameObject, Vector2.zero, Vector2.one, new Vector2(18, 12), new Vector2(-18, -12));
			_modeLabel = Button(controlBody, "Globe", () => SetFlat(!_flat)).GetComponentInChildren<TextMeshProUGUI>();
			Button(controlBody, "Layers", () => ToggleDrawer(_layersPanel));
			Button(controlBody, "Reset view", ResetView);
			_layersPanel = _skin.Panel("Layers drawer", transform);
			At(_layersPanel, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-145, 169), new Vector2(145, 467));
			var layers = Column(_layersPanel.transform); At(layers.gameObject, Vector2.zero, Vector2.one, new Vector2(20, 20), new Vector2(-20, -20));
			_layerLabel = Button(layers, "Terrain", () => { _political = !_political; _layerLabel.text = _political ? "Political" : "Terrain"; _surface.SetPolitical(_political); }).GetComponentInChildren<TextMeshProUGUI>();
			Button(layers, "Borders: on", () => Toggle("Borders: ", true));
			Button(layers, "Roads: on", () => Toggle("Roads: ", false));
			_gridLabel = Button(layers, "Grid: off", () => { _grid = !_grid; _gridLabel.text = _grid ? "Grid: on" : "Grid: off"; RefreshViews(); }).GetComponentInChildren<TextMeshProUGUI>();
			var legend = Label(layers, "Gold: capitals / selection. Double lines: national borders. Orange lines: roads.", 15);
			float legendHeight = legend.GetPreferredValues(legend.text, 250, float.PositiveInfinity).y + 8;
			legend.GetComponent<LayoutElement>().preferredHeight = legendHeight;
			At(_layersPanel, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-145, 169), new Vector2(145, 169 + 248 + legendHeight));
			_layersPanel.SetActive(false);

			var statusPanel = _skin.Panel("Atlas status", transform);
			At(statusPanel, Vector2.zero, new Vector2(1, 0), new Vector2(24, 18), new Vector2(-24, 75));
			var statusArea = New("Status scroll", statusPanel.transform); At(statusArea, Vector2.zero, Vector2.one, new Vector2(18, 8), new Vector2(-18, -8));
			_status = Label(Scroll(statusArea.transform), "Drawing world.", 17);
			_status.GetComponent<LayoutElement>().preferredHeight = -1;
			_status.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_savedPanel = _skin.Panel("Saved worlds drawer", transform);
			At(_savedPanel, new Vector2(.22f, .2f), new Vector2(.78f, .8f), Vector2.zero, Vector2.zero);
			var savedTitle = Label(_savedPanel.transform, "Saved worlds", 24);
			At(savedTitle.gameObject, new Vector2(0, 1), Vector2.one, new Vector2(20, -54), new Vector2(-130, -14));
			var savedClose = Button(_savedPanel.transform, "Close", () => ToggleDrawer(_savedPanel));
			At(savedClose.gameObject, new Vector2(1, 1), Vector2.one, new Vector2(-115, -56), new Vector2(-20, -12));
			var saves = New("Saved world list", _savedPanel.transform);
			At(saves, Vector2.zero, Vector2.one, new Vector2(20, 20), new Vector2(-20, -68)); _saved = Scroll(saves.transform); _savedPanel.SetActive(false);

			_generationControls = _actions.GetComponentsInChildren<Button>(true).Concat(_worldPanel.GetComponentsInChildren<Button>(true)).ToArray();
			_inputs = GetComponentsInChildren<TMP_InputField>(true);
			_progress = new AtlasProgress(_skin, transform);
			_surface.Progress = value =>
			{
				SetControlsReady(false);
				if (!_surface.Ready)
				{
					_status.text = _surface.WorkStage;
					_progress.Show(_surface.WorkStage, _surface.Shading ? 5 : 4, 7, _surface.CompletedRows, PlanetSurface.Height);
				}
				else
				{
					if (!_surface.ReusedPreparation) _progress.Show("Finishing the atlas", 6, 7);
				}
			};
			_surface.Failed += error => ShowFailure("Cannot draw world", error);
			_globe.Failed += RenderFailed;
			SetFlat(false);
			if (_inGame) { Display(TileGame.Active.Planet); _selected = TileGame.Active.TileId; RefreshViews(); }
			else Regenerate();
		}
		private void ShowFailure(string operation, Exception error)
		{
			SetControlsReady(true); _save.interactable = _surface.Ready && _drawFinished; _play.interactable = false;
			_progress?.Hide(); _generating = false;
			_status.text = operation + ". Try again or return with Back."; TruePlanetMod.Log.Warning(operation + ": " + error);
		}
		private void RenderFailed(Exception error)
		{
			ShowFailure("Cannot render globe", error);
			// Only Back/Escape remain usable after the renderer releases its resources.
			foreach (Button button in GetComponentsInChildren<Button>(true)) button.interactable = button == _back;
			foreach (TMP_InputField input in GetComponentsInChildren<TMP_InputField>(true)) input.interactable = false;
		}
		private void Advanced() => _advanced.SetActive(!_advanced.activeSelf);
		private void ToggleDrawer(GameObject drawer)
		{
			bool open = !drawer.activeSelf;
			if (_worldPanel != drawer) _worldPanel.SetActive(false);
			if (_layersPanel != drawer) _layersPanel.SetActive(false);
			if (_savedPanel != drawer) _savedPanel.SetActive(false);
			var panel = drawer.GetComponent<AtlasPanelInput>();
			if (open)
			{
				drawer.transform.SetAsLastSibling(); panel.Show();
				_actions.SetActive(!_inGame && !_worldPanel.activeSelf);
			}
			else panel.Hide(() => _actions.SetActive(!_inGame && !_worldPanel.activeSelf));
		}
		private void Update()
		{
			if (!_drawFinished && !_generating && _surface != null && _surface.Ready && (_flat ? _map.Ready : _globe.FrameReady))
			{
				_drawFinished = true; _progress.Hide(); SetControlsReady(true);
				_status.text = "Ready. Select land. Scroll to zoom; drag to " + (_flat ? "pan." : "rotate.");
				if (_inGame) { Inspect(_selected); ResetView(); }
			}
			if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
			if (_savedPanel.activeSelf) { ToggleDrawer(_savedPanel); return; }
			if (_layersPanel.activeSelf) { ToggleDrawer(_layersPanel); return; }
			if (_worldPanel.activeSelf) { ToggleDrawer(_worldPanel); return; }
			Close();
		}
		private void Close()
		{
			_inputScope?.Dispose(); _inputScope = null;
			if (_inGame && _surface.Ready && _globe.enabled) gameObject.SetActive(false);
			else Destroy(gameObject);
		}
		private void ResetView()
		{
			int id = _selected >= 0 ? _selected : _inGame && TileGame.Active != null ? TileGame.Active.TileId : -1;
			if (id < 0 || !_surface.Ready) { _map.ResetView(); _globe.ResetView(); return; }
			UnitVector direction = _planet.FormatVersion == 3 ? TileLand.For(_planet).Cells[id].DryPoint : _planet.Provinces[id].Direction;
			_map.CenterOn(direction); _globe.CenterOn(direction);
		}
		private void Play() => Attempt(() =>
		{
			_inputScope?.Dispose();
			try
			{
				if (_inGame) TileTravel.Instance.SwitchTo(_selected);
				else
				{
					_planet.Name = _name.text.Trim();
					if (_planet.FormatVersion == 3) TileGame.Play(_planet, _selected, null, null);
					else LegacyProvinceGame.Play(_planet, _selected);
				}
				Destroy(gameObject);
			}
			catch { _inputScope = new AtlasInputScope(); throw; }
		});
		private void Toggle(string prefix, bool border)
		{
			bool enabled;
			if (border) enabled = _borders = !_borders; else enabled = _roads = !_roads;
			foreach (TextMeshProUGUI label in GetComponentsInChildren<TextMeshProUGUI>(true)) if (label.text.StartsWith(prefix, StringComparison.Ordinal)) label.text = prefix + (enabled ? "on" : "off");
			RefreshViews();
		}
		private void RefreshViews()
		{
			_map.Refresh(_selected, _borders, _roads, _grid);
			_globe?.Refresh(_selected, _borders, _roads, _grid);
		}
		private void SetFlat(bool flat)
		{
			_flat = flat; _map.SetVisible(flat); _globe.SetVisible(!flat);
			_modeLabel.text = flat ? "Map" : "Globe"; RefreshViews();
		}
		private static int NextSeed(int previous)
		{
			int seed;
			do { seed = Seeds.Next(); } while (seed == previous);
			return seed;
		}
		private void Regenerate()
		{
			if (_generating || !_generate.interactable) return;
			int.TryParse(_seed.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int previous);
			_seed.text = NextSeed(previous).ToString(CultureInfo.InvariantCulture);
			Generate();
		}
		private void Generate()
		{
			if (_generating || !_generate.interactable) return;
			try
			{
				int Integer(TMP_InputField field) => int.Parse(field.text, NumberStyles.Integer, CultureInfo.InvariantCulture);
				var options = new PlanetOptions { Provinces = Integer(_count), LandShare = Integer(_land) / 100.0, Nations = Integer(_nations), WildShare = Integer(_wild) / 100.0, LocalSize = _localSize, Resolution = _resolution };
				options.Validate(); int seed = Integer(_seed); string name = _name.text;
				_generating = true; _drawFinished = false; _surface.Invalidate(); SetControlsReady(false); _play.interactable = false;
				_status.text = _planet == null ? "Generating planet..." : "Re-generating planet...";
				_progress.Show(_status.text, 0, 7);
				StartCoroutine(GenerateAsync(seed, options, name));
			}
			catch (Exception error) { ShowFailure("Cannot generate planet", error); }
		}
		private IEnumerator GenerateAsync(int seed, PlanetOptions options, string name)
		{
			yield return null; // Render the operation state before starting work.
			var operation = new GenerationOperation();
			Task<Planet> task = Task.Run(() => operation.Run(seed, options));
			GenerationProgress previous = null;
			while (!task.IsCompleted)
			{
				GenerationProgress progress = operation.Progress;
				if (!ReferenceEquals(previous, progress))
				{
					previous = progress; _status.text = progress.Stage;
					_progress.Show(progress.Stage, progress.Completed, 7);
				}
				yield return null;
			}
			if (task.IsFaulted) { ShowFailure("Cannot generate planet", task.Exception.GetBaseException()); yield break; }
			try
			{
				Planet planet = task.Result; TruePlanetMod.Name(planet);
				if (!string.IsNullOrWhiteSpace(name) && (_planet == null || name != _planet.Name)) planet.Name = name.Trim();
				_generating = false; Display(planet);
			}
			catch (Exception error) { ShowFailure("Cannot generate planet", error); }
		}
		private void Display(Planet planet)
		{
			_drawFinished = false; _terrainTile = -1;
			_planet = planet; _selected = -1; _terrainPlan = null; _name.text = planet.Name; _map.ResetView(); _globe.ResetView(); RefreshViews(); _surface.Show(planet); _save.interactable = false; _play.interactable = false;
			_worldTitle.text = planet.Name;
			_generate.GetComponentInChildren<TextMeshProUGUI>().text = "Re-generate";
			_seed.text = planet.Seed.ToString(CultureInfo.InvariantCulture); _count.text = planet.Options.Provinces.ToString(); _land.text = (planet.Options.LandShare * 100).ToString("0");
			_nations.text = planet.Options.Nations.ToString(); _wild.text = (planet.Options.WildShare * 100).ToString("0");
			_largest = planet.Options.LargestProvince; _localSize = planet.Options.LocalSize;
			_sizeLabel.text = planet.FormatVersion == 3 ? "Map: " + TilePresentation.MapSize(_localSize) : "Max: " + Size(_largest);
			_resolution = planet.Options.Resolution; _resolutionLabel.text = Detail(_resolution);
			_heading.text = planet.FormatVersion == 3 ? TilePresentation.Heading : "World / Province";
			_inspector.text = planet.Name + "\n\n" + planet.Provinces.Length + " provinces\n" + planet.Nations.Length + " nations\n"
				+ planet.Provinces.Sum(p => p.Villages.Length) + " settlements\n\nSelect land to inspect its biome and settlements.";
			_status.text = _surface.Ready ? "Select land to explore." : "Preparing the atlas...";
		}
		private void Inspect(int id)
		{
			if (_generating || !_surface.Ready) return;
			_selected = id; RefreshViews();
			if (_planet.FormatVersion == 3) { InspectTile(id); return; }
			Province p = _planet.Provinces[id]; var text = new StringBuilder(); text.AppendLine(p.Name + (p.Land ? " / Land" : " / Ocean"));
			text.AppendLine(p.Visited ? "Visited" : "Unvisited"); text.AppendLine(p.Held ? "Player held" : "Not held by the player");
			if (p.Land)
			{
				text.AppendLine("Map size: " + Size(p.Size)); text.AppendLine("Nation: " + (p.Nation >= 0 ? _planet.Nations[p.Nation].Name : "Independent"));
				if (p.Nation >= 0) text.AppendLine(_planet.Nations[p.Nation].Elven ? "Elven" : "Human");
				text.AppendLine("Grass / forest / desert / snow: " + string.Join(" / ", p.Biomes.Select(v => (v * 100).ToString("0") + "%")));
				text.AppendLine("Mountain: " + (p.Mountains * 100).ToString("0") + "% / Water: " + (p.Water * 100).ToString("0") + "%");
				text.AppendLine("Temperature: " + (p.Temperature * 100).ToString("0") + "% / Moisture: " + (p.Moisture * 100).ToString("0") + "%");
				text.AppendLine("\nVillages: " + p.Villages.Length);
				foreach (Village v in p.Villages) text.AppendLine(v.Name + (v.Capital ? " (capital)" : "") + "\n  " + (v.Elven ? "Elven" : "Human") + " / " + (v.Nation < 0 ? "Independent" : _planet.Nations[v.Nation].Name));
				text.AppendLine("Road routes: " + _planet.Roads.Count(r => _planet.TopologyVersion == 1 ? r.Tiles.Any(t => _planet.TileOwners[t] == id) : r.Path.Contains(id)));
			}
			text.AppendLine("\nNeighbours: " + string.Join(", ", p.Neighbours.Select(i => _planet.Provinces[i].Name)));
			_inspector.text = text.ToString(); _play.interactable = p.Land; _status.text = "Selected " + p.Name + (p.Land ? ". Play creates this province; place your Portal normally." : ". Select land to play.");
		}
		private void InspectTile(int tile)
		{
			TileLandSample land = TileLand.For(_planet).Cells[tile];
			TerrainSample sample = _surface.Geography.Sample(land.DryPoint);
			_heading.text = TilePresentation.BiomeName(sample);
			if (_terrainTile != tile)
			{
				_terrainPlan = LocalTerrainPlan.Build(_planet, tile);
				_terrainTile = tile;
			}
            CampaignManifest campaign = _inGame ? TileGame.Campaign : null;
			_inspector.text = TilePresentation.Details(_planet, tile, sample, _terrainPlan, campaign,
                _inGame ? TileGame.Active.TileId : -1, _inGame && TileTravel.HasPortal());
			_play.interactable = land.Eligible && (!_inGame || tile != TileGame.Active.TileId);
			_status.text = TilePresentation.Status(_inGame);
		}
		private void Save() => Attempt(() => { _planet.Name = _name.text.Trim(); string path = PlanetStore.Save(TruePlanetMod.SaveRoot, _planet); _status.text = "Saved " + _planet.Name + "."; TruePlanetMod.Log.Info("Saved world to " + path); RefreshSaves(); });
		private void RefreshSaves()
		{
			for (int i = _saved.childCount - 1; i >= 0; i--) { var child = _saved.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
			foreach (string path in PlanetStore.Files(TruePlanetMod.SaveRoot))
			{
				try { Planet saved = PlanetStore.Load(path); Button(_saved, saved.Name + " / seed " + saved.Seed, () => Attempt(() => { Display(PlanetStore.Load(path)); _savedPanel.SetActive(false); })); }
				catch (Exception e) { Label(_saved, "A saved world could not be read.", 16); TruePlanetMod.Log.Warning("Cannot read saved world " + path + ": " + e); }
			}
			if (_saved.childCount == 0) Label(_saved, "No saved worlds. Generate a world, then Save world.", 16);
		}
		private void SetControlsReady(bool ready)
		{
			if (_controlsReady == ready) return;
			_controlsReady = ready;
			foreach (Button button in _generationControls) button.interactable = ready;
			foreach (TMP_InputField input in _inputs) input.interactable = ready;
		}
		private void Attempt(Action action)
		{
			if (_generating || !_generate.interactable) return;
			try { action(); } catch (Exception e) { _status.text = "That action could not finish. Try again or return with Back."; TruePlanetMod.Log.Warning("Atlas operation failed: " + e); }
		}
		private static string Size(ProvinceSize size) => size == ProvinceSize.Huge ? "Huge (32x20)" : size == ProvinceSize.ExtraLarge ? "Extra Large (24x14)" : "Large (20x12)";
		private static string Detail(TileResolution resolution) => resolution == TileResolution.Standard ? "Tiles: Standard (10,242)" : "Tiles: Detailed (40,962)";
		private GameObject New(string name, Transform parent) => GlobeSkin.Node(name, parent);
		private static void At(GameObject go, Vector2 min, Vector2 max, Vector2 low, Vector2 high) { var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; }
		private static void Stretch(GameObject go) => At(go, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		private TextMeshProUGUI Label(Transform parent, string text, float size = 18)
		{
			var go = New("Label", parent); var label = go.AddComponent<TextMeshProUGUI>(); label.font = _font; label.fontSharedMaterial = _fontMaterial;
			label.text = text; label.fontSize = size; label.color = new Color32(238, 224, 196, 255); label.richText = false; label.enableWordWrapping = true; label.raycastTarget = false; label.alignment = TextAlignmentOptions.TopLeft;
			label.overflowMode = TextOverflowModes.Overflow;
			var layout = go.AddComponent<LayoutElement>(); layout.preferredHeight = label.GetPreferredValues(text).y + 8; layout.minHeight = size + 8; return label;
		}
		private Button Button(Transform parent, string text, Action action) => _skin.Button(text, parent, action);
		private TMP_InputField Input(Transform parent, string name, string value) => _skin.Input(name, parent, value);
		private TMP_InputField Field(Transform parent, string title, string value, float width)
		{
			var column = New(title, parent); var layout = column.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = false;
			var label = Label(column.transform, title, 17); label.gameObject.GetComponent<LayoutElement>().preferredWidth = 125;
			var input = Input(column.transform, title, value); input.contentType = TMP_InputField.ContentType.IntegerNumber; input.gameObject.GetComponent<LayoutElement>().preferredWidth = width;
			var element = column.AddComponent<LayoutElement>(); element.preferredHeight = 44; element.minHeight = 44; return input;
		}
		private Transform Row(Transform parent)
		{
			var go = New("Row", parent); var layout = go.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childAlignment = TextAnchor.MiddleCenter; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; return go.transform;
		}
		private Transform Column(Transform parent)
		{
			var go = New("Column", parent); var layout = go.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8;
			layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; return go.transform;
		}
		private Transform Scroll(Transform parent) => _skin.Scroll(parent);
		private void OnDestroy()
		{
			_inputScope?.Dispose(); _skin?.Dispose(); if (Instance == this) Instance = null;
		}
	}
}
