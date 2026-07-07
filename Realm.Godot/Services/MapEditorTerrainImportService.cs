using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

public class MapEditorTerrainImportService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	public MapEditorTerrainImportService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool ImportTerrain(
		Entity worldEntity,
		string selectedPath,
		out float[,] smoothedHeights,
		out Color[,] colors,
		out List<(float X, float Y, float Z, float Rot, float Scale)> treePositions)
	{
		smoothedHeights = null;
		colors = null;
		treePositions = new List<(float X, float Y, float Z, float Rot, float Scale)>();

		if (!_ecsWorld.TryGet<TerrainState>(worldEntity, out var terrain))
		{
			return false;
		}

		var img = Image.LoadFromFile(selectedPath);
		if (img == null)
		{
			return false;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float spacing = terrain.Spacing;

		float[,] heights = new float[width, depth];
		colors = new Color[width, depth];
		bool[,] isTreeColored = new bool[width, depth];

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				float srcX = (gx / (float)(width - 1)) * (img.GetWidth() - 1);
				float srcZ = (gz / (float)(depth - 1)) * (img.GetHeight() - 1);

				int x0 = (int)MathF.Floor(srcX);
				int x1 = Math.Min(x0 + 1, img.GetWidth() - 1);
				int z0 = (int)MathF.Floor(srcZ);
				int z1 = Math.Min(z0 + 1, img.GetHeight() - 1);

				float tx = srcX - x0;
				float tz = srcZ - z0;

				Color p00 = img.GetPixel(x0, z0);
				Color p10 = img.GetPixel(x1, z0);
				Color p01 = img.GetPixel(x0, z1);
				Color p11 = img.GetPixel(x1, z1);

				float r = (1f - tx) * (1f - tz) * p00.R + tx * (1f - tz) * p10.R + (1f - tx) * tz * p01.R + tx * tz * p11.R;
				float g = (1f - tx) * (1f - tz) * p00.G + tx * (1f - tz) * p10.G + (1f - tx) * tz * p01.G + tx * tz * p11.G;
				float b = (1f - tx) * (1f - tz) * p00.B + tx * (1f - tz) * p10.B + (1f - tx) * tz * p01.B + tx * tz * p11.B;

				string type = "grass";
				if (b > r + 0.06f && b > g + 0.06f)
				{
					type = "water";
				}
				else if (g > r + 0.04f && g > b + 0.04f)
				{
					if (r < 0.4f && g < 0.5f && b < 0.4f)
					{
						type = "forest";
					}
					else
					{
						type = "grass";
					}
				}
				else if (r > g + 0.06f && r > b + 0.1f && g > b)
				{
					type = "cliff";
				}
				else if (MathF.Abs(r - g) < 0.08f && MathF.Abs(g - b) < 0.08f && MathF.Abs(r - b) < 0.08f && r > 0.2f)
				{
					type = "stone";
				}
				else
				{
					float max = Math.Max(r, Math.Max(g, b));
					if (max == b) type = "water";
					else if (max == g) type = "grass";
					else if (max == r && g > b) type = "cliff";
					else type = "stone";
				}

				float h = 0.0f;
				Color c = new Color(0.2f, 0.6f, 0.2f);
				if (type == "water")
				{
					h = -2.0f;
					c = new Color(0.0f, 0.33f, 0.7f);
				}
				else if (type == "grass")
				{
					h = 0.0f;
					c = new Color(0.2f, 0.6f, 0.2f);
				}
				else if (type == "forest")
				{
					h = 0.0f;
					c = new Color(0.16f, 0.48f, 0.16f);
					isTreeColored[gx, gz] = true;
				}
				else if (type == "cliff")
				{
					h = 4.0f;
					c = new Color(0.54f, 0.35f, 0.17f);
				}
				else if (type == "stone")
				{
					h = 0.0f;
					c = new Color(0.5f, 0.5f, 0.5f);
				}

				heights[gx, gz] = h;
				colors[gx, gz] = c;
			}
		}

		smoothedHeights = new float[width, depth];
		int blurRadius = 2;
		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				float sum = 0f;
				int count = 0;
				for (int dz = -blurRadius; dz <= blurRadius; dz++)
				{
					for (int dx = -blurRadius; dx <= blurRadius; dx++)
					{
						int nx = gx + dx;
						int nz = gz + dz;
						if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
						{
							sum += heights[nx, nz];
							count++;
						}
					}
				}
				smoothedHeights[gx, gz] = sum / count;
			}
		}

		var random = new Random();
		bool[,] visited = new bool[width, depth];

		float Noise2D(float x, float z)
		{
			float val = MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453123f;
			return val - MathF.Floor(val);
		}

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				if (isTreeColored[gx, gz] && !visited[gx, gz])
				{
					var blob = new List<Vector2I>();
					var queue = new Queue<Vector2I>();
					var start = new Vector2I(gx, gz);
					queue.Enqueue(start);
					visited[gx, gz] = true;

					while (queue.Count > 0)
					{
						var curr = queue.Dequeue();
						blob.Add(curr);

						Vector2I[] neighbors = new Vector2I[]
						{
							new Vector2I(curr.X + 1, curr.Y),
							new Vector2I(curr.X - 1, curr.Y),
							new Vector2I(curr.X, curr.Y + 1),
							new Vector2I(curr.X, curr.Y - 1)
						};

						foreach (var n in neighbors)
						{
							if (n.X >= 0 && n.X < width && n.Y >= 0 && n.Y < depth)
							{
								if (isTreeColored[n.X, n.Y] && !visited[n.X, n.Y])
								{
									visited[n.X, n.Y] = true;
									queue.Enqueue(n);
								}
							}
						}
					}

					int size = blob.Count;
					float baseDensity = 0.15f;
					if (size > 15) baseDensity = 0.35f;
					if (size > 50) baseDensity = 0.55f;

					foreach (var cell in blob)
					{
						if (smoothedHeights[cell.X, cell.Y] >= 0.0f && Noise2D(cell.X, cell.Y) < baseDensity)
						{
							float offsetX = (random.NextSingle() - 0.5f) * 1.5f;
							float offsetZ = (random.NextSingle() - 0.5f) * 1.5f;
							float worldX = (cell.X - (width - 1) / 2.0f) * spacing + offsetX;
							float worldZ = (cell.Y - (depth - 1) / 2.0f) * spacing + offsetZ;
							float hValue = smoothedHeights[cell.X, cell.Y];
							float rot = random.NextSingle() * 360f;
							float scale = 0.8f + random.NextSingle() * 0.4f;

							treePositions.Add((worldX, hValue, worldZ, rot, scale));
						}
					}
				}
			}
		}

		return true;
	}
}
