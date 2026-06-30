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

		_btnInspectorRotLeft.Pressed += () => MapEditorHUD.Instance?.RotateSelectedObjectAction(-15f);
		_btnInspectorRotRight.Pressed += () => MapEditorHUD.Instance?.RotateSelectedObjectAction(15f);
		_btnInspectorScaleDown.Pressed += () => MapEditorHUD.Instance?.ScaleSelectedObjectAction(0.9f);
		_btnInspectorScaleUp.Pressed += () => MapEditorHUD.Instance?.ScaleSelectedObjectAction(1.1f);
		_btnInspectorScaleReset.Pressed += () => MapEditorHUD.Instance?.ScaleSelectedObjectAction(-1f);
		_btnInspectorDelete.Pressed += () => MapEditorHUD.Instance?.DeleteSelectedObjectAction();
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		_lblInspectorTitle.Text = TranslationServer.Translate(viewModel.InspectorTitle);
		_lblInspectorPos.Text = TranslationServer.Translate(viewModel.InspectorPos);
	}
}
