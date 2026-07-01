using Godot;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Common;
using Realm.MapAPI;
using Realm.Godot.ReplaySystem;
using System;
using System.Collections.Generic;

public partial class GameHost
{


	private int _cycleSelectionIndex = 0;
	private int _buildingCycleIndex = 0;
	private PhysicsRayQueryParameters3D? _cachedRaycastQuery;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)
		{
			return;
		}

		if (IsMapEditorMode)
		{
			if (@event is InputEventKey editorKeyEvent && editorKeyEvent.Pressed && !editorKeyEvent.Echo)
			{
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				bool shiftPressed = Input.IsKeyPressed(Key.Shift);
				
				if (editorKeyEvent.Keycode == Key.Escape)
				{
					if (_rampStartPos != null)
					{
						_rampStartPos = null;
						MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Cancelled");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (SelectedEditorObject != null)
					{
						SelectedEditorObject = null;
						MapEditorHUD.Instance?.ShowFeedbackExternal("Deselected Object");
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (ActiveEditorTool != EditorTool.SelectMove)
					{
						ActiveEditorTool = EditorTool.SelectMove;
						MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Space && !ctrlPressed && !shiftPressed)
				{
					CenterCameraOnSelectedOrCastle();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Z && !ctrlPressed && !shiftPressed)
				{
					CycleCameraZoom();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.H && ctrlPressed)
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.Visible = !MapEditorHUD.Instance.Visible;
						MapEditorHUD.Instance.ShowFeedbackExternal(MapEditorHUD.Instance.Visible ? "HUD: Visible" : "HUD: Hidden");
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.H && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.ToggleHelpPanelExternal();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.M && !ctrlPressed && !shiftPressed)
				{
					EditorBlockMode = !EditorBlockMode;
					MapEditorHUD.Instance?.UpdateBlockModeExternal(EditorBlockMode);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Q && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.I && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Eyedropper);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.N && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Noise);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Delete || editorKeyEvent.Keycode == Key.Backspace)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var target = SelectedEditorObject;
						SelectedEditorObject = null;
						var action = DeleteObjectAtWithUndo(target, (target as Node3D).Position);
						if (action != null)
						{
							EditorHistoryManager.RecordAction(action);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Deleted Object");
							EditorHasUnsavedChanges = true;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Z && ctrlPressed)
				{
					if (shiftPressed)
					{
						EditorHistoryManager.Redo();
						MapEditorHUD.Instance?.ShowFeedbackExternal("Redo Action performed");
					}
					else
					{
						EditorHistoryManager.Undo();
						MapEditorHUD.Instance?.ShowFeedbackExternal("Undo Action performed");
					}
					EditorHasUnsavedChanges = true;
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Y && ctrlPressed)
				{
					EditorHistoryManager.Redo();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Redo Action performed");
					EditorHasUnsavedChanges = true;
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.S && ctrlPressed)
				{
					MapEditorHUD.Instance?.SaveMapActionExternal();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.O && ctrlPressed)
				{
					MapEditorHUD.Instance?.LoadMapAction();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.P && ctrlPressed)
				{
					SaveMapToFile();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Map published & compiled!");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.F6 && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.ImportTerrainFromMinimapDialog();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.G && ctrlPressed)
				{
					EditorSnapToGrid = !EditorSnapToGrid;
					MapEditorHUD.Instance?.UpdateGridSnapExternal(EditorSnapToGrid);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.G && !ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						MapEditorHUD.Instance?.AlignSelectedObjectToGround();
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.F && !ctrlPressed && !shiftPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject) && SelectedEditorObject is Unit3D unit)
					{
						bool nextIsEnemy = !unit.IsEnemy;
						MapEditorHUD.Instance?.ToggleSelectedObjectTeam(nextIsEnemy);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.C && !ctrlPressed && !shiftPressed)
				{
					var cam = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");
					if (cam != null && cam.HasMethod("ToggleTopDown"))
					{
						cam.Call("ToggleTopDown");
						bool topDown = cam.Call("IsTopDown").AsBool();
						MapEditorHUD.Instance?.UpdateCameraAngleButtonText(topDown);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.C && ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectArea)
					{
						PerformCopyArea();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						if (SelectedEditorObject is Unit3D unit)
						{
							_copiedObject = new CopiedObjectTemplate {
								Type = "unit",
								Id = unit.UnitId,
								Rotation = unit.RotationDegrees.Y,
								Scale = unit.Scale.X,
								IsEnemy = unit.IsEnemy
							};
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
						}
						else if (SelectedEditorObject is Prop3D prop)
						{
							_copiedObject = new CopiedObjectTemplate {
								Type = "prop",
								Id = prop.PropId,
								Rotation = prop.RotationDegrees.Y,
								Scale = prop.Scale.X,
								IsEnemy = false
							};
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
						}
						else if (SelectedEditorObject is Decal decal)
						{
							string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
							_copiedObject = new CopiedObjectTemplate {
								Type = "decal",
								Id = decalId,
								Rotation = decal.RotationDegrees.Y,
								Scale = decal.Scale.X,
								IsEnemy = false
							};
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.V && ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
					{
						if (_copiedArea != null)
						{
							ActiveEditorTool = EditorTool.PasteArea;
							MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					if (_copiedObject != null)
					{
						var hit = RaycastFromMouse(GetViewport().GetMousePosition());
						if (hit != null && hit.ContainsKey("position"))
						{
							Vector3 spawnPos = hit["position"].AsVector3();
							if (EditorSnapToGrid && GroundTerrain != null)
							{
								float spacing = GroundTerrain.Spacing;
								int width = GroundTerrain.Width;
								int depth = GroundTerrain.Depth;
								float fx = Mathf.Round(spawnPos.X / spacing + (width - 1) / 2.0f);
								spawnPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
								float fz = Mathf.Round(spawnPos.Z / spacing + (depth - 1) / 2.0f);
								spawnPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
							}
							spawnPos.Y = GetTerrainHeightAt(spawnPos);
							var cop = _copiedObject.Value;
							Node pastedNode = null;
							IEditorAction action = null;
							if (cop.Type == "unit")
							{
								pastedNode = SpawnUnitExternal(cop.Id, spawnPos, cop.IsEnemy, cop.Rotation, cop.Scale);
								if (pastedNode != null)
								{
									action = new ObjectSpawnAction("unit", cop.Id, spawnPos, cop.Rotation, cop.Scale, cop.IsEnemy, pastedNode);
									MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Unit: {cop.Id.ToUpper()}");
								}
							}
							else if (cop.Type == "prop")
							{
								pastedNode = SpawnPropExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
								if (pastedNode != null)
								{
									action = new ObjectSpawnAction("prop", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
									MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Prop: {cop.Id.ToUpper()}");
								}
							}
							else if (cop.Type == "decal")
							{
								pastedNode = SpawnDecalExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
								if (pastedNode != null)
								{
									action = new ObjectSpawnAction("decal", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
									MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Decal: {cop.Id.ToUpper()}");
								}
							}
							if (action != null)
							{
								if (EditorMirrorMode != MirrorMode.None)
								{
									var actionsList = new List<IEditorAction> { action };
									foreach (var t in GetMirroredTransforms(spawnPos, cop.Rotation))
									{
										Vector3 mPos = t.Position;
										mPos.Y = GetTerrainHeightAt(mPos);
										Node mNode = null;
										if (cop.Type == "unit")
										{
											mNode = SpawnUnitExternal(cop.Id, mPos, cop.IsEnemy, t.Rotation, cop.Scale);
											if (mNode != null)
											{
												actionsList.Add(new ObjectSpawnAction("unit", cop.Id, mPos, t.Rotation, cop.Scale, cop.IsEnemy, mNode));
											}
										}
										else if (cop.Type == "prop")
										{
											mNode = SpawnPropExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
											if (mNode != null)
											{
												actionsList.Add(new ObjectSpawnAction("prop", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
											}
										}
										else if (cop.Type == "decal")
										{
											mNode = SpawnDecalExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
											if (mNode != null)
											{
												actionsList.Add(new ObjectSpawnAction("decal", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
											}
										}
									}
									var composite = new CompositeAction(actionsList);
									EditorHistoryManager.RecordAction(composite);
								}
								else
								{
									EditorHistoryManager.RecordAction(action);
								}
								SelectedEditorObject = pastedNode;
								EditorHasUnsavedChanges = true;
							}
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.D && ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						Node3D selectedNode = SelectedEditorObject as Node3D;
						Vector3 spawnPos = selectedNode.Position + new Vector3(2.0f, 0.0f, 2.0f);
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float rotY = selectedNode.RotationDegrees.Y;
						float scaleVal = selectedNode.Scale.X;
						Node clonedNode = null;
						IEditorAction action = null;
						if (SelectedEditorObject is Unit3D unit)
						{
							clonedNode = SpawnUnitExternal(unit.UnitId, spawnPos, unit.IsEnemy, rotY, scaleVal);
							if (clonedNode != null)
							{
								action = new ObjectSpawnAction("unit", unit.UnitId, spawnPos, rotY, scaleVal, unit.IsEnemy, clonedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Unit: {unit.UnitId.ToUpper()}");
							}
						}
						else if (SelectedEditorObject is Prop3D prop)
						{
							clonedNode = SpawnPropExternalWithParams(prop.PropId, spawnPos, rotY, scaleVal);
							if (clonedNode != null)
							{
								action = new ObjectSpawnAction("prop", prop.PropId, spawnPos, rotY, scaleVal, false, clonedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Prop: {prop.PropId.ToUpper()}");
							}
						}
						else if (SelectedEditorObject is Decal decal)
						{
							string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
							clonedNode = SpawnDecalExternalWithParams(decalId, spawnPos, rotY, scaleVal);
							if (clonedNode != null)
							{
								action = new ObjectSpawnAction("decal", decalId, spawnPos, rotY, scaleVal, false, clonedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Decal: {decalId.ToUpper()}");
							}
						}
						if (action != null)
						{
							EditorHistoryManager.RecordAction(action);
							SelectedEditorObject = clonedNode;
							EditorHasUnsavedChanges = true;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Bracketleft)
				{
					EditorBrushRadius = Mathf.Max(1.0f, EditorBrushRadius - 1.0f);
					MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Bracketright)
				{
					EditorBrushRadius = Mathf.Min(25.0f, EditorBrushRadius + 1.0f);
					MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}
				bool isNumpadNudge = editorKeyEvent.Keycode == Key.Kp1 ||
									 editorKeyEvent.Keycode == Key.Kp2 ||
									 editorKeyEvent.Keycode == Key.Kp3 ||
									 editorKeyEvent.Keycode == Key.Kp4 ||
									 editorKeyEvent.Keycode == Key.Kp6 ||
									 editorKeyEvent.Keycode == Key.Kp7 ||
									 editorKeyEvent.Keycode == Key.Kp8 ||
									 editorKeyEvent.Keycode == Key.Kp9;

				if (isNumpadNudge)
				{
					if (GodotObject.IsInstanceValid(SelectedEditorObject) && SelectedEditorObject is Node3D node3D)
					{
						Vector3 nudgeDir = Vector3.Zero;
						if (editorKeyEvent.Keycode == Key.Kp8) nudgeDir = new Vector3(0, 0, -1);
						else if (editorKeyEvent.Keycode == Key.Kp2) nudgeDir = new Vector3(0, 0, 1);
						else if (editorKeyEvent.Keycode == Key.Kp4) nudgeDir = new Vector3(-1, 0, 0);
						else if (editorKeyEvent.Keycode == Key.Kp6) nudgeDir = new Vector3(1, 0, 0);
						else if (editorKeyEvent.Keycode == Key.Kp7) nudgeDir = new Vector3(-1, 0, -1).Normalized();
						else if (editorKeyEvent.Keycode == Key.Kp9) nudgeDir = new Vector3(1, 0, -1).Normalized();
						else if (editorKeyEvent.Keycode == Key.Kp1) nudgeDir = new Vector3(-1, 0, 1).Normalized();
						else if (editorKeyEvent.Keycode == Key.Kp3) nudgeDir = new Vector3(1, 0, 1).Normalized();

						float nudgeDistance = 1.0f;
						Vector3 targetPos = node3D.Position + nudgeDir * nudgeDistance;
						
						bool valid = true;
						if (GroundTerrain != null)
						{
							float spacing = GroundTerrain.Spacing;
							int width = GroundTerrain.Width;
							int depth = GroundTerrain.Depth;
							float halfW = (width - 1) / 2.0f * spacing;
							float halfD = (depth - 1) / 2.0f * spacing;
							if (Mathf.Abs(targetPos.X) > halfW || Mathf.Abs(targetPos.Z) > halfD)
							{
								valid = false;
							}
						}

						if (valid)
						{
							float radius = 1.0f;
							if (node3D is Unit3D u) radius = GetPlacementRadius(u.UnitId, u.Scale.X);
							else if (node3D is Prop3D p) radius = GetPlacementRadius(p.PropId, p.Scale.X);

							if (IsPositionBlocked(targetPos, radius, node3D))
							{
								valid = false;
							}
						}

						if (valid)
						{
							targetPos.Y = GetTerrainHeightAt(targetPos);
							bool isUnit = node3D is Unit3D;
							bool isEnemy = isUnit ? (node3D as Unit3D).IsEnemy : false;
							var action = new ObjectTransformAction(
								node3D,
								node3D.Position, targetPos,
								node3D.RotationDegrees, node3D.RotationDegrees,
								node3D.Scale, node3D.Scale,
								isEnemy, isEnemy
							);
							node3D.Position = targetPos;
							if (node3D is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
							{
								EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z)));
							}
							EditorHistoryManager.RecordAction(action);
							EditorHasUnsavedChanges = true;
							MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
							MapEditorHUD.Instance?.ShowFeedbackExternal("Object nudged");
						}
						else
						{
							UIManager.Instance?.PlayWarningSound();
						}
					}
					GetViewport().SetInputAsHandled();
					return;
				}

				if (editorKeyEvent.Keycode == Key.Minus)
				{
					EditorBrushStrength = Mathf.Max(0.1f, EditorBrushStrength - 0.5f);
					MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Equal)
				{
					EditorBrushStrength = Mathf.Min(10.0f, EditorBrushStrength + 0.5f);
					MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.R)
				{
					if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal)
					{
						if (EditorRandomRotation || EditorRandomScale)
						{
							GenerateNewRandomPlacementRotationAndScale();
							MapEditorHUD.Instance?.ShowFeedbackExternal("Re-randomized Rotation & Scale");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					float angleStep = shiftPressed ? 15.0f : 45.0f;
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldRot = node3D.RotationDegrees;
						Vector3 newRot = oldRot;
						newRot.Y = (newRot.Y + angleStep) % 360.0f;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							oldRot, newRot,
							node3D.Scale, node3D.Scale,
							isEnemy, isEnemy
						);
						node3D.RotationDegrees = newRot;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Rotated Object to {newRot.Y}°");
					}
					else
					{
						EditorPlacementRotation = (EditorPlacementRotation + angleStep) % 360.0f;
						MapEditorHUD.Instance?.UpdateRotationExternal(EditorPlacementRotation);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.S)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldScale = node3D.Scale;
						float current = oldScale.X;
						float next = current;
						if (shiftPressed)
						{
							next = current + 0.1f;
							if (next > 3.0f) next = 0.2f;
						}
						else
						{
							next = current switch {
								0.5f => 1.0f,
								1.0f => 1.5f,
								1.5f => 2.0f,
								2.0f => 0.5f,
								_ => 1.0f
							};
						}
						Vector3 newScale = Vector3.One * next;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							node3D.RotationDegrees, node3D.RotationDegrees,
							oldScale, newScale,
							isEnemy, isEnemy
						);
						node3D.Scale = newScale;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Scaled Object to {next:F1}x");
					}
					else
					{
						float current = EditorPlacementScale;
						float next = current;
						if (shiftPressed)
						{
							next = current + 0.1f;
							if (next > 3.0f) next = 0.2f;
						}
						else
						{
							next = current switch {
								0.5f => 1.0f,
								1.0f => 1.5f,
								1.5f => 2.0f,
								2.0f => 0.5f,
								_ => 1.0f
							};
						}
						EditorPlacementScale = next;
						MapEditorHUD.Instance?.UpdateScaleExternal(next);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode >= Key.Key1 && editorKeyEvent.Keycode <= Key.Key9)
				{
					int toolIndex = (int)(editorKeyEvent.Keycode - Key.Key1);
					EditorTool targetTool = toolIndex switch
					{
						0 => EditorTool.Raise,
						1 => EditorTool.Lower,
						2 => EditorTool.Smooth,
						3 => EditorTool.Flatten,
						4 => EditorTool.Cliff,
						5 => EditorTool.PaintGrass,
						6 => EditorTool.PlaceDecal,
						7 => EditorTool.PlaceUnit,
						8 => EditorTool.Ramp,
						_ => EditorTool.None
					};
					if (targetTool != EditorTool.None)
					{
						MapEditorHUD.Instance?.SelectToolFromHotkey(targetTool);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Key0)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.DeleteObject);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.B && !ctrlPressed && !shiftPressed)
				{
					EditorBrushIsSquare = !EditorBrushIsSquare;
					UpdateBrushMesh();
					MapEditorHUD.Instance?.UpdateBrushShapeExternal(EditorBrushIsSquare);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.V && !ctrlPressed && !shiftPressed)
				{
					EditorGridVisible = !EditorGridVisible;
					UpdateGridOverlayVisibility();
					MapEditorHUD.Instance?.UpdateGridOverlayExternal(EditorGridVisible);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.T && !ctrlPressed && !shiftPressed)
				{
					GenerateNewRandomPlacementRotationAndScale();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Re-randomized Rotation & Scale");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Tab)
				{
					MapEditorHUD.Instance?.CycleTextureSwatch(!shiftPressed);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton wheelBtn && wheelBtn.Pressed && (wheelBtn.ButtonIndex == MouseButton.WheelUp || wheelBtn.ButtonIndex == MouseButton.WheelDown))
			{
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				bool shiftPressed = Input.IsKeyPressed(Key.Shift);
				bool isUp = wheelBtn.ButtonIndex == MouseButton.WheelUp;

				bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
									 ActiveEditorTool == EditorTool.Lower ||
									 ActiveEditorTool == EditorTool.Flatten ||
									 ActiveEditorTool == EditorTool.Smooth ||
									 ActiveEditorTool == EditorTool.Cliff ||
									 ActiveEditorTool == EditorTool.PaintGrass ||
									 ActiveEditorTool == EditorTool.PaintDirt ||
									 ActiveEditorTool == EditorTool.PaintRock ||
									 ActiveEditorTool == EditorTool.PaintSand ||
									 ActiveEditorTool == EditorTool.Noise;

				if (isTerrainTool)
				{
					if (shiftPressed)
					{
						float deltaSize = isUp ? 1.0f : -1.0f;
						EditorBrushRadius = Mathf.Clamp(EditorBrushRadius + deltaSize, 1.0f, 25.0f);
						MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
						GetViewport().SetInputAsHandled();
						return;
					}
					if (ctrlPressed)
					{
						float deltaStr = isUp ? 0.5f : -0.5f;
						EditorBrushStrength = Mathf.Clamp(EditorBrushStrength + deltaStr, 0.1f, 10.0f);
						MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				if (shiftPressed)
				{
					float rotDelta = isUp ? 15.0f : -15.0f;
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldRot = node3D.RotationDegrees;
						Vector3 newRot = oldRot;
						newRot.Y = (newRot.Y + rotDelta + 360.0f) % 360.0f;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							oldRot, newRot,
							node3D.Scale, node3D.Scale,
							isEnemy, isEnemy
						);
						node3D.RotationDegrees = newRot;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
					}
					else
					{
						EditorPlacementRotation = (EditorPlacementRotation + rotDelta + 360.0f) % 360.0f;
						MapEditorHUD.Instance?.UpdateRotationExternal(EditorPlacementRotation);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (ctrlPressed)
				{
					float scaleDelta = isUp ? 0.1f : -0.1f;
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldScale = node3D.Scale;
						float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 3.0f);
						Vector3 newScale = Vector3.One * newScaleVal;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							node3D.RotationDegrees, node3D.RotationDegrees,
							oldScale, newScale,
							isEnemy, isEnemy
						);
						node3D.Scale = newScale;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
					}
					else
					{
						EditorPlacementScale = Mathf.Clamp(EditorPlacementScale + scaleDelta, 0.2f, 3.0f);
						MapEditorHUD.Instance?.UpdateScaleExternal(EditorPlacementScale);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton editorRightMouseBtn && editorRightMouseBtn.Pressed && editorRightMouseBtn.ButtonIndex == MouseButton.Right)
			{
				if (IsMouseOverUI()) return;
				if (_rampStartPos != null)
				{
					_rampStartPos = null;
					MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Cancelled");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (SelectedEditorObject != null)
				{
					SelectedEditorObject = null;
					MapEditorHUD.Instance?.ShowFeedbackExternal("Deselected Object");
					GetViewport().SetInputAsHandled();
					return;
				}
				else if (ActiveEditorTool == EditorTool.PasteArea)
				{
					ActiveEditorTool = EditorTool.SelectArea;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
					if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
					GetViewport().SetInputAsHandled();
					return;
				}
				else if (ActiveEditorTool != EditorTool.SelectMove)
				{
					ActiveEditorTool = EditorTool.SelectMove;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
					if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton releaseEvent && !releaseEvent.Pressed && releaseEvent.ButtonIndex == MouseButton.Left)
			{
				if (_isSelectingArea)
				{
					_isSelectingArea = false;
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton editorMouseBtn && editorMouseBtn.Pressed && editorMouseBtn.ButtonIndex == MouseButton.Left)
			{
				if (IsMouseOverUI()) return;
				
				var hit = RaycastFromMouse(editorMouseBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					Vector3 hitPos = hit["position"].AsVector3();
					
					if (EditorSnapToGrid && GroundTerrain != null)
					{
						float spacing = GroundTerrain.Spacing;
						int width = GroundTerrain.Width;
						int depth = GroundTerrain.Depth;
						float fx = Mathf.Round(hitPos.X / spacing + (width - 1) / 2.0f);
						hitPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
						float fz = Mathf.Round(hitPos.Z / spacing + (depth - 1) / 2.0f);
						hitPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
					}
					
					if (ActiveEditorTool == EditorTool.PlaceUnit)
					{
						if (EditorClumpMode) return;
						if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;

						Vector3 spawnPos = hitPos;
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float radius = GetPlacementRadius(ActivePlaceId, scaleVal);
						var finalPos = FindNearestFreePosition(spawnPos, radius);
						if (finalPos == null)
						{
							MapEditorHUD.Instance?.ShowFeedbackExternal("invalid location");
							UIManager.Instance?.PlayWarningSound();
							GetViewport().SetInputAsHandled();
							return;
						}
						spawnPos = finalPos.Value;

						var unit = SpawnUnitExternal(ActivePlaceId, spawnPos, PlaceUnitIsEnemy, placementRot, scaleVal);
						if (unit != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("unit", ActivePlaceId, spawnPos, placementRot, scaleVal, PlaceUnitIsEnemy, unit)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									if (IsPositionBlocked(mPos, radius)) continue;
									var mUnit = SpawnUnitExternal(ActivePlaceId, mPos, PlaceUnitIsEnemy, t.Rotation, scaleVal);
									if (mUnit != null)
									{
										actions.Add(new ObjectSpawnAction("unit", ActivePlaceId, mPos, t.Rotation, scaleVal, PlaceUnitIsEnemy, mUnit));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_isPastingObject = false;
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceProp)
					{
						if (EditorClumpMode) return;
						if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;

						Vector3 spawnPos = hitPos;
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float radius = GetPlacementRadius(ActivePlaceId, scaleVal);
						var finalPos = FindNearestFreePosition(spawnPos, radius);
						if (finalPos == null)
						{
							MapEditorHUD.Instance?.ShowFeedbackExternal("invalid location");
							UIManager.Instance?.PlayWarningSound();
							GetViewport().SetInputAsHandled();
							return;
						}
						spawnPos = finalPos.Value;

						var prop = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, placementRot, scaleVal);
						if (prop != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("prop", ActivePlaceId, spawnPos, placementRot, scaleVal, false, prop)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									if (IsPositionBlocked(mPos, radius)) continue;
									var mProp = SpawnPropExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
									if (mProp != null)
									{
										actions.Add(new ObjectSpawnAction("prop", ActivePlaceId, mPos, t.Rotation, scaleVal, false, mProp));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_isPastingObject = false;
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceDecal)
					{
						if (EditorClumpMode) return;
						if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;
						var decal = SpawnDecalExternalWithParams(ActivePlaceId, hitPos, placementRot, scaleVal);
						if (decal != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("decal", ActivePlaceId, hitPos, placementRot, scaleVal, false, decal)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(hitPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									var mDecal = SpawnDecalExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
									if (mDecal != null)
									{
										actions.Add(new ObjectSpawnAction("decal", ActivePlaceId, mPos, t.Rotation, scaleVal, false, mDecal));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_isPastingObject = false;
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.DeleteObject)
					{
						var collider = hit["collider"].As<Node>();
						var action = DeleteObjectAtWithUndo(collider, hitPos);
						if (action != null)
						{
							var actions = new List<IEditorAction> { action };
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(hitPos, 0.0f))
								{
									var nearObj = FindObjectNearPosition(t.Position);
									if (nearObj != null)
									{
										var mAction = DeleteObjectAtWithUndo(nearObj, t.Position);
										if (mAction != null)
										{
											actions.Add(mAction);
										}
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.Eyedropper)
					{
						string mode = MapEditorHUD.Instance != null ? MapEditorHUD.Instance.GetEyedropperMode() : "all";
						var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
						Node clickedNode = null;

						if (mode == "all" || mode == "3d")
						{
							if (collider != null)
							{
								clickedNode = FindUnit3DInParentChain(collider);
								if (clickedNode == null)
								{
									clickedNode = FindProp3DInParentChain(collider);
								}
							}
						}

						if (clickedNode == null && (mode == "all" || mode == "decal"))
						{
							Decal closestDecal = null;
							float closestDist = 3.0f;
							foreach (var child in GetChildren())
							{
								if (child is Decal dec && GodotObject.IsInstanceValid(dec))
								{
									float d = dec.GlobalPosition.DistanceTo(hitPos);
									if (d < closestDist)
									{
										closestDist = d;
										closestDecal = dec;
									}
								}
							}
							if (closestDecal != null)
							{
								clickedNode = closestDecal;
							}
						}

						if (clickedNode != null)
						{
							if (clickedNode is Unit3D unit)
							{
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedUnitOrProp(unit.UnitId, unit.IsBuilding);
								}
								else
								{
									ActivePlaceId = unit.UnitId;
									PlaceUnitIsEnemy = unit.IsEnemy;
									ActiveEditorTool = EditorTool.PlaceUnit;
								}
							}
							else if (clickedNode is Prop3D prop)
							{
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedUnitOrProp(prop.PropId, false);
								}
								else
								{
									ActivePlaceId = prop.PropId;
									ActiveEditorTool = EditorTool.PlaceProp;
								}
							}
							else if (clickedNode is Decal decal)
							{
								string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedDecal(decalId);
								}
								else
								{
									ActivePlaceId = decalId;
									ActiveEditorTool = EditorTool.PlaceDecal;
								}
							}
						}
						else
						{
							bool wantHeight = (mode == "height") || (mode == "all" && Input.IsKeyPressed(Key.Shift));
							bool wantTerrain = (mode == "terrain") || (mode == "all" && !Input.IsKeyPressed(Key.Shift));

							if (wantHeight)
							{
								float sampledHeight = GetTerrainHeightAt(hitPos);
								EditorFlattenHeight = sampledHeight;
								MapEditorHUD.Instance?.UpdateFlattenHeightExternal(sampledHeight);
								MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Flatten);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Picked Height: {sampledHeight:F1}m");
							}
							else if (wantTerrain && GroundTerrain != null)
							{
								int w = GroundTerrain.Width;
								int d = GroundTerrain.Depth;
								float sp = GroundTerrain.Spacing;
								float fx = hitPos.X / sp + (w - 1) / 2.0f;
								float fz = hitPos.Z / sp + (d - 1) / 2.0f;
								int x = Mathf.Clamp((int)Math.Round(fx), 0, w - 1);
								int z = Mathf.Clamp((int)Math.Round(fz), 0, d - 1);
								Color sampledColor = GroundTerrain.Colors[x, z];
								EditorPaintColor = sampledColor;
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPaintSwatchFromColor(sampledColor);
								}
								else
								{
									ActiveEditorTool = EditorTool.PaintGrass;
								}
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Picked Color: {sampledColor.ToHtml(false)}");
							}
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.SelectMove)
					{
						var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
						Node clickedNode = null;
						if (collider != null)
						{
							clickedNode = FindUnit3DInParentChain(collider);
							if (clickedNode == null)
							{
								clickedNode = FindProp3DInParentChain(collider);
							}
						}
						if (clickedNode == null)
						{
							Decal closestDecal = null;
							float closestDist = 3.0f;
							foreach (var child in GetChildren())
							{
								if (child is Decal dec && GodotObject.IsInstanceValid(dec))
								{
									float d = dec.GlobalPosition.DistanceTo(hitPos);
									if (d < closestDist)
									{
										closestDist = d;
										closestDecal = dec;
									}
								}
							}
							if (closestDecal != null)
							{
								clickedNode = closestDecal;
							}
						}
						if (clickedNode != null)
						{
							SelectedEditorObject = clickedNode;
							_isDraggingObject = true;
							_dragObjectStartPos = (SelectedEditorObject as Node3D).Position;
							_dragObjectStartRot = (SelectedEditorObject as Node3D).RotationDegrees;
							_dragObjectStartScale = (SelectedEditorObject as Node3D).Scale;
							_dragObjectStartIsEnemy = (SelectedEditorObject is Unit3D u) ? u.IsEnemy : false;
							_dragObjectStartHitPos = hitPos;
							_dragObjectHasMoved = false;
						}
						else
						{
							SelectedEditorObject = null;
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.Ramp)
					{
						if (_rampStartPos == null)
						{
							_rampStartPos = hitPos;
							MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Start Point Set!");
						}
						else
						{
							Vector3 start = _rampStartPos.Value;
							Vector3 end = hitPos;
							var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
							var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
							bool modified = ApplyRampInternal(start, end);
							if (EditorMirrorMode != MirrorMode.None)
							{
								var startMirrored = GetMirroredTransforms(start, 0.0f);
								var endMirrored = GetMirroredTransforms(end, 0.0f);
								for (int i = 0; i < startMirrored.Count; i++)
								{
									bool mResult = ApplyRampInternal(startMirrored[i].Position, endMirrored[i].Position);
									if (mResult) modified = true;
								}
							}
							if (modified)
							{
								GroundTerrain.UpdateMeshAndPhysics(true, false);
								AlignAllEntitiesToTerrain();
								var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
								var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
								var action = new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter);
								EditorHistoryManager.RecordAction(action);
								EditorHasUnsavedChanges = true;
							}
							_rampStartPos = null;
							MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Created!");
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.FloodFill)
					{
						PerformFloodFill(hitPos, EditorPaintColor);
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.SelectArea)
					{
						if (GroundTerrain != null)
						{
							float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
							float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
							int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
							int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
							_selectionStart = new Vector2I(cx, cz);
							_selectionEnd = new Vector2I(cx, cz);
							_isSelectingArea = true;
							CreateSelectionHighlight();
							RebuildSelectionHighlightMesh(cx, cz, cx, cz);
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PasteArea)
					{
						if (GroundTerrain != null && _copiedArea != null)
						{
							float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
							float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
							int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
							int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
							PerformPasteArea(cx, cz);
							ActiveEditorTool = EditorTool.SelectArea;
							MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
							if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
						}
						GetViewport().SetInputAsHandled();
					}
				}
			}
			return;
		}

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			return;
		}

		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		if (isSpectator)
		{
			if (@event is InputEventKey specKeyEvent && specKeyEvent.Pressed && !specKeyEvent.Echo)
			{
				if (specKeyEvent.Keycode != Key.Escape && specKeyEvent.Keycode != Key.Space && specKeyEvent.Keycode != Key.Z && specKeyEvent.Keycode != Key.Tab)
				{
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			else if (@event is InputEventMouseButton specMouseBtn)
			{
				if (specMouseBtn.ButtonIndex == MouseButton.Right)
				{
					GetViewport().SetInputAsHandled();
					return;
				}
				if (specMouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (_activeSpellTargeting != null || _activeCommandTargeting != null || _activeBuildingPlacementType != null)
					{
						_activeSpellTargeting = null;
						_activeCommandTargeting = null;
						CancelBuildingPlacement();
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
			}
		}

		if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && escapeEvent.Keycode == Key.Escape)
		{
			if (_activeSpellTargeting != null || _activeCommandTargeting != null)
			{
				_activeSpellTargeting = null;
				_activeCommandTargeting = null;
				Input.SetCustomMouseCursor(null);
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				if (InGameHUD.Instance != null)
					InGameHUD.Instance.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}
			if (_activeBuildingPlacementType != null)
			{
				CancelBuildingPlacement();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (ActivePingMode)
			{
				ActivePingMode = false;
				if (InGameHUD.Instance != null)
					InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}
			if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
			{
				InGameHUD.Instance.ExitBuildSubMenu();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (SelectedUnits.Count > 0)
			{
				if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].UnitId == "castle")
				{
					var castle = SelectedUnits[0];
					if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castle.Entity) && EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castle.Entity).UnitIds.Count > 0)
					{
						CancelLastQueuedUnit(castle.Entity);
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				ClearSelection();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
				GetViewport().SetInputAsHandled();
				return;
			}

			GetViewport().SetInputAsHandled();
			UIManager.Instance.OpenSettingsOverlay();
			return;
		}

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode >= Key.Key0 && keyEvent.Keycode <= Key.Key9)
			{
				int groupIdx = (int)(keyEvent.Keycode - Key.Key0);
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				if (ctrlPressed)
				{
					AssignControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					RecallControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (keyEvent.Keycode == Key.F1)
			{
				SelectAllIdleUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F2)
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F4)
			{
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.ToggleMinimapTerrain();
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.G && Input.IsKeyPressed(Key.Alt))
			{
				ActivePingMode = !ActivePingMode;
				if (InGameHUD.Instance != null)
				{
					if (ActivePingMode)
						InGameHUD.Instance.ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
					else
						InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.A && Input.IsKeyPressed(Key.Ctrl))
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.Z)
			{
				CycleCameraZoom();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.Space)
			{
				CenterCameraOnCastle();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy)
			{
				var selectedUnit = SelectedUnits[0];
				if (selectedUnit.UnitId == "castle")
				{
					if (keyEvent.Keycode == Key.F)
					{
						TrainUnitAtCastle("soldier");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.R)
					{
						TrainUnitAtCastle("archer");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.P)
					{
						TrainUnitAtCastle("priest");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.W)
					{
						BuyWeaponsUpgrade();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.G)
					{
						BuyShieldsUpgrade();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.T)
					{
						BuyHarvestingUpgrade();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.Y)
					{
						EnterCommandTargeting("rally");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.I)
					{
						BuyHealingPotion(selectedUnit.Entity);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				else if (selectedUnit.UnitId == "tower")
				{
					if (keyEvent.Keycode == Key.U)
					{
						UpgradeTower(selectedUnit);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
			}

			if (keyEvent.Keycode == Key.Tab)
			{
				CycleSelectionFocus();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.F3)
			{
				SelectAllBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.F5)
			{
				InGameHUD.Instance?.ToggleHotkeyPanel();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.Delete)
			{
				DeleteSelectedUnits();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.Quoteleft) 
			{
				CycleThroughBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}

			bool hasPlayerSelection = SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy;
			if (hasPlayerSelection)
			{
				switch (keyEvent.Keycode)
				{
					case Key.A:
						EnterCommandTargeting("attack");
						GetViewport().SetInputAsHandled();
						return;
					case Key.M:
						EnterCommandTargeting("move");
						GetViewport().SetInputAsHandled();
						return;
					case Key.P:
						EnterCommandTargeting("patrol");
						GetViewport().SetInputAsHandled();
						return;
					case Key.S:
						StopSelectedUnits();
						if (InGameHUD.Instance != null)
							InGameHUD.Instance.ShowFeedbackText("Command: Stop Current Action", new Color(0.9f, 0.2f, 0.2f));
						GetViewport().SetInputAsHandled();
						return;
					case Key.H:
						HoldSelectedUnits();
						if (InGameHUD.Instance != null)
							InGameHUD.Instance.ShowFeedbackText("Command: Hold Position", new Color(0.9f, 0.8f, 0.1f));
						GetViewport().SetInputAsHandled();
						return;
					case Key.B:
						if (InGameHUD.Instance != null)
						{
							InGameHUD.Instance.ShowFeedbackText("Build Mode: Select Building structures", new Color(0.3f, 0.8f, 1.0f));
							InGameHUD.Instance.EnterBuildSubMenu();
						}
						GetViewport().SetInputAsHandled();
						return;
					case Key.C:
						if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
						{
							EnterBuildingPlacement("castle");
							GetViewport().SetInputAsHandled();
							return;
						}
						break;
					case Key.T:
						if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
						{
							EnterBuildingPlacement("tower");
							GetViewport().SetInputAsHandled();
							return;
						}
						break;
					case Key.Q:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && UnitHasAbility(unit, "fireball"))
							{
								EnterSpellTargeting("fireball");
								GetViewport().SetInputAsHandled();
							}
						}
						return;
					case Key.E:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && UnitHasAbility(unit, "lightning"))
							{
								EnterSpellTargeting("lightning");
								GetViewport().SetInputAsHandled();
							}
						}
						return;
					case Key.W:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && UnitHasAbility(unit, "holylight"))
							{
								EnterSpellTargeting("holylight");
								GetViewport().SetInputAsHandled();
							}
						}
						return;
					case Key.I:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && !unit.IsBuilding)
							{
								UseHealingPotion(unit);
								GetViewport().SetInputAsHandled();
								return;
							}
						}
						break;
				}
			}
		}

		if (@event is InputEventMouseButton rightBtn && rightBtn.ButtonIndex == MouseButton.Right && rightBtn.Pressed && !IsMouseOverUI())
		{
			if (_activeSpellTargeting != null || _activeCommandTargeting != null || _activeBuildingPlacementType != null)
			{
				_activeSpellTargeting = null;
				_activeCommandTargeting = null;
				CancelBuildingPlacement();
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count == 0) goto SkipRightClick;

			bool anyFriendlySelected = false;
			foreach (var su in SelectedUnits)
			{
				if (!su.IsEnemy) { anyFriendlySelected = true; break; }
			}
			if (!anyFriendlySelected) goto SkipRightClick;

			if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].IsBuilding)
			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					SetRallyPoint(SelectedUnits[0], hit["position"].AsVector3());
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);
					bool shiftHeld = Input.IsKeyPressed(Key.Shift);

					if (clickedUnit != null && clickedUnit.IsEnemy && clickedUnit.Visible)
					{
						IssueAttackCommand(clickedUnit);
					}
					else if (clickedUnit != null && !clickedUnit.IsEnemy && clickedUnit != SelectedUnits.Find(u => !u.IsEnemy))
					{
						IssueFollowCommand(clickedUnit);
					}
					else if (clickedProp != null && (clickedProp.PropId == "goldmine" || clickedProp.PropId == "tree" || clickedProp.PropId == "rock"))
					{
						IssueGatherCommand(clickedProp);
					}
					else
					{
						if (shiftHeld)
						{
							IssueMoveCommandQueued(hitPos);
						}
						else
						{
							IssueMoveCommand(hitPos);
						}
					}
					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}
		SkipRightClick:

		if (@event is InputEventMouseButton mouseBtn)
		{
			if (mouseBtn.ButtonIndex == MouseButton.Left)
			{
				if (mouseBtn.Pressed)
				{
					GD.Print($"[GameHost] Unhandled left-click press at position: {mouseBtn.Position}");
					
					if (mouseBtn.DoubleClick)
					{
						PerformDoubleClickSelection(mouseBtn.Position);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (ActivePingMode)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							AddMinimapPing(hit["position"].AsVector3());
						}
						ActivePingMode = false;
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (_activeBuildingPlacementType != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							hitPos.Y = 0f;
							ExecuteBuildingPlacement(_activeBuildingPlacementType, hitPos);
						}
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (_activeSpellTargeting != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							ExecuteSpellCast(_activeSpellTargeting, hit["position"].AsVector3());
						}
						_activeSpellTargeting = null;
						Input.SetCustomMouseCursor(null); 
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else if (_activeCommandTargeting != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							var collider = hit["collider"].As<Node>();
							var clickedUnit = FindUnit3DInParentChain(collider);

							if (_activeCommandTargeting == "attack")
							{
								if (clickedUnit != null && clickedUnit.Entity != Entity.Null && clickedUnit.IsEnemy)
								{
									IssueAttackCommand(clickedUnit);
								}
								else
								{
									IssueAttackMoveCommand(hitPos);
								}
							}
							else if (_activeCommandTargeting == "move")
							{
								IssueMoveCommand(hitPos);
							}
							else if (_activeCommandTargeting == "patrol")
							{
								IssuePatrolCommand(hitPos);
							}
							else if (_activeCommandTargeting == "rally")
							{
								if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].IsBuilding)
								{
									SetRallyPoint(SelectedUnits[0], hitPos);
								}
							}
						}
						_activeCommandTargeting = null;
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else
					{
						if (IsMouseOverUI()) return;

						_isDragging = true;
						_dragStart = mouseBtn.Position;
						_dragEnd = mouseBtn.Position;
					}
				}
				else if (_isDragging)
				{
					GD.Print($"[GameHost] Unhandled left-click release at position: {mouseBtn.Position}");

					_isDragging = false;
					if (InGameHUD.Instance != null)
					{
						InGameHUD.Instance.UpdateDragBox(Vector2.Zero, Vector2.Zero, false);
					}

					float dragDist = _dragStart.DistanceTo(_dragEnd);
					if (dragDist > DragThreshold)
					{
						PerformBoxSelection(_dragStart, _dragEnd);
					}
					else
					{
						PerformSingleClickSelection(_dragStart);
					}
				}
			}
			else if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
			{
				GD.Print($"[GameHost] Unhandled right-click press at position: {mouseBtn.Position}");
				
				if (_activeBuildingPlacementType != null)
				{
					CancelBuildingPlacement();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActivePingMode)
				{
					ActivePingMode = false;
					if (InGameHUD.Instance != null)
						InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
					GetViewport().SetInputAsHandled();
					return;
				}

				if (_activeSpellTargeting != null || _activeCommandTargeting != null)
				{
					_activeSpellTargeting = null;
					_activeCommandTargeting = null;
					Input.SetCustomMouseCursor(null);
					Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					if (InGameHUD.Instance != null)
						InGameHUD.Instance.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
					return;
				}

				var hit = RaycastFromMouse(mouseBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);

					if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].IsBuilding)
					{
						SetRallyPoint(SelectedUnits[0], hitPos);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (clickedUnit != null && clickedUnit.Entity != Entity.Null)
					{
						if (clickedUnit.IsEnemy)
						{
							IssueAttackCommand(clickedUnit);
						}
						else
						{
							IssueFollowCommand(clickedUnit);
						}
					}
					else if (clickedProp != null && (clickedProp.PropId == "goldmine" || clickedProp.PropId == "tree" || clickedProp.PropId == "rock"))
					{
						IssueGatherCommand(clickedProp);
					}
					else
					{
						IssueMoveCommand(hitPos);
					}
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			_dragEnd = mouseMotion.Position;
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.UpdateDragBox(_dragStart, _dragEnd, true);
			}
		}
	}

	private void PerformSingleClickSelection(Vector2 clickPos)
	{
		GD.Print($"[GameHost] PerformSingleClickSelection at screen coordinate: {clickPos}");
		
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}
		SelectedProp = null;

		var hit = RaycastFromMouse(clickPos);
		if (hit != null && hit.ContainsKey("collider"))
		{
			var collider = hit["collider"].As<Node>();
			var clickedUnit = FindUnit3DInParentChain(collider);
			
			if (clickedUnit != null)
			{
				if (!clickedUnit.Visible)
				{
					ClearSelection();
					return;
				}
				if (clickedUnit.IsEnemy)
				{
					ClearSelection();
					SelectUnit(clickedUnit);
				}
				else
				{
					bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
					if (ctrlPressed)
					{
						PerformDoubleClickSelection(clickPos);
						return;
					}

					bool shiftPressed = Input.IsKeyPressed(Key.Shift);
					bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;
					
					if (selectingEnemy)
					{
						ClearSelection();
						SelectUnit(clickedUnit);
					}
					else if (shiftPressed)
					{
						if (SelectedUnits.Contains(clickedUnit))
						{
							DeselectUnit(clickedUnit);
						}
						else
						{
							SelectUnit(clickedUnit);
						}
					}
					else
					{
						ClearSelection();
						SelectUnit(clickedUnit);
					}
				}
			}
			else
			{
				var clickedProp = FindProp3DInParentChain(collider);
				if (clickedProp != null && (clickedProp.PropId == "goldmine" || clickedProp.PropId == "tree" || clickedProp.PropId == "rock"))
				{
					ClearSelection();
					SelectedProp = clickedProp;
					SelectedProp.IsSelected = true;
				}
				else
				{
					ClearSelection();
				}
			}
		}
		else
		{
			ClearSelection();
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void PerformBoxSelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		Vector2 min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
		Vector2 max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
		var dragRect = new Rect2(min, max - min);

		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;
		
		if (selectingEnemy || !shiftPressed)
		{
			ClearSelection();
		}

		foreach (var unit in AllUnits)
		{
			if (unit.IsEnemy) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			if (dragRect.HasPoint(screenPos))
			{
				SelectUnit(unit);
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void SelectUnit(Unit3D unit)
	{
		if (!SelectedUnits.Contains(unit))
		{
			SelectedUnits.Add(unit);
			unit.IsSelected = true;
			OnUnitSelected?.Invoke(GetUnitWrapper(unit.Entity));
		}
	}

	private void ClearSelection()
	{
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}
		SelectedProp = null;

		foreach (var u in SelectedUnits)
		{
			u.IsSelected = false;
		}
		SelectedUnits.Clear();
		_cycleSelectionIndex = 0;
	}

	private Unit3D FindUnit3DInParentChain(Node node)
	{
		while (node != null)
		{
			if (node is Unit3D unit)
			{
				return unit;
			}
			node = node.GetParent();
		}
		return null;
	}



	private Godot.Collections.Dictionary RaycastFromMouse(Vector2 mousePos)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return null;

		var from = camera.ProjectRayOrigin(mousePos);
		var to = from + camera.ProjectRayNormal(mousePos) * 1000f;

		var spaceState = GetWorld3D().DirectSpaceState;
		if (_cachedRaycastQuery == null)
		{
			_cachedRaycastQuery = PhysicsRayQueryParameters3D.Create(from, to);
		}
		else
		{
			_cachedRaycastQuery.From = from;
			_cachedRaycastQuery.To = to;
		}
		var result = spaceState.IntersectRay(_cachedRaycastQuery);

		if (result.Count == 0) return null;
		return result;
	}

	public void EnterCommandTargeting(string mode)
	{
		_activeCommandTargeting = mode;
		_activeSpellTargeting = null; 
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);
		
		if (InGameHUD.Instance != null)
		{
			if (mode == "attack")
			{
				InGameHUD.Instance.ShowFeedbackText("Attack Command: Click enemy to attack, or ground to Attack-Move", new Color(0.9f, 0.4f, 0.1f));
			}
			else if (mode == "move")
			{
				InGameHUD.Instance.ShowFeedbackText("Move Command: Click ground to move", new Color(0.2f, 0.9f, 0.3f));
			}
			else if (mode == "patrol")
			{
				InGameHUD.Instance.ShowFeedbackText("Patrol Command: Click ground to set patrol endpoint", new Color(0.7f, 0.4f, 1.0f));
			}
			else if (mode == "rally")
			{
				InGameHUD.Instance.ShowFeedbackText("Rally Command: Click ground to set building Rally Point", new Color(1.0f, 0.85f, 0.5f));
			}
		}
	}

	public void IssueMoveCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.1f, 0.9f, 0.2f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Command: Move to position", new Color(0.2f, 0.9f, 0.3f));
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			int clientUnitIndex = 0;
			int clientCols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
			float clientSpacing = 2.2f;

			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				int row = clientUnitIndex / clientCols;
				int col = clientUnitIndex % clientCols;
				float offsetX = (col - clientCols * 0.5f + 0.5f) * clientSpacing;
				float offsetZ = row * clientSpacing;
				Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var moveTo = new MoveTo(new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z));
				if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
				else EcsWorld.Add(unit.Entity, moveTo);
				clientUnitIndex++;
			}
			QueueClientCommand("move", targetIds, targetPos, 0, "");
			return;
		}

		int unitIndex = 0;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
		float spacing = 2.2f;

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			ClearUnitOrders(unit.Entity);

			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var moveTo = new MoveTo(new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z));
				if (EcsWorld.Has<MoveTo>(unit.Entity))
					EcsWorld.Set(unit.Entity, moveTo);
				else
					EcsWorld.Add(unit.Entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueAttackCommand(Unit3D target)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(target.GlobalPosition, new Color(0.9f, 0.1f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Command: Attack {target.UnitId.ToUpper()}", new Color(0.9f, 0.2f, 0.2f));
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				var attackTarget = new AttackTarget(target.Entity);
				if (EcsWorld.Has<AttackTarget>(unit.Entity)) EcsWorld.Set(unit.Entity, attackTarget);
				else EcsWorld.Add(unit.Entity, attackTarget);
			}
			QueueClientCommand("attack", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue; 

			ClearUnitOrders(unit.Entity);

			var attackTarget = new AttackTarget(target.Entity);
			if (EcsWorld.Has<AttackTarget>(unit.Entity))
				EcsWorld.Set(unit.Entity, attackTarget);
			else
				EcsWorld.Add(unit.Entity, attackTarget);
		}
	}

	public void IssueFollowCommand(Unit3D target)
	{
		if (SelectedUnits.Count == 0 || target == null || target.Entity == Entity.Null) return;

		SpawnTargetIndicator(target.GlobalPosition, new Color(0.2f, 0.6f, 1.0f));

		if (InGameHUD.Instance != null)
		{
			bool hasPriest = false;
			foreach (var unit in SelectedUnits)
			{
				if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
				{
					hasPriest = true;
					break;
				}
			}
			if (hasPriest)
			{
				InGameHUD.Instance.ShowFeedbackText($"Priest: Healing support target {target.UnitId.ToUpper()}", new Color(0.2f, 0.9f, 0.3f));
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText($"Command: Follow {target.UnitId.ToUpper()}", new Color(0.2f, 0.6f, 1.0f));
			}
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy || unit.Entity == target.Entity) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
				{
					var healTarget = new HealingTarget(target.Entity);
					EcsWorld.Add(unit.Entity, healTarget);
				}
				else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
				{
					var follow = new Realm.Ecs.Components.Movement.Follow(target.Entity);
					if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity)) EcsWorld.Set(unit.Entity, follow);
					else EcsWorld.Add(unit.Entity, follow);
				}
			}
			QueueClientCommand("follow", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.Entity == target.Entity) continue; 

			ClearUnitOrders(unit.Entity);

			if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
			{
				var healTarget = new HealingTarget(target.Entity);
				EcsWorld.Add(unit.Entity, healTarget);
			}
			else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
			{
				var follow = new Realm.Ecs.Components.Movement.Follow(target.Entity);
				if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity))
					EcsWorld.Set(unit.Entity, follow);
				else
					EcsWorld.Add(unit.Entity, follow);
			}
		}
	}



	private void SelectAllBuildings()
	{
		ClearSelection();
		int count = 0;
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.IsBuilding)
			{
				SelectUnit(unit);
				count++;
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		InGameHUD.Instance?.ShowFeedbackText($"Selected {count} Buildings", new Color(0.9f, 0.7f, 0.2f));
	}


	private void CycleSelectionFocus(bool reverse = false)
	{
		if (SelectedUnits.Count <= 1) return;
		if (reverse)
		{
			_cycleSelectionIndex = (_cycleSelectionIndex - 1 + SelectedUnits.Count) % SelectedUnits.Count;
		}
		else
		{
			_cycleSelectionIndex = (_cycleSelectionIndex + 1) % SelectedUnits.Count;
		}
		var focusUnit = SelectedUnits[_cycleSelectionIndex];

		var camera = GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(focusUnit.GlobalPosition.X, camera.GlobalPosition.Y, focusUnit.GlobalPosition.Z);
		}
		InGameHUD.Instance?.ShowFeedbackText($"Focused: {focusUnit.UnitId.ToUpper()} ({_cycleSelectionIndex + 1}/{SelectedUnits.Count})", new Color(0.5f, 1.0f, 0.5f));
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}


	private void CycleThroughBuildings()
	{
		var buildings = AllUnits.FindAll(u => !u.IsEnemy && u.IsBuilding);
		if (buildings.Count == 0) return;
		_buildingCycleIndex = (_buildingCycleIndex + 1) % buildings.Count;
		var building = buildings[_buildingCycleIndex];
		var camera = GetViewport().GetCamera3D();
		if (camera != null)
			camera.GlobalPosition = new Vector3(building.GlobalPosition.X, camera.GlobalPosition.Y, building.GlobalPosition.Z);
		SelectOnlyUnit(building);
		InGameHUD.Instance?.ShowFeedbackText($"Jumped to: {building.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.3f));
	}

	private void DeleteSelectedUnits()
	{
		if (SelectedUnits.Count == 0) return;

		var toDelete = new List<Unit3D>(SelectedUnits.FindAll(u => !u.IsEnemy));
		foreach (var unit in toDelete)
		{
			if (!EcsWorld.Has<Dead>(unit.Entity))
				EcsWorld.Add<Dead>(unit.Entity);
			CallDeferred("KillUnitDeferred", unit);
		}
		InGameHUD.Instance?.ShowFeedbackText($"Removed {toDelete.Count} unit(s)", new Color(0.9f, 0.3f, 0.3f));
	}

	private void KillUnitDeferred(Unit3D unit)
	{
		if (AllUnits.Contains(unit))
			KillUnit(unit);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void SelectAllIdleUnits()
	{
		ClearSelection();
		int selectedCount = 0;
		foreach (var unit in AllUnits)
		{
			if (unit.IsEnemy || unit.IsBuilding) continue;
			
			bool isMovable = EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity);
			bool hasMoveTo = EcsWorld.Has<MoveTo>(unit.Entity);
			bool hasAttackTarget = EcsWorld.Has<AttackTarget>(unit.Entity);
			bool hasAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity);
			bool isGathering = EcsWorld.Has<Gatherer>(unit.Entity) && EcsWorld.Get<Gatherer>(unit.Entity).TargetEntity != Entity.Null;

			if (isMovable && !hasMoveTo && !hasAttackTarget && !hasAttackMove && !isGathering)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected {selectedCount} Idle Units", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	public void SelectAllMilitaryUnits()
	{
		ClearSelection();
		int selectedCount = 0;
		foreach (var unit in AllUnits)
		{
			if (unit.IsEnemy || unit.IsBuilding) continue;
			
			bool isMovable = EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity);
			if (isMovable)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected All Army ({selectedCount} Units)", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	private void PerformDoubleClickSelection(Vector2 clickPos)
	{
		GD.Print($"[GameHost] PerformDoubleClickSelection at screen coordinate: {clickPos}");
		var hit = RaycastFromMouse(clickPos);
		if (hit != null && hit.ContainsKey("collider"))
		{
			var collider = hit["collider"].As<Node>();
			var clickedUnit = FindUnit3DInParentChain(collider);
			
			if (clickedUnit != null && !clickedUnit.IsEnemy)
			{
				ClearSelection();
				
				var camera = GetViewport().GetCamera3D();
				if (camera != null)
				{
					var windowSize = GetViewport().GetVisibleRect().Size;
					var screenRect = new Rect2(Vector2.Zero, windowSize);
					string targetUnitId = clickedUnit.UnitId;
					int count = 0;

					foreach (var unit in AllUnits)
					{
						if (!unit.IsEnemy && unit.UnitId == targetUnitId && !unit.IsBuilding)
						{
							var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
							if (screenRect.HasPoint(screenPos))
							{
								SelectUnit(unit);
								count++;
							}
						}
					}
					
					if (InGameHUD.Instance != null)
					{
						InGameHUD.Instance.ShowFeedbackText($"Selected {count} {targetUnitId.ToUpper()}s on screen", new Color(0.5f, 1.0f, 0.5f));
					}
				}
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void CenterCameraOnCastle()
	{
		Unit3D friendlyCastle = null;
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.UnitId == "castle")
			{
				friendlyCastle = unit;
				break;
			}
		}
		var camera = GetViewport().GetCamera3D();
		if (friendlyCastle != null && camera != null)
		{
			var zVector = camera.GlobalTransform.Basis.Z;
			if (Mathf.Abs(zVector.Y) > 0.001f)
			{
				float dist = (camera.GlobalPosition.Y - friendlyCastle.GlobalPosition.Y) / zVector.Y;
				camera.GlobalPosition = friendlyCastle.GlobalPosition + zVector * dist;
			}
			else
			{
				camera.GlobalPosition = new Vector3(friendlyCastle.GlobalPosition.X, camera.GlobalPosition.Y, friendlyCastle.GlobalPosition.Z);
			}
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Camera Centered on Castle", new Color(0.5f, 0.8f, 1.0f));
			}
		}
	}

	public void CenterCameraOnSelectedOrCastle()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		if (SelectedEditorObject is Node3D node3D && GodotObject.IsInstanceValid(node3D))
		{
			var zVector = camera.GlobalTransform.Basis.Z;
			if (Mathf.Abs(zVector.Y) > 0.001f)
			{
				float dist = (camera.GlobalPosition.Y - node3D.GlobalPosition.Y) / zVector.Y;
				camera.GlobalPosition = node3D.GlobalPosition + zVector * dist;
			}
			else
			{
				camera.GlobalPosition = new Vector3(node3D.GlobalPosition.X, camera.GlobalPosition.Y, node3D.GlobalPosition.Z);
			}
			MapEditorHUD.Instance?.ShowFeedbackExternal("Centered Camera on Selected Object");
			return;
		}

		CenterCameraOnCastle();
	}

	public void TriggerCopyFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea)
		{
			PerformCopyArea();
			return;
		}
		if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
		{
			if (SelectedEditorObject is Unit3D unit)
			{
				_copiedObject = new CopiedObjectTemplate {
					Type = "unit",
					Id = unit.UnitId,
					Rotation = unit.RotationDegrees.Y,
					Scale = unit.Scale.X,
					IsEnemy = unit.IsEnemy
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
			}
			else if (SelectedEditorObject is Prop3D prop)
			{
				_copiedObject = new CopiedObjectTemplate {
					Type = "prop",
					Id = prop.PropId,
					Rotation = prop.RotationDegrees.Y,
					Scale = prop.Scale.X,
					IsEnemy = false
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
			}
			else if (SelectedEditorObject is Decal decal)
			{
				string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
				_copiedObject = new CopiedObjectTemplate {
					Type = "decal",
					Id = decalId,
					Rotation = decal.RotationDegrees.Y,
					Scale = decal.Scale.X,
					IsEnemy = false
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
			}
		}
		else
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an object or area first)");
		}
	}

	public void TriggerPasteFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
		{
			if (_copiedArea != null)
			{
				ActiveEditorTool = EditorTool.PasteArea;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
				MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
				return;
			}
		}
		if (_copiedObject != null)
		{
			if (_copiedObject.Value.Type == "unit")
			{
				ActiveEditorTool = EditorTool.PlaceUnit;
				_isPastingObject = true;
				ActivePlaceId = _copiedObject.Value.Id;
				PlaceUnitIsEnemy = _copiedObject.Value.IsEnemy;
				EditorPlacementRotation = _copiedObject.Value.Rotation;
				EditorPlacementScale = _copiedObject.Value.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceUnit);
			}
			else if (_copiedObject.Value.Type == "prop")
			{
				ActiveEditorTool = EditorTool.PlaceProp;
				_isPastingObject = true;
				ActivePlaceId = _copiedObject.Value.Id;
				EditorPlacementRotation = _copiedObject.Value.Rotation;
				EditorPlacementScale = _copiedObject.Value.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceProp);
			}
			else if (_copiedObject.Value.Type == "decal")
			{
				ActiveEditorTool = EditorTool.PlaceDecal;
				_isPastingObject = true;
				ActivePlaceId = _copiedObject.Value.Id;
				EditorPlacementRotation = _copiedObject.Value.Rotation;
				EditorPlacementScale = _copiedObject.Value.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceDecal);
			}
			MapEditorHUD.Instance?.ShowFeedbackExternal($"Paste Mode Active - Placing {_copiedObject.Value.Id.ToUpper()}");
		}
		else
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Paste (copy an object or area first)");
		}
	}

	private void AssignControlGroup(int index)
	{
		var groupUnits = new List<Unit3D>();
		foreach (var u in SelectedUnits)
		{
			if (!u.IsEnemy)
			{
				groupUnits.Add(u);
			}
		}
		_controlGroups[index] = groupUnits;
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Assigned {groupUnits.Count} units to Control Group {index}", new Color(0.5f, 0.8f, 1.0f));
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	public void RecallControlGroup(int index)
	{
		var group = _controlGroups[index];
		if (group == null || group.Count == 0) return;

		group.RemoveAll(u => !GodotObject.IsInstanceValid(u) || !AllUnits.Contains(u));
		if (group.Count == 0) return;

		ClearSelection();
		foreach (var u in group)
		{
			SelectUnit(u);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);

		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastGroupPressTime[index] < 0.3)
		{
			Vector3 sumPos = Vector3.Zero;
			foreach (var u in group)
			{
				sumPos += u.GlobalPosition;
			}
			Vector3 avgPos = sumPos / group.Count;
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(avgPos.X, camera.GlobalPosition.Y, avgPos.Z);
			}
		}
		_lastGroupPressTime[index] = now;
	}

	public void ClearTargetingModes()
	{
		_activeSpellTargeting = null;
		_activeCommandTargeting = null;
		_activeBuildingPlacementType = null;
		Input.SetCustomMouseCursor(null);
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}
	}

	public void CastSpellAt(string spellId, Vector3 position)
	{
		ExecuteSpellCast(spellId, position);
	}

	public void PlaceBuildingAt(string type, Vector3 position)
	{
		ExecuteBuildingPlacement(type, position);
	}

	private bool UnitHasAbility(Unit3D unit, string abilityId)
	{
		if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
		{
			if (meta.Abilities != null)
			{
				return Array.Exists(meta.Abilities, a => a == abilityId);
			}
		}
		if (abilityId == "holylight") return unit.UnitId == "priest";
		if (abilityId == "fireball" || abilityId == "lightning") return unit.UnitId == "tower";
		return false;
	}

	public void EnterSpellTargeting(string spellId)
	{
		if (SelectedUnits.Count == 0) return;
		var unit = SelectedUnits[CycleSelectionIndex];
		if (unit.IsEnemy) return;
		
		if (!UnitHasAbility(unit, spellId)) return;

		_activeSpellTargeting = spellId;
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Casting: Select Location for {spellId.ToUpper()}", new Color(1f, 0.7f, 0.1f));
		}
	}

	private void ExecuteSpellCast(string spellId, Vector3 position)
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			if (spellId == "fireball")
			{
				FireballCooldown = FireballCooldownMax;
				SpawnFireblastEffect(position);
				SpawnTargetIndicator(position, new Color(0.9f, 0.3f, 0.1f));
			}
			else if (spellId == "lightning")
			{
				LightningCooldown = LightningCooldownMax;
				SpawnLightningEffect(position);
				SpawnTargetIndicator(position, new Color(0.2f, 0.5f, 1f));
			}
			else if (spellId == "holylight")
			{
				HolyLightCooldown = HolyLightCooldownMax;
				SpawnHolyLightEffect(position);
				SpawnTargetIndicator(position, new Color(0.2f, 0.9f, 0.3f));
			}

			var targetIds = new List<int>();
			if (SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy)
			{
				targetIds.Add(GetServerEntityId(SelectedUnits[0].Entity));
			}
			QueueClientCommand("spell", targetIds, position, 0, spellId);
			return;
		}

		IUnit? caster = null;
		if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
		{
			caster = GetUnitWrapper(SelectedUnits[0].Entity);
		}
		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		if (spellId == "fireball")
		{
			if (FireballCooldown > 0)
			{
				InGameHUD.Instance?.ShowFeedbackText($"Fireball on cooldown: {FireballCooldown:F1}s remaining", new Color(0.9f, 0.4f, 0.1f));
				return;
			}
			FireballCooldown = FireballCooldownMax;

			SpawnFireblastEffect(position);
			SpawnTargetIndicator(position, new Color(0.9f, 0.3f, 0.1f));
			
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Cast: Fireball Spell", new Color(0.9f, 0.3f, 0.1f));
				UIManager.Instance.PlayClickSound();
			}

			DealSpellDamageAOE(position, 4.0f, 50f);
		}
		else if (spellId == "lightning")
		{
			if (LightningCooldown > 0)
			{
				InGameHUD.Instance?.ShowFeedbackText($"Lightning on cooldown: {LightningCooldown:F1}s remaining", new Color(0.2f, 0.6f, 1f));
				return;
			}
			LightningCooldown = LightningCooldownMax;

			SpawnLightningEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.5f, 1f));

			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Cast: Lightning Bolt", new Color(0.2f, 0.6f, 1f));
				UIManager.Instance.PlayClickSound();
			}

			DealSpellDamageAOE(position, 2.0f, 80f);
		}
		else if (spellId == "holylight")
		{
			if (HolyLightCooldown > 0)
			{
				InGameHUD.Instance?.ShowFeedbackText($"Holy Light on cooldown: {HolyLightCooldown:F1}s remaining", new Color(0.2f, 0.9f, 0.3f));
				return;
			}
			HolyLightCooldown = HolyLightCooldownMax;

			SpawnHolyLightEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.9f, 0.3f));

			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Cast: Holy Light", new Color(0.2f, 0.9f, 0.3f));
				UIManager.Instance.PlayClickSound();
			}

			HealAOE(position, 4.0f, 60f);
		}
	}

	public void BuyHealingPotion(Entity castleEntity)
	{
		float costGold = 50f;
		if (InGameHUD.Instance != null && InGameHUD.Instance.Gold >= costGold)
		{
			if (EcsWorld.Has<Unit3D>(castleEntity))
			{
				var castle3D = EcsWorld.Get<Unit3D>(castleEntity);
				Unit3D targetUnit = null;
				float closestDist = 20f;

				foreach (var unit in AllUnits)
				{
					if (!unit.IsEnemy && !unit.IsBuilding)
					{
						float dist = castle3D.GlobalPosition.DistanceTo(unit.GlobalPosition);
						if (dist < closestDist)
						{
							closestDist = dist;
							targetUnit = unit;
						}
					}
				}

				if (targetUnit == null && SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy && !SelectedUnits[0].IsBuilding)
				{
					targetUnit = SelectedUnits[0];
				}

				if (targetUnit != null && EcsWorld.Has<Inventory>(targetUnit.Entity))
				{
					InGameHUD.Instance.Gold -= costGold;
					var inv = EcsWorld.Get<Inventory>(targetUnit.Entity);
					EcsWorld.Set(targetUnit.Entity, new Inventory(inv.Potions + 1));

					InGameHUD.Instance.ShowFeedbackText($"Bought Healing Potion for {targetUnit.UnitId.ToUpper()}!", new Color(0.3f, 0.9f, 0.4f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
				else
				{
					InGameHUD.Instance.ShowFeedbackText("Cannot buy potion: No friendly combat units nearby!", new Color(1.0f, 0.2f, 0.2f));
					UIManager.Instance?.PlayWarningSound();
				}
			}
		}
		else
		{
			InGameHUD.Instance?.ShowFeedbackText("Cannot buy potion: Insufficient gold!", new Color(1.0f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public void UseHealingPotion(Unit3D unit)
	{
		if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Inventory>(unit.Entity))
		{
			var inv = EcsWorld.Get<Inventory>(unit.Entity);
			if (inv.Potions > 0)
			{
				var hp = EcsWorld.Get<Health>(unit.Entity);
				if (hp.Current >= hp.Max)
				{
					InGameHUD.Instance?.ShowFeedbackText("Unit is already at full health!", new Color(0.8f, 0.8f, 0.8f));
					UIManager.Instance?.PlayWarningSound();
					return;
				}

				EcsWorld.Set(unit.Entity, new Inventory(inv.Potions - 1));
				float newHp = Math.Min(hp.Max, hp.Current + 50f);
				EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

				InGameHUD.Instance?.ShowFeedbackText($"{unit.UnitId.ToUpper()} used Healing Potion (+50 HP)!", new Color(0.3f, 0.9f, 0.4f));
				SpawnHolyLightEffect(unit.GlobalPosition);
				FlashHealUnit(unit);

				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}

	public void EnterBuildingPlacement(string type)
	{
		_activeSpellTargeting = null;
		_activeCommandTargeting = null;
		ActivePingMode = false;

		_activeBuildingPlacementType = type;
		if (InGameHUD.Instance != null)
		{
			var meta = UnitRegistry[type];
			InGameHUD.Instance.ShowFeedbackText($"Place Building: Select Location for {meta.Name}", new Color(0.3f, 0.8f, 1.0f));
		}
	}

	public void CancelBuildingPlacement()
	{
		_activeBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Building Placement Cancelled", new Color(0.8f, 0.8f, 0.8f));
		}
	}

	private void ExecuteBuildingPlacement(string type, Vector3 position)
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var bldMeta = UnitRegistry[type];
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold -= bldMeta.CostGold;
				InGameHUD.Instance.Wood -= bldMeta.CostWood;
				InGameHUD.Instance.Stone -= bldMeta.CostStone;
				InGameHUD.Instance.ShowFeedbackText($"Constructing {bldMeta.Name}...", new Color(0.3f, 0.9f, 0.4f));
			}
			QueueClientCommand("build", new List<int>(), position, 0, type);
			_activeBuildingPlacementType = null;
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.QueueFree();
				_buildingPreviewMesh = null;
			}
			return;
		}

		var meta = UnitRegistry[type];
		if (InGameHUD.Instance != null)
		{
			float clearance = type == "castle" ? 7f : 4f;
			foreach (var unit in AllUnits)
			{
				if (GodotObject.IsInstanceValid(unit))
				{
					float dist = position.DistanceTo(unit.GlobalPosition);
					if (dist < clearance)
					{
						InGameHUD.Instance.ShowFeedbackText("Cannot construct: Area is obstructed!", new Color(1.0f, 0.2f, 0.2f));
						UIManager.Instance?.PlayWarningSound();
						_activeBuildingPlacementType = null;
						if (_buildingPreviewMesh != null)
						{
							_buildingPreviewMesh.QueueFree();
							_buildingPreviewMesh = null;
						}
						return;
					}
				}
			}

			if (InGameHUD.Instance.Gold >= meta.CostGold &&
				InGameHUD.Instance.Wood >= meta.CostWood &&
				InGameHUD.Instance.Stone >= meta.CostStone)
			{
				InGameHUD.Instance.Gold -= meta.CostGold;
				InGameHUD.Instance.Wood -= meta.CostWood;
				InGameHUD.Instance.Stone -= meta.CostStone;

				var playerOwner = _playerEntity.AsPlayerEntity(EcsWorld);
				string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(type, true);
				
				var bldEntity = CreateEcsUnit(type, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, position, playerOwner);
				SpawnUnit3D(bldEntity, type, modelPath, position, true, false);

				InGameHUD.Instance.ShowFeedbackText($"Constructed: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
				UIManager.Instance?.PlayClickSound();
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot construct: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}

		_activeBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}
	}

	public void CancelLastQueuedUnit(Entity castleEntity)
	{
		if (EcsWorld.IsAlive(castleEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
		{
			var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
			if (prod.UnitIds.Count > 0)
			{
				int lastIndex = prod.UnitIds.Count - 1;
				string cancelledId = prod.UnitIds[lastIndex];
				prod.UnitIds.RemoveAt(lastIndex);

				var meta = UnitRegistry[cancelledId];
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.Gold += meta.CostGold;
					InGameHUD.Instance.Wood += meta.CostWood;
					InGameHUD.Instance.Stone += meta.CostStone;

					CurrentPopulation = Math.Max(0, CurrentPopulation - meta.PopCost);
					InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
				}

				if (prod.UnitIds.Count == 0)
				{
					prod.CurrentProgress = 0f;
				}

				EcsWorld.Set(castleEntity, prod);

				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}

	public void SetRallyPoint(Unit3D building, Vector3 position)
	{
		if (EcsWorld.IsAlive(building.Entity))
		{
			var rp = new RallyPoint(new System.Numerics.Vector3(position.X, position.Y, position.Z));
			if (EcsWorld.Has<RallyPoint>(building.Entity))
			{
				EcsWorld.Set(building.Entity, rp);
			}
			else
			{
				EcsWorld.Add(building.Entity, rp);
			}

			SpawnTargetIndicator(position, new Color(1.0f, 0.82f, 0.55f)); 
			
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText($"Set Rally Point to: {position.X:F0}, {position.Z:F0}", new Color(1.0f, 0.85f, 0.5f));
			}

			building.UpdateRallyVisuals();
		}
	}

	public void DeselectUnit(Unit3D unit)
	{
		if (SelectedUnits.Contains(unit))
		{
			SelectedUnits.Remove(unit);
			unit.IsSelected = false;
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void SelectOnlyUnit(Unit3D unit)
	{
		ClearSelection();
		SelectUnit(unit);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void BuyWeaponsUpgrade()
	{
		if (HasWeaponsUpgrade) return;
		
		float costGold = 150f;
		float costWood = 100f;
		
		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Wood >= costWood)
			{
				InGameHUD.Instance.Gold -= costGold;
				InGameHUD.Instance.Wood -= costWood;
				
				HasWeaponsUpgrade = true;

				var query = new QueryDescription().WithAll<Attack, Owner>().WithNone<Dead, Building>();
				EcsWorld.Query(in query, (Entity entity, ref Attack atk, ref Owner owner) =>
				{
					if (owner.PlayerEntity == _playerEntity.AsPlayerEntity(EcsWorld))
					{
						atk.Damage += 3f;
					}
				});
				
				InGameHUD.Instance.ShowFeedbackText("Weapons Upgrade Complete! +3 Damage to all units.", new Color(0.2f, 0.8f, 1.0f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void BuyShieldsUpgrade()
	{
		if (HasShieldsUpgrade) return;
		
		float costGold = 150f;
		float costStone = 100f;
		
		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Stone >= costStone)
			{
				InGameHUD.Instance.Gold -= costGold;
				InGameHUD.Instance.Stone -= costStone;
				
				HasShieldsUpgrade = true;

				var query = new QueryDescription().WithAll<Armor, Owner>().WithNone<Dead>();
				EcsWorld.Query(in query, (Entity entity, ref Armor arm, ref Owner owner) =>
				{
					if (owner.PlayerEntity == _playerEntity.AsPlayerEntity(EcsWorld))
					{
						arm.Value += 2f;
					}
				});
				
				InGameHUD.Instance.ShowFeedbackText("Plated Armor Upgrade Complete! +2 Armor to all units.", new Color(0.2f, 0.8f, 1.0f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void BuyHarvestingUpgrade()
	{
		if (HasHarvestingUpgrade) return;

		float costWood = 150f;
		float costStone = 100f;

		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Wood >= costWood && InGameHUD.Instance.Stone >= costStone)
			{
				InGameHUD.Instance.Wood -= costWood;
				InGameHUD.Instance.Stone -= costStone;

				HasHarvestingUpgrade = true;
				InGameHUD.Instance.ResourceGatherMultiplier = 1.5f;

				InGameHUD.Instance.ShowFeedbackText("Harvesting Upgrade Complete! Passive resource accumulation +50%.", new Color(0.2f, 0.8f, 1.0f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void IssueAttackMoveCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.9f, 0.5f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Command: Attack-Move to position", new Color(0.9f, 0.5f, 0.1f));
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			ClearUnitOrders(unit.Entity);

			var attackMove = new Realm.Ecs.Components.Movement.AttackMove(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
			if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity))
				EcsWorld.Set(unit.Entity, attackMove);
			else
				EcsWorld.Add(unit.Entity, attackMove);

			var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
			if (EcsWorld.Has<MoveTo>(unit.Entity))
				EcsWorld.Set(unit.Entity, moveTo);
			else
				EcsWorld.Add(unit.Entity, moveTo);
		}
	}

	public void HoldSelectedUnits()
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);
				unit.Velocity = Vector3.Zero;
				if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity))
					EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity);
			}
			QueueClientCommand("hold", targetIds, Vector3.Zero, 0, "");
			return;
		}

		StopSelectedUnits();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity))
				EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity);
		}
	}

	public void IssuePatrolCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.6f, 0.3f, 1.0f));
		InGameHUD.Instance?.ShowFeedbackText("Command: Patrol Route Set", new Color(0.7f, 0.4f, 1.0f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			int clientUnitIndex = 0;
			int clientCols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
			float clientSpacing = 2.2f;

			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
				{
					int row = clientUnitIndex / clientCols;
					int col = clientUnitIndex % clientCols;
					float offsetX = (col - clientCols * 0.5f + 0.5f) * clientSpacing;
					float offsetZ = row * clientSpacing;

					var unitPos = unit.GlobalPosition;
					var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
					var patrolB = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

					var patrol = new Patrol(patrolA, patrolB);
					if (EcsWorld.Has<Patrol>(unit.Entity)) EcsWorld.Set(unit.Entity, patrol);
					else EcsWorld.Add(unit.Entity, patrol);

					var moveTo = new MoveTo(patrolB);
					if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
					else EcsWorld.Add(unit.Entity, moveTo);

					clientUnitIndex++;
				}
			}
			QueueClientCommand("patrol", targetIds, targetPos, 0, "");
			return;
		}

		int unitIndex = 0;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
		float spacing = 2.2f;

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			ClearUnitOrders(unit.Entity);

			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;

				var unitPos = unit.GlobalPosition;
				var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
				var patrolB = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var patrol = new Patrol(patrolA, patrolB);
				if (EcsWorld.Has<Patrol>(unit.Entity)) EcsWorld.Set(unit.Entity, patrol);
				else EcsWorld.Add(unit.Entity, patrol);

				var moveTo = new MoveTo(patrolB);
				if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
				else EcsWorld.Add(unit.Entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueMoveCommandQueued(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.2f, 0.7f, 1.0f));
		InGameHUD.Instance?.ShowFeedbackText("Command: Queued Move (Shift+Click)", new Color(0.2f, 0.7f, 1.0f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			int clientUnitIndex = 0;
			int clientCols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
			float clientSpacing = 2.2f;

			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity)) continue;

				targetIds.Add(GetServerEntityId(unit.Entity));

				bool alreadyMoving = EcsWorld.Has<MoveTo>(unit.Entity);
				if (!alreadyMoving)
				{
					ClearUnitOrders(unit.Entity);
				}

				int row = clientUnitIndex / clientCols;
				int col = clientUnitIndex % clientCols;
				float offsetX = (col - clientCols * 0.5f + 0.5f) * clientSpacing;
				float offsetZ = row * clientSpacing;
				Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var targetVec = new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z);

				if (alreadyMoving)
				{
					if (EcsWorld.Has<WaypointQueue>(unit.Entity))
					{
						var q = EcsWorld.Get<WaypointQueue>(unit.Entity);
						q.Add(targetVec);
						EcsWorld.Set(unit.Entity, q);
					}
					else
					{
						var q = new WaypointQueue(targetVec);
						EcsWorld.Add(unit.Entity, q);
					}
				}
				else
				{
					var moveTo = new MoveTo(targetVec);
					if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
					else EcsWorld.Add(unit.Entity, moveTo);
				}

				clientUnitIndex++;
			}
			QueueClientCommand("move_queued", targetIds, targetPos, 0, "");
			return;
		}

		int unitIndex = 0;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
		float spacing = 2.2f;

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity)) continue;

			bool alreadyMoving = EcsWorld.Has<MoveTo>(unit.Entity);
			if (!alreadyMoving)
			{
				ClearUnitOrders(unit.Entity);
			}

			int row = unitIndex / cols;
			int col = unitIndex % cols;
			float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
			float offsetZ = row * spacing;
			Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

			var targetVec = new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z);

			if (alreadyMoving)
			{
				if (EcsWorld.Has<WaypointQueue>(unit.Entity))
				{
					var q = EcsWorld.Get<WaypointQueue>(unit.Entity);
					q.Add(targetVec);
					EcsWorld.Set(unit.Entity, q);
				}
				else
				{
					var q = new WaypointQueue(targetVec);
					EcsWorld.Add(unit.Entity, q);
				}
			}
			else
			{
				var moveTo = new MoveTo(targetVec);
				if (EcsWorld.Has<MoveTo>(unit.Entity))
					EcsWorld.Set(unit.Entity, moveTo);
				else
					EcsWorld.Add(unit.Entity, moveTo);
			}

			unitIndex++;
		}
	}

	private bool IsPositionBlocked(Vector3 pos, float checkRadius, Node3D ignoreNode = null)
	{
		foreach (var unit in AllUnits)
		{
			if (unit == ignoreNode || !GodotObject.IsInstanceValid(unit)) continue;
			float distXZ = new Vector2(unit.GlobalPosition.X - pos.X, unit.GlobalPosition.Z - pos.Z).Length();
			float r1 = checkRadius;
			float r2 = unit.Scale.X * 1.2f;
			if (unit.UnitId == "castle") r2 = unit.Scale.X * 5.0f;
			else if (unit.UnitId == "tower") r2 = unit.Scale.X * 2.5f;
			if (distXZ < (r1 + r2) * 0.85f)
			{
				return true;
			}
		}

		foreach (var node in GetChildren())
		{
			if (node == ignoreNode || !GodotObject.IsInstanceValid(node)) continue;
			if (node is Prop3D prop)
			{
				float distXZ = new Vector2(prop.GlobalPosition.X - pos.X, prop.GlobalPosition.Z - pos.Z).Length();
				float r1 = checkRadius;
				float r2 = prop.Scale.X * 1.5f;
				if (prop.PropId == "goldmine") r2 = prop.Scale.X * 4.0f;
				if (distXZ < (r1 + r2) * 0.85f)
				{
					return true;
				}
			}
		}

		return false;
	}

	private float GetPlacementRadius(string placeId, float scale)
	{
		if (string.IsNullOrEmpty(placeId)) return 1.2f * scale;
		string lowerId = placeId.ToLower();
		float baseRadius = 1.2f;
		if (lowerId.Contains("castle")) baseRadius = 5.0f;
		else if (lowerId.Contains("tower")) baseRadius = 2.5f;
		else if (lowerId.Contains("goldmine")) baseRadius = 4.0f;
		else if (lowerId.Contains("logo") || lowerId.Contains("flag") || lowerId.Contains("rune")) baseRadius = 1.0f;
		return baseRadius * scale;
	}

	private Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f)
	{
		if (!IsPositionBlocked(startPos, checkRadius))
		{
			return startPos;
		}

		float stepDist = 1.0f;
		int numSteps = Mathf.CeilToInt(maxSearchDist / stepDist);

		for (int i = 1; i <= numSteps; i++)
		{
			float dist = i * stepDist;
			int numAngles = 8 + i * 4;
			for (int a = 0; a < numAngles; a++)
			{
				float angle = a * (Mathf.Pi * 2.0f) / numAngles;
				Vector3 testPos = new Vector3(
					startPos.X + dist * Mathf.Cos(angle),
					startPos.Y,
					startPos.Z + dist * Mathf.Sin(angle)
				);

				if (GroundTerrain != null)
				{
					float spacing = GroundTerrain.Spacing;
					int width = GroundTerrain.Width;
					int depth = GroundTerrain.Depth;
					float halfW = (width - 1) / 2.0f * spacing;
					float halfD = (depth - 1) / 2.0f * spacing;
					if (Mathf.Abs(testPos.X) > halfW || Mathf.Abs(testPos.Z) > halfD) continue;
				}

				testPos.Y = GetTerrainHeightAt(testPos);

				if (!IsPositionBlocked(testPos, checkRadius))
				{
					return testPos;
				}
			}
		}

		return null;
	}

	private void UpdateBuildingPreview()
	{
		if (_activeBuildingPlacementType == null)
		{
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.QueueFree();
				_buildingPreviewMesh = null;
			}
			return;
		}

		var mousePos = GetViewport().GetMousePosition();
		var hit = RaycastFromMouse(mousePos);
		if (hit != null && hit.ContainsKey("position"))
		{
			var hitPos = hit["position"].AsVector3();
			hitPos.Y = 0f;

			bool isClear = true;
			float clearance = _activeBuildingPlacementType == "castle" ? 7f : 4f;
			foreach (var unit in AllUnits)
			{
				if (GodotObject.IsInstanceValid(unit))
				{
					float dist = hitPos.DistanceTo(unit.GlobalPosition);
					if (dist < clearance)
					{
						isClear = false;
						break;
					}
				}
			}

			var meta = UnitRegistry[_activeBuildingPlacementType];
			bool hasResources = InGameHUD.Instance != null &&
				InGameHUD.Instance.Gold >= meta.CostGold &&
				InGameHUD.Instance.Wood >= meta.CostWood &&
				InGameHUD.Instance.Stone >= meta.CostStone;

			bool canPlace = isClear && hasResources;

			if (_buildingPreviewMesh == null)
			{
				_buildingPreviewMesh = new MeshInstance3D();
				var boxMesh = new BoxMesh();
				boxMesh.Size = _activeBuildingPlacementType == "castle" ? new Vector3(8f, 4f, 8f) : new Vector3(4f, 8f, 4f);
				_buildingPreviewMesh.Mesh = boxMesh;
				
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = canPlace ? new Color(0.1f, 0.8f, 0.2f, 0.4f) : new Color(0.9f, 0.1f, 0.1f, 0.4f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				_buildingPreviewMesh.MaterialOverride = mat;
				AddChild(_buildingPreviewMesh);
			}
			else
			{
				if (_buildingPreviewMesh.MaterialOverride is StandardMaterial3D mat)
				{
					mat.AlbedoColor = canPlace ? new Color(0.1f, 0.8f, 0.2f, 0.4f) : new Color(0.9f, 0.1f, 0.1f, 0.4f);
				}
			}

			_buildingPreviewMesh.GlobalPosition = hitPos;
		}
		else
		{
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.GlobalPosition = new Vector3(9999f, 9999f, 9999f);
			}
		}
	}

	public void TrainUnitAtCastle(string unitId)
	{
		var meta = UnitRegistry[unitId];
		if (InGameHUD.Instance == null) return;

		Unit3D targetCastle = null;
		Realm.Ecs.Components.Core.ProductionQueue targetProd = default;
		bool foundCastle = false;

		foreach (var unit in SelectedUnits)
		{
			if (!unit.IsEnemy && unit.UnitId == "castle" && EcsWorld.IsAlive(unit.Entity))
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity))
				{
					var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity);
					if (prod.UnitIds.Count < 5)
					{
						targetCastle = unit;
						targetProd = prod;
						foundCastle = true;
						break;
					}
				}
				else
				{
					targetCastle = unit;
					targetProd = new Realm.Ecs.Components.Core.ProductionQueue();
					targetProd.BuildTime = meta.ProductionTime;
					EcsWorld.Add(unit.Entity, targetProd);
					foundCastle = true;
					break;
				}
			}
		}

		if (!foundCastle)
		{
			bool hasCastleSelected = SelectedUnits.Exists(u => !u.IsEnemy && u.UnitId == "castle");
			if (hasCastleSelected)
			{
				InGameHUD.Instance.ShowFeedbackText("Training queue is full! (Max 5)", new Color(1f, 0.3f, 0.3f));
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot train unit: No Castle selected!", new Color(1f, 0.3f, 0.3f));
			}
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		if (meta.PopCost > 0 && CurrentPopulation + meta.PopCost > MaxPopulation)
		{
			InGameHUD.Instance.ShowFeedbackText($"Population cap reached! ({CurrentPopulation}/{MaxPopulation})", new Color(1f, 0.3f, 0.3f));
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		if (InGameHUD.Instance.Gold >= meta.CostGold && 
			InGameHUD.Instance.Wood >= meta.CostWood && 
			InGameHUD.Instance.Stone >= meta.CostStone)
		{
			InGameHUD.Instance.Gold -= meta.CostGold;
			InGameHUD.Instance.Wood -= meta.CostWood;
			InGameHUD.Instance.Stone -= meta.CostStone;

			CurrentPopulation += meta.PopCost;
			targetProd.UnitIds.Add(unitId);
			EcsWorld.Set(targetCastle.Entity, targetProd);

			InGameHUD.Instance.ShowFeedbackText($"Queued {meta.Name} ({CurrentPopulation}/{MaxPopulation} pop)", new Color(0.2f, 0.8f, 1f));
			UIManager.Instance?.PlayClickSound();
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
		else
		{
			InGameHUD.Instance.ShowFeedbackText("Cannot train unit: Insufficient resources!", new Color(1f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public Unit3D SpawnUnitFromProduction(string unitId, Vector3 position, bool isEnemy, Vector3? rallyPoint = null, bool isFromQueue = false)
	{
		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
		
		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(unitId, meta.Speed == 0f);

		string name = meta.Name;
		if (isEnemy)
		{
			if (unitId == "worker") name = "Orc Worker";
			else if (unitId == "soldier") name = "Orc Raider";
			else if (unitId == "archer") name = "Dark Archer";
			else if (unitId == "priest") name = "Orc Shaman";
			else if (unitId == "castle") name = "Orc Stronghold";
			else if (unitId == "tower") name = "Orc Totem Tower";
		}

		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, meta.Speed == 0f, isEnemy, isFromQueue);

		if (rallyPoint.HasValue && meta.Speed > 0f)
		{
			var rpVal = rallyPoint.Value;
			var moveTo = new MoveTo(new System.Numerics.Vector3(rpVal.X, rpVal.Y, rpVal.Z));
			EcsWorld.Add(entity, moveTo);
		}
		return unit3D;
	}

	public void UpgradeTower(Unit3D tower)
	{
		float costGold = 150f;
		float costStone = 100f;

		if (InGameHUD.Instance != null)
		{
			int currentLevel = 1;
			if (EcsWorld.Has<TowerUpgradeLevel>(tower.Entity))
			{
				currentLevel = EcsWorld.Get<TowerUpgradeLevel>(tower.Entity).Value;
			}
			
			if (currentLevel >= 3)
			{
				InGameHUD.Instance.ShowFeedbackText("Tower is already at maximum upgrade level (Level 3)!", new Color(1.0f, 0.3f, 0.3f));
				UIManager.Instance?.PlayWarningSound();
				return;
			}

			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Stone >= costStone)
			{
				InGameHUD.Instance.Gold -= costGold;
				InGameHUD.Instance.Stone -= costStone;

				if (EcsWorld.IsAlive(tower.Entity))
				{
					int newLevel = currentLevel + 1;
					EcsWorld.Set(tower.Entity, new TowerUpgradeLevel(newLevel));
					
					string baseName = "Spell Tower";
					if (EcsWorld.Has<Name>(tower.Entity))
					{
						var nameComp = EcsWorld.Get<Name>(tower.Entity);
						if (nameComp.Value.Contains("Orc")) baseName = "Orc Totem Tower";
					}
					EcsWorld.Set(tower.Entity, new Name($"{baseName} (Lvl {newLevel})"));

					if (EcsWorld.Has<Health>(tower.Entity))
					{
						var hp = EcsWorld.Get<Health>(tower.Entity);
						EcsWorld.Set(tower.Entity, new Health(hp.Current + 250f, hp.Max + 250f));
					}
					if (EcsWorld.Has<Armor>(tower.Entity))
					{
						var arm = EcsWorld.Get<Armor>(tower.Entity);
						EcsWorld.Set(tower.Entity, new Armor(arm.Value + 5f));
					}
					if (EcsWorld.Has<Attack>(tower.Entity))
					{
						var atk = EcsWorld.Get<Attack>(tower.Entity);
						EcsWorld.Set(tower.Entity, new Attack(atk.Damage + 10f, atk.Range, atk.Cooldown));
					}
					
					float newScale = 1.0f + newLevel * 0.2f;
					tower.Scale = new Vector3(newScale, newScale, newScale);
					SpawnTargetIndicator(tower.GlobalPosition, new Color(0.1f, 0.8f, 0.9f));
					
					InGameHUD.Instance.ShowFeedbackText($"Tower Upgraded to Level {newLevel}!", new Color(0.2f, 0.8f, 1.0f));
					UIManager.Instance?.PlayClickSound();
					
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void CycleCameraZoom()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera != null && camera is CameraControl camCtrl)
		{
			camCtrl.CycleZoom();
		}
	}

	public void StopSelectedUnits()
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);
				unit.Velocity = Vector3.Zero;
			}
			QueueClientCommand("stop", targetIds, Vector3.Zero, 0, "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsEnemy) continue;

			ClearUnitOrders(unit.Entity);

			unit.Velocity = Vector3.Zero;
		}
	}

	public void CancelQueuedUnitAt(Entity castleEntity, int index)
	{
		if (EcsWorld.IsAlive(castleEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
		{
			var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
			if (index >= 0 && index < prod.UnitIds.Count)
			{
				string cancelledId = prod.UnitIds[index];
				prod.UnitIds.RemoveAt(index);

				var meta = UnitRegistry[cancelledId];
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.Gold += meta.CostGold;
					InGameHUD.Instance.Wood += meta.CostWood;
					InGameHUD.Instance.Stone += meta.CostStone;

					CurrentPopulation = Math.Max(0, CurrentPopulation - meta.PopCost);
					InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
				}

				if (index == 0)
				{
					prod.CurrentProgress = 0f;
					if (prod.UnitIds.Count > 0)
					{
						string nextUnitId = prod.UnitIds[0];
						prod.BuildTime = UnitRegistry[nextUnitId].ProductionTime;
					}
				}

				EcsWorld.Set(castleEntity, prod);

				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}
}
