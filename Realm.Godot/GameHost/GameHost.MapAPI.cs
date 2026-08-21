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
			}
		};

		return catalog;
	}

	public void ResetAbilityCatalog()
	{
		_abilityDefinitions.Clear();
		foreach (var kvp in CreateDefaultAbilityCatalog())
		{
			_abilityDefinitions[kvp.Key] = kvp.Value;
		}
	}

	public void RegisterCustomAbilities(List<AbilityMetadata> customAbilities)
	{
		if (customAbilities == null) return;
		foreach (var meta in customAbilities)
		{
			if (string.IsNullOrEmpty(meta.AbilityId)) continue;
			_abilityDefinitions[meta.AbilityId] = new AbilityDefinition
			{
				Id = meta.AbilityId,
				DisplayName = meta.Name ?? "",
				Tooltip = meta.Description ?? "",
				IconPath = meta.IconPath ?? "",
				IsInstant = string.Equals(meta.AbilityType, "instant_spell", StringComparison.OrdinalIgnoreCase),
				ManaCost = meta.ManaCost
			};
		}
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
