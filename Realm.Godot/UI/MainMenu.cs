using Godot;
using System;

public partial class MainMenu : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _centralPanel;
	
	private TextureRect _gameLogo;
	private Button _playButton;
	private Button _singlePlayerButton;
	private Button _mapDiscoveryButton;
	private Button _mapEditorButton;
	private Button _replaysButton;
	private Button _settingsButton;
	private Button _profileButton;
	private Button _quitButton;
	private Button _discordButton;
	private Button _donateButton;
	private Button _socialButton;
	private Control _socialPopover;
	private Control _socialPopoverOverlay;
	private Button _contributeButton;
	private Button _bugReportButton;
	private Button _seedNodeButton;
	private Control _profilePopup;

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };

	public override void _Ready()
	{

		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_centralPanel = GetNode<PanelContainer>("CentralPanel");
		_gameLogo = GetNode<TextureRect>("GameLogo");
		_playButton = GetNode<Button>("CentralPanel/VBoxContainer/PlayButton");
		_singlePlayerButton = GetNode<Button>("CentralPanel/VBoxContainer/SinglePlayerButton");
		_mapDiscoveryButton = GetNode<Button>("CentralPanel/VBoxContainer/MapDiscoveryButton");
		_mapEditorButton = GetNode<Button>("CentralPanel/VBoxContainer/MapEditorButton");
		_replaysButton = GetNode<Button>("CentralPanel/VBoxContainer/ReplaysButton");
		_settingsButton = GetNode<Button>("SettingsButton");
		_profileButton = GetNode<Button>("ProfileButton");
		_quitButton = GetNode<Button>("QuitButton");
		_socialButton = GetNode<Button>("SocialButton");
		_socialPopover = GetNode<Control>("SocialPopover");
		_socialPopoverOverlay = GetNode<Control>("SocialPopoverOverlay");
		_discordButton = GetNode<Button>("SocialPopover/PopoverVBox/DiscordButton");
		_donateButton = GetNode<Button>("SocialPopover/PopoverVBox/DonateButton");
		_contributeButton = GetNode<Button>("SocialPopover/PopoverVBox/ContributeButton");
		_bugReportButton = GetNode<Button>("SocialPopover/PopoverVBox/BugReportButton");
		_seedNodeButton = GetNode<Button>("SocialPopover/PopoverVBox/SeedNodeButton");


		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_centralPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());


		SetupPlayButton(_playButton, () => {
			if (LobbyManager.Instance != null) LobbyManager.Instance.IsSinglePlayer = false;
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		});
		SetupButton(_singlePlayerButton, "SINGLE PLAYER", () => {
			if (LobbyManager.Instance != null) LobbyManager.Instance.IsSinglePlayer = true;
			UIManager.Instance.TransitionTo(GameScreen.LobbyCreate);
		});
		SetupButton(_mapDiscoveryButton, "MAP DISCOVERY", () => UIManager.Instance.TransitionTo(GameScreen.MapDiscovery));
		SetupButton(_mapEditorButton, "MAP EDITOR", () => OnMapEditorPressed());
		SetupButton(_replaysButton, "REPLAYS", () => UIManager.Instance.TransitionTo(GameScreen.ReplayList));
		SetupUtilityButton(_settingsButton, "res://Assets/UI/gear_icon.png", () => UIManager.Instance.OpenSettingsOverlay());
		SetupUtilityButton(_profileButton, "res://Assets/UI/avatar_icon.png", () => ShowProfilePopup());
		SetupButton(_quitButton, "QUIT GAME", () => GetTree().Quit());
		SetupUtilityButton(_socialButton, "res://Assets/UI/social_icon.png", () => ToggleSocialPopover());
		_socialPopover.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		SetupButton(_discordButton, "DISCORD", () => { OS.ShellOpen("https://discord.com/servers/realm"); HideSocialPopover(); });
		SetupButton(_donateButton, "DONATE", () => { OS.ShellOpen("https://github.com/sponsors/speige"); HideSocialPopover(); });
		SetupButton(_contributeButton, "CONTRIBUTE", () => { OS.ShellOpen("https://github.com/speige/realm"); HideSocialPopover(); });
		SetupButton(_bugReportButton, "BUG REPORT", () => { OS.ShellOpen("https://github.com/speige/Realm/issues"); HideSocialPopover(); });
		SetupButton(_seedNodeButton, "HOST A SEED NODE (ADVANCED)", () => { OS.ShellOpen("https://github.com/speige/Realm/blob/main/Seed_Node_Setup.md"); HideSocialPopover(); });

		_socialPopoverOverlay.GuiInput += (InputEvent @event) =>
		{
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				HideSocialPopover();
			}
		};


		_socialButton.AddThemeConstantOverride("icon_max_width", 32);
		_discordButton.AddThemeConstantOverride("icon_max_width", 36);
		_mapDiscoveryButton.AddThemeConstantOverride("icon_max_width", 36);
		_mapEditorButton.AddThemeConstantOverride("icon_max_width", 36);
		_replaysButton.AddThemeConstantOverride("icon_max_width", 36);
		_donateButton.AddThemeConstantOverride("icon_max_width", 36);
		_contributeButton.AddThemeConstantOverride("icon_max_width", 36);
		_bugReportButton.AddThemeConstantOverride("icon_max_width", 36);
		_seedNodeButton.AddThemeConstantOverride("icon_max_width", 36);


		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void SetupButton(Button button, string text, Action onClick)
	{
		button.Flat = false;
		UIStyle.ApplyButtonText(button, text, 20);
		

		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		button.Pressed += () => 
		{
			PlayClickSound();
			onClick?.Invoke();
		};

		button.MouseEntered += () => PlayHoverSound();
	}

	private void SetupPlayButton(Button button, Action onClick)
	{
		button.Flat = false;
		button.Text = Tr("PLAY");
		button.AddThemeFontSizeOverride("font_size", 22);
		button.AddThemeColorOverride("font_color", new Color(1.0f, 0.92f, 0.7f));
		button.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 0.9f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.8f, 0.7f, 0.4f));
		button.AddThemeConstantOverride("outline_size", 4);
		button.AddThemeColorOverride("font_outline_color", UIStyle.ColorCyanGlowDim);

		var normalStyle = new StyleBoxTexture();
		normalStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/play_button_stylized.png");
		normalStyle.ContentMarginLeft = 24;
		normalStyle.ContentMarginRight = 24;
		normalStyle.ContentMarginTop = 12;
		normalStyle.ContentMarginBottom = 12;

		var hoverStyle = new StyleBoxTexture();
		hoverStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/play_button_stylized.png");
		hoverStyle.ContentMarginLeft = 24;
		hoverStyle.ContentMarginRight = 24;
		hoverStyle.ContentMarginTop = 12;
		hoverStyle.ContentMarginBottom = 12;
		hoverStyle.ModulateColor = new Color(1.15f, 1.05f, 0.85f);

		var pressedStyle = new StyleBoxTexture();
		pressedStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/play_button_stylized.png");
		pressedStyle.ContentMarginLeft = 26;
		pressedStyle.ContentMarginRight = 26;
		pressedStyle.ContentMarginTop = 14;
		pressedStyle.ContentMarginBottom = 10;
		pressedStyle.ModulateColor = new Color(0.85f, 0.75f, 0.55f);

		button.AddThemeStyleboxOverride("normal", normalStyle);
		button.AddThemeStyleboxOverride("hover", hoverStyle);
		button.AddThemeStyleboxOverride("pressed", pressedStyle);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());


		Tween scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.04f, 1.04f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

		private void SetupUtilityButton(Button button, string iconPath, Action onClick)
	{
		button.Flat = false;
		button.Text = "";
		button.Icon = GD.Load<Texture2D>(iconPath);
		button.IconAlignment = HorizontalAlignment.Center;
		button.ExpandIcon = true;

		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		button.AddThemeColorOverride("icon_normal_color", UIStyle.ColorGoldDull);
		button.AddThemeColorOverride("icon_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("icon_pressed_color", UIStyle.ColorCyanGlow);
		button.AddThemeColorOverride("icon_focus_color", UIStyle.ColorGoldDull);

		Tween scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.08f, 1.08f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

private void ToggleSocialPopover()
	{
		bool isVisible = !_socialPopover.Visible;
		_socialPopover.Visible = isVisible;
		_socialPopoverOverlay.Visible = isVisible;
	}

	private void HideSocialPopover()
	{
		_socialPopover.Visible = false;
		_socialPopoverOverlay.Visible = false;
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	private void PlayHoverSound()
	{
		UIManager.Instance.PlayHoverSound();
	}

	private void PlayClickSound()
	{
		UIManager.Instance.PlayClickSound();
	}

	private void ShowProfilePopup()
	{
		if (_profilePopup != null)
		{
			_profilePopup.QueueFree();
		}

		_profilePopup = new Panel();
		_profilePopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_profilePopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(_profilePopup);


		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(500, 560);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		_profilePopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(440, 500);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);


		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });


		var title = new Label();
		UIStyle.ApplyTitle(title, "PLAYER PROFILE", 26);
		vbox.AddChild(title);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });


		var factionFlag = new TextureRect();
		factionFlag.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
		factionFlag.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		factionFlag.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		factionFlag.CustomMinimumSize = new Vector2(160, 120);
		factionFlag.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(factionFlag);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });


		var statsText = new Label();
		var currentUsername = LobbyManager.Instance.AuthenticatedUsername;
		var currentProvider = LobbyManager.Instance.AuthProvider ?? "None";
		statsText.Text = $"Username: {currentUsername}\nAuth Provider: {currentProvider}\nMatches Played: 142\nVictories: 89\nDefeats: 53\nWin Rate: 62.7%\nRank: Grand Marshal";
		statsText.HorizontalAlignment = HorizontalAlignment.Center;
		statsText.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		statsText.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(statsText);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var oauthHbox = new HBoxContainer();
		oauthHbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(oauthHbox);

		var discordLoginBtn = new Button();
		discordLoginBtn.AddThemeConstantOverride("icon_max_width", 0);
		SetupButton(discordLoginBtn, "LOGIN DISCORD", async () =>
		{
			bool success = await LobbyManager.Instance.StartOAuthFlowAsync("discord");
			if (success && GodotObject.IsInstanceValid(statsText))
			{
				statsText.Text = $"Username: {LobbyManager.Instance.AuthenticatedUsername}\nAuth Provider: {LobbyManager.Instance.AuthProvider}\nMatches Played: 142\nVictories: 89\nDefeats: 53\nWin Rate: 62.7%\nRank: Grand Marshal";
			}
		});
		discordLoginBtn.CustomMinimumSize = new Vector2(180, 40);
		oauthHbox.AddChild(discordLoginBtn);

		var spacer = new Control { CustomMinimumSize = new Vector2(10, 0) };
		oauthHbox.AddChild(spacer);

		var steamLoginBtn = new Button();
		steamLoginBtn.AddThemeConstantOverride("icon_max_width", 0);
		SetupButton(steamLoginBtn, "LOGIN STEAM", async () =>
		{
			bool success = await LobbyManager.Instance.StartOAuthFlowAsync("steam");
			if (success && GodotObject.IsInstanceValid(statsText))
			{
				statsText.Text = $"Username: {LobbyManager.Instance.AuthenticatedUsername}\nAuth Provider: {LobbyManager.Instance.AuthProvider}\nMatches Played: 142\nVictories: 89\nDefeats: 53\nWin Rate: 62.7%\nRank: Grand Marshal";
			}
		});
		steamLoginBtn.CustomMinimumSize = new Vector2(180, 40);
		oauthHbox.AddChild(steamLoginBtn);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });


		var backBtn = new Button();
		SetupButton(backBtn, "BACK", () => 
		{
			_profilePopup.QueueFree();
			_profilePopup = null;
		});
		backBtn.CustomMinimumSize = new Vector2(180, 48);
		backBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(backBtn);
	}

	private Control _installWaitingPopup;

	private void OnMapEditorPressed()
	{
		if (System.OperatingSystem.IsWindows())
		{
			if (VSCodeManager.Instance.IsInstalling)
			{
				ShowInstallWaitingPopup();
				return;
			}
			if (!VSCodeManager.Instance.IsInstalled())
			{
				VSCodeManager.Instance.StartInstallIfNeeded();
				ShowInstallWaitingPopup();
				return;
			}
		}
		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
	}

	private void ShowInstallWaitingPopup()
	{
		if (_installWaitingPopup != null)
		{
			_installWaitingPopup.QueueFree();
		}

		_installWaitingPopup = new Panel();
		_installWaitingPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_installWaitingPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(_installWaitingPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(450, 250);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_installWaitingPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(400, 200);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var title = new Label();
		UIStyle.ApplyTitle(title, "⚠️ EDITOR INSTALLING", 22);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(title);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var msg = new Label();
		msg.Text = Tr("The Map VSCode Editor is currently installing in the background.\nPlease wait for it to complete.");
		msg.HorizontalAlignment = HorizontalAlignment.Center;
		msg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		msg.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(msg);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 25) });

		var okBtn = new Button();
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		SetupButton(okBtn, "OK", () =>
		{
			_installWaitingPopup.QueueFree();
			_installWaitingPopup = null;
		});
		okBtn.CustomMinimumSize = new Vector2(120, 40);
		okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(okBtn);

		StartInstallWaitingPolling();
	}

	private void StartInstallWaitingPolling()
	{
		var timer = GetTree().CreateTimer(0.5f);
		timer.Timeout += OnInstallWaitingPollTimeout;
	}

	private void OnInstallWaitingPollTimeout()
	{
		if (_installWaitingPopup == null || !GodotObject.IsInstanceValid(_installWaitingPopup))
		{
			return;
		}

		if (System.OperatingSystem.IsWindows())
		{
			if (VSCodeManager.Instance.IsInstalled())
			{
				_installWaitingPopup.QueueFree();
				_installWaitingPopup = null;
				UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
				return;
			}

			if (VSCodeManager.Instance.IsInstalling)
			{
				StartInstallWaitingPolling();
			}
		}
	}
}
