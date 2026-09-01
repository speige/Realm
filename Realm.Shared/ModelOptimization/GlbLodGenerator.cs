using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared;

namespace Realm.Shared.ModelOptimization;

public static unsafe class GlbLodGenerator
{
	public static readonly float[] DefaultLodRatios = new float[] { 1.0f, 0.50f, 0.25f, 0.10f };
	public static readonly float[] DefaultVisibilityBegins = new float[] { 0f, 45f, 90f, 150f };
	public static readonly float[] DefaultVisibilityEnds = new float[] { 45f, 90f, 150f, 0f };

	public static (bool Success, byte[] OutputGlbBytes, string ErrorMessage) GenerateLods(
		byte[] inputGlbBytes,
		float[]? lodRatios = null,
		float[]? visBegins = null,
		float[]? visEnds = null)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20)
		{
			return (false, inputGlbBytes ?? Array.Empty<byte>(), "Invalid GLB buffer");
		}

		lodRatios ??= DefaultLodRatios;
		visBegins ??= DefaultVisibilityBegins;
		visEnds ??= DefaultVisibilityEnds;

		try
		{
			var (jsonNode, binBytes, glbVer) = GlbManifestUtils.ParseGlb(inputGlbBytes);
			if (jsonNode is not JsonObject root)
			{
				return (false, inputGlbBytes, "Failed to parse glTF JSON root");
			}

			if (root["meshes"] is not JsonArray meshes || meshes.Count == 0 ||
				root["accessors"] is not JsonArray accessors ||
				root["bufferViews"] is not JsonArray bufferViews ||
				root["buffers"] is not JsonArray buffers)
			{
				return (true, inputGlbBytes, string.Empty);
			}

			binBytes ??= Array.Empty<byte>();
			using var newBinStream = new MemoryStream();
			newBinStream.Write(binBytes, 0, binBytes.Length);

			var meshLodMap = new Dictionary<int, List<int>>(); // origMeshIdx -> list of LOD mesh indices [LOD1, LOD2, LOD3]

			for (int m = 0; m < meshes.Count; m++)
			{
				if (meshes[m] is not JsonObject meshObj) continue;
				if (meshObj["primitives"] is not JsonArray primitives || primitives.Count == 0) continue;

				string meshName = meshObj["name"]?.GetValue<string>() ?? $"Mesh_{m}";
				if (meshName.Contains("_LOD", StringComparison.OrdinalIgnoreCase)) continue;

				var lodMeshIndices = new List<int>();

				for (int t = 1; t < lodRatios.Length; t++)
				{
					float ratio = lodRatios[t];
					var newPrimitives = new JsonArray();

					for (int p = 0; p < primitives.Count; p++)
					{
						if (primitives[p] is not JsonObject primObj) continue;
						if (primObj["attributes"] is not JsonObject attributes) continue;
						if (!attributes.ContainsKey("POSITION")) continue;

						int posAccIdx = attributes["POSITION"]!.GetValue<int>();
						if (posAccIdx < 0 || posAccIdx >= accessors.Count) continue;
						var posAcc = accessors[posAccIdx] as JsonObject;
						if (posAcc == null) continue;

						int posBvIdx = posAcc["bufferView"]!.GetValue<int>();
						var posBv = bufferViews[posBvIdx] as JsonObject;
						if (posBv == null) continue;

						int posByteOffset = (posAcc["byteOffset"]?.GetValue<int>() ?? 0) +
											(posBv["byteOffset"]?.GetValue<int>() ?? 0);
						int posCount = posAcc["count"]!.GetValue<int>();
						int posStride = posBv["byteStride"]?.GetValue<int>() ?? 12;

						if (posCount < 3 || posByteOffset + (posCount * posStride) > binBytes.Length) continue;

						uint[] originalIndices;
						if (primObj.ContainsKey("indices"))
						{
							int indAccIdx = primObj["indices"]!.GetValue<int>();
							if (indAccIdx < 0 || indAccIdx >= accessors.Count) continue;
							var indAcc = accessors[indAccIdx] as JsonObject;
							if (indAcc == null) continue;

							int indBvIdx = indAcc["bufferView"]!.GetValue<int>();
							var indBv = bufferViews[indBvIdx] as JsonObject;
							if (indBv == null) continue;

							int indByteOffset = (indAcc["byteOffset"]?.GetValue<int>() ?? 0) +
												(indBv["byteOffset"]?.GetValue<int>() ?? 0);
							int indCount = indAcc["count"]!.GetValue<int>();
							int componentType = indAcc["componentType"]!.GetValue<int>();

							originalIndices = new uint[indCount];
							if (componentType == 5123) // UNSIGNED_SHORT
							{
								for (int i = 0; i < indCount; i++)
								{
									originalIndices[i] = BitConverter.ToUInt16(binBytes, indByteOffset + (i * 2));
								}
							}
							else if (componentType == 5125) // UNSIGNED_INT
							{
								for (int i = 0; i < indCount; i++)
								{
									originalIndices[i] = BitConverter.ToUInt32(binBytes, indByteOffset + (i * 4));
								}
							}
							else if (componentType == 5121) // UNSIGNED_BYTE
							{
								for (int i = 0; i < indCount; i++)
								{
									originalIndices[i] = binBytes[indByteOffset + i];
								}
							}
							else
							{
								continue;
							}
						}
						else
						{
							originalIndices = new uint[posCount];
							for (uint i = 0; i < posCount; i++) originalIndices[i] = i;
						}

						if (originalIndices.Length < 12)
						{
							// Too small to simplify, clone primitive as is
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						int targetIndexCount = Math.Max(3, (int)(originalIndices.Length * ratio) / 3 * 3);
						uint[] simplifiedIndices = new uint[originalIndices.Length];
						nuint simplifiedCount = 0;

						fixed (uint* pOrig = originalIndices)
						fixed (uint* pDest = simplifiedIndices)
						fixed (byte* pBin = binBytes)
						{
							float* pPos = (float*)(pBin + posByteOffset);
							float resultError = 0f;

							simplifiedCount = MeshOptimizerNative.meshopt_simplify(
								pDest,
								pOrig,
								(nuint)originalIndices.Length,
								pPos,
								(nuint)posCount,
								(nuint)posStride,
								(nuint)targetIndexCount,
								0.02f * t,
								0,
								&resultError);

							if (simplifiedCount >= (nuint)originalIndices.Length || simplifiedCount < 3)
							{
								simplifiedCount = MeshOptimizerNative.meshopt_simplifySloppy(
									pDest,
									pOrig,
									(nuint)originalIndices.Length,
									pPos,
									(nuint)posCount,
									(nuint)posStride,
									(nuint)targetIndexCount,
									0.05f * t,
									&resultError);
							}

							if (simplifiedCount > 0 && simplifiedCount < (nuint)originalIndices.Length)
							{
								MeshOptimizerNative.meshopt_optimizeVertexCache(pDest, pDest, simplifiedCount, (nuint)posCount);
							}
						}

						if (simplifiedCount == 0 || simplifiedCount >= (nuint)originalIndices.Length)
						{
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						// Align BIN stream to 4 bytes
						while ((newBinStream.Position % 4) != 0)
						{
							newBinStream.WriteByte(0);
						}

						int newIndByteOffset = (int)newBinStream.Position;
						bool useShort = posCount <= 65535;
						int newIndByteLength = (int)simplifiedCount * (useShort ? 2 : 4);

						for (int i = 0; i < (int)simplifiedCount; i++)
						{
							if (useShort)
							{
								byte[] bytes = BitConverter.GetBytes((ushort)simplifiedIndices[i]);
								newBinStream.Write(bytes, 0, 2);
							}
							else
							{
								byte[] bytes = BitConverter.GetBytes(simplifiedIndices[i]);
								newBinStream.Write(bytes, 0, 4);
							}
						}

						// Add bufferView for indices
						int newBvIdx = bufferViews.Count;
						var newBv = new JsonObject
						{
							["buffer"] = 0,
							["byteOffset"] = newIndByteOffset,
							["byteLength"] = newIndByteLength,
							["target"] = 34963 // ELEMENT_ARRAY_BUFFER
						};
						bufferViews.Add(newBv);

						// Add accessor for indices
						int newAccIdx = accessors.Count;
						var newAcc = new JsonObject
						{
							["bufferView"] = newBvIdx,
							["byteOffset"] = 0,
							["componentType"] = useShort ? 5123 : 5125,
							["count"] = (int)simplifiedCount,
							["type"] = "SCALAR"
						};
						accessors.Add(newAcc);

						var newPrim = new JsonObject
						{
							["attributes"] = primObj["attributes"]!.DeepClone(),
							["indices"] = newAccIdx
						};
						if (primObj.ContainsKey("material"))
						{
							newPrim["material"] = primObj["material"]!.GetValue<int>();
						}

						newPrimitives.Add(newPrim);
					}

					if (newPrimitives.Count > 0)
					{
						int newMeshIdx = meshes.Count;
						var newMesh = new JsonObject
						{
							["name"] = $"{meshName}_LOD{t}",
							["primitives"] = newPrimitives
						};
						meshes.Add(newMesh);
						lodMeshIndices.Add(newMeshIdx);
					}
				}

				if (lodMeshIndices.Count > 0)
				{
					meshLodMap[m] = lodMeshIndices;
					meshObj["name"] = $"{meshName}_LOD0";
				}
			}

			// Align final BIN stream to 4 bytes
			while ((newBinStream.Position % 4) != 0)
			{
				newBinStream.WriteByte(0);
			}

			byte[] finalBinBytes = newBinStream.ToArray();
			if (buffers[0] is JsonObject buf0)
			{
				buf0["byteLength"] = finalBinBytes.Length;
			}

			// Add MSFT_lod to extensionsUsed
			if (root["extensionsUsed"] is not JsonArray extUsed)
			{
				extUsed = new JsonArray();
				root["extensionsUsed"] = extUsed;
			}
			bool hasMsftLod = false;
			foreach (var item in extUsed)
			{
				if (item?.ToString() == "MSFT_lod") { hasMsftLod = true; break; }
			}
			if (!hasMsftLod)
			{
				extUsed.Add("MSFT_lod");
			}

			// Update scene nodes with LOD siblings & MSFT_lod
			if (root["nodes"] is JsonArray nodes && nodes.Count > 0)
			{
				int initialNodeCount = nodes.Count;
				for (int n = 0; n < initialNodeCount; n++)
				{
					if (nodes[n] is not JsonObject nodeObj) continue;
					if (!nodeObj.ContainsKey("mesh")) continue;

					int meshIdx = nodeObj["mesh"]!.GetValue<int>();
					if (meshLodMap.TryGetValue(meshIdx, out var lodMeshesList))
					{
						string nodeName = nodeObj["name"]?.GetValue<string>() ?? $"Node_{n}";
						if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
						{
							nodeName = nodeName.Substring(0, nodeName.Length - 5);
						}
						nodeObj["name"] = $"{nodeName}_LOD0";

						var lodNodeIndices = new JsonArray();

						for (int t = 0; t < lodMeshesList.Count; t++)
						{
							int lodMeshIdx = lodMeshesList[t];
							int lodTier = t + 1;

							var lodNode = new JsonObject
							{
								["name"] = $"{nodeName}_LOD{lodTier}",
								["mesh"] = lodMeshIdx
							};

							if (nodeObj.ContainsKey("skin")) lodNode["skin"] = nodeObj["skin"]!.GetValue<int>();
							if (nodeObj.ContainsKey("matrix")) lodNode["matrix"] = nodeObj["matrix"]!.DeepClone();
							if (nodeObj.ContainsKey("translation")) lodNode["translation"] = nodeObj["translation"]!.DeepClone();
							if (nodeObj.ContainsKey("rotation")) lodNode["rotation"] = nodeObj["rotation"]!.DeepClone();
							if (nodeObj.ContainsKey("scale")) lodNode["scale"] = nodeObj["scale"]!.DeepClone();

							var lodExtras = new JsonObject
							{
								["visibility_range_begin"] = visBegins[lodTier],
								["visibility_range_end"] = visEnds[lodTier]
							};
							lodNode["extras"] = lodExtras;

							int newLodNodeIdx = nodes.Count;
							nodes.Add(lodNode);
							lodNodeIndices.Add(newLodNodeIdx);

							// If scene has root nodes, add LOD sibling node to scenes
							if (root["scenes"] is JsonArray scenes)
							{
								foreach (var sc in scenes)
								{
									if (sc is JsonObject scObj && scObj["nodes"] is JsonArray scNodes)
									{
										for (int sn = 0; sn < scNodes.Count; sn++)
										{
											if (scNodes[sn]?.GetValue<int>() == n)
											{
												scNodes.Add(newLodNodeIdx);
												break;
											}
										}
									}
								}
							}
						}

						if (nodeObj["extensions"] is not JsonObject nodeExts)
						{
							nodeExts = new JsonObject();
							nodeObj["extensions"] = nodeExts;
						}
						nodeExts["MSFT_lod"] = new JsonObject
						{
							["ids"] = lodNodeIndices
						};

						if (nodeObj["extras"] is not JsonObject nExtras)
						{
							nExtras = new JsonObject();
							nodeObj["extras"] = nExtras;
						}
						nExtras["visibility_range_begin"] = visBegins[0];
						nExtras["visibility_range_end"] = visEnds[0];
					}
				}
			}

			byte[] outputGlb = GlbManifestUtils.BuildGlb(root, finalBinBytes, glbVer);
			return (true, outputGlb, string.Empty);
		}
		catch (Exception ex)
		{
			return (false, inputGlbBytes, $"Failed to generate LODs: {ex.Message}");
		}
	}
}
