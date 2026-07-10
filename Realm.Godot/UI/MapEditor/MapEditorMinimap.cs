using Godot;
using System;

public class MapEditorMinimap
{
	private PanelContainer _minimapFrame;
	private Control _minimapArea;
	private MapEditorCameraIndicator _cameraIndicator;
	private Node _hudNode;
	private bool _isDragging;

	public bool IsDragging => _isDragging && Input.IsMouseButtonPressed(MouseButton.Left);

	public MapEditorMinimap(PanelContainer minimapFrame, Control minimapArea, MapEditorCameraIndicator cameraIndicator, Node hudNode)
	{
		_minimapFrame = minimapFrame;
		_minimapArea = minimapArea;
		_cameraIndicator = cameraIndicator;
		_hudNode = hudNode;

		_minimapArea.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				_isDragging = mouseBtn.Pressed;
				if (mouseBtn.Pressed)
				{
					TeleportCameraToMinimapPos(mouseBtn.Position);
				}
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

		float worldX = (xRatio - 0.5f) * physicalWidth;
		float worldZ = (yRatio - 0.5f) * physicalDepth;

		float minX = GameHost.Instance.EditorCameraBoundsLeft;
		float maxX = GameHost.Instance.EditorCameraBoundsRight;
		float minZ = GameHost.Instance.EditorCameraBoundsTop;
		float maxZ = GameHost.Instance.EditorCameraBoundsBottom;

		worldX = Mathf.Clamp(worldX, minX, maxX);
		worldZ = Mathf.Clamp(worldZ, minZ, maxZ);

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

		var viewport = camera.GetViewport();
		if (viewport == null) return;
		Vector2 viewportSize = viewport.GetVisibleRect().Size;

		Vector2 topLeftScreen = new Vector2(0, 0);
		Vector2 topRightScreen = new Vector2(viewportSize.X, 0);
		Vector2 bottomRightScreen = new Vector2(viewportSize.X, viewportSize.Y);
		Vector2 bottomLeftScreen = new Vector2(0, viewportSize.Y);

		Vector3 pTL = ProjectToGround(camera, topLeftScreen);
		Vector3 pTR = ProjectToGround(camera, topRightScreen);
		Vector3 pBR = ProjectToGround(camera, bottomRightScreen);
		Vector3 pBL = ProjectToGround(camera, bottomLeftScreen);

		Vector2[] minimapPoints = new Vector2[4];
		minimapPoints[0] = WorldToMinimap(pTL, physicalWidth, physicalDepth);
		minimapPoints[1] = WorldToMinimap(pTR, physicalWidth, physicalDepth);
		minimapPoints[2] = WorldToMinimap(pBR, physicalWidth, physicalDepth);
		minimapPoints[3] = WorldToMinimap(pBL, physicalWidth, physicalDepth);

		_cameraIndicator.SetPoints(minimapPoints);
	}

	private Vector3 ProjectToGround(Camera3D camera, Vector2 screenPos)
	{
		Vector3 rayOrigin = camera.ProjectRayOrigin(screenPos);
		Vector3 rayNormal = camera.ProjectRayNormal(screenPos);

		if (Mathf.IsZeroApprox(rayNormal.Y))
		{
			return rayOrigin + rayNormal * 1000f;
		}

		float t = -rayOrigin.Y / rayNormal.Y;
		if (t < 0f || t > 1000f)
		{
			t = 1000f;
		}

		return rayOrigin + t * rayNormal;
	}

	private Vector2 WorldToMinimap(Vector3 worldPos, float physicalWidth, float physicalDepth)
	{
		float xRatio = (worldPos.X / physicalWidth) + 0.5f;
		float yRatio = (worldPos.Z / physicalDepth) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		return new Vector2(xRatio * _minimapArea.Size.X, yRatio * _minimapArea.Size.Y);
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

			bool wasPathingVisible = false;
			if (GameHost.Instance?.PathingOverlayMesh != null)
			{
				wasPathingVisible = GameHost.Instance.PathingOverlayMesh.Visible;
				GameHost.Instance.PathingOverlayMesh.Visible = false;
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

			if (GameHost.Instance?.PathingOverlayMesh != null)
			{
				GameHost.Instance.PathingOverlayMesh.Visible = wasPathingVisible;
			}

			viewport.QueueFree();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
	}

}
