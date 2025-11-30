using System.Collections.Generic;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Tracks cooldown timers for commander abilities/spells.
/// </summary>
internal record struct SpellCooldowns(Dictionary<string, float> Value);
