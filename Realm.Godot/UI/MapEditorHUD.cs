using Godot;
using System;
using System.Collections.Generic;

public partial class MapEditorHUD : Control
{
	public static MapEditorHUD Instance { get; private set; }

	public enum EditorModule
	{
		Terrain,
		TextureDeco,
		Objects
	}

	private PanelContainer _panelLeft;
	private Button _btnLeftTab;
	private PanelContainer _panelRight;
	private Button _btnRightTab;
	private VBoxContainer _accordionContainer;
	
	private bool _leftPanelExpanded = false;
	private bool _rightPanelExpanded = true;

	private HBoxContainer _moduleBar;
	private Button _btnModuleTerrain;
	private Button _btnModulePaint;
	private Button _btnModuleObjects;
	private EditorModule _activeModule = EditorModule.Terrain;

	private VBoxContainer _accordionBrush;
	private Button _btnHeaderBrush;
	private VBoxContainer _contentBrush;
	
	private VBoxContainer _accordionToolSettings;
	private Button _btnHeaderToolSettings;
	private VBoxContainer _contentToolSettings;
	
	private VBoxContainer _accordionPlacement;
	private Button _btnHeaderPlacement;
	private VBoxContainer _contentPlacement;
	
	private VBoxContainer _accordionViewport;
	private Button _btnHeaderViewport;
	private VBoxContainer _contentViewport;
	
	private VBoxContainer _accordionNavigation;
	private Button _btnHeaderNavigation;
	private VBoxContainer _contentNavigation;
	
	private VBoxContainer _accordionInspector;
	private Button _btnHeaderInspector;
	private VBoxContainer _contentInspector;

	private VBoxContainer _containerFlattenSettings;
	private VBoxContainer _containerTextureSettings;
	private VBoxContainer _containerPathingSettings;
	private VBoxContainer _containerClumpSettings;
	private VBoxContainer _containerPlacementSettings;
	private VBoxContainer _containerDecalSettings;
	private VBoxContainer _containerEyedropperSettings;
	private VBoxContainer _containerPasteSettings;
	private VBoxContainer _containerCategorySelector;

	private PanelContainer _panelObjects;
	
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
	private Slider _sldFlattenHeight;
	private Label _lblFlattenHeightValue;

	private Button _btnFootman;
	private Button _btnArcher;
	private Button _btnCastle;
	private Button _btnTower;
	private CheckBox _chkSpawnAsEnemy;

	private Button _btnTree;
	private Button _btnPropRock;
	private Button _btnGoldMine;
	private Button _btnPillar;
	private Button _btnFlag;

	private Button _btnChars;
	private Button _btnBuilds;
	private Button _btnEnv;
	private Button _btnProps;
	private Button _btnDecals;
	private CheckBox _chkRandomRotation;
	private CheckBox _chkRandomScale;
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
	private Button _btnToggleSnap;
	private Button _btnBigSave;
	private Button _btnToggleGrid;
	private Button _btnBrushShape;
	private Button _btnFillMap;
	private Button _btnResetMap;
	private Button _btnGenerateMap;
	private Button _btnImportMinimap;
	private int _genHillsDensity = 5;
	private int _genTerrainRoughness = 5;
	private int _genMountainHeight = 5;
	private int _genChokeWidth = 5;
	private int _genWaterLevel = 5;
	private int _genTreeDensity = 5;
	private int _genResourceAbundance = 5;
	private int _genDecoDensity = 5;
	private string _genSeed = "";
	private Button _btnEyedropper;
	private OptionButton _optEyedropperMode;
	private Button _btnNoise;
	private PanelContainer _minimapFrame;
	private Control _minimapArea;
	private ReferenceRect _cameraIndicator;

	private Button _btnSkybox;
	private OptionButton _optSkybox;
	private List<string> _skyboxFiles = new List<string>();

	private string _currentCategory = "Characters";
	private List<string> _categoryFiles = new List<string>();
	private OptionButton _optCategoryItems;

	private string[] _swatchPaths = new string[12];
	private string[] _swatchDisplayNames = new string[12];
	private Color[] _swatchColors = new Color[12];

	private Button _btnRaise;
	private Button _btnLower;
	private Button _btnSmooth;
	private Button _btnFlatten;
	private Button _btnCliff;
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
	private CheckBox _chkInspectorIsEnemy;
	private Button _btnInspectorAlignToGround;
	private Button _btnInspectorDelete;

	private Button _btnPathingBrush;
	private PanelContainer _panelPathing;
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkUnpathable;
	private OptionButton _optPathingMode;

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

	private Camera3D _camera3D;
	private Button _btnVSCode;
	private VSCodeMdiWindow _vscodeMdi;
	private bool _isDraggingSlider = false;

	public override void _Ready()
	{
		Instance = this;

		_camera3D = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");

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
		_panelEnv = GetNode<PanelContainer>("TopToolbar/PanelEnv");

		for (int i = 1; i <= 12; i++)
		{
			_swatchButtons[i - 1] = GetNode<Button>($"PanelTextures/VBox/Content/GridSwatches/Swatch{i}");
		}

		_btnBackToHub = GetNode<Button>("TopLeftBox/BtnBack");
		_btnPublish = GetNode<Button>("MiddleRightBox/BtnPublish");
		_btnSave = GetNode<Button>("MiddleRightBox/BtnSave");
		_btnUndo = GetNode<Button>("MiddleRightBox/HistoryHBox/BtnUndo");
		_btnRedo = GetNode<Button>("MiddleRightBox/HistoryHBox/BtnRedo");
		_btnDeleteObject = GetNode<Button>("RightPillar/VBox/BtnDeleteObject");
		_statusLabel = GetNode<Label>("TopBar/HBox/StatusLabel");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");

		_btnZoomIn = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnZoomIn");
		_btnZoomOut = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnZoomOut");
		_btnCenter = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnCenter");
		_btnRotate = GetNode<Button>("MiddleRightBox/PanelZoom/VBox/Content/BtnRotate");
		_btnCameraAngle = new Button();
		_btnCameraAngle.Name = "BtnCameraAngle";
		_btnCameraAngle.Set("icon_max_width", 0);
		GetNode<GridContainer>("MiddleRightBox/PanelZoom/VBox/Content").AddChild(_btnCameraAngle);

		_sldBrushSize = GetNode<Slider>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox/SldBrushSize");
		_lblBrushSizeValue = GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox/Header/LblBrushSizeValue");
		_sldBrushStrength = GetNode<Slider>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox/SldBrushStrength");
		_lblBrushStrengthValue = GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox/Header/LblBrushStrengthValue");
		_sldFlattenHeight = GetNode<Slider>("PanelTextures/VBox/Content/SettingsVBox/FlattenHeightBox/SldFlattenHeight");
		_lblFlattenHeightValue = GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/FlattenHeightBox/Header/LblFlattenHeightValue");

		_btnRaise = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnRaise");
		_btnLower = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnLower");
		_btnSmooth = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnSmooth");
		_btnFlatten = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnFlatten");
		_btnCliff = GetNode<Button>("TopToolbar/PanelTerrain/VBox/Content/BtnCliff");
		_btnRamp = new Button();
		_btnRamp.Name = "BtnRamp";
		_btnRamp.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelTerrain/VBox/Content").AddChild(_btnRamp);

		_btnTextureBrush = GetNode<Button>("TopToolbar/PanelDeco/VBox/Content/BtnTextureBrush");
		_btnDecalTool = GetNode<Button>("TopToolbar/PanelDeco/VBox/Content/BtnDecalTool");
		_btnFloodFill = new Button();
		_btnFloodFill.Name = "BtnFloodFill";
		_btnFloodFill.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnFloodFill);

		_btnSelectArea = new Button();
		_btnSelectArea.Name = "BtnSelectArea";
		_btnSelectArea.Set("icon_max_width", 0);
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnSelectArea);

		_btnSkybox = GetNode<Button>("TopToolbar/PanelEnv/VBox/Content/BtnSkybox");
		if (_btnSkybox != null)
		{
			_btnSkybox.Visible = false;
		}

		_optSkybox = new OptionButton();
		_optSkybox.Name = "OptSkybox";
		_optSkybox.CustomMinimumSize = new Vector2(160, 30);
		var envContent = GetNode<HBoxContainer>("TopToolbar/PanelEnv/VBox/Content");
		envContent.AddChild(_optSkybox);

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
			_optSkybox.AddItem(cleanName);
		}

		_optSkybox.ItemSelected += (index) =>
		{
			if (index >= 0 && index < _skyboxFiles.Count && GameHost.Instance != null)
			{
				string selectedFile = _skyboxFiles[(int)index];
				string path = $"res://Assets/Skyboxes/{selectedFile}";
				GameHost.Instance.SetSkyboxTexture(path);
				ShowFeedback($"Skybox environment set to: {_optSkybox.GetItemText((int)index)}");
			}
		};

		_btnFootman = GetNodeOrNull<Button>("PanelEntityPalette/VBox/Content/PalettesVBox/UnitsGrid/BtnFootman");
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

		var categoryGrid = new GridContainer();
		categoryGrid.Columns = 2;
		categoryGrid.AddThemeConstantOverride("h_separation", 6);
		categoryGrid.AddThemeConstantOverride("v_separation", 6);
		palettesVBox.AddChild(categoryGrid);
		palettesVBox.MoveChild(categoryGrid, 0);

		_optCategoryItems = new OptionButton();
		_optCategoryItems.Name = "OptCategoryItems";
		_optCategoryItems.CustomMinimumSize = new Vector2(180, 30);
		palettesVBox.AddChild(_optCategoryItems);
		palettesVBox.MoveChild(_optCategoryItems, 1);

		_optCategoryItems.ItemSelected += (index) => SelectCategoryItem((int)index);

		_btnChars = new Button();
		_btnChars.Set("icon_max_width", 0);
		SetupButton(_btnChars, "👤 Characters", () => SelectCategory("Characters"), 12, "Select Characters category");
		categoryGrid.AddChild(_btnChars);

		_btnBuilds = new Button();
		_btnBuilds.Set("icon_max_width", 0);
		SetupButton(_btnBuilds, "🏢 Buildings", () => SelectCategory("Buildings"), 12, "Select Buildings category");
		categoryGrid.AddChild(_btnBuilds);

		_btnEnv = new Button();
		_btnEnv.Set("icon_max_width", 0);
		SetupButton(_btnEnv, "🌳 Environment", () => SelectCategory("Environment"), 12, "Select Environment category");
		categoryGrid.AddChild(_btnEnv);

		_btnProps = new Button();
		_btnProps.Set("icon_max_width", 0);
		SetupButton(_btnProps, "📦 Props", () => SelectCategory("Props"), 12, "Select Props category");
		categoryGrid.AddChild(_btnProps);

		_btnDecals = new Button();
		_btnDecals.Set("icon_max_width", 0);
		SetupButton(_btnDecals, "🎨 Decals", () => SelectCategory("Decals"), 12, "Select Decals category");
		categoryGrid.AddChild(_btnDecals);

		SelectCategory("Characters");

		_btnToggleRotate = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnToggleRotate");
		_btnToggleScale = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnToggleScale");
		_btnToggleRotate.Visible = false;
		_btnToggleScale.Visible = false;

		_placementRotateBox = new VBoxContainer();
		_placementRotateBox.Name = "PlacementRotateBox";
		var rotateHeader = new HBoxContainer();
		_placementRotateBox.AddChild(rotateHeader);
		var lblRotateTitle = new Label();
		lblRotateTitle.Text = "🔄 Rotation";
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
		_sldPlacementRotate.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblPlacementRotateValue.Text = fVal.ToString("F0") + "°";
			if (GameHost.Instance != null) GameHost.Instance.EditorPlacementRotation = fVal;
		};
		_sldPlacementRotate.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementRotate.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_placementScaleBox = new VBoxContainer();
		_placementScaleBox.Name = "PlacementScaleBox";
		var scaleHeader = new HBoxContainer();
		_placementScaleBox.AddChild(scaleHeader);
		var lblScaleTitle = new Label();
		lblScaleTitle.Text = "📏 Scale";
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
		_sldPlacementScale.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblPlacementScaleValue.Text = fVal.ToString("F1") + "x";
			if (GameHost.Instance != null) GameHost.Instance.EditorPlacementScale = fVal;
		};
		_sldPlacementScale.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementScale.DragEnded += (valueChanged) => _isDraggingSlider = false;

		var placementRightSettingsVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/RightSettingsVBox");
		placementRightSettingsVBox.AddChild(_placementRotateBox);
		placementRightSettingsVBox.AddChild(_placementScaleBox);
		_btnToggleSnap = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnToggleSnap");
		_btnBigSave = GetNode<Button>("PanelEntityPalette/VBox/Content/RightSettingsVBox/BtnBigSave");

		_lblInfoText = GetNode<Label>("RightPillar/VBox/LblInfoText");

		var clumpPalettesVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/PalettesVBox");
		var lblClumpTitle = new Label();
		lblClumpTitle.Name = "LblClumpTitle";
		lblClumpTitle.Text = "PROP CLUMPING TOOL";
		lblClumpTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblClumpTitle.AddThemeFontSizeOverride("font_size", 11);
		clumpPalettesVBox.AddChild(lblClumpTitle);

		_btnClumpBrush = new Button();
		_btnClumpBrush.Name = "BtnClumpBrush";
		_btnClumpBrush.Set("icon_max_width", 0);
		clumpPalettesVBox.AddChild(_btnClumpBrush);
		SetupButton(_btnClumpBrush, "🌲 CLUMP BRUSH", () => TriggerToolSelection(GameHost.EditorTool.PlacePropClump, _btnClumpBrush, "tree"), 13, "Paint multiple props continuously inside the brush area");

		var clumpRightSettingsVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/RightSettingsVBox");
		var densityBox = new VBoxContainer();
		densityBox.Name = "DensityBox";
		clumpRightSettingsVBox.AddChild(densityBox);

		var densityHeader = new HBoxContainer();
		densityBox.AddChild(densityHeader);

		var lblDensityTitle = new Label();
		lblDensityTitle.Text = "Clump Density";
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
		densityBox.AddChild(_sldClumpDensity);
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

		var scaleVarBox = new VBoxContainer();
		scaleVarBox.Name = "ScaleVarBox";
		clumpRightSettingsVBox.AddChild(scaleVarBox);

		var scaleVarHeader = new HBoxContainer();
		scaleVarBox.AddChild(scaleVarHeader);

		var lblScaleVarTitle = new Label();
		lblScaleVarTitle.Text = "Clump Scale Var";
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
		scaleVarBox.AddChild(_sldClumpScaleVar);
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

		_genSeed = new Random().Next(100000, 999999).ToString();

		_btnResetMap = new Button();
		_btnResetMap.Name = "BtnResetMap";
		_btnResetMap.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnResetMap);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnResetMap, _btnSave.GetIndex() + 1);
		SetupButton(_btnResetMap, "🧹 RESET MAP", () =>
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
		SetupButton(_btnGenerateMap, "🎲 RANDOM GEN", () => ShowGenerationDialog(), 13, "Open random terrain generator settings modal");

		_btnImportMinimap = new Button();
		_btnImportMinimap.Name = "BtnImportMinimap";
		_btnImportMinimap.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnImportMinimap);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnImportMinimap, _btnGenerateMap.GetIndex() + 1);
		SetupButton(_btnImportMinimap, "🗺️ GEN FROM IMAGE", () => ImportTerrainFromMinimapDialog(), 13, "Import terrain elevations, textures, and trees from a minimap image file (F6)");

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

		if (OperatingSystem.IsWindows())
		{
			_vscodeMdi = new VSCodeMdiWindow();
			_vscodeMdi.Visible = false;
			AddChild(_vscodeMdi);
			_vscodeMdi.Position = new Vector2(250, 100);

			_btnVSCode = new Button();
			_btnVSCode.Name = "BtnVSCode";
			_btnVSCode.Set("icon_max_width", 0);
			GetNode<HBoxContainer>("TopLeftBox").AddChild(_btnVSCode);
			SetupButton(_btnVSCode, "💻 DATA EDITOR", () =>
			{
				if (_vscodeMdi != null)
				{
					_vscodeMdi.Visible = !_vscodeMdi.Visible;
				}
			}, 13, "Toggle the embedded VS Code data editor");
		}

		_btnLoad = new Button();
		_btnLoad.Name = "BtnLoad";
		_btnLoad.Set("icon_max_width", 0);
		GetNode<VBoxContainer>("MiddleRightBox").AddChild(_btnLoad);
		GetNode<VBoxContainer>("MiddleRightBox").MoveChild(_btnLoad, _btnSave.GetIndex() + 1);
		SetupButton(_btnLoad, "📂 LOAD FILE", () => LoadMapAction(), 13, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

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
				GameHost.Instance.EditorGridVisible = !GameHost.Instance.EditorGridVisible;
				GameHost.Instance.UpdateGridOverlayVisibility();
				UpdateGridOverlayExternal(GameHost.Instance.EditorGridVisible);
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
		_chkRandomRotation.Text = "🎲 Random Rotation";
		_chkRandomRotation.TooltipText = "Randomize the placement rotation for units and props";
		UIStyle.ApplyCheckboxStyle(_chkRandomRotation);
		rightSettingsVBox.AddChild(_chkRandomRotation);
		rightSettingsVBox.MoveChild(_chkRandomRotation, chkIndex + 1);
		_chkRandomRotation.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorRandomRotation = toggled;
			}
		};

		_chkRandomScale = new CheckBox();
		_chkRandomScale.Name = "ChkRandomScale";
		_chkRandomScale.Text = "📏 Random Scale";
		_chkRandomScale.TooltipText = "Randomize the placement scale for props";
		UIStyle.ApplyCheckboxStyle(_chkRandomScale);
		rightSettingsVBox.AddChild(_chkRandomScale);
		rightSettingsVBox.MoveChild(_chkRandomScale, chkIndex + 1);
		_chkRandomScale.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorRandomScale = toggled;
			}
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

		var camBoundsBox = new VBoxContainer();
		camBoundsBox.Name = "CamBoundsBox";
		rightSettingsVBox.AddChild(camBoundsBox);
		rightSettingsVBox.MoveChild(camBoundsBox, chkIndex + 2);

		var lblCamBoundsTitle = new Label();
		lblCamBoundsTitle.Text = "📹 ADJUST CAMERA BOUNDS";
		lblCamBoundsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblCamBoundsTitle.AddThemeFontSizeOverride("font_size", 11);
		camBoundsBox.AddChild(lblCamBoundsTitle);

		var adjustGrid = new GridContainer();
		adjustGrid.Columns = 3;
		adjustGrid.AddThemeConstantOverride("h_separation", 6);
		adjustGrid.AddThemeConstantOverride("v_separation", 4);
		camBoundsBox.AddChild(adjustGrid);

		_lblCamLeftVal = new Label();
		_lblCamLeftVal.AddThemeFontSizeOverride("font_size", 11);
		_lblCamLeftVal.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		adjustGrid.AddChild(_lblCamLeftVal);

		var btnLeftDec = new Button();
		btnLeftDec.Set("icon_max_width", 0);
		SetupButton(btnLeftDec, "⬅️", () => {
			if (GameHost.Instance != null) {
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
		_chkBlockMode.Text = "🧱 Block Mode (M)";
		_chkBlockMode.TooltipText = "Toggle blocky terrain sculpting & automatic steep cliff coloring";
		UIStyle.ApplyCheckboxStyle(_chkBlockMode);
		settingsVBox.AddChild(_chkBlockMode);
		_chkBlockMode.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorBlockMode = toggled;
				ShowFeedback(toggled ? "Block Mode: Enabled" : "Block Mode: Disabled");
			}
		};
		_chkBlockMode.ButtonPressed = true;

		var stepBox = new VBoxContainer();
		stepBox.Name = "StepBox";
		settingsVBox.AddChild(stepBox);

		var stepHeader = new HBoxContainer();
		stepBox.AddChild(stepHeader);

		var lblStepTitle = new Label();
		lblStepTitle.Text = "Block Level Height";
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
		stepBox.AddChild(_sldBlockStep);
		_sldBlockStep.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblBlockStepValue.Text = fVal.ToString("F1") + " m";
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorBlockLevelHeight = fVal;
			}
		};
		_sldBlockStep.DragStarted += () => _isDraggingSlider = true;
		_sldBlockStep.DragEnded += (valueChanged) => _isDraggingSlider = false;

		var divider = new HSeparator();
		settingsVBox.AddChild(divider);

		var waterHeightBox = new VBoxContainer();
		waterHeightBox.Name = "WaterHeightBox";
		settingsVBox.AddChild(waterHeightBox);

		var waterHeader = new HBoxContainer();
		waterHeightBox.AddChild(waterHeader);

		var lblWaterTitle = new Label();
		lblWaterTitle.Text = "Water Height Level";
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
		waterHeightBox.AddChild(_sldWaterHeight);
		_sldWaterHeight.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblWaterHeightValue.Text = fVal.ToString("F1") + " m";
			if (GameHost.Instance?.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.WaterHeight = fVal;
			}
		};
		_sldWaterHeight.DragStarted += () => _isDraggingSlider = true;
		_sldWaterHeight.DragEnded += (valueChanged) => _isDraggingSlider = false;

		var pasteDivider = new HSeparator();
		settingsVBox.AddChild(pasteDivider);

		var pasteOptionsBox = new VBoxContainer();
		pasteOptionsBox.Name = "PasteOptionsBox";
		settingsVBox.AddChild(pasteOptionsBox);

		var lblPasteOptionsTitle = new Label();
		lblPasteOptionsTitle.Text = "PASTE CONTENTS OPTIONS";
		lblPasteOptionsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lblPasteOptionsTitle.AddThemeFontSizeOverride("font_size", 11);
		pasteOptionsBox.AddChild(lblPasteOptionsTitle);

		var chkPasteTextures = new CheckBox();
		chkPasteTextures.Name = "ChkPasteTextures";
		chkPasteTextures.Text = "📋 Paste Textures";
		chkPasteTextures.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPasteTextures);
		pasteOptionsBox.AddChild(chkPasteTextures);
		chkPasteTextures.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionTextures = toggled;
		};

		var chkPasteHeights = new CheckBox();
		chkPasteHeights.Name = "ChkPasteHeights";
		chkPasteHeights.Text = "⛰️ Paste HeightMap";
		chkPasteHeights.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPasteHeights);
		pasteOptionsBox.AddChild(chkPasteHeights);
		chkPasteHeights.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionHeights = toggled;
		};

		var chkPasteEntities = new CheckBox();
		chkPasteEntities.Name = "ChkPasteEntities";
		chkPasteEntities.Text = "💂 Paste Units / Props";
		chkPasteEntities.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(chkPasteEntities);
		pasteOptionsBox.AddChild(chkPasteEntities);
		chkPasteEntities.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PasteOptionEntities = toggled;
		};
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

		_btnFillMap = new Button();
		_btnFillMap.Name = "BtnFillMap";
		_btnFillMap.Set("icon_max_width", 0);
		_btnFillMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		brushActionsHBox.AddChild(_btnFillMap);
		SetupButton(_btnFillMap, "🪣 FILL MAP", () =>
		{
			GameHost.Instance?.FillMapWithActiveColor();
		}, 11, "Paint the entire map with the currently selected texture swatch");

		if (GameHost.Instance != null)
		{
			_btnBrushShape.Text = GameHost.Instance.EditorBrushIsSquare ? "🔳 BRUSH: SQUARE" : "⚪ BRUSH: CIRCLE";
			_btnToggleGrid.Text = GameHost.Instance.EditorGridVisible ? "🌐 GRID OVERLAY: ON" : "🌐 GRID OVERLAY: OFF";
			_btnToggleCameraBounds.Text = GameHost.Instance.EditorCameraBoundsVisible ? "📹 CAM BOUNDS: ON" : "📹 CAM BOUNDS: OFF";
		}

		_btnEyedropper = new Button();
		_btnEyedropper.Name = "BtnEyedropper";
		_btnEyedropper.Set("icon_max_width", 0);
		var rightVBox = GetNode<VBoxContainer>("RightPillar/VBox");
		rightVBox.AddChild(_btnEyedropper);
		rightVBox.MoveChild(_btnEyedropper, _btnDeleteObject.GetIndex());
		SetupButton(_btnEyedropper, "🔍 EYEDROPPER", () => TriggerToolSelection(GameHost.EditorTool.Eyedropper, _btnEyedropper), 14, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");

		_optEyedropperMode = new OptionButton();
		_optEyedropperMode.Name = "OptEyedropperMode";
		_optEyedropperMode.AddItem("🔍 Auto-Detect Mode", 0);
		_optEyedropperMode.AddItem("🌳 Pick 3D Asset", 1);
		_optEyedropperMode.AddItem("🎨 Pick Decal", 2);
		_optEyedropperMode.AddItem("⛰️ Pick Terrain Texture", 3);
		_optEyedropperMode.AddItem("📏 Pick Height", 4);
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
		SetupButton(_btnNoise, "🎲 Roughen", () => TriggerToolSelection(GameHost.EditorTool.Noise, _btnNoise), 11, "Add random height variations/noise to the terrain under the brush (N)");

		palettesVBox = GetNode<VBoxContainer>("PanelEntityPalette/VBox/Content/PalettesVBox");
		var lblDecalsTitle = new Label();
		lblDecalsTitle.Name = "LblDecalsTitle";
		lblDecalsTitle.Text = "PALETTE: DECALS";
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

		string[] decalIds = { "logo", "forest", "snowy", "flag", "rune" };
		string[] decalIcons = { "🖼️", "🌳", "❄️", "🚩", "🔯" };
		string[] decalTooltips = { "Godot Logo decal", "Forest Path decal", "Snowy Forest Path decal", "Alliance Flag decal", "Magic Rune decal" };
		for (int idx = 0; idx < decalIds.Length; idx++)
		{
			string dId = decalIds[idx];
			var btn = new Button();
			btn.Name = $"BtnDecal_{dId}";
			btn.Set("icon_max_width", 0);
			btn.CustomMinimumSize = new Vector2(42, 42);
			decalsGrid.AddChild(btn);
			SetupButton(btn, decalIcons[idx], () => TriggerToolSelection(GameHost.EditorTool.PlaceDecal, btn, dId), 18, decalTooltips[idx]);
		}

		TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise);

		if (GameHost.Instance != null)
		{
			UpdateRotationExternal(GameHost.Instance.EditorPlacementRotation);
			UpdateScaleExternal(GameHost.Instance.EditorPlacementScale);
			UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
		}

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;

		SetupMinimap();
		RebuildHUDLayout();
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
		GetNode<PanelContainer>("TopToolbar/PanelEnv").AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
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

		SetupButton(_btnBackToHub, "BACK TO HUB", () =>
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
		}, 13, "Exit the editor and return to the main menu");
		SetupButton(_btnPublish, "📜 PUBLISH MAP", () => PublishMapAction(), 13, "Compile map mesh, shaders, and metadata for gameplay (Ctrl+P)");
		SetupButton(_btnSave, "💾 SAVE FILE", () => SaveMapAction(), 13, "Save current heights, colors, and entities to terrain.json (Ctrl+S)");
		SetupButton(_btnUndo, "↩️ UNDO", () => {
			EditorHistoryManager.Undo();
			ShowFeedback("Undo Action performed");
		}, 11, "Undo the last terrain edit or object action (Ctrl+Z)");
		SetupButton(_btnRedo, "↪️ REDO", () => {
			EditorHistoryManager.Redo();
			ShowFeedback("Redo Action performed");
		}, 11, "Redo the last undone action (Ctrl+Y / Ctrl+Shift+Z)");
		SetupButton(_btnDeleteObject, "ERASER TOOL", () => TriggerToolSelection(GameHost.EditorTool.DeleteObject, _btnDeleteObject), 14, "Remove entities from the map. Left click on units or props in 3D (0)");

		_btnSelectMove = new Button();
		_btnSelectMove.Name = "BtnSelectMove";
		_btnSelectMove.Set("icon_max_width", 0);
		var rightVBox = GetNode<VBoxContainer>("RightPillar/VBox");
		rightVBox.AddChild(_btnSelectMove);
		rightVBox.MoveChild(_btnSelectMove, _btnDeleteObject.GetIndex());
		SetupButton(_btnSelectMove, "SELECT / MOVE", () => TriggerToolSelection(GameHost.EditorTool.SelectMove, _btnSelectMove), 14, "Select or drag entities. Drag to move, R to rotate, S to scale (Q)");

		CreateInspectorPanel();

		SetupButton(_btnRaise, "⬆️ Raise", () => TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise), 11, "Elevate terrain under the brush (1)");
		SetupButton(_btnLower, "⬇️ Lower", () => TriggerToolSelection(GameHost.EditorTool.Lower, _btnLower), 11, "Depress terrain under the brush (2)");
		SetupButton(_btnSmooth, "🌐 Smooth", () => TriggerToolSelection(GameHost.EditorTool.Smooth, _btnSmooth), 11, "Blend and soften steep slopes under the brush (3)");
		SetupButton(_btnFlatten, "➖ Flatten", () => TriggerToolSelection(GameHost.EditorTool.Flatten, _btnFlatten), 11, "Flatten heights under the brush to target Height slider value (4)");
		SetupButton(_btnCliff, "⛰️ Cliff", () => TriggerToolSelection(GameHost.EditorTool.Cliff, _btnCliff), 11, "Create terraced cliffs. Hold Shift to sculpt lower level (5)");
		SetupButton(_btnRamp, "📐 Ramp", () => TriggerToolSelection(GameHost.EditorTool.Ramp, _btnRamp), 11, "Create a ramp between two clicked points (9)");

		SetupButton(_btnTextureBrush, "🖌️ Texture", () => TriggerToolSelection(GameHost.EditorTool.PaintGrass, _btnTextureBrush), 11, "Paint vertex colors onto the terrain using selected texture swatch (6)");
		SetupButton(_btnDecalTool, "🖼️ Decal", () => {
			TriggerToolSelection(GameHost.EditorTool.PlaceDecal, _btnDecalTool);
			ShowFeedback("Decal Placement Tool Selected");
		}, 11, "Place decorative decals projecting textures onto ground (7)");
		SetupButton(_btnFloodFill, "🪣 Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFill, _btnFloodFill), 11, "Flood fill connected area sharing the same texture until hitting a cliff/boundary");
		SetupButton(_btnSelectArea, "🟦 Area Select", () => TriggerToolSelection(GameHost.EditorTool.SelectArea, _btnSelectArea), 11, "Select a rectangular area of the map to copy/paste (Ctrl+C, Ctrl+V)");



		SetupButton(_btnSkybox, "☀️ Sky / Time", () => {
			GameHost.Instance?.CycleTimeOfDay();
			string timeName = GameHost.Instance != null ? GameHost.Instance.Call("GetTimeOfDayName").AsString() : "";
			ShowFeedback($"Environment time set to: {timeName}");
		}, 11, "Cycle map sky box and lighting preset");

		SetupButton(_btnZoomIn, "➕ In", () => _camera3D?.Call("ZoomIn"), 12, "Zoom camera in (Mouse Wheel Up)");
		SetupButton(_btnZoomOut, "➖ Out", () => _camera3D?.Call("ZoomOut"), 12, "Zoom camera out (Mouse Wheel Down)");
		SetupButton(_btnCenter, "🎯 Target", () => GameHost.Instance?.CenterCameraOnSelectedOrCastle(), 11, "Center camera on selected object (or castle)");
		SetupButton(_btnRotate, "🔄 Rotate", () => {
			if (_camera3D != null && _camera3D.HasMethod("Rotate90Degrees"))
			{
				_camera3D.Call("Rotate90Degrees");
				ShowFeedback("Rotated camera 90 degrees clockwise");
			}
		}, 11, "Rotate camera yaw by 90 degrees clockwise on click");
		SetupButton(_btnCameraAngle, "📐 Angle: Tilt", () => {
			if (_camera3D != null && _camera3D.HasMethod("ToggleTopDown"))
			{
				_camera3D.Call("ToggleTopDown");
				bool topDown = _camera3D.Call("IsTopDown").AsBool();
				UpdateCameraAngleButtonText(topDown);
			}
		}, 11, "Toggle between precisely top-down view and tilted in-game camera view (C)");

		SetupTextureSwatches(true);

		SetupButton(_btnFootman, "💂", () => TriggerToolSelection(GameHost.EditorTool.PlaceUnit, _btnFootman, "footman"), 18, "Place a Footman warrior");
		SetupButton(_btnArcher, "🏹", () => TriggerToolSelection(GameHost.EditorTool.PlaceUnit, _btnArcher, "archer"), 18, "Place an Archer unit");
		SetupButton(_btnCastle, "🏰", () => TriggerToolSelection(GameHost.EditorTool.PlaceUnit, _btnCastle, "castle"), 18, "Place a Castle/Stronghold town hall");
		SetupButton(_btnTower, "🗼", () => TriggerToolSelection(GameHost.EditorTool.PlaceUnit, _btnTower, "tower"), 18, "Place a defensive watch tower");
		_chkSpawnAsEnemy.TooltipText = "Place units as the enemy orc faction";
		UIStyle.ApplyCheckboxStyle(_chkSpawnAsEnemy);

		SetupButton(_btnTree, "🌳", () => TriggerToolSelection(GameHost.EditorTool.PlaceProp, _btnTree, "tree"), 18, "Place a deciduous oak tree");
		SetupButton(_btnPropRock, "🪨", () => TriggerToolSelection(GameHost.EditorTool.PlaceProp, _btnPropRock, "rock"), 18, "Place a stone rock prop");
		SetupButton(_btnGoldMine, "🪙", () => TriggerToolSelection(GameHost.EditorTool.PlaceProp, _btnGoldMine, "goldmine"), 18, "Place a goldmine resource node");
		SetupButton(_btnPillar, "🏛️", () => TriggerToolSelection(GameHost.EditorTool.PlaceProp, _btnPillar, "pillar"), 18, "Place a marble column ruin");
		SetupButton(_btnFlag, "🚩", () => TriggerToolSelection(GameHost.EditorTool.PlaceProp, _btnFlag, "flag"), 18, "Place a custom flag prop");

		SetupButton(_btnToggleRotate, "🔄 Object Rotation", () => {
			if (GameHost.Instance != null) {
				GameHost.Instance.EditorPlacementRotation = (GameHost.Instance.EditorPlacementRotation + 45.0f) % 360.0f;
				ShowFeedback($"Placement Rotation: {GameHost.Instance.EditorPlacementRotation}°");
			}
		}, 10, "Cycle object placement yaw rotation in 45 degree steps (R)");
		SetupButton(_btnToggleScale, "📏 Scale Option", () => {
			if (GameHost.Instance != null) {
				float current = GameHost.Instance.EditorPlacementScale;
				float next = current switch {
					0.5f => 1.0f,
					1.0f => 1.5f,
					1.5f => 2.0f,
					2.0f => 0.5f,
					_ => 1.0f
				};
				GameHost.Instance.EditorPlacementScale = next;
				ShowFeedback($"Placement Scale: {next}x");
			}
		}, 10, "Cycle object placement scale size multiplier (S)");
		SetupButton(_btnToggleSnap, "🔲 Snap to Grid", () => {
			if (GameHost.Instance != null) {
				GameHost.Instance.EditorSnapToGrid = !GameHost.Instance.EditorSnapToGrid;
				ShowFeedback($"Snapping: {(GameHost.Instance.EditorSnapToGrid ? "Enabled" : "Disabled")}");
			}
		}, 10, "Toggle snapping placement and translation to grid points (Ctrl+G)");
		SetupButton(_btnBigSave, "📜 SAVE & COMPILE", () => SaveMapAction(), 13, "Save and compile current heights and entities");

		GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushSettingsTitle").AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox/Header/LblSizeTitle").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblBrushSizeValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox/Header/LblStrengthTitle").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblBrushStrengthValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		GetNode<Label>("PanelTextures/VBox/Content/SettingsVBox/FlattenHeightBox/Header/LblFlattenTitle").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblFlattenHeightValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);

		GetNode<Label>("PanelEntityPalette/VBox/Content/PalettesVBox/LblUnitsTitle").AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		GetNode<Label>("PanelEntityPalette/VBox/Content/PalettesVBox/LblPropsTitle").AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		GetNode<Label>("RightPillar/VBox/ObjectToolsTitle").AddThemeColorOverride("font_color", UIStyle.ColorBronze);

		UIStyle.ApplyTitle(_feedbackLabel, "", 24);
		UpdateMirrorButtonText();

		_btnPathingBrush = new Button();
		_btnPathingBrush.Name = "BtnPathingBrush";
		_btnPathingBrush.Set("icon_max_width", 0);
		SetupButton(_btnPathingBrush, "🧭 Pathing", () => TriggerToolSelection(GameHost.EditorTool.PaintPathing, _btnPathingBrush), 11, "Paint pathing attributes onto the terrain map");
		GetNode<HBoxContainer>("TopToolbar/PanelDeco/VBox/Content").AddChild(_btnPathingBrush);

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

		var modeHBox = new HBoxContainer();
		var modeLabel = new Label();
		modeLabel.Text = "Mode: ";
		modeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		modeHBox.AddChild(modeLabel);

		_optPathingMode = new OptionButton();
		_optPathingMode.Name = "OptPathingMode";
		_optPathingMode.AddItem("➕ Add Layer", 0);
		_optPathingMode.AddItem("➖ Remove Layer", 1);
		_optPathingMode.Selected = 0;
		modeHBox.AddChild(_optPathingMode);
		pContent.AddChild(modeHBox);

		var layersLabel = new Label();
		layersLabel.Text = "Select Layers:";
		layersLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		pContent.AddChild(layersLabel);

		_chkShallowWater = new CheckBox();
		_chkShallowWater.Text = "Shallow Water";
		_chkShallowWater.ButtonPressed = false;
		UIStyle.ApplyCheckboxStyle(_chkShallowWater);
		pContent.AddChild(_chkShallowWater);

		_chkDeepWater = new CheckBox();
		_chkDeepWater.Text = "Deep Water";
		_chkDeepWater.ButtonPressed = false;
		UIStyle.ApplyCheckboxStyle(_chkDeepWater);
		pContent.AddChild(_chkDeepWater);

		_chkFlying = new CheckBox();
		_chkFlying.Text = "Flying";
		_chkFlying.ButtonPressed = false;
		UIStyle.ApplyCheckboxStyle(_chkFlying);
		pContent.AddChild(_chkFlying);

		_chkGround = new CheckBox();
		_chkGround.Text = "Ground";
		_chkGround.ButtonPressed = true;
		UIStyle.ApplyCheckboxStyle(_chkGround);
		pContent.AddChild(_chkGround);

		_chkUnpathable = new CheckBox();
		_chkUnpathable.Text = "Unpathable";
		_chkUnpathable.ButtonPressed = false;
		UIStyle.ApplyCheckboxStyle(_chkUnpathable);
		pContent.AddChild(_chkUnpathable);

		var brushSettingsTitle = new Label();
		brushSettingsTitle.Text = "Brush Settings";
		brushSettingsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		pContent.AddChild(brushSettingsTitle);

		var brushSizeBox = new VBoxContainer();
		var sizeHeader = new HBoxContainer();
		var sizeTitle = new Label();
		sizeTitle.Text = "Size: ";
		sizeTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		var sizeVal = new Label();
		sizeVal.Text = _sldBrushSize.Value.ToString("F1");
		sizeVal.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		sizeHeader.AddChild(sizeTitle);
		sizeHeader.AddChild(sizeVal);
		brushSizeBox.AddChild(sizeHeader);

		var pathSldBrushSize = new HSlider();
		pathSldBrushSize.MinValue = _sldBrushSize.MinValue;
		pathSldBrushSize.MaxValue = _sldBrushSize.MaxValue;
		pathSldBrushSize.Step = _sldBrushSize.Step;
		pathSldBrushSize.Value = _sldBrushSize.Value;
		brushSizeBox.AddChild(pathSldBrushSize);
		pContent.AddChild(brushSizeBox);

		pathSldBrushSize.ValueChanged += (val) =>
		{
			_sldBrushSize.Value = val;
			sizeVal.Text = val.ToString("F1");
		};
		_sldBrushSize.ValueChanged += (val) =>
		{
			pathSldBrushSize.Value = val;
			sizeVal.Text = val.ToString("F1");
		};

		var pathingDivider = new HSeparator();
		pContent.AddChild(pathingDivider);

		var legendLabel = new Label();
		legendLabel.Text = "LAYER COLOR LEGEND";
		legendLabel.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		legendLabel.AddThemeFontSizeOverride("font_size", 11);
		pContent.AddChild(legendLabel);

		var legendColors = new (string name, Color color)[] {
			("Shallow Water", new Color(0.2f, 0.6f, 1.0f, 0.7f)),
			("Deep Water",    new Color(0.0f, 0.15f, 0.7f, 0.7f)),
			("Flying",        new Color(0.85f, 0.85f, 0.0f, 0.7f)),
			("Ground",        new Color(0.2f, 0.85f, 0.2f, 0.7f)),
			("Unpathable",    new Color(0.9f, 0.1f, 0.1f, 0.7f)),
		};
		foreach (var (name, col) in legendColors)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			pContent.AddChild(row);

			var swatch = new ColorRect();
			swatch.Color = col;
			swatch.CustomMinimumSize = new Vector2(14, 14);
			swatch.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			row.AddChild(swatch);

			var nameLbl = new Label();
			nameLbl.Text = name;
			nameLbl.AddThemeColorOverride("font_color", col.Lightened(0.2f));
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(nameLbl);
		}

		var overlayBtn = new Button();
		overlayBtn.Name = "BtnPathingOverlay";
		overlayBtn.Set("icon_max_width", 0);
		SetupButton(overlayBtn, "👁 SHOW OVERLAY: ON", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.PathingOverlayVisible = !GameHost.Instance.PathingOverlayVisible;
				GameHost.Instance.UpdatePathingOverlay();
				overlayBtn.Text = GameHost.Instance.PathingOverlayVisible ? "👁 SHOW OVERLAY: ON" : "👁 SHOW OVERLAY: OFF";
			}
		}, 11, "Toggle the colored pathing overlay visualization on the terrain");
		pContent.AddChild(overlayBtn);
	}

	private void SetupButton(Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		btn.Flat = false;
		UIStyle.ApplyButtonText(btn, text, fontSize);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = tooltip;
		}

		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
	}

	private Color AutoCalcModColor(Texture2D tex)
	{
		if (tex == null) return new Color(1, 1, 1);
		try
		{
			var img = tex.GetImage();
			if (img != null)
			{
				var tempImg = (Image)img.Duplicate();
				tempImg.Resize(1, 1, Image.Interpolation.Bilinear);
				Color avgColor = tempImg.GetPixel(0, 0);
				avgColor.A = 1.0f;
				return avgColor;
			}
		}
		catch
		{
		}
		return new Color(1, 1, 1);
	}

	private GameHost.EditorTool ClassifyToolFromColor(Color color)
	{
		float maxDiff = Mathf.Max(Mathf.Abs(color.R - color.G), Mathf.Max(Mathf.Abs(color.G - color.B), Mathf.Abs(color.R - color.B)));
		if (maxDiff < 0.08f)
		{
			return GameHost.EditorTool.PaintRock;
		}
		
		if (color.G > color.R + 0.05f && color.G > color.B + 0.05f)
		{
			return GameHost.EditorTool.PaintGrass;
		}
		
		if (color.R > 0.5f && color.G > 0.4f && color.B < 0.4f)
		{
			return GameHost.EditorTool.PaintSand;
		}
		
		return GameHost.EditorTool.PaintDirt;
	}

	private void SetupTextureSwatches(bool connectEvents = false)
	{
		var sheetFiles = new List<string>();
		using (var dir = DirAccess.Open("res://Assets/2d/TileSheets"))
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
						sheetFiles.Add($"res://Assets/2d/TileSheets/{fileName}");
					}
					fileName = dir.GetNext();
				}
			}
		}

		sheetFiles.Sort();

		while (sheetFiles.Count < 12)
		{
			sheetFiles.Add("res://Assets/terrain_grass.jpg");
		}

		for (int i = 1; i <= 12; i++)
		{
			int index = i;
			var swatch = _swatchButtons[i - 1];
			
			string texPath = sheetFiles[i - 1];
			_swatchPaths[i - 1] = texPath;
			
			string cleanName = System.IO.Path.GetFileNameWithoutExtension(texPath).Replace("_", " ");
			cleanName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);
			_swatchDisplayNames[i - 1] = cleanName;

			var tex = GD.Load<Texture2D>(texPath);
			if (tex == null)
			{
				tex = GD.Load<Texture2D>("res://Assets/terrain_grass.jpg");
			}

			Color modColor = AutoCalcModColor(tex);
			_swatchColors[i - 1] = modColor;

			var styleNormal = new StyleBoxTexture();
			styleNormal.Texture = tex;
			swatch.AddThemeStyleboxOverride("normal", styleNormal);
			
			var styleHover = (StyleBoxTexture)styleNormal.Duplicate();
			styleHover.ModulateColor = new Color(1.2f, 1.2f, 1.2f);
			swatch.AddThemeStyleboxOverride("hover", styleHover);
			
			var stylePressed = (StyleBoxTexture)styleNormal.Duplicate();
			stylePressed.ModulateColor = new Color(0.8f, 0.8f, 0.8f);
			swatch.AddThemeStyleboxOverride("pressed", stylePressed);

			swatch.ButtonMask = MouseButtonMask.Left | MouseButtonMask.Right;
			if (connectEvents)
			{
				swatch.GuiInput += (InputEvent @event) =>
				{
					if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
					{
						if (mouseEvent.ButtonIndex == MouseButton.Left)
						{
							UIManager.Instance?.PlayClickSound();
							SelectTerrainTexture(index, modColor, swatch);
							swatch.AcceptEvent();
						}
						else if (mouseEvent.ButtonIndex == MouseButton.Right)
						{
							UIManager.Instance?.PlayClickSound();
							SelectCliffTexture(index, modColor);
							swatch.AcceptEvent();
						}
					}
				};
			}
		}
		UpdateTextureLabels();
	}

	private void SelectTerrainTexture(int index, Color modColor, Button swatch)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorPaintColor = modColor;
		}
		
		GameHost.EditorTool tool = GameHost.EditorTool.PaintGrass;
		if (GameHost.Instance != null && GameHost.Instance.ActiveEditorTool == GameHost.EditorTool.FloodFill)
		{
			tool = GameHost.EditorTool.FloodFill;
		}
		else
		{
			tool = ClassifyToolFromColor(modColor);
		}
		TriggerToolSelection(tool, swatch, $"layer_{index}");
		UpdateTextureLabels();
	}

	private void SelectCliffTexture(int index, Color modColor)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorCliffPaintColor = modColor;
		}
		UpdateTextureLabels();
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
				return $"Swatch {i} ({texName})";
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

	private void UpdateTextureLabels()
	{
		if (_lblTerrainTexture == null || _lblCliffTexture == null) return;
		if (GameHost.Instance == null) return;

		_lblTerrainTexture.Text = "Terrain: " + GetSwatchName(GameHost.Instance.EditorPaintColor);
		_lblCliffTexture.Text = "Cliff (Right-Click): " + GetSwatchName(GameHost.Instance.EditorCliffPaintColor);
	}

	private void SetupMenuHooks()
	{
		_sldBrushSize.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblBrushSizeValue.Text = fVal.ToString("F1");
			if (GameHost.Instance != null) GameHost.Instance.EditorBrushRadius = fVal;
		};

		_sldBrushStrength.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblBrushStrengthValue.Text = fVal.ToString("F1");
			if (GameHost.Instance != null) GameHost.Instance.EditorBrushStrength = fVal;
		};

		_sldFlattenHeight.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblFlattenHeightValue.Text = fVal.ToString("F1") + " m";
			if (GameHost.Instance != null) GameHost.Instance.EditorFlattenHeight = fVal;
		};
		_sldBrushSize.DragStarted += () => _isDraggingSlider = true;
		_sldBrushSize.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_sldBrushStrength.DragStarted += () => _isDraggingSlider = true;
		_sldBrushStrength.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_sldFlattenHeight.DragStarted += () => _isDraggingSlider = true;
		_sldFlattenHeight.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_chkSpawnAsEnemy.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.PlaceUnitIsEnemy = toggled;
			}
		};

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

	private void TriggerToolSelection(GameHost.EditorTool tool, Button btn, string placeId = "")
	{
		if (GameHost.Instance == null) return;

		if (tool != GameHost.EditorTool.SelectMove)
		{
			GameHost.Instance.SelectedEditorObject = null;
		}

		if (_activeToolButton != null)
		{
			_activeToolButton.RemoveThemeStyleboxOverride("normal");
			if (_activeToolButton.Name.ToString().StartsWith("Swatch"))
			{
				SetupTextureSwatches(false);
			}
		}

		_activeToolButton = btn;
		if (_activeToolButton != null)
		{
			_activeToolButton.AddThemeStyleboxOverride("normal", _highlightStyle);
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
			tool == GameHost.EditorTool.Flatten ||
			tool == GameHost.EditorTool.Smooth ||
			tool == GameHost.EditorTool.Cliff ||
			tool == GameHost.EditorTool.Ramp ||
			tool == GameHost.EditorTool.Noise)
		{
			targetModule = EditorModule.Terrain;
		}
		else if (tool == GameHost.EditorTool.PaintGrass ||
				 tool == GameHost.EditorTool.PaintDirt ||
				 tool == GameHost.EditorTool.PaintRock ||
				 tool == GameHost.EditorTool.PaintSand ||
				 tool == GameHost.EditorTool.FloodFill ||
				 tool == GameHost.EditorTool.SelectArea ||
				 tool == GameHost.EditorTool.PasteArea ||
				 tool == GameHost.EditorTool.PaintPathing)
		{
			targetModule = EditorModule.TextureDeco;
		}
		else if (tool == GameHost.EditorTool.PlaceUnit ||
				 tool == GameHost.EditorTool.PlaceProp ||
				 tool == GameHost.EditorTool.PlacePropClump ||
				 tool == GameHost.EditorTool.PlaceDecal ||
				 tool == GameHost.EditorTool.DeleteObject ||
				 tool == GameHost.EditorTool.SelectMove ||
				 tool == GameHost.EditorTool.Eyedropper)
		{
			targetModule = EditorModule.Objects;
		}

		if (targetModule != _activeModule)
		{
			_activeModule = targetModule;
			UpdateModuleSwitchButtons();
			if (_panelTerrain != null) _panelTerrain.Visible = (targetModule == EditorModule.Terrain);
			if (_panelDeco != null) _panelDeco.Visible = (targetModule == EditorModule.TextureDeco);
			if (_panelEnv != null) _panelEnv.Visible = (targetModule == EditorModule.TextureDeco);
			if (_panelObjects != null) _panelObjects.Visible = (targetModule == EditorModule.Objects);
		}

		if (tool == GameHost.EditorTool.PaintPathing)
		{
			if (_panelPathing != null) _panelPathing.Visible = false;
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
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
					_lblInfoText.Text = "TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings.";
					break;
				case GameHost.EditorTool.Ramp:
					_lblInfoText.Text = "TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel.";
					break;
				case GameHost.EditorTool.Lower:
					_lblInfoText.Text = "TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings.";
					break;
				case GameHost.EditorTool.Flatten:
					_lblInfoText.Text = "TOOL: Flatten Heights\n\nDrag left click to snap terrain heights toward the target Flatten Height slider value.";
					break;
				case GameHost.EditorTool.Smooth:
					_lblInfoText.Text = "TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations.";
					break;
				case GameHost.EditorTool.Cliff:
					_lblInfoText.Text = "TOOL: Cliff / Terrace\n\nDrag left click to create flat terraced steps. Hold Shift to lower the target cliff level.";
					break;
				case GameHost.EditorTool.PaintGrass:
				case GameHost.EditorTool.PaintDirt:
				case GameHost.EditorTool.PaintRock:
				case GameHost.EditorTool.PaintSand:
					_lblInfoText.Text = $"TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh.";
					break;
				case GameHost.EditorTool.FloodFill:
					_lblInfoText.Text = "TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch.";
					break;
				case GameHost.EditorTool.SelectArea:
					_lblInfoText.Text = "TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area.";
					break;
				case GameHost.EditorTool.PasteArea:
					_lblInfoText.Text = "TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Paste Contents Options checkboxes to filter what is pasted (Textures, Heights, Entities).";
					break;
				case GameHost.EditorTool.PlaceUnit:
					string alignment = _chkSpawnAsEnemy.ButtonPressed ? "Enemy (Orc)" : "Player (Alliance)";
					_lblInfoText.Text = $"TOOL: Place Unit\n\nLeft-click on the ground to spawn a {placeId.ToUpper()} aligned with {alignment}.";
					break;
				case GameHost.EditorTool.PlaceProp:
					_lblInfoText.Text = $"TOOL: Place Prop\n\nLeft-click on the ground to spawn static decorative object: {placeId.ToUpper()}.";
					break;
				case GameHost.EditorTool.PlacePropClump:
					_lblInfoText.Text = $"TOOL: Clump Brush\n\nDrag left click on the ground to paint clumps of static props: {placeId.ToUpper()} based on Density and Scale Variation settings. Uses texture brush shape (Circle/Square).";
					break;
				case GameHost.EditorTool.PlaceDecal:
					_lblInfoText.Text = "TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply.";
					break;
				case GameHost.EditorTool.DeleteObject:
					_lblInfoText.Text = "TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map.";
					break;
				case GameHost.EditorTool.SelectMove:
					_lblInfoText.Text = "TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Use R to rotate, S to scale, or Delete/Backspace to delete.";
					break;
				case GameHost.EditorTool.Eyedropper:
					_lblInfoText.Text = "TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height.";
					break;
				case GameHost.EditorTool.Noise:
					_lblInfoText.Text = "TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings.";
					break;
				case GameHost.EditorTool.PaintPathing:
					_lblInfoText.Text = "TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove.";
					break;
				case GameHost.EditorTool.None:
					_lblInfoText.Text = "Select a tool from the panels to begin terrain modification.";
					break;
			}
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
			"Save Map Folder",
			GetInitialDirectory(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string selectedFolder = selectedPaths[0];
					string terrainPath = System.IO.Path.Combine(selectedFolder, "terrain.json");
					GameHost.Instance.SaveMapToFile(terrainPath);

					string mapName = System.IO.Path.GetFileName(selectedFolder);
					string scriptPath = System.IO.Path.Combine(selectedFolder, "MapScript.cs");
					if (!System.IO.File.Exists(scriptPath))
					{
						string scriptContent = $@"namespace Realm.Maps;

using Realm.MapAPI;

public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}
";
						System.IO.File.WriteAllText(scriptPath, scriptContent);
					}

					string unitsPath = System.IO.Path.Combine(selectedFolder, "metadata.json");
					if (!System.IO.File.Exists(unitsPath))
					{
						System.IO.File.WriteAllText(unitsPath, "{}");
					}

					if (OperatingSystem.IsWindows())
					{
						VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
					}
					ShowFeedback($"Map saved successfully to folder {System.IO.Path.GetFileName(selectedFolder)}!");
				}
				else
				{
					ShowFeedback("Save cancelled");
				}
			})
		);

		if (err != Error.Ok)
		{
			GameHost.Instance.SaveMapToFile();
			ShowFeedback("Saving map heights & entities...");
			var timer = GetTree().CreateTimer(0.8f);
			timer.Timeout += () => ShowFeedback("Map saved successfully to user://terrain.json!");
		}
	}

	public void LoadMapAction()
	{
		if (GameHost.Instance == null) return;

		var err = DisplayServer.FileDialogShow(
			"Load Map Folder",
			GetInitialDirectory(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string selectedFolder = selectedPaths[0];
					string terrainPath = System.IO.Path.Combine(selectedFolder, "terrain.json");
					bool success = GameHost.Instance.LoadMapFromFile(terrainPath);
					if (success)
					{
						if (OperatingSystem.IsWindows())
						{
							VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
						}
						ShowFeedback($"Map loaded successfully from folder {System.IO.Path.GetFileName(selectedFolder)}!");
					}
					else
					{
						ShowFeedback("Failed to load map files from folder!");
					}
				}
			})
		);

		if (err != Error.Ok)
		{
			bool success = GameHost.Instance.LoadMapFromFile();
			if (success)
			{
				ShowFeedback("Map loaded from user://terrain.json");
			}
			else
			{
				ShowFeedback("No map file found at user://terrain.json");
			}
		}
	}

	private string GetInitialDirectory()
	{
		if (OperatingSystem.IsWindows())
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			if (System.IO.File.Exists(recentPathFile))
			{
				string path = System.IO.File.ReadAllText(recentPathFile).Trim();
				if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
				{
					string parent = System.IO.Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(parent) && System.IO.Directory.Exists(parent))
					{
						return parent;
					}
					return path;
				}
			}
			return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
		}
		return OS.GetSystemDir(OS.SystemDir.Documents);
	}

	private void PublishMapAction()
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.SaveMapToFile();
		}
		ShowFeedback("Compiling terrain shaders & entity data...");
		var timer = GetTree().CreateTimer(1.0f);
		timer.Timeout += () => ShowFeedback("Map compiled & published successfully!");
	}

	public void ShowFeedbackExternal(string text)
	{
		ShowFeedback(text);
	}

	public void SaveMapActionExternal()
	{
		SaveMapAction();
	}

	public void UpdateWaterEnabledExternal(bool enabled)
	{
		if (_chkWaterEnabled != null)
		{
			_chkWaterEnabled.SetBlockSignals(true);
			_chkWaterEnabled.ButtonPressed = enabled;
			_chkWaterEnabled.SetBlockSignals(false);
		}
	}

	public void UpdateGridSnapExternal(bool snap)
	{
		if (_btnToggleSnap != null)
		{
			_btnToggleSnap.Text = snap ? "🔲 GRID SNAP: ON" : "🔲 GRID SNAP: OFF";
		}
		ShowFeedback(snap ? "Grid Snapping: Enabled" : "Grid Snapping: Disabled");
	}

	public void UpdateGridOverlayExternal(bool visible)
	{
		if (_btnToggleGrid != null)
		{
			_btnToggleGrid.Text = visible ? "🌐 GRID OVERLAY: ON" : "🌐 GRID OVERLAY: OFF";
		}
		ShowFeedback(visible ? "Grid Overlay: Visible" : "Grid Overlay: Hidden");
	}

	public void UpdateCameraBoundsOverlayExternal(bool visible)
	{
		if (_btnToggleCameraBounds != null)
		{
			_btnToggleCameraBounds.Text = visible ? "📹 CAM BOUNDS: ON" : "📹 CAM BOUNDS: OFF";
		}
		ShowFeedback(visible ? "Camera Bounds: Visible" : "Camera Bounds: Hidden");
	}

	public void UpdateCameraBoundsUI()
	{
		if (GameHost.Instance == null) return;
		if (_lblCamLeftVal != null) _lblCamLeftVal.Text = $"L: {GameHost.Instance.EditorCameraBoundsLeft:F0}m";
		if (_lblCamRightVal != null) _lblCamRightVal.Text = $"R: {GameHost.Instance.EditorCameraBoundsRight:F0}m";
		if (_lblCamTopVal != null) _lblCamTopVal.Text = $"T: {GameHost.Instance.EditorCameraBoundsTop:F0}m";
		if (_lblCamBottomVal != null) _lblCamBottomVal.Text = $"B: {GameHost.Instance.EditorCameraBoundsBottom:F0}m";
	}

	public void UpdateSelectedSkyboxExternal(string path)
	{
		if (_optSkybox == null || _skyboxFiles == null) return;
		string fileName = System.IO.Path.GetFileName(path);
		int index = _skyboxFiles.IndexOf(fileName);
		if (index >= 0)
		{
			_optSkybox.Selected = index;
		}
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

	public int GetSelectedPathingMask()
	{
		int mask = 0;
		if (_chkShallowWater != null && _chkShallowWater.ButtonPressed) mask |= EditableTerrain.PATHING_SHALLOW_WATER;
		if (_chkDeepWater    != null && _chkDeepWater.ButtonPressed)    mask |= EditableTerrain.PATHING_DEEP_WATER;
		if (_chkFlying       != null && _chkFlying.ButtonPressed)       mask |= EditableTerrain.PATHING_FLYING;
		if (_chkGround       != null && _chkGround.ButtonPressed)       mask |= EditableTerrain.PATHING_GROUND;
		if (_chkUnpathable   != null && _chkUnpathable.ButtonPressed)   mask |= EditableTerrain.PATHING_UNPATHABLE;
		return mask == 0 ? EditableTerrain.PATHING_GROUND : mask;
	}

	public bool IsPathingAddMode()
	{
		return _optPathingMode == null || _optPathingMode.Selected == 0;
	}

	public void UpdatePathingOverlayExternal(bool visible)
	{
		var btn = GetNodeOrNull<Button>("PanelPathing/VBox/Content/BtnPathingOverlay");
		if (btn != null)
		{
			btn.Text = visible ? "\ud83d\udc41 SHOW OVERLAY: ON" : "\ud83d\udc41 SHOW OVERLAY: OFF";
		}
	}

	public void SelectCategoryItem(int index)
	{
		if (index >= 0 && index < _categoryFiles.Count && GameHost.Instance != null)
		{
			string selectedFile = _categoryFiles[index];
			string path = "";
			GameHost.EditorTool tool = GameHost.EditorTool.None;

			switch (_currentCategory)
			{
				case "Characters":
					path = "res://Assets/3d/Characters";
					tool = GameHost.EditorTool.PlaceUnit;
					break;
				case "Buildings":
					path = "res://Assets/3d/Buildings";
					tool = GameHost.EditorTool.PlaceUnit;
					break;
				case "Environment":
					path = "res://Assets/3d/Environment";
					tool = GameHost.EditorTool.PlaceProp;
					break;
				case "Props":
					path = "res://Assets/3d/Props";
					tool = GameHost.EditorTool.PlaceProp;
					break;
				case "Decals":
					path = "res://Assets/2d/Decals";
					tool = GameHost.EditorTool.PlaceDecal;
					break;
			}

			string placeId = selectedFile;
			if (_currentCategory == "Characters" || _currentCategory == "Buildings" || _currentCategory == "Environment" || _currentCategory == "Props")
			{
				placeId = $"{path}/{selectedFile}";
			}
			
			Button categoryBtn = _currentCategory switch
			{
				"Characters" => _btnChars,
				"Buildings" => _btnBuilds,
				"Environment" => _btnEnv,
				"Props" => _btnProps,
				"Decals" => _btnDecals,
				_ => null
			};
			
			TriggerToolSelection(tool, categoryBtn, placeId);
			ShowFeedback($"Placing {_currentCategory}: {_optCategoryItems.GetItemText(index)}");
		}
	}

	public void SelectCategoryItemExternal(string category, string filename)
	{
		SelectCategory(category);
		int index = _categoryFiles.IndexOf(filename);
		if (index >= 0)
		{
			_optCategoryItems.Selected = index;
			SelectCategoryItem(index);
		}
	}

	public void SelectPickedUnitOrProp(string id, bool isBuilding)
	{
		string category = "";
		string filename = "";

		if (id.StartsWith("res://"))
		{
			filename = System.IO.Path.GetFileName(id);
			if (id.Contains("Characters")) category = "Characters";
			else if (id.Contains("Buildings")) category = "Buildings";
			else if (id.Contains("Environment")) category = "Environment";
			else if (id.Contains("Props")) category = "Props";
		}
		else
		{
			if (isBuilding)
			{
				category = "Buildings";
				filename = id switch
				{
					"castle" => "altar.glb",
					"tower" => "altar_pillar.glb",
					_ => id
				};
			}
			else
			{
				category = "Characters";
				filename = id switch
				{
					"worker" => "adventurer.glb",
					"footman" => "armored_warlord.glb",
					"archer" => "armored_dragon.glb",
					"priest" => "armored_battlelord.glb",
					_ => id
				};
			}
		}

		if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(filename))
		{
			SelectCategoryItemExternal(category, filename);
		}
	}

	public void SelectPickedDecal(string decalId)
	{
		string filename = decalId.StartsWith("res://") ? System.IO.Path.GetFileName(decalId) : decalId;
		if (decalId == "logo") filename = "logo.png";
		SelectCategoryItemExternal("Decals", filename);
	}
	public void SelectPaintSwatchFromColor(Color color)
	{
		int bestIndex = 1;
		float minDiff = float.MaxValue;
		for (int i = 0; i < 12; i++)
		{
			Color c = _swatchColors[i];
			float diff = Mathf.Abs(c.R - color.R) + Mathf.Abs(c.G - color.G) + Mathf.Abs(c.B - color.B);
			if (diff < minDiff)
			{
				minDiff = diff;
				bestIndex = i + 1;
			}
		}

		var swatch = _swatchButtons[bestIndex - 1];
		if (swatch != null)
		{
			SelectTerrainTexture(bestIndex, _swatchColors[bestIndex - 1], swatch);
		}
		
		string swatchName = _swatchDisplayNames[bestIndex - 1];
		ShowFeedback($"Picked Color: #{color.ToHtml(false)} ({swatchName})");
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
		_minimapArea.AddChild(minimapBg);

		_cameraIndicator = new ReferenceRect();
		_cameraIndicator.Name = "Indicator";
		_cameraIndicator.CustomMinimumSize = new Vector2(25, 18);
		_cameraIndicator.BorderColor = new Color(0, 0.9f, 0.1f, 0.85f);
		_cameraIndicator.BorderWidth = 2.0f;
		_cameraIndicator.EditorOnly = false;
		_minimapArea.AddChild(_cameraIndicator);

		_minimapArea.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				TeleportCameraToMinimapPos(mouseBtn.Position);
			}
			else if (@event is InputEventMouseMotion mouseMotion && mouseMotion.ButtonMask == MouseButtonMask.Left)
			{
				TeleportCameraToMinimapPos(mouseMotion.Position);
			}
		};

		GenerateDynamicMinimap();
	}

	private void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		if (_minimapArea == null || GameHost.Instance == null) return;
		float xRatio = clickPos.X / _minimapArea.Size.X;
		float yRatio = clickPos.Y / _minimapArea.Size.Y;

		float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
		float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);

		var camera = GameHost.Instance.GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(worldX, camera.GlobalPosition.Y, worldZ);
		}
	}

	private void UpdateMinimapIndicator()
	{
		if (_cameraIndicator == null || _minimapArea == null || GameHost.Instance == null) return;
		var camera = GameHost.Instance.GetViewport().GetCamera3D();
		if (camera == null) return;

		float worldX = camera.GlobalPosition.X;
		float worldZ = camera.GlobalPosition.Z;

		float xRatio = (worldX / 250f) + 0.5f;
		float yRatio = (worldZ / 250f) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		float xPos = xRatio * _minimapArea.Size.X - (_cameraIndicator.Size.X / 2f);
		float yPos = yRatio * _minimapArea.Size.Y - (_cameraIndicator.Size.Y / 2f);

		_cameraIndicator.Position = new Vector2(xPos, yPos);
	}

	public void RegenerateMinimap()
	{
		GenerateDynamicMinimap();
	}

	private async void GenerateDynamicMinimap()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (_minimapArea == null) return;
		var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
		if (minimapBg == null) return;

		try
		{
			var viewport = new SubViewport();
			viewport.Size = new Vector2I(256, 256);
			viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			AddChild(viewport);

			var camera = new Camera3D();
			camera.Projection = Camera3D.ProjectionType.Orthogonal;
			camera.Size = 250f;
			camera.Far = 200f;
			camera.Position = new Vector3(0, 100, 0);
			camera.RotationDegrees = new Vector3(-90, 0, 0);
			viewport.AddChild(camera);

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			var texture = viewport.GetTexture();
			if (texture != null)
			{
				var img = texture.GetImage();
				if (img != null)
				{
					var imgTexture = ImageTexture.CreateFromImage(img);
					minimapBg.Texture = imgTexture;
				}
			}

			viewport.QueueFree();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
	}

	public void SelectCategory(string category)
	{
		_currentCategory = category;
		_categoryFiles.Clear();
		if (_optCategoryItems != null)
		{
			_optCategoryItems.Clear();
		}

		string path = "";
		GameHost.EditorTool tool = GameHost.EditorTool.None;

		switch (category)
		{
			case "Characters":
				path = "res://Assets/3d/Characters";
				tool = GameHost.EditorTool.PlaceUnit;
				break;
			case "Buildings":
				path = "res://Assets/3d/Buildings";
				tool = GameHost.EditorTool.PlaceUnit;
				break;
			case "Environment":
				path = "res://Assets/3d/Environment";
				tool = GameHost.EditorTool.PlaceProp;
				break;
			case "Props":
				path = "res://Assets/3d/Props";
				tool = GameHost.EditorTool.PlaceProp;
				break;
			case "Decals":
				path = "res://Assets/2d/Decals";
				tool = GameHost.EditorTool.PlaceDecal;
				break;
		}

		bool is3D = category == "Characters" || category == "Buildings" || category == "Environment" || category == "Props";

		using (var dir = DirAccess.Open(path))
		{
			if (dir != null)
			{
				dir.ListDirBegin();
				string fileName = dir.GetNext();
				while (fileName != "")
				{
					if (!dir.CurrentIsDir() && !fileName.EndsWith(".import"))
					{
						if (is3D && fileName.EndsWith(".glb"))
						{
							_categoryFiles.Add(fileName);
						}
						else if (!is3D && (fileName.EndsWith(".png") || fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg")))
						{
							_categoryFiles.Add(fileName);
						}
					}
					fileName = dir.GetNext();
				}
			}
		}

		_categoryFiles.Sort();

		if (_optCategoryItems != null)
		{
			foreach (var file in _categoryFiles)
			{
				string cleanName = System.IO.Path.GetFileNameWithoutExtension(file).Replace("_", " ");
				cleanName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);
				_optCategoryItems.AddItem(cleanName);
			}
		}

		if (_categoryFiles.Count > 0)
		{
			if (_optCategoryItems != null)
			{
				_optCategoryItems.Selected = 0;
			}
			string selectedFile = _categoryFiles[0];
			string placeId = selectedFile;
			if (category == "Characters" || category == "Buildings" || category == "Environment" || category == "Props")
			{
				placeId = $"{path}/{selectedFile}";
			}
			
			Button categoryBtn = category switch
			{
				"Characters" => _btnChars,
				"Buildings" => _btnBuilds,
				"Environment" => _btnEnv,
				"Props" => _btnProps,
				"Decals" => _btnDecals,
				_ => null
			};
			
			TriggerToolSelection(tool, categoryBtn, placeId);
		}
	}

	public void UpdateBrushShapeExternal(bool isSquare)
	{
		if (_btnBrushShape != null)
		{
			_btnBrushShape.Text = isSquare ? "🔳 BRUSH: SQUARE" : "⚪ BRUSH: CIRCLE";
		}
		ShowFeedback(isSquare ? "Brush Shape: Square" : "Brush Shape: Circle");
	}

	public void UpdateRotationExternal(float angle)
	{
		if (_btnToggleRotate != null)
		{
			_btnToggleRotate.Text = $"🔄 ROTATION: {angle}°";
		}
		ShowFeedback($"Placement Rotation: {angle}°");
	}

	public void UpdateScaleExternal(float scale)
	{
		if (_btnToggleScale != null)
		{
			_btnToggleScale.Text = $"📏 SCALE: {scale:F1}x";
		}
		ShowFeedback($"Placement Scale: {scale}x");
	}

	public void UpdateBrushSizeExternal(float size)
	{
		if (_sldBrushSize != null)
		{
			_sldBrushSize.Value = size;
		}
		ShowFeedback($"Brush Size: {size:F1}");
	}

	public void UpdateCameraAngleButtonText(bool isTopDown)
	{
		if (_btnCameraAngle != null)
		{
			_btnCameraAngle.Text = isTopDown ? "📐 Angle: TopDown" : "📐 Angle: Tilt";
		}
		ShowFeedback(isTopDown ? "Camera set to Top-Down View" : "Camera set to In-game Tilt View");
	}

	public void UpdateBrushStrengthExternal(float strength)
	{
		if (_sldBrushStrength != null)
		{
			_sldBrushStrength.Value = strength;
		}
		ShowFeedback($"Brush Strength: {strength:F1}");
	}

	public void UpdateBlockModeExternal(bool enabled)
	{
		if (_chkBlockMode != null)
		{
			_chkBlockMode.SetBlockSignals(true);
			_chkBlockMode.ButtonPressed = enabled;
			_chkBlockMode.SetBlockSignals(false);
		}
		ShowFeedback(enabled ? "Block Mode: Enabled" : "Block Mode: Disabled");
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
		Button targetBtn = tool switch
		{
			GameHost.EditorTool.Raise => _btnRaise,
			GameHost.EditorTool.Lower => _btnLower,
			GameHost.EditorTool.Smooth => _btnSmooth,
			GameHost.EditorTool.Flatten => _btnFlatten,
			GameHost.EditorTool.Cliff => _btnCliff,
			GameHost.EditorTool.PaintGrass => _btnTextureBrush,
			GameHost.EditorTool.PaintDirt => _btnTextureBrush,
			GameHost.EditorTool.PaintRock => _btnTextureBrush,
			GameHost.EditorTool.PaintSand => _btnTextureBrush,
			GameHost.EditorTool.FloodFill => _btnFloodFill,
			GameHost.EditorTool.SelectArea => _btnSelectArea,
			GameHost.EditorTool.PasteArea => _btnSelectArea,
			GameHost.EditorTool.PlaceUnit => _btnChars,
			GameHost.EditorTool.PlaceProp => _btnProps,
			GameHost.EditorTool.PlacePropClump => _btnClumpBrush,
			GameHost.EditorTool.PlaceDecal => _btnDecals,
			GameHost.EditorTool.DeleteObject => _btnDeleteObject,
			GameHost.EditorTool.SelectMove => _btnSelectMove,
			GameHost.EditorTool.Eyedropper => _btnEyedropper,
			GameHost.EditorTool.Noise => _btnNoise,
			GameHost.EditorTool.Ramp => _btnRamp,
			_ => null
		};

		if (tool == GameHost.EditorTool.None)
		{
			TriggerToolSelection(GameHost.EditorTool.None, null);
			ShowFeedback("Tool Cancelled");
			return;
		}

		if (targetBtn != null)
		{
			if (tool == GameHost.EditorTool.PlaceUnit)
			{
				SelectCategory("Characters");
				return;
			}
			else if (tool == GameHost.EditorTool.PlaceProp)
			{
				SelectCategory("Props");
				return;
			}
			else if (tool == GameHost.EditorTool.PlaceDecal)
			{
				SelectCategory("Decals");
				return;
			}
			string placeId = "";
			TriggerToolSelection(tool, targetBtn, placeId);
			ShowFeedback($"Selected Tool: {tool.ToString().ToUpper()}");
		}
	}

	private void ShowFeedback(string text)
	{
		if (_feedbackLabel == null) return;
		
		_feedbackLabel.Text = text;
		_feedbackLabel.Modulate = new Color(1, 1, 1, 1);
		
		var timer = GetTree().CreateTimer(2.0f);
		timer.Timeout += () =>
		{
			var tween = CreateTween();
			tween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, 0.5f);
		};
	}

	public override void _Process(double delta)
	{
		if (GameHost.Instance != null && _statusLabel != null)
		{
			var mousePos = GetViewport().GetMousePosition();
			var hit = GameHost.Instance.Call("RaycastFromMouse", mousePos).AsGodotDictionary();
			if (hit != null && hit.ContainsKey("position"))
			{
				Vector3 pos = hit["position"].AsVector3();
				string toolName = GameHost.Instance.ActiveEditorTool.ToString().ToUpper();
				if (!string.IsNullOrEmpty(GameHost.Instance.ActivePlaceId)) 
					toolName += $" ({GameHost.Instance.ActivePlaceId.ToUpper()})";
				
				_statusLabel.Text = $"ACTIVE TOOL: {toolName} | Pos: {pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}";

				if (GameHost.Instance.ActiveEditorTool == GameHost.EditorTool.PaintPathing && GameHost.Instance.GroundTerrain != null)
				{
					var terrain = GameHost.Instance.GroundTerrain;
					float fx = pos.X / terrain.Spacing + (terrain.Width - 1) / 2.0f;
					float fz = pos.Z / terrain.Spacing + (terrain.Depth - 1) / 2.0f;
					int cx = Mathf.Clamp((int)Mathf.Round(fx), 0, terrain.Width - 1);
					int cz = Mathf.Clamp((int)Mathf.Round(fz), 0, terrain.Depth - 1);

					if (terrain.PathingCodes != null)
					{
						int code = terrain.PathingCodes[cx, cz];
						var layers = new List<string>();
						if ((code & EditableTerrain.PATHING_GROUND) != 0) layers.Add("Ground");
						if ((code & EditableTerrain.PATHING_FLYING) != 0) layers.Add("Flying");
						if ((code & EditableTerrain.PATHING_SHALLOW_WATER) != 0) layers.Add("Shallow Water");
						if ((code & EditableTerrain.PATHING_DEEP_WATER) != 0) layers.Add("Deep Water");
						if ((code & EditableTerrain.PATHING_UNPATHABLE) != 0) layers.Add("Unpathable");

						string layersStr = layers.Count > 0 ? string.Join(", ", layers) : "None";
						_statusLabel.Text += $" | Path: {layersStr}";
					}
				}
			}
		}
		UpdateMinimapIndicator();
	}

	private void CreateInspectorPanel()
	{
		var rightVBox = GetNode<VBoxContainer>("RightPillar/VBox");
		_inspectorPanel = new PanelContainer();
		_inspectorPanel.Name = "InspectorPanel";
		_inspectorPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_inspectorPanel.Visible = false;
		rightVBox.AddChild(_inspectorPanel);
		rightVBox.MoveChild(_inspectorPanel, _lblInfoText.GetIndex());

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);
		_inspectorPanel.AddChild(vbox);

		_lblInspectorTitle = new Label();
		UIStyle.ApplyTitle(_lblInspectorTitle, "INSPECTOR", 14);
		_lblInspectorTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		vbox.AddChild(_lblInspectorTitle);

		_lblInspectorPos = new Label();
		_lblInspectorPos.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblInspectorPos.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(_lblInspectorPos);

		var rotHBox = new HBoxContainer();
		rotHBox.AddThemeConstantOverride("separation", 5);
		vbox.AddChild(rotHBox);

		var lblRot = new Label();
		lblRot.Text = "Rotate:";
		lblRot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblRot.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblRot.AddThemeFontSizeOverride("font_size", 12);
		rotHBox.AddChild(lblRot);

		_btnInspectorRotLeft = new Button();
		_btnInspectorRotLeft.Set("icon_max_width", 0);
		SetupButton(_btnInspectorRotLeft, "🔄 -45°", () => RotateSelectedObject(-45f), 10, "Rotate selected object 45 degrees counter-clockwise");
		rotHBox.AddChild(_btnInspectorRotLeft);

		_btnInspectorRotRight = new Button();
		_btnInspectorRotRight.Set("icon_max_width", 0);
		SetupButton(_btnInspectorRotRight, "🔄 +45°", () => RotateSelectedObject(45f), 10, "Rotate selected object 45 degrees clockwise");
		rotHBox.AddChild(_btnInspectorRotRight);

		var scaleHBox = new HBoxContainer();
		scaleHBox.AddThemeConstantOverride("separation", 5);
		vbox.AddChild(scaleHBox);

		var lblScale = new Label();
		lblScale.Text = "Scale:";
		lblScale.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblScale.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblScale.AddThemeFontSizeOverride("font_size", 12);
		scaleHBox.AddChild(lblScale);

		_btnInspectorScaleDown = new Button();
		_btnInspectorScaleDown.Set("icon_max_width", 0);
		SetupButton(_btnInspectorScaleDown, "➖", () => ScaleSelectedObject(-0.1f), 11, "Scale selected object down");
		scaleHBox.AddChild(_btnInspectorScaleDown);

		_btnInspectorScaleUp = new Button();
		_btnInspectorScaleUp.Set("icon_max_width", 0);
		SetupButton(_btnInspectorScaleUp, "➕", () => ScaleSelectedObject(0.1f), 11, "Scale selected object up");
		scaleHBox.AddChild(_btnInspectorScaleUp);

		_btnInspectorScaleReset = new Button();
		_btnInspectorScaleReset.Set("icon_max_width", 0);
		SetupButton(_btnInspectorScaleReset, "🎯", () => ResetScaleSelectedObject(), 11, "Reset selected object scale to 1.0x");
		scaleHBox.AddChild(_btnInspectorScaleReset);

		_chkInspectorIsEnemy = new CheckBox();
		_chkInspectorIsEnemy.Text = "Enemy (Orc)";
		_chkInspectorIsEnemy.TooltipText = "Toggle selected unit team faction alignment";
		UIStyle.ApplyCheckboxStyle(_chkInspectorIsEnemy);
		_chkInspectorIsEnemy.Toggled += (toggled) => ToggleSelectedObjectTeam(toggled);
		vbox.AddChild(_chkInspectorIsEnemy);

		_btnInspectorAlignToGround = new Button();
		_btnInspectorAlignToGround.Set("icon_max_width", 0);
		SetupButton(_btnInspectorAlignToGround, "📐 ALIGN TO GROUND", () => AlignSelectedObjectToGround(), 11, "Snap selected object height to align with terrain surface");
		vbox.AddChild(_btnInspectorAlignToGround);

		_btnInspectorDelete = new Button();
		_btnInspectorDelete.Set("icon_max_width", 0);
		SetupButton(_btnInspectorDelete, "🗑️ DELETE OBJECT", () => DeleteSelectedObject(), 11, "Remove selected object from map (Delete/Backspace)");
		_btnInspectorDelete.AddThemeColorOverride("font_color", new Color(0.95f, 0.25f, 0.25f));
		vbox.AddChild(_btnInspectorDelete);
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
				_chkInspectorIsEnemy.Visible = true;
				_chkInspectorIsEnemy.SetBlockSignals(true);
				_chkInspectorIsEnemy.ButtonPressed = unit.IsEnemy;
				_chkInspectorIsEnemy.SetBlockSignals(false);
			}
			else if (selected is Prop3D prop)
			{
				typeStr = "PROP";
				nameStr = prop.PropId.ToUpper();
				_chkInspectorIsEnemy.Visible = false;
			}
			else if (selected is Decal decal)
			{
				typeStr = "DECAL";
				nameStr = "DECAL";
				_chkInspectorIsEnemy.Visible = false;
			}
			_lblInspectorTitle.Text = $"SELECTED: {nameStr}\n[{typeStr}]";
			_lblInspectorPos.Text = $"Pos: {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}\nRot: {rot.Y:F1}° | Scale: {scale.X:F2}x";
		}
		else
		{
			_lblInfoText.Visible = true;
			_inspectorPanel.Visible = false;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = false;
			}
			TriggerToolSelection(GameHost.Instance.ActiveEditorTool, _activeToolButton, GameHost.Instance.ActivePlaceId);
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
			ShowFeedback($"Rotated Object to {newRot.Y}°");
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
			ShowFeedback($"Scaled Object to {newScaleVal:F1}x");
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
			ShowFeedback("Reset Object scale to 1.0x");
		}
	}

	public void ToggleSelectedObjectTeam(bool isEnemy)
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected) && selected is Unit3D unit)
		{
			bool oldIsEnemy = unit.IsEnemy;
			if (oldIsEnemy == isEnemy) return;
			var action = new ObjectTransformAction(
				unit,
				unit.Position, unit.Position,
				unit.RotationDegrees, unit.RotationDegrees,
				unit.Scale, unit.Scale,
				oldIsEnemy, isEnemy
			);
			GameHost.Instance.SetUnitTeamExternal(unit, isEnemy);
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(isEnemy ? "Aligned Unit to Enemy" : "Aligned Unit to Player");
		}
	}

	public void AlignSelectedObjectToGround()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldPos = node3D.Position;
			Vector3 newPos = oldPos;
			newPos.Y = GameHost.Instance.GetTerrainHeightAt(newPos);
			if (Mathf.Abs(newPos.Y - oldPos.Y) > 0.01f)
			{
				bool isUnit = selected is Unit3D;
				bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
				var action = new ObjectTransformAction(
					node3D,
					oldPos, newPos,
					node3D.RotationDegrees, node3D.RotationDegrees,
					node3D.Scale, node3D.Scale,
					isEnemy, isEnemy
				);
				node3D.Position = newPos;
				if (selected is Unit3D unit && GameHost.Instance.EcsWorld.IsAlive(unit.Entity))
				{
					GameHost.Instance.EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(newPos.X, newPos.Y, newPos.Z)));
				}
				EditorHistoryManager.RecordAction(action);
				UpdateSelectedObjectInfo();
				ShowFeedback("Aligned Object to Ground");
			}
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
				ShowFeedback("Deleted Selected Object");
			}
		}
	}

	public bool IsMouseOverUI(Vector2 mousePos)
	{
		if (_isDraggingSlider)
		{
			return true;
		}
		if (_optSkybox != null && _optSkybox.GetPopup() != null && _optSkybox.GetPopup().Visible)
		{
			return true;
		}
		if (_optCategoryItems != null && _optCategoryItems.GetPopup() != null && _optCategoryItems.GetPopup().Visible)
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
		if (_moduleBar != null && _moduleBar.Visible && _moduleBar.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		return false;
	}

	public void CycleTextureSwatch(bool forward)
	{
		int currentIndex = 0;
		if (_activeToolButton != null && _activeToolButton.Name.ToString().StartsWith("Swatch"))
		{
			string name = _activeToolButton.Name.ToString();
			if (int.TryParse(name.Substring(6), out int parsed))
			{
				currentIndex = parsed;
			}
		}
		int nextIndex = currentIndex;
		if (forward)
		{
			nextIndex = nextIndex % 12 + 1;
		}
		else
		{
			nextIndex = (nextIndex - 2 + 12) % 12 + 1;
		}
		var nextSwatch = _swatchButtons[nextIndex - 1];
		if (nextSwatch != null)
		{
			nextSwatch.EmitSignal(Button.SignalName.Pressed);
		}
	}

	public void UpdateFlattenHeightExternal(float height)
	{
		if (_sldFlattenHeight != null)
		{
			_sldFlattenHeight.Value = Mathf.Clamp(height, _sldFlattenHeight.MinValue, _sldFlattenHeight.MaxValue);
		}
		if (_lblFlattenHeightValue != null)
		{
			_lblFlattenHeightValue.Text = height.ToString("F1") + " m";
		}
	}

	private void ShowConfirmationDialog(string message, Action onConfirm)
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.AnchorsPreset = (int)LayoutPreset.FullRect;
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = new CenterContainer();
		center.AnchorsPreset = (int)LayoutPreset.FullRect;
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, "⚠️ CONFIRMATION REQUIRED", 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var lblMsg = new Label();
		lblMsg.Text = message;
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
		SetupButton(btnConfirm, "YES", () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		}, 13);
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		hbox.AddChild(btnConfirm);

		var btnCancel = new Button();
		btnCancel.Set("icon_max_width", 0);
		SetupButton(btnCancel, "NO", () =>
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
		_helpOverlayPanel.AnchorsPreset = (int)LayoutPreset.FullRect;
		AddChild(_helpOverlayPanel);

		var center = new CenterContainer();
		center.AnchorsPreset = (int)LayoutPreset.FullRect;
		_helpOverlayPanel.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(650, 480);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, "📜 RTS MAP EDITOR REFERENCE MANUAL", 18);
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

		AddHelpSectionHeader(grid, "PRIMARY MODULE SWITCHING");
		AddHelpShortcutRow(grid, "F1 / Ctrl+1", "Terrain Modeling Module");
		AddHelpShortcutRow(grid, "F2 / Ctrl+2", "Texturing & Decoration Module");
		AddHelpShortcutRow(grid, "F3 / Ctrl+3", "Object Placement Module");

		AddHelpSectionHeader(grid, "CAMERA CONTROLS");
		AddHelpShortcutRow(grid, "W, A, S, D / Arrows", "Pan map camera");
		AddHelpShortcutRow(grid, "Mouse Scroll", "Zoom camera in / out");
		AddHelpShortcutRow(grid, "Middle Mouse Drag", "Pan camera by dragging");
		AddHelpShortcutRow(grid, "Shift + Middle Drag", "Rotate map camera view");
		AddHelpShortcutRow(grid, "Comma (,) / Period (.)", "Rotate camera 90 degrees");
		AddHelpShortcutRow(grid, "Spacebar", "Center camera view on castle");

		AddHelpSectionHeader(grid, "EDITOR TOOLS");
		AddHelpShortcutRow(grid, "1, 2, 3, 4, 5", "Raise, Lower, Smooth, Flatten, Cliff");
		AddHelpShortcutRow(grid, "6, 7", "Texture Painter, Place Decals");
		AddHelpShortcutRow(grid, "8, 9", "Add Unit Palette, Add Prop Palette");
		AddHelpShortcutRow(grid, "0, Q", "Object Eraser Tool, Select / Move Tool");
		AddHelpShortcutRow(grid, "I, N", "Eyedropper Picker, Roughen (Noise) Tool");

		AddHelpSectionHeader(grid, "SCULPTING / PLACEMENT SETTINGS");
		AddHelpShortcutRow(grid, "Left Bracket [ / Right Bracket ]", "Increase / decrease brush size");
		AddHelpShortcutRow(grid, "Minus - / Equals =", "Increase / decrease brush strength");
		AddHelpShortcutRow(grid, "Shift + Mouse Scroll", "Quickly change brush size");
		AddHelpShortcutRow(grid, "Ctrl + Mouse Scroll", "Quickly change brush strength");
		AddHelpShortcutRow(grid, "B Key", "Toggle brush shape (Circle / Square)");
		AddHelpShortcutRow(grid, "V Key", "Toggle terrain alignment grid lines");
		AddHelpShortcutRow(grid, "M Key", "Toggle blocky sculpt mode");
		AddHelpShortcutRow(grid, "Tab / Shift + Tab", "Cycle selected texture painted color");
		AddHelpShortcutRow(grid, "R Key", "Rotate placement/selected object by 45°");
		AddHelpShortcutRow(grid, "Shift + R / Scroll (in Select)", "Rotate placement/selected object by 15°");
		AddHelpShortcutRow(grid, "S Key", "Cycle placement/selected object scale size");
		AddHelpShortcutRow(grid, "Ctrl + S / Scroll (in Select)", "Fine-tune object scale size");
		AddHelpShortcutRow(grid, "G Key", "Align selected object height to ground");
		AddHelpShortcutRow(grid, "Ctrl + G", "Toggle alignment grid snap placement");
		AddHelpShortcutRow(grid, "F Key", "Toggle selected unit faction (Alliance/Orc)");
		AddHelpShortcutRow(grid, "Ctrl + D", "Duplicate / clone selected object");
		AddHelpShortcutRow(grid, "Delete / Backspace", "Delete / erase selected object");

		AddHelpSectionHeader(grid, "GENERAL OPERATIONS");
		AddHelpShortcutRow(grid, "Ctrl + Z / Ctrl + Y", "Undo / Redo editor actions");
		AddHelpShortcutRow(grid, "Ctrl + S / Ctrl + O", "Save Map File / Load Map File");
		AddHelpShortcutRow(grid, "Ctrl + P", "Save & Publish Map");
		AddHelpShortcutRow(grid, "F6 Key", "Import terrain from minimap image");
		AddHelpShortcutRow(grid, "Escape Key", "Clear selection or cancel active tool");

		var btnClose = new Button();
		btnClose.Set("icon_max_width", 0);
		SetupButton(btnClose, "CLOSE MANUAL", () =>
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

	private void AddSliderRow(GridContainer grid, string title, int initialValue, Action<int> onValueChanged)
	{
		var lblName = new Label();
		lblName.Text = title;
		lblName.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblName.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lblName);

		var sld = new HSlider();
		sld.MinValue = 1;
		sld.MaxValue = 10;
		sld.Step = 1;
		sld.Value = initialValue;
		sld.CustomMinimumSize = new Vector2(180, 0);
		sld.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		grid.AddChild(sld);

		var lblVal = new Label();
		lblVal.Text = initialValue.ToString();
		lblVal.CustomMinimumSize = new Vector2(30, 0);
		lblVal.HorizontalAlignment = HorizontalAlignment.Right;
		lblVal.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		lblVal.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lblVal);

		sld.ValueChanged += (double value) =>
		{
			int val = (int)value;
			lblVal.Text = val.ToString();
			onValueChanged(val);
		};
	}

	private void ShowGenerationDialog()
	{
		var overlay = new ColorRect();
		overlay.Name = "GenerationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.AnchorsPreset = (int)LayoutPreset.FullRect;
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(420, 480);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = new CenterContainer();
		center.AnchorsPreset = (int)LayoutPreset.FullRect;
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, "🎲 RANDOM MAP GENERATOR", 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var grid = new GridContainer();
		grid.Columns = 3;
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 8);
		vbox.AddChild(grid);

		AddSliderRow(grid, "Hills Density", _genHillsDensity, (val) => _genHillsDensity = val);
		AddSliderRow(grid, "Terrain Roughness", _genTerrainRoughness, (val) => _genTerrainRoughness = val);
		AddSliderRow(grid, "Mountain Height", _genMountainHeight, (val) => _genMountainHeight = val);
		AddSliderRow(grid, "Choke Point Width", _genChokeWidth, (val) => _genChokeWidth = val);
		AddSliderRow(grid, "Water Level", _genWaterLevel, (val) => _genWaterLevel = val);
		AddSliderRow(grid, "Tree Clump Density", _genTreeDensity, (val) => _genTreeDensity = val);
		AddSliderRow(grid, "Resource Abundance", _genResourceAbundance, (val) => _genResourceAbundance = val);
		AddSliderRow(grid, "Decorative Prop Density", _genDecoDensity, (val) => _genDecoDensity = val);

		var seedHBox = new HBoxContainer();
		seedHBox.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(seedHBox);

		var lblSeed = new Label();
		lblSeed.Text = "Map Seed:";
		lblSeed.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblSeed.AddThemeFontSizeOverride("font_size", 12);
		seedHBox.AddChild(lblSeed);

		var txtSeed = new LineEdit();
		txtSeed.Text = _genSeed;
		txtSeed.CustomMinimumSize = new Vector2(150, 30);
		txtSeed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		txtSeed.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		txtSeed.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		txtSeed.AddThemeFontSizeOverride("font_size", 12);
		txtSeed.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		txtSeed.TextChanged += (newText) =>
		{
			_genSeed = newText;
		};
		seedHBox.AddChild(txtSeed);

		var btnRoll = new Button();
		btnRoll.Set("icon_max_width", 0);
		SetupButton(btnRoll, "🎲 ROLL", () =>
		{
			_genSeed = new Random().Next(100000, 999999).ToString();
			txtSeed.Text = _genSeed;
		}, 11, "Generate a new random seed");
		btnRoll.CustomMinimumSize = new Vector2(70, 30);
		seedHBox.AddChild(btnRoll);

		var btnHBox = new HBoxContainer();
		btnHBox.AddThemeConstantOverride("separation", 20);
		btnHBox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(btnHBox);

		var btnGen = new Button();
		btnGen.Set("icon_max_width", 0);
		SetupButton(btnGen, "⚙️ GENERATE", () =>
		{
			overlay.QueueFree();
			if (GameHost.Instance != null)
			{
				MapGenerator.GenerateMap(
					GameHost.Instance,
					_genHillsDensity,
					_genTerrainRoughness,
					_genMountainHeight,
					_genChokeWidth,
					_genWaterLevel,
					_genTreeDensity,
					_genResourceAbundance,
					_genDecoDensity,
					_genSeed
				);
				GenerateDynamicMinimap();
			}
		}, 13, "Generate the random map");
		btnGen.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		btnHBox.AddChild(btnGen);

		var btnCancel = new Button();
		btnCancel.Set("icon_max_width", 0);
		SetupButton(btnCancel, "❌ CLOSE", () =>
		{
			overlay.QueueFree();
		}, 13, "Close dialog without generating");
		btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		btnHBox.AddChild(btnCancel);
	}

	public void ImportTerrainFromMinimapDialog()
	{
		var err = DisplayServer.FileDialogShow(
			"Select Minimap Image to Import Terrain",
			ProjectSettings.GlobalizePath("res://"),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new string[] { "*.png", "*.jpg", "*.jpeg", "*.webp" },
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

		var img = Image.LoadFromFile(selectedPath);
		if (img == null) return;

		int width = GameHost.Instance.GroundTerrain.Width;
		int depth = GameHost.Instance.GroundTerrain.Depth;

		float[,] heights = new float[width, depth];
		Color[,] colors = new Color[width, depth];
		bool[,] isTreeColored = new bool[width, depth];

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				float srcX = (gx / (float)(width - 1)) * (img.GetWidth() - 1);
				float srcZ = (gz / (float)(depth - 1)) * (img.GetHeight() - 1);

				int x0 = (int)MathF.Floor(srcX);
				int x1 = Math.Min(x0 + 1, img.GetWidth() - 1);
				int z0 = (int)MathF.Floor(srcZ);
				int z1 = Math.Min(z0 + 1, img.GetHeight() - 1);

				float tx = srcX - x0;
				float tz = srcZ - z0;

				Color p00 = img.GetPixel(x0, z0);
				Color p10 = img.GetPixel(x1, z0);
				Color p01 = img.GetPixel(x0, z1);
				Color p11 = img.GetPixel(x1, z1);

				float r = (1f - tx) * (1f - tz) * p00.R + tx * (1f - tz) * p10.R + (1f - tx) * tz * p01.R + tx * tz * p11.R;
				float g = (1f - tx) * (1f - tz) * p00.G + tx * (1f - tz) * p10.G + (1f - tx) * tz * p01.G + tx * tz * p11.G;
				float b = (1f - tx) * (1f - tz) * p00.B + tx * (1f - tz) * p10.B + (1f - tx) * tz * p01.B + tx * tz * p11.B;

				string type = "grass";
				if (b > r + 0.06f && b > g + 0.06f)
				{
					type = "water";
				}
				else if (g > r + 0.04f && g > b + 0.04f)
				{
					if (r < 0.4f && g < 0.5f && b < 0.4f)
					{
						type = "forest";
					}
					else
					{
						type = "grass";
					}
				}
				else if (r > g + 0.06f && r > b + 0.1f && g > b)
				{
					type = "cliff";
				}
				else if (MathF.Abs(r - g) < 0.08f && MathF.Abs(g - b) < 0.08f && MathF.Abs(r - b) < 0.08f && r > 0.2f)
				{
					type = "stone";
				}
				else
				{
					float max = Math.Max(r, Math.Max(g, b));
					if (max == b) type = "water";
					else if (max == g) type = "grass";
					else if (max == r && g > b) type = "cliff";
					else type = "stone";
				}

				float h = 0.0f;
				Color c = new Color(0.2f, 0.6f, 0.2f);
				if (type == "water")
				{
					h = -2.0f;
					c = new Color(0.0f, 0.33f, 0.7f);
				}
				else if (type == "grass")
				{
					h = 0.0f;
					c = new Color(0.2f, 0.6f, 0.2f);
				}
				else if (type == "forest")
				{
					h = 0.0f;
					c = new Color(0.16f, 0.48f, 0.16f);
					isTreeColored[gx, gz] = true;
				}
				else if (type == "cliff")
				{
					h = 4.0f;
					c = new Color(0.54f, 0.35f, 0.17f);
				}
				else if (type == "stone")
				{
					h = 0.0f;
					c = new Color(0.5f, 0.5f, 0.5f);
				}

				heights[gx, gz] = h;
				colors[gx, gz] = c;
			}
		}

		float[,] smoothedHeights = new float[width, depth];
		int blurRadius = 2;
		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				float sum = 0f;
				int count = 0;
				for (int dz = -blurRadius; dz <= blurRadius; dz++)
				{
					for (int dx = -blurRadius; dx <= blurRadius; dx++)
					{
						int nx = gx + dx;
						int nz = gz + dz;
						if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
						{
							sum += heights[nx, nz];
							count++;
						}
					}
				}
				smoothedHeights[gx, gz] = sum / count;
			}
		}

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				GameHost.Instance.GroundTerrain.Heights[gx, gz] = smoothedHeights[gx, gz];
				GameHost.Instance.GroundTerrain.Colors[gx, gz] = colors[gx, gz];
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

		var random = new Random();
		bool[,] visited = new bool[width, depth];

		float Noise2D(float x, float z)
		{
			float val = MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453123f;
			return val - MathF.Floor(val);
		}

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				if (isTreeColored[gx, gz] && !visited[gx, gz])
				{
					var blob = new List<Vector2I>();
					var queue = new Queue<Vector2I>();
					var start = new Vector2I(gx, gz);
					queue.Enqueue(start);
					visited[gx, gz] = true;

					while (queue.Count > 0)
					{
						var curr = queue.Dequeue();
						blob.Add(curr);

						Vector2I[] neighbors = new Vector2I[]
						{
							new Vector2I(curr.X + 1, curr.Y),
							new Vector2I(curr.X - 1, curr.Y),
							new Vector2I(curr.X, curr.Y + 1),
							new Vector2I(curr.X, curr.Y - 1)
						};

						foreach (var n in neighbors)
						{
							if (n.X >= 0 && n.X < width && n.Y >= 0 && n.Y < depth)
							{
								if (isTreeColored[n.X, n.Y] && !visited[n.X, n.Y])
								{
									visited[n.X, n.Y] = true;
									queue.Enqueue(n);
								}
							}
						}
					}

					int size = blob.Count;
					float baseDensity = 0.15f;
					if (size > 15) baseDensity = 0.35f;
					if (size > 50) baseDensity = 0.55f;

					foreach (var cell in blob)
					{
						if (smoothedHeights[cell.X, cell.Y] >= 0.0f && Noise2D(cell.X, cell.Y) < baseDensity)
						{
							float offsetX = (random.NextSingle() - 0.5f) * 1.5f;
							float offsetZ = (random.NextSingle() - 0.5f) * 1.5f;
							float worldX = (cell.X - (width - 1) / 2.0f) * GameHost.Instance.GroundTerrain.Spacing + offsetX;
							float worldZ = (cell.Y - (depth - 1) / 2.0f) * GameHost.Instance.GroundTerrain.Spacing + offsetZ;
							float hValue = smoothedHeights[cell.X, cell.Y];
							float rot = random.NextSingle() * 360f;
							float scale = 0.8f + random.NextSingle() * 0.4f;

							GameHost.Instance.SpawnPropExternalWithParams("tree", new Vector3(worldX, hValue, worldZ), rot, scale);
						}
					}
				}
			}
		}

		ShowFeedback("Terrain imported from minimap image successfully!");
	}

	private void CycleMirrorMode()
	{
		if (GameHost.Instance == null) return;
		var current = GameHost.Instance.EditorMirrorMode;
		var next = current switch
		{
			GameHost.MirrorMode.None => GameHost.MirrorMode.Vertical,
			GameHost.MirrorMode.Vertical => GameHost.MirrorMode.Horizontal,
			GameHost.MirrorMode.Horizontal => GameHost.MirrorMode.Both,
			GameHost.MirrorMode.Both => GameHost.MirrorMode.None,
			_ => GameHost.MirrorMode.None
		};
		GameHost.Instance.EditorMirrorMode = next;
		UpdateMirrorButtonText();
		ShowFeedback($"Mirroring: {next.ToString().ToUpper()}");
	}

	public void UpdateMirrorButtonText()
	{
		if (_btnMirrorMode == null || GameHost.Instance == null) return;
		string modeText = GameHost.Instance.EditorMirrorMode switch
		{
			GameHost.MirrorMode.None => "🪞 MIRROR: NONE",
			GameHost.MirrorMode.Vertical => "🪞 MIRROR: VERTICAL",
			GameHost.MirrorMode.Horizontal => "🪞 MIRROR: HORIZONTAL",
			GameHost.MirrorMode.Both => "🪞 MIRROR: BOTH",
			_ => "🪞 MIRROR: NONE"
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

		_moduleBar = new HBoxContainer();
		_moduleBar.Name = "ModuleBar";
		_moduleBar.Alignment = BoxContainer.AlignmentMode.Center;
		_moduleBar.AddThemeConstantOverride("separation", 15);
		AddChild(_moduleBar);
		
		_moduleBar.LayoutMode = 1;
		_moduleBar.SetAnchorsPreset(LayoutPreset.CenterTop);
		_moduleBar.GrowHorizontal = GrowDirection.Both;
		_moduleBar.OffsetLeft = -250;
		_moduleBar.OffsetRight = 250;
		_moduleBar.OffsetTop = 15;
		_moduleBar.OffsetBottom = 55;

		_btnModuleTerrain = new Button();
		_btnModuleTerrain.Set("icon_max_width", 0);
		SetupButton(_btnModuleTerrain, "⛰️ TERRAIN MODELING", () => SwitchModule(EditorModule.Terrain), 12);
		_moduleBar.AddChild(_btnModuleTerrain);

		_btnModulePaint = new Button();
		_btnModulePaint.Set("icon_max_width", 0);
		SetupButton(_btnModulePaint, "🎨 TEXTURE & DECO", () => SwitchModule(EditorModule.TextureDeco), 12);
		_moduleBar.AddChild(_btnModulePaint);

		_btnModuleObjects = new Button();
		_btnModuleObjects.Set("icon_max_width", 0);
		SetupButton(_btnModuleObjects, "💂 OBJECT PLACEMENT", () => SwitchModule(EditorModule.Objects), 12);
		_moduleBar.AddChild(_btnModuleObjects);

		_panelLeft = new PanelContainer();
		_panelLeft.Name = "LeftSlidePanel";
		_panelLeft.CustomMinimumSize = new Vector2(260, 0);
		_panelLeft.LayoutMode = 1;
		_panelLeft.SetAnchorsPreset(LayoutPreset.LeftWide);
		_panelLeft.GrowVertical = GrowDirection.Both;
		_panelLeft.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));
		AddChild(_panelLeft);

		var leftVBox = new VBoxContainer();
		leftVBox.AddThemeConstantOverride("separation", 10);
		_panelLeft.AddChild(leftVBox);

		var leftTitle = new Label();
		leftTitle.Text = "📁 FILE OPERATIONS";
		leftTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		leftTitle.AddThemeFontSizeOverride("font_size", 13);
		leftTitle.HorizontalAlignment = HorizontalAlignment.Center;
		leftVBox.AddChild(leftTitle);

		var btnHeaderFile = new Button();
		btnHeaderFile.Set("icon_max_width", 0);
		StyleAccordionHeader(btnHeaderFile);
		leftVBox.AddChild(btnHeaderFile);

		var contentFile = new VBoxContainer();
		contentFile.AddThemeConstantOverride("separation", 8);
		leftVBox.AddChild(contentFile);
		SetupAccordion(btnHeaderFile, contentFile, "File & Map Actions");

		SafeReparent(_btnPublish, contentFile);
		SafeReparent(_btnSave, contentFile);
		SafeReparent(_btnLoad, contentFile);
		SafeReparent(_btnResetMap, contentFile);
		SafeReparent(_btnGenerateMap, contentFile);
		SafeReparent(_btnImportMinimap, contentFile);

		_btnLeftTab = new Button();
		_btnLeftTab.Name = "LeftTabButton";
		_btnLeftTab.Set("icon_max_width", 0);
		_btnLeftTab.CustomMinimumSize = new Vector2(30, 120);
		_btnLeftTab.LayoutMode = 1;
		_btnLeftTab.SetAnchorsPreset(LayoutPreset.CenterRight);
		_btnLeftTab.GrowHorizontal = GrowDirection.Begin;
		_btnLeftTab.GrowVertical = GrowDirection.Both;
		_btnLeftTab.OffsetLeft = 260;
		_btnLeftTab.OffsetRight = 290;
		_btnLeftTab.OffsetTop = -60;
		_btnLeftTab.OffsetBottom = 60;
		_btnLeftTab.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnLeftTab.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnLeftTab.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnLeftTab.AddThemeFontSizeOverride("font_size", 10);
		_btnLeftTab.Pressed += ToggleLeftPanel;
		_panelLeft.AddChild(_btnLeftTab);

		SetLeftPanelExpanded(false);

		_panelRight = new PanelContainer();
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
		_btnRightTab.AddThemeFontSizeOverride("font_size", 10);
		_btnRightTab.Pressed += ToggleRightPanel;
		_panelRight.AddChild(_btnRightTab);

		var rightScroll = new ScrollContainer();
		rightScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_panelRight.AddChild(rightScroll);

		_accordionContainer = new VBoxContainer();
		_accordionContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_accordionContainer.AddThemeConstantOverride("separation", 10);
		rightScroll.AddChild(_accordionContainer);

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
		SetupAccordion(_btnHeaderBrush, _contentBrush, "Global Brush Properties");

		var brushSizeBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/BrushSizeBox");
		SafeReparent(brushSizeBox, _contentBrush);
		var brushStrengthBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/BrushStrengthBox");
		SafeReparent(brushStrengthBox, _contentBrush);
		SafeReparent(_btnBrushShape, _contentBrush);
		SafeReparent(_chkBlockMode, _contentBrush);
		var stepBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/StepBox");
		SafeReparent(stepBox, _contentBrush);
		var waterHeightBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/WaterHeightBox");
		SafeReparent(waterHeightBox, _contentBrush);

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
		SetupAccordion(_btnHeaderToolSettings, _contentToolSettings, "Tool Settings");

		_containerFlattenSettings = new VBoxContainer();
		_containerFlattenSettings.Name = "ContainerFlatten";
		_contentToolSettings.AddChild(_containerFlattenSettings);
		var flattenBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/FlattenHeightBox");
		SafeReparent(flattenBox, _containerFlattenSettings);

		_containerTextureSettings = new VBoxContainer();
		_containerTextureSettings.Name = "ContainerTexture";
		_containerTextureSettings.AddThemeConstantOverride("separation", 6);
		_contentToolSettings.AddChild(_containerTextureSettings);
		SafeReparent(_lblTerrainTexture, _containerTextureSettings);
		SafeReparent(_lblCliffTexture, _containerTextureSettings);
		var swatchesGrid = GetNodeOrNull<Control>("PanelTextures/VBox/Content/GridSwatches");
		SafeReparent(swatchesGrid, _containerTextureSettings);
		SafeReparent(_btnFillMap, _containerTextureSettings);

		_containerPathingSettings = new VBoxContainer();
		_containerPathingSettings.Name = "ContainerPathing";
		_contentToolSettings.AddChild(_containerPathingSettings);
		var pathingContent = GetNodeOrNull<Control>("PanelPathing/VBox/Content");
		SafeReparent(pathingContent, _containerPathingSettings);

		_containerClumpSettings = new VBoxContainer();
		_containerClumpSettings.Name = "ContainerClump";
		_containerClumpSettings.AddThemeConstantOverride("separation", 6);
		_contentToolSettings.AddChild(_containerClumpSettings);
		
		var clumpTitle = GetNodeOrNull<Label>("PanelEntityPalette/VBox/Content/PalettesVBox/LblClumpTitle");
		SafeReparent(clumpTitle, _containerClumpSettings);
		var densityBox = GetNodeOrNull<Control>("PanelEntityPalette/VBox/Content/RightSettingsVBox/DensityBox");
		SafeReparent(densityBox, _containerClumpSettings);
		var scaleVarBox = GetNodeOrNull<Control>("PanelEntityPalette/VBox/Content/RightSettingsVBox/ScaleVarBox");
		SafeReparent(scaleVarBox, _containerClumpSettings);

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
		SetupAccordion(_btnHeaderPlacement, _contentPlacement, "Placement Config");

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
		SafeReparent(_optCategoryItems, _containerCategorySelector);
		
		SafeReparent(_btnToggleRotate, _contentPlacement);
		SafeReparent(_btnToggleScale, _contentPlacement);
		SafeReparent(_placementRotateBox, _contentPlacement);
		SafeReparent(_placementScaleBox, _contentPlacement);
		SafeReparent(_btnToggleSnap, _contentPlacement);
		SafeReparent(_chkSpawnAsEnemy, _contentPlacement);
		SafeReparent(_chkRandomRotation, _contentPlacement);
		SafeReparent(_chkRandomScale, _contentPlacement);

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
		SetupAccordion(_btnHeaderInspector, _contentInspector, "Selected Object Inspector");
		
		var inspectorVBox = _inspectorPanel?.GetChildOrNull<VBoxContainer>(0);
		if (inspectorVBox != null)
		{
			SafeReparent(inspectorVBox, _contentInspector);
		}

		_accordionViewport = new VBoxContainer();
		_accordionViewport.Name = "AccordionViewport";
		_accordionContainer.AddChild(_accordionViewport);
		_btnHeaderViewport = new Button();
		_btnHeaderViewport.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderViewport);
		_accordionViewport.AddChild(_btnHeaderViewport);
		_contentViewport = new VBoxContainer();
		_contentViewport.AddThemeConstantOverride("separation", 8);
		_accordionViewport.AddChild(_contentViewport);
		SetupAccordion(_btnHeaderViewport, _contentViewport, "Viewport Settings");

		SafeReparent(_btnToggleGrid, _contentViewport);
		SafeReparent(_btnMirrorMode, _contentViewport);
		SafeReparent(_btnToggleCameraBounds, _contentViewport);
		var camBoundsBox = GetNodeOrNull<Control>("PanelEntityPalette/VBox/Content/RightSettingsVBox/CamBoundsBox");
		SafeReparent(camBoundsBox, _contentViewport);

		_accordionNavigation = new VBoxContainer();
		_accordionNavigation.Name = "AccordionNavigation";
		_accordionContainer.AddChild(_accordionNavigation);
		_btnHeaderNavigation = new Button();
		_btnHeaderNavigation.Set("icon_max_width", 0);
		StyleAccordionHeader(_btnHeaderNavigation);
		_accordionNavigation.AddChild(_btnHeaderNavigation);
		_contentNavigation = new VBoxContainer();
		_contentNavigation.AddThemeConstantOverride("separation", 8);
		_accordionNavigation.AddChild(_contentNavigation);
		SetupAccordion(_btnHeaderNavigation, _contentNavigation, "Map Navigation");

		SafeReparent(_minimapFrame, _contentNavigation);
		var zoomContentGrid = GetNodeOrNull<Control>("MiddleRightBox/PanelZoom/VBox/Content");
		SafeReparent(zoomContentGrid, _contentNavigation);

		SetRightPanelExpanded(true);

		_panelObjects = new PanelContainer();
		_panelObjects.Name = "PanelObjects";
		_panelObjects.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_panelObjects.Visible = false;
		_topToolbar.AddChild(_panelObjects);

		var objectsHBox = new HBoxContainer();
		objectsHBox.AddThemeConstantOverride("separation", 8);
		_panelObjects.AddChild(objectsHBox);

		SafeReparent(_btnSelectMove, objectsHBox);
		SafeReparent(_btnDeleteObject, objectsHBox);
		SafeReparent(_btnClumpBrush, objectsHBox);

		var topLeftBox = GetNode<HBoxContainer>("TopLeftBox");
		SafeReparent(_btnUndo, topLeftBox);
		SafeReparent(_btnRedo, topLeftBox);
		SafeReparent(_btnCopy, topLeftBox);
		SafeReparent(_btnPaste, topLeftBox);
		SafeReparent(_btnEyedropper, topLeftBox);
		
		topLeftBox.MoveChild(_btnBackToHub, 0);
		var btnHelp = GetNodeOrNull<Button>("TopLeftBox/BtnHelp");
		if (btnHelp != null) topLeftBox.MoveChild(btnHelp, 1);
		topLeftBox.MoveChild(_btnVSCode, 2);
		topLeftBox.MoveChild(_btnUndo, 3);
		topLeftBox.MoveChild(_btnRedo, 4);
		topLeftBox.MoveChild(_btnCopy, 5);
		topLeftBox.MoveChild(_btnPaste, 6);
		topLeftBox.MoveChild(_btnEyedropper, 7);

		foreach (Control child in contentFile.GetChildren())
		{
			child.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		}

		SwitchModule(EditorModule.Terrain);
	}

	public void SwitchModule(EditorModule module)
	{
		_activeModule = module;
		UpdateModuleSwitchButtons();

		if (_panelTerrain != null) _panelTerrain.Visible = (module == EditorModule.Terrain);
		if (_panelDeco != null) _panelDeco.Visible = (module == EditorModule.TextureDeco);
		if (_panelEnv != null) _panelEnv.Visible = (module == EditorModule.TextureDeco);
		if (_panelObjects != null) _panelObjects.Visible = (module == EditorModule.Objects);

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
				case EditorModule.Objects:
					TriggerToolSelection(GameHost.EditorTool.SelectMove, _btnSelectMove);
					break;
			}
		}
	}

	private void UpdateModuleSwitchButtons()
	{
		if (_btnModuleTerrain == null || _btnModulePaint == null || _btnModuleObjects == null) return;

		var activeStyle = new StyleBoxFlat();
		activeStyle.BgColor = new Color(0.15f, 0.45f, 0.7f, 0.8f);
		activeStyle.BorderColor = UIStyle.ColorCyanGlow;
		activeStyle.SetBorderWidthAll(2);
		activeStyle.CornerRadiusTopLeft = 4;
		activeStyle.CornerRadiusTopRight = 4;
		activeStyle.CornerRadiusBottomLeft = 4;
		activeStyle.CornerRadiusBottomRight = 4;

		_btnModuleTerrain.RemoveThemeStyleboxOverride("normal");
		_btnModulePaint.RemoveThemeStyleboxOverride("normal");
		_btnModuleObjects.RemoveThemeStyleboxOverride("normal");

		if (_activeModule == EditorModule.Terrain) _btnModuleTerrain.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnModuleTerrain.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (_activeModule == EditorModule.TextureDeco) _btnModulePaint.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnModulePaint.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (_activeModule == EditorModule.Objects) _btnModuleObjects.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnModuleObjects.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
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
		_btnLeftTab.Text = expand ? "◀\nF\nI\nL\nE" : "▶\nF\nI\nL\nE";
	}

	private void SetRightPanelExpanded(bool expand)
	{
		_rightPanelExpanded = expand;
		var tween = CreateTween();
		float targetLeft = expand ? -300.0f : 0.0f;
		float targetRight = expand ? 0.0f : 300.0f;
		tween.TweenProperty(_panelRight, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panelRight, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_btnRightTab.Text = expand ? "▶\nT\nO\nO\nL\nS" : "◀\nT\nO\nO\nL\nS";
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
					   tool == GameHost.EditorTool.Flatten ||
					   tool == GameHost.EditorTool.Smooth ||
					   tool == GameHost.EditorTool.Cliff ||
					   tool == GameHost.EditorTool.PaintGrass ||
					   tool == GameHost.EditorTool.PaintDirt ||
					   tool == GameHost.EditorTool.PaintRock ||
					   tool == GameHost.EditorTool.PaintSand ||
					   tool == GameHost.EditorTool.Noise ||
					   tool == GameHost.EditorTool.PaintPathing ||
					   tool == GameHost.EditorTool.PlacePropClump;

		if (_accordionBrush != null)
		{
			_accordionBrush.Visible = isBrush;
			if (_sldBrushStrength != null && _sldBrushStrength.GetParent() is Control strengthParent)
			{
				strengthParent.Visible = (tool != GameHost.EditorTool.PaintPathing && tool != GameHost.EditorTool.PlacePropClump);
			}
			if (_chkBlockMode != null)
			{
				_chkBlockMode.Visible = (tool != GameHost.EditorTool.PaintPathing);
			}
			var stepBox = GetNodeOrNull<Control>("PanelTextures/VBox/Content/SettingsVBox/StepBox");
			if (stepBox != null)
			{
				stepBox.Visible = (tool != GameHost.EditorTool.PaintPathing);
			}
		}

		_containerFlattenSettings.Visible = (tool == GameHost.EditorTool.Flatten);
		_containerTextureSettings.Visible = (tool == GameHost.EditorTool.PaintGrass ||
											 tool == GameHost.EditorTool.PaintDirt ||
											 tool == GameHost.EditorTool.PaintRock ||
											 tool == GameHost.EditorTool.PaintSand ||
											 tool == GameHost.EditorTool.FloodFill);
		_containerPathingSettings.Visible = (tool == GameHost.EditorTool.PaintPathing);
		_containerClumpSettings.Visible = (tool == GameHost.EditorTool.PlacePropClump);
		_containerDecalSettings.Visible = (tool == GameHost.EditorTool.PlaceDecal);
		_containerEyedropperSettings.Visible = (tool == GameHost.EditorTool.Eyedropper);
		_containerPasteSettings.Visible = (tool == GameHost.EditorTool.SelectArea ||
										   tool == GameHost.EditorTool.PasteArea);

		bool isPlacement = (tool == GameHost.EditorTool.PlaceUnit ||
							tool == GameHost.EditorTool.PlaceProp ||
							tool == GameHost.EditorTool.PlacePropClump ||
							tool == GameHost.EditorTool.PlaceDecal);
		
		_containerCategorySelector.Visible = isPlacement;

		bool anyToolSettingVisible = _containerFlattenSettings.Visible ||
									 _containerTextureSettings.Visible ||
									 _containerPathingSettings.Visible ||
									 _containerClumpSettings.Visible ||
									 _containerDecalSettings.Visible ||
									 _containerEyedropperSettings.Visible ||
									 _containerPasteSettings.Visible ||
									 _containerCategorySelector.Visible;

		if (_accordionToolSettings != null)
		{
			_accordionToolSettings.Visible = anyToolSettingVisible;
		}

		bool hasPlacementConfig = isPlacement || (tool == GameHost.EditorTool.SelectMove);
		if (_accordionPlacement != null)
		{
			_accordionPlacement.Visible = hasPlacementConfig;
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
			bool ctrl = keyEvent.CtrlPressed;
			if (keyEvent.Keycode == Key.F1 || (keyEvent.Keycode == Key.Key1 && ctrl))
			{
				SwitchModule(EditorModule.Terrain);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Key.F2 || (keyEvent.Keycode == Key.Key2 && ctrl))
			{
				SwitchModule(EditorModule.TextureDeco);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Key.F3 || (keyEvent.Keycode == Key.Key3 && ctrl))
			{
				SwitchModule(EditorModule.Objects);
				GetViewport().SetInputAsHandled();
			}
		}
	}
}
