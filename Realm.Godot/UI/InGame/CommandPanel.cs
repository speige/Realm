using Godot;
using System;
using System.Collections.Generic;

public class CommandPanel
{
	private GridContainer _commandGrid;
	private Button _btnMove;
	private Button _btnStop;
	private Button _btnHold;
	private Button _btnBuild;
	private Button _btnAttack;
	private Button _btnPatrol;

	private Button _btnBuildCastle;
	private Button _btnBuildTower;
	private Button _btnCancelBuild;

	private Button _btnTrainSoldier;
	private Button _btnTrainArcher;
	private Button _btnTrainPriest;
	private Button _btnTrainWorker;
	private Button _btnBuyPotion;

	private Button _btnUpgradeWeapons;
	private Button _btnUpgradeShields;
	private Button _btnUpgradeHarvesting;
	private Button _btnUpgradeTower;
	private Button _btnSetRally;

	private Button _btnFireball;
	private Button _btnLightning;
	private Button _btnHolyLight;
	private Button _btnUsePotion;

	private List<Button> _dynamicBuildButtons = new();

	public CommandPanel(GridContainer commandGrid, 
		Button btnMove, Button btnStop, Button btnHold, Button btnBuild, Button btnAttack, Button btnPatrol,
		Button btnBuildCastle, Button btnBuildTower, Button btnCancelBuild,
		Button btnTrainSoldier, Button btnTrainArcher, Button btnTrainPriest, Button btnTrainWorker, Button btnBuyPotion,
		Button btnUpgradeWeapons, Button btnUpgradeShields, Button btnUpgradeHarvesting, Button btnUpgradeTower, Button btnSetRally,
		Button btnFireball, Button btnLightning, Button btnHolyLight, Button btnUsePotion)
	{
		_commandGrid = commandGrid;
		_btnMove = btnMove;
		_btnStop = btnStop;
		_btnHold = btnHold;
		_btnBuild = btnBuild;
		_btnAttack = btnAttack;
		_btnPatrol = btnPatrol;
		_btnBuildCastle = btnBuildCastle;
		_btnBuildTower = btnBuildTower;
		_btnCancelBuild = btnCancelBuild;
		_btnTrainSoldier = btnTrainSoldier;
		_btnTrainArcher = btnTrainArcher;
		_btnTrainPriest = btnTrainPriest;
		_btnTrainWorker = btnTrainWorker;
		_btnBuyPotion = btnBuyPotion;
		_btnUpgradeWeapons = btnUpgradeWeapons;
		_btnUpgradeShields = btnUpgradeShields;
		_btnUpgradeHarvesting = btnUpgradeHarvesting;
		_btnUpgradeTower = btnUpgradeTower;
		_btnSetRally = btnSetRally;
		_btnFireball = btnFireball;
		_btnLightning = btnLightning;
		_btnHolyLight = btnHolyLight;
		_btnUsePotion = btnUsePotion;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_commandGrid == null) return;

		foreach (var btn in _dynamicBuildButtons)
		{
			if (GodotObject.IsInstanceValid(btn))
			{
				btn.QueueFree();
			}
		}
		_dynamicBuildButtons.Clear();

		foreach (Node child in _commandGrid.GetChildren())
		{
			_commandGrid.RemoveChild(child);
		}

		if (viewModel.SelectedUnits.Count == 0)
		{
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy)
		{
			return;
		}

		if (focusedUnit.IsBuilding)
		{
			if (focusedUnit.UnitId == "castle")
			{
				_commandGrid.AddChild(_btnTrainSoldier);
				_commandGrid.AddChild(_btnTrainArcher);
				_commandGrid.AddChild(_btnTrainPriest);
				_commandGrid.AddChild(_btnTrainWorker);
				_commandGrid.AddChild(_btnSetRally);
				_commandGrid.AddChild(_btnBuyPotion);

				if (GameHost.Instance != null)
				{
					ApplyUpgradeButtonState(_btnUpgradeWeapons, GameHost.Instance.HasWeaponsUpgrade, "MAXED: Weapons");
					_commandGrid.AddChild(_btnUpgradeWeapons);
					ApplyUpgradeButtonState(_btnUpgradeShields, GameHost.Instance.HasShieldsUpgrade, "MAXED: Armor");
					_commandGrid.AddChild(_btnUpgradeShields);
					ApplyUpgradeButtonState(_btnUpgradeHarvesting, GameHost.Instance.HasHarvestingUpgrade, "MAXED: Harvest");
					_commandGrid.AddChild(_btnUpgradeHarvesting);
				}
			}
			else if (focusedUnit.UnitId == "tower")
			{
				if (GameHost.Instance != null)
				{
					bool isMaxed = false;
					if (GameHost.Instance.EcsWorld.IsAlive(focusedUnit.Entity) && GameHost.Instance.EcsWorld.Has<GameHost.TowerUpgradeLevel>(focusedUnit.Entity))
					{
						isMaxed = GameHost.Instance.EcsWorld.Get<GameHost.TowerUpgradeLevel>(focusedUnit.Entity).Value >= 3;
					}
					ApplyUpgradeButtonState(_btnUpgradeTower, isMaxed, "MAXED: Tower Level 3");
				}
				_commandGrid.AddChild(_btnUpgradeTower);
				_btnFireball.GetParent()?.RemoveChild(_btnFireball);
				_commandGrid.AddChild(_btnFireball);
				_btnLightning.GetParent()?.RemoveChild(_btnLightning);
				_commandGrid.AddChild(_btnLightning);
			}
		}
		else
		{
			if (viewModel.IsBuildSubMenuOpen)
			{
				if (GameHost.Instance != null && GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta) && meta.BuildOptions != null)
				{
					foreach (var buildOpt in meta.BuildOptions)
					{
						if (GameHost.UnitRegistry.TryGetValue(buildOpt, out var structureMeta))
						{
							var btn = new Button();
							_dynamicBuildButtons.Add(btn);
							string iconPath = GetUnitIcon(buildOpt);
							string tooltipText = $"Build {structureMeta.Name} (Cost: {structureMeta.CostGold} Gold, {structureMeta.CostWood} Wood, {structureMeta.CostStone} Stone)";
							string structureType = buildOpt;
							
							SetupHUDButton(btn, iconPath, tooltipText, () => GameHost.Instance?.EnterBuildingPlacement(structureType));
							_commandGrid.AddChild(btn);
						}
					}
				}
				else
				{
					_commandGrid.AddChild(_btnBuildCastle);
					_commandGrid.AddChild(_btnBuildTower);
				}
				_commandGrid.AddChild(_btnCancelBuild);
			}
			else
			{
				_commandGrid.AddChild(_btnMove);
				_commandGrid.AddChild(_btnStop);
				_commandGrid.AddChild(_btnHold);
				_commandGrid.AddChild(_btnAttack);
				_commandGrid.AddChild(_btnPatrol);
				
				bool canBuild = false;
				if (GameHost.Instance != null && GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta))
				{
					canBuild = meta.BuildOptions != null && meta.BuildOptions.Length > 0;
				}
				if (canBuild)
				{
					_commandGrid.AddChild(_btnBuild);
				}

				if (focusedUnit.UnitId == "priest")
				{
					_btnHolyLight.GetParent()?.RemoveChild(_btnHolyLight);
					_commandGrid.AddChild(_btnHolyLight);
				}

				if (_btnUsePotion != null)
				{
					_btnUsePotion.Text = $" {focusedUnit.Potions} ";
					_btnUsePotion.TooltipText = $"[I] Healing Potion (Have: {focusedUnit.Potions})\nRestores 50 HP on use.";
					_btnUsePotion.Disabled = focusedUnit.Potions <= 0;
					_btnUsePotion.GetParent()?.RemoveChild(_btnUsePotion);
					_commandGrid.AddChild(_btnUsePotion);
				}
			}
		}
	}

	private void ApplyUpgradeButtonState(Button btn, bool isMaxed, string maxedLabel)
	{
		if (isMaxed)
		{
			btn.Disabled = true;
			btn.TooltipText = $"✓ {maxedLabel} — Already researched!";
			btn.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
		}
		else
		{
			btn.Disabled = false;
			btn.Modulate = Colors.White;
		}
	}

	private string GetUnitIcon(string unitId)
	{
		return unitId switch
		{
			"soldier" => "res://Assets/UI/heavy_knight.png",
			"archer" => "res://Assets/UI/elf_warrior.png",
			"priest" => "res://Assets/UI/alliance_flag.png",
			"castle" => "res://Assets/UI/moonlit_castle.png",
			"tower" => "res://Assets/UI/unknown_unit_1.png",
			_ => "res://Assets/UI/unit_placeholder.png"
		};
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.TooltipText = tooltip;
		btn.CustomMinimumSize = new Vector2(80, 80);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;

		if (tooltip.StartsWith("[") && tooltip.Contains("]"))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => onClick?.Invoke();
	}
}
