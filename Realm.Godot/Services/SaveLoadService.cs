using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

public class SaveLoadService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	public SaveLoadService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool SaveMapToFile(
		string absolutePath,
		string[] htmlColors,
		(Entity Entity, float RotationY, float Scale)[] unitsData,
		(Entity Entity, float RotationY, float Scale)[] propsData,
		(string DecalId, System.Numerics.Vector3 Position, float RotationY, float Scale)[] decalsData)
	{
		try
		{
			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			_ecsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity != Entity.Null)
			{
				if (_ecsWorld.Has<TerrainColorsState>(worldEntity))
				{
					_ecsWorld.Set(worldEntity, new TerrainColorsState(htmlColors));
				}
				else
				{
					_ecsWorld.Add(worldEntity, new TerrainColorsState(htmlColors));
				}
			}

			foreach (var u in unitsData)
			{
				if (_ecsWorld.IsAlive(u.Entity))
				{
					if (_ecsWorld.Has<RotationY>(u.Entity))
					{
						_ecsWorld.Set(u.Entity, new RotationY(u.RotationY));
					}
					else
					{
						_ecsWorld.Add(u.Entity, new RotationY(u.RotationY));
					}

					if (_ecsWorld.Has<ModelScale>(u.Entity))
					{
						_ecsWorld.Set(u.Entity, new ModelScale(u.Scale));
					}
					else
					{
						_ecsWorld.Add(u.Entity, new ModelScale(u.Scale));
					}
				}
			}

			foreach (var p in propsData)
			{
				if (_ecsWorld.IsAlive(p.Entity))
				{
					if (_ecsWorld.Has<RotationY>(p.Entity))
					{
						_ecsWorld.Set(p.Entity, new RotationY(p.RotationY));
					}
					else
					{
						_ecsWorld.Add(p.Entity, new RotationY(p.RotationY));
					}

					if (_ecsWorld.Has<ModelScale>(p.Entity))
					{
						_ecsWorld.Set(p.Entity, new ModelScale(p.Scale));
					}
					else
					{
						_ecsWorld.Add(p.Entity, new ModelScale(p.Scale));
					}
				}
			}

			var tempDecals = new List<Entity>();
			foreach (var d in decalsData)
			{
				var ent = _ecsWorld.Create();
				_ecsWorld.Add(ent, new DecalIdentity(d.DecalId));
				_ecsWorld.Add(ent, new Position(d.Position));
				_ecsWorld.Add(ent, new RotationY(d.RotationY));
				_ecsWorld.Add(ent, new ModelScale(d.Scale));
				tempDecals.Add(ent);
			}

			TerrainState terrain = default;
			EditorState editor = default;
			TerrainColorsState colorsState = default;
			bool foundWorld = false;
			bool foundColors = false;

			var worldQuery2 = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
			_ecsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
				if (_ecsWorld.Has<TerrainColorsState>(entity))
				{
					colorsState = _ecsWorld.Get<TerrainColorsState>(entity);
					foundColors = true;
				}
			});

			if (!foundWorld) return false;

			var saveData = new MapSaveData();
			saveData.WaterEnabled = terrain.WaterEnabled;
			saveData.WaterHeight = terrain.WaterHeight;
			saveData.BlockMode = editor.BlockMode;
			saveData.BlockLevelHeight = editor.BlockLevelHeight;
			saveData.WC3BlockMode = editor.BlockMode;
			saveData.WC3LevelHeight = editor.BlockLevelHeight;
			saveData.CameraBoundsLeft = editor.CameraBoundsLeft;
			saveData.CameraBoundsRight = editor.CameraBoundsRight;
			saveData.CameraBoundsTop = editor.CameraBoundsTop;
			saveData.CameraBoundsBottom = editor.CameraBoundsBottom;
			saveData.SkyboxPath = editor.SkyboxPath;

			int width = terrain.Width;
			int depth = terrain.Depth;
			saveData.Heights = new float[width * depth];
			saveData.Pathing = new int[width * depth];

			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int idx = z * width + x;
					saveData.Heights[idx] = terrain.Heights[x, z];
					saveData.Pathing[idx] = terrain.PathingCodes != null ? terrain.PathingCodes[x, z] : (8 | 4);
				}
			}

			if (foundColors && colorsState.Colors != null)
			{
				saveData.Colors = colorsState.Colors;
			}
			else
			{
				saveData.Colors = new string[width * depth];
			}

			saveData.Units = new List<UnitSaveData>();
			var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
			_ecsWorld.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position pos, ref Owner owner) =>
			{
				float rotY = 0f;
				if (_ecsWorld.Has<RotationY>(entity)) rotY = _ecsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (_ecsWorld.Has<ModelScale>(entity)) scale = _ecsWorld.Get<ModelScale>(entity).Value;

				bool isEnemy = false;
				if (_ecsWorld.IsAlive(owner.PlayerEntity.Value) && _ecsWorld.Has<Name>(owner.PlayerEntity.Value))
				{
					isEnemy = _ecsWorld.Get<Name>(owner.PlayerEntity.Value).Value == "Enemy_AI";
				}

				saveData.Units.Add(new UnitSaveData
				{
					UnitId = defId.Value,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationY = rotY,
					Scale = scale,
					IsEnemy = isEnemy
				});
			});

			saveData.Props = new List<PropSaveData>();
			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			_ecsWorld.Query(in propQuery, (Entity entity, ref PropIdentity propId, ref Position pos) =>
			{
				float rotY = 0f;
				if (_ecsWorld.Has<RotationY>(entity)) rotY = _ecsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (_ecsWorld.Has<ModelScale>(entity)) scale = _ecsWorld.Get<ModelScale>(entity).Value;

				saveData.Props.Add(new PropSaveData
				{
					PropId = propId.PropId,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationY = rotY,
					Scale = scale
				});
			});

			saveData.Decals = new List<DecalSaveData>();
			var decalQuery = Realm.Ecs.Common.QueryCache.AllDecalIdentityAndPositionQuery;
			_ecsWorld.Query(in decalQuery, (Entity entity, ref DecalIdentity decalId, ref Position pos) =>
			{
				float rotY = 0f;
				if (_ecsWorld.Has<RotationY>(entity)) rotY = _ecsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (_ecsWorld.Has<ModelScale>(entity)) scale = _ecsWorld.Get<ModelScale>(entity).Value;

				saveData.Decals.Add(new DecalSaveData
				{
					DecalId = decalId.DecalId,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationY = rotY,
					Scale = scale
				});
			});

			string json = JsonSerializer.Serialize(saveData);
			File.WriteAllText(absolutePath, json);

			foreach (var ent in tempDecals)
			{
				if (_ecsWorld.IsAlive(ent))
				{
					_ecsWorld.Destroy(ent);
				}
			}

			if (foundWorld)
			{
				var updatedEditor = new EditorState(
					editor.BlockMode,
					editor.BlockLevelHeight,
					editor.CameraBoundsLeft,
					editor.CameraBoundsRight,
					editor.CameraBoundsTop,
					editor.CameraBoundsBottom,
					editor.SkyboxPath,
					false,
					editor.MirrorMode
				);

				_ecsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
				{
					_ecsWorld.Set(entity, updatedEditor);
				});
			}

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return false;
		}
	}

	public bool LoadMapFromFile(string absolutePath, bool terrainOnly = false)
	{
		if (!File.Exists(absolutePath)) return false;

		try
		{
			string json = File.ReadAllText(absolutePath);
			var saveData = JsonSerializer.Deserialize<MapSaveData>(json);
			if (saveData == null) return false;

			var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
			var unitsToDestroy = new List<Entity>();
			_ecsWorld.Query(in unitQuery, (Entity entity) => unitsToDestroy.Add(entity));
			foreach (var ent in unitsToDestroy) _ecsWorld.Destroy(ent);

			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			var propsToDestroy = new List<Entity>();
			_ecsWorld.Query(in propQuery, (Entity entity) => propsToDestroy.Add(entity));
			foreach (var ent in propsToDestroy) _ecsWorld.Destroy(ent);

			var decalQuery = Realm.Ecs.Common.QueryCache.AllDecalIdentityAndPositionQuery;
			var decalsToDestroy = new List<Entity>();
			_ecsWorld.Query(in decalQuery, (Entity entity) => decalsToDestroy.Add(entity));
			foreach (var ent in decalsToDestroy) _ecsWorld.Destroy(ent);

			var req1 = Realm.Ecs.Common.QueryCache.AllUnitSpawnRequestQuery;
			var req1List = new List<Entity>();
			_ecsWorld.Query(in req1, (Entity entity) => req1List.Add(entity));
			foreach (var ent in req1List) _ecsWorld.Destroy(ent);

			var req2 = Realm.Ecs.Common.QueryCache.AllPropSpawnRequestQuery;
			var req2List = new List<Entity>();
			_ecsWorld.Query(in req2, (Entity entity) => req2List.Add(entity));
			foreach (var ent in req2List) _ecsWorld.Destroy(ent);

			var req3 = Realm.Ecs.Common.QueryCache.AllDecalSpawnRequestQuery;
			var req3List = new List<Entity>();
			_ecsWorld.Query(in req3, (Entity entity) => req3List.Add(entity));
			foreach (var ent in req3List) _ecsWorld.Destroy(ent);

			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			_ecsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity == Entity.Null)
			{
				worldEntity = _ecsWorld.Create();
			}

			if (!_ecsWorld.Has<TerrainState>(worldEntity))
			{
				_ecsWorld.Add(worldEntity, new TerrainState(126, 126, 2.0f, 5.0f / 2.5f / 10.0f, -2.0f, true, new float[126, 126], new int[126, 126], null, null));
			}

			if (_ecsWorld.Has<TerrainState>(worldEntity))
			{
				ref var ts = ref _ecsWorld.Get<TerrainState>(worldEntity);
				int width = ts.Width;
				int depth = ts.Depth;

				if (ts.Heights == null)
				{
					ts.Heights = new float[width, depth];
				}
				if (ts.PathingCodes == null)
				{
					ts.PathingCodes = new int[width, depth];
				}

				if (saveData.Heights != null && saveData.Heights.Length == width * depth)
				{
					for (int z = 0; z < depth; z++)
					{
						for (int x = 0; x < width; x++)
						{
							ts.Heights[x, z] = saveData.Heights[z * width + x];
						}
					}
				}

				if (saveData.Pathing != null && saveData.Pathing.Length == width * depth)
				{
					for (int z = 0; z < depth; z++)
					{
						for (int x = 0; x < width; x++)
						{
							ts.PathingCodes[x, z] = saveData.Pathing[z * width + x];
						}
					}
				}
				else
				{
					for (int z = 0; z < depth; z++)
					{
						for (int x = 0; x < width; x++)
						{
							ts.PathingCodes[x, z] = 8 | 4;
						}
					}
				}

				ts.WaterEnabled = saveData.WaterEnabled ?? true;
				if (saveData.WaterHeight.HasValue)
				{
					ts.WaterHeight = saveData.WaterHeight.Value;
				}

				_ecsWorld.Set(worldEntity, ts);
			}

			if (saveData.Colors != null)
			{
				if (_ecsWorld.Has<TerrainColorsState>(worldEntity))
				{
					_ecsWorld.Set(worldEntity, new TerrainColorsState(saveData.Colors));
				}
				else
				{
					_ecsWorld.Add(worldEntity, new TerrainColorsState(saveData.Colors));
				}
			}

			bool isBlock = saveData.BlockMode ?? saveData.WC3BlockMode ?? false;
			float step = saveData.BlockLevelHeight ?? saveData.WC3LevelHeight ?? 4.0f;
			float left = saveData.CameraBoundsLeft ?? -95.0f;
			float right = saveData.CameraBoundsRight ?? 95.0f;
			float top = saveData.CameraBoundsTop ?? -95.0f;
			float bottom = saveData.CameraBoundsBottom ?? 125.0f;
			string skybox = saveData.SkyboxPath ?? "res://Assets/skybox_panoramic.jpg";

			var newEditorState = new EditorState(isBlock, step, left, right, top, bottom, skybox, false);
			if (_ecsWorld.Has<EditorState>(worldEntity))
			{
				_ecsWorld.Set(worldEntity, newEditorState);
			}
			else
			{
				_ecsWorld.Add(worldEntity, newEditorState);
			}

			if (!terrainOnly)
			{
				if (saveData.Units != null)
				{
					foreach (var u in saveData.Units)
					{
						var reqEnt = _ecsWorld.Create();
						_ecsWorld.Add(reqEnt, new UnitSpawnRequest(
							u.UnitId,
							new System.Numerics.Vector3(u.PosX, u.PosY, u.PosZ),
							u.RotationY,
							u.Scale,
							u.IsEnemy
						));
					}
				}

				if (saveData.Props != null)
				{
					foreach (var p in saveData.Props)
					{
						var reqEnt = _ecsWorld.Create();
						_ecsWorld.Add(reqEnt, new PropSpawnRequest(
							p.PropId,
							new System.Numerics.Vector3(p.PosX, p.PosY, p.PosZ),
							p.RotationY,
							p.Scale
						));
					}
				}

				if (saveData.Decals != null)
				{
					foreach (var d in saveData.Decals)
					{
						var reqEnt = _ecsWorld.Create();
						_ecsWorld.Add(reqEnt, new DecalSpawnRequest(
							d.DecalId,
							new System.Numerics.Vector3(d.PosX, d.PosY, d.PosZ),
							d.RotationY,
							d.Scale
						));
					}
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return false;
		}
	}
}
