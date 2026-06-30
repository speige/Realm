using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;

namespace Realm.Ecs.Services;

/// <summary>
///     A conceptual service that initializes the game state, including players and their starting units.
///     This demonstrates how PlayerEntity and Owner components would be used in practice.
/// </summary>
internal class GameInitializer
{
	private readonly ArchetypeManager _archetypeManager;
	private readonly DefinitionManager _definitionManager;
	private readonly EntityFactory _entityFactory;
	private readonly World _world;

	public GameInitializer(World world, MapLoader mapLoader)
	{
		_world = world;
		_definitionManager = mapLoader.DefinitionManager;
		_archetypeManager = mapLoader.ArchetypeManager;
		_entityFactory = new EntityFactory(_world, _archetypeManager, mapLoader.DefinitionManager);
	}

	/// <summary>
	///     Creates a player entity and spawns a starting unit for them, assigning ownership.
	/// </summary>
	public void InitializePlayer(string playerName, string startingUnitArchetypeId)
	{
		var playerEntity = _world.Create();
		_world.Set(playerEntity, new Player());
		_world.Set(playerEntity, new Name(playerName));
		_world.Set(playerEntity, new PlayerResources(new Dictionary<ResourceId, int>
		{
			{ "Gold".AsResourceId(_definitionManager), 500 }
		}));

		var typedPlayerEntity = playerEntity.AsPlayerEntity(_world);

		var unitEntity =
			_entityFactory.SpawnUnit(startingUnitArchetypeId, new Vector3(0, 0, 0));

		_world.Set(unitEntity, new Owner(typedPlayerEntity));

		Console.WriteLine($"{playerName} initialized with a {startingUnitArchetypeId} unit.");
	}
}