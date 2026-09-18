using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using NSec.Cryptography;
using Realm.Shared.Distribution;

namespace Realm.Tools.Admin;

[Verb("greenlight", HelpText = "Cryptographically sign and greenlight a map on the registry server using an admin private key.")]
public class AdminGreenlightOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name to greenlight.")]
	public string Map { get; set; } = string.Empty;

	[Option('v', "version", Required = false, Default = "1.0", HelpText = "Map version to greenlight (default: 1.0).")]
	public string Version { get; set; } = "1.0";

	[Option('k', "key", Required = true, HelpText = "Path to admin private key file (PEM or raw 32-byte binary) or Base64 private key string.")]
	public string Key { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("status", HelpText = "Check the greenlight status and community metrics of a map on the registry server.")]
public class AdminStatusOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name to inspect.")]
	public string Map { get; set; } = string.Empty;

	[Option('v', "version", Required = false, Default = "1.0", HelpText = "Map version to inspect (default: 1.0).")]
	public string Version { get; set; } = "1.0";

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("unlock-name", HelpText = "Override or unlock a reserved creator username on the registry server using an admin private key.")]
public class AdminUnlockNameOptions
{
	[Option('u', "username", Required = true, HelpText = "Username to unlock or re-register.")]
	public string Username { get; set; } = string.Empty;

	[Option('k', "key", Required = true, HelpText = "Path to admin private key file or Base64 private key string.")]
	public string Key { get; set; } = string.Empty;

	[Option("target-key", Required = false, HelpText = "New public key to assign the username to (defaults to admin public key).")]
	public string? TargetKey { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("info", HelpText = "Display server admin information and public key configuration.")]
public class AdminInfoOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("sync-cluster", HelpText = "Cryptographically authorize and synchronize cluster metrics, maps, or creators between authoritative cluster nodes.")]
public class AdminSyncClusterOptions
{
	[Option('t', "type", Required = true, HelpText = "Sync type: 'metrics', 'maps', or 'creators'.")]
	public string Type { get; set; } = "metrics";

	[Option('k', "key", Required = true, HelpText = "Path to admin private key file or Base64 private key string.")]
	public string Key { get; set; } = string.Empty;

	[Option('d', "dest", Required = false, HelpText = "Destination registry server URL.")]
	public string? Destination { get; set; }

	[Option('p', "payload-file", Required = false, HelpText = "Path to JSON payload file containing items to synchronize.")]
	public string? PayloadFile { get; set; }
}

[Verb("export-events", HelpText = "Export cluster events from a seed node to a local JSON file.")]
public class AdminExportEventsOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('o', "out", Required = false, Default = "cluster_events.json", HelpText = "Output JSON file path.")]
	public string OutputFile { get; set; } = "cluster_events.json";

	[Option("since", Required = false, HelpText = "Optional ISO timestamp (e.g. 2026-01-01T00:00:00Z) to fetch events since.")]
	public string? Since { get; set; }

	[Option('l', "limit", Required = false, Default = 500, HelpText = "Maximum number of events to export.")]
	public int Limit { get; set; } = 500;
}

[Verb("replay-events", HelpText = "Replay cluster events from a local JSON file to a destination seed node.")]
public class AdminReplayEventsOptions
{
	[Option('i', "in", Required = true, HelpText = "Input JSON file path containing exported cluster events.")]
	public string InputFile { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Destination registry server URL.")]
	public string? Server { get; set; }
}

[Verb("prune-cas", HelpText = "Trigger server-side CAS integrity verification and orphan asset pruning.")]
public class AdminPruneCasOptions
{
	[Option('k', "key", Required = true, HelpText = "Path to admin private key file or Base64 private key string.")]
	public string Key { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("digest", HelpText = "Query and verify BLAKE3 database state digest across one or more seed nodes.")]
public class AdminDigestOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("snapshot", HelpText = "Download a complete database state snapshot from a seed node to a local JSON file.")]
public class AdminSnapshotOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('o', "out", Required = false, Default = "cluster_snapshot.json", HelpText = "Output JSON snapshot file path.")]
	public string OutputFile { get; set; } = "cluster_snapshot.json";
}

[Verb("restore-snapshot", HelpText = "Restore a database state snapshot file to a target seed node using an admin private key.")]
public class AdminRestoreSnapshotOptions
{
	[Option('i', "in", Required = true, HelpText = "Input JSON snapshot file path.")]
	public string InputFile { get; set; } = string.Empty;

	[Option('k', "key", Required = true, HelpText = "Path to admin private key file or Base64 private key string.")]
	public string Key { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Destination registry server URL.")]
	public string? Server { get; set; }
}

public static class Program
{
	public static int Main(string[] args)
	{
		return Parser.Default.ParseArguments<AdminGreenlightOptions, AdminStatusOptions, AdminUnlockNameOptions, AdminInfoOptions, AdminSyncClusterOptions, AdminExportEventsOptions, AdminReplayEventsOptions, AdminPruneCasOptions, AdminDigestOptions, AdminSnapshotOptions, AdminRestoreSnapshotOptions>(args)
			.MapResult(
				(AdminGreenlightOptions options) => ExecuteGreenlight(options),
				(AdminStatusOptions options) => ExecuteStatus(options),
				(AdminUnlockNameOptions options) => ExecuteUnlockName(options),
				(AdminInfoOptions options) => ExecuteInfo(options),
				(AdminSyncClusterOptions options) => ExecuteSyncCluster(options),
				(AdminExportEventsOptions options) => ExecuteExportEvents(options),
				(AdminReplayEventsOptions options) => ExecuteReplayEvents(options),
				(AdminPruneCasOptions options) => ExecutePruneCas(options),
				(AdminDigestOptions options) => ExecuteDigest(options),
				(AdminSnapshotOptions options) => ExecuteSnapshot(options),
				(AdminRestoreSnapshotOptions options) => ExecuteRestoreSnapshot(options),
				errors => 1);
	}

	private static string ResolveServerUrl(string? explicitServer)
	{
		if (!string.IsNullOrWhiteSpace(explicitServer))
		{
			return explicitServer.Trim();
		}

		var config = ServersConfigHelper.Load();
		if (config.RegistryServers.Count > 0 && !string.IsNullOrWhiteSpace(config.RegistryServers[0]))
		{
			return config.RegistryServers[0];
		}
		if (config.Servers.Count > 0 && !string.IsNullOrWhiteSpace(config.Servers[0].Url))
		{
			return config.Servers[0].Url;
		}

		return "http://localhost:5000";
	}

	private static (string privateKeyBase64, string publicKeyBase64)? ParseAdminKey(string keyInput)
	{
		if (string.IsNullOrWhiteSpace(keyInput))
		{
			Console.Error.WriteLine("Error: Admin key input cannot be empty.");
			return null;
		}

		try
		{
			string rawInput = keyInput.Trim();
			if (File.Exists(rawInput))
			{
				byte[] fileBytes = File.ReadAllBytes(rawInput);
				if (fileBytes.Length == 32)
				{
					rawInput = Convert.ToBase64String(fileBytes);
				}
				else
				{
					string textContent = File.ReadAllText(rawInput).Trim();
					textContent = textContent.Replace("-----BEGIN PRIVATE KEY-----", "")
						.Replace("-----END PRIVATE KEY-----", "")
						.Replace("\r", "")
						.Replace("\n", "")
						.Trim();
					rawInput = textContent;
				}
			}

			byte[] privateBytes = Convert.FromBase64String(rawInput);
			using var key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
			byte[] publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

			return (Convert.ToBase64String(privateBytes), Convert.ToBase64String(publicBytes));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error decoding admin private key: {ex.Message}");
			return null;
		}
	}

	private static int ExecuteGreenlight(AdminGreenlightOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string mapVersion = string.IsNullOrWhiteSpace(options.Version) ? "1.0" : options.Version.Trim();
		string payload = $"greenlight:{mapTitle.ToLowerInvariant()}:{mapVersion.ToLowerInvariant()}";
		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Map Greenlight Override");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Map Title:  {mapTitle}");
		Console.WriteLine($"Version:    {mapVersion}");
		Console.WriteLine($"Admin Key:  {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			var requestBody = new
			{
				MapTitle = mapTitle,
				MapVersion = mapVersion,
				AdminPublicKey = keyPair.Value.publicKeyBase64,
				Signature = signature
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/admin/greenlight", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Map '{mapTitle}' v{mapVersion} has been greenlit!");
				Console.WriteLine("The map creator may now publish this map directly through the Map Editor.");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteStatus(AdminStatusOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		string mapTitle = options.Map.Trim();
		string mapVersion = string.IsNullOrWhiteSpace(options.Version) ? "1.0" : options.Version.Trim();
		string endpoint = $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}";

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
			string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (!response.IsSuccessStatusCode)
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
				return 1;
			}

			var node = JsonNode.Parse(json);
			if (node == null)
			{
				Console.Error.WriteLine("Error parsing status response.");
				return 1;
			}

			bool isGreenlit = node["isGreenlit"]?.GetValue<bool>() ?? false;
			bool adminOverride = node["adminOverride"]?.GetValue<bool>() ?? false;
			int verifiedGoodReviews = node["verifiedGoodReviewsCount"]?.GetValue<int>() ?? 0;
			int totalReviews = node["totalReviewsCount"]?.GetValue<int>() ?? 0;
			double avgRating = node["averageRating"]?.GetValue<double>() ?? 0.0;

			Console.WriteLine("=================================================");
			Console.WriteLine($"Greenlight Status for '{mapTitle}' v{mapVersion}");
			Console.WriteLine("=================================================");
			Console.WriteLine($"Overall Greenlit:             {(isGreenlit ? "YES (Approved for Discovery)" : "NO (In Beta-Testing)")}");
			Console.WriteLine($"Admin Override:               {(adminOverride ? "ACTIVE" : "None")}");
			Console.WriteLine();
			Console.WriteLine("Greenlight Criteria:");
			Console.WriteLine($"• Total Verified Good Reviews: {verifiedGoodReviews} / 100 (Required: >= 100)");
			Console.WriteLine();
			Console.WriteLine("Public Discovery Statistics:");
			Console.WriteLine($"• Public Total Reviews:        {totalReviews}");
			Console.WriteLine($"• Public Average Rating:       {avgRating:F1} / 5.0");
			Console.WriteLine("=================================================");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteUnlockName(AdminUnlockNameOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string username = options.Username.Trim();
		string targetKey = !string.IsNullOrWhiteSpace(options.TargetKey) ? options.TargetKey.Trim() : keyPair.Value.publicKeyBase64;
		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, $"{username}:{targetKey}");

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Unlock / Reassign Creator Name");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Username:   {username}");
		Console.WriteLine($"Target Key: {targetKey}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			string bypassToken = AdminBypassAuth.CreateBypassToken(keyPair.Value.privateKeyBase64, "admin", "override");
			httpClient.DefaultRequestHeaders.Add("X-Admin-Bypass", bypassToken);

			var requestBody = new
			{
				Username = username,
				PublicKey = targetKey,
				Signature = signature
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/creators/register", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Username '{username}' has been successfully reassigned to public key '{targetKey}'.");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteInfo(AdminInfoOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			var response = httpClient.GetAsync($"{serverUrl.TrimEnd('/')}/api/admin/info").GetAwaiter().GetResult();
			string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("=================================================");
				Console.WriteLine("Realm Registry Server Admin Info");
				Console.WriteLine("=================================================");
				var node = JsonNode.Parse(json);
				Console.WriteLine($"Admin Username:      {node?["adminUsername"]?.ToString() ?? "N/A"}");
				Console.WriteLine($"Admin Public Key:    {node?["adminPublicKey"]?.ToString() ?? "N/A"}");
				Console.WriteLine($"Is Graduated Admin:  {node?["isGraduatedAdmin"]?.ToString() ?? "N/A"}");
				Console.WriteLine("=================================================");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteSyncCluster(AdminSyncClusterOptions options)
	{
		string destUrl = ResolveServerUrl(options.Destination);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string syncType = options.Type.Trim().ToLowerInvariant();
		string endpoint = syncType switch
		{
			"metrics" => "/api/cluster/sync_metrics",
			"maps" or "published_maps" => "/api/cluster/sync_published_maps",
			"creators" => "/api/cluster/sync_creators",
			_ => ""
		};

		if (string.IsNullOrEmpty(endpoint))
		{
			Console.Error.WriteLine($"[ERROR] Unknown sync type '{options.Type}'. Expected 'metrics', 'maps', or 'creators'.");
			return 1;
		}

		string jsonPayload = "[]";
		if (!string.IsNullOrWhiteSpace(options.PayloadFile) && File.Exists(options.PayloadFile))
		{
			jsonPayload = File.ReadAllText(options.PayloadFile);
		}

		string sigPayload = $"sync_{syncType}";
		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, sigPayload);
		string bypassToken = AdminBypassAuth.CreateBypassToken(keyPair.Value.privateKeyBase64, "cluster", "sync");

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Cluster Authorization Sync");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Destination Server: {destUrl}");
		Console.WriteLine($"Sync Type:          {syncType}");
		Console.WriteLine($"Admin Key:          {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
			httpClient.DefaultRequestHeaders.Add("X-Cluster-Signature", signature);
			httpClient.DefaultRequestHeaders.Add("X-Admin-PublicKey", keyPair.Value.publicKeyBase64);
			httpClient.DefaultRequestHeaders.Add("X-Admin-Bypass", bypassToken);

			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{destUrl.TrimEnd('/')}{endpoint}", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Cluster {syncType} synchronization accepted by destination node.");
				Console.WriteLine($"Response: {responseText}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteExportEvents(AdminExportEventsOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		DateTime? sinceUtc = null;
		if (!string.IsNullOrWhiteSpace(options.Since) && DateTime.TryParse(options.Since, out var parsed))
		{
			sinceUtc = parsed.ToUniversalTime();
		}

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Export Cluster Events");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Since:     {(sinceUtc.HasValue ? sinceUtc.Value.ToString("o") : "All History")}");
		Console.WriteLine($"Limit:     {options.Limit}");
		Console.WriteLine($"Output:    {options.OutputFile}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var events = client.GetClusterEventsAsync(sinceUtc, options.Limit).GetAwaiter().GetResult();

			string json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(options.OutputFile, json);

			Console.WriteLine($"[SUCCESS] Exported {events.Count} cluster events to '{options.OutputFile}'.");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Failed to export events: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteReplayEvents(AdminReplayEventsOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		if (!File.Exists(options.InputFile))
		{
			Console.Error.WriteLine($"[ERROR] File '{options.InputFile}' not found.");
			return 1;
		}

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Replay Cluster Events");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Input:     {options.InputFile}");
		Console.WriteLine();

		try
		{
			string json = File.ReadAllText(options.InputFile);
			var events = JsonSerializer.Deserialize<List<ClusterEventDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (events == null || events.Count == 0)
			{
				Console.WriteLine("No events found in file.");
				return 0;
			}

			var client = new DistributionClient(serverUrl);
			int successCount = 0;
			int failureCount = 0;

			foreach (var evt in events)
			{
				bool success = client.PostClusterEventAsync(evt).GetAwaiter().GetResult();
				if (success)
				{
					successCount++;
					Console.WriteLine($"[REPLAYED] Event '{evt.EventType}' ({evt.EventId})");
				}
				else
				{
					failureCount++;
					Console.Error.WriteLine($"[REJECTED] Event '{evt.EventType}' ({evt.EventId})");
				}
			}

			Console.WriteLine();
			Console.WriteLine($"[SUMMARY] Replayed {successCount}/{events.Count} events successfully ({failureCount} failed).");
			return failureCount == 0 ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Failed to replay events: {ex.Message}");
			return 1;
		}
	}

	private static int ExecutePruneCas(AdminPruneCasOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Server-Side CAS Prune");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Admin Key: {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var result = client.PruneServerCasAsync(keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

			if (result.Success)
			{
				Console.WriteLine("[SUCCESS] CAS prune completed.");
				Console.WriteLine($"Total Scanned:   {result.TotalScanned}");
				Console.WriteLine($"Orphans Pruned:  {result.OrphansPruned}");
				Console.WriteLine($"Corrupt Pruned:  {result.CorruptPruned}");
				Console.WriteLine($"Bytes Freed:     {result.BytesFreed} bytes");
				Console.WriteLine($"Message:         {result.Message}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] {result.Message}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] CAS prune failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteDigest(AdminDigestOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Database State Digest");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server: {serverUrl}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var digest = client.GetStateDigestAsync().GetAwaiter().GetResult();

			if (digest != null)
			{
				Console.WriteLine($"Root State Digest: {digest.StateDigest}");
				Console.WriteLine($"Server Timestamp:  {digest.ServerTimestampUtc:o}");
				Console.WriteLine();
				Console.WriteLine("Collection Hashes & Counts:");
				foreach (var col in digest.CollectionHashes.Keys.OrderBy(k => k))
				{
					int count = digest.CollectionCounts.TryGetValue(col, out var c) ? c : 0;
					Console.WriteLine($"• {col,-20} [{count,4} items] Hash: {digest.CollectionHashes[col]}");
				}
				Console.WriteLine("=================================================");
				return 0;
			}
			else
			{
				Console.Error.WriteLine("[FAILED] Could not retrieve state digest from server.");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteSnapshot(AdminSnapshotOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Export Database Snapshot");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server: {serverUrl}");
		Console.WriteLine($"Output: {options.OutputFile}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var snapshot = client.GetSnapshotAsync().GetAwaiter().GetResult();

			if (snapshot != null)
			{
				string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(options.OutputFile, json);

				Console.WriteLine($"[SUCCESS] Snapshot exported to '{options.OutputFile}'.");
				Console.WriteLine($"State Digest:   {snapshot.StateDigest}");
				Console.WriteLine($"Creators:       {snapshot.Creators.Count}");
				Console.WriteLine($"Published Maps: {snapshot.PublishedMaps.Count}");
				Console.WriteLine($"Map Stats:      {snapshot.MapStats.Count}");
				Console.WriteLine($"Generated At:   {snapshot.GeneratedAtUtc:o}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine("[FAILED] Could not retrieve snapshot from server.");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteRestoreSnapshot(AdminRestoreSnapshotOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		if (!File.Exists(options.InputFile))
		{
			Console.Error.WriteLine($"[ERROR] File '{options.InputFile}' not found.");
			return 1;
		}

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Restore Database Snapshot");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Input:     {options.InputFile}");
		Console.WriteLine($"Admin Key: {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			string json = File.ReadAllText(options.InputFile);
			var snapshot = JsonSerializer.Deserialize<ClusterSnapshotDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (snapshot == null)
			{
				Console.Error.WriteLine("[ERROR] Could not parse snapshot JSON.");
				return 1;
			}

			var client = new DistributionClient(serverUrl);
			var result = client.ApplySnapshotAsync(snapshot, keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

			if (result.Success)
			{
				Console.WriteLine($"[SUCCESS] Snapshot applied: {result.Message}");
				Console.WriteLine($"Computed Digest: {result.ComputedStateDigest}");
				Console.WriteLine($"Expected Digest: {result.ExpectedStateDigest}");
				Console.WriteLine($"Creators:        {result.CreatorsImported}");
				Console.WriteLine($"Maps:            {result.MapsImported}");
				Console.WriteLine($"Stats:           {result.StatsImported}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] {result.Message}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Restore failed: {ex.Message}");
			return 1;
		}
	}
}
