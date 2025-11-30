namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Tracks the construction progress of a building entity, from placement to completion.
/// </summary>
internal struct ConstructionState
{
    public float TotalBuildTime;
    public float Progress;

    public ConstructionState(float totalBuildTime)
    {
        TotalBuildTime = totalBuildTime;
        Progress = 0f;
    }

    public readonly bool IsComplete => Progress >= TotalBuildTime;
}
