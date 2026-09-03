using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

public class TerrainTextureSnapshot
{
	public float Brightness { get; set; } = 1.0f;
	public Color Tint { get; set; } = Colors.White;
	public float HeightScale { get; set; } = 1.0f;
	public float HeightOffset { get; set; } = 0.0f;
	public float CrevicePower { get; set; } = 1.0f;
	public float NormalScale { get; set; } = 1.0f;
	public float RoughnessScale { get; set; } = 1.0f;
	public string TileMode { get; set; } = "Stochastic";
	public float UvScale { get; set; } = 1.0f;
	public float StochasticTileSize { get; set; } = 1.0f;
	public float CrossFade { get; set; } = 0.0f;

	public TerrainTextureSnapshot Clone()
	{
		return new TerrainTextureSnapshot
		{
			Brightness = this.Brightness,
			Tint = this.Tint,
			HeightScale = this.HeightScale,
			HeightOffset = this.HeightOffset,
			CrevicePower = this.CrevicePower,
			NormalScale = this.NormalScale,
			RoughnessScale = this.RoughnessScale,
			TileMode = this.TileMode,
			UvScale = this.UvScale,
			StochasticTileSize = this.StochasticTileSize,
			CrossFade = this.CrossFade
		};
	}
}

public class TerrainTextureUndoAction : IEditorAction
{
	private readonly string _textureFileName;
	private readonly TerrainTextureSnapshot _before;
	private readonly TerrainTextureSnapshot _after;

	public TerrainTextureUndoAction(string textureFileName, TerrainTextureSnapshot before, TerrainTextureSnapshot after)
	{
		_textureFileName = textureFileName;
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		ApplySnapshot(_before);
	}

	public void Redo()
	{
		ApplySnapshot(_after);
	}

	private void ApplySnapshot(TerrainTextureSnapshot snapshot)
	{
		if (string.IsNullOrEmpty(_textureFileName)) return;

		string tintHex = $"#{snapshot.Tint.ToHtml(false)}";

		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(
				_textureFileName,
				snapshot.TileMode,
				snapshot.UvScale,
				snapshot.StochasticTileSize,
				snapshot.CrossFade,
				snapshot.Brightness,
				tintHex,
				snapshot.HeightScale,
				snapshot.HeightOffset,
				snapshot.CrevicePower,
				snapshot.NormalScale,
				snapshot.RoughnessScale
			);
		}

		try
		{
			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string metadataPath = Path.Combine(wsPath, "metadata.json");
			if (File.Exists(metadataPath))
			{
				string json = File.ReadAllText(metadataPath);
				var root = JsonNode.Parse(json)?.AsObject();
				var texturesObj = root?["Assets"]?["textures"]?.AsObject() ?? root?["textures"]?.AsObject();
				if (texturesObj != null)
				{
					foreach (var kvp in texturesObj)
					{
						if (string.Equals(kvp.Key, _textureFileName, StringComparison.OrdinalIgnoreCase) ||
							string.Equals(Path.GetFileName(kvp.Key), _textureFileName, StringComparison.OrdinalIgnoreCase))
						{
							if (kvp.Value is JsonObject sObj)
							{
								sObj["Brightness"] = snapshot.Brightness;
								sObj["Tint"] = tintHex;
								sObj["Height_Scale"] = snapshot.HeightScale;
								sObj["Height_Offset"] = snapshot.HeightOffset;
								sObj["Crevice_Power"] = snapshot.CrevicePower;
								sObj["Normal_Scale"] = snapshot.NormalScale;
								sObj["Roughness_Scale"] = snapshot.RoughnessScale;
								sObj["Tile_Mode"] = snapshot.TileMode;
								sObj["UV_Scale"] = snapshot.UvScale;
								sObj["Stochastic_Tile_Size"] = snapshot.StochasticTileSize;
								sObj["Cross_Fade"] = snapshot.CrossFade;
								MapJsonFormatter.SaveFormattedJson(metadataPath, root);
								break;
							}
						}
					}
				}
			}
		}
		catch { }
	}
}

public partial class TerrainTextureEditDialog : FloatingDialogBase
{
	private string _textureFileName = "";
	private float _brightness = 1.0f;
	private Color _tint = Colors.White;
	private float _heightScale = 1.0f;
	private float _heightOffset = 0.0f;
	private float _crevicePower = 1.0f;
	private float _normalScale = 1.0f;
	private float _roughnessScale = 1.0f;
	private string _tileMode = "Stochastic";
	private float _uvScale = 1.0f;
	private float _stochasticTileSize = 1.0f;
	private float _crossFade = 0.0f;

	private TerrainTextureSnapshot _initialSnapshot;

	private Action<JsonObject> _onApplied;

	private HSlider _sldBrightness;
	private Label _lblBrightness;
	private ColorPickerButton _btnTint;
	private HSlider _sldRoughnessScale;
	private Label _lblRoughnessScale;
	private HSlider _sldNormalScale;
	private Label _lblNormalScale;
	private Label _iconHelpNormalScale;
	private HSlider _sldHeightScale;
	private Label _lblHeightScale;
	private Label _iconHelpHeightScale;
	private HSlider _sldHeightOffset;
	private Label _lblHeightOffset;
	private Label _iconHelpHeightOffset;
	private HSlider _sldCrevicePower;
	private Label _lblCrevicePower;
	private Label _iconHelpCrevicePower;
	private OptionButton _optTileMode;
	private HSlider _sldUvScale;
	private Label _lblUvScale;
	private HSlider _sldStochasticTileSize;
	private Label _lblStochasticTileSize;
	private HSlider _sldCrossFade;
	private Label _lblCrossFade;

	public TerrainTextureEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Terrain Texture Swatch"), new Vector2(460, 640))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(520);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		// SECTION 1: COLOR & LIGHTING
		AddSectionHeader(contentVBox, "🎨 " + TranslationServer.Translate("COLOR & LIGHTING"), new Color(0.95f, 0.8f, 0.4f));

		(_sldBrightness, _lblBrightness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Brightness:"),
			0.2f,
			2.5f,
			0.05f,
			_brightness,
			(val) =>
			{
				_brightness = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		var rowTint = new HBoxContainer();
		rowTint.AddThemeConstantOverride("separation", 8);
		var lblTint = new Label();
		lblTint.Text = TranslationServer.Translate("Tint Color:");
		lblTint.CustomMinimumSize = new Vector2(140, 0);
		lblTint.AddThemeFontSizeOverride("font_size", 11);
		rowTint.AddChild(lblTint);

		_btnTint = new ColorPickerButton();
		_btnTint.CustomMinimumSize = new Vector2(90, 24);
		_btnTint.Color = _tint;
		_btnTint.ColorChanged += (newCol) =>
		{
			_tint = newCol;
			ApplyLiveTerrainUpdate();
		};
		rowTint.AddChild(_btnTint);
		contentVBox.AddChild(rowTint);

		(_sldRoughnessScale, _lblRoughnessScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Roughness Scale:"),
			0.1f,
			3.0f,
			0.05f,
			_roughnessScale,
			(val) =>
			{
				_roughnessScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		// SECTION 2: HEIGHTMAP & CREVICE BLENDING
		AddSectionHeader(contentVBox, "🏔️ " + TranslationServer.Translate("HEIGHTMAP & CREVICES"), new Color(0.4f, 0.85f, 0.5f));

		(_sldNormalScale, _lblNormalScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Normal Strength:"),
			0.0f,
			3.0f,
			0.05f,
			_normalScale,
			(val) =>
			{
				_normalScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpNormalScale = CreateHelpTooltipIcon(_sldNormalScale);

		(_sldHeightScale, _lblHeightScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Height Scale:"),
			0.1f,
			3.0f,
			0.05f,
			_heightScale,
			(val) =>
			{
				_heightScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpHeightScale = CreateHelpTooltipIcon(_sldHeightScale);

		(_sldHeightOffset, _lblHeightOffset) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Height Offset:"),
			-1.0f,
			1.0f,
			0.05f,
			_heightOffset,
			(val) =>
			{
				_heightOffset = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00",
			140f
		);
		_iconHelpHeightOffset = CreateHelpTooltipIcon(_sldHeightOffset);

		(_sldCrevicePower, _lblCrevicePower) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Crevice Power:"),
			0.5f,
			4.0f,
			0.1f,
			_crevicePower,
			(val) =>
			{
				_crevicePower = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpCrevicePower = CreateHelpTooltipIcon(_sldCrevicePower);

		// SECTION 3: TILING & PROJECTION
		AddSectionHeader(contentVBox, "📐 " + TranslationServer.Translate("TILING & PROJECTION"), new Color(0.35f, 0.75f, 0.9f));

		_optTileMode = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Tile Mode:"),
			new[] { "Grid", "Stochastic" },
			_tileMode == "Stochastic" ? 1 : 0,
			(idx) =>
			{
				_tileMode = idx == 1 ? "Stochastic" : "Grid";
				UpdateTileModeVisibility();
				ApplyLiveTerrainUpdate();
			},
			140f
		);

		(_sldUvScale, _lblUvScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("UV Scale:"),
			0.1f,
			4.0f,
			0.05f,
			_uvScale,
			(val) =>
			{
				_uvScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		(_sldStochasticTileSize, _lblStochasticTileSize) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Stochastic Size:"),
			0.5f,
			3.0f,
			0.05f,
			_stochasticTileSize,
			(val) =>
			{
				_stochasticTileSize = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		(_sldCrossFade, _lblCrossFade) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Cross-Fade:"),
			0.0f,
			10.0f,
			0.25f,
			_crossFade,
			(val) =>
			{
				_crossFade = val;
				ApplyLiveTerrainUpdate();
			},
			"0.0'%'",
			140f
		);

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
	}

	private Label CreateHelpTooltipIcon(HSlider slider)
	{
		var helpIcon = new Label();
		helpIcon.Text = "❓";
		helpIcon.TooltipText = TranslationServer.Translate("NOTE: unavailable on your current graphics preset");
		helpIcon.MouseFilter = Control.MouseFilterEnum.Stop;
		helpIcon.AddThemeFontSizeOverride("font_size", 10);
		helpIcon.Visible = false;

		if (slider.GetParent() is HBoxContainer row)
		{
			row.AddChild(helpIcon);
			row.MoveChild(helpIcon, 0);
		}
		return helpIcon;
	}

	private void UpdateGraphicsQualityState()
	{
		bool isAvailable = GameSettings.QualityIdx >= GraphicsQuality.High;
		string noteTooltip = TranslationServer.Translate("NOTE: unavailable on your current graphics preset");

		void SetSliderQuality(HSlider slider, Label lbl, Label helpIcon)
		{
			if (slider == null) return;
			slider.Editable = isAvailable;
			slider.Modulate = isAvailable ? Colors.White : new Color(0.65f, 0.65f, 0.65f, 0.6f);
			slider.TooltipText = isAvailable ? "" : noteTooltip;
			if (lbl != null)
			{
				lbl.Modulate = isAvailable ? Colors.White : new Color(0.75f, 0.75f, 0.75f, 0.7f);
			}
			if (helpIcon != null)
			{
				helpIcon.Visible = !isAvailable;
			}
		}

		SetSliderQuality(_sldNormalScale, _lblNormalScale, _iconHelpNormalScale);
		SetSliderQuality(_sldHeightScale, _lblHeightScale, _iconHelpHeightScale);
		SetSliderQuality(_sldHeightOffset, _lblHeightOffset, _iconHelpHeightOffset);
		SetSliderQuality(_sldCrevicePower, _lblCrevicePower, _iconHelpCrevicePower);
	}

	private void UpdateTileModeVisibility()
	{
		bool isStochastic = string.Equals(_tileMode, "Stochastic", StringComparison.OrdinalIgnoreCase);
		if (_sldStochasticTileSize?.GetParent() is Control stochRow)
		{
			stochRow.Visible = isStochastic;
		}
		if (_sldCrossFade?.GetParent() is Control crossFadeRow)
		{
			crossFadeRow.Visible = !isStochastic;
		}
	}

	private void ApplyLiveTerrainUpdate()
	{
		if (string.IsNullOrEmpty(_textureFileName)) return;

		string tintHex = $"#{_tint.ToHtml(false)}";
		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(
				_textureFileName,
				_tileMode,
				_uvScale,
				_stochasticTileSize,
				_crossFade,
				_brightness,
				tintHex,
				_heightScale,
				_heightOffset,
				_crevicePower,
				_normalScale,
				_roughnessScale
			);
		}
	}

	public void OpenForTexture(string fileName, JsonObject textureData, Action<JsonObject> onApplied)
	{
		_textureFileName = fileName ?? string.Empty;
		_onApplied = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Edit Texture Swatch")} - {_textureFileName}";

		RuntimeTerrain.ActiveSwatchConfig activeConfig = default;
		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			activeConfig = GameHost.Instance.GroundTerrain.GetActiveSwatchConfig(_textureFileName);
		}
		else
		{
			activeConfig = new RuntimeTerrain.ActiveSwatchConfig
			{
				TileMode = "Stochastic",
				UvScale = 1.0f,
				StochasticTileSize = 1.0f,
				CrossFade = 0.0f,
				HeightScale = 1.0f,
				HeightOffset = 0.0f,
				CrevicePower = 1.0f,
				NormalScale = 1.0f,
				RoughnessScale = 1.0f,
				Brightness = 1.0f,
				Tint = Colors.White
			};
		}

		_brightness = activeConfig.Brightness;
		_tint = activeConfig.Tint;
		_roughnessScale = activeConfig.RoughnessScale;
		_normalScale = activeConfig.NormalScale;
		_heightScale = activeConfig.HeightScale;
		_heightOffset = activeConfig.HeightOffset;
		_crevicePower = activeConfig.CrevicePower;
		_tileMode = activeConfig.TileMode;
		_uvScale = activeConfig.UvScale;
		_stochasticTileSize = activeConfig.StochasticTileSize;
		_crossFade = activeConfig.CrossFade;

		if (textureData != null)
		{
			string brightStr = textureData["Brightness"]?.ToString() ?? textureData["brightness"]?.ToString();
			if (!string.IsNullOrEmpty(brightStr) && float.TryParse(brightStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedB))
			{
				_brightness = parsedB;
			}

			string tintStr = textureData["Tint"]?.ToString() ?? textureData["tint"]?.ToString();
			if (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr))
			{
				_tint = Color.FromHtml(tintStr);
			}

			string roughStr = textureData["Roughness_Scale"]?.ToString() ?? textureData["roughness_scale"]?.ToString() ?? textureData["roughnessScale"]?.ToString();
			if (!string.IsNullOrEmpty(roughStr) && float.TryParse(roughStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedR))
			{
				_roughnessScale = parsedR;
			}

			string normStr = textureData["Normal_Scale"]?.ToString() ?? textureData["normal_scale"]?.ToString() ?? textureData["normalScale"]?.ToString();
			if (!string.IsNullOrEmpty(normStr) && float.TryParse(normStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedN))
			{
				_normalScale = parsedN;
			}

			string hsStr = textureData["Height_Scale"]?.ToString() ?? textureData["height_scale"]?.ToString() ?? textureData["heightScale"]?.ToString();
			if (!string.IsNullOrEmpty(hsStr) && float.TryParse(hsStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHs))
			{
				_heightScale = parsedHs;
			}

			string hoStr = textureData["Height_Offset"]?.ToString() ?? textureData["height_offset"]?.ToString() ?? textureData["heightOffset"]?.ToString();
			if (!string.IsNullOrEmpty(hoStr) && float.TryParse(hoStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHo))
			{
				_heightOffset = parsedHo;
			}

			string cpStr = textureData["Crevice_Power"]?.ToString() ?? textureData["crevice_power"]?.ToString() ?? textureData["crevicePower"]?.ToString();
			if (!string.IsNullOrEmpty(cpStr) && float.TryParse(cpStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCp))
			{
				_crevicePower = parsedCp;
			}

			string tmStr = textureData["Tile_Mode"]?.ToString() ?? textureData["tile_mode"]?.ToString() ?? textureData["tileMode"]?.ToString();
			if (!string.IsNullOrEmpty(tmStr))
			{
				_tileMode = string.Equals(tmStr, "Grid", StringComparison.OrdinalIgnoreCase) ? "Grid" : "Stochastic";
			}

			string uvStr = textureData["UV_Scale"]?.ToString() ?? textureData["uv_scale"]?.ToString() ?? textureData["uvScale"]?.ToString();
			if (!string.IsNullOrEmpty(uvStr) && float.TryParse(uvStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedUv))
			{
				_uvScale = parsedUv;
			}

			string stochStr = textureData["Stochastic_Tile_Size"]?.ToString() ?? textureData["stochastic_tile_size"]?.ToString() ?? textureData["stochasticTileSize"]?.ToString();
			if (!string.IsNullOrEmpty(stochStr) && float.TryParse(stochStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStoch))
			{
				_stochasticTileSize = parsedStoch;
			}

			string cfStr = textureData["Cross_Fade"]?.ToString() ?? textureData["cross_fade"]?.ToString() ?? textureData["Grid_Cross_Fade"]?.ToString() ?? textureData["grid_cross_fade"]?.ToString() ?? textureData["crossFade"]?.ToString();
			if (!string.IsNullOrEmpty(cfStr) && float.TryParse(cfStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCf))
			{
				_crossFade = parsedCf <= 0.10f && parsedCf > 0.0f ? parsedCf * 100.0f : parsedCf;
			}
		}

		_initialSnapshot = new TerrainTextureSnapshot
		{
			Brightness = _brightness,
			Tint = _tint,
			RoughnessScale = _roughnessScale,
			NormalScale = _normalScale,
			HeightScale = _heightScale,
			HeightOffset = _heightOffset,
			CrevicePower = _crevicePower,
			TileMode = _tileMode,
			UvScale = _uvScale,
			StochasticTileSize = _stochasticTileSize,
			CrossFade = _crossFade
		};

		if (_sldBrightness != null) _sldBrightness.Value = _brightness;
		if (_btnTint != null) _btnTint.Color = _tint;
		if (_sldRoughnessScale != null) _sldRoughnessScale.Value = _roughnessScale;
		if (_sldNormalScale != null) _sldNormalScale.Value = _normalScale;
		if (_sldHeightScale != null) _sldHeightScale.Value = _heightScale;
		if (_sldHeightOffset != null) _sldHeightOffset.Value = _heightOffset;
		if (_sldCrevicePower != null) _sldCrevicePower.Value = _crevicePower;
		if (_optTileMode != null) _optTileMode.Selected = _tileMode == "Stochastic" ? 1 : 0;
		if (_sldUvScale != null) _sldUvScale.Value = _uvScale;
		if (_sldStochasticTileSize != null) _sldStochasticTileSize.Value = _stochasticTileSize;
		if (_sldCrossFade != null) _sldCrossFade.Value = _crossFade;

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
		OpenDialog();
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		UpdateGraphicsQualityState();
	}

	protected override void OnApply()
	{
		var currentSnapshot = new TerrainTextureSnapshot
		{
			Brightness = _brightness,
			Tint = _tint,
			RoughnessScale = _roughnessScale,
			NormalScale = _normalScale,
			HeightScale = _heightScale,
			HeightOffset = _heightOffset,
			CrevicePower = _crevicePower,
			TileMode = _tileMode,
			UvScale = _uvScale,
			StochasticTileSize = _stochasticTileSize,
			CrossFade = _crossFade
		};

		if (_initialSnapshot != null)
		{
			var action = new TerrainTextureUndoAction(_textureFileName, _initialSnapshot, currentSnapshot);
			EditorHistoryManager.RecordAction(action);
		}

		var result = new JsonObject
		{
			["Brightness"] = _brightness,
			["Tint"] = $"#{_tint.ToHtml(false)}",
			["Roughness_Scale"] = _roughnessScale,
			["Normal_Scale"] = _normalScale,
			["Height_Scale"] = _heightScale,
			["Height_Offset"] = _heightOffset,
			["Crevice_Power"] = _crevicePower,
			["Tile_Mode"] = _tileMode,
			["UV_Scale"] = _uvScale,
			["Stochastic_Tile_Size"] = _stochasticTileSize,
			["Cross_Fade"] = _crossFade
		};

		_onApplied?.Invoke(result);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Texture swatch {0} updated successfully."), _textureFileName));
	}

	protected override void OnCancel()
	{
		if (_initialSnapshot != null)
		{
			_brightness = _initialSnapshot.Brightness;
			_tint = _initialSnapshot.Tint;
			_roughnessScale = _initialSnapshot.RoughnessScale;
			_normalScale = _initialSnapshot.NormalScale;
			_heightScale = _initialSnapshot.HeightScale;
			_heightOffset = _initialSnapshot.HeightOffset;
			_crevicePower = _initialSnapshot.CrevicePower;
			_tileMode = _initialSnapshot.TileMode;
			_uvScale = _initialSnapshot.UvScale;
			_stochasticTileSize = _initialSnapshot.StochasticTileSize;
			_crossFade = _initialSnapshot.CrossFade;

			if (_optTileMode != null) _optTileMode.Selected = _tileMode == "Stochastic" ? 1 : 0;
			UpdateTileModeVisibility();
			ApplyLiveTerrainUpdate();
		}
	}
}
