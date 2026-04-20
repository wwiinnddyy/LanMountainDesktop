namespace LanMountainDesktop.Launcher.Models;

internal sealed class SignedFileMap
{
    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    public string? Platform { get; set; }

    public string? Arch { get; set; }

    public List<UpdateFileEntry> Files { get; set; } = [];
}

internal sealed class UpdateFileEntry
{
    public string Path { get; set; } = string.Empty;

    public string? ArchivePath { get; set; }

    public string Action { get; set; } = "replace";

    public string? Sha256 { get; set; }
}

internal sealed class SnapshotMetadata
{
    public string SnapshotId { get; set; } = string.Empty;

    public string SourceVersion { get; set; } = string.Empty;

    public string? TargetVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string SourceDirectory { get; set; } = string.Empty;

    public string? TargetDirectory { get; set; }

    public string Status { get; set; } = "pending";
}

internal sealed class UpdateApplyResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? FromVersion { get; init; }

    public string? ToVersion { get; init; }

    public string? RolledBackTo { get; init; }
}

internal sealed class PdcUpdateMetadata
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

internal sealed class PdcFileMap
{
    public string? DistributionId { get; set; }

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    public string? Version { get; set; }

    public string? Platform { get; set; }

    public string? Arch { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];

    public List<PdcComponentEntry> Components { get; set; } = [];

    public List<PdcFileEntry> Files { get; set; } = [];
}

internal sealed class PdcComponentEntry
{
    public string Name { get; set; } = string.Empty;

    public string? Version { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];

    public List<PdcFileEntry> Files { get; set; } = [];
}

internal sealed class PdcFileEntry
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

    public PdcHashDescriptor? Hash { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];
}

internal sealed class PdcHashDescriptor
{
    public string? Algorithm { get; set; }

    public string? Value { get; set; }

    public byte[]? Bytes { get; set; }
}
