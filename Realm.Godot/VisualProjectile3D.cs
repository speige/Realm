using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Godot.Utils;
using System;

public partial class VisualProjectile3D : Node3D
{
	private Node3D _meshContainer;
	private MeshInstance3D _fallbackMeshInstance;
	private Node3D _customModelInstance;
	private GpuParticles3D _trailEmitter;
	private RibbonTrailMesh _ribbonMesh;
	private ParticleProcessMaterial _particleProcessMaterial;
	private StandardMaterial3D _ribbonMaterial;
	private ShaderMaterial _uberShaderMaterial;
	private static Shader _sharedUberShader;

	private GameHost.WeaponMetadata _weapon;
	private Vector3 _startPosition;
	private Vector3 _targetPosition;
	private Vector3 _initialTargetPosition;
	private Entity _targetEntity;
	private float _speed;
	private float _totalFlightDuration;
	private float _elapsedTime;
	private bool _isFlying;
	private bool _isImpacted;
	private float _fadeTimer;
	private float _fadeDuration;

	private Vector3 _tumbleAxis;
	private float _tumbleSpeed;
	private string _currentLoadedModelPath;
	private string _currentLoadedRibbonPath;

	public bool IsActive => _isFlying || _isImpacted;
	public Action<VisualProjectile3D> OnRecycleRequested;

	public override void _Ready()
	{
		_meshContainer = new Node3D();
		_meshContainer.Name = "MeshContainer";
		AddChild(_meshContainer);

		_fallbackMeshInstance = new MeshInstance3D();
		_fallbackMeshInstance.Name = "FallbackMesh";
		var sphere = new SphereMesh();
		sphere.Radius = 0.22f;
		sphere.Height = 0.44f;
		_fallbackMeshInstance.Mesh = sphere;
		_meshContainer.AddChild(_fallbackMeshInstance);

		EnsureSharedShader();
		_uberShaderMaterial = new ShaderMaterial();
		_uberShaderMaterial.Shader = _sharedUberShader;
		_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;

		SetupTrailEmitter();
		SetProcess(false);
	}

	private static void EnsureSharedShader()
	{
		if (_sharedUberShader == null || !GodotObject.IsInstanceValid(_sharedUberShader))
		{
			if (ResourceLoader.Exists("res://Assets/shaders/projectile_fx.gdshader"))
			{
				_sharedUberShader = GD.Load<Shader>("res://Assets/shaders/projectile_fx.gdshader");
			}
		}
	}

	private void SetupTrailEmitter()
	{
		_trailEmitter = new GpuParticles3D();
		_trailEmitter.Name = "RibbonTrailEmitter";
		_trailEmitter.Amount = 48;
		_trailEmitter.Lifetime = 0.5f;
		_trailEmitter.Explosiveness = 0.0f;
		_trailEmitter.Randomness = 0.0f;
		_trailEmitter.FixedFps = 60;
		_trailEmitter.FractDelta = true;
		_trailEmitter.LocalCoords = false;
		_trailEmitter.VisibilityAabb = new Aabb(new Vector3(-50, -50, -50), new Vector3(100, 100, 100));

		_particleProcessMaterial = new ParticleProcessMaterial();
		_particleProcessMaterial.ParticleFlagDisableZ = false;
		_particleProcessMaterial.Gravity = Vector3.Zero;
		_particleProcessMaterial.Spread = 0.0f;
		_particleProcessMaterial.InitialVelocityMin = 0.0f;
		_particleProcessMaterial.InitialVelocityMax = 0.0f;

		_ribbonMesh = new RibbonTrailMesh();
		_ribbonMesh.Size = 0.4f;
		_ribbonMesh.Sections = 12;
		_ribbonMesh.SectionLength = 0.35f;

		var taperCurve = new Curve();
		taperCurve.AddPoint(new Vector2(0.0f, 1.0f));
		taperCurve.AddPoint(new Vector2(1.0f, 0.0f));
		_ribbonMesh.Curve = taperCurve;

		_ribbonMaterial = new StandardMaterial3D();
		_ribbonMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		_ribbonMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
		_ribbonMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_ribbonMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_ribbonMaterial.UseParticleTrails = true;

		_ribbonMesh.Material = _ribbonMaterial;
		_trailEmitter.DrawPass1 = _ribbonMesh;
		_trailEmitter.ProcessMaterial = _particleProcessMaterial;
		_trailEmitter.Emitting = false;

		AddChild(_trailEmitter);
	}

	public void Initialize(GameHost.WeaponMetadata weapon, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D> recycleCallback = null)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		Initialize(start, target, weapon, targetEntity);
	}

	public void Initialize(string weaponId, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D> recycleCallback = null)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		if (GameHost.WeaponRegistry.TryGetValue(weaponId ?? "arrow", out var meta))
		{
			Initialize(start, target, meta, targetEntity);
		}
		else
		{
			var fallback = new GameHost.WeaponMetadata
			{
				WeaponId = weaponId ?? "arrow",
				ProjectileSpeed = 25f,
				OrientToTrajectory = true
			};
			Initialize(start, target, fallback, targetEntity);
		}
	}

	public void Initialize(Vector3 start, Vector3 target, GameHost.WeaponMetadata weapon, Entity targetEntity = default)
	{
		_weapon = weapon;
		_startPosition = start;
		_targetPosition = target;
		_initialTargetPosition = target;
		_targetEntity = targetEntity;

		_speed = weapon.ProjectileSpeed > 0 ? weapon.ProjectileSpeed : 25.0f;
		float distance = _startPosition.DistanceTo(_targetPosition);
		_totalFlightDuration = Mathf.Max(0.05f, distance / _speed);
		_elapsedTime = 0.0f;
		_isFlying = true;
		_isImpacted = false;
		_fadeTimer = 0.0f;
		_fadeDuration = weapon.RibbonLifetime > 0 ? weapon.RibbonLifetime : 0.5f;

		GlobalPosition = _startPosition;
		_meshContainer.Visible = true;
		_meshContainer.Rotation = Vector3.Zero;

		Vector3 tumble = weapon.TumbleAngularVelocity;
		if (tumble.LengthSquared() > 0.001f)
		{
			_tumbleAxis = tumble.Normalized();
			_tumbleSpeed = tumble.Length();
		}
		else
		{
			_tumbleAxis = new Vector3(1f, 0.5f, 0.2f).Normalized();
			_tumbleSpeed = 4.0f;
		}

		UpdateModel();
		UpdateShaderMaterial();
		UpdateRibbonTrail();

		Visible = true;
		_trailEmitter.Restart();
		_trailEmitter.Emitting = true;

		SetProcess(true);
	}

	private void UpdateModel()
	{
		string modelPath = _weapon.ProjectileModelPath;
		if (string.IsNullOrEmpty(modelPath))
		{
			if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
			{
				_customModelInstance.QueueFree();
				_customModelInstance = null;
			}
			_currentLoadedModelPath = null;
			_fallbackMeshInstance.Visible = true;
			_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;
			return;
		}

		if (modelPath != _currentLoadedModelPath || _customModelInstance == null || !GodotObject.IsInstanceValid(_customModelInstance))
		{
			if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
			{
				_customModelInstance.QueueFree();
				_customModelInstance = null;
			}

			_currentLoadedModelPath = modelPath;
			var loaded = ModelCache.GetModel(modelPath);
			if (loaded is Node3D node3D)
			{
				_customModelInstance = node3D;
				_meshContainer.AddChild(_customModelInstance);
				_fallbackMeshInstance.Visible = false;
				ApplyUberMaterialRecursively(_customModelInstance, _uberShaderMaterial);
			}
			else
			{
				_fallbackMeshInstance.Visible = true;
				_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;
			}
		}
		else
		{
			_fallbackMeshInstance.Visible = false;
			ApplyUberMaterialRecursively(_customModelInstance, _uberShaderMaterial);
		}
	}

	private void ApplyUberMaterialRecursively(Node node, Material material)
	{
		if (node is MeshInstance3D meshInst)
		{
			meshInst.MaterialOverride = material;
		}
		foreach (Node child in node.GetChildren())
		{
			ApplyUberMaterialRecursively(child, material);
		}
	}

	private void UpdateShaderMaterial()
	{
		EnsureSharedShader();
		if (_uberShaderMaterial.Shader == null)
		{
			_uberShaderMaterial.Shader = _sharedUberShader;
		}

		Color baseCol = ParseColor(_weapon.BaseColor, new Color(0.15f, 0.12f, 0.1f));
		Color emissiveCol = ParseColor(_weapon.EmissionColor, new Color(1.0f, 0.4f, 0.05f));
		Color fresnelCol = ParseColor(_weapon.FresnelColor, new Color(1.0f, 0.6f, 0.1f));

		_uberShaderMaterial.SetShaderParameter("base_color", baseCol);
		_uberShaderMaterial.SetShaderParameter("emission_color", emissiveCol);
		_uberShaderMaterial.SetShaderParameter("emission_energy", _weapon.EmissionEnergy > 0 ? _weapon.EmissionEnergy : 4.0f);
		_uberShaderMaterial.SetShaderParameter("fresnel_power", _weapon.FresnelPower > 0 ? _weapon.FresnelPower : 3.0f);
		_uberShaderMaterial.SetShaderParameter("fresnel_color", fresnelCol);
		_uberShaderMaterial.SetShaderParameter("fresnel_factor", _weapon.FresnelFactor > 0 ? _weapon.FresnelFactor : 1.5f);
		_uberShaderMaterial.SetShaderParameter("noise_scale", _weapon.NoiseScale > 0 ? _weapon.NoiseScale : 3.0f);
		_uberShaderMaterial.SetShaderParameter("uv_scroll_speed_1", _weapon.UvScrollSpeed1);
		_uberShaderMaterial.SetShaderParameter("uv_scroll_speed_2", _weapon.UvScrollSpeed2);
		_uberShaderMaterial.SetShaderParameter("threshold_cutoff", _weapon.ThresholdCutoff);
		_uberShaderMaterial.SetShaderParameter("threshold_smoothness", _weapon.ThresholdSmoothness > 0 ? _weapon.ThresholdSmoothness : 0.1f);

		if (!string.IsNullOrEmpty(_weapon.NoiseTexture))
		{
			var tex = LoadTextureSafe(_weapon.NoiseTexture);
			if (tex != null)
			{
				_uberShaderMaterial.SetShaderParameter("noise_texture", tex);
				_uberShaderMaterial.SetShaderParameter("use_procedural_noise", false);
			}
			else
			{
				_uberShaderMaterial.SetShaderParameter("use_procedural_noise", true);
			}
		}
		else
		{
			_uberShaderMaterial.SetShaderParameter("use_procedural_noise", true);
		}
	}

	private void UpdateRibbonTrail()
	{
		Color ribbonCol = ParseColor(_weapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f));
		float width = _weapon.RibbonWidth > 0 ? _weapon.RibbonWidth : 0.4f;
		float lifetime = _weapon.RibbonLifetime > 0 ? _weapon.RibbonLifetime : 0.5f;

		_trailEmitter.Lifetime = lifetime;
		_ribbonMesh.Size = width;

		if (_weapon.RibbonTaper)
		{
			var taperCurve = new Curve();
			taperCurve.AddPoint(new Vector2(0.0f, 1.0f));
			taperCurve.AddPoint(new Vector2(1.0f, 0.0f));
			_ribbonMesh.Curve = taperCurve;
		}
		else
		{
			_ribbonMesh.Curve = null;
		}

		_ribbonMaterial.AlbedoColor = ribbonCol;
		_ribbonMaterial.EmissionEnabled = true;
		_ribbonMaterial.Emission = ribbonCol;
		_ribbonMaterial.BlendMode = _weapon.RibbonAdditive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix;

		if (!string.IsNullOrEmpty(_weapon.RibbonTexture) && _weapon.RibbonTexture != _currentLoadedRibbonPath)
		{
			_currentLoadedRibbonPath = _weapon.RibbonTexture;
			var tex = LoadTextureSafe(_weapon.RibbonTexture);
			_ribbonMaterial.AlbedoTexture = tex;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (_isImpacted)
		{
			_fadeTimer += dt;
			if (_fadeTimer >= _fadeDuration)
			{
				SetProcess(false);
				_isImpacted = false;
				Visible = false;
				OnRecycleRequested?.Invoke(this);
			}
			return;
		}

		if (!_isFlying) return;

		_elapsedTime += dt;
		float rawT = Mathf.Clamp(_elapsedTime / _totalFlightDuration, 0.0f, 1.0f);
		float easedT = ApplyEaseCurve(rawT, _weapon.EaseCurve);

		Vector3 currentTarget = _initialTargetPosition;
		if (_targetEntity != default && GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(_targetEntity))
		{
			if (GameHost.Instance.EcsWorld.Has<Position>(_targetEntity))
			{
				var posComp = GameHost.Instance.EcsWorld.Get<Position>(_targetEntity);
				currentTarget = new Vector3(posComp.Value.X, posComp.Value.Y + 1.2f, posComp.Value.Z);
			}
		}

		Vector3 effectiveTarget = _initialTargetPosition.Lerp(currentTarget, Mathf.Clamp(_weapon.HomingWeight * easedT, 0.0f, 1.0f));
		Vector3 basePos = _startPosition.Lerp(effectiveTarget, easedT);

		float arcY = 0.0f;
		if (_weapon.MaxBounces > 0)
		{
			int segments = 1 + _weapon.MaxBounces;
			float segmentProgress = rawT * segments;
			int currentBounce = Mathf.Min((int)Mathf.Floor(segmentProgress), segments - 1);
			float localU = segmentProgress - currentBounce;
			float bounceHeight = _weapon.ArcHeight / (1.0f + currentBounce * 0.6f);
			arcY = 4.0f * bounceHeight * localU * (1.0f - localU);
		}
		else if (_weapon.ArcHeight > 0.0f)
		{
			arcY = 4.0f * _weapon.ArcHeight * rawT * (1.0f - rawT);
		}

		Vector3 direction = (effectiveTarget - _startPosition);
		Vector3 forward = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector3.Forward;
		Vector3 right = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.99f ? forward.Cross(Vector3.Right).Normalized() : forward.Cross(Vector3.Up).Normalized();
		Vector3 up = right.Cross(forward).Normalized();

		Vector3 spiralOffset = Vector3.Zero;
		if (_weapon.SpiralRadius > 0.0f && _weapon.SpiralFrequency > 0.0f)
		{
			float theta = Mathf.Tau * _weapon.SpiralFrequency * _elapsedTime;
			spiralOffset = (right * Mathf.Cos(theta) + up * Mathf.Sin(theta)) * _weapon.SpiralRadius;
		}

		Vector3 zigzagOffset = Vector3.Zero;
		if (_weapon.ZigzagAmplitude > 0.0f && _weapon.ZigzagFrequency > 0.0f)
		{
			float phi = Mathf.Tau * _weapon.ZigzagFrequency * _elapsedTime;
			zigzagOffset = right * (Mathf.Sin(phi) * _weapon.ZigzagAmplitude);
		}

		Vector3 nextPos = basePos + new Vector3(0, arcY, 0) + spiralOffset + zigzagOffset;

		if (_weapon.OrientToTrajectory)
		{
			Vector3 velocityDelta = nextPos - GlobalPosition;
			if (velocityDelta.LengthSquared() > 0.0001f)
			{
				LookAtFromPosition(nextPos, nextPos + velocityDelta.Normalized(), Vector3.Up);
			}
			else
			{
				GlobalPosition = nextPos;
			}
		}
		else
		{
			GlobalPosition = nextPos;
		}

		_meshContainer.RotateObjectLocal(_tumbleAxis, _tumbleSpeed * dt);

		if (rawT >= 1.0f)
		{
			HandleImpact(nextPos);
		}
	}

	private void HandleImpact(Vector3 impactPosition)
	{
		_isFlying = false;
		_isImpacted = true;
		_fadeTimer = 0.0f;

		_meshContainer.Visible = false;
		_trailEmitter.Emitting = false;

		if (!string.IsNullOrEmpty(_weapon.ImpactVisualEffect) && GameHost.Instance != null)
		{
			var fxService = ServiceLocator.TryGet<FXService>();
			fxService?.SpawnSpritesheetEffect(GetParent<Node3D>() ?? this, _weapon.ImpactVisualEffect, impactPosition, 4, 4, 0.04f, 4.0f);
		}

		if (!string.IsNullOrEmpty(_weapon.ImpactSound))
		{
			var audioService = ServiceLocator.TryGet<AudioService>();
			audioService?.PlaySound3D(_weapon.ImpactSound, impactPosition);
		}
	}

	private static float ApplyEaseCurve(float t, string easeCurve)
	{
		if (string.IsNullOrEmpty(easeCurve)) return t;
		switch (easeCurve.ToLowerInvariant())
		{
			case "ease_in":
				return t * t;
			case "ease_out":
				return 1.0f - (1.0f - t) * (1.0f - t);
			case "ease_in_out":
				return t < 0.5f ? 2.0f * t * t : 1.0f - Mathf.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f;
			default:
				return t;
		}
	}

	private static Color ParseColor(string hex, Color fallback)
	{
		if (string.IsNullOrEmpty(hex)) return fallback;
		try
		{
			return Color.FromHtml(hex);
		}
		catch
		{
			return fallback;
		}
	}

	private static Texture2D LoadTextureSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		if (path.StartsWith("res://") || path.StartsWith("user://"))
		{
			if (ResourceLoader.Exists(path))
			{
				return GD.Load<Texture2D>(path);
			}
		}

		string resolvedPath = ModelCache.ResolveModelPath(path);
		if (!string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath))
		{
			if (resolvedPath.EndsWith(".ktx2", StringComparison.OrdinalIgnoreCase))
			{
				var img = LoadKtx2Layer0(resolvedPath);
				if (img != null) return ImageTexture.CreateFromImage(img);
			}
			else
			{
				var img = Image.LoadFromFile(resolvedPath);
				if (img != null) return ImageTexture.CreateFromImage(img);
			}
		}

		return null;
	}

	private static Image LoadKtx2Layer0(string ktx2Path)
	{
		string globalKtx2 = ProjectSettings.GlobalizePath(ktx2Path);
		string cacheDir = ProjectSettings.GlobalizePath("user://ktx_layer_cache");
		System.IO.Directory.CreateDirectory(cacheDir);

		string baseName = System.IO.Path.GetFileNameWithoutExtension(ktx2Path);
		string tempOut = System.IO.Path.Combine(cacheDir, $"{baseName}_ribbon_l0.png");

		if (System.IO.File.Exists(globalKtx2))
		{
			DateTime ktxTime = System.IO.File.GetLastWriteTimeUtc(globalKtx2);
			if (System.IO.File.Exists(tempOut) && System.IO.File.GetLastWriteTimeUtc(tempOut) >= ktxTime)
			{
				return Image.LoadFromFile(tempOut);
			}
		}

		string ktxCmd = EditableTerrain.GetKtxCmdPath();
		try
		{
			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = ktxCmd,
				WorkingDirectory = System.IO.Path.GetDirectoryName(ktxCmd),
				Arguments = $"extract --layer 0 --level 0 --transcode rgba8 \"{globalKtx2}\" \"{tempOut}\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using var process = System.Diagnostics.Process.Start(startInfo);
			process?.WaitForExit();
			if (process != null && process.ExitCode == 0 && System.IO.File.Exists(tempOut))
			{
				return Image.LoadFromFile(tempOut);
			}
		}
		catch
		{
		}

		return null;
	}
}
