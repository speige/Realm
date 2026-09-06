using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Realm.Shared.Textures;

public static class SkyboxProcessor
{
	public static Rgba32? ParseColor(string? colorString)
	{
		if (string.IsNullOrWhiteSpace(colorString) || colorString.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string trimmed = colorString.Trim();
		if (trimmed.StartsWith('#'))
		{
			string hex = trimmed.TrimStart('#');
			if (hex.Length >= 6 &&
				byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
				byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
				byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
			{
				return new Rgba32(r, g, b, 255);
			}
		}
		else if (trimmed.Contains(','))
		{
			string[] parts = trimmed.Split(',');
			if (parts.Length >= 3 &&
				byte.TryParse(parts[0].Trim(), out byte r) &&
				byte.TryParse(parts[1].Trim(), out byte g) &&
				byte.TryParse(parts[2].Trim(), out byte b))
			{
				return new Rgba32(r, g, b, 255);
			}
		}

		return null;
	}

	public static Image<Rgba32> ProcessSkybox(
		Image<Rgba32> sourceImage,
		float horizonBlendStart = 0.5f,
		Rgba32? horizonColor = null,
		float wrapBlendWidth = 0.05f,
		float zenithBlendEnd = 0.08f,
		Rgba32? zenithColor = null)
	{
		ArgumentNullException.ThrowIfNull(sourceImage);

		int width = sourceImage.Width;
		int height = sourceImage.Height;

		if (width <= 0 || height <= 0)
		{
			return sourceImage.Clone();
		}

		Image<Rgba32> workingImage = sourceImage.Clone();

		int horizonY = Math.Clamp((int)(height * horizonBlendStart), 0, height);
		float horizonColorR;
		float horizonColorG;
		float horizonColorB;

		if (horizonColor == null)
		{
			int sampleY = Math.Clamp(horizonY - (int)(height * 0.05f), 0, height - 1);
			float sumR = 0f;
			float sumG = 0f;
			float sumB = 0f;
			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = workingImage[x, sampleY];
				sumR += pixel.R;
				sumG += pixel.G;
				sumB += pixel.B;
			}
			horizonColorR = sumR / width;
			horizonColorG = sumG / width;
			horizonColorB = sumB / width;
		}
		else
		{
			horizonColorR = horizonColor.Value.R;
			horizonColorG = horizonColor.Value.G;
			horizonColorB = horizonColor.Value.B;
		}

		int horizonSpan = height - 1 - horizonY;
		for (int y = horizonY; y < height; y++)
		{
			float t = horizonSpan > 0 ? (y - horizonY) / (float)horizonSpan : 1.0f;
			float tSmooth = 0.5f - 0.5f * MathF.Cos(MathF.PI * t);
			float oneMinusTSmooth = 1.0f - tSmooth;

			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = workingImage[x, y];
				byte r = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.R + tSmooth * horizonColorR), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.G + tSmooth * horizonColorG), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.B + tSmooth * horizonColorB), 0, 255);
				workingImage[x, y] = new Rgba32(r, g, b, 255);
			}
		}

		int zenithYEnd = Math.Clamp((int)(height * zenithBlendEnd), 0, height);
		float zenithColorR;
		float zenithColorG;
		float zenithColorB;

		if (zenithColor == null)
		{
			float sumR = 0f;
			float sumG = 0f;
			float sumB = 0f;
			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = workingImage[x, 0];
				sumR += pixel.R;
				sumG += pixel.G;
				sumB += pixel.B;
			}
			zenithColorR = sumR / width;
			zenithColorG = sumG / width;
			zenithColorB = sumB / width;
		}
		else
		{
			zenithColorR = zenithColor.Value.R;
			zenithColorG = zenithColor.Value.G;
			zenithColorB = zenithColor.Value.B;
		}

		for (int y = 0; y < zenithYEnd; y++)
		{
			float t = zenithYEnd > 0 ? y / (float)zenithYEnd : 0.0f;
			float tSmooth = 0.5f + 0.5f * MathF.Cos(MathF.PI * t);
			float oneMinusTSmooth = 1.0f - tSmooth;

			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = workingImage[x, y];
				byte r = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.R + tSmooth * zenithColorR), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.G + tSmooth * zenithColorG), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.B + tSmooth * zenithColorB), 0, 255);
				workingImage[x, y] = new Rgba32(r, g, b, 255);
			}
		}

		int blendWidth = (int)(width * wrapBlendWidth);
		if (blendWidth <= 0 || blendWidth >= width)
		{
			return workingImage;
		}

		int newWidth = width - blendWidth;
		Image<Rgba32> blendedImage = new Image<Rgba32>(newWidth, height);

		for (int y = 0; y < height; y++)
		{
			for (int x = blendWidth; x < newWidth; x++)
			{
				blendedImage[x, y] = workingImage[x, y];
			}

			for (int x = 0; x < blendWidth; x++)
			{
				float t = blendWidth > 0 ? x / (float)blendWidth : 0.0f;
				float tSmooth = 0.5f - 0.5f * MathF.Cos(MathF.PI * t);
				float oneMinusTSmooth = 1.0f - tSmooth;

				Rgba32 leftVal = workingImage[x, y];
				Rgba32 rightVal = workingImage[newWidth + x, y];

				byte r = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * rightVal.R + tSmooth * leftVal.R), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * rightVal.G + tSmooth * leftVal.G), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * rightVal.B + tSmooth * leftVal.B), 0, 255);
				blendedImage[x, y] = new Rgba32(r, g, b, 255);
			}
		}

		workingImage.Dispose();

		blendedImage.Mutate(ctx => ctx.Resize(width, height, KnownResamplers.Lanczos3));

		return blendedImage;
	}

	public static TextureConversionResult ProcessSkyboxFile(
		string inputPath,
		string outputPath,
		float horizonBlendStart = 0.5f,
		Rgba32? horizonColor = null,
		float wrapBlendWidth = 0.05f,
		float zenithBlendEnd = 0.08f,
		Rgba32? zenithColor = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string fullOutput = Path.GetFullPath(outputPath);

		var result = new TextureConversionResult
		{
			InputPath = fullInput,
			OutputPath = fullOutput
		};

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputPath}";
			return result;
		}

		try
		{
			string ext = Path.GetExtension(fullOutput).ToLowerInvariant();
			if (ext == ".rtex")
			{
				return TextureConverter.ProcessAndSaveSkybox(
					fullInput,
					fullOutput,
					false,
					horizonBlendStart,
					horizonColor,
					wrapBlendWidth,
					zenithBlendEnd,
					zenithColor);
			}

			byte[] inputBytes = File.ReadAllBytes(fullInput);
			using var sourceImage = Image.Load<Rgba32>(inputBytes);
			using var processedImage = ProcessSkybox(
				sourceImage,
				horizonBlendStart,
				horizonColor,
				wrapBlendWidth,
				zenithBlendEnd,
				zenithColor);

			string? dir = Path.GetDirectoryName(fullOutput);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			if (ext == ".png")
			{
				processedImage.SaveAsPng(fullOutput);
			}
			else if (ext == ".webp")
			{
				processedImage.SaveAsWebp(fullOutput);
			}
			else
			{
				processedImage.Save(fullOutput);
			}

			result.Success = true;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}
}
