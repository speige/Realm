using Godot;
using System;
using System.Text.Json.Nodes;

public partial class SpritesheetAssetEditDialog : FloatingDialogBase
{
	private string _sheetFileName = "";
	private int _columns = 4;
	private int _rows = 4;
	private Action<int, int> _onApplied;

	private SpinBox _spinCols;
	private SpinBox _spinRows;

	public SpritesheetAssetEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit VFX Spritesheet"), new Vector2(340, 240))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("SPRITESHEET GRID DIMENSIONS"), new Color(0.35f, 0.75f, 0.9f));

		// COLUMNS
		var rowCols = new HBoxContainer();
		rowCols.AddThemeConstantOverride("separation", 8);
		var lblCols = new Label();
		lblCols.Text = TranslationServer.Translate("Columns:");
		lblCols.CustomMinimumSize = new Vector2(100, 0);
		lblCols.AddThemeFontSizeOverride("font_size", 11);
		rowCols.AddChild(lblCols);

		_spinCols = new SpinBox();
		_spinCols.MinValue = 1;
		_spinCols.MaxValue = 32;
		_spinCols.Step = 1;
		_spinCols.Value = 4;
		_spinCols.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_spinCols.ValueChanged += (val) => _columns = (int)val;
		_spinCols.GetLineEdit().TextChanged += (text) =>
		{
			if (int.TryParse(text, out int v))
			{
				_columns = Math.Clamp(v, (int)_spinCols.MinValue, (int)_spinCols.MaxValue);
			}
		};
		rowCols.AddChild(_spinCols);
		contentVBox.AddChild(rowCols);

		// ROWS
		var rowRows = new HBoxContainer();
		rowRows.AddThemeConstantOverride("separation", 8);
		var lblRows = new Label();
		lblRows.Text = TranslationServer.Translate("Rows:");
		lblRows.CustomMinimumSize = new Vector2(100, 0);
		lblRows.AddThemeFontSizeOverride("font_size", 11);
		rowRows.AddChild(lblRows);

		_spinRows = new SpinBox();
		_spinRows.MinValue = 1;
		_spinRows.MaxValue = 32;
		_spinRows.Step = 1;
		_spinRows.Value = 4;
		_spinRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_spinRows.ValueChanged += (val) => _rows = (int)val;
		_spinRows.GetLineEdit().TextChanged += (text) =>
		{
			if (int.TryParse(text, out int v))
			{
				_rows = Math.Clamp(v, (int)_spinRows.MinValue, (int)_spinRows.MaxValue);
			}
		};
		rowRows.AddChild(_spinRows);
		contentVBox.AddChild(rowRows);
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, Action<int, int> onApplied)
	{
		_sheetFileName = fileName ?? string.Empty;
		_columns = Math.Max(1, initialCols);
		_rows = Math.Max(1, initialRows);
		_onApplied = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Edit Spritesheet")} - {_sheetFileName}";

		if (_spinCols != null) _spinCols.Value = _columns;
		if (_spinRows != null) _spinRows.Value = _rows;

		OpenDialog();
	}

	protected override void OnApply()
	{
		if (_spinCols != null)
		{
			_spinCols.Apply();
			if (int.TryParse(_spinCols.GetLineEdit()?.Text, out int c))
				_columns = Math.Clamp(c, (int)_spinCols.MinValue, (int)_spinCols.MaxValue);
			else
				_columns = (int)_spinCols.Value;
		}

		if (_spinRows != null)
		{
			_spinRows.Apply();
			if (int.TryParse(_spinRows.GetLineEdit()?.Text, out int r))
				_rows = Math.Clamp(r, (int)_spinRows.MinValue, (int)_spinRows.MaxValue);
			else
				_rows = (int)_spinRows.Value;
		}

		_onApplied?.Invoke(_columns, _rows);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Spritesheet {0} updated to {1}x{2}."), _sheetFileName, _columns, _rows));
	}
}
