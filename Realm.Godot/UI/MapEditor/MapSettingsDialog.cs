using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class MapSettingsDialog : FloatingDialogBase
{
	private LineEdit _txtMapName;
	private OptionButton _optMapType;
	private GridContainer _tagsGrid;
	private readonly List<CheckBox> _activeTagCheckboxes = new();
	private OptionButton _optSkybox;
	private readonly List<string> _skyboxFiles = new();

	private Label _lblCamLeftVal;
	private Label _lblCamRightVal;
	private Label _lblCamTopVal;
	private Label _lblCamBottomVal;

	private Label _lblMapWidthVal;
	private Label _lblMapHeightVal;
	private Button _btnScaleMap;

	public int SelectedMapTypeIndex => _optMapType?.Selected ?? 0;

	public MapSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Settings"), new Vector2(460, 560))
	{
		SetFooterCloseOnly("CLOSE");
		BuildControls();
	}

	private void BuildControls()
	{
		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		BodyContainer.AddChild(scroll);

		var contentVBox = new VBoxContainer();
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentVBox.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(contentVBox);

		var namePanel = CreateSectionBox(contentVBox, "🏷️ " + TranslationServer.Translate("Map Name"));
		_txtMapName = new LineEdit();
		_txtMapName.PlaceholderText = TranslationServer.Translate("Enter map name...");
		_txtMapName.TextChanged += (txt) =>
		{
			SaveMapProperties();
			Hud?.UpdateMapNameHeader();
		};
		namePanel.AddChild(_txtMapName);

		var mapTypePanel = CreateSectionBox(contentVBox, TranslationServer.Translate("Map Type"));
		_optMapType = new OptionButton();
		_optMapType.FocusMode = FocusModeEnum.None;
		_optMapType.AddItem(TranslationServer.Translate("Arcade Custom Map"), 0);
		_optMapType.AddItem(TranslationServer.Translate("Asset Pack"), 1);
		_optMapType.ItemSelected += (idx) =>
		{
			RebuildTagsUI();
			SaveMapProperties();
		};
		mapTypePanel.AddChild(_optMapType);

		var tagsPanel = CreateSectionBox(contentVBox, TranslationServer.Translate("Map Tags & Category"));
		_tagsGrid = new GridContainer();
		_tagsGrid.Columns = 2;
		_tagsGrid.AddThemeConstantOverride("h_separation", 8);
		_tagsGrid.AddThemeConstantOverride("v_separation", 4);
		_tagsGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tagsPanel.AddChild(_tagsGrid);

		var skyboxPanel = CreateSectionBox(contentVBox, "🌅 " + TranslationServer.Translate("Skybox Environment"));
		_optSkybox = new OptionButton();
		_optSkybox.FocusMode = FocusModeEnum.None;
		_optSkybox.ItemSelected += (index) =>
		{
			int idx = (int)index;
			if (idx >= 0 && idx < _skyboxFiles.Count)
			{
				string selectedFile = _skyboxFiles[idx];
				string relPath = selectedFile.Contains("/") || selectedFile.Contains("\\")
					? selectedFile
					: $"Assets/skyboxes/{selectedFile}";
				GameHost.Instance?.SetSkyboxTexture(relPath);
			}
		};
		skyboxPanel.AddChild(_optSkybox);

		var camBoundsPanel = CreateSectionBox(contentVBox, TranslationServer.Translate("Camera Boundaries"));
		var camGrid = new GridContainer();
		camGrid.Columns = 3;
		camGrid.AddThemeConstantOverride("h_separation", 6);
		camGrid.AddThemeConstantOverride("v_separation", 4);
		camBoundsPanel.AddChild(camGrid);

		_lblCamLeftVal = CreateBadgeLabel();
		var btnLeftDec = CreateControlButton("\uf060", "Move Left boundary further left (West)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float minX = -GameHost.Instance.GroundTerrain.Width;
				GameHost.Instance.EditorCameraBoundsLeft = Mathf.Max(minX, GameHost.Instance.EditorCameraBoundsLeft - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		var btnLeftInc = CreateControlButton("\uf061", "Move Left boundary further right (East)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float maxX = GameHost.Instance.EditorCameraBoundsRight;
				GameHost.Instance.EditorCameraBoundsLeft = Mathf.Min(maxX, GameHost.Instance.EditorCameraBoundsLeft + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		camGrid.AddChild(_lblCamLeftVal);
		camGrid.AddChild(btnLeftDec);
		camGrid.AddChild(btnLeftInc);

		_lblCamRightVal = CreateBadgeLabel();
		var btnRightDec = CreateControlButton("\uf060", "Move Right boundary further left (West)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float minX = GameHost.Instance.EditorCameraBoundsLeft;
				GameHost.Instance.EditorCameraBoundsRight = Mathf.Max(minX, GameHost.Instance.EditorCameraBoundsRight - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		var btnRightInc = CreateControlButton("\uf061", "Move Right boundary further right (East)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float maxX = (float)GameHost.Instance.GroundTerrain.Width;
				GameHost.Instance.EditorCameraBoundsRight = Mathf.Min(maxX, GameHost.Instance.EditorCameraBoundsRight + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		camGrid.AddChild(_lblCamRightVal);
		camGrid.AddChild(btnRightDec);
		camGrid.AddChild(btnRightInc);

		_lblCamTopVal = CreateBadgeLabel();
		var btnTopDec = CreateControlButton("\uf060", "Move Top boundary further North (Up)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float minZ = -GameHost.Instance.GroundTerrain.Depth;
				GameHost.Instance.EditorCameraBoundsTop = Mathf.Max(minZ, GameHost.Instance.EditorCameraBoundsTop - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		var btnTopInc = CreateControlButton("\uf061", "Move Top boundary further South (Down)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float maxZ = GameHost.Instance.EditorCameraBoundsBottom;
				GameHost.Instance.EditorCameraBoundsTop = Mathf.Min(maxZ, GameHost.Instance.EditorCameraBoundsTop + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		camGrid.AddChild(_lblCamTopVal);
		camGrid.AddChild(btnTopDec);
		camGrid.AddChild(btnTopInc);

		_lblCamBottomVal = CreateBadgeLabel();
		var btnBottomDec = CreateControlButton("\uf060", "Move Bottom boundary further North (Up)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float minZ = GameHost.Instance.EditorCameraBoundsTop;
				GameHost.Instance.EditorCameraBoundsBottom = Mathf.Max(minZ, GameHost.Instance.EditorCameraBoundsBottom - 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		var btnBottomInc = CreateControlButton("\uf061", "Move Bottom boundary further South (Down)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				Hud?.EnsureCameraBoundsVisible();
				float maxZ = (float)GameHost.Instance.GroundTerrain.Depth;
				GameHost.Instance.EditorCameraBoundsBottom = Mathf.Min(maxZ, GameHost.Instance.EditorCameraBoundsBottom + 5.0f);
				GameHost.Instance.RebuildCameraBoundsOverlay();
				UpdateCameraBoundsUI();
			}
		});
		camGrid.AddChild(_lblCamBottomVal);
		camGrid.AddChild(btnBottomDec);
		camGrid.AddChild(btnBottomInc);

		var mapSizePanel = CreateSectionBox(contentVBox, TranslationServer.Translate("Map Dimensions"));
		var sizeGrid = new GridContainer();
		sizeGrid.Columns = 3;
		sizeGrid.AddThemeConstantOverride("h_separation", 6);
		sizeGrid.AddThemeConstantOverride("v_separation", 4);
		mapSizePanel.AddChild(sizeGrid);

		_lblMapWidthVal = CreateBadgeLabel();
		var btnWidthDec = CreateControlButton("\uf068", "Decrease map tile columns (West)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				int w = GameHost.Instance.GroundTerrain.Width;
				if (w > 32)
				{
					int targetW = Math.Max(32, (w % 32 == 0 ? w - 32 : (w / 32) * 32));
					GameHost.Instance.ResizeMapExternal(targetW, GameHost.Instance.GroundTerrain.Depth);
					UpdateCameraBoundsUI();
				}
			}
		});
		var btnWidthInc = CreateControlButton("\uf067", "Increase map tile columns (East)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				int w = GameHost.Instance.GroundTerrain.Width;
				if (w < 512)
				{
					int targetW = Math.Min(512, (w / 32 + 1) * 32);
					GameHost.Instance.ResizeMapExternal(targetW, GameHost.Instance.GroundTerrain.Depth);
					UpdateCameraBoundsUI();
				}
			}
		});
		sizeGrid.AddChild(_lblMapWidthVal);
		sizeGrid.AddChild(btnWidthDec);
		sizeGrid.AddChild(btnWidthInc);

		_lblMapHeightVal = CreateBadgeLabel();
		var btnHeightDec = CreateControlButton("\uf068", "Decrease map tile rows (North)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				int d = GameHost.Instance.GroundTerrain.Depth;
				if (d > 32)
				{
					int targetD = Math.Max(32, (d % 32 == 0 ? d - 32 : (d / 32) * 32));
					GameHost.Instance.ResizeMapExternal(GameHost.Instance.GroundTerrain.Width, targetD);
					UpdateCameraBoundsUI();
				}
			}
		});
		var btnHeightInc = CreateControlButton("\uf067", "Increase map tile rows (South)", () =>
		{
			if (GameHost.Instance?.GroundTerrain != null)
			{
				int d = GameHost.Instance.GroundTerrain.Depth;
				if (d < 512)
				{
					int targetD = Math.Min(512, (d / 32 + 1) * 32);
					GameHost.Instance.ResizeMapExternal(GameHost.Instance.GroundTerrain.Width, targetD);
					UpdateCameraBoundsUI();
				}
			}
		});
		sizeGrid.AddChild(_lblMapHeightVal);
		sizeGrid.AddChild(btnHeightDec);
		sizeGrid.AddChild(btnHeightInc);

		_btnScaleMap = new Button();
		_btnScaleMap.Set("icon_max_width", 0);
		_btnScaleMap.Text = "⚖ " + TranslationServer.Translate("SCALE MAP");
		_btnScaleMap.TooltipText = TranslationServer.Translate("Scale the entire map: stretches/shrinks terrain data and repositions all entities proportionally");
		_btnScaleMap.FocusMode = FocusModeEnum.None;
		_btnScaleMap.CustomMinimumSize = new Vector2(0, 26);
		_btnScaleMap.Pressed += () =>
		{
			if (GameHost.Instance?.GroundTerrain != null && Hud != null)
			{
				Hud.SetScaleDialogTargets(GameHost.Instance.GroundTerrain.Width, GameHost.Instance.GroundTerrain.Depth);
				Hud.OpenScaleMapDialog();
			}
		};
		mapSizePanel.AddChild(_btnScaleMap);

		RebuildTagsUI();
		RefreshSkyboxList();
	}

	private VBoxContainer CreateSectionBox(Control parent, string titleText)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(vbox);

		var lbl = new Label();
		lbl.Text = titleText;
		lbl.AddThemeFontSizeOverride("font_size", 12);
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lbl);

		return vbox;
	}

	private Label CreateBadgeLabel()
	{
		var lbl = new Label();
		lbl.AddThemeFontSizeOverride("font_size", 11);
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return lbl;
	}

	private Button CreateControlButton(string text, string tooltip, Action onClick)
	{
		var btn = new Button();
		btn.Set("icon_max_width", 0);
		btn.Text = text;
		btn.TooltipText = TranslationServer.Translate(tooltip);
		btn.FocusMode = FocusModeEnum.None;
		btn.CustomMinimumSize = new Vector2(32, 24);
		btn.AddThemeFontSizeOverride("font_size", 11);
		btn.Pressed += onClick;
		return btn;
	}

	public override void OpenDialog()
	{
		UpdateCameraBoundsUI();
		RefreshSkyboxList();
		LoadMapProperties();
		base.OpenDialog();
	}

	public void LoadMapProperties()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (File.Exists(metaPath))
		{
			try
			{
				var metaDoc = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject;
				if (metaDoc != null)
				{
					if (metaDoc.TryGetPropertyValue("Name", out var n) && n != null && _txtMapName != null)
					{
						_txtMapName.Text = n.ToString();
					}
					else if (metaDoc.TryGetPropertyValue("map_name", out var mn) && mn != null && _txtMapName != null)
					{
						_txtMapName.Text = mn.ToString();
					}
				}
			}
			catch { }
		}

		string mapJsonPath = Path.Combine(wsPath, "map.json");
		if (File.Exists(mapJsonPath))
		{
			try
			{
				string mapJsonContent = File.ReadAllText(mapJsonPath);
				var mapDoc = JsonNode.Parse(mapJsonContent) as JsonObject;
				if (mapDoc != null && mapDoc.ContainsKey("MapProperties"))
				{
					var props = mapDoc["MapProperties"] as JsonObject;
					if (props != null)
					{
						if (_txtMapName != null && string.IsNullOrEmpty(_txtMapName.Text) && props.ContainsKey("Name"))
						{
							_txtMapName.Text = props["Name"]?.GetValue<string>() ?? "";
						}

						if (_optMapType != null && props.ContainsKey("MapType"))
						{
							string mapType = props["MapType"]?.GetValue<string>() ?? "";
							_optMapType.Selected = (mapType == "Asset Pack") ? 1 : 0;
						}

						RebuildTagsUI();

						if (props.ContainsKey("Tags") && props["Tags"] is JsonArray tagsArr)
						{
							var activeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							foreach (var tagNode in tagsArr)
							{
								if (tagNode != null) activeTags.Add(tagNode.GetValue<string>());
							}

							foreach (var chk in _activeTagCheckboxes)
							{
								chk.ButtonPressed = activeTags.Contains(chk.Text);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to load map properties: {ex.Message}");
			}
		}
	}

	public void SaveMapProperties()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		// Save to metadata.json
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (File.Exists(metaPath) && _txtMapName != null)
		{
			try
			{
				var metaDoc = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject ?? new JsonObject();
				metaDoc["Name"] = _txtMapName.Text.Trim();
				MapJsonFormatter.SaveFormattedJson(metaPath, metaDoc);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to save metadata.json map name: {ex.Message}");
			}
		}

		// Save to map.json
		string mapJsonPath = Path.Combine(wsPath, "map.json");
		if (File.Exists(mapJsonPath))
		{
			try
			{
				string mapJsonContent = File.ReadAllText(mapJsonPath);
				var mapDoc = JsonNode.Parse(mapJsonContent) as JsonObject;
				if (mapDoc != null)
				{
					if (!mapDoc.ContainsKey("MapProperties")) mapDoc["MapProperties"] = new JsonObject();
					var props = mapDoc["MapProperties"] as JsonObject;
					if (props != null)
					{
						if (_txtMapName != null) props["Name"] = _txtMapName.Text.Trim();
						if (_optMapType != null) props["MapType"] = _optMapType.Selected == 0 ? "Arcade Custom Map" : "Asset Pack";
						var tagsArr = new JsonArray();
						foreach (var chk in _activeTagCheckboxes)
						{
							if (chk.ButtonPressed)
							{
								tagsArr.Add(chk.Text);
							}
						}
						props["Tags"] = tagsArr;
					}
					var options = new JsonSerializerOptions { WriteIndented = true };
					File.WriteAllText(mapJsonPath, mapDoc.ToJsonString(options));
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to save map properties: {ex.Message}");
			}
		}
	}

	public void RebuildTagsUI()
	{
		if (_tagsGrid == null) return;

		foreach (var child in _tagsGrid.GetChildren())
		{
			if (child is CheckBox chk)
			{
				_tagsGrid.RemoveChild(chk);
				chk.QueueFree();
			}
		}

		_activeTagCheckboxes.Clear();

		string[] tags;
		if (_optMapType != null && _optMapType.Selected == 1)
		{
			tags = new[] { "3D Models", "Audio", "Code Scripts", "Terrain PBR", "UI Components" };
		}
		else
		{
			tags = new[] { "Tower Defense", "Campaign", "Melee", "Coop / Survival", "Tutorial / Skirmish" };
		}

		foreach (var tag in tags)
		{
			var chk = new CheckBox();
			chk.Set("icon_max_width", 0);
			chk.Text = tag;
			chk.FocusMode = FocusModeEnum.None;
			chk.AddThemeFontSizeOverride("font_size", 11);
			chk.Toggled += (_) => SaveMapProperties();
			_tagsGrid.AddChild(chk);
			_activeTagCheckboxes.Add(chk);
		}
	}

	public void UpdateCameraBoundsUI()
	{
		if (GameHost.Instance == null) return;
		if (_lblCamLeftVal != null) _lblCamLeftVal.Text = $"L: {GameHost.Instance.EditorCameraBoundsLeft:F0}m";
		if (_lblCamRightVal != null) _lblCamRightVal.Text = $"R: {GameHost.Instance.EditorCameraBoundsRight:F0}m";
		if (_lblCamTopVal != null) _lblCamTopVal.Text = $"T: {GameHost.Instance.EditorCameraBoundsTop:F0}m";
		if (_lblCamBottomVal != null) _lblCamBottomVal.Text = $"B: {GameHost.Instance.EditorCameraBoundsBottom:F0}m";

		if (GameHost.Instance.GroundTerrain != null)
		{
			if (_lblMapWidthVal != null) _lblMapWidthVal.Text = $"W: {GameHost.Instance.GroundTerrain.Width}";
			if (_lblMapHeightVal != null) _lblMapHeightVal.Text = $"H: {GameHost.Instance.GroundTerrain.Depth}";
		}
	}

	public void RefreshSkyboxList()
	{
		if (_optSkybox == null) return;
		_skyboxFiles.Clear();
		_optSkybox.Clear();

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metadataPath = Path.Combine(wsPath, "metadata.json");

		if (File.Exists(metadataPath))
		{
			try
			{
				string text = File.ReadAllText(metadataPath);
				var root = JsonNode.Parse(text) as JsonObject;
				if (root != null)
				{
					JsonObject? skyboxesObj = null;
					if (root.ContainsKey("Assets") && root["Assets"] is JsonObject assets && assets.ContainsKey("skyboxes") && assets["skyboxes"] is JsonObject sObj1)
					{
						skyboxesObj = sObj1;
					}
					else if (root.ContainsKey("MapProperties") && root["MapProperties"] is JsonObject mp && mp.ContainsKey("Assets") && mp["Assets"] is JsonObject mpAssets && mpAssets.ContainsKey("skyboxes") && mpAssets["skyboxes"] is JsonObject sObj2)
					{
						skyboxesObj = sObj2;
					}
					else if (root.ContainsKey("skyboxes") && root["skyboxes"] is JsonObject sObj3)
					{
						skyboxesObj = sObj3;
					}

					if (skyboxesObj != null)
					{
						foreach (var kvp in skyboxesObj)
						{
							string filename = kvp.Key;
							if (!_skyboxFiles.Contains(filename))
							{
								_skyboxFiles.Add(filename);
								string cleanName = Path.GetFileNameWithoutExtension(filename).Replace("_", " ");
								cleanName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);
								_optSkybox.AddItem(TranslationServer.Translate(cleanName));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"RefreshSkyboxList error parsing metadata.json: {ex.Message}");
			}
		}

		if (_skyboxFiles.Count == 0)
		{
			_skyboxFiles.Add("skybox_panoramic.jpg");
			_optSkybox.AddItem(TranslationServer.Translate("Default Panoramic"));
		}
	}

	public void SelectSkybox(string relPath)
	{
		if (_optSkybox == null) return;
		string file = Path.GetFileName(relPath);
		int index = _skyboxFiles.IndexOf(file);
		if (index < 0)
		{
			RefreshSkyboxList();
			index = _skyboxFiles.IndexOf(file);
		}
		if (index >= 0)
		{
			_optSkybox.Selected = index;
		}
	}
}
