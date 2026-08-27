using Godot;
using System;

public class GlobalObjectOverridesUndoAction : IEditorAction
{
	private readonly string _assetKey;
	private readonly GlobalObjectOverridesDialog.GlobalOverridesSnapshot _before;
	private readonly GlobalObjectOverridesDialog.GlobalOverridesSnapshot _after;

	public GlobalObjectOverridesUndoAction(string assetKey, GlobalObjectOverridesDialog.GlobalOverridesSnapshot before, GlobalObjectOverridesDialog.GlobalOverridesSnapshot after)
	{
		_assetKey = assetKey;
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		ApplySnapshot(_before);
	}

	public void Redo()
	{
		ApplySnapshot(_after);
	}

	private void ApplySnapshot(GlobalObjectOverridesDialog.GlobalOverridesSnapshot snapshot)
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_assetKey)) return;

		GameHost.Instance.SetModelScale(_assetKey, snapshot.Scale);
		GameHost.Instance.SetModelYOffset(_assetKey, snapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_assetKey, snapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_assetKey, snapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_assetKey, snapshot.ColorTint);
		GameHost.Instance.SetModelNormalMode(_assetKey, snapshot.NormalMode);
		GameHost.Instance.SetModelNormalizeLuminance(_assetKey, snapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_assetKey, snapshot.IgnorePlayerColor);

		GameHost.Instance.RefreshAllPlacedObjectModels(_assetKey);
		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
	}
}

public partial class GlobalObjectOverridesDialog : FloatingDialogBase
{
	public struct GlobalOverridesSnapshot
	{
		public float Scale;
		public float YOffset;
		public float CollisionCircleRatio;
		public float Brightness;
		public Color ColorTint;
		public GameHost.ModelNormalMode NormalMode;
		public bool NormalizeLuminance;
		public bool IgnorePlayerColor;
	}

	private string _currentAssetKey = "";
	private Node _currentSelectedObject;
	private GlobalOverridesSnapshot _initialSnapshot;
	private bool _isUpdatingUI;

	private HSlider _sldScale;
	private Label _lblScaleValue;
	private HSlider _sldYOffset;
	private Label _lblYOffsetValue;
	private HSlider _sldCollisionCircle;
	private Label _lblCollisionCircleValue;
	private HSlider _sldBrightness;
	private Label _lblBrightnessValue;
	private HSlider _sldColorTint;
	private ColorPickerButton _cpkColorTint;
	private OptionButton _optNormalMode;
	private CheckBox _chkNormalizeLuminance;
	private CheckBox _chkIgnorePlayerColor;

	public GlobalObjectOverridesDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Global Object Overrides"), new Vector2(380, 420))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var descLabel = new Label();
		descLabel.Text = TranslationServer.Translate("Modify visual and collision settings for all instances of this model.");
		descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		descLabel.AddThemeFontSizeOverride("font_size", 11);
		descLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		BodyContainer.AddChild(descLabel);

		var grid = new VBoxContainer();
		grid.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(grid);

		(_sldScale, _lblScaleValue) = CreateSliderField(grid, TranslationServer.Translate("Scale"), 0.1f, 10.0f, 0.05f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelScale(_currentAssetKey, val);
		});

		(_sldYOffset, _lblYOffsetValue) = CreateSliderField(grid, TranslationServer.Translate("Y-Offset"), -10.0f, 10.0f, 0.05f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelYOffset(_currentAssetKey, val);
		});

		(_sldCollisionCircle, _lblCollisionCircleValue) = CreateSliderField(grid, TranslationServer.Translate("Collision Circle"), 0.1f, 10.0f, 0.05f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelCollisionCircleRatio(_currentAssetKey, val);
		});

		(_sldBrightness, _lblBrightnessValue) = CreateSliderField(grid, TranslationServer.Translate("Brightness"), 0.10f, 2.0f, 0.02f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelBrightness(_currentAssetKey, val);
		});

		var tintRow = new HBoxContainer();
		var lblTint = new Label();
		lblTint.Text = TranslationServer.Translate("Tint");
		lblTint.CustomMinimumSize = new Vector2(110, 0);
		lblTint.AddThemeFontSizeOverride("font_size", 11);
		tintRow.AddChild(lblTint);

		_sldColorTint = new HSlider();
		_sldColorTint.MinValue = 0.0f;
		_sldColorTint.MaxValue = 1.0f;
		_sldColorTint.Step = 0.01f;
		_sldColorTint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tintRow.AddChild(_sldColorTint);

		_cpkColorTint = new ColorPickerButton();
		_cpkColorTint.CustomMinimumSize = new Vector2(36, 22);
		_cpkColorTint.EditAlpha = false;
		tintRow.AddChild(_cpkColorTint);
		grid.AddChild(tintRow);

		_sldColorTint.ValueChanged += (double val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			Color tintColor = (val <= 0.0) ? new Color(1.0f, 1.0f, 1.0f) : Color.FromHsv((float)val, 0.75f, 1.0f);
			_cpkColorTint.Color = tintColor;
			GameHost.Instance.SetModelColorTint(_currentAssetKey, tintColor);
		};

		_cpkColorTint.ColorChanged += (Color color) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelColorTint(_currentAssetKey, color);
		};

		var normalRow = new HBoxContainer();
		var lblNormal = new Label();
		lblNormal.Text = TranslationServer.Translate("Normals");
		lblNormal.CustomMinimumSize = new Vector2(110, 0);
		lblNormal.AddThemeFontSizeOverride("font_size", 11);
		normalRow.AddChild(lblNormal);

		_optNormalMode = new OptionButton();
		_optNormalMode.AddItem(TranslationServer.Translate("Original"), 0);
		_optNormalMode.AddItem(TranslationServer.Translate("Smooth Normals"), 1);
		_optNormalMode.AddItem(TranslationServer.Translate("Flat Normals"), 2);
		_optNormalMode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optNormalMode.ItemSelected += (long index) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelNormalMode(_currentAssetKey, (GameHost.ModelNormalMode)(int)index);
		};
		normalRow.AddChild(_optNormalMode);
		grid.AddChild(normalRow);

		_chkNormalizeLuminance = CreateCheckBox(grid, TranslationServer.Translate("Normalize Luminosity"), (pressed) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelNormalizeLuminance(_currentAssetKey, pressed);
		});

		_chkIgnorePlayerColor = CreateCheckBox(grid, TranslationServer.Translate("Ignore Player Color"), (pressed) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelIgnorePlayerColor(_currentAssetKey, pressed);
			GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		});
	}

	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject) || GameHost.Instance == null) return;

		_currentSelectedObject = selectedObject;
		_currentAssetKey = GameHost.Instance.GetSelectedEntityOrAssetKey(selectedObject);
		if (string.IsNullOrEmpty(_currentAssetKey)) return;

		_initialSnapshot = new GlobalOverridesSnapshot
		{
			Scale = GameHost.Instance.GetModelScale(selectedObject),
			YOffset = GameHost.Instance.GetModelYOffset(_currentAssetKey),
			CollisionCircleRatio = GameHost.Instance.GetModelCollisionCircleRatio(_currentAssetKey),
			Brightness = GameHost.Instance.GetModelBrightness(_currentAssetKey),
			ColorTint = GameHost.Instance.GetModelColorTint(_currentAssetKey),
			NormalMode = GameHost.Instance.GetModelNormalMode(_currentAssetKey),
			NormalizeLuminance = GameHost.Instance.GetModelNormalizeLuminance(_currentAssetKey),
			IgnorePlayerColor = GameHost.Instance.GetModelIgnorePlayerColor(_currentAssetKey)
		};

		TitleLabel.Text = $"{TranslationServer.Translate("Global Overrides")} - {_currentAssetKey}";

		_isUpdatingUI = true;
		_sldScale.Value = _initialSnapshot.Scale;
		_lblScaleValue.Text = _initialSnapshot.Scale.ToString("0.00");

		_sldYOffset.Value = _initialSnapshot.YOffset;
		_lblYOffsetValue.Text = _initialSnapshot.YOffset.ToString("0.00");

		_sldCollisionCircle.Value = _initialSnapshot.CollisionCircleRatio;
		_lblCollisionCircleValue.Text = _initialSnapshot.CollisionCircleRatio.ToString("0.00");

		_sldBrightness.Value = _initialSnapshot.Brightness;
		_lblBrightnessValue.Text = _initialSnapshot.Brightness.ToString("0.00");

		_cpkColorTint.Color = _initialSnapshot.ColorTint;
		if (Mathf.Abs(_initialSnapshot.ColorTint.R - 1.0f) < 0.001f && Mathf.Abs(_initialSnapshot.ColorTint.G - 1.0f) < 0.001f && Mathf.Abs(_initialSnapshot.ColorTint.B - 1.0f) < 0.001f)
		{
			_sldColorTint.Value = 0.0f;
		}
		else
		{
			_sldColorTint.Value = _initialSnapshot.ColorTint.H;
		}

		_optNormalMode.Selected = (int)_initialSnapshot.NormalMode;
		_chkNormalizeLuminance.ButtonPressed = _initialSnapshot.NormalizeLuminance;
		_chkIgnorePlayerColor.ButtonPressed = _initialSnapshot.IgnorePlayerColor;
		_isUpdatingUI = false;

		OpenDialog();
	}

	protected override void OnApply()
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;

		var currentSnapshot = new GlobalOverridesSnapshot
		{
			Scale = (float)_sldScale.Value,
			YOffset = (float)_sldYOffset.Value,
			CollisionCircleRatio = (float)_sldCollisionCircle.Value,
			Brightness = (float)_sldBrightness.Value,
			ColorTint = _cpkColorTint.Color,
			NormalMode = (GameHost.ModelNormalMode)_optNormalMode.Selected,
			NormalizeLuminance = _chkNormalizeLuminance.ButtonPressed,
			IgnorePlayerColor = _chkIgnorePlayerColor.ButtonPressed
		};

		var action = new GlobalObjectOverridesUndoAction(_currentAssetKey, _initialSnapshot, currentSnapshot);
		EditorHistoryManager.RecordAction(action);

		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
		Hud?.ShowFeedback(TranslationServer.Translate("Global object overrides applied"));
	}

	protected override void OnCancel()
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;

		GameHost.Instance.SetModelScale(_currentAssetKey, _initialSnapshot.Scale);
		GameHost.Instance.SetModelYOffset(_currentAssetKey, _initialSnapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_currentAssetKey, _initialSnapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_currentAssetKey, _initialSnapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_currentAssetKey, _initialSnapshot.ColorTint);
		GameHost.Instance.SetModelNormalMode(_currentAssetKey, _initialSnapshot.NormalMode);
		GameHost.Instance.SetModelNormalizeLuminance(_currentAssetKey, _initialSnapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_currentAssetKey, _initialSnapshot.IgnorePlayerColor);

		GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
	}

	private (HSlider Slider, Label ValueLabel) CreateSliderField(VBoxContainer parent, string labelText, float min, float max, float step, Action<float> onChanged)
	{
		var row = new HBoxContainer();

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(110, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var slider = new HSlider();
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(slider);

		var valLbl = new Label();
		valLbl.Text = "0.00";
		valLbl.CustomMinimumSize = new Vector2(40, 0);
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);

		slider.ValueChanged += (double val) =>
		{
			valLbl.Text = ((float)val).ToString("0.00");
			onChanged((float)val);
		};

		parent.AddChild(row);
		return (slider, valLbl);
	}

	private CheckBox CreateCheckBox(VBoxContainer parent, string labelText, Action<bool> onChanged)
	{
		var chk = new CheckBox();
		chk.Text = labelText;
		chk.AddThemeFontSizeOverride("font_size", 11);
		chk.Toggled += (pressed) => onChanged(pressed);
		parent.AddChild(chk);
		return chk;
	}
}
