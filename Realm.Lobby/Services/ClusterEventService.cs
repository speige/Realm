using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Lobby.Services;

public class ClusterEventService
{
    private readonly ConcurrentDictionary<string, byte> _processedEvents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public bool HasEventBeenProcessed(string eventId, DataStoreService db)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        if (_processedEvents.ContainsKey(eventId)) return true;
        var existing = db.Get<JsonDocument>("processed_events", eventId);
        if (existing != null)
        {
            _processedEvents.TryAdd(eventId, 0);
            return true;
        }
        return false;
    }

    public void MarkEventProcessed(string eventId, DataStoreService db)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;
        _processedEvents.TryAdd(eventId, 0);
        var doc = JsonSerializer.SerializeToDocument(new { eventId, processedAt = DateTime.UtcNow });
        db.Upsert("processed_events", eventId, doc);
    }

    public void RecordEvent(ClusterEventDto evt, DataStoreService db)
    {
        if (string.IsNullOrWhiteSpace(evt.EventId)) return;
        db.Upsert("cluster_events", evt.EventId, evt);
        MarkEventProcessed(evt.EventId, db);
    }

    public List<ClusterEventDto> GetEvents(DateTime? sinceUtc, int limit, DataStoreService db)
    {
        var allEvents = db.GetAll<ClusterEventDto>("cluster_events");
        var query = allEvents.AsEnumerable();
        if (sinceUtc.HasValue)
        {
            query = query.Where(e => e.TimestampUtc >= sinceUtc.Value);
        }
        return query
            .OrderBy(e => e.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToList();
    }

    public void BroadcastEvent(ClusterEventDto evt, PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        evt.OriginServerUrl = peerRegistry.SelfUrl;
        string json = JsonSerializer.Serialize(evt, JsonOpts);

        _ = Task.Run(async () =>
        {
            using var client = httpClientFactory.CreateClient();
            foreach (var peerUrl in peerRegistry.PeerUrls)
            {
                if (!string.IsNullOrWhiteSpace(evt.OriginServerUrl) &&
                    string.Equals(peerUrl.TrimEnd('/'), evt.OriginServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{peerUrl.TrimEnd('/')}/api/cluster/event", content);
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ClusterEventService] Replicated event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}");
                    }
                    else
                    {
                        Console.WriteLine($"[ClusterEventService] Failed to replicate event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClusterEventService] Error replicating event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}: {ex.Message}");
                }
            }
        });
    }

    public async Task RunCatchUpSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        var allEvents = db.GetAll<ClusterEventDto>("cluster_events").ToList();
        DateTime? latestUtc = allEvents.Count > 0 ? allEvents.Max(e => e.TimestampUtc) : null;

        using var client = httpClientFactory.CreateClient();
        foreach (var peerUrl in peerRegistry.PeerUrls)
        {
            try
            {
                var distClient = new DistributionClient(peerUrl, client);
                var events = await distClient.GetClusterEventsAsync(latestUtc, 200);
                foreach (var evt in events)
                {
                    await ApplyEventAsync(evt, db, cas, adminPublicKeys);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClusterEventService] Error catching up from peer {peerUrl}: {ex.Message}");
            }
        }
    }

    public async Task<bool> ApplyEventAsync(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (string.IsNullOrWhiteSpace(evt.EventId) || string.IsNullOrWhiteSpace(evt.EventType))
        {
            return false;
        }

        if (HasEventBeenProcessed(evt.EventId, db))
        {
            return true;
        }

        bool success = evt.EventType.ToLowerInvariant() switch
        {
            "map_published" => ApplyMapPublished(evt, db, cas),
            "creator_registered" => ApplyCreatorRegistered(evt, db, adminPublicKeys),
            "admin_greenlight" => ApplyAdminGreenlight(evt, db, adminPublicKeys),
            "map_metric_report" => ApplyMapMetricReport(evt, db),
            _ => false
        };

        if (success)
        {
            RecordEvent(evt, db);
        }

        return await Task.FromResult(success);
    }

    public ClusterStateDigestDto ComputeStateDigest(DataStoreService db)
    {
        var colHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var colCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var creators = db.GetAllWithKeys<JsonDocument>("creators");
        colCounts["creators"] = creators.Count;
        var creatorSb = new StringBuilder();
        foreach (var key in creators.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var root = creators[key].RootElement;
            string un = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string pk = root.TryGetProperty("public_key", out var p) ? p.GetString() ?? "" : "";
            string dl = root.TryGetProperty("donation_link", out var d) ? d.GetString() ?? "" : "";
            string ci = root.TryGetProperty("contact_info", out var c) ? c.GetString() ?? "" : "";
            creatorSb.Append($"{key}:{un}:{pk}:{dl}:{ci};");
        }
        colHashes["creators"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(creatorSb.ToString()), ".txt");

        var nameLocks = db.GetAllWithKeys<JsonDocument>("name_locks");
        colCounts["name_locks"] = nameLocks.Count;
        var lockSb = new StringBuilder();
        foreach (var key in nameLocks.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var root = nameLocks[key].RootElement;
            string un = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string op = root.TryGetProperty("owner_public_key", out var o) ? o.GetString() ?? "" : "";
            lockSb.Append($"{key}:{un}:{op};");
        }
        colHashes["name_locks"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(lockSb.ToString()), ".txt");

        var mapOwnership = db.GetAllWithKeys<string>("map_ownership");
        colCounts["map_ownership"] = mapOwnership.Count;
        var ownSb = new StringBuilder();
        foreach (var key in mapOwnership.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            ownSb.Append($"{key}:{mapOwnership[key]};");
        }
        colHashes["map_ownership"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(ownSb.ToString()), ".txt");

        var publishedMaps = db.GetAllWithKeys<JsonDocument>("published_maps");
        colCounts["published_maps"] = publishedMaps.Count;
        var mapSb = new StringBuilder();
        foreach (var key in publishedMaps.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            string json = publishedMaps[key].RootElement.GetRawText();
            mapSb.Append($"{key}:{json};");
        }
        colHashes["published_maps"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(mapSb.ToString()), ".txt");

        var mapStats = db.GetAllWithKeys<MapStats>("map_stats");
        colCounts["map_stats"] = mapStats.Count;
        var statSb = new StringBuilder();
        foreach (var key in mapStats.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var st = mapStats[key];
            statSb.Append($"{key}:{st.TotalPlaytimeMinutes:F1}:{st.TotalGamesPlayed}:{st.ReviewsCount}:{st.TotalStars}:{st.VerifiedGoodReviewsCount}:{st.AdminOverrideGreenlit};");
        }
        colHashes["map_stats"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(statSb.ToString()), ".txt");

        var assetSignatures = db.GetAllWithKeys<JsonDocument>("asset_signatures");
        colCounts["asset_signatures"] = assetSignatures.Count;
        var sigSb = new StringBuilder();
        foreach (var key in assetSignatures.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var root = assetSignatures[key].RootElement;
            string h = root.TryGetProperty("Hash", out var hp) ? hp.GetString() ?? "" : "";
            string a = root.TryGetProperty("AuthorUsername", out var ap) ? ap.GetString() ?? "" : "";
            string s = root.TryGetProperty("Signature", out var sp) ? sp.GetString() ?? "" : "";
            string pk = root.TryGetProperty("PublicKey", out var pkp) ? pkp.GetString() ?? "" : "";
            sigSb.Append($"{key}:{h}:{a}:{s}:{pk};");
        }
        colHashes["asset_signatures"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(sigSb.ToString()), ".txt");

        var playerEngagements = db.GetAllWithKeys<PlayerMapEngagement>("player_engagement");
        colCounts["player_engagement"] = playerEngagements.Count;
        var engSb = new StringBuilder();
        foreach (var key in playerEngagements.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var en = playerEngagements[key];
            engSb.Append($"{key}:{en.PlayerId}:{en.TotalPlaytimeMinutes:F1}:{en.GamesPlayed}:{en.SubmittedRating}:{en.IsVerifiedAccount};");
        }
        colHashes["player_engagement"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(engSb.ToString()), ".txt");

        var rootSb = new StringBuilder();
        foreach (var colName in colHashes.Keys.OrderBy(c => c, StringComparer.Ordinal))
        {
            rootSb.Append($"{colName}:{colHashes[colName]};");
        }
        string rootDigest = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(rootSb.ToString()), ".txt");

        return new ClusterStateDigestDto
        {
            StateDigest = rootDigest,
            CollectionHashes = colHashes,
            CollectionCounts = colCounts,
            ServerTimestampUtc = DateTime.UtcNow
        };
    }

    public ClusterSnapshotDto GenerateSnapshot(DataStoreService db)
    {
        var digest = ComputeStateDigest(db);

        var creators = new List<CreatorSyncDto>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("creators"))
        {
            var root = pair.Value.RootElement;
            creators.Add(new CreatorSyncDto
            {
                PublicKey = pair.Key,
                Username = root.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
                DonationLink = root.TryGetProperty("donation_link", out var dl) ? dl.GetString() ?? "" : "",
                ContactInfo = root.TryGetProperty("contact_info", out var ci) ? ci.GetString() ?? "" : ""
            });
        }

        var publishedMaps = new List<PublishedMapSyncDto>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
        {
            publishedMaps.Add(new PublishedMapSyncDto
            {
                MapId = pair.Key,
                ManifestJson = pair.Value.RootElement.GetRawText()
            });
        }

        var mapStats = new List<MapStatsSyncDto>();
        foreach (var pair in db.GetAllWithKeys<MapStats>("map_stats"))
        {
            var st = pair.Value;
            mapStats.Add(new MapStatsSyncDto
            {
                MapId = pair.Key,
                TotalPlaytimeMinutes = (int)Math.Round(st.TotalPlaytimeMinutes),
                TotalGamesPlayed = st.TotalGamesPlayed,
                TotalReviewsCount = st.ReviewsCount,
                VerifiedGoodReviewsCount = st.VerifiedGoodReviewsCount,
                AverageRating = st.AverageRating,
                AdminOverrideGreenlit = st.AdminOverrideGreenlit
            });
        }

        var nameLocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("name_locks"))
        {
            nameLocks[pair.Key] = pair.Value.RootElement.GetRawText();
        }

        var assetSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("asset_signatures"))
        {
            assetSignatures[pair.Key] = pair.Value.RootElement.GetRawText();
        }

        var playerEngagement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<PlayerMapEngagement>("player_engagement"))
        {
            playerEngagement[pair.Key] = JsonSerializer.Serialize(pair.Value, JsonOpts);
        }

        return new ClusterSnapshotDto
        {
            StateDigest = digest.StateDigest,
            GeneratedAtUtc = DateTime.UtcNow,
            Creators = creators,
            PublishedMaps = publishedMaps,
            MapOwnership = db.GetAllWithKeys<string>("map_ownership"),
            MapStats = mapStats,
            NameLocks = nameLocks,
            AssetSignatures = assetSignatures,
            PlayerEngagement = playerEngagement
        };
    }

    public ApplySnapshotResponseDto ApplySnapshot(ClusterSnapshotDto snapshot, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (snapshot == null)
        {
            return new ApplySnapshotResponseDto { Success = false, Message = "Snapshot was null." };
        }

        int creatorsCount = 0;
        int mapsCount = 0;
        int statsCount = 0;

        foreach (var creator in snapshot.Creators)
        {
            if (string.IsNullOrWhiteSpace(creator.PublicKey)) continue;
            var creatorDoc = JsonSerializer.SerializeToDocument(new
            {
                username = creator.Username,
                public_key = creator.PublicKey,
                donation_link = creator.DonationLink ?? "",
                contact_info = creator.ContactInfo ?? "",
                registered_at = creator.RegisteredAt ?? DateTime.UtcNow
            });
            db.Upsert("creators", creator.PublicKey, creatorDoc);
            creatorsCount++;
        }

        foreach (var pair in snapshot.NameLocks)
        {
            try
            {
                var doc = JsonDocument.Parse(pair.Value);
                db.Upsert("name_locks", pair.Key, doc);
            }
            catch { }
        }

        foreach (var pair in snapshot.MapOwnership)
        {
            db.Upsert("map_ownership", pair.Key, pair.Value);
        }

        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

        foreach (var map in snapshot.PublishedMaps)
        {
            if (string.IsNullOrWhiteSpace(map.MapId) || string.IsNullOrWhiteSpace(map.ManifestJson)) continue;
            try
            {
                var doc = JsonDocument.Parse(map.ManifestJson);
                db.Upsert("published_maps", map.MapId, doc);

                string manifestPath = Path.Combine(manifestDir, $"{map.MapId}_manifest.json");
                File.WriteAllText(manifestPath, map.ManifestJson);

                if (!string.IsNullOrWhiteSpace(map.MapTitle))
                {
                    string defaultPath = Path.Combine(manifestDir, $"{map.MapTitle}_manifest.json");
                    File.WriteAllText(defaultPath, map.ManifestJson);
                }

                mapsCount++;
            }
            catch { }
        }

        foreach (var stat in snapshot.MapStats)
        {
            if (string.IsNullOrWhiteSpace(stat.MapId)) continue;
            var s = new MapStats
            {
                TotalPlaytimeMinutes = stat.TotalPlaytimeMinutes,
                TotalGamesPlayed = stat.TotalGamesPlayed,
                ReviewsCount = stat.TotalReviewsCount,
                VerifiedGoodReviewsCount = stat.VerifiedGoodReviewsCount,
                TotalStars = (int)Math.Round(stat.AverageRating * stat.TotalReviewsCount),
                AdminOverrideGreenlit = stat.AdminOverrideGreenlit
            };
            db.Upsert("map_stats", stat.MapId, s);
            statsCount++;
        }

        foreach (var pair in snapshot.AssetSignatures)
        {
            try
            {
                var doc = JsonDocument.Parse(pair.Value);
                db.Upsert("asset_signatures", pair.Key, doc);
            }
            catch { }
        }

        foreach (var pair in snapshot.PlayerEngagement)
        {
            try
            {
                var eng = JsonSerializer.Deserialize<PlayerMapEngagement>(pair.Value, JsonOpts);
                if (eng != null)
                {
                    db.Upsert("player_engagement", pair.Key, eng);
                }
            }
            catch { }
        }

        var localDigest = ComputeStateDigest(db);
        bool digestMatches = string.Equals(localDigest.StateDigest, snapshot.StateDigest, StringComparison.OrdinalIgnoreCase);

        return new ApplySnapshotResponseDto
        {
            Success = true,
            ComputedStateDigest = localDigest.StateDigest,
            ExpectedStateDigest = snapshot.StateDigest,
            CreatorsImported = creatorsCount,
            MapsImported = mapsCount,
            StatsImported = statsCount,
            Message = digestMatches ? "Snapshot applied successfully with matching state digest." : $"Snapshot applied with digest mismatch: computed {localDigest.StateDigest}, expected {snapshot.StateDigest}."
        };
    }

    public async Task RunQuorumSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        var localDigest = ComputeStateDigest(db);
        var peerDigests = new ConcurrentDictionary<string, ClusterStateDigestDto>(StringComparer.OrdinalIgnoreCase);

        using var client = httpClientFactory.CreateClient();

        var pollTasks = peerRegistry.PeerUrls.Select(async peerUrl =>
        {
            try
            {
                var distClient = new DistributionClient(peerUrl, client);
                var digest = await distClient.GetStateDigestAsync();
                if (digest != null && !string.IsNullOrEmpty(digest.StateDigest))
                {
                    peerDigests[peerUrl] = digest;
                }
            }
            catch
            {
            }
        });

        await Task.WhenAll(pollTasks);

        if (peerDigests.Count == 0)
        {
            return;
        }

        var digestGroups = peerDigests.GroupBy(p => p.Value.StateDigest, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (digestGroups.Count == 0) return;

        var majorityGroup = digestGroups[0];
        string majorityDigest = majorityGroup.Key;
        int majorityCount = majorityGroup.Count();
        int totalResponding = peerDigests.Count;

        bool hasMajority = majorityCount > (totalResponding / 2);

        if (!hasMajority)
        {
            Console.WriteLine($"[QuorumSync] No clear majority state digest reached ({majorityCount}/{totalResponding} votes). Preserving current state.");
            return;
        }

        if (string.Equals(localDigest.StateDigest, majorityDigest, StringComparison.OrdinalIgnoreCase))
        {
            await RunCatchUpSyncAsync(peerRegistry, httpClientFactory, db, cas, adminPublicKeys);
            return;
        }

        Console.WriteLine($"[QuorumSync] Local state digest ({localDigest.StateDigest}) diverged from majority consensus ({majorityDigest}, {majorityCount}/{totalResponding} votes). Fetching snapshot...");

        var candidatePeer = majorityGroup.First().Key;
        try
        {
            var candidateClient = new DistributionClient(candidatePeer, client);
            var snapshot = await candidateClient.GetSnapshotAsync();
            if (snapshot != null)
            {
                var result = ApplySnapshot(snapshot, db, cas, adminPublicKeys);
                Console.WriteLine($"[QuorumSync] Self-healing snapshot applied from {candidatePeer}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QuorumSync] Error applying snapshot from {candidatePeer}: {ex.Message}");
        }
    }

    public void PruneOldEvents(DataStoreService db, TimeSpan retentionWindow)
    {
        DateTime cutoffUtc = DateTime.UtcNow - retentionWindow;
        var allEvents = db.GetAll<ClusterEventDto>("cluster_events").ToList();
        int pruned = 0;
        foreach (var evt in allEvents)
        {
            if (evt.TimestampUtc < cutoffUtc)
            {
                db.Delete("cluster_events", evt.EventId);
                _processedEvents.TryRemove(evt.EventId, out _);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            Console.WriteLine($"[ClusterEventService] Pruned {pruned} cluster events older than {retentionWindow.TotalHours:F0} hours.");
        }
    }

    public CasPruneResponseDto PruneCas(ContentAddressableStorage cas, DataStoreService db)
    {
        int totalScanned = 0;
        int orphansPruned = 0;
        int corruptPruned = 0;
        long bytesFreed = 0;

        var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var publishedMaps = db.GetAll<JsonDocument>("published_maps");
        foreach (var doc in publishedMaps)
        {
            try
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("Files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var fileProp in filesProp.EnumerateObject())
                    {
                        string h = ContentAddressableStorage.NormalizeBlake3Hash(fileProp.Value.GetString() ?? "");
                        if (!string.IsNullOrEmpty(h)) referencedHashes.Add(h);
                    }
                }
            }
            catch { }
        }

        var manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (Directory.Exists(manifestDir))
        {
            foreach (var file in Directory.EnumerateFiles(manifestDir, "*_manifest.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var fileProp in filesProp.EnumerateObject())
                        {
                            string h = ContentAddressableStorage.NormalizeBlake3Hash(fileProp.Value.GetString() ?? "");
                            if (!string.IsNullOrEmpty(h)) referencedHashes.Add(h);
                        }
                    }
                }
                catch { }
            }
        }

        var assetDirs = new List<string> { cas.RootDirectory, ".data/assets" };
        foreach (var dir in assetDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var filePath in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(fileName);
                if (string.IsNullOrEmpty(normalizedHash) || normalizedHash.Length < 32) continue;

                totalScanned++;
                long fileSize = 0;
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    fileSize = fileInfo.Length;
                    byte[] bytes = File.ReadAllBytes(filePath);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    string computedHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(bytes, ext));

                    if (!string.Equals(computedHash, normalizedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(filePath);
                        corruptPruned++;
                        bytesFreed += fileSize;
                        continue;
                    }

                    if (!referencedHashes.Contains(normalizedHash))
                    {
                        File.Delete(filePath);
                        orphansPruned++;
                        bytesFreed += fileSize;
                    }
                }
                catch { }
            }
        }

        return new CasPruneResponseDto
        {
            Success = true,
            TotalScanned = totalScanned,
            OrphansPruned = orphansPruned,
            CorruptPruned = corruptPruned,
            BytesFreed = bytesFreed,
            Message = $"Prune completed: {totalScanned} scanned, {orphansPruned} orphans deleted, {corruptPruned} corrupt files deleted, {bytesFreed} bytes freed."
        };
    }

    private bool ApplyMapPublished(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas)
    {
        MapPublishedEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<MapPublishedEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string manifestJson = payload?.ManifestJson ?? "";
        string publicKey = payload?.PublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;
        string mapTitle = payload?.MapTitle ?? "";
        string mapVersion = payload?.MapVersion ?? "1.0";

        if (string.IsNullOrWhiteSpace(manifestJson) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                if (doc.RootElement.TryGetProperty("MapName", out var mn)) mapTitle = mn.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("Version", out var mv)) mapVersion = mv.GetString() ?? "1.0";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return false;
        }

        byte[] mapBytes = Encoding.UTF8.GetBytes(manifestJson);
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
        string mapHashStr = $"{canonicalBlake3}.json";

        bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, mapHashStr, signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, canonicalBlake3, signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, manifestJson, signature);

        if (!sigValid)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Invalid signature for key {publicKey}");
            return false;
        }

        var existingOwner = db.Get<string>("map_ownership", mapTitle);
        if (existingOwner != null && !string.Equals(existingOwner, publicKey, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is owned by another key");
            return false;
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var stats = db.Get<MapStats>("map_stats", compositeKey) ?? db.Get<MapStats>("map_stats", mapTitle);
        if (stats == null || !stats.IsGreenlit)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is not greenlit");
            return false;
        }

        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

        string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
        File.WriteAllText(manifestPath, manifestJson);
        string defaultPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
        File.WriteAllText(defaultPath, manifestJson);

        try
        {
            var doc = JsonDocument.Parse(manifestJson);
            db.Upsert("published_maps", compositeKey, doc);
            db.Upsert("published_maps", mapTitle, doc);
            db.Upsert("map_ownership", mapTitle, publicKey);
        }
        catch
        {
            return false;
        }

        return true;
    }

    private bool ApplyCreatorRegistered(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        CreatorRegisteredEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<CreatorRegisteredEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string username = payload?.Username ?? "";
        string publicKey = payload?.PublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;
        string? donationLink = payload?.DonationLink;
        string? contactInfo = payload?.ContactInfo;
        string? adminBypassToken = payload?.AdminBypassToken;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, $"{username}:{publicKey}", signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, username, signature);

        if (!sigValid)
        {
            Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Invalid signature for {username}");
            return false;
        }

        string slug = username.ToLowerInvariant().Replace(" ", "-");
        var existingLock = db.Get<JsonDocument>("name_locks", slug);
        if (existingLock != null)
        {
            var root = existingLock.RootElement;
            string owner = root.TryGetProperty("owner_public_key", out var op) ? op.GetString() ?? "" : "";
            if (!string.Equals(owner, publicKey, StringComparison.OrdinalIgnoreCase))
            {
                bool bypassValid = !string.IsNullOrEmpty(adminBypassToken)
                    && adminPublicKeys.Count > 0
                    && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "override", adminBypassToken);

                if (!bypassValid)
                {
                    Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Username '{username}' already registered");
                    return false;
                }
            }
        }

        var lockDoc = JsonSerializer.SerializeToDocument(new
        {
            username = username,
            owner_public_key = publicKey,
            registered_at = payload?.RegisteredAt ?? DateTime.UtcNow
        });
        db.Upsert("name_locks", slug, lockDoc);

        var creatorDoc = JsonSerializer.SerializeToDocument(new
        {
            username = username,
            public_key = publicKey,
            donation_link = donationLink ?? "",
            contact_info = contactInfo ?? "",
            registered_at = payload?.RegisteredAt ?? DateTime.UtcNow
        });
        db.Upsert("creators", publicKey, creatorDoc);

        return true;
    }

    private bool ApplyAdminGreenlight(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        AdminGreenlightEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<AdminGreenlightEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string mapTitle = payload?.MapTitle ?? "";
        string mapVersion = payload?.MapVersion ?? "1.0";
        string adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;

        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(adminPublicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (adminPublicKeys.Count == 0 || !adminPublicKeys.Contains(adminPublicKey.Trim()))
        {
            Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Unauthorized admin key {adminPublicKey}");
            return false;
        }

        string sigPayload = $"greenlight:{mapTitle.ToLowerInvariant()}:{mapVersion.ToLowerInvariant()}";
        if (!AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload, signature))
        {
            Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Invalid admin signature");
            return false;
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var stats = db.Get<MapStats>("map_stats", compositeKey)
            ?? db.Get<MapStats>("map_stats", mapTitle)
            ?? new MapStats();

        stats.AdminOverrideGreenlit = true;

        db.Upsert("map_stats", compositeKey, stats);
        db.Upsert("map_stats", mapTitle, stats);

        return true;
    }

    private bool ApplyMapMetricReport(ClusterEventDto evt, DataStoreService db)
    {
        MapMetricReportEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<MapMetricReportEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.MapTitle))
        {
            return false;
        }

        string mapTitle = payload.MapTitle.Trim();
        string mapVersion = string.IsNullOrWhiteSpace(payload.MapVersion) ? "1.0" : payload.MapVersion.Trim();
        string compositeKey = $"{mapTitle}_{mapVersion}";
        string playerIdentifier = !string.IsNullOrWhiteSpace(payload.PlayerId) ? payload.PlayerId.Trim() : "Anonymous";

        string engagementKey = $"{playerIdentifier}_{compositeKey}".ToLowerInvariant();
        var engagement = db.Get<PlayerMapEngagement>("player_engagement", engagementKey)
            ?? new PlayerMapEngagement { PlayerId = playerIdentifier };

        bool wasVerifiedGood = engagement.IsVerifiedGoodReview;
        bool hadSubmittedRating = engagement.SubmittedRating.HasValue;
        int prevRating = engagement.SubmittedRating ?? 0;

        engagement.TotalPlaytimeMinutes += Math.Max(0.0, payload.PlaytimeMinutes);
        if (payload.IsCompleteGame)
        {
            engagement.GamesPlayed += 1;
        }
        if (!string.IsNullOrWhiteSpace(payload.AuthProvider) && !payload.AuthProvider.Equals("guest", StringComparison.OrdinalIgnoreCase))
        {
            engagement.IsVerifiedAccount = true;
        }

        var stats = db.Get<MapStats>("map_stats", compositeKey)
            ?? db.Get<MapStats>("map_stats", mapTitle)
            ?? new MapStats();

        stats.TotalPlaytimeMinutes += Math.Max(0.0, payload.PlaytimeMinutes);
        if (payload.IsCompleteGame)
        {
            stats.TotalGamesPlayed += 1;
        }

        if (payload.Stars >= 1 && payload.Stars <= 5)
        {
            if (hadSubmittedRating)
            {
                stats.TotalStars += (payload.Stars - prevRating);
            }
            else
            {
                stats.ReviewsCount += 1;
                stats.TotalStars += payload.Stars;
            }
            engagement.SubmittedRating = payload.Stars;
        }

        bool isNowVerifiedGood = engagement.IsVerifiedGoodReview;
        if (!wasVerifiedGood && isNowVerifiedGood)
        {
            stats.VerifiedGoodReviewsCount += 1;
        }
        else if (wasVerifiedGood && !isNowVerifiedGood)
        {
            stats.VerifiedGoodReviewsCount = Math.Max(0, stats.VerifiedGoodReviewsCount - 1);
        }

        db.Upsert("player_engagement", engagementKey, engagement);
        db.Upsert("map_stats", compositeKey, stats);
        db.Upsert("map_stats", mapTitle, stats);

        return true;
    }
}
