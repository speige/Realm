using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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
	private OptionButton _optPreviewRightHand;
	private OptionButton _optPreviewLeftHand;
	private OptionButton _optTargetAction;
	private VBoxContainer _actionListContainer;

	private BoneAttachment3D _rightHandBoneAttachment;
	private BoneAttachment3D _leftHandBoneAttachment;
	private Node3D _rightHandModelNode;
	private Node3D _leftHandModelNode;

	private Node _sourceSelectedObject;
	private string _currentUnitId = "";
	private string _currentPreviewRanim = "";
	private float _currentSpeed = 1.0f;
	private bool _isUpdatingUI;

	private Dictionary<string, List<GameHost.UnitAnimationEntry>> _workingAnimations = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, List<GameHost.UnitAnimationEntry>> _initialAnimations = new(StringComparer.OrdinalIgnoreCase);

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

		var rightHandRow = new HBoxContainer();
		rightHandRow.AddThemeConstantOverride("separation", 6);

		var lblRightHand = new Label();
		lblRightHand.Text = TranslationServer.Translate("Preview Right Hand:");
		lblRightHand.CustomMinimumSize = new Vector2(130, 0);
		lblRightHand.AddThemeFontSizeOverride("font_size", 11);
		lblRightHand.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rightHandRow.AddChild(lblRightHand);

		_optPreviewRightHand = new OptionButton();
		_optPreviewRightHand.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optPreviewRightHand.AddThemeFontSizeOverride("font_size", 11);
		_optPreviewRightHand.ItemSelected += (idx) => OnPreviewHandSelectionChanged(HumanoidBone.RightHand);
		rightHandRow.AddChild(_optPreviewRightHand);

		var btnEditRight = new Button();
		btnEditRight.Set("icon_max_width", 0);
		btnEditRight.AddThemeConstantOverride("icon_max_width", 0);
		btnEditRight.Text = "✏️";
		btnEditRight.CustomMinimumSize = new Vector2(28, 22);
		btnEditRight.FocusMode = FocusModeEnum.None;
		btnEditRight.TooltipText = TranslationServer.Translate("Edit Object Attachment");
		btnEditRight.Pressed += () => OpenEditAttachmentForHand(HumanoidBone.RightHand);
		rightHandRow.AddChild(btnEditRight);

		animInputSection.AddChild(rightHandRow);

		var leftHandRow = new HBoxContainer();
		leftHandRow.AddThemeConstantOverride("separation", 6);

		var lblLeftHand = new Label();
		lblLeftHand.Text = TranslationServer.Translate("Preview Left Hand:");
		lblLeftHand.CustomMinimumSize = new Vector2(130, 0);
		lblLeftHand.AddThemeFontSizeOverride("font_size", 11);
		lblLeftHand.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		leftHandRow.AddChild(lblLeftHand);

		_optPreviewLeftHand = new OptionButton();
		_optPreviewLeftHand.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optPreviewLeftHand.AddThemeFontSizeOverride("font_size", 11);
		_optPreviewLeftHand.ItemSelected += (idx) => OnPreviewHandSelectionChanged(HumanoidBone.LeftHand);
		leftHandRow.AddChild(_optPreviewLeftHand);

		var btnEditLeft = new Button();
		btnEditLeft.Set("icon_max_width", 0);
		btnEditLeft.AddThemeConstantOverride("icon_max_width", 0);
		btnEditLeft.Text = "✏️";
		btnEditLeft.CustomMinimumSize = new Vector2(28, 22);
		btnEditLeft.FocusMode = FocusModeEnum.None;
		btnEditLeft.TooltipText = TranslationServer.Translate("Edit Object Attachment");
		btnEditLeft.Pressed += () => OpenEditAttachmentForHand(HumanoidBone.LeftHand);
		leftHandRow.AddChild(btnEditLeft);

		animInputSection.AddChild(leftHandRow);

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
		AddSectionHeader(BodyContainer, "📋 " + TranslationServer.Translate("CONFIGURED UNIT ANIMATIONS"), new Color(0.85f, 0.75f, 0.4f));

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
		PopulateHandAttachmentDropdowns();
		ClearPreviewModel();
		OpenDialog();
		ResetCameraDefault();

		SetupPreviewModel(modelRoot);

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	public void OpenForUnitId(string unitId, string modelPath = null)
	{
		_currentUnitId = unitId;
		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {unitId}";

		if (string.IsNullOrEmpty(modelPath) && !string.IsNullOrEmpty(unitId))
		{
			if (GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta) && !string.IsNullOrEmpty(uMeta.ModelPath))
			{
				modelPath = uMeta.ModelPath;
			}
			else
			{
				try
				{
					string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
					string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");
					if (!System.IO.File.Exists(metadataPath))
					{
						string tPath = PathUtils.FindPath("MapTemplate/metadata.json");
						if (System.IO.File.Exists(tPath)) metadataPath = tPath;
					}
					if (System.IO.File.Exists(metadataPath))
					{
						string json = System.IO.File.ReadAllText(metadataPath);
						var root = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
						var customUnits = root?["CustomUnits"]?.AsArray();
						if (customUnits != null)
						{
							foreach (var uNode in customUnits)
							{
								if (uNode?["UnitId"]?.ToString() == unitId)
								{
									modelPath = uNode["ModelPath"]?.ToString();
									break;
								}
							}
						}
					}
				}
				catch { }
			}
		}

		InitWorkingAnimations();
		PopulateHandAttachmentDropdowns();
		ClearPreviewModel();

		OpenDialog();
		ResetCameraDefault();

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
				var list = kvp.Value != null
					? new List<GameHost.UnitAnimationEntry>(kvp.Value)
					: new List<GameHost.UnitAnimationEntry>();
				_workingAnimations[kvp.Key] = list;
				_initialAnimations[kvp.Key] = new List<GameHost.UnitAnimationEntry>(list);
			}
		}

		if (_workingAnimations.Count == 0 && !string.IsNullOrEmpty(_currentUnitId))
		{
			try
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");
				if (!System.IO.File.Exists(metadataPath))
				{
					string tPath = PathUtils.FindPath("MapTemplate/metadata.json");
					if (System.IO.File.Exists(tPath)) metadataPath = tPath;
				}
				if (System.IO.File.Exists(metadataPath))
				{
					string json = System.IO.File.ReadAllText(metadataPath);
					var root = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
					var customUnits = root?["CustomUnits"]?.AsArray();
					if (customUnits != null)
					{
						foreach (var uNode in customUnits)
						{
							if (uNode?["UnitId"]?.ToString() == _currentUnitId)
							{
								var anims = uNode["Animations"]?.AsObject();
								if (anims != null)
								{
									foreach (var prop in anims)
									{
										var list = new List<GameHost.UnitAnimationEntry>();
										if (prop.Value is System.Text.Json.Nodes.JsonArray arr)
										{
											foreach (var item in arr)
											{
												if (item is System.Text.Json.Nodes.JsonObject obj)
												{
													string a = obj["Animation"]?.ToString() ?? obj["Name"]?.ToString() ?? string.Empty;
													string? r = obj["RightHandAttachment"]?.ToString() ?? obj["RightHand"]?.ToString();
													string? l = obj["LeftHandAttachment"]?.ToString() ?? obj["LeftHand"]?.ToString();
													list.Add(new GameHost.UnitAnimationEntry { Animation = a, RightHandAttachment = r, LeftHandAttachment = l });
												}
												else if (item != null)
												{
													list.Add(new GameHost.UnitAnimationEntry { Animation = item.ToString() });
												}
											}
										}
										else if (prop.Value != null)
										{
											list.Add(new GameHost.UnitAnimationEntry { Animation = prop.Value.ToString() });
										}
										_workingAnimations[prop.Key] = list;
										_initialAnimations[prop.Key] = new List<GameHost.UnitAnimationEntry>(list);
									}
								}
								break;
							}
						}
					}
				}
			}
			catch { }
		}
	}

	private void PopulateHandAttachmentDropdowns()
	{
		var available = ObjectAttachmentDialog.GetAvailableObjectAttachmentIds();

		if (_optPreviewRightHand != null)
		{
			_optPreviewRightHand.Clear();
			_optPreviewRightHand.AddItem("<None>", 0);
			_optPreviewRightHand.SetItemMetadata(0, string.Empty);
			int idx = 1;
			foreach (var id in available)
			{
				_optPreviewRightHand.AddItem(id, idx);
				_optPreviewRightHand.SetItemMetadata(idx, id);
				idx++;
			}
			_optPreviewRightHand.Selected = 0;
		}

		if (_optPreviewLeftHand != null)
		{
			_optPreviewLeftHand.Clear();
			_optPreviewLeftHand.AddItem("<None>", 0);
			_optPreviewLeftHand.SetItemMetadata(0, string.Empty);
			int idx = 1;
			foreach (var id in available)
			{
				_optPreviewLeftHand.AddItem(id, idx);
				_optPreviewLeftHand.SetItemMetadata(idx, id);
				idx++;
			}
			_optPreviewLeftHand.Selected = 0;
		}
	}

	private string GetSelectedHandAttachment(HumanoidBone hand)
	{
		var opt = hand == HumanoidBone.RightHand ? _optPreviewRightHand : _optPreviewLeftHand;
		if (opt == null || opt.ItemCount == 0 || opt.Selected < 0) return string.Empty;
		return opt.GetItemMetadata(opt.Selected).AsString();
	}

	private void SetSelectedHandAttachment(HumanoidBone hand, string? attachmentId)
	{
		var opt = hand == HumanoidBone.RightHand ? _optPreviewRightHand : _optPreviewLeftHand;
		if (opt == null || opt.ItemCount == 0) return;

		if (string.IsNullOrEmpty(attachmentId) || attachmentId.Equals("<None>", StringComparison.OrdinalIgnoreCase) || attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			opt.Selected = 0;
			return;
		}

		string clean = System.IO.Path.GetFileNameWithoutExtension(attachmentId);
		for (int i = 0; i < opt.ItemCount; i++)
		{
			string meta = opt.GetItemMetadata(i).AsString();
			if (meta.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
				meta.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
				System.IO.Path.GetFileNameWithoutExtension(meta).Equals(clean, StringComparison.OrdinalIgnoreCase))
			{
				opt.Selected = i;
				return;
			}
		}

		int newIdx = opt.ItemCount;
		opt.AddItem(clean, newIdx);
		opt.SetItemMetadata(newIdx, clean);
		opt.Selected = newIdx;
	}

	private void OnPreviewHandSelectionChanged(HumanoidBone hand)
	{
		string attId = GetSelectedHandAttachment(hand);
		AttachModelToPreviewHand(hand, attId);
	}

	private void OpenEditAttachmentForHand(HumanoidBone hand)
	{
		string attId = GetSelectedHandAttachment(hand);
		if (string.IsNullOrEmpty(attId) || attId.Equals("<None>", StringComparison.OrdinalIgnoreCase))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select an attachment model to edit."));
			return;
		}

		string handStr = hand == HumanoidBone.LeftHand ? "LeftHand" : "RightHand";
		Hud?.OpenObjectAttachmentDialog(
			_currentUnitId,
			attId,
			handStr,
			_previewModelRoot,
			(orientation) =>
			{
				UpdatePreviewHandAttachments();
			}
		);
	}

	private void SelectFirstAvailableOrAssignedAnimation()
	{
		foreach (var act in StandardActionTypes)
		{
			if (_workingAnimations.TryGetValue(act, out var list) && list.Count > 0)
			{
				var entry = list[0];
				_currentPreviewRanim = entry.Animation;
				_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
				SetSelectedHandAttachment(HumanoidBone.RightHand, entry.RightHandAttachment);
				SetSelectedHandAttachment(HumanoidBone.LeftHand, entry.LeftHandAttachment);
				UpdatePreviewHandAttachments();
				PlayAnimationFile(_currentPreviewRanim);
				return;
			}
		}

		var allAvailable = ScanAvailableAssets("animations");
		if (allAvailable.Count > 0)
		{
			_currentPreviewRanim = allAvailable[0];
			_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
			SetSelectedHandAttachment(HumanoidBone.RightHand, null);
			SetSelectedHandAttachment(HumanoidBone.LeftHand, null);
			UpdatePreviewHandAttachments();
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
					var entry = animList[i];
					string animFile = entry.Animation;

					var itemRow = new HBoxContainer();
					itemRow.AddThemeConstantOverride("separation", 6);

					var badge = new Label();
					badge.Text = $"{actionType}_{index}";
					badge.CustomMinimumSize = new Vector2(55, 0);
					badge.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
					badge.AddThemeFontSizeOverride("font_size", 10);
					itemRow.AddChild(badge);

					var nameLbl = new Label();
					nameLbl.Text = animFile;
					nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
					nameLbl.AddThemeFontSizeOverride("font_size", 11);
					nameLbl.ClipText = true;
					itemRow.AddChild(nameLbl);

					var lblRight = new Label();
					lblRight.Text = !string.IsNullOrEmpty(entry.RightHandAttachment) ? $"R: {entry.RightHandAttachment}" : "R: -";
					lblRight.CustomMinimumSize = new Vector2(75, 0);
					lblRight.AddThemeFontSizeOverride("font_size", 10);
					lblRight.AddThemeColorOverride("font_color", UIStyle.ColorGold);
					lblRight.ClipText = true;
					itemRow.AddChild(lblRight);

					var lblLeft = new Label();
					lblLeft.Text = !string.IsNullOrEmpty(entry.LeftHandAttachment) ? $"L: {entry.LeftHandAttachment}" : "L: -";
					lblLeft.CustomMinimumSize = new Vector2(75, 0);
					lblLeft.AddThemeFontSizeOverride("font_size", 10);
					lblLeft.AddThemeColorOverride("font_color", UIStyle.ColorGold);
					lblLeft.ClipText = true;
					itemRow.AddChild(lblLeft);

					AddButton(itemRow, "▶ " + TranslationServer.Translate("Preview"), () =>
					{
						_currentPreviewRanim = entry.Animation;
						_setPreviewRanimValue?.Invoke(entry.Animation);
						SetSelectedHandAttachment(HumanoidBone.RightHand, entry.RightHandAttachment);
						SetSelectedHandAttachment(HumanoidBone.LeftHand, entry.LeftHandAttachment);
						UpdatePreviewHandAttachments();

						for (int a = 0; a < StandardActionTypes.Length; a++)
						{
							if (StandardActionTypes[a].Equals(actionType, StringComparison.OrdinalIgnoreCase))
							{
								_optTargetAction.Selected = a;
								break;
							}
						}

						PlayAnimationFile(entry.Animation);
					}, "Preview this animation and attachments", 10, new Vector2(65, 22));

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
			Hud?.ShowFeedback(TranslationServer.Translate("Please select an animation in the preview field first."));
			return;
		}

		int selectedActionIdx = _optTargetAction != null ? _optTargetAction.Selected : 0;
		if (selectedActionIdx < 0 || selectedActionIdx >= StandardActionTypes.Length) selectedActionIdx = 0;
		string actionType = StandardActionTypes[selectedActionIdx];

		if (!_workingAnimations.TryGetValue(actionType, out var list) || list == null)
		{
			list = new List<GameHost.UnitAnimationEntry>();
			_workingAnimations[actionType] = list;
		}

		string right = GetSelectedHandAttachment(HumanoidBone.RightHand);
		string left = GetSelectedHandAttachment(HumanoidBone.LeftHand);
		string? rightVal = string.IsNullOrEmpty(right) ? null : right;
		string? leftVal = string.IsNullOrEmpty(left) ? null : left;

		bool exists = list.Any(e =>
			e.Animation.Equals(animFile, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(e.RightHandAttachment ?? "", rightVal ?? "", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(e.LeftHandAttachment ?? "", leftVal ?? "", StringComparison.OrdinalIgnoreCase));

		if (exists)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("This animation and attachment combination already exists in this action."));
			return;
		}

		list.Add(new GameHost.UnitAnimationEntry
		{
			Animation = animFile,
			RightHandAttachment = rightVal,
			LeftHandAttachment = leftVal
		});

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Added {0} to {1}"), animFile, actionType));
		RebuildActionListUI();
	}

	private void RemoveAnimationFromAction(string actionType, int index)
	{
		if (_workingAnimations.TryGetValue(actionType, out var list) && list != null && index >= 0 && index < list.Count)
		{
			var removed = list[index];
			list.RemoveAt(index);
			if (list.Count == 0)
			{
				_workingAnimations.Remove(actionType);
			}
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Removed {0} from {1}"), removed.Animation, actionType));
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
		_rightHandBoneAttachment = null;
		_leftHandBoneAttachment = null;
		_rightHandModelNode = null;
		_leftHandModelNode = null;
	}

	private void SetupPreviewModel(Node sourceModelRoot)
	{
		if (sourceModelRoot == null || _subViewport == null) return;

		var clonedNode = (Node3D)sourceModelRoot.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		if (clonedNode == null) return;

		RemoveAllBoneAttachments(clonedNode);

		clonedNode.Position = Vector3.Zero;
		clonedNode.Rotation = Vector3.Zero;
		clonedNode.Scale = Vector3.One;

		_subViewport.AddChild(clonedNode);
		_previewModelRoot = clonedNode;

		if (_previewModelRoot.IsInsideTree())
		{
			_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);
		}

		FrameCameraOnModel(_previewModelRoot);

		var skeleton = SkeletonValidator.FindSkeleton(_previewModelRoot);
		if (skeleton != null)
		{
			int rightIdx = HumanoidBoneMapper.FindBoneInSkeleton(skeleton, HumanoidBone.RightHand);
			if (rightIdx >= 0)
			{
				_rightHandBoneAttachment = new BoneAttachment3D
				{
					Name = "BoneAttachment_RightHand",
					BoneName = skeleton.GetBoneName(rightIdx),
					BoneIdx = rightIdx
				};
				skeleton.AddChild(_rightHandBoneAttachment);
			}

			int leftIdx = HumanoidBoneMapper.FindBoneInSkeleton(skeleton, HumanoidBone.LeftHand);
			if (leftIdx >= 0)
			{
				_leftHandBoneAttachment = new BoneAttachment3D
				{
					Name = "BoneAttachment_LeftHand",
					BoneName = skeleton.GetBoneName(leftIdx),
					BoneIdx = leftIdx
				};
				skeleton.AddChild(_leftHandBoneAttachment);
			}
		}

		UpdatePreviewHandAttachments();

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
	}

	private void UpdatePreviewHandAttachments()
	{
		string rightId = GetSelectedHandAttachment(HumanoidBone.RightHand);
		string leftId = GetSelectedHandAttachment(HumanoidBone.LeftHand);

		AttachModelToPreviewHand(HumanoidBone.RightHand, rightId);
		AttachModelToPreviewHand(HumanoidBone.LeftHand, leftId);
	}

	private static void RemoveAllBoneAttachments(Node node)
	{
		if (node == null) return;
		var boneAttachments = new List<BoneAttachment3D>();
		CollectBoneAttachmentsRecursive(node, boneAttachments);
		foreach (var ba in boneAttachments)
		{
			ba.GetParent()?.RemoveChild(ba);
			ba.QueueFree();
		}
	}

	private static void CollectBoneAttachmentsRecursive(Node node, List<BoneAttachment3D> list)
	{
		if (node is BoneAttachment3D ba)
		{
			list.Add(ba);
			return;
		}
		foreach (Node child in node.GetChildren())
		{
			CollectBoneAttachmentsRecursive(child, list);
		}
	}

	private void AttachModelToPreviewHand(HumanoidBone hand, string attachmentId)
	{
		var targetBone = hand == HumanoidBone.RightHand ? _rightHandBoneAttachment : _leftHandBoneAttachment;
		if (targetBone == null || !GodotObject.IsInstanceValid(targetBone)) return;

		foreach (Node child in targetBone.GetChildren())
		{
			targetBone.RemoveChild(child);
			child.QueueFree();
		}

		if (hand == HumanoidBone.RightHand) _rightHandModelNode = null;
		else _leftHandModelNode = null;

		if (string.IsNullOrEmpty(attachmentId) || attachmentId.Equals("<None>", StringComparison.OrdinalIgnoreCase) || attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		Node3D loaded = Unit3D.ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (loaded != null)
		{
			float scale = defScale;
			Vector3 pos = defPos;
			Vector3 rot = defRot;

			if (!string.IsNullOrEmpty(_currentUnitId) && GameHost.UnitRegistry.TryGetValue(_currentUnitId, out var uMeta) &&
				uMeta.TryGetObjectAttachment(hand, attachmentId, out var unitOrient))
			{
				if (unitOrient.Scale > 0f) scale = unitOrient.Scale;
				pos = unitOrient.Position;
				rot = unitOrient.RotationDegrees;
			}

			loaded.Position = pos;
			loaded.RotationDegrees = rot;
			loaded.Scale = Vector3.One * (scale <= 0f ? 1.0f : scale);
			targetBone.AddChild(loaded);

			if (hand == HumanoidBone.RightHand) _rightHandModelNode = loaded;
			else _leftHandModelNode = loaded;
		}
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
			Hud?.SaveCustomUnitAnimations(_currentUnitId, _workingAnimations);
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
