using Godot;
using Arch.Core;

public class AudioService
{
	private readonly World _ecsWorld;

	public AudioService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
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
