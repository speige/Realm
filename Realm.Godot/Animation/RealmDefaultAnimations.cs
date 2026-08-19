using Godot;
using System;
using System.IO;

namespace Realm.Godot.Animation;

public static class RealmDefaultAnimations
{
    public static RealmAnimationData Idle { get; private set; }
    public static RealmAnimationData Walk { get; private set; }
    public static RealmAnimationData Attack { get; private set; }
    public static RealmAnimationData Death { get; private set; }
    public static RealmAnimationData Labor { get; private set; }
    public static RealmAnimationData Spell_Cast { get; private set; }
    public static RealmAnimationData Dance { get; private set; }

    public static void EnsureDefaultTemplateAnimations(string baseAssetsDirectory)
	{
		try
		{
			string animsDir = Path.Combine(baseAssetsDirectory, "animations");
			if (!Directory.Exists(animsDir))
			{
				Directory.CreateDirectory(animsDir);
			}

			Idle = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "idle.ranim"));
			Walk = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "walk.ranim"));
			Attack = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "attack.ranim"));
			Death = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "death.ranim"));
			Labor = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "labor.ranim"));
			Spell_Cast = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "spell_cast.ranim"));
			Dance = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "dance.ranim"));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RealmDefaultAnimations] Error ensuring default animations: {ex.Message}");
		}
	}
}
