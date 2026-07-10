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
			float moveZ = -deltaMouse.Y * sensFactor * _currentHeight;

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
				float minX = LimitLeft ?? -MapLimit;
				float maxX = LimitRight ?? MapLimit;
				float minZ = LimitTop ?? -MapLimit;
				float maxZ = LimitBottom ?? (MapLimit + 30f);
				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}
			else
			{
				float leftBound = GameHost.Instance.EditorCameraBoundsLeft;
				float rightBound = GameHost.Instance.EditorCameraBoundsRight;
				float topBound = GameHost.Instance.EditorCameraBoundsTop;
				float bottomBound = GameHost.Instance.EditorCameraBoundsBottom;

				float rangeX = rightBound - leftBound;
				float rangeZ = bottomBound - topBound;

				float paddingX = rangeX * 0.25f;
				float paddingZ = rangeZ * 0.25f;

				float minX = leftBound - paddingX;
				float maxX = rightBound + paddingX;
				float minZ = topBound - paddingZ;
				float maxZ = bottomBound + paddingZ;

				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}


			Position = newPos;
		}
	}

	public override void _Process(double delta)
	{
		float fDelta = (float)delta;

		MoveSpeed = 10.0f + (GameSettings.ScrollSpeed / 100.0f) * 50.0f;

		_currentHeight = Mathf.Lerp(_currentHeight, _targetHeight, ZoomSpeed * fDelta);
		if (FollowTarget != null && GodotObject.IsInstanceValid(FollowTarget))
		{
			Position = new Vector3(FollowTarget.Position.X, _currentHeight, FollowTarget.Position.Z + 25.0f);
		}
		else
		{
			Position = new Vector3(Position.X, _currentHeight, Position.Z);
		}

		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			if (Input.IsKeyPressed(Key.Comma))
			{
				_targetYaw = (_targetYaw - 90.0f * fDelta + 360.0f) % 360.0f;
			}
			if (Input.IsKeyPressed(Key.Period))
			{
				_targetYaw = (_targetYaw + 90.0f * fDelta) % 360.0f;
			}
		}

		bool isEditor = GameHost.Instance != null && GameHost.Instance.IsMapEditorMode;
		bool isInputBlocked = IsLocked || (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive);

		if (!isEditor)
		{
			if (!isInputBlocked && Input.IsKeyPressed(Key.Insert))
			{
				_yawSwing = Mathf.MoveToward(_yawSwing, 90.0f, 45.0f * fDelta);
			}
			else if (!isInputBlocked && Input.IsKeyPressed(Key.Delete))
			{
				_yawSwing = Mathf.MoveToward(_yawSwing, -90.0f, 45.0f * fDelta);
			}
			else
			{
				_yawSwing = Mathf.MoveToward(_yawSwing, 0.0f, 45.0f * fDelta);
			}

			if (!isInputBlocked && Input.IsKeyPressed(Key.Pageup))
			{
				_pitchSwing = Mathf.MoveToward(_pitchSwing, 45.0f, 22.5f * fDelta);
			}
			else if (!isInputBlocked && Input.IsKeyPressed(Key.Pagedown))
			{
				_pitchSwing = Mathf.MoveToward(_pitchSwing, -45.0f, 22.5f * fDelta);
			}
			else
			{
				_pitchSwing = Mathf.MoveToward(_pitchSwing, 0.0f, 22.5f * fDelta);
			}
		}
		else
		{
			_yawSwing = 0.0f;
			_pitchSwing = 0.0f;
		}

		_currentYaw = Mathf.LerpAngle(Mathf.DegToRad(_currentYaw), Mathf.DegToRad(_targetYaw), 10.0f * fDelta);
		_currentYaw = Mathf.RadToDeg(_currentYaw);
		_currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, 10.0f * fDelta);
		RotationDegrees = new Vector3(_currentPitch + _pitchSwing, _currentYaw + _yawSwing, 0.0f);

		if (IsLocked || (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)) return;


		Vector3 velocity = Vector3.Zero;

		float yawRad = Mathf.DegToRad(_currentYaw);
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

		if (EnableEdgePanning && Input.MouseMode == Input.MouseModeEnum.Visible)
		{
			Vector2 mousePos = GetViewport().GetMousePosition();
			Vector2 windowSize = GetViewport().GetVisibleRect().Size;

			if (mousePos.X >= 0 && mousePos.X < windowSize.X && mousePos.Y >= 0 && mousePos.Y < windowSize.Y)
			{
				if (mousePos.X < EdgePanMargin)
					velocity -= arrowRight;
				else if (mousePos.X > windowSize.X - EdgePanMargin)
					velocity += arrowRight;

				if (mousePos.Y < EdgePanMargin)
					velocity += arrowForward;
				else if (mousePos.Y > windowSize.Y - EdgePanMargin)
					velocity -= arrowForward;
			}
		}

		if (velocity != Vector3.Zero)
		{
			FollowTarget = null;
			velocity = velocity.Normalized() * MoveSpeed * fDelta;

			float zoomFactor = _currentHeight / GetMaxZoom();
			velocity *= Mathf.Lerp(0.5f, 1.5f, zoomFactor);

			Vector3 newPos = Position + velocity;
			if (GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode)
			{
				float minX = LimitLeft ?? -MapLimit;
				float maxX = LimitRight ?? MapLimit;
				float minZ = LimitTop ?? -MapLimit;
				float maxZ = LimitBottom ?? (MapLimit + 30f);
				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}
			else
			{
				float leftBound = GameHost.Instance.EditorCameraBoundsLeft;
				float rightBound = GameHost.Instance.EditorCameraBoundsRight;
				float topBound = GameHost.Instance.EditorCameraBoundsTop;
				float bottomBound = GameHost.Instance.EditorCameraBoundsBottom;

				float rangeX = rightBound - leftBound;
				float rangeZ = bottomBound - topBound;

				float paddingX = rangeX * 0.25f;
				float paddingZ = rangeZ * 0.25f;

				float minX = leftBound - paddingX;
				float maxX = rightBound + paddingX;
				float minZ = topBound - paddingZ;
				float maxZ = bottomBound + paddingZ;

				newPos.X = Mathf.Clamp(newPos.X, minX, maxX);
				newPos.Z = Mathf.Clamp(newPos.Z, minZ, maxZ);
			}


			Position = newPos;
		}
	}
}
