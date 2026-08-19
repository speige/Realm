using Godot;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

public class AbilityDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Tooltip { get; set; } = "";
	public string IconPath { get; set; } = "";
	public bool IsInstant { get; set; }
	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public float ManaCost { get; set; } = 0f;
}

public partial class GameHost
{
	private readonly Dictionary<string, AbilityDefinition> _abilityDefinitions = CreateDefaultAbilityCatalog();

	private static Dictionary<string, AbilityDefinition> CreateDefaultAbilityCatalog()
	{
		var catalog = new Dictionary<string, AbilityDefinition>(StringComparer.OrdinalIgnoreCase)
		{
			["fireball"] = new AbilityDefinition
			{
				Id = "fireball",
				DisplayName = "Bola de fuego",
				Tooltip = "[X] Bola de fuego — Daño 50 en área (radio 4)",
				IconPath = "res://Assets/UI/fire_spell.png",
				IsInstant = false
			},
			["lightning"] = new AbilityDefinition
			{
				Id = "lightning",
				DisplayName = "Rayo",
				Tooltip = "[X] Rayo — Daño 80 en área (radio 2)",
				IconPath = "res://Assets/UI/lightning_spell.png",
				IsInstant = false
			},
			["holylight"] = new AbilityDefinition
			{
				Id = "holylight",
				DisplayName = "Luz sagrada",
				Tooltip = "[X] Luz sagrada — Cura 60 en área (radio 4)",
				IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
				IsInstant = false
			},
			["survivor_buy_healthstone"] = new AbilityDefinition
			{
				Id = "survivor_buy_healthstone",
				DisplayName = "Healthstone",
				Tooltip = "[X] Healthstone — 2000g. +2500 vida máx y regeneración +35/s (única).",
				IconPath = "res://Assets/UI/battle_shield.png",
				IsInstant = true
			},
			["survivor_buy_damage"] = new AbilityDefinition
			{
				Id = "survivor_buy_damage",
				DisplayName = "Piedra de Daño",
				Tooltip = "[X] Piedra de Daño — 150g por nivel, máx 5. +25 daño.",
				IconPath = "res://Assets/UI/battle_axe.png",
				IsInstant = true
			},
			["survivor_buy_range"] = new AbilityDefinition
			{
				Id = "survivor_buy_range",
				DisplayName = "Piedra de Alcance",
				Tooltip = "[X] Piedra de Alcance — 300g por nivel, máx 3. +8 alcance.",
				IconPath = "res://Assets/UI/elf_warrior.png",
				IsInstant = true
			},
			["survivor_buy_fury"] = new AbilityDefinition
			{
				Id = "survivor_buy_fury",
				DisplayName = "Furia de Flechas",
				Tooltip = "[X] Furia de Flechas — 200g por nivel, máx 3. +20% de flecha extra.",
				IconPath = "res://Assets/UI/golden_hammers.png",
				IsInstant = true
			},
			["survivor_buy_multishot"] = new AbilityDefinition
			{
				Id = "survivor_buy_multishot",
				DisplayName = "Multidisparo",
				Tooltip = "[X] Multidisparo — 1500g. Tus ataques impactan a 3 objetivos cercanos.",
				IconPath = "res://Assets/UI/scroll_icon.png",
				IsInstant = true
			},
			["survivor_heal"] = new AbilityDefinition
			{
				Id = "survivor_heal",
				DisplayName = "Poción de Restauración",
				Tooltip = "[X] Poción de Restauración — 200g. Cura 600 de vida.",
				IconPath = "res://Assets/UI/gold_coin.png",
				IsInstant = true
			},
			["upgrade_health"] = new AbilityDefinition
			{
				Id = "upgrade_health",
				DisplayName = "Mejora de Vida",
				Tooltip = "[H] Mejora de Vida — Mejora de vida de prueba.",
				IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
				IsInstant = true
			}
		};

		return catalog;
	}

	public AbilityDefinition GetAbilityDefinition(string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return null;
		_abilityDefinitions.TryGetValue(abilityId, out var def);
		return def;
	}

	void IGameAPI.RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.DisplayName = displayName ?? "";
		def.Tooltip = tooltip ?? "";
		if (!string.IsNullOrEmpty(iconPath)) def.IconPath = iconPath;
		def.IsInstant = isInstant;
	}

	void IGameAPI.SetAbilityInstant(string abilityId, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.IsInstant = isInstant;
	}

	void IGameAPI.SetAbilityIcon(string abilityId, string iconPath)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.IconPath = iconPath ?? "";
	}

	void IGameAPI.SetAbilityTooltip(string abilityId, string tooltip)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.Tooltip = tooltip ?? "";
	}

	void IGameAPI.SetAbilityGridPosition(string abilityId, int x, int y)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.GridX = x;
		def.GridY = y;
	}

	void IGameAPI.SetAbilityManaCost(IUnit unit, string abilityId, float manaCost)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.ManaCost = manaCost;
	}
}
