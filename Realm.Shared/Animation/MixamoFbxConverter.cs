using Assimp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Shared.Animation;

public class MixamoFbxConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public List<string> ConvertedAnimationNames { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class MixamoFbxConverter
{
	public static List<(string AnimationName, RealmAnimationData Data)> ExtractAnimationsFromFbx(string fbxPath, string? originalFileName = null)
	{
		var result = new List<(string AnimationName, RealmAnimationData Data)>();
		if (!File.Exists(fbxPath)) return result;

		using var importer = new AssimpContext();
		var scene = importer.ImportFile(fbxPath, PostProcessSteps.None);
		if (scene == null || scene.AnimationCount == 0)
		{
			return result;
		}

		string fileBaseName = !string.IsNullOrEmpty(originalFileName)
			? Path.GetFileNameWithoutExtension(originalFileName)
			: Path.GetFileNameWithoutExtension(fbxPath);

		for (int i = 0; i < scene.AnimationCount; i++)
		{
			var assimpAnim = scene.Animations[i];
			string animName = SanitizeAnimationName(assimpAnim.Name, fileBaseName, scene.AnimationCount);
			double ticksPerSecond = assimpAnim.TicksPerSecond > 0 ? assimpAnim.TicksPerSecond : 30.0;
			float duration = (float)(assimpAnim.DurationInTicks / ticksPerSecond);

			var realmAnim = new RealmAnimationData
			{
				FormatVersion = 1,
				Name = animName,
				Duration = duration,
				FrameRate = (float)ticksPerSecond,
				LoopMode = RealmAnimationLoopMode.Linear
			};

			var trackList = new List<RealmAnimationBoneTrack>();
			var boneTrackDict = new Dictionary<string, RealmAnimationBoneTrack>(StringComparer.OrdinalIgnoreCase);

			foreach (var channel in assimpAnim.NodeAnimationChannels)
			{
				string rawBoneName = HumanoidBoneMapper.CleanBoneName(channel.NodeName);
				if (string.IsNullOrEmpty(rawBoneName)) continue;

				string canonicalName = rawBoneName;
				if (HumanoidBoneMapper.TryMapToCanonical(rawBoneName, out var canonicalBone))
				{
					canonicalName = canonicalBone.ToString();
				}

				if (!boneTrackDict.TryGetValue(canonicalName, out var boneTrack))
				{
					boneTrack = new RealmAnimationBoneTrack
					{
						BoneName = canonicalName
					};
					boneTrackDict[canonicalName] = boneTrack;
					trackList.Add(boneTrack);
				}

				if (channel.HasPositionKeys)
				{
					var posKeys = new RealmKeyframeVector3[channel.PositionKeyCount];
					for (int k = 0; k < channel.PositionKeyCount; k++)
					{
						var key = channel.PositionKeys[k];
						float time = (float)(key.Time / ticksPerSecond);
						posKeys[k] = new RealmKeyframeVector3(time, key.Value.X, key.Value.Y, key.Value.Z);
					}
					boneTrack.PositionKeys = posKeys;
				}

				if (channel.HasRotationKeys)
				{
					var rotKeys = new RealmKeyframeQuaternion[channel.RotationKeyCount];
					for (int k = 0; k < channel.RotationKeyCount; k++)
					{
						var key = channel.RotationKeys[k];
						float time = (float)(key.Time / ticksPerSecond);
						rotKeys[k] = new RealmKeyframeQuaternion(time, key.Value.X, key.Value.Y, key.Value.Z, key.Value.W);
					}
					boneTrack.RotationKeys = rotKeys;
				}

				if (channel.HasScalingKeys)
				{
					var scaleKeys = new RealmKeyframeVector3[channel.ScalingKeyCount];
					for (int k = 0; k < channel.ScalingKeyCount; k++)
					{
						var key = channel.ScalingKeys[k];
						float time = (float)(key.Time / ticksPerSecond);
						scaleKeys[k] = new RealmKeyframeVector3(time, key.Value.X, key.Value.Y, key.Value.Z);
					}
					boneTrack.ScaleKeys = scaleKeys;
				}
			}

			realmAnim.Tracks = trackList.ToArray();
			if (realmAnim.Tracks.Length > 0)
			{
				result.Add((animName, realmAnim));
			}
		}

		return result;
	}

	public static MixamoFbxConversionResult ConvertFbxFile(string fbxPath, string? outputPath = null)
	{
		var result = new MixamoFbxConversionResult { InputPath = fbxPath };
		try
		{
			if (!File.Exists(fbxPath))
			{
				result.Success = false;
				result.ErrorMessage = $"Input file not found: {fbxPath}";
				return result;
			}

			var anims = ExtractAnimationsFromFbx(fbxPath);
			if (anims.Count == 0)
			{
				result.Success = false;
				result.ErrorMessage = "No animation tracks found in FBX file.";
				return result;
			}

			string originalBaseName = Path.GetFileNameWithoutExtension(fbxPath);
			string targetBase = string.IsNullOrEmpty(outputPath)
				? Path.ChangeExtension(fbxPath, ".ranim")
				: outputPath;

			if (Directory.Exists(targetBase) || (string.IsNullOrEmpty(Path.GetExtension(targetBase)) && anims.Count > 1))
			{
				Directory.CreateDirectory(targetBase);
				for (int i = 0; i < anims.Count; i++)
				{
					string outName = anims.Count == 1
						? $"{originalBaseName}.ranim"
						: $"{originalBaseName}_{i}.ranim";
					string fullOut = Path.Combine(targetBase, outName);
					RealmAnimationSerializer.SaveToFile(fullOut, anims[i].Data);
					result.ConvertedAnimationNames.Add(anims[i].AnimationName);
					result.OutputPath = fullOut;
				}
			}
			else
			{
				if (anims.Count == 1)
				{
					RealmAnimationSerializer.SaveToFile(targetBase, anims[0].Data);
					result.ConvertedAnimationNames.Add(anims[0].AnimationName);
					result.OutputPath = targetBase;
				}
				else
				{
					string dir = Path.GetDirectoryName(targetBase) ?? "";
					for (int i = 0; i < anims.Count; i++)
					{
						string outName = $"{originalBaseName}_{i}.ranim";
						string fullOut = Path.Combine(dir, outName);
						RealmAnimationSerializer.SaveToFile(fullOut, anims[i].Data);
						result.ConvertedAnimationNames.Add(anims[i].AnimationName);
						result.OutputPath = fullOut;
					}
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

	public static int ConvertFbxDirectory(string inputDir, string? outputDir, bool recursive)
	{
		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(inputDir, "*.fbx", searchOpt);
		Console.WriteLine($"Found {files.Length} .fbx file(s) in {inputDir}");

		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string? target;
			if (string.IsNullOrEmpty(outputDir))
			{
				target = Path.ChangeExtension(file, ".ranim");
			}
			else
			{
				string rel = Path.GetRelativePath(inputDir, file);
				target = Path.Combine(outputDir, Path.ChangeExtension(rel, ".ranim"));
			}

			var res = ConvertFbxFile(file, target);
			if (res.Success)
			{
				Console.WriteLine($"Converted: {file} -> {res.OutputPath} ({string.Join(", ", res.ConvertedAnimationNames)})");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished FBX conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	private static string SanitizeAnimationName(string assimpAnimName, string fileBaseName, int totalAnims)
	{
		if (string.IsNullOrEmpty(assimpAnimName) || totalAnims <= 1)
		{
			return !string.IsNullOrEmpty(fileBaseName) ? fileBaseName : (assimpAnimName ?? "anim");
		}

		string name = assimpAnimName;
		if (name.Contains('|'))
		{
			string[] parts = name.Split('|', StringSplitOptions.RemoveEmptyEntries);
			name = parts[^1];
		}

		string cleanCheck = name.Replace(':', '_').Replace('.', '_');
		if (cleanCheck.StartsWith("mixamo", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("Layer0", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("default", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.StartsWith("Take", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("Animation", StringComparison.OrdinalIgnoreCase))
		{
			return fileBaseName;
		}

		return name;
	}
}
