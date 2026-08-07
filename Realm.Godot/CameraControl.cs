using Godot;
using Realm.Ecs.Components.Core;

public partial class CameraControl : Camera3D
{
	private bool HasCameraState => GameHost.Instance?.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<CameraState>(GameHost.Instance.WorldEntity);

	[Export]
	public float MoveSpeed
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).MoveSpeed : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.MoveSpeed = value;
			}
		}
	}

	[Export]
	public float ZoomSpeed
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).ZoomSpeed : 10.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.ZoomSpeed = value;
			}
		}
	}

	[Export]
	public float MinZoom
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).MinZoom : 10.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.MinZoom = value;
			}
		}
	}

	[Export]
	public float MaxZoom
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).MaxZoom : 60.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.MaxZoom = value;
			}
		}
	}

	[Export]
	public float ZoomStep
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).ZoomStep : 4.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.ZoomStep = value;
			}
		}
	}

	[Export]
	public float EdgePanMargin
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).EdgePanMargin : 20.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.EdgePanMargin = value;
			}
		}
	}

	[Export]
	public bool EnableEdgePanning
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).EnableEdgePanning : true;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.EnableEdgePanning = value;
			}
		}
	}

	[Export]
	public bool IsLocked
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).IsLocked : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.IsLocked = value;
			}
		}
	}

	public Node3D FollowTarget { get; set; } = null;

	public float? LimitLeft
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).LimitLeft : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.LimitLeft = value;
			}
		}
	}

	public float? LimitRight
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).LimitRight : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.LimitRight = value;
			}
		}
	}

	public float? LimitTop
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).LimitTop : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.LimitTop = value;
			}
		}
	}

	public float? LimitBottom
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).LimitBottom : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.LimitBottom = value;
			}
		}
	}

	private float _targetHeight
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).TargetHeight : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.TargetHeight = value;
			}
		}
	}

	private float _currentHeight
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).CurrentHeight : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.CurrentHeight = value;
			}
		}
	}

	private bool _isDraggingMouse
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).IsDraggingMouse : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.IsDraggingMouse = value;
			}
		}
	}

	private Vector2 _lastMousePosition
	{
		get
		{
			if (HasCameraState)
			{
				var pos = GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).LastMousePosition;
				return new Vector2(pos.X, pos.Y);
			}
			return Vector2.Zero;
		}
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.LastMousePosition = new System.Numerics.Vector2(value.X, value.Y);
			}
		}
	}

	private float _targetYaw
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).TargetYaw : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.TargetYaw = value;
			}
		}
	}

	private float _currentYaw
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).CurrentYaw : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.CurrentYaw = value;
			}
		}
	}

	private float _targetPitch
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).TargetPitch : -55.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.TargetPitch = value;
			}
		}
	}

	private float _currentPitch
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).CurrentPitch : -55.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.CurrentPitch = value;
			}
		}
	}

	private bool _isTopDown
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).IsTopDown : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.IsTopDown = value;
			}
		}
	}

	private float _yawSwing
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).YawSwing : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.YawSwing = value;
			}
		}
	}

	private float _pitchSwing
	{
		get => HasCameraState ? GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity).PitchSwing : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
				state.PitchSwing = value;
			}
		}
	}

	public void ToggleTopDown()
	{
		_isTopDown = !_isTopDown;
		_targetPitch = _isTopDown ? -90.0f : -55.0f;
	}

	public bool IsTopDown()
	{
		return _isTopDown;
	}

	public void Rotate90Degrees()
	{
		_targetYaw = (_targetYaw + 90.0f) % 360.0f;
	}

	public void CycleZoom()
	{
		if (IsLocked) return;

		if (_targetHeight < 25.0f)
		{
			_targetHeight = 35.0f;
		}
		else if (_targetHeight < 45.0f)
		{
			_targetHeight = 55.0f;
		}
		else
		{
			_targetHeight = 15.0f;
		}
	}

	public void ResetRotationAndCycleZoom()
	{
		_targetYaw = 0.0f;
		CycleZoom();
	}

	private float GetMaxZoom()
	{
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			return MaxZoom * 5.0f;
		}
		return MaxZoom;
	}

	public void ZoomIn()
	{
		if (IsLocked) return;
		_targetHeight = Mathf.Clamp(_targetHeight - ZoomStep, MinZoom, GetMaxZoom());
	}

	public void ZoomOut()
	{
		if (IsLocked) return;
		_targetHeight = Mathf.Clamp(_targetHeight + ZoomStep, MinZoom, GetMaxZoom());
	}

	private const float MapLimit = 95f;

	public void FocusOnPosition(Vector3 targetPos)
	{
		float offsetZ = _isTopDown ? 0.0f : 15.0f;
		Position = new Vector3(targetPos.X, Position.Y, targetPos.Z + offsetZ);
	}

	public override void _Ready()
	{
		RotationDegrees = new Vector3(-55.0f, 0.0f, 0.0f);
		Position = new Vector3(0.0f, _currentHeight, 25.0f);
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _Input(InputEvent @event)
	{
		if (IsLocked || (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)) return;

		if (@event is InputEventMouseButton mouseBtn)
		{
			if (mouseBtn.Pressed)
			{
				if (mouseBtn.ButtonIndex == MouseButton.WheelUp || mouseBtn.ButtonIndex == MouseButton.WheelDown)
				{
					if (mouseBtn.AltPressed)
					{
						return;
					}
					bool shiftPressed = Input.IsKeyPressed(Key.Shift);
					bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
					if ((shiftPressed || ctrlPressed) && GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
					{
						return;
					}
				}

				if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
				{
					_targetHeight = Mathf.Clamp(_targetHeight - ZoomStep, MinZoom, GetMaxZoom());
				}
				else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
				{
					_targetHeight = Mathf.Clamp(_targetHeight + ZoomStep, MinZoom, GetMaxZoom());
				}
				else if (mouseBtn.ButtonIndex == MouseButton.Middle)
				{
					_isDraggingMouse = true;
					_lastMousePosition = mouseBtn.Position;
				}
			}
			else if (mouseBtn.ButtonIndex == MouseButton.Middle)
			{
				_isDraggingMouse = false;
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDraggingMouse)
		{
			FollowTarget = null;
			Vector2 deltaMouse = mouseMotion.Position - _lastMousePosition;
			_lastMousePosition = mouseMotion.Position;

			if (Input.IsKeyPressed(Key.Shift) && GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
			{
				_targetYaw = (_targetYaw + deltaMouse.X * 0.25f + 360.0f) % 360.0f;
				return;
			}

			float sensFactor = 0.0005f + (GameSettings.MouseSens / 100.0f) * 0.003f;
			float moveX = -deltaMouse.X * sensFactor * _currentHeight;
			float moveZ = deltaMouse.Y * sensFactor * _currentHeight;

			Vector3 forwardXZ = -GlobalTransform.Basis.Z;
			forwardXZ.Y = 0f;
			forwardXZ = forwardXZ.Normalized();

			Vector3 rightXZ = GlobalTransform.Basis.X;
			rightXZ.Y = 0f;
			rightXZ = rightXZ.Normalized();

			Vector3 velocity = (rightXZ * moveX) + (forwardXZ * moveZ);

			Vector3 newPos = Position + velocity;
			if (GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode)
			{
				float rawMinX = LimitLeft ?? -MapLimit;
				float rawMaxX = LimitRight ?? MapLimit;
				float rawMinZ = LimitTop ?? -MapLimit;
				float rawMaxZ = LimitBottom ?? (MapLimit + 30f);

				float minX = Mathf.Min(rawMinX, rawMaxX);
				float maxX = Mathf.Max(rawMinX, rawMaxX);
				float minZ = Mathf.Min(rawMinZ, rawMaxZ);
				float maxZ = Mathf.Max(rawMinZ, rawMaxZ);

				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}
			else
			{
				float leftBound = GameHost.Instance.EditorCameraBoundsLeft;
				float rightBound = GameHost.Instance.EditorCameraBoundsRight;
				float topBound = GameHost.Instance.EditorCameraBoundsTop;
				float bottomBound = GameHost.Instance.EditorCameraBoundsBottom;

				float boundMinX = Mathf.Min(leftBound, rightBound);
				float boundMaxX = Mathf.Max(leftBound, rightBound);
				float boundMinZ = Mathf.Min(topBound, bottomBound);
				float boundMaxZ = Mathf.Max(topBound, bottomBound);

				float rangeX = boundMaxX - boundMinX;
				float rangeZ = boundMaxZ - boundMinZ;

				float paddingX = rangeX * 0.25f;
				float paddingZ = rangeZ * 0.25f;

				float minX = boundMinX - paddingX;
				float maxX = boundMaxX + paddingX;
				float minZ = boundMinZ - paddingZ;
				float maxZ = boundMaxZ + paddingZ;

				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}


			Position = newPos;
		}
	}

	public override void _Process(double delta)
	{
		float fDelta = (float)delta;

		if (!HasCameraState) return;

		ref var state = ref GameHost.Instance.EcsWorld.Get<CameraState>(GameHost.Instance.WorldEntity);
		state.MoveSpeed = 10.0f + (GameSettings.ScrollSpeed / 100.0f) * 50.0f;

		float terrainHeight = 0.0f;
		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(Position.X, Position.Z, out terrainHeight, out _);
		}

		float minAllowedTargetHeight = terrainHeight + state.MinZoom;
		if (state.TargetHeight < minAllowedTargetHeight)
		{
			state.TargetHeight = minAllowedTargetHeight;
		}

		state.CurrentHeight = Mathf.Lerp(state.CurrentHeight, state.TargetHeight, state.ZoomSpeed * fDelta);
		float smoothY = Mathf.Lerp(Position.Y, state.TargetHeight, 3.0f * fDelta);

		if (FollowTarget != null && GodotObject.IsInstanceValid(FollowTarget))
		{
			Position = new Vector3(FollowTarget.Position.X, smoothY, FollowTarget.Position.Z + 25.0f);
		}
		else
		{
			Position = new Vector3(Position.X, smoothY, Position.Z);
		}

		bool isEditor = GameHost.Instance != null && GameHost.Instance.IsMapEditorMode;
		if (isEditor)
		{
			if (Input.IsKeyPressed(Key.Comma))
			{
				state.TargetYaw = (state.TargetYaw - 90.0f * fDelta + 360.0f) % 360.0f;
			}
			if (Input.IsKeyPressed(Key.Period))
			{
				state.TargetYaw = (state.TargetYaw + 90.0f * fDelta) % 360.0f;
			}
		}

		bool isInputBlocked = state.IsLocked || (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive);

		if (!isEditor)
		{
			if (!isInputBlocked && Input.IsKeyPressed(Key.Insert))
			{
				state.YawSwing = Mathf.MoveToward(state.YawSwing, 90.0f, 45.0f * fDelta);
			}
			else if (!isInputBlocked && Input.IsKeyPressed(Key.Delete))
			{
				state.YawSwing = Mathf.MoveToward(state.YawSwing, -90.0f, 45.0f * fDelta);
			}
			else
			{
				state.YawSwing = Mathf.MoveToward(state.YawSwing, 0.0f, 45.0f * fDelta);
			}

			if (!isInputBlocked && Input.IsKeyPressed(Key.Pageup))
			{
				state.PitchSwing = Mathf.MoveToward(state.PitchSwing, 45.0f, 22.5f * fDelta);
			}
			else if (!isInputBlocked && Input.IsKeyPressed(Key.Pagedown))
			{
				state.PitchSwing = Mathf.MoveToward(state.PitchSwing, -45.0f, 22.5f * fDelta);
			}
			else
			{
				state.PitchSwing = Mathf.MoveToward(state.PitchSwing, 0.0f, 22.5f * fDelta);
			}
		}
		else
		{
			state.YawSwing = 0.0f;
			state.PitchSwing = 0.0f;
		}

		state.CurrentYaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(state.CurrentYaw), Mathf.DegToRad(state.TargetYaw), 10.0f * fDelta));
		state.CurrentPitch = Mathf.Lerp(state.CurrentPitch, state.TargetPitch, 10.0f * fDelta);
		RotationDegrees = new Vector3(state.CurrentPitch + state.PitchSwing, state.CurrentYaw + state.YawSwing, 0.0f);

		if (state.IsLocked || (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)) return;

		Vector3 velocity = Vector3.Zero;
		float yawRad = Mathf.DegToRad(state.CurrentYaw);
		Vector3 arrowForward = new Vector3(-Mathf.Sin(yawRad), 0f, -Mathf.Cos(yawRad));
		Vector3 arrowRight   = new Vector3( Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

		if (Input.IsKeyPressed(Key.Up))
			velocity += arrowForward;
		if (Input.IsKeyPressed(Key.Down))
			velocity -= arrowForward;
		if (Input.IsKeyPressed(Key.Left))
			velocity -= arrowRight;
		if (Input.IsKeyPressed(Key.Right))
			velocity += arrowRight;

		if (state.EnableEdgePanning && Input.MouseMode == Input.MouseModeEnum.Visible)
		{
			bool isModifyingSlider = isEditor && Input.IsMouseButtonPressed(MouseButton.Left) && IsModifyingHudSlider();
			if (!isModifyingSlider)
			{
				Vector2 mousePos = GetViewport().GetMousePosition();
				Vector2 windowSize = GetViewport().GetVisibleRect().Size;

				if (mousePos.X >= 0 && mousePos.X < windowSize.X && mousePos.Y >= 0 && mousePos.Y < windowSize.Y)
				{
					if (mousePos.X < state.EdgePanMargin)
						velocity -= arrowRight;
					else if (mousePos.X > windowSize.X - state.EdgePanMargin)
						velocity += arrowRight;

					if (mousePos.Y < state.EdgePanMargin)
						velocity += arrowForward;
					else if (mousePos.Y > windowSize.Y - state.EdgePanMargin)
						velocity -= arrowForward;
				}
			}
		}

		if (velocity != Vector3.Zero)
		{
			FollowTarget = null;
			velocity = velocity.Normalized() * state.MoveSpeed * fDelta;

			float maxZoom = isEditor ? state.MaxZoom * 5.0f : state.MaxZoom;
			float zoomFactor = state.CurrentHeight / maxZoom;
			velocity *= Mathf.Lerp(0.5f, 1.5f, zoomFactor);

			Vector3 newPos = Position + velocity;
			if (!isEditor)
			{
				float rawMinX = state.LimitLeft ?? -MapLimit;
				float rawMaxX = state.LimitRight ?? MapLimit;
				float rawMinZ = state.LimitTop ?? -MapLimit;
				float rawMaxZ = state.LimitBottom ?? (MapLimit + 30f);

				float minX = Mathf.Min(rawMinX, rawMaxX);
				float maxX = Mathf.Max(rawMinX, rawMaxX);
				float minZ = Mathf.Min(rawMinZ, rawMaxZ);
				float maxZ = Mathf.Max(rawMinZ, rawMaxZ);

				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}
			else if (GameHost.Instance != null)
			{
				float leftBound = GameHost.Instance.EditorCameraBoundsLeft;
				float rightBound = GameHost.Instance.EditorCameraBoundsRight;
				float topBound = GameHost.Instance.EditorCameraBoundsTop;
				float bottomBound = GameHost.Instance.EditorCameraBoundsBottom;

				float boundMinX = Mathf.Min(leftBound, rightBound);
				float boundMaxX = Mathf.Max(leftBound, rightBound);
				float boundMinZ = Mathf.Min(topBound, bottomBound);
				float boundMaxZ = Mathf.Max(topBound, bottomBound);

				float rangeX = boundMaxX - boundMinX;
				float rangeZ = boundMaxZ - boundMinZ;

				float paddingX = rangeX * 0.25f;
				float paddingZ = rangeZ * 0.25f;

				float minX = boundMinX - paddingX;
				float maxX = boundMaxX + paddingX;
				float minZ = boundMinZ - paddingZ;
				float maxZ = boundMaxZ + paddingZ;

				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}

			Position = newPos;
		}
	}

	private bool IsModifyingHudSlider()
	{
		if (!Input.IsMouseButtonPressed(MouseButton.Left))
		{
			if (MapEditorHUD.IsDraggingSlider)
			{
				MapEditorHUD.IsDraggingSlider = false;
			}
			return false;
		}

		if (MapEditorHUD.IsDraggingSlider) return true;

		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			if (viewport.GuiGetFocusOwner() is Slider) return true;
			if (viewport.GuiGetHoveredControl() is Slider)
			{
				MapEditorHUD.IsDraggingSlider = true;
				return true;
			}
		}

		return false;
	}
}
