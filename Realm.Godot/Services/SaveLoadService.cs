using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Godot.Utils;
using Realm.Shared.Metadata;

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
		(Entity Entity, System.Numerics.Vector3 Position, float RotationY, float Scale)[] decalsData,
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
						float existing = EcsWorld.Get<RotationY>(u.Entity).Value;
						if (MathF.Abs(existing - u.RotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - u.RotationY) - 360f) > 0.001f)
						{
							EcsWorld.Set(u.Entity, new RotationY(u.RotationY));
						}
					}
					else
					{
						EcsWorld.Add(u.Entity, new RotationY(u.RotationY));
					}

					if (EcsWorld.Has<ModelScale>(u.Entity))
					{
						float existing = EcsWorld.Get<ModelScale>(u.Entity).Value;
						if (MathF.Abs(existing - u.Scale) > 0.0001f)
						{
							EcsWorld.Set(u.Entity, new ModelScale(u.Scale));
						}
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
						float existing = EcsWorld.Get<RotationY>(p.Entity).Value;
						if (MathF.Abs(existing - p.RotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - p.RotationY) - 360f) > 0.001f)
						{
							EcsWorld.Set(p.Entity, new RotationY(p.RotationY));
						}
					}
					else
					{
						EcsWorld.Add(p.Entity, new RotationY(p.RotationY));
					}

					if (EcsWorld.Has<ModelScale>(p.Entity))
					{
						float existing = EcsWorld.Get<ModelScale>(p.Entity).Value;
						if (MathF.Abs(existing - p.Scale) > 0.0001f)
						{
							EcsWorld.Set(p.Entity, new ModelScale(p.Scale));
						}
					}
					else
					{
						EcsWorld.Add(p.Entity, new ModelScale(p.Scale));
					}
				}
			}

			var validDecalEntities = decalsData != null
				? new HashSet<Entity>(decalsData.Select(d => d.Entity))
				: null;

			if (decalsData != null)
			{
				foreach (var d in decalsData)
				{
					if (EcsWorld.IsAlive(d.Entity))
					{
						if (EcsWorld.Has<Position>(d.Entity))
						{
							var existingPos = EcsWorld.Get<Position>(d.Entity).Value;
							if ((d.Position - existingPos).Length() > 0.0001f)
							{
								EcsWorld.Set(d.Entity, new Position(d.Position));
							}
						}
						else
						{
							EcsWorld.Add(d.Entity, new Position(d.Position));
						}

						if (EcsWorld.Has<RotationY>(d.Entity))
						{
							float existing = EcsWorld.Get<RotationY>(d.Entity).Value;
							if (MathF.Abs(existing - d.RotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - d.RotationY) - 360f) > 0.001f)
							{
								EcsWorld.Set(d.Entity, new RotationY(d.RotationY));
							}
						}
						else
						{
							EcsWorld.Add(d.Entity, new RotationY(d.RotationY));
						}

						if (EcsWorld.Has<ModelScale>(d.Entity))
						{
							float existing = EcsWorld.Get<ModelScale>(d.Entity).Value;
							if (MathF.Abs(existing - d.Scale) > 0.0001f)
							{
								EcsWorld.Set(d.Entity, new ModelScale(d.Scale));
							}
						}
						else
						{
							EcsWorld.Add(d.Entity, new ModelScale(d.Scale));
						}
					}
				}
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
			else
			{
				int maxBackups = EditorSettingsDialog.CurrentSettings?.MaxBackupSnapshots ?? 3;
				CreateWorkspaceBackup(directory, maxBackups);
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
				if (!IsValidUnitObjectId(defId.Value, directory))
				{
					return;
				}

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
				if (!IsValidPropObjectId(propId.PropId, directory))
				{
					return;
				}

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
			var orphanedDecalEntities = new List<Entity>();
			var savedDecalFingerprints = new HashSet<string>();
			EcsWorld.Query(in decalQuery, (Entity entity, ref DecalIdentity decalId, ref Position pos) =>
			{
				if (validDecalEntities != null && !validDecalEntities.Contains(entity))
				{
					orphanedDecalEntities.Add(entity);
					return;
				}

				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

				string fingerprint = $"{decalId.DecalId}_{pos.Value.X:F3}_{pos.Value.Y:F3}_{pos.Value.Z:F3}_{rotY:F2}_{scale:F3}";
				if (!savedDecalFingerprints.Add(fingerprint))
				{
					orphanedDecalEntities.Add(entity);
					return;
				}

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

			foreach (var orphan in orphanedDecalEntities)
			{
				if (EcsWorld.IsAlive(orphan))
				{
					EcsWorld.Destroy(orphan);
				}
			}

			SortMapSaveData(saveData);

			var saveDoc = JsonSerializer.SerializeToNode(saveData) as JsonObject;
			if (saveDoc != null)
			{
				CleanTerrainJsonSchema(saveDoc);
				MapJsonFormatter.SaveFormattedJson(absolutePath, saveDoc);
			}
			else
			{
				string json = JsonSerializer.Serialize(saveData);
				MapJsonFormatter.SaveFormattedJson(absolutePath, json);
			}

			GameHost.Instance?.SaveModelYOffsetsToMetadataJson(directory);

			SyncMetadataAssetsAndPrune(directory);

			string metaPath = Path.Combine(directory, "metadata.json");
			if (File.Exists(metaPath))
			{
				try
				{
					var metaRoot = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject;
					if (metaRoot != null)
					{
						CleanMetadataJsonSchema(metaRoot);
						MapJsonFormatter.SaveFormattedJson(metaPath, metaRoot);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SaveLoadService] CleanMetadataJson error: {ex.Message}");
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
						if (!IsValidUnitObjectId(cleanUnitId, mapDir))
						{
							GD.PushWarning($"[SaveLoadService] Ignored invalid unit '{u.UnitId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
							continue;
						}

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
						if (!IsValidPropObjectId(cleanPropId, mapDir))
						{
							GD.PushWarning($"[SaveLoadService] Ignored invalid prop '{p.PropId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
							continue;
						}

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
					var loadedDecalFingerprints = new HashSet<string>();
					foreach (var d in saveData.Decals)
					{
						string fingerprint = $"{d.DecalId}_{d.PosX:F3}_{d.PosY:F3}_{d.PosZ:F3}_{d.RotationY:F2}_{d.Scale:F3}";
						if (!loadedDecalFingerprints.Add(fingerprint))
						{
							continue;
						}

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

	public static void SortMapSaveData(MapSaveData saveData)
	{
		if (saveData == null) return;

		int width = saveData.Width > 0 ? saveData.Width : 128;
		int depth = saveData.Depth > 0 ? saveData.Depth : 128;
		float topLeftX = -width / 2.0f;
		float topLeftZ = -depth / 2.0f;

		if (saveData.Units != null)
		{
			saveData.Units.Sort((a, b) =>
			{
				int comparison = string.Compare(a.UnitId, b.UnitId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.UnitId, b.UnitId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				comparison = a.Scale.CompareTo(b.Scale);
				if (comparison != 0) return comparison;
				comparison = a.Player.CompareTo(b.Player);
				if (comparison != 0) return comparison;
				return a.IsEnemy.CompareTo(b.IsEnemy);
			});
		}

		if (saveData.Props != null)
		{
			saveData.Props.Sort((a, b) =>
			{
				int comparison = string.Compare(a.PropId, b.PropId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.PropId, b.PropId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				return a.Scale.CompareTo(b.Scale);
			});
		}

		if (saveData.Decals != null)
		{
			saveData.Decals.Sort((a, b) =>
			{
				int comparison = string.Compare(a.DecalId, b.DecalId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.DecalId, b.DecalId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				return a.Scale.CompareTo(b.Scale);
			});
		}

		if (saveData.Coordinates != null)
		{
			saveData.Coordinates.Sort((a, b) =>
			{
				int comparison = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float centerAX = (a.MinX + a.MaxX) * 0.5f;
				float centerAZ = (a.MinZ + a.MaxZ) * 0.5f;
				float centerBX = (b.MinX + b.MaxX) * 0.5f;
				float centerBZ = (b.MinZ + b.MaxZ) * 0.5f;

				float distanceA = MathF.Sqrt(MathF.Pow(centerAX - topLeftX, 2) + MathF.Pow(centerAZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(centerBX - topLeftX, 2) + MathF.Pow(centerBZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.MinX.CompareTo(b.MinX);
				if (comparison != 0) return comparison;
				comparison = a.MinZ.CompareTo(b.MinZ);
				if (comparison != 0) return comparison;
				comparison = a.MaxX.CompareTo(b.MaxX);
				if (comparison != 0) return comparison;
				return a.MaxZ.CompareTo(b.MaxZ);
			});
		}
	}

	public static string CreateWorkspaceBackup(string workspacePath, int maxBackups = 3)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
			return string.Empty;

		try
		{
			string wsName = Path.GetFileName(workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(wsName)) wsName = MapWorkspaceService.DefaultWorkspaceFolder;

			string backupsRoot = Path.Combine(OS.GetUserDataDir(), "map_backups", wsName);
			if (!Directory.Exists(backupsRoot))
			{
				Directory.CreateDirectory(backupsRoot);
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
			string targetBackupDir = Path.Combine(backupsRoot, $"backup_{timestamp}");

			Directory.CreateDirectory(targetBackupDir);

			CopyDirectoryContentsSafe(workspacePath, targetBackupDir);

			PruneOldBackups(backupsRoot, maxBackups);

			return targetBackupDir;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] Failed to create workspace backup: {ex.Message}");
			return string.Empty;
		}
	}

	private static void CopyDirectoryContentsSafe(string sourceDir, string targetDir)
	{
		var source = new DirectoryInfo(sourceDir);
		if (!source.Exists) return;

		var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".git", "bin", "obj", ".godot", ".vs", ".vscode"
		};

		foreach (var dir in source.GetDirectories())
		{
			if (excludedFolders.Contains(dir.Name)) continue;
			string destSubDir = Path.Combine(targetDir, dir.Name);
			Directory.CreateDirectory(destSubDir);
			CopyDirectoryContentsSafe(dir.FullName, destSubDir);
		}

		foreach (var file in source.GetFiles())
		{
			if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
			string destFile = Path.Combine(targetDir, file.Name);
			file.CopyTo(destFile, true);
		}
	}

	private static void PruneOldBackups(string backupsRoot, int maxBackups)
	{
		if (maxBackups < 1) maxBackups = 1;
		try
		{
			var rootDir = new DirectoryInfo(backupsRoot);
			if (!rootDir.Exists) return;

			var backupDirs = rootDir.GetDirectories("backup_*")
				.OrderBy(d => d.CreationTimeUtc)
				.ThenBy(d => d.Name)
				.ToList();

			while (backupDirs.Count > maxBackups)
			{
				var oldest = backupDirs[0];
				backupDirs.RemoveAt(0);
				try
				{
					oldest.Delete(true);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SaveLoadService] Failed to prune old backup {oldest.FullName}: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] PruneOldBackups error: {ex.Message}");
		}
	}

	private static HashSet<string>? _cachedAllowedTerrainTopLevel;
	private static HashSet<string>? _cachedAllowedTerrainUnitProperties;
	private static HashSet<string>? _cachedAllowedTerrainPropProperties;
	private static HashSet<string>? _cachedAllowedTerrainDecalProperties;
	private static HashSet<string>? _cachedAllowedTerrainCoordinateProperties;

	private static HashSet<string>? _cachedAllowedMetadataTopLevel;
	private static HashSet<string>? _cachedAllowedAssetCategories;
	private static HashSet<string>? _cachedAllowedGlbSubCategories;
	private static HashSet<string>? _cachedAllowedGlbItemProperties;
	private static HashSet<string>? _cachedAllowedTextureItemProperties;
	private static HashSet<string>? _cachedAllowedDecalItemProperties;
	private static HashSet<string>? _cachedAllowedVfxItemProperties;
	private static HashSet<string>? _cachedAllowedShaderItemProperties;
	private static HashSet<string>? _cachedAllowedMapProperties;
	private static HashSet<string>? _cachedAllowedEntityItemProperties;
	private static HashSet<string>? _cachedAllowedAbilityItemProperties;
	private static HashSet<string>? _cachedAllowedWeaponItemProperties;
	private static HashSet<string>? _cachedAllowedUpgradeItemProperties;
	private static HashSet<string>? _cachedAllowedCustomItemProperties;

	private static void AddTypeMembersToSet(Type type, HashSet<string> destination)
	{
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			destination.Add(property.Name);
			destination.Add(property.Name.ToLowerInvariant());
			destination.Add(ConvertToSnakeCase(property.Name));
		}
		foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			destination.Add(field.Name);
			destination.Add(field.Name.ToLowerInvariant());
			destination.Add(ConvertToSnakeCase(field.Name));
		}
	}

	private static string ConvertToSnakeCase(string input)
	{
		if (string.IsNullOrEmpty(input)) return input;
		var stringBuilder = new System.Text.StringBuilder();
		for (int index = 0; index < input.Length; index++)
		{
			char character = input[index];
			if (char.IsUpper(character))
			{
				if (index > 0 && input[index - 1] != '_')
				{
					stringBuilder.Append('_');
				}
				stringBuilder.Append(char.ToLowerInvariant(character));
			}
			else
			{
				stringBuilder.Append(character);
			}
		}
		return stringBuilder.ToString();
	}

	private static JsonObject? LoadMapSchemaJson()
	{
		string[] candidatePaths = new[]
		{
			PathUtils.FindPath("Realm.MapEditorExtension/map_schema.json"),
			PathUtils.FindPath("MapTemplate/.vscode/map_schema.json"),
			PathUtils.FindPath(".vscode/map_schema.json")
		};

		foreach (var candidatePath in candidatePaths)
		{
			if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
			{
				try
				{
					return JsonNode.Parse(File.ReadAllText(candidatePath)) as JsonObject;
				}
				catch
				{
				}
			}
		}

		return null;
	}

	private static void ExtractPropertiesFromSchemaNode(JsonNode? node, HashSet<string> destination)
	{
		if (node is JsonObject jsonObject)
		{
			if (jsonObject.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
			{
				foreach (var property in propertiesObject)
				{
					destination.Add(property.Key);
					destination.Add(ConvertToSnakeCase(property.Key));
				}
			}
			if (jsonObject.TryGetPropertyValue("items", out var itemsNode))
			{
				ExtractPropertiesFromSchemaNode(itemsNode, destination);
			}
		}
	}

	private static HashSet<string> GetAllowedTerrainTopLevel()
	{
		if (_cachedAllowedTerrainTopLevel != null) return _cachedAllowedTerrainTopLevel;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(MapSaveData), set);
		_cachedAllowedTerrainTopLevel = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainUnitProperties()
	{
		if (_cachedAllowedTerrainUnitProperties != null) return _cachedAllowedTerrainUnitProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(UnitSaveData), set);
		_cachedAllowedTerrainUnitProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainPropProperties()
	{
		if (_cachedAllowedTerrainPropProperties != null) return _cachedAllowedTerrainPropProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(PropSaveData), set);
		_cachedAllowedTerrainPropProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainDecalProperties()
	{
		if (_cachedAllowedTerrainDecalProperties != null) return _cachedAllowedTerrainDecalProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(DecalSaveData), set);
		_cachedAllowedTerrainDecalProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainCoordinateProperties()
	{
		if (_cachedAllowedTerrainCoordinateProperties != null) return _cachedAllowedTerrainCoordinateProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(CoordinateSaveData), set);
		_cachedAllowedTerrainCoordinateProperties = set;
		return set;
	}

	public static void CleanTerrainJsonSchema(JsonObject root)
	{
		if (root == null) return;

		var allowedTopLevel = GetAllowedTerrainTopLevel();
		var topKeysToRemove = root.Select(keyValuePair => keyValuePair.Key).Where(key => !allowedTopLevel.Contains(key)).ToList();
		foreach (var key in topKeysToRemove)
		{
			root.Remove(key);
		}

		var allowedUnitProperties = GetAllowedTerrainUnitProperties();
		if (root.TryGetPropertyValue("Units", out var unitsNode) && unitsNode is JsonArray unitsArray)
		{
			foreach (var item in unitsArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedUnitProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedPropProperties = GetAllowedTerrainPropProperties();
		if (root.TryGetPropertyValue("Props", out var propsNode) && propsNode is JsonArray propsArray)
		{
			foreach (var item in propsArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedPropProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedDecalProperties = GetAllowedTerrainDecalProperties();
		if (root.TryGetPropertyValue("Decals", out var decalsNode) && decalsNode is JsonArray decalsArray)
		{
			foreach (var item in decalsArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedDecalProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedCoordinateProperties = GetAllowedTerrainCoordinateProperties();
		if (root.TryGetPropertyValue("Coordinates", out var coordinatesNode) && coordinatesNode is JsonArray coordinatesArray)
		{
			foreach (var item in coordinatesArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedCoordinateProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}
	}

	public static bool IsValidPropObjectId(string propId, string mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(propId)) return false;

		if (GameHost.PropRegistry != null && GameHost.PropRegistry.ContainsKey(propId)) return true;
		if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.ContainsKey(propId)) return true;

		string metaPath = !string.IsNullOrEmpty(mapDirectory)
			? Path.Combine(mapDirectory, "metadata.json")
			: Path.Combine(MapWorkspaceService.GetActiveWorkspacePath(), "metadata.json");

		if (File.Exists(metaPath))
		{
			try
			{
				string json = File.ReadAllText(metaPath);
				using var doc = JsonDocument.Parse(json);
				if (doc.RootElement.ValueKind == JsonValueKind.Object)
				{
					if (doc.RootElement.TryGetProperty("CustomProps", out var propsProp) && propsProp.ValueKind == JsonValueKind.Array)
					{
						foreach (var el in propsProp.EnumerateArray())
						{
							if (el.TryGetProperty("UnitId", out var idProp) && propId.Equals(idProp.GetString(), StringComparison.OrdinalIgnoreCase))
								return true;
						}
					}
					if (doc.RootElement.TryGetProperty("CustomResources", out var resProp) && resProp.ValueKind == JsonValueKind.Array)
					{
						foreach (var el in resProp.EnumerateArray())
						{
							if (el.TryGetProperty("UnitId", out var idProp) && propId.Equals(idProp.GetString(), StringComparison.OrdinalIgnoreCase))
								return true;
						}
					}
				}
			}
			catch { }
		}

		return false;
	}

	public static bool IsValidUnitObjectId(string unitId, string mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(unitId)) return false;

		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.ContainsKey(unitId)) return true;
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(unitId)) return true;

		string metaPath = !string.IsNullOrEmpty(mapDirectory)
			? Path.Combine(mapDirectory, "metadata.json")
			: Path.Combine(MapWorkspaceService.GetActiveWorkspacePath(), "metadata.json");

		if (File.Exists(metaPath))
		{
			try
			{
				string json = File.ReadAllText(metaPath);
				using var doc = JsonDocument.Parse(json);
				if (doc.RootElement.ValueKind == JsonValueKind.Object)
				{
					if (doc.RootElement.TryGetProperty("CustomUnits", out var unitsProp) && unitsProp.ValueKind == JsonValueKind.Array)
					{
						foreach (var el in unitsProp.EnumerateArray())
						{
							if (el.TryGetProperty("UnitId", out var idProp) && unitId.Equals(idProp.GetString(), StringComparison.OrdinalIgnoreCase))
								return true;
						}
					}
					if (doc.RootElement.TryGetProperty("CustomBuildings", out var bldProp) && bldProp.ValueKind == JsonValueKind.Array)
					{
						foreach (var el in bldProp.EnumerateArray())
						{
							if (el.TryGetProperty("UnitId", out var idProp) && unitId.Equals(idProp.GetString(), StringComparison.OrdinalIgnoreCase))
								return true;
						}
					}
					bool hasStructuredArrays = doc.RootElement.TryGetProperty("CustomUnits", out _)
						|| doc.RootElement.TryGetProperty("CustomBuildings", out _)
						|| doc.RootElement.TryGetProperty("CustomProps", out _)
						|| doc.RootElement.TryGetProperty("CustomResources", out _);
					if (!hasStructuredArrays)
					{
						foreach (var prop in doc.RootElement.EnumerateObject())
						{
							if (!prop.Name.Equals("MapProperties", StringComparison.OrdinalIgnoreCase) &&
								!prop.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
								unitId.Equals(prop.Name, StringComparison.OrdinalIgnoreCase))
							{
								return true;
							}
						}
					}
				}
			}
			catch { }
		}

		return false;
	}

	private static void AddEnumNamesToSet<T>(HashSet<string> destination) where T : struct, Enum
	{
		foreach (var name in Enum.GetNames<T>())
		{
			destination.Add(name);
			destination.Add(name.ToLowerInvariant());
			destination.Add(ConvertToSnakeCase(name));
		}
	}

	private static HashSet<string> GetAllowedMetadataTopLevel(JsonObject? schemaRoot)
	{
		if (_cachedAllowedMetadataTopLevel != null) return _cachedAllowedMetadataTopLevel;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(Realm.Ecs.Definitions.MapProperties), set);
		set.Add(nameof(Realm.Ecs.Definitions.MapProperties));
		set.Add("map_name");
		set.Add("name");

		foreach (var field in typeof(GameHost).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
		{
			if (field.Name.StartsWith("Model", StringComparison.OrdinalIgnoreCase))
			{
				set.Add(field.Name);
				set.Add(field.Name.ToLowerInvariant());
				set.Add(ConvertToSnakeCase(field.Name));
			}
		}
		set.Add("ModelOffsets");
		set.Add("ModelSpawnShaders");
		set.Add("ModelDeathShaders");
		set.Add("ModelDespawnShaders");
		set.Add("textures");
		set.Add("decals");
		set.Add("vfx_spritesheets");
		set.Add("noise_textures");

		Type[] entityTypes = new[]
		{
			typeof(GameHost.UnitMetadata),
			typeof(GameHost.PropMetadata),
			typeof(GameHost.ResourceMetadata),
			typeof(GameHost.WeaponMetadata),
			typeof(GameHost.AttachmentMetadata),
			typeof(GameHost.AbilityMetadata),
			typeof(GameHost.UpgradeMetadata),
			typeof(GameHost.ItemMetadata)
		};

		foreach (var t in entityTypes)
		{
			string baseName = t.Name;
			if (baseName.EndsWith("Metadata", StringComparison.OrdinalIgnoreCase))
			{
				baseName = baseName[..^"Metadata".Length];
			}

			string plural = baseName.EndsWith("y", StringComparison.OrdinalIgnoreCase)
				? baseName[..^1] + "ies"
				: baseName + "s";

			set.Add(baseName);
			set.Add(plural);
			set.Add("Custom" + baseName);
			set.Add("Custom" + plural);
		}

		foreach (var field in typeof(GameHost).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.Name.EndsWith("Registry", StringComparison.OrdinalIgnoreCase))
			{
				string baseName = field.Name[..^"Registry".Length];
				string plural = baseName.EndsWith("y", StringComparison.OrdinalIgnoreCase)
					? baseName[..^1] + "ies"
					: baseName + "s";

				set.Add(baseName);
				set.Add(plural);
				set.Add("Custom" + baseName);
				set.Add("Custom" + plural);
			}
		}

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
		{
			foreach (var property in propertiesObject)
			{
				set.Add(property.Key);
				set.Add(ConvertToSnakeCase(property.Key));
			}
		}

		_cachedAllowedMetadataTopLevel = set;
		return set;
	}

	private static HashSet<string> GetAllowedAssetCategories(JsonObject? schemaRoot)
	{
		if (_cachedAllowedAssetCategories != null) return _cachedAllowedAssetCategories;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddEnumNamesToSet<GameHost.AssetCategory>(set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
		{
			if (propertiesObject.TryGetPropertyValue("Assets", out var assetsDefinition))
			{
				ExtractPropertiesFromSchemaNode(assetsDefinition, set);
			}
		}

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("Assets", out var assetsDefinition))
			{
				ExtractPropertiesFromSchemaNode(assetsDefinition, set);
			}
		}

		_cachedAllowedAssetCategories = set;
		return set;
	}

	private static HashSet<string> GetAllowedGlbSubCategories(JsonObject? schemaRoot)
	{
		if (_cachedAllowedGlbSubCategories != null) return _cachedAllowedGlbSubCategories;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddEnumNamesToSet<GameHost.GlbSubCategory>(set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("Assets", out var assetsDefinition) && assetsDefinition is JsonObject assetsObj)
			{
				if (assetsObj.TryGetPropertyValue("properties", out var assetsProps) && assetsProps is JsonObject assetsPropsObj)
				{
					if (assetsPropsObj.TryGetPropertyValue("glb", out var glbDefinition))
					{
						ExtractPropertiesFromSchemaNode(glbDefinition, set);
					}
				}
			}
		}

		_cachedAllowedGlbSubCategories = set;
		return set;
	}

	private static HashSet<string> GetAllowedGlbItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedGlbItemProperties != null) return _cachedAllowedGlbItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.GlbItemMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.UnitMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.PropMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.ResourceMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.WeaponMetadata), set);

		set.Add("spawn_shader");
		set.Add("spawnshader");
		set.Add("SpawnShader");
		set.Add("death_shader");
		set.Add("deathshader");
		set.Add("DeathShader");
		set.Add("despawn_shader");
		set.Add("despawnshader");
		set.Add("DespawnShader");

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("EntityItem", out var entityDefinition))
			{
				ExtractPropertiesFromSchemaNode(entityDefinition, set);
			}
		}

		_cachedAllowedGlbItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTextureItemProperties()
	{
		if (_cachedAllowedTextureItemProperties != null) return _cachedAllowedTextureItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(GameHost.TextureMetadata), set);
		AddTypeMembersToSet(typeof(TerrainTextureSnapshot), set);
		_cachedAllowedTextureItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedDecalItemProperties()
	{
		if (_cachedAllowedDecalItemProperties != null) return _cachedAllowedDecalItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(GameHost.DecalMetadata), set);
		AddTypeMembersToSet(typeof(DecalSnapshot), set);
		_cachedAllowedDecalItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedVfxItemProperties()
	{
		if (_cachedAllowedVfxItemProperties != null) return _cachedAllowedVfxItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(GameHost.VfxMetadata), set);
		_cachedAllowedVfxItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedShaderItemProperties()
	{
		if (_cachedAllowedShaderItemProperties != null) return _cachedAllowedShaderItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(CustomShaderConfig), set);
		_cachedAllowedShaderItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedMapProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedMapProperties != null) return _cachedAllowedMapProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(Realm.Ecs.Definitions.MapProperties), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
		{
			if (propertiesObject.TryGetPropertyValue("MapProperties", out var mapPropertiesDefinition))
			{
				ExtractPropertiesFromSchemaNode(mapPropertiesDefinition, set);
			}
		}

		_cachedAllowedMapProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedEntityItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedEntityItemProperties != null) return _cachedAllowedEntityItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.UnitMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.PropMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.ResourceMetadata), set);

		set.Add("spawn_shader");
		set.Add("spawnshader");
		set.Add("SpawnShader");
		set.Add("death_shader");
		set.Add("deathshader");
		set.Add("DeathShader");
		set.Add("despawn_shader");
		set.Add("despawnshader");
		set.Add("DespawnShader");

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("EntityItem", out var entityDefinition))
			{
				ExtractPropertiesFromSchemaNode(entityDefinition, set);
			}
		}

		_cachedAllowedEntityItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedAbilityItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedAbilityItemProperties != null) return _cachedAllowedAbilityItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.AbilityMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomAbilities", out var abilityDefinition))
			{
				ExtractPropertiesFromSchemaNode(abilityDefinition, set);
			}
		}

		_cachedAllowedAbilityItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedWeaponItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedWeaponItemProperties != null) return _cachedAllowedWeaponItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.WeaponMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomWeapons", out var weaponDefinition))
			{
				ExtractPropertiesFromSchemaNode(weaponDefinition, set);
			}
		}

		_cachedAllowedWeaponItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedUpgradeItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedUpgradeItemProperties != null) return _cachedAllowedUpgradeItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.UpgradeMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomUpgrades", out var upgradeDefinition))
			{
				ExtractPropertiesFromSchemaNode(upgradeDefinition, set);
			}
		}

		_cachedAllowedUpgradeItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedCustomItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedCustomItemProperties != null) return _cachedAllowedCustomItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.ItemMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomItems", out var itemDefinition))
			{
				ExtractPropertiesFromSchemaNode(itemDefinition, set);
			}
		}

		_cachedAllowedCustomItemProperties = set;
		return set;
	}

	private static void CleanJsonArrayObjects(JsonArray array, HashSet<string> allowedProperties)
	{
		foreach (var item in array.OfType<JsonObject>())
		{
			var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedProperties.Contains(property)).ToList();
			foreach (var property in propertiesToRemove)
			{
				item.Remove(property);
			}
		}
	}

	private static void CleanTexturesObject(JsonObject texturesObject)
	{
		var allowedTextureItemProperties = GetAllowedTextureItemProperties();
		foreach (var keyValuePair in texturesObject)
		{
			if (keyValuePair.Value is JsonObject itemObject)
			{
				var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedTextureItemProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove)
				{
					itemObject.Remove(property);
				}
			}
		}
	}

	internal static void CleanAssetsObject(JsonObject root, JsonObject? schemaRoot)
	{
		if (!root.TryGetPropertyValue("Assets", out var assetsNode) || assetsNode is not JsonObject assetsObject)
		{
			return;
		}

		var allowedCategories = GetAllowedAssetCategories(schemaRoot);
		var categoryKeysToRemove = assetsObject.Select(keyValuePair => keyValuePair.Key).Where(key => !allowedCategories.Contains(key)).ToList();
		foreach (var key in categoryKeysToRemove)
		{
			assetsObject.Remove(key);
		}

		if (assetsObject.TryGetPropertyValue("glb", out var glbNode) && glbNode is JsonObject glbObject)
		{
			var allowedGlbSubCategories = GetAllowedGlbSubCategories(schemaRoot);
			var subCategoryKeysToRemove = glbObject.Select(keyValuePair => keyValuePair.Key).Where(key => !allowedGlbSubCategories.Contains(key)).ToList();
			foreach (var key in subCategoryKeysToRemove)
			{
				glbObject.Remove(key);
			}

			var allowedGlbItemProperties = GetAllowedGlbItemProperties(schemaRoot);
			foreach (var subCategoryKeyValuePair in glbObject)
			{
				if (subCategoryKeyValuePair.Value is JsonObject subCategoryDictionary)
				{
					foreach (var itemKeyValuePair in subCategoryDictionary)
					{
						if (itemKeyValuePair.Value is JsonObject itemObject)
						{
							var itemPropertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedGlbItemProperties.Contains(property)).ToList();
							foreach (var property in itemPropertiesToRemove) itemObject.Remove(property);
						}
					}
				}
			}
		}

		if (assetsObject.TryGetPropertyValue("textures", out var texturesNode) && texturesNode is JsonObject texturesObject)
		{
			CleanTexturesObject(texturesObject);
		}

		if (assetsObject.TryGetPropertyValue("decals", out var decalsNode) && decalsNode is JsonObject decalsObject)
		{
			var allowedDecalItemProperties = GetAllowedDecalItemProperties();
			foreach (var keyValuePair in decalsObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedDecalItemProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (assetsObject.TryGetPropertyValue("vfx_spritesheets", out var vfxNode) && vfxNode is JsonObject vfxObject)
		{
			var allowedVfxItemProperties = GetAllowedVfxItemProperties();
			foreach (var keyValuePair in vfxObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedVfxItemProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (assetsObject.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObject)
		{
			var allowedShaderItemProperties = GetAllowedShaderItemProperties();
			foreach (var keyValuePair in shadersObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedShaderItemProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}
	}

	private static void CleanMapPropertiesObject(JsonObject mapPropertiesObject, JsonObject? schemaRoot)
	{
		var allowedMapProperties = GetAllowedMapProperties(schemaRoot);
		var propertiesToRemove = mapPropertiesObject.Select(property => property.Key).Where(property => !allowedMapProperties.Contains(property)).ToList();
		foreach (var property in propertiesToRemove)
		{
			mapPropertiesObject.Remove(property);
		}

		mapPropertiesObject.Remove("Assets");
	}

	public static void CleanMetadataJsonSchema(JsonObject root)
	{
		if (root == null) return;

		root.Remove("Assets");

		if (root.TryGetPropertyValue("textures", out var texturesNode) && texturesNode is JsonObject texturesObject)
		{
			CleanTexturesObject(texturesObject);
			foreach (var keyValuePair in texturesObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
				}
			}
		}

		if (root.TryGetPropertyValue("decals", out var decalsNode) && decalsNode is JsonObject decalsObject)
		{
			var allowedDecalProperties = GetAllowedDecalItemProperties();
			foreach (var keyValuePair in decalsObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedDecalProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (root.TryGetPropertyValue("vfx_spritesheets", out var vfxNode) && vfxNode is JsonObject vfxObject)
		{
			var allowedVfxProperties = GetAllowedVfxItemProperties();
			foreach (var keyValuePair in vfxObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedVfxProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (root.TryGetPropertyValue("noise_textures", out var noiseNode) && noiseNode is JsonObject noiseObject)
		{
			foreach (var keyValuePair in noiseObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
				}
			}
		}

		var schemaRoot = LoadMapSchemaJson();
		var allowedTopLevel = GetAllowedMetadataTopLevel(schemaRoot);

		var topKeysToRemove = root.Select(keyValuePair => keyValuePair.Key)
			.Where(key => !allowedTopLevel.Contains(key))
			.ToList();

		foreach (var key in topKeysToRemove)
		{
			root.Remove(key);
		}

		string mapPropsName = nameof(Realm.Ecs.Definitions.MapProperties);
		if (root.TryGetPropertyValue(mapPropsName, out var mapPropertiesNode) && mapPropertiesNode is JsonObject mapPropertiesObject)
		{
			CleanMapPropertiesObject(mapPropertiesObject, schemaRoot);
		}

		var allowedEntityProperties = GetAllowedEntityItemProperties(schemaRoot);
		foreach (var arrayName in GetMetadataEntityArrayNames())
		{
			if (root.TryGetPropertyValue(arrayName, out var node) && node is JsonArray array)
			{
				CleanJsonArrayObjects(array, allowedEntityProperties);
			}
		}

		string baseAbilityName = nameof(GameHost.AbilityMetadata)[..^"Metadata".Length];
		string abilityPlural = baseAbilityName.EndsWith("y", StringComparison.OrdinalIgnoreCase) ? baseAbilityName[..^1] + "ies" : baseAbilityName + "s";
		var allowedAbilityProperties = GetAllowedAbilityItemProperties(schemaRoot);
		foreach (var arrayName in new[] { "Custom" + abilityPlural, abilityPlural })
		{
			if (root.TryGetPropertyValue(arrayName, out var abilitiesNode) && abilitiesNode is JsonArray abilitiesArray)
			{
				CleanJsonArrayObjects(abilitiesArray, allowedAbilityProperties);
			}
		}

		string baseWeaponName = nameof(GameHost.WeaponMetadata)[..^"Metadata".Length];
		string weaponPlural = baseWeaponName + "s";
		var allowedWeaponProperties = GetAllowedWeaponItemProperties(schemaRoot);
		foreach (var arrayName in new[] { "Custom" + weaponPlural, weaponPlural })
		{
			if (root.TryGetPropertyValue(arrayName, out var weaponsNode) && weaponsNode is JsonArray weaponsArray)
			{
				CleanJsonArrayObjects(weaponsArray, allowedWeaponProperties);
			}
		}

		string baseUpgradeName = nameof(GameHost.UpgradeMetadata)[..^"Metadata".Length];
		string upgradePlural = baseUpgradeName + "s";
		var allowedUpgradeProperties = GetAllowedUpgradeItemProperties(schemaRoot);
		foreach (var arrayName in new[] { "Custom" + upgradePlural, upgradePlural })
		{
			if (root.TryGetPropertyValue(arrayName, out var upgradesNode) && upgradesNode is JsonArray upgradesArray)
			{
				CleanJsonArrayObjects(upgradesArray, allowedUpgradeProperties);
			}
		}

		string baseItemName = nameof(GameHost.ItemMetadata)[..^"Metadata".Length];
		string itemPlural = baseItemName + "s";
		var allowedCustomItemProperties = GetAllowedCustomItemProperties(schemaRoot);
		foreach (var arrayName in new[] { "Custom" + itemPlural, itemPlural })
		{
			if (root.TryGetPropertyValue(arrayName, out var customItemsNode) && customItemsNode is JsonArray customItemsArray)
			{
				CleanJsonArrayObjects(customItemsArray, allowedCustomItemProperties);
			}
		}
	}

	private static string[] GetMetadataEntityArrayNames()
	{
		var list = new List<string>();
		Type[] types = new[]
		{
			typeof(GameHost.UnitMetadata),
			typeof(GameHost.PropMetadata),
			typeof(GameHost.ResourceMetadata)
		};

		foreach (var t in types)
		{
			string name = t.Name;
			if (name.EndsWith("Metadata", StringComparison.OrdinalIgnoreCase))
			{
				name = name[..^"Metadata".Length];
			}
			string plural = name + "s";
			list.Add("Custom" + plural);
			list.Add(plural);
		}

		foreach (var field in typeof(GameHost).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.Name.EndsWith("Registry", StringComparison.OrdinalIgnoreCase) && !field.Name.StartsWith("Weapon", StringComparison.OrdinalIgnoreCase))
			{
				string name = field.Name[..^"Registry".Length];
				string plural = name + "s";
				if (!list.Contains("Custom" + plural)) list.Add("Custom" + plural);
				if (!list.Contains(plural)) list.Add(plural);
			}
		}

		return list.ToArray();
	}

	public static void SyncMetadataAssetsAndPrune(string mapDirectory)
	{
		if (string.IsNullOrEmpty(mapDirectory) || !Directory.Exists(mapDirectory)) return;

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(mapDirectory);
			string assetsDir = Path.Combine(mapDirectory, "Assets");

			var includedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var assetsToSync = new List<(string RelativePath, JsonNode? EntryNode, JsonObject ParentObj, string PropertyKey)>();

			foreach (var categoryKvp in assetsObj)
			{
				string category = categoryKvp.Key.ToLowerInvariant();
				if (category == "glb" && categoryKvp.Value is JsonObject glbObj)
				{
					foreach (var subKvp in glbObj)
					{
						string subCategory = MapAssetHelper.NormalizeGlbSubCategory(subKvp.Key);
						if (subKvp.Value is JsonObject subCatObj)
						{
							foreach (var itemKvp in subCatObj)
							{
								string fileName = itemKvp.Key;
								string relPath = Path.Combine("Assets", "models", subCategory, fileName).Replace('\\', '/');
								includedRelativePaths.Add(relPath);
								assetsToSync.Add((relPath, itemKvp.Value, subCatObj, fileName));
							}
						}
					}
				}
				else if (categoryKvp.Value is JsonObject catObj)
				{
					string subFolder = category switch
					{
						"vfx" or "vfx_spritesheets" => "vfx",
						"animations" => "animations",
						"sfx" => "sfx",
						"music" => "music",
						"icons" => "icons",
						"decals" => "decals",
						"ribbons" or "ribbon_textures" => "ribbons",
						"noise" or "noise_textures" => "noise",
						"skyboxes" => "skyboxes",
						"textures" => "textures",
						_ => category
					};

					foreach (var itemKvp in catObj)
					{
						string fileName = itemKvp.Key;
						string relPath = Path.Combine("Assets", subFolder, fileName).Replace('\\', '/');
						includedRelativePaths.Add(relPath);

						if (subFolder is "sfx" or "music")
						{
							includedRelativePaths.Add(Path.Combine("Assets", "audio", subFolder, fileName).Replace('\\', '/'));
						}

						assetsToSync.Add((relPath, itemKvp.Value, catObj, fileName));
					}
				}
			}

			if (Directory.Exists(assetsDir))
			{
				string[] importFiles = Directory.GetFiles(assetsDir, "*.import", SearchOption.AllDirectories);
				foreach (var importFile in importFiles)
				{
					string sourceFile = importFile.Substring(0, importFile.Length - ".import".Length);
					if (!File.Exists(sourceFile))
					{
						try
						{
							File.Delete(importFile);
						}
						catch { }
					}
				}

				DeleteEmptyDirectoriesRecursive(assetsDir);
			}

			foreach (var (relPath, entryNode, parentObj, propertyKey) in assetsToSync)
			{
				string fullDiskPath = Path.Combine(mapDirectory, relPath);
				if (!File.Exists(fullDiskPath))
				{
					string fileName = Path.GetFileName(relPath);
					string? altPath = FindAssetFileByName(assetsDir, fileName);
					if (altPath != null && File.Exists(altPath))
					{
						fullDiskPath = altPath;
					}
				}

				if (File.Exists(fullDiskPath))
				{
					string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fullDiskPath);
					if (!string.IsNullOrEmpty(canonicalBlake3))
					{
						if (entryNode is JsonObject itemObj)
						{
							string existingHash = itemObj["hash"]?.ToString() ?? "";
							if (!string.Equals(existingHash, canonicalBlake3, StringComparison.OrdinalIgnoreCase))
							{
								itemObj["hash"] = canonicalBlake3;
							}
						}
						else if (entryNode is JsonValue)
						{
							string existingHash = entryNode.ToString();
							if (!string.Equals(existingHash, canonicalBlake3, StringComparison.OrdinalIgnoreCase))
							{
								parentObj[propertyKey] = canonicalBlake3;
							}
						}

						RealmMetadataHelper.SyncBlake3Metadata(fullDiskPath);
					}
				}
			}

			MapAssetHelper.SaveAssetsToManifest(mapDirectory, assetsObj, removeFromMetadata: true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] SyncMetadataAssetsAndPrune error: {ex.Message}");
		}
	}

	private static string? FindAssetFileByName(string searchDir, string fileName)
	{
		if (!Directory.Exists(searchDir)) return null;
		string[] matches = Directory.GetFiles(searchDir, fileName, SearchOption.AllDirectories);
		return matches.Length > 0 ? matches[0] : null;
	}

	private static void DeleteEmptyDirectoriesRecursive(string directory)
	{
		if (!Directory.Exists(directory)) return;
		foreach (var sub in Directory.GetDirectories(directory))
		{
			DeleteEmptyDirectoriesRecursive(sub);
		}
		if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
		{
			try { Directory.Delete(directory, false); } catch { }
		}
	}
}

