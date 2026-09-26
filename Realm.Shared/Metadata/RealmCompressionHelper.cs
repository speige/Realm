using System;
using ZstdSharp;

namespace Realm.Shared.Metadata;

public static class RealmCompressionHelper
{
	public const int DefaultCompressionLevel = 3;

	public static byte[] Compress(ReadOnlySpan<byte> uncompressedBytes, int level = DefaultCompressionLevel)
	{
		if (uncompressedBytes.IsEmpty)
		{
			return Array.Empty<byte>();
		}

		using var compressor = new Compressor(level);
		return compressor.Wrap(uncompressedBytes).ToArray();
	}

	public static byte[] Decompress(ReadOnlySpan<byte> compressedBytes)
	{
		if (compressedBytes.IsEmpty)
		{
			return Array.Empty<byte>();
		}

		using var decompressor = new Decompressor();
		return decompressor.Unwrap(compressedBytes).ToArray();
	}
}
