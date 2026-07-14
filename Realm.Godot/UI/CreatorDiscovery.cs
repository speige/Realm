using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

public partial class CreatorDiscovery : Control
{
	private Button _backButton;
	private TabContainer _tabContainer;
	private GridContainer _forYouGrid;
	private OptionButton _creatorDropdown;
	private GridContainer _portfolioGrid;
	private RichTextLabel _contactInfo;

	public override void _Ready()
	{
		var bgPanel = new Panel();
		bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
		bgPanel.AddThemeStyleboxOverride("panel", style);
		AddChild(bgPanel);

		_backButton = new Button();
		_backButton.Text = "◀ BACK";
		_backButton.Position = new Vector2(20, 20);
		_backButton.CustomMinimumSize = new Vector2(100, 40);
		_backButton.Pressed += () => UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		AddChild(_backButton);

		var titleLabel = new Label();
		titleLabel.Text = "Creator Discovery";
		titleLabel.Position = new Vector2(140, 20);
		titleLabel.AddThemeFontSizeOverride("font_size", 32);
		AddChild(titleLabel);

		_tabContainer = new TabContainer();
		_tabContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		_tabContainer.OffsetTop = 80;
		_tabContainer.OffsetBottom = -20;
		_tabContainer.OffsetLeft = 20;
		_tabContainer.OffsetRight = -20;
		AddChild(_tabContainer);

		var forYouTab = new Control();
		forYouTab.Name = "For You";
		_tabContainer.AddChild(forYouTab);

		var forYouVBox = new VBoxContainer();
		forYouVBox.SetAnchorsPreset(LayoutPreset.FullRect);
		forYouVBox.AddThemeConstantOverride("separation", 20);
		forYouVBox.OffsetTop = 20;
		forYouVBox.OffsetLeft = 20;
		forYouTab.AddChild(forYouVBox);
		
		var forYouLabel = new Label();
		forYouLabel.Text = "Based on your playtime, here are the top creators who contributed to the maps you love!";
		forYouVBox.AddChild(forYouLabel);

		_forYouGrid = new GridContainer();
		_forYouGrid.Columns = 2;
		_forYouGrid.AddThemeConstantOverride("h_separation", 40);
		_forYouGrid.AddThemeConstantOverride("v_separation", 20);
		forYouVBox.AddChild(_forYouGrid);

		var portfolioTab = new Control();
		portfolioTab.Name = "Portfolio Browser";
		_tabContainer.AddChild(portfolioTab);
		
		var portVBox = new VBoxContainer();
		portVBox.SetAnchorsPreset(LayoutPreset.FullRect);
		portVBox.AddThemeConstantOverride("separation", 20);
		portVBox.OffsetTop = 20;
		portVBox.OffsetLeft = 20;
		portfolioTab.AddChild(portVBox);
		
		var portHBox = new HBoxContainer();
		portHBox.AddThemeConstantOverride("separation", 20);
		portVBox.AddChild(portHBox);
		
		var selectLbl = new Label();
		selectLbl.Text = "Select Creator:";
		portHBox.AddChild(selectLbl);
		
		_creatorDropdown = new OptionButton();
		_creatorDropdown.CustomMinimumSize = new Vector2(200, 30);
		portHBox.AddChild(_creatorDropdown);
		
		_contactInfo = new RichTextLabel();
		_contactInfo.BbcodeEnabled = true;
		_contactInfo.CustomMinimumSize = new Vector2(400, 80);
		portVBox.AddChild(_contactInfo);
		
		_portfolioGrid = new GridContainer();
		_portfolioGrid.Columns = 3;
		_portfolioGrid.AddThemeConstantOverride("h_separation", 20);
		_portfolioGrid.AddThemeConstantOverride("v_separation", 20);
		portVBox.AddChild(_portfolioGrid);

		LoadForYouData();
		LoadPortfolioData();
	}

	private void LoadForYouData()
	{
		string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
		var localCreators = new Dictionary<string, int>();
		if (System.IO.File.Exists(statsPath))
		{
			try
			{
				string statsJson = System.IO.File.ReadAllText(statsPath);
				localCreators = JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson) ?? localCreators;
			}
			catch {}
		}

		if (localCreators.Count == 0)
		{
			localCreators = new Dictionary<string, int> {
				{ "Realm Builder", 120 },
				{ "Ice Mage", 45 },
				{ "Pathfinder", 30 }
			};
		}

		foreach (Node child in _forYouGrid.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var kvp in localCreators)
		{
			var nameLbl = new Label();
			nameLbl.Text = $"Creator: {kvp.Key}";
			_forYouGrid.AddChild(nameLbl);
			
			var ptLbl = new Label();
			ptLbl.Text = $"{kvp.Value} mins played on their maps/assets";
			_forYouGrid.AddChild(ptLbl);
		}
	}

	private struct CreatorInfo
	{
		public string PublicKey { get; set; }
		public string Username { get; set; }
		public string DonationLink { get; set; }
		public string ContactInfo { get; set; }
	}

	private struct PortfolioAsset
	{
		public string Hash { get; set; }
		public string AuthorUsername { get; set; }
	}

	private List<CreatorInfo> _creatorsList = new();

	private async void LoadPortfolioData()
	{
		string seedServerUrl = GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : "http://localhost:5000";
		try
		{
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				var res = await httpClient.GetAsync(seedServerUrl + "/api/creators");
				if (res.IsSuccessStatusCode)
				{
					string json = await res.Content.ReadAsStringAsync();
					var creators = JsonSerializer.Deserialize<CreatorInfo[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
					if (creators != null && creators.Length > 0)
					{
						_creatorsList = creators.ToList();
						_creatorDropdown.Clear();
						for (int i = 0; i < _creatorsList.Count; i++)
						{
							_creatorDropdown.AddItem(_creatorsList[i].Username, i);
						}
						_creatorDropdown.ItemSelected += (idx) => UpdatePortfolioView(idx);
						UpdatePortfolioView(0);
						return;
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CreatorDiscovery] Failed to load creators: {ex.Message}");
		}

		// Fallback to static mock if server call fails
		_creatorDropdown.Clear();
		_creatorDropdown.AddItem("Realm Builder", 0);
		_creatorDropdown.AddItem("Ice Mage", 1);
		_creatorDropdown.ItemSelected += (idx) => UpdatePortfolioViewFallback(idx);
		UpdatePortfolioViewFallback(0);
	}

	private async void UpdatePortfolioView(long index)
	{
		int idx = (int)index;
		if (idx < 0 || idx >= _creatorsList.Count) return;
		var creator = _creatorsList[idx];

		string contactText = "";
		if (!string.IsNullOrEmpty(creator.DonationLink)) contactText += $"Donation Link: [color=cyan]{creator.DonationLink}[/color]\n";
		if (!string.IsNullOrEmpty(creator.ContactInfo)) contactText += $"Contact Info: [color=cyan]{creator.ContactInfo}[/color]";
		if (string.IsNullOrEmpty(contactText)) contactText = "No contact/donation links provided.";
		_contactInfo.Text = contactText;

		foreach (Node n in _portfolioGrid.GetChildren())
		{
			n.QueueFree();
		}

		string seedServerUrl = GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : "http://localhost:5000";
		try
		{
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				var res = await httpClient.GetAsync(seedServerUrl + $"/api/creators/{Uri.EscapeDataString(creator.PublicKey)}/portfolio");
				if (res.IsSuccessStatusCode)
				{
					string json = await res.Content.ReadAsStringAsync();
					var assets = JsonSerializer.Deserialize<PortfolioAsset[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
					if (assets != null && assets.Length > 0)
					{
						foreach (var asset in assets)
						{
							var p = new PanelContainer();
							p.CustomMinimumSize = new Vector2(200, 150);
							var vbox = new VBoxContainer();
							vbox.Alignment = BoxContainer.AlignmentMode.Center;
							p.AddChild(vbox);

							var l = new Label();
							l.Text = "Greenlit Asset Pack";
							l.HorizontalAlignment = HorizontalAlignment.Center;
							vbox.AddChild(l);

							var h = new Label();
							h.Text = asset.Hash.Substring(0, Math.Min(12, asset.Hash.Length)) + "...";
							h.HorizontalAlignment = HorizontalAlignment.Center;
							h.AddThemeFontSizeOverride("font_size", 10);
							h.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
							vbox.AddChild(h);

							_portfolioGrid.AddChild(p);
						}
						return;
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CreatorDiscovery] Failed to load portfolio: {ex.Message}");
		}

		// Fallback label
		var emptyLbl = new Label();
		emptyLbl.Text = "No greenlit assets found in portfolio.";
		_portfolioGrid.AddChild(emptyLbl);
	}

	private void UpdatePortfolioViewFallback(long idx)
	{
		_contactInfo.Text = idx == 0 ? "Support me on Patreon: [color=cyan]patreon.com/realmbuilder[/color]" : "Donate to my Ko-fi: [color=cyan]ko-fi.com/icemage[/color]";
		
		foreach (Node n in _portfolioGrid.GetChildren())
		{
			n.QueueFree();
		}
		
		var numAssets = idx == 0 ? 3 : 1;
		for (int i = 0; i < numAssets; i++)
		{
			var p = new PanelContainer();
			p.CustomMinimumSize = new Vector2(200, 150);
			var l = new Label();
			l.Text = $"Greenlit Asset Pack {i+1}";
			l.HorizontalAlignment = HorizontalAlignment.Center;
			l.VerticalAlignment = VerticalAlignment.Center;
			p.AddChild(l);
			_portfolioGrid.AddChild(p);
		}
	}
}
