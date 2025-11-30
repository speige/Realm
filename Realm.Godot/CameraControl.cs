using Godot;

public partial class CameraControl : Camera3D
{
	[Export] public float MoveSpeed = 35.0f;
	[Export] public float ZoomSpeed = 10.0f;
	[Export] public float MinZoom = 10.0f;
	[Export] public float MaxZoom = 60.0f;
	[Export] public float ZoomStep = 4.0f;
	[Export] public float EdgePanMargin = 20.0f;
	[Export] public bool EnableEdgePanning = true;

	[Export] public bool IsLocked { get; set; } = false;

	private float _targetHeight = 35.0f;
	private float _currentHeight = 35.0f;
	private bool _isDraggingMouse = false;
	private Vector2 _lastMousePosition = Vector2.Zero;
	private float _targetYaw = 0.0f;
	private float _currentYaw = 0.0f;

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

	public void ZoomIn()
	{
		if (IsLocked) return;
		_targetHeight = Mathf.Clamp(_targetHeight - ZoomStep, MinZoom, MaxZoom);
	}

	public void ZoomOut()
	{
		if (IsLocked) return;
		_targetHeight = Mathf.Clamp(_targetHeight + ZoomStep, MinZoom, MaxZoom);
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
					bool shiftPressed = Input.IsKeyPressed(Key.Shift);
					bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
					if ((shiftPressed || ctrlPressed) && GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
					{
						return;
					}
				}

				if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
				{
					_targetHeight = Mathf.Clamp(_targetHeight - ZoomStep, MinZoom, MaxZoom);
				}
				else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
				{
					_targetHeight = Mathf.Clamp(_targetHeight + ZoomStep, MinZoom, MaxZoom);
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
			newPos.X = Mathf.Clamp(newPos.X, -MapLimit, MapLimit);
			newPos.Z = Mathf.Clamp(newPos.Z, -MapLimit, MapLimit + 30f);

			Position = newPos;
		}
	}

	public override void _Process(double delta)
	{
		float fDelta = (float)delta;

		MoveSpeed = 10.0f + (GameSettings.ScrollSpeed / 100.0f) * 50.0f;

		_currentHeight = Mathf.Lerp(_currentHeight, _targetHeight, ZoomSpeed * fDelta);
		Position = new Vector3(Position.X, _currentHeight, Position.Z);

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

		_currentYaw = Mathf.LerpAngle(Mathf.DegToRad(_currentYaw), Mathf.DegToRad(_targetYaw), 10.0f * fDelta);
		_currentYaw = Mathf.RadToDeg(_currentYaw);
		RotationDegrees = new Vector3(RotationDegrees.X, _currentYaw, 0.0f);

		if (IsLocked || (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)) return;

		Vector3 forwardXZ = -GlobalTransform.Basis.Z;
		forwardXZ.Y = 0f;
		forwardXZ = forwardXZ.Normalized();

		Vector3 rightXZ = GlobalTransform.Basis.X;
		rightXZ.Y = 0f;
		rightXZ = rightXZ.Normalized();

		Vector3 velocity = Vector3.Zero;

		if (Input.IsActionPressed("move_forward") || Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			velocity += forwardXZ;
		if (Input.IsActionPressed("move_back") || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			velocity -= forwardXZ;
		if (Input.IsActionPressed("move_left") || Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			velocity -= rightXZ;
		if (Input.IsActionPressed("move_right") || Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			velocity += rightXZ;

		if (EnableEdgePanning && Input.MouseMode == Input.MouseModeEnum.Visible)
		{
			Vector2 mousePos = GetViewport().GetMousePosition();
			Vector2 windowSize = GetViewport().GetVisibleRect().Size;

			if (mousePos.X >= 0 && mousePos.X < windowSize.X && mousePos.Y >= 0 && mousePos.Y < windowSize.Y)
			{
				if (mousePos.X < EdgePanMargin)
					velocity -= rightXZ;
				else if (mousePos.X > windowSize.X - EdgePanMargin)
					velocity += rightXZ;

				if (mousePos.Y < EdgePanMargin)
					velocity += forwardXZ;
				else if (mousePos.Y > windowSize.Y - EdgePanMargin)
					velocity -= forwardXZ;
			}
		}

		if (velocity != Vector3.Zero)
		{
			velocity = velocity.Normalized() * MoveSpeed * fDelta;

			float zoomFactor = _currentHeight / MaxZoom;
			velocity *= Mathf.Lerp(0.5f, 1.5f, zoomFactor);

			Vector3 newPos = Position + velocity;
			newPos.X = Mathf.Clamp(newPos.X, -MapLimit, MapLimit);
			newPos.Z = Mathf.Clamp(newPos.Z, -MapLimit, MapLimit + 30f);

			Position = newPos;
		}
	}
}
