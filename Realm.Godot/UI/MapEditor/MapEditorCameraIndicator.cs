using Godot;

public partial class MapEditorCameraIndicator : Control
{
	private Vector2[] _points = new Vector2[4];
	private bool _hasPoints;

	public void SetPoints(Vector2[] points)
	{
		_points = points;
		_hasPoints = true;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (!_hasPoints || _points == null || _points.Length < 4)
		{
			return;
		}

		Color color = new Color(0f, 0.9f, 0.1f, 0.85f);
		float width = 2.0f;

		DrawLine(_points[0], _points[1], color, width);
		DrawLine(_points[1], _points[2], color, width);
		DrawLine(_points[2], _points[3], color, width);
		DrawLine(_points[3], _points[0], color, width);
	}
}
