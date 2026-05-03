namespace LanMountainDesktop.Shared.Contracts.Update;

public sealed record InstallProgressReport(
    InstallStage Stage,
    string Message,
    int ProgressPercent,
    string? CurrentFile,
    int FilesCompleted,
    int FilesTotal)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record InstallCompleteReport(
    bool Success,
    string? FromVersion,
    string? ToVersion,
    string? ErrorMessage,
    bool WasRolledBack)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DownloadProgressReport(
    string CurrentFile,
    long BytesDownloaded,
    long BytesTotal,
    double BytesPerSecond,
    int FilesCompleted,
    int FilesTotal,
    double OverallFraction)
{
    public int OverallPercent => (int)Math.Clamp(OverallFraction * 100, 0, 100);
}

public sealed record UpdateProgressReport(
    UpdatePhase Phase,
    string Message,
    double ProgressFraction,
    DownloadProgressReport? DownloadDetail,
    InstallProgressReport? InstallDetail)
{
    public int ProgressPercent => (int)Math.Clamp(ProgressFraction * 100, 0, 100);
}

public sealed record UpdateCheckReport(
    bool IsUpdateAvailable,
    string? LatestVersion,
    string? CurrentVersion,
    UpdatePayloadKind? PayloadKind,
    string? DistributionId,
    string? Channel,
    DateTimeOffset? PublishedAt,
    long? TotalDownloadBytes,
    long? FullInstallerBytes,
    string? ErrorMessage);

public sealed record InstallRequest(
    UpdatePayloadKind PayloadKind,
    string LauncherRoot,
    string? LaunchSource = null);

public sealed record LaunchResult(
    bool Success,
    string? ErrorMessage,
    int? ProcessId);
