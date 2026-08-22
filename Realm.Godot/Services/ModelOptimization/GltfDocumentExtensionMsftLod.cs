using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Realm.Godot.Services.ModelOptimization;

public partial class GltfDocumentExtensionMsftLod : GltfDocumentExtension
{
	private static bool _isRegistered = false;
	private static readonly object _lock = new object();

	public static void RegisterExtension()
	{
		lock (_lock)
		{
			if (!_isRegistered)
			{
				GltfDocument.RegisterGltfDocumentExtension(new GltfDocumentExtensionMsftLod(), true);
				_isRegistered = true;
			}
		}
	}

	public override string[] _GetSupportedExtensions()
	{
		return new string[] { "MSFT_lod" };
	}

	public override Error _ImportPreflight(GltfState state, string[] extensions)
	{
		return Error.Ok;
	}

	public override Error _ParseNodeExtensions(GltfState state, GltfNode gltfNode, global::Godot.Collections.Dictionary extensions)
	{
		if (extensions != null && extensions.ContainsKey("MSFT_lod"))
		{
			gltfNode.SetAdditionalData("MSFT_lod", extensions["MSFT_lod"]);
		}
		return Error.Ok;
	}

	public override Error _ImportPost(GltfState state, Node root)
	{
		ProcessImportedScene(state, root);
		return Error.Ok;
	}

	public static void ProcessImportedScene(GltfState state, Node root)
	{
		if (root == null || state == null)
		{
			return;
		}

		try
		{
			var gltfNodes = state.GetNodes();
			var gltfMeshes = state.GetMeshes();

			var meshInstances = new List<MeshInstance3D>();
			CollectMeshInstances(root, meshInstances);

			var masterGltfNodes = new Dictionary<string, (int NodeIndex, GltfNode Node)>();
			var companionLodNodes = new Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>>();

			for (int n = 0; n < gltfNodes.Count; n++)
			{
				var gNode = gltfNodes[n];
				string rawName = !string.IsNullOrEmpty(gNode.OriginalName) ? gNode.OriginalName : gNode.ResourceName;

				if (string.IsNullOrEmpty(rawName))
				{
					continue;
				}

				if (rawName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase) || rawName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
				{
					string prefix = rawName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase)
						? rawName.Substring(0, rawName.Length - 5)
						: rawName.Substring(0, rawName.Length - 4);
					masterGltfNodes[prefix] = (n, gNode);
				}
				else if (rawName.Contains("_LOD") || rawName.Contains("LOD"))
				{
					int lodTagIdx = rawName.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
					int tagLen = 4;
					if (lodTagIdx < 0)
					{
						lodTagIdx = rawName.LastIndexOf("LOD", StringComparison.OrdinalIgnoreCase);
						tagLen = 3;
					}

					if (lodTagIdx >= 0 && lodTagIdx + tagLen < rawName.Length && int.TryParse(rawName.Substring(lodTagIdx + tagLen), out int level))
					{
						string prefix = rawName.Substring(0, lodTagIdx);
						if (!companionLodNodes.TryGetValue(prefix, out var dict))
						{
							dict = new SortedDictionary<int, (int NodeIndex, GltfNode Node)>();
							companionLodNodes[prefix] = dict;
						}
						dict[level] = (n, gNode);
					}
				}
			}

			foreach (var kvp in masterGltfNodes)
			{
				string prefix = kvp.Key;
				var masterData = kvp.Value;
				int masterNodeIndex = masterData.NodeIndex;
				GltfNode masterGltfNode = masterData.Node;

				string masterNodeName = !string.IsNullOrEmpty(masterGltfNode.OriginalName) ? masterGltfNode.OriginalName : masterGltfNode.ResourceName;
				MeshInstance3D masterInstance = FindMeshInstanceByNameOrIndex(meshInstances, masterNodeName, masterNodeIndex);

				if (masterInstance == null && meshInstances.Count == 1)
				{
					masterInstance = meshInstances[0];
				}

				if (masterInstance != null)
				{
					ApplyLodSettings(masterInstance, 0);

					Node parentNode = masterInstance.GetParent() ?? root;

					if (companionLodNodes.TryGetValue(prefix, out var lodDict))
					{
						foreach (var lodKvp in lodDict)
						{
							int lodLevel = lodKvp.Key;
							var lodData = lodKvp.Value;
							int lodNodeIndex = lodData.NodeIndex;
							GltfNode lodGltfNode = lodData.Node;

							string lodNodeName = !string.IsNullOrEmpty(lodGltfNode.OriginalName) ? lodGltfNode.OriginalName : lodGltfNode.ResourceName;
							if (string.IsNullOrEmpty(lodNodeName))
							{
								lodNodeName = $"{prefix}_LOD{lodLevel}";
							}

							MeshInstance3D existingLodInstance = FindMeshInstanceByNameOrIndex(meshInstances, lodNodeName, lodNodeIndex);
							if (existingLodInstance != null && existingLodInstance != masterInstance)
							{
								ApplyLodSettings(existingLodInstance, lodLevel);
							}
							else
							{
								int meshIndex = lodGltfNode.Mesh;
								if (meshIndex >= 0 && meshIndex < gltfMeshes.Count)
								{
									var gltfMesh = gltfMeshes[meshIndex];
									Mesh importedMesh = gltfMesh.Mesh?.GetMesh();

									if (importedMesh != null)
									{
										var newLodInstance = new MeshInstance3D
										{
											Name = lodNodeName,
											Mesh = importedMesh,
											Transform = masterInstance.Transform
										};

										int surfaceCount = importedMesh.GetSurfaceCount();
										for (int s = 0; s < surfaceCount; s++)
										{
											var mat = masterInstance.GetSurfaceOverrideMaterial(s) ?? masterInstance.Mesh?.SurfaceGetMaterial(s);
											if (mat != null)
											{
												newLodInstance.SetSurfaceOverrideMaterial(s, mat);
											}
										}

										if (masterInstance.MaterialOverride != null)
										{
											newLodInstance.MaterialOverride = masterInstance.MaterialOverride;
										}

										if (masterInstance.Skin != null)
										{
											newLodInstance.Skin = masterInstance.Skin;
											newLodInstance.Skeleton = masterInstance.Skeleton;
										}

										ApplyLodSettings(newLodInstance, lodLevel);
										parentNode.AddChild(newLodInstance);
										newLodInstance.Owner = root;
										meshInstances.Add(newLodInstance);
									}
								}
							}
						}
					}
				}
			}

			ConfigureExistingLodNodes(meshInstances);
		}
		catch
		{
		}
	}

	private static void CollectMeshInstances(Node current, List<MeshInstance3D> list)
	{
		if (current is MeshInstance3D mi && mi.Mesh != null)
		{
			list.Add(mi);
		}

		int childCount = current.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			CollectMeshInstances(current.GetChild(i), list);
		}
	}

	private static MeshInstance3D FindMeshInstanceByNameOrIndex(List<MeshInstance3D> instances, string name, int index)
	{
		if (instances == null || instances.Count == 0)
		{
			return null;
		}

		if (!string.IsNullOrEmpty(name))
		{
			foreach (var mi in instances)
			{
				string miName = mi.Name.ToString();
				if (miName == name || miName.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(miName, StringComparison.OrdinalIgnoreCase))
				{
					return mi;
				}
			}
		}

		if (index >= 0 && index < instances.Count)
		{
			return instances[index];
		}

		if (instances.Count == 1)
		{
			return instances[0];
		}

		foreach (var mi in instances)
		{
			string miName = mi.Name.ToString();
			if (miName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase) || miName.Contains("LOD0", StringComparison.OrdinalIgnoreCase))
			{
				return mi;
			}
		}

		return instances[0];
	}

	private static void ConfigureExistingLodNodes(List<MeshInstance3D> meshInstances)
	{
		foreach (var mi in meshInstances)
		{
			string name = mi.Name.ToString();
			if (name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 0);
			}
			else if (name.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD1", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 1);
			}
			else if (name.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD2", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 2);
			}
			else if (name.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD3", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 3);
			}
		}
	}

	public static void ApplyLodSettings(MeshInstance3D meshInstance, int lodLevel, float scaleMultiplier = 1.0f)
	{
		if (meshInstance == null)
		{
			return;
		}

		meshInstance.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
		meshInstance.VisibilityRangeBeginMargin = 0f;
		meshInstance.VisibilityRangeEndMargin = 0f;

		float scale = Math.Max(0.01f, scaleMultiplier);

		switch (lodLevel)
		{
			case 0:
				meshInstance.VisibilityRangeBegin = 0f;
				meshInstance.VisibilityRangeEnd = 4f * scale;
				meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
				break;
			case 1:
				meshInstance.VisibilityRangeBegin = 4f * scale;
				meshInstance.VisibilityRangeEnd = 12f * scale;
				meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
				break;
			case 2:
				meshInstance.VisibilityRangeBegin = 12f * scale;
				meshInstance.VisibilityRangeEnd = 30f * scale;
				meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
				break;
			case 3:
			default:
				meshInstance.VisibilityRangeBegin = 30f * scale;
				meshInstance.VisibilityRangeEnd = 0f;
				meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				break;
		}
	}

	public static void UpdateLodVisibilityRanges(Node rootNode, float scaleMultiplier)
	{
		if (rootNode == null || scaleMultiplier <= 0f)
		{
			return;
		}

		var meshInstances = new List<MeshInstance3D>();
		CollectMeshInstances(rootNode, meshInstances);

		foreach (var mi in meshInstances)
		{
			string name = mi.Name.ToString();
			if (name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 0, scaleMultiplier);
			}
			else if (name.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD1", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 1, scaleMultiplier);
			}
			else if (name.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD2", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 2, scaleMultiplier);
			}
			else if (name.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase) || name.EndsWith("LOD3", StringComparison.OrdinalIgnoreCase))
			{
				ApplyLodSettings(mi, 3, scaleMultiplier);
			}
		}
	}
}
