namespace LanMountainDesktop.Shared.Contracts.Launcher;

/// <summary>
/// 应用版本信息
/// </summary>
public record AppVersionInfo
{
    /// <summary>
    /// 版本号，如 "1.0.0"
    /// </summary>
    public string Version { get; init; } = "0.0.0";
    
    /// <summary>
    /// 开发代号，如 "Administrate"
    /// </summary>
    public string Codename { get; init; } = "Unknown";
    
    /// <summary>
    /// 完整版本字符串，如 "1.0.0 (Administrate)"
    /// </summary>
    public string FullVersionText => $"{Version} ({Codename})";
}
