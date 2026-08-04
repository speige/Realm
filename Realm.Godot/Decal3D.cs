using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;

public partial class Decal3D : Decal
{
	public Entity Entity { get; set; }
	private string _decalId = "logo";
	
	private StaticBody3D _staticBody;
	private CollisionShape3D _collisionShape;

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
		_staticBody = new StaticBody3D();
		_staticBody.CollisionLayer = 1; // Or appropriate editor layer
		_staticBody.CollisionMask = 0;
		AddChild(_staticBody);

		_collisionShape = new CollisionShape3D();
		var box = new BoxShape3D();
		box.Size = new Vector3(Size.X, 0.5f, Size.Z);
		_collisionShape.Shape = box;
		_staticBody.AddChild(_collisionShape);

		SetProcess(false);
	}

	public void UpdateCollisionShape()
	{
		if (_collisionShape != null && _collisionShape.Shape is BoxShape3D box)
		{
			box.Size = new Vector3(Size.X, 0.5f, Size.Z);
		}
	}
}
