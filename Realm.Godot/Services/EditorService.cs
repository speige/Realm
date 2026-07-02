using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

public class EditorService
{
	private readonly World _ecsWorld;
	private Color[,] _terrainColors;

	private float _clumpSpawnCooldown = 0.0f;
	private bool _isDrawingClump = false;
	private readonly List<IEditorAction> _clumpSpawnActionsInSession = new();

	private bool _hasBlockTargetHeight = false;
	private float _activeBlockTargetHeight = 0.0f;
	private float? _activeCliffHeight = null;

	private float[,] _terrainHeightsBefore;
	private Color[,] _terrainColorsBefore;
	private int[,] _terrainPathingBefore;
	private bool _isDrawingTerrain = false;

	private CopiedAreaTemplate _copiedArea = null;

	private Vector2I? _selectionStart = null;
	private Vector2I? _selectionEnd = null;
	private bool _isSelectingArea = false;

	private float _cachedRandomRotation = 0.0f;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom = false;
	private bool _isPastingObject = false;

	private CopiedObjectTemplate? _copiedObject = null;
	private Vector3? _rampStartPos = null;

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
		public float[,] Heights;
		public Color[,] Colors;
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
		public bool ColorsModified;
		public bool PathingModified;
	}

	public struct PasteAreaResult
	{
		public bool TerrainModified;
		public bool HeightsModified;
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
		public List<Node3D> NodesToDelete;
	}

	public EditorService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	public void SetTerrainColors(Color[,] colors)
	{
		_terrainColors = colors;
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

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return 0.0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;

		float fx = worldPos.X / spacing + (width - 1) / 2.0f;
		float fz = worldPos.Z / spacing + (depth - 1) / 2.0f;

		int x = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
		int z = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);

		return terrain.Heights[x, z];
	}

	public TerrainEditResult ApplyContinuousTerrainEditing(
		Vector3 worldPos,
		float delta,
		GameHost.EditorTool activeTool,
		float brushRadius,
		float brushStrength,
		float flattenHeight,
		bool brushIsSquare,
		bool blockMode,
		float blockLevelHeight,
		Color paintColor,
		Color cliffPaintColor,
		int pathingMask,
		bool pathingAdd)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return default;

		bool isHeights = activeTool == GameHost.EditorTool.Raise ||
						 activeTool == GameHost.EditorTool.Lower ||
						 activeTool == GameHost.EditorTool.Flatten ||
						 activeTool == GameHost.EditorTool.Smooth ||
						 activeTool == GameHost.EditorTool.Cliff ||
						 activeTool == GameHost.EditorTool.Noise;

		bool isPaint = activeTool == GameHost.EditorTool.PaintGrass ||
					   activeTool == GameHost.EditorTool.PaintDirt ||
					   activeTool == GameHost.EditorTool.PaintRock ||
					   activeTool == GameHost.EditorTool.PaintSand;

		bool isPathing = activeTool == GameHost.EditorTool.PaintPathing;

		if (!isHeights && !isPaint && !isPathing) return default;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;

		bool modified = false;
		var result = new TerrainEditResult();

		if (blockMode)
		{
			int cx = Mathf.Clamp((int)Math.Round(worldPos.X / spacing + (width - 1) / 2.0f), 0, width - 1);
			int cz = Mathf.Clamp((int)Math.Round(worldPos.Z / spacing + (depth - 1) / 2.0f), 0, depth - 1);
			int brushGridRadius = Mathf.Max(0, (int)Math.Round(brushRadius / spacing));

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
							if (!brushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}

							if (inBounds)
							{
								if (activeTool == GameHost.EditorTool.Raise ||
									activeTool == GameHost.EditorTool.Lower ||
									activeTool == GameHost.EditorTool.Flatten ||
									activeTool == GameHost.EditorTool.Cliff)
								{
									terrain.Heights[x, z] = Mathf.Clamp(targetHeight, -10.0f, 50.0f);
									modified = true;
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
											if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
											{
												avg += terrain.Heights[nxVal, nzVal];
												count++;
											}
										}
									}
									avg /= count;
									float snappedAvg = Mathf.Round(avg / blockLevelHeight) * blockLevelHeight;
									terrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(terrain.Heights[x, z], snappedAvg, brushStrength * delta * 2.0f), -10.0f, 50.0f);
									modified = true;
								}
								else if (activeTool == GameHost.EditorTool.Noise)
								{
									if (GD.Randf() < 0.15f * brushStrength * delta)
									{
										float direction = GD.Randf() > 0.5f ? 1.0f : -1.0f;
										terrain.Heights[x, z] = Mathf.Clamp(terrain.Heights[x, z] + direction * blockLevelHeight, -10.0f, 50.0f);
										modified = true;
									}
								}
							}
						}
					}
				}

				if (modified && activeTool != GameHost.EditorTool.Smooth && activeTool != GameHost.EditorTool.Flatten && activeTool != GameHost.EditorTool.Noise)
				{
					for (int z = cz - brushGridRadius - 1; z <= cz + brushGridRadius + 1; z++)
					{
						for (int x = cx - brushGridRadius - 1; x <= cx + brushGridRadius + 1; x++)
						{
							if (x >= 0 && x < width && z >= 0 && z < depth)
							{
								float h = terrain.Heights[x, z];
								float hl = terrain.Heights[Math.Max(0, x - 1), z];
								float hr = terrain.Heights[Math.Min(width - 1, x + 1), z];
								float hd = terrain.Heights[x, Math.Max(0, z - 1)];
								float hu = terrain.Heights[x, Math.Min(depth - 1, z + 1)];

								float maxDiff = Mathf.Max(
									Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
									Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
								);

								if (maxDiff >= blockLevelHeight * 0.5f)
								{
									_terrainColors[x, z] = cliffPaintColor;
								}
								else
								{
									bool insideBrush = true;
									if (!brushIsSquare)
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
										_terrainColors[x, z] = paintColor;
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
							if (!brushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}

							if (inBounds)
							{
								float h = terrain.Heights[x, z];
								float hl = terrain.Heights[Math.Max(0, x - 1), z];
								float hr = terrain.Heights[Math.Min(width - 1, x + 1), z];
								float hd = terrain.Heights[x, Math.Max(0, z - 1)];
								float hu = terrain.Heights[x, Math.Min(depth - 1, z + 1)];

								float maxDiff = Mathf.Max(
									Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
									Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
								);

								Color baseColor = (maxDiff >= blockLevelHeight * 0.5f) ? cliffPaintColor : paintColor;
								float targetAlpha = baseColor.A;
								Color targetColor = new Color(baseColor.R, baseColor.G, baseColor.B, targetAlpha);
								_terrainColors[x, z] = _terrainColors[x, z].Lerp(targetColor, brushStrength * delta * 5.0f);
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
							if (!brushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}

							if (inBounds)
							{
								if (pathingAdd)
									terrain.PathingCodes[x, z] |= pathingMask;
								else
									terrain.PathingCodes[x, z] &= ~pathingMask;
								modified = true;
							}
						}
					}
				}
			}
		}
		else
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float vx = (x - (width - 1) / 2.0f) * spacing;
					float vz = (z - (depth - 1) / 2.0f) * spacing;

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
							if (activeTool == GameHost.EditorTool.Raise)
							{
								terrain.Heights[x, z] = Mathf.Clamp(terrain.Heights[x, z] + brushStrength * falloff * delta, -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Lower)
							{
								terrain.Heights[x, z] = Mathf.Clamp(terrain.Heights[x, z] - brushStrength * falloff * delta, -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Flatten)
							{
								terrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(terrain.Heights[x, z], flattenHeight, brushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
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
										if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
										{
											avg += terrain.Heights[nxVal, nzVal];
											count++;
										}
									}
								}
								avg /= count;
								terrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(terrain.Heights[x, z], avg, brushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Cliff)
							{
								float targetHeight = _activeCliffHeight ?? 4.0f;
								terrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(terrain.Heights[x, z], targetHeight, brushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
							}
							else if (activeTool == GameHost.EditorTool.Noise)
							{
								float noiseVal = (float)(GD.Randf() * 2.0 - 1.0) * brushStrength * falloff * delta * 2.0f;
								terrain.Heights[x, z] = Mathf.Clamp(terrain.Heights[x, z] + noiseVal, -10.0f, 50.0f);
							}
							modified = true;
						}
						else if (isPaint)
						{
							float h = terrain.Heights[x, z];
							float hl = terrain.Heights[Math.Max(0, x - 1), z];
							float hr = terrain.Heights[Math.Min(width - 1, x + 1), z];
							float hd = terrain.Heights[x, Math.Max(0, z - 1)];
							float hu = terrain.Heights[x, Math.Min(depth - 1, z + 1)];

							float maxDiff = Mathf.Max(
								Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
								Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
							);

							Color baseColor = (maxDiff >= spacing * 0.5f) ? cliffPaintColor : paintColor;
							float targetAlpha = baseColor.A;
							Color targetColor = new Color(baseColor.R, baseColor.G, baseColor.B, targetAlpha);
							_terrainColors[x, z] = _terrainColors[x, z].Lerp(targetColor, brushStrength * falloff * delta * 3.0f);
							modified = true;
						}
						else if (isPathing)
						{
							int icx = Mathf.Clamp((int)Math.Round(vx / spacing + (width - 1) / 2.0f), 0, width - 1);
							int icz = Mathf.Clamp((int)Math.Round(vz / spacing + (depth - 1) / 2.0f), 0, depth - 1);
							if (pathingAdd)
								terrain.PathingCodes[icx, icz] |= pathingMask;
							else
								terrain.PathingCodes[icx, icz] &= ~pathingMask;
							modified = true;
						}
					}
				}
			}
		}

		if (modified)
		{
			result.HeightsModified = isHeights;
			result.ColorsModified = isPaint || (isHeights && !blockMode);
			result.PathingModified = isPathing;
		}

		return result;
	}

	public void BeginTerrainDraw(
		Vector3 hitPos,
		GameHost.EditorTool activeTool,
		bool blockMode,
		float blockLevelHeight,
		float flattenHeight,
		float[,] currentHeights,
		Color[,] currentColors,
		int[,] currentPathing,
		out float newFlattenHeight)
	{
		_isDrawingTerrain = true;
		_terrainHeightsBefore = (float[,])currentHeights.Clone();
		_terrainColorsBefore = (Color[,])currentColors.Clone();
		_terrainPathingBefore = (int[,])currentPathing.Clone();

		newFlattenHeight = flattenHeight;

		if (activeTool == GameHost.EditorTool.Flatten)
		{
			newFlattenHeight = GetMinHeightInBrushBoundsInternal(hitPos);
		}

		if (blockMode)
		{
			ref var terrain = ref GetTerrainState();
			float startHeight = GetTerrainHeightAt(hitPos);
			if (activeTool == GameHost.EditorTool.Raise)
			{
				_activeBlockTargetHeight = (Mathf.Floor(startHeight / blockLevelHeight) + 1.0f) * blockLevelHeight;
				_hasBlockTargetHeight = true;
			}
			else if (activeTool == GameHost.EditorTool.Lower)
			{
				_activeBlockTargetHeight = (Mathf.Ceil(startHeight / blockLevelHeight) - 1.0f) * blockLevelHeight;
				_hasBlockTargetHeight = true;
			}
			else if (activeTool == GameHost.EditorTool.Flatten)
			{
				_activeBlockTargetHeight = Mathf.Round(newFlattenHeight / blockLevelHeight) * blockLevelHeight;
				_hasBlockTargetHeight = true;
			}
			else if (activeTool == GameHost.EditorTool.Cliff)
			{
				bool lower = Input.IsKeyPressed(Key.Shift);
				_activeBlockTargetHeight = lower
					? (Mathf.Ceil(startHeight / blockLevelHeight) - 1.0f) * blockLevelHeight
					: (Mathf.Floor(startHeight / blockLevelHeight) + 1.0f) * blockLevelHeight;
				_hasBlockTargetHeight = true;
			}
		}
	}

	public TerrainModifyAction EndTerrainDraw(float[,] currentHeights, Color[,] currentColors, int[,] currentPathing)
	{
		_isDrawingTerrain = false;
		_hasBlockTargetHeight = false;
		_activeCliffHeight = null;

		var action = new TerrainModifyAction(
			_terrainHeightsBefore, currentHeights,
			_terrainColorsBefore, currentColors,
			_terrainPathingBefore, currentPathing);

		return action;
	}

	public void BeginCliffIfNeeded(Vector3 hitPos, bool blockMode, float blockLevelHeight)
	{
		if (_activeCliffHeight == null && !blockMode)
		{
			float startHeight = GetTerrainHeightAt(hitPos);
			bool lower = Input.IsKeyPressed(Key.Shift);
			_activeCliffHeight = lower
				? (Mathf.Ceil(startHeight / 4.0f) - 1.0f) * 4.0f
				: (Mathf.Floor(startHeight / 4.0f) + 1.0f) * 4.0f;
		}
	}

	public void ResetDrawState()
	{
		_activeCliffHeight = null;
		_hasBlockTargetHeight = false;
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
	}

	public void RecordClumpSpawnAction(ObjectSpawnAction action)
	{
		_clumpSpawnActionsInSession.Add(action);
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
		float clumpDensity,
		float clumpScaleVar,
		float brushRadius,
		bool brushIsSquare,
		bool randomRotation,
		bool randomScale,
		float placementRotation,
		MirrorMode mirrorMode)
	{
		if (string.IsNullOrEmpty(activePlaceId)) return new List<EntitySpawnRequest>();

		var requests = new List<EntitySpawnRequest>();
		int spawnCount = Mathf.Max(1, (int)Math.Round(clumpDensity));

		ref var terrain = ref GetTerrainState();
		float spacing = terrain.Heights != null ? terrain.Spacing : 1f;
		int terrainWidth = terrain.Heights != null ? terrain.Width : 0;
		int terrainDepth = terrain.Heights != null ? terrain.Depth : 0;
		float halfW = (terrainWidth - 1) / 2.0f * spacing;
		float halfD = (terrainDepth - 1) / 2.0f * spacing;

		for (int i = 0; i < spawnCount; i++)
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
			if (terrain.Heights != null)
			{
				if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
			}
			spawnPos.Y = GetTerrainHeightAt(spawnPos);

			float scaleVal = placementScale + (float)(GD.Randf() * 2.0 - 1.0) * clumpScaleVar;
			scaleVal = Mathf.Clamp(scaleVal, 0.2f, 3.0f);

			float rotY = (randomRotation && !_isPastingObject) ? (float)(GD.Randf() * 360.0) : placementRotation;
			if (randomScale && !_isPastingObject)
			{
				scaleVal = 0.2f + (float)(GD.Randf() * 2.8);
			}

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
		}

		return requests;
	}

	public bool ApplyRamp(Vector3 start, Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight, Color paintColor, Color cliffPaintColor)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return false;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;
		bool modified = false;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;
				float abLenSqr = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
				if (abLenSqr > 0.0001f)
				{
					float t = ((vx - start.X) * (end.X - start.X) + (vz - start.Z) * (end.Z - start.Z)) / abLenSqr;
					t = Mathf.Clamp(t, 0.0f, 1.0f);
					float projX = start.X + t * (end.X - start.X);
					float projZ = start.Z + t * (end.Z - start.Z);
					float dist = Mathf.Sqrt((vx - projX) * (vx - projX) + (vz - projZ) * (vz - projZ));
					if (dist <= brushRadius)
					{
						float targetHeight = Mathf.Lerp(start.Y, end.Y, t);
						float falloff = 1.0f - (dist / brushRadius);
						falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);
						terrain.Heights[x, z] = Mathf.Lerp(terrain.Heights[x, z], targetHeight, falloff);
						modified = true;
					}
				}
			}
		}

		if (modified)
		{
			float threshold = blockMode ? (blockLevelHeight * 0.5f) : (spacing * 0.5f);
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float vx = (x - (width - 1) / 2.0f) * spacing;
					float vz = (z - (depth - 1) / 2.0f) * spacing;
					float abLenSqr = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
					if (abLenSqr > 0.0001f)
					{
						float t = ((vx - start.X) * (end.X - start.X) + (vz - start.Z) * (end.Z - start.Z)) / abLenSqr;
						t = Mathf.Clamp(t, 0.0f, 1.0f);
						float projX = start.X + t * (end.X - start.X);
						float projZ = start.Z + t * (end.Z - start.Z);
						float dist = Mathf.Sqrt((vx - projX) * (vx - projX) + (vz - projZ) * (vz - projZ));
						if (dist <= brushRadius)
						{
							float h = terrain.Heights[x, z];
							float hl = terrain.Heights[Math.Max(0, x - 1), z];
							float hr = terrain.Heights[Math.Min(width - 1, x + 1), z];
							float hd = terrain.Heights[x, Math.Max(0, z - 1)];
							float hu = terrain.Heights[x, Math.Min(depth - 1, z + 1)];
							float maxDiff = Mathf.Max(
								Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
								Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
							);
							_terrainColors[x, z] = maxDiff >= threshold ? cliffPaintColor : paintColor;
						}
					}
				}
			}
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
		if (terrain.Heights == null) return;

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var heights = new float[selWidth, selDepth];
		var colors = new Color[selWidth, selDepth];

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				heights[sx, sz] = terrain.Heights[minX + sx, minZ + sz];
				colors[sx, sz] = _terrainColors[minX + sx, minZ + sz];
			}
		}

		_copiedArea = new CopiedAreaTemplate
		{
			Width = selWidth,
			Depth = selDepth,
			Heights = heights,
			Colors = colors,
			Entities = entities
		};
	}

	public List<CopiedEntityInfo> BuildCopiedEntityList(
		int minX, int minZ, int maxX, int maxZ,
		IEnumerable<Node3D> sceneChildren)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return new List<CopiedEntityInfo>();

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;

		float minWorldX = (minX - (width - 1) / 2.0f) * spacing - spacing * 0.5f;
		float maxWorldX = (maxX - (width - 1) / 2.0f) * spacing + spacing * 0.5f;
		float minWorldZ = (minZ - (depth - 1) / 2.0f) * spacing - spacing * 0.5f;
		float maxWorldZ = (maxZ - (depth - 1) / 2.0f) * spacing + spacing * 0.5f;
		Vector3 origin = new Vector3((minX - (width - 1) / 2.0f) * spacing, 0.0f, (minZ - (depth - 1) / 2.0f) * spacing);

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

		return entities;
	}

	public PasteAreaResult BuildPasteAreaResult(
		int startX,
		int startZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		MirrorMode mirrorMode)
	{
		var result = new PasteAreaResult();
		result.SpawnRequests = new List<EntitySpawnRequest>();

		if (_copiedArea == null) return result;

		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return result;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;

		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		bool modified = false;

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				PasteCell(sx, sz, startX, startZ, width, depth, pasteHeights, pasteTextures, mirrorMode, ref terrain, ref modified);
			}
		}

		result.TerrainModified = modified;
		result.HeightsModified = pasteHeights && modified;

		if (pasteEntities)
		{
			Vector3 origin = new Vector3((startX - (width - 1) / 2.0f) * spacing, 0.0f, (startZ - (depth - 1) / 2.0f) * spacing);
			foreach (var ent in _copiedArea.Entities)
			{
				Vector3 destPos = origin + ent.RelativePos;
				destPos.Y = GetTerrainHeightAt(destPos);

				result.SpawnRequests.Add(new EntitySpawnRequest
				{
					Type = ent.Type,
					Id = ent.Id,
					Position = destPos,
					Rotation = ent.Rotation,
					Scale = ent.Scale,
					IsEnemy = ent.IsEnemy
				});

				AddMirroredRequests(result.SpawnRequests, ent.Type, ent.Id, destPos, ent.Rotation, ent.Scale, ent.IsEnemy, mirrorMode);
			}
		}

		return result;
	}

	public EraseAreaResult BuildEraseAreaResult(
		int minX, int minZ, int maxX, int maxZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		IEnumerable<Node3D> sceneChildren,
		Node3D previewNode)
	{
		var result = new EraseAreaResult();
		result.NodesToDelete = new List<Node3D>();

		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return result;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		bool terrainModified = false;

		if (pasteHeights || pasteTextures)
		{
			for (int sz = 0; sz < selDepth; sz++)
			{
				for (int sx = 0; sx < selWidth; sx++)
				{
					int targetX = minX + sx;
					int targetZ = minZ + sz;
					if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
					{
						if (pasteHeights) terrain.Heights[targetX, targetZ] = 0.0f;
						if (pasteTextures) _terrainColors[targetX, targetZ] = new Color(0.2f, 0.45f, 0.15f);
						terrainModified = true;
					}
				}
			}
		}

		result.TerrainModified = terrainModified;
		result.HeightsModified = pasteHeights && terrainModified;

		if (pasteEntities)
		{
			float minWorldX = (minX - (width - 1) / 2.0f) * spacing - spacing * 0.5f;
			float maxWorldX = (maxX - (width - 1) / 2.0f) * spacing + spacing * 0.5f;
			float minWorldZ = (minZ - (depth - 1) / 2.0f) * spacing - spacing * 0.5f;
			float maxWorldZ = (maxZ - (depth - 1) / 2.0f) * spacing + spacing * 0.5f;

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
		}

		return result;
	}

	public (float[,] Heights, Color[,] Colors) PerformFloodFill(Vector3 clickPos, Color fillColor, MirrorMode mirrorMode)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return (null, null);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;
		var colorsBefore = (Color[,])_terrainColors.Clone();
		var visited = new bool[width, depth];
		float[,] heights = terrain.Heights;

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
				_terrainColors[currX, currZ] = new Color(fillColor.R, fillColor.G, fillColor.B, targetAlpha);
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
							if (colorsBefore[nextX, nextZ] != startColor) continue;
							float hCurrent = heights[currX, currZ];
							float hNext = heights[nextX, nextZ];
							if (Mathf.Abs(hNext - hCurrent) >= 1.0f) continue;
							visited[nextX, nextZ] = true;
							queue.Enqueue((nextX, nextZ));
						}
					}
				}
			}
		}

		DoSingleFill(clickPos);
		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors) DoSingleFill(m);
		}

		return ((float[,])terrain.Heights.Clone(), (Color[,])_terrainColors.Clone());
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
		float spacing = terrain.Spacing;
		float fx = worldPos.X / spacing + (width - 1) / 2.0f;
		float fz = worldPos.Z / spacing + (depth - 1) / 2.0f;
		int cx = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
		int cz = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);
		return (cx, cz);
	}

	public Vector3 SnapToGrid(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return worldPos;
		float spacing = terrain.Spacing;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / spacing + (width - 1) / 2.0f);
		worldPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
		float fz = Mathf.Round(worldPos.Z / spacing + (depth - 1) / 2.0f);
		worldPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
		return worldPos;
	}

	private ref TerrainState GetTerrainState()
	{
		var worldQuery = new QueryDescription().WithAll<TerrainState>();
		Entity worldEntity = Entity.Null;
		_ecsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity))
		{
			return ref _ecsWorld.Get<TerrainState>(worldEntity);
		}
		throw new InvalidOperationException("TerrainState entity not found in ECS world.");
	}

	private float GetMinHeightInBrushBoundsInternal(Vector3 worldPos, float brushRadius = -1f, bool brushIsSquare = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Heights == null) return GetTerrainHeightAt(worldPos);

		float spacing = terrain.Spacing;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;

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
					float h = terrain.Heights[x, z];
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

	private void PasteCell(
		int sx, int sz,
		int startX, int startZ,
		int width, int depth,
		bool pasteHeights, bool pasteTextures,
		MirrorMode mirrorMode,
		ref TerrainState terrain,
		ref bool modified)
	{
		int targetX = startX + sx;
		int targetZ = startZ + sz;

		if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
		{
			if (pasteHeights) terrain.Heights[targetX, targetZ] = _copiedArea.Heights[sx, sz];
			if (pasteTextures) _terrainColors[targetX, targetZ] = _copiedArea.Colors[sx, sz];
			modified = true;
		}

		if (mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both)
		{
			int mx = width - 1 - targetX;
			int mz = targetZ;
			if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
			{
				if (pasteHeights) terrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
				if (pasteTextures) _terrainColors[mx, mz] = _copiedArea.Colors[sx, sz];
				modified = true;
			}
		}

		if (mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both)
		{
			int mx = targetX;
			int mz = depth - 1 - targetZ;
			if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
			{
				if (pasteHeights) terrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
				if (pasteTextures) _terrainColors[mx, mz] = _copiedArea.Colors[sx, sz];
				modified = true;
			}
		}

		if (mirrorMode == MirrorMode.Both)
		{
			int mx = width - 1 - targetX;
			int mz = depth - 1 - targetZ;
			if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
			{
				if (pasteHeights) terrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
				if (pasteTextures) _terrainColors[mx, mz] = _copiedArea.Colors[sx, sz];
				modified = true;
			}
		}
	}

	public bool GetBlockMode(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.BlockMode, true);
	}

	public void SetBlockMode(Entity worldEntity, bool value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(value, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetBlockLevelHeight(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.BlockLevelHeight, 4.0f);
	}

	public void SetBlockLevelHeight(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, value, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsLeft(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsLeft, -95.0f);
	}

	public void SetCameraBoundsLeft(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, value, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsRight(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsRight, 95.0f);
	}

	public void SetCameraBoundsRight(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, value, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsTop(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsTop, -95.0f);
	}

	public void SetCameraBoundsTop(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, value, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsBottom(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsBottom, 125.0f);
	}

	public void SetCameraBoundsBottom(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, value, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public string GetSkyboxPath(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, string>(worldEntity, s => s.SkyboxPath, "res://Assets/skybox_panoramic.jpg");
	}

	public void SetSkyboxPath(Entity worldEntity, string value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, value, s.HasUnsavedChanges)));
	}

	public bool GetHasUnsavedChanges(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.HasUnsavedChanges, false);
	}

	public void SetHasUnsavedChanges(Entity worldEntity, bool value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, value)));
	}

	public string GetTerrainStatusString(Vector3 pos, string toolName, string activePlaceId)
	{
		string formattedToolName = toolName.ToUpper();
		if (!string.IsNullOrEmpty(activePlaceId))
		{
			formattedToolName += $" ({activePlaceId.ToUpper()})";
		}

		string status = $"ACTIVE TOOL: {formattedToolName} | Pos: {pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}";

		ref var terrain = ref GetTerrainState();

		if (terrain.Heights != null && toolName.Equals("PaintPathing", StringComparison.OrdinalIgnoreCase))
		{
			float fx = pos.X / terrain.Spacing + (terrain.Width - 1) / 2.0f;
			float fz = pos.Z / terrain.Spacing + (terrain.Depth - 1) / 2.0f;
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
				if ((code & EditableTerrain.PATHING_UNPATHABLE) != 0)
				{
					layers.Add("Unpathable");
				}

				string layersStr = layers.Count > 0 ? string.Join(", ", layers) : "None";
				status += $" | Path: {layersStr}";
			}
		}

		return status;
	}
}
