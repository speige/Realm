using System.Collections.Generic;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the remaining cooldown durations for an entity's abilities.
/// </summary>
internal record struct Cooldowns(Dictionary<string, float> Value);
