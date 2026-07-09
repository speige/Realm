using Godot;
using System;

public class MapEditorPathingPanel
{
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkUnpathable;
	private CheckBox _chkBuildable;
	private OptionButton _optPathingMode;

	public MapEditorPathingPanel(CheckBox chkShallowWater, CheckBox chkDeepWater, CheckBox chkFlying,
		CheckBox chkGround, CheckBox chkUnpathable, CheckBox chkBuildable, OptionButton optPathingMode)
	{
		_chkShallowWater = chkShallowWater;
		_chkDeepWater = chkDeepWater;
		_chkFlying = chkFlying;
		_chkGround = chkGround;
		_chkUnpathable = chkUnpathable;
		_chkBuildable = chkBuildable;
		_optPathingMode = optPathingMode;

		if (_chkShallowWater != null)
			_chkShallowWater.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.ShallowWater = val; };
		if (_chkDeepWater != null)
			_chkDeepWater.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.DeepWater = val; };
		if (_chkFlying != null)
			_chkFlying.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Flying = val; };
		if (_chkGround != null)
			_chkGround.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Ground = val; };
		if (_chkUnpathable != null)
			_chkUnpathable.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Unpathable = val; };
		if (_chkBuildable != null)
			_chkBuildable.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Buildable = val; };
		if (_optPathingMode != null)
			_optPathingMode.ItemSelected += (idx) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.PathingModeIndex = (int)idx; };
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (_chkShallowWater != null) _chkShallowWater.ButtonPressed = viewModel.ShallowWater;
		if (_chkDeepWater != null) _chkDeepWater.ButtonPressed = viewModel.DeepWater;
		if (_chkFlying != null) _chkFlying.ButtonPressed = viewModel.Flying;
		if (_chkGround != null) _chkGround.ButtonPressed = viewModel.Ground;
		if (_chkUnpathable != null) _chkUnpathable.ButtonPressed = viewModel.Unpathable;
		if (_chkBuildable != null) _chkBuildable.ButtonPressed = viewModel.Buildable;
		
		if (_optPathingMode != null && viewModel.PathingModeIndex < _optPathingMode.ItemCount)
		{
			_optPathingMode.Selected = viewModel.PathingModeIndex;
		}
	}
}
