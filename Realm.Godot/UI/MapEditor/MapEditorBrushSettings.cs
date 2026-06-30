using Godot;
using System;

public class MapEditorBrushSettings
{
	private Slider _sldBrushSize;
	private Label _lblBrushSizeValue;
	private Slider _sldBrushStrength;
	private Label _lblBrushStrengthValue;
	private Slider _sldFlattenHeight;
	private Label _lblFlattenHeightValue;

	private CheckBox _chkBlockMode;
	private Slider _sldBlockStep;
	private Label _lblBlockStepValue;

	private Slider _sldWaterHeight;
	private Label _lblWaterHeightValue;
	private CheckBox _chkWaterEnabled;

	public MapEditorBrushSettings(Slider sldBrushSize, Label lblBrushSizeValue, Slider sldBrushStrength, Label lblBrushStrengthValue,
		Slider sldFlattenHeight, Label lblFlattenHeightValue, CheckBox chkBlockMode, Slider sldBlockStep, Label lblBlockStepValue,
		Slider sldWaterHeight, Label lblWaterHeightValue, CheckBox chkWaterEnabled)
	{
		_sldBrushSize = sldBrushSize;
		_lblBrushSizeValue = lblBrushSizeValue;
		_sldBrushStrength = sldBrushStrength;
		_lblBrushStrengthValue = lblBrushStrengthValue;
		_sldFlattenHeight = sldFlattenHeight;
		_lblFlattenHeightValue = lblFlattenHeightValue;
		_chkBlockMode = chkBlockMode;
		_sldBlockStep = sldBlockStep;
		_lblBlockStepValue = lblBlockStepValue;
		_sldWaterHeight = sldWaterHeight;
		_lblWaterHeightValue = lblWaterHeightValue;
		_chkWaterEnabled = chkWaterEnabled;

		_sldBrushSize.ValueChanged += (val) =>
		{
			_lblBrushSizeValue.Text = val.ToString("F0");
			if (GameHost.Instance != null) GameHost.Instance.EditorBrushRadius = (float)val;
		};

		_sldBrushStrength.ValueChanged += (val) =>
		{
			_lblBrushStrengthValue.Text = val.ToString("F0");
			if (GameHost.Instance != null) GameHost.Instance.EditorBrushStrength = (float)val;
		};

		_sldFlattenHeight.ValueChanged += (val) =>
		{
			_lblFlattenHeightValue.Text = val.ToString("F1") + "m";
			if (GameHost.Instance != null) GameHost.Instance.EditorFlattenHeight = (float)val;
		};

		if (_chkBlockMode != null)
		{
			_chkBlockMode.Toggled += (buttonPressed) =>
			{
				if (GameHost.Instance != null) GameHost.Instance.EditorBlockMode = buttonPressed;
			};
		}

		if (_sldBlockStep != null)
		{
			_sldBlockStep.ValueChanged += (val) =>
			{
				float fVal = (float)val;
				_lblBlockStepValue.Text = fVal.ToString("F1") + "m";
				if (GameHost.Instance != null) GameHost.Instance.EditorBlockLevelHeight = fVal;
			};
		}

		if (_sldWaterHeight != null)
		{
			_sldWaterHeight.ValueChanged += (val) =>
			{
				float fVal = (float)val;
				_lblWaterHeightValue.Text = fVal.ToString("F1") + "m";
				if (GameHost.Instance?.GroundTerrain != null) GameHost.Instance.GroundTerrain.WaterHeight = fVal;
			};
		}

		if (_chkWaterEnabled != null)
		{
			_chkWaterEnabled.Toggled += (buttonPressed) =>
			{
				// WaterEnabled is handled by water height
			};
		}
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		_sldBrushSize.Value = viewModel.BrushSize;
		_lblBrushSizeValue.Text = viewModel.BrushSize.ToString("F0");

		_sldBrushStrength.Value = viewModel.BrushStrength;
		_lblBrushStrengthValue.Text = viewModel.BrushStrength.ToString("F0");

		_sldFlattenHeight.Value = viewModel.FlattenHeight;
		_lblFlattenHeightValue.Text = viewModel.FlattenHeight.ToString("F1") + "m";

		if (_chkBlockMode != null) _chkBlockMode.ButtonPressed = GameHost.Instance != null && GameHost.Instance.EditorBlockMode;
		if (_sldBlockStep != null)
		{
			_sldBlockStep.Value = viewModel.BlockStep;
			_lblBlockStepValue.Text = viewModel.BlockStep.ToString("F1") + "m";
		}

		if (_sldWaterHeight != null)
		{
			_sldWaterHeight.Value = viewModel.WaterHeight;
			_lblWaterHeightValue.Text = viewModel.WaterHeight.ToString("F1") + "m";
		}

		if (_chkWaterEnabled != null)
		{
			_chkWaterEnabled.ButtonPressed = viewModel.WaterEnabled;
		}
	}
}
