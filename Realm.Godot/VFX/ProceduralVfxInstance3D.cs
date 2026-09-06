using Arch.Core;
using Godot;
using System;

namespace Realm.Godot.VFX;

public partial class ProceduralVfxInstance3D : Node3D
{
	private MeshInstance3D _meshInstance;
	private ShaderMaterial _material;
	private StaticBody3D _editorStaticBody;
	private CollisionShape3D _editorCollisionShape;

	private VfxAttachmentConfig _config = new();
	public VfxAttachmentConfig Config => _config;

	public Entity Entity { get; set; }
	public string VfxId { get; set; } = "vfx_primitive";
	public bool IsHovered { get; set; }
	public bool IsSelected { get; set; }

	public ProceduralVfxInstance3D()
	{
	}

	public ProceduralVfxInstance3D(VfxAttachmentConfig config)
	{
		Initialize(config);
	}

	public override void _Ready()
	{
		EnsureMeshAndMaterial();
		SetupEditorCollision();
	}

	private void EnsureMeshAndMaterial()
	{
		if (_meshInstance == null)
		{
			_meshInstance = new MeshInstance3D();
			_meshInstance.Name = "VfxPrimitiveMesh";
			_meshInstance.LodBias = 100.0f;
			AddChild(_meshInstance);
		}

		if (_material == null)
		{
			_material = VfxShaderManager.CreateMaterial(_config);
			_meshInstance.MaterialOverride = _material;
		}

		UpdateMesh();
	}

	private void UpdateMesh()
	{
		if (_meshInstance != null)
		{
			_meshInstance.Mesh = ProceduralVfxMeshGenerator.GetMesh(_config.PrimitiveType);
			UpdateCollisionShapeBounds();
		}
	}

	public void Initialize(VfxAttachmentConfig config)
	{
		_config = config?.Clone() ?? new VfxAttachmentConfig();
		VfxId = _config.VfxId;

		EnsureMeshAndMaterial();
		VfxShaderManager.ApplyConfigToMaterial(_material, _config);
		UpdateMesh();

		Position = _config.PositionOffset;
		RotationDegrees = _config.RotationOffset;
		Scale = _config.ScaleOffset;
	}

	public void UpdateConfig(VfxAttachmentConfig config)
	{
		if (config == null) return;
		bool meshChanged = _config.PrimitiveType != config.PrimitiveType;
		bool blendChanged = _config.BlendMode != config.BlendMode;

		_config = config.Clone();
		VfxId = _config.VfxId;

		EnsureMeshAndMaterial();

		if (blendChanged)
		{
			_material.Shader = VfxShaderManager.GetShader(_config.BlendMode);
		}

		VfxShaderManager.ApplyConfigToMaterial(_material, _config);

		if (meshChanged)
		{
			UpdateMesh();
		}
	}

	private void SetupEditorCollision()
	{
		bool isEditor = GameHost.Instance?.IsMapEditorMode == true;
		if (!isEditor) return;

		if (_editorStaticBody == null)
		{
			_editorStaticBody = new StaticBody3D();
			_editorStaticBody.Name = "EditorCollider";
			_editorStaticBody.CollisionLayer = 1u;
			_editorStaticBody.CollisionMask = 0;
			AddChild(_editorStaticBody);

			_editorCollisionShape = new CollisionShape3D();
			_editorCollisionShape.Name = "CollisionShape";
			_editorStaticBody.AddChild(_editorCollisionShape);

			UpdateCollisionShapeBounds();
		}
	}

	private void UpdateCollisionShapeBounds()
	{
		if (_editorCollisionShape == null || _meshInstance?.Mesh == null) return;

		Aabb bounds = _meshInstance.Mesh.GetAabb();
		Vector3 size = bounds.Size;
		size.X = Mathf.Max(size.X, 0.4f);
		size.Y = Mathf.Max(size.Y, 0.4f);
		size.Z = Mathf.Max(size.Z, 0.4f);

		var box = new BoxShape3D();
		box.Size = size;
		_editorCollisionShape.Shape = box;
		_editorCollisionShape.Position = bounds.GetCenter();
	}

	public void SetEditorCollisionEnabled(bool enabled)
	{
		if (_editorStaticBody != null && GodotObject.IsInstanceValid(_editorStaticBody))
		{
			_editorStaticBody.CollisionLayer = enabled ? 1u : 0u;
		}
	}
}
