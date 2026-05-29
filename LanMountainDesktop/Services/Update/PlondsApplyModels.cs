using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Update;

internal sealed class ApplyUpdateResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = "ok";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public string? CurrentVersion { get; init; }

    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; init; }

    [JsonPropertyName("rolledBackTo")]
    public string? RolledBackTo { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}

internal sealed class ApplySnapshotMetadata
{
    public string SnapshotId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string? TargetVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string? TargetDirectory { get; set; }
    public string Status { get; set; } = "pending";
}

internal sealed class ApplyInstallCheckpoint
{
    public string SnapshotId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string? TargetVersion { get; set; }
    public string? SourceDirectory { get; set; }
    public string TargetDirectory { get; set; } = string.Empty;
    public bool IsInitialDeployment { get; set; }
    public int AppliedCount { get; set; }
    public int VerifiedCount { get; set; }
}

internal sealed class ApplyPlondsUpdateMetadata
{
    public string? DistributionId { get; set; }
    public string? Channel { get; set; }
    public string? SubChannel { get; set; }
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public string? FileMapPath { get; set; }
    public string? FileMapSignaturePath { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

internal sealed class ApplyPlondsFileMap
{
    public string? DistributionId { get; set; }
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public string? Version { get; set; }
    public string? Platform { get; set; }
    public string? Arch { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    public List<ApplyPlondsComponentEntry> Components { get; set; } = [];
    public List<ApplyPlondsFileEntry> Files { get; set; } = [];
}

internal sealed class ApplyPlondsComponentEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    public List<ApplyPlondsFileEntry> Files { get; set; } = [];
}

internal sealed class ApplyPlondsFileEntry
{
    public string Path { get; set; } = string.Empty;
    public string? Action { get; set; } = "replace";
    public string? Url { get; set; }
    public string? ObjectUrl { get; set; }
    public string? ObjectPath { get; set; }
    public string? ObjectKey { get; set; }
    public string? ArchivePath { get; set; }
    public string? Sha256 { get; set; }
    public string? Sha512 { get; set; }
    public string? Sha512Base64 { get; set; }
    public byte[]? Sha512Bytes { get; set; }
    public ApplyPlondsHashDescriptor? Hash { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

internal sealed class ApplyPlondsHashDescriptor
{
    public string? Algorithm { get; set; }
    public string? Value { get; set; }
    public byte[]? Bytes { get; set; }
}
