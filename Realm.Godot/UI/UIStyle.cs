using Godot;

public static class UIStyle
{

	public static readonly Color ColorBgDark = new Color(0.08f, 0.08f, 0.1f, 1.0f);       // Deep charcoal/midnight
	public static readonly Color ColorPanelBg = new Color(0.13f, 0.14f, 0.17f, 0.95f);    // Granite slate
	public static readonly Color ColorPanelLight = new Color(0.18f, 0.19f, 0.23f, 0.95f); // Lighter granite
	
	public static readonly Color ColorBronze = new Color(0.6f, 0.5f, 0.35f, 1.0f);        // Bronze border
	public static readonly Color ColorGold = new Color(0.95f, 0.82f, 0.55f, 1.0f);        // Bright gold text
	public static readonly Color ColorGoldDull = new Color(0.75f, 0.67f, 0.5f, 1.0f);      // Dull gold
	
	public static readonly Color ColorCyanGlow = new Color(0.15f, 0.65f, 1.0f, 1.0f);     // Runic cyan glow
	public static readonly Color ColorCyanGlowDim = new Color(0.15f, 0.65f, 1.0f, 0.4f);  // Dim cyan
	

	public static StyleBox CreateBgTexture(string path)
	{
		var texture = GD.Load<Texture2D>(path);
		var style = new StyleBoxTexture();
		style.Texture = texture;
		return style;
	}

	public static StyleBox CreateBgGradient()
	{
		return CreateBgTexture("res://Assets/UI/menu_background_with_frame.jpg");
	}


	public static StyleBox CreateStonePanel(bool isLight = false)
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_slate_panel.jpg");
		
		if (isLight)
		{
			style.TextureMarginLeft = 32;
			style.TextureMarginRight = 32;
			style.TextureMarginTop = 32;
			style.TextureMarginBottom = 32;
			
			style.ContentMarginLeft = 16;
			style.ContentMarginRight = 16;
			style.ContentMarginTop = 16;
			style.ContentMarginBottom = 16;
			
			style.ModulateColor = new Color(0.9f, 0.9f, 0.93f, 0.95f); // Slightly lighter for sub-panels
		}
		else
		{
			style.TextureMarginLeft = 40;
			style.TextureMarginRight = 40;
			style.TextureMarginTop = 40;
			style.TextureMarginBottom = 40;
			
			style.ContentMarginLeft = 24;
			style.ContentMarginRight = 24;
			style.ContentMarginTop = 24;
			style.ContentMarginBottom = 24;
			
			style.ModulateColor = new Color(0.72f, 0.72f, 0.75f, 0.98f); // Darker base frame
		}
		return style;
	}


	public static StyleBox CreatePillarPanel(bool isLeft)
	{
		return new StyleBoxEmpty();
	}


	public static StyleBox CreateHUDPillarPanel(bool isLeft)
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/hud_stone_pillar.png");
		style.TextureMarginLeft = 40;
		style.TextureMarginRight = 40;
		style.TextureMarginTop = 150;
		style.TextureMarginBottom = 150;
		return style;
	}


	public static StyleBox CreateButtonNormal()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_button_premium.png");
		style.TextureMarginLeft = 30;
		style.TextureMarginRight = 30;
		style.TextureMarginTop = 15;
		style.TextureMarginBottom = 15;
		
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		
		style.ModulateColor = new Color(0.9f, 0.9f, 0.93f);
		return style;
	}


	public static StyleBox CreateButtonHover()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_button_premium.png");
		style.TextureMarginLeft = 30;
		style.TextureMarginRight = 30;
		style.TextureMarginTop = 15;
		style.TextureMarginBottom = 15;
		
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		
		style.ModulateColor = new Color(1.15f, 1.08f, 0.92f); // Golden highlight on hover
		return style;
	}


	public static StyleBox CreateButtonPressed()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_button_premium.png");
		style.TextureMarginLeft = 30;
		style.TextureMarginRight = 30;
		style.TextureMarginTop = 15;
		style.TextureMarginBottom = 15;
		
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		
		style.ModulateColor = new Color(0.7f, 0.85f, 1.0f); // Blue neon tint when clicked
		return style;
	}


	public static StyleBoxFlat CreateTextInput(bool hasFocus = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.06f, 0.07f, 0.9f);
		style.BorderColor = hasFocus ? ColorCyanGlow : new Color(0.3f, 0.3f, 0.35f);
		style.SetBorderWidthAll(2);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}


	public static void ApplyCheckboxStyle(CheckBox cb)
	{
		var checkedTex = GD.Load<Texture2D>("res://Assets/UI/checked_box.jpg");
		var uncheckedTex = GD.Load<Texture2D>("res://Assets/UI/unchecked_box.jpg");
		
		cb.AddThemeIconOverride("checked", checkedTex);
		cb.AddThemeIconOverride("unchecked", uncheckedTex);
		cb.AddThemeIconOverride("checked_disabled", checkedTex);
		cb.AddThemeIconOverride("unchecked_disabled", uncheckedTex);
		
		cb.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		cb.AddThemeColorOverride("font_hover_color", ColorGold);
		cb.AddThemeColorOverride("font_pressed_color", ColorCyanGlow);
		cb.AddThemeFontSizeOverride("font_size", 14);
		cb.AddThemeConstantOverride("icon_max_width", 0);
	}

	public static StyleBoxFlat CreateSliderTrack()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.05f, 0.07f, 1.0f);
		style.BorderColor = new Color(0.3f, 0.3f, 0.35f, 1.0f);
		style.SetBorderWidthAll(1);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.CornerRadiusTopLeft = 2;
		style.CornerRadiusTopRight = 2;
		style.CornerRadiusBottomLeft = 2;
		style.CornerRadiusBottomRight = 2;
		return style;
	}

	public static StyleBoxFlat CreateSliderFill()
	{
		var style = new StyleBoxFlat();
		style.BgColor = ColorCyanGlow;
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.CornerRadiusTopLeft = 2;
		style.CornerRadiusTopRight = 2;
		style.CornerRadiusBottomLeft = 2;
		style.CornerRadiusBottomRight = 2;
		return style;
	}


	public static void ApplyTitle(Label label, string text, int fontSize = 36)
	{
		label.Text = TranslationServer.Translate(text);
		label.AddThemeColorOverride("font_color", ColorGold);
		label.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		label.AddThemeConstantOverride("outline_size", 8);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
	}

	public static void ApplyButtonText(Button button, string text, int fontSize = 18)
	{
		button.Text = TranslationServer.Translate(text);
		button.AddThemeColorOverride("font_color", ColorGoldDull);
		button.AddThemeColorOverride("font_hover_color", ColorGold);
		button.AddThemeColorOverride("font_pressed_color", ColorCyanGlow);
		button.AddThemeFontSizeOverride("font_size", fontSize);
	}
}
