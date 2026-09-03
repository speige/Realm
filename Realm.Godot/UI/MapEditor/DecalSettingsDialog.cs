using Godot;
using System;
using System.IO;
using System.Text.Json.Nodes;

public class DecalSnapshot
{
	public float Brightness { get; set; } = 1.0f;
	public Color Tint { get; set; } = Colors.White;
	public float Contrast { get; set; } = 1.0f;
	public float Saturation { get; set; } = 1.0f;
	public float Opacity { get; set; } = 1.0f;
	public float AlbedoMix { get; set; } = 1.0f;
	public float NormalStrength { get; set; } = 0.0f;
	public float Roughness { get; set; } = 1.0f;
	public float Metallic { get; set; } = 0.0f;
	public string BlendMode { get; set; } = "Mix";

	public DecalSnapshot Clone()
	{
		return new DecalSnapshot
		{
			Brightness = this.Brightness,
			Tint = this.Tint,
			Contrast = this.Contrast,
			Saturation = this.Saturation,
			Opacity = this.Opacity,
			AlbedoMix = this.AlbedoMix,
			NormalStrength = this.NormalStrength,
			Roughness = this.Roughness,
			Metallic = this.Metallic,
			BlendMode = this.BlendMode
		};
	}
}

public partial class DecalSettingsDialog : FloatingDialogBase
{
	private string _decalKey = "";
	private float _brightness = 1.0f;
	private Color _tint = Colors.White;
	private float _contrast = 1.0f;
	private float _saturation = 1.0f;
	private float _opacity = 1.0f;
	private float _albedoMix = 1.0f;
	private float _normalStrength = 0.0f;
	private float _roughness = 1.0f;
	private float _metallic = 0.0f;
	private string _blendMode = "Mix";
	private bool _isSyncingControls = false;

	private struct DecalNodeState
	{
		public Color Modulate;
		public float AlbedoMix;
		public Texture2D TextureNormal;
		public Texture2D TextureOrm;
		public Texture2D TextureEmission;
		public float EmissionEnergy;
	}

	private readonly System.Collections.Generic.Dictionary<ulong, DecalNodeState> _originalDecalStates = new();

	private DecalSnapshot _initialSnapshot;
	private Action<JsonObject> _onApplied;

	private Label _lblDecalName;
	private TextureRect _previewRect;
	private Texture2D _baseTexture;

	private HSlider _sldBrightness;
	private Label _lblBrightness;
	private ColorPickerButton _btnTint;
	private HSlider _sldContrast;
	private Label _lblContrast;
	private HSlider _sldSaturation;
	private Label _lblSaturation;
	private HSlider _sldOpacity;
	private Label _lblOpacity;
	private HSlider _sldAlbedoMix;
	private Label _lblAlbedoMix;
	private HSlider _sldNormalStrength;
	private Label _lblNormalStrength;
	private HSlider _sldRoughness;
	private Label _lblRoughness;
	private HSlider _sldMetallic;
	private Label _lblMetallic;
	private OptionButton _optBlendMode;

	public DecalSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Decal Rendering & Blending Properties"), new Vector2(460, 660))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(540);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		_lblDecalName = new Label();
		_lblDecalName.AddThemeFontSizeOverride("font_size", 12);
		_lblDecalName.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.55f));
		_lblDecalName.HorizontalAlignment = HorizontalAlignment.Center;
		contentVBox.AddChild(_lblDecalName);

		// PREVIEW BOX
		var previewPanel = new PanelContainer();
		previewPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		previewPanel.CustomMinimumSize = new Vector2(0, 130);

		var previewVBox = new VBoxContainer();
		previewVBox.Alignment = BoxContainer.AlignmentMode.Center;

		_previewRect = new TextureRect();
		_previewRect.CustomMinimumSize = new Vector2(100, 100);
		_previewRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_previewRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_previewRect.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_previewRect.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		previewVBox.AddChild(_previewRect);
		previewPanel.AddChild(previewVBox);
		contentVBox.AddChild(previewPanel);

		// SECTION 1: COLOR & LIGHTING
		AddSectionHeader(contentVBox, "🎨 " + TranslationServer.Translate("COLOR & LIGHTING"), new Color(0.95f, 0.8f, 0.4f));

		(_sldBrightness, _lblBrightness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Brightness:"),
			0.1f,
			3.0f,
			0.05f,
			_brightness,
			(val) =>
			{
				if (_isSyncingControls) return;
				_brightness = val;
				UpdateLivePreviewAndWorld();
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
			if (_isSyncingControls) return;
			_tint = newCol;
			UpdateLivePreviewAndWorld();
		};
		rowTint.AddChild(_btnTint);
		contentVBox.AddChild(rowTint);

		(_sldContrast, _lblContrast) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Contrast:"),
			0.2f,
			2.5f,
			0.05f,
			_contrast,
			(val) =>
			{
				if (_isSyncingControls) return;
				_contrast = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);

		(_sldSaturation, _lblSaturation) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Saturation:"),
			0.0f,
			2.5f,
			0.05f,
			_saturation,
			(val) =>
			{
				if (_isSyncingControls) return;
				_saturation = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);

		// SECTION 2: BLENDING & TRANSPARENCY
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("BLENDING & MATERIAL"), new Color(0.5f, 0.85f, 1.0f));

		(_sldOpacity, _lblOpacity) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Opacity / Alpha:"),
			0.0f,
			1.0f,
			0.02f,
			_opacity,
			(val) =>
			{
				if (_isSyncingControls) return;
				_opacity = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		(_sldAlbedoMix, _lblAlbedoMix) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Albedo Mix:"),
			0.0f,
			1.0f,
			0.02f,
			_albedoMix,
			(val) =>
			{
				if (_isSyncingControls) return;
				_albedoMix = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		(_sldNormalStrength, _lblNormalStrength) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Normal Depth:"),
			0.0f,
			2.0f,
			0.05f,
			_normalStrength,
			(val) =>
			{
				if (_isSyncingControls) return;
				_normalStrength = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);

		(_sldRoughness, _lblRoughness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Roughness:"),
			0.0f,
			1.0f,
			0.05f,
			_roughness,
			(val) =>
			{
				if (_isSyncingControls) return;
				_roughness = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		(_sldMetallic, _lblMetallic) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Metallic:"),
			0.0f,
			1.0f,
			0.05f,
			_metallic,
			(val) =>
			{
				if (_isSyncingControls) return;
				_metallic = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		string[] blendModes = new[]
		{
			TranslationServer.Translate("Mix (Normal)").ToString(),
			TranslationServer.Translate("Additive").ToString(),
			TranslationServer.Translate("Multiply").ToString(),
			TranslationServer.Translate("Screen").ToString()
		};

		_optBlendMode = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Blend Mode:"),
			blendModes,
			0,
			(idx) =>
			{
				if (_isSyncingControls) return;
				_blendMode = idx switch
				{
					1 => "Additive",
					2 => "Multiply",
					3 => "Screen",
					_ => "Mix"
				};
				UpdateLivePreviewAndWorld();
			},
			140f
		);
	}

	public static JsonObject ResolveDecalMetadata(string decalKey, JsonObject? providedData = null)
	{
		var result = new JsonObject();
		if (providedData != null)
		{
			foreach (var kvp in providedData)
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}

		// 1. Check metadata.json if properties are missing
		if (!result.ContainsKey("brightness") && !result.ContainsKey("tint") && !result.ContainsKey("contrast") && !result.ContainsKey("roughness"))
		{
			try
			{
				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
				string metaPath = Path.Combine(wsPath, "metadata.json");
				if (File.Exists(metaPath))
				{
					var root = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
					var decalsObj = (root?["Assets"]?["decals"] ?? root?["MapProperties"]?["Assets"]?["decals"])?.AsObject();
					if (decalsObj != null)
					{
						string key = Path.GetFileName(decalKey);
						string baseKey = Path.GetFileNameWithoutExtension(decalKey);

						JsonObject? foundMeta = null;
						if (decalsObj.TryGetPropertyValue(key, out var n1) && n1 is JsonObject o1) foundMeta = o1;
						else if (decalsObj.TryGetPropertyValue(baseKey, out var n2) && n2 is JsonObject o2) foundMeta = o2;
						else if (decalsObj.TryGetPropertyValue($"{baseKey}.rtex", out var n3) && n3 is JsonObject o3) foundMeta = o3;
						else if (decalsObj.TryGetPropertyValue($"{baseKey}.png", out var n4) && n4 is JsonObject o4) foundMeta = o4;
						else if (decalsObj.TryGetPropertyValue($"{baseKey}.webp", out var n5) && n5 is JsonObject o5) foundMeta = o5;

						if (foundMeta != null)
						{
							foreach (var kvp in foundMeta)
							{
								if (!result.ContainsKey(kvp.Key))
								{
									result[kvp.Key] = kvp.Value?.DeepClone();
								}
							}
						}
					}
				}
			}
			catch { }
		}

		// 2. Check .rtex file metadata if still missing
		if (!result.ContainsKey("brightness") && !result.ContainsKey("tint"))
		{
			try
			{
				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
				string filename = Path.GetFileName(decalKey);
				string baseKey = Path.GetFileNameWithoutExtension(decalKey);
				string[] candidates = new[]
				{
					Path.Combine(wsPath, "Assets", "decals", filename),
					Path.Combine(wsPath, "Assets", "decals", $"{baseKey}.rtex"),
					Path.Combine(wsPath, "Assets", $"{baseKey}.rtex")
				};

				foreach (var path in candidates)
				{
					if (File.Exists(path) && path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						byte[] bytes = File.ReadAllBytes(path);
						if (Realm.Shared.Textures.RtexFile.IsRtexBytes(bytes))
						{
							var (customJson, _, _) = Realm.Shared.Textures.RtexFile.Parse(bytes);
							if (!string.IsNullOrEmpty(customJson))
							{
								var rtexMeta = JsonNode.Parse(customJson)?.AsObject();
								if (rtexMeta != null)
								{
									foreach (var kvp in rtexMeta)
									{
										if (!result.ContainsKey(kvp.Key))
										{
											result[kvp.Key] = kvp.Value?.DeepClone();
										}
									}
									break;
								}
							}
						}
					}
				}
			}
			catch { }
		}

		// 3. Inspect existing in-world Decal node if still missing
		if (!result.ContainsKey("brightness") && !result.ContainsKey("tint") && GameHost.Instance?.AllDecals != null)
		{
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);
			foreach (var d in GameHost.Instance.AllDecals)
			{
				if (d != null && GodotObject.IsInstanceValid(d))
				{
					string dId = d is Decal3D d3d ? d3d.DecalId : "";
					string dBase = Path.GetFileNameWithoutExtension(dId);
					if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
					{
						result["albedo_mix"] = d.AlbedoMix;
						result["tint"] = $"#{d.Modulate.ToHtml(false)}";
						result["opacity"] = d.Modulate.A;
						result["normal_strength"] = (d.TextureNormal != null) ? 1.0f : 0.0f;
						result["roughness"] = 1.0f;
						result["metallic"] = 0.0f;
						if (d.TextureEmission != null)
						{
							result["blend_mode"] = d.AlbedoMix <= 0.01f ? "Additive" : "Screen";
						}
						else
						{
							result["blend_mode"] = "Mix";
						}
						break;
					}
				}
			}
		}

		return result;
	}

	public void OpenForDecal(string decalKey, JsonObject decalData, Action<JsonObject> onApplied)
	{
		_decalKey = decalKey;
		_onApplied = onApplied;
		_lblDecalName.Text = "Decal: " + decalKey;

		var resolvedData = ResolveDecalMetadata(decalKey, decalData);

		_brightness = resolvedData.TryGetPropertyValue("brightness", out var bNode) && float.TryParse(bNode?.ToString(), out float b) ? b : 1.0f;
		_contrast = resolvedData.TryGetPropertyValue("contrast", out var cNode) && float.TryParse(cNode?.ToString(), out float c) ? c : 1.0f;
		_saturation = resolvedData.TryGetPropertyValue("saturation", out var sNode) && float.TryParse(sNode?.ToString(), out float s) ? s : 1.0f;
		_opacity = resolvedData.TryGetPropertyValue("opacity", out var oNode) && float.TryParse(oNode?.ToString(), out float o) ? o : 1.0f;
		_albedoMix = resolvedData.TryGetPropertyValue("albedo_mix", out var mNode) && float.TryParse(mNode?.ToString(), out float m) ? m : 1.0f;
		_normalStrength = resolvedData.TryGetPropertyValue("normal_strength", out var nNode) && float.TryParse(nNode?.ToString(), out float n) ? n : 0.0f;
		_roughness = resolvedData.TryGetPropertyValue("roughness", out var rNode) && float.TryParse(rNode?.ToString(), out float r) ? r : 1.0f;
		_metallic = resolvedData.TryGetPropertyValue("metallic", out var metNode) && float.TryParse(metNode?.ToString(), out float met) ? met : 0.0f;
		_blendMode = resolvedData.TryGetPropertyValue("blend_mode", out var bmNode) ? bmNode?.ToString() ?? "Mix" : "Mix";

		_tint = Colors.White;
		if (resolvedData.TryGetPropertyValue("tint", out var tNode) && tNode != null)
		{
			string tStr = tNode.ToString();
			if (tStr.StartsWith("#")) _tint = Color.FromHtml(tStr);
		}

		_originalDecalStates.Clear();
		if (GameHost.Instance?.AllDecals != null)
		{
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);
			foreach (var d in GameHost.Instance.AllDecals)
			{
				if (d != null && GodotObject.IsInstanceValid(d))
				{
					string dId = d is Decal3D d3d ? d3d.DecalId : "";
					string dBase = Path.GetFileNameWithoutExtension(dId);
					if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
					{
						_originalDecalStates[d.GetInstanceId()] = new DecalNodeState
						{
							Modulate = d.Modulate,
							AlbedoMix = d.AlbedoMix,
							TextureNormal = d.TextureNormal,
							TextureOrm = d.TextureOrm,
							TextureEmission = d.TextureEmission,
							EmissionEnergy = d.EmissionEnergy
						};
					}
				}
			}
		}

		_initialSnapshot = new DecalSnapshot
		{
			Brightness = _brightness,
			Tint = _tint,
			Contrast = _contrast,
			Saturation = _saturation,
			Opacity = _opacity,
			AlbedoMix = _albedoMix,
			NormalStrength = _normalStrength,
			Roughness = _roughness,
			Metallic = _metallic,
			BlendMode = _blendMode
		};

		SyncControlsWithValues();

		_baseTexture = GameHost.Instance?.LoadDecalTexture(decalKey);
		if (_baseTexture == null)
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			string p = Path.Combine(wsPath, "Assets", "decals", decalKey);
			if (File.Exists(p))
			{
				var img = Image.LoadFromFile(p);
				if (img != null) _baseTexture = ImageTexture.CreateFromImage(img);
			}
		}

		UpdateLivePreviewAndWorld();
		OpenDialog();
	}

	private void SyncControlsWithValues()
	{
		_isSyncingControls = true;
		try
		{
			_sldBrightness.Value = _brightness;
			_lblBrightness.Text = $"{_brightness:F2}x";
			_btnTint.Color = _tint;
			_sldContrast.Value = _contrast;
			_lblContrast.Text = $"{_contrast:F2}x";
			_sldSaturation.Value = _saturation;
			_lblSaturation.Text = $"{_saturation:F2}x";
			_sldOpacity.Value = _opacity;
			_lblOpacity.Text = $"{_opacity:F2}";
			_sldAlbedoMix.Value = _albedoMix;
			_lblAlbedoMix.Text = $"{_albedoMix:F2}";
			_sldNormalStrength.Value = _normalStrength;
			_lblNormalStrength.Text = $"{_normalStrength:F2}x";
			_sldRoughness.Value = _roughness;
			_lblRoughness.Text = $"{_roughness:F2}";
			_sldMetallic.Value = _metallic;
			_lblMetallic.Text = $"{_metallic:F2}";

			_optBlendMode.Selected = _blendMode switch
			{
				"Additive" => 1,
				"Multiply" => 2,
				"Screen" => 3,
				_ => 0
			};
		}
		finally
		{
			_isSyncingControls = false;
		}
	}

	private void UpdateLivePreviewAndWorld()
	{
		_lblBrightness.Text = $"{_brightness:F2}x";
		_lblContrast.Text = $"{_contrast:F2}x";
		_lblSaturation.Text = $"{_saturation:F2}x";
		_lblOpacity.Text = $"{_opacity:F2}";
		_lblAlbedoMix.Text = $"{_albedoMix:F2}";
		_lblNormalStrength.Text = $"{_normalStrength:F2}x";
		_lblRoughness.Text = $"{_roughness:F2}";
		_lblMetallic.Text = $"{_metallic:F2}";

		if (_previewRect != null)
		{
			_previewRect.Texture = _baseTexture;

			float r = _tint.R * _brightness;
			float g = _tint.G * _brightness;
			float b = _tint.B * _brightness;

			float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
			r = lum + (r - lum) * _saturation;
			g = lum + (g - lum) * _saturation;
			b = lum + (b - lum) * _saturation;

			r = (r - 0.5f) * _contrast + 0.5f;
			g = (g - 0.5f) * _contrast + 0.5f;
			b = (b - 0.5f) * _contrast + 0.5f;

			_previewRect.Modulate = new Color(Mathf.Clamp(r, 0f, 4f), Mathf.Clamp(g, 0f, 4f), Mathf.Clamp(b, 0f, 4f), Mathf.Clamp(_opacity, 0f, 1f));

			if (_previewRect.Material is not CanvasItemMaterial mat)
			{
				mat = new CanvasItemMaterial();
				_previewRect.Material = mat;
			}
			mat.BlendMode = _blendMode switch
			{
				"Additive" => CanvasItemMaterial.BlendModeEnum.Add,
				"Multiply" => CanvasItemMaterial.BlendModeEnum.Mul,
				"Screen" => CanvasItemMaterial.BlendModeEnum.Add,
				_ => CanvasItemMaterial.BlendModeEnum.Mix
			};
		}

		GameHost.Instance?.RefreshDecalsLive(
			_decalKey,
			_brightness,
			_tint,
			_contrast,
			_saturation,
			_opacity,
			_albedoMix,
			_normalStrength,
			_roughness,
			_metallic,
			_blendMode
		);
	}

	protected override void OnApply()
	{
		var result = new JsonObject
		{
			["brightness"] = Math.Round(_brightness, 3),
			["tint"] = $"#{_tint.ToHtml(false)}",
			["contrast"] = Math.Round(_contrast, 3),
			["saturation"] = Math.Round(_saturation, 3),
			["opacity"] = Math.Round(_opacity, 3),
			["albedo_mix"] = Math.Round(_albedoMix, 3),
			["normal_strength"] = Math.Round(_normalStrength, 3),
			["roughness"] = Math.Round(_roughness, 3),
			["metallic"] = Math.Round(_metallic, 3),
			["blend_mode"] = _blendMode,
			["asset_type"] = "Decal"
		};

		_onApplied?.Invoke(result);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved decal properties for '{0}'"), _decalKey));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		if (_originalDecalStates.Count > 0 && GameHost.Instance?.AllDecals != null)
		{
			foreach (var d in GameHost.Instance.AllDecals)
			{
				if (d != null && GodotObject.IsInstanceValid(d) && _originalDecalStates.TryGetValue(d.GetInstanceId(), out var orig))
				{
					d.Modulate = orig.Modulate;
					d.AlbedoMix = orig.AlbedoMix;
					d.TextureNormal = orig.TextureNormal;
					d.TextureOrm = orig.TextureOrm;
					d.TextureEmission = orig.TextureEmission;
					d.EmissionEnergy = orig.EmissionEnergy;
				}
			}
		}
		else if (_initialSnapshot != null)
		{
			_brightness = _initialSnapshot.Brightness;
			_tint = _initialSnapshot.Tint;
			_contrast = _initialSnapshot.Contrast;
			_saturation = _initialSnapshot.Saturation;
			_opacity = _initialSnapshot.Opacity;
			_albedoMix = _initialSnapshot.AlbedoMix;
			_normalStrength = _initialSnapshot.NormalStrength;
			_roughness = _initialSnapshot.Roughness;
			_metallic = _initialSnapshot.Metallic;
			_blendMode = _initialSnapshot.BlendMode;

			UpdateLivePreviewAndWorld();
		}
		base.OnCancel();
	}
}
