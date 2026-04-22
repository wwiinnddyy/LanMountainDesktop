namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum StartupStage
{
    Initializing,
    LoadingSettings,
    LoadingPlugins,
    TrayReady,
    InitializingUI,
    ShellInitialized,
    BackgroundReady,
    DesktopVisible,
    ActivationRedirected,
    ActivationFailed,
    Ready
}

public record StartupProgressMessage
{
    public StartupStage Stage { get; init; }

    public int ProgressPercent { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public static class LauncherIpcConstants
{
    public const string PipeName = "LanMountainDesktop_Launcher";

    public const string LauncherPidEnvVar = "LMD_LAUNCHER_PID";

    public const string PackageRootEnvVar = "LMD_PACKAGE_ROOT";

    public const string VersionEnvVar = "LMD_VERSION";

    public const string CodenameEnvVar = "LMD_CODENAME";

    public const string LaunchSourceOptionName = "launch-source";

    public const string RestartParentPidOptionName = "restart-parent-pid";

    public const string RestartPresentationOptionName = "restart-presentation";
}
