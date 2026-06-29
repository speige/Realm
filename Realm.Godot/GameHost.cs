using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Common;
using Realm.MapAPI;
using MemoryPack;
using Realm.Ecs.Services;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Godot.ReplaySystem;

public partial class GameHost : Node3D, IGameAPI
{
	public static GameHost Instance { get; private set; }
	private static readonly long[] _pathCorridorBuffer = new long[512];
	private static readonly DtStraightPath[] _straightPathBuffer = new DtStraightPath[512];
	private static readonly DtQueryDefaultFilter _queryFilter = new DtQueryDefaultFilter();
	private static readonly RcVec3f _pathfindingExtents = new RcVec3f(2f, 4f, 2f);
	public string ActiveMapName { get; private set; } = "melee";

	private bool _multiplayerActive => Multiplayer.MultiplayerPeer != null;
	private int _localPeerId = 1;
	private int _nextCommandId = 1;
	private float _commandSendTimer = 0f;
	private int _snapshotSequence = 0;
	private int _lastReceivedBaselineSeq = -1;
	private bool _hasReceivedInitialBaseline = false;
	private int _lastAppliedSnapshotSequence = -1;

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

	private Entity _playerEntity;
	private Entity _enemyPlayerEntity;
	private ReplayRecorder _replayRecorder;
	private int _replayTickCounter = 0;
	private readonly Dictionary<int, ReplayUnitSnapshot> _lastRecordedUnits = new();
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

	// Building Placement variables
	private string _activeBuildingPlacementType = null; // "castle" or "tower"
	private MeshInstance3D _buildingPreviewMesh = null;

	// Map Editor Mode variables
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

	// Map ping variables
	public struct MinimapPing
	{
		public Vector3 WorldPos;
		public float LifeTime;
		public float MaxLifeTime;
	}
	public List<MinimapPing> ActivePings { get; } = new List<MinimapPing>();
	public bool ActivePingMode { get; set; } = false;

	// Unit Metadata Definition
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

	// Supply / Population cap
	public int MaxPopulation { get; private set; } = 0;
	public int CurrentPopulation => _currentPopulation;
	private int _currentPopulation = 0;

	// Game elapsed time (seconds)
	public float GameElapsedTime { get; private set; } = 0f;

	// Day/night cycle variables
	public int TimeOfDayIndex => _timeOfDayIndex;
	private int _timeOfDayIndex = 0; // 0 = Day, 1 = Sunset, 2 = Night, 3 = Dawn
	private float _timeOfDayTimer = 0f;
	private const float TimeOfDayCycleDuration = 90f;

	// Tower upgrade levels component
	public record struct TowerUpgradeLevel(int Value);

	public record struct BypassPopulationTag;

	// Spell cooldown tracking (per spell, global for now since spells are hero-less)
	private float _fireballCooldown = 0f;
	private float _lightningCooldown = 0f;
	private float _holyLightCooldown = 0f;
	public const float FireballCooldownMax = 12f;
	public const float LightningCooldownMax = 18f;
	public const float HolyLightCooldownMax = 15f;
	public float FireballCooldown => _fireballCooldown;
	public float LightningCooldown => _lightningCooldown;
	public float HolyLightCooldown => _holyLightCooldown;

	// Resource storage cap (hard ceiling on gold/wood/stone)
	public const float ResourceCap = 9999f;

	// Under-attack alert throttle
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

	private readonly Dictionary<int, float> _unitScale = new();
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
			selected = new UnitWrapper(SelectedUnits[0].Entity, EcsWorld);
		}
		OnPlayerChatMessage?.Invoke(message, selected);
	}

	public void TriggerKillUnit(Unit3D unit)
	{
		KillUnit(unit);
	}

	float IGameAPI.Gold
	{
		get => InGameHUD.Instance != null ? InGameHUD.Instance.Gold : _goldBackup;
		set
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold = value;
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				_goldBackup = value;
			}
		}
	}

	float IGameAPI.Wood
	{
		get => InGameHUD.Instance != null ? InGameHUD.Instance.Wood : _woodBackup;
		set
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Wood = value;
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				_woodBackup = value;
			}
		}
	}

	float IGameAPI.Stone
	{
		get => InGameHUD.Instance != null ? InGameHUD.Instance.Stone : _stoneBackup;
		set
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Stone = value;
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				_stoneBackup = value;
			}
		}
	}

	float IGameAPI.GameElapsedTime => GameElapsedTime;

	IUnit IGameAPI.SpawnUnit(string unitTypeId, System.Numerics.Vector3 position, bool isEnemy)
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
		var unit3D = SpawnUnit3D(entity, unitTypeId, modelPath, pos, meta.Speed == 0f, isEnemy, false);
		
		return new UnitWrapper(entity, EcsWorld);
	}

	IUnit IGameAPI.SpawnUnit_V2(string unitTypeId, System.Numerics.Vector3 position, bool isEnemy, bool bypassPopulation)
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
		
		return new UnitWrapper(entity, EcsWorld);
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
				list.Add(new UnitWrapper(u.Entity, EcsWorld));
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
				list.Add(new UnitWrapper(u.Entity, EcsWorld));
			}
		}
		return list;
	}

	IEnumerable<IResourceNode> IGameAPI.GetResourceNodes()
	{
		var list = new List<IResourceNode>();
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				if (prop.PropId == "goldmine" || prop.PropId == "tree" || prop.PropId == "rock")
				{
					list.Add(new ResourceNodeWrapper(prop));
				}
			}
		}
		return list;
	}

	void IGameAPI.ShowFeedbackText(string text, System.Numerics.Vector3 color)
	{
		if (InGameHUD.Instance != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			Callable.From(() => InGameHUD.Instance.ShowFeedbackText(text, gColor)).CallDeferred();
		}
	}

	void IGameAPI.PlayWarningSound()
	{
		Callable.From(() => UIManager.Instance?.PlayWarningSound()).CallDeferred();
	}

	void IGameAPI.PlayClickSound()
	{
		Callable.From(() => UIManager.Instance?.PlayClickSound()).CallDeferred();
	}

	void IGameAPI.TriggerVictory()
	{
		GD.Print("[GameHost] Victory triggered by map script!");
		Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
	}

	void IGameAPI.TriggerDefeat()
	{
		GD.Print("[GameHost] Defeat triggered by map script!");
		Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
	}

	IUnit? IGameAPI.GetCastle(bool isEnemy)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.UnitId == "castle" && u.IsEnemy == isEnemy && EcsWorld.IsAlive(u.Entity) && !EcsWorld.Has<Dead>(u.Entity))
			{
				return new UnitWrapper(u.Entity, EcsWorld);
			}
		}
		return null;
	}

	void IGameAPI.UpgradeUnit(IUnit unit)
	{
		if (unit is UnitWrapper wrapper)
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
				return new UnitWrapper(u.Entity, EcsWorld);
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
				list.Add(new UnitWrapper(u.Entity, EcsWorld));
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
			// Expects a 24-hour clock input float (e.g. 12.0 = Noon)
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
		if (unit is UnitWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (EcsWorld.Has<Unit3D>(wrapper.Entity))
			{
				var u3d = EcsWorld.Get<Unit3D>(wrapper.Entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					if (!EcsWorld.Has<Dead>(wrapper.Entity))
					{
						EcsWorld.Add<Dead>(wrapper.Entity);
						Callable.From(() => KillUnit(u3d)).CallDeferred();
					}
				}
			}
		}
	}

	void IGameAPI.DestroyUnit(IUnit unit)
	{
		if (unit is UnitWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (EcsWorld.Has<Unit3D>(wrapper.Entity))
			{
				var u3d = EcsWorld.Get<Unit3D>(wrapper.Entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					SelectedUnits.Remove(u3d);
					AllUnits.Remove(u3d);
					int id = wrapper.Entity.Id;
					_unitScale.Remove(id);
					_lastAttacker.Remove(id);
					EcsWorld.Destroy(wrapper.Entity);
					u3d.QueueFree();
				}
			}
		}
	}

	// ── Multi-player IGameAPI implementations ──────────────────────────────

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
		if (unit is UnitWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
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
			var w = new UnitWrapper(u.Entity, EcsWorld);
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
			var w = new UnitWrapper(u.Entity, EcsWorld);
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

	// ── Zone / Region trigger implementation ──────────────────────────────

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
					var wrapper = new UnitWrapper(unit3D.Entity, EcsWorld);
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
		if (unit is UnitWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
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
		if (unit is UnitWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
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
			if (unit is UnitWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
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


	private class UnitWrapper : IUnit
	{
		private readonly Entity _entity;
		private readonly World _world;

		public UnitWrapper(Entity entity, World world)
		{
			_entity = entity;
			_world = world;
		}

		public Entity Entity => _entity;

		public int UniqueId => _entity.Id;

		public override bool Equals(object? obj)
		{
			return obj is UnitWrapper other && _entity == other._entity;
		}

		public override int GetHashCode()
		{
			return _entity.GetHashCode();
		}

		public string UnitId
		{
			get
			{
				if (!_world.IsAlive(_entity)) return string.Empty;
				if (_world.Has<Unit3D>(_entity))
				{
					return _world.Get<Unit3D>(_entity).UnitId;
				}
				return string.Empty;
			}
		}

		public string Name
		{
			get
			{
				if (!_world.IsAlive(_entity)) return string.Empty;
				if (_world.Has<Name>(_entity))
				{
					return _world.Get<Name>(_entity).Value;
				}
				return string.Empty;
			}
		}

		public bool IsEnemy
		{
			get
			{
				if (!_world.IsAlive(_entity)) return false;
				if (_world.Has<Unit3D>(_entity))
				{
					return _world.Get<Unit3D>(_entity).IsEnemy;
				}
				return false;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<Unit3D>(_entity))
				{
					var u3d = _world.Get<Unit3D>(_entity);
					if (GodotObject.IsInstanceValid(u3d))
					{
						u3d.IsEnemy = value;
						if (_world.Has<Owner>(_entity))
						{
							var playerOwner = value 
								? GameHost.Instance._enemyPlayerEntity.AsPlayerEntity(_world) 
								: GameHost.Instance._playerEntity.AsPlayerEntity(_world);
							_world.Set(_entity, new Owner(playerOwner));
						}
					}
				}
			}
		}

		public bool IsBuilding
		{
			get
			{
				if (!_world.IsAlive(_entity)) return false;
				if (_world.Has<Unit3D>(_entity))
				{
					return _world.Get<Unit3D>(_entity).IsBuilding;
				}
				return false;
			}
		}

		public System.Numerics.Vector3 Position
		{
			get
			{
				if (!_world.IsAlive(_entity)) return System.Numerics.Vector3.Zero;
				if (_world.Has<Position>(_entity))
				{
					var pos = _world.Get<Position>(_entity).Value;
					return new System.Numerics.Vector3(pos.X, pos.Y, pos.Z);
				}
				return System.Numerics.Vector3.Zero;
			}
		}

		public float Health
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Health>(_entity))
				{
					return _world.Get<Health>(_entity).Current;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<Health>(_entity))
				{
					var hp = _world.Get<Health>(_entity);
					float finalHp = Math.Max(0f, value);
					_world.Set(_entity, new Health(finalHp, hp.Max));
					if (_world.Has<Unit3D>(_entity))
					{
						var u3d = _world.Get<Unit3D>(_entity);
						u3d.SetDeferred("current_hp", finalHp);
					}
					if (finalHp <= 0f)
					{
						if (!_world.Has<Dead>(_entity))
						{
							_world.Add(_entity, new Dead());
							if (_world.Has<Unit3D>(_entity))
							{
								var u3d = _world.Get<Unit3D>(_entity);
								GameHost.Instance.TriggerKillUnit(u3d);
							}
						}
					}
				}
			}
		}

		public float MaxHealth
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Health>(_entity))
				{
					return _world.Get<Health>(_entity).Max;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<Health>(_entity))
				{
					var hp = _world.Get<Health>(_entity);
					float finalMax = Math.Max(1f, value);
					float finalCur = Math.Min(hp.Current, finalMax);
					_world.Set(_entity, new Health(finalCur, finalMax));
					if (_world.Has<Unit3D>(_entity))
					{
						var u3d = _world.Get<Unit3D>(_entity);
						u3d.SetDeferred("max_hp", finalMax);
						u3d.SetDeferred("current_hp", finalCur);
					}
				}
			}
		}

		public float Damage
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Attack>(_entity))
				{
					return _world.Get<Attack>(_entity).Damage;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<Attack>(_entity))
				{
					var atk = _world.Get<Attack>(_entity);
					_world.Set(_entity, new Attack(value, atk.Range, atk.Cooldown));
				}
			}
		}

		public float Range
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Attack>(_entity))
				{
					return _world.Get<Attack>(_entity).Range;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<Attack>(_entity))
				{
					var atk = _world.Get<Attack>(_entity);
					_world.Set(_entity, new Attack(atk.Damage, value, atk.Cooldown));
				}
			}
		}

		public float Armor
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Armor>(_entity))
				{
					return _world.Get<Armor>(_entity).Value;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<Armor>(_entity))
				{
					_world.Set(_entity, new Armor(value));
				}
			}
		}

		public float Speed
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<MovementStats>(_entity))
				{
					return _world.Get<MovementStats>(_entity).Speed;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (_world.Has<MovementStats>(_entity))
				{
					var mv = _world.Get<MovementStats>(_entity);
					_world.Set(_entity, new MovementStats(value, mv.Acceleration, mv.TurnRate));
				}
			}
		}

		public bool IsHero
		{
			get
			{
				if (!_world.IsAlive(_entity)) return false;
				return _world.Has<Realm.Ecs.Components.Tags.Hero>(_entity);
			}
		}

		public int Level
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 1;
				if (_world.Has<Realm.Ecs.Components.Meta.Level>(_entity))
				{
					return _world.Get<Realm.Ecs.Components.Meta.Level>(_entity).Value;
				}
				return 1;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				var levelComp = new Realm.Ecs.Components.Meta.Level(value);
				if (_world.Has<Realm.Ecs.Components.Meta.Level>(_entity))
				{
					_world.Set(_entity, levelComp);
				}
				else
				{
					_world.Add(_entity, levelComp);
				}
			}
		}

		public float Experience
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Realm.Ecs.Components.Meta.Experience>(_entity))
				{
					return _world.Get<Realm.Ecs.Components.Meta.Experience>(_entity).Value;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				var expComp = new Realm.Ecs.Components.Meta.Experience(value);
				if (_world.Has<Realm.Ecs.Components.Meta.Experience>(_entity))
				{
					_world.Set(_entity, expComp);
				}
				else
				{
					_world.Add(_entity, expComp);
				}
			}
		}

		public int Potions
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0;
				if (_world.Has<Inventory>(_entity))
				{
					return _world.Get<Inventory>(_entity).Potions;
				}
				return 0;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				var invComp = new Inventory(value);
				if (_world.Has<Inventory>(_entity))
				{
					_world.Set(_entity, invComp);
				}
				else
				{
					_world.Add(_entity, invComp);
				}
			}
		}

		public float XpBounty
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (UnitRegistry.TryGetValue(UnitId, out var meta))
				{
					return meta.XpBounty;
				}
				return 0f;
			}
		}

		public float GoldBounty
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (UnitRegistry.TryGetValue(UnitId, out var meta))
				{
					return meta.GoldBounty;
				}
				return 0f;
			}
		}

		public bool IsDead
		{
			get
			{
				if (!_world.IsAlive(_entity)) return true;
				return _world.Has<Dead>(_entity);
			}
		}

		public void MoveTo(System.Numerics.Vector3 destination)
		{
			if (!_world.IsAlive(_entity) || IsDead) return;
			var mv = new MoveTo(destination);
			if (_world.Has<MoveTo>(_entity))
			{
				_world.Set(_entity, mv);
			}
			else
			{
				_world.Add(_entity, mv);
			}
		}

		public void AttackMove(System.Numerics.Vector3 destination)
		{
			if (!_world.IsAlive(_entity) || IsDead) return;
			var am = new Realm.Ecs.Components.Movement.AttackMove(destination);
			if (_world.Has<Realm.Ecs.Components.Movement.AttackMove>(_entity))
			{
				_world.Set(_entity, am);
			}
			else
			{
				_world.Add(_entity, am);
			}
			var mv = new MoveTo(destination);
			if (_world.Has<MoveTo>(_entity))
			{
				_world.Set(_entity, mv);
			}
			else
			{
				_world.Add(_entity, mv);
			}
		}

		public void Attack(IUnit target)
		{
			if (!_world.IsAlive(_entity) || IsDead || target == null) return;
			if (target is UnitWrapper wrapper && _world.IsAlive(wrapper.Entity))
			{
				var at = new AttackTarget(wrapper.Entity);
				if (_world.Has<AttackTarget>(_entity))
				{
					_world.Set(_entity, at);
				}
				else
				{
					_world.Add(_entity, at);
				}
			}
		}

		public void Gather(IResourceNode resourceNode)
		{
			if (!_world.IsAlive(_entity) || IsDead || resourceNode == null) return;
			if (resourceNode is ResourceNodeWrapper nodeWrapper)
			{
				var propNode = nodeWrapper.Prop;
				if (GodotObject.IsInstanceValid(propNode))
				{
					var gatherer = new Gatherer(resourceNode.ResourceType, propNode);
					if (_world.Has<Gatherer>(_entity))
					{
						_world.Set(_entity, gatherer);
					}
					else
					{
						_world.Add(_entity, gatherer);
					}
				}
			}
		}

		public void Teleport(System.Numerics.Vector3 position)
		{
			if (!_world.IsAlive(_entity)) return;

			var newPosComp = new Position(position);
			if (_world.Has<Position>(_entity))
			{
				_world.Set(_entity, newPosComp);
			}
			else
			{
				_world.Add(_entity, newPosComp);
			}

			if (_world.Has<Unit3D>(_entity))
			{
				var unit3D = _world.Get<Unit3D>(_entity);
				if (GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.GlobalPosition = new Vector3(position.X, position.Y, position.Z);
				}
			}
		}

		public float Mana
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Realm.Ecs.Components.Core.Mana>(_entity))
				{
					return _world.Get<Realm.Ecs.Components.Core.Mana>(_entity).Current;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				float max = MaxMana;
				var val = Math.Max(0f, value);
				if (_world.Has<Realm.Ecs.Components.Core.Mana>(_entity))
				{
					_world.Set(_entity, new Realm.Ecs.Components.Core.Mana(val, max));
				}
				else
				{
					_world.Add(_entity, new Realm.Ecs.Components.Core.Mana(val, max));
				}
			}
		}

		public float MaxMana
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 0f;
				if (_world.Has<Realm.Ecs.Components.Core.Mana>(_entity))
				{
					return _world.Get<Realm.Ecs.Components.Core.Mana>(_entity).Max;
				}
				return 0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				float current = Mana;
				var val = Math.Max(0f, value);
				if (_world.Has<Realm.Ecs.Components.Core.Mana>(_entity))
				{
					_world.Set(_entity, new Realm.Ecs.Components.Core.Mana(current, val));
				}
				else
				{
					_world.Add(_entity, new Realm.Ecs.Components.Core.Mana(current, val));
				}
			}
		}

		public float Scale
		{
			get
			{
				if (!_world.IsAlive(_entity)) return 1.0f;
				if (_world.Has<Unit3D>(_entity))
				{
					var u3d = _world.Get<Unit3D>(_entity);
					if (GodotObject.IsInstanceValid(u3d))
					{
						return u3d.Scale.X;
					}
				}
				return GameHost.Instance._unitScale.TryGetValue(_entity.Id, out float val) ? val : 1.0f;
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				GameHost.Instance._unitScale[_entity.Id] = value;
				if (_world.Has<Unit3D>(_entity))
				{
					var u3d = _world.Get<Unit3D>(_entity);
					if (GodotObject.IsInstanceValid(u3d))
					{
						u3d.Scale = new Vector3(value, value, value);
					}
				}
			}
		}

		public bool Invulnerable
		{
			get
			{
				if (!_world.IsAlive(_entity)) return false;
				return _world.Has<Realm.Ecs.Components.Tags.Invulnerable>(_entity);
			}
			set
			{
				if (!_world.IsAlive(_entity)) return;
				if (value)
				{
					if (!_world.Has<Realm.Ecs.Components.Tags.Invulnerable>(_entity))
					{
						_world.Add<Realm.Ecs.Components.Tags.Invulnerable>(_entity);
					}
				}
				else
				{
					if (_world.Has<Realm.Ecs.Components.Tags.Invulnerable>(_entity))
					{
						_world.Remove<Realm.Ecs.Components.Tags.Invulnerable>(_entity);
					}
				}
			}
		}

		public void Stop()
		{
			if (!_world.IsAlive(_entity)) return;
			GameHost.Instance.ClearUnitOrders(_entity);
			if (_world.Has<Unit3D>(_entity))
			{
				var u3d = _world.Get<Unit3D>(_entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					u3d.Velocity = Vector3.Zero;
				}
			}
		}

		public void HoldPosition()
		{
			if (!_world.IsAlive(_entity)) return;
			Stop();
			if (!_world.Has<Realm.Ecs.Components.Movement.HoldPosition>(_entity))
			{
				_world.Add<Realm.Ecs.Components.Movement.HoldPosition>(_entity);
			}
		}

		public void Stun(float duration)
		{
			AddBuff("stun", duration);
			Stop();
		}

		public void Silence(float duration)
		{
			AddBuff("silence", duration);
		}

		public bool AddItem(string itemId)
		{
			if (!_world.IsAlive(_entity)) return false;
			List<string> items;
			if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
			{
				items = _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
			}
			else
			{
				items = new List<string>();
				_world.Add(_entity, new Realm.Ecs.Components.Core.UnitItems(items));
			}
			if (items.Count >= 6) return false;
			items.Add(itemId);
			return true;
		}

		public bool RemoveItem(string itemId)
		{
			if (!_world.IsAlive(_entity)) return false;
			if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
			{
				var items = _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
				return items.Remove(itemId);
			}
			return false;
		}

		public bool HasItem(string itemId)
		{
			if (!_world.IsAlive(_entity)) return false;
			if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
			{
				var items = _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
				return items.Contains(itemId);
			}
			return false;
		}

		public IEnumerable<string> GetItems()
		{
			if (!_world.IsAlive(_entity)) return Array.Empty<string>();
			if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
			}
			return Array.Empty<string>();
		}

		public void AddBuff(string buffId, float duration)
		{
			if (!_world.IsAlive(_entity)) return;
			Dictionary<string, float> buffs;
			if (_world.Has<Realm.Ecs.Components.Core.Buffs>(_entity))
			{
				buffs = _world.Get<Realm.Ecs.Components.Core.Buffs>(_entity).Value;
			}
			else
			{
				buffs = new Dictionary<string, float>();
				_world.Add(_entity, new Realm.Ecs.Components.Core.Buffs(buffs));
			}
			buffs[buffId] = duration;
		}

		public void RemoveBuff(string buffId)
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Realm.Ecs.Components.Core.Buffs>(_entity))
			{
				var buffs = _world.Get<Realm.Ecs.Components.Core.Buffs>(_entity).Value;
				buffs.Remove(buffId);
			}
		}

		public bool HasBuff(string buffId)
		{
			if (!_world.IsAlive(_entity)) return false;
			if (_world.Has<Realm.Ecs.Components.Core.Buffs>(_entity))
			{
				var buffs = _world.Get<Realm.Ecs.Components.Core.Buffs>(_entity).Value;
				return buffs.ContainsKey(buffId);
			}
			return false;
		}

		public void SetCustomData(string key, object value)
		{
			if (!_world.IsAlive(_entity)) return;
			Dictionary<string, object> dict;
			if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
			{
				dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
			}
			else
			{
				dict = new Dictionary<string, object>();
				_world.Add(_entity, new Realm.Ecs.Components.Core.CustomMetadata(dict));
			}
			dict[key] = value;
		}

		public object? GetCustomData(string key)
		{
			if (!_world.IsAlive(_entity)) return null;
			if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
			{
				var dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
				return dict.TryGetValue(key, out var val) ? val : null;
			}
			return null;
		}

		public bool RemoveCustomData(string key)
		{
			if (!_world.IsAlive(_entity)) return false;
			if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
			{
				var dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
				return dict.Remove(key);
			}
			return false;
		}

		public bool HasCustomData(string key)
		{
			if (!_world.IsAlive(_entity)) return false;
			if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
			{
				var dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
				return dict.ContainsKey(key);
			}
			return false;
		}
	}

	private class ResourceNodeWrapper : IResourceNode
	{
		private readonly Prop3D _prop;

		public ResourceNodeWrapper(Prop3D prop)
		{
			_prop = prop;
		}

		public Prop3D Prop => _prop;

		public string ResourceType
		{
			get
			{
				if (!GodotObject.IsInstanceValid(_prop)) return string.Empty;
				return _prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => _prop.PropId ?? string.Empty
				};
			}
		}

		public System.Numerics.Vector3 Position
		{
			get
			{
				if (!GodotObject.IsInstanceValid(_prop)) return System.Numerics.Vector3.Zero;
				var pos = _prop.GlobalPosition;
				return new System.Numerics.Vector3(pos.X, pos.Y, pos.Z);
			}
		}

		public float ResourceAmount
		{
			get
			{
				if (!GodotObject.IsInstanceValid(_prop)) return 0f;
				return _prop.ResourceAmount;
			}
			set
			{
				if (!GodotObject.IsInstanceValid(_prop)) return;
				_prop.ResourceAmount = value;
			}
		}

		public bool IsDepleted
		{
			get
			{
				return !GodotObject.IsInstanceValid(_prop) || _prop.ResourceAmount <= 0f;
			}
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
			Type[] types;
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

	private double _enemyAiTimer = 0.0;
	private double _enemySpawnTimer = 0.0;

	private List<Unit3D>[] _controlGroups = new List<Unit3D>[10];
	public List<Unit3D>[] ControlGroups => _controlGroups;
	private double[] _lastGroupPressTime = new double[10];

	// Marquee drag selection variables
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

		CreateGround();
		SetupSkybox();
		UpdateDayNightVisuals(0.5f);

		// 2. Initialize Arch ECS
		EcsWorld = World.Create();

		if (_multiplayerActive)
		{
			_localPeerId = Multiplayer.GetUniqueId();
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
				_peerIdToPlayerEntityMap[1] = _playerEntity;

				_enemyPlayerEntity = EcsWorld.Create();
				EcsWorld.Add(_enemyPlayerEntity, new Player());
				EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
				_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;
			}
		}
		else
		{
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
			_peerIdToPlayerEntityMap[1] = _playerEntity;

			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
			_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;
		}

		// 4. Load map and script at runtime
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

		// 5. Connect UI (wait briefly for UIManager to load InGameHUD)
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
		if (Instance == this) Instance = null;
		EcsWorld?.Dispose();
		if (OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.CleanUp();
		}
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

		// 1. Spawning Friendly Units & Buildings (Player owned)
		// Worker
		var workerEntity = CreateEcsUnit("worker", "Worker", 70f, 5f, 1.8f, 0f, 7.0f, new Vector3(-16, 0, -20), playerOwner);
		SpawnUnit3D(workerEntity, "worker", GetFallbackModelPath("worker", false), new Vector3(-16, 0, -20), false, false);

		// Soldier
		var soldierEntity = CreateEcsUnit("soldier", "Soldier", 150f, 15f, 2.0f, 5f, 6.0f, new Vector3(-8, 0, 5), playerOwner);
		var soldier3D = SpawnUnit3D(soldierEntity, "soldier", GetFallbackModelPath("soldier", false), new Vector3(-8, 0, 5), false, false);
		
		// Elf Archer
		var archerEntity = CreateEcsUnit("archer", "Elf Archer", 90f, 12f, 18.0f, 2f, 8.0f, new Vector3(-12, 0, 5), playerOwner);
		var archer3D = SpawnUnit3D(archerEntity, "archer", GetFallbackModelPath("archer", false), new Vector3(-12, 0, 5), false, false);

		// Castle (Base)
		var castleEntity = CreateEcsUnit("castle", "Town Castle", 1000f, 0f, 0f, 15f, 0f, new Vector3(-25, 0, -25), playerOwner);
		var castle3D = SpawnUnit3D(castleEntity, "castle", GetFallbackModelPath("castle", true), new Vector3(-25, 0, -25), true, false);

		// Spell Tower (Defense)
		var towerEntity = CreateEcsUnit("tower", "Spell Tower", 500f, 25f, 25.0f, 8f, 0f, new Vector3(-15, 0, -15), playerOwner);
		var tower3D = SpawnUnit3D(towerEntity, "tower", GetFallbackModelPath("tower", true), new Vector3(-15, 0, -15), true, false);

		// 2. Spawning Enemy Units & Buildings (Enemy owned)
		// Enemy Worker
		var enemyWorkerEntity = CreateEcsUnit("worker", "Orc Worker", 70f, 5f, 1.8f, 0f, 7.0f, new Vector3(16, 0, 20), enemyOwner);
		SpawnUnit3D(enemyWorkerEntity, "worker", GetFallbackModelPath("worker", false), new Vector3(16, 0, 20), false, true);

		// Set enemy worker to harvest their nearest goldmine
		var enemyGoldmine = FindNearbyResourceNode(new Vector3(16, 0, 20), "gold", 50f);
		if (enemyGoldmine != null)
		{
			var gatherer = new Gatherer("gold", enemyGoldmine);
			EcsWorld.Add(enemyWorkerEntity, gatherer);
		}

		// Enemy Soldier
		var enemySoldierEntity = CreateEcsUnit("soldier", "Orc Raider", 150f, 15f, 2.0f, 5f, 6.0f, new Vector3(15, 0, 10), enemyOwner);
		var enemySoldier3D = SpawnUnit3D(enemySoldierEntity, "soldier", GetFallbackModelPath("soldier", false), new Vector3(15, 0, 10), false, true);

		// Enemy Archer
		var enemyArcherEntity = CreateEcsUnit("archer", "Dark Archer", 90f, 12f, 18.0f, 2f, 8.0f, new Vector3(20, 0, 15), enemyOwner);
		var enemyArcher3D = SpawnUnit3D(enemyArcherEntity, "archer", GetFallbackModelPath("archer", false), new Vector3(20, 0, 15), false, true);

		// Enemy Spell Tower
		var enemyTowerEntity = CreateEcsUnit("tower", "Orc Totem Tower", 500f, 25f, 25.0f, 8f, 0f, new Vector3(25, 0, 5), enemyOwner);
		var enemyTower3D = SpawnUnit3D(enemyTowerEntity, "tower", GetFallbackModelPath("tower", true), new Vector3(25, 0, 5), true, true);

		// Enemy Castle (Orc Stronghold)
		var enemyCastleEntity = CreateEcsUnit("castle", "Orc Stronghold", 1000f, 0f, 0f, 15f, 0f, new Vector3(25, 0, 25), enemyOwner);
		var enemyCastle3D = SpawnUnit3D(enemyCastleEntity, "castle", GetFallbackModelPath("castle", true), new Vector3(25, 0, 25), true, true);
	}

	private void SpawnDefaultResourceNodes()
	{
		// Player-side resources
		SpawnPropExternal("goldmine", new Vector3(-35f, 0f, -15f));
		SpawnPropExternal("tree", new Vector3(-18f, 0f, -35f));
		SpawnPropExternal("tree", new Vector3(-22f, 0f, -36f));
		SpawnPropExternal("tree", new Vector3(-26f, 0f, -34f));
		SpawnPropExternal("rock", new Vector3(-36f, 0f, -32f));
		SpawnPropExternal("rock", new Vector3(-32f, 0f, -35f));

		// Enemy-side resources
		SpawnPropExternal("goldmine", new Vector3(35f, 0f, 15f));
		SpawnPropExternal("tree", new Vector3(18f, 0f, 35f));
		SpawnPropExternal("tree", new Vector3(22f, 0f, 36f));
		SpawnPropExternal("tree", new Vector3(26f, 0f, 34f));
		SpawnPropExternal("rock", new Vector3(36f, 0f, 32f));
		SpawnPropExternal("rock", new Vector3(32f, 0f, 35f));

		// Center/Neutral contention resources
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

	private void ClearUnitOrders(Entity entity)
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

	private Prop3D FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Prop3D closest = null;
		float closestDist = radius;
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
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

			// Clear other contradictory behaviors
			ClearUnitOrders(unit.Entity);

			// Add Gatherer component
			var gatherer = new Gatherer(resType, prop);
			if (EcsWorld.Has<Gatherer>(unit.Entity))
				EcsWorld.Set(unit.Entity, gatherer);
			else
				EcsWorld.Add(unit.Entity, gatherer);

			// Move to the resource node
			var moveTo = new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z));
			EcsWorld.Add(unit.Entity, moveTo);
		}
	}

	private Entity CreateEcsUnit(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner)
	{
		// Look up cooldown from registry (fallback 1.5s)
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
		
		// Apply player upgrades
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

		OnUnitCreated?.Invoke(new UnitWrapper(entity, EcsWorld));
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

		// Update ECS node binding
		EcsWorld.Add(entity, unit3D); // Store Unit3D as component for easy mapping

		// Track population for player units
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
			if (_clumpSpawnCooldown > 0.0f)
			{
				_clumpSpawnCooldown -= fDelta;
			}

			var mousePos = GetViewport().GetMousePosition();
			var hit = RaycastFromMouse(mousePos);
			if (hit != null && hit.ContainsKey("position"))
			{
				Vector3 hitPos = hit["position"].AsVector3();
				UpdateBrushIndicator(hitPos);
				UpdateEditorPreview(hitPos);
				if (GroundTerrain != null)
				{
					if (ActiveEditorTool == EditorTool.SelectArea && _isSelectingArea && _selectionStart != null)
					{
						float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
						float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
						int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
						int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
						_selectionEnd = new Vector2I(cx, cz);
						int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
						int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
						int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
						int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
						CreateSelectionHighlight();
						RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
					}
					else if (ActiveEditorTool == EditorTool.PasteArea && _copiedArea != null)
					{
						float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
						float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
						int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
						int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
						int minX = cx;
						int minZ = cz;
						int maxX = Mathf.Min(minX + _copiedArea.Width - 1, GroundTerrain.Width - 1);
						int maxZ = Mathf.Min(minZ + _copiedArea.Depth - 1, GroundTerrain.Depth - 1);
						CreateSelectionHighlight();
						RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
					}
				}
				
				bool canHover = (ActiveEditorTool == EditorTool.SelectMove && !_isDraggingObject) ||
								ActiveEditorTool == EditorTool.DeleteObject ||
								ActiveEditorTool == EditorTool.Eyedropper;
				Node newHovered = null;
				if (canHover && !IsMouseOverUI())
				{
					var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
					if (collider != null)
					{
						newHovered = FindUnit3DInParentChain(collider);
						if (newHovered == null)
						{
							newHovered = FindProp3DInParentChain(collider);
						}
					}
					if (newHovered == null)
					{
						Decal closestDecal = null;
						float closestDist = 3.0f;
						foreach (var child in GetChildren())
						{
							if (child is Decal dec && GodotObject.IsInstanceValid(dec))
							{
								float d = dec.GlobalPosition.DistanceTo(hitPos);
								if (d < closestDist)
								{
									closestDist = d;
									closestDecal = dec;
								}
							}
						}
						if (closestDecal != null)
						{
							newHovered = closestDecal;
						}
					}
				}

				if (_hoveredEditorObject != newHovered)
				{
					if (GodotObject.IsInstanceValid(_hoveredEditorObject))
					{
						if (_hoveredEditorObject is Unit3D u) u.IsHovered = false;
						else if (_hoveredEditorObject is Prop3D p) p.IsHovered = false;
						else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, false);
					}
					_hoveredEditorObject = newHovered;
					if (GodotObject.IsInstanceValid(_hoveredEditorObject))
					{
						if (_hoveredEditorObject is Unit3D u) u.IsHovered = true;
						else if (_hoveredEditorObject is Prop3D p) p.IsHovered = true;
						else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, true);
					}
				}

				if (Input.IsMouseButtonPressed(MouseButton.Left) && !IsMouseOverUI())
				{
					if ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode)
					{
						if (!_isDrawingClump)
						{
							_isDrawingClump = true;
							_clumpSpawnActionsInSession.Clear();
						}
						if (_clumpSpawnCooldown <= 0.0f)
						{
							ApplyGeneralClumpSpawn(hitPos);
							_clumpSpawnCooldown = 0.15f;
						}
					}

					if (ActiveEditorTool == EditorTool.SelectMove && _isDraggingObject && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						if (!_dragObjectHasMoved && hitPos.DistanceTo(_dragObjectStartHitPos) > 0.3f)
						{
							_dragObjectHasMoved = true;
						}

						if (_dragObjectHasMoved)
						{
							var node3D = SelectedEditorObject as Node3D;
							var dragPos = hitPos - (_dragObjectStartHitPos - _dragObjectStartPos);
							if (EditorSnapToGrid && GroundTerrain != null)
							{
								float spacing = GroundTerrain.Spacing;
								int width = GroundTerrain.Width;
								int depth = GroundTerrain.Depth;
								float fx = Mathf.Round(dragPos.X / spacing + (width - 1) / 2.0f);
								dragPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
								float fz = Mathf.Round(dragPos.Z / spacing + (depth - 1) / 2.0f);
								dragPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
							}
							dragPos.Y = GetTerrainHeightAt(dragPos);
							node3D.Position = dragPos;
							if (SelectedEditorObject is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
							{
								EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
							}
							MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						}
					}

					bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
										 ActiveEditorTool == EditorTool.Lower ||
										 ActiveEditorTool == EditorTool.Flatten ||
										 ActiveEditorTool == EditorTool.Smooth ||
										 ActiveEditorTool == EditorTool.Cliff ||
										 ActiveEditorTool == EditorTool.PaintGrass ||
										 ActiveEditorTool == EditorTool.PaintDirt ||
										 ActiveEditorTool == EditorTool.PaintRock ||
										 ActiveEditorTool == EditorTool.PaintSand ||
										 ActiveEditorTool == EditorTool.Noise ||
										 ActiveEditorTool == EditorTool.PaintPathing;

					if (isTerrainTool && !_isDrawingTerrain && GroundTerrain != null)
					{
						_isDrawingTerrain = true;
						_terrainHeightsBefore = (float[,])GroundTerrain.Heights.Clone();
						_terrainColorsBefore = (Color[,])GroundTerrain.Colors.Clone();
						_terrainPathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

						if (ActiveEditorTool == EditorTool.Flatten)
						{
							EditorFlattenHeight = GetMinHeightInBrushBounds(hitPos);
							MapEditorHUD.Instance?.UpdateFlattenHeightExternal(EditorFlattenHeight);
						}

						if (EditorBlockMode)
						{
							float startHeight = GetTerrainHeightAt(hitPos);
							if (ActiveEditorTool == EditorTool.Raise)
							{
								_activeBlockTargetHeight = (Mathf.Floor(startHeight / EditorBlockLevelHeight) + 1.0f) * EditorBlockLevelHeight;
								_hasBlockTargetHeight = true;
							}
							else if (ActiveEditorTool == EditorTool.Lower)
							{
								_activeBlockTargetHeight = (Mathf.Ceil(startHeight / EditorBlockLevelHeight) - 1.0f) * EditorBlockLevelHeight;
								_hasBlockTargetHeight = true;
							}
							else if (ActiveEditorTool == EditorTool.Flatten)
							{
								_activeBlockTargetHeight = Mathf.Round(EditorFlattenHeight / EditorBlockLevelHeight) * EditorBlockLevelHeight;
								_hasBlockTargetHeight = true;
							}
							else if (ActiveEditorTool == EditorTool.Cliff)
							{
								bool lower = Input.IsKeyPressed(Key.Shift);
								if (lower)
									_activeBlockTargetHeight = (Mathf.Ceil(startHeight / EditorBlockLevelHeight) - 1.0f) * EditorBlockLevelHeight;
								else
									_activeBlockTargetHeight = (Mathf.Floor(startHeight / EditorBlockLevelHeight) + 1.0f) * EditorBlockLevelHeight;
								_hasBlockTargetHeight = true;
							}
						}
					}

					if (ActiveEditorTool == EditorTool.Cliff && _activeCliffHeight == null && !EditorBlockMode)
					{
						float startHeight = GetTerrainHeightAt(hitPos);
						bool lower = Input.IsKeyPressed(Key.Shift);
						if (lower)
							_activeCliffHeight = (Mathf.Ceil(startHeight / 4.0f) - 1.0f) * 4.0f;
						else
							_activeCliffHeight = (Mathf.Floor(startHeight / 4.0f) + 1.0f) * 4.0f;
					}
					ApplyContinuousTerrainEditing(hitPos, fDelta);
					if (EditorMirrorMode != MirrorMode.None)
					{
						foreach (var t in GetMirroredTransforms(hitPos, 0.0f))
						{
							ApplyContinuousTerrainEditing(t.Position, fDelta);
						}
					}
				}
				else
				{
					if (_isDrawingClump)
					{
						_isDrawingClump = false;
						if (_clumpSpawnActionsInSession.Count > 0)
						{
							var composite = new CompositeAction(_clumpSpawnActionsInSession);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
					}

					_activeCliffHeight = null;
					_hasBlockTargetHeight = false;
					if (_isDraggingObject)
					{
						_isDraggingObject = false;
						if (GodotObject.IsInstanceValid(SelectedEditorObject))
						{
							var node3D = SelectedEditorObject as Node3D;
							bool isUnit = SelectedEditorObject is Unit3D;
							bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
							if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
							{
								var action = new ObjectTransformAction(
									node3D,
									_dragObjectStartPos, node3D.Position,
									_dragObjectStartRot, node3D.RotationDegrees,
									_dragObjectStartScale, node3D.Scale,
									_dragObjectStartIsEnemy, isEnemy
								);
								EditorHistoryManager.RecordAction(action);
								MapEditorHUD.Instance?.ShowFeedbackExternal("Moved Object");
								EditorHasUnsavedChanges = true;
							}
						}
					}
					if (_isSelectingArea)
					{
						_isSelectingArea = false;
					}
					if (_isDrawingTerrain)
					{
						_isDrawingTerrain = false;
						if (GroundTerrain != null)
						{
							var currentHeights = (float[,])GroundTerrain.Heights.Clone();
							var currentColors = (Color[,])GroundTerrain.Colors.Clone();
							var currentPathing = (int[,])GroundTerrain.PathingCodes.Clone();
							var action = new TerrainModifyAction(_terrainHeightsBefore, currentHeights, _terrainColorsBefore, currentColors, _terrainPathingBefore, currentPathing);
							EditorHistoryManager.RecordAction(action);
							bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
												 ActiveEditorTool == EditorTool.Lower ||
												 ActiveEditorTool == EditorTool.Flatten ||
												 ActiveEditorTool == EditorTool.Smooth ||
												 ActiveEditorTool == EditorTool.Cliff ||
												 ActiveEditorTool == EditorTool.Noise ||
												 ActiveEditorTool == EditorTool.PaintPathing;
							if (isHeightsTool)
							{
								GroundTerrain.BakeNavMesh();
								RebuildGridOverlayMeshExternal();
							}
							EditorHasUnsavedChanges = true;
						}
					}
				}
			}
			else
			{
				if (_brushIndicatorMesh != null)
					_brushIndicatorMesh.Visible = false;
				ClearEditorPreview();
				if (_isDraggingObject)
				{
					_isDraggingObject = false;
					if (GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
						{
							var action = new ObjectTransformAction(
								node3D,
								_dragObjectStartPos, node3D.Position,
								_dragObjectStartRot, node3D.RotationDegrees,
								_dragObjectStartScale, node3D.Scale,
								_dragObjectStartIsEnemy, isEnemy
							);
							EditorHistoryManager.RecordAction(action);
							EditorHasUnsavedChanges = true;
						}
					}
				}
				if (_isDrawingClump)
				{
					_isDrawingClump = false;
					if (_clumpSpawnActionsInSession.Count > 0)
					{
						var composite = new CompositeAction(_clumpSpawnActionsInSession);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}
				}
				if (_isSelectingArea)
				{
					_isSelectingArea = false;
				}
				if (_isDrawingTerrain)
				{
					_isDrawingTerrain = false;
					if (GroundTerrain != null)
					{
						var currentHeights = (float[,])GroundTerrain.Heights.Clone();
						var currentColors = (Color[,])GroundTerrain.Colors.Clone();
						var currentPathing = (int[,])GroundTerrain.PathingCodes.Clone();
						var action = new TerrainModifyAction(_terrainHeightsBefore, currentHeights, _terrainColorsBefore, currentColors, _terrainPathingBefore, currentPathing);
						EditorHistoryManager.RecordAction(action);
						bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
											 ActiveEditorTool == EditorTool.Lower ||
											 ActiveEditorTool == EditorTool.Flatten ||
											 ActiveEditorTool == EditorTool.Smooth ||
											 ActiveEditorTool == EditorTool.Cliff ||
											 ActiveEditorTool == EditorTool.Noise ||
											 ActiveEditorTool == EditorTool.PaintPathing;
						if (isHeightsTool)
						{
							GroundTerrain.BakeNavMesh();
							RebuildGridOverlayMeshExternal();
						}
						EditorHasUnsavedChanges = true;
					}
				}
			}
			
			// Process unit movement only to sync positions/gravity (e.g. if units are spawned)
			// But skip AI, combat, production, etc.
			ProcessMapEditorPhysics(fDelta);
			return;
		}

		// 0a. Game clock, spell cooldowns & day/night cycle
		GameElapsedTime += fDelta;
		if (_fireballCooldown > 0) _fireballCooldown = Math.Max(0, _fireballCooldown - fDelta);
		if (_lightningCooldown > 0) _lightningCooldown = Math.Max(0, _lightningCooldown - fDelta);
		if (_holyLightCooldown > 0) _holyLightCooldown = Math.Max(0, _holyLightCooldown - fDelta);
		if (_underAttackAlertTimer > 0) _underAttackAlertTimer -= fDelta;

		// Update buff durations
		var buffQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.Buffs>().WithNone<Dead>();
		EcsWorld.Query(in buffQuery, (Entity entity, ref Realm.Ecs.Components.Core.Buffs buffs) =>
		{
			var buffsDict = buffs.Value;
			var expired = new List<string>();
			var keys = new List<string>(buffsDict.Keys);
			foreach (var key in keys)
			{
				float newTime = buffsDict[key] - fDelta;
				if (newTime <= 0) expired.Add(key);
				else buffsDict[key] = newTime;
			}
			foreach (var expKey in expired)
			{
				buffsDict.Remove(expKey);
			}
		});

		if (!IsMapEditorMode && _dayNightCycleEnabled)
		{
			// Advance time based on game delta relative to total cycle duration
			_timeOfDayTimer += fDelta;
			if (_timeOfDayTimer >= TimeOfDayCycleDuration)
			{
				_timeOfDayTimer -= TimeOfDayCycleDuration;
			}

			float progress = _timeOfDayTimer / TimeOfDayCycleDuration; // Normalized 0.0 to 1.0 across full cycle
			
			// Dynamically determine index based on the 4 cycle quadrants for external state readers
			float currentHour = progress * 24f;
			if (currentHour >= 5f && currentHour < 6f) _timeOfDayIndex = 3;      // Dawn
			else if (currentHour >= 6f && currentHour < 18f) _timeOfDayIndex = 0; // Day
			else if (currentHour >= 18f && currentHour < 20f) _timeOfDayIndex = 1;// Sunset
			else _timeOfDayIndex = 2;                                             // Night

			UpdateDayNightVisuals(progress);
		}
		// 0b. Patrol System: reverse direction when destination is reached
		var patrolArrivalQuery = new QueryDescription().WithAll<Patrol, Position>().WithNone<Dead, AttackTarget>();
		var patrolToFlip = new List<(Entity, Patrol)>();
		EcsWorld.Query(in patrolArrivalQuery, (Entity entity, ref Patrol patrol, ref Position pos) =>
		{
			var current = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			var dest = patrol.GoingToB
				? new Vector3(patrol.PointB.X, patrol.PointB.Y, patrol.PointB.Z)
				: new Vector3(patrol.PointA.X, patrol.PointA.Y, patrol.PointA.Z);
			if (current.DistanceTo(dest) < 1.5f)
			{
				patrolToFlip.Add((entity, patrol));
			}
		});
		foreach (var (entity, patrol) in patrolToFlip)
		{
			var flipped = new Patrol(patrol.PointA, patrol.PointB) { GoingToB = !patrol.GoingToB };
			EcsWorld.Set(entity, flipped);
			var newDest = flipped.GoingToB ? flipped.PointB : flipped.PointA;
			var moveTo = new MoveTo(new System.Numerics.Vector3(newDest.X, newDest.Y, newDest.Z));
			if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
			else EcsWorld.Add(entity, moveTo);
		}

		// 0c. Follow System: update MoveTo target to follow the tracked target's position
		var followQuery = new QueryDescription().WithAll<Follow, Position>().WithNone<Dead>();
		var followToStop = new List<Entity>();
		var followToMove = new List<(Entity Follower, Vector3 TargetPos)>();
		EcsWorld.Query(in followQuery, (Entity entity, ref Follow follow, ref Position pos) =>
		{
			if (!EcsWorld.IsAlive(follow.Target) || EcsWorld.Has<Dead>(follow.Target))
			{
				followToStop.Add(entity);
				return;
			}

			var targetPosComp = EcsWorld.Get<Position>(follow.Target);
			var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

			float dist = currentPos.DistanceTo(targetPos);
			if (dist <= 3.0f) // Keep some distance when following
			{
				followToStop.Add(entity);
			}
			else
			{
				followToMove.Add((entity, targetPos));
			}
		});

		foreach (var ent in followToStop)
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

		foreach (var (ent, targetPos) in followToMove)
		{
			if (EcsWorld.IsAlive(ent))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (EcsWorld.Has<MoveTo>(ent)) EcsWorld.Set(ent, moveTo);
				else EcsWorld.Add(ent, moveTo);
			}
		}

		// 0d. Gathering System for Workers
		var gatherQuery = new QueryDescription().WithAll<Position, Gatherer>().WithNone<Dead>();
		var gatherersToUpdate = new List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)>();
		
		EcsWorld.Query(in gatherQuery, (Entity entity, ref Position pos, ref Gatherer gather) =>
		{
			var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			
			if (gather.ReturningToBase)
			{
				// Walk to nearest Castle
				Unit3D nearestCastle = null;
				float nearestDist = float.MaxValue;
				foreach (var u in AllUnits)
				{
					var wOwner = EcsWorld.Get<Owner>(entity).PlayerEntity;
					var uOwner = EcsWorld.Get<Owner>(u.Entity).PlayerEntity;
					if (uOwner == wOwner && u.UnitId == "castle" && GodotObject.IsInstanceValid(u))
					{
						float dist = currentPos.DistanceTo(u.GlobalPosition);
						if (dist < nearestDist)
						{
							nearestDist = dist;
							nearestCastle = u;
						}
					}
				}
				
				if (nearestCastle == null)
				{
					// No castle? Stop returning
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0;
					gatherersToUpdate.Add((entity, newState, null));
					return;
				}
				
				float castleRadius = 6.0f; // Castle is large
				if (currentPos.DistanceTo(nearestCastle.GlobalPosition) <= castleRadius)
				{
					// Arrived at Castle! Deposit resources
					float carry = gather.CarriedAmount;
					bool isEnemy = EcsWorld.Get<Owner>(entity).PlayerEntity == _enemyPlayerEntity.AsPlayerEntity(EcsWorld);
					
					if (!isEnemy && InGameHUD.Instance != null)
					{
						if (gather.ResourceType == "gold") InGameHUD.Instance.Gold = Math.Min(ResourceCap, InGameHUD.Instance.Gold + carry);
						else if (gather.ResourceType == "wood") InGameHUD.Instance.Wood = Math.Min(ResourceCap, InGameHUD.Instance.Wood + carry);
						else if (gather.ResourceType == "stone") InGameHUD.Instance.Stone = Math.Min(ResourceCap, InGameHUD.Instance.Stone + carry);
						
						// Show feedback on HUD
						string resType = gather.ResourceType;
						Callable.From(() => InGameHUD.Instance.ShowFeedbackText($"+{carry:F0} {resType.ToUpper()} deposited", new Color(0.2f, 0.9f, 0.4f))).CallDeferred();
					}
					
					// Return to harvest node
					if (gather.TargetNode != null && GodotObject.IsInstanceValid(gather.TargetNode))
					{
						var newState = gather;
						newState.ReturningToBase = false;
						newState.CarriedAmount = 0f;
						var dest = gather.TargetNode.GlobalPosition;
						gatherersToUpdate.Add((entity, newState, dest));
					}
					else
					{
						// Target node destroyed/missing? Stop gathering
						var newState = gather;
						newState.ReturningToBase = false;
						newState.CarriedAmount = 0f;
						newState.TargetNode = null;
						gatherersToUpdate.Add((entity, newState, null));
					}
				}
				else
				{
					// Keep walking to castle if not already headed there
					if (!EcsWorld.Has<MoveTo>(entity))
					{
						var dest = nearestCastle.GlobalPosition;
						gatherersToUpdate.Add((entity, gather, dest));
					}
				}
			}
			else
			{
				// Harvesting from node
				if (gather.TargetNode == null || !GodotObject.IsInstanceValid(gather.TargetNode))
				{
					// Node is gone, try to find another nearby node of the same type
					Prop3D alternate = FindNearbyResourceNode(currentPos, gather.ResourceType, 25.0f);
					if (alternate != null)
					{
						var newState = gather;
						newState.TargetNode = alternate;
						var dest = alternate.GlobalPosition;
						gatherersToUpdate.Add((entity, newState, dest));
					}
					else
					{
						// No other nodes, stop gathering
						Callable.From(() => {
							if (EcsWorld.IsAlive(entity)) ClearUnitOrders(entity);
						}).CallDeferred();
					}
					return;
				}
				
				float dist = currentPos.DistanceTo(gather.TargetNode.GlobalPosition);
				float gatherRange = 3.5f; // goldmine base is large
				if (dist <= gatherRange)
				{
					// Stop walking
					if (EcsWorld.Has<MoveTo>(entity))
					{
						Callable.From(() => {
							if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
							if (EcsWorld.Has<Unit3D>(entity)) EcsWorld.Get<Unit3D>(entity).Velocity = Vector3.Zero;
						}).CallDeferred();
					}
					
					// Mine!
					var newState = gather;
					float mineRate = 4.0f * fDelta; // gather 4 per second
					
					bool isEnemy = EcsWorld.Get<Owner>(entity).PlayerEntity == _enemyPlayerEntity.AsPlayerEntity(EcsWorld);
					// Harvesting upgrade gives +50% rate to player
					if (!isEnemy && HasHarvestingUpgrade) mineRate *= 1.5f;
					
					// Limit mineRate to remaining amount in node
					float nodeRemaining = gather.TargetNode.ResourceAmount;
					if (mineRate > nodeRemaining)
					{
						mineRate = nodeRemaining;
					}
					
					gather.TargetNode.ResourceAmount -= mineRate;
					newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);
					
					// Simple visual scaling pulse to indicate gathering action
					if (EcsWorld.Has<Unit3D>(entity))
					{
						var worker3D = EcsWorld.Get<Unit3D>(entity);
						float pulse = 1.0f + Mathf.Sin(GameElapsedTime * 10f) * 0.1f;
						worker3D.Scale = new Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
					}
					
					if (gather.TargetNode.ResourceAmount <= 0f)
					{
						// Depleted! Destroy the node
						var depletedNode = gather.TargetNode;
						AllProps.Remove(depletedNode);
						depletedNode.QueueFree();
					}
					
					if (newState.CarriedAmount >= gather.MaxCapacity)
					{
						// Full! Go back to base
						newState.ReturningToBase = true;
						
						// Find nearest castle
						Unit3D nearestCastle = null;
						float nearestDist = float.MaxValue;
						foreach (var u in AllUnits)
						{
							var wOwner = EcsWorld.Get<Owner>(entity).PlayerEntity;
							var uOwner = EcsWorld.Get<Owner>(u.Entity).PlayerEntity;
							if (uOwner == wOwner && u.UnitId == "castle" && GodotObject.IsInstanceValid(u))
							{
								float d = currentPos.DistanceTo(u.GlobalPosition);
								if (d < nearestDist)
								{
									nearestDist = d;
									nearestCastle = u;
								}
							}
						}
						
						if (nearestCastle != null)
						{
							var dest = nearestCastle.GlobalPosition;
							gatherersToUpdate.Add((entity, newState, dest));
						}
						else
						{
							gatherersToUpdate.Add((entity, newState, null));
						}
					}
					else
					{
						gatherersToUpdate.Add((entity, newState, null));
					}
				}
				else
				{
					// Walk to resource node if not already moving
					if (!EcsWorld.Has<MoveTo>(entity))
					{
						var dest = gather.TargetNode.GlobalPosition;
						gatherersToUpdate.Add((entity, gather, dest));
					}
				}
			}
		});
		
		foreach (var (worker, newState, dest) in gatherersToUpdate)
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

		// 1. ECS Movement System Execution
		var query = new QueryDescription().WithAll<Position, MoveTo, MovementStats>().WithNone<Dead>();
		var arrivedUnits = new List<Entity>();
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats) =>
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && EcsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
			{
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var u3d = EcsWorld.Get<Unit3D>(entity);
					if (GodotObject.IsInstanceValid(u3d))
					{
						u3d.Velocity = Vector3.Zero;
					}
				}
				return;
			}

			string unitId = "worker";
			if (EcsWorld.Has<Unit3D>(entity))
			{
				unitId = EcsWorld.Get<Unit3D>(entity).UnitId;
			}
			int includeFlags = 8; // default to ground (8)
			if (UnitRegistry.TryGetValue(unitId, out var meta))
			{
				includeFlags = GetUnitPathingFlags(meta);
			}
			var unitFilter = new DtQueryDefaultFilter();
			unitFilter.SetIncludeFlags(includeFlags);
			unitFilter.SetExcludeFlags(0);

			if (!EcsWorld.Has<PathFollow>(entity))
			{
				EcsWorld.Add(entity, new PathFollow { Waypoints = new System.Numerics.Vector3[256], WaypointCount = 0, CurrentWaypointIndex = 0, Target = moveTo.Target });
			}
			ref var pf = ref EcsWorld.Get<PathFollow>(entity);
			if (pf.Target != moveTo.Target || pf.Waypoints == null || pf.WaypointCount == 0)
			{
				pf.Target = moveTo.Target;
				if (pf.Waypoints == null)
				{
					pf.Waypoints = new System.Numerics.Vector3[256];
				}
				pf.WaypointCount = 0;
				pf.CurrentWaypointIndex = 0;
				if (GroundTerrain != null && GroundTerrain.NavMeshQuery != null)
				{
					var startPos = new RcVec3f(pos.Value.X, pos.Value.Y, pos.Value.Z);
					var endPos = new RcVec3f(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);
					GroundTerrain.NavMeshQuery.FindNearestPoly(startPos, _pathfindingExtents, unitFilter, out long startRef, out var startPt, out _);
					GroundTerrain.NavMeshQuery.FindNearestPoly(endPos, _pathfindingExtents, unitFilter, out long endRef, out var endPt, out _);
					if (startRef != 0 && endRef != 0)
					{
						GroundTerrain.NavMeshQuery.FindPath(startRef, endRef, startPt, endPt, unitFilter, _pathCorridorBuffer, out int corridorCount, _pathCorridorBuffer.Length);
						if (corridorCount > 0)
						{
							GroundTerrain.NavMeshQuery.FindStraightPath(startPt, endPt, _pathCorridorBuffer, corridorCount, _straightPathBuffer, out int straightPathCount, _straightPathBuffer.Length, 0);
							pf.WaypointCount = Math.Min(straightPathCount, pf.Waypoints.Length);
							pf.CurrentWaypointIndex = 0;
							for (int i = 0; i < pf.WaypointCount; i++)
							{
								pf.Waypoints[i] = new System.Numerics.Vector3(_straightPathBuffer[i].pos.X, _straightPathBuffer[i].pos.Y, _straightPathBuffer[i].pos.Z);
							}
						}
					}
				}
				if (pf.WaypointCount <= 0)
				{
					pf.Waypoints[0] = moveTo.Target;
					pf.WaypointCount = 1;
					pf.CurrentWaypointIndex = 0;
				}
			}
			var current = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			var target = new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);
			if (pf.CurrentWaypointIndex < pf.WaypointCount)
			{
				var wp = pf.Waypoints[pf.CurrentWaypointIndex];
				target = new Vector3(wp.X, wp.Y, wp.Z);
			}
			float dist = current.DistanceTo(target);
			if (dist < 0.2f)
			{
				pf.CurrentWaypointIndex++;
				if (pf.CurrentWaypointIndex < pf.WaypointCount)
				{
					var nextWp = pf.Waypoints[pf.CurrentWaypointIndex];
					target = new Vector3(nextWp.X, nextWp.Y, nextWp.Z);
					dist = current.DistanceTo(target);
				}
			}
			if (pf.CurrentWaypointIndex >= pf.WaypointCount)
			{
				arrivedUnits.Add(entity);
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var unit3D = EcsWorld.Get<Unit3D>(entity);
					unit3D.Velocity = Vector3.Zero;
				}
			}
			else
			{
				Vector3 dir = (target - current).Normalized();
				Vector3 velocity = dir * stats.Speed;
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var unit3D = EcsWorld.Get<Unit3D>(entity);
					var nextPos = current + dir * stats.Speed * fDelta;
					float r1 = unit3D.Scale.X * 1.2f;
					if (unit3D.UnitId == "castle") r1 = unit3D.Scale.X * 5.0f;
					else if (unit3D.UnitId == "tower") r1 = unit3D.Scale.X * 2.5f;

					foreach (var other in AllUnits)
					{
						if (other == unit3D || !GodotObject.IsInstanceValid(other)) continue;
						if (EcsWorld.Has<Dead>(other.Entity)) continue;

						float r2 = other.Scale.X * 1.2f;
						if (other.UnitId == "castle") r2 = other.Scale.X * 5.0f;
						else if (other.UnitId == "tower") r2 = other.Scale.X * 2.5f;

						float minDist = (r1 + r2) * 0.85f;
						float dx = nextPos.X - other.GlobalPosition.X;
						float dz = nextPos.Z - other.GlobalPosition.Z;
						float distSq = dx * dx + dz * dz;
						if (distSq < minDist * minDist)
						{
							float otherDist = Mathf.Sqrt(distSq);
							Vector3 pushDir;
							if (otherDist < 0.001f)
							{
								pushDir = new Vector3(1f, 0f, 0f);
								otherDist = 1f;
							}
							else
							{
								pushDir = new Vector3(dx / otherDist, 0f, dz / otherDist);
							}
							float overlap = minDist - otherDist;
							nextPos += pushDir * overlap;
						}
					}

					foreach (var prop in AllProps)
					{
						if (!GodotObject.IsInstanceValid(prop)) continue;

						float r2 = prop.Scale.X * 1.5f;
						if (prop.PropId == "goldmine") r2 = prop.Scale.X * 4.0f;

						float minDist = (r1 + r2) * 0.85f;
						float dx = nextPos.X - prop.GlobalPosition.X;
						float dz = nextPos.Z - prop.GlobalPosition.Z;
						float distSq = dx * dx + dz * dz;
						if (distSq < minDist * minDist)
						{
							float propDist = Mathf.Sqrt(distSq);
							Vector3 pushDir;
							if (propDist < 0.001f)
							{
								pushDir = new Vector3(1f, 0f, 0f);
								propDist = 1f;
							}
							else
							{
								pushDir = new Vector3(dx / propDist, 0f, dz / propDist);
							}
							float overlap = minDist - propDist;
							nextPos += pushDir * overlap;
						}
					}

					if (GroundTerrain != null && GroundTerrain.NavMeshQuery != null)
					{
						var nextRc = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
						GroundTerrain.NavMeshQuery.FindNearestPoly(nextRc, _pathfindingExtents, unitFilter, out long nearestRef, out var nearestPt, out _);
						if (nearestRef != 0)
						{
							nextPos = new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
						}
					}
					float groundHeight = nextPos.Y;
					Vector3 normal = Vector3.Up;
					if (GroundTerrain != null)
					{
						GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out groundHeight, out normal);
					}
					nextPos.Y = groundHeight;
					unit3D.Velocity = velocity;
					unit3D.GlobalPosition = nextPos;
					pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
					if (dir.LengthSquared() > 0.01f)
					{
						float angle = Mathf.Atan2(-dir.X, -dir.Z);
						var rot = unit3D.Rotation;
						rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * fDelta);
						unit3D.Rotation = rot;
						Vector3 forwardDir = new Vector3(-Mathf.Sin(unit3D.Rotation.Y), 0f, -Mathf.Cos(unit3D.Rotation.Y));
						Vector3 up = normal.Normalized();
						Vector3 right = forwardDir.Cross(up).Normalized();
						Vector3 forwardPerp = right.Cross(up).Normalized();
						Basis targetBasis = new Basis(right, up, forwardPerp);
						var qTarget = targetBasis.GetRotationQuaternion();
						var qCurrent = unit3D.Basis.GetRotationQuaternion();
						var qLerp = qCurrent.Slerp(qTarget, 10f * fDelta);
						unit3D.Basis = new Basis(qLerp);
					}
				}
				else
				{
					var nextPos = current + dir * stats.Speed * fDelta;
					if (GroundTerrain != null && GroundTerrain.NavMeshQuery != null)
					{
						var nextRc = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
						GroundTerrain.NavMeshQuery.FindNearestPoly(nextRc, _pathfindingExtents, unitFilter, out long nearestRef, out var nearestPt, out _);
						if (nearestRef != 0)
						{
							nextPos = new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
						}
					}
					float groundHeight = nextPos.Y;
					if (GroundTerrain != null)
					{
						GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out groundHeight, out _);
					}
					nextPos.Y = groundHeight;
					pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
				}
			}
		});
		foreach (var entity in arrivedUnits)
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
					if (q.Waypoints != null && q.Waypoints.Count > 0)
					{
						var nextWaypoint = q.Waypoints[0];
						q.Waypoints.RemoveAt(0);
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

		// 2. Cooldown Ticks
		var attackCooldownQuery = new QueryDescription().WithAll<Attack>();
		EcsWorld.Query(in attackCooldownQuery, (Entity entity, ref Attack atk) =>
		{
			if (atk.CurrentCooldown > 0)
			{
				atk.CurrentCooldown = Math.Max(0, atk.CurrentCooldown - fDelta);
			}
		});

		// 3. Auto-Acquire Targets for Idle/Attack-Move/Patrol units
		var targetAcquisitionQuery = new QueryDescription().WithAll<Position, Attack, Owner>().WithNone<AttackTarget, Dead>();
		var newAttackTargets = new List<(Entity Attacker, AttackTarget Target)>();
		EcsWorld.Query(in targetAcquisitionQuery, (Entity entity, ref Position pos, ref Attack atk, ref Owner owner) =>
		{
			// Priest healer support units do not auto-acquire enemy attack targets
			if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
			{
				return;
			}

			// Only units that are Idle, on Attack-Move, or Patrolling should auto-acquire targets
			bool isAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
			bool isPatrol     = EcsWorld.Has<Patrol>(entity);
			bool isIdle = !EcsWorld.Has<MoveTo>(entity) && !isAttackMove;

			if (isIdle || isAttackMove || isPatrol)
			{
				// Use registry scan radius when available
				float scanRadius = 15.0f;
				if (EcsWorld.Has<DefinitionId>(entity))
				{
					string defId = EcsWorld.Get<DefinitionId>(entity).Value;
					if (UnitRegistry.TryGetValue(defId, out var metaReg) && metaReg.ScanRadius > 0)
						scanRadius = metaReg.ScanRadius;
				}

				// Tactical Night Vision: Reduce scan range by 30% at night
				if (_timeOfDayIndex == 2)
				{
					scanRadius *= 0.7f;
				}

				// Scan for closest enemy
				Entity closestEnemy = Entity.Null;
				float closestDist = scanRadius;

				var enemyQuery = new QueryDescription().WithAll<Position, Owner>().WithNone<Dead>();
				var attackerPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
				var attackerOwner = owner.PlayerEntity;

				bool isAttackerEnemy = false;
				if (EcsWorld.Has<Unit3D>(entity))
				{
					isAttackerEnemy = EcsWorld.Get<Unit3D>(entity).IsEnemy;
				}

				EcsWorld.Query(in enemyQuery, (Entity potentialEnemy, ref Position enemyPosComp, ref Owner enemyOwnerComp) =>
				{
					if (attackerOwner != enemyOwnerComp.PlayerEntity)
					{
						if (!isAttackerEnemy && EcsWorld.Has<Unit3D>(potentialEnemy))
						{
							var enemyUnit3D = EcsWorld.Get<Unit3D>(potentialEnemy);
							if (enemyUnit3D != null && !enemyUnit3D.Visible) return;
						}
						var enemyPos = new Vector3(enemyPosComp.Value.X, enemyPosComp.Value.Y, enemyPosComp.Value.Z);
						float dist = attackerPos.DistanceTo(enemyPos);
						if (dist < closestDist)
						{
							closestDist = dist;
							closestEnemy = potentialEnemy;
						}
					}
				});

				if (closestEnemy != Entity.Null)
				{
					newAttackTargets.Add((entity, new AttackTarget(closestEnemy)));
				}
			}
		});
		foreach (var (attacker, target) in newAttackTargets)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				if (EcsWorld.Has<AttackTarget>(attacker))
					EcsWorld.Set(attacker, target);
				else
					EcsWorld.Add(attacker, target);
			}
		}

		// 4. Attack & Chase execution
		var combatQuery = new QueryDescription().WithAll<Position, Attack, AttackTarget, Owner>().WithNone<Dead>();
		var actionsToRemoveTarget = new List<Entity>();
		var actionsToChase = new List<(Entity Attacker, Vector3 TargetPos)>();
		var actionsToStopChasing = new List<Entity>();
		var unitsToKill = new List<(Entity Entity, Unit3D Unit)>();

		EcsWorld.Query(in combatQuery, (Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner) =>
		{
			if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
			{
				actionsToRemoveTarget.Add(entity);
				return;
			}

			var targetPosComp = EcsWorld.Get<Position>(target.Target);
			var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

			float dist = currentPos.DistanceTo(targetPos);
			if (dist <= atk.Range)
			{
				// In range: Stop moving and attack
				actionsToStopChasing.Add(entity);

				// Face target in 3D
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var unit3D = EcsWorld.Get<Unit3D>(entity);
					Vector3 dir = (targetPos - currentPos).Normalized();
					if (dir.LengthSquared() > 0.01f)
					{
						float angle = Mathf.Atan2(-dir.X, -dir.Z);
						var rot = unit3D.Rotation;
						rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * fDelta);
						unit3D.Rotation = rot;
					}
				}

				if (atk.CurrentCooldown <= 0)
				{
					if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(target.Target))
					{
						atk.CurrentCooldown = atk.Cooldown;
						return;
					}

					var targetHealth = EcsWorld.Get<Health>(target.Target);
					var targetArmor = EcsWorld.Has<Armor>(target.Target) ? EcsWorld.Get<Armor>(target.Target) : new Armor(0);

					float damage = atk.Damage - targetArmor.Value;
					if (damage < 1f) damage = 1f;

					_lastAttacker[target.Target.Id] = entity;
					OnUnitDamaged?.Invoke(new UnitWrapper(target.Target, EcsWorld), new UnitWrapper(entity, EcsWorld), damage);

					float newHp = Math.Max(0, targetHealth.Current - damage);
					EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

					// Under-attack alert: notify player when friendly units/castle take damage
					if (EcsWorld.Has<Unit3D>(target.Target))
					{
						var targetUnit3D_alert = EcsWorld.Get<Unit3D>(target.Target);
						if (!targetUnit3D_alert.IsEnemy && _underAttackAlertTimer <= 0f)
						{
							_underAttackAlertTimer = UnderAttackAlertCooldown;
							string alertMsg = targetUnit3D_alert.UnitId == "castle"
								? "⚠ YOUR CASTLE IS UNDER ATTACK!"
								: $"⚠ {targetUnit3D_alert.UnitId.ToUpper()} is under attack!";
							Callable.From(() => InGameHUD.Instance?.ShowFeedbackText(alertMsg, new Color(1.0f, 0.2f, 0.1f))).CallDeferred();
							Callable.From(() => UIManager.Instance?.PlayWarningSound()).CallDeferred();
						}
					}

					// Retaliation: if defender is idle/not attacking, attack back!
					if (EcsWorld.IsAlive(target.Target) && !EcsWorld.Has<Dead>(target.Target) && !EcsWorld.Has<AttackTarget>(target.Target))
					{
						if (EcsWorld.Has<Attack>(target.Target))
						{
							bool hasMoveTo = EcsWorld.Has<MoveTo>(target.Target);
							if (!hasMoveTo || EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(target.Target))
							{
								EcsWorld.Add(target.Target, new AttackTarget(entity));
							}
						}
					}

					atk.CurrentCooldown = atk.Cooldown;

					// Visual flash and kill check
					if (EcsWorld.Has<Unit3D>(target.Target))
					{
						var target3D = EcsWorld.Get<Unit3D>(target.Target);

						// Spawn ranged projectile visual if attacker has range > 3
						if (atk.Range > 3f && EcsWorld.Has<Unit3D>(entity))
						{
							var attacker3D = EcsWorld.Get<Unit3D>(entity);
							SpawnArrowProjectile(attacker3D.GlobalPosition, target3D.GlobalPosition);
						}

						if (newHp <= 0)
						{
							unitsToKill.Add((target.Target, target3D));
						}
						else
						{
							Callable.From(() => FlashDamageUnit(target3D)).CallDeferred();
						}
					}
				}
			}
			else
			{
				// Out of range: Chase unless holding position or building
				if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity) && !EcsWorld.Has<Building>(entity))
				{
					actionsToChase.Add((entity, targetPos));
				}
				else
				{
					// Out of range and cannot chase (building or hold position) -> remove target so we can acquire a new one!
					actionsToRemoveTarget.Add(entity);
				}
			}
		});

		// Apply deferred unit kills
		foreach (var (targetEntity, target3D) in unitsToKill)
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				if (!EcsWorld.Has<Dead>(targetEntity))
				{
					EcsWorld.Add<Dead>(targetEntity);
					Callable.From(() => KillUnit(target3D)).CallDeferred();
				}
			}
		}

		// Apply deferred combat actions
		foreach (var ent in actionsToRemoveTarget)
		{
			if (EcsWorld.IsAlive(ent))
			{
				if (EcsWorld.Has<AttackTarget>(ent))
				{
					EcsWorld.Remove<AttackTarget>(ent);
				}
				// If it was in Attack-Move mode, make sure it continues moving to destination
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
					// If it was patrolling, resume moving to the active patrol point
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

		foreach (var (attacker, targetPos) in actionsToChase)
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

		foreach (var attacker in actionsToStopChasing)
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

		// 4b. Healer System: Auto-Acquire Healing Targets for Priests
		var priestScanQuery = new QueryDescription().WithAll<Position, Owner, DefinitionId>().WithNone<Dead, HealingTarget>();
		var newHealingTargets = new List<(Entity Priest, HealingTarget Target)>();
		EcsWorld.Query(in priestScanQuery, (Entity entity, ref Position pos, ref Owner owner, ref DefinitionId defId) =>
		{
			if (defId.Value == "priest")
			{
				bool isIdle = !EcsWorld.Has<MoveTo>(entity);
				if (isIdle)
				{
					Entity closestDamagedFriendly = Entity.Null;
					float closestDist = 15.0f; // Priest scan radius
					var priestPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
					var friendlyOwner = owner.PlayerEntity;

					var friendlyScanQuery = new QueryDescription().WithAll<Position, Health, Owner>().WithNone<Dead>();
					EcsWorld.Query(in friendlyScanQuery, (Entity potentialFriendly, ref Position fPosComp, ref Health fHealth, ref Owner fOwner) =>
					{
						if (fOwner.PlayerEntity == friendlyOwner && fHealth.Current < fHealth.Max)
						{
							var fPos = new Vector3(fPosComp.Value.X, fPosComp.Value.Y, fPosComp.Value.Z);
							float dist = priestPos.DistanceTo(fPos);
							if (dist < closestDist)
							{
								closestDist = dist;
								closestDamagedFriendly = potentialFriendly;
							}
						}
					});

					if (closestDamagedFriendly != Entity.Null)
					{
						newHealingTargets.Add((entity, new HealingTarget(closestDamagedFriendly)));
					}
				}
			}
		});
		foreach (var (priest, target) in newHealingTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				if (EcsWorld.Has<HealingTarget>(priest)) EcsWorld.Set(priest, target);
				else EcsWorld.Add(priest, target);
			}
		}

		// 4c. Healer System: Heal & Chase execution
		var healingExecutionQuery = new QueryDescription().WithAll<Position, Attack, HealingTarget, Owner>().WithNone<Dead>();
		var healRemoveTargets = new List<Entity>();
		var healChaseTargets = new List<(Entity Priest, Vector3 TargetPos)>();
		var healStopChasing = new List<Entity>();

		EcsWorld.Query(in healingExecutionQuery, (Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner) =>
		{
			if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
			{
				healRemoveTargets.Add(entity);
				return;
			}

			var targetHealth = EcsWorld.Get<Health>(target.Target);
			if (targetHealth.Current >= targetHealth.Max)
			{
				healRemoveTargets.Add(entity);
				return;
			}

			var targetPosComp = EcsWorld.Get<Position>(target.Target);
			var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

			float dist = currentPos.DistanceTo(targetPos);
			if (dist <= atk.Range)
			{
				healStopChasing.Add(entity);

				if (EcsWorld.Has<Unit3D>(entity))
				{
					var unit3D = EcsWorld.Get<Unit3D>(entity);
					Vector3 dir = (targetPos - currentPos).Normalized();
					if (dir.LengthSquared() > 0.01f)
					{
						float angle = Mathf.Atan2(-dir.X, -dir.Z);
						var rot = unit3D.Rotation;
						rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * fDelta);
						unit3D.Rotation = rot;
					}
				}

				if (atk.CurrentCooldown <= 0)
				{
					float healAmount = atk.Damage;
					float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
					EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

					atk.CurrentCooldown = atk.Cooldown;

					if (EcsWorld.Has<Unit3D>(target.Target))
					{
						var target3D = EcsWorld.Get<Unit3D>(target.Target);
						var priest3D = EcsWorld.Get<Unit3D>(entity);

						SpawnHealVisualEffect(priest3D.GlobalPosition, target3D.GlobalPosition);
						Callable.From(() => FlashHealUnit(target3D)).CallDeferred();
					}
				}
			}
			else
			{
				if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
				{
					healChaseTargets.Add((entity, targetPos));
				}
			}
		});

		foreach (var ent in healRemoveTargets)
		{
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<HealingTarget>(ent))
			{
				EcsWorld.Remove<HealingTarget>(ent);
			}
		}

		foreach (var (priest, targetPos) in healChaseTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Set(priest, moveTo);
				else EcsWorld.Add(priest, moveTo);
			}
		}

		foreach (var priest in healStopChasing)
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

		// 5. Update Map Pings
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

		// 6. Update Unit Production Queue
		var prodQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.ProductionQueue>();
		EcsWorld.Query(in prodQuery, (Entity entity, ref Realm.Ecs.Components.Core.ProductionQueue prod) =>
		{
			if (prod.UnitIds.Count > 0)
			{
				prod.CurrentProgress += fDelta;
				if (prod.CurrentProgress >= prod.BuildTime)
				{
					string unitToSpawn = prod.UnitIds[0];
					prod.UnitIds.RemoveAt(0);
					prod.CurrentProgress = 0f;

					if (prod.UnitIds.Count > 0)
					{
						string nextUnitId = prod.UnitIds[0];
						prod.BuildTime = UnitRegistry[nextUnitId].ProductionTime;
					}

					if (EcsWorld.Has<Unit3D>(entity))
					{
						var building3D = EcsWorld.Get<Unit3D>(entity);
						var spawnPos = building3D.GlobalPosition + new Vector3(0, 0, 8); // Spawn slightly in front
						
						// Determine owner and if enemy
						var ownerComp = EcsWorld.Get<Owner>(entity);
						bool isEnemy = ownerComp.PlayerEntity != _playerEntity.AsPlayerEntity(EcsWorld);

						Vector3? rallyPoint = null;
						if (EcsWorld.Has<RallyPoint>(entity))
						{
							var rp = EcsWorld.Get<RallyPoint>(entity);
							rallyPoint = new Vector3(rp.Value.X, rp.Value.Y, rp.Value.Z);
						}
						else
						{
							rallyPoint = building3D.ToGlobal(new Vector3(0, 0, 8));
						}

						// Defer spawn to next frame
						Callable.From(() => SpawnUnitFromProduction(unitToSpawn, spawnPos, isEnemy, rallyPoint, true)).CallDeferred();

						// Notify player of training completion
						if (!isEnemy)
						{
							string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
							Callable.From(() => InGameHUD.Instance?.ShowFeedbackText($"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f))).CallDeferred();
						}
					}

					// Update HUD
					Callable.From(() => InGameHUD.Instance?.RefreshUI(SelectedUnits)).CallDeferred();
				}
			}
		});

		// 7. Update Map-Specific Script Logic
		TickScheduledTimers(fDelta);
		TickZoneTriggers();
		if (_activeMapScript != null)
		{
			_activeMapScript.Update(this, fDelta);
		}

		// 8. Update Building Hologram Preview
		UpdateBuildingPreview();

		if (!ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays && _replayRecorder != null)
		{
			RecordGameplayTick();
		}

		if (_multiplayerActive && Multiplayer.IsServer())
		{
			UpdateServerSnapshotTick(fDelta);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)
		{
			return;
		}

		if (IsMapEditorMode)
		{
			if (@event is InputEventKey editorKeyEvent && editorKeyEvent.Pressed && !editorKeyEvent.Echo)
			{
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				bool shiftPressed = Input.IsKeyPressed(Key.Shift);
				
				if (editorKeyEvent.Keycode == Key.Escape)
				{
					if (_rampStartPos != null)
					{
						_rampStartPos = null;
						MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Cancelled");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (SelectedEditorObject != null)
					{
						SelectedEditorObject = null;
						MapEditorHUD.Instance?.ShowFeedbackExternal("Deselected Object");
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (ActiveEditorTool != EditorTool.SelectMove)
					{
						ActiveEditorTool = EditorTool.SelectMove;
						MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Space && !ctrlPressed && !shiftPressed)
				{
					CenterCameraOnSelectedOrCastle();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Z && !ctrlPressed && !shiftPressed)
				{
					CycleCameraZoom();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.H && ctrlPressed)
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.Visible = !MapEditorHUD.Instance.Visible;
						MapEditorHUD.Instance.ShowFeedbackExternal(MapEditorHUD.Instance.Visible ? "HUD: Visible" : "HUD: Hidden");
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.H && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.ToggleHelpPanelExternal();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.M && !ctrlPressed && !shiftPressed)
				{
					EditorBlockMode = !EditorBlockMode;
					MapEditorHUD.Instance?.UpdateBlockModeExternal(EditorBlockMode);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Q && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.I && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Eyedropper);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.N && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Noise);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Delete || editorKeyEvent.Keycode == Key.Backspace)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var target = SelectedEditorObject;
						SelectedEditorObject = null;
						var action = DeleteObjectAtWithUndo(target, (target as Node3D).Position);
						if (action != null)
						{
							EditorHistoryManager.RecordAction(action);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Deleted Object");
							EditorHasUnsavedChanges = true;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Z && ctrlPressed)
				{
					if (shiftPressed)
					{
						EditorHistoryManager.Redo();
						MapEditorHUD.Instance?.ShowFeedbackExternal("Redo Action performed");
					}
					else
					{
						EditorHistoryManager.Undo();
						MapEditorHUD.Instance?.ShowFeedbackExternal("Undo Action performed");
					}
					EditorHasUnsavedChanges = true;
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Y && ctrlPressed)
				{
					EditorHistoryManager.Redo();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Redo Action performed");
					EditorHasUnsavedChanges = true;
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.S && ctrlPressed)
				{
					MapEditorHUD.Instance?.SaveMapActionExternal();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.O && ctrlPressed)
				{
					MapEditorHUD.Instance?.LoadMapAction();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.P && ctrlPressed)
				{
					SaveMapToFile();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Map published & compiled!");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.F6 && !ctrlPressed && !shiftPressed)
				{
					MapEditorHUD.Instance?.ImportTerrainFromMinimapDialog();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.G && ctrlPressed)
				{
					EditorSnapToGrid = !EditorSnapToGrid;
					MapEditorHUD.Instance?.UpdateGridSnapExternal(EditorSnapToGrid);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.G && !ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						MapEditorHUD.Instance?.AlignSelectedObjectToGround();
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.F && !ctrlPressed && !shiftPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject) && SelectedEditorObject is Unit3D unit)
					{
						bool nextIsEnemy = !unit.IsEnemy;
						MapEditorHUD.Instance?.ToggleSelectedObjectTeam(nextIsEnemy);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.C && !ctrlPressed && !shiftPressed)
				{
					var cam = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");
					if (cam != null && cam.HasMethod("ToggleTopDown"))
					{
						cam.Call("ToggleTopDown");
						bool topDown = cam.Call("IsTopDown").AsBool();
						MapEditorHUD.Instance?.UpdateCameraAngleButtonText(topDown);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.C && ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectArea)
					{
						PerformCopyArea();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						if (SelectedEditorObject is Unit3D unit)
						{
							_copiedObject = new CopiedObjectTemplate {
								Type = "unit",
								Id = unit.UnitId,
								Rotation = unit.RotationDegrees.Y,
								Scale = unit.Scale.X,
								IsEnemy = unit.IsEnemy
							};
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
						}
						else if (SelectedEditorObject is Prop3D prop)
						{
							_copiedObject = new CopiedObjectTemplate {
								Type = "prop",
								Id = prop.PropId,
								Rotation = prop.RotationDegrees.Y,
								Scale = prop.Scale.X,
								IsEnemy = false
							};
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
						}
						else if (SelectedEditorObject is Decal decal)
						{
							string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
							_copiedObject = new CopiedObjectTemplate {
								Type = "decal",
								Id = decalId,
								Rotation = decal.RotationDegrees.Y,
								Scale = decal.Scale.X,
								IsEnemy = false
							};
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.V && ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
					{
						if (_copiedArea != null)
						{
							ActiveEditorTool = EditorTool.PasteArea;
							MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					if (_copiedObject != null)
					{
						var hit = RaycastFromMouse(GetViewport().GetMousePosition());
						if (hit != null && hit.ContainsKey("position"))
						{
							Vector3 spawnPos = hit["position"].AsVector3();
							if (EditorSnapToGrid && GroundTerrain != null)
							{
								float spacing = GroundTerrain.Spacing;
								int width = GroundTerrain.Width;
								int depth = GroundTerrain.Depth;
								float fx = Mathf.Round(spawnPos.X / spacing + (width - 1) / 2.0f);
								spawnPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
								float fz = Mathf.Round(spawnPos.Z / spacing + (depth - 1) / 2.0f);
								spawnPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
							}
							spawnPos.Y = GetTerrainHeightAt(spawnPos);
							var cop = _copiedObject.Value;
							Node pastedNode = null;
							IEditorAction action = null;
							if (cop.Type == "unit")
							{
								pastedNode = SpawnUnitExternal(cop.Id, spawnPos, cop.IsEnemy, cop.Rotation, cop.Scale);
								if (pastedNode != null)
								{
									action = new ObjectSpawnAction("unit", cop.Id, spawnPos, cop.Rotation, cop.Scale, cop.IsEnemy, pastedNode);
									MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Unit: {cop.Id.ToUpper()}");
								}
							}
							else if (cop.Type == "prop")
							{
								pastedNode = SpawnPropExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
								if (pastedNode != null)
								{
									action = new ObjectSpawnAction("prop", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
									MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Prop: {cop.Id.ToUpper()}");
								}
							}
							else if (cop.Type == "decal")
							{
								pastedNode = SpawnDecalExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
								if (pastedNode != null)
								{
									action = new ObjectSpawnAction("decal", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
									MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Decal: {cop.Id.ToUpper()}");
								}
							}
							if (action != null)
							{
								if (EditorMirrorMode != MirrorMode.None)
								{
									var actionsList = new List<IEditorAction> { action };
									foreach (var t in GetMirroredTransforms(spawnPos, cop.Rotation))
									{
										Vector3 mPos = t.Position;
										mPos.Y = GetTerrainHeightAt(mPos);
										Node mNode = null;
										if (cop.Type == "unit")
										{
											mNode = SpawnUnitExternal(cop.Id, mPos, cop.IsEnemy, t.Rotation, cop.Scale);
											if (mNode != null)
											{
												actionsList.Add(new ObjectSpawnAction("unit", cop.Id, mPos, t.Rotation, cop.Scale, cop.IsEnemy, mNode));
											}
										}
										else if (cop.Type == "prop")
										{
											mNode = SpawnPropExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
											if (mNode != null)
											{
												actionsList.Add(new ObjectSpawnAction("prop", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
											}
										}
										else if (cop.Type == "decal")
										{
											mNode = SpawnDecalExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
											if (mNode != null)
											{
												actionsList.Add(new ObjectSpawnAction("decal", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
											}
										}
									}
									var composite = new CompositeAction(actionsList);
									EditorHistoryManager.RecordAction(composite);
								}
								else
								{
									EditorHistoryManager.RecordAction(action);
								}
								SelectedEditorObject = pastedNode;
								EditorHasUnsavedChanges = true;
							}
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.D && ctrlPressed)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						Node3D selectedNode = SelectedEditorObject as Node3D;
						Vector3 spawnPos = selectedNode.Position + new Vector3(2.0f, 0.0f, 2.0f);
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float rotY = selectedNode.RotationDegrees.Y;
						float scaleVal = selectedNode.Scale.X;
						Node clonedNode = null;
						IEditorAction action = null;
						if (SelectedEditorObject is Unit3D unit)
						{
							clonedNode = SpawnUnitExternal(unit.UnitId, spawnPos, unit.IsEnemy, rotY, scaleVal);
							if (clonedNode != null)
							{
								action = new ObjectSpawnAction("unit", unit.UnitId, spawnPos, rotY, scaleVal, unit.IsEnemy, clonedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Unit: {unit.UnitId.ToUpper()}");
							}
						}
						else if (SelectedEditorObject is Prop3D prop)
						{
							clonedNode = SpawnPropExternalWithParams(prop.PropId, spawnPos, rotY, scaleVal);
							if (clonedNode != null)
							{
								action = new ObjectSpawnAction("prop", prop.PropId, spawnPos, rotY, scaleVal, false, clonedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Prop: {prop.PropId.ToUpper()}");
							}
						}
						else if (SelectedEditorObject is Decal decal)
						{
							string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
							clonedNode = SpawnDecalExternalWithParams(decalId, spawnPos, rotY, scaleVal);
							if (clonedNode != null)
							{
								action = new ObjectSpawnAction("decal", decalId, spawnPos, rotY, scaleVal, false, clonedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Decal: {decalId.ToUpper()}");
							}
						}
						if (action != null)
						{
							EditorHistoryManager.RecordAction(action);
							SelectedEditorObject = clonedNode;
							EditorHasUnsavedChanges = true;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Bracketleft)
				{
					EditorBrushRadius = Mathf.Max(1.0f, EditorBrushRadius - 1.0f);
					MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Bracketright)
				{
					EditorBrushRadius = Mathf.Min(25.0f, EditorBrushRadius + 1.0f);
					MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}
				bool isNumpadNudge = editorKeyEvent.Keycode == Key.Kp1 ||
									 editorKeyEvent.Keycode == Key.Kp2 ||
									 editorKeyEvent.Keycode == Key.Kp3 ||
									 editorKeyEvent.Keycode == Key.Kp4 ||
									 editorKeyEvent.Keycode == Key.Kp6 ||
									 editorKeyEvent.Keycode == Key.Kp7 ||
									 editorKeyEvent.Keycode == Key.Kp8 ||
									 editorKeyEvent.Keycode == Key.Kp9;

				if (isNumpadNudge)
				{
					if (GodotObject.IsInstanceValid(SelectedEditorObject) && SelectedEditorObject is Node3D node3D)
					{
						Vector3 nudgeDir = Vector3.Zero;
						if (editorKeyEvent.Keycode == Key.Kp8) nudgeDir = new Vector3(0, 0, -1);
						else if (editorKeyEvent.Keycode == Key.Kp2) nudgeDir = new Vector3(0, 0, 1);
						else if (editorKeyEvent.Keycode == Key.Kp4) nudgeDir = new Vector3(-1, 0, 0);
						else if (editorKeyEvent.Keycode == Key.Kp6) nudgeDir = new Vector3(1, 0, 0);
						else if (editorKeyEvent.Keycode == Key.Kp7) nudgeDir = new Vector3(-1, 0, -1).Normalized();
						else if (editorKeyEvent.Keycode == Key.Kp9) nudgeDir = new Vector3(1, 0, -1).Normalized();
						else if (editorKeyEvent.Keycode == Key.Kp1) nudgeDir = new Vector3(-1, 0, 1).Normalized();
						else if (editorKeyEvent.Keycode == Key.Kp3) nudgeDir = new Vector3(1, 0, 1).Normalized();

						float nudgeDistance = 1.0f;
						Vector3 targetPos = node3D.Position + nudgeDir * nudgeDistance;
						
						bool valid = true;
						if (GroundTerrain != null)
						{
							float spacing = GroundTerrain.Spacing;
							int width = GroundTerrain.Width;
							int depth = GroundTerrain.Depth;
							float halfW = (width - 1) / 2.0f * spacing;
							float halfD = (depth - 1) / 2.0f * spacing;
							if (Mathf.Abs(targetPos.X) > halfW || Mathf.Abs(targetPos.Z) > halfD)
							{
								valid = false;
							}
						}

						if (valid)
						{
							float radius = 1.0f;
							if (node3D is Unit3D u) radius = GetPlacementRadius(u.UnitId, u.Scale.X);
							else if (node3D is Prop3D p) radius = GetPlacementRadius(p.PropId, p.Scale.X);

							if (IsPositionBlocked(targetPos, radius, node3D))
							{
								valid = false;
							}
						}

						if (valid)
						{
							targetPos.Y = GetTerrainHeightAt(targetPos);
							bool isUnit = node3D is Unit3D;
							bool isEnemy = isUnit ? (node3D as Unit3D).IsEnemy : false;
							var action = new ObjectTransformAction(
								node3D,
								node3D.Position, targetPos,
								node3D.RotationDegrees, node3D.RotationDegrees,
								node3D.Scale, node3D.Scale,
								isEnemy, isEnemy
							);
							node3D.Position = targetPos;
							if (node3D is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
							{
								EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z)));
							}
							EditorHistoryManager.RecordAction(action);
							EditorHasUnsavedChanges = true;
							MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
							MapEditorHUD.Instance?.ShowFeedbackExternal("Object nudged");
						}
						else
						{
							UIManager.Instance?.PlayWarningSound();
						}
					}
					GetViewport().SetInputAsHandled();
					return;
				}

				if (editorKeyEvent.Keycode == Key.Minus)
				{
					EditorBrushStrength = Mathf.Max(0.1f, EditorBrushStrength - 0.5f);
					MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Equal)
				{
					EditorBrushStrength = Mathf.Min(10.0f, EditorBrushStrength + 0.5f);
					MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.R)
				{
					if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal)
					{
						if (EditorRandomRotation || EditorRandomScale)
						{
							GenerateNewRandomPlacementRotationAndScale();
							MapEditorHUD.Instance?.ShowFeedbackExternal("Re-randomized Rotation & Scale");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					float angleStep = shiftPressed ? 15.0f : 45.0f;
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldRot = node3D.RotationDegrees;
						Vector3 newRot = oldRot;
						newRot.Y = (newRot.Y + angleStep) % 360.0f;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							oldRot, newRot,
							node3D.Scale, node3D.Scale,
							isEnemy, isEnemy
						);
						node3D.RotationDegrees = newRot;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Rotated Object to {newRot.Y}Â°");
					}
					else
					{
						EditorPlacementRotation = (EditorPlacementRotation + angleStep) % 360.0f;
						MapEditorHUD.Instance?.UpdateRotationExternal(EditorPlacementRotation);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.S)
				{
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldScale = node3D.Scale;
						float current = oldScale.X;
						float next = current;
						if (shiftPressed)
						{
							next = current + 0.1f;
							if (next > 3.0f) next = 0.2f;
						}
						else
						{
							next = current switch {
								0.5f => 1.0f,
								1.0f => 1.5f,
								1.5f => 2.0f,
								2.0f => 0.5f,
								_ => 1.0f
							};
						}
						Vector3 newScale = Vector3.One * next;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							node3D.RotationDegrees, node3D.RotationDegrees,
							oldScale, newScale,
							isEnemy, isEnemy
						);
						node3D.Scale = newScale;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Scaled Object to {next:F1}x");
					}
					else
					{
						float current = EditorPlacementScale;
						float next = current;
						if (shiftPressed)
						{
							next = current + 0.1f;
							if (next > 3.0f) next = 0.2f;
						}
						else
						{
							next = current switch {
								0.5f => 1.0f,
								1.0f => 1.5f,
								1.5f => 2.0f,
								2.0f => 0.5f,
								_ => 1.0f
							};
						}
						EditorPlacementScale = next;
						MapEditorHUD.Instance?.UpdateScaleExternal(next);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode >= Key.Key1 && editorKeyEvent.Keycode <= Key.Key9)
				{
					int toolIndex = (int)(editorKeyEvent.Keycode - Key.Key1);
					EditorTool targetTool = toolIndex switch
					{
						0 => EditorTool.Raise,
						1 => EditorTool.Lower,
						2 => EditorTool.Smooth,
						3 => EditorTool.Flatten,
						4 => EditorTool.Cliff,
						5 => EditorTool.PaintGrass,
						6 => EditorTool.PlaceDecal,
						7 => EditorTool.PlaceUnit,
						8 => EditorTool.Ramp,
						_ => EditorTool.None
					};
					if (targetTool != EditorTool.None)
					{
						MapEditorHUD.Instance?.SelectToolFromHotkey(targetTool);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				if (editorKeyEvent.Keycode == Key.Key0)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.DeleteObject);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.B && !ctrlPressed && !shiftPressed)
				{
					EditorBrushIsSquare = !EditorBrushIsSquare;
					UpdateBrushMesh();
					MapEditorHUD.Instance?.UpdateBrushShapeExternal(EditorBrushIsSquare);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.V && !ctrlPressed && !shiftPressed)
				{
					EditorGridVisible = !EditorGridVisible;
					UpdateGridOverlayVisibility();
					MapEditorHUD.Instance?.UpdateGridOverlayExternal(EditorGridVisible);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.T && !ctrlPressed && !shiftPressed)
				{
					GenerateNewRandomPlacementRotationAndScale();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Re-randomized Rotation & Scale");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Tab)
				{
					MapEditorHUD.Instance?.CycleTextureSwatch(!shiftPressed);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton wheelBtn && wheelBtn.Pressed && (wheelBtn.ButtonIndex == MouseButton.WheelUp || wheelBtn.ButtonIndex == MouseButton.WheelDown))
			{
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				bool shiftPressed = Input.IsKeyPressed(Key.Shift);
				bool isUp = wheelBtn.ButtonIndex == MouseButton.WheelUp;

				bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
									 ActiveEditorTool == EditorTool.Lower ||
									 ActiveEditorTool == EditorTool.Flatten ||
									 ActiveEditorTool == EditorTool.Smooth ||
									 ActiveEditorTool == EditorTool.Cliff ||
									 ActiveEditorTool == EditorTool.PaintGrass ||
									 ActiveEditorTool == EditorTool.PaintDirt ||
									 ActiveEditorTool == EditorTool.PaintRock ||
									 ActiveEditorTool == EditorTool.PaintSand ||
									 ActiveEditorTool == EditorTool.Noise;

				if (isTerrainTool)
				{
					if (shiftPressed)
					{
						float deltaSize = isUp ? 1.0f : -1.0f;
						EditorBrushRadius = Mathf.Clamp(EditorBrushRadius + deltaSize, 1.0f, 25.0f);
						MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
						GetViewport().SetInputAsHandled();
						return;
					}
					if (ctrlPressed)
					{
						float deltaStr = isUp ? 0.5f : -0.5f;
						EditorBrushStrength = Mathf.Clamp(EditorBrushStrength + deltaStr, 0.1f, 10.0f);
						MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				if (shiftPressed)
				{
					float rotDelta = isUp ? 15.0f : -15.0f;
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldRot = node3D.RotationDegrees;
						Vector3 newRot = oldRot;
						newRot.Y = (newRot.Y + rotDelta + 360.0f) % 360.0f;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							oldRot, newRot,
							node3D.Scale, node3D.Scale,
							isEnemy, isEnemy
						);
						node3D.RotationDegrees = newRot;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
					}
					else
					{
						EditorPlacementRotation = (EditorPlacementRotation + rotDelta + 360.0f) % 360.0f;
						MapEditorHUD.Instance?.UpdateRotationExternal(EditorPlacementRotation);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (ctrlPressed)
				{
					float scaleDelta = isUp ? 0.1f : -0.1f;
					if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 oldScale = node3D.Scale;
						float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 3.0f);
						Vector3 newScale = Vector3.One * newScaleVal;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, node3D.Position,
							node3D.RotationDegrees, node3D.RotationDegrees,
							oldScale, newScale,
							isEnemy, isEnemy
						);
						node3D.Scale = newScale;
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
					}
					else
					{
						EditorPlacementScale = Mathf.Clamp(EditorPlacementScale + scaleDelta, 0.2f, 3.0f);
						MapEditorHUD.Instance?.UpdateScaleExternal(EditorPlacementScale);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton editorRightMouseBtn && editorRightMouseBtn.Pressed && editorRightMouseBtn.ButtonIndex == MouseButton.Right)
			{
				if (IsMouseOverUI()) return;
				if (_rampStartPos != null)
				{
					_rampStartPos = null;
					MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Cancelled");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (SelectedEditorObject != null)
				{
					SelectedEditorObject = null;
					MapEditorHUD.Instance?.ShowFeedbackExternal("Deselected Object");
					GetViewport().SetInputAsHandled();
					return;
				}
				else if (ActiveEditorTool == EditorTool.PasteArea)
				{
					ActiveEditorTool = EditorTool.SelectArea;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
					if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
					GetViewport().SetInputAsHandled();
					return;
				}
				else if (ActiveEditorTool != EditorTool.SelectMove)
				{
					ActiveEditorTool = EditorTool.SelectMove;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
					if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton releaseEvent && !releaseEvent.Pressed && releaseEvent.ButtonIndex == MouseButton.Left)
			{
				if (_isSelectingArea)
				{
					_isSelectingArea = false;
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventMouseButton editorMouseBtn && editorMouseBtn.Pressed && editorMouseBtn.ButtonIndex == MouseButton.Left)
			{
				if (IsMouseOverUI()) return;
				
				var hit = RaycastFromMouse(editorMouseBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					Vector3 hitPos = hit["position"].AsVector3();
					
					if (EditorSnapToGrid && GroundTerrain != null)
					{
						float spacing = GroundTerrain.Spacing;
						int width = GroundTerrain.Width;
						int depth = GroundTerrain.Depth;
						float fx = Mathf.Round(hitPos.X / spacing + (width - 1) / 2.0f);
						hitPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
						float fz = Mathf.Round(hitPos.Z / spacing + (depth - 1) / 2.0f);
						hitPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
					}
					
					if (ActiveEditorTool == EditorTool.PlaceUnit)
					{
						if (EditorClumpMode) return;
						if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;

						Vector3 spawnPos = hitPos;
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float radius = GetPlacementRadius(ActivePlaceId, scaleVal);
						var finalPos = FindNearestFreePosition(spawnPos, radius);
						if (finalPos == null)
						{
							MapEditorHUD.Instance?.ShowFeedbackExternal("invalid location");
							UIManager.Instance?.PlayWarningSound();
							GetViewport().SetInputAsHandled();
							return;
						}
						spawnPos = finalPos.Value;

						var unit = SpawnUnitExternal(ActivePlaceId, spawnPos, PlaceUnitIsEnemy, placementRot, scaleVal);
						if (unit != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("unit", ActivePlaceId, spawnPos, placementRot, scaleVal, PlaceUnitIsEnemy, unit)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									if (IsPositionBlocked(mPos, radius)) continue;
									var mUnit = SpawnUnitExternal(ActivePlaceId, mPos, PlaceUnitIsEnemy, t.Rotation, scaleVal);
									if (mUnit != null)
									{
										actions.Add(new ObjectSpawnAction("unit", ActivePlaceId, mPos, t.Rotation, scaleVal, PlaceUnitIsEnemy, mUnit));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_isPastingObject = false;
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceProp)
					{
						if (EditorClumpMode) return;
						if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;

						Vector3 spawnPos = hitPos;
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float radius = GetPlacementRadius(ActivePlaceId, scaleVal);
						var finalPos = FindNearestFreePosition(spawnPos, radius);
						if (finalPos == null)
						{
							MapEditorHUD.Instance?.ShowFeedbackExternal("invalid location");
							UIManager.Instance?.PlayWarningSound();
							GetViewport().SetInputAsHandled();
							return;
						}
						spawnPos = finalPos.Value;

						var prop = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, placementRot, scaleVal);
						if (prop != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("prop", ActivePlaceId, spawnPos, placementRot, scaleVal, false, prop)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									if (IsPositionBlocked(mPos, radius)) continue;
									var mProp = SpawnPropExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
									if (mProp != null)
									{
										actions.Add(new ObjectSpawnAction("prop", ActivePlaceId, mPos, t.Rotation, scaleVal, false, mProp));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_isPastingObject = false;
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceDecal)
					{
						if (EditorClumpMode) return;
						if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;
						var decal = SpawnDecalExternalWithParams(ActivePlaceId, hitPos, placementRot, scaleVal);
						if (decal != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("decal", ActivePlaceId, hitPos, placementRot, scaleVal, false, decal)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(hitPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									var mDecal = SpawnDecalExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
									if (mDecal != null)
									{
										actions.Add(new ObjectSpawnAction("decal", ActivePlaceId, mPos, t.Rotation, scaleVal, false, mDecal));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_isPastingObject = false;
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.DeleteObject)
					{
						var collider = hit["collider"].As<Node>();
						var action = DeleteObjectAtWithUndo(collider, hitPos);
						if (action != null)
						{
							var actions = new List<IEditorAction> { action };
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(hitPos, 0.0f))
								{
									var nearObj = FindObjectNearPosition(t.Position);
									if (nearObj != null)
									{
										var mAction = DeleteObjectAtWithUndo(nearObj, t.Position);
										if (mAction != null)
										{
											actions.Add(mAction);
										}
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.Eyedropper)
					{
						string mode = MapEditorHUD.Instance != null ? MapEditorHUD.Instance.GetEyedropperMode() : "all";
						var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
						Node clickedNode = null;

						if (mode == "all" || mode == "3d")
						{
							if (collider != null)
							{
								clickedNode = FindUnit3DInParentChain(collider);
								if (clickedNode == null)
								{
									clickedNode = FindProp3DInParentChain(collider);
								}
							}
						}

						if (clickedNode == null && (mode == "all" || mode == "decal"))
						{
							Decal closestDecal = null;
							float closestDist = 3.0f;
							foreach (var child in GetChildren())
							{
								if (child is Decal dec && GodotObject.IsInstanceValid(dec))
								{
									float d = dec.GlobalPosition.DistanceTo(hitPos);
									if (d < closestDist)
									{
										closestDist = d;
										closestDecal = dec;
									}
								}
							}
							if (closestDecal != null)
							{
								clickedNode = closestDecal;
							}
						}

						if (clickedNode != null)
						{
							if (clickedNode is Unit3D unit)
							{
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedUnitOrProp(unit.UnitId, unit.IsBuilding);
								}
								else
								{
									ActivePlaceId = unit.UnitId;
									PlaceUnitIsEnemy = unit.IsEnemy;
									ActiveEditorTool = EditorTool.PlaceUnit;
								}
							}
							else if (clickedNode is Prop3D prop)
							{
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedUnitOrProp(prop.PropId, false);
								}
								else
								{
									ActivePlaceId = prop.PropId;
									ActiveEditorTool = EditorTool.PlaceProp;
								}
							}
							else if (clickedNode is Decal decal)
							{
								string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedDecal(decalId);
								}
								else
								{
									ActivePlaceId = decalId;
									ActiveEditorTool = EditorTool.PlaceDecal;
								}
							}
						}
						else
						{
							bool wantHeight = (mode == "height") || (mode == "all" && Input.IsKeyPressed(Key.Shift));
							bool wantTerrain = (mode == "terrain") || (mode == "all" && !Input.IsKeyPressed(Key.Shift));

							if (wantHeight)
							{
								float sampledHeight = GetTerrainHeightAt(hitPos);
								EditorFlattenHeight = sampledHeight;
								MapEditorHUD.Instance?.UpdateFlattenHeightExternal(sampledHeight);
								MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Flatten);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Picked Height: {sampledHeight:F1}m");
							}
							else if (wantTerrain && GroundTerrain != null)
							{
								int w = GroundTerrain.Width;
								int d = GroundTerrain.Depth;
								float sp = GroundTerrain.Spacing;
								float fx = hitPos.X / sp + (w - 1) / 2.0f;
								float fz = hitPos.Z / sp + (d - 1) / 2.0f;
								int x = Mathf.Clamp((int)Math.Round(fx), 0, w - 1);
								int z = Mathf.Clamp((int)Math.Round(fz), 0, d - 1);
								Color sampledColor = GroundTerrain.Colors[x, z];
								EditorPaintColor = sampledColor;
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPaintSwatchFromColor(sampledColor);
								}
								else
								{
									ActiveEditorTool = EditorTool.PaintGrass;
								}
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Picked Color: {sampledColor.ToHtml(false)}");
							}
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.SelectMove)
					{
						var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
						Node clickedNode = null;
						if (collider != null)
						{
							clickedNode = FindUnit3DInParentChain(collider);
							if (clickedNode == null)
							{
								clickedNode = FindProp3DInParentChain(collider);
							}
						}
						if (clickedNode == null)
						{
							Decal closestDecal = null;
							float closestDist = 3.0f;
							foreach (var child in GetChildren())
							{
								if (child is Decal dec && GodotObject.IsInstanceValid(dec))
								{
									float d = dec.GlobalPosition.DistanceTo(hitPos);
									if (d < closestDist)
									{
										closestDist = d;
										closestDecal = dec;
									}
								}
							}
							if (closestDecal != null)
							{
								clickedNode = closestDecal;
							}
						}
						if (clickedNode != null)
						{
							SelectedEditorObject = clickedNode;
							_isDraggingObject = true;
							_dragObjectStartPos = (SelectedEditorObject as Node3D).Position;
							_dragObjectStartRot = (SelectedEditorObject as Node3D).RotationDegrees;
							_dragObjectStartScale = (SelectedEditorObject as Node3D).Scale;
							_dragObjectStartIsEnemy = (SelectedEditorObject is Unit3D u) ? u.IsEnemy : false;
							_dragObjectStartHitPos = hitPos;
							_dragObjectHasMoved = false;
						}
						else
						{
							SelectedEditorObject = null;
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.Ramp)
					{
						if (_rampStartPos == null)
						{
							_rampStartPos = hitPos;
							MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Start Point Set!");
						}
						else
						{
							Vector3 start = _rampStartPos.Value;
							Vector3 end = hitPos;
							var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
							var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
							bool modified = ApplyRampInternal(start, end);
							if (EditorMirrorMode != MirrorMode.None)
							{
								var startMirrored = GetMirroredTransforms(start, 0.0f);
								var endMirrored = GetMirroredTransforms(end, 0.0f);
								for (int i = 0; i < startMirrored.Count; i++)
								{
									bool mResult = ApplyRampInternal(startMirrored[i].Position, endMirrored[i].Position);
									if (mResult) modified = true;
								}
							}
							if (modified)
							{
								GroundTerrain.UpdateMeshAndPhysics(true, false);
								AlignAllEntitiesToTerrain();
								var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
								var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
								var action = new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter);
								EditorHistoryManager.RecordAction(action);
								EditorHasUnsavedChanges = true;
							}
							_rampStartPos = null;
							MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Created!");
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.FloodFill)
					{
						PerformFloodFill(hitPos, EditorPaintColor);
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.SelectArea)
					{
						if (GroundTerrain != null)
						{
							float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
							float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
							int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
							int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
							_selectionStart = new Vector2I(cx, cz);
							_selectionEnd = new Vector2I(cx, cz);
							_isSelectingArea = true;
							CreateSelectionHighlight();
							RebuildSelectionHighlightMesh(cx, cz, cx, cz);
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PasteArea)
					{
						if (GroundTerrain != null && _copiedArea != null)
						{
							float fx = hitPos.X / GroundTerrain.Spacing + (GroundTerrain.Width - 1) / 2.0f;
							float fz = hitPos.Z / GroundTerrain.Spacing + (GroundTerrain.Depth - 1) / 2.0f;
							int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
							int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
							PerformPasteArea(cx, cz);
							ActiveEditorTool = EditorTool.SelectArea;
							MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
							if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
						}
						GetViewport().SetInputAsHandled();
					}
				}
			}
			return;
		}

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			return;
		}

		// Process Escape key before anything else
		if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && escapeEvent.Keycode == Key.Escape)
		{
			if (_activeSpellTargeting != null || _activeCommandTargeting != null)
			{
				_activeSpellTargeting = null;
				_activeCommandTargeting = null;
				Input.SetCustomMouseCursor(null);
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				if (InGameHUD.Instance != null)
					InGameHUD.Instance.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}
			if (_activeBuildingPlacementType != null)
			{
				CancelBuildingPlacement();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (ActivePingMode)
			{
				ActivePingMode = false;
				if (InGameHUD.Instance != null)
					InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}
			if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
			{
				InGameHUD.Instance.ExitBuildSubMenu();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (SelectedUnits.Count > 0)
			{
				// If a friendly castle is selected, try to cancel its queue first. Otherwise, clear selection.
				if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].UnitId == "castle")
				{
					var castle = SelectedUnits[0];
					if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castle.Entity) && EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castle.Entity).UnitIds.Count > 0)
					{
						CancelLastQueuedUnit(castle.Entity);
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				ClearSelection();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
				GetViewport().SetInputAsHandled();
				return;
			}

			// If nothing to cancel, open menu!
			GetViewport().SetInputAsHandled();
			UIManager.Instance.OpenSettingsOverlay();
			return;
		}

		// Process keyboard hotkeys
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			// Check for Control Groups (0-9)
			if (keyEvent.Keycode >= Key.Key0 && keyEvent.Keycode <= Key.Key9)
			{
				int groupIdx = (int)(keyEvent.Keycode - Key.Key0);
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				if (ctrlPressed)
				{
					AssignControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					RecallControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			// F1: Select all Idle movable player units
			if (keyEvent.Keycode == Key.F1)
			{
				SelectAllIdleUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			// F2: Select all combat/military player units
			if (keyEvent.Keycode == Key.F2)
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			// F4: Toggle minimap terrain overlay (Radar Mode)
			if (keyEvent.Keycode == Key.F4)
			{
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.ToggleMinimapTerrain();
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// Alt+G: Send Alert Ping on Map
			if (keyEvent.Keycode == Key.G && Input.IsKeyPressed(Key.Alt))
			{
				ActivePingMode = !ActivePingMode;
				if (InGameHUD.Instance != null)
				{
					if (ActivePingMode)
						InGameHUD.Instance.ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
					else
						InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// Ctrl+A: Select all military player units
			if (keyEvent.Keycode == Key.A && Input.IsKeyPressed(Key.Ctrl))
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			// Z: Cycle Camera Zoom
			if (keyEvent.Keycode == Key.Z)
			{
				CycleCameraZoom();
				GetViewport().SetInputAsHandled();
				return;
			}

			// Space: Center camera on Player Castle
			if (keyEvent.Keycode == Key.Space)
			{
				CenterCameraOnCastle();
				GetViewport().SetInputAsHandled();
				return;
			}

			// Unit-specific hotkeys for single selections (production/upgrades)
			if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy)
			{
				var selectedUnit = SelectedUnits[0];
				if (selectedUnit.UnitId == "castle")
				{
					if (keyEvent.Keycode == Key.F)
					{
						TrainUnitAtCastle("soldier");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.R)
					{
						TrainUnitAtCastle("archer");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.P)
					{
						TrainUnitAtCastle("priest");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.W)
					{
						BuyWeaponsUpgrade();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.G)
					{
						BuyShieldsUpgrade();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.T)
					{
						BuyHarvestingUpgrade();
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.Y)
					{
						EnterCommandTargeting("rally");
						GetViewport().SetInputAsHandled();
						return;
					}
					if (keyEvent.Keycode == Key.I)
					{
						BuyHealingPotion(selectedUnit.Entity);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
				else if (selectedUnit.UnitId == "tower")
				{
					if (keyEvent.Keycode == Key.U)
					{
						UpgradeTower(selectedUnit);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
			}

			// Global hotkeys that don't require selection
			if (keyEvent.Keycode == Key.Tab)
			{
				CycleSelectionFocus();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.F3)
			{
				SelectAllBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.F5)
			{
				InGameHUD.Instance?.ToggleHotkeyPanel();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.Delete)
			{
				DeleteSelectedUnits();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Keycode == Key.Quoteleft) // backtick / `
			{
				CycleThroughBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}

			// If we have player units selected, check hotkeys
			bool hasPlayerSelection = SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy;
			if (hasPlayerSelection)
			{
				switch (keyEvent.Keycode)
				{
					case Key.A:
						EnterCommandTargeting("attack");
						GetViewport().SetInputAsHandled();
						return;
					case Key.M:
						EnterCommandTargeting("move");
						GetViewport().SetInputAsHandled();
						return;
					case Key.P:
						EnterCommandTargeting("patrol");
						GetViewport().SetInputAsHandled();
						return;
					case Key.S:
						StopSelectedUnits();
						if (InGameHUD.Instance != null)
							InGameHUD.Instance.ShowFeedbackText("Command: Stop Current Action", new Color(0.9f, 0.2f, 0.2f));
						GetViewport().SetInputAsHandled();
						return;
					case Key.H:
						HoldSelectedUnits();
						if (InGameHUD.Instance != null)
							InGameHUD.Instance.ShowFeedbackText("Command: Hold Position", new Color(0.9f, 0.8f, 0.1f));
						GetViewport().SetInputAsHandled();
						return;
					case Key.B:
						if (InGameHUD.Instance != null)
						{
							InGameHUD.Instance.ShowFeedbackText("Build Mode: Select Building structures", new Color(0.3f, 0.8f, 1.0f));
							InGameHUD.Instance.EnterBuildSubMenu();
						}
						GetViewport().SetInputAsHandled();
						return;
					case Key.C:
						if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
						{
							EnterBuildingPlacement("castle");
							GetViewport().SetInputAsHandled();
							return;
						}
						break;
					case Key.T:
						if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
						{
							EnterBuildingPlacement("tower");
							GetViewport().SetInputAsHandled();
							return;
						}
						break;
					case Key.Q:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && UnitHasAbility(unit, "fireball"))
							{
								EnterSpellTargeting("fireball");
								GetViewport().SetInputAsHandled();
							}
						}
						return;
					case Key.E:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && UnitHasAbility(unit, "lightning"))
							{
								EnterSpellTargeting("lightning");
								GetViewport().SetInputAsHandled();
							}
						}
						return;
					case Key.W:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && UnitHasAbility(unit, "holylight"))
							{
								EnterSpellTargeting("holylight");
								GetViewport().SetInputAsHandled();
							}
						}
						return;
					case Key.I:
						if (SelectedUnits.Count > 0)
						{
							var unit = SelectedUnits[CycleSelectionIndex];
							if (!unit.IsEnemy && !unit.IsBuilding)
							{
								UseHealingPotion(unit);
								GetViewport().SetInputAsHandled();
								return;
							}
						}
						break;
				}
			}
		}

		// ─── RIGHT-CLICK CONTEXT COMMAND (the #1 RTS UX staple) ──────────────────
		if (@event is InputEventMouseButton rightBtn && rightBtn.ButtonIndex == MouseButton.Right && rightBtn.Pressed && !IsMouseOverUI())
		{
			// Cancel any active targeting / placement modes first
			if (_activeSpellTargeting != null || _activeCommandTargeting != null || _activeBuildingPlacementType != null)
			{
				_activeSpellTargeting = null;
				_activeCommandTargeting = null;
				CancelBuildingPlacement();
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count == 0) goto SkipRightClick;

			// Check if ALL selected are enemies (shouldn't command enemies)
			bool anyFriendlySelected = false;
			foreach (var su in SelectedUnits)
			{
				if (!su.IsEnemy) { anyFriendlySelected = true; break; }
			}
			if (!anyFriendlySelected) goto SkipRightClick;

			// Determine if only a single friendly building is selected → set rally
			if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].IsBuilding)
			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					SetRallyPoint(SelectedUnits[0], hit["position"].AsVector3());
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// Mobile units: smart right-click
			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);
					bool shiftHeld = Input.IsKeyPressed(Key.Shift);

					if (clickedUnit != null && clickedUnit.IsEnemy && clickedUnit.Visible)
					{
						IssueAttackCommand(clickedUnit);
					}
					else if (clickedUnit != null && !clickedUnit.IsEnemy && clickedUnit != SelectedUnits.Find(u => !u.IsEnemy))
					{
						// Right-click on friendly unit → follow / heal
						IssueFollowCommand(clickedUnit);
					}
					else if (clickedProp != null && (clickedProp.PropId == "goldmine" || clickedProp.PropId == "tree" || clickedProp.PropId == "rock"))
					{
						// Right-click on resource node → gather!
						IssueGatherCommand(clickedProp);
					}
					else
					{
						// Right-click on ground
						if (shiftHeld)
						{
							// Shift+right-click: queue additional waypoint
							IssueMoveCommandQueued(hitPos);
						}
						else
						{
							IssueMoveCommand(hitPos);
						}
					}
					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}
		SkipRightClick:

		if (@event is InputEventMouseButton mouseBtn)
		{
			if (mouseBtn.ButtonIndex == MouseButton.Left)
			{
				if (mouseBtn.Pressed)
				{
					GD.Print($"[GameHost] Unhandled left-click press at position: {mouseBtn.Position}");
					
					if (mouseBtn.DoubleClick)
					{
						PerformDoubleClickSelection(mouseBtn.Position);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (ActivePingMode)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							AddMinimapPing(hit["position"].AsVector3());
						}
						ActivePingMode = false;
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (_activeBuildingPlacementType != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							hitPos.Y = 0f;
							ExecuteBuildingPlacement(_activeBuildingPlacementType, hitPos);
						}
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (_activeSpellTargeting != null)
					{
						// Casting spell (handled immediately)
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							ExecuteSpellCast(_activeSpellTargeting, hit["position"].AsVector3());
						}
						_activeSpellTargeting = null;
						Input.SetCustomMouseCursor(null); // Reset cursor
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else if (_activeCommandTargeting != null)
					{
						// Casting command (attack or move)
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							var collider = hit["collider"].As<Node>();
							var clickedUnit = FindUnit3DInParentChain(collider);

							if (_activeCommandTargeting == "attack")
							{
								if (clickedUnit != null && clickedUnit.Entity != Entity.Null && clickedUnit.IsEnemy)
								{
									IssueAttackCommand(clickedUnit);
								}
								else
								{
									IssueAttackMoveCommand(hitPos);
								}
							}
							else if (_activeCommandTargeting == "move")
							{
								IssueMoveCommand(hitPos);
							}
							else if (_activeCommandTargeting == "patrol")
							{
								IssuePatrolCommand(hitPos);
							}
							else if (_activeCommandTargeting == "rally")
							{
								if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].IsBuilding)
								{
									SetRallyPoint(SelectedUnits[0], hitPos);
								}
							}
						}
						_activeCommandTargeting = null;
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else
					{
						if (IsMouseOverUI()) return;
						// Start box selection drag
						_isDragging = true;
						_dragStart = mouseBtn.Position;
						_dragEnd = mouseBtn.Position;
					}
				}
				else if (_isDragging)
				{
					GD.Print($"[GameHost] Unhandled left-click release at position: {mouseBtn.Position}");
					// Release Left Click: End box selection drag
					_isDragging = false;
					if (InGameHUD.Instance != null)
					{
						InGameHUD.Instance.UpdateDragBox(Vector2.Zero, Vector2.Zero, false);
					}

					float dragDist = _dragStart.DistanceTo(_dragEnd);
					if (dragDist > DragThreshold)
					{
						PerformBoxSelection(_dragStart, _dragEnd);
					}
					else
					{
						PerformSingleClickSelection(_dragStart);
					}
				}
			}
			// Right Click: Command Move / Attack
			else if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
			{
				GD.Print($"[GameHost] Unhandled right-click press at position: {mouseBtn.Position}");
				
				if (_activeBuildingPlacementType != null)
				{
					CancelBuildingPlacement();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActivePingMode)
				{
					ActivePingMode = false;
					if (InGameHUD.Instance != null)
						InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
					GetViewport().SetInputAsHandled();
					return;
				}

				// Cancel active command/spell targeting
				if (_activeSpellTargeting != null || _activeCommandTargeting != null)
				{
					_activeSpellTargeting = null;
					_activeCommandTargeting = null;
					Input.SetCustomMouseCursor(null);
					Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					if (InGameHUD.Instance != null)
						InGameHUD.Instance.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
					return;
				}

				var hit = RaycastFromMouse(mouseBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);

					// Check if we are selecting a single friendly building to set its Rally Point
					if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].IsBuilding)
					{
						SetRallyPoint(SelectedUnits[0], hitPos);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (clickedUnit != null && clickedUnit.Entity != Entity.Null)
					{
						if (clickedUnit.IsEnemy)
						{
							IssueAttackCommand(clickedUnit);
						}
						else
						{
							IssueFollowCommand(clickedUnit);
						}
					}
					else if (clickedProp != null && (clickedProp.PropId == "goldmine" || clickedProp.PropId == "tree" || clickedProp.PropId == "rock"))
					{
						IssueGatherCommand(clickedProp);
					}
					else
					{
						IssueMoveCommand(hitPos);
					}
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			_dragEnd = mouseMotion.Position;
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.UpdateDragBox(_dragStart, _dragEnd, true);
			}
		}
	}

	private void PerformSingleClickSelection(Vector2 clickPos)
	{
		GD.Print($"[GameHost] PerformSingleClickSelection at screen coordinate: {clickPos}");
		
		// Clear previous prop selection outline
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}
		SelectedProp = null;

		var hit = RaycastFromMouse(clickPos);
		if (hit != null && hit.ContainsKey("collider"))
		{
			var collider = hit["collider"].As<Node>();
			var clickedUnit = FindUnit3DInParentChain(collider);
			
			if (clickedUnit != null)
			{
				if (!clickedUnit.Visible)
				{
					ClearSelection();
					return;
				}
				if (clickedUnit.IsEnemy)
				{
					// If clicking enemy, single select it only
					ClearSelection();
					SelectUnit(clickedUnit);
				}
				else
				{
					// Clicking friendly unit
					bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
					if (ctrlPressed)
					{
						PerformDoubleClickSelection(clickPos);
						return;
					}

					bool shiftPressed = Input.IsKeyPressed(Key.Shift);
					bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;
					
					if (selectingEnemy)
					{
						ClearSelection();
						SelectUnit(clickedUnit);
					}
					else if (shiftPressed)
					{
						// Shift+click: Add/remove unit from selection
						if (SelectedUnits.Contains(clickedUnit))
						{
							DeselectUnit(clickedUnit);
						}
						else
						{
							SelectUnit(clickedUnit);
						}
					}
					else
					{
						ClearSelection();
						SelectUnit(clickedUnit);
					}
				}
			}
			else
			{
				// Check for Prop3D (resource nodes)
				var clickedProp = FindProp3DInParentChain(collider);
				if (clickedProp != null && (clickedProp.PropId == "goldmine" || clickedProp.PropId == "tree" || clickedProp.PropId == "rock"))
				{
					ClearSelection();
					SelectedProp = clickedProp;
					SelectedProp.IsSelected = true;
				}
				else
				{
					ClearSelection();
				}
			}
		}
		else
		{
			ClearSelection();
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void PerformBoxSelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		Vector2 min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
		Vector2 max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
		var dragRect = new Rect2(min, max - min);

		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;
		
		if (selectingEnemy || !shiftPressed)
		{
			ClearSelection();
		}

		foreach (var unit in AllUnits)
		{
			// Box selection ONLY selects player's units (never enemy units)
			if (unit.IsEnemy) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			if (dragRect.HasPoint(screenPos))
			{
				SelectUnit(unit);
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void SelectUnit(Unit3D unit)
	{
		if (!SelectedUnits.Contains(unit))
		{
			SelectedUnits.Add(unit);
			unit.IsSelected = true;
			OnUnitSelected?.Invoke(new UnitWrapper(unit.Entity, EcsWorld));
		}
	}

	private void ClearSelection()
	{
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}
		SelectedProp = null;

		foreach (var u in SelectedUnits)
		{
			u.IsSelected = false;
		}
		SelectedUnits.Clear();
		_cycleSelectionIndex = 0;
	}

	private Unit3D FindUnit3DInParentChain(Node node)
	{
		while (node != null)
		{
			if (node is Unit3D unit)
			{
				return unit;
			}
			node = node.GetParent();
		}
		return null;
	}

	private Godot.Collections.Dictionary RaycastFromMouse(Vector2 mousePos)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return null;

		var from = camera.ProjectRayOrigin(mousePos);
		var to = from + camera.ProjectRayNormal(mousePos) * 1000f;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		var result = spaceState.IntersectRay(query);

		if (result.Count == 0) return null;
		return result;
	}

	public void EnterCommandTargeting(string mode)
	{
		_activeCommandTargeting = mode;
		_activeSpellTargeting = null; // Clear spell targeting
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);
		
		if (InGameHUD.Instance != null)
		{
			if (mode == "attack")
			{
				InGameHUD.Instance.ShowFeedbackText("Attack Command: Click enemy to attack, or ground to Attack-Move", new Color(0.9f, 0.4f, 0.1f));
			}
			else if (mode == "move")
			{
				InGameHUD.Instance.ShowFeedbackText("Move Command: Click ground to move", new Color(0.2f, 0.9f, 0.3f));
			}
			else if (mode == "patrol")
			{
				InGameHUD.Instance.ShowFeedbackText("Patrol Command: Click ground to set patrol endpoint", new Color(0.7f, 0.4f, 1.0f));
			}
			else if (mode == "rally")
			{
				InGameHUD.Instance.ShowFeedbackText("Rally Command: Click ground to set building Rally Point", new Color(1.0f, 0.85f, 0.5f));
			}
		}
	}

	public void IssueMoveCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		// Spawn visual green marker
		SpawnTargetIndicator(targetPos, new Color(0.1f, 0.9f, 0.2f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Command: Move to position", new Color(0.2f, 0.9f, 0.3f));
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			int clientUnitIndex = 0;
			int clientCols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
			float clientSpacing = 2.2f;

			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				int row = clientUnitIndex / clientCols;
				int col = clientUnitIndex % clientCols;
				float offsetX = (col - clientCols * 0.5f + 0.5f) * clientSpacing;
				float offsetZ = row * clientSpacing;
				Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var moveTo = new MoveTo(new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z));
				if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
				else EcsWorld.Add(unit.Entity, moveTo);
				clientUnitIndex++;
			}
			QueueClientCommand("move", targetIds, targetPos, 0, "");
			return;
		}

		// Formation scatter: spread units in a grid so they don't stack
		int unitIndex = 0;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
		float spacing = 2.2f;

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue; // Buildings and enemies cannot move

			// Clear conflicting intents/states
			ClearUnitOrders(unit.Entity);

			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
			{
				// Offset in a grid pattern centered on targetPos
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var moveTo = new MoveTo(new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z));
				if (EcsWorld.Has<MoveTo>(unit.Entity))
					EcsWorld.Set(unit.Entity, moveTo);
				else
					EcsWorld.Add(unit.Entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueAttackCommand(Unit3D target)
	{
		if (SelectedUnits.Count == 0) return;

		// Spawn visual red marker
		SpawnTargetIndicator(target.GlobalPosition, new Color(0.9f, 0.1f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Command: Attack {target.UnitId.ToUpper()}", new Color(0.9f, 0.2f, 0.2f));
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				var attackTarget = new AttackTarget(target.Entity);
				if (EcsWorld.Has<AttackTarget>(unit.Entity)) EcsWorld.Set(unit.Entity, attackTarget);
				else EcsWorld.Add(unit.Entity, attackTarget);
			}
			QueueClientCommand("attack", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue; // Buildings and enemies cannot move to attack

			// Clear conflicting intents/states
			ClearUnitOrders(unit.Entity);

			// Add/Set AttackTarget component
			var attackTarget = new AttackTarget(target.Entity);
			if (EcsWorld.Has<AttackTarget>(unit.Entity))
				EcsWorld.Set(unit.Entity, attackTarget);
			else
				EcsWorld.Add(unit.Entity, attackTarget);
		}
	}

	public void IssueFollowCommand(Unit3D target)
	{
		if (SelectedUnits.Count == 0 || target == null || target.Entity == Entity.Null) return;

		// Spawn visual blue marker
		SpawnTargetIndicator(target.GlobalPosition, new Color(0.2f, 0.6f, 1.0f));

		if (InGameHUD.Instance != null)
		{
			// Check if selected units contain priest to show heal feedback
			bool hasPriest = false;
			foreach (var unit in SelectedUnits)
			{
				if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
				{
					hasPriest = true;
					break;
				}
			}
			if (hasPriest)
			{
				InGameHUD.Instance.ShowFeedbackText($"Priest: Healing support target {target.UnitId.ToUpper()}", new Color(0.2f, 0.9f, 0.3f));
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText($"Command: Follow {target.UnitId.ToUpper()}", new Color(0.2f, 0.6f, 1.0f));
			}
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy || unit.Entity == target.Entity) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
				{
					var healTarget = new HealingTarget(target.Entity);
					EcsWorld.Add(unit.Entity, healTarget);
				}
				else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
				{
					var follow = new Realm.Ecs.Components.Movement.Follow(target.Entity);
					if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity)) EcsWorld.Set(unit.Entity, follow);
					else EcsWorld.Add(unit.Entity, follow);
				}
			}
			QueueClientCommand("follow", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
			return;
		}


		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.Entity == target.Entity) continue; // Cannot follow itself or if building/enemy

			// Clear conflicting intents/states
			ClearUnitOrders(unit.Entity);

			if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
			{
				var healTarget = new HealingTarget(target.Entity);
				EcsWorld.Add(unit.Entity, healTarget);
			}
			else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
			{
				var follow = new Realm.Ecs.Components.Movement.Follow(target.Entity);
				if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity))
					EcsWorld.Set(unit.Entity, follow);
				else
					EcsWorld.Add(unit.Entity, follow);
			}
		}
	}

	public void IssueAttackMoveCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		// Spawn visual orange-red marker
		SpawnTargetIndicator(targetPos, new Color(0.9f, 0.5f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Command: Attack-Move to position", new Color(0.9f, 0.5f, 0.1f));
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			// Clear conflicting intents/states
			ClearUnitOrders(unit.Entity);

			// Set AttackMove component
			var attackMove = new Realm.Ecs.Components.Movement.AttackMove(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
			if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity))
				EcsWorld.Set(unit.Entity, attackMove);
			else
				EcsWorld.Add(unit.Entity, attackMove);

			// Also set MoveTo to drive physics movement towards the destination
			var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
			if (EcsWorld.Has<MoveTo>(unit.Entity))
				EcsWorld.Set(unit.Entity, moveTo);
			else
				EcsWorld.Add(unit.Entity, moveTo);
		}
	}

	public void StopSelectedUnits()
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);
				unit.Velocity = Vector3.Zero;
			}
			QueueClientCommand("stop", targetIds, Vector3.Zero, 0, "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsEnemy) continue;

			ClearUnitOrders(unit.Entity);

			unit.Velocity = Vector3.Zero;
		}
	}

	public void HoldSelectedUnits()
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);
				unit.Velocity = Vector3.Zero;
				if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity))
					EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity);
			}
			QueueClientCommand("hold", targetIds, Vector3.Zero, 0, "");
			return;
		}

		StopSelectedUnits();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity))
				EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(unit.Entity);
		}
	}

	/// <summary>Issues a patrol command between the unit's current position and a target destination.</summary>
	public void IssuePatrolCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.6f, 0.3f, 1.0f));
		InGameHUD.Instance?.ShowFeedbackText("Command: Patrol Route Set", new Color(0.7f, 0.4f, 1.0f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			int clientUnitIndex = 0;
			int clientCols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
			float clientSpacing = 2.2f;

			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
				ClearUnitOrders(unit.Entity);

				if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
				{
					int row = clientUnitIndex / clientCols;
					int col = clientUnitIndex % clientCols;
					float offsetX = (col - clientCols * 0.5f + 0.5f) * clientSpacing;
					float offsetZ = row * clientSpacing;

					var unitPos = unit.GlobalPosition;
					var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
					var patrolB = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

					var patrol = new Patrol(patrolA, patrolB);
					if (EcsWorld.Has<Patrol>(unit.Entity)) EcsWorld.Set(unit.Entity, patrol);
					else EcsWorld.Add(unit.Entity, patrol);

					var moveTo = new MoveTo(patrolB);
					if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
					else EcsWorld.Add(unit.Entity, moveTo);

					clientUnitIndex++;
				}
			}
			QueueClientCommand("patrol", targetIds, targetPos, 0, "");
			return;
		}

		int unitIndex = 0;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
		float spacing = 2.2f;

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;

			// Clear conflicting intents/states
			ClearUnitOrders(unit.Entity);

			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;

				var unitPos = unit.GlobalPosition;
				var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
				var patrolB = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var patrol = new Patrol(patrolA, patrolB);
				if (EcsWorld.Has<Patrol>(unit.Entity)) EcsWorld.Set(unit.Entity, patrol);
				else EcsWorld.Add(unit.Entity, patrol);

				// Start moving toward B immediately
				var moveTo = new MoveTo(patrolB);
				if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
				else EcsWorld.Add(unit.Entity, moveTo);

				unitIndex++;
			}
		}
	}

	/// <summary>Queues an additional move waypoint without clearing existing MoveTo (Shift+right-click).</summary>
	public void IssueMoveCommandQueued(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.2f, 0.7f, 1.0f));
		InGameHUD.Instance?.ShowFeedbackText("Command: Queued Move (Shift+Click)", new Color(0.2f, 0.7f, 1.0f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			int clientUnitIndex = 0;
			int clientCols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
			float clientSpacing = 2.2f;

			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy) continue;
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity)) continue;

				targetIds.Add(GetServerEntityId(unit.Entity));

				bool alreadyMoving = EcsWorld.Has<MoveTo>(unit.Entity);
				if (!alreadyMoving)
				{
					ClearUnitOrders(unit.Entity);
				}

				int row = clientUnitIndex / clientCols;
				int col = clientUnitIndex % clientCols;
				float offsetX = (col - clientCols * 0.5f + 0.5f) * clientSpacing;
				float offsetZ = row * clientSpacing;
				Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var targetVec = new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z);

				if (alreadyMoving)
				{
					if (EcsWorld.Has<WaypointQueue>(unit.Entity))
					{
						var q = EcsWorld.Get<WaypointQueue>(unit.Entity);
						if (q.Waypoints == null) q.Waypoints = new List<System.Numerics.Vector3>();
						q.Waypoints.Add(targetVec);
						EcsWorld.Set(unit.Entity, q);
					}
					else
					{
						var q = new WaypointQueue(new List<System.Numerics.Vector3> { targetVec });
						EcsWorld.Add(unit.Entity, q);
					}
				}
				else
				{
					var moveTo = new MoveTo(targetVec);
					if (EcsWorld.Has<MoveTo>(unit.Entity)) EcsWorld.Set(unit.Entity, moveTo);
					else EcsWorld.Add(unit.Entity, moveTo);
				}

				clientUnitIndex++;
			}
			QueueClientCommand("move_queued", targetIds, targetPos, 0, "");
			return;
		}

		int unitIndex = 0;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(SelectedUnits.Count));
		float spacing = 2.2f;

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity)) continue;

			// Only queue if currently moving, otherwise just move
			bool alreadyMoving = EcsWorld.Has<MoveTo>(unit.Entity);
			if (!alreadyMoving)
			{
				// Clear conflicting states before first queued order
				ClearUnitOrders(unit.Entity);
			}

			int row = unitIndex / cols;
			int col = unitIndex % cols;
			float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
			float offsetZ = row * spacing;
			Vector3 scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

			var targetVec = new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z);

			if (alreadyMoving)
			{
				if (EcsWorld.Has<WaypointQueue>(unit.Entity))
				{
					var q = EcsWorld.Get<WaypointQueue>(unit.Entity);
					if (q.Waypoints == null) q.Waypoints = new List<System.Numerics.Vector3>();
					q.Waypoints.Add(targetVec);
					EcsWorld.Set(unit.Entity, q);
				}
				else
				{
					var q = new WaypointQueue(new List<System.Numerics.Vector3> { targetVec });
					EcsWorld.Add(unit.Entity, q);
				}
			}
			else
			{
				var moveTo = new MoveTo(targetVec);
				if (EcsWorld.Has<MoveTo>(unit.Entity))
					EcsWorld.Set(unit.Entity, moveTo);
				else
					EcsWorld.Add(unit.Entity, moveTo);
			}

			unitIndex++;
		}
	}

	private void SelectAllBuildings()
	{
		ClearSelection();
		int count = 0;
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.IsBuilding)
			{
				SelectUnit(unit);
				count++;
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		InGameHUD.Instance?.ShowFeedbackText($"Selected {count} Buildings", new Color(0.9f, 0.7f, 0.2f));
	}

	private int _cycleSelectionIndex = 0;
	private void CycleSelectionFocus(bool reverse = false)
	{
		if (SelectedUnits.Count <= 1) return;
		if (reverse)
		{
			_cycleSelectionIndex = (_cycleSelectionIndex - 1 + SelectedUnits.Count) % SelectedUnits.Count;
		}
		else
		{
			_cycleSelectionIndex = (_cycleSelectionIndex + 1) % SelectedUnits.Count;
		}
		var focusUnit = SelectedUnits[_cycleSelectionIndex];
		// Visually highlight by centering camera
		var camera = GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(focusUnit.GlobalPosition.X, camera.GlobalPosition.Y, focusUnit.GlobalPosition.Z);
		}
		InGameHUD.Instance?.ShowFeedbackText($"Focused: {focusUnit.UnitId.ToUpper()} ({_cycleSelectionIndex + 1}/{SelectedUnits.Count})", new Color(0.5f, 1.0f, 0.5f));
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	/// <summary>Cycles camera focus to each friendly building (backtick/tilde key).</summary>
	private int _buildingCycleIndex = 0;
	private void CycleThroughBuildings()
	{
		var buildings = AllUnits.FindAll(u => !u.IsEnemy && u.IsBuilding);
		if (buildings.Count == 0) return;
		_buildingCycleIndex = (_buildingCycleIndex + 1) % buildings.Count;
		var building = buildings[_buildingCycleIndex];
		var camera = GetViewport().GetCamera3D();
		if (camera != null)
			camera.GlobalPosition = new Vector3(building.GlobalPosition.X, camera.GlobalPosition.Y, building.GlobalPosition.Z);
		SelectOnlyUnit(building);
		InGameHUD.Instance?.ShowFeedbackText($"Jumped to: {building.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.3f));
	}

	/// <summary>Delete: removes (self-destructs) the first selected player unit (dev/testing tool).</summary>
	private void DeleteSelectedUnits()
	{
		if (SelectedUnits.Count == 0) return;
		// Collect to avoid modifying during iteration
		var toDelete = new List<Unit3D>(SelectedUnits.FindAll(u => !u.IsEnemy));
		foreach (var unit in toDelete)
		{
			if (!EcsWorld.Has<Dead>(unit.Entity))
				EcsWorld.Add<Dead>(unit.Entity);
			CallDeferred("KillUnitDeferred", unit);
		}
		InGameHUD.Instance?.ShowFeedbackText($"Removed {toDelete.Count} unit(s)", new Color(0.9f, 0.3f, 0.3f));
	}

	// Deferred wrapper because KillUnit is private (GDScript interop shim not needed — calling directly)
	private void KillUnitDeferred(Unit3D unit)
	{
		if (AllUnits.Contains(unit))
			KillUnit(unit);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void SelectAllIdleUnits()
	{
		ClearSelection();
		int selectedCount = 0;
		foreach (var unit in AllUnits)
		{
			if (unit.IsEnemy || unit.IsBuilding) continue;
			
			bool isMovable = EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity);
			bool hasMoveTo = EcsWorld.Has<MoveTo>(unit.Entity);
			bool hasAttackTarget = EcsWorld.Has<AttackTarget>(unit.Entity);
			bool hasAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity);
			bool isGathering = EcsWorld.Has<Gatherer>(unit.Entity) && EcsWorld.Get<Gatherer>(unit.Entity).TargetNode != null;

			if (isMovable && !hasMoveTo && !hasAttackTarget && !hasAttackMove && !isGathering)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected {selectedCount} Idle Units", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	public void SelectAllMilitaryUnits()
	{
		ClearSelection();
		int selectedCount = 0;
		foreach (var unit in AllUnits)
		{
			if (unit.IsEnemy || unit.IsBuilding) continue;
			
			bool isMovable = EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity);
			if (isMovable)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected All Army ({selectedCount} Units)", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	private void PerformDoubleClickSelection(Vector2 clickPos)
	{
		GD.Print($"[GameHost] PerformDoubleClickSelection at screen coordinate: {clickPos}");
		var hit = RaycastFromMouse(clickPos);
		if (hit != null && hit.ContainsKey("collider"))
		{
			var collider = hit["collider"].As<Node>();
			var clickedUnit = FindUnit3DInParentChain(collider);
			
			if (clickedUnit != null && !clickedUnit.IsEnemy)
			{
				ClearSelection();
				
				// Find all friendly units of the same type on screen
				var camera = GetViewport().GetCamera3D();
				if (camera != null)
				{
					var windowSize = GetViewport().GetVisibleRect().Size;
					var screenRect = new Rect2(Vector2.Zero, windowSize);
					string targetUnitId = clickedUnit.UnitId;
					int count = 0;

					foreach (var unit in AllUnits)
					{
						if (!unit.IsEnemy && unit.UnitId == targetUnitId && !unit.IsBuilding)
						{
							var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
							if (screenRect.HasPoint(screenPos))
							{
								SelectUnit(unit);
								count++;
							}
						}
					}
					
					if (InGameHUD.Instance != null)
					{
						InGameHUD.Instance.ShowFeedbackText($"Selected {count} {targetUnitId.ToUpper()}s on screen", new Color(0.5f, 1.0f, 0.5f));
					}
				}
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void CenterCameraOnCastle()
	{
		Unit3D friendlyCastle = null;
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.UnitId == "castle")
			{
				friendlyCastle = unit;
				break;
			}
		}
		var camera = GetViewport().GetCamera3D();
		if (friendlyCastle != null && camera != null)
		{
			var zVector = camera.GlobalTransform.Basis.Z;
			if (Mathf.Abs(zVector.Y) > 0.001f)
			{
				float dist = (camera.GlobalPosition.Y - friendlyCastle.GlobalPosition.Y) / zVector.Y;
				camera.GlobalPosition = friendlyCastle.GlobalPosition + zVector * dist;
			}
			else
			{
				camera.GlobalPosition = new Vector3(friendlyCastle.GlobalPosition.X, camera.GlobalPosition.Y, friendlyCastle.GlobalPosition.Z);
			}
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Camera Centered on Castle", new Color(0.5f, 0.8f, 1.0f));
			}
		}
	}

	public void CenterCameraOnSelectedOrCastle()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		if (SelectedEditorObject is Node3D node3D && GodotObject.IsInstanceValid(node3D))
		{
			var zVector = camera.GlobalTransform.Basis.Z;
			if (Mathf.Abs(zVector.Y) > 0.001f)
			{
				float dist = (camera.GlobalPosition.Y - node3D.GlobalPosition.Y) / zVector.Y;
				camera.GlobalPosition = node3D.GlobalPosition + zVector * dist;
			}
			else
			{
				camera.GlobalPosition = new Vector3(node3D.GlobalPosition.X, camera.GlobalPosition.Y, node3D.GlobalPosition.Z);
			}
			MapEditorHUD.Instance?.ShowFeedbackExternal("Centered Camera on Selected Object");
			return;
		}

		CenterCameraOnCastle();
	}

	public void TriggerCopyFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea)
		{
			PerformCopyArea();
			return;
		}
		if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
		{
			if (SelectedEditorObject is Unit3D unit)
			{
				_copiedObject = new CopiedObjectTemplate {
					Type = "unit",
					Id = unit.UnitId,
					Rotation = unit.RotationDegrees.Y,
					Scale = unit.Scale.X,
					IsEnemy = unit.IsEnemy
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
			}
			else if (SelectedEditorObject is Prop3D prop)
			{
				_copiedObject = new CopiedObjectTemplate {
					Type = "prop",
					Id = prop.PropId,
					Rotation = prop.RotationDegrees.Y,
					Scale = prop.Scale.X,
					IsEnemy = false
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
			}
			else if (SelectedEditorObject is Decal decal)
			{
				string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
				_copiedObject = new CopiedObjectTemplate {
					Type = "decal",
					Id = decalId,
					Rotation = decal.RotationDegrees.Y,
					Scale = decal.Scale.X,
					IsEnemy = false
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
			}
		}
		else
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an object or area first)");
		}
	}

	public void TriggerPasteFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
		{
			if (_copiedArea != null)
			{
				ActiveEditorTool = EditorTool.PasteArea;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
				MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
				return;
			}
		}
		if (_copiedObject != null)
		{
			if (_copiedObject.Value.Type == "unit")
			{
				ActiveEditorTool = EditorTool.PlaceUnit;
				_isPastingObject = true;
				ActivePlaceId = _copiedObject.Value.Id;
				PlaceUnitIsEnemy = _copiedObject.Value.IsEnemy;
				EditorPlacementRotation = _copiedObject.Value.Rotation;
				EditorPlacementScale = _copiedObject.Value.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceUnit);
			}
			else if (_copiedObject.Value.Type == "prop")
			{
				ActiveEditorTool = EditorTool.PlaceProp;
				_isPastingObject = true;
				ActivePlaceId = _copiedObject.Value.Id;
				EditorPlacementRotation = _copiedObject.Value.Rotation;
				EditorPlacementScale = _copiedObject.Value.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceProp);
			}
			else if (_copiedObject.Value.Type == "decal")
			{
				ActiveEditorTool = EditorTool.PlaceDecal;
				_isPastingObject = true;
				ActivePlaceId = _copiedObject.Value.Id;
				EditorPlacementRotation = _copiedObject.Value.Rotation;
				EditorPlacementScale = _copiedObject.Value.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceDecal);
			}
			MapEditorHUD.Instance?.ShowFeedbackExternal($"Paste Mode Active - Placing {_copiedObject.Value.Id.ToUpper()}");
		}
		else
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Paste (copy an object or area first)");
		}
	}

	private void AssignControlGroup(int index)
	{
		var groupUnits = new List<Unit3D>();
		foreach (var u in SelectedUnits)
		{
			if (!u.IsEnemy)
			{
				groupUnits.Add(u);
			}
		}
		_controlGroups[index] = groupUnits;
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Assigned {groupUnits.Count} units to Control Group {index}", new Color(0.5f, 0.8f, 1.0f));
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	public void RecallControlGroup(int index)
	{
		var group = _controlGroups[index];
		if (group == null || group.Count == 0) return;

		group.RemoveAll(u => !GodotObject.IsInstanceValid(u) || !AllUnits.Contains(u));
		if (group.Count == 0) return;

		ClearSelection();
		foreach (var u in group)
		{
			SelectUnit(u);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);

		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastGroupPressTime[index] < 0.3)
		{
			Vector3 sumPos = Vector3.Zero;
			foreach (var u in group)
			{
				sumPos += u.GlobalPosition;
			}
			Vector3 avgPos = sumPos / group.Count;
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(avgPos.X, camera.GlobalPosition.Y, avgPos.Z);
			}
		}
		_lastGroupPressTime[index] = now;
	}

	public void ClearTargetingModes()
	{
		_activeSpellTargeting = null;
		_activeCommandTargeting = null;
		_activeBuildingPlacementType = null;
		Input.SetCustomMouseCursor(null);
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}
	}

	public void CastSpellAt(string spellId, Vector3 position)
	{
		ExecuteSpellCast(spellId, position);
	}

	public void PlaceBuildingAt(string type, Vector3 position)
	{
		ExecuteBuildingPlacement(type, position);
	}

	private bool UnitHasAbility(Unit3D unit, string abilityId)
	{
		if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
		{
			if (meta.Abilities != null)
			{
				return Array.Exists(meta.Abilities, a => a == abilityId);
			}
		}
		if (abilityId == "holylight") return unit.UnitId == "priest";
		if (abilityId == "fireball" || abilityId == "lightning") return unit.UnitId == "tower";
		return false;
	}

	public void EnterSpellTargeting(string spellId)
	{
		if (SelectedUnits.Count == 0) return;
		var unit = SelectedUnits[CycleSelectionIndex];
		if (unit.IsEnemy) return;
		
		if (!UnitHasAbility(unit, spellId)) return;

		_activeSpellTargeting = spellId;
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);
		// Visually signal targeting mode by showing feedback
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Casting: Select Location for {spellId.ToUpper()}", new Color(1f, 0.7f, 0.1f));
		}
	}

	private void ExecuteSpellCast(string spellId, Vector3 position)
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			if (spellId == "fireball")
			{
				_fireballCooldown = FireballCooldownMax;
				SpawnFireblastEffect(position);
				SpawnTargetIndicator(position, new Color(0.9f, 0.3f, 0.1f));
			}
			else if (spellId == "lightning")
			{
				_lightningCooldown = LightningCooldownMax;
				SpawnLightningEffect(position);
				SpawnTargetIndicator(position, new Color(0.2f, 0.5f, 1f));
			}
			else if (spellId == "holylight")
			{
				_holyLightCooldown = HolyLightCooldownMax;
				SpawnHolyLightEffect(position);
				SpawnTargetIndicator(position, new Color(0.2f, 0.9f, 0.3f));
			}

			var targetIds = new List<int>();
			if (SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy)
			{
				targetIds.Add(GetServerEntityId(SelectedUnits[0].Entity));
			}
			QueueClientCommand("spell", targetIds, position, 0, spellId);
			return;
		}

		IUnit? caster = null;
		if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
		{
			caster = new UnitWrapper(SelectedUnits[0].Entity, EcsWorld);
		}
		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		if (spellId == "fireball")
		{
			if (_fireballCooldown > 0)
			{
				InGameHUD.Instance?.ShowFeedbackText($"Fireball on cooldown: {_fireballCooldown:F1}s remaining", new Color(0.9f, 0.4f, 0.1f));
				return;
			}
			_fireballCooldown = FireballCooldownMax;

			// Spawn Fire Blast Visual Effect
			SpawnFireblastEffect(position);
			SpawnTargetIndicator(position, new Color(0.9f, 0.3f, 0.1f));
			
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Cast: Fireball Spell", new Color(0.9f, 0.3f, 0.1f));
				UIManager.Instance.PlayClickSound();
			}

			// Deal damage to all units in range
			DealSpellDamageAOE(position, 4.0f, 50f);
		}
		else if (spellId == "lightning")
		{
			if (_lightningCooldown > 0)
			{
				InGameHUD.Instance?.ShowFeedbackText($"Lightning on cooldown: {_lightningCooldown:F1}s remaining", new Color(0.2f, 0.6f, 1f));
				return;
			}
			_lightningCooldown = LightningCooldownMax;

			// Spawn Lightning Bolt Visual Effect
			SpawnLightningEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.5f, 1f));

			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Cast: Lightning Bolt", new Color(0.2f, 0.6f, 1f));
				UIManager.Instance.PlayClickSound();
			}

			// Deal damage to units in range
			DealSpellDamageAOE(position, 2.0f, 80f);
		}
		else if (spellId == "holylight")
		{
			if (_holyLightCooldown > 0)
			{
				InGameHUD.Instance?.ShowFeedbackText($"Holy Light on cooldown: {_holyLightCooldown:F1}s remaining", new Color(0.2f, 0.9f, 0.3f));
				return;
			}
			_holyLightCooldown = HolyLightCooldownMax;

			// Spawn Holy Light Visual Effect
			SpawnHolyLightEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.9f, 0.3f));

			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("Cast: Holy Light", new Color(0.2f, 0.9f, 0.3f));
				UIManager.Instance.PlayClickSound();
			}

			// Heal friendly units in range
			HealAOE(position, 4.0f, 60f);
		}
	}

	private void SpawnFireblastEffect(Vector3 position)
	{
		SpawnSpritesheetEffect("res://Assets/2d/SpellSpritesheets/solar_flare_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.05f, 6f);
	}

	private void SpawnLightningEffect(Vector3 position)
	{
		SpawnSpritesheetEffect("res://Assets/2d/SpellSpritesheets/arcane_surge_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.035f, 6f);
	}

	private void SpawnSpritesheetEffect(string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		var texture = GD.Load<Texture2D>(texturePath);
		if (texture == null) return;

		int totalFrames = columns * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.None);
		frames.SetAnimationSpeed("play", 1.0f / secondsPerFrame);

		var atlasBase = new AtlasTexture();
		atlasBase.Atlas = texture;
		int frameWidth = texture.GetWidth() / columns;
		int frameHeight = texture.GetHeight() / rows;

		for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
		{
			int col = frameIndex % columns;
			int row = frameIndex / columns;
			var atlasFrame = new AtlasTexture();
			atlasFrame.Atlas = texture;
			atlasFrame.Region = new Rect2(col * frameWidth, row * frameHeight, frameWidth, frameHeight);
			frames.AddFrame("play", atlasFrame);
		}

		var sprite = new AnimatedSprite3D();
		sprite.SpriteFrames = frames;
		sprite.Animation = "play";
		sprite.Position = worldPosition;
		sprite.PixelSize = sizeInWorldUnits / frameWidth;
		sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		sprite.Transparent = true;
		sprite.AlphaCut = SpriteBase3D.AlphaCutMode.Disabled;
		AddChild(sprite);
		sprite.Play("play");
		sprite.AnimationFinished += sprite.QueueFree;
	}

	private void DealSpellDamageAOE(Vector3 position, float radius, float damage, bool enemyOnly = true)
	{
		// Snapshot AllUnits to avoid collection modification during iteration
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			// Skip friendly units by default (enemyOnly=true prevents friendly-fire)
			if (enemyOnly && !unit.IsEnemy) continue;
			if (unit.GlobalPosition.DistanceTo(position) <= radius)
			{
				if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Health>(unit.Entity))
				{
					if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(unit.Entity)) continue;

					IUnit? caster = null;
					if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
					{
						caster = new UnitWrapper(SelectedUnits[0].Entity, EcsWorld);
					}
					if (caster != null)
					{
						_lastAttacker[unit.Entity.Id] = ((UnitWrapper)caster).Entity;
					}
					OnUnitDamaged?.Invoke(new UnitWrapper(unit.Entity, EcsWorld), caster ?? new UnitWrapper(unit.Entity, EcsWorld), damage);

					var hp = EcsWorld.Get<Health>(unit.Entity);
					float newHp = Math.Max(0, hp.Current - damage);
					EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));
					
					// Trigger feedback animation on Unit3D if alive or kill it
					if (newHp <= 0)
					{
						KillUnit(unit);
					}
					else
					{
						FlashDamageUnit(unit);
					}
				}
			}
		}

		// Refresh HUD in case selection stats changed
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void FlashDamageUnit(Unit3D unit)
	{
		// Squash and stretch the unit to show hit feedback
		var tween = CreateTween();
		unit.Scale = new Vector3(0.8f, 1.3f, 0.8f);
		tween.TweenProperty(unit, "scale", new Vector3(1.0f, 1.0f, 1.0f), 0.25f);
	}

	private void SpawnHealVisualEffect(Vector3 start, Vector3 target)
	{
		var orb = new MeshInstance3D();
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = 0.15f;
		sphereMesh.Height = 0.3f;
		orb.Mesh = sphereMesh;
		orb.Position = start + new Vector3(0, 1.5f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.2f, 0.9f, 0.3f);
		material.EmissionEnabled = true;
		material.Emission = new Color(0.1f, 0.8f, 0.2f);
		orb.MaterialOverride = material;

		AddChild(orb);

		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);

		var tween = CreateTween();
		float travelTime = Mathf.Clamp(start.DistanceTo(target) / 25f, 0.2f, 0.8f);
		tween.TweenProperty(orb, "global_position", targetPos, travelTime);
		tween.Chain().TweenCallback(Callable.From(orb.QueueFree));
	}

	private void FlashHealUnit(Unit3D unit)
	{
		var tween = CreateTween();
		unit.Scale = new Vector3(0.9f, 1.25f, 0.9f);
		tween.TweenProperty(unit, "scale", new Vector3(1.0f, 1.0f, 1.0f), 0.25f);
	}

	private void SpawnHolyLightEffect(Vector3 position)
	{
		var cylinder = new MeshInstance3D();
		var cylinderMesh = new CylinderMesh();
		cylinderMesh.TopRadius = 2.0f;
		cylinderMesh.BottomRadius = 2.0f;
		cylinderMesh.Height = 8.0f;
		cylinder.Mesh = cylinderMesh;
		cylinder.Position = position + new Vector3(0, 4.0f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(1.0f, 0.9f, 0.3f, 0.6f);
		material.EmissionEnabled = true;
		material.Emission = new Color(0.9f, 0.8f, 0.2f);
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		cylinder.MaterialOverride = material;

		AddChild(cylinder);

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(cylinder, "scale:x", 0.05f, 0.6f);
		tween.TweenProperty(cylinder, "scale:z", 0.05f, 0.6f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.6f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.6f);
		tween.Chain().TweenCallback(Callable.From(cylinder.QueueFree));
	}

	private void HealAOE(Vector3 position, float radius, float healAmount)
	{
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.GlobalPosition.DistanceTo(position) <= radius)
			{
				if (EcsWorld.Has<Health>(unit.Entity))
				{
					var hp = EcsWorld.Get<Health>(unit.Entity);
					float newHp = Math.Min(hp.Max, hp.Current + healAmount);
					EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

					if (EcsWorld.Has<Unit3D>(unit.Entity))
					{
						FlashHealUnit(unit);
					}
				}
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void BuyHealingPotion(Entity castleEntity)
	{
		float costGold = 50f;
		if (InGameHUD.Instance != null && InGameHUD.Instance.Gold >= costGold)
		{
			if (EcsWorld.Has<Unit3D>(castleEntity))
			{
				var castle3D = EcsWorld.Get<Unit3D>(castleEntity);
				Unit3D targetUnit = null;
				float closestDist = 20f;

				foreach (var unit in AllUnits)
				{
					if (!unit.IsEnemy && !unit.IsBuilding)
					{
						float dist = castle3D.GlobalPosition.DistanceTo(unit.GlobalPosition);
						if (dist < closestDist)
						{
							closestDist = dist;
							targetUnit = unit;
						}
					}
				}

				if (targetUnit == null && SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy && !SelectedUnits[0].IsBuilding)
				{
					targetUnit = SelectedUnits[0];
				}

				if (targetUnit != null && EcsWorld.Has<Inventory>(targetUnit.Entity))
				{
					InGameHUD.Instance.Gold -= costGold;
					var inv = EcsWorld.Get<Inventory>(targetUnit.Entity);
					EcsWorld.Set(targetUnit.Entity, new Inventory(inv.Potions + 1));

					InGameHUD.Instance.ShowFeedbackText($"Bought Healing Potion for {targetUnit.UnitId.ToUpper()}!", new Color(0.3f, 0.9f, 0.4f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
				else
				{
					InGameHUD.Instance.ShowFeedbackText("Cannot buy potion: No friendly combat units nearby!", new Color(1.0f, 0.2f, 0.2f));
					UIManager.Instance?.PlayWarningSound();
				}
			}
		}
		else
		{
			InGameHUD.Instance?.ShowFeedbackText("Cannot buy potion: Insufficient gold!", new Color(1.0f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public void UseHealingPotion(Unit3D unit)
	{
		if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Inventory>(unit.Entity))
		{
			var inv = EcsWorld.Get<Inventory>(unit.Entity);
			if (inv.Potions > 0)
			{
				var hp = EcsWorld.Get<Health>(unit.Entity);
				if (hp.Current >= hp.Max)
				{
					InGameHUD.Instance?.ShowFeedbackText("Unit is already at full health!", new Color(0.8f, 0.8f, 0.8f));
					UIManager.Instance?.PlayWarningSound();
					return;
				}

				EcsWorld.Set(unit.Entity, new Inventory(inv.Potions - 1));
				float newHp = Math.Min(hp.Max, hp.Current + 50f);
				EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

				InGameHUD.Instance?.ShowFeedbackText($"{unit.UnitId.ToUpper()} used Healing Potion (+50 HP)!", new Color(0.3f, 0.9f, 0.4f));
				SpawnHolyLightEffect(unit.GlobalPosition);
				FlashHealUnit(unit);

				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}

	private void KillUnit(Unit3D unit)
	{
		IUnit? killer = null;
		if (EcsWorld.IsAlive(unit.Entity))
		{
			if (_lastAttacker.TryGetValue(unit.Entity.Id, out var killerEntity) && EcsWorld.IsAlive(killerEntity))
			{
				killer = new UnitWrapper(killerEntity, EcsWorld);
			}
			_lastAttacker.Remove(unit.Entity.Id);
			OnUnitDied?.Invoke(new UnitWrapper(unit.Entity, EcsWorld), killer);
			
			int id = unit.Entity.Id;
			_unitScale.Remove(id);
		}

		if (SelectedUnits.Contains(unit))
		{
			SelectedUnits.Remove(unit);
		}
		AllUnits.Remove(unit);

		// Award gold bounty to player when they kill an enemy unit
		if (unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var bountyMeta) && bountyMeta.GoldBounty > 0f)
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold = Math.Min(ResourceCap, InGameHUD.Instance.Gold + bountyMeta.GoldBounty);
			}
		}

		// Decrement population for dead player units
		if (!unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var killMeta))
		{
			if (unit.UnitId == "castle")
			{
				MaxPopulation = Math.Max(0, MaxPopulation - 20);
			}
			if (!EcsWorld.Has<BypassPopulationTag>(unit.Entity))
			{
				_currentPopulation = Math.Max(0, _currentPopulation - killMeta.PopCost);
			}
		}

		if (_multiplayerActive)
		{
			if (_clientToServerEntityMap.TryGetValue(unit.Entity.Id, out int serverId))
			{
				_serverToClientEntityMap.Remove(serverId);
			}
			_clientToServerEntityMap.Remove(unit.Entity.Id);
		}

		if (EcsWorld.IsAlive(unit.Entity))
		{
			EcsWorld.Destroy(unit.Entity);
		}

		// Visual scale down and sink into ground, then delete
		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(unit, "position:y", -3.0f, 1.0f);
		tween.TweenProperty(unit, "scale", Vector3.Zero, 1.0f);
		tween.Chain().TweenCallback(Callable.From(unit.QueueFree));

		// Check for Victory / Defeat conditions when a castle is destroyed
		if (unit.UnitId == "castle")
		{
			if (unit.IsEnemy)
			{
				// Enemy Castle destroyed -> Victory!
				GD.Print("[GameHost] Enemy Castle destroyed! Player wins!");
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
			}
			else
			{
				// Player Castle destroyed -> Defeat!
				GD.Print("[GameHost] Player Castle destroyed! Player loses!");
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
			}
		}

		GD.Print($"Unit {unit.Name} died.");
	}

	private void SpawnTargetIndicator(Vector3 position, Color color)
	{
		var meshInstance = new MeshInstance3D();
		var torusMesh = new TorusMesh();
		torusMesh.InnerRadius = 0.8f;
		torusMesh.OuterRadius = 1.0f;
		meshInstance.Mesh = torusMesh;
		meshInstance.Position = position + new Vector3(0, 0.05f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = color;
		material.EmissionEnabled = true;
		material.Emission = color;
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		meshInstance.MaterialOverride = material;

		AddChild(meshInstance);

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(meshInstance, "scale", new Vector3(1.6f, 0.1f, 1.6f), 0.35f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.35f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.35f);
		tween.Chain().TweenCallback(Callable.From(meshInstance.QueueFree));
	}

	public void EnterBuildingPlacement(string type)
	{
		_activeSpellTargeting = null;
		_activeCommandTargeting = null;
		ActivePingMode = false;

		_activeBuildingPlacementType = type;
		if (InGameHUD.Instance != null)
		{
			var meta = UnitRegistry[type];
			InGameHUD.Instance.ShowFeedbackText($"Place Building: Select Location for {meta.Name}", new Color(0.3f, 0.8f, 1.0f));
		}
	}

	public void CancelBuildingPlacement()
	{
		_activeBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Building Placement Cancelled", new Color(0.8f, 0.8f, 0.8f));
		}
	}

	private void ExecuteBuildingPlacement(string type, Vector3 position)
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var bldMeta = UnitRegistry[type];
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold -= bldMeta.CostGold;
				InGameHUD.Instance.Wood -= bldMeta.CostWood;
				InGameHUD.Instance.Stone -= bldMeta.CostStone;
				InGameHUD.Instance.ShowFeedbackText($"Constructing {bldMeta.Name}...", new Color(0.3f, 0.9f, 0.4f));
			}
			QueueClientCommand("build", new List<int>(), position, 0, type);
			_activeBuildingPlacementType = null;
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.QueueFree();
				_buildingPreviewMesh = null;
			}
			return;
		}

		var meta = UnitRegistry[type];
		if (InGameHUD.Instance != null)
		{
			// Check for collision/obstruction with existing units or buildings
			float clearance = type == "castle" ? 7f : 4f;
			foreach (var unit in AllUnits)
			{
				if (GodotObject.IsInstanceValid(unit))
				{
					float dist = position.DistanceTo(unit.GlobalPosition);
					if (dist < clearance)
					{
						InGameHUD.Instance.ShowFeedbackText("Cannot construct: Area is obstructed!", new Color(1.0f, 0.2f, 0.2f));
						UIManager.Instance?.PlayWarningSound();
						_activeBuildingPlacementType = null;
						if (_buildingPreviewMesh != null)
						{
							_buildingPreviewMesh.QueueFree();
							_buildingPreviewMesh = null;
						}
						return;
					}
				}
			}

			if (InGameHUD.Instance.Gold >= meta.CostGold &&
				InGameHUD.Instance.Wood >= meta.CostWood &&
				InGameHUD.Instance.Stone >= meta.CostStone)
			{
				InGameHUD.Instance.Gold -= meta.CostGold;
				InGameHUD.Instance.Wood -= meta.CostWood;
				InGameHUD.Instance.Stone -= meta.CostStone;

				var playerOwner = _playerEntity.AsPlayerEntity(EcsWorld);
				string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(type, true);
				
				var bldEntity = CreateEcsUnit(type, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, position, playerOwner);
				SpawnUnit3D(bldEntity, type, modelPath, position, true, false);

				InGameHUD.Instance.ShowFeedbackText($"Constructed: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
				UIManager.Instance?.PlayClickSound();
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot construct: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}

		_activeBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}
	}

	private void UpdateBuildingPreview()
	{
		if (_activeBuildingPlacementType == null)
		{
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.QueueFree();
				_buildingPreviewMesh = null;
			}
			return;
		}

		var mousePos = GetViewport().GetMousePosition();
		var hit = RaycastFromMouse(mousePos);
		if (hit != null && hit.ContainsKey("position"))
		{
			var hitPos = hit["position"].AsVector3();
			hitPos.Y = 0f;

			bool isClear = true;
			float clearance = _activeBuildingPlacementType == "castle" ? 7f : 4f;
			foreach (var unit in AllUnits)
			{
				if (GodotObject.IsInstanceValid(unit))
				{
					float dist = hitPos.DistanceTo(unit.GlobalPosition);
					if (dist < clearance)
					{
						isClear = false;
						break;
					}
				}
			}

			var meta = UnitRegistry[_activeBuildingPlacementType];
			bool hasResources = InGameHUD.Instance != null &&
				InGameHUD.Instance.Gold >= meta.CostGold &&
				InGameHUD.Instance.Wood >= meta.CostWood &&
				InGameHUD.Instance.Stone >= meta.CostStone;

			bool canPlace = isClear && hasResources;

			if (_buildingPreviewMesh == null)
			{
				_buildingPreviewMesh = new MeshInstance3D();
				var boxMesh = new BoxMesh();
				boxMesh.Size = _activeBuildingPlacementType == "castle" ? new Vector3(8f, 4f, 8f) : new Vector3(4f, 8f, 4f);
				_buildingPreviewMesh.Mesh = boxMesh;
				
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = canPlace ? new Color(0.1f, 0.8f, 0.2f, 0.4f) : new Color(0.9f, 0.1f, 0.1f, 0.4f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				_buildingPreviewMesh.MaterialOverride = mat;
				AddChild(_buildingPreviewMesh);
			}
			else
			{
				if (_buildingPreviewMesh.MaterialOverride is StandardMaterial3D mat)
				{
					mat.AlbedoColor = canPlace ? new Color(0.1f, 0.8f, 0.2f, 0.4f) : new Color(0.9f, 0.1f, 0.1f, 0.4f);
				}
			}

			_buildingPreviewMesh.GlobalPosition = hitPos;
		}
		else
		{
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.GlobalPosition = new Vector3(9999f, 9999f, 9999f);
			}
		}
	}

	public void AddMinimapPing(Vector3 position)
	{
		ActivePings.Add(new MinimapPing
		{
			WorldPos = position,
			LifeTime = 0f,
			MaxLifeTime = 3.0f
		});

		SpawnPing3DEffect(position);

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"[ALERT PING] Signal at: {position.X:F0}, {position.Z:F0}", new Color(1.0f, 0.1f, 0.2f));
			UIManager.Instance?.PlayClickSound();
		}
	}

	private void SpawnPing3DEffect(Vector3 position)
	{
		var meshInstance = new MeshInstance3D();
		var torusMesh = new TorusMesh();
		torusMesh.InnerRadius = 2.0f;
		torusMesh.OuterRadius = 2.4f;
		meshInstance.Mesh = torusMesh;
		meshInstance.Position = position + new Vector3(0, 0.1f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(1.0f, 0.1f, 0.1f, 0.8f);
		material.EmissionEnabled = true;
		material.Emission = new Color(1.0f, 0.1f, 0.1f);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		meshInstance.MaterialOverride = material;

		AddChild(meshInstance);

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(meshInstance, "scale", new Vector3(4f, 0.1f, 4f), 0.8f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.8f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.8f);
		tween.Chain().TweenCallback(Callable.From(meshInstance.QueueFree));
	}

	private void SpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		var arrow = new MeshInstance3D();
		var cylinderMesh = new CylinderMesh();
		cylinderMesh.TopRadius = 0.05f;
		cylinderMesh.BottomRadius = 0.05f;
		cylinderMesh.Height = 0.6f;
		arrow.Mesh = cylinderMesh;
		arrow.Position = start + new Vector3(0, 1.2f, 0); // chest height

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.9f, 0.8f, 0.4f);
		material.EmissionEnabled = true;
		material.Emission = new Color(0.9f, 0.7f, 0.2f);
		arrow.MaterialOverride = material;

		// Orient arrow towards target
		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);
		if (arrow.Position.DistanceTo(targetPos) > 0.1f)
		{
			arrow.LookAtFromPosition(arrow.Position, targetPos, Vector3.Up);
			arrow.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(90f));
		}

		AddChild(arrow);

		// Tween to move from start to target
		var tween = CreateTween();
		float travelTime = Mathf.Clamp(start.DistanceTo(target) / 40f, 0.15f, 0.6f);
		tween.TweenProperty(arrow, "global_position", targetPos, travelTime);
		tween.Chain().TweenCallback(Callable.From(arrow.QueueFree));
	}

	public void TrainUnitAtCastle(string unitId)
	{
		var meta = UnitRegistry[unitId];
		if (InGameHUD.Instance == null) return;

		// 1. Find a selected friendly castle that has space in its queue (count < 5)
		Unit3D targetCastle = null;
		Realm.Ecs.Components.Core.ProductionQueue targetProd = default;
		bool foundCastle = false;

		foreach (var unit in SelectedUnits)
		{
			if (!unit.IsEnemy && unit.UnitId == "castle" && EcsWorld.IsAlive(unit.Entity))
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity))
				{
					var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(unit.Entity);
					if (prod.UnitIds.Count < 5)
					{
						targetCastle = unit;
						targetProd = prod;
						foundCastle = true;
						break;
					}
				}
				else
				{
					// No queue component yet, meaning count is 0, which is < 5
					targetCastle = unit;
					targetProd = new Realm.Ecs.Components.Core.ProductionQueue();
					targetProd.BuildTime = meta.ProductionTime;
					EcsWorld.Add(unit.Entity, targetProd);
					foundCastle = true;
					break;
				}
			}
		}

		if (!foundCastle)
		{
			// Check if we even have a castle selected
			bool hasCastleSelected = SelectedUnits.Exists(u => !u.IsEnemy && u.UnitId == "castle");
			if (hasCastleSelected)
			{
				InGameHUD.Instance.ShowFeedbackText("Training queue is full! (Max 5)", new Color(1f, 0.3f, 0.3f));
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot train unit: No Castle selected!", new Color(1f, 0.3f, 0.3f));
			}
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		// 2. Check population cap
		if (meta.PopCost > 0 && _currentPopulation + meta.PopCost > MaxPopulation)
		{
			InGameHUD.Instance.ShowFeedbackText($"Population cap reached! ({_currentPopulation}/{MaxPopulation})", new Color(1f, 0.3f, 0.3f));
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		// 3. Check resources
		if (InGameHUD.Instance.Gold >= meta.CostGold && 
			InGameHUD.Instance.Wood >= meta.CostWood && 
			InGameHUD.Instance.Stone >= meta.CostStone)
		{
			// 4. Deduct resources
			InGameHUD.Instance.Gold -= meta.CostGold;
			InGameHUD.Instance.Wood -= meta.CostWood;
			InGameHUD.Instance.Stone -= meta.CostStone;

			// 5. Reserve population and add to queue
			_currentPopulation += meta.PopCost;
			targetProd.UnitIds.Add(unitId);
			EcsWorld.Set(targetCastle.Entity, targetProd);

			InGameHUD.Instance.ShowFeedbackText($"Queued {meta.Name} ({_currentPopulation}/{MaxPopulation} pop)", new Color(0.2f, 0.8f, 1f));
			UIManager.Instance?.PlayClickSound();
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
		else
		{
			InGameHUD.Instance.ShowFeedbackText("Cannot train unit: Insufficient resources!", new Color(1f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public void CancelLastQueuedUnit(Entity castleEntity)
	{
		if (EcsWorld.IsAlive(castleEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
		{
			var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
			if (prod.UnitIds.Count > 0)
			{
				int lastIndex = prod.UnitIds.Count - 1;
				string cancelledId = prod.UnitIds[lastIndex];
				prod.UnitIds.RemoveAt(lastIndex);

				var meta = UnitRegistry[cancelledId];
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.Gold += meta.CostGold;
					InGameHUD.Instance.Wood += meta.CostWood;
					InGameHUD.Instance.Stone += meta.CostStone;
					// Refund population reservation
					_currentPopulation = Math.Max(0, _currentPopulation - meta.PopCost);
					InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
				}

				if (prod.UnitIds.Count == 0)
				{
					prod.CurrentProgress = 0f;
				}

				EcsWorld.Set(castleEntity, prod);

				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}

	public Unit3D SpawnUnitFromProduction(string unitId, Vector3 position, bool isEnemy, Vector3? rallyPoint = null, bool isFromQueue = false)
	{
		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
		
		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(unitId, meta.Speed == 0f);

		string name = meta.Name;
		if (isEnemy)
		{
			if (unitId == "worker") name = "Orc Worker";
			else if (unitId == "soldier") name = "Orc Raider";
			else if (unitId == "archer") name = "Dark Archer";
			else if (unitId == "priest") name = "Orc Shaman";
			else if (unitId == "castle") name = "Orc Stronghold";
			else if (unitId == "tower") name = "Orc Totem Tower";
		}

		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, meta.Speed == 0f, isEnemy, isFromQueue);

		if (rallyPoint.HasValue && meta.Speed > 0f)
		{
			var rpVal = rallyPoint.Value;
			var moveTo = new MoveTo(new System.Numerics.Vector3(rpVal.X, rpVal.Y, rpVal.Z));
			EcsWorld.Add(entity, moveTo);
		}
		return unit3D;
	}

	private void UpdateEnemyAI(float delta)
	{
		// Escalating difficulty: spawn interval shrinks from 15s → 6s over the first 5 minutes
		float timeFactor = Mathf.Clamp(GameElapsedTime / 300f, 0f, 1f); // 0..1 over 5 min
		double spawnInterval = Mathf.Lerp(15.0f, 6.0f, timeFactor);
		// Extra units per wave at later stages
		int unitsPerWave = 1 + (int)(timeFactor * 2f); // 1..3

		_enemySpawnTimer += (double)delta;
		if (_enemySpawnTimer >= spawnInterval)
		{
			_enemySpawnTimer = 0.0;
			
			// Find enemy castle
			Unit3D enemyCastle = null;
			foreach (var unit in AllUnits)
			{
				if (unit.IsEnemy && unit.UnitId == "castle")
				{
					enemyCastle = unit;
					break;
				}
			}
			
			if (enemyCastle != null)
			{
				for (int w = 0; w < unitsPerWave; w++)
				{
					// Spread spawn positions slightly
					float ox = (GD.Randf() - 0.5f) * 6f;
					float oz = (GD.Randf() - 0.5f) * 6f;
					Vector3 spawnPos = enemyCastle.GlobalPosition + new Vector3(-8 + ox, 0, -8 + oz);
					
					// Alternate between soldier, archer, priest; later waves spawn priests too
					string unitId;
					uint roll = GD.Randi() % 10;
					if (timeFactor > 0.7f && roll == 0)
						unitId = "priest"; // Enemy gets a shaman healer in late game
					else if (timeFactor > 0.4f && roll <= 1)
						unitId = "soldier"; // Heavy soldier bias mid-game
					else
						unitId = (GD.Randi() % 2 == 0) ? "soldier" : "archer";
					
					SpawnUnitFromProduction(unitId, spawnPos, true);
				}
				if (unitsPerWave > 1)
				{
					InGameHUD.Instance?.ShowFeedbackText($"ALERT: Enemy sending {unitsPerWave} units!", new Color(1.0f, 0.3f, 0.3f));
				}
			}
		}

		_enemyAiTimer += (double)delta;
		double marchInterval = Mathf.Lerp(20.0f, 12.0f, Mathf.Clamp(GameElapsedTime / 300f, 0f, 1f));
		if (_enemyAiTimer >= marchInterval)
		{
			_enemyAiTimer = 0.0;
			
			// Dynamically find the player's castle position
			Vector3 targetPos = new Vector3(-25, 0, -25); // fallback default
			foreach (var u in AllUnits)
			{
				if (!u.IsEnemy && u.UnitId == "castle" && GodotObject.IsInstanceValid(u))
				{
					targetPos = u.GlobalPosition;
					break;
				}
			}
			
			int attackingEnemiesCount = 0;
			foreach (var unit in AllUnits)
			{
				if (unit.IsEnemy && !unit.IsBuilding)
				{
					bool hasAttackTarget = EcsWorld.Has<AttackTarget>(unit.Entity);
					if (!hasAttackTarget)
					{
						var attackMove = new Realm.Ecs.Components.Movement.AttackMove(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
						if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity))
							EcsWorld.Set(unit.Entity, attackMove);
						else
							EcsWorld.Add(unit.Entity, attackMove);

						var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
						if (EcsWorld.Has<MoveTo>(unit.Entity))
							EcsWorld.Set(unit.Entity, moveTo);
						else
							EcsWorld.Add(unit.Entity, moveTo);
							
						attackingEnemiesCount++;
					}
				}
			}
			
			if (attackingEnemiesCount > 0 && InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText("ALERT: Orc Raider forces are marching towards your base!", new Color(1.0f, 0.2f, 0.2f));
			}
		}
	}

	public void UpgradeTower(Unit3D tower)
	{
		float costGold = 150f;
		float costStone = 100f;

		if (InGameHUD.Instance != null)
		{
			int currentLevel = 1;
			if (EcsWorld.Has<TowerUpgradeLevel>(tower.Entity))
			{
				currentLevel = EcsWorld.Get<TowerUpgradeLevel>(tower.Entity).Value;
			}
			
			if (currentLevel >= 3)
			{
				InGameHUD.Instance.ShowFeedbackText("Tower is already at maximum upgrade level (Level 3)!", new Color(1.0f, 0.3f, 0.3f));
				UIManager.Instance?.PlayWarningSound();
				return;
			}

			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Stone >= costStone)
			{
				InGameHUD.Instance.Gold -= costGold;
				InGameHUD.Instance.Stone -= costStone;

				if (EcsWorld.IsAlive(tower.Entity))
				{
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
					tower.Scale = new Vector3(newScale, newScale, newScale);
					SpawnTargetIndicator(tower.GlobalPosition, new Color(0.1f, 0.8f, 0.9f));
					
					InGameHUD.Instance.ShowFeedbackText($"Tower Upgraded to Level {newLevel}!", new Color(0.2f, 0.8f, 1.0f));
					UIManager.Instance?.PlayClickSound();
					
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void CycleCameraZoom()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera != null && camera is CameraControl camCtrl)
		{
			camCtrl.CycleZoom();
		}
	}

	public void SetRallyPoint(Unit3D building, Vector3 position)
	{
		if (EcsWorld.IsAlive(building.Entity))
		{
			var rp = new RallyPoint(new System.Numerics.Vector3(position.X, position.Y, position.Z));
			if (EcsWorld.Has<RallyPoint>(building.Entity))
			{
				EcsWorld.Set(building.Entity, rp);
			}
			else
			{
				EcsWorld.Add(building.Entity, rp);
			}

			// Spawn a visual target indicator at the rally point to show feedback
			SpawnTargetIndicator(position, new Color(1.0f, 0.82f, 0.55f)); // Gold indicator
			
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText($"Set Rally Point to: {position.X:F0}, {position.Z:F0}", new Color(1.0f, 0.85f, 0.5f));
			}

			// Update the building's visuals immediately
			building.UpdateRallyVisuals();
		}
	}

	public void CancelQueuedUnitAt(Entity castleEntity, int index)
	{
		if (EcsWorld.IsAlive(castleEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
		{
			var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
			if (index >= 0 && index < prod.UnitIds.Count)
			{
				string cancelledId = prod.UnitIds[index];
				prod.UnitIds.RemoveAt(index);

				var meta = UnitRegistry[cancelledId];
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.Gold += meta.CostGold;
					InGameHUD.Instance.Wood += meta.CostWood;
					InGameHUD.Instance.Stone += meta.CostStone;
					// Refund population reservation
					_currentPopulation = Math.Max(0, _currentPopulation - meta.PopCost);
					InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
				}

				if (index == 0)
				{
					prod.CurrentProgress = 0f;
					if (prod.UnitIds.Count > 0)
					{
						string nextUnitId = prod.UnitIds[0];
						prod.BuildTime = UnitRegistry[nextUnitId].ProductionTime;
					}
				}

				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}

	public void DeselectUnit(Unit3D unit)
	{
		if (SelectedUnits.Contains(unit))
		{
			SelectedUnits.Remove(unit);
			unit.IsSelected = false;
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void SelectOnlyUnit(Unit3D unit)
	{
		ClearSelection();
		SelectUnit(unit);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void BuyWeaponsUpgrade()
	{
		if (HasWeaponsUpgrade) return;
		
		float costGold = 150f;
		float costWood = 100f;
		
		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Wood >= costWood)
			{
				InGameHUD.Instance.Gold -= costGold;
				InGameHUD.Instance.Wood -= costWood;
				
				HasWeaponsUpgrade = true;
				
				// Apply to existing units
				var query = new QueryDescription().WithAll<Attack, Owner>().WithNone<Dead, Building>();
				EcsWorld.Query(in query, (Entity entity, ref Attack atk, ref Owner owner) =>
				{
					if (owner.PlayerEntity == _playerEntity.AsPlayerEntity(EcsWorld))
					{
						atk.Damage += 3f;
					}
				});
				
				InGameHUD.Instance.ShowFeedbackText("Weapons Upgrade Complete! +3 Damage to all units.", new Color(0.2f, 0.8f, 1.0f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void BuyShieldsUpgrade()
	{
		if (HasShieldsUpgrade) return;
		
		float costGold = 150f;
		float costStone = 100f;
		
		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Stone >= costStone)
			{
				InGameHUD.Instance.Gold -= costGold;
				InGameHUD.Instance.Stone -= costStone;
				
				HasShieldsUpgrade = true;
				
				// Apply to existing units
				var query = new QueryDescription().WithAll<Armor, Owner>().WithNone<Dead>();
				EcsWorld.Query(in query, (Entity entity, ref Armor arm, ref Owner owner) =>
				{
					if (owner.PlayerEntity == _playerEntity.AsPlayerEntity(EcsWorld))
					{
						arm.Value += 2f;
					}
				});
				
				InGameHUD.Instance.ShowFeedbackText("Plated Armor Upgrade Complete! +2 Armor to all units.", new Color(0.2f, 0.8f, 1.0f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void BuyHarvestingUpgrade()
	{
		if (HasHarvestingUpgrade) return;

		float costWood = 150f;
		float costStone = 100f;

		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Wood >= costWood && InGameHUD.Instance.Stone >= costStone)
			{
				InGameHUD.Instance.Wood -= costWood;
				InGameHUD.Instance.Stone -= costStone;

				HasHarvestingUpgrade = true;
				InGameHUD.Instance.ResourceGatherMultiplier = 1.5f;

				InGameHUD.Instance.ShowFeedbackText("Harvesting Upgrade Complete! Passive resource accumulation +50%.", new Color(0.2f, 0.8f, 1.0f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	// --- MAP EDITOR IMPLEMENTATION ---

	public void StartMapEditorMode()
	{
		IsMapEditorMode = true;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		
		ClearAllUnits();
		
		var groundNode = GetNodeOrNull("Ground");
		if (groundNode != null)
		{
			groundNode.QueueFree();
			RemoveChild(groundNode);
		}
		
		var terrainNode = new EditableTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;

		bool loaded = LoadMapFromFile();
		
		CreateBrushIndicator();
		CreateGridOverlay();
		InitializeCameraBoundsOverlay();
		UpdateDayNightVisuals(0.5f);
	}

	public void ExitMapEditorMode()
	{
		IsMapEditorMode = false;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		ClearEditorPreview();
		
		if (_brushIndicatorMesh != null)
		{
			_brushIndicatorMesh.QueueFree();
			_brushIndicatorMesh = null;
		}
		
		if (_gridOverlayMesh != null)
		{
			_gridOverlayMesh.QueueFree();
			_gridOverlayMesh = null;
		}

		if (_cameraBoundsOverlayMesh != null)
		{
			_cameraBoundsOverlayMesh.QueueFree();
			_cameraBoundsOverlayMesh = null;
		}
		
		var groundNode = GetNodeOrNull("Ground");
		if (groundNode != null)
		{
			groundNode.QueueFree();
			RemoveChild(groundNode);
		}
		
		CreateGround();
		SpawnInitialEntities();
	}

	private void ClearAllUnits()
	{
		SelectedUnits.Clear();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();
		_currentPopulation = 0;
		MaxPopulation = 0;

		// Clear props too
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop)
			{
				prop.QueueFree();
			}
		}
		AllProps.Clear();
		
		if (EcsWorld != null)
		{
			EcsWorld.Dispose();
			EcsWorld = World.Create();
			
			// Recreate player entities
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));

			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
		}
	}

	private void CreateBrushIndicator()
	{
		if (_brushIndicatorMesh != null) return;
		
		_brushIndicatorMesh = new MeshInstance3D();
		_brushIndicatorMesh.Name = "BrushIndicator";
		
		var torus = new TorusMesh();
		torus.InnerRadius = 0.95f;
		torus.OuterRadius = 1.05f;
		_brushIndicatorMesh.Mesh = torus;
		
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.15f, 0.65f, 1.0f, 0.3f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(0.15f, 0.65f, 1.0f) * 0.5f;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_brushIndicatorMesh.MaterialOverride = mat;
		
		AddChild(_brushIndicatorMesh);
		_brushIndicatorMesh.Visible = false;
	}

	public void UpdateBrushMesh()
	{
		if (_brushIndicatorMesh == null) return;
		if (EditorBrushIsSquare)
		{
			var plane = new PlaneMesh();
			plane.Size = new Vector2(2.0f, 2.0f);
			_brushIndicatorMesh.Mesh = plane;
		}
		else
		{
			var torus = new TorusMesh();
			torus.InnerRadius = 0.95f;
			torus.OuterRadius = 1.05f;
			_brushIndicatorMesh.Mesh = torus;
		}
	}

	private void UpdateBrushIndicator(Vector3 position)
	{
		if (_brushIndicatorMesh == null) return;
		
		_brushIndicatorMesh.Position = new Vector3(position.X, position.Y + 0.1f, position.Z);
		_brushIndicatorMesh.Scale = new Vector3(EditorBrushRadius, 0.1f, EditorBrushRadius);
		
		bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
							 ActiveEditorTool == EditorTool.Lower ||
							 ActiveEditorTool == EditorTool.Flatten ||
							 ActiveEditorTool == EditorTool.Smooth ||
							 ActiveEditorTool == EditorTool.Cliff ||
							 ActiveEditorTool == EditorTool.PaintGrass ||
							 ActiveEditorTool == EditorTool.PaintDirt ||
							 ActiveEditorTool == EditorTool.PaintRock ||
							 ActiveEditorTool == EditorTool.PaintSand ||
							 ActiveEditorTool == EditorTool.Noise ||
							 ActiveEditorTool == EditorTool.Ramp ||
							 ActiveEditorTool == EditorTool.PlacePropClump;
							 
		_brushIndicatorMesh.Visible = isTerrainTool;
	}

	public void ClearRampStartPosExternal()
	{
		_rampStartPos = null;
	}

	public struct MirroredTransform
	{
		public Vector3 Position;
		public float Rotation;
	}

	public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
	{
		var list = new List<MirroredTransform>();
		if (EditorMirrorMode == MirrorMode.None) return list;
		if (EditorMirrorMode == MirrorMode.Horizontal || EditorMirrorMode == MirrorMode.Both)
		{
			list.Add(new MirroredTransform {
				Position = new Vector3(-pos.X, pos.Y, pos.Z),
				Rotation = 180.0f - rotation
			});
		}
		if (EditorMirrorMode == MirrorMode.Vertical || EditorMirrorMode == MirrorMode.Both)
		{
			list.Add(new MirroredTransform {
				Position = new Vector3(pos.X, pos.Y, -pos.Z),
				Rotation = -rotation
			});
		}
		if (EditorMirrorMode == MirrorMode.Both)
		{
			list.Add(new MirroredTransform {
				Position = new Vector3(-pos.X, pos.Y, -pos.Z),
				Rotation = rotation + 180.0f
			});
		}
		return list;
	}

	private Node FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
	{
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
				{
					float dist = new Vector2(n3d.GlobalPosition.X - position.X, n3d.GlobalPosition.Z - position.Z).Length();
					if (dist <= searchRadius)
					{
						return n3d;
					}
				}
			}
		}
		return null;
	}

	private bool ApplyRampInternal(Vector3 start, Vector3 end)
	{
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		bool modified = false;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;
				float ab_len_sqr = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
				if (ab_len_sqr > 0.0001f)
				{
					float t = ((vx - start.X) * (end.X - start.X) + (vz - start.Z) * (end.Z - start.Z)) / ab_len_sqr;
					t = Mathf.Clamp(t, 0.0f, 1.0f);
					float proj_x = start.X + t * (end.X - start.X);
					float proj_z = start.Z + t * (end.Z - start.Z);
					float dist = Mathf.Sqrt((vx - proj_x) * (vx - proj_x) + (vz - proj_z) * (vz - proj_z));
					if (dist <= EditorBrushRadius)
					{
						float targetHeight = Mathf.Lerp(start.Y, end.Y, t);
						float falloff = 1.0f - (dist / EditorBrushRadius);
						falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);
						GroundTerrain.Heights[x, z] = Mathf.Lerp(GroundTerrain.Heights[x, z], targetHeight, falloff);
						modified = true;
					}
				}
			}
		}
		if (modified)
		{
			float threshold = EditorBlockMode ? (EditorBlockLevelHeight * 0.5f) : (spacing * 0.5f);
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float vx = (x - (width - 1) / 2.0f) * spacing;
					float vz = (z - (depth - 1) / 2.0f) * spacing;
					float ab_len_sqr = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
					if (ab_len_sqr > 0.0001f)
					{
						float t = ((vx - start.X) * (end.X - start.X) + (vz - start.Z) * (end.Z - start.Z)) / ab_len_sqr;
						t = Mathf.Clamp(t, 0.0f, 1.0f);
						float proj_x = start.X + t * (end.X - start.X);
						float proj_z = start.Z + t * (end.Z - start.Z);
						float dist = Mathf.Sqrt((vx - proj_x) * (vx - proj_x) + (vz - proj_z) * (vz - proj_z));
						if (dist <= EditorBrushRadius)
						{
							float h = GroundTerrain.Heights[x, z];
							float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
							float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
							float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
							float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
							float maxDiff = Mathf.Max(
								Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
								Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
							);
							if (maxDiff >= threshold)
							{
								GroundTerrain.Colors[x, z] = EditorCliffPaintColor;
							}
							else
							{
								GroundTerrain.Colors[x, z] = EditorPaintColor;
							}
						}
					}
				}
			}
		}
		return modified;
	}

	private float GetMinHeightInBrushBounds(Vector3 worldPos)
	{
		if (GroundTerrain == null) return 0.0f;
		float spacing = GroundTerrain.Spacing;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;

				bool inBounds = false;
				if (EditorBrushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= EditorBrushRadius && dz <= EditorBrushRadius;
				}
				else
				{
					float dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= EditorBrushRadius;
				}

				if (inBounds)
				{
					float h = GroundTerrain.Heights[x, z];
					if (h < minHeight)
					{
						minHeight = h;
						foundAny = true;
					}
				}
			}
		}

		return foundAny ? minHeight : GetTerrainHeightAt(worldPos);
	}

	private void ApplyGeneralClumpSpawn(Vector3 centerPos)
	{
		if (string.IsNullOrEmpty(ActivePlaceId)) return;
		int spawnCount = Mathf.Max(1, (int)Math.Round(EditorClumpDensity));
		for (int i = 0; i < spawnCount; i++)
		{
			float dx = 0.0f;
			float dz = 0.0f;
			if (EditorBrushIsSquare)
			{
				dx = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushRadius;
				dz = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushRadius;
			}
			else
			{
				float r = Mathf.Sqrt((float)GD.Randf()) * EditorBrushRadius;
				float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
				dx = r * Mathf.Cos(theta);
				dz = r * Mathf.Sin(theta);
			}
			Vector3 spawnPos = new Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
			if (GroundTerrain != null)
			{
				float spacing = GroundTerrain.Spacing;
				int width = GroundTerrain.Width;
				int depth = GroundTerrain.Depth;
				float halfW = (width - 1) / 2.0f * spacing;
				float halfD = (depth - 1) / 2.0f * spacing;
				if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
			}
			spawnPos.Y = GetTerrainHeightAt(spawnPos);

			float scaleVal = EditorPlacementScale + (float)(GD.Randf() * 2.0 - 1.0) * EditorClumpScaleVar;
			scaleVal = Mathf.Clamp(scaleVal, 0.2f, 3.0f);

			float rotY = (EditorRandomRotation && !_isPastingObject) ? (float)(GD.Randf() * 360.0) : EditorPlacementRotation;
			if (EditorRandomScale && !_isPastingObject)
			{
				scaleVal = 0.2f + (float)(GD.Randf() * 2.8);
			}

			Node spawnedNode = null;
			string spawnType = "";
			bool isEnemy = false;

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				spawnType = "unit";
				isEnemy = PlaceUnitIsEnemy;
				spawnedNode = SpawnUnitExternal(ActivePlaceId, spawnPos, isEnemy, rotY, scaleVal);
			}
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				spawnType = "prop";
				spawnedNode = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, rotY, scaleVal);
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				spawnType = "decal";
				spawnedNode = SpawnDecalExternalWithParams(ActivePlaceId, spawnPos, rotY, scaleVal);
			}

			if (spawnedNode != null)
			{
				_clumpSpawnActionsInSession.Add(new ObjectSpawnAction(spawnType, ActivePlaceId, spawnPos, rotY, scaleVal, isEnemy, spawnedNode));
				if (EditorMirrorMode != MirrorMode.None)
				{
					foreach (var t in GetMirroredTransforms(spawnPos, rotY))
					{
						Vector3 mPos = t.Position;
						mPos.Y = GetTerrainHeightAt(mPos);
						Node mNode = null;
						if (ActiveEditorTool == EditorTool.PlaceUnit)
						{
							mNode = SpawnUnitExternal(ActivePlaceId, mPos, isEnemy, t.Rotation, scaleVal);
						}
						else if (ActiveEditorTool == EditorTool.PlaceProp)
						{
							mNode = SpawnPropExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
						}
						else if (ActiveEditorTool == EditorTool.PlaceDecal)
						{
							mNode = SpawnDecalExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
						}
						if (mNode != null)
						{
							_clumpSpawnActionsInSession.Add(new ObjectSpawnAction(spawnType, ActivePlaceId, mPos, t.Rotation, scaleVal, isEnemy, mNode));
						}
					}
				}
			}
		}
	}

	private bool IsPositionBlocked(Vector3 pos, float checkRadius, Node3D ignoreNode = null)
	{
		foreach (var unit in AllUnits)
		{
			if (unit == ignoreNode || !GodotObject.IsInstanceValid(unit)) continue;
			float distXZ = new Vector2(unit.GlobalPosition.X - pos.X, unit.GlobalPosition.Z - pos.Z).Length();
			float r1 = checkRadius;
			float r2 = unit.Scale.X * 1.2f;
			if (unit.UnitId == "castle") r2 = unit.Scale.X * 5.0f;
			else if (unit.UnitId == "tower") r2 = unit.Scale.X * 2.5f;
			if (distXZ < (r1 + r2) * 0.85f)
			{
				return true;
			}
		}

		foreach (var node in GetChildren())
		{
			if (node == ignoreNode || !GodotObject.IsInstanceValid(node)) continue;
			if (node is Prop3D prop)
			{
				float distXZ = new Vector2(prop.GlobalPosition.X - pos.X, prop.GlobalPosition.Z - pos.Z).Length();
				float r1 = checkRadius;
				float r2 = prop.Scale.X * 1.5f;
				if (prop.PropId == "goldmine") r2 = prop.Scale.X * 4.0f;
				if (distXZ < (r1 + r2) * 0.85f)
				{
					return true;
				}
			}
		}

		return false;
	}

	private float GetPlacementRadius(string placeId, float scale)
	{
		if (string.IsNullOrEmpty(placeId)) return 1.2f * scale;
		string lowerId = placeId.ToLower();
		float baseRadius = 1.2f;
		if (lowerId.Contains("castle")) baseRadius = 5.0f;
		else if (lowerId.Contains("tower")) baseRadius = 2.5f;
		else if (lowerId.Contains("goldmine")) baseRadius = 4.0f;
		else if (lowerId.Contains("logo") || lowerId.Contains("flag") || lowerId.Contains("rune")) baseRadius = 1.0f;
		return baseRadius * scale;
	}

	private Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f)
	{
		if (!IsPositionBlocked(startPos, checkRadius))
		{
			return startPos;
		}

		float stepDist = 1.0f;
		int numSteps = Mathf.CeilToInt(maxSearchDist / stepDist);

		for (int i = 1; i <= numSteps; i++)
		{
			float dist = i * stepDist;
			int numAngles = 8 + i * 4;
			for (int a = 0; a < numAngles; a++)
			{
				float angle = a * (Mathf.Pi * 2.0f) / numAngles;
				Vector3 testPos = new Vector3(
					startPos.X + dist * Mathf.Cos(angle),
					startPos.Y,
					startPos.Z + dist * Mathf.Sin(angle)
				);

				if (GroundTerrain != null)
				{
					float spacing = GroundTerrain.Spacing;
					int width = GroundTerrain.Width;
					int depth = GroundTerrain.Depth;
					float halfW = (width - 1) / 2.0f * spacing;
					float halfD = (depth - 1) / 2.0f * spacing;
					if (Mathf.Abs(testPos.X) > halfW || Mathf.Abs(testPos.Z) > halfD) continue;
				}

				testPos.Y = GetTerrainHeightAt(testPos);

				if (!IsPositionBlocked(testPos, checkRadius))
				{
					return testPos;
				}
			}
		}

		return null;
	}

	private void ApplyPropClumpSpawn(Vector3 centerPos)
	{
		if (string.IsNullOrEmpty(ActivePlaceId)) return;
		int spawnCount = Mathf.Max(1, (int)Math.Round(EditorClumpDensity));
		for (int i = 0; i < spawnCount; i++)
		{
			float dx = 0.0f;
			float dz = 0.0f;
			if (EditorBrushIsSquare)
			{
				dx = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushRadius;
				dz = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushRadius;
			}
			else
			{
				float r = Mathf.Sqrt((float)GD.Randf()) * EditorBrushRadius;
				float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
				dx = r * Mathf.Cos(theta);
				dz = r * Mathf.Sin(theta);
			}
			Vector3 spawnPos = new Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
			if (GroundTerrain != null)
			{
				float spacing = GroundTerrain.Spacing;
				int width = GroundTerrain.Width;
				int depth = GroundTerrain.Depth;
				float halfW = (width - 1) / 2.0f * spacing;
				float halfD = (depth - 1) / 2.0f * spacing;
				if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
			}
			float scaleVal = EditorPlacementScale + (float)(GD.Randf() * 2.0 - 1.0) * EditorClumpScaleVar;
			scaleVal = Mathf.Clamp(scaleVal, 0.1f, 10.0f);
			float rotY = (float)(GD.Randf() * 360.0);
			var prop = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, rotY, scaleVal);
			if (prop != null)
			{
				_clumpSpawnActionsInSession.Add(new ObjectSpawnAction("prop", ActivePlaceId, spawnPos, rotY, scaleVal, false, prop));
				if (EditorMirrorMode != MirrorMode.None)
				{
					foreach (var t in GetMirroredTransforms(spawnPos, rotY))
					{
						Vector3 mPos = t.Position;
						mPos.Y = GetTerrainHeightAt(mPos);
						var mProp = SpawnPropExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
						if (mProp != null)
						{
							_clumpSpawnActionsInSession.Add(new ObjectSpawnAction("prop", ActivePlaceId, mPos, t.Rotation, scaleVal, false, mProp));
						}
					}
				}
			}
		}
	}

	private void InitializeCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh != null) return;

		_cameraBoundsOverlayMesh = new MeshInstance3D();
		_cameraBoundsOverlayMesh.Name = "CameraBoundsOverlay";

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.NoDepthTest = false;
		mat.VertexColorUseAsAlbedo = true;
		_cameraBoundsOverlayMesh.MaterialOverride = mat;

		AddChild(_cameraBoundsOverlayMesh);
		_cameraBoundsOverlayMesh.Visible = false;
	}

	public void UpdateCameraBoundsOverlayVisibility()
	{
		if (_cameraBoundsOverlayMesh == null) return;
		_cameraBoundsOverlayMesh.Visible = IsMapEditorMode && EditorCameraBoundsVisible;
		if (_cameraBoundsOverlayMesh.Visible)
		{
			RebuildCameraBoundsOverlay();
		}
	}

	public void RebuildCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh == null || GroundTerrain == null) return;
		if (!EditorCameraBoundsVisible) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		float halfW = (width - 1) / 2.0f;
		float halfD = (depth - 1) / 2.0f;

		float minWorldX = -halfW * spacing;
		float maxWorldX = halfW * spacing;
		float minWorldZ = -halfD * spacing;
		float maxWorldZ = halfD * spacing;

		float left = Mathf.Clamp(EditorCameraBoundsLeft, minWorldX, maxWorldX);
		float right = Mathf.Clamp(EditorCameraBoundsRight, minWorldX, maxWorldX);
		float top = Mathf.Clamp(EditorCameraBoundsTop, minWorldZ, maxWorldZ);
		float bottom = Mathf.Clamp(EditorCameraBoundsBottom, minWorldZ, maxWorldZ);

		var linePoints = new List<Vector3>();

		float GetTerrainHeightAtCoord(float worldX, float worldZ)
		{
			float gridX = worldX / spacing + halfW;
			float gridZ = worldZ / spacing + halfD;
			int x0 = Mathf.Clamp((int)Mathf.Floor(gridX), 0, width - 1);
			int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
			int z0 = Mathf.Clamp((int)Mathf.Floor(gridZ), 0, depth - 1);
			int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);
			
			float tx = gridX - x0;
			float tz = gridZ - z0;
			
			float h00 = GroundTerrain.Heights[x0, z0];
			float h10 = GroundTerrain.Heights[x1, z0];
			float h01 = GroundTerrain.Heights[x0, z1];
			float h11 = GroundTerrain.Heights[x1, z1];
			
			float h0 = Mathf.Lerp(h00, h10, tx);
			float h1 = Mathf.Lerp(h01, h11, tx);
			return Mathf.Lerp(h0, h1, tz);
		}

		void AddSegmentedLine(float x1, float z1, float x2, float z2)
		{
			float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
			int segments = Mathf.Max(1, (int)Mathf.Ceil(dist / spacing));
			for (int i = 0; i < segments; i++)
			{
				float t1 = (float)i / segments;
				float t2 = (float)(i + 1) / segments;
				
				float lx1 = Mathf.Lerp(x1, x2, t1);
				float lz1 = Mathf.Lerp(z1, z2, t1);
				float lx2 = Mathf.Lerp(x1, x2, t2);
				float lz2 = Mathf.Lerp(z1, z2, t2);
				
				float y1 = GetTerrainHeightAtCoord(lx1, lz1) + 0.2f;
				float y2 = GetTerrainHeightAtCoord(lx2, lz2) + 0.2f;
				
				linePoints.Add(new Vector3(lx1, y1, lz1));
				linePoints.Add(new Vector3(lx2, y2, lz2));
			}
		}

		AddSegmentedLine(left, top, right, top);
		AddSegmentedLine(right, top, right, bottom);
		AddSegmentedLine(right, bottom, left, bottom);
		AddSegmentedLine(left, bottom, left, top);

		int totalVertices = linePoints.Count * 3;
		var vertices = new Vector3[totalVertices];
		var colors = new Color[totalVertices];
		int idx = 0;

		Color boundsColor = new Color(0.9f, 0.1f, 0.8f, 0.95f);

		for (int i = 0; i < linePoints.Count; i += 2)
		{
			Vector3 p1 = linePoints[i];
			Vector3 p2 = linePoints[i + 1];

			vertices[idx] = p1;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2;
			colors[idx] = boundsColor;
			idx++;

			Vector3 dir = (p2 - p1).Normalized();
			Vector3 ortho = new Vector3(-dir.Z, 0, dir.X) * 0.08f;

			vertices[idx] = p1 + ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 + ortho;
			colors[idx] = boundsColor;
			idx++;

			vertices[idx] = p1 - ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 - ortho;
			colors[idx] = boundsColor;
			idx++;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		_cameraBoundsOverlayMesh.Mesh = arrayMesh;
	}

	public void UpdatePathingOverlay()
	{
		if (_pathingOverlayMesh == null)
		{
			_pathingOverlayMesh = new MeshInstance3D();
			_pathingOverlayMesh.Name = "PathingOverlay";

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.VertexColorUseAsAlbedo = true;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			mat.NoDepthTest = false;
			mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
			_pathingOverlayMesh.MaterialOverride = mat;
			_pathingOverlayMesh.Position = new Vector3(0f, 0.05f, 0f);
			AddChild(_pathingOverlayMesh);
		}

		bool shouldBeVisible = IsMapEditorMode && PathingOverlayVisible && ActiveEditorTool == EditorTool.PaintPathing;
		_pathingOverlayMesh.Visible = shouldBeVisible;

		if (shouldBeVisible && GroundTerrain != null)
		{
			RebuildPathingOverlay();
		}
	}

	private void RebuildPathingOverlay()
	{
		if (_pathingOverlayMesh == null || GroundTerrain == null) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		static Color GetLayerColor(int flag) => flag switch
		{
			EditableTerrain.PATHING_SHALLOW_WATER => new Color(0.2f, 0.6f, 1.0f, 0.55f),
			EditableTerrain.PATHING_DEEP_WATER    => new Color(0.0f, 0.15f, 0.7f, 0.55f),
			EditableTerrain.PATHING_FLYING        => new Color(0.85f, 0.85f, 0.0f, 0.55f),
			EditableTerrain.PATHING_GROUND        => new Color(0.2f, 0.85f, 0.2f, 0.55f),
			EditableTerrain.PATHING_UNPATHABLE    => new Color(0.9f, 0.1f, 0.1f, 0.55f),
			_ => new Color(0f, 0f, 0f, 0f)
		};

		var allFlags = new int[]
		{
			EditableTerrain.PATHING_SHALLOW_WATER,
			EditableTerrain.PATHING_DEEP_WATER,
			EditableTerrain.PATHING_FLYING,
			EditableTerrain.PATHING_GROUND,
			EditableTerrain.PATHING_UNPATHABLE
		};

		int cellWidth = width - 1;
		int cellDepth = depth - 1;
		int maxQuads = cellWidth * cellDepth;

		var verticesList = new List<Vector3>(maxQuads * 8);
		var colorsList = new List<Color>(maxQuads * 8);
		var indicesList = new List<int>(maxQuads * 12);

		for (int z = 0; z < cellDepth; z++)
		{
			for (int x = 0; x < cellWidth; x++)
			{
				int code = GroundTerrain.PathingCodes[x, z];
				if (code == 0)
				{
					continue;
				}

				var activeFlags = new List<int>();
				foreach (var flag in allFlags)
				{
					if ((code & flag) != 0)
					{
						activeFlags.Add(flag);
					}
				}

				if (activeFlags.Count == 0)
				{
					continue;
				}

				float lx0 = (x - (width - 1) / 2.0f) * spacing;
				float lz0 = (z - (depth - 1) / 2.0f) * spacing;
				float lx1 = lx0 + spacing;
				float lz1 = lz0 + spacing;

				float h00 = GroundTerrain.Heights[x,     z    ] + 0.06f;
				float h10 = GroundTerrain.Heights[x + 1, z    ] + 0.06f;
				float h01 = GroundTerrain.Heights[x,     z + 1] + 0.06f;
				float h11 = GroundTerrain.Heights[x + 1, z + 1] + 0.06f;

				if (activeFlags.Count == 1)
				{
					Color cellColor = GetLayerColor(activeFlags[0]);
					if (cellColor.A < 0.01f) continue;

					int baseV = verticesList.Count;
					verticesList.Add(new Vector3(lx0, h00, lz0));
					colorsList.Add(cellColor);
					verticesList.Add(new Vector3(lx1, h10, lz0));
					colorsList.Add(cellColor);
					verticesList.Add(new Vector3(lx1, h11, lz1));
					colorsList.Add(cellColor);
					verticesList.Add(new Vector3(lx0, h01, lz1));
					colorsList.Add(cellColor);

					indicesList.Add(baseV);
					indicesList.Add(baseV + 1);
					indicesList.Add(baseV + 2);
					indicesList.Add(baseV);
					indicesList.Add(baseV + 2);
					indicesList.Add(baseV + 3);
				}
				else
				{
					int S = 4;
					for (int sz = 0; sz < S; sz++)
					{
						for (int sx = 0; sx < S; sx++)
						{
							float tx0 = (float)sx / S;
							float tx1 = (float)(sx + 1) / S;
							float tz0 = (float)sz / S;
							float tz1 = (float)(sz + 1) / S;

							float h_sub00 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx0), Mathf.Lerp(h01, h11, tx0), tz0);
							float h_sub10 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx1), Mathf.Lerp(h01, h11, tx1), tz0);
							float h_sub11 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx1), Mathf.Lerp(h01, h11, tx1), tz1);
							float h_sub01 = Mathf.Lerp(Mathf.Lerp(h00, h10, tx0), Mathf.Lerp(h01, h11, tx0), tz1);

							float subX0 = lx0 + sx * (spacing / S);
							float subX1 = lx0 + (sx + 1) * (spacing / S);
							float subZ0 = lz0 + sz * (spacing / S);
							float subZ1 = lz0 + (sz + 1) * (spacing / S);

							int flagIndex = (sx + sz) % activeFlags.Count;
							Color subColor = GetLayerColor(activeFlags[flagIndex]);
							if (subColor.A < 0.01f) continue;

							int baseV = verticesList.Count;
							verticesList.Add(new Vector3(subX0, h_sub00, subZ0));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX1, h_sub10, subZ0));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX1, h_sub11, subZ1));
							colorsList.Add(subColor);
							verticesList.Add(new Vector3(subX0, h_sub01, subZ1));
							colorsList.Add(subColor);

							indicesList.Add(baseV);
							indicesList.Add(baseV + 1);
							indicesList.Add(baseV + 2);
							indicesList.Add(baseV);
							indicesList.Add(baseV + 2);
							indicesList.Add(baseV + 3);
						}
					}
				}
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verticesList.ToArray();
		arrays[(int)Mesh.ArrayType.Color]  = colorsList.ToArray();
		arrays[(int)Mesh.ArrayType.Index]  = indicesList.ToArray();

		var arrayMesh = new ArrayMesh();
		if (indicesList.Count > 0)
		{
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		}
		_pathingOverlayMesh.Mesh = arrayMesh;
	}

	private void CreateGridOverlay()
	{
		if (_gridOverlayMesh != null) return;

		_gridOverlayMesh = new MeshInstance3D();
		_gridOverlayMesh.Name = "GridOverlay";

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.NoDepthTest = false;
		mat.VertexColorUseAsAlbedo = true;
		_gridOverlayMesh.MaterialOverride = mat;

		AddChild(_gridOverlayMesh);
		_gridOverlayMesh.Visible = false;
	}

	public void RebuildGridOverlayMeshExternal()
	{
		RebuildGridOverlayMesh();
	}

	private void RebuildGridOverlayMesh()
	{
		if (_gridOverlayMesh == null || GroundTerrain == null) return;
		if (!EditorGridVisible) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;

		int totalVertices = 0;
		for (int z = 0; z < depth; z++)
		{
			bool isThick = (z % 10 == 0);
			totalVertices += (width - 1) * (isThick ? 6 : 2);
		}
		for (int x = 0; x < width; x++)
		{
			bool isThick = (x % 10 == 0);
			totalVertices += (depth - 1) * (isThick ? 6 : 2);
		}

		var vertices = new Vector3[totalVertices];
		var colors = new Color[totalVertices];
		int idx = 0;

		Color thickColor = new Color(1.0f, 0.9f, 0.0f, 0.85f);
		Color thinColor = new Color(1.0f, 0.9f, 0.0f, 0.25f);

		void AddLine(Vector3 p1, Vector3 p2, Color color, bool thick, bool isVertical)
		{
			vertices[idx] = p1;
			colors[idx] = color;
			idx++;
			vertices[idx] = p2;
			colors[idx] = color;
			idx++;

			if (thick)
			{
				float offset = 0.04f;
				Vector3 o = isVertical ? new Vector3(offset, 0, 0) : new Vector3(0, 0, offset);
				
				vertices[idx] = p1 + o;
				colors[idx] = color;
				idx++;
				vertices[idx] = p2 + o;
				colors[idx] = color;
				idx++;

				vertices[idx] = p1 - o;
				colors[idx] = color;
				idx++;
				vertices[idx] = p2 - o;
				colors[idx] = color;
				idx++;
			}
		}

		for (int z = 0; z < depth; z++)
		{
			bool isThick = (z % 10 == 0);
			Color col = isThick ? thickColor : thinColor;
			float lz = (z - (depth - 1) / 2.0f) * spacing;
			for (int x = 0; x < width - 1; x++)
			{
				float lx1 = (x - (width - 1) / 2.0f) * spacing;
				float lx2 = (x + 1 - (width - 1) / 2.0f) * spacing;

				float y1 = GroundTerrain.Heights[x, z] + 0.15f;
				float y2 = GroundTerrain.Heights[x + 1, z] + 0.15f;

				AddLine(new Vector3(lx1, y1, lz), new Vector3(lx2, y2, lz), col, isThick, false);
			}
		}

		for (int x = 0; x < width; x++)
		{
			bool isThick = (x % 10 == 0);
			Color col = isThick ? thickColor : thinColor;
			float lx = (x - (width - 1) / 2.0f) * spacing;
			for (int z = 0; z < depth - 1; z++)
			{
				float lz1 = (z - (depth - 1) / 2.0f) * spacing;
				float lz2 = (z + 1 - (depth - 1) / 2.0f) * spacing;

				float y1 = GroundTerrain.Heights[x, z] + 0.15f;
				float y2 = GroundTerrain.Heights[x, z + 1] + 0.15f;

				AddLine(new Vector3(lx, y1, lz1), new Vector3(lx, y2, lz2), col, isThick, true);
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		_gridOverlayMesh.Mesh = arrayMesh;
	}

	public void UpdateGridOverlayVisibility()
	{
		if (_gridOverlayMesh == null) return;
		_gridOverlayMesh.Visible = IsMapEditorMode && EditorGridVisible;
		if (_gridOverlayMesh.Visible)
		{
			RebuildGridOverlayMesh();
		}
	}

	public void FillMapWithActiveColor()
	{
		if (GroundTerrain == null) return;
		
		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		Color paintColor = EditorPaintColor;
		
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float targetAlpha = paintColor.A;
				GroundTerrain.Colors[x, z] = new Color(paintColor.R, paintColor.G, paintColor.B, targetAlpha);
			}
		}
		
		GroundTerrain.UpdateMeshAndPhysics(false);
		
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var action = new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter);
		EditorHistoryManager.RecordAction(action);
		
		MapEditorHUD.Instance?.ShowFeedbackExternal("Map filled with selected texture");
	}

	public void PerformFloodFill(Vector3 clickPos, Color fillColor)
	{
		if (GroundTerrain == null) return;
		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		var visited = new bool[width, depth];
		void DoSingleFill(Vector3 pos)
		{
			float fx = pos.X / spacing + (width - 1) / 2.0f;
			float fz = pos.Z / spacing + (depth - 1) / 2.0f;
			int startX = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
			int startZ = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);
			Color startColor = colorsBefore[startX, startZ];
			if (startColor == fillColor) return;
			var queue = new Queue<(int x, int z)>();
			if (!visited[startX, startZ])
			{
				queue.Enqueue((startX, startZ));
				visited[startX, startZ] = true;
			}
			while (queue.Count > 0)
			{
				var (currX, currZ) = queue.Dequeue();
				float targetAlpha = fillColor.A;
				GroundTerrain.Colors[currX, currZ] = new Color(fillColor.R, fillColor.G, fillColor.B, targetAlpha);
				int[] dx = { 0, 0, -1, 1 };
				int[] dz = { -1, 1, 0, 0 };
				for (int i = 0; i < 4; i++)
				{
					int nextX = currX + dx[i];
					int nextZ = currZ + dz[i];
					if (nextX >= 0 && nextX < width && nextZ >= 0 && nextZ < depth)
					{
						if (!visited[nextX, nextZ])
						{
							if (colorsBefore[nextX, nextZ] != startColor)
							{
								continue;
							}
							float hCurrent = GroundTerrain.Heights[currX, currZ];
							float hNext = GroundTerrain.Heights[nextX, nextZ];
							if (Mathf.Abs(hNext - hCurrent) >= 1.0f)
							{
								continue;
							}
							visited[nextX, nextZ] = true;
							queue.Enqueue((nextX, nextZ));
						}
					}
				}
			}
		}
		DoSingleFill(clickPos);
		if (EditorMirrorMode != MirrorMode.None)
		{
			foreach (var t in GetMirroredTransforms(clickPos, 0.0f))
			{
				DoSingleFill(t.Position);
			}
		}
		GroundTerrain.UpdateMeshAndPhysics(false, false);
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var action = new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled terrain area");
	}

	private void CreateSelectionHighlight()
	{
		if (_selectionHighlightMesh != null) return;
		_selectionHighlightMesh = new MeshInstance3D();
		_selectionHighlightMesh.Name = "SelectionHighlight";
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_selectionHighlightMesh.MaterialOverride = mat;
		AddChild(_selectionHighlightMesh);
		_selectionHighlightMesh.Visible = false;
	}

	private void RebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
	{
		if (_selectionHighlightMesh == null || GroundTerrain == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth <= 0 || selDepth <= 0)
		{
			_selectionHighlightMesh.Visible = false;
			return;
		}
		int vertexCount = selWidth * selDepth;
		var vertices = new Vector3[vertexCount];
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int mapX = minX + sx;
				int mapZ = minZ + sz;
				int idx = sz * selWidth + sx;
				float lx = (mapX - (width - 1) / 2.0f) * spacing;
				float lz = (mapZ - (depth - 1) / 2.0f) * spacing;
				vertices[idx] = new Vector3(lx, GroundTerrain.Heights[mapX, mapZ] + 0.05f, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		int iIdx = 0;
		for (int sz = 0; sz < cellDepth; sz++)
		{
			for (int sx = 0; sx < cellWidth; sx++)
			{
				int v00 = sz * selWidth + sx;
				int v10 = sz * selWidth + (sx + 1);
				int v01 = (sz + 1) * selWidth + sx;
				int v11 = (sz + 1) * selWidth + (sx + 1);
				indices[iIdx++] = v00;
				indices[iIdx++] = v10;
				indices[iIdx++] = v01;
				indices[iIdx++] = v10;
				indices[iIdx++] = v11;
				indices[iIdx++] = v01;
			}
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_selectionHighlightMesh.Mesh = arrayMesh;
		_selectionHighlightMesh.Visible = true;
	}

	private void PerformCopyArea()
	{
		if (GroundTerrain == null || _selectionStart == null || _selectionEnd == null) return;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var heights = new float[selWidth, selDepth];
		var colors = new Color[selWidth, selDepth];
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				heights[sx, sz] = GroundTerrain.Heights[minX + sx, minZ + sz];
				colors[sx, sz] = GroundTerrain.Colors[minX + sx, minZ + sz];
			}
		}
		float minWorldX = (minX - (width - 1) / 2.0f) * spacing - spacing * 0.5f;
		float maxWorldX = (maxX - (width - 1) / 2.0f) * spacing + spacing * 0.5f;
		float minWorldZ = (minZ - (depth - 1) / 2.0f) * spacing - spacing * 0.5f;
		float maxWorldZ = (maxZ - (depth - 1) / 2.0f) * spacing + spacing * 0.5f;
		Vector3 origin = new Vector3((minX - (width - 1) / 2.0f) * spacing, 0.0f, (minZ - (depth - 1) / 2.0f) * spacing);
		var entities = new List<CopiedEntityInfo>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				Vector3 pos = n3d.Position;
				if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
				{
					if (n3d is Unit3D unit)
					{
						entities.Add(new CopiedEntityInfo {
							Type = "unit",
							Id = unit.UnitId,
							RelativePos = pos - origin,
							Rotation = unit.RotationDegrees.Y,
							Scale = unit.Scale.X,
							IsEnemy = unit.IsEnemy
						});
					}
					else if (n3d is Prop3D prop)
					{
						entities.Add(new CopiedEntityInfo {
							Type = "prop",
							Id = prop.PropId,
							RelativePos = pos - origin,
							Rotation = prop.RotationDegrees.Y,
							Scale = prop.Scale.X,
							IsEnemy = false
						});
					}
					else if (n3d is Decal decal)
					{
						string decalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo";
						entities.Add(new CopiedEntityInfo {
							Type = "decal",
							Id = decalId,
							RelativePos = pos - origin,
							Rotation = decal.RotationDegrees.Y,
							Scale = decal.Scale.X,
							IsEnemy = false
						});
					}
				}
			}
		}
		_copiedArea = new CopiedAreaTemplate {
			Width = selWidth,
			Depth = selDepth,
			Heights = heights,
			Colors = colors,
			Entities = entities
		};
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Area: {selWidth}x{selDepth} tiles, {entities.Count} entities");
	}

	private void PerformPasteArea(int startX, int startZ)
	{
		if (GroundTerrain == null || _copiedArea == null) return;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		bool modified = false;
		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;

		void PasteCell(int sx, int sz)
		{
			int targetX = startX + sx;
			int targetZ = startZ + sz;

			if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
			{
				if (PasteOptionHeights) GroundTerrain.Heights[targetX, targetZ] = _copiedArea.Heights[sx, sz];
				if (PasteOptionTextures) GroundTerrain.Colors[targetX, targetZ] = _copiedArea.Colors[sx, sz];
				modified = true;
			}

			if (EditorMirrorMode == MirrorMode.Horizontal || EditorMirrorMode == MirrorMode.Both)
			{
				int mx = width - 1 - targetX;
				int mz = targetZ;
				if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
				{
					if (PasteOptionHeights) GroundTerrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
					if (PasteOptionTextures) GroundTerrain.Colors[mx, mz] = _copiedArea.Colors[sx, sz];
					modified = true;
				}
			}

			if (EditorMirrorMode == MirrorMode.Vertical || EditorMirrorMode == MirrorMode.Both)
			{
				int mx = targetX;
				int mz = depth - 1 - targetZ;
				if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
				{
					if (PasteOptionHeights) GroundTerrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
					if (PasteOptionTextures) GroundTerrain.Colors[mx, mz] = _copiedArea.Colors[sx, sz];
					modified = true;
				}
			}

			if (EditorMirrorMode == MirrorMode.Both)
			{
				int mx = width - 1 - targetX;
				int mz = depth - 1 - targetZ;
				if (mx >= 0 && mx < width && mz >= 0 && mz < depth)
				{
					if (PasteOptionHeights) GroundTerrain.Heights[mx, mz] = _copiedArea.Heights[sx, sz];
					if (PasteOptionTextures) GroundTerrain.Colors[mx, mz] = _copiedArea.Colors[sx, sz];
					modified = true;
				}
			}
		}

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				PasteCell(sx, sz);
			}
		}

		if (modified)
		{
			GroundTerrain.UpdateMeshAndPhysics(PasteOptionHeights, false);
			if (PasteOptionHeights)
			{
				AlignAllEntitiesToTerrain();
			}
		}

		var spawnActions = new List<IEditorAction>();
		void SpawnAndRecord(CopiedEntityInfo ent, Vector3 pos, float rotation)
		{
			Node pastedNode = null;
			if (ent.Type == "unit")
			{
				pastedNode = SpawnUnitExternal(ent.Id, pos, ent.IsEnemy, rotation, ent.Scale);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction("unit", ent.Id, pos, rotation, ent.Scale, ent.IsEnemy, pastedNode));
				}
			}
			else if (ent.Type == "prop")
			{
				pastedNode = SpawnPropExternalWithParams(ent.Id, pos, rotation, ent.Scale);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction("prop", ent.Id, pos, rotation, ent.Scale, false, pastedNode));
				}
			}
			else if (ent.Type == "decal")
			{
				pastedNode = SpawnDecalExternalWithParams(ent.Id, pos, rotation, ent.Scale);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction("decal", ent.Id, pos, rotation, ent.Scale, false, pastedNode));
				}
			}
		}

		if (PasteOptionEntities)
		{
			Vector3 origin = new Vector3((startX - (width - 1) / 2.0f) * spacing, 0.0f, (startZ - (depth - 1) / 2.0f) * spacing);
			foreach (var ent in _copiedArea.Entities)
			{
				Vector3 destPos = origin + ent.RelativePos;
				destPos.Y = GetTerrainHeightAt(destPos);
				
				SpawnAndRecord(ent, destPos, ent.Rotation);

				if (EditorMirrorMode == MirrorMode.Horizontal || EditorMirrorMode == MirrorMode.Both)
				{
					Vector3 mPos = new Vector3(-destPos.X, destPos.Y, destPos.Z);
					mPos.Y = GetTerrainHeightAt(mPos);
					SpawnAndRecord(ent, mPos, 180.0f - ent.Rotation);
				}
				if (EditorMirrorMode == MirrorMode.Vertical || EditorMirrorMode == MirrorMode.Both)
				{
					Vector3 mPos = new Vector3(destPos.X, destPos.Y, -destPos.Z);
					mPos.Y = GetTerrainHeightAt(mPos);
					SpawnAndRecord(ent, mPos, -ent.Rotation);
				}
				if (EditorMirrorMode == MirrorMode.Both)
				{
					Vector3 mPos = new Vector3(-destPos.X, destPos.Y, -destPos.Z);
					mPos.Y = GetTerrainHeightAt(mPos);
					SpawnAndRecord(ent, mPos, ent.Rotation + 180.0f);
				}
			}
		}
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var actions = new List<IEditorAction>();
		if (modified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter));
		}
		if (spawnActions.Count > 0)
		{
			actions.AddRange(spawnActions);
		}
		if (actions.Count > 0)
		{
			var composite = new CompositeAction(actions);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted Area");
		}
	}

	public void PerformEraseAreaExternal()
	{
		PerformEraseArea();
	}

	public void PerformCutAreaExternal()
	{
		if (GroundTerrain == null || _selectionStart == null || _selectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
			return;
		}

		PerformCopyArea();
		PerformEraseArea();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Cut");
	}

	private void PerformEraseArea()
	{
		if (GroundTerrain == null || _selectionStart == null || _selectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Erase (select an area first)");
			return;
		}

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var colorsBefore = (Color[,])GroundTerrain.Colors.Clone();
		bool terrainModified = false;

		if (PasteOptionHeights || PasteOptionTextures)
		{
			for (int sz = 0; sz < selDepth; sz++)
			{
				for (int sx = 0; sx < selWidth; sx++)
				{
					int targetX = minX + sx;
					int targetZ = minZ + sz;
					if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
					{
						if (PasteOptionHeights) GroundTerrain.Heights[targetX, targetZ] = 0.0f;
						if (PasteOptionTextures) GroundTerrain.Colors[targetX, targetZ] = new Color(0.2f, 0.45f, 0.15f);
						terrainModified = true;
					}
				}
			}
		}

		if (terrainModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(PasteOptionHeights, false);
			if (PasteOptionHeights)
			{
				AlignAllEntitiesToTerrain();
			}
		}

		var deleteActions = new List<IEditorAction>();
		if (PasteOptionEntities)
		{
			float minWorldX = (minX - (width - 1) / 2.0f) * spacing - spacing * 0.5f;
			float maxWorldX = (maxX - (width - 1) / 2.0f) * spacing + spacing * 0.5f;
			float minWorldZ = (minZ - (depth - 1) / 2.0f) * spacing - spacing * 0.5f;
			float maxWorldZ = (maxZ - (depth - 1) / 2.0f) * spacing + spacing * 0.5f;

			var toDelete = new List<Node3D>();
			foreach (var child in GetChildren())
			{
				if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d) && n3d != _editorPreviewNode)
				{
					Vector3 pos = n3d.Position;
					if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
					{
						if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
						{
							toDelete.Add(n3d);
						}
					}
				}
			}

			foreach (var node in toDelete)
			{
				var act = DeleteObjectAtWithUndo(node, node.Position);
				if (act != null) deleteActions.Add(act);
			}
		}

		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var colorsAfter = (Color[,])GroundTerrain.Colors.Clone();
		var actions = new List<IEditorAction>();
		if (terrainModified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, colorsBefore, colorsAfter));
		}
		if (deleteActions.Count > 0)
		{
			actions.AddRange(deleteActions);
		}

		if (actions.Count > 0)
		{
			var composite = new CompositeAction(actions);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Area Erased");
		}
	}

	public void ClearMapEntirely()
	{
		if (GroundTerrain == null) return;
		
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				DeleteNodeExternal(unit);
			}
		}
		SelectedUnits.Clear();
		AllUnits.Clear();
		AllProps.Clear();
		
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				prop.QueueFree();
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				decal.QueueFree();
			}
		}
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				GroundTerrain.Heights[x, z] = 0.0f;
				GroundTerrain.Colors[x, z] = new Color(0.2f, 0.6f, 0.2f);
			}
		}
		
		GroundTerrain.UpdateMeshAndPhysics(true, true);
		EditorHistoryManager.Clear();
		RebuildGridOverlayMeshExternal();
		
		EditorCameraBoundsLeft = -95.0f;
		EditorCameraBoundsRight = 95.0f;
		EditorCameraBoundsTop = -95.0f;
		EditorCameraBoundsBottom = 125.0f;
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		MapEditorHUD.Instance?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
	}

	private bool IsMouseOverUI()
	{
		if (GodotObject.IsInstanceValid(MapEditorHUD.Instance))
		{
			return MapEditorHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
		}
		var mousePos = GetViewport().GetMousePosition();
		var viewportSize = GetViewport().GetVisibleRect().Size;
		
		if (mousePos.Y < 75) return true;
		if (mousePos.Y > viewportSize.Y - 245) return true;
		if (mousePos.X < 225 || mousePos.X > viewportSize.X - 225) return true;
		
		return false;
	}

	private void ApplyContinuousTerrainEditing(Vector3 worldPos, float delta)
	{
		if (GroundTerrain == null) return;
		
		bool isHeights = ActiveEditorTool == EditorTool.Raise ||
						 ActiveEditorTool == EditorTool.Lower ||
						 ActiveEditorTool == EditorTool.Flatten ||
						 ActiveEditorTool == EditorTool.Smooth ||
						 ActiveEditorTool == EditorTool.Cliff ||
						 ActiveEditorTool == EditorTool.Noise;
						 
		bool isPaint = ActiveEditorTool == EditorTool.PaintGrass ||
					   ActiveEditorTool == EditorTool.PaintDirt ||
					   ActiveEditorTool == EditorTool.PaintRock ||
					   ActiveEditorTool == EditorTool.PaintSand;

		bool isPathing = ActiveEditorTool == EditorTool.PaintPathing;
					   
		if (!isHeights && !isPaint && !isPathing) return;
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		
		Color paintColor = EditorPaintColor;
		
		bool modified = false;

		int pathingMask = 0;
		bool pathingAdd = true;
		if (isPathing && MapEditorHUD.Instance != null)
		{
			pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
			pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
		}
		
		if (EditorBlockMode)
		{
			int cx = Mathf.Clamp((int)Math.Round(worldPos.X / spacing + (width - 1) / 2.0f), 0, width - 1);
			int cz = Mathf.Clamp((int)Math.Round(worldPos.Z / spacing + (depth - 1) / 2.0f), 0, depth - 1);
			int brushGridRadius = Mathf.Max(0, (int)Math.Round(EditorBrushRadius / spacing));
			
			if (isHeights)
			{
				float targetHeight = _hasBlockTargetHeight ? _activeBlockTargetHeight : 0.0f;
				for (int z = cz - brushGridRadius; z <= cz + brushGridRadius; z++)
				{
					for (int x = cx - brushGridRadius; x <= cx + brushGridRadius; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!EditorBrushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}
							
							if (inBounds)
							{
								if (ActiveEditorTool == EditorTool.Raise || 
									ActiveEditorTool == EditorTool.Lower || 
									ActiveEditorTool == EditorTool.Flatten || 
									ActiveEditorTool == EditorTool.Cliff)
								{
									GroundTerrain.Heights[x, z] = Mathf.Clamp(targetHeight, -10.0f, 50.0f);
									modified = true;
								}
								else if (ActiveEditorTool == EditorTool.Smooth)
								{
									float avg = 0f;
									int count = 0;
									for (int nz = -1; nz <= 1; nz++)
									{
										for (int nx = -1; nx <= 1; nx++)
										{
											int nxVal = x + nx;
											int nzVal = z + nz;
											if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
											{
												avg += GroundTerrain.Heights[nxVal, nzVal];
												count++;
											}
										}
									}
									avg /= count;
									float snappedAvg = Mathf.Round(avg / EditorBlockLevelHeight) * EditorBlockLevelHeight;
									GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], snappedAvg, EditorBrushStrength * delta * 2.0f), -10.0f, 50.0f);
									modified = true;
								}
								else if (ActiveEditorTool == EditorTool.Noise)
								{
									if (GD.Randf() < 0.15f * EditorBrushStrength * delta)
									{
										float direction = GD.Randf() > 0.5f ? 1.0f : -1.0f;
										GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] + direction * EditorBlockLevelHeight, -10.0f, 50.0f);
										modified = true;
									}
								}
							}
						}
					}
				}
				
				if (modified && ActiveEditorTool != EditorTool.Smooth && ActiveEditorTool != EditorTool.Flatten && ActiveEditorTool != EditorTool.Noise)
				{
					for (int z = cz - brushGridRadius - 1; z <= cz + brushGridRadius + 1; z++)
					{
						for (int x = cx - brushGridRadius - 1; x <= cx + brushGridRadius + 1; x++)
						{
							if (x >= 0 && x < width && z >= 0 && z < depth)
							{
								float h = GroundTerrain.Heights[x, z];
								float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
								float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
								float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
								float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
								
								float maxDiff = Mathf.Max(
									Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
									Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
								);
								
								if (maxDiff >= EditorBlockLevelHeight * 0.5f)
								{
									GroundTerrain.Colors[x, z] = EditorCliffPaintColor;
								}
								else
								{
									bool insideBrush = true;
									if (!EditorBrushIsSquare)
									{
										float dx = x - cx;
										float dz = z - cz;
										insideBrush = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
									}
									else
									{
										insideBrush = (x >= cx - brushGridRadius && x <= cx + brushGridRadius && z >= cz - brushGridRadius && z <= cz + brushGridRadius);
									}
									
									if (insideBrush)
									{
										GroundTerrain.Colors[x, z] = EditorPaintColor;
									}
								}
							}
						}
					}
				}
			}
			else if (isPaint)
			{
				for (int z = cz - brushGridRadius; z <= cz + brushGridRadius; z++)
				{
					for (int x = cx - brushGridRadius; x <= cx + brushGridRadius; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!EditorBrushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}
							
							if (inBounds)
							{
								float h = GroundTerrain.Heights[x, z];
								float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
								float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
								float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
								float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
								
								float maxDiff = Mathf.Max(
									Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
									Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
								);
								
								Color baseColor = (maxDiff >= EditorBlockLevelHeight * 0.5f) ? EditorCliffPaintColor : EditorPaintColor;
								float targetAlpha = baseColor.A;
								Color targetColor = new Color(baseColor.R, baseColor.G, baseColor.B, targetAlpha);
								GroundTerrain.Colors[x, z] = GroundTerrain.Colors[x, z].Lerp(targetColor, EditorBrushStrength * delta * 5.0f);
								modified = true;
							}
						}
					}
				}
			}
			else if (isPathing)
			{
				for (int z = cz - brushGridRadius; z <= cz + brushGridRadius; z++)
				{
					for (int x = cx - brushGridRadius; x <= cx + brushGridRadius; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!EditorBrushIsSquare)
							{
								float dx = x - cx;
								float dz = z - cz;
								inBounds = (dx * dx + dz * dz) <= (brushGridRadius * brushGridRadius);
							}
							
							if (inBounds)
							{
								if (pathingAdd)
								{
									GroundTerrain.PathingCodes[x, z] |= pathingMask;
								}
								else
								{
									GroundTerrain.PathingCodes[x, z] &= ~pathingMask;
								}
								modified = true;
							}
						}
					}
				}
			}
			
			if (modified)
			{
				GroundTerrain.UpdateMeshAndPhysics(isHeights, false);
				if (isHeights)
				{
					AlignAllEntitiesToTerrain();
				}
				if (isPathing && PathingOverlayVisible)
				{
					RebuildPathingOverlay();
				}
				EditorHasUnsavedChanges = true;
			}
			return;
		}
		
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float vx = (x - (width - 1) / 2.0f) * spacing;
				float vz = (z - (depth - 1) / 2.0f) * spacing;
				
				float dist = 0.0f;
				bool inBounds = false;
				if (EditorBrushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= EditorBrushRadius && dz <= EditorBrushRadius;
					dist = Mathf.Max(dx, dz);
				}
				else
				{
					dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= EditorBrushRadius;
				}

				if (inBounds)
				{
					float falloff = 1.0f - (dist / EditorBrushRadius);
					falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);
					
					if (isHeights)
					{
						if (ActiveEditorTool == EditorTool.Raise)
						{
							GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] + EditorBrushStrength * falloff * delta, -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Lower)
						{
							GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] - EditorBrushStrength * falloff * delta, -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Flatten)
						{
							GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], EditorFlattenHeight, EditorBrushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Smooth)
						{
							float avg = 0f;
							int count = 0;
							for (int nz = -1; nz <= 1; nz++)
							{
								for (int nx = -1; nx <= 1; nx++)
								{
									int nxVal = x + nx;
									int nzVal = z + nz;
									if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
									{
										avg += GroundTerrain.Heights[nxVal, nzVal];
										count++;
									}
								}
							}
							avg /= count;
							GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], avg, EditorBrushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Cliff)
						{
							float targetHeight = _activeCliffHeight ?? 4.0f;
							GroundTerrain.Heights[x, z] = Mathf.Clamp(Mathf.Lerp(GroundTerrain.Heights[x, z], targetHeight, EditorBrushStrength * falloff * delta * 2.0f), -10.0f, 50.0f);
						}
						else if (ActiveEditorTool == EditorTool.Noise)
						{
							float noiseVal = (float)(GD.Randf() * 2.0 - 1.0) * EditorBrushStrength * falloff * delta * 2.0f;
							GroundTerrain.Heights[x, z] = Mathf.Clamp(GroundTerrain.Heights[x, z] + noiseVal, -10.0f, 50.0f);
						}
						modified = true;
					}
					else if (isPaint)
					{
						float h = GroundTerrain.Heights[x, z];
						float hl = GroundTerrain.Heights[Math.Max(0, x - 1), z];
						float hr = GroundTerrain.Heights[Math.Min(width - 1, x + 1), z];
						float hd = GroundTerrain.Heights[x, Math.Max(0, z - 1)];
						float hu = GroundTerrain.Heights[x, Math.Min(depth - 1, z + 1)];
						
						float maxDiff = Mathf.Max(
							Mathf.Max(Mathf.Abs(h - hl), Mathf.Abs(h - hr)),
							Mathf.Max(Mathf.Abs(h - hd), Mathf.Abs(h - hu))
						);
						
						Color baseColor = (maxDiff >= spacing * 0.5f) ? EditorCliffPaintColor : EditorPaintColor;
						float targetAlpha = baseColor.A;
						Color targetColor = new Color(baseColor.R, baseColor.G, baseColor.B, targetAlpha);
						GroundTerrain.Colors[x, z] = GroundTerrain.Colors[x, z].Lerp(targetColor, EditorBrushStrength * falloff * delta * 3.0f);
						modified = true;
					}
					else if (isPathing)
					{
						int cx = Mathf.Clamp((int)Math.Round(vx / spacing + (width - 1) / 2.0f), 0, width - 1);
						int cz = Mathf.Clamp((int)Math.Round(vz / spacing + (depth - 1) / 2.0f), 0, depth - 1);
						if (pathingAdd)
						{
							GroundTerrain.PathingCodes[cx, cz] |= pathingMask;
						}
						else
						{
							GroundTerrain.PathingCodes[cx, cz] &= ~pathingMask;
						}
						modified = true;
					}
				}
			}
		}
		
		if (modified)
		{
			GroundTerrain.UpdateMeshAndPhysics(isHeights, false);
			
			if (isHeights)
			{
				AlignAllEntitiesToTerrain();
			}
			if (isPathing && PathingOverlayVisible)
			{
				RebuildPathingOverlay();
			}
			EditorHasUnsavedChanges = true;
		}
	}

	public Prop3D SpawnPropExternal(string propId, Vector3 position)
	{
		var prop = new Prop3D();
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);
		
		position.Y = GetTerrainHeightAt(position);
		prop.Position = position;
		
		if (IsMapEditorMode)
		{
			prop.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			prop.Scale *= EditorPlacementScale;
		}
		return prop;
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		if (decalId.StartsWith("res://") || decalId.Contains("/"))
		{
			return decalId;
		}
		string customPath = $"res://Assets/2d/Decals/{decalId}";
		if (ResourceLoader.Exists(customPath))
		{
			return customPath;
		}
		if (!decalId.Contains("."))
		{
			string customPathWithPng = $"res://Assets/2d/Decals/{decalId}.png";
			if (ResourceLoader.Exists(customPathWithPng))
			{
				return customPathWithPng;
			}
		}
		return decalId switch
		{
			"forest" => "res://Assets/UI/forest_path.png",
			"snowy" => "res://Assets/UI/snowy_forest_path.png",
			"flag" => "res://Assets/UI/alliance_flag.png",
			"rune" => "res://Assets/UI/magic_frame.png",
			_ => "res://icon.svg"
		};
	}

	public Decal SpawnDecalExternal(Vector3 position)
	{
		var decal = new Decal();
		decal.TextureAlbedo = GD.Load<Texture2D>("res://icon.svg");
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f);
		decal.SetMeta("DecalId", "logo");
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		
		position.Y = GetTerrainHeightAt(position);
		decal.Position = position;
		
		if (IsMapEditorMode)
		{
			decal.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
			decal.Scale = Vector3.One;
		}
		return decal;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		if (GroundTerrain == null) return 0.0f;
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float spacing = GroundTerrain.Spacing;
		
		float fx = worldPos.X / spacing + (width - 1) / 2.0f;
		float fz = worldPos.Z / spacing + (depth - 1) / 2.0f;
		
		int x = Mathf.Clamp((int)Math.Round(fx), 0, width - 1);
		int z = Mathf.Clamp((int)Math.Round(fz), 0, depth - 1);
		
		return GroundTerrain.Heights[x, z];
	}

	private void AlignAllEntitiesToTerrain()
	{
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.GlobalPosition;
				pos.Y = GetTerrainHeightAt(pos);
				unit.GlobalPosition = pos;
				if (EcsWorld.IsAlive(unit.Entity))
				{
					EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
				}
			}
		}

		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.GlobalPosition;
				pos.Y = GetTerrainHeightAt(pos);
				prop.GlobalPosition = pos;
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				var pos = decal.GlobalPosition;
				pos.Y = GetTerrainHeightAt(pos);
				decal.GlobalPosition = pos;
			}
		}
	}

	private void DeleteObjectAt(Node collider, Vector3 hitPos)
	{
		var unit = FindUnit3DInParentChain(collider);
		if (unit != null)
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return;
		}
		
		Node current = collider;
		while (current != null && current != this)
		{
			if (current is Prop3D prop)
			{
				AllProps.Remove(prop);
				prop.QueueFree();
				return;
			}
			current = current.GetParent();
		}

		// Try to delete a nearby decal
		Decal closestDecal = null;
		float closestDist = 3.0f; // search radius in units
		foreach (var child in GetChildren())
		{
			if (child is Decal dec && GodotObject.IsInstanceValid(dec))
			{
				float d = dec.GlobalPosition.DistanceTo(hitPos);
				if (d < closestDist)
				{
					closestDist = d;
					closestDecal = dec;
				}
			}
		}
		if (closestDecal != null)
		{
			closestDecal.QueueFree();
		}
	}

	private void ProcessMapEditorPhysics(float fDelta)
	{
		var query = new QueryDescription().WithAll<Position, MoveTo, MovementStats>().WithNone<Dead>();
		var arrivedUnits = new List<Entity>();
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats) =>
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && EcsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
			{
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var u3d = EcsWorld.Get<Unit3D>(entity);
					if (GodotObject.IsInstanceValid(u3d))
					{
						u3d.Velocity = Vector3.Zero;
					}
				}
				return;
			}
			var current = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			var target = new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);

			float dist = current.DistanceTo(target);
			if (dist < 0.2f)
			{
				arrivedUnits.Add(entity);
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var unit3D = EcsWorld.Get<Unit3D>(entity);
					unit3D.Velocity = Vector3.Zero;
				}
			}
			else
			{
				Vector3 dir = (target - current).Normalized();
				Vector3 velocity = dir * stats.Speed;

				if (EcsWorld.Has<Unit3D>(entity))
				{
					var unit3D = EcsWorld.Get<Unit3D>(entity);
					unit3D.Velocity = velocity;
					unit3D.MoveAndSlide();

					var finalPos = unit3D.GlobalPosition;
					pos.Value = new System.Numerics.Vector3(finalPos.X, finalPos.Y, finalPos.Z);

					if (unit3D.Velocity.LengthSquared() > 0.01f)
					{
						float angle = Mathf.Atan2(-unit3D.Velocity.X, -unit3D.Velocity.Z);
						var rot = unit3D.Rotation;
						rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * fDelta);
						unit3D.Rotation = rot;
					}
				}
				else
				{
					var nextPos = current + dir * stats.Speed * fDelta;
					pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
				}
			}
		});

		foreach (var entity in arrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}

	private void UpdateDayNightVisuals(float progress)
	{
		var worldEnv = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		var light = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
		if (worldEnv == null || worldEnv.Environment == null) return;
		
		var env = worldEnv.Environment;
		
		// FIX 1: ACES keeps shadow details visible
		env.TonemapMode = Godot.Environment.ToneMapper.Aces; 
		env.AdjustmentEnabled = true;
		env.AdjustmentSaturation = 1.2f;
		env.AdjustmentContrast = 1.05f; 

		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;

		// 1. Base Sky/Sun Colors
		Color[] colors = new Color[]
		{
			new Color(0.22f, 0.38f, 0.58f),   // Dawn
			new Color(0.9804f, 0.9569f, 0.8784f),  // Day
			new Color(0.58f, 0.35f, 0.42f),   // Dusk
			new Color(0.19f, 0.29f, 0.48f),   // Night
			new Color(0.22f, 0.38f, 0.58f)    // Dawn (wrap)
		};

		// Sun directional light energy
		float[] sunEnergies = new float[] { 0.88f, 1.0f, 0.95f, 0.80f, 0.88f }; 

		// 2. Separate Ambient Energy Profile
		float[] ambientEnergies = new float[] { 0.8f, 0.5f, 0.75f, 1.6f, 0.8f }; 

		float segment = progress * 4f;
		int idx = (int)Mathf.Floor(segment) % 4;
		float t = segment - idx;

		// Calculate current base color and energies
		Color rawColor = colors[idx].Lerp(colors[idx + 1], t);
		float currentSunEnergy = Mathf.Lerp(sunEnergies[idx], sunEnergies[idx + 1], t);
		float currentAmbientEnergy = Mathf.Lerp(ambientEnergies[idx], ambientEnergies[idx + 1], t);

		Color nightVisibilityFloor = new Color(0.22f, 0.26f, 0.42f); 
		Color ambientColor = new Color(
			Mathf.Max(rawColor.R, nightVisibilityFloor.R),
			Mathf.Max(rawColor.G, nightVisibilityFloor.G),
			Mathf.Max(rawColor.B, nightVisibilityFloor.B)
		);

		// Apply Ambient Settings
		env.AmbientLightColor = ambientColor;
		env.AmbientLightEnergy = currentAmbientEnergy;

		if (light != null)
		{
			light.LightColor = rawColor;
			light.LightEnergy = currentSunEnergy;
			
			float angle = progress * 360.0f;
			light.RotationDegrees = new Vector3(-90.0f, angle, 0.0f);
		}
	}

	public void CycleTimeOfDay()
	{
		_timeOfDayIndex = (_timeOfDayIndex + 1) % 4;

		float targetHour = _timeOfDayIndex switch
		{
			0 => 12.0f, // Day
			1 => 19.0f, // Sunset
			2 => 0.0f,  // Night
			3 => 5.5f,  // Dawn
			_ => 12.0f
		};

		float progress = targetHour / 24f;
		_timeOfDayTimer = progress * TimeOfDayCycleDuration;

		UpdateDayNightVisuals(progress);
	}

	public string GetTimeOfDayName()
	{
		return _timeOfDayIndex switch
		{
			0 => "Day",
			1 => "Sunset",
			2 => "Night",
			3 => "Dawn",
			_ => "Unknown"
		};
	}

	public void SaveMapToFile(string customPath = "")
	{
		if (GroundTerrain == null) return;

		var saveData = new MapSaveData();
		saveData.WaterEnabled = GroundTerrain.WaterEnabled;
		saveData.WaterHeight = GroundTerrain.WaterHeight;
		saveData.BlockMode = EditorBlockMode;
		saveData.BlockLevelHeight = EditorBlockLevelHeight;
		saveData.WC3BlockMode = EditorBlockMode;
		saveData.WC3LevelHeight = EditorBlockLevelHeight;
		saveData.CameraBoundsLeft = EditorCameraBoundsLeft;
		saveData.CameraBoundsRight = EditorCameraBoundsRight;
		saveData.CameraBoundsTop = EditorCameraBoundsTop;
		saveData.CameraBoundsBottom = EditorCameraBoundsBottom;
		saveData.SkyboxPath = EditorSkyboxPath;
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		saveData.Heights = new float[width * depth];
		saveData.Colors = new string[width * depth];
		saveData.Pathing = new int[width * depth];

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int idx = z * width + x;
				saveData.Heights[idx] = GroundTerrain.Heights[x, z];
				saveData.Colors[idx] = GroundTerrain.Colors[x, z].ToHtml(true);
				saveData.Pathing[idx] = GroundTerrain.PathingCodes != null ? GroundTerrain.PathingCodes[x, z] : (EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING);
			}
		}

		saveData.Units = new List<UnitSaveData>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				saveData.Units.Add(new UnitSaveData
				{
					UnitId = unit.UnitId,
					PosX = unit.Position.X,
					PosY = unit.Position.Y,
					PosZ = unit.Position.Z,
					RotationY = unit.RotationDegrees.Y,
					Scale = unit.Scale.X,
					IsEnemy = unit.IsEnemy
				});
			}
		}

		saveData.Props = new List<PropSaveData>();
		saveData.Decals = new List<DecalSaveData>();
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				saveData.Props.Add(new PropSaveData
				{
					PropId = prop.PropId,
					PosX = prop.Position.X,
					PosY = prop.Position.Y,
					PosZ = prop.Position.Z,
					RotationY = prop.RotationDegrees.Y,
					Scale = prop.Scale.X
				});
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				saveData.Decals.Add(new DecalSaveData
				{
					DecalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo",
					PosX = decal.Position.X,
					PosY = decal.Position.Y,
					PosZ = decal.Position.Z,
					RotationY = decal.RotationDegrees.Y,
					Scale = decal.Scale.X
				});
			}
		}

		try
		{
			string json = System.Text.Json.JsonSerializer.Serialize(saveData);
			string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(json);
				EditorHasUnsavedChanges = false;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false)
	{
		string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
		if (!FileAccess.FileExists(path)) return false;

		try
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			if (file == null) return false;

			string json = file.GetAsText();
			var saveData = System.Text.Json.JsonSerializer.Deserialize<MapSaveData>(json);
			if (saveData == null) return false;

			ClearAllUnits();

			if (GroundTerrain == null)
			{
				var terrainNode = new EditableTerrain();
				terrainNode.Name = "Ground";
				AddChild(terrainNode);
				GroundTerrain = terrainNode;
			}

			bool waterEnabled = saveData.WaterEnabled ?? true;
			GroundTerrain.WaterEnabled = waterEnabled;
			MapEditorHUD.Instance?.UpdateWaterEnabledExternal(waterEnabled);
			if (saveData.WaterHeight.HasValue)
			{
				GroundTerrain.WaterHeight = saveData.WaterHeight.Value;
				MapEditorHUD.Instance?.UpdateWaterHeightExternal(saveData.WaterHeight.Value);
			}
			bool isBlock = saveData.BlockMode ?? saveData.WC3BlockMode ?? false;
			EditorBlockMode = isBlock;
			MapEditorHUD.Instance?.UpdateBlockModeExternal(isBlock);

			float step = saveData.BlockLevelHeight ?? saveData.WC3LevelHeight ?? 4.0f;
			EditorBlockLevelHeight = step;
			MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(step);

			if (saveData.CameraBoundsLeft.HasValue) EditorCameraBoundsLeft = saveData.CameraBoundsLeft.Value;
			if (saveData.CameraBoundsRight.HasValue) EditorCameraBoundsRight = saveData.CameraBoundsRight.Value;
			if (saveData.CameraBoundsTop.HasValue) EditorCameraBoundsTop = saveData.CameraBoundsTop.Value;
			if (saveData.CameraBoundsBottom.HasValue) EditorCameraBoundsBottom = saveData.CameraBoundsBottom.Value;
			MapEditorHUD.Instance?.UpdateCameraBoundsUI();
			RebuildCameraBoundsOverlay();

			if (!string.IsNullOrEmpty(saveData.SkyboxPath))
			{
				SetSkyboxTexture(saveData.SkyboxPath);
				MapEditorHUD.Instance?.UpdateSelectedSkyboxExternal(saveData.SkyboxPath);
			}

			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			if (saveData.Heights != null && saveData.Heights.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int idx = z * width + x;
						GroundTerrain.Heights[x, z] = saveData.Heights[idx];
					}
				}
			}

			if (saveData.Colors != null && saveData.Colors.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int idx = z * width + x;
						GroundTerrain.Colors[x, z] = Color.FromHtml(saveData.Colors[idx]);
					}
				}
			}

			if (saveData.Pathing != null && saveData.Pathing.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int idx = z * width + x;
						GroundTerrain.PathingCodes[x, z] = saveData.Pathing[idx];
					}
				}
			}
			else
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						GroundTerrain.PathingCodes[x, z] = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING;
					}
				}
			}

			GroundTerrain.UpdateMeshAndPhysics();

			if (!terrainOnly)
			{
				if (saveData.Units != null)
				{
					foreach (var u in saveData.Units)
					{
						SpawnUnitExternal(u.UnitId, new Vector3(u.PosX, u.PosY, u.PosZ), u.IsEnemy, u.RotationY, u.Scale);
					}
				}

				if (saveData.Props != null)
				{
					foreach (var p in saveData.Props)
					{
						SpawnPropExternalWithParams(p.PropId, new Vector3(p.PosX, p.PosY, p.PosZ), p.RotationY, p.Scale);
					}
				}

				if (saveData.Decals != null)
				{
					foreach (var d in saveData.Decals)
					{
						SpawnDecalExternalWithParams(d.DecalId, new Vector3(d.PosX, d.PosY, d.PosZ), d.RotationY, d.Scale);
					}
				}
			}

			EditorHasUnsavedChanges = false;
			MapEditorHUD.Instance?.RegenerateMinimap();
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr(ex.Message);
			return false;
		}
	}

	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale)
	{
		position.Y = GetTerrainHeightAt(position);
		if (!UnitRegistry.ContainsKey(unitId))
		{
			var dynamicMeta = new UnitMetadata
			{
				Name = System.IO.Path.GetFileNameWithoutExtension(unitId).Replace("_", " "),
				MaxHp = 100f,
				Damage = 10f,
				Range = 2f,
				Armor = 2f,
				Speed = 6.0f,
				ProductionTime = 10f,
				ModelPath = unitId.StartsWith("res://") ? unitId : $"res://Assets/3d/Characters/{unitId}"
			};
			if (unitId.Contains("Buildings") || unitId.Contains("castle") || unitId.Contains("tower"))
			{
				dynamicMeta.Speed = 0f;
				dynamicMeta.ModelPath = unitId.StartsWith("res://") ? unitId : $"res://Assets/3d/Buildings/{unitId}";
			}
			UnitRegistry[unitId] = dynamicMeta;
		}

		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
		
		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(unitId, meta.Speed == 0f);

		string name = meta.Name;
		if (isEnemy)
		{
			if (unitId == "worker") name = "Orc Worker";
			else if (unitId == "soldier") name = "Orc Raider";
			else if (unitId == "archer") name = "Dark Archer";
			else if (unitId == "priest") name = "Orc Shaman";
			else if (unitId == "castle") name = "Orc Stronghold";
			else if (unitId == "tower") name = "Orc Totem Tower";
		}

		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, meta.Speed == 0f, isEnemy);
		unit3D.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		unit3D.Scale = Vector3.One * scale;

		return unit3D;
	}

	public Prop3D SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
	{
		var prop = new Prop3D();
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);
		
		position.Y = GetTerrainHeightAt(position);
		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * scale;
		
		return prop;
	}

	public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, float rotationY, float scale)
	{
		var decal = new Decal();
		decal.TextureAlbedo = GD.Load<Texture2D>(GetDecalTexturePath(decalId));
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * scale;
		decal.SetMeta("DecalId", string.IsNullOrEmpty(decalId) ? "logo" : decalId);
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		
		position.Y = GetTerrainHeightAt(position);
		decal.Position = position;
		decal.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		decal.Scale = Vector3.One;
		
		return decal;
	}

	public void DeleteNodeExternal(Node node)
	{
		if (_selectedEditorObject == node)
		{
			SelectedEditorObject = null;
		}
		if (node is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
		}
		else if (node is Prop3D prop && GodotObject.IsInstanceValid(prop))
		{
			AllProps.Remove(prop);
			prop.QueueFree();
		}
		else if (node is Decal decal && GodotObject.IsInstanceValid(decal))
		{
			decal.QueueFree();
		}
	}

	public IEditorAction DeleteObjectAtWithUndo(Node collider, Vector3 hitPos)
	{
		if (collider == _selectedEditorObject)
		{
			SelectedEditorObject = null;
		}
		var unit = FindUnit3DInParentChain(collider);
		if (unit == null)
		{
			Unit3D closestUnit = null;
			float closestUnitDist = 2.0f;
			foreach (var u in AllUnits)
			{
				if (GodotObject.IsInstanceValid(u))
				{
					float d = u.Position.DistanceTo(hitPos);
					if (d < closestUnitDist)
					{
						closestUnitDist = d;
						closestUnit = u;
					}
				}
			}
			if (closestUnit != null)
			{
				unit = closestUnit;
			}
		}

		if (unit != null)
		{
			if (unit == _selectedEditorObject)
			{
				SelectedEditorObject = null;
			}
			var action = new ObjectDeleteAction("unit", unit.UnitId, unit.Position, unit.RotationDegrees.Y, unit.Scale.X, unit.IsEnemy, unit);
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return action;
		}
		
		Prop3D prop = null;
		Node current = collider;
		while (current != null && current != this)
		{
			if (current is Prop3D p)
			{
				prop = p;
				break;
			}
			current = current.GetParent();
		}

		if (prop == null)
		{
			Prop3D closestProp = null;
			float closestPropDist = 2.0f;
			foreach (var child in GetChildren())
			{
				if (child is Prop3D p && GodotObject.IsInstanceValid(p))
				{
					float d = p.Position.DistanceTo(hitPos);
					if (d < closestPropDist)
					{
						closestPropDist = d;
						closestProp = p;
					}
				}
			}
			if (closestProp != null)
			{
				prop = closestProp;
			}
		}

		if (prop != null)
		{
			if (prop == _selectedEditorObject)
			{
				SelectedEditorObject = null;
			}
			var action = new ObjectDeleteAction("prop", prop.PropId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
			AllProps.Remove(prop);
			prop.QueueFree();
			return action;
		}

		Decal closestDecal = null;
		float closestDist = 3.0f;
		foreach (var child in GetChildren())
		{
			if (child is Decal dec && GodotObject.IsInstanceValid(dec))
			{
				float d = dec.GlobalPosition.DistanceTo(hitPos);
				if (d < closestDist)
				{
					closestDist = d;
					closestDecal = dec;
				}
			}
		}
		if (closestDecal != null)
		{
			if (closestDecal == _selectedEditorObject)
			{
				SelectedEditorObject = null;
			}
			var action = new ObjectDeleteAction("decal", "", closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
			closestDecal.QueueFree();
			return action;
		}
		return null;
	}

	public void AlignAllEntitiesToTerrainExternal()
	{
		AlignAllEntitiesToTerrain();
	}

	private void UpdateEditorPreview(Vector3 position)
	{
		bool needsPreview = ActiveEditorTool == EditorTool.PlaceUnit ||
							ActiveEditorTool == EditorTool.PlaceProp ||
							ActiveEditorTool == EditorTool.PlaceDecal;

		if (!needsPreview)
		{
			ClearEditorPreview();
			return;
		}

		string reqType = ActiveEditorTool.ToString();
		string reqId = ActivePlaceId;
		bool reqIsEnemy = PlaceUnitIsEnemy;

		if (_editorPreviewNode == null || _editorPreviewType != reqType || _editorPreviewId != reqId || _editorPreviewIsEnemy != reqIsEnemy)
		{
			ClearEditorPreview();
			
			_editorPreviewType = reqType;
			_editorPreviewId = reqId;
			_editorPreviewIsEnemy = reqIsEnemy;

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				if (!UnitRegistry.ContainsKey(reqId))
				{
					var dynamicMeta = new UnitMetadata
					{
						Name = System.IO.Path.GetFileNameWithoutExtension(reqId).Replace("_", " "),
						MaxHp = 100f,
						Damage = 10f,
						Range = 2f,
						Armor = 2f,
						Speed = 6.0f,
						ProductionTime = 10f,
						ModelPath = reqId.StartsWith("res://") ? reqId : $"res://Assets/3d/Characters/{reqId}"
					};
					if (reqId.Contains("Buildings") || reqId.Contains("castle") || reqId.Contains("tower"))
					{
						dynamicMeta.Speed = 0f;
						dynamicMeta.ModelPath = reqId.StartsWith("res://") ? reqId : $"res://Assets/3d/Buildings/{reqId}";
					}
					UnitRegistry[reqId] = dynamicMeta;
				}

				if (UnitRegistry.TryGetValue(reqId, out var meta))
				{
 					string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(reqId, meta.Speed == 0f);

					var previewUnit = new Unit3D();
					previewUnit.UnitId = reqId;
					previewUnit.IsBuilding = meta.Speed == 0f;
					previewUnit.IsEnemy = reqIsEnemy;
					previewUnit.IsPreview = true;
					AddChild(previewUnit);
					previewUnit.LoadModel(modelPath);

					Color color = reqIsEnemy ? new Color(1.0f, 0.3f, 0.15f) : new Color(0.15f, 0.65f, 1.0f);
					MakeHologramRecursive(previewUnit, color);
					_editorPreviewNode = previewUnit;
				}
			}
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				var previewProp = new Prop3D();
				previewProp.PropId = reqId;
				previewProp.IsPreview = true;
				AddChild(previewProp);

				Color color = new Color(0.95f, 0.82f, 0.15f);
				MakeHologramRecursive(previewProp, color);
				_editorPreviewNode = previewProp;
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				var previewDecal = new Decal();
				previewDecal.TextureAlbedo = GD.Load<Texture2D>(GetDecalTexturePath(reqId));
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
				AddChild(previewDecal);
				previewDecal.SetMeta("DecalId", string.IsNullOrEmpty(reqId) ? "logo" : reqId);

				Color color = new Color(1.0f, 1.0f, 1.0f);
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				previewDecal.AlbedoMix = 0.5f;
				_editorPreviewNode = previewDecal;
			}
		}

		if (_editorPreviewNode != null)
		{
			if (!_hasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
			float previewRot = (EditorRandomRotation && !_isPastingObject) ? _cachedRandomRotation : EditorPlacementRotation;
			float previewScaleVal = (EditorRandomScale && !_isPastingObject) ? _cachedRandomScale : EditorPlacementScale;

			Vector3 previewPos = position;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				float spacing = GroundTerrain.Spacing;
				int width = GroundTerrain.Width;
				int depth = GroundTerrain.Depth;
				float fx = Mathf.Round(previewPos.X / spacing + (width - 1) / 2.0f);
				previewPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * spacing;
				float fz = Mathf.Round(previewPos.Z / spacing + (depth - 1) / 2.0f);
				previewPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * spacing;
			}
			previewPos.Y = GetTerrainHeightAt(previewPos);
			if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp)
			{
				float radius = GetPlacementRadius(ActivePlaceId, previewScaleVal);
				var finalPos = FindNearestFreePosition(previewPos, radius);
				if (finalPos != null)
				{
					previewPos = finalPos.Value;
				}
			}
			_editorPreviewNode.Position = previewPos;
			_editorPreviewNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
			if (_editorPreviewNode is Decal previewDecal)
			{
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * previewScaleVal;
				previewDecal.Scale = Vector3.One;
			}
			else
			{
				_editorPreviewNode.Scale = Vector3.One * previewScaleVal;
			}
			_editorPreviewNode.Visible = true;
		}
	}

	private void ClearEditorPreview()
	{
		if (_editorPreviewNode != null)
		{
			_editorPreviewNode.QueueFree();
			_editorPreviewNode = null;
		}
		_editorPreviewType = "";
		_editorPreviewId = "";
		_editorPreviewIsEnemy = false;
	}

	private void MakeHologramRecursive(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(color.R, color.G, color.B) * 0.5f;
			meshInstance.MaterialOverride = mat;
		}
		foreach (var child in node.GetChildren())
		{
			MakeHologramRecursive(child, color);
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
		{
			var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
			EcsWorld.Set(unit.Entity, new Owner(playerOwner));
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
			{
				string name = meta.Name;
				if (isEnemy)
				{
					if (unit.UnitId == "worker") name = "Orc Worker";
					else if (unit.UnitId == "soldier") name = "Orc Raider";
					else if (unit.UnitId == "archer") name = "Dark Archer";
					else if (unit.UnitId == "priest") name = "Orc Shaman";
					else if (unit.UnitId == "castle") name = "Orc Stronghold";
					else if (unit.UnitId == "tower") name = "Orc Totem Tower";
				}
				EcsWorld.Set(unit.Entity, new Name(name));
			}
			unit.IsEnemy = isEnemy;
			if (unit.UnitId == "priest")
			{
				Color priestColor = isEnemy ? new Color(0.8f, 0.2f, 0.8f) : new Color(1.0f, 0.85f, 0.2f);
				unit.ApplyModelTint(priestColor);
			}
			else if (unit.UnitId == "worker")
			{
				Color workerColor = isEnemy ? new Color(0.6f, 0.4f, 0.2f) : new Color(0.8f, 0.6f, 0.4f);
				unit.ApplyModelTint(workerColor);
			}
			unit.IsSelected = unit.IsSelected;
		}
	}

	private void UpdateDecalSelectionRing(Decal decal, bool selected)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("EditorSelectionRing");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (selected)
		{
			var ring = new MeshInstance3D();
			ring.Name = "EditorSelectionRing";
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.1f, 0.7f, 0.95f);
			material.EmissionEnabled = true;
			material.Emission = new Color(0.1f, 0.7f, 0.95f);
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void UpdateDecalHoverRing(Decal decal, bool hovered)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("EditorHoverRing");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (hovered && SelectedEditorObject != decal)
		{
			var ring = new MeshInstance3D();
			ring.Name = "EditorHoverRing";
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.4f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.EmissionEnabled = true;
			material.Emission = new Color(1.0f, 1.0f, 1.0f) * 0.3f;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private int GetServerEntityId(Entity localEntity)
	{
		if (_clientToServerEntityMap.TryGetValue(localEntity.Id, out int serverId))
		{
			return serverId;
		}
		return localEntity.Id;
	}

	private Entity FindServerEntity(int entityId)
	{
		foreach (var unit in AllUnits)
		{
			if (unit.Entity.Id == entityId)
			{
				return unit.Entity;
			}
		}
		return Entity.Null;
	}

	private Prop3D FindClosestProp(Vector3 position, string propIdType)
	{
		Prop3D closest = null;
		float closestDist = float.MaxValue;
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				float dist = prop.GlobalPosition.DistanceTo(position);
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = prop;
				}
			}
		}
		return closest;
	}

	public int GetOwnerPeerId(Entity unitEntity)
	{
		if (!EcsWorld.Has<Owner>(unitEntity)) return -1;
		var owner = EcsWorld.Get<Owner>(unitEntity).PlayerEntity;
		foreach (var kvp in _peerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}
		return -1;
	}

	private bool IsClientAuthorized(int peerId, Entity unitEntity)
	{
		if (!EcsWorld.Has<Owner>(unitEntity)) return false;
		var ownerComp = EcsWorld.Get<Owner>(unitEntity);
		if (_peerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}
		return false;
	}

	private bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity)
	{
		if (EcsWorld.Has<Owner>(unitEntity) && EcsWorld.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
		{
			return true;
		}
		Vector3 unitPos = Vector3.Zero;
		foreach (var unit in AllUnits)
		{
			if (unit.Entity == unitEntity)
			{
				unitPos = unit.GlobalPosition;
				break;
			}
		}
		foreach (var unit in AllUnits)
		{
			if (EcsWorld.Has<Owner>(unit.Entity) && EcsWorld.Get<Owner>(unit.Entity).PlayerEntity.Value == playerEntity)
			{
				if (unit.GlobalPosition.DistanceTo(unitPos) <= 15.0f)
				{
					return true;
				}
			}
		}
		return false;
	}

	private void QueueClientCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var cmd = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new NetworkVector3(targetPos),
			TargetEntityId = targetEntityId,
			ArgString = argString
		};
		_unacknowledgedCommands.Add(cmd);
		GD.Print($"[CLIENT_CMD_SENT] CommandType={commandType} Units={string.Join(",", unitIds)} Target={targetPos}");
		var payload = MemoryPackSerializer.Serialize(cmd);
		RpcId(1, nameof(SubmitCommand), payload);
	}

	private void UpdateClientTick(float fDelta)
	{
		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			foreach (var cmd in _unacknowledgedCommands)
			{
				var payload = MemoryPackSerializer.Serialize(cmd);
				RpcId(1, nameof(SubmitCommand), payload);
			}
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				var pos = new NetworkVector3(camera.GlobalPosition);
				RpcId(1, nameof(UpdateClientCamera), MemoryPackSerializer.Serialize(pos));
			}
		}
		ProcessClientPredictionAndInterpolation(fDelta);
	}

	private float GetDynamicInterpolationFactor()
	{
		float bufferTime = 0.1f;
		if (LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null)
		{
			string latStr = LobbyManager.Instance.LocalPlayer.Latency;
			if (!string.IsNullOrEmpty(latStr) && latStr != "--" && latStr.Contains(" ms"))
			{
				string numStr = latStr.Replace(" ms", "").Trim();
				if (float.TryParse(numStr, out float rttMs))
				{
					bufferTime = Mathf.Max(0.1f, (rttMs / 1000f) * 1.5f);
				}
			}
		}
		return 1f / bufferTime;
	}

	private void ProcessClientPredictionAndInterpolation(float fDelta)
	{
		float factor = GetDynamicInterpolationFactor();
		var query = new QueryDescription().WithAll<InterpolationTarget, Unit3D>();
		EcsWorld.Query(in query, (Entity entity, ref InterpolationTarget target, ref Unit3D unit) =>
		{
			if (!GodotObject.IsInstanceValid(unit)) return;
			Vector3 targetPos = new Vector3(target.Position.X, target.Position.Y, target.Position.Z);
			Vector3 targetVel = new Vector3(target.Velocity.X, target.Velocity.Y, target.Velocity.Z);
			if (!unit.IsEnemy)
			{
				if (EcsWorld.Has<MoveTo>(entity) && EcsWorld.Has<MovementStats>(entity))
				{
					var moveTo = EcsWorld.Get<MoveTo>(entity);
					var stats = EcsWorld.Get<MovementStats>(entity);
					Vector3 dest = new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);
					float distToDest = unit.GlobalPosition.DistanceTo(dest);
					if (distToDest > 0.05f)
					{
						Vector3 dir = (dest - unit.GlobalPosition).Normalized();
						float step = stats.Speed * fDelta;
						if (step > distToDest) step = distToDest;
						unit.GlobalPosition += dir * step;
						unit.Velocity = dir * stats.Speed;
					}
					else
					{
						unit.GlobalPosition = dest;
						unit.Velocity = Vector3.Zero;
						EcsWorld.Remove<MoveTo>(entity);
					}
					GD.Print($"[CLIENT_ESTIMATED] Unit={entity.Id} Pos={unit.GlobalPosition} Target={moveTo.Target}");
				}
				else
				{
					float dist = unit.GlobalPosition.DistanceTo(targetPos);
					if (dist > 2.0f)
					{
						unit.GlobalPosition = targetPos;
						unit.Velocity = targetVel;
					}
					else if (dist > 0.5f)
					{
						Vector3 diff = targetPos - unit.GlobalPosition;
						unit.GlobalPosition += diff * (fDelta / 0.2f);
					}
					else if (dist > 0.01f)
					{
						Vector3 diff = targetPos - unit.GlobalPosition;
						unit.GlobalPosition += diff * (fDelta / 0.5f);
					}
				}
				if (EcsWorld.Has<Position>(entity))
				{
					EcsWorld.Set(entity, new Position(new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z)));
				}
			}
			else
			{
				unit.GlobalPosition = unit.GlobalPosition.Lerp(targetPos, factor * fDelta);
				unit.GlobalRotation = new Vector3(0, Mathf.LerpAngle(unit.GlobalRotation.Y, target.RotationY, factor * fDelta), 0);
				unit.Velocity = targetVel;
				if (EcsWorld.Has<Position>(entity))
				{
					EcsWorld.Set(entity, new Position(new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z)));
				}
			}
		});
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		var cmd = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);
		GD.Print($"[SERVER_CMD_RECEIVED] Peer={senderId} CommandType={cmd.CommandType} Units={string.Join(",", cmd.UnitEntityIds)} Target={cmd.TargetPosition.ToGodot()}");
		ExecuteServerCommand(senderId, cmd);
		RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void AcknowledgeCommand(int commandId)
	{
		_unacknowledgedCommands.RemoveAll(c => c.CommandId == commandId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdateClientCamera(byte[] payload)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		var pos = MemoryPackSerializer.Deserialize<NetworkVector3>(payload);
		_clientCameraPositions[senderId] = pos.ToGodot();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveSnapshot(byte[] payload)
	{
		if (Multiplayer.IsServer()) return;
		ProcessSnapshotDirect(payload);
	}

	private void ProcessSnapshotDirect(byte[] payload)
	{
		var snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(payload);
		if (snapshot.Sequence <= _lastAppliedSnapshotSequence) return;
		_lastAppliedSnapshotSequence = snapshot.Sequence;

		if (snapshot.IsBaseline)
		{
			_lastReceivedBaselineSeq = snapshot.Sequence;
			_hasReceivedInitialBaseline = true;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			ApplyWorldSnapshot(snapshot);
			while (_queuedDeltas.Count > 0 && _queuedDeltas[0].BaseSequence == _lastReceivedBaselineSeq)
			{
				var nextDelta = _queuedDeltas[0];
				_queuedDeltas.RemoveAt(0);
				ApplyWorldSnapshot(nextDelta);
			}
		}
		else
		{
			if (!_hasReceivedInitialBaseline || snapshot.BaseSequence != _lastReceivedBaselineSeq)
			{
				_queuedDeltas.Add(snapshot);
				_queuedDeltas.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
			}
			else
			{
				ApplyWorldSnapshot(snapshot);
			}
		}
	}

	private void ExecuteServerCommand(int peerId, NetworkCommand cmd)
	{
		if (cmd.CommandType == "move")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				Vector3 scattered = new Vector3(cmd.TargetPosition.X + offsetX, cmd.TargetPosition.Y, cmd.TargetPosition.Z + offsetZ);
				var moveTo = new MoveTo(new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z));
				if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
				else EcsWorld.Add(entity, moveTo);
				unitIndex++;
			}
		}
		else if (cmd.CommandType == "attack")
		{
			var targetEntity = FindServerEntity(cmd.TargetEntityId);
			if (targetEntity != Entity.Null)
			{
				foreach (int serverId in cmd.UnitEntityIds)
				{
					var entity = FindServerEntity(serverId);
					if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
					ClearUnitOrders(entity);
					var attackTarget = new AttackTarget(targetEntity);
					if (EcsWorld.Has<AttackTarget>(entity)) EcsWorld.Set(entity, attackTarget);
					else EcsWorld.Add(entity, attackTarget);
				}
			}
		}
		else if (cmd.CommandType == "follow")
		{
			var targetEntity = FindServerEntity(cmd.TargetEntityId);
			if (targetEntity != Entity.Null)
			{
				foreach (int serverId in cmd.UnitEntityIds)
				{
					var entity = FindServerEntity(serverId);
					if (entity == Entity.Null || !IsClientAuthorized(peerId, entity) || entity == targetEntity) continue;
					ClearUnitOrders(entity);
					if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
					{
						var healTarget = new HealingTarget(targetEntity);
						EcsWorld.Add(entity, healTarget);
					}
					else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
					{
						var follow = new Realm.Ecs.Components.Movement.Follow(targetEntity);
						if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) EcsWorld.Set(entity, follow);
						else EcsWorld.Add(entity, follow);
					}
				}
			}
		}
		else if (cmd.CommandType == "gather")
		{
			Prop3D prop = FindClosestProp(cmd.TargetPosition.ToGodot(), "");
			if (prop != null)
			{
				string resType = prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};
				if (resType != null)
				{
					foreach (int serverId in cmd.UnitEntityIds)
					{
						var entity = FindServerEntity(serverId);
						if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
						if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value != "worker") continue;
						ClearUnitOrders(entity);
						var gatherer = new Gatherer(resType, prop);
						if (EcsWorld.Has<Gatherer>(entity)) EcsWorld.Set(entity, gatherer);
						else EcsWorld.Add(entity, gatherer);
						var moveTo = new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z));
						if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
						else EcsWorld.Add(entity, moveTo);
					}
				}
			}
		}
		else if (cmd.CommandType == "stop")
		{
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				foreach (var unit in AllUnits)
				{
					if (unit.Entity == entity)
					{
						unit.Velocity = Vector3.Zero;
						break;
					}
				}
			}
		}
		else if (cmd.CommandType == "hold")
		{
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				foreach (var unit in AllUnits)
				{
					if (unit.Entity == entity)
					{
						unit.Velocity = Vector3.Zero;
						break;
					}
				}
				if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity))
				{
					EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(entity);
				}
			}
		}
		else if (cmd.CommandType == "patrol")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
				{
					int row = unitIndex / cols;
					int col = unitIndex % cols;
					float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
					float offsetZ = row * spacing;
					Vector3 unitPos = Vector3.Zero;
					foreach (var u in AllUnits)
					{
						if (u.Entity == entity)
						{
							unitPos = u.GlobalPosition;
							break;
						}
					}
					var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
					var patrolB = new System.Numerics.Vector3(cmd.TargetPosition.X + offsetX, cmd.TargetPosition.Y, cmd.TargetPosition.Z + offsetZ);
					var patrol = new Patrol(patrolA, patrolB);
					if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Set(entity, patrol);
					else EcsWorld.Add(entity, patrol);
					var moveTo = new MoveTo(patrolB);
					if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
					else EcsWorld.Add(entity, moveTo);
					unitIndex++;
				}
			}
		}
		else if (cmd.CommandType == "move_queued")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity)) continue;
				bool alreadyMoving = EcsWorld.Has<MoveTo>(entity);
				if (!alreadyMoving) ClearUnitOrders(entity);
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				Vector3 scattered = new Vector3(cmd.TargetPosition.X + offsetX, cmd.TargetPosition.Y, cmd.TargetPosition.Z + offsetZ);
				var targetVec = new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z);
				if (alreadyMoving)
				{
					if (EcsWorld.Has<WaypointQueue>(entity))
					{
						var q = EcsWorld.Get<WaypointQueue>(entity);
						if (q.Waypoints == null) q.Waypoints = new List<System.Numerics.Vector3>();
						q.Waypoints.Add(targetVec);
						EcsWorld.Set(entity, q);
					}
					else
					{
						var q = new WaypointQueue(new List<System.Numerics.Vector3> { targetVec });
						EcsWorld.Add(entity, q);
					}
				}
				else
				{
					var moveTo = new MoveTo(targetVec);
					if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
					else EcsWorld.Add(entity, moveTo);
				}
				unitIndex++;
			}
		}
		else if (cmd.CommandType == "build")
		{
			string buildType = cmd.ArgString;
			if (UnitRegistry.TryGetValue(buildType, out var meta))
			{
				var playerOwner = _peerIdToPlayerEntityMap[peerId].AsPlayerEntity(EcsWorld);
				Vector3 position = cmd.TargetPosition.ToGodot();
				string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(buildType, true);
				var bldEntity = CreateEcsUnit(buildType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, position, playerOwner);
				SpawnUnit3D(bldEntity, buildType, modelPath, position, true, false);
			}
		}
		else if (cmd.CommandType == "spell")
		{
			string spellId = cmd.ArgString;
			Vector3 position = cmd.TargetPosition.ToGodot();
			IUnit caster = null;
			if (cmd.UnitEntityIds.Count > 0)
			{
				var casterEntity = FindServerEntity(cmd.UnitEntityIds[0]);
				if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
				{
					caster = new UnitWrapper(casterEntity, EcsWorld);
				}
			}
			OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));
			if (spellId == "fireball")
			{
				DealSpellDamageAOE(position, 4.0f, 50f);
			}
			else if (spellId == "lightning")
			{
				DealSpellDamageAOE(position, 2.0f, 80f);
			}
			else if (spellId == "holylight")
			{
				HealAOE(position, 4.0f, 60f);
			}
			Rpc(nameof(PlaySpellEffect), spellId, cmd.TargetPosition.ToGodot());
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void PlaySpellEffect(string spellId, Vector3 position)
	{
		if (spellId == "fireball")
		{
			SpawnFireblastEffect(position);
			SpawnTargetIndicator(position, new Color(0.9f, 0.3f, 0.1f));
		}
		else if (spellId == "lightning")
		{
			SpawnLightningEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.5f, 1f));
		}
		else if (spellId == "holylight")
		{
			SpawnHolyLightEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.9f, 0.3f));
		}
	}

	private void UpdateServerSnapshotTick(float fDelta)
	{
		_snapshotSequence++;
		bool isBaseline = (_snapshotSequence % 30 == 0);
		foreach (var p in LobbyManager.Instance.PlayerList)
		{
			if (p.PeerId == _localPeerId || p.PeerId < 0) continue;
			int peerId = p.PeerId;
			if (!_peerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity)) continue;
			Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
			var snapshotUnits = new List<UnitSnapshot>();
			bool hasBaseline = _lastBaselineSnapshotsPerClient.TryGetValue(peerId, out var lastBaselineMap);
			if (!hasBaseline && !isBaseline) continue;
			var nextBaselineMap = isBaseline ? new Dictionary<int, UnitSnapshot>() : null;
			foreach (var unit in AllUnits)
			{
				if (!GodotObject.IsInstanceValid(unit)) continue;
				if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity)) continue;
				float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
				bool isDetailed = distToCamera <= 35.0f;
				var currentSnap = new UnitSnapshot
				{
					EntityId = unit.Entity.Id,
					UnitId = unit.UnitId,
					OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
					Position = new NetworkVector3(unit.GlobalPosition),
					RotationY = unit.GlobalRotation.Y,
					CurrentHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Current : 0f,
					MaxHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Max : 0f,
					IsDead = EcsWorld.Has<Dead>(unit.Entity),
					IsBuilding = unit.IsBuilding,
					IsDetailed = isDetailed,
					Velocity = new NetworkVector3(unit.Velocity)
				};
				if (isBaseline)
				{
					snapshotUnits.Add(currentSnap);
					nextBaselineMap[unit.Entity.Id] = currentSnap;
				}
				else
				{
					bool changed = true;
					if (lastBaselineMap.TryGetValue(unit.Entity.Id, out var baseSnap))
					{
						if (isDetailed)
						{
							bool posChanged = baseSnap.Position.ToGodot().DistanceTo(unit.GlobalPosition) > 0.05f;
							bool rotChanged = Mathf.Abs(baseSnap.RotationY - unit.GlobalRotation.Y) > 0.05f;
							bool hpChanged = Mathf.Abs(baseSnap.CurrentHp - currentSnap.CurrentHp) > 0.1f;
							bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
							changed = posChanged || rotChanged || hpChanged || deadChanged;
						}
						else
						{
							bool posChanged = baseSnap.Position.ToGodot().DistanceTo(unit.GlobalPosition) > 1.0f;
							bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
							changed = posChanged || deadChanged;
							currentSnap.RotationY = 0f;
							currentSnap.CurrentHp = 0f;
							currentSnap.MaxHp = 0f;
							currentSnap.Velocity = new NetworkVector3(0f, 0f, 0f);
						}
					}
					if (changed)
					{
						snapshotUnits.Add(currentSnap);
					}
				}
			}
			if (isBaseline)
			{
				_lastBaselineSnapshotsPerClient[peerId] = nextBaselineMap;
			}
			var worldSnapshot = new WorldSnapshot
			{
				Sequence = _snapshotSequence,
				IsBaseline = isBaseline,
				BaseSequence = isBaseline ? _snapshotSequence : (_snapshotSequence / 30) * 30,
				Units = snapshotUnits
			};
			var payload = MemoryPackSerializer.Serialize(worldSnapshot);
			RpcId(peerId, nameof(ReceiveSnapshot), payload);
		}
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				GD.Print($"[SERVER_STATE] Unit={unit.Entity.Id} Pos={unit.GlobalPosition}");
			}
		}
	}

	private void ApplyWorldSnapshot(WorldSnapshot snapshot)
	{
		foreach (var snap in snapshot.Units)
		{
			if (_serverToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
			{
				if (EcsWorld.IsAlive(localEntity))
				{
					if (snap.IsDead)
					{
						if (!EcsWorld.Has<Dead>(localEntity))
						{
							EcsWorld.Add<Dead>(localEntity);
							var unit3D = EcsWorld.Get<Unit3D>(localEntity);
							CallDeferred("KillUnitDeferred", unit3D);
						}
						continue;
					}
					if (EcsWorld.Has<Health>(localEntity))
					{
						var hp = EcsWorld.Get<Health>(localEntity);
						hp.Current = snap.CurrentHp;
						hp.Max = snap.MaxHp;
						EcsWorld.Set(localEntity, hp);
					}
					var target = new InterpolationTarget
					{
						Position = snap.Position.ToNumerics(),
						Velocity = snap.Velocity.ToNumerics(),
						RotationY = snap.RotationY
					};
					var unit = EcsWorld.Get<Unit3D>(localEntity);
					Vector3 localPosBefore = Vector3.Zero;
					if (GodotObject.IsInstanceValid(unit))
					{
						localPosBefore = unit.GlobalPosition;
					}
					if (EcsWorld.Has<InterpolationTarget>(localEntity))
					{
						EcsWorld.Set(localEntity, target);
					}
					else
					{
						EcsWorld.Add(localEntity, target);
					}
					if (GodotObject.IsInstanceValid(unit))
					{
						GD.Print($"[CLIENT_SNAPSHOT_APPLIED] Sequence={snapshot.Sequence} Unit={snap.EntityId} ServerPos={snap.Position.ToGodot()} LocalPosBefore={localPosBefore}");
					}
				}
			}
			else
			{
				if (!snap.IsDead)
				{
					SpawnUnitFromSnapshot(snap);
				}
			}
		}
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		if (!UnitRegistry.TryGetValue(snap.UnitId, out var meta)) return;
		Entity ownerPlayerEntity = _playerEntity;
		if (_peerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		bool isEnemy = ownerPlayerEntity != _playerEntity;
		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(snap.UnitId));
		EcsWorld.Add(entity, new Name(meta.Name));
		EcsWorld.Add(entity, new Position(snap.Position.ToNumerics()));
		EcsWorld.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(EcsWorld)));
		EcsWorld.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0 || snap.UnitId == "priest")
		{
			EcsWorld.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}
		EcsWorld.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			EcsWorld.Add(entity, new Building());
		}
		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		EcsWorld.Add(entity, target);

		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(snap.UnitId, snap.IsBuilding);
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, isEnemy);
		_serverToClientEntityMap[snap.EntityId] = entity;
		_clientToServerEntityMap[entity.Id] = snap.EntityId;
	}

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		return _serverToClientEntityMap.TryGetValue(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone)
	{
		_goldBackup = gold;
		_woodBackup = wood;
		_stoneBackup = stone;
	}

	public void KillUnitDeferredExternal(Unit3D unit)
	{
		CallDeferred("KillUnitDeferred", unit);
	}

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

		EcsWorld?.Dispose();
		EcsWorld = World.Create();
		_serverToClientEntityMap.Clear();
		_clientToServerEntityMap.Clear();
		_peerIdToPlayerEntityMap.Clear();

		if (ReplayPlaybackManager.Instance.Header.Players != null)
		{
			foreach (var p in ReplayPlaybackManager.Instance.Header.Players)
			{
				var playerEntity = EcsWorld.Create();
				EcsWorld.Add(playerEntity, new Player());
				EcsWorld.Add(playerEntity, new Name(p.Name));
				_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
				if (p.PeerId == 1)
				{
					_playerEntity = playerEntity;
				}
				else if (p.PeerId == -1)
				{
					_enemyPlayerEntity = playerEntity;
				}
			}
		}
	}

	public void SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		if (!UnitRegistry.TryGetValue(snap.UnitId, out var meta)) return;
		Entity ownerPlayerEntity = _playerEntity;
		if (_peerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		bool isEnemy = ownerPlayerEntity != _playerEntity;
		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(snap.UnitId));
		EcsWorld.Add(entity, new Name(meta.Name));
		EcsWorld.Add(entity, new Position(snap.Position.ToNumerics()));
		EcsWorld.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(EcsWorld)));
		EcsWorld.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0 || snap.UnitId == "priest")
		{
			EcsWorld.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}
		EcsWorld.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			EcsWorld.Add(entity, new Building());
		}
		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		EcsWorld.Add(entity, target);

		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(snap.UnitId, snap.IsBuilding);
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, isEnemy);
		_serverToClientEntityMap[snap.EntityId] = entity;
		_clientToServerEntityMap[entity.Id] = snap.EntityId;
	}

	private void RecordGameplayTick()
	{
		if (_replayRecorder == null) return;

		bool isKeyframe = (_replayTickCounter % 600 == 0);
		List<ReplayUnitSnapshot> unitsToRecord = ReplayObjectPool.RentList();
		List<int> activeIds = ReplayObjectPool.RentIntList();

		if (isKeyframe)
		{
			_lastRecordedUnits.Clear();
		}

		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;

			int entityId = unit.Entity.Id;
			string unitId = unit.UnitId;
			int ownerPlayerEntityId = GetOwnerPeerId(unit.Entity);
			Vector3 pos = unit.GlobalPosition;
			float rotY = unit.GlobalRotation.Y;
			float currentHp = EcsWorld.Has<Realm.Ecs.Components.Core.Health>(unit.Entity) ? EcsWorld.Get<Realm.Ecs.Components.Core.Health>(unit.Entity).Current : 0f;
			float maxHp = EcsWorld.Has<Realm.Ecs.Components.Core.Health>(unit.Entity) ? EcsWorld.Get<Realm.Ecs.Components.Core.Health>(unit.Entity).Max : 0f;
			bool isDead = EcsWorld.Has<Realm.Ecs.Components.Tags.Dead>(unit.Entity);
			bool isBuilding = unit.IsBuilding;
			Vector3 vel = unit.Velocity;

			activeIds.Add(entityId);

			if (isKeyframe)
			{
				var snap = new ReplayUnitSnapshot
				{
					EntityId = entityId,
					UnitId = unitId,
					OwnerPlayerEntityId = ownerPlayerEntityId,
					Position = new NetworkVector3(pos),
					RotationY = rotY,
					CurrentHp = currentHp,
					MaxHp = maxHp,
					IsDead = isDead,
					IsBuilding = isBuilding,
					Velocity = new NetworkVector3(vel)
				};
				unitsToRecord.Add(snap);
				_lastRecordedUnits[entityId] = snap;
			}
			else
			{
				if (_lastRecordedUnits.TryGetValue(entityId, out var last))
				{
					bool changed = last.UnitId != unitId ||
								   last.OwnerPlayerEntityId != ownerPlayerEntityId ||
								   last.Position.X != pos.X ||
								   last.Position.Y != pos.Y ||
								   last.Position.Z != pos.Z ||
								   last.RotationY != rotY ||
								   last.CurrentHp != currentHp ||
								   last.MaxHp != maxHp ||
								   last.IsDead != isDead ||
								   last.IsBuilding != isBuilding ||
								   last.Velocity.X != vel.X ||
								   last.Velocity.Y != vel.Y ||
								   last.Velocity.Z != vel.Z;

					if (changed)
					{
						var snap = new ReplayUnitSnapshot
						{
							EntityId = entityId,
							UnitId = unitId,
							OwnerPlayerEntityId = ownerPlayerEntityId,
							Position = new NetworkVector3(pos),
							RotationY = rotY,
							CurrentHp = currentHp,
							MaxHp = maxHp,
							IsDead = isDead,
							IsBuilding = isBuilding,
							Velocity = new NetworkVector3(vel)
						};
						unitsToRecord.Add(snap);
						_lastRecordedUnits[entityId] = snap;
					}
				}
				else
				{
					var snap = new ReplayUnitSnapshot
					{
						EntityId = entityId,
						UnitId = unitId,
						OwnerPlayerEntityId = ownerPlayerEntityId,
						Position = new NetworkVector3(pos),
						RotationY = rotY,
						CurrentHp = currentHp,
						MaxHp = maxHp,
						IsDead = isDead,
						IsBuilding = isBuilding,
						Velocity = new NetworkVector3(vel)
					};
					unitsToRecord.Add(snap);
					_lastRecordedUnits[entityId] = snap;
				}
			}
		}

		if (!isKeyframe)
		{
			List<int> destroyedIds = ReplayObjectPool.RentIntList();
			foreach (var pair in _lastRecordedUnits)
			{
				if (!activeIds.Contains(pair.Key))
				{
					destroyedIds.Add(pair.Key);
				}
			}

			foreach (int id in destroyedIds)
			{
				var deadSnap = _lastRecordedUnits[id];
				var deadEventSnap = new ReplayUnitSnapshot
				{
					EntityId = deadSnap.EntityId,
					UnitId = deadSnap.UnitId,
					OwnerPlayerEntityId = deadSnap.OwnerPlayerEntityId,
					Position = deadSnap.Position,
					RotationY = deadSnap.RotationY,
					CurrentHp = 0f,
					MaxHp = deadSnap.MaxHp,
					IsDead = true,
					IsBuilding = deadSnap.IsBuilding,
					Velocity = default
				};
				unitsToRecord.Add(deadEventSnap);
				_lastRecordedUnits.Remove(id);
			}
			ReplayObjectPool.ReturnIntList(destroyedIds);
		}

		float gold = InGameHUD.Instance != null ? InGameHUD.Instance.Gold : _goldBackup;
		float wood = InGameHUD.Instance != null ? InGameHUD.Instance.Wood : _woodBackup;
		float stone = InGameHUD.Instance != null ? InGameHUD.Instance.Stone : _stoneBackup;

		_replayRecorder.RecordTick(_replayTickCounter, unitsToRecord, gold, wood, stone, isKeyframe);

		ReplayObjectPool.ReturnList(unitsToRecord);
		ReplayObjectPool.ReturnIntList(activeIds);

		_replayTickCounter++;
	}
}

/// <summary>Network-serializable representation of a 3D vector.</summary>
[MemoryPackable]
public partial struct NetworkVector3
{
	public float X;
	public float Y;
	public float Z;

	public NetworkVector3(float x, float y, float z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public NetworkVector3(Vector3 v)
	{
		X = v.X;
		Y = v.Y;
		Z = v.Z;
	}

	public Vector3 ToGodot() => new Vector3(X, Y, Z);
	public System.Numerics.Vector3 ToNumerics() => new System.Numerics.Vector3(X, Y, Z);
}

/// <summary>Stores the network interpolation target state for non-local units.</summary>
public struct InterpolationTarget
{
	public System.Numerics.Vector3 Position;
	public System.Numerics.Vector3 Velocity;
	public float RotationY;
}

/// <summary>Command sent from a client to the server representing a player action.</summary>
[MemoryPackable]
public partial struct NetworkCommand
{
	public int CommandId;
	public string CommandType;
	public List<int> UnitEntityIds;
	public NetworkVector3 TargetPosition;
	public int TargetEntityId;
	public string ArgString;
}

/// <summary>Replicated state of a single unit or building.</summary>
[MemoryPackable]
public partial struct UnitSnapshot
{
	public int EntityId;
	public string UnitId;
	public int OwnerPlayerEntityId;
	public NetworkVector3 Position;
	public float RotationY;
	public float CurrentHp;
	public float MaxHp;
	public bool IsDead;
	public bool IsBuilding;
	public bool IsDetailed;
	public NetworkVector3 Velocity;
}

/// <summary>Replicated state of the entire world simulation for a specific tick.</summary>
[MemoryPackable]
public partial struct WorldSnapshot
{
	public int Sequence;
	public bool IsBaseline;
	public int BaseSequence;
	public List<UnitSnapshot> Units;
}
