using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using System;

public partial class Decal3D : Decal
{
	public Entity Entity { get; set; }
	private string _decalId = "logo";
	
	private StaticBody3D _staticBody;
	private CollisionShape3D _collisionShape;

	private Texture2D[]? _albedoFrames;
	private Texture2D[]? _normalFrames;
	private int _columns = 1;
	private int _rows = 1;
	private float _fps = 12.0f;
	private bool _subframeBlend = true;
	private int _currentFrame = 0;
	private double _frameTimer = 0.0;
	private bool _normalEnabled = true;

	public int Columns => _columns;
	public int Rows => _rows;
	public float Fps => _fps;
	public bool SubframeBlend
	{
		get => _subframeBlend;
		set => _subframeBlend = value;
	}
	public bool IsAnimated => _columns > 1 || _rows > 1;
	public bool NormalEnabled
	{
		get => _normalEnabled;
		set => _normalEnabled = value;
	}

	public string DecalId
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<DecalIdentity>(Entity))
				return GameHost.Instance.EcsWorld.Get<DecalIdentity>(Entity).DecalId;
			return _decalId;
		}
		set
		{
			_decalId = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<DecalIdentity>(Entity))
					world.Set(Entity, new DecalIdentity(value));
				else
					world.Add(Entity, new DecalIdentity(value));
			}
		}
	}
	
	public override void _Ready()
	{
		CullMask = RuntimeTerrain.TerrainDecalCullMask;
		bool isEditor = GameHost.Instance?.IsMapEditorMode == true;
		_staticBody = new StaticBody3D();
		_staticBody.CollisionLayer = isEditor ? 1u : 0u;
		_staticBody.CollisionMask = 0;
		AddChild(_staticBody);

		_collisionShape = new CollisionShape3D();
		var box = new BoxShape3D();
		box.Size = new Vector3(Size.X, 0.5f, Size.Z);
		_collisionShape.Shape = box;
		_staticBody.AddChild(_collisionShape);

		SetProcess(_albedoFrames != null && _albedoFrames.Length > 1);
	}

	public void SetAnimationFrames(Texture2D[]? albedoFrames, Texture2D[]? normalFrames, int columns, int rows, float fps = 12.0f, bool subframeBlend = true)
	{
		_columns = Math.Max(1, columns);
		_rows = Math.Max(1, rows);
		_fps = fps > 0.001f ? fps : 12.0f;
		_subframeBlend = subframeBlend;
		_albedoFrames = albedoFrames;
		_normalFrames = normalFrames;
		_currentFrame = 0;
		_frameTimer = 0.0;

		if (_albedoFrames != null && _albedoFrames.Length > 1)
		{
			TextureAlbedo = _albedoFrames[0];
			if (_normalEnabled && _normalFrames != null && _normalFrames.Length > 0)
			{
				TextureNormal = _normalFrames[0];
			}
			SetProcess(true);
		}
		else
		{
			if (_albedoFrames != null && _albedoFrames.Length == 1)
			{
				TextureAlbedo = _albedoFrames[0];
			}
			if (_normalEnabled && _normalFrames != null && _normalFrames.Length == 1)
			{
				TextureNormal = _normalFrames[0];
			}
			SetProcess(false);
		}
	}

	public override void _Process(double delta)
	{
		if (_albedoFrames == null || _albedoFrames.Length <= 1)
		{
			SetProcess(false);
			return;
		}

		_frameTimer += delta;
		double frameDuration = 1.0 / (_fps > 0.001f ? _fps : 12.0f);
		if (_frameTimer >= frameDuration)
		{
			_frameTimer -= frameDuration;
			if (_frameTimer >= frameDuration)
			{
				_frameTimer %= frameDuration;
			}

			_currentFrame = (_currentFrame + 1) % _albedoFrames.Length;
			TextureAlbedo = _albedoFrames[_currentFrame];
			if (_normalEnabled && _normalFrames != null && _normalFrames.Length > _currentFrame)
			{
				TextureNormal = _normalFrames[_currentFrame];
			}
			else if (!_normalEnabled && TextureNormal != null)
			{
				TextureNormal = null;
			}

			if (TextureEmission != null)
			{
				TextureEmission = TextureAlbedo;
			}
		}
	}

	public void SetEditorCollisionEnabled(bool enabled)
	{
		if (_staticBody != null && GodotObject.IsInstanceValid(_staticBody))
		{
			_staticBody.CollisionLayer = enabled ? 1u : 0u;
		}
	}

	public void UpdateCollisionShape()
	{
		if (_collisionShape != null && _collisionShape.Shape is BoxShape3D box)
		{
			box.Size = new Vector3(Size.X, 0.5f, Size.Z);
		}
	}
}
