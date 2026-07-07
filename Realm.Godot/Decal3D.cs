using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;

public partial class Decal3D : Decal
{
	public Entity Entity { get; set; }
	private string _decalId = "logo";

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
}
