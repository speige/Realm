namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds the state and configuration of the map editor, including block modes, camera boundary limits, and file status.
	/// </summary>
	internal struct EditorState
	{
		public bool BlockMode;
		public float BlockLevelHeight;
		public float CameraBoundsLeft;
		public float CameraBoundsRight;
		public float CameraBoundsTop;
		public float CameraBoundsBottom;
		public string SkyboxPath;
		public bool HasUnsavedChanges;

		public EditorState(
			bool blockMode,
			float blockLevelHeight,
			float cameraBoundsLeft,
			float cameraBoundsRight,
			float cameraBoundsTop,
			float cameraBoundsBottom,
			string skyboxPath,
			bool hasUnsavedChanges)
		{
			BlockMode = blockMode;
			BlockLevelHeight = blockLevelHeight;
			CameraBoundsLeft = cameraBoundsLeft;
			CameraBoundsRight = cameraBoundsRight;
			CameraBoundsTop = cameraBoundsTop;
			CameraBoundsBottom = cameraBoundsBottom;
			SkyboxPath = skyboxPath;
			HasUnsavedChanges = hasUnsavedChanges;
		}
	}
}
