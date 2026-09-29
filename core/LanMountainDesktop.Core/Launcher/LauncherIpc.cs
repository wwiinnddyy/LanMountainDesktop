namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum StartupStage
{
    Initializing,
    LoadingSettings,
    LoadingAirApps,
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
    public const string LauncherPidEnvVar = "LMD_LAUNCHER_PID";

    public const string PackageRootEnvVar = "LMD_PACKAGE_ROOT";

    public const string VersionEnvVar = "LMD_VERSION";

    public const string CodenameEnvVar = "LMD_CODENAME";

    public const string LaunchSourceOptionName = "launch-source";

    public const string RestartParentPidOptionName = "restart-parent-pid";

    public const string RestartPresentationOptionName = "restart-presentation";
}

/// <summary>
/// "这次启动是怎么来的"这张跨进程词表只住这里。写的一侧是宿主的重启路径
/// （<c>--launch-source=restart</c>）、安装后的首启与预览入口，读的一侧有六个地方：
/// 启动器判"要不要先探已存在的宿主"、判 GUI 命令走哪条、记启动成败、OOBE 跳过判定，
/// 以及宿主自己判"这次是不是重启"。这些点在收口前各写各的字面量，
/// 拼错一个字母不报错——症状是那条判定永远不成立，而看起来一切正常
/// （最狠的一处是 <c>restart</c>：它对不上时启动器会去唤醒那个正在退出的旧宿主，
/// 用户点"重启"就变成"什么也没发生"）。
///
/// 值本身是**对外契约**：命令行与 IPC 里传的就是这些串，改口径要连调用方一起改。
/// </summary>
public static class LauncherLaunchSources
{
    public const string Normal = "normal";

    public const string Restart = "restart";

    public const string PostInstall = "postinstall";

    public const string PluginInstall = "plugin-install";

    public const string DebugPreview = "debug-preview";
}
