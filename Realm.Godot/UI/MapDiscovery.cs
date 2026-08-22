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

	private static readonly Font FontMedieval = GD.Load<Font>("res://Assets/UI/BlackwoodCastle.ttf");
	private static readonly Font FontCinzel = GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");
	private static readonly Font FontMedievalShadow = GD.Load<Font>("res://Assets/UI/BlackwoodCastle-Shadow.ttf");
	private static readonly Font FontOutfit = GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");
	private static readonly Font FontNorseBold = GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");

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

		_allMaps = MapData.GetDummyMaps();

		ApplyStyles();
		RegisterEvents();
		RenderMapGrid();
	}

	private void ApplyStyles()
	{
		Texture2D bgTexture = null;
		string[] bgPaths = new string[]
		{
			"res://Assets/UI/procedural_bg.png",
			"res://Assets/UI/map_discovery_bg_rpg_grey2.jpg",
			"res://Assets/UI/map_discovery_bg_fortress2.jpg"
		};

		foreach (var path in bgPaths)
		{
			if (ResourceLoader.Exists(path))
			{
				bgTexture = GD.Load<Texture2D>(path);
				if (bgTexture != null) break;
			}
		}

		if (bgTexture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = bgTexture;
			_bgPanel.AddThemeStyleboxOverride("panel", style);
			_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));

		var filterPanelStyle = new StyleBoxTexture();
		filterPanelStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_filter_panel.png");
		filterPanelStyle.TextureMarginLeft = 0;
		filterPanelStyle.TextureMarginRight = 0;
		filterPanelStyle.TextureMarginTop = 0;
		filterPanelStyle.TextureMarginBottom = 0;
		filterPanelStyle.ContentMarginLeft = 55;
		filterPanelStyle.ContentMarginRight = 55;
		filterPanelStyle.ContentMarginTop = 60;
		filterPanelStyle.ContentMarginBottom = 24;
		_filterPanel.AddThemeStyleboxOverride("panel", filterPanelStyle);
		_filterPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

		var lobbyPanelStyle = new StyleBoxTexture();
		lobbyPanelStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_main_panel.png");
		lobbyPanelStyle.TextureMarginLeft = 0;
		lobbyPanelStyle.TextureMarginRight = 0;
		lobbyPanelStyle.TextureMarginTop = 0;
		lobbyPanelStyle.TextureMarginBottom = 0;
		lobbyPanelStyle.ContentMarginLeft = 80;
		lobbyPanelStyle.ContentMarginRight = 80;
		lobbyPanelStyle.ContentMarginTop = 130;
		lobbyPanelStyle.ContentMarginBottom = 65;
		_mapListPanel.AddThemeStyleboxOverride("panel", lobbyPanelStyle);
		_mapListPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

		_discoveryTitle.Text = TranslationServer.Translate("MAP DISCOVERY");
		_discoveryTitle.AddThemeFontOverride("font", FontNorseBold);
		_discoveryTitle.AddThemeFontSizeOverride("font_size", 24);
		_discoveryTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_discoveryTitle.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		_discoveryTitle.AddThemeConstantOverride("outline_size", 8);
		_discoveryTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_discoveryTitle.VerticalAlignment = VerticalAlignment.Center;
		_discoveryTitle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());

		_discoveryTitle.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_discoveryTitle.CustomMinimumSize = new Vector2(300, 40);
		_discoveryTitle.Size = new Vector2(300, 40);
		float titleX = _mapListPanel.Position.X + 200.0f;
		_discoveryTitle.Position = new Vector2(titleX, _mapListPanel.Position.Y + 30.0f);
		_discoveryTitle.ZIndex = 1;
		MoveChild(_discoveryTitle, -1);

		_backButton.Text = TranslationServer.Translate("MAIN MENU");
		_backButton.AddThemeFontOverride("font", FontNorseBold);
		_backButton.AddThemeFontSizeOverride("font_size", 18);
		_backButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_backButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_backButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		_backButton.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		_backButton.Icon = null;

		_backButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_backButton.TextureFilter = CanvasItem.TextureFilterEnum.Linear;

		_backButton.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_backButton.CustomMinimumSize = new Vector2(190, 52);
		_backButton.Size = new Vector2(190, 52);
		_backButton.Position = new Vector2(1590, 50);
		_backButton.ZIndex = 1;

		_backButton.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		};
		_backButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();

		var searchStyle = new StyleBoxTexture();
		searchStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_search_bar.png");
		searchStyle.TextureMarginLeft = 0;
		searchStyle.TextureMarginRight = 0;
		searchStyle.TextureMarginTop = 0;
		searchStyle.TextureMarginBottom = 0;
		searchStyle.ContentMarginLeft = 24;
		searchStyle.ContentMarginRight = 24;
		searchStyle.ContentMarginTop = 0;
		searchStyle.ContentMarginBottom = 0;

		_searchBar.AddThemeStyleboxOverride("normal", searchStyle);
		_searchBar.AddThemeStyleboxOverride("focus", searchStyle);
		_searchBar.AddThemeFontOverride("font", FontNorseBold);
		_searchBar.AddThemeFontSizeOverride("font_size", 17);
		_searchBar.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.AddThemeColorOverride("font_placeholder_color", new Color(0.6f, 0.55f, 0.45f));
		_searchBar.AddThemeColorOverride("caret_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.PlaceholderText = TranslationServer.Translate("Search maps... ");
		_searchBar.Alignment = HorizontalAlignment.Left;
		_searchBar.RightIcon = null;
		_searchBar.CustomMinimumSize = new Vector2(0, 56);

		var container = GetNode<VBoxContainer>("FilterPanel/VBoxContainer");
		if (container != null && !container.HasNode("SearchLabel"))
		{
			var searchLabel = new Label();
			searchLabel.Name = "SearchLabel";
			searchLabel.Text = TranslationServer.Translate("Search");
			searchLabel.AddThemeFontOverride("font", FontNorseBold);
			searchLabel.AddThemeFontSizeOverride("font_size", 20);
			searchLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			searchLabel.HorizontalAlignment = HorizontalAlignment.Center;
			container.AddChild(searchLabel);
			container.MoveChild(searchLabel, 0);
		}

		SetupCategoryButton(_btnAll, "ALL MAPS", "All");
		SetupCategoryButton(_btnFeatured, "FEATURED", "Featured");
		SetupCategoryButton(_btnTD, "TOWER DEFENSE", "TD");
		SetupCategoryButton(_btnMelee, "MELEE", "Melee");
		SetupCategoryButton(_btnCampaign, "CAMPAIGN", "Campaign");

		UpdateButtonHighlight();
	}

	private void SetupCategoryButton(Button button, string text, string categoryValue)
	{
		button.Text = TranslationServer.Translate(text);
		button.AddThemeFontOverride("font", FontNorseBold);
		button.AddThemeFontSizeOverride("font_size", 17);
		button.CustomMinimumSize = new Vector2(0, 52);
		button.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
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
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		button.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
		if (highlight)
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
			
			var activeStyle = new StyleBoxTexture();
			activeStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_btn_normal_selected.png");
			activeStyle.TextureMarginLeft = 0;
			activeStyle.TextureMarginRight = 0;
			activeStyle.TextureMarginTop = 0;
			activeStyle.TextureMarginBottom = 0;
			activeStyle.ContentMarginLeft = 16;
			activeStyle.ContentMarginRight = 16;
			activeStyle.ContentMarginTop = 10;
			activeStyle.ContentMarginBottom = 10;
			
			var activeHover = (StyleBoxTexture)activeStyle.Duplicate();
			activeHover.ModulateColor = new Color(1.1f, 1.1f, 1.15f);
			
			var activePressed = (StyleBoxTexture)activeStyle.Duplicate();
			activePressed.ModulateColor = new Color(0.8f, 0.8f, 0.85f);
			
			button.AddThemeStyleboxOverride("normal", activeStyle);
			button.AddThemeStyleboxOverride("hover", activeHover);
			button.AddThemeStyleboxOverride("pressed", activePressed);
		}
		else
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
			
			var normalStyle = new StyleBoxTexture();
			normalStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_btn_normal.png");
			normalStyle.TextureMarginLeft = 0;
			normalStyle.TextureMarginRight = 0;
			normalStyle.TextureMarginTop = 0;
			normalStyle.TextureMarginBottom = 0;
			normalStyle.ContentMarginLeft = 16;
			normalStyle.ContentMarginRight = 16;
			normalStyle.ContentMarginTop = 10;
			normalStyle.ContentMarginBottom = 10;
			
			var hoverStyle = new StyleBoxTexture();
			hoverStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_btn_normal_hover.png");
			hoverStyle.TextureMarginLeft = 0;
			hoverStyle.TextureMarginRight = 0;
			hoverStyle.TextureMarginTop = 0;
			hoverStyle.TextureMarginBottom = 0;
			hoverStyle.ContentMarginLeft = 16;
			hoverStyle.ContentMarginRight = 16;
			hoverStyle.ContentMarginTop = 10;
			hoverStyle.ContentMarginBottom = 10;
			
			var pressedStyle = (StyleBoxTexture)hoverStyle.Duplicate();
			pressedStyle.ModulateColor = new Color(0.85f, 0.85f, 0.9f);
			
			button.AddThemeStyleboxOverride("normal", normalStyle);
			button.AddThemeStyleboxOverride("hover", hoverStyle);
			button.AddThemeStyleboxOverride("pressed", pressedStyle);
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
		foreach (Node child in _mapGrid.GetChildren())
		{
			child.QueueFree();
		}

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
			noMapsLabel.Text = TranslationServer.Translate("No maps found matching criteria.");
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
		card.CustomMinimumSize = new Vector2(560, 220);
		card.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
		var cardStyle = new StyleBoxTexture();
		cardStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_card_bg.png");
		cardStyle.TextureMarginLeft = 0;
		cardStyle.TextureMarginRight = 0;
		cardStyle.TextureMarginTop = 0;
		cardStyle.TextureMarginBottom = 0;
		cardStyle.ContentMarginLeft = 32;
		cardStyle.ContentMarginRight = 32;
		cardStyle.ContentMarginTop = 38;
		cardStyle.ContentMarginBottom = 26;
		card.AddThemeStyleboxOverride("panel", cardStyle);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 16);
		card.AddChild(hBox);

		var thumbnail = new TextureRect();
		thumbnail.CustomMinimumSize = new Vector2(150, 110);
		thumbnail.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		thumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		thumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		thumbnail.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		if (FileAccess.FileExists(map.ThumbnailPath))
		{
			thumbnail.Texture = GD.Load<Texture2D>(map.ThumbnailPath);
		}
		else
		{
			thumbnail.Texture = GD.Load<Texture2D>("res://icon.svg");
		}
		hBox.AddChild(thumbnail);

		var vBox = new VBoxContainer();
		vBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vBox.AddThemeConstantOverride("separation", 2);
		hBox.AddChild(vBox);

		var headerBox = new HBoxContainer();
		headerBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.AddChild(headerBox);

		var title = new Label();
		title.Text = map.Title;
		title.AddThemeFontOverride("font", FontNorseBold);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.AddThemeFontSizeOverride("font_size", 18);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		headerBox.AddChild(title);

		var ratingLabel = new Label();
		ratingLabel.Text = $"★ {map.RatingStars:F1}";
		ratingLabel.AddThemeFontOverride("font", FontNorseBold);
		ratingLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		ratingLabel.AddThemeFontSizeOverride("font_size", 15);
		headerBox.AddChild(ratingLabel);

		var creator = new Label();
		creator.Text = $"By: {map.Creator}  •  {map.Genre}";
		creator.AddThemeFontOverride("font", FontNorseBold);
		creator.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		creator.AddThemeFontSizeOverride("font_size", 12);
		vBox.AddChild(creator);

		var desc = new Label();
		desc.Text = map.Description;
		desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc.SizeFlagsVertical = SizeFlags.ExpandFill;
		desc.AddThemeFontOverride("font", FontNorseBold);
		desc.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.94f));
		desc.AddThemeFontSizeOverride("font_size", 12);
		vBox.AddChild(desc);

		var footer = new HBoxContainer();
		footer.Alignment = BoxContainer.AlignmentMode.End;
		vBox.AddChild(footer);

		var btnDetails = new Button();
		btnDetails.CustomMinimumSize = new Vector2(110, 32);
		btnDetails.Text = TranslationServer.Translate("DETAILS");
		btnDetails.AddThemeFontOverride("font", FontNorseBold);
		btnDetails.AddThemeFontSizeOverride("font_size", 14);
		btnDetails.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btnDetails.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btnDetails.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		btnDetails.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		btnDetails.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnDetails.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnDetails.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnDetails.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btnDetails.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

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
