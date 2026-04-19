namespace LanMountainDesktop.Shared.Contracts.Launcher;

/// <summary>
/// Standardized host process exit codes consumed by the launcher.
/// </summary>
public static class HostExitCodes
{
    public const int Success = 0;

    // Secondary instance activated the existing primary instance successfully.
    public const int SecondaryActivationSucceeded = 12;

    // Secondary instance failed to activate the existing primary instance.
    public const int SecondaryActivationFailed = 13;

    // Restart relaunch couldn't acquire the single-instance lock in time.
    public const int RestartLockNotAcquired = 14;
}
