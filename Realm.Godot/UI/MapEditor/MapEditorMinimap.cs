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

		float quadSize = GameHost.Instance.GroundTerrain?.QuadSize ?? 2.0f;
		float physicalWidth = (GameHost.Instance.GroundTerrain?.Width - 1 ?? 125) * quadSize;
		float physicalDepth = (GameHost.Instance.GroundTerrain?.Depth - 1 ?? 125) * quadSize;

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

	private Vector3 _lastCameraPos;
	private Vector3 _lastCameraRot;
	private readonly Vector2[] _cachedMinimapPoints = new Vector2[4];

	private void UpdateMinimapIndicator()
	{
		if (_cameraIndicator == null || _minimapArea == null || GameHost.Instance == null) return;
		var camera = GameHost.Instance.GetViewport()?.GetCamera3D();
		if (camera == null) return;

		Vector3 camPos = camera.GlobalPosition;
		Vector3 camRot = camera.GlobalRotation;
		if ((camPos - _lastCameraPos).LengthSquared() < 0.0001f && (camRot - _lastCameraRot).LengthSquared() < 0.0001f)
		{
			return;
		}
		_lastCameraPos = camPos;
		_lastCameraRot = camRot;

		float quadSize = GameHost.Instance.GroundTerrain?.QuadSize ?? 2.0f;
		float physicalWidth = (GameHost.Instance.GroundTerrain?.Width - 1 ?? 125) * quadSize;
		float physicalDepth = (GameHost.Instance.GroundTerrain?.Depth - 1 ?? 125) * quadSize;

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

		_cachedMinimapPoints[0] = WorldToMinimap(pTL, physicalWidth, physicalDepth);
		_cachedMinimapPoints[1] = WorldToMinimap(pTR, physicalWidth, physicalDepth);
		_cachedMinimapPoints[2] = WorldToMinimap(pBR, physicalWidth, physicalDepth);
		_cachedMinimapPoints[3] = WorldToMinimap(pBL, physicalWidth, physicalDepth);

		_cameraIndicator.SetPoints(_cachedMinimapPoints);
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
			float quadSize = GameHost.Instance?.GroundTerrain?.QuadSize ?? 2.0f;
			float physicalWidth = (GameHost.Instance?.GroundTerrain?.Width - 1 ?? 125) * quadSize;
			float physicalDepth = (GameHost.Instance?.GroundTerrain?.Depth - 1 ?? 125) * quadSize;

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
			viewport.DebugDraw = Viewport.DebugDrawEnum.Unshaded;
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

			var wasGridMode = GameHost.GridOverlayMode.Off;
			bool wasPathingVisible = false;
			if (GameHost.Instance != null)
			{
				wasGridMode = GameHost.Instance.EditorGridMode;
				wasPathingVisible = GameHost.Instance.PathingOverlayVisible;
				GameHost.Instance.EditorGridMode = GameHost.GridOverlayMode.Off;
				GameHost.Instance.PathingOverlayVisible = false;
				GameHost.Instance.UpdateGridOverlayVisibility();
				GameHost.Instance.UpdatePathingOverlay();
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

			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorGridMode = wasGridMode;
				GameHost.Instance.PathingOverlayVisible = wasPathingVisible;
				GameHost.Instance.UpdateGridOverlayVisibility();
				GameHost.Instance.UpdatePathingOverlay();
			}

			viewport.QueueFree();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
	}

}
