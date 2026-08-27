using Godot;
using Realm.Godot.Animation;
using System;
using System.Collections.Generic;

public static class RanimSkeletonThumbnailGenerator
{
	private const int ThumbnailSize = 128;
	private const int FrameCount = 12;
	private const float FrameRate = 6.0f;

	private struct JointHierarchyDef
	{
		public HumanoidBone Bone;
		public HumanoidBone Parent;
		public Vector3 RestOffset;
	}

	private static readonly JointHierarchyDef[] Hierarchy = new JointHierarchyDef[]
	{
		new JointHierarchyDef { Bone = HumanoidBone.Hips, Parent = HumanoidBone.Hips, RestOffset = new Vector3(0, 0.94f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.Spine, Parent = HumanoidBone.Hips, RestOffset = new Vector3(0, 0.14f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.Chest, Parent = HumanoidBone.Spine, RestOffset = new Vector3(0, 0.14f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.UpperChest, Parent = HumanoidBone.Chest, RestOffset = new Vector3(0, 0.12f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.Neck, Parent = HumanoidBone.UpperChest, RestOffset = new Vector3(0, 0.09f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.Head, Parent = HumanoidBone.Neck, RestOffset = new Vector3(0, 0.15f, 0) },

		new JointHierarchyDef { Bone = HumanoidBone.LeftShoulder, Parent = HumanoidBone.UpperChest, RestOffset = new Vector3(-0.06f, 0.04f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.LeftUpperArm, Parent = HumanoidBone.LeftShoulder, RestOffset = new Vector3(-0.13f, -0.02f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.LeftLowerArm, Parent = HumanoidBone.LeftUpperArm, RestOffset = new Vector3(-0.24f, 0, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.LeftHand, Parent = HumanoidBone.LeftLowerArm, RestOffset = new Vector3(-0.20f, 0, 0) },

		new JointHierarchyDef { Bone = HumanoidBone.RightShoulder, Parent = HumanoidBone.UpperChest, RestOffset = new Vector3(0.06f, 0.04f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.RightUpperArm, Parent = HumanoidBone.RightShoulder, RestOffset = new Vector3(0.13f, -0.02f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.RightLowerArm, Parent = HumanoidBone.RightUpperArm, RestOffset = new Vector3(0.24f, 0, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.RightHand, Parent = HumanoidBone.RightLowerArm, RestOffset = new Vector3(0.20f, 0, 0) },

		new JointHierarchyDef { Bone = HumanoidBone.LeftUpperLeg, Parent = HumanoidBone.Hips, RestOffset = new Vector3(-0.10f, -0.08f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.LeftLowerLeg, Parent = HumanoidBone.LeftUpperLeg, RestOffset = new Vector3(0, -0.40f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.LeftFoot, Parent = HumanoidBone.LeftLowerLeg, RestOffset = new Vector3(0, -0.38f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.LeftToes, Parent = HumanoidBone.LeftFoot, RestOffset = new Vector3(0, -0.04f, 0.12f) },

		new JointHierarchyDef { Bone = HumanoidBone.RightUpperLeg, Parent = HumanoidBone.Hips, RestOffset = new Vector3(0.10f, -0.08f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.RightLowerLeg, Parent = HumanoidBone.RightUpperLeg, RestOffset = new Vector3(0, -0.40f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.RightFoot, Parent = HumanoidBone.RightLowerLeg, RestOffset = new Vector3(0, -0.38f, 0) },
		new JointHierarchyDef { Bone = HumanoidBone.RightToes, Parent = HumanoidBone.RightFoot, RestOffset = new Vector3(0, -0.04f, 0.12f) }
	};

	public static AnimatedThumbnail? GenerateAnimatedThumbnail(RealmAnimationData animData)
	{
		if (animData == null) return null;

		var trackMap = new Dictionary<HumanoidBone, RealmAnimationBoneTrack>();
		if (animData.Tracks != null)
		{
			foreach (var track in animData.Tracks)
			{
				if (track != null && HumanoidBoneMapper.TryMapToCanonical(track.BoneName, out var bone))
				{
					trackMap[bone] = track;
				}
			}
		}

		float duration = animData.Duration > 0f ? animData.Duration : 1.0f;
		var frames = new List<Texture2D>();

		for (int frameIdx = 0; frameIdx < FrameCount; frameIdx++)
		{
			float time = (frameIdx / (float)FrameCount) * duration;
			var frameImage = RenderSkeletonFrame(trackMap, time);
			var tex = ImageTexture.CreateFromImage(frameImage);
			frames.Add(tex);
		}

		float speedScaledDuration = duration * 2.0f;
		float effectiveFps = Math.Clamp(FrameCount / speedScaledDuration, 1.0f, 6.0f);

		return new AnimatedThumbnail
		{
			Frames = frames,
			Fps = effectiveFps
		};
	}

	private static Image RenderSkeletonFrame(Dictionary<HumanoidBone, RealmAnimationBoneTrack> trackMap, float time)
	{
		var worldPositions = new Dictionary<HumanoidBone, Vector3>();
		var worldRotations = new Dictionary<HumanoidBone, Quaternion>();

		Vector3 rootTranslation = Vector3.Zero;
		if (trackMap.TryGetValue(HumanoidBone.Hips, out var hipsTrack) && hipsTrack.PositionKeys != null && hipsTrack.PositionKeys.Length > 0)
		{
			rootTranslation = SamplePosition(hipsTrack.PositionKeys, time) - SamplePosition(hipsTrack.PositionKeys, 0f);
		}

		foreach (var joint in Hierarchy)
		{
			Quaternion localRot = Quaternion.Identity;
			if (trackMap.TryGetValue(joint.Bone, out var track) && track.RotationKeys != null && track.RotationKeys.Length > 0)
			{
				localRot = SampleRotation(track.RotationKeys, time);
			}

			if (joint.Bone == HumanoidBone.Hips)
			{
				worldPositions[joint.Bone] = joint.RestOffset + rootTranslation;
				worldRotations[joint.Bone] = localRot;
			}
			else
			{
				Quaternion parentRot = worldRotations.TryGetValue(joint.Parent, out var pRot) ? pRot : Quaternion.Identity;
				Vector3 parentPos = worldPositions.TryGetValue(joint.Parent, out var pPos) ? pPos : Vector3.Zero;

				Vector3 localOffsetRotated = parentRot * joint.RestOffset;
				worldPositions[joint.Bone] = parentPos + localOffsetRotated;
				worldRotations[joint.Bone] = (parentRot * localRot).Normalized();
			}
		}

		var projectedPoints = new Dictionary<HumanoidBone, Vector2I>();
		foreach (var kvp in worldPositions)
		{
			projectedPoints[kvp.Key] = Project3DTo2D(kvp.Value);
		}

		var img = Image.CreateEmpty(ThumbnailSize, ThumbnailSize, false, Image.Format.Rgba8);
		img.Fill(new Color(0.08f, 0.09f, 0.12f, 1.0f));

		DrawCardBorder(img);
		DrawFloorShadow(img, projectedPoints.TryGetValue(HumanoidBone.Hips, out var hipsPt) ? hipsPt.X : 64);

		var colorSpine = new Color(0.35f, 0.90f, 0.95f, 1.0f);
		var colorLeftLimb = new Color(0.25f, 0.70f, 1.0f, 1.0f);
		var colorRightLimb = new Color(0.98f, 0.75f, 0.20f, 1.0f);
		var colorJoint = new Color(1.0f, 1.0f, 1.0f, 1.0f);

		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.Hips, HumanoidBone.Spine, HumanoidBone.Chest, HumanoidBone.UpperChest, HumanoidBone.Neck, HumanoidBone.Head }, colorSpine);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.UpperChest, HumanoidBone.LeftShoulder, HumanoidBone.LeftUpperArm, HumanoidBone.LeftLowerArm, HumanoidBone.LeftHand }, colorLeftLimb);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.UpperChest, HumanoidBone.RightShoulder, HumanoidBone.RightUpperArm, HumanoidBone.RightLowerArm, HumanoidBone.RightHand }, colorRightLimb);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.Hips, HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot, HumanoidBone.LeftToes }, colorLeftLimb);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.Hips, HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot, HumanoidBone.RightToes }, colorRightLimb);

		foreach (var kvp in projectedPoints)
		{
			int radius = (kvp.Key == HumanoidBone.Head) ? 5 : ((kvp.Key == HumanoidBone.Hips || kvp.Key == HumanoidBone.Chest) ? 3 : 2);
			DrawFilledCircle(img, kvp.Value.X, kvp.Value.Y, radius, colorJoint);
		}

		return img;
	}

	private static Vector2I Project3DTo2D(Vector3 worldPos)
	{
		float targetCenterX = 0f;
		float targetCenterY = 0.85f;
		float targetCenterZ = 0f;

		float dx = worldPos.X - targetCenterX;
		float dy = worldPos.Y - targetCenterY;
		float dz = worldPos.Z - targetCenterZ;

		float cosAz = MathF.Cos(0.42f);
		float sinAz = MathF.Sin(0.42f);
		float cosEl = MathF.Cos(0.18f);
		float sinEl = MathF.Sin(0.18f);

		float camX = dx * cosAz - dz * sinAz;
		float camZ = dx * sinAz + dz * cosAz;
		float camY = dy * cosEl - camZ * sinEl;

		float pixelsPerMeter = 56.0f;
		int u = (int)MathF.Round(64f + camX * pixelsPerMeter);
		int v = (int)MathF.Round(66f - camY * pixelsPerMeter);

		return new Vector2I(u, v);
	}

	private static void DrawBoneChain(Image img, Dictionary<HumanoidBone, Vector2I> points, HumanoidBone[] chain, Color color)
	{
		for (int i = 0; i < chain.Length - 1; i++)
		{
			if (points.TryGetValue(chain[i], out var p0) && points.TryGetValue(chain[i + 1], out var p1))
			{
				DrawThickLine(img, p0.X, p0.Y, p1.X, p1.Y, color, 2);
			}
		}
	}

	private static void DrawCardBorder(Image img)
	{
		var borderColor = new Color(0.24f, 0.26f, 0.32f, 0.9f);
		int w = img.GetWidth();
		int h = img.GetHeight();
		for (int x = 0; x < w; x++)
		{
			img.SetPixel(x, 0, borderColor);
			img.SetPixel(x, h - 1, borderColor);
		}
		for (int y = 0; y < h; y++)
		{
			img.SetPixel(0, y, borderColor);
			img.SetPixel(w - 1, y, borderColor);
		}
	}

	private static void DrawFloorShadow(Image img, int centerX)
	{
		int groundY = 118;
		var shadowColor = new Color(0.04f, 0.05f, 0.07f, 0.7f);
		int radiusX = 26;
		int radiusY = 7;

		for (int y = -radiusY; y <= radiusY; y++)
		{
			for (int x = -radiusX; x <= radiusX; x++)
			{
				float normX = (float)x / radiusX;
				float normY = (float)y / radiusY;
				if (normX * normX + normY * normY <= 1.0f)
				{
					int px = centerX + x;
					int py = groundY + y;
					if (px >= 0 && px < img.GetWidth() && py >= 0 && py < img.GetHeight())
					{
						img.SetPixel(px, py, shadowColor);
					}
				}
			}
		}
	}

	private static void DrawThickLine(Image img, int x0, int y0, int x1, int y1, Color color, int thickness)
	{
		int dx = Math.Abs(x1 - x0);
		int dy = Math.Abs(y1 - y0);
		int sx = x0 < x1 ? 1 : -1;
		int sy = y0 < y1 ? 1 : -1;
		int err = dx - dy;
		int halfThick = thickness / 2;

		while (true)
		{
			for (int ty = -halfThick; ty <= halfThick; ty++)
			{
				for (int tx = -halfThick; tx <= halfThick; tx++)
				{
					int px = x0 + tx;
					int py = y0 + ty;
					if (px >= 0 && px < img.GetWidth() && py >= 0 && py < img.GetHeight())
					{
						img.SetPixel(px, py, color);
					}
				}
			}

			if (x0 == x1 && y0 == y1) break;
			int e2 = 2 * err;
			if (e2 > -dy)
			{
				err -= dy;
				x0 += sx;
			}
			if (e2 < dx)
			{
				err += dx;
				y0 += sy;
			}
		}
	}

	private static void DrawFilledCircle(Image img, int cx, int cy, int radius, Color color)
	{
		int r2 = radius * radius;
		for (int y = -radius; y <= radius; y++)
		{
			for (int x = -radius; x <= radius; x++)
			{
				if (x * x + y * y <= r2)
				{
					int px = cx + x;
					int py = cy + y;
					if (px >= 0 && px < img.GetWidth() && py >= 0 && py < img.GetHeight())
					{
						img.SetPixel(px, py, color);
					}
				}
			}
		}
	}

	private static Vector3 SamplePosition(RealmKeyframeVector3[] keys, float time)
	{
		if (keys == null || keys.Length == 0) return Vector3.Zero;
		if (keys.Length == 1 || time <= keys[0].Time) return new Vector3(keys[0].X, keys[0].Y, keys[0].Z);
		if (time >= keys[^1].Time) return new Vector3(keys[^1].X, keys[^1].Y, keys[^1].Z);

		for (int i = 0; i < keys.Length - 1; i++)
		{
			if (time >= keys[i].Time && time <= keys[i + 1].Time)
			{
				float segDuration = keys[i + 1].Time - keys[i].Time;
				float t = segDuration > 0.0001f ? (time - keys[i].Time) / segDuration : 0f;
				var p0 = new Vector3(keys[i].X, keys[i].Y, keys[i].Z);
				var p1 = new Vector3(keys[i + 1].X, keys[i + 1].Y, keys[i + 1].Z);
				return p0.Lerp(p1, t);
			}
		}
		return new Vector3(keys[0].X, keys[0].Y, keys[0].Z);
	}

	private static Quaternion SampleRotation(RealmKeyframeQuaternion[] keys, float time)
	{
		if (keys == null || keys.Length == 0) return Quaternion.Identity;
		if (keys.Length == 1 || time <= keys[0].Time)
		{
			var q = new Quaternion(keys[0].X, keys[0].Y, keys[0].Z, keys[0].W);
			return q.LengthSquared() > 0.0001f ? q.Normalized() : Quaternion.Identity;
		}
		if (time >= keys[^1].Time)
		{
			var q = new Quaternion(keys[^1].X, keys[^1].Y, keys[^1].Z, keys[^1].W);
			return q.LengthSquared() > 0.0001f ? q.Normalized() : Quaternion.Identity;
		}

		for (int i = 0; i < keys.Length - 1; i++)
		{
			if (time >= keys[i].Time && time <= keys[i + 1].Time)
			{
				float segDuration = keys[i + 1].Time - keys[i].Time;
				float t = segDuration > 0.0001f ? (time - keys[i].Time) / segDuration : 0f;
				var q0 = new Quaternion(keys[i].X, keys[i].Y, keys[i].Z, keys[i].W).Normalized();
				var q1 = new Quaternion(keys[i + 1].X, keys[i + 1].Y, keys[i + 1].Z, keys[i + 1].W).Normalized();
				return q0.Slerp(q1, t);
			}
		}
		return Quaternion.Identity;
	}
}
