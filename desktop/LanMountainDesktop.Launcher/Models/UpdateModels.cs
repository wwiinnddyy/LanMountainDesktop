namespace LanMountainDesktop.Launcher.Models;

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
