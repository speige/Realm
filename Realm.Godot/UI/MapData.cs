public class MapData
{
	public string MapId { get; set; }
	public string Title { get; set; }
	public string Creator { get; set; }
	public string ThumbnailPath { get; set; }
	public string Description { get; set; }
	public string[] Screenshots { get; set; }
	public string[] Features { get; set; }
	

	public float RatingStars { get; set; }
	public string Votes5Star { get; set; }
	public string Votes3Star { get; set; }
	public string Votes1Star { get; set; }
	public string AvgRating { get; set; }
	

	public string AvgPlaytime { get; set; }
	public string PlayerCount { get; set; }
	public string CompletionRate { get; set; }
	

	public string FileSize { get; set; }
	public string EngineVersion { get; set; }
	public string MaxPlayers { get; set; }
	public string Genre { get; set; }
	

	public string[] Awards { get; set; }


	public static MapData[] GetDummyMaps()
	{
		return new MapData[]
		{
			new MapData
			{
				MapId = "castle_td",
				Title = "CASTLE TD",
				Creator = "Realm Builder",
				ThumbnailPath = "res://Assets/UI/moonlit_castle.png",
				Description = "Defend the last fortress against waves of ancient evil! Includes the sintepres sucitoure unit. Features deep mountain passes, branching paths, unique hero units, and custom tower designs. Can you withstand the boss waves?",
				Screenshots = new string[]
				{
					"res://Assets/UI/moonlit_castle.png",
					"res://Assets/UI/moonlit_forest.png",
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/snowy_forest_path.png"
				},
				Features = new string[]
				{
					"Custom Units",
					"Boss Waves",
					"Achievements",
					"Hardcore Mode"
				},
				RatingStars = 4.7f,
				Votes5Star = "14,231 Votes",
				Votes3Star = "1,533 Votes",
				Votes1Star = "3,273 Votes",
				AvgRating = "4.7 / 5.0",
				AvgPlaytime = "42 min",
				PlayerCount = "1.2k Active (Last 24h)",
				CompletionRate = "74%",
				FileSize = "1.8 GB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "8 Players",
				Genre = "Campaign / TD",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.png",
					"res://Assets/UI/victory_flag.png",
					"res://Assets/UI/battle_shield.png",
					"res://Assets/UI/battle_axe.png"
				}
			},
			new MapData
			{
				MapId = "moonlit_valley",
				Title = "MOONLIT VALLEY",
				Creator = "Elven Scout",
				ThumbnailPath = "res://Assets/UI/moonlit_forest.png",
				Description = "An elven sanctuary under attack by the undead horde. Harvest timber, fortify ancient trees, and command the forest spirits to push back the darkness in this beautiful, defense-focused melee map.",
				Screenshots = new string[]
				{
					"res://Assets/UI/moonlit_forest.png",
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/moonlit_castle.png"
				},
				Features = new string[]
				{
					"Elven Alliance",
					"Timber Economy",
					"Hero Units",
					"Dynamic Weather"
				},
				RatingStars = 4.3f,
				Votes5Star = "8,409 Votes",
				Votes3Star = "720 Votes",
				Votes1Star = "950 Votes",
				AvgRating = "4.3 / 5.0",
				AvgPlaytime = "35 min",
				PlayerCount = "850 Active",
				CompletionRate = "81%",
				FileSize = "950 MB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "4 Players",
				Genre = "Melee / Defense",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.png",
					"res://Assets/UI/battle_shield.png"
				}
			},
			new MapData
			{
				MapId = "frostbite_pass",
				Title = "FROSTBITE PASS",
				Creator = "Ice Mage",
				ThumbnailPath = "res://Assets/UI/snowy_forest_path.png",
				Description = "Brace yourself for freezing winds and relentless siege attacks. Frostbite Pass is a highly tactical map where snow slows unit movement and avalanches can shift the battlefield layout.",
				Screenshots = new string[]
				{
					"res://Assets/UI/snowy_forest_path.png",
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/moonlit_castle.png"
				},
				Features = new string[]
				{
					"Snow Hazards",
					"Avalanches",
					"Custom Towers",
					"Achievements"
				},
				RatingStars = 3.9f,
				Votes5Star = "4,213 Votes",
				Votes3Star = "1,980 Votes",
				Votes1Star = "1,112 Votes",
				AvgRating = "3.9 / 5.0",
				AvgPlaytime = "50 min",
				PlayerCount = "420 Active",
				CompletionRate = "52%",
				FileSize = "1.2 GB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "6 Players",
				Genre = "Coop / Survival",
				Awards = new string[]
				{
					"res://Assets/UI/battle_axe.png",
					"res://Assets/UI/victory_flag.png"
				}
			},
			new MapData
			{
				MapId = "forest_trails",
				Title = "FOREST TRAILS",
				Creator = "Pathfinder",
				ThumbnailPath = "res://Assets/UI/forest_path.png",
				Description = "A simple yet beautiful beginner-friendly map featuring lush forests, wide lanes, and rich resource spots. Perfect for testing new strategies or practicing build orders.",
				Screenshots = new string[]
				{
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/moonlit_forest.png",
					"res://Assets/UI/snowy_forest_path.png"
				},
				Features = new string[]
				{
					"Rich Resources",
					"Beginner Friendly",
					"Simple Paths"
				},
				RatingStars = 4.5f,
				Votes5Star = "12,987 Votes",
				Votes3Star = "1,102 Votes",
				Votes1Star = "640 Votes",
				AvgRating = "4.5 / 5.0",
				AvgPlaytime = "25 min",
				PlayerCount = "2.1k Active",
				CompletionRate = "92%",
				FileSize = "620 MB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "2 Players",
				Genre = "Tutorial / Skirmish",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.png",
					"res://Assets/UI/victory_flag.png"
				}
			}
		};
	}

	public static MapData FromDto(Realm.Shared.Distribution.DiscoveryMapDto dto, string? serverBaseUrl = null)
	{
		string thumbPath = "";
		if (!string.IsNullOrEmpty(dto.ThumbnailHash))
		{
			string? localCasPath = MapAssetManager.Storage.FindAssetFilePath(dto.ThumbnailHash);
			if (localCasPath != null && System.IO.File.Exists(localCasPath))
			{
				thumbPath = localCasPath;
			}
		}
		if (string.IsNullOrEmpty(thumbPath))
		{
			thumbPath = !string.IsNullOrEmpty(dto.ThumbnailUrl) ? dto.ThumbnailUrl : "res://Assets/UI/moonlit_castle.png";
		}

		var screenshots = new System.Collections.Generic.List<string>();
		if (dto.Screenshots != null && dto.Screenshots.Count > 0)
		{
			foreach (var s in dto.Screenshots)
			{
				string? localCas = MapAssetManager.Storage.FindAssetFilePath(s);
				if (localCas != null && System.IO.File.Exists(localCas))
				{
					screenshots.Add(localCas);
				}
			}
		}
		if (screenshots.Count == 0)
		{
			screenshots.Add("res://Assets/UI/moonlit_castle.png");
			screenshots.Add("res://Assets/UI/moonlit_forest.png");
			screenshots.Add("res://Assets/UI/forest_path.png");
		}

		var features = dto.Features != null && dto.Features.Count > 0
			? dto.Features.ToArray()
			: new string[] { "Custom Assets", "Verified Map", "Community Rated" };

		int totalVotes = dto.TotalReviews > 0 ? dto.TotalReviews : 10;
		int v5 = (int)(totalVotes * 0.7);
		int v3 = (int)(totalVotes * 0.2);
		int v1 = System.Math.Max(0, totalVotes - v5 - v3);

		return new MapData
		{
			MapId = !string.IsNullOrEmpty(dto.MapId) ? dto.MapId : $"{dto.Title}_{dto.Version}",
			Title = dto.Title,
			Creator = dto.Creator,
			ThumbnailPath = thumbPath,
			Description = !string.IsNullOrEmpty(dto.Description) ? dto.Description : "A custom map package published to the Realm network.",
			Screenshots = screenshots.ToArray(),
			Features = features,
			RatingStars = dto.RatingStars > 0 ? dto.RatingStars : 5.0f,
			Votes5Star = $"{v5:N0} Votes",
			Votes3Star = $"{v3:N0} Votes",
			Votes1Star = $"{v1:N0} Votes",
			AvgRating = $"{dto.RatingStars:F1} / 5.0",
			AvgPlaytime = dto.PlaytimeMinutes > 0 ? $"{dto.PlaytimeMinutes} min" : "30 min",
			PlayerCount = dto.GamesPlayed > 0 ? $"{dto.GamesPlayed} Played" : "New Release",
			CompletionRate = "85%",
			FileSize = !string.IsNullOrEmpty(dto.FileSizeFormatted) ? dto.FileSizeFormatted : "10 MB",
			EngineVersion = dto.EngineVersion,
			MaxPlayers = dto.MaxPlayers,
			Genre = !string.IsNullOrEmpty(dto.Genre) ? dto.Genre : "Custom Map",
			Awards = dto.Awards != null && dto.Awards.Count > 0 ? dto.Awards.ToArray() : new string[]
			{
				"res://Assets/UI/gold_coin.png",
				"res://Assets/UI/battle_shield.png"
			}
		};
	}
}
