using Godot;
using System.Collections.Generic;

public class SavedUnit
{
	public string Id;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
	public bool IsEnemy;
}

public class SavedProp
{
	public string Id;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
}

public class SavedDecal
{
	public string Id;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
}

public class MapStateSnapshot
{
	public int Width;
	public int Depth;
	public float[,] Heights;
	public int[,] PathingCodes;
	public Color[,] Colors;
	public bool WaterEnabled;
	public float WaterHeight;

	public float CameraBoundsLeft;
	public float CameraBoundsRight;
	public float CameraBoundsTop;
	public float CameraBoundsBottom;

	public List<SavedUnit> Units = new List<SavedUnit>();
	public List<SavedProp> Props = new List<SavedProp>();
	public List<SavedDecal> Decals = new List<SavedDecal>();

	public static MapStateSnapshot CreateSnapshot()
	{
		var host = GameHost.Instance;
		if (host == null || host.GroundTerrain == null) return null;

		var snapshot = new MapStateSnapshot();
		snapshot.Width = host.GroundTerrain.Width;
		snapshot.Depth = host.GroundTerrain.Depth;
		snapshot.Heights = (float[,])host.GroundTerrain.Heights.Clone();
		snapshot.PathingCodes = (int[,])host.GroundTerrain.PathingCodes.Clone();
		snapshot.Colors = (Color[,])host.GroundTerrain.Colors.Clone();
		snapshot.WaterEnabled = host.GroundTerrain.WaterEnabled;
		snapshot.WaterHeight = host.GroundTerrain.WaterHeight;

		snapshot.CameraBoundsLeft = host.EditorCameraBoundsLeft;
		snapshot.CameraBoundsRight = host.EditorCameraBoundsRight;
		snapshot.CameraBoundsTop = host.EditorCameraBoundsTop;
		snapshot.CameraBoundsBottom = host.EditorCameraBoundsBottom;

		foreach (var unit in host.AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				snapshot.Units.Add(new SavedUnit
				{
					Id = unit.UnitId,
					Position = unit.Position,
					RotationY = unit.RotationDegrees.Y,
					Scale = unit.Scale.X,
					IsEnemy = unit.IsEnemy
				});
			}
		}

		foreach (var prop in host.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				snapshot.Props.Add(new SavedProp
				{
					Id = prop.PropId,
					Position = prop.Position,
					RotationY = prop.RotationDegrees.Y,
					Scale = prop.Scale.X
				});
			}
		}

		foreach (var child in host.GetChildren())
		{
			if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
				snapshot.Decals.Add(new SavedDecal
				{
					Id = decalId,
					Position = decal.Position,
					RotationY = decal.RotationDegrees.Y,
					Scale = decal.Scale.X
				});
			}
		}

		return snapshot;
	}
}

public class MapResizeAction : IEditorAction
{
	private readonly MapStateSnapshot _before;
	private readonly MapStateSnapshot _after;

	public MapResizeAction(MapStateSnapshot before, MapStateSnapshot after)
	{
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		RestoreSnapshot(_before);
	}

	public void Redo()
	{
		RestoreSnapshot(_after);
	}

	private void RestoreSnapshot(MapStateSnapshot snapshot)
	{
		var host = GameHost.Instance;
		if (host == null || host.GroundTerrain == null) return;

		var unitsCopy = new List<Unit3D>(host.AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				host.DeleteNodeExternal(unit);
			}
		}
		host.SelectedUnits.Clear();
		host.AllUnits.Clear();

		var children = host.GetChildren();
		foreach (var child in children)
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				host.DeleteNodeExternal(prop);
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				host.DeleteNodeExternal(decal);
			}
		}

		host.GroundTerrain.RestoreTerrainFromSnapshot(
			snapshot.Width,
			snapshot.Depth,
			host.GroundTerrain.Spacing,
			snapshot.WaterHeight,
			snapshot.WaterEnabled,
			snapshot.Heights,
			snapshot.PathingCodes,
			snapshot.Colors
		);

		host.EditorCameraBoundsLeft = snapshot.CameraBoundsLeft;
		host.EditorCameraBoundsRight = snapshot.CameraBoundsRight;
		host.EditorCameraBoundsTop = snapshot.CameraBoundsTop;
		host.EditorCameraBoundsBottom = snapshot.CameraBoundsBottom;

		foreach (var u in snapshot.Units)
		{
			host.SpawnUnitExternal(u.Id, u.Position, u.IsEnemy, u.RotationY, u.Scale);
		}
		foreach (var p in snapshot.Props)
		{
			host.SpawnPropExternalWithParams(p.Id, p.Position, p.RotationY, p.Scale);
		}
		foreach (var d in snapshot.Decals)
		{
			host.SpawnDecalExternalWithParams(d.Id, d.Position, d.RotationY, d.Scale);
		}

		host.RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();
	}
}
