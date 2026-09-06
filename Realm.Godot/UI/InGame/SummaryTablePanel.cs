using Godot;
using System;
using System.Collections.Generic;

public class SummaryTablePanel
{
	private PanelContainer _summaryPanel;
	private Label _titleLabel;
	private VBoxContainer _rowsContainer;

	public SummaryTablePanel(PanelContainer summaryPanel, Label titleLabel, VBoxContainer rowsContainer)
	{
		_summaryPanel = summaryPanel;
		_titleLabel = titleLabel;
		_rowsContainer = rowsContainer;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		_summaryPanel.Visible = viewModel.SummaryTableVisible;
		if (viewModel.SummaryTableVisible)
		{
			_titleLabel.Text = viewModel.SummaryTableTitle;

			foreach (Node child in _rowsContainer.GetChildren())
			{
				_rowsContainer.RemoveChild(child);
				child.QueueFree();
			}

			var headerRow = new HBoxContainer();
			headerRow.AddThemeConstantOverride("separation", 15);

			var colPlayer = new Label { Text = TranslationServer.Translate("PLAYER") };
			colPlayer.AddThemeFontSizeOverride("font_size", 12);
			colPlayer.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			colPlayer.CustomMinimumSize = new Vector2(100, 0);
			headerRow.AddChild(colPlayer);

			var colDmg = new Label { Text = TranslationServer.Translate("DAMAGE") };
			colDmg.AddThemeFontSizeOverride("font_size", 12);
			colDmg.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			colDmg.CustomMinimumSize = new Vector2(80, 0);
			colDmg.HorizontalAlignment = HorizontalAlignment.Right;
			headerRow.AddChild(colDmg);

			var colInc = new Label { Text = TranslationServer.Translate("INCOME") };
			colInc.AddThemeFontSizeOverride("font_size", 12);
			colInc.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			colInc.CustomMinimumSize = new Vector2(80, 0);
			colInc.HorizontalAlignment = HorizontalAlignment.Right;
			headerRow.AddChild(colInc);

			var colScore = new Label { Text = TranslationServer.Translate("SCORE") };
			colScore.AddThemeFontSizeOverride("font_size", 12);
			colScore.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			colScore.CustomMinimumSize = new Vector2(80, 0);
			colScore.HorizontalAlignment = HorizontalAlignment.Right;
			headerRow.AddChild(colScore);

			_rowsContainer.AddChild(headerRow);

			var sep = new HSeparator();
			sep.AddThemeColorOverride("separator_color", UIStyle.ColorBronze);
			_rowsContainer.AddChild(sep);

			foreach (var kvp in viewModel.SummaryTableRows)
			{
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 15);

				var playerLbl = new Label { Text = kvp.Key };
				playerLbl.AddThemeFontSizeOverride("font_size", 12);
				playerLbl.AddThemeColorOverride("font_color", Colors.White);
				playerLbl.CustomMinimumSize = new Vector2(100, 0);
				row.AddChild(playerLbl);

				var dmgLbl = new Label { Text = kvp.Value.Damage };
				dmgLbl.AddThemeFontSizeOverride("font_size", 12);
				dmgLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				dmgLbl.CustomMinimumSize = new Vector2(80, 0);
				dmgLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(dmgLbl);

				var incLbl = new Label { Text = kvp.Value.Income };
				incLbl.AddThemeFontSizeOverride("font_size", 12);
				incLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				incLbl.CustomMinimumSize = new Vector2(80, 0);
				incLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(incLbl);

				var scoreLbl = new Label { Text = kvp.Value.Score };
				scoreLbl.AddThemeFontSizeOverride("font_size", 12);
				scoreLbl.AddThemeColorOverride("font_color", Colors.White);
				scoreLbl.CustomMinimumSize = new Vector2(80, 0);
				scoreLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(scoreLbl);

				_rowsContainer.AddChild(row);
			}
		}
	}
}
