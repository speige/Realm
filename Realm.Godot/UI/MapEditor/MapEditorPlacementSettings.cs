using Godot;
using System;

public class MapEditorPlacementSettings
{
	private Slider _sldPlacementRotate;
	private Label _lblPlacementRotateValue;
	private Slider _sldPlacementScale;
	private Label _lblPlacementScaleValue;
	private CheckBox _chkRandomRotation;
	private CheckBox _chkRandomScale;
	private CheckBox _chkClumpMode;
	private Slider _sldClumpDensity;
	private Label _lblClumpDensityValue;
	private Slider _sldClumpScaleVar;
	private Label _lblClumpScaleVarValue;

	public MapEditorPlacementSettings(Slider sldPlacementRotate, Label lblPlacementRotateValue, Slider sldPlacementScale, Label lblPlacementScaleValue,
		CheckBox chkRandomRotation, CheckBox chkRandomScale, CheckBox chkClumpMode,
		Slider sldClumpDensity, Label lblClumpDensityValue, Slider sldClumpScaleVar, Label lblClumpScaleVarValue)
	{
		_sldPlacementRotate = sldPlacementRotate;
		_lblPlacementRotateValue = lblPlacementRotateValue;
		_sldPlacementScale = sldPlacementScale;
		_lblPlacementScaleValue = lblPlacementScaleValue;
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

		_chkRandomRotation.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorRandomRotation = buttonPressed;
			UpdateVisibility();
		};

		_chkRandomScale.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorRandomScale = buttonPressed;
			UpdateVisibility();
		};

		_chkClumpMode.Toggled += (buttonPressed) =>
		{
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpMode = buttonPressed;
			UpdateVisibility();
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

		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		bool clumpMode = _chkClumpMode.ButtonPressed;
		bool randomRotation = _chkRandomRotation.ButtonPressed;
		bool randomScale = _chkRandomScale.ButtonPressed;

		var rotateContainer = _sldPlacementRotate.GetParent() as Control;
		var scaleContainer = _sldPlacementScale.GetParent() as Control;

		if (clumpMode)
		{
			if (rotateContainer != null && rotateContainer.Visible) rotateContainer.Visible = false;
			if (scaleContainer != null && scaleContainer.Visible) scaleContainer.Visible = false;
			if (_chkRandomRotation.Visible) _chkRandomRotation.Visible = false;
			if (_chkRandomScale.Visible) _chkRandomScale.Visible = false;
		}
		else
		{
			bool rotVis = !randomRotation;
			if (rotateContainer != null && rotateContainer.Visible != rotVis) rotateContainer.Visible = rotVis;

			bool scaleVis = !randomScale;
			if (scaleContainer != null && scaleContainer.Visible != scaleVis) scaleContainer.Visible = scaleVis;

			if (!_chkRandomRotation.Visible) _chkRandomRotation.Visible = true;
			if (!_chkRandomScale.Visible) _chkRandomScale.Visible = true;
		}
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;

		if (!Mathf.IsEqualApprox((float)_sldPlacementRotate.Value, viewModel.PlacementRotate))
		{
			_sldPlacementRotate.Value = viewModel.PlacementRotate;
			_lblPlacementRotateValue.Text = viewModel.PlacementRotate.ToString("F0") + "°";
		}

		if (!Mathf.IsEqualApprox((float)_sldPlacementScale.Value, viewModel.PlacementScale))
		{
			_sldPlacementScale.Value = viewModel.PlacementScale;
			_lblPlacementScaleValue.Text = viewModel.PlacementScale.ToString("F1") + "x";
		}

		bool stateChanged = false;

		if (_chkRandomRotation.ButtonPressed != viewModel.RandomRotation)
		{
			_chkRandomRotation.ButtonPressed = viewModel.RandomRotation;
			stateChanged = true;
		}

		if (_chkRandomScale.ButtonPressed != viewModel.RandomScale)
		{
			_chkRandomScale.ButtonPressed = viewModel.RandomScale;
			stateChanged = true;
		}

		if (_chkClumpMode.ButtonPressed != viewModel.ClumpMode)
		{
			_chkClumpMode.ButtonPressed = viewModel.ClumpMode;
			stateChanged = true;
		}

		if (!Mathf.IsEqualApprox((float)_sldClumpDensity.Value, viewModel.ClumpDensity))
		{
			_sldClumpDensity.Value = viewModel.ClumpDensity;
			_lblClumpDensityValue.Text = viewModel.ClumpDensity.ToString("F0");
		}

		if (!Mathf.IsEqualApprox((float)_sldClumpScaleVar.Value, viewModel.ClumpScaleVar))
		{
			_sldClumpScaleVar.Value = viewModel.ClumpScaleVar;
			_lblClumpScaleVarValue.Text = viewModel.ClumpScaleVar.ToString("F2");
		}

		if (stateChanged)
		{
			UpdateVisibility();
		}
	}
}
