using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Arch.Core;
using Godot;
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
		List<CoordinateSaveData> coordinatesData = null,
		string[] cliffHtmlColors = null)
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
			bool foundWorld = false;

			var worldQuery2 = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
			EcsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			var saveData = new MapSaveData();
			saveData.Width = terrain.Width;
			saveData.Depth = terrain.Depth;
			saveData.CameraBoundsLeft = editor.CameraBoundsLeft;
			saveData.CameraBoundsRight = editor.CameraBoundsRight;
			saveData.CameraBoundsTop = editor.CameraBoundsTop;
			saveData.CameraBoundsBottom = editor.CameraBoundsBottom;
			saveData.SkyboxPath = editor.SkyboxPath;
			saveData.Coordinates = coordinatesData ?? new List<CoordinateSaveData>();

			int width = terrain.Width;
			int depth = terrain.Depth;

			string directory = Path.GetDirectoryName(absolutePath);
			if (!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			string heightsPath = Path.Combine(directory, "terrain_heights.exr");
			string waterPath = Path.Combine(directory, "terrain_water.exr");
			string splatIndicesPath = Path.Combine(directory, "terrain_splat_indices.exr");
			string splatWeightsPath = Path.Combine(directory, "terrain_splat_weights.exr");
			string pathingPath = Path.Combine(directory, "terrain_pathing.png");

			var cells = terrain.Cells;
			byte[] heightsBytes = new byte[width * depth * 4 * sizeof(float)];
			Span<float> heightsSpan = MemoryMarshal.Cast<byte, float>(heightsBytes.AsSpan());

			byte[] waterBytes = new byte[width * depth * 4 * sizeof(float)];
			Span<float> waterSpan = MemoryMarshal.Cast<byte, float>(waterBytes.AsSpan());

			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					var cell = cells != null ? cells[x, z] : default;
					int baseIdx = (z * width + x) * 4;

					heightsSpan[baseIdx + 0] = cell.Y_NW;
					heightsSpan[baseIdx + 1] = cell.Y_NE;
					heightsSpan[baseIdx + 2] = cell.Y_SE;
					heightsSpan[baseIdx + 3] = cell.Y_SW;

					waterSpan[baseIdx + 0] = (float)cell.WaterMode;
					waterSpan[baseIdx + 1] = 0f;
					waterSpan[baseIdx + 2] = 0f;
					waterSpan[baseIdx + 3] = 1f;
				}
			}

			Image heightsImage = Image.CreateFromData(width, depth, false, Image.Format.Rgbaf, heightsBytes);
			Image waterImage = Image.CreateFromData(width, depth, false, Image.Format.Rgbaf, waterBytes);
			heightsImage.SaveExr(heightsPath);
			waterImage.SaveExr(waterPath);

			byte[] pathingBytes = new byte[width * depth * 4];
			Span<byte> pathingSpan = pathingBytes.AsSpan();

			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int code = terrain.PathingCodes != null ? terrain.PathingCodes[x, z] : EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
					int baseIdx = (z * width + x) * 4;

					pathingSpan[baseIdx + 0] = (byte)code;
					pathingSpan[baseIdx + 1] = 0;
					pathingSpan[baseIdx + 2] = 0;
					pathingSpan[baseIdx + 3] = 255;
				}
			}

			Image pathingImage = Image.CreateFromData(width, depth, false, Image.Format.Rgba8, pathingBytes);
			pathingImage.SavePng(pathingPath);

			int splatW = width;
			int splatD = depth;
			if (htmlColors != null && htmlColors.Length == (width + 1) * (depth + 1))
			{
				splatW = width + 1;
				splatD = depth + 1;
			}

			byte[] splatIndicesBytes = new byte[splatW * splatD * 4 * sizeof(float)];
			Span<float> splatIndicesSpan = MemoryMarshal.Cast<byte, float>(splatIndicesBytes.AsSpan());

			byte[] splatWeightsBytes = new byte[splatW * splatD * 4 * sizeof(float)];
			Span<float> splatWeightsSpan = MemoryMarshal.Cast<byte, float>(splatWeightsBytes.AsSpan());

			for (int z = 0; z < splatD; z++)
			{
				for (int x = 0; x < splatW; x++)
				{
					int idx = z * splatW + x;
					string serialized = (htmlColors != null && idx < htmlColors.Length) ? htmlColors[idx] : null;
					TerrainSplatWeights s = TerrainSplatWeights.Deserialize(serialized);
					int baseIdx = idx * 4;

					splatIndicesSpan[baseIdx + 0] = s.Index0;
					splatIndicesSpan[baseIdx + 1] = s.Index1;
					splatIndicesSpan[baseIdx + 2] = s.Index2;
					splatIndicesSpan[baseIdx + 3] = s.Index3;

					splatWeightsSpan[baseIdx + 0] = s.Weight0;
					splatWeightsSpan[baseIdx + 1] = s.Weight1;
					splatWeightsSpan[baseIdx + 2] = s.Weight2;
					splatWeightsSpan[baseIdx + 3] = s.Weight3;
				}
			}

			Image splatIndicesImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, splatIndicesBytes);
			Image splatWeightsImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, splatWeightsBytes);
			splatIndicesImage.SaveExr(splatIndicesPath);
			splatWeightsImage.SaveExr(splatWeightsPath);

			if (cliffHtmlColors != null && cliffHtmlColors.Length == splatW * splatD)
			{
				string cliffSplatIndicesPath = Path.Combine(directory, "terrain_cliff_splat_indices.exr");
				string cliffSplatWeightsPath = Path.Combine(directory, "terrain_cliff_splat_weights.exr");

				byte[] cliffIndicesBytes = new byte[splatW * splatD * 4 * sizeof(float)];
				Span<float> cliffIndicesSpan = MemoryMarshal.Cast<byte, float>(cliffIndicesBytes.AsSpan());

				byte[] cliffWeightsBytes = new byte[splatW * splatD * 4 * sizeof(float)];
				Span<float> cliffWeightsSpan = MemoryMarshal.Cast<byte, float>(cliffWeightsBytes.AsSpan());

				for (int z = 0; z < splatD; z++)
				{
					for (int x = 0; x < splatW; x++)
					{
						int idx = z * splatW + x;
						string serialized = cliffHtmlColors[idx];
						TerrainSplatWeights s = TerrainSplatWeights.Deserialize(serialized);
						int baseIdx = idx * 4;

						cliffIndicesSpan[baseIdx + 0] = s.Index0;
						cliffIndicesSpan[baseIdx + 1] = s.Index1;
						cliffIndicesSpan[baseIdx + 2] = s.Index2;
						cliffIndicesSpan[baseIdx + 3] = s.Index3;

						cliffWeightsSpan[baseIdx + 0] = s.Weight0;
						cliffWeightsSpan[baseIdx + 1] = s.Weight1;
						cliffWeightsSpan[baseIdx + 2] = s.Weight2;
						cliffWeightsSpan[baseIdx + 3] = s.Weight3;
					}
				}

				Image cliffIndicesImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, cliffIndicesBytes);
				Image cliffWeightsImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, cliffWeightsBytes);
				cliffIndicesImage.SaveExr(cliffSplatIndicesPath);
				cliffWeightsImage.SaveExr(cliffSplatWeightsPath);
			}

			saveData.Units = new List<UnitSaveData>();
			var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
			EcsWorld.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position pos, ref Owner owner) =>
			{
				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

				int playerIndex = 0;
				if (EcsWorld.Has<UnitOwnerPlayer>(entity))
				{
					playerIndex = EcsWorld.Get<UnitOwnerPlayer>(entity).PlayerIndex;
				}

				bool isEnemy = NetworkService.ArePlayerIndicesEnemies(GameHost.Instance?.LocalPlayerIndex ?? 0, playerIndex);
				if (EcsWorld.Has<UnitFaction>(entity))
				{
					isEnemy = EcsWorld.Get<UnitFaction>(entity).IsEnemy;
				}

				saveData.Units.Add(new UnitSaveData
				{
					UnitId = defId.Value,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationY = rotY,
					Scale = scale,
					IsEnemy = isEnemy,
					Player = playerIndex
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
			MapJsonFormatter.SaveFormattedJson(absolutePath, json);

			GameHost.Instance?.SaveModelYOffsetsToMetadataJson(directory);

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
					editor.MirrorMode,
					editor.WaterMode
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

			string mapDir = Path.GetDirectoryName(absolutePath);
			GameHost.Instance?.LoadModelYOffsetsFromMetadataJson(mapDir);

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

			int width = saveData.Width > 0 ? Math.Clamp((int)Math.Round(saveData.Width / 32.0) * 32, 32, 512) : 128;
			int depth = saveData.Depth > 0 ? Math.Clamp((int)Math.Round(saveData.Depth / 32.0) * 32, 32, 512) : 128;

			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity == Entity.Null)
			{
				worldEntity = EcsWorld.Create();
			}

			if (!EcsWorld.Has<TerrainState>(worldEntity))
			{
				EcsWorld.Add(worldEntity, new TerrainState(width, depth, TerrainState.DefaultQuadSize, TerrainState.DefaultCellSize, new TerrainCell[width, depth], new int[width, depth], null, null));
			}
			else
			{
				ref var ts = ref EcsWorld.Get<TerrainState>(worldEntity);
				ts.Width = width;
				ts.Depth = depth;
				ts.Cells = new TerrainCell[width, depth];
				ts.PathingCodes = new int[width, depth];
				EcsWorld.Set(worldEntity, ts);
			}

			if (EcsWorld.Has<TerrainState>(worldEntity))
			{
				ref var ts = ref EcsWorld.Get<TerrainState>(worldEntity);

				if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
				{
					ts.Cells = new TerrainCell[width, depth];
				}
				if (ts.PathingCodes == null)
				{
					ts.PathingCodes = new int[width, depth];
				}

				if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
				{
					ts.Cells = new TerrainCell[width, depth];
				}

				string directory = Path.GetDirectoryName(absolutePath);
				string heightsPath = Path.Combine(directory, "terrain_heights.exr");
				bool heightsLoaded = false;

				if (File.Exists(heightsPath))
				{
					Image heightsImage = Image.LoadFromFile(heightsPath);
					if (heightsImage != null)
					{
						heightsImage.Convert(Image.Format.Rgbaf);
						int imgW = heightsImage.GetWidth();
						int imgH = heightsImage.GetHeight();
						ReadOnlySpan<float> floatData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(heightsImage.GetData());

						if (imgW == width && imgH == depth)
						{
							for (int z = 0; z < depth; z++)
							{
								for (int x = 0; x < width; x++)
								{
									int baseIdx = (z * imgW + x) * 4;
									float yNW = floatData[baseIdx + 0];
									float yNE = floatData[baseIdx + 1];
									float ySE = floatData[baseIdx + 2];
									float ySW = floatData[baseIdx + 3];
									ts.Cells[x, z] = new TerrainCell(yNW, yNE, ySE, ySW);
								}
							}
							heightsLoaded = true;
						}
					}
				}

				if (!heightsLoaded)
				{
					ts.Cells = new TerrainCell[width, depth];
				}

				string waterPath = Path.Combine(directory, "terrain_water.exr");
				if (File.Exists(waterPath))
				{
					Image waterImage = Image.LoadFromFile(waterPath);
					if (waterImage != null)
					{
						waterImage.Convert(Image.Format.Rgbaf);
						int imgW = waterImage.GetWidth();
						int imgH = waterImage.GetHeight();
						ReadOnlySpan<float> waterFloatData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(waterImage.GetData());

						if (imgW == width && imgH == depth)
						{
							for (int z = 0; z < depth; z++)
							{
								for (int x = 0; x < width; x++)
								{
									int baseIdx = (z * imgW + x) * 4;
									var wMode = (WaterType)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 0]), 0, 2);
									ts.Cells[x, z].WaterMode = wMode;
								}
							}
						}
					}
				}

				string pathingPath = Path.Combine(directory, "terrain_pathing.png");
				bool pathingLoaded = false;
				if (File.Exists(pathingPath))
				{
					Image pathingImage = Image.LoadFromFile(pathingPath);
					if (pathingImage != null)
					{
						pathingImage.Convert(Image.Format.Rgba8);
						int imgW = pathingImage.GetWidth();
						int imgH = pathingImage.GetHeight();
						ReadOnlySpan<byte> byteData = pathingImage.GetData();

						for (int z = 0; z < depth; z++)
						{
							for (int x = 0; x < width; x++)
							{
								int imgX = Math.Clamp(x, 0, imgW - 1);
								int imgZ = Math.Clamp(z, 0, imgH - 1);
								int baseIdx = (imgZ * imgW + imgX) * 4;
								ts.PathingCodes[x, z] = byteData[baseIdx + 0];
							}
						}
						pathingLoaded = true;
					}
				}
				if (!pathingLoaded)
				{
					for (int z = 0; z < depth; z++)
					{
						for (int x = 0; x < width; x++)
						{
							ts.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
						}
					}
				}

				EcsWorld.Set(worldEntity, ts);
			}

			string splatIndicesPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_indices.exr");
			string splatWeightsPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_weights.exr");
			
			string[] loadedColors = null;

			if (File.Exists(splatIndicesPath) && File.Exists(splatWeightsPath))
			{
				Image splatIndicesImage = Image.LoadFromFile(splatIndicesPath);
				Image splatWeightsImage = Image.LoadFromFile(splatWeightsPath);
				if (splatIndicesImage != null && splatWeightsImage != null)
				{
					splatIndicesImage.Convert(Image.Format.Rgbaf);
					splatWeightsImage.Convert(Image.Format.Rgbaf);
					int idxW = splatIndicesImage.GetWidth();
					int idxH = splatIndicesImage.GetHeight();
					int wgtW = splatWeightsImage.GetWidth();
					int wgtH = splatWeightsImage.GetHeight();

					ReadOnlySpan<float> idxData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(splatIndicesImage.GetData());
					ReadOnlySpan<float> wgtData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(splatWeightsImage.GetData());

					int splatW = idxW;
					int splatD = idxH;
					loadedColors = new string[splatW * splatD];
					for (int z = 0; z < splatD; z++)
					{
						for (int x = 0; x < splatW; x++)
						{
							int imgX = Math.Clamp(x, 0, idxW - 1);
							int imgZ = Math.Clamp(z, 0, idxH - 1);
							int idxOffset = (imgZ * idxW + imgX) * 4;

							int imgWeightX = Math.Clamp(x, 0, wgtW - 1);
							int imgWeightZ = Math.Clamp(z, 0, wgtH - 1);
							int weightOffset = (imgWeightZ * wgtW + imgWeightX) * 4;

							int i0 = (int)Math.Round(idxData[idxOffset + 0]);
							int i1 = (int)Math.Round(idxData[idxOffset + 1]);
							int i2 = (int)Math.Round(idxData[idxOffset + 2]);
							int i3 = (int)Math.Round(idxData[idxOffset + 3]);

							float w0 = wgtData[weightOffset + 0];
							float w1 = wgtData[weightOffset + 1];
							float w2 = wgtData[weightOffset + 2];
							float w3 = wgtData[weightOffset + 3];

							var s = new TerrainSplatWeights
							{
								Index0 = i0,
								Index1 = i1,
								Index2 = i2,
								Index3 = i3,
								Weight0 = w0,
								Weight1 = w1,
								Weight2 = w2,
								Weight3 = w3
							};

							loadedColors[z * splatW + x] = s.Serialize();
						}
					}
				}
			}

			if (loadedColors == null)
			{
				loadedColors = new string[width * depth];
				string defaultSolid = TerrainSplatWeights.CreateSolid(3).Serialize();
				for (int i = 0; i < loadedColors.Length; i++)
				{
					loadedColors[i] = defaultSolid;
				}
			}

			if (loadedColors != null)
			{
				if (EcsWorld.Has<TerrainColorsState>(worldEntity))
				{
					EcsWorld.Set(worldEntity, new TerrainColorsState(loadedColors));
				}
				else
				{
					EcsWorld.Add(worldEntity, new TerrainColorsState(loadedColors));
				}
			}

			bool isBlock = true;
			float step = EditableTerrain.TIER_HEIGHT;
			float left = saveData.CameraBoundsLeft ?? -95.0f;
			float right = saveData.CameraBoundsRight ?? 95.0f;
			float top = saveData.CameraBoundsTop ?? -95.0f;
			float bottom = saveData.CameraBoundsBottom ?? 125.0f;
			string skybox = saveData.SkyboxPath ?? "Assets/skyboxes/jade_shrine.png";

			WaterType currentWaterMode = EcsWorld.Has<EditorState>(worldEntity) ? EcsWorld.Get<EditorState>(worldEntity).WaterMode : WaterType.None;
			var newEditorState = new EditorState(isBlock, step, left, right, top, bottom, skybox, false, MirrorMode.None, currentWaterMode);
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
						string cleanUnitId = StripIdPath(u.UnitId);
						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new UnitSpawnRequest(
							cleanUnitId,
							new System.Numerics.Vector3(u.PosX, u.PosY, u.PosZ),
							u.RotationY,
							u.Scale,
							u.IsEnemy,
							u.Player
						));
					}
				}

				if (saveData.Props != null)
				{
					foreach (var p in saveData.Props)
					{
						string cleanPropId = StripIdPath(p.PropId);
						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new PropSpawnRequest(
							cleanPropId,
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

	private static string StripIdPath(string id)
	{
		if (string.IsNullOrEmpty(id)) return id;
		string directory = Path.GetDirectoryName(id);
		return string.IsNullOrEmpty(directory) ? id : Path.GetFileName(id);
	}

	public static void RemapSplatExrFiles(string mapDirectory, IReadOnlyDictionary<int, int> remap)
	{
		if (string.IsNullOrEmpty(mapDirectory) || remap == null || remap.Count == 0) return;

		string[] indicesFiles = new[]
		{
			Path.Combine(mapDirectory, "terrain_splat_indices.exr"),
			Path.Combine(mapDirectory, "terrain_cliff_splat_indices.exr")
		};

		foreach (string file in indicesFiles)
		{
			if (File.Exists(file))
			{
				try
				{
					var img = Image.LoadFromFile(file);
					if (img != null)
					{
						img.Convert(Image.Format.Rgbaf);
						int w = img.GetWidth();
						int h = img.GetHeight();
						byte[] data = img.GetData();
						Span<float> floats = MemoryMarshal.Cast<byte, float>(data.AsSpan());
						bool modified = false;
						for (int i = 0; i < floats.Length; i++)
						{
							int oldIdx = (int)MathF.Round(floats[i]);
							if (remap.TryGetValue(oldIdx, out int newIdx) && newIdx != oldIdx)
							{
								floats[i] = newIdx;
								modified = true;
							}
						}
						if (modified)
						{
							var updatedImg = Image.CreateFromData(w, h, false, Image.Format.Rgbaf, data);
							updatedImg.SaveExr(file);
						}
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SaveLoadService] Failed to remap splat EXR {file}: {ex.Message}");
				}
			}
		}
	}
}

