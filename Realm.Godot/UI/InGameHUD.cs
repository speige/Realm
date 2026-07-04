using Arch.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Godot.ReplaySystem;
using System;
using System.Collections.Generic;
using Vector3 = Godot.Vector3;

public partial class InGameHUD : Control
{
	public static InGameHUD Instance { get; private set; }

	private InGameHUDViewModel _viewModel = new();

	public float Gold { get => _viewModel.Gold; set => _viewModel.Gold = value; }
	public float Wood { get => _viewModel.Wood; set => _viewModel.Wood = value; }
	public float Stone { get => _viewModel.Stone; set => _viewModel.Stone = value; }
	public float ResourceGatherMultiplier { get => _viewModel.ResourceGatherMultiplier; set => _viewModel.ResourceGatherMultiplier = value; }

	private PanelContainer _resourceContainer;
	private PanelContainer _bottomConsole;
	private PanelContainer _minimapFrame;
	private PanelContainer _portraitFrame;
	private PanelContainer _selectionFrame;
	private PanelContainer _commandFrame;
	private Panel _devPanel;

	private GridContainer _commandGrid;
	public bool IsBuildSubMenuOpen => _viewModel.IsBuildSubMenuOpen;

	public void EnterBuildSubMenu()
	{
		_viewModel.IsBuildSubMenuOpen = true;
		if (GameHost.Instance != null)
		{
			RefreshUI(GameHost.Instance.SelectedUnits);
		}
	}

	public void ExitBuildSubMenu()
	{
		_viewModel.IsBuildSubMenuOpen = false;
		if (GameHost.Instance != null)
		{
			RefreshUI(GameHost.Instance.SelectedUnits);
		}
	}

	private Button _btnBuildCastle;
	private Button _btnBuildTower;
	private Button _btnCancelBuild;

	private Button _btnTrainSoldier;
	private Button _btnTrainArcher;
	private Button _btnTrainPriest;
	private Button _btnTrainWorker;
	private Button _btnBuyPotion;
	private Button _btnUpgradeWeapons;
	private Button _btnUpgradeShields;
	private Button _btnUpgradeHarvesting;
	private Button _btnUsePotion;

	private Button _btnUpgradeTower;
	private Button _btnSetRally;

	private Button _btnFireball;
	private Button _btnLightning;
	private Button _btnHolyLight;
	private VBoxContainer _fireballVBox;
	private VBoxContainer _lightningVBox;
	private VBoxContainer _holyLightVBox;

	private VBoxContainer _productionBox;
	private Label _productionTitle;
	private ProgressBar _productionProgress;
	private Label _productionQueueLabel;
	private HBoxContainer _queueSlotsContainer;

	private VBoxContainer _minimapControls;
	private Button _btnZoom;
	private Button _btnToggleTerrain;
	private Button _btnPing;
	private Button _btnCenter;
	public bool ShowMinimapTerrain => _viewModel.ShowMinimapTerrain;


	public byte[,] FogGrid
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				if (GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<FogAndWeatherState>(GameHost.Instance.WorldEntity))
				{
					return GameHost.Instance.EcsWorld.Get<FogAndWeatherState>(GameHost.Instance.WorldEntity).FogGrid;
				}
			}
			return new byte[32, 32];
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				if (GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<FogAndWeatherState>(GameHost.Instance.WorldEntity))
				{
					ref var state = ref GameHost.Instance.EcsWorld.Get<FogAndWeatherState>(GameHost.Instance.WorldEntity);
					state.FogGrid = value;
				}
			}
		}
	}
	private byte[,] _fogGrid { get => FogGrid; set => FogGrid = value; }

	public string FogOfWarType
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				if (GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<FogAndWeatherState>(GameHost.Instance.WorldEntity))
				{
					return GameHost.Instance.EcsWorld.Get<FogAndWeatherState>(GameHost.Instance.WorldEntity).FogOfWarType;
				}
			}
			return "grey";
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				if (GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<FogAndWeatherState>(GameHost.Instance.WorldEntity))
				{
					ref var state = ref GameHost.Instance.EcsWorld.Get<FogAndWeatherState>(GameHost.Instance.WorldEntity);
					state.FogOfWarType = value;
				}
			}
		}
	}
	private string _fogOfWarType { get => FogOfWarType; set => FogOfWarType = value; }

	private string _currentWeather
	{
		get => GameHost.Instance?.EnvironmentService?.GetCurrentWeather() ?? "clear";
		set => GameHost.Instance?.EnvironmentService?.SetCurrentWeather(value);
	}

	private CpuParticles3D _rainParticles = null;

	private float _baseFogDensity
	{
		get => GameHost.Instance?.EnvironmentService?.GetBaseFogDensity() ?? 0f;
		set => GameHost.Instance?.EnvironmentService?.SetBaseFogDensity(value);
	}

	private Label _goldLabel;
	private Label _woodLabel;
	private Label _stoneLabel;

	private List<Button> _unitButtons = new List<Button>();
	private HBoxContainer _unitsContainer;

	private HBoxContainer _statsContainer;
	private Label _statsLabel;
	private VBoxContainer _spellsBox;
	private VBoxContainer _itemsBox;

	private Vector2 _dragStart;
	private Vector2 _dragEnd;
	private bool _isDrawingDragBox = false;

	private Button _btnMove;
	private Button _btnStop;
	private Button _btnHold;
	private Button _btnAttack;
	private Button _btnBuild;
	private Button _btnPatrol;

	private Button _btnVictory;
	private Button _btnDefeat;

	private Label _feedbackLabel;
	private Label _connectionWarningLabel;
	private Control _minimapArea;
	private Control _cameraIndicator;

	private Label _populationLabel;
	private Label _clockLabel;

	private VBoxContainer _customUIPanel;
	private PanelContainer _leaderboardPanel;
	private VBoxContainer _leaderboardContent;
	private Label _leaderboardTitleLabel;
	private PanelContainer _countdownPanel;
	private Label _countdownLabel;

	private Label _unitDescLabel;

	private HBoxContainer _controlGroupsContainer;
	private Button _btnSelectIdle;
	private Button _btnSelectArmy;

	private float _idlePulseTimer = 0f;
	private int _lastIdleCount = 0;

	private PanelContainer _hotkeyPanel;
	private bool _hotkeyPanelVisible = false;

	private Label _armyCompositionLabel;

	private float _incomeUpdateTimer = 0f;

	private ProgressBar _fireballCooldownBar;
	private ProgressBar _lightningCooldownBar;
	private ProgressBar _holyLightCooldownBar;

	private Camera3D _camera3D;

	private PanelContainer _chatPanel;
	private LineEdit _chatInput;
	private RichTextLabel _chatLog;
	public bool IsChatActive => _chatPanelController.IsChatActive;

	public int LiveSpectatorPerspective
	{
		get => GameHost.Instance?.SpectatorService?.GetSpectatorPerspective() ?? -1;
		set => GameHost.Instance?.SpectatorService?.SetSpectatorPerspective(value);
	}

	private ResourcePanel _resourcePanelController;
	private MinimapPanel _minimapPanelController;
	private ChatPanel _chatPanelController;
	private LeaderboardPanel _leaderboardPanelController;
	private PortraitPanel _portraitPanelController;
	private CommandPanel _commandPanelController;
	private ControlGroupsUIController _controlGroupsUIController;

	public override void _Ready()
	{
		Instance = this;

		_resourceContainer = GetNode<PanelContainer>("ResourceContainer");
		_bottomConsole = GetNode<PanelContainer>("BottomConsole");
		_minimapFrame = GetNode<PanelContainer>("BottomConsole/HBox/MinimapFrame");
		_portraitFrame = GetNode<PanelContainer>("BottomConsole/HBox/PortraitFrame");
		_selectionFrame = GetNode<PanelContainer>("BottomConsole/HBox/SelectionFrame");
		_commandFrame = GetNode<PanelContainer>("BottomConsole/HBox/CommandFrame");
		_devPanel = GetNode<Panel>("DevPanel");

		_goldLabel = GetNode<Label>("ResourceContainer/HBox/GoldBox/GoldLabel");
		_woodLabel = GetNode<Label>("ResourceContainer/HBox/WoodBox/WoodLabel");
		_stoneLabel = GetNode<Label>("ResourceContainer/HBox/StoneBox/StoneLabel");

		var resHBox = GetNode<HBoxContainer>("ResourceContainer/HBox");
		var popBox = new VBoxContainer();
		popBox.AddThemeConstantOverride("separation", 2);
		resHBox.AddChild(popBox);
		var popTitleLbl = new Label();
		popTitleLbl.Text = TranslationServer.Translate("SUPPLY");
		popTitleLbl.AddThemeFontSizeOverride("font_size", 15);
		popTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		popBox.AddChild(popTitleLbl);
		_populationLabel = new Label();
		_populationLabel.Text = "0 / 20";
		_populationLabel.AddThemeFontSizeOverride("font_size", 24);
		_populationLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		popBox.AddChild(_populationLabel);

		var clockBox = new VBoxContainer();
		clockBox.AddThemeConstantOverride("separation", 2);
		resHBox.AddChild(clockBox);
		var clockTitleLbl = new Label();
		clockTitleLbl.Text = TranslationServer.Translate("TIME");
		clockTitleLbl.AddThemeFontSizeOverride("font_size", 15);
		clockTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		clockBox.AddChild(clockTitleLbl);
		_clockLabel = new Label();
		_clockLabel.Text = "0:00";
		_clockLabel.AddThemeFontSizeOverride("font_size", 24);
		_clockLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		clockBox.AddChild(_clockLabel);

		_unitsContainer = GetNode<HBoxContainer>("BottomConsole/HBox/SelectionFrame/UnitsContainer");

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

		_btnVictory = GetNode<Button>("DevPanel/BtnVictory");
		_btnDefeat = GetNode<Button>("DevPanel/BtnDefeat");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");

		_connectionWarningLabel = new Label();
		_connectionWarningLabel.Name = "ConnectionWarningLabel";
		_connectionWarningLabel.Text = TranslationServer.Translate("Connection to host lost ... Reconnecting");
		_connectionWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_connectionWarningLabel.VerticalAlignment = VerticalAlignment.Center;
		_connectionWarningLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.2f, 0.2f));
		_connectionWarningLabel.AddThemeFontSizeOverride("font_size", 22);
		
		var warningStyle = new StyleBoxFlat();
		warningStyle.BgColor = new Color(0.12f, 0.05f, 0.05f, 0.7f);
		warningStyle.BorderColor = new Color(0.8f, 0.2f, 0.2f, 0.5f);
		warningStyle.SetBorderWidthAll(2);
		warningStyle.ContentMarginLeft = 20;
		warningStyle.ContentMarginRight = 20;
		warningStyle.ContentMarginTop = 8;
		warningStyle.ContentMarginBottom = 8;
		_connectionWarningLabel.AddThemeStyleboxOverride("normal", warningStyle);
		
		AddChild(_connectionWarningLabel);
		_connectionWarningLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
		_connectionWarningLabel.GrowHorizontal = GrowDirection.Both;
		_connectionWarningLabel.OffsetTop = 20;
		_connectionWarningLabel.OffsetLeft = -250;
		_connectionWarningLabel.OffsetRight = 250;
		_connectionWarningLabel.Visible = false;

		_minimapArea = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea");
		_cameraIndicator = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea/Indicator");

		_minimapControls = new VBoxContainer();
		_minimapControls.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		_minimapControls.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_minimapControls.AddThemeConstantOverride("separation", 9);
		_minimapControls.CustomMinimumSize = new Vector2(60, 0);
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

		var btnHotkeys = new Button();
		SetupMinimapButton(btnHotkeys, "res://Assets/UI/game_menu.png", "Hotkey Reference [F5]", () => ToggleHotkeyPanel());
		_minimapControls.AddChild(btnHotkeys);

		if (GameHost.Instance != null)
		{
			GameHost.Instance.LoadMapProperties("res://map.json");
		}

		_chatPanel = GetNode<PanelContainer>("ChatPanel");
		_chatInput = GetNode<LineEdit>("ChatPanel/ChatContainer/ChatInput");
		_chatLog = GetNode<RichTextLabel>("ChatPanel/ChatContainer/ChatLog");

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
		_chatLog.Text = $"[color=#ffd700]{TranslationServer.Translate("Chat log initialized. Press Enter to type a message/cheat.")}[/color]\n";

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
		_leaderboardTitleLabel.Text = TranslationServer.Translate("LEADERBOARD");
		UIStyle.ApplyTitle(_leaderboardTitleLabel, TranslationServer.Translate("LEADERBOARD"), 12);
		_leaderboardTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		lbVBox.AddChild(_leaderboardTitleLabel);

		_leaderboardContent = new VBoxContainer();
		_leaderboardContent.Name = "LeaderboardContent";
		lbVBox.AddChild(_leaderboardContent);
		_customUIPanel.AddChild(_leaderboardPanel);

		CreateStatsContainer();
		ApplyWeatherEffects(_currentWeather);
		ApplyThemeStyles();
		SetupCommandCard();
		SetupDevPanel();
		SetupPortrait();

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;
		MouseFilter = MouseFilterEnum.Ignore;

		Resized += OnHUDResized;
		ApplyHUDScale();
		UpdateFPSVisibility();
		BuildHotkeyReferencePanel();

		_resourcePanelController = new ResourcePanel(_resourceContainer);
		_resourcePanelController.InitializeSupplyAndClock(_populationLabel, _clockLabel);

		_camera3D = GameHost.Instance?.MainCamera;
		_minimapPanelController = new MinimapPanel(_minimapFrame, _minimapArea, _cameraIndicator, _camera3D);
		_chatPanelController = new ChatPanel(_chatPanel, _chatInput, _chatLog);
		_leaderboardPanelController = new LeaderboardPanel(_customUIPanel, _countdownPanel, _countdownLabel, _leaderboardPanel, _leaderboardTitleLabel, _leaderboardContent);
		
		_portraitPanelController = new PortraitPanel(
			_portraitFrame, _selectionFrame, _unitsContainer, _unitButtons, _statsContainer,
			_statsLabel, _spellsBox, _itemsBox, _btnUsePotion,
			_productionBox, _productionTitle, _productionProgress, _productionQueueLabel, _queueSlotsContainer,
			_armyCompositionLabel, GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName"), 
			GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture") != null ? GetNode<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture") : null,
			_fireballVBox, _lightningVBox, _holyLightVBox,
			_fireballCooldownBar, _lightningCooldownBar, _holyLightCooldownBar,
			_btnFireball, _btnLightning, _btnHolyLight
		);
		_portraitPanelController.UnitSelectionButtonClicked += OnUnitSelectionButtonClicked;

		_commandPanelController = new CommandPanel(
			_commandGrid,
			_btnMove, _btnStop, _btnHold, _btnBuild, _btnAttack, _btnPatrol,
			_btnBuildCastle, _btnBuildTower, _btnCancelBuild,
			_btnTrainSoldier, _btnTrainArcher, _btnTrainPriest, _btnTrainWorker, _btnBuyPotion,
			_btnUpgradeWeapons, _btnUpgradeShields, _btnUpgradeHarvesting, _btnUpgradeTower, _btnSetRally,
			_btnFireball, _btnLightning, _btnHolyLight, _btnUsePotion
		);

		_controlGroupsUIController = new ControlGroupsUIController(_controlGroupsContainer);
		GenerateDynamicMinimap();

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			_bottomConsole.Visible = false;
			_resourceContainer.Visible = false;
			if (_devPanel != null) _devPanel.Visible = false;
			if (_chatPanel != null) _chatPanel.Visible = false;
		}

		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		if (isSpectator)
		{
			_resourceContainer.Visible = false;
			if (_devPanel != null) _devPanel.Visible = false;
			CreateSpectatorPerspectiveUI();
		}
	}

	private void OnHUDResized()
	{
		ApplyHUDScale();
		var specPanel = GetNodeOrNull<PanelContainer>("SpectatorPanel");
		if (specPanel != null)
		{
			specPanel.Position = new Vector2((GetViewportRect().Size.X - 300) / 2.0f, 10);
		}
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

	public void UpdateFPSVisibility()
	{
		var fpsLabel = (GameHost.Instance != null ? GameHost.Instance.MainNode?.GetNodeOrNull<Label>("CanvasLayer/FPS") : null);
		if (fpsLabel != null)
		{
			fpsLabel.Visible = GameSettings.DisplayFps;
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ChatReceived -= OnLobbyChatReceived;
		}

		if (GodotObject.IsInstanceValid(_rainParticles))
		{
			_rainParticles.QueueFree();
			_rainParticles = null;
		}
	}

	private async void GenerateDynamicMinimap()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
		if (minimapBg == null) return;

		var fogMesh = GameHost.Instance?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DFogMesh");
		bool wasVisible = false;
		if (fogMesh != null)
		{
			wasVisible = fogMesh.Visible;
			fogMesh.Visible = false;
		}

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
		finally
		{
			if (fogMesh != null)
			{
				fogMesh.Visible = wasVisible;
			}
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

		var statsVBox = new VBoxContainer();
		statsVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsVBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(statsVBox);

		_statsLabel = new Label();
		_statsLabel.Text = TranslationServer.Translate("HP: 100/100\nDamage: 10\nArmor: 2\nSpeed: 5");
		_statsLabel.AddThemeFontSizeOverride("font_size", 14);
		_statsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		statsVBox.AddChild(_statsLabel);

		_spellsBox = new VBoxContainer();
		_spellsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_spellsBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_spellsBox);

		var spellsTitle = new Label();
		spellsTitle.Text = TranslationServer.Translate("SPELLS");
		UIStyle.ApplyTitle(spellsTitle, TranslationServer.Translate("SPELLS"), 12);
		spellsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_spellsBox.AddChild(spellsTitle);

		var spellsHBox = new HBoxContainer();
		spellsHBox.AddThemeConstantOverride("separation", 8);
		_spellsBox.AddChild(spellsHBox);

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

		_itemsBox = new VBoxContainer();
		_itemsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_itemsBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_itemsBox);

		var itemsTitle = new Label();
		itemsTitle.Text = TranslationServer.Translate("ITEMS");
		UIStyle.ApplyTitle(itemsTitle, TranslationServer.Translate("ITEMS"), 12);
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
		potHotkeyLabel.Text = TranslationServer.Translate("I");
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

		_productionBox = new VBoxContainer();
		_productionBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_productionBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_productionBox);
		_productionBox.Visible = false;

		_productionTitle = new Label();
		_productionTitle.Text = TranslationServer.Translate("PRODUCTION");
		UIStyle.ApplyTitle(_productionTitle, TranslationServer.Translate("PRODUCTION"), 12);
		_productionTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_productionBox.AddChild(_productionTitle);

		_productionProgress = new ProgressBar();
		_productionProgress.CustomMinimumSize = new Vector2(120, 16);
		_productionProgress.ShowPercentage = true;
		_productionProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		_productionProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		_productionBox.AddChild(_productionProgress);

		_productionQueueLabel = new Label();
		_productionQueueLabel.Text = string.Format(TranslationServer.Translate("Queue: {0}"), 0);
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
		if (selectedUnits != null)
		{
			bool hasPlayerSelection = false;
			foreach (var u in selectedUnits)
			{
				if (!u.IsEnemy)
				{
					hasPlayerSelection = true;
					break;
				}
			}
			bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
			_commandFrame.Visible = isSpectator ? false : hasPlayerSelection;
		}

		_viewModel.UpdateSelectedUnits(selectedUnits);
		_portraitPanelController?.Update(_viewModel);
		_commandPanelController?.Update(_viewModel);
	}

	private void ApplyThemeStyles()
	{
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
			lbl.AddThemeFontSizeOverride("font_size", 27);
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		}
		_goldLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		var minimapBg = new TextureRect();
		minimapBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		minimapBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		minimapBg.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.AddChild(minimapBg);
		minimapBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_minimapArea.MoveChild(minimapBg, 0); 

		var overlay = new MinimapOverlay();
		overlay.Name = "MinimapOverlay";
		overlay.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_cameraIndicator.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.MoveChild(_cameraIndicator, _minimapArea.GetChildCount() - 1);
	}

	public override void _Process(double delta)
	{
		_viewModel.Update(delta);

		_resourcePanelController?.Update(_viewModel);
		_leaderboardPanelController?.Update(_viewModel);

		if (_connectionWarningLabel != null)
		{
			_connectionWarningLabel.Visible = _viewModel.IsConnectionLost;
		}

		if (_btnSelectIdle != null)
		{
			_btnSelectIdle.TooltipText = $"{TranslationServer.Translate("Select All Idle Units [F1]")} ({_viewModel.IdleCount} {TranslationServer.Translate("Idle")})";

			if (_viewModel.IdleCount > 0)
			{
				_idlePulseTimer += (float)delta;
				if (_lastIdleCount == 0)
				{
					ShowFeedbackText(string.Format(TranslationServer.Translate("{0} unit(s) are idle! [F1] to select"), _viewModel.IdleCount), new Color(0.9f, 0.7f, 0.2f));
				}
				float pulse = Mathf.Sin(_idlePulseTimer * 4f) * 0.5f + 0.5f;
				_btnSelectIdle.Modulate = new Color(1f, 0.8f + pulse * 0.2f, 0.2f + pulse * 0.8f, 1f);
			}
			else
			{
				_idlePulseTimer = 0f;
				_btnSelectIdle.Modulate = Colors.White;
			}
			_lastIdleCount = _viewModel.IdleCount;
		}

		_minimapPanelController?.UpdateMinimapIndicator();
		_portraitPanelController?.Update(_viewModel);

		var overlay = _minimapArea.GetNodeOrNull<MinimapOverlay>("MinimapOverlay");
		if (overlay != null)
		{
			overlay.QueueRedraw();
		}

		_controlGroupsUIController?.Update();
		QueueRedraw();
	}

	public void ShowFeedbackText(string text, Color color)
	{
		_feedbackLabel.Text = TranslationServer.Translate(text);
		_feedbackLabel.AddThemeColorOverride("font_color", color);
		_feedbackLabel.Modulate = new Color(color.R, color.G, color.B, 1.0f);

		var tween = CreateTween();
		tween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, 1.5f).SetDelay(0.5f);
	}

	public void ToggleMinimapTerrain()
	{
		_viewModel.ShowMinimapTerrain = !_viewModel.ShowMinimapTerrain;
	}

	private void SetupMinimapButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.TooltipText = TranslationServer.Translate(tooltip);
		btn.CustomMinimumSize = new Vector2(50, 50);
		btn.FocusMode = FocusModeEnum.None;
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.Pressed += () => onClick?.Invoke();
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.TooltipText = TranslationServer.Translate(tooltip);
		btn.CustomMinimumSize = new Vector2(80, 80);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		btn.ClipContents = true;

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

		btn.Pressed += () => onClick?.Invoke();
	}

	private void SetupDevPanel()
	{
		if (_btnVictory != null)
		{
			_btnVictory.Text = TranslationServer.Translate("Trigger Victory");
			_btnVictory.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, true);
			};
		}
		if (_btnDefeat != null)
		{
			_btnDefeat.Text = TranslationServer.Translate("Trigger Defeat");
			_btnDefeat.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, false);
			};
		}
	}

	private void SetupPortrait()
	{
		var pTexture = GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture");
		if (pTexture != null)
		{
			pTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
		}
		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").Text = TranslationServer.Translate("No Selection");
	}

	private void CenterCameraOnSelectedUnit()
	{
		var selected = GameHost.Instance?.SelectedUnits;
		if (selected != null && selected.Count > 0)
		{
			int idx = GameHost.Instance != null ? GameHost.Instance.CycleSelectionIndex : 0;
			if (idx >= 0 && idx < selected.Count && _camera3D != null && GodotObject.IsInstanceValid(_camera3D))
			{
				var target = selected[idx];
				_camera3D.GlobalPosition = new Vector3(target.GlobalPosition.X, _camera3D.GlobalPosition.Y, target.GlobalPosition.Z);
				ShowFeedbackText($"Centered Camera on {target.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.5f));
			}
		}
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
		if (_isDrawingDragBox)
		{
			DrawRect(new Rect2(_dragStart, _dragEnd - _dragStart), new Color(0.1f, 0.9f, 0.2f, 0.15f), true);
			DrawRect(new Rect2(_dragStart, _dragEnd - _dragStart), new Color(0.1f, 0.9f, 0.2f, 0.6f), false, 2f);
		}
	}


	private void OnUnitSelectionButtonClicked(int index)
	{
		if (GameHost.Instance != null && index >= 0 && index < GameHost.Instance.SelectedUnits.Count)
		{
			if (Input.IsKeyPressed(Key.Ctrl))
			{
				var u = GameHost.Instance.SelectedUnits[index];
				GameHost.Instance.DeselectUnit(u);
			}
			else
			{
				GameHost.Instance.CycleSelectionIndex = index;
			}
			RefreshUI(GameHost.Instance.SelectedUnits);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_focus_next"))
		{
			if (GameHost.Instance != null && GameHost.Instance.SelectedUnits.Count > 1)
			{
				GameHost.Instance.CycleSelectionIndex = (GameHost.Instance.CycleSelectionIndex + 1) % GameHost.Instance.SelectedUnits.Count;
				RefreshUI(GameHost.Instance.SelectedUnits);
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Space)
		{
			if (!IsChatActive)
			{
				CenterCameraOnSelectedUnit();
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventKey keyEntEvent && keyEntEvent.Pressed && keyEntEvent.Keycode == Key.Enter)
		{
			if (!IsChatActive)
			{
				_chatPanelController?.ShowChatInput();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void OnChatInputSubmitted(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		if (TryTriggerCheat(text))
		{
			return;
		}
	}

	private void OnLobbyChatReceived(string senderName, string message)
	{
		_chatPanelController?.OnLobbyChatReceived(senderName, message);
	}

	private void BuildHotkeyReferencePanel()
	{
		_hotkeyPanel = new PanelContainer();
		_hotkeyPanel.Name = "HotkeyPanel";

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
		titleLbl.Text = TranslationServer.Translate("HOTKEY REFERENCE — [F5] to close");
		UIStyle.ApplyTitle(titleLbl, TranslationServer.Translate("HOTKEY REFERENCE — [F5] to close"), 13);
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
			("F",   "Train Soldier"),
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
			("Enter",      "Open chat"),
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
			keyLbl.Text = TranslationServer.Translate(key);
			keyLbl.CustomMinimumSize = new Vector2(130, 0);
			keyLbl.AddThemeFontSizeOverride("font_size", 11);
			keyLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			var descLbl = new Label();
			descLbl.Text = TranslationServer.Translate(desc);
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
		_hotkeyPanelVisible = !_hotkeyPanelVisible;
		_hotkeyPanel.Visible = _hotkeyPanelVisible;
	}

	public void StartCountdownTimer(float duration, string labelText)
	{
		_viewModel.CountdownDuration = duration;
		_viewModel.CountdownText = labelText;
		_viewModel.CountdownActive = true;
	}

	public void StopCountdownTimer()
	{
		_viewModel.CountdownActive = false;
	}

	public void UpdateCountdownLabel(string labelText)
	{
		_viewModel.CountdownText = labelText;
	}

	public void SetLeaderboardVisible(string title, bool visible)
	{
		_viewModel.LeaderboardTitle = title;
		_viewModel.LeaderboardVisible = visible;
	}

	public void ClearLeaderboard()
	{
		_viewModel.LeaderboardValues.Clear();
	}

	public void SetLeaderboardValue(string label, string value)
	{
		_viewModel.LeaderboardValues[label] = value;
	}


	private void CycleWeather()
	{
		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer()) return;

		if (GameHost.Instance?.EnvironmentService != null)
		{
			string next = GameHost.Instance.EnvironmentService.CycleWeather();
			if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
			{
				Rpc(nameof(SyncWeather), next);
			}
			else
			{
				ApplyWeatherEffects(next);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void SyncWeather(string weather)
	{
		if (GameHost.Instance?.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.SetCurrentWeather(weather);
			float density = weather switch
			{
				"clear" => 0f,
				"rain" => 0.008f,
				"fog" => 0.045f,
				"storm" => 0.015f,
				_ => 0f
			};
			GameHost.Instance.EnvironmentService.SetBaseFogDensity(density);
		}
		ApplyWeatherEffects(weather);
	}

	private void ApplyWeatherEffects(string weather)
	{
		var worldEnv = (GameHost.Instance != null ? GameHost.Instance.MainNode?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") : null);
		if (worldEnv == null || worldEnv.Environment == null) return;

		var mainNode = (GameHost.Instance != null ? GameHost.Instance.MainNode : null);
		if (mainNode == null) return;

		if (GodotObject.IsInstanceValid(_rainParticles)) { _rainParticles.QueueFree(); _rainParticles = null; }
		
		var sky = worldEnv.Environment.Sky;

		if (weather == "clear")
		{
			worldEnv.Environment.FogEnabled = false;
			_baseFogDensity = 0f;
			ShowFeedbackText("Weather Forecast: Clear Skies", new Color(0.3f, 0.9f, 1.0f));
		}
		else if (weather == "rain")
		{
			worldEnv.Environment.FogEnabled = true;
			_baseFogDensity = 0.008f;
			
			_rainParticles = new CpuParticles3D();
			_rainParticles.Name = "RainParticles";
			_rainParticles.Amount = 800;
			_rainParticles.Lifetime = 2.0f;
			_rainParticles.Preprocess = 2.0f;
			
			var mesh = new BoxMesh();
			mesh.Size = new Vector3(0.05f, 1.5f, 0.05f);
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.5f, 0.6f, 0.9f, 0.4f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mesh.Material = mat;
			_rainParticles.Mesh = mesh;
			
			_rainParticles.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
			_rainParticles.EmissionBoxExtents = new Vector3(150f, 1f, 150f);
			_rainParticles.Direction = new Vector3(0.1f, -1f, 0f);
			_rainParticles.Spread = 5f;
			_rainParticles.InitialVelocityMin = 20f;
			_rainParticles.InitialVelocityMax = 30f;
			
			mainNode.AddChild(_rainParticles);
			_rainParticles.GlobalPosition = new Vector3(0f, 40f, 0f);
			
			ShowFeedbackText("Weather Forecast: Light Rain Shower", new Color(0.2f, 0.5f, 0.9f));
		}
		else if (weather == "fog")
		{
			worldEnv.Environment.FogEnabled = true;
			_baseFogDensity = 0.045f;
			ShowFeedbackText("Weather Forecast: Dense Fog Warning", new Color(0.7f, 0.7f, 0.8f));
		}
		else if (weather == "storm")
		{
			worldEnv.Environment.FogEnabled = true;
			_baseFogDensity = 0.015f;
			
			_rainParticles = new CpuParticles3D();
			_rainParticles.Name = "StormParticles";
			_rainParticles.Amount = 2500;
			_rainParticles.Lifetime = 1.5f;
			_rainParticles.Preprocess = 2.0f;
			
			var mesh = new BoxMesh();
			mesh.Size = new Vector3(0.06f, 2.2f, 0.06f);
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.4f, 0.45f, 0.6f, 0.6f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mesh.Material = mat;
			_rainParticles.Mesh = mesh;
			
			_rainParticles.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
			_rainParticles.EmissionBoxExtents = new Vector3(150f, 1f, 150f);
			_rainParticles.Direction = new Vector3(-0.4f, -1f, -0.1f);
			_rainParticles.Spread = 12f;
			_rainParticles.InitialVelocityMin = 35f;
			_rainParticles.InitialVelocityMax = 45f;
			
			mainNode.AddChild(_rainParticles);
			_rainParticles.GlobalPosition = new Vector3(0f, 45f, 0f);
			
			ShowFeedbackText("Weather Forecast: Severe Thunderstorm!", new Color(0.6f, 0.2f, 0.8f));
		}
	}


	public bool TryTriggerCheat(string text)
	{
		if (GameHost.Instance == null || GameHost.Instance.CheatService == null)
		{
			return false;
		}

		var selectedEntities = new List<Entity>();
		if (GameHost.Instance.SelectedUnits != null)
		{
			foreach (var u in GameHost.Instance.SelectedUnits)
			{
				selectedEntities.Add(u.Entity);
			}
		}

		var (result, affectedCount) = GameHost.Instance.CheatService.TryTriggerCheat(
			text,
			Multiplayer.MultiplayerPeer != null,
			GameHost.Instance.PlayerEntity,
			GameHost.Instance.DefinitionManager,
			selectedEntities
		);

		if (result == CheatService.CheatResult.None)
		{
			return false;
		}

		switch (result)
		{
			case CheatService.CheatResult.Stonks:
				ShowFeedbackText("Cheat Activated: Stonks! (+10,000 resources)", new Color(0.95f, 0.82f, 0.55f));
				_chatLog.Text += $"[color=#ffd700]System: {TranslationServer.Translate("Cheat 'stonks' activated. Added 10,000 resources.")}[/color]\n";
				break;
			case CheatService.CheatResult.Gigachad:
				ShowFeedbackText($"Cheat Activated: Gigachad Main Character Energy! ({affectedCount} units empowered)", new Color(1.0f, 0.3f, 0.1f));
				_chatLog.Text += $"[color=#ffd700]System: {string.Format(TranslationServer.Translate("Cheat 'gigachad' activated. Powered up {0} units."), affectedCount)}[/color]\n";
				RefreshUI(GameHost.Instance.SelectedUnits);
				break;
			case CheatService.CheatResult.AbsoluteUnit:
				foreach (var unit in GameHost.Instance.SelectedUnits)
				{
					if (GodotObject.IsInstanceValid(unit))
					{
						unit.Scale = new Vector3(3f, 3f, 3f);
					}
				}
				ShowFeedbackText($"Cheat Activated: Absolute Unit! (+Scale, +Speed) on {affectedCount} units", new Color(0.2f, 0.8f, 1.0f));
				_chatLog.Text += $"[color=#ffd700]System: {string.Format(TranslationServer.Translate("Cheat 'absoluteunit' activated. Gigantified {0} units with super speed!"), affectedCount)}[/color]\n";
				RefreshUI(GameHost.Instance.SelectedUnits);
				break;
			case CheatService.CheatResult.ThanosSnap:
				ShowFeedbackText($"Cheat Activated: Thanos Snapped. Destroyed {affectedCount} enemies.", new Color(0.9f, 0.1f, 0.1f));
				_chatLog.Text += $"[color=#ffd700]System: {string.Format(TranslationServer.Translate("Cheat 'thanossnap' activated. Slain {0} enemy units."), affectedCount)}[/color]\n";
				break;
			case CheatService.CheatResult.EzClap:
				ShowFeedbackText("Cheat Activated: EZ Clap Speedrun!", new Color(0.1f, 0.9f, 0.2f));
				_chatLog.Text += $"[color=#ffd700]System: {TranslationServer.Translate("Cheat 'ezclap' activated. Proceeding to Victory.")}[/color]\n";
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, true);
				break;
			case CheatService.CheatResult.NoCap:
				ShowFeedbackText("Cheat Activated: Fog of War removed! No cap.", new Color(0.2f, 0.8f, 0.5f));
				_chatLog.Text += $"[color=#ffd700]System: {TranslationServer.Translate("Cheat 'nocap' activated. Fog of War disabled.")}[/color]\n";
				break;
		}
		return true;
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

		_btnBuildCastle = new Button();
		SetupHUDButton(_btnBuildCastle, "res://Assets/UI/moonlit_castle.png", "[C] Build Castle (Cost: 400 Gold, 300 Wood, 200 Stone)", () => GameHost.Instance?.EnterBuildingPlacement("castle"));
		_btnBuildTower = new Button();
		SetupHUDButton(_btnBuildTower, "res://Assets/UI/unknown_unit_1.png", "[T] Build Spell Tower (Cost: 200 Gold, 150 Wood, 100 Stone)", () => GameHost.Instance?.EnterBuildingPlacement("tower"));
		_btnCancelBuild = new Button();
		SetupHUDButton(_btnCancelBuild, "res://Assets/UI/cancel_button_2.png", "[Esc] Cancel", () => ExitBuildSubMenu());

		_btnTrainSoldier = new Button();
		SetupHUDButton(_btnTrainSoldier, "res://Assets/UI/heavy_knight.png", "[F] Train Soldier (Cost: 100 Gold, 1 Pop) — Heavy armored melee fighter", () => GameHost.Instance?.TrainUnitAtCastle("soldier"));
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

		_btnUpgradeTower = new Button();
		SetupHUDButton(_btnUpgradeTower, "res://Assets/UI/magic_upgrade_arrow.png", "[U] Upgrade Tower (Cost: 150 Gold, 100 Stone)", () => UpgradeSelectedTower());
	}

	private void CreateSpectatorPerspectiveUI()
	{
		var specPanel = new PanelContainer();
		specPanel.Name = "SpectatorPanel";
		specPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		
		var specVBox = new VBoxContainer();
		specPanel.AddChild(specVBox);

		var title = new Label();
		title.Text = TranslationServer.Translate("SPECTATOR MODE");
		UIStyle.ApplyTitle(title, TranslationServer.Translate("SPECTATOR MODE"), 12);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		specVBox.AddChild(title);

		var specHBox = new HBoxContainer();
		specHBox.Alignment = BoxContainer.AlignmentMode.Center;
		specHBox.AddThemeConstantOverride("separation", 10);
		specVBox.AddChild(specHBox);

		var btnAll = new Button();
		btnAll.Text = TranslationServer.Translate("ALL");
		btnAll.Pressed += () => { LiveSpectatorPerspective = -1; };
		specHBox.AddChild(btnAll);

		var btnBlue = new Button();
		btnBlue.Text = TranslationServer.Translate("BLUE");
		btnBlue.Pressed += () => { LiveSpectatorPerspective = 0; };
		specHBox.AddChild(btnBlue);

		var btnRed = new Button();
		btnRed.Text = TranslationServer.Translate("RED");
		btnRed.Pressed += () => { LiveSpectatorPerspective = 1; };
		specHBox.AddChild(btnRed);

		var btnGreen = new Button();
		btnGreen.Text = TranslationServer.Translate("GREEN");
		btnGreen.Pressed += () => { LiveSpectatorPerspective = 2; };
		specHBox.AddChild(btnGreen);

		var btnYellow = new Button();
		btnYellow.Text = TranslationServer.Translate("YELLOW");
		btnYellow.Pressed += () => { LiveSpectatorPerspective = 3; };
		specHBox.AddChild(btnYellow);

		AddChild(specPanel);
	}
}
