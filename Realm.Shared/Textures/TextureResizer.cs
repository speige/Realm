using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Realm.Shared.Textures;

public enum TextureDataType
{
	Albedo,
	NormalMap,
	Data
}

public static class TextureResizer
{
	public static bool IsPowerOfTwo(int n)
	{
		return n > 0 && (n & (n - 1)) == 0;
	}

	public static int CalculateTargetPowerOfTwo(int size, int maxResolution = 4096)
	{
		if (size <= 0) return 1;
		if (size >= maxResolution) return maxResolution;

		if (IsPowerOfTwo(size))
		{
			return size;
		}

		int p = 1;
		while (p < size && p < maxResolution)
		{
			p <<= 1;
		}

		int lower = p >> 1;
		if (lower > 0 && (size - lower) < (p - size))
		{
			return lower;
		}

		return Math.Min(maxResolution, p);
	}

	public static void ResizeImage(
		Image<Rgba32> image,
		int targetWidth,
		int targetHeight,
		TextureDataType dataType = TextureDataType.Albedo)
	{
		if (image.Width == targetWidth && image.Height == targetHeight)
		{
			if (dataType == TextureDataType.NormalMap)
			{
				RenormalizeNormalMap(image);
			}
			return;
		}

		var resampler = dataType switch
		{
			TextureDataType.NormalMap => KnownResamplers.Lanczos3,
			TextureDataType.Data => KnownResamplers.Bicubic,
			_ => KnownResamplers.Lanczos3
		};

		image.Mutate(ctx => ctx.Resize(new ResizeOptions
		{
			Size = new Size(targetWidth, targetHeight),
			Sampler = resampler,
			Mode = ResizeMode.Stretch
		}));

		if (dataType == TextureDataType.NormalMap)
		{
			RenormalizeNormalMap(image);
		}
	}

	public static void RenormalizeNormalMap(Image<Rgba32> image)
	{
		int width = image.Width;
		int height = image.Height;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = image[x, y];
				float nx = (pixel.R / 255.0f) * 2.0f - 1.0f;
				float ny = (pixel.G / 255.0f) * 2.0f - 1.0f;
				float nz = (pixel.B / 255.0f) * 2.0f - 1.0f;

				float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
				if (len > 1e-5f)
				{
					float invLen = 1.0f / len;
					nx *= invLen;
					ny *= invLen;
					nz *= invLen;
				}
				else
				{
					nx = 0.0f;
					ny = 0.0f;
					nz = 1.0f;
				}

				byte r = (byte)Math.Clamp((int)Math.Round((nx * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round((ny * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round((nz * 0.5f + 0.5f) * 255.0f), 0, 255);

				image[x, y] = new Rgba32(r, g, b, pixel.A);
			}
		}
	}
}
