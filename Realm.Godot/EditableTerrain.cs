using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public partial class EditableTerrain : RuntimeTerrain
{
	public new static EditableTerrain Instance => RuntimeTerrain.Instance as EditableTerrain;

	public EditableTerrain() : base()
	{
	}

	protected override bool IsRuntimeOnly => false;

	public override void ProcessAndSaveRawTexture(string rawPngPath, string outputKtx2Path)
	{
		var img = Godot.Image.LoadFromFile(rawPngPath);
		if (img == null) return;
		img = AutoCropToPowerOfTwoSquare(img);
		
		int w = img.GetWidth();
		int h = img.GetHeight();
		if (img.GetFormat() != Godot.Image.Format.Rgba8)
		{
			img.Convert(Godot.Image.Format.Rgba8);
		}

		img = NormalizeAlbedoLuminance(img);
		
		var layer0 = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);
		var layer1 = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);

		float[,] luminance = new float[w, h];
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				var color = img.GetPixel(x, y);
				luminance[x, y] = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
			}
		}

		float[,] fineMean = ComputeSeparableBoxBlur(luminance, w, h, 3);
		float[,] coarseMean = ComputeSeparableBoxBlur(luminance, w, h, 14);

		float[,] rawHeight = new float[w, h];
		int totalPixels = w * h;
		float[] flatHeights = new float[totalPixels];
		int flatIdx = 0;

		for (int y = 0; y < h; y++)
		{
			int py = y > 0 ? y - 1 : h - 1;
			int ny = y < h - 1 ? y + 1 : 0;

			for (int x = 0; x < w; x++)
			{
				int px = x > 0 ? x - 1 : w - 1;
				int nx = x < w - 1 ? x + 1 : 0;

				float lum = luminance[x, y];
				float highFreq = lum - fineMean[x, y];
				float midFreq = fineMean[x, y] - coarseMean[x, y];

				float dx = (luminance[nx, y] - luminance[px, y]) * 0.5f;
				float dy = (luminance[x, ny] - luminance[x, py]) * 0.5f;
				float gradMag = MathF.Sqrt(dx * dx + dy * dy);
				float laplacian = luminance[nx, y] + luminance[px, y] + luminance[x, ny] + luminance[x, py] - 4.0f * lum;

				float structuralValue = 0.5f + (highFreq * 2.2f) + (midFreq * 1.4f) + (laplacian * 0.5f) - (gradMag * 0.25f);
				rawHeight[x, y] = structuralValue;
				flatHeights[flatIdx++] = structuralValue;
			}
		}

		Array.Sort(flatHeights);
		int p1Index = Math.Clamp((int)(totalPixels * 0.01f), 0, totalPixels - 1);
		int p99Index = Math.Clamp((int)(totalPixels * 0.99f), 0, totalPixels - 1);
		float lowPercentile = flatHeights[p1Index];
		float highPercentile = flatHeights[p99Index];

		float normRange = highPercentile - lowPercentile;
		float invNormRange = normRange > 1e-5f ? 1.0f / normRange : 0.0f;
		float[,] normalizedHeight = new float[w, h];

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				float normH = (rawHeight[x, y] - lowPercentile) * invNormRange;
				normalizedHeight[x, y] = Godot.Mathf.Clamp(normH, 0.0f, 1.0f);
			}
		}

		for (int y = 0; y < h; y++)
		{
			int py = y > 0 ? y - 1 : h - 1;
			int ny = y < h - 1 ? y + 1 : 0;

			for (int x = 0; x < w; x++)
			{
				int px = x > 0 ? x - 1 : w - 1;
				int nx = x < w - 1 ? x + 1 : 0;

				var albedoCol = img.GetPixel(x, y);
				float height = normalizedHeight[x, y];
				layer0.SetPixel(x, y, new Godot.Color(albedoCol.R, albedoCol.G, albedoCol.B, height));

				float dX = (normalizedHeight[nx, y] - normalizedHeight[px, y]) * 2.5f;
				float dY = (normalizedHeight[x, ny] - normalizedHeight[x, py]) * 2.5f;

				Vector3 norm = new Vector3(-dX, -dY, 1.0f).Normalized();
				float r = norm.X * 0.5f + 0.5f;
				float g = norm.Y * 0.5f + 0.5f;
				float b = norm.Z * 0.5f + 0.5f;

				float contrastHeight = (height - 0.5f) * 1.4f + 0.5f;
				contrastHeight = Godot.Mathf.Clamp(contrastHeight, 0.0f, 1.0f);
				float highDetail = Math.Abs(luminance[x, y] - fineMean[x, y]);
				float roughness = Godot.Mathf.Clamp(Godot.Mathf.Lerp(0.85f, 0.45f, contrastHeight) + highDetail * 0.8f, 0.15f, 0.95f);

				layer1.SetPixel(x, y, new Godot.Color(r, g, b, roughness));
			}
		}
		
		string tempL0 = $"user://temp_l0_{System.Guid.NewGuid()}.png";
		string tempL1 = $"user://temp_l1_{System.Guid.NewGuid()}.png";
		
		layer0.SavePng(tempL0);
		layer1.SavePng(tempL1);
		
		string globalTempL0 = Godot.ProjectSettings.GlobalizePath(tempL0);
		string globalTempL1 = Godot.ProjectSettings.GlobalizePath(tempL1);
		string globalOutput = Godot.ProjectSettings.GlobalizePath(outputKtx2Path);
		
		string dir = System.IO.Path.GetDirectoryName(globalOutput);
		if (!System.IO.Directory.Exists(dir))
		{
			System.IO.Directory.CreateDirectory(dir);
		}
		
		string ktxCmd = GetKtxCmdPath();
		
		try
		{
			string ktxDir = System.IO.Path.GetDirectoryName(ktxCmd);
			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = ktxCmd,
				WorkingDirectory = ktxDir,
				Arguments = $"create --format R8G8B8A8_UNORM --layers 2 --encode uastc --generate-mipmap \"{globalTempL0}\" \"{globalTempL1}\" \"{globalOutput}\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			
			using (var process = System.Diagnostics.Process.Start(startInfo))
			{
				string stdout = process.StandardOutput.ReadToEnd();
				string stderr = process.StandardError.ReadToEnd();
				process.WaitForExit();
				if (process.ExitCode != 0)
				{
					throw new System.Exception($"ktx create failed with exit code {process.ExitCode}. Stderr: {stderr}. Stdout: {stdout}");
				}
			}
		}
		catch (System.Exception ex)
		{
			System.Console.Error.WriteLine($"Failed to execute ktx create: {ex.Message}");
			Godot.GD.PrintErr($"Failed to execute ktx create: {ex.Message}");
			throw;
		}
		finally
		{
			if (System.IO.File.Exists(globalTempL0)) System.IO.File.Delete(globalTempL0);
			if (System.IO.File.Exists(globalTempL1)) System.IO.File.Delete(globalTempL1);
		}
	}

	public class SwatchLiveConfig
	{
		public float? TileMode { get; set; }
		public float? UvScale { get; set; }
		public float? StochasticTileSize { get; set; }
		public float? CrossFade { get; set; }
		public float? Brightness { get; set; }
		public Color? Tint { get; set; }
		public float? HeightScale { get; set; }
		public float? HeightOffset { get; set; }
		public float? CrevicePower { get; set; }
	}

	private static readonly Dictionary<string, SwatchLiveConfig> _liveSwatchOverrides = new(StringComparer.OrdinalIgnoreCase);

	public override void UpdateTextureParamDirect(
		string swatchName,
		string tileMode,
		float uvScale,
		float stochasticTileSize,
		float crossFade = 0.0f,
		float? brightness = null,
		string? tintStr = null,
		float heightScale = 1.0f,
		float heightOffset = 0.0f,
		float crevicePower = 1.0f,
		float edgeNoiseInfluence = 1.0f)
	{
		if (_material == null) return;
		string cleanName = System.IO.Path.GetFileNameWithoutExtension(swatchName);

		float tm = string.Equals(tileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
		float uv = Math.Clamp(uvScale, 0.1f, 4.0f);
		float stoch = Math.Clamp(stochasticTileSize, 0.5f, 3.0f);
		float cf = Math.Clamp(crossFade, 0.0f, 10.0f) * 0.01f;

		float hs = Math.Clamp(heightScale, 0.1f, 3.0f);
		float ho = Math.Clamp(heightOffset, -1.0f, 1.0f);
		float cp = Math.Clamp(crevicePower, 0.5f, 4.0f);
		float en = 1.0f;

		Color? parsedTint = null;
		if (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr))
		{
			parsedTint = Color.FromHtml(tintStr);
		}

		_liveSwatchOverrides[cleanName] = new SwatchLiveConfig
		{
			TileMode = tm,
			UvScale = uv,
			StochasticTileSize = stoch,
			CrossFade = cf,
			Brightness = brightness,
			Tint = parsedTint,
			HeightScale = hs,
			HeightOffset = ho,
			CrevicePower = cp
		};

		int targetIndex = -1;
		for (int i = 0; i < _loadedTextureList.Count && i < 32; i++)
		{
			if (string.Equals(_loadedTextureList[i], cleanName, StringComparison.OrdinalIgnoreCase))
			{
				targetIndex = i;
				break;
			}
		}

		if (targetIndex >= 0)
		{
			_swatchParamsCache[targetIndex] = new Godot.Vector4(tm, uv, stoch, cf);
			_swatchHeightParamsCache[targetIndex] = new Godot.Vector4(hs, ho, cp, en);

			_material.SetShaderParameter("swatch_params", _swatchParamsCache);
			_material.SetShaderParameter("swatch_height_params", _swatchHeightParamsCache);

			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var ts = ref GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity);
				if (ts.SwatchConfigs == null || ts.SwatchConfigs.Length != 32)
				{
					ts.SwatchConfigs = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig[32];
				}
				ts.SwatchConfigs[targetIndex] = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig(hs, ho, cp, en);
			}
		}

		if (brightness.HasValue || parsedTint.HasValue)
		{
			ReloadTerrainTextures(true);
		}
	}

	public override void SetPathingVisible(bool visible)
	{
		if (_material != null)
		{
			_material.SetShaderParameter("pathing_visible", visible);
		}
	}

	public override void SetGridVisible(bool visible)
	{
		if (_material != null)
		{
			_material.SetShaderParameter("grid_visible", visible);
		}
	}

	public override void SetWireframeMode(bool enabled)
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			viewport.DebugDraw = enabled ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
		}
	}

	public override void ToggleWireframeMode()
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			bool isWireframe = viewport.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
			viewport.DebugDraw = isWireframe ? Viewport.DebugDrawEnum.Disabled : Viewport.DebugDrawEnum.Wireframe;
		}
	}

	public override void UpdatePathingTexture()
	{
		if (_material == null || PathingCodes == null) return;
		
		int w = Width;
		int d = Depth;
		var img = Image.CreateEmpty(w, d, false, Image.Format.Rgba8);
		
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int code = PathingCodes[x, z];
				img.SetPixel(x, z, new Color(code / 255.0f, 0f, 0f, 0f));
			}
		}
		
		var tex = ImageTexture.CreateFromImage(img);
		_material.SetShaderParameter("pathing_texture", tex);
	}

	public override void ResizeTerrain(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		
		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		var newCells = new TerrainCell[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;

		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int oldX = x - offsetX;
				int oldZ = z - offsetZ;
				if (x < newWidth && z < newDepth)
				{
					if (oldCells != null && oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
					{
						newCells[x, z] = oldCells[oldX, oldZ];
					}
					if (oldPathing != null && oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
					{
						newPathing[x, z] = oldPathing[oldX, oldZ];
					}
					else
					{
						newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
					}
				}
				if (oldSplatMap != null && oldX >= 0 && oldX < oldSplatMap.GetLength(0) && oldZ >= 0 && oldZ < oldSplatMap.GetLength(1))
				{
					newSplatMap[x, z] = oldSplatMap[oldX, oldZ];
				}
				else
				{
					newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.QuadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.QuadSize, newDepth * state.QuadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RemapSplatIndices(IReadOnlyDictionary<int, int> remap)
	{
		if (remap == null || remap.Count == 0) return;

		bool splatChanged = false;
		if (SplatMap != null)
		{
			int sw = SplatMap.GetLength(0);
			int sd = SplatMap.GetLength(1);
			for (int z = 0; z < sd; z++)
			{
				for (int x = 0; x < sw; x++)
				{
					var s = SplatMap[x, z];
					int i0 = remap.TryGetValue(s.Index0, out int r0) ? r0 : s.Index0;
					int i1 = remap.TryGetValue(s.Index1, out int r1) ? r1 : s.Index1;
					int i2 = remap.TryGetValue(s.Index2, out int r2) ? r2 : s.Index2;
					int i3 = remap.TryGetValue(s.Index3, out int r3) ? r3 : s.Index3;
					if (i0 != s.Index0 || i1 != s.Index1 || i2 != s.Index2 || i3 != s.Index3)
					{
						SplatMap[x, z] = new TerrainSplatWeights
						{
							Index0 = i0,
							Index1 = i1,
							Index2 = i2,
							Index3 = i3,
							Weight0 = s.Weight0,
							Weight1 = s.Weight1,
							Weight2 = s.Weight2,
							Weight3 = s.Weight3
						};
						splatChanged = true;
					}
				}
			}
		}

		if (CliffSplatMap != null)
		{
			int cw = CliffSplatMap.GetLength(0);
			int cd = CliffSplatMap.GetLength(1);
			for (int z = 0; z < cd; z++)
			{
				for (int x = 0; x < cw; x++)
				{
					var c = CliffSplatMap[x, z];
					int i0 = remap.TryGetValue(c.Index0, out int r0) ? r0 : c.Index0;
					int i1 = remap.TryGetValue(c.Index1, out int r1) ? r1 : c.Index1;
					int i2 = remap.TryGetValue(c.Index2, out int r2) ? r2 : c.Index2;
					int i3 = remap.TryGetValue(c.Index3, out int r3) ? r3 : c.Index3;
					if (i0 != c.Index0 || i1 != c.Index1 || i2 != c.Index2 || i3 != c.Index3)
					{
						CliffSplatMap[x, z] = new TerrainSplatWeights
						{
							Index0 = i0,
							Index1 = i1,
							Index2 = i2,
							Index3 = i3,
							Weight0 = c.Weight0,
							Weight1 = c.Weight1,
							Weight2 = c.Weight2,
							Weight3 = c.Weight3
						};
						splatChanged = true;
					}
				}
			}
		}

		if (splatChanged)
		{
			UpdateMeshAndPhysics(false, false);
		}
	}

	public override void ScaleTerrainData(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		var newCells = new TerrainCell[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];

		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int x0 = oldSplatMap != null ? Math.Clamp((int)Math.Floor(x * (float)(oldSplatMap.GetLength(0) - 1) / newWidth), 0, oldSplatMap.GetLength(0) - 1) : 0;
				int z0 = oldSplatMap != null ? Math.Clamp((int)Math.Floor(z * (float)(oldSplatMap.GetLength(1) - 1) / newDepth), 0, oldSplatMap.GetLength(1) - 1) : 0;

				if (x < newWidth && z < newDepth)
				{
					int cellX0 = Math.Clamp((int)Math.Floor(x * (float)oldWidth / newWidth), 0, oldWidth - 1);
					int cellZ0 = Math.Clamp((int)Math.Floor(z * (float)oldDepth / newDepth), 0, oldDepth - 1);
					if (oldCells != null) newCells[x, z] = oldCells[cellX0, cellZ0];

					if (oldPathing != null)
					{
						newPathing[x, z] = oldPathing[cellX0, cellZ0];
					}
					else
					{
						newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
					}
				}

				newSplatMap[x, z] = oldSplatMap != null ? oldSplatMap[x0, z0] : TerrainSplatWeights.CreateSolid(0);
			}
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.QuadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.QuadSize, newDepth * state.QuadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		TerrainCell[,] clonedCells = cells != null ? (TerrainCell[,])cells.Clone() : new TerrainCell[newWidth, newDepth];
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth, newDepth];

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			clonedCells, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = clonedCells;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;

		CreateChunks();
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		float[,] clonedSource = (float[,])heights.Clone();
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth, newDepth];

		var calculatedCells = TerrainState.CalculateCells(newWidth, newDepth, clonedSource);

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			calculatedCells, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = calculatedCells;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;

		CreateChunks();
		
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}
}
