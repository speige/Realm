using Godot;
using System;
using System.Collections.Generic;

public interface IEditorAction
{
	void Undo();
	void Redo();
}

public class TerrainModifyAction : IEditorAction
{
	private readonly float[,] _beforeHeights;
	private readonly float[,] _afterHeights;
	private readonly Color[,] _beforeColors;
	private readonly Color[,] _afterColors;

	public TerrainModifyAction(float[,] beforeHeights, float[,] afterHeights, Color[,] beforeColors, Color[,] afterColors)
	{
		_beforeHeights = beforeHeights;
		_afterHeights = afterHeights;
		_beforeColors = beforeColors;
		_afterColors = afterColors;
	}

	public void Undo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		Array.Copy(_beforeHeights, GameHost.Instance.GroundTerrain.Heights, _beforeHeights.Length);
		Array.Copy(_beforeColors, GameHost.Instance.GroundTerrain.Colors, _beforeColors.Length);
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();
		GameHost.Instance.AlignAllEntitiesToTerrainExternal();
		GameHost.Instance.RebuildGridOverlayMeshExternal();
	}

	public void Redo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		Array.Copy(_afterHeights, GameHost.Instance.GroundTerrain.Heights, _afterHeights.Length);
		Array.Copy(_afterColors, GameHost.Instance.GroundTerrain.Colors, _afterColors.Length);
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();
		GameHost.Instance.AlignAllEntitiesToTerrainExternal();
		GameHost.Instance.RebuildGridOverlayMeshExternal();
	}
}

public class CompositeAction : IEditorAction
{
	private readonly List<IEditorAction> _actions = new List<IEditorAction>();

	public CompositeAction(IEnumerable<IEditorAction> actions)
	{
		_actions.AddRange(actions);
	}

	public void Undo()
	{
		for (int i = _actions.Count - 1; i >= 0; i--)
		{
			_actions[i].Undo();
		}
	}

	public void Redo()
	{
		for (int i = 0; i < _actions.Count; i++)
		{
			_actions[i].Redo();
		}
	}
}

public class ObjectSpawnAction : IEditorAction
{
	private readonly string _objectType;
	private readonly string _objectId;
	private readonly Vector3 _position;
	private readonly float _rotationY;
	private readonly float _scale;
	private readonly bool _isEnemy;
	private Node _spawnedNode;

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, bool isEnemy, Node spawnedNode)
	{
		_objectType = objectType;
		_objectId = objectId;
		_position = position;
		_rotationY = rotationY;
		_scale = scale;
		_isEnemy = isEnemy;
		_spawnedNode = spawnedNode;
	}

	public void Undo()
	{
		if (GodotObject.IsInstanceValid(_spawnedNode))
		{
			GameHost.Instance?.DeleteNodeExternal(_spawnedNode);
		}
	}

	public void Redo()
	{
		if (_objectType == "unit")
		{
			_spawnedNode = GameHost.Instance?.SpawnUnitExternal(_objectId, _position, _isEnemy, _rotationY, _scale);
		}
		else if (_objectType == "prop")
		{
			_spawnedNode = GameHost.Instance?.SpawnPropExternalWithParams(_objectId, _position, _rotationY, _scale);
		}
		else if (_objectType == "decal")
		{
			_spawnedNode = GameHost.Instance?.SpawnDecalExternalWithParams(_objectId, _position, _rotationY, _scale);
		}
	}
}

public class ObjectDeleteAction : IEditorAction
{
	private readonly string _objectType;
	private readonly string _objectId;
	private readonly Vector3 _position;
	private readonly float _rotationY;
	private readonly float _scale;
	private readonly bool _isEnemy;
	private Node _spawnedNode;

	public ObjectDeleteAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, bool isEnemy, Node deletedNode)
	{
		_objectType = objectType;
		_objectId = objectId;
		_position = position;
		_rotationY = rotationY;
		_scale = scale;
		_isEnemy = isEnemy;
		_spawnedNode = deletedNode;
	}

	public void Undo()
	{
		if (_objectType == "unit")
		{
			_spawnedNode = GameHost.Instance?.SpawnUnitExternal(_objectId, _position, _isEnemy, _rotationY, _scale);
		}
		else if (_objectType == "prop")
		{
			_spawnedNode = GameHost.Instance?.SpawnPropExternalWithParams(_objectId, _position, _rotationY, _scale);
		}
		else if (_objectType == "decal")
		{
			_spawnedNode = GameHost.Instance?.SpawnDecalExternalWithParams(_objectId, _position, _rotationY, _scale);
		}
	}

	public void Redo()
	{
		if (GodotObject.IsInstanceValid(_spawnedNode))
		{
			GameHost.Instance?.DeleteNodeExternal(_spawnedNode);
		}
	}
}

public class ObjectTransformAction : IEditorAction
{
	private readonly Node3D _targetNode;
	private readonly Vector3 _beforePos;
	private readonly Vector3 _afterPos;
	private readonly Vector3 _beforeRot;
	private readonly Vector3 _afterRot;
	private readonly Vector3 _beforeScale;
	private readonly Vector3 _afterScale;
	private readonly bool _beforeIsEnemy;
	private readonly bool _afterIsEnemy;

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, bool beforeIsEnemy, bool afterIsEnemy)
	{
		_targetNode = targetNode;
		_beforePos = beforePos;
		_afterPos = afterPos;
		_beforeRot = beforeRot;
		_afterRot = afterRot;
		_beforeScale = beforeScale;
		_afterScale = afterScale;
		_beforeIsEnemy = beforeIsEnemy;
		_afterIsEnemy = afterIsEnemy;
	}

	public void Undo()
	{
		if (GodotObject.IsInstanceValid(_targetNode))
		{
			_targetNode.Position = _beforePos;
			_targetNode.RotationDegrees = _beforeRot;
			_targetNode.Scale = _beforeScale;
			if (_targetNode is Unit3D unit)
			{
				GameHost.Instance?.SetUnitTeamExternal(unit, _beforeIsEnemy);
			}
			MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
		}
	}

	public void Redo()
	{
		if (GodotObject.IsInstanceValid(_targetNode))
		{
			_targetNode.Position = _afterPos;
			_targetNode.RotationDegrees = _afterRot;
			_targetNode.Scale = _afterScale;
			if (_targetNode is Unit3D unit)
			{
				GameHost.Instance?.SetUnitTeamExternal(unit, _afterIsEnemy);
			}
			MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
		}
	}
}

public static class EditorHistoryManager
{
	private static readonly Stack<IEditorAction> _undoStack = new Stack<IEditorAction>();
	private static readonly Stack<IEditorAction> _redoStack = new Stack<IEditorAction>();

	public static void RecordAction(IEditorAction action)
	{
		_undoStack.Push(action);
		_redoStack.Clear();
	}

	public static void Undo()
	{
		if (_undoStack.Count > 0)
		{
			var action = _undoStack.Pop();
			action.Undo();
			_redoStack.Push(action);
		}
	}

	public static void Redo()
	{
		if (_redoStack.Count > 0)
		{
			var action = _redoStack.Pop();
			action.Redo();
			_undoStack.Push(action);
		}
	}

	public static void Clear()
	{
		_undoStack.Clear();
		_redoStack.Clear();
	}
}

public class MapSaveData
{
	public float[] Heights { get; set; }
	public string[] Colors { get; set; }
	public List<UnitSaveData> Units { get; set; }
	public List<PropSaveData> Props { get; set; }
	public List<DecalSaveData> Decals { get; set; }
	public bool? WaterEnabled { get; set; }
	public float? WaterHeight { get; set; }
	public bool? BlockMode { get; set; }
	public float? BlockLevelHeight { get; set; }
	public bool? WC3BlockMode { get; set; }
	public float? WC3LevelHeight { get; set; }
}

public class UnitSaveData
{
	public string UnitId { get; set; }
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; }
	public bool IsEnemy { get; set; }
}

public class PropSaveData
{
	public string PropId { get; set; }
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; }
}

public class DecalSaveData
{
	public string DecalId { get; set; }
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; }
}
