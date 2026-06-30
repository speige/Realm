using Godot;
using System;

public partial class MapDetails : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	
	private Button _backButton;
	

	private PanelContainer _headerPanel;
	private Label _titleLabel;
	private Label _creatorLabel;


	private TextureRect _texLeftPeek;
	private TextureRect _texCenter;
	private TextureRect _texRightPeek;
	private Button _btnLeftArrow;
	private Button _btnRightArrow;
	private HSlider _carouselSlider;


	private PanelContainer _descriptionPanel;
	private Label _descTitle;
	private Label _descText;


	private PanelContainer _featuresPanel;
	private Label _featuresTitle;
	private VBoxContainer _featuresList;


	private PanelContainer _statsPanel;
	private Label _statsTitle;
	
	private Label _ratingTitle;
	private HBoxContainer _starsRow1;
	private Label _votesRow1;
	private HBoxContainer _starsRow2;
	private Label _votesRow2;
	private HBoxContainer _starsRow3;
	private Label _votesRow3;

	private Label _awardsTitle;
	private HBoxContainer _awardsContainer;

	private Label _gameplayTitle;
	private VBoxContainer _gameplayStatsContainer;

	private Label _techTitle;
	private VBoxContainer _techInfoContainer;

	private Button _downloadButton;
	private Label _downloadSubtitle;

	private MapData _mapData;
	private int _carouselIndex = 0;
	private bool _isDownloading = false;
	private float _downloadProgress = 0.0f;

	public override void _Ready()
	{

		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		
		_backButton = GetNode<Button>("BackButton");


		_headerPanel = GetNode<PanelContainer>("HeaderPanel");
		_titleLabel = GetNode<Label>("HeaderPanel/VBoxContainer/TitleLabel");
		_creatorLabel = GetNode<Label>("HeaderPanel/VBoxContainer/CreatorLabel");


		_texLeftPeek = GetNode<TextureRect>("CarouselPanel/LeftPeek");
		_texCenter = GetNode<TextureRect>("CarouselPanel/CenterScreenshot");
		_texRightPeek = GetNode<TextureRect>("CarouselPanel/RightPeek");
		_btnLeftArrow = GetNode<Button>("CarouselPanel/LeftArrow");
		_btnRightArrow = GetNode<Button>("CarouselPanel/RightArrow");
		_carouselSlider = GetNode<HSlider>("CarouselPanel/CarouselSlider");


		_descriptionPanel = GetNode<PanelContainer>("DescriptionPanel");
		_descTitle = GetNode<Label>("DescriptionPanel/VBoxContainer/DescTitle");
		_descText = GetNode<Label>("DescriptionPanel/VBoxContainer/ScrollContainer/DescText");


		_featuresPanel = GetNode<PanelContainer>("FeaturesPanel");
		_featuresTitle = GetNode<Label>("FeaturesPanel/VBoxContainer/FeaturesTitle");
		_featuresList = GetNode<VBoxContainer>("FeaturesPanel/VBoxContainer/ScrollContainer/FeaturesList");


		_statsPanel = GetNode<PanelContainer>("StatsPanel");
		_statsTitle = GetNode<Label>("StatsPanel/VBoxContainer/StatsTitle");

		_ratingTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingTitle");
		_starsRow1 = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row1Stars");
		_votesRow1 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row1Votes");
		_starsRow2 = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row2Stars");
		_votesRow2 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row2Votes");
		_starsRow3 = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row3Stars");
		_votesRow3 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row3Votes");

		_awardsTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/AwardsTitle");
		_awardsContainer = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/AwardsContainer");

		_gameplayTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/GameplayTitle");
		_gameplayStatsContainer = GetNode<VBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/GameplayStatsContainer");

		_techTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/TechTitle");
		_techInfoContainer = GetNode<VBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/TechInfoContainer");

		_downloadButton = GetNode<Button>("StatsPanel/VBoxContainer/DownloadButton");
		_downloadSubtitle = GetNode<Label>("StatsPanel/VBoxContainer/DownloadSubtitle");

		ApplyStyles();
		RegisterEvents();
	}

	public override void _Process(double delta)
	{
		if (_isDownloading)
		{
			_downloadProgress += (float)delta * 40.0f; // Simulate 40% per second
			if (_downloadProgress >= 100.0f)
			{
				_downloadProgress = 100.0f;
				_isDownloading = false;
				_downloadButton.Disabled = false;
				UIStyle.ApplyButtonText(_downloadButton, "** PLAY MAP **", 18);
				_downloadSubtitle.Text = Tr("Ready to Play");
				_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			}
			else
			{
				UIStyle.ApplyButtonText(_downloadButton, $"Downloading... {(int)_downloadProgress}%", 16);
				_downloadSubtitle.Text = $"{((_downloadProgress / 100.0f) * double.Parse(_mapData.FileSize.Split(' ')[0])):F2} GB / {_mapData.FileSize}";
			}
		}
	}

	public void SetMapData(MapData mapData)
	{
		_mapData = mapData;
		if (_mapData == null) return;

		_carouselIndex = 0;
		_isDownloading = false;
		_downloadProgress = 0.0f;


		UIStyle.ApplyTitle(_titleLabel, $"MAP DETAILS: {_mapData.Title}", 28);
		_creatorLabel.Text = $"By: {_mapData.Creator}";


		_descText.Text = _mapData.Description;


		PopulateFeatures();


		if (_mapData.Screenshots != null && _mapData.Screenshots.Length > 0)
		{
			_carouselSlider.MaxValue = _mapData.Screenshots.Length - 1;
			_carouselSlider.Value = 0;
			UpdateCarousel();
		}


		PopulateRatings();


		PopulateAwards();


		PopulateGameplayStats();


		PopulateTechInfo();


		_downloadButton.Disabled = false;
		UIStyle.ApplyButtonText(_downloadButton, "** DOWNLOAD MAP **", 18);
		_downloadSubtitle.Text = $"File Size: {_mapData.FileSize}";
		_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
	}

	private void ApplyStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		
		_headerPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_descriptionPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_featuresPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_statsPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		SetupPillarButton(_backButton, "◀ BACK", () => UIManager.Instance.TransitionTo(GameScreen.MapDiscovery));


		_descTitle.Text = Tr("MAP DESCRIPTION");
		_descTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_descTitle.AddThemeFontSizeOverride("font_size", 16);

		_featuresTitle.Text = Tr("MAP FEATURES");
		_featuresTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_featuresTitle.AddThemeFontSizeOverride("font_size", 16);

		_statsTitle.Text = Tr("MAP INFO & STATS");
		_statsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_statsTitle.AddThemeFontSizeOverride("font_size", 20);

		_ratingTitle.Text = Tr("RATINGS & COMMUNITY");
		_ratingTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_ratingTitle.AddThemeFontSizeOverride("font_size", 14);

		_awardsTitle.Text = Tr("AWARDS");
		_awardsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_awardsTitle.AddThemeFontSizeOverride("font_size", 14);

		_gameplayTitle.Text = Tr("GAMEPLAY STATS");
		_gameplayTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_gameplayTitle.AddThemeFontSizeOverride("font_size", 14);

		_techTitle.Text = Tr("TECHNICAL INFO");
		_techTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_techTitle.AddThemeFontSizeOverride("font_size", 14);


		_downloadButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_downloadButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_downloadButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_downloadButton.AddThemeStyleboxOverride("disabled", UIStyle.CreateButtonPressed());
		_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_downloadSubtitle.AddThemeFontSizeOverride("font_size", 12);


		_texLeftPeek.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.7f);
		_texRightPeek.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.7f);

		_btnLeftArrow.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnLeftArrow.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnLeftArrow.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		UIStyle.ApplyButtonText(_btnLeftArrow, "◀", 18);

		_btnRightArrow.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnRightArrow.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnRightArrow.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		UIStyle.ApplyButtonText(_btnRightArrow, "▶", 18);

		_carouselSlider.AddThemeStyleboxOverride("slider", UIStyle.CreateSliderTrack());
		_carouselSlider.AddThemeStyleboxOverride("grabber_area", UIStyle.CreateSliderFill());
		_carouselSlider.AddThemeStyleboxOverride("grabber_area_highlight", UIStyle.CreateSliderFill());
	}

	private void SetupPillarButton(Button button, string text, Action onClick)
	{
		UIStyle.ApplyButtonText(button, text, 18);
		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			onClick?.Invoke();
		};
		button.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void RegisterEvents()
	{
		_btnLeftArrow.Pressed += () =>
		{
			if (_mapData?.Screenshots == null || _mapData.Screenshots.Length == 0) return;
			_carouselIndex = (_carouselIndex - 1 + _mapData.Screenshots.Length) % _mapData.Screenshots.Length;
			_carouselSlider.Value = _carouselIndex;
			UpdateCarousel();
			UIManager.Instance.PlayClickSound();
		};

		_btnRightArrow.Pressed += () =>
		{
			if (_mapData?.Screenshots == null || _mapData.Screenshots.Length == 0) return;
			_carouselIndex = (_carouselIndex + 1) % _mapData.Screenshots.Length;
			_carouselSlider.Value = _carouselIndex;
			UpdateCarousel();
			UIManager.Instance.PlayClickSound();
		};

		_carouselSlider.ValueChanged += (value) =>
		{
			_carouselIndex = (int)value;
			UpdateCarousel();
		};

		_downloadButton.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			if (_downloadButton.Text == "** PLAY MAP **")
			{
				GD.Print("Playing map: " + _mapData?.Title);

				UIManager.Instance.TransitionTo(GameScreen.MainMenu);
			}
			else
			{
				_isDownloading = true;
				_downloadProgress = 0.0f;
				_downloadButton.Disabled = true;
			}
		};
	}

	private void UpdateCarousel()
	{
		if (_mapData?.Screenshots == null || _mapData.Screenshots.Length == 0) return;

		int len = _mapData.Screenshots.Length;
		int leftIndex = (_carouselIndex - 1 + len) % len;
		int rightIndex = (_carouselIndex + 1) % len;

		_texCenter.Texture = GD.Load<Texture2D>(_mapData.Screenshots[_carouselIndex]);
		_texLeftPeek.Texture = GD.Load<Texture2D>(_mapData.Screenshots[leftIndex]);
		_texRightPeek.Texture = GD.Load<Texture2D>(_mapData.Screenshots[rightIndex]);
	}

	private void PopulateFeatures()
	{

		foreach (Node child in _featuresList.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData == null) return;


		string[] allPossibleFeatures = { "Custom Units", "Boss Waves", "Achievements", "Hardcore Mode" };

		foreach (var feat in allPossibleFeatures)
		{
			var cb = new CheckBox();
			cb.Text = feat;
			cb.ButtonPressed = Array.Exists(_mapData.Features, f => f == feat);
			cb.Disabled = true; // Read-only checkbox representation
			cb.FocusMode = FocusModeEnum.None;
			UIStyle.ApplyCheckboxStyle(cb);
			_featuresList.AddChild(cb);
		}
	}

	private void PopulateRatings()
	{

		FillStarsRow(_starsRow1, 5);
		_votesRow1.Text = $"{_mapData.Votes5Star}   {_mapData.AvgRating} Stars";
		_votesRow1.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow1.AddThemeFontSizeOverride("font_size", 12);

		FillStarsRow(_starsRow2, 3);
		_votesRow2.Text = $"{_mapData.Votes3Star}   3.0/5 Stars";
		_votesRow2.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow2.AddThemeFontSizeOverride("font_size", 12);

		FillStarsRow(_starsRow3, 1);
		_votesRow3.Text = $"{_mapData.Votes1Star}   1.0/5 Stars";
		_votesRow3.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow3.AddThemeFontSizeOverride("font_size", 12);
	}

	private void FillStarsRow(HBoxContainer container, int filledStars)
	{
		foreach (Node child in container.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < 5; i++)
		{
			var star = new Label();
			star.Text = i < filledStars ? "★" : "☆";
			star.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			star.AddThemeFontSizeOverride("font_size", 16);
			container.AddChild(star);
		}
	}

	private void PopulateAwards()
	{
		foreach (Node child in _awardsContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData?.Awards == null) return;

		foreach (var awardPath in _mapData.Awards)
		{
			var rect = new TextureRect();
			rect.CustomMinimumSize = new Vector2(36, 36);
			rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			rect.Texture = GD.Load<Texture2D>(awardPath);
			_awardsContainer.AddChild(rect);
		}
	}

	private void PopulateGameplayStats()
	{
		foreach (Node child in _gameplayStatsContainer.GetChildren())
		{
			child.QueueFree();
		}


		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/clock.png", $"AVG PLAYTIME: {_mapData.AvgPlaytime}");
		

		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/alliance_flag.png", $"PLAYER COUNT: {_mapData.PlayerCount}");


		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/victory_flag.png", $"COMPLETION RATE: {_mapData.CompletionRate}");
	}

	private void PopulateTechInfo()
	{
		foreach (Node child in _techInfoContainer.GetChildren())
		{
			child.QueueFree();
		}


		AddStatRow(_techInfoContainer, "res://Assets/UI/wood_logs.png", $"FILE SIZE: {_mapData.FileSize}");


		AddStatRow(_techInfoContainer, "res://Assets/UI/gold_g.png", $"ENGINE VERSION: {_mapData.EngineVersion}");


		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_shield.png", $"MAX PLAYERS: {_mapData.MaxPlayers}");


		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_axe.png", $"GENRE: {_mapData.Genre}");
	}

	private void AddStatRow(VBoxContainer container, string iconPath, string text)
	{
		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 10);
		container.AddChild(hBox);

		var icon = new TextureRect();
		icon.CustomMinimumSize = new Vector2(24, 24);
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.Texture = GD.Load<Texture2D>(iconPath);
		hBox.AddChild(icon);

		var label = new Label();
		label.Text = text;
		label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		label.AddThemeFontSizeOverride("font_size", 13);
		hBox.AddChild(label);
	}
}
