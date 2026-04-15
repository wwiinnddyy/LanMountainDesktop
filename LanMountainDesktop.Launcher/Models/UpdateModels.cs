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
