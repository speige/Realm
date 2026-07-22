using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using HttpClient = System.Net.Http.HttpClient;
using NSec.Cryptography;
using System.Linq;

using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;

public partial class MapEditorHUD : Control
{
	public static MapEditorHUD Instance { get; private set; }
	public static bool IsTestMode { get; set; } = false;
	public static bool ReturningFromTest { get; set; } = false;

	public static Vector3 SavedCameraPosition;
	public static float SavedTargetHeight;
	public static float SavedCurrentHeight;
	public static float SavedTargetYaw;
	public static float SavedCurrentYaw;
	public static float SavedTargetPitch;
	public static float SavedCurrentPitch;
	public static bool SavedIsTopDown;
	public static float SavedYawSwing;
	public static float SavedPitchSwing;

	public static GameHost.GridOverlayMode SavedGridMode = GameHost.GridOverlayMode.Off;
	public static GameHost.EditorTool SavedActiveTool = GameHost.EditorTool.Raise;
	public static string SavedActivePlaceId = "";
	public static bool SavedCameraBoundsVisible = false;
	public static string SavedEntityCategory = "";

	public static float SavedBrushRadius = 2f;
	public static float SavedBrushStrength = 0.5f;

	private static string _lastUsedFolder = "";
	private static string _currentSourceFolder = "";

	private static bool _agreementShownThisSession = false;

	public enum EditorModule
	{
		Terrain,
		TextureDeco,
		Pathing,
		Objects,
		Coordinates,
		Clipboard
	}

	private MapEditorHUDViewModel _viewModel = new();
	public MapEditorHUDViewModel ViewModel => _viewModel;

	private Panel _panelLeft;
	private Button _btnLeftTab;
	private Panel _panelRight;
	private Button _btnRightTab;
	private VBoxContainer _accordionContainer;
	
	private bool _leftPanelExpanded = false;
	private bool _rightPanelExpanded = true;

	private OptionButton _optModule;
	private EditorModule _activeModule = EditorModule.Terrain;

	private VBoxContainer _accordionBrush;
	private Button _btnHeaderBrush;
	private VBoxContainer _contentBrush;
	
	private VBoxContainer _accordionTool;
	private Button _btnHeaderTool;
	private VBoxContainer _contentTool;
	
	private VBoxContainer _accordionToolSettings;
	private Button _btnHeaderToolSettings;
	private VBoxContainer _contentToolSettings;
	
	private VBoxContainer _accordionPlacement;
	private Button _btnHeaderPlacement;
	private VBoxContainer _contentPlacement;
	
	private VBoxContainer _accordionViewport;
	private Button _btnHeaderViewport;
	private VBoxContainer _contentViewport;
	

	
	private VBoxContainer _accordionInspector;
	private Button _btnHeaderInspector;
	private VBoxContainer _contentInspector;
	private Button _btnHeaderFile;
	private Control _contentFile;
	private Button _btnHeaderMapSettings;
	private Control _contentMapSettings;

	private VBoxContainer _containerTextureSettings;
	private VBoxContainer _containerPathingSettings;
	private VBoxContainer _containerPlacementSettings;
	private VBoxContainer _containerEyedropperSettings;
	private VBoxContainer _containerPasteSettings;
	private VBoxContainer _containerCategorySelector;

	private VBoxContainer _panelObjects;
	private VBoxContainer _panelClipboard;
	private VBoxContainer _panelTerrainVBox;
	private VBoxContainer _panelDecoVBox;
	private VBoxContainer _panelPathingVBox;
	private VBoxContainer _panelCoordinatesVBox;
	private Button _btnCut;
	private Button _btnEraseArea;
	private Button _btnMirrorVertically;
	private Button _btnMirrorHorizontally;
	private HSlider _sldPasteRotation;
	private Label _lblPasteRotation;
	
	private Button[] _swatchButtons = new Button[12];

	private Panel _leftPillar;
	private CheckBox _chkWaterEnabled;
	private Panel _rightPillar;
	private PanelContainer _topBar;
	private HBoxContainer _topToolbar;
	private VBoxContainer _middleRightBox;
	
	private PanelContainer _panelTextures;
	private PanelContainer _panelEntityPalette;
	private PanelContainer _panelTerrain;
	private PanelContainer _panelDeco;
	private PanelContainer _panelEnv;

	private Button _btnBackToHub;
	private Button _btnPublish;
	private Button _btnSave;
	private Button _btnTestMap;
	private Button _btnLoad;
	private Button _btnDeleteObject;
	private Button _btnUndo;
	private Button _btnRedo;

	private Label _statusLabel;
	private Label _feedbackLabel;

	private Button _btnZoomIn;
	private Button _btnZoomOut;
	private Button _btnCenter;
	private Button _btnRotate;
	private Button _btnCameraAngle;

	private Slider _sldBrushSize;
	private Label _lblBrushSizeValue;
	private Slider _sldBrushStrength;
	private Label _lblBrushStrengthValue;



	private CheckBox _chkRandomRotation;
	private CheckBox _chkRandomScale;
	private Button _btnAddObject;
	private CheckBox _chkClumpMode;
	private Control _densityBox;
	private Control _scaleVarBox;
	private Control _camBoundsBox;
	private Control _waterHeightBox;
	private CheckBox _chkBlockMode;
	private Slider _sldBlockStep;
	private Label _lblBlockStepValue;
	private Slider _sldWaterHeight;
	private Label _lblWaterHeightValue;


	private Slider _sldPlacementRotate;
	private Label _lblPlacementRotateValue;
	private Slider _sldPlacementScale;
	private Label _lblPlacementScaleValue;
	private VBoxContainer _placementRotateBox;
	private VBoxContainer _placementScaleBox;
	private Button _btnCopy;
	private Button _btnPaste;
	private Control _stepBox;
	private Button _btnToggleSnap;
	private Button _btnToggleGrid;
	private Button _btnBrushShape;
	private Button _btnResetMap;
	private Button _btnGenerateMap;
	private Button _btnImportMinimap;
	private Button _btnEyedropper;
	private OptionButton _optEyedropperMode;
	private Button _btnNoise;
	private PanelContainer _minimapFrame;
	private Control _minimapArea;
	private MapEditorCameraIndicator _cameraIndicator;

	private Button _btnSkybox;
	private OptionButton _optSkybox;

	private OptionButton _optMapType;
	private CheckBox _chkTagTD;
	private CheckBox _chkTagCampaign;
	private CheckBox _chkTagMelee;
	private CheckBox _chkTagCoop;
	private CheckBox _chkTagTutorial;

	private List<string> _skyboxFiles = new List<string>();



	private string[] _swatchPaths = new string[12];
	private string[] _swatchDisplayNames = new string[12];
	private Color[] _swatchColors = new Color[12];

	private Button _btnRaise;
	private Button _btnLower;
	private Button _btnSmooth;
	private Button _btnPlateau;
	private Button _btnRamp;
	private Button _btnMirrorMode;
	private Button _btnClumpBrush;
	private HSlider _sldClumpDensity;
	private Label _lblClumpDensityValue;
	private HSlider _sldClumpScaleVar;
	private Label _lblClumpScaleVarValue;

	private Button _btnTextureBrush;

	private Button _btnFloodFill;
	private Button _btnSelectArea;
	private Button _btnSelectMove;
	private PanelContainer _inspectorPanel;
	private Label _lblInspectorTitle;
	private Label _lblInspectorPos;
	private Button _btnInspectorRotLeft;
	private Button _btnInspectorRotRight;
	private Button _btnInspectorScaleDown;
	private Button _btnInspectorScaleUp;
	private Button _btnInspectorScaleReset;
	private Button _btnInspectorDelete;

	private Button _btnPathingBrush;
	private Button _btnFloodFillPathing;
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkBuildable;

	private OptionButton _optPathingMode;


	private Button _btnDrawCoordinate;
	private LineEdit _txtCoordinateName;
	private Button _btnCommitCoordinate;
	private VBoxContainer _coordinateListVBox;
	private int _pendingCoordinateMinX;
	private int _pendingCoordinateMinZ;
	private int _pendingCoordinateMaxX;
	private int _pendingCoordinateMaxZ;

	private Button _activeToolButton = null;
	private StyleBoxFlat _highlightStyle;
	private Label _lblInfoText;
	private Label _lblTerrainTexture;
	private Label _lblCliffTexture;

	private Button _btnToggleCameraBounds;
	private Label _lblCamLeftVal;
	private Label _lblCamRightVal;
	private Label _lblCamTopVal;
	private Label _lblCamBottomVal;
	
	private Label _lblMapWidthVal;
	private Label _lblMapHeightVal;

	private PanelContainer _scaleMapDialog;
	private Label _lblScalePreviewWidth;
	private Label _lblScalePreviewHeight;
	private int _scaleDialogTargetWidth;
	private int _scaleDialogTargetDepth;

	private Camera3D _camera3D;
	private Button _btnVSCode;
	private bool _isDraggingSlider = false;
	private Panel _swatchHighlightPanel;
	private Panel _swatchCliffHighlightPanel;

	private MapEditorTopBar _topBarController;
	private MapEditorBrushSettings _brushSettingsController;
	private MapEditorPlacementSettings _placementSettingsController;
	private MapEditorInspector _inspectorController;
	private MapEditorPathingPanel _pathingPanelController;
	private MapEditorMinimap _minimapController;
	private MapEditorEntityPaletteController _entityPaletteController;
	private MapEditorGenerationDialog _generationDialog;

	private bool _wasmHasErrors = false;
	private string _wasmCompileLogPath = "";

	public const string TempWorkspaceGodotPath = "user://temp_map_workspace";

	private string _tempWorkspacePath;
	private long _lastTerrainSyncTime = 0;
	private long _lastMetadataSyncTime = 0;
	private bool _isSyncing = false;

	public override void _ExitTree()
	{
		CloseWasmConsoleModal();
		if (Instance == this)
		{
			Instance = null;
		}
		if (GodotObject.IsInstanceValid(_swatchHighlightPanel) && _swatchHighlightPanel.GetParent() == null)
		{
			_swatchHighlightPanel.QueueFree();
		}
		if (GodotObject.IsInstanceValid(_swatchCliffHighlightPanel) && _swatchCliffHighlightPanel.GetParent() == null)
		{
			_swatchCliffHighlightPanel.QueueFree();
		}
	}

	public override void _Ready()
	{
		try
		{
			Instance = this;
			_tempWorkspacePath = ProjectSettings.GlobalizePath("user://temp_map_workspace");

			_camera3D = (GameHost.Instance?.MainCamera);

		_highlightStyle = new StyleBoxFlat();
		_highlightStyle.BgColor = new Color(0, 0, 0, 0);
		_highlightStyle.BorderColor = UIStyle.ColorCyanGlow;
		_highlightStyle.SetBorderWidthAll(3);
		_highlightStyle.CornerRadiusTopLeft = 4;
		_highlightStyle.CornerRadiusTopRight = 4;
		_highlightStyle.CornerRadiusBottomLeft = 4;
		_highlightStyle.CornerRadiusBottomRight = 4;

		_leftPillar = new Panel();
		_rightPillar = new Panel();
		_topToolbar = new HBoxContainer();

		_panelTextures = new PanelContainer();
		_panelEntityPalette = new PanelContainer();
		_panelTerrain = new PanelContainer();
		_panelDeco = new PanelContainer();
		_panelEnv = new PanelContainer();
		_btnClumpBrush = new Button();

		_panelLeft = GetNode<Panel>("LeftSlidePanel");
		_panelRight = GetNode<Panel>("RightSlidePanel");

		_btnLeftTab = GetNode<Button>("LeftSlidePanel/LeftTabButton");
		_btnLeftTab.Pressed += ToggleLeftPanel;

		_btnRightTab = GetNode<Button>("RightSlidePanel/RightTabButton");
		_btnRightTab.Pressed += ToggleRightPanel;

		_btnBackToHub = GetNode<Button>("TopLeftBox/BtnBack");
		SetupButton(_btnBackToHub, "🚪 BACK TO HUB", () => BackToHubAction(), 13, "Exit editor and return to game lobby");

		var btnHelp = GetNode<Button>("TopLeftBox/BtnHelp");
		SetupButton(btnHelp, "❓ HELP / HOTKEYS", () => ToggleHelpPanelExternal(), 13, "Toggle the hotkeys and editor guide overlay (H)");

		if (OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.Initialize(this);
			_btnVSCode = GetNode<Button>("TopLeftBox/BtnVSCode");
			SetupButton(_btnVSCode, "💻 CODE & DATA", () => ToggleVSCodeEditor(), 13, "Toggle the embedded VSCode editor");
		}
		else
		{
			_btnVSCode = new Button();
		}

		_btnUndo = GetNode<Button>("TopLeftBox/BtnUndo");
		SetupButton(_btnUndo, "↩️ UNDO", () => UndoAction(), 13, "Undo the last action (Ctrl+Z)");

		_btnRedo = GetNode<Button>("TopLeftBox/BtnRedo");
		SetupButton(_btnRedo, "↪️ REDO", () => RedoAction(), 13, "Redo the last undone action (Ctrl+Y)");

		_btnEyedropper = GetNode<Button>("TopLeftBox/BtnEyedropper");
		SetupButton(_btnEyedropper, "🔍 EYEDROPPER", () => TriggerToolSelection(GameHost.EditorTool.Eyedropper, _btnEyedropper), 14, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");
		UIStyle.ApplyButtonText(_btnEyedropper, "🔍 EYEDROPPER", 13);

		_optModule = GetNode<OptionButton>("TopLeftBox/OptModule");
		_optModule.AddItem("⛰️ " + TranslationServer.Translate("TERRAIN"), (int)EditorModule.Terrain);
		_optModule.AddItem("🎨 " + TranslationServer.Translate("TEXTURE"), (int)EditorModule.TextureDeco);
		_optModule.AddItem("🧭 " + TranslationServer.Translate("PATHING"), (int)EditorModule.Pathing);
		_optModule.AddItem("💂 " + TranslationServer.Translate("OBJECTS"), (int)EditorModule.Objects);
		_optModule.AddItem("🗺️ " + TranslationServer.Translate("COORDINATES"), (int)EditorModule.Coordinates);
		_optModule.AddItem("📋 " + TranslationServer.Translate("CLIPBOARD"), (int)EditorModule.Clipboard);
		_optModule.ItemSelected += (index) => SwitchModule((EditorModule)index);

		_statusLabel = GetNode<Label>("TopBar/HBox/StatusLabel");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");
		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);

		// Left Accordions
		_btnHeaderFile = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/BtnHeaderFile");
		_contentFile = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile");
		StyleAccordionHeader(_btnHeaderFile);
		SetupMutualAccordion(_btnHeaderFile, _contentFile, TranslationServer.Translate("File"));

		_btnLoad = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnLoad");
		SetupButton(_btnLoad, "📂 LOAD", () => LoadMapAction(), 13, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

		_btnSave = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnSave");
		SetupButton(_btnSave, "💾 SAVE", () => SaveMapActionExternal(), 13, "Save current heightmap, textures, and entities (Ctrl+S)");

		_btnTestMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnTestMap");
		SetupButton(_btnTestMap, "🎮 TEST", () => TestMapAction(), 13, "Launch single-player mode on the current editor map");

		_btnPublish = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnPublish");
		SetupButton(_btnPublish, "📜 PUBLISH", () => PublishMapActionExternal(), 13, "Publish/export map to custom map registry");

		_btnResetMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnResetMap");
		SetupButton(_btnResetMap, "🧹 RESET TO BLANK MAP", () =>
		{
			ShowConfirmationDialog(
				"Are you sure you want to clear the entire map? This will delete all placed entities and reset terrain heights.",
				() => GameHost.Instance?.ClearMapEntirely()
			);
		}, 13, "Clear all terrain heights, colors, and placed entities");

		_btnGenerateMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnGenerateMap");
		SetupButton(_btnGenerateMap, "🎲 RANDOM GEN", () => _generationDialog.Show(), 13, "Open random terrain generator settings modal");

		_btnImportMinimap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnImportMinimap");
		SetupButton(_btnImportMinimap, "🗺️ GEN FROM IMAGE", () => ImportTerrainFromMinimapDialog(), 13, "Import terrain elevations, textures, and trees from a minimap image file");

		_btnHeaderViewport = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/BtnHeaderViewport");
		_contentViewport = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport");
		StyleAccordionHeader(_btnHeaderViewport);
		SetupMutualAccordion(_btnHeaderViewport, _contentViewport, TranslationServer.Translate("Viewport & Navigation"));

		InitializeTempWorkspace();

	


		_btnToggleGrid = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnToggleGrid");
		SetupButton(_btnToggleGrid, "🌐 GRID OVERLAY: OFF", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorGridMode = GameHost.Instance.EditorGridMode switch
				{
					GameHost.GridOverlayMode.Off => GameHost.GridOverlayMode.Mesh,
					GameHost.GridOverlayMode.Mesh => GameHost.GridOverlayMode.Off,
					_ => GameHost.GridOverlayMode.Off
				};
				GameHost.Instance.UpdateGridOverlayVisibility();
				UpdateGridOverlayExternal(GameHost.Instance.EditorGridMode);
			}
		}, 10, "Toggle rendering of the overlay alignment grid lines (V)");

		_btnToggleCameraBounds = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnToggleCameraBounds");
		SetupButton(_btnToggleCameraBounds, "📹 CAM BOUNDS: OFF", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorCameraBoundsVisible = !GameHost.Instance.EditorCameraBoundsVisible;
				GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
				UpdateCameraBoundsOverlayExternal(GameHost.Instance.EditorCameraBoundsVisible);
			}
		}, 10, "Toggle rendering of the camera bounds overlay");

		_btnRotate = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnRotate");
		SetupButton(_btnRotate, "🔄 Rotate", () =>
		{
			UIManager.Instance?.PlayClickSound();
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.Rotate90Degrees();
		}, 13, "Rotate camera 90 degrees");

		_btnCameraAngle = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnCameraAngle");
		SetupButton(_btnCameraAngle, "📐 Tilt", () =>
		{
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.ToggleTopDown();
		}, 11, "Toggle top-down vs perspective angle (C)");

		_btnSkybox = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnSkybox");
		SetupButton(_btnSkybox, "☀️ Cycle Lighting", () => {
			if (GameHost.Instance != null)
			{
				var res = GameHost.Instance.CycleTimeOfDay();
				UpdateLightingTuningSlidersFromPhase(res.TimeOfDayIndex);
			}
		}, 11, "Cycle map environment lighting (L)");

		_btnZoomIn = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnZoomIn");
		_btnZoomIn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			(GameHost.Instance?.MainCamera as CameraControl)?.ZoomIn();
		};

		_btnZoomOut = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnZoomOut");
		_btnZoomOut.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			(GameHost.Instance?.MainCamera as CameraControl)?.ZoomOut();
		};

		_minimapFrame = GetNode<PanelContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame");
		_minimapArea = GetNode<Control>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea");
		_cameraIndicator = GetNode<MapEditorCameraIndicator>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea/CameraIndicator");

		_btnHeaderMapSettings = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/BtnHeaderMapSettings");
		_contentMapSettings = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings");
		StyleAccordionHeader(_btnHeaderMapSettings);
		SetupMutualAccordion(_btnHeaderMapSettings, _contentMapSettings, TranslationServer.Translate("Map Settings"));

		_waterHeightBox = GetNode<Control>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/WaterHeightBox");
		_lblWaterHeightValue = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/WaterHeightBox/Header/LblWaterHeightValue");
		_sldWaterHeight = GetNode<Slider>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/WaterHeightBox/SldWaterHeight");
		_sldWaterHeight.DragStarted += () => _isDraggingSlider = true;
		_sldWaterHeight.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_chkWaterEnabled = GetNode<CheckBox>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/WaterHeightBox/ChkWaterEnabled");

		_camBoundsBox = GetNode<Control>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox");
		_lblCamLeftVal = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/LblCamLeftVal");
		_lblCamRightVal = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/LblCamRightVal");
		_lblCamTopVal = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/LblCamTopVal");
		_lblCamBottomVal = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/LblCamBottomVal");

		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnLeftDec"), "⬅️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float minX = -GameHost.Instance.GroundTerrain.Width;
				GameHost.Instance.EditorCameraBoundsLeft = Mathf.Max(minX, GameHost.Instance.EditorCameraBoundsLeft - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Left boundary further left (West)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnLeftInc"), "➡️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float maxX = GameHost.Instance.EditorCameraBoundsRight;
				GameHost.Instance.EditorCameraBoundsLeft = Mathf.Min(maxX, GameHost.Instance.EditorCameraBoundsLeft + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Left boundary further right (East)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnRightDec"), "⬅️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float minX = GameHost.Instance.EditorCameraBoundsLeft;
				GameHost.Instance.EditorCameraBoundsRight = Mathf.Max(minX, GameHost.Instance.EditorCameraBoundsRight - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Right boundary further left (West)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnRightInc"), "➡️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float maxX = (float)GameHost.Instance.GroundTerrain.Width;
				GameHost.Instance.EditorCameraBoundsRight = Mathf.Min(maxX, GameHost.Instance.EditorCameraBoundsRight + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Right boundary further right (East)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnTopDec"), "⬅️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float minZ = -GameHost.Instance.GroundTerrain.Depth;
				GameHost.Instance.EditorCameraBoundsTop = Mathf.Max(minZ, GameHost.Instance.EditorCameraBoundsTop - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Top boundary further North (Up)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnTopInc"), "➡️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float maxZ = GameHost.Instance.EditorCameraBoundsBottom;
				GameHost.Instance.EditorCameraBoundsTop = Mathf.Min(maxZ, GameHost.Instance.EditorCameraBoundsTop + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Top boundary further South (Down)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnBottomDec"), "⬅️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float minZ = GameHost.Instance.EditorCameraBoundsTop;
				GameHost.Instance.EditorCameraBoundsBottom = Mathf.Max(minZ, GameHost.Instance.EditorCameraBoundsBottom - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Bottom boundary further North (Up)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/CamBoundsBox/CamBoundsGrid/BtnBottomInc"), "➡️", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				EnsureCameraBoundsVisible();
				float maxZ = (float)GameHost.Instance.GroundTerrain.Depth;
				GameHost.Instance.EditorCameraBoundsBottom = Mathf.Min(maxZ, GameHost.Instance.EditorCameraBoundsBottom + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Bottom boundary further South (Down)");

		_lblMapWidthVal = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/MapSizeGrid/LblMapWidthVal");
		_lblMapHeightVal = GetNode<Label>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/MapSizeGrid/LblMapHeightVal");

		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/MapSizeGrid/BtnWidthDec"), "➖", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int w = GameHost.Instance.GroundTerrain.Width;
				if (w > 32) {
					GameHost.Instance.ResizeMapExternal(w - 16, GameHost.Instance.GroundTerrain.Depth);
					UpdateCameraBoundsUI();
				}
			}
		}, 10, "Decrease map tile columns (West)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/MapSizeGrid/BtnWidthInc"), "➕", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int w = GameHost.Instance.GroundTerrain.Width;
				if (w < 512) {
					GameHost.Instance.ResizeMapExternal(w + 16, GameHost.Instance.GroundTerrain.Depth);
					UpdateCameraBoundsUI();
				}
			}
		}, 10, "Increase map tile columns (East)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/MapSizeGrid/BtnHeightDec"), "➖", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int d = GameHost.Instance.GroundTerrain.Depth;
				if (d > 32) {
					GameHost.Instance.ResizeMapExternal(GameHost.Instance.GroundTerrain.Width, d - 16);
					UpdateCameraBoundsUI();
				}
			}
		}, 10, "Decrease map tile rows (North)");
		SetupButton(GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/MapSizeGrid/BtnHeightInc"), "➕", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int d = GameHost.Instance.GroundTerrain.Depth;
				if (d < 512) {
					GameHost.Instance.ResizeMapExternal(GameHost.Instance.GroundTerrain.Width, d + 16);
					UpdateCameraBoundsUI();
				}
			}
		}, 10, "Increase map tile rows (South)");

		UpdateCameraBoundsUI();

		var btnScaleMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapSizeBox/BtnScaleMap");
		SetupButton(btnScaleMap, "⚖ SCALE MAP", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				_scaleDialogTargetWidth = GameHost.Instance.GroundTerrain.Width;
				_scaleDialogTargetDepth = GameHost.Instance.GroundTerrain.Depth;
				OpenScaleMapDialog();
			}
		}, 11, "Scale the entire map: stretches/shrinks terrain data and repositions all entities proportionally");

		_optSkybox = GetNode<OptionButton>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/SkyboxBox/OptSkybox");

		_optMapType = GetNodeOrNull<OptionButton>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapTypeBox/OptMapType");
		if (_optMapType != null) {
			_optMapType.AddItem("Arcade Custom Map", 0);
			_optMapType.AddItem("Asset Pack", 1);
			_optMapType.ItemSelected += (idx) => {
				RebuildTagsUI();
				SaveMapProperties();
			};
		}

		_skyboxFiles.Clear();
		_optSkybox.Clear();
		_skyboxFiles.Add("skybox_panoramic.jpg");
		_optSkybox.AddItem(TranslationServer.Translate("Default Panoramic"));

		using (var dir = DirAccess.Open("res://Assets/Skyboxes"))
		{
			if (dir != null)
			{
				dir.ListDirBegin();
				string fileName = dir.GetNext();
				while (fileName != "")
				{
					if (!dir.CurrentIsDir() && !fileName.EndsWith(".import") &&
						(fileName.EndsWith(".png") || fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg")))
					{
						_skyboxFiles.Add(fileName);
						string cleanName = System.IO.Path.GetFileNameWithoutExtension(fileName).Replace("_", " ");
						cleanName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);
						_optSkybox.AddItem(TranslationServer.Translate(cleanName));
					}
					fileName = dir.GetNext();
				}
			}
		}

		_optSkybox.ItemSelected += (index) =>
		{
			int idx = (int)index;
			if (idx >= 0 && idx < _skyboxFiles.Count)
			{
				string selectedFile = _skyboxFiles[idx];
				string path = selectedFile == "skybox_panoramic.jpg"
					? "res://Assets/skybox_panoramic.jpg"
					: $"res://Assets/Skyboxes/{selectedFile}";
				GameHost.Instance?.SetSkyboxTexture(path);
			}
		};

		ApplyThemeStyles();
		SetupLightingTuningUI();

		// Right Accordions
		_accordionTool = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion");
		_btnHeaderTool = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/BtnHeaderTool");
		_contentTool = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool");
		StyleAccordionHeader(_btnHeaderTool);
		SetupAccordion(_btnHeaderTool, _contentTool, TranslationServer.Translate("Tool"));

		_panelTerrainVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox");
		_panelDecoVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox");
		_panelPathingVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox");
		_panelCoordinatesVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox");
		_panelObjects = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox");
		_panelClipboard = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard");

		_btnRaise = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnRaise");
		SetupButton(_btnRaise, "⛰️ Raise", () => TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise), 11, "Elevate terrain height (1)");

		_btnLower = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnLower");
		SetupButton(_btnLower, "🕳️ Lower", () => TriggerToolSelection(GameHost.EditorTool.Lower, _btnLower), 11, "Lower terrain height (2)");

		_btnSmooth = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnSmooth");
		SetupButton(_btnSmooth, "✨ Smooth", () => TriggerToolSelection(GameHost.EditorTool.Smooth, _btnSmooth), 11, "Smooth terrain height (3)");

		_btnPlateau = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnPlateau");
		SetupButton(_btnPlateau, "🥞 Plateau", () => TriggerToolSelection(GameHost.EditorTool.Plateau, _btnPlateau), 11, "Flatten terrain to cursor height on click (5)");

		_btnRamp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnRamp");
		SetupButton(_btnRamp, "📐 Ramp", () => TriggerToolSelection(GameHost.EditorTool.Ramp, _btnRamp), 11, "Create ramp between two points (6)");

		_btnNoise = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnNoise");
		SetupButton(_btnNoise, "🎲 Roughen", () => TriggerToolSelection(GameHost.EditorTool.Noise, _btnNoise), 11, "Add random height variations/noise to the terrain under the brush (7)");

		_btnTextureBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnTextureBrush");
		SetupButton(_btnTextureBrush, "🎨 Paint", () => TriggerToolSelection(GameHost.EditorTool.PaintGrass, _btnTextureBrush), 11, "Paint terrain texture (8)");

		_btnFloodFill = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnFloodFill");
		SetupButton(_btnFloodFill, "🪣 Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFill, _btnFloodFill), 11, "Flood fill terrain texture");

		_btnSelectArea = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnSelectArea");
		SetupButton(_btnSelectArea, "🔲 Select Area", () => TriggerToolSelection(GameHost.EditorTool.SelectArea, _btnSelectArea), 11, "Select rectangular area");

		_btnPathingBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/BtnPathingBrush");
		SetupButton(_btnPathingBrush, "🧭 Paint", () => TriggerToolSelection(GameHost.EditorTool.PaintPathing, _btnPathingBrush), 11, "Paint pathing attributes onto the terrain map");

		_btnFloodFillPathing = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/BtnFloodFillPathing");
		SetupButton(_btnFloodFillPathing, "🪣 Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFillPathing, _btnFloodFillPathing), 11, "Flood fill pathing attributes onto the terrain map");

		_btnAddObject = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnAddObject");
		SetupButton(_btnAddObject, "➕ Add Object", () => _entityPaletteController?.TriggerAddObjectMode(), 11, "Place units, props, or decals");

		_btnSelectMove = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnSelectMove");
		SetupButton(_btnSelectMove, "🖱️ Select / Move", () => TriggerToolSelection(GameHost.EditorTool.SelectMove, _btnSelectMove), 11, "Select and move units, props, or decals");

		_btnDeleteObject = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnDeleteObject");
		SetupButton(_btnDeleteObject, "❌ Erase Object", () =>
		{
			if (GodotObject.IsInstanceValid(GameHost.Instance?.SelectedEditorObject))
				DeleteSelectedObjectAction();
			else
				TriggerToolSelection(GameHost.EditorTool.DeleteObject, _btnDeleteObject);
		}, 11, "Erase units, props, or decals");

		_btnDrawCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnDrawCoordinate");
		SetupButton(_btnDrawCoordinate, "🗺️ Draw Coordinate", () => TriggerToolSelection(GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate), 11, "Drag to define a named coordinate box exposed as C# variables");

		_txtCoordinateName = GetNode<LineEdit>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/CoordinateNameRow/TxtCoordinateName");
		_btnCommitCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnCommitCoordinate");
		_btnCommitCoordinate.Pressed += () =>
		{
			if (_pendingCoordinateMinX == _pendingCoordinateMaxX && _pendingCoordinateMinZ == _pendingCoordinateMaxZ)
			{
				ShowFeedback("Select a valid area first by dragging.");
				return;
			}
			string name = _txtCoordinateName?.Text ?? "";
			if (string.IsNullOrWhiteSpace(name))
			{
				ShowFeedback("Enter a coordinate name before creating.");
				return;
			}
			bool ok = GameHost.Instance?.CommitCoordinateExternal(name, _pendingCoordinateMinX, _pendingCoordinateMinZ, _pendingCoordinateMaxX, _pendingCoordinateMaxZ) ?? false;
			if (ok)
			{
				RefreshCoordinateListExternal();
				ShowFeedback($"Coordinate '{name}' created.");
				_txtCoordinateName.Text = "";
			}
		};
		_coordinateListVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/CoordinateListVBox");

		_btnCopy = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnCopy");
		SetupButton(_btnCopy, "📋 COPY", () => GameHost.Instance?.PerformCopyAreaExternal(), 13, "Copy selected area to clipboard (Ctrl+C)");

		_btnPaste = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnPaste");
		SetupButton(_btnPaste, "📋 PASTE", () => TriggerToolSelection(GameHost.EditorTool.PasteArea, _btnPaste), 13, "Paste clipboard contents onto terrain (Ctrl+V)");

		_btnCut = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnCut");
		SetupButton(_btnCut, "✂️ CUT", () => GameHost.Instance?.PerformCutAreaExternal(), 13, "Cut selected area to clipboard (Ctrl+X)");

		_btnEraseArea = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnEraseArea");
		SetupButton(_btnEraseArea, "🧹 ERASE SELECTED AREA", () => GameHost.Instance?.PerformEraseAreaExternal(), 13, "Erase heights, textures and objects within selection (Delete)");

		_btnMirrorHorizontally = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnMirrorHorizontally");
		SetupButton(_btnMirrorHorizontally, TranslationServer.Translate("MIRROR HORIZONTALLY"), () => GameHost.Instance?.PerformMirrorSelectionHorizontallyExternal(), 13, "Mirror selection horizontally");

		_btnMirrorVertically = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnMirrorVertically");
		SetupButton(_btnMirrorVertically, TranslationServer.Translate("MIRROR VERTICALLY"), () => GameHost.Instance?.PerformMirrorSelectionVerticallyExternal(), 13, "Mirror selection vertically");

		_accordionBrush = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion");
		_btnHeaderBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/BtnHeaderBrush");
		_contentBrush = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush");
		StyleAccordionHeader(_btnHeaderBrush);
		SetupAccordion(_btnHeaderBrush, _contentBrush, TranslationServer.Translate("Global Brush Properties"));

		_sldBrushSize = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox/SldBrushSize");
		_lblBrushSizeValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox/Header/LblBrushSizeValue");
		_sldBrushStrength = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushStrengthBox/SldBrushStrength");
		_lblBrushStrengthValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushStrengthBox/Header/LblBrushStrengthValue");

		_btnBrushShape = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BtnBrushShape");
		SetupButton(_btnBrushShape, "⚪ BRUSH: CIRCLE", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorBrushIsSquare = !GameHost.Instance.EditorBrushIsSquare;
				GameHost.Instance.UpdateBrushMesh();
				UpdateBrushShapeExternal(GameHost.Instance.EditorBrushIsSquare);
			}
		}, 11, "Toggle brush shape between circular and square (B)");

		_btnMirrorMode = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BtnMirrorMode");
		SetupButton(_btnMirrorMode, "🪞 MIRROR: NONE", () => CycleMirrorMode(), 10, "Cycle terrain and object mirroring symmetry mode");

		_chkBlockMode = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/ChkBlockMode");
		_chkBlockMode.Toggled += (toggled) =>
		{
			ShowFeedback(toggled ? "Block Mode: Enabled" : "Block Mode: Disabled");
			UpdateBlockStepVisibility();
			UpdateBrushStrengthVisibility();
		};
		_chkBlockMode.ButtonPressed = true;

		_stepBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox");
		_sldBlockStep = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox/SldBlockStep");
		_sldBlockStep.DragStarted += () => _isDraggingSlider = true;
		_sldBlockStep.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_lblBlockStepValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox/Header/LblBlockStepValue");

		_accordionToolSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion");
		_btnHeaderToolSettings = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/BtnHeaderToolSettings");
		_contentToolSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings");
		StyleAccordionHeader(_btnHeaderToolSettings);
		SetupAccordion(_btnHeaderToolSettings, _contentToolSettings, TranslationServer.Translate("Tool Settings"));

		_containerTextureSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture");
		_lblTerrainTexture = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/LblTerrainTexture");
		_lblCliffTexture = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/LblCliffTexture");
		
		var btnTextureSwap = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/BtnTextureSwap");
		SetupButton(btnTextureSwap, "🔄 SWAP TEXTURES (GLOBAL)", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.SwapTexturesExternal(GameHost.Instance.EditorPaintTextureIndex, GameHost.Instance.EditorCliffPaintTextureIndex);
			}
		}, 11, "Globally swap grass/dirt texture assignment indices (X)");

		var btnTextureImport = new Button();
		btnTextureImport.Name = "BtnTextureImport";
		_containerTextureSettings.AddChild(btnTextureImport);
		SetupButton(btnTextureImport, "📥 IMPORT CUSTOM TEXTURE", () => ImportTextureAction(), 11, "Import a custom image for the selected slot");

		for (int i = 1; i <= 12; i++)
		{
			_swatchButtons[i - 1] = GetNode<Button>($"RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/GridSwatches/Swatch{i}");
		}
		SetupTextureSwatches(true);

		_containerPathingSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing");
		_chkShallowWater = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent/ChkShallowWater");
		UIStyle.ApplyCheckboxStyle(_chkShallowWater);
		_chkDeepWater = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent/ChkDeepWater");
		UIStyle.ApplyCheckboxStyle(_chkDeepWater);
		_chkFlying = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent/ChkFlying");
		UIStyle.ApplyCheckboxStyle(_chkFlying);
		_chkGround = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent/ChkGround");
		UIStyle.ApplyCheckboxStyle(_chkGround);
		_chkBuildable = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent/ChkBuildable");
		UIStyle.ApplyCheckboxStyle(_chkBuildable);
		_chkGround.ButtonPressed = true;

		_optPathingMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent/PathingModeRow/OptPathingMode");
		_optPathingMode.AddItem(TranslationServer.Translate("Add Pathing Attribute"), 0);
		_optPathingMode.AddItem(TranslationServer.Translate("Clear Pathing Attribute"), 1);
		_optPathingMode.Selected = 0;

		_containerCategorySelector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerCategorySelector");


		_containerEyedropperSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerEyedropper");
		_optEyedropperMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerEyedropper/OptEyedropperMode");
		_optEyedropperMode.AddItem(TranslationServer.Translate("Auto-Detect Mode"), 0);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick 3D Asset"), 1);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Decal"), 2);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Terrain Texture"), 3);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Height"), 4);
		_optEyedropperMode.Selected = 0;

		_containerPasteSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste");
		var chkPasteHeights = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPasteHeights");
		chkPasteHeights.Text = TranslationServer.Translate("HeightMap");
		chkPasteHeights.ButtonPressed = true;
		var chkPasteTextures = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPasteTextures");
		chkPasteTextures.Text = TranslationServer.Translate("Textures");
		chkPasteTextures.ButtonPressed = true;
		var chkPasteEntities = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPasteEntities");
		chkPasteEntities.Text = TranslationServer.Translate("Units / Props / Decals");
		chkPasteEntities.ButtonPressed = true;
		var chkPastePathing = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPastePathing");
		chkPastePathing.Text = TranslationServer.Translate("Pathing");
		chkPastePathing.ButtonPressed = true;

		chkPasteTextures.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionTextures = toggled; };
		chkPasteHeights.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionHeights = toggled; };
		chkPasteEntities.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionEntities = toggled; };
		chkPastePathing.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionPathing = toggled; };

		_lblPasteRotation = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/Header/LblPasteRotationValue");
		_sldPasteRotation = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/SldPasteRotation");
		_sldPasteRotation.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblPasteRotation.Text = fVal.ToString("F0") + "°";
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorPasteRotation = fVal;
			}
		};
		_sldPasteRotation.DragStarted += () => _isDraggingSlider = true;
		_sldPasteRotation.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_accordionInspector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion");
		_btnHeaderInspector = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/BtnHeaderInspector");
		_contentInspector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector");
		StyleAccordionHeader(_btnHeaderInspector);
		SetupAccordion(_btnHeaderInspector, _contentInspector, TranslationServer.Translate("Selected Object Inspector"));

		_accordionPlacement = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion");
		_btnHeaderPlacement = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/BtnHeaderPlacement");
		_contentPlacement = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement");
		StyleAccordionHeader(_btnHeaderPlacement);
		SetupAccordion(_btnHeaderPlacement, _contentPlacement, TranslationServer.Translate("Placement Config"));

		_sldPlacementRotate = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementRotateBox/SldPlacementRotate");
		_lblPlacementRotateValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementRotateBox/Header/LblPlacementRotateValue");
		_sldPlacementScale = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementScaleBox/SldPlacementScale");
		_lblPlacementScaleValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementScaleBox/Header/LblPlacementScaleValue");
		
		_btnToggleSnap = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/BtnToggleSnap");
		SetupButton(_btnToggleSnap, "🔲 SNAP TO GRID: OFF", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorSnapToGrid = !GameHost.Instance.EditorSnapToGrid;
				UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
			}
		}, 11, "Toggle snapping objects and placements to the grid");


		_chkRandomRotation = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ChkRandomRotation");
		_chkRandomScale = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ChkRandomScale");

		_chkClumpMode = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ChkClumpMode");
		_densityBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/DensityBox");
		_sldClumpDensity = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/DensityBox/SldClumpDensity");
		_lblClumpDensityValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/DensityBox/Header/LblClumpDensityValue");
		_scaleVarBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox");
		_sldClumpScaleVar = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox/SldClumpScaleVar");
		_lblClumpScaleVarValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox/Header/LblClumpScaleVarValue");

		_chkClumpMode.ButtonPressed = false;
		_chkClumpMode.Toggled += (toggled) =>
		{
			if (_densityBox != null) _densityBox.Visible = toggled;
			if (_scaleVarBox != null) _scaleVarBox.Visible = toggled;
		};

		_sldClumpDensity.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblClumpDensityValue.Text = fVal.ToString("F0");
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpDensity = fVal;
		};
		_sldClumpDensity.DragStarted += () => _isDraggingSlider = true;
		_sldClumpDensity.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_sldClumpScaleVar.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblClumpScaleVarValue.Text = fVal.ToString("F2");
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpScaleVar = fVal;
		};
		_sldClumpScaleVar.DragStarted += () => _isDraggingSlider = true;
		_sldClumpScaleVar.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_sldPlacementRotate.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementScale.DragStarted += () => _isDraggingSlider = true;

		TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise);

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;



		_entityPaletteController = new MapEditorEntityPaletteController(this, _containerCategorySelector, _btnAddObject);
		_generationDialog = new MapEditorGenerationDialog(this);

		_topBarController = new MapEditorTopBar(_btnBackToHub, _btnPublish, _btnSave, _btnLoad, _btnUndo, _btnRedo, _btnVSCode, _statusLabel, _feedbackLabel);
		_brushSettingsController = new MapEditorBrushSettings(_sldBrushSize, _lblBrushSizeValue, _sldBrushStrength, _lblBrushStrengthValue, _chkBlockMode, _sldBlockStep, _lblBlockStepValue, _sldWaterHeight, _lblWaterHeightValue, _chkWaterEnabled);
		_placementSettingsController = new MapEditorPlacementSettings(_sldPlacementRotate, _lblPlacementRotateValue, _sldPlacementScale, _lblPlacementScaleValue, _chkRandomRotation, _chkRandomScale, _chkClumpMode, _sldClumpDensity, _lblClumpDensityValue, _sldClumpScaleVar, _lblClumpScaleVarValue);
		InitializeInspectorPanel();
		_inspectorController = new MapEditorInspector(_lblInspectorTitle, _lblInspectorPos, _btnInspectorRotLeft, _btnInspectorRotRight, _btnInspectorScaleDown, _btnInspectorScaleUp, _btnInspectorScaleReset, _btnInspectorDelete);
		_pathingPanelController = new MapEditorPathingPanel(_chkShallowWater, _chkDeepWater, _chkFlying, _chkGround, _chkBuildable, _optPathingMode);

		SetupMinimap();

		_minimapController = new MapEditorMinimap(_minimapFrame, _minimapArea, _cameraIndicator, this);
		RegenerateMinimap();

		if (GameHost.Instance != null)
		{
			UpdateRotationExternal(GameHost.Instance.EditorPlacementRotation);
			UpdateScaleExternal(GameHost.Instance.EditorPlacementScale);
			UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
			UpdatePasteRotationExternal(GameHost.Instance.EditorPasteRotation);
		}

		if (!_agreementShownThisSession)
		{
			_agreementShownThisSession = true;
			ShowAgreementModal();
		}

		foreach (Control child in _contentFile.GetChildren())
		{
			child.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		}

			SwitchModule(EditorModule.Terrain);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"CRITICAL ERROR IN MAPEDITORHUD _READY: {ex}");
			System.IO.File.WriteAllText(@"C:\temp\Realm\ready_exception.txt", ex.ToString());
			throw;
		}
	}

	public override void _Process(double delta)
	{
		if (GameHost.Instance != null)
		{
			_viewModel.UpdateFromHost();
			var mousePos = GetViewport().GetMousePosition();
			var hit = GameHost.Instance.Call("RaycastFromMouse", mousePos).AsGodotDictionary();
			if (hit != null && hit.ContainsKey("position"))
			{
				Vector3 pos = hit["position"].AsVector3();
				_viewModel.StatusText = GameHost.Instance.GetTerrainStatusString(pos);
			}
		}

		_topBarController?.Update(_viewModel);
		if (!_isDraggingSlider)
		{
			_brushSettingsController?.Update(_viewModel);
			_placementSettingsController?.Update(_viewModel);
			UpdatePasteRotationExternal(_viewModel.PasteRotation);
		}
		_inspectorController?.Update(_viewModel);
		_pathingPanelController?.Update(_viewModel);
		_minimapController?.Update(_viewModel);

		if (GameHost.Instance != null)
		{
			bool hasSelectedObject = GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject);
			bool shouldShowInspector = hasSelectedObject && (GameHost.Instance.ActiveEditorTool == GameHost.EditorTool.SelectMove);
			if (_accordionInspector != null && _accordionInspector.Visible != shouldShowInspector)
			{
				_accordionInspector.Visible = shouldShowInspector;
				if (shouldShowInspector)
				{
					if (_contentInspector != null) _contentInspector.Visible = true;
					if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ " + TranslationServer.Translate("Selected Object Inspector");
				}
				_accordionContainer?.QueueSort();
			}
		}
	}

	public void ShowFeedbackExternal(string text)
	{
		ShowFeedback(text);
	}

	public void ShowFeedback(string text)
	{
		_topBarController?.ShowFeedback(text);
	}

	public int GetSelectedPathingMask()
	{
		int mask = 0;
		if (_chkGround != null && _chkGround.ButtonPressed) mask |= EditableTerrain.PATHING_GROUND;
		if (_chkFlying != null && _chkFlying.ButtonPressed) mask |= EditableTerrain.PATHING_FLYING;
		if (_chkShallowWater != null && _chkShallowWater.ButtonPressed) mask |= EditableTerrain.PATHING_SHALLOW_WATER;
		if (_chkDeepWater != null && _chkDeepWater.ButtonPressed) mask |= EditableTerrain.PATHING_DEEP_WATER;
		if (_chkBuildable != null && _chkBuildable.ButtonPressed) mask |= EditableTerrain.PATHING_BUILDABLE;
		return mask;
	}

	public bool IsPathingAddMode()
	{
		if (_optPathingMode == null) return true;
		return _optPathingMode.Selected == 0;
	}

	public string GetEyedropperMode()
	{
		if (_optEyedropperMode == null) return "all";
		return _optEyedropperMode.Selected switch
		{
			1 => "3d",
			2 => "decal",
			3 => "terrain",
			4 => "height",
			_ => "all"
		};
	}

	private void ApplyThemeStyles()
	{
		if (_panelLeft != null) _panelLeft.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));
		if (_panelRight != null) _panelRight.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));

		var topBar = GetNode<PanelContainer>("TopBar");
		topBar.SetAnchorsPreset(LayoutPreset.CenterBottom);
		topBar.GrowHorizontal = GrowDirection.Both;
		topBar.GrowVertical = GrowDirection.Begin;
		topBar.OffsetLeft = -250;
		topBar.OffsetRight = 250;
		topBar.OffsetTop = -70;
		topBar.OffsetBottom = -10;

		GetNode<Label>("TopBar/HBox/TitleLabel").Visible = false;

		var alphaStyle = new StyleBoxFlat();
		alphaStyle.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.6f);
		alphaStyle.BorderColor = UIStyle.ColorCyanGlow;
		alphaStyle.SetBorderWidthAll(2);
		alphaStyle.CornerRadiusTopLeft = 6;
		alphaStyle.CornerRadiusTopRight = 6;
		alphaStyle.CornerRadiusBottomLeft = 6;
		alphaStyle.CornerRadiusBottomRight = 6;
		topBar.AddThemeStyleboxOverride("panel", alphaStyle);

		var hBox = GetNode<HBoxContainer>("TopBar/HBox");
		hBox.Alignment = BoxContainer.AlignmentMode.Center;

		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;

		UIStyle.ApplyTitle(_feedbackLabel, "", 24);
	}

	public void UpdateSelectedObjectInfo()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			if (_lblInfoText != null) _lblInfoText.Visible = false;
			if (_inspectorPanel != null) _inspectorPanel.Visible = true;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = true;
				if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ Selected Object Inspector";
				if (_contentInspector != null) _contentInspector.Visible = true;
			}
			string nameStr = selected.Name;
			Vector3 pos = Vector3.Zero;
			Vector3 rot = Vector3.Zero;
			Vector3 scale = Vector3.One;
			if (selected is Node3D node3D)
			{
				pos = node3D.Position;
				rot = node3D.RotationDegrees;
				scale = node3D.Scale;
			}
			string typeStr = "";
			if (selected is Unit3D unit)
			{
				typeStr = "UNIT";
				nameStr = $"{unit.UnitId.ToUpper()}";
			}
			else if (selected is Prop3D prop)
			{
				typeStr = "PROP";
				nameStr = prop.PropId.ToUpper();
			}
			else if (selected is Decal decal)
			{
				typeStr = "DECAL";
				nameStr = "DECAL";
			}
			
			if (_viewModel != null)
			{
				_viewModel.HasInspectorSelection = true;
				_viewModel.InspectorTitle = $"SELECTED: {nameStr}\n[{typeStr}]";
				_viewModel.InspectorPos = $"Pos: {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}\nRot: {rot.Y:F1}° | Scale: {scale.X:F2}x";
			}
		}
		else
		{
			if (_lblInfoText != null) _lblInfoText.Visible = true;
			if (_inspectorPanel != null) _inspectorPanel.Visible = false;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = false;
			}
			if (_viewModel != null)
			{
				_viewModel.HasInspectorSelection = false;
				_viewModel.InspectorTitle = "No Selection";
				_viewModel.InspectorPos = "Position: (0, 0)";
			}
			TriggerToolSelection(GameHost.Instance.ActiveEditorTool, _activeToolButton, GameHost.Instance.ActivePlaceId);
		}
	}

	public void SaveMapActionExternal()
	{
		SaveMapAction();
	}

	public void UpdateWaterEnabledExternal(bool enabled)
	{
		if (_chkWaterEnabled != null)
		{
			_chkWaterEnabled.ButtonPressed = enabled;
		}
	}

	public void UpdateGridSnapExternal(bool snap)
	{
		if (_btnToggleSnap != null)
		{
			_btnToggleSnap.Text = snap ? "🔲 SNAP TO GRID: ON" : "🔲 SNAP TO GRID: OFF";
		}
	}

	public void OpenCoordinateNamingPanel(int minX, int minZ, int maxX, int maxZ)
	{
		_pendingCoordinateMinX = minX;
		_pendingCoordinateMinZ = minZ;
		_pendingCoordinateMaxX = maxX;
		_pendingCoordinateMaxZ = maxZ;
		if (_btnCommitCoordinate != null) _btnCommitCoordinate.Visible = true;
		if (_txtCoordinateName != null) _txtCoordinateName.GrabFocus();
	}

	public void RefreshCoordinateListExternal()
	{
		if (_coordinateListVBox == null) return;
		foreach (var child in _coordinateListVBox.GetChildren())
		{
			child.QueueFree();
		}
		var coordinates = GameHost.Instance?.EditorCoordinates;
		if (coordinates == null) return;
		foreach (var coord in coordinates)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			_coordinateListVBox.AddChild(row);

			var btnSelect = new Button();
			btnSelect.Text = $"{coord.Name}  ({coord.MinX:F0},{coord.MinZ:F0}) → ({coord.MaxX:F0},{coord.MaxZ:F0})";
			btnSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnSelect.Flat = true;
			btnSelect.Alignment = HorizontalAlignment.Left;
			btnSelect.AddThemeFontSizeOverride("font_size", 11);
			btnSelect.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			string coordinateName = coord.Name;
			btnSelect.Pressed += () =>
			{
				GameHost.Instance?.SelectCoordinateExternal(coordinateName);
			};
			row.AddChild(btnSelect);

			var btnDel = new Button();
			btnDel.Set("icon_max_width", 0);
			SetupButton(btnDel, "✕", () =>
			{
				GameHost.Instance?.DeleteCoordinateExternal(coordinateName);
				RefreshCoordinateListExternal();
			}, 10, $"Delete coordinate '{coordinateName}'");
			btnDel.CustomMinimumSize = new Vector2(28, 24);
			row.AddChild(btnDel);
		}
		WriteCoordinatesCsFile();
	}

	public void WriteCoordinatesCsFile()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath)) return;
		if (GameHost.Instance == null) return;

		string filePath = System.IO.Path.Combine(_tempWorkspacePath, "Coordinates.cs");
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("using Realm.MapAPI;");
		sb.AppendLine();
		sb.AppendLine("namespace Realm.Maps;");
		sb.AppendLine();
		sb.AppendLine("public static class Coordinates");
		sb.AppendLine("{");

		foreach (var coord in GameHost.Instance.EditorCoordinates)
		{
			string varName = System.Text.RegularExpressions.Regex.Replace(coord.Name, @"[^a-zA-Z0-9_]", "_");
			if (varName.Length > 0 && char.IsDigit(varName[0])) varName = "_" + varName;

			var invariant = System.Globalization.CultureInfo.InvariantCulture;
			sb.AppendLine($"    public static readonly Coordinate {varName} = new Coordinate(");
			sb.AppendLine(string.Format(invariant, "        new System.Numerics.Vector3({0:F2}f, 0f, {1:F2}f),", coord.MinX, coord.MinZ));
			sb.AppendLine(string.Format(invariant, "        new System.Numerics.Vector3({0:F2}f, 0f, {1:F2}f)", coord.MaxX, coord.MaxZ));
			sb.AppendLine("    );");
			sb.AppendLine();
		}

		sb.AppendLine("}");

		try
		{
			System.IO.File.WriteAllText(filePath, sb.ToString());
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to write Coordinates.cs: {ex.Message}");
		}
	}

	public void UpdateGridOverlayExternal(GameHost.GridOverlayMode mode)
	{
		if (_btnToggleGrid != null)
		{
			_btnToggleGrid.Text = mode switch
			{
				GameHost.GridOverlayMode.Off => "🌐 GRID OVERLAY: OFF",
				GameHost.GridOverlayMode.Mesh => "🌐 GRID OVERLAY: ON",
				_ => "🌐 GRID OVERLAY: OFF"
			};
		}
	}

	public void UpdateCameraBoundsOverlayExternal(bool visible)
	{
		if (_btnToggleCameraBounds != null)
		{
			_btnToggleCameraBounds.Text = visible ? "📹 CAM BOUNDS: ON" : "📹 CAM BOUNDS: OFF";
		}
	}

	private void EnsureCameraBoundsVisible()
	{
		if (GameHost.Instance != null && !GameHost.Instance.EditorCameraBoundsVisible)
		{
			GameHost.Instance.EditorCameraBoundsVisible = true;
			GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
			UpdateCameraBoundsOverlayExternal(true);
		}
	}


	public void UpdateCameraBoundsUI()
	{
		if (GameHost.Instance == null) return;
		if (_lblCamLeftVal != null) _lblCamLeftVal.Text = $"L: {GameHost.Instance.EditorCameraBoundsLeft:F0}m";
		if (_lblCamRightVal != null) _lblCamRightVal.Text = $"R: {GameHost.Instance.EditorCameraBoundsRight:F0}m";
		if (_lblCamTopVal != null) _lblCamTopVal.Text = $"T: {GameHost.Instance.EditorCameraBoundsTop:F0}m";
		if (_lblCamBottomVal != null) _lblCamBottomVal.Text = $"B: {GameHost.Instance.EditorCameraBoundsBottom:F0}m";
		
		if (GameHost.Instance.GroundTerrain != null)
		{
			if (_lblMapWidthVal != null) _lblMapWidthVal.Text = $"W: {GameHost.Instance.GroundTerrain.Width}";
			if (_lblMapHeightVal != null) _lblMapHeightVal.Text = $"H: {GameHost.Instance.GroundTerrain.Depth}";
		}
	}

	public void UpdateSelectedSkyboxExternal(string path)
	{
		if (_optSkybox == null) return;
		string file = System.IO.Path.GetFileName(path);
		int index = _skyboxFiles.IndexOf(file);
		if (index >= 0)
		{
			_optSkybox.Selected = index;
		}
	}

	public void UpdatePathingOverlayExternal(bool visible)
	{
	}

	public void ToggleVSCodeEditor()
	{
		if (OperatingSystem.IsWindows())
		{
			bool isVisible = !VSCodeManager.Instance.IsVisible;
			if (isVisible)
			{
				GenerateVSCodeFilesExternal();
			}
			VSCodeManager.Instance.SetVisible(isVisible);
		}
	}

	public void BackToHubAction()
	{
		if (GameHost.Instance != null && GameHost.Instance.EditorHasUnsavedChanges)
		{
			ShowConfirmationDialog(
				"Unsaved changes will be lost. Are you sure you want to exit?",
				() => UIManager.Instance.TransitionTo(GameScreen.MainMenu)
			);
		}
		else
		{
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		}
	}


	public void PublishMapActionExternal()
	{
		var overlay = new ColorRect();
		overlay.Name = "PublishInstructionsOverlay";
		overlay.Color = new Color(0, 0, 0, 0.7f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(1200, 800);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
		style.BorderWidthTop = 2; style.BorderWidthBottom = 2; style.BorderWidthLeft = 2; style.BorderWidthRight = 2;
		style.BorderColor = new Color(0.3f, 0.3f, 0.35f, 1f);
		style.CornerRadiusTopLeft = 4; style.CornerRadiusTopRight = 4; style.CornerRadiusBottomLeft = 4; style.CornerRadiusBottomRight = 4;
		panel.AddThemeStyleboxOverride("panel", style);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(1180, 780);
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddChild(vbox);
		panel.AddChild(margin);

		var title = new Label();
		title.Text = "Publish Map Instructions";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 24);
		vbox.AddChild(title);

		var optType = new OptionButton();
		optType.AddItem("Custom Arcade Map", 0);
		optType.AddItem("Reusable Asset Pack", 1);
		optType.Selected = _optMapType != null ? _optMapType.Selected : 0;
		vbox.AddChild(optType);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		var instructionsText = new RichTextLabel();
		instructionsText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		instructionsText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		instructionsText.BbcodeEnabled = true;
		scroll.AddChild(instructionsText);
		vbox.AddChild(scroll);

		string textArcade = "🚀 [b]Publishing Your Custom Map[/b]\nTo keep the public library high-quality, all new maps start in a Beta-Testing Phase.\n\nOnce your map hits our community play-time metrics (gaining enough unique players, ratings, & community playtime), it will automatically graduate and be available for discovery on community maps screens.\n\nIt is up to you to share it with the community & market it until you hit that threshold.\nYour map is ready to play right now! Host a lobby with your map & wait for players to join.\nMessage the community via discord channels, etc to explain your map & convince them to try it. If they enjoy it, they will probably re-host it, which will help you hit the graduation threshold more quickly.\n\nWhile in testing, your map name will include a prefix [Beta-Testing] so players know it's an active work-in-progress. However, you should still do as much personal testing as possible before public hosting to avoid a frustrating experience for your testers.\n\nAfter graduation, your map name will be permanently reserved to your creator profile so no one else can use that same name.";
		string textAssetPack = "📦 [b]Publishing a Reusable Asset Pack[/b]\nWant to share your custom 3D models, audio, or code scripts with other map makers?\n\nIn Realm, Asset Packs are published as playable Showcase/Demo Maps.\n\n[b]Build a Playground:[/b] Turn your asset pack into a map where players can preview the functionality provided by your systems, view your models, etc.\n\n[b]Gather Community Metrics:[/b] Just like a regular map, your asset pack will start in a \"beta\" phase before being promoted on community discovery pages. Read the \"Custom Arcade Map\" section for more information.\n\n[b]Easy Importing:[/b] Once a player has a copy of your map, they can import assets from it into their maps via the map editor.\n\n[b]Automatic Credit:[/b] When creators import from you, the system tracks your files' signatures and automatically adds your info to their map credits.";

		Action updateText = () => {
			instructionsText.Text = optType.Selected == 0 ? textArcade : textAssetPack;
		};
		updateText();
		optType.ItemSelected += (_) => updateText();

		var hbox = new HBoxContainer();
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var btnClose = new Button();
		btnClose.Text = "Close";
		btnClose.CustomMinimumSize = new Vector2(120, 40);
		btnClose.Pressed += () => overlay.QueueFree();
		hbox.AddChild(btnClose);
	}


	public void UndoAction()
	{
		EditorHistoryManager.Undo();
		ShowFeedback("Undo Action performed");
	}

	public void RedoAction()
	{
		EditorHistoryManager.Redo();
		ShowFeedback("Redo Action performed");
	}

	public void DeleteSelectedObjectAction()
	{
		DeleteSelectedObject();
	}

	private void DeleteSelectedObject()
	{
		if (GameHost.Instance == null)
		{
			ShowFeedback("[Debug] DeleteSelectedObject: GameHost.Instance is NULL!");
			return;
		}
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			Vector3 pos = (selected is Node3D n) ? n.Position : Vector3.Zero;
			GameHost.Instance.SelectedEditorObject = null;
			var action = GameHost.Instance.DeleteObjectAtWithUndo(selected, pos);
			if (action != null)
			{
				EditorHistoryManager.RecordAction(action);
			}
			else
			{
				GameHost.Instance.DeleteNodeExternal(selected);
			}
		}
	}

	public void RotateSelectedObjectAction(float angleDelta)
	{
		RotateSelectedObject(angleDelta);
	}

	public void ScaleSelectedObjectAction(float scaleMultiplier)
	{
		if (scaleMultiplier < 0f)
		{
			ResetScaleSelectedObject();
		}
		else
		{
			ScaleSelectedObject(scaleMultiplier - 1.0f);
		}
	}

	public void SetSpawnAsEnemy(bool isEnemy)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.PlaceUnitIsEnemy = isEnemy;
		}
	}

	public void SelectCategoryItemExternal(string category, string filename)
	{
		_entityPaletteController?.SelectCategoryItemExternal(category, filename);
	}

	public void SelectPickedUnitOrProp(string id, bool isBuilding)
	{
		if (isBuilding)
		{
			_entityPaletteController?.SelectCategoryItemExternal("Buildings", id + ".glb");
		}
		else
		{
			string charactersPath = "res://Assets/3d/Characters";
			if (FileAccess.FileExists($"{charactersPath}/{id}.glb") || FileAccess.FileExists($"{charactersPath}/{id}.gltf"))
			{
				_entityPaletteController?.SelectCategoryItemExternal("Characters", id + ".glb");
			}
			else
			{
				_entityPaletteController?.SelectCategoryItemExternal("Props", id + ".glb");
			}
		}
	}

	public void SelectPickedDecal(string decalId)
	{
		_entityPaletteController?.SelectCategoryItemExternal("Decals", decalId);
	}

	public void SelectPaintSwatchByIndex(int index)
	{
		if (index >= 0 && index < 12)
		{
			HighlightSwatch(_swatchButtons[index]);
			TriggerToolSelection(GameHost.EditorTool.PaintGrass, _swatchButtons[index]);
		}
	}

	public void UpdateBrushShapeExternal(bool isSquare)
	{
		if (_btnBrushShape != null)
		{
			_btnBrushShape.Text = isSquare ? "🔳 BRUSH: SQUARE" : "⚪ BRUSH: CIRCLE";
		}
	}

	public void UpdateRotationExternal(float angle)
	{
		if (_lblPlacementRotateValue != null) _lblPlacementRotateValue.Text = angle.ToString("F0") + "°";
		if (_sldPlacementRotate != null) _sldPlacementRotate.Value = angle;
	}

	public void UpdatePasteRotationExternal(float angle)
	{
		if (_lblPasteRotation != null) _lblPasteRotation.Text = angle.ToString("F0") + "°";
		if (_sldPasteRotation != null) _sldPasteRotation.Value = angle;
	}

	public void UpdateScaleExternal(float scale)
	{
		if (_lblPlacementScaleValue != null) _lblPlacementScaleValue.Text = scale.ToString("F1") + "x";
		if (_sldPlacementScale != null) _sldPlacementScale.Value = scale;
	}

	public void UpdateBrushSizeExternal(float size)
	{
		if (_sldBrushSize != null)
		{
			_sldBrushSize.Value = size;
		}
		if (_lblBrushSizeValue != null)
		{
			_lblBrushSizeValue.Text = size.ToString("F1");
		}
	}

	public void UpdateCameraAngleButtonText(bool isTopDown)
	{
		if (_btnCameraAngle != null)
		{
			_btnCameraAngle.Text = isTopDown ? "📐 TopDown" : "📐 Tilt";
		}
	}

	public void UpdateBrushStrengthExternal(float strength)
	{
		if (_sldBrushStrength != null)
		{
			_sldBrushStrength.Value = strength;
		}
		if (_lblBrushStrengthValue != null)
		{
			_lblBrushStrengthValue.Text = strength.ToString("F1");
		}
	}

	public void UpdateBlockModeExternal(bool enabled)
	{
		if (_chkBlockMode != null)
		{
			_chkBlockMode.ButtonPressed = enabled;
		}
	}

	public void UpdateBlockLevelHeightExternal(float step)
	{
		if (_sldBlockStep != null)
		{
			_sldBlockStep.Value = step;
		}
		if (_lblBlockStepValue != null)
		{
			_lblBlockStepValue.Text = step.ToString("F1") + " m";
		}
	}

	public void UpdateWaterHeightExternal(float height)
	{
		if (_sldWaterHeight != null)
		{
			_sldWaterHeight.Value = height;
		}
		if (_lblWaterHeightValue != null)
		{
			_lblWaterHeightValue.Text = height.ToString("F1") + " m";
		}
	}

	public void SelectToolFromHotkey(GameHost.EditorTool tool)
	{
		Button targetBtn = null;
		switch (tool)
		{
			case GameHost.EditorTool.Raise: targetBtn = _btnRaise; break;
			case GameHost.EditorTool.Lower: targetBtn = _btnLower; break;
			case GameHost.EditorTool.Smooth: targetBtn = _btnSmooth; break;
			case GameHost.EditorTool.Plateau: targetBtn = _btnPlateau; break;
			case GameHost.EditorTool.Ramp: targetBtn = _btnRamp; break;
			case GameHost.EditorTool.Noise: targetBtn = _btnNoise; break;
			case GameHost.EditorTool.PaintGrass: targetBtn = _btnTextureBrush; break;
			case GameHost.EditorTool.FloodFill: targetBtn = _btnFloodFill; break;
			case GameHost.EditorTool.PaintPathing: targetBtn = _btnPathingBrush; break;
			case GameHost.EditorTool.FloodFillPathing: targetBtn = _btnFloodFillPathing; break;
			case GameHost.EditorTool.DrawCoordinate: targetBtn = _btnDrawCoordinate; break;
			case GameHost.EditorTool.SelectArea: targetBtn = _btnSelectArea; break;
			case GameHost.EditorTool.PasteArea: targetBtn = _btnPaste; break;
			case GameHost.EditorTool.PlaceUnit:
			case GameHost.EditorTool.PlaceProp:
			case GameHost.EditorTool.PlaceDecal:
				targetBtn = _btnAddObject;
				break;
			case GameHost.EditorTool.DeleteObject: targetBtn = _btnDeleteObject; break;
			case GameHost.EditorTool.SelectMove: targetBtn = _btnSelectMove; break;
			case GameHost.EditorTool.Eyedropper: targetBtn = _btnEyedropper; break;
		}
		if (targetBtn != null)
		{
			TriggerToolSelection(tool, targetBtn, GameHost.Instance?.ActivePlaceId ?? "");
		}
	}

	private void HighlightSwatch(Button selectedSwatch)
	{
		if (!GodotObject.IsInstanceValid(_swatchHighlightPanel))
		{
			_swatchHighlightPanel = new Panel();
			_swatchHighlightPanel.Name = "SwatchHighlightPanel";
			_swatchHighlightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
			_swatchHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
			_swatchHighlightPanel.GrowHorizontal = GrowDirection.Both;
			_swatchHighlightPanel.GrowVertical = GrowDirection.Both;

			var style = new StyleBoxFlat();
			style.BgColor = new Color(0, 0, 0, 0);
			style.BorderColor = UIStyle.ColorCyanGlow;
			style.SetBorderWidthAll(3);
			style.CornerRadiusTopLeft = 4;
			style.CornerRadiusTopRight = 4;
			style.CornerRadiusBottomLeft = 4;
			style.CornerRadiusBottomRight = 4;
			_swatchHighlightPanel.AddThemeStyleboxOverride("panel", style);
		}

		if (_swatchHighlightPanel.GetParent() != null)
		{
			_swatchHighlightPanel.GetParent().RemoveChild(_swatchHighlightPanel);
		}

		if (selectedSwatch != null)
		{
			selectedSwatch.AddChild(_swatchHighlightPanel);
		}
	}

	private void HighlightCliffSwatch(Button selectedSwatch)
	{
		if (!GodotObject.IsInstanceValid(_swatchCliffHighlightPanel))
		{
			_swatchCliffHighlightPanel = new Panel();
			_swatchCliffHighlightPanel.Name = "SwatchCliffHighlightPanel";
			_swatchCliffHighlightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
			_swatchCliffHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
			_swatchCliffHighlightPanel.GrowHorizontal = GrowDirection.Both;
			_swatchCliffHighlightPanel.GrowVertical = GrowDirection.Both;

			var style = new StyleBoxFlat();
			style.BgColor = new Color(0, 0, 0, 0);
			style.BorderColor = new Color(0.9f, 0.45f, 0.1f); // Vibrant orange/gold for cliff
			style.SetBorderWidthAll(3);
			style.CornerRadiusTopLeft = 4;
			style.CornerRadiusTopRight = 4;
			style.CornerRadiusBottomLeft = 4;
			style.CornerRadiusBottomRight = 4;
			_swatchCliffHighlightPanel.AddThemeStyleboxOverride("panel", style);
		}

		if (_swatchCliffHighlightPanel.GetParent() != null)
		{
			_swatchCliffHighlightPanel.GetParent().RemoveChild(_swatchCliffHighlightPanel);
		}

		if (selectedSwatch != null)
		{
			selectedSwatch.AddChild(_swatchCliffHighlightPanel);
		}
	}

	private Button GetButtonForTool(GameHost.EditorTool tool, string placeId)
	{
		return tool switch
		{
			GameHost.EditorTool.Raise => _btnRaise,
			GameHost.EditorTool.Lower => _btnLower,
			GameHost.EditorTool.Smooth => _btnSmooth,
			GameHost.EditorTool.Plateau => _btnPlateau,
			GameHost.EditorTool.Ramp => _btnRamp,
			GameHost.EditorTool.Noise => _btnNoise,
			GameHost.EditorTool.PaintPathing => _btnPathingBrush,
			GameHost.EditorTool.FloodFillPathing => _btnFloodFillPathing,
			GameHost.EditorTool.DrawCoordinate => _btnDrawCoordinate,
			GameHost.EditorTool.SelectArea => _btnSelectArea,
			GameHost.EditorTool.SelectMove => _btnSelectMove,
			GameHost.EditorTool.DeleteObject => _btnDeleteObject,
			GameHost.EditorTool.PaintGrass => GetTextureSwatchButton(placeId),
			GameHost.EditorTool.PaintDirt => GetTextureSwatchButton(placeId),
			GameHost.EditorTool.PaintRock => GetTextureSwatchButton(placeId),
			GameHost.EditorTool.PaintSand => GetTextureSwatchButton(placeId),
			GameHost.EditorTool.FloodFill => _btnFloodFill,
			GameHost.EditorTool.PlacePropClump => _btnClumpBrush,
			GameHost.EditorTool.Eyedropper => _btnEyedropper,
			_ => null
		};
	}

	private Button GetTextureSwatchButton(string placeId)
	{
		for (int i = 0; i < 12; i++)
		{
			if (_swatchPaths[i] == placeId)
			{
				return _swatchButtons[i];
			}
		}
		return _btnTextureBrush;
	}

	public void TriggerToolSelection(GameHost.EditorTool tool, Button btn, string placeId = "")
	{
		if (GameHost.Instance == null) return;

		if (tool != GameHost.EditorTool.SelectMove)
		{
			GameHost.Instance.SelectedEditorObject = null;
		}

		if (_activeToolButton != null)
		{
			_activeToolButton.RemoveThemeStyleboxOverride("normal");
		}

		_activeToolButton = btn;
		if (_activeToolButton != null)
		{
			if (_activeToolButton.Name.ToString().StartsWith("Swatch"))
			{
				HighlightSwatch(_activeToolButton as Button);
			}
			else
			{
				_activeToolButton.AddThemeStyleboxOverride("normal", _highlightStyle);
				HighlightSwatch(null);
			}
		}
		else
		{
			HighlightSwatch(null);
		}

		GameHost.Instance.ActiveEditorTool = tool;
		if (tool != GameHost.EditorTool.Ramp)
		{
			GameHost.Instance.ClearRampStartPosExternal();
		}
		GameHost.Instance.ActivePlaceId = placeId;


		EditorModule targetModule = _activeModule;
		if (tool == GameHost.EditorTool.Raise ||
			tool == GameHost.EditorTool.Lower ||
			tool == GameHost.EditorTool.Smooth ||
			tool == GameHost.EditorTool.Plateau ||
			tool == GameHost.EditorTool.Ramp ||
			tool == GameHost.EditorTool.Noise)
		{
			targetModule = EditorModule.Terrain;
		}
		else if (tool == GameHost.EditorTool.PaintGrass ||
				 tool == GameHost.EditorTool.PaintDirt ||
				 tool == GameHost.EditorTool.PaintRock ||
				 tool == GameHost.EditorTool.PaintSand ||
				 tool == GameHost.EditorTool.FloodFill)
		{
			targetModule = EditorModule.TextureDeco;
		}
		else if (tool == GameHost.EditorTool.DrawCoordinate)
		{
			targetModule = EditorModule.Coordinates;
		}
		else if (tool == GameHost.EditorTool.PaintPathing ||
				 tool == GameHost.EditorTool.FloodFillPathing)
		{
			targetModule = EditorModule.Pathing;
		}
		else if (tool == GameHost.EditorTool.PlaceUnit ||
				 tool == GameHost.EditorTool.PlaceProp ||
				 tool == GameHost.EditorTool.PlacePropClump ||
				 tool == GameHost.EditorTool.PlaceDecal ||
				 tool == GameHost.EditorTool.DeleteObject ||
				 tool == GameHost.EditorTool.SelectMove)
		{
			targetModule = EditorModule.Objects;
		}
		else if (tool == GameHost.EditorTool.SelectArea ||
				 tool == GameHost.EditorTool.PasteArea)
		{
			targetModule = EditorModule.Clipboard;
		}

		if (targetModule != _activeModule)
		{
			_activeModule = targetModule;
			UpdateModuleSwitchButtons();
			if (_panelTerrainVBox != null) _panelTerrainVBox.Visible = (targetModule == EditorModule.Terrain);
			if (_panelDecoVBox != null) _panelDecoVBox.Visible = (targetModule == EditorModule.TextureDeco);
			if (_panelEnv != null) _panelEnv.Visible = (targetModule == EditorModule.TextureDeco);
			if (_panelPathingVBox != null) _panelPathingVBox.Visible = (targetModule == EditorModule.Pathing);
			if (_panelCoordinatesVBox != null) _panelCoordinatesVBox.Visible = (targetModule == EditorModule.Coordinates);
			if (_panelObjects != null) _panelObjects.Visible = (targetModule == EditorModule.Objects);
			if (_panelClipboard != null) _panelClipboard.Visible = (targetModule == EditorModule.Clipboard);
		}

		if (tool == GameHost.EditorTool.PaintPathing || tool == GameHost.EditorTool.FloodFillPathing)
		{
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			GameHost.Instance?.UpdatePathingOverlay();
		}
		else if (tool == GameHost.EditorTool.DrawCoordinate)
		{
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			if (_btnCommitCoordinate != null) _btnCommitCoordinate.Visible = false;
			GameHost.Instance?.UpdatePathingOverlay();
		}
		else
		{
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			GameHost.Instance?.UpdatePathingOverlay();
		}

		UpdateSidebarMorph(tool);

		string toolName = tool.ToString().ToUpper();
		if (!string.IsNullOrEmpty(placeId)) toolName += $" ({placeId.ToUpper()})";
		
		if (_statusLabel != null)
		{
			_statusLabel.Text = $"ACTIVE TOOL: {toolName}";
		}

		if (_lblInfoText != null)
		{
			switch (tool)
			{
				case GameHost.EditorTool.Raise:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings.");
					break;
				case GameHost.EditorTool.Ramp:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel.");
					break;
				case GameHost.EditorTool.Lower:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings.");
					break;
				case GameHost.EditorTool.Plateau:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Plateau\n\nDrag left click to flatten terrain to the elevation of your initial click point.");
					break;
				case GameHost.EditorTool.Smooth:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations.");
					break;
				case GameHost.EditorTool.PaintGrass:
				case GameHost.EditorTool.PaintDirt:
				case GameHost.EditorTool.PaintRock:
				case GameHost.EditorTool.PaintSand:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh.");
					break;
				case GameHost.EditorTool.FloodFill:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch.");
					break;
				case GameHost.EditorTool.SelectArea:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area.");
					break;
				case GameHost.EditorTool.PasteArea:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Affected Layers checkboxes to filter what is pasted (Textures, HeightMap, Units / Props, Pathing).");
					break;
				case GameHost.EditorTool.PlaceUnit:
					string alignment = (GameHost.Instance != null && GameHost.Instance.PlaceUnitIsEnemy) ? TranslationServer.Translate("Enemy (Orc)") : TranslationServer.Translate("Player (Alliance)");
					_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Unit\n\nLeft-click on the ground to spawn a {0} aligned with {1}."), placeId.ToUpper(), alignment);
					break;
				case GameHost.EditorTool.PlaceProp:
					_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Prop\n\nLeft-click on the ground to spawn static decorative object: {0}."), placeId.ToUpper());
					break;
				case GameHost.EditorTool.PlacePropClump:
					_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Clump Brush\n\nDrag left click on the ground to paint clumps of static props: {0} based on Density and Scale Variation settings. Uses texture brush shape (Circle/Square)."), placeId.ToUpper());
					break;
				case GameHost.EditorTool.PlaceDecal:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply.");
					break;
				case GameHost.EditorTool.DeleteObject:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map.");
					break;
				case GameHost.EditorTool.SelectMove:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Use Alt + MouseWheel to scale, or Delete to delete.");
					break;
				case GameHost.EditorTool.Eyedropper:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height.");
					break;
				case GameHost.EditorTool.Noise:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings.");
					break;
				case GameHost.EditorTool.PaintPathing:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove.");
					break;
				case GameHost.EditorTool.FloodFillPathing:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Flood Fill Pathing\n\nClick once on the terrain map to flood-fill pathing properties (ground, flying, water, etc.) across an area sharing the same texture color until hitting a boundary. Use checkboxes to select layers, and Mode to Add/Remove.");
					break;
				case GameHost.EditorTool.None:
					_lblInfoText.Text = TranslationServer.Translate("Select a tool from the panels to begin terrain modification.");
					break;
			}
		}
		UpdateTextureLabels();
	}

	private void InitializeTempWorkspace()
	{
		_tempWorkspacePath = ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
		if (!ReturningFromTest)
		{
			ClearTempWorkspaceExternal();
			System.IO.Directory.CreateDirectory(_tempWorkspacePath);
			MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "CustomMap");
		}

		_lastTerrainSyncTime = 0;
		_lastMetadataSyncTime = 0;

		var syncTimer = new Godot.Timer();
		syncTimer.WaitTime = 1.0f;
		syncTimer.Autostart = true;
		syncTimer.Timeout += OnSyncTimerTimeout;
		AddChild(syncTimer);

		// Load properties from map.json
		LoadMapProperties();

		// Check creator registration on editor startup
		CheckCreatorRegistrationAndPrompt();
	}

	public void ClearTempWorkspaceExternal()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;

		try
		{
			foreach (var file in System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.TopDirectoryOnly))
			{
				var fileAttributes = System.IO.File.GetAttributes(file);
				if ((fileAttributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly)
				{
					System.IO.File.SetAttributes(file, fileAttributes & ~System.IO.FileAttributes.ReadOnly);
				}
				System.IO.File.Delete(file);
			}

			foreach (var directory in System.IO.Directory.GetDirectories(_tempWorkspacePath))
			{
				string name = System.IO.Path.GetFileName(directory);
				if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				ClearDirectoryReadOnly(directory);
				System.IO.Directory.Delete(directory, true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ClearTempWorkspaceExternal] Error: {ex.Message}");
		}

		_lastTerrainSyncTime = 0;
		_lastMetadataSyncTime = 0;
	}

	private static void ClearDirectoryReadOnly(string targetDir)
	{
		foreach (var file in System.IO.Directory.GetFiles(targetDir, "*", System.IO.SearchOption.AllDirectories))
		{
			var attrs = System.IO.File.GetAttributes(file);
			if ((attrs & System.IO.FileAttributes.ReadOnly) != 0)
			{
				System.IO.File.SetAttributes(file, attrs & ~System.IO.FileAttributes.ReadOnly);
			}
		}
	}

	public void GenerateVSCodeFilesExternal()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath)) return;
		string scriptPath = System.IO.Path.Combine(_tempWorkspacePath, "MapScript.cs");
		string unitsPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
		System.IO.Directory.CreateDirectory(_tempWorkspacePath);
		MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "CustomMap");
	}

	private long GetLastWriteTimeSafe(string path)
	{
		if (!System.IO.File.Exists(path)) return 0;
		return System.IO.File.GetLastWriteTimeUtc(path).Ticks;
	}

	private long GetMaxTerrainWriteTime(string baseTerrainJsonPath)
	{
		long maxTime = GetLastWriteTimeSafe(baseTerrainJsonPath);
		string dir = System.IO.Path.GetDirectoryName(baseTerrainJsonPath);
		if (!string.IsNullOrEmpty(dir))
		{
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_heights.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_indices.png")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_weights.png")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_pathing.png")));
		}
		return maxTime;
	}

	private void OnSyncTimerTimeout()
	{
		if (GameHost.Instance == null || _isSyncing) return;
		_isSyncing = true;
		
		string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");

		long currentTerrainWrite = GetMaxTerrainWriteTime(terrainPath);
		long currentMetadataWrite = GetLastWriteTimeSafe(metadataPath);

		bool terrainModifiedOnDisk = currentTerrainWrite > _lastTerrainSyncTime;
		bool metadataModifiedOnDisk = currentMetadataWrite > _lastMetadataSyncTime;

		if (terrainModifiedOnDisk || metadataModifiedOnDisk)
		{
			if (terrainModifiedOnDisk)
			{
				GameHost.Instance.LoadMapFromFile(terrainPath);
				_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
			}
			if (metadataModifiedOnDisk)
			{
				_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
			}
			
			GameHost.Instance.EditorHasUnsavedChanges = false;
		}
		else if (GameHost.Instance.EditorHasUnsavedChanges)
		{
			GameHost.Instance.SaveMapToFile(terrainPath);
			GameHost.Instance.EditorHasUnsavedChanges = false;
			_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
		}
		
		_isSyncing = false;
	}

	private static bool IsIgnoredPath(string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath)) return false;
		string normalized = relativePath.Replace('\\', '/');
		string[] parts = normalized.Split('/');
		foreach (var part in parts)
		{
			if (part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
				part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
				part.Equals(".godot", StringComparison.OrdinalIgnoreCase) ||
				part.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
				part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
				part.Equals("obj", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void CopyFileClearingReadOnly(string sourceFile, string targetFile)
	{
		if (System.IO.File.Exists(targetFile))
		{
			var attrs = System.IO.File.GetAttributes(targetFile);
			if ((attrs & System.IO.FileAttributes.ReadOnly) != 0)
			{
				System.IO.File.SetAttributes(targetFile, attrs & ~System.IO.FileAttributes.ReadOnly);
			}
		}
		System.IO.File.Copy(sourceFile, targetFile, true);
	}

	private void CopyFolderToTempWorkspace(string sourceFolder)
	{
		ClearTempWorkspaceExternal();
		if (!System.IO.Directory.Exists(_tempWorkspacePath))
		{
			System.IO.Directory.CreateDirectory(_tempWorkspacePath);
		}
		
		foreach (var file in System.IO.Directory.GetFiles(sourceFolder, "*", System.IO.SearchOption.AllDirectories))
		{
			string relativePath = file.Substring(sourceFolder.Length + 1);
			if (IsIgnoredPath(relativePath)) continue;

			string targetFile = System.IO.Path.Combine(_tempWorkspacePath, relativePath);
			string targetDir = System.IO.Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(targetDir) && !System.IO.Directory.Exists(targetDir))
			{
				System.IO.Directory.CreateDirectory(targetDir);
			}
			CopyFileClearingReadOnly(file, targetFile);
		}
		MapWorkspaceService.EnsureWitFile(_tempWorkspacePath);
		MapWorkspaceService.EnsureWasmEntryPoint(_tempWorkspacePath);
		MapWorkspaceService.EnsureCsproj(_tempWorkspacePath, System.IO.Path.GetFileName(sourceFolder));
	}

	private void CopyTempWorkspaceToFolder(string targetFolder)
	{
		if (!System.IO.Directory.Exists(targetFolder))
		{
			System.IO.Directory.CreateDirectory(targetFolder);
		}
		
		string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");

		// Sync latest source code files before compiling
		if (!string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
		{
			foreach (var codeFile in new[] { "MapScript.cs", "Coordinates.cs", "metadata.json" })
			{
				string src = System.IO.Path.Combine(_currentSourceFolder, codeFile);
				if (System.IO.File.Exists(src))
				{
					System.IO.File.Copy(src, System.IO.Path.Combine(_tempWorkspacePath, codeFile), true);
				}
			}
		}

		_lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);

		foreach (var file in System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories))
		{
			string relativePath = file.Substring(_tempWorkspacePath.Length + 1);
			if (IsIgnoredPath(relativePath)) continue;
			string targetFile = System.IO.Path.Combine(targetFolder, relativePath);
			string targetDir = System.IO.Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(targetDir) && !System.IO.Directory.Exists(targetDir))
			{
				System.IO.Directory.CreateDirectory(targetDir);
			}
			CopyFileClearingReadOnly(file, targetFile);
		}
		
		if (OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.SaveRecentMapDir(targetFolder);
		}
	}

	private void SetPanelExpanded(string panelPath, string buttonPath, string contentPath, bool expand)
	{
		var btn = GetNodeOrNull<Button>($"{panelPath}/{buttonPath}");
		var content = GetNodeOrNull<Control>($"{panelPath}/{contentPath}");

		if (btn != null && content != null)
		{
			content.Visible = expand;
			btn.Text = expand ? "▲" : "▼";
		}
	}

	private async System.Threading.Tasks.Task SaveMapToFolderAsync(string targetFolder)
	{
		string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		if (GameHost.Instance != null)
		{
			GameHost.Instance.SaveMapToFile(tempTerrainPath);
			GameHost.Instance.EditorHasUnsavedChanges = false;
		}

		ShowFeedback(TranslationServer.Translate("Saving map folder..."));

		try
		{
			await System.Threading.Tasks.Task.Run(() => CopyTempWorkspaceToFolder(targetFolder));

			ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), System.IO.Path.GetFileName(targetFolder)));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveMapToFolderAsync failed: {ex}");
			ShowFeedback(string.Format(TranslationServer.Translate("Failed to save map: {0}"), ex.Message));
		}
	}

	private void SaveMapAction()
	{
		if (GameHost.Instance == null) return;

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Save Map Folder"),
			GetInitialDirectory(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string selectedFolder = selectedPaths[0];
					_lastUsedFolder = selectedFolder;
					_ = SaveMapToFolderAsync(selectedFolder);
				}
				else
				{
					ShowFeedback(TranslationServer.Translate("Save cancelled"));
				}
			})
		);

		if (err != Error.Ok)
		{
			string defaultFolder = ProjectSettings.GlobalizePath("user://maps/default_map");
			System.IO.Directory.CreateDirectory(defaultFolder);
			_ = SaveMapToFolderAsync(defaultFolder);
		}
	}

	public void LoadMapAction()
	{
		if (GameHost.Instance == null) return;

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Load Map Folder"),
			GetInitialDirectory(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string selectedFolder = selectedPaths[0];
					_ = LoadMapFolderAsync(selectedFolder);
				}
			})
		);

		if (err != Error.Ok)
		{
			string defaultFolder = ProjectSettings.GlobalizePath("user://maps/default_map");
			if (System.IO.Directory.Exists(defaultFolder))
			{
				_ = LoadMapFolderAsync(defaultFolder);
			}
		}
	}

	public bool LoadMapFolder(string selectedFolder)
	{
		return LoadMapFolderAsync(selectedFolder).GetAwaiter().GetResult();
	}

	public async System.Threading.Tasks.Task<bool> LoadMapFolderAsync(string selectedFolder)
	{
		if (!System.IO.Directory.Exists(selectedFolder)) return false;
		_lastUsedFolder = selectedFolder;
		_currentSourceFolder = selectedFolder;

		ShowFeedback(TranslationServer.Translate("Loading map..."));
		await System.Threading.Tasks.Task.Run(() => CopyFolderToTempWorkspace(selectedFolder));

		var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
		Callable.From(() =>
		{
			try
			{
				LoadMapProperties();
				string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
				bool success = GameHost.Instance?.LoadMapFromFile(terrainPath) ?? false;

				if (success)
				{
					if (OperatingSystem.IsWindows())
					{
						VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
					}
					_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
					_lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
					ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), System.IO.Path.GetFileName(selectedFolder)));
				}
				else
				{
					ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
				}

				tcs.SetResult(success);
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}
		}).CallDeferred();

		return await tcs.Task;
	}

	private string GetInitialDirectory()
	{
		if (!string.IsNullOrEmpty(_lastUsedFolder) && System.IO.Directory.Exists(_lastUsedFolder))
		{
			return _lastUsedFolder;
		}
		return ProjectSettings.GlobalizePath("res://");
	}


	private NSec.Cryptography.Key GetOrGenerateAuthorshipKey()
	{
		string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
		if (!System.IO.Directory.Exists(keyDir))
		{
			System.IO.Directory.CreateDirectory(keyDir);
		}
		
		string keyPath = System.IO.Path.Combine(keyDir, "authorship_key.pem");
		if (System.IO.File.Exists(keyPath))
		{
			byte[] keyBytes = System.IO.File.ReadAllBytes(keyPath);
			return NSec.Cryptography.Key.Import(SignatureAlgorithm.Ed25519, keyBytes, KeyBlobFormat.RawPrivateKey);
		}
		else
		{
			var key = NSec.Cryptography.Key.Create(SignatureAlgorithm.Ed25519, new NSec.Cryptography.KeyCreationParameters { ExportPolicy = NSec.Cryptography.KeyExportPolicies.AllowPlaintextExport });
			byte[] exported = key.Export(KeyBlobFormat.RawPrivateKey);
			System.IO.File.WriteAllBytes(keyPath, exported);
			
			// Show warning to backup
			ShowFeedback("A new authorship key has been generated at " + keyPath + ". Please backup this file to retain your authorship identity.");
			return key;
		}
	}

	private async void PublishMapAction()
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.SaveMapToFile();
		}
		ShowFeedback(TranslationServer.Translate("Compiling terrain shaders & entity data..."));
		
		// Compile triggers to .dll
		string workspace = ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
		try
		{
			if (System.IO.Directory.Exists(workspace))
			{
				await System.Threading.Tasks.Task.Run(() => 
				{
					var compileProcess = new System.Diagnostics.Process();
					compileProcess.StartInfo.FileName = "dotnet";
					compileProcess.StartInfo.Arguments = "publish \"CustomMap.csproj\" -c Release -r wasi-wasm";
					compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = "C:\\Users\\devin\\.wasi-sdk\\wasi-sdk-25.0-x86_64-windows";
					compileProcess.StartInfo.WorkingDirectory = workspace;
					compileProcess.StartInfo.CreateNoWindow = true;
					compileProcess.StartInfo.UseShellExecute = false;
					compileProcess.Start();
					compileProcess.WaitForExit();
				});
				ShowFeedback(TranslationServer.Translate("Triggers compiled successfully."));
			}
		}
		catch (Exception ex)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Compilation error: {0}"), ex.Message));
		}
		
		try
		{
			var authorshipKey = GetOrGenerateAuthorshipKey();
			string currentUsername = "MapAuthor"; // Could be fetched from a config
			
			var referencedHashes = new List<string>();
			var allFiles = System.IO.Directory.GetFiles(workspace, "*", System.IO.SearchOption.AllDirectories);
			
			string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : "http://localhost:5000";
			
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				foreach (var file in allFiles)
				{
					if (file.EndsWith("map.json") || file.EndsWith("authorship_key.pem")) continue;
					
					byte[] fileBytes = System.IO.File.ReadAllBytes(file);
					string hash = MapAssetManager.ComputeBlake3(fileBytes);
					
					byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(hash);
					byte[] signatureBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, hashBytes);
					string signatureStr = Convert.ToBase64String(signatureBytes);
					
					referencedHashes.Add(hash);
					
					var existsRes = await httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
					if (!existsRes.IsSuccessStatusCode)
					{
						using var form = new System.Net.Http.MultipartFormDataContent();
						form.Add(new System.Net.Http.StringContent(hash), "Hash");
						form.Add(new System.Net.Http.StringContent(signatureStr), "Signature");
						form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
						form.Add(new System.Net.Http.StringContent(Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey))), "PublicKey");
						
						var fileContent = new System.Net.Http.ByteArrayContent(fileBytes);
						form.Add(fileContent, "File", System.IO.Path.GetFileName(file));
						
						await httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
					}
				}
			}
			
			// Load map.json, check and update contributors
			string mapJsonPath = System.IO.Path.Combine(workspace, "map.json");
			if (System.IO.File.Exists(mapJsonPath))
			{
				string mapJsonContent = System.IO.File.ReadAllText(mapJsonPath);
				
				var options = new JsonSerializerOptions { WriteIndented = true };
				var mapDoc = JsonNode.Parse(mapJsonContent) as JsonObject;
				
				if (mapDoc != null)
				{
					var contributorsList = new HashSet<string>();
					if (mapDoc.TryGetPropertyValue("Contributors", out var contNode) && contNode is JsonArray arr)
					{
						foreach (var node in arr)
						{
							if (node != null)
							{
								contributorsList.Add(node.GetValue<string>());
							}
						}
					}
					else
					{
						mapDoc["Contributors"] = new JsonArray();
					}
					
					// Ensure all asset authors are in the contributors and attributions list
					var authorCounts = new System.Collections.Generic.Dictionary<string, int>();
					using (var httpClient = new System.Net.Http.HttpClient())
					{
						foreach (var hash in referencedHashes)
						{
							var assetAuthorRes = await httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
							if (assetAuthorRes.IsSuccessStatusCode)
							{
								string assetAuthorJson = await assetAuthorRes.Content.ReadAsStringAsync();
								var assetMeta = JsonNode.Parse(assetAuthorJson);
								if (assetMeta != null && assetMeta["AuthorUsername"] != null)
								{
									string author = assetMeta["AuthorUsername"].GetValue<string>();
									if (!string.IsNullOrEmpty(author))
									{
										contributorsList.Add(author);
										if (!authorCounts.ContainsKey(author)) authorCounts[author] = 0;
										authorCounts[author]++;
									}
								}
							}
						}
					}
					
					// Add self
					contributorsList.Add(currentUsername);
					
					var newContributorsArr = new JsonArray();
					foreach (var cont in contributorsList)
					{
						newContributorsArr.Add(cont);
					}
					mapDoc["Contributors"] = newContributorsArr;

					// Build Attributions
					var attributionsArr = new JsonArray();
					var sortedAuthors = authorCounts.Keys.ToList();
					sortedAuthors.Sort((a, b) => authorCounts[b].CompareTo(authorCounts[a])); // Sort descending
					foreach (var a in sortedAuthors)
					{
						attributionsArr.Add(a);
					}
					mapDoc["Attributions"] = attributionsArr;
					
					mapDoc["EngineVersion"] = LobbyManager.GameBinaryVersion;
					
					string updatedMapJson = mapDoc.ToJsonString(options);
					System.IO.File.WriteAllText(mapJsonPath, updatedMapJson);
					
					byte[] mapBytes = System.IO.File.ReadAllBytes(mapJsonPath);
					string mapHash = MapAssetManager.ComputeBlake3(mapBytes);
					byte[] mapHashBytes = System.Text.Encoding.UTF8.GetBytes(mapHash);
					byte[] mapSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, mapHashBytes);
					
					using (var httpClient = new System.Net.Http.HttpClient())
					{
						using var form = new System.Net.Http.MultipartFormDataContent();
						form.Add(new System.Net.Http.StringContent(mapHash), "Hash");
						form.Add(new System.Net.Http.StringContent(Convert.ToBase64String(mapSigBytes)), "Signature");
						form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
						form.Add(new System.Net.Http.StringContent(Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey))), "PublicKey");
						
						var fileContent = new System.Net.Http.ByteArrayContent(mapBytes);
						form.Add(fileContent, "File", "map.json");
						
						await httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
						
						var publishReq = new 
						{
							MapJson = updatedMapJson,
							ReferencedHashes = referencedHashes,
							Signature = Convert.ToBase64String(mapSigBytes),
							PublicKey = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey))
						};
						
						var pubContent = new StringContent(JsonSerializer.Serialize(publishReq), System.Text.Encoding.UTF8, "application/json");
						var pubRes = await httpClient.PostAsync(seedServerUrl + "/api/publish_map", pubContent);
						
						if (pubRes.IsSuccessStatusCode)
						{
							ShowFeedback(TranslationServer.Translate("Map compiled & published successfully!"));
						}
						else
						{
							string errText = await pubRes.Content.ReadAsStringAsync();
							ShowFeedback("Failed to publish: " + errText);
						}
					}
				}
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("map.json not found in workspace, unable to publish."));
			}
		}
		catch (Exception ex)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Publish error: {0}"), ex.Message));
		}
	}


	private void ShowConfirmationDialog(string message, Action onConfirm, string confirmText = "YES", string cancelText = "NO")
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("CONFIRMATION REQUIRED"), 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var lblMsg = new Label();
		lblMsg.Text = TranslationServer.Translate(message);
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
		lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		lblMsg.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(lblMsg);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 20);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		var btnConfirm = new Button();
		btnConfirm.Set("icon_max_width", 0);
		SetupButton(btnConfirm, TranslationServer.Translate(confirmText), () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		}, 13);
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		hbox.AddChild(btnConfirm);

		var btnCancel = new Button();
		btnCancel.Set("icon_max_width", 0);
		SetupButton(btnCancel, TranslationServer.Translate(cancelText), () =>
		{
			overlay.QueueFree();
		}, 13);
		btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		hbox.AddChild(btnCancel);
	}

	private void ShowWasmConsoleModal()
	{
		Realm.Godot.UI.WasmConsoleWindow.Instance.ClearLogs();
		Realm.Godot.UI.WasmConsoleWindow.Instance.ShowConsole();
	}

	public void AppendWasmConsoleLog(string line)
	{
		Realm.Godot.UI.WasmConsoleWindow.Instance.AppendLog(line);
	}

	public void SetWasmConsoleStatus(string statusText, Color color)
	{
		Realm.Godot.UI.WasmConsoleWindow.Instance.SetStatus(statusText, color);
	}

	public void CloseWasmConsoleModal()
	{
		// WasmConsoleWindow remains open as a persistent window across scene transitions.
	}

	private ColorRect _helpOverlayPanel = null;

	public void ToggleHelpPanelExternal()
	{
		if (GodotObject.IsInstanceValid(_helpOverlayPanel))
		{
			_helpOverlayPanel.QueueFree();
			_helpOverlayPanel = null;
			return;
		}

		_helpOverlayPanel = new ColorRect();
		_helpOverlayPanel.Name = "HelpOverlayPanel";
		_helpOverlayPanel.Color = new Color(0, 0, 0, 0.6f);
		_helpOverlayPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_helpOverlayPanel);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		_helpOverlayPanel.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(950, 680);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("RTS MAP EDITOR REFERENCE MANUAL"), 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.AddChild(scroll);

		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 30);
		grid.AddThemeConstantOverride("v_separation", 10);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(grid);

		AddHelpSectionHeader(grid, TranslationServer.Translate("PRIMARY MODULE SWITCHING"));
		AddHelpShortcutRow(grid, "F1", TranslationServer.Translate("Terrain Module"));
		AddHelpShortcutRow(grid, "F2", TranslationServer.Translate("Texture Module"));
		AddHelpShortcutRow(grid, "F3", TranslationServer.Translate("Pathing Module"));
		AddHelpShortcutRow(grid, "F4", TranslationServer.Translate("Objects Module"));
		AddHelpShortcutRow(grid, "F5", TranslationServer.Translate("Coordinates Module"));
		AddHelpShortcutRow(grid, "F6", TranslationServer.Translate("Clipboard Module"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("CAMERA CONTROLS"));
		AddHelpShortcutRow(grid, "Arrows", TranslationServer.Translate("Pan map camera"));
		AddHelpShortcutRow(grid, "Mouse Scroll", TranslationServer.Translate("Zoom camera in / out"));
		AddHelpShortcutRow(grid, "Middle Mouse Drag", TranslationServer.Translate("Pan camera by dragging"));
		AddHelpShortcutRow(grid, "Shift + Middle Drag", TranslationServer.Translate("Rotate map camera view"));
		AddHelpShortcutRow(grid, "Comma (,) / Period (.)", TranslationServer.Translate("Rotate camera 90 degrees"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("EDITOR TOOLS"));
		AddHelpShortcutRow(grid, "1", TranslationServer.Translate("Raise Terrain Tool"));
		AddHelpShortcutRow(grid, "2", TranslationServer.Translate("Lower Terrain Tool"));
		AddHelpShortcutRow(grid, "3", TranslationServer.Translate("Smooth Terrain Tool"));
		AddHelpShortcutRow(grid, "4", TranslationServer.Translate("Flatten Terrain Tool"));
		AddHelpShortcutRow(grid, "5", TranslationServer.Translate("Plateau Terrain Tool"));
		AddHelpShortcutRow(grid, "6", TranslationServer.Translate("Ramp Tool"));
		AddHelpShortcutRow(grid, "7", TranslationServer.Translate("Roughen (Noise) Tool"));
		AddHelpShortcutRow(grid, "8", TranslationServer.Translate("Texture Painter Brush"));
		AddHelpShortcutRow(grid, "9", TranslationServer.Translate("Add Objects"));
		AddHelpShortcutRow(grid, "Q", TranslationServer.Translate("Select / Move Tool"));
		AddHelpShortcutRow(grid, "I", TranslationServer.Translate("Eyedropper Picker"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("SCULPTING / PLACEMENT SETTINGS"));
		AddHelpShortcutRow(grid, "[ / ]", TranslationServer.Translate("Increase / decrease brush size"));
		AddHelpShortcutRow(grid, "- / =", TranslationServer.Translate("Increase / decrease brush strength"));
		AddHelpShortcutRow(grid, "Shift + MouseWheel Scroll", TranslationServer.Translate("Quickly change brush size"));
		AddHelpShortcutRow(grid, "Ctrl + MouseWheel Scroll", TranslationServer.Translate("Quickly change brush strength"));
		AddHelpShortcutRow(grid, "B Key", TranslationServer.Translate("Toggle brush shape (Circle / Square)"));
		AddHelpShortcutRow(grid, "V Key", TranslationServer.Translate("Toggle terrain alignment grid lines"));
		AddHelpShortcutRow(grid, "M Key", TranslationServer.Translate("Toggle blocky sculpt mode"));
		AddHelpShortcutRow(grid, "Shift + MouseWheel Scroll", TranslationServer.Translate("Rotate placement/selected object"));
		AddHelpShortcutRow(grid, "Alt + MouseWheel Scroll", TranslationServer.Translate("Fine-tune object scale size"));
		AddHelpShortcutRow(grid, "Ctrl + G", TranslationServer.Translate("Toggle alignment grid snap placement"));
		AddHelpShortcutRow(grid, "Ctrl + D", TranslationServer.Translate("Duplicate / clone selected object"));
		AddHelpShortcutRow(grid, "Delete", TranslationServer.Translate("Delete / erase selected object"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("GENERAL OPERATIONS"));
		AddHelpShortcutRow(grid, "Ctrl + Z / Ctrl + Y", TranslationServer.Translate("Undo / Redo editor actions"));
		AddHelpShortcutRow(grid, "Ctrl + S / Ctrl + O", TranslationServer.Translate("Save Map File / Load Map File"));
		AddHelpShortcutRow(grid, "Escape Key", TranslationServer.Translate("Clear selection or cancel active tool"));

		var btnClose = new Button();
		btnClose.Set("icon_max_width", 0);
		SetupButton(btnClose, TranslationServer.Translate("CLOSE MANUAL"), () =>
		{
			_helpOverlayPanel.QueueFree();
			_helpOverlayPanel = null;
		}, 13);
		vbox.AddChild(btnClose);
	}

	private void AddHelpSectionHeader(GridContainer grid, string title)
	{
		var lbl = new Label();
		lbl.Text = title;
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lbl.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lbl);

		var empty = new Control();
		grid.AddChild(empty);
	}

	private void AddHelpShortcutRow(GridContainer grid, string keys, string action)
	{
		var lblKeys = new Label();
		lblKeys.Text = keys;
		lblKeys.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		lblKeys.AddThemeFontSizeOverride("font_size", 11);
		grid.AddChild(lblKeys);

		var lblAction = new Label();
		lblAction.Text = action;
		lblAction.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		lblAction.AddThemeFontSizeOverride("font_size", 11);
		grid.AddChild(lblAction);
	}



	
	private void SaveMapProperties()
	{
		if (GameHost.Instance == null) return;
		
		string mapJsonPath = ProjectSettings.GlobalizePath("user://temp_map_workspace/map.json");
		if (System.IO.File.Exists(mapJsonPath))
		{
			try
			{
				string mapJsonContent = System.IO.File.ReadAllText(mapJsonPath);
				var mapDoc = System.Text.Json.Nodes.JsonNode.Parse(mapJsonContent) as System.Text.Json.Nodes.JsonObject;
				if (mapDoc != null)
				{
					if (!mapDoc.ContainsKey("MapProperties")) mapDoc["MapProperties"] = new System.Text.Json.Nodes.JsonObject();
					var props = mapDoc["MapProperties"] as System.Text.Json.Nodes.JsonObject;
					if (props != null)
					{
						if (_optMapType != null) props["MapType"] = _optMapType.Selected == 0 ? "Arcade Custom Map" : "Asset Pack";
						var tagsArr = new System.Text.Json.Nodes.JsonArray();
						foreach (var chk in _activeTagCheckboxes)
						{
							if (chk.ButtonPressed)
							{
								tagsArr.Add(chk.Text);
							}
						}
						props["Tags"] = tagsArr;
					}
					var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
					System.IO.File.WriteAllText(mapJsonPath, mapDoc.ToJsonString(options));
				}
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"Failed to save map properties: {ex.Message}");
			}
		}
	}

	private List<CheckBox> _activeTagCheckboxes = new();

	private void RebuildTagsUI()
	{
		var mapTagsBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/MapSettingsAccordion/ContentMapSettings/MapTagsBox");
		if (mapTagsBox == null) return;

		// Remove old checkboxes
		foreach (var child in mapTagsBox.GetChildren())
		{
			if (child is CheckBox chk)
			{
				mapTagsBox.RemoveChild(chk);
				chk.QueueFree();
			}
		}

		_activeTagCheckboxes.Clear();

		string[] tags;
		if (_optMapType != null && _optMapType.Selected == 1)
		{
			// Asset Pack tags
			tags = new[] { "3D Models", "Audio", "Code Scripts", "Terrain PBR", "UI Components" };
		}
		else
		{
			// Arcade Custom Map tags
			tags = new[] { "Tower Defense", "Campaign", "Melee", "Coop / Survival", "Tutorial / Skirmish" };
		}

		foreach (var tag in tags)
		{
			var chk = new CheckBox();
			chk.Text = tag;
			chk.FocusMode = FocusModeEnum.None;
			chk.AddThemeFontSizeOverride("font_size", 11);
			chk.Toggled += (_) => SaveMapProperties();
			mapTagsBox.AddChild(chk);
			_activeTagCheckboxes.Add(chk);
		}
	}

	private void LoadMapProperties()
	{
		string mapJsonPath = ProjectSettings.GlobalizePath("user://temp_map_workspace/map.json");
		if (System.IO.File.Exists(mapJsonPath))
		{
			try
			{
				string mapJsonContent = System.IO.File.ReadAllText(mapJsonPath);
				var mapDoc = System.Text.Json.Nodes.JsonNode.Parse(mapJsonContent) as System.Text.Json.Nodes.JsonObject;
				if (mapDoc != null && mapDoc.ContainsKey("MapProperties"))
				{
					var props = mapDoc["MapProperties"] as System.Text.Json.Nodes.JsonObject;
					if (props != null)
					{
						if (_optMapType != null && props.ContainsKey("MapType"))
						{
							string mapType = props["MapType"]?.GetValue<string>() ?? "";
							_optMapType.Selected = (mapType == "Asset Pack") ? 1 : 0;
						}
						
						// Rebuild tags UI based on selected MapType
						RebuildTagsUI();

						if (props.ContainsKey("Tags") && props["Tags"] is System.Text.Json.Nodes.JsonArray tagsArr)
						{
							var activeTags = new HashSet<string>();
							foreach (var tagNode in tagsArr)
							{
								if (tagNode != null) activeTags.Add(tagNode.GetValue<string>());
							}

							foreach (var chk in _activeTagCheckboxes)
							{
								chk.ButtonPressed = activeTags.Contains(chk.Text);
							}
						}
					}
				}
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"Failed to load map properties: {ex.Message}");
			}
		}
		else
		{
			// Default initialization
			RebuildTagsUI();
		}
	}

	private async System.Threading.Tasks.Task CompileAndSignMapAsync(string workspace, bool skipAttribution = true)
	{
		// 1. Compile triggers
		try
		{
			if (System.IO.Directory.Exists(workspace))
			{
				MapWorkspaceService.EnsureWitFile(workspace);
				MapWorkspaceService.EnsureWasmEntryPoint(workspace);
				MapWorkspaceService.EnsureCsproj(workspace, System.IO.Path.GetFileName(workspace));

				var csprojFiles = System.IO.Directory.GetFiles(workspace, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
				if (csprojFiles.Length == 0)
				{
					_wasmHasErrors = true;
					SetWasmConsoleStatus("❌ WASM Compilation Failed: No .csproj found", new Color(1.0f, 0.3f, 0.3f));
					AppendWasmConsoleLog("[ERROR] No .csproj found in workspace, cannot compile map script");
					GD.PrintErr("[MapEditorHUD] No .csproj found in workspace, cannot compile map script");
					return;
				}

				// Pick the first .csproj (prefer CustomMap.csproj)
				string csproj = csprojFiles.FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals("CustomMap.csproj", System.StringComparison.OrdinalIgnoreCase)) ?? csprojFiles[0];

				// Check if WASM binary already exists and no .cs files have been modified since it was built
				string binDir = System.IO.Path.Combine(workspace, "bin");
				string existingWasm = null;
				if (System.IO.Directory.Exists(binDir))
				{
					existingWasm = System.IO.Directory.GetFiles(
						binDir,
						"*.wasm",
						System.IO.SearchOption.AllDirectories
					).FirstOrDefault();
				}

				if (string.IsNullOrEmpty(existingWasm) && !string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
				{
					string sourceBinDir = System.IO.Path.Combine(_currentSourceFolder, "bin");
					if (System.IO.Directory.Exists(sourceBinDir))
					{
						existingWasm = System.IO.Directory.GetFiles(
							sourceBinDir,
							"*.wasm",
							System.IO.SearchOption.AllDirectories
						).FirstOrDefault();
					}
				}

				if (!string.IsNullOrEmpty(existingWasm) && System.IO.File.Exists(existingWasm))
				{
					DateTime wasmTime = System.IO.File.GetLastWriteTimeUtc(existingWasm);
					var sourceDirs = new System.Collections.Generic.List<string> { workspace };
					if (!string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder) && !_currentSourceFolder.Equals(workspace, System.StringComparison.OrdinalIgnoreCase))
					{
						sourceDirs.Add(_currentSourceFolder);
					}

					bool hasNewerCsFile = false;
					foreach (var dir in sourceDirs)
					{
						var csFiles = System.IO.Directory.GetFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories)
							.Where(f => {
								string rel = f.Substring(dir.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
								return !rel.StartsWith("bin", System.StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("obj", System.StringComparison.OrdinalIgnoreCase);
							});

						if (csFiles.Any(f => System.IO.File.GetLastWriteTimeUtc(f) > wasmTime))
						{
							hasNewerCsFile = true;
							break;
						}
					}

					if (!hasNewerCsFile)
					{
						string targetWasmInTemp = System.IO.Path.Combine(workspace, "bin", System.IO.Path.GetFileName(existingWasm));
						if (!System.IO.File.Exists(targetWasmInTemp))
						{
							System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetWasmInTemp));
							System.IO.File.Copy(existingWasm, targetWasmInTemp, true);
						}

						_wasmHasErrors = false;
						SetWasmConsoleStatus("✓ WASM Compilation Bypassed (Unchanged)", UIStyle.ColorCyanGlow);
						AppendWasmConsoleLog("[INFO] .cs files unchanged since last build. Bypassing compilation using existing WASM binary.");
						if (skipAttribution) return;
					}
				}

				await System.Threading.Tasks.Task.Run(() =>
				{
					var compileProcess = new System.Diagnostics.Process();
					compileProcess.StartInfo.FileName = "dotnet";
					compileProcess.StartInfo.Arguments = $"publish \"{csproj}\" -c Release -r wasi-wasm";
					compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = "C:\\Users\\devin\\.wasi-sdk\\wasi-sdk-25.0-x86_64-windows";
					compileProcess.StartInfo.WorkingDirectory = workspace;
					compileProcess.StartInfo.CreateNoWindow = true;
					compileProcess.StartInfo.UseShellExecute = false;
					compileProcess.StartInfo.RedirectStandardOutput = true;
					compileProcess.StartInfo.RedirectStandardError = true;

					compileProcess.OutputDataReceived += (s, e) =>
					{
						if (!string.IsNullOrEmpty(e.Data))
						{
							if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
							{
								_wasmHasErrors = true;
							}
							AppendWasmConsoleLog(e.Data);
						}
					};

					compileProcess.ErrorDataReceived += (s, e) =>
					{
						if (!string.IsNullOrEmpty(e.Data))
						{
							if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
							{
								_wasmHasErrors = true;
								AppendWasmConsoleLog("[COMPILER ERROR] " + e.Data);
							}
							else if (e.Data.Contains(": warning "))
							{
								AppendWasmConsoleLog("[COMPILER WARNING] " + e.Data);
							}
							else
							{
								AppendWasmConsoleLog(e.Data);
							}
						}
					};

					compileProcess.Start();
					compileProcess.BeginOutputReadLine();
					compileProcess.BeginErrorReadLine();
					compileProcess.WaitForExit();

					if (compileProcess.ExitCode != 0 || _wasmHasErrors)
					{
						_wasmHasErrors = true;
						SetWasmConsoleStatus($"❌ WASM Compilation Failed (exit code {compileProcess.ExitCode})", new Color(1.0f, 0.3f, 0.3f));
						AppendWasmConsoleLog($"[ERROR] dotnet publish failed with exit code {compileProcess.ExitCode}");
						GD.PrintErr($"[MapEditorHUD] Map script compilation failed (exit code {compileProcess.ExitCode})");
					}
					else
					{
						_wasmHasErrors = false;
						SetWasmConsoleStatus("✓ WASM Compilation Succeeded", UIStyle.ColorCyanGlow);
						AppendWasmConsoleLog("[SUCCESS] WASM compilation complete (exit code 0).");
					}
				});
			}
		}
		catch (Exception ex)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus($"❌ WASM Compilation Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[COMPILER EXCEPTION] {ex}");
			GD.PrintErr($"[MapEditorHUD] Trigger compilation failed: {ex.Message}");
		}

		if (skipAttribution) return;

		// 2. Resolve contributors, attributions, and sign
		try
		{
			var authorshipKey = GetOrGenerateAuthorshipKey();
			string currentUsername = "MapAuthor";
			string pubKeyStr = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
			
			string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : "http://localhost:5000";
			
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				try
				{
					var resTask = httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
					resTask.Wait();
					var res = resTask.Result;
					if (res.IsSuccessStatusCode)
					{
						var jsonTask = res.Content.ReadAsStringAsync();
						jsonTask.Wait();
						using var creatorDoc = JsonDocument.Parse(jsonTask.Result);
						if (creatorDoc.RootElement.TryGetProperty("username", out var uProp))
						{
							currentUsername = uProp.GetString() ?? currentUsername;
						}
					}
				}
				catch {}

				var referencedHashes = new List<string>();
				var allFiles = System.IO.Directory.GetFiles(workspace, "*", System.IO.SearchOption.AllDirectories);
				
				foreach (var file in allFiles)
				{
					if (file.EndsWith("map.json") || file.EndsWith("authorship_key.pem")) continue;
					
					byte[] fileBytes = System.IO.File.ReadAllBytes(file);
					string hash = MapAssetManager.ComputeBlake3(fileBytes);
					
					byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(hash);
					byte[] signatureBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, hashBytes);
					string signatureStr = Convert.ToBase64String(signatureBytes);
					
					referencedHashes.Add(hash);
					
					try
					{
						var existsResTask = httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
						existsResTask.Wait();
						var existsRes = existsResTask.Result;
						if (!existsRes.IsSuccessStatusCode)
						{
							using var form = new System.Net.Http.MultipartFormDataContent();
							form.Add(new System.Net.Http.StringContent(hash), "Hash");
							form.Add(new System.Net.Http.StringContent(signatureStr), "Signature");
							form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
							form.Add(new System.Net.Http.StringContent(pubKeyStr), "PublicKey");
							
							var fileContent = new System.Net.Http.ByteArrayContent(fileBytes);
							form.Add(fileContent, "File", System.IO.Path.GetFileName(file));
							
							var uploadTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
							uploadTask.Wait();
						}
					}
					catch {}
				}
				
				string mapJsonPath = System.IO.Path.Combine(workspace, "map.json");
				if (System.IO.File.Exists(mapJsonPath))
				{
					string mapJsonContent = System.IO.File.ReadAllText(mapJsonPath);
					var options = new JsonSerializerOptions { WriteIndented = true };
					var mapDoc = JsonNode.Parse(mapJsonContent) as JsonObject;
					
					if (mapDoc != null)
					{
						var contributorsList = new HashSet<string>();
						if (mapDoc.TryGetPropertyValue("Contributors", out var contNode) && contNode is JsonArray arr)
						{
							foreach (var node in arr)
							{
								if (node != null) contributorsList.Add(node.GetValue<string>());
							}
						}
						
						var authorCounts = new System.Collections.Generic.Dictionary<string, int>();
						foreach (var hash in referencedHashes)
						{
							try
							{
								var assetAuthorResTask = httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
								assetAuthorResTask.Wait();
								var assetAuthorRes = assetAuthorResTask.Result;
								if (assetAuthorRes.IsSuccessStatusCode)
								{
									var assetAuthorJsonTask = assetAuthorRes.Content.ReadAsStringAsync();
									assetAuthorJsonTask.Wait();
									var assetMeta = JsonNode.Parse(assetAuthorJsonTask.Result);
									if (assetMeta != null && assetMeta["AuthorUsername"] != null)
									{
										string author = assetMeta["AuthorUsername"].GetValue<string>();
										if (!string.IsNullOrEmpty(author))
										{
											contributorsList.Add(author);
											if (!authorCounts.ContainsKey(author)) authorCounts[author] = 0;
											authorCounts[author]++;
										}
									}
								}
							}
							catch {}
						}
						
						contributorsList.Add(currentUsername);
						
						var newContributorsArr = new JsonArray();
						foreach (var cont in contributorsList)
						{
							newContributorsArr.Add(cont);
						}
						mapDoc["Contributors"] = newContributorsArr;

						var attributionsArr = new JsonArray();
						var sortedAuthors = authorCounts.Keys.ToList();
						sortedAuthors.Sort((a, b) => authorCounts[b].CompareTo(authorCounts[a]));
						foreach (var a in sortedAuthors)
						{
							attributionsArr.Add(a);
						}
						mapDoc["Attributions"] = attributionsArr;
						mapDoc["EngineVersion"] = LobbyManager.GameBinaryVersion;
						
						mapDoc["author_key"] = pubKeyStr;
						if (mapDoc.ContainsKey("signature"))
						{
							mapDoc.Remove("signature");
						}
						
						string updatedMapJson = mapDoc.ToJsonString(options);
						System.IO.File.WriteAllText(mapJsonPath, updatedMapJson);
						
						byte[] mapBytes = System.IO.File.ReadAllBytes(mapJsonPath);
						string mapHash = MapAssetManager.ComputeBlake3(mapBytes);
						byte[] mapHashBytes = System.Text.Encoding.UTF8.GetBytes(mapHash);
						byte[] mapSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, mapHashBytes);
						
						mapDoc["signature"] = Convert.ToBase64String(mapSigBytes);
						updatedMapJson = mapDoc.ToJsonString(options);
						System.IO.File.WriteAllText(mapJsonPath, updatedMapJson);
						
						try
						{
							using (var form = new System.Net.Http.MultipartFormDataContent())
							{
								form.Add(new System.Net.Http.StringContent(mapHash), "Hash");
								form.Add(new System.Net.Http.StringContent(Convert.ToBase64String(mapSigBytes)), "Signature");
								form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
								form.Add(new System.Net.Http.StringContent(pubKeyStr), "PublicKey");
								
								var fileContent = new System.Net.Http.ByteArrayContent(mapBytes);
								form.Add(fileContent, "File", "map.json");
								
								var uploadMapTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
								uploadMapTask.Wait();
							}
							
							var publishReq = new 
							{
								MapJson = updatedMapJson,
								ReferencedHashes = referencedHashes,
								Signature = Convert.ToBase64String(mapSigBytes),
								PublicKey = pubKeyStr
							};
							
							var pubContent = new StringContent(JsonSerializer.Serialize(publishReq), System.Text.Encoding.UTF8, "application/json");
							var pubTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map", pubContent);
							pubTask.Wait();
						}
						catch {}
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Error signing/compiling map: {ex.Message}");
		}
	}

	private async void CheckCreatorRegistrationAndPrompt()
	{
		var key = GetOrGenerateAuthorshipKey();
		string pubKeyStr = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
		
		string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : "http://localhost:5000";
		
		try
		{
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				var res = await httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
				if (!res.IsSuccessStatusCode)
				{
					// Not registered! Show registration prompt
					ShowCreatorRegistrationDialog(pubKeyStr, key);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Error checking creator registration: {ex.Message}");
		}
	}

	private void ShowCreatorRegistrationDialog(string pubKeyStr, NSec.Cryptography.Key key)
	{
		var overlay = new ColorRect();
		overlay.Name = "CreatorRegistrationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.8f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SetAnchorsPreset(LayoutPreset.Center);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.95f);
		style.BorderWidthTop = 2; style.BorderWidthBottom = 2; style.BorderWidthLeft = 2; style.BorderWidthRight = 2;
		style.BorderColor = UIStyle.ColorCyanGlow;
		style.CornerRadiusTopLeft = 4; style.CornerRadiusTopRight = 4; style.CornerRadiusBottomLeft = 4; style.CornerRadiusBottomRight = 4;
		panel.AddThemeStyleboxOverride("panel", style);
		overlay.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 15);
		margin.AddThemeConstantOverride("margin_bottom", 15);
		margin.AddThemeConstantOverride("margin_left", 15);
		margin.AddThemeConstantOverride("margin_right", 15);
		margin.AddChild(vbox);
		panel.AddChild(margin);

		var title = new Label();
		title.Text = "Register Creator Profile";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 18);
		vbox.AddChild(title);

		var desc = new Label();
		desc.Text = "A new cryptographic key pair has been generated for your machine. Please choose a unique display name to lock to this key.";
		desc.HorizontalAlignment = HorizontalAlignment.Center;
		desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc.AddThemeFontSizeOverride("font_size", 12);
		vbox.AddChild(desc);

		var lineEdit = new LineEdit();
		lineEdit.PlaceholderText = "Enter Username";
		lineEdit.Alignment = HorizontalAlignment.Center;
		vbox.AddChild(lineEdit);

		var errLabel = new Label();
		errLabel.HorizontalAlignment = HorizontalAlignment.Center;
		errLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
		errLabel.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(errLabel);

		var btnRegister = new Button();
		btnRegister.Text = "Register Display Name";
		btnRegister.CustomMinimumSize = new Vector2(150, 36);
		btnRegister.Pressed += async () => {
			string username = lineEdit.Text.Trim();
			if (string.IsNullOrEmpty(username))
			{
				errLabel.Text = "Username cannot be empty.";
				return;
			}
			if (username.Length > 32)
			{
				errLabel.Text = "Username must be 32 characters or less.";
				return;
			}

			btnRegister.Disabled = true;
			errLabel.Text = "Registering...";

			try
			{
				// Sign the registration payload
				byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(username + ":" + pubKeyStr);
				byte[] sigBytes = SignatureAlgorithm.Ed25519.Sign(key, payloadBytes);
				string signatureStr = Convert.ToBase64String(sigBytes);

				var regPayload = new {
					Username = username,
					PublicKey = pubKeyStr,
					Signature = signatureStr
				};

				string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : "http://localhost:5000";
				using (var httpClient = new System.Net.Http.HttpClient())
				{
					var content = new StringContent(JsonSerializer.Serialize(regPayload), System.Text.Encoding.UTF8, "application/json");
					var res = await httpClient.PostAsync(seedServerUrl + "/api/creators/register", content);
					if (res.IsSuccessStatusCode)
					{
						overlay.QueueFree();
						ShowFeedback("Creator registered successfully!");
					}
					else
					{
						string errText = await res.Content.ReadAsStringAsync();
						try {
							var errDoc = JsonDocument.Parse(errText);
							if (errDoc.RootElement.TryGetProperty("Message", out var msgProp))
							{
								errLabel.Text = msgProp.GetString();
							}
							else
							{
								errLabel.Text = "Registration failed.";
							}
						}
						catch {
							errLabel.Text = "Registration failed: " + errText;
						}
						btnRegister.Disabled = false;
					}
				}
			}
			catch (Exception ex)
			{
				errLabel.Text = "Error: " + ex.Message;
				btnRegister.Disabled = false;
			}
		};
		vbox.AddChild(btnRegister);
	}

	public void ImportTerrainFromMinimapDialog()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Minimap Image to Import Terrain"),
			ProjectSettings.GlobalizePath("res://"),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new string[] { "*.png, *.jpg, *.jpeg, *.webp, *.gif ; Images" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					ImportTerrainFromMinimapPath(selectedPaths[0]);
				}
			})
		);
	}

	private void ImportTerrainFromMinimapPath(string selectedPath)
	{
		if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;

		GameHost.Instance.ClearMapEntirely();
		bool success = GameHost.Instance.ImportTerrainFromMinimap(selectedPath, out var smoothedHeights, out var splatMap, out var treePositions);
		if (!success) return;

		int width = GameHost.Instance.GroundTerrain.Width;
		int depth = GameHost.Instance.GroundTerrain.Depth;

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				GameHost.Instance.GroundTerrain.Heights[gx, gz] = smoothedHeights[gx, gz];
				GameHost.Instance.GroundTerrain.SplatMap[gx, gz] = splatMap[gx, gz];
			}
		}

		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();

		foreach (var child in GameHost.Instance.GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				if (prop.PropId == "tree")
				{
					prop.QueueFree();
				}
			}
		}

		foreach (var (x, y, z, rot, scale) in treePositions)
		{
			GameHost.Instance.SpawnPropExternalWithParams("tree", new Vector3(x, y, z), rot, scale);
		}

		ShowFeedback(TranslationServer.Translate("Terrain imported from minimap image successfully!"));
	}

	private void CycleMirrorMode()
	{
		if (GameHost.Instance == null) return;
		var current = GameHost.Instance.EditorMirrorMode;
		var next = current switch
		{
			MirrorMode.None => MirrorMode.Vertical,
			MirrorMode.Vertical => MirrorMode.Horizontal,
			MirrorMode.Horizontal => MirrorMode.Both,
			MirrorMode.Both => MirrorMode.None,
			_ => MirrorMode.None
		};
		GameHost.Instance.EditorMirrorMode = next;
		UpdateMirrorButtonText();
		ShowFeedback(string.Format(TranslationServer.Translate("Mirroring: {0}"), next.ToString().ToUpper()));
	}

	public void UpdateMirrorButtonText()
	{
		if (_btnMirrorMode == null || GameHost.Instance == null) return;
		string modeText = GameHost.Instance.EditorMirrorMode switch
		{
			MirrorMode.None => TranslationServer.Translate("🪞 MIRROR: NONE"),
			MirrorMode.Vertical => TranslationServer.Translate("🪞 MIRROR: VERTICAL"),
			MirrorMode.Horizontal => TranslationServer.Translate("🪞 MIRROR: HORIZONTAL"),
			MirrorMode.Both => TranslationServer.Translate("🪞 MIRROR: BOTH"),
			_ => TranslationServer.Translate("🪞 MIRROR: NONE")
		};
		_btnMirrorMode.Text = modeText;
	}

	private void RebuildHUDLayout()
	{
	}

	public void SwitchModule(EditorModule module)
	{
		_activeModule = module;
		UpdateModuleSwitchButtons();

		if (module != EditorModule.Coordinates)
		{
			GameHost.Instance?.HideCoordinateSelectionOutline();
		}

		if (_panelTerrainVBox != null) _panelTerrainVBox.Visible = (module == EditorModule.Terrain);
		if (_panelDecoVBox != null) _panelDecoVBox.Visible = (module == EditorModule.TextureDeco);
		if (_panelEnv != null) _panelEnv.Visible = (module == EditorModule.TextureDeco);
		if (_panelPathingVBox != null) _panelPathingVBox.Visible = (module == EditorModule.Pathing);
		if (_panelCoordinatesVBox != null) _panelCoordinatesVBox.Visible = (module == EditorModule.Coordinates);
		if (_panelObjects != null) _panelObjects.Visible = (module == EditorModule.Objects);
		if (_panelClipboard != null) _panelClipboard.Visible = (module == EditorModule.Clipboard);

		if (GameHost.Instance != null)
		{
			switch (module)
			{
				case EditorModule.Terrain:
					TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise);
					break;
				case EditorModule.TextureDeco:
					TriggerToolSelection(GameHost.EditorTool.PaintGrass, _btnTextureBrush);
					break;
				case EditorModule.Pathing:
					TriggerToolSelection(GameHost.EditorTool.PaintPathing, _btnPathingBrush);
					break;
				case EditorModule.Objects:
					_entityPaletteController?.TriggerAddObjectMode();
					break;
				case EditorModule.Coordinates:
					TriggerToolSelection(GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate);
					break;
				case EditorModule.Clipboard:
					TriggerToolSelection(GameHost.EditorTool.SelectArea, _btnSelectArea);
					break;
			}
		}
	}

	private void UpdateModuleSwitchButtons()
	{
		if (_optModule != null)
		{
			_optModule.Selected = (int)_activeModule;
		}
	}



	private void SetupAccordion(Button headerBtn, Control contentControl, string titleText)
	{
		headerBtn.Text = (contentControl.Visible ? "▼ " : "▶ ") + titleText;
		headerBtn.Pressed += () =>
		{
			contentControl.Visible = !contentControl.Visible;
			headerBtn.Text = (contentControl.Visible ? "▼ " : "▶ ") + titleText;
			UIManager.Instance?.PlayClickSound();
		};
	}

	private void SetupMutualAccordion(Button headerBtn, Control contentControl, string titleText)
	{
		headerBtn.Text = (contentControl.Visible ? "▼ " : "▶ ") + titleText;
		headerBtn.Pressed += () =>
		{
			contentControl.Visible = !contentControl.Visible;
			headerBtn.Text = (contentControl.Visible ? "▼ " : "▶ ") + titleText;
			if (contentControl.Visible)
			{
				if (headerBtn != _btnHeaderFile && _contentFile != null)
				{
					_contentFile.Visible = false;
					_btnHeaderFile.Text = "▶ " + TranslationServer.Translate("File");
				}
				if (headerBtn != _btnHeaderViewport && _contentViewport != null)
				{
					_contentViewport.Visible = false;
					_btnHeaderViewport.Text = "▶ " + TranslationServer.Translate("Viewport & Navigation");
				}
				if (headerBtn != _btnHeaderMapSettings && _contentMapSettings != null)
				{
					_contentMapSettings.Visible = false;
					_btnHeaderMapSettings.Text = "▶ " + TranslationServer.Translate("Map Settings");
				}
			}
			UIManager.Instance?.PlayClickSound();
		};
	}

	private void StyleAccordionHeader(Button btn)
	{
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.9f, 0.7f));
		btn.Alignment = HorizontalAlignment.Left;
	}

	private void ToggleLeftPanel()
	{
		SetLeftPanelExpanded(!_leftPanelExpanded);
	}

	private void ToggleRightPanel()
	{
		SetRightPanelExpanded(!_rightPanelExpanded);
	}

	private void SetLeftPanelExpanded(bool expand)
	{
		_leftPanelExpanded = expand;
		var tween = CreateTween();
		float targetLeft = expand ? 0.0f : -260.0f;
		float targetRight = expand ? 260.0f : 0.0f;
		tween.TweenProperty(_panelLeft, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panelLeft, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_btnLeftTab.Text = expand ? "◀" : "▶";
	}

	private void SetRightPanelExpanded(bool expand)
	{
		_rightPanelExpanded = expand;
		var tween = CreateTween();
		float targetLeft = expand ? -300.0f : 0.0f;
		float targetRight = expand ? 0.0f : 300.0f;
		tween.TweenProperty(_panelRight, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panelRight, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_btnRightTab.Text = expand ? "▶" : "◀";
	}

	private void SafeReparent(Node node, Node newParent)
	{
		if (node == null || newParent == null) return;
		var oldParent = node.GetParent();
		if (oldParent == newParent) return;
		oldParent?.RemoveChild(node);
		newParent.AddChild(node);
	}

	private void UpdateSidebarMorph(GameHost.EditorTool tool)
	{
		if (_panelRight == null) return;

		bool isBrush = tool == GameHost.EditorTool.Raise ||
					   tool == GameHost.EditorTool.Lower ||
					   tool == GameHost.EditorTool.Smooth ||
					   tool == GameHost.EditorTool.Plateau ||
					   tool == GameHost.EditorTool.PaintGrass ||
					   tool == GameHost.EditorTool.PaintDirt ||
					   tool == GameHost.EditorTool.PaintRock ||
					   tool == GameHost.EditorTool.PaintSand ||
					   tool == GameHost.EditorTool.Noise ||
					   tool == GameHost.EditorTool.PaintPathing ||
					   tool == GameHost.EditorTool.FloodFillPathing ||
					   tool == GameHost.EditorTool.PlacePropClump;

		if (_accordionBrush != null)
		{
			_accordionBrush.Visible = isBrush;
			UpdateBrushStrengthVisibility();
			bool isTextureMode = tool == GameHost.EditorTool.PaintGrass ||
								 tool == GameHost.EditorTool.PaintDirt ||
								 tool == GameHost.EditorTool.PaintRock ||
								 tool == GameHost.EditorTool.PaintSand;
			if (_chkBlockMode != null)
			{
				_chkBlockMode.Visible = (tool != GameHost.EditorTool.PaintPathing && 
										 tool != GameHost.EditorTool.FloodFillPathing &&
										 !isTextureMode &&
										 tool != GameHost.EditorTool.Smooth &&
										 tool != GameHost.EditorTool.Noise);
			}
			UpdateBlockStepVisibility();
		}

		bool texSettingsVisible = _containerTextureSettings != null && (tool == GameHost.EditorTool.PaintGrass ||
											 tool == GameHost.EditorTool.PaintDirt ||
											 tool == GameHost.EditorTool.PaintRock ||
											 tool == GameHost.EditorTool.PaintSand ||
											 tool == GameHost.EditorTool.FloodFill ||
											 tool == GameHost.EditorTool.Raise ||
											 tool == GameHost.EditorTool.Lower ||
											 tool == GameHost.EditorTool.Plateau ||
											 tool == GameHost.EditorTool.Ramp);
		if (_containerTextureSettings != null) _containerTextureSettings.Visible = texSettingsVisible;

		bool pathingSettingsVisible = _containerPathingSettings != null && (tool == GameHost.EditorTool.PaintPathing || tool == GameHost.EditorTool.FloodFillPathing);
		if (_containerPathingSettings != null) _containerPathingSettings.Visible = pathingSettingsVisible;

		bool eyedropperSettingsVisible = _containerEyedropperSettings != null && (tool == GameHost.EditorTool.Eyedropper);
		if (_containerEyedropperSettings != null) _containerEyedropperSettings.Visible = eyedropperSettingsVisible;

		bool pasteSettingsVisible = _containerPasteSettings != null && (tool == GameHost.EditorTool.SelectArea || tool == GameHost.EditorTool.PasteArea);
		if (_containerPasteSettings != null) _containerPasteSettings.Visible = pasteSettingsVisible;

		bool isPlacement = (tool == GameHost.EditorTool.PlaceUnit ||
							tool == GameHost.EditorTool.PlaceProp ||
							tool == GameHost.EditorTool.PlacePropClump ||
							tool == GameHost.EditorTool.PlaceDecal);
		
		bool categorySelectorVisible = _containerCategorySelector != null && isPlacement;
		if (_containerCategorySelector != null) _containerCategorySelector.Visible = categorySelectorVisible;

		bool anyToolSettingVisible = texSettingsVisible ||
									 pathingSettingsVisible ||
									 eyedropperSettingsVisible ||
									 pasteSettingsVisible ||
									 categorySelectorVisible;

		if (_accordionToolSettings != null)
		{
			_accordionToolSettings.Visible = anyToolSettingVisible;
		}

		bool hasPlacementConfig = isPlacement;
		if (_accordionPlacement != null)
		{
			_accordionPlacement.Visible = hasPlacementConfig;
		}
		if (_chkClumpMode != null)
		{
			_chkClumpMode.Visible = isPlacement;
			if (_densityBox != null) _densityBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
			if (_scaleVarBox != null) _scaleVarBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
		}

		bool hasSelectedObject = GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject);
		if (_accordionInspector != null)
		{
			_accordionInspector.Visible = hasSelectedObject && (tool == GameHost.EditorTool.SelectMove);
		}

		if (_accordionContainer != null)
		{
			_accordionContainer.QueueSort();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Godot.Key.F1)
			{
				SwitchModule(EditorModule.Terrain);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F2)
			{
				SwitchModule(EditorModule.TextureDeco);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F3)
			{
				SwitchModule(EditorModule.Pathing);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F4)
			{
				SwitchModule(EditorModule.Objects);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F5)
			{
				SwitchModule(EditorModule.Coordinates);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F6)
			{
				SwitchModule(EditorModule.Clipboard);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void RotateSelectedObject(float angleDelta)
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldRot = node3D.RotationDegrees;
			Vector3 newRot = oldRot;
			newRot.Y = (newRot.Y + angleDelta + 360.0f) % 360.0f;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				oldRot, newRot,
				node3D.Scale, node3D.Scale,
				isEnemy, isEnemy
			);
			node3D.RotationDegrees = newRot;
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(string.Format(TranslationServer.Translate("Rotated Object to {0}°"), newRot.Y));
		}
	}

	private void ScaleSelectedObject(float scaleDelta)
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldScale = node3D.Scale;
			float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 4.0f);
			Vector3 newScale = Vector3.One * newScaleVal;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				node3D.RotationDegrees, node3D.RotationDegrees,
				oldScale, newScale,
				isEnemy, isEnemy
			);
			node3D.Scale = newScale;
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(string.Format(TranslationServer.Translate("Scaled Object to {0:F1}x"), newScaleVal));
		}
	}

	private void ResetScaleSelectedObject()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldScale = node3D.Scale;
			Vector3 newScale = Vector3.One;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				node3D.RotationDegrees, node3D.RotationDegrees,
				oldScale, newScale,
				isEnemy, isEnemy
			);
			node3D.Scale = newScale;
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(TranslationServer.Translate("Reset Object scale to 1.0x"));
		}
	}

	public void LocateSelectedObjectAction()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected) && selected is Node3D node3D)
		{
			(GameHost.Instance.MainCamera as CameraControl)?.FocusOnPosition(node3D.Position);
			ShowFeedback(string.Format(TranslationServer.Translate("Focused camera on {0}"), selected.Name));
		}
		else
		{
			ShowFeedback(TranslationServer.Translate("No object selected to locate"));
		}
	}



	private void SetupMinimap()
	{
		if (_minimapArea == null) return;

		var minimapBg = new TextureRect();
		minimapBg.Name = "MinimapBg";
		minimapBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		minimapBg.StretchMode = TextureRect.StretchModeEnum.Scale;
		minimapBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		minimapBg.MouseFilter = Control.MouseFilterEnum.Ignore;
		_minimapArea.AddChild(minimapBg);
		_minimapArea.MoveChild(minimapBg, 0);
	}

	public void RegenerateMinimap()
	{
		_minimapController?.RegenerateMinimap();
	}

	private void OpenScaleMapDialog()
	{
		if (_scaleMapDialog != null) return;

		GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);

		_scaleMapDialog = new PanelContainer();
		_scaleMapDialog.Name = "ScaleMapDialog";
		_scaleMapDialog.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		_scaleMapDialog.SetAnchorsPreset(Control.LayoutPreset.Center);
		_scaleMapDialog.CustomMinimumSize = new Vector2(320, 0);
		_scaleMapDialog.GrowHorizontal = Control.GrowDirection.Both;
		_scaleMapDialog.GrowVertical = Control.GrowDirection.Both;
		AddChild(_scaleMapDialog);
		_scaleMapDialog.MoveToFront();

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		_scaleMapDialog.AddChild(margin);

		var innerVBox = new VBoxContainer();
		innerVBox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(innerVBox);

		var titleLabel = new Label();
		titleLabel.Text = TranslationServer.Translate("⚖ Scale Map");
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		titleLabel.AddThemeFontSizeOverride("font_size", 14);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(titleLabel);

		var hintLabel = new Label();
		hintLabel.Text = TranslationServer.Translate("Sets the new size. All terrain and entity\npositions will be scaled proportionally.");
		hintLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
		hintLabel.AddThemeFontSizeOverride("font_size", 10);
		hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(hintLabel);

		var sizeGrid = new GridContainer();
		sizeGrid.Columns = 3;
		sizeGrid.AddThemeConstantOverride("h_separation", 8);
		sizeGrid.AddThemeConstantOverride("v_separation", 6);
		innerVBox.AddChild(sizeGrid);

		_lblScalePreviewWidth = new Label();
		_lblScalePreviewWidth.AddThemeFontSizeOverride("font_size", 11);
		_lblScalePreviewWidth.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblScalePreviewWidth.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sizeGrid.AddChild(_lblScalePreviewWidth);

		var btnW_Dec = new Button();
		btnW_Dec.Set("icon_max_width", 0);
		SetupButton(btnW_Dec, "➖", () =>
		{
			if (_scaleDialogTargetWidth > 32)
			{
				_scaleDialogTargetWidth = Math.Max(32, _scaleDialogTargetWidth - 16);
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Decrease target width");
		sizeGrid.AddChild(btnW_Dec);

		var btnW_Inc = new Button();
		btnW_Inc.Set("icon_max_width", 0);
		SetupButton(btnW_Inc, "➕", () =>
		{
			if (_scaleDialogTargetWidth < 512)
			{
				_scaleDialogTargetWidth = Math.Min(512, _scaleDialogTargetWidth + 16);
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Increase target width");
		sizeGrid.AddChild(btnW_Inc);

		_lblScalePreviewHeight = new Label();
		_lblScalePreviewHeight.AddThemeFontSizeOverride("font_size", 11);
		_lblScalePreviewHeight.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblScalePreviewHeight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sizeGrid.AddChild(_lblScalePreviewHeight);

		UpdateScaleDialogLabels();

		var btnH_Dec = new Button();
		btnH_Dec.Set("icon_max_width", 0);
		SetupButton(btnH_Dec, "➖", () =>
		{
			if (_scaleDialogTargetDepth > 32)
			{
				_scaleDialogTargetDepth = Math.Max(32, _scaleDialogTargetDepth - 16);
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Decrease target depth");
		sizeGrid.AddChild(btnH_Dec);

		var btnH_Inc = new Button();
		btnH_Inc.Set("icon_max_width", 0);
		SetupButton(btnH_Inc, "➕", () =>
		{
			if (_scaleDialogTargetDepth < 512)
			{
				_scaleDialogTargetDepth = Math.Min(512, _scaleDialogTargetDepth + 16);
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Increase target depth");
		sizeGrid.AddChild(btnH_Inc);

		var separator = new HSeparator();
		innerVBox.AddChild(separator);

		var buttonRow = new HBoxContainer();
		buttonRow.AddThemeConstantOverride("separation", 8);
		innerVBox.AddChild(buttonRow);

		var btnCancel = new Button();
		btnCancel.Set("icon_max_width", 0);
		btnCancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SetupButton(btnCancel, "✖ CANCEL", () =>
		{
			GameHost.Instance?.HideScaleMapSilhouette();
			CloseScaleMapDialog();
		}, 11, "Cancel and discard the scale operation");
		buttonRow.AddChild(btnCancel);

		var btnApply = new Button();
		btnApply.Set("icon_max_width", 0);
		btnApply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SetupButton(btnApply, "✔ APPLY", () =>
		{
			GameHost.Instance?.HideScaleMapSilhouette();
			GameHost.Instance?.ScaleMapExternal(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			CloseScaleMapDialog();
		}, 11, "Apply scale and stretch the entire map");
		buttonRow.AddChild(btnApply);

		if (ReturningFromTest)
		{
			_sldBrushSize.Value = SavedBrushRadius;
			_sldBrushStrength.Value = SavedBrushStrength;


			if (SavedActiveTool == GameHost.EditorTool.PlaceUnit ||
				SavedActiveTool == GameHost.EditorTool.PlaceProp ||
				SavedActiveTool == GameHost.EditorTool.PlaceDecal)
			{
				_entityPaletteController?.SelectCategoryItemExternal(SavedEntityCategory, SavedActivePlaceId);
			}
			else
			{
				Button toolBtn = GetButtonForTool(SavedActiveTool, SavedActivePlaceId);
				if (toolBtn != null)
				{
					TriggerToolSelection(SavedActiveTool, toolBtn, SavedActivePlaceId);
				}
			}

			ReturningFromTest = false;
		}
	}

	private void UpdateScaleDialogLabels()
	{
		if (_lblScalePreviewWidth != null)
			_lblScalePreviewWidth.Text = $"W: {_scaleDialogTargetWidth}";
		if (_lblScalePreviewHeight != null)
			_lblScalePreviewHeight.Text = $"H: {_scaleDialogTargetDepth}";
	}

	private void CloseScaleMapDialog()
	{
		if (_scaleMapDialog != null && GodotObject.IsInstanceValid(_scaleMapDialog))
		{
			_scaleMapDialog.QueueFree();
			_scaleMapDialog = null;
		}
		_lblScalePreviewWidth = null;
		_lblScalePreviewHeight = null;
	}

	private void SetupButton(Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		btn.Text = TranslationServer.Translate(text);
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}
		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupTextureSwatches(bool connectEvents = false)
	{
		_swatchPaths[0] = "res://Assets/2d/TileSheets/ancient_ruin.png";
		_swatchDisplayNames[0] = "Ancient Ruin";
		_swatchColors[0] = new Color(0.95f, 0.95f, 1.0f);

		_swatchPaths[1] = "res://Assets/2d/TileSheets/deep_moss.png";
		_swatchDisplayNames[1] = "Deep Moss";
		_swatchColors[1] = new Color(0.5f, 0.5f, 0.52f);

		_swatchPaths[2] = "res://Assets/2d/TileSheets/grey_slate.png";
		_swatchDisplayNames[2] = "Gray Slate";
		_swatchColors[2] = new Color(0.5f, 0.45f, 0.38f);

		_swatchPaths[3] = "res://Assets/2d/TileSheets/iron_dust.png";
		_swatchDisplayNames[3] = "Iron Dust";
		_swatchColors[3] = new Color(0.2f, 0.6f, 0.2f);

		_swatchPaths[4] = "res://Assets/2d/TileSheets/lava_vein.png";
		_swatchDisplayNames[4] = "Lava Vein";
		_swatchColors[4] = new Color(0.38f, 0.38f, 0.4f);

		_swatchPaths[5] = "res://Assets/2d/TileSheets/mossy_stone.png";
		_swatchDisplayNames[5] = "Mossy Stone";
		_swatchColors[5] = new Color(0.4f, 0.28f, 0.18f);

		_swatchPaths[6] = "res://Assets/2d/TileSheets/pale_sand.png";
		_swatchDisplayNames[6] = "Pale Sand";
		_swatchColors[6] = new Color(0.3f, 0.7f, 0.2f);

		_swatchPaths[7] = "res://Assets/2d/TileSheets/river_silt.png";
		_swatchDisplayNames[7] = "River Silt";
		_swatchColors[7] = new Color(0.12f, 0.48f, 0.18f);

		_swatchPaths[8] = "res://Assets/2d/TileSheets/royal_marble.png";
		_swatchDisplayNames[8] = "Royal Marble";
		_swatchColors[8] = new Color(0.7f, 0.55f, 0.35f);

		_swatchPaths[9] = "res://Assets/2d/TileSheets/tarn_mud.png";
		_swatchDisplayNames[9] = "Tarn Mud";
		_swatchColors[9] = new Color(0.85f, 0.75f, 0.5f);

		_swatchPaths[10] = "res://Assets/2d/TileSheets/dark_wood.png";
		_swatchDisplayNames[10] = "Dark Wood";
		_swatchColors[10] = new Color(0.45f, 0.55f, 0.65f);

		_swatchPaths[11] = "res://Assets/2d/TileSheets/mist_grove.png";
		_swatchDisplayNames[11] = "Mist Grove";
		_swatchColors[11] = new Color(0.6f, 0.3f, 0.15f);

		for (int i = 0; i < 12; i++)
		{
			var btn = _swatchButtons[i];
			if (btn == null) continue;

			btn.Flat = false;
			btn.ExpandIcon = true;
			btn.FocusMode = FocusModeEnum.None;
			btn.CustomMinimumSize = new Vector2(46, 46);
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());

			foreach (var child in btn.GetChildren())
			{
				btn.RemoveChild(child);
				child.QueueFree();
			}

			Texture2D tex = GetSwatchTexture(i);

			if (tex != null)
			{
				var texRect = new TextureRect();
				texRect.Texture = tex;
				texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
				texRect.MouseFilter = MouseFilterEnum.Ignore;
				texRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				texRect.GrowHorizontal = GrowDirection.Both;
				texRect.GrowVertical = GrowDirection.Both;
				btn.AddChild(texRect);
			}
			else
			{
				var colorBox = new ColorRect();
				colorBox.Color = _swatchColors[i];
				colorBox.MouseFilter = MouseFilterEnum.Ignore;
				colorBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				colorBox.GrowHorizontal = GrowDirection.Both;
				colorBox.GrowVertical = GrowDirection.Both;
				btn.AddChild(colorBox);
			}

			if (connectEvents)
			{
				int index = i;
				btn.GuiInput += (@event) =>
				{
					if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
					{
						if (mouseEvent.ButtonIndex == MouseButton.Left)
						{
							if (Input.IsKeyPressed(Godot.Key.Shift))
							{
								SelectCliffTexture(index);
							}
							else
							{
								SelectTerrainTexture(index, btn);
							}
						}
						else if (mouseEvent.ButtonIndex == MouseButton.Right)
						{
							SelectCliffTexture(index);
						}
					}
				};
			}
		}

		UpdateTextureLabels();
	}

	private void SelectTerrainTexture(int index, Button swatch)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorPaintTextureIndex = index;
			HighlightSwatch(swatch);

			if (!IsSwatchCompatibleTool(GameHost.Instance.ActiveEditorTool))
			{
				TriggerToolSelection(GameHost.EditorTool.PaintGrass, _btnTextureBrush);
			}
			
			UpdateTextureLabels();
			
			string name = TranslationServer.Translate(_swatchDisplayNames[index]);
			ShowFeedback(string.Format(TranslationServer.Translate("Selected Terrain: {0}"), name));
		}
	}

	private void SelectCliffTexture(int index)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorCliffPaintTextureIndex = index;
			UpdateTextureLabels();
			
			string name = TranslationServer.Translate(_swatchDisplayNames[index]);
			ShowFeedback(string.Format(TranslationServer.Translate("Selected Cliff Face: {0}"), name));
		}
	}

	private static bool IsSwatchCompatibleTool(GameHost.EditorTool tool) => tool switch
	{
		GameHost.EditorTool.Raise       => true,
		GameHost.EditorTool.Lower       => true,
		GameHost.EditorTool.Smooth      => true,
		GameHost.EditorTool.Plateau     => true,
		GameHost.EditorTool.Ramp        => true,
		GameHost.EditorTool.Noise       => true,
		GameHost.EditorTool.PaintGrass  => true,
		GameHost.EditorTool.FloodFill   => true,
		GameHost.EditorTool.Eyedropper  => true,
		GameHost.EditorTool.SelectArea  => true,
		_ => false
	};

	private void UpdateTextureLabels()
	{
		if (GameHost.Instance == null) return;
		int terrainIdx = GameHost.Instance.EditorPaintTextureIndex;
		int cliffIdx = GameHost.Instance.EditorCliffPaintTextureIndex;

		string terrainName = (terrainIdx >= 0 && terrainIdx < 12) ? _swatchDisplayNames[terrainIdx] : "Unknown";
		string cliffName = (cliffIdx >= 0 && cliffIdx < 12) ? _swatchDisplayNames[cliffIdx] : "Unknown";

		if (_lblTerrainTexture != null) _lblTerrainTexture.Text = $"{TranslationServer.Translate("Brush")}: {TranslationServer.Translate(terrainName)}";
		if (_lblCliffTexture != null) _lblCliffTexture.Text = $"{TranslationServer.Translate("Cliff Face")}: {TranslationServer.Translate(cliffName)}";

		Button terrainSwatch = (terrainIdx >= 0 && terrainIdx < 12) ? _swatchButtons[terrainIdx] : null;
		Button cliffSwatch = (cliffIdx >= 0 && cliffIdx < 12) ? _swatchButtons[cliffIdx] : null;

		HighlightSwatch(terrainSwatch);
		HighlightCliffSwatch(cliffSwatch);
	}

	private string GetSwatchName(Color color)
	{
		float epsilon = 0.01f;
		for (int i = 1; i <= 12; i++)
		{
			Color c = GetSwatchColor(i);
			if (Mathf.Abs(color.R - c.R) < epsilon &&
				Mathf.Abs(color.G - c.G) < epsilon &&
				Mathf.Abs(color.B - c.B) < epsilon)
			{
				string texName = (i >= 1 && i <= 12 && _swatchDisplayNames != null) ? _swatchDisplayNames[i - 1] : "Unknown";
				return texName;
			}
		}
		return "Custom";
	}

	private Color GetSwatchColor(int index)
	{
		if (index >= 1 && index <= 12 && _swatchColors != null)
		{
			return _swatchColors[index - 1];
		}
		return new Color(1, 1, 1);
	}

	public bool IsMouseOverUI(Vector2 mousePos)
	{
		if (_minimapController != null && _minimapController.IsDragging)
		{
			return true;
		}
		if (_isDraggingSlider)
		{
			return true;
		}
		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl != null && hoveredControl != this)
		{
			return true;
		}
		if (_optSkybox != null && _optSkybox.GetPopup() != null && _optSkybox.GetPopup().Visible)
		{
			return true;
		}
		if (_optModule != null && _optModule.GetPopup() != null && _optModule.GetPopup().Visible)
		{
			return true;
		}
		if (_entityPaletteController?.OptCategoryItems != null && _entityPaletteController.OptCategoryItems.GetPopup() != null && _entityPaletteController.OptCategoryItems.GetPopup().Visible)
		{
			return true;
		}
		if (_optEyedropperMode != null && _optEyedropperMode.GetPopup() != null && _optEyedropperMode.GetPopup().Visible)
		{
			return true;
		}
		if (_optPathingMode != null && _optPathingMode.GetPopup() != null && _optPathingMode.GetPopup().Visible)
		{
			return true;
		}
		if (_leftPanelExpanded && _panelLeft != null && _panelLeft.Visible && mousePos.X < _panelLeft.Size.X)
		{
			return true;
		}
		if (_rightPanelExpanded && _panelRight != null && _panelRight.Visible && mousePos.X > GetViewportRect().Size.X - _panelRight.Size.X)
		{
			return true;
		}
		if (_btnLeftTab != null && _btnLeftTab.Visible && _btnLeftTab.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_btnRightTab != null && _btnRightTab.Visible && _btnRightTab.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		var topLeftBox = GetNodeOrNull<Control>("TopLeftBox");
		if (topLeftBox != null && topLeftBox.Visible && topLeftBox.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		var topBar = GetNodeOrNull<Control>("TopBar");
		if (topBar != null && topBar.Visible && topBar.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_topToolbar != null && _topToolbar.Visible && _topToolbar.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_middleRightBox != null && _middleRightBox.Visible && _middleRightBox.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_scaleMapDialog != null && _scaleMapDialog.Visible && _scaleMapDialog.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		var genOverlay = GetNodeOrNull<Control>("GenerationOverlay");
		if (genOverlay != null && genOverlay.Visible && genOverlay.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}

		return false;
	}

	private void UpdateBlockStepVisibility()
	{
		if (_stepBox != null)
		{
			bool toolSupportsBlockMode = (_chkBlockMode != null && _chkBlockMode.Visible);
			bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.ButtonPressed);
			_stepBox.Visible = toolSupportsBlockMode && blockModeEnabled;
		}
	}

	private void UpdateBrushStrengthVisibility()
	{
		if (_sldBrushStrength != null && _sldBrushStrength.GetParent() is Control strengthParent)
		{
			if (GameHost.Instance == null) return;
			var tool = GameHost.Instance.ActiveEditorTool;
			bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.ButtonPressed);

			if (tool == GameHost.EditorTool.Raise || tool == GameHost.EditorTool.Lower)
			{
				strengthParent.Visible = !blockModeEnabled;
			}
			else
			{
				strengthParent.Visible = (tool != GameHost.EditorTool.PaintPathing && 
										  tool != GameHost.EditorTool.FloodFillPathing &&
										  tool != GameHost.EditorTool.PlacePropClump &&
										  tool != GameHost.EditorTool.Plateau &&
										  tool != GameHost.EditorTool.Ramp);
			}
		}
	}

	private void ShowAgreementModal()
	{
		var overlay = new ColorRect();
		overlay.Name = "AgreementOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(1050, 780);
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		margin.AddThemeConstantOverride("margin_left", 25);
		margin.AddThemeConstantOverride("margin_right", 25);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		margin.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, "Realm Creator Agreement", 18);
		lblTitle.Text = "Realm Creator Agreement";
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(lblTitle);

		var richText = new RichTextLabel();
		richText.BbcodeEnabled = true;
		richText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		richText.SizeFlagsVertical = SizeFlags.ExpandFill;
		richText.CustomMinimumSize = new Vector2(975, 570);
		richText.ScrollActive = true;
		richText.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.95f));
		richText.AddThemeFontSizeOverride("normal_font_size", 13);
		richText.AddThemeFontSizeOverride("bold_font_size", 13);

		string agreementText = 
			"By publishing content on Realm, you grant us permission to host, distribute, and display your map so people can play it.\n\n" +
			"We want Realm to be a thriving, collaborative arcade. Please respect these rules:\n\n" +
			"[color=#d4af37][b]- Your Work is Yours[/b][/color]\n" +
			"You retain full ownership of your original creations. You aren't signing away your copyright to anyone.\n\n" +
			"[color=#d4af37][b]- Collaboration[/b][/color]\n" +
			"By publishing your content on Realm, you allow other creators to open and learn from your work. You also grant them permission to adapt, build upon, and incorporate it into their own creations, provided those new works remain exclusively within the Realm platform.\n\n" +
			"[color=#d4af37][b]- Give Credit[/b][/color]\n" +
			"If you import another creator's work, they still own the original. Never claim their work as your own.\n\n" +
			"[color=#d4af37][b]- Monetization[/b][/color]\n" +
			"You may ask for donations from your player base, but you may not offer any differences in gameplay compared to non-paying users, other than cosmetic rewards. Pay-to-win is not allowed.\n\n" +
			"[color=#d4af37][b]- Going Solo[/b][/color]\n" +
			"Want to turn your map into a standalone game? Go for it! However, you can only take your original work with you. You must remove and re-create any official Realm assets as well as content you imported from other Realm users, unless you obtain their explicit written permission.\n\n" +
			"[color=#d4af37][b]- No Plagiarism or Piracy[/b][/color]\n" +
			"Do not upload content you didn’t make or don't have the rights to use. This includes trademarked content from other video games, movies, music, and media.";

		richText.Text = agreementText;
		vbox.AddChild(richText);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 40);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		var btnAccept = new Button();
		btnAccept.Set("icon_max_width", 0);
		SetupButton(btnAccept, "Accept", () =>
		{
			overlay.QueueFree();
		}, 13);
		btnAccept.Text = "Accept";
		btnAccept.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		hbox.AddChild(btnAccept);

		var btnQuit = new Button();
		btnQuit.Set("icon_max_width", 0);
		SetupButton(btnQuit, "Quit", () =>
		{
			overlay.QueueFree();
			UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
		}, 13);
		btnQuit.Text = "Quit";
		btnQuit.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		hbox.AddChild(btnQuit);
	}

	private void InitializeInspectorPanel()
	{
		_inspectorPanel = GetNode<PanelContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel");
		_lblInspectorTitle = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorTitle");
		_lblInspectorPos = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorPos");
		
		_btnInspectorRotLeft = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorRotLeft");
		SetupButton(_btnInspectorRotLeft, "🔄 Rot -15°", () => RotateSelectedObjectAction(-15f), 11, "Rotate object counter-clockwise");

		_btnInspectorRotRight = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorRotRight");
		SetupButton(_btnInspectorRotRight, "🔄 Rot +15°", () => RotateSelectedObjectAction(15f), 11, "Rotate object clockwise");

		_btnInspectorScaleDown = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorScaleDown");
		SetupButton(_btnInspectorScaleDown, "➖ Scale Down", () => ScaleSelectedObjectAction(0.9f), 11, "Shrink object size by 10%");

		_btnInspectorScaleUp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorScaleUp");
		SetupButton(_btnInspectorScaleUp, "➕ Scale Up", () => ScaleSelectedObjectAction(1.1f), 11, "Enlarge object size by 10%");

		_btnInspectorScaleReset = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnInspectorScaleReset");
		SetupButton(_btnInspectorScaleReset, "📐 Reset Scale", () => ScaleSelectedObjectAction(-1f), 12, "Reset object scale size to 1.0x");

		_btnCenter = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnCenter");
		SetupButton(_btnCenter, "🎯 Locate Object", () => LocateSelectedObjectAction(), 12, "Center camera on selected object");

		_btnInspectorDelete = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnInspectorDelete");
		SetupButton(_btnInspectorDelete, "❌ Erase", () => DeleteSelectedObjectAction(), 12, "Erase selected unit, prop, or decal");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent)
		{
			if (keyEvent.Keycode == Godot.Key.Tab)
			{
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Godot.Key.Quoteleft)
			{
				var focusOwner = GetViewport().GuiGetFocusOwner();
				if (focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit))
				{
					return;
				}
				if (Realm.Godot.UI.WasmConsoleWindow.IsSinglePlayerOrTestMode())
				{
					Realm.Godot.UI.WasmConsoleWindow.Instance.ToggleVisibility();
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			if (keyEvent.Pressed && (keyEvent.Keycode == Godot.Key.Up || keyEvent.Keycode == Godot.Key.Down || 
				keyEvent.Keycode == Godot.Key.Left || keyEvent.Keycode == Godot.Key.Right))
			{
				var focusOwner = GetViewport().GuiGetFocusOwner();
				if (focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit))
				{
					return;
				}
				GetViewport().SetInputAsHandled();
			}
		}
	}
	private async void TestMapAction()
	{
		if (GameHost.Instance == null) return;

		if (GameHost.Instance.AllUnits.Count == 0)
		{
			ShowConfirmationDialog(
				"Warning: You have not placed any units, you won't see anything due to Fog of War.",
				async () => await ProceedToTestMap(),
				"Okay",
				"Cancel"
			);
		}
		else
		{
			await ProceedToTestMap();
		}
	}

	public async System.Threading.Tasks.Task ProceedToTestMap()
	{
		if (GameHost.Instance == null) return;

		ShowWasmConsoleModal();
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		Realm.Godot.WasmRuntime.OnWasmLog += logHandler;

		try
		{
			var camera = GameHost.Instance.MainCamera as CameraControl;
			if (camera != null)
			{
				SavedCameraPosition = camera.Position;
				if (GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.CameraState>(GameHost.Instance.WorldEntity))
				{
					var state = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.CameraState>(GameHost.Instance.WorldEntity);
					SavedTargetHeight = state.TargetHeight;
					SavedCurrentHeight = state.CurrentHeight;
					SavedTargetYaw = state.TargetYaw;
					SavedCurrentYaw = state.CurrentYaw;
					SavedTargetPitch = state.TargetPitch;
					SavedCurrentPitch = state.CurrentPitch;
					SavedIsTopDown = state.IsTopDown;
					SavedYawSwing = state.YawSwing;
					SavedPitchSwing = state.PitchSwing;
				}
			}
			SavedGridMode = GameHost.Instance.EditorGridMode;
			SavedActiveTool = GameHost.Instance.ActiveEditorTool;
			SavedActivePlaceId = GameHost.Instance.ActivePlaceId;
			SavedCameraBoundsVisible = GameHost.Instance.EditorCameraBoundsVisible;
			SavedEntityCategory = _entityPaletteController?.CurrentCategory ?? "";
			SavedBrushRadius = (float)_sldBrushSize.Value;
			SavedBrushStrength = (float)_sldBrushStrength.Value;

			string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");

			if (!string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
			{
				CopyFolderToTempWorkspace(_currentSourceFolder);
			}

			GameHost.Instance.SaveMapToFile(tempTerrainPath);
			GameHost.Instance.EditorHasUnsavedChanges = false;

			SetWasmConsoleStatus("Compiling WASM map script...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("=== WASM COMPILATION PIPELINE STARTED ===");

			// Compile map script DLL (skip attribution/signing during test mode)
			await CompileAndSignMapAsync(_tempWorkspacePath, skipAttribution: true);

			if (_wasmHasErrors)
			{
				SetWasmConsoleStatus("❌ WASM Compilation Failed", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog("[ERROR] Map script compilation failed. Test mode aborted.");
				return;
			}

			// Find compiled map script WASM
			string binDir = System.IO.Path.Combine(_tempWorkspacePath, "bin");
			string wasmPath = null;
			if (System.IO.Directory.Exists(binDir))
			{
				wasmPath = System.IO.Directory.GetFiles(
					binDir,
					"*.wasm",
					System.IO.SearchOption.AllDirectories
				).FirstOrDefault();
			}
			if (System.IO.File.Exists(wasmPath))
			{
				GameHost.PendingMapScriptPath = wasmPath;
				AppendWasmConsoleLog($"[WASM] Located compiled WASM binary: {System.IO.Path.GetFileName(wasmPath)}");
			}
			else
			{
				_wasmHasErrors = true;
				SetWasmConsoleStatus("❌ WASM Compilation Failed: Output binary missing", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog($"[ERROR] Could not find compiled WASM in {binDir}. Test mode aborted.");
				return;
			}

			SetWasmConsoleStatus("Launching test mode...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("=== LAUNCHING GAME ENGINE ===");

			GameHost.Instance.ExitMapEditorMode();
			IsTestMode = true;

			if (UIManager.Instance != null)
			{
				UIManager.Instance.TransitionTo(GameScreen.InGameHUD);
			}

			if (LobbyManager.Instance != null)
			{
				LobbyManager.Instance.HostSinglePlayerGame(TempWorkspaceGodotPath, "Test Map");
			}

			// Close console modal once InGameHUD has started
			await System.Threading.Tasks.Task.Delay(500);
			CloseWasmConsoleModal();
		}
		catch (Exception ex)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Error launching test mode", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[RUNTIME EXCEPTION] {ex}");
		}
		finally
		{
			Realm.Godot.WasmRuntime.OnWasmLog -= logHandler;
		}
	}

	private Texture2D GetSwatchTexture(int i)
	{
		var names = new[]
		{
			"ancient_ruin", "deep_moss", "grey_slate", "iron_dust",
			"lava_vein", "mossy_stone", "pale_sand", "river_silt",
			"royal_marble", "tarn_mud", "dark_wood", "mist_grove"
		};
		string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
			? ProjectSettings.GlobalizePath("user://temp_map_workspace") 
			: _tempWorkspacePath;
		string localKtx2 = System.IO.Path.Combine(wsPath, names[i] + ".ktx2");
		if (System.IO.File.Exists(localKtx2))
		{
			string tempOut = $"user://temp_swatch_{i}_{System.Guid.NewGuid()}.png";
			string globalTempOut = ProjectSettings.GlobalizePath(tempOut);
			string ktxCmd = System.IO.Path.GetFullPath(System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("res://"), "..", "ktx_tools", "v5.0.0-rc1", "bin", "ktx.exe"));
			try
			{
				var startInfo = new System.Diagnostics.ProcessStartInfo
				{
					FileName = ktxCmd,
					WorkingDirectory = System.IO.Path.GetDirectoryName(ktxCmd),
					Arguments = $"extract --layer 0 --level 0 --transcode rgba8 \"{localKtx2}\" \"{globalTempOut}\"",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using (var process = System.Diagnostics.Process.Start(startInfo))
				{
					process.WaitForExit();
					if (process.ExitCode == 0)
					{
						var img = Image.LoadFromFile(globalTempOut);
						if (img != null)
						{
							return ImageTexture.CreateFromImage(img);
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to extract swatch preview: {ex.Message}");
			}
			finally
			{
				if (System.IO.File.Exists(globalTempOut)) System.IO.File.Delete(globalTempOut);
			}
		}
		if (ResourceLoader.Exists(_swatchPaths[i]))
		{
			return GD.Load<Texture2D>(_swatchPaths[i]);
		}
		return null;
	}

	private void ImportTextureAction()
	{
		if (GameHost.Instance == null) return;
		int selectedIdx = GameHost.Instance.EditorPaintTextureIndex;
		if (selectedIdx < 0 || selectedIdx >= 12)
		{
			ShowFeedback(TranslationServer.Translate("Please select a texture slot first"));
			return;
		}
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Import Texture Image"),
			GetInitialDirectory(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new string[] { "*.png", "*.jpg", "*.jpeg" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string imagePath = selectedPaths[0];
					ImportTextureFile(imagePath, selectedIdx);
				}
				else
				{
					ShowFeedback(TranslationServer.Translate("Import cancelled"));
				}
			})
		);
		if (err != Error.Ok)
		{
			ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ImportTextureFile(string imagePath, int index)
	{
		var names = new[]
		{
			"ancient_ruin", "deep_moss", "grey_slate", "iron_dust",
			"lava_vein", "mossy_stone", "pale_sand", "river_silt",
			"royal_marble", "tarn_mud", "dark_wood", "mist_grove"
		};
		string name = names[index];
		string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
			? ProjectSettings.GlobalizePath("user://temp_map_workspace") 
			: _tempWorkspacePath;
		string outputKtx2 = System.IO.Path.Combine(wsPath, name + ".ktx2");
		ShowFeedback(TranslationServer.Translate("Importing texture..."));
		try
		{
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(imagePath, outputKtx2);
				GameHost.Instance.GroundTerrain.ReloadTerrainTextures();
				SetupTextureSwatches(false);
				ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported custom texture for {0}!"), _swatchDisplayNames[index]));
			}
		}
		catch (Exception ex)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Failed to import texture: {0}"), ex.Message));
			GD.PrintErr($"Failed to import texture: {ex.Message}");
		}
	}

	private Button _btnHeaderLightingTuning;
	private VBoxContainer _contentLightingTuning;

	private bool _tuneOverrideDayNight = true;

	private float _tuneSunPitch = 55.0f;
	private float _tuneSunYaw = 20.0f;
	private float _tuneSunEnergy = 3.20f;
	private float _tuneSunR = 1.000f;
	private float _tuneSunG = 0.957f;
	private float _tuneSunB = 0.878f;

	private float _tuneAmbientEnergy = 2.00f;
	private float _tuneAmbientR = 0.400f;
	private float _tuneAmbientG = 0.600f;
	private float _tuneAmbientB = 0.850f;

	private bool _tuneFogEnabled = true;
	private float _tuneFogDensity = 0.0150f;
	private float _tuneFogR = 0.080f;
	private float _tuneFogG = 0.100f;
	private float _tuneFogB = 0.150f;

	private bool _tuneSsaoEnabled = true;
	private float _tuneSsaoRadius = 1.80f;
	private float _tuneSsaoIntensity = 0.80f;

	private float _tuneExposure = 1.00f;
	private float _tuneContrast = 1.00f;
	private float _tuneSaturation = 1.05f;

	private float _tuneBloomIntensity = 0.60f;
	private float _tuneBloomThreshold = 0.12f;

	private HSlider _sldSunPitch, _sldSunYaw, _sldSunEnergy, _sldSunR, _sldSunG, _sldSunB;
	private HSlider _sldShadowPitch, _sldShadowYaw, _sldShadowEnergy, _sldShadowOpacity;
	private HSlider _sldAmbientEnergy, _sldAmbientR, _sldAmbientG, _sldAmbientB;
	private HSlider _sldFogDensity, _sldFogR, _sldFogG, _sldFogB;
	private HSlider _sldSsaoRadius, _sldSsaoIntensity;
	private HSlider _sldExposure, _sldContrast, _sldSaturation, _sldBloomIntensity, _sldBloomThreshold;

	private void SetupLightingTuningUI()
	{
		var leftSlidePanel = GetNodeOrNull<Control>("LeftSlidePanel");
		if (leftSlidePanel != null)
		{
			leftSlidePanel.CustomMinimumSize = new Vector2(325, 0);
			leftSlidePanel.OffsetRight = 325.0f;
		}

		var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		if (leftVBox == null) return;

		var lightingAccordion = new VBoxContainer();
		lightingAccordion.Name = "LightingTuningAccordion";

		_btnHeaderLightingTuning = new Button();
		_btnHeaderLightingTuning.Name = "BtnHeaderLightingTuning";
		lightingAccordion.AddChild(_btnHeaderLightingTuning);

		_contentLightingTuning = new VBoxContainer();
		_contentLightingTuning.Name = "ContentLightingTuning";
		lightingAccordion.AddChild(_contentLightingTuning);

		leftVBox.AddChild(lightingAccordion);

		StyleAccordionHeader(_btnHeaderLightingTuning);
		SetupAccordion(_btnHeaderLightingTuning, _contentLightingTuning, "💡 Lighting Tuning (Live Override)");

		CreateToggleRow(_contentLightingTuning, "Freeze Day/Night Cycle (Live Override)", _tuneOverrideDayNight, val => {
			_tuneOverrideDayNight = val;
			ApplyLiveLightingTuning();
		});

		var btnLog = new Button();
		btnLog.Text = "📋 LOG / COPY VALUES TO CLIPBOARD";
		btnLog.CustomMinimumSize = new Vector2(0, 26);
		btnLog.Pressed += LogLightingTuningValues;
		_contentLightingTuning.AddChild(btnLog);

		CreateSectionHeader(_contentLightingTuning, "--- SUN (PRIMARY LIGHT) ---");
		_sldSunPitch = CreateSliderRow(_contentLightingTuning, "Sun Pitch", -90f, 90f, 1f, _tuneSunPitch, val => { _tuneSunPitch = val; ApplyLiveLightingTuning(); });
		_sldSunYaw = CreateSliderRow(_contentLightingTuning, "Sun Yaw", -180f, 180f, 1f, _tuneSunYaw, val => { _tuneSunYaw = val; ApplyLiveLightingTuning(); });
		_sldSunEnergy = CreateSliderRow(_contentLightingTuning, "Sun Energy", 0f, 5f, 0.05f, _tuneSunEnergy, val => { _tuneSunEnergy = val; ApplyLiveLightingTuning(); });
		_sldSunR = CreateSliderRow(_contentLightingTuning, "Sun Red", 0f, 1f, 0.01f, _tuneSunR, val => { _tuneSunR = val; ApplyLiveLightingTuning(); });
		_sldSunG = CreateSliderRow(_contentLightingTuning, "Sun Green", 0f, 1f, 0.01f, _tuneSunG, val => { _tuneSunG = val; ApplyLiveLightingTuning(); });
		_sldSunB = CreateSliderRow(_contentLightingTuning, "Sun Blue", 0f, 1f, 0.01f, _tuneSunB, val => { _tuneSunB = val; ApplyLiveLightingTuning(); });

		CreateSectionHeader(_contentLightingTuning, "--- AMBIENT LIGHT ---");
		_sldAmbientEnergy = CreateSliderRow(_contentLightingTuning, "Amb Energy", 0f, 3f, 0.05f, _tuneAmbientEnergy, val => { _tuneAmbientEnergy = val; ApplyLiveLightingTuning(); });
		_sldAmbientR = CreateSliderRow(_contentLightingTuning, "Amb Red", 0f, 1f, 0.01f, _tuneAmbientR, val => { _tuneAmbientR = val; ApplyLiveLightingTuning(); });
		_sldAmbientG = CreateSliderRow(_contentLightingTuning, "Amb Green", 0f, 1f, 0.01f, _tuneAmbientG, val => { _tuneAmbientG = val; ApplyLiveLightingTuning(); });
		_sldAmbientB = CreateSliderRow(_contentLightingTuning, "Amb Blue", 0f, 1f, 0.01f, _tuneAmbientB, val => { _tuneAmbientB = val; ApplyLiveLightingTuning(); });

		CreateSectionHeader(_contentLightingTuning, "--- ATMOSPHERIC FOG ---");
		CreateToggleRow(_contentLightingTuning, "Fog Enabled", _tuneFogEnabled, val => { _tuneFogEnabled = val; ApplyLiveLightingTuning(); });
		_sldFogDensity = CreateSliderRow(_contentLightingTuning, "Fog Density", 0f, 0.03f, 0.0005f, _tuneFogDensity, val => { _tuneFogDensity = val; ApplyLiveLightingTuning(); });
		_sldFogR = CreateSliderRow(_contentLightingTuning, "Fog Red", 0f, 1f, 0.01f, _tuneFogR, val => { _tuneFogR = val; ApplyLiveLightingTuning(); });
		_sldFogG = CreateSliderRow(_contentLightingTuning, "Fog Green", 0f, 1f, 0.01f, _tuneFogG, val => { _tuneFogG = val; ApplyLiveLightingTuning(); });
		_sldFogB = CreateSliderRow(_contentLightingTuning, "Fog Blue", 0f, 1f, 0.01f, _tuneFogB, val => { _tuneFogB = val; ApplyLiveLightingTuning(); });

		CreateSectionHeader(_contentLightingTuning, "--- SSAO (AMBIENT OCCLUSION) ---");
		CreateToggleRow(_contentLightingTuning, "SSAO Enabled", _tuneSsaoEnabled, val => { _tuneSsaoEnabled = val; ApplyLiveLightingTuning(); });
		_sldSsaoRadius = CreateSliderRow(_contentLightingTuning, "SSAO Radius", 0.1f, 5f, 0.1f, _tuneSsaoRadius, val => { _tuneSsaoRadius = val; ApplyLiveLightingTuning(); });
		_sldSsaoIntensity = CreateSliderRow(_contentLightingTuning, "SSAO Intensity", 0f, 6f, 0.1f, _tuneSsaoIntensity, val => { _tuneSsaoIntensity = val; ApplyLiveLightingTuning(); });

		CreateSectionHeader(_contentLightingTuning, "--- POST-PROCESSING ---");
		_sldExposure = CreateSliderRow(_contentLightingTuning, "Exposure", 0.1f, 3f, 0.02f, _tuneExposure, val => { _tuneExposure = val; ApplyLiveLightingTuning(); });
		_sldContrast = CreateSliderRow(_contentLightingTuning, "Contrast", 0.5f, 2f, 0.02f, _tuneContrast, val => { _tuneContrast = val; ApplyLiveLightingTuning(); });
		_sldSaturation = CreateSliderRow(_contentLightingTuning, "Saturation", 0f, 2f, 0.02f, _tuneSaturation, val => { _tuneSaturation = val; ApplyLiveLightingTuning(); });
		_sldBloomIntensity = CreateSliderRow(_contentLightingTuning, "Bloom Intensity", 0f, 2f, 0.05f, _tuneBloomIntensity, val => { _tuneBloomIntensity = val; ApplyLiveLightingTuning(); });
		_sldBloomThreshold = CreateSliderRow(_contentLightingTuning, "Bloom Threshold", 0f, 1f, 0.02f, _tuneBloomThreshold, val => { _tuneBloomThreshold = val; ApplyLiveLightingTuning(); });

		ApplyLiveLightingTuning();
		lightingAccordion.Visible = false;
	}

	public void UpdateLightingTuningSlidersFromPhase(int phaseIndex)
	{
		phaseIndex = Math.Clamp(phaseIndex, 0, 3);

		_tuneSunPitch = EnvironmentService.SunPitches[phaseIndex];
		_tuneSunYaw = EnvironmentService.SunYaws[phaseIndex];
		_tuneSunEnergy = EnvironmentService.SunEnergies[phaseIndex];
		_tuneSunR = EnvironmentService.SunColors[phaseIndex].R;
		_tuneSunG = EnvironmentService.SunColors[phaseIndex].G;
		_tuneSunB = EnvironmentService.SunColors[phaseIndex].B;

		_tuneAmbientEnergy = EnvironmentService.AmbientEnergies[phaseIndex];
		_tuneAmbientR = EnvironmentService.AmbientColors[phaseIndex].R;
		_tuneAmbientG = EnvironmentService.AmbientColors[phaseIndex].G;
		_tuneAmbientB = EnvironmentService.AmbientColors[phaseIndex].B;

		_tuneFogEnabled = true;
		_tuneFogDensity = EnvironmentService.FogDensities[phaseIndex];
		_tuneFogR = EnvironmentService.FogColors[phaseIndex].R;
		_tuneFogG = EnvironmentService.FogColors[phaseIndex].G;
		_tuneFogB = EnvironmentService.FogColors[phaseIndex].B;

		_tuneSsaoEnabled = true;
		_tuneSsaoRadius = EnvironmentService.SsaoRadii[phaseIndex];
		_tuneSsaoIntensity = EnvironmentService.SsaoIntensities[phaseIndex];

		_tuneExposure = EnvironmentService.Exposures[phaseIndex];
		_tuneContrast = EnvironmentService.Contrasts[phaseIndex];
		_tuneSaturation = EnvironmentService.Saturations[phaseIndex];

		_tuneBloomIntensity = EnvironmentService.GlowIntensities[phaseIndex];
		_tuneBloomThreshold = EnvironmentService.GlowBlooms[phaseIndex];

		if (_sldSunPitch != null) _sldSunPitch.Value = _tuneSunPitch;
		if (_sldSunYaw != null) _sldSunYaw.Value = _tuneSunYaw;
		if (_sldSunEnergy != null) _sldSunEnergy.Value = _tuneSunEnergy;
		if (_sldSunR != null) _sldSunR.Value = _tuneSunR;
		if (_sldSunG != null) _sldSunG.Value = _tuneSunG;
		if (_sldSunB != null) _sldSunB.Value = _tuneSunB;

		if (_sldAmbientEnergy != null) _sldAmbientEnergy.Value = _tuneAmbientEnergy;
		if (_sldAmbientR != null) _sldAmbientR.Value = _tuneAmbientR;
		if (_sldAmbientG != null) _sldAmbientG.Value = _tuneAmbientG;
		if (_sldAmbientB != null) _sldAmbientB.Value = _tuneAmbientB;

		if (_sldFogDensity != null) _sldFogDensity.Value = _tuneFogDensity;
		if (_sldFogR != null) _sldFogR.Value = _tuneFogR;
		if (_sldFogG != null) _sldFogG.Value = _tuneFogG;
		if (_sldFogB != null) _sldFogB.Value = _tuneFogB;

		if (_sldSsaoRadius != null) _sldSsaoRadius.Value = _tuneSsaoRadius;
		if (_sldSsaoIntensity != null) _sldSsaoIntensity.Value = _tuneSsaoIntensity;

		if (_sldExposure != null) _sldExposure.Value = _tuneExposure;
		if (_sldContrast != null) _sldContrast.Value = _tuneContrast;
		if (_sldSaturation != null) _sldSaturation.Value = _tuneSaturation;

		if (_sldBloomIntensity != null) _sldBloomIntensity.Value = _tuneBloomIntensity;
		if (_sldBloomThreshold != null) _sldBloomThreshold.Value = _tuneBloomThreshold;

		ApplyLiveLightingTuning();
	}

	private void ApplyLiveLightingTuning()
	{
		if (GameHost.Instance == null) return;
		var host = GameHost.Instance;
		var worldEnv = host.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		var sun = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");

		if (GameHost.Instance.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.OverrideDayNightVisuals = _tuneOverrideDayNight;
		}

		if (sun != null)
		{
			sun.RotationDegrees = new Vector3(_tuneSunPitch, _tuneSunYaw, 0f);
			sun.LightEnergy = _tuneSunEnergy;
			sun.LightColor = new Color(_tuneSunR, _tuneSunG, _tuneSunB);
			sun.ShadowEnabled = false;
		}

		if (worldEnv != null && worldEnv.Environment != null)
		{
			var env = worldEnv.Environment;
			env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
			env.AmbientLightColor = new Color(_tuneAmbientR, _tuneAmbientG, _tuneAmbientB);
			env.AmbientLightEnergy = _tuneAmbientEnergy;

			env.FogEnabled = _tuneFogEnabled;
			env.FogDensity = _tuneFogDensity;
			env.FogLightColor = new Color(_tuneFogR, _tuneFogG, _tuneFogB);

			env.SsaoEnabled = _tuneSsaoEnabled;
			env.SsaoRadius = _tuneSsaoRadius;
			env.SsaoIntensity = _tuneSsaoIntensity;

			env.TonemapMode = Godot.Environment.ToneMapper.Aces;
			env.TonemapExposure = _tuneExposure;
			env.AdjustmentEnabled = true;
			env.AdjustmentContrast = _tuneContrast;
			env.AdjustmentSaturation = _tuneSaturation;

			env.GlowEnabled = true;
			env.GlowIntensity = _tuneBloomIntensity;
			env.GlowBloom = _tuneBloomThreshold;
		}
	}

	private void LogLightingTuningValues()
	{
		string report = $@"
=== LIVE LIGHTING TUNING VALUES ===
Sun Pitch: {_tuneSunPitch:F1}°, Yaw: {_tuneSunYaw:F1}°, Energy: {_tuneSunEnergy:F2}, Color: ({_tuneSunR:F3}f, {_tuneSunG:F3}f, {_tuneSunB:F3}f)
Ambient Energy: {_tuneAmbientEnergy:F2}, Color: ({_tuneAmbientR:F3}f, {_tuneAmbientG:F3}f, {_tuneAmbientB:F3}f) [Hex: #{ColorToHex(_tuneAmbientR, _tuneAmbientG, _tuneAmbientB)}]
Fog Enabled: {_tuneFogEnabled}, Density: {_tuneFogDensity:F4}, Color: ({_tuneFogR:F3}f, {_tuneFogG:F3}f, {_tuneFogB:F3}f)
SSAO Enabled: {_tuneSsaoEnabled}, Radius: {_tuneSsaoRadius:F2}, Intensity: {_tuneSsaoIntensity:F2}
PostProc Exposure: {_tuneExposure:F2}, Contrast: {_tuneContrast:F2}, Saturation: {_tuneSaturation:F2}
Glow/Bloom Intensity: {_tuneBloomIntensity:F2}, Threshold: {_tuneBloomThreshold:F2}
===================================
";
		GD.Print(report);
		DisplayServer.ClipboardSet(report);
		ShowFeedback(TranslationServer.Translate("Copied lighting tuning values to clipboard!"));
	}

	private string ColorToHex(float r, float g, float b)
	{
		int ir = Mathf.Clamp((int)(r * 255f), 0, 255);
		int ig = Mathf.Clamp((int)(g * 255f), 0, 255);
		int ib = Mathf.Clamp((int)(b * 255f), 0, 255);
		return $"{ir:X2}{ig:X2}{ib:X2}";
	}

	private HSlider CreateSliderRow(VBoxContainer parent, string labelText, float min, float max, float step, float initialVal, Action<float> onChanged)
	{
		var row = new HBoxContainer();
		
		var lblName = new Label();
		lblName.Text = labelText;
		lblName.CustomMinimumSize = new Vector2(110, 0);
		lblName.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblName);

		var slider = new HSlider();
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialVal;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.CustomMinimumSize = new Vector2(90, 0);
		slider.DragStarted += () => _isDraggingSlider = true;
		slider.DragEnded += (valueChanged) => _isDraggingSlider = false;
		row.AddChild(slider);

		var lblVal = new Label();
		lblVal.Text = initialVal.ToString("0.00");
		lblVal.CustomMinimumSize = new Vector2(40, 0);
		lblVal.HorizontalAlignment = HorizontalAlignment.Right;
		lblVal.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblVal);

		slider.ValueChanged += (double val) =>
		{
			float fVal = (float)val;
			lblVal.Text = fVal.ToString("0.00");
			onChanged(fVal);
		};

		parent.AddChild(row);
		return slider;
	}

	private HBoxContainer CreateToggleRow(VBoxContainer parent, string labelText, bool initialVal, Action<bool> onChanged)
	{
		var row = new HBoxContainer();
		var chk = new CheckBox();
		chk.Text = labelText;
		chk.ButtonPressed = initialVal;
		chk.AddThemeFontSizeOverride("font_size", 11);
		chk.Toggled += (bool pressed) => onChanged(pressed);
		row.AddChild(chk);
		parent.AddChild(row);
		return row;
	}

	private Label CreateSectionHeader(VBoxContainer parent, string titleText)
	{
		var lbl = new Label();
		lbl.Text = titleText;
		lbl.AddThemeFontSizeOverride("font_size", 11);
		lbl.Modulate = new Color(0.95f, 0.85f, 0.35f);
		parent.AddChild(lbl);
		return lbl;
	}
}
