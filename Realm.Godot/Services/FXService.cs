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
		SpawnSpritesheetEffect(parent, "res://Assets/2d/SpellSpritesheets/solar_flare_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.05f, 6f);
	}

	public void SpawnLightningEffect(Node3D parent, Vector3 position)
	{
		SpawnSpritesheetEffect(parent, "res://Assets/2d/SpellSpritesheets/arcane_surge_sheet.png", position + new Vector3(0, 0.5f, 0), 4, 4, 0.035f, 6f);
	}

	public void SpawnSpritesheetEffect(Node3D parent, string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
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
		cylinderMesh.TopRadius = 0.05f;
		cylinderMesh.BottomRadius = 0.05f;
		cylinderMesh.Height = 0.6f;
		arrow.Mesh = cylinderMesh;
		arrow.Position = start + new Vector3(0, 1.2f, 0); 

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.9f, 0.8f, 0.4f);
		material.EmissionEnabled = true;
		material.Emission = new Color(0.9f, 0.7f, 0.2f);
		arrow.MaterialOverride = material;

		Vector3 targetPos = target + new Vector3(0, 1.2f, 0);
		if (arrow.Position.DistanceTo(targetPos) > 0.1f)
		{
			arrow.LookAtFromPosition(arrow.Position, targetPos, Vector3.Up);
			arrow.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(90f));
		}

		parent.AddChild(arrow);

		var tween = parent.CreateTween();
		float travelTime = Mathf.Clamp(start.DistanceTo(target) / 40f, 0.15f, 0.6f);
		tween.TweenProperty(arrow, "global_position", targetPos, travelTime);
		tween.Chain().TweenCallback(Callable.From(arrow.QueueFree));
	}

	public void SpawnTargetIndicator(Node3D parent, Vector3 position, Color color)
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
