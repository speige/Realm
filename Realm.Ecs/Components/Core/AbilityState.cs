using System.Collections.Generic;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the active abilities configured on an entity.
/// </summary>
internal record struct AbilityState(List<string> Abilities);
