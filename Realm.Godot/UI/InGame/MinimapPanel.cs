using Godot;
using System;
using DotRecast.Detour;
using DotRecast.Core.Numerics;
using Realm.Godot.ReplaySystem;

public class MinimapPanel
{
	private Control _minimapArea;
	private Control _cameraIndicator;
	private Camera3D _camera3D;
	private PanelContainer _minimapFrame;
	private bool _isRightClickPanning = false;
	private bool _isLeftClickDragging = false;

	public MinimapPanel(PanelContainer minimapFrame, Control minimapArea, Control cameraIndicator, Camera3D camera3D)
	{
		_minimapFrame = minimapFrame;
		_minimapArea = minimapArea;
		_cameraIndicator = cameraIndicator;
		_camera3D = camera3D;

		SetupMinimap();
	}

	public void SetCamera(Camera3D camera)
	{
		_camera3D = camera;
	}

	private void SetupMinimap()
	{
		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		_minimapArea.GuiInput += (@event) =>
		{
			if (ReplayPlaybackManager.Instance.IsPlayingReplay || isSpectator)
			{
				if (@event is InputEventMouseButton rMouseBtn)
				{
					if (rMouseBtn.ButtonIndex == MouseButton.Left)
					{
						if (rMouseBtn.Pressed)
						{
							TeleportCameraToMinimapPos(rMouseBtn.Position);
							_isLeftClickDragging = true;
						}
						else
						{
							_isLeftClickDragging = false;
						}
					}
					else if (rMouseBtn.ButtonIndex == MouseButton.Right)
					{
						if (rMouseBtn.Pressed)
						{
							TeleportCameraToMinimapPos(rMouseBtn.Position);
							_isRightClickPanning = true;
						}
						else
						{
							_isRightClickPanning = false;
						}
					}
				}
				else if (@event is InputEventMouseMotion rMouseMotion)
				{
					if (_isLeftClickDragging || _isRightClickPanning)
					{
						TeleportCameraToMinimapPos(rMouseMotion.Position);
					}
				}
				return;
			}

			if (@event is InputEventMouseButton mouseBtn)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (mouseBtn.Pressed)
					{
						float xRatio = mouseBtn.Position.X / _minimapArea.Size.X;
						float yRatio = mouseBtn.Position.Y / _minimapArea.Size.Y;
						float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
						float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);
						float height = 0f;
						if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
						{
							GameHost.Instance.GroundTerrain.GetHeightAndNormal(worldX, worldZ, out height, out _);
						}
						var minimapWorldPos = new Vector3(worldX, height, worldZ);

						if (mouseBtn.AltPressed)
						{
							if (GameHost.Instance != null)
							{
								if (GameHost.Instance.Multiplayer.MultiplayerPeer != null)
								{
									GameHost.Instance.Rpc("NetworkPingMinimap", minimapWorldPos);
								}
								else
								{
									GameHost.Instance.AddMinimapPing(minimapWorldPos);
								}
							}
							return;
						}

						if (GameHost.Instance != null)
						{
							if (GameHost.Instance.ActivePingMode)
							{
								GameHost.Instance.AddMinimapPing(minimapWorldPos);
								GameHost.Instance.ActivePingMode = false;
							}
							else if (GameHost.Instance.ActiveCommandTargeting != null)
							{
								string cmd = GameHost.Instance.ActiveCommandTargeting;
								if (cmd == "attack")
								{
									GameHost.Instance.IssueAttackMoveCommand(minimapWorldPos);
								}
								else if (cmd == "move")
								{
									if (Input.IsKeyPressed(Key.Shift))
										GameHost.Instance.IssueMoveCommand(minimapWorldPos, true);
									else
										GameHost.Instance.IssueMoveCommand(minimapWorldPos);
								}
								else if (cmd == "patrol")
								{
									GameHost.Instance.IssuePatrolCommand(minimapWorldPos);
								}
								else if (cmd == "rally")
								{
									if (GameHost.Instance.SelectedUnits.Count == 1 && 
										!GameHost.Instance.SelectedUnits[0].IsEnemy && 
										GameHost.Instance.SelectedUnits[0].IsBuilding)
									{
										GameHost.Instance.SetRallyPoint(GameHost.Instance.SelectedUnits[0], minimapWorldPos);
									}
								}
								GameHost.Instance.ClearTargetingModes();
							}
							else if (GameHost.Instance.ActiveSpellTargeting != null)
							{
								GameHost.Instance.CastSpellAt(GameHost.Instance.ActiveSpellTargeting, minimapWorldPos);
								GameHost.Instance.ClearTargetingModes();
							}
							else if (GameHost.Instance.ActiveBuildingPlacementType != null)
							{
								GameHost.Instance.PlaceBuildingAt(GameHost.Instance.ActiveBuildingPlacementType, minimapWorldPos);
								GameHost.Instance.ClearTargetingModes();
							}
							else
							{
								TeleportCameraToMinimapPos(mouseBtn.Position);
								_isLeftClickDragging = true;
							}
						}
					}
					else
					{
						_isLeftClickDragging = false;
					}
				}
				else if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					if (mouseBtn.Pressed)
					{
						TeleportCameraToMinimapPos(mouseBtn.Position);
						_isRightClickPanning = true;
					}
					else
					{
						_isRightClickPanning = false;
					}
				}
			}
			else if (@event is InputEventMouseMotion mouseMotion)
			{
				if (_isLeftClickDragging || _isRightClickPanning)
				{
					TeleportCameraToMinimapPos(mouseMotion.Position);
				}
			}
		};
	}

	public void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		float xRatio = clickPos.X / _minimapArea.Size.X;
		float yRatio = clickPos.Y / _minimapArea.Size.Y;

		float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
		float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);

		if (_camera3D != null && GodotObject.IsInstanceValid(_camera3D))
		{
			_camera3D.GlobalPosition = new Vector3(worldX, _camera3D.GlobalPosition.Y, worldZ);
			InGameHUD.Instance?.ShowFeedbackText(string.Format(TranslationServer.Translate("Panned Camera on Minimap to: {0:F0}, {1:F0}"), worldX, worldZ), new Color(1, 0.85f, 0.5f));
		}
	}

	public void UpdateMinimapIndicator()
	{
		if (_camera3D == null || !GodotObject.IsInstanceValid(_camera3D) || _cameraIndicator == null || _minimapArea == null) return;

		float scale = _camera3D.GlobalPosition.Y / 35.0f;
		Vector2 newSize = new Vector2(45.0f * scale, 30.0f * scale);
		_cameraIndicator.CustomMinimumSize = newSize;
		_cameraIndicator.Size = newSize;

		float worldX = _camera3D.GlobalPosition.X;
		float worldZ = _camera3D.GlobalPosition.Z;

		float xRatio = (worldX / 250f) + 0.5f;
		float yRatio = (worldZ / 250f) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		float xPos = xRatio * _minimapArea.Size.X - (newSize.X / 2f);
		float yPos = yRatio * _minimapArea.Size.Y - (newSize.Y / 2f);

		_cameraIndicator.Position = new Vector2(xPos, yPos);
	}
}
