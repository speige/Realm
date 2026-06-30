using Godot;
using Vector3 = Godot.Vector3;

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