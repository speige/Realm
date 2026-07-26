using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct TerrainSplatWeights : IEquatable<TerrainSplatWeights>
{
    public int Index0;
    public int Index1;
    public int Index2;
    public int Index3;

    public float Weight0;
    public float Weight1;
    public float Weight2;
    public float Weight3;

    public static TerrainSplatWeights CreateSolid(int textureIndex)
    {
        return new TerrainSplatWeights
        {
            Index0 = textureIndex,
            Index1 = textureIndex,
            Index2 = textureIndex,
            Index3 = textureIndex,
            Weight0 = 1.0f,
            Weight1 = 0.0f,
            Weight2 = 0.0f,
            Weight3 = 0.0f
        };
    }

    public static TerrainSplatWeights PaintVertex(TerrainSplatWeights current, int targetTextureIndex, float brushIntensity)
    {
        float I = Math.Max(0.0f, Math.Min(1.0f, brushIntensity));
        if (I <= 0.0001f) return current;

        int slotForTarget = -1;
        if (current.Index0 == targetTextureIndex) slotForTarget = 0;
        else if (current.Index1 == targetTextureIndex) slotForTarget = 1;
        else if (current.Index2 == targetTextureIndex) slotForTarget = 2;
        else if (current.Index3 == targetTextureIndex) slotForTarget = 3;

        if (slotForTarget < 0)
        {
            if (current.Weight0 <= 0.001f) { current.Index0 = targetTextureIndex; current.Weight0 = 0.0f; slotForTarget = 0; }
            else if (current.Weight1 <= 0.001f) { current.Index1 = targetTextureIndex; current.Weight1 = 0.0f; slotForTarget = 1; }
            else if (current.Weight2 <= 0.001f) { current.Index2 = targetTextureIndex; current.Weight2 = 0.0f; slotForTarget = 2; }
            else if (current.Weight3 <= 0.001f) { current.Index3 = targetTextureIndex; current.Weight3 = 0.0f; slotForTarget = 3; }
            else
            {
                int lowestSlot = 0;
                float lowestW = current.Weight0;
                if (current.Weight1 < lowestW) { lowestW = current.Weight1; lowestSlot = 1; }
                if (current.Weight2 < lowestW) { lowestW = current.Weight2; lowestSlot = 2; }
                if (current.Weight3 < lowestW) { lowestW = current.Weight3; lowestSlot = 3; }

                if (I > lowestW)
                {
                    switch (lowestSlot)
                    {
                        case 0: current.Index0 = targetTextureIndex; current.Weight0 = 0.0f; break;
                        case 1: current.Index1 = targetTextureIndex; current.Weight1 = 0.0f; break;
                        case 2: current.Index2 = targetTextureIndex; current.Weight2 = 0.0f; break;
                        case 3: current.Index3 = targetTextureIndex; current.Weight3 = 0.0f; break;
                    }
                    slotForTarget = lowestSlot;
                }
                else
                {
                    return current;
                }
            }
        }

        float w0 = current.Weight0 * (slotForTarget == 0 ? 1.0f : (1.0f - I));
        float w1 = current.Weight1 * (slotForTarget == 1 ? 1.0f : (1.0f - I));
        float w2 = current.Weight2 * (slotForTarget == 2 ? 1.0f : (1.0f - I));
        float w3 = current.Weight3 * (slotForTarget == 3 ? 1.0f : (1.0f - I));

        if (slotForTarget == 0) w0 += I * (1.0f - current.Weight0);
        if (slotForTarget == 1) w1 += I * (1.0f - current.Weight1);
        if (slotForTarget == 2) w2 += I * (1.0f - current.Weight2);
        if (slotForTarget == 3) w3 += I * (1.0f - current.Weight3);

        float sum = w0 + w1 + w2 + w3;
        if (sum > 0.0001f)
        {
            w0 /= sum;
            w1 /= sum;
            w2 /= sum;
            w3 /= sum;
        }
        else
        {
            w0 = 1.0f;
            w1 = 0.0f;
            w2 = 0.0f;
            w3 = 0.0f;
        }

        current.Weight0 = w0;
        current.Weight1 = w1;
        current.Weight2 = w2;
        current.Weight3 = w3;
        return current;
    }

    public bool Equals(TerrainSplatWeights other)
    {
        return Index0 == other.Index0 && Index1 == other.Index1 &&
               Index2 == other.Index2 && Index3 == other.Index3 &&
               Weight0 == other.Weight0 && Weight1 == other.Weight1 &&
               Weight2 == other.Weight2 && Weight3 == other.Weight3;
    }

    public override bool Equals(object? obj) => obj is TerrainSplatWeights other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Index0, Index1, Index2, Index3);
    public static bool operator ==(TerrainSplatWeights a, TerrainSplatWeights b) => a.Equals(b);
    public static bool operator !=(TerrainSplatWeights a, TerrainSplatWeights b) => !a.Equals(b);

    public string Serialize()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4}", Index0, Index1, Index2, Index3, Weight0, Weight1, Weight2, Weight3);
    }

    public static TerrainSplatWeights Deserialize(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return CreateSolid(3);

        int idx0 = 0, idx1 = 0, idx2 = 0, idx3 = 0;
        float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;

        int fieldIndex = 0;
        int start = 0;
        for (int i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == ',')
            {
                ReadOnlySpan<char> field = span.Slice(start, i - start);
                start = i + 1;

                switch (fieldIndex)
                {
                    case 0: int.TryParse(field, out idx0); break;
                    case 1: int.TryParse(field, out idx1); break;
                    case 2: int.TryParse(field, out idx2); break;
                    case 3: int.TryParse(field, out idx3); break;
                    case 4: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w0); break;
                    case 5: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w1); break;
                    case 6: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w2); break;
                    case 7: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w3); break;
                }
                fieldIndex++;
            }
        }

        if (fieldIndex < 8) return CreateSolid(3);

        return new TerrainSplatWeights
        {
            Index0 = idx0,
            Index1 = idx1,
            Index2 = idx2,
            Index3 = idx3,
            Weight0 = w0,
            Weight1 = w1,
            Weight2 = w2,
            Weight3 = w3
        };
    }

    public static TerrainSplatWeights Deserialize(string? serialized)
    {
        if (string.IsNullOrEmpty(serialized)) return CreateSolid(3);
        return Deserialize(serialized.AsSpan());
    }
}
