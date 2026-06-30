using Godot;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class VSCodeMdiWindow : Panel
{
	private Panel _header;
	private Panel _content;
	private Label _titleLabel;
	private Button _btnClose;
	private bool _dragging;
	private Vector2 _dragOffset;

	public override void _Ready()
	{
		Name = "VSCodeMdiWindow";
		CustomMinimumSize = new Vector2(400, 300);
		var viewportSize = GetViewportRect().Size;
		Position = new Vector2(10, 80);
		Size = new Vector2(viewportSize.X - 20, viewportSize.Y - 90);

		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
		styleBox.BorderColor = UIStyle.ColorCyanGlow;
		styleBox.SetBorderWidthAll(2);
		styleBox.CornerRadiusTopLeft = 6;
		styleBox.CornerRadiusTopRight = 6;
		styleBox.CornerRadiusBottomLeft = 6;
		styleBox.CornerRadiusBottomRight = 6;
		AddThemeStyleboxOverride("panel", styleBox);

		_header = new Panel();
		_header.CustomMinimumSize = new Vector2(0, 36);
		_header.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		
		var headerStyle = new StyleBoxFlat();
		headerStyle.BgColor = new Color(0.18f, 0.18f, 0.18f, 1.0f);
		headerStyle.CornerRadiusTopLeft = 4;
		headerStyle.CornerRadiusTopRight = 4;
		_header.AddThemeStyleboxOverride("panel", headerStyle);
		AddChild(_header);

		var headerLayout = new HBoxContainer();
		headerLayout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		headerLayout.OffsetLeft = 10;
		headerLayout.OffsetRight = -10;
		_header.AddChild(headerLayout);

		_titleLabel = new Label();
		_titleLabel.Text = "💻 VS Code Data Editor";
		_titleLabel.VerticalAlignment = VerticalAlignment.Center;
		_titleLabel.SizeFlagsVertical = SizeFlags.Fill;
		_titleLabel.AddThemeFontSizeOverride("font_size", 13);
		_titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		headerLayout.AddChild(_titleLabel);

		var spacer = new Control();
		spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		headerLayout.AddChild(spacer);

		_btnClose = new Button();
		_btnClose.Text = " X ";
		_btnClose.Set("icon_max_width", 0);
		_btnClose.FocusMode = FocusModeEnum.None;
		_btnClose.CustomMinimumSize = new Vector2(24, 24);
		_btnClose.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		
		var closeNormal = new StyleBoxFlat();
		closeNormal.BgColor = new Color(0.35f, 0.15f, 0.15f);
		closeNormal.CornerRadiusTopLeft = 4;
		closeNormal.CornerRadiusTopRight = 4;
		closeNormal.CornerRadiusBottomLeft = 4;
		closeNormal.CornerRadiusBottomRight = 4;
		_btnClose.AddThemeStyleboxOverride("normal", closeNormal);

		var closeHover = new StyleBoxFlat();
		closeHover.BgColor = new Color(0.5f, 0.2f, 0.2f);
		closeHover.CornerRadiusTopLeft = 4;
		closeHover.CornerRadiusTopRight = 4;
		closeHover.CornerRadiusBottomLeft = 4;
		closeHover.CornerRadiusBottomRight = 4;
		_btnClose.AddThemeStyleboxOverride("hover", closeHover);

		_btnClose.Pressed += () => Hide();
		headerLayout.AddChild(_btnClose);

		_content = new Panel();
		_content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_content.OffsetTop = 36;
		
		var contentStyle = new StyleBoxEmpty();
		_content.AddThemeStyleboxOverride("panel", contentStyle);
		AddChild(_content);

		_header.GuiInput += OnHeaderGuiInput;
		Resized += OnResized;
		VisibilityChanged += OnVisibilityChanged;

		VSCodeManager.Instance.Initialize(_content);
		VSCodeManager.Instance.SetVisible(Visible);
	}

	public override void _Process(double delta)
	{
		if (_dragging)
		{
			Position = GetGlobalMousePosition() - _dragOffset;
			VSCodeManager.Instance.UpdateBounds();
		}
	}

	private void OnHeaderGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			if (mouseEvent.Pressed)
			{
				_dragging = true;
				_dragOffset = mouseEvent.Position;
			}
			else
			{
				_dragging = false;
			}
		}
	}

	private void OnResized()
	{
		VSCodeManager.Instance.UpdateBounds();
	}

	private void OnVisibilityChanged()
	{
		VSCodeManager.Instance.SetVisible(Visible);
		if (Visible)
		{
			var viewportSize = GetViewportRect().Size;
			Position = new Vector2(10, 80);
			Size = new Vector2(viewportSize.X - 20, viewportSize.Y - 90);
			VSCodeManager.Instance.UpdateBounds();
		}
	}
}
