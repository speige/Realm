using Godot;
using System;
using System.Collections.Generic;
using Arch.Core;

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

	private Button _btnUsePotion;

	private List<Button> _dynamicBuildButtons = new();

	public CommandPanel(GridContainer commandGrid, 
		Button btnMove, Button btnStop, Button btnHold, Button btnBuild, Button btnAttack, Button btnPatrol,
		Button btnBuildCastle, Button btnBuildTower, Button btnCancelBuild,
		Button btnTrainSoldier, Button btnTrainArcher, Button btnTrainPriest, Button btnTrainWorker, Button btnBuyPotion,
		Button btnUpgradeWeapons, Button btnUpgradeShields, Button btnUpgradeHarvesting, Button btnUpgradeTower, Button btnSetRally,
		Button btnUsePotion)
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
		_btnUsePotion = btnUsePotion;
	}

	public class CommandCardItem
	{
		public string Id { get; set; }
		public string IconPath { get; set; }
		public string Tooltip { get; set; }
		public Action Callback { get; set; }
		public Key Hotkey { get; set; }
		public Func<bool> IsDisabled { get; set; }
		public Func<string> GetButtonText { get; set; }
	}

	private int _pageIndex = 0;
	private List<CommandCardItem> _activeItems = new();
	private Entity _lastFocusedEntity = Entity.Null;

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
			child.QueueFree();
		}

		if (viewModel.SelectedUnits.Count == 0)
		{
			_lastFocusedEntity = Entity.Null;
			_pageIndex = 0;
			_activeItems.Clear();
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy)
		{
			_lastFocusedEntity = Entity.Null;
			_pageIndex = 0;
			_activeItems.Clear();
			return;
		}

		if (focusedUnit.Entity != _lastFocusedEntity)
		{
			_pageIndex = 0;
			_lastFocusedEntity = focusedUnit.Entity;
		}

		_activeItems = GetCommandCardItems(focusedUnit, viewModel.IsBuildSubMenuOpen);

		int totalItems = _activeItems.Count;
		
		Key[] gridHotkeys = new Key[] {
			Key.Q, Key.W, Key.E, Key.R,
			Key.A, Key.S, Key.D, Key.F,
			Key.Z, Key.X, Key.C, Key.V
		};
		
		int pageOffset = _pageIndex * 11;
		for (int i = 0; i < totalItems; i++)
		{
			int localIdx = -1;
			if (totalItems <= 12) {
				localIdx = i;
			} else {
				if (i >= pageOffset && i < pageOffset + 11) {
					localIdx = i - pageOffset;
				}
			}
			
			if (localIdx >= 0 && localIdx < 12) {
				_activeItems[i].Hotkey = gridHotkeys[localIdx];
				_activeItems[i].Tooltip = System.Text.RegularExpressions.Regex.Replace(_activeItems[i].Tooltip, @"^\[.*?\] ", "[" + gridHotkeys[localIdx].ToString() + "] ");
			} else {
				_activeItems[i].Hotkey = Key.None;
			}
		}
		if (totalItems <= 12)
		{
			_pageIndex = 0;
			for (int i = 0; i < 12; i++)
			{
				if (i < totalItems)
				{
					_commandGrid.AddChild(CreateButtonForItem(_activeItems[i]));
				}
				else
				{
					_commandGrid.AddChild(CreateBlackTile());
				}
			}
		}
		else
		{
			int numPages = (totalItems + 10) / 11;
			if (_pageIndex >= numPages) _pageIndex = 0;

			int startIndex = _pageIndex * 11;
			for (int i = 0; i < 11; i++)
			{
				int itemIndex = startIndex + i;
				if (itemIndex < totalItems)
				{
					_commandGrid.AddChild(CreateButtonForItem(_activeItems[itemIndex]));
				}
				else
				{
					_commandGrid.AddChild(CreateBlackTile());
				}
			}

			_commandGrid.AddChild(CreateCycleButton(numPages));
		}
	}

	public bool HandleHotkey(Key keycode)
	{
		if (_activeItems.Count == 0) return false;

		int totalItems = _activeItems.Count;
		if (totalItems <= 12)
		{
			for (int i = 0; i < totalItems; i++)
			{
				var item = _activeItems[i];
				if (item.Hotkey == keycode && !(item.IsDisabled?.Invoke() ?? false))
				{
					item.Callback?.Invoke();
					return true;
				}
			}
		}
		else
		{
			int numPages = (totalItems + 10) / 11;
			if (_pageIndex >= numPages) _pageIndex = 0;
			int startIndex = _pageIndex * 11;
			for (int i = 0; i < 11; i++)
			{
				int itemIndex = startIndex + i;
				if (itemIndex < totalItems)
				{
					var item = _activeItems[itemIndex];
					if (item.Hotkey == keycode && !(item.IsDisabled?.Invoke() ?? false))
					{
						item.Callback?.Invoke();
						return true;
					}
				}
			}
		}
		return false;
	}

	private Control CreateBlackTile()
	{
		var tile = new ColorRect();
		tile.Color = Colors.Black;
		tile.CustomMinimumSize = new Vector2(80, 80);
		tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tile.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		return tile;
	}

	private Button CreateCycleButton(int numPages)
	{
		var btn = new Button();
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>("res://Assets/UI/search_icon_clean.png");
		btn.TooltipText = TranslationServer.Translate("Cycle Abilities / Commands");
		btn.CustomMinimumSize = new Vector2(80, 80);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.AddThemeConstantOverride("icon_max_width", 72);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		var label = new Label();
		label.Text = TranslationServer.Translate("CYCLE");
		label.AddThemeFontSizeOverride("font_size", 10);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 4);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
		label.OffsetBottom = -3;
		label.GrowHorizontal = Control.GrowDirection.Both;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		btn.AddChild(label);

		btn.Pressed += () =>
		{
			_pageIndex = (_pageIndex + 1) % numPages;
			if (GameHost.Instance != null)
			{
				InGameHUD.Instance?.RefreshUI(GameHost.Instance.SelectedUnits);
			}
		};

		return btn;
	}

	private Button CreateButtonForItem(CommandCardItem item)
	{
		var btn = new Button();
		btn.Flat = false;
		btn.Text = item.GetButtonText?.Invoke() ?? "";
		btn.ExpandIcon = true;
		btn.Icon = !string.IsNullOrEmpty(item.IconPath) ? GD.Load<Texture2D>(item.IconPath) : null;
		
		string transTooltip = TranslationServer.Translate(item.Tooltip);
		btn.TooltipText = string.IsNullOrEmpty(transTooltip) ? item.Tooltip : transTooltip;
		
		btn.CustomMinimumSize = new Vector2(80, 80);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 72);

		if (item.Hotkey != Key.None)
		{
			string hotkeyText = item.Hotkey.ToString();
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

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		bool disabled = item.IsDisabled?.Invoke() ?? false;
		if (disabled)
		{
			btn.Disabled = true;
			btn.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
		}
		else
		{
			btn.Disabled = false;
			btn.Modulate = Colors.White;
		}

		btn.Pressed += () => {
			item.Callback?.Invoke();
		};

		return btn;
	}

	private List<CommandCardItem> GetCommandCardItems(InGameHUDViewModel.SelectedUnitInfo focusedUnit, bool isBuildSubMenuOpen)
	{
		var items = new List<CommandCardItem>();
		if (focusedUnit == null) return items;

		if (focusedUnit.IsUnderConstruction)
		{
			items.Add(new CommandCardItem
			{
				Id = "stop",
				IconPath = "res://Assets/UI/cancel_button_2.png",
				Tooltip = "[S] Cancel Construction",
				Hotkey = Key.S,
				// Future: Implement cancel construction logic
				Callback = () => { } 
			});
			return items;
		}

		bool hasMetadata = GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta);

		if (!focusedUnit.IsBuilding)
		{
			if (isBuildSubMenuOpen)
			{
				string[] options = hasMetadata && meta.BuildOptions != null ? meta.BuildOptions : new[] { "castle", "tower" };
				foreach (var opt in options)
				{
					items.Add(CreateBuildOptionItem(opt));
				}
				items.Add(new CommandCardItem
				{
					Id = "cancel_build",
					IconPath = "res://Assets/UI/cancel_button_2.png",
					Tooltip = "[Esc] Cancel",
					Hotkey = Key.Escape,
					Callback = () => InGameHUD.Instance?.ExitBuildSubMenu()
				});
			}
			else
			{
				items.Add(new CommandCardItem
				{
					Id = "move",
					IconPath = "res://Assets/UI/move_speed.png",
					Tooltip = "[M] Move / Right-Click Ground",
					Hotkey = Key.M,
					Callback = () => GameHost.Instance?.EnterCommandTargeting("move")
				});
				items.Add(new CommandCardItem
				{
					Id = "stop",
					IconPath = "res://Assets/UI/cancel_button_2.png",
					Tooltip = "[S] Stop Selected Units",
					Hotkey = Key.S,
					Callback = () => {
						InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
						GameHost.Instance?.StopSelectedUnits();
					}
				});
				items.Add(new CommandCardItem
				{
					Id = "hold",
					IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
					Tooltip = "[H] Hold Position — Unit stays put and attacks in place",
					Hotkey = Key.H,
					Callback = () => {
						InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
						GameHost.Instance?.HoldSelectedUnits();
					}
				});
				items.Add(new CommandCardItem
				{
					Id = "attack",
					IconPath = "res://Assets/UI/battle_axe.png",
					Tooltip = "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move",
					Hotkey = Key.A,
					Callback = () => GameHost.Instance?.EnterCommandTargeting("attack")
				});
				items.Add(new CommandCardItem
				{
					Id = "patrol",
					IconPath = "res://Assets/UI/patrol.jpg",
					Tooltip = "[P] Patrol — Unit patrols between current position and target, engaging enemies",
					Hotkey = Key.P,
					Callback = () => GameHost.Instance?.EnterCommandTargeting("patrol")
				});

				bool canBuild = hasMetadata && meta.BuildOptions != null && meta.BuildOptions.Length > 0;
				if (canBuild)
				{
					items.Add(new CommandCardItem
					{
						Id = "build",
						IconPath = "res://Assets/UI/golden_hammers.png",
						Tooltip = "[B] Build Structure",
						Hotkey = Key.B,
						Callback = () => InGameHUD.Instance?.EnterBuildSubMenu()
					});
				}

				if (hasMetadata && meta.Abilities != null)
				{
					foreach (var ab in meta.Abilities)
					{
						items.Add(CreateAbilityItem(ab));
					}
				}
				else
				{
					if (focusedUnit.UnitId == "priest")
					{
						items.Add(CreateAbilityItem("holylight"));
					}
				}


			}
		}
		else
		{
			if (focusedUnit.UnitId == "castle")
			{
				string[] trainOptions = hasMetadata && meta.BuildOptions != null && meta.BuildOptions.Length > 0 ? meta.BuildOptions : new[] { "soldier", "archer", "priest", "worker" };
				foreach (var opt in trainOptions)
				{
					items.Add(CreateTrainOptionItem(opt));
				}

				items.Add(new CommandCardItem
				{
					Id = "set_rally",
					IconPath = "res://Assets/UI/alliance_flag.png",
					Tooltip = "[Y] Set Rally Point — Set location where new units will walk",
					Hotkey = Key.Y,
					Callback = () => GameHost.Instance?.EnterCommandTargeting("rally")
				});

				items.Add(new CommandCardItem
				{
					Id = "buy_potion",
					IconPath = "res://Assets/UI/alliance_flag.png",
					Tooltip = "[I] Buy Potion (Cost: 50 Gold) — Buy a Healing Potion for a nearby combat unit",
					Hotkey = Key.I,
					Callback = () => {
						var selected = GameHost.Instance?.SelectedUnits;
						if (selected != null && selected.Count == 1)
						{
							GameHost.Instance.BuyHealingPotion(selected[0].Entity);
						}
					}
				});

				items.Add(new CommandCardItem
				{
					Id = "upgrade_weapons",
					IconPath = "res://Assets/UI/battle_axe.png",
					Tooltip = "[W] Upgrade Weapons (Cost: 150 Gold, 100 Wood)\nPermanently increases unit damage by +3",
					Hotkey = Key.W,
					Callback = () => GameHost.Instance?.BuyWeaponsUpgrade(),
					IsDisabled = () => GameHost.Instance != null && GameHost.Instance.HasWeaponsUpgrade,
					GetButtonText = () => (GameHost.Instance != null && GameHost.Instance.HasWeaponsUpgrade) ? TranslationServer.Translate("MAXED") : ""
				});

				items.Add(new CommandCardItem
				{
					Id = "upgrade_shields",
					IconPath = "res://Assets/UI/battle_shield.png",
					Tooltip = "[G] Upgrade Armor (Cost: 150 Gold, 100 Stone)\nPermanently increases unit armor by +2",
					Hotkey = Key.G,
					Callback = () => GameHost.Instance?.BuyShieldsUpgrade(),
					IsDisabled = () => GameHost.Instance != null && GameHost.Instance.HasShieldsUpgrade,
					GetButtonText = () => (GameHost.Instance != null && GameHost.Instance.HasShieldsUpgrade) ? TranslationServer.Translate("MAXED") : ""
				});

				items.Add(new CommandCardItem
				{
					Id = "upgrade_harvesting",
					IconPath = "res://Assets/UI/gold_coin.png",
					Tooltip = "[T] Upgrade Harvesting (Cost: 150 Wood, 100 Stone)\nPermanently increases passive resource gathering rates by +50%",
					Hotkey = Key.T,
					Callback = () => GameHost.Instance?.BuyHarvestingUpgrade(),
					IsDisabled = () => GameHost.Instance != null && GameHost.Instance.HasHarvestingUpgrade,
					GetButtonText = () => (GameHost.Instance != null && GameHost.Instance.HasHarvestingUpgrade) ? TranslationServer.Translate("MAXED") : ""
				});

				if (hasMetadata && meta.Abilities != null)
				{
					foreach (var ab in meta.Abilities)
					{
						items.Add(CreateAbilityItem(ab));
					}
				}
			}
			else if (focusedUnit.UnitId == "tower")
			{
				items.Add(new CommandCardItem
				{
					Id = "upgrade_tower",
					IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
					Tooltip = "[U] Upgrade Tower (Cost: 150 Gold, 100 Stone)",
					Hotkey = Key.U,
					Callback = () => InGameHUD.Instance?.UpgradeSelectedTower(),
					IsDisabled = () => {
						if (GameHost.Instance == null) return false;
						bool isMaxed = false;
						if (GameHost.Instance.EcsWorld.IsAlive(focusedUnit.Entity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.TowerUpgradeLevel>(focusedUnit.Entity))
						{
							isMaxed = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.TowerUpgradeLevel>(focusedUnit.Entity).Value >= 3;
						}
						return isMaxed;
					},
					GetButtonText = () => {
						if (GameHost.Instance == null) return "";
						bool isMaxed = false;
						if (GameHost.Instance.EcsWorld.IsAlive(focusedUnit.Entity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.TowerUpgradeLevel>(focusedUnit.Entity))
						{
							isMaxed = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.TowerUpgradeLevel>(focusedUnit.Entity).Value >= 3;
						}
						return isMaxed ? TranslationServer.Translate("MAXED") : "";
					}
				});

				if (hasMetadata && meta.Abilities != null)
				{
					foreach (var ab in meta.Abilities)
					{
						items.Add(CreateAbilityItem(ab));
					}
				}
				else
				{
					items.Add(CreateAbilityItem("fireball"));
					items.Add(CreateAbilityItem("lightning"));
				}
			}
			else
			{
				if (hasMetadata && meta.BuildOptions != null)
				{
					foreach (var opt in meta.BuildOptions)
					{
						items.Add(CreateTrainOptionItem(opt));
					}
				}
				if (hasMetadata && meta.Abilities != null)
				{
					foreach (var ab in meta.Abilities)
					{
						items.Add(CreateAbilityItem(ab));
					}
				}
			}
		}

		return items;
	}

	private CommandCardItem CreateBuildOptionItem(string unitId)
	{
		var hotkey = unitId switch
		{
			"castle" => Key.C,
			"tower" => Key.T,
			_ => Key.None
		};
		
		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		if (GameHost.UnitRegistry.TryGetValue(unitId, out var structureMeta))
		{
			name = structureMeta.Name;
			gold = structureMeta.CostGold;
			wood = structureMeta.CostWood;
			stone = structureMeta.CostStone;
		}

		string tooltipFormat = hotkey != Key.None 
			? "[{0}] Build {1} (Cost: {2} Gold, {3} Wood, {4} Stone)"
			: "Build {0} (Cost: {1} Gold, {2} Wood, {3} Stone)";

		string finalTooltip = string.Format(TranslationServer.Translate(tooltipFormat), 
			hotkey.ToString(), name, gold, wood, stone);

		return new CommandCardItem
		{
			Id = "build_" + unitId,
			IconPath = GetUnitIcon(unitId),
			Tooltip = finalTooltip,
			Hotkey = hotkey,
			Callback = () => GameHost.Instance?.EnterBuildingPlacement(unitId)
		};
	}

	private CommandCardItem CreateTrainOptionItem(string unitId)
	{
		var hotkey = unitId switch
		{
			"soldier" => Key.F,
			"archer" => Key.R,
			"priest" => Key.P,
			"worker" => Key.V,
			_ => Key.None
		};

		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		int pop = 0;
		string desc = "";
		if (GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			name = meta.Name;
			gold = meta.CostGold;
			wood = meta.CostWood;
			stone = meta.CostStone;
			pop = meta.PopCost;
			desc = meta.Description;
		}

		string costStr = $"Cost: {gold} Gold";
		if (wood > 0) costStr += $", {wood} Wood";
		if (stone > 0) costStr += $", {stone} Stone";
		if (pop > 0) costStr += $", {pop} Pop";

		string tooltipFormat = hotkey != Key.None
			? "[{0}] Train {1} ({2}) — {3}"
			: "Train {0} ({1}) — {2}";

		string finalTooltip = string.Format(TranslationServer.Translate(tooltipFormat), 
			hotkey.ToString(), name, costStr, desc);

		return new CommandCardItem
		{
			Id = "train_" + unitId,
			IconPath = GetUnitIcon(unitId),
			Tooltip = finalTooltip,
			Hotkey = hotkey,
			Callback = () => GameHost.Instance?.TrainUnitAtCastle(unitId)
		};
	}

	private CommandCardItem CreateAbilityItem(string abilityId)
	{
		var hotkey = abilityId switch
		{
			"fireball" => Key.Q,
			"lightning" => Key.E,
			"holylight" => Key.W,
			"upgrade_health" => Key.H,
			_ => Key.None
		};

		string iconPath = abilityId switch
		{
			"fireball" => "res://Assets/UI/fire_spell.png",
			"lightning" => "res://Assets/UI/lightning_spell.png",
			"holylight" => "res://Assets/UI/magic_upgrade_arrow.png",
			"upgrade_health" => "res://Assets/UI/magic_upgrade_arrow.png",
			_ => "res://Assets/UI/alliance_flag.png"
		};

		string tooltip = abilityId switch
		{
			"fireball" => string.Format(TranslationServer.Translate("[Q] Cast Fireball — 50 AoE Dmg, {0}s cooldown"), GameHost.FireballCooldownMax),
			"lightning" => string.Format(TranslationServer.Translate("[E] Cast Lightning — 80 AoE Dmg, {0}s cooldown"), GameHost.LightningCooldownMax),
			"holylight" => string.Format(TranslationServer.Translate("[W] Cast Holy Light — 60 AoE Heal, {0}s cooldown"), GameHost.HolyLightCooldownMax),
			"upgrade_health" => TranslationServer.Translate("[H] Upgrade Health — Dummy Health Upgrade"),
			_ => string.Format(TranslationServer.Translate("Cast {0}"), abilityId.ToUpper())
		};

		Action callback = () => GameHost.Instance?.EnterSpellTargeting(abilityId);
		if (abilityId == "upgrade_health")
		{
			callback = () => {
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.ShowFeedbackText(TranslationServer.Translate("Health Upgrade Researched! (Test)"), new Color(0.2f, 0.9f, 0.2f));
				}
			};
		}

		return new CommandCardItem
		{
			Id = abilityId,
			IconPath = iconPath,
			Tooltip = tooltip,
			Hotkey = hotkey,
			Callback = callback
		};
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
		btn.AddThemeConstantOverride("icon_max_width", 72);

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

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => onClick?.Invoke();
	}
}
