using Godot;
using System.Collections.Generic;

public static class PlayerColorConfig
{
	public const int NEUTRAL_PLAYER_INDEX = 9;

	public readonly struct PlayerColorEntry
	{
		public int Index { get; }
		public string Name { get; }
		public Color Color { get; }

		public PlayerColorEntry(int index, string name, Color color)
		{
			Index = index;
			Name = name;
			Color = color;
		}
	}

	public static readonly PlayerColorEntry[] Palette = new PlayerColorEntry[]
	{
		new PlayerColorEntry(0, "NEUTRAL_TAN", new Color(0.620f, 0.541f, 0.431f)),
		new PlayerColorEntry(1, "Crimson Red", new Color(0.850f, 0.100f, 0.100f)),
		new PlayerColorEntry(2, "Royal Blue", new Color(0.100f, 0.400f, 0.900f)),
		new PlayerColorEntry(3, "Emerald Green", new Color(0.100f, 0.750f, 0.200f)),
		new PlayerColorEntry(4, "Bright Yellow", new Color(0.950f, 0.850f, 0.100f)),
		new PlayerColorEntry(5, "Blaze Orange", new Color(0.950f, 0.450f, 0.050f)),
		new PlayerColorEntry(6, "Bright Cyan", new Color(0.000f, 0.800f, 0.800f)),
		new PlayerColorEntry(7, "Royal Purple", new Color(0.600f, 0.150f, 0.850f)),
		new PlayerColorEntry(8, "Hot Magenta", new Color(0.950f, 0.200f, 0.600f)),
		new PlayerColorEntry(9, "Lime", new Color(0.650f, 0.850f, 0.100f)),
		new PlayerColorEntry(10, "Deep Teal", new Color(0.000f, 0.550f, 0.550f)),
		new PlayerColorEntry(11, "Coral", new Color(0.950f, 0.400f, 0.350f)),
		new PlayerColorEntry(12, "Deep Cobalt", new Color(0.150f, 0.200f, 0.750f)),
		new PlayerColorEntry(13, "Maroon", new Color(0.550f, 0.050f, 0.200f)),
		new PlayerColorEntry(14, "Mint", new Color(0.300f, 0.750f, 0.600f)),
		new PlayerColorEntry(15, "Lavender", new Color(0.750f, 0.550f, 0.950f)),
		new PlayerColorEntry(16, "Amber", new Color(0.950f, 0.680f, 0.100f)),
		new PlayerColorEntry(17, "Forest Green", new Color(0.150f, 0.450f, 0.150f)),
		new PlayerColorEntry(18, "Dark Indigo", new Color(0.400f, 0.100f, 0.550f)),
		new PlayerColorEntry(19, "Sky Blue", new Color(0.000f, 0.650f, 0.950f)),
		new PlayerColorEntry(20, "Rose Ruby", new Color(0.800f, 0.250f, 0.400f)),
		new PlayerColorEntry(21, "Russet Brown", new Color(0.550f, 0.300f, 0.100f)),
		new PlayerColorEntry(22, "Olive Drab", new Color(0.400f, 0.500f, 0.200f)),
		new PlayerColorEntry(23, "Slate Blue-Grey", new Color(0.400f, 0.450f, 0.550f)),
		new PlayerColorEntry(24, "Silver", new Color(0.850f, 0.880f, 0.900f))
	};

	public static readonly List<Color> AvailableColors = new();

	static PlayerColorConfig()
	{
		for (int i = 0; i < Palette.Length; i++)
		{
			AvailableColors.Add(Palette[i].Color);
		}
	}

	public static Color GetColor(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < Palette.Length)
		{
			return Palette[playerIndex].Color;
		}
		return Palette[NEUTRAL_PLAYER_INDEX].Color;
	}

	public static string GetName(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < Palette.Length)
		{
			return Palette[playerIndex].Name;
		}
		return Palette[NEUTRAL_PLAYER_INDEX].Name;
	}

	public static int GetColorIndex(Color color)
	{
		for (int i = 0; i < Palette.Length; i++)
		{
			if (Palette[i].Color.IsEqualApprox(color)) return i;
		}
		return 0;
	}

	public static Color GetColorForOwner(int? playerIndex)
	{
		if (!playerIndex.HasValue || playerIndex.Value < 0 || playerIndex.Value >= Palette.Length)
		{
			return Palette[NEUTRAL_PLAYER_INDEX].Color;
		}
		return Palette[playerIndex.Value].Color;
	}
}
