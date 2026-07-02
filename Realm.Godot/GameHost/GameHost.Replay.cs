using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Godot.ReplaySystem;
using System.Collections.Generic;

public partial class GameHost
{
	public void ResetStateForReplayPlayback()
	{
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();

		ReinitializeEcsAndServices();

		var players = new List<(int PeerId, string Name)>();
		if (ReplayPlaybackManager.Instance.Header.Players != null)
		{
			foreach (var p in ReplayPlaybackManager.Instance.Header.Players)
			{
				players.Add((p.PeerId, p.Name));
			}
		}
		_replayService.SetupPlayersForPlayback(players);
	}

	public void SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		var result = _replayService.SpawnUnitFromReplaySnapshot(snap);
		if (result.Entity == default) return;

		string modelPath = !string.IsNullOrEmpty(result.ModelPath) ? result.ModelPath : GetFallbackModelPath(snap.UnitId, snap.IsBuilding);
		SpawnUnit3D(result.Entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, result.IsEnemy);
	}

	private void RecordGameplayTick()
	{
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;

			if (EcsWorld.Has<Position>(unit.Entity))
			{
				EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z)));
			}

			if (EcsWorld.Has<Velocity>(unit.Entity))
			{
				EcsWorld.Set(unit.Entity, new Velocity(new System.Numerics.Vector3(unit.Velocity.X, unit.Velocity.Y, unit.Velocity.Z)));
			}
			else
			{
				EcsWorld.Add(unit.Entity, new Velocity(new System.Numerics.Vector3(unit.Velocity.X, unit.Velocity.Y, unit.Velocity.Z)));
			}

			if (EcsWorld.Has<RotationY>(unit.Entity))
			{
				EcsWorld.Set(unit.Entity, new RotationY(unit.GlobalRotation.Y));
			}
			else
			{
				EcsWorld.Add(unit.Entity, new RotationY(unit.GlobalRotation.Y));
			}
		}

		_replayService.RecordGameplayTick();
	}
}
