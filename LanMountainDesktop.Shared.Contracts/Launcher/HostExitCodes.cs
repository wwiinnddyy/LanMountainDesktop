namespace LanMountainDesktop.Shared.Contracts.Launcher;

/// <summary>
/// Standardized host process exit codes consumed by the launcher.
/// </summary>
public static class HostExitCodes
{
    public const int Success = 0;

    // Legacy host-side activation result retained for old builds and launcher compatibility.
    public const int SecondaryActivationSucceeded = 12;

    // Legacy host-side activation failure retained for old builds and launcher compatibility.
    public const int SecondaryActivationFailed = 13;

    // Legacy restart lock failure retained for old builds and launcher compatibility.
    public const int RestartLockNotAcquired = 14;
}
