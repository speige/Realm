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

	public static float SavedBrushRadius = 4f;
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
	private VBoxContainer _containerDecalSettings;
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

	private Button _btnSoldier;
	private Button _btnArcher;
	private Button _btnCastle;
	private Button _btnTower;
	private CheckBox _chkSpawnAsEnemy;

	private Button _btnTree;
	private Button _btnPropRock;
	private Button _btnGoldMine;
	private Button _btnPillar;
	private Button _btnFlag;

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

	private Button _btnToggleRotate;
	private Button _btnToggleScale;
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
	private Button _btnBigSave;
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
	private Button _btnDecalTool;
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
	private PanelContainer _panelPathing;
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkBuildable;
	private OptionButton _optPathingMode;
	private HBoxContainer _pathingModeHBox;

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

	public const string TempWorkspaceGodotPath = "user://temp_map_workspace";

	private string _tempWorkspacePath;
	private long _lastTerrainSyncTime = 0;
	private long _lastMetadataSyncTime = 0;
	private bool _isSyncing = false;

	public override void _ExitTree()
	{
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
		Instance = this;

		_camera3D = (GameHost.Instance?.MainCamera);

		_highlightStyle = new StyleBoxFlat();
		_highlightStyle.BgColor = new Color(0, 0, 0, 0);
		_highlightStyle.BorderColor = UIStyle.ColorCyanGlow;
		_highlightStyle.SetBorderWidthAll(3);
		_highlightStyle.CornerRadiusTopLeft = 4;
		_highlightStyle.CornerRadiusTopRight = 4;
		_highlightStyle.CornerRadiusBottomLeft = 4;
		_highlightStyle.CornerRadiusBottomRight = 4;

		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_topToolbar = GetNode<HBoxContainer>("TopToolbar");
		_middleRightBox = GetNode<VBoxContainer>("MiddleRightBox");
		_panelTextures = GetNode<PanelContainer>("PanelTextures");
		_panelEntityPalette = GetNode<PanelContainer>("PanelEntityPalette");
		_panelTerrain = GetNode<PanelContainer>("TopToolbar/PanelTerrain");
		_panelDeco = GetNode<PanelContainer>("TopToolbar/PanelDeco");
		_panelEnv = GetNodeOrNull<PanelContainer>("TopToolbar/PanelEnv");

		for (int i = 1; i <= 12; i++)
		{
			_swatchButtons[i - 1] = GetNode<Button>($"PanelTextures/VBox/Content/GridSwatches/Swatch{i}");
		}
		SetupTextureSwatches(true);

		_btnBackToHub = GetNode<Button>("TopLeftBox/BtnBack");
		_btnPublish = GetNode<Button>("MiddleRightBox/BtnPublish");
		_btnSave = GetNode<Button>("MiddleRightBox/BtnSave");
		_btnUndo = GetNode<Button>("MiddleRightBox/HistoryHBox/BtnUndo");
		_btnRedo = GetNode<Button>("MiddleRightBox/HistoryHBox/BtnRedo");
		UIStyle.ApplyButtonText(_btnUndo, "↩️ UNDO", 13);
		UIStyle.ApplyButtonText(_btnRedo, "↪️ REDO", 13);
		
		_btnDeleteObject = GetNode<Button>("RightPillar/VBox/BtnDeleteObject");
		SetupButton(_btnDeleteObject, "❌ Erase Object", () => TriggerToolSelection(GameHost.EditorTool.DeleteObject, _btnDeleteObject), 11, "Delete units or props");
		_statusLabel = GetNode<Label>("TopBar/HBox/StatusLabel");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");

		_btnZoomIn = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnZoomIn");
		_btnZoomIn.Text = TranslationServer.Translate("🔍 Zoom In");
		_btnZoomIn.Set("icon_max_width", 0);
		_btnZoomIn.TooltipText = "Zoom Camera In";
		_btnZoomIn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.ZoomIn();
		};

		_btnZoomOut = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnZoomOut");
		_btnZoomOut.Text = TranslationServer.Translate("🔍 Zoom Out");
		_btnZoomOut.Set("icon_max_width", 0);
		_btnZoomOut.TooltipText = "Zoom Camera Out";
		_btnZoomOut.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.ZoomOut();
		};

		_btnCenter = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnCenter");
		_btnCenter.Text = "🎯 Locate Object";
		_btnCenter.TooltipText = "Center camera on selected object";
		_btnCenter.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			if (camera != null)
			{
				Node3D target = null;
				if (GameHost.Instance != null)
				{
					if (GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject) && GameHost.Instance.SelectedEditorObject is Node3D node3D)
					{
						target = node3D;
					}
					else if (GameHost.Instance.SelectedUnits.Count > 0 && GodotObject.IsInstanceValid(GameHost.Instance.SelectedUnits[0]))
					{
						target = GameHost.Instance.SelectedUnits[0];
					}
				}
				if (target != null)
				{
					camera.FollowTarget = target;
					camera.Position = new Vector3(target.Position.X, camera.Position.Y, target.Position.Z + 25.0f);
				}
			}
		};

		_btnRotate = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnRotate");
		SetupButton(_btnRotate, "🔄 Rotate", () =>
		{
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.Rotate90Degrees();
		}, 13, "Rotate camera 90 degrees");

		_btnCameraAngle = new Button();
		_btnCameraAngle.Name = "BtnCameraAngle";
		_btnCameraAngle.Set("icon_max_width", 0);
		GetNode<GridContainer>("MiddleRightBox/PanelZoom/VBox/Content").AddChild(_btnCameraAngle);
		SetupButton(_btnCameraAngle, "📐 Tilt", () =>
		{
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.ToggleTopDown();
		}, 11, "Toggle top-down vs perspective angle (C)");

		_sldBrushSize = GetNode<Slider>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox/SldBrushSize");
		_lblBrushSizeValue = GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox/Header/LblBrushSizeValue");
		_sldBrushStrength = GetNode<Slider>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox/SldBrushStrength");
		_lblBrushStrengthValue = GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox/Header/LblBrushStrengthValue");
		_btnRaise = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnRaise");
		SetupButton(_btnRaise, "⛰️ Raise", () => TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise), 11, "Elevate terrain height (1)");

		_btnLower = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnLower");
		SetupButton(_btnLower, "🕳️ Lower", () => TriggerToolSelection(GameHost.EditorTool.Lower, _btnLower), 11, "Lower terrain height (2)");

		_btnSmooth = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnSmooth");
		SetupButton(_btnSmooth, "✨ Smooth", () => TriggerToolSelection(GameHost.EditorTool.Smooth, _btnSmooth), 11, "Smooth terrain height (3)");

		_btnPlateau = new Button();
		_btnPlateau.Name = "BtnPlateau";
		_btnPlateau.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelTerrain/VBox/Content").AddChild(_btnPlateau);
		SetupButton(_btnPlateau, "🥞 Plateau", () => TriggerToolSelection(GameHost.EditorTool.Plateau, _btnPlateau), 11, "Flatten terrain to cursor height on click (5)");


		_btnRamp = new Button();
		_btnRamp.Name = "BtnRamp";
		_btnRamp.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelTerrain/VBox/Content").AddChild(_btnRamp);
		SetupButton(_btnRamp, "📐 Ramp", () => TriggerToolSelection(GameHost.EditorTool.Ramp, _btnRamp), 11, "Create ramp between two points (6)");

		_btnTextureBrush = GetNode<Button>("TopToolbar/PanelDeco/VBox/Content/BtnTextureBrush");
		SetupButton(_btnTextureBrush, "🎨 Paint", () => TriggerToolSelection(GameHost.EditorTool.PaintGrass, _btnTextureBrush), 11, "Paint terrain texture (8)");

		_btnDecalTool = GetNodeOrNull<Button>("TopToolbar/PanelDeco/VBox/Content/BtnDecalTool");
		if (_btnDecalTool != null) _btnDecalTool.Visible = false;
		_btnFloodFill = new Button();
		_btnFloodFill.Name = "BtnFloodFill";
		_btnFloodFill.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnFloodFill);
		SetupButton(_btnFloodFill, "🪣 Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFill, _btnFloodFill), 11, "Flood fill terrain texture");

		_btnSelectArea = new Button();
		_btnSelectArea.Name = "BtnSelectArea";
		_btnSelectArea.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnSelectArea);
		SetupButton(_btnSelectArea, "🔲 Select Area", () => TriggerToolSelection(GameHost.EditorTool.SelectArea, _btnSelectArea), 11, "Select rectangular area");

		_btnSkybox = new Button();
		_btnSkybox.Name = "BtnSkybox";
		_btnSkybox.Set("icon_max_width", 0);
		SetupButton(_btnSkybox, "☀️ Cycle Lighting", () => GameHost.Instance?.CycleTimeOfDay(), 11, "Cycle map environment lighting (L)");

		_optSkybox = new OptionButton();
		_optSkybox.Name = "OptSkybox";
		_optSkybox.CustomMinimumSize = new Vector2(160, 30);

		_skyboxFiles.Clear();
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
					}
					fileName = dir.GetNext();
				}
			}
		}

		_skyboxFiles.Sort();

		foreach (var file in _skyboxFiles)
		{
			string cleanName = System.IO.Path.GetFileNameWithoutExtension(file).Replace("_", " ");
			cleanName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);
			_optSkybox.AddItem(TranslationServer.Translate(cleanName));
		}

		_optSkybox.ItemSelected += (index) =>
		{
			if (index >= 0 && index < _skyboxFiles.Count && GameHost.Instance != null)
			{
				string selectedFile = _skyboxFiles[(int)index];
				string path = $"res://Assets/Skyboxes/{selectedFile}";
				GameHost.Instance.SetSkyboxTexture(path);
				ShowFeedback(string.Format(TranslationServer.Translate("Skybox environment set to: {0}"), _optSkybox.GetItemText((int)index)));
			}
		};

		_btnSoldier = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/UnitsGrid/BtnSoldier");
		_btnArcher = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/UnitsGrid/BtnArcher");
		_btnCastle = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/UnitsGrid/BtnCastle");
		_btnTower = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/UnitsGrid/BtnTower");
		_btnTree = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/PropsGrid/BtnTree");
		_btnPropRock = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/PropsGrid/BtnPropRock");
		_btnGoldMine = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/PropsGrid/BtnGoldMine");
		_btnPillar = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/PropsGrid/BtnPillar");
		_btnFlag = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/PropsGrid/BtnFlag");

		var unitsGrid = GetNodeOrNull<GridContainer>("PanelEntityPalette/VBox/Content/PalettesVBox/UnitsGrid");
		if (unitsGrid != null) unitsGrid.Visible = false;
		var propsGrid = GetNodeOrNull<GridContainer>("PanelEntityPalette/VBox/Content/PalettesVBox/PropsGrid");
		if (propsGrid != null) propsGrid.Visible = false;

		_chkSpawnAsEnemy = GetNode<CheckBox>("PanelEntityPalette/VBox/Content/RightSettingsVBox/ChkSpawnAsEnemy");
		_chkSpawnAsEnemy.Visible = false;
		_chkSpawnAsEnemy.ButtonPressed = false;

		var palettesVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/PalettesVBox");
		foreach (Node child in palettesVBox.GetChildren())
		{
			if (child is Label lbl && (lbl.Text.Contains("PALETTE") || lbl.Text.Contains("UNITS") || lbl.Text.Contains("PROPS")))
			{
				lbl.Visible = false;
			}
		}

		_btnSelectMove = new Button();
		_btnSelectMove.Name = "BtnSelectMove";
		_btnSelectMove.Set("icon_max_width", 0);
		SetupButton(_btnSelectMove, "🖱️ SELECT / MOVE", () => TriggerToolSelection(GameHost.EditorTool.SelectMove, _btnSelectMove), 13, "Select, drag-move, rotate, scale, or delete objects (Q)");

		_btnAddObject = new Button();
		_btnAddObject.Name = "BtnAddObject";
		_btnAddObject.Set("icon_max_width", 0);
		SetupButton(_btnAddObject, "➕ ADD OBJECT", () => _entityPaletteController?.TriggerAddObjectMode(), 13, "Place selected unit or prop (9)");

		_entityPaletteController = new MapEditorEntityPaletteController(this, palettesVBox, _btnAddObject);
		_generationDialog = new MapEditorGenerationDialog(this);

		_btnToggleRotate = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnToggleRotate");
		_btnToggleScale = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnToggleScale");
		_btnToggleRotate.Visible = false;
		_btnToggleScale.Visible = false;

		_placementRotateBox = new VBoxContainer();
		_placementRotateBox.Name = "PlacementRotateBox";
		var rotateHeader = new HBoxContainer();
		_placementRotateBox.AddChild(rotateHeader);
		var lblRotateTitle = new Label();
		lblRotateTitle.Text = TranslationServer.Translate("Rotation");
		lblRotateTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblRotateTitle.AddThemeFontSizeOverride("font_size", 12);
		rotateHeader.AddChild(lblRotateTitle);
		_lblPlacementRotateValue = new Label();
		_lblPlacementRotateValue.Text = "0°";
		_lblPlacementRotateValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblPlacementRotateValue.HorizontalAlignment = HorizontalAlignment.Right;
		_lblPlacementRotateValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblPlacementRotateValue.AddThemeFontSizeOverride("font_size", 12);
		rotateHeader.AddChild(_lblPlacementRotateValue);
		_sldPlacementRotate = new HSlider();
		_sldPlacementRotate.Name = "SldPlacementRotate";
		_sldPlacementRotate.MinValue = 0.0;
		_sldPlacementRotate.MaxValue = 360.0;
		_sldPlacementRotate.Step = 5.0;
		_sldPlacementRotate.Value = 0.0;
		_placementRotateBox.AddChild(_sldPlacementRotate);
		_sldPlacementRotate.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementRotate.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_placementScaleBox = new VBoxContainer();
		_placementScaleBox.Name = "PlacementScaleBox";
		var scaleHeader = new HBoxContainer();
		_placementScaleBox.AddChild(scaleHeader);
		var lblScaleTitle = new Label();
		lblScaleTitle.Text = TranslationServer.Translate("Scale");
		lblScaleTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblScaleTitle.AddThemeFontSizeOverride("font_size", 12);
		scaleHeader.AddChild(lblScaleTitle);
		_lblPlacementScaleValue = new Label();
		_lblPlacementScaleValue.Text = "1.0x";
		_lblPlacementScaleValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblPlacementScaleValue.HorizontalAlignment = HorizontalAlignment.Right;
		_lblPlacementScaleValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblPlacementScaleValue.AddThemeFontSizeOverride("font_size", 12);
		scaleHeader.AddChild(_lblPlacementScaleValue);
		_sldPlacementScale = new HSlider();
		_sldPlacementScale.Name = "SldPlacementScale";
		_sldPlacementScale.MinValue = 0.2;
		_sldPlacementScale.MaxValue = 3.0;
		_sldPlacementScale.Step = 0.1;
		_sldPlacementScale.Value = 1.0;
		_placementScaleBox.AddChild(_sldPlacementScale);
		_sldPlacementScale.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementScale.DragEnded += (valueChanged) => _isDraggingSlider = false;

		var placementRightSettingsVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/RightSettingsVBox");
		placementRightSettingsVBox.AddChild(_placementRotateBox);
		placementRightSettingsVBox.AddChild(_placementScaleBox);
		_btnToggleSnap = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnToggleSnap");
		SetupButton(_btnToggleSnap, "🔲 SNAP TO GRID: OFF", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorSnapToGrid = !GameHost.Instance.EditorSnapToGrid;
				UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
			}
		}, 11, "Toggle snapping objects and placements to the grid");
		_btnBigSave = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnBigSave");

		_lblInfoText = GetNode<Label>("RightPillar/VBox/LblInfoText");

		var clumpPalettesVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/PalettesVBox");
		var lblClumpTitle = new Label();
		lblClumpTitle.Name = "LblClumpTitle";
		lblClumpTitle.Text = TranslationServer.Translate("PROP CLUMPING TOOL");
		lblClumpTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblClumpTitle.AddThemeFontSizeOverride("font_size", 11);
		clumpPalettesVBox.AddChild(lblClumpTitle);

		_btnClumpBrush = new Button();
		_btnClumpBrush.Name = "BtnClumpBrush";
		_btnClumpBrush.Set("icon_max_width", 0);
		clumpPalettesVBox.AddChild(_btnClumpBrush);
		SetupButton(_btnClumpBrush, "🌲 CLUMP BRUSH", () => TriggerToolSelection(GameHost.EditorTool.PlacePropClump, _btnClumpBrush, "tree"), 13, "Paint multiple props continuously inside the brush area");

		var clumpRightSettingsVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/RightSettingsVBox");
		_densityBox = new VBoxContainer();
		_densityBox.Name = "DensityBox";
		_densityBox.Visible = false;
		clumpRightSettingsVBox.AddChild(_densityBox);

		var densityHeader = new HBoxContainer();
		_densityBox.AddChild(densityHeader);

		var lblDensityTitle = new Label();
		lblDensityTitle.Text = TranslationServer.Translate("Clump Density");
		lblDensityTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblDensityTitle.AddThemeFontSizeOverride("font_size", 12);
		densityHeader.AddChild(lblDensityTitle);

		_lblClumpDensityValue = new Label();
		_lblClumpDensityValue.Text = "5.0";
		_lblClumpDensityValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblClumpDensityValue.HorizontalAlignment = HorizontalAlignment.Right;
		_lblClumpDensityValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblClumpDensityValue.AddThemeFontSizeOverride("font_size", 12);
		densityHeader.AddChild(_lblClumpDensityValue);

		_sldClumpDensity = new HSlider();
		_sldClumpDensity.Name = "SldClumpDensity";
		_sldClumpDensity.MinValue = 1.0;
		_sldClumpDensity.MaxValue = 20.0;
		_sldClumpDensity.Step = 1.0;
		_sldClumpDensity.Value = 5.0;
		_densityBox.AddChild(_sldClumpDensity);
		_sldClumpDensity.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblClumpDensityValue.Text = fVal.ToString("F0");
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorClumpDensity = fVal;
			}
		};
		_sldClumpDensity.DragStarted += () => _isDraggingSlider = true;
		_sldClumpDensity.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_scaleVarBox = new VBoxContainer();
		_scaleVarBox.Name = "ScaleVarBox";
		_scaleVarBox.Visible = false;
		clumpRightSettingsVBox.AddChild(_scaleVarBox);

		var scaleVarHeader = new HBoxContainer();
		_scaleVarBox.AddChild(scaleVarHeader);

		var lblScaleVarTitle = new Label();
		lblScaleVarTitle.Text = TranslationServer.Translate("Clump Scale Var");
		lblScaleVarTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblScaleVarTitle.AddThemeFontSizeOverride("font_size", 12);
		scaleVarHeader.AddChild(lblScaleVarTitle);

		_lblClumpScaleVarValue = new Label();
		_lblClumpScaleVarValue.Text = "0.3";
		_lblClumpScaleVarValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblClumpScaleVarValue.HorizontalAlignment = HorizontalAlignment.Right;
		_lblClumpScaleVarValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblClumpScaleVarValue.AddThemeFontSizeOverride("font_size", 12);
		scaleVarHeader.AddChild(_lblClumpScaleVarValue);

		_sldClumpScaleVar = new HSlider();
		_sldClumpScaleVar.Name = "SldClumpScaleVar";
		_sldClumpScaleVar.MinValue = 0.0;
		_sldClumpScaleVar.MaxValue = 1.0;
		_sldClumpScaleVar.Step = 0.05;
		_sldClumpScaleVar.Value = 0.3;
		_scaleVarBox.AddChild(_sldClumpScaleVar);
		_sldClumpScaleVar.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblClumpScaleVarValue.Text = fVal.ToString("F2");
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorClumpScaleVar = fVal;
			}
		};
		_sldClumpScaleVar.DragStarted += () => _isDraggingSlider = true;
		_sldClumpScaleVar.DragEnded += (valueChanged) => _isDraggingSlider = false;

		ApplyThemeStyles();
		SetupMenuHooks();

		_panelTextures.OffsetTop = -320;
		_panelEntityPalette.OffsetTop = -320;

		_btnResetMap = new Button();
		_btnResetMap.Name = "BtnResetMap";
		_btnResetMap.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnResetMap);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnResetMap, _btnSave.GetIndex() + 1);
		SetupButton(_btnResetMap, "🧹 RESET TO BLANK MAP", () =>
		{
			ShowConfirmationDialog(
				"Are you sure you want to clear the entire map? This will delete all placed entities and reset terrain heights.",
				() => GameHost.Instance?.ClearMapEntirely()
			);
		}, 13, "Clear all terrain heights, colors, and placed entities");

		_btnGenerateMap = new Button();
		_btnGenerateMap.Name = "BtnGenerateMap";
		_btnGenerateMap.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnGenerateMap);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnGenerateMap, _btnResetMap.GetIndex() + 1);
		SetupButton(_btnGenerateMap, "🎲 RANDOM GEN", () => _generationDialog.Show(), 13, "Open random terrain generator settings modal");

		_btnImportMinimap = new Button();
		_btnImportMinimap.Name = "BtnImportMinimap";
		_btnImportMinimap.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnImportMinimap);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnImportMinimap, _btnGenerateMap.GetIndex() + 1);
		SetupButton(_btnImportMinimap, "🗺️ GEN FROM IMAGE", () => ImportTerrainFromMinimapDialog(), 13, "Import terrain elevations, textures, and trees from a minimap image file");

		var btnHelp = new Button();
		btnHelp.Name = "BtnHelp";
		btnHelp.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopLeftBox").AddChild(btnHelp);
		SetupButton(btnHelp, "❓ HELP / HOTKEYS", () => ToggleHelpPanelExternal(), 13, "Toggle the hotkeys and editor guide overlay (H)");

		_btnCopy = new Button();
		_btnCopy.Name = "BtnCopy";
		_btnCopy.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopLeftBox").AddChild(_btnCopy);
		SetupButton(_btnCopy, "📋 COPY", () => GameHost.Instance?.TriggerCopyFromUI(), 13, "Copy selected object or selected area (Ctrl+C)");

		_btnPaste = new Button();
		_btnPaste.Name = "BtnPaste";
		_btnPaste.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopLeftBox").AddChild(_btnPaste);
		SetupButton(_btnPaste, "📋 PASTE", () => GameHost.Instance?.TriggerPasteFromUI(), 13, "Paste copied object or area (Ctrl+V)");

		InitializeTempWorkspace();

		if (OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.Initialize(this);

			_btnVSCode = new Button();
			_btnVSCode.Name = "BtnVSCode";
			_btnVSCode.Set("icon_max_width", 0);
			GetNode<HBoxContainer>("TopLeftBox").AddChild(_btnVSCode);
			SetupButton(_btnVSCode, "💻 CODE & DATA", null, 13, "Toggle the embedded VSCode editor");
		}

		_btnLoad = new Button();
		_btnLoad.Name = "BtnLoad";
		_btnLoad.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnLoad);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnLoad, _btnSave.GetIndex() + 1);
		SetupButton(_btnLoad, "📂 LOAD", () => LoadMapAction(), 13, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

		_btnTestMap = new Button();
		_btnTestMap.Name = "BtnTestMap";
		_btnTestMap.Set("icon_max_width", 0);
		SetupButton(_btnTestMap, "🎮 TEST", () => TestMapAction(), 13, "Launch single-player mode on the current editor map");

		_btnToggleGrid = new Button();
		_btnToggleGrid.Name = "BtnToggleGrid";
		_btnToggleGrid.Set("icon_max_width", 0);
		var rightSettingsVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/RightSettingsVBox");
		rightSettingsVBox.AddChild(_btnToggleGrid);
		var chkIndex = GetNode<CheckBox>("PanelEntityPalette/VBox/Content/RightSettingsVBox/ChkSpawnAsEnemy").GetIndex();
		rightSettingsVBox.MoveChild(_btnToggleGrid, chkIndex);
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

		_btnMirrorMode = new Button();
		_btnMirrorMode.Name = "BtnMirrorMode";
		_btnMirrorMode.Set("icon_max_width", 0);
		rightSettingsVBox.AddChild(_btnMirrorMode);
		rightSettingsVBox.MoveChild(_btnMirrorMode, chkIndex + 1);
		SetupButton(_btnMirrorMode, "🪞 MIRROR: NONE", () => CycleMirrorMode(), 10, "Cycle terrain and object mirroring symmetry mode");

		_chkRandomRotation = new CheckBox();
		_chkRandomRotation.Name = "ChkRandomRotation";
		_chkRandomRotation.Text = TranslationServer.Translate("Random Rotation");
		_chkRandomRotation.TooltipText = "Randomize the placement rotation for units and props";
		UIStyle.ApplyCheckboxStyle(_chkRandomRotation);
		rightSettingsVBox.AddChild(_chkRandomRotation);
		rightSettingsVBox.MoveChild(_chkRandomRotation, chkIndex + 1);

		_chkRandomScale = new CheckBox();
		_chkRandomScale.Name = "ChkRandomScale";
		_chkRandomScale.Text = TranslationServer.Translate("Random Scale");
		_chkRandomScale.TooltipText = "Randomize the placement scale for props";
		UIStyle.ApplyCheckboxStyle(_chkRandomScale);
		rightSettingsVBox.AddChild(_chkRandomScale);
		rightSettingsVBox.MoveChild(_chkRandomScale, chkIndex + 1);

		_chkClumpMode = new CheckBox();
		_chkClumpMode.Name = "ChkClumpMode";
		_chkClumpMode.Text = TranslationServer.Translate("Clump Brush Mode");
		_chkClumpMode.TooltipText = "Paint multiple objects continuously inside the brush area";
		UIStyle.ApplyCheckboxStyle(_chkClumpMode);
		rightSettingsVBox.AddChild(_chkClumpMode);
		rightSettingsVBox.MoveChild(_chkClumpMode, chkIndex + 1);
		_chkClumpMode.ButtonPressed = false;
		_chkClumpMode.Toggled += (toggled) =>
		{
			if (_densityBox != null) _densityBox.Visible = toggled;
			if (_scaleVarBox != null) _scaleVarBox.Visible = toggled;
		};

		_btnToggleCameraBounds = new Button();
		_btnToggleCameraBounds.Name = "BtnToggleCameraBounds";
		_btnToggleCameraBounds.Set("icon_max_width", 0);
		rightSettingsVBox.AddChild(_btnToggleCameraBounds);
		rightSettingsVBox.MoveChild(_btnToggleCameraBounds, chkIndex + 1);
		SetupButton(_btnToggleCameraBounds, "📹 CAM BOUNDS: OFF", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorCameraBoundsVisible = !GameHost.Instance.EditorCameraBoundsVisible;
				GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
				UpdateCameraBoundsOverlayExternal(GameHost.Instance.EditorCameraBoundsVisible);
			}
		}, 10, "Toggle rendering of the camera bounds overlay");

		_camBoundsBox = new VBoxContainer();
		_camBoundsBox.Name = "CamBoundsBox";
		rightSettingsVBox.AddChild(_camBoundsBox);
		rightSettingsVBox.MoveChild(_camBoundsBox, chkIndex + 2);

		var lblCamBoundsTitle = new Label();
		lblCamBoundsTitle.Text = TranslationServer.Translate("ADJUST CAMERA BOUNDS");
		lblCamBoundsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblCamBoundsTitle.AddThemeFontSizeOverride("font_size", 11);
		_camBoundsBox.AddChild(lblCamBoundsTitle);

		var adjustGrid = new GridContainer();
		adjustGrid.Columns = 3;
		adjustGrid.AddThemeConstantOverride("h_separation", 6);
		adjustGrid.AddThemeConstantOverride("v_separation", 4);
		_camBoundsBox.AddChild(adjustGrid);

		_lblCamLeftVal = new Label();
		_lblCamLeftVal.AddThemeFontSizeOverride("font_size", 11);
		_lblCamLeftVal.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		adjustGrid.AddChild(_lblCamLeftVal);

		var btnLeftDec = new Button();
		btnLeftDec.Set("icon_max_width", 0);
		SetupButton(btnLeftDec, "⬅️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsLeft -= 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Left boundary further left (West)");
		adjustGrid.AddChild(btnLeftDec);

		var btnLeftInc = new Button();
		btnLeftInc.Set("icon_max_width", 0);
		SetupButton(btnLeftInc, "➡️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsLeft += 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Left boundary further right (East)");
		adjustGrid.AddChild(btnLeftInc);

		_lblCamRightVal = new Label();
		_lblCamRightVal.AddThemeFontSizeOverride("font_size", 11);
		_lblCamRightVal.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		adjustGrid.AddChild(_lblCamRightVal);

		var btnRightDec = new Button();
		btnRightDec.Set("icon_max_width", 0);
		SetupButton(btnRightDec, "⬅️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsRight -= 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Right boundary further left (West)");
		adjustGrid.AddChild(btnRightDec);

		var btnRightInc = new Button();
		btnRightInc.Set("icon_max_width", 0);
		SetupButton(btnRightInc, "➡️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsRight += 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Right boundary further right (East)");
		adjustGrid.AddChild(btnRightInc);

		_lblCamTopVal = new Label();
		_lblCamTopVal.AddThemeFontSizeOverride("font_size", 11);
		_lblCamTopVal.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		adjustGrid.AddChild(_lblCamTopVal);

		var btnTopDec = new Button();
		btnTopDec.Set("icon_max_width", 0);
		SetupButton(btnTopDec, "⬆️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsTop -= 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Top boundary further North (Up)");
		adjustGrid.AddChild(btnTopDec);

		var btnTopInc = new Button();
		btnTopInc.Set("icon_max_width", 0);
		SetupButton(btnTopInc, "⬇️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsTop += 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Top boundary further South (Down)");
		adjustGrid.AddChild(btnTopInc);

		_lblCamBottomVal = new Label();
		_lblCamBottomVal.AddThemeFontSizeOverride("font_size", 11);
		_lblCamBottomVal.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		adjustGrid.AddChild(_lblCamBottomVal);

		var btnBottomDec = new Button();
		btnBottomDec.Set("icon_max_width", 0);
		SetupButton(btnBottomDec, "⬆️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsBottom -= 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Bottom boundary further North (Up)");
		adjustGrid.AddChild(btnBottomDec);

		var btnBottomInc = new Button();
		btnBottomInc.Set("icon_max_width", 0);
		SetupButton(btnBottomInc, "⬇️", () => {
			if (GameHost.Instance != null) {
				EnsureCameraBoundsVisible();
				GameHost.Instance.EditorCameraBoundsBottom += 5.0f;
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		}, 10, "Move Bottom boundary further South (Down)");
		adjustGrid.AddChild(btnBottomInc);


		UpdateCameraBoundsUI();

		var settingsVBox = GetNode<VBoxContainer>("PanelTextures/VBox/Content/SettingsVBox");
		_lblTerrainTexture = new Label();
		_lblTerrainTexture.Name = "LblTerrainTexture";
		_lblTerrainTexture.AddThemeFontSizeOverride("font_size", 12);
		_lblTerrainTexture.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		settingsVBox.AddChild(_lblTerrainTexture);
		settingsVBox.MoveChild(_lblTerrainTexture, 0);

		_lblCliffTexture = new Label();
		_lblCliffTexture.Name = "LblCliffTexture";
		_lblCliffTexture.AddThemeFontSizeOverride("font_size", 12);
		_lblCliffTexture.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		settingsVBox.AddChild(_lblCliffTexture);
		settingsVBox.MoveChild(_lblCliffTexture, 1);

		var brushActionsHBox = new HBoxContainer();
		brushActionsHBox.Name = "BrushActionsHBox";
		brushActionsHBox.AddThemeConstantOverride("separation", 8);
		settingsVBox.AddChild(brushActionsHBox);

		_chkBlockMode = new CheckBox();
		_chkBlockMode.Name = "ChkBlockMode";
		_chkBlockMode.Text = TranslationServer.Translate("Block Mode (M)");
		_chkBlockMode.TooltipText = "Toggle blocky terrain sculpting & automatic steep cliff coloring";
		UIStyle.ApplyCheckboxStyle(_chkBlockMode);
		settingsVBox.AddChild(_chkBlockMode);

		_stepBox = new VBoxContainer();
		_stepBox.Name = "StepBox";
		settingsVBox.AddChild(_stepBox);

		var stepHeader = new HBoxContainer();
		_stepBox.AddChild(stepHeader);

		var lblStepTitle = new Label();
		lblStepTitle.Text = TranslationServer.Translate("Block Level Height");
		lblStepTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblStepTitle.AddThemeFontSizeOverride("font_size", 12);
		stepHeader.AddChild(lblStepTitle);

		_lblBlockStepValue = new Label();
		_lblBlockStepValue.Text = "4.0 m";
		_lblBlockStepValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblBlockStepValue.HorizontalAlignment = HorizontalAlignment.Right;
		_lblBlockStepValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblBlockStepValue.AddThemeFontSizeOverride("font_size", 12);
		stepHeader.AddChild(_lblBlockStepValue);

		_sldBlockStep = new HSlider();
		_sldBlockStep.Name = "SldBlockStep";
		_sldBlockStep.MinValue = 1.0;
		_sldBlockStep.MaxValue = 10.0;
		_sldBlockStep.Step = 0.5;
		_sldBlockStep.Value = 4.0;
		_stepBox.AddChild(_sldBlockStep);
		_sldBlockStep.DragStarted += () => _isDraggingSlider = true;
		_sldBlockStep.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_chkBlockMode.Toggled += (toggled) =>
		{
			ShowFeedback(toggled ? "Block Mode: Enabled" : "Block Mode: Disabled");
			UpdateBlockStepVisibility();
			UpdateBrushStrengthVisibility();
		};
		_chkBlockMode.ButtonPressed = true;

		var divider = new HSeparator();
		settingsVBox.AddChild(divider);

		_waterHeightBox = new VBoxContainer();
		_waterHeightBox.Name = "WaterHeightBox";
		settingsVBox.AddChild(_waterHeightBox);

		var waterHeader = new HBoxContainer();
		_waterHeightBox.AddChild(waterHeader);

		var lblWaterTitle = new Label();
		lblWaterTitle.Text = TranslationServer.Translate("Water Height Level");
		lblWaterTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblWaterTitle.AddThemeFontSizeOverride("font_size", 12);
		waterHeader.AddChild(lblWaterTitle);

		_lblWaterHeightValue = new Label();
		_lblWaterHeightValue.Text = "-2.0 m";
		_lblWaterHeightValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblWaterHeightValue.HorizontalAlignment = HorizontalAlignment.Right;
		_lblWaterHeightValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblWaterHeightValue.AddThemeFontSizeOverride("font_size", 12);
		waterHeader.AddChild(_lblWaterHeightValue);

		_sldWaterHeight = new HSlider();
		_sldWaterHeight.Name = "SldWaterHeight";
		_sldWaterHeight.MinValue = -10.0;
		_sldWaterHeight.MaxValue = 40.0;
		_sldWaterHeight.Step = 0.5;
		_sldWaterHeight.Value = -2.0;
		_waterHeightBox.AddChild(_sldWaterHeight);
		_sldWaterHeight.DragStarted += () => _isDraggingSlider = true;
		_sldWaterHeight.DragEnded += (valueChanged) => _isDraggingSlider = false;

		var pasteDivider = new HSeparator();
		settingsVBox.AddChild(pasteDivider);

		var pasteOptionsBox = new VBoxContainer();
		pasteOptionsBox.Name = "PasteOptionsBox";
		settingsVBox.AddChild(pasteOptionsBox);

		var lblPasteOptionsTitle = new Label();
		lblPasteOptionsTitle.Text = TranslationServer.Translate("Affected Layers");
		lblPasteOptionsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblPasteOptionsTitle.AddThemeFontSizeOverride("font_size", 11);
		pasteOptionsBox.AddChild(lblPasteOptionsTitle);

		var chkPasteTextures = new CheckBox();
		chkPasteTextures.Name = "ChkPasteTextures";
		chkPasteTextures.Text = TranslationServer.Translate("Textures");
		chkPasteTextures.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPasteTextures);
		pasteOptionsBox.AddChild(chkPasteTextures);
		chkPasteTextures.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionTextures = toggled;
		};

		var chkPasteHeights = new CheckBox();
		chkPasteHeights.Name = "ChkPasteHeights";
		chkPasteHeights.Text = TranslationServer.Translate("HeightMap");
		chkPasteHeights.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPasteHeights);
		pasteOptionsBox.AddChild(chkPasteHeights);
		chkPasteHeights.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionHeights = toggled;
		};

		var chkPasteEntities = new CheckBox();
		chkPasteEntities.Name = "ChkPasteEntities";
		chkPasteEntities.Text = TranslationServer.Translate("Units / Props");
		chkPasteEntities.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPasteEntities);
		pasteOptionsBox.AddChild(chkPasteEntities);
		chkPasteEntities.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionEntities = toggled;
		};

		var chkPastePathing = new CheckBox();
		chkPastePathing.Name = "ChkPastePathing";
		chkPastePathing.Text = TranslationServer.Translate("Pathing");
		chkPastePathing.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPastePathing);
		pasteOptionsBox.AddChild(chkPastePathing);
		chkPastePathing.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionPathing = toggled;
		};

		var rotBox = new VBoxContainer();
		rotBox.Name = "PasteRotationBox";
		pasteOptionsBox.AddChild(rotBox);

		_lblPasteRotation = new Label();
		_lblPasteRotation.Text = TranslationServer.Translate("Paste Rotation: 0°");
		rotBox.AddChild(_lblPasteRotation);

		_sldPasteRotation = new HSlider();
		_sldPasteRotation.Name = "SldPasteRotation";
		_sldPasteRotation.MinValue = -180;
		_sldPasteRotation.MaxValue = 180;
		_sldPasteRotation.Step = 90;
		_sldPasteRotation.Value = 0;
		_sldPasteRotation.ValueChanged += (val) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorPasteRotation = (float)val;
			_lblPasteRotation.Text = string.Format(TranslationServer.Translate("Paste Rotation: {0}°"), val);
		};
		rotBox.AddChild(_sldPasteRotation);

		_btnBrushShape = new Button();
		_btnBrushShape.Name = "BtnBrushShape";
		_btnBrushShape.Set("icon_max_width", 0);
		_btnBrushShape.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		brushActionsHBox.AddChild(_btnBrushShape);
		SetupButton(_btnBrushShape, "⚪ BRUSH: CIRCLE", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorBrushIsSquare = !GameHost.Instance.EditorBrushIsSquare;
				GameHost.Instance.UpdateBrushMesh();
				UpdateBrushShapeExternal(GameHost.Instance.EditorBrushIsSquare);
			}
		}, 11, "Toggle brush shape between circular and square (B)");

		if (GameHost.Instance != null)
		{
			_btnBrushShape.Text = GameHost.Instance.EditorBrushIsSquare ? "🔳 BRUSH: SQUARE" : "⚪ BRUSH: CIRCLE";
			_btnToggleGrid.Text = GameHost.Instance.EditorGridMode switch
			{
				GameHost.GridOverlayMode.Off => "🌐 GRID OVERLAY: OFF",
				GameHost.GridOverlayMode.Mesh => "🌐 GRID OVERLAY: ON",
				_ => "🌐 GRID OVERLAY: OFF"
			};
			_btnToggleCameraBounds.Text = GameHost.Instance.EditorCameraBoundsVisible ? "📹 CAM BOUNDS: ON" : "📹 CAM BOUNDS: OFF";
		}

		_btnEyedropper = new Button();
		_btnEyedropper.Name = "BtnEyedropper";
		_btnEyedropper.Set("icon_max_width", 0);
		var rightVBox = GetNode<VBoxContainer>("RightPillar/VBox");
		rightVBox.GetParent<Control>().AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		rightVBox.AddChild(_btnEyedropper);
		rightVBox.MoveChild(_btnEyedropper, _btnDeleteObject.GetIndex());
		SetupButton(_btnEyedropper, "🔍 EYEDROPPER", () => TriggerToolSelection(GameHost.EditorTool.Eyedropper, _btnEyedropper), 14, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");
		UIStyle.ApplyButtonText(_btnEyedropper, "🔍 EYEDROPPER", 13);

		_optEyedropperMode = new OptionButton();
		_optEyedropperMode.Name = "OptEyedropperMode";
		_optEyedropperMode.AddItem(TranslationServer.Translate("Auto-Detect Mode"), 0);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick 3D Asset"), 1);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Decal"), 2);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Terrain Texture"), 3);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Height"), 4);
		_optEyedropperMode.Selected = 0;
		_optEyedropperMode.AddThemeFontSizeOverride("font_size", 11);
		_optEyedropperMode.TooltipText = "Select what type of information the Eyedropper tool should sample.";
		rightVBox.AddChild(_optEyedropperMode);
		rightVBox.MoveChild(_optEyedropperMode, _btnEyedropper.GetIndex() + 1);

		_btnNoise = new Button();
		_btnNoise.Name = "BtnNoise";
		_btnNoise.Set("icon_max_width", 0);
		var terrainContent = GetNode<HBoxContainer>("TopToolbar/PanelTerrain/VBox/Content");
		terrainContent.AddChild(_btnNoise);
		SetupButton(_btnNoise, "🎲 Roughen", () => TriggerToolSelection(GameHost.EditorTool.Noise, _btnNoise), 11, "Add random height variations/noise to the terrain under the brush (7)");

		palettesVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/PalettesVBox");
		var lblDecalsTitle = new Label();
		lblDecalsTitle.Name = "LblDecalsTitle";
		lblDecalsTitle.Text = TranslationServer.Translate("PALETTE: DECALS");
		lblDecalsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblDecalsTitle.AddThemeFontSizeOverride("font_size", 11);
		lblDecalsTitle.Visible = false;
		palettesVBox.AddChild(lblDecalsTitle);

		var decalsGrid = new GridContainer();
		decalsGrid.Name = "DecalsGrid";
		decalsGrid.Columns = 5;
		decalsGrid.AddThemeConstantOverride("h_separation", 6);
		decalsGrid.Visible = false;
		palettesVBox.AddChild(decalsGrid);

		var decalFiles = new List<string>();
		using (var dir = DirAccess.Open("res://Assets/2d/Decals"))
		{
			if (dir != null)
			{
				dir.ListDirBegin();
				string fileName = dir.GetNext();
				while (fileName != "")
				{
					if (!dir.CurrentIsDir() && !fileName.EndsWith(".import") && 
						(fileName.EndsWith(".png") || fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg") || fileName.EndsWith(".svg")))
					{
						decalFiles.Add(fileName);
					}
					fileName = dir.GetNext();
				}
			}
		}
		decalFiles.Sort();

		foreach (var dFile in decalFiles)
		{
			var btn = new Button();
			string dId = System.IO.Path.GetFileNameWithoutExtension(dFile);
			btn.Name = $"BtnDecal_{dId}";
			string decalPath = $"res://Assets/2d/Decals/{dFile}";
			string cleanName = dId.Replace("_", " ");
			cleanName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);
			SetupButton(btn, "", () => TriggerToolSelection(GameHost.EditorTool.PlaceDecal, btn, decalPath), 12, cleanName);
			btn.Set("icon_max_width", 32);
			btn.CustomMinimumSize = new Vector2(42, 42);
			decalsGrid.AddChild(btn);
			var tex = GD.Load<Texture2D>(decalPath);
			if (tex != null)
			{
				btn.Icon = tex;
				btn.ExpandIcon = true;
			}
		}

		TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise);

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;

		_btnPathingBrush = new Button();
		_btnPathingBrush.Name = "BtnPathingBrush";
		_btnPathingBrush.Set("icon_max_width", 0);
		SetupButton(_btnPathingBrush, "🧭 Paint", () => TriggerToolSelection(GameHost.EditorTool.PaintPathing, _btnPathingBrush), 11, "Paint pathing attributes onto the terrain map");
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnPathingBrush);

		_btnFloodFillPathing = new Button();
		_btnFloodFillPathing.Name = "BtnFloodFillPathing";
		_btnFloodFillPathing.Set("icon_max_width", 0);
		SetupButton(_btnFloodFillPathing, "🪣 Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFillPathing, _btnFloodFillPathing), 11, "Flood fill pathing attributes onto the terrain map");
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnFloodFillPathing);

		_btnDrawCoordinate = new Button();
		_btnDrawCoordinate.Name = "BtnDrawCoordinate";
		_btnDrawCoordinate.Set("icon_max_width", 0);
		SetupButton(_btnDrawCoordinate, "🗺️ Draw Coordinate", () => TriggerToolSelection(GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate), 11, "Drag to define a named coordinate box exposed as C# variables");
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnDrawCoordinate);

		_panelPathing = new PanelContainer();
		_panelPathing.Name = "PanelPathing";
		_panelPathing.LayoutMode = 1;
		_panelPathing.AnchorsPreset = (int)Control.LayoutPreset.BottomLeft;
		_panelPathing.AnchorTop = 1.0f;
		_panelPathing.AnchorBottom = 1.0f;
		_panelPathing.GrowVertical = Control.GrowDirection.Begin;
		_panelPathing.OffsetLeft = 20.0f;
		_panelPathing.OffsetTop = -380.0f;
		_panelPathing.OffsetRight = 440.0f;
		_panelPathing.OffsetBottom = -20.0f;
		_panelPathing.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		AddChild(_panelPathing);
		_panelPathing.Visible = false;

		var pVBox = new VBoxContainer();
		pVBox.Name = "VBox";
		_panelPathing.AddChild(pVBox);

		var pHeader = new HBoxContainer();
		pHeader.Name = "HeaderHBox";
		pVBox.AddChild(pHeader);

		var pTitle = new Label();
		pTitle.Name = "Title";
		pTitle.Text = "🧭 PATHING PAINTING";
		pTitle.AddThemeFontSizeOverride("font_size", 14);
		pTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		pHeader.AddChild(pTitle);

		var pContent = new VBoxContainer();
		pContent.Name = "Content";
		pVBox.AddChild(pContent);

		_pathingModeHBox = new HBoxContainer();
		var modeLabel = new Label();
		modeLabel.Text = TranslationServer.Translate("Mode: ");
		modeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_pathingModeHBox.AddChild(modeLabel);

		_optPathingMode = new OptionButton();
		_optPathingMode.Name = "OptPathingMode";
		_optPathingMode.AddItem(TranslationServer.Translate("Add Pathing Attribute"), 0);
		_optPathingMode.AddItem(TranslationServer.Translate("Clear Pathing Attribute"), 1);
		_optPathingMode.Selected = 0;
		_optPathingMode.AddThemeFontSizeOverride("font_size", 11);
		_optPathingMode.CustomMinimumSize = new Vector2(180, 28);
		_pathingModeHBox.AddChild(_optPathingMode);
		pContent.AddChild(_pathingModeHBox);

		var layersVBox = new VBoxContainer();
		layersVBox.AddThemeConstantOverride("separation", 6);
		pContent.AddChild(layersVBox);

		_chkShallowWater = new CheckBox();
		_chkShallowWater.Text = TranslationServer.Translate("Shallow Water");
		UIStyle.ApplyCheckboxStyle(_chkShallowWater);
		layersVBox.AddChild(_chkShallowWater);

		_chkDeepWater = new CheckBox();
		_chkDeepWater.Text = TranslationServer.Translate("Deep Water");
		UIStyle.ApplyCheckboxStyle(_chkDeepWater);
		layersVBox.AddChild(_chkDeepWater);

		_chkFlying = new CheckBox();
		_chkFlying.Text = TranslationServer.Translate("Flying");
		UIStyle.ApplyCheckboxStyle(_chkFlying);
		layersVBox.AddChild(_chkFlying);

		_chkGround = new CheckBox();
		_chkGround.Text = TranslationServer.Translate("Ground");
		UIStyle.ApplyCheckboxStyle(_chkGround);
		layersVBox.AddChild(_chkGround);

		_chkBuildable = new CheckBox();
		_chkBuildable.Text = TranslationServer.Translate("Buildable");
		UIStyle.ApplyCheckboxStyle(_chkBuildable);
		layersVBox.AddChild(_chkBuildable);

		_chkGround.ButtonPressed = true;

		_topBarController = new MapEditorTopBar(_btnBackToHub, _btnPublish, _btnSave, _btnLoad, _btnUndo, _btnRedo, _btnVSCode, _statusLabel, _feedbackLabel);
		_brushSettingsController = new MapEditorBrushSettings(_sldBrushSize, _lblBrushSizeValue, _sldBrushStrength, _lblBrushStrengthValue, _chkBlockMode, _sldBlockStep, _lblBlockStepValue, _sldWaterHeight, _lblWaterHeightValue, _chkWaterEnabled);
		_placementSettingsController = new MapEditorPlacementSettings(_sldPlacementRotate, _lblPlacementRotateValue, _sldPlacementScale, _lblPlacementScaleValue, _chkSpawnAsEnemy, _chkRandomRotation, _chkRandomScale, _chkClumpMode, _sldClumpDensity, _lblClumpDensityValue, _sldClumpScaleVar, _lblClumpScaleVarValue);
		InitializeInspectorPanel();
		_inspectorController = new MapEditorInspector(_lblInspectorTitle, _lblInspectorPos, _btnInspectorRotLeft, _btnInspectorRotRight, _btnInspectorScaleDown, _btnInspectorScaleUp, _btnInspectorScaleReset, _btnInspectorDelete);
		_pathingPanelController = new MapEditorPathingPanel(_chkShallowWater, _chkDeepWater, _chkFlying, _chkGround, _chkBuildable, _optPathingMode);

		SetupMinimap();
		RebuildHUDLayout();

		_minimapController = new MapEditorMinimap(_minimapFrame, _minimapArea, _cameraIndicator, this);
		RegenerateMinimap();

		if (GameHost.Instance != null)
		{
			UpdateRotationExternal(GameHost.Instance.EditorPlacementRotation);
			UpdateScaleExternal(GameHost.Instance.EditorPlacementScale);
			UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
		}

		if (!_agreementShownThisSession)
		{
			_agreementShownThisSession = true;
			ShowAgreementModal();
		}
	}

	public override void _Process(double delta)
	{
		_viewModel.UpdateFromHost();

		if (GameHost.Instance != null)
		{
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
		}
		_inspectorController?.Update(_viewModel);
		_pathingPanelController?.Update(_viewModel);
		_minimapController?.Update(_viewModel);
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
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreateHUDPillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreateHUDPillarPanel(false));

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

		GetNode<PanelContainer>("TopToolbar/PanelTerrain").AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		GetNode<PanelContainer>("TopToolbar/PanelDeco").AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		GetNode<PanelContainer>("MiddleRightBox/PanelZoom").AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_panelTextures.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		_panelEntityPalette.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());

		var headers = new string[] {
			"TopToolbar/PanelTerrain/VBox/HeaderHBox/Title",
			"TopToolbar/PanelDeco/VBox/HeaderHBox/Title",
			"TopToolbar/PanelEnv/VBox/HeaderHBox/Title",
			"MiddleRightBox/PanelZoom/VBox/HeaderHBox/Title",
			"PanelTextures/VBox/HeaderHBox/Title",
			"PanelEntityPalette/VBox/HeaderHBox/Title"
		};
		foreach (var path in headers)
		{
			var lbl = GetNodeOrNull<Label>(path);
			if (lbl != null)
			{
				lbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
				lbl.AddThemeFontSizeOverride("font_size", 12);
			}
		}

		UIStyle.ApplyTitle(_feedbackLabel, "", 24);
	}

	public void UpdateSelectedObjectInfo()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			_lblInfoText.Visible = false;
			_inspectorPanel.Visible = true;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = true;
				_btnHeaderInspector.Text = "▼ Selected Object Inspector";
				_contentInspector.Visible = true;
			}
			string nameStr = selected.Name;
			Vector3 pos = (selected as Node3D).Position;
			Vector3 rot = (selected as Node3D).RotationDegrees;
			Vector3 scale = (selected as Node3D).Scale;
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
			
			_viewModel.HasInspectorSelection = true;
			_viewModel.InspectorTitle = $"SELECTED: {nameStr}\n[{typeStr}]";
			_viewModel.InspectorPos = $"Pos: {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}\nRot: {rot.Y:F1}° | Scale: {scale.X:F2}x";
		}
		else
		{
			_lblInfoText.Visible = true;
			_inspectorPanel.Visible = false;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = false;
			}
			_viewModel.HasInspectorSelection = false;
			_viewModel.InspectorTitle = "No Selection";
			_viewModel.InspectorPos = "Position: (0, 0)";
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
		PublishMapAction();
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
		if (_chkSpawnAsEnemy != null)
		{
			_chkSpawnAsEnemy.ButtonPressed = isEnemy;
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

	private void SetupMenuHooks()
	{
		_sldBrushSize.DragStarted += () => _isDraggingSlider = true;
		_sldBrushSize.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_sldBrushStrength.DragStarted += () => _isDraggingSlider = true;
		_sldBrushStrength.DragEnded += (valueChanged) => _isDraggingSlider = false;

		SetupCollapsible("TopToolbar/PanelTerrain", "VBox/HeaderHBox/BtnCollapse", "VBox/Content");
		SetupCollapsible("TopToolbar/PanelDeco", "VBox/HeaderHBox/BtnCollapse", "VBox/Content");
		SetupCollapsible("TopToolbar/PanelEnv", "VBox/HeaderHBox/BtnCollapse", "VBox/Content");
		SetupCollapsible("MiddleRightBox/PanelZoom", "VBox/HeaderHBox/BtnCollapse", "VBox/Content");
		SetupCollapsible("PanelTextures", "VBox/HeaderHBox/BtnCollapse", "VBox/Content");
		SetupCollapsible("PanelEntityPalette", "VBox/HeaderHBox/BtnCollapse", "VBox/Content");
	}

	private void SetupCollapsible(string panelPath, string buttonPath, string contentPath)
	{
		var panel = GetNodeOrNull<Control>(panelPath);
		var btn = GetNodeOrNull<Button>($"{panelPath}/{buttonPath}");
		var content = GetNodeOrNull<Control>($"{panelPath}/{contentPath}");

		if (panel == null || btn == null || content == null) return;

		btn.Pressed += () =>
		{
			content.Visible = !content.Visible;
			btn.Text = content.Visible ? "▲" : "▼";
			UIManager.Instance?.PlayClickSound();
		};
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
		GameHost.Instance.PlaceUnitIsEnemy = _chkSpawnAsEnemy.ButtonPressed;

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
			if (_panelPathing != null) _panelPathing.Visible = true;
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			if (_pathingModeHBox != null)
			{
				_pathingModeHBox.Visible = (tool != GameHost.EditorTool.FloodFillPathing);
			}
			GameHost.Instance?.UpdatePathingOverlay();
		}
		else if (tool == GameHost.EditorTool.DrawCoordinate)
		{
			if (_panelPathing != null) _panelPathing.Visible = false;
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			if (_btnCommitCoordinate != null) _btnCommitCoordinate.Visible = false;
			GameHost.Instance?.UpdatePathingOverlay();
		}
		else
		{
			if (_panelPathing != null) _panelPathing.Visible = false;
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
					string alignment = _chkSpawnAsEnemy.ButtonPressed ? TranslationServer.Translate("Enemy (Orc)") : TranslationServer.Translate("Player (Alliance)");
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
		ClearTempWorkspaceExternal();
		System.IO.Directory.CreateDirectory(_tempWorkspacePath);

		MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "CustomMap");

		_lastTerrainSyncTime = 0;
		_lastMetadataSyncTime = 0;

		var syncTimer = new Godot.Timer();
		syncTimer.WaitTime = 1.0f;
		syncTimer.Autostart = true;
		syncTimer.Timeout += OnSyncTimerTimeout;
		AddChild(syncTimer);
	}

	public void ClearTempWorkspaceExternal()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;

		foreach (var file in System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories))
		{
			var fileAttributes = System.IO.File.GetAttributes(file);
			if ((fileAttributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly)
			{
				System.IO.File.SetAttributes(file, fileAttributes & ~System.IO.FileAttributes.ReadOnly);
			}
			System.IO.File.Delete(file);
		}

		foreach (var directory in System.IO.Directory.GetDirectories(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories))
		{
			var directoryAttributes = System.IO.File.GetAttributes(directory);
			if ((directoryAttributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly)
			{
				System.IO.File.SetAttributes(directory, directoryAttributes & ~System.IO.FileAttributes.ReadOnly);
			}
		}

		foreach (var directory in System.IO.Directory.GetDirectories(_tempWorkspacePath))
		{
			System.IO.Directory.Delete(directory, true);
		}

		_lastTerrainSyncTime = 0;
		_lastMetadataSyncTime = 0;
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
			string targetFile = System.IO.Path.Combine(_tempWorkspacePath, relativePath);
			string targetDir = System.IO.Path.GetDirectoryName(targetFile);
			if (!System.IO.Directory.Exists(targetDir))
			{
				System.IO.Directory.CreateDirectory(targetDir);
			}
			System.IO.File.Copy(file, targetFile, true);
		}
		MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "CustomMap");
	}

	private void CopyTempWorkspaceToFolder(string targetFolder)
	{
		if (!System.IO.Directory.Exists(targetFolder))
		{
			System.IO.Directory.CreateDirectory(targetFolder);
		}
		
		string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		if (GameHost.Instance != null)
		{
			GameHost.Instance.SaveMapToFile(tempTerrainPath);
			GameHost.Instance.EditorHasUnsavedChanges = false;
		}
		_lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);
		
		foreach (var file in System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories))
		{
			string relativePath = file.Substring(_tempWorkspacePath.Length + 1);
			string targetFile = System.IO.Path.Combine(targetFolder, relativePath);
			string targetDir = System.IO.Path.GetDirectoryName(targetFile);
			if (!System.IO.Directory.Exists(targetDir))
			{
				System.IO.Directory.CreateDirectory(targetDir);
			}
			System.IO.File.Copy(file, targetFile, true);
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
					CopyTempWorkspaceToFolder(selectedFolder);
					ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), System.IO.Path.GetFileName(selectedFolder)));
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
			CopyTempWorkspaceToFolder(defaultFolder);
			ShowFeedback(TranslationServer.Translate("Saving map to default location..."));
			var timer = GetTree().CreateTimer(0.8f);
			timer.Timeout += () => ShowFeedback(TranslationServer.Translate("Map saved successfully to user://maps/default_map!"));
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
					_lastUsedFolder = selectedFolder;
					_currentSourceFolder = selectedFolder;
					CopyFolderToTempWorkspace(selectedFolder);
					string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
					bool success = GameHost.Instance.LoadMapFromFile(terrainPath);
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
				}
			})
		);

		if (err != Error.Ok)
		{
			string defaultFolder = ProjectSettings.GlobalizePath("user://maps/default_map");
			if (System.IO.Directory.Exists(defaultFolder))
			{
				_currentSourceFolder = defaultFolder;
				CopyFolderToTempWorkspace(defaultFolder);
			}
			string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
			bool success = GameHost.Instance.LoadMapFromFile(terrainPath);
			if (success)
			{
				_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
				_lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
				ShowFeedback(TranslationServer.Translate("Map loaded from default_map"));
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("No map file found"));
			}
		}
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
			var key = NSec.Cryptography.Key.Create(SignatureAlgorithm.Ed25519);
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
					compileProcess.StartInfo.Arguments = "build -c Release";
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
					
					// Ensure all asset authors are in the contributors list
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
							ReferencedHashes = referencedHashes
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
		if (_leftPillar != null) _leftPillar.Visible = false;
		if (_rightPillar != null) _rightPillar.Visible = false;
		if (_panelTextures != null) _panelTextures.Visible = false;
		if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
		if (_panelPathing != null) _panelPathing.Visible = false;
		if (_middleRightBox != null) _middleRightBox.Visible = false;

		var titleLabel = GetNodeOrNull<Label>("TopBar/HBox/TitleLabel");
		if (titleLabel != null) titleLabel.Visible = false;



		_panelLeft = new Panel();
		_panelLeft.Name = "LeftSlidePanel";
		_panelLeft.CustomMinimumSize = new Vector2(260, 0);
		_panelLeft.LayoutMode = 1;
		_panelLeft.SetAnchorsPreset(LayoutPreset.LeftWide);
		_panelLeft.GrowVertical = GrowDirection.Both;
		_panelLeft.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));
		AddChild(_panelLeft);

		var leftScroll = new ScrollContainer();
		leftScroll.LayoutMode = 1;
		leftScroll.SetAnchorsPreset(LayoutPreset.FullRect);
		leftScroll.GrowHorizontal = GrowDirection.Both;
		leftScroll.GrowVertical = GrowDirection.Both;
		leftScroll.OffsetLeft = 10;
		leftScroll.OffsetRight = -10;
		leftScroll.OffsetTop = 40;
		leftScroll.OffsetBottom = -10;
		_panelLeft.AddChild(leftScroll);

		var leftVBox = new VBoxContainer();
		leftVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		leftVBox.AddThemeConstantOverride("separation", 10);
		leftScroll.AddChild(leftVBox);



		_btnHeaderFile = new Button();
		_btnHeaderFile.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderFile);
		leftVBox.AddChild(_btnHeaderFile);

		_contentFile = new VBoxContainer();
		_contentFile.Visible = false;
		((VBoxContainer)_contentFile).AddThemeConstantOverride("separation", 8);
		leftVBox.AddChild(_contentFile);
		SetupMutualAccordion(_btnHeaderFile, _contentFile, TranslationServer.Translate("File"));

		SafeReparent(_btnLoad, _contentFile);
		SafeReparent(_btnSave, _contentFile);
		SafeReparent(_btnTestMap, _contentFile);
		SafeReparent(_btnPublish, _contentFile);
		SafeReparent(_btnResetMap, _contentFile);
		SafeReparent(_btnGenerateMap, _contentFile);
		SafeReparent(_btnImportMinimap, _contentFile);

		_btnLeftTab = new Button();
		_btnLeftTab.Name = "LeftTabButton";
		_btnLeftTab.Set("icon_max_width", 0);
		_btnLeftTab.CustomMinimumSize = new Vector2(30, 120);
		_btnLeftTab.LayoutMode = 1;
		_btnLeftTab.SetAnchorsPreset(LayoutPreset.CenterRight);
		_btnLeftTab.GrowHorizontal = GrowDirection.Begin;
		_btnLeftTab.GrowVertical = GrowDirection.Both;
		_btnLeftTab.OffsetLeft = 6;
		_btnLeftTab.OffsetRight = 36;
		_btnLeftTab.OffsetTop = -60;
		_btnLeftTab.OffsetBottom = 60;
		_btnLeftTab.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnLeftTab.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnLeftTab.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnLeftTab.AddThemeFontSizeOverride("font_size", 20);
		_btnLeftTab.Pressed += ToggleLeftPanel;
		_panelLeft.AddChild(_btnLeftTab);

		SetLeftPanelExpanded(true);

		_panelRight = new Panel();
		_panelRight.Name = "RightSlidePanel";
		_panelRight.CustomMinimumSize = new Vector2(300, 0);
		_panelRight.LayoutMode = 1;
		_panelRight.SetAnchorsPreset(LayoutPreset.RightWide);
		_panelRight.GrowVertical = GrowDirection.Both;
		_panelRight.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));
		AddChild(_panelRight);

		_btnRightTab = new Button();
		_btnRightTab.Name = "RightTabButton";
		_btnRightTab.Set("icon_max_width", 0);
		_btnRightTab.CustomMinimumSize = new Vector2(30, 120);
		_btnRightTab.LayoutMode = 1;
		_btnRightTab.SetAnchorsPreset(LayoutPreset.CenterLeft);
		_btnRightTab.GrowHorizontal = GrowDirection.Begin;
		_btnRightTab.GrowVertical = GrowDirection.Both;
		_btnRightTab.OffsetLeft = -30;
		_btnRightTab.OffsetRight = 0;
		_btnRightTab.OffsetTop = -60;
		_btnRightTab.OffsetBottom = 60;
		_btnRightTab.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnRightTab.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnRightTab.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnRightTab.AddThemeFontSizeOverride("font_size", 20);
		_btnRightTab.Pressed += ToggleRightPanel;
		_panelRight.AddChild(_btnRightTab);

		var rightScroll = new ScrollContainer();
		rightScroll.LayoutMode = 1;
		rightScroll.SetAnchorsPreset(LayoutPreset.FullRect);
		rightScroll.GrowHorizontal = GrowDirection.Both;
		rightScroll.GrowVertical = GrowDirection.Both;
		rightScroll.OffsetLeft = 10;
		rightScroll.OffsetRight = -10;
		rightScroll.OffsetTop = 40;
		rightScroll.OffsetBottom = -10;
		_panelRight.AddChild(rightScroll);

		_accordionContainer = new VBoxContainer();
		_accordionContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_accordionContainer.AddThemeConstantOverride("separation", 10);
		rightScroll.AddChild(_accordionContainer);

		_accordionTool = new VBoxContainer();
		_accordionTool.Name = "AccordionTool";
		_accordionContainer.AddChild(_accordionTool);
		_btnHeaderTool = new Button();
		_btnHeaderTool.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderTool);
		_accordionTool.AddChild(_btnHeaderTool);
		_contentTool = new VBoxContainer();
		_contentTool.AddThemeConstantOverride("separation", 8);
		_accordionTool.AddChild(_contentTool);
		SetupAccordion(_btnHeaderTool, _contentTool, TranslationServer.Translate("Tool"));

		if (_panelTerrain != null) _panelTerrain.Visible = false;
		if (_panelDeco != null) _panelDeco.Visible = false;

		_panelTerrainVBox = new VBoxContainer();
		_panelTerrainVBox.Name = "PanelTerrainVBox";
		_panelTerrainVBox.AddThemeConstantOverride("separation", 6);
		_contentTool.AddChild(_panelTerrainVBox);
		SafeReparent(_btnRaise, _panelTerrainVBox);
		SafeReparent(_btnLower, _panelTerrainVBox);
		SafeReparent(_btnSmooth, _panelTerrainVBox);
		SafeReparent(_btnPlateau, _panelTerrainVBox);
		SafeReparent(_btnRamp, _panelTerrainVBox);
		SafeReparent(_btnNoise, _panelTerrainVBox);

		_panelDecoVBox = new VBoxContainer();
		_panelDecoVBox.Name = "PanelDecoVBox";
		_panelDecoVBox.AddThemeConstantOverride("separation", 6);
		_contentTool.AddChild(_panelDecoVBox);
		SafeReparent(_btnTextureBrush, _panelDecoVBox);
		SafeReparent(_btnFloodFill, _panelDecoVBox);

		_panelPathingVBox = new VBoxContainer();
		_panelPathingVBox.Name = "PanelPathingVBox";
		_panelPathingVBox.AddThemeConstantOverride("separation", 6);
		_panelPathingVBox.Visible = false;
		_contentTool.AddChild(_panelPathingVBox);
		if (_btnPathingBrush != null) SafeReparent(_btnPathingBrush, _panelPathingVBox);
		if (_btnFloodFillPathing != null) SafeReparent(_btnFloodFillPathing, _panelPathingVBox);

		_panelCoordinatesVBox = new VBoxContainer();
		_panelCoordinatesVBox.Name = "PanelCoordinatesVBox";
		_panelCoordinatesVBox.AddThemeConstantOverride("separation", 8);
		_panelCoordinatesVBox.Visible = false;
		_contentTool.AddChild(_panelCoordinatesVBox);

		if (_btnDrawCoordinate != null) SafeReparent(_btnDrawCoordinate, _panelCoordinatesVBox);

		var rTitle = new Label();
		rTitle.Text = "🗺️ COORDINATE TOOL";
		rTitle.AddThemeFontSizeOverride("font_size", 14);
		rTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_panelCoordinatesVBox.AddChild(rTitle);

		var rInstructions = new Label();
		rInstructions.Text = "Drag to draw a box on the terrain, then name it and create.";
		rInstructions.AddThemeFontSizeOverride("font_size", 11);
		rInstructions.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		rInstructions.AutowrapMode = TextServer.AutowrapMode.Word;
		_panelCoordinatesVBox.AddChild(rInstructions);

		var rNameRow = new HBoxContainer();
		rNameRow.AddThemeConstantOverride("separation", 8);
		_panelCoordinatesVBox.AddChild(rNameRow);

		var rNameLabel = new Label();
		rNameLabel.Text = "Name:";
		rNameLabel.AddThemeFontSizeOverride("font_size", 12);
		rNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		rNameRow.AddChild(rNameLabel);

		_txtCoordinateName = new LineEdit();
		_txtCoordinateName.Name = "TxtCoordinateName";
		_txtCoordinateName.PlaceholderText = "e.g. spawnZone";
		_txtCoordinateName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtCoordinateName.AddThemeFontSizeOverride("font_size", 12);
		rNameRow.AddChild(_txtCoordinateName);

		_btnCommitCoordinate = new Button();
		_btnCommitCoordinate.Name = "BtnCommitCoordinate";
		_btnCommitCoordinate.Set("icon_max_width", 0);
		SetupButton(_btnCommitCoordinate, "✅ Create", () =>
		{
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
		}, 12);
		_panelCoordinatesVBox.AddChild(_btnCommitCoordinate);
		_btnCommitCoordinate.Visible = false;

		var rListTitle = new Label();
		rListTitle.Text = "Defined Coordinates";
		rListTitle.AddThemeFontSizeOverride("font_size", 12);
		rListTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_panelCoordinatesVBox.AddChild(rListTitle);

		_coordinateListVBox = new VBoxContainer();
		_coordinateListVBox.Name = "CoordinateListVBox";
		_coordinateListVBox.AddThemeConstantOverride("separation", 4);
		_panelCoordinatesVBox.AddChild(_coordinateListVBox);

		_accordionBrush = new VBoxContainer();
		_accordionBrush.Name = "AccordionBrush";
		_accordionContainer.AddChild(_accordionBrush);
		_btnHeaderBrush = new Button();
		_btnHeaderBrush.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderBrush);
		_accordionBrush.AddChild(_btnHeaderBrush);
		_contentBrush = new VBoxContainer();
		_contentBrush.AddThemeConstantOverride("separation", 8);
		_accordionBrush.AddChild(_contentBrush);
		SetupAccordion(_btnHeaderBrush, _contentBrush, TranslationServer.Translate("Global Brush Properties"));

		var brushSizeBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox");
		SafeReparent(brushSizeBox, _contentBrush);
		var brushStrengthBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox");
		SafeReparent(brushStrengthBox, _contentBrush);
		SafeReparent(_btnBrushShape, _contentBrush);
		SafeReparent(_btnMirrorMode, _contentBrush);
		SafeReparent(_chkBlockMode, _contentBrush);
		SafeReparent(_stepBox, _contentBrush);

		_accordionToolSettings = new VBoxContainer();
		_accordionToolSettings.Name = "AccordionToolSettings";
		_accordionContainer.AddChild(_accordionToolSettings);
		_btnHeaderToolSettings = new Button();
		_btnHeaderToolSettings.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderToolSettings);
		_accordionToolSettings.AddChild(_btnHeaderToolSettings);
		_contentToolSettings = new VBoxContainer();
		_contentToolSettings.AddThemeConstantOverride("separation", 8);
		_accordionToolSettings.AddChild(_contentToolSettings);
		SetupAccordion(_btnHeaderToolSettings, _contentToolSettings, TranslationServer.Translate("Tool Settings"));


		_containerTextureSettings = new VBoxContainer();
		_containerTextureSettings.Name = "ContainerTexture";
		_containerTextureSettings.AddThemeConstantOverride("separation", 6);
		_contentToolSettings.AddChild(_containerTextureSettings);
		SafeReparent(_lblTerrainTexture, _containerTextureSettings);
		SafeReparent(_lblCliffTexture, _containerTextureSettings);
		var swatchesGrid = GetNodeOrNull<Control>("PanelTextures/VBox/Content/GridSwatches");
		SafeReparent(swatchesGrid, _containerTextureSettings);

		var swapBox = new HBoxContainer();
		swapBox.AddThemeConstantOverride("separation", 8);
		_containerTextureSettings.AddChild(swapBox);

		var btnTextureSwap = new Button();
		btnTextureSwap.Set("icon_max_width", 0);
		btnTextureSwap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SetupButton(btnTextureSwap, "🔄 SWAP TEXTURES (GLOBAL)", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.SwapTexturesExternal(GameHost.Instance.EditorPaintTextureIndex, GameHost.Instance.EditorCliffPaintTextureIndex);
			}
		}, 11, "Globally swap the currently selected Brush Texture with the selected Cliff Texture on the entire map");
		swapBox.AddChild(btnTextureSwap);

		_containerPathingSettings = new VBoxContainer();
		_containerPathingSettings.Name = "ContainerPathing";
		_contentToolSettings.AddChild(_containerPathingSettings);
		var pathingContent = GetNodeOrNull<Control>("PanelPathing/VBox/Content");
		SafeReparent(pathingContent, _containerPathingSettings);

		var clumpTitle = GetNodeOrNull<Label>("PanelEntityPalette/VBox/Content/PalettesVBox/LblClumpTitle");
		if (clumpTitle != null)
		{
			clumpTitle.Visible = false;
		}

		_containerDecalSettings = new VBoxContainer();
		_containerDecalSettings.Name = "ContainerDecal";
		_contentToolSettings.AddChild(_containerDecalSettings);
		var decalsGrid = GetNodeOrNull<Control>("PanelEntityPalette/VBox/Content/PalettesVBox/DecalsGrid");
		SafeReparent(decalsGrid, _containerDecalSettings);

		_containerEyedropperSettings = new VBoxContainer();
		_containerEyedropperSettings.Name = "ContainerEyedropper";
		_contentToolSettings.AddChild(_containerEyedropperSettings);
		SafeReparent(_optEyedropperMode, _containerEyedropperSettings);

		_containerPasteSettings = new VBoxContainer();
		_containerPasteSettings.Name = "ContainerPaste";
		_contentToolSettings.AddChild(_containerPasteSettings);
		var pasteBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/PasteOptionsBox");
		SafeReparent(pasteBox, _containerPasteSettings);

		_containerCategorySelector = new VBoxContainer();
		_containerCategorySelector.Name = "ContainerCategorySelector";
		_containerCategorySelector.AddThemeConstantOverride("separation", 6);
		_contentToolSettings.AddChild(_containerCategorySelector);

		_accordionPlacement = new VBoxContainer();
		_accordionPlacement.Name = "AccordionPlacement";
		_accordionContainer.AddChild(_accordionPlacement);
		_btnHeaderPlacement = new Button();
		_btnHeaderPlacement.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderPlacement);
		_accordionPlacement.AddChild(_btnHeaderPlacement);
		_contentPlacement = new VBoxContainer();
		_contentPlacement.AddThemeConstantOverride("separation", 6);
		_accordionPlacement.AddChild(_contentPlacement);
		SetupAccordion(_btnHeaderPlacement, _contentPlacement, TranslationServer.Translate("Placement Config"));

		var palettesVBox = GetNodeOrNull<Control>("PanelEntityPalette/VBox/Content/PalettesVBox");
		GridContainer categoryGrid = null;
		foreach (var child in palettesVBox.GetChildren())
		{
			if (child is GridContainer gc && gc != decalsGrid && gc.Columns == 2)
			{
				categoryGrid = gc;
				break;
			}
		}
		SafeReparent(categoryGrid, _containerCategorySelector);
		SafeReparent(_entityPaletteController?.OptCategoryItems, _containerCategorySelector);
		
		SafeReparent(_btnToggleRotate, _contentPlacement);
		SafeReparent(_btnToggleScale, _contentPlacement);
		SafeReparent(_placementRotateBox, _contentPlacement);
		SafeReparent(_placementScaleBox, _contentPlacement);
		SafeReparent(_btnToggleSnap, _contentPlacement);
		SafeReparent(_chkSpawnAsEnemy, _contentPlacement);
		SafeReparent(_chkRandomRotation, _contentPlacement);
		SafeReparent(_chkRandomScale, _contentPlacement);
		SafeReparent(_chkClumpMode, _contentPlacement);
		SafeReparent(_densityBox, _contentPlacement);
		SafeReparent(_scaleVarBox, _contentPlacement);

		_accordionInspector = new VBoxContainer();
		_accordionInspector.Name = "AccordionInspector";
		_accordionContainer.AddChild(_accordionInspector);
		_btnHeaderInspector = new Button();
		_btnHeaderInspector.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderInspector);
		_accordionInspector.AddChild(_btnHeaderInspector);
		_contentInspector = new VBoxContainer();
		_contentInspector.AddThemeConstantOverride("separation", 8);
		_accordionInspector.AddChild(_contentInspector);
		SetupAccordion(_btnHeaderInspector, _contentInspector, TranslationServer.Translate("Selected Object Inspector"));
		
		var inspectorVBox = _inspectorPanel?.GetChildOrNull<VBoxContainer>(0);
		if (inspectorVBox != null)
		{
			SafeReparent(inspectorVBox, _contentInspector);
		}

		_accordionViewport = new VBoxContainer();
		_accordionViewport.Name = "AccordionViewport";
		leftVBox.AddChild(_accordionViewport);
		_btnHeaderViewport = new Button();
		_btnHeaderViewport.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderViewport);
		_accordionViewport.AddChild(_btnHeaderViewport);
		_contentViewport = new VBoxContainer();
		_contentViewport.AddThemeConstantOverride("separation", 8);
		_accordionViewport.AddChild(_contentViewport);
		SetupMutualAccordion(_btnHeaderViewport, _contentViewport, TranslationServer.Translate("Viewport & Navigation"));

		SafeReparent(_btnToggleGrid, _contentViewport);
		SafeReparent(_btnToggleCameraBounds, _contentViewport);
		SafeReparent(_btnRotate, _contentViewport);
		SafeReparent(_btnCameraAngle, _contentViewport);
		if (_btnSkybox != null)
		{
			_btnSkybox.CustomMinimumSize = new Vector2(0, 32);
			_btnSkybox.Visible = true;
			SafeReparent(_btnSkybox, _contentViewport);
		}

		SafeReparent(_btnZoomIn, _contentViewport);
		SafeReparent(_btnZoomOut, _contentViewport);
		SafeReparent(_minimapFrame, _contentViewport);

		var panelZoom = GetNodeOrNull<Control>("MiddleRightBox/PanelZoom");
		if (panelZoom != null)
		{
			panelZoom.Visible = false;
		}

		var accordionMapSettings = new VBoxContainer();
		accordionMapSettings.Name = "AccordionMapSettings";
		leftVBox.AddChild(accordionMapSettings);
		
		_btnHeaderMapSettings = new Button();
		_btnHeaderMapSettings.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderMapSettings);
		accordionMapSettings.AddChild(_btnHeaderMapSettings);
		
		_contentMapSettings = new VBoxContainer();
		_contentMapSettings.Visible = false;
		((VBoxContainer)_contentMapSettings).AddThemeConstantOverride("separation", 8);
		accordionMapSettings.AddChild(_contentMapSettings);
		SetupMutualAccordion(_btnHeaderMapSettings, _contentMapSettings, TranslationServer.Translate("Map Settings"));

		SafeReparent(_waterHeightBox, _contentMapSettings);
		SafeReparent(_camBoundsBox, _contentMapSettings);

		var mapSizeBox = new VBoxContainer();
		mapSizeBox.Name = "MapSizeBox";
		_contentMapSettings.AddChild(mapSizeBox);

		var lblMapSizeTitle = new Label();
		lblMapSizeTitle.Text = "📏 " + TranslationServer.Translate("Map Dimensions");
		lblMapSizeTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblMapSizeTitle.AddThemeFontSizeOverride("font_size", 12);
		mapSizeBox.AddChild(lblMapSizeTitle);

		var mapSizeGrid = new GridContainer();
		mapSizeGrid.Columns = 3;
		mapSizeGrid.AddThemeConstantOverride("h_separation", 6);
		mapSizeGrid.AddThemeConstantOverride("v_separation", 4);
		mapSizeBox.AddChild(mapSizeGrid);

		_lblMapWidthVal = new Label();
		_lblMapWidthVal.AddThemeFontSizeOverride("font_size", 11);
		_lblMapWidthVal.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		mapSizeGrid.AddChild(_lblMapWidthVal);

		var btnWidthDec = new Button();
		btnWidthDec.Set("icon_max_width", 0);
		SetupButton(btnWidthDec, "➖", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int w = GameHost.Instance.GroundTerrain.Width;
				if (w > 32) GameHost.Instance.ResizeMapExternal(w - 16, GameHost.Instance.GroundTerrain.Depth);
			}
		}, 10, "Decrease Map Width (West/East edges)");
		mapSizeGrid.AddChild(btnWidthDec);

		var btnWidthInc = new Button();
		btnWidthInc.Set("icon_max_width", 0);
		SetupButton(btnWidthInc, "➕", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int w = GameHost.Instance.GroundTerrain.Width;
				if (w < 512) GameHost.Instance.ResizeMapExternal(w + 16, GameHost.Instance.GroundTerrain.Depth);
			}
		}, 10, "Increase Map Width (West/East edges)");
		mapSizeGrid.AddChild(btnWidthInc);

		_lblMapHeightVal = new Label();
		_lblMapHeightVal.AddThemeFontSizeOverride("font_size", 11);
		_lblMapHeightVal.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		mapSizeGrid.AddChild(_lblMapHeightVal);

		var btnHeightDec = new Button();
		btnHeightDec.Set("icon_max_width", 0);
		SetupButton(btnHeightDec, "➖", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int d = GameHost.Instance.GroundTerrain.Depth;
				if (d > 32) GameHost.Instance.ResizeMapExternal(GameHost.Instance.GroundTerrain.Width, d - 16);
			}
		}, 10, "Decrease Map Height (North/South edges)");
		mapSizeGrid.AddChild(btnHeightDec);

		var btnHeightInc = new Button();
		btnHeightInc.Set("icon_max_width", 0);
		SetupButton(btnHeightInc, "➕", () => {
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null) {
				int d = GameHost.Instance.GroundTerrain.Depth;
				if (d < 512) GameHost.Instance.ResizeMapExternal(GameHost.Instance.GroundTerrain.Width, d + 16);
			}
		}, 10, "Increase Map Height (North/South edges)");
		mapSizeGrid.AddChild(btnHeightInc);

		var btnScaleMap = new Button();
		btnScaleMap.Set("icon_max_width", 0);
		btnScaleMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SetupButton(btnScaleMap, "⬛ SCALE MAP", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				_scaleDialogTargetWidth = GameHost.Instance.GroundTerrain.Width;
				_scaleDialogTargetDepth = GameHost.Instance.GroundTerrain.Depth;
				OpenScaleMapDialog();
			}
		}, 11, "Scale the entire map: stretches/shrinks terrain data and repositions all entities proportionally");
		mapSizeBox.AddChild(btnScaleMap);

		var skyboxBox = new VBoxContainer();
		skyboxBox.Name = "SkyboxBox";
		var lblSkyboxTitle = new Label();
		lblSkyboxTitle.Text = "🌅 " + TranslationServer.Translate("Skybox Environment");
		lblSkyboxTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblSkyboxTitle.AddThemeFontSizeOverride("font_size", 12);
		skyboxBox.AddChild(lblSkyboxTitle);
		_contentMapSettings.AddChild(skyboxBox);
		SafeReparent(_optSkybox, skyboxBox);

		_panelEnv = GetNodeOrNull<PanelContainer>("TopToolbar/PanelEnv");
		if (_panelEnv != null)
		{
			_panelEnv.Visible = false;
		}

		SetRightPanelExpanded(true);

		_panelObjects = new VBoxContainer();
		_panelObjects.Name = "PanelObjects";
		_panelObjects.AddThemeConstantOverride("separation", 6);
		_panelObjects.Visible = false;
		_contentTool.AddChild(_panelObjects);

		SafeReparent(_btnAddObject, _panelObjects);
		SafeReparent(_btnSelectMove, _panelObjects);
		SafeReparent(_btnDeleteObject, _panelObjects);
		if (_btnClumpBrush != null) _btnClumpBrush.Visible = false;

		_panelClipboard = new VBoxContainer();
		_panelClipboard.Name = "PanelClipboard";
		_panelClipboard.AddThemeConstantOverride("separation", 6);
		_panelClipboard.Visible = false;
		_contentTool.AddChild(_panelClipboard);

		SafeReparent(_btnSelectArea, _panelClipboard);
		SafeReparent(_btnCopy, _panelClipboard);
		SafeReparent(_btnPaste, _panelClipboard);

		_btnCut = new Button();
		_btnCut.Name = "BtnCut";
		_btnCut.Set("icon_max_width", 0);
		SetupButton(_btnCut, TranslationServer.Translate("CUT"), () => GameHost.Instance?.PerformCutAreaExternal(), 13, "Cut selected area (Copy and Erase)");
		_panelClipboard.AddChild(_btnCut);

		_btnEraseArea = new Button();
		_btnEraseArea.Name = "BtnEraseArea";
		_btnEraseArea.Set("icon_max_width", 0);
		SetupButton(_btnEraseArea, TranslationServer.Translate("ERASE AREA"), () => GameHost.Instance?.PerformEraseAreaExternal(), 13, "Erase textures, heights, or entities in selected area");
		_panelClipboard.AddChild(_btnEraseArea);

		_btnMirrorHorizontally = new Button();
		_btnMirrorHorizontally.Name = "BtnMirrorHorizontally";
		_btnMirrorHorizontally.Set("icon_max_width", 0);
		SetupButton(_btnMirrorHorizontally, TranslationServer.Translate("MIRROR HORIZONTALLY"), () => GameHost.Instance?.PerformMirrorSelectionHorizontallyExternal(), 13, "Mirror selection horizontally");
		_panelClipboard.AddChild(_btnMirrorHorizontally);

		_btnMirrorVertically = new Button();
		_btnMirrorVertically.Name = "BtnMirrorVertically";
		_btnMirrorVertically.Set("icon_max_width", 0);
		SetupButton(_btnMirrorVertically, TranslationServer.Translate("MIRROR VERTICALLY"), () => GameHost.Instance?.PerformMirrorSelectionVerticallyExternal(), 13, "Mirror selection vertically");
		_panelClipboard.AddChild(_btnMirrorVertically);

		var topLeftBox = GetNode<HBoxContainer>("TopLeftBox");
		topLeftBox.GetParent<Control>().AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		SafeReparent(_btnUndo, topLeftBox);
		SafeReparent(_btnRedo, topLeftBox);
		SafeReparent(_btnEyedropper, topLeftBox);

		_optModule = new OptionButton();
		_optModule.Name = "OptModule";
		_optModule.CustomMinimumSize = new Vector2(100, 32);
		_optModule.Set("icon_max_width", 0);
		_optModule.AddItem("⛰️ " + TranslationServer.Translate("TERRAIN"), (int)EditorModule.Terrain);
		_optModule.AddItem("🎨 " + TranslationServer.Translate("TEXTURE"), (int)EditorModule.TextureDeco);
		_optModule.AddItem("🧭 " + TranslationServer.Translate("PATHING"), (int)EditorModule.Pathing);
		_optModule.AddItem("💂 " + TranslationServer.Translate("OBJECTS"), (int)EditorModule.Objects);
		_optModule.AddItem("🗺️ " + TranslationServer.Translate("COORDINATES"), (int)EditorModule.Coordinates);
		_optModule.AddItem("📋 " + TranslationServer.Translate("CLIPBOARD"), (int)EditorModule.Clipboard);
		_optModule.ItemSelected += (index) =>
		{
			SwitchModule((EditorModule)index);
		};
		topLeftBox.AddChild(_optModule);
		
		topLeftBox.LayoutMode = 1;
		topLeftBox.SetAnchorsPreset(LayoutPreset.CenterTop);
		topLeftBox.GrowHorizontal = GrowDirection.Both;
		topLeftBox.Alignment = BoxContainer.AlignmentMode.Center;
		topLeftBox.OffsetLeft = -450;
		topLeftBox.OffsetRight = 450;
		topLeftBox.OffsetTop = 15;
		topLeftBox.OffsetBottom = 55;

		topLeftBox.MoveChild(_btnBackToHub, 0);
		var btnHelp2 = GetNodeOrNull<Button>("TopLeftBox/BtnHelp");
		if (btnHelp2 != null) topLeftBox.MoveChild(btnHelp2, 1);
		topLeftBox.MoveChild(_btnVSCode, 2);
		topLeftBox.MoveChild(_btnUndo, 3);
		topLeftBox.MoveChild(_btnRedo, 4);
		topLeftBox.MoveChild(_btnEyedropper, 5);
		topLeftBox.MoveChild(_optModule, 6);
		if (_topToolbar != null)
		{
			_topToolbar.Visible = false;
		}

		foreach (Control child in _contentFile.GetChildren())
		{
			child.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		}

		SwitchModule(EditorModule.Terrain);
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

		_containerTextureSettings.Visible = (tool == GameHost.EditorTool.PaintGrass ||
											 tool == GameHost.EditorTool.PaintDirt ||
											 tool == GameHost.EditorTool.PaintRock ||
											 tool == GameHost.EditorTool.PaintSand ||
											 tool == GameHost.EditorTool.FloodFill ||
											 tool == GameHost.EditorTool.Raise ||
											 tool == GameHost.EditorTool.Lower ||
											 tool == GameHost.EditorTool.Plateau ||
											 tool == GameHost.EditorTool.Ramp);
		_containerPathingSettings.Visible = (tool == GameHost.EditorTool.PaintPathing || tool == GameHost.EditorTool.FloodFillPathing);
		_containerDecalSettings.Visible = (tool == GameHost.EditorTool.PlaceDecal);
		_containerEyedropperSettings.Visible = (tool == GameHost.EditorTool.Eyedropper);
		_containerPasteSettings.Visible = (tool == GameHost.EditorTool.SelectArea ||
										   tool == GameHost.EditorTool.PasteArea);

		bool isPlacement = (tool == GameHost.EditorTool.PlaceUnit ||
							tool == GameHost.EditorTool.PlaceProp ||
							tool == GameHost.EditorTool.PlacePropClump ||
							tool == GameHost.EditorTool.PlaceDecal);
		
		_containerCategorySelector.Visible = isPlacement;

		bool anyToolSettingVisible = _containerTextureSettings.Visible ||
									 _containerPathingSettings.Visible ||
									 _containerDecalSettings.Visible ||
									 _containerEyedropperSettings.Visible ||
									 _containerPasteSettings.Visible ||
									 _containerCategorySelector.Visible;

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

	private void DeleteSelectedObject()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			GameHost.Instance.SelectedEditorObject = null;
			var action = GameHost.Instance.DeleteObjectAtWithUndo(selected, (selected as Node3D).Position);
			if (action != null)
			{
				EditorHistoryManager.RecordAction(action);
				ShowFeedback(TranslationServer.Translate("Deleted Selected Object"));
			}
		}
	}

	private void SetupMinimap()
	{
		var rightVBox = GetNode<VBoxContainer>("RightPillar/VBox");

		_minimapFrame = new PanelContainer();
		_minimapFrame.Name = "MinimapFrame";
		_minimapFrame.CustomMinimumSize = new Vector2(176, 176);
		_minimapFrame.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_minimapFrame.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		rightVBox.AddChild(_minimapFrame);
		if (_optEyedropperMode != null)
		{
			rightVBox.MoveChild(_minimapFrame, _optEyedropperMode.GetIndex() + 1);
		}

		_minimapArea = new Control();
		_minimapArea.Name = "MinimapArea";
		_minimapArea.LayoutMode = 2;
		_minimapFrame.AddChild(_minimapArea);

		var minimapBg = new TextureRect();
		minimapBg.Name = "MinimapBg";
		minimapBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		minimapBg.StretchMode = TextureRect.StretchModeEnum.Scale;
		minimapBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		minimapBg.MouseFilter = Control.MouseFilterEnum.Ignore;
		_minimapArea.AddChild(minimapBg);

		_cameraIndicator = new MapEditorCameraIndicator();
		_cameraIndicator.Name = "Indicator";
		_cameraIndicator.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_cameraIndicator.MouseFilter = Control.MouseFilterEnum.Ignore;
		_minimapArea.AddChild(_cameraIndicator);
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

			Texture2D tex = null;
			if (ResourceLoader.Exists(_swatchPaths[i]))
			{
				tex = GD.Load<Texture2D>(_swatchPaths[i]);
			}

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
		_inspectorPanel = new PanelContainer();
		_inspectorPanel.Name = "InspectorPanel";
		
		var vbox = new VBoxContainer();
		vbox.Name = "InspectorVBox";
		_inspectorPanel.AddChild(vbox);

		_lblInspectorTitle = new Label();
		_lblInspectorTitle.Name = "LblInspectorTitle";
		_lblInspectorTitle.Text = "No Selection";
		_lblInspectorTitle.AddThemeFontSizeOverride("font_size", 13);
		_lblInspectorTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(_lblInspectorTitle);

		_lblInspectorPos = new Label();
		_lblInspectorPos.Name = "LblInspectorPos";
		_lblInspectorPos.Text = "Position: (0, 0)";
		_lblInspectorPos.AddThemeFontSizeOverride("font_size", 11);
		_lblInspectorPos.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		vbox.AddChild(_lblInspectorPos);

		var grid = new GridContainer();
		grid.Columns = 2;
		vbox.AddChild(grid);

		_btnInspectorRotLeft = new Button();
		_btnInspectorRotLeft.Name = "BtnInspectorRotLeft";
		_btnInspectorRotLeft.Text = "↩ Rot Left";
		_btnInspectorRotLeft.Set("icon_max_width", 0);
		grid.AddChild(_btnInspectorRotLeft);

		_btnInspectorRotRight = new Button();
		_btnInspectorRotRight.Name = "BtnInspectorRotRight";
		_btnInspectorRotRight.Text = "Rot Right ↪";
		_btnInspectorRotRight.Set("icon_max_width", 0);
		grid.AddChild(_btnInspectorRotRight);

		_btnInspectorScaleDown = new Button();
		_btnInspectorScaleDown.Name = "BtnInspectorScaleDown";
		_btnInspectorScaleDown.Text = "➖ Scale Down";
		_btnInspectorScaleDown.Set("icon_max_width", 0);
		grid.AddChild(_btnInspectorScaleDown);

		_btnInspectorScaleUp = new Button();
		_btnInspectorScaleUp.Name = "BtnInspectorScaleUp";
		_btnInspectorScaleUp.Text = "Scale Up ➕";
		_btnInspectorScaleUp.Set("icon_max_width", 0);
		grid.AddChild(_btnInspectorScaleUp);

		_btnInspectorScaleReset = new Button();
		_btnInspectorScaleReset.Name = "BtnInspectorScaleReset";
		_btnInspectorScaleReset.Text = "↺ Reset Scale";
		_btnInspectorScaleReset.Set("icon_max_width", 0);
		vbox.AddChild(_btnInspectorScaleReset);

		SafeReparent(_btnCenter, vbox);

		_btnInspectorDelete = new Button();
		_btnInspectorDelete.Name = "BtnInspectorDelete";
		_btnInspectorDelete.Text = "❌ Delete Object";
		_btnInspectorDelete.Set("icon_max_width", 0);
		vbox.AddChild(_btnInspectorDelete);
		
		vbox.MoveChild(_btnCenter, _btnInspectorDelete.GetIndex());
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

	private async System.Threading.Tasks.Task ProceedToTestMap()
	{
		if (GameHost.Instance == null) return;

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

		string csprojPath = System.IO.Path.Combine(_tempWorkspacePath, "CustomMap.csproj");
		if (System.IO.File.Exists(csprojPath))
		{
			ShowFeedbackExternal("Compiling map script...");
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"build \"{csprojPath}\"")
				{
					WorkingDirectory = _tempWorkspacePath,
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using var process = System.Diagnostics.Process.Start(psi);
				if (process != null)
				{
					await process.WaitForExitAsync();
					if (process.ExitCode == 0)
					{
						string dllPath = System.IO.Directory.GetFiles(
							System.IO.Path.Combine(_tempWorkspacePath, "bin"),
							"CustomMap.dll",
							System.IO.SearchOption.AllDirectories
						).FirstOrDefault();
						if (System.IO.File.Exists(dllPath))
						{
							GameHost.PendingMapScriptPath = dllPath;
						}
					}
					else
					{
						string error = process.StandardError.ReadToEnd();
						ShowFeedbackExternal($"Build failed: {error}");
						return;
					}
				}
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"[ProceedToTestMap] Build exception: {ex.Message}");
			}
		}

		// Clean Godot Mono assembly cache to force fresh assembly loading
		string godotCachePath = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".godot", "mono", "temp");
		try
		{
			if (System.IO.Directory.Exists(godotCachePath))
			{
				System.IO.Directory.Delete(godotCachePath, true);
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[ProceedToTestMap] Failed to clean Godot cache: {ex.Message}");
		}

		IsTestMode = true;

		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.HostSinglePlayerGame(TempWorkspaceGodotPath, "Test Map");
		}
	}
}
