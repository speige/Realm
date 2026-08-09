using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Godot.Utils
{
	public static class ModelCache
	{
		private static readonly Dictionary<string, PackedScene> _cachedScenes = new(StringComparer.OrdinalIgnoreCase);

		public static Node GetModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (_cachedScenes.TryGetValue(modelPath, out var cachedScene) && GodotObject.IsInstanceValid(cachedScene))
			{
				return cachedScene.Instantiate();
			}

			PackedScene scene = LoadPackedScene(modelPath);
			if (scene != null)
			{
				_cachedScenes[modelPath] = scene;
				return scene.Instantiate();
			}

			return null;
		}

		private static PackedScene LoadPackedScene(string modelPath)
		{
			try
			{
				if (System.IO.File.Exists(modelPath) && !modelPath.StartsWith("res://"))
				{
					var doc = new GltfDocument();
					var state = new GltfState();
					var err = doc.AppendFromFile(modelPath, state);
					if (err == Error.Ok)
					{
						Node generatedNode = doc.GenerateScene(state);
						if (generatedNode != null)
						{
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
				else if (ResourceLoader.Exists(modelPath))
				{
					return GD.Load<PackedScene>(modelPath);
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

		public static void Clear()
		{
			_cachedScenes.Clear();
		}
	}
}
