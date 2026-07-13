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
        float i0 = current.Index0;
        float i1 = current.Index1;
        float i2 = current.Index2;
        float i3 = current.Index3;

        float w0 = current.Weight0;
        float w1 = current.Weight1;
        float w2 = current.Weight2;
        float w3 = current.Weight3;

        int slotForTarget = -1;
        if (current.Index0 == targetTextureIndex) slotForTarget = 0;
        else if (current.Index1 == targetTextureIndex) slotForTarget = 1;
        else if (current.Index2 == targetTextureIndex) slotForTarget = 2;
        else if (current.Index3 == targetTextureIndex) slotForTarget = 3;

        if (slotForTarget < 0)
        {
            int lowestWeightSlot = 0;
            float lowestWeight = w0;
            if (w1 < lowestWeight) { lowestWeight = w1; lowestWeightSlot = 1; }
            if (w2 < lowestWeight) { lowestWeight = w2; lowestWeightSlot = 2; }
            if (w3 < lowestWeight) { lowestWeight = w3; lowestWeightSlot = 3; }

            switch (lowestWeightSlot)
            {
                case 0: i0 = targetTextureIndex; w0 = 0.0f; break;
                case 1: i1 = targetTextureIndex; w1 = 0.0f; break;
                case 2: i2 = targetTextureIndex; w2 = 0.0f; break;
                case 3: i3 = targetTextureIndex; w3 = 0.0f; break;
            }
            slotForTarget = lowestWeightSlot;
        }

        float I = Math.Max(0.0f, Math.Min(1.0f, brushIntensity));

        if (slotForTarget == 0)
        {
            w0 = w0 + I * (1.0f - w0);
            w1 = w1 * (1.0f - I);
            w2 = w2 * (1.0f - I);
            w3 = w3 * (1.0f - I);
        }
        else if (slotForTarget == 1)
        {
            w0 = w0 * (1.0f - I);
            w1 = w1 + I * (1.0f - w1);
            w2 = w2 * (1.0f - I);
            w3 = w3 * (1.0f - I);
        }
        else if (slotForTarget == 2)
        {
            w0 = w0 * (1.0f - I);
            w1 = w1 * (1.0f - I);
            w2 = w2 + I * (1.0f - w2);
            w3 = w3 * (1.0f - I);
        }
        else if (slotForTarget == 3)
        {
            w0 = w0 * (1.0f - I);
            w1 = w1 * (1.0f - I);
            w2 = w2 * (1.0f - I);
            w3 = w3 + I * (1.0f - w3);
        }

        float totalWeight = w0 + w1 + w2 + w3;
        if (totalWeight > 0.0001f)
        {
            w0 /= totalWeight;
            w1 /= totalWeight;
            w2 /= totalWeight;
            w3 /= totalWeight;
        }
        else
        {
            w0 = 1.0f;
            w1 = 0.0f;
            w2 = 0.0f;
            w3 = 0.0f;
        }

        return new TerrainSplatWeights
        {
            Index0 = (int)i0,
            Index1 = (int)i1,
            Index2 = (int)i2,
            Index3 = (int)i3,
            Weight0 = w0,
            Weight1 = w1,
            Weight2 = w2,
            Weight3 = w3
        };
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
        return $"{Index0},{Index1},{Index2},{Index3},{Weight0:F4},{Weight1:F4},{Weight2:F4},{Weight3:F4}";
    }

    public static TerrainSplatWeights Deserialize(string serialized)
    {
        if (string.IsNullOrEmpty(serialized)) return CreateSolid(3);
        var parts = serialized.Split(',');
        if (parts.Length != 8) return CreateSolid(3);
        return new TerrainSplatWeights
        {
            Index0 = int.Parse(parts[0]),
            Index1 = int.Parse(parts[1]),
            Index2 = int.Parse(parts[2]),
            Index3 = int.Parse(parts[3]),
            Weight0 = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
            Weight1 = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
            Weight2 = float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture),
            Weight3 = float.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
