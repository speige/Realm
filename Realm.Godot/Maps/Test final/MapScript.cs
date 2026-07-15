namespace Realm.Maps;

using System.Numerics;
using Realm.MapAPI;

public class CustomMap : IMapScript
{
    private Vector3 _spawnPos;
    private Vector3 _endPos;
    private int _wave = 0;
    private int _aliveEnemies = 0;
    private bool _gameOver = false;

    public void Initialize(IGameAPI api)
    {
        _endPos = Coordinates.EndRegion.Center;
        _spawnPos = Coordinates.SpawnRegion.Center;

        var tower1 = Coordinates.Tower1.Center;
        var tower2 = Coordinates.Tower2.Center;

        api.SpawnUnit("tower", tower1, false);
        api.SpawnUnit("tower", tower2, false);

        api.SpawnUnit("base", _endPos, false);
        api.Gold = 700;
        api.BroadcastMessage("Tower Defense Started!");

        api.OnUnitDied += (unit, killed) =>
        {
            if (unit.UnitId == "base")
            {
                api.BroadcastMessage("Your base has been destroyed!");
                api.TriggerDefeat();
                _gameOver = true;
            }

            if (unit.IsEnemy)
            {
                _aliveEnemies--;
                if (_aliveEnemies <= 0 && _wave > 0 && _wave < 10)
                {
                    api.ScheduleTimer(3f, () => StartWave(api));
                }
            }
        };

        api.ScheduleTimer(5f, () => StartWave(api));
    }

    public void Update(IGameAPI api, float delta)
    {
        if (_gameOver) return;

        if (_aliveEnemies <= 0 && _wave > 0)
        {
            if (_wave >= 4)
            {
                api.BroadcastMessage("Victory!!");
                api.TriggerVictory();
                _gameOver = true;
                return;
            }

        }
    }

    private void StartWave(IGameAPI api)
    {
        if (_gameOver) return;

        _wave++;
        int count = 5 + _wave * 2;
        _aliveEnemies = count;
        api.BroadcastMessage($"Wave {_wave} incoming! ({count} enimies)");

        for (int i = 0; i < count; i++)
        {
            var enemy = api.SpawnUnit("footman", _spawnPos, true);
            api.ScheduleTimer(0.5f, () => enemy?.AttackMove(_endPos));
        }
    }
}