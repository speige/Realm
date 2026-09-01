using System;
using System.IO;
using Realm.Shared.Metadata;

namespace Realm.Shared.Audio;

public class AudioConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class AudioConverter
{
	public static readonly string[] SupportedExtensions =
	[
		".mp3", ".wav", ".aiff", ".aif", ".flac", ".aac", ".m4a", ".wma", ".ogg", ".opus"
	];

	public static bool IsAudioFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return Array.Exists(SupportedExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
	}

	public static AudioConversionResult ConvertToOgg(string inputAudioPath, string? outputOggPath = null)
	{
		string fullInput = Path.GetFullPath(inputAudioPath);
		var result = new AudioConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input audio file not found: {inputAudioPath}";
			return result;
		}

		string targetOgg = !string.IsNullOrEmpty(outputOggPath)
			? Path.GetFullPath(outputOggPath)
			: Path.ChangeExtension(fullInput, ".ogg");
		result.OutputPath = targetOgg;

		string? targetDir = Path.GetDirectoryName(targetOgg);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}

		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		if (ext == ".ogg")
		{
			if (!string.Equals(fullInput, targetOgg, StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(fullInput, targetOgg, true);
			}

			try
			{
				string? existingMeta = RealmMetadataHelper.ExtractMetadata(fullInput);
				if (string.IsNullOrEmpty(existingMeta))
				{
					string defaultMeta = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"format\":\"ogg_vorbis\"}}";
					RealmMetadataHelper.AddMetadataToOgg(targetOgg, defaultMeta);
				}
			}
			catch
			{
			}

			result.Success = true;
			return result;
		}

		string? ffmpeg = NativeToolRunner.FindFfmpegPath();
		if (string.IsNullOrEmpty(ffmpeg))
		{
			result.Success = false;
			result.ErrorMessage = "ffmpeg binary not found for audio conversion.";
			return result;
		}

		var run = NativeToolRunner.RunTool(ffmpeg, $"-y -i \"{fullInput}\" -c:a libvorbis -q:a 5 \"{targetOgg}\"");
		if (run.ExitCode == 0 && File.Exists(targetOgg) && new FileInfo(targetOgg).Length > 0)
		{
			try
			{
				string? existingMeta = RealmMetadataHelper.ExtractMetadata(fullInput);
				string metaToEmbed = !string.IsNullOrEmpty(existingMeta)
					? existingMeta
					: $"{{\"original_format\":\"{ext}\",\"created_utc\":\"{DateTime.UtcNow:O}\"}}";

				RealmMetadataHelper.AddMetadataToOgg(targetOgg, metaToEmbed);
			}
			catch
			{
			}

			result.Success = true;
			return result;
		}

		result.Success = false;
		result.ErrorMessage = $"ffmpeg audio conversion failed (exit code {run.ExitCode}): {run.Stderr}\n{run.Stdout}";
		return result;
	}

	public static int ConvertAudioDirectory(string inputDir, string? outputDir, bool recursive)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			if (!IsAudioFile(file)) continue;

			string target;
			if (string.IsNullOrEmpty(fullOutputDir))
			{
				target = Path.ChangeExtension(file, ".ogg");
			}
			else
			{
				string rel = Path.GetRelativePath(fullInputDir, file);
				target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, ".ogg"));
			}

			var res = ConvertToOgg(file, target);
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

		Console.WriteLine($"Finished audio conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}
