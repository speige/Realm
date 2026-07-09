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
		ClearAllBuildQueueGhosts();
		AllProps.Clear();
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		
		var childrenCopy = new List<Node>(GetChildren());
		foreach (var child in childrenCopy)
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				DeleteNodeExternal(prop);
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				DeleteNodeExternal(decal);
			}
		}
		
		if (GroundTerrain != null && GroundTerrain.Heights != null && GroundTerrain.SplatMap != null)
		{
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					GroundTerrain.Heights[x, z] = 0.0f;
					GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(3);
				}
			}
			GroundTerrain.UpdateMeshAndPhysics(true, true);
		}
		EditorHistoryManager.Clear();
		RebuildGridOverlayMeshExternal();
		
		EditorCameraBoundsLeft = -95.0f;
		EditorCameraBoundsRight = 95.0f;
		EditorCameraBoundsTop = -95.0f;
		EditorCameraBoundsBottom = 125.0f;
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		EditorCoordinates.Clear();
		RebuildAllCoordinatePersistentMeshes();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();

		MapEditorHUD.Instance?.ClearTempWorkspaceExternal();
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

		int pathingMask = 0;
		bool pathingAdd = true;
		if (ActiveEditorTool == EditorTool.PaintPathing && MapEditorHUD.Instance != null)
		{
			pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
			pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
		}

		var result = _editorService.ApplyContinuousTerrainEditing(
			worldPos, delta,
			ActiveEditorTool,
			EditorBrushRadius, EditorBrushStrength,
			EditorFlattenHeight,
			EditorBrushIsSquare,
			EditorBlockMode, EditorBlockLevelHeight,
			EditorPaintTextureIndex, EditorCliffPaintTextureIndex,
			pathingMask, pathingAdd);

		if (result.HeightsModified || result.SplatModified || result.PathingModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(result.HeightsModified, false);
			if (result.HeightsModified)
			{
				AlignAllEntitiesToTerrain();
			}
			if (result.PathingModified && PathingOverlayVisible)
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

		EntityToProp3D[entity] = prop;

		position.Y = _editorService.GetTerrainHeightAt(position);
		prop.Position = position;
		
		float actualScale = 1.0f;
		if (IsMapEditorMode)
		{
			prop.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			prop.Scale *= EditorPlacementScale;
			actualScale = EditorPlacementScale;
		}

		EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Prop());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new CollisionScale(actualScale));

		float baseRadius = GetOrCalculateObstacleRadius(propId, prop);
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));

		return prop;
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId))
		{
			decalId = "logo";
		}
		if (decalId.StartsWith("res://") || decalId.Contains('/'))
		{
			if (decalId.EndsWith(".glb") || decalId.EndsWith(".gltf"))
			{
				return "res://icon.svg";
			}
			return decalId;
		}
		string customPath = $"res://Assets/2d/Decals/{decalId}";
		if (ResourceLoader.Exists(customPath))
		{
			return customPath;
		}
		if (!decalId.Contains('.'))
		{
			string customPathWithPng = $"res://Assets/2d/Decals/{decalId}.png";
			if (ResourceLoader.Exists(customPathWithPng))
			{
				return customPathWithPng;
			}
		}
		return "res://icon.svg";
	}

	public Decal SpawnDecalExternal(Vector3 position)
	{
		var entity = EcsWorld.Create();
		var decal = new Decal3D();
		decal.Entity = entity;
		decal.DecalId = "logo";
		decal.TextureAlbedo = GD.Load<Texture2D>("res://icon.svg");
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f);
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		
		position.Y = _editorService.GetTerrainHeightAt(position);
		decal.Position = position;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(0.0f));
		EcsWorld.Add(entity, new ModelScale(1.0f));
		
		if (IsMapEditorMode)
		{
			decal.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
			decal.Scale = Vector3.One;
			EcsWorld.Set(entity, new RotationY(EditorPlacementRotation));
			EcsWorld.Set(entity, new ModelScale(EditorPlacementScale));
		}
		return decal;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		return _editorService.GetTerrainHeightAt(worldPos);
	}

	private void AlignAllEntitiesToTerrain()
	{
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.GlobalPosition;
				pos.Y = _editorService.GetTerrainHeightAt(pos);
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
				pos.Y = _editorService.GetTerrainHeightAt(pos);
				prop.GlobalPosition = pos;
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				var pos = decal.GlobalPosition;
				pos.Y = _editorService.GetTerrainHeightAt(pos);
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
			EntityToUnit3D.Remove(unit.Entity);
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
				EntityToProp3D.Remove(prop.Entity);
				if (EcsWorld.IsAlive(prop.Entity))
				{
					EcsWorld.Destroy(prop.Entity);
				}
				prop.QueueFree();
				return;
			}
			current = current.GetParent();
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
			if (closestDecal is Decal3D decal3D && EcsWorld.IsAlive(decal3D.Entity))
			{
				EcsWorld.Destroy(decal3D.Entity);
			}
			closestDecal.QueueFree();
		}
	}

	private void ProcessMapEditorPhysics(float fDelta)
	{
		if (_simulationService == null || EcsWorld == null) return;

		_simulationService.TickEditorPhysics(fDelta);
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
		var arrivedUnits = _simulationService.GetEditorArrivedUnits();
		arrivedUnits.Clear();
		EcsWorld.Query(in query, _simulationService.EditorMovementQueryDelegate);

		foreach (var entity in arrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}
	


	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale)
	{
		position.Y = _editorService.GetTerrainHeightAt(position);
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

		if (EcsWorld.Has<CollisionScale>(entity))
		{
			EcsWorld.Set(entity, new CollisionScale(scale));
		}
		else
		{
			EcsWorld.Add(entity, new CollisionScale(scale));
		}

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

		EntityToProp3D[entity] = prop;

		position.Y = _editorService.GetTerrainHeightAt(position);
		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * scale;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Prop());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new CollisionScale(scale));

		return prop;
	}

	public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, float rotationY, float scale)
	{
		var entity = EcsWorld.Create();
		var decal = new Decal3D();
		decal.Entity = entity;
		decal.DecalId = string.IsNullOrEmpty(decalId) ? "logo" : decalId;
		var texture = GD.Load<Texture2D>(GetDecalTexturePath(decalId));
		if (texture == null)
		{
			texture = GD.Load<Texture2D>("res://icon.svg");
		}
		decal.TextureAlbedo = texture;
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * scale;
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		
		position.Y = _editorService.GetTerrainHeightAt(position);
		decal.Position = position;
		decal.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		decal.Scale = Vector3.One;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(rotationY));
		EcsWorld.Add(entity, new ModelScale(scale));
		
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
			EntityToUnit3D.Remove(unit.Entity);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
		}
		else if (node is Prop3D prop && GodotObject.IsInstanceValid(prop))
		{
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
			prop.QueueFree();
		}
		else if (node is Decal decal && GodotObject.IsInstanceValid(decal))
		{
			if (decal is Decal3D decal3D && EcsWorld.IsAlive(decal3D.Entity))
			{
				EcsWorld.Destroy(decal3D.Entity);
			}
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
			EntityToUnit3D.Remove(unit.Entity);
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
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
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
			var action = new ObjectDeleteAction("decal", closestDecal is Decal3D decal3D ? decal3D.DecalId : "", closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
			if (closestDecal is Decal3D d3d && EcsWorld.IsAlive(d3d.Entity))
			{
				EcsWorld.Destroy(d3d.Entity);
			}
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

		if (_editorPreviewNode == null || !GodotObject.IsInstanceValid(_editorPreviewNode) || _editorPreviewType != reqType || _editorPreviewId != reqId || _editorPreviewIsEnemy != reqIsEnemy)
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
				var previewDecal = new Decal3D();
				var texture = GD.Load<Texture2D>(GetDecalTexturePath(reqId));
				if (texture == null)
				{
					texture = GD.Load<Texture2D>("res://icon.svg");
				}
				previewDecal.TextureAlbedo = texture;
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
				AddChild(previewDecal);
				previewDecal.DecalId = string.IsNullOrEmpty(reqId) ? "logo" : reqId;

				Color color = new Color(1.0f, 1.0f, 1.0f);
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				previewDecal.AlbedoMix = 0.5f;
				_editorPreviewNode = previewDecal;
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			if (!_editorService.HasCachedRandom) _editorService.GenerateNewRandomPlacementRotationAndScale();
			float previewRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
			float previewScaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;

			Vector3 previewPos = position;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				previewPos = _editorService.SnapToGrid(previewPos);
			}
			previewPos.Y = _editorService.GetTerrainHeightAt(previewPos);
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
		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			_editorPreviewNode.QueueFree();
		}
		_editorPreviewNode = null;
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
		_editorService.TickClumpCooldown(fDelta);

		var mousePos = GetViewport().GetMousePosition();
		var hit = RaycastFromMouse(mousePos);
		Vector3 hitPos = Vector3.Zero;
		bool hasHit = false;
		if (hit != null && hit.ContainsKey("position"))
		{
			hitPos = hit["position"].AsVector3();
			hasHit = true;
		}
		else
		{
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				var from = camera.ProjectRayOrigin(mousePos);
				var normal = camera.ProjectRayNormal(mousePos);
				if (Mathf.Abs(normal.Y) > 0.0001f)
				{
					float t = (0.0f - from.Y) / normal.Y;
					hitPos = from + normal * t;
					hasHit = true;
				}
			}
		}
		if (hasHit)
		{
			UpdateBrushIndicator(hitPos);
			UpdateEditorPreview(hitPos);
			if (GroundTerrain != null)
			{
				if (ActiveEditorTool == EditorTool.SelectArea && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
				{
					var (cx, cz) = _editorService.WorldPosToCellCoords(hitPos);
					_editorService.SetSelectionEnd(new Vector2I(cx, cz));
					int minX = Mathf.Min(_editorService.SelectionStart.Value.X, cx);
					int minZ = Mathf.Min(_editorService.SelectionStart.Value.Y, cz);
					int maxX = Mathf.Max(_editorService.SelectionStart.Value.X, cx);
					int maxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, cz);
					CreateSelectionHighlight();
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
				else if (ActiveEditorTool == EditorTool.DrawCoordinate && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
				{
					var (rcx, rcz) = _editorService.WorldPosToCellCoords(hitPos);
					_editorService.SetSelectionEnd(new Vector2I(rcx, rcz));
					int rMinX = Mathf.Min(_editorService.SelectionStart.Value.X, rcx);
					int rMinZ = Mathf.Min(_editorService.SelectionStart.Value.Y, rcz);
					int rMaxX = Mathf.Max(_editorService.SelectionStart.Value.X, rcx);
					int rMaxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, rcz);
					UpdateCoordinatePreviewMesh(rMinX, rMinZ, rMaxX, rMaxZ);
				}
				else if (ActiveEditorTool == EditorTool.PasteArea && _editorService.HasCopiedArea)
				{
					var (cx, cz) = _editorService.WorldPosToCellCoords(hitPos);
					float r = EditorPasteRotation % 360.0f;
					if (r < 0) r += 360.0f;
					int rotSteps = (int)Math.Round(r / 90.0f) % 4;

					int pasteWidth = _editorService.CopiedAreaWidth;
					int pasteDepth = _editorService.CopiedAreaDepth;

					int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
					int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

					int dX = 0;
					int dZ = 0;
					if (rotSteps == 1 || rotSteps == 3)
					{
						dX = (pasteWidth - pasteDepth) / 2;
						dZ = (pasteDepth - pasteWidth) / 2;
					}

					int minX = Mathf.Clamp(cx + dX, 0, GroundTerrain.Width - 1);
					int minZ = Mathf.Clamp(cz + dZ, 0, GroundTerrain.Depth - 1);
					int maxX = Mathf.Clamp(cx + dX + targetWidth - 1, 0, GroundTerrain.Width - 1);
					int maxZ = Mathf.Clamp(cz + dZ + targetDepth - 1, 0, GroundTerrain.Depth - 1);

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
				var collider = (hit != null && hit.ContainsKey("collider")) ? hit["collider"].As<Node>() : null;
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
					if (!_editorService.IsDrawingClump)
					{
						_editorService.BeginClumpSession();
					}
					if (_editorService.CanSpawnClump())
					{
						ApplyGeneralClumpSpawn(hitPos);
						_editorService.SetClumpCooldown(0.15f);
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
							dragPos = _editorService.SnapToGrid(dragPos);
						}
						dragPos.Y = _editorService.GetTerrainHeightAt(dragPos);
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
									 ActiveEditorTool == EditorTool.Plateau ||
									 ActiveEditorTool == EditorTool.PaintGrass ||
									 ActiveEditorTool == EditorTool.PaintDirt ||
									 ActiveEditorTool == EditorTool.PaintRock ||
									 ActiveEditorTool == EditorTool.PaintSand ||
									 ActiveEditorTool == EditorTool.Noise ||
									 ActiveEditorTool == EditorTool.PaintPathing;

				if (isTerrainTool && !_editorService.IsDrawingTerrain && GroundTerrain != null)
				{
					_editorService.BeginTerrainDraw(
						hitPos,
						ActiveEditorTool,
						EditorBlockMode,
						EditorBlockLevelHeight,
						EditorFlattenHeight,
						GroundTerrain.Heights,
						GroundTerrain.SplatMap,
						GroundTerrain.PathingCodes,
						out float newFlattenHeight);

					EditorFlattenHeight = newFlattenHeight;
					if (ActiveEditorTool == EditorTool.Flatten)
					{
						MapEditorHUD.Instance?.UpdateFlattenHeightExternal(EditorFlattenHeight);
					}
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
				if (_editorService.IsDrawingClump)
				{
					var composite = _editorService.EndClumpSession();
					if (composite != null)
					{
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}
				}

				_editorService.ResetDrawState();

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
				if (_editorService.IsSelectingArea)
				{
					_editorService.SetIsSelectingArea(false);
				}
				if (_editorService.IsDrawingTerrain)
				{
					if (GroundTerrain != null && GroundTerrain.Heights != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
					{
						var action = _editorService.EndTerrainDraw(
							(float[,])GroundTerrain.Heights.Clone(),
							(TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone(),
							(int[,])GroundTerrain.PathingCodes.Clone());

						EditorHistoryManager.RecordAction(action);
						bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
											 ActiveEditorTool == EditorTool.Lower ||
											 ActiveEditorTool == EditorTool.Flatten ||
											 ActiveEditorTool == EditorTool.Smooth ||
											 ActiveEditorTool == EditorTool.Plateau ||
											 ActiveEditorTool == EditorTool.Noise ||
											 ActiveEditorTool == EditorTool.PaintPathing;
						if (isHeightsTool)
						{
							GroundTerrain.BakeNavMesh();
							RebuildGridOverlayMeshExternal();
							UpdatePathingOverlay();
						}
						EditorHasUnsavedChanges = true;
					}
					else
					{
						_editorService.EndTerrainDraw(null, null, null);
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
			if (_editorService.IsDrawingClump)
			{
				var composite = _editorService.EndClumpSession();
				if (composite != null)
				{
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}
			}
			if (_editorService.IsSelectingArea)
			{
				_editorService.SetIsSelectingArea(false);
			}
			if (_editorService.IsDrawingTerrain)
			{
				if (GroundTerrain != null && GroundTerrain.Heights != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
				{
					var action = _editorService.EndTerrainDraw(
						(float[,])GroundTerrain.Heights.Clone(),
						(TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone(),
						(int[,])GroundTerrain.PathingCodes.Clone());

					EditorHistoryManager.RecordAction(action);
					bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
										 ActiveEditorTool == EditorTool.Lower ||
										 ActiveEditorTool == EditorTool.Flatten ||
										 ActiveEditorTool == EditorTool.Smooth ||
										 ActiveEditorTool == EditorTool.Plateau ||
										 ActiveEditorTool == EditorTool.Noise ||
										 ActiveEditorTool == EditorTool.PaintPathing;
					if (isHeightsTool)
					{
						GroundTerrain.BakeNavMesh();
						RebuildGridOverlayMeshExternal();
						UpdatePathingOverlay();
					}
					EditorHasUnsavedChanges = true;
				}
				else
				{
					_editorService.EndTerrainDraw(null, null, null);
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
		
		if (MapEditorHUD.ReturningFromTest)
		{
			LoadMapFromFile("user://temp_map_workspace/terrain.json");
			MapEditorHUD.ReturningFromTest = false;
		}
		else
		{
			ClearMapEntirely();
		}
		
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
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		
		ReinitializeEcsAndServices();
		
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
							 ActiveEditorTool == EditorTool.Plateau ||
							 ActiveEditorTool == EditorTool.PaintGrass ||
							 ActiveEditorTool == EditorTool.PaintDirt ||
							 ActiveEditorTool == EditorTool.PaintRock ||
							 ActiveEditorTool == EditorTool.PaintSand ||
							 ActiveEditorTool == EditorTool.Noise ||
							 ActiveEditorTool == EditorTool.Ramp ||
							 ActiveEditorTool == EditorTool.PlacePropClump;
							 
		_brushIndicatorMesh.Visible = isTerrainTool;
	}

	public MeshInstance3D BrushIndicatorMesh => _brushIndicatorMesh;
	public MeshInstance3D? GridOverlayMesh => _gridOverlayMesh;

	public void ClearRampStartPosExternal()
	{
		_editorService.SetRampStartPos(null);
	}

	public struct MirroredTransform
	{
		public Vector3 Position;
		public float Rotation;
	}

	public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
	{
		return _editorService.GetMirroredTransforms(pos, rotation, EditorMirrorMode);
	}

	private Node3D FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
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
		return _editorService.ApplyRamp(start, end, EditorBrushRadius, EditorBlockMode, EditorBlockLevelHeight, EditorPaintTextureIndex, EditorCliffPaintTextureIndex);
	}

	private float GetMinHeightInBrushBounds(Vector3 worldPos)
	{
		return _editorService.GetMinHeightInBrushBounds(worldPos, EditorBrushRadius, EditorBrushIsSquare);
	}

	private void ApplyGeneralClumpSpawn(Vector3 centerPos)
	{
		var requests = _editorService.BuildClumpSpawnRequests(
			centerPos,
			ActiveEditorTool,
			ActivePlaceId,
			PlaceUnitIsEnemy,
			EditorPlacementScale,
			EditorClumpDensity,
			EditorClumpScaleVar,
			EditorBrushRadius,
			EditorBrushIsSquare,
			EditorRandomRotation,
			EditorRandomScale,
			EditorPlacementRotation,
			EditorMirrorMode);

		foreach (var req in requests)
		{
			Node spawnedNode = null;
			if (req.Type == "unit")
				spawnedNode = SpawnUnitExternal(req.Id, req.Position, req.IsEnemy, req.Rotation, req.Scale);
			else if (req.Type == "prop")
				spawnedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			else if (req.Type == "decal")
				spawnedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

			if (spawnedNode != null)
			{
				_editorService.RecordClumpSpawnAction(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, req.IsEnemy, spawnedNode));
			}
		}
	}

	public void SwapTexturesExternal(int indexA, int indexB)
	{
		if (GroundTerrain == null || GroundTerrain.SplatMap == null) return;
		if (indexA == indexB) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;

		float[,] heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		TerrainSplatWeights[,] splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();

		bool anyChanged = false;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				var s = GroundTerrain.SplatMap[x, z];
				if (s.Index0 == indexA || s.Index0 == indexB ||
				    s.Index1 == indexA || s.Index1 == indexB ||
				    s.Index2 == indexA || s.Index2 == indexB ||
				    s.Index3 == indexA || s.Index3 == indexB)
				{
					GroundTerrain.SplatMap[x, z] = new TerrainSplatWeights
					{
						Index0 = s.Index0 == indexA ? indexB : (s.Index0 == indexB ? indexA : s.Index0),
						Index1 = s.Index1 == indexA ? indexB : (s.Index1 == indexB ? indexA : s.Index1),
						Index2 = s.Index2 == indexA ? indexB : (s.Index2 == indexB ? indexA : s.Index2),
						Index3 = s.Index3 == indexA ? indexB : (s.Index3 == indexB ? indexA : s.Index3),
						Weight0 = s.Weight0,
						Weight1 = s.Weight1,
						Weight2 = s.Weight2,
						Weight3 = s.Weight3
					};
					anyChanged = true;
				}
			}
		}

		if (anyChanged)
		{
			float[,] heightsAfter = (float[,])GroundTerrain.Heights.Clone();
			TerrainSplatWeights[,] splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			
			var action = new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			
			GroundTerrain.UpdateMeshAndPhysics(false, false);
			MapEditorHUD.Instance?.ShowFeedbackExternal("Textures swapped successfully!");
		}
	}

	public void AlignTerrainSplatMapExternal()
	{
		if (GroundTerrain != null)
		{
			_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);
		}
	}

	public void ResizeMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		var before = MapStateSnapshot.CreateSnapshot();

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;

		float diffWidth = (newWidth - oldWidth) * GroundTerrain.Spacing;
		float diffDepth = (newDepth - oldDepth) * GroundTerrain.Spacing;

		EditorCameraBoundsLeft -= diffWidth / 2.0f;
		EditorCameraBoundsRight += diffWidth / 2.0f;
		EditorCameraBoundsTop -= diffDepth / 2.0f;
		EditorCameraBoundsBottom += diffDepth / 2.0f;

		GroundTerrain.ResizeTerrain(newWidth, newDepth);

		_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);
		DeleteEntitiesOutsideBounds();

		RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Map resized to {newWidth}x{newDepth}");

		var after = MapStateSnapshot.CreateSnapshot();
		EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
	}

	public void ScaleMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		var before = MapStateSnapshot.CreateSnapshot();

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		float oldHalfW = (oldWidth - 1) / 2.0f * spacing;
		float oldHalfD = (oldDepth - 1) / 2.0f * spacing;
		float newHalfW = (newWidth - 1) / 2.0f * spacing;
		float newHalfD = (newDepth - 1) / 2.0f * spacing;
		float scaleX = oldHalfW > 0f ? newHalfW / oldHalfW : 1f;
		float scaleZ = oldHalfD > 0f ? newHalfD / oldHalfD : 1f;

		GroundTerrain.ScaleTerrainData(newWidth, newDepth);

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.Position = new Godot.Vector3(unit.Position.X * scaleX, unit.Position.Y, unit.Position.Z * scaleZ);
			}
		}

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				prop.Position = new Godot.Vector3(prop.Position.X * scaleX, prop.Position.Y, prop.Position.Z * scaleZ);
			}
		}

		foreach (var child in GetChildren())
		{
			if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				decal.Position = new Godot.Vector3(decal.Position.X * scaleX, decal.Position.Y, decal.Position.Z * scaleZ);
			}
		}

		float diffWidth = (newWidth - oldWidth) * spacing;
		float diffDepth = (newDepth - oldDepth) * spacing;
		EditorCameraBoundsLeft -= diffWidth / 2.0f;
		EditorCameraBoundsRight += diffWidth / 2.0f;
		EditorCameraBoundsTop -= diffDepth / 2.0f;
		EditorCameraBoundsBottom += diffDepth / 2.0f;

		DeleteEntitiesOutsideBounds();

		_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);
		RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Map scaled to {newWidth}x{newDepth}");

		var after = MapStateSnapshot.CreateSnapshot();
		EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
	}

	private void DeleteEntitiesOutsideBounds()
	{
		if (GroundTerrain == null) return;

		float halfW = (GroundTerrain.Width - 1) / 2.0f * GroundTerrain.Spacing;
		float halfD = (GroundTerrain.Depth - 1) / 2.0f * GroundTerrain.Spacing;

		var unitsToDelete = new List<Unit3D>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					unitsToDelete.Add(unit);
				}
			}
		}
		foreach (var unit in unitsToDelete)
		{
			DeleteNodeExternal(unit);
		}

		var propsToDelete = new List<Prop3D>();
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					propsToDelete.Add(prop);
				}
			}
		}
		foreach (var prop in propsToDelete)
		{
			DeleteNodeExternal(prop);
		}
	}

	private MeshInstance3D _scaleMapSilhouetteMesh;

	public void ShowScaleMapSilhouette(int previewWidth, int previewDepth)
	{
		if (GroundTerrain == null) return;

		HideScaleMapSilhouette();

		_scaleMapSilhouetteMesh = new MeshInstance3D();
		_scaleMapSilhouetteMesh.Name = "ScaleMapSilhouette";

		var plane = new PlaneMesh();
		plane.Size = new Godot.Vector2(previewWidth * GroundTerrain.Spacing, previewDepth * GroundTerrain.Spacing);
		plane.SubdivideWidth = 0;
		plane.SubdivideDepth = 0;
		_scaleMapSilhouetteMesh.Mesh = plane;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.8f, 0.5f, 0.05f, 0.25f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(1.0f, 0.6f, 0.1f) * 0.4f;
		_scaleMapSilhouetteMesh.MaterialOverride = mat;

		float peakY = 0f;
		if (GroundTerrain.Heights != null)
		{
			for (int z = 0; z < GroundTerrain.Depth; z++)
				for (int x = 0; x < GroundTerrain.Width; x++)
					if (GroundTerrain.Heights[x, z] > peakY) peakY = GroundTerrain.Heights[x, z];
		}
		_scaleMapSilhouetteMesh.Position = new Godot.Vector3(0f, peakY + 0.5f, 0f);

		AddChild(_scaleMapSilhouetteMesh);
	}

	public void HideScaleMapSilhouette()
	{
		if (_scaleMapSilhouetteMesh != null)
		{
			_scaleMapSilhouetteMesh.QueueFree();
			_scaleMapSilhouetteMesh = null;
		}
	}

	public void RebuildCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
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
			if (GroundTerrain == null || GroundTerrain.Heights == null) return 0f;
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
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null) return;

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();

		var node3Ds = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d) node3Ds.Add(n3d);
		}

		var entities = _editorService.BuildCopiedEntityList(minX, minZ, maxX, maxZ, node3Ds);
		_editorService.CopyArea(minX, minZ, maxX, maxZ, entities);

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
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
		if (_pathingOverlayMesh == null || GroundTerrain == null || GroundTerrain.PathingCodes == null || GroundTerrain.Heights == null) return;

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
			EditableTerrain.PATHING_BUILDABLE     => new Color(0.6f, 0.2f, 0.8f, 0.55f),
			_ => new Color(0f, 0f, 0f, 0f)
		};

		var allFlags = new int[]
		{
			EditableTerrain.PATHING_SHALLOW_WATER,
			EditableTerrain.PATHING_DEEP_WATER,
			EditableTerrain.PATHING_FLYING,
			EditableTerrain.PATHING_GROUND,
			EditableTerrain.PATHING_UNPATHABLE,
			EditableTerrain.PATHING_BUILDABLE
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

							float hSub00 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx0), Mathf.Lerp(h01, h11, tx0), tz0);
							float hSub10 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx1), Mathf.Lerp(h01, h11, tx1), tz0);
							float hSub11 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx1), Mathf.Lerp(h01, h11, tx1), tz1);
							float hSub01 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx0), Mathf.Lerp(h01, h11, tx0), tz1);

							float subX0 = lx0 + sx * (spacing / S);
							float subX1 = lx0 + (sx + 1) * (spacing / S);
							float subZ0 = lz0 + sz * (spacing / S);
							float subZ1 = lz0 + (sz + 1) * (spacing / S);

							int flagIndex = (sx + sz) % activeFlags.Count;
							Color subColor = GetLayerColor(activeFlags[flagIndex]);
							if (subColor.A < 0.01f) continue;

							int baseV = verticesList.Count;
							verticesList.Add(new Vector3(subX0, hSub00, subZ0));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX1, hSub10, subZ0));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX1, hSub11, subZ1));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX0, hSub01, subZ1));
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
		if (_gridOverlayMesh == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
		if (!EditorGridVisible) return;

		bool straightMode = EditorGridMode == GridOverlayMode.Straight;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		float straightY = 0f;
		if (straightMode)
		{
			float maxH = float.MinValue;
			for (int sz = 0; sz < depth; sz++)
				for (int sx = 0; sx < width; sx++)
					if (GroundTerrain.Heights[sx, sz] > maxH)
						maxH = GroundTerrain.Heights[sx, sz];
			straightY = maxH + 1.0f;
		}

		int centerZ = (depth - 1) / 2;
		int centerX = (width - 1) / 2;

		int totalVertices = 0;
		for (int z = 0; z < depth; z++)
		{
			bool isThick = ((z - centerZ) % 10 == 0);
			totalVertices += (width - 1) * (isThick ? 6 : 2);
		}
		for (int x = 0; x < width; x++)
		{
			bool isThick = ((x - centerX) % 10 == 0);
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
			bool isThick = ((z - centerZ) % 10 == 0);
			Color col = isThick ? thickColor : thinColor;
			float lz = (z - (depth - 1) / 2.0f) * spacing;
			for (int x = 0; x < width - 1; x++)
			{
				float lx1 = (x - (width - 1) / 2.0f) * spacing;
				float lx2 = (x + 1 - (width - 1) / 2.0f) * spacing;

				float y1 = straightMode ? straightY : GroundTerrain.Heights[x, z] + 0.15f;
				float y2 = straightMode ? straightY : GroundTerrain.Heights[x + 1, z] + 0.15f;

				AddLine(new Vector3(lx1, y1, lz), new Vector3(lx2, y2, lz), col, isThick, false);
			}
		}

		for (int x = 0; x < width; x++)
		{
			bool isThick = ((x - centerX) % 10 == 0);
			Color col = isThick ? thickColor : thinColor;
			float lx = (x - (width - 1) / 2.0f) * spacing;
			for (int z = 0; z < depth - 1; z++)
			{
				float lz1 = (z - (depth - 1) / 2.0f) * spacing;
				float lz2 = (z + 1 - (depth - 1) / 2.0f) * spacing;

				float y1 = straightMode ? straightY : GroundTerrain.Heights[x, z] + 0.15f;
				float y2 = straightMode ? straightY : GroundTerrain.Heights[x, z + 1] + 0.15f;

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

	public void PerformFloodFill(Vector3 clickPos, int fillTextureIndex)
	{
		if (GroundTerrain == null || GroundTerrain.Heights == null || GroundTerrain.SplatMap == null) return;
		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();

		var result = _editorService.PerformFloodFill(clickPos, fillTextureIndex, EditorMirrorMode);
		if (result.Heights == null || result.SplatMap == null) return;

		Array.Copy(result.SplatMap, GroundTerrain.SplatMap, result.SplatMap.Length);
		GroundTerrain.UpdateMeshAndPhysics(false, false);
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var action = new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled terrain area");
	}

	public void PerformFloodFillPathing(Vector3 clickPos, int pathingMask, bool pathingAdd)
	{
		if (GroundTerrain == null || GroundTerrain.PathingCodes == null) return;

		var result = _editorService.PerformFloodFillPathing(clickPos, pathingMask, pathingAdd, EditorMirrorMode);

		if (result.Before != null && result.After != null)
		{
			GroundTerrain.UpdateMeshAndPhysics(false, false);
			var action = new TerrainModifyAction(null, null, null, null, result.Before, result.After);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled pathing area");
			UpdatePathingOverlay();
			GroundTerrain.BakeNavMesh();
		}
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
		if (_selectionHighlightMesh == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
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

	private void CreateCoordinatePreviewMesh()
	{
		if (_coordinatePreviewMesh != null) return;
		_coordinatePreviewMesh = new MeshInstance3D();
		_coordinatePreviewMesh.Name = "CoordinatePreview";
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.1f, 1.0f, 0.3f, 0.4f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_coordinatePreviewMesh.MaterialOverride = mat;
		AddChild(_coordinatePreviewMesh);
		_coordinatePreviewMesh.Visible = false;
	}

	public void UpdateCoordinatePreviewMesh(int minX, int minZ, int maxX, int maxZ)
	{
		CreateCoordinatePreviewMesh();
		RebuildCoordinateMeshInstance(_coordinatePreviewMesh, minX, minZ, maxX, maxZ, new Color(0.1f, 1.0f, 0.3f, 0.4f));
	}

	public void HideCoordinatePreviewMesh()
	{
		if (_coordinatePreviewMesh != null) _coordinatePreviewMesh.Visible = false;
	}

	private void RebuildCoordinateMeshInstance(MeshInstance3D meshInstance, int minX, int minZ, int maxX, int maxZ, Color color, float yOffset = 0.15f)
	{
		if (meshInstance == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
		{
			meshInstance.Visible = false;
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
				vertices[idx] = new Vector3(lx, GroundTerrain.Heights[mapX, mapZ] + yOffset, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		var colors = new Color[vertexCount];
		for (int i = 0; i < vertexCount; i++) colors[i] = color;
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
		arrays[(int)Mesh.ArrayType.Color] = colors;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		meshInstance.Mesh = arrayMesh;
		meshInstance.Visible = true;
	}

	public bool CommitCoordinateExternal(string coordinateName, int minX, int minZ, int maxX, int maxZ)
	{
		if (GroundTerrain == null) return false;
		string safeName = coordinateName.Trim();
		if (string.IsNullOrEmpty(safeName)) return false;

		var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		float worldMinX = (minX - (width - 1) / 2.0f) * spacing;
		float worldMinZ = (minZ - (depth - 1) / 2.0f) * spacing;
		float worldMaxX = (maxX - (width - 1) / 2.0f) * spacing;
		float worldMaxZ = (maxZ - (depth - 1) / 2.0f) * spacing;

		bool committed = false;
		for (int i = 0; i < EditorCoordinates.Count; i++)
		{
			if (EditorCoordinates[i].Name == safeName)
			{
				EditorCoordinates[i] = new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ };
				committed = true;
				break;
			}
		}

		if (!committed)
		{
			EditorCoordinates.Add(new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ });
		}

		RebuildAllCoordinatePersistentMeshes();

		var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		var action = new CoordinateAction(oldCoordinates, newCoordinates);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;

		return true;
	}

	public void DeleteCoordinateExternal(string coordinateName)
	{
		var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		EditorCoordinates.RemoveAll(r => r.Name == coordinateName);
		RebuildAllCoordinatePersistentMeshes();

		var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		var action = new CoordinateAction(oldCoordinates, newCoordinates);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;
	}

	public void RebuildAllCoordinatePersistentMeshes()
	{
		foreach (var mesh in _coordinatePersistentMeshes)
		{
			if (GodotObject.IsInstanceValid(mesh))
			{
				RemoveChild(mesh);
				mesh.QueueFree();
			}
		}
		_coordinatePersistentMeshes.Clear();

		if (GroundTerrain == null || GroundTerrain.Heights == null) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		foreach (var coord in EditorCoordinates)
		{
			int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / spacing + (width - 1) / 2.0f), 0, width - 1);
			int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / spacing + (depth - 1) / 2.0f), 0, depth - 1);
			int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / spacing + (width - 1) / 2.0f), 0, width - 1);
			int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / spacing + (depth - 1) / 2.0f), 0, depth - 1);

			var meshInst = new MeshInstance3D();
			meshInst.Name = $"Coordinate_{coord.Name}";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.1f, 0.9f, 0.3f, 0.25f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			meshInst.MaterialOverride = mat;
			AddChild(meshInst);
			RebuildCoordinateMeshInstance(meshInst, minX, minZ, maxX, maxZ, new Color(0.1f, 0.9f, 0.3f, 0.25f));
			_coordinatePersistentMeshes.Add(meshInst);
		}
	}

	public void SelectCoordinateExternal(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName))
		{
			HideCoordinateSelectionOutline();
			return;
		}

		if (GroundTerrain == null || GroundTerrain.Heights == null) return;

		EditorCoordinate? found = null;
		foreach (var r in EditorCoordinates)
		{
			if (r.Name == coordinateName)
			{
				found = r;
				break;
			}
		}

		if (found == null)
		{
			HideCoordinateSelectionOutline();
			return;
		}

		var coord = found.Value;

		if (_coordinateSelectionOutlineMesh == null)
		{
			_coordinateSelectionOutlineMesh = new MeshInstance3D();
			_coordinateSelectionOutlineMesh.Name = "CoordinateSelectionOutline";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 0.6f, 0.0f, 0.45f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			_coordinateSelectionOutlineMesh.MaterialOverride = mat;
			AddChild(_coordinateSelectionOutlineMesh);
		}

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / spacing + (width - 1) / 2.0f), 0, width - 1);
		int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / spacing + (depth - 1) / 2.0f), 0, depth - 1);
		int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / spacing + (width - 1) / 2.0f), 0, width - 1);
		int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / spacing + (depth - 1) / 2.0f), 0, depth - 1);

		_coordinateSelectionOutlineMesh.Visible = true;
		RebuildCoordinateMeshInstance(_coordinateSelectionOutlineMesh, minX, minZ, maxX, maxZ, new Color(1.0f, 0.6f, 0.0f, 0.45f), 0.25f);

		float centerX = (coord.MinX + coord.MaxX) / 2.0f;
		float centerZ = (coord.MinZ + coord.MaxZ) / 2.0f;
		float centerY = GetTerrainHeightAt(new Vector3(centerX, 0f, centerZ));

		var camera = GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(centerX, centerY, centerZ + 25f);
		}
	}

	public void HideCoordinateSelectionOutline()
	{
		if (_coordinateSelectionOutlineMesh != null)
		{
			_coordinateSelectionOutlineMesh.Visible = false;
		}
	}

	public void PerformEraseAreaExternal()
	{
		PerformEraseArea();
		if (GroundTerrain != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
		{
			var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
			if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
			{
				RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
			}
		}
	}

	public void PerformMirrorSelectionVerticallyExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to mirror (select an area first)");
			return;
		}

		PerformCopyArea();
		var eraseActions = PerformEraseArea(false);
		_editorService.MirrorCopiedAreaVertically();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		var pasteActions = PerformPasteArea(minX, minZ, 0.0f, false);

		var combined = new List<IEditorAction>();
		if (eraseActions != null) combined.AddRange(eraseActions);
		if (pasteActions != null) combined.AddRange(pasteActions);

		if (combined.Count > 0)
		{
			var composite = new CompositeAction(combined);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
		}

		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Selection Mirrored Vertically");
	}

	public void PerformMirrorSelectionHorizontallyExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to mirror (select an area first)");
			return;
		}

		PerformCopyArea();
		var eraseActions = PerformEraseArea(false);
		_editorService.MirrorCopiedAreaHorizontally();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		var pasteActions = PerformPasteArea(minX, minZ, 0.0f, false);

		var combined = new List<IEditorAction>();
		if (eraseActions != null) combined.AddRange(eraseActions);
		if (pasteActions != null) combined.AddRange(pasteActions);

		if (combined.Count > 0)
		{
			var composite = new CompositeAction(combined);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
		}

		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Selection Mirrored Horizontally");
	}

	public void PerformCutAreaExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
			return;
		}

		PerformCopyArea();
		PerformEraseArea();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Cut");
	}

	private List<IEditorAction> PerformEraseArea(bool recordToHistory = true)
	{
		if (GroundTerrain == null || GroundTerrain.Heights == null || GroundTerrain.SplatMap == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Erase (select an area first)");
			return new List<IEditorAction>();
		}

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();

		var node3Ds = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d) node3Ds.Add(n3d);
		}

		var eraseResult = _editorService.BuildEraseAreaResult(
			minX, minZ, maxX, maxZ,
			PasteOptionHeights, PasteOptionTextures, PasteOptionEntities,
			node3Ds, _editorPreviewNode as Node3D);

		if (eraseResult.TerrainModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(eraseResult.HeightsModified, false);
			if (eraseResult.HeightsModified)
			{
				AlignAllEntitiesToTerrain();
			}
		}

		var deleteActions = new List<IEditorAction>();
		foreach (var node in eraseResult.NodesToDelete)
		{
			var act = DeleteObjectAtWithUndo(node, node.Position);
			if (act != null) deleteActions.Add(act);
		}

		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var actions = new List<IEditorAction>();
		if (eraseResult.TerrainModified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter));
		}
		if (deleteActions.Count > 0)
		{
			actions.AddRange(deleteActions);
		}

		if (actions.Count > 0)
		{
			if (recordToHistory)
			{
				var composite = new CompositeAction(actions);
				EditorHistoryManager.RecordAction(composite);
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Area Erased");
			}
		}
		return actions;
	}

	private List<IEditorAction> PerformPasteArea(int startX, int startZ, float rotationDegrees, bool recordToHistory = true)
	{
		if (GroundTerrain == null || GroundTerrain.Heights == null || GroundTerrain.SplatMap == null || !_editorService.HasCopiedArea) return new List<IEditorAction>();

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();

		var pasteResult = _editorService.BuildPasteAreaResult(
			startX, startZ,
			PasteOptionHeights, PasteOptionTextures, PasteOptionEntities,
			EditorMirrorMode,
			rotationDegrees);

		if (pasteResult.TerrainModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(pasteResult.HeightsModified, false);
			if (pasteResult.HeightsModified)
			{
				AlignAllEntitiesToTerrain();
			}
		}

		var spawnActions = new List<IEditorAction>();
		foreach (var req in pasteResult.SpawnRequests)
		{
			Node pastedNode = null;
			if (req.Type == "unit")
				pastedNode = SpawnUnitExternal(req.Id, req.Position, req.IsEnemy, req.Rotation, req.Scale);
			else if (req.Type == "prop")
				pastedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			else if (req.Type == "decal")
				pastedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

			if (pastedNode != null)
			{
				spawnActions.Add(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, req.IsEnemy, pastedNode));
			}
		}

		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var actions = new List<IEditorAction>();
		if (pasteResult.TerrainModified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter));
		}
		if (spawnActions.Count > 0)
		{
			actions.AddRange(spawnActions);
		}
		if (actions.Count > 0)
		{
			if (recordToHistory)
			{
				var composite = new CompositeAction(actions);
				EditorHistoryManager.RecordAction(composite);
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted Area");
			}
		}
		return actions;
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

		bool shouldBeVisible = IsMapEditorMode && PathingOverlayVisible && (ActiveEditorTool == EditorTool.PaintPathing || ActiveEditorTool == EditorTool.FloodFillPathing);
		_pathingOverlayMesh.Visible = shouldBeVisible;

		if (shouldBeVisible && GroundTerrain != null)
		{
			RebuildPathingOverlay();
		}
	}

	public void RefreshSelectionHighlight()
	{
		if (GroundTerrain != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
		{
			var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
			if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
			{
				RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
			}
		}
	}
}
