using Godot;
using System;

public class MapEditorPathingPanel
{
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkUnpathable;
	private OptionButton _optPathingMode;

	public MapEditorPathingPanel(CheckBox chkShallowWater, CheckBox chkDeepWater, CheckBox chkFlying,
		CheckBox chkGround, CheckBox chkUnpathable, OptionButton optPathingMode)
	{
		_chkShallowWater = chkShallowWater;
		_chkDeepWater = chkDeepWater;
		_chkFlying = chkFlying;
		_chkGround = chkGround;
		_chkUnpathable = chkUnpathable;
		_optPathingMode = optPathingMode;
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (_chkShallowWater != null) _chkShallowWater.ButtonPressed = viewModel.ShallowWater;
		if (_chkDeepWater != null) _chkDeepWater.ButtonPressed = viewModel.DeepWater;
		if (_chkFlying != null) _chkFlying.ButtonPressed = viewModel.Flying;
		if (_chkGround != null) _chkGround.ButtonPressed = viewModel.Ground;
		if (_chkUnpathable != null) _chkUnpathable.ButtonPressed = viewModel.Unpathable;
		
		if (_optPathingMode != null && viewModel.PathingModeIndex < _optPathingMode.ItemCount)
		{
			_optPathingMode.Selected = viewModel.PathingModeIndex;
		}
	}
}
