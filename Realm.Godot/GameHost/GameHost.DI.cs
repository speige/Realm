using Arch.Core;
using Godot;
using Microsoft.Extensions.DependencyInjection;
using Realm.Ecs.Services;
using System;

public partial class GameHost
{
	private WorldAccessor _worldAccessor;

	public void BuildDependencyInjection()
	{
		var services = new ServiceCollection();

		var world = World.Create();
		_worldAccessor = new WorldAccessor(world);

		services.AddSingleton<WorldAccessor>(_worldAccessor);
		services.AddTransient<World>(sp => sp.GetRequiredService<WorldAccessor>().Current);

		// Ecs Services
		services.AddSingleton<DefinitionManager>();
		services.AddSingleton<ArchetypeManager>(sp =>
		{
			return new ArchetypeManager(sp.GetRequiredService<WorldAccessor>(), new System.Collections.Generic.List<Realm.Ecs.Archetypes.UnitArchetype>(), sp.GetRequiredService<DefinitionManager>());
		});
		services.AddSingleton<CombatService>();
		services.AddSingleton<EntityFactory>();
		services.AddSingleton<GameInitializer>(sp =>
		{
			var mapLoader = new MapLoader(sp.GetRequiredService<WorldAccessor>(), "Definitions");
			return new GameInitializer(sp.GetRequiredService<WorldAccessor>(), mapLoader);
		});
		services.AddSingleton<MovementService>();
		services.AddSingleton<PlayerResourceService>();
		services.AddSingleton<StatService>();
		services.AddSingleton<TerrainNavMeshService>();

		// Godot / Presentation Services
		services.AddSingleton<AudioService>();
		services.AddSingleton<FXService>();
		services.AddSingleton<SaveLoadService>();
		services.AddSingleton<EditorService>();
		services.AddSingleton<ReplayService>();
		services.AddSingleton<NetworkService>();
		services.AddSingleton<TechTreeService>();
		services.AddSingleton<InputService>();
		services.AddSingleton<FogOfWarService>();
		services.AddSingleton<UnitSpawnService>();
		services.AddSingleton<WorldInitService>();
		services.AddSingleton<MapPropertiesLoader>();
		services.AddSingleton<MapEditorTerrainImportService>();
		services.AddSingleton<CheatService>();
		services.AddSingleton<EnvironmentService>();
		services.AddSingleton<SpectatorService>();
		services.AddSingleton<SimulationService>(sp =>
		{
			return new SimulationService(sp.GetRequiredService<WorldAccessor>().Current, Entity.Null, GameHost.Instance?._pathfinder ?? new NavMeshPathfinder());
		});

		var provider = services.BuildServiceProvider();
		ServiceLocator.Initialize(provider);
	}

	public override void _EnterTree()
	{
		BuildDependencyInjection();

		// Assign to readonly fields
		_audioService = ServiceLocator.Get<AudioService>();
		_fxService = ServiceLocator.Get<FXService>();
		_saveLoadService = ServiceLocator.Get<SaveLoadService>();
		_editorService = ServiceLocator.Get<EditorService>();
		_replayService = ServiceLocator.Get<ReplayService>();
		_networkService = ServiceLocator.Get<NetworkService>();
		_techTreeService = ServiceLocator.Get<TechTreeService>();
		_inputService = ServiceLocator.Get<InputService>();
		_fogOfWarService = ServiceLocator.Get<FogOfWarService>();
		_unitSpawnService = ServiceLocator.Get<UnitSpawnService>();
		_worldInitService = ServiceLocator.Get<WorldInitService>();
		_mapPropertiesLoader = ServiceLocator.Get<MapPropertiesLoader>();
		_terrainImportService = ServiceLocator.Get<MapEditorTerrainImportService>();
		_cheatService = ServiceLocator.Get<CheatService>();
		_environmentService = ServiceLocator.Get<EnvironmentService>();
		_spectatorService = ServiceLocator.Get<SpectatorService>();
	}
}
