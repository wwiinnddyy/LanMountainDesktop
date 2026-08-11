namespace LanMountainDesktop.Launcher.Models;

/// <summary>
/// 隐私配置模型
/// </summary>
public class PrivacyConfig
{
    /// <summary>
    /// 是否启用崩溃报告遥测
    /// </summary>
    public bool CrashTelemetryEnabled { get; set; } = true;

    /// <summary>
    /// 是否启用使用统计遥测
    /// </summary>
    public bool UsageTelemetryEnabled { get; set; } = true;

    /// <summary>
    /// 隐私追踪 ID
    /// </summary>
    public string TelemetryId { get; set; } = string.Empty;
}
