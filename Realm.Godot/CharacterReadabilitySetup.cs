using Godot;

/// <summary>
/// Attach this node as a direct child of a Unit3D scene root.
/// On _Ready it recursively patches every StandardMaterial3D / ORMMaterial3D
/// found in the character hierarchy so that models remain visually readable
/// under all Day/Night lighting conditions without modifying source assets.
///
/// Visual Layer Setup
///   • Sets every MeshInstance3D to render on BOTH Layer 1 (world) and
///     Layer 2 (character-only fill light).
///   • Add a second OmniLight3D or SpotLight3D in your scene with
///     LightCullMask = Layer 2 only, LightEnergy ≈ 0.35, a cool-white or
///     sky-blue colour. That light will exclusively boost character brightness
///     during dark phases without brightening the terrain.
/// </summary>
public partial class CharacterReadabilitySetup : Node3D
{
	/// <summary>
	/// The unit's primary accent colour. Used as the subtle emission tint so
	/// the model retains identity colour even in total darkness. Ally units
	/// typically use a warm gold; enemy units a deep crimson.
	/// </summary>
	[Export] public Color AccentColor { get; set; } = new Color(0.95f, 0.82f, 0.55f);

	/// <summary>
	/// Roughness target for all model surfaces. High values stop armour from
	/// mirror-reflecting a dark night skybox into a fully black surface.
	/// </summary>
	[Export] public float TargetRoughness { get; set; } = 0.80f;

	/// <summary>
	/// Specular intensity target. Kept very low to prevent high-contrast
	/// specular hotspots during the bright Sunset phase from blowing out detail.
	/// </summary>
	[Export] public float TargetSpecular { get; set; } = 0.10f;

	/// <summary>
	/// Emission energy. Low enough to be invisible in daylight, but prevents
	/// models from reaching absolute-black under any lighting configuration.
	/// </summary>
	[Export] public float BaseEmissionEnergy { get; set; } = 0.07f;

	public override void _Ready()
	{
		PatchMeshesRecursive(this);
	}

	private void PatchMeshesRecursive(Node node)
	{
		if (node is MeshInstance3D meshInstance)
		{
			AssignCharacterFillLightLayer(meshInstance);
			int surfaceCount = meshInstance.GetSurfaceOverrideMaterialCount();

			for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
			{
				Material sourceMaterial = meshInstance.GetActiveMaterial(surfaceIndex);
				if (sourceMaterial == null) continue;

				if (sourceMaterial is StandardMaterial3D standardMaterial)
				{
					var patchedMaterial = (StandardMaterial3D)standardMaterial.Duplicate();
					ApplyReadabilityPatches(patchedMaterial);
					meshInstance.SetSurfaceOverrideMaterial(surfaceIndex, patchedMaterial);
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			PatchMeshesRecursive(child);
		}
	}

	private void ApplyReadabilityPatches(StandardMaterial3D material)
	{
		// Prevent skybox reflections from turning the surface black at night.
		material.Roughness = TargetRoughness;
		material.MetallicSpecular = TargetSpecular;

		// Constant internal glow: barely perceptible in full daylight, but
		// guarantees the model never reaches 0-luminance at midnight.
		material.EmissionEnabled = true;
		material.Emission = AccentColor;
		material.EmissionEnergyMultiplier = BaseEmissionEnergy;
	}

	private static void AssignCharacterFillLightLayer(MeshInstance3D meshInstance)
	{
		// Godot visual layers are bit-flags.
		// Layer 1 = bit 0 (world geometry, camera default).
		// Layer 2 = bit 1 (character-only fill light target).
		// Setting both keeps the mesh visible to the main camera AND to the
		// dedicated fill light whose cull mask targets layer 2 exclusively.
		meshInstance.Layers = 0b11; // Layer 1 + Layer 2
	}
}
