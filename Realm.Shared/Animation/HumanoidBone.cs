using System;
using System.Collections.Generic;

namespace Realm.Shared.Animation;

public enum HumanoidBone
{
	Hips,
	Spine,
	Chest,
	UpperChest,
	Neck,
	Head,
	LeftShoulder,
	LeftUpperArm,
	LeftLowerArm,
	LeftHand,
	RightShoulder,
	RightUpperArm,
	RightLowerArm,
	RightHand,
	LeftUpperLeg,
	LeftLowerLeg,
	LeftFoot,
	LeftToes,
	RightUpperLeg,
	RightLowerLeg,
	RightFoot,
	RightToes,
	LeftThumb1,
	LeftThumb2,
	LeftThumb3,
	LeftIndex1,
	LeftIndex2,
	LeftIndex3,
	LeftMiddle1,
	LeftMiddle2,
	LeftMiddle3,
	LeftRing1,
	LeftRing2,
	LeftRing3,
	LeftLittle1,
	LeftLittle2,
	LeftLittle3,
	RightThumb1,
	RightThumb2,
	RightThumb3,
	RightIndex1,
	RightIndex2,
	RightIndex3,
	RightMiddle1,
	RightMiddle2,
	RightMiddle3,
	RightRing1,
	RightRing2,
	RightRing3,
	RightLittle1,
	RightLittle2,
	RightLittle3
}

public class HumanoidBoneMapper
{
	private static readonly Dictionary<string, HumanoidBone> KnownAliases = new(StringComparer.OrdinalIgnoreCase)
	{
		["hips"] = HumanoidBone.Hips,
		["pelvis"] = HumanoidBone.Hips,
		["root"] = HumanoidBone.Hips,
		["spine"] = HumanoidBone.Spine,
		["spine1"] = HumanoidBone.Chest,
		["chest"] = HumanoidBone.Chest,
		["spine2"] = HumanoidBone.UpperChest,
		["upperchest"] = HumanoidBone.UpperChest,
		["neck"] = HumanoidBone.Neck,
		["head"] = HumanoidBone.Head,

		["leftshoulder"] = HumanoidBone.LeftShoulder,
		["shoulder_l"] = HumanoidBone.LeftShoulder,
		["l_shoulder"] = HumanoidBone.LeftShoulder,
		["clavicle_l"] = HumanoidBone.LeftShoulder,
		["l_clavicle"] = HumanoidBone.LeftShoulder,

		["leftarm"] = HumanoidBone.LeftUpperArm,
		["leftupperarm"] = HumanoidBone.LeftUpperArm,
		["upperarm_l"] = HumanoidBone.LeftUpperArm,
		["l_upperarm"] = HumanoidBone.LeftUpperArm,
		["arm_l"] = HumanoidBone.LeftUpperArm,

		["leftforearm"] = HumanoidBone.LeftLowerArm,
		["leftlowerarm"] = HumanoidBone.LeftLowerArm,
		["forearm_l"] = HumanoidBone.LeftLowerArm,
		["lowerarm_l"] = HumanoidBone.LeftLowerArm,
		["l_forearm"] = HumanoidBone.LeftLowerArm,

		["lefthand"] = HumanoidBone.LeftHand,
		["hand_l"] = HumanoidBone.LeftHand,
		["l_hand"] = HumanoidBone.LeftHand,

		["rightshoulder"] = HumanoidBone.RightShoulder,
		["shoulder_r"] = HumanoidBone.RightShoulder,
		["r_shoulder"] = HumanoidBone.RightShoulder,
		["clavicle_r"] = HumanoidBone.RightShoulder,
		["r_clavicle"] = HumanoidBone.RightShoulder,

		["rightarm"] = HumanoidBone.RightUpperArm,
		["rightupperarm"] = HumanoidBone.RightUpperArm,
		["upperarm_r"] = HumanoidBone.RightUpperArm,
		["r_upperarm"] = HumanoidBone.RightUpperArm,
		["arm_r"] = HumanoidBone.RightUpperArm,

		["rightforearm"] = HumanoidBone.RightLowerArm,
		["rightlowerarm"] = HumanoidBone.RightLowerArm,
		["forearm_r"] = HumanoidBone.RightLowerArm,
		["lowerarm_r"] = HumanoidBone.RightLowerArm,
		["r_forearm"] = HumanoidBone.RightLowerArm,

		["righthand"] = HumanoidBone.RightHand,
		["hand_r"] = HumanoidBone.RightHand,
		["r_hand"] = HumanoidBone.RightHand,

		["leftupleg"] = HumanoidBone.LeftUpperLeg,
		["leftupperleg"] = HumanoidBone.LeftUpperLeg,
		["thigh_l"] = HumanoidBone.LeftUpperLeg,
		["upperleg_l"] = HumanoidBone.LeftUpperLeg,
		["l_thigh"] = HumanoidBone.LeftUpperLeg,
		["l_upperleg"] = HumanoidBone.LeftUpperLeg,

		["leftleg"] = HumanoidBone.LeftLowerLeg,
		["leftlowerleg"] = HumanoidBone.LeftLowerLeg,
		["shin_l"] = HumanoidBone.LeftLowerLeg,
		["calf_l"] = HumanoidBone.LeftLowerLeg,
		["lowerleg_l"] = HumanoidBone.LeftLowerLeg,
		["l_calf"] = HumanoidBone.LeftLowerLeg,
		["l_shin"] = HumanoidBone.LeftLowerLeg,

		["leftfoot"] = HumanoidBone.LeftFoot,
		["foot_l"] = HumanoidBone.LeftFoot,
		["l_foot"] = HumanoidBone.LeftFoot,

		["lefttoebase"] = HumanoidBone.LeftToes,
		["lefttoe"] = HumanoidBone.LeftToes,
		["lefttoes"] = HumanoidBone.LeftToes,
		["toe_l"] = HumanoidBone.LeftToes,
		["toes_l"] = HumanoidBone.LeftToes,
		["l_toe"] = HumanoidBone.LeftToes,

		["rightupleg"] = HumanoidBone.RightUpperLeg,
		["rightupperleg"] = HumanoidBone.RightUpperLeg,
		["thigh_r"] = HumanoidBone.RightUpperLeg,
		["upperleg_r"] = HumanoidBone.RightUpperLeg,
		["r_thigh"] = HumanoidBone.RightUpperLeg,
		["r_upperleg"] = HumanoidBone.RightUpperLeg,

		["rightleg"] = HumanoidBone.RightLowerLeg,
		["rightlowerleg"] = HumanoidBone.RightLowerLeg,
		["shin_r"] = HumanoidBone.RightLowerLeg,
		["calf_r"] = HumanoidBone.RightLowerLeg,
		["lowerleg_r"] = HumanoidBone.RightLowerLeg,
		["r_calf"] = HumanoidBone.RightLowerLeg,
		["r_shin"] = HumanoidBone.RightLowerLeg,

		["rightfoot"] = HumanoidBone.RightFoot,
		["foot_r"] = HumanoidBone.RightFoot,
		["r_foot"] = HumanoidBone.RightFoot,

		["righttoebase"] = HumanoidBone.RightToes,
		["righttoe"] = HumanoidBone.RightToes,
		["righttoes"] = HumanoidBone.RightToes,
		["toe_r"] = HumanoidBone.RightToes,
		["toes_r"] = HumanoidBone.RightToes,
		["r_toe"] = HumanoidBone.RightToes
	};

	public static string CleanBoneName(string rawName)
	{
		if (string.IsNullOrEmpty(rawName)) return string.Empty;
		string clean = rawName;
		int colonIdx = clean.LastIndexOf(':');
		if (colonIdx >= 0)
		{
			clean = clean.Substring(colonIdx + 1);
		}
		int slashIdx = clean.LastIndexOf('/');
		if (slashIdx >= 0)
		{
			clean = clean.Substring(slashIdx + 1);
		}
		if (clean.StartsWith("mixamorig_", StringComparison.OrdinalIgnoreCase))
		{
			clean = clean.Substring("mixamorig_".Length);
		}
		if (clean.StartsWith("bip01_", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("bip01 ", StringComparison.OrdinalIgnoreCase))
		{
			clean = clean.Substring(6);
		}
		return clean;
	}

	public static bool TryMapToCanonical(string rawName, out HumanoidBone canonicalBone)
	{
		string cleaned = CleanBoneName(rawName);
		if (KnownAliases.TryGetValue(cleaned, out canonicalBone))
		{
			return true;
		}

		if (Enum.TryParse<HumanoidBone>(cleaned, true, out canonicalBone))
		{
			return true;
		}

		canonicalBone = default;
		return false;
	}
}
