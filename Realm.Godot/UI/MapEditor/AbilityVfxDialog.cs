using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class AbilityVfxDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _simRoot;
	private AnimatedSprite3D _vfxSprite;
	private MeshInstance3D _aoeRingMesh;
	private MeshInstance3D _aoeDiskMesh;
	private AudioStreamPlayer _sfxPlayer;

	private TextureRect _iconPreviewRect;
	private LineEdit _txtIconPath;
	private Action<string> _setIconPathValue;

	private LineEdit _txtVisualEffect;
	private Action<string> _setVisualEffectValue;
	private HSlider _sldAoeRadius;
	private Label _lblAoeRadiusVal;
	private LineEdit _txtCastSound;
	private Action<string> _setCastSoundValue;

	private string _abilityId = "";
	private string _abilityName = "";
	private string _initialVisualEffect = "";
	private string _initialCastSound = "";
	private string _initialIconPath = "";
	private float _initialAoeRadius = 0.0f;

	private string _currentVisualEffect = "";
	private string _currentCastSound = "";
	private string _currentIconPath = "";
	private float _currentAoeRadius = 4.0f;
	private float _playbackSpeed = 1.0f;
	private bool _isPaused = false;
	private Action<JsonObject> _onApplied;

	private float _defaultDistance = 8.0f;
	private float _cameraDistance = 8.0f;
	private float _defaultYaw = Mathf.DegToRad(45.0f);
	private float _defaultPitch = Mathf.DegToRad(30.0f);
	private float _cameraYaw = Mathf.DegToRad(45.0f);
	private float _cameraPitch = Mathf.DegToRad(30.0f);
	private Vector3 _targetPosition = Vector3.Zero;

	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;

	public AbilityVfxDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Ability VFX & Audio Studio"), new Vector2(500, 720))
	{
		_sfxPlayer = new AudioStreamPlayer();
		AddChild(_sfxPlayer);

		BuildControls();
	}

	private void BuildControls()
	{
		// 3D VIEWPORT
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(480, 230), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		Setup3DEnvironment();

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		// ROW 1: CAMERA PRESETS & CAST TEST
		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Camera:");
		lblPreset.AddThemeFontSizeOverride("font_size", 10);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		presetRow.AddChild(lblPreset);

		AddButton(presetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 15f), "Front view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 15f), "Side view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 30f), "Isometric view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera", 10, new Vector2(0, 22));

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		presetRow.AddChild(spacer);

		AddButton(presetRow, "🔥 " + TranslationServer.Translate("Cast Test"), () => TriggerCastTest(), "Simulate casting ability VFX & sound", 10, new Vector2(90, 22));

		topControlsVBox.AddChild(presetRow);

		// ROW 2: PLAYBACK CONTROLS
		var playbackRow = new HBoxContainer();
		playbackRow.AddThemeConstantOverride("separation", 6);

		AddButton(playbackRow, "▶ " + TranslationServer.Translate("Play"), () => PlayVfxAnimation(), "Play VFX loop", 10, new Vector2(50, 22));
		AddButton(playbackRow, "⏸ " + TranslationServer.Translate("Pause"), () => PauseVfxAnimation(), "Pause VFX", 10, new Vector2(50, 22));
		AddButton(playbackRow, "⏹ " + TranslationServer.Translate("Stop"), () => StopVfxAnimation(), "Stop VFX", 10, new Vector2(50, 22));

		var lblSpeed = new Label();
		lblSpeed.Text = TranslationServer.Translate("Speed:");
		lblSpeed.AddThemeFontSizeOverride("font_size", 10);
		lblSpeed.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playbackRow.AddChild(lblSpeed);

		var sldSpeed = new HSlider();
		sldSpeed.MinValue = 0.25;
		sldSpeed.MaxValue = 2.50;
		sldSpeed.Step = 0.05;
		sldSpeed.Value = 1.0;
		sldSpeed.CustomMinimumSize = new Vector2(90, 0);
		sldSpeed.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		sldSpeed.ValueChanged += (val) =>
		{
			_playbackSpeed = (float)val;
			if (_vfxSprite != null && _vfxSprite.SpriteFrames != null && _vfxSprite.SpriteFrames.HasAnimation("play"))
			{
				_vfxSprite.SpeedScale = _playbackSpeed;
			}
		};
		playbackRow.AddChild(sldSpeed);

		topControlsVBox.AddChild(playbackRow);

		// SCROLLABLE CONFIGURATION SECTIONS
		var scrollBody = CreateScrollBody(340);
		var configVBox = new VBoxContainer();
		configVBox.AddThemeConstantOverride("separation", 10);
		configVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(configVBox);

		// SECTION 1: ABILITY ICON
		AddSectionHeader(configVBox, "🎨 " + TranslationServer.Translate("ABILITY ICON"), new Color(0.95f, 0.8f, 0.4f));

		var iconRow = new HBoxContainer();
		iconRow.AddThemeConstantOverride("separation", 8);

		var iconPanel = new PanelContainer();
		iconPanel.CustomMinimumSize = new Vector2(48, 48);
		iconPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		_iconPreviewRect = new TextureRect();
		_iconPreviewRect.CustomMinimumSize = new Vector2(44, 44);
		_iconPreviewRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_iconPreviewRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconPanel.AddChild(_iconPreviewRect);
		iconRow.AddChild(iconPanel);

		var iconDropdownVBox = new VBoxContainer();
		iconDropdownVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		(_txtIconPath, _setIconPathValue) = AddAssetFilterDropdown(
			iconDropdownVBox,
			TranslationServer.Translate("Icon Texture:"),
			_currentIconPath,
			(all) => ScanAvailableAssets("icons", all),
			(val) =>
			{
				_currentIconPath = val ?? string.Empty;
				UpdateIconPreview(_currentIconPath);
			},
			TranslationServer.Translate("Select or search icon from metadata..."),
			100f
		);

		iconRow.AddChild(iconDropdownVBox);
		configVBox.AddChild(iconRow);

		// SECTION 2: VISUAL EFFECT (SPRITESHEET)
		AddSectionHeader(configVBox, "✨ " + TranslationServer.Translate("VISUAL EFFECT (SPRITESHEET)"), new Color(0.35f, 0.75f, 0.9f));

		(_txtVisualEffect, _setVisualEffectValue) = AddAssetFilterDropdown(
			configVBox,
			TranslationServer.Translate("Spritesheet (VFX):"),
			_currentVisualEffect,
			(all) => ScanAvailableAssets("vfx", all),
			(val) =>
			{
				_currentVisualEffect = val ?? string.Empty;
				ReloadVfxSpritesheet();
			},
			TranslationServer.Translate("Select or search spritesheet..."),
			140f
		);

		// SECTION 3: AREA OF EFFECT (AOE)
		AddSectionHeader(configVBox, "🎯 " + TranslationServer.Translate("AREA OF EFFECT (AOE)"), new Color(0.4f, 0.85f, 0.5f));

		(_sldAoeRadius, _lblAoeRadiusVal) = AddSlider(
			configVBox,
			TranslationServer.Translate("AoE Radius:"),
			0.0f,
			20.0f,
			0.25f,
			_currentAoeRadius,
			(val) =>
			{
				_currentAoeRadius = val;
				UpdateAoEIndicator(_currentAoeRadius);
			},
			"0.00m",
			140f
		);

		// SECTION 4: AUDIO & CAST SOUND
		AddSectionHeader(configVBox, "🔊 " + TranslationServer.Translate("AUDIO & CAST SOUND"), new Color(0.9f, 0.6f, 0.35f));

		(_txtCastSound, _setCastSoundValue) = AddAssetFilterDropdown(
			configVBox,
			TranslationServer.Translate("Cast Sound:"),
			_currentCastSound,
			(all) => ScanAvailableAssets("audio", all),
			(val) =>
			{
				_currentCastSound = val ?? string.Empty;
			},
			TranslationServer.Translate("Select or search audio event..."),
			140f,
			false,
			(soundVal) => PlaySoundFile(soundVal)
		);
	}

	private void Setup3DEnvironment()
	{
		if (_subViewport == null) return;

		_simRoot = new Node3D();
		_subViewport.AddChild(_simRoot);

		// AOE Disk (Translucent Fill)
		var diskMesh = new CylinderMesh
		{
			TopRadius = 1.0f,
			BottomRadius = 1.0f,
			Height = 0.005f
		};
		var diskMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 0.7f, 1.0f, 0.15f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		_aoeDiskMesh = new MeshInstance3D { Mesh = diskMesh, MaterialOverride = diskMat, Position = new Vector3(0, 0.01f, 0) };
		_simRoot.AddChild(_aoeDiskMesh);

		// AOE Outer Ring
		var ringMesh = new TorusMesh
		{
			InnerRadius = 0.97f,
			OuterRadius = 1.0f,
			Rings = 48,
			RingSegments = 8
		};
		var ringMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.25f, 0.85f, 1.0f, 0.85f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		_aoeRingMesh = new MeshInstance3D { Mesh = ringMesh, MaterialOverride = ringMat, Position = new Vector3(0, 0.015f, 0) };
		_simRoot.AddChild(_aoeRingMesh);

		// Spritesheet Animated Sprite 3D
		_vfxSprite = new AnimatedSprite3D
		{
			Position = new Vector3(0, 0.8f, 0),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Transparent = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.Disabled
		};
		_simRoot.AddChild(_vfxSprite);

		UpdateAoEIndicator(_currentAoeRadius);
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

	private void UpdateAoEIndicator(float radius)
	{
		if (_aoeRingMesh == null || _aoeDiskMesh == null) return;

		if (radius <= 0.05f)
		{
			_aoeRingMesh.Visible = false;
			_aoeDiskMesh.Visible = false;
			return;
		}

		_aoeRingMesh.Visible = true;
		_aoeDiskMesh.Visible = true;

		_aoeRingMesh.Scale = new Vector3(radius, 1.0f, radius);
		_aoeDiskMesh.Scale = new Vector3(radius, 1.0f, radius);
	}

	private void UpdateIconPreview(string iconPath)
	{
		if (_iconPreviewRect == null) return;
		var tex = ResolveTexture(iconPath);
		_iconPreviewRect.Texture = tex;
	}

	private void ReloadVfxSpritesheet()
	{
		if (_vfxSprite == null) return;

		if (string.IsNullOrWhiteSpace(_currentVisualEffect))
		{
			_vfxSprite.SpriteFrames = null;
			_vfxSprite.Visible = false;
			return;
		}

		var texture = ResolveTexture(_currentVisualEffect);
		if (texture == null)
		{
			_vfxSprite.SpriteFrames = null;
			_vfxSprite.Visible = false;
			return;
		}

		int cols = 4;
		int rows = 4;

		// Detect columns and rows from metadata if available
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace");
		string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");

		try
		{
			string json = System.IO.File.ReadAllText(metadataPath);
			var root = JsonNode.Parse(json)?.AsObject();
			var vfxSheets = (root?["Assets"]?["vfx_spritesheets"] ?? root?["MapProperties"]?["Assets"]?["vfx_spritesheets"])?.AsObject();
			string fName = System.IO.Path.GetFileName(_currentVisualEffect);
			if (vfxSheets != null)
			{
				JsonObject sheetObj = null;
				if (vfxSheets.ContainsKey(fName) && vfxSheets[fName] is JsonObject so1) sheetObj = so1;
				else if (vfxSheets.ContainsKey(_currentVisualEffect) && vfxSheets[_currentVisualEffect] is JsonObject so2) sheetObj = so2;

				if (sheetObj != null)
				{
					if (sheetObj.ContainsKey("columns")) cols = (int)sheetObj["columns"];
					if (sheetObj.ContainsKey("rows")) rows = (int)sheetObj["rows"];
				}
			}
		}
		catch { }

		if (cols <= 0) cols = 1;
		if (rows <= 0) rows = 1;

		int totalFrames = cols * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed("play", 20.0f);

		int frameWidth = Math.Max(1, texture.GetWidth() / cols);
		int frameHeight = Math.Max(1, texture.GetHeight() / rows);

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

		_vfxSprite.SpriteFrames = frames;
		_vfxSprite.Animation = "play";
		_vfxSprite.PixelSize = 6.0f / frameWidth;
		_vfxSprite.SpeedScale = _playbackSpeed;
		_vfxSprite.Visible = true;
		_vfxSprite.Play("play");
	}

	private void TriggerCastTest()
	{
		ReloadVfxSpritesheet();
		if (_vfxSprite != null && _vfxSprite.SpriteFrames != null && _vfxSprite.SpriteFrames.HasAnimation("play"))
		{
			_vfxSprite.Frame = 0;
			_vfxSprite.Play("play");
		}

		PlaySoundFile(_currentCastSound);
	}

	private void PlayVfxAnimation()
	{
		_isPaused = false;
		if (_vfxSprite != null && _vfxSprite.SpriteFrames != null && _vfxSprite.SpriteFrames.HasAnimation("play"))
		{
			_vfxSprite.Play("play");
		}
	}

	private void PauseVfxAnimation()
	{
		_isPaused = true;
		if (_vfxSprite != null && _vfxSprite.SpriteFrames != null && _vfxSprite.SpriteFrames.HasAnimation("play"))
		{
			_vfxSprite.Pause();
		}
	}

	private void StopVfxAnimation()
	{
		_isPaused = false;
		if (_vfxSprite != null && _vfxSprite.SpriteFrames != null && _vfxSprite.SpriteFrames.HasAnimation("play"))
		{
			_vfxSprite.Stop();
			_vfxSprite.Frame = 0;
		}
	}

	private void PlaySoundFile(string soundPath)
	{
		if (string.IsNullOrWhiteSpace(soundPath) || _sfxPlayer == null) return;

		try
		{
			if (soundPath.StartsWith("res://"))
			{
				if (ResourceLoader.Exists(soundPath))
				{
					_sfxPlayer.Stream = GD.Load<AudioStream>(soundPath);
					_sfxPlayer.Play();
					return;
				}
			}

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace");
			string cleanPath = soundPath.Trim().TrimStart('/', '\\').Replace('\\', '/');
			string fileName = System.IO.Path.GetFileName(cleanPath);

			var candidatePaths = new List<string>
			{
				soundPath,
				System.IO.Path.Combine(wsPath, cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", "audio", "sfx", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "audio", "music", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "audio", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "sounds", fileName),
			};

			AudioStream stream = null;
			foreach (var candidate in candidatePaths)
			{
				if (!string.IsNullOrWhiteSpace(candidate) && System.IO.File.Exists(candidate))
				{
					if (candidate.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
					{
						stream = AudioStreamOggVorbis.LoadFromFile(candidate);
					}
					else
					{
						stream = GD.Load<AudioStream>(candidate);
					}
					if (stream != null) break;
				}
			}

			if (stream == null)
			{
				var resCandidates = new[]
				{
					$"res://Assets/audio/sfx/{fileName}",
					$"res://Assets/audio/music/{fileName}",
					$"res://Assets/audio/{fileName}",
					$"res://Assets/sounds/{fileName}",
					$"res://{cleanPath}"
				};
				foreach (var resPath in resCandidates)
				{
					if (ResourceLoader.Exists(resPath))
					{
						stream = GD.Load<AudioStream>(resPath);
						if (stream != null) break;
					}
				}
			}

			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AbilityVfxDialog] PlaySoundFile error: {ex.Message}");
		}
	}

	private Texture2D ResolveTexture(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;

		try
		{
			if (path.StartsWith("res://"))
			{
				if (ResourceLoader.Exists(path))
				{
					return GD.Load<Texture2D>(path);
				}
			}

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace");
			string cleanPath = path.Trim().TrimStart('/', '\\').Replace('\\', '/');
			string fileName = System.IO.Path.GetFileName(cleanPath);

			var candidatePaths = new List<string>
			{
				path,
				System.IO.Path.Combine(wsPath, cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", "vfx", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "icons", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "decals", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "textures", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "textures", "ribbons", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "textures", "noise", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "skyboxes", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "UI", fileName),
			};

			foreach (var candidate in candidatePaths)
			{
				if (!string.IsNullOrWhiteSpace(candidate) && System.IO.File.Exists(candidate))
				{
					Image? img = null;
					if (candidate.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						byte[] rtexBytes = System.IO.File.ReadAllBytes(candidate);
						byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(rtexBytes, 0);
						if (webpBytes != null && webpBytes.Length > 0)
						{
							img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
							if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
							{
								img.LoadPngFromBuffer(webpBytes);
							}
						}
					}
					else
					{
						img = Image.LoadFromFile(candidate);
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
			}

			var resCandidates = new[]
			{
				$"res://Assets/vfx/{fileName}",
				$"res://Assets/icons/{fileName}",
				$"res://Assets/decals/{fileName}",
				$"res://Assets/textures/{fileName}",
				$"res://Assets/UI/{fileName}",
				$"res://{cleanPath}"
			};

			foreach (var resPath in resCandidates)
			{
				if (ResourceLoader.Exists(resPath))
				{
					return GD.Load<Texture2D>(resPath);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AbilityVfxDialog] ResolveTexture error: {ex.Message}");
		}

		return null;
	}

	public void OpenForAbility(string abilityId, JsonObject abilityData, Action<JsonObject> onApplied = null)
	{
		_abilityId = abilityId ?? string.Empty;
		_abilityName = abilityData?["Name"]?.ToString() ?? _abilityId;
		_onApplied = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Ability VFX Studio")} - {_abilityName}";

		_currentVisualEffect = abilityData?["VisualEffect"]?.ToString() ?? string.Empty;
		_currentCastSound = abilityData?["CastSound"]?.ToString() ?? string.Empty;
		_currentIconPath = abilityData?["IconPath"]?.ToString() ?? string.Empty;
		_currentAoeRadius = abilityData?["AreaOfEffectRadius"] != null ? (float)abilityData["AreaOfEffectRadius"] : 4.0f;

		_initialVisualEffect = _currentVisualEffect;
		_initialCastSound = _currentCastSound;
		_initialIconPath = _currentIconPath;
		_initialAoeRadius = _currentAoeRadius;

		_setVisualEffectValue?.Invoke(_currentVisualEffect);
		_setCastSoundValue?.Invoke(_currentCastSound);
		_setIconPathValue?.Invoke(_currentIconPath);
		if (_sldAoeRadius != null) _sldAoeRadius.Value = _currentAoeRadius;

		UpdateIconPreview(_currentIconPath);
		UpdateAoEIndicator(_currentAoeRadius);
		ReloadVfxSpritesheet();

		OpenDialog();
		ResetCameraDefault();
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_abilityId))
		{
			Hud?.SaveCustomAbilityVfxToMetadata(
				_abilityId,
				_currentVisualEffect,
				_currentCastSound,
				_currentIconPath,
				_currentAoeRadius
			);

			var updatedData = new JsonObject
			{
				["AbilityId"] = _abilityId,
				["VisualEffect"] = _currentVisualEffect,
				["CastSound"] = _currentCastSound,
				["IconPath"] = _currentIconPath,
				["AreaOfEffectRadius"] = _currentAoeRadius
			};

			_onApplied?.Invoke(updatedData);
			Hud?.ShowFeedback(TranslationServer.Translate("Ability VFX and audio updated successfully."));
		}

		StopVfxAnimation();
	}

	protected override void OnCancel()
	{
		StopVfxAnimation();
	}

	public override void CloseDialog()
	{
		StopVfxAnimation();
		base.CloseDialog();
	}
}
