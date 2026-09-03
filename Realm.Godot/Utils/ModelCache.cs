using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Godot.Utils
{
	public static class ModelCache
	{
		private static readonly Dictionary<string, PackedScene> _cachedScenes = new(StringComparer.OrdinalIgnoreCase);

		static ModelCache()
		{
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.RegisterExtension();
		}

		public static string ResolveModelPath(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (System.IO.File.Exists(modelPath))
			{
				return modelPath;
			}

			if (modelPath.StartsWith("res://") || modelPath.StartsWith("user://"))
			{
				string globalized = ProjectSettings.GlobalizePath(modelPath);
				if (System.IO.File.Exists(globalized))
				{
					return globalized;
				}
				if (ResourceLoader.Exists(modelPath))
				{
					return modelPath;
				}
			}

			string cleanPath = modelPath.TrimStart('/', '\\');

			string tempWs = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			if (!string.IsNullOrEmpty(tempWs))
			{
				string candTemp = System.IO.Path.Combine(tempWs, cleanPath);
				if (System.IO.File.Exists(candTemp))
				{
					return candTemp;
				}
			}

			string activeMap = GameHost.Instance?.ActiveMapName ?? LobbyManager.Instance?.ActiveMapName;
			if (!string.IsNullOrEmpty(activeMap))
			{
				if (System.IO.Directory.Exists(activeMap))
				{
					string candDirect = System.IO.Path.Combine(activeMap, cleanPath);
					if (System.IO.File.Exists(candDirect))
					{
						return candDirect;
					}
				}
				string mapDir = ProjectSettings.GlobalizePath($"user://maps/{activeMap}");
				string candMap = System.IO.Path.Combine(mapDir, cleanPath);
				if (System.IO.File.Exists(candMap))
				{
					return candMap;
				}
			}

			string currentMapDir = GameHost.Instance?.CurrentMapDirectory;
			if (!string.IsNullOrEmpty(currentMapDir) && System.IO.Directory.Exists(currentMapDir))
			{
				string candCur = System.IO.Path.Combine(currentMapDir, cleanPath);
				if (System.IO.File.Exists(candCur))
				{
					return candCur;
				}
			}

			string resDir = ProjectSettings.GlobalizePath("res://");
			string candRes = System.IO.Path.Combine(resDir, cleanPath);
			if (System.IO.File.Exists(candRes))
			{
				return candRes;
			}
			string godotResPath = "res://" + cleanPath.Replace("\\", "/");
			if (ResourceLoader.Exists(godotResPath))
			{
				return godotResPath;
			}

			string userDir = ProjectSettings.GlobalizePath("user://");
			string candUser = System.IO.Path.Combine(userDir, cleanPath);
			if (System.IO.File.Exists(candUser))
			{
				return candUser;
			}

			return modelPath;
		}

		public static Node GetModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (_cachedScenes.TryGetValue(modelPath, out var cachedScene) && GodotObject.IsInstanceValid(cachedScene))
			{
				return cachedScene.Instantiate();
			}

			string resolvedPath = ResolveModelPath(modelPath);
			if (!string.IsNullOrEmpty(resolvedPath) && _cachedScenes.TryGetValue(resolvedPath, out var cachedSceneResolved) && GodotObject.IsInstanceValid(cachedSceneResolved))
			{
				return cachedSceneResolved.Instantiate();
			}

			PackedScene scene = LoadPackedScene(resolvedPath ?? modelPath);
			if (scene != null)
			{
				_cachedScenes[modelPath] = scene;
				if (!string.IsNullOrEmpty(resolvedPath))
				{
					_cachedScenes[resolvedPath] = scene;
				}
				return scene.Instantiate();
			}

			return null;
		}

		private static PackedScene LoadPackedScene(string modelPath)
		{
			try
			{
				string targetPath = modelPath;
				if (targetPath.StartsWith("res://") || targetPath.StartsWith("user://"))
				{
					if (ResourceLoader.Exists(targetPath))
					{
						return GD.Load<PackedScene>(targetPath);
					}
					targetPath = ProjectSettings.GlobalizePath(targetPath);
				}

				if (System.IO.File.Exists(targetPath))
				{
					var doc = new GltfDocument();
					var state = new GltfState();
					var err = doc.AppendFromFile(targetPath, state);
					if (err == Error.Ok)
					{
						Node generatedNode = doc.GenerateScene(state);
						if (generatedNode != null)
						{
							Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.ProcessImportedScene(state, generatedNode);
							SetOwnerRecursive(generatedNode, generatedNode);
							var packedScene = new PackedScene();
							Error packErr = packedScene.Pack(generatedNode);
							generatedNode.Free();
							if (packErr == Error.Ok)
							{
								return packedScene;
							}
						}
					}
				}
				else if (ResourceLoader.Exists(targetPath))
				{
					return GD.Load<PackedScene>(targetPath);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ModelCache error loading '{modelPath}': {ex.Message}");
			}
			return null;
		}

		private static void SetOwnerRecursive(Node node, Node owner)
		{
			int childCount = node.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				Node child = node.GetChild(i);
				child.Owner = owner;
				SetOwnerRecursive(child, owner);
			}
		}

		public static (float MinY, float YOffset) CalculateModelBounds(string modelPath, float scale = 1.0f)
		{
			if (string.IsNullOrEmpty(modelPath)) return (0f, 0f);

			try
			{
				string resolved = ResolveModelPath(modelPath);
				Node node = null;
				if (!string.IsNullOrEmpty(resolved) && System.IO.File.Exists(resolved))
				{
					var doc = new GltfDocument();
					var state = new GltfState();
					var err = doc.AppendFromFile(resolved, state);
					if (err == Error.Ok)
					{
						node = doc.GenerateScene(state);
					}
				}

				if (node == null)
				{
					node = GetModel(modelPath);
				}

				if (node != null)
				{
					float minY = Unit3D.GetMinY(node, Transform3D.Identity);
					node.Free();

					if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f)
					{
						float yOffset = (float)Math.Round(-minY * scale, 4);
						return (minY, yOffset);
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ModelCache] CalculateModelBounds error for '{modelPath}': {ex.Message}");
			}

			return (0f, 0f);
		}

		public static void Clear()
		{
			_cachedScenes.Clear();
		}
	}
}
