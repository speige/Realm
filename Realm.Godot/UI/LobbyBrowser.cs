using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public partial class LobbyBrowser : Control
{
	public struct LobbyData
	{
		public string LobbyId;
		public string Map;
		public string Mode;
		public string Players;
		public int Ping;
	}

	private List<LobbyData> _allLobbies = new List<LobbyData>();
	private List<LobbyData> _filteredLobbies = new List<LobbyData>();

	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _filterPanel;
	private PanelContainer _lobbyPanel;

	private Button _backButton;
	private Button _refreshButton;
	private Button _hostButton;
	private LineEdit _searchBar;
	private CheckBox _campaignCheck;
	private CheckBox _meleeCheck;
	private CheckBox _tutorialCheck;
	private CheckBox _arcadeCheck;
	private VBoxContainer _lobbyListContainer;

	private Label _browserTitle;
	private Label _filterTitle;
	private Label _mapCol;
	private Label _modeCol;
	private Label _playersCol;
	private Label _pingCol;

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };
	private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

	public override void _Ready()
	{
		// Bind Panels
		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_lobbyPanel = GetNode<PanelContainer>("LobbyPanel");

		// Bind Buttons and Input
		_backButton = GetNode<Button>("BackButton");
		_refreshButton = GetNode<Button>("RefreshButton");
		_hostButton = GetNode<Button>("HostButton");
		_searchBar = GetNode<LineEdit>("SearchBar");
		_campaignCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/CampaignCheck");
		_meleeCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/MeleeCheck");
		_tutorialCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/TutorialCheck");
		_arcadeCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/ArcadeCheck");
		_lobbyListContainer = GetNode<VBoxContainer>("LobbyPanel/VBoxContainer/ScrollContainer/LobbyListContainer");

		// Bind Labels
		_browserTitle = GetNode<Label>("BrowserTitle");
		_filterTitle = GetNode<Label>("FilterPanel/VBoxContainer/FilterTitle");
		_mapCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/MapCol");
		_modeCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/ModeCol");
		_playersCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PlayersCol");
		_pingCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PingCol");

		// Apply Theme Styling
		ApplyStyles();

		// Listen to NAT testing status
		LobbyManager.Instance.NatTestCompleted += UpdateHostButtonState;
		UpdateHostButtonState();

		// Refresh lists
		FetchLobbiesFromRegistry();
	}

	private void ApplyStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_filterPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_lobbyPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		// Headers & Text
		UIStyle.ApplyTitle(_browserTitle, "CUSTOM LOBBY BROWSER", 36);
		UIStyle.ApplyTitle(_filterTitle, "FILTER", 20);

		// Table Columns
		foreach (var lbl in new[] { _mapCol, _modeCol, _playersCol, _pingCol })
		{
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lbl.AddThemeFontSizeOverride("font_size", 16);
		}

		// Back and Refresh Buttons (Symbols)
		SetupPillarButton(_backButton, "◀", () => UIManager.Instance.TransitionTo(GameScreen.MainMenu));
		Label refreshIcon = new Label();
		refreshIcon.Text = "↻";
		refreshIcon.HorizontalAlignment = HorizontalAlignment.Center;
		refreshIcon.VerticalAlignment = VerticalAlignment.Center;
		refreshIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		refreshIcon.PivotOffset = new Vector2(30, 30);
		refreshIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
		refreshIcon.AddThemeFontSizeOverride("font_size", 28);
		refreshIcon.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_refreshButton.AddChild(refreshIcon);

		_refreshButton.MouseEntered += () => refreshIcon.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_refreshButton.MouseExited += () => refreshIcon.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_refreshButton.ButtonDown += () => refreshIcon.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_refreshButton.ButtonUp += () => refreshIcon.AddThemeColorOverride("font_color", _refreshButton.IsHovered() ? UIStyle.ColorGold : UIStyle.ColorGoldDull);

		SetupPillarButton(_refreshButton, "", () => 
		{
			var tween = CreateTween();
			tween.TweenProperty(refreshIcon, "rotation", refreshIcon.Rotation + Mathf.Pi * 2, 0.4f);
			FetchLobbiesFromRegistry();
		});

		// Host Button
		SetupHostButton();

		// Checkboxes
		var checkBoxes = new[] { _campaignCheck, _meleeCheck, _tutorialCheck, _arcadeCheck };
		foreach (var cb in checkBoxes)
		{
			cb.Pressed += () => 
			{
				UIManager.Instance.PlayClickSound();
				ApplyFilters();
			};
			cb.MouseEntered += () => UIManager.Instance.PlayHoverSound();
			UIStyle.ApplyCheckboxStyle(cb);
		}

		// Search Bar
		_searchBar.TextChanged += (text) => ApplyFilters();
		_searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		_searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		_searchBar.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		_searchBar.PlaceholderText = "Search Lobbies...";
		_searchBar.RightIcon = GD.Load<Texture2D>("res://Assets/UI/search_icon.jpg");

		// Side pillars
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

	private void SetupHostButton()
	{
		_hostButton.Flat = false;
		UIStyle.ApplyButtonText(_hostButton, "HOST A GAME", 18);
		
		_hostButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_hostButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_hostButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_hostButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_hostButton.Pressed += async () => 
		{
			UIManager.Instance.PlayClickSound();
			_hostButton.Disabled = true;
			bool success = await LobbyManager.Instance.HostLobbyAsync("The Frosting Pass");
			_hostButton.Disabled = false;
			
			if (success)
			{
				UIManager.Instance.TransitionTo(GameScreen.LobbyRoom);
			}
			else
			{
				UIManager.Instance.PlayWarningSound();
				GD.PrintErr("[LobbyBrowser] Failed to host game.");
			}
		};
		_hostButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void UpdateHostButtonState()
	{
		if (LobbyManager.Instance.LocalNatType == NatType.Symmetric)
		{
			_hostButton.Disabled = true;
			_hostButton.TooltipText = "WARNING: Symmetric NAT detected! Hosting is disabled since direct UDP connections cannot be established.";
			_hostButton.AddThemeColorOverride("font_disabled_color", new Color(0.7f, 0.3f, 0.3f));
		}
		else
		{
			_hostButton.Disabled = false;
			_hostButton.TooltipText = $"NAT Classification: {LobbyManager.Instance.LocalNatType} (Hole Punching Supported)";
			_hostButton.RemoveThemeColorOverride("font_disabled_color");
		}
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	private void FetchLobbiesFromRegistry()
	{
		Task.Run(async () =>
		{
			try
			{
				var json = await LobbyManager.Instance.FetchLobbiesRawAsync();
				if (json != null)
				{
					using var doc = JsonDocument.Parse(json);
					var lobbyList = new List<LobbyData>();
					
					foreach (var item in doc.RootElement.EnumerateArray())
					{
						lobbyList.Add(new LobbyData
						{
							LobbyId = item.GetProperty("lobbyId").GetString() ?? "",
							Map = item.GetProperty("map").GetString() ?? "",
							Mode = "Melee", // Default mode
							Players = $"{item.GetProperty("slotsUsed").GetInt32()}/{item.GetProperty("maxPlayers").GetInt32()}",
							Ping = item.GetProperty("estimatedPingMs").GetInt32()
						});
					}

					Callable.From(() => OnLobbiesFetched(lobbyList)).CallDeferred();
				}
				else
				{
					Callable.From(() => OnLobbiesFetchedFallback()).CallDeferred();
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyBrowser] Error fetching lobbies: {ex.Message}");
				Callable.From(() => OnLobbiesFetchedFallback()).CallDeferred();
			}
		});
	}

	private void OnLobbiesFetched(List<LobbyData> list)
	{
		_allLobbies.Clear();
		if (list != null)
		{
			_allLobbies.AddRange(list);
		}
		ApplyFilters();
	}

	private void OnLobbiesFetchedFallback()
	{
		// Generate dummy lobbies for offline preview if registry is not reachable
		_allLobbies.Clear();
		_allLobbies.Add(new LobbyData { LobbyId = "offline-1", Map = "CastleTD (Offline Default)", Mode = "Melee", Players = "2/8", Ping = 5 });
		_allLobbies.Add(new LobbyData { LobbyId = "offline-2", Map = "Frosting Pass (Offline Default)", Mode = "Melee", Players = "1/8", Ping = 8 });
		ApplyFilters();
	}

	private void ApplyFilters()
	{
		_filteredLobbies = _allLobbies.ToList();

		string query = _searchBar.Text.Trim().ToLower();
		if (!string.IsNullOrEmpty(query))
		{
			_filteredLobbies = _filteredLobbies.Where(x => x.Map.ToLower().Contains(query)).ToList();
		}

		bool campaign = _campaignCheck.ButtonPressed;
		bool melee = _meleeCheck.ButtonPressed;
		bool tutorial = _tutorialCheck.ButtonPressed;
		bool arcade = _arcadeCheck.ButtonPressed;

		if (campaign || melee || tutorial || arcade)
		{
			_filteredLobbies = _filteredLobbies.Where(x =>
				(campaign && x.Mode.Equals("Campaign", StringComparison.OrdinalIgnoreCase)) ||
				(melee && x.Mode.Equals("Melee", StringComparison.OrdinalIgnoreCase)) ||
				(tutorial && x.Mode.Equals("Tutorial", StringComparison.OrdinalIgnoreCase)) ||
				(arcade && x.Mode.Equals("Arcade", StringComparison.OrdinalIgnoreCase))
			).ToList();
		}

		RefreshLobbyDisplay();
	}

	private void RefreshLobbyDisplay()
	{
		foreach (Node child in _lobbyListContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var lobby in _filteredLobbies)
		{
			var row = CreateLobbyRow(lobby);
			_lobbyListContainer.AddChild(row);
		}
	}

	private PanelContainer CreateLobbyRow(LobbyData data)
	{
		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(0, 48);

		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.3f);
		rowStyle.BorderColor = new Color(0.25f, 0.25f, 0.3f, 0.2f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.ContentMarginLeft = 12;
		rowStyle.ContentMarginRight = 24;
		rowStyle.ContentMarginTop = 0;
		rowStyle.ContentMarginBottom = 0;
		
		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = new Color(0.95f, 0.82f, 0.55f, 0.12f); // Gold hover fill
		hoverStyle.BorderColor = UIStyle.ColorGold;
		hoverStyle.SetBorderWidthAll(1);
		hoverStyle.ContentMarginLeft = 12;
		hoverStyle.ContentMarginRight = 24;
		hoverStyle.ContentMarginTop = 0;
		hoverStyle.ContentMarginBottom = 0;

		panel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		panel.AddChild(hBox);

		var lblMap = new Label();
		lblMap.Text = $"  {data.Map}";
		lblMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMap.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		lblMap.AddThemeFontSizeOverride("font_size", 16);
		lblMap.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblMap);

		var lblMode = new Label();
		lblMode.Text = data.Mode;
		lblMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMode.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblMode.AddThemeFontSizeOverride("font_size", 15);
		lblMode.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblMode);

		var lblPlayers = new Label();
		lblPlayers.Text = data.Players;
		lblPlayers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPlayers.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblPlayers.AddThemeFontSizeOverride("font_size", 15);
		lblPlayers.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblPlayers);

		var lblPing = new Label();
		lblPing.Text = $"{data.Ping} ms  ";
		lblPing.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPing.HorizontalAlignment = HorizontalAlignment.Right;
		lblPing.AddThemeColorOverride("font_color", data.Ping < 80 ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.85f, 0.65f, 0.3f));
		lblPing.AddThemeFontSizeOverride("font_size", 15);
		lblPing.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblPing);

		panel.MouseEntered += () => 
		{
			UIManager.Instance.PlayHoverSound();
			panel.AddThemeStyleboxOverride("panel", hoverStyle);
		};
		panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", rowStyle);

		panel.GuiInput += async (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				UIManager.Instance.PlayClickSound();
				
				// Click transitions to join handshake
				panel.MouseFilter = MouseFilterEnum.Ignore; // Disable clicks during handshake
				bool success = await LobbyManager.Instance.JoinLobbyAsync(data.LobbyId);
				if (success)
				{
					UIManager.Instance.TransitionTo(GameScreen.LobbyRoom);
				}
				else
				{
					UIManager.Instance.PlayWarningSound();
					panel.MouseFilter = MouseFilterEnum.Stop;
				}
			}
		};

		return panel;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (LobbyManager.Instance != null)
			{
				LobbyManager.Instance.NatTestCompleted -= UpdateHostButtonState;
			}
			_httpClient.Dispose();
		}
		base.Dispose(disposing);
	}
}
