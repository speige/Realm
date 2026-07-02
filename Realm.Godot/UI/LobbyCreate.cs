using Godot;
using System;
using System.Collections.Generic;

public partial class LobbyCreate : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _centralPanel;
	private PanelContainer _briefingPanel;

	private Button _backButton;
	private Button _createButton;
	private OptionButton _mapSelectButton;
	private RichTextLabel _briefingText;

	private Label _titleLabel;
	private Label _mapSelectLabel;

	private List<MapBriefingDetails> _availableMaps = new List<MapBriefingDetails>();

	public override void _Ready()
	{
		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_centralPanel = GetNode<PanelContainer>("CentralPanel");
		
		_backButton = GetNode<Button>("BackButton");
		_createButton = GetNode<Button>("CentralPanel/ContentContainer/CreateButton");
		_mapSelectButton = GetNode<OptionButton>("CentralPanel/ContentContainer/MapSelectButton");
		_briefingPanel = GetNode<PanelContainer>("CentralPanel/ContentContainer/BriefingPanel");
		_briefingText = GetNode<RichTextLabel>("CentralPanel/ContentContainer/BriefingPanel/BriefingText");

		_titleLabel = GetNode<Label>("Title");
		_mapSelectLabel = GetNode<Label>("CentralPanel/ContentContainer/MapSelectLabel");

		ApplyThemeStyles();

		_availableMaps = MapInfoHelper.GetAvailableMaps();
		_mapSelectButton.Clear();
		foreach (var map in _availableMaps)
		{
			_mapSelectButton.AddItem(map.DisplayName);
		}

		_mapSelectButton.ItemSelected += OnMapSelected;
		if (_mapSelectButton.ItemCount > 0)
		{
			_mapSelectButton.Selected = 0;
			OnMapSelected(0);
		}
	}

	private void ApplyThemeStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_centralPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_briefingPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));

		UIStyle.ApplyTitle(_titleLabel, LobbyManager.Instance.IsSinglePlayer ? "SINGLE PLAYER" : "CREATE CUSTOM MATCH", 36);
		UIStyle.ApplyTitle(_mapSelectLabel, "SELECT MAP", 20);

		SetupPillarButton(_backButton, "◀", () => UIManager.Instance.TransitionTo(LobbyManager.Instance.IsSinglePlayer ? GameScreen.MainMenu : GameScreen.LobbyBrowser));
		SetupCreateButton();

		_mapSelectButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_mapSelectButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_mapSelectButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_mapSelectButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_mapSelectButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_mapSelectButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_mapSelectButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_mapSelectButton.AddThemeFontSizeOverride("font_size", 16);

		_briefingText.AddThemeColorOverride("default_color", new Color(0.85f, 0.85f, 0.9f));
		_briefingText.AddThemeFontSizeOverride("normal_font_size", 14);

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void SetupPillarButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		btn.Text = text;
		btn.AddThemeFontSizeOverride("font_size", 24);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);

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

	private void SetupCreateButton()
	{
		_createButton.Flat = false;
		UIStyle.ApplyButtonText(_createButton, LobbyManager.Instance.IsSinglePlayer ? "START GAME" : "CREATE LOBBY", 18);
		
		_createButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_createButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_createButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_createButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_createButton.Pressed += OnCreatePressed;
		_createButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private async void OnCreatePressed()
	{
		UIManager.Instance.PlayClickSound();

		if (LobbyManager.Instance.IsSinglePlayer)
		{
			string singleMapPathName = "melee";
			string singleMapDisplayName = "Melee Battlefield";
			int singleSelectedIndex = _mapSelectButton.Selected;
			if (singleSelectedIndex >= 0 && singleSelectedIndex < _availableMaps.Count)
			{
				singleMapPathName = _availableMaps[singleSelectedIndex].PathName;
				singleMapDisplayName = _availableMaps[singleSelectedIndex].DisplayName;
			}
			_createButton.Disabled = true;
			LobbyManager.Instance.HostSinglePlayerGame(singleMapPathName, singleMapDisplayName);
			_createButton.Disabled = false;
			return;
		}

		if (LobbyManager.Instance.LocalNatType == NatType.Symmetric)
		{
			UIManager.Instance.PlayWarningSound();
			ShowSTUNErrorModal();
			return;
		}
		
		string mapPathName = "melee";
		string mapDisplayName = "Melee Battlefield";
		int selectedIndex = _mapSelectButton.Selected;
		if (selectedIndex >= 0 && selectedIndex < _availableMaps.Count)
		{
			mapPathName = _availableMaps[selectedIndex].PathName;
			mapDisplayName = _availableMaps[selectedIndex].DisplayName;
		}

		_createButton.Disabled = true;
		bool success = await LobbyManager.Instance.HostLobbyAsync(mapPathName, mapDisplayName);
		_createButton.Disabled = false;

		if (success)
		{
			UIManager.Instance.TransitionTo(GameScreen.LobbyRoom);
		}
		else
		{
			UIManager.Instance.PlayWarningSound();
			GD.PrintErr("[LobbyCreate] Failed to host lobby.");
		}
	}

	private void ShowSTUNErrorModal()
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(450, 220);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(400, 180);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("HOSTING ERROR"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = Tr("STUN test determines you cannot host a game due to a Symmetric NAT router configuration. Please configure port forwarding or UPnP.");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var okBtn = new Button();
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, Tr("OK"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		okBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		};
		vbox.AddChild(okBtn);
	}

	private void OnMapSelected(long index)
	{
		UIManager.Instance.PlayClickSound();
		if (index >= 0 && index < _availableMaps.Count)
		{
			_briefingText.Text = _availableMaps[(int)index].Description;
		}
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}
}
