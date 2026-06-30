using Arch.Core;
using DotRecast.Core.Numerics;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Godot.ReplaySystem;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class GameHost : Node3D, IGameAPI
{
	public static GameHost Instance { get; private set; }
	internal readonly NavMeshPathfinder _pathfinder = new();
	public string ActiveMapName { get; private set; } = "melee";

	private readonly GameHostAudioService _audioService = new();
	private readonly GameHostDayNightService _dayNightService = new();
	private readonly GameHostFXService _fxService = new();

	private float _fDelta;
	private float _dynamicInterpolationFactor;


	private readonly List<string> _tickExpiredBuffs = new();
	private readonly List<string> _tickBuffKeys = new();
	internal readonly List<(Entity Entity, Patrol Patrol)> _tickPatrolToFlip = new();
	private readonly List<Entity> _tickFollowToStop = new();
	private readonly List<(Entity Follower, Vector3 TargetPos)> _tickFollowToMove = new();
	private readonly List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly List<Entity> _tickArrivedUnits = new();
	internal readonly List<(Entity Entity, PathFollow PathFollow)> _tickAddPathFollow = new();
	internal readonly List<(Entity Attacker, AttackTarget Target)> _tickNewAttackTargets = new();
	private readonly List<Entity> _tickActionsToRemoveTarget = new();
	private readonly List<(Entity Attacker, Vector3 TargetPos)> _tickActionsToChase = new();
	private readonly List<Entity> _tickActionsToStopChasing = new();
	private readonly List<(Entity Entity, Unit3D Unit)> _tickUnitsToKill = new();
	internal readonly List<(Entity Priest, HealingTarget Target)> _tickNewHealingTargets = new();
	private readonly List<Entity> _tickHealRemoveTargets = new();
	private readonly List<(Entity Priest, Vector3 TargetPos)> _tickHealChaseTargets = new();
	private readonly List<Entity> _tickHealStopChasing = new();
	private readonly List<Entity> _tickEditorArrivedUnits = new();

	private struct SpawningRequest
	{
		public string UnitId;
		public Vector3 Position;
		public bool IsEnemy;
		public Vector3? RallyPoint;
		public bool IsFromQueue;
	}

	private readonly List<Entity> _tickEntitiesToClearOrders = new();
	private readonly List<Entity> _tickEntitiesToStopGathering = new();
	private readonly List<SpawningRequest> _tickSpawningRequests = new();
	private bool _tickNeedsUiRefresh = false;




	private Entity _scanAttackerEntity;
	private Vector3 _scanAttackerPos;
	private PlayerEntity _scanAttackerOwner;
	private bool _scanIsAttackerEnemy;
	private float _scanClosestDist;
	private Entity _scanClosestEnemy;
	
	private Vector3 _scanPriestPos;
	private PlayerEntity _scanFriendlyOwner;
	private float _scanFriendlyClosestDist;
	private Entity _scanClosestDamagedFriendly;

	private readonly QueryDescription _enemyQuery = new QueryDescription().WithAll<Position, Owner>().WithNone<Dead>();
	private readonly QueryDescription _friendlyScanQuery = new QueryDescription().WithAll<Position, Health, Owner>().WithNone<Dead>();
	private readonly QueryDescription _passiveIncomeQuery = new QueryDescription().WithAll<PlayerResources>().WithNone<Dead>();
	private readonly QueryDescription _buffQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.Buffs>().WithNone<Dead>();
	private readonly QueryDescription _patrolArrivalQuery = new QueryDescription().WithAll<Patrol, Position>().WithNone<Dead, AttackTarget>();
	private readonly QueryDescription _followQuery = new QueryDescription().WithAll<Follow, Position>().WithNone<Dead>();
	private readonly QueryDescription _gatherQuery = new QueryDescription().WithAll<Position, Gatherer>().WithNone<Dead>();
	private readonly QueryDescription _movementQuery = new QueryDescription().WithAll<Position, MoveTo, MovementStats>().WithNone<Dead>();
	private readonly QueryDescription _attackCooldownQuery = new QueryDescription().WithAll<Attack>();
	private readonly QueryDescription _targetAcquisitionQuery = new QueryDescription().WithAll<Position, Attack, Owner>().WithNone<AttackTarget, Dead>();
	private readonly QueryDescription _combatQuery = new QueryDescription().WithAll<Position, Attack, AttackTarget, Owner>().WithNone<Dead>();
	private readonly QueryDescription _priestScanQuery = new QueryDescription().WithAll<Position, Owner, DefinitionId>().WithNone<Dead, HealingTarget>();
	private readonly QueryDescription _healingExecutionQuery = new QueryDescription().WithAll<Position, Attack, HealingTarget, Owner>().WithNone<Dead>();
	private readonly QueryDescription _prodQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.ProductionQueue>();


	private ForEachWithEntity<Realm.Ecs.Components.Core.Buffs> _buffsQueryDelegate = null!;
	private ForEachWithEntity<Patrol, Position> _patrolArrivalQueryDelegate = null!;
	private ForEachWithEntity<Follow, Position> _followQueryDelegate = null!;
	private ForEachWithEntity<Position, Gatherer> _gatherQueryDelegate = null!;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _movementQueryDelegate = null!;
	private ForEachWithEntity<Attack> _attackCooldownQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, Owner> _targetAcquisitionQueryDelegate = null!;
	private ForEachWithEntity<Position, Owner> _potentialEnemyQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, AttackTarget, Owner> _combatQueryDelegate = null!;
	private ForEachWithEntity<Position, Owner, DefinitionId> _priestScanQueryDelegate = null!;
	private ForEachWithEntity<Position, Health, Owner> _friendlyScanQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, HealingTarget, Owner> _healingExecutionQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.ProductionQueue> _prodQueryDelegate = null!;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _editorMovementQueryDelegate = null!;
	private ForEachWithEntity<InterpolationTarget, Unit3D> _interpolationQueryDelegate = null!;
	private ForEachWithEntity<PlayerResources> _passiveIncomeQueryDelegate = null!;
	
	internal DefinitionManager DefinitionManager => _definitionManager;
	private DefinitionManager _definitionManager = null!;
	
	internal ResourceId _goldResourceId;
	internal ResourceId _woodResourceId;
	internal ResourceId _stoneResourceId;
	
	public Entity PlayerEntity => _playerEntity;
	public Entity EnemyEntity => _enemyPlayerEntity;
	
	private const float CollisionCellSize = 10f;
	private readonly Dictionary<long, List<Unit3D>> _unitGrid = new();
	private readonly Dictionary<long, List<Prop3D>> _propGrid = new();
	private readonly List<List<Unit3D>> _unitListPool = new();
	private readonly List<List<Prop3D>> _propListPool = new();

	private bool _multiplayerActive => Multiplayer.MultiplayerPeer != null;
	private int _localPeerId = 1;
	private int _nextCommandId = 1;
	private float _commandSendTimer = 0f;
	private int _snapshotSequence = 0;
	private int _lastReceivedBaselineSeq = -1;
	private bool _hasReceivedInitialBaseline = false;
	private int _lastAppliedSnapshotSequence = -1;
	private ulong _lastSnapshotReceivedTime = 0;
	private bool _wasClientInMultiplayer = false;
	public bool IsConnectionLost { get; private set; } = false;

	private readonly Dictionary<int, Entity> _peerIdToPlayerEntityMap = new();
	private readonly Dictionary<int, Entity> _serverToClientEntityMap = new();
	private readonly Dictionary<int, int> _clientToServerEntityMap = new();
	private readonly List<NetworkCommand> _unacknowledgedCommands = new();
	private readonly List<WorldSnapshot> _queuedDeltas = new();

	private readonly Dictionary<int, Vector3> _clientCameraPositions = new();
	private readonly Dictionary<int, Dictionary<int, UnitSnapshot>> _lastBaselineSnapshotsPerClient = new();

	public struct Gatherer
	{
		public string ResourceType; // "gold", "wood", "stone"
		public float CarriedAmount;
		public float MaxCapacity;
		public Prop3D TargetNode;
		public bool ReturningToBase;

		public Gatherer(string type, Prop3D node)
		{
			ResourceType = type;
			CarriedAmount = 0f;
			MaxCapacity = 15f;
			TargetNode = node;
			ReturningToBase = false;
		}
	}

	public World EcsWorld { get; private set; }
	public List<Unit3D> SelectedUnits { get; } = new List<Unit3D>();
	public List<Unit3D> AllUnits { get; } = new List<Unit3D>();
	public List<Prop3D> AllProps { get; } = new List<Prop3D>();
	private readonly List<Unit3D> _castlesList = new();

	private Entity _playerEntity;
	private Entity _enemyPlayerEntity;
	private ReplayRecorder _replayRecorder;
	private int _replayTickCounter = 0;
	private readonly Dictionary<int, ReplayUnitSnapshot> _lastRecordedUnits = new();
	private System.Diagnostics.Stopwatch _trackerTickStopwatch = new System.Diagnostics.Stopwatch();
	private System.Diagnostics.Stopwatch _trackerIntervalStopwatch = new System.Diagnostics.Stopwatch();
	private List<float> _trackerTickDurations;
	private List<float> _trackerApiDurations;
	private float _trackerLastTickDelay = 0f;
	private string _activeSpellTargeting = null; // "fireball" or "lightning"
	private string _activeCommandTargeting = null; // "attack" or "move"

	public string ActiveSpellTargeting
	{
		get => _activeSpellTargeting;
		set => _activeSpellTargeting = value;
	}
	public string ActiveCommandTargeting
	{
		get => _activeCommandTargeting;
		set => _activeCommandTargeting = value;
	}
	public Prop3D SelectedProp { get; private set; } = null;
	public string ActiveBuildingPlacementType
	{
		get => _activeBuildingPlacementType;
		set => _activeBuildingPlacementType = value;
	}

	public int CycleSelectionIndex
	{
		get
		{
			if (SelectedUnits.Count == 0) return 0;
			_cycleSelectionIndex = Math.Clamp(_cycleSelectionIndex, 0, SelectedUnits.Count - 1);
			return _cycleSelectionIndex;
		}
		set
		{
			_cycleSelectionIndex = SelectedUnits.Count > 0 ? Math.Clamp(value, 0, SelectedUnits.Count - 1) : 0;
		}
	}

	public bool HasWeaponsUpgrade { get; set; } = false;
	public bool HasShieldsUpgrade { get; set; } = false;
	public bool HasHarvestingUpgrade { get; set; } = false;


	private string _activeBuildingPlacementType = null; // "castle" or "tower"
	private MeshInstance3D _buildingPreviewMesh = null;


	public bool IsMapEditorMode { get; set; } = false;
	public EditableTerrain GroundTerrain { get; private set; }
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
			_isPastingObject = false;
		}
	}
	public string ActivePlaceId { get; set; } = ""; // "soldier", "tree", etc.
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
	public float EditorCameraBoundsLeft { get; set; } = -95.0f;
	public float EditorCameraBoundsRight { get; set; } = 95.0f;
	public float EditorCameraBoundsTop { get; set; } = -95.0f;
	public float EditorCameraBoundsBottom { get; set; } = 125.0f;
	public bool EditorBrushIsSquare { get; set; } = false;
	public enum MirrorMode
	{
		None,
		Horizontal,
		Vertical,
		Both
	}
	public MirrorMode EditorMirrorMode { get; set; } = MirrorMode.None;
	public float EditorClumpDensity { get; set; } = 5.0f;
	public float EditorClumpScaleVar { get; set; } = 0.3f;
	public bool EditorClumpMode { get; set; } = false;
	private float _clumpSpawnCooldown = 0.0f;
	private bool _isDrawingClump = false;
	private List<IEditorAction> _clumpSpawnActionsInSession = new List<IEditorAction>();
	public bool EditorRandomRotation { get; set; } = false;
	public bool EditorRandomScale { get; set; } = false;
	public string EditorSkyboxPath { get; set; } = "res://Assets/skybox_panoramic.jpg";
	public bool EditorHasUnsavedChanges { get; set; } = false;
	public bool EditorBlockMode { get; set; } = true;
	public float EditorBlockLevelHeight { get; set; } = 4.0f;
	private bool _hasBlockTargetHeight = false;
	private float _activeBlockTargetHeight = 0.0f;
	private Node _hoveredEditorObject = null;
	private Vector3? _rampStartPos = null;
	private struct CopiedObjectTemplate
	{
		public string Type;
		public string Id;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}
	private CopiedObjectTemplate? _copiedObject = null;
	private MeshInstance3D _selectionHighlightMesh = null;
	private float _cachedRandomRotation = 0.0f;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom = false;
	private bool _isPastingObject = false;

	private struct DelayedPacket
	{
		public string FunctionName;
		public object[] Arguments;
		public double SendTime;
	}
	private readonly Dictionary<int, List<DelayedPacket>> _spectatorDelayedPackets = new Dictionary<int, List<DelayedPacket>>();

	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_cachedRandomRotation = (float)(GD.Randf() * 360.0);
		_cachedRandomScale = 0.2f + (float)(GD.Randf() * 2.8);
		_hasCachedRandom = true;
	}
	private Vector2I? _selectionStart = null;
	private Vector2I? _selectionEnd = null;
	private bool _isSelectingArea = false;
	public bool PasteOptionTextures { get; set; } = true;
	public bool PasteOptionHeights { get; set; } = true;
	public bool PasteOptionEntities { get; set; } = true;
	private class CopiedAreaTemplate
	{
		public int Width;
		public int Depth;
		public float[,] Heights;
		public Color[,] Colors;
		public List<CopiedEntityInfo> Entities;
	}
	private class CopiedEntityInfo
	{
		public string Type;
		public string Id;
		public Vector3 RelativePos;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}
	private CopiedAreaTemplate _copiedArea = null;
	private float? _activeCliffHeight = null;
	private float[,] _terrainHeightsBefore;
	private Color[,] _terrainColorsBefore;
	private int[,] _terrainPathingBefore;
	private bool _isDrawingTerrain = false;
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
	public bool ActivePingMode { get; set; } = false;


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
		if (meta.PathingCapabilities == null || meta.PathingCapabilities.Length == 0)
		{
			if (meta.MovementType == "air" || meta.MovementType == "flying")
			{
				return 4; // flying
			}
			else if (meta.MovementType == "amphibious")
			{
				return 8 | 1; // ground | shallow_water
			}
			return 8; // ground
		}

		int flags = 0;
		foreach (var cap in meta.PathingCapabilities)
		{
			switch (cap.ToLower())
			{
				case "shallow_water":
					flags |= 1;
					break;
				case "deep_water":
					flags |= 2;
					break;
				case "flying":
				case "air":
					flags |= 4;
					break;
				case "ground":
					flags |= 8;
					break;
				case "unpathable":
					flags |= 16;
					break;
			}
		}
		return flags;
	}


	public int MaxPopulation { get; private set; } = 0;
	public int CurrentPopulation => _currentPopulation;
	private int _currentPopulation = 0;


	public float GameElapsedTime { get; private set; } = 0f;


	public int TimeOfDayIndex => _timeOfDayIndex;
	private int _timeOfDayIndex = 0; // 0 = Day, 1 = Sunset, 2 = Night, 3 = Dawn
	private float _timeOfDayTimer = 0f;
	private const float TimeOfDayCycleDuration = 90f;


	public record struct TowerUpgradeLevel(int Value);

	public record struct BypassPopulationTag;


	private float _fireballCooldown = 0f;
	private float _lightningCooldown = 0f;
	private float _holyLightCooldown = 0f;
	public const float FireballCooldownMax = 12f;
	public const float LightningCooldownMax = 18f;
	public const float HolyLightCooldownMax = 15f;
	public float FireballCooldown => _fireballCooldown;
	public float LightningCooldown => _lightningCooldown;
	public float HolyLightCooldown => _holyLightCooldown;


	public const float ResourceCap = 9999f;


	private float _underAttackAlertTimer = 0f;
	private const float UnderAttackAlertCooldown = 8f;

	public static readonly Dictionary<string, UnitMetadata> UnitRegistry = new();

	public string GetFallbackModelPath(string unitId, bool isBuilding)
	{
		if (isBuilding)
		{
			return unitId switch
			{
				"castle" => "res://Assets/3d/Buildings/altar.glb",
				"tower" => "res://Assets/3d/Buildings/altar_pillar.glb",
				_ => "res://Assets/3d/Buildings/altar.glb"
			};
		}
		else
		{
			return unitId switch
			{
				"worker" => "res://Assets/3d/Characters/adventurer.glb",
				"soldier" => "res://Assets/3d/Characters/armored_warlord.glb",
				"archer" => "res://Assets/3d/Characters/armored_dragon.glb",
				"priest" => "res://Assets/3d/Characters/armored_battlelord.glb",
				_ => "res://Assets/3d/Characters/adventurer.glb"
			};
		}
	}

	private float _goldBackup = 500f;
	private float _woodBackup = 400f;
	private float _stoneBackup = 200f;
	private IMapScript _activeMapScript;

	public readonly Dictionary<int, float> _unitScale = new();
	private readonly Dictionary<int, Entity> _lastAttacker = new();
	private bool _dayNightCycleEnabled = true;

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

	float IGameAPI.Gold
	{
		get
		{
			if (EcsWorld != null && EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				var dict = EcsWorld.Get<PlayerResources>(_playerEntity).Value;
				if (dict.TryGetValue(_goldResourceId, out var val)) return val;
			}
			return _goldBackup;
		}
		set
		{
			if (EcsWorld != null && EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				ref var playerRes = ref EcsWorld.Get<PlayerResources>(_playerEntity);
				if (playerRes.Value.ContainsKey(_goldResourceId))
				{
					playerRes.Value[_goldResourceId] = (int)value;
				}
			}
			else
			{
				_goldBackup = value;
			}
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
		}
	}

	float IGameAPI.Wood
	{
		get
		{
			if (EcsWorld != null && EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				var dict = EcsWorld.Get<PlayerResources>(_playerEntity).Value;
				if (dict.TryGetValue(_woodResourceId, out var val)) return val;
			}
			return _woodBackup;
		}
		set
		{
			if (EcsWorld != null && EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				ref var playerRes = ref EcsWorld.Get<PlayerResources>(_playerEntity);
				if (playerRes.Value.ContainsKey(_woodResourceId))
				{
					playerRes.Value[_woodResourceId] = (int)value;
				}
			}
			else
			{
				_woodBackup = value;
			}
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
		}
	}

	float IGameAPI.Stone
	{
		get
		{
			if (EcsWorld != null && EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				var dict = EcsWorld.Get<PlayerResources>(_playerEntity).Value;
				if (dict.TryGetValue(_stoneResourceId, out var val)) return val;
			}
			return _stoneBackup;
		}
		set
		{
			if (EcsWorld != null && EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				ref var playerRes = ref EcsWorld.Get<PlayerResources>(_playerEntity);
				if (playerRes.Value.ContainsKey(_stoneResourceId))
				{
					playerRes.Value[_stoneResourceId] = (int)value;
				}
			}
			else
			{
				_stoneBackup = value;
			}
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
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
		
		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(unitTypeId, meta.Speed == 0f);

		string name = meta.Name;
		if (isEnemy)
		{
			if (unitTypeId == "worker") name = "Orc Worker";
			else if (unitTypeId == "soldier") name = "Orc Raider";
			else if (unitTypeId == "archer") name = "Dark Archer";
			else if (unitTypeId == "priest") name = "Orc Shaman";
			else if (unitTypeId == "castle") name = "Orc Stronghold";
			else if (unitTypeId == "tower") name = "Orc Totem Tower";
		}

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
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<Unit3D>(entity))
			{
				var tower = EcsWorld.Get<Unit3D>(entity);
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
			var camera = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");
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
			var camera = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");
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
			
			if (clampedTime >= 5f && clampedTime < 6f) _timeOfDayIndex = 3;
			else if (clampedTime >= 6f && clampedTime < 18f) _timeOfDayIndex = 0;
			else if (clampedTime >= 18f && clampedTime < 20f) _timeOfDayIndex = 1;
			else _timeOfDayIndex = 2;

			_timeOfDayTimer = (clampedTime / 24f) * TimeOfDayCycleDuration;
			UpdateDayNightVisuals(clampedTime / 24f);
		}).CallDeferred();
	}

	void IGameAPI.SetDayNightCycleEnabled(bool enabled)
	{
		_dayNightCycleEnabled = enabled;
	}

	void IGameAPI.KillUnit(IUnit unit)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (EcsWorld.Has<Unit3D>(wrapper.Entity))
			{
				var u3d = EcsWorld.Get<Unit3D>(wrapper.Entity);
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
			if (EcsWorld.Has<Unit3D>(wrapper.Entity))
			{
				var u3d = EcsWorld.Get<Unit3D>(wrapper.Entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					SelectedUnits.Remove(u3d);
					AllUnits.Remove(u3d);
					if (u3d.UnitId == "castle")
					{
						_castlesList.Remove(u3d);
					}
					int id = wrapper.Entity.Id;
					_unitScale.Remove(id);
					_lastAttacker.Remove(id);
					_unitWrapperCache.Remove(id);
					EcsWorld.Destroy(wrapper.Entity);
					u3d.QueueFree();
				}
			}
		}
	}



	private readonly Dictionary<int, float> _playerGold = new();
	private readonly Dictionary<int, bool> _playerActive = new();
	private readonly Dictionary<int, string> _playerNames = new();

	private int _nextTimerHandle = 0;
	private readonly Dictionary<int, (float Interval, float Remaining, bool Repeating, Action Callback)> _scheduledTimers = new();

	private static readonly Random _rng = new();

	int IGameAPI.PlayerCount => Math.Max(1, _playerActive.Count);

	string IGameAPI.GetPlayerName(int playerIndex)
	{
		if (_playerNames.TryGetValue(playerIndex, out var name)) return name;
		return $"Player {playerIndex + 1}";
	}

	bool IGameAPI.IsPlayerActive(int playerIndex)
	{
		if (_playerActive.TryGetValue(playerIndex, out var active)) return active;
		return playerIndex == 0;
	}

	float IGameAPI.GetPlayerGold(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Gold;
		return _playerGold.TryGetValue(playerIndex, out var g) ? g : 0f;
	}

	void IGameAPI.SetPlayerGold(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold = amount; return; }
		_playerGold[playerIndex] = Math.Max(0f, amount);
	}

	void IGameAPI.AdjustPlayerGold(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold += delta; return; }
		float current = _playerGold.TryGetValue(playerIndex, out var g) ? g : 0f;
		_playerGold[playerIndex] = Math.Max(0f, current + delta);
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
			var u3d = EcsWorld.Has<Unit3D>(wrapper.Entity) ? EcsWorld.Get<Unit3D>(wrapper.Entity) : null;
			if (u3d != null && GodotObject.IsInstanceValid(u3d))
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

	private readonly Dictionary<int, float> _playerWood = new();

	float IGameAPI.GetPlayerWood(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Wood;
		return _playerWood.TryGetValue(playerIndex, out var w) ? w : 0f;
	}

	void IGameAPI.SetPlayerWood(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood = amount; return; }
		_playerWood[playerIndex] = Math.Max(0f, amount);
	}

	void IGameAPI.AdjustPlayerWood(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood += delta; return; }
		float current = _playerWood.TryGetValue(playerIndex, out var w) ? w : 0f;
		_playerWood[playerIndex] = Math.Max(0f, current + delta);
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



	private struct ZoneBounds
	{
		public float MinX, MinZ, MaxX, MaxZ;
		public System.Numerics.Vector3 Center;
	}

	private readonly List<ZoneBounds> _registeredZones = new();
	private readonly Dictionary<int, HashSet<int>> _unitZoneOccupancy = new();
	private readonly int[] _playerKillCounts = new int[12];

	public event Action<IUnit, int>? OnUnitEnterZone;
	public event Action<int>? OnPlayerLeft;

	int IGameAPI.DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		int handle = _registeredZones.Count;
		float cx = (minX + maxX) * 0.5f;
		float cz = (minZ + maxZ) * 0.5f;
		_registeredZones.Add(new ZoneBounds
		{
			MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ,
			Center = new System.Numerics.Vector3(cx, 0f, cz)
		});
		return handle;
	}

	System.Numerics.Vector3 IGameAPI.GetZoneCenter(int zoneHandle)
	{
		if (zoneHandle < 0 || zoneHandle >= _registeredZones.Count)
			return System.Numerics.Vector3.Zero;
		return _registeredZones[zoneHandle].Center;
	}

	private void TickZoneTriggers()
	{
		if (OnUnitEnterZone == null || _registeredZones.Count == 0) return;

		foreach (var unit3D in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit3D) || !EcsWorld.IsAlive(unit3D.Entity)) continue;

			int unitId = unit3D.Entity.Id;
			var pos = unit3D.GlobalPosition;

			if (!_unitZoneOccupancy.TryGetValue(unitId, out var occupiedZones))
			{
				occupiedZones = new HashSet<int>();
				_unitZoneOccupancy[unitId] = occupiedZones;
			}

			for (int i = 0; i < _registeredZones.Count; i++)
			{
				ref ZoneBounds z = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_registeredZones)[i];
				bool inside = pos.X >= z.MinX && pos.X <= z.MaxX && pos.Z >= z.MinZ && pos.Z <= z.MaxZ;

				if (inside && !occupiedZones.Contains(i))
				{
					occupiedZones.Add(i);
					var wrapper = GetUnitWrapper(unit3D.Entity);
					OnUnitEnterZone?.Invoke(wrapper, i);
				}
				else if (!inside)
				{
					occupiedZones.Remove(i);
				}
			}
		}
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
		if (playerIndex < 0 || playerIndex >= _playerKillCounts.Length) return 0;
		return _playerKillCounts[playerIndex];
	}

	void IGameAPI.SetPlayerKills(int playerIndex, int kills)
	{
		if (playerIndex < 0 || playerIndex >= _playerKillCounts.Length) return;
		_playerKillCounts[playerIndex] = kills;
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
				if (EcsWorld.Has<Unit3D>(wrapper.Entity))
				{
					var u3d = EcsWorld.Get<Unit3D>(wrapper.Entity);
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
		GD.Print($"[GAMEHOST_READY] GameHost _Ready starting");
		Instance = this;
		GameSettings.ApplyGraphicsSettings(this);

		if (System.OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.StartInstallIfNeeded();
		}

		if (Multiplayer.IsServer())
		{
			_trackerTickDurations = new List<float>(100000);
			_trackerApiDurations = new List<float>(10000);
			_trackerIntervalStopwatch.Start();
		}

		CreateGround();
		SetupSkybox();
		UpdateDayNightVisuals(0.5f);

		EcsWorld = World.Create();
		_definitionManager = new DefinitionManager();
		_goldResourceId = "gold".AsResourceId(_definitionManager);
		_woodResourceId = "wood".AsResourceId(_definitionManager);
		_stoneResourceId = "stone".AsResourceId(_definitionManager);
		_buffsQueryDelegate = UpdateBuffsQueryAction;
		_patrolArrivalQueryDelegate = PatrolArrivalQueryAction;
		_followQueryDelegate = FollowQueryAction;
		_gatherQueryDelegate = GatherQueryAction;
		_movementQueryDelegate = MovementQueryAction;
		_attackCooldownQueryDelegate = AttackCooldownQueryAction;
		_targetAcquisitionQueryDelegate = TargetAcquisitionQueryAction;
		_potentialEnemyQueryDelegate = ScanEnemyQueryAction;
		_combatQueryDelegate = CombatQueryAction;
		_priestScanQueryDelegate = PriestScanQueryAction;
		_friendlyScanQueryDelegate = ScanFriendlyQueryAction;
		_healingExecutionQueryDelegate = HealingExecutionQueryAction;
		_prodQueryDelegate = ProdQueryAction;
		_editorMovementQueryDelegate = ProcessMapEditorPhysicsQueryAction;
		_interpolationQueryDelegate = InterpolationQueryAction;
		_passiveIncomeQueryDelegate = UpdatePassiveIncomeQueryAction;

		if (_multiplayerActive)
		{
			_localPeerId = Multiplayer.GetUniqueId();
			if (!Multiplayer.IsServer())
			{
				_wasClientInMultiplayer = true;
				_lastSnapshotReceivedTime = Time.GetTicksMsec();
			}
			if (Multiplayer is SceneMultiplayer sceneMultiplayer)
			{
				sceneMultiplayer.ServerRelay = false;
			}

			if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
			{
				foreach (var p in LobbyManager.Instance.PlayerList)
				{
					var playerEntity = EcsWorld.Create();
					EcsWorld.Add(playerEntity, new Player());
					EcsWorld.Add(playerEntity, new Name(p.Name));
					InitializePlayerResources(playerEntity);
					_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
					if (p.PeerId == _localPeerId)
					{
						_playerEntity = playerEntity;
					}
					else
					{
						_enemyPlayerEntity = playerEntity;
					}
				}
			}
			else
			{
				_playerEntity = EcsWorld.Create();
				EcsWorld.Add(_playerEntity, new Player());
				EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
				InitializePlayerResources(_playerEntity);
				_peerIdToPlayerEntityMap[1] = _playerEntity;

				_enemyPlayerEntity = EcsWorld.Create();
				EcsWorld.Add(_enemyPlayerEntity, new Player());
				EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
				InitializePlayerResources(_enemyPlayerEntity);
				_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;
			}
		}
		else
		{
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
			InitializePlayerResources(_playerEntity);
			_peerIdToPlayerEntityMap[1] = _playerEntity;

			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
			InitializePlayerResources(_enemyPlayerEntity);
			_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;
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
			_replayTickCounter = 0;
			_lastRecordedUnits.Clear();
			_replayRecorder = new ReplayRecorder(
				replayPath, 
				normalizedMapName, 
				LobbyManager.Instance?.PlayerList
			);
			_replayRecorder.Start();
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
	}

	public void StopRecording()
	{
		if (_replayRecorder != null)
		{
			_replayRecorder.Stop();
			_replayRecorder = null;
		}
	}

	public override void _ExitTree()
	{
		if (Multiplayer.IsServer() && _trackerTickDurations != null && _trackerTickDurations.Count >= 30)
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
		_spectatorDelayedPackets.Clear();
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
			var gatherer = new Gatherer("gold", enemyGoldmine);
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
			if (EcsWorld.Has<Unit3D>(entity)) EcsWorld.Get<Unit3D>(entity).Velocity = Vector3.Zero;
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

				var gatherer = new Gatherer(resType, prop);
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


			var gatherer = new Gatherer(resType, prop);
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

		float cooldown = 1.5f;
		bool isHero = false;
		if (UnitRegistry.TryGetValue(id, out var regMeta))
		{
			cooldown = regMeta.AttackCooldown > 0 ? regMeta.AttackCooldown : 1.5f;
			isHero = regMeta.IsHero;
		}

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(id));
		EcsWorld.Add(entity, new Name(name));
		EcsWorld.Add(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
		EcsWorld.Add(entity, new Owner(owner));

		if (isHero)
		{
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Hero());
			EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Level(1));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Experience(0f));
		}
		

		bool isPlayer = owner.Value == _playerEntity;
		if (isPlayer)
		{
			if (HasShieldsUpgrade)
			{
				armor += 2f;
			}
			if (HasWeaponsUpgrade && (damage > 0 || id == "priest") && id != "castle" && id != "tower")
			{
				damage += 3f;
			}
		}

		EcsWorld.Add(entity, new Health(hp, hp));
		
		if (damage > 0 || id == "priest")
		{
			EcsWorld.Add(entity, new Attack(damage, range, cooldown));
		}
		
		EcsWorld.Add(entity, new Armor(armor));

		if (speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(speed, 20f, 10f));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			EcsWorld.Add(entity, new Building());
			if (id == "tower")
			{
				EcsWorld.Add(entity, new TowerUpgradeLevel(1));
			}
		}

		OnUnitCreated?.Invoke(GetUnitWrapper(entity));
		return entity;
	}

	private Unit3D SpawnUnit3D(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, bool isEnemy, bool isFromQueue = false)
	{
		var unit3D = new Unit3D();
		unit3D.Entity = entity;
		unit3D.UnitId = id;
		unit3D.IsBuilding = isBuilding;
		unit3D.IsEnemy = isEnemy;
		unit3D.Name = $"{id}_{entity.Id}";

		if (!isBuilding && !IsMapEditorMode)
		{
			unit3D.CollisionLayer = 0;
			unit3D.CollisionMask = 0;
		}

		AddChild(unit3D);
		unit3D.Position = pos; // Set position after AddChild
		unit3D.LoadModel(modelPath);

		if (IsMapEditorMode)
		{
			unit3D.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			unit3D.Scale *= EditorPlacementScale;
		}


		EcsWorld.Add(entity, unit3D); // Store Unit3D as component for easy mapping


		if (!isEnemy && UnitRegistry.TryGetValue(id, out var popMeta))
		{
			if (id == "castle")
			{
				MaxPopulation += 20;
			}
			if (!isFromQueue)
			{
				_currentPopulation += popMeta.PopCost;
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
		bool isLost = false;
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			ulong now = Time.GetTicksMsec();
			if (_lastSnapshotReceivedTime > 0)
			{
				double timeSinceLastSnapshot = (now - _lastSnapshotReceivedTime) / 1000.0;
				if (timeSinceLastSnapshot > 30.0)
				{
					isLost = true;
				}
			}
		}
		else
		{
			isLost = true;
		}
		IsConnectionLost = isLost;
	}

	private void ProcessGameplayTick(float fDelta)
	{
		float actualIntervalMs = 0f;
		if (Multiplayer.IsServer())
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
		GameElapsedTime += fDelta;
		_tickEntitiesToClearOrders.Clear();
		_tickEntitiesToStopGathering.Clear();
		_tickSpawningRequests.Clear();
		_tickAddPathFollow.Clear();
		_tickNeedsUiRefresh = false;
		if (_fireballCooldown > 0) _fireballCooldown = Math.Max(0, _fireballCooldown - fDelta);
		if (_lightningCooldown > 0) _lightningCooldown = Math.Max(0, _lightningCooldown - fDelta);
		if (_holyLightCooldown > 0) _holyLightCooldown = Math.Max(0, _holyLightCooldown - fDelta);
		if (_underAttackAlertTimer > 0) _underAttackAlertTimer -= fDelta;


		EcsWorld.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);


		EcsWorld.Query(in _buffQuery, _buffsQueryDelegate);

		UpdateGameplayDayNightCycle(fDelta);

		ProcessPatrolArrivals();
		ProcessFollowMovements();

		ProcessGatheringTicks();
		ProcessMovementTicks();

		EcsWorld.Query(in _attackCooldownQuery, _attackCooldownQueryDelegate);
		ProcessTargetAcquisition();
		ProcessCombatTicks();

		ProcessHealingTicks();

		UpdateMinimapPings(fDelta);
		EcsWorld.Query(in _prodQuery, _prodQueryDelegate);

		foreach (var (entity, pf) in _tickAddPathFollow)
		{
			if (EcsWorld.IsAlive(entity))
			{
				EcsWorld.Add(entity, pf);
			}
		}


		TickScheduledTimers(fDelta);
		TickZoneTriggers();
		if (_activeMapScript != null)
		{
			_activeMapScript.Update(this, fDelta);
		}


		UpdateBuildingPreview();

		if (!ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays && _replayRecorder != null)
		{
			RecordGameplayTick();
		}

		if (_multiplayerActive && Multiplayer.IsServer())
		{
			UpdateServerSnapshotTick(fDelta);
		}

		if (Multiplayer.IsServer())
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

		ApplyDeferredTickCommands();
	}

	private void UpdateGameplayDayNightCycle(float fDelta)
	{
		if (!IsMapEditorMode && _dayNightCycleEnabled)
		{

			_timeOfDayTimer += fDelta;
			if (_timeOfDayTimer >= TimeOfDayCycleDuration)
			{
				_timeOfDayTimer -= TimeOfDayCycleDuration;
			}

			float progress = _timeOfDayTimer / TimeOfDayCycleDuration; // Normalized 0.0 to 1.0 across full cycle
			

			float currentHour = progress * 24f;
			if (currentHour >= 5f && currentHour < 6f) _timeOfDayIndex = 3;      // Dawn
			else if (currentHour >= 6f && currentHour < 18f) _timeOfDayIndex = 0; // Day
			else if (currentHour >= 18f && currentHour < 20f) _timeOfDayIndex = 1;// Sunset
			else _timeOfDayIndex = 2;                                             // Night

			UpdateDayNightVisuals(progress);
		}
	}

	private void ProcessPatrolArrivals()
	{
		_tickPatrolToFlip.Clear();
		EcsWorld.Query(in _patrolArrivalQuery, _patrolArrivalQueryDelegate);
		foreach (var (entity, patrol) in _tickPatrolToFlip)
		{
			var flipped = new Patrol(patrol.PointA, patrol.PointB) { GoingToB = !patrol.GoingToB };
			EcsWorld.Set(entity, flipped);
			var newDest = flipped.GoingToB ? flipped.PointB : flipped.PointA;
			var moveTo = new MoveTo(new System.Numerics.Vector3(newDest.X, newDest.Y, newDest.Z));
			if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
			else EcsWorld.Add(entity, moveTo);
		}
	}

	private void ProcessFollowMovements()
	{
		_tickFollowToStop.Clear();
		_tickFollowToMove.Clear();
		EcsWorld.Query(in _followQuery, _followQueryDelegate);

		foreach (var ent in _tickFollowToStop)
		{
			if (EcsWorld.IsAlive(ent))
			{
				if (!EcsWorld.IsAlive(EcsWorld.Get<Follow>(ent).Target) || EcsWorld.Has<Dead>(EcsWorld.Get<Follow>(ent).Target))
				{
					EcsWorld.Remove<Follow>(ent);
				}
				if (EcsWorld.Has<MoveTo>(ent))
				{
					EcsWorld.Remove<MoveTo>(ent);
				}
				if (EcsWorld.Has<Unit3D>(ent))
				{
					var unit3D = EcsWorld.Get<Unit3D>(ent);
					unit3D.Velocity = Vector3.Zero;
				}
			}
		}

		foreach (var (ent, targetPos) in _tickFollowToMove)
		{
			if (EcsWorld.IsAlive(ent))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (EcsWorld.Has<MoveTo>(ent)) EcsWorld.Set(ent, moveTo);
				else EcsWorld.Add(ent, moveTo);
			}
		}
	}

	private void ProcessGatheringTicks()
	{
		_tickGatherersToUpdate.Clear();
		
		EcsWorld.Query(in _gatherQuery, _gatherQueryDelegate);
		
		foreach (var (worker, newState, dest) in _tickGatherersToUpdate)
		{
			if (EcsWorld.IsAlive(worker))
			{
				EcsWorld.Set(worker, newState);
				if (dest.HasValue)
				{
					var moveTo = new MoveTo(new System.Numerics.Vector3(dest.Value.X, dest.Value.Y, dest.Value.Z));
					if (EcsWorld.Has<MoveTo>(worker)) EcsWorld.Set(worker, moveTo);
					else EcsWorld.Add(worker, moveTo);
				}
			}
		}
	}

	private void ProcessMovementTicks()
	{
		_tickArrivedUnits.Clear();
		RebuildSpatialGrid();
		EcsWorld.Query(in _movementQuery, _movementQueryDelegate);
		foreach (var entity in _tickArrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				if (EcsWorld.Has<PathFollow>(entity))
				{
					EcsWorld.Remove<PathFollow>(entity);
				}
				if (EcsWorld.Has<WaypointQueue>(entity))
				{
					var q = EcsWorld.Get<WaypointQueue>(entity);
					if (q.Count > 0)
					{
						var nextWaypoint = q.Dequeue();
						EcsWorld.Set(entity, q);
						EcsWorld.Set(entity, new MoveTo(nextWaypoint));
						continue;
					}
					else
					{
						EcsWorld.Remove<WaypointQueue>(entity);
					}
				}
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}

	private void ProcessTargetAcquisition()
	{
		_tickNewAttackTargets.Clear();
		EcsWorld.Query(in _targetAcquisitionQuery, _targetAcquisitionQueryDelegate);
		foreach (var (attacker, target) in _tickNewAttackTargets)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				if (EcsWorld.Has<AttackTarget>(attacker))
					EcsWorld.Set(attacker, target);
				else
					EcsWorld.Add(attacker, target);
			}
		}
	}

	private void ProcessCombatTicks()
	{
		_tickActionsToRemoveTarget.Clear();
		_tickActionsToChase.Clear();
		_tickActionsToStopChasing.Clear();
		_tickUnitsToKill.Clear();

		EcsWorld.Query(in _combatQuery, _combatQueryDelegate);


		foreach (var (targetEntity, target3D) in _tickUnitsToKill)
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				if (!EcsWorld.Has<Dead>(targetEntity))
				{
					EcsWorld.Add<Dead>(targetEntity);
					this.CallDeferred(nameof(KillUnit), target3D);
				}
			}
		}


		foreach (var ent in _tickActionsToRemoveTarget)
		{
			if (EcsWorld.IsAlive(ent))
			{
				if (EcsWorld.Has<AttackTarget>(ent))
				{
					EcsWorld.Remove<AttackTarget>(ent);
				}

				if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(ent))
				{
					var am = EcsWorld.Get<Realm.Ecs.Components.Movement.AttackMove>(ent);
					var moveTo = new MoveTo(am.Target);
					if (EcsWorld.Has<MoveTo>(ent))
						EcsWorld.Set(ent, moveTo);
					else
						EcsWorld.Add(ent, moveTo);
				}
				else if (EcsWorld.Has<Patrol>(ent))
				{

					var patrol = EcsWorld.Get<Patrol>(ent);
					var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
					var moveTo = new MoveTo(destVec);
					if (EcsWorld.Has<MoveTo>(ent))
						EcsWorld.Set(ent, moveTo);
					else
						EcsWorld.Add(ent, moveTo);
				}
			}
		}

		foreach (var (attacker, targetPos) in _tickActionsToChase)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (EcsWorld.Has<MoveTo>(attacker))
				{
					EcsWorld.Set(attacker, moveTo);
				}
				else
				{
					EcsWorld.Add(attacker, moveTo);
				}
			}
		}

		foreach (var attacker in _tickActionsToStopChasing)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				if (EcsWorld.Has<MoveTo>(attacker))
				{
					EcsWorld.Remove<MoveTo>(attacker);
				}
				if (EcsWorld.Has<Unit3D>(attacker))
				{
					var unit3D = EcsWorld.Get<Unit3D>(attacker);
					unit3D.Velocity = Vector3.Zero;
				}
			}
		}
	}

	private void ProcessHealingTicks()
	{
		_tickNewHealingTargets.Clear();
		EcsWorld.Query(in _priestScanQuery, _priestScanQueryDelegate);
		foreach (var (priest, target) in _tickNewHealingTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				if (EcsWorld.Has<HealingTarget>(priest)) EcsWorld.Set(priest, target);
				else EcsWorld.Add(priest, target);
			}
		}


		_tickHealRemoveTargets.Clear();
		_tickHealChaseTargets.Clear();
		_tickHealStopChasing.Clear();

		EcsWorld.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

		foreach (var ent in _tickHealRemoveTargets)
		{
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<HealingTarget>(ent))
			{
				EcsWorld.Remove<HealingTarget>(ent);
			}
		}

		foreach (var (priest, targetPos) in _tickHealChaseTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Set(priest, moveTo);
				else EcsWorld.Add(priest, moveTo);
			}
		}

		foreach (var priest in _tickHealStopChasing)
		{
			if (EcsWorld.IsAlive(priest))
			{
				if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Remove<MoveTo>(priest);
				if (EcsWorld.Has<Unit3D>(priest))
				{
					var unit3D = EcsWorld.Get<Unit3D>(priest);
					unit3D.Velocity = Vector3.Zero;
				}
			}
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

	private void ApplyDeferredTickCommands()
	{
		foreach (var ent in _tickEntitiesToClearOrders)
		{
			if (EcsWorld.IsAlive(ent))
			{
				ClearUnitOrders(ent);
			}
		}

		foreach (var ent in _tickEntitiesToStopGathering)
		{
			if (EcsWorld.IsAlive(ent))
			{
				StopGatheringMovement(ent);
			}
		}

		foreach (var req in _tickSpawningRequests)
		{
			SpawnUnitFromProduction(req.UnitId, req.Position, req.IsEnemy, req.RallyPoint, req.IsFromQueue);
		}

		if (_tickNeedsUiRefresh && InGameHUD.Instance != null)
		{
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	private void UpdateDayNightVisuals(float progress)
	{
		_dayNightService.UpdateDayNightVisuals(this, progress);
	}

	public void CycleTimeOfDay()
	{
		var (newIndex, newTimer) = _dayNightService.CycleTimeOfDay(this, _timeOfDayIndex, TimeOfDayCycleDuration);
		_timeOfDayIndex = newIndex;
		_timeOfDayTimer = newTimer;
	}

	public string GetTimeOfDayName()
	{
		return _dayNightService.GetTimeOfDayName(_timeOfDayIndex);
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
