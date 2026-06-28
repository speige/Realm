using Godot;
using System;
using System.Collections.Generic;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Services;

public partial class InGameHUD : Control
{
	public static InGameHUD Instance { get; private set; }

	// Resources properties
	public float Gold { get => _gold; set => _gold = value; }
	public float Wood { get => _wood; set => _wood = value; }
	public float Stone { get => _stone; set => _stone = value; }
	public float ResourceGatherMultiplier { get => _resourceGatherMultiplier; set => _resourceGatherMultiplier = value; }

	private float _gold = 500f;
	private float _wood = 400f;
	private float _stone = 200f;
	private float _resourceGatherMultiplier = 1.0f;

	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _resourceContainer;
	private PanelContainer _bottomConsole;
	private PanelContainer _minimapFrame;
	private PanelContainer _portraitFrame;
	private PanelContainer _selectionFrame;
	private PanelContainer _commandFrame;
	private Panel _devPanel;

	// Grid containers & sub-menus
	private GridContainer _commandGrid;
	private bool _isBuildSubMenuOpen = false;
	public bool IsBuildSubMenuOpen => _isBuildSubMenuOpen;

	// Build sub-panel buttons
	private Button _btnBuildCastle;
	private Button _btnBuildTower;
	private Button _btnCancelBuild;

	// Castle training buttons
	private Button _btnTrainFootman;
	private Button _btnTrainArcher;
	private Button _btnTrainPriest;
	private Button _btnTrainWorker;
	private Button _btnBuyPotion;
	private Button _btnUpgradeWeapons;
	private Button _btnUpgradeShields;
	private Button _btnUpgradeHarvesting;
	private Button _btnUsePotion;

	// Tower upgrade buttons
	private Button _btnUpgradeTower;

	private Button _btnSetRally;

	// Spell buttons and VBox containers to dynamically manage their visibility
	private Button _btnFireball;
	private Button _btnLightning;
	private Button _btnHolyLight;
	private VBoxContainer _fireballVBox;
	private VBoxContainer _lightningVBox;
	private VBoxContainer _holyLightVBox;

	// Production Box variables for structures
	private VBoxContainer _productionBox;
	private Label _productionTitle;
	private ProgressBar _productionProgress;
	private Label _productionQueueLabel;
	private HBoxContainer _queueSlotsContainer;

	// Minimap controls variables
	private VBoxContainer _minimapControls;
	private Button _btnZoom;
	private Button _btnToggleTerrain;
	private Button _btnPing;
	private Button _btnCenter;
	private bool _showMinimapTerrain = true;
	public bool ShowMinimapTerrain => _showMinimapTerrain;

	private Label _goldLabel;
	private Label _woodLabel;
	private Label _stoneLabel;

	// Selected units (buttons overlaying the central panel in multi-select)
	private List<Button> _unitButtons = new List<Button>();
	private HBoxContainer _unitsContainer;

	// Dynamic stats / spells / items containers for single-select
	private HBoxContainer _statsContainer;
	private Label _statsLabel;
	private VBoxContainer _spellsBox;
	private VBoxContainer _itemsBox;

	// Marquee drag-box selection variables
	private Vector2 _dragStart;
	private Vector2 _dragEnd;
	private bool _isDrawingDragBox = false;

	// Command buttons
	private Button _btnMove;
	private Button _btnStop;
	private Button _btnHold;
	private Button _btnAttack;
	private Button _btnBuild;
	private Button _btnPatrol; // NEW: Patrol command
	private readonly System.Collections.Generic.List<Button> _dynamicBuildButtons = new();

	// Dev panel
	private Button _btnVictory;
	private Button _btnDefeat;
	private CheckButton _chkCameraToggle;

	// Feedback label
	private Label _feedbackLabel;
	private Control _minimapArea;
	private Control _cameraIndicator;

	// Population / Game clock labels
	private Label _populationLabel;
	private Label _clockLabel;

	private VBoxContainer _customUIPanel;
	private PanelContainer _leaderboardPanel;
	private VBoxContainer _leaderboardContent;
	private Label _leaderboardTitleLabel;
	private PanelContainer _countdownPanel;
	private Label _countdownLabel;

	private float _countdownDuration = 0f;
	private string _countdownText = "";
	private bool _countdownActive = false;

	// Unit description label in portrait frame
	private Label _unitDescLabel;

	// RTS HUD additions
	private HBoxContainer _controlGroupsContainer;
	private Button _btnSelectIdle;
	private Button _btnSelectArmy;

	// Idle alert badge pulsing
	private float _idlePulseTimer = 0f;
	private int _lastIdleCount = 0;

	// Hotkey reference panel
	private PanelContainer _hotkeyPanel;
	private bool _hotkeyPanelVisible = false;

	// Army composition label (shown in portrait during multi-select)
	private Label _armyCompositionLabel;

	// Income rate tooltip timer
	private float _incomeUpdateTimer = 0f;
	private float _goldPerSec = 1.5f;
	private float _woodPerSec = 1.0f;
	private float _stonePerSec = 0.8f;

	// Spell cooldown progress bars
	private ProgressBar _fireballCooldownBar;
	private ProgressBar _lightningCooldownBar;
	private ProgressBar _holyLightCooldownBar;

	// Reference to 3D Camera
	private Camera3D _camera3D;

	// Chat system variables
	private PanelContainer _chatPanel;
	private LineEdit _chatInput;
	private RichTextLabel _chatLog;
	private bool _isChatActive = false;
	public bool IsChatActive => _isChatActive;

	public override void _Ready()
	{
		Instance = this;

		// Bind Panels
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_resourceContainer = GetNode<PanelContainer>("ResourceContainer");
		_bottomConsole = GetNode<PanelContainer>("BottomConsole");
		_minimapFrame = GetNode<PanelContainer>("BottomConsole/HBox/MinimapFrame");
		_portraitFrame = GetNode<PanelContainer>("BottomConsole/HBox/PortraitFrame");
		_selectionFrame = GetNode<PanelContainer>("BottomConsole/HBox/SelectionFrame");
		_commandFrame = GetNode<PanelContainer>("BottomConsole/HBox/CommandFrame");
		_devPanel = GetNode<Panel>("DevPanel");

		// Bind Resources
		_goldLabel = GetNode<Label>("ResourceContainer/HBox/GoldBox/GoldLabel");
		_woodLabel = GetNode<Label>("ResourceContainer/HBox/WoodBox/WoodLabel");
		_stoneLabel = GetNode<Label>("ResourceContainer/HBox/StoneBox/StoneLabel");

		// Add population & clock labels to the resource container
		var resHBox = GetNode<HBoxContainer>("ResourceContainer/HBox");
		var popBox = new VBoxContainer();
		popBox.AddThemeConstantOverride("separation", 2);
		resHBox.AddChild(popBox);
		var popTitleLbl = new Label();
		popTitleLbl.Text = "SUPPLY";
		popTitleLbl.AddThemeFontSizeOverride("font_size", 10);
		popTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		popBox.AddChild(popTitleLbl);
		_populationLabel = new Label();
		_populationLabel.Text = "0 / 20";
		_populationLabel.AddThemeFontSizeOverride("font_size", 16);
		_populationLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		popBox.AddChild(_populationLabel);

		var clockBox = new VBoxContainer();
		clockBox.AddThemeConstantOverride("separation", 2);
		resHBox.AddChild(clockBox);
		var clockTitleLbl = new Label();
		clockTitleLbl.Text = "TIME";
		clockTitleLbl.AddThemeFontSizeOverride("font_size", 10);
		clockTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		clockBox.AddChild(clockTitleLbl);
		_clockLabel = new Label();
		_clockLabel.Text = "0:00";
		_clockLabel.AddThemeFontSizeOverride("font_size", 16);
		_clockLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		clockBox.AddChild(_clockLabel);

		// Bind Multi-Selection Container & Buttons — now fully dynamic (up to 12)
		_unitsContainer = GetNode<HBoxContainer>("BottomConsole/HBox/SelectionFrame/UnitsContainer");
		// Remove hardcoded scene buttons and recreate dynamically
		foreach (Node child in _unitsContainer.GetChildren())
			_unitsContainer.RemoveChild(child);
		_unitButtons.Clear();
		for (int i = 0; i < 12; i++)
		{
			var btn = new Button();
			btn.Name = $"Unit{i + 1}";
			btn.CustomMinimumSize = new Vector2(58, 58);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.ExpandIcon = true;
			btn.FocusMode = FocusModeEnum.None;
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover",   UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			btn.AddThemeStyleboxOverride("focus",   new StyleBoxEmpty());

			// Add a health bar child
			var hpBar = new ProgressBar();
			hpBar.Name = "HealthBar";
			hpBar.CustomMinimumSize = new Vector2(50, 5);
			hpBar.ShowPercentage = false;
			hpBar.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
			hpBar.OffsetTop = -6;
			hpBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
			hpBar.AddThemeStyleboxOverride("fill",       UIStyle.CreateSliderFill());
			hpBar.Visible = false;
			btn.AddChild(hpBar);

			// Add status icon overlay
			var statusLbl = new Label();
			statusLbl.Name = "StatusIcon";
			statusLbl.Text = "";
			statusLbl.AddThemeFontSizeOverride("font_size", 10);
			statusLbl.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
			statusLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			statusLbl.Visible = false;
			btn.AddChild(statusLbl);

			_unitsContainer.AddChild(btn);
			_unitButtons.Add(btn);
		}
		for (int i = 0; i < _unitButtons.Count; i++)
		{
			int index = i;
			_unitButtons[i].Pressed += () => OnUnitSelectionButtonClicked(index);
		}

		// Create Single-Selection Stats Container programmatically inside SelectionFrame
		CreateStatsContainer();

		// Bind Commands
		_commandGrid = GetNode<GridContainer>("BottomConsole/HBox/CommandFrame/GridContainer");
		_commandGrid.Columns = 3;

		_btnMove = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnMove");
		_btnStop = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnStop");
		_btnHold = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnHold");
		_btnBuild = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnBuild");

		_btnAttack = new Button();
		_btnAttack.Name = "BtnAttack";
		_btnAttack.CustomMinimumSize = new Vector2(80, 80);
		_btnAttack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_btnAttack.SizeFlagsVertical = SizeFlags.ExpandFill;
		_btnAttack.FocusMode = FocusModeEnum.None;

		_btnPatrol = new Button();
		_btnPatrol.Name = "BtnPatrol";
		_btnPatrol.CustomMinimumSize = new Vector2(80, 80);
		_btnPatrol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_btnPatrol.SizeFlagsVertical = SizeFlags.ExpandFill;
		_btnPatrol.FocusMode = FocusModeEnum.None;

		// Bind Dev & Output
		_btnVictory = GetNode<Button>("DevPanel/BtnVictory");
		_btnDefeat = GetNode<Button>("DevPanel/BtnDefeat");
		_chkCameraToggle = GetNode<CheckButton>("DevPanel/ChkCameraToggle");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");

		_minimapArea = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea");
		_cameraIndicator = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea/Indicator");

		// Programmatically add the minimap overlay buttons next to MinimapArea inside the MinimapFrame
		_minimapControls = new VBoxContainer();
		_minimapControls.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		_minimapControls.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_minimapControls.AddThemeConstantOverride("separation", 6);
		_minimapControls.CustomMinimumSize = new Vector2(40, 0);
		_minimapFrame.AddChild(_minimapControls);

		_btnZoom = new Button();
		SetupMinimapButton(_btnZoom, "res://Assets/UI/search_icon.jpg", "Toggle Camera Zoom (Near / Mid / Far) [Z]", () => GameHost.Instance?.CycleCameraZoom());
		_minimapControls.AddChild(_btnZoom);

		_btnToggleTerrain = new Button();
		SetupMinimapButton(_btnToggleTerrain, "res://Assets/UI/game_menu.png", "Toggle Terrain Overlay (Radar Mode) [F4]", () => ToggleMinimapTerrain());
		_minimapControls.AddChild(_btnToggleTerrain);

		_btnPing = new Button();
		SetupMinimapButton(_btnPing, "res://Assets/UI/magic_upgrade_arrow.png", "Send Alert Ping on Map [Alt+G]", () => 
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.ActivePingMode = true;
				ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
			}
		});
		_minimapControls.AddChild(_btnPing);

		_btnCenter = new Button();
		SetupMinimapButton(_btnCenter, "res://Assets/UI/alliance_flag.png", "Center Camera on Castle [Space]", () => GameHost.Instance?.CenterCameraOnCastle());
		_minimapControls.AddChild(_btnCenter);

		_btnSelectIdle = new Button();
		SetupMinimapButton(_btnSelectIdle, "res://Assets/UI/unit_placeholder.png", "Select All Idle Units [F1]", () => GameHost.Instance?.SelectAllIdleUnits());
		_minimapControls.AddChild(_btnSelectIdle);

		_btnSelectArmy = new Button();
		SetupMinimapButton(_btnSelectArmy, "res://Assets/UI/heavy_knight.png", "Select All Army Units [F2]", () => GameHost.Instance?.SelectAllMilitaryUnits());
		_minimapControls.AddChild(_btnSelectArmy);

		// Hotkey reference toggle button
		var btnHotkeys = new Button();
		SetupMinimapButton(btnHotkeys, "res://Assets/UI/game_menu.png", "Hotkey Reference [F5]", () => ToggleHotkeyPanel());
		_minimapControls.AddChild(btnHotkeys);

		_camera3D = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");
		if (_camera3D is CameraControl camCtrl)
		{
			if (FileAccess.FileExists("res://map.json"))
			{
				using var file = FileAccess.Open("res://map.json", FileAccess.ModeFlags.Read);
				if (file != null)
				{
					try
					{
						string jsonText = file.GetAsText();
						using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonText);
						if (jsonDoc.RootElement.TryGetProperty("MapProperties", out var mapProps))
						{
							if (mapProps.TryGetProperty("CameraBoundsLeft", out var leftProp) && leftProp.ValueKind == System.Text.Json.JsonValueKind.Number)
								camCtrl.LimitLeft = (float)leftProp.GetDouble();
							if (mapProps.TryGetProperty("CameraBoundsRight", out var rightProp) && rightProp.ValueKind == System.Text.Json.JsonValueKind.Number)
								camCtrl.LimitRight = (float)rightProp.GetDouble();
							if (mapProps.TryGetProperty("CameraBoundsTop", out var topProp) && topProp.ValueKind == System.Text.Json.JsonValueKind.Number)
								camCtrl.LimitTop = (float)topProp.GetDouble();
							if (mapProps.TryGetProperty("CameraBoundsBottom", out var bottomProp) && bottomProp.ValueKind == System.Text.Json.JsonValueKind.Number)
								camCtrl.LimitBottom = (float)bottomProp.GetDouble();
						}
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[InGameHUD] Failed to load camera bounds from map.json: {ex.Message}");
					}
				}
			}
		}

		ApplyThemeStyles();
		SetupCommandCard();
		SetupDevPanel();
		SetupMinimap();
		SetupPortrait();

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0); // Hide initially
		Input.MouseMode = Input.MouseModeEnum.Visible;
		MouseFilter = MouseFilterEnum.Ignore;

		// Dynamically generate the minimap screenshot
		GenerateDynamicMinimap();

		// Bind Chat
		_chatPanel = GetNode<PanelContainer>("ChatPanel");
		_chatInput = GetNode<LineEdit>("ChatPanel/ChatContainer/ChatInput");
		_chatLog = GetNode<RichTextLabel>("ChatPanel/ChatContainer/ChatLog");

		// Style Chat
		var chatPanelStyle = new StyleBoxFlat();
		chatPanelStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
		chatPanelStyle.SetBorderWidthAll(1);
		chatPanelStyle.BorderColor = new Color(0.25f, 0.25f, 0.25f, 0.9f);
		chatPanelStyle.CornerRadiusTopLeft = 4;
		chatPanelStyle.CornerRadiusTopRight = 4;
		chatPanelStyle.CornerRadiusBottomLeft = 4;
		chatPanelStyle.CornerRadiusBottomRight = 4;
		chatPanelStyle.ContentMarginLeft = 8;
		chatPanelStyle.ContentMarginRight = 8;
		chatPanelStyle.ContentMarginTop = 8;
		chatPanelStyle.ContentMarginBottom = 8;
		_chatPanel.AddThemeStyleboxOverride("panel", chatPanelStyle);
		_chatInput.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		_chatInput.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		_chatInput.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		_chatLog.Text = "[color=#ffd700]Chat log initialized. Press Enter to type a message/cheat.[/color]\n";

		// Connect TextSubmitted signal on ChatInput
		_chatInput.TextSubmitted += OnChatInputSubmitted;
		
		// Hide chat input initially
		_chatInput.Visible = false;

		// Listen to LobbyManager chat
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ChatReceived += OnLobbyChatReceived;
		}

		_controlGroupsContainer = new HBoxContainer();
		_controlGroupsContainer.Name = "ControlGroupsContainer";
		_controlGroupsContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
		_controlGroupsContainer.OffsetLeft = 20;
		_controlGroupsContainer.OffsetTop = 80;
		_controlGroupsContainer.AddThemeConstantOverride("separation", 8);
		AddChild(_controlGroupsContainer);

		_customUIPanel = new VBoxContainer();
		_customUIPanel.Name = "CustomUIPanel";
		_customUIPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
		_customUIPanel.OffsetLeft = -260;
		_customUIPanel.OffsetTop = 20;
		_customUIPanel.CustomMinimumSize = new Vector2(240, 0);
		_customUIPanel.AddThemeConstantOverride("separation", 10);
		AddChild(_customUIPanel);

		_countdownPanel = new PanelContainer();
		_countdownPanel.Name = "CountdownPanel";
		_countdownPanel.Visible = false;
		var countdownStyle = new StyleBoxFlat();
		countdownStyle.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
		countdownStyle.SetBorderWidthAll(1);
		countdownStyle.BorderColor = UIStyle.ColorBronze;
		countdownStyle.CornerRadiusTopLeft = 4;
		countdownStyle.CornerRadiusTopRight = 4;
		countdownStyle.CornerRadiusBottomLeft = 4;
		countdownStyle.CornerRadiusBottomRight = 4;
		countdownStyle.ContentMarginLeft = 8;
		countdownStyle.ContentMarginRight = 8;
		countdownStyle.ContentMarginTop = 6;
		countdownStyle.ContentMarginBottom = 6;
		_countdownPanel.AddThemeStyleboxOverride("panel", countdownStyle);

		_countdownLabel = new Label();
		_countdownLabel.Name = "CountdownLabel";
		_countdownLabel.Text = "";
		_countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownLabel.AddThemeFontSizeOverride("font_size", 14);
		_countdownLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_countdownPanel.AddChild(_countdownLabel);
		_customUIPanel.AddChild(_countdownPanel);

		_leaderboardPanel = new PanelContainer();
		_leaderboardPanel.Name = "LeaderboardPanel";
		_leaderboardPanel.Visible = false;
		var leaderboardStyle = new StyleBoxFlat();
		leaderboardStyle.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
		leaderboardStyle.SetBorderWidthAll(1);
		leaderboardStyle.BorderColor = UIStyle.ColorBronze;
		leaderboardStyle.CornerRadiusTopLeft = 4;
		leaderboardStyle.CornerRadiusTopRight = 4;
		leaderboardStyle.CornerRadiusBottomLeft = 4;
		leaderboardStyle.CornerRadiusBottomRight = 4;
		leaderboardStyle.ContentMarginLeft = 10;
		leaderboardStyle.ContentMarginRight = 10;
		leaderboardStyle.ContentMarginTop = 8;
		leaderboardStyle.ContentMarginBottom = 8;
		_leaderboardPanel.AddThemeStyleboxOverride("panel", leaderboardStyle);

		var lbVBox = new VBoxContainer();
		_leaderboardPanel.AddChild(lbVBox);

		_leaderboardTitleLabel = new Label();
		_leaderboardTitleLabel.Name = "LeaderboardTitle";
		_leaderboardTitleLabel.Text = "LEADERBOARD";
		UIStyle.ApplyTitle(_leaderboardTitleLabel, "LEADERBOARD", 12);
		_leaderboardTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		lbVBox.AddChild(_leaderboardTitleLabel);

		_leaderboardContent = new VBoxContainer();
		_leaderboardContent.Name = "LeaderboardContent";
		lbVBox.AddChild(_leaderboardContent);
		_customUIPanel.AddChild(_leaderboardPanel);

		Resized += OnHUDResized;
		ApplyHUDScale();

		// Build F5 hotkey reference panel (hidden by default)
		BuildHotkeyReferencePanel();
	}

	private void OnHUDResized()
	{
		ApplyHUDScale();
	}

	public void ApplyHUDScale()
	{
		float s = GameSettings.HudScale / 100f;

		if (_resourceContainer != null)
		{
			_resourceContainer.PivotOffset = new Vector2(_resourceContainer.Size.X / 2f, 0f);
			_resourceContainer.Scale = new Vector2(s, s);
		}

		if (_bottomConsole != null)
		{
			_bottomConsole.PivotOffset = new Vector2(_bottomConsole.Size.X / 2f, _bottomConsole.Size.Y);
			_bottomConsole.Scale = new Vector2(s, s);
		}

		if (_leftPillar != null)
		{
			_leftPillar.PivotOffset = new Vector2(0f, _leftPillar.Size.Y / 2f);
			_leftPillar.Scale = new Vector2(s, s);
		}

		if (_rightPillar != null)
		{
			_rightPillar.PivotOffset = new Vector2(_rightPillar.Size.X, _rightPillar.Size.Y / 2f);
			_rightPillar.Scale = new Vector2(s, s);
		}

		if (_chatPanel != null)
		{
			_chatPanel.PivotOffset = new Vector2(0f, _chatPanel.Size.Y);
			_chatPanel.Scale = new Vector2(s, s);
		}

		if (_devPanel != null)
		{
			_devPanel.PivotOffset = Vector2.Zero;
			_devPanel.Scale = new Vector2(s, s);
		}

		if (_controlGroupsContainer != null)
		{
			_controlGroupsContainer.PivotOffset = Vector2.Zero;
			_controlGroupsContainer.Scale = new Vector2(s, s);
		}

		if (_customUIPanel != null)
		{
			_customUIPanel.PivotOffset = new Vector2(_customUIPanel.Size.X, 0f);
			_customUIPanel.Scale = new Vector2(s, s);
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ChatReceived -= OnLobbyChatReceived;
		}
	}

	private async void GenerateDynamicMinimap()
	{
		// Wait two frames to make sure everything in 3D is fully loaded and textured
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
		if (minimapBg == null) return;

		try
		{
			// 1. Create a SubViewport (256x256)
			var viewport = new SubViewport();
			viewport.Size = new Vector2I(256, 256);
			viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			AddChild(viewport);

			// 2. Create a top-down Orthogonal Camera3D inside the Viewport
			var camera = new Camera3D();
			camera.Projection = Camera3D.ProjectionType.Orthogonal;
			camera.Size = 250f; // matches our ground floor width (250m)
			camera.Far = 200f;
			camera.Position = new Vector3(0, 100, 0);
			camera.RotationDegrees = new Vector3(-90, 0, 0);
			viewport.AddChild(camera);

			// 3. Force render the viewport
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			// 4. Retrieve the rendered texture and convert to ImageTexture (to detach from viewport memory)
			var texture = viewport.GetTexture();
			if (texture != null)
			{
				var img = texture.GetImage();
				if (img != null)
				{
					var imgTexture = ImageTexture.CreateFromImage(img);
					minimapBg.Texture = imgTexture;
					GD.Print("[HUD] Generated dynamic minimap from 3D terrain screenshot successfully!");
				}
			}

			// 5. Clean up SubViewport
			viewport.QueueFree();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
	}

	private void CreateStatsContainer()
	{
		_statsContainer = new HBoxContainer();
		_statsContainer.LayoutMode = 2;
		_statsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statsContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_statsContainer.AddThemeConstantOverride("separation", 20);
		_selectionFrame.AddChild(_statsContainer);
		_statsContainer.Visible = false;

		// 1. Stats Text Section
		var statsVBox = new VBoxContainer();
		statsVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsVBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(statsVBox);

		_statsLabel = new Label();
		_statsLabel.Text = "HP: 100/100\nDamage: 10\nArmor: 2\nSpeed: 5";
		_statsLabel.AddThemeFontSizeOverride("font_size", 14);
		_statsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		statsVBox.AddChild(_statsLabel);

		// 2. Spells Section
		_spellsBox = new VBoxContainer();
		_spellsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_spellsBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_spellsBox);

		var spellsTitle = new Label();
		spellsTitle.Text = "SPELLS";
		UIStyle.ApplyTitle(spellsTitle, "SPELLS", 12);
		spellsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_spellsBox.AddChild(spellsTitle);

		var spellsHBox = new HBoxContainer();
		spellsHBox.AddThemeConstantOverride("separation", 8);
		_spellsBox.AddChild(spellsHBox);

		// Fireball button + cooldown bar
		_fireballVBox = new VBoxContainer();
		_fireballVBox.AddThemeConstantOverride("separation", 2);
		spellsHBox.AddChild(_fireballVBox);
		_btnFireball = new Button();
		SetupSpellButton(_btnFireball, "res://Assets/UI/fire_spell.png", "fireball",
			$"[Q] Cast Fireball — 50 AoE Dmg, {GameHost.FireballCooldownMax}s cooldown");
		_fireballVBox.AddChild(_btnFireball);
		_fireballCooldownBar = new ProgressBar();
		_fireballCooldownBar.CustomMinimumSize = new Vector2(70, 6);
		_fireballCooldownBar.MaxValue = GameHost.FireballCooldownMax;
		_fireballCooldownBar.ShowPercentage = false;
		_fireballCooldownBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		var fireStyle = new StyleBoxFlat();
		fireStyle.BgColor = new Color(0.9f, 0.3f, 0.1f);
		_fireballCooldownBar.AddThemeStyleboxOverride("fill", fireStyle);
		_fireballCooldownBar.Visible = false;
		_fireballVBox.AddChild(_fireballCooldownBar);

		// Lightning button + cooldown bar
		_lightningVBox = new VBoxContainer();
		_lightningVBox.AddThemeConstantOverride("separation", 2);
		spellsHBox.AddChild(_lightningVBox);
		_btnLightning = new Button();
		SetupSpellButton(_btnLightning, "res://Assets/UI/lightning_spell.png", "lightning",
			$"[E] Cast Lightning — 80 AoE Dmg, {GameHost.LightningCooldownMax}s cooldown");
		_lightningVBox.AddChild(_btnLightning);
		_lightningCooldownBar = new ProgressBar();
		_lightningCooldownBar.CustomMinimumSize = new Vector2(70, 6);
		_lightningCooldownBar.MaxValue = GameHost.LightningCooldownMax;
		_lightningCooldownBar.ShowPercentage = false;
		_lightningCooldownBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		var lightStyle = new StyleBoxFlat();
		lightStyle.BgColor = new Color(0.2f, 0.5f, 1.0f);
		_lightningCooldownBar.AddThemeStyleboxOverride("fill", lightStyle);
		_lightningCooldownBar.Visible = false;
		_lightningVBox.AddChild(_lightningCooldownBar);

		// Holy Light button + cooldown bar
		_holyLightVBox = new VBoxContainer();
		_holyLightVBox.AddThemeConstantOverride("separation", 2);
		spellsHBox.AddChild(_holyLightVBox);
		_btnHolyLight = new Button();
		SetupSpellButton(_btnHolyLight, "res://Assets/UI/magic_upgrade_arrow.png", "holylight",
			$"[W] Cast Holy Light — 60 AoE Heal, {GameHost.HolyLightCooldownMax}s cooldown");
		_holyLightVBox.AddChild(_btnHolyLight);
		_holyLightCooldownBar = new ProgressBar();
		_holyLightCooldownBar.CustomMinimumSize = new Vector2(70, 6);
		_holyLightCooldownBar.MaxValue = GameHost.HolyLightCooldownMax;
		_holyLightCooldownBar.ShowPercentage = false;
		_holyLightCooldownBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		var holyStyle = new StyleBoxFlat();
		holyStyle.BgColor = new Color(0.2f, 0.9f, 0.3f);
		_holyLightCooldownBar.AddThemeStyleboxOverride("fill", holyStyle);
		_holyLightCooldownBar.Visible = false;
		_holyLightVBox.AddChild(_holyLightCooldownBar);

		// 3. Items Section
		_itemsBox = new VBoxContainer();
		_itemsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_itemsBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_itemsBox);

		var itemsTitle = new Label();
		itemsTitle.Text = "ITEMS";
		UIStyle.ApplyTitle(itemsTitle, "ITEMS", 12);
		itemsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_itemsBox.AddChild(itemsTitle);

		var itemsHBox = new HBoxContainer();
		itemsHBox.AddThemeConstantOverride("separation", 8);
		_itemsBox.AddChild(itemsHBox);

		var itemAxe = new TextureRect();
		SetupItemIcon(itemAxe, "res://Assets/UI/battle_axe.png", "Battle Axe\n+5 Attack Damage (Equipped)");
		itemsHBox.AddChild(itemAxe);

		var itemShield = new TextureRect();
		SetupItemIcon(itemShield, "res://Assets/UI/battle_shield.png", "Battle Shield\n+3 Armor Block (Equipped)");
		itemsHBox.AddChild(itemShield);

		_btnUsePotion = new Button();
		_btnUsePotion.Name = "BtnUsePotion";
		_btnUsePotion.Flat = false;
		_btnUsePotion.ExpandIcon = true;
		_btnUsePotion.Icon = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
		_btnUsePotion.CustomMinimumSize = new Vector2(60, 60);
		_btnUsePotion.FocusMode = FocusModeEnum.None;
		_btnUsePotion.ClipContents = true;
		_btnUsePotion.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnUsePotion.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnUsePotion.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());

		var potHotkeyLabel = new Label();
		potHotkeyLabel.Name = "HotkeyLabel";
		potHotkeyLabel.Text = "I";
		potHotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
		potHotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		potHotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		potHotkeyLabel.AddThemeConstantOverride("outline_size", 4);
		potHotkeyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
		potHotkeyLabel.OffsetLeft = 4;
		potHotkeyLabel.OffsetTop = 3;
		potHotkeyLabel.MouseFilter = MouseFilterEnum.Ignore;
		_btnUsePotion.AddChild(potHotkeyLabel);

		itemsHBox.AddChild(_btnUsePotion);
		_btnUsePotion.Pressed += () =>
		{
			var selected = GameHost.Instance?.SelectedUnits;
			if (selected != null && selected.Count == 1 && !selected[0].IsEnemy)
			{
				GameHost.Instance.UseHealingPotion(selected[0]);
			}
		};

		// 4. Production Section (for structures)
		_productionBox = new VBoxContainer();
		_productionBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_productionBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_productionBox);
		_productionBox.Visible = false;

		_productionTitle = new Label();
		_productionTitle.Text = "PRODUCTION";
		UIStyle.ApplyTitle(_productionTitle, "PRODUCTION", 12);
		_productionTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_productionBox.AddChild(_productionTitle);

		_productionProgress = new ProgressBar();
		_productionProgress.CustomMinimumSize = new Vector2(120, 16);
		_productionProgress.ShowPercentage = true;
		_productionProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		_productionProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		_productionBox.AddChild(_productionProgress);

		_productionQueueLabel = new Label();
		_productionQueueLabel.Text = "Queue: 0";
		_productionQueueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_productionQueueLabel.AddThemeFontSizeOverride("font_size", 11);
		_productionQueueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_productionBox.AddChild(_productionQueueLabel);

		_queueSlotsContainer = new HBoxContainer();
		_queueSlotsContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_queueSlotsContainer.AddThemeConstantOverride("separation", 6);
		_productionBox.AddChild(_queueSlotsContainer);
	}

	private void SetupSpellButton(Button btn, string iconPath, string spellId, string tooltip)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.TooltipText = tooltip;
		btn.CustomMinimumSize = new Vector2(70, 70);
		btn.ClipContents = true;

		// Parse hotkey from "[X]" prefix and overlay it on the button face
		if (tooltip.StartsWith("[") && tooltip.Contains("]"))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EnterSpellTargeting(spellId);
			}
		};
	}

	private void SetupItemIcon(TextureRect rect, string iconPath, string tooltip)
	{
		rect.Texture = GD.Load<Texture2D>(iconPath);
		rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		rect.CustomMinimumSize = new Vector2(60, 60);
		rect.TooltipText = tooltip;
	}

	public void RefreshUI(List<Unit3D> selectedUnits)
	{
		bool hasPlayerSelection = false;
		if (selectedUnits != null)
		{
			foreach (var u in selectedUnits)
			{
				if (!u.IsEnemy)
				{
					hasPlayerSelection = true;
					break;
				}
			}
		}
		_commandFrame.Visible = hasPlayerSelection;

		_isBuildSubMenuOpen = false;
		PopulateCommandGrid();

		if (selectedUnits == null || selectedUnits.Count == 0)
		{
			// No selection
			_unitsContainer.Visible = false;
			_statsContainer.Visible = false;
			_armyCompositionLabel?.Hide();
			
			GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").Text = "No Selection";
			var pTexture = GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture");
			if (pTexture != null)
			{
				pTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
			}
		}
		else if (selectedUnits.Count == 1)
		{
			// Single selection detail view
			_unitsContainer.Visible = false;
			_statsContainer.Visible = true;
			_armyCompositionLabel?.Hide();

			var unit = selectedUnits[GameHost.Instance != null ? GameHost.Instance.CycleSelectionIndex : 0];
			string unitName = "Footman";
			float maxHp = 100f, currHp = 100f, damage = 10f, armor = 2f, speed = 5f, range = 2f;
			string icon = GetUnitIcon(unit.UnitId);

			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<Name>(unit.Entity)) unitName = world.Get<Name>(unit.Entity).Value;
				if (world.Has<Health>(unit.Entity))
				{
					var hp = world.Get<Health>(unit.Entity);
					maxHp = hp.Max;
					currHp = hp.Current;
				}
				if (world.Has<Attack>(unit.Entity))
				{
					var atk = world.Get<Attack>(unit.Entity);
					damage = atk.Damage;
					range = atk.Range;
				}
				if (world.Has<Armor>(unit.Entity)) armor = world.Get<Armor>(unit.Entity).Value;
				if (world.Has<MovementStats>(unit.Entity)) speed = world.Get<MovementStats>(unit.Entity).Speed;
			}

			// Update Portrait & Name
			GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").Text = unitName;
			var pTexture = GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture");
			if (pTexture != null)
			{
				pTexture.Texture = GD.Load<Texture2D>(icon);
			}

			// Description from registry
			string desc = "";
			if (GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var regMeta))
				desc = regMeta.Description;

			// Current state string
			string stateText = "";
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<GameHost.Gatherer>(unit.Entity))
				{
					var gather = world.Get<GameHost.Gatherer>(unit.Entity);
					string stateLabel = gather.ReturningToBase ? "● DELIVERING" : "● HARVESTING";
					stateText = $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {gather.ResourceType.ToUpper()})";
				}
				else if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity))   stateText = "● HOLDING";
				else if (world.Has<Realm.Ecs.Components.Movement.Patrol>(unit.Entity))     stateText = "● PATROLLING";
				else if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity)) stateText = "● ATTACK-MOVE";
				else if (world.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity))     stateText = "● FOLLOWING";
				else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(unit.Entity))     stateText = "● MOVING";
				else if (world.Has<AttackTarget>(unit.Entity))                              stateText = "● ATTACKING";
				else                                                                         stateText = "○ IDLE";
			}

			// Update stats display — richer format with DPS
			string statsText = $"HP: {currHp:F0} / {maxHp:F0}";
			if (damage > 0)
			{
				string label = (unit.UnitId == "priest") ? "HEAL" : "ATK";
				statsText += $"   {label}: {damage:F0}   RNG: {range:F0}";
				// Show DPS only for attacking units
				if (unit.UnitId != "priest" && GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity))
				{
					var world2 = GameHost.Instance.EcsWorld;
					if (world2.Has<Attack>(unit.Entity))
					{
						var atkComp = world2.Get<Attack>(unit.Entity);
						if (atkComp.Cooldown > 0)
						{
							float dps = atkComp.Damage / atkComp.Cooldown;
							statsText += $"   DPS: {dps:F1}";
						}
					}
				}
			}
			statsText += $"\nArmor: {armor:F0}";
			if (speed > 0) statsText += $"   Speed: {speed:F0}";
			// Show tower level badge
			if (unit.UnitId == "tower" && GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity))
			{
				var tw = GameHost.Instance.EcsWorld;
				if (tw.Has<GameHost.TowerUpgradeLevel>(unit.Entity))
				{
					int lvl = tw.Get<GameHost.TowerUpgradeLevel>(unit.Entity).Value;
					statsText += $"   \u2605 LVL {lvl}";
				}
			}
			statsText += $"\n{stateText}";
			if (!string.IsNullOrEmpty(desc)) statsText += $"\n\n{desc}";
			_statsLabel.Text = statsText;

			// Adjust Spells, Items, and Production visual panels
			// 1. Spells Box visibility
			bool hasFireball = false;
			bool hasLightning = false;
			bool hasHolyLight = false;

			if (GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var metadata))
			{
				if (metadata.Abilities != null)
				{
					hasFireball = Array.Exists(metadata.Abilities, a => a == "fireball");
					hasLightning = Array.Exists(metadata.Abilities, a => a == "lightning");
					hasHolyLight = Array.Exists(metadata.Abilities, a => a == "holylight");
				}
				else
				{
					if (unit.UnitId == "priest")
					{
						hasHolyLight = true;
					}
					else if (unit.UnitId == "tower")
					{
						hasFireball = true;
						hasLightning = true;
					}
				}
			}
			else
			{
				if (unit.UnitId == "priest")
				{
					hasHolyLight = true;
				}
				else if (unit.UnitId == "tower")
				{
					hasFireball = true;
					hasLightning = true;
				}
			}

			if (hasFireball || hasLightning || hasHolyLight)
			{
				_spellsBox.Visible = true;
				if (_fireballVBox != null) _fireballVBox.Visible = hasFireball;
				if (_lightningVBox != null) _lightningVBox.Visible = hasLightning;
				if (_holyLightVBox != null) _holyLightVBox.Visible = hasHolyLight;
			}
			else
			{
				_spellsBox.Visible = false;
			}

			// 2. Items Box visibility
			if (!unit.IsBuilding)
			{
				_itemsBox.Visible = true;
				
				// Hardcode unit-specific item tooltips
				var itemsHBox = _itemsBox.GetChild<HBoxContainer>(1);
				var axeIcon = itemsHBox.GetChild<TextureRect>(0);
				var shieldIcon = itemsHBox.GetChild<TextureRect>(1);

				if (unit.UnitId == "archer")
				{
					axeIcon.TooltipText = "Composite Recurve Bow\n+4 Attack Damage (Equipped)";
					shieldIcon.TooltipText = "Elven Leather Boots\n+2 Movement Speed (Equipped)";
				}
				else if (unit.UnitId == "priest")
				{
					axeIcon.TooltipText = "Blessed Rod\n+3 Healing Power (Equipped)";
					shieldIcon.TooltipText = "Cloth Robes\n+1 Armor Block (Equipped)";
				}
				else
				{
					axeIcon.TooltipText = "Battle Axe\n+5 Attack Damage (Equipped)";
					shieldIcon.TooltipText = "Battle Shield\n+3 Armor Block (Equipped)";
				}

				int potions = 0;
				if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity) && GameHost.Instance.EcsWorld.Has<Inventory>(unit.Entity))
				{
					potions = GameHost.Instance.EcsWorld.Get<Inventory>(unit.Entity).Potions;
				}

				if (_btnUsePotion != null)
				{
					_btnUsePotion.Text = $" {potions} ";
					_btnUsePotion.TooltipText = $"[I] Healing Potion (Have: {potions})\nRestores 50 HP on use.";
					_btnUsePotion.Disabled = potions <= 0 || unit.IsEnemy;
				}
			}
			else
			{
				_itemsBox.Visible = false;
			}

			// 3. Production Box visibility
			if (unit.IsBuilding && !unit.IsEnemy && unit.UnitId == "castle")
			{
				_productionBox.Visible = true;
				if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity))
				{
					var prod = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity);
					if (prod.UnitIds.Count > 0)
					{
						string trainingName = prod.UnitIds[0].ToUpper();
						_productionTitle.Text = $"TRAINING: {trainingName}";
						_productionProgress.Visible = true;
						_productionProgress.Value = prod.CurrentProgress;
						_productionProgress.MaxValue = prod.BuildTime;
						_productionQueueLabel.Text = $"Queue: {prod.UnitIds.Count}";
						PopulateQueueSlots(unit.Entity, prod.UnitIds);
					}
					else
					{
						_productionTitle.Text = "PRODUCTION IDLE";
						_productionProgress.Visible = false;
						_productionQueueLabel.Text = "Queue empty — [F] Footman  [R] Archer  [P] Priest";
						ClearQueueSlots();
					}
				}
				else
				{
					_productionTitle.Text = "PRODUCTION READY";
					_productionProgress.Visible = false;
					_productionQueueLabel.Text = "Queue empty — [F] Footman  [R] Archer  [P] Priest";
					ClearQueueSlots();
				}
			}
			else
			{
				_productionBox.Visible = false;
			}
		}
		else
		{
			// Multi-selection grid view
			_unitsContainer.Visible = true;
			_statsContainer.Visible = false;

			GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").Text = $"{selectedUnits.Count} Units Selected";
			var pTexture = GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture");
			if (pTexture != null)
			{
				pTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
			}

			// Army composition breakdown
			if (_armyCompositionLabel == null)
			{
				_armyCompositionLabel = new Label();
				_armyCompositionLabel.Name = "ArmyCompositionLabel";
				_armyCompositionLabel.AddThemeFontSizeOverride("font_size", 10);
				_armyCompositionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				_armyCompositionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				var portraitVBox = GetNodeOrNull<VBoxContainer>("BottomConsole/HBox/PortraitFrame/VBox");
				portraitVBox?.AddChild(_armyCompositionLabel);
			}
			// Count units by type
			var unitTypeCounts = new System.Collections.Generic.Dictionary<string, int>();
			foreach (var u in selectedUnits)
			{
				string tid = u.UnitId;
				if (!unitTypeCounts.ContainsKey(tid)) unitTypeCounts[tid] = 0;
				unitTypeCounts[tid]++;
			}
			var compParts = new System.Collections.Generic.List<string>();
			foreach (var kv in unitTypeCounts)
				compParts.Add($"{kv.Value}× {kv.Key}");
			if (_armyCompositionLabel != null)
				_armyCompositionLabel.Text = string.Join(", ", compParts);
			_armyCompositionLabel?.Show();

			// Configure unit frame buttons
			var selectedBorder = new StyleBoxFlat();
			selectedBorder.BgColor = new Color(0, 0, 0, 0);
			selectedBorder.BorderColor = new Color(0.1f, 0.8f, 0.2f, 0.8f);
			selectedBorder.SetBorderWidthAll(3);

			for (int i = 0; i < _unitButtons.Count; i++)
			{
				var btn = _unitButtons[i];
				var hpBar = btn.GetNodeOrNull<ProgressBar>("HealthBar");

				if (i < selectedUnits.Count)
				{
					var unit = selectedUnits[i];
					btn.Visible = true;
					btn.Icon = GD.Load<Texture2D>(GetUnitIcon(unit.UnitId));
					btn.TooltipText = unit.UnitId.ToUpper();

					// Highlight focused unit in gold, other selected units in green
					bool isFocused = GameHost.Instance != null && i == GameHost.Instance.CycleSelectionIndex;
					if (isFocused)
					{
						var focusedBorder = new StyleBoxFlat();
						focusedBorder.BgColor = new Color(0, 0, 0, 0);
						focusedBorder.BorderColor = new Color(0.95f, 0.82f, 0.55f, 1.0f); // Ornate gold
						focusedBorder.SetBorderWidthAll(3);
						btn.AddThemeStyleboxOverride("normal", focusedBorder);
					}
					else
					{
						btn.AddThemeStyleboxOverride("normal", selectedBorder);
					}

					if (hpBar != null && GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(unit.Entity))
					{
						var world = GameHost.Instance.EcsWorld;
						var hp = world.Get<Health>(unit.Entity);
						hpBar.Visible = true;
						hpBar.MaxValue = hp.Max;
						hpBar.Value = hp.Current;
						// Tint health bar red if low
						float hpPct = hp.Max > 0 ? hp.Current / hp.Max : 1f;
						var fillStyle = new StyleBoxFlat();
						fillStyle.BgColor = hpPct < 0.35f ? new Color(0.9f, 0.2f, 0.1f)
										  : hpPct < 0.7f  ? new Color(0.9f, 0.7f, 0.1f)
										  : new Color(0.1f, 0.85f, 0.2f);
						hpBar.AddThemeStyleboxOverride("fill", fillStyle);
					}
					else if (hpBar != null)
					{
						hpBar.Visible = false;
					}
				}
				else
				{
					btn.Visible = false;
				}
			}
		}
	}

	private string GetUnitIcon(string unitId)
	{
		return unitId switch
		{
			"footman" => "res://Assets/UI/heavy_knight.png",
			"archer" => "res://Assets/UI/elf_warrior.png",
			"priest" => "res://Assets/UI/alliance_flag.png",
			"castle" => "res://Assets/UI/moonlit_castle.png",
			"tower" => "res://Assets/UI/unknown_unit_1.png",
			_ => "res://Assets/UI/unit_placeholder.png"
		};
	}

	private void ApplyThemeStyles()
	{
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreateHUDPillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreateHUDPillarPanel(false));
		
		var resourceBg = new StyleBoxTexture();
		resourceBg.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_button_premium.png");
		resourceBg.TextureMarginLeft = 30;
		resourceBg.TextureMarginRight = 30;
		resourceBg.TextureMarginTop = 15;
		resourceBg.TextureMarginBottom = 15;
		resourceBg.ContentMarginLeft = 20;
		resourceBg.ContentMarginRight = 20;
		resourceBg.ContentMarginTop = 4;
		resourceBg.ContentMarginBottom = 4;
		_resourceContainer.AddThemeStyleboxOverride("panel", resourceBg);

		var bottomConsoleStyle = UIStyle.CreateStonePanel();
		bottomConsoleStyle.ContentMarginLeft = 210; 
		bottomConsoleStyle.ContentMarginRight = 210; 
		_bottomConsole.AddThemeStyleboxOverride("panel", bottomConsoleStyle);
		_minimapFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_portraitFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_selectionFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		
		var commandFrameStyle = (StyleBoxTexture)UIStyle.CreateStonePanel(true).Duplicate();
		commandFrameStyle.ContentMarginLeft = 2;
		commandFrameStyle.ContentMarginRight = 2;
		commandFrameStyle.ContentMarginTop = 2;
		commandFrameStyle.ContentMarginBottom = 2;
		_commandFrame.AddThemeStyleboxOverride("panel", commandFrameStyle);

		var devStyle = new StyleBoxFlat();
		devStyle.BgColor = new Color(0.12f, 0.13f, 0.15f, 0.9f);
		devStyle.BorderColor = UIStyle.ColorBronze;
		devStyle.SetBorderWidthAll(2);
		devStyle.CornerRadiusBottomRight = 8;
		_devPanel.AddThemeStyleboxOverride("panel", devStyle);

		UIStyle.ApplyTitle(_feedbackLabel, "", 32);
		_feedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		GetNode<TextureRect>("ResourceContainer/HBox/GoldBox/GoldIcon").Texture = GD.Load<Texture2D>("res://Assets/UI/gold_coin.png");
		GetNode<TextureRect>("ResourceContainer/HBox/WoodBox/WoodIcon").Texture = GD.Load<Texture2D>("res://Assets/UI/wood_logs.png");
		GetNode<TextureRect>("ResourceContainer/HBox/StoneBox/StoneIcon").Texture = GD.Load<Texture2D>("res://Assets/UI/wooden_planks.png");

		var topLabels = new[] { _goldLabel, _woodLabel, _stoneLabel };
		foreach (var lbl in topLabels)
		{
			lbl.AddThemeFontSizeOverride("font_size", 18);
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		}
		_goldLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));

		// Minimap Tiled Background (Initially empty, will load terrain snapshot)
		var minimapBg = new TextureRect();
		minimapBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		minimapBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		minimapBg.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.AddChild(minimapBg);
		minimapBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_minimapArea.MoveChild(minimapBg, 0); 

		// Add MinimapOverlay for dynamic unit icons
		var overlay = new MinimapOverlay();
		overlay.Name = "MinimapOverlay";
		overlay.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		// Keep the green camera cameraIndicator at the top
		_cameraIndicator.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.MoveChild(_cameraIndicator, _minimapArea.GetChildCount() - 1);
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	public override void _Process(double delta)
	{
		if (_countdownActive)
		{
			_countdownDuration -= (float)delta;
			if (_countdownDuration <= 0f)
			{
				_countdownDuration = 0f;
				_countdownActive = false;
				_countdownPanel.Visible = false;

			}
			else
			{
				_countdownLabel.Text = $"{_countdownText}: {(int)Math.Ceiling(_countdownDuration)}s";
			}
		}
		// Standard tick accumulation of resources (scaled by upgrades)
		_goldPerSec = 1.5f * _resourceGatherMultiplier;
		_woodPerSec = 1.0f * _resourceGatherMultiplier;
		_stonePerSec = 0.8f * _resourceGatherMultiplier;

		_gold += (float)delta * _goldPerSec;
		_wood += (float)delta * _woodPerSec;
		_stone += (float)delta * _stonePerSec;

		_goldLabel.Text = $"{_gold:F0}";
		_woodLabel.Text = $"{_wood:F0}";
		_stoneLabel.Text = $"{_stone:F0}";

		// Update resource income tooltips periodically
		_incomeUpdateTimer += (float)delta;
		if (_incomeUpdateTimer >= 2f)
		{
			_incomeUpdateTimer = 0f;
			_goldLabel.TooltipText = $"Gold: {_gold:F0}\nIncome: +{_goldPerSec:F1}/sec";
			_woodLabel.TooltipText = $"Wood: {_wood:F0}\nIncome: +{_woodPerSec:F1}/sec";
			_stoneLabel.TooltipText = $"Stone: {_stone:F0}\nIncome: +{_stonePerSec:F1}/sec";
		}

		// Population & game clock
		if (_populationLabel != null && GameHost.Instance != null)
		{
			int pop = GameHost.Instance.CurrentPopulation;
			int maxPop = GameHost.Instance.MaxPopulation;
			_populationLabel.Text = $"{pop} / {maxPop}";
			// Highlight red when near cap
			_populationLabel.AddThemeColorOverride("font_color", pop >= maxPop ? new Color(1f, 0.3f, 0.3f) : UIStyle.ColorGoldDull);
		}
		if (_clockLabel != null && GameHost.Instance != null)
		{
			float t = GameHost.Instance.GameElapsedTime;
			int mins = (int)(t / 60);
			int secs = (int)(t % 60);
			string phase = GameHost.Instance.TimeOfDayIndex switch
			{
				0 => "Day",
				1 => "Sunset",
				2 => "Night",
				3 => "Dawn",
				_ => "Day"
			};
			_clockLabel.Text = $"{mins}:{secs:D2} ({phase})";
		}

		// Count idle units
		int idleCount = 0;
		if (GameHost.Instance != null)
		{
			foreach (var unit in GameHost.Instance.AllUnits)
			{
				if (unit.IsEnemy || unit.IsBuilding) continue;
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(unit.Entity))
				{
					bool isMovable = world.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity);
					bool hasMoveTo = world.Has<MoveTo>(unit.Entity);
					bool hasAttackTarget = world.Has<AttackTarget>(unit.Entity);
					bool hasAttackMove = world.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity);
					bool hasPatrol = world.Has<Patrol>(unit.Entity);
					bool hasFollow = world.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity);
					bool hasHealTarget = world.Has<HealingTarget>(unit.Entity);
					if (isMovable && !hasMoveTo && !hasAttackTarget && !hasAttackMove && !hasPatrol && !hasFollow && !hasHealTarget)
					{
						idleCount++;
					}
				}
			}
		}
		if (_btnSelectIdle != null)
		{
			_btnSelectIdle.TooltipText = $"Select All Idle Units [F1] ({idleCount} Idle)";
			// Pulse/blink the idle button when there are idle units
			if (idleCount > 0)
			{
				_idlePulseTimer += (float)delta;
				if (_lastIdleCount == 0)
				{
					// Just became idle — show feedback
					ShowFeedbackText($"{idleCount} unit(s) are idle! [F1] to select", new Color(0.9f, 0.7f, 0.2f));
				}
				float pulse = Mathf.Sin(_idlePulseTimer * 4f) * 0.5f + 0.5f;
				_btnSelectIdle.Modulate = new Color(1f, 0.8f + pulse * 0.2f, 0.2f + pulse * 0.8f, 1f);
			}
			else
			{
				_idlePulseTimer = 0f;
				_btnSelectIdle.Modulate = Colors.White;
			}
			_lastIdleCount = idleCount;
		}

		// Disable spell buttons during cooldowns
		if (_btnFireball != null && GameHost.Instance != null)
		{
			_btnFireball.Disabled = GameHost.Instance.FireballCooldown > 0;
		}
		if (_btnLightning != null && GameHost.Instance != null)
		{
			_btnLightning.Disabled = GameHost.Instance.LightningCooldown > 0;
		}
		if (_btnHolyLight != null && GameHost.Instance != null)
		{
			_btnHolyLight.Disabled = GameHost.Instance.HolyLightCooldown > 0;
		}

		// Spell cooldown bars
		if (_fireballCooldownBar != null && GameHost.Instance != null)
		{
			float cd = GameHost.Instance.FireballCooldown;
			_fireballCooldownBar.Value = cd;
			_fireballCooldownBar.Visible = cd > 0;
		}
		if (_lightningCooldownBar != null && GameHost.Instance != null)
		{
			float cd = GameHost.Instance.LightningCooldown;
			_lightningCooldownBar.Value = cd;
			_lightningCooldownBar.Visible = cd > 0;
		}
		if (_holyLightCooldownBar != null && GameHost.Instance != null)
		{
			float cd = GameHost.Instance.HolyLightCooldown;
			_holyLightCooldownBar.Value = cd;
			_holyLightCooldownBar.Visible = cd > 0;
		}

		UpdateMinimapIndicator();

		// Update production progress smoothly if visible
		if (_productionBox != null && _productionBox.Visible && GameHost.Instance != null)
		{
			var selectedUnits = GameHost.Instance.SelectedUnits;
			if (selectedUnits != null && selectedUnits.Count == 1)
			{
				var unit = selectedUnits[0];
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(unit.Entity) && world.Has<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity))
				{
					var prod = world.Get<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity);
					if (prod.UnitIds.Count > 0)
					{
						_productionProgress.Value = prod.CurrentProgress;
						_productionProgress.MaxValue = prod.BuildTime;
					}
				}
			}
		}

		// Update unit status icons in multi-select grid
		if (GameHost.Instance != null && _unitsContainer != null && _unitsContainer.Visible)
		{
			var selectedUnits = GameHost.Instance.SelectedUnits;
			for (int i = 0; i < _unitButtons.Count; i++)
			{
				var btn = _unitButtons[i];
				var statusLbl = btn.GetNodeOrNull<Label>("StatusIcon");
				if (statusLbl == null || i >= selectedUnits.Count) { if (statusLbl != null) statusLbl.Visible = false; continue; }
				var unit = selectedUnits[i];
				if (!GameHost.Instance.EcsWorld.IsAlive(unit.Entity)) { statusLbl.Visible = false; continue; }
				var world = GameHost.Instance.EcsWorld;
				string status = "";
				if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity))       status = "H";
				else if (world.Has<Realm.Ecs.Components.Movement.Patrol>(unit.Entity))         status = "P";
				else if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity))     status = "A";
				else if (world.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity))         status = "F";
				else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(unit.Entity))         status = "M";
				statusLbl.Text = status;
				statusLbl.Visible = !string.IsNullOrEmpty(status);
			}
		}

		// Tell minimap overlay to redraw the unit positions
		var overlay = _minimapArea.GetNodeOrNull<MinimapOverlay>("MinimapOverlay");
		if (overlay != null)
		{
			overlay.QueueRedraw();
		}

		// Update Visual Control Groups
		UpdateControlGroupsUI();

		// Redraw HUD layer (for floating 3D health bars)
		QueueRedraw();
	}

	public void ShowFeedbackText(string text, Color color)
	{
		_feedbackLabel.Text = text;
		_feedbackLabel.AddThemeColorOverride("font_color", color);
		_feedbackLabel.Modulate = new Color(color.R, color.G, color.B, 1.0f);

		var tween = CreateTween();
		tween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, 1.5f).SetDelay(0.5f);
	}

	public void EnterBuildSubMenu()
	{
		_isBuildSubMenuOpen = true;
		PopulateCommandGrid();
	}

	public void ExitBuildSubMenu()
	{
		_isBuildSubMenuOpen = false;
		PopulateCommandGrid();
	}

	public void PopulateCommandGrid()
	{
		if (_commandGrid == null) return;

		foreach (var btn in _dynamicBuildButtons)
		{
			if (GodotObject.IsInstanceValid(btn))
			{
				btn.QueueFree();
			}
		}
		_dynamicBuildButtons.Clear();

		// Clear current children
		foreach (Node child in _commandGrid.GetChildren())
		{
			_commandGrid.RemoveChild(child);
		}

		var selectedUnits = GameHost.Instance?.SelectedUnits;
		if (selectedUnits == null || selectedUnits.Count == 0)
		{
			return;
		}

		// Find the focused unit based on CycleSelectionIndex
		int focusIdx = GameHost.Instance != null ? GameHost.Instance.CycleSelectionIndex : 0;
		if (focusIdx < 0 || focusIdx >= selectedUnits.Count) focusIdx = 0;
		var focusedUnit = selectedUnits[focusIdx];

		if (focusedUnit.IsEnemy)
		{
			// Enemies shouldn't have command buttons
			return;
		}

		if (focusedUnit.IsBuilding)
		{
			if (focusedUnit.UnitId == "castle")
			{
				_commandGrid.AddChild(_btnTrainFootman);
				_commandGrid.AddChild(_btnTrainArcher);
				_commandGrid.AddChild(_btnTrainPriest);
				_commandGrid.AddChild(_btnTrainWorker);
				_commandGrid.AddChild(_btnSetRally);
				_commandGrid.AddChild(_btnBuyPotion);

				if (GameHost.Instance != null)
				{
					ApplyUpgradeButtonState(_btnUpgradeWeapons, GameHost.Instance.HasWeaponsUpgrade, "MAXED: Weapons");
					_commandGrid.AddChild(_btnUpgradeWeapons);
					ApplyUpgradeButtonState(_btnUpgradeShields, GameHost.Instance.HasShieldsUpgrade, "MAXED: Armor");
					_commandGrid.AddChild(_btnUpgradeShields);
					ApplyUpgradeButtonState(_btnUpgradeHarvesting, GameHost.Instance.HasHarvestingUpgrade, "MAXED: Harvest");
					_commandGrid.AddChild(_btnUpgradeHarvesting);
				}
			}
			else if (focusedUnit.UnitId == "tower")
			{
				if (GameHost.Instance != null)
				{
					bool isMaxed = false;
					if (GameHost.Instance.EcsWorld.IsAlive(focusedUnit.Entity) && GameHost.Instance.EcsWorld.Has<GameHost.TowerUpgradeLevel>(focusedUnit.Entity))
					{
						isMaxed = GameHost.Instance.EcsWorld.Get<GameHost.TowerUpgradeLevel>(focusedUnit.Entity).Value >= 3;
					}
					ApplyUpgradeButtonState(_btnUpgradeTower, isMaxed, "MAXED: Tower Level 3");
				}
				_commandGrid.AddChild(_btnUpgradeTower);
				_commandGrid.AddChild(_btnFireball);
				_commandGrid.AddChild(_btnLightning);
			}
		}
		else
		{
			// Mobile unit
			if (_isBuildSubMenuOpen)
			{
				if (GameHost.Instance != null && GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta) && meta.BuildOptions != null)
				{
					foreach (var buildOpt in meta.BuildOptions)
					{
						if (GameHost.UnitRegistry.TryGetValue(buildOpt, out var structureMeta))
						{
							var btn = new Button();
							_dynamicBuildButtons.Add(btn);
							string iconPath = GetUnitIcon(buildOpt);
							string tooltipText = $"Build {structureMeta.Name} (Cost: {structureMeta.CostGold} Gold, {structureMeta.CostWood} Wood, {structureMeta.CostStone} Stone)";
							string structureType = buildOpt;
							SetupHUDButton(btn, iconPath, tooltipText, () => GameHost.Instance?.EnterBuildingPlacement(structureType));
							_commandGrid.AddChild(btn);
						}
					}
				}
				else
				{
					_commandGrid.AddChild(_btnBuildCastle);
					_commandGrid.AddChild(_btnBuildTower);
				}
				_commandGrid.AddChild(_btnCancelBuild);
			}
			else
			{
				_commandGrid.AddChild(_btnMove);
				_commandGrid.AddChild(_btnStop);
				_commandGrid.AddChild(_btnHold);
				_commandGrid.AddChild(_btnAttack);
				_commandGrid.AddChild(_btnPatrol);
				
				bool canBuild = false;
				if (GameHost.Instance != null && GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta))
				{
					canBuild = meta.BuildOptions != null && meta.BuildOptions.Length > 0;
				}
				if (canBuild)
				{
					_commandGrid.AddChild(_btnBuild);
				}

				// Add spells/potions for mobile unit if applicable
				if (focusedUnit.UnitId == "priest")
				{
					_commandGrid.AddChild(_btnHolyLight);
				}

				// Always show the use potion button for friendly mobile units
				int potions = 0;
				if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(focusedUnit.Entity) && GameHost.Instance.EcsWorld.Has<Inventory>(focusedUnit.Entity))
				{
					potions = GameHost.Instance.EcsWorld.Get<Inventory>(focusedUnit.Entity).Potions;
				}
				if (_btnUsePotion != null)
				{
					_btnUsePotion.Text = $" {potions} ";
					_btnUsePotion.TooltipText = $"[I] Healing Potion (Have: {potions})\nRestores 50 HP on use.";
					_btnUsePotion.Disabled = potions <= 0;
					_commandGrid.AddChild(_btnUsePotion);
				}
			}
		}
	}

	/// <summary>Dims and relabels an upgrade button if the upgrade is already purchased.</summary>
	private void ApplyUpgradeButtonState(Button btn, bool isMaxed, string maxedLabel)
	{
		if (isMaxed)
		{
			btn.Disabled = true;
			btn.TooltipText = $"✓ {maxedLabel} — Already researched!";
			btn.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
		}
		else
		{
			btn.Disabled = false;
			btn.Modulate = Colors.White;
		}
	}

	private void UpgradeSelectedTower()
	{
		var selectedUnits = GameHost.Instance?.SelectedUnits;
		if (selectedUnits != null && selectedUnits.Count > 0)
		{
			int idx = GameHost.Instance != null ? GameHost.Instance.CycleSelectionIndex : 0;
			if (idx >= 0 && idx < selectedUnits.Count)
			{
				var tower = selectedUnits[idx];
				if (!tower.IsEnemy && tower.UnitId == "tower")
				{
					GameHost.Instance?.UpgradeTower(tower);
				}
			}
		}
	}

	private void SetupCommandCard()
	{
		// Mobile unit commands — each has a [Hotkey] label appended
		SetupHUDButton(_btnMove, "res://Assets/UI/move_speed.png", "[M] Move / Right-Click Ground", () => GameHost.Instance?.EnterCommandTargeting("move"));
		SetupHUDButton(_btnStop, "res://Assets/UI/cancel_button_2.png", "[S] Stop Selected Units", () => 
		{
			ShowFeedbackText("Command: Stop Current Action", new Color(0.9f, 0.2f, 0.2f));
			GameHost.Instance?.StopSelectedUnits();
		});
		SetupHUDButton(_btnHold, "res://Assets/UI/magic_upgrade_arrow.png", "[H] Hold Position — Unit stays put and attacks in place", () => 
		{
			ShowFeedbackText("Command: Hold Position", new Color(0.9f, 0.8f, 0.1f));
			GameHost.Instance?.HoldSelectedUnits();
		});
		SetupHUDButton(_btnAttack, "res://Assets/UI/battle_axe.png", "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move", () => GameHost.Instance?.EnterCommandTargeting("attack"));
		SetupHUDButton(_btnPatrol, "res://Assets/UI/patrol.jpg", "[P] Patrol — Unit patrols between current position and target, engaging enemies", () => GameHost.Instance?.EnterCommandTargeting("patrol"));
		SetupHUDButton(_btnBuild, "res://Assets/UI/golden_hammers.png", "[B] Build Structure", () => EnterBuildSubMenu());

		// Build sub-menu commands
		_btnBuildCastle = new Button();
		SetupHUDButton(_btnBuildCastle, "res://Assets/UI/moonlit_castle.png", "[C] Build Castle (Cost: 400 Gold, 300 Wood, 200 Stone)", () => GameHost.Instance?.EnterBuildingPlacement("castle"));
		_btnBuildTower = new Button();
		SetupHUDButton(_btnBuildTower, "res://Assets/UI/unknown_unit_1.png", "[T] Build Spell Tower (Cost: 200 Gold, 150 Wood, 100 Stone)", () => GameHost.Instance?.EnterBuildingPlacement("tower"));
		_btnCancelBuild = new Button();
		SetupHUDButton(_btnCancelBuild, "res://Assets/UI/cancel_button_2.png", "[Esc] Cancel", () => ExitBuildSubMenu());

		// Castle production commands
		_btnTrainFootman = new Button();
		SetupHUDButton(_btnTrainFootman, "res://Assets/UI/heavy_knight.png", "[F] Train Footman (Cost: 100 Gold, 1 Pop) — Heavy armored melee fighter", () => GameHost.Instance?.TrainUnitAtCastle("footman"));
		_btnTrainArcher = new Button();
		SetupHUDButton(_btnTrainArcher, "res://Assets/UI/elf_warrior.png", "[R] Train Archer (Cost: 120 Gold, 40 Wood, 1 Pop) — Ranged elf with high range", () => GameHost.Instance?.TrainUnitAtCastle("archer"));

		_btnTrainPriest = new Button();
		SetupHUDButton(_btnTrainPriest, "res://Assets/UI/alliance_flag.png", "[P] Train Priest (Cost: 140 Gold, 20 Wood, 1 Pop) — Healing support unit", () => GameHost.Instance?.TrainUnitAtCastle("priest"));

		_btnTrainWorker = new Button();
		SetupHUDButton(_btnTrainWorker, "res://Assets/UI/unit_placeholder.png", "[V] Train Worker (Cost: 75 Gold, 1 Pop) — Dedicated gatherer and builder", () => GameHost.Instance?.TrainUnitAtCastle("worker"));

		_btnSetRally = new Button();
		SetupHUDButton(_btnSetRally, "res://Assets/UI/alliance_flag.png", "[Y] Set Rally Point — Set location where new units will walk", () => GameHost.Instance?.EnterCommandTargeting("rally"));
		
		_btnBuyPotion = new Button();
		SetupHUDButton(_btnBuyPotion, "res://Assets/UI/alliance_flag.png", "[I] Buy Potion (Cost: 50 Gold) — Buy a Healing Potion for a nearby combat unit", () => {
			var selected = GameHost.Instance?.SelectedUnits;
			if (selected != null && selected.Count == 1)
			{
				GameHost.Instance.BuyHealingPotion(selected[0].Entity);
			}
		});

		_btnUpgradeWeapons = new Button();
		SetupHUDButton(_btnUpgradeWeapons, "res://Assets/UI/battle_axe.png", "[W] Upgrade Weapons (Cost: 150 Gold, 100 Wood)\nPermanently increases unit damage by +3", () => GameHost.Instance?.BuyWeaponsUpgrade());
		_btnUpgradeShields = new Button();
		SetupHUDButton(_btnUpgradeShields, "res://Assets/UI/battle_shield.png", "[G] Upgrade Armor (Cost: 150 Gold, 100 Stone)\nPermanently increases unit armor by +2", () => GameHost.Instance?.BuyShieldsUpgrade());
		_btnUpgradeHarvesting = new Button();
		SetupHUDButton(_btnUpgradeHarvesting, "res://Assets/UI/gold_coin.png", "[T] Upgrade Harvesting (Cost: 150 Wood, 100 Stone)\nPermanently increases passive resource gathering rates by +50%", () => GameHost.Instance?.BuyHarvestingUpgrade());

		// Tower upgrade commands
		_btnUpgradeTower = new Button();
		SetupHUDButton(_btnUpgradeTower, "res://Assets/UI/magic_upgrade_arrow.png", "[U] Upgrade Tower (Cost: 150 Gold, 100 Stone)", () => UpgradeSelectedTower());
	}

	public void ToggleMinimapTerrain()
	{
		_showMinimapTerrain = !_showMinimapTerrain;
		var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
		if (minimapBg != null)
		{
			minimapBg.Visible = _showMinimapTerrain;
		}
		ShowFeedbackText(_showMinimapTerrain ? "Minimap: Terrain Mode" : "Minimap: Radar Mode", new Color(0.3f, 0.9f, 0.4f));
	}

	private void SetupMinimapButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = ""; 
		btn.TooltipText = tooltip;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.ExpandIcon = true;
		btn.CustomMinimumSize = new Vector2(34, 34);

		var styleNormal = (StyleBoxTexture)UIStyle.CreateButtonNormal().Duplicate();
		styleNormal.ContentMarginLeft = 1;
		styleNormal.ContentMarginRight = 1;
		styleNormal.ContentMarginTop = 1;
		styleNormal.ContentMarginBottom = 1;
		styleNormal.TextureMarginLeft = 4;
		styleNormal.TextureMarginRight = 4;
		styleNormal.TextureMarginTop = 4;
		styleNormal.TextureMarginBottom = 4;

		var styleHover = (StyleBoxTexture)UIStyle.CreateButtonHover().Duplicate();
		styleHover.ContentMarginLeft = 1;
		styleHover.ContentMarginRight = 1;
		styleHover.ContentMarginTop = 1;
		styleHover.ContentMarginBottom = 1;
		styleHover.TextureMarginLeft = 4;
		styleHover.TextureMarginRight = 4;
		styleHover.TextureMarginTop = 4;
		styleHover.TextureMarginBottom = 4;

		var stylePressed = (StyleBoxTexture)UIStyle.CreateButtonPressed().Duplicate();
		stylePressed.ContentMarginLeft = 1;
		stylePressed.ContentMarginRight = 1;
		stylePressed.ContentMarginTop = 1;
		stylePressed.ContentMarginBottom = 1;
		stylePressed.TextureMarginLeft = 4;
		stylePressed.TextureMarginRight = 4;
		stylePressed.TextureMarginTop = 4;
		stylePressed.TextureMarginBottom = 4;

		btn.AddThemeStyleboxOverride("normal", styleNormal);
		btn.AddThemeStyleboxOverride("hover", styleHover);
		btn.AddThemeStyleboxOverride("pressed", stylePressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.TooltipText = tooltip;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.ExpandIcon = true;
		btn.CustomMinimumSize = new Vector2(80, 80);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		btn.ClipContents = true;

		// Parse hotkey from "[X]" prefix in tooltip and render it as a visible corner label
		string hotkeyText = "";
		if (tooltip.StartsWith("[") && tooltip.Contains("]"))
		{
			int end = tooltip.IndexOf(']');
			hotkeyText = tooltip.Substring(1, end - 1); // e.g. "M", "P", "Esc"
		}
		// Remove any stale label from previous calls (button re-use)
		var existingHotkeyLabel = btn.GetNodeOrNull<Label>("HotkeyLabel");
		if (existingHotkeyLabel != null) btn.RemoveChild(existingHotkeyLabel);

		if (!string.IsNullOrEmpty(hotkeyText))
		{
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}

		var styleNormal = (StyleBoxTexture)UIStyle.CreateButtonNormal().Duplicate();
		styleNormal.ContentMarginLeft = 2;
		styleNormal.ContentMarginRight = 2;
		styleNormal.ContentMarginTop = 2;
		styleNormal.ContentMarginBottom = 2;
		styleNormal.TextureMarginLeft = 6;
		styleNormal.TextureMarginRight = 6;
		styleNormal.TextureMarginTop = 6;
		styleNormal.TextureMarginBottom = 6;

		var styleHover = (StyleBoxTexture)UIStyle.CreateButtonHover().Duplicate();
		styleHover.ContentMarginLeft = 2;
		styleHover.ContentMarginRight = 2;
		styleHover.ContentMarginTop = 2;
		styleHover.ContentMarginBottom = 2;
		styleHover.TextureMarginLeft = 6;
		styleHover.TextureMarginRight = 6;
		styleHover.TextureMarginTop = 6;
		styleHover.TextureMarginBottom = 6;

		var stylePressed = (StyleBoxTexture)UIStyle.CreateButtonPressed().Duplicate();
		stylePressed.ContentMarginLeft = 2;
		stylePressed.ContentMarginRight = 2;
		stylePressed.ContentMarginTop = 2;
		stylePressed.ContentMarginBottom = 2;
		stylePressed.TextureMarginLeft = 6;
		stylePressed.TextureMarginRight = 6;
		stylePressed.TextureMarginTop = 6;
		stylePressed.TextureMarginBottom = 6;

		btn.AddThemeStyleboxOverride("normal", styleNormal);
		btn.AddThemeStyleboxOverride("hover", styleHover);
		btn.AddThemeStyleboxOverride("pressed", stylePressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void SetupDevPanel()
	{
		_btnVictory.Flat = false;
		_btnDefeat.Flat = false;

		UIStyle.ApplyButtonText(_btnVictory, "Victory Screen", 13);
		UIStyle.ApplyButtonText(_btnDefeat, "Defeat Screen", 13);

		_btnVictory.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnVictory.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnVictory.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());

		_btnDefeat.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnDefeat.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnDefeat.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());

		_btnVictory.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.GameOver, true);
		};
		_btnDefeat.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.GameOver, false);
		};
		_btnVictory.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		_btnDefeat.MouseEntered += () => UIManager.Instance.PlayHoverSound();

		if (_camera3D != null && _camera3D is CameraControl initialCamCtrl)
		{
			_chkCameraToggle.SetPressedNoSignal(!initialCamCtrl.IsLocked);
		}

		_chkCameraToggle.Toggled += (toggled) =>
		{
			UIManager.Instance.PlayClickSound();
			if (_camera3D != null && _camera3D is CameraControl camCtrl)
			{
				camCtrl.IsLocked = !toggled;
				if (toggled)
				{
					ShowFeedbackText("Camera Control Active: Use WASD/Shift or Mouse edge", new Color(0.3f, 0.8f, 1.0f));
				}
				else
				{
					ShowFeedbackText("Camera Control Locked", new Color(0.8f, 0.8f, 0.8f));
				}
			}
			else
			{
				ShowFeedbackText("No camera control found!", new Color(1.0f, 0.3f, 0.3f));
				_chkCameraToggle.SetPressedNoSignal(false);
			}
		};
		_chkCameraToggle.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void SetupPortrait()
	{
		var portraitLabel = GetNodeOrNull<Label>("BottomConsole/HBox/PortraitFrame/VBox/PortraitLabel");
		if (portraitLabel != null)
		{
			portraitLabel.Visible = false; // Hide the text label
		}

		var portraitTexture = new TextureRect();
		portraitTexture.Name = "PortraitTexture";
		portraitTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
		portraitTexture.CustomMinimumSize = new Vector2(100, 100);
		portraitTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		portraitTexture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		portraitTexture.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		portraitTexture.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		portraitTexture.MouseFilter = MouseFilterEnum.Stop;
		portraitTexture.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				CenterCameraOnSelectedUnit();
			}
		};

		var portraitVBox = GetNodeOrNull<VBoxContainer>("BottomConsole/HBox/PortraitFrame/VBox");
		if (portraitVBox != null)
		{
			portraitVBox.AddChild(portraitTexture);
			portraitVBox.MoveChild(portraitTexture, 0); 
		}
	}

	private void CenterCameraOnSelectedUnit()
	{
		var selectedUnits = GameHost.Instance?.SelectedUnits;
		if (selectedUnits == null || selectedUnits.Count == 0) return;

		var targetUnit = selectedUnits[GameHost.Instance != null ? GameHost.Instance.CycleSelectionIndex : 0];
		if (targetUnit != null && GodotObject.IsInstanceValid(targetUnit))
		{
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(targetUnit.GlobalPosition.X, camera.GlobalPosition.Y, targetUnit.GlobalPosition.Z);
				ShowFeedbackText($"Camera Centered on Selected Unit: {targetUnit.UnitId.ToUpper()}", new Color(0.5f, 0.8f, 1.0f));
			}
		}
	}

	private void SetupMinimap()
	{
		_minimapArea.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (GameHost.Instance != null)
					{
						float xRatio = mouseBtn.Position.X / _minimapArea.Size.X;
						float yRatio = mouseBtn.Position.Y / _minimapArea.Size.Y;
						float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
						float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);
						var minimapWorldPos = new Vector3(worldX, 0f, worldZ);

						if (GameHost.Instance.ActivePingMode)
						{
							GameHost.Instance.AddMinimapPing(minimapWorldPos);
							GameHost.Instance.ActivePingMode = false;
						}
						else if (GameHost.Instance.ActiveCommandTargeting != null)
						{
							string cmd = GameHost.Instance.ActiveCommandTargeting;
							if (cmd == "attack")
							{
								GameHost.Instance.IssueAttackMoveCommand(minimapWorldPos);
							}
							else if (cmd == "move")
							{
								if (Input.IsKeyPressed(Key.Shift))
									GameHost.Instance.IssueMoveCommandQueued(minimapWorldPos);
								else
									GameHost.Instance.IssueMoveCommand(minimapWorldPos);
							}
							else if (cmd == "patrol")
							{
								GameHost.Instance.IssuePatrolCommand(minimapWorldPos);
							}
							else if (cmd == "rally")
							{
								if (GameHost.Instance.SelectedUnits.Count == 1 && 
									!GameHost.Instance.SelectedUnits[0].IsEnemy && 
									GameHost.Instance.SelectedUnits[0].IsBuilding)
								{
									GameHost.Instance.SetRallyPoint(GameHost.Instance.SelectedUnits[0], minimapWorldPos);
								}
							}
							GameHost.Instance.ClearTargetingModes();
						}
						else if (GameHost.Instance.ActiveSpellTargeting != null)
						{
							GameHost.Instance.CastSpellAt(GameHost.Instance.ActiveSpellTargeting, minimapWorldPos);
							GameHost.Instance.ClearTargetingModes();
						}
						else if (GameHost.Instance.ActiveBuildingPlacementType != null)
						{
							GameHost.Instance.PlaceBuildingAt(GameHost.Instance.ActiveBuildingPlacementType, minimapWorldPos);
							GameHost.Instance.ClearTargetingModes();
						}
						else
						{
							TeleportCameraToMinimapPos(mouseBtn.Position);
						}
					}
				}
				else if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					if (GameHost.Instance != null && GameHost.Instance.SelectedUnits.Count > 0)
					{
						float xRatio = mouseBtn.Position.X / _minimapArea.Size.X;
						float yRatio = mouseBtn.Position.Y / _minimapArea.Size.Y;
						float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
						float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);
						var hitPos = new Vector3(worldX, 0f, worldZ);

						if (GameHost.Instance.SelectedUnits.Count == 1 && 
							!GameHost.Instance.SelectedUnits[0].IsEnemy && 
							GameHost.Instance.SelectedUnits[0].IsBuilding)
						{
							GameHost.Instance.SetRallyPoint(GameHost.Instance.SelectedUnits[0], hitPos);
						}
						else
						{
							bool shiftHeld = Input.IsKeyPressed(Key.Shift);
							if (shiftHeld)
							{
								GameHost.Instance.IssueMoveCommandQueued(hitPos);
							}
							else
							{
								GameHost.Instance.IssueMoveCommand(hitPos);
							}
						}
					}
				}
			}
			else if (@event is InputEventMouseMotion mouseMotion && mouseMotion.ButtonMask == MouseButtonMask.Left)
			{
				if (GameHost.Instance == null || (!GameHost.Instance.ActivePingMode && GameHost.Instance.ActiveCommandTargeting == null && GameHost.Instance.ActiveSpellTargeting == null && GameHost.Instance.ActiveBuildingPlacementType == null))
				{
					TeleportCameraToMinimapPos(mouseMotion.Position);
				}
			}
		};
	}

	private void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		float xRatio = clickPos.X / _minimapArea.Size.X;
		float yRatio = clickPos.Y / _minimapArea.Size.Y;

		float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
		float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);

		if (_camera3D != null)
		{
			_camera3D.GlobalPosition = new Vector3(worldX, _camera3D.GlobalPosition.Y, worldZ);
			ShowFeedbackText($"Panned Camera on Minimap to: {worldX:F0}, {worldZ:F0}", new Color(1, 0.85f, 0.5f));
		}
	}

	private void UpdateMinimapIndicator()
	{
		if (_camera3D == null || _cameraIndicator == null || _minimapArea == null) return;

		float worldX = _camera3D.GlobalPosition.X;
		float worldZ = _camera3D.GlobalPosition.Z;

		float xRatio = (worldX / 250f) + 0.5f;
		float yRatio = (worldZ / 250f) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		float xPos = xRatio * _minimapArea.Size.X - (_cameraIndicator.Size.X / 2f);
		float yPos = yRatio * _minimapArea.Size.Y - (_cameraIndicator.Size.Y / 2f);

		_cameraIndicator.Position = new Vector2(xPos, yPos);
	}

	public void UpdateDragBox(Vector2 start, Vector2 end, bool isVisible)
	{
		_dragStart = start;
		_dragEnd = end;
		_isDrawingDragBox = isVisible;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_isDrawingDragBox && _dragStart != _dragEnd)
		{
			var rect = new Rect2(_dragStart, _dragEnd - _dragStart);
			DrawRect(rect, new Color(0.1f, 0.9f, 0.2f, 0.15f), true);
			DrawRect(rect, new Color(0.1f, 0.9f, 0.2f, 0.75f), false, 2.0f);
		}

		// Draw waypoint paths for selected player units
		if (GameHost.Instance != null && _camera3D != null && GameHost.Instance.SelectedUnits.Count > 0)
		{
			var world = GameHost.Instance.EcsWorld;
			foreach (var unit in GameHost.Instance.SelectedUnits)
			{
				if (unit == null || !GodotObject.IsInstanceValid(unit) || unit.IsEnemy || !world.IsAlive(unit.Entity)) continue;

				// Draw path if unit is moving
				if (world.Has<MoveTo>(unit.Entity))
				{
					var moveTo = world.Get<MoveTo>(unit.Entity);
					var current3D = unit.GlobalPosition;
					var points3D = new List<Vector3> { current3D };

					if (world.Has<PathFollow>(unit.Entity))
					{
						var pf = world.Get<PathFollow>(unit.Entity);
						if (pf.WaypointCount > 0 && pf.CurrentWaypointIndex < pf.WaypointCount)
						{
							for (int i = pf.CurrentWaypointIndex; i < pf.WaypointCount; i++)
							{
								points3D.Add(new Vector3(pf.Waypoints[i].X, pf.Waypoints[i].Y, pf.Waypoints[i].Z));
							}
						}
						else
						{
							points3D.Add(new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z));
						}
					}
					else
					{
						points3D.Add(new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z));
					}

					if (world.Has<WaypointQueue>(unit.Entity))
					{
						var q = world.Get<WaypointQueue>(unit.Entity);
						if (q.Waypoints != null)
						{
							foreach (var wp in q.Waypoints)
							{
								points3D.Add(new Vector3(wp.X, wp.Y, wp.Z));
							}
						}
					}

					// Project all 3D points to 2D screen positions
					var points2D = new List<Vector2>();
					bool pathIsVisible = true;
					foreach (var pt in points3D)
					{
						if (_camera3D.IsPositionBehind(pt))
						{
							pathIsVisible = false; // skip drawing if segment is behind camera
						}
						points2D.Add(_camera3D.UnprojectPosition(pt));
					}

					if (pathIsVisible && points2D.Count >= 2)
					{
						Color pathColor = new Color(0.2f, 0.8f, 1.0f, 0.5f); // Semi-transparent Cyan
						// Draw lines connecting the path
						for (int i = 0; i < points2D.Count - 1; i++)
						{
							DrawLine(points2D[i], points2D[i + 1], pathColor, 1.5f);
						}

						// Draw a circle/cross at each waypoint destination
						for (int i = 1; i < points2D.Count; i++)
						{
							DrawCircle(points2D[i], 4f, pathColor);
							DrawCircle(points2D[i], 2f, new Color(1f, 1f, 1f, 0.8f)); // white center dot
						}
					}
				}
			}
		}

		// Floating 3D health bars projected on screen for selected units
		bool showSelectedHp = GameSettings.ShowHealthBars != "hidden";
		bool showAllHp = GameSettings.ShowHealthBars == "visible" || GameSettings.ShowHealthBars == "damaged";

		if (showSelectedHp && GameHost.Instance != null && _camera3D != null)
		{
			foreach (var unit in GameHost.Instance.SelectedUnits)
			{
				if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;

				float height = unit.IsBuilding ? 6.5f : 2.5f;
				Vector3 worldPos = unit.GlobalPosition + new Vector3(0, height, 0);

				if (_camera3D.IsPositionBehind(worldPos)) continue;

				Vector2 screenPos = _camera3D.UnprojectPosition(worldPos);

				float currentHp = 100f, maxHp = 100f;
				if (GameHost.Instance.EcsWorld.IsAlive(unit.Entity) && GameHost.Instance.EcsWorld.Has<Health>(unit.Entity))
				{
					var hp = GameHost.Instance.EcsWorld.Get<Health>(unit.Entity);
					currentHp = hp.Current;
					maxHp = hp.Max;
				}

				float hpPct = Mathf.Clamp(currentHp / maxHp, 0f, 1f);

				float barWidth = unit.IsBuilding ? 80f : 50f;
				float barHeight = 6f;
				Vector2 barSize = new Vector2(barWidth, barHeight);
				Vector2 topLeft = screenPos - new Vector2(barWidth / 2f, barHeight / 2f);

				// Dark border background
				DrawRect(new Rect2(topLeft - new Vector2(1, 1), barSize + new Vector2(2, 2)), new Color(0, 0, 0, 0.8f), true);
				DrawRect(new Rect2(topLeft, barSize), new Color(0.2f, 0.2f, 0.2f, 1.0f), true);

				// Color changes based on percentage
				Color hpColor = new Color(0.1f, 0.9f, 0.2f); // Green
				if (hpPct < 0.35f)
					hpColor = new Color(0.9f, 0.2f, 0.1f); // Red
				else if (hpPct < 0.7f)
					hpColor = new Color(0.9f, 0.7f, 0.1f); // Yellow

				// *** THE FIX: actually draw the HP fill bar ***
				DrawRect(new Rect2(topLeft, new Vector2(barWidth * hpPct, barHeight)), hpColor, true);

			}
		}

		// Also draw health bars above ALL units on screen (not just selected) at reduced opacity
		if (showAllHp && GameHost.Instance != null && _camera3D != null)
		{
			foreach (var unit in GameHost.Instance.AllUnits)
			{
				if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
				if (unit.IsSelected) continue; // Already drawn above
				if (!GameHost.Instance.EcsWorld.IsAlive(unit.Entity)) continue;
				if (!GameHost.Instance.EcsWorld.Has<Health>(unit.Entity)) continue;

				float height = unit.IsBuilding ? 6.5f : 2.5f;
				Vector3 worldPos = unit.GlobalPosition + new Vector3(0, height, 0);
				if (_camera3D.IsPositionBehind(worldPos)) continue;
				Vector2 screenPos = _camera3D.UnprojectPosition(worldPos);

				var hp = GameHost.Instance.EcsWorld.Get<Health>(unit.Entity);
				float hpPct = Mathf.Clamp(hp.Current / hp.Max, 0f, 1f);
				// Only show if damaged in damaged mode
				if (GameSettings.ShowHealthBars == "damaged" && hpPct >= 1.0f) continue;

				float barWidth = unit.IsBuilding ? 60f : 36f;
				float barHeight = 4f;
				Vector2 topLeft = screenPos - new Vector2(barWidth / 2f, barHeight / 2f);

				DrawRect(new Rect2(topLeft - Vector2.One, new Vector2(barWidth + 2, barHeight + 2)), new Color(0, 0, 0, 0.5f), true);
				DrawRect(new Rect2(topLeft, new Vector2(barWidth, barHeight)), new Color(0.15f, 0.15f, 0.15f, 0.8f), true);

				Color col = hpPct < 0.35f ? new Color(0.9f, 0.2f, 0.1f, 0.8f)
							: hpPct < 0.7f ? new Color(0.9f, 0.7f, 0.1f, 0.8f)
							: (unit.IsEnemy ? new Color(0.9f, 0.2f, 0.1f, 0.7f) : new Color(0.1f, 0.9f, 0.2f, 0.7f));
				DrawRect(new Rect2(topLeft, new Vector2(barWidth * hpPct, barHeight)), col, true);
			}
		}
	}

	private void UpdateControlGroupsUI()
	{
		if (_controlGroupsContainer == null || GameHost.Instance == null) return;

		// Clear children first
		foreach (Node child in _controlGroupsContainer.GetChildren())
		{
			_controlGroupsContainer.RemoveChild(child);
			child.QueueFree();
		}

		var groups = GameHost.Instance.ControlGroups;
		if (groups == null) return;

		for (int i = 1; i <= 10; i++)
		{
			int groupIdx = i % 10;
			var group = groups[groupIdx];
			if (group == null) continue;

			group.RemoveAll(u => !GodotObject.IsInstanceValid(u) || !GameHost.Instance.AllUnits.Contains(u));
			if (group.Count == 0) continue;

			var btn = new Button();
			btn.Text = $" {groupIdx} : {group.Count} ";
			btn.TooltipText = $"Control Group {groupIdx} ({group.Count} Units)\nClick to recall.";
			btn.FocusMode = FocusModeEnum.None;
			btn.CustomMinimumSize = new Vector2(50, 30);

			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover",   UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			btn.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			btn.AddThemeFontSizeOverride("font_size", 12);

			int idx = groupIdx;
			btn.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				GameHost.Instance.RecallControlGroup(idx);
			};

			_controlGroupsContainer.AddChild(btn);
		}
	}

	private void OnUnitSelectionButtonClicked(int index)
	{
		var selectedUnits = GameHost.Instance?.SelectedUnits;
		if (selectedUnits == null || index >= selectedUnits.Count) return;

		var clickedUnit = selectedUnits[index];
		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);

		if (ctrlPressed)
		{
			string targetId = clickedUnit.UnitId;
			var toDeselect = new List<Unit3D>();
			foreach (var u in selectedUnits)
			{
				if (u.UnitId != targetId)
				{
					toDeselect.Add(u);
				}
			}
			foreach (var u in toDeselect)
			{
				GameHost.Instance.DeselectUnit(u);
			}
		}
		else if (shiftPressed)
		{
			GameHost.Instance.DeselectUnit(clickedUnit);
		}
		else
		{
			GameHost.Instance.SelectOnlyUnit(clickedUnit);
		}
	}

	private void PopulateQueueSlots(Entity castleEntity, List<string> unitIds)
	{
		ClearQueueSlots();

		for (int i = 0; i < unitIds.Count; i++)
		{
			string unitId = unitIds[i];
			int index = i;
			var btn = new Button();
			btn.CustomMinimumSize = new Vector2(40, 40);
			btn.ExpandIcon = true;
			btn.Icon = GD.Load<Texture2D>(GetUnitIcon(unitId));
			btn.TooltipText = $"Queued: {unitId.ToUpper()}\nClick to cancel and refund resources.";
			btn.FocusMode = FocusModeEnum.None;

			var style = (StyleBoxTexture)UIStyle.CreateButtonNormal().Duplicate();
			style.ContentMarginLeft = 1;
			style.ContentMarginRight = 1;
			style.ContentMarginTop = 1;
			style.ContentMarginBottom = 1;
			style.TextureMarginLeft = 4;
			style.TextureMarginRight = 4;
			style.TextureMarginTop = 4;
			style.TextureMarginBottom = 4;

			btn.AddThemeStyleboxOverride("normal", style);
			btn.AddThemeStyleboxOverride("hover", style);
			btn.AddThemeStyleboxOverride("pressed", style);

			btn.Pressed += () =>
			{
				GameHost.Instance?.CancelQueuedUnitAt(castleEntity, index);
			};

			_queueSlotsContainer.AddChild(btn);
		}
	}

	private void ClearQueueSlots()
	{
		if (_queueSlotsContainer == null) return;
		foreach (Node child in _queueSlotsContainer.GetChildren())
		{
			_queueSlotsContainer.RemoveChild(child);
			child.QueueFree();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
			{
				if (_chatInput != null)
				{
					if (!_chatInput.Visible)
					{
						ShowChatInput();
						GetViewport().SetInputAsHandled();
					}
				}
			}
			else if (keyEvent.Keycode == Key.Escape)
			{
				if (_chatInput != null && _chatInput.Visible)
				{
					HideChatInput();
					GetViewport().SetInputAsHandled();
				}
			}
			else if (keyEvent.Keycode == Key.F5)
			{
				ToggleHotkeyPanel();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void ShowChatInput()
	{
		_isChatActive = true;
		_chatInput.Visible = true;
		_chatInput.GrabFocus();
	}

	private void HideChatInput()
	{
		_isChatActive = false;
		_chatInput.Clear();
		_chatInput.Visible = false;
		_chatInput.ReleaseFocus();
	}

	private void OnChatInputSubmitted(string text)
	{
		HideChatInput();
		
		if (string.IsNullOrWhiteSpace(text)) return;
		
		string trimmedText = text.Trim();

		if (GameHost.Instance != null)
		{
			GameHost.Instance.TriggerPlayerChatMessage(trimmedText);
		}
		
		if (TryTriggerCheat(trimmedText))
		{
			return;
		}
		
		string sender = LobbyManager.Instance?.LocalPlayer?.Name ?? "Player";
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.SendChatMessage(sender, trimmedText);
		}
		else
		{
			OnLobbyChatReceived(sender, trimmedText);
		}
	}

	private void OnLobbyChatReceived(string senderName, string message)
	{
		string color = senderName == "System" ? "#ff5555" : (senderName == (LobbyManager.Instance?.LocalPlayer?.Name ?? "Player") ? "#55ff55" : "#55aaff");
		_chatLog.Text += $"[color={color}]{senderName}[/color]: {message}\n";
	}

	private bool TryTriggerCheat(string text)
	{
		string lower = text.ToLowerInvariant().Trim();
		
		if (lower == "stonks" || lower == "securethebag")
		{
			_gold += 10000f;
			_wood += 10000f;
			_stone += 10000f;
			ShowFeedbackText("Cheat Activated: Stonks! (+10,000 resources)", new Color(0.95f, 0.82f, 0.55f));
			_chatLog.Text += "[color=#ffd700]System: Cheat 'stonks' activated. Added 10,000 resources.[/color]\n";
			return true;
		}
		
		if (lower == "gigachad" || lower == "maincharacter")
		{
			var selected = GameHost.Instance?.SelectedUnits;
			if (selected != null && selected.Count > 0)
			{
				var world = GameHost.Instance.EcsWorld;
				int affected = 0;
				foreach (var unit in selected)
				{
					if (world.IsAlive(unit.Entity))
					{
						if (world.Has<Health>(unit.Entity))
						{
							world.Set(unit.Entity, new Health(9000f, 9000f));
						}
						if (world.Has<Attack>(unit.Entity))
						{
							var atk = world.Get<Attack>(unit.Entity);
							world.Set(unit.Entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
						}
						affected++;
					}
				}
				ShowFeedbackText($"Cheat Activated: Gigachad Main Character Energy! ({affected} units empowered)", new Color(1.0f, 0.3f, 0.1f));
				_chatLog.Text += $"[color=#ffd700]System: Cheat 'gigachad' activated. Powered up {affected} units.[/color]\n";
				RefreshUI(selected);
			}
			else
			{
				ShowFeedbackText("Cheat failed: Select some units first!", new Color(0.8f, 0.3f, 0.3f));
			}
			return true;
		}

		if (lower == "skibidi" || lower == "rizz" || lower == "absoluteunit")
		{
			var selected = GameHost.Instance?.SelectedUnits;
			if (selected != null && selected.Count > 0)
			{
				int affected = 0;
				foreach (var unit in selected)
				{
					if (GodotObject.IsInstanceValid(unit))
					{
						unit.Scale = new Vector3(3f, 3f, 3f);
						
						var world = GameHost.Instance.EcsWorld;
						if (world.IsAlive(unit.Entity) && world.Has<MovementStats>(unit.Entity))
						{
							var mv = world.Get<MovementStats>(unit.Entity);
							world.Set(unit.Entity, new MovementStats(25f, mv.Acceleration, mv.TurnRate));
						}
						affected++;
					}
				}
				ShowFeedbackText($"Cheat Activated: Absolute Unit! (+Scale, +Speed) on {affected} units", new Color(0.2f, 0.8f, 1.0f));
				_chatLog.Text += $"[color=#ffd700]System: Cheat 'absoluteunit' activated. Gigantified {affected} units with super speed![/color]\n";
				RefreshUI(selected);
			}
			else
			{
				ShowFeedbackText("Cheat failed: Select some units first!", new Color(0.8f, 0.3f, 0.3f));
			}
			return true;
		}

		if (lower == "thanossnap" || lower == "emotionaldamage")
		{
			if (GameHost.Instance != null)
			{
				int destroyed = 0;
				var unitsCopy = new List<Unit3D>(GameHost.Instance.AllUnits);
				foreach (var unit in unitsCopy)
				{
					if (unit != null && GodotObject.IsInstanceValid(unit) && unit.IsEnemy)
					{
						var world = GameHost.Instance.EcsWorld;
						if (world.IsAlive(unit.Entity) && world.Has<Health>(unit.Entity))
						{
							world.Set(unit.Entity, new Health(0f, world.Get<Health>(unit.Entity).Max));
							destroyed++;
						}
					}
				}
				ShowFeedbackText($"Cheat Activated: Thanos Snapped. Destroyed {destroyed} enemies.", new Color(0.9f, 0.1f, 0.1f));
				_chatLog.Text += $"[color=#ffd700]System: Cheat 'thanossnap' activated. Slain {destroyed} enemy units.[/color]\n";
			}
			return true;
		}

		if (lower == "ezclap" || lower == "speedrun")
		{
			ShowFeedbackText("Cheat Activated: EZ Clap Speedrun!", new Color(0.1f, 0.9f, 0.2f));
			_chatLog.Text += "[color=#ffd700]System: Cheat 'ezclap' activated. Proceeding to Victory.[/color]\n";
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.GameOver, true);
			return true;
		}

		return false;
	}

	// ─── HOTKEY REFERENCE PANEL ─────────────────────────────────────────────────
	private void BuildHotkeyReferencePanel()
	{
		_hotkeyPanel = new PanelContainer();
		_hotkeyPanel.Name = "HotkeyPanel";
		// Position: upper-right corner
		_hotkeyPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
		_hotkeyPanel.OffsetRight = -10;
		_hotkeyPanel.OffsetTop = 80;
		_hotkeyPanel.OffsetLeft = -380;
		_hotkeyPanel.OffsetBottom = 600;
		_hotkeyPanel.Visible = false;
		_hotkeyPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		AddChild(_hotkeyPanel);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_hotkeyPanel.AddChild(scroll);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(vbox);

		var titleLbl = new Label();
		titleLbl.Text = "HOTKEY REFERENCE — [F5] to close";
		UIStyle.ApplyTitle(titleLbl, "HOTKEY REFERENCE — [F5] to close", 13);
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(titleLbl);

		var sep = new HSeparator();
		vbox.AddChild(sep);

		var hotkeys = new (string key, string desc)[] {
			("Right-click",      "Smart command (move / attack / follow / rally)"),
			("Shift+Right-click","Queue additional move waypoint"),
			("Left-click",       "Select unit (drag to box-select)"),
			("Double-click",     "Select all same-type on screen"),
			("Shift+click",      "Add/remove unit from selection"),
			("", ""),
			("M",   "Move command (then left-click ground)"),
			("A",   "Attack / Attack-Move command"),
			("P",   "Patrol command"),
			("S",   "Stop all selected units"),
			("H",   "Hold Position"),
			("B",   "Open Build submenu"),
			("C",   "Build Castle (in build submenu)"),
			("T",   "Build Tower (in build submenu)"),
			("", ""),
			("Castle selected:", ""),
			("F",   "Train Footman"),
			("R",   "Train Archer"),
			("P",   "Train Priest"),
			("V",   "Train Worker"),
			("Y",   "Set Rally Point"),
			("I",   "Buy Healing Potion"),
			("W",   "Upgrade Weapons"),
			("G",   "Upgrade Armor"),
			("T",   "Upgrade Harvesting"),
			("", ""),
			("Tower selected:", ""),
			("U",   "Upgrade Tower"),
			("Q",   "Cast Fireball"),
			("E",   "Cast Lightning"),
			("", ""),
			("Priest selected:", ""),
			("W",   "Cast Holy Light"),
			("I",   "Use Healing Potion"),
			("", ""),
			("Camera:", ""),
			("WASD / Edge","Pan camera"),
			("Shift+WASD", "Fast pan"),
			("Space",      "Center on your Castle"),
			("Z",          "Cycle camera zoom"),
			("", ""),
			("Selection:", ""),
			("F1",         "Select all Idle units"),
			("F2",         "Select all Army units"),
			("F3",         "Select all Buildings"),
			("Tab",        "Cycle focus through selected"),
			("`",          "Cycle through your buildings"),
			("Ctrl+0..9",  "Assign Control Group"),
			("0..9",       "Recall Control Group (double: jump)"),
			("", ""),
			("Map:", ""),
			("F4",         "Toggle Minimap terrain / radar"),
			("Alt+G",      "Ping map alert"),
			("", ""),
			("Chat / Cheats:", ""),
			("Enter",      "Open chat (also type cheats here)"),
			("stonks",     "Cheat: +10000 resources"),
			("gigachad",   "Cheat: godmode selected units"),
			("thanossnap", "Cheat: kill all enemies"),
			("ezclap",     "Cheat: instant victory"),
			("", ""),
			("[F5]",       "Toggle this hotkey panel"),
			("[Esc]",      "Cancel / clear selection / open settings"),
			("[Del]",      "Remove selected units (dev)"),
		};

		foreach (var (key, desc) in hotkeys)
		{
			if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(desc))
			{
				var spacer = new Label();
				spacer.Text = "";
				spacer.CustomMinimumSize = new Vector2(0, 4);
				vbox.AddChild(spacer);
				continue;
			}
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var keyLbl = new Label();
			keyLbl.Text = key;
			keyLbl.CustomMinimumSize = new Vector2(130, 0);
			keyLbl.AddThemeFontSizeOverride("font_size", 11);
			keyLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			var descLbl = new Label();
			descLbl.Text = desc;
			descLbl.AddThemeFontSizeOverride("font_size", 11);
			descLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			descLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			descLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(keyLbl);
			row.AddChild(descLbl);
			vbox.AddChild(row);
		}
	}

	public void ToggleHotkeyPanel()
	{
		if (_hotkeyPanel == null) return;
		_hotkeyPanelVisible = !_hotkeyPanelVisible;
		_hotkeyPanel.Visible = _hotkeyPanelVisible;
		ShowFeedbackText(_hotkeyPanelVisible ? "Hotkey Reference — Press F5 to close" : "Hotkey Reference closed", new Color(0.5f, 0.8f, 1.0f));
	}

	public void StartCountdownTimer(float duration, string labelText)
	{
		_countdownDuration = duration;
		_countdownText = labelText;
		_countdownActive = true;
		_countdownPanel.Visible = true;
		_countdownLabel.Text = $"{_countdownText}: {(int)Math.Ceiling(_countdownDuration)}s";
	}

	public void StopCountdownTimer()
	{
		_countdownActive = false;
		_countdownPanel.Visible = false;
	}

	public void UpdateCountdownLabel(string labelText)
	{
		_countdownText = labelText;
		if (_countdownActive)
			_countdownLabel.Text = $"{_countdownText}: {(int)Math.Ceiling(_countdownDuration)}s";
	}

	public void SetLeaderboardVisible(string title, bool visible)
	{
		_leaderboardTitleLabel.Text = title.ToUpper();
		_leaderboardPanel.Visible = visible;
	}

	public void ClearLeaderboard()
	{
		foreach (Node child in _leaderboardContent.GetChildren())
		{
			_leaderboardContent.RemoveChild(child);
			child.QueueFree();
		}
	}

	public void SetLeaderboardValue(string label, string value)
	{
		HBoxContainer? rowBox = _leaderboardContent.GetNodeOrNull<HBoxContainer>(label);
		if (rowBox == null)
		{
			var hBox = new HBoxContainer();
			hBox.Name = label;
			hBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

			var lblName = new Label();
			lblName.Name = "NameLabel";
			lblName.Text = label;
			lblName.AddThemeFontSizeOverride("font_size", 13);
			lblName.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			hBox.AddChild(lblName);

			var lblSpacer = new Control();
			lblSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			hBox.AddChild(lblSpacer);

			var lblVal = new Label();
			lblVal.Name = "ValueLabel";
			lblVal.Text = value;
			lblVal.AddThemeFontSizeOverride("font_size", 13);
			lblVal.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
			hBox.AddChild(lblVal);

			_leaderboardContent.AddChild(hBox);
		}
		else
		{
			var lblVal = rowBox.GetNode<Label>("ValueLabel");
			lblVal.Text = value;
		}
	}
}

// Procedural radar mini-map display (unused legacy frame border)
public partial class MinimapRadar : Control
{
	public override void _Draw()
	{
		Vector2 size = Size;
		DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.07f, 0.05f), true); // Deep green backdrop
		Color radarColor = new Color(0.1f, 0.5f, 0.15f, 0.35f);
		
		DrawLine(new Vector2(size.X / 2f, 0), new Vector2(size.X / 2f, size.Y), radarColor, 1.0f);
		DrawLine(new Vector2(0, size.Y / 2f), new Vector2(size.X, size.Y / 2f), radarColor, 1.0f);

		DrawCircle(size / 2f, size.X * 0.45f, radarColor, false, 1.5f);
		DrawCircle(size / 2f, size.X * 0.3f, radarColor, false, 1.0f);
		DrawCircle(size / 2f, size.X * 0.15f, radarColor, false, 1.0f);
		
		DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 1.5f);
	}
}

// Dynamic Minimap overlay to procedurally draw unit positions
public partial class MinimapOverlay : Control
{
	public override void _Draw()
	{
		if (GameHost.Instance == null) return;

		var size = Size;

		// 1. Draw radar background if terrain screenshot is hidden
		if (InGameHUD.Instance != null && !InGameHUD.Instance.ShowMinimapTerrain)
		{
			DrawRect(new Rect2(Vector2.Zero, size), new Color(0.04f, 0.08f, 0.04f), true); // Deep radar green background
			Color radarGridColor = new Color(0.1f, 0.4f, 0.15f, 0.3f);
			
			// Draw grid lines
			DrawLine(new Vector2(size.X / 2f, 0), new Vector2(size.X / 2f, size.Y), radarGridColor, 1.0f);
			DrawLine(new Vector2(0, size.Y / 2f), new Vector2(size.X, size.Y / 2f), radarGridColor, 1.0f);
			
			// Concentric circles
			DrawCircle(size / 2f, size.X * 0.45f, radarGridColor, false, 1.5f);
			DrawCircle(size / 2f, size.X * 0.3f, radarGridColor, false, 1.0f);
			DrawCircle(size / 2f, size.X * 0.15f, radarGridColor, false, 1.0f);

			// Runic outline border
			DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 1.5f);
		}
		
		// 2. Draw unit positions
		foreach (var unit in GameHost.Instance.AllUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;

			// Map 3D coordinates (-125 to 125) to 2D ratio (0 to 1)
			float xRatio = (unit.GlobalPosition.X / 250f) + 0.5f;
			float yRatio = (unit.GlobalPosition.Z / 250f) + 0.5f;

			xRatio = Mathf.Clamp(xRatio, 0f, 1f);
			yRatio = Mathf.Clamp(yRatio, 0f, 1f);

			Vector2 drawPos = new Vector2(xRatio * size.X, yRatio * size.Y);

			// Determine size, shape, and color based on unit configurations
			Color color = new Color(0.2f, 0.6f, 1.0f); // Default blue
			float iconSize = 5.0f;

			if (unit.IsEnemy)
			{
				if (unit.IsBuilding)
				{
					iconSize = 8.0f;
					color = new Color(0.9f, 0.1f, 0.1f); // Red Enemy Building
					var rect = new Rect2(drawPos - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
					DrawRect(rect, color, true);
					DrawRect(rect, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
				else
				{
					color = new Color(0.9f, 0.3f, 0.1f); // Orange-Red Enemy Unit
					DrawCircle(drawPos, iconSize, color);
					DrawCircle(drawPos, iconSize, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
			}
			else
			{
				if (unit.IsBuilding)
				{
					iconSize = 8.0f;
					if (unit.UnitId == "castle")
						color = new Color(0.9f, 0.7f, 0.1f); // Gold Castle
					else
						color = new Color(0.1f, 0.8f, 0.8f); // Cyan Spell Tower

					var rect = new Rect2(drawPos - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
					DrawRect(rect, color, true);
					DrawRect(rect, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
				else
				{
					if (unit.UnitId == "archer")
						color = new Color(0.2f, 0.8f, 0.3f); // Green Elf Archer
					else
						color = new Color(0.2f, 0.5f, 0.9f); // Blue Footman

					DrawCircle(drawPos, iconSize, color);
					DrawCircle(drawPos, iconSize, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
			}

			// Draw selection ring on overlay if selected
			if (unit.IsSelected)
			{
				Color selColor = unit.IsEnemy ? new Color(0.9f, 0.1f, 0.2f) : new Color(0.1f, 0.9f, 0.2f);
				DrawCircle(drawPos, iconSize + 2.5f, selColor, false, 1.2f);
			}
		}

		// 3. Draw map alert pings
		foreach (var ping in GameHost.Instance.ActivePings)
		{
			float xRatio = (ping.WorldPos.X / 250f) + 0.5f;
			float yRatio = (ping.WorldPos.Z / 250f) + 0.5f;

			xRatio = Mathf.Clamp(xRatio, 0f, 1f);
			yRatio = Mathf.Clamp(yRatio, 0f, 1f);

			Vector2 drawPos = new Vector2(xRatio * size.X, yRatio * size.Y);

			// Pulsating animation scale
			float pulse = Mathf.Sin(ping.LifeTime * 15f) * 0.5f + 1.0f;
			float radius = 12f * pulse;

			DrawCircle(drawPos, radius, new Color(1f, 0.1f, 0.1f, 0.5f), false, 2.0f);
			DrawCircle(drawPos, radius - 4f, new Color(1f, 0.1f, 0.1f, 0.2f), true);
		}
	}}
