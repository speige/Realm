using Godot;
using System;

public partial class ChangeAssetTypeDialog : FloatingDialogBase
{
	private Label _lblPrompt;
	private OptionButton _optTargetType;
	private string _assetKey = "";
	private string _currentSubCategory = "";
	private Action<string> _onApplied;

	public ChangeAssetTypeDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Change Asset Type"), new Vector2(380, 200))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🔄 " + TranslationServer.Translate("SELECT NEW MODEL TYPE"), new Color(0.35f, 0.75f, 0.9f));

		_lblPrompt = new Label();
		_lblPrompt.AddThemeFontSizeOverride("font_size", 11);
		_lblPrompt.AutowrapMode = TextServer.AutowrapMode.Word;
		contentVBox.AddChild(_lblPrompt);

		var rowType = new HBoxContainer();
		rowType.AddThemeConstantOverride("separation", 8);

		var lblType = new Label();
		lblType.Text = TranslationServer.Translate("Target Type:");
		lblType.CustomMinimumSize = new Vector2(100, 0);
		lblType.AddThemeFontSizeOverride("font_size", 11);
		rowType.AddChild(lblType);

		_optTargetType = new OptionButton();
		_optTargetType.AddThemeFontSizeOverride("font_size", 11);
		_optTargetType.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optTargetType.AddItem(TranslationServer.Translate("3D Models (units)"), 0);
		_optTargetType.SetItemMetadata(0, "units");
		_optTargetType.AddItem(TranslationServer.Translate("3D Models (buildings)"), 1);
		_optTargetType.SetItemMetadata(1, "buildings");
		_optTargetType.AddItem(TranslationServer.Translate("3D Models (resources)"), 2);
		_optTargetType.SetItemMetadata(2, "resources");
		_optTargetType.AddItem(TranslationServer.Translate("3D Models (props)"), 3);
		_optTargetType.SetItemMetadata(3, "props");
		_optTargetType.AddItem(TranslationServer.Translate("3D Models (projectiles)"), 4);
		_optTargetType.SetItemMetadata(4, "projectiles");
		rowType.AddChild(_optTargetType);

		contentVBox.AddChild(rowType);
	}

	public void OpenForAsset(string assetKey, string currentSubCategory, Action<string> onApplied)
	{
		_assetKey = assetKey;
		_currentSubCategory = currentSubCategory ?? "props";
		_onApplied = onApplied;

		_lblPrompt.Text = string.Format(
			TranslationServer.Translate("Select new 3D model category for '{0}' (currently {1}):"),
			_assetKey,
			_currentSubCategory
		);

		for (int i = 0; i < _optTargetType.ItemCount; i++)
		{
			string meta = _optTargetType.GetItemMetadata(i).AsString();
			if (!meta.Equals(_currentSubCategory, StringComparison.OrdinalIgnoreCase))
			{
				_optTargetType.Selected = i;
				break;
			}
		}

		OpenDialog();
	}

	protected override void OnApply()
	{
		int sel = _optTargetType.Selected;
		if (sel >= 0 && sel < _optTargetType.ItemCount)
		{
			string targetSub = _optTargetType.GetItemMetadata(sel).AsString();
			_onApplied?.Invoke(targetSub);
		}
	}
}
