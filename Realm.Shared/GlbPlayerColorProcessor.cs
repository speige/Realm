using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Realm.Shared;

public class GlbPlayerColorResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputFilePath { get; set; }
    public int MaskedFaceCount { get; set; }
    public int TotalFaceCount { get; set; }
}

public class GlbPlayerColorOptions
{
    public string TargetHex { get; set; } = "#FF00FF";
    public float CoreThreshold { get; set; } = 0.88f;
    public float FringeThreshold { get; set; } = 0.80f;
    public int MinClusterFaces { get; set; } = 10;
    public int DilationRadius { get; set; } = 3;
}

public static class GlbPlayerColorProcessor
{
    public static GlbPlayerColorResult ProcessFile(
        string inputPath,
        string outputPath,
        GlbPlayerColorOptions options)
    {
        if (!File.Exists(inputPath))
        {
            return new GlbPlayerColorResult
            {
                Success = false,
                ErrorMessage = $"Input file does not exist: {inputPath}"
            };
        }

        try
        {
            byte[] inputBytes = File.ReadAllBytes(inputPath);
            var (success, outputBytes, errorMessage, maskedFaces, totalFaces) = ProcessBytes(inputBytes, options);

            if (!success)
            {
                return new GlbPlayerColorResult { Success = false, ErrorMessage = errorMessage };
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outputPath, outputBytes!);
            RealmMetadataHelper.SyncBlake3Metadata(outputPath);

            return new GlbPlayerColorResult
            {
                Success = true,
                OutputFilePath = outputPath,
                MaskedFaceCount = maskedFaces,
                TotalFaceCount = totalFaces
            };
        }
        catch (Exception ex)
        {
            return new GlbPlayerColorResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static (bool Success, byte[]? OutputBytes, string? ErrorMessage, int MaskedFaces, int TotalFaces) ProcessBytes(
        byte[] glbBytes,
        GlbPlayerColorOptions options)
    {
        var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);

        if (jsonNode is not JsonObject root || binChunk == null)
        {
            return (false, null, "Failed to parse GLB: missing JSON or BIN chunk.", 0, 0);
        }

        var meshes = root["meshes"] as JsonArray;
        var accessors = root["accessors"] as JsonArray;
        var bufferViews = root["bufferViews"] as JsonArray;
        var materials = root["materials"] as JsonArray;
        var images = root["images"] as JsonArray;
        var textures = root["textures"] as JsonArray;

        if (meshes == null || accessors == null || bufferViews == null)
        {
            return (false, null, "GLB lacks required mesh/accessor/bufferView data.", 0, 0);
        }

        if (images == null || textures == null || materials == null)
        {
            return (false, null, "GLB lacks required image/texture/material data.", 0, 0);
        }

        int albedoImageIndex = FindAlbedoImageIndex(textures, materials);
        if (albedoImageIndex < 0)
        {
            return (false, null, "No albedo/base-color texture found in the GLB.", 0, 0);
        }

        int ormImageIndex = FindOrmImageIndex(textures, materials);

        (float targetR, float targetG, float targetB) = HexToRgb(options.TargetHex);
        (float targetCb, float targetCr) = HexToBt601CbCr(options.TargetHex);

        byte[] albedoRaw = ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
        if (albedoRaw.Length == 0)
        {
            return (false, null, "Albedo image data could not be extracted from GLB.", 0, 0);
        }

        byte[] ormRaw = ormImageIndex >= 0
            ? ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk)
            : Array.Empty<byte>();

        using var albedoImg = Image.Load<Rgba32>(albedoRaw);
        int texW = albedoImg.Width;
        int texH = albedoImg.Height;

        using var ormImg = ormRaw.Length > 0
            ? Image.Load<Rgba32>(ormRaw)
            : CreateDefaultOrm(texW, texH);

        var coreMask = new bool[texW * texH];
        var fringeMask = new bool[texW * texH];
        var globalMask = new float[texW * texH];

        ScoreTexelsByTarget(albedoImg, targetR, targetG, targetB, targetCb, targetCr, options.CoreThreshold, options.FringeThreshold, coreMask, fringeMask);

        int totalFaces = 0;
        int maskedFaces = 0;

        foreach (var mesh in meshes)
        {
            if (mesh is not JsonObject meshObj) continue;
            if (meshObj["primitives"] is not JsonArray primitives) continue;

            foreach (var prim in primitives)
            {
                if (prim is not JsonObject primObj) continue;

                var faceUvCoords = ExtractFaceUvData(primObj, accessors, bufferViews, binChunk);
                if (faceUvCoords.Count == 0) continue;

                totalFaces += faceUvCoords.Count;

                var facePositions = ExtractFacePositionData(primObj, accessors, bufferViews, binChunk);
                var candidateFaces = ScoreFaces(faceUvCoords, coreMask, texW, texH);
                var confirmedFaces = FilterByTopology(candidateFaces, facePositions, options.MinClusterFaces);

                maskedFaces += confirmedFaces.Count;

                RasterizeMaskFromFaces(confirmedFaces, faceUvCoords, coreMask, fringeMask, texW, texH, globalMask, options.DilationRadius);
            }
        }

        ApplyMaskToOrm(ormImg, globalMask, texW, texH);
        DesaturateAlbedoUnderMask(albedoImg, globalMask);

        using var newBinStream = new MemoryStream();
        var newBufferViewsList = CloneNonImageBufferViews(bufferViews, binChunk, newBinStream);

        byte[] newAlbedoBytes = EncodeImagePng(albedoImg);
        int newAlbedoBvIndex = AppendToBin(newAlbedoBytes, newBinStream);
        newBufferViewsList.Add(new JsonObject
        {
            ["byteOffset"] = newAlbedoBvIndex,
            ["byteLength"] = newAlbedoBytes.Length,
            ["buffer"] = 0
        });
        int albedoBvIdx = newBufferViewsList.Count - 1;

        byte[] newOrmBytes = EncodeImagePng(ormImg);
        int newOrmBvOffset = AppendToBin(newOrmBytes, newBinStream);
        newBufferViewsList.Add(new JsonObject
        {
            ["byteOffset"] = newOrmBvOffset,
            ["byteLength"] = newOrmBytes.Length,
            ["buffer"] = 0
        });
        int ormBvIdx = newBufferViewsList.Count - 1;

        PatchImagesAndMaterials(root, images, textures, materials,
            albedoImageIndex, albedoBvIdx, ormImageIndex, ormBvIdx);

        RebuildBufferViewsInJson(root, newBufferViewsList);

        if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
        {
            buf0["byteLength"] = (int)newBinStream.Position;
        }

        byte[] newBin = newBinStream.ToArray();
        byte[] outputBytes = GlbManifestUtils.BuildGlb(root, newBin, glbVersion);

        return (true, outputBytes, null, maskedFaces, totalFaces);
    }

    private static (float R, float G, float B) HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return (r / 255f, g / 255f, b / 255f);
    }

    private static (float Cb, float Cr) HexToBt601CbCr(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        return RgbToBt601CbCr(r, g, b);
    }

    private static (float Cb, float Cr) RgbToBt601CbCr(float r, float g, float b)
    {
        float cb = -0.16874f * r - 0.33126f * g + 0.50000f * b;
        float cr = 0.50000f * r - 0.41869f * g - 0.08131f * b;
        return (cb, cr);
    }

    private static void ScoreTexelsByTarget(
        Image<Rgba32> albedoImg,
        float targetR, float targetG, float targetB,
        float targetCb, float targetCr,
        float coreThreshold, float fringeThreshold,
        bool[] coreMask, bool[] fringeMask)
    {
        float targetMagnitude = MathF.Sqrt(targetCb * targetCb + targetCr * targetCr);
        bool isMagentaTarget = targetR > 0.5f && targetB > 0.5f && targetG < 0.2f;

        albedoImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    var pixel = row[x];
                    float r = pixel.R / 255f;
                    float g = pixel.G / 255f;
                    float bVal = pixel.B / 255f;

                    float lum = 0.299f * r + 0.587f * g + 0.114f * bVal;
                    if (lum < 0.04f) continue;

                    (float cb, float cr) = RgbToBt601CbCr(r, g, bVal);
                    float pixelMagnitude = MathF.Sqrt(cb * cb + cr * cr);
                    if (pixelMagnitude < 0.02f) continue;

                    float dot = (cb * targetCb + cr * targetCr) / (pixelMagnitude * targetMagnitude);
                    float chromaRatio = targetMagnitude > 1e-6f ? pixelMagnitude / targetMagnitude : 0f;

                    int idx = y * accessor.Width + x;

                    if (isMagentaTarget)
                    {
                        float minRb = MathF.Min(r, bVal);
                        float maxRb = MathF.Max(r, bVal);
                        float rbBalance = maxRb > 1e-4f ? minRb / maxRb : 0f;

                        bool isCore = dot >= coreThreshold &&
                                      chromaRatio >= 0.35f &&
                                      lum >= 0.08f &&
                                      (minRb - g) >= 0.25f &&
                                      g <= 0.45f &&
                                      rbBalance >= 0.50f;

                        bool isFringe = dot >= fringeThreshold &&
                                        chromaRatio >= 0.20f &&
                                        lum >= 0.05f &&
                                        (minRb - g) >= 0.15f &&
                                        g <= 0.55f &&
                                        rbBalance >= 0.40f;

                        if (isCore)
                        {
                            coreMask[idx] = true;
                            fringeMask[idx] = true;
                        }
                        else if (isFringe)
                        {
                            fringeMask[idx] = true;
                        }
                    }
                    else
                    {
                        float dr = r - targetR;
                        float dg = g - targetG;
                        float db = bVal - targetB;
                        float rgbDist = MathF.Sqrt(dr * dr + dg * dg + db * db);

                        bool isCore = dot >= coreThreshold &&
                                      chromaRatio >= 0.35f &&
                                      lum >= 0.08f &&
                                      rgbDist <= 0.65f;

                        bool isFringe = dot >= fringeThreshold &&
                                        chromaRatio >= 0.20f &&
                                        lum >= 0.05f &&
                                        rgbDist <= 0.80f;

                        if (isCore)
                        {
                            coreMask[idx] = true;
                            fringeMask[idx] = true;
                        }
                        else if (isFringe)
                        {
                            fringeMask[idx] = true;
                        }
                    }
                }
            }
        });
    }


    private static List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> ExtractFaceUvData(
        JsonObject primObj,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] bin)
    {
        var result = new List<(Vector2, Vector2, Vector2)>();

        int uvAccessorIndex = GetAttributeIndex(primObj, "TEXCOORD_0");
        if (uvAccessorIndex < 0 || uvAccessorIndex >= accessors.Count) return result;

        var uvData = ReadAccessorVec2(accessors, bufferViews, bin, uvAccessorIndex);
        if (uvData.Count == 0) return result;

        int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

        if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
        {
            var indices = ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                if (i0 < uvData.Count && i1 < uvData.Count && i2 < uvData.Count)
                {
                    result.Add((uvData[i0], uvData[i1], uvData[i2]));
                }
            }
        }
        else
        {
            for (int i = 0; i + 2 < uvData.Count; i += 3)
            {
                result.Add((uvData[i], uvData[i + 1], uvData[i + 2]));
            }
        }

        return result;
    }

    private static List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> ExtractFacePositionData(
        JsonObject primObj,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] bin)
    {
        var result = new List<(Vector3, Vector3, Vector3)>();

        int posAccessorIndex = GetAttributeIndex(primObj, "POSITION");
        if (posAccessorIndex < 0 || posAccessorIndex >= accessors.Count) return result;

        var posData = ReadAccessorVec3(accessors, bufferViews, bin, posAccessorIndex);
        if (posData.Count == 0) return result;

        int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

        if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
        {
            var indices = ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                if (i0 < posData.Count && i1 < posData.Count && i2 < posData.Count)
                {
                    result.Add((posData[i0], posData[i1], posData[i2]));
                }
            }
        }
        else
        {
            for (int i = 0; i + 2 < posData.Count; i += 3)
            {
                result.Add((posData[i], posData[i + 1], posData[i + 2]));
            }
        }

        return result;
    }

    private static int GetAttributeIndex(JsonObject primObj, string attributeName)
    {
        if (primObj["attributes"] is not JsonObject attributes) return -1;
        if (!attributes.TryGetPropertyValue(attributeName, out var val)) return -1;
        return val?.GetValue<int>() ?? -1;
    }

    private static HashSet<int> ScoreFaces(
        List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> faceUvCoords,
        bool[] coreMask,
        int texW,
        int texH)
    {
        var candidates = new HashSet<int>();

        for (int faceIdx = 0; faceIdx < faceUvCoords.Count; faceIdx++)
        {
            var (uv0, uv1, uv2) = faceUvCoords[faceIdx];

            int minX = Math.Clamp((int)(Math.Min(Math.Min(uv0.X, uv1.X), uv2.X) * texW), 0, texW - 1);
            int maxX = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.X, uv1.X), uv2.X) * texW)), 0, texW - 1);
            int minY = Math.Clamp((int)(Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y) * texH), 0, texH - 1);
            int maxY = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y) * texH)), 0, texH - 1);

            int coreHits = 0;
            int totalSampled = 0;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    var p = new Vector2(px / (float)texW, py / (float)texH);
                    if (!PointInTriangle(p, uv0, uv1, uv2)) continue;
                    totalSampled++;
                    if (coreMask[py * texW + px]) coreHits++;
                }
            }

            if (totalSampled > 0)
            {
                if ((float)coreHits / totalSampled >= 0.20f)
                {
                    candidates.Add(faceIdx);
                }
            }
            else
            {
                Vector2 centroid = (uv0 + uv1 + uv2) / 3f;
                int cx = Math.Clamp((int)(centroid.X * texW), 0, texW - 1);
                int cy = Math.Clamp((int)(centroid.Y * texH), 0, texH - 1);
                if (coreMask[cy * texW + cx])
                {
                    candidates.Add(faceIdx);
                }
            }
        }

        return candidates;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
        float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
        float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);

        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;

        return !(hasNeg && hasPos);
    }

    private static HashSet<int> FilterByTopology(
        HashSet<int> candidateFaces,
        List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> facePositions,
        int minClusterFaces)
    {
        if (facePositions.Count == 0 || candidateFaces.Count == 0) return candidateFaces;

        var positionToFaces = new Dictionary<long, List<int>>();
        for (int faceIdx = 0; faceIdx < facePositions.Count; faceIdx++)
        {
            var (p0, p1, p2) = facePositions[faceIdx];
            foreach (var pos in new[] { p0, p1, p2 })
            {
                long key = QuantizePosition(pos);
                if (!positionToFaces.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    positionToFaces[key] = list;
                }
                list.Add(faceIdx);
            }
        }

        var adjacency = BuildFaceAdjacency(facePositions.Count, positionToFaces);

        var visited = new bool[facePositions.Count];
        var confirmedFaces = new HashSet<int>();

        foreach (int startFace in candidateFaces)
        {
            if (visited[startFace]) continue;

            var cluster = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startFace);
            visited[startFace] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (candidateFaces.Contains(current))
                {
                    cluster.Add(current);
                }

                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (visited[neighbor] || !candidateFaces.Contains(neighbor)) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            if (cluster.Count >= minClusterFaces)
            {
                foreach (int faceIdx in cluster)
                {
                    confirmedFaces.Add(faceIdx);
                }
            }
        }

        return confirmedFaces;
    }

    private static Dictionary<int, HashSet<int>> BuildFaceAdjacency(
        int faceCount,
        Dictionary<long, List<int>> positionToFaces)
    {
        var adjacency = new Dictionary<int, HashSet<int>>(faceCount);
        for (int i = 0; i < faceCount; i++) adjacency[i] = new HashSet<int>();

        foreach (var kvp in positionToFaces)
        {
            var facesAtPos = kvp.Value;
            for (int i = 0; i < facesAtPos.Count; i++)
            {
                for (int j = i + 1; j < facesAtPos.Count; j++)
                {
                    adjacency[facesAtPos[i]].Add(facesAtPos[j]);
                    adjacency[facesAtPos[j]].Add(facesAtPos[i]);
                }
            }
        }

        return adjacency;
    }

    private static long QuantizePosition(Vector3 pos)
    {
        const float precision = 10000f;
        long x = (long)(pos.X * precision + 0.5f) & 0xFFFFF;
        long y = (long)(pos.Y * precision + 0.5f) & 0xFFFFF;
        long z = (long)(pos.Z * precision + 0.5f) & 0xFFFFF;
        return x | (y << 20) | (z << 40);
    }

    private static void RasterizeMaskFromFaces(
        HashSet<int> confirmedFaces,
        List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> faceUvCoords,
        bool[] coreMask,
        bool[] fringeMask,
        int texW,
        int texH,
        float[] globalMask,
        int dilationRadius)
    {
        var localFaceMask = new float[texW * texH];

        foreach (int faceIdx in confirmedFaces)
        {
            var (uv0, uv1, uv2) = faceUvCoords[faceIdx];

            int minX = Math.Clamp((int)(Math.Min(Math.Min(uv0.X, uv1.X), uv2.X) * texW), 0, texW - 1);
            int maxX = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.X, uv1.X), uv2.X) * texW)), 0, texW - 1);
            int minY = Math.Clamp((int)(Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y) * texH), 0, texH - 1);
            int maxY = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y) * texH)), 0, texH - 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    var p = new Vector2(px / (float)texW, py / (float)texH);
                    if (!PointInTriangle(p, uv0, uv1, uv2)) continue;

                    int idx = py * texW + px;
                    if (coreMask[idx])
                    {
                        localFaceMask[idx] = 1.0f;
                    }
                    else if (fringeMask[idx])
                    {
                        localFaceMask[idx] = Math.Max(localFaceMask[idx], 0.7f);
                    }
                }
            }
        }

        var dilatedMask = DilateFloat(localFaceMask, texW, texH, dilationRadius);

        for (int i = 0; i < dilatedMask.Length; i++)
        {
            if (dilatedMask[i] > 0f)
            {
                globalMask[i] = Math.Max(globalMask[i], dilatedMask[i]);
            }
        }
    }

    private static void ApplyMaskToOrm(Image<Rgba32> ormImg, float[] globalMask, int texW, int texH)
    {
        int ormW = ormImg.Width;
        int ormH = ormImg.Height;

        ormImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    int srcX = Math.Clamp((int)((x / (float)ormW) * texW), 0, texW - 1);
                    int srcY = Math.Clamp((int)((y / (float)ormH) * texH), 0, texH - 1);
                    float maskValue = globalMask[srcY * texW + srcX];
                    byte maskByte = (byte)Math.Clamp((int)(maskValue * 255f + 0.5f), 0, 255);
                    var pixel = row[x];
                    row[x] = new Rgba32(maskByte, pixel.G, pixel.B, pixel.A);
                }
            }
        });
    }

    private static float[] DilateFloat(float[] mask, int texW, int texH, int radius)
    {
        var dilated = new float[mask.Length];
        Array.Copy(mask, dilated, mask.Length);

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                float sourceValue = mask[y * texW + x];
                if (sourceValue <= 0f) continue;

                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= texW || ny < 0 || ny >= texH) continue;
                        dilated[ny * texW + nx] = Math.Max(dilated[ny * texW + nx], sourceValue);
                    }
                }
            }
        }

        return dilated;
    }

    private static void DesaturateAlbedoUnderMask(Image<Rgba32> albedoImg, float[] globalMask)
    {
        albedoImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    int idx = y * accessor.Width + x;
                    float maskValue = globalMask[idx];
                    if (maskValue <= 0.01f) continue;

                    var pixel = row[x];
                    float gray = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
                    byte newR = (byte)Math.Clamp((int)((1f - maskValue) * pixel.R + maskValue * gray + 0.5f), 0, 255);
                    byte newG = (byte)Math.Clamp((int)((1f - maskValue) * pixel.G + maskValue * gray + 0.5f), 0, 255);
                    byte newB = (byte)Math.Clamp((int)((1f - maskValue) * pixel.B + maskValue * gray + 0.5f), 0, 255);
                    row[x] = new Rgba32(newR, newG, newB, pixel.A);
                }
            }
        });
    }


    private static int FindAlbedoImageIndex(JsonArray textures, JsonArray materials)
    {
        foreach (var mat in materials)
        {
            if (mat is not JsonObject matObj) continue;
            if (matObj["pbrMetallicRoughness"] is not JsonObject pbr) continue;

            int texIdx = GetTextureRefIndex(pbr, "baseColorTexture");
            int imgIdx = ResolveTextureToImage(texIdx, textures);
            if (imgIdx >= 0) return imgIdx;
        }

        return -1;
    }

    private static int FindOrmImageIndex(JsonArray textures, JsonArray materials)
    {
        foreach (var mat in materials)
        {
            if (mat is not JsonObject matObj) continue;

            if (matObj["pbrMetallicRoughness"] is JsonObject pbr)
            {
                int texIdx = GetTextureRefIndex(pbr, "metallicRoughnessTexture");
                int imgIdx = ResolveTextureToImage(texIdx, textures);
                if (imgIdx >= 0) return imgIdx;
            }

            {
                int texIdx = GetTextureRefIndex(matObj, "occlusionTexture");
                int imgIdx = ResolveTextureToImage(texIdx, textures);
                if (imgIdx >= 0) return imgIdx;
            }
        }

        return -1;
    }

    private static int GetTextureRefIndex(JsonObject container, string propertyName)
    {
        if (!container.TryGetPropertyValue(propertyName, out var texVal)) return -1;
        if (texVal is not JsonObject texObj) return -1;
        return texObj["index"]?.GetValue<int>() ?? -1;
    }

    private static int ResolveTextureToImage(int textureIndex, JsonArray textures)
    {
        if (textureIndex < 0 || textureIndex >= textures.Count) return -1;
        if (textures[textureIndex] is not JsonObject texObj) return -1;

        if (texObj["extensions"] is JsonObject texExt)
        {
            if (texExt["EXT_texture_webp"] is JsonObject webpObj)
            {
                int src = webpObj["source"]?.GetValue<int>() ?? -1;
                if (src >= 0) return src;
            }
            if (texExt["KHR_texture_basisu"] is JsonObject basisObj)
            {
                int src = basisObj["source"]?.GetValue<int>() ?? -1;
                if (src >= 0) return src;
            }
        }

        return texObj["source"]?.GetValue<int>() ?? -1;
    }

    private static byte[] ExtractImageBytes(int imageIndex, JsonArray images, JsonArray bufferViews, byte[] bin)
    {
        if (imageIndex < 0 || imageIndex >= images.Count) return Array.Empty<byte>();
        if (images[imageIndex] is not JsonObject imgObj) return Array.Empty<byte>();

        int bvIdx = imgObj["bufferView"]?.GetValue<int>() ?? -1;
        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return Array.Empty<byte>();
        if (bufferViews[bvIdx] is not JsonObject bv) return Array.Empty<byte>();

        int byteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int byteLength = bv["byteLength"]?.GetValue<int>() ?? 0;
        if (byteOffset + byteLength > bin.Length) return Array.Empty<byte>();

        byte[] result = new byte[byteLength];
        Array.Copy(bin, byteOffset, result, 0, byteLength);
        return result;
    }

    private static Image<Rgba32> CreateDefaultOrm(int width, int height)
    {
        var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    row[x] = new Rgba32(0, 255, 0, 255);
                }
            }
        });
        return img;
    }

    private static List<JsonObject> CloneNonImageBufferViews(JsonArray bufferViews, byte[] bin, MemoryStream newBinStream)
    {
        var result = new List<JsonObject>();

        for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
        {
            if (bufferViews[bvIdx] is not JsonObject bv)
            {
                result.Add(new JsonObject());
                continue;
            }

            int origOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
            int origLength = bv["byteLength"]?.GetValue<int>() ?? 0;
            int newOffset = (int)newBinStream.Position;

            var newBv = (JsonObject)bv.DeepClone();
            newBv["byteOffset"] = newOffset;
            result.Add(newBv);

            if (origOffset + origLength <= bin.Length && origLength > 0)
            {
                newBinStream.Write(bin, origOffset, origLength);
                int pad = (4 - (origLength % 4)) % 4;
                for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);
            }
        }

        return result;
    }

    private static int AppendToBin(byte[] data, MemoryStream binStream)
    {
        int offset = (int)binStream.Position;
        binStream.Write(data, 0, data.Length);
        int pad = (4 - (data.Length % 4)) % 4;
        for (int p = 0; p < pad; p++) binStream.WriteByte(0);
        return offset;
    }

    private static void PatchImagesAndMaterials(
        JsonObject root,
        JsonArray images,
        JsonArray textures,
        JsonArray materials,
        int albedoImageIndex,
        int newAlbedoBvIdx,
        int ormImageIndex,
        int newOrmBvIdx)
    {
        int totalExistingBvCount = (root["bufferViews"] as JsonArray)?.Count ?? 0;

        if (albedoImageIndex >= 0 && albedoImageIndex < images.Count && images[albedoImageIndex] is JsonObject albedoImgObj)
        {
            int oldBvIdx = albedoImgObj["bufferView"]?.GetValue<int>() ?? -1;
            albedoImgObj["bufferView"] = totalExistingBvCount;
            albedoImgObj["mimeType"] = "image/png";
            if (albedoImgObj.ContainsKey("extensions")) albedoImgObj.Remove("extensions");
        }

        if (ormImageIndex >= 0 && ormImageIndex < images.Count && images[ormImageIndex] is JsonObject ormImgObj)
        {
            ormImgObj["bufferView"] = totalExistingBvCount + 1;
            ormImgObj["mimeType"] = "image/png";
            if (ormImgObj.ContainsKey("extensions")) ormImgObj.Remove("extensions");
        }
        else
        {
            int newOrmImageIdx = images.Count;
            images.Add(new JsonObject
            {
                ["mimeType"] = "image/png",
                ["bufferView"] = totalExistingBvCount + 1
            });

            int newOrmTextureIdx = textures.Count;
            textures.Add(new JsonObject { ["source"] = newOrmImageIdx });

            if (materials.Count > 0 && materials[0] is JsonObject firstMat)
            {
                if (firstMat["pbrMetallicRoughness"] is not JsonObject pbr)
                {
                    pbr = new JsonObject();
                    firstMat["pbrMetallicRoughness"] = pbr;
                }

                if (!pbr.ContainsKey("metallicRoughnessTexture"))
                {
                    pbr["metallicRoughnessTexture"] = new JsonObject { ["index"] = newOrmTextureIdx };
                }
            }
        }

        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] is not JsonObject texObj) continue;
            if (!texObj.ContainsKey("source") || texObj["source"] == null)
            {
                int src = ResolveTextureToImage(i, textures);
                if (src >= 0 && src < images.Count)
                {
                    texObj["source"] = src;
                }
                else if (images.Count > 0)
                {
                    texObj["source"] = 0;
                }
            }
            if (texObj.ContainsKey("extensions"))
            {
                texObj.Remove("extensions");
            }
        }
    }

    private static void RebuildBufferViewsInJson(JsonObject root, List<JsonObject> bufferViews)
    {
        var newBvArray = new JsonArray();
        foreach (var bv in bufferViews) newBvArray.Add((JsonObject)bv.DeepClone());
        root["bufferViews"] = newBvArray;
    }

    private static byte[] EncodeImagePng(Image<Rgba32> img)
    {
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static List<Vector2> ReadAccessorVec2(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
    {
        var result = new List<Vector2>();
        if (accessorIndex < 0 || accessorIndex >= accessors.Count) return result;
        if (accessors[accessorIndex] is not JsonObject acc) return result;

        int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
        int count = acc["count"]?.GetValue<int>() ?? 0;
        int accessorByteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;
        int componentType = acc["componentType"]?.GetValue<int>() ?? 5126;

        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return result;
        if (bufferViews[bvIdx] is not JsonObject bv) return result;

        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int bvStride = bv["byteStride"]?.GetValue<int>() ?? 0;

        int elementSize = componentType == 5126 ? 8 : 4;
        int stride = bvStride > 0 ? bvStride : elementSize;
        int baseOffset = bvByteOffset + accessorByteOffset;

        for (int i = 0; i < count; i++)
        {
            int offset = baseOffset + i * stride;
            if (offset + elementSize > bin.Length) break;

            float u, v;
            if (componentType == 5126)
            {
                u = BitConverter.ToSingle(bin, offset);
                v = BitConverter.ToSingle(bin, offset + 4);
            }
            else
            {
                u = BitConverter.ToUInt16(bin, offset) / 65535f;
                v = BitConverter.ToUInt16(bin, offset + 2) / 65535f;
            }

            result.Add(new Vector2(Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f)));
        }

        return result;
    }

    private static List<Vector3> ReadAccessorVec3(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
    {
        var result = new List<Vector3>();
        if (accessorIndex < 0 || accessorIndex >= accessors.Count) return result;
        if (accessors[accessorIndex] is not JsonObject acc) return result;

        int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
        int count = acc["count"]?.GetValue<int>() ?? 0;
        int accessorByteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;

        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return result;
        if (bufferViews[bvIdx] is not JsonObject bv) return result;

        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int bvStride = bv["byteStride"]?.GetValue<int>() ?? 0;
        int stride = bvStride > 0 ? bvStride : 12;
        int baseOffset = bvByteOffset + accessorByteOffset;

        for (int i = 0; i < count; i++)
        {
            int offset = baseOffset + i * stride;
            if (offset + 12 > bin.Length) break;

            float x = BitConverter.ToSingle(bin, offset);
            float y = BitConverter.ToSingle(bin, offset + 4);
            float z = BitConverter.ToSingle(bin, offset + 8);
            result.Add(new Vector3(x, y, z));
        }

        return result;
    }

    private static List<int> ReadAccessorIndices(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
    {
        var result = new List<int>();
        if (accessorIndex < 0 || accessorIndex >= accessors.Count) return result;
        if (accessors[accessorIndex] is not JsonObject acc) return result;

        int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
        int count = acc["count"]?.GetValue<int>() ?? 0;
        int accessorByteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;
        int componentType = acc["componentType"]?.GetValue<int>() ?? 5125;

        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return result;
        if (bufferViews[bvIdx] is not JsonObject bv) return result;

        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int baseOffset = bvByteOffset + accessorByteOffset;

        int elementSize = componentType switch
        {
            5121 => 1,
            5123 => 2,
            _ => 4
        };

        for (int i = 0; i < count; i++)
        {
            int offset = baseOffset + i * elementSize;
            if (offset + elementSize > bin.Length) break;

            int index = componentType switch
            {
                5121 => bin[offset],
                5123 => BitConverter.ToUInt16(bin, offset),
                _ => (int)BitConverter.ToUInt32(bin, offset)
            };

            result.Add(index);
        }

        return result;
    }
}
