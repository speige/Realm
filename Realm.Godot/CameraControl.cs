using Godot;

public partial class CameraControl : Camera3D
{
	// --- Configuration ---
	[Export] public float LookSensitivity = 0.2f; // Mouse rotation speed
	[Export] public float MoveSpeed = 10.0f;     // WASD movement speed
	[Export] public float SprintSpeed = 20.0f;    // Speed when holding Shift

	private Vector2 _rotationInput = Vector2.Zero;
	private float _currentSpeed;

	// --- Godot Lifecycle Methods ---
	
	public override void _Ready()
	{
		// Capture the mouse and hide it for camera look functionality
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		// Handle mouse movement for rotation
		if (@event is InputEventMouseMotion mouseMotion)
		{
			// Only process input if the mouse is captured
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				// Store mouse movement delta for rotation calculations in _Process
				_rotationInput = mouseMotion.Relative;
			}
		}
		
		// Handle un-capturing the mouse (e.g., to debug or interact with UI)
		if (@event.IsActionPressed("ui_cancel"))
		{
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}
			else
			{
				Input.MouseMode = Input.MouseModeEnum.Captured;
			}
		}
	}

	public override void _Process(double delta)
	{
		// --- 1. Rotation (Mouse Look) ---
		ApplyRotation((float)delta);
		
		// --- 2. Movement (WASD) ---
		ApplyMovement((float)delta);
	}
	
	// --- Custom Methods ---

	private void ApplyRotation(float delta)
	{
		if (_rotationInput == Vector2.Zero) return;

		// Apply horizontal (Y-axis) rotation to the camera's parent (or the camera itself if it's the root)
		// Since the camera is a Node3D, rotating it directly on the Y-axis works.
		// Multiply by LookSensitivity and delta for framerate independence and desired speed.
		RotateY(Mathf.DegToRad(-_rotationInput.X * LookSensitivity));

		// Apply vertical (X-axis) rotation (pitch)
		// We use RotateObjectLocal to rotate around the camera's local X-axis.
		// We also clamp the rotation to prevent the camera from flipping over (gimbal lock).
		
		// Get the current local rotation on the X-axis (pitch)
		float currentXRotation = Rotation.X;

		// Calculate the rotation amount
		float rotationAmount = Mathf.DegToRad(-_rotationInput.Y * LookSensitivity);

		// Check if the new rotation will be within the limits (e.g., -90 to +90 degrees)
		float newXRotation = currentXRotation + rotationAmount;
		float maxAngle = Mathf.DegToRad(90.0f); 

		// Clamp the new rotation and apply
		if (newXRotation > maxAngle)
		{
			Rotation = new Vector3(maxAngle, Rotation.Y, Rotation.Z);
		}
		else if (newXRotation < -maxAngle)
		{
			Rotation = new Vector3(-maxAngle, Rotation.Y, Rotation.Z);
		}
		else
		{
			RotateObjectLocal(Vector3.Right, rotationAmount);
		}

		// Reset the rotation input for the next frame
		_rotationInput = Vector2.Zero;
	}

	private void ApplyMovement(float delta)
{
	// Determine the movement speed
	_currentSpeed = Input.IsActionPressed("sprint") ? SprintSpeed : MoveSpeed;
	
	// --- FIX: Get Vector2 for horizontal movement first ---
	Vector2 horizontalInput = Input.GetVector(
		"move_left", "move_right",   // X-axis (Strafe)
		"move_forward", "move_back"  // Y-axis in Vector2, which corresponds to Z-axis in 3D world
	);
	
	// Initialize the final 3D direction vector
	Vector3 inputDir = new Vector3(
		horizontalInput.X, 
		0.0f, 
		horizontalInput.Y  // Vector2's Y is our 3D world's Z
	);
	
	// Add vertical movement (Y-axis) for elevation
	if (Input.IsActionPressed("move_up"))
	{
		inputDir.Y += 1.0f;
	}
	if (Input.IsActionPressed("move_down"))
	{
		inputDir.Y -= 1.0f;
	}

	// Normalize the vector to prevent faster diagonal movement
	Vector3 direction = inputDir.Normalized();

	// Calculate the velocity vector
	Vector3 velocity = direction * _currentSpeed * delta;

	// Apply movement using the camera's local space (Transform)
	// Note: The Y-axis (up/down) is treated as global movement.
	Translate(new Vector3(velocity.X, 0, velocity.Z));
	
	// Apply vertical movement directly (no rotation applied)
	GlobalPosition = new Vector3(GlobalPosition.X, GlobalPosition.Y + velocity.Y, GlobalPosition.Z);
}
}
