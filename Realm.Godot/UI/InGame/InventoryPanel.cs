using Godot;
using System;
using System.Collections.Generic;
using Arch.Core;

public class InventoryPanel
{
	private GridContainer _inventoryGrid;

	public InventoryPanel(GridContainer inventoryGrid)
	{
		_inventoryGrid = inventoryGrid;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_inventoryGrid == null) return;

		foreach (Node child in _inventoryGrid.GetChildren())
		{
			child.QueueFree();
		}

		if (viewModel.SelectedUnits.Count == 0)
		{
            for(int i = 0; i < 6; i++) {
                _inventoryGrid.AddChild(CreateBlackTile());
            }
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy || focusedUnit.IsBuilding)
		{
            // Empty 2x3 grid
            for(int i = 0; i < 6; i++) {
                _inventoryGrid.AddChild(CreateBlackTile());
            }
			return;
		}

        int totalItems = 0;
        
        // Add potions
        if (focusedUnit.Potions > 0)
        {
            var btn = CreateButton(
                "res://Assets/UI/alliance_flag.png",
                $"Healing Potion (Have: {focusedUnit.Potions})\nRestores 50 HP on use.",
                $" {focusedUnit.Potions} ",
                () => {
                    var selected = GameHost.Instance?.SelectedUnits;
                    if (selected != null && selected.Count == 1 && !selected[0].IsEnemy)
                    {
                        GameHost.Instance.UseHealingPotion(selected[0]);
                    }
                }
            );
            _inventoryGrid.AddChild(btn);
            totalItems++;
        }

        // Fill rest of the 2x3 grid
        for (int i = totalItems; i < 6; i++)
        {
            _inventoryGrid.AddChild(CreateBlackTile());
        }
	}

	private ColorRect CreateBlackTile()
	{
		var tile = new ColorRect();
		tile.Color = Colors.Black;
		tile.CustomMinimumSize = new Vector2(44, 44);
		tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tile.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		return tile;
	}

	private Button CreateButton(string iconPath, string tooltip, string text, Action callback)
	{
		var btn = new Button();
		btn.Flat = false;
		btn.Text = text;
		btn.ExpandIcon = true;
		btn.Icon = !string.IsNullOrEmpty(iconPath) ? GD.Load<Texture2D>(iconPath) : null;
		
		string transTooltip = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(transTooltip) ? tooltip : transTooltip;
		
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Disabled = false;
		btn.Modulate = Colors.White;

		btn.Pressed += () => {
			callback?.Invoke();
		};

		return btn;
	}
}
