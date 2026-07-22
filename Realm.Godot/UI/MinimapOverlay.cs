using Godot;

public partial class MinimapOverlay : Control
{
	public override void _Draw()
	{
		if (GameHost.Instance == null) return;

		var size = Size;


		if (InGameHUD.Instance != null && !InGameHUD.Instance.ShowMinimapTerrain)
		{
			DrawRect(new Rect2(Vector2.Zero, size), new Color(0.04f, 0.08f, 0.04f), true); // Deep radar green background
			Color radarGridColor = new Color(0.1f, 0.4f, 0.15f, 0.3f);
			

			DrawLine(new Vector2(size.X / 2f, 0), new Vector2(size.X / 2f, size.Y), radarGridColor, 1.0f);
			DrawLine(new Vector2(0, size.Y / 2f), new Vector2(size.X, size.Y / 2f), radarGridColor, 1.0f);
			

			DrawCircle(size / 2f, size.X * 0.45f, radarGridColor, false, 1.5f);
			DrawCircle(size / 2f, size.X * 0.3f, radarGridColor, false, 1.0f);
			DrawCircle(size / 2f, size.X * 0.15f, radarGridColor, false, 1.0f);


			DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 1.5f);
		}

		if (InGameHUD.Instance != null && InGameHUD.Instance.FogOfWarType != "visible")
		{
			float cellWidth = size.X / 32f;
			float cellHeight = size.Y / 32f;
			var grid = InGameHUD.Instance.FogGrid;
			for (int x = 0; x < 32; x++)
			{
				for (int z = 0; z < 32; z++)
				{
					byte val = grid[x, z];
					if (val == 0)
					{
						var rect = new Rect2(new Vector2(x * cellWidth, z * cellHeight), new Vector2(cellWidth, cellHeight));
						DrawRect(rect, new Color(0f, 0f, 0f, 1.0f), true);
					}
					else if (val == 1)
					{
						var rect = new Rect2(new Vector2(x * cellWidth, z * cellHeight), new Vector2(cellWidth, cellHeight));
						DrawRect(rect, new Color(0f, 0f, 0f, 0.33f), true);
					}
				}
			}
		}

		foreach (var unit in GameHost.Instance.AllUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;

			if (unit.IsEnemy && InGameHUD.Instance != null && InGameHUD.Instance.FogOfWarType != "visible")
			{
				int gx = (int)Mathf.Clamp((unit.GlobalPosition.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((unit.GlobalPosition.Z / 250f + 0.5f) * 32, 0, 31);
				if (InGameHUD.Instance.FogGrid[gx, gz] != 2)
				{
					continue;
				}
			}


			float xRatio = (unit.GlobalPosition.X / 250f) + 0.5f;
			float yRatio = (unit.GlobalPosition.Z / 250f) + 0.5f;

			xRatio = Mathf.Clamp(xRatio, 0f, 1f);
			yRatio = Mathf.Clamp(yRatio, 0f, 1f);

			Vector2 drawPos = new Vector2(xRatio * size.X, yRatio * size.Y);


			Color color = new Color(0.2f, 0.6f, 1.0f); // Default blue
			float iconSize = 5.0f;

			if (unit.IsEnemy)
			{
				if (unit.IsBuilding)
				{
					iconSize = 8.0f;
					color = new Color(0.9f, 0.1f, 0.1f); // Red Enemy Building
					var rect = new Rect2(drawPos - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
					DrawRect(rect, color, true);
					DrawRect(rect, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
				else
				{
					color = new Color(0.9f, 0.3f, 0.1f); // Orange-Red Enemy Unit
					DrawCircle(drawPos, iconSize, color);
					DrawCircle(drawPos, iconSize, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
			}
			else
			{
				if (unit.IsBuilding)
				{
					iconSize = 8.0f;
					if (unit.UnitId == "castle")
						color = new Color(0.9f, 0.7f, 0.1f); // Gold Castle
					else
						color = new Color(0.1f, 0.8f, 0.8f); // Cyan Spell Tower

					var rect = new Rect2(drawPos - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
					DrawRect(rect, color, true);
					DrawRect(rect, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
				else
				{
					if (unit.UnitId == "archer")
						color = new Color(0.2f, 0.8f, 0.3f); // Green Elf Archer
					else
						color = new Color(0.2f, 0.5f, 0.9f); // Blue Soldier

					DrawCircle(drawPos, iconSize, color);
					DrawCircle(drawPos, iconSize, new Color(0f, 0f, 0f, 0.6f), false, 1.0f); // dark outline
				}
			}


			if (unit.IsSelected)
			{
				Color selColor = unit.IsEnemy ? new Color(0.9f, 0.1f, 0.2f) : new Color(0.1f, 0.9f, 0.2f);
				DrawCircle(drawPos, iconSize + 2.5f, selColor, false, 1.2f);
			}
		}


		foreach (var ping in GameHost.Instance.ActivePings)
		{
			float xRatio = (ping.WorldPos.X / 250f) + 0.5f;
			float yRatio = (ping.WorldPos.Z / 250f) + 0.5f;

			xRatio = Mathf.Clamp(xRatio, 0f, 1f);
			yRatio = Mathf.Clamp(yRatio, 0f, 1f);

			Vector2 drawPos = new Vector2(xRatio * size.X, yRatio * size.Y);


			float pulse = Mathf.Sin(ping.LifeTime * 15f) * 0.5f + 1.0f;
			float radius = 12f * pulse;

			DrawCircle(drawPos, radius, new Color(1f, 0.1f, 0.1f, 0.5f), false, 2.0f);
			DrawCircle(drawPos, radius - 4f, new Color(1f, 0.1f, 0.1f, 0.2f), true);
		}
	}}