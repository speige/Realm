using Godot;
using Realm.Godot.Animation;
using Realm.Shared;
using System;
using System.Collections.Generic;
using System.IO;

public class AnimatedThumbnail
{
	public List<Texture2D> Frames { get; set; } = new();
	public float Fps { get; set; } = 6.0f;
	public Texture2D? PrimaryFrame => Frames.Count > 0 ? Frames[0] : null;
}

public static class AssetThumbnailProvider
{
	public static event Action<string, Texture2D>? ThumbnailGenerated;

	private static readonly Dictionary<string, Texture2D> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, AnimatedThumbnail> _animatedThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Texture2D> _formatBadgeCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly List<string> _lruOrder = new();
	private const int MaxCacheEntries = 400;

	static AssetThumbnailProvider()
	{
		GlbThumbnailRenderer.Instance.ThumbnailGenerated += OnGlbThumbnailGenerated;
	}

	private static void OnGlbThumbnailGenerated(string filePath, Texture2D texture)
	{
		lock (_thumbnailCache)
		{
			_thumbnailCache[filePath] = texture;
			TouchLru(filePath);
		}
		ThumbnailGenerated?.Invoke(filePath, texture);
	}

	public static Texture2D? GetThumbnail(IndexedAsset asset)
	{
		if (asset == null || string.IsNullOrEmpty(asset.FilePath))
		{
			return GetPlaceholderTexture("?");
		}

		string ext = asset.Extension.ToLowerInvariant();
		if (ext == ".ranim")
		{
			var animThumb = GetAnimatedThumbnail(asset);
			if (animThumb?.PrimaryFrame != null)
			{
				return animThumb.PrimaryFrame;
			}
			return GetPlaceholderTexture("RANIM");
		}

		lock (_thumbnailCache)
		{
			if (_thumbnailCache.TryGetValue(asset.FilePath, out var cachedTexture) && cachedTexture != null)
			{
				TouchLru(asset.FilePath);
				return cachedTexture;
			}
		}

		Texture2D? generatedTexture = GenerateThumbnailDirect(asset);
		if (generatedTexture == null)
		{
			generatedTexture = GetPlaceholderTexture(asset.Extension.TrimStart('.').ToUpperInvariant());
		}

		lock (_thumbnailCache)
		{
			if (_thumbnailCache.Count >= MaxCacheEntries && _lruOrder.Count > 0)
			{
				string oldestKey = _lruOrder[0];
				_lruOrder.RemoveAt(0);
				_thumbnailCache.Remove(oldestKey);
			}

			_thumbnailCache[asset.FilePath] = generatedTexture;
			TouchLru(asset.FilePath);
		}

		return generatedTexture;
	}

	public static AnimatedThumbnail? GetAnimatedThumbnail(IndexedAsset asset)
	{
		if (asset == null || string.IsNullOrEmpty(asset.FilePath)) return null;

		lock (_animatedThumbnailCache)
		{
			if (_animatedThumbnailCache.TryGetValue(asset.FilePath, out var cachedAnim) && cachedAnim != null)
			{
				return cachedAnim;
			}
		}

		if (!File.Exists(asset.FilePath)) return null;

		try
		{
			var animData = RealmAnimationSerializer.LoadFromFile(asset.FilePath);
			if (animData != null)
			{
				var animThumb = RanimSkeletonThumbnailGenerator.GenerateAnimatedThumbnail(animData);
				if (animThumb != null && animThumb.Frames.Count > 0)
				{
					lock (_animatedThumbnailCache)
					{
						if (_animatedThumbnailCache.Count >= MaxCacheEntries)
						{
							_animatedThumbnailCache.Clear();
						}
						_animatedThumbnailCache[asset.FilePath] = animThumb;
					}
					return animThumb;
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetThumbnailProvider] Error loading ranim {asset.FilePath}: {ex.Message}");
		}

		return null;
	}

	private static void TouchLru(string key)
	{
		_lruOrder.Remove(key);
		_lruOrder.Add(key);
	}

	private static Texture2D? GenerateThumbnailDirect(IndexedAsset asset)
	{
		string ext = asset.Extension.ToLowerInvariant();

		if (ext == ".ktx2")
		{
			return LoadKtx2AlbedoThumbnail(asset.FilePath, asset.LastModifiedUtc);
		}

		if (ext == ".glb" || ext == ".gltf")
		{
			return LoadGlbThumbnail(asset.FilePath, asset.LastModifiedUtc);
		}

		if (ext == ".ogg" || ext == ".wav" || ext == ".mp3")
		{
			return LoadAudioThumbnail(ext);
		}

		if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".bmp" || ext == ".tga")
		{
			return LoadRasterImageThumbnail(asset.FilePath);
		}

		if (ext == ".svg")
		{
			return LoadSvgThumbnail(asset.FilePath);
		}

		return GetPlaceholderTexture(ext.TrimStart('.').ToUpperInvariant());
	}

	private static Texture2D? LoadGlbThumbnail(string glbPath, DateTime lastModifiedUtc)
	{
		if (GlbThumbnailRenderer.Instance.TryGetDiskCached(glbPath, lastModifiedUtc, out var cachedTexture))
		{
			return cachedTexture;
		}

		GlbThumbnailRenderer.Instance.EnqueueRequest(glbPath, lastModifiedUtc);
		return GetPlaceholderTexture("GLB");
	}

	private static Texture2D? LoadKtx2AlbedoThumbnail(string ktx2Path, DateTime lastModifiedUtc)
	{
		if (!File.Exists(ktx2Path))
		{
			return null;
		}

		string cacheDirectory = ProjectSettings.GlobalizePath("user://ktx_layer_cache");
		if (!Directory.Exists(cacheDirectory))
		{
			Directory.CreateDirectory(cacheDirectory);
		}

		string baseName = Path.GetFileNameWithoutExtension(ktx2Path);
		string cachedPngPath = Path.Combine(cacheDirectory, $"{baseName}_thumb_l0_{lastModifiedUtc.Ticks}.png");

		bool needsExtraction = true;
		if (File.Exists(cachedPngPath))
		{
			needsExtraction = false;
		}

		if (needsExtraction)
		{
			string? ktxCommandPath = NativeToolRunner.FindKtxPath();
			if (!string.IsNullOrEmpty(ktxCommandPath))
			{
				try
				{
					string tempExtractionPng = Path.Combine(cacheDirectory, $"{baseName}_raw_{Guid.NewGuid():N}.png");
					var runResult = NativeToolRunner.RunTool(ktxCommandPath, $"extract --layer 0 --level 0 --transcode rgba8 \"{ktx2Path}\" \"{tempExtractionPng}\"", 10000);
					if (runResult.ExitCode == 0 && File.Exists(tempExtractionPng))
					{
						var img = Image.LoadFromFile(tempExtractionPng);
						if (img != null)
						{
							if (img.GetWidth() > 128 || img.GetHeight() > 128)
							{
								img.Resize(128, 128, Image.Interpolation.Bilinear);
							}
							img.SavePng(cachedPngPath);
						}
						try { File.Delete(tempExtractionPng); } catch { }
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[AssetThumbnailProvider] KTX extraction error on {ktx2Path}: {ex.Message}");
				}
			}
		}

		if (File.Exists(cachedPngPath))
		{
			try
			{
				var image = Image.LoadFromFile(cachedPngPath);
				if (image != null)
				{
					return ImageTexture.CreateFromImage(image);
				}
			}
			catch { }
		}

		return null;
	}

	private static Texture2D? LoadRasterImageThumbnail(string imagePath)
	{
		if (!File.Exists(imagePath))
		{
			return null;
		}

		try
		{
			var image = Image.LoadFromFile(imagePath);
			if (image != null)
			{
				if (image.GetWidth() > 128 || image.GetHeight() > 128)
				{
					image.Resize(128, 128, Image.Interpolation.Bilinear);
				}
				return ImageTexture.CreateFromImage(image);
			}
		}
		catch { }

		return null;
	}

	private static Texture2D? LoadSvgThumbnail(string svgPath)
	{
		if (!File.Exists(svgPath))
		{
			return null;
		}

		try
		{
			var image = new Image();
			var err = image.Load(svgPath);
			if (err == Error.Ok)
			{
				if (image.GetWidth() > 128 || image.GetHeight() > 128)
				{
					image.Resize(128, 128, Image.Interpolation.Bilinear);
				}
				return ImageTexture.CreateFromImage(image);
			}
		}
		catch { }

		return null;
	}

	public static Texture2D LoadAudioThumbnail(string extension)
	{
		string extUpper = extension.TrimStart('.').ToUpperInvariant();
		string cacheKey = $"AUDIO_{extUpper}";

		lock (_formatBadgeCache)
		{
			if (_formatBadgeCache.TryGetValue(cacheKey, out var cached))
			{
				return cached;
			}

			var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);

			var bgTop = new Color(0.06f, 0.14f, 0.10f, 1.0f);
			var bgBottom = new Color(0.10f, 0.22f, 0.16f, 1.0f);
			var borderColor = new Color(0.18f, 0.45f, 0.30f, 0.9f);
			var playColor = new Color(0.96f, 0.82f, 0.38f, 1.0f);
			var shadowColor = new Color(0.02f, 0.04f, 0.03f, 0.8f);

			for (int y = 0; y < 128; y++)
			{
				float t = (float)y / 128f;
				var rowColor = bgTop.Lerp(bgBottom, t);
				for (int x = 0; x < 128; x++)
				{
					bool isBorder = (x == 0 || x == 127 || y == 0 || y == 127);
					img.SetPixel(x, y, isBorder ? borderColor : rowColor);
				}
			}

			DrawPlayTriangle(img, 46, 34, 46, 86, 86, 60, shadowColor, 2);
			DrawPlayTriangle(img, 44, 32, 44, 84, 84, 58, playColor, 0);

			int[] barHeights = new[] { 10, 18, 14, 24, 16, 20, 12 };
			int startX = 31;
			var barColor = new Color(0.20f, 0.70f, 0.45f, 1.0f);
			var capColor = new Color(0.96f, 0.82f, 0.38f, 1.0f);

			for (int b = 0; b < barHeights.Length; b++)
			{
				int bx = startX + b * 10;
				int bh = barHeights[b];
				int by = 112 - bh;
				for (int py = by; py <= 112; py++)
				{
					var col = (py <= by + 2) ? capColor : barColor;
					for (int px = bx; px < bx + 6; px++)
					{
						if (px < 127 && py < 127) img.SetPixel(px, py, col);
					}
				}
			}

			var texture = ImageTexture.CreateFromImage(img);
			_formatBadgeCache[cacheKey] = texture;
			return texture;
		}
	}

	private static void DrawPlayTriangle(Image img, int x0, int y0, int x1, int y1, int x2, int y2, Color color, int offset)
	{
		x0 += offset; y0 += offset;
		x1 += offset; y1 += offset;
		x2 += offset; y2 += offset;

		int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
		int maxX = Math.Min(img.GetWidth() - 1, Math.Max(x0, Math.Max(x1, x2)));
		int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
		int maxY = Math.Min(img.GetHeight() - 1, Math.Max(y0, Math.Max(y1, y2)));

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				if (PointInTriangle(x, y, x0, y0, x1, y1, x2, y2))
				{
					img.SetPixel(x, y, color);
				}
			}
		}
	}

	private static bool PointInTriangle(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
	{
		float d1 = Sign(px, py, x0, y0, x1, y1);
		float d2 = Sign(px, py, x1, y1, x2, y2);
		float d3 = Sign(px, py, x2, y2, x0, y0);

		bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
		bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

		return !(hasNeg && hasPos);
	}

	private static float Sign(int px, int py, int x1, int y1, int x2, int y2)
	{
		return (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);
	}

	public static Texture2D GetPlaceholderTexture(string formatBadge)
	{
		string key = string.IsNullOrEmpty(formatBadge) ? "FILE" : formatBadge.ToUpperInvariant();

		lock (_formatBadgeCache)
		{
			if (_formatBadgeCache.TryGetValue(key, out var cachedBadge))
			{
				return cachedBadge;
			}

			var image = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);

			Color baseBackgroundColor = key switch
			{
				"GLB" or "GLTF" or "FBX" => new Color(0.18f, 0.28f, 0.42f, 1.0f),
				"RANIM" or "ANIM" => new Color(0.38f, 0.22f, 0.45f, 1.0f),
				"OGG" or "WAV" or "MP3" => new Color(0.20f, 0.42f, 0.30f, 1.0f),
				"KTX2" or "PNG" or "JPG" or "JPEG" => new Color(0.35f, 0.32f, 0.18f, 1.0f),
				"JSON" or "TXT" => new Color(0.28f, 0.28f, 0.30f, 1.0f),
				_ => new Color(0.20f, 0.22f, 0.26f, 1.0f)
			};

			Color borderColor = new Color(baseBackgroundColor.R * 1.5f, baseBackgroundColor.G * 1.5f, baseBackgroundColor.B * 1.5f, 0.8f);

			for (int y = 0; y < 128; y++)
			{
				for (int x = 0; x < 128; x++)
				{
					bool isBorder = (x < 2 || x >= 126 || y < 2 || y >= 126);
					image.SetPixel(x, y, isBorder ? borderColor : baseBackgroundColor);
				}
			}

			var texture = ImageTexture.CreateFromImage(image);
			_formatBadgeCache[key] = texture;
			return texture;
		}
	}
}
