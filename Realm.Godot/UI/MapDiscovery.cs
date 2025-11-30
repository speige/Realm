using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MapDiscovery : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _filterPanel;
	private PanelContainer _mapListPanel;

	private Button _backButton;
	private Label _discoveryTitle;
	private LineEdit _searchBar;
	private GridContainer _mapGrid;

	// Category buttons
	private Button _btnAll;
	private Button _btnFeatured;
	private Button _btnTD;
	private Button _btnMelee;
	private Button _btnCampaign;

	private MapData[] _allMaps;
	private string _selectedCategory = "All";
	private string _searchQuery = "";

	public override void _Ready()
	{
		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_mapListPanel = GetNode<PanelContainer>("MapListPanel");
		
		_backButton = GetNode<Button>("BackButton");
		_discoveryTitle = GetNode<Label>("DiscoveryTitle");
		
		_searchBar = GetNode<LineEdit>("FilterPanel/VBoxContainer/SearchBar");
		_mapGrid = GetNode<GridContainer>("MapListPanel/VBoxContainer/ScrollContainer/MapGrid");

		_btnAll = GetNode<Button>("FilterPanel/VBoxContainer/CatAll");
		_btnFeatured = GetNode<Button>("FilterPanel/VBoxContainer/CatFeatured");
		_btnTD = GetNode<Button>("FilterPanel/VBoxContainer/CatTD");
		_btnMelee = GetNode<Button>("FilterPanel/VBoxContainer/CatMelee");
		_btnCampaign = GetNode<Button>("FilterPanel/VBoxContainer/CatCampaign");

		// Get dummy data
		_allMaps = MapData.GetDummyMaps();

		ApplyStyles();
		RegisterEvents();
		RenderMapGrid();
	}

	private void ApplyStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_filterPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_mapListPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		UIStyle.ApplyTitle(_discoveryTitle, "MAP DISCOVERY", 36);

		SetupPillarButton(_backButton, "◀ BACK", () => UIManager.Instance.TransitionTo(GameScreen.MainMenu));

		_searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		_searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		_searchBar.PlaceholderText = "Search maps...";
		_searchBar.Alignment = HorizontalAlignment.Center;
		_searchBar.AddThemeFontSizeOverride("font_size", 14);

		SetupCategoryButton(_btnAll, "ALL MAPS", "All");
		SetupCategoryButton(_btnFeatured, "FEATURED", "Featured");
		SetupCategoryButton(_btnTD, "TOWER DEFENSE", "TD");
		SetupCategoryButton(_btnMelee, "MELEE", "Melee");
		SetupCategoryButton(_btnCampaign, "CAMPAIGN", "Campaign");
		
		UpdateButtonHighlight();
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

	private void SetupCategoryButton(Button button, string text, string categoryValue)
	{
		UIStyle.ApplyButtonText(button, text, 14);
		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		
		button.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			_selectedCategory = categoryValue;
			UpdateButtonHighlight();
			RenderMapGrid();
		};
		button.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void UpdateButtonHighlight()
	{
		HighlightButton(_btnAll, _selectedCategory == "All");
		HighlightButton(_btnFeatured, _selectedCategory == "Featured");
		HighlightButton(_btnTD, _selectedCategory == "TD");
		HighlightButton(_btnMelee, _selectedCategory == "Melee");
		HighlightButton(_btnCampaign, _selectedCategory == "Campaign");
	}

	private void HighlightButton(Button button, bool highlight)
	{
		if (highlight)
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}
		else
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		}
	}

	private void RegisterEvents()
	{
		_searchBar.TextChanged += (text) =>
		{
			_searchQuery = text.Trim();
			RenderMapGrid();
		};
	}

	private void RenderMapGrid()
	{
		// Clear existing children
		foreach (Node child in _mapGrid.GetChildren())
		{
			child.QueueFree();
		}

		// Filter
		var filtered = _allMaps.AsEnumerable();

		if (_selectedCategory != "All")
		{
			if (_selectedCategory == "Featured")
			{
				filtered = filtered.Where(m => m.RatingStars >= 4.5f);
			}
			else
			{
				filtered = filtered.Where(m => m.Genre.Contains(_selectedCategory) || m.Features.Contains(_selectedCategory) || m.MapId.Contains(_selectedCategory));
			}
		}

		if (!string.IsNullOrEmpty(_searchQuery))
		{
			filtered = filtered.Where(m => m.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) || 
			                               m.Creator.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
			                               m.Description.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
		}

		var list = filtered.ToList();

		if (list.Count == 0)
		{
			var noMapsLabel = new Label();
			noMapsLabel.Text = "No maps found matching criteria.";
			noMapsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			noMapsLabel.AddThemeFontSizeOverride("font_size", 16);
			_mapGrid.AddChild(noMapsLabel);
			return;
		}

		foreach (var map in list)
		{
			var card = CreateMapCard(map);
			_mapGrid.AddChild(card);
		}
	}

	private Control CreateMapCard(MapData map)
	{
		var card = new PanelContainer();
		card.CustomMinimumSize = new Vector2(580, 220);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 16);
		card.AddChild(hBox);

		// Thumbnail
		var thumbnail = new TextureRect();
		thumbnail.CustomMinimumSize = new Vector2(200, 150);
		thumbnail.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		thumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		thumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		if (FileAccess.FileExists(map.ThumbnailPath))
		{
			thumbnail.Texture = GD.Load<Texture2D>(map.ThumbnailPath);
		}
		else
		{
			thumbnail.Texture = GD.Load<Texture2D>("res://icon.svg");
		}
		hBox.AddChild(thumbnail);

		// Details container
		var vBox = new VBoxContainer();
		vBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.AddThemeConstantOverride("separation", 4);
		hBox.AddChild(vBox);

		// Title
		var title = new Label();
		title.Text = map.Title;
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.AddThemeFontSizeOverride("font_size", 20);
		vBox.AddChild(title);

		// Creator
		var creator = new Label();
		creator.Text = $"By: {map.Creator}  •  {map.Genre}";
		creator.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		creator.AddThemeFontSizeOverride("font_size", 12);
		vBox.AddChild(creator);

		// Rating Stars (Unicode stars)
		var ratingBox = new HBoxContainer();
		vBox.AddChild(ratingBox);
		int fullStars = (int)Math.Round(map.RatingStars);
		var starsLabel = new Label();
		string starsStr = "";
		for (int i = 0; i < 5; i++)
		{
			starsStr += i < fullStars ? "★" : "☆";
		}
		starsLabel.Text = $"{starsStr} ({map.RatingStars}/5)";
		starsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		starsLabel.AddThemeFontSizeOverride("font_size", 14);
		ratingBox.AddChild(starsLabel);

		// Description (trimmed)
		var desc = new Label();
		desc.Text = map.Description;
		desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc.MaxLinesVisible = 3;
		desc.SizeFlagsVertical = SizeFlags.ExpandFill;
		desc.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		desc.AddThemeFontSizeOverride("font_size", 12);
		vBox.AddChild(desc);

		// Footer Box for Action Button
		var footer = new HBoxContainer();
		vBox.AddChild(footer);

		var filler = new Control();
		filler.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		footer.AddChild(filler);

		var btnDetails = new Button();
		btnDetails.CustomMinimumSize = new Vector2(140, 36);
		UIStyle.ApplyButtonText(btnDetails, "DETAILS", 14);
		btnDetails.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnDetails.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnDetails.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnDetails.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionToMapDetails(map);
		};
		btnDetails.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		footer.AddChild(btnDetails);

		return card;
	}
}
