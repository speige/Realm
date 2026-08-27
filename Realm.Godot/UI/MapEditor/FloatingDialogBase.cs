using Godot;
using System;

public partial class FloatingDialogBase : PanelContainer
{
	protected readonly MapEditorHUD Hud;
	protected VBoxContainer MainVBox;
	protected HBoxContainer HeaderHBox;
	protected Label TitleLabel;
	protected Button CloseButton;
	protected VBoxContainer BodyContainer;
	protected HBoxContainer FooterHBox;
	protected Button CancelButton;
	protected Button ApplyButton;

	private bool _isDragging;
	private Vector2 _dragStartMousePosition;
	private Vector2 _dragStartPosition;

	public bool IsOpen => Visible && GetParent() != null;

	public FloatingDialogBase(MapEditorHUD hud, string titleText, Vector2 minSize)
	{
		Hud = hud;
		CustomMinimumSize = minSize;
		AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		MainVBox = new VBoxContainer();
		MainVBox.AddThemeConstantOverride("separation", 10);
		AddChild(MainVBox);

		HeaderHBox = new HBoxContainer();
		HeaderHBox.AddThemeConstantOverride("separation", 8);
		HeaderHBox.GuiInput += OnHeaderGuiInput;
		HeaderHBox.MouseFilter = MouseFilterEnum.Stop;
		HeaderHBox.MouseDefaultCursorShape = CursorShape.Move;
		MainVBox.AddChild(HeaderHBox);

		TitleLabel = new Label();
		TitleLabel.Text = titleText;
		TitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		TitleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		TitleLabel.AddThemeFontSizeOverride("font_size", 14);
		TitleLabel.MouseFilter = MouseFilterEnum.Pass;
		HeaderHBox.AddChild(TitleLabel);

		CloseButton = new Button();
		CloseButton.Set("icon_max_width", 0);
		CloseButton.Text = "✕";
		CloseButton.CustomMinimumSize = new Vector2(24, 24);
		CloseButton.FocusMode = FocusModeEnum.None;
		CloseButton.Pressed += () => CancelAndClose();
		HeaderHBox.AddChild(CloseButton);

		BodyContainer = new VBoxContainer();
		BodyContainer.AddThemeConstantOverride("separation", 8);
		BodyContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		MainVBox.AddChild(BodyContainer);

		FooterHBox = new HBoxContainer();
		FooterHBox.AddThemeConstantOverride("separation", 12);
		FooterHBox.Alignment = BoxContainer.AlignmentMode.End;
		MainVBox.AddChild(FooterHBox);

		CancelButton = new Button();
		CancelButton.Set("icon_max_width", 0);
		CancelButton.Text = TranslationServer.Translate("CANCEL");
		CancelButton.CustomMinimumSize = new Vector2(90, 30);
		CancelButton.FocusMode = FocusModeEnum.None;
		CancelButton.Pressed += () => CancelAndClose();
		FooterHBox.AddChild(CancelButton);

		ApplyButton = new Button();
		ApplyButton.Set("icon_max_width", 0);
		ApplyButton.Text = TranslationServer.Translate("APPLY");
		ApplyButton.CustomMinimumSize = new Vector2(90, 30);
		ApplyButton.FocusMode = FocusModeEnum.None;
		ApplyButton.Pressed += () => ApplyAndClose();
		FooterHBox.AddChild(ApplyButton);
	}

	public virtual void OpenDialog()
	{
		if (GetParent() == null && Hud != null)
		{
			Hud.AddChild(this);
		}

		Visible = true;
		MoveToFront();

		Vector2 parentSize = Hud != null ? Hud.GetViewportRect().Size : GetViewportRect().Size;
		Position = new Vector2(
			Mathf.Max(20, (parentSize.X - CustomMinimumSize.X) * 0.5f),
			Mathf.Max(20, (parentSize.Y - CustomMinimumSize.Y) * 0.4f)
		);
	}

	public virtual void ApplyAndClose()
	{
		OnApply();
		CloseDialog();
	}

	public virtual void CancelAndClose()
	{
		OnCancel();
		CloseDialog();
	}

	public virtual void CloseDialog()
	{
		Visible = false;
		if (GetParent() != null)
		{
			GetParent().RemoveChild(this);
		}
	}

	protected virtual void OnApply() { }
	protected virtual void OnCancel() { }

	private void OnHeaderGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			_isDragging = mouseButton.Pressed;
			if (_isDragging)
			{
				_dragStartMousePosition = mouseButton.GlobalPosition;
				_dragStartPosition = Position;
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			Vector2 delta = mouseMotion.GlobalPosition - _dragStartMousePosition;
			Vector2 newPosition = _dragStartPosition + delta;
			Vector2 viewportSize = GetViewportRect().Size;
			newPosition.X = Mathf.Clamp(newPosition.X, 10, Mathf.Max(10, viewportSize.X - Size.X - 10));
			newPosition.Y = Mathf.Clamp(newPosition.Y, 10, Mathf.Max(10, viewportSize.Y - Size.Y - 10));
			Position = newPosition;
		}
	}
}
