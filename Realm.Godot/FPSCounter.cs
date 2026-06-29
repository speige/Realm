using Godot;

public partial class FPSCounter : Label
{
	public override void _Ready()
	{
		AddThemeColorOverride("font_color", new Color(0.15f, 0.65f, 1.0f));
		AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		AddThemeConstantOverride("outline_size", 4);
		AddThemeFontSizeOverride("font_size", 16);
	}

	public override void _Process(double delta)
	{
		Text = $"FPS: {Engine.GetFramesPerSecond():F0}";
	}
}
