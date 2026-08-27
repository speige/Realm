using Godot;
using System;
using System.Collections.Generic;
using Realm.Godot.Animation;

public partial class AnimationPreviewDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewModelRoot;
	private AnimationPlayer _animPlayer;

	private OptionButton _optAnimations;
	private Button _btnPlay;
	private Button _btnPause;
	private Button _btnStop;
	private HSlider _sldSpeed;
	private Label _lblSpeedVal;

	private Node _sourceSelectedObject;
	private string _currentUnitId = "";
	private float _currentSpeed = 1.0f;
	private bool _isUpdatingUI;

	private Vector3 _modelCenter = Vector3.Zero;
	private Vector3 _targetPosition = Vector3.Zero;
	private float _defaultDistance = 3.0f;
	private float _cameraDistance = 3.0f;
	private float _defaultYaw = Mathf.DegToRad(30.0f);
	private float _defaultPitch = Mathf.DegToRad(15.0f);
	private float _cameraYaw = Mathf.DegToRad(30.0f);
	private float _cameraPitch = Mathf.DegToRad(15.0f);

	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;

	public AnimationPreviewDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Animation Preview"), new Vector2(380, 500))
	{
		ApplyButton.Text = TranslationServer.Translate("CLOSE");
		CancelButton.Visible = false;

		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(360, 240), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		var controlsVBox = new VBoxContainer();
		controlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(controlsVBox);

		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Camera:");
		lblPreset.AddThemeFontSizeOverride("font_size", 11);
		presetRow.AddChild(lblPreset);

		AddButton(presetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View model from front", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View model from side", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 0f), "View model from back", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric 3/4 view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera zoom and position to default", 10, new Vector2(0, 22));

		controlsVBox.AddChild(presetRow);

		AddLabel(controlsVBox, TranslationServer.Translate("LMB: Orbit • RMB/MMB: Pan • Scroll: Zoom"), 10, UIStyle.ColorGoldDull);

		var animRow = new HBoxContainer();
		animRow.AddThemeConstantOverride("separation", 6);

		var lblAnim = new Label();
		lblAnim.Text = TranslationServer.Translate("Animation:");
		lblAnim.CustomMinimumSize = new Vector2(80, 0);
		lblAnim.AddThemeFontSizeOverride("font_size", 11);
		animRow.AddChild(lblAnim);

		_optAnimations = new OptionButton();
		_optAnimations.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optAnimations.AddThemeFontSizeOverride("font_size", 11);
		_optAnimations.ClipText = true;
		_optAnimations.FitToLongestItem = false;
		_optAnimations.ItemSelected += (index) =>
		{
			if (_isUpdatingUI) return;
			PlaySelectedAnimation();
		};
		animRow.AddChild(_optAnimations);
		controlsVBox.AddChild(animRow);

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 8);

		_btnPlay = AddButton(btnRow, "▶", () => PlaySelectedAnimation(), "Play Animation", 11, new Vector2(36, 26));
		_btnPause = AddButton(btnRow, "⏸", () =>
		{
			if (_animPlayer != null && _animPlayer.IsPlaying())
			{
				_animPlayer.Pause();
			}
		}, "Pause Animation", 11, new Vector2(36, 26));
		_btnStop = AddButton(btnRow, "⏹", () =>
		{
			if (_animPlayer != null)
			{
				_animPlayer.Stop(true);
			}
		}, "Stop Animation", 11, new Vector2(36, 26));

		controlsVBox.AddChild(btnRow);

		(_sldSpeed, _lblSpeedVal) = AddSlider(controlsVBox, TranslationServer.Translate("Speed:"), 0.25f, 2.0f, 0.05f, 1.0f, (val) =>
		{
			_currentSpeed = val;
			if (_animPlayer != null)
			{
				_animPlayer.SpeedScale = _currentSpeed;
			}
		}, "0.00x", 80.0f);
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
		_cameraDistance = Mathf.Clamp(_cameraDistance * factor, _defaultDistance * 0.15f, _defaultDistance * 6.0f);
		UpdateCameraTransform();
	}

	public void SetCameraPreset(float yawDegrees, float pitchDegrees)
	{
		_cameraYaw = Mathf.DegToRad(yawDegrees);
		_cameraPitch = Mathf.DegToRad(pitchDegrees);
		_targetPosition = _modelCenter;
		UpdateCameraTransform();
	}

	public void ResetCameraDefault()
	{
		_cameraDistance = _defaultDistance;
		_targetPosition = _modelCenter;
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

	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject)) return;

		_sourceSelectedObject = selectedObject;
		_currentUnitId = (selectedObject is Unit3D unit) ? unit.UnitId : "";

		Node modelRoot = selectedObject;
		if (selectedObject is Unit3D u && u.ModelNode != null)
		{
			modelRoot = u.ModelNode;
		}
		else if (selectedObject is Prop3D prop)
		{
			modelRoot = prop.GetNodeOrNull<Node3D>("VisualModel") ?? selectedObject;
		}

		var validation = SkeletonValidator.Validate(modelRoot);
		if (!validation.IsValid)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Selected model is not a compatible rigged mesh."));
			return;
		}

		TitleLabel.Text = $"{TranslationServer.Translate("Animation Preview")} - {selectedObject.Name}";

		ClearPreviewModel();
		SetupPreviewModel(modelRoot);
		PopulateAnimationDropdown();

		OpenDialog();

		PlaySelectedAnimation();
	}

	private void ClearPreviewModel()
	{
		if (_previewModelRoot != null && GodotObject.IsInstanceValid(_previewModelRoot))
		{
			if (_animPlayer != null && GodotObject.IsInstanceValid(_animPlayer))
			{
				_animPlayer.Stop(true);
			}
			_previewModelRoot.QueueFree();
			_previewModelRoot = null;
			_animPlayer = null;
		}
	}

	private void SetupPreviewModel(Node sourceModelRoot)
	{
		if (sourceModelRoot == null || _subViewport == null) return;

		var clonedNode = (Node3D)sourceModelRoot.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		if (clonedNode == null) return;

		clonedNode.Position = Vector3.Zero;
		clonedNode.Rotation = Vector3.Zero;
		clonedNode.Scale = Vector3.One;

		_subViewport.AddChild(clonedNode);
		_previewModelRoot = clonedNode;

		_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);

		FrameCameraOnModel(_previewModelRoot);

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
	}

	private void FrameCameraOnModel(Node3D modelRoot)
	{
		if (modelRoot == null || _camera == null) return;

		Aabb totalAabb = new Aabb();
		bool hasMesh = false;

		Action<Node, Transform3D> collectAabb = null;
		collectAabb = (node, parentTransform) =>
		{
			Transform3D currentTransform = parentTransform;
			if (node is Node3D node3D)
			{
				currentTransform = parentTransform * node3D.Transform;
			}

			if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
			{
				Aabb localAabb = meshInstance.GetAabb();
				Vector3 min = localAabb.Position;
				Vector3 max = localAabb.End;
				Vector3[] corners = new Vector3[]
				{
					currentTransform * new Vector3(min.X, min.Y, min.Z),
					currentTransform * new Vector3(max.X, min.Y, min.Z),
					currentTransform * new Vector3(min.X, max.Y, min.Z),
					currentTransform * new Vector3(max.X, max.Y, min.Z),
					currentTransform * new Vector3(min.X, min.Y, max.Z),
					currentTransform * new Vector3(max.X, min.Y, max.Z),
					currentTransform * new Vector3(min.X, max.Y, max.Z),
					currentTransform * max
				};

				Aabb globalMeshAabb = new Aabb(corners[0], Vector3.Zero);
				foreach (var c in corners) globalMeshAabb = globalMeshAabb.Expand(c);

				if (!hasMesh)
				{
					totalAabb = globalMeshAabb;
					hasMesh = true;
				}
				else
				{
					totalAabb = totalAabb.Merge(globalMeshAabb);
				}
			}

			int childCount = node.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				collectAabb(node.GetChild(i), currentTransform);
			}
		};

		collectAabb(modelRoot, Transform3D.Identity);

		if (hasMesh && totalAabb.Size.LengthSquared() > 0.001f)
		{
			_modelCenter = totalAabb.GetCenter();
			float radius = totalAabb.Size.Length() * 0.6f;
			_defaultDistance = radius * 2.2f;
		}
		else
		{
			_modelCenter = new Vector3(0, 0.8f, 0);
			_defaultDistance = 2.8f;
		}

		_defaultYaw = Mathf.DegToRad(30.0f);
		_defaultPitch = Mathf.DegToRad(15.0f);
		ResetCameraDefault();
	}

	private void PopulateAnimationDropdown()
	{
		_isUpdatingUI = true;
		_optAnimations.Clear();

		var animations = new List<string>
		{
			"Idle_0",
			"Walk_0",
			"Attack_0",
			"Death_0",
			"Labor_0",
			"Spell_Cast_0",
			"Dance_0"
		};

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string animDir = System.IO.Path.Combine(wsPath, "Assets", "animations");
		if (System.IO.Directory.Exists(animDir))
		{
			foreach (var file in System.IO.Directory.GetFiles(animDir, "*.ranim"))
			{
				string animName = System.IO.Path.GetFileNameWithoutExtension(file);
				if (!animations.Contains(animName))
				{
					animations.Add(animName);
				}
			}
		}

		for (int i = 0; i < animations.Count; i++)
		{
			_optAnimations.AddItem(animations[i], i);
		}

		_optAnimations.Selected = 0;
		_isUpdatingUI = false;
	}

	private void PlaySelectedAnimation()
	{
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;
		if (_optAnimations == null || _optAnimations.ItemCount == 0) return;

		string animName = _optAnimations.GetItemText(_optAnimations.Selected);
		if (string.IsNullOrEmpty(animName)) return;

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
		if (_animPlayer == null) return;

		if (_animPlayer.HasAnimation(animName))
		{
			var anim = _animPlayer.GetAnimation(animName);
			if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
			_animPlayer.SpeedScale = _currentSpeed;
			_animPlayer.Play(animName);
			return;
		}

		int underscoreIdx = animName.LastIndexOf('_');
		string baseType = underscoreIdx > 0 ? animName.Substring(0, underscoreIdx) : animName;
		string specificFile = null;

		if (!string.IsNullOrEmpty(_currentUnitId) && GameHost.UnitRegistry.TryGetValue(_currentUnitId, out var uMeta) && uMeta.Animations != null)
		{
			if (underscoreIdx > 0 && int.TryParse(animName.Substring(underscoreIdx + 1), out int varIdx))
			{
				if (uMeta.Animations.TryGetValue(baseType, out var aFiles) && varIdx >= 0 && varIdx < aFiles.Length)
				{
					specificFile = aFiles[varIdx];
				}
			}
		}

		string filePath = !string.IsNullOrEmpty(specificFile)
			? AnimationRetargetingService.ResolveAnimationFilePath(specificFile, _currentUnitId)
			: AnimationRetargetingService.ResolveAnimationFilePath(animName, _currentUnitId);

		RealmAnimationData animData = null;
		if (!string.IsNullOrEmpty(filePath))
		{
			animData = AnimationRetargetingService.GetOrLoadRanimData(filePath);
		}
		else
		{
			animData = baseType switch
			{
				"Idle" => RealmDefaultAnimations.Idle,
				"Walk" => RealmDefaultAnimations.Walk,
				"Attack" => RealmDefaultAnimations.Attack,
				"Death" => RealmDefaultAnimations.Death,
				"Labor" => RealmDefaultAnimations.Labor,
				"Spell_Cast" => RealmDefaultAnimations.Spell_Cast,
				"Dance" => RealmDefaultAnimations.Dance,
				_ => null
			};
		}

		if (animData != null)
		{
			if (AnimationRetargetingService.RetargetAndBind(animData, _previewModelRoot, animName, out _))
			{
				_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
				if (_animPlayer != null && _animPlayer.HasAnimation(animName))
				{
					var anim = _animPlayer.GetAnimation(animName);
					if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
					_animPlayer.SpeedScale = _currentSpeed;
					_animPlayer.Play(animName);
				}
			}
		}
	}

	public override void CloseDialog()
	{
		ClearPreviewModel();
		base.CloseDialog();
	}

	protected override void OnCancel()
	{
		ClearPreviewModel();
	}

	protected override void OnApply()
	{
		ClearPreviewModel();
	}
}
