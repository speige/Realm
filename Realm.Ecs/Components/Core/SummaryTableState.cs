namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the current multiplayer summary statistics table display state.
	/// </summary>
	internal struct SummaryTableState
	{
		public bool Visible;
		public string Title;
		public Dictionary<string, (string Damage, string Income, string Score)> Rows;

		public SummaryTableState(bool visible, string title, Dictionary<string, (string Damage, string Income, string Score)> rows)
		{
			Visible = visible;
			Title = title;
			Rows = rows;
		}
	}
}
