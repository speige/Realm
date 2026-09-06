using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class VfxStudioDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewSceneRoot;
	private ProceduralVfxInstance3D _previewVfxInstance;
	private MeshInstance3D _previewGroundGrid;

	private LineEdit _txtVfxId;
	private LineEdit _txtVfxName;
	private OptionButton _optPrimitive;
	private OptionButton _optBlendMode;
	private OptionButton _optPlacementMode;
	private OptionButton _optSocket;

	private VfxAttachmentConfig _currentConfig = new();
	private VfxAttachmentConfig _initialConfig = new();
	private Action<VfxAttachmentConfig> _onAppliedCallback;
	private bool _isUpdatingUI;

	private float _cameraDistance = 4.0f;
	private const float DefaultDistance = 4.0f;
	private float _cameraYaw = Mathf.DegToRad(30.0f);
	private float _cameraPitch = Mathf.DegToRad(20.0f);
	private Vector3 _targetPosition = new Vector3(0.0f, 0.5f, 0.0f);
	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;

	public VfxStudioDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Procedural VFX Studio (Uber-Shader & Attachments)"), new Vector2(560, 780))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(530, 220), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		_previewSceneRoot = new Node3D { Name = "VfxPreviewRoot" };
		_subViewport.AddChild(_previewSceneRoot);

		CreatePreviewEnvironment();

		var topToolbar = new HBoxContainer();
		topToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(topToolbar, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View front", 10, new Vector2(0, 22));
		AddButton(topToolbar, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View side", 10, new Vector2(0, 22));
		AddButton(topToolbar, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric 3/4 view", 10, new Vector2(0, 22));
		AddButton(topToolbar, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(topToolbar, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera", 10, new Vector2(0, 22));

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		topToolbar.AddChild(spacer);

		AddButton(topToolbar, TranslationServer.Translate("Toggle Grid"), () =>
		{
			if (_previewGroundGrid != null && GodotObject.IsInstanceValid(_previewGroundGrid))
			{
				_previewGroundGrid.Visible = !_previewGroundGrid.Visible;
			}
		}, "Toggle preview ground plane", 10, new Vector2(0, 22));

		BodyContainer.AddChild(topToolbar);

		var scrollBody = CreateScrollBody(440);

		AddSectionHeader(scrollBody, "⚡ " + TranslationServer.Translate("PRESETS & PRIMITIVE SHAPE"));

		var presetsRow = new HBoxContainer();
		presetsRow.AddThemeConstantOverride("separation", 4);
		AddLabel(presetsRow, TranslationServer.Translate("Presets:"), 11, UIStyle.ColorGoldDull);
		AddButton(presetsRow, "🔥 " + TranslationServer.Translate("Fire Blade"), () => ApplyPreset("fire_blade"), "Fire blade preset", 10);
		AddButton(presetsRow, "🌀 " + TranslationServer.Translate("Arcane Portal"), () => ApplyPreset("arcane_portal"), "Arcane portal preset", 10);
		AddButton(presetsRow, "⚡ " + TranslationServer.Translate("Lightning"), () => ApplyPreset("lightning_blade"), "Lightning blade preset", 10);
		AddButton(presetsRow, "🛡️ " + TranslationServer.Translate("Shield"), () => ApplyPreset("holy_shield"), "Divine shield preset", 10);
		AddButton(presetsRow, "❄️ " + TranslationServer.Translate("Frost Rune"), () => ApplyPreset("frost_rune"), "Frost rune preset", 10);
		AddButton(presetsRow, "🧪 " + TranslationServer.Translate("Poison"), () => ApplyPreset("poison_ring"), "Poison ring preset", 10);
		AddButton(presetsRow, "💡 " + TranslationServer.Translate("Light Shaft"), () => ApplyPreset("light_shaft"), "Light shaft preset", 10);
		scrollBody.AddChild(presetsRow);

		_txtVfxId = AddTextInput(scrollBody, TranslationServer.Translate("VFX ID:"), _currentConfig.VfxId, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.VfxId = val;
		}, "Unique identifier e.g. vfx_fire_blade", 140f);

		_txtVfxName = AddTextInput(scrollBody, TranslationServer.Translate("Display Name:"), _currentConfig.Name, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.Name = val;
		}, "Human-readable name", 140f);

		string[] primitiveNames = Enum.GetNames<VfxPrimitiveType>();
		_optPrimitive = AddOptionDropdown(scrollBody, TranslationServer.Translate("Primitive Shape"), primitiveNames, (int)_currentConfig.PrimitiveType, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PrimitiveType = (VfxPrimitiveType)idx;
			RestartPreviewVfx();
		}, 140f);

		string[] blendModes = Enum.GetNames<VfxBlendMode>();
		_optBlendMode = AddOptionDropdown(scrollBody, TranslationServer.Translate("Blend Mode"), blendModes, (int)_currentConfig.BlendMode, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BlendMode = (VfxBlendMode)idx;
			RestartPreviewVfx();
		}, 140f);

		string[] placementModes = Enum.GetNames<VfxPlacementMode>();
		_optPlacementMode = AddOptionDropdown(scrollBody, TranslationServer.Translate("Placement Mode"), placementModes, (int)_currentConfig.PlacementMode, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PlacementMode = (VfxPlacementMode)idx;
		}, 140f);

		string[] socketNames = new[] { "Standalone (World)", "RightHand (Weapon)", "LeftHand (Offhand)", "Chest (Torso Aura)", "Root (Feet/Ground)", "Head (Crown)", "LeftFoot", "RightFoot" };
		int currentSocketIdx = GetSocketIndex(_currentConfig.TargetSocket);
		_optSocket = AddOptionDropdown(scrollBody, TranslationServer.Translate("Socket / Bone Bind"), socketNames, currentSocketIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.TargetSocket = GetSocketKeyFromIndex(idx);
		}, 140f);

		AddSectionHeader(scrollBody, "🖼️ " + TranslationServer.Translate("SLOT 1: BASE SHAPE MASK (SILHOUETTE)"), new Color(0.85f, 0.6f, 0.35f));

		AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Base Texture (.rtex)"),
			_currentConfig.BaseTexture,
			(all) => ScanTextureAssets(true),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentConfig.BaseTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select ribbon/decal/texture asset..."),
			140f,
			true
		);

		AddCheckBox(scrollBody, TranslationServer.Translate("Luminance to Alpha"), _currentConfig.LuminanceToAlpha, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LuminanceToAlpha = pressed;
			RestartPreviewVfx();
		}, "Derive transparency from texture brightness to strip black/dark backgrounds automatically");

		AddSlider(scrollBody, TranslationServer.Translate("Luminance Threshold"), 0.0f, 1.0f, 0.01f, _currentConfig.LuminanceThreshold, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LuminanceThreshold = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Threshold Smoothness"), 0.001f, 0.5f, 0.01f, _currentConfig.LuminanceSmoothness, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LuminanceSmoothness = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Convert to Grayscale"), _currentConfig.UseGrayscale, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.UseGrayscale = pressed;
			RestartPreviewVfx();
		});

		AddCheckBox(scrollBody, TranslationServer.Translate("Invert Mask Colors"), _currentConfig.InvertMask, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.InvertMask = pressed;
			RestartPreviewVfx();
		});

		AddSlider(scrollBody, TranslationServer.Translate("High-Pass Cutoff"), 0.0f, 1.0f, 0.02f, _currentConfig.HighPassCutoff, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.HighPassCutoff = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("Base UV Scroll (X, Y)"), _currentConfig.BaseUvScroll, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseUvScroll = val;
			RestartPreviewVfx();
		}, 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("Base UV Scale (X, Y)"), _currentConfig.BaseUvScale, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseUvScale = val;
			RestartPreviewVfx();
		}, 140f);

		AddSectionHeader(scrollBody, "🌪️ " + TranslationServer.Translate("SLOT 2: DISTORTION & NOISE MASK"), new Color(0.35f, 0.75f, 0.85f));

		AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Noise Texture (.rtex)"),
			_currentConfig.NoiseTexture,
			(all) => ScanNoiseAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentConfig.NoiseTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select noise texture... (or leave empty for procedural noise)"),
			140f
		);

		AddSlider(scrollBody, TranslationServer.Translate("Distortion Strength"), 0.0f, 2.0f, 0.02f, _currentConfig.DistortionStrength, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.DistortionStrength = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("Noise UV Scroll (X, Y)"), _currentConfig.NoiseUvScroll, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.NoiseUvScroll = val;
			RestartPreviewVfx();
		}, 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("Noise UV Scale (X, Y)"), _currentConfig.NoiseUvScale, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.NoiseUvScale = val;
			RestartPreviewVfx();
		}, 140f);

		AddSectionHeader(scrollBody, "🔥 " + TranslationServer.Translate("COLOR & HEAT HIERARCHY"), new Color(0.95f, 0.45f, 0.25f));

		AddColorPicker(scrollBody, TranslationServer.Translate("Base Color"), VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		AddColorPicker(scrollBody, TranslationServer.Translate("Secondary / Rim Color"), VfxShaderManager.ParseColorSafe(_currentConfig.SecondaryColor, Colors.DarkRed), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SecondaryColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		AddColorPicker(scrollBody, TranslationServer.Translate("Inner Core Color"), VfxShaderManager.ParseColorSafe(_currentConfig.CoreColor, Colors.White), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.CoreColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Emission Boost"), 0.0f, 15.0f, 0.2f, _currentConfig.EmissionBoost, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EmissionBoost = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Core Threshold"), 0.0f, 1.0f, 0.02f, _currentConfig.CoreThreshold, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.CoreThreshold = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSectionHeader(scrollBody, "🌊 " + TranslationServer.Translate("FADING & DISSOLVE SUITE"), new Color(0.5f, 0.85f, 0.65f));

		AddCheckBox(scrollBody, TranslationServer.Translate("Radial Falloff (Edge Fade)"), _currentConfig.EnableRadialFalloff, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableRadialFalloff = pressed;
			RestartPreviewVfx();
		}, "Softens perimeter edges of discs and quads to prevent hard clipping");

		AddSlider(scrollBody, TranslationServer.Translate("Radial Falloff Start"), 0.0f, 1.0f, 0.02f, _currentConfig.RadialFalloffStart, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RadialFalloffStart = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Radial Falloff End"), 0.0f, 1.0f, 0.02f, _currentConfig.RadialFalloffEnd, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RadialFalloffEnd = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Length Fade (Erosion)"), _currentConfig.EnableLengthFade, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableLengthFade = pressed;
			RestartPreviewVfx();
		}, "Gradient falloff along UV length to pinch off flame tongues and dissolve trails");

		AddSlider(scrollBody, TranslationServer.Translate("Length Fade Start"), 0.0f, 1.0f, 0.02f, _currentConfig.LengthFadeStart, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LengthFadeStart = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Length Fade End"), 0.0f, 1.0f, 0.02f, _currentConfig.LengthFadeEnd, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LengthFadeEnd = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Erosion Progress"), 0.0f, 1.0f, 0.02f, _currentConfig.ErosionProgress, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ErosionProgress = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Fresnel / Rim Glow"), _currentConfig.EnableFresnel, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableFresnel = pressed;
			RestartPreviewVfx();
		}, "View-angle falloff for luminous shields, force fields, and domes");

		AddSlider(scrollBody, TranslationServer.Translate("Fresnel Power"), 0.1f, 10.0f, 0.1f, _currentConfig.FresnelPower, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.FresnelPower = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Fresnel Intensity"), 0.0f, 10.0f, 0.2f, _currentConfig.FresnelIntensity, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.FresnelIntensity = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Depth Fade (Soft Intersect)"), _currentConfig.EnableDepthFade, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableDepthFade = pressed;
			RestartPreviewVfx();
		}, "Eliminates hard seams where VFX intersects terrain or geometry");

		AddSlider(scrollBody, TranslationServer.Translate("Depth Fade Distance"), 0.0f, 5.0f, 0.05f, _currentConfig.DepthFadeDistance, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.DepthFadeDistance = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSectionHeader(scrollBody, "📐 " + TranslationServer.Translate("TRANSFORM & SURFACE OFFSET"), new Color(0.75f, 0.65f, 0.95f));

		AddSlider(scrollBody, TranslationServer.Translate("Surface Normal Offset"), -0.2f, 0.5f, 0.005f, _currentConfig.SurfaceNormalOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SurfaceNormalOffset = val;
			RestartPreviewVfx();
		}, "0.000", 140f);

		AddVector3Input(scrollBody, TranslationServer.Translate("Position Offset"), _currentConfig.PositionOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PositionOffset = val;
			RestartPreviewVfx();
		}, 140f);

		AddVector3Input(scrollBody, TranslationServer.Translate("Rotation (Pitch, Yaw, Roll)"), _currentConfig.RotationOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RotationOffset = val;
			RestartPreviewVfx();
		}, 140f);

		AddVector3Input(scrollBody, TranslationServer.Translate("Non-Uniform Scale"), _currentConfig.ScaleOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ScaleOffset = val;
			RestartPreviewVfx();
		}, 140f);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("SAVE & APPLY");
	}

	private void CreatePreviewEnvironment()
	{
		_previewGroundGrid = new MeshInstance3D();
		_previewGroundGrid.Name = "GroundGrid";
		var planeMesh = new PlaneMesh { Size = new Vector2(10f, 10f), SubdivideWidth = 10, SubdivideDepth = 10 };
		_previewGroundGrid.Mesh = planeMesh;
		var gridMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.12f, 0.14f, 0.18f, 0.85f),
			Roughness = 0.8f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		_previewGroundGrid.MaterialOverride = gridMat;
		_previewGroundGrid.Position = new Vector3(0, -0.01f, 0);
		_previewSceneRoot.AddChild(_previewGroundGrid);

		_previewVfxInstance = new ProceduralVfxInstance3D();
		_previewVfxInstance.Name = "PreviewVfx";
		_previewSceneRoot.AddChild(_previewVfxInstance);
		_previewVfxInstance.Initialize(_currentConfig);
	}

	public void OpenForConfig(VfxAttachmentConfig config, Action<VfxAttachmentConfig> onApplied = null)
	{
		_initialConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_currentConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_onAppliedCallback = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Procedural VFX Studio")} - {(!string.IsNullOrEmpty(_currentConfig.Name) ? _currentConfig.Name : _currentConfig.VfxId)}";

		UpdateUIFromCurrentConfig();
		OpenDialog();
		ResetCameraDefault();
		RestartPreviewVfx();
	}

	private void UpdateUIFromCurrentConfig()
	{
		_isUpdatingUI = true;
		try
		{
			if (_txtVfxId != null) _txtVfxId.Text = _currentConfig.VfxId;
			if (_txtVfxName != null) _txtVfxName.Text = _currentConfig.Name;
			if (_optPrimitive != null) _optPrimitive.Selected = (int)_currentConfig.PrimitiveType;
			if (_optBlendMode != null) _optBlendMode.Selected = (int)_currentConfig.BlendMode;
			if (_optPlacementMode != null) _optPlacementMode.Selected = (int)_currentConfig.PlacementMode;
			if (_optSocket != null) _optSocket.Selected = GetSocketIndex(_currentConfig.TargetSocket);
		}
		finally
		{
			_isUpdatingUI = false;
		}
	}

	public void RestartPreviewVfx()
	{
		if (_previewVfxInstance != null && GodotObject.IsInstanceValid(_previewVfxInstance))
		{
			_previewVfxInstance.UpdateConfig(_currentConfig);
			_previewVfxInstance.Position = _currentConfig.PositionOffset;
			_previewVfxInstance.RotationDegrees = _currentConfig.RotationOffset;
			_previewVfxInstance.Scale = _currentConfig.ScaleOffset;
		}
	}

	private void ApplyPreset(string presetName)
	{
		var preset = VfxAttachmentConfig.CreatePreset(presetName);
		preset.VfxId = _currentConfig.VfxId;
		preset.Name = _currentConfig.Name;
		_currentConfig = preset;
		UpdateUIFromCurrentConfig();
		RestartPreviewVfx();
	}

	private static int GetSocketIndex(string socketKey)
	{
		return (socketKey?.ToLowerInvariant()) switch
		{
			"righthand" or "weapon" => 1,
			"lefthand" or "offhand" => 2,
			"chest" or "torso" => 3,
			"root" or "feet" or "hips" => 4,
			"head" or "crown" => 5,
			"leftfoot" => 6,
			"rightfoot" => 7,
			_ => 0
		};
	}

	private static string GetSocketKeyFromIndex(int index)
	{
		return index switch
		{
			1 => "RightHand",
			2 => "LeftHand",
			3 => "Chest",
			4 => "Root",
			5 => "Head",
			6 => "LeftFoot",
			7 => "RightFoot",
			_ => "Standalone"
		};
	}

	private List<string> ScanTextureAssets(bool includeAll)
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		void CollectFromDir(string subFolder)
		{
			string dir = Path.Combine(wsPath, "Assets", subFolder);
			if (Directory.Exists(dir))
			{
				foreach (var file in Directory.GetFiles(dir, "*.*"))
				{
					if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
					    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
					    file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(file));
					}
				}
			}
		}

		CollectFromDir("ribbons");
		CollectFromDir("decals");
		CollectFromDir("textures");

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private List<string> ScanNoiseAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		string dir = Path.Combine(wsPath, "Assets", "noise");
		if (Directory.Exists(dir))
		{
			foreach (var file in Directory.GetFiles(dir, "*.*"))
			{
				if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
				    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	protected override void OnApply()
	{
		Hud?.SaveCustomVfxToMetadata(_currentConfig.VfxId, _currentConfig);
		_onAppliedCallback?.Invoke(_currentConfig);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved VFX '{0}' to metadata.json"), _currentConfig.Name));
	}

	protected override void OnCancel()
	{
		_currentConfig = _initialConfig.Clone();
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
		_cameraDistance = Mathf.Clamp(_cameraDistance * factor, DefaultDistance * 0.15f, DefaultDistance * 6.0f);
		UpdateCameraTransform();
	}

	public void SetCameraPreset(float yawDegrees, float pitchDegrees)
	{
		_cameraYaw = Mathf.DegToRad(yawDegrees);
		_cameraPitch = Mathf.DegToRad(pitchDegrees);
		_targetPosition = new Vector3(0.0f, 0.5f, 0.0f);
		UpdateCameraTransform();
	}

	public void ResetCameraDefault()
	{
		_cameraDistance = DefaultDistance;
		_targetPosition = new Vector3(0.0f, 0.5f, 0.0f);
		_cameraYaw = Mathf.DegToRad(30.0f);
		_cameraPitch = Mathf.DegToRad(20.0f);
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
		if (newPos.DistanceSquaredTo(_targetPosition) > 0.0001f)
		{
			Vector3 dir = (_targetPosition - newPos).Normalized();
			Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			_camera.LookAtFromPosition(newPos, _targetPosition, up);
		}
	}
}
