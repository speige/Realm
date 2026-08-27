using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class AssetBrowserDialog : FloatingDialogBase
{
	private const float CellWidth = 130.0f;
	private const float CellHeight = 150.0f;
	private const float SpacingX = 10.0f;
	private const float SpacingY = 10.0f;
	private const float GridPadding = 10.0f;

	private HBoxContainer _folderChipsContainer;
	private Button _btnAddFolder;
	private Button _btnRescanAll;
	private Button _btnConvertMixamo;
	private OptionButton _optDirectoryFilter;
	private LineEdit _txtSearch;
	private Label _lblResultsCount;
	private Label _lblFilterExtensions;

	private ScrollContainer _scrollContainer;
	private Control _virtualGridContent;
	private Label _lblEmptyState;

	private PanelContainer _bottomDetailsPanel;
	private TextureRect _bottomThumbnail;
	private Label _lblSelectedFileName;
	private Label _lblSelectedPath;
	private Label _lblSelectedSize;
	private Button _btnAudioPlayPause;
	private LineEdit _txtTagsEdit;
	private Button _btnSaveTags;

	private readonly AudioStreamPlayer _audioPlayer;

	private readonly List<AssetGridCell> _cellPool = new();
	private readonly List<IndexedAsset> _matchingAssets = new();

	private static bool _hasAutoRescannedOnFirstOpen = false;
	private HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase);
	private string? _selectedDirectoryFilter;
	private IndexedAsset? _selectedAsset;
	private Action<string>? _onAssetSelectedCallback;

	public AssetBrowserDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Asset Browser"), new Vector2(880, 620))
	{
		_audioPlayer = new AudioStreamPlayer();
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		AssetThumbnailProvider.ThumbnailGenerated += OnThumbnailGenerated;
		AssetIndexService.Instance.DirectoryIndexingStateChanged += OnDirectoryIndexingStateChanged;
		AssetIndexService.Instance.DirectoryScanCompleted += OnDirectoryScanCompleted;
	}

	public override void _ExitTree()
	{
		StopAudio();
		AssetThumbnailProvider.ThumbnailGenerated -= OnThumbnailGenerated;
		AssetIndexService.Instance.DirectoryIndexingStateChanged -= OnDirectoryIndexingStateChanged;
		AssetIndexService.Instance.DirectoryScanCompleted -= OnDirectoryScanCompleted;
		base._ExitTree();
	}

	public override void CloseDialog()
	{
		StopAudio();
		base.CloseDialog();
	}

	private void OnDirectoryIndexingStateChanged(string dirPath, bool isIndexing)
	{
		Callable.From(() => RefreshFolderChips()).CallDeferred();
	}

	private void OnDirectoryScanCompleted(string dirPath)
	{
		Callable.From(() =>
		{
			RefreshFolderChips();
			RefreshSearchResults();
		}).CallDeferred();
	}

	private void OnThumbnailGenerated(string filePath, Texture2D texture)
	{
		if (_selectedAsset != null && string.Equals(_selectedAsset.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
		{
			_bottomThumbnail.Texture = texture;
		}
	}

	private void BuildControls()
	{
		var topFoldersSection = new VBoxContainer();
		topFoldersSection.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topFoldersSection);

		var folderHeaderRow = new HBoxContainer();
		folderHeaderRow.AddThemeConstantOverride("separation", 8);

		var lblFoldersTitle = new Label();
		lblFoldersTitle.Text = "📁 " + TranslationServer.Translate("Indexed Folders:");
		lblFoldersTitle.AddThemeFontSizeOverride("font_size", 11);
		lblFoldersTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		folderHeaderRow.AddChild(lblFoldersTitle);

		_btnAddFolder = new Button();
		_btnAddFolder.Set("icon_max_width", 0);
		_btnAddFolder.Text = "+ " + TranslationServer.Translate("Add Folder");
		_btnAddFolder.AddThemeFontSizeOverride("font_size", 11);
		_btnAddFolder.CustomMinimumSize = new Vector2(100, 24);
		_btnAddFolder.FocusMode = FocusModeEnum.None;
		_btnAddFolder.TooltipText = TranslationServer.Translate("Add a new local folder to the asset index");
		_btnAddFolder.Pressed += OnAddFolderPressed;
		folderHeaderRow.AddChild(_btnAddFolder);

		_btnRescanAll = new Button();
		_btnRescanAll.Set("icon_max_width", 0);
		_btnRescanAll.Text = "🔄 " + TranslationServer.Translate("Rescan");
		_btnRescanAll.AddThemeFontSizeOverride("font_size", 11);
		_btnRescanAll.CustomMinimumSize = new Vector2(80, 24);
		_btnRescanAll.FocusMode = FocusModeEnum.None;
		_btnRescanAll.TooltipText = TranslationServer.Translate("Rescan all indexed folders for new or modified files");
		_btnRescanAll.Pressed += OnRescanAllPressed;
		folderHeaderRow.AddChild(_btnRescanAll);

		_btnConvertMixamo = new Button();
		_btnConvertMixamo.Set("icon_max_width", 0);
		_btnConvertMixamo.Text = "🔄 " + TranslationServer.Translate("Convert Mixamo FBX/GLB to .ranim...");
		_btnConvertMixamo.AddThemeFontSizeOverride("font_size", 11);
		_btnConvertMixamo.CustomMinimumSize = new Vector2(230, 24);
		_btnConvertMixamo.FocusMode = FocusModeEnum.None;
		_btnConvertMixamo.TooltipText = TranslationServer.Translate("Select a Mixamo .fbx or .glb file from disk to extract and convert into .ranim animations");
		_btnConvertMixamo.Pressed += OnConvertMixamoPressed;
		_btnConvertMixamo.Visible = false;
		folderHeaderRow.AddChild(_btnConvertMixamo);

		topFoldersSection.AddChild(folderHeaderRow);

		var folderScroll = new ScrollContainer();
		folderScroll.CustomMinimumSize = new Vector2(0, 32);
		folderScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		folderScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;

		_folderChipsContainer = new HBoxContainer();
		_folderChipsContainer.AddThemeConstantOverride("separation", 6);
		folderScroll.AddChild(_folderChipsContainer);
		topFoldersSection.AddChild(folderScroll);

		var filterRow = new HBoxContainer();
		filterRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(filterRow);

		_txtSearch = new LineEdit();
		_txtSearch.PlaceholderText = TranslationServer.Translate("🔍 Search tags (e.g. grass, rock) or filename...");
		_txtSearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearch.AddThemeFontSizeOverride("font_size", 11);
		_txtSearch.TextChanged += (_) => RefreshSearchResults();
		filterRow.AddChild(_txtSearch);

		_optDirectoryFilter = new OptionButton();
		_optDirectoryFilter.CustomMinimumSize = new Vector2(180, 24);
		_optDirectoryFilter.AddThemeFontSizeOverride("font_size", 11);
		_optDirectoryFilter.FocusMode = FocusModeEnum.None;
		_optDirectoryFilter.ItemSelected += OnDirectoryFilterChanged;
		filterRow.AddChild(_optDirectoryFilter);

		var infoBar = new HBoxContainer();
		infoBar.AddThemeConstantOverride("separation", 12);
		BodyContainer.AddChild(infoBar);

		_lblResultsCount = new Label();
		_lblResultsCount.Text = "0 " + TranslationServer.Translate("items found");
		_lblResultsCount.AddThemeFontSizeOverride("font_size", 10);
		_lblResultsCount.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		infoBar.AddChild(_lblResultsCount);

		_lblFilterExtensions = new Label();
		_lblFilterExtensions.AddThemeFontSizeOverride("font_size", 10);
		_lblFilterExtensions.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		_lblFilterExtensions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		infoBar.AddChild(_lblFilterExtensions);

		var gridPanel = new PanelContainer();
		gridPanel.CustomMinimumSize = new Vector2(0, 320);
		gridPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		gridPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(gridPanel);

		_scrollContainer = new ScrollContainer();
		_scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		gridPanel.AddChild(_scrollContainer);

		_virtualGridContent = new Control();
		_virtualGridContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_virtualGridContent.MouseFilter = MouseFilterEnum.Pass;
		_scrollContainer.AddChild(_virtualGridContent);

		_lblEmptyState = new Label();
		_lblEmptyState.Text = TranslationServer.Translate("No assets found matching the search criteria.\nClick '+ Add Folder' above to index directories.");
		_lblEmptyState.HorizontalAlignment = HorizontalAlignment.Center;
		_lblEmptyState.VerticalAlignment = VerticalAlignment.Center;
		_lblEmptyState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblEmptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
		_lblEmptyState.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblEmptyState.AddThemeFontSizeOverride("font_size", 12);
		_lblEmptyState.Visible = false;
		gridPanel.AddChild(_lblEmptyState);

		_scrollContainer.GetVScrollBar().ValueChanged += (_) => UpdateVisibleGridCells();
		_scrollContainer.Resized += () => UpdateVisibleGridCells();

		_bottomDetailsPanel = new PanelContainer();
		_bottomDetailsPanel.CustomMinimumSize = new Vector2(0, 60);
		var detailStyle = new StyleBoxFlat();
		detailStyle.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.9f);
		detailStyle.BorderColor = new Color(0.25f, 0.23f, 0.20f, 0.8f);
		detailStyle.SetBorderWidthAll(1);
		detailStyle.CornerRadiusTopLeft = 4;
		detailStyle.CornerRadiusTopRight = 4;
		detailStyle.CornerRadiusBottomLeft = 4;
		detailStyle.CornerRadiusBottomRight = 4;
		detailStyle.ContentMarginLeft = 8;
		detailStyle.ContentMarginRight = 8;
		detailStyle.ContentMarginTop = 6;
		detailStyle.ContentMarginBottom = 6;
		_bottomDetailsPanel.AddThemeStyleboxOverride("panel", detailStyle);
		BodyContainer.AddChild(_bottomDetailsPanel);

		var bottomHBox = new HBoxContainer();
		bottomHBox.AddThemeConstantOverride("separation", 10);
		_bottomDetailsPanel.AddChild(bottomHBox);

		_bottomThumbnail = new TextureRect();
		_bottomThumbnail.CustomMinimumSize = new Vector2(48, 48);
		_bottomThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_bottomThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		bottomHBox.AddChild(_bottomThumbnail);

		var bottomInfoVBox = new VBoxContainer();
		bottomInfoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bottomInfoVBox.AddThemeConstantOverride("separation", 2);
		bottomHBox.AddChild(bottomInfoVBox);

		var nameRow = new HBoxContainer();
		nameRow.AddThemeConstantOverride("separation", 8);

		_lblSelectedFileName = new Label();
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedFileName.AddThemeFontSizeOverride("font_size", 11);
		_lblSelectedFileName.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		nameRow.AddChild(_lblSelectedFileName);

		_lblSelectedSize = new Label();
		_lblSelectedSize.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedSize.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		nameRow.AddChild(_lblSelectedSize);

		_btnAudioPlayPause = new Button();
		_btnAudioPlayPause.Set("icon_max_width", 0);
		_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
		_btnAudioPlayPause.AddThemeFontSizeOverride("font_size", 10);
		_btnAudioPlayPause.CustomMinimumSize = new Vector2(70, 20);
		_btnAudioPlayPause.FocusMode = FocusModeEnum.None;
		_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		_btnAudioPlayPause.Pressed += OnAudioPlayPausePressed;
		_btnAudioPlayPause.Visible = false;
		nameRow.AddChild(_btnAudioPlayPause);

		bottomInfoVBox.AddChild(nameRow);

		_lblSelectedPath = new Label();
		_lblSelectedPath.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_lblSelectedPath.AddThemeFontSizeOverride("font_size", 9);
		_lblSelectedPath.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		bottomInfoVBox.AddChild(_lblSelectedPath);

		var tagsRow = new HBoxContainer();
		tagsRow.AddThemeConstantOverride("separation", 6);

		var lblTagsTitle = new Label();
		lblTagsTitle.Text = TranslationServer.Translate("Tags:");
		lblTagsTitle.AddThemeFontSizeOverride("font_size", 10);
		lblTagsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		tagsRow.AddChild(lblTagsTitle);

		_txtTagsEdit = new LineEdit();
		_txtTagsEdit.PlaceholderText = TranslationServer.Translate("comma, separated, tags");
		_txtTagsEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtTagsEdit.AddThemeFontSizeOverride("font_size", 10);
		tagsRow.AddChild(_txtTagsEdit);

		_btnSaveTags = new Button();
		_btnSaveTags.Set("icon_max_width", 0);
		_btnSaveTags.Text = "💾 " + TranslationServer.Translate("Save Tags");
		_btnSaveTags.AddThemeFontSizeOverride("font_size", 10);
		_btnSaveTags.CustomMinimumSize = new Vector2(90, 22);
		_btnSaveTags.FocusMode = FocusModeEnum.None;
		_btnSaveTags.Pressed += OnSaveTagsPressed;
		tagsRow.AddChild(_btnSaveTags);

		bottomInfoVBox.AddChild(tagsRow);

		ApplyButton.Text = TranslationServer.Translate("Select");
	}

	public void OpenForImport(string titleText, IEnumerable<string> allowedExtensions, Action<string> onAssetSelected)
	{
		_onAssetSelectedCallback = onAssetSelected;
		_selectedAsset = null;
		_txtSearch.Text = string.Empty;

		TitleLabel.Text = string.IsNullOrWhiteSpace(titleText)
			? TranslationServer.Translate("Asset Browser")
			: TranslationServer.Translate(titleText);

		_allowedExtensions = (allowedExtensions ?? Array.Empty<string>())
			.Select(e => e.Trim().ToLowerInvariant())
			.Select(e => e.StartsWith(".") ? e : "." + e)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (_allowedExtensions.Count > 0)
		{
			_lblFilterExtensions.Text = $"{TranslationServer.Translate("Extensions")}: {string.Join(", ", _allowedExtensions)}";
			_lblFilterExtensions.Visible = true;
		}
		else
		{
			_lblFilterExtensions.Visible = false;
		}

		if (_btnConvertMixamo != null)
		{
			_btnConvertMixamo.Visible = _allowedExtensions.Contains(".ranim");
		}

		if (!_hasAutoRescannedOnFirstOpen)
		{
			_hasAutoRescannedOnFirstOpen = true;
			AssetIndexService.Instance.RescanAllDirectories();
		}

		RefreshFolderChips();
		RefreshSearchResults();
		UpdateSelectedAssetDisplay();

		OpenDialog();
	}

	private void RefreshFolderChips()
	{
		foreach (var child in _folderChipsContainer.GetChildren())
		{
			child.QueueFree();
		}

		_optDirectoryFilter.Clear();
		_optDirectoryFilter.AddItem(TranslationServer.Translate("All Indexed Folders"), 0);

		var indexedDirs = AssetIndexService.Instance.GetIndexedDirectories();
		for (int i = 0; i < indexedDirs.Count; i++)
		{
			string dirPath = indexedDirs[i];
			string folderName = Path.GetFileName(dirPath.TrimEnd('/', '\\'));
			if (string.IsNullOrEmpty(folderName))
			{
				folderName = dirPath;
			}

			bool isIndexing = AssetIndexService.Instance.IsDirectoryIndexing(dirPath);

			_optDirectoryFilter.AddItem(isIndexing ? $"⏳ {folderName} ({TranslationServer.Translate("Indexing...")})" : folderName, i + 1);

			var chip = new PanelContainer();
			var chipStyle = new StyleBoxFlat();
			chipStyle.BgColor = isIndexing ? new Color(0.18f, 0.16f, 0.12f, 0.9f) : new Color(0.15f, 0.16f, 0.19f, 0.9f);
			chipStyle.BorderColor = isIndexing ? UIStyle.ColorGold : new Color(0.35f, 0.32f, 0.28f, 0.7f);
			chipStyle.SetBorderWidthAll(1);
			chipStyle.CornerRadiusTopLeft = 3;
			chipStyle.CornerRadiusTopRight = 3;
			chipStyle.CornerRadiusBottomLeft = 3;
			chipStyle.CornerRadiusBottomRight = 3;
			chipStyle.ContentMarginLeft = 6;
			chipStyle.ContentMarginRight = 4;
			chipStyle.ContentMarginTop = 2;
			chipStyle.ContentMarginBottom = 2;
			chip.AddThemeStyleboxOverride("panel", chipStyle);
			chip.TooltipText = isIndexing
				? $"{dirPath}\n({TranslationServer.Translate("Indexing in progress...")})"
				: dirPath;

			var chipHBox = new HBoxContainer();
			chipHBox.AddThemeConstantOverride("separation", 4);
			chip.AddChild(chipHBox);

			var lblName = new Label();
			lblName.Text = isIndexing ? $"⏳ {folderName}" : $"📁 {folderName}";
			lblName.AddThemeFontSizeOverride("font_size", 10);
			lblName.AddThemeColorOverride("font_color", isIndexing ? UIStyle.ColorCyanGlow : UIStyle.ColorGold);
			chipHBox.AddChild(lblName);

			var btnRemove = new Button();
			btnRemove.Set("icon_max_width", 0);
			btnRemove.Text = "✕";
			btnRemove.AddThemeFontSizeOverride("font_size", 9);
			btnRemove.CustomMinimumSize = new Vector2(16, 16);
			btnRemove.FocusMode = FocusModeEnum.None;
			btnRemove.TooltipText = $"{TranslationServer.Translate("Remove folder from index")}: {dirPath}";
			btnRemove.Pressed += () =>
			{
				AssetIndexService.Instance.RemoveDirectory(dirPath);
				RefreshFolderChips();
				RefreshSearchResults();
			};
			chipHBox.AddChild(btnRemove);

			_folderChipsContainer.AddChild(chip);
		}

		if (_selectedDirectoryFilter != null && !indexedDirs.Contains(_selectedDirectoryFilter, StringComparer.OrdinalIgnoreCase))
		{
			_selectedDirectoryFilter = null;
			_optDirectoryFilter.Selected = 0;
		}
	}

	private void OnDirectoryFilterChanged(long index)
	{
		if (index == 0)
		{
			_selectedDirectoryFilter = null;
		}
		else
		{
			var indexedDirs = AssetIndexService.Instance.GetIndexedDirectories();
			int dirIdx = (int)index - 1;
			if (dirIdx >= 0 && dirIdx < indexedDirs.Count)
			{
				_selectedDirectoryFilter = indexedDirs[dirIdx];
			}
		}

		RefreshSearchResults();
	}

	private void OnAddFolderPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Folder to Index"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string folderPath = selectedPaths[0];
					AssetIndexService.Instance.AddDirectory(folderPath);
					RefreshFolderChips();
					RefreshSearchResults();
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show folder dialog"));
		}
	}

	private void OnRescanAllPressed()
	{
		AssetIndexService.Instance.RescanAllDirectories();
		Hud?.ShowFeedback(TranslationServer.Translate("Rescanned all indexed directories."));
		RefreshSearchResults();
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
		if (string.IsNullOrEmpty(sourceFilePath) || !System.IO.File.Exists(sourceFilePath)) return;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace");
		string animsDir = System.IO.Path.Combine(wsPath, "Assets", "animations");
		System.IO.Directory.CreateDirectory(animsDir);

		try
		{
			string originalFileName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
			var extracted = Realm.Godot.Animation.MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
			if (extracted.Count == 0)
			{
				Hud?.ShowFeedback(TranslationServer.Translate("No skeletal animations found in the selected file."));
				return;
			}

			string metaPath = System.IO.Path.Combine(wsPath, "metadata.json");
			System.Text.Json.Nodes.JsonObject root = System.IO.File.Exists(metaPath)
				? (System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(metaPath))?.AsObject() ?? new System.Text.Json.Nodes.JsonObject())
				: new System.Text.Json.Nodes.JsonObject();

			if (!root.ContainsKey("Assets") || root["Assets"] == null) root["Assets"] = new System.Text.Json.Nodes.JsonObject();
			var assetsObj = root["Assets"].AsObject();
			if (!assetsObj.ContainsKey("animations") || assetsObj["animations"] == null) assetsObj["animations"] = new System.Text.Json.Nodes.JsonObject();
			var animsObj = assetsObj["animations"].AsObject();

			int importedCount = 0;
			int skippedCount = 0;

			foreach (var (animName, animData) in extracted)
			{
				var (savedFileName, blake3, alreadyExisted) = Realm.Godot.Animation.MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
				animsObj[savedFileName] = blake3;
				if (alreadyExisted) skippedCount++;
				else importedCount++;
			}

			System.IO.File.WriteAllText(metaPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

			AssetIndexService.Instance.AddDirectory(animsDir);
			AssetIndexService.Instance.RescanDirectory(animsDir);

			RefreshFolderChips();
			RefreshSearchResults();

			if (importedCount > 0)
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Successfully converted and imported {0} .ranim animation(s)!"), importedCount));
			}
			else
			{
				Hud?.ShowFeedback(TranslationServer.Translate("Animation(s) already existed in map workspace (identical BLAKE3 hash)."));
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetBrowserDialog] ConvertMixamoFileToRanim error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting animation: {0}"), ex.Message));
		}
	}

	private void RefreshSearchResults()
	{
		string searchTerm = _txtSearch.Text?.Trim() ?? string.Empty;
		_matchingAssets.Clear();
		_matchingAssets.AddRange(AssetIndexService.Instance.SearchAssets(searchTerm, _allowedExtensions, _selectedDirectoryFilter));

		_lblResultsCount.Text = $"{_matchingAssets.Count} {TranslationServer.Translate("items found")}";
		_lblEmptyState.Visible = _matchingAssets.Count == 0;

		if (_selectedAsset != null && !_matchingAssets.Any(a => string.Equals(a.FilePath, _selectedAsset.FilePath, StringComparison.OrdinalIgnoreCase)))
		{
			_selectedAsset = null;
			UpdateSelectedAssetDisplay();
		}

		UpdateVirtualGridSize();
		UpdateVisibleGridCells();
	}

	private void UpdateVirtualGridSize()
	{
		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (_matchingAssets.Count + columns - 1) / columns;
		float totalHeight = GridPadding * 2.0f + totalRows * CellHeight + Math.Max(0, totalRows - 1) * SpacingY;

		_virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
	}

	private void UpdateVisibleGridCells()
	{
		if (_matchingAssets.Count == 0)
		{
			foreach (var cell in _cellPool)
			{
				cell.Visible = false;
			}
			return;
		}

		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (_matchingAssets.Count + columns - 1) / columns;

		float scrollY = _scrollContainer.ScrollVertical;
		float viewHeight = _scrollContainer.Size.Y;

		int startRow = Math.Max(0, (int)((scrollY - GridPadding) / (CellHeight + SpacingY)) - 1);
		int endRow = Math.Min(totalRows - 1, (int)((scrollY + viewHeight - GridPadding) / (CellHeight + SpacingY)) + 1);

		int startIndex = Math.Max(0, startRow * columns);
		int endIndex = Math.Min(_matchingAssets.Count - 1, (endRow + 1) * columns - 1);
		int visibleCount = endIndex >= startIndex ? (endIndex - startIndex + 1) : 0;

		while (_cellPool.Count < visibleCount)
		{
			var newCell = new AssetGridCell();
			_cellPool.Add(newCell);
			_virtualGridContent.AddChild(newCell);
		}

		for (int i = 0; i < visibleCount; i++)
		{
			int assetIndex = startIndex + i;
			var asset = _matchingAssets[assetIndex];
			int row = assetIndex / columns;
			int col = assetIndex % columns;

			float posX = GridPadding + col * (CellWidth + SpacingX);
			float posY = GridPadding + row * (CellHeight + SpacingY);

			var cell = _cellPool[i];
			cell.Position = new Vector2(posX, posY);
			cell.Size = new Vector2(CellWidth, CellHeight);
			cell.Visible = true;

			bool isSelected = _selectedAsset != null && string.Equals(_selectedAsset.FilePath, asset.FilePath, StringComparison.OrdinalIgnoreCase);
			cell.Bind(asset, isSelected, OnAssetCellClicked);
		}

		for (int i = visibleCount; i < _cellPool.Count; i++)
		{
			_cellPool[i].Visible = false;
		}
	}

	private void OnAssetCellClicked(IndexedAsset asset, bool isDoubleClick)
	{
		_selectedAsset = asset;
		UpdateSelectedAssetDisplay();
		UpdateVisibleGridCells();

		if (isDoubleClick)
		{
			ApplyAndClose();
		}
	}

	private void UpdateSelectedAssetDisplay()
	{
		StopAudio();

		if (_selectedAsset != null)
		{
			_lblSelectedFileName.Text = _selectedAsset.FileName;
			_lblSelectedPath.Text = _selectedAsset.FilePath;
			_lblSelectedSize.Text = FormatFileSize(_selectedAsset.FileSizeBytes);
			_bottomThumbnail.Texture = AssetThumbnailProvider.GetThumbnail(_selectedAsset);
			_txtTagsEdit.Text = _selectedAsset.Tags != null ? string.Join(", ", _selectedAsset.Tags) : string.Empty;
			_txtTagsEdit.Editable = true;
			_btnSaveTags.Disabled = false;

			string ext = _selectedAsset.Extension?.ToLowerInvariant() ?? "";
			bool isAudio = ext is ".ogg" or ".wav" or ".mp3";
			if (isAudio && File.Exists(_selectedAsset.FilePath))
			{
				try
				{
					if (ext == ".ogg")
					{
						var oggStream = AudioStreamOggVorbis.LoadFromFile(_selectedAsset.FilePath);
						if (oggStream != null)
						{
							oggStream.Loop = false;
							_audioPlayer.Stream = oggStream;
						}
					}
					else
					{
						_audioPlayer.Stream = GD.Load<AudioStream>(_selectedAsset.FilePath);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[AssetBrowserDialog] Failed to load audio stream: {ex.Message}");
				}
				_btnAudioPlayPause.Visible = (_audioPlayer.Stream != null);
				_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
				_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
			}
			else
			{
				_btnAudioPlayPause.Visible = false;
			}
		}
		else
		{
			_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
			_lblSelectedPath.Text = string.Empty;
			_lblSelectedSize.Text = string.Empty;
			_bottomThumbnail.Texture = null;
			_txtTagsEdit.Text = string.Empty;
			_txtTagsEdit.Editable = false;
			_btnSaveTags.Disabled = true;
			_btnAudioPlayPause.Visible = false;
		}
	}

	private void OnAudioPlayPausePressed()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null) return;

		if (_audioPlayer.Playing && !_audioPlayer.StreamPaused)
		{
			_audioPlayer.StreamPaused = true;
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Resume audio");
		}
		else if (_audioPlayer.Playing && _audioPlayer.StreamPaused)
		{
			_audioPlayer.StreamPaused = false;
			_btnAudioPlayPause.Text = "⏸ " + TranslationServer.Translate("Pause");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Pause audio");
		}
		else
		{
			_audioPlayer.Play();
			_audioPlayer.StreamPaused = false;
			_btnAudioPlayPause.Text = "⏸ " + TranslationServer.Translate("Pause");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Pause audio");
		}
	}

	private void OnAudioFinished()
	{
		if (_audioPlayer != null)
		{
			_audioPlayer.StreamPaused = false;
		}
		if (_btnAudioPlayPause != null)
		{
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
	}

	private void StopAudio()
	{
		if (_audioPlayer != null)
		{
			if (_audioPlayer.Playing || _audioPlayer.StreamPaused)
			{
				_audioPlayer.Stop();
				_audioPlayer.StreamPaused = false;
			}
			_audioPlayer.Stream = null;
		}
		if (_btnAudioPlayPause != null)
		{
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
	}

	private void OnSaveTagsPressed()
	{
		if (_selectedAsset == null)
		{
			return;
		}

		string rawTags = _txtTagsEdit.Text ?? string.Empty;
		var tagList = rawTags
			.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(t => t.Trim().ToLowerInvariant())
			.Where(t => !string.IsNullOrEmpty(t))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		AssetIndexService.Instance.UpdateAssetTags(_selectedAsset.FilePath, tagList);
		_selectedAsset.Tags = tagList;

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Updated tags for {0}"), _selectedAsset.FileName));
		RefreshSearchResults();
	}

	protected override void OnApply()
	{
		StopAudio();
		if (_selectedAsset != null && File.Exists(_selectedAsset.FilePath))
		{
			_onAssetSelectedCallback?.Invoke(_selectedAsset.FilePath);
		}
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024)
		{
			return $"{bytes} B";
		}
		if (bytes < 1024 * 1024)
		{
			return $"{bytes / 1024.0:F1} KB";
		}
		return $"{bytes / (1024.0 * 1024.0):F2} MB";
	}
}
