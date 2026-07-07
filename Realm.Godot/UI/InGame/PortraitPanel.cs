using Godot;
using System;
using System.Collections.Generic;
using Arch.Core;

public class PortraitPanel
{
	private PanelContainer _portraitFrame;
	private PanelContainer _selectionFrame;
	private HBoxContainer _unitsContainer;
	private List<Button> _unitButtons;
	private HBoxContainer _statsContainer;
	private Label _statsLabel;
	private VBoxContainer _spellsBox;
	private VBoxContainer _itemsBox;
	private Button _btnUsePotion;
	private VBoxContainer _productionBox;
	private Label _productionTitle;
	private ProgressBar _productionProgress;
	private Label _productionQueueLabel;
	private HBoxContainer _queueSlotsContainer;
	private Label _armyCompositionLabel;
	private Label _unitNameLabel;
	private TextureRect _portraitTexture;

	private VBoxContainer _fireballVBox;
	private VBoxContainer _lightningVBox;
	private VBoxContainer _holyLightVBox;
	private ProgressBar _fireballCooldownBar;
	private ProgressBar _lightningCooldownBar;
	private ProgressBar _holyLightCooldownBar;
	private Button _btnFireball;
	private Button _btnLightning;
	private Button _btnHolyLight;
	private List<string> _lastProductionQueue = new();

	public event Action<int> UnitSelectionButtonClicked;

	public PortraitPanel(PanelContainer portraitFrame, PanelContainer selectionFrame, 
		HBoxContainer unitsContainer, List<Button> unitButtons, HBoxContainer statsContainer,
		Label statsLabel, VBoxContainer spellsBox, VBoxContainer itemsBox, Button btnUsePotion,
		VBoxContainer productionBox, Label productionTitle, ProgressBar productionProgress,
		Label productionQueueLabel, HBoxContainer queueSlotsContainer, Label armyCompositionLabel,
		Label unitNameLabel, TextureRect portraitTexture,
		VBoxContainer fireballVBox, VBoxContainer lightningVBox, VBoxContainer holyLightVBox,
		ProgressBar fireballCooldownBar, ProgressBar lightningCooldownBar, ProgressBar holyLightCooldownBar,
		Button btnFireball, Button btnLightning, Button btnHolyLight)
	{
		_portraitFrame = portraitFrame;
		_selectionFrame = selectionFrame;
		_unitsContainer = unitsContainer;
		_unitButtons = unitButtons;
		_statsContainer = statsContainer;
		_statsLabel = statsLabel;
		_spellsBox = spellsBox;
		_itemsBox = itemsBox;
		_btnUsePotion = btnUsePotion;
		_productionBox = productionBox;
		_productionTitle = productionTitle;
		_productionProgress = productionProgress;
		_productionQueueLabel = productionQueueLabel;
		_queueSlotsContainer = queueSlotsContainer;
		_armyCompositionLabel = armyCompositionLabel;
		_unitNameLabel = unitNameLabel;
		_portraitTexture = portraitTexture;

		_fireballVBox = fireballVBox;
		_lightningVBox = lightningVBox;
		_holyLightVBox = holyLightVBox;
		_fireballCooldownBar = fireballCooldownBar;
		_lightningCooldownBar = lightningCooldownBar;
		_holyLightCooldownBar = holyLightCooldownBar;
		_btnFireball = btnFireball;
		_btnLightning = btnLightning;
		_btnHolyLight = btnHolyLight;

		for (int i = 0; i < _unitButtons.Count; i++)
		{
			int index = i;
			_unitButtons[i].Pressed += () => UnitSelectionButtonClicked?.Invoke(index);
		}
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (viewModel.SelectedUnits.Count == 0)
		{
			_unitsContainer.Visible = false;
			_statsContainer.Visible = false;
			_armyCompositionLabel?.Hide();
			
			_unitNameLabel.Text = TranslationServer.Translate("No Selection");
			if (_portraitTexture != null)
			{
				_portraitTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
			}
		}
		else if (viewModel.SelectedUnits.Count == 1)
		{
			_unitsContainer.Visible = false;
			_statsContainer.Visible = true;
			_armyCompositionLabel?.Hide();

			var info = viewModel.SelectedUnits[viewModel.CycleSelectionIndex < viewModel.SelectedUnits.Count ? viewModel.CycleSelectionIndex : 0];
			_unitNameLabel.Text = info.Name;
			if (_portraitTexture != null)
			{
				_portraitTexture.Texture = GD.Load<Texture2D>(GetUnitIcon(info.UnitId));
			}

			string statsText = $"{TranslationServer.Translate("HP")}: {info.Health:F0} / {info.MaxHealth:F0}";
			if (info.Damage > 0)
			{
				string label = (info.UnitId == "priest") ? TranslationServer.Translate("HEAL") : TranslationServer.Translate("ATK");
				statsText += $"   {label}: {info.Damage:F0}   {TranslationServer.Translate("RNG")}: {info.Range:F0}";
				if (info.UnitId != "priest" && info.Dps > 0)
				{
					statsText += $"   {TranslationServer.Translate("DPS")}: {info.Dps:F1}";
				}
			}
			statsText += $"\n{TranslationServer.Translate("Armor")}: {info.Armor:F0}";
			if (info.Speed > 0) statsText += $"   {TranslationServer.Translate("Speed")}: {info.Speed:F0}";
			
			statsText += $"\n{info.StateText}";
			if (!string.IsNullOrEmpty(info.Description)) statsText += $"\n\n{info.Description}";
			_statsLabel.Text = statsText;

			if (info.HasFireball || info.HasLightning || info.HasHolyLight)
			{
				_spellsBox.Visible = true;
				if (_fireballVBox != null) _fireballVBox.Visible = info.HasFireball;
				if (_lightningVBox != null) _lightningVBox.Visible = info.HasLightning;
				if (_holyLightVBox != null) _holyLightVBox.Visible = info.HasHolyLight;
			}
			else
			{
				_spellsBox.Visible = false;
			}

			if (_btnFireball != null) _btnFireball.Disabled = viewModel.FireballCooldown > 0;
			if (_btnLightning != null) _btnLightning.Disabled = viewModel.LightningCooldown > 0;
			if (_btnHolyLight != null) _btnHolyLight.Disabled = viewModel.HolyLightCooldown > 0;

			if (_fireballCooldownBar != null)
			{
				_fireballCooldownBar.Value = viewModel.FireballCooldown;
				_fireballCooldownBar.Visible = viewModel.FireballCooldown > 0;
			}
			if (_lightningCooldownBar != null)
			{
				_lightningCooldownBar.Value = viewModel.LightningCooldown;
				_lightningCooldownBar.Visible = viewModel.LightningCooldown > 0;
			}
			if (_holyLightCooldownBar != null)
			{
				_holyLightCooldownBar.Value = viewModel.HolyLightCooldown;
				_holyLightCooldownBar.Visible = viewModel.HolyLightCooldown > 0;
			}

			if (!info.IsBuilding)
			{
				_itemsBox.Visible = true;
				var itemsHBox = _itemsBox.GetChild<HBoxContainer>(1);
				var axeIcon = itemsHBox.GetChild<TextureRect>(0);
				var shieldIcon = itemsHBox.GetChild<TextureRect>(1);

				if (info.UnitId == "archer")
				{
					axeIcon.TooltipText = TranslationServer.Translate("Composite Recurve Bow\n+4 Attack Damage (Equipped)");
					shieldIcon.TooltipText = TranslationServer.Translate("Elven Leather Boots\n+2 Movement Speed (Equipped)");
				}
				else if (info.UnitId == "priest")
				{
					axeIcon.TooltipText = TranslationServer.Translate("Blessed Rod\n+3 Healing Power (Equipped)");
					shieldIcon.TooltipText = TranslationServer.Translate("Cloth Robes\n+1 Armor Block (Equipped)");
				}
				else
				{
					axeIcon.TooltipText = TranslationServer.Translate("Battle Axe\n+5 Attack Damage (Equipped)");
					shieldIcon.TooltipText = TranslationServer.Translate("Battle Shield\n+3 Armor Block (Equipped)");
				}

				if (_btnUsePotion != null)
				{
					_btnUsePotion.Text = $" {info.Potions} ";
					_btnUsePotion.TooltipText = string.Format(TranslationServer.Translate("[I] Healing Potion (Have: {0})\nRestores 50 HP on use."), info.Potions);
					_btnUsePotion.Disabled = info.Potions <= 0 || info.IsEnemy;
				}
			}
			else
			{
				_itemsBox.Visible = false;
			}

			if (info.IsBuilding && !info.IsEnemy && info.UnitId == "castle" && info.HasProduction)
			{
				_productionBox.Visible = true;
				if (info.ProductionQueue.Count > 0)
				{
					_productionTitle.Text = info.ProductionTitle;
					_productionProgress.Visible = true;
					_productionProgress.Value = info.ProductionProgress;
					_productionProgress.MaxValue = info.ProductionMaxProgress;
					_productionQueueLabel.Text = string.Format(TranslationServer.Translate("Queue: {0}"), info.ProductionQueue.Count);
					
					bool queueChanged = _lastProductionQueue.Count != info.ProductionQueue.Count;
					if (!queueChanged)
					{
						for (int i = 0; i < info.ProductionQueue.Count; i++)
						{
							if (_lastProductionQueue[i] != info.ProductionQueue[i])
							{
								queueChanged = true;
								break;
							}
						}
					}
					if (queueChanged)
					{
						_lastProductionQueue.Clear();
						_lastProductionQueue.AddRange(info.ProductionQueue);
						PopulateQueueSlots(info.Entity, info.ProductionQueue);
					}
				}
				else
				{
					_productionTitle.Text = TranslationServer.Translate("PRODUCTION IDLE");
					_productionProgress.Visible = false;
					_productionQueueLabel.Text = TranslationServer.Translate("Queue empty — [F] Soldier  [R] Archer  [P] Priest");
					if (_lastProductionQueue.Count > 0)
					{
						_lastProductionQueue.Clear();
						ClearQueueSlots();
					}
				}
			}
			else
			{
				_productionBox.Visible = false;
				if (_lastProductionQueue.Count > 0)
				{
					_lastProductionQueue.Clear();
				}
			}
		}
		else
		{
			_unitsContainer.Visible = true;
			_statsContainer.Visible = false;

			_unitNameLabel.Text = string.Format(TranslationServer.Translate("{0} Units Selected"), viewModel.SelectedUnits.Count);
			if (_portraitTexture != null)
			{
				_portraitTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
			}

			if (_armyCompositionLabel != null)
			{
				var unitTypeCounts = new Dictionary<string, int>();
				foreach (var u in viewModel.SelectedUnits)
				{
					string tid = u.UnitId;
					if (!unitTypeCounts.ContainsKey(tid)) unitTypeCounts[tid] = 0;
					unitTypeCounts[tid]++;
				}
				var compParts = new List<string>();
				foreach (var kv in unitTypeCounts)
					compParts.Add($"{kv.Value}× {kv.Key}");
				
				_armyCompositionLabel.Text = string.Join(", ", compParts);
				_armyCompositionLabel.Show();
			}

			var selectedBorder = new StyleBoxFlat();
			selectedBorder.BgColor = new Color(0, 0, 0, 0);
			selectedBorder.BorderColor = new Color(0.1f, 0.8f, 0.2f, 0.8f);
			selectedBorder.SetBorderWidthAll(3);

			for (int i = 0; i < _unitButtons.Count; i++)
			{
				var btn = _unitButtons[i];
				var hpBar = btn.GetNodeOrNull<ProgressBar>("HealthBar");

				if (i < viewModel.SelectedUnits.Count)
				{
					var uInfo = viewModel.SelectedUnits[i];
					btn.Visible = true;
					btn.Icon = GD.Load<Texture2D>(GetUnitIcon(uInfo.UnitId));
					btn.TooltipText = uInfo.UnitId.ToUpper();

					bool isFocused = i == viewModel.CycleSelectionIndex;
					if (isFocused)
					{
						var focusedBorder = new StyleBoxFlat();
						focusedBorder.BgColor = new Color(0, 0, 0, 0);
						focusedBorder.BorderColor = new Color(0.95f, 0.82f, 0.55f, 1.0f);
						focusedBorder.SetBorderWidthAll(3);
						btn.AddThemeStyleboxOverride("normal", focusedBorder);
					}
					else
					{
						btn.AddThemeStyleboxOverride("normal", selectedBorder);
					}

					if (hpBar != null && GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(uInfo.Entity))
					{
						hpBar.Visible = true;
						hpBar.MaxValue = uInfo.MaxHealth;
						hpBar.Value = uInfo.Health;

						float hpPct = uInfo.MaxHealth > 0 ? uInfo.Health / uInfo.MaxHealth : 1f;
						var fillStyle = new StyleBoxFlat();
						fillStyle.BgColor = hpPct < 0.35f ? new Color(0.9f, 0.2f, 0.1f)
										  : hpPct < 0.7f  ? new Color(0.9f, 0.7f, 0.1f)
										  : new Color(0.1f, 0.85f, 0.2f);
						hpBar.AddThemeStyleboxOverride("fill", fillStyle);
					}
					else if (hpBar != null)
					{
						hpBar.Visible = false;
					}
				}
				else
				{
					btn.Visible = false;
				}
			}
		}

		if (_unitsContainer.Visible)
		{
			for (int i = 0; i < _unitButtons.Count; i++)
			{
				var btn = _unitButtons[i];
				var statusLbl = btn.GetNodeOrNull<Label>("StatusIcon");
				if (statusLbl == null || i >= viewModel.SelectedUnits.Count)
				{
					if (statusLbl != null) statusLbl.Visible = false;
					continue;
				}
				var uInfo = viewModel.SelectedUnits[i];
				if (!GameHost.Instance.EcsWorld.IsAlive(uInfo.Entity))
				{
					statusLbl.Visible = false;
					continue;
				}
				var world = GameHost.Instance.EcsWorld;
				string status = "";
				if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(uInfo.Entity))       status = "H";
				else if (world.Has<Realm.Ecs.Components.Movement.Patrol>(uInfo.Entity))         status = "P";
				else if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(uInfo.Entity))     status = "A";
				else if (world.Has<Realm.Ecs.Components.Movement.Follow>(uInfo.Entity))         status = "F";
				else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(uInfo.Entity))         status = "M";
				statusLbl.Text = status;
				statusLbl.Visible = !string.IsNullOrEmpty(status);
			}
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

	private void PopulateQueueSlots(Entity castleEntity, List<string> unitIds)
	{
		if (_queueSlotsContainer == null) return;
		foreach (Node child in _queueSlotsContainer.GetChildren())
		{
			_queueSlotsContainer.RemoveChild(child);
			child.QueueFree();
		}

		for (int i = 0; i < unitIds.Count; i++)
		{
			var slot = new PanelContainer();
			slot.CustomMinimumSize = new Vector2(32, 32);
			
			var border = new StyleBoxFlat();
			border.BgColor = new Color(0, 0, 0, 0.4f);
			border.BorderColor = UIStyle.ColorBronze;
			border.SetBorderWidthAll(1);
			slot.AddThemeStyleboxOverride("panel", border);

			var icon = new TextureRect();
			icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			icon.Texture = GD.Load<Texture2D>(GetUnitIcon(unitIds[i]));
			slot.AddChild(icon);

			var btnCancel = new Button();
			btnCancel.Text = "×";
			btnCancel.FocusMode = Control.FocusModeEnum.None;
			btnCancel.AddThemeFontSizeOverride("font_size", 9);
			btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
			btnCancel.AddThemeColorOverride("font_outline_color", Colors.Black);
			btnCancel.AddThemeConstantOverride("outline_size", 3);
			btnCancel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
			btnCancel.OffsetRight = 2;
			btnCancel.OffsetTop = -2;

			var styleEmpty = new StyleBoxEmpty();
			btnCancel.AddThemeStyleboxOverride("normal", styleEmpty);
			btnCancel.AddThemeStyleboxOverride("hover", styleEmpty);
			btnCancel.AddThemeStyleboxOverride("pressed", styleEmpty);
			btnCancel.AddThemeStyleboxOverride("focus", styleEmpty);

			int idx = i;
			btnCancel.Pressed += () =>
			{
				if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(castleEntity))
				{
					GameHost.Instance.CancelQueuedUnitAt(castleEntity, idx);
				}
			};

			slot.AddChild(btnCancel);
			_queueSlotsContainer.AddChild(slot);
		}
	}

	private void ClearQueueSlots()
	{
		if (_queueSlotsContainer == null) return;
		foreach (Node child in _queueSlotsContainer.GetChildren())
		{
			_queueSlotsContainer.RemoveChild(child);
			child.QueueFree();
		}
	}
}
