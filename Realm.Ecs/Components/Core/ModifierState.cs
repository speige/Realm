using System.Collections.Generic;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the active modifiers applied to an entity's stats.
/// </summary>
internal record struct ModifierState(List<StatModifier> Value);
