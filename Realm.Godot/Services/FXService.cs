using Godot;
using Realm.Ecs.Services;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

public class FXService
{
	public FXService(WorldAccessor ecsWorldAccessor)
	{
	}
	public void SpawnFireblastEffect(Node3D parent, Vector3 position)
	{
		SpawnSpritesheetEffect(parent, "Assets/vfx/solar_flare_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.05f, 6f);
	}

	public void SpawnLightningEffect(Node3D parent, Vector3 position)
	{
		SpawnSpritesheetEffect(parent, "Assets/vfx/arcane_surge_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.035f, 6f);
	}

	public void SpawnSpritesheetEffect(Node3D parent, string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		Texture2D? texture = null;
		string fullPath = texturePath;
		if (!texturePath.StartsWith("res://") && !System.IO.File.Exists(texturePath))
		{
			string wsPath = ProjectSettings.GlobalizePath("user://temp_map_workspace");
			fullPath = System.IO.Path.Combine(wsPath, texturePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
			if (!System.IO.File.Exists(fullPath))
			{
				fullPath = System.IO.Path.Combine(wsPath, "Assets", "vfx", System.IO.Path.GetFileName(texturePath));
			}
		}

		if (fullPath.StartsWith("res://"))
		{
			texture = GD.Load<Texture2D>(fullPath);
		}
		else if (System.IO.File.Exists(fullPath))
		{
			var img = Image.LoadFromFile(fullPath);
			if (img != null)
			{
				texture = ImageTexture.CreateFromImage(img);
			}
		}

		if (texture == null) return;

		int totalFrames = columns * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.None);
		frames.SetAnimationSpeed("play", 1.0f / secondsPerFrame);

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
		parent.AddChild(sprite);
		sprite.Play("play");
		sprite.AnimationFinished += sprite.QueueFree;
	}

	public void FlashDamageUnit(Unit3D unit)
	{
		var tween = unit.CreateTween();
		unit.Scale = new Vector3(0.8f, 1.3f, 0.8f);
		tween.TweenProperty(unit, "scale", new Vector3(1.0f, 1.0f, 1.0f), 0.25f);
	}

	public void SpawnHealVisualEffect(Node3D parent, Vector3 start, Vector3 target)
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

		parent.AddChild(orb);

		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);

		var tween = parent.CreateTween();
		float travelTime = Mathf.Clamp(start.DistanceTo(target) / 25f, 0.2f, 0.8f);
		tween.TweenProperty(orb, "global_position", targetPos, travelTime);
		tween.Chain().TweenCallback(Callable.From(orb.QueueFree));
	}

	public void FlashHealUnit(Unit3D unit)
	{
		var tween = unit.CreateTween();
		unit.Scale = new Vector3(0.9f, 1.25f, 0.9f);
		tween.TweenProperty(unit, "scale", new Vector3(1.0f, 1.0f, 1.0f), 0.25f);
	}

	public void SpawnHolyLightEffect(Node3D parent, Vector3 position)
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

		parent.AddChild(cylinder);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(cylinder, "scale:x", 0.05f, 0.6f);
		tween.TweenProperty(cylinder, "scale:z", 0.05f, 0.6f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.6f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.6f);
		tween.Chain().TweenCallback(Callable.From(cylinder.QueueFree));
	}

	public void SpawnPing3DEffect(Node3D parent, Vector3 position)
	{
		var meshInstance = new MeshInstance3D();
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		meshInstance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
		var torusMesh = new TorusMesh();
		torusMesh.InnerRadius = 2.0f;
		torusMesh.OuterRadius = 2.4f;
		meshInstance.Mesh = torusMesh;
		meshInstance.Position = position + new Vector3(0, 0.1f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(1.0f, 0.1f, 0.1f, 0.8f);
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.DisableReceiveShadows = true;
		material.EmissionEnabled = true;
		material.Emission = new Color(1.0f, 0.1f, 0.1f);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		meshInstance.MaterialOverride = material;

		parent.AddChild(meshInstance);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(meshInstance, "scale", new Vector3(4f, 0.1f, 4f), 0.8f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.8f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.8f);
		tween.Chain().TweenCallback(Callable.From(meshInstance.QueueFree));
	}

	public void SpawnArrowProjectile(Node3D parent, Vector3 start, Vector3 target)
	{
		var arrow = new MeshInstance3D();
		var cylinderMesh = new CylinderMesh();
		cylinderMesh.TopRadius = 0.22f;
		cylinderMesh.BottomRadius = 0.22f;
		cylinderMesh.Height = 1.0f;
		arrow.Mesh = cylinderMesh;
		arrow.Position = start + new Vector3(0, 1.2f, 0); 

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(1.0f, 0.75f, 0.3f);
		material.EmissionEnabled = true;
		material.Emission = new Color(3.0f, 1.5f, 0.4f);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		arrow.MaterialOverride = material;

		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);
		Vector3 shotDir = targetPos - arrow.Position;
		if (shotDir.LengthSquared() > 0.01f)
		{
			Vector3 flatDir = new Vector3(shotDir.X, 0f, shotDir.Z);
			if (flatDir.LengthSquared() > 0.0001f)
			{
				arrow.LookAtFromPosition(arrow.Position, targetPos, Vector3.Up);
			}
			else
			{
				arrow.LookAtFromPosition(arrow.Position, targetPos + new Vector3(0, 0, 0.001f), Vector3.Up);
			}
			arrow.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(90f));
		}

		var light = new OmniLight3D();
		light.LightColor = new Color(1.0f, 0.7f, 0.3f);
		light.LightEnergy = 2.0f;
		light.OmniRange = 3.0f;
		light.ShadowEnabled = false;
		arrow.AddChild(light);

		parent.AddChild(arrow);

		var tween = parent.CreateTween();
		float travelTime = Mathf.Clamp(start.DistanceTo(target) / 40f, 0.15f, 0.6f);
		tween.TweenProperty(arrow, "global_position", targetPos, travelTime);
		tween.Chain().TweenCallback(Callable.From(arrow.QueueFree));
	}

	public void SpawnDamageNumber(Node3D parent, Vector3 worldPosition, float amount)
	{
		var label = new Label3D();
		label.Text = ((int)Math.Round(amount)).ToString();
		label.Modulate = new Color(1.0f, 0.85f, 0.3f);
		label.OutlineModulate = Colors.Black;
		label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		label.Position = worldPosition + new Vector3(0, 1.6f, 0);
		label.FontSize = 40;
		parent.AddChild(label);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(label, "position", label.Position + new Vector3(0, 2.0f, 0), 1.2f);
		tween.TweenProperty(label, "modulate:a", 0.0f, 1.2f);
		tween.Chain().TweenCallback(Callable.From(label.QueueFree));
	}

	public void SpawnTargetIndicator(Node3D parent, Vector3 position, Color color)
	{
		var meshInstance = new MeshInstance3D();
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		meshInstance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
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
		material.DisableReceiveShadows = true;
		meshInstance.MaterialOverride = material;

		parent.AddChild(meshInstance);

		var tween = parent.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(meshInstance, "scale", new Vector3(1.6f, 0.1f, 1.6f), 0.35f);
		tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.35f);
		tween.TweenProperty(material, "emission:a", 0.0f, 0.35f);
		tween.Chain().TweenCallback(Callable.From(meshInstance.QueueFree));
	}

	public void AddMinimapPing(Node3D parent, List<GameHost.MinimapPing> activePings, Vector3 position)
	{
		activePings.Add(new GameHost.MinimapPing
		{
			WorldPos = position,
			LifeTime = 0f,
			MaxLifeTime = 3.0f
		});

		SpawnPing3DEffect(parent, position);

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"[ALERT PING] Signal at: {position.X:F0}, {position.Z:F0}", new Color(1.0f, 0.1f, 0.2f));
			UIManager.Instance?.PlayClickSound();
		}
	}
}
