using Godot;
using System;

public class MapEditorPlacementSettings
{
	private Slider _sldPlacementRotate;
	private Label _lblPlacementRotateValue;
	private Slider _sldPlacementScale;
	private Label _lblPlacementScaleValue;
	private CheckBox _chkSpawnAsEnemy;
	private CheckBox _chkRandomRotation;
	private CheckBox _chkRandomScale;
	private CheckBox _chkClumpMode;
	private Slider _sldClumpDensity;
	private Label _lblClumpDensityValue;
	private Slider _sldClumpScaleVar;
	private Label _lblClumpScaleVarValue;

	public MapEditorPlacementSettings(Slider sldPlacementRotate, Label lblPlacementRotateValue, Slider sldPlacementScale, Label lblPlacementScaleValue,
		CheckBox chkSpawnAsEnemy, CheckBox chkRandomRotation, CheckBox chkRandomScale, CheckBox chkClumpMode,
		Slider sldClumpDensity, Label lblClumpDensityValue, Slider sldClumpScaleVar, Label lblClumpScaleVarValue)
	{
		_sldPlacementRotate = sldPlacementRotate;
		_lblPlacementRotateValue = lblPlacementRotateValue;
		_sldPlacementScale = sldPlacementScale;
		_lblPlacementScaleValue = lblPlacementScaleValue;
		_chkSpawnAsEnemy = chkSpawnAsEnemy;
		_chkRandomRotation = chkRandomRotation;
		_chkRandomScale = chkRandomScale;
		_chkClumpMode = chkClumpMode;
		_sldClumpDensity = sldClumpDensity;
		_lblClumpDensityValue = lblClumpDensityValue;
		_sldClumpScaleVar = sldClumpScaleVar;
		_lblClumpScaleVarValue = lblClumpScaleVarValue;

		_sldPlacementRotate.ValueChanged += (val) =>
		{
			_lblPlacementRotateValue.Text = val.ToString("F0") + "°";
			if (GameHost.Instance != null) GameHost.Instance.EditorPlacementRotation = (float)val;
		};

		_sldPlacementScale.ValueChanged += (val) =>
		{
			_lblPlacementScaleValue.Text = val.ToString("F1") + "x";
			if (GameHost.Instance != null) GameHost.Instance.EditorPlacementScale = (float)val;
		};

		_chkSpawnAsEnemy.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.PlaceUnitIsEnemy = buttonPressed;
		};

		_chkRandomRotation.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorRandomRotation = buttonPressed;
		};

		_chkRandomScale.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorRandomScale = buttonPressed;
		};

		_chkClumpMode.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpMode = buttonPressed;
		};

		_sldClumpDensity.ValueChanged += (val) =>
		{
			_lblClumpDensityValue.Text = val.ToString("F0");
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpDensity = (float)val;
		};

		_sldClumpScaleVar.ValueChanged += (val) =>
		{
			_lblClumpScaleVarValue.Text = val.ToString("F2");
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpScaleVar = (float)val;
		};
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		_sldPlacementRotate.Value = viewModel.PlacementRotate;
		_lblPlacementRotateValue.Text = viewModel.PlacementRotate.ToString("F0") + "°";

		_sldPlacementScale.Value = viewModel.PlacementScale;
		_lblPlacementScaleValue.Text = viewModel.PlacementScale.ToString("F1") + "x";

		_chkSpawnAsEnemy.ButtonPressed = viewModel.SpawnAsEnemy;
		_chkRandomRotation.ButtonPressed = viewModel.RandomRotation;
		_chkRandomScale.ButtonPressed = viewModel.RandomScale;
		_chkClumpMode.ButtonPressed = viewModel.ClumpMode;

		_sldClumpDensity.Value = viewModel.ClumpDensity;
		_lblClumpDensityValue.Text = viewModel.ClumpDensity.ToString("F0");

		_sldClumpScaleVar.Value = viewModel.ClumpScaleVar;
		_lblClumpScaleVarValue.Text = viewModel.ClumpScaleVar.ToString("F2");
	}
}
