using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.Services.ModelOptimization;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using Realm.Shared.Audio;
using Realm.Shared.Metadata;

public partial class AssetManagerDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _simRoot;
	private Node3D _currentModelRoot;
	private AnimatedSprite3D _vfxSprite;

	private PanelContainer _preview2DContainer;
	private TextureRect _preview2DImage;
	private Label _lblPreview2DInfo;

	private PanelContainer _previewAudioContainer;
	private Label _lblAudioInfo;
	private Button _btnAudioPlay;
	private AudioStreamPlayer _audioPlayer;

	private HBoxContainer _ranimBaseModelRow;
	private LineEdit _txtRanimBaseModel;
	private Action<string> _setRanimBaseModelValue;
	private string _selectedRanimBaseModel = "";

	private HBoxContainer _cameraPresetRow;

	private OptionButton _optAssetCategory;
	private Label _lblModelTypeDescription;
	private Button _btnImportAsset;
	private Button _btnConvert3DModel;
	private Button _btnAiGenerate3D;
	private Button _btnConvertImage;
	private Button _btnConvertAudio;
	private Button _btnConvertMixamo;
	private Button _btnGenerateNoise;
	private Button _btnPruneUnused;
	private LineEdit _txtSearchFilter;
	private VBoxContainer _listVBox;

	private CustomShaderConfig _currentShaderConfig;
	private float _shaderPreviewTime = 0f;
	private bool _shaderPreviewForward = true;

	private SpritesheetAssetEditDialog _spritesheetEditDialog;
	private TerrainTextureEditDialog _textureEditDialog;
	private DecalSettingsDialog _decalEditDialog;
	private ChangeAssetTypeDialog _changeTypeDialog;
	private ShaderEditorDialog _shaderEditDialog;

	private string _currentCategory = "glb_units";
	private string _searchFilter = "";
	private string _currentPreviewAssetKey = "";
	private string _currentPreviewAssetCategory = "";

	private float _defaultDistance = 5.0f;
	private float _cameraDistance = 5.0f;
	private float _defaultYaw = Mathf.DegToRad(45.0f);
	private float _defaultPitch = Mathf.DegToRad(25.0f);
	private float _cameraYaw = Mathf.DegToRad(45.0f);
	private float _cameraPitch = Mathf.DegToRad(25.0f);
	private Vector3 _targetPosition = Vector3.Zero;

	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;

	public AssetManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Assets Manager & Importer"), new Vector2(700, 780))
	{
		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_changeTypeDialog = new ChangeAssetTypeDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);

		_audioPlayer = new AudioStreamPlayer();
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		// 1. TOP LIVE PREVIEW SECTION (3D / 2D / Audio)
		var previewStack = new PanelContainer();
		previewStack.CustomMinimumSize = new Vector2(0, 220);
		previewStack.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewStack);

		// 3D Viewport
		_viewportContainer = Add3DViewportContainer(previewStack, new Vector2(0, 220), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		Setup3DEnvironment();

		// 2D Static Preview
		_preview2DContainer = new PanelContainer();
		_preview2DContainer.CustomMinimumSize = new Vector2(0, 220);
		_preview2DContainer.Visible = false;
		var preview2DVBox = new VBoxContainer();
		preview2DVBox.Alignment = BoxContainer.AlignmentMode.Center;
		preview2DVBox.AddThemeConstantOverride("separation", 6);

		_preview2DImage = new TextureRect();
		_preview2DImage.CustomMinimumSize = new Vector2(180, 180);
		_preview2DImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_preview2DImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_preview2DImage.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_preview2DImage.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		preview2DVBox.AddChild(_preview2DImage);

		_lblPreview2DInfo = new Label();
		_lblPreview2DInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblPreview2DInfo.AddThemeFontSizeOverride("font_size", 10);
		_lblPreview2DInfo.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
		preview2DVBox.AddChild(_lblPreview2DInfo);

		_preview2DContainer.AddChild(preview2DVBox);
		previewStack.AddChild(_preview2DContainer);

		// Audio Preview
		_previewAudioContainer = new PanelContainer();
		_previewAudioContainer.CustomMinimumSize = new Vector2(0, 220);
		_previewAudioContainer.Visible = false;
		var audioVBox = new VBoxContainer();
		audioVBox.Alignment = BoxContainer.AlignmentMode.Center;
		audioVBox.AddThemeConstantOverride("separation", 10);

		var lblAudioIcon = new Label();
		lblAudioIcon.Text = "🔊";
		lblAudioIcon.HorizontalAlignment = HorizontalAlignment.Center;
		lblAudioIcon.AddThemeFontSizeOverride("font_size", 36);
		audioVBox.AddChild(lblAudioIcon);

		_lblAudioInfo = new Label();
		_lblAudioInfo.Text = TranslationServer.Translate("No audio loaded");
		_lblAudioInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblAudioInfo.AddThemeFontSizeOverride("font_size", 12);
		_lblAudioInfo.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		audioVBox.AddChild(_lblAudioInfo);

		var audioBtnRow = new HBoxContainer();
		audioBtnRow.Alignment = BoxContainer.AlignmentMode.Center;
		audioBtnRow.AddThemeConstantOverride("separation", 8);

		_btnAudioPlay = AddButton(audioBtnRow, "▶ " + TranslationServer.Translate("Play"), () => ToggleAudioPlayback(), "Play loaded audio", 11, new Vector2(80, 26));

		audioVBox.AddChild(audioBtnRow);
		_previewAudioContainer.AddChild(audioVBox);
		previewStack.AddChild(_previewAudioContainer);

		// 2. CAMERA PRESETS BAR
		_cameraPresetRow = new HBoxContainer();
		_cameraPresetRow.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Camera:");
		lblPreset.AddThemeFontSizeOverride("font_size", 10);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_cameraPresetRow.AddChild(lblPreset);

		AddButton(_cameraPresetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 15f), "Front view", 10, new Vector2(0, 22));
		AddButton(_cameraPresetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 15f), "Side view", 10, new Vector2(0, 22));
		AddButton(_cameraPresetRow, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 15f), "Back view", 10, new Vector2(0, 22));
		AddButton(_cameraPresetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric view", 10, new Vector2(0, 22));
		AddButton(_cameraPresetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(_cameraPresetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera", 10, new Vector2(0, 22));

		BodyContainer.AddChild(_cameraPresetRow);

		// 3. RANIM BASE MODEL DROPDOWN ROW (Visible for .ranim and shaders)
		_ranimBaseModelRow = new HBoxContainer();
		_ranimBaseModelRow.AddThemeConstantOverride("separation", 6);
		_ranimBaseModelRow.Visible = false;

		(_txtRanimBaseModel, _setRanimBaseModelValue) = AddAssetFilterDropdown(
			_ranimBaseModelRow,
			TranslationServer.Translate("Preview Mesh:"),
			_selectedRanimBaseModel,
			(all) => GetPreviewMeshModels(),
			(val) =>
			{
				_selectedRanimBaseModel = val ?? string.Empty;
				if ((_currentCategory == "animations" || _currentCategory == "shaders") && !string.IsNullOrEmpty(_currentPreviewAssetKey))
				{
					LoadPreviewForAsset(_currentCategory, _currentPreviewAssetKey);
				}
			},
			TranslationServer.Translate("Select GLB base mesh for preview..."),
			100f
		);
		BodyContainer.AddChild(_ranimBaseModelRow);

		// 4. CATEGORY DROPDOWN & ACTIONS BAR
		var catRow = new HBoxContainer();
		catRow.AddThemeConstantOverride("separation", 8);

		var lblCat = new Label();
		lblCat.Text = TranslationServer.Translate("Asset Type:");
		lblCat.AddThemeFontSizeOverride("font_size", 11);
		lblCat.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		catRow.AddChild(lblCat);

		_optAssetCategory = new OptionButton();
		_optAssetCategory.AddThemeFontSizeOverride("font_size", 11);
		_optAssetCategory.CustomMinimumSize = new Vector2(170, 26);
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (units)"), 0);
		_optAssetCategory.SetItemMetadata(0, "glb_units");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (buildings)"), 1);
		_optAssetCategory.SetItemMetadata(1, "glb_buildings");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (resources)"), 2);
		_optAssetCategory.SetItemMetadata(2, "glb_resources");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (props)"), 3);
		_optAssetCategory.SetItemMetadata(3, "glb_props");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (projectiles)"), 4);
		_optAssetCategory.SetItemMetadata(4, "glb_projectiles");
		_optAssetCategory.AddItem(TranslationServer.Translate("Object Attachments"), 5);
		_optAssetCategory.SetItemMetadata(5, "glb_attachments");
		_optAssetCategory.AddItem(TranslationServer.Translate("Terrain Textures"), 6);
		_optAssetCategory.SetItemMetadata(6, "textures");
		_optAssetCategory.AddItem(TranslationServer.Translate("VFX Spritesheets"), 7);
		_optAssetCategory.SetItemMetadata(7, "vfx_spritesheets");
		_optAssetCategory.AddItem(TranslationServer.Translate("Animations (.ranim)"), 8);
		_optAssetCategory.SetItemMetadata(8, "animations");
		_optAssetCategory.AddItem(TranslationServer.Translate("Sound Effects (SFX)"), 9);
		_optAssetCategory.SetItemMetadata(9, "sfx");
		_optAssetCategory.AddItem(TranslationServer.Translate("Music"), 10);
		_optAssetCategory.SetItemMetadata(10, "music");
		_optAssetCategory.AddItem(TranslationServer.Translate("Icons"), 11);
		_optAssetCategory.SetItemMetadata(11, "icons");
		_optAssetCategory.AddItem(TranslationServer.Translate("Decals"), 12);
		_optAssetCategory.SetItemMetadata(12, "decals");
		_optAssetCategory.AddItem(TranslationServer.Translate("Ribbon Textures"), 13);
		_optAssetCategory.SetItemMetadata(13, "ribbons");
		_optAssetCategory.AddItem(TranslationServer.Translate("Noise Textures"), 14);
		_optAssetCategory.SetItemMetadata(14, "noise_textures");
		_optAssetCategory.AddItem(TranslationServer.Translate("Skyboxes"), 15);
		_optAssetCategory.SetItemMetadata(15, "skyboxes");
		_optAssetCategory.AddItem(TranslationServer.Translate("Custom Shaders"), 16);
		_optAssetCategory.SetItemMetadata(16, "shaders");

		_optAssetCategory.ItemSelected += (idx) =>
		{
			string cat = _optAssetCategory.GetItemMetadata((int)idx).AsString();
			SetCurrentCategory(cat);
		};
		catRow.AddChild(_optAssetCategory);

		_btnImportAsset = AddButton(catRow, "📥 " + TranslationServer.Translate("Import Asset"), () => OpenImportFileDialog(), "Import a new asset for the selected category", 11, new Vector2(120, 26));
		_btnConvert3DModel = AddButton(catRow, "🔄 " + TranslationServer.Translate("Convert 3D Model to Realm Format"), () =>
		{
			IsGlbCategory(_currentCategory, out string glbSub);
			Hud?.OpenConvertGlbDialog(null, glbSub, (_) => RefreshAssetList());
		}, "Select a 3D model (.glb, .gltf, .fbx, .obj) to optimize with LODs and convert to Realm format", 11, new Vector2(230, 26));
		_btnConvert3DModel.Visible = false;

		_btnAiGenerate3D = new Button();
		_btnAiGenerate3D.Set("icon_max_width", 16);
		_btnAiGenerate3D.AddThemeConstantOverride("icon_max_width", 16);
		_btnAiGenerate3D.ExpandIcon = false;
		_btnAiGenerate3D.IconAlignment = HorizontalAlignment.Center;
		_btnAiGenerate3D.VerticalIconAlignment = VerticalAlignment.Center;
		if (ResourceLoader.Exists("res://Assets/UI/globe_icon.png"))
		{
			_btnAiGenerate3D.Icon = GD.Load<Texture2D>("res://Assets/UI/globe_icon.png");
		}
		else
		{
			_btnAiGenerate3D.Text = "🌐";
		}
		_btnAiGenerate3D.TooltipText = TranslationServer.Translate("Generate 3D Model with AI Online");
		_btnAiGenerate3D.CustomMinimumSize = new Vector2(26, 26);
		_btnAiGenerate3D.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_btnAiGenerate3D.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		_btnAiGenerate3D.FocusMode = FocusModeEnum.None;
		_btnAiGenerate3D.Pressed += () => OS.ShellOpen("https://3d.hunyuanglobal.com");
		catRow.AddChild(_btnAiGenerate3D);
		_btnAiGenerate3D.Visible = false;

		_btnConvertImage = AddButton(catRow, "🔄 " + TranslationServer.Translate("Convert Image to Realm format"), () => OnConvertImagePressed(), "Convert an image file (PNG, JPG, BMP, WEBP, DDS, SVG, etc.) to Realm format (RTEX)", 11, new Vector2(230, 26));
		_btnConvertImage.Visible = false;

		_btnConvertAudio = AddButton(catRow, "🔄 " + TranslationServer.Translate("Convert Audio to .ogg"), () => OnConvertAudioPressed(), "Convert an audio file (MP3, WAV, AIFF, FLAC, AAC, etc.) to .ogg format", 11, new Vector2(190, 26));
		_btnConvertAudio.Visible = false;

		_btnConvertMixamo = AddButton(catRow, "🔄 " + TranslationServer.Translate("Convert Mixamo FBX/GLB to .ranim"), () => OnConvertMixamoPressed(), "Select a Mixamo .fbx or .glb file from disk to extract and convert into .ranim animations", 11, new Vector2(230, 26));
		_btnConvertMixamo.Visible = false;

		_btnGenerateNoise = AddButton(catRow, "🎲 " + TranslationServer.Translate("Generate Noise"), () => Hud?.OpenNoiseTextureDialog((_) => RefreshAssetList()), "Create procedural noise texture on CPU using FastNoiseLite", 11, new Vector2(130, 26));
		_btnGenerateNoise.Visible = false;
		_btnPruneUnused = AddButton(catRow, "🧹 " + TranslationServer.Translate("Prune Unused"), () => PruneUnusedAssets(), "Remove assets not referenced anywhere in the map", 11, new Vector2(110, 26));

		BodyContainer.AddChild(catRow);

		_lblModelTypeDescription = new Label();
		_lblModelTypeDescription.AddThemeFontSizeOverride("font_size", 10);
		_lblModelTypeDescription.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
		_lblModelTypeDescription.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblModelTypeDescription.Visible = false;
		BodyContainer.AddChild(_lblModelTypeDescription);

		// 5. SEARCH FILTER INPUT
		var searchRow = new HBoxContainer();
		searchRow.AddThemeConstantOverride("separation", 6);

		var lblSearch = new Label();
		lblSearch.Text = "🔍 " + TranslationServer.Translate("Filter:");
		lblSearch.AddThemeFontSizeOverride("font_size", 11);
		searchRow.AddChild(lblSearch);

		_txtSearchFilter = new LineEdit();
		_txtSearchFilter.PlaceholderText = TranslationServer.Translate("Type to filter assets by name...");
		_txtSearchFilter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) =>
		{
			_searchFilter = text?.Trim() ?? string.Empty;
			RefreshAssetList();
		};
		searchRow.AddChild(_txtSearchFilter);

		BodyContainer.AddChild(searchRow);

		// 6. SCROLLABLE ASSET LIST GRID
		_listVBox = CreateScrollBody(340);
	}

	private void Setup3DEnvironment()
	{
		if (_subViewport == null) return;

		_simRoot = new Node3D();
		_subViewport.AddChild(_simRoot);

		_currentModelRoot = new Node3D();
		_simRoot.AddChild(_currentModelRoot);

		_vfxSprite = new AnimatedSprite3D
		{
			Position = Vector3.Zero,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Transparent = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
			Visible = false
		};
		_simRoot.AddChild(_vfxSprite);
	}

	private static bool IsGlbCategory(string category, out string subCategory)
	{
		if (!string.IsNullOrEmpty(category) && category.StartsWith("glb_", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = category.Substring(4).ToLowerInvariant();
			return true;
		}
		if (!string.IsNullOrEmpty(category) && category.Equals("glb", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = "props";
			return true;
		}
		subCategory = string.Empty;
		return false;
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		SetCurrentCategory(_currentCategory);
		ResetCameraDefault();
	}

	private void SetCurrentCategory(string category)
	{
		_currentCategory = category;

		if (_lblModelTypeDescription != null)
		{
			if (IsGlbCategory(_currentCategory, out string glbSub))
			{
				_lblModelTypeDescription.Visible = true;
				_lblModelTypeDescription.Text = glbSub switch
				{
					"units" => TranslationServer.Translate("Units: Controllable characters, heroes, monsters, and mobile entities."),
					"buildings" => TranslationServer.Translate("Buildings: Player bases, towers, barracks, and stationary structures."),
					"resources" => TranslationServer.Translate("Resources: Harvestable nodes, trees, gold mines, and gatherable objects."),
					"props" => TranslationServer.Translate("Props: Static environmental decorations, rocks, clutter, and obstacles."),
					"projectiles" => TranslationServer.Translate("Projectiles: Arrows, missiles, spell effects, and ballistic models."),
					_ => ""
				};
			}
			else
			{
				_lblModelTypeDescription.Visible = false;
				_lblModelTypeDescription.Text = "";
			}
		}

		bool isRanim = _currentCategory == "animations";
		bool isShader = _currentCategory == "shaders";
		bool isRtexCategory = category is "textures" or "vfx_spritesheets" or "decals" or "ribbons" or "ribbon_textures" or "noise_textures" or "skyboxes" or "icons";
		bool isAudioCategory = category is "sfx" or "music";

		if (_ranimBaseModelRow != null)
		{
			_ranimBaseModelRow.Visible = (isRanim || isShader);
			if (isRanim || isShader)
			{
				var models = GetPreviewMeshModels();
				if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !models.Contains(_selectedRanimBaseModel))
				{
					_selectedRanimBaseModel = models.Count > 0 ? models[0] : string.Empty;
					_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
				}
			}
		}

		if (_btnGenerateNoise != null)
		{
			_btnGenerateNoise.Visible = category == "noise_textures";
		}
		if (_btnConvert3DModel != null)
		{
			_btnConvert3DModel.Visible = IsGlbCategory(category, out _);
		}
		if (_btnAiGenerate3D != null)
		{
			_btnAiGenerate3D.Visible = IsGlbCategory(category, out _);
		}
		if (_btnConvertImage != null)
		{
			_btnConvertImage.Visible = isRtexCategory;
		}
		if (_btnConvertAudio != null)
		{
			_btnConvertAudio.Visible = isAudioCategory;
		}
		if (_btnConvertMixamo != null)
		{
			_btnConvertMixamo.Visible = isRanim;
		}
		if (_btnImportAsset != null)
		{
			_btnImportAsset.Text = category == "shaders"
				? "✨ " + TranslationServer.Translate("Create Shader")
				: "📥 " + TranslationServer.Translate("Import Asset");
		}

		RefreshAssetList();

		// Auto-preview first item if available
		var items = GetAssetsForCategory(_currentCategory);
		if (items.Count > 0)
		{
			LoadPreviewForAsset(_currentCategory, items[0].Key, items[0].SubCategory);
		}
		else
		{
			ClearPreview();
		}
	}

	public void RefreshAssetList()
	{
		if (_listVBox == null) return;

		foreach (Node child in _listVBox.GetChildren())
		{
			child.QueueFree();
		}

		var items = GetAssetsForCategory(_currentCategory);
		if (!string.IsNullOrEmpty(_searchFilter))
		{
			items = items.Where(i => i.Key.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		}

		if (items.Count == 0)
		{
			var lblEmpty = new Label();
			lblEmpty.Text = TranslationServer.Translate("No assets found for this category.");
			lblEmpty.AddThemeFontSizeOverride("font_size", 11);
			lblEmpty.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
			_listVBox.AddChild(lblEmpty);
			return;
		}

		foreach (var item in items)
		{
			var row = CreateAssetRow(item.Category, item.Key, item.SubCategory, item.ExtraData);
			_listVBox.AddChild(row);
		}
	}

	public void RefreshAssetListAndPreview(string preferredAssetKey)
	{
		if (_listVBox == null) return;

		foreach (Node child in _listVBox.GetChildren())
		{
			child.QueueFree();
		}

		var items = GetAssetsForCategory(_currentCategory);
		if (!string.IsNullOrEmpty(_searchFilter))
		{
			items = items.Where(i => i.Key.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		}

		if (items.Count == 0)
		{
			var lblEmpty = new Label();
			lblEmpty.Text = TranslationServer.Translate("No assets found for this category.");
			lblEmpty.AddThemeFontSizeOverride("font_size", 11);
			lblEmpty.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
			_listVBox.AddChild(lblEmpty);
			return;
		}

		string targetKey = string.IsNullOrEmpty(preferredAssetKey) ? null
			: System.IO.Path.GetFileName(preferredAssetKey);

		Control targetRow = null;
		foreach (var item in items)
		{
			var row = CreateAssetRow(item.Category, item.Key, item.SubCategory, item.ExtraData);
			_listVBox.AddChild(row);
			if (targetKey != null && (string.Equals(item.Key, targetKey, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(System.IO.Path.GetFileName(item.Key), targetKey, StringComparison.OrdinalIgnoreCase)))
			{
				targetRow = row;
			}
		}

		if (targetRow != null && _listVBox.GetParent() is ScrollContainer scroll)
		{
			scroll.EnsureControlVisible(targetRow);
		}

		// Auto-preview the newly imported asset, falling back to the first item
		var match = items.FirstOrDefault(i =>
			string.Equals(i.Key, targetKey, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(System.IO.Path.GetFileName(i.Key), targetKey, StringComparison.OrdinalIgnoreCase));

		var toPreview = match.Key != null ? match : (items.Count > 0 ? items[0] : default);
		if (toPreview.Key != null)
		{
			LoadPreviewForAsset(toPreview.Category, toPreview.Key, toPreview.SubCategory);
			if (toPreview.Category == "sfx" || toPreview.Category == "music")
			{
				PlayCurrentAudio();
			}
		}
	}

	private struct AssetItemInfo
	{
		public string Category;
		public string SubCategory;
		public string Key;
		public JsonNode ExtraData;
	}

	private string ResolveAssetType(string fileName, string subCategoryOrFolder, JsonNode? extraData)
	{
		if (extraData is JsonObject obj)
		{
			string? typeVal = obj["asset_type"]?.ToString()
				?? obj["AssetType"]?.ToString()
				?? obj["default_asset_type"]?.ToString()
				?? obj["type"]?.ToString();
			if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(fileName, typeVal, out string canonical, out _))
			{
				return canonical;
			}
		}

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string filePath = ResolveAssetFilePath(wsPath, fileName, subCategoryOrFolder);
		if (File.Exists(filePath))
		{
			string? embeddedType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(filePath);
			if (!string.IsNullOrEmpty(embeddedType))
			{
				return embeddedType;
			}
		}

		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		if (ext == ".glb")
		{
			return subCategoryOrFolder switch
			{
				"units" => "Character",
				"buildings" => "Building",
				"resources" => "Environment",
				"projectiles" => "Projectile",
				"props" => "Prop",
				"attachments" => "Attachment",
				_ => "Prop"
			};
		}
		else if (ext is ".rtex")
		{
			return subCategoryOrFolder switch
			{
				"textures" => "Tilesheet",
				"vfx_spritesheets" or "vfx" => "SpellSpritesheet",
				"icons" => "Icon",
				"decals" => "Decal",
				"ribbon_textures" or "ribbons" or "ribbon" => "Ribbon",
				"skyboxes" => "Skybox",
				"noise_textures" or "noise" => "Noise",
				_ => "Tilesheet"
			};
		}
		else if (ext == ".ranim")
		{
			return "Animation";
		}
		else if (ext == ".ogg")
		{
			return subCategoryOrFolder == "music" ? "Music" : "SoundEffect";
		}

		return string.Empty;
	}

	private string ResolveAssetFilePath(string wsPath, string fileName, string subCategoryOrFolder)
	{
		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		if (ext == ".glb")
		{
			string path = Path.Combine(wsPath, "Assets", "models", subCategoryOrFolder, fileName);
			if (File.Exists(path)) return path;
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, fileName);
				if (File.Exists(p)) return p;
			}
			return path;
		}
		else if (ext is ".rtex")
		{
			string sub = subCategoryOrFolder switch
			{
				"vfx_spritesheets" => "vfx",
				"ribbon_textures" or "ribbons" => "ribbons",
				"noise_textures" => "noise",
				_ => subCategoryOrFolder
			};
			string path = Path.Combine(wsPath, "Assets", sub, fileName);
			if (File.Exists(path)) return path;
			return path;
		}
		else if (ext == ".ranim")
		{
			return Path.Combine(wsPath, "Assets", "animations", fileName);
		}
		else if (ext == ".ogg")
		{
			string sub = subCategoryOrFolder == "music" ? "music" : "sfx";
			string path = Path.Combine(wsPath, "Assets", "audio", sub, fileName);
			if (File.Exists(path)) return path;
			string pathDirect = Path.Combine(wsPath, "Assets", sub, fileName);
			if (File.Exists(pathDirect)) return pathDirect;
			return path;
		}
		return Path.Combine(wsPath, "Assets", fileName);
	}

	private List<AssetItemInfo> GetAssetsForCategory(string category)
	{
		var result = new List<AssetItemInfo>();
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return result;

		try
		{
			string json = File.ReadAllText(metaPath);
			var root = JsonNode.Parse(json)?.AsObject();
			var assetsObj = root?["Assets"]?.AsObject();
			if (assetsObj == null) return result;

			string expectedAssetType = category switch
			{
				"glb_units" => "Character",
				"glb_buildings" => "Building",
				"glb_resources" => "Environment",
				"glb_props" => "Prop",
				"glb_projectiles" => "Projectile",
				"glb_attachments" => "Attachment",
				"textures" => "Tilesheet",
				"vfx_spritesheets" => "SpellSpritesheet",
				"animations" => "Animation",
				"sfx" => "SoundEffect",
				"music" => "Music",
				"icons" => "Icon",
				"decals" => "Decal",
				"ribbons" or "ribbon_textures" => "Ribbon",
				"noise_textures" => "Noise",
				"skyboxes" => "Skybox",
				_ => category
			};

			if (IsGlbCategory(category, out string glbSub))
			{
				var glbObj = assetsObj["glb"]?.AsObject();
				if (glbObj != null)
				{
					foreach (var subKvp in glbObj)
					{
						if (subKvp.Value is JsonObject subCatObj)
						{
							foreach (var model in subCatObj)
							{
								string resolvedType = ResolveAssetType(model.Key, subKvp.Key, model.Value);
								if (resolvedType.Equals(expectedAssetType, StringComparison.OrdinalIgnoreCase))
								{
									result.Add(new AssetItemInfo { Category = category, SubCategory = subKvp.Key, Key = model.Key, ExtraData = model.Value });
								}
							}
						}
					}
				}
			}
			else if (category == "glb")
			{
				var glbObj = assetsObj["glb"]?.AsObject();
				if (glbObj != null)
				{
					foreach (var sub in glbObj)
					{
						if (sub.Value is JsonObject subCatObj)
						{
							foreach (var model in subCatObj)
							{
								result.Add(new AssetItemInfo { Category = "glb", SubCategory = sub.Key, Key = model.Key, ExtraData = model.Value });
							}
						}
					}
				}
			}
			else if (category is "textures" or "vfx_spritesheets" or "icons" or "decals" or "ribbons" or "ribbon_textures" or "noise_textures" or "skyboxes")
			{
				foreach (var catName in new[] { "textures", "vfx_spritesheets", "icons", "decals", "ribbons", "ribbon_textures", "noise_textures", "skyboxes" })
				{
					if (assetsObj[catName] is JsonObject catObj)
					{
						foreach (var item in catObj)
						{
							string resolvedType = ResolveAssetType(item.Key, catName, item.Value);
							if (resolvedType.Equals(expectedAssetType, StringComparison.OrdinalIgnoreCase))
							{
								result.Add(new AssetItemInfo { Category = category, SubCategory = "", Key = item.Key, ExtraData = item.Value });
							}
						}
					}
				}
			}
			else if (category is "sfx" or "music")
			{
				foreach (var catName in new[] { "sfx", "music", "audio" })
				{
					if (assetsObj[catName] is JsonObject catObj)
					{
						foreach (var item in catObj)
						{
							string resolvedType = ResolveAssetType(item.Key, catName, item.Value);
							if (resolvedType.Equals(expectedAssetType, StringComparison.OrdinalIgnoreCase))
							{
								result.Add(new AssetItemInfo { Category = category, SubCategory = "", Key = item.Key, ExtraData = item.Value });
							}
						}
					}
				}
			}
			else if (category == "animations")
			{
				var animObj = assetsObj["animations"]?.AsObject();
				if (animObj != null)
				{
					foreach (var anim in animObj)
					{
						string resolvedType = ResolveAssetType(anim.Key, "animations", anim.Value);
						if (resolvedType.Equals("Animation", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(resolvedType))
						{
							result.Add(new AssetItemInfo { Category = "animations", SubCategory = "", Key = anim.Key, ExtraData = anim.Value });
						}
					}
				}
			}
			else if (category == "shaders")
			{
				var shaders = SpawnDeathShaderManager.LoadAllCustomShaders(wsPath);
				foreach (var kvp in shaders)
				{
					result.Add(new AssetItemInfo
					{
						Category = "shaders",
						SubCategory = "shaders",
						Key = kvp.Key,
						ExtraData = kvp.Value.ToJsonObject()
					});
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] GetAssetsForCategory error: {ex.Message}");
		}

		return result.OrderBy(r => r.Key).ToList();
	}

	private Control CreateAssetRow(string category, string key, string subCategory, JsonNode extraData)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 8);

		var lblName = new Label();
		lblName.Text = key;
		lblName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		lblName.AddThemeFontSizeOverride("font_size", 11);
		hBox.AddChild(lblName);

		// Action 1: Preview Button
		var btnPreview = new Button();
		btnPreview.Set("icon_max_width", 0);
		btnPreview.Text = "👁 " + TranslationServer.Translate("Preview");
		btnPreview.AddThemeFontSizeOverride("font_size", 10);
		btnPreview.FocusMode = FocusModeEnum.None;
		btnPreview.CustomMinimumSize = new Vector2(65, 22);
		btnPreview.Pressed += () => LoadPreviewForAsset(category, key, subCategory);
		hBox.AddChild(btnPreview);

		// Action 2: Play Button (Audio only)
		bool isAudio = category == "sfx" || category == "music" || key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
		if (isAudio)
		{
			var btnPlay = new Button();
			btnPlay.Set("icon_max_width", 0);
			btnPlay.Text = "▶";
			btnPlay.AddThemeFontSizeOverride("font_size", 11);
			btnPlay.FocusMode = FocusModeEnum.None;
			btnPlay.CustomMinimumSize = new Vector2(26, 22);
			btnPlay.TooltipText = TranslationServer.Translate("Play audio");
			btnPlay.Pressed += () =>
			{
				if (_audioPlayer != null && _audioPlayer.Playing && _currentPreviewAssetKey == key)
				{
					StopCurrentAudio();
				}
				else
				{
					LoadPreviewForAsset(category, key, subCategory);
					PlayCurrentAudio();
				}
			};
			hBox.AddChild(btnPlay);
		}

		// Action 3: Edit Button (Spritesheets, Textures, Decals, Shaders)
		bool hasEditDialog = category == "vfx_spritesheets" || category == "textures" || category == "decals" || category == "shaders" || (extraData is JsonObject edObj && edObj.ContainsKey("asset_type") && (edObj["asset_type"]?.ToString() == "Decal" || edObj["asset_type"]?.ToString() == "Shader"));
		if (hasEditDialog)
		{
			var btnEdit = new Button();
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "✏️";
			btnEdit.AddThemeFontSizeOverride("font_size", 11);
			btnEdit.FocusMode = FocusModeEnum.None;
			btnEdit.CustomMinimumSize = new Vector2(26, 22);
			btnEdit.TooltipText = TranslationServer.Translate("Edit asset parameters");
			btnEdit.Pressed += () => OpenEditSubDialog(category, key, extraData);
			hBox.AddChild(btnEdit);
		}

		// Action 4: Delete Button
		var btnDelete = new Button();
		btnDelete.Set("icon_max_width", 0);
		btnDelete.Text = "❌";
		btnDelete.AddThemeFontSizeOverride("font_size", 11);
		btnDelete.FocusMode = FocusModeEnum.None;
		btnDelete.CustomMinimumSize = new Vector2(26, 22);
		btnDelete.TooltipText = TranslationServer.Translate("Delete asset");
		btnDelete.Pressed += () => DeleteAsset(category, key, subCategory);
		hBox.AddChild(btnDelete);

		panel.AddChild(hBox);
		return panel;
	}

	private void LoadPreviewForAsset(string category, string key, string subCategory = "")
	{
		_currentPreviewAssetKey = key;
		_currentPreviewAssetCategory = category;

		// 1. Clear previous previews
		Clear3DModelPreview();
		StopCurrentAudio();

		if (IsGlbCategory(category, out string glbSub) || category == "animations" || category == "vfx_spritesheets" || category == "shaders")
		{
			_viewportContainer.Visible = true;
			_preview2DContainer.Visible = false;
			_previewAudioContainer.Visible = false;
			if (_cameraPresetRow != null) _cameraPresetRow.Visible = (IsGlbCategory(category, out _) || category == "animations" || category == "shaders");

			if (IsGlbCategory(category, out glbSub))
			{
				Load3DGlbModel(key, !string.IsNullOrEmpty(subCategory) ? subCategory : glbSub);
			}
			else if (category == "animations")
			{
				LoadRanimAnimation(key);
			}
			else if (category == "vfx_spritesheets")
			{
				LoadVfxSpritesheet(key);
			}
			else if (category == "shaders")
			{
				LoadCustomShaderPreview(key);
			}
		}
		else if (category == "sfx" || category == "music")
		{
			_viewportContainer.Visible = false;
			_preview2DContainer.Visible = false;
			_previewAudioContainer.Visible = true;
			if (_cameraPresetRow != null) _cameraPresetRow.Visible = false;

			_lblAudioInfo.Text = key;
			LoadAudioStream(key, category);
		}
		else
		{
			// 2D Static Images (textures, icons, decals, ribbon, noise, skyboxes)
			_viewportContainer.Visible = false;
			_preview2DContainer.Visible = true;
			_previewAudioContainer.Visible = false;
			if (_cameraPresetRow != null) _cameraPresetRow.Visible = false;

			LoadStatic2DTexture(key, category);
		}
	}

	private void ClearPreview()
	{
		_currentPreviewAssetKey = "";
		_currentShaderConfig = null;
		Clear3DModelPreview();
		StopCurrentAudio();
		if (_preview2DImage != null) _preview2DImage.Texture = null;
		if (_lblPreview2DInfo != null) _lblPreview2DInfo.Text = "";
		if (_cameraPresetRow != null) _cameraPresetRow.Visible = false;
	}

	private void Clear3DModelPreview()
	{
		_currentShaderConfig = null;
		if (_currentModelRoot != null)
		{
			foreach (Node child in _currentModelRoot.GetChildren())
			{
				child.QueueFree();
			}
		}
		if (_vfxSprite != null)
		{
			_vfxSprite.Visible = false;
			_vfxSprite.Stop();
			_vfxSprite.SpriteFrames = null;
		}
	}

	private void Load3DGlbModel(string key, string subCategory)
	{
		Clear3DModelPreview();
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string modelPath = Path.Combine(wsPath, "Assets", "models", subCategory ?? "props", key);
		if (!File.Exists(modelPath))
		{
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, key);
				if (File.Exists(p))
				{
					modelPath = p;
					break;
				}
			}
		}

		if (!File.Exists(modelPath)) return;

		var gltfDoc = new GltfDocument();
		var gltfState = new GltfState();
		var err = gltfDoc.AppendFromFile(modelPath, gltfState);
		if (err == Error.Ok)
		{
			var node = gltfDoc.GenerateScene(gltfState);
			if (node is Node3D node3D)
			{
				_currentModelRoot.AddChild(node3D);
				CenterAndFrameNode(node3D);
			}
		}
	}

	private void LoadRanimAnimation(string ranimKey)
	{
		Clear3DModelPreview();
		var rigged = GetRiggedGlbModels();
		if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !rigged.Contains(_selectedRanimBaseModel))
		{
			_selectedRanimBaseModel = rigged.Count > 0 ? rigged[0] : string.Empty;
			_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
		}

		if (string.IsNullOrEmpty(_selectedRanimBaseModel)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string modelPath = Path.Combine(wsPath, "Assets", "models", "units", _selectedRanimBaseModel);
		if (!File.Exists(modelPath))
		{
			foreach (var sub in new[] { "units", "buildings", "resources", "props" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, _selectedRanimBaseModel);
				if (File.Exists(p)) { modelPath = p; break; }
			}
		}

		if (!File.Exists(modelPath)) return;

		var gltfDoc = new GltfDocument();
		var gltfState = new GltfState();
		if (gltfDoc.AppendFromFile(modelPath, gltfState) == Error.Ok)
		{
			var node = gltfDoc.GenerateScene(gltfState);
			if (node is Node3D node3D)
			{
				_currentModelRoot.AddChild(node3D);
				CenterAndFrameNode(node3D);

				// Load and bind .ranim animation
				string animPath = Path.Combine(wsPath, "Assets", "animations", ranimKey);
				if (File.Exists(animPath))
				{
					try
					{
						var animData = AnimationRetargetingService.GetOrLoadRanimData(animPath);
						if (animData != null)
						{
							if (AnimationRetargetingService.RetargetAndBind(animData, node3D, "preview_loop", out _))
							{
								var animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(node3D);
								if (animPlayer != null && animPlayer.HasAnimation("preview_loop"))
								{
									var anim = animPlayer.GetAnimation("preview_loop");
									if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
									animPlayer.Play("preview_loop");
								}
							}
						}
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[AssetManagerDialog] LoadRanimAnimation error: {ex.Message}");
					}
				}
			}
		}
	}

	private static Texture2D? LoadTextureFromFileOrRtex(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

		try
		{
			Image? img = null;
			if (filePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				byte[] rtexBytes = File.ReadAllBytes(filePath);
				byte[]? layer0Bytes = Realm.Shared.Textures.RtexFile.IsRtexBytes(rtexBytes)
					? Realm.Shared.Textures.RtexFile.GetLayer(rtexBytes, 0)
					: rtexBytes;
				if (layer0Bytes != null && layer0Bytes.Length > 0)
				{
					img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
					if (img.LoadWebpFromBuffer(layer0Bytes) != Error.Ok)
					{
						if (img.LoadPngFromBuffer(layer0Bytes) != Error.Ok)
						{
							if (img.LoadJpgFromBuffer(layer0Bytes) != Error.Ok)
							{
								if (img.LoadTgaFromBuffer(layer0Bytes) != Error.Ok)
								{
									img.LoadBmpFromBuffer(layer0Bytes);
								}
							}
						}
					}
				}
			}
			else
			{
				img = Image.LoadFromFile(filePath);
			}

			if (img != null)
			{
				if (!img.HasMipmaps())
				{
					img.GenerateMipmaps();
				}
				return ImageTexture.CreateFromImage(img);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] Failed to load texture from '{filePath}': {ex.Message}");
		}

		return null;
	}

	private void LoadVfxSpritesheet(string key)
	{
		Clear3DModelPreview();
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string cleanPath = key.Replace("\\", "/").TrimStart('/');
		string fileName = Path.GetFileName(key);
		string cleanBase = Path.GetFileNameWithoutExtension(key);

		var candidatePaths = new List<string?>
		{
			Path.Combine(wsPath, "Assets", "vfx", fileName),
			Path.Combine(wsPath, "Assets", "vfx", key),
			Path.Combine(wsPath, "Assets", cleanPath),
		};

		if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			candidatePaths.Add(Path.Combine(wsPath, "Assets", "vfx", cleanBase + ".rtex"));
		}
		if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			candidatePaths.Add(Path.Combine(wsPath, "Assets", "vfx", cleanBase + ".png"));
		}

		Texture2D? texture = null;
		foreach (var candidate in candidatePaths)
		{
			if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
			{
				texture = LoadTextureFromFileOrRtex(candidate);
				if (texture != null) break;
			}
		}

		if (texture == null) return;

		int cols = 4;
		int rows = 4;
		string[] metaPaths = new[]
		{
			Path.Combine(wsPath, "metadata.json"),
		};

		foreach (var metaPath in metaPaths)
		{
			if (!string.IsNullOrWhiteSpace(metaPath) && File.Exists(metaPath))
			{
				try
				{
					var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
					var vfxSheets = (root?["Assets"]?["vfx_spritesheets"] ?? root?["MapProperties"]?["Assets"]?["vfx_spritesheets"])?.AsObject();
					if (vfxSheets != null)
					{
						JsonObject? sheetObj = null;
						if (vfxSheets.TryGetPropertyValue(fileName, out var s1) && s1 is JsonObject so1) sheetObj = so1;
						else if (vfxSheets.TryGetPropertyValue(key, out var s2) && s2 is JsonObject so2) sheetObj = so2;
						else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.rtex", out var s3) && s3 is JsonObject so3) sheetObj = so3;
						else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.png", out var s4) && s4 is JsonObject so4) sheetObj = so4;

						if (sheetObj != null)
						{
							if (sheetObj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int parsedCols) && parsedCols > 0)
								cols = parsedCols;
							if (sheetObj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int parsedRows) && parsedRows > 0)
								rows = parsedRows;
							break;
						}
					}
				}
				catch { }
			}
		}

		if (cols <= 0) cols = 1;
		if (rows <= 0) rows = 1;

		int totalFrames = cols * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed("play", 20.0f);

		int frameWidth = Math.Max(1, (int)texture.GetWidth() / cols);
		int frameHeight = Math.Max(1, (int)texture.GetHeight() / rows);

		for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
		{
			int col = frameIndex % cols;
			int row = frameIndex / cols;
			var atlasFrame = new AtlasTexture
			{
				Atlas = texture,
				Region = new Rect2(col * frameWidth, row * frameHeight, frameWidth, frameHeight)
			};
			frames.AddFrame("play", atlasFrame);
		}

		if (_vfxSprite != null)
		{
			_vfxSprite.SpriteFrames = frames;
			_vfxSprite.Animation = "play";
			_vfxSprite.PixelSize = 6.0f / frameWidth;
			_vfxSprite.Position = Vector3.Zero;
			_vfxSprite.Visible = true;
			_vfxSprite.Play("play");
		}

		_defaultDistance = 3.5f;
		_defaultYaw = 0f;
		_defaultPitch = 0f;
		_targetPosition = Vector3.Zero;
		ResetCameraDefault();
	}

	private void LoadCustomShaderPreview(string shaderKey)
	{
		Clear3DModelPreview();
		_currentShaderConfig = SpawnDeathShaderManager.GetShaderConfig(shaderKey);
		if (_currentShaderConfig == null) return;

		_shaderPreviewTime = 0f;
		_shaderPreviewForward = true;

		var models = GetPreviewMeshModels();
		if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !models.Contains(_selectedRanimBaseModel))
		{
			_selectedRanimBaseModel = models.Count > 0 ? models[0] : string.Empty;
			_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
		}

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string modelPath = null;
		if (!string.IsNullOrEmpty(_selectedRanimBaseModel))
		{
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "attachments" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, _selectedRanimBaseModel);
				if (File.Exists(p)) { modelPath = p; break; }
			}
		}

		if (modelPath != null && File.Exists(modelPath))
		{
			var gltfDoc = new GltfDocument();
			var gltfState = new GltfState();
			if (gltfDoc.AppendFromFile(modelPath, gltfState) == Error.Ok)
			{
				var node = gltfDoc.GenerateScene(gltfState);
				if (node is Node3D node3D)
				{
					_currentModelRoot.AddChild(node3D);
					CenterAndFrameNode(node3D);
					SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, 0.5f);
				}
			}
		}
		else
		{
			var meshInst = new MeshInstance3D
			{
				Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f },
				Position = new Vector3(0, 0.9f, 0)
			};
			var mat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.7f, 0.5f) };
			meshInst.MaterialOverride = mat;
			_currentModelRoot.AddChild(meshInst);
			CenterAndFrameNode(_currentModelRoot);
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, 0.5f);
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (_currentPreviewAssetCategory == "shaders" && _currentShaderConfig != null && _currentModelRoot != null && Visible)
		{
			float dur = _currentShaderConfig.Duration > 0.05f ? _currentShaderConfig.Duration : 1.5f;
			if (_shaderPreviewForward)
			{
				_shaderPreviewTime += (float)delta;
				if (_shaderPreviewTime >= dur)
				{
					_shaderPreviewTime = dur;
					_shaderPreviewForward = false;
				}
			}
			else
			{
				_shaderPreviewTime -= (float)delta;
				if (_shaderPreviewTime <= 0f)
				{
					_shaderPreviewTime = 0f;
					_shaderPreviewForward = true;
				}
			}
			float prog = Mathf.Clamp(_shaderPreviewTime / dur, 0f, 1f);
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, prog);
		}
	}

	private void LoadStatic2DTexture(string key, string category)
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string subFolder = category switch
		{
			"textures" => "textures",
			"icons" => "icons",
			"decals" => "decals",
			"ribbons" or "ribbon_textures" => "ribbons",
			"noise_textures" => "noise",
			"skyboxes" => "skyboxes",
			_ => "textures"
		};

		string filePath = Path.Combine(wsPath, "Assets", subFolder, key);
		if (!File.Exists(filePath)) return;

		Texture2D? tex = LoadTextureFromFileOrRtex(filePath);
		if (_preview2DImage != null) _preview2DImage.Texture = tex;
		if (_lblPreview2DInfo != null)
		{
			_lblPreview2DInfo.Text = tex != null
				? $"{key} ({tex.GetWidth()}x{tex.GetHeight()})"
				: key;
		}
	}

	private void LoadAudioStream(string key, string category)
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string sub = category == "music" ? "music" : "sfx";
		string audioPath = Path.Combine(wsPath, "Assets", "audio", sub, key);
		if (!File.Exists(audioPath))
		{
			audioPath = Path.Combine(wsPath, "Assets", sub, key);
		}

		if (File.Exists(audioPath) && _audioPlayer != null)
		{
			try
			{
				if (audioPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
				{
					_audioPlayer.Stream = AudioStreamOggVorbis.LoadFromFile(audioPath);
				}
				else
				{
					_audioPlayer.Stream = GD.Load<AudioStream>(audioPath);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetManagerDialog] LoadAudioStream error: {ex.Message}");
			}
		}
	}

	private void PlayCurrentAudio()
	{
		if (_audioPlayer != null && _audioPlayer.Stream != null)
		{
			_audioPlayer.Play();
			UpdateAudioPlayButtonState();
		}
	}

	private void StopCurrentAudio()
	{
		if (_audioPlayer != null && _audioPlayer.Playing)
		{
			_audioPlayer.Stop();
		}
		UpdateAudioPlayButtonState();
	}

	private void ToggleAudioPlayback()
	{
		if (_audioPlayer != null && _audioPlayer.Playing)
		{
			StopCurrentAudio();
		}
		else
		{
			PlayCurrentAudio();
		}
	}

	private void OnAudioFinished()
	{
		UpdateAudioPlayButtonState();
	}

	private void UpdateAudioPlayButtonState()
	{
		if (_btnAudioPlay != null)
		{
			bool isPlaying = _audioPlayer != null && _audioPlayer.Playing;
			_btnAudioPlay.Text = isPlaying ? "⏹ " + TranslationServer.Translate("Stop") : "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlay.TooltipText = isPlaying ? TranslationServer.Translate("Stop audio playback") : TranslationServer.Translate("Play loaded audio");
		}
	}

	private void CenterAndFrameNode(Node3D root)
	{
		Aabb aabb = new Aabb();
		bool hasAabb = false;

		void CalculateAabb(Node node)
		{
			if (node is VisualInstance3D visual)
			{
				Aabb itemAabb = visual.GetAabb();
				if (itemAabb.Size.LengthSquared() > 0.001f)
				{
					Transform3D localXform = root.GlobalTransform.AffineInverse() * visual.GlobalTransform;
					Aabb transformedAabb = localXform * itemAabb;
					aabb = hasAabb ? aabb.Merge(transformedAabb) : transformedAabb;
					hasAabb = true;
				}
			}
			foreach (Node child in node.GetChildren())
			{
				CalculateAabb(child);
			}
		}

		CalculateAabb(root);

		if (hasAabb)
		{
			Vector3 center = aabb.Position + aabb.Size * 0.5f;
			root.Position = -center;
			float maxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
			_defaultDistance = Mathf.Max(2.5f, maxDim * 2.2f);
		}
		else
		{
			root.Position = Vector3.Zero;
			_defaultDistance = 5.0f;
		}

		ResetCameraDefault();
	}

	private static readonly Dictionary<string, bool> _riggedModelCache = new(StringComparer.OrdinalIgnoreCase);

	private List<string> GetRiggedGlbModels()
	{
		var candidateModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// 1. Scan metadata.json from temp workspace and fallback template
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metaPath = Path.Combine(wsPath, "metadata.json");

		if (File.Exists(metaPath))
		{
			try
			{
				var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
				
				// CustomUnits
				var customUnits = root?["CustomUnits"]?.AsArray();
				if (customUnits != null)
				{
					foreach (var u in customUnits)
					{
						string mPath = u?["ModelPath"]?.ToString();
						if (!string.IsNullOrEmpty(mPath) && mPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
						{
							candidateModels.Add(Path.GetFileName(mPath));
						}
					}
				}

				// Assets.glb.units
				var unitsGlbObj = root?["Assets"]?["glb"]?["units"]?.AsObject();
				if (unitsGlbObj != null)
				{
					foreach (var model in unitsGlbObj)
					{
						if (!string.IsNullOrEmpty(model.Key))
						{
							candidateModels.Add(Path.GetFileName(model.Key));
						}
					}
				}
			}
			catch { }
		}

		// 2. Scan GameHost.UnitRegistry
		if (GameHost.UnitRegistry != null)
		{
			foreach (var kvp in GameHost.UnitRegistry)
			{
				if (!string.IsNullOrEmpty(kvp.Value.ModelPath) && kvp.Value.ModelPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
				{
					candidateModels.Add(Path.GetFileName(kvp.Value.ModelPath));
				}
			}
		}

		// 3. Scan filesystem directories for unit GLB models
		var unitDirs = new List<string>
		{
			Path.Combine(wsPath, "Assets", "models", "units"),
		};

		foreach (var dir in unitDirs)
		{
			if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
			{
				foreach (var file in Directory.GetFiles(dir, "*.glb"))
				{
					candidateModels.Add(Path.GetFileName(file));
				}
			}
		}

		// 4. Filter for rigged models (containing Skeleton3D)
		var riggedList = new List<string>();
		foreach (var modelFile in candidateModels)
		{
			if (_riggedModelCache.TryGetValue(modelFile, out bool isRigged))
			{
				if (isRigged) riggedList.Add(modelFile);
				continue;
			}

			bool hasSkeleton = false;
			try
			{
				var loaded = ModelCache.GetModel(modelFile);
				if (loaded is Node node)
				{
					hasSkeleton = SkeletonValidator.FindSkeleton(node) != null;
				}
				else
				{
					string resolvedPath = Path.Combine(wsPath, "Assets", "models", "units", modelFile);

					var doc = new GltfDocument();
					var state = new GltfState();
					if (doc.AppendFromFile(resolvedPath, state) == Error.Ok)
					{
						var scene = doc.GenerateScene(state);
						if (scene != null)
						{
							hasSkeleton = SkeletonValidator.FindSkeleton(scene) != null;
							scene.QueueFree();
						}
					}
				}
			}
			catch { }

			_riggedModelCache[modelFile] = hasSkeleton;
			if (hasSkeleton)
			{
				riggedList.Add(modelFile);
			}
		}

		if (riggedList.Count == 0 && candidateModels.Count > 0)
		{
			riggedList.AddRange(candidateModels);
		}

		riggedList.Sort(StringComparer.OrdinalIgnoreCase);
		return riggedList;
	}

	private List<string> GetPreviewMeshModels()
	{
		if (_currentCategory == "animations")
		{
			return GetRiggedGlbModels();
		}
		return GetAllGlbModels();
	}

	private List<string> GetAllGlbModels()
	{
		var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metaPath = Path.Combine(wsPath, "metadata.json");

		if (File.Exists(metaPath))
		{
			try
			{
				var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
				if (root?["Assets"]?["glb"] is JsonObject glbObj)
				{
					foreach (var sub in glbObj)
					{
						if (sub.Value is JsonObject subObj)
						{
							foreach (var model in subObj)
							{
								if (!string.IsNullOrEmpty(model.Key))
								{
									models.Add(Path.GetFileName(model.Key));
								}
							}
						}
					}
				}
			}
			catch { }
		}

		foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles" })
		{
			string dir = Path.Combine(wsPath, "Assets", "models", sub);
			if (Directory.Exists(dir))
			{
				foreach (var file in Directory.GetFiles(dir, "*.glb"))
				{
					models.Add(Path.GetFileName(file));
				}
			}
		}

		var list = models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
		return list.Count > 0 ? list : GetRiggedGlbModels();
	}

	private string? GetRequiredAssetTypeForCategory(string category)
	{
		if (IsGlbCategory(category, out string glbSub))
		{
			return glbSub switch
			{
				"units" => "Character",
				"buildings" => "Building",
				"resources" => "Environment",
				"projectiles" => "Projectile",
				"props" => "Prop",
				_ => "Prop"
			};
		}

		return category switch
		{
			"textures" => "Tilesheet",
			"vfx_spritesheets" => "SpellSpritesheet",
			"icons" => "Icon",
			"decals" => "Decal",
			"ribbons" or "ribbon_textures" => "Ribbon",
			"skyboxes" => "Skybox",
			"animations" => "Animation",
			"music" => "Music",
			"sfx" => "SoundEffect",
			_ => null
		};
	}

	private void OpenImportFileDialog()
	{
		if (_currentCategory == "shaders")
		{
			_shaderEditDialog.OpenForShader("", (_) => RefreshAssetList());
			return;
		}

		bool requireRealmMetadata = true;
		string[] extensions;
		string? requiredAssetType = GetRequiredAssetTypeForCategory(_currentCategory);

		if (IsGlbCategory(_currentCategory, out _))
		{
			extensions = new[] { ".glb" };
		}
		else
		{
			switch (_currentCategory)
			{
				case "textures":
				case "vfx_spritesheets":
				case "decals":
				case "ribbons":
				case "ribbon_textures":
				case "noise_textures":
				case "skyboxes":
				case "icons":
					extensions = new[] { ".rtex" };
					break;
				case "sfx":
				case "music":
					extensions = new[] { ".ogg" };
					break;
				case "animations":
					extensions = new[] { ".ranim" };
					break;
				default:
					extensions = Array.Empty<string>();
					requireRealmMetadata = false;
					break;
			}
		}

		Hud?.OpenAssetBrowser($"Import Asset ({_currentCategory})", extensions, OnImportFileSelected, requireRealmMetadata, requiredAssetType);
	}

	private void OnConvertImagePressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Image File to Convert to Realm format"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.png,*.jpg,*.jpeg,*.bmp,*.webp,*.tga,*.dds,*.rtex ; Supported Image Files (*.png, *.jpg, *.jpeg, *.bmp, *.webp, *.tga, *.dds, *.rtex)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string sourceFilePath = selectedPaths[0];
					ConvertImageToRealmFormat(sourceFilePath);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ConvertImageToRealmFormat(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string cleanBase = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(' ', '_');
		string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

		try
		{
			string metaPath = Path.Combine(wsPath, "metadata.json");
			JsonObject root = File.Exists(metaPath)
				? (JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject())
				: new JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
			var assetsObj = root["Assets"].AsObject();

			string targetCategory = _currentCategory;
			string subDir = targetCategory switch
			{
				"decals" => "decals",
				"icons" => "icons",
				"vfx_spritesheets" => "vfx",
				"ribbons" or "ribbon_textures" => "ribbons",
				"noise_textures" => "noise",
				"skyboxes" => "skyboxes",
				_ => "textures"
			};

			string destDir = Path.Combine(wsPath, "Assets", subDir);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.rtex");

			bool isRtexWithMeta = ext == ".rtex" && RealmMetadataHelper.HasRealmMetadata(sourceFilePath);
			TextureConversionResult convResult = default;

			if (isRtexWithMeta)
			{
				File.Copy(sourceFilePath, destPath, true);
			}
			else
			{
				if (targetCategory == "skyboxes")
				{
					convResult = TextureConverter.ProcessAndSaveSkybox(sourceFilePath, destPath);
				}
				else if (targetCategory == "decals")
				{
					convResult = TextureConverter.ProcessAndSaveDecalTexture(sourceFilePath, destPath);
				}
				else if (targetCategory == "icons")
				{
					convResult = TextureConverter.ProcessAndSaveIconTexture(sourceFilePath, destPath);
				}
				else if (targetCategory == "vfx_spritesheets")
				{
					convResult = TextureConverter.ProcessAndSaveSpritesheet(sourceFilePath, destPath, 4, 4);
				}
				else if (targetCategory is "ribbons" or "ribbon_textures")
				{
					convResult = TextureConverter.ProcessAndSaveRibbonTexture(sourceFilePath, destPath);
				}
				else if (targetCategory == "noise_textures")
				{
					convResult = TextureConverter.ProcessAndSaveSingleLayerTexture(sourceFilePath, destPath, "noise_texture");
				}
				else
				{
					convResult = TextureConverter.ProcessAndSaveTerrainTexture(sourceFilePath, destPath);
				}

				if (!convResult.Success)
				{
					Hud?.ShowFeedback($"Failed to convert image: {convResult.ErrorMessage}");
					return;
				}
			}

			byte[] bytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".rtex");

			if (!assetsObj.ContainsKey(targetCategory) || assetsObj[targetCategory] == null)
			{
				assetsObj[targetCategory] = new JsonObject();
			}

			if (targetCategory == "vfx_spritesheets")
			{
				assetsObj["vfx_spritesheets"].AsObject()[$"{cleanBase}.rtex"] = new JsonObject
				{
					["columns"] = 4,
					["rows"] = 4,
					["hash"] = hash
				};
			}
			else if (targetCategory == "textures")
			{
				float calculatedScaleFactor = isRtexWithMeta
					? TextureConverter.CalculateLuminanceScaleFactor(destPath)
					: convResult.ScaleFactor;

				var texDict = assetsObj["textures"].AsObject();
				string destFileName = $"{cleanBase}.rtex";
				int nextSwatchIdx = 0;
				foreach (var kvp in texDict)
				{
					if (kvp.Value is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && int.TryParse(idxNode?.ToString(), out int s))
					{
						if (s >= nextSwatchIdx) nextSwatchIdx = s + 1;
					}
				}

				texDict[destFileName] = new JsonObject
				{
					["hash"] = hash,
					["swatchIndex"] = nextSwatchIdx,
					["Scale_Factor"] = calculatedScaleFactor
				};

				if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
					Hud?.SetupTextureSwatches(false);
				}
			}
			else
			{
				assetsObj[targetCategory].AsObject()[$"{cleanBase}.rtex"] = hash;
			}

			MapJsonFormatter.SaveFormattedJson(metaPath, root);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported {0}.rtex"), cleanBase));

			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndPreview($"{cleanBase}.rtex");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertImageToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting image: {ex.Message}");
		}
	}

	private void OnConvertAudioPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Audio File to Convert to .ogg"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.mp3,*.wav,*.aiff,*.aif,*.flac,*.aac,*.m4a,*.wma,*.ogg ; Audio Files (*.mp3, *.wav, *.aiff, *.aif, *.flac, *.aac, *.m4a, *.wma, *.ogg)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string sourceFilePath = selectedPaths[0];
					ConvertAudioToRealmFormat(sourceFilePath);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ConvertAudioToRealmFormat(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string cleanBase = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(' ', '_');

		try
		{
			string targetCategory = _currentCategory == "music" ? "music" : "sfx";
			string sub = targetCategory == "music" ? "music" : "sfx";
			string destDir = Path.Combine(wsPath, "Assets", "audio", sub);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.ogg");

			var res = AudioConverter.ConvertToOgg(sourceFilePath, destPath);
			if (!res.Success)
			{
				Hud?.ShowFeedback($"Failed to convert audio: {res.ErrorMessage}");
				return;
			}

			string metaPath = Path.Combine(wsPath, "metadata.json");
			var root = File.Exists(metaPath)
				? (JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject())
				: new JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
			var assetsObj = root["Assets"].AsObject();
			if (!assetsObj.ContainsKey(targetCategory) || assetsObj[targetCategory] == null)
			{
				assetsObj[targetCategory] = new JsonObject();
			}

			byte[] bytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");
			assetsObj[targetCategory].AsObject()[$"{cleanBase}.ogg"] = hash;

			MapJsonFormatter.SaveFormattedJson(metaPath, root);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported audio {0}.ogg"), cleanBase));

			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndPreview($"{cleanBase}.ogg");
			PlayCurrentAudio();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertAudioToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting audio: {ex.Message}");
		}
	}

	private void OnConvertMixamoPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Mixamo FBX or GLB File to Convert to .ranim"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.fbx,*.glb,*.gltf ; 3D Animation Files (*.fbx, *.glb, *.gltf)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string sourceFilePath = selectedPaths[0];
					ConvertMixamoFileToRanim(sourceFilePath);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ConvertMixamoFileToRanim(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string animsDir = Path.Combine(wsPath, "Assets", "animations");
		Directory.CreateDirectory(animsDir);

		try
		{
			string originalFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
			var extracted = MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
			if (extracted.Count == 0)
			{
				Hud?.ShowFeedback(TranslationServer.Translate("No skeletal animations found in the selected file."));
				return;
			}

			string metaPath = Path.Combine(wsPath, "metadata.json");
			JsonObject root = File.Exists(metaPath)
				? (JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject())
				: new JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
			var assetsObj = root["Assets"].AsObject();
			if (!assetsObj.ContainsKey("animations") || assetsObj["animations"] == null)
			{
				assetsObj["animations"] = new JsonObject();
			}
			var animsObj = assetsObj["animations"].AsObject();

			int importedCount = 0;
			int skippedCount = 0;
			string firstSavedFileName = null;

			foreach (var (animName, animData) in extracted)
			{
				var (savedFileName, blake3, alreadyExisted) = MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
				animsObj[savedFileName] = blake3;
				if (alreadyExisted) skippedCount++;
				else importedCount++;
				if (firstSavedFileName == null) firstSavedFileName = savedFileName;
			}

			MapJsonFormatter.SaveFormattedJson(metaPath, root);

			if (importedCount > 0)
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Successfully converted and imported {0} .ranim animation(s)!"), importedCount));
			}
			else
			{
				Hud?.ShowFeedback(TranslationServer.Translate("Animation(s) already existed in map workspace (identical BLAKE3 hash)."));
			}

			AssetIndexService.Instance?.RescanAllDirectories();
			if (!string.IsNullOrEmpty(firstSavedFileName))
			{
				RefreshAssetListAndPreview(firstSavedFileName);
			}
			else
			{
				RefreshAssetList();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertMixamoFileToRanim error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting animation: {0}"), ex.Message));
		}
	}

	private void ConvertGlbToRealmFormat(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string cleanBase = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(' ', '_');

		try
		{
			string subCat = "props";
			if (IsGlbCategory(_currentCategory, out string glbSub))
			{
				subCat = glbSub;
			}

			string destDir = Path.Combine(wsPath, "Assets", "glb", subCat);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.glb");

			byte[] srcBytes = File.ReadAllBytes(sourceFilePath);
			var optimizer = ServiceLocator.TryGet<ModelOptimizerService>()
				?? new ModelOptimizerService(ServiceLocator.TryGet<WorldAccessor>());

			var optResult = optimizer.OptimizeGlb(srcBytes, new ModelOptimizerService.OptimizationOptions
			{
				AllowedPixelError = 1.5f,
				CreaseAngleDegrees = 45.0f,
				MaxTextureResolution = 1024,
				ForceReDecimate = true
			});

			if (!optResult.Success || optResult.OptimizedGlbBytes == null)
			{
				var glbOpt = new Realm.Shared.GlbOptimizer();
				var res = glbOpt.Optimize(srcBytes, new Realm.Shared.OptimizationOptions
				{
					SimplificationRatio = 0.5f,
					MaxTextureResolution = 1024,
					ForceReDecimate = true
				});
				if (res.Success && res.OutputGlbBytes != null)
				{
					File.WriteAllBytes(destPath, res.OutputGlbBytes);
				}
				else
				{
					Hud?.ShowFeedback($"Failed to optimize model: {optResult.ErrorMessage ?? res.ErrorMessage}");
					return;
				}
			}
			else
			{
				File.WriteAllBytes(destPath, optResult.OptimizedGlbBytes);
			}

			string metaPath = Path.Combine(wsPath, "metadata.json");
			JsonObject root = File.Exists(metaPath)
				? (JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject())
				: new JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
			var assetsObj = root["Assets"].AsObject();
			if (!assetsObj.ContainsKey("glb") || assetsObj["glb"] == null) assetsObj["glb"] = new JsonObject();
			var glbObj = assetsObj["glb"].AsObject();
			if (!glbObj.ContainsKey(subCat) || glbObj[subCat] == null) glbObj[subCat] = new JsonObject();

			byte[] finalBytes = File.ReadAllBytes(destPath);
			string hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(finalBytes, ".glb");

			float defaultScale = subCat switch
			{
				"resources" => 2.75f,
				"buildings" => 1.5f,
				"props" => 1.25f,
				"units" => 1.0f,
				_ => 1.0f
			};

			var (minY, autoYOffset) = Realm.Godot.Utils.ModelCache.CalculateModelBounds(destPath, defaultScale);
			bool isPropOrRes = subCat == "resources" || subCat == "props";

			var glbMetaObj = new JsonObject
			{
				["hash"] = hash,
				["min_y"] = minY,
				["scale"] = defaultScale,
				["y_offset"] = autoYOffset,
				["default_asset_type"] = subCat,
				["normal_mode"] = "Flat",
				["normalize_luminance"] = true,
				["ignore_player_color"] = isPropOrRes
			};
			glbObj[subCat].AsObject()[$"{cleanBase}.glb"] = glbMetaObj;

			if (!root.ContainsKey("ModelOffsets") || root["ModelOffsets"] == null) root["ModelOffsets"] = new JsonObject();
			root["ModelOffsets"].AsObject()[$"{cleanBase}.glb"] = autoYOffset;

			if (!root.ContainsKey("ModelScales") || root["ModelScales"] == null) root["ModelScales"] = new JsonObject();
			root["ModelScales"].AsObject()[$"{cleanBase}.glb"] = defaultScale;

			GameHost.Instance?.SetModelYOffset($"{cleanBase}.glb", autoYOffset);
			GameHost.Instance?.SetModelScale($"{cleanBase}.glb", defaultScale);

			string unitId = cleanBase;
			string? targetArrayKey = subCat switch
			{
				"units" => "CustomUnits",
				"buildings" => "CustomBuildings",
				"resources" => "CustomResources",
				"props" => "CustomProps",
				_ => null
			};

			if (targetArrayKey != null)
			{
				if (!root.ContainsKey(targetArrayKey) || root[targetArrayKey] == null) root[targetArrayKey] = new JsonArray();
				var targetArray = root[targetArrayKey].AsArray();
				bool exists = false;
				foreach (var item in targetArray)
				{
					if (item is JsonObject uObj && (uObj["UnitId"]?.ToString() == unitId || uObj["ModelPath"]?.ToString() == $"{cleanBase}.glb"))
					{
						exists = true;
						if (autoYOffset != 0f) uObj["YOffset"] = autoYOffset;
						break;
					}
				}

				if (!exists)
				{
					int defaultPathing = subCat switch
					{
						"units" => 9,
						"buildings" => 32,
						"resources" => 255,
						"props" => 255,
						_ => 9
					};

					var defaultEntity = new JsonObject
					{
						["UnitId"] = unitId,
						["Name"] = unitId,
						["Description"] = "",
						["ModelPath"] = $"{cleanBase}.glb",
						["Scale"] = defaultScale,
						["YOffset"] = autoYOffset,
						["PathingType"] = defaultPathing,
						["NormalMode"] = "Flat",
						["NormalizeLuminance"] = true,
						["Animations"] = new JsonObject()
					};

					if (subCat == "resources" || subCat == "props")
					{
						defaultEntity["IgnorePlayerColor"] = true;
					}

					targetArray.Add(defaultEntity);
				}
			}

			MapJsonFormatter.SaveFormattedJson(metaPath, root);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported 3D model {0}.glb"), cleanBase));

			RefreshAssetList();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertGlbToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting 3D model: {ex.Message}");
		}
	}



	private void OnImportFileSelected(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string fileName = Path.GetFileName(sourceFilePath);

		try
		{
			string metaPath = Path.Combine(wsPath, "metadata.json");
			JsonObject root = File.Exists(metaPath)
				? (JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject())
				: new JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
			var assetsObj = root["Assets"].AsObject();

			byte[] fileBytes = File.ReadAllBytes(sourceFilePath);
			string hash = ComputeHashHex(fileBytes);

			if (IsGlbCategory(_currentCategory, out string subCategory))
			{
				if (!ModelOptimizerService.HasOptimizationCompletedFlag(sourceFilePath))
				{
					Hud?.OpenConvertGlbDialog(sourceFilePath, subCategory, (_) => RefreshAssetList());
					return;
				}
				string destDir = Path.Combine(wsPath, "Assets", "models", subCategory);
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] finalBytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(finalBytes, ".glb");

				float defaultScale = subCategory switch
				{
					"resources" => 2.75f,
					"buildings" => 1.5f,
					"props" => 1.25f,
					"units" => 1.0f,
					_ => 1.0f
				};

				var (minY, autoYOffset) = Realm.Godot.Utils.ModelCache.CalculateModelBounds(destPath, defaultScale);
				bool isPropOrRes = subCategory == "resources" || subCategory == "props";

				if (!assetsObj.ContainsKey("glb") || assetsObj["glb"] == null) assetsObj["glb"] = new JsonObject();
				var glbObj = assetsObj["glb"].AsObject();
				if (!glbObj.ContainsKey(subCategory) || glbObj[subCategory] == null) glbObj[subCategory] = new JsonObject();

				var glbMetaObj = new JsonObject
				{
					["hash"] = hash,
					["min_y"] = minY,
					["scale"] = defaultScale,
					["y_offset"] = autoYOffset,
					["default_asset_type"] = subCategory,
					["normal_mode"] = "Flat",
					["normalize_luminance"] = true,
					["ignore_player_color"] = isPropOrRes
				};
				glbObj[subCategory].AsObject()[fileName] = glbMetaObj;

				if (!root.ContainsKey("ModelOffsets") || root["ModelOffsets"] == null) root["ModelOffsets"] = new JsonObject();
				root["ModelOffsets"].AsObject()[fileName] = autoYOffset;

				if (!root.ContainsKey("ModelScales") || root["ModelScales"] == null) root["ModelScales"] = new JsonObject();
				root["ModelScales"].AsObject()[fileName] = defaultScale;

				GameHost.Instance?.SetModelYOffset(fileName, autoYOffset);
				GameHost.Instance?.SetModelScale(fileName, defaultScale);

				string unitId = Path.GetFileNameWithoutExtension(fileName);
				string targetArrayKey = subCategory switch
				{
					"units" => "CustomUnits",
					"buildings" => "CustomBuildings",
					"resources" => "CustomResources",
					"props" => "CustomProps",
					_ => null
				};

				if (targetArrayKey != null)
				{
					if (!root.ContainsKey(targetArrayKey) || root[targetArrayKey] == null) root[targetArrayKey] = new JsonArray();
					var targetArray = root[targetArrayKey].AsArray();
					bool exists = false;
					foreach (var item in targetArray)
					{
						if (item is JsonObject uObj && (uObj["UnitId"]?.ToString() == unitId || uObj["ModelPath"]?.ToString() == fileName))
						{
							exists = true;
							if (autoYOffset != 0f) uObj["YOffset"] = autoYOffset;
							break;
						}
					}

					if (!exists)
					{
						int defaultPathing = subCategory switch
						{
							"units" => 9,
							"buildings" => 32,
							"resources" => 255,
							"props" => 255,
							_ => 9
						};

						var defaultEntity = new JsonObject
						{
							["UnitId"] = unitId,
							["Name"] = unitId,
							["Description"] = "",
							["ModelPath"] = fileName,
							["Scale"] = defaultScale,
							["YOffset"] = autoYOffset,
							["PathingType"] = defaultPathing,
							["NormalMode"] = "Flat",
							["NormalizeLuminance"] = true,
							["Animations"] = new JsonObject()
						};

						if (subCategory == "resources" || subCategory == "props")
						{
							defaultEntity["IgnorePlayerColor"] = true;
						}

						targetArray.Add(defaultEntity);
					}
				}
			}
			else if (_currentCategory == "textures")
			{
				string cleanBase = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace(' ', '_');
				string destDir = Path.Combine(wsPath, "Assets", "textures");
				Directory.CreateDirectory(destDir);
				string ext = Path.GetExtension(fileName).ToLowerInvariant();

				string destFileName = ext == ".rtex" ? fileName : $"{cleanBase}.rtex";
				string destPath = Path.Combine(destDir, destFileName);

				float calculatedScaleFactor = 1.0f;
				if (ext == ".rtex")
				{
					File.Copy(sourceFilePath, destPath, true);
					Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
					calculatedScaleFactor = TextureConverter.CalculateLuminanceScaleFactor(destPath);
				}
				else
				{
					var convResult = TextureConverter.ProcessAndSaveTerrainTexture(sourceFilePath, destPath);
					if (!convResult.Success)
					{
						throw new InvalidOperationException($"Failed to convert terrain texture: {convResult.ErrorMessage}");
					}
					calculatedScaleFactor = convResult.ScaleFactor;
				}

				byte[] rtexBytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

				if (!assetsObj.ContainsKey("textures") || assetsObj["textures"] == null) assetsObj["textures"] = new JsonObject();
				var texDict = assetsObj["textures"].AsObject();

				var parsedItems = new List<(string Key, int SwatchIndex, JsonNode? Node)>();
				foreach (var kvp in texDict)
				{
					int sIdx = -1;
					if (kvp.Value is JsonObject sObj)
					{
						if (sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
						{
							sIdx = parsed;
						}
						else if (sObj.TryGetPropertyValue("swatch_index", out var idxNode2) && idxNode2 != null && int.TryParse(idxNode2.ToString(), out int parsed2))
						{
							sIdx = parsed2;
						}
						else if (sObj.TryGetPropertyValue("SwatchIndex", out var idxNode3) && idxNode3 != null && int.TryParse(idxNode3.ToString(), out int parsed3))
						{
							sIdx = parsed3;
						}
					}
					parsedItems.Add((kvp.Key, sIdx, kvp.Value));
				}

				var usedIndices = new HashSet<int>();
				foreach (var item in parsedItems)
				{
					if (item.SwatchIndex >= 0)
					{
						usedIndices.Add(item.SwatchIndex);
					}
				}

				int nextFree = 0;
				for (int i = 0; i < parsedItems.Count; i++)
				{
					var item = parsedItems[i];
					if (item.SwatchIndex < 0)
					{
						while (usedIndices.Contains(nextFree))
						{
							nextFree++;
						}
						item.SwatchIndex = nextFree;
						usedIndices.Add(nextFree);
						parsedItems[i] = item;
					}
				}

				foreach (var item in parsedItems)
				{
					if (item.Key.Equals(destFileName, StringComparison.OrdinalIgnoreCase)) continue;

					if (item.Node is JsonObject sObj)
					{
						sObj["swatchIndex"] = item.SwatchIndex;
						if (sObj.ContainsKey("swatch_index")) sObj.Remove("swatch_index");
						if (sObj.ContainsKey("SwatchIndex")) sObj.Remove("SwatchIndex");
					}
					else
					{
						string existingHash = item.Node?.ToString() ?? "";
						texDict[item.Key] = new JsonObject
						{
							["hash"] = existingHash,
							["swatchIndex"] = item.SwatchIndex
						};
					}
				}

				int maxSwatchIndex = usedIndices.Count > 0 ? usedIndices.Max() : -1;
				int existingItemIndex = -1;
				for (int i = 0; i < parsedItems.Count; i++)
				{
					if (parsedItems[i].Key.Equals(destFileName, StringComparison.OrdinalIgnoreCase))
					{
						existingItemIndex = parsedItems[i].SwatchIndex;
						break;
					}
				}

				int nextSwatchIdx = existingItemIndex >= 0 ? existingItemIndex : maxSwatchIndex + 1;

				if (texDict.ContainsKey(destFileName) && texDict[destFileName] is JsonObject destObj)
				{
					destObj["hash"] = hash;
					destObj["swatchIndex"] = nextSwatchIdx;
					destObj["Scale_Factor"] = calculatedScaleFactor;
					if (destObj.ContainsKey("swatch_index")) destObj.Remove("swatch_index");
					if (destObj.ContainsKey("SwatchIndex")) destObj.Remove("SwatchIndex");
				}
				else
				{
					texDict[destFileName] = new JsonObject
					{
						["hash"] = hash,
						["swatchIndex"] = nextSwatchIdx,
						["Scale_Factor"] = calculatedScaleFactor
					};
				}
			}
			else if (_currentCategory == "vfx_spritesheets")
			{
				string destDir = Path.Combine(wsPath, "Assets", "vfx");
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] bytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ".rtex");

				if (!assetsObj.ContainsKey("vfx_spritesheets") || assetsObj["vfx_spritesheets"] == null) assetsObj["vfx_spritesheets"] = new JsonObject();
				assetsObj["vfx_spritesheets"].AsObject()[fileName] = new JsonObject
				{
					["columns"] = 4,
					["rows"] = 4,
					["hash"] = hash
				};
			}
			else if (_currentCategory == "animations")
			{
				string destDir = Path.Combine(wsPath, "Assets", "animations");
				Directory.CreateDirectory(destDir);

				if (fileName.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase))
				{
					string destPath = Path.Combine(destDir, fileName);
					File.Copy(sourceFilePath, destPath, true);
					Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
					byte[] bytes = File.ReadAllBytes(destPath);
					hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ".ranim");
					if (!assetsObj.ContainsKey("animations") || assetsObj["animations"] == null) assetsObj["animations"] = new JsonObject();
					assetsObj["animations"].AsObject()[fileName] = hash;
				}
				else if (fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
				{
					var res = Realm.Godot.Animation.MixamoAnimationImporter.ImportMixamoGlb(sourceFilePath, wsPath, "units");
					if (res.Success)
					{
						Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported {0} animations from {1}"), res.ExtractedAnimationFiles.Count, fileName));
					}
				}
			}
			else if (_currentCategory == "sfx" || _currentCategory == "music")
			{
				string sub = _currentCategory == "music" ? "music" : "sfx";
				string destDir = Path.Combine(wsPath, "Assets", "audio", sub);
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] bytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");

				if (!assetsObj.ContainsKey(_currentCategory) || assetsObj[_currentCategory] == null) assetsObj[_currentCategory] = new JsonObject();
				assetsObj[_currentCategory].AsObject()[fileName] = hash;
			}
			else
			{
				string sub = _currentCategory switch
				{
					"decals" => "decals",
					"icons" => "icons",
					"ribbons" or "ribbon_textures" => "ribbons",
					"noise_textures" => "noise",
					"skyboxes" => "skyboxes",
					_ => _currentCategory
				};
				string destDir = Path.Combine(wsPath, "Assets", sub);
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] bytes = File.ReadAllBytes(destPath);
				string ext = Path.GetExtension(destPath).ToLowerInvariant();
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ext);

				if (!assetsObj.ContainsKey(_currentCategory) || assetsObj[_currentCategory] == null) assetsObj[_currentCategory] = new JsonObject();
				assetsObj[_currentCategory].AsObject()[fileName] = hash;
			}

			MapJsonFormatter.SaveFormattedJson(metaPath, root);
			RefreshAssetList();
			string importedKey = _currentCategory == "textures" ? (Path.GetExtension(fileName).ToLowerInvariant() == ".rtex" ? fileName : $"{Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace(' ', '_')}.rtex") : fileName;
			LoadPreviewForAsset(_currentCategory, importedKey);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported asset {0} successfully."), importedKey));

			if (_currentCategory == "textures")
			{
				if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
					Hud?.SetupTextureSwatches(false);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] OnImportFileSelected error: {ex.Message}");
			Hud?.ShowFeedback($"Import error: {ex.Message}");
		}
	}

	private void OpenChangeTypeDialog(string category, string key, string currentSubCategory)
	{
		_changeTypeDialog.OpenForAsset(key, currentSubCategory, (targetSubCategory) =>
		{
			MoveGlbAssetType(key, currentSubCategory, targetSubCategory);
		});
	}

	private void MoveGlbAssetType(string key, string fromSubCat, string toSubCat)
	{
		if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(fromSubCat) || string.IsNullOrEmpty(toSubCat)) return;
		if (fromSubCat.Equals(toSubCat, StringComparison.OrdinalIgnoreCase)) return;

		try
		{
			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string srcDir = Path.Combine(wsPath, "Assets", "models", fromSubCat);
			string srcPath = Path.Combine(srcDir, key);
			string dstDir = Path.Combine(wsPath, "Assets", "models", toSubCat);
			Directory.CreateDirectory(dstDir);
			string dstPath = Path.Combine(dstDir, key);

			if (File.Exists(srcPath))
			{
				if (!srcPath.Equals(dstPath, StringComparison.OrdinalIgnoreCase))
				{
					File.Copy(srcPath, dstPath, true);
					File.Delete(srcPath);
				}
			}
			else if (!File.Exists(dstPath))
			{
				foreach (var candidateSub in new[] { "units", "buildings", "resources", "props", "projectiles" })
				{
					string candidatePath = Path.Combine(wsPath, "Assets", "models", candidateSub, key);
					if (File.Exists(candidatePath))
					{
						File.Copy(candidatePath, dstPath, true);
						File.Delete(candidatePath);
						break;
					}
				}
			}

			string hash = "";
			if (File.Exists(dstPath))
			{
				hash = ComputeHashHex(File.ReadAllBytes(dstPath));
			}

			string metaPath = Path.Combine(wsPath, "metadata.json");
			JsonObject root = File.Exists(metaPath)
				? (JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject())
				: new JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
			if (!root["Assets"].AsObject().ContainsKey("glb") || root["Assets"]["glb"] == null) root["Assets"]["glb"] = new JsonObject();
			var glbObj = root["Assets"]["glb"].AsObject();

			string canonicalType = toSubCat switch
			{
				"units" => "Character",
				"buildings" => "Building",
				"resources" => "Environment",
				"projectiles" => "Projectile",
				"props" => "Prop",
				_ => "Prop"
			};

			JsonNode existingMeta = null;
			if (glbObj.ContainsKey(fromSubCat) && glbObj[fromSubCat] is JsonObject fromObj && fromObj.ContainsKey(key))
			{
				existingMeta = fromObj[key]?.DeepClone();
				fromObj.Remove(key);
			}

			JsonObject itemMetaObj;
			if (existingMeta is JsonObject metaObj)
			{
				itemMetaObj = metaObj;
			}
			else
			{
				itemMetaObj = new JsonObject();
				if (existingMeta != null) itemMetaObj["hash"] = existingMeta.ToString();
				else if (!string.IsNullOrEmpty(hash)) itemMetaObj["hash"] = hash;
			}
			itemMetaObj["asset_type"] = canonicalType;
			itemMetaObj["default_asset_type"] = toSubCat;

			if (!glbObj.ContainsKey(toSubCat) || glbObj[toSubCat] == null) glbObj[toSubCat] = new JsonObject();
			glbObj[toSubCat].AsObject()[key] = itemMetaObj;

			string unitId = Path.GetFileNameWithoutExtension(key);

			// Remove from old custom entity array
			string oldArrayKey = fromSubCat switch
			{
				"units" => "CustomUnits",
				"buildings" => "CustomBuildings",
				"resources" => "CustomResources",
				"props" => "CustomProps",
				_ => null
			};

			if (oldArrayKey != null && root.ContainsKey(oldArrayKey) && root[oldArrayKey] is JsonArray oldArr)
			{
				for (int i = oldArr.Count - 1; i >= 0; i--)
				{
					if (oldArr[i] is JsonObject uObj)
					{
						string uId = uObj["UnitId"]?.ToString() ?? "";
						string mPath = uObj["ModelPath"]?.ToString() ?? "";
						if (uId.Equals(unitId, StringComparison.OrdinalIgnoreCase) || mPath.Equals(key, StringComparison.OrdinalIgnoreCase))
						{
							oldArr.RemoveAt(i);
						}
					}
				}
			}

			// Add to new custom entity array
			string newArrayKey = toSubCat switch
			{
				"units" => "CustomUnits",
				"buildings" => "CustomBuildings",
				"resources" => "CustomResources",
				"props" => "CustomProps",
				_ => null
			};

			if (newArrayKey != null)
			{
				if (!root.ContainsKey(newArrayKey) || root[newArrayKey] == null) root[newArrayKey] = new JsonArray();
				var newArr = root[newArrayKey].AsArray();
				bool exists = false;
				foreach (var item in newArr)
				{
					if (item is JsonObject uObj && (uObj["UnitId"]?.ToString() == unitId || uObj["ModelPath"]?.ToString() == key))
					{
						exists = true;
						break;
					}
				}

				if (!exists)
				{
					float defaultScale = toSubCat switch
					{
						"resources" => 2.75f,
						"buildings" => 1.5f,
						"props" => 1.25f,
						"units" => 1.0f,
						_ => 1.0f
					};

					int defaultPathing = toSubCat switch
					{
						"units" => 9,
						"buildings" => 32,
						"resources" => 255,
						"props" => 255,
						_ => 9
					};

					var defaultEntity = new JsonObject
					{
						["UnitId"] = unitId,
						["Name"] = unitId,
						["Description"] = "",
						["ModelPath"] = key,
						["Scale"] = defaultScale,
						["YOffset"] = 0.0f,
						["PathingType"] = defaultPathing,
						["NormalMode"] = "Flat",
						["NormalizeLuminance"] = true,
						["Animations"] = new JsonObject()
					};

					if (toSubCat == "resources" || toSubCat == "props")
					{
						defaultEntity["IgnorePlayerColor"] = true;
					}

					newArr.Add(defaultEntity);
				}
			}

			MapJsonFormatter.SaveFormattedJson(metaPath, root);
			RefreshAssetList();
			ClearPreview();
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Changed asset '{0}' type from {1} to {2}."), key, fromSubCat, toSubCat));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] MoveGlbAssetType error: {ex.Message}");
			Hud?.ShowFeedback($"Change type error: {ex.Message}");
		}
	}

	private void OpenEditSubDialog(string category, string key, JsonNode extraData)
	{
		if (category == "vfx_spritesheets")
		{
			int cols = 4;
			int rows = 4;
			if (extraData is JsonObject obj)
			{
				if (obj.ContainsKey("columns")) cols = (int)obj["columns"];
				if (obj.ContainsKey("rows")) rows = (int)obj["rows"];
			}

			_spritesheetEditDialog.OpenForSheet(key, cols, rows, (newCols, newRows) =>
			{
				SaveSpritesheetGrid(key, newCols, newRows);
				LoadVfxSpritesheet(key);
				RefreshAssetList();
			});
		}
		else if (category == "textures")
		{
			var texData = extraData as JsonObject ?? new JsonObject();
			_textureEditDialog.OpenForTexture(key, texData, (updatedData) =>
			{
				SaveTextureSwatch(key, updatedData);
				RefreshAssetList();
			});
		}
		else if (category == "decals" || (extraData is JsonObject edObj && edObj.ContainsKey("asset_type") && edObj["asset_type"]?.ToString() == "Decal"))
		{
			var decalData = extraData as JsonObject ?? new JsonObject();
			_decalEditDialog.OpenForDecal(key, decalData, (updatedData) =>
			{
				SaveDecalMetadata(key, updatedData);
				RefreshAssetList();
			});
		}
		else if (category == "shaders" || (extraData is JsonObject shObj && shObj.ContainsKey("asset_type") && shObj["asset_type"]?.ToString() == "Shader"))
		{
			_shaderEditDialog.OpenForShader(key, (updatedConfig) =>
			{
				RefreshAssetList();
			});
		}
	}

	private void SaveDecalMetadata(string key, JsonObject updatedData)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return;

		try
		{
			var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
			if (root != null)
			{
				if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new JsonObject();
				var assetsObj = root["Assets"].AsObject();
				if (!assetsObj.ContainsKey("decals") || assetsObj["decals"] == null) assetsObj["decals"] = new JsonObject();
				var decalsDict = assetsObj["decals"].AsObject();

				JsonObject newObj;
				if (decalsDict.TryGetPropertyValue(key, out var exNode) && exNode is JsonObject exObj)
				{
					newObj = exObj;
				}
				else
				{
					newObj = new JsonObject();
					if (exNode is JsonValue v) newObj["hash"] = v.ToString();
				}

				foreach (var prop in updatedData)
				{
					newObj[prop.Key] = prop.Value?.DeepClone();
				}

				decalsDict[key] = newObj;
				MapJsonFormatter.SaveFormattedJson(metaPath, root);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] SaveDecalMetadata error: {ex.Message}");
		}
	}

	private void SaveSpritesheetGrid(string key, int columns, int rows)
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return;

		try
		{
			var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
			var vfxSheets = (root?["Assets"]?["vfx_spritesheets"] ?? root?["MapProperties"]?["Assets"]?["vfx_spritesheets"])?.AsObject();
			if (vfxSheets != null)
			{
				string fileName = Path.GetFileName(key);
				string cleanBase = Path.GetFileNameWithoutExtension(key);

				JsonObject? sheetObj = null;
				if (vfxSheets.TryGetPropertyValue(fileName, out var s1) && s1 is JsonObject so1) sheetObj = so1;
				else if (vfxSheets.TryGetPropertyValue(key, out var s2) && s2 is JsonObject so2) sheetObj = so2;
				else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.rtex", out var s3) && s3 is JsonObject so3) sheetObj = so3;
				else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.png", out var s4) && s4 is JsonObject so4) sheetObj = so4;

				if (sheetObj != null)
				{
					sheetObj["columns"] = columns;
					sheetObj["rows"] = rows;
					MapJsonFormatter.SaveFormattedJson(metaPath, root);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] SaveSpritesheetGrid error: {ex.Message}");
		}
	}

	private void SaveTextureSwatch(string key, JsonObject updatedData)
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return;

		try
		{
			var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
			if (root != null && root["Assets"]?["textures"]?[key] is JsonNode node)
			{
				string hash = node is JsonObject o && o.ContainsKey("hash") ? o["hash"]?.ToString() : (node is JsonValue v ? v.ToString() : "");
				int swatchIdx = -1;
				if (node is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsedIdx))
				{
					swatchIdx = parsedIdx;
				}
				else if (node is JsonObject sObj2 && sObj2.TryGetPropertyValue("swatch_index", out var idxNode2) && idxNode2 != null && int.TryParse(idxNode2.ToString(), out int parsedIdx2))
				{
					swatchIdx = parsedIdx2;
				}
				else if (node is JsonObject sObj3 && sObj3.TryGetPropertyValue("SwatchIndex", out var idxNode3) && idxNode3 != null && int.TryParse(idxNode3.ToString(), out int parsedIdx3))
				{
					swatchIdx = parsedIdx3;
				}

				if (swatchIdx < 0 && root["Assets"]?["textures"] is JsonObject texturesDict)
				{
					var usedIndices = new HashSet<int>();
					foreach (var kvp in texturesDict)
					{
						if (kvp.Value is JsonObject itemObj && itemObj.TryGetPropertyValue("swatchIndex", out var sNode) && sNode != null && int.TryParse(sNode.ToString(), out int p) && p >= 0)
						{
							usedIndices.Add(p);
						}
					}
					int nextFree = 0;
					while (usedIndices.Contains(nextFree)) nextFree++;
					swatchIdx = nextFree;
				}

				var newObj = new JsonObject();
				if (!string.IsNullOrEmpty(hash)) newObj["hash"] = hash;
				if (swatchIdx >= 0) newObj["swatchIndex"] = swatchIdx;
				if (node is JsonObject origObj)
				{
					if (origObj.TryGetPropertyValue("scale_factor", out var sf1)) newObj["scale_factor"] = sf1?.DeepClone();
					else if (origObj.TryGetPropertyValue("Scale_Factor", out var sf2)) newObj["scale_factor"] = sf2?.DeepClone();
					else if (origObj.TryGetPropertyValue("ScaleFactor", out var sf3)) newObj["scale_factor"] = sf3?.DeepClone();
				}
				foreach (var prop in updatedData)
				{
					if (prop.Key.Equals("swatch_index", StringComparison.OrdinalIgnoreCase) || prop.Key.Equals("swatchIndex", StringComparison.OrdinalIgnoreCase)) continue;
					newObj[prop.Key] = prop.Value?.DeepClone();
				}
				root["Assets"]["textures"][key] = newObj;
				MapJsonFormatter.SaveFormattedJson(metaPath, root);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] SaveTextureSwatch error: {ex.Message}");
		}
	}

	private void DeleteAsset(string category, string key, string subCategory)
	{
		UIManager.Instance?.ShowConfirmationDialog(
			string.Format(TranslationServer.Translate("Are you sure you want to delete asset '{0}'?"), key),
			() =>
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				string metaPath = Path.Combine(wsPath, "metadata.json");
				if (File.Exists(metaPath))
				{
					try
					{
						var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
						var assetsObj = root?["Assets"]?.AsObject();
						if (assetsObj != null)
						{
							if (IsGlbCategory(category, out string glbSub) || category == "glb")
							{
								string targetSub = !string.IsNullOrEmpty(subCategory) ? subCategory : glbSub;
								assetsObj["glb"]?[targetSub]?.AsObject()?.Remove(key);
								string p = Path.Combine(wsPath, "Assets", "models", targetSub ?? "props", key);
								if (File.Exists(p)) File.Delete(p);
								if (File.Exists(p + ".import")) File.Delete(p + ".import");
								foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles" })
								{
									string cand = Path.Combine(wsPath, "Assets", "models", sub, key);
									if (File.Exists(cand)) File.Delete(cand);
									if (File.Exists(cand + ".import")) File.Delete(cand + ".import");
								}

								string oldArrayKey = targetSub switch
								{
									"units" => "CustomUnits",
									"buildings" => "CustomBuildings",
									"resources" => "CustomResources",
									"props" => "CustomProps",
									_ => null
								};

								if (oldArrayKey != null && root.ContainsKey(oldArrayKey) && root[oldArrayKey] is JsonArray oldArr)
								{
									string unitId = Path.GetFileNameWithoutExtension(key);
									for (int i = oldArr.Count - 1; i >= 0; i--)
									{
										if (oldArr[i] is JsonObject uObj)
										{
											string uId = uObj["UnitId"]?.ToString() ?? "";
											string mPath = uObj["ModelPath"]?.ToString() ?? "";
											if (uId.Equals(unitId, StringComparison.OrdinalIgnoreCase) || mPath.Equals(key, StringComparison.OrdinalIgnoreCase))
											{
												oldArr.RemoveAt(i);
											}
										}
									}
								}
							}
							else if (category == "textures" && assetsObj["textures"] is JsonObject texturesObj)
							{
								int deletedIdx = -1;
								if (texturesObj.ContainsKey(key) && texturesObj[key] is JsonObject delObj)
								{
									if (delObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
									{
										deletedIdx = parsed;
									}
								}
								texturesObj.Remove(key);
								string p = Path.Combine(wsPath, "Assets", "textures", key);
								if (File.Exists(p)) File.Delete(p);

								if (deletedIdx >= 0)
								{
									var remap = new Dictionary<int, int>();
									remap[deletedIdx] = 0;

									foreach (var kvp in texturesObj)
									{
										if (kvp.Value is JsonObject sObj)
										{
											if (sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsedIdx))
											{
												if (parsedIdx > deletedIdx)
												{
													int newIdx = parsedIdx - 1;
													sObj["swatchIndex"] = newIdx;
													remap[parsedIdx] = newIdx;
												}
											}
										}
									}

									GameHost.Instance?.GroundTerrain?.RemapSplatIndices(remap);
									SaveLoadService.RemapSplatExrFiles(wsPath, remap);
								}
							}
							else if (category == "shaders")
							{
								if (assetsObj.ContainsKey("shaders") && assetsObj["shaders"] is JsonObject shObj)
								{
									shObj.Remove(key);
								}
							}
							else
							{
								assetsObj[category]?.AsObject()?.Remove(key);
								string sub = category switch
								{
									"vfx_spritesheets" => "vfx",
									"animations" => "animations",
									"sfx" => "sfx",
									"music" => "music",
									"icons" => "icons",
									"decals" => "decals",
									"ribbons" or "ribbon_textures" => "ribbons",
									"noise_textures" or "noise" => "noise",
									"skyboxes" => "skyboxes",
									_ => category
								};
								string p = Path.Combine(wsPath, "Assets", sub, key);
								if (File.Exists(p)) File.Delete(p);
								if (File.Exists(p + ".import")) File.Delete(p + ".import");
								if (category is "sfx" or "music")
								{
									string pAudio = Path.Combine(wsPath, "Assets", "audio", sub, key);
									if (File.Exists(pAudio)) File.Delete(pAudio);
									if (File.Exists(pAudio + ".import")) File.Delete(pAudio + ".import");
								}
							}

							MapJsonFormatter.SaveFormattedJson(metaPath, root);
							SaveLoadService.SyncMetadataAssetsAndPrune(wsPath);
							if (category == "textures")
							{
								GameHost.Instance?.GroundTerrain?.ReloadTerrainTextures(true);
								Hud?.ReadMetadataAndRefreshTextures();
							}
							RefreshAssetList();
							ClearPreview();
							Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Deleted asset {0}."), key));
						}
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[AssetManagerDialog] DeleteAsset error: {ex.Message}");
					}
				}
			}
		);
	}

	private string GetCategoryDisplayName(string cat) => cat switch
	{
		"glb_units" => TranslationServer.Translate("3D Models (units)"),
		"glb_buildings" => TranslationServer.Translate("3D Models (buildings)"),
		"glb_resources" => TranslationServer.Translate("3D Models (resources)"),
		"glb_props" => TranslationServer.Translate("3D Models (props)"),
		"glb_projectiles" => TranslationServer.Translate("3D Models (projectiles)"),
		"glb_attachments" => TranslationServer.Translate("Object Attachments"),
		"glb" => TranslationServer.Translate("3D Models (GLB)"),
		"textures" => TranslationServer.Translate("Terrain Textures"),
		"vfx_spritesheets" => TranslationServer.Translate("VFX Spritesheets"),
		"animations" => TranslationServer.Translate("Animations (.ranim)"),
		"sfx" => TranslationServer.Translate("Sound Effects (SFX)"),
		"music" => TranslationServer.Translate("Music"),
		"icons" => TranslationServer.Translate("Icons"),
		"decals" => TranslationServer.Translate("Decals"),
		"ribbons" or "ribbon_textures" => TranslationServer.Translate("Ribbon Textures"),
		"noise_textures" => TranslationServer.Translate("Noise Textures"),
		"skyboxes" => TranslationServer.Translate("Skyboxes"),
		_ => cat
	};

	private void PruneUnusedAssets()
	{
		string catName = GetCategoryDisplayName(_currentCategory);
		UIManager.Instance?.ShowConfirmationDialog(
			string.Format(TranslationServer.Translate("Are you sure you want to prune all unused assets in '{0}'?"), catName),
			() => PerformPruneUnused()
		);
	}

	private void PerformPruneUnused()
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return;

		try
		{
			string json = File.ReadAllText(metaPath);
			var root = JsonNode.Parse(json)?.AsObject();
			if (root == null || root["Assets"] == null) return;

			var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			void AddRef(string val)
			{
				if (string.IsNullOrWhiteSpace(val)) return;
				string trimmed = val.Trim();
				referenced.Add(trimmed);
				string fn = Path.GetFileName(trimmed);
				if (!string.IsNullOrEmpty(fn))
				{
					referenced.Add(fn);
					string fnNoExt = Path.GetFileNameWithoutExtension(fn);
					if (!string.IsNullOrEmpty(fnNoExt))
					{
						referenced.Add(fnNoExt);
					}
				}
			}

			void CollectReferencesFromNode(JsonNode node)
			{
				if (node == null) return;
				if (node is JsonValue jVal)
				{
					AddRef(jVal.ToString());
				}
				else if (node is JsonArray jArr)
				{
					foreach (var child in jArr)
					{
						CollectReferencesFromNode(child);
					}
				}
				else if (node is JsonObject jObj)
				{
					foreach (var prop in jObj)
					{
						CollectReferencesFromNode(prop.Value);
					}
				}
			}

			// 1. Collect from all metadata.json sections EXCEPT "Assets"
			foreach (var prop in root)
			{
				if (prop.Key.Equals("Assets", StringComparison.OrdinalIgnoreCase)) continue;
				CollectReferencesFromNode(prop.Value);
			}

			// 2. Collect from terrain.json if present
			string terrainPath = Path.Combine(wsPath, "terrain.json");
			if (File.Exists(terrainPath))
			{
				try
				{
					var terrainRoot = JsonNode.Parse(File.ReadAllText(terrainPath));
					if (terrainRoot != null) CollectReferencesFromNode(terrainRoot);
				}
				catch { }
			}

			// 3. Collect from active ECS state / GameHost
			if (GameHost.Instance != null)
			{
				if (GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity))
				{
					if (GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.EditorState>(GameHost.Instance.WorldEntity))
					{
						var es = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.EditorState>(GameHost.Instance.WorldEntity);
						if (!string.IsNullOrEmpty(es.SkyboxPath)) AddRef(es.SkyboxPath);
					}
				}
			}

			// 4. Collect string literals from all workspace code and data files
			foreach (var file in Directory.GetFiles(wsPath, "*.*", SearchOption.AllDirectories))
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext == ".cs" || ext == ".json" || ext == ".txt" || ext == ".xml" || ext == ".gdshader" || ext == ".csproj")
				{
					if (Path.GetFileName(file).Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;
					if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
						file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
						file.Contains(Path.DirectorySeparatorChar + ".godot" + Path.DirectorySeparatorChar)) continue;

					try
					{
						string content = File.ReadAllText(file);
						var matches = StringLiteralRegex().Matches(content);
						foreach (Match m in matches)
						{
							if (m.Groups.Count > 1 && m.Groups[1].Value.Length < 120)
							{
								AddRef(m.Groups[1].Value);
							}
						}
					}
					catch { }
				}
			}

			int prunedCount = 0;
			var assetsObj = root["Assets"].AsObject();

			if (IsGlbCategory(_currentCategory, out string glbSub))
			{
				if (assetsObj["glb"] is JsonObject glbObj && glbObj[glbSub] is JsonObject subObj)
				{
					foreach (var modelProp in subObj.ToList())
					{
						if (!referenced.Contains(modelProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(modelProp.Key)))
						{
							subObj.Remove(modelProp.Key);
							string p = Path.Combine(wsPath, "Assets", "models", glbSub, modelProp.Key);
							if (File.Exists(p)) File.Delete(p);
							prunedCount++;
						}
					}
				}
			}
			else if (_currentCategory == "glb")
			{
				if (assetsObj["glb"] is JsonObject glbObj)
				{
					foreach (var subProp in glbObj.ToList())
					{
						if (subProp.Value is JsonObject subObj)
						{
							foreach (var modelProp in subObj.ToList())
							{
								if (!referenced.Contains(modelProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(modelProp.Key)))
								{
									subObj.Remove(modelProp.Key);
									string p = Path.Combine(wsPath, "Assets", "models", subProp.Key, modelProp.Key);
									if (File.Exists(p)) File.Delete(p);
									prunedCount++;
								}
							}
						}
					}
				}
			}
			else if (_currentCategory == "textures")
			{
				if (assetsObj["textures"] is JsonObject texObj)
				{
					var usedIndices = new HashSet<int>();
					if (GameHost.Instance?.GroundTerrain != null)
					{
						var splatMap = GameHost.Instance.GroundTerrain.SplatMap;
						if (splatMap != null)
						{
							int sw = splatMap.GetLength(0);
							int sd = splatMap.GetLength(1);
							for (int z = 0; z < sd; z++)
							{
								for (int x = 0; x < sw; x++)
								{
									var s = splatMap[x, z];
									if (s.Weight0 > 0.001f) usedIndices.Add(s.Index0);
									if (s.Weight1 > 0.001f) usedIndices.Add(s.Index1);
									if (s.Weight2 > 0.001f) usedIndices.Add(s.Index2);
									if (s.Weight3 > 0.001f) usedIndices.Add(s.Index3);
								}
							}
						}

						var cliffSplat = GameHost.Instance.GroundTerrain.CliffSplatMap;
						if (cliffSplat != null)
						{
							int cw = cliffSplat.GetLength(0);
							int cd = cliffSplat.GetLength(1);
							for (int z = 0; z < cd; z++)
							{
								for (int x = 0; x < cw; x++)
								{
									var c = cliffSplat[x, z];
									if (c.Weight0 > 0.001f) usedIndices.Add(c.Index0);
									if (c.Weight1 > 0.001f) usedIndices.Add(c.Index1);
									if (c.Weight2 > 0.001f) usedIndices.Add(c.Index2);
									if (c.Weight3 > 0.001f) usedIndices.Add(c.Index3);
								}
							}
						}
					}

					var keptEntries = new List<(string Key, int OldSwatchIdx, JsonObject Obj)>();
					var indexRemap = new Dictionary<int, int>();

					foreach (var itemProp in texObj.ToList())
					{
						int swatchIdx = -1;
						if (itemProp.Value is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
						{
							swatchIdx = parsed;
						}

						bool isUsed = referenced.Contains(itemProp.Key) || referenced.Contains(Path.GetFileNameWithoutExtension(itemProp.Key));
						if (!isUsed && swatchIdx >= 0 && usedIndices.Contains(swatchIdx))
						{
							isUsed = true;
						}

						if (isUsed)
						{
							var keptObj = itemProp.Value as JsonObject ?? new JsonObject();
							keptEntries.Add((itemProp.Key, swatchIdx, keptObj));
						}
						else
						{
							texObj.Remove(itemProp.Key);
							string p = Path.Combine(wsPath, "Assets", "textures", itemProp.Key);
							if (File.Exists(p)) File.Delete(p);
							if (swatchIdx >= 0)
							{
								indexRemap[swatchIdx] = 0;
							}
							prunedCount++;
						}
					}

					keptEntries.Sort((a, b) => a.OldSwatchIdx.CompareTo(b.OldSwatchIdx));
					for (int i = 0; i < keptEntries.Count; i++)
					{
						var entry = keptEntries[i];
						int newIdx = i;
						if (entry.OldSwatchIdx >= 0)
						{
							indexRemap[entry.OldSwatchIdx] = newIdx;
						}
						entry.Obj["swatchIndex"] = newIdx;
					}

					if (prunedCount > 0)
					{
						GameHost.Instance?.GroundTerrain?.RemapSplatIndices(indexRemap);
						SaveLoadService.RemapSplatExrFiles(wsPath, indexRemap);
					}
				}
			}
			else
			{
				if (assetsObj[_currentCategory] is JsonObject catObj)
				{
					string subDir = _currentCategory switch
					{
						"vfx_spritesheets" => "vfx",
						"animations" => "animations",
						"sfx" => "sfx",
						"music" => "music",
						"icons" => "icons",
						"decals" => "decals",
						"ribbons" or "ribbon_textures" => "ribbons",
						"noise_textures" or "noise" => "noise",
						"skyboxes" => "skyboxes",
						_ => _currentCategory
					};

					foreach (var itemProp in catObj.ToList())
					{
						if (!referenced.Contains(itemProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(itemProp.Key)))
						{
							catObj.Remove(itemProp.Key);
							string p = Path.Combine(wsPath, "Assets", subDir, itemProp.Key);
							if (File.Exists(p)) File.Delete(p);
							if (File.Exists(p + ".import")) File.Delete(p + ".import");
							prunedCount++;
						}
					}
				}
			}

			if (prunedCount > 0)
			{
				MapJsonFormatter.SaveFormattedJson(metaPath, root);
				SaveLoadService.SyncMetadataAssetsAndPrune(wsPath);
				if (_currentCategory == "textures")
				{
					GameHost.Instance?.GroundTerrain?.ReloadTerrainTextures(true);
					Hud?.ReadMetadataAndRefreshTextures();
				}
				RefreshAssetList();
				ClearPreview();
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Pruned {0} unused asset(s) from {1}."), prunedCount, GetCategoryDisplayName(_currentCategory)));
			}
			else
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("No unused assets found for '{0}'. All assets are currently referenced."), GetCategoryDisplayName(_currentCategory)));
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] PruneUnusedAssets error: {ex.Message}");
		}
	}

	private static string ComputeHashHex(byte[] bytes)
	{
		using var sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(bytes);
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
	}

	private void OnViewportGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				_isOrbiting = mouseButton.Pressed;
				_lastMousePosition = mouseButton.Position;
			}
			else if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
			{
				_isPanning = mouseButton.Pressed;
				_lastMousePosition = mouseButton.Position;
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
			{
				ZoomCamera(-1.0f);
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
			{
				ZoomCamera(1.0f);
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			Vector2 delta = mouseMotion.Position - _lastMousePosition;
			_lastMousePosition = mouseMotion.Position;

			if (_isOrbiting)
			{
				_cameraYaw -= delta.X * 0.01f;
				_cameraPitch -= delta.Y * 0.01f;
				UpdateCameraTransform();
			}
			else if (_isPanning && _camera != null)
			{
				Vector3 camRight = _camera.GlobalTransform.Basis.X;
				Vector3 camUp = _camera.GlobalTransform.Basis.Y;
				float panSpeed = _cameraDistance * 0.0025f;
				_targetPosition -= (camRight * delta.X - camUp * delta.Y) * panSpeed;
				UpdateCameraTransform();
			}
		}
	}

	private void ZoomCamera(float direction)
	{
		float factor = direction > 0 ? 1.15f : 0.85f;
		_cameraDistance = Mathf.Clamp(_cameraDistance * factor, _defaultDistance * 0.2f, _defaultDistance * 4.0f);
		UpdateCameraTransform();
	}

	public void SetCameraPreset(float yawDegrees, float pitchDegrees)
	{
		_cameraYaw = Mathf.DegToRad(yawDegrees);
		_cameraPitch = Mathf.DegToRad(pitchDegrees);
		_targetPosition = Vector3.Zero;
		UpdateCameraTransform();
	}

	public void ResetCameraDefault()
	{
		_cameraDistance = _defaultDistance;
		_targetPosition = Vector3.Zero;
		_cameraYaw = _defaultYaw;
		_cameraPitch = _defaultPitch;
		UpdateCameraTransform();
	}

	private void UpdateCameraTransform()
	{
		if (_camera == null) return;

		_cameraPitch = Mathf.Clamp(_cameraPitch, -1.45f, 1.45f);

		float cosPitch = Mathf.Cos(_cameraPitch);
		float sinPitch = Mathf.Sin(_cameraPitch);
		float cosYaw = Mathf.Cos(_cameraYaw);
		float sinYaw = Mathf.Sin(_cameraYaw);

		Vector3 offset = new Vector3(
			sinYaw * cosPitch,
			sinPitch,
			cosYaw * cosPitch
		) * _cameraDistance;

		Vector3 newPos = _targetPosition + offset;
		_camera.Position = newPos;
		_camera.LookAtFromPosition(newPos, _targetPosition, Vector3.Up);
	}

	public override void CloseDialog()
	{
		StopCurrentAudio();
		ClearPreview();
		base.CloseDialog();
	}

	[GeneratedRegex("\"([^\"]*)\"")]
	private static partial Regex StringLiteralRegex();
}
