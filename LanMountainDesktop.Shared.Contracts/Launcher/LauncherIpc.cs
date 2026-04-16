namespace LanMountainDesktop.Shared.Contracts.Launcher;

/// <summary>
/// 启动阶段枚举
/// </summary>
public enum StartupStage
{
    /// <summary>
    /// 初始化中
    /// </summary>
    Initializing,
    
    /// <summary>
    /// 加载设置中
    /// </summary>
    LoadingSettings,
    
    /// <summary>
    /// 加载插件中
    /// </summary>
    LoadingPlugins,
    
    /// <summary>
    /// 初始化界面中
    /// </summary>
    InitializingUI,
    
    /// <summary>
    /// 就绪
    /// </summary>
    Ready
}

/// <summary>
/// 启动进度消息
/// </summary>
public record StartupProgressMessage
{
    /// <summary>
    /// 当前阶段
    /// </summary>
    public StartupStage Stage { get; init; }
    
    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public int ProgressPercent { get; init; }
    
    /// <summary>
    /// 状态消息
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Launcher IPC 常量
/// </summary>
public static class LauncherIpcConstants
{
    /// <summary>
    /// 命名管道名称
    /// </summary>
    public const string PipeName = "LanMountainDesktop_Launcher";
    
    /// <summary>
    /// Launcher 进程 ID 环境变量
    /// </summary>
    public const string LauncherPidEnvVar = "LMD_LAUNCHER_PID";
    
    /// <summary>
    /// 包根目录环境变量
    /// </summary>
    public const string PackageRootEnvVar = "LMD_PACKAGE_ROOT";
    
    /// <summary>
    /// 版本环境变量
    /// </summary>
    public const string VersionEnvVar = "LMD_VERSION";
}
