using System.Collections.Generic;
using System.Linq;

public class CoordinateAction : IEditorAction
{
	private readonly List<GameHost.EditorCoordinate> _oldCoordinates;
	private readonly List<GameHost.EditorCoordinate> _newCoordinates;

	public CoordinateAction(IEnumerable<GameHost.EditorCoordinate> oldCoordinates, IEnumerable<GameHost.EditorCoordinate> newCoordinates)
	{
		_oldCoordinates = oldCoordinates.ToList();
		_newCoordinates = newCoordinates.ToList();
	}

	public void Undo()
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorCoordinates.Clear();
			GameHost.Instance.EditorCoordinates.AddRange(_oldCoordinates);
			GameHost.Instance.RebuildAllCoordinatePersistentMeshes();
			GameHost.Instance.HideCoordinateSelectionOutline();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}
	}

	public void Redo()
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorCoordinates.Clear();
			GameHost.Instance.EditorCoordinates.AddRange(_newCoordinates);
			GameHost.Instance.RebuildAllCoordinatePersistentMeshes();
			GameHost.Instance.HideCoordinateSelectionOutline();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}
	}
}
