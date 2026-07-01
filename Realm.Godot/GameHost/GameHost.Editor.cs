using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;

public partial class GameHost
{
	public void ClearMapEntirely()
	{
		if (GroundTerrain == null) return;
		
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				DeleteNodeExternal(unit);
			}
		}
		SelectedUnits.Clear();
		AllUnits.Clear();
		AllProps.Clear();
		
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				prop.QueueFree();
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				decal.QueueFree();
			}
		}
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				GroundTerrain.Heights[x, z] = 0.0f;
				GroundTerrain.Colors[x, z] = new Color(0.2f, 0.6f, 0.2f);
			}
		}
		
		GroundTerrain.UpdateMeshAndPhysics(true, true);
		EditorHistoryManager.Clear();
		RebuildGridOverlayMeshExternal();
		
		EditorCameraBoundsLeft = -95.0f;
		EditorCameraBoundsRight = 95.0f;
		EditorCameraBoundsTop = -95.0f;
		EditorCameraBoundsBottom = 125.0f;
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		MapEditorHUD.Instance?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
	}

	private bool IsMouseOverUI()
	{
		if (GodotObject.IsInstanceValid(MapEditorHUD.Instance))
		{
			return MapEditorHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
		}
		var mousePos = GetViewport().GetMousePosition();
		var viewportSize = GetViewport().GetVisibleRect().Size;
		
		if (mousePos.Y < 75) return true;
		if (mousePos.Y > viewportSize.Y - 245) return true;
		if (mousePos.X < 225 || mousePos.X > viewportSize.X - 225) return true;
		
		return false;
	}

	private void ApplyContinuousTerrainEditing(Vector3 worldPos, float delta)
	{
		if (GroundTerrain == null) return;
		
		bool isHeights = ActiveEditorTool == EditorTool.Raise ||
						 ActiveEditorTool == EditorTool.Lower ||
						 ActiveEditorTool == EditorTool.Flatten ||
						 ActiveEditorTool == EditorTool.Smooth ||
						 ActiveEditorTool == EditorTool.Cliff ||
						 ActiveEditorTool == EditorTool.Noise;
						 
		bool isPaint = ActiveEditorTool == EditorTool.PaintGrass ||
					   ActiveEditorTool == EditorTool.PaintDirt ||
					   ActiveEditorTool == EditorTool.PaintRock ||
					   ActiveEditorTool == EditorTool.PaintSand;

		bool isPathing = ActiveEditorTool == EditorTool.PaintPathing;
					   
		if (!isHeights && !isPaint && !isPathing) return;
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		
		Color paintColor = EditorPaintColor;
		
		bool modified = false;

		int pathingMask = 0;
		bool pathingAdd = true;
		if (isPathing && MapEditorHUD.Instance != null)
		{
			pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
			pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
		}
		
		if (EditorBlockMode)
		{
			int cx = Mathf.Clamp((int)Math.Round(worldPos.X / spacing + (width - 1) / 2.0f), 0, width - 1);
			int cz = Mathf.Clamp((int)Math.Round(worldPos.Z / spacing + (depth - 1) / 2.0f), 0, depth - 1);
			int brushGridRadius = Mathf.Max(0, (int)Math.Round(EditorBrushRadius / spacing));
			
			if (isHeights)
			{
				float targetHeight = _hasBlockTargetHeight ? _activeBlockTargetHeight : 0.0f;
				for (int z = cz - brushGridRadius; z <= cz + brushGridRadius; z++)
				{
					for (int x = cx - brushGridRadius; x <= cx + brushGridRadius; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!EditorBrushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}
							
							if (inBounds)
							{
								if (ActiveEditorTool == EditorTool.Raise || 
									ActiveEditorTool == EditorTool.Lower || 
									ActiveEditorTool == EditorTool.Flatten || 
									ActiveEditorTool == EditorTool.Cliff)
								{
									GroundTerrain.Heights[x, z] = Mathf.Clamp(targetHeight, -10.0f, 50.0f);
									modified = true;
								}
								else if (ActiveEditorTool == EditorTool.Smooth)
								{
									float avg = 0f;
									int count = 0;
									for (int nz = -1; nz <= 1; nz++)
									{
										for (int nx = -1; nx <= 1; nx++)
										{
											int nxVal = x + nx;
											int nzVal = z + nz;
											if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
											{
												avg += GroundTerrain.Heights[nxVal, nzVal];
												count++;
											}
										}
									}
									avg /= count;
									float snappedAvg = Mathf.Round(avg / EditorBlockLevelHeight) * EditorBlockLevelHeight;
									GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], snappedAvg, EditorBrushStrength * delta * 2.0f), -10.0f, 50.0f);
									modified = true;
								}
								else if (ActiveEditorTool == EditorTool.Noise)
								{
									if (GD.Randf() < 0.15f * EditorBrushStrength * delta)
									{
										float direction = GD.Randf() > 0.5f ? 1.0f : -1.0f;
										GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] + direction * EditorBlockLevelHeight, -10.0f, 50.0f);
										modified = true;
									}
								}
							}
						}
					}
				}
				
				if (modified && ActiveEditorTool != EditorTool.Smooth && ActiveEditorTool != EditorTool.Flatten && ActiveEditorTool != EditorTool.Noise)
				{
					for (int z = cz - brushGridRadius - 1; z <= cz + brushGridRadius + 1; z++)
					{
						for (int x = cx - brushGridRadius - 1; x <= cx + brushGridRadius + 1; x++)
						{
							if (x >= 0 && x < width && z >= 0 && z < depth)
							{
								float h = GroundTerrain.Heights[x, z];
								float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
								float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
								float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
								float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
								
								float maxDiff = Mathf.Max(
									Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
									Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
								);
								
								if (maxDiff >= EditorBlockLevelHeight * 0.5f)
								{
									GroundTerrain.Colors[x, z] = EditorCliffPaintColor;
								}
								else
								{
									bool insideBrush = true;
									if (!EditorBrushIsSquare)
									{
										float dx = x - cx;
										float dz = z - cz;
										insideBrush = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
									}
									else
									{
										insideBrush = (x >= cx - brushGridRadius && x <= cx + brushGridRadius && z >= cz - brushGridRadius && z <= cz + brushGridRadius);
									}
									
									if (insideBrush)
									{
										GroundTerrain.Colors[x, z] = EditorPaintColor;
									}
								}
							}
						}
					}
				}
			}
			else if (isPaint)
			{
				for (int z = cz - brushGridRadius; z <= cz + brushGridRadius; z++)
				{
					for (int x = cx - brushGridRadius; x <= cx + brushGridRadius; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!EditorBrushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}
							
							if (inBounds)
							{
								float h = GroundTerrain.Heights[x, z];
								float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
								float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
								float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
								float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
								
								float maxDiff = Mathf.Max(
									Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
									Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
								);
								
								Color baseColor = (maxDiff >= EditorBlockLevelHeight * 0.5f) ? EditorCliffPaintColor : EditorPaintColor;
								float targetAlpha = baseColor.A;
								Color targetColor = new Color(baseColor.R, baseColor.G, baseColor.B, targetAlpha);
								GroundTerrain.Colors[x, z] = GroundTerrain.Colors[x, z].Lerp(targetColor, EditorBrushStrength * delta * 5.0f);
								modified = true;
							}
						}
					}
				}
			}
			else if (isPathing)
			{
				for (int z = cz - brushGridRadius; z <= cz + brushGridRadius; z++)
				{
					for (int x = cx - brushGridRadius; x <= cx + brushGridRadius; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!EditorBrushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}
							
							if (inBounds)
							{
								if (pathingAdd)
								{
									GroundTerrain.PathingCodes[x, z] |= pathingMask;
								}
								else
								{
									GroundTerrain.PathingCodes[x, z] &= ~pathingMask;
								}
								modified = true;
							}
						}
					}
				}
			}
			
			if (modified)
			{
				GroundTerrain.UpdateMeshAndPhysics(isHeights, false);
				if (isHeights)
				{
					AlignAllEntitiesToTerrain();
				}
				if (isPathing && PathingOverlayVisible)
				{
					RebuildPathingOverlay();
				}
				EditorHasUnsavedChanges = true;
			}
			return;
		}
		
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;
				
				float dist = 0.0f;
				bool inBounds = false;
				if (EditorBrushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= EditorBrushRadius && dz <= EditorBrushRadius;
					dist = Mathf.Max(dx, dz);
				}
				else
				{
					dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= EditorBrushRadius;
				}

				if (inBounds)
				{
					float falloff = 1.0f - (dist / EditorBrushRadius);
					falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);
					
					if (isHeights)
					{
						if (ActiveEditorTool == EditorTool.Raise)
						{
							GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] + EditorBrushStrength * falloff * delta, -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Lower)
						{
							GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] - EditorBrushStrength * falloff * delta, -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Flatten)
						{
							GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], EditorFlattenHeight, EditorBrushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Smooth)
						{
							float avg = 0f;
							int count = 0;
							for (int nz = -1; nz <= 1; nz++)
							{
								for (int nx = -1; nx <= 1; nx++)
								{
									int nxVal = x + nx;
									int nzVal = z + nz;
									if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
									{
										avg += GroundTerrain.Heights[nxVal, nzVal];
										count++;
									}
								}
							}
							avg /= count;
							GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], avg, EditorBrushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Cliff)
						{
							float targetHeight = _activeCliffHeight ?? 4.0f;
							GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], targetHeight, EditorBrushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Noise)
						{
							float noiseVal = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushStrength * falloff * delta * 2.0f;
							GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] + noiseVal, -10.0f, 50.0f);
						}
						modified = true;
					}
					else if (isPaint)
					{
						float h = GroundTerrain.Heights[x, z];
						float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
						float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
						float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
						float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
						
						float maxDiff = Mathf.Max(
							Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
							Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
						);
						
						Color baseColor = (maxDiff >= spacing * 0.5f) ? EditorCliffPaintColor : EditorPaintColor;
						float targetAlpha = baseColor.A;
						Color targetColor = new Color(baseColor.R, baseColor.G, baseColor.B, targetAlpha);
						GroundTerrain.Colors[x, z] = GroundTerrain.Colors[x, z].Lerp(targetColor, EditorBrushStrength * falloff * delta * 3.0f);
						modified = true;
					}
					else if (isPathing)
					{
						int cx = Mathf.Clamp((int)Math.Round(vx / spacing + (width - 1) / 2.0f), 0, width - 1);
						int cz = Mathf.Clamp((int)Math.Round(vz / spacing + (depth - 1) / 2.0f), 0, depth - 1);
						if (pathingAdd)
						{
							GroundTerrain.PathingCodes[cx, cz] |= pathingMask;
						}
						else
						{
							GroundTerrain.PathingCodes[cx, cz] &= ~pathingMask;
						}
						modified = true;
					}
				}
			}
		}
		
		if (modified)
		{
			GroundTerrain.UpdateMeshAndPhysics(isHeights, false);
			
			if (isHeights)
			{
				AlignAllEntitiesToTerrain();
			}
			if (isPathing && PathingOverlayVisible)
			{
				RebuildPathingOverlay();
			}
			EditorHasUnsavedChanges = true;
		}
	}

	public Prop3D SpawnPropExternal(string propId, Vector3 position)
	{
		float defaultAmount = propId switch
		{
			"goldmine" => 2000f,
			"rock" => 1000f,
			"tree" => 500f,
			_ => 0f
		};

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new PropIdentity(propId));
		if (defaultAmount > 0f)
		{
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, defaultAmount));
		}

		var prop = new Prop3D();
		prop.Entity = entity;
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);

		EcsWorld.Add(entity, prop);

		position.Y = GetTerrainHeightAt(position);
		prop.Position = position;
		
		if (IsMapEditorMode)
		{
			prop.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			prop.Scale *= EditorPlacementScale;
		}
		return prop;
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		if (decalId.StartsWith("res://") || decalId.Contains("/"))
		{
			return decalId;
		}
		string customPath = $"res://Assets/2d/Decals/{decalId}";
		if (ResourceLoader.Exists(customPath))
		{
			return customPath;
		}
		if (!decalId.Contains("."))
		{
			string customPathWithPng = $"res://Assets/2d/Decals/{decalId}.png";
			if (ResourceLoader.Exists(customPathWithPng))
			{
				return customPathWithPng;
			}
		}
		return decalId switch
		{
			"forest" => "res://Assets/UI/forest_path.png",
			"snowy" => "res://Assets/UI/snowy_forest_path.png",
			"flag" => "res://Assets/UI/alliance_flag.png",
			"rune" => "res://Assets/UI/magic_frame.png",
			_ => "res://icon.svg"
		};
	}

	public Decal SpawnDecalExternal(Vector3 position)
	{
		var decal = new Decal();
		decal.TextureAlbedo = GD.Load<Texture2D>("res://icon.svg");
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f);
		decal.SetMeta("DecalId", "logo");
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		
		position.Y = GetTerrainHeightAt(position);
		decal.Position = position;
		
		if (IsMapEditorMode)
		{
			decal.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
			decal.Scale = Vector3.One;
		}
		return decal;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		if (GroundTerrain == null) return 0.0f;
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		
		float fx = worldPos.X / spacing + (width - 1) / 2.0f;
		float fz = worldPos.Z / spacing + (depth - 1) / 2.0f;
		
		int x = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
		int z = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);
		
		return GroundTerrain.Heights[x, z];
	}

	private void AlignAllEntitiesToTerrain()
	{
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.GlobalPosition;
				pos.Y = GetTerrainHeightAt(pos);
				unit.GlobalPosition = pos;
				if (EcsWorld.IsAlive(unit.Entity))
				{
					EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
				}
			}
		}

		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.GlobalPosition;
				pos.Y = GetTerrainHeightAt(pos);
				prop.GlobalPosition = pos;
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				var pos = decal.GlobalPosition;
				pos.Y = GetTerrainHeightAt(pos);
				decal.GlobalPosition = pos;
			}
		}
	}

	private void DeleteObjectAt(Node collider, Vector3 hitPos)
	{
		var unit = FindUnit3DInParentChain(collider);
		if (unit != null)
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return;
		}
		
		Node current = collider;
		while (current != null && current != this)
		{
			if (current is Prop3D prop)
			{
				AllProps.Remove(prop);
				prop.QueueFree();
				return;
			}
			current = current.GetParent();
		}


		Decal closestDecal = null;
		float closestDist = 3.0f; // search radius in units
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
			closestDecal.QueueFree();
		}
	}

	private void ProcessMapEditorPhysics(float fDelta)
	{
		_fDelta = fDelta;
		var query = new QueryDescription().WithAll<Position, MoveTo, MovementStats>().WithNone<Dead>();
		_tickEditorArrivedUnits.Clear();
		EcsWorld.Query(in query, _editorMovementQueryDelegate);

		foreach (var entity in _tickEditorArrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}
	


	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale)
	{
		position.Y = GetTerrainHeightAt(position);
		if (!UnitRegistry.ContainsKey(unitId))
		{
			var dynamicMeta = new UnitMetadata
			{
				Name = System.IO.Path.GetFileNameWithoutExtension(unitId).Replace("_", " "),
				MaxHp = 100f,
				Damage = 10f,
				Range = 2f,
				Armor = 2f,
				Speed = 6.0f,
				ProductionTime = 10f,
				ModelPath = unitId.StartsWith("res://") ? unitId : $"res://Assets/3d/Characters/{unitId}"
			};
			if (unitId.Contains("Buildings") || unitId.Contains("castle") || unitId.Contains("tower"))
			{
				dynamicMeta.Speed = 0f;
				dynamicMeta.ModelPath = unitId.StartsWith("res://") ? unitId : $"res://Assets/3d/Buildings/{unitId}";
			}
			UnitRegistry[unitId] = dynamicMeta;
		}

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

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, meta.Speed == 0f, isEnemy);
		unit3D.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		unit3D.Scale = Vector3.One * scale;

		return unit3D;
	}

	public Prop3D SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
	{
		float defaultAmount = propId switch
		{
			"goldmine" => 2000f,
			"rock" => 1000f,
			"tree" => 500f,
			_ => 0f
		};

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new PropIdentity(propId));
		if (defaultAmount > 0f)
		{
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, defaultAmount));
		}

		var prop = new Prop3D();
		prop.Entity = entity;
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);

		EcsWorld.Add(entity, prop);

		position.Y = GetTerrainHeightAt(position);
		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * scale;
		
		return prop;
	}

	public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, float rotationY, float scale)
	{
		var decal = new Decal();
		decal.TextureAlbedo = GD.Load<Texture2D>(GetDecalTexturePath(decalId));
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * scale;
		decal.SetMeta("DecalId", string.IsNullOrEmpty(decalId) ? "logo" : decalId);
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		
		position.Y = GetTerrainHeightAt(position);
		decal.Position = position;
		decal.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		decal.Scale = Vector3.One;
		
		return decal;
	}

	public void DeleteNodeExternal(Node node)
	{
		if (_selectedEditorObject == node)
		{
			SelectedEditorObject = null;
		}
		if (node is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
		}
		else if (node is Prop3D prop && GodotObject.IsInstanceValid(prop))
		{
			AllProps.Remove(prop);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
			prop.QueueFree();
		}
		else if (node is Decal decal && GodotObject.IsInstanceValid(decal))
		{
			decal.QueueFree();
		}
	}

	public IEditorAction DeleteObjectAtWithUndo(Node collider, Vector3 hitPos)
	{
		if (collider == _selectedEditorObject)
		{
			SelectedEditorObject = null;
		}
		var unit = FindUnit3DInParentChain(collider);
		if (unit == null)
		{
			Unit3D closestUnit = null;
			float closestUnitDist = 2.0f;
			foreach (var u in AllUnits)
			{
				if (GodotObject.IsInstanceValid(u))
				{
					float d = u.Position.DistanceTo(hitPos);
					if (d < closestUnitDist)
					{
						closestUnitDist = d;
						closestUnit = u;
					}
				}
			}
			if (closestUnit != null)
			{
				unit = closestUnit;
			}
		}

		if (unit != null)
		{
			if (unit == _selectedEditorObject)
			{
				SelectedEditorObject = null;
			}
			var action = new ObjectDeleteAction("unit", unit.UnitId, unit.Position, unit.RotationDegrees.Y, unit.Scale.X, unit.IsEnemy, unit);
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return action;
		}
		
		Prop3D prop = null;
		Node current = collider;
		while (current != null && current != this)
		{
			if (current is Prop3D p)
			{
				prop = p;
				break;
			}
			current = current.GetParent();
		}

		if (prop == null)
		{
			Prop3D closestProp = null;
			float closestPropDist = 2.0f;
			foreach (var child in GetChildren())
			{
				if (child is Prop3D p && GodotObject.IsInstanceValid(p))
				{
					float d = p.Position.DistanceTo(hitPos);
					if (d < closestPropDist)
					{
						closestPropDist = d;
						closestProp = p;
					}
				}
			}
			if (closestProp != null)
			{
				prop = closestProp;
			}
		}

		if (prop != null)
		{
			if (prop == _selectedEditorObject)
			{
				SelectedEditorObject = null;
			}
			var action = new ObjectDeleteAction("prop", prop.PropId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
			AllProps.Remove(prop);
			prop.QueueFree();
			return action;
		}

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
			if (closestDecal == _selectedEditorObject)
			{
				SelectedEditorObject = null;
			}
			var action = new ObjectDeleteAction("decal", "", closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
			closestDecal.QueueFree();
			return action;
		}
		return null;
	}

	public void AlignAllEntitiesToTerrainExternal()
	{
		AlignAllEntitiesToTerrain();
	}

	private void UpdateEditorPreview(Vector3 position)
	{
		bool needsPreview = ActiveEditorTool == EditorTool.PlaceUnit ||
							ActiveEditorTool == EditorTool.PlaceProp ||
							ActiveEditorTool == EditorTool.PlaceDecal;

		if (!needsPreview)
		{
			ClearEditorPreview();
			return;
		}

		string reqType = ActiveEditorTool.ToString();
		string reqId = ActivePlaceId;
		bool reqIsEnemy = PlaceUnitIsEnemy;

		if (_editorPreviewNode == null || _editorPreviewType != reqType || _editorPreviewId != reqId || _editorPreviewIsEnemy != reqIsEnemy)
		{
			ClearEditorPreview();
			
			_editorPreviewType = reqType;
			_editorPreviewId = reqId;
			_editorPreviewIsEnemy = reqIsEnemy;

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				if (!UnitRegistry.ContainsKey(reqId))
				{
					var dynamicMeta = new UnitMetadata
					{
						Name = System.IO.Path.GetFileNameWithoutExtension(reqId).Replace("_", " "),
						MaxHp = 100f,
						Damage = 10f,
						Range = 2f,
						Armor = 2f,
						Speed = 6.0f,
						ProductionTime = 10f,
						ModelPath = reqId.StartsWith("res://") ? reqId : $"res://Assets/3d/Characters/{reqId}"
					};
					if (reqId.Contains("Buildings") || reqId.Contains("castle") || reqId.Contains("tower"))
					{
						dynamicMeta.Speed = 0f;
						dynamicMeta.ModelPath = reqId.StartsWith("res://") ? reqId : $"res://Assets/3d/Buildings/{reqId}";
					}
					UnitRegistry[reqId] = dynamicMeta;
				}

				if (UnitRegistry.TryGetValue(reqId, out var meta))
				{
 					string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(reqId, meta.Speed == 0f);

					var previewUnit = new Unit3D();
					previewUnit.UnitId = reqId;
					previewUnit.IsBuilding = meta.Speed == 0f;
					previewUnit.IsEnemy = reqIsEnemy;
					previewUnit.IsPreview = true;
					AddChild(previewUnit);
					previewUnit.LoadModel(modelPath);

					Color color = reqIsEnemy ? new Color(1.0f, 0.3f, 0.15f) : new Color(0.15f, 0.65f, 1.0f);
					MakeHologramRecursive(previewUnit, color);
					_editorPreviewNode = previewUnit;
				}
			}
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				var previewProp = new Prop3D();
				previewProp.PropId = reqId;
				previewProp.IsPreview = true;
				AddChild(previewProp);

				Color color = new Color(0.95f, 0.82f, 0.15f);
				MakeHologramRecursive(previewProp, color);
				_editorPreviewNode = previewProp;
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				var previewDecal = new Decal();
				previewDecal.TextureAlbedo = GD.Load<Texture2D>(GetDecalTexturePath(reqId));
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
				AddChild(previewDecal);
				previewDecal.SetMeta("DecalId", string.IsNullOrEmpty(reqId) ? "logo" : reqId);

				Color color = new Color(1.0f, 1.0f, 1.0f);
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				previewDecal.AlbedoMix = 0.5f;
				_editorPreviewNode = previewDecal;
			}
		}

		if (_editorPreviewNode != null)
		{
			if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
			float previewRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
			float previewScaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;

			Vector3 previewPos = position;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				float spacing = GroundTerrain.Spacing;
				int width = GroundTerrain.Width;
				int depth = GroundTerrain.Depth;
				float fx = Mathf.Round(previewPos.X / spacing + (width - 1) / 2.0f);
				previewPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
				float fz = Mathf.Round(previewPos.Z / spacing + (depth - 1) / 2.0f);
				previewPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
			}
			previewPos.Y = GetTerrainHeightAt(previewPos);
			if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp)
			{
				float radius = GetPlacementRadius(ActivePlaceId, previewScaleVal);
				var finalPos = FindNearestFreePosition(previewPos, radius);
				if (finalPos != null)
				{
					previewPos = finalPos.Value;
				}
			}
			_editorPreviewNode.Position = previewPos;
			_editorPreviewNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
			if (_editorPreviewNode is Decal previewDecal)
			{
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * previewScaleVal;
				previewDecal.Scale = Vector3.One;
			}
			else
			{
				_editorPreviewNode.Scale = Vector3.One * previewScaleVal;
			}
			_editorPreviewNode.Visible = true;
		}
	}

	private void ClearEditorPreview()
	{
		if (_editorPreviewNode != null)
		{
			_editorPreviewNode.QueueFree();
			_editorPreviewNode = null;
		}
		_editorPreviewType = "";
		_editorPreviewId = "";
		_editorPreviewIsEnemy = false;
	}

	private void MakeHologramRecursive(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(color.R, color.G, color.B) * 0.5f;
			meshInstance.MaterialOverride = mat;
		}
		foreach (var child in node.GetChildren())
		{
			MakeHologramRecursive(child, color);
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
		{
			var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
			EcsWorld.Set(unit.Entity, new Owner(playerOwner));
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
			{
				string name = meta.Name;
				if (isEnemy)
				{
					if (unit.UnitId == "worker") name = "Orc Worker";
					else if (unit.UnitId == "soldier") name = "Orc Raider";
					else if (unit.UnitId == "archer") name = "Dark Archer";
					else if (unit.UnitId == "priest") name = "Orc Shaman";
					else if (unit.UnitId == "castle") name = "Orc Stronghold";
					else if (unit.UnitId == "tower") name = "Orc Totem Tower";
				}
				EcsWorld.Set(unit.Entity, new Name(name));
			}
			unit.IsEnemy = isEnemy;
			if (unit.UnitId == "priest")
			{
				Color priestColor = isEnemy ? new Color(0.8f, 0.2f, 0.8f) : new Color(1.0f, 0.85f, 0.2f);
				unit.ApplyModelTint(priestColor);
			}
			else if (unit.UnitId == "worker")
			{
				Color workerColor = isEnemy ? new Color(0.6f, 0.4f, 0.2f) : new Color(0.8f, 0.6f, 0.4f);
				unit.ApplyModelTint(workerColor);
			}
			unit.IsSelected = unit.IsSelected;
		}
	}

	private void UpdateDecalSelectionRing(Decal decal, bool selected)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("EditorSelectionRing");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (selected)
		{
			var ring = new MeshInstance3D();
			ring.Name = "EditorSelectionRing";
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.1f, 0.7f, 0.95f);
			material.EmissionEnabled = true;
			material.Emission = new Color(0.1f, 0.7f, 0.95f);
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void UpdateDecalHoverRing(Decal decal, bool hovered)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("EditorHoverRing");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (hovered && SelectedEditorObject != decal)
		{
			var ring = new MeshInstance3D();
			ring.Name = "EditorHoverRing";
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.4f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.EmissionEnabled = true;
			material.Emission = new Color(1.0f, 1.0f, 1.0f) * 0.3f;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void ProcessMapEditorTick(float fDelta)
	{
		if (_clumpSpawnCooldown > 0.0f)
		{
			_clumpSpawnCooldown -= fDelta;
		}

		var mousePos = GetViewport().GetMousePosition();
		var hit = RaycastFromMouse(mousePos);
		if (hit != null && hit.ContainsKey("position"))
		{
			Vector3 hitPos = hit["position"].AsVector3();
			UpdateBrushIndicator(hitPos);
			UpdateEditorPreview(hitPos);
			if (GroundTerrain != null)
			{
				if (ActiveEditorTool == EditorTool.SelectArea && _isSelectingArea && _selectionStart != null)
				{
					float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
					float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
					int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
					int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
					_selectionEnd = new Vector2I(cx, cz);
					int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
					int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
					int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
					int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
					CreateSelectionHighlight();
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
				else if (ActiveEditorTool == EditorTool.PasteArea && _copiedArea != null)
				{
					float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
					float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
					int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
					int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
					int minX = cx;
					int minZ = cz;
					int maxX = Mathf.Min(minX + _copiedArea.Width - 1, GroundTerrain.Width - 1);
					int maxZ = Mathf.Min(minZ + _copiedArea.Depth - 1, GroundTerrain.Depth - 1);
					CreateSelectionHighlight();
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
			}
			
			bool canHover = (ActiveEditorTool == EditorTool.SelectMove && !_isDraggingObject) ||
							ActiveEditorTool == EditorTool.DeleteObject ||
							ActiveEditorTool == EditorTool.Eyedropper;
			Node newHovered = null;
			if (canHover && !IsMouseOverUI())
			{
				var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
				if (collider != null)
				{
					newHovered = FindUnit3DInParentChain(collider);
					if (newHovered == null)
					{
						newHovered = FindProp3DInParentChain(collider);
					}
				}
				if (newHovered == null)
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
						newHovered = closestDecal;
					}
				}
			}

			if (_hoveredEditorObject != newHovered)
			{
				if (GodotObject.IsInstanceValid(_hoveredEditorObject))
				{
					if (_hoveredEditorObject is Unit3D u) u.IsHovered = false;
					else if (_hoveredEditorObject is Prop3D p) p.IsHovered = false;
					else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, false);
				}
				_hoveredEditorObject = newHovered;
				if (GodotObject.IsInstanceValid(_hoveredEditorObject))
				{
					if (_hoveredEditorObject is Unit3D u) u.IsHovered = true;
					else if (_hoveredEditorObject is Prop3D p) p.IsHovered = true;
					else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, true);
				}
			}

			if (Input.IsMouseButtonPressed(MouseButton.Left) && !IsMouseOverUI())
			{
				if ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode)
				{
					if (!_isDrawingClump)
					{
						_isDrawingClump = true;
						_clumpSpawnActionsInSession.Clear();
					}
					if (_clumpSpawnCooldown <= 0.0f)
					{
						ApplyGeneralClumpSpawn(hitPos);
						_clumpSpawnCooldown = 0.15f;
					}
				}

				if (ActiveEditorTool == EditorTool.SelectMove && _isDraggingObject && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					if (!_dragObjectHasMoved && hitPos.DistanceTo(_dragObjectStartHitPos) > 0.3f)
					{
						_dragObjectHasMoved = true;
					}

					if (_dragObjectHasMoved)
					{
						var node3D = SelectedEditorObject as Node3D;
						var dragPos = hitPos - (_dragObjectStartHitPos - _dragObjectStartPos);
						if (EditorSnapToGrid && GroundTerrain != null)
						{
							float spacing = GroundTerrain.Spacing;
							int width = GroundTerrain.Width;
							int depth = GroundTerrain.Depth;
							float fx = Mathf.Round(dragPos.X / spacing + (width - 1) / 2.0f);
							dragPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
							float fz = Mathf.Round(dragPos.Z / spacing + (depth - 1) / 2.0f);
							dragPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
						}
						dragPos.Y = GetTerrainHeightAt(dragPos);
						node3D.Position = dragPos;
						if (SelectedEditorObject is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
						{
							EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
						}
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
					}
				}

				bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
									 ActiveEditorTool == EditorTool.Lower ||
									 ActiveEditorTool == EditorTool.Flatten ||
									 ActiveEditorTool == EditorTool.Smooth ||
									 ActiveEditorTool == EditorTool.Cliff ||
									 ActiveEditorTool == EditorTool.PaintGrass ||
									 ActiveEditorTool == EditorTool.PaintDirt ||
									 ActiveEditorTool == EditorTool.PaintRock ||
									 ActiveEditorTool == EditorTool.PaintSand ||
									 ActiveEditorTool == EditorTool.Noise ||
									 ActiveEditorTool == EditorTool.PaintPathing;

				if (isTerrainTool && !_isDrawingTerrain && GroundTerrain != null)
				{
					_isDrawingTerrain = true;
					_terrainHeightsBefore = (float[,])GroundTerrain.Heights.Clone();
					_terrainColorsBefore = (Color[,])GroundTerrain.Colors.Clone();
					_terrainPathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

					if (ActiveEditorTool == EditorTool.Flatten)
					{
						EditorFlattenHeight = GetMinHeightInBrushBounds(hitPos);
						MapEditorHUD.Instance?.UpdateFlattenHeightExternal(EditorFlattenHeight);
					}

					if (EditorBlockMode)
					{
						float startHeight = GetTerrainHeightAt(hitPos);
						if (ActiveEditorTool == EditorTool.Raise)
						{
							_activeBlockTargetHeight = (Mathf.Floor(startHeight / EditorBlockLevelHeight) + 1.0f) * EditorBlockLevelHeight;
							_hasBlockTargetHeight = true;
						}
						else if (ActiveEditorTool == EditorTool.Lower)
						{
							_activeBlockTargetHeight = (Mathf.Ceil(startHeight / EditorBlockLevelHeight) - 1.0f) * EditorBlockLevelHeight;
							_hasBlockTargetHeight = true;
						}
						else if (ActiveEditorTool == EditorTool.Flatten)
						{
							_activeBlockTargetHeight = Mathf.Round(EditorFlattenHeight / EditorBlockLevelHeight) * EditorBlockLevelHeight;
							_hasBlockTargetHeight = true;
						}
						else if (ActiveEditorTool == EditorTool.Cliff)
						{
							bool lower = Input.IsKeyPressed(Key.Shift);
							if (lower)
								_activeBlockTargetHeight = (Mathf.Ceil(startHeight / EditorBlockLevelHeight) - 1.0f) * EditorBlockLevelHeight;
							else
								_activeBlockTargetHeight = (Mathf.Floor(startHeight / EditorBlockLevelHeight) + 1.0f) * EditorBlockLevelHeight;
							_hasBlockTargetHeight = true;
						}
					}
				}

				if (ActiveEditorTool == EditorTool.Cliff && _activeCliffHeight == null && !EditorBlockMode)
				{
					float startHeight = GetTerrainHeightAt(hitPos);
					bool lower = Input.IsKeyPressed(Key.Shift);
					if (lower)
						_activeCliffHeight = (Mathf.Ceil(startHeight / 4.0f) - 1.0f) * 4.0f;
					else
						_activeCliffHeight = (Mathf.Floor(startHeight / 4.0f) + 1.0f) * 4.0f;
				}
				ApplyContinuousTerrainEditing(hitPos, fDelta);
				if (EditorMirrorMode != MirrorMode.None)
				{
					foreach (var t in GetMirroredTransforms(hitPos, 0.0f))
					{
						ApplyContinuousTerrainEditing(t.Position, fDelta);
					}
				}
			}
			else
			{
				if (_isDrawingClump)
				{
					_isDrawingClump = false;
					if (_clumpSpawnActionsInSession.Count > 0)
					{
						var composite = new CompositeAction(_clumpSpawnActionsInSession);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}
				}

				_activeCliffHeight = null;
				_hasBlockTargetHeight = false;
				if (_isDraggingObject)
				{
					_isDraggingObject = false;
					if (GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
						{
							var action = new ObjectTransformAction(
								node3D,
								_dragObjectStartPos, node3D.Position,
								_dragObjectStartRot, node3D.RotationDegrees,
								_dragObjectStartScale, node3D.Scale,
								_dragObjectStartIsEnemy, isEnemy
							);
							EditorHistoryManager.RecordAction(action);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Moved Object");
							EditorHasUnsavedChanges = true;
						}
					}
				}
				if (_isSelectingArea)
				{
					_isSelectingArea = false;
				}
				if (_isDrawingTerrain)
				{
					_isDrawingTerrain = false;
					if (GroundTerrain != null)
					{
						var currentHeights = (float[,])GroundTerrain.Heights.Clone();
						var currentColors = (Color[,])GroundTerrain.Colors.Clone();
						var currentPathing = (int[,])GroundTerrain.PathingCodes.Clone();
						var action = new TerrainModifyAction(_terrainHeightsBefore, currentHeights, _terrainColorsBefore, currentColors, _terrainPathingBefore, currentPathing);
						EditorHistoryManager.RecordAction(action);
						bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
											 ActiveEditorTool == EditorTool.Lower ||
											 ActiveEditorTool == EditorTool.Flatten ||
											 ActiveEditorTool == EditorTool.Smooth ||
											 ActiveEditorTool == EditorTool.Cliff ||
											 ActiveEditorTool == EditorTool.Noise ||
											 ActiveEditorTool == EditorTool.PaintPathing;
						if (isHeightsTool)
						{
							GroundTerrain.BakeNavMesh();
							RebuildGridOverlayMeshExternal();
						}
						EditorHasUnsavedChanges = true;
					}
				}
			}
		}
		else
		{
			if (_brushIndicatorMesh != null)
				_brushIndicatorMesh.Visible = false;
			ClearEditorPreview();
			if (_isDraggingObject)
			{
				_isDraggingObject = false;
				if (GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					var node3D = SelectedEditorObject as Node3D;
					bool isUnit = SelectedEditorObject is Unit3D;
					bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
					if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
					{
						var action = new ObjectTransformAction(
							node3D,
							_dragObjectStartPos, node3D.Position,
							_dragObjectStartRot, node3D.RotationDegrees,
							_dragObjectStartScale, node3D.Scale,
							_dragObjectStartIsEnemy, isEnemy
						);
						EditorHistoryManager.RecordAction(action);
						EditorHasUnsavedChanges = true;
					}
				}
			}
			if (_isDrawingClump)
			{
				_isDrawingClump = false;
				if (_clumpSpawnActionsInSession.Count > 0)
				{
					var composite = new CompositeAction(_clumpSpawnActionsInSession);
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}
			}
			if (_isSelectingArea)
			{
				_isSelectingArea = false;
			}
			if (_isDrawingTerrain)
			{
				_isDrawingTerrain = false;
				if (GroundTerrain != null)
				{
					var currentHeights = (float[,])GroundTerrain.Heights.Clone();
					var currentColors = (Color[,])GroundTerrain.Colors.Clone();
					var currentPathing = (int[,])GroundTerrain.PathingCodes.Clone();
					var action = new TerrainModifyAction(_terrainHeightsBefore, currentHeights, _terrainColorsBefore, currentColors, _terrainPathingBefore, currentPathing);
					EditorHistoryManager.RecordAction(action);
					bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
										 ActiveEditorTool == EditorTool.Lower ||
										 ActiveEditorTool == EditorTool.Flatten ||
										 ActiveEditorTool == EditorTool.Smooth ||
										 ActiveEditorTool == EditorTool.Cliff ||
										 ActiveEditorTool == EditorTool.Noise ||
										 ActiveEditorTool == EditorTool.PaintPathing;
					if (isHeightsTool)
					{
						GroundTerrain.BakeNavMesh();
						RebuildGridOverlayMeshExternal();
					}
					EditorHasUnsavedChanges = true;
				}
			}
		}
		
		ProcessMapEditorPhysics(fDelta);
	}

	public void StartMapEditorMode()
	{
		IsMapEditorMode = true;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		
		ClearAllUnits();
		
		var groundNode = GetNodeOrNull("Ground");
		if (groundNode != null)
		{
			groundNode.QueueFree();
			RemoveChild(groundNode);
		}
		
		var terrainNode = new EditableTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;

		bool loaded = LoadMapFromFile();
		
		CreateBrushIndicator();
		CreateGridOverlay();
		InitializeCameraBoundsOverlay();
		UpdateDayNightVisuals(0.5f);
	}

	public void ExitMapEditorMode()
	{
		IsMapEditorMode = false;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		ClearEditorPreview();
		
		if (_brushIndicatorMesh != null)
		{
			_brushIndicatorMesh.QueueFree();
			_brushIndicatorMesh = null;
		}
		
		if (_gridOverlayMesh != null)
		{
			_gridOverlayMesh.QueueFree();
			_gridOverlayMesh = null;
		}

		if (_cameraBoundsOverlayMesh != null)
		{
			_cameraBoundsOverlayMesh.QueueFree();
			_cameraBoundsOverlayMesh = null;
		}
		
		var groundNode = GetNodeOrNull("Ground");
		if (groundNode != null)
		{
			groundNode.QueueFree();
			RemoveChild(groundNode);
		}
		
		CreateGround();
		SpawnInitialEntities();
	}

	private void ClearAllUnits()
	{
		SelectedUnits.Clear();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();
		_castlesList.Clear();
		CurrentPopulation = 0;
		MaxPopulation = 0;

		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop)
			{
				prop.QueueFree();
			}
		}
		AllProps.Clear();
		
		if (EcsWorld != null)
		{
			EcsWorld.Dispose();
			EcsWorld = World.Create();
			SetupWorldEntityComponents();
			
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
			InitializePlayerResources(_playerEntity);
			SetupPlayerEntityComponents(_playerEntity);

			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
			InitializePlayerResources(_enemyPlayerEntity);
			SetupPlayerEntityComponents(_enemyPlayerEntity);
		}
	}

	private void CreateBrushIndicator()
	{
		if (_brushIndicatorMesh != null) return;
		
		_brushIndicatorMesh = new MeshInstance3D();
		_brushIndicatorMesh.Name = "BrushIndicator";
		
		var torus = new TorusMesh();
		torus.InnerRadius = 0.95f;
		torus.OuterRadius = 1.05f;
		_brushIndicatorMesh.Mesh = torus;
		
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.15f, 0.65f, 1.0f, 0.3f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(0.15f, 0.65f, 1.0f) * 0.5f;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_brushIndicatorMesh.MaterialOverride = mat;
		
		AddChild(_brushIndicatorMesh);
		_brushIndicatorMesh.Visible = false;
	}

	public void UpdateBrushMesh()
	{
		if (_brushIndicatorMesh == null) return;
		if (EditorBrushIsSquare)
		{
			var plane = new PlaneMesh();
			plane.Size = new Vector2(2.0f, 2.0f);
			_brushIndicatorMesh.Mesh = plane;
		}
		else
		{
			var torus = new TorusMesh();
			torus.InnerRadius = 0.95f;
			torus.OuterRadius = 1.05f;
			_brushIndicatorMesh.Mesh = torus;
		}
	}

	private void UpdateBrushIndicator(Vector3 position)
	{
		if (_brushIndicatorMesh == null) return;
		
		_brushIndicatorMesh.Position = new Vector3(position.X, position.Y + 0.1f, position.Z);
		_brushIndicatorMesh.Scale = new Vector3(EditorBrushRadius, 0.1f, EditorBrushRadius);
		
		bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
							 ActiveEditorTool == EditorTool.Lower ||
							 ActiveEditorTool == EditorTool.Flatten ||
							 ActiveEditorTool == EditorTool.Smooth ||
							 ActiveEditorTool == EditorTool.Cliff ||
							 ActiveEditorTool == EditorTool.PaintGrass ||
							 ActiveEditorTool == EditorTool.PaintDirt ||
							 ActiveEditorTool == EditorTool.PaintRock ||
							 ActiveEditorTool == EditorTool.PaintSand ||
							 ActiveEditorTool == EditorTool.Noise ||
							 ActiveEditorTool == EditorTool.Ramp ||
							 ActiveEditorTool == EditorTool.PlacePropClump;
							 
		_brushIndicatorMesh.Visible = isTerrainTool;
	}

	public void ClearRampStartPosExternal()
	{
		_rampStartPos = null;
	}

	public struct MirroredTransform
	{
		public Vector3 Position;
		public float Rotation;
	}

	public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
	{
		var list = new List<MirroredTransform>();
		if (EditorMirrorMode == MirrorMode.None) return list;
		if (EditorMirrorMode == MirrorMode.Horizontal || EditorMirrorMode == MirrorMode.Both)
		{
			list.Add(new MirroredTransform {
				Position = new Vector3(-pos.X, pos.Y, pos.Z),
				Rotation = 180.0f - rotation
			});
		}
		if (EditorMirrorMode == MirrorMode.Vertical || EditorMirrorMode == MirrorMode.Both)
		{
			list.Add(new MirroredTransform {
				Position = new Vector3(pos.X, pos.Y, -pos.Z),
				Rotation = -rotation
			});
		}
		if (EditorMirrorMode == MirrorMode.Both)
		{
			list.Add(new MirroredTransform {
				Position = new Vector3(-pos.X, pos.Y, -pos.Z),
				Rotation = rotation + 180.0f
			});
		}
		return list;
	}

	private Node FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
	{
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
				{
					float dist = new Vector2(n3d.GlobalPosition.X - position.X, n3d.GlobalPosition.Z - position.Z).Length();
					if (dist <= searchRadius)
					{
						return n3d;
					}
				}
			}
		}
		return null;
	}

	private bool ApplyRampInternal(Vector3 start, Vector3 end)
	{
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		bool modified = false;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;
				float ab_len_sqr = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
				if (ab_len_sqr > 0.0001f)
				{
					float t = ((vx - start.X) * (end.X - start.X) + (vz - start.Z) * (end.Z - start.Z)) / ab_len_sqr;
					t = Mathf.Clamp(t, 0.0f, 1.0f);
					float proj_x = start.X + t * (end.X - start.X);
					float proj_z = start.Z + t * (end.Z - start.Z);
					float dist = Mathf.Sqrt((vx - proj_x) * (vx - proj_x) + (vz - proj_z) * (vz - proj_z));
					if (dist <= EditorBrushRadius)
					{
						float targetHeight = Mathf.Lerp(start.Y, end.Y, t);
						float falloff = 1.0f - (dist / EditorBrushRadius);
						falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);
						GroundTerrain.Heights[x, z] = Mathf.Lerp(GroundTerrain.Heights[x, z], targetHeight, falloff);
						modified = true;
					}
				}
			}
		}
		if (modified)
		{
			float threshold = EditorBlockMode ? (EditorBlockLevelHeight * 0.5f) : (spacing * 0.5f);
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float vx = (x - (width - 1) / 2.0f) * spacing;
					float vz = (z - (depth - 1) / 2.0f) * spacing;
					float ab_len_sqr = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
					if (ab_len_sqr > 0.0001f)
					{
						float t = ((vx - start.X) * (end.X - start.X) + (vz - start.Z) * (end.Z - start.Z)) / ab_len_sqr;
						t = Mathf.Clamp(t, 0.0f, 1.0f);
						float proj_x = start.X + t * (end.X - start.X);
						float proj_z = start.Z + t * (end.Z - start.Z);
						float dist = Mathf.Sqrt((vx - proj_x) * (vx - proj_x) + (vz - proj_z) * (vz - proj_z));
						if (dist <= EditorBrushRadius)
						{
							float h = GroundTerrain.Heights[x, z];
							float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
							float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
							float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
							float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
							float maxDiff = Mathf.Max(
								Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
								Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
							);
							if (maxDiff >= threshold)
							{
								GroundTerrain.Colors[x, z] = EditorCliffPaintColor;
							}
							else
							{
								GroundTerrain.Colors[x, z] = EditorPaintColor;
							}
						}
					}
				}
			}
		}
		return modified;
	}

	private float GetMinHeightInBrushBounds(Vector3 worldPos)
	{
		if (GroundTerrain == null) return 0.0f;
		float spacing = GroundTerrain.Spacing;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;

				bool inBounds = false;
				if (EditorBrushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= EditorBrushRadius && dz <= EditorBrushRadius;
				}
				else
				{
					float dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= EditorBrushRadius;
				}

				if (inBounds)
				{
					float h = GroundTerrain.Heights[x, z];
					if (h < minHeight)
					{
						minHeight = h;
						foundAny = true;
					}
				}
			}
		}

		return foundAny ? minHeight : GetTerrainHeightAt(worldPos);
	}

	private void ApplyGeneralClumpSpawn(Vector3 centerPos)
	{
		if (string.IsNullOrEmpty(ActivePlaceId)) return;
		int spawnCount = Mathf.Max(1, (int)Math.Round(EditorClumpDensity));
		for (int i = 0; i < spawnCount; i++)
		{
			float dx = 0.0f;
			float dz = 0.0f;
			if (EditorBrushIsSquare)
			{
				dx = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushRadius;
				dz = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushRadius;
			}
			else
			{
				float r = Mathf.Sqrt((float)GD.Randf()) * EditorBrushRadius;
				float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
				dx = r * Mathf.Cos(theta);
				dz = r * Mathf.Sin(theta);
			}
			Vector3 spawnPos = new Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
			if (GroundTerrain != null)
			{
				float spacing = GroundTerrain.Spacing;
				int width = GroundTerrain.Width;
				int depth = GroundTerrain.Depth;
				float halfW = (width - 1) / 2.0f * spacing;
				float halfD = (depth - 1) / 2.0f * spacing;
				if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
			}
			spawnPos.Y = GetTerrainHeightAt(spawnPos);

			float scaleVal = EditorPlacementScale + (float)(GD.Randf() * 2.0 - 1.0) * EditorClumpScaleVar;
			scaleVal = Mathf.Clamp(scaleVal, 0.2f, 3.0f);

			float rotY = (EditorRandomRotation && !_isPastingObject) ? (float)(GD.Randf() * 360.0) : EditorPlacementRotation;
			if (EditorRandomScale && !_isPastingObject)
			{
				scaleVal = 0.2f + (float)(GD.Randf() * 2.8);
			}

			Node spawnedNode = null;
			string spawnType = "";
			bool isEnemy = false;

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				spawnType = "unit";
				isEnemy = PlaceUnitIsEnemy;
				spawnedNode = SpawnUnitExternal(ActivePlaceId, spawnPos, isEnemy, rotY, scaleVal);
			}
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				spawnType = "prop";
				spawnedNode = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, rotY, scaleVal);
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				spawnType = "decal";
				spawnedNode = SpawnDecalExternalWithParams(ActivePlaceId, spawnPos, rotY, scaleVal);
			}

			if (spawnedNode != null)
			{
				_clumpSpawnActionsInSession.Add(new ObjectSpawnAction(spawnType, ActivePlaceId, spawnPos, rotY, scaleVal, isEnemy, spawnedNode));
				if (EditorMirrorMode != MirrorMode.None)
				{
					foreach (var t in GetMirroredTransforms(spawnPos, rotY))
					{
						Vector3 mPos = t.Position;
						mPos.Y = GetTerrainHeightAt(mPos);
						Node mNode = null;
						if (ActiveEditorTool == EditorTool.PlaceUnit)
						{
							mNode = SpawnUnitExternal(ActivePlaceId, mPos, isEnemy, t.Rotation, scaleVal);
						}
						else if (ActiveEditorTool == EditorTool.PlaceProp)
						{
							mNode = SpawnPropExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
						}
						else if (ActiveEditorTool == EditorTool.PlaceDecal)
						{
							mNode = SpawnDecalExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
						}
						if (mNode != null)
						{
							_clumpSpawnActionsInSession.Add(new ObjectSpawnAction(spawnType, ActivePlaceId, mPos, t.Rotation, scaleVal, isEnemy, mNode));
						}
					}
				}
			}
		}
	}

	public void RebuildCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh == null || GroundTerrain == null) return;
		if (!EditorCameraBoundsVisible) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		float halfW = (width - 1) / 2.0f;
		float halfD = (depth - 1) / 2.0f;

		float minWorldX = -halfW * spacing;
		float maxWorldX = halfW * spacing;
		float minWorldZ = -halfD * spacing;
		float maxWorldZ = halfD * spacing;

		float left = Mathf.Clamp(EditorCameraBoundsLeft, minWorldX, maxWorldX);
		float right = Mathf.Clamp(EditorCameraBoundsRight, minWorldX, maxWorldX);
		float top = Mathf.Clamp(EditorCameraBoundsTop, minWorldZ, maxWorldZ);
		float bottom = Mathf.Clamp(EditorCameraBoundsBottom, minWorldZ, maxWorldZ);

		var linePoints = new List<Vector3>();

		float GetTerrainHeightAtCoord(float worldX, float worldZ)
		{
			float gridX = worldX / spacing + halfW;
			float gridZ = worldZ / spacing + halfD;
			int x0 = Mathf.Clamp((int)Mathf.Floor(gridX), 0, width - 1);
			int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
			int z0 = Mathf.Clamp((int)Mathf.Floor(gridZ), 0, depth - 1);
			int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);
			
			float tx = gridX - x0;
			float tz = gridZ - z0;
			
			float h00 = GroundTerrain.Heights[x0, z0];
			float h10 = GroundTerrain.Heights[x1, z0];
			float h01 = GroundTerrain.Heights[x0, z1];
			float h11 = GroundTerrain.Heights[x1, z1];
			
			float h0 = Mathf.Lerp(h00, h10, tx);
			float h1 = Mathf.Lerp(h01, h11, tx);
			return Mathf.Lerp(h0, h1, tz);
		}

		void AddSegmentedLine(float x1, float z1, float x2, float z2)
		{
			float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
			int segments = Mathf.Max(1, (int)Mathf.Ceil(dist / spacing));
			for (int i = 0; i < segments; i++)
			{
				float t1 = (float)i / segments;
				float t2 = (float)(i + 1) / segments;
				
				float lx1 = Mathf.Lerp(x1, x2, t1);
				float lz1 = Mathf.Lerp(z1, z2, t1);
				float lx2 = Mathf.Lerp(x1, x2, t2);
				float lz2 = Mathf.Lerp(z1, z2, t2);
				
				float y1 = GetTerrainHeightAtCoord(lx1, lz1) + 0.2f;
				float y2 = GetTerrainHeightAtCoord(lx2, lz2) + 0.2f;
				
				linePoints.Add(new Vector3(lx1, y1, lz1));
				linePoints.Add(new Vector3(lx2, y2, lz2));
			}
		}

		AddSegmentedLine(left, top, right, top);
		AddSegmentedLine(right, top, right, bottom);
		AddSegmentedLine(right, bottom, left, bottom);
		AddSegmentedLine(left, bottom, left, top);

		int totalVertices = linePoints.Count * 3;
		var vertices = new Vector3[totalVertices];
		var colors = new Color[totalVertices];
		int idx = 0;

		Color boundsColor = new Color(0.9f, 0.1f, 0.8f, 0.95f);

		for (int i = 0; i < linePoints.Count; i += 2)
		{
			Vector3 p1 = linePoints[i];
			Vector3 p2 = linePoints[i + 1];

			vertices[idx] = p1;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2;
			colors[idx] = boundsColor;
			idx++;

			Vector3 dir = (p2 - p1).Normalized();
			Vector3 ortho = new Vector3(-dir.Z, 0, dir.X) * 0.08f;

			vertices[idx] = p1 + ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 + ortho;
			colors[idx] = boundsColor;
			idx++;

			vertices[idx] = p1 - ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 - ortho;
			colors[idx] = boundsColor;
			idx++;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		_cameraBoundsOverlayMesh.Mesh = arrayMesh;
	}

	private void PerformCopyArea()
	{
		if (GroundTerrain == null || _selectionStart == null || _selectionEnd == null) return;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var heights = new float[selWidth, selDepth];
		var colors = new Color[selWidth, selDepth];
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				heights[sx, sz] = GroundTerrain.Heights[minX + sx, minZ + sz];
				colors[sx, sz] = GroundTerrain.Colors[minX + sx, minZ + sz];
			}
		}
		float minWorldX = (minX - (width - 1) / 2.0f) * spacing - spacing * 0.5f;
		float maxWorldX = (maxX - (width - 1) / 2.0f) * spacing + spacing * 0.5f;
		float minWorldZ = (minZ - (depth - 1) / 2.0f) * spacing - spacing * 0.5f;
		float maxWorldZ = (maxZ - (depth - 1) / 2.0f) * spacing + spacing * 0.5f;
		Vector3 origin = new Vector3((minX - (width - 1) / 2.0f) * spacing, 0.0f, (minZ - (depth - 1) / 2.0f) * spacing);
		var entities = new List<CopiedEntityInfo>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				Vector3 pos = n3d.Position;
				if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
				{
					if (n3d is Unit3D unit)
					{
						entities.Add(new CopiedEntityInfo {
							Type = "unit",
							Id = unit.UnitId,
							RelativePos = pos - origin,
							Rotation = unit.RotationDegrees.Y,
							Scale = unit.Scale.X,
							IsEnemy = unit.IsEnemy
						});
					}
					else if (n3d is Prop3D prop)
					{
						entities.Add(new CopiedEntityInfo {
							Type = "prop",
							Id = prop.PropId,
							RelativePos = pos - origin,
							Rotation = prop.RotationDegrees.Y,
							Scale = prop.Scale.X,
							IsEnemy = false
						});
					}
					else if (n3d is Decal decal)
					{
						string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
						entities.Add(new CopiedEntityInfo {
							Type = "decal",
							Id = decalId,
							RelativePos = pos - origin,
							Rotation = decal.RotationDegrees.Y,
							Scale = decal.Scale.X,
							IsEnemy = false
						});
					}
				}
			}
		}
		_copiedArea = new CopiedAreaTemplate {
			Width = selWidth,
			Depth = selDepth,
			Heights = heights,
			Colors = colors,
			Entities = entities
		};
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Area: {selWidth}x{selDepth} tiles, {entities.Count} entities");
	}

	private void InitializeCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh != null) return;

		_cameraBoundsOverlayMesh = new MeshInstance3D();
		_cameraBoundsOverlayMesh.Name = "CameraBoundsOverlay";

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.NoDepthTest = false;
		mat.VertexColorUseAsAlbedo = true;
		_cameraBoundsOverlayMesh.MaterialOverride = mat;

		AddChild(_cameraBoundsOverlayMesh);
		_cameraBoundsOverlayMesh.Visible = false;
	}

	private void RebuildPathingOverlay()
	{
		if (_pathingOverlayMesh == null || GroundTerrain == null) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		static Color GetLayerColor(int flag) => flag switch
		{
			EditableTerrain.PATHING_SHALLOW_WATER => new Color(0.2f, 0.6f, 1.0f, 0.55f),
			EditableTerrain.PATHING_DEEP_WATER    => new Color(0.0f, 0.15f, 0.7f, 0.55f),
			EditableTerrain.PATHING_FLYING        => new Color(0.85f, 0.85f, 0.0f, 0.55f),
			EditableTerrain.PATHING_GROUND        => new Color(0.2f, 0.85f, 0.2f, 0.55f),
			EditableTerrain.PATHING_UNPATHABLE    => new Color(0.9f, 0.1f, 0.1f, 0.55f),
			_ => new Color(0f, 0f, 0f, 0f)
		};

		var allFlags = new int[]
		{
			EditableTerrain.PATHING_SHALLOW_WATER,
			EditableTerrain.PATHING_DEEP_WATER,
			EditableTerrain.PATHING_FLYING,
			EditableTerrain.PATHING_GROUND,
			EditableTerrain.PATHING_UNPATHABLE
		};

		int cellWidth = width - 1;
		int cellDepth = depth - 1;
		int maxQuads = cellWidth * cellDepth;

		var verticesList = new List<Vector3>(maxQuads * 8);
		var colorsList = new List<Color>(maxQuads * 8);
		var indicesList = new List<int>(maxQuads * 12);

		for (int z = 0; z < cellDepth; z++)
		{
			for (int x = 0; x < cellWidth; x++)
			{
				int code = GroundTerrain.PathingCodes[x, z];
				if (code == 0)
				{
					continue;
				}

				var activeFlags = new List<int>();
				foreach (var flag in allFlags)
				{
					if ((code & flag) != 0)
					{
						activeFlags.Add(flag);
					}
				}

				if (activeFlags.Count == 0)
				{
					continue;
				}

				float lx0 = (x - (width - 1) / 2.0f) * spacing;
				float lz0 = (z - (depth - 1) / 2.0f) * spacing;
				float lx1 = lx0 + spacing;
				float lz1 = lz0 + spacing;

				float h00 = GroundTerrain.Heights[x,     z    ] + 0.06f;
				float h10 = GroundTerrain.Heights[x + 1, z    ] + 0.06f;
				float h01 = GroundTerrain.Heights[x,     z + 1] + 0.06f;
				float h11 = GroundTerrain.Heights[x + 1, z + 1] + 0.06f;

				if (activeFlags.Count == 1)
				{
					Color cellColor = GetLayerColor(activeFlags[0]);
					if (cellColor.A < 0.01f) continue;

					int baseV = verticesList.Count;
					verticesList.Add(new Vector3(lx0, h00, lz0));
					colorsList.Add(cellColor);
					verticesList.Add(new Vector3(lx1, h10, lz0));
					colorsList.Add(cellColor);
					verticesList.Add(new Vector3(lx1, h11, lz1));
					colorsList.Add(cellColor);
					verticesList.Add(new Vector3(lx0, h01, lz1));
					colorsList.Add(cellColor);

					indicesList.Add(baseV);
					indicesList.Add(baseV + 1);
					indicesList.Add(baseV + 2);
					indicesList.Add(baseV);
					indicesList.Add(baseV + 2);
					indicesList.Add(baseV + 3);
				}
				else
				{
					int S = 4;
					for (int sz = 0; sz < S; sz++)
					{
						for (int sx = 0; sx < S; sx++)
						{
							float tx0 = (float)sx / S;
							float tx1 = (float)(sx + 1) / S;
							float tz0 = (float)sz / S;
							float tz1 = (float)(sz + 1) / S;

							float h_sub00 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx0), Mathf.Lerp(h01, h11, tx0), tz0);
							float h_sub10 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx1), Mathf.Lerp(h01, h11, tx1), tz0);
							float h_sub11 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx1), Mathf.Lerp(h01, h11, tx1), tz1);
							float h_sub01 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx0), Mathf.Lerp(h01, h11, tx0), tz1);

							float subX0 = lx0 + sx * (spacing / S);
							float subX1 = lx0 + (sx + 1) * (spacing / S);
							float subZ0 = lz0 + sz * (spacing / S);
							float subZ1 = lz0 + (sz + 1) * (spacing / S);

							int flagIndex = (sx + sz) % activeFlags.Count;
							Color subColor = GetLayerColor(activeFlags[flagIndex]);
							if (subColor.A < 0.01f) continue;

							int baseV = verticesList.Count;
							verticesList.Add(new Vector3(subX0, h_sub00, subZ0));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX1, h_sub10, subZ0));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX1, h_sub11, subZ1));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX0, h_sub01, subZ1));
							colorsList.Add(subColor);

							indicesList.Add(baseV);
							indicesList.Add(baseV + 1);
							indicesList.Add(baseV + 2);
							indicesList.Add(baseV);
							indicesList.Add(baseV + 2);
							indicesList.Add(baseV + 3);
						}
					}
				}
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verticesList.ToArray();
		arrays[(int)Mesh.ArrayType.Color]  = colorsList.ToArray();
		arrays[(int)Mesh.ArrayType.Index]  = indicesList.ToArray();

		var arrayMesh = new ArrayMesh();
		if (indicesList.Count > 0)
		{
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		}
		_pathingOverlayMesh.Mesh = arrayMesh;
	}

	private void CreateGridOverlay()
	{
		if (_gridOverlayMesh != null) return;

		_gridOverlayMesh = new MeshInstance3D();
		_gridOverlayMesh.Name = "GridOverlay";

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.NoDepthTest = false;
		mat.VertexColorUseAsAlbedo = true;
		_gridOverlayMesh.MaterialOverride = mat;

		AddChild(_gridOverlayMesh);
		_gridOverlayMesh.Visible = false;
	}

	public void RebuildGridOverlayMeshExternal()
	{
		RebuildGridOverlayMesh();
	}

	private void RebuildGridOverlayMesh()
	{
		if (_gridOverlayMesh == null || GroundTerrain == null) return;
		if (!EditorGridVisible) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		int totalVertices = 0;
		for (int z = 0; z < depth; z++)
		{
			bool isThick = (z % 10 == 0);
			totalVertices += (width - 1) * (isThick ? 6 : 2);
		}
		for (int x = 0; x < width; x++)
		{
			bool isThick = (x % 10 == 0);
			totalVertices += (depth - 1) * (isThick ? 6 : 2);
		}

		var vertices = new Vector3[totalVertices];
		var colors = new Color[totalVertices];
		int idx = 0;

		Color thickColor = new Color(1.0f, 0.9f, 0.0f, 0.85f);
		Color thinColor = new Color(1.0f, 0.9f, 0.0f, 0.25f);

		void AddLine(Vector3 p1, Vector3 p2, Color color, bool thick, bool isVertical)
		{
			vertices[idx] = p1;
			colors[idx] = color;
			idx++;
			vertices[idx] = p2;
			colors[idx] = color;
			idx++;

			if (thick)
			{
				float offset = 0.04f;
				Vector3 o = isVertical ? new Vector3(offset, 0, 0) : new Vector3(0, 0, offset);
				
				vertices[idx] = p1 + o;
				colors[idx] = color;
				idx++;
				vertices[idx] = p2 + o;
				colors[idx] = color;
				idx++;

				vertices[idx] = p1 - o;
				colors[idx] = color;
				idx++;
				vertices[idx] = p2 - o;
				colors[idx] = color;
				idx++;
			}
		}

		for (int z = 0; z < depth; z++)
		{
			bool isThick = (z % 10 == 0);
			Color col = isThick ? thickColor : thinColor;
			float lz = (z - (depth - 1) / 2.0f) * spacing;
			for (int x = 0; x < width - 1; x++)
			{
				float lx1 = (x - (width - 1) / 2.0f) * spacing;
				float lx2 = (x + 1 - (width - 1) / 2.0f) * spacing;

				float y1 = GroundTerrain.Heights[x, z] + 0.15f;
				float y2 = GroundTerrain.Heights[x + 1, z] + 0.15f;

				AddLine(new Vector3(lx1, y1, lz), new Vector3(lx2, y2, lz), col, isThick, false);
			}
		}

		for (int x = 0; x < width; x++)
		{
			bool isThick = (x % 10 == 0);
			Color col = isThick ? thickColor : thinColor;
			float lx = (x - (width - 1) / 2.0f) * spacing;
			for (int z = 0; z < depth - 1; z++)
			{
				float lz1 = (z - (depth - 1) / 2.0f) * spacing;
				float lz2 = (z + 1 - (depth - 1) / 2.0f) * spacing;

				float y1 = GroundTerrain.Heights[x, z] + 0.15f;
				float y2 = GroundTerrain.Heights[x, z + 1] + 0.15f;

				AddLine(new Vector3(lx, y1, lz1), new Vector3(lx, y2, lz2), col, isThick, true);
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		_gridOverlayMesh.Mesh = arrayMesh;
	}

	public void UpdateGridOverlayVisibility()
	{
		if (_gridOverlayMesh == null) return;
		_gridOverlayMesh.Visible = IsMapEditorMode && EditorGridVisible;
		if (_gridOverlayMesh.Visible)
		{
			RebuildGridOverlayMesh();
		}
	}

	public void PerformFloodFill(Vector3 clickPos, Color fillColor)
	{
		if (GroundTerrain == null) return;
		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		var visited = new bool[width, depth];
		void DoSingleFill(Vector3 pos)
		{
			float fx = pos.X / spacing + (width - 1) / 2.0f;
			float fz = pos.Z / spacing + (depth - 1) / 2.0f;
			int startX = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
			int startZ = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);
			Color startColor = colorsBefore[startX, startZ];
			if (startColor == fillColor) return;
			var queue = new Queue<(int x, int z)>();
			if (!visited[startX, startZ])
			{
				queue.Enqueue((startX, startZ));
				visited[startX, startZ] = true;
			}
			while (queue.Count > 0)
			{
				var (currX, currZ) = queue.Dequeue();
				float targetAlpha = fillColor.A;
				GroundTerrain.Colors[currX, currZ] = new Color(fillColor.R, fillColor.G, fillColor.B, targetAlpha);
				int[] dx = { 0, 0, -1, 1 };
				int[] dz = { -1, 1, 0, 0 };
				for (int i = 0; i < 4; i++)
				{
					int nextX = currX + dx[i];
					int nextZ = currZ + dz[i];
					if (nextX >= 0 && nextX < width && nextZ >= 0 && nextZ < depth)
					{
						if (!visited[nextX, nextZ])
						{
							if (colorsBefore[nextX, nextZ] != startColor)
							{
								continue;
							}
							float hCurrent = GroundTerrain.Heights[currX, currZ];
							float hNext = GroundTerrain.Heights[nextX, nextZ];
							if (Mathf.Abs(hNext - hCurrent) >= 1.0f)
							{
								continue;
							}
							visited[nextX, nextZ] = true;
							queue.Enqueue((nextX, nextZ));
						}
					}
				}
			}
		}
		DoSingleFill(clickPos);
		if (EditorMirrorMode != MirrorMode.None)
		{
			foreach (var t in GetMirroredTransforms(clickPos, 0.0f))
			{
				DoSingleFill(t.Position);
			}
		}
		GroundTerrain.UpdateMeshAndPhysics(false, false);
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var action = new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled terrain area");
	}

	private void CreateSelectionHighlight()
	{
		if (_selectionHighlightMesh != null) return;
		_selectionHighlightMesh = new MeshInstance3D();
		_selectionHighlightMesh.Name = "SelectionHighlight";
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_selectionHighlightMesh.MaterialOverride = mat;
		AddChild(_selectionHighlightMesh);
		_selectionHighlightMesh.Visible = false;
	}

	private void RebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
	{
		if (_selectionHighlightMesh == null || GroundTerrain == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth <= 0 || selDepth <= 0)
		{
			_selectionHighlightMesh.Visible = false;
			return;
		}
		int vertexCount = selWidth * selDepth;
		var vertices = new Vector3[vertexCount];
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int mapX = minX + sx;
				int mapZ = minZ + sz;
				int idx = sz * selWidth + sx;
				float lx = (mapX - (width - 1) / 2.0f) * spacing;
				float lz = (mapZ - (depth - 1) / 2.0f) * spacing;
				vertices[idx] = new Vector3(lx, GroundTerrain.Heights[mapX, mapZ] + 0.05f, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		int iIdx = 0;
		for (int sz = 0; sz < cellDepth; sz++)
		{
			for (int sx = 0; sx < cellWidth; sx++)
			{
				int v00 = sz * selWidth + sx;
				int v10 = sz * selWidth + (sx + 1);
				int v01 = (sz + 1) * selWidth + sx;
				int v11 = (sz + 1) * selWidth + (sx + 1);
				indices[iIdx++] = v00;
				indices[iIdx++] = v10;
				indices[iIdx++] = v01;
				indices[iIdx++] = v10;
				indices[iIdx++] = v11;
				indices[iIdx++] = v01;
			}
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_selectionHighlightMesh.Mesh = arrayMesh;
		_selectionHighlightMesh.Visible = true;
	}

	public void PerformEraseAreaExternal()
	{
		PerformEraseArea();
	}

	public void PerformCutAreaExternal()
	{
		if (GroundTerrain == null || _selectionStart == null || _selectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
			return;
		}

		PerformCopyArea();
		PerformEraseArea();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Cut");
	}

	private void PerformEraseArea()
	{
		if (GroundTerrain == null || _selectionStart == null || _selectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Erase (select an area first)");
			return;
		}

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		bool terrainModified = false;

		if (PasteOptionHeights || PasteOptionTextures)
		{
			for (int sz = 0; sz < selDepth; sz++)
			{
				for (int sx = 0; sx < selWidth; sx++)
				{
					int targetX = minX + sx;
					int targetZ = minZ + sz;
					if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
					{
						if (PasteOptionHeights) GroundTerrain.Heights[targetX, targetZ] = 0.0f;
						if (PasteOptionTextures) GroundTerrain.Colors[targetX, targetZ] = new Color(0.2f, 0.45f, 0.15f);
						terrainModified = true;
					}
				}
			}
		}

		if (terrainModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(PasteOptionHeights, false);
			if (PasteOptionHeights)
			{
				AlignAllEntitiesToTerrain();
			}
		}

		var deleteActions = new List<IEditorAction>();
		if (PasteOptionEntities)
		{
			float minWorldX = (minX - (width - 1) / 2.0f) * spacing - spacing * 0.5f;
			float maxWorldX = (maxX - (width - 1) / 2.0f) * spacing + spacing * 0.5f;
			float minWorldZ = (minZ - (depth - 1) / 2.0f) * spacing - spacing * 0.5f;
			float maxWorldZ = (maxZ - (depth - 1) / 2.0f) * spacing + spacing * 0.5f;

			var toDelete = new List<Node3D>();
			foreach (var child in GetChildren())
			{
				if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d) && n3d != _editorPreviewNode)
				{
					Vector3 pos = n3d.Position;
					if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
					{
						if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
						{
							toDelete.Add(n3d);
						}
					}
				}
			}

			foreach (var node in toDelete)
			{
				var act = DeleteObjectAtWithUndo(node, node.Position);
				if (act != null) deleteActions.Add(act);
			}
		}

		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var actions = new List<IEditorAction>();
		if (terrainModified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter));
		}
		if (deleteActions.Count > 0)
		{
			actions.AddRange(deleteActions);
		}

		if (actions.Count > 0)
		{
			var composite = new CompositeAction(actions);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Area Erased");
		}
	}

	private void PerformPasteArea(int startX, int startZ)
	{
		if (GroundTerrain == null || _copiedArea == null) return;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		bool modified = false;
		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;

		void PasteCell(int sx, int sz)
		{
			int targetX = startX + sx;
			int targetZ = startZ + sz;

			if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
			{
				if (PasteOptionHeights) GroundTerrain.Heights[targetX, targetZ] = _copiedArea.Heights[sx, sz];
				if (PasteOptionTextures) GroundTerrain.Colors[targetX, targetZ] = _copiedArea.Colors[sx, sz];
				modified = true;
			}

			if (EditorMirrorMode == MirrorMode.Horizontal || EditorMirrorMode == MirrorMode.Both)
			{
				int mx = width - 1 - targetX;
				int mz = targetZ;
				if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
				{
					if (PasteOptionHeights) GroundTerrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
					if (PasteOptionTextures) GroundTerrain.Colors[mx, mz] = _copiedArea.Colors[sx, sz];
					modified = true;
				}
			}

			if (EditorMirrorMode == MirrorMode.Vertical || EditorMirrorMode == MirrorMode.Both)
			{
				int mx = targetX;
				int mz = depth - 1 - targetZ;
				if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
				{
					if (PasteOptionHeights) GroundTerrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
					if (PasteOptionTextures) GroundTerrain.Colors[mx, mz] = _copiedArea.Colors[sx, sz];
					modified = true;
				}
			}

			if (EditorMirrorMode == MirrorMode.Both)
			{
				int mx = width - 1 - targetX;
				int mz = depth - 1 - targetZ;
				if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
				{
					if (PasteOptionHeights) GroundTerrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
					if (PasteOptionTextures) GroundTerrain.Colors[mx, mz] = _copiedArea.Colors[sx, sz];
					modified = true;
				}
			}
		}

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				PasteCell(sx, sz);
			}
		}

		if (modified)
		{
			GroundTerrain.UpdateMeshAndPhysics(PasteOptionHeights, false);
			if (PasteOptionHeights)
			{
				AlignAllEntitiesToTerrain();
			}
		}

		var spawnActions = new List<IEditorAction>();
		void SpawnAndRecord(CopiedEntityInfo ent, Vector3 pos, float rotation)
		{
			Node pastedNode = null;
			if (ent.Type == "unit")
			{
				pastedNode = SpawnUnitExternal(ent.Id, pos, ent.IsEnemy, rotation, ent.Scale);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction("unit", ent.Id, pos, rotation, ent.Scale, ent.IsEnemy, pastedNode));
				}
			}
			else if (ent.Type == "prop")
			{
				pastedNode = SpawnPropExternalWithParams(ent.Id, pos, rotation, ent.Scale);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction("prop", ent.Id, pos, rotation, ent.Scale, false, pastedNode));
				}
			}
			else if (ent.Type == "decal")
			{
				pastedNode = SpawnDecalExternalWithParams(ent.Id, pos, rotation, ent.Scale);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction("decal", ent.Id, pos, rotation, ent.Scale, false, pastedNode));
				}
			}
		}

		if (PasteOptionEntities)
		{
			Vector3 origin = new Vector3((startX - (width - 1) / 2.0f) * spacing, 0.0f, (startZ - (depth - 1) / 2.0f) * spacing);
			foreach (var ent in _copiedArea.Entities)
			{
				Vector3 destPos = origin + ent.RelativePos;
				destPos.Y = GetTerrainHeightAt(destPos);
				
				SpawnAndRecord(ent, destPos, ent.Rotation);

				if (EditorMirrorMode == MirrorMode.Horizontal || EditorMirrorMode == MirrorMode.Both)
				{
					Vector3 mPos = new Vector3(-destPos.X, destPos.Y, destPos.Z);
					mPos.Y = GetTerrainHeightAt(mPos);
					SpawnAndRecord(ent, mPos, 180.0f - ent.Rotation);
				}
				if (EditorMirrorMode == MirrorMode.Vertical || EditorMirrorMode == MirrorMode.Both)
				{
					Vector3 mPos = new Vector3(destPos.X, destPos.Y, -destPos.Z);
					mPos.Y = GetTerrainHeightAt(mPos);
					SpawnAndRecord(ent, mPos, -ent.Rotation);
				}
				if (EditorMirrorMode == MirrorMode.Both)
				{
					Vector3 mPos = new Vector3(-destPos.X, destPos.Y, -destPos.Z);
					mPos.Y = GetTerrainHeightAt(mPos);
					SpawnAndRecord(ent, mPos, ent.Rotation + 180.0f);
				}
			}
		}
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var actions = new List<IEditorAction>();
		if (modified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter));
		}
		if (spawnActions.Count > 0)
		{
			actions.AddRange(spawnActions);
		}
		if (actions.Count > 0)
		{
			var composite = new CompositeAction(actions);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted Area");
		}
	}

	public void UpdateCameraBoundsOverlayVisibility()
	{
		if (_cameraBoundsOverlayMesh == null) return;
		_cameraBoundsOverlayMesh.Visible = IsMapEditorMode && EditorCameraBoundsVisible;
		if (_cameraBoundsOverlayMesh.Visible)
		{
			RebuildCameraBoundsOverlay();
		}
	}

	public void UpdatePathingOverlay()
	{
		if (_pathingOverlayMesh == null)
		{
			_pathingOverlayMesh = new MeshInstance3D();
			_pathingOverlayMesh.Name = "PathingOverlay";

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.NoDepthTest = false;
			mat.VertexColorUseAsAlbedo = true;
			_pathingOverlayMesh.MaterialOverride = mat;
			_pathingOverlayMesh.Position = new Vector3(0f, 0.05f, 0f);
			AddChild(_pathingOverlayMesh);
		}

		bool shouldBeVisible = IsMapEditorMode && PathingOverlayVisible && ActiveEditorTool == EditorTool.PaintPathing;
		_pathingOverlayMesh.Visible = shouldBeVisible;

		if (shouldBeVisible && GroundTerrain != null)
		{
			RebuildPathingOverlay();
		}
	}
}
