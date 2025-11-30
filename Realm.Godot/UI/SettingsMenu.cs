using Godot;
using System;

public partial class SettingsMenu : Control
{
	[Export] public bool IsOverlay = false;

	private Panel _bgPanel;
	private PanelContainer _mainFrame;
	private PanelContainer _videoPanel;
	private PanelContainer _audioPanel;
	private PanelContainer _gameplayPanel;

	private Label _settingsTitle;
	private Label _videoTitle;
	private Label _audioTitle;
	private Label _gameplayTitle;

	// VIDEO
	private OptionButton _resolutionOpt;
	private OptionButton _qualityOpt;
	private OptionButton _windowModeOpt;
	private OptionButton _vsyncOpt;

	// AUDIO
	private HSlider _masterSlider;
	private HSlider _musicSlider;
	private HSlider _sfxSlider;
	private HSlider _voiceSlider;

	// GAMEPLAY
	private HSlider _scrollSpeedSlider;
	private HSlider _mouseSensSlider;
	private HSlider _hudScaleSlider;
	private OptionButton _healthBarsOpt;

	// BOTTOM BUTTONS
	private Button _applyBtn;
	private Button _cancelBtn;
	private Button _resetBtn;

	public override void _Ready()
	{
		// Panels
		_bgPanel = GetNodeOrNull<Panel>("Background");
		_mainFrame = GetNode<PanelContainer>("CenterContainer/MainFrame");
		_videoPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel");
		_audioPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel");
		_gameplayPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel");

		// Titles
		_settingsTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/SettingsTitle");
		_videoTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/PanelTitle");
		_audioTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/PanelTitle");
		_gameplayTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/PanelTitle");

		// Video options
		_resolutionOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/ResolutionOpt");
		_qualityOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/QualityOpt");
		_windowModeOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/WindowModeOpt");
		_vsyncOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/VsyncOpt");

		// Audio options
		_masterSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/MasterSlider");
		_musicSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/MusicSlider");
		_sfxSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/SfxSlider");
		_voiceSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/VoiceSlider");

		// Gameplay options
		_scrollSpeedSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/ScrollSpeedSlider");
		_mouseSensSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/MouseSensSlider");
		_hudScaleSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HudScaleSlider");
		_healthBarsOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HealthBarsOpt");

		// Buttons
		_applyBtn = GetNode<Button>("CenterContainer/MainFrame/VBoxContainer/ButtonsRow/ApplyButton");
		_cancelBtn = GetNode<Button>("CenterContainer/MainFrame/VBoxContainer/ButtonsRow/CancelButton");
		_resetBtn = GetNode<Button>("CenterContainer/MainFrame/VBoxContainer/ButtonsRow/ResetButton");

		ApplyThemeStyles();
		PopulateDropdowns();
		SetupSliders();
		SetupButtons();

		LoadCurrentSettings();
	}

	private void ApplyThemeStyles()
	{
		if (_bgPanel != null)
		{
			_bgPanel.Visible = true;
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
			if (GetNodeOrNull<ColorRect>("OverlayBg") is ColorRect rect)
			{
				rect.Visible = false;
			}
		}

		_mainFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		_videoPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_audioPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_gameplayPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		UIStyle.ApplyTitle(_settingsTitle, "GAME SETTINGS", 32);
		UIStyle.ApplyTitle(_videoTitle, "VIDEO", 18);
		UIStyle.ApplyTitle(_audioTitle, "AUDIO", 18);
		UIStyle.ApplyTitle(_gameplayTitle, "GAMEPLAY", 18);

		// Style sub-labels
		string[] labelPaths = {
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/ResLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/QualLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/ModeLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/VsyncLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/MasterLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/MusicLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/SfxLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/VoiceLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/ScrollLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/SensLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HudScaleLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HealthBarsLabel"
		};
		foreach (var path in labelPaths)
		{
			var lbl = GetNode<Label>(path);
			lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
			lbl.AddThemeFontSizeOverride("font_size", 14);
		}
	}

	private void PopulateDropdowns()
	{
		_resolutionOpt.Clear();
		_resolutionOpt.AddItem("1920 x 1080", 0);
		_resolutionOpt.AddItem("1600 x 900", 1);
		_resolutionOpt.AddItem("1280 x 720", 2);

		_qualityOpt.Clear();
		_qualityOpt.AddItem("Low", 0);
		_qualityOpt.AddItem("Medium", 1);
		_qualityOpt.AddItem("High", 2);
		_qualityOpt.AddItem("Ultra", 3);

		_windowModeOpt.Clear();
		_windowModeOpt.AddItem("Fullscreen", 0);
		_windowModeOpt.AddItem("Windowed", 1);
		_windowModeOpt.AddItem("Borderless", 2);

		_vsyncOpt.Clear();
		_vsyncOpt.AddItem("On", 0);
		_vsyncOpt.AddItem("Off", 1);

		_healthBarsOpt.Clear();
		_healthBarsOpt.AddItem("Hidden", 0);
		_healthBarsOpt.AddItem("Visible", 1);
		_healthBarsOpt.AddItem("Damaged", 2);

		// Dropdown Theme Styling and Sound Integration
		var dropdowns = new[] { _resolutionOpt, _qualityOpt, _windowModeOpt, _vsyncOpt, _healthBarsOpt };
		foreach (var opt in dropdowns)
		{
			opt.Flat = false;
			opt.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			opt.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			opt.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			opt.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

			opt.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			opt.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			opt.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
			opt.AddThemeFontSizeOverride("font_size", 14);

			opt.ItemSelected += (idx) => UIManager.Instance.PlayClickSound();
			opt.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		}
	}

	private void SetupSliders()
	{
		_hudScaleSlider.MinValue = 50;
		_hudScaleSlider.MaxValue = 150;
		_hudScaleSlider.Step = 5;

		var sliders = new[] { _masterSlider, _musicSlider, _sfxSlider, _voiceSlider, _scrollSpeedSlider, _mouseSensSlider, _hudScaleSlider };
		
		var trackStyle = UIStyle.CreateSliderTrack();
		var fillStyle = UIStyle.CreateSliderFill();

		foreach (var s in sliders)
		{
			s.AddThemeStyleboxOverride("slider", trackStyle);
			s.AddThemeStyleboxOverride("grabber_area", fillStyle);
			s.AddThemeStyleboxOverride("grabber_area_highlight", fillStyle);

			// Slider Sound Trigger Integration
			s.MouseEntered += () => UIManager.Instance.PlayHoverSound();
			s.DragEnded += (valChanged) => UIManager.Instance.PlayClickSound();
		}

		_masterSlider.ValueChanged += (val) =>
		{
			GameSettings.MasterVolume = (float)val;
			UIManager.Instance.UpdateAudioVolumes();
		};
		_musicSlider.ValueChanged += (val) =>
		{
			GameSettings.MusicVolume = (float)val;
			UIManager.Instance.UpdateAudioVolumes();
		};
		_sfxSlider.ValueChanged += (val) =>
		{
			GameSettings.SfxVolume = (float)val;
			UIManager.Instance.UpdateAudioVolumes();
		};
		_voiceSlider.ValueChanged += (val) =>
		{
			GameSettings.VoiceVolume = (float)val;
			UIManager.Instance.UpdateAudioVolumes();
		};
	}

	private void SetupButtons()
	{
		SetupSettingsButton(_applyBtn, "APPLY", ApplySettings);
		SetupSettingsButton(_cancelBtn, "CANCEL", CancelSettings);
		SetupSettingsButton(_resetBtn, "RESET TO DEFAULT", ResetToDefaults);
	}

	private void SetupSettingsButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		UIStyle.ApplyButtonText(btn, text, 16);
		
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void LoadCurrentSettings()
	{
		_resolutionOpt.Select(GameSettings.ResolutionIdx);
		_qualityOpt.Select(GameSettings.QualityIdx);
		_windowModeOpt.Select(GameSettings.WindowModeIdx);
		_vsyncOpt.Select(GameSettings.VsyncIdx);

		_masterSlider.Value = GameSettings.MasterVolume;
		_musicSlider.Value = GameSettings.MusicVolume;
		_sfxSlider.Value = GameSettings.SfxVolume;
		_voiceSlider.Value = GameSettings.VoiceVolume;

		_scrollSpeedSlider.Value = GameSettings.ScrollSpeed;
		_mouseSensSlider.Value = GameSettings.MouseSens;
		int hbIdx = GameSettings.ShowHealthBars switch
		{
			"hidden" => 0,
			"visible" => 1,
			"damaged" => 2,
			_ => 2
		};
		_healthBarsOpt.Select(hbIdx);
	}

	private void ApplySettings()
	{
		string resText = _resolutionOpt.GetItemText(_resolutionOpt.Selected);
		var parts = resText.Split("x");
		if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int w) && int.TryParse(parts[1].Trim(), out int h))
		{
			GetWindow().Size = new Vector2I(w, h);
		}

		int modeIdx = _windowModeOpt.Selected;
		if (modeIdx == 0) // Fullscreen
		{
			GetWindow().Mode = Window.ModeEnum.ExclusiveFullscreen;
		}
		else if (modeIdx == 1) // Windowed
		{
			GetWindow().Borderless = false;
			GetWindow().Mode = Window.ModeEnum.Windowed;
		}
		else if (modeIdx == 2) // Borderless
		{
			GetWindow().Borderless = true;
			GetWindow().Mode = Window.ModeEnum.Maximized;
		}

		if (_vsyncOpt.Selected == 0)
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		}
		else
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		}

		// Save settings in GameSettings
		GameSettings.ResolutionIdx = _resolutionOpt.Selected;
		GameSettings.QualityIdx = _qualityOpt.Selected;
		GameSettings.WindowModeIdx = _windowModeOpt.Selected;
		GameSettings.VsyncIdx = _vsyncOpt.Selected;

		GameSettings.MasterVolume = (float)_masterSlider.Value;
		GameSettings.MusicVolume = (float)_musicSlider.Value;
		GameSettings.SfxVolume = (float)_sfxSlider.Value;
		GameSettings.VoiceVolume = (float)_voiceSlider.Value;

		GameSettings.ScrollSpeed = (float)_scrollSpeedSlider.Value;
		GameSettings.MouseSens = (float)_mouseSensSlider.Value;
		GameSettings.HudScale = (float)_hudScaleSlider.Value;
		GameSettings.ShowHealthBars = _healthBarsOpt.Selected switch
		{
			0 => "hidden",
			1 => "visible",
			2 => "damaged",
			_ => "damaged"
		};

		GameSettings.Save();

		// Update active HUD if it exists
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ApplyHUDScale();
		}

		GD.Print("Settings Applied successfully!");
		CloseOrTransition();
	}

	private void CancelSettings()
	{
		GameSettings.Load();
		UIManager.Instance.UpdateAudioVolumes();
		CloseOrTransition();
	}

	private void CloseOrTransition()
	{
		if (IsOverlay)
		{
			QueueFree();
		}
		else
		{
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		}
	}

	private void ResetToDefaults()
	{
		GameSettings.ResetToDefaults();
		LoadCurrentSettings();
		GD.Print("Settings reset to defaults.");
	}
}
