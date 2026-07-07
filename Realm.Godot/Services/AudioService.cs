using Godot;
using Realm.Ecs.Services;

public class AudioService
{
	public AudioService(WorldAccessor ecsWorldAccessor)
	{
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
