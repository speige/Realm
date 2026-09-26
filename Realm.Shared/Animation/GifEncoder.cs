using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace Realm.Shared.Animation;

public static class GifEncoder
{
	public static void EncodeAnimatedGif(List<SKBitmap> frames, string outputPath, float duration)
	{
		if (frames == null || frames.Count == 0) return;

		int width = frames[0].Width;
		int height = frames[0].Height;
		int frameDelayHundredths = (int)Math.Max(1, MathF.Round((duration / frames.Count) * 100.0f));

		string? dir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		using var stream = File.Create(outputPath);
		using var writer = new BinaryWriter(stream);

		// 1. Header: GIF89a
		writer.Write(new char[] { 'G', 'I', 'F', '8', '9', 'a' });

		// 2. Logical Screen Descriptor
		writer.Write((ushort)width);
		writer.Write((ushort)height);

		// Quantize first frame to get Global Color Table
		var (gctPalette, firstIndices) = QuantizeFrame(frames[0]);

		// Packed: GCT present (0x80), color resolution 8-bit (0x70), GCT size 256 (0x07) -> 0xF7
		writer.Write((byte)0xF7);
		writer.Write((byte)0); // Background color index
		writer.Write((byte)0); // Pixel aspect ratio

		// Write GCT (768 bytes)
		for (int i = 0; i < 256; i++)
		{
			if (i < gctPalette.Length)
			{
				writer.Write(gctPalette[i].Red);
				writer.Write(gctPalette[i].Green);
				writer.Write(gctPalette[i].Blue);
			}
			else
			{
				writer.Write((byte)0);
				writer.Write((byte)0);
				writer.Write((byte)0);
			}
		}

		// 3. Application Extension (Netscape 2.0 for infinite looping)
		writer.Write((byte)0x21); // Extension Introducer
		writer.Write((byte)0xFF); // Application Extension Label
		writer.Write((byte)0x0B); // Block Size
		writer.Write(new char[] { 'N', 'E', 'T', 'S', 'C', 'A', 'P', 'E', '2', '.', '0' });
		writer.Write((byte)0x03); // Sub-block Size
		writer.Write((byte)0x01); // Loop extension
		writer.Write((ushort)0);  // Loop count (0 = infinite)
		writer.Write((byte)0x00); // Block Terminator

		// 4. Frames
		for (int f = 0; f < frames.Count; f++)
		{
			var frame = frames[f];
			var (palette, indices) = (f == 0) ? (gctPalette, firstIndices) : QuantizeFrame(frame);

			// Graphic Control Extension
			writer.Write((byte)0x21); // Extension Introducer
			writer.Write((byte)0xF9); // Graphic Control Label
			writer.Write((byte)0x04); // Block Size
			writer.Write((byte)0x08); // Disposal method 2 (Restore to background)
			writer.Write((ushort)frameDelayHundredths);
			writer.Write((byte)0x00); // Transparent color index
			writer.Write((byte)0x00); // Block Terminator

			// Image Descriptor
			writer.Write((byte)0x2C); // Image Separator
			writer.Write((ushort)0);  // Left
			writer.Write((ushort)0);  // Top
			writer.Write((ushort)width);
			writer.Write((ushort)height);

			if (f == 0)
			{
				// Uses GCT
				writer.Write((byte)0x00);
			}
			else
			{
				// Local Color Table present (0x80) + 256 colors (0x07) = 0x87
				writer.Write((byte)0x87);
				for (int i = 0; i < 256; i++)
				{
					if (i < palette.Length)
					{
						writer.Write(palette[i].Red);
						writer.Write(palette[i].Green);
						writer.Write(palette[i].Blue);
					}
					else
					{
						writer.Write((byte)0);
						writer.Write((byte)0);
						writer.Write((byte)0);
					}
				}
			}

			// Write LZW Image Data
			WriteLzwData(writer, indices);
		}

		// 5. Trailer
		writer.Write((byte)0x3B);
	}

	private static (SKColor[] Palette, byte[] Indices) QuantizeFrame(SKBitmap bitmap)
	{
		int w = bitmap.Width;
		int h = bitmap.Height;
		byte[] indices = new byte[w * h];

		var paletteList = new List<SKColor>();
		var colorMap = new Dictionary<int, byte>();

		int idx = 0;
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				SKColor c = bitmap.GetPixel(x, y);
				int key = ((c.Red >> 3) << 10) | ((c.Green >> 3) << 5) | (c.Blue >> 3);

				if (!colorMap.TryGetValue(key, out byte pIdx))
				{
					if (paletteList.Count < 256)
					{
						pIdx = (byte)paletteList.Count;
						paletteList.Add(new SKColor(c.Red, c.Green, c.Blue, 255));
						colorMap[key] = pIdx;
					}
					else
					{
						pIdx = FindClosestColor(c, paletteList);
						colorMap[key] = pIdx;
					}
				}

				indices[idx++] = pIdx;
			}
		}

		if (paletteList.Count == 0)
		{
			paletteList.Add(SKColors.Black);
		}

		return (paletteList.ToArray(), indices);
	}

	private static byte FindClosestColor(SKColor color, List<SKColor> palette)
	{
		int minDist = int.MaxValue;
		byte bestIdx = 0;

		for (int i = 0; i < palette.Count; i++)
		{
			int dr = color.Red - palette[i].Red;
			int dg = color.Green - palette[i].Green;
			int db = color.Blue - palette[i].Blue;
			int dist = dr * dr + dg * dg + db * db;
			if (dist < minDist)
			{
				minDist = dist;
				bestIdx = (byte)i;
				if (dist == 0) break;
			}
		}

		return bestIdx;
	}

	private static void WriteLzwData(BinaryWriter writer, byte[] pixels)
	{
		const int initCodeSize = 8;
		writer.Write((byte)initCodeSize);

		int clearCode = 1 << initCodeSize; // 256
		int eoiCode = clearCode + 1;       // 257

		int codeSize = initCodeSize + 1;   // 9 bits initially
		int maxCode = (1 << codeSize) - 1;
		int nextCode = eoiCode + 1;

		var dictionary = new Dictionary<long, int>();

		using var ms = new MemoryStream();
		int bitBuffer = 0;
		int bitCount = 0;

		void OutputCode(int code)
		{
			bitBuffer |= (code << bitCount);
			bitCount += codeSize;

			while (bitCount >= 8)
			{
				ms.WriteByte((byte)(bitBuffer & 0xFF));
				bitBuffer >>= 8;
				bitCount -= 8;
			}
		}

		OutputCode(clearCode);

		int prefix = -1;

		for (int i = 0; i < pixels.Length; i++)
		{
			int k = pixels[i];
			if (prefix == -1)
			{
				prefix = k;
				continue;
			}

			long key = (((long)(uint)prefix) << 16) | (uint)k;
			if (dictionary.TryGetValue(key, out int existingCode))
			{
				prefix = existingCode;
			}
			else
			{
				OutputCode(prefix);

				if (nextCode < 4096)
				{
					dictionary[key] = nextCode++;
					if (nextCode > maxCode && codeSize < 12)
					{
						codeSize++;
						maxCode = (1 << codeSize) - 1;
					}
				}
				else
				{
					OutputCode(clearCode);
					dictionary.Clear();
					codeSize = initCodeSize + 1;
					maxCode = (1 << codeSize) - 1;
					nextCode = eoiCode + 1;
				}

				prefix = k;
			}
		}

		if (prefix != -1)
		{
			OutputCode(prefix);
		}

		OutputCode(eoiCode);

		if (bitCount > 0)
		{
			ms.WriteByte((byte)(bitBuffer & 0xFF));
		}

		byte[] lzwBytes = ms.ToArray();
		int offset = 0;
		while (offset < lzwBytes.Length)
		{
			int chunkSize = Math.Min(255, lzwBytes.Length - offset);
			writer.Write((byte)chunkSize);
			writer.Write(lzwBytes, offset, chunkSize);
			offset += chunkSize;
		}

		writer.Write((byte)0x00);
	}
}
