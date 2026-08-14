using Godot;
using System;

public partial class SettingsMenu : Control
{
	public static SettingsMenu Instance { get; private set; }
	public static bool IsOpen => Instance != null && GodotObject.IsInstanceValid(Instance) && Instance.IsInsideTree() && Instance.Visible;

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


	private OptionButton _resolutionOpt;
	private OptionButton _qualityOpt;
	private OptionButton _windowModeOpt;
	private OptionButton _vsyncOpt;


	private HSlider _masterSlider;
	private HSlider _musicSlider;
	private HSlider _sfxSlider;
	private HSlider _voiceSlider;


	private HSlider _scrollSpeedSlider;
	private HSlider _mouseSensSlider;
	private HSlider _hudScaleSlider;
	private CheckBox _displayFpsChk;
	private CheckBox _recordReplaysChk;
	private CheckBox _seedMapFilesChk;
	private OptionButton _healthBarsOpt;
	private OptionButton _languageOpt;


	private Button _applyBtn;
	private Button _cancelBtn;
	private Button _resetBtn;

	public override void _Ready()
	{
		Instance = this;
		MouseFilter = MouseFilterEnum.Stop;

		_bgPanel = GetNodeOrNull<Panel>("Background");
		if (_bgPanel != null)
		{
			_bgPanel.MouseFilter = MouseFilterEnum.Stop;
		}
		if (GetNodeOrNull<Control>("OverlayBg") is Control overlay)
		{
			overlay.MouseFilter = MouseFilterEnum.Stop;
		}
		if (GetNodeOrNull<Control>("CenterContainer") is Control center)
		{
			center.MouseFilter = MouseFilterEnum.Stop;
		}

		_mainFrame = GetNode<PanelContainer>("CenterContainer/MainFrame");
		_mainFrame.MouseFilter = MouseFilterEnum.Stop;
		_videoPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel");
		_audioPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel");
		_gameplayPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel");


		_settingsTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/SettingsTitle");
		_videoTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/PanelTitle");
		_audioTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/PanelTitle");
		_gameplayTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/PanelTitle");


		_resolutionOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/ResolutionOpt");
		_qualityOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/QualityOpt");
		_windowModeOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/WindowModeOpt");
		_vsyncOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/VideoPanel/VBox/VsyncOpt");


		_masterSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/MasterSlider");
		_musicSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/MusicSlider");
		_sfxSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/SfxSlider");
		_voiceSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/AudioPanel/VBox/VoiceSlider");


		_scrollSpeedSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/ScrollSpeedSlider");
		_mouseSensSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/MouseSensSlider");
		_hudScaleSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HudScaleSlider");
		_displayFpsChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/DisplayFpsChk");
		_recordReplaysChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/RecordReplaysChk");
		_seedMapFilesChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/SeedMapFilesChk");
		_healthBarsOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HealthBarsOpt");
		_languageOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/LanguageOpt");


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
			if (IsOverlay)
			{
				_bgPanel.Visible = false;
				if (GetNodeOrNull<ColorRect>("OverlayBg") is ColorRect rect)
				{
					rect.Visible = true;
				}
			}
			else
			{
				_bgPanel.Visible = true;
				_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
				if (GetNodeOrNull<ColorRect>("OverlayBg") is ColorRect rect)
				{
					rect.Visible = false;
				}
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
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/HealthBarsLabel",
			"CenterContainer/MainFrame/VBoxContainer/ColumnsContainer/GameplayPanel/VBox/LanguageLabel"
		};
		foreach (var path in labelPaths)
		{
			var lbl = GetNode<Label>(path);
			lbl.Text = TranslationServer.Translate(lbl.Text);
			lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
			lbl.AddThemeFontSizeOverride("font_size", 14);
		}

		UIStyle.ApplyCheckboxStyle(_displayFpsChk);
		_displayFpsChk.Text = TranslationServer.Translate(_displayFpsChk.Text);
		UIStyle.ApplyCheckboxStyle(_recordReplaysChk);
		_recordReplaysChk.Text = TranslationServer.Translate(_recordReplaysChk.Text);
		UIStyle.ApplyCheckboxStyle(_seedMapFilesChk);
		_seedMapFilesChk.Text = TranslationServer.Translate(_seedMapFilesChk.Text);
		_seedMapFilesChk.TooltipText = TranslationServer.Translate(_seedMapFilesChk.TooltipText);
	}

	private void PopulateDropdowns()
	{
		_resolutionOpt.Clear();
		for (int i = 0; i < GameSettings.Resolutions.Count; i++)
		{
			var res = GameSettings.Resolutions[i];
			_resolutionOpt.AddItem($"{res.X} x {res.Y}", i);
		}

		_qualityOpt.Clear();
		_qualityOpt.AddItem(TranslationServer.Translate("Low"), 0);
		_qualityOpt.AddItem(TranslationServer.Translate("Medium"), 1);
		_qualityOpt.AddItem(TranslationServer.Translate("High"), 2);
		_qualityOpt.AddItem(TranslationServer.Translate("Ultra"), 3);

		_windowModeOpt.Clear();
		_windowModeOpt.AddItem(TranslationServer.Translate("Fullscreen"), 0);
		_windowModeOpt.AddItem(TranslationServer.Translate("Windowed"), 1);
		_windowModeOpt.AddItem(TranslationServer.Translate("Borderless"), 2);

		_vsyncOpt.Clear();
		_vsyncOpt.AddItem(TranslationServer.Translate("On"), 0);
		_vsyncOpt.AddItem(TranslationServer.Translate("Off"), 1);

		_healthBarsOpt.Clear();
		_healthBarsOpt.AddItem(TranslationServer.Translate("Hidden"), 0);
		_healthBarsOpt.AddItem(TranslationServer.Translate("Visible"), 1);
		_healthBarsOpt.AddItem(TranslationServer.Translate("Damaged"), 2);

		_languageOpt.Clear();
		_languageOpt.AddItem("English", 0);
		_languageOpt.AddItem("Español", 1);
		_languageOpt.AddItem("Français", 2);
		_languageOpt.AddItem("Deutsch", 3);
		_languageOpt.AddItem("Português", 4);
		_languageOpt.AddItem("Русский", 5);
		_languageOpt.AddItem("中文", 6);
		_languageOpt.AddItem("日本語", 7);
		_languageOpt.AddItem("العربية", 8);
		_languageOpt.AddItem("हिन्दी", 9);


		var dropdowns = new[] { _resolutionOpt, _qualityOpt, _windowModeOpt, _vsyncOpt, _healthBarsOpt, _languageOpt };
		foreach (var opt in dropdowns)
		{
			opt.Flat = false;
			opt.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
			opt.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
			opt.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
			opt.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

			opt.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			opt.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			opt.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
			opt.AddThemeFontSizeOverride("font_size", 15);

			opt.ItemSelected += (idx) => UIManager.Instance.PlayClickSound();
			opt.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		}

		_windowModeOpt.ItemSelected += (idx) =>
		{
			if (idx == 0 || idx == 2)
			{
				_resolutionOpt.Disabled = true;
				_resolutionOpt.Select(-1);
			}
			else
			{
				_resolutionOpt.Disabled = false;
				_resolutionOpt.Select(GameSettings.ResolutionIdx);
			}
		};

		_displayFpsChk.Pressed += () => UIManager.Instance.PlayClickSound();
		_displayFpsChk.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		_recordReplaysChk.Pressed += () => UIManager.Instance.PlayClickSound();
		_recordReplaysChk.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		_seedMapFilesChk.Pressed += () => UIManager.Instance.PlayClickSound();
		_seedMapFilesChk.MouseEntered += () => UIManager.Instance.PlayHoverSound();
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
		btn.AddThemeConstantOverride("icon_max_width", 0);

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

		if (GameSettings.WindowModeIdx == 0 || GameSettings.WindowModeIdx == 2)
		{
			_resolutionOpt.Disabled = true;
			_resolutionOpt.Select(-1);
		}
		else
		{
			_resolutionOpt.Disabled = false;
		}

		_masterSlider.Value = GameSettings.MasterVolume;
		_musicSlider.Value = GameSettings.MusicVolume;
		_sfxSlider.Value = GameSettings.SfxVolume;
		_voiceSlider.Value = GameSettings.VoiceVolume;

		_scrollSpeedSlider.Value = GameSettings.ScrollSpeed;
		_mouseSensSlider.Value = GameSettings.MouseSens;
		_hudScaleSlider.Value = GameSettings.HudScale;
		_displayFpsChk.ButtonPressed = GameSettings.DisplayFps;
		_recordReplaysChk.ButtonPressed = GameSettings.RecordReplays;
		_seedMapFilesChk.ButtonPressed = GameSettings.SeedMapFiles;
		int hbIdx = GameSettings.ShowHealthBars switch
		{
			"hidden" => 0,
			"visible" => 1,
			"damaged" => 2,
			_ => 2
		};
		_healthBarsOpt.Select(hbIdx);

		int langIdx = GameSettings.Language switch
		{
			"en" => 0,
			"es" => 1,
			"fr" => 2,
			"de" => 3,
			"pt" => 4,
			"ru" => 5,
			"zh" => 6,
			"ja" => 7,
			"ar" => 8,
			"hi" => 9,
			_ => 0
		};
		_languageOpt.Select(langIdx);
	}

	private void ApplySettings()
	{
		int modeIdx = _windowModeOpt.Selected;
		if (modeIdx == 1)
		{
			int resSel = _resolutionOpt.Selected;
			if (resSel >= 0 && resSel < GameSettings.Resolutions.Count)
			{
				GetWindow().Size = GameSettings.Resolutions[resSel];
			}
		}

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


		if (modeIdx == 1)
		{
			GameSettings.ResolutionIdx = _resolutionOpt.Selected;
		}
		GameSettings.QualityIdx = _qualityOpt.Selected;
		GameSettings.DownsamplingIdx = GameSettings.GetDownsamplingIdxForQuality(GameSettings.QualityIdx);
		GameSettings.WindowModeIdx = _windowModeOpt.Selected;
		GameSettings.VsyncIdx = _vsyncOpt.Selected;

		GameSettings.MasterVolume = (float)_masterSlider.Value;
		GameSettings.MusicVolume = (float)_musicSlider.Value;
		GameSettings.SfxVolume = (float)_sfxSlider.Value;
		GameSettings.VoiceVolume = (float)_voiceSlider.Value;

		GameSettings.ScrollSpeed = (float)_scrollSpeedSlider.Value;
		GameSettings.MouseSens = (float)_mouseSensSlider.Value;
		GameSettings.HudScale = (float)_hudScaleSlider.Value;
		GameSettings.DisplayFps = _displayFpsChk.ButtonPressed;
		GameSettings.RecordReplays = _recordReplaysChk.ButtonPressed;
		GameSettings.SeedMapFiles = _seedMapFilesChk.ButtonPressed;
		GameSettings.ShowHealthBars = _healthBarsOpt.Selected switch
		{
			0 => "hidden",
			1 => "visible",
			2 => "damaged",
			_ => "damaged"
		};

		string newLang = _languageOpt.Selected switch
		{
			0 => "en",
			1 => "es",
			2 => "fr",
			3 => "de",
			4 => "pt",
			5 => "ru",
			6 => "zh",
			7 => "ja",
			8 => "ar",
			9 => "hi",
			_ => "en"
		};
		GameSettings.Language = newLang;
		LocalizationManager.UpdateLocale(newLang);

		GameSettings.Save();
		GameSettings.ApplyGraphicsSettings(this);


		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ApplyHUDScale();
			InGameHUD.Instance.UpdateFPSVisibility();
		}
		if (MapEditorHUD.Instance != null)
		{
			MapEditorHUD.Instance.UpdateFPSVisibility();
		}

		GD.Print("Settings Applied successfully!");
		CloseOrTransition();
	}

	private void CancelSettings()
	{
		GameSettings.Load();
		UIManager.Instance.UpdateAudioVolumes();
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.UpdateFPSVisibility();
		}
		if (MapEditorHUD.Instance != null)
		{
			MapEditorHUD.Instance.UpdateFPSVisibility();
		}
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
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.UpdateFPSVisibility();
		}
		if (MapEditorHUD.Instance != null)
		{
			MapEditorHUD.Instance.UpdateFPSVisibility();
		}
		GD.Print("Settings reset to defaults.");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton or InputEventMouseMotion)
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && escapeEvent.Keycode == Key.Escape)
		{
			GetViewport().SetInputAsHandled();
			CancelSettings();
		}
	}
}
