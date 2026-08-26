using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Ecs.Services;
using Realm.Godot.Utils;

namespace Realm.Godot.Services.ModelOptimization;

public class ModelOptimizerService
{
	private readonly WorldAccessor _worldAccessor;

	static ModelOptimizerService()
	{
		GltfDocumentExtensionMsftLod.RegisterExtension();
	}

	public enum TextureType
	{
		Albedo,
		PbrOrm,
		NormalMap,
		Generic
	}

	public struct LodTierConfig
	{
		public int TierIndex { get; set; }
		public string Name { get; set; }
		public int ViewportHeight { get; set; }
		public float TargetError { get; set; }
		public float QualityThreshold { get; set; }
		public float TargetRatio { get; set; }
		public int MaxBoneInfluences { get; set; }
		public float BoneWeightThreshold { get; set; }
		public bool CastShadow { get; set; }
		public float VisibilityRangeBegin { get; set; }
		public float VisibilityRangeEnd { get; set; }

		public LodTierConfig(
			int tierIndex,
			string name,
			int viewportHeight,
			float targetError,
			float qualityThreshold,
			float targetRatio,
			int maxBoneInfluences,
			bool castShadow,
			float visBegin,
			float visEnd,
			float boneWeightThreshold = 0.01f)
		{
			TierIndex = tierIndex;
			Name = name;
			ViewportHeight = viewportHeight;
			TargetError = targetError;
			QualityThreshold = qualityThreshold;
			TargetRatio = targetRatio;
			MaxBoneInfluences = maxBoneInfluences;
			BoneWeightThreshold = boneWeightThreshold;
			CastShadow = castShadow;
			VisibilityRangeBegin = visBegin;
			VisibilityRangeEnd = visEnd;
		}

		public static LodTierConfig[] CreateDefaultTiers(float allowedPixelError = 1.5f)
		{
			return new LodTierConfig[]
			{
				new LodTierConfig(0, "LOD0", 800, CalculateTargetErrorFromScreenHeight(800, allowedPixelError), 0.94f, 1.00f, 4, true, 0f, 25f),
				new LodTierConfig(1, "LOD1", 250, CalculateTargetErrorFromScreenHeight(250, allowedPixelError), 0.87f, 0.50f, 2, true, 25f, 50f),
				new LodTierConfig(2, "LOD2", 100, CalculateTargetErrorFromScreenHeight(100, allowedPixelError), 0.78f, 0.25f, 1, true, 50f, 85f),
				new LodTierConfig(3, "LOD3", 35, CalculateTargetErrorFromScreenHeight(35, allowedPixelError), 0.68f, 0.10f, 1, true, 85f, 250f)
			};
		}
	}

	public struct OptimizationOptions
	{
		public float AllowedPixelError { get; set; } = 1.5f;
		public float CreaseAngleDegrees { get; set; } = 45.0f;
		public int MaxTextureResolution { get; set; } = 1024;
		public bool ForceReDecimate { get; set; } = false;
		public LodTierConfig[] LodTiers { get; set; } = null;

		public OptimizationOptions()
		{
		}
	}

	public struct OptimizationResult
	{
		public bool Success;
		public byte[] OptimizedGlbBytes;
		public int OriginalTriangleCount;
		public int OptimizedTriangleCount;
		public int[] LodTriangleCounts;
		public float ReductionRatio;
		public bool DecimationSkipped;
		public int TexturesProcessedCount;
		public int ChosenTextureResolution;
		public string ErrorMessage;
	}

	public struct RawVertexData
	{
		public Vector3 Position;
		public Vector3 Normal;
		public Vector2 UV;
		public Vector4 BoneIndices;
		public Vector4 BoneWeights;
		public Color Color;
	}

	public struct RawSurfaceData
	{
		public RawVertexData[] Vertices;
		public uint[] Indices;
		public Material SurfaceMaterial;
		public bool HasTangents;
	}

	public ModelOptimizerService(WorldAccessor worldAccessor)
	{
		_worldAccessor = worldAccessor;
		GltfDocumentExtensionMsftLod.RegisterExtension();
	}

	public OptimizationResult OptimizeGlb(byte[] glbBytes, OptimizationOptions options = default)
	{
		if (options.MaxTextureResolution <= 0)
		{
			options.MaxTextureResolution = 1024;
		}
		if (options.AllowedPixelError <= 0f)
		{
			options.AllowedPixelError = 1.5f;
		}
		if (options.LodTiers == null || options.LodTiers.Length == 0)
		{
			options.LodTiers = LodTierConfig.CreateDefaultTiers(options.AllowedPixelError);
		}

		OptimizationResult result = new OptimizationResult
		{
			Success = false,
			OptimizedGlbBytes = glbBytes,
			ReductionRatio = 1.0f,
			LodTriangleCounts = new int[options.LodTiers.Length]
		};

		if (glbBytes == null || glbBytes.Length == 0)
		{
			result.ErrorMessage = "Empty or null GLB buffer provided.";
			return result;
		}

		try
		{
			bool alreadyDecimated = !options.ForceReDecimate && HasDecimationCompletedFlag(glbBytes);
			result.DecimationSkipped = alreadyDecimated;

			List<string> originalImageNames = GetOriginalImageNames(glbBytes);

			var gltfDocument = new GltfDocument();
			var gltfState = new GltfState();

			Error loadError = gltfDocument.AppendFromBuffer(glbBytes, string.Empty, gltfState);
			if (loadError != Error.Ok)
			{
				result.ErrorMessage = $"GltfDocument failed to parse GLB buffer: {loadError}";
				return result;
			}

			Node rootScene = gltfDocument.GenerateScene(gltfState);
			if (rootScene == null)
			{
				result.ErrorMessage = "Failed to generate scene from GLTF state.";
				return rootSceneToResult(result, glbBytes);
			}

			var (sceneTexturesProcessed, chosenRes) = ProcessSceneTextures(
				rootScene,
				options.MaxTextureResolution);

			result.TexturesProcessedCount = sceneTexturesProcessed;
			result.ChosenTextureResolution = chosenRes;

			List<MeshInstance3D> meshInstances = FindAllMeshInstances(rootScene);
			if (meshInstances.Count == 0)
			{
				byte[] fallbackBytes = ExportSceneToGlb(rootScene, true, options.MaxTextureResolution, originalImageNames);
				rootScene.Free();
				result.Success = true;
				result.OptimizedGlbBytes = fallbackBytes ?? glbBytes;
				return result;
			}

			int totalOriginalTriangles = 0;
			foreach (var meshInst in meshInstances)
			{
				if (meshInst.Mesh != null)
				{
					totalOriginalTriangles += GetMeshTriangleCount(meshInst.Mesh);
				}
			}
			result.OriginalTriangleCount = totalOriginalTriangles;

			if (alreadyDecimated || totalOriginalTriangles < 12)
			{
				byte[] exportedBytes = ExportSceneToGlb(rootScene, true, options.MaxTextureResolution, originalImageNames);
				rootScene.Free();
				result.Success = true;
				result.OptimizedGlbBytes = exportedBytes ?? glbBytes;
				result.OptimizedTriangleCount = totalOriginalTriangles;
				result.ReductionRatio = 1.0f;
				for (int i = 0; i < result.LodTriangleCounts.Length; i++)
				{
					result.LodTriangleCounts[i] = totalOriginalTriangles;
				}
				return result;
			}

			int totalOptimizedMasterTriangles = 0;
			int[] lodTotalTriangles = new int[options.LodTiers.Length];

			foreach (var masterMeshInst in meshInstances)
			{
				if (masterMeshInst.Mesh == null)
				{
					continue;
				}

				List<RawSurfaceData> surfaces = ExtractRawSurfaces(masterMeshInst.Mesh);
				if (surfaces.Count == 0)
				{
					continue;
				}

				ArrayMesh[] lodMeshes = new ArrayMesh[options.LodTiers.Length];

				List<RawSurfaceData> masterWeldedSurfaces = new List<RawSurfaceData>(surfaces.Count);
				foreach (var surf in surfaces)
				{
					masterWeldedSurfaces.Add(WeldAndRepairTopology(surf, options.LodTiers[0].MaxBoneInfluences, false));
				}

				ArrayMesh referenceMesh = BuildSmoothedMesh(masterWeldedSurfaces, options.CreaseAngleDegrees);

				int masterOriginalTris = GetMeshTriangleCount(masterMeshInst.Mesh);

				for (int t = 0; t < options.LodTiers.Length; t++)
				{
					var tier = options.LodTiers[t];
					bool isRigid = tier.MaxBoneInfluences <= 1;

					List<RawSurfaceData> tierWeldedSurfaces = new List<RawSurfaceData>(surfaces.Count);
					foreach (var surf in surfaces)
					{
						tierWeldedSurfaces.Add(WeldAndRepairTopology(surf, tier.MaxBoneInfluences, isRigid));
					}

					ArrayMesh tierReferenceMesh = isRigid ? BuildSmoothedMesh(tierWeldedSurfaces, options.CreaseAngleDegrees) : referenceMesh;

					float tierError = CalculateTargetErrorFromScreenHeight(tier.ViewportHeight, options.AllowedPixelError);
					float uvWeight = t == 0 ? 1.0f : (t == 1 ? 0.2f : (t == 2 ? 0.05f : 0.005f));

					List<RawSurfaceData> simplifiedSurfaces = new List<RawSurfaceData>(tierWeldedSurfaces.Count);
					foreach (var surf in tierWeldedSurfaces)
					{
						// targetTriangleCount = 0 -> simplify as aggressively as possible until hitting tierError
						simplifiedSurfaces.Add(SimplifySurface(surf, 0, tierError, uvWeight));
					}

					ArrayMesh simplifiedMesh = BuildSmoothedMesh(simplifiedSurfaces, options.CreaseAngleDegrees);

					if (t > 0)
					{
						int prevTris = GetMeshTriangleCount(lodMeshes[t - 1]);
						if (GetMeshTriangleCount(simplifiedMesh) >= prevTris)
						{
							List<RawSurfaceData> fallbackSurfaces = new List<RawSurfaceData>(tierWeldedSurfaces.Count);
							foreach (var surf in tierWeldedSurfaces)
							{
								fallbackSurfaces.Add(SimplifySurface(surf, 0, tierError * 1.5f, uvWeight * 0.1f));
							}
							simplifiedMesh = BuildSmoothedMesh(fallbackSurfaces, options.CreaseAngleDegrees);
						}
					}

					lodMeshes[t] = simplifiedMesh;

					int tierTris = GetMeshTriangleCount(lodMeshes[t]);
					lodTotalTriangles[t] += tierTris;
				}

				string masterBaseName = masterMeshInst.Name.ToString();
				if (masterBaseName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
				{
					masterBaseName = masterBaseName.Substring(0, masterBaseName.Length - 5);
				}

				masterMeshInst.Name = $"{masterBaseName}_LOD0";
				masterMeshInst.Mesh = lodMeshes[0];
				masterMeshInst.VisibilityRangeBegin = options.LodTiers[0].VisibilityRangeBegin;
				masterMeshInst.VisibilityRangeEnd = options.LodTiers[0].VisibilityRangeEnd;
				masterMeshInst.VisibilityRangeBeginMargin = 2.0f;
				masterMeshInst.VisibilityRangeEndMargin = 2.0f;
				masterMeshInst.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
				masterMeshInst.CastShadow = options.LodTiers[0].CastShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;

				totalOptimizedMasterTriangles += GetMeshTriangleCount(lodMeshes[0]);

				Node parentNode = masterMeshInst.GetParent() ?? rootScene;
				for (int t = 1; t < options.LodTiers.Length; t++)
				{
					var lodInst = new MeshInstance3D
					{
						Name = $"{masterBaseName}_LOD{t}",
						Mesh = lodMeshes[t],
						Transform = masterMeshInst.Transform,
						Skin = masterMeshInst.Skin,
						Skeleton = masterMeshInst.Skeleton,
						VisibilityRangeBegin = options.LodTiers[t].VisibilityRangeBegin,
						VisibilityRangeEnd = options.LodTiers[t].VisibilityRangeEnd,
						VisibilityRangeBeginMargin = 2.0f,
						VisibilityRangeEndMargin = 2.0f,
						VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled,
						CastShadow = options.LodTiers[t].CastShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off
					};

					int surfaceCount = lodMeshes[t].GetSurfaceCount();
					for (int s = 0; s < surfaceCount; s++)
					{
						var mat = masterMeshInst.GetSurfaceOverrideMaterial(s);
						if (mat != null)
						{
							lodInst.SetSurfaceOverrideMaterial(s, mat);
						}
					}

					if (masterMeshInst.MaterialOverride != null)
					{
						lodInst.MaterialOverride = masterMeshInst.MaterialOverride;
					}

					parentNode.AddChild(lodInst);
					lodInst.Owner = rootScene;
				}
			}

			result.OptimizedTriangleCount = totalOptimizedMasterTriangles;
			result.LodTriangleCounts = lodTotalTriangles;
			result.ReductionRatio = totalOriginalTriangles > 0 ? ((float)totalOptimizedMasterTriangles / totalOriginalTriangles) : 1.0f;

			byte[] finalGlbBytes = ExportSceneToGlb(rootScene, true, options.MaxTextureResolution, originalImageNames);
			rootScene.Free();

			if (finalGlbBytes != null && finalGlbBytes.Length > 0)
			{
				result.OptimizedGlbBytes = finalGlbBytes;
				result.Success = true;
			}
			else
			{
				result.ErrorMessage = "Failed to export optimized GLTF scene to GLB buffer.";
			}

			return result;
		}
		catch (Exception ex)
		{
			result.ErrorMessage = ex.ToString();
			return result;
		}
	}

	private static OptimizationResult rootSceneToResult(OptimizationResult res, byte[] original)
	{
		res.OptimizedGlbBytes = original;
		return res;
	}

	public static bool HasDecimationCompletedFlag(string filePath)
	{
		try
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return false;
			}

			using var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
			if (stream.Length < 20)
			{
				return false;
			}

			byte[] header = new byte[20];
			int read = stream.Read(header, 0, 20);
			if (read < 20)
			{
				return false;
			}

			uint magic = BitConverter.ToUInt32(header, 0);
			if (magic != 0x46546C67)
			{
				return false;
			}

			uint chunkLength = BitConverter.ToUInt32(header, 12);
			uint chunkType = BitConverter.ToUInt32(header, 16);

			if (chunkType != 0x4E4F534A)
			{
				return false;
			}

			if (chunkLength > 10 * 1024 * 1024)
			{
				return false;
			}

			byte[] jsonBytes = new byte[chunkLength];
			int totalRead = 0;
			while (totalRead < chunkLength)
			{
				int bytesRead = stream.Read(jsonBytes, totalRead, (int)chunkLength - totalRead);
				if (bytesRead <= 0) break;
				totalRead += bytesRead;
			}

			if (totalRead < chunkLength)
			{
				return false;
			}

			using var jsonDoc = JsonDocument.Parse(jsonBytes);
			var root = jsonDoc.RootElement;

			if (root.TryGetProperty("asset", out var assetElement) &&
				assetElement.TryGetProperty("extras", out var assetExtras) &&
				assetExtras.TryGetProperty("realm_decimate_completed", out var flag1) &&
				flag1.GetBoolean())
			{
				return true;
			}

			if (root.TryGetProperty("extras", out var rootExtras) &&
				rootExtras.TryGetProperty("realm_decimate_completed", out var flag2) &&
				flag2.GetBoolean())
			{
				return true;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool HasDecimationCompletedFlag(byte[] glbBytes)
	{
		try
		{
			if (glbBytes == null || glbBytes.Length < 20)
			{
				return false;
			}

			uint magic = BitConverter.ToUInt32(glbBytes, 0);
			if (magic != 0x46546C67)
			{
				return false;
			}

			int currentOffset = 12;
			while (currentOffset + 8 <= glbBytes.Length)
			{
				uint chunkLength = BitConverter.ToUInt32(glbBytes, currentOffset);
				uint chunkType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);

				if (chunkType == 0x4E4F534A)
				{
					string jsonString = System.Text.Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)chunkLength);
					using var jsonDoc = JsonDocument.Parse(jsonString);
					var root = jsonDoc.RootElement;

					if (root.TryGetProperty("asset", out var assetElement) &&
						assetElement.TryGetProperty("extras", out var assetExtras) &&
						assetExtras.TryGetProperty("realm_decimate_completed", out var flag1) &&
						flag1.GetBoolean())
					{
						return true;
					}

					if (root.TryGetProperty("extras", out var rootExtras) &&
						rootExtras.TryGetProperty("realm_decimate_completed", out var flag2) &&
						flag2.GetBoolean())
					{
						return true;
					}

					return false;
				}

				currentOffset += (int)(8 + chunkLength);
			}
		}
		catch
		{
		}

		return false;
	}

	private static List<string> GetOriginalImageNames(byte[] glbBytes)
	{
		var list = new List<string>();
		try
		{
			if (glbBytes == null || glbBytes.Length < 20)
			{
				return list;
			}

			int currentOffset = 12;
			while (currentOffset + 8 <= glbBytes.Length)
			{
				uint chunkLength = BitConverter.ToUInt32(glbBytes, currentOffset);
				uint chunkType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);

				if (chunkType == 0x4E4F534A)
				{
					string jsonString = System.Text.Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)chunkLength);
					using var jsonDoc = JsonDocument.Parse(jsonString);
					var root = jsonDoc.RootElement;
					if (root.TryGetProperty("images", out var imagesElement))
					{
						foreach (var img in imagesElement.EnumerateArray())
						{
							if (img.TryGetProperty("name", out var nameProp))
							{
								list.Add(nameProp.GetString());
							}
							else
							{
								list.Add($"Image_{list.Count}");
							}
						}
					}
					break;
				}

				currentOffset += (int)(8 + chunkLength);
			}
		}
		catch
		{
		}

		return list;
	}

	public static byte[] SetDecimationCompletedFlag(byte[] glbBytes, List<string> originalImageNames = null)
	{
		return EmbedMsftLodAndFlags(glbBytes, originalImageNames);
	}

	public static byte[] EmbedMsftLodAndFlags(byte[] glbBytes, List<string> originalImageNames = null)
	{
		try
		{
			if (glbBytes == null || glbBytes.Length < 20)
			{
				return glbBytes;
			}

			uint magic = BitConverter.ToUInt32(glbBytes, 0);
			if (magic != 0x46546C67)
			{
				return glbBytes;
			}

			int currentOffset = 12;

			uint jsonChunkLength = BitConverter.ToUInt32(glbBytes, currentOffset);
			uint jsonChunkType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);

			if (jsonChunkType != 0x4E4F534A)
			{
				return glbBytes;
			}

			string jsonString = System.Text.Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)jsonChunkLength);
			var jsonNode = JsonNode.Parse(jsonString);
			if (jsonNode == null)
			{
				return glbBytes;
			}

			var rootObj = jsonNode.AsObject();

			if (rootObj.ContainsKey("materials") && rootObj["materials"] is JsonArray matArray)
			{
				foreach (var matNode in matArray)
				{
					if (matNode is JsonObject matObj)
					{
						matObj.Remove("occlusionTexture");
					}
				}
			}

			if (!rootObj.ContainsKey("asset") || rootObj["asset"] == null)
			{
				rootObj["asset"] = new JsonObject();
			}

			var assetObj = rootObj["asset"].AsObject();
			if (!assetObj.ContainsKey("extras") || assetObj["extras"] == null)
			{
				assetObj["extras"] = new JsonObject();
			}

			assetObj["extras"].AsObject()["realm_decimate_completed"] = true;

			if (!rootObj.ContainsKey("extensionsUsed") || rootObj["extensionsUsed"] == null)
			{
				rootObj["extensionsUsed"] = new JsonArray();
			}
			var extUsedArray = rootObj["extensionsUsed"].AsArray();
			bool hasMsftLodExt = false;
			foreach (var ext in extUsedArray)
			{
				if (ext?.ToString() == "MSFT_lod")
				{
					hasMsftLodExt = true;
					break;
				}
			}
			if (!hasMsftLodExt)
			{
				extUsedArray.Add("MSFT_lod");
			}

			if (rootObj.ContainsKey("nodes") && rootObj["nodes"] is JsonArray nodesArray)
			{
				var masterNodeMap = new Dictionary<string, int>();
				var lodNodesByMaster = new Dictionary<string, List<int>>();
				var allLodNodeIndices = new HashSet<int>();

				for (int i = 0; i < nodesArray.Count; i++)
				{
					if (nodesArray[i] is JsonObject nObj && nObj.ContainsKey("name"))
					{
						string nName = nObj["name"]?.ToString() ?? "";
						if (nName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
						{
							string basePrefix = nName.Substring(0, nName.Length - 5);
							masterNodeMap[basePrefix] = i;
						}
						else if (nName.Contains("_LOD"))
						{
							int lodIdx = nName.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
							string basePrefix = nName.Substring(0, lodIdx);
							if (!lodNodesByMaster.TryGetValue(basePrefix, out var list))
							{
								list = new List<int>();
								lodNodesByMaster[basePrefix] = list;
							}
							list.Add(i);
							allLodNodeIndices.Add(i);
						}
					}
				}

				foreach (var kvp in masterNodeMap)
				{
					string basePrefix = kvp.Key;
					int masterIndex = kvp.Value;

					if (lodNodesByMaster.TryGetValue(basePrefix, out var lodList) && lodList.Count > 0)
					{
						lodList.Sort();
						if (nodesArray[masterIndex] is JsonObject masterObj)
						{
							if (!masterObj.ContainsKey("extensions") || masterObj["extensions"] == null)
							{
								masterObj["extensions"] = new JsonObject();
							}
							var masterExtObj = masterObj["extensions"].AsObject();
							var msftLodObj = new JsonObject();
							var idsArray = new JsonArray();
							foreach (var lodNodeIdx in lodList)
							{
								idsArray.Add(lodNodeIdx);
							}
							msftLodObj["ids"] = idsArray;
							masterExtObj["MSFT_lod"] = msftLodObj;
						}
					}
				}

				if (allLodNodeIndices.Count > 0)
				{
					foreach (int lodNodeIdx in allLodNodeIndices)
					{
						if (lodNodeIdx >= 0 && lodNodeIdx < nodesArray.Count && nodesArray[lodNodeIdx] is JsonObject lodObj)
						{
							lodObj.Remove("skin");
						}
					}

					for (int i = 0; i < nodesArray.Count; i++)
					{
						if (nodesArray[i] is JsonObject nObj && nObj.ContainsKey("children") && nObj["children"] is JsonArray chArray)
						{
							var filteredChildren = new JsonArray();
							foreach (var ch in chArray)
							{
								int chIdx = ch?.GetValue<int>() ?? -1;
								if (!allLodNodeIndices.Contains(chIdx))
								{
									filteredChildren.Add(chIdx);
								}
							}
							if (filteredChildren.Count > 0)
							{
								nObj["children"] = filteredChildren;
							}
							else
							{
								nObj.Remove("children");
							}
						}
					}

					if (rootObj.ContainsKey("scenes") && rootObj["scenes"] is JsonArray scenesArray)
					{
						foreach (var scNode in scenesArray)
						{
							if (scNode is JsonObject scObj && scObj.ContainsKey("nodes") && scObj["nodes"] is JsonArray scNodes)
							{
								var filteredNodes = new JsonArray();
								foreach (var sn in scNodes)
								{
									int nIdx = sn?.GetValue<int>() ?? -1;
									if (!allLodNodeIndices.Contains(nIdx))
									{
										filteredNodes.Add(nIdx);
									}
								}
								scObj["nodes"] = filteredNodes;
							}
						}
					}
				}
			}

			if (rootObj.ContainsKey("images") && rootObj["images"] is JsonArray imgArray)
			{
				for (int i = 0; i < imgArray.Count; i++)
				{
					if (imgArray[i] is JsonObject imgObj)
					{
						if (originalImageNames != null && i < originalImageNames.Count && !string.IsNullOrEmpty(originalImageNames[i]))
						{
							imgObj["name"] = originalImageNames[i];
						}
						else
						{
							imgObj["name"] = $"Image_{i}";
						}
					}
				}
			}

			if (rootObj.ContainsKey("textures") && rootObj["textures"] is JsonArray texArray)
			{
				for (int i = 0; i < texArray.Count; i++)
				{
					if (texArray[i] is JsonObject texObj)
					{
						if (originalImageNames != null && i < originalImageNames.Count && !string.IsNullOrEmpty(originalImageNames[i]))
						{
							texObj["name"] = originalImageNames[i];
						}
						else
						{
							texObj["name"] = $"Image_{i}";
						}
					}
				}
			}

			byte[] newJsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonNode.ToJsonString());
			int paddedJsonLength = (newJsonBytes.Length + 3) & ~3;
			byte[] paddedJson = new byte[paddedJsonLength];
			Array.Copy(newJsonBytes, paddedJson, newJsonBytes.Length);
			for (int i = newJsonBytes.Length; i < paddedJsonLength; i++)
			{
				paddedJson[i] = 0x20;
			}

			int binChunkStart = currentOffset + 8 + (int)jsonChunkLength;
			int binChunkRemaining = glbBytes.Length - binChunkStart;

			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);

			writer.Write(magic);
			writer.Write(BitConverter.ToUInt32(glbBytes, 4));
			uint newTotalLength = 12 + 8 + (uint)paddedJsonLength + (uint)binChunkRemaining;
			writer.Write(newTotalLength);

			writer.Write((uint)paddedJsonLength);
			writer.Write(0x4E4F534A);
			writer.Write(paddedJson);

			if (binChunkRemaining > 0)
			{
				writer.Write(glbBytes, binChunkStart, binChunkRemaining);
			}

			return ms.ToArray();
		}
		catch
		{
			return glbBytes;
		}
	}

	public static (int TexturesProcessedCount, int ChosenTextureResolution) ProcessSceneTextures(
		Node rootScene,
		int maxResolution = 1024)
	{
		int processedCount = 0;
		int maxChosenResolution = 0;
		var meshInstances = FindAllMeshInstances(rootScene);
		var processedMaterials = new HashSet<Material>();

		foreach (var meshInst in meshInstances)
		{
			int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 0;
			for (int s = 0; s < surfaceCount; s++)
			{
				var mat = meshInst.GetSurfaceOverrideMaterial(s) ?? meshInst.Mesh?.SurfaceGetMaterial(s);
				if (mat is StandardMaterial3D stdMat && processedMaterials.Add(stdMat))
				{
					if (stdMat.AlbedoTexture is Texture2D albedoTex)
					{
						stdMat.AlbedoColor = new Color(1f, 1f, 1f, 1f);
						var albedoImg = albedoTex.GetImage();
						if (albedoImg != null)
						{
							var processed = ProcessImage(albedoImg, TextureType.Albedo, maxResolution);
							stdMat.AlbedoTexture = ImageTexture.CreateFromImage(processed);
							maxChosenResolution = Math.Max(maxChosenResolution, Math.Max(processed.GetWidth(), processed.GetHeight()));
							processedCount++;
						}
					}

					if (stdMat.OrmTexture is Texture2D ormTex)
					{
						var img = ormTex.GetImage();
						if (img != null)
						{
							var processed = ProcessImage(img, TextureType.PbrOrm, maxResolution);
							var newTex = ImageTexture.CreateFromImage(processed);
							stdMat.OrmTexture = newTex;
							stdMat.Set("ao_enabled", true);
							stdMat.Set("ao_texture", newTex);
							processedCount++;
						}
						else
						{
							stdMat.Set("ao_enabled", true);
							stdMat.Set("ao_texture", ormTex);
						}
					}
					else
					{
						if (stdMat.MetallicTexture is Texture2D metTex)
						{
							var img = metTex.GetImage();
							if (img != null)
							{
								var processed = ProcessImage(img, TextureType.PbrOrm, maxResolution);
								var newTex = ImageTexture.CreateFromImage(processed);
								stdMat.MetallicTexture = newTex;
								stdMat.RoughnessTexture = newTex;
								stdMat.OrmTexture = newTex;
								stdMat.Set("ao_enabled", true);
								stdMat.Set("ao_texture", newTex);
								processedCount++;
							}
							else
							{
								stdMat.RoughnessTexture = metTex;
								stdMat.OrmTexture = metTex;
								stdMat.Set("ao_enabled", true);
								stdMat.Set("ao_texture", metTex);
							}
						}
						else if (stdMat.RoughnessTexture is Texture2D roughTex)
						{
							var img = roughTex.GetImage();
							if (img != null)
							{
								var processed = ProcessImage(img, TextureType.PbrOrm, maxResolution);
								var newTex = ImageTexture.CreateFromImage(processed);
								stdMat.RoughnessTexture = newTex;
								stdMat.OrmTexture = newTex;
								stdMat.Set("ao_enabled", true);
								stdMat.Set("ao_texture", newTex);
								processedCount++;
							}
							else
							{
								stdMat.OrmTexture = roughTex;
								stdMat.Set("ao_enabled", true);
								stdMat.Set("ao_texture", roughTex);
							}
						}
					}
					if (stdMat.NormalTexture is Texture2D normTex)
					{
						var img = normTex.GetImage();
						if (img != null)
						{
							var processed = ProcessImage(img, TextureType.NormalMap, maxResolution);
							stdMat.NormalTexture = ImageTexture.CreateFromImage(processed);
							processedCount++;
						}
					}

					if (stdMat.EmissionTexture is Texture2D emissTex)
					{
						var img = emissTex.GetImage();
						if (img != null)
						{
							var processed = ProcessImage(img, TextureType.Generic, maxResolution);
							stdMat.EmissionTexture = ImageTexture.CreateFromImage(processed);
							processedCount++;
						}
					}
				}
				else if (mat is OrmMaterial3D ormMat && processedMaterials.Add(ormMat))
				{
					if (ormMat.AlbedoTexture is Texture2D albedoTex)
					{
						ormMat.AlbedoColor = new Color(1f, 1f, 1f, 1f);
						var albedoImg = albedoTex.GetImage();
						if (albedoImg != null)
						{
							var processed = ProcessImage(albedoImg, TextureType.Albedo, maxResolution);
							ormMat.AlbedoTexture = ImageTexture.CreateFromImage(processed);
							maxChosenResolution = Math.Max(maxChosenResolution, Math.Max(processed.GetWidth(), processed.GetHeight()));
							processedCount++;
						}
					}

					if (ormMat.OrmTexture is Texture2D ormTex)
					{
						var img = ormTex.GetImage();
						if (img != null)
						{
							var processed = ProcessImage(img, TextureType.PbrOrm, maxResolution);
							ormMat.OrmTexture = ImageTexture.CreateFromImage(processed);
							processedCount++;
						}
					}

					if (ormMat.NormalTexture is Texture2D normTex)
					{
						var img = normTex.GetImage();
						if (img != null)
						{
							var processed = ProcessImage(img, TextureType.NormalMap, maxResolution);
							ormMat.NormalTexture = ImageTexture.CreateFromImage(processed);
							processedCount++;
						}
					}
				}
			}
		}

		if (maxChosenResolution == 0)
		{
			maxChosenResolution = Math.Min(maxResolution, 1024);
		}

		return (processedCount, maxChosenResolution);
	}

	public static Image ProcessImage(Image src, TextureType textureType = TextureType.Albedo, int maxResolution = 1024)
	{
		if (src == null)
		{
			return src;
		}

		int w = src.GetWidth();
		int h = src.GetHeight();

		int targetW = CalculateTargetTextureDimension(w, maxResolution);
		int targetH = CalculateTargetTextureDimension(h, maxResolution);

		bool wasPowerOfTwo = IsPowerOfTwo(w) && IsPowerOfTwo(h);
		Image.Interpolation defaultFilter = wasPowerOfTwo ? Image.Interpolation.Bilinear : Image.Interpolation.Lanczos;

		if (textureType == TextureType.Albedo && targetW == w && targetH == h && wasPowerOfTwo && !src.IsCompressed())
		{
			return src;
		}

		Image working = (Image)src.Duplicate();
		if (working.IsCompressed())
		{
			working.Decompress();
		}

		if (working.GetFormat() != Image.Format.Rgba8 && working.GetFormat() != Image.Format.Rgb8)
		{
			working.Convert(Image.Format.Rgba8);
		}

		if (textureType == TextureType.PbrOrm)
		{
			working = ProcessPbrOrmImage(working, targetW, targetH, wasPowerOfTwo);
		}
		else if (textureType == TextureType.NormalMap)
		{
			if (targetW != w || targetH != h)
			{
				working.Resize(targetW, targetH, defaultFilter);
			}
			RenormalizeNormalMap(working);
		}
		else if (targetW != w || targetH != h)
		{
			working.Resize(targetW, targetH, defaultFilter);
		}

		return working;
	}

	private static Image ProcessPbrOrmImage(Image source, int targetW, int targetH, bool wasPowerOfTwo)
	{
		int srcW = source.GetWidth();
		int srcH = source.GetHeight();

		if (targetW == srcW && targetH == srcH && wasPowerOfTwo)
		{
			return source;
		}

		var rChannel = Image.CreateEmpty(srcW, srcH, false, Image.Format.L8);
		var gbChannels = Image.CreateEmpty(srcW, srcH, false, Image.Format.Rgba8);

		byte[] srcData = source.GetData();
		int bytesPerPixel = source.GetFormat() == Image.Format.Rgba8 ? 4 : 3;

		byte[] rData = new byte[srcW * srcH];
		byte[] gbData = new byte[srcW * srcH * 4];

		for (int i = 0; i < srcW * srcH; i++)
		{
			int srcIdx = i * bytesPerPixel;
			rData[i] = srcData[srcIdx];
			gbData[i * 4] = 0;
			gbData[i * 4 + 1] = srcData[srcIdx + 1];
			gbData[i * 4 + 2] = srcData[srcIdx + 2];
			gbData[i * 4 + 3] = bytesPerPixel > 3 ? srcData[srcIdx + 3] : (byte)255;
		}

		rChannel.SetData(srcW, srcH, false, Image.Format.L8, rData);
		gbChannels.SetData(srcW, srcH, false, Image.Format.Rgba8, gbData);

		rChannel.Resize(targetW, targetH, Image.Interpolation.Lanczos);

		Image.Interpolation gbFilter = wasPowerOfTwo ? Image.Interpolation.Bilinear : Image.Interpolation.Lanczos;
		gbChannels.Resize(targetW, targetH, gbFilter);

		var combined = Image.CreateEmpty(targetW, targetH, false, Image.Format.Rgba8);
		byte[] finalR = rChannel.GetData();
		byte[] finalGb = gbChannels.GetData();
		byte[] combinedData = new byte[targetW * targetH * 4];

		for (int i = 0; i < targetW * targetH; i++)
		{
			combinedData[i * 4] = finalR[i];
			combinedData[i * 4 + 1] = finalGb[i * 4 + 1];
			combinedData[i * 4 + 2] = finalGb[i * 4 + 2];
			combinedData[i * 4 + 3] = finalGb[i * 4 + 3];
		}

		combined.SetData(targetW, targetH, false, Image.Format.Rgba8, combinedData);
		return combined;
	}

	private static void RenormalizeNormalMap(Image normalMap)
	{
		int w = normalMap.GetWidth();
		int h = normalMap.GetHeight();
		byte[] data = normalMap.GetData();
		int bpp = normalMap.GetFormat() == Image.Format.Rgba8 ? 4 : 3;

		for (int i = 0; i < w * h; i++)
		{
			int idx = i * bpp;
			float nx = (data[idx] / 255.0f) * 2.0f - 1.0f;
			float ny = (data[idx + 1] / 255.0f) * 2.0f - 1.0f;
			float nz = (data[idx + 2] / 255.0f) * 2.0f - 1.0f;

			float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
			if (len > 0.0001f)
			{
				nx /= len;
				ny /= len;
				nz /= len;
			}
			else
			{
				nx = 0f;
				ny = 0f;
				nz = 1f;
			}

			data[idx] = (byte)Math.Clamp((int)MathF.Round((nx * 0.5f + 0.5f) * 255.0f), 0, 255);
			data[idx + 1] = (byte)Math.Clamp((int)MathF.Round((ny * 0.5f + 0.5f) * 255.0f), 0, 255);
			data[idx + 2] = (byte)Math.Clamp((int)MathF.Round((nz * 0.5f + 0.5f) * 255.0f), 0, 255);
		}

		normalMap.SetData(w, h, false, normalMap.GetFormat(), data);
	}

	private static bool IsPowerOfTwo(int n)
	{
		return n > 0 && (n & (n - 1)) == 0;
	}

	private static int CalculateTargetTextureDimension(int size, int maxResolution = 1024)
	{
		if (size >= maxResolution)
		{
			return maxResolution;
		}

		if (IsPowerOfTwo(size))
		{
			return size;
		}

		int p = 1;
		while (p < size && p < maxResolution)
		{
			p <<= 1;
		}
		return Math.Min(maxResolution, p);
	}

	public static List<MeshInstance3D> FindAllMeshInstances(Node node)
	{
		List<MeshInstance3D> result = new List<MeshInstance3D>();
		CollectMeshInstancesRecursive(node, result);
		return result;
	}

	private static void CollectMeshInstancesRecursive(Node current, List<MeshInstance3D> list)
	{
		if (current is MeshInstance3D mi && mi.Mesh != null)
		{
			list.Add(mi);
		}

		int childCount = current.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			CollectMeshInstancesRecursive(current.GetChild(i), list);
		}
	}

	private static int GetMeshTriangleCount(Mesh mesh)
	{
		int count = 0;
		int surfaceCount = mesh.GetSurfaceCount();
		for (int i = 0; i < surfaceCount; i++)
		{
			var arrays = mesh.SurfaceGetArrays(i);
			if (arrays == null || arrays.Count <= (int)Mesh.ArrayType.Index)
			{
				continue;
			}

			int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
			if (indices != null && indices.Length > 0)
			{
				count += indices.Length / 3;
			}
			else
			{
				Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				if (vertices != null)
				{
					count += vertices.Length / 3;
				}
			}
		}
		return count;
	}

	public static List<RawSurfaceData> ExtractRawSurfaces(Mesh mesh)
	{
		List<RawSurfaceData> surfaces = new List<RawSurfaceData>();
		int surfaceCount = mesh.GetSurfaceCount();

		for (int s = 0; s < surfaceCount; s++)
		{
			var arrays = mesh.SurfaceGetArrays(s);
			if (arrays == null || arrays.Count <= (int)Mesh.ArrayType.Vertex)
			{
				continue;
			}

			Vector3[] posArray = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			if (posArray == null || posArray.Length == 0)
			{
				continue;
			}

			Vector3[] normalArray = arrays.Count > (int)Mesh.ArrayType.Normal ? arrays[(int)Mesh.ArrayType.Normal].AsVector3Array() : null;
			Vector2[] uvArray = arrays.Count > (int)Mesh.ArrayType.TexUV ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array() : null;
			float[] boneWeights = arrays.Count > (int)Mesh.ArrayType.Weights ? arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array() : null;
			int[] boneIndices = arrays.Count > (int)Mesh.ArrayType.Bones ? arrays[(int)Mesh.ArrayType.Bones].AsInt32Array() : null;
			Color[] colorArray = arrays.Count > (int)Mesh.ArrayType.Color ? arrays[(int)Mesh.ArrayType.Color].AsColorArray() : null;
			int[] indexArray = arrays.Count > (int)Mesh.ArrayType.Index ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : null;

			bool hasTangents = arrays.Count > (int)Mesh.ArrayType.Tangent &&
				arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil &&
				arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array() != null &&
				arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array().Length > 0;

			int vertexCount = posArray.Length;
			RawVertexData[] vertices = new RawVertexData[vertexCount];
			int boneStride = (boneWeights != null && boneIndices != null && boneWeights.Length >= vertexCount * 8 && boneIndices.Length >= vertexCount * 8) ? 8 : 4;

			for (int i = 0; i < vertexCount; i++)
			{
				vertices[i].Position = posArray[i];
				vertices[i].Normal = (normalArray != null && i < normalArray.Length) ? normalArray[i] : Vector3.Up;
				vertices[i].UV = (uvArray != null && i < uvArray.Length) ? uvArray[i] : Vector2.Zero;
				vertices[i].Color = (colorArray != null && i < colorArray.Length) ? colorArray[i] : Colors.White;

				if (boneIndices != null && boneWeights != null && (i * boneStride + Math.Min(4, boneStride) - 1) < boneIndices.Length && (i * boneStride + Math.Min(4, boneStride) - 1) < boneWeights.Length)
				{
					int count = Math.Min(boneStride, Math.Min(boneIndices.Length - i * boneStride, boneWeights.Length - i * boneStride));
					ReadOnlySpan<int> vertBoneIndices = boneIndices.AsSpan(i * boneStride, count);
					ReadOnlySpan<float> vertBoneWeights = boneWeights.AsSpan(i * boneStride, count);
					var (prunedIndices, prunedWeights) = PruneAndNormalizeBoneInfluences(vertBoneIndices, vertBoneWeights, 4, 0.01f);
					vertices[i].BoneIndices = prunedIndices;
					vertices[i].BoneWeights = prunedWeights;
				}
			}

			uint[] indices;
			if (indexArray != null && indexArray.Length > 0)
			{
				indices = new uint[indexArray.Length];
				for (int i = 0; i < indexArray.Length; i++)
				{
					indices[i] = (uint)indexArray[i];
				}
			}
			else
			{
				indices = new uint[vertexCount];
				for (uint i = 0; i < (uint)vertexCount; i++)
				{
					indices[i] = i;
				}
			}

			surfaces.Add(new RawSurfaceData
			{
				Vertices = vertices,
				Indices = indices,
				SurfaceMaterial = mesh.SurfaceGetMaterial(s),
				HasTangents = hasTangents
			});
		}

		return surfaces;
	}

	public static (Vector4 Indices, Vector4 Weights) PruneAndNormalizeBoneInfluences(
		ReadOnlySpan<int> rawIndices,
		ReadOnlySpan<float> rawWeights,
		int maxInfluences = 4,
		float weightThreshold = 0.01f)
	{
		int count = Math.Min(rawIndices.Length, rawWeights.Length);
		if (count == 0 || maxInfluences <= 0)
		{
			return (Vector4.Zero, Vector4.Zero);
		}

		Span<int> indices = stackalloc int[count];
		Span<float> weights = stackalloc float[count];

		for (int i = 0; i < count; i++)
		{
			float w = rawWeights[i];
			if (float.IsNaN(w) || float.IsInfinity(w) || w < weightThreshold)
			{
				weights[i] = 0f;
				indices[i] = 0;
			}
			else
			{
				weights[i] = w;
				indices[i] = rawIndices[i];
			}
		}

		for (int i = 0; i < count - 1; i++)
		{
			for (int j = i + 1; j < count; j++)
			{
				if (weights[j] > weights[i])
				{
					(weights[i], weights[j]) = (weights[j], weights[i]);
					(indices[i], indices[j]) = (indices[j], indices[i]);
				}
			}
		}

		if (maxInfluences == 1)
		{
			int topBone = weights[0] > 0f ? indices[0] : 0;
			float topWeight = weights[0] > 0f ? 1.0f : 0f;
			return (
				new Vector4(topBone, 0, 0, 0),
				new Vector4(topWeight, 0, 0, 0)
			);
		}

		int activeCount = Math.Min(maxInfluences, count);
		float totalWeight = 0f;
		for (int i = 0; i < activeCount; i++)
		{
			totalWeight += weights[i];
		}

		float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;
		int i0 = 0, i1 = 0, i2 = 0, i3 = 0;

		if (totalWeight > 0.0001f)
		{
			float invSum = 1.0f / totalWeight;
			if (activeCount > 0 && weights[0] > 0f) { w0 = weights[0] * invSum; i0 = indices[0]; }
			if (activeCount > 1 && weights[1] > 0f) { w1 = weights[1] * invSum; i1 = indices[1]; }
			if (activeCount > 2 && weights[2] > 0f) { w2 = weights[2] * invSum; i2 = indices[2]; }
			if (activeCount > 3 && weights[3] > 0f) { w3 = weights[3] * invSum; i3 = indices[3]; }
		}

		return (
			new Vector4(i0, i1, i2, i3),
			new Vector4(w0, w1, w2, w3)
		);
	}

	public static (Vector4 Indices, Vector4 Weights) PruneAndNormalizeBoneInfluences(
		Vector4 rawIndices,
		Vector4 rawWeights,
		int maxInfluences = 4,
		float weightThreshold = 0.01f)
	{
		Span<int> indices = stackalloc int[4] { (int)rawIndices.X, (int)rawIndices.Y, (int)rawIndices.Z, (int)rawIndices.W };
		Span<float> weights = stackalloc float[4] { rawWeights.X, rawWeights.Y, rawWeights.Z, rawWeights.W };
		return PruneAndNormalizeBoneInfluences(indices, weights, maxInfluences, weightThreshold);
	}

	public static (Vector4 Indices, Vector4 Weights) PruneAndNormalizeBoneInfluences(
		ReadOnlySpan<int> rawIndices,
		ReadOnlySpan<float> rawWeights,
		float weightThreshold)
	{
		return PruneAndNormalizeBoneInfluences(rawIndices, rawWeights, 4, weightThreshold);
	}

	public static (Vector4 Indices, Vector4 Weights) PruneAndNormalizeBoneInfluences(
		Vector4 rawIndices,
		Vector4 rawWeights,
		float weightThreshold)
	{
		return PruneAndNormalizeBoneInfluences(rawIndices, rawWeights, 4, weightThreshold);
	}

	public static unsafe RawSurfaceData OptimizeSurfaceGpuLayout(RawSurfaceData input)
	{
		if (input.Vertices == null || input.Vertices.Length == 0 || input.Indices == null || input.Indices.Length < 3)
		{
			return input;
		}

		int vertexCount = input.Vertices.Length;
		int indexCount = input.Indices.Length;

		uint[] indices = new uint[indexCount];
		Array.Copy(input.Indices, indices, indexCount);

		float[] positions = new float[vertexCount * 3];
		for (int i = 0; i < vertexCount; i++)
		{
			positions[i * 3] = input.Vertices[i].Position.X;
			positions[i * 3 + 1] = input.Vertices[i].Position.Y;
			positions[i * 3 + 2] = input.Vertices[i].Position.Z;
		}

		fixed (uint* indicesPtr = indices)
		fixed (float* posPtr = positions)
		{
			MeshOptimizerNative.meshopt_optimizeVertexCache(
				indicesPtr,
				indicesPtr,
				(nuint)indexCount,
				(nuint)vertexCount);

			MeshOptimizerNative.meshopt_optimizeOverdraw(
				indicesPtr,
				indicesPtr,
				(nuint)indexCount,
				posPtr,
				(nuint)vertexCount,
				(nuint)(3 * sizeof(float)),
				1.05f);

			uint[] remap = new uint[vertexCount];
			fixed (uint* remapPtr = remap)
			{
				nuint uniqueVertexCount = MeshOptimizerNative.meshopt_optimizeVertexFetchRemap(
					remapPtr,
					indicesPtr,
					(nuint)indexCount,
					(nuint)vertexCount);

				MeshOptimizerNative.meshopt_remapIndexBuffer(
					indicesPtr,
					indicesPtr,
					(nuint)indexCount,
					remapPtr);

				RawVertexData[] newVertices = new RawVertexData[uniqueVertexCount];
				for (int i = 0; i < vertexCount; i++)
				{
					uint newIdx = remap[i];
					if (newIdx != 0xFFFFFFFF && newIdx < uniqueVertexCount)
					{
						newVertices[newIdx] = input.Vertices[i];
					}
				}

				return new RawSurfaceData
				{
					Vertices = newVertices,
					Indices = indices,
					SurfaceMaterial = input.SurfaceMaterial,
					HasTangents = input.HasTangents
				};
			}
		}
	}

	public static RawSurfaceData WeldAndRepairTopology(RawSurfaceData input, int maxBoneInfluences = 4, bool ignoreBoneDifferences = false)
	{
		if (input.Vertices == null || input.Vertices.Length == 0 || input.Indices == null || input.Indices.Length == 0)
		{
			return input;
		}

		var uniqueMap = new Dictionary<VertexWeldKey, uint>(input.Vertices.Length);
		var uniqueVertices = new List<RawVertexData>(input.Vertices.Length);
		uint[] remap = new uint[input.Vertices.Length];

		for (int i = 0; i < input.Vertices.Length; i++)
		{
			var v = input.Vertices[i];
			var (prunedIndices, prunedWeights) = PruneAndNormalizeBoneInfluences(v.BoneIndices, v.BoneWeights, maxBoneInfluences, 0.01f);
			v.BoneIndices = prunedIndices;
			v.BoneWeights = prunedWeights;

			var key = new VertexWeldKey(v.Position, v.UV, v.BoneIndices, v.BoneWeights, v.Color, ignoreBoneDifferences);

			if (!uniqueMap.TryGetValue(key, out uint newIdx))
			{
				newIdx = (uint)uniqueVertices.Count;
				uniqueVertices.Add(v);
				uniqueMap[key] = newIdx;
			}

			remap[i] = newIdx;
		}

		var newIndices = new List<uint>(input.Indices.Length);
		for (int t = 0; t + 2 < input.Indices.Length; t += 3)
		{
			uint i0 = remap[input.Indices[t]];
			uint i1 = remap[input.Indices[t + 1]];
			uint i2 = remap[input.Indices[t + 2]];

			if (i0 != i1 && i1 != i2 && i0 != i2)
			{
				newIndices.Add(i0);
				newIndices.Add(i1);
				newIndices.Add(i2);
			}
		}

		var welded = new RawSurfaceData
		{
			Vertices = uniqueVertices.ToArray(),
			Indices = newIndices.ToArray(),
			SurfaceMaterial = input.SurfaceMaterial,
			HasTangents = input.HasTangents
		};

		return OptimizeSurfaceGpuLayout(welded);
	}

	public static RawSurfaceData WeldAndRepairTopology(RawSurfaceData input)
	{
		return WeldAndRepairTopology(input, 4, false);
	}

	public static unsafe RawSurfaceData SimplifySurface(RawSurfaceData input, int targetTriangleCount, float targetError, float uvWeight = 1.0f)
	{
		if (input.Vertices == null || input.Vertices.Length == 0 || input.Indices == null || input.Indices.Length < 3)
		{
			return input;
		}

		int vertexCount = input.Vertices.Length;
		int indexCount = input.Indices.Length;

		float[] positions = new float[vertexCount * 3];
		float[] uvs = new float[vertexCount * 2];

		for (int i = 0; i < vertexCount; i++)
		{
			positions[i * 3] = input.Vertices[i].Position.X;
			positions[i * 3 + 1] = input.Vertices[i].Position.Y;
			positions[i * 3 + 2] = input.Vertices[i].Position.Z;

			uvs[i * 2] = input.Vertices[i].UV.X;
			uvs[i * 2 + 1] = input.Vertices[i].UV.Y;
		}

		uint targetIndexCount = (uint)Math.Clamp(targetTriangleCount * 3, 3, indexCount);
		if (targetIndexCount >= (uint)indexCount && targetError <= 0.01f)
		{
			return input;
		}

		uint[] destination = new uint[indexCount];
		float resultError = 0f;
		float[] attributeWeights = new float[] { uvWeight, uvWeight };

		nuint simplifiedCount;

		fixed (uint* destPtr = destination)
		fixed (uint* srcIndicesPtr = input.Indices)
		fixed (float* posPtr = positions)
		fixed (float* uvPtr = uvs)
		fixed (float* weightsPtr = attributeWeights)
		{
			simplifiedCount = MeshOptimizerNative.meshopt_simplifyWithAttributes(
				destPtr,
				srcIndicesPtr,
				(nuint)indexCount,
				posPtr,
				(nuint)vertexCount,
				(nuint)(3 * sizeof(float)),
				uvPtr,
				(nuint)(2 * sizeof(float)),
				weightsPtr,
				2,
				null,
				(nuint)targetIndexCount,
				targetError,
				0,
				&resultError);
		}

		if (simplifiedCount == 0 || simplifiedCount >= (nuint)indexCount)
		{
			return input;
		}

		for (int i = 0; i < (int)simplifiedCount; i++)
		{
			if (destination[i] >= (uint)vertexCount)
			{
				return input;
			}
		}

		uint[] simplifiedIndices = new uint[simplifiedCount];
		Array.Copy(destination, simplifiedIndices, (int)simplifiedCount);

		fixed (uint* simpIndicesPtr = simplifiedIndices)
		fixed (float* posPtr = positions)
		{
			MeshOptimizerNative.meshopt_optimizeVertexCache(
				simpIndicesPtr,
				simpIndicesPtr,
				simplifiedCount,
				(nuint)vertexCount);

			MeshOptimizerNative.meshopt_optimizeOverdraw(
				simpIndicesPtr,
				simpIndicesPtr,
				simplifiedCount,
				posPtr,
				(nuint)vertexCount,
				(nuint)(3 * sizeof(float)),
				1.05f);

			uint[] remap = new uint[vertexCount];
			fixed (uint* remapPtr = remap)
			{
				nuint uniqueVertexCount = MeshOptimizerNative.meshopt_optimizeVertexFetchRemap(
					remapPtr,
					simpIndicesPtr,
					simplifiedCount,
					(nuint)vertexCount);

				MeshOptimizerNative.meshopt_remapIndexBuffer(
					simpIndicesPtr,
					simpIndicesPtr,
					simplifiedCount,
					remapPtr);

				RawVertexData[] newVertices = new RawVertexData[uniqueVertexCount];
				for (int i = 0; i < vertexCount; i++)
				{
					uint newIdx = remap[i];
					if (newIdx != 0xFFFFFFFF && newIdx < uniqueVertexCount)
					{
						newVertices[newIdx] = input.Vertices[i];
					}
				}

				return new RawSurfaceData
				{
					Vertices = newVertices,
					Indices = simplifiedIndices,
					SurfaceMaterial = input.SurfaceMaterial,
					HasTangents = input.HasTangents
				};
			}
		}
	}

	public static RawSurfaceData SimplifySurface(RawSurfaceData input, float targetRatio, float targetError, float uvWeight = 1.0f)
	{
		int originalTriangles = (input.Indices != null) ? input.Indices.Length / 3 : 0;
		int targetTriangles = (int)Math.Max(1, originalTriangles * targetRatio);
		return SimplifySurface(input, targetTriangles, targetError, uvWeight);
	}

	public static ArrayMesh BuildSmoothedMesh(List<RawSurfaceData> surfaces, float creaseAngleDegrees = 45.0f)
	{
		var arrayMesh = new ArrayMesh();
		float cosThreshold = Mathf.Cos(Mathf.DegToRad(creaseAngleDegrees));

		foreach (var surf in surfaces)
		{
			if (surf.Vertices == null || surf.Vertices.Length == 0 || surf.Indices == null || surf.Indices.Length < 3)
			{
				continue;
			}

			int triangleCount = surf.Indices.Length / 3;
			Vector3[] faceNormals = new Vector3[triangleCount];
			float[,] cornerWeights = new float[triangleCount, 3];

			for (int t = 0; t < triangleCount; t++)
			{
				Vector3 p0 = surf.Vertices[surf.Indices[t * 3]].Position;
				Vector3 p1 = surf.Vertices[surf.Indices[t * 3 + 1]].Position;
				Vector3 p2 = surf.Vertices[surf.Indices[t * 3 + 2]].Position;

				Vector3 e01 = p1 - p0;
				Vector3 e02 = p2 - p0;
				Vector3 e12 = p2 - p1;

				Vector3 fn = e01.Cross(e02);
				float len = fn.Length();
				faceNormals[t] = len > 0.000001f ? (fn / len) : Vector3.Up;

				float len01Sq = e01.LengthSquared();
				float len02Sq = e02.LengthSquared();
				float len12Sq = e12.LengthSquared();

				float a0 = (len01Sq > 0.0000000001f && len02Sq > 0.0000000001f) ? e01.AngleTo(e02) : 1.0f;
				float a1 = (len01Sq > 0.0000000001f && len12Sq > 0.0000000001f) ? (-e01).AngleTo(e12) : 1.0f;
				float a2 = (len02Sq > 0.0000000001f && len12Sq > 0.0000000001f) ? (-e02).AngleTo(-e12) : 1.0f;

				cornerWeights[t, 0] = float.IsNaN(a0) || a0 < 0.0001f ? 1.0f : a0;
				cornerWeights[t, 1] = float.IsNaN(a1) || a1 < 0.0001f ? 1.0f : a1;
				cornerWeights[t, 2] = float.IsNaN(a2) || a2 < 0.0001f ? 1.0f : a2;
			}

			var spatialPosMap = new Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>>();
			for (int t = 0; t < triangleCount; t++)
			{
				for (int c = 0; c < 3; c++)
				{
					Vector3 pos = surf.Vertices[surf.Indices[t * 3 + c]].Position;
					var key = new SpatialPositionKey(pos);
					if (!spatialPosMap.TryGetValue(key, out var list))
					{
						list = new List<(int TriIdx, int CornerIdx)>();
						spatialPosMap[key] = list;
					}
					list.Add((t, c));
				}
			}

			Vector3[,] cornerNormals = new Vector3[triangleCount, 3];

			foreach (var kvp in spatialPosMap)
			{
				var corners = kvp.Value;
				int cCount = corners.Count;

				if (cCount == 1)
				{
					cornerNormals[corners[0].TriIdx, corners[0].CornerIdx] = faceNormals[corners[0].TriIdx];
					continue;
				}

				for (int i = 0; i < cCount; i++)
				{
					var cA = corners[i];
					Vector3 nA = faceNormals[cA.TriIdx];

					Vector3 avgNormal = Vector3.Zero;
					for (int j = 0; j < cCount; j++)
					{
						var cB = corners[j];
						Vector3 nB = faceNormals[cB.TriIdx];

						float dot = nA.Dot(nB);
						if (dot >= cosThreshold)
						{
							float w = cornerWeights[cB.TriIdx, cB.CornerIdx];
							avgNormal += nB * w;
						}
					}

					float avgLen = avgNormal.Length();
					cornerNormals[cA.TriIdx, cA.CornerIdx] = avgLen > 0.000001f ? (avgNormal / avgLen) : nA;
				}
			}

			bool hasBones = false;
			for (int i = 0; i < surf.Vertices.Length; i++)
			{
				if (surf.Vertices[i].BoneWeights != Vector4.Zero)
				{
					hasBones = true;
					break;
				}
			}

			var st = new SurfaceTool();
			st.Begin(Mesh.PrimitiveType.Triangles);
			if (surf.SurfaceMaterial != null)
			{
				st.SetMaterial(surf.SurfaceMaterial);
			}

			for (int t = 0; t < triangleCount; t++)
			{
				for (int c = 0; c < 3; c++)
				{
					int vertexIdx = (int)surf.Indices[t * 3 + c];
					var v = surf.Vertices[vertexIdx];
					Vector3 computedNormal = v.Normal.LengthSquared() > 0.001f ? v.Normal : cornerNormals[t, c];

					st.SetNormal(computedNormal);
					st.SetUV(v.UV);

					if (hasBones)
					{
						int[] bones = new int[] { (int)v.BoneIndices.X, (int)v.BoneIndices.Y, (int)v.BoneIndices.Z, (int)v.BoneIndices.W };
						float[] weights = new float[] { v.BoneWeights.X, v.BoneWeights.Y, v.BoneWeights.Z, v.BoneWeights.W };
						st.SetBones(bones);
						st.SetWeights(weights);
					}

					st.SetColor(v.Color);
					st.AddVertex(v.Position);
				}
			}

			if (surf.HasTangents)
			{
				try
				{
					st.GenerateTangents();
				}
				catch
				{
				}
			}

			st.Index();
			st.Commit(arrayMesh);
		}

		return arrayMesh;
	}

	public static float CalculateTargetErrorFromScreenHeight(int viewportHeightPixels, float allowedPixelError = 1.5f)
	{
		if (viewportHeightPixels <= 0)
		{
			return 0.05f;
		}

		return Math.Clamp(allowedPixelError / viewportHeightPixels, 0.0001f, 0.5f);
	}

	public static (Shape3D Shape, Vector3 Offset) GenerateAnalyticalCollisionShape(Aabb aabb, bool isBuilding = false, float marginRatio = 1.0f)
	{
		Vector3 center = aabb.GetCenter();
		Vector3 size = aabb.Size * marginRatio;

		if (isBuilding)
		{
			var box = new BoxShape3D
			{
				Size = new Vector3(Math.Max(1.0f, size.X), Math.Max(1.0f, size.Y), Math.Max(1.0f, size.Z))
			};
			return (box, center);
		}

		float radius = Math.Max(size.X, size.Z) * 0.5f;
		float height = Math.Max(1.0f, size.Y);

		if (height >= radius * 2.0f)
		{
			var capsule = new CapsuleShape3D
			{
				Radius = Math.Max(0.2f, radius),
				Height = height
			};
			return (capsule, center);
		}
		else
		{
			var cylShape = new CylinderShape3D
			{
				Radius = Math.Max(0.2f, radius),
				Height = height
			};
			return (cylShape, center);
		}
	}

	public static byte[] ExportSceneToGlb(
		Node rootScene,
		bool embedCompletedFlag = true,
		int maxTextureResolution = 1024,
		List<string> originalImageNames = null)
	{
		try
		{
			var doc = new GltfDocument();
			var state = new GltfState();

			ApplyAlbedoGammaCompensation(rootScene);
			SetOwnerRecursive(rootScene, rootScene);
			doc.AppendFromScene(rootScene, state);

			byte[] glbBytes = doc.GenerateBuffer(state);
			if (glbBytes != null && glbBytes.Length > 0)
			{
				glbBytes = SanitizeGlbMaterialsBeforeGltfpack(glbBytes);
				glbBytes = ApplyKhrTextureBasisu(glbBytes, maxTextureResolution);
				if (embedCompletedFlag)
				{
					glbBytes = EmbedMsftLodAndFlags(glbBytes, originalImageNames);
				}
			}

			return glbBytes;
		}
		catch
		{
			return null;
		}
	}

	public static byte[] SanitizeGlbMaterialsBeforeGltfpack(byte[] glbBytes)
	{
		try
		{
			if (glbBytes == null || glbBytes.Length < 20) return glbBytes;
			uint magic = BitConverter.ToUInt32(glbBytes, 0);
			if (magic != 0x46546C67) return glbBytes;

			int currentOffset = 12;
			uint jsonChunkLength = BitConverter.ToUInt32(glbBytes, currentOffset);
			uint jsonChunkType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);
			if (jsonChunkType != 0x4E4F534A) return glbBytes;

			string jsonString = System.Text.Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)jsonChunkLength);
			var jsonNode = JsonNode.Parse(jsonString);
			if (jsonNode == null) return glbBytes;

			var rootObj = jsonNode.AsObject();
			if (rootObj.ContainsKey("materials") && rootObj["materials"] is JsonArray matArray)
			{
				foreach (var matNode in matArray)
				{
					if (matNode is JsonObject matObj && matObj.ContainsKey("pbrMetallicRoughness") && matObj["pbrMetallicRoughness"] is JsonObject pbrObj)
					{
						if (pbrObj.ContainsKey("baseColorTexture"))
						{
							pbrObj["baseColorFactor"] = new JsonArray(1.0, 1.0, 1.0, 1.0);
						}
					}
				}
			}

			byte[] newJsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonNode.ToJsonString());
			int paddedJsonLength = (newJsonBytes.Length + 3) & ~3;
			byte[] paddedJson = new byte[paddedJsonLength];
			Array.Copy(newJsonBytes, paddedJson, newJsonBytes.Length);
			for (int i = newJsonBytes.Length; i < paddedJsonLength; i++)
			{
				paddedJson[i] = 0x20;
			}

			int binChunkStart = currentOffset + 8 + (int)jsonChunkLength;
			int binChunkRemaining = glbBytes.Length - binChunkStart;

			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);

			writer.Write(magic);
			writer.Write(BitConverter.ToUInt32(glbBytes, 4));
			uint newTotalLength = 12 + 8 + (uint)paddedJsonLength + (uint)binChunkRemaining;
			writer.Write(newTotalLength);

			writer.Write((uint)paddedJsonLength);
			writer.Write(0x4E4F534A);
			writer.Write(paddedJson);

			if (binChunkRemaining > 0)
			{
				writer.Write(glbBytes, binChunkStart, binChunkRemaining);
			}

			return ms.ToArray();
		}
		catch
		{
			return glbBytes;
		}
	}

	private static string FindGltfPackPath()
	{
		string[] candidatePaths = new string[]
		{
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThirdPartyBinaries", "gltfpack.exe"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThirdPartyBinaries", "gltfpack"),
			Path.Combine(PathUtils.GetProjectRoot(), "ThirdPartyBinaries", "gltfpack.exe"),
			Path.Combine(PathUtils.GetProjectRoot(), "ThirdPartyBinaries", "gltfpack"),
			Path.Combine(PathUtils.GetProjectRoot(), "..", "ThirdPartyBinaries", "gltfpack.exe"),
			Path.Combine(PathUtils.GetProjectRoot(), "..", "ThirdPartyBinaries", "gltfpack"),
			"gltfpack.exe",
			"gltfpack"
		};

		foreach (var path in candidatePaths)
		{
			if (File.Exists(path))
			{
				return Path.GetFullPath(path);
			}
		}

		return "gltfpack.exe";
	}

	public static byte[] ApplyKhrTextureBasisu(byte[] glbBytes, int maxTextureResolution = 1024)
	{
		if (glbBytes == null || glbBytes.Length == 0)
		{
			return glbBytes;
		}

		string gltfpackPath = FindGltfPackPath();
		if (string.IsNullOrEmpty(gltfpackPath))
		{
			return glbBytes;
		}

		string tempDir = Path.Combine(Path.GetTempPath(), $"realm_gltf_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string tempInput = Path.Combine(tempDir, "input.glb");
		string tempOutput = Path.Combine(tempDir, "output.glb");

		try
		{
			File.WriteAllBytes(tempInput, glbBytes);

			var psi = new System.Diagnostics.ProcessStartInfo
			{
				FileName = gltfpackPath,
				Arguments = $"-i \"{tempInput}\" -o \"{tempOutput}\" -tc -tl {maxTextureResolution} -kn -km -ke -noq",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			using var proc = System.Diagnostics.Process.Start(psi);
			if (proc != null)
			{
				proc.WaitForExit(30000);
				if (proc.ExitCode == 0 && File.Exists(tempOutput))
				{
					byte[] resultBytes = File.ReadAllBytes(tempOutput);
					if (resultBytes != null && resultBytes.Length > 0)
					{
						return resultBytes;
					}
				}
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir))
				{
					Directory.Delete(tempDir, true);
				}
			}
			catch
			{
			}
		}

		return glbBytes;
	}

	private static readonly byte[] GammaCompensationLookupTable = BuildGammaCompensationLookupTable();

	private static byte[] BuildGammaCompensationLookupTable()
	{
		byte[] lookupTable = new byte[256];
		for (int i = 0; i < 256; i++)
		{
			float normalized = i / 255.0f;
			float compensated = MathF.Pow(normalized, 1.0f / 2.2f);
			lookupTable[i] = (byte)Math.Clamp((int)MathF.Round(compensated * 255.0f), 0, 255);
		}
		return lookupTable;
	}

	public static void ApplyAlbedoGammaCompensation(Node rootScene)
	{
		var meshInstances = FindAllMeshInstances(rootScene);
		var processedMaterials = new HashSet<Material>();

		foreach (var meshInst in meshInstances)
		{
			int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 0;
			for (int s = 0; s < surfaceCount; s++)
			{
				var mat = meshInst.GetSurfaceOverrideMaterial(s) ?? meshInst.Mesh?.SurfaceGetMaterial(s);
				if (mat is StandardMaterial3D stdMat && processedMaterials.Add(stdMat))
				{
					if (stdMat.AlbedoTexture is Texture2D albedoTex)
					{
						var img = albedoTex.GetImage();
						if (img != null)
						{
							var compensated = CompensateAlbedoGamma(img);
							stdMat.AlbedoTexture = ImageTexture.CreateFromImage(compensated);
						}
					}
				}
				else if (mat is OrmMaterial3D ormMat && processedMaterials.Add(ormMat))
				{
					if (ormMat.AlbedoTexture is Texture2D albedoTex)
					{
						var img = albedoTex.GetImage();
						if (img != null)
						{
							var compensated = CompensateAlbedoGamma(img);
							ormMat.AlbedoTexture = ImageTexture.CreateFromImage(compensated);
						}
					}
				}
			}
		}
	}

	public static Image CompensateAlbedoGamma(Image source)
	{
		Image working = (Image)source.Duplicate();
		if (working.IsCompressed())
		{
			working.Decompress();
		}
		if (working.GetFormat() != Image.Format.Rgba8 && working.GetFormat() != Image.Format.Rgb8)
		{
			working.Convert(Image.Format.Rgba8);
		}

		byte[] data = working.GetData();
		int bytesPerPixel = working.GetFormat() == Image.Format.Rgba8 ? 4 : 3;

		for (int i = 0; i < data.Length; i += bytesPerPixel)
		{
			data[i] = GammaCompensationLookupTable[data[i]];
			data[i + 1] = GammaCompensationLookupTable[data[i + 1]];
			data[i + 2] = GammaCompensationLookupTable[data[i + 2]];
		}

		var result = Image.CreateEmpty(working.GetWidth(), working.GetHeight(), working.HasMipmaps(), working.GetFormat());
		result.SetData(working.GetWidth(), working.GetHeight(), working.HasMipmaps(), working.GetFormat(), data);
		return result;
	}

	private static void SetOwnerRecursive(Node node, Node owner)
	{
		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			Node child = node.GetChild(i);
			child.Owner = owner;
			SetOwnerRecursive(child, owner);
		}
	}

	public readonly struct VertexWeldKey : IEquatable<VertexWeldKey>
	{
		public readonly int X;
		public readonly int Y;
		public readonly int Z;
		public readonly int U;
		public readonly int V;
		public readonly int ColorHash;
		public readonly int BoneHash;

		public VertexWeldKey(Vector3 pos, Vector2 uv, Vector4 bones, Vector4 weights, Color color, bool ignoreBoneDifferences = false)
		{
			X = (int)MathF.Round(pos.X * 10000.0f);
			Y = (int)MathF.Round(pos.Y * 10000.0f);
			Z = (int)MathF.Round(pos.Z * 10000.0f);
			U = (int)MathF.Round(uv.X * 10000.0f);
			V = (int)MathF.Round(uv.Y * 10000.0f);

			int cR = (int)MathF.Round(color.R * 255.0f);
			int cG = (int)MathF.Round(color.G * 255.0f);
			int cB = (int)MathF.Round(color.B * 255.0f);
			int cA = (int)MathF.Round(color.A * 255.0f);
			ColorHash = HashCode.Combine(cR, cG, cB, cA);

			if (ignoreBoneDifferences)
			{
				BoneHash = 0;
			}
			else
			{
				int b0 = (int)bones.X;
				int b1 = (int)bones.Y;
				int b2 = (int)bones.Z;
				int b3 = (int)bones.W;
				int w0 = (int)MathF.Round(weights.X * 100.0f);
				int w1 = (int)MathF.Round(weights.Y * 100.0f);
				int w2 = (int)MathF.Round(weights.Z * 100.0f);
				int w3 = (int)MathF.Round(weights.W * 100.0f);

				BoneHash = HashCode.Combine(b0, b1, b2, b3, w0, w1, w2, w3);
			}
		}

		public bool Equals(VertexWeldKey other)
		{
			return X == other.X && Y == other.Y && Z == other.Z && U == other.U && V == other.V && ColorHash == other.ColorHash && BoneHash == other.BoneHash;
		}

		public override bool Equals(object? obj)
		{
			return obj is VertexWeldKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(X, Y, Z, U, V, ColorHash, BoneHash);
		}
	}

	public readonly struct SpatialPositionKey : IEquatable<SpatialPositionKey>
	{
		public readonly int X;
		public readonly int Y;
		public readonly int Z;

		public SpatialPositionKey(Vector3 pos)
		{
			X = (int)MathF.Round(pos.X * 10000.0f);
			Y = (int)MathF.Round(pos.Y * 10000.0f);
			Z = (int)MathF.Round(pos.Z * 10000.0f);
		}

		public bool Equals(SpatialPositionKey other)
		{
			return X == other.X && Y == other.Y && Z == other.Z;
		}

		public override bool Equals(object? obj)
		{
			return obj is SpatialPositionKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(X, Y, Z);
		}
	}
}
