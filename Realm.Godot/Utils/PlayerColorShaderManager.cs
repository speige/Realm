using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Godot.Utils;

public static class PlayerColorShaderManager
{
	private static Shader _sharedShader;
	private static readonly Dictionary<string, ShaderMaterial> _materialCache = new(StringComparer.Ordinal);
	private static readonly Dictionary<ulong, Texture2D> _normalizedAlbedoCache = new();
	private static readonly Dictionary<ulong, bool> _playerMaskCheckCache = new();
	private static readonly float[] SrgbToLinearLut = new float[256];
	private static readonly StringName _paramPlayerColor = new("player_color");
	private static readonly StringName _paramModelBrightness = new("model_brightness");
	private static readonly StringName _paramModelColorTint = new("model_color_tint");
	private static readonly StringName _paramIgnorePlayerColor = new("ignore_player_color");
	private static readonly StringName _paramNormalMode = new("normal_mode");

	private const string ShaderPath = "res://Assets/shaders/player_color_spatial.gdshader";

	static PlayerColorShaderManager()
	{
		for (int i = 0; i < 256; i++)
		{
			float srgb = i / 255.0f;
			SrgbToLinearLut[i] = srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
		}
	}

	public static Shader GetOrCreateShader()
	{
		if (_sharedShader != null && GodotObject.IsInstanceValid(_sharedShader))
		{
			return _sharedShader;
		}

		_sharedShader = GD.Load<Shader>(ShaderPath);
		return _sharedShader;
	}

	public static Texture2D GetOrCreateNormalizedAlbedoTexture(Texture2D sourceTexture, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceTexture == null) return null;
		ulong id = sourceTexture.GetInstanceId();
		if (_normalizedAlbedoCache.TryGetValue(id, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var normalized = NormalizeAlbedoTexture(sourceTexture, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		_normalizedAlbedoCache[id] = normalized;
		return normalized;
	}

	public static Texture2D NormalizeAlbedoTexture(Texture2D sourceTexture, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceTexture == null) return null;
		Image img = sourceTexture.GetImage();
		if (img == null) return sourceTexture;

		Image normalizedImg = NormalizeAlbedoImage(img, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		if (normalizedImg == img)
		{
			return sourceTexture;
		}

		return ImageTexture.CreateFromImage(normalizedImg);
	}

	public static Image NormalizeAlbedoImage(Image sourceImage, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceImage == null) return null;

		Image workingImage = (Image)sourceImage.Duplicate();
		if (workingImage.IsCompressed())
		{
			workingImage.Decompress();
		}

		if (workingImage.HasMipmaps())
		{
			workingImage.ClearMipmaps();
		}

		var fmt = workingImage.GetFormat();
		if (fmt != Image.Format.Rgba8 && fmt != Image.Format.Rgb8)
		{
			workingImage.Convert(Image.Format.Rgba8);
			fmt = Image.Format.Rgba8;
		}

		int w = workingImage.GetWidth();
		int h = workingImage.GetHeight();
		byte[] data = workingImage.GetData();
		int channels = fmt == Image.Format.Rgba8 ? 4 : 3;

		double totalLinearLuminance = 0.0;
		long validPixelCount = 0;

		for (int i = 0; i < data.Length; i += channels)
		{
			byte r = data[i];
			byte g = data[i + 1];
			byte b = data[i + 2];
			byte a = channels >= 4 ? data[i + 3] : (byte)255;

			if (a < 13)
			{
				continue;
			}

			if (r == 0 && g == 0 && b == 0)
			{
				continue;
			}

			float rLin = SrgbToLinearLut[r];
			float gLin = SrgbToLinearLut[g];
			float bLin = SrgbToLinearLut[b];

			float lum = (0.2126f * rLin) + (0.7152f * gLin) + (0.0722f * bLin);
			totalLinearLuminance += lum;
			validPixelCount++;
		}

		if (validPixelCount == 0)
		{
			for (int i = 0; i < data.Length; i += channels)
			{
				byte r = data[i];
				byte g = data[i + 1];
				byte b = data[i + 2];
				byte a = channels >= 4 ? data[i + 3] : (byte)255;
				if (a < 13) continue;

				float rLin = SrgbToLinearLut[r];
				float gLin = SrgbToLinearLut[g];
				float bLin = SrgbToLinearLut[b];
				float lum = (0.2126f * rLin) + (0.7152f * gLin) + (0.0722f * bLin);
				totalLinearLuminance += lum;
				validPixelCount++;
			}
		}

		if (validPixelCount == 0) return sourceImage;

		float avgLuminance = (float)(totalLinearLuminance / validPixelCount);
		if (avgLuminance <= 0.0001f) return sourceImage;

		float rawScaleFactor = targetLinearLuminance / avgLuminance;
		float scaleFactor = Mathf.Clamp(rawScaleFactor, minScaleFactor, maxScaleFactor);

		if (MathF.Abs(scaleFactor - 1.0f) < 0.01f)
		{
			return sourceImage;
		}

		byte[] resultData = new byte[data.Length];
		for (int i = 0; i < data.Length; i += channels)
		{
			byte r = data[i];
			byte g = data[i + 1];
			byte b = data[i + 2];

			float rLin = SrgbToLinearLut[r] * scaleFactor;
			float gLin = SrgbToLinearLut[g] * scaleFactor;
			float bLin = SrgbToLinearLut[b] * scaleFactor;

			resultData[i] = LinearToSrgbByte(rLin);
			resultData[i + 1] = LinearToSrgbByte(gLin);
			resultData[i + 2] = LinearToSrgbByte(bLin);

			if (channels >= 4)
			{
				resultData[i + 3] = data[i + 3];
			}
		}

		Image result = Image.CreateFromData(w, h, false, fmt, resultData);
		result.GenerateMipmaps();

		return result;
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static byte LinearToSrgbByte(float lin)
	{
		if (lin <= 0.0f) return 0;
		if (lin >= 1.0f) return 255;
		float srgb = lin <= 0.0031308f ? lin * 12.92f : 1.055f * MathF.Pow(lin, 1.0f / 2.4f) - 0.055f;
		return (byte)Math.Clamp((int)Math.Round(srgb * 255.0f), 0, 255);
	}

	internal static bool CheckHasPlayerMask(Texture2D ormTexture, Material sourceMaterial)
	{
		if (ormTexture == null) return false;

		if (sourceMaterial is BaseMaterial3D baseMat and not OrmMaterial3D)
		{
			if (baseMat.RoughnessTexture != baseMat.MetallicTexture)
			{
				return false;
			}
		}

		ulong ormId = ormTexture.GetInstanceId();
		if (_playerMaskCheckCache.TryGetValue(ormId, out bool cached))
		{
			return cached;
		}

		Image img = ormTexture.GetImage();
		if (img == null)
		{
			_playerMaskCheckCache[ormId] = false;
			return false;
		}

		Image workingImg = (Image)img.Duplicate();
		if (workingImg.IsCompressed())
		{
			workingImg.Decompress();
		}

		if (workingImg.HasMipmaps())
		{
			workingImg.ClearMipmaps();
		}

		var fmt = workingImg.GetFormat();
		if (fmt != Image.Format.Rgba8 && fmt != Image.Format.Rgb8 && fmt != Image.Format.R8)
		{
			workingImg.Convert(Image.Format.Rgba8);
			fmt = Image.Format.Rgba8;
		}

		byte[] data = workingImg.GetData();
		int channels = fmt == Image.Format.Rgba8 ? 4 : (fmt == Image.Format.Rgb8 ? 3 : 1);
		int totalPixels = workingImg.GetWidth() * workingImg.GetHeight();
		if (totalPixels == 0)
		{
			_playerMaskCheckCache[ormId] = false;
			return false;
		}

		long lowCount = 0;
		long highCount = 0;

		for (int i = 0; i < data.Length; i += channels)
		{
			byte r = data[i];
			if (r <= 32)
			{
				lowCount++;
			}
			else if (r >= 96)
			{
				highCount++;
			}
		}

		double lowRatio = (double)lowCount / totalPixels;
		double highRatio = (double)highCount / totalPixels;

		bool isValidMask = lowRatio >= 0.05 && highRatio >= 0.005;
		_playerMaskCheckCache[ormId] = isValidMask;
		return isValidMask;
	}

	public static bool ModelHasPlayerMask(Node rootNode)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return false;
		return ModelHasPlayerMaskRecursive(rootNode);
	}

	private static bool ModelHasPlayerMaskRecursive(Node node)
	{
		if (node is MeshInstance3D meshInst && !IsExcludedMesh(meshInst))
		{
			int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
			for (int i = 0; i < surfaceCount; i++)
			{
				Material srcMat = meshInst.GetSurfaceOverrideMaterial(i);
				if (srcMat == null && meshInst.Mesh != null)
				{
					srcMat = meshInst.Mesh.SurfaceGetMaterial(i);
				}

				Texture2D ormTexture = null;
				if (srcMat is OrmMaterial3D ormMat)
				{
					ormTexture = ormMat.OrmTexture;
				}
				else if (srcMat is BaseMaterial3D baseMat)
				{
					ormTexture = baseMat.RoughnessTexture ?? baseMat.MetallicTexture;
				}
				else if (srcMat is ShaderMaterial sm)
				{
					var ormVar = sm.GetShaderParameter("texture_orm");
					ormTexture = ormVar.VariantType != Variant.Type.Nil ? ormVar.As<Texture2D>() : null;
				}

				if (ormTexture != null && CheckHasPlayerMask(ormTexture, srcMat))
				{
					return true;
				}
			}

			if (meshInst.MaterialOverride != null)
			{
				Texture2D ormTexture = null;
				if (meshInst.MaterialOverride is OrmMaterial3D ormMat)
				{
					ormTexture = ormMat.OrmTexture;
				}
				else if (meshInst.MaterialOverride is BaseMaterial3D baseMat)
				{
					ormTexture = baseMat.RoughnessTexture ?? baseMat.MetallicTexture;
				}
				else if (meshInst.MaterialOverride is ShaderMaterial sm)
				{
					var ormVar = sm.GetShaderParameter("texture_orm");
					ormTexture = ormVar.VariantType != Variant.Type.Nil ? ormVar.As<Texture2D>() : null;
				}

				if (ormTexture != null && CheckHasPlayerMask(ormTexture, meshInst.MaterialOverride))
				{
					return true;
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && ModelHasPlayerMaskRecursive(childNode))
			{
				return true;
			}
		}

		return false;
	}

	public static ShaderMaterial GetOrCreateShaderMaterial(Material sourceMaterial, bool normalizeLuminance = true)
	{
		var shader = GetOrCreateShader();

		Texture2D rawAlbedoTexture = null;
		Texture2D ormTexture = null;
		Texture2D normalTexture = null;
		Texture2D emissionTexture = null;
		Color albedoColor = new Color(1f, 1f, 1f, 1f);
		Color emissionColor = new Color(0f, 0f, 0f, 1f);
		float emissionEnergy = 1f;
		float roughness = 1f;
		float metallic = 0f;
		float specular = 0.5f;
		Vector3 uv1Scale = Vector3.One;
		Vector3 uv1Offset = Vector3.Zero;
		bool useAlphaBlend = false;
		bool useAlphaScissor = false;
		float alphaScissorThreshold = 0.5f;

		if (sourceMaterial is OrmMaterial3D ormMat)
		{
			rawAlbedoTexture = ormMat.AlbedoTexture;
			ormTexture = ormMat.OrmTexture;
			normalTexture = ormMat.NormalEnabled ? ormMat.NormalTexture : null;
			emissionTexture = ormMat.EmissionEnabled ? ormMat.EmissionTexture : null;
			albedoColor = ormMat.AlbedoColor;
			emissionColor = ormMat.EmissionEnabled ? ormMat.Emission : new Color(0f, 0f, 0f, 1f);
			emissionEnergy = ormMat.EmissionEnabled ? ormMat.EmissionEnergyMultiplier : 1f;
			roughness = ormMat.Roughness;
			metallic = ormMat.Metallic;
			specular = ormMat.MetallicSpecular;
			uv1Scale = ormMat.Uv1Scale;
			uv1Offset = ormMat.Uv1Offset;
			if (ormMat.Transparency == BaseMaterial3D.TransparencyEnum.Alpha || ormMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass)
			{
				useAlphaBlend = true;
			}
			else if (ormMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
			{
				useAlphaScissor = true;
				alphaScissorThreshold = ormMat.AlphaScissorThreshold;
			}
		}
		else if (sourceMaterial is BaseMaterial3D baseMat)
		{
			rawAlbedoTexture = baseMat.AlbedoTexture;
			ormTexture = baseMat.RoughnessTexture ?? baseMat.MetallicTexture;
			normalTexture = baseMat.NormalEnabled ? baseMat.NormalTexture : null;
			emissionTexture = baseMat.EmissionEnabled ? baseMat.EmissionTexture : null;
			albedoColor = baseMat.AlbedoColor;
			emissionColor = baseMat.EmissionEnabled ? baseMat.Emission : new Color(0f, 0f, 0f, 1f);
			emissionEnergy = baseMat.EmissionEnabled ? baseMat.EmissionEnergyMultiplier : 1f;
			roughness = baseMat.Roughness;
			metallic = baseMat.Metallic;
			specular = baseMat.MetallicSpecular;
			uv1Scale = baseMat.Uv1Scale;
			uv1Offset = baseMat.Uv1Offset;
			if (baseMat.Transparency == BaseMaterial3D.TransparencyEnum.Alpha || baseMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass)
			{
				useAlphaBlend = true;
			}
			else if (baseMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
			{
				useAlphaScissor = true;
				alphaScissorThreshold = baseMat.AlphaScissorThreshold;
			}
		}
		else if (sourceMaterial is ShaderMaterial sm)
		{
			var rawTexVar = sm.GetShaderParameter("texture_albedo");
			rawAlbedoTexture = rawTexVar.VariantType != Variant.Type.Nil ? rawTexVar.As<Texture2D>() : null;
			var ormTexVar = sm.GetShaderParameter("texture_orm");
			ormTexture = ormTexVar.VariantType != Variant.Type.Nil ? ormTexVar.As<Texture2D>() : null;
			var normTexVar = sm.GetShaderParameter("texture_normal");
			normalTexture = normTexVar.VariantType != Variant.Type.Nil ? normTexVar.As<Texture2D>() : null;
			var emissTexVar = sm.GetShaderParameter("texture_emission");
			emissionTexture = emissTexVar.VariantType != Variant.Type.Nil ? emissTexVar.As<Texture2D>() : null;

			var albColVar = sm.GetShaderParameter("albedo_color");
			if (albColVar.VariantType != Variant.Type.Nil) albedoColor = albColVar.As<Color>();
			var emissColVar = sm.GetShaderParameter("emission_color");
			if (emissColVar.VariantType != Variant.Type.Nil) emissionColor = emissColVar.As<Color>();
			var emissEnVar = sm.GetShaderParameter("emission_energy");
			if (emissEnVar.VariantType != Variant.Type.Nil) emissionEnergy = emissEnVar.As<float>();
			var roughVar = sm.GetShaderParameter("roughness_value");
			if (roughVar.VariantType != Variant.Type.Nil) roughness = roughVar.As<float>();
			var metVar = sm.GetShaderParameter("metallic_value");
			if (metVar.VariantType != Variant.Type.Nil) metallic = metVar.As<float>();
			var specVar = sm.GetShaderParameter("specular_value");
			if (specVar.VariantType != Variant.Type.Nil) specular = specVar.As<float>();
			var uvScaleVar = sm.GetShaderParameter("uv1_scale");
			if (uvScaleVar.VariantType != Variant.Type.Nil) uv1Scale = uvScaleVar.As<Vector3>();
			var uvOffsetVar = sm.GetShaderParameter("uv1_offset");
			if (uvOffsetVar.VariantType != Variant.Type.Nil) uv1Offset = uvOffsetVar.As<Vector3>();
			var alphaBlendVar = sm.GetShaderParameter("use_alpha_blend");
			if (alphaBlendVar.VariantType != Variant.Type.Nil) useAlphaBlend = alphaBlendVar.As<bool>();
			var alphaScissorVar = sm.GetShaderParameter("use_alpha_scissor");
			if (alphaScissorVar.VariantType != Variant.Type.Nil) useAlphaScissor = alphaScissorVar.As<bool>();
			var alphaThresholdVar = sm.GetShaderParameter("alpha_scissor_threshold");
			if (alphaThresholdVar.VariantType != Variant.Type.Nil) alphaScissorThreshold = alphaThresholdVar.As<float>();
		}

		Texture2D albedoTexture = normalizeLuminance ? GetOrCreateNormalizedAlbedoTexture(rawAlbedoTexture) : rawAlbedoTexture;
		bool hasPlayerMask = CheckHasPlayerMask(ormTexture, sourceMaterial);

		ulong rawAlbedoId = rawAlbedoTexture != null ? rawAlbedoTexture.GetInstanceId() : 0;
		ulong ormId = ormTexture != null ? ormTexture.GetInstanceId() : 0;
		ulong normalId = normalTexture != null ? normalTexture.GetInstanceId() : 0;
		ulong emissionId = emissionTexture != null ? emissionTexture.GetInstanceId() : 0;

		string key = $"{rawAlbedoId}_{normalizeLuminance}_{ormId}_{hasPlayerMask}_{normalId}_{emissionId}_{albedoColor.ToHtml()}_{emissionColor.ToHtml()}_{emissionEnergy:F2}_{roughness:F2}_{metallic:F2}_{specular:F2}_{uv1Scale.X:F2}_{uv1Scale.Y:F2}_{uv1Offset.X:F2}_{uv1Offset.Y:F2}_{useAlphaBlend}_{useAlphaScissor}_{alphaScissorThreshold:F2}";

		if (_materialCache.TryGetValue(key, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var material = new ShaderMaterial
		{
			Shader = shader
		};

		if (albedoTexture != null)
		{
			material.SetShaderParameter("texture_albedo", albedoTexture);
		}
		if (ormTexture != null)
		{
			material.SetShaderParameter("texture_orm", ormTexture);
			material.SetShaderParameter("has_orm_texture", true);
			material.SetShaderParameter("has_player_mask", hasPlayerMask);
		}
		else
		{
			material.SetShaderParameter("has_orm_texture", false);
			material.SetShaderParameter("has_player_mask", false);
		}
		if (normalTexture != null)
		{
			material.SetShaderParameter("texture_normal", normalTexture);
			material.SetShaderParameter("has_normal_texture", true);
		}
		else
		{
			material.SetShaderParameter("has_normal_texture", false);
		}
		if (emissionTexture != null)
		{
			material.SetShaderParameter("texture_emission", emissionTexture);
			material.SetShaderParameter("has_emission_texture", true);
		}
		else
		{
			material.SetShaderParameter("has_emission_texture", false);
		}

		material.SetShaderParameter("use_alpha_blend", useAlphaBlend);
		material.SetShaderParameter("use_alpha_scissor", useAlphaScissor);
		material.SetShaderParameter("alpha_scissor_threshold", alphaScissorThreshold);

		material.SetShaderParameter("albedo_color", albedoColor);
		material.SetShaderParameter("emission_color", emissionColor);
		material.SetShaderParameter("emission_energy", emissionEnergy);
		material.SetShaderParameter("roughness_value", roughness);
		material.SetShaderParameter("metallic_value", metallic);
		material.SetShaderParameter("specular_value", specular);
		material.SetShaderParameter("uv1_scale", uv1Scale);
		material.SetShaderParameter("uv1_offset", uv1Offset);

		_materialCache[key] = material;
		return material;
	}

	private static bool IsExcludedMesh(GeometryInstance3D geomInst)
	{
		if (geomInst == null) return true;
		string nodeName = geomInst.Name.ToString();
		return nodeName.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
			|| nodeName.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
			|| nodeName.Contains("HoverRing", StringComparison.OrdinalIgnoreCase);
	}

	public static void ApplyPlayerColorShader(Node rootNode, Color playerColor, bool ignorePlayerColor = false, bool normalizeLuminance = true)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		ApplyPlayerColorShaderRecursive(rootNode, playerColor, ignorePlayerColor, normalizeLuminance);
	}

	private static void ApplyPlayerColorShaderRecursive(Node node, Color playerColor, bool ignorePlayerColor = false, bool normalizeLuminance = true)
	{
		if (node is MeshInstance3D meshInst)
		{
			if (!IsExcludedMesh(meshInst))
			{
				int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
				for (int i = 0; i < surfaceCount; i++)
				{
					Material srcMat = meshInst.GetSurfaceOverrideMaterial(i);
					if (srcMat == null && meshInst.Mesh != null)
					{
						srcMat = meshInst.Mesh.SurfaceGetMaterial(i);
					}

					if (srcMat is ShaderMaterial sm && sm.Shader == _sharedShader)
					{
						continue;
					}

					if (srcMat is BaseMaterial3D || srcMat == null)
					{
						var shaderMat = GetOrCreateShaderMaterial(srcMat, normalizeLuminance);
						meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
					}
				}

				if (meshInst.MaterialOverride is ShaderMaterial smOver && smOver.Shader == _sharedShader)
				{
					var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride, normalizeLuminance);
					meshInst.MaterialOverride = shaderMat;
				}

				meshInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
				meshInst.SetInstanceShaderParameter(_paramIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
				meshInst.SetInstanceShaderParameter(_paramNormalMode, 2.0f);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				ApplyPlayerColorShaderRecursive(childNode, playerColor, ignorePlayerColor, normalizeLuminance);
			}
		}
	}

	public static void RefreshShaderMaterialsForNode(Node rootNode, bool normalizeLuminance = true)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		RefreshShaderMaterialsRecursive(rootNode, normalizeLuminance);
	}

	private static void RefreshShaderMaterialsRecursive(Node node, bool normalizeLuminance)
	{
		if (node is MeshInstance3D meshInst && !IsExcludedMesh(meshInst))
		{
			int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
			for (int i = 0; i < surfaceCount; i++)
			{
				Material srcMat = meshInst.Mesh != null ? meshInst.Mesh.SurfaceGetMaterial(i) : null;
				if (srcMat == null) srcMat = meshInst.GetSurfaceOverrideMaterial(i);
				if (srcMat != null)
				{
					var shaderMat = GetOrCreateShaderMaterial(srcMat, normalizeLuminance);
					meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
				}
			}

			if (meshInst.MaterialOverride is ShaderMaterial smOver && smOver.Shader == _sharedShader)
			{
				var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride, normalizeLuminance);
				meshInst.MaterialOverride = shaderMat;
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				RefreshShaderMaterialsRecursive(childNode, normalizeLuminance);
			}
		}
	}

	public static void SetPlayerColor(Node rootNode, Color playerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetPlayerColorRecursive(rootNode, playerColor);
	}

	private static void SetPlayerColorRecursive(Node node, Color playerColor)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetPlayerColorRecursive(childNode, playerColor);
			}
		}
	}

	public static void SetIgnorePlayerColor(Node rootNode, bool ignorePlayerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetIgnorePlayerColorRecursive(rootNode, ignorePlayerColor);
	}

	private static void SetIgnorePlayerColorRecursive(Node node, bool ignorePlayerColor)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetIgnorePlayerColorRecursive(childNode, ignorePlayerColor);
			}
		}
	}

	public static void SetBrightnessAndTint(Node rootNode, float brightness, Color tint)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		if (MathF.Abs(brightness - 1.0f) < 0.001f && tint == new Color(1.0f, 1.0f, 1.0f)) return;
		SetBrightnessAndTintRecursive(rootNode, brightness, tint);
	}

	private static void SetBrightnessAndTintRecursive(Node node, float brightness, Color tint)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramModelBrightness, brightness);
				geomInst.SetInstanceShaderParameter(_paramModelColorTint, tint);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetBrightnessAndTintRecursive(childNode, brightness, tint);
			}
		}
	}

	public static void SetNormalMode(Node rootNode, float normalMode)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetNormalModeRecursive(rootNode, normalMode);
	}

	private static void SetNormalModeRecursive(Node node, float normalMode)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramNormalMode, normalMode);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetNormalModeRecursive(childNode, normalMode);
			}
		}
	}

	public static void ApplyBrightnessAndTintToAlbedoImage(Image img, float brightness, Color tint)
	{
		if (img == null) return;
		if (MathF.Abs(brightness - 1.0f) <= 0.001f && MathF.Abs(tint.R - 1.0f) <= 0.001f && MathF.Abs(tint.G - 1.0f) <= 0.001f && MathF.Abs(tint.B - 1.0f) <= 0.001f)
		{
			return;
		}

		if (img.GetFormat() != Image.Format.Rgba8)
		{
			img.Convert(Image.Format.Rgba8);
		}

		byte[] data = img.GetData();
		float multR = brightness * tint.R;
		float multG = brightness * tint.G;
		float multB = brightness * tint.B;

		for (int i = 0; i < data.Length; i += 4)
		{
			data[i]     = (byte)Math.Clamp((int)MathF.Round(data[i]     * multR), 0, 255);
			data[i + 1] = (byte)Math.Clamp((int)MathF.Round(data[i + 1] * multG), 0, 255);
			data[i + 2] = (byte)Math.Clamp((int)MathF.Round(data[i + 2] * multB), 0, 255);
		}

		img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data);
	}

	public static void ClearCache()
	{
		_materialCache.Clear();
		_normalizedAlbedoCache.Clear();
		_playerMaskCheckCache.Clear();
	}
}
