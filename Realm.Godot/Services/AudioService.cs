using Godot;

public class AudioService
{
	public void PlayWarningSound()
	{
		UIManager.Instance?.CallDeferred(nameof(UIManager.PlayWarningSound));
	}

	public void PlayClickSound()
	{
		UIManager.Instance?.CallDeferred(nameof(UIManager.PlayClickSound));
	}
}
