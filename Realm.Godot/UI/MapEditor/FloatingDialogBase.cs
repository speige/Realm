using Godot;
using System;
using System.Collections.Generic;

public partial class FloatingDialogBase : PanelContainer
{
	private static readonly HashSet<FloatingDialogBase> _openDialogs = new();
	public static bool HasAnyDialogOpen => _openDialogs.Count > 0;

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

	public void SetFooterCloseOnly(string closeText = "CLOSE")
	{
		if (CancelButton != null) CancelButton.Visible = false;
		if (ApplyButton != null) ApplyButton.Visible = false;

		var btnClose = new Button();
		btnClose.Set("icon_max_width", 0);
		btnClose.Text = TranslationServer.Translate(closeText);
		btnClose.CustomMinimumSize = new Vector2(90, 30);
		btnClose.FocusMode = FocusModeEnum.None;
		btnClose.Pressed += () => CloseDialog();
		FooterHBox.AddChild(btnClose);
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationPredelete || what == NotificationExitTree)
		{
			_openDialogs.Remove(this);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsOpen) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Tab)
			{
				CycleFocusToNextInput(keyEvent.ShiftPressed);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void CycleFocusToNextInput(bool reverse)
	{
		var inputs = new List<LineEdit>();
		CollectFocusableInputs(this, inputs);
		if (inputs.Count == 0) return;

		Control currentFocus = GetViewport().GuiGetFocusOwner();
		int currentIndex = -1;
		if (currentFocus is LineEdit currentEdit)
		{
			currentIndex = inputs.IndexOf(currentEdit);
		}

		int nextIndex;
		if (currentIndex == -1)
		{
			nextIndex = reverse ? inputs.Count - 1 : 0;
		}
		else
		{
			nextIndex = reverse ? (currentIndex - 1 + inputs.Count) % inputs.Count : (currentIndex + 1) % inputs.Count;
		}

		inputs[nextIndex].GrabFocus();
		inputs[nextIndex].SelectAll();
	}

	private void CollectFocusableInputs(Node node, List<LineEdit> inputs)
	{
		if (node is LineEdit edit && edit.Visible && edit.IsInsideTree() && edit.Editable)
		{
			inputs.Add(edit);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			CollectFocusableInputs(node.GetChild(i), inputs);
		}
	}

	public virtual void OpenDialog()
	{
		if (GetParent() == null && Hud != null)
		{
			Hud.AddChild(this);
		}

		Visible = true;
		MoveToFront();
		_openDialogs.Add(this);

		Vector2 parentSize = Hud != null ? Hud.GetViewportRect().Size : GetViewportRect().Size;
		Position = new Vector2(
			Mathf.Max(20, (parentSize.X - CustomMinimumSize.X) * 0.5f),
			Mathf.Max(20, (parentSize.Y - CustomMinimumSize.Y) * 0.4f)
		);
	}

	public virtual void ApplyAndClose()
	{
		CommitPendingInputFocus();
		OnApply();
		CloseDialog();
	}

	protected void CommitPendingInputFocus()
	{
		try
		{
			var focusOwner = GetViewport()?.GuiGetFocusOwner();
			if (focusOwner != null && IsAncestorOf(focusOwner))
			{
				focusOwner.ReleaseFocus();
			}
		}
		catch { }

		CommitInputsRecursive(this);
	}

	private void CommitInputsRecursive(Node node)
	{
		if (node == null) return;

		if (node is SpinBox spinBox)
		{
			try
			{
				spinBox.Apply();
				var lineEdit = spinBox.GetLineEdit();
				if (lineEdit != null && double.TryParse(lineEdit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
				{
					spinBox.Value = Math.Clamp(parsedVal, spinBox.MinValue, spinBox.MaxValue);
				}
			}
			catch { }
		}
		else if (node is LineEdit lineEdit)
		{
			try
			{
				lineEdit.ReleaseFocus();
			}
			catch { }
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			CommitInputsRecursive(node.GetChild(i));
		}
	}

	public virtual void CancelAndClose()
	{
		OnCancel();
		CloseDialog();
	}

	public virtual void CloseDialog()
	{
		_openDialogs.Remove(this);
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

	public Label AddSectionHeader(Control parent, string titleText, Color? color = null)
	{
		var header = new Label();
		header.Text = titleText;
		header.AddThemeColorOverride("font_color", color ?? UIStyle.ColorGold);
		header.AddThemeFontSizeOverride("font_size", 12);
		parent.AddChild(header);
		return header;
	}

	public Label AddLabel(Control parent, string text, int fontSize = 11, Color? color = null)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (color.HasValue)
		{
			label.AddThemeColorOverride("font_color", color.Value);
		}
		parent.AddChild(label);
		return label;
	}

	public Label AddDescription(Control parent, string text)
	{
		var desc = new Label();
		desc.Text = text;
		desc.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		desc.AddThemeFontSizeOverride("font_size", 11);
		desc.AutowrapMode = TextServer.AutowrapMode.Word;
		parent.AddChild(desc);
		return desc;
	}

	public (HSlider Slider, Label ValueLabel) AddSlider(
		VBoxContainer parent,
		string labelText,
		float min,
		float max,
		float step,
		float initialValue,
		Action<float> onChanged,
		string format = "0.00",
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var slider = new HSlider();
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialValue;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(slider);

		var valLbl = new Label();
		valLbl.Text = initialValue.ToString(format);
		valLbl.CustomMinimumSize = new Vector2(45, 0);
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);

		slider.ValueChanged += (double val) =>
		{
			valLbl.Text = ((float)val).ToString(format);
			onChanged((float)val);
		};

		parent.AddChild(row);
		return (slider, valLbl);
	}

	public (ColorPickerButton Picker, HSlider HueSlider) AddColorPicker(
		VBoxContainer parent,
		string labelText,
		Color initialColor,
		Action<Color> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var hueSlider = new HSlider();
		hueSlider.MinValue = 0.0f;
		hueSlider.MaxValue = 1.0f;
		hueSlider.Step = 0.01f;
		hueSlider.Value = initialColor.H;
		hueSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(hueSlider);

		var picker = new ColorPickerButton();
		picker.CustomMinimumSize = new Vector2(36, 22);
		picker.EditAlpha = false;
		picker.Color = initialColor;
		row.AddChild(picker);

		hueSlider.ValueChanged += (double val) =>
		{
			Color tintColor = (val <= 0.0) ? new Color(1.0f, 1.0f, 1.0f) : Color.FromHsv((float)val, 0.75f, 1.0f);
			picker.Color = tintColor;
			onChanged(tintColor);
		};

		picker.ColorChanged += (Color color) =>
		{
			onChanged(color);
		};

		parent.AddChild(row);
		return (picker, hueSlider);
	}

	public CheckBox AddCheckBox(
		VBoxContainer parent,
		string labelText,
		bool initialValue,
		Action<bool> onChanged,
		string tooltip = "")
	{
		var chk = new CheckBox();
		chk.Text = labelText;
		chk.ButtonPressed = initialValue;
		chk.AddThemeFontSizeOverride("font_size", 11);
		if (!string.IsNullOrEmpty(tooltip)) chk.TooltipText = tooltip;
		chk.Toggled += (pressed) => onChanged(pressed);
		parent.AddChild(chk);
		return chk;
	}

	public OptionButton AddOptionDropdown(
		VBoxContainer parent,
		string labelText,
		string[] options,
		int initialIndex,
		Action<int> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var opt = new OptionButton();
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opt.AddThemeFontSizeOverride("font_size", 11);
		for (int i = 0; i < options.Length; i++)
		{
			opt.AddItem(options[i], i);
		}
		if (initialIndex >= 0 && initialIndex < options.Length)
		{
			opt.Selected = initialIndex;
		}
		opt.ItemSelected += (long idx) => onChanged((int)idx);
		row.AddChild(opt);

		parent.AddChild(row);
		return opt;
	}

	public LineEdit AddTextInput(
		VBoxContainer parent,
		string labelText,
		string initialText,
		Action<string> onChanged,
		string placeholder = "",
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Text = initialText ?? string.Empty;
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		txt.TextChanged += (val) => onChanged(val);
		row.AddChild(txt);

		parent.AddChild(row);
		return txt;
	}

	public (LineEdit Edit, Action<float> SetValue) AddNumberInput(
		VBoxContainer parent,
		string labelText,
		float initialValue,
		Action<float> onChanged,
		float step = 0.5f,
		string placeholder = "",
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Text = initialValue.ToString("0.##");
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		txt.TextChanged += (val) =>
		{
			if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
			{
				onChanged(parsed);
			}
		};
		row.AddChild(txt);

		parent.AddChild(row);
		return (txt, (val) => txt.Text = val.ToString("0.##"));
	}

	public (LineEdit X, LineEdit Y, LineEdit Z) AddVector3Input(
		VBoxContainer parent,
		string labelText,
		Vector3 initialValue,
		Action<Vector3> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector3 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var txtZ = new LineEdit { Text = initialValue.Z.ToString("0.##"), PlaceholderText = "Z", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		txtX.AddThemeFontSizeOverride("font_size", 11);
		txtY.AddThemeFontSizeOverride("font_size", 11);
		txtZ.AddThemeFontSizeOverride("font_size", 11);

		void UpdateVal()
		{
			float.TryParse(txtX.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
			float.TryParse(txtY.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
			float.TryParse(txtZ.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
			current = new Vector3(x, y, z);
			onChanged(current);
		}

		txtX.TextChanged += (_) => UpdateVal();
		txtY.TextChanged += (_) => UpdateVal();
		txtZ.TextChanged += (_) => UpdateVal();

		row.AddChild(txtX);
		row.AddChild(txtY);
		row.AddChild(txtZ);

		parent.AddChild(row);
		return (txtX, txtY, txtZ);
	}

	public (LineEdit X, LineEdit Y) AddVector2Input(
		VBoxContainer parent,
		string labelText,
		Vector2 initialValue,
		Action<Vector2> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector2 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		txtX.AddThemeFontSizeOverride("font_size", 11);
		txtY.AddThemeFontSizeOverride("font_size", 11);

		void UpdateVal()
		{
			float.TryParse(txtX.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
			float.TryParse(txtY.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
			current = new Vector2(x, y);
			onChanged(current);
		}

		txtX.TextChanged += (_) => UpdateVal();
		txtY.TextChanged += (_) => UpdateVal();

		row.AddChild(txtX);
		row.AddChild(txtY);

		parent.AddChild(row);
		return (txtX, txtY);
	}

	public (LineEdit Input, Action<string> SetValue) AddAssetFilterDropdown(
		Control parent,
		string labelText,
		string initialText,
		Func<bool, List<string>> itemsProvider,
		Action<string> onChanged,
		string placeholder = "",
		float labelWidth = 140.0f,
		bool hasAllFoldersCheckbox = false,
		Action<string> onPlaySound = null)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Text = initialText ?? string.Empty;
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(txt);

		var btnDropdown = new Button();
		btnDropdown.Set("icon_max_width", 0);
		btnDropdown.Text = "▼";
		btnDropdown.CustomMinimumSize = new Vector2(24, 22);
		btnDropdown.AddThemeFontSizeOverride("font_size", 10);
		btnDropdown.FocusMode = FocusModeEnum.None;
		btnDropdown.TooltipText = TranslationServer.Translate("Select from imported assets");
		row.AddChild(btnDropdown);

		bool includeAllFolders = false;
		if (hasAllFoldersCheckbox)
		{
			var chkAll = new CheckBox();
			chkAll.Text = TranslationServer.Translate("All folders");
			chkAll.AddThemeFontSizeOverride("font_size", 10);
			chkAll.TooltipText = TranslationServer.Translate("Include models outside of projectiles folder");
			chkAll.Toggled += (pressed) =>
			{
				includeAllFolders = pressed;
			};
			row.AddChild(chkAll);
		}

		if (onPlaySound != null)
		{
			var btnPlay = new Button();
			btnPlay.Set("icon_max_width", 0);
			btnPlay.Text = "▶";
			btnPlay.CustomMinimumSize = new Vector2(26, 22);
			btnPlay.AddThemeFontSizeOverride("font_size", 11);
			btnPlay.FocusMode = FocusModeEnum.None;
			btnPlay.TooltipText = TranslationServer.Translate("Preview Sound");
			btnPlay.Pressed += () => onPlaySound(txt.Text);
			row.AddChild(btnPlay);
		}

		var popup = new PopupMenu();
		popup.AddThemeFontSizeOverride("font_size", 11);
		AddChild(popup);

		var currentFilteredItems = new List<string>();

		void ShowAssetPopup(bool showAll = false)
		{
			popup.Clear();
			currentFilteredItems.Clear();

			var allItems = itemsProvider(includeAllFolders);
			string currentText = txt.Text?.Trim() ?? "";
			string query = (showAll || allItems.Contains(currentText))
				? ""
				: currentText.ToLowerInvariant();

			var matched = string.IsNullOrEmpty(query)
				? allItems
				: allItems.FindAll(s => s.ToLowerInvariant().Contains(query));

			if (matched.Count == 0)
			{
				popup.AddItem(TranslationServer.Translate("No matching assets found"), 0);
				popup.SetItemDisabled(0, true);
			}
			else
			{
				int maxItems = Math.Min(matched.Count, 35);
				for (int i = 0; i < maxItems; i++)
				{
					currentFilteredItems.Add(matched[i]);
					popup.AddItem(matched[i], i);
				}
				if (matched.Count > 35)
				{
					popup.AddItem($"... ({matched.Count - 35} more)", 35);
					popup.SetItemDisabled(35, true);
				}
			}

			Vector2 globalPos = txt.GlobalPosition;
			Vector2 txtSize = txt.Size;
			popup.Position = new Vector2I((int)globalPos.X, (int)(globalPos.Y + txtSize.Y + 2));
			popup.ResetSize();
			popup.Popup();
		}

		btnDropdown.Pressed += () => ShowAssetPopup(true);

		popup.IdPressed += (long id) =>
		{
			int idx = (int)id;
			if (idx >= 0 && idx < currentFilteredItems.Count)
			{
				string selected = currentFilteredItems[idx];
				txt.Text = selected;
				onChanged(selected);
			}
		};

		txt.TextChanged += (val) => onChanged(val);

		parent.AddChild(row);
		return (txt, (val) => txt.Text = val ?? string.Empty);
	}

	public static List<string> ScanAvailableAssets(string category, bool includeAllFolders = false, string subFolder = null)
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace");
		string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");

		if (!System.IO.File.Exists(metadataPath))
		{
			string tPath = PathUtils.FindPath("MapTemplate/metadata.json");
			if (System.IO.File.Exists(tPath)) metadataPath = tPath;
		}

		if (!System.IO.File.Exists(metadataPath))
		{
			return new List<string>();
		}

		try
		{
			string jsonStr = System.IO.File.ReadAllText(metadataPath);
			var root = System.Text.Json.Nodes.JsonNode.Parse(jsonStr)?.AsObject();
			if (root != null)
			{
				var assetsObj = root["Assets"]?.AsObject() ?? (root["MapProperties"]?["Assets"]?.AsObject());
				if (assetsObj != null)
				{
					if (category == "audio" || category == "sound" || category == "sfx" || category == "music")
					{
						foreach (var key in new[] { "sfx", "music", "audio", "sound", "sounds" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject sObj)
							{
								foreach (var prop in sObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										result.Add(prop.Key);
									}
								}
							}
						}
					}
					else if (category == "vfx" || category == "vfx_spritesheets" || category == "spritesheets")
					{
						foreach (var key in new[] { "vfx_spritesheets", "vfx", "spritesheets" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject vObj)
							{
								foreach (var prop in vObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										result.Add(prop.Key);
									}
								}
							}
						}
					}
					else if (category == "decals" || category == "decal")
					{
						foreach (var key in new[] { "decals", "decal" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject dObj)
							{
								foreach (var prop in dObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										result.Add(prop.Key);
									}
								}
							}
						}
					}
					else if (category == "models" || category == "glb")
					{
						string defaultFolder = !string.IsNullOrEmpty(subFolder) ? subFolder : "projectiles";
						foreach (var modelKey in new[] { "glb", "models" })
						{
							if (assetsObj[modelKey] is System.Text.Json.Nodes.JsonObject glbObj)
							{
								foreach (var sub in glbObj)
								{
									bool matches = includeAllFolders || sub.Key.Equals(defaultFolder, StringComparison.OrdinalIgnoreCase);
									if (sub.Value is System.Text.Json.Nodes.JsonObject subObj)
									{
										if (matches)
										{
											foreach (var prop in subObj)
											{
												if (!string.IsNullOrWhiteSpace(prop.Key))
												{
													result.Add($"Assets/models/{sub.Key}/{prop.Key}");
												}
											}
										}
									}
									else if (!string.IsNullOrWhiteSpace(sub.Key))
									{
										if (includeAllFolders || sub.Key.Contains(defaultFolder, StringComparison.OrdinalIgnoreCase))
										{
											result.Add(sub.Key);
										}
									}
								}
							}
						}
					}
					else if (category == "ribbons" || category == "ribbon_textures")
					{
						foreach (var key in new[] { "ribbon_textures", "ribbons" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject rObj)
							{
								foreach (var prop in rObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										if (prop.Key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
										{
											result.Add(prop.Key);
										}
										else
										{
											result.Add($"Assets/textures/ribbons/{prop.Key}");
											result.Add(prop.Key);
										}
									}
								}
							}
						}
					}
					else if (category == "animations" || category == "ranim")
					{
						foreach (var key in new[] { "animations", "ranim", "anim" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject aObj)
							{
								foreach (var prop in aObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										result.Add(prop.Key);
									}
								}
							}
						}
					}
					else if (category == "icons" || category == "icon")
					{
						foreach (var key in new[] { "icons", "ui" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject iObj)
							{
								foreach (var prop in iObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										result.Add(prop.Key);
									}
								}
							}
						}
					}
					else if (category == "noise" || category == "noise_textures")
					{
						foreach (var key in new[] { "noise_textures", "noise" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject nObj)
							{
								foreach (var prop in nObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										if (prop.Key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
										{
											result.Add(prop.Key);
										}
										else
										{
											result.Add($"Assets/textures/{prop.Key}");
											result.Add(prop.Key);
										}
									}
								}
							}
						}
					}
					else if (category == "textures")
					{
						foreach (var key in new[] { "textures" })
						{
							if (assetsObj[key] is System.Text.Json.Nodes.JsonObject tObj)
							{
								foreach (var prop in tObj)
								{
									if (!string.IsNullOrWhiteSpace(prop.Key))
									{
										result.Add(prop.Key);
									}
								}
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[FloatingDialogBase] ScanAvailableAssets metadata error: {ex.Message}");
		}

		var list = new List<string>(result);
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	public Button AddButton(
		HBoxContainer parent,
		string text,
		Action onClick,
		string tooltip = "",
		int fontSize = 11,
		Vector2? minSize = null)
	{
		var btn = new Button();
		btn.Set("icon_max_width", 0);
		btn.Text = text;
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = FocusModeEnum.None;
		if (minSize.HasValue)
		{
			btn.CustomMinimumSize = minSize.Value;
		}
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}
		btn.Pressed += onClick;
		parent.AddChild(btn);
		return btn;
	}

	public SubViewportContainer Add3DViewportContainer(
		Control parent,
		Vector2 minSize,
		out SubViewport subViewport,
		out Camera3D camera,
		out DirectionalLight3D light)
	{
		var container = new SubViewportContainer();
		container.CustomMinimumSize = minSize;
		container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		container.Stretch = true;

		subViewport = new SubViewport();
		subViewport.Size = new Vector2I((int)minSize.X, (int)minSize.Y);
		subViewport.TransparentBg = false;
		subViewport.OwnWorld3D = true;

		var world = new World3D();
		var env = new Godot.Environment();
		env.BackgroundMode = Godot.Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.11f, 0.13f, 0.17f);
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.60f, 0.60f, 0.65f);
		env.AmbientLightEnergy = 0.75f;
		world.Environment = env;
		subViewport.World3D = world;

		light = new DirectionalLight3D();
		light.RotationDegrees = new Vector3(-30, 30, 0);
		light.LightEnergy = 0.75f;
		subViewport.AddChild(light);

		camera = new Camera3D();
		camera.Position = new Vector3(0, 1.2f, 2.8f);
		subViewport.AddChild(camera);

		container.AddChild(subViewport);
		parent.AddChild(container);
		return container;
	}

	public VBoxContainer CreateScrollBody(int minHeight = 350)
	{
		var scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(0, minHeight);
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var innerVBox = new VBoxContainer();
		innerVBox.AddThemeConstantOverride("separation", 8);
		innerVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		innerVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		scroll.AddChild(innerVBox);
		BodyContainer.AddChild(scroll);
		return innerVBox;
	}
}
