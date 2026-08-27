using Godot;
using System;
using System.Collections.Generic;
using Realm.Godot.Animation;
using Realm.Godot.Utils;

public partial class AnimationPreviewDialog : FloatingDialogBase
{
	private static readonly string[] StandardActionTypes = new[]
	{
		"Idle",
		"Walk",
		"Attack",
		"Death",
		"Labor",
		"Spell_Cast",
		"Dance"
	};

	private static readonly Dictionary<string, string> ActionIcons = new()
	{
		{ "Idle", "💤" },
		{ "Walk", "🚶" },
		{ "Attack", "⚔️" },
		{ "Death", "💀" },
		{ "Labor", "⚒️" },
		{ "Spell_Cast", "🪄" },
		{ "Dance", "💃" }
	};

	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewModelRoot;
	private AnimationPlayer _animPlayer;

	private LineEdit _txtPreviewRanim;
	private Action<string> _setPreviewRanimValue;
	private OptionButton _optTargetAction;
	private VBoxContainer _actionListContainer;

	private Node _sourceSelectedObject;
	private string _currentUnitId = "";
	private string _currentPreviewRanim = "";
	private float _currentSpeed = 1.0f;
	private bool _isUpdatingUI;

	private Dictionary<string, List<string>> _workingAnimations = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, List<string>> _initialAnimations = new(StringComparer.OrdinalIgnoreCase);

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
		: base(hud, TranslationServer.Translate("Unit Animation Studio"), new Vector2(500, 720))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(480, 220), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		// CAMERA TOOLBAR
		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Camera:");
		lblPreset.AddThemeFontSizeOverride("font_size", 10);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		presetRow.AddChild(lblPreset);

		AddButton(presetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View model from front", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View model from side", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 0f), "View model from back", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric 3/4 view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera zoom and position to default", 10, new Vector2(0, 22));

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		presetRow.AddChild(spacer);

		AddButton(presetRow, "▶ " + TranslationServer.Translate("Play"), () => PlayCurrentPreview(), "Play Animation", 10, new Vector2(0, 22));
		AddButton(presetRow, "⏸ " + TranslationServer.Translate("Pause"), () => PauseAnimation(), "Pause Animation", 10, new Vector2(0, 22));
		AddButton(presetRow, "⏹ " + TranslationServer.Translate("Stop"), () => StopAnimation(), "Stop Animation", 10, new Vector2(0, 22));

		topControlsVBox.AddChild(presetRow);

		// TOP AUTO-COMPLETE DROPDOWN FOR PREVIEWING ANY .RANIM FILE
		var animInputSection = new VBoxContainer();
		animInputSection.AddThemeConstantOverride("separation", 4);

		AddSectionHeader(animInputSection, "🎬 " + TranslationServer.Translate("ANIMATION PREVIEW & ASSIGNMENT"), new Color(0.35f, 0.75f, 0.9f));

		(_txtPreviewRanim, _setPreviewRanimValue) = AddAssetFilterDropdown(
			animInputSection,
			TranslationServer.Translate("Preview .ranim:"),
			_currentPreviewRanim,
			(all) => ScanAvailableAssets("animations", all),
			(val) =>
			{
				_currentPreviewRanim = val ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(_currentPreviewRanim))
				{
					PlayAnimationFile(_currentPreviewRanim);
				}
			},
			TranslationServer.Translate("Select or search .ranim asset from metadata..."),
			130f
		);

		// ADD TO ACTION ROW
		var addActionRow = new HBoxContainer();
		addActionRow.AddThemeConstantOverride("separation", 6);

		var lblAssign = new Label();
		lblAssign.Text = TranslationServer.Translate("Assign to Action:");
		lblAssign.CustomMinimumSize = new Vector2(130, 0);
		lblAssign.AddThemeFontSizeOverride("font_size", 11);
		addActionRow.AddChild(lblAssign);

		_optTargetAction = new OptionButton();
		_optTargetAction.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optTargetAction.AddThemeFontSizeOverride("font_size", 11);
		for (int i = 0; i < StandardActionTypes.Length; i++)
		{
			string act = StandardActionTypes[i];
			string icon = ActionIcons.TryGetValue(act, out var ic) ? ic : "⚡";
			_optTargetAction.AddItem($"{icon} {act}", i);
		}
		addActionRow.AddChild(_optTargetAction);

		AddButton(addActionRow, "+ " + TranslationServer.Translate("Add to Action"), () => AddCurrentPreviewToSelectedAction(), "Add previewed animation into action's random array list", 11, new Vector2(120, 26));

		animInputSection.AddChild(addActionRow);
		topControlsVBox.AddChild(animInputSection);

		// ACTION TYPES & CONFIGURED ANIMATIONS LIST
		AddSectionHeader(BodyContainer, "📋 " + TranslationServer.Translate("CONFIGURED UNIT ANIMATIONS (RANDOM SELECTION)"), new Color(0.85f, 0.75f, 0.4f));

		var scrollBody = CreateScrollBody(250);
		_actionListContainer = new VBoxContainer();
		_actionListContainer.AddThemeConstantOverride("separation", 10);
		_actionListContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(_actionListContainer);
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

		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {selectedObject.Name}";

		InitWorkingAnimations();
		ClearPreviewModel();
		SetupPreviewModel(modelRoot);

		OpenDialog();
		ResetCameraDefault();

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	public void OpenForUnitId(string unitId, string modelPath = null)
	{
		_currentUnitId = unitId;
		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {unitId}";

		InitWorkingAnimations();
		ClearPreviewModel();

		Node3D loadedModel = null;
		if (!string.IsNullOrEmpty(modelPath))
		{
			var loaded = ModelCache.GetModel(modelPath);
			if (loaded is Node3D node3D) loadedModel = node3D;
		}

		if (loadedModel != null)
		{
			SetupPreviewModel(loadedModel);
		}

		OpenDialog();
		ResetCameraDefault();

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	private void InitWorkingAnimations()
	{
		_workingAnimations.Clear();
		_initialAnimations.Clear();

		if (!string.IsNullOrEmpty(_currentUnitId) && GameHost.UnitRegistry.TryGetValue(_currentUnitId, out var uMeta) && uMeta.Animations != null)
		{
			foreach (var kvp in uMeta.Animations)
			{
				var list = new List<string>(kvp.Value ?? Array.Empty<string>());
				_workingAnimations[kvp.Key] = list;
				_initialAnimations[kvp.Key] = new List<string>(list);
			}
		}
	}

	private void SelectFirstAvailableOrAssignedAnimation()
	{
		// Try to find the first assigned animation, or first available from metadata
		foreach (var act in StandardActionTypes)
		{
			if (_workingAnimations.TryGetValue(act, out var list) && list.Count > 0)
			{
				_currentPreviewRanim = list[0];
				_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
				PlayAnimationFile(_currentPreviewRanim);
				return;
			}
		}

		var allAvailable = ScanAvailableAssets("animations");
		if (allAvailable.Count > 0)
		{
			_currentPreviewRanim = allAvailable[0];
			_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
			PlayAnimationFile(_currentPreviewRanim);
		}
	}

	private void RebuildActionListUI()
	{
		if (_actionListContainer == null) return;

		foreach (Node child in _actionListContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (string actionType in StandardActionTypes)
		{
			var actionCard = new PanelContainer();
			actionCard.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var cardVBox = new VBoxContainer();
			cardVBox.AddThemeConstantOverride("separation", 6);
			actionCard.AddChild(cardVBox);

			var headerRow = new HBoxContainer();
			headerRow.AddThemeConstantOverride("separation", 6);

			string icon = ActionIcons.TryGetValue(actionType, out var ic) ? ic : "⚡";
			var lblHeader = new Label();
			lblHeader.Text = $"{icon} {actionType}";
			lblHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lblHeader.AddThemeFontSizeOverride("font_size", 12);
			lblHeader.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			headerRow.AddChild(lblHeader);

			_workingAnimations.TryGetValue(actionType, out var animList);
			int count = animList?.Count ?? 0;
			var countBadge = new Label();
			countBadge.Text = count == 1 ? "1 anim" : $"{count} anims";
			countBadge.AddThemeColorOverride("font_color", count > 0 ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
			countBadge.AddThemeFontSizeOverride("font_size", 10);
			headerRow.AddChild(countBadge);

			cardVBox.AddChild(headerRow);

			if (animList == null || animList.Count == 0)
			{
				var fallbackLbl = new Label();
				fallbackLbl.Text = TranslationServer.Translate($"[Default fallback: {actionType.ToLowerInvariant()}.ranim]");
				fallbackLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f, 0.7f));
				fallbackLbl.AddThemeFontSizeOverride("font_size", 11);
				cardVBox.AddChild(fallbackLbl);
			}
			else
			{
				for (int i = 0; i < animList.Count; i++)
				{
					int index = i;
					string animFile = animList[i];

					var itemRow = new HBoxContainer();
					itemRow.AddThemeConstantOverride("separation", 6);

					var badge = new Label();
					badge.Text = $"{actionType}_{index}";
					badge.CustomMinimumSize = new Vector2(65, 0);
					badge.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
					badge.AddThemeFontSizeOverride("font_size", 10);
					itemRow.AddChild(badge);

					var nameLbl = new Label();
					nameLbl.Text = animFile;
					nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
					nameLbl.AddThemeFontSizeOverride("font_size", 11);
					nameLbl.ClipText = true;
					itemRow.AddChild(nameLbl);

					AddButton(itemRow, "▶ " + TranslationServer.Translate("Preview"), () =>
					{
						_currentPreviewRanim = animFile;
						_setPreviewRanimValue?.Invoke(animFile);
						PlayAnimationFile(animFile);
					}, "Preview this animation on loop", 10, new Vector2(65, 22));

					AddButton(itemRow, "✕ " + TranslationServer.Translate("Remove"), () =>
					{
						RemoveAnimationFromAction(actionType, index);
					}, "Remove this animation from action array", 10, new Vector2(65, 22));

					cardVBox.AddChild(itemRow);
				}
			}

			_actionListContainer.AddChild(actionCard);
		}
	}

	private void AddCurrentPreviewToSelectedAction()
	{
		string animFile = _currentPreviewRanim?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(animFile))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select or type an animation in the preview field first."));
			return;
		}

		int selectedActionIdx = _optTargetAction != null ? _optTargetAction.Selected : 0;
		if (selectedActionIdx < 0 || selectedActionIdx >= StandardActionTypes.Length) selectedActionIdx = 0;
		string actionType = StandardActionTypes[selectedActionIdx];

		if (!_workingAnimations.TryGetValue(actionType, out var list) || list == null)
		{
			list = new List<string>();
			_workingAnimations[actionType] = list;
		}

		list.Add(animFile);
		Hud?.ShowFeedback(TranslationServer.Translate($"Added {animFile} to {actionType}"));
		RebuildActionListUI();
	}

	private void RemoveAnimationFromAction(string actionType, int index)
	{
		if (_workingAnimations.TryGetValue(actionType, out var list) && list != null && index >= 0 && index < list.Count)
		{
			string removed = list[index];
			list.RemoveAt(index);
			if (list.Count == 0)
			{
				_workingAnimations.Remove(actionType);
			}
			Hud?.ShowFeedback(TranslationServer.Translate($"Removed {removed} from {actionType}"));
			RebuildActionListUI();
		}
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

	private void PlayCurrentPreview()
	{
		if (!string.IsNullOrEmpty(_currentPreviewRanim))
		{
			PlayAnimationFile(_currentPreviewRanim);
		}
	}

	private void PauseAnimation()
	{
		if (_animPlayer != null && _animPlayer.IsPlaying())
		{
			_animPlayer.Pause();
		}
	}

	private void StopAnimation()
	{
		if (_animPlayer != null)
		{
			_animPlayer.Stop(true);
		}
	}

	public void PlayAnimationFile(string animFileName)
	{
		if (string.IsNullOrEmpty(animFileName)) return;
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
		if (_animPlayer == null) return;

		string animName = System.IO.Path.GetFileNameWithoutExtension(animFileName);

		if (_animPlayer.HasAnimation(animName))
		{
			var anim = _animPlayer.GetAnimation(animName);
			if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
			_animPlayer.SpeedScale = _currentSpeed;
			_animPlayer.Play(animName);
			return;
		}

		string filePath = AnimationRetargetingService.ResolveAnimationFilePath(animFileName, _currentUnitId);
		RealmAnimationData animData = null;
		if (!string.IsNullOrEmpty(filePath))
		{
			animData = AnimationRetargetingService.GetOrLoadRanimData(filePath);
		}
		else
		{
			animData = animName switch
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

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_currentUnitId))
		{
			var finalDict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
			foreach (var kvp in _workingAnimations)
			{
				if (kvp.Value != null && kvp.Value.Count > 0)
				{
					finalDict[kvp.Key] = kvp.Value.ToArray();
				}
			}

			Hud?.SaveCustomUnitAnimations(_currentUnitId, finalDict);
			Hud?.ShowFeedback(TranslationServer.Translate("Unit animations applied successfully."));
		}

		ClearPreviewModel();
	}

	protected override void OnCancel()
	{
		ClearPreviewModel();
	}

	public override void CloseDialog()
	{
		ClearPreviewModel();
		base.CloseDialog();
	}
}
