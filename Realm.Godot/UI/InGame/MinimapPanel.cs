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
				if (@event is InputEventMouseButton rMouseBtn && rMouseBtn.Pressed && rMouseBtn.ButtonIndex == MouseButton.Left)
				{
					TeleportCameraToMinimapPos(rMouseBtn.Position);
				}
				else if (@event is InputEventMouseMotion rMouseMotion && rMouseMotion.ButtonMask == MouseButtonMask.Left)
				{
					TeleportCameraToMinimapPos(rMouseMotion.Position);
				}
				return;
			}

			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (GameHost.Instance != null)
					{
						float xRatio = mouseBtn.Position.X / _minimapArea.Size.X;
						float yRatio = mouseBtn.Position.Y / _minimapArea.Size.Y;
						float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
						float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);
						float height = 0f;
						if (GameHost.Instance.GroundTerrain != null)
						{
							GameHost.Instance.GroundTerrain.GetHeightAndNormal(worldX, worldZ, out height, out _);
						}
						var minimapWorldPos = new Vector3(worldX, height, worldZ);

						if (GameHost.Instance.GroundTerrain != null && GameHost.Instance.GroundTerrain.NavMeshQuery != null)
						{
							Unit3D firstMovable = null;
							foreach (var u in GameHost.Instance.SelectedUnits)
							{
								if (u != null && GodotObject.IsInstanceValid(u) && !u.IsEnemy && !u.IsBuilding)
								{
									firstMovable = u;
									break;
								}
							}
							if (firstMovable != null)
							{
								int includeFlags = 8;
								if (GameHost.UnitRegistry.TryGetValue(firstMovable.UnitId, out var meta))
								{
									includeFlags = GameHost.GetUnitPathingFlags(meta);
								}
								var filter = new DtQueryDefaultFilter();
								filter.SetIncludeFlags(includeFlags);
								filter.SetExcludeFlags(0);

								var extents = new RcVec3f(2f, 4f, 2f);
								var targetRc = new RcVec3f(worldX, height, worldZ);
								GameHost.Instance.GroundTerrain.NavMeshQuery.FindNearestPoly(targetRc, extents, filter, out long nearestRef, out var nearestPt, out _);
								if (nearestRef != 0)
								{
									minimapWorldPos = new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
								}
							}
						}

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
									GameHost.Instance.IssueMoveCommandQueued(minimapWorldPos);
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
						}
					}
				}
				else if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					if (GameHost.Instance != null && GameHost.Instance.SelectedUnits.Count > 0)
					{
						float xRatio = mouseBtn.Position.X / _minimapArea.Size.X;
						float yRatio = mouseBtn.Position.Y / _minimapArea.Size.Y;
						float worldX = Mathf.Clamp((xRatio - 0.5f) * 250f, -95f, 95f);
						float worldZ = Mathf.Clamp((yRatio - 0.5f) * 250f, -95f, 125f);
						float height = 0f;
						if (GameHost.Instance.GroundTerrain != null)
						{
							GameHost.Instance.GroundTerrain.GetHeightAndNormal(worldX, worldZ, out height, out _);
						}
						var hitPos = new Vector3(worldX, height, worldZ);

						if (GameHost.Instance.SelectedUnits.Count == 1 && 
							!GameHost.Instance.SelectedUnits[0].IsEnemy && 
							GameHost.Instance.SelectedUnits[0].IsBuilding)
						{
							GameHost.Instance.SetRallyPoint(GameHost.Instance.SelectedUnits[0], hitPos);
						}
						else
						{
							if (GameHost.Instance.GroundTerrain != null && GameHost.Instance.GroundTerrain.NavMeshQuery != null)
							{
								Unit3D firstMovable = null;
								foreach (var u in GameHost.Instance.SelectedUnits)
								{
									if (u != null && GodotObject.IsInstanceValid(u) && !u.IsEnemy && !u.IsBuilding)
									{
										firstMovable = u;
										break;
									}
								}
								if (firstMovable != null)
								{
									int includeFlags = 8;
									if (GameHost.UnitRegistry.TryGetValue(firstMovable.UnitId, out var meta))
									{
										includeFlags = GameHost.GetUnitPathingFlags(meta);
									}
									var filter = new DtQueryDefaultFilter();
									filter.SetIncludeFlags(includeFlags);
									filter.SetExcludeFlags(0);

									var extents = new RcVec3f(2f, 4f, 2f);
									var targetRc = new RcVec3f(worldX, height, worldZ);
									GameHost.Instance.GroundTerrain.NavMeshQuery.FindNearestPoly(targetRc, extents, filter, out long nearestRef, out var nearestPt, out _);
									if (nearestRef != 0)
									{
										hitPos = new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
									}
								}
							}

							bool shiftHeld = Input.IsKeyPressed(Key.Shift);
							if (shiftHeld)
							{
								GameHost.Instance.IssueMoveCommandQueued(hitPos);
							}
							else
							{
								GameHost.Instance.IssueMoveCommand(hitPos);
							}
						}
					}
				}
			}
			else if (@event is InputEventMouseMotion mouseMotion && mouseMotion.ButtonMask == MouseButtonMask.Left)
			{
				if (GameHost.Instance == null || (!GameHost.Instance.ActivePingMode && GameHost.Instance.ActiveCommandTargeting == null && GameHost.Instance.ActiveSpellTargeting == null && GameHost.Instance.ActiveBuildingPlacementType == null))
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

		float worldX = _camera3D.GlobalPosition.X;
		float worldZ = _camera3D.GlobalPosition.Z;

		float xRatio = (worldX / 250f) + 0.5f;
		float yRatio = (worldZ / 250f) + 0.5f;

		xRatio = Mathf.Clamp(xRatio, 0f, 1f);
		yRatio = Mathf.Clamp(yRatio, 0f, 1f);

		float xPos = xRatio * _minimapArea.Size.X - (_cameraIndicator.Size.X / 2f);
		float yPos = yRatio * _minimapArea.Size.Y - (_cameraIndicator.Size.Y / 2f);

		_cameraIndicator.Position = new Vector2(xPos, yPos);
	}
}
