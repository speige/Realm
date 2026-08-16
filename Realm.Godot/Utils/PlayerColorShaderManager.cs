using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Godot.Utils;

public static class PlayerColorShaderManager
{
	private static Shader _sharedShader;
	private static readonly Dictionary<string, ShaderMaterial> _materialCache = new(StringComparer.Ordinal);
	private static readonly StringName _paramPlayerColor = new("player_color");
	private static readonly StringName _paramModelBrightness = new("model_brightness");
	private static readonly StringName _paramModelColorTint = new("model_color_tint");

	public static Shader GetOrCreateShader()
	{
		if (_sharedShader != null && GodotObject.IsInstanceValid(_sharedShader))
		{
			return _sharedShader;
		}

		if (ResourceLoader.Exists("res://Assets/shaders/player_color_spatial.gdshader"))
		{
			_sharedShader = GD.Load<Shader>("res://Assets/shaders/player_color_spatial.gdshader");
			if (_sharedShader != null) return _sharedShader;
		}

		_sharedShader = new Shader
		{
			Code = @"shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D texture_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_orm : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool has_orm_texture = false;
uniform sampler2D texture_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool has_normal_texture = false;
uniform float normal_scale = 1.0;
uniform sampler2D texture_emission : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool has_emission_texture = false;

uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 1.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 1.0;
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;
uniform float specular_value : hint_range(0.0, 1.0) = 0.5;

uniform bool use_alpha_blend = false;
uniform bool use_alpha_scissor = false;
uniform float alpha_scissor_threshold : hint_range(0.0, 1.0) = 0.5;

uniform vec3 uv1_scale = vec3(1.0, 1.0, 1.0);
uniform vec3 uv1_offset = vec3(0.0, 0.0, 0.0);

instance uniform vec4 player_color : source_color = vec4(0.620, 0.541, 0.431, 1.0);
instance uniform float model_brightness : hint_range(0.0, 2.0) = 1.0;
instance uniform vec4 model_color_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

varying vec2 base_uv;

void vertex() {
	base_uv = UV * uv1_scale.xy + uv1_offset.xy;
}

vec3 perturb_normal_cotangent(vec3 view_normal, vec3 view_pos, vec2 uv, vec3 normal_sample, float scale) {
	vec3 map_n = normal_sample * 2.0 - 1.0;
	map_n.xy *= scale;
	
	vec3 dp1 = dFdx(view_pos);
	vec3 dp2 = dFdy(view_pos);
	vec2 duv1 = dFdx(uv);
	vec2 duv2 = dFdy(uv);
	
	vec3 dp2perp = cross(dp2, view_normal);
	vec3 dp1perp = cross(view_normal, dp1);
	
	vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
	vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;
	
	float invmax = inversesqrt(max(dot(t, t), dot(b, b)));
	mat3 tbn = mat3(t * invmax, b * invmax, view_normal);
	return normalize(tbn * map_n);
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}

	vec4 albedo_tex = texture(texture_albedo, base_uv);
	vec3 base_albedo = albedo_tex.rgb * albedo_color.rgb;
	
	float player_mask = 0.0;
	float roughness = roughness_value;
	float metallic = metallic_value;

	if (has_orm_texture) {
		vec4 orm_tex = texture(texture_orm, base_uv);
		player_mask = clamp(orm_tex.r, 0.0, 1.0);
		roughness *= orm_tex.g;
		metallic *= orm_tex.b;
	}

	vec3 final_color = mix(base_albedo, player_color.rgb, player_mask);
	final_color *= model_brightness * model_color_tint.rgb;

	ALBEDO = final_color;

	if (use_alpha_scissor) {
		ALPHA_SCISSOR_THRESHOLD = alpha_scissor_threshold;
		ALPHA = albedo_tex.a * albedo_color.a;
	} else if (use_alpha_blend) {
		ALPHA = albedo_tex.a * albedo_color.a;
	}

	ROUGHNESS = roughness;
	METALLIC = metallic;
	SPECULAR = specular_value;

	if (has_normal_texture) {
		vec3 normal_sample = texture(texture_normal, base_uv).rgb;
		NORMAL = perturb_normal_cotangent(NORMAL, VERTEX, base_uv, normal_sample, normal_scale);
	}

	if (has_emission_texture) {
		EMISSION = texture(texture_emission, base_uv).rgb * emission_color.rgb * emission_energy;
	} else if (emission_color.r > 0.0 || emission_color.g > 0.0 || emission_color.b > 0.0) {
		EMISSION = emission_color.rgb * emission_energy;
	}
}"
		};

		return _sharedShader;
	}

	public static ShaderMaterial GetOrCreateShaderMaterial(Material sourceMaterial)
	{
		var shader = GetOrCreateShader();

		Texture2D albedoTexture = null;
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
			albedoTexture = ormMat.AlbedoTexture;
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
			albedoTexture = baseMat.AlbedoTexture;
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

		ulong albedoId = albedoTexture != null ? albedoTexture.GetInstanceId() : 0;
		ulong ormId = ormTexture != null ? ormTexture.GetInstanceId() : 0;
		ulong normalId = normalTexture != null ? normalTexture.GetInstanceId() : 0;
		ulong emissionId = emissionTexture != null ? emissionTexture.GetInstanceId() : 0;

		string key = $"{albedoId}_{ormId}_{normalId}_{emissionId}_{albedoColor.ToHtml()}_{emissionColor.ToHtml()}_{emissionEnergy:F2}_{roughness:F2}_{metallic:F2}_{specular:F2}_{uv1Scale.X:F2}_{uv1Scale.Y:F2}_{uv1Offset.X:F2}_{uv1Offset.Y:F2}_{useAlphaBlend}_{useAlphaScissor}_{alphaScissorThreshold:F2}";

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
		}
		else
		{
			material.SetShaderParameter("has_orm_texture", false);
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

	public static void ApplyPlayerColorShader(Node rootNode, Color playerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		ApplyPlayerColorShaderRecursive(rootNode, playerColor);
	}

	private static void ApplyPlayerColorShaderRecursive(Node node, Color playerColor)
	{
		if (node is MeshInstance3D meshInst)
		{
			string nodeName = meshInst.Name.ToString();
			if (!nodeName.StartsWith("_selection") && !nodeName.StartsWith("_hover") && !nodeName.StartsWith("BrushIndicator") && !nodeName.StartsWith("DropShadow"))
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
						var shaderMat = GetOrCreateShaderMaterial(srcMat);
						meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
					}
				}

				if (meshInst.MaterialOverride != null && !(meshInst.MaterialOverride is ShaderMaterial smOver && smOver.Shader == _sharedShader))
				{
					var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride);
					meshInst.MaterialOverride = shaderMat;
				}

				meshInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				ApplyPlayerColorShaderRecursive(childNode, playerColor);
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
		if (node is MeshInstance3D meshInst)
		{
			string nodeName = meshInst.Name.ToString();
			if (!nodeName.StartsWith("_selection") && !nodeName.StartsWith("_hover") && !nodeName.StartsWith("BrushIndicator") && !nodeName.StartsWith("DropShadow"))
			{
				meshInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
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

	public static void SetBrightnessAndTint(Node rootNode, float brightness, Color tint)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetBrightnessAndTintRecursive(rootNode, brightness, tint);
	}

	private static void SetBrightnessAndTintRecursive(Node node, float brightness, Color tint)
	{
		if (node is MeshInstance3D meshInst)
		{
			string nodeName = meshInst.Name.ToString();
			if (!nodeName.StartsWith("_selection") && !nodeName.StartsWith("_hover") && !nodeName.StartsWith("BrushIndicator") && !nodeName.StartsWith("DropShadow"))
			{
				meshInst.SetInstanceShaderParameter(_paramModelBrightness, brightness);
				meshInst.SetInstanceShaderParameter(_paramModelColorTint, tint);
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

	public static void ClearCache()
	{
		_materialCache.Clear();
	}
}
