using Godot;
using Realm.Ecs.Services;
using Arch.Core;

public class AudioService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	public AudioService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}
	public void PlayWarningSound()
	{
		UIManager.Instance?.CallDeferred(nameof(UIManager.PlayWarningSound));
	}

	public void PlayClickSound()
	{
		UIManager.Instance?.CallDeferred(nameof(UIManager.PlayClickSound));
	}
}
