using Arch.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Godot.ReplaySystem;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using System.Text.Json;
using static Realm.Ecs.Common.ResourceConstants;
using static Realm.Ecs.Common.WorldExtensions;

public partial class GameHost : Node3D, IGameAPI
{
	public Camera3D MainCamera { get; private set; }
	public Node MainNode { get; private set; }

	public static GameHost Instance { get; private set; }
	internal readonly NavMeshPathfinder _pathfinder = new();
	public string ActiveMapName { get; private set; } = "melee";

	private AudioService _audioService;
	private FXService _fxService;
	private SaveLoadService _saveLoadService;
	private EditorService _editorService;
	private ReplayService _replayService;
	private SimulationService _simulationService;
	private FogOfWarService _fogOfWarService;
	private UnitSpawnService _unitSpawnService;
	private WorldInitService _worldInitService;
	private MapPropertiesLoader _mapPropertiesLoader;
	private MapEditorTerrainImportService _terrainImportService;
	private CheatService _cheatService;
	private EnvironmentService _environmentService;
	private SpectatorService _spectatorService;
	private TechTreeService _techTreeService;

	public CheatService CheatService => _cheatService;
	public EnvironmentService EnvironmentService => _environmentService;
	public SpectatorService SpectatorService => _spectatorService;

	private float _fDelta;

	
	internal DefinitionManager DefinitionManager => _definitionManager;
	private DefinitionManager _definitionManager = null!;
	
	internal ResourceId _goldResourceId;
	internal ResourceId _woodResourceId;
	internal ResourceId _stoneResourceId;
	
	public Entity PlayerEntity => _playerEntity;
	public Entity EnemyEntity => _enemyPlayerEntity;
	

	private bool _multiplayerActive => Multiplayer.MultiplayerPeer != null;
	private int _localPeerId
	{
		get => _networkService?.LocalPeerId ?? 1;
		set { if (_networkService != null) _networkService.LocalPeerId = value; }
	}

	private int _nextCommandId
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.NextCommandId, 1) ?? 1;
		set => EcsWorld?.Mutate<NetworkState>(_worldEntity, (ref NetworkState s) => s.NextCommandId = value);
	}

	private float _commandSendTimer
	{
		get => _networkService?.CommandSendTimer ?? 0f;
		set { if (_networkService != null) _networkService.CommandSendTimer = value; }
	}

	private int _snapshotSequence
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.SnapshotSequence) ?? 0;
		set => EcsWorld?.Mutate<NetworkState>(_worldEntity, (ref NetworkState s) => s.SnapshotSequence = value);
	}

	private int _lastReceivedBaselineSeq
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.LastReceivedBaselineSeq, -1) ?? -1;
		set => EcsWorld?.Mutate<NetworkState>(_worldEntity, (ref NetworkState s) => s.LastReceivedBaselineSeq = value);
	}

	private bool _hasReceivedInitialBaseline
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, bool>(_worldEntity, s => s.HasReceivedInitialBaseline) ?? false;
		set => EcsWorld?.Mutate<NetworkState>(_worldEntity, (ref NetworkState s) => s.HasReceivedInitialBaseline = value);
	}

	private int _lastAppliedSnapshotSequence
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.LastAppliedSnapshotSequence, -1) ?? -1;
		set => EcsWorld?.Mutate<NetworkState>(_worldEntity, (ref NetworkState s) => s.LastAppliedSnapshotSequence = value);
	}

	private ulong _lastSnapshotReceivedTime
	{
		get => _networkService?.LastSnapshotReceivedTime ?? 0;
		set { if (_networkService != null) _networkService.LastSnapshotReceivedTime = value; }
	}

	private bool _wasClientInMultiplayer => _networkService?.WasClientInMultiplayer ?? false;
	public bool IsConnectionLost => _networkService?.IsConnectionLost ?? false;

	private System.Collections.Generic.Dictionary<int, Entity> _peerIdToPlayerEntityMap
		=> EcsWorld?.GetFieldOrDefault<NetworkMappingState, System.Collections.Generic.Dictionary<int, Entity>>(_worldEntity, s => s.PeerIdToPlayerEntityMap);

	private System.Collections.Generic.Dictionary<int, Entity> _serverToClientEntityMap
		=> EcsWorld?.GetFieldOrDefault<NetworkMappingState, System.Collections.Generic.Dictionary<int, Entity>>(_worldEntity, s => s.ServerToClientEntityMap);

	private System.Collections.Generic.Dictionary<int, int> _clientToServerEntityMap
		=> EcsWorld?.GetFieldOrDefault<NetworkMappingState, System.Collections.Generic.Dictionary<int, int>>(_worldEntity, s => s.ClientToServerEntityMap);




	public World EcsWorld { get; private set; }
	public Entity WorldEntity => _worldEntity;
	public List<Unit3D> SelectedUnits { get; } = new List<Unit3D>();
	public List<Unit3D> AllUnits { get; } = new List<Unit3D>();
	public List<Prop3D> AllProps { get; } = new List<Prop3D>();
	private readonly List<Unit3D> _castlesList = new();

	public static readonly Dictionary<Entity, Unit3D> EntityToUnit3D = new();
	public static readonly Dictionary<Entity, Prop3D> EntityToProp3D = new();

	public static bool TryGetUnit3D(Entity entity, out Unit3D unit)
	{
		return EntityToUnit3D.TryGetValue(entity, out unit);
	}

	public static bool TryGetProp3D(Entity entity, out Prop3D prop)
	{
		return EntityToProp3D.TryGetValue(entity, out prop);
	}

	private Entity _playerEntity
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkMappingState, Entity>(_worldEntity, s => s.PlayerEntity, Entity.Null) ?? Entity.Null;
		set => EcsWorld?.Mutate<NetworkMappingState>(_worldEntity, (ref NetworkMappingState s) => s.PlayerEntity = value);
	}

	private Entity _enemyPlayerEntity
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkMappingState, Entity>(_worldEntity, s => s.EnemyPlayerEntity, Entity.Null) ?? Entity.Null;
		set => EcsWorld?.Mutate<NetworkMappingState>(_worldEntity, (ref NetworkMappingState s) => s.EnemyPlayerEntity = value);
	}

	private Entity _worldEntity;

	private int _replayTickCounter
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, int>(_worldEntity, s => s.ReplayTickCounter) ?? 0;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.ReplayTickCounter = value);
	}
	private System.Diagnostics.Stopwatch _trackerTickStopwatch = new System.Diagnostics.Stopwatch();
	private System.Diagnostics.Stopwatch _trackerIntervalStopwatch = new System.Diagnostics.Stopwatch();
	private List<float> _trackerTickDurations;
	private List<float> _trackerApiDurations;
	private float _trackerLastTickDelay = 0f;
	public string? ActiveSpellTargeting
	{
		get => _inputService?.ActiveSpellTargeting;
		set { if (_inputService != null) _inputService.ActiveSpellTargeting = value; }
	}

	public string? ActiveCommandTargeting
	{
		get => _inputService?.ActiveCommandTargeting;
		set { if (_inputService != null) _inputService.ActiveCommandTargeting = value; }
	}

	public Prop3D SelectedProp { get; private set; } = null;

	public string? ActiveBuildingPlacementType
	{
		get => _inputService?.ActiveBuildingPlacementType;
		set { if (_inputService != null) _inputService.ActiveBuildingPlacementType = value; }
	}

	public int CycleSelectionIndex
	{
		get => _inputService?.GetCycleSelectionIndex(SelectedUnits.Count) ?? 0;
		set => _inputService?.SetCycleSelectionIndex(value, SelectedUnits.Count);
	}

	public bool HasWeaponsUpgrade
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerUpgrades, bool>(_playerEntity, u => u.WeaponsUpgrade) ?? false;
		set => EcsWorld?.Mutate<PlayerUpgrades>(_playerEntity, (ref PlayerUpgrades u) => u.WeaponsUpgrade = value);
	}

	public bool HasShieldsUpgrade
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerUpgrades, bool>(_playerEntity, u => u.ShieldsUpgrade) ?? false;
		set => EcsWorld?.Mutate<PlayerUpgrades>(_playerEntity, (ref PlayerUpgrades u) => u.ShieldsUpgrade = value);
	}

	public bool HasHarvestingUpgrade
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerUpgrades, bool>(_playerEntity, u => u.HarvestingUpgrade) ?? false;
		set => EcsWorld?.Mutate<PlayerUpgrades>(_playerEntity, (ref PlayerUpgrades u) => u.HarvestingUpgrade = value);
	}


	private MeshInstance3D _buildingPreviewMesh = null;


	public bool IsMapEditorMode { get; set; } = false;
	private EditableTerrain _groundTerrain;
	public EditableTerrain GroundTerrain
	{
		get => _groundTerrain;
		private set
		{
			_groundTerrain = value;
			if (value != null && _editorService != null)
			{
				_editorService.SetTerrainColors(value.Colors);
			}
		}
	}
	private MeshInstance3D _brushIndicatorMesh = null;
	private MeshInstance3D _gridOverlayMesh = null;
	private MeshInstance3D _cameraBoundsOverlayMesh = null;
	private MeshInstance3D _pathingOverlayMesh = null;
	public bool PathingOverlayVisible { get; set; } = true;
	public enum EditorTool
	{
		None,
		Raise,
		Lower,
		Flatten,
		Smooth,
		Cliff,
		PaintGrass,
		PaintDirt,
		PaintRock,
		PaintSand,
		PlaceUnit,
		PlaceProp,
		PlaceDecal,
		DeleteObject,
		SelectMove,
		Eyedropper,
		Noise,
		Ramp,
		PlacePropClump,
		FloodFill,
		SelectArea,
		PasteArea,
		PaintPathing
	}
	private EditorTool _activeEditorTool = EditorTool.None;
	public EditorTool ActiveEditorTool
	{
		get => _activeEditorTool;
		set
		{
			_activeEditorTool = value;
			_editorService?.SetIsPastingObject(false);
		}
	}
	public string ActivePlaceId { get; set; } = ""; // "soldier", "tree", etc.
	public string GetTerrainStatusString(Vector3 hitPos)
	{
		return _editorService.GetTerrainStatusString(hitPos, ActiveEditorTool.ToString(), ActivePlaceId);
	}

	public void LoadMapProperties(string path)
	{
		_mapPropertiesLoader?.LoadMapProperties(_worldEntity, path);
	}

	public bool ImportTerrainFromMinimap(
		string selectedPath,
		out float[,] smoothedHeights,
		out Color[,] colors,
		out List<(float X, float Y, float Z, float Rot, float Scale)> treePositions)
	{
		smoothedHeights = null;
		colors = null;
		treePositions = null;
		if (_terrainImportService == null)
		{
			return false;
		}
		return _terrainImportService.ImportTerrain(_worldEntity, selectedPath, out smoothedHeights, out colors, out treePositions);
	}
	public bool PlaceUnitIsEnemy { get; set; } = false;
	public float EditorBrushRadius { get; set; } = 6.0f;
	public float EditorBrushStrength { get; set; } = 3.0f;
	public float EditorFlattenHeight { get; set; } = 0.0f;
	public Color EditorPaintColor { get; set; } = new Color(0.2f, 0.6f, 0.2f);
	public Color EditorCliffPaintColor { get; set; } = new Color(0.5f, 0.5f, 0.52f);
	public bool EditorSnapToGrid { get; set; } = false;
	public float EditorPlacementRotation { get; set; } = 0.0f;
	public float EditorPlacementScale { get; set; } = 1.0f;
	public bool EditorGridVisible { get; set; } = false;
	public bool EditorCameraBoundsVisible { get; set; } = false;
	public float EditorCameraBoundsLeft
	{
		get => _editorService.GetCameraBoundsLeft(_worldEntity);
		set => _editorService.SetCameraBoundsLeft(_worldEntity, value);
	}

	public float EditorCameraBoundsRight
	{
		get => _editorService.GetCameraBoundsRight(_worldEntity);
		set => _editorService.SetCameraBoundsRight(_worldEntity, value);
	}

	public float EditorCameraBoundsTop
	{
		get => _editorService.GetCameraBoundsTop(_worldEntity);
		set => _editorService.SetCameraBoundsTop(_worldEntity, value);
	}

	public float EditorCameraBoundsBottom
	{
		get => _editorService.GetCameraBoundsBottom(_worldEntity);
		set => _editorService.SetCameraBoundsBottom(_worldEntity, value);
	}
	public MirrorMode EditorMirrorMode
	{
		get => EcsWorld?.GetFieldOrDefault<EditorState, MirrorMode>(_worldEntity, s => s.MirrorMode, MirrorMode.None) ?? MirrorMode.None;
		set => EcsWorld?.Mutate<EditorState>(_worldEntity, (ref EditorState s) => s.MirrorMode = value);
	}
	public bool EditorBrushIsSquare { get; set; } = false;
	public float EditorClumpDensity { get; set; } = 5.0f;
	public float EditorClumpScaleVar { get; set; } = 0.3f;
	public bool EditorClumpMode { get; set; } = false;

	public bool EditorRandomRotation { get; set; } = false;
	public bool EditorRandomScale { get; set; } = false;
	public string EditorSkyboxPath
	{
		get => _editorService.GetSkyboxPath(_worldEntity);
		set => _editorService.SetSkyboxPath(_worldEntity, value);
	}

	public bool EditorHasUnsavedChanges
	{
		get => _editorService.GetHasUnsavedChanges(_worldEntity);
		set => _editorService.SetHasUnsavedChanges(_worldEntity, value);
	}

	public bool EditorBlockMode
	{
		get => _editorService.GetBlockMode(_worldEntity);
		set => _editorService.SetBlockMode(_worldEntity, value);
	}

	public float EditorBlockLevelHeight
	{
		get => _editorService.GetBlockLevelHeight(_worldEntity);
		set => _editorService.SetBlockLevelHeight(_worldEntity, value);
	}
	private Node _hoveredEditorObject = null;
	private MeshInstance3D _selectionHighlightMesh = null;



	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_editorService.GenerateNewRandomPlacementRotationAndScale();
	}
	public bool PasteOptionTextures { get; set; } = true;
	public bool PasteOptionHeights { get; set; } = true;
	public bool PasteOptionEntities { get; set; } = true;

	public Node SelectedEditorObject
	{
		get => _selectedEditorObject;
		set
		{
			if (_selectedEditorObject == value) return;
			if (GodotObject.IsInstanceValid(_selectedEditorObject))
			{
				if (_selectedEditorObject is Unit3D oldUnit)
				{
					oldUnit.IsSelected = false;
				}
				else if (_selectedEditorObject is Prop3D oldProp)
				{
					oldProp.IsSelected = false;
				}
				else if (_selectedEditorObject is Decal oldDecal)
				{
					UpdateDecalSelectionRing(oldDecal, false);
				}
			}
			_selectedEditorObject = value;
			if (GodotObject.IsInstanceValid(_selectedEditorObject))
			{
				if (_selectedEditorObject is Unit3D newUnit)
				{
					newUnit.IsSelected = true;
				}
				else if (_selectedEditorObject is Prop3D newProp)
				{
					newProp.IsSelected = true;
				}
				else if (_selectedEditorObject is Decal newDecal)
				{
					UpdateDecalSelectionRing(newDecal, true);
				}
			}
			MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
		}
	}
	private Node _selectedEditorObject = null;
	private bool _isDraggingObject = false;
	private Vector3 _dragObjectStartPos;
	private Vector3 _dragObjectStartRot;
	private Vector3 _dragObjectStartScale;
	private bool _dragObjectStartIsEnemy;
	private Vector3 _dragObjectStartHitPos;
	private bool _dragObjectHasMoved;
	private Node3D _editorPreviewNode;
	private string _editorPreviewType = "";
	private string _editorPreviewId = "";
	private bool _editorPreviewIsEnemy = false;


	public struct MinimapPing
	{
		public Vector3 WorldPos;
		public float LifeTime;
		public float MaxLifeTime;
	}
	public List<MinimapPing> ActivePings { get; } = new List<MinimapPing>();
	public bool ActivePingMode
	{
		get => _inputService?.ActivePingMode ?? false;
		set { if (_inputService != null) _inputService.ActivePingMode = value; }
	}


	public struct UnitMetadata
	{
		public string UnitId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public float MaxHp { get; set; }
		public float Damage { get; set; }
		public float Range { get; set; }
		public float Armor { get; set; }
		public float Speed { get; set; }
		public float AttackCooldown { get; set; }
		public float ScanRadius { get; set; }
		public float CostGold { get; set; }
		public float CostWood { get; set; }
		public float CostStone { get; set; }
		public float ProductionTime { get; set; }
		public int   PopCost { get; set; }
		public string AttackType { get; set; }
		public string ArmorType { get; set; }
		public float GoldBounty { get; set; }
		public string ModelPath { get; set; }
		public string[]? BuildOptions { get; set; }
		public bool IsHero { get; set; }
		public string[]? Abilities { get; set; }
		public float XpBounty { get; set; }
		public string[]? PathingCapabilities { get; set; }
		public string? MovementType { get; set; }
	}

	public static int GetUnitPathingFlags(UnitMetadata meta)
	{
		return Instance?._unitSpawnService?.GetUnitPathingFlags(meta) ?? 8;
	}


	public int MaxPopulation
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerPopulation, int>(_playerEntity, p => p.Max) ?? 0;
		private set => EcsWorld?.Mutate<PlayerPopulation>(_playerEntity, (ref PlayerPopulation p) =>
			EcsWorld.Set(_playerEntity, new PlayerPopulation(p.Current, value)));
	}

	public int CurrentPopulation
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerPopulation, int>(_playerEntity, p => p.Current) ?? 0;
		set => EcsWorld?.Mutate<PlayerPopulation>(_playerEntity, (ref PlayerPopulation p) =>
			EcsWorld.Set(_playerEntity, new PlayerPopulation(value, p.Max)));
	}

	public float GameElapsedTime
	{
		get => EcsWorld?.GetFieldOrDefault<WorldState, float>(_worldEntity, s => s.GameElapsedTime) ?? 0f;
		private set => EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState s) =>
			EcsWorld.Set(_worldEntity, new WorldState(value, s.TimeOfDayIndex, s.TimeOfDayTimer, s.DayNightCycleEnabled)));
	}

	public int TimeOfDayIndex
		=> EcsWorld?.GetFieldOrDefault<WorldState, int>(_worldEntity, s => s.TimeOfDayIndex) ?? 0;

	private float TimeOfDayTimer
	{
		get => EcsWorld?.GetFieldOrDefault<WorldState, float>(_worldEntity, s => s.TimeOfDayTimer) ?? 0f;
		set => EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState s) =>
			EcsWorld.Set(_worldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, value, s.DayNightCycleEnabled)));
	}

	private const float TimeOfDayCycleDuration = 90f;

	public const float FireballCooldownMax = 12f;
	public const float LightningCooldownMax = 18f;
	public const float HolyLightCooldownMax = 15f;

	public float FireballCooldown
	{
		get => EcsWorld?.GetFieldOrDefault<SpellCooldowns, float>(_playerEntity, c => c.FireballCooldown) ?? 0f;
		set => EcsWorld?.Mutate<SpellCooldowns>(_playerEntity, (ref SpellCooldowns c) =>
			EcsWorld.Set(_playerEntity, new SpellCooldowns(value, c.LightningCooldown, c.HolyLightCooldown)));
	}

	public float LightningCooldown
	{
		get => EcsWorld?.GetFieldOrDefault<SpellCooldowns, float>(_playerEntity, c => c.LightningCooldown) ?? 0f;
		set => EcsWorld?.Mutate<SpellCooldowns>(_playerEntity, (ref SpellCooldowns c) =>
			EcsWorld.Set(_playerEntity, new SpellCooldowns(c.FireballCooldown, value, c.HolyLightCooldown)));
	}

	public float HolyLightCooldown
	{
		get => EcsWorld?.GetFieldOrDefault<SpellCooldowns, float>(_playerEntity, c => c.HolyLightCooldown) ?? 0f;
		set => EcsWorld?.Mutate<SpellCooldowns>(_playerEntity, (ref SpellCooldowns c) =>
			EcsWorld.Set(_playerEntity, new SpellCooldowns(c.FireballCooldown, c.LightningCooldown, value)));
	}


	public const float ResourceCap = ResourceConstants.ResourceCap;



	public static readonly Dictionary<string, UnitMetadata> UnitRegistry = new();

	public string GetFallbackModelPath(string unitId, bool isBuilding)
	{
		return _unitSpawnService.GetFallbackModelPath(unitId, isBuilding);
	}

	private float _goldBackup
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, float>(_worldEntity, s => s.GoldBackup, 500f) ?? 500f;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.GoldBackup = value);
	}

	private float _woodBackup
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, float>(_worldEntity, s => s.WoodBackup, 400f) ?? 400f;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.WoodBackup = value);
	}

	private float _stoneBackup
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, float>(_worldEntity, s => s.StoneBackup, 200f) ?? 200f;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.StoneBackup = value);
	}

	private IMapScript _activeMapScript;

	private bool DayNightCycleEnabled
	{
		get => EcsWorld?.GetFieldOrDefault<WorldState, bool>(_worldEntity, s => s.DayNightCycleEnabled, true) ?? true;
		set => EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState s) =>
			EcsWorld.Set(_worldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, s.TimeOfDayTimer, value)));
	}

	public event Action<IUnit>? OnUnitCreated;
	public event Action<IUnit, IUnit?>? OnUnitDied;
	public event Action<IUnit, IUnit, float>? OnUnitDamaged;
	public event Action<IUnit?, string, System.Numerics.Vector3>? OnSpellCast;
	public event Action<string, IUnit?>? OnPlayerChatMessage;
	public event Action<IUnit>? OnUnitSelected;

	public void TriggerPlayerChatMessage(string message)
	{
		IUnit? selected = null;
		if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
		{
			selected = GetUnitWrapper(SelectedUnits[0].Entity);
		}
		OnPlayerChatMessage?.Invoke(message, selected);
	}

	public void TriggerKillUnit(Unit3D unit)
	{
		KillUnit(unit);
	}

	private void InitializePlayerResources(Entity playerEntity)
	{
		var resourcesDict = new Dictionary<ResourceId, int>
		{
			{ _goldResourceId, 500 },
			{ _woodResourceId, 400 },
			{ _stoneResourceId, 200 }
		};
		EcsWorld.Add(playerEntity, new PlayerResources(resourcesDict));
	}

	private void SetupPlayerEntityComponents(Entity playerEntity)
	{
		EcsWorld.Add(playerEntity, new PlayerPopulation(0, 0));
		EcsWorld.Add(playerEntity, new SpellCooldowns(0f, 0f, 0f));
		EcsWorld.Add(playerEntity, new PlayerUpgrades(false, false, false));
	}

	private void SetupWorldEntityComponents()
	{
		int width = GroundTerrain != null ? GroundTerrain.Width : 126;
		int depth = GroundTerrain != null ? GroundTerrain.Depth : 126;
		float spacing = GroundTerrain != null ? GroundTerrain.Spacing : 2.0f;
		float cellSize = GroundTerrain != null ? GroundTerrain.CellSize : 5.0f / 2.5f / 10.0f;
		float waterHeight = GroundTerrain != null ? GroundTerrain.WaterHeight : -2.0f;
		bool waterEnabled = GroundTerrain != null ? GroundTerrain.WaterEnabled : true;
		float[,] heights = GroundTerrain != null ? GroundTerrain.Heights : null;
		int[,] pathingCodes = GroundTerrain != null ? GroundTerrain.PathingCodes : null;
		DotRecast.Detour.DtNavMesh navMesh = GroundTerrain != null ? GroundTerrain.NavMesh : null;
		DotRecast.Detour.DtNavMeshQuery navMeshQuery = GroundTerrain != null ? GroundTerrain.NavMeshQuery : null;

		_worldEntity = _worldInitService.SetupWorldEntityComponents(
			width, depth, spacing, cellSize, waterHeight, waterEnabled,
			heights, pathingCodes, navMesh, navMeshQuery
		);
	}

	float IGameAPI.Gold
	{
		get
		{
			if (EcsWorld != null && EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(_goldResourceId, out var val))
				return val;
			return _goldBackup;
		}
		set
		{
			if (EcsWorld != null)
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(_goldResourceId))
						r.Value[_goldResourceId] = (int)value;
				});
			else
				_goldBackup = value;
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.Wood
	{
		get
		{
			if (EcsWorld != null && EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(_woodResourceId, out var val))
				return val;
			return _woodBackup;
		}
		set
		{
			if (EcsWorld != null)
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(_woodResourceId))
						r.Value[_woodResourceId] = (int)value;
				});
			else
				_woodBackup = value;
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.Stone
	{
		get
		{
			if (EcsWorld != null && EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(_stoneResourceId, out var val))
				return val;
			return _stoneBackup;
		}
		set
		{
			if (EcsWorld != null)
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(_stoneResourceId))
						r.Value[_stoneResourceId] = (int)value;
				});
			else
				_stoneBackup = value;
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.GameElapsedTime => GameElapsedTime;

	IUnit IGameAPI.SpawnUnit(string unitTypeId, System.Numerics.Vector3 position, bool isEnemy, bool bypassPopulation)
	{
		var pos = new Vector3(position.X, position.Y, position.Z);
		if (!UnitRegistry.TryGetValue(unitTypeId, out var meta))
		{
			throw new ArgumentException($"Unit ID '{unitTypeId}' not found in registry.");
		}
		var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
		
		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : _unitSpawnService.GetFallbackModelPath(unitTypeId, meta.Speed == 0f);

		string name = isEnemy ? _unitSpawnService.GetEnemyUnitName(unitTypeId, meta.Name) : meta.Name;

		var entity = CreateEcsUnit(unitTypeId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, pos, playerOwner);
		if (bypassPopulation)
		{
			EcsWorld.Add(entity, new BypassPopulationTag());
		}
		var unit3D = SpawnUnit3D(entity, unitTypeId, modelPath, pos, meta.Speed == 0f, isEnemy, bypassPopulation);
		
		return GetUnitWrapper(entity);
	}

	void IGameAPI.SpawnResourceNode(string resourceType, System.Numerics.Vector3 position, float amount)
	{
		var pos = new Vector3(position.X, position.Y, position.Z);
		var prop = SpawnPropExternal(resourceType, pos);
		prop.ResourceAmount = amount;
	}

	IEnumerable<IUnit> IGameAPI.GetAllUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity))
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(System.Numerics.Vector3 center, float radius)
	{
		var list = new List<IUnit>();
		var godotCenter = new Vector3(center.X, center.Y, center.Z);
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity) && u.GlobalPosition.DistanceTo(godotCenter) <= radius)
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	IEnumerable<IResourceNode> IGameAPI.GetResourceNodes()
	{
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				if (prop.PropId == "goldmine" || prop.PropId == "tree" || prop.PropId == "rock")
				{
					yield return new ResourceNodeWrapper(prop);
				}
			}
		}
	}

	void IGameAPI.ShowFeedbackText(string text, System.Numerics.Vector3 color)
	{
		if (InGameHUD.Instance != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			InGameHUD.Instance.CallDeferred(nameof(InGameHUD.ShowFeedbackText), text, gColor);
		}
	}


	void IGameAPI.TriggerVictory()
	{
		GD.Print("[GameHost] Victory triggered by map script!");
		UIManager.Instance?.CallDeferred(nameof(UIManager.TransitionTo), (int)GameScreen.GameOver, true);
	}

	void IGameAPI.TriggerDefeat()
	{
		GD.Print("[GameHost] Defeat triggered by map script!");
		UIManager.Instance?.CallDeferred(nameof(UIManager.TransitionTo), (int)GameScreen.GameOver, false);
	}

	IUnit? IGameAPI.GetCastle(bool isEnemy)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.UnitId == "castle" && u.IsEnemy == isEnemy && EcsWorld.IsAlive(u.Entity) && !EcsWorld.Has<Dead>(u.Entity))
			{
				return GetUnitWrapper(u.Entity);
			}
		}
		return null;
	}

	void IGameAPI.UpgradeUnit(IUnit unit)
	{
		if (unit is IEcsEntityWrapper wrapper)
		{
			var entity = wrapper.Entity;
			if (EcsWorld.IsAlive(entity) && GameHost.TryGetUnit3D(entity, out var tower))
			{
				if (GodotObject.IsInstanceValid(tower))
				{
					int currentLevel = 1;
					if (EcsWorld.Has<TowerUpgradeLevel>(tower.Entity))
					{
						currentLevel = EcsWorld.Get<TowerUpgradeLevel>(tower.Entity).Value;
					}

					int newLevel = currentLevel + 1;
					EcsWorld.Set(tower.Entity, new TowerUpgradeLevel(newLevel));
					
					string baseName = "Spell Tower";
					if (EcsWorld.Has<Name>(tower.Entity))
					{
						var nameComp = EcsWorld.Get<Name>(tower.Entity);
						if (nameComp.Value.Contains("Orc")) baseName = "Orc Totem Tower";
					}
					EcsWorld.Set(tower.Entity, new Name($"{baseName} (Lvl {newLevel})"));

					if (EcsWorld.Has<Health>(tower.Entity))
					{
						var hp = EcsWorld.Get<Health>(tower.Entity);
						EcsWorld.Set(tower.Entity, new Health(hp.Current + 250f, hp.Max + 250f));
					}
					if (EcsWorld.Has<Armor>(tower.Entity))
					{
						var arm = EcsWorld.Get<Armor>(tower.Entity);
						EcsWorld.Set(tower.Entity, new Armor(arm.Value + 5f));
					}
					if (EcsWorld.Has<Attack>(tower.Entity))
					{
						var atk = EcsWorld.Get<Attack>(tower.Entity);
						EcsWorld.Set(tower.Entity, new Attack(atk.Damage + 10f, atk.Range, atk.Cooldown));
					}
					
					float newScale = 1.0f + newLevel * 0.2f;
					tower.Scale = new Godot.Vector3(newScale, newScale, newScale);
				}
			}
		}
	}

	void IGameAPI.SpawnTargetIndicator(System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		SpawnTargetIndicator(new Godot.Vector3(position.X, position.Y, position.Z), new Godot.Color(color.X, color.Y, color.Z));
	}

	int IGameAPI.MaxPopulation
	{
		get => MaxPopulation;
		set => MaxPopulation = value;
	}

	int IGameAPI.CurrentPopulation => CurrentPopulation;

	IUnit? IGameAPI.GetUnitById(int uniqueId)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.Entity.Id == uniqueId && EcsWorld.IsAlive(u.Entity))
			{
				return GetUnitWrapper(u.Entity);
			}
		}
		return null;
	}

	IEnumerable<IUnit> IGameAPI.GetSelectedUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in SelectedUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity))
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	void IGameAPI.PingMinimap(System.Numerics.Vector3 position)
	{
		AddMinimapPing(new Vector3(position.X, position.Y, position.Z));
	}

	void IGameAPI.StartBuildingPlacement(string unitTypeId)
	{
		EnterBuildingPlacement(unitTypeId);
	}

	void IGameAPI.GenerateMapDirectory(string mapName, string? targetDirectory)
	{
		string parentDir = string.IsNullOrEmpty(targetDirectory) ? "user://maps" : targetDirectory;
		string globalParentDir = ProjectSettings.GlobalizePath(parentDir);
		string mapDir = System.IO.Path.Combine(globalParentDir, mapName);
		System.IO.Directory.CreateDirectory(mapDir);

		string scriptContent = $@"namespace Realm.Maps;

using Realm.MapAPI;

public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}
";
		System.IO.File.WriteAllText(System.IO.Path.Combine(mapDir, "MapScript.cs"), scriptContent);
		System.IO.File.WriteAllText(System.IO.Path.Combine(mapDir, "metadata.json"), "{}");
		System.IO.File.WriteAllText(System.IO.Path.Combine(mapDir, "terrain.json"), "{}");
	}

	event Action<IUnit>? IGameAPI.OnUnitCreated
	{
		add => OnUnitCreated += value;
		remove => OnUnitCreated -= value;
	}

	event Action<IUnit, IUnit?>? IGameAPI.OnUnitDied
	{
		add => OnUnitDied += value;
		remove => OnUnitDied -= value;
	}

	event Action<IUnit, IUnit, float>? IGameAPI.OnUnitDamaged
	{
		add => OnUnitDamaged += value;
		remove => OnUnitDamaged -= value;
	}

	event Action<IUnit?, string, System.Numerics.Vector3>? IGameAPI.OnSpellCast
	{
		add => OnSpellCast += value;
		remove => OnSpellCast -= value;
	}

	event Action<string, IUnit?>? IGameAPI.OnPlayerChatMessage
	{
		add => OnPlayerChatMessage += value;
		remove => OnPlayerChatMessage -= value;
	}

	event Action<IUnit>? IGameAPI.OnUnitSelected
	{
		add => OnUnitSelected += value;
		remove => OnUnitSelected -= value;
	}


	void IGameAPI.CreateFloatingText(string text, System.Numerics.Vector3 position, System.Numerics.Vector3 color, float duration)
	{
		Callable.From(() =>
		{
			var label = new Label3D();
			label.Text = text;
			label.Modulate = new Color(color.X, color.Y, color.Z);
			label.OutlineModulate = Colors.Black;
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			label.Position = new Vector3(position.X, position.Y + 1.5f, position.Z);
			label.FontSize = 48;
			AddChild(label);

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(label, "position", label.Position + new Vector3(0, 2.0f, 0), duration);
			tween.TweenProperty(label, "modulate:a", 0.0f, duration);
			tween.Chain().TweenCallback(Callable.From(label.QueueFree));
		}).CallDeferred();
	}

	void IGameAPI.SpawnVisualEffect(string effectTypeId, System.Numerics.Vector3 position, float scale)
	{
		Callable.From(() =>
		{
			var pos = new Vector3(position.X, position.Y, position.Z);
			if (effectTypeId == "fireblast")
			{
				SpawnSpritesheetEffect("res://Assets/2d/SpellSpritesheets/solar_flare_sheet.png", pos + new Vector3(0, 0.5f, 0), 4, 4, 0.05f, scale * 6f);
			}
			else if (effectTypeId == "lightning")
			{
				SpawnSpritesheetEffect("res://Assets/2d/SpellSpritesheets/arcane_surge_sheet.png", pos + new Vector3(0, 0.5f, 0), 4, 4, 0.035f, scale * 6f);
			}
			else if (effectTypeId == "holylight")
			{
				var cylinder = new MeshInstance3D();
				var cylinderMesh = new CylinderMesh();
				cylinderMesh.TopRadius = 1.5f * scale;
				cylinderMesh.BottomRadius = 1.5f * scale;
				cylinderMesh.Height = 8.0f;
				cylinder.Mesh = cylinderMesh;
				cylinder.Position = pos + new Vector3(0, 4.0f, 0);

				var material = new StandardMaterial3D();
				material.AlbedoColor = new Color(0.2f, 0.9f, 0.3f, 0.6f);
				material.EmissionEnabled = true;
				material.Emission = new Color(0.1f, 0.8f, 0.2f);
				material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				cylinder.MaterialOverride = material;

				AddChild(cylinder);

				var tween = CreateTween();
				tween.SetParallel(true);
				tween.TweenProperty(cylinder, "scale", new Vector3(1.2f, 1.0f, 1.2f), 0.5f);
				tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.5f);
				tween.TweenProperty(material, "emission:a", 0.0f, 0.5f);
				tween.Chain().TweenCallback(Callable.From(cylinder.QueueFree));
			}
		}).CallDeferred();
	}

	void IGameAPI.SpawnProjectile(string projectileTypeId, System.Numerics.Vector3 start, System.Numerics.Vector3 target, float speed)
	{
		Callable.From(() =>
		{
			var startPos = new Vector3(start.X, start.Y, start.Z);
			var targetPos = new Vector3(target.X, target.Y, target.Z);
			if (projectileTypeId == "arrow")
			{
				SpawnArrowProjectile(startPos, targetPos);
			}
			else
			{
				var meshInstance = new MeshInstance3D();
				var sphereMesh = new SphereMesh();
				sphereMesh.Radius = 0.25f;
				sphereMesh.Height = 0.5f;
				meshInstance.Mesh = sphereMesh;
				meshInstance.Position = startPos + new Vector3(0, 1.0f, 0);

				var material = new StandardMaterial3D();
				material.AlbedoColor = new Color(0.9f, 0.8f, 0.2f);
				material.EmissionEnabled = true;
				material.Emission = new Color(0.8f, 0.6f, 0.1f);
				meshInstance.MaterialOverride = material;

				AddChild(meshInstance);

				float dist = startPos.DistanceTo(targetPos);
				float duration = speed > 0.01f ? dist / speed : 1.0f;

				var tween = CreateTween();
				tween.TweenProperty(meshInstance, "global_position", targetPos + new Vector3(0, 1.0f, 0), duration);
				tween.Chain().TweenCallback(Callable.From(meshInstance.QueueFree));
			}
		}).CallDeferred();
	}

	void IGameAPI.SetLeaderboardVisible(string title, bool visible)
	{
		Callable.From(() => InGameHUD.Instance?.SetLeaderboardVisible(title, visible)).CallDeferred();
	}

	void IGameAPI.SetLeaderboardValue(string label, string value)
	{
		Callable.From(() => InGameHUD.Instance?.SetLeaderboardValue(label, value)).CallDeferred();
	}

	void IGameAPI.ClearLeaderboard()
	{
		Callable.From(() => InGameHUD.Instance?.ClearLeaderboard()).CallDeferred();
	}

	void IGameAPI.StartCountdownTimer(float duration, string label)
	{
		Callable.From(() => InGameHUD.Instance?.StartCountdownTimer(duration, label)).CallDeferred();
	}

	void IGameAPI.StopCountdownTimer()
	{
		Callable.From(() => InGameHUD.Instance?.StopCountdownTimer()).CallDeferred();
	}

	void IGameAPI.ShakeCamera(float intensity, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera;
			if (camera == null) return;

			var startPos = camera.Position;
			var tween = CreateTween();
			float stepDuration = duration / 20f;
			for (int i = 0; i < 20; i++)
			{
				var offset = new Vector3(
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1)
				);
				tween.TweenProperty(camera, "position", startPos + offset, stepDuration);
			}
			tween.TweenProperty(camera, "position", startPos, stepDuration);
		}).CallDeferred();
	}

	void IGameAPI.PanCameraTo(System.Numerics.Vector3 position, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera;
			if (camera == null) return;

			var targetPos = new Vector3(position.X, camera.Position.Y, position.Z + 15.0f);
			var tween = CreateTween();
			tween.TweenProperty(camera, "position", targetPos, duration);
		}).CallDeferred();
	}

	void IGameAPI.SetTimeOfDay(float time)
	{
		Callable.From(() =>
		{

			float clampedTime = Mathf.Clamp(time, 0f, 24f);
			
			int index;
			if (clampedTime >= 5f && clampedTime < 6f) index = 3;
			else if (clampedTime >= 6f && clampedTime < 18f) index = 0;
			else if (clampedTime >= 18f && clampedTime < 20f) index = 1;
			else index = 2;

			float timer = (clampedTime / 24f) * TimeOfDayCycleDuration;
			UpdateDayNightVisuals(clampedTime / 24f);

			EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState state) =>
				EcsWorld.Set(_worldEntity, new WorldState(state.GameElapsedTime, index, timer, state.DayNightCycleEnabled)));
		}).CallDeferred();
	}

	void IGameAPI.SetDayNightCycleEnabled(bool enabled)
	{
		DayNightCycleEnabled = enabled;
	}

	void IGameAPI.KillUnit(IUnit unit)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var u3d))
			{
				if (GodotObject.IsInstanceValid(u3d))
				{
					if (!EcsWorld.Has<Dead>(wrapper.Entity))
					{
						EcsWorld.Add<Dead>(wrapper.Entity);
						this.CallDeferred(nameof(KillUnit), u3d);
					}
				}
			}
		}
	}

	void IGameAPI.DestroyUnit(IUnit unit)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var u3d))
			{
				if (GodotObject.IsInstanceValid(u3d))
				{
					SelectedUnits.Remove(u3d);
					AllUnits.Remove(u3d);
					EntityToUnit3D.Remove(wrapper.Entity);
					if (u3d.UnitId == "castle")
					{
						_castlesList.Remove(u3d);
					}
					int id = wrapper.Entity.Id;
					_unitWrapperCache.Remove(id);
					EcsWorld.Destroy(wrapper.Entity);
					u3d.QueueFree();
				}
			}
		}
	}





	private int _nextTimerHandle = 0;
	private readonly Dictionary<int, (float Interval, float Remaining, bool Repeating, Action Callback)> _scheduledTimers = new();

	private static readonly Random _rng = new();

	int IGameAPI.PlayerCount
	{
		get
		{
			if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true)
			{
				int count = 0;
				foreach (var p in playersState.Players)
				{
					if (p.Active) count++;
				}
				return Math.Max(1, count);
			}
			return 1;
		}
	}

	string IGameAPI.GetPlayerName(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Name;
		}
		return $"Player {playerIndex + 1}";
	}

	bool IGameAPI.IsPlayerActive(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Active;
		}
		return playerIndex == 0;
	}

	float IGameAPI.GetPlayerGold(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Gold;
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Gold;
		}
		return 0f;
	}

	void IGameAPI.SetPlayerGold(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold = amount; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Gold = Math.Max(0f, amount);
		}
	}

	void IGameAPI.AdjustPlayerGold(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold += delta; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Gold = Math.Max(0f, playersState.Players[playerIndex].Gold + delta);
		}
	}

	IUnit IGameAPI.SpawnUnitForPlayer(string unitTypeId, System.Numerics.Vector3 position, int playerIndex)
	{
		bool isEnemy = playerIndex != 0;
		return ((IGameAPI)this).SpawnUnit(unitTypeId, position, isEnemy);
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex)
	{
		bool isEnemy = playerIndex != 0;
		foreach (var unit in ((IGameAPI)this).GetAllUnits())
		{
			if (unit.IsEnemy == isEnemy) yield return unit;
		}
	}

	void IGameAPI.TriggerPlayerDefeat(int playerIndex, string reason)
	{
		if (playerIndex == 0) ((IGameAPI)this).TriggerDefeat();
	}

	void IGameAPI.TriggerPlayerVictory(int playerIndex)
	{
		if (playerIndex == 0) ((IGameAPI)this).TriggerVictory();
	}

	void IGameAPI.BroadcastMessage(string message)
	{
		((IGameAPI)this).ShowFeedbackText(message, new System.Numerics.Vector3(0.9f, 0.9f, 0.9f));
	}

	void IGameAPI.SendMessageToPlayer(int playerIndex, string message)
	{
		if (playerIndex == 0) ((IGameAPI)this).ShowFeedbackText(message, new System.Numerics.Vector3(0.9f, 0.9f, 0.9f));
	}

	int IGameAPI.ScheduleTimer(float delay, Action callback)
	{
		int handle = _nextTimerHandle++;
		_scheduledTimers[handle] = (delay, delay, false, callback);
		return handle;
	}

	int IGameAPI.ScheduleRepeatingTimer(float interval, Action callback)
	{
		int handle = _nextTimerHandle++;
		_scheduledTimers[handle] = (interval, interval, true, callback);
		return handle;
	}

	void IGameAPI.CancelTimer(int timerHandle)
	{
		_scheduledTimers.Remove(timerHandle);
	}

	int IGameAPI.RandomInt(int min, int max) => _rng.Next(min, max + 1);

	float IGameAPI.RandomFloat(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

	System.Numerics.Vector3 IGameAPI.GetPlayerStartLocation(int playerIndex)
	{
		return System.Numerics.Vector3.Zero;
	}

	void IGameAPI.SetPlayerTeam(int playerIndex, int teamIndex)
	{
	}

	int IGameAPI.GetPlayerTeam(int playerIndex)
	{
		return 0;
	}

	void IGameAPI.SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
	}

	bool IGameAPI.IsPlayerComputer(int playerIndex)
	{
		return false;
	}

	void IGameAPI.SetUnitColor(IUnit unit, System.Numerics.Vector3 color)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var u3d) && GodotObject.IsInstanceValid(u3d))
				u3d.ApplyModelTint(new Godot.Color(color.X, color.Y, color.Z));
		}
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(System.Numerics.Vector3 center, float radius, System.Func<IUnit, bool> filter)
	{
		var godotCenter = new Vector3(center.X, center.Y, center.Z);
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !EcsWorld.IsAlive(u.Entity)) continue;
			if (u.GlobalPosition.DistanceTo(godotCenter) > radius) continue;
			var w = GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}
		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex, System.Func<IUnit, bool> filter)
	{
		bool isEnemy = playerIndex != 0;
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !EcsWorld.IsAlive(u.Entity)) continue;
			if (u.IsEnemy != isEnemy) continue;
			var w = GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}
		return list;
	}

	void IGameAPI.IssueAttackMoveOrder(IUnit unit, System.Numerics.Vector3 destination)
	{
		unit.AttackMove(destination);
	}

	void IGameAPI.IssueCastOrder(IUnit caster, string abilityId, IUnit target)
	{
		caster.Attack(target);
	}

	void IGameAPI.IssueCastOrderAt(IUnit caster, string abilityId, System.Numerics.Vector3 position)
	{
		caster.AttackMove(position);
	}

	void IGameAPI.SetAbilityAutoCast(IUnit unit, string abilityId, bool active)
	{
	}

	void IGameAPI.SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
	}

	void IGameAPI.AddLeaderboardRow(string label, string value, System.Numerics.Vector3? color)
	{
		InGameHUD.Instance?.SetLeaderboardValue(label, value);
	}

	float IGameAPI.GetPlayerWood(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Wood;
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Wood;
		}
		return 0f;
	}

	void IGameAPI.SetPlayerWood(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood = amount; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, amount);
		}
	}

	void IGameAPI.AdjustPlayerWood(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood += delta; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, playersState.Players[playerIndex].Wood + delta);
		}
	}

	void IGameAPI.SetUnitOwner(IUnit unit, int playerIndex)
	{
		unit.IsEnemy = playerIndex != 0;
	}

	int IGameAPI.GetPlayerCurrentPopulation(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).CurrentPopulation;
		return 0;
	}

	int IGameAPI.GetPlayerMaxPopulation(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).MaxPopulation;
		return 200;
	}

	void IGameAPI.SetPlayerMaxPopulation(int playerIndex, int max)
	{
		if (playerIndex == 0) ((IGameAPI)this).MaxPopulation = max;
	}

	void IGameAPI.SetCountdownTimerLabel(string label)
	{
		InGameHUD.Instance?.UpdateCountdownLabel(label);
	}

	void IGameAPI.IssueAttackMoveOrderToPlayer(int playerIndex, System.Numerics.Vector3 destination)
	{
		foreach (var unit in ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex))
		{
			if (!unit.IsDead && !unit.IsBuilding)
				unit.AttackMove(destination);
		}
	}

	int IGameAPI.CountUnitsOwnedByPlayer(int playerIndex, System.Func<IUnit, bool>? filter)
	{
		int count = 0;
		foreach (var unit in ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex))
		{
			if (!unit.IsDead && (filter == null || filter(unit)))
				count++;
		}
		return count;
	}





	public event Action<IUnit, int>? OnUnitEnterZone;
	public event Action<int>? OnPlayerLeft;

	int IGameAPI.DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		if (EcsWorld?.TryGet<ScriptZonesState>(_worldEntity, out var zonesState) == true)
		{
			int handle = zonesState.Zones.Count;
			float cx = (minX + maxX) * 0.5f;
			float cz = (minZ + maxZ) * 0.5f;
			zonesState.Zones.Add(new ZoneBounds
			{
				MinX = minX,
				MinZ = minZ,
				MaxX = maxX,
				MaxZ = maxZ,
				Center = new System.Numerics.Vector3(cx, 0f, cz)
			});
			return handle;
		}
		return -1;
	}

	System.Numerics.Vector3 IGameAPI.GetZoneCenter(int zoneHandle)
	{
		if (EcsWorld?.TryGet<ScriptZonesState>(_worldEntity, out var zonesState) == true
			&& zoneHandle >= 0 && zoneHandle < zonesState.Zones.Count)
		{
			return zonesState.Zones[zoneHandle].Center;
		}
		return System.Numerics.Vector3.Zero;
	}

	private void TickZoneTriggers()
	{
		if (OnUnitEnterZone == null) return;
		if (EcsWorld == null || !EcsWorld.IsAlive(_worldEntity) || !EcsWorld.Has<ScriptZonesState>(_worldEntity)) return;

		var zones = EcsWorld.Get<ScriptZonesState>(_worldEntity).Zones;
		if (zones.Count == 0) return;

		var positionQuery = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		EcsWorld.Query(in positionQuery, (Entity entity, ref Position posComp) =>
		{
			var pos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);

			if (!EcsWorld.Has<OccupiedZones>(entity))
			{
				EcsWorld.Add(entity, new OccupiedZones(new HashSet<int>()));
			}
			var occupiedZones = EcsWorld.Get<OccupiedZones>(entity).ZoneIds;

			for (int i = 0; i < zones.Count; i++)
			{
				ref ZoneBounds z = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(zones)[i];
				bool inside = pos.X >= z.MinX && pos.X <= z.MaxX && pos.Z >= z.MinZ && pos.Z <= z.MaxZ;

				if (inside && !occupiedZones.Contains(i))
				{
					occupiedZones.Add(i);
					if (TryGetUnit3D(entity, out var unit3D))
					{
						var wrapper = GetUnitWrapper(entity);
						OnUnitEnterZone?.Invoke(wrapper, i);
					}
				}
				else if (!inside)
				{
					occupiedZones.Remove(i);
				}
			}
		});
	}

	void IGameAPI.SetUnitRouteState(IUnit unit, int state)
	{
		unit.SetCustomData("__routeState", state);
	}

	int IGameAPI.GetUnitRouteState(IUnit unit)
	{
		object? data = unit.GetCustomData("__routeState");
		return data is int i ? i : 0;
	}

	void IGameAPI.SetUnitLevel(IUnit unit, int level)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Meta.Level>(wrapper.Entity))
			{
				EcsWorld.Set(wrapper.Entity, new Realm.Ecs.Components.Meta.Level(level));
			}
			else
			{
				EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Meta.Level(level));
			}
		}
	}

	int IGameAPI.GetPlayerKills(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].KillCount;
		}
		return 0;
	}

	void IGameAPI.SetPlayerKills(int playerIndex, int kills)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].KillCount = kills;
		}
	}

	void IGameAPI.IssueMoveOrder(IUnit unit, System.Numerics.Vector3 destination)
	{
		unit.MoveTo(destination);
	}

	void IGameAPI.SetUnitSpellImmune(IUnit unit, bool immune)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (immune)
			{
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity))
					EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Tags.SpellImmune());
			}
			else
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity))
					EcsWorld.Remove<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity);
			}
		}
	}

	void IGameAPI.SelectUnit(IUnit unit)
	{
		Callable.From(() =>
		{
			if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
			{
				if (GameHost.TryGetUnit3D(wrapper.Entity, out var u3d))
				{
					if (GodotObject.IsInstanceValid(u3d))
					{
						ClearSelection();
						SelectUnit(u3d);
						InGameHUD.Instance?.RefreshUI(SelectedUnits);
					}
				}
			}
		}).CallDeferred();
	}

	void IGameAPI.ClearSelection()
	{
		Callable.From(() =>
		{
			ClearSelection();
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}).CallDeferred();
	}


	public void NotifyPlayerLeft(int playerIndex)
	{
		OnPlayerLeft?.Invoke(playerIndex);
	}

	private void TickScheduledTimers(float delta)
	{
		if (_scheduledTimers.Count == 0) return;


		Span<int> keysBuffer = stackalloc int[Math.Min(_scheduledTimers.Count, 64)];
		int keyCount = 0;
		foreach (int k in _scheduledTimers.Keys)
		{
			if (keyCount < keysBuffer.Length)
				keysBuffer[keyCount++] = k;
		}

		List<int>? toRemove = null;
		for (int i = 0; i < keyCount; i++)
		{
			int handle = keysBuffer[i];
			if (!_scheduledTimers.TryGetValue(handle, out var entry)) continue;

			float remaining = entry.Remaining - delta;
			if (remaining <= 0f)
			{
				entry.Callback.Invoke();

				if (entry.Repeating)
				{
					_scheduledTimers[handle] = (entry.Interval, entry.Interval + remaining, true, entry.Callback);
				}
				else
				{
					toRemove ??= new List<int>();
					toRemove.Add(handle);
				}
			}
			else
			{
				_scheduledTimers[handle] = (entry.Interval, remaining, entry.Repeating, entry.Callback);
			}
		}

		if (toRemove != null)
		{
			foreach (int h in toRemove)
				_scheduledTimers.Remove(h);
		}
	}




	private static readonly Dictionary<string, UnitMetadata> DefaultRegistryFallback = new()
	{
		{
			"worker", new UnitMetadata {
				UnitId = "worker",
				Name = "Worker",
				Description = "Dedicated worker. Can gather resources from Goldmines, Trees, and Rocks, and construct buildings.",
				MaxHp = 70f,
				Damage = 5f,
				Range = 1.8f,
				Armor = 0f,
				Speed = 7.0f,
				AttackCooldown = 1.5f,
				ScanRadius = 10.0f,
				CostGold = 75f,
				CostWood = 0f,
				CostStone = 0f,
				ProductionTime = 4.0f,
				PopCost = 1,
				AttackType = "melee",
				ArmorType = "light",
				GoldBounty = 15f,
				BuildOptions = new[] { "castle", "tower" },
				PathingCapabilities = new[] { "ground" }
			}
		},
		{
			"soldier", new UnitMetadata {
				UnitId = "soldier",
				Name = "Soldier",
				Description = "Heavy armored infantry. Slow but tanky front-line fighter.",
				MaxHp = 150f,
				Damage = 15f,
				Range = 2.0f,
				Armor = 5f,
				Speed = 6.0f,
				AttackCooldown = 1.5f,
				ScanRadius = 14.0f,
				CostGold = 100f,
				CostWood = 0f,
				CostStone = 0f,
				ProductionTime = 5.0f,
				PopCost = 1,
				AttackType = "melee",
				ArmorType = "heavy",
				GoldBounty = 20f,
				PathingCapabilities = new[] { "ground" }
			}
		},
		{
			"archer", new UnitMetadata {
				UnitId = "archer",
				Name = "Elf Archer",
				Description = "Nimble elven ranged unit. High range and speed but fragile.",
				MaxHp = 90f,
				Damage = 12f,
				Range = 18.0f,
				Armor = 2f,
				Speed = 8.0f,
				AttackCooldown = 1.2f,
				ScanRadius = 20.0f,
				CostGold = 120f,
				CostWood = 40f,
				CostStone = 0f,
				ProductionTime = 7.0f,
				PopCost = 1,
				AttackType = "ranged",
				ArmorType = "light",
				GoldBounty = 25f,
				PathingCapabilities = new[] { "ground" }
			}
		},
		{
			"priest", new UnitMetadata {
				UnitId = "priest",
				Name = "Cleric Priest",
				Description = "Holy support unit. Automatically heals nearby damaged friendly units.",
				MaxHp = 80f,
				Damage = 25f,
				Range = 12.0f,
				Armor = 1f,
				Speed = 7.0f,
				AttackCooldown = 2.0f,
				ScanRadius = 15.0f,
				CostGold = 140f,
				CostWood = 20f,
				CostStone = 0f,
				ProductionTime = 8.0f,
				PopCost = 1,
				AttackType = "ranged",
				ArmorType = "light",
				GoldBounty = 30f,
				PathingCapabilities = new[] { "ground" }
			}
		},
		{
			"castle", new UnitMetadata {
				UnitId = "castle",
				Name = "Town Castle",
				Description = "Your fortress and command center. Produces units and upgrades. Guard it well!",
				MaxHp = 1000f,
				Damage = 0f,
				Range = 0f,
				Armor = 15f,
				Speed = 0f,
				AttackCooldown = 0f,
				ScanRadius = 0f,
				CostGold = 400f,
				CostWood = 300f,
				CostStone = 200f,
				ProductionTime = 15.0f,
				PopCost = 0,
				AttackType = "none",
				ArmorType = "building",
				GoldBounty = 0f,
				PathingCapabilities = new[] { "ground" }
			}
		},
		{
			"tower", new UnitMetadata {
				UnitId = "tower",
				Name = "Spell Tower",
				Description = "Defensive structure that auto-attacks nearby enemies. Upgradeable for +HP/+DMG.",
				MaxHp = 500f,
				Damage = 25f,
				Range = 25.0f,
				Armor = 8f,
				Speed = 0f,
				AttackCooldown = 2.0f,
				ScanRadius = 25.0f,
				CostGold = 200f,
				CostWood = 150f,
				CostStone = 100f,
				ProductionTime = 10.0f,
				PopCost = 0,
				AttackType = "ranged",
				ArmorType = "building",
				GoldBounty = 0f
			}
		},
		{
			"turtle", new UnitMetadata {
				UnitId = "turtle",
				Name = "Amphibious Turtle",
				Description = "Slow but sturdy amphibious beast that can travel on both ground and shallow water.",
				MaxHp = 120f,
				Damage = 10f,
				Range = 1.5f,
				Armor = 4f,
				Speed = 5.0f,
				AttackCooldown = 1.8f,
				ScanRadius = 12.0f,
				CostGold = 90f,
				CostWood = 10f,
				CostStone = 0f,
				ProductionTime = 5.0f,
				PopCost = 1,
				AttackType = "melee",
				ArmorType = "heavy",
				GoldBounty = 18f,
				PathingCapabilities = new[] { "ground", "shallow_water" }
			}
		}
	};

	private void LoadUnitMetadata(string mapName)
	{
		ActiveMapName = mapName;
		string path = $"res://Maps/{mapName}/metadata.json";
		string globalPath = ProjectSettings.GlobalizePath(path);
		string jsonText = "";

		if (FileAccess.FileExists(path))
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			jsonText = file.GetAsText();
		}
		else if (System.IO.File.Exists(globalPath))
		{
			jsonText = System.IO.File.ReadAllText(globalPath);
		}

		if (!string.IsNullOrEmpty(jsonText))
		{
			try
			{
				var options = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					IncludeFields = true
				};
				var loadedRegistry = JsonSerializer.Deserialize<Dictionary<string, UnitMetadata>>(jsonText, options);
				if (loadedRegistry != null)
				{
					UnitRegistry.Clear();
					foreach (var kvp in loadedRegistry)
					{
						UnitRegistry[kvp.Key] = kvp.Value;
					}
					return;
				}
			}
			catch { }
		}

		UnitRegistry.Clear();
		foreach (var kvp in DefaultRegistryFallback)
		{
			UnitRegistry[kvp.Key] = kvp.Value;
		}
	}

	private void LoadMapScript(string mapName)
	{
		_activeMapScript = null;
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type?[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch (System.Reflection.ReflectionTypeLoadException ex)
			{
				types = ex.Types;
			}
			catch (Exception)
			{
				continue;
			}
			if (types == null) continue;
			foreach (var type in types)
			{
				if (type == null) continue;
				if (typeof(IMapScript).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
				{
					string typeName = type.Name.ToLower();
					string searchName = mapName.Replace("_", "").ToLower();
					if (typeName.Contains(searchName) || searchName.Contains(typeName))
					{
						try
						{
							_activeMapScript = (IMapScript)Activator.CreateInstance(type);
							break;
						}
						catch { }
					}
				}
			}
			if (_activeMapScript != null) break;
		}

		if (_activeMapScript == null)
		{
			_activeMapScript = new Realm.Maps.MeleeMap();
		}
	}

	private List<Unit3D>[] _controlGroups = new List<Unit3D>[10];
	public List<Unit3D>[] ControlGroups => _controlGroups;
	private double[] _lastGroupPressTime = new double[10];


	private bool _isDragging = false;
	private Vector2 _dragStart = Vector2.Zero;
	private Vector2 _dragEnd = Vector2.Zero;
	private const float DragThreshold = 8f;

	public override void _Ready()
	{
		MainNode = GetTree().Root.GetNodeOrNull("Main");
		MainCamera = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");

		GD.Print($"[GAMEHOST_READY] GameHost _Ready starting");
		Instance = this;
		GameSettings.ApplyGraphicsSettings(this);
		ReinitializeEcsAndServices();
	}

	private void InitializeServices()
	{
		// Services are initialized and resolved in DI, no-op or just configuration
	}

	private void InitializeGameEcs()
	{
		if (System.OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.StartInstallIfNeeded();
		}

		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			_trackerTickDurations = new List<float>(100000);
			_trackerApiDurations = new List<float>(10000);
			_trackerIntervalStopwatch.Start();
		}

		EcsWorld = ServiceLocator.Get<World>();
		SetupWorldEntityComponents();

		CreateGround();
		if (GroundTerrain != null)
			_editorService.SetTerrainColors(GroundTerrain.Colors);
		SetupSkybox();
		UpdateDayNightVisuals(0.5f);
		_definitionManager = ServiceLocator.Get<DefinitionManager>();
		_goldResourceId = "gold".AsResourceId(_definitionManager);
		_woodResourceId = "wood".AsResourceId(_definitionManager);
		_stoneResourceId = "stone".AsResourceId(_definitionManager);
		_simulationService = ServiceLocator.Get<SimulationService>();
		_simulationService.SetRuntimeReferences(AllUnits, AllProps, _castlesList, _definitionManager, _goldResourceId, _woodResourceId, _stoneResourceId, GroundTerrain);
		_simulationService.Initialize();

		_simulationService.OnArrowProjectileRequested = (start, target) => SpawnArrowProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnDamageFlashRequested = entity =>
		{
			if (GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(FlashDamageUnit), unit3D);
			}
		};
		_simulationService.OnHealEffectRequested = (start, target) => SpawnHealVisualEffect(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnHealFlashRequested = entity =>
		{
			if (GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(FlashHealUnit), unit3D);
			}
		};
		_simulationService.OnKillUnitRequested = entity =>
		{
			if (EcsWorld.IsAlive(entity) && GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(KillUnit), unit3D);
			}
		};
		_simulationService.OnPropDepleted = entity =>
		{
			if (TryGetProp3D(entity, out var prop3D))
			{
				this.CallDeferred(nameof(DepleteProp), prop3D);
			}
		};
		_simulationService.OnUnitDamagedCallback = (targetEntity, attackerEntity, damage) =>
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				IUnit attackerWrapper = EcsWorld.IsAlive(attackerEntity)
					? GetUnitWrapper(attackerEntity)
					: null;
				OnUnitDamaged?.Invoke(GetUnitWrapper(targetEntity), attackerWrapper, damage);
			}
		};
		_simulationService.OnUnderAttackAlertRequested = unitId =>
		{
			string alertMsg = unitId == "castle"
				? "⚠️ YOUR CASTLE IS UNDER ATTACK!"
				: $"⚠️ {unitId.ToUpper()} is under attack!";
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), alertMsg, new Color(1.0f, 0.2f, 0.1f));
			UIManager.Instance?.CallDeferred(nameof(UIManager.PlayWarningSound));
		};
		_simulationService.OnSpawnUnitFromProductionRequested = (unitId, position, isEnemy, rallyPoint, isFromQueue) =>
			SpawnUnitFromProduction(unitId, position, isEnemy, rallyPoint, isFromQueue);
		_simulationService.GetProductionBuildTime = unitId =>
			UnitRegistry.TryGetValue(unitId, out var meta) ? meta.ProductionTime : 5f;
		_simulationService.OnClearUnitOrdersRequested = entity => ClearUnitOrders(entity);
		_simulationService.OnStopGatheringMovementRequested = entity => StopGatheringMovement(entity);
		_simulationService.OnUiRefreshRequested = () => InGameHUD.Instance?.RefreshUI(SelectedUnits);
		_simulationService.OnResourceDepositedForPlayer = (resType, carry) =>
		{
			string resTypeUpper = resType.ToUpper();
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f));
		};
		_simulationService.OnProductionCompleted = unitToSpawn =>
		{
			string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f));
		};

		if (_multiplayerActive)
		{
			_localPeerId = Multiplayer.GetUniqueId();
			if (!Multiplayer.IsServer())
			{
				_networkService.MarkClientEnteredMultiplayer();
			}
			if (Multiplayer is SceneMultiplayer sceneMultiplayer)
			{
				sceneMultiplayer.ServerRelay = false;
			}

			if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
			{
				int pIdx = 0;
				foreach (var p in LobbyManager.Instance.PlayerList)
				{
					var playerEntity = EcsWorld.Create();
					EcsWorld.Add(playerEntity, new Player());
					EcsWorld.Add(playerEntity, new Name(p.Name));
					InitializePlayerResources(playerEntity);
					SetupPlayerEntityComponents(playerEntity);
					_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
					if (p.PeerId == _localPeerId)
					{
						_playerEntity = playerEntity;
					}
					else
					{
						_enemyPlayerEntity = playerEntity;
					}
					if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
					{
						var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
						if (pIdx < players.Length)
						{
							players[pIdx].Name = p.Name;
							players[pIdx].Active = true;
						}
					}
					pIdx++;
				}
			}
			else
			{
				_playerEntity = EcsWorld.Create();
				EcsWorld.Add(_playerEntity, new Player());
				EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
				InitializePlayerResources(_playerEntity);
				SetupPlayerEntityComponents(_playerEntity);
				_peerIdToPlayerEntityMap[1] = _playerEntity;

				_enemyPlayerEntity = EcsWorld.Create();
				EcsWorld.Add(_enemyPlayerEntity, new Player());
				EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
				InitializePlayerResources(_enemyPlayerEntity);
				SetupPlayerEntityComponents(_enemyPlayerEntity);
				_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;

				if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
				{
					var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
					players[0].Name = "Horaid_Topa";
					players[0].Active = true;
					players[1].Name = "Enemy_AI";
					players[1].Active = true;
				}
			}
		}
		else
		{
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
			InitializePlayerResources(_playerEntity);
			SetupPlayerEntityComponents(_playerEntity);
			_peerIdToPlayerEntityMap[1] = _playerEntity;

			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
			InitializePlayerResources(_enemyPlayerEntity);
			SetupPlayerEntityComponents(_enemyPlayerEntity);
			_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;

			if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
			{
				var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
				players[0].Name = "Horaid_Topa";
				players[0].Active = true;
				players[1].Name = "Enemy_AI";
				players[1].Active = true;
			}
		}


		string rawMapName = "melee";
		if (LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.ActiveMapName))
		{
			rawMapName = LobbyManager.Instance.ActiveMapName;
		}

		string normalizedMapName = rawMapName.ToLower().Trim();
		if (!DirAccess.DirExistsAbsolute($"res://Maps/{normalizedMapName}"))
		{
			if (normalizedMapName.Contains("legion"))
			{
				normalizedMapName = "legion_td";
			}
			else if (normalizedMapName.Contains("defense") || normalizedMapName.Contains("td"))
			{
				normalizedMapName = "green_td";
			}
			else
			{
				normalizedMapName = "melee";
			}
		}

		LoadUnitMetadata(normalizedMapName);

		bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
		if ((isGameStarted && !ReplayPlaybackManager.Instance.IsPlayingReplay) || IsMapEditorMode)
		{
			LoadMapScript(normalizedMapName);
			if (_activeMapScript != null)
			{
				_activeMapScript.Initialize(this);
			}
		}

		if (isGameStarted && !IsMapEditorMode && !ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays)
		{
			string replayDir = ProjectSettings.GlobalizePath("user://replays");
			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string replayPath = System.IO.Path.Combine(replayDir, $"replay_{timestamp}.rep");
			_replayService.StartRecording(
				replayPath, 
				normalizedMapName, 
				LobbyManager.Instance?.PlayerList
			);
			GD.Print($"[ReplayRecorder] Started recording to {replayPath}");
		}


		var timer = GetTree().CreateTimer(0.1f);
		timer.Timeout += () =>
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold = _goldBackup;
				InGameHUD.Instance.Wood = _woodBackup;
				InGameHUD.Instance.Stone = _stoneBackup;
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}

			if (ReplayPlaybackManager.Instance.IsPlayingReplay)
			{
				ReplayPlaybackManager.Instance.ApplyInitialFrame();
			}
		};

		_fogOfWarService.Initialize(MainNode);
	}

	private void ReinitializeEcsAndServices()
	{
		EcsWorld?.Dispose();
		
		BuildDependencyInjection();
		ResolveServices();
		if (GroundTerrain != null)
		{
			_editorService.SetTerrainColors(GroundTerrain.Colors);
		}

		EcsWorld = ServiceLocator.Get<World>();
		SetupWorldEntityComponents();

		_definitionManager = ServiceLocator.Get<DefinitionManager>();
		_goldResourceId = "gold".AsResourceId(_definitionManager);
		_woodResourceId = "wood".AsResourceId(_definitionManager);
		_stoneResourceId = "stone".AsResourceId(_definitionManager);
		_simulationService = ServiceLocator.Get<SimulationService>();
		_simulationService.SetRuntimeReferences(AllUnits, AllProps, _castlesList, _definitionManager, _goldResourceId, _woodResourceId, _stoneResourceId, GroundTerrain);
		_simulationService.Initialize();

		_simulationService.OnArrowProjectileRequested = (start, target) => SpawnArrowProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnDamageFlashRequested = entity =>
		{
			if (GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(FlashDamageUnit), unit3D);
			}
		};
		_simulationService.OnHealEffectRequested = (start, target) => SpawnHealVisualEffect(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnHealFlashRequested = entity =>
		{
			if (GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(FlashHealUnit), unit3D);
			}
		};
		_simulationService.OnKillUnitRequested = entity =>
		{
			if (EcsWorld.IsAlive(entity) && GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(KillUnit), unit3D);
			}
		};
		_simulationService.OnPropDepleted = entity =>
		{
			if (TryGetProp3D(entity, out var prop3D))
			{
				this.CallDeferred(nameof(DepleteProp), prop3D);
			}
		};
		_simulationService.OnUnitDamagedCallback = (targetEntity, attackerEntity, damage) =>
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				IUnit attackerWrapper = EcsWorld.IsAlive(attackerEntity)
					? GetUnitWrapper(attackerEntity)
					: null;
				OnUnitDamaged?.Invoke(GetUnitWrapper(targetEntity), attackerWrapper, damage);
			}
		};
		_simulationService.OnUnderAttackAlertRequested = unitId =>
		{
			string alertMsg = unitId == "castle"
				? "⚠️ YOUR CASTLE IS UNDER ATTACK!"
				: $"⚠️ {unitId.ToUpper()} is under attack!";
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), alertMsg, new Color(1.0f, 0.2f, 0.1f));
			UIManager.Instance?.CallDeferred(nameof(UIManager.PlayWarningSound));
		};
		_simulationService.OnSpawnUnitFromProductionRequested = (unitId, position, isEnemy, rallyPoint, isFromQueue) =>
			SpawnUnitFromProduction(unitId, position, isEnemy, rallyPoint, isFromQueue);
		_simulationService.GetProductionBuildTime = unitId =>
			UnitRegistry.TryGetValue(unitId, out var meta) ? meta.ProductionTime : 5f;
		_simulationService.OnClearUnitOrdersRequested = entity => ClearUnitOrders(entity);
		_simulationService.OnStopGatheringMovementRequested = entity => StopGatheringMovement(entity);
		_simulationService.OnUiRefreshRequested = () => InGameHUD.Instance?.RefreshUI(SelectedUnits);
		_simulationService.OnResourceDepositedForPlayer = (resType, carry) =>
		{
			string resTypeUpper = resType.ToUpper();
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f));
		};
		_simulationService.OnProductionCompleted = unitToSpawn =>
		{
			string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f));
		};
	}

	public void StopRecording()
	{
		_replayService?.StopRecording();
	}

	public override void _ExitTree()
	{
		if ((Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()) && _trackerTickDurations != null && _trackerTickDurations.Count >= 30)
		{
			var summary = new GameStabilitySummary
			{
				AvgTickMs = HostStabilityTracker.CalculateAverage(_trackerTickDurations),
				MedianTickMs = HostStabilityTracker.CalculateMedian(_trackerTickDurations),
				MaxTickMs = HostStabilityTracker.CalculateMax(_trackerTickDurations),
				AvgApiMs = HostStabilityTracker.CalculateAverage(_trackerApiDurations),
				MedianApiMs = HostStabilityTracker.CalculateMedian(_trackerApiDurations),
				MaxApiMs = HostStabilityTracker.CalculateMax(_trackerApiDurations)
			};
			HostStabilityTracker.AddGameSummary(summary);
		}

		if (Instance == this) Instance = null;
		EcsWorld?.Dispose();
		if (OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.CleanUp();
		}
		_networkService?.Clear();
		_fogOfWarService?.CleanUp();
		StopRecording();
	}

	private void CreateGround()
	{
		if (IsMapEditorMode)
		{
			var terrainNode = new EditableTerrain();
			terrainNode.Name = "Ground";
			AddChild(terrainNode);
			GroundTerrain = terrainNode;
			return;
		}

		string rawMapName = "melee";
		if (LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.ActiveMapName))
		{
			rawMapName = LobbyManager.Instance.ActiveMapName;
		}

		string normalizedMapName = rawMapName.ToLower().Trim();
		if (!DirAccess.DirExistsAbsolute($"res://Maps/{normalizedMapName}"))
		{
			if (normalizedMapName.Contains("legion"))
			{
				normalizedMapName = "legion_td";
			}
			else if (normalizedMapName.Contains("defense") || normalizedMapName.Contains("td"))
			{
				normalizedMapName = "green_td";
			}
			else
			{
				normalizedMapName = "melee";
			}
		}

		string terrainPath = $"res://Maps/{normalizedMapName}/terrain.json";
		if (FileAccess.FileExists(terrainPath))
		{
			if (LoadMapFromFile(terrainPath, true))
			{
				return;
			}
		}

		var staticBody = new StaticBody3D();
		staticBody.Name = "Ground";
		AddChild(staticBody);

		var meshInstance = new MeshInstance3D();
		var planeMesh = new PlaneMesh();
		planeMesh.Size = new Vector2(250, 250);
		meshInstance.Mesh = planeMesh;
		staticBody.AddChild(meshInstance);

		var material = new StandardMaterial3D();
		var texture = GD.Load<Texture2D>("res://Assets/terrain_grass.jpg");
		if (texture != null)
		{
			material.AlbedoTexture = texture;
			material.Uv1Scale = new Vector3(25, 25, 1);
		}
		else
		{
			material.AlbedoColor = new Color(0.15f, 0.45f, 0.15f); // backup green
		}
		material.Roughness = 0.9f;
		meshInstance.MaterialOverride = material;

		var collisionShape = new CollisionShape3D();
		var boxShape = new BoxShape3D();
		boxShape.Size = new Vector3(250, 0.1f, 250);
		collisionShape.Shape = boxShape;
		collisionShape.Position = new Vector3(0, -0.05f, 0);
		staticBody.AddChild(collisionShape);
	}

	public void SetSkyboxTexture(string path)
	{
		var worldEnv = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		if (worldEnv != null)
		{
			var env = worldEnv.Environment;
			if (env != null)
			{
				var panoramaMaterial = new PanoramaSkyMaterial();
				var skyTexture = GD.Load<Texture2D>(path);
				if (skyTexture != null)
				{
					panoramaMaterial.Panorama = skyTexture;
					var sky = new Sky();
					sky.SkyMaterial = panoramaMaterial;
					env.Sky = sky;
					env.BackgroundMode = Godot.Environment.BGMode.Sky;
					EditorSkyboxPath = path;
				}
			}
		}
	}

	private void SetupSkybox()
	{
		SetSkyboxTexture(EditorSkyboxPath);
	}

	private void SpawnInitialEntities()
	{

		var playerOwner = _playerEntity.AsPlayerEntity(EcsWorld);
		var enemyOwner = _enemyPlayerEntity.AsPlayerEntity(EcsWorld);

		SpawnDefaultResourceNodes();



		var workerEntity = CreateEcsUnit("worker", "Worker", 70f, 5f, 1.8f, 0f, 7.0f, new Vector3(-16, 0, -20), playerOwner);
		SpawnUnit3D(workerEntity, "worker", GetFallbackModelPath("worker", false), new Vector3(-16, 0, -20), false, false);


		var soldierEntity = CreateEcsUnit("soldier", "Soldier", 150f, 15f, 2.0f, 5f, 6.0f, new Vector3(-8, 0, 5), playerOwner);
		var soldier3D = SpawnUnit3D(soldierEntity, "soldier", GetFallbackModelPath("soldier", false), new Vector3(-8, 0, 5), false, false);
		

		var archerEntity = CreateEcsUnit("archer", "Elf Archer", 90f, 12f, 18.0f, 2f, 8.0f, new Vector3(-12, 0, 5), playerOwner);
		var archer3D = SpawnUnit3D(archerEntity, "archer", GetFallbackModelPath("archer", false), new Vector3(-12, 0, 5), false, false);


		var castleEntity = CreateEcsUnit("castle", "Town Castle", 1000f, 0f, 0f, 15f, 0f, new Vector3(-25, 0, -25), playerOwner);
		var castle3D = SpawnUnit3D(castleEntity, "castle", GetFallbackModelPath("castle", true), new Vector3(-25, 0, -25), true, false);


		var towerEntity = CreateEcsUnit("tower", "Spell Tower", 500f, 25f, 25.0f, 8f, 0f, new Vector3(-15, 0, -15), playerOwner);
		var tower3D = SpawnUnit3D(towerEntity, "tower", GetFallbackModelPath("tower", true), new Vector3(-15, 0, -15), true, false);



		var enemyWorkerEntity = CreateEcsUnit("worker", "Orc Worker", 70f, 5f, 1.8f, 0f, 7.0f, new Vector3(16, 0, 20), enemyOwner);
		SpawnUnit3D(enemyWorkerEntity, "worker", GetFallbackModelPath("worker", false), new Vector3(16, 0, 20), false, true);


		var enemyGoldmine = FindNearbyResourceNode(new Vector3(16, 0, 20), "gold", 50f);
		if (enemyGoldmine != null)
		{
			var gatherer = new Gatherer("gold", enemyGoldmine.Entity);
			EcsWorld.Add(enemyWorkerEntity, gatherer);
		}


		var enemySoldierEntity = CreateEcsUnit("soldier", "Orc Raider", 150f, 15f, 2.0f, 5f, 6.0f, new Vector3(15, 0, 10), enemyOwner);
		var enemySoldier3D = SpawnUnit3D(enemySoldierEntity, "soldier", GetFallbackModelPath("soldier", false), new Vector3(15, 0, 10), false, true);


		var enemyArcherEntity = CreateEcsUnit("archer", "Dark Archer", 90f, 12f, 18.0f, 2f, 8.0f, new Vector3(20, 0, 15), enemyOwner);
		var enemyArcher3D = SpawnUnit3D(enemyArcherEntity, "archer", GetFallbackModelPath("archer", false), new Vector3(20, 0, 15), false, true);


		var enemyTowerEntity = CreateEcsUnit("tower", "Orc Totem Tower", 500f, 25f, 25.0f, 8f, 0f, new Vector3(25, 0, 5), enemyOwner);
		var enemyTower3D = SpawnUnit3D(enemyTowerEntity, "tower", GetFallbackModelPath("tower", true), new Vector3(25, 0, 5), true, true);


		var enemyCastleEntity = CreateEcsUnit("castle", "Orc Stronghold", 1000f, 0f, 0f, 15f, 0f, new Vector3(25, 0, 25), enemyOwner);
		var enemyCastle3D = SpawnUnit3D(enemyCastleEntity, "castle", GetFallbackModelPath("castle", true), new Vector3(25, 0, 25), true, true);
	}

	private void SpawnDefaultResourceNodes()
	{

		SpawnPropExternal("goldmine", new Vector3(-35f, 0f, -15f));
		SpawnPropExternal("tree", new Vector3(-18f, 0f, -35f));
		SpawnPropExternal("tree", new Vector3(-22f, 0f, -36f));
		SpawnPropExternal("tree", new Vector3(-26f, 0f, -34f));
		SpawnPropExternal("rock", new Vector3(-36f, 0f, -32f));
		SpawnPropExternal("rock", new Vector3(-32f, 0f, -35f));


		SpawnPropExternal("goldmine", new Vector3(35f, 0f, 15f));
		SpawnPropExternal("tree", new Vector3(18f, 0f, 35f));
		SpawnPropExternal("tree", new Vector3(22f, 0f, 36f));
		SpawnPropExternal("tree", new Vector3(26f, 0f, 34f));
		SpawnPropExternal("rock", new Vector3(36f, 0f, 32f));
		SpawnPropExternal("rock", new Vector3(32f, 0f, 35f));


		SpawnPropExternal("goldmine", new Vector3(0f, 0f, 0f));
		SpawnPropExternal("tree", new Vector3(-10f, 0f, 10f));
		SpawnPropExternal("tree", new Vector3(-12f, 0f, 12f));
		SpawnPropExternal("tree", new Vector3(10f, 0f, -10f));
		SpawnPropExternal("tree", new Vector3(12f, 0f, -12f));
		SpawnPropExternal("rock", new Vector3(15f, 0f, 15f));
		SpawnPropExternal("rock", new Vector3(-15f, 0f, -15f));
	}

	private Prop3D FindProp3DInParentChain(Node node)
	{
		while (node != null)
		{
			if (node is Prop3D prop)
			{
				return prop;
			}
			node = node.GetParent();
		}
		return null;
	}

	public void ClearUnitOrders(Entity entity)
	{
		if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		if (EcsWorld.Has<PathFollow>(entity)) EcsWorld.Remove<PathFollow>(entity);
		if (EcsWorld.Has<AttackTarget>(entity)) EcsWorld.Remove<AttackTarget>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.AttackMove>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.Follow>(entity);
		if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Remove<Patrol>(entity);
		if (EcsWorld.Has<HealingTarget>(entity)) EcsWorld.Remove<HealingTarget>(entity);
		if (EcsWorld.Has<WaypointQueue>(entity)) EcsWorld.Remove<WaypointQueue>(entity);
		if (EcsWorld.Has<Gatherer>(entity)) EcsWorld.Remove<Gatherer>(entity);
	}

	private void StopGatheringMovement(Entity entity)
	{
		if (EcsWorld.IsAlive(entity))
		{
			if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		}
	}

	private Prop3D FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Prop3D closest = null;
		float closestDist = radius;
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				string pType = prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};
				
				if (pType == type)
				{
					float d = pos.DistanceTo(prop.GlobalPosition);
					if (d < closestDist)
					{
						closestDist = d;
						closest = prop;
					}
				}
			}
		}
		return closest;
	}

	public void IssueGatherCommand(Prop3D prop)
	{
		if (SelectedUnits.Count == 0 || prop == null || !GodotObject.IsInstanceValid(prop)) return;

		string resType = prop.PropId switch
		{
			"goldmine" => "gold",
			"tree" => "wood",
			"rock" => "stone",
			_ => null
		};

		if (resType == null) return;

		SpawnTargetIndicator(prop.GlobalPosition, new Color(0.9f, 0.8f, 0.1f));
		InGameHUD.Instance?.ShowFeedbackText($"Gathering {resType.ToUpper()} from {prop.PropId}", new Color(0.9f, 0.8f, 0.1f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				var gatherer = new Gatherer(resType, prop.Entity);
				if (EcsWorld.Has<Gatherer>(unit.Entity)) EcsWorld.Set(unit.Entity, gatherer);
				else EcsWorld.Add(unit.Entity, gatherer);
			}
			QueueClientCommand("gather", targetIds, prop.GlobalPosition, 0, "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;


			ClearUnitOrders(unit.Entity);


			var gatherer = new Gatherer(resType, prop.Entity);
			if (EcsWorld.Has<Gatherer>(unit.Entity))
				EcsWorld.Set(unit.Entity, gatherer);
			else
				EcsWorld.Add(unit.Entity, gatherer);


			var moveTo = new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z));
			EcsWorld.Add(unit.Entity, moveTo);
		}
	}

	private Entity CreateEcsUnit(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner)
	{
		float scanRadius = 15.0f;
		float attackCooldown = 1.5f;
		bool isHero = false;
		int pathingFlags = 8;
		if (UnitRegistry.TryGetValue(id, out var regMeta))
		{
			if (regMeta.ScanRadius > 0) scanRadius = regMeta.ScanRadius;
			if (regMeta.AttackCooldown > 0) attackCooldown = regMeta.AttackCooldown;
			isHero = regMeta.IsHero;
			pathingFlags = GetUnitPathingFlags(regMeta);
		}

		var entity = _unitSpawnService.CreateEcsUnitEntity(
			id, name, hp, damage, range, armor, speed, scanRadius, isHero, attackCooldown, pathingFlags, pos, owner,
			_playerEntity, HasShieldsUpgrade, HasWeaponsUpgrade
		);

		OnUnitCreated?.Invoke(GetUnitWrapper(entity));
		return entity;
	}

	private Unit3D SpawnUnit3D(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, bool isEnemy, bool isFromQueue = false)
	{
		EcsWorld.Add(entity, new UnitFaction(isEnemy));

		var unit3D = new Unit3D();
		unit3D.Entity = entity;
		unit3D.Name = $"{id}_{entity.Id}";

		if (!isBuilding && !IsMapEditorMode)
		{
			unit3D.CollisionLayer = 0;
			unit3D.CollisionMask = 0;
		}

		AddChild(unit3D);
		unit3D.Position = pos;
		unit3D.LoadModel(modelPath);

		if (isBuilding)
		{
			var spawnOffset = new System.Numerics.Vector3(0f, 0f, 8f);
			EcsWorld.Add(entity, new BuildingSpawnOffset(spawnOffset));
		}

		if (IsMapEditorMode)
		{
			unit3D.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			unit3D.Scale *= EditorPlacementScale;
		}

		EntityToUnit3D[entity] = unit3D;

		if (!isEnemy && UnitRegistry.TryGetValue(id, out var popMeta))
		{
			if (id == "castle")
			{
				MaxPopulation += 20;
			}
			if (!isFromQueue)
			{
				CurrentPopulation += popMeta.PopCost;
			}
		}

		AllUnits.Add(unit3D);
		if (id == "castle")
		{
			_castlesList.Add(unit3D);
		}
		return unit3D;
	}

	public override void _PhysicsProcess(double delta)
	{
		bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
		if (!isGameStarted && !IsMapEditorMode)
		{
			return;
		}

		float fDelta = (float)delta;

		if (_wasClientInMultiplayer)
		{
			UpdateConnectionStatus();
		}

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			ReplayPlaybackManager.Instance.Update(fDelta);
			return;
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			UpdateClientTick(fDelta);
			return;
		}

		if (IsMapEditorMode)
		{
			ProcessMapEditorTick(fDelta);
			return;
		}

		ProcessGameplayTick(fDelta);
	}

	private void UpdateConnectionStatus()
	{
		_networkService.UpdateConnectionStatus(_multiplayerActive, Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer());
	}

	private void ProcessGameplayTick(float fDelta)
	{
		float actualIntervalMs = 0f;
		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			if (_trackerIntervalStopwatch.IsRunning)
			{
				actualIntervalMs = (float)_trackerIntervalStopwatch.Elapsed.TotalMilliseconds;
				_trackerIntervalStopwatch.Restart();
			}
			else
			{
				_trackerIntervalStopwatch.Start();
			}
			_trackerTickStopwatch.Restart();
		}

		_fDelta = fDelta;

		_simulationService.TickEcs(fDelta);
		UpdateVisualNodesFromEcs(fDelta);

		if (EcsWorld != null && EcsWorld.IsAlive(_worldEntity) && EcsWorld.Has<WorldState>(_worldEntity))
		{
			var state = EcsWorld.Get<WorldState>(_worldEntity);
			if (state.DayNightCycleEnabled && !IsMapEditorMode)
			{
				float progress = state.TimeOfDayTimer / TimeOfDayCycleDuration;
				UpdateDayNightVisuals(progress);
			}
		}

		UpdateMinimapPings(fDelta);

		if (_fogOfWarService != null)
		{
			bool isReplay = ReplayPlaybackManager.Instance.IsPlayingReplay;
			bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
			int spectatorPerspective = InGameHUD.Instance?.LiveSpectatorPerspective ?? -1;
			_fogOfWarService.Tick(fDelta, AllUnits, MainCamera, spectatorPerspective, isReplay, isSpectator);
		}

		TickScheduledTimers(fDelta);
		TickZoneTriggers();
		if (_activeMapScript != null)
		{
			_activeMapScript.Update(this, fDelta);
		}

		UpdateBuildingPreview();

		if (!ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays && _replayService != null && _replayService.IsRecording)
		{
			RecordGameplayTick();
		}

		if (_multiplayerActive && Multiplayer.IsServer())
		{
			UpdateServerSnapshotTick(fDelta);
		}

		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			_trackerTickStopwatch.Stop();
			float tickCpuMs = (float)_trackerTickStopwatch.Elapsed.TotalMilliseconds;
			float tickDelay = 0f;
			if (actualIntervalMs > 33.33f)
			{
				tickDelay = actualIntervalMs - 33.33f;
			}
			float adjustedTickMs = tickCpuMs + tickDelay;
			if (_trackerTickDurations != null)
			{
				_trackerTickDurations.Add(adjustedTickMs);
			}
			_trackerLastTickDelay = tickDelay;
		}
	}


	private void UpdateMinimapPings(float fDelta)
	{
		for (int i = ActivePings.Count - 1; i >= 0; i--)
		{
			var ping = ActivePings[i];
			ping.LifeTime += fDelta;
			if (ping.LifeTime >= ping.MaxLifeTime)
			{
				ActivePings.RemoveAt(i);
			}
			else
			{
				ActivePings[i] = ping;
			}
		}
	}

	private void UpdateDayNightVisuals(float progress)
	{
		_environmentService?.UpdateDayNightVisuals(this, progress);
	}

	public void CycleTimeOfDay()
	{
		_environmentService?.CycleTimeOfDay(this, _worldEntity, TimeOfDayCycleDuration);
	}

	public string GetTimeOfDayName()
	{
		return _environmentService?.GetTimeOfDayName(TimeOfDayIndex) ?? "Unknown";
	}

	private void SpawnFireblastEffect(Vector3 position)
	{
		_fxService.SpawnFireblastEffect(this, position);
	}

	private void SpawnLightningEffect(Vector3 position)
	{
		_fxService.SpawnLightningEffect(this, position);
	}

	private void SpawnSpritesheetEffect(string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		_fxService.SpawnSpritesheetEffect(this, texturePath, worldPosition, columns, rows, secondsPerFrame, sizeInWorldUnits);
	}

	private void FlashDamageUnit(Unit3D unit)
	{
		_fxService.FlashDamageUnit(unit);
	}

	private void SpawnHealVisualEffect(Vector3 start, Vector3 target)
	{
		_fxService.SpawnHealVisualEffect(this, start, target);
	}

	private void FlashHealUnit(Unit3D unit)
	{
		_fxService.FlashHealUnit(unit);
	}

	private void SpawnHolyLightEffect(Vector3 position)
	{
		_fxService.SpawnHolyLightEffect(this, position);
	}

	private void SpawnPing3DEffect(Vector3 position)
	{
		_fxService.SpawnPing3DEffect(this, position);
	}

	private void SpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		_fxService.SpawnArrowProjectile(this, start, target);
	}

	private void SpawnTargetIndicator(Vector3 position, Color color)
	{
		_fxService.SpawnTargetIndicator(this, position, color);
	}

	public void AddMinimapPing(Vector3 position)
	{
		_fxService.AddMinimapPing(this, ActivePings, position);
	}

	void IGameAPI.PlayWarningSound()
	{
		_audioService.PlayWarningSound();
	}

	void IGameAPI.PlayClickSound()
	{
		_audioService.PlayClickSound();
	}
}
