using System.Collections.Generic;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the active buffs on an entity.
/// </summary>
internal record struct BuffState(Dictionary<string, float> Value);
