using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class ObjectAttachmentDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewSceneRoot;
	private Node3D _previewModel;

	private readonly Dictionary<HumanoidBone, BoneAttachment3D> _boneAttachments = new();
	private readonly Dictionary<HumanoidBone, Node3D> _boneModelNodes = new();

	private Label _lblUnitValue;
	private OptionButton _optSocket;
	private OptionButton _optAttachmentPicker;

	private HSlider _sliderPosX;
	private HSlider _sliderPosY;
	private HSlider _sliderPosZ;
	private Label _lblPosX;
	private Label _lblPosY;
	private Label _lblPosZ;

	private HSlider _sliderRotX;
	private HSlider _sliderRotY;
	private HSlider _sliderRotZ;
	private Label _lblRotX;
	private Label _lblRotY;
	private Label _lblRotZ;

	private HSlider _sliderScaleX;
	private HSlider _sliderScaleY;
	private HSlider _sliderScaleZ;
	private Label _lblScaleX;
	private Label _lblScaleY;
	private Label _lblScaleZ;

	private HSlider _sliderNormalOffset;
	private Label _lblNormalOffset;

	private string _currentUnitId = string.Empty;
	private string _currentAttachmentId = string.Empty;
	private HumanoidBone _currentEditingSocket = HumanoidBone.RightHand;

	private Vector3 _currentPosOffset = Vector3.Zero;
	private Vector3 _currentRotOffset = Vector3.Zero;
	private Vector3 _currentScaleOffset = Vector3.One;
	private float _currentNormalOffset = 0.0f;

	private List<string> _availableAttachments = new();
	private bool _isUpdatingUI;

	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;
	private float _cameraYaw = 0f;
	private float _cameraPitch = 0.2f;
	private float _cameraDistance = 3.5f;
	private const float DefaultDistance = 3.5f;
	private Vector3 _targetPosition = new Vector3(0f, 1.0f, 0f);

	private Action<GameHost.HandAttachmentOrientation> _onApplied;

	private static readonly (HumanoidBone Bone, string DisplayName)[] SupportedSockets = new[]
	{
		(HumanoidBone.RightHand, "Right Hand (Weapon)"),
		(HumanoidBone.LeftHand, "Left Hand (Offhand/Shield)"),
		(HumanoidBone.Chest, "Chest (Torso Aura)"),
		(HumanoidBone.Hips, "Hips (Root/Foot Ring)"),
		(HumanoidBone.Head, "Head (Crown Glow)"),
		(HumanoidBone.LeftFoot, "Left Foot"),
		(HumanoidBone.RightFoot, "Right Foot")
	};

	public ObjectAttachmentDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Socket & VFX Attachment Studio"), new Vector2(560, 620))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(530, 230), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		_previewSceneRoot = new Node3D { Name = "PreviewRoot" };
		_subViewport.AddChild(_previewSceneRoot);

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);

		AddButton(presetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View front", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View side", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 0f), "View back", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera zoom and position", 10, new Vector2(0, 22));

		topControlsVBox.AddChild(presetRow);

		var infoRow = new HBoxContainer();
		infoRow.AddThemeConstantOverride("separation", 8);

		var lblUnitTitle = new Label { Text = TranslationServer.Translate("Unit:") };
		lblUnitTitle.AddThemeFontSizeOverride("font_size", 11);
		lblUnitTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoRow.AddChild(lblUnitTitle);

		_lblUnitValue = new Label { Text = "-" };
		_lblUnitValue.AddThemeFontSizeOverride("font_size", 11);
		_lblUnitValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		infoRow.AddChild(_lblUnitValue);

		var lblSocketTitle = new Label { Text = TranslationServer.Translate("Socket:") };
		lblSocketTitle.AddThemeFontSizeOverride("font_size", 11);
		lblSocketTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoRow.AddChild(lblSocketTitle);

		_optSocket = new OptionButton();
		_optSocket.AddThemeFontSizeOverride("font_size", 11);
		_optSocket.CustomMinimumSize = new Vector2(160, 24);
		for (int i = 0; i < SupportedSockets.Length; i++)
		{
			_optSocket.AddItem(TranslationServer.Translate(SupportedSockets[i].DisplayName), i);
		}
		_optSocket.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentEditingSocket = SupportedSockets[(int)idx].Bone;
			LoadAttachmentOrientationIntoSliders(_currentUnitId, _currentEditingSocket, _currentAttachmentId);
			AttachModelToSocket(_currentEditingSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset);
		};
		infoRow.AddChild(_optSocket);

		topControlsVBox.AddChild(infoRow);

		var pickerRow = new HBoxContainer();
		pickerRow.AddThemeConstantOverride("separation", 6);

		var lblAttTitle = new Label { Text = TranslationServer.Translate("Attachment / VFX:") };
		lblAttTitle.AddThemeFontSizeOverride("font_size", 11);
		lblAttTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		pickerRow.AddChild(lblAttTitle);

		_optAttachmentPicker = new OptionButton();
		_optAttachmentPicker.AddThemeFontSizeOverride("font_size", 11);
		_optAttachmentPicker.CustomMinimumSize = new Vector2(220, 24);
		_optAttachmentPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optAttachmentPicker.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _availableAttachments.Count)
			{
				_currentAttachmentId = _availableAttachments[(int)idx];
				LoadAttachmentOrientationIntoSliders(_currentUnitId, _currentEditingSocket, _currentAttachmentId);
				AttachModelToSocket(_currentEditingSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset);
			}
		};
		pickerRow.AddChild(_optAttachmentPicker);

		AddButton(pickerRow, "✨ " + TranslationServer.Translate("VFX Studio"), () =>
		{
			Hud?.OpenVfxStudioDialog(null, (cfg) =>
			{
				RefreshAttachmentList();
				_currentAttachmentId = cfg.VfxId;
				LoadAttachmentOrientationIntoSliders(_currentUnitId, _currentEditingSocket, _currentAttachmentId);
				AttachModelToSocket(_currentEditingSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset);
			});
		}, "Open Procedural VFX Studio", 10, new Vector2(100, 24));

		topControlsVBox.AddChild(pickerRow);

		AddSectionHeader(BodyContainer, "🗡️ " + TranslationServer.Translate("TRANSFORM & NORMAL OFFSET"));

		var transformCols = new HBoxContainer();
		transformCols.AddThemeConstantOverride("separation", 12);

		var leftCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		leftCol.AddThemeConstantOverride("separation", 4);
		var rightCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rightCol.AddThemeConstantOverride("separation", 4);

		var posResultX = AddSlider(leftCol, TranslationServer.Translate("Position X:"), -1.5f, 1.5f, 0.005f, 0f, (v) => { _currentPosOffset.X = v; UpdateActiveAttachmentTransform(); }, "0.000", 90f);
		_sliderPosX = posResultX.Slider; _lblPosX = posResultX.ValueLabel;

		var posResultY = AddSlider(leftCol, TranslationServer.Translate("Position Y:"), -1.5f, 1.5f, 0.005f, 0f, (v) => { _currentPosOffset.Y = v; UpdateActiveAttachmentTransform(); }, "0.000", 90f);
		_sliderPosY = posResultY.Slider; _lblPosY = posResultY.ValueLabel;

		var posResultZ = AddSlider(leftCol, TranslationServer.Translate("Position Z:"), -1.5f, 1.5f, 0.005f, 0f, (v) => { _currentPosOffset.Z = v; UpdateActiveAttachmentTransform(); }, "0.000", 90f);
		_sliderPosZ = posResultZ.Slider; _lblPosZ = posResultZ.ValueLabel;

		var rotResultX = AddSlider(rightCol, TranslationServer.Translate("Pitch X (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.X = v; UpdateActiveAttachmentTransform(); }, "0", 95f);
		_sliderRotX = rotResultX.Slider; _lblRotX = rotResultX.ValueLabel;

		var rotResultY = AddSlider(rightCol, TranslationServer.Translate("Yaw Y (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.Y = v; UpdateActiveAttachmentTransform(); }, "0", 95f);
		_sliderRotY = rotResultY.Slider; _lblRotY = rotResultY.ValueLabel;

		var rotResultZ = AddSlider(rightCol, TranslationServer.Translate("Roll Z (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.Z = v; UpdateActiveAttachmentTransform(); }, "0", 95f);
		_sliderRotZ = rotResultZ.Slider; _lblRotZ = rotResultZ.ValueLabel;

		var scaleResultX = AddSlider(leftCol, TranslationServer.Translate("Scale X:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.X = v; UpdateActiveAttachmentTransform(); }, "0.00", 90f);
		_sliderScaleX = scaleResultX.Slider; _lblScaleX = scaleResultX.ValueLabel;

		var scaleResultY = AddSlider(leftCol, TranslationServer.Translate("Scale Y:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.Y = v; UpdateActiveAttachmentTransform(); }, "0.00", 90f);
		_sliderScaleY = scaleResultY.Slider; _lblScaleY = scaleResultY.ValueLabel;

		var scaleResultZ = AddSlider(leftCol, TranslationServer.Translate("Scale Z:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.Z = v; UpdateActiveAttachmentTransform(); }, "0.00", 90f);
		_sliderScaleZ = scaleResultZ.Slider; _lblScaleZ = scaleResultZ.ValueLabel;

		var normalResult = AddSlider(rightCol, TranslationServer.Translate("Normal Offset:"), -0.5f, 0.5f, 0.005f, 0.0f, (v) => { _currentNormalOffset = v; UpdateActiveAttachmentTransform(); }, "0.000", 95f);
		_sliderNormalOffset = normalResult.Slider; _lblNormalOffset = normalResult.ValueLabel;

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 8);
		AddButton(btnRow, "⚡ " + TranslationServer.Translate("Auto Calculate"), () => AutoCalculateAttachmentOrientation(), "Automatically calculate orientation and position based on attachment bounds and camera view", 11, new Vector2(130, 24));
		AddButton(btnRow, "⟲ " + TranslationServer.Translate("Reset"), () => ResetTransformValues(), "Reset transform to defaults", 11, new Vector2(80, 24));
		rightCol.AddChild(btnRow);

		transformCols.AddChild(leftCol);
		transformCols.AddChild(rightCol);
		BodyContainer.AddChild(transformCols);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	public void OpenForUnitAndAttachment(
		string unitId,
		string attachmentId,
		string hand = "RightHand",
		Node3D sourceModel = null,
		Action<GameHost.HandAttachmentOrientation> onApplied = null)
	{
		_onApplied = onApplied;

		if (string.IsNullOrEmpty(unitId) && GameHost.UnitRegistry.Count > 0)
		{
			unitId = GameHost.UnitRegistry.Keys.First();
		}
		_currentUnitId = unitId ?? string.Empty;

		_currentEditingSocket = ParseBoneSafe(hand);
		_currentAttachmentId = attachmentId ?? string.Empty;

		_lblUnitValue.Text = !string.IsNullOrEmpty(_currentUnitId) ? _currentUnitId : "-";

		RefreshAttachmentList();
		UpdateSocketPickerIndex();

		LoadAttachmentOrientationIntoSliders(_currentUnitId, _currentEditingSocket, _currentAttachmentId);
		LoadUnitModelAndRig(_currentUnitId, sourceModel);

		ResetCameraDefault();
		OpenDialog();
	}

	private static HumanoidBone ParseBoneSafe(string name)
	{
		if (string.IsNullOrEmpty(name)) return HumanoidBone.RightHand;
		if (name.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) || name.Equals("left_hand", StringComparison.OrdinalIgnoreCase)) return HumanoidBone.LeftHand;
		if (name.Equals("Chest", StringComparison.OrdinalIgnoreCase)) return HumanoidBone.Chest;
		if (name.Equals("Root", StringComparison.OrdinalIgnoreCase) || name.Equals("Hips", StringComparison.OrdinalIgnoreCase)) return HumanoidBone.Hips;
		if (name.Equals("Head", StringComparison.OrdinalIgnoreCase)) return HumanoidBone.Head;
		if (name.Equals("LeftFoot", StringComparison.OrdinalIgnoreCase)) return HumanoidBone.LeftFoot;
		if (name.Equals("RightFoot", StringComparison.OrdinalIgnoreCase)) return HumanoidBone.RightFoot;
		return HumanoidBone.RightHand;
	}

	private void UpdateSocketPickerIndex()
	{
		if (_optSocket == null) return;
		_isUpdatingUI = true;
		for (int i = 0; i < SupportedSockets.Length; i++)
		{
			if (SupportedSockets[i].Bone == _currentEditingSocket)
			{
				_optSocket.Selected = i;
				break;
			}
		}
		_isUpdatingUI = false;
	}

	private void RefreshAttachmentList()
	{
		_availableAttachments = GetAvailableObjectAttachmentIds();
		if (_optAttachmentPicker != null)
		{
			_isUpdatingUI = true;
			_optAttachmentPicker.Clear();
			int selectedIdx = 0;
			for (int i = 0; i < _availableAttachments.Count; i++)
			{
				string item = _availableAttachments[i];
				string display = item.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? $"✨ {item.Substring(4)}" : item;
				_optAttachmentPicker.AddItem(display, i);
				if (item.Equals(_currentAttachmentId, StringComparison.OrdinalIgnoreCase))
				{
					selectedIdx = i;
				}
			}
			_optAttachmentPicker.Selected = selectedIdx;
			_isUpdatingUI = false;
		}
	}

	private void LoadAttachmentOrientationIntoSliders(string unitId, HumanoidBone socket, string attachmentId)
	{
		if (!string.IsNullOrEmpty(unitId) && GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta) &&
			uMeta.TryGetObjectAttachment(socket, attachmentId, out var unitOrient))
		{
			_currentPosOffset = unitOrient.Position;
			_currentRotOffset = unitOrient.RotationDegrees;
			_currentScaleOffset = unitOrient.ScaleVector;
			_currentNormalOffset = unitOrient.NormalOffset;
		}
		else if (!string.IsNullOrEmpty(attachmentId) && GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attMeta))
		{
			_currentPosOffset = attMeta.PositionOffset;
			_currentRotOffset = attMeta.RotationOffset;
			_currentScaleOffset = Vector3.One * (attMeta.Scale <= 0f ? 1.0f : attMeta.Scale);
			_currentNormalOffset = 0.0f;
		}
		else
		{
			_currentPosOffset = Vector3.Zero;
			_currentRotOffset = Vector3.Zero;
			_currentScaleOffset = Vector3.One;
			_currentNormalOffset = 0.0f;
		}

		UpdateSliderDisplayValues();
	}

	private void UpdateSliderDisplayValues()
	{
		if (_sliderPosX != null) { _sliderPosX.Value = _currentPosOffset.X; _lblPosX.Text = _currentPosOffset.X.ToString("F3"); }
		if (_sliderPosY != null) { _sliderPosY.Value = _currentPosOffset.Y; _lblPosY.Text = _currentPosOffset.Y.ToString("F3"); }
		if (_sliderPosZ != null) { _sliderPosZ.Value = _currentPosOffset.Z; _lblPosZ.Text = _currentPosOffset.Z.ToString("F3"); }

		if (_sliderRotX != null) { _sliderRotX.Value = _currentRotOffset.X; _lblRotX.Text = _currentRotOffset.X.ToString("F0"); }
		if (_sliderRotY != null) { _sliderRotY.Value = _currentRotOffset.Y; _lblRotY.Text = _currentRotOffset.Y.ToString("F0"); }
		if (_sliderRotZ != null) { _sliderRotZ.Value = _currentRotOffset.Z; _lblRotZ.Text = _currentRotOffset.Z.ToString("F0"); }

		if (_sliderScaleX != null) { _sliderScaleX.Value = _currentScaleOffset.X; _lblScaleX.Text = _currentScaleOffset.X.ToString("F2"); }
		if (_sliderScaleY != null) { _sliderScaleY.Value = _currentScaleOffset.Y; _lblScaleY.Text = _currentScaleOffset.Y.ToString("F2"); }
		if (_sliderScaleZ != null) { _sliderScaleZ.Value = _currentScaleOffset.Z; _lblScaleZ.Text = _currentScaleOffset.Z.ToString("F2"); }

		if (_sliderNormalOffset != null) { _sliderNormalOffset.Value = _currentNormalOffset; _lblNormalOffset.Text = _currentNormalOffset.ToString("F3"); }
	}

	private void ResetTransformValues()
	{
		_currentPosOffset = Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentScaleOffset = Vector3.One;
		_currentNormalOffset = 0.0f;
		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
	}

	private void AutoCalculateAttachmentOrientation()
	{
		_boneAttachments.TryGetValue(_currentEditingSocket, out var targetBone);
		if (targetBone == null || !GodotObject.IsInstanceValid(targetBone)) return;

		_boneModelNodes.TryGetValue(_currentEditingSocket, out var targetNode);
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
		{
			AttachModelToSocket(_currentEditingSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset);
			_boneModelNodes.TryGetValue(_currentEditingSocket, out targetNode);
			if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return;
		}

		Transform3D savedTransform = targetNode.Transform;
		targetNode.Transform = Transform3D.Identity;

		Aabb localAabb = CalculateAttachmentLocalAabb(targetNode);
		targetNode.Transform = savedTransform;

		Vector3 size = localAabb.Size;
		int primaryAxis = 1;
		if (size.X > size.Y && size.X > size.Z) primaryAxis = 0;
		else if (size.Z > size.Y && size.Z > size.X) primaryAxis = 2;

		Vector3 localGripPoint;
		if (primaryAxis == 0)
		{
			float gripX = localAabb.Position.X < -0.05f && localAabb.End.X > 0.05f
				? localAabb.Position.X + size.X * 0.15f
				: localAabb.Position.X;
			localGripPoint = new Vector3(gripX, localAabb.GetCenter().Y, localAabb.GetCenter().Z);
		}
		else if (primaryAxis == 2)
		{
			float gripZ = localAabb.Position.Z < -0.05f && localAabb.End.Z > 0.05f
				? localAabb.Position.Z + size.Z * 0.15f
				: localAabb.Position.Z;
			localGripPoint = new Vector3(localAabb.GetCenter().X, localAabb.GetCenter().Y, gripZ);
		}
		else
		{
			float gripY = localAabb.Position.Y < -0.05f && localAabb.End.Y > 0.05f
				? localAabb.Position.Y + size.Y * 0.15f
				: localAabb.Position.Y;
			localGripPoint = new Vector3(localAabb.GetCenter().X, gripY, localAabb.GetCenter().Z);
		}

		Vector3 handPos = targetBone.GlobalPosition;
		Vector3 camPos = _camera != null ? _camera.GlobalPosition : new Vector3(0f, 1.5f, 3f);
		Vector3 dirToCam = (camPos - handPos).Normalized();

		Vector3 wristNormal = targetBone.GlobalTransform.Basis.Y.Normalized();
		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		if (skeleton != null)
		{
			int lowerArmIdx = HumanoidBoneMapper.FindBoneInSkeleton(skeleton, 
				_currentEditingSocket == HumanoidBone.RightHand ? HumanoidBone.RightLowerArm : HumanoidBone.LeftLowerArm);
			if (lowerArmIdx >= 0)
			{
				Vector3 lowerArmPos = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(lowerArmIdx).Origin;
				wristNormal = (handPos - lowerArmPos).Normalized();
			}
		}

		Vector3 desiredTopDir = (dirToCam * 0.7f + Vector3.Up * 0.5f + wristNormal * 0.3f).Normalized();

		Vector3 desiredRight = desiredTopDir.Cross(Vector3.Up).Normalized();
		if (desiredRight.LengthSquared() < 0.001f)
		{
			Vector3 camRight = _camera != null ? _camera.GlobalTransform.Basis.X.Normalized() : Vector3.Right;
			desiredRight = desiredTopDir.Cross(camRight).Normalized();
		}

		if (_currentEditingSocket == HumanoidBone.LeftHand)
		{
			desiredRight = -desiredRight;
		}

		Vector3 desiredNormal = desiredRight.Cross(desiredTopDir).Normalized();

		Basis desiredWorldBasis;
		if (primaryAxis == 0)
		{
			desiredWorldBasis = new Basis(desiredTopDir, desiredNormal, desiredRight);
		}
		else if (primaryAxis == 2)
		{
			desiredWorldBasis = new Basis(desiredRight, desiredNormal, desiredTopDir);
		}
		else
		{
			desiredWorldBasis = new Basis(desiredRight, desiredTopDir, desiredNormal);
		}

		Basis handBasis = targetBone.GlobalTransform.Basis.Orthonormalized();
		Basis desiredLocalBasis = Mathf.Abs(handBasis.Determinant()) < 0.0001f 
			? desiredWorldBasis.Orthonormalized() 
			: (handBasis.Inverse() * desiredWorldBasis).Orthonormalized();

		Vector3 rotEulerRad = desiredLocalBasis.GetEuler();
		_currentRotOffset = new Vector3(
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.X)),
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.Y)),
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.Z))
		);

		Vector3 localOffset = -(desiredLocalBasis * localGripPoint);
		_currentPosOffset = new Vector3(
			Mathf.Clamp(localOffset.X, -1.5f, 1.5f),
			Mathf.Clamp(localOffset.Y, -1.5f, 1.5f),
			Mathf.Clamp(localOffset.Z, -1.5f, 1.5f)
		);

		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
	}

	private static Aabb CalculateAttachmentLocalAabb(Node3D root)
	{
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;
		if (root == null || Mathf.Abs(root.GlobalTransform.Basis.Determinant()) < 0.0001f)
		{
			return combinedAabb;
		}

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null)
			{
				if (Mathf.Abs(meshInst.GlobalTransform.Basis.Determinant()) < 0.0001f) return;
				Transform3D relXform = root.GlobalTransform.AffineInverse() * meshInst.GlobalTransform;
				Aabb mAabb = meshInst.Mesh.GetAabb();
				Vector3 min = mAabb.Position;
				Vector3 max = mAabb.End;
				Vector3[] corners = new[]
				{
					new Vector3(min.X, min.Y, min.Z),
					new Vector3(min.X, min.Y, max.Z),
					new Vector3(min.X, max.Y, min.Z),
					new Vector3(min.X, max.Y, max.Z),
					new Vector3(max.X, min.Y, min.Z),
					new Vector3(max.X, min.Y, max.Z),
					new Vector3(max.X, max.Y, min.Z),
					new Vector3(max.X, max.Y, max.Z)
				};
				for (int i = 0; i < 8; i++)
				{
					Vector3 pt = relXform * corners[i];
					if (!hasAabb)
					{
						combinedAabb = new Aabb(pt, Vector3.Zero);
						hasAabb = true;
					}
					else
					{
						combinedAabb = combinedAabb.Expand(pt);
					}
				}
			}
			foreach (Node child in current.GetChildren())
			{
				Collect(child);
			}
		}

		Collect(root);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.1f, -0.5f, -0.1f), new Vector3(0.2f, 1.0f, 0.2f));
		}
		return combinedAabb;
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}

	private void ClearPreviewModel()
	{
		if (_previewModel != null && GodotObject.IsInstanceValid(_previewModel))
		{
			_previewModel.QueueFree();
			_previewModel = null;
		}
		_boneAttachments.Clear();
		_boneModelNodes.Clear();
	}

	private void LoadUnitModelAndRig(string unitId, Node3D sourceModel)
	{
		ClearPreviewModel();
		if (string.IsNullOrEmpty(unitId)) return;

		Node3D modelToInstantiate = null;

		if (sourceModel != null && GodotObject.IsInstanceValid(sourceModel))
		{
			modelToInstantiate = (Node3D)sourceModel.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		}
		else if (GameHost.UnitRegistry.TryGetValue(unitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
		{
			var cached = ModelCache.GetModel(meta.ModelPath);
			if (cached is Node3D n3d)
			{
				modelToInstantiate = (Node3D)n3d.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
			}
		}

		if (modelToInstantiate == null) return;

		RemoveAllBoneAttachments(modelToInstantiate);

		_previewModel = modelToInstantiate;
		_previewModel.Position = Vector3.Zero;
		_previewModel.Rotation = Vector3.Zero;
		_previewModel.Scale = Vector3.One;
		_previewSceneRoot.AddChild(_previewModel);

		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		if (skeleton != null)
		{
			if (sourceModel != null && GodotObject.IsInstanceValid(sourceModel))
			{
				var srcSkeleton = SkeletonValidator.FindSkeleton(sourceModel);
				if (srcSkeleton != null)
				{
					int count = Math.Min(srcSkeleton.GetBoneCount(), skeleton.GetBoneCount());
					for (int i = 0; i < count; i++)
					{
						skeleton.SetBonePosePosition(i, srcSkeleton.GetBonePosePosition(i));
						skeleton.SetBonePoseRotation(i, srcSkeleton.GetBonePoseRotation(i));
						skeleton.SetBonePoseScale(i, srcSkeleton.GetBonePoseScale(i));
					}
				}
			}

			foreach (var sock in SupportedSockets)
			{
				int boneIdx = HumanoidBoneMapper.FindBoneInSkeleton(skeleton, sock.Bone);
				if (boneIdx >= 0)
				{
					var ba = new BoneAttachment3D
					{
						Name = $"BoneAttachment_{sock.Bone}",
						BoneName = skeleton.GetBoneName(boneIdx),
						BoneIdx = boneIdx
					};
					skeleton.AddChild(ba);
					_boneAttachments[sock.Bone] = ba;
				}
			}
		}

		AttachModelToSocket(_currentEditingSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset);
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

	private void AttachModelToSocket(HumanoidBone socket, string attachmentId, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset)
	{
		_boneAttachments.TryGetValue(socket, out var targetBone);
		if (targetBone == null || !GodotObject.IsInstanceValid(targetBone)) return;

		foreach (Node child in targetBone.GetChildren())
		{
			targetBone.RemoveChild(child);
			child.QueueFree();
		}

		_boneModelNodes.Remove(socket);

		if (string.IsNullOrEmpty(attachmentId) || attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase)) return;

		Node3D loaded = Unit3D.ResolveAndInstantiateAttachment(attachmentId, out _, out _, out _);
		if (loaded != null)
		{
			loaded.Position = pos + (loaded.Transform.Basis.Y * normalOffset);
			loaded.RotationDegrees = rot;
			loaded.Scale = scale;
			targetBone.AddChild(loaded);
			_boneModelNodes[socket] = loaded;
		}
	}

	private void UpdateActiveAttachmentTransform()
	{
		_boneModelNodes.TryGetValue(_currentEditingSocket, out var node);
		if (node != null && GodotObject.IsInstanceValid(node))
		{
			node.Position = _currentPosOffset + (node.Transform.Basis.Y * _currentNormalOffset);
			node.RotationDegrees = _currentRotOffset;
			node.Scale = _currentScaleOffset;
		}
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_currentUnitId) && !string.IsNullOrEmpty(_currentAttachmentId))
		{
			var orientation = new GameHost.HandAttachmentOrientation
			{
				PositionX = _currentPosOffset.X,
				PositionY = _currentPosOffset.Y,
				PositionZ = _currentPosOffset.Z,
				PitchX = _currentRotOffset.X,
				YawY = _currentRotOffset.Y,
				RollZ = _currentRotOffset.Z,
				Scale = _currentScaleOffset.X,
				ScaleX = _currentScaleOffset.X,
				ScaleY = _currentScaleOffset.Y,
				ScaleZ = _currentScaleOffset.Z,
				NormalOffset = _currentNormalOffset
			};

			Hud?.SaveUnitObjectAttachment(_currentUnitId, _currentEditingSocket, _currentAttachmentId, orientation);
			_onApplied?.Invoke(orientation);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Applied {0} to {1} on socket {2}."), _currentAttachmentId, _currentUnitId, _currentEditingSocket));
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

	public static List<string> GetAvailableObjectAttachmentIds()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();

		foreach (var primType in Enum.GetNames<VfxPrimitiveType>())
		{
			string vfxKey = $"vfx:{primType}";
			if (seen.Add(vfxKey))
			{
				result.Add(vfxKey);
			}
		}

		if (GameHost.VfxRegistry != null)
		{
			foreach (var kvp in GameHost.VfxRegistry)
			{
				string vfxKey = $"vfx:{kvp.Key}";
				if (seen.Add(vfxKey))
				{
					result.Add(vfxKey);
				}
			}
		}

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");
		if (!System.IO.File.Exists(metadataPath))
		{
			string tPath = PathUtils.FindPath("MapTemplate/metadata.json");
			if (System.IO.File.Exists(tPath)) metadataPath = tPath;
		}

		if (System.IO.File.Exists(metadataPath))
		{
			try
			{
				string jsonStr = System.IO.File.ReadAllText(metadataPath);
				var root = JsonNode.Parse(jsonStr)?.AsObject();
				var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath);
				var glbObj = assetsObj?["glb"]?.AsObject();
				if (glbObj != null)
				{
					foreach (var subCat in glbObj)
					{
						if (subCat.Value is JsonObject modelsObj)
						{
							foreach (var modelProp in modelsObj)
							{
								string fileName = modelProp.Key;
								string id = System.IO.Path.GetFileNameWithoutExtension(fileName);
								bool isAttachment = subCat.Key.Equals("attachments", StringComparison.OrdinalIgnoreCase);

								if (!isAttachment && modelProp.Value is JsonObject mObj)
								{
									string? at = mObj["asset_type"]?.ToString()
										?? mObj["AssetType"]?.ToString()
										?? mObj["default_asset_type"]?.ToString()
										?? mObj["type"]?.ToString();
									if (!string.IsNullOrEmpty(at) && (
										at.Equals("Attachment", StringComparison.OrdinalIgnoreCase) ||
										at.Equals("Weapon", StringComparison.OrdinalIgnoreCase) ||
										at.Equals("Object Attachments", StringComparison.OrdinalIgnoreCase) ||
										at.Equals("glb_attachments", StringComparison.OrdinalIgnoreCase) ||
										at.Equals("attachments", StringComparison.OrdinalIgnoreCase) ||
										at.Equals("weapons", StringComparison.OrdinalIgnoreCase)))
									{
										isAttachment = true;
									}
								}

								if (isAttachment && seen.Add(id))
								{
									result.Add(id);
								}
							}
						}
					}
				}

				if (root?["CustomAttachments"] is JsonArray customAtts)
				{
					foreach (var node in customAtts)
					{
						if (node is JsonObject attObj)
						{
							string? attId = attObj["AttachmentId"]?.ToString() ?? attObj["attachment_id"]?.ToString();
							if (!string.IsNullOrEmpty(attId))
							{
								string cleanId = System.IO.Path.GetFileNameWithoutExtension(attId);
								if (seen.Add(cleanId))
								{
									result.Add(cleanId);
								}
							}
						}
					}
				}

				if (root?["CustomVfx"] is JsonArray customVfxArr)
				{
					foreach (var node in customVfxArr)
					{
						if (node is JsonObject vfxObj)
						{
							string? vId = vfxObj["VfxId"]?.ToString() ?? vfxObj["vfxId"]?.ToString();
							if (!string.IsNullOrEmpty(vId))
							{
								string vfxKey = $"vfx:{vId}";
								if (seen.Add(vfxKey))
								{
									result.Add(vfxKey);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ObjectAttachmentDialog] Error scanning attachment assets: {ex.Message}");
			}
		}

		foreach (var kvp in GameHost.AttachmentRegistry)
		{
			string id = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
			if (seen.Add(id))
			{
				result.Add(id);
			}
		}

		string attachmentsDir = System.IO.Path.Combine(wsPath, "Assets", "models", "attachments");
		if (System.IO.Directory.Exists(attachmentsDir))
		{
			foreach (var file in System.IO.Directory.GetFiles(attachmentsDir, "*.glb"))
			{
				string id = System.IO.Path.GetFileNameWithoutExtension(file);
				if (seen.Add(id))
				{
					result.Add(id);
				}
			}
		}

		string templateDir = PathUtils.FindPath("MapTemplate/Assets/models/attachments");
		if (!string.IsNullOrEmpty(templateDir) && System.IO.Directory.Exists(templateDir))
		{
			foreach (var file in System.IO.Directory.GetFiles(templateDir, "*.glb"))
			{
				string id = System.IO.Path.GetFileNameWithoutExtension(file);
				if (seen.Add(id))
				{
					result.Add(id);
				}
			}
		}

		return result;
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
		UpdateCameraTransform();
	}

	public void ResetCameraDefault()
	{
		_cameraYaw = 0f;
		_cameraPitch = 0.2f;
		_cameraDistance = DefaultDistance;
		_targetPosition = new Vector3(0f, 1.0f, 0f);
		UpdateCameraTransform();
	}

	private void UpdateCameraTransform()
	{
		if (_camera == null) return;
		_cameraPitch = Mathf.Clamp(_cameraPitch, -Mathf.Pi * 0.48f, Mathf.Pi * 0.48f);

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
		if (newPos.DistanceSquaredTo(_targetPosition) > 0.0001f)
		{
			Vector3 dir = (_targetPosition - newPos).Normalized();
			Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			_camera.LookAtFromPosition(newPos, _targetPosition, up);
		}
	}
}
