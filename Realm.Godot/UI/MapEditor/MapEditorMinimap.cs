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

		float spacing = GameHost.Instance.GroundTerrain?.Spacing ?? 2.0f;
		float physicalWidth = (GameHost.Instance.GroundTerrain?.Width - 1 ?? 125) * spacing;
		float physicalDepth = (GameHost.Instance.GroundTerrain?.Depth - 1 ?? 125) * spacing;

		float halfWidth = physicalWidth / 2.0f;
		float halfDepth = physicalDepth / 2.0f;

		float margin = Mathf.Min(30.0f, halfWidth * 0.8f);
		float clampX = halfWidth - margin;
		float clampZMin = -halfDepth + margin;
		float clampZMax = halfDepth;

		float worldX = Mathf.Clamp((xRatio - 0.5f) * physicalWidth, -clampX, clampX);
		float worldZ = Mathf.Clamp((yRatio - 0.5f) * physicalDepth, -clampZMin, clampZMax);

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

		float spacing = GameHost.Instance.GroundTerrain?.Spacing ?? 2.0f;
		float physicalWidth = (GameHost.Instance.GroundTerrain?.Width - 1 ?? 125) * spacing;
		float physicalDepth = (GameHost.Instance.GroundTerrain?.Depth - 1 ?? 125) * spacing;

		float scale = camera.GlobalPosition.Y / 35.0f;
		float targetIndicatorWidth = 25.0f * scale * (250f / physicalWidth);
		float targetIndicatorHeight = 18.0f * scale * (250f / physicalDepth);
		targetIndicatorWidth = Mathf.Clamp(targetIndicatorWidth, 5.0f, _minimapArea.Size.X);
		targetIndicatorHeight = Mathf.Clamp(targetIndicatorHeight, 5.0f, _minimapArea.Size.Y);
		Vector2 newSize = new Vector2(targetIndicatorWidth, targetIndicatorHeight);
		_cameraIndicator.CustomMinimumSize = newSize;
		_cameraIndicator.Size = newSize;

		float worldX = camera.GlobalPosition.X;
		float worldZ = camera.GlobalPosition.Z;

		float xRatio = (worldX / physicalWidth) + 0.5f;
		float yRatio = (worldZ / physicalDepth) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		float xPos = xRatio * _minimapArea.Size.X - (newSize.X / 2f);
		float yPos = yRatio * _minimapArea.Size.Y - (newSize.Y / 2f);

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
			float spacing = GameHost.Instance?.GroundTerrain?.Spacing ?? 2.0f;
			float physicalWidth = (GameHost.Instance?.GroundTerrain?.Width - 1 ?? 125) * spacing;
			float physicalDepth = (GameHost.Instance?.GroundTerrain?.Depth - 1 ?? 125) * spacing;

			int viewportWidth = 256;
			int viewportHeight = 256;

			if (physicalWidth >= physicalDepth && physicalWidth > 0.0f)
			{
				viewportHeight = Mathf.Max(16, Mathf.RoundToInt(256f * physicalDepth / physicalWidth));
			}
			else if (physicalDepth > physicalWidth && physicalDepth > 0.0f)
			{
				viewportWidth = Mathf.Max(16, Mathf.RoundToInt(256f * physicalWidth / physicalDepth));
			}

			var viewport = new SubViewport();
			viewport.Size = new Vector2I(viewportWidth, viewportHeight);
			viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			_hudNode.AddChild(viewport);

			var camera = new Camera3D();
			camera.Projection = Camera3D.ProjectionType.Orthogonal;
			camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
			camera.Size = physicalDepth;
			camera.Far = 200f;
			camera.Position = new Vector3(0, 100, 0);
			camera.RotationDegrees = new Vector3(-90, 0, 0);
			viewport.AddChild(camera);

			bool wasVisible = false;
			if (GameHost.Instance?.BrushIndicatorMesh != null)
			{
				wasVisible = GameHost.Instance.BrushIndicatorMesh.Visible;
				GameHost.Instance.BrushIndicatorMesh.Visible = false;
			}

			bool wasGridVisible = false;
			if (GameHost.Instance?.GridOverlayMesh != null)
			{
				wasGridVisible = GameHost.Instance.GridOverlayMesh.Visible;
				GameHost.Instance.GridOverlayMesh.Visible = false;
			}

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

			if (GameHost.Instance?.BrushIndicatorMesh != null)
			{
				GameHost.Instance.BrushIndicatorMesh.Visible = wasVisible;
			}

			if (GameHost.Instance?.GridOverlayMesh != null)
			{
				GameHost.Instance.GridOverlayMesh.Visible = wasGridVisible;
			}

			viewport.QueueFree();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
	}

}
