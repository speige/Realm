namespace Realm.Maps;

using System;
using System.Numerics;
using System.Collections.Generic;
using Realm.MapAPI;

public class MeleeMap : IMapScript
{
    private float _enemyAiTimer = 0f;
    private float _enemySpawnTimer = 0f;

    public void Initialize(IGameAPI api)
    {
        api.SpawnResourceNode("goldmine", new Vector3(-35f, 0f, -15f), 2000f);
        api.SpawnResourceNode("tree", new Vector3(-18f, 0f, -35f), 500f);
        api.SpawnResourceNode("tree", new Vector3(-22f, 0f, -36f), 500f);
        api.SpawnResourceNode("tree", new Vector3(-26f, 0f, -34f), 500f);
        api.SpawnResourceNode("rock", new Vector3(-36f, 0f, -32f), 1000f);
        api.SpawnResourceNode("rock", new Vector3(-32f, 0f, -35f), 1000f);

        api.SpawnResourceNode("goldmine", new Vector3(35f, 0f, 15f), 2000f);
        api.SpawnResourceNode("tree", new Vector3(18f, 0f, 35f), 500f);
        api.SpawnResourceNode("tree", new Vector3(22f, 0f, 36f), 500f);
        api.SpawnResourceNode("tree", new Vector3(26f, 0f, 34f), 500f);
        api.SpawnResourceNode("rock", new Vector3(36f, 0f, 32f), 1000f);
        api.SpawnResourceNode("rock", new Vector3(32f, 0f, 35f), 1000f);

        api.SpawnResourceNode("goldmine", new Vector3(0f, 0f, 0f), 2000f);
        api.SpawnResourceNode("tree", new Vector3(-10f, 0f, 10f), 500f);
        api.SpawnResourceNode("tree", new Vector3(-12f, 0f, 12f), 500f);
        api.SpawnResourceNode("tree", new Vector3(10f, 0f, -10f), 500f);
        api.SpawnResourceNode("tree", new Vector3(12f, 0f, -12f), 500f);
        api.SpawnResourceNode("rock", new Vector3(15f, 0f, 15f), 1000f);
        api.SpawnResourceNode("rock", new Vector3(-15f, 0f, -15f), 1000f);

        api.SpawnUnit("worker", new Vector3(-16f, 0f, -20f), false);
        api.SpawnUnit("footman", new Vector3(-8f, 0f, 5f), false);
        api.SpawnUnit("archer", new Vector3(-12f, 0f, 5f), false);
        api.SpawnUnit("castle", new Vector3(-25f, 0f, -25f), false);
        api.SpawnUnit("tower", new Vector3(-15f, 0f, -15f), false);

        var enemyWorker = api.SpawnUnit("worker", new Vector3(16f, 0f, 20f), true);
        api.SpawnUnit("footman", new Vector3(15f, 0f, 10f), true);
        api.SpawnUnit("archer", new Vector3(20f, 0f, 15f), true);
        api.SpawnUnit("tower", new Vector3(25f, 0f, 5f), true);
        api.SpawnUnit("castle", new Vector3(25f, 0f, 25f), true);

        IResourceNode? nearestGoldmine = null;
        float nearestDist = float.MaxValue;
        foreach (var node in api.GetResourceNodes())
        {
            if (node.ResourceType == "gold")
            {
                float dist = Vector3.Distance(new Vector3(16f, 0f, 20f), node.Position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestGoldmine = node;
                }
            }
        }
        if (nearestGoldmine != null)
        {
            enemyWorker.Gather(nearestGoldmine);
        }
    }

    public void Update(IGameAPI api, float delta)
    {
        var playerCastle = api.GetCastle(false);
        var enemyCastle = api.GetCastle(true);

        if (playerCastle == null || playerCastle.IsDead)
        {
            api.TriggerDefeat();
            return;
        }

        if (enemyCastle == null || enemyCastle.IsDead)
        {
            api.TriggerVictory();
            return;
        }

        float timeFactor = Math.Min(api.GameElapsedTime / 300f, 1f);
        float spawnInterval = 15f - (timeFactor * 9f);
        int unitsPerWave = 1 + (int)(timeFactor * 2f);

        _enemySpawnTimer += delta;
        if (_enemySpawnTimer >= spawnInterval)
        {
            _enemySpawnTimer = 0f;
            if (enemyCastle != null)
            {
                for (int w = 0; w < unitsPerWave; w++)
                {
                    float ox = (Random.Shared.NextSingle() - 0.5f) * 6f;
                    float oz = (Random.Shared.NextSingle() - 0.5f) * 6f;
                    Vector3 spawnPos = enemyCastle.Position + new Vector3(-8f + ox, 0f, -8f + oz);

                    string unitId;
                    int roll = Random.Shared.Next(0, 10);
                    if (timeFactor > 0.7f && roll == 0)
                    {
                        unitId = "priest";
                    }
                    else if (timeFactor > 0.4f && roll <= 1)
                    {
                        unitId = "footman";
                    }
                    else
                    {
                        unitId = (Random.Shared.Next(0, 2) == 0) ? "footman" : "archer";
                    }

                    api.SpawnUnit(unitId, spawnPos, true);
                }

                if (unitsPerWave > 1)
                {
                    api.ShowFeedbackText($"ALERT: Enemy sending {unitsPerWave} units!", new Vector3(1f, 0.3f, 0.3f));
                }
            }
        }

        _enemyAiTimer += delta;
        float marchInterval = 20f - (timeFactor * 8f);
        if (_enemyAiTimer >= marchInterval)
        {
            _enemyAiTimer = 0f;
            Vector3 targetPos = playerCastle != null ? playerCastle.Position : new Vector3(-25f, 0f, -25f);

            int attackingEnemiesCount = 0;
            foreach (var unit in api.GetAllUnits())
            {
                if (unit.IsEnemy && !unit.IsBuilding && !unit.IsDead)
                {
                    unit.AttackMove(targetPos);
                    attackingEnemiesCount++;
                }
            }

            if (attackingEnemiesCount > 0)
            {
                api.ShowFeedbackText("ALERT: Orc Raider forces are marching towards your base!", new Vector3(1f, 0.2f, 0.2f));
            }
        }
    }
}
