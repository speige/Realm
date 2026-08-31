using System.Numerics;

namespace Realm.MapAPI;

public readonly struct TowerDefenseConfig
{
    public float Range { get; }
    public float Damage { get; }
    public float AttackCooldownSeconds { get; }
    public string VisualEffectId { get; }

    public TowerDefenseConfig(float range, float damage, float attackCooldownSeconds, string visualEffectId)
    {
        Range = range;
        Damage = damage;
        AttackCooldownSeconds = attackCooldownSeconds;
        VisualEffectId = visualEffectId;
    }
}

public readonly struct WaypointMarchConfig
{
    public float WaypointArrivalRadiusSquared { get; }
    public float AttackCooldownSeconds { get; }

    public WaypointMarchConfig(float waypointArrivalRadiusSquared, float attackCooldownSeconds)
    {
        WaypointArrivalRadiusSquared = waypointArrivalRadiusSquared;
        AttackCooldownSeconds = attackCooldownSeconds;
    }

    public static WaypointMarchConfig Default => new(4f, 1.2f);
}

public static class MapScriptHelpers
{
    public static bool RunTowerDefenseTick(
        IGameAPI api,
        IUnit tower,
        TowerDefenseConfig config,
        ref float cooldownRemaining,
        float delta)
    {
        if (tower.IsDead)
            return false;

        cooldownRemaining = MathF.Max(0f, cooldownRemaining - delta);

        var range = tower.Range > 0 ? tower.Range : config.Range;

        var target = api.GetUnitsInRadius(tower.Position, range)
            .Where(unit => !unit.IsDead && unit.IsEnemy != tower.IsEnemy)
            .OrderBy(unit => Vector3.DistanceSquared(unit.Position, tower.Position))
            .FirstOrDefault();

        if (target == null || cooldownRemaining > 0)
            return false;

        tower.Attack(target);
        if (!string.IsNullOrEmpty(config.VisualEffectId))
            api.SpawnVisualEffect(config.VisualEffectId, target.Position, 0.35f);

        cooldownRemaining = config.AttackCooldownSeconds;
        return true;
    }
}

public sealed class WaypointMarcher
{
    private readonly IUnit _unit;
    private readonly IReadOnlyList<Vector3> _waypoints;
    private readonly Vector3 _finalDestination;
    private readonly WaypointMarchConfig _config;
    private int _waypointIndex = 1;
    private bool _hasMovementOrder;
    private bool _wasFighting;
    private float _attackCooldown;
    private Vector3? _orderedDestination;
    private IUnit? _activeCombatTarget;

    public WaypointMarcher(
        IUnit unit,
        IReadOnlyList<Vector3> waypoints,
        WaypointMarchConfig? config = null)
    {
        _unit = unit;
        _waypoints = waypoints;
        _finalDestination = waypoints.Count > 0 ? waypoints[^1] : unit.Position;
        _config = config ?? WaypointMarchConfig.Default;
    }

    public bool IsAlive => !_unit.IsDead;

    public void Update(IGameAPI api, float delta)
    {
        if (!IsAlive || _waypointIndex >= _waypoints.Count)
            return;

        _attackCooldown = MathF.Max(0f, _attackCooldown - delta);
        var attackRange = _unit.Range > 0 ? _unit.Range : 1.5f;

        var target = api.GetUnitsInRadius(_unit.Position, attackRange + 1.25f)
            .Where(candidate => !candidate.IsDead && candidate.IsEnemy != _unit.IsEnemy)
            .OrderBy(candidate => HorizontalDistanceSquared(candidate.Position, _unit.Position))
            .FirstOrDefault();

        if (target != null)
        {
            _wasFighting = true;

            if (!ReferenceEquals(_activeCombatTarget, target))
            {
                _unit.Attack(target);
                _activeCombatTarget = target;
                _orderedDestination = null;
                _hasMovementOrder = false;
            }

            var distSq = HorizontalDistanceSquared(_unit.Position, target.Position);
            if (distSq <= attackRange * attackRange && _attackCooldown <= 0f)
            {
                _unit.Attack(target);
                _attackCooldown = _config.AttackCooldownSeconds;
            }

            return;
        }

        _activeCombatTarget = null;
        AdvanceWaypointIfReached();
        if (!_hasMovementOrder || _wasFighting)
        {
            IssueLanePush(api);
            _wasFighting = false;
        }
    }

    private void AdvanceWaypointIfReached()
    {
        var waypoint = _waypoints[_waypointIndex];
        if (HorizontalDistanceSquared(_unit.Position, waypoint) > _config.WaypointArrivalRadiusSquared)
            return;
        _waypointIndex++;
        _hasMovementOrder = false;
    }

    private void IssueLanePush(IGameAPI api)
    {
        var destination = _waypointIndex >= _waypoints.Count
            ? _finalDestination
            : _waypoints[_waypointIndex];

        if (_orderedDestination.HasValue &&
            HorizontalDistanceSquared(_orderedDestination.Value, destination) < 0.25f)
        {
            return;
        }

        api.IssueAttackMoveOrder(_unit, destination);
        _orderedDestination = destination;
        _hasMovementOrder = true;
    }

    private static float HorizontalDistanceSquared(Vector3 from, Vector3 to)
    {
        var dx = from.X - to.X;
        var dz = from.Z - to.Z;
        return dx * dx + dz * dz;
    }
}
