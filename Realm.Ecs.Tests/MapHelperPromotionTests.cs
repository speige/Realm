using System.Numerics;
using NUnit.Framework;
using Realm.MapAPI;

namespace Realm.Ecs.Tests;

[TestFixture]
public class MapHelperPromotionTests
{
	[Test]
	public void TryGetCenters_Succeeds_When_All_Names_Exist()
	{
		Vector3? Lookup(string name) => name switch
		{
			"Base_Team1" => new Vector3(1, 0, 2),
			"Base_Team2" => new Vector3(9, 0, 8),
			_ => null
		};

		var ok = CoordinateResolver.TryGetCenters(
			Lookup, ["Base_Team1", "Base_Team2"], out var centers, out var missing);

		Assert.That(ok, Is.True);
		Assert.That(missing, Is.Null);
		Assert.That(centers["Base_Team1"], Is.EqualTo(new Vector3(1, 0, 2)));
		Assert.That(centers["Base_Team2"], Is.EqualTo(new Vector3(9, 0, 8)));
	}

	[Test]
	public void TryGetCenters_Fails_With_Missing_Name()
	{
		Vector3? Lookup(string name) => name == "Spawn_Team1" ? new Vector3(0, 0, 0) : null;

		var ok = CoordinateResolver.TryGetCenters(
			Lookup, ["Spawn_Team1", "Missing_Zone"], out var centers, out var missing);

		Assert.That(ok, Is.False);
		Assert.That(missing, Is.EqualTo("Missing_Zone"));
		Assert.That(centers, Is.Empty);
	}

	[Test]
	public void TryBuildThreePointPath_Fails_When_Corner_Missing()
	{
		var ok = CoordinateResolver.TryBuildThreePointPath(
			_ => null,
			new Vector3(0, 0, 0),
			"Top_Corner",
			new Vector3(10, 0, 10),
			out var waypoints);

		Assert.That(ok, Is.False);
		Assert.That(waypoints, Is.Empty);
	}

	[Test]
	public void TryBuildThreePointPath_Order_Is_Start_Corner_Dest()
	{
		var start = new Vector3(0, 0, 0);
		var dest = new Vector3(10, 0, 10);
		var corner = new Vector3(5, 0, 0);

		var ok = CoordinateResolver.TryBuildThreePointPath(
			name => name == "Middle" ? corner : null,
			start,
			"Middle",
			dest,
			out var waypoints);

		Assert.That(ok, Is.True);
		Assert.That(waypoints, Is.EqualTo(new[] { start, corner, dest }));
	}

	[Test]
	public void AfterKill_First_Kill_Stays_Level_1()
	{
		var config = new HeroProgressionConfig(100f, 25f, 40f, 0, 300f, "MOBA");
		var result = HeroKillReward.AfterKill(config, 300f, 0f);

		Assert.That(result.Gold, Is.EqualTo(325f));
		Assert.That(result.Xp, Is.EqualTo(40f));
		Assert.That(result.Level, Is.EqualTo(1));
	}

	[Test]
	public void AfterKill_Three_Kills_Reaches_Level_2()
	{
		var config = new HeroProgressionConfig(100f, 25f, 40f, 0, 300f, "MOBA");
		var gold = 300f;
		var xp = 0f;
		(gold, xp, var level) = (300f, 0f, 1);
		for (var i = 0; i < 3; i++)
		{
			var result = HeroKillReward.AfterKill(config, gold, xp);
			gold = result.Gold;
			xp = result.Xp;
			level = result.Level;
		}

		Assert.That(gold, Is.EqualTo(375f));
		Assert.That(xp, Is.EqualTo(120f));
		Assert.That(level, Is.EqualTo(2));
	}
}
