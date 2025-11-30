using System.Collections.Generic;

public class MapData
{
	public string MapId { get; set; }
	public string Title { get; set; }
	public string Version { get; set; } = "1.0.0";
	public string ManifestHash { get; set; } = string.Empty;
	public List<MapData> AvailableVersions { get; set; } = new();
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
			thumbPath = !string.IsNullOrEmpty(dto.ThumbnailUrl) ? dto.ThumbnailUrl : "";
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
			: (dto.Tags != null && dto.Tags.Count > 0 ? dto.Tags.ToArray() : new string[] { "Custom Assets", "Verified Map", "Community Rated" });

		float rating = dto.RatingStars > 0 ? dto.RatingStars : (dto.AverageRating > 0 ? (float)dto.AverageRating : 5.0f);
		int totalVotes = dto.TotalReviews;
		int v5 = 0, v3 = 0, v1 = 0;
		if (totalVotes > 0)
		{
			v5 = System.Math.Clamp((int)System.Math.Round(totalVotes * System.Math.Max(0.0, (rating - 3.0) / 2.0)), 0, totalVotes);
			v1 = System.Math.Clamp((int)System.Math.Round(totalVotes * System.Math.Max(0.0, (3.0 - rating) / 2.0)), 0, totalVotes - v5);
			v3 = totalVotes - v5 - v1;
		}

		string version = !string.IsNullOrWhiteSpace(dto.Version) ? dto.Version.Trim() : "1.0.0";
		string avgPlaytime = dto.PlaytimeMinutes > 0
			? (dto.GamesPlayed > 0 ? $"{(int)System.Math.Max(1, System.Math.Round((double)dto.PlaytimeMinutes / dto.GamesPlayed))} min" : $"{dto.PlaytimeMinutes} min")
			: "N/A";
		string playerCount = dto.GamesPlayed > 0 ? $"{dto.GamesPlayed:N0} Played" : "New Release";
		string completionRate = dto.GamesPlayed > 0 ? "100%" : "N/A";
		string fileSize = !string.IsNullOrWhiteSpace(dto.FileSizeFormatted)
			? dto.FileSizeFormatted
			: (dto.TotalSizeBytes > 0 ? $"{dto.TotalSizeBytes / (1024.0 * 1024.0):F1} MB" : "0 MB");
		string genre = !string.IsNullOrWhiteSpace(dto.Genre)
			? dto.Genre
			: (dto.Tags != null && dto.Tags.Count > 0 ? dto.Tags[0] : "Custom Map");

		var mapData = new MapData
		{
			MapId = !string.IsNullOrEmpty(dto.MapId) ? dto.MapId : $"{dto.Title}_{version}",
			Title = dto.Title,
			Version = version,
			Creator = dto.Creator,
			ThumbnailPath = thumbPath,
			Description = !string.IsNullOrEmpty(dto.Description) ? dto.Description : "A custom map package published to the Realm network.",
			Screenshots = screenshots.ToArray(),
			Features = features,
			RatingStars = rating,
			Votes5Star = $"{v5:N0} Votes",
			Votes3Star = $"{v3:N0} Votes",
			Votes1Star = $"{v1:N0} Votes",
			AvgRating = $"{rating:F1} / 5.0",
			AvgPlaytime = avgPlaytime,
			PlayerCount = playerCount,
			CompletionRate = completionRate,
			FileSize = fileSize,
			EngineVersion = !string.IsNullOrWhiteSpace(dto.EngineVersion) ? dto.EngineVersion : "Godot Realm Engine v1.0",
			MaxPlayers = !string.IsNullOrWhiteSpace(dto.MaxPlayers) ? dto.MaxPlayers : "8 Players",
			Genre = genre,
			Awards = dto.Awards != null && dto.Awards.Count > 0 ? dto.Awards.ToArray() : new string[]
			{
				"res://Assets/UI/gold_coin.png",
				"res://Assets/UI/battle_shield.png"
			}
		};

		mapData.AvailableVersions = new List<MapData> { mapData };
		return mapData;
	}
}
