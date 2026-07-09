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
	private World EcsWorld => _ecsWorldAccessor.Current;

	private List<CoordinateSaveData> _lastLoadedCoordinates = new();

	public SaveLoadService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public List<CoordinateSaveData> GetLastLoadedCoordinates() => _lastLoadedCoordinates;

	public bool SaveMapToFile(
		string absolutePath,
		string[] htmlColors,
		(Entity Entity, float RotationY, float Scale)[] unitsData,
		(Entity Entity, float RotationY, float Scale)[] propsData,
		(string DecalId, System.Numerics.Vector3 Position, float RotationY, float Scale)[] decalsData,
		List<CoordinateSaveData> coordinatesData = null)
	{
		try
		{
			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity != Entity.Null)
			{
				if (EcsWorld.Has<TerrainColorsState>(worldEntity))
				{
					EcsWorld.Set(worldEntity, new TerrainColorsState(htmlColors));
				}
				else
				{
					EcsWorld.Add(worldEntity, new TerrainColorsState(htmlColors));
				}
			}

			foreach (var u in unitsData)
			{
				if (EcsWorld.IsAlive(u.Entity))
				{
					if (EcsWorld.Has<RotationY>(u.Entity))
					{
						EcsWorld.Set(u.Entity, new RotationY(u.RotationY));
					}
					else
					{
						EcsWorld.Add(u.Entity, new RotationY(u.RotationY));
					}

					if (EcsWorld.Has<ModelScale>(u.Entity))
					{
						EcsWorld.Set(u.Entity, new ModelScale(u.Scale));
					}
					else
					{
						EcsWorld.Add(u.Entity, new ModelScale(u.Scale));
					}
				}
			}

			foreach (var p in propsData)
			{
				if (EcsWorld.IsAlive(p.Entity))
				{
					if (EcsWorld.Has<RotationY>(p.Entity))
					{
						EcsWorld.Set(p.Entity, new RotationY(p.RotationY));
					}
					else
					{
						EcsWorld.Add(p.Entity, new RotationY(p.RotationY));
					}

					if (EcsWorld.Has<ModelScale>(p.Entity))
					{
						EcsWorld.Set(p.Entity, new ModelScale(p.Scale));
					}
					else
					{
						EcsWorld.Add(p.Entity, new ModelScale(p.Scale));
					}
				}
			}

			var tempDecals = new List<Entity>();
			foreach (var d in decalsData)
			{
				var ent = EcsWorld.Create();
				EcsWorld.Add(ent, new DecalIdentity(d.DecalId));
				EcsWorld.Add(ent, new Position(d.Position));
				EcsWorld.Add(ent, new RotationY(d.RotationY));
				EcsWorld.Add(ent, new ModelScale(d.Scale));
				tempDecals.Add(ent);
			}

			TerrainState terrain = default;
			EditorState editor = default;
			TerrainColorsState colorsState = default;
			bool foundWorld = false;
			bool foundColors = false;

			var worldQuery2 = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
			EcsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
				if (EcsWorld.Has<TerrainColorsState>(entity))
				{
					colorsState = EcsWorld.Get<TerrainColorsState>(entity);
					foundColors = true;
				}
			});

			if (!foundWorld) return false;

			var saveData = new MapSaveData();
			saveData.WaterEnabled = terrain.WaterEnabled;
			saveData.WaterHeight = terrain.WaterHeight;
			saveData.CameraBoundsLeft = editor.CameraBoundsLeft;
			saveData.CameraBoundsRight = editor.CameraBoundsRight;
			saveData.CameraBoundsTop = editor.CameraBoundsTop;
			saveData.CameraBoundsBottom = editor.CameraBoundsBottom;
			saveData.SkyboxPath = editor.SkyboxPath;
			saveData.Coordinates = coordinatesData ?? new List<CoordinateSaveData>();

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
			EcsWorld.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position pos, ref Owner owner) =>
			{
				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

				bool isEnemy = false;
				if (EcsWorld.IsAlive(owner.PlayerEntity.Value) && EcsWorld.Has<Name>(owner.PlayerEntity.Value))
				{
					isEnemy = EcsWorld.Get<Name>(owner.PlayerEntity.Value).Value == "Enemy_AI";
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
			EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity propId, ref Position pos) =>
			{
				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

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
			EcsWorld.Query(in decalQuery, (Entity entity, ref DecalIdentity decalId, ref Position pos) =>
			{
				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

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
				if (EcsWorld.IsAlive(ent))
				{
					EcsWorld.Destroy(ent);
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

				EcsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
				{
					EcsWorld.Set(entity, updatedEditor);
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

			_lastLoadedCoordinates = saveData.Coordinates ?? new List<CoordinateSaveData>();

			var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
			var unitsToDestroy = new List<Entity>();
			EcsWorld.Query(in unitQuery, (Entity entity) => unitsToDestroy.Add(entity));
			foreach (var ent in unitsToDestroy) EcsWorld.Destroy(ent);

			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			var propsToDestroy = new List<Entity>();
			EcsWorld.Query(in propQuery, (Entity entity) => propsToDestroy.Add(entity));
			foreach (var ent in propsToDestroy) EcsWorld.Destroy(ent);

			var decalQuery = Realm.Ecs.Common.QueryCache.AllDecalIdentityAndPositionQuery;
			var decalsToDestroy = new List<Entity>();
			EcsWorld.Query(in decalQuery, (Entity entity) => decalsToDestroy.Add(entity));
			foreach (var ent in decalsToDestroy) EcsWorld.Destroy(ent);

			var req1 = Realm.Ecs.Common.QueryCache.AllUnitSpawnRequestQuery;
			var req1List = new List<Entity>();
			EcsWorld.Query(in req1, (Entity entity) => req1List.Add(entity));
			foreach (var ent in req1List) EcsWorld.Destroy(ent);

			var req2 = Realm.Ecs.Common.QueryCache.AllPropSpawnRequestQuery;
			var req2List = new List<Entity>();
			EcsWorld.Query(in req2, (Entity entity) => req2List.Add(entity));
			foreach (var ent in req2List) EcsWorld.Destroy(ent);

			var req3 = Realm.Ecs.Common.QueryCache.AllDecalSpawnRequestQuery;
			var req3List = new List<Entity>();
			EcsWorld.Query(in req3, (Entity entity) => req3List.Add(entity));
			foreach (var ent in req3List) EcsWorld.Destroy(ent);

			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity == Entity.Null)
			{
				worldEntity = EcsWorld.Create();
			}

			if (!EcsWorld.Has<TerrainState>(worldEntity))
			{
				EcsWorld.Add(worldEntity, new TerrainState(126, 126, 2.0f, 5.0f / 2.5f / 10.0f, -2.0f, true, new float[126, 126], new int[126, 126], null, null));
			}

			if (EcsWorld.Has<TerrainState>(worldEntity))
			{
				ref var ts = ref EcsWorld.Get<TerrainState>(worldEntity);
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

				EcsWorld.Set(worldEntity, ts);
			}

			if (saveData.Colors != null)
			{
				if (EcsWorld.Has<TerrainColorsState>(worldEntity))
				{
					EcsWorld.Set(worldEntity, new TerrainColorsState(saveData.Colors));
				}
				else
				{
					EcsWorld.Add(worldEntity, new TerrainColorsState(saveData.Colors));
				}
			}

			bool isBlock = true;
			float step = 4.0f;
			float left = saveData.CameraBoundsLeft ?? -95.0f;
			float right = saveData.CameraBoundsRight ?? 95.0f;
			float top = saveData.CameraBoundsTop ?? -95.0f;
			float bottom = saveData.CameraBoundsBottom ?? 125.0f;
			string skybox = saveData.SkyboxPath ?? "res://Assets/skybox_panoramic.jpg";

			var newEditorState = new EditorState(isBlock, step, left, right, top, bottom, skybox, false);
			if (EcsWorld.Has<EditorState>(worldEntity))
			{
				EcsWorld.Set(worldEntity, newEditorState);
			}
			else
			{
				EcsWorld.Add(worldEntity, newEditorState);
			}

			if (!terrainOnly)
			{
				if (saveData.Units != null)
				{
					foreach (var u in saveData.Units)
					{
						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new UnitSpawnRequest(
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
						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new PropSpawnRequest(
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
						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new DecalSpawnRequest(
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
