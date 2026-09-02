using Godot;
using System;
using System.Collections.Generic;
using System.IO;

public partial class GlbThumbnailRenderer : Node
{
	private static GlbThumbnailRenderer? _instance;
	public static GlbThumbnailRenderer Instance
	{
		get
		{
			if (_instance == null || !GodotObject.IsInstanceValid(_instance))
			{
				_instance = new GlbThumbnailRenderer();
				EnsureInTree(_instance);
			}
			else if (!_instance.IsInsideTree())
			{
				EnsureInTree(_instance);
			}
			return _instance;
		}
	}

	public static void EnsureInTree(GlbThumbnailRenderer inst)
	{
		if (inst.IsInsideTree()) return;

		if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
		{
			try
			{
				if (System.Threading.Thread.CurrentThread.ManagedThreadId == 1)
				{
					if (!inst.IsInsideTree())
					{
						tree.Root.AddChild(inst);
					}
				}
				else
				{
					tree.Root.CallDeferred(Node.MethodName.AddChild, inst);
				}
			}
			catch { }
		}
	}

	public event Action<string, Texture2D>? ThumbnailGenerated;

	private class GlbRequest
	{
		public string FilePath { get; set; } = string.Empty;
		public DateTime LastModifiedUtc { get; set; }
		public string CacheKey { get; set; } = string.Empty;
		public Action<string, Texture2D>? Callback { get; set; }
	}

	private SubViewport _subViewport;
	private Node3D _modelContainer;
	private Camera3D _camera;
	private DirectionalLight3D _keyLight;
	private DirectionalLight3D _fillLight;

	private readonly Queue<GlbRequest> _requestQueue = new();
	private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);

	private GlbRequest? _currentRequest;
	private int _framesRemainingForCapture;

	public GlbThumbnailRenderer()
	{
		Name = "GlbThumbnailRenderer";
		SetupViewport();
	}

	public override void _Ready()
	{
		SetupViewport();
	}

	private void SetupViewport()
	{
		if (_subViewport != null) return;

		_subViewport = new SubViewport();
		_subViewport.Size = new Vector2I(128, 128);
		_subViewport.TransparentBg = false;
		_subViewport.OwnWorld3D = true;
		_subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		var world = new World3D();
		var env = new Godot.Environment();
		env.BackgroundMode = Godot.Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.10f, 0.11f, 0.14f, 1.0f);
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.40f, 0.42f, 0.48f);
		env.AmbientLightEnergy = 1.2f;
		world.Environment = env;
		_subViewport.World3D = world;

		_modelContainer = new Node3D();
		_modelContainer.Name = "ModelContainer";
		_subViewport.AddChild(_modelContainer);

		_keyLight = new DirectionalLight3D();
		_keyLight.LightColor = new Color(1.0f, 0.96f, 0.90f);
		_keyLight.LightEnergy = 1.5f;
		_keyLight.RotationDegrees = new Vector3(-35, 45, 0);
		_subViewport.AddChild(_keyLight);

		_fillLight = new DirectionalLight3D();
		_fillLight.LightColor = new Color(0.55f, 0.70f, 0.95f);
		_fillLight.LightEnergy = 0.9f;
		_fillLight.RotationDegrees = new Vector3(25, -135, 0);
		_subViewport.AddChild(_fillLight);

		_camera = new Camera3D();
		_camera.Current = true;
		_camera.Fov = 35.0f;
		_camera.Near = 0.01f;
		_camera.Far = 500.0f;
		_subViewport.AddChild(_camera);

		AddChild(_subViewport);
	}

	public static string NormalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return string.Empty;
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	public bool TryGetDiskCached(string glbPath, DateTime lastModifiedUtc, out Texture2D? texture)
	{
		texture = null;
		string normPath = NormalizePath(glbPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return false;

		string cacheDirectory = ProjectSettings.GlobalizePath("user://model_thumb_cache");
		string fileName = Path.GetFileNameWithoutExtension(normPath);
		string pathHash = Math.Abs(normPath.ToLowerInvariant().GetHashCode()).ToString("X8");
		string cacheKey = $"{SanitizeFileName(fileName)}_{pathHash}_{lastModifiedUtc.Ticks}";
		string cachedPngPath = Path.Combine(cacheDirectory, $"{cacheKey}.png");

		if (File.Exists(cachedPngPath))
		{
			try
			{
				var img = Image.LoadFromFile(cachedPngPath);
				if (img != null)
				{
					texture = ImageTexture.CreateFromImage(img);
					return true;
				}
			}
			catch { }
		}

		string legacyPngPath = Path.Combine(cacheDirectory, $"{SanitizeFileName(fileName)}_{lastModifiedUtc.Ticks}.png");
		if (File.Exists(legacyPngPath))
		{
			try
			{
				var img = Image.LoadFromFile(legacyPngPath);
				if (img != null)
				{
					texture = ImageTexture.CreateFromImage(img);
					return true;
				}
			}
			catch { }
		}

		return false;
	}

	public void EnqueueRequest(string glbPath, DateTime lastModifiedUtc, Action<string, Texture2D>? callback = null)
	{
		string normPath = NormalizePath(glbPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return;

		EnsureInTree(this);

		lock (_requestQueue)
		{
			if (_pendingPaths.Contains(normPath)) return;
			_pendingPaths.Add(normPath);

			string fileName = Path.GetFileNameWithoutExtension(normPath);
			string pathHash = Math.Abs(normPath.ToLowerInvariant().GetHashCode()).ToString("X8");
			string cacheKey = $"{SanitizeFileName(fileName)}_{pathHash}_{lastModifiedUtc.Ticks}";

			_requestQueue.Enqueue(new GlbRequest
			{
				FilePath = normPath,
				LastModifiedUtc = lastModifiedUtc,
				CacheKey = cacheKey,
				Callback = callback
			});
		}
	}

	public override void _Process(double delta)
	{
		if (_currentRequest != null)
		{
			_framesRemainingForCapture--;
			if (_framesRemainingForCapture <= 0)
			{
				CaptureCurrentThumbnail();
			}
			return;
		}

		lock (_requestQueue)
		{
			if (_requestQueue.Count > 0)
			{
				_currentRequest = _requestQueue.Dequeue();
				LoadGlbForCapture(_currentRequest);
			}
		}
	}

	private void LoadGlbForCapture(GlbRequest request)
	{
		SetupViewport();

		foreach (var child in _modelContainer.GetChildren())
		{
			_modelContainer.RemoveChild(child);
			child.QueueFree();
		}

		if (!File.Exists(request.FilePath))
		{
			_pendingPaths.Remove(request.FilePath);
			_currentRequest = null;
			return;
		}

		try
		{
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.RegisterExtension();

			var doc = new GltfDocument();
			var state = new GltfState();
			var err = doc.AppendFromFile(request.FilePath, state);
			if (err != Error.Ok)
			{
				_pendingPaths.Remove(request.FilePath);
				_currentRequest = null;
				return;
			}

			var scene = doc.GenerateScene(state);
			if (scene == null)
			{
				_pendingPaths.Remove(request.FilePath);
				_currentRequest = null;
				return;
			}

			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.ProcessImportedScene(state, scene);
			StopAnimations(scene);

			_modelContainer.AddChild(scene);

			Aabb aabb = CalculateVisualAabb(scene);
			if (aabb.Size.LengthSquared() < 0.0001f)
			{
				aabb = new Aabb(new Vector3(-0.5f, 0, -0.5f), new Vector3(1f, 1f, 1f));
			}

			Vector3 center = aabb.Position + aabb.Size * 0.5f;
			float maxDim = MathF.Max(aabb.Size.X, MathF.Max(aabb.Size.Y, aabb.Size.Z));
			if (maxDim < 0.01f) maxDim = 1.0f;

			float fovRad = Mathf.DegToRad(_camera.Fov * 0.5f);
			float dist = (maxDim * 0.85f) / MathF.Tan(fovRad);
			Vector3 camDir = new Vector3(1.2f, 0.8f, 1.4f).Normalized();
			_camera.Position = center + camDir * dist;
			_camera.LookAt(center, Vector3.Up);
			_camera.Current = true;

			_framesRemainingForCapture = 3;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GlbThumbnailRenderer] Error preparing {request.FilePath}: {ex.Message}");
			_pendingPaths.Remove(request.FilePath);
			_currentRequest = null;
		}
	}

	private void CaptureCurrentThumbnail()
	{
		if (_currentRequest == null) return;

		try
		{
			var tex = _subViewport.GetTexture();
			if (tex != null)
			{
				var img = tex.GetImage();
				if (img != null && !img.IsEmpty())
				{
					string cacheDirectory = ProjectSettings.GlobalizePath("user://model_thumb_cache");
					if (!Directory.Exists(cacheDirectory)) Directory.CreateDirectory(cacheDirectory);
					string cachedPngPath = Path.Combine(cacheDirectory, $"{_currentRequest.CacheKey}.png");
					img.SavePng(cachedPngPath);

					var imgTex = ImageTexture.CreateFromImage(img);
					_currentRequest.Callback?.Invoke(_currentRequest.FilePath, imgTex);
					ThumbnailGenerated?.Invoke(_currentRequest.FilePath, imgTex);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GlbThumbnailRenderer] Capture failed: {ex.Message}");
		}
		finally
		{
			foreach (var child in _modelContainer.GetChildren())
			{
				_modelContainer.RemoveChild(child);
				child.QueueFree();
			}
			_pendingPaths.Remove(_currentRequest.FilePath);
			_currentRequest = null;
		}
	}

	private static void StopAnimations(Node node)
	{
		if (node is AnimationPlayer ap)
		{
			ap.Stop();
			ap.ProcessMode = ProcessModeEnum.Disabled;
		}
		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			StopAnimations(node.GetChild(i));
		}
	}

	private static Aabb CalculateVisualAabb(Node node)
	{
		Aabb totalAabb = new Aabb();
		bool first = true;

		void Traverse(Node current, Transform3D currentTransform)
		{
			if (current is VisualInstance3D visual)
			{
				Aabb localAabb = visual.GetAabb();
				if (localAabb.Size.LengthSquared() > 0.00001f)
				{
					Aabb transformedAabb = currentTransform * localAabb;
					if (first)
					{
						totalAabb = transformedAabb;
						first = false;
					}
					else
					{
						totalAabb = totalAabb.Merge(transformedAabb);
					}
				}
			}

			int childCount = current.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				var child = current.GetChild(i);
				if (child is Node3D child3D)
				{
					Traverse(child, currentTransform * child3D.Transform);
				}
				else
				{
					Traverse(child, currentTransform);
				}
			}
		}

		if (node is Node3D root3D)
		{
			Traverse(root3D, root3D.Transform);
		}
		else
		{
			Traverse(node, Transform3D.Identity);
		}

		return totalAabb;
	}

	private static string SanitizeFileName(string name)
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		var chars = name.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (Array.IndexOf(invalidChars, chars[i]) >= 0)
			{
				chars[i] = '_';
			}
		}
		return new string(chars);
	}
}
