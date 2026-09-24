using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace Realm.Shared.Textures;

public static class IndexedPngHelper
{
	private static readonly PngEncoder IndexedEncoder = new()
	{
		ColorType = PngColorType.Palette,
		BitDepth = PngBitDepth.Bit8,
		Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = 256 }),
		CompressionLevel = PngCompressionLevel.DefaultCompression
	};

	public static void SaveAs256ColorPng(byte[] rgbaPixelBytes, int width, int height, string destinationPath)
	{
		string? directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using var image = Image.LoadPixelData<Rgba32>(rgbaPixelBytes, width, height);
		image.Save(destinationPath, IndexedEncoder);
	}

	public static void SaveAs256ColorPng(Image<Rgba32> image, string destinationPath)
	{
		string? directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		image.Save(destinationPath, IndexedEncoder);
	}
}
