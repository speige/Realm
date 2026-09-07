using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Realm.Shared.Textures;

public class TextureConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public float ScaleFactor { get; set; } = 1.0f;
}

public static class TextureConverter
{
	private static readonly float[] SrgbToLinearLut = InitializeSrgbToLinearLut();

	private static float[] InitializeSrgbToLinearLut()
	{
		float[] lut = new float[256];
		for (int i = 0; i < 256; i++)
		{
			float s = i / 255.0f;
			lut[i] = s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
		}
		return lut;
	}

	private static byte LinearToSrgbByte(float lin)
	{
		if (lin <= 0.0f) return 0;
		if (lin >= 1.0f) return 255;
		float srgb = lin <= 0.0031308f ? lin * 12.92f : 1.055f * MathF.Pow(lin, 1.0f / 2.4f) - 0.055f;
		return (byte)Math.Clamp((int)Math.Round(srgb * 255.0f), 0, 255);
	}

	public static float CalculateLuminanceScaleFactor(
		Image<Rgba32> sourceImage,
		float targetLinearLuminance = 0.1133f,
		float minScaleFactor = 0.2f,
		float maxScaleFactor = 4.0f)
	{
		int width = sourceImage.Width;
		int height = sourceImage.Height;
		double totalReshapedLuminance = 0.0;
		long validPixelCount = 0;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = sourceImage[x, y];
				if (pixel.A < 13 || (pixel.R == 0 && pixel.G == 0 && pixel.B == 0))
				{
					continue;
				}

				float rLinear = SrgbToLinearLut[pixel.R];
				float gLinear = SrgbToLinearLut[pixel.G];
				float bLinear = SrgbToLinearLut[pixel.B];

				float rPow = rLinear * rLinear;
				float gPow = gLinear * gLinear;
				float bPow = bLinear * bLinear;

				float lum = (0.2126f * rPow) + (0.7152f * gPow) + (0.0722f * bPow);
				totalReshapedLuminance += lum;
				validPixelCount++;
			}
		}

		if (validPixelCount == 0)
		{
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Rgba32 pixel = sourceImage[x, y];
					if (pixel.A < 13) continue;

					float rLinear = SrgbToLinearLut[pixel.R];
					float gLinear = SrgbToLinearLut[pixel.G];
					float bLinear = SrgbToLinearLut[pixel.B];

					float rPow = rLinear * rLinear;
					float gPow = gLinear * gLinear;
					float bPow = bLinear * bLinear;

					float lum = (0.2126f * rPow) + (0.7152f * gPow) + (0.0722f * bPow);
					totalReshapedLuminance += lum;
					validPixelCount++;
				}
			}
		}

		if (validPixelCount > 0)
		{
			float avgLuminance = (float)(totalReshapedLuminance / validPixelCount);
			if (avgLuminance > 0.0001f)
			{
				float rawScaleFactor = targetLinearLuminance / avgLuminance;
				return Math.Clamp(rawScaleFactor, minScaleFactor, maxScaleFactor);
			}
		}

		return 1.0f;
	}

	public static float CalculateLuminanceScaleFactor(
		string imagePath,
		float targetLinearLuminance = 0.1133f,
		float minScaleFactor = 0.2f,
		float maxScaleFactor = 4.0f)
	{
		if (!File.Exists(imagePath)) return 1.0f;
		try
		{
			string ext = Path.GetExtension(imagePath).ToLowerInvariant();
			if (ext == ".rtex")
			{
				using var rtexImg = ExtractImageFromRtex(imagePath, layer: 0);
				if (rtexImg != null)
				{
					return CalculateLuminanceScaleFactor(rtexImg, targetLinearLuminance, minScaleFactor, maxScaleFactor);
				}
			}

			using var img = Image.Load<Rgba32>(imagePath);
			return CalculateLuminanceScaleFactor(img, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		}
		catch
		{
			return 1.0f;
		}
	}

	public static Image<Rgba32> NormalizeLuminance(Image<Rgba32> sourceImage, float scaleFactor)
	{
		int width = sourceImage.Width;
		int height = sourceImage.Height;
		var result = new Image<Rgba32>(width, height);

		byte[] scaledLut = new byte[256];
		for (int i = 0; i < 256; i++)
		{
			float lin = SrgbToLinearLut[i] * scaleFactor;
			scaledLut[i] = LinearToSrgbByte(lin);
		}

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Rgba32 pixel = sourceImage[x, y];
				if (pixel.A < 13)
				{
					result[x, y] = pixel;
					continue;
				}

				byte r = scaledLut[pixel.R];
				byte g = scaledLut[pixel.G];
				byte b = scaledLut[pixel.B];

				result[x, y] = new Rgba32(r, g, b, pixel.A);
			}
		}

		return result;
	}

	public static void ProcessTerrainPbr(
		Image<Rgba32> sourceImage,
		bool isDecal,
		out Image<Rgba32> layer0,
		out Image<Rgba32> layer1)
	{
		int width = sourceImage.Width;
		int height = sourceImage.Height;

		float[,] luminance = new float[width, height];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Rgba32 p = sourceImage[x, y];
				luminance[x, y] = (0.299f * p.R + 0.587f * p.G + 0.114f * p.B) / 255.0f;
			}
		}

		float[,] fineMean = ComputeSeparableBoxBlur(luminance, width, height, 3);
		float[,] coarseMean = ComputeSeparableBoxBlur(luminance, width, height, 14);

		float[,] rawHeight = new float[width, height];
		float[] flatHeights = new float[width * height];
		int idx = 0;

		float normalStrength = isDecal ? 2.5f : 2.5f;

		for (int y = 0; y < height; y++)
		{
			int py = y > 0 ? y - 1 : height - 1;
			int ny = y < height - 1 ? y + 1 : 0;

			for (int x = 0; x < width; x++)
			{
				int px = x > 0 ? x - 1 : width - 1;
				int nx = x < width - 1 ? x + 1 : 0;

				float lum = luminance[x, y];
				float highFreq = lum - fineMean[x, y];
				float midFreq = fineMean[x, y] - coarseMean[x, y];

				float dx = (luminance[nx, y] - luminance[px, y]) * 0.5f;
				float dy = (luminance[x, ny] - luminance[x, py]) * 0.5f;
				float gradMag = MathF.Sqrt(dx * dx + dy * dy);
				float laplacian = luminance[nx, y] + luminance[px, y] + luminance[x, ny] + luminance[x, py] - 4.0f * lum;

				float structuralValue = 0.5f + (highFreq * 2.2f) + (midFreq * 1.4f) + (laplacian * 0.5f) - (gradMag * 0.25f);
				rawHeight[x, y] = structuralValue;
				flatHeights[idx++] = structuralValue;
			}
		}

		Array.Sort(flatHeights);
		int totalPixels = flatHeights.Length;
		int p1Index = Math.Clamp((int)(totalPixels * 0.01f), 0, totalPixels - 1);
		int p99Index = Math.Clamp((int)(totalPixels * 0.99f), 0, totalPixels - 1);
		float lowPercentile = flatHeights[p1Index];
		float highPercentile = flatHeights[p99Index];

		float normRange = highPercentile - lowPercentile;
		float invNormRange = normRange > 1e-5f ? 1.0f / normRange : 0.0f;
		float[,] normalizedHeight = new float[width, height];

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float normH = (rawHeight[x, y] - lowPercentile) * invNormRange;
				normalizedHeight[x, y] = Math.Clamp(normH, 0.0f, 1.0f);
			}
		}

		layer0 = new Image<Rgba32>(width, height);
		layer1 = new Image<Rgba32>(width, height);

		for (int y = 0; y < height; y++)
		{
			int py = y > 0 ? y - 1 : height - 1;
			int ny = y < height - 1 ? y + 1 : 0;

			for (int x = 0; x < width; x++)
			{
				int px = x > 0 ? x - 1 : width - 1;
				int nx = x < width - 1 ? x + 1 : 0;

				Rgba32 albedoCol = sourceImage[x, y];
				float heightVal = normalizedHeight[x, y];
				byte heightByte = (byte)Math.Clamp((int)Math.Round(heightVal * 255.0f), 0, 255);
				byte alphaVal = isDecal ? albedoCol.A : (byte)255;

				layer0[x, y] = new Rgba32(albedoCol.R, albedoCol.G, albedoCol.B, alphaVal);

				float dX = (normalizedHeight[nx, y] - normalizedHeight[px, y]) * normalStrength;
				float dY = (normalizedHeight[x, ny] - normalizedHeight[x, py]) * normalStrength;

				float len = MathF.Sqrt(dX * dX + dY * dY + 1.0f);
				float invLen = 1.0f / len;
				float normX = -dX * invLen;
				float normY = -dY * invLen;

				byte normR = (byte)Math.Clamp((int)Math.Round((normX * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte normG = (byte)Math.Clamp((int)Math.Round((normY * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte normB = heightByte;

				float contrastHeight = Math.Clamp((heightVal - 0.5f) * 1.4f + 0.5f, 0.0f, 1.0f);
				float highDetail = Math.Abs(luminance[x, y] - fineMean[x, y]);
				float lerpVal = 0.85f + (0.45f - 0.85f) * contrastHeight;
				float roughness = Math.Clamp(lerpVal + highDetail * 0.8f, 0.15f, 0.95f);
				byte normA = (byte)Math.Clamp((int)Math.Round(roughness * 255.0f), 0, 255);

				layer1[x, y] = new Rgba32(normR, normG, normB, normA);
			}
		}
	}

	public static byte[] EncodeWebp(Image<Rgba32> image, bool lossless = false, int quality = 90)
	{
		using var ms = new MemoryStream();
		var encoder = new WebpEncoder
		{
			FileFormat = lossless ? WebpFileFormatType.Lossless : WebpFileFormatType.Lossy,
			Quality = lossless ? 100 : quality
		};
		image.Save(ms, encoder);
		return ms.ToArray();
	}

	private static bool EncodeTwoLayerPbrRtex(
		Image<Rgba32> layer0,
		Image<Rgba32> layer1,
		string outputRtexPath,
		string metadataJson,
		out string errorMessage,
		bool compressAlbedo = true)
	{
		errorMessage = string.Empty;
		try
		{
			byte[] l0Bytes = EncodeWebp(layer0, lossless: !compressAlbedo, quality: 90);
			byte[] l1Bytes = EncodeWebp(layer1, lossless: true); // Always lossless for PBR normal/height/roughness

			byte[] rtexBytes = RtexFile.Build(metadataJson, [l0Bytes, l1Bytes]);
			string? dir = Path.GetDirectoryName(outputRtexPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			File.WriteAllBytes(outputRtexPath, rtexBytes);
			RealmMetadataHelper.SyncBlake3Metadata(outputRtexPath);
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
	}

	private static bool EncodeSingleLayerRtex(
		Image<Rgba32> image,
		string outputRtexPath,
		string metadataJson,
		out string errorMessage,
		bool lossless = false,
		int quality = 90)
	{
		errorMessage = string.Empty;
		try
		{
			byte[] l0Bytes = EncodeWebp(image, lossless: lossless, quality: quality);
			byte[] rtexBytes = RtexFile.Build(metadataJson, [l0Bytes]);
			string? dir = Path.GetDirectoryName(outputRtexPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			File.WriteAllBytes(outputRtexPath, rtexBytes);
			RealmMetadataHelper.SyncBlake3Metadata(outputRtexPath);
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
	}

	public static TextureConversionResult ProcessAndSaveTerrainTexture(
		string rawImagePath,
		string outputRtexPath,
		float? forcedScaleFactor = null,
		bool enableRdo = true)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = Image.Load<Rgba32>(result.InputPath);
			float scaleFactor = forcedScaleFactor ?? CalculateLuminanceScaleFactor(sourceImage);
			result.ScaleFactor = scaleFactor;

			ProcessTerrainPbr(sourceImage, isDecal: false, out var layer0, out var layer1);

			using (layer0)
			using (layer1)
			{
				string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"terrain_texture\",\"canonical_blake3\":\"{originalBlake3}\",\"scale_factor\":{scaleFactor.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},\"layers\":2}}";
				bool encodeOk = EncodeTwoLayerPbrRtex(
					layer0,
					layer1,
					result.OutputPath,
					metadataJson,
					out string errorMsg,
					compressAlbedo: true);

				if (!encodeOk)
				{
					result.Success = false;
					result.ErrorMessage = errorMsg;
					return result;
				}
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

	public static TextureConversionResult ProcessAndSaveDecalTexture(
		string rawImagePath,
		string outputRtexPath,
		float? forcedScaleFactor = null,
		bool enableRdo = true,
		int columns = 1,
		int rows = 1)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = Image.Load<Rgba32>(result.InputPath);
			float scaleFactor = forcedScaleFactor ?? CalculateLuminanceScaleFactor(sourceImage);
			result.ScaleFactor = scaleFactor;

			int safeCols = Math.Max(1, columns);
			int safeRows = Math.Max(1, rows);

			ProcessTerrainPbr(sourceImage, isDecal: true, out var layer0, out var layer1);

			using (layer0)
			using (layer1)
			{
				string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"decal\",\"canonical_blake3\":\"{originalBlake3}\",\"scale_factor\":{scaleFactor.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},\"columns\":{safeCols},\"rows\":{safeRows},\"layers\":2}}";
				bool encodeOk = EncodeTwoLayerPbrRtex(
					layer0,
					layer1,
					result.OutputPath,
					metadataJson,
					out string errorMsg,
					compressAlbedo: true);

				if (!encodeOk)
				{
					result.Success = false;
					result.ErrorMessage = errorMsg;
					return result;
				}
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

	public static TextureConversionResult ProcessAndSaveSpritesheet(
		string rawImagePath,
		string outputRtexPath,
		int columns = 4,
		int rows = 4,
		float fps = 20.0f,
		bool enableRdo = false)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = Image.Load<Rgba32>(result.InputPath);
			string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"vfx_spritesheet\",\"canonical_blake3\":\"{originalBlake3}\",\"columns\":{columns},\"rows\":{rows},\"fps\":{fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},\"layers\":1}}";

			bool encodeOk = EncodeSingleLayerRtex(
				sourceImage,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				lossless: false,
				quality: 90);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static TextureConversionResult ProcessAndSaveSkybox(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false,
		float horizonBlendStart = 0.5f,
		Rgba32? horizonColor = null,
		float wrapBlendWidth = 0.05f,
		float zenithBlendEnd = 0.08f,
		Rgba32? zenithColor = null)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = Path.GetExtension(result.InputPath).Equals(".rtex", StringComparison.OrdinalIgnoreCase)
				? ExtractImageFromRtex(result.InputPath, 0) ?? throw new InvalidOperationException($"Failed to load image from RTEX: {result.InputPath}")
				: Image.Load<Rgba32>(originalBits);

			using var processedImage = SkyboxProcessor.ProcessSkybox(
				sourceImage,
				horizonBlendStart,
				horizonColor,
				wrapBlendWidth,
				zenithBlendEnd,
				zenithColor);

			string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"skybox\",\"canonical_blake3\":\"{originalBlake3}\",\"layers\":1}}";

			bool encodeOk = EncodeSingleLayerRtex(
				processedImage,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				lossless: false,
				quality: 95);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static TextureConversionResult ProcessAndSaveRibbonTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "ribbon_texture", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveIconTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = true)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "icon", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveVfxRadialTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "vfx_radial", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveVfxVerticalTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "vfx_vertical", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveSingleLayerTexture(
		string rawImagePath,
		string outputRtexPath,
		string assetType,
		bool enableRdo = false,
		string? customMetadataJson = null)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = Image.Load<Rgba32>(result.InputPath);
			string metadataJson;
			if (!string.IsNullOrWhiteSpace(customMetadataJson))
			{
				try
				{
					var metaObj = System.Text.Json.Nodes.JsonNode.Parse(customMetadataJson)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
					metaObj["created_utc"] = $"{DateTime.UtcNow:O}";
					metaObj["type"] = assetType;
					metaObj["canonical_blake3"] = originalBlake3;
					metaObj["layers"] = 1;
					metadataJson = metaObj.ToJsonString();
				}
				catch
				{
					metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"{assetType}\",\"canonical_blake3\":\"{originalBlake3}\",\"layers\":1}}";
				}
			}
			else
			{
				metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"{assetType}\",\"canonical_blake3\":\"{originalBlake3}\",\"layers\":1}}";
			}

			bool encodeOk = EncodeSingleLayerRtex(
				sourceImage,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				lossless: false,
				quality: 90);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static Image<Rgba32>? ExtractImageFromRtex(string rtexPath, int layer = 0)
	{
		if (!File.Exists(rtexPath)) return null;
		byte[] bytes = File.ReadAllBytes(rtexPath);
		return ExtractImageFromRtexBytes(bytes, layer);
	}

	public static Image<Rgba32>? ExtractImageFromRtexBytes(ReadOnlySpan<byte> rtexBytes, int layer = 0)
	{
		byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, layer);
		if (webpBytes == null || webpBytes.Length == 0) return null;
		return Image.Load<Rgba32>(webpBytes);
	}

	public static byte[]? ExtractWebpFromRtex(string rtexPath, int layer = 0)
	{
		if (!File.Exists(rtexPath)) return null;
		byte[] bytes = File.ReadAllBytes(rtexPath);
		return RtexFile.GetLayer(bytes, layer);
	}

	public static TextureConversionResult ExtractPngFromRtex(
		string inputRtexPath,
		string outputPngPath,
		int layer = 0)
	{
		string fullInput = Path.GetFullPath(inputRtexPath);
		string fullOutput = Path.GetFullPath(outputPngPath);

		var result = new TextureConversionResult
		{
			InputPath = fullInput,
			OutputPath = fullOutput
		};

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputRtexPath}";
			return result;
		}

		try
		{
			using var image = ExtractImageFromRtex(fullInput, layer);
			if (image == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to extract layer {layer} from RTEX '{inputRtexPath}'.";
				return result;
			}

			string? dir = Path.GetDirectoryName(fullOutput);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			image.SaveAsPng(fullOutput);

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

	public static TextureConversionResult ExtractWebpFromRtex(
		string inputRtexPath,
		string outputWebpPath,
		int layer = 0)
	{
		string fullInput = Path.GetFullPath(inputRtexPath);
		string fullOutput = Path.GetFullPath(outputWebpPath);

		var result = new TextureConversionResult
		{
			InputPath = fullInput,
			OutputPath = fullOutput
		};

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputRtexPath}";
			return result;
		}

		try
		{
			byte[]? webpBytes = ExtractWebpFromRtex(fullInput, layer);
			if (webpBytes == null || webpBytes.Length == 0)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to extract layer {layer} from RTEX '{inputRtexPath}'.";
				return result;
			}

			string? dir = Path.GetDirectoryName(fullOutput);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			File.WriteAllBytes(fullOutput, webpBytes);

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

	public static TextureConversionResult ConvertTextureFile(
		string inputPath,
		string? outputPath,
		string? assetType = null,
		int? columns = null,
		int? rows = null,
		float? fps = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		if (ext == ".rtex")
		{
			string targetWebp = string.IsNullOrEmpty(outputPath)
				? Path.ChangeExtension(fullInput, ".webp")
				: Path.GetFullPath(outputPath);
			return targetWebp.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
				? ExtractPngFromRtex(fullInput, targetWebp)
				: ExtractWebpFromRtex(fullInput, targetWebp);
		}

		string normType = (assetType ?? string.Empty).Trim().ToLowerInvariant();

		if (string.IsNullOrEmpty(normType))
		{
			string? meta = RealmMetadataHelper.ExtractMetadata(fullInput);
			if (!string.IsNullOrEmpty(meta))
			{
				try
				{
					var node = JsonNode.Parse(meta);
					string? metaType = node?["type"]?.GetValue<string>()
						?? node?["asset_type"]?.GetValue<string>()
						?? node?["AssetType"]?.GetValue<string>();
					if (!string.IsNullOrEmpty(metaType))
					{
						normType = metaType.Trim().ToLowerInvariant();
					}
					if (node?["columns"] != null && int.TryParse(node["columns"]?.ToString(), out int c) && c > 0)
					{
						columns ??= c;
					}
					if (node?["rows"] != null && int.TryParse(node["rows"]?.ToString(), out int r) && r > 0)
					{
						rows ??= r;
					}
					if (node?["fps"] != null && float.TryParse(node["fps"]?.ToString(), out float f) && f > 0.001f)
					{
						fps ??= f;
					}
				}
				catch { }
			}
		}

		if (string.IsNullOrEmpty(normType))
		{
			throw new InvalidOperationException($"Asset type was not specified and could not be detected from image metadata in '{inputPath}'. Please specify -t / --type (Decal, Icon, Noise, Ribbon, Skybox, SpellSpritesheet, Tilesheet, vfx_radial, vfx_vertical).");
		}

		string targetRtex = string.IsNullOrEmpty(outputPath)
			? Path.ChangeExtension(fullInput, ".rtex")
			: Path.GetFullPath(outputPath);

		if (normType is "terrain" or "terrain_texture" or "terrain_textures" or "tilesheet" or "tilesheets" or "terraintexture" or "terraintextures" or "textures" or "texture")
		{
			return ProcessAndSaveTerrainTexture(fullInput, targetRtex);
		}

		if (normType is "decal" or "decals")
		{
			return ProcessAndSaveDecalTexture(fullInput, targetRtex, columns: columns ?? 1, rows: rows ?? 1);
		}

		if (normType is "spritesheet" or "vfx_spritesheet" or "vfx_spritesheets" or "spritesheets" or "spellspritesheet" or "spellspritesheets" or "spell_spritesheet" or "spell_spritesheets" or "vfxspritesheet" or "vfxspritesheets" or "vfx")
		{
			return ProcessAndSaveSpritesheet(fullInput, targetRtex, columns ?? 4, rows ?? 4, fps: fps ?? 20.0f);
		}

		if (normType is "skybox" or "skyboxes")
		{
			string outExt = Path.GetExtension(targetRtex).ToLowerInvariant();
			if (outExt is not ".rtex")
			{
				return SkyboxProcessor.ProcessSkyboxFile(fullInput, targetRtex);
			}

			return ProcessAndSaveSkybox(fullInput, targetRtex);
		}

		if (normType is "ribbon" or "ribbon_texture" or "ribbon_textures" or "ribbons" or "ribbontexture" or "ribbontextures")
		{
			return ProcessAndSaveRibbonTexture(fullInput, targetRtex);
		}

		if (normType is "noise" or "noise_texture" or "noise_textures" or "noisetexture" or "noisetextures")
		{
			return ProcessAndSaveSingleLayerTexture(fullInput, targetRtex, "noise_texture");
		}

		if (normType is "icon" or "icons")
		{
			return ProcessAndSaveIconTexture(fullInput, targetRtex);
		}

		if (normType is "vfx_radial" or "vfxradial" or "radial" or "radial_mask" or "radialmask")
		{
			return ProcessAndSaveVfxRadialTexture(fullInput, targetRtex);
		}

		if (normType is "vfx_vertical" or "vfxvertical" or "vertical" or "vertical_fin" or "verticalfin")
		{
			return ProcessAndSaveVfxVerticalTexture(fullInput, targetRtex);
		}

		throw new InvalidOperationException($"Unsupported asset type '{normType}'. Supported types: Decal, Icon, Noise, Ribbon, Skybox, SpellSpritesheet, Tilesheet, vfx_radial, vfx_vertical.");
	}

	public static int ConvertTextureDirectory(
		string inputDir,
		string? outputDir,
		string? assetType,
		bool recursive,
		int? columns = null,
		int? rows = null,
		float? fps = null)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			if (!ImageFormatConverter.IsImageFile(file)) continue;

			string fileExt = Path.GetExtension(file).ToLowerInvariant();
			string targetExt = fileExt == ".rtex" ? ".webp" : ".rtex";

			string target;
			if (string.IsNullOrEmpty(fullOutputDir))
			{
				target = Path.ChangeExtension(file, targetExt);
			}
			else
			{
				string rel = Path.GetRelativePath(fullInputDir, file);
				target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, targetExt));
			}

			try
			{
				var res = ConvertTextureFile(file, target, assetType, columns, rows, fps);
				if (res.Success)
				{
					Console.WriteLine($"Converted: {file} -> {target}");
					successCount++;
				}
				else
				{
					Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
					failCount++;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to convert {file}: {ex.Message}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished texture conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	private static float[,] ComputeSeparableBoxBlur(float[,] input, int w, int h, int radius)
	{
		float[,] temp = new float[w, h];
		float[,] result = new float[w, h];
		int windowSize = 2 * radius + 1;
		float invWindow = 1.0f / windowSize;

		for (int y = 0; y < h; y++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int px = (k % w + w) % w;
				sum += input[px, y];
			}
			temp[0, y] = sum * invWindow;

			for (int x = 1; x < w; x++)
			{
				int removeX = ((x - 1 - radius) % w + w) % w;
				int addX = ((x + radius) % w + w) % w;
				sum += input[addX, y] - input[removeX, y];
				temp[x, y] = sum * invWindow;
			}
		}

		for (int x = 0; x < w; x++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int py = (k % h + h) % h;
				sum += temp[x, py];
			}
			result[x, 0] = sum * invWindow;

			for (int y = 1; y < h; y++)
			{
				int removeY = ((y - 1 - radius) % h + h) % h;
				int addY = ((y + radius) % h + h) % h;
				sum += temp[x, addY] - temp[x, removeY];
				result[x, y] = sum * invWindow;
			}
		}

		return result;
	}
}
