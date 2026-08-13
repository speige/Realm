using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Godot.Utils;
using System;
using System.Collections.Generic;

public class EditorService
{
	private readonly WorldAccessor EcsWorldAccessor;
	private World EcsWorld => EcsWorldAccessor.Current;
	private TerrainSplatWeights[,] _terrainSplatMap;
	private TerrainSplatWeights[,] _terrainCliffSplatMap;

	private float _clumpSpawnCooldown;
	private bool _isDrawingClump;
	private readonly List<IEditorAction> _clumpSpawnActionsInSession = new();
	private readonly List<(Vector3 Position, float Radius)> _cachedSessionPoints = new();

	private readonly Dictionary<Vector2I, long> _paintCoordLastTimeMs = new();

	public void ResetPaintThrottle()
	{
		_paintCoordLastTimeMs.Clear();
	}

	private bool ShouldPaintCoord(int x, int z, long nowMs)
	{
		Vector2I coord = new Vector2I(x, z);
		if (_paintCoordLastTimeMs.TryGetValue(coord, out long lastTime))
		{
			if (nowMs - lastTime < 250)
			{
				return false;
			}
		}
		_paintCoordLastTimeMs[coord] = nowMs;
		return true;
	}

	private bool _hasBlockTargetHeight;
	private float _activeBlockTargetHeight;
	private WaterType _activeBlockTargetWaterMode = WaterType.None;
	private float? _activePlateauHeight;
	private WaterType _activePlateauWaterMode = WaterType.None;

	private TerrainCell[,] _terrainCellsBefore;
	private TerrainSplatWeights[,] _terrainSplatMapBefore;
	private TerrainSplatWeights[,] _terrainCliffSplatMapBefore;
	private int[,] _terrainPathingBefore;
	private bool _isDrawingTerrain;
	private int _drawMinX;
	private int _drawMinZ;
	private int _drawMaxX;
	private int _drawMaxZ;

	private CopiedAreaTemplate? _copiedArea;

	private Vector2I? _selectionStart;
	private Vector2I? _selectionEnd;
	private bool _isSelectingArea;

	private float _cachedRandomRotation;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom;
	private bool _isPastingObject;

	private CopiedObjectTemplate? _copiedObject;
	private Vector3? _rampStartPos;

	public struct CopiedObjectTemplate
	{
		public string Type;
		public string Id;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}

	private class CopiedAreaTemplate
	{
		public int Width;
		public int Depth;
		public TerrainCell[,] Cells;
		public float[,] Heights
		{
			get
			{
				if (Cells == null) return null;
				int w = Cells.GetLength(0);
				int d = Cells.GetLength(1);
				return Realm.Ecs.Components.Terrain.TerrainState.CalculateHeights(w, d, Cells);
			}
			set
			{
				if (value == null) return;
				int w = value.GetLength(0);
				int d = value.GetLength(1);
				Cells = Realm.Ecs.Components.Terrain.TerrainState.CalculateCells(w, d, value);
			}
		}
		public TerrainSplatWeights[,] SplatMap;
		public int[,] Pathing;
		public List<CopiedEntityInfo> Entities;
	}

	public class CopiedEntityInfo
	{
		public string Type;
		public string Id;
		public Vector3 RelativePos;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}

	public struct TerrainEditResult
	{
		public bool HeightsModified;
		public bool SplatModified;
		public bool PathingModified;
		public int MinX;
		public int MinZ;
		public int MaxX;
		public int MaxZ;
	}

	public struct PasteAreaResult
	{
		public bool TerrainModified;
		public bool HeightsModified;
		public bool PathingModified;
		public List<EntitySpawnRequest> SpawnRequests;
	}

	public struct EntitySpawnRequest
	{
		public string Type;
		public string Id;
		public Vector3 Position;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}

	public struct EraseAreaResult
	{
		public bool TerrainModified;
		public bool HeightsModified;
		public bool PathingModified;
		public List<Node3D> NodesToDelete;
	}

	public EditorService(WorldAccessor ecsWorldAccessor)
	{
		EcsWorldAccessor = ecsWorldAccessor;
	}

	public void SetTerrainSplatMap(TerrainSplatWeights[,] splatMap)
	{
		_terrainSplatMap = splatMap;
		if (splatMap != null)
		{
			AlignSplatMapSlots(0, 0, splatMap.GetLength(0) - 1, splatMap.GetLength(1) - 1);
		}
	}

	public bool IsPastingObject => _isPastingObject;
	public bool HasCachedRandom => _hasCachedRandom;
	public float CachedRandomRotation => _cachedRandomRotation;
	public float CachedRandomScale => _cachedRandomScale;
	public Vector2I? SelectionStart => _selectionStart;
	public Vector2I? SelectionEnd => _selectionEnd;
	public bool IsSelectingArea => _isSelectingArea;
	public bool HasCopiedArea => _copiedArea != null;
	public int CopiedAreaWidth => _copiedArea?.Width ?? 0;
	public int CopiedAreaDepth => _copiedArea?.Depth ?? 0;
	public Vector3? RampStartPos => _rampStartPos;
	public bool IsDrawingTerrain => _isDrawingTerrain;
	public bool IsDrawingClump => _isDrawingClump;

	public CopiedObjectTemplate? GetCopiedObject() => _copiedObject;

	public void SetCopiedObject(CopiedObjectTemplate? template)
	{
		_copiedObject = template;
	}

	public void SetIsPastingObject(bool value)
	{
		_isPastingObject = value;
	}

	public void SetRampStartPos(Vector3? pos)
	{
		_rampStartPos = pos;
	}

	public void SetSelectionStart(Vector2I? value)
	{
		_selectionStart = value;
	}

	public void SetSelectionEnd(Vector2I? value)
	{
		_selectionEnd = value;
	}

	public void SetIsSelectingArea(bool value)
	{
		_isSelectingArea = value;
	}

	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_cachedRandomRotation = (float)(GD.Randf() * 360.0);
		_cachedRandomScale = 0.2f + (float)(GD.Randf() * 2.8);
		_hasCachedRandom = true;
	}

	public void InvalidateCachedRandom()
	{
		_hasCachedRandom = false;
	}

	private void SetGridNodeHeight(ref TerrainState terrain, int gx, int gz, float height)
	{
		var cells = terrain.Cells;
		if (cells == null) return;
		int w = terrain.Width;
		int d = terrain.Depth;
		if (gx > 0 && gz > 0 && gx - 1 < w && gz - 1 < d)
		{
			ref var c = ref cells[gx - 1, gz - 1];
			c.Y_SE = height;
		}
		if (gx < w && gz > 0 && gz - 1 < d)
		{
			ref var c = ref cells[gx, gz - 1];
			c.Y_SW = height;
		}
		if (gx > 0 && gz < d && gx - 1 < w)
		{
			ref var c = ref cells[gx - 1, gz];
			c.Y_NE = height;
		}
		if (gx < w && gz < d)
		{
			ref var c = ref cells[gx, gz];
			c.Y_NW = height;
		}
	}

	private float GetGridNodeHeight(in TerrainState terrain, int gx, int gz)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0f;
		int w = terrain.Width;
		int d = terrain.Depth;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		if (gx < w && gz < d) return cells[cellX, cellZ].Y_NW;
		if (gx == w && gz < d) return cells[w - 1, cellZ].Y_NE;
		if (gx < w && gz == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0.0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0.0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;

		int x = Math.Clamp((int)Math.Floor(fx), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(fz), 0, cellD - 1);
		float tx = Math.Clamp(fx - x, 0f, 1f);
		float tz = Math.Clamp(fz - z, 0f, 1f);

		var cell = cells[x, z];
		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSW = cell.Y_SW;
		float hSE = cell.Y_SE;

		return (1 - tx) * (1 - tz) * hNW + tx * (1 - tz) * hNE + (1 - tx) * tz * hSW + tx * tz * hSE;
	}

	public WaterType GetWaterModeAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return WaterType.None;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return WaterType.None;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterMode;
	}

	public TerrainEditResult ApplyContinuousTerrainEditing(
		Vector3 worldPos,
		float delta,
		GameHost.EditorTool activeTool,
		float brushRadius,
		float brushStrength,
		bool brushIsSquare,
		bool blockMode,
		float blockLevelHeight,
		int paintTextureIndex,
		int cliffPaintTextureIndex,
		int pathingMask,
		bool pathingAdd,
		bool isFirstClick = false,
		bool applyGroundTexture = true,
		bool applyCliffTexture = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return default;

		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainSplatMap == null) return default;

		bool isHeights = activeTool == GameHost.EditorTool.Raise ||
						 activeTool == GameHost.EditorTool.Lower ||
						 activeTool == GameHost.EditorTool.Smooth ||
						 activeTool == GameHost.EditorTool.Plateau ||
						 activeTool == GameHost.EditorTool.Noise;

		bool isPaint = activeTool == GameHost.EditorTool.PaintTexture;

		bool isPathing = activeTool == GameHost.EditorTool.PaintPathing;

		if (!isHeights && !isPaint && !isPathing) return default;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		bool modified = false;
		var result = new TerrainEditResult();
		
		int modMinX = width;
		int modMinZ = depth;
		int modMaxX = -1;
		int modMaxZ = -1;

		if (isFirstClick)
		{
			ResetPaintThrottle();
		}

		if (blockMode && activeTool != GameHost.EditorTool.Noise && activeTool != GameHost.EditorTool.Smooth)
		{
			int cx = Mathf.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, width - 1);
			int cz = Mathf.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, depth - 1);
			int N = Mathf.Max(1, (int)Mathf.Round(brushRadius));

			int quadMinX = cx - (N - 1) / 2;
			int quadMaxX = cx + N / 2;

			int quadMinZ = cz - (N - 1) / 2;
			int quadMaxZ = cz + N / 2;

			if (isHeights)
			{
				if (!_hasBlockTargetHeight)
				{
					float startHeight = GetTerrainHeightAt(worldPos);
					WaterType startWater = GetWaterModeAt(worldPos);
					if (activeTool == GameHost.EditorTool.Raise)
					{
						_activeBlockTargetHeight = Math.Clamp(startHeight + blockLevelHeight, -16.0f, 16.0f);
					}
					else if (activeTool == GameHost.EditorTool.Lower)
					{
						_activeBlockTargetHeight = Math.Clamp(startHeight - blockLevelHeight, -16.0f, 16.0f);
					}
					else if (activeTool == GameHost.EditorTool.Plateau)
					{
						_activeBlockTargetHeight = Math.Clamp(startHeight, -16.0f, 16.0f);
						_activeBlockTargetWaterMode = startWater;
					}
					_activeBlockTargetHeight = Math.Clamp(_activeBlockTargetHeight, -16.0f, 16.0f);
					_hasBlockTargetHeight = true;
				}
				float targetHeight = Math.Clamp(_activeBlockTargetHeight, -16.0f, 16.0f);
				sbyte targetMacroTier = (sbyte)Math.Clamp((int)MathF.Round(targetHeight / TerrainCell.TIER_HEIGHT), -16, 16);
				Entity worldEntity = GameHost.Instance?.WorldEntity ?? Entity.Null;
				WaterType selectedWaterMode = GetWaterMode(worldEntity);

				float[,] heights = TerrainState.CalculateHeights(width, depth, terrain.Cells);

				for (int z = quadMinZ; z <= quadMaxZ; z++)
				{
					for (int x = quadMinX; x <= quadMaxX; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!brushIsSquare)
							{
								float radius = N / 2.0f;
								float centerX = quadMinX + N / 2.0f;
								float centerZ = quadMinZ + N / 2.0f;
								float dx = (x + 0.5f) - centerX;
								float dz = (z + 0.5f) - centerZ;
								inBounds = (dx * dx + dz * dz) <= (radius * radius);
							}

							if (inBounds)
							{
								if (activeTool == GameHost.EditorTool.Raise ||
									activeTool == GameHost.EditorTool.Lower ||
									activeTool == GameHost.EditorTool.Plateau)
								{
									ref var cell = ref terrain.Cells[x, z];
									float oldH = cell.CenterHeight;
									float newH = targetHeight;
									bool waterChanged = false;

									if (activeTool == GameHost.EditorTool.Raise)
									{
										if (cell.WaterMode != WaterType.None)
										{
											cell.WaterMode = WaterType.None;
											waterChanged = true;
										}
									}
									else if (activeTool == GameHost.EditorTool.Lower)
									{
										if (selectedWaterMode != WaterType.None)
										{
											if (cell.WaterMode != selectedWaterMode)
											{
												cell.WaterMode = selectedWaterMode;
												waterChanged = true;
											}
										}
										else
										{
											bool foundWater = false;
											for (int nz = z - 1; nz <= z + 1; nz++)
											{
												for (int nx = x - 1; nx <= x + 1; nx++)
												{
													if (nx >= 0 && nx < width && nz >= 0 && nz < depth && !(nx == x && nz == z))
													{
														WaterType neighborWater = terrain.Cells[nx, nz].WaterMode;
														if (neighborWater == WaterType.Shallow || neighborWater == WaterType.Deep)
														{
															if (cell.WaterMode != neighborWater)
															{
																cell.WaterMode = neighborWater;
																waterChanged = true;
															}
															foundWater = true;
															break;
														}
													}
												}
												if (foundWater) break;
											}
										}
									}
									else if (activeTool == GameHost.EditorTool.Plateau)
									{
										if (cell.WaterMode != _activeBlockTargetWaterMode)
										{
											cell.WaterMode = _activeBlockTargetWaterMode;
											waterChanged = true;
										}
									}

									if (MathF.Abs(cell.CenterHeight - targetHeight) > 0.001f || waterChanged)
									{
										if (terrain.PathingCodes != null)
										{
											if (cell.WaterMode != WaterType.None)
											{
												terrain.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cell.WaterMode);
												result.PathingModified = true;
											}
											else
											{
												int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(cell);
												if (terrain.PathingCodes[x, z] == defaultPathBefore)
												{
													terrain.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cell);
													result.PathingModified = true;
												}
											}
										}
										heights[x, z] = targetHeight;
										heights[x + 1, z] = targetHeight;
										heights[x + 1, z + 1] = targetHeight;
										heights[x, z + 1] = targetHeight;

										if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
										{
											_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
										}
										if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
										{
											_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
										}

										if (applyGroundTexture && _terrainSplatMap != null)
										{
											int splatW = _terrainSplatMap.GetLength(0);
											int splatD = _terrainSplatMap.GetLength(1);
											for (int gz = z; gz <= z + 1; gz++)
											{
												for (int gx = x; gx <= x + 1; gx++)
												{
													if (gx >= 0 && gx < splatW && gz >= 0 && gz < splatD)
													{
														_terrainSplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(paintTextureIndex);
													}
												}
											}
										}

										if (applyCliffTexture && _terrainCliffSplatMap != null)
										{
											int cliffW = _terrainCliffSplatMap.GetLength(0);
											int cliffD = _terrainCliffSplatMap.GetLength(1);
											for (int nz = z - 1; nz <= z + 2; nz++)
											{
												for (int nx = x - 1; nx <= x + 2; nx++)
												{
													if (nx >= 0 && nx < cliffW && nz >= 0 && nz < cliffD)
													{
														_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(cliffPaintTextureIndex);
													}
												}
											}
										}

										int minXBound = Math.Max(0, x - 1);
										int maxXBound = Math.Min(width - 1, x + 1);
										int minZBound = Math.Max(0, z - 1);
										int maxZBound = Math.Min(depth - 1, z + 1);

										if (minXBound < modMinX) modMinX = minXBound;
										if (maxXBound > modMaxX) modMaxX = maxXBound;
										if (minZBound < modMinZ) modMinZ = minZBound;
										if (maxZBound > modMaxZ) modMaxZ = maxZBound;
										modified = true;
									}
								}
								else if (activeTool == GameHost.EditorTool.Smooth)
								{
									int sum = 0;
									int count = 0;
									for (int nz = -1; nz <= 1; nz++)
									{
										for (int nx = -1; nx <= 1; nx++)
										{
											int nxVal = x + nx;
											int nzVal = z + nz;
											if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
											{
												sum += terrain.Cells[nxVal, nzVal].MacroTier;
												count++;
											}
										}
									}
									sbyte avgMacro = (sbyte)Math.Clamp((int)MathF.Round((float)sum / count), -16, 16);
									float targetH = avgMacro * TerrainCell.TIER_HEIGHT;
									if (MathF.Abs(terrain.Cells[x, z].CenterHeight - targetH) > 0.001f)
									{
										heights[x, z] = targetH;
										heights[x + 1, z] = targetH;
										heights[x + 1, z + 1] = targetH;
										heights[x, z + 1] = targetH;

										int minXBound = Math.Max(0, x - 1);
										int maxXBound = Math.Min(width - 1, x + 1);
										int minZBound = Math.Max(0, z - 1);
										int maxZBound = Math.Min(depth - 1, z + 1);

										if (minXBound < modMinX) modMinX = minXBound;
										if (maxXBound > modMaxX) modMaxX = maxXBound;
										if (minZBound < modMinZ) modMinZ = minZBound;
										if (maxZBound > modMaxZ) modMaxZ = maxZBound;
										modified = true;
									}
								}
							}
						}
					}
				}

				if (modified && heights != null)
				{
					terrain.Cells = TerrainState.CalculateCells(width, depth, heights, terrain.Cells);
				}
			}
			else if (isPaint)
			{
				if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
				{
					_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
				}

				int splatW = _terrainSplatMap.GetLength(0);
				int splatD = _terrainSplatMap.GetLength(1);
				int intensityLevel = Math.Clamp((int)MathF.Round(brushStrength), 0, 10);

				for (int z = 0; z < splatD; z++)
				{
					for (int x = 0; x < splatW; x++)
					{
						float vx = (x - width / 2.0f) * quadSize;
						float vz = (z - depth / 2.0f) * quadSize;

						bool inBounds = false;
						if (brushIsSquare)
						{
							float dx = Mathf.Abs(vx - worldPos.X);
							float dz = Mathf.Abs(vz - worldPos.Z);
							inBounds = dx <= brushRadius && dz <= brushRadius;
						}
						else
						{
							inBounds = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length() <= brushRadius;
						}

						if (inBounds && ShouldPaintCoord(x, z, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
						{
							if (applyGroundTexture)
							{
								_terrainSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainSplatMap[x, z], paintTextureIndex, intensityLevel);
							}
							if (applyCliffTexture && _terrainCliffSplatMap != null && x < _terrainCliffSplatMap.GetLength(0) && z < _terrainCliffSplatMap.GetLength(1))
							{
								_terrainCliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainCliffSplatMap[x, z], cliffPaintTextureIndex, intensityLevel);
							}

							if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
							modified = true;
						}
					}
				}
			}
			else if (isPathing)
			{
				for (int z = quadMinZ; z <= quadMaxZ; z++)
				{
					for (int x = quadMinX; x <= quadMaxX; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!brushIsSquare)
							{
								float radius = N / 2.0f;
								float centerX = quadMinX + N / 2.0f;
								float centerZ = quadMinZ + N / 2.0f;
								float dx = (x + 0.5f) - centerX;
								float dz = (z + 0.5f) - centerZ;
								inBounds = (dx * dx + dz * dz) <= (radius * radius);
							}

							if (inBounds)
							{
								if (pathingAdd)
									terrain.PathingCodes[x, z] |= pathingMask;
								else
									terrain.PathingCodes[x, z] &= ~pathingMask;
								if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
								modified = true;
							}
						}
					}
				}
			}
		}
		else
		{
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			for (int z = 0; z <= depth; z++)
			{
				for (int x = 0; x <= width; x++)
				{
					float vx = (x - width / 2.0f) * quadSize;
					float vz = (z - depth / 2.0f) * quadSize;

					float dist = 0.0f;
					bool inBounds = false;
					if (brushIsSquare)
					{
						float dx = Mathf.Abs(vx - worldPos.X);
						float dz = Mathf.Abs(vz - worldPos.Z);
						inBounds = dx <= brushRadius && dz <= brushRadius;
						dist = Mathf.Max(dx, dz);
					}
					else
					{
						dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
						inBounds = dist <= brushRadius;
					}

					if (inBounds)
					{
						float falloff = 1.0f - (dist / brushRadius);
						falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);

						if (isHeights)
						{
							float oldH = GetGridNodeHeight(in terrain, x, z);
							float newH = oldH;
							if (activeTool == GameHost.EditorTool.Raise)
							{
								newH = Mathf.Clamp(oldH + brushStrength * falloff * delta, -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Lower)
							{
								newH = Mathf.Clamp(oldH - brushStrength * falloff * delta, -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Plateau)
							{
								float targetHeight = _activePlateauHeight ?? 0.0f;
								newH = Mathf.Clamp(Mathf.Lerp(oldH, targetHeight, brushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
								int cellX = Math.Clamp(x, 0, width - 1);
								int cellZ = Math.Clamp(z, 0, depth - 1);
								if (terrain.Cells != null && cellX < terrain.Cells.GetLength(0) && cellZ < terrain.Cells.GetLength(1))
								{
									if (terrain.Cells[cellX, cellZ].WaterMode != _activePlateauWaterMode)
									{
										terrain.Cells[cellX, cellZ].WaterMode = _activePlateauWaterMode;
										if (terrain.PathingCodes != null)
										{
											terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
											result.PathingModified = true;
										}
									}
								}
								if (Mathf.Abs(newH - oldH) > 0.001f && terrain.PathingCodes != null)
								{
									int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
									if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
									{
										terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
										result.PathingModified = true;
									}
								}
							}
							else if (activeTool == GameHost.EditorTool.Smooth)
							{
								float avg = 0f;
								int count = 0;
								for (int nz = -1; nz <= 1; nz++)
								{
									for (int nx = -1; nx <= 1; nx++)
									{
										int nxVal = x + nx;
										int nzVal = z + nz;
										if (nxVal >= 0 && nxVal <= width && nzVal >= 0 && nzVal <= depth)
										{
											avg += GetGridNodeHeight(in terrain, nxVal, nzVal);
											count++;
										}
									}
								}
								avg /= count;
								float effectiveStrength = brushStrength * 15.0f + 5.0f;
								newH = Mathf.Clamp(Mathf.Lerp(oldH, avg, Mathf.Clamp(effectiveStrength * falloff * delta, 0.0f, 1.0f)), -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Noise)
							{
								float dRatio = Mathf.Clamp(dist / brushRadius, 0.0f, 1.0f);
								float falloffWeight = 1.0f - (dRatio * dRatio * (3.0f - 2.0f * dRatio));
								float octave1 = SimplexNoise.Simplex2D(x * 0.05f, z * 0.05f) * 1.0f;
								float octave2 = SimplexNoise.Simplex2D(x * 0.2f, z * 0.2f) * 0.35f;
								float noiseValue = (octave1 + octave2) / 1.35f;
								float effectiveStrength = brushStrength * 0.25f + 8.25f;
								newH = Mathf.Clamp(oldH + noiseValue * effectiveStrength * falloffWeight * delta * 0.5f, -10.0f, 50.0f);
							}

							if (Mathf.Abs(newH - oldH) > 0.001f)
							{
								SetGridNodeHeight(ref terrain, x, z, newH);
								if (activeTool == GameHost.EditorTool.Raise || activeTool == GameHost.EditorTool.Lower || activeTool == GameHost.EditorTool.Plateau)
								{
									if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
									{
										_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
									}
									if (_terrainCliffSplatMap != null)
									{
										for (int cz = z - 1; cz <= z; cz++)
										{
											for (int cx = x - 1; cx <= x; cx++)
											{
												if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
												{
													for (int nz = cz - 1; nz <= cz + 1; nz++)
													{
														for (int nx = cx - 1; nx <= cx + 1; nx++)
														{
															if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
															{
																_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(cliffPaintTextureIndex);
															}
														}
													}
												}
											}
										}
									}
								}
								if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
								modified = true;
							}
						}
						else if (isPaint)
						{
							if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
							{
								_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
							}

							int splatW = _terrainSplatMap.GetLength(0);
							int splatD = _terrainSplatMap.GetLength(1);
							int intensityLevel = Math.Clamp((int)MathF.Round(brushStrength), 0, 10);

							if (x < splatW && z < splatD && ShouldPaintCoord(x, z, nowMs))
							{
								if (applyGroundTexture)
								{
									_terrainSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainSplatMap[x, z], paintTextureIndex, intensityLevel);
								}
								if (applyCliffTexture && _terrainCliffSplatMap != null && x < _terrainCliffSplatMap.GetLength(0) && z < _terrainCliffSplatMap.GetLength(1))
								{
									_terrainCliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainCliffSplatMap[x, z], cliffPaintTextureIndex, intensityLevel);
								}

								if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
								modified = true;
							}
						}
						else if (isPathing && x < width && z < depth)
						{
							int icx = Mathf.Clamp((int)Math.Round(vx / quadSize + width / 2.0f), 0, width - 1);
							int icz = Mathf.Clamp((int)Math.Round(vz / quadSize + depth / 2.0f), 0, depth - 1);
							if (pathingAdd)
								terrain.PathingCodes[icx, icz] |= pathingMask;
							else
								terrain.PathingCodes[icx, icz] &= ~pathingMask;
							if (icx < modMinX) modMinX = icx; if (icx > modMaxX) modMaxX = icx; if (icz < modMinZ) modMinZ = icz; if (icz > modMaxZ) modMaxZ = icz;
							modified = true;
						}
					}
				}
			}
		}

		if (modified)
		{
			result.HeightsModified = isHeights;
			result.SplatModified = isPaint || (isHeights && activeTool != GameHost.EditorTool.Smooth && activeTool != GameHost.EditorTool.Noise);
			result.PathingModified = isPathing;
			if (modMinX < _drawMinX) _drawMinX = modMinX;
			if (modMaxX > _drawMaxX) _drawMaxX = modMaxX;
			if (modMinZ < _drawMinZ) _drawMinZ = modMinZ;
			if (modMaxZ > _drawMaxZ) _drawMaxZ = modMaxZ;
			result.MinX = modMinX;
			result.MinZ = modMinZ;
			result.MaxX = modMaxX;
			result.MaxZ = modMaxZ;

			if (result.SplatModified)
			{
				AlignSplatMapSlots(modMinX - 2, modMinZ - 2, modMaxX + 2, modMaxZ + 2);
			}
		}

		return result;
	}

	public void BeginTerrainDraw(
		Vector3 hitPos,
		GameHost.EditorTool activeTool,
		bool blockMode,
		float blockLevelHeight,
		float[,] currentHeights,
		TerrainSplatWeights[,] currentSplatMap,
		int[,] currentPathing,
		TerrainSplatWeights[,] currentCliffSplatMap = null)
	{
		ref var terrain = ref GetTerrainState();
		_isDrawingTerrain = true;
		_drawMinX = int.MaxValue;
		_drawMinZ = int.MaxValue;
		_drawMaxX = -1;
		_drawMaxZ = -1;
		_terrainCellsBefore = terrain.Cells != null ? (TerrainCell[,])terrain.Cells.Clone() : null;
		_terrainSplatMapBefore = currentSplatMap != null ? (TerrainSplatWeights[,])currentSplatMap.Clone() : null;
		_terrainCliffSplatMapBefore = currentCliffSplatMap != null ? (TerrainSplatWeights[,])currentCliffSplatMap.Clone() : null;
		_terrainPathingBefore = currentPathing != null ? (int[,])currentPathing.Clone() : null;

		if (activeTool == GameHost.EditorTool.Plateau)
		{
			_activePlateauHeight = GetTerrainHeightAt(hitPos);
			_activePlateauWaterMode = GetWaterModeAt(hitPos);
		}

		if (blockMode)
		{
			float startHeight = GetTerrainHeightAt(hitPos);
			WaterType startWater = GetWaterModeAt(hitPos);
			if (activeTool == GameHost.EditorTool.Raise)
			{
				_activeBlockTargetHeight = startHeight + blockLevelHeight;
				_hasBlockTargetHeight = true;
			}
			else if (activeTool == GameHost.EditorTool.Lower)
			{
				_activeBlockTargetHeight = startHeight - blockLevelHeight;
				_hasBlockTargetHeight = true;
			}
			else if (activeTool == GameHost.EditorTool.Plateau)
			{
				_activeBlockTargetHeight = startHeight;
				_activeBlockTargetWaterMode = startWater;
				_hasBlockTargetHeight = true;
			}
		}
	}

	public TerrainModifyAction EndTerrainDraw(
		float[,] currentHeights,
		TerrainSplatWeights[,] currentSplatMap,
		int[,] currentPathing,
		TerrainSplatWeights[,] currentCliffSplatMap = null)
	{
		_isDrawingTerrain = false;
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;

		ref var terrain = ref GetTerrainState();
		var currentCells = terrain.Cells;

		if (_drawMinX <= _drawMaxX && _drawMinZ <= _drawMaxZ && currentCells != null)
		{
			int w = _drawMaxX - _drawMinX + 1;
			int d = _drawMaxZ - _drawMinZ + 1;

			TerrainCell[,] beforeC = new TerrainCell[w, d];
			TerrainCell[,] afterC = new TerrainCell[w, d];
			int[,] beforeP = currentPathing != null ? new int[w, d] : null;
			int[,] afterP = currentPathing != null ? new int[w, d] : null;

			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (_terrainCellsBefore != null && mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1))
					{
						beforeC[x, z] = _terrainCellsBefore[mapX, mapZ];
					}
					if (currentCells != null && mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
					{
						afterC[x, z] = currentCells[mapX, mapZ];
					}
					if (beforeP != null)
					{
						beforeP[x, z] = _terrainPathingBefore[mapX, mapZ];
						afterP[x, z] = currentPathing[mapX, mapZ];
					}
				}
			}

			// Capture node-based splatmaps spanning [_drawMinX - 1 .. _drawMaxX + 2] to include all surrounding node updates
			int splatW = currentSplatMap != null ? currentSplatMap.GetLength(0) : terrain.Width + 1;
			int splatD = currentSplatMap != null ? currentSplatMap.GetLength(1) : terrain.Depth + 1;

			int minNodeX = Math.Max(0, _drawMinX - 1);
			int maxNodeX = Math.Min(splatW - 1, _drawMaxX + 2);
			int minNodeZ = Math.Max(0, _drawMinZ - 1);
			int maxNodeZ = Math.Min(splatD - 1, _drawMaxZ + 2);
			int nodeW = maxNodeX - minNodeX + 1;
			int nodeD = maxNodeZ - minNodeZ + 1;

			TerrainSplatWeights[,] beforeS = currentSplatMap != null ? new TerrainSplatWeights[nodeW, nodeD] : null;
			TerrainSplatWeights[,] afterS = currentSplatMap != null ? new TerrainSplatWeights[nodeW, nodeD] : null;
			TerrainSplatWeights[,] beforeCliffS = currentCliffSplatMap != null ? new TerrainSplatWeights[nodeW, nodeD] : null;
			TerrainSplatWeights[,] afterCliffS = currentCliffSplatMap != null ? new TerrainSplatWeights[nodeW, nodeD] : null;

			for (int z = 0; z < nodeD; z++)
			{
				for (int x = 0; x < nodeW; x++)
				{
					int mapX = minNodeX + x;
					int mapZ = minNodeZ + z;
					if (_terrainSplatMapBefore != null && mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1))
					{
						beforeS[x, z] = _terrainSplatMapBefore[mapX, mapZ];
					}
					if (currentSplatMap != null && mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
					{
						afterS[x, z] = currentSplatMap[mapX, mapZ];
					}
					if (_terrainCliffSplatMapBefore != null && mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1))
					{
						beforeCliffS[x, z] = _terrainCliffSplatMapBefore[mapX, mapZ];
					}
					if (currentCliffSplatMap != null && mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
					{
						afterCliffS[x, z] = currentCliffSplatMap[mapX, mapZ];
					}
				}
			}

			return new TerrainModifyAction(_drawMinX, _drawMinZ, w, d, beforeC, afterC, beforeS, afterS, beforeP, afterP, beforeCliffS, afterCliffS);
		}

		return new TerrainModifyAction(
			_terrainCellsBefore, currentCells,
			_terrainSplatMapBefore, currentSplatMap,
			_terrainPathingBefore, currentPathing,
			_terrainCliffSplatMapBefore, currentCliffSplatMap);
	}

	public void ResetDrawState()
	{
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
	}

	public void ResetAllState()
	{
		_selectionStart = null;
		_selectionEnd = null;
		_copiedArea = null;
		_hasBlockTargetHeight = false;
		_activeBlockTargetHeight = 0.0f;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
		_terrainSplatMap = null;
		_terrainCliffSplatMap = null;
		_clumpSpawnCooldown = 0.0f;
	}

	public void TickClumpCooldown(float delta)
	{
		if (_clumpSpawnCooldown > 0.0f)
			_clumpSpawnCooldown -= delta;
	}

	public bool CanSpawnClump() => _clumpSpawnCooldown <= 0.0f;

	public void BeginClumpSession()
	{
		_isDrawingClump = true;
		_clumpSpawnActionsInSession.Clear();
		_cachedSessionPoints.Clear();
	}

	public void RecordClumpSpawnAction(IEditorAction action)
	{
		_clumpSpawnActionsInSession.Add(action);
		if (action is ObjectSpawnAction objAct && GodotObject.IsInstanceValid(objAct.SpawnedNode))
		{
			float baseRadius = 0.5f;
			_cachedSessionPoints.Add((objAct.Position, baseRadius * objAct.Scale));
		}
	}

	public void SetClumpCooldown(float value)
	{
		_clumpSpawnCooldown = value;
	}

	public CompositeAction EndClumpSession()
	{
		_isDrawingClump = false;
		if (_clumpSpawnActionsInSession.Count > 0)
		{
			return new CompositeAction(_clumpSpawnActionsInSession);
		}
		return null;
	}

	public List<EntitySpawnRequest> BuildClumpSpawnRequests(
		Vector3 centerPos,
		GameHost.EditorTool activeTool,
		string activePlaceId,
		bool placeUnitIsEnemy,
		float placementScale,
		float clumpCount,
		float clumpScale,
		float brushRadius,
		bool brushIsSquare,
		bool randomRotation,
		bool randomScale,
		float placementRotation,
		MirrorMode mirrorMode,
		float assetBaseCollisionRadius = 0.5f)
	{
		if (string.IsNullOrEmpty(activePlaceId)) return new List<EntitySpawnRequest>();

		var requests = new List<EntitySpawnRequest>();
		int spawnCount = Mathf.Max(1, (int)Math.Round(clumpCount));

		ref var terrain = ref GetTerrainState();
		float quadSize = terrain.Cells != null ? terrain.QuadSize : 1f;
		int terrainWidth = terrain.Cells != null ? terrain.Width : 0;
		int terrainDepth = terrain.Cells != null ? terrain.Depth : 0;
		float halfW = terrainWidth / 2.0f * quadSize;
		float halfD = terrainDepth / 2.0f * quadSize;

		float baseRadius = Mathf.Max(0.1f, assetBaseCollisionRadius);
		float autoClumpSpacing = Mathf.Clamp(brushRadius / (1.5f * Mathf.Sqrt(Mathf.Max(1, spawnCount))), 0.5f, 4.0f);

		int maxAttemptsPerObject = 30;

		for (int i = 0; i < spawnCount; i++)
		{
			bool placed = false;
			for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
			{
				float dx = 0.0f;
				float dz = 0.0f;
				if (brushIsSquare)
				{
					dx = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
					dz = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
				}
				else
				{
					float r = Mathf.Sqrt((float)GD.Randf()) * brushRadius;
					float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
					dx = r * Mathf.Cos(theta);
					dz = r * Mathf.Sin(theta);
				}

				Vector3 spawnPos = new Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
				if (terrain.Cells != null)
				{
					if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
				}
				spawnPos.Y = GetTerrainHeightAt(spawnPos);

				float offsetRange = clumpScale * placementScale;
				float minScale = Mathf.Max(0.01f, placementScale - offsetRange);
				float maxScale = placementScale + offsetRange;
				float scaleVal = minScale + (float)GD.Randf() * (maxScale - minScale);

				float candidateRadius = baseRadius * scaleVal;

				bool collision = false;
				foreach (var req in requests)
				{
					float reqRadius = baseRadius * req.Scale;
					float minDist = (candidateRadius + reqRadius) * autoClumpSpacing;
					float distSq = (spawnPos.X - req.Position.X) * (spawnPos.X - req.Position.X) +
					               (spawnPos.Z - req.Position.Z) * (spawnPos.Z - req.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				foreach (var sessionPt in _cachedSessionPoints)
				{
					float minDist = (candidateRadius + sessionPt.Radius) * autoClumpSpacing;
					float distSq = (spawnPos.X - sessionPt.Position.X) * (spawnPos.X - sessionPt.Position.X) +
					               (spawnPos.Z - sessionPt.Position.Z) * (spawnPos.Z - sessionPt.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				float rotY = randomRotation ? (float)(GD.Randf() * 360.0) : placementRotation;
				string spawnType = activeTool == GameHost.EditorTool.PlaceUnit ? "unit"
					: activeTool == GameHost.EditorTool.PlaceProp ? "prop"
					: "decal";
				bool isEnemy = activeTool == GameHost.EditorTool.PlaceUnit && placeUnitIsEnemy;

				requests.Add(new EntitySpawnRequest
				{
					Type = spawnType,
					Id = activePlaceId,
					Position = spawnPos,
					Rotation = rotY,
					Scale = scaleVal,
					IsEnemy = isEnemy
				});

				if (mirrorMode != MirrorMode.None)
				{
					AddMirroredRequests(requests, spawnType, activePlaceId, spawnPos, rotY, scaleVal, isEnemy, mirrorMode);
				}

				placed = true;
				break;
			}

			if (!placed && requests.Count > 0)
			{
			}
		}

		return requests;
	}

	public bool ApplyRamp(Vector3 start, Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return false;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		bool modified = false;

		float segmentLengthSquared = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
		if (segmentLengthSquared <= 0.0001f) return false;

		float minWorldX = Mathf.Min(start.X, end.X) - brushRadius;
		float maxWorldX = Mathf.Max(start.X, end.X) + brushRadius;
		float minWorldZ = Mathf.Min(start.Z, end.Z) - brushRadius;
		float maxWorldZ = Mathf.Max(start.Z, end.Z) + brushRadius;

		int minGridX = Mathf.Clamp(Mathf.FloorToInt(minWorldX / quadSize + width / 2.0f), 0, width);
		int maxGridX = Mathf.Clamp(Mathf.CeilToInt(maxWorldX / quadSize + width / 2.0f), 0, width);
		int minGridZ = Mathf.Clamp(Mathf.FloorToInt(minWorldZ / quadSize + depth / 2.0f), 0, depth);
		int maxGridZ = Mathf.Clamp(Mathf.CeilToInt(maxWorldZ / quadSize + depth / 2.0f), 0, depth);

		for (int gridZ = minGridZ; gridZ <= maxGridZ; gridZ++)
		{
			for (int gridX = minGridX; gridX <= maxGridX; gridX++)
			{
				float worldX = (gridX - width / 2.0f) * quadSize;
				float worldZ = (gridZ - depth / 2.0f) * quadSize;
				float interpolationFactor = ((worldX - start.X) * (end.X - start.X) + (worldZ - start.Z) * (end.Z - start.Z)) / segmentLengthSquared;
				interpolationFactor = Mathf.Clamp(interpolationFactor, 0.0f, 1.0f);
				float projectedX = start.X + interpolationFactor * (end.X - start.X);
				float projectedZ = start.Z + interpolationFactor * (end.Z - start.Z);
				float distanceToProjected = Mathf.Sqrt((worldX - projectedX) * (worldX - projectedX) + (worldZ - projectedZ) * (worldZ - projectedZ));
				if (distanceToProjected <= brushRadius)
				{
					float targetHeight = Mathf.Lerp(start.Y, end.Y, interpolationFactor);
					float innerRadius = brushRadius * 0.7f;
					float oldHeight = GetGridNodeHeight(in terrain, gridX, gridZ);
					float newHeight;

					if (distanceToProjected <= innerRadius)
					{
						newHeight = targetHeight;
					}
					else
					{
						float edgeFactor = 1.0f - ((distanceToProjected - innerRadius) / Math.Max(0.001f, brushRadius - innerRadius));
						edgeFactor = Mathf.Sin(edgeFactor * Mathf.Pi / 2.0f);
						newHeight = Mathf.Lerp(oldHeight, targetHeight, edgeFactor);
					}

					if (Mathf.Abs(newHeight - oldHeight) > 0.001f)
					{
						int cellX = Math.Clamp(gridX, 0, width - 1);
						int cellZ = Math.Clamp(gridZ, 0, depth - 1);
						if (terrain.PathingCodes != null)
						{
							int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
							if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
							{
								terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
							}
						}
						SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
						int rampPaintIdx = GameHost.Instance != null ? GameHost.Instance.EditorPaintTextureIndex : 0;
						int rampCliffIdx = GameHost.Instance != null ? GameHost.Instance.EditorCliffPaintTextureIndex : 1;

						if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
						{
							_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
						}
						if (_terrainSplatMap != null)
						{
							for (int cz = gridZ - 1; cz <= gridZ; cz++)
							{
								for (int cx = gridX - 1; cx <= gridX; cx++)
								{
									if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
									{
										_terrainSplatMap[cx, cz] = TerrainSplatWeights.CreateSolid(rampPaintIdx);
									}
								}
							}
						}

						if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
						{
							_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
						}
						if (_terrainCliffSplatMap != null)
						{
							for (int cz = gridZ - 1; cz <= gridZ; cz++)
							{
								for (int cx = gridX - 1; cx <= gridX; cx++)
								{
									if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
									{
										for (int nz = cz - 1; nz <= cz + 1; nz++)
										{
											for (int nx = cx - 1; nx <= cx + 1; nx++)
											{
												if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
												{
													_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(rampCliffIdx);
												}
											}
										}
									}
								}
							}
						}
						modified = true;
					}
				}
			}
		}

		if (modified)
		{
			AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);
		}

		return modified;
	}

	public float GetMinHeightInBrushBounds(Vector3 worldPos, float brushRadius, bool brushIsSquare)
	{
		float result = GetMinHeightInBrushBoundsInternal(worldPos, brushRadius, brushIsSquare);
		return result;
	}

	public void CopyArea(int minX, int minZ, int maxX, int maxZ, List<CopiedEntityInfo> entities)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return;

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var copiedCells = new TerrainCell[selWidth, selDepth];
		var splatMap = new TerrainSplatWeights[selWidth, selDepth];
		var pathing = new int[selWidth, selDepth];

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int sourceX = Math.Clamp(minX + sx, 0, terrain.Width - 1);
				int sourceZ = Math.Clamp(minZ + sz, 0, terrain.Depth - 1);
				copiedCells[sx, sz] = cells[sourceX, sourceZ];
				splatMap[sx, sz] = _terrainSplatMap[minX + sx, minZ + sz];
				if (terrain.PathingCodes != null)
				{
					pathing[sx, sz] = terrain.PathingCodes[minX + sx, minZ + sz];
				}
			}
		}

		_copiedArea = new CopiedAreaTemplate
		{
			Width = selWidth,
			Depth = selDepth,
			Cells = copiedCells,
			SplatMap = splatMap,
			Pathing = pathing,
			Entities = entities
		};
	}

	public List<CopiedEntityInfo> BuildCopiedEntityList(
		int minX, int minZ, int maxX, int maxZ,
		IEnumerable<Node3D> sceneChildren)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return new List<CopiedEntityInfo>();

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
		float minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;
		Vector3 origin = new Vector3((minX - width / 2.0f) * quadSize, 0.0f, (minZ - depth / 2.0f) * quadSize);

		var entities = new List<CopiedEntityInfo>();
		foreach (var n3d in sceneChildren)
		{
			if (!GodotObject.IsInstanceValid(n3d)) continue;
			Vector3 pos = n3d.Position;
			if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
			{
				if (n3d is Unit3D unit)
				{
					entities.Add(new CopiedEntityInfo
					{
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
					entities.Add(new CopiedEntityInfo
					{
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
					string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
					entities.Add(new CopiedEntityInfo
					{
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

		if (EcsWorldAccessor.Current != null)
		{
			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			var world = EcsWorldAccessor.Current;
			world.Query(in propQuery, (Arch.Core.Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
			{
				if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

				Vector3 worldPos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
				if (worldPos.X >= minWorldX && worldPos.X <= maxWorldX && worldPos.Z >= minWorldZ && worldPos.Z <= maxWorldZ)
				{
					float rotY = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : 0f;
					float scale = world.Has<ModelScale>(entity) ? world.Get<ModelScale>(entity).Value : 1f;

					entities.Add(new CopiedEntityInfo
					{
						Type = "prop",
						Id = propIdComp.PropId,
						RelativePos = worldPos - origin,
						Rotation = rotY,
						Scale = scale,
						IsEnemy = false
					});
				}
			});
		}

		return entities;
	}

	public void MirrorCopiedAreaVertically()
	{
		if (_copiedArea == null) return;
		int w = _copiedArea.Width;
		int d = _copiedArea.Depth;
		
		var newCells = new TerrainCell[w, d];
		var newSplatMap = new TerrainSplatWeights[w, d];
		var newPathing = _copiedArea.Pathing != null ? new int[w, d] : null;

		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				newCells[x, z] = _copiedArea.Cells[x, d - 1 - z];
				newSplatMap[x, z] = _copiedArea.SplatMap[x, d - 1 - z];
				if (newPathing != null)
				{
					newPathing[x, z] = _copiedArea.Pathing[x, d - 1 - z];
				}
			}
		}
		
		_copiedArea.Cells = newCells;
		_copiedArea.SplatMap = newSplatMap;
		if (newPathing != null)
		{
			_copiedArea.Pathing = newPathing;
		}
		
		ref var terrain = ref GetTerrainState();
		float quadSize = terrain.QuadSize;
		foreach (var ent in _copiedArea.Entities)
		{
			ent.RelativePos = new Vector3(ent.RelativePos.X, ent.RelativePos.Y, (d - 1) * quadSize - ent.RelativePos.Z);
			ent.Rotation = 180.0f - ent.Rotation;
		}
	}

	public void MirrorCopiedAreaHorizontally()
	{
		if (_copiedArea == null) return;
		int w = _copiedArea.Width;
		int d = _copiedArea.Depth;
		
		var newCells = new TerrainCell[w, d];
		var newSplatMap = new TerrainSplatWeights[w, d];
		var newPathing = _copiedArea.Pathing != null ? new int[w, d] : null;

		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				newCells[x, z] = _copiedArea.Cells[w - 1 - x, z];
				newSplatMap[x, z] = _copiedArea.SplatMap[w - 1 - x, z];
				if (newPathing != null)
				{
					newPathing[x, z] = _copiedArea.Pathing[w - 1 - x, z];
				}
			}
		}
		
		_copiedArea.Cells = newCells;
		_copiedArea.SplatMap = newSplatMap;
		if (newPathing != null)
		{
			_copiedArea.Pathing = newPathing;
		}
		
		ref var terrain = ref GetTerrainState();
		float quadSize = terrain.QuadSize;
		foreach (var ent in _copiedArea.Entities)
		{
			ent.RelativePos = new Vector3((w - 1) * quadSize - ent.RelativePos.X, ent.RelativePos.Y, ent.RelativePos.Z);
			ent.Rotation = -ent.Rotation;
		}
	}

	public PasteAreaResult BuildPasteAreaResult(
		int startX,
		int startZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		MirrorMode mirrorMode,
		float rotationDegrees)
	{
		var result = new PasteAreaResult();
		result.SpawnRequests = new List<EntitySpawnRequest>();

		if (_copiedArea == null) return result;

		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		bool modified = false;
		bool pathingModified = false;

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				int rotX = sx;
				int rotZ = sz;

				if (rotSteps == 1)
				{
					rotX = pasteDepth - 1 - sz;
					rotZ = sx;
				}
				else if (rotSteps == 2)
				{
					rotX = pasteWidth - 1 - sx;
					rotZ = pasteDepth - 1 - sz;
				}
				else if (rotSteps == 3)
				{
					rotX = sz;
					rotZ = pasteWidth - 1 - sx;
				}
				
				int dX = 0;
				int dZ = 0;
				if (rotSteps == 1 || rotSteps == 3)
				{
					dX = (pasteWidth - pasteDepth) / 2;
					dZ = (pasteDepth - pasteWidth) / 2;
				}

				PasteCellRotated(sx, sz, rotX, rotZ, startX + dX, startZ + dZ, width, depth, pasteHeights, pasteTextures, pastePathing, mirrorMode, ref terrain, ref modified, ref pathingModified);
			}
		}
		if (modified && pasteTextures)
		{
			AlignSplatMapSlots(0, 0, width - 1, depth - 1);
		}
		result.TerrainModified = modified;
		result.HeightsModified = pasteHeights && modified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			int dX = 0;
			int dZ = 0;
			if (rotSteps == 1 || rotSteps == 3)
			{
				dX = (pasteWidth - pasteDepth) / 2;
				dZ = (pasteDepth - pasteWidth) / 2;
			}

			int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
			int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

			Vector3 pasteCenter = new Vector3((startX + dX + (targetWidth - 1) / 2.0f - width / 2.0f) * quadSize, 0, (startZ + dZ + (targetDepth - 1) / 2.0f - depth / 2.0f) * quadSize);

			float rad = rotationDegrees * Mathf.Pi / 180.0f;
			float cosR = Mathf.Cos(rad);
			float sinR = Mathf.Sin(rad);

			Vector3 originalCenterOffset = new Vector3((pasteWidth - 1) / 2.0f * quadSize, 0, (pasteDepth - 1) / 2.0f * quadSize);

			foreach (var ent in _copiedArea.Entities)
			{
				Vector3 relativeToCenter = ent.RelativePos - originalCenterOffset;
				
				float rx = relativeToCenter.X * cosR - relativeToCenter.Z * sinR;
				float rz = relativeToCenter.X * sinR + relativeToCenter.Z * cosR;

				Vector3 rotatedRelative = new Vector3(rx, 0, rz);
				Vector3 destPos = pasteCenter + rotatedRelative;

				destPos.Y = GetTerrainHeightAt(destPos);

				float finalRot = ent.Rotation - rotationDegrees;

				result.SpawnRequests.Add(new EntitySpawnRequest
				{
					Type = ent.Type,
					Id = ent.Id,
					Position = destPos,
					Rotation = finalRot,
					Scale = ent.Scale,
					IsEnemy = ent.IsEnemy
				});

				AddMirroredRequests(result.SpawnRequests, ent.Type, ent.Id, destPos, finalRot, ent.Scale, ent.IsEnemy, mirrorMode);
			}
		}

		return result;
	}

	public EraseAreaResult BuildEraseAreaResult(
		int minX, int minZ, int maxX, int maxZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		IEnumerable<Node3D> sceneChildren,
		Node3D previewNode)
	{
		var result = new EraseAreaResult();
		result.NodesToDelete = new List<Node3D>();

		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		bool terrainModified = false;
		bool pathingModified = false;

		if (pasteHeights || pasteTextures || (pastePathing && terrain.PathingCodes != null))
		{
			for (int sz = 0; sz <= selDepth; sz++)
			{
				for (int sx = 0; sx <= selWidth; sx++)
				{
					int targetX = minX + sx;
					int targetZ = minZ + sz;
					if (targetX >= 0 && targetX <= width && targetZ >= 0 && targetZ <= depth)
					{
						if (pasteHeights)
						{
							if (targetX < width && targetZ < depth && terrain.Cells != null)
							{
								terrain.Cells[targetX, targetZ] = default;
							}
						}
						if (sx < selWidth && sz < selDepth && targetX < width && targetZ < depth)
						{
							if (pasteTextures) _terrainSplatMap[targetX, targetZ] = TerrainSplatWeights.CreateSolid(3);
							if (pastePathing && terrain.PathingCodes != null)
							{
								terrain.PathingCodes[targetX, targetZ] = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING;
								pathingModified = true;
							}
						}
						terrainModified = true;
					}
				}
			}
			if (terrainModified && pasteTextures)
			{
				AlignSplatMapSlots(minX - 2, minZ - 2, maxX + 2, maxZ + 2);
			}
		}

		result.TerrainModified = terrainModified;
		result.HeightsModified = pasteHeights && terrainModified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			float minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
			float maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
			float minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
			float maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;

			foreach (var n3d in sceneChildren)
			{
				if (!GodotObject.IsInstanceValid(n3d) || n3d == previewNode) continue;
				Vector3 pos = n3d.Position;
				if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
				{
					if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
					{
						result.NodesToDelete.Add(n3d);
					}
				}
			}

			if (EcsWorldAccessor.Current != null)
			{
				var world = EcsWorldAccessor.Current;
				var staticPropsToDestroy = new List<(Arch.Core.Entity Entity, string PropId)>();
				var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
				world.Query(in propQuery, (Arch.Core.Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
				{
					if (GameHost.EntityToProp3D.ContainsKey(entity)) return;
					Vector3 wPos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
					if (wPos.X >= minWorldX && wPos.X <= maxWorldX && wPos.Z >= minWorldZ && wPos.Z <= maxWorldZ)
					{
						staticPropsToDestroy.Add((entity, propIdComp.PropId));
					}
				});

				foreach (var sp in staticPropsToDestroy)
				{
					if (world.IsAlive(sp.Entity))
					{
						world.Destroy(sp.Entity);
					}
					PropMultiMeshManager.Instance?.MarkDirty(sp.PropId);
				}
			}
		}

		return result;
	}

	private int GetDominantTextureIndex(TerrainSplatWeights splat)
	{
		float maxW = splat.Weight0;
		int idx = splat.Index0;
		if (splat.Weight1 > maxW)
		{
			maxW = splat.Weight1;
			idx = splat.Index1;
		}
		if (splat.Weight2 > maxW)
		{
			maxW = splat.Weight2;
			idx = splat.Index2;
		}
		if (splat.Weight3 > maxW)
		{
			maxW = splat.Weight3;
			idx = splat.Index3;
		}
		return idx;
	}

	private List<Vector2I> GetFloodFillCells(Vector3 clickPos, TerrainSplatWeights[,] splatBefore, int width, int depth, float quadSize, bool[,] visited, bool isCliff)
	{
		ref var terrain = ref GetTerrainState();
		var cells = new List<Vector2I>();

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int clickX = Mathf.Clamp((int)Math.Round(startFx), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Round(startFz), 0, depth - 1);
		int startDominantIndex = GetDominantTextureIndex(splatBefore[clickX, clickZ]);

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));

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
						if (GetDominantTextureIndex(splatBefore[nextX, nextZ]) != startDominantIndex) continue;
						if (!isCliff)
						{
							float hCurrent = terrain.Cells != null ? terrain.Cells[Math.Clamp(currX, 0, width - 1), Math.Clamp(currZ, 0, depth - 1)].Y_NW : 0.0f;
							float hNext = terrain.Cells != null ? terrain.Cells[Math.Clamp(nextX, 0, width - 1), Math.Clamp(nextZ, 0, depth - 1)].Y_NW : 0.0f;
							if (Mathf.Abs(hNext - hCurrent) >= 1.0f) continue;
						}
						visited[nextX, nextZ] = true;
						queue.Enqueue((nextX, nextZ));
					}
				}
			}
		}

		return cells;
	}

	private List<Vector2I> GetFloodFillArea(
		Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		bool isCliff,
		Func<int, int, bool> shouldFillCell)
	{
		var cells = new List<Vector2I>();

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Mathf.Round(startFx), 0, width - 1);
		int startZ = Mathf.Clamp((int)Mathf.Round(startFz), 0, depth - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillCells(clickPos, splatBefore, width, depth, quadSize, visited, isCliff));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Round(mFx), 0, width - 1);
				int mZ = Mathf.Clamp((int)Math.Round(mFz), 0, depth - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillCells(m, splatBefore, width, depth, quadSize, visited, isCliff));
				}
			}
		}

		return cells;
	}

	public (float[,]? Heights, TerrainSplatWeights[,]? SplatMap, bool IsCliff) PerformFloodFill(
		Vector3 clickPos,
		int fillTextureIndex,
		int cliffTextureIndex,
		MirrorMode mirrorMode,
		bool isCliff = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return (null, null, isCliff);

		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		if (_terrainSplatMap == null) return (null, null, isCliff);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		TerrainSplatWeights[,] targetSplatMap = (isCliff && _terrainCliffSplatMap != null) ? _terrainCliffSplatMap : _terrainSplatMap;
		int targetTextureIndex = isCliff ? cliffTextureIndex : fillTextureIndex;

		var splatBefore = (TerrainSplatWeights[,])targetSplatMap.Clone();
		var visited = new bool[width, depth];

		var cells = GetFloodFillArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			isCliff,
			(x, z) => splatBefore[x, z].Index0 != targetTextureIndex
		);

		foreach (var cell in cells)
		{
			targetSplatMap[cell.X, cell.Y] = TerrainSplatWeights.CreateSolid(targetTextureIndex);
		}

		if (cells.Count > 0)
		{
			int minGridX = width;
			int maxGridX = 0;
			int minGridZ = depth;
			int maxGridZ = 0;
			foreach (var cell in cells)
			{
				if (cell.X < minGridX) minGridX = cell.X;
				if (cell.X > maxGridX) maxGridX = cell.X;
				if (cell.Y < minGridZ) minGridZ = cell.Y;
				if (cell.Y > maxGridZ) maxGridZ = cell.Y;
			}
			AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);
		}

		return ((float[,])terrain.Heights.Clone(), (TerrainSplatWeights[,])targetSplatMap.Clone(), isCliff);
	}

	public (int[,]? Before, int[,]? After) PerformFloodFillPathing(Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		var splatBefore = (TerrainSplatWeights[,])_terrainSplatMap.Clone();
		var pathingBefore = (int[,])terrain.PathingCodes.Clone();
		var visited = new bool[width, depth];

		var pathingCodes = terrain.PathingCodes;

		int targetValue = pathingAdd ? pathingMask : 0;

		var cells = GetFloodFillArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			isCliff: false,
			(x, z) => pathingBefore[x, z] != targetValue
		);

		foreach (var cell in cells)
		{
			if (pathingAdd)
				pathingCodes[cell.X, cell.Y] |= pathingMask;
			else
				pathingCodes[cell.X, cell.Y] &= ~pathingMask;
		}

		return (pathingBefore, (int[,])pathingCodes.Clone());
	}

	public List<GameHost.MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation, MirrorMode mirrorMode)
	{
		var list = new List<GameHost.MirroredTransform>();
		if (mirrorMode == MirrorMode.None) return list;

		if (mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both)
		{
			list.Add(new GameHost.MirroredTransform { Position = new Vector3(-pos.X, pos.Y, pos.Z), Rotation = 180.0f - rotation });
		}
		if (mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both)
		{
			list.Add(new GameHost.MirroredTransform { Position = new Vector3(pos.X, pos.Y, -pos.Z), Rotation = -rotation });
		}
		if (mirrorMode == MirrorMode.Both)
		{
			list.Add(new GameHost.MirroredTransform { Position = new Vector3(-pos.X, pos.Y, -pos.Z), Rotation = rotation + 180.0f });
		}

		return list;
	}

	public (int minX, int minZ, int maxX, int maxZ) GetCurrentSelectionBounds()
	{
		if (_selectionStart == null || _selectionEnd == null) return (0, 0, 0, 0);
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		return (minX, minZ, maxX, maxZ);
	}

	public (int cx, int cz) WorldPosToCellCoords(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int cx = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
		int cz = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);
		return (cx, cz);
	}

	public Vector3 SnapToGrid(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		worldPos.X = (Mathf.Clamp(fx, 0, width) - width / 2.0f) * quadSize;
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		worldPos.Z = (Mathf.Clamp(fz, 0, depth) - depth / 2.0f) * quadSize;
		return worldPos;
	}

	private ref TerrainState GetTerrainState()
	{
		var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
		Entity worldEntity = Entity.Null;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity))
		{
			return ref EcsWorld.Get<TerrainState>(worldEntity);
		}
		throw new InvalidOperationException("TerrainState entity not found in ECS world.");
	}

	private float GetMinHeightInBrushBoundsInternal(Vector3 worldPos, float brushRadius = -1f, bool brushIsSquare = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return GetTerrainHeightAt(worldPos);

		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float vx = (x - width / 2.0f) * quadSize;
				float vz = (z - depth / 2.0f) * quadSize;

				bool inBounds = false;
				if (brushRadius < 0)
				{
					inBounds = true;
				}
				else if (brushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= brushRadius && dz <= brushRadius;
				}
				else
				{
					float dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= brushRadius;
				}

				if (inBounds)
				{
					float h = GetGridNodeHeight(in terrain, x, z);
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

	private void AddMirroredRequests(
		List<EntitySpawnRequest> requests,
		string type, string id,
		Vector3 pos, float rotation, float scale,
		bool isEnemy,
		MirrorMode mirrorMode)
	{
		if (mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both)
		{
			Vector3 mPos = new Vector3(-pos.X, pos.Y, pos.Z);
			mPos.Y = GetTerrainHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = 180.0f - rotation, Scale = scale, IsEnemy = isEnemy });
		}
		if (mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both)
		{
			Vector3 mPos = new Vector3(pos.X, pos.Y, -pos.Z);
			mPos.Y = GetTerrainHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = -rotation, Scale = scale, IsEnemy = isEnemy });
		}
		if (mirrorMode == MirrorMode.Both)
		{
			Vector3 mPos = new Vector3(-pos.X, pos.Y, -pos.Z);
			mPos.Y = GetTerrainHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = rotation + 180.0f, Scale = scale, IsEnemy = isEnemy });
		}
	}

	private List<Vector3> GetMirroredPositions(Vector3 pos, MirrorMode mirrorMode)
	{
		var list = new List<Vector3>();
		if (mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both)
			list.Add(new Vector3(-pos.X, pos.Y, pos.Z));
		if (mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both)
			list.Add(new Vector3(pos.X, pos.Y, -pos.Z));
		if (mirrorMode == MirrorMode.Both)
			list.Add(new Vector3(-pos.X, pos.Y, -pos.Z));
		return list;
	}

	private void PasteCellRotated(
		int srcX, int srcZ,
		int rotX, int rotZ,
		int startX, int startZ,
		int width, int depth,
		bool pasteHeights, bool pasteTextures, bool pastePathing,
		MirrorMode mirrorMode,
		ref TerrainState terrain,
		ref bool modified,
		ref bool pathingModified)
	{
		int targetX = startX + rotX;
		int targetZ = startZ + rotZ;

		var cells = terrain.Cells;
		var srcCells = _copiedArea.Cells;

		if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth && srcX < _copiedArea.Width && srcZ < _copiedArea.Depth)
		{
			if (pasteHeights && srcCells != null)
			{
				cells[targetX, targetZ] = srcCells[srcX, srcZ];
			}
			if (pasteTextures) _terrainSplatMap[targetX, targetZ] = _copiedArea.SplatMap[srcX, srcZ];
			if (pastePathing && _copiedArea.Pathing != null && terrain.PathingCodes != null)
			{
				terrain.PathingCodes[targetX, targetZ] = _copiedArea.Pathing[srcX, srcZ];
				pathingModified = true;
			}
			modified = true;
		}

		if (mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both)
		{
			int mx = width - 1 - targetX;
			int mz = targetZ;
			if (mx >= 0 && mx < width && mz >= 0 && mz < depth && srcX < _copiedArea.Width && srcZ < _copiedArea.Depth)
			{
				if (pasteHeights && srcCells != null)
				{
					cells[mx, mz] = srcCells[srcX, srcZ];
				}
				if (pasteTextures) _terrainSplatMap[mx, mz] = _copiedArea.SplatMap[srcX, srcZ];
				if (pastePathing && _copiedArea.Pathing != null && terrain.PathingCodes != null)
				{
					terrain.PathingCodes[mx, mz] = _copiedArea.Pathing[srcX, srcZ];
					pathingModified = true;
				}
				modified = true;
			}
		}

		if (mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both)
		{
			int mx = targetX;
			int mz = depth - 1 - targetZ;
			if (mx >= 0 && mx < width && mz >= 0 && mz < depth && srcX < _copiedArea.Width && srcZ < _copiedArea.Depth)
			{
				if (pasteHeights && srcCells != null)
				{
					cells[mx, mz] = srcCells[srcX, srcZ];
				}
				if (pasteTextures) _terrainSplatMap[mx, mz] = _copiedArea.SplatMap[srcX, srcZ];
				if (pastePathing && _copiedArea.Pathing != null && terrain.PathingCodes != null)
				{
					terrain.PathingCodes[mx, mz] = _copiedArea.Pathing[srcX, srcZ];
					pathingModified = true;
				}
				modified = true;
			}
		}

		if (mirrorMode == MirrorMode.Both)
		{
			int mx = width - 1 - targetX;
			int mz = depth - 1 - targetZ;
			if (mx >= 0 && mx < width && mz >= 0 && mz < depth && srcX < _copiedArea.Width && srcZ < _copiedArea.Depth)
			{
				if (pasteHeights && srcCells != null)
				{
					cells[mx, mz] = srcCells[srcX, srcZ];
				}
				if (pasteTextures) _terrainSplatMap[mx, mz] = _copiedArea.SplatMap[srcX, srcZ];
				if (pastePathing && _copiedArea.Pathing != null && terrain.PathingCodes != null)
				{
					terrain.PathingCodes[mx, mz] = _copiedArea.Pathing[srcX, srcZ];
					pathingModified = true;
				}
				modified = true;
			}
		}
	}

	public bool GetBlockMode(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.BlockMode, true);
	}

	public void SetBlockMode(Entity worldEntity, bool value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockMode = value);
	}

	public float GetBlockLevelHeight(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.BlockLevelHeight, 3.0f);
	}

	public void SetBlockLevelHeight(Entity worldEntity, float value)
	{
		float clamped = Math.Clamp((float)Math.Round(value / 3.0f) * 3.0f, 3.0f, 16.0f);
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockLevelHeight = clamped);
	}

	public WaterType GetWaterMode(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, WaterType>(worldEntity, s => s.WaterMode, WaterType.None);
	}

	public void SetWaterMode(Entity worldEntity, WaterType value)
	{
		if (EcsWorld != null && EcsWorld.IsAlive(worldEntity))
		{
			if (EcsWorld.Has<EditorState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<EditorState>(worldEntity);
				state.WaterMode = value;
			}
			else
			{
				EcsWorld.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "Assets/skyboxes/jade_shrine.png", false, MirrorMode.None, value));
			}
		}
	}

	public float GetCameraBoundsLeft(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsLeft, -95.0f);
	}

	public void SetCameraBoundsLeft(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsLeft = value);
	}

	public float GetCameraBoundsRight(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsRight, 95.0f);
	}

	public void SetCameraBoundsRight(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsRight = value);
	}

	public float GetCameraBoundsTop(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsTop, -95.0f);
	}

	public void SetCameraBoundsTop(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsTop = value);
	}

	public float GetCameraBoundsBottom(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsBottom, 125.0f);
	}

	public void SetCameraBoundsBottom(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsBottom = value);
	}

	public string GetSkyboxPath(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, string>(worldEntity, s => s.SkyboxPath, "Assets/skyboxes/jade_shrine.png");
	}

	public void SetSkyboxPath(Entity worldEntity, string value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.SkyboxPath = value);
	}

	public bool GetHasUnsavedChanges(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.HasUnsavedChanges, false);
	}

	public void SetHasUnsavedChanges(Entity worldEntity, bool value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.HasUnsavedChanges = value);
	}

	public string GetTerrainStatusString(Vector3 pos, string toolName, string activePlaceId)
	{
		string formattedToolName = toolName.ToUpper();
		if (!string.IsNullOrEmpty(activePlaceId))
		{
			string shortId = System.IO.Path.GetFileName(activePlaceId).ToUpper();
			formattedToolName += $" ({shortId})";
		}

		string status = $"ACTIVE TOOL: {formattedToolName} | Pos: {pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}";

		ref var terrain = ref GetTerrainState();

		if (terrain.Cells != null && toolName.Equals("PaintPathing", StringComparison.OrdinalIgnoreCase))
		{
			float fx = pos.X / terrain.QuadSize + terrain.Width / 2.0f;
			float fz = pos.Z / terrain.QuadSize + terrain.Depth / 2.0f;
			int cx = Mathf.Clamp((int)Mathf.Round(fx), 0, terrain.Width - 1);
			int cz = Mathf.Clamp((int)Mathf.Round(fz), 0, terrain.Depth - 1);

			if (terrain.PathingCodes != null)
			{
				int code = terrain.PathingCodes[cx, cz];
				var layers = new List<string>();
				if ((code & EditableTerrain.PATHING_GROUND) != 0)
				{
					layers.Add("Ground");
				}
				if ((code & EditableTerrain.PATHING_FLYING) != 0)
				{
					layers.Add("Flying");
				}
				if ((code & EditableTerrain.PATHING_SHALLOW_WATER) != 0)
				{
					layers.Add("Shallow Water");
				}
				if ((code & EditableTerrain.PATHING_DEEP_WATER) != 0)
				{
					layers.Add("Deep Water");
				}
				if ((code & EditableTerrain.PATHING_BUILDABLE) != 0)
				{
					layers.Add("Buildable");
				}

				string layersStr = layers.Count > 0 ? string.Join(", ", layers) : "None";
				status += $" | Path: {layersStr}";
			}
		}

		return status;
	}

	public void AlignSplatMapSlots(int minX, int minZ, int maxX, int maxZ)
	{
		if (_terrainSplatMap != null) AlignSplatMapMatrix(_terrainSplatMap, minX, minZ, maxX, maxZ);
		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}
		if (_terrainCliffSplatMap != null) AlignSplatMapMatrix(_terrainCliffSplatMap, minX, minZ, maxX, maxZ);
	}

	private void AlignSplatMapMatrix(TerrainSplatWeights[,] splatMatrix, int minX, int minZ, int maxX, int maxZ)
	{
		int width = splatMatrix.GetLength(0);
		int depth = splatMatrix.GetLength(1);

		minX = Math.Max(0, minX);
		minZ = Math.Max(0, minZ);
		maxX = Math.Min(width - 1, maxX);
		maxZ = Math.Min(depth - 1, maxZ);

		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dz == 0) continue;
						int nx = x + dx;
						int nz = z + dz;
						if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
						{
							var neighbor = splatMatrix[nx, nz];
							if (neighbor.Weight0 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index0);
							if (neighbor.Weight1 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index1);
							if (neighbor.Weight2 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index2);
							if (neighbor.Weight3 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index3);
						}
					}
				}
			}
		}
	}

	private void TryAddIndexToUnusedSlot(TerrainSplatWeights[,] splatMatrix, int x, int z, int index)
	{
		var current = splatMatrix[x, z];

		if (current.Index0 == index || current.Index1 == index || current.Index2 == index || current.Index3 == index)
		{
			return;
		}

		int preferredSlot = index % 4;
		bool slotAvailable = preferredSlot switch
		{
			0 => current.Weight0 <= 0.001f,
			1 => current.Weight1 <= 0.001f,
			2 => current.Weight2 <= 0.001f,
			3 => current.Weight3 <= 0.001f,
			_ => false
		};

		if (slotAvailable)
		{
			switch (preferredSlot)
			{
				case 0: current.Index0 = index; current.Weight0 = 0.0f; break;
				case 1: current.Index1 = index; current.Weight1 = 0.0f; break;
				case 2: current.Index2 = index; current.Weight2 = 0.0f; break;
				case 3: current.Index3 = index; current.Weight3 = 0.0f; break;
			}
			splatMatrix[x, z] = current;
			return;
		}

		if (current.Weight0 <= 0.001f) { current.Index0 = index; current.Weight0 = 0.0f; splatMatrix[x, z] = current; return; }
		if (current.Weight1 <= 0.001f) { current.Index1 = index; current.Weight1 = 0.0f; splatMatrix[x, z] = current; return; }
		if (current.Weight2 <= 0.001f) { current.Index2 = index; current.Weight2 = 0.0f; splatMatrix[x, z] = current; return; }
		if (current.Weight3 <= 0.001f) { current.Index3 = index; current.Weight3 = 0.0f; splatMatrix[x, z] = current; return; }
	}
}
