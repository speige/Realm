namespace Realm.Maps;

using System.Numerics;
using Realm.MapAPI;

public class CustomMap : IMapScript
{
    private int _wave = 0;
    private int _alive = 0;
    private bool _waveActive = false;

    public void Initialize(IGameAPI api)
    {
        api.SpawnUnit("base", Coordinates.basezone.Center, false);

        var posT = Coordinates.basezone.Center;
        api.SpawnUnit("tower", new Vector3(posT.X - 10f, 0f, posT.Z), false);
        api.SpawnUnit("tower", new Vector3(posT.X + 10f, 0f, posT.Z), false);

        var c = Coordinates.towers.Center;
        api.SpawnUnit("tower", new Vector3(c.X - 10f, 0f, c.Z), false);
        api.SpawnUnit("tower", new Vector3(c.X, 0f, c.Z), false);
        api.SpawnUnit("tower", new Vector3(c.X + 10f, 0f, c.Z), false);

        api.ScheduleTimer(5f, () => StartWave(api));

        api.OnUnitDied += (unit, killer) =>
        {
            if (_waveActive && unit.IsEnemy) _alive--;
        };
    }

    public void Update(IGameAPI api, float delta)
    {
        if (_waveActive && _alive <= 0 && _wave <= 3)
        {
            _waveActive = false;
            _wave++;
            if (_wave <= 3) api.ScheduleTimer(8f, () => StartWave(api));
        }
        if (_wave > 3 && _alive <= 0) api.TriggerVictory();
    }

    public void StartWave(IGameAPI api)
    {
        int[] counts = { 0, 5, 8, 12 };
        int count = counts[_wave];
        _alive = count;
        _waveActive = true;

        for (int i = 0; i < count; i++)
        {
            var pos = Coordinates.spawn.Center;
            var unit = api.SpawnUnit("footman", pos, true);

            if (unit != null) unit.AttackMove(Coordinates.basezone.Center);
        }
    }
}