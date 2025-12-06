using Templates.Ids;

namespace Templates.Definitions;

public readonly record struct StatusModifierEffectTemplate : IBaseEffectTemplate<StatusModifierEffectTemplate>, IBaseEffectTemplate
{
	public UniqueId<StatusModifierEffectTemplate> UniqueId { get; init; }

	public bool CanMove { get; init; }
	public bool CanAttack { get; init; }
	public bool CanCastSpells { get; init; }
}