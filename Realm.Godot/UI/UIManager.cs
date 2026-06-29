using Godot;
using System;
using Realm.Godot.ReplaySystem;

public enum GameScreen
{
	MainMenu,
	LobbyBrowser,
	LobbyRoom,
	Settings,
	InGameHUD,
	GameOver,
	MapDiscovery,
	MapDetails,
	MapEditorHUD,
	ReplayList
}

public partial class UIManager : Control
{
	public static UIManager Instance { get; private set; }

	[Export] public PackedScene MainMenuScene;
	[Export] public PackedScene LobbyBrowserScene;
	[Export] public PackedScene LobbyRoomScene;
	[Export] public PackedScene SettingsScene;
	[Export] public PackedScene InGameHUDScene;
	[Export] public PackedScene GameOverScene;
	[Export] public PackedScene MapDiscoveryScene;
	[Export] public PackedScene MapDetailsScene;
	[Export] public PackedScene MapEditorHUDScene;
	[Export] public PackedScene ReplayListScene;

	private Control _currentScreen;
	private ColorRect _fadeOverlay;
	private AnimationPlayer _fadeAnim;
	private GameScreen _targetScreen;
	private bool _isVictory = true; // State passed to Game Over screen

	private MapData _selectedMapData;

	public void TransitionToMapDetails(MapData mapData)
	{
		_selectedMapData = mapData;
		TransitionTo(GameScreen.MapDetails);
	}

	private AudioStreamPlayer _musicPlayer;
	private AudioStreamPlayer _sfxPlayer;

	public override void _Ready()
	{
		Instance = this;
		MouseFilter = MouseFilterEnum.Ignore;

		// Initialize Audio Players
		_musicPlayer = new AudioStreamPlayer();
		AddChild(_musicPlayer);

		_sfxPlayer = new AudioStreamPlayer();
		AddChild(_sfxPlayer);

		ApplyStartupSettings();

		// Create fade overlay for smooth transitions
		CreateFadeOverlay();

		// Start with Main Menu, or bypass directly to InGameHUD if game is already active
		if (LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted)
		{
			TransitionTo(GameScreen.InGameHUD);
		}
		else
		{
			TransitionTo(GameScreen.MainMenu);
		}
	}

	private void ApplyStartupSettings()
	{
		GameSettings.Load();
		GameSettings.ApplyGraphicsSettings(this);
		LocalizationManager.SetupTranslations();

		int modeIdx = GameSettings.WindowModeIdx;
		if (modeIdx != 2)
		{
			if (GameSettings.ResolutionIdx >= 0 && GameSettings.ResolutionIdx < GameSettings.Resolutions.Count)
			{
				GetWindow().Size = GameSettings.Resolutions[GameSettings.ResolutionIdx];
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

		if (GameSettings.VsyncIdx == 0)
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		}
		else
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		}

		UpdateAudioVolumes();
	}

	public void UpdateAudioVolumes()
	{
		if (_musicPlayer != null)
		{
			float master = GameSettings.MasterVolume / 100f;
			float music = GameSettings.MusicVolume / 100f;
			float combined = master * music;
			_musicPlayer.VolumeDb = combined <= 0f ? -80f : Mathf.LinearToDb(combined);
		}

		if (_sfxPlayer != null)
		{
			float master = GameSettings.MasterVolume / 100f;
			float sfx = GameSettings.SfxVolume / 100f;
			float combined = master * sfx;
			_sfxPlayer.VolumeDb = combined <= 0f ? -80f : Mathf.LinearToDb(combined);
		}
	}

	private void CreateFadeOverlay()
	{
		_fadeOverlay = new ColorRect();
		_fadeOverlay.Color = new Color(0, 0, 0, 1); // Black
		_fadeOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_fadeOverlay.MouseFilter = MouseFilterEnum.Ignore; // Let clicks pass when transparent
		AddChild(_fadeOverlay);

		_fadeAnim = new AnimationPlayer();
		AddChild(_fadeAnim);

		// Create animations programmatically
		var library = new AnimationLibrary();
		
		// Fade In (to black)
		var animFadeIn = new Animation();
		int trackId = animFadeIn.AddTrack(Animation.TrackType.Value);
		animFadeIn.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeIn.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 0));
		animFadeIn.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 1));
		library.AddAnimation("fade_in", animFadeIn);

		// Fade Out (to clear)
		var animFadeOut = new Animation();
		trackId = animFadeOut.AddTrack(Animation.TrackType.Value);
		animFadeOut.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeOut.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 1));
		animFadeOut.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 0));
		library.AddAnimation("fade_out", animFadeOut);

		_fadeAnim.AddAnimationLibrary("", library);
		_fadeOverlay.Color = new Color(0, 0, 0, 0); // Start clear
	}

	public void TransitionTo(GameScreen screen, bool isVictory = true)
	{
		if (screen == GameScreen.GameOver && (_targetScreen == GameScreen.GameOver || _currentScreen is GameOver))
		{
			return;
		}
		_targetScreen = screen;
		_isVictory = isVictory;

		if (screen == GameScreen.GameOver || screen == GameScreen.MainMenu)
		{
			GameHost.Instance?.StopRecording();
		}

		// Make overlay intercept inputs during transition
		_fadeOverlay.MouseFilter = MouseFilterEnum.Stop;

		// Play fade in
		_fadeAnim.Play("fade_in");

		// Audio Transitions
		if (screen == GameScreen.MainMenu || screen == GameScreen.LobbyBrowser || screen == GameScreen.LobbyRoom || screen == GameScreen.Settings || screen == GameScreen.MapDiscovery || screen == GameScreen.MapDetails)
		{
			PlayMusic("res://Assets/Audio/Music/enchanted_realm.ogg");
		}
		else if (screen == GameScreen.InGameHUD)
		{
			PlayMusic("res://Assets/Audio/Music/battle_anthem.ogg");
		}
		else if (screen == GameScreen.GameOver)
		{
			StopMusic();
			if (_sfxPlayer != null)
			{
				var soundFile = isVictory ? "res://Assets/Audio/UI/victory_theme_sting.ogg" : "res://Assets/Audio/UI/defeat_drone_low.ogg";
				_sfxPlayer.Stream = GD.Load<AudioStream>(soundFile);
				_sfxPlayer.Play();
			}
		}
		
		// Connect to finished signal or use a timer
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += OnFadeInComplete;
	}

	public void PlayMusic(string path)
	{
		if (_musicPlayer != null)
		{
			if (_musicPlayer.Stream?.ResourcePath == path && _musicPlayer.Playing)
				return;
			
			var stream = GD.Load<AudioStream>(path);
			if (stream != null)
			{
				_musicPlayer.Stream = stream;
				_musicPlayer.Play();
			}
		}
	}

	public void StopMusic()
	{
		_musicPlayer?.Stop();
	}

	public void PlayClickSound()
	{
		if (_sfxPlayer != null)
		{
			var stream = GD.Load<AudioStream>("res://Assets/Audio/UI/click_confirm_heavy.ogg");
			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
	}

	public void PlayHoverSound()
	{
		if (_sfxPlayer != null)
		{
			var stream = GD.Load<AudioStream>("res://Assets/Audio/UI/hover_highlight_sparkle.ogg");
			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
	}

	public void PlayWarningSound()
	{
		if (_sfxPlayer != null)
		{
			var stream = GD.Load<AudioStream>("res://Assets/Audio/UI/alert_warning_buzz.ogg");
			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
	}

	private void OnFadeInComplete()
	{
		// Clean up current screen
		if (_currentScreen != null)
		{
			if (_currentScreen is MapEditorHUD)
			{
				GameHost.Instance?.ExitMapEditorMode();
			}
			_currentScreen.QueueFree();
			_currentScreen = null;
		}

		// Instantiate new screen
		PackedScene targetScene = null;
		switch (_targetScreen)
		{
			case GameScreen.MainMenu:
				targetScene = MainMenuScene ?? GD.Load<PackedScene>("res://UI/MainMenu.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				if (LobbyManager.Instance != null)
				{
					LobbyManager.Instance.Disconnect();
				}
				break;
			case GameScreen.LobbyBrowser:
				targetScene = LobbyBrowserScene ?? GD.Load<PackedScene>("res://UI/LobbyBrowser.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.LobbyRoom:
				targetScene = LobbyRoomScene ?? GD.Load<PackedScene>("res://UI/LobbyRoom.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.Settings:
				targetScene = SettingsScene ?? GD.Load<PackedScene>("res://UI/SettingsMenu.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.InGameHUD:
				targetScene = InGameHUDScene ?? GD.Load<PackedScene>("res://UI/InGameHUD.tscn");
				// In-game HUD keeps mouse captured by default (RTS screen allows toggle)
				Input.MouseMode = Input.MouseModeEnum.Visible; // Keep visible for HUD interaction
				break;
			case GameScreen.GameOver:
				targetScene = GameOverScene ?? GD.Load<PackedScene>("res://UI/GameOver.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.MapDiscovery:
				targetScene = MapDiscoveryScene ?? GD.Load<PackedScene>("res://UI/MapDiscovery.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.MapDetails:
				targetScene = MapDetailsScene ?? GD.Load<PackedScene>("res://UI/MapDetails.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.MapEditorHUD:
				targetScene = MapEditorHUDScene ?? GD.Load<PackedScene>("res://UI/MapEditorHUD.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				// Notify GameHost to set up editor
				GameHost.Instance?.StartMapEditorMode();
				break;
			case GameScreen.ReplayList:
				targetScene = ReplayListScene ?? GD.Load<PackedScene>("res://UI/ReplayListPanel.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
		}

		if (targetScene != null)
		{
			_currentScreen = targetScene.Instantiate<Control>();
			AddChild(_currentScreen);

			if (_targetScreen == GameScreen.InGameHUD && ReplayPlaybackManager.Instance.IsPlayingReplay)
			{
				GameHost.Instance?.StopRecording();
				var panelScene = GD.Load<PackedScene>("res://UI/ReplayViewerPanel.tscn");
				if (panelScene != null)
				{
					var panel = panelScene.Instantiate<Control>();
					_currentScreen.AddChild(panel);
				}
			}
			
			// Move fade overlay to front so it stays on top during transition
			MoveChild(_fadeOverlay, GetChildCount() - 1);

			// If transitioning to Game Over, pass victory/defeat status
			if (_currentScreen is GameOver gameOver)
			{
				gameOver.SetStatus(_isVictory);
			}
			else if (_currentScreen is MapDetails mapDetails)
			{
				mapDetails.SetMapData(_selectedMapData);
			}
		}

		// Play fade out
		_fadeAnim.Play("fade_out");
		
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += () => 
		{
			_fadeOverlay.MouseFilter = MouseFilterEnum.Ignore; // Allow clicks again
		};
	}

	// Helper to overlay settings popup
	public void OpenSettingsOverlay()
	{
		var settingsPopup = GD.Load<PackedScene>("res://UI/SettingsMenu.tscn").Instantiate<SettingsMenu>();
		settingsPopup.IsOverlay = true;
		AddChild(settingsPopup);
		MoveChild(settingsPopup, GetChildCount() - 1);
		if (_fadeOverlay != null)
		{
			MoveChild(_fadeOverlay, GetChildCount() - 1); // Keep fade overlay at the very top
		}
	}
}
