using System.Collections.Generic;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents a queue of pending visual effect requests.
/// </summary>
internal record struct VFXQueue(List<VFXRequest> Requests);
