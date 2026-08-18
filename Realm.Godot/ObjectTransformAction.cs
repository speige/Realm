using Godot;
using Vector3 = Godot.Vector3;

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
	private readonly int _beforePlayer;
	private readonly int _afterPlayer;

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, bool beforeIsEnemy, bool afterIsEnemy)
		: this(targetNode, beforePos, afterPos, beforeRot, afterRot, beforeScale, afterScale, beforeIsEnemy, afterIsEnemy, (targetNode as Unit3D)?.Player ?? (beforeIsEnemy ? 1 : 0), (targetNode as Unit3D)?.Player ?? (afterIsEnemy ? 1 : 0))
	{
	}

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, bool beforeIsEnemy, bool afterIsEnemy, int beforePlayer, int afterPlayer)
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
		_beforePlayer = beforePlayer;
		_afterPlayer = afterPlayer;
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
				GameHost.Instance?.SetUnitPlayerExternal(unit, _beforePlayer);
			}
			else if (_targetNode is Prop3D prop)
			{
				PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
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
				GameHost.Instance?.SetUnitPlayerExternal(unit, _afterPlayer);
			}
			else if (_targetNode is Prop3D prop)
			{
				PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
			}
			MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
		}
	}
}