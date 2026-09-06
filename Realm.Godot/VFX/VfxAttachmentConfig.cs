using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Realm.Godot.VFX;

public enum VfxBlendMode
{
	Additive,
	AlphaBlend
}

public enum VfxPlacementMode
{
	SurfaceSnap,
	Free
}

public class VfxAttachmentConfig
{
	public string VfxId { get; set; } = "vfx_portal";
	public string Name { get; set; } = "VFX Effect";

	public VfxPrimitiveType PrimitiveType { get; set; } = VfxPrimitiveType.VortexDisc;
	public VfxBlendMode BlendMode { get; set; } = VfxBlendMode.Additive;
	public VfxPlacementMode PlacementMode { get; set; } = VfxPlacementMode.SurfaceSnap;
	public string TargetSocket { get; set; } = "Root";

	public string BaseTexture { get; set; } = "";
	public string NoiseTexture { get; set; } = "";

	public Vector2 BaseUvScroll { get; set; } = new Vector2(0.2f, 0.0f);
	public Vector2 BaseUvScale { get; set; } = Vector2.One;
	public Vector2 NoiseUvScroll { get; set; } = new Vector2(-0.15f, 0.25f);
	public Vector2 NoiseUvScale { get; set; } = Vector2.One;

	public float DistortionStrength { get; set; } = 0.2f;

	public string BaseColor { get; set; } = "#ff7711";
	public string SecondaryColor { get; set; } = "#aa1100";
	public string CoreColor { get; set; } = "#ffffff";
	public float EmissionBoost { get; set; } = 3.5f;
	public float CoreThreshold { get; set; } = 0.65f;

	public bool LuminanceToAlpha { get; set; } = true;
	public float LuminanceThreshold { get; set; } = 0.05f;
	public float LuminanceSmoothness { get; set; } = 0.08f;
	public bool UseGrayscale { get; set; } = true;
	public bool InvertMask { get; set; } = false;
	public float HighPassCutoff { get; set; } = 0.0f;

	public bool EnableRadialFalloff { get; set; } = true;
	public float RadialFalloffStart { get; set; } = 0.65f;
	public float RadialFalloffEnd { get; set; } = 1.0f;

	public bool EnableLengthFade { get; set; } = false;
	public float LengthFadeStart { get; set; } = 0.0f;
	public float LengthFadeEnd { get; set; } = 1.0f;
	public float ErosionProgress { get; set; } = 0.0f;

	public bool EnableFresnel { get; set; } = false;
	public float FresnelPower { get; set; } = 2.5f;
	public float FresnelIntensity { get; set; } = 1.5f;

	public bool EnableDepthFade { get; set; } = true;
	public float DepthFadeDistance { get; set; } = 0.35f;

	public float SurfaceNormalOffset { get; set; } = 0.02f;

	public Vector3 PositionOffset { get; set; } = Vector3.Zero;
	public Vector3 RotationOffset { get; set; } = Vector3.Zero;
	public Vector3 ScaleOffset { get; set; } = Vector3.One;

	public VfxAttachmentConfig Clone()
	{
		return new VfxAttachmentConfig
		{
			VfxId = VfxId,
			Name = Name,
			PrimitiveType = PrimitiveType,
			BlendMode = BlendMode,
			PlacementMode = PlacementMode,
			TargetSocket = TargetSocket,
			BaseTexture = BaseTexture,
			NoiseTexture = NoiseTexture,
			BaseUvScroll = BaseUvScroll,
			BaseUvScale = BaseUvScale,
			NoiseUvScroll = NoiseUvScroll,
			NoiseUvScale = NoiseUvScale,
			DistortionStrength = DistortionStrength,
			BaseColor = BaseColor,
			SecondaryColor = SecondaryColor,
			CoreColor = CoreColor,
			EmissionBoost = EmissionBoost,
			CoreThreshold = CoreThreshold,
			LuminanceToAlpha = LuminanceToAlpha,
			LuminanceThreshold = LuminanceThreshold,
			LuminanceSmoothness = LuminanceSmoothness,
			UseGrayscale = UseGrayscale,
			InvertMask = InvertMask,
			HighPassCutoff = HighPassCutoff,
			EnableRadialFalloff = EnableRadialFalloff,
			RadialFalloffStart = RadialFalloffStart,
			RadialFalloffEnd = RadialFalloffEnd,
			EnableLengthFade = EnableLengthFade,
			LengthFadeStart = LengthFadeStart,
			LengthFadeEnd = LengthFadeEnd,
			ErosionProgress = ErosionProgress,
			EnableFresnel = EnableFresnel,
			FresnelPower = FresnelPower,
			FresnelIntensity = FresnelIntensity,
			EnableDepthFade = EnableDepthFade,
			DepthFadeDistance = DepthFadeDistance,
			SurfaceNormalOffset = SurfaceNormalOffset,
			PositionOffset = PositionOffset,
			RotationOffset = RotationOffset,
			ScaleOffset = ScaleOffset
		};
	}

	public static VfxAttachmentConfig CreatePreset(string presetName)
	{
		return presetName.ToLowerInvariant() switch
		{
			"fire_blade" or "fire" => new VfxAttachmentConfig
			{
				VfxId = "vfx_fire_blade",
				Name = "Flaming Blade",
				PrimitiveType = VfxPrimitiveType.CrossQuad,
				BlendMode = VfxBlendMode.Additive,
				TargetSocket = "RightHand",
				BaseColor = "#ff5500",
				SecondaryColor = "#881100",
				CoreColor = "#ffffbb",
				EmissionBoost = 4.0f,
				CoreThreshold = 0.6f,
				DistortionStrength = 0.35f,
				BaseUvScroll = new Vector2(0.0f, -1.8f),
				NoiseUvScroll = new Vector2(0.4f, -1.2f),
				EnableLengthFade = true,
				LengthFadeStart = 0.7f,
				LengthFadeEnd = 1.0f,
				EnableRadialFalloff = false,
				EnableFresnel = false,
				EnableDepthFade = true,
				SurfaceNormalOffset = 0.01f
			},
			"arcane_portal" or "portal" => new VfxAttachmentConfig
			{
				VfxId = "vfx_arcane_portal",
				Name = "Arcane Portal",
				PrimitiveType = VfxPrimitiveType.VortexDisc,
				BlendMode = VfxBlendMode.Additive,
				TargetSocket = "Root",
				BaseColor = "#0088ff",
				SecondaryColor = "#5500aa",
				CoreColor = "#cceeff",
				EmissionBoost = 4.5f,
				CoreThreshold = 0.65f,
				DistortionStrength = 0.25f,
				BaseUvScroll = new Vector2(0.3f, 0.0f),
				NoiseUvScroll = new Vector2(-0.25f, 0.15f),
				EnableRadialFalloff = true,
				RadialFalloffStart = 0.75f,
				RadialFalloffEnd = 1.0f,
				EnableFresnel = false,
				EnableDepthFade = true,
				SurfaceNormalOffset = 0.02f
			},
			"lightning_blade" or "lightning" => new VfxAttachmentConfig
			{
				VfxId = "vfx_lightning_blade",
				Name = "Lightning Blade",
				PrimitiveType = VfxPrimitiveType.WeaponFin,
				BlendMode = VfxBlendMode.Additive,
				TargetSocket = "RightHand",
				BaseColor = "#00e5ff",
				SecondaryColor = "#0044bb",
				CoreColor = "#ffffff",
				EmissionBoost = 5.0f,
				CoreThreshold = 0.55f,
				DistortionStrength = 0.5f,
				BaseUvScroll = new Vector2(1.2f, -3.0f),
				NoiseUvScroll = new Vector2(-2.0f, 1.5f),
				EnableLengthFade = true,
				LengthFadeStart = 0.85f,
				LengthFadeEnd = 1.0f,
				EnableRadialFalloff = false,
				EnableFresnel = false,
				EnableDepthFade = true,
				SurfaceNormalOffset = 0.01f
			},
			"holy_shield" or "shield" or "aura" => new VfxAttachmentConfig
			{
				VfxId = "vfx_holy_shield",
				Name = "Divine Shield",
				PrimitiveType = VfxPrimitiveType.AuraCapsule,
				BlendMode = VfxBlendMode.Additive,
				TargetSocket = "Chest",
				BaseColor = "#ffcc00",
				SecondaryColor = "#bb7700",
				CoreColor = "#ffffff",
				EmissionBoost = 3.2f,
				CoreThreshold = 0.7f,
				DistortionStrength = 0.15f,
				BaseUvScroll = new Vector2(0.1f, 0.05f),
				NoiseUvScroll = new Vector2(-0.08f, -0.1f),
				EnableRadialFalloff = false,
				EnableLengthFade = false,
				EnableFresnel = true,
				FresnelPower = 2.2f,
				FresnelIntensity = 2.5f,
				EnableDepthFade = true,
				DepthFadeDistance = 0.4f,
				SurfaceNormalOffset = 0.01f
			},
			"frost_rune" or "frost" => new VfxAttachmentConfig
			{
				VfxId = "vfx_frost_rune",
				Name = "Frost Ground Rune",
				PrimitiveType = VfxPrimitiveType.GroundPlane,
				BlendMode = VfxBlendMode.Additive,
				TargetSocket = "Root",
				BaseColor = "#66ccff",
				SecondaryColor = "#114488",
				CoreColor = "#e6f7ff",
				EmissionBoost = 3.0f,
				CoreThreshold = 0.65f,
				DistortionStrength = 0.1f,
				BaseUvScroll = new Vector2(0.0f, 0.0f),
				NoiseUvScroll = new Vector2(0.05f, 0.08f),
				EnableRadialFalloff = true,
				RadialFalloffStart = 0.8f,
				RadialFalloffEnd = 1.0f,
				EnableFresnel = false,
				EnableDepthFade = true,
				SurfaceNormalOffset = 0.025f
			},
			"poison_ring" or "poison" => new VfxAttachmentConfig
			{
				VfxId = "vfx_poison_ring",
				Name = "Poison Barrier",
				PrimitiveType = VfxPrimitiveType.RibbonRing,
				BlendMode = VfxBlendMode.AlphaBlend,
				TargetSocket = "Root",
				BaseColor = "#22ee44",
				SecondaryColor = "#005511",
				CoreColor = "#ccffcc",
				EmissionBoost = 2.5f,
				CoreThreshold = 0.75f,
				DistortionStrength = 0.3f,
				BaseUvScroll = new Vector2(0.4f, 0.1f),
				NoiseUvScroll = new Vector2(-0.2f, 0.3f),
				EnableRadialFalloff = false,
				EnableLengthFade = true,
				LengthFadeStart = 0.0f,
				LengthFadeEnd = 0.9f,
				EnableFresnel = false,
				EnableDepthFade = true,
				SurfaceNormalOffset = 0.02f
			},
			"light_shaft" or "shaft" => new VfxAttachmentConfig
			{
				VfxId = "vfx_light_shaft",
				Name = "Pillar of Light",
				PrimitiveType = VfxPrimitiveType.LightShaft,
				BlendMode = VfxBlendMode.Additive,
				TargetSocket = "Root",
				BaseColor = "#ffea88",
				SecondaryColor = "#886611",
				CoreColor = "#ffffff",
				EmissionBoost = 3.5f,
				CoreThreshold = 0.7f,
				DistortionStrength = 0.2f,
				BaseUvScroll = new Vector2(0.05f, -0.5f),
				NoiseUvScroll = new Vector2(-0.1f, 0.4f),
				EnableRadialFalloff = false,
				EnableLengthFade = true,
				LengthFadeStart = 0.1f,
				LengthFadeEnd = 1.0f,
				EnableFresnel = false,
				EnableDepthFade = true,
				SurfaceNormalOffset = 0.01f
			},
			_ => new VfxAttachmentConfig()
		};
	}

	public static Dictionary<string, VfxAttachmentConfig> GetAllPresets()
	{
		return new Dictionary<string, VfxAttachmentConfig>(StringComparer.OrdinalIgnoreCase)
		{
			{ "vfx_fire_blade", CreatePreset("fire_blade") },
			{ "vfx_arcane_portal", CreatePreset("arcane_portal") },
			{ "vfx_lightning_blade", CreatePreset("lightning_blade") },
			{ "vfx_divine_shield", CreatePreset("divine_shield") },
			{ "vfx_frost_rune", CreatePreset("frost_rune") },
			{ "vfx_poison_ring", CreatePreset("poison_ring") },
			{ "vfx_light_shaft", CreatePreset("light_shaft") }
		};
	}
}

public static class VfxPresets
{
	public static VfxAttachmentConfig CreatePreset(string presetName) => VfxAttachmentConfig.CreatePreset(presetName);
	public static Dictionary<string, VfxAttachmentConfig> GetAllPresets() => VfxAttachmentConfig.GetAllPresets();
}
