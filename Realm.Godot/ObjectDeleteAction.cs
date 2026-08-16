using Godot;
using Vector3 = Godot.Vector3;

public class ObjectDeleteAction : IEditorAction
{
	private readonly string _objectType;
	private readonly string _objectId;
	private readonly Vector3 _position;
	private readonly float _rotationY;
	private readonly float _scale;
	private readonly bool _isEnemy;
	private readonly int _player;
	private Node _spawnedNode;

	public ObjectDeleteAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, bool isEnemy, Node deletedNode, int player = -1)
	{
		_objectType = objectType;
		_objectId = objectId;
		_position = position;
		_rotationY = rotationY;
		_scale = scale;
		_isEnemy = isEnemy;
		_player = player >= 0 ? player : (isEnemy ? 1 : 0);
		_spawnedNode = deletedNode;
	}

	public void Undo()
	{
		if (_objectType == "unit")
		{
			_spawnedNode = GameHost.Instance?.SpawnUnitExternal(_objectId, _position, _isEnemy, _rotationY, _scale, _player);
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
		else if (_objectType == "prop")
		{
			GameHost.Instance?.DeleteStaticPropAtPosition(_objectId, _position);
		}
	}
}