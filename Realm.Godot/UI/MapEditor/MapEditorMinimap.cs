using Godot;
using System;

public class MapEditorMinimap
{
	private PanelContainer _minimapFrame;
	private Control _minimapArea;
	private ReferenceRect _cameraIndicator;
	private Node _hudNode;

	public MapEditorMinimap(PanelContainer minimapFrame, Control minimapArea, ReferenceRect cameraIndicator, Node hudNode)
	{
		_minimapFrame = minimapFrame;
		_minimapArea = minimapArea;
		_cameraIndicator = cameraIndicator;
		_hudNode = hudNode;

		_minimapArea.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				TeleportCameraToMinimapPos(mouseBtn.Position);
			}
			else if (@event is InputEventMouseMotion mouseMotion && mouseMotion.ButtonMask == MouseButtonMask.Left)
			{
				TeleportCameraToMinimapPos(mouseMotion.Position);
			}
		};
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		UpdateMinimapIndicator();
	}

	private void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		if (_minimapArea == null || GameHost.Instance == null) return;
		float xRatio = clickPos.X / _minimapArea.Size.X;
		float yRatio = clickPos.Y / _minimapArea.Size.Y;

		float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
		float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);

		var camera = GameHost.Instance.GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(worldX, camera.GlobalPosition.Y, worldZ);
		}
	}

	private void UpdateMinimapIndicator()
	{
		if (_cameraIndicator == null || _minimapArea == null || GameHost.Instance == null) return;
		var camera = GameHost.Instance.GetViewport().GetCamera3D();
		if (camera == null) return;

		float worldX = camera.GlobalPosition.X;
		float worldZ = camera.GlobalPosition.Z;

		float xRatio = (worldX / 250f) + 0.5f;
		float yRatio = (worldZ / 250f) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		float xPos = xRatio * _minimapArea.Size.X - (_cameraIndicator.Size.X / 2f);
		float yPos = yRatio * _minimapArea.Size.Y - (_cameraIndicator.Size.Y / 2f);

		_cameraIndicator.Position = new Vector2(xPos, yPos);
	}

	public void RegenerateMinimap()
	{
		GenerateDynamicMinimap();
	}

	private async void GenerateDynamicMinimap()
	{
		if (_hudNode == null || !GodotObject.IsInstanceValid(_hudNode)) return;
		var tree = _hudNode.GetTree();
		if (tree == null) return;

		await _hudNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		await _hudNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

		if (_minimapArea == null) return;
		var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
		if (minimapBg == null) return;

		try
		{
			var viewport = new SubViewport();
			viewport.Size = new Vector2I(256, 256);
			viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			_hudNode.AddChild(viewport);

			var camera = new Camera3D();
			camera.Projection = Camera3D.ProjectionType.Orthogonal;
			camera.Size = 250f;
			camera.Far = 200f;
			camera.Position = new Vector3(0, 100, 0);
			camera.RotationDegrees = new Vector3(-90, 0, 0);
			viewport.AddChild(camera);

			await _hudNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

			var texture = viewport.GetTexture();
			if (texture != null)
			{
				var img = texture.GetImage();
				if (img != null)
				{
					var imgTexture = ImageTexture.CreateFromImage(img);
					minimapBg.Texture = imgTexture;
				}
			}

			viewport.QueueFree();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
	}
}
