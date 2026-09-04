using System;
using System.Collections.Generic;

namespace Realm.Shared.Distribution;

public class SeederNodeDto
{
    public string SeederId { get; set; } = string.Empty;
    public string IP { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public int CapacityPercentage { get; set; } = 100;
    public bool AcceptingUploads { get; set; } = true;
    public int StoredAssetCount { get; set; }
    public List<string> MapIds { get; set; } = new();
}

public class SeederRegisterRequestDto
{
    public string SeederId { get; set; } = string.Empty;
    public string ReportedIP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int CapacityPercentage { get; set; } = 100;
    public bool AcceptingUploads { get; set; } = true;
    public List<string> MapIds { get; set; } = new();
}

public class SeederCatalogResponseDto
{
    public string SeederId { get; set; } = string.Empty;
    public int CapacityPercentage { get; set; } = 100;
    public List<string> AssetHashes { get; set; } = new();
}

public class AssetUploadResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Deduplicated { get; set; }
    public bool Merged { get; set; }
    public string Blake3Hash { get; set; } = string.Empty;
}

public class MapPublishResponseDto
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> MissingAssetHashes { get; set; } = new();
}

public class HeaderSyncRequestDto
{
    public string Blake3Hash { get; set; } = string.Empty;
    public string? MetadataHeadersJson { get; set; }
    public string? AuthorPublicKey { get; set; }
    public string? AuthorSignature { get; set; }
}

public class HeaderSyncResponseDto
{
    public bool Updated { get; set; }
    public string CurrentMetadataHeadersJson { get; set; } = string.Empty;
}

public class BloomHeadersResponseDto
{
    public string SeederId { get; set; } = string.Empty;
    public int BitCount { get; set; }
    public int HashCount { get; set; }
    public int ItemCount { get; set; }
    public string FilterDataBase64 { get; set; } = string.Empty;
}
