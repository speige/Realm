using Arch.Core;
using Godot;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

public class MapEditorTerrainStatusService
{
	private readonly World _ecsWorld;

	public MapEditorTerrainStatusService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	public string GetTerrainStatusString(Vector3 pos, string toolName, string activePlaceId)
	{
		string formattedToolName = toolName.ToUpper();
		if (!string.IsNullOrEmpty(activePlaceId))
		{
			formattedToolName += $" ({activePlaceId.ToUpper()})";
		}

		string status = $"ACTIVE TOOL: {formattedToolName} | Pos: {pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}";

		var worldQuery = new QueryDescription().WithAll<TerrainState>();
		Entity worldEntity = Entity.Null;
		_ecsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity))
		{
			ref var terrain = ref _ecsWorld.Get<TerrainState>(worldEntity);
			if (terrain.Heights != null && toolName.Equals("PaintPathing", StringComparison.OrdinalIgnoreCase))
			{
				float fx = pos.X / terrain.Spacing + (terrain.Width - 1) / 2.0f;
				float fz = pos.Z / terrain.Spacing + (terrain.Depth - 1) / 2.0f;
				int cx = Mathf.Clamp((int)Mathf.Round(fx), 0, terrain.Width - 1);
				int cz = Mathf.Clamp((int)Mathf.Round(fz), 0, terrain.Depth - 1);

				if (terrain.PathingCodes != null)
				{
					int code = terrain.PathingCodes[cx, cz];
					var layers = new List<string>();
					if ((code & EditableTerrain.PATHING_GROUND) != 0)
					{
						layers.Add("Ground");
					}
					if ((code & EditableTerrain.PATHING_FLYING) != 0)
					{
						layers.Add("Flying");
					}
					if ((code & EditableTerrain.PATHING_SHALLOW_WATER) != 0)
					{
						layers.Add("Shallow Water");
					}
					if ((code & EditableTerrain.PATHING_DEEP_WATER) != 0)
					{
						layers.Add("Deep Water");
					}
					if ((code & EditableTerrain.PATHING_UNPATHABLE) != 0)
					{
						layers.Add("Unpathable");
					}

					string layersStr = layers.Count > 0 ? string.Join(", ", layers) : "None";
					status += $" | Path: {layersStr}";
				}
			}
		}

		return status;
	}
}
