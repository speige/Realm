using Godot;
using System;
using System.Collections.Generic;

public class MapEditorEntityPaletteController
{
	private readonly MapEditorHUD _hud;
	private readonly VBoxContainer _palettesVBox;
	private readonly Button _btnAddObject;

	private string _currentCategory = "Characters";
	private readonly List<string> _categoryFiles = new();
	private OptionButton _optCategoryItems;

	private Button _btnChars;
	private Button _btnBuilds;
	private Button _btnEnv;
	private Button _btnProps;
	private Button _btnDecals;

	public string CurrentCategory => _currentCategory;
	public List<string> CategoryFiles => _categoryFiles;
	public OptionButton OptCategoryItems => _optCategoryItems;

	public MapEditorEntityPaletteController(MapEditorHUD hud, VBoxContainer palettesVBox, Button btnAddObject)
	{
		_hud = hud;
		_palettesVBox = palettesVBox;
		_btnAddObject = btnAddObject;



		var categoryGrid = new GridContainer();
		categoryGrid.Columns = 2;
		categoryGrid.AddThemeConstantOverride("h_separation", 6);
		categoryGrid.AddThemeConstantOverride("v_separation", 6);
		_palettesVBox.AddChild(categoryGrid);
		_palettesVBox.MoveChild(categoryGrid, 0);

		_optCategoryItems = new OptionButton();
		_optCategoryItems.Name = "OptCategoryItems";
		_optCategoryItems.CustomMinimumSize = new Vector2(180, 30);
		_palettesVBox.AddChild(_optCategoryItems);
		_palettesVBox.MoveChild(_optCategoryItems, 1);

		_optCategoryItems.ItemSelected += (index) =>
		{
			SelectCategoryItem((int)index);
			TriggerAddObjectMode();
		};

		_btnChars = new Button();
		_btnChars.Set("icon_max_width", 0);
		SetupButton(_btnChars, "👤 Characters", () => SelectCategory("Characters"), 12, "Select Characters category");
		categoryGrid.AddChild(_btnChars);

		_btnBuilds = new Button();
		_btnBuilds.Set("icon_max_width", 0);
		SetupButton(_btnBuilds, "🏢 Buildings", () => SelectCategory("Buildings"), 12, "Select Buildings category");
		categoryGrid.AddChild(_btnBuilds);

		_btnEnv = new Button();
		_btnEnv.Set("icon_max_width", 0);
		SetupButton(_btnEnv, "🌳 Environment", () => SelectCategory("Environment"), 12, "Select Environment category");
		categoryGrid.AddChild(_btnEnv);

		_btnProps = new Button();
		_btnProps.Set("icon_max_width", 0);
		SetupButton(_btnProps, "📦 Props", () => SelectCategory("Props"), 12, "Select Props category");
		categoryGrid.AddChild(_btnProps);

		_btnDecals = new Button();
		_btnDecals.Set("icon_max_width", 0);
		SetupButton(_btnDecals, "🎨 Decals", () => SelectCategory("Decals"), 12, "Select Decals category");
		categoryGrid.AddChild(_btnDecals);

		SelectCategory("Characters", triggerAddObject: false);
	}

	private void SetupButton(Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		btn.Text = TranslationServer.Translate(text);
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = Control.FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}
		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	public void SelectCategory(string category, bool triggerAddObject = true)
	{
		_currentCategory = category;

		var activeStyle = new StyleBoxFlat();
		activeStyle.BgColor = new Color(0.15f, 0.45f, 0.7f, 0.8f);
		activeStyle.BorderColor = UIStyle.ColorCyanGlow;
		activeStyle.SetBorderWidthAll(2);
		activeStyle.CornerRadiusTopLeft = 4;
		activeStyle.CornerRadiusTopRight = 4;
		activeStyle.CornerRadiusBottomLeft = 4;
		activeStyle.CornerRadiusBottomRight = 4;

		_btnChars.RemoveThemeStyleboxOverride("normal");
		_btnBuilds.RemoveThemeStyleboxOverride("normal");
		_btnEnv.RemoveThemeStyleboxOverride("normal");
		_btnProps.RemoveThemeStyleboxOverride("normal");
		_btnDecals.RemoveThemeStyleboxOverride("normal");

		if (category == "Characters") _btnChars.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnChars.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Buildings") _btnBuilds.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnBuilds.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Environment") _btnEnv.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnEnv.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Props") _btnProps.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnProps.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Decals") _btnDecals.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnDecals.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		_categoryFiles.Clear();
		_optCategoryItems.Clear();

		// Scan temp workspace files and metadata.json for custom assets
		try
		{
			string wsPath = MapEditorHUD.TempWorkspaceGodotPath;
			string globalWs = Godot.ProjectSettings.GlobalizePath(wsPath);
			string metadataPath = System.IO.Path.Combine(globalWs, "metadata.json");

			if (System.IO.File.Exists(metadataPath))
			{
				string json = System.IO.File.ReadAllText(metadataPath);
				var rootNode = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
				if (rootNode != null && rootNode.ContainsKey("Assets") && rootNode["Assets"] is System.Text.Json.Nodes.JsonObject assets)
				{
					if (category == "Decals" && assets.ContainsKey("decals") && assets["decals"] is System.Text.Json.Nodes.JsonObject decalsObj)
					{
						foreach (var kvp in decalsObj)
						{
							string decalFile = kvp.Key;
							string relDecalPath = System.IO.Path.Combine("Assets", "decals", decalFile);
							if (!_categoryFiles.Contains(relDecalPath) && !_categoryFiles.Contains(decalFile))
							{
								if (System.IO.File.Exists(System.IO.Path.Combine(globalWs, relDecalPath)))
								{
									_categoryFiles.Add(relDecalPath);
								}
								else if (System.IO.File.Exists(System.IO.Path.Combine(globalWs, decalFile)))
								{
									_categoryFiles.Add(decalFile);
								}
							}
						}
					}
					else if (assets.ContainsKey("glb") && assets["glb"] is System.Text.Json.Nodes.JsonObject glbRoot)
					{
						string glbCatKey = category switch
						{
							"Characters" => "character",
							"Buildings" => "building",
							"Environment" => "environment",
							"Props" => "props",
							_ => ""
						};

						if (!string.IsNullOrEmpty(glbCatKey) && glbRoot.ContainsKey(glbCatKey) && glbRoot[glbCatKey] is System.Text.Json.Nodes.JsonObject catObj)
						{
							foreach (var kvp in catObj)
							{
								string modelFile = kvp.Key;
								string relModelPath = System.IO.Path.Combine("Assets", "models", glbCatKey, modelFile);
								if (!_categoryFiles.Contains(relModelPath) && !_categoryFiles.Contains(modelFile))
								{
									if (System.IO.File.Exists(System.IO.Path.Combine(globalWs, relModelPath)))
									{
										_categoryFiles.Add(relModelPath);
									}
									else if (System.IO.File.Exists(System.IO.Path.Combine(globalWs, modelFile)))
									{
										_categoryFiles.Add(modelFile);
									}
								}
							}
						}
					}
				}
			}
		}
		catch { }

		_categoryFiles.Sort();

		foreach (var file in _categoryFiles)
		{
			string cleanName = System.IO.Path.GetFileNameWithoutExtension(file).Replace("_", " ").ToUpper();
			_optCategoryItems.AddItem(TranslationServer.Translate(cleanName));
		}

		if (_optCategoryItems.ItemCount > 0)
		{
			_optCategoryItems.Selected = 0;
			SelectCategoryItem(0);
		}

		if (triggerAddObject)
		{
			TriggerAddObjectMode();
		}
	}

	public void SelectCategoryItem(int index)
	{
		if (index >= 0 && index < _categoryFiles.Count)
		{
			string selectedFile = _categoryFiles[index];
			string wsPath = MapEditorHUD.TempWorkspaceGodotPath;
			string globalWs = Godot.ProjectSettings.GlobalizePath(wsPath);

			string placeId = System.IO.Path.Combine(globalWs, selectedFile);
			if (!System.IO.File.Exists(placeId))
			{
				placeId = selectedFile;
			}

			if (GameHost.Instance != null)
			{
				GameHost.Instance.ActivePlaceId = placeId;
			}
		}
	}

	public void TriggerAddObjectMode()
	{
		if (GameHost.Instance == null) return;

		GameHost.EditorTool targetTool = GameHost.EditorTool.PlaceProp;
		if (_currentCategory == "Characters" || _currentCategory == "Buildings")
		{
			targetTool = GameHost.EditorTool.PlaceUnit;
		}
		else if (_currentCategory == "Decals")
		{
			targetTool = GameHost.EditorTool.PlaceDecal;
		}

		string placeId = "";
		int selectedIndex = _optCategoryItems != null ? _optCategoryItems.Selected : -1;
		if (selectedIndex >= 0 && selectedIndex < _categoryFiles.Count)
		{
			string selectedFile = _categoryFiles[selectedIndex];
			string wsPath = MapEditorHUD.TempWorkspaceGodotPath;
			string globalWs = Godot.ProjectSettings.GlobalizePath(wsPath);

			placeId = System.IO.Path.Combine(globalWs, selectedFile);
			if (!System.IO.File.Exists(placeId))
			{
				placeId = selectedFile;
			}
		}

		if (string.IsNullOrEmpty(placeId))
		{
			if (targetTool == GameHost.EditorTool.PlaceUnit) placeId = "adventurer.glb";
			else if (targetTool == GameHost.EditorTool.PlaceDecal) placeId = "acid_drip.png";
			else placeId = "wooden_box.glb";
		}

		_hud.TriggerToolSelection(targetTool, _btnAddObject, placeId);
	}

	public void SelectCategoryItemExternal(string category, string filename)
	{
		if (category != _currentCategory)
		{
			SelectCategory(category);
		}

		string searchName = filename;
		if (filename.StartsWith("res://") || filename.Contains('/') || filename.Contains('\\'))
		{
			searchName = System.IO.Path.GetFileName(filename);
		}

		int idx = _categoryFiles.FindIndex(f => System.IO.Path.GetFileName(f).Equals(searchName, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			_optCategoryItems.Selected = idx;
			SelectCategoryItem(idx);
			TriggerAddObjectMode();
		}
		else
		{
			GameHost.EditorTool targetTool = GameHost.EditorTool.PlaceProp;
			if (category == "Characters" || category == "Buildings")
			{
				targetTool = GameHost.EditorTool.PlaceUnit;
			}
			else if (category == "Decals")
			{
				targetTool = GameHost.EditorTool.PlaceDecal;
			}

			_hud.TriggerToolSelection(targetTool, _btnAddObject, filename);
		}
	}
}
