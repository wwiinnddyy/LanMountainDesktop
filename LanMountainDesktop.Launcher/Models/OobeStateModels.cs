namespace LanMountainDesktop.Launcher.Models;

internal enum OobeStateStatus
{
    FirstRun,
    Completed,
    Unavailable,
    Suppressed
}

internal sealed class OobeStateFile
{
    public int SchemaVersion { get; init; } = 1;

    public string CompletedAtUtc { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string? UserSid { get; init; }

    public string LaunchSource { get; init; } = string.Empty;
}

internal sealed class OobeLaunchDecision
{
    public OobeStateStatus Status { get; init; }

    public bool ShouldShowOobe { get; init; }

    public string StatePath { get; init; } = string.Empty;

    public string LaunchSource { get; init; } = "normal";

    public bool IsElevated { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string? UserSid { get; init; }

    public string ResultCode { get; init; } = "ok";

    public string SuppressionReason { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public bool UsedLegacyMarker { get; init; }

    public bool MigratedLegacyMarker { get; init; }
}

internal sealed class OobeCompletionResult
{
    public bool Success { get; init; }

    public string ResultCode { get; init; } = "ok";

    public string ErrorMessage { get; init; } = string.Empty;
}

internal sealed record LauncherExecutionSnapshot(
    bool IsElevated,
    string UserName,
    string? UserSid);
