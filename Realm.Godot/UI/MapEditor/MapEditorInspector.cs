using Godot;
using System;

public class MapEditorInspector
{
	private Label _lblInspectorTitle;
	private Label _lblInspectorPos;
	private Button _btnInspectorRotLeft;
	private Button _btnInspectorRotRight;
	private Button _btnInspectorScaleDown;
	private Button _btnInspectorScaleUp;
	private Button _btnInspectorScaleReset;
	private Button _btnInspectorDelete;

	public MapEditorInspector(Label lblInspectorTitle, Label lblInspectorPos,
		Button btnInspectorRotLeft, Button btnInspectorRotRight,
		Button btnInspectorScaleDown, Button btnInspectorScaleUp,
		Button btnInspectorScaleReset, Button btnInspectorDelete)
	{
		_lblInspectorTitle = lblInspectorTitle;
		_lblInspectorPos = lblInspectorPos;
		_btnInspectorRotLeft = btnInspectorRotLeft;
		_btnInspectorRotRight = btnInspectorRotRight;
		_btnInspectorScaleDown = btnInspectorScaleDown;
		_btnInspectorScaleUp = btnInspectorScaleUp;
		_btnInspectorScaleReset = btnInspectorScaleReset;
		_btnInspectorDelete = btnInspectorDelete;

		if (_btnInspectorRotLeft != null) _btnInspectorRotLeft.Pressed += () => MapEditorHUD.Instance?.RotateSelectedObjectAction(-15f);
		if (_btnInspectorRotRight != null) _btnInspectorRotRight.Pressed += () => MapEditorHUD.Instance?.RotateSelectedObjectAction(15f);
		if (_btnInspectorScaleDown != null) _btnInspectorScaleDown.Pressed += () => MapEditorHUD.Instance?.ScaleSelectedObjectAction(0.9f);
		if (_btnInspectorScaleUp != null) _btnInspectorScaleUp.Pressed += () => MapEditorHUD.Instance?.ScaleSelectedObjectAction(1.1f);
		if (_btnInspectorScaleReset != null) _btnInspectorScaleReset.Pressed += () => MapEditorHUD.Instance?.ScaleSelectedObjectAction(-1f);
		if (_btnInspectorDelete != null) _btnInspectorDelete.Pressed += () => MapEditorHUD.Instance?.DeleteSelectedObjectAction();
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;
		if (_lblInspectorTitle != null) _lblInspectorTitle.Text = TranslationServer.Translate(viewModel.InspectorTitle);
		if (_lblInspectorPos != null) _lblInspectorPos.Text = TranslationServer.Translate(viewModel.InspectorPos);
	}
}
