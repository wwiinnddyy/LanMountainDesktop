namespace LanMountainDesktop.Launcher.Models;

/// <summary>
/// 隐私协议同意状态模型（带防篡改保护）
/// </summary>
public class PrivacyAgreementState
{
    /// <summary>
    /// 用户是否同意隐私协议
    /// </summary>
    public bool IsAgreed { get; set; } = false;

    /// <summary>
    /// 同意时间（UTC）
    /// </summary>
    public DateTime AgreedAtUtc { get; set; }

    /// <summary>
    /// 同意的协议版本
    /// </summary>
    public string AgreementVersion { get; set; } = "1.0";

    /// <summary>
    /// 用户标识（匿名）
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 设备标识
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 数据完整性校验哈希（HMAC-SHA256）
    /// </summary>
    public string IntegrityHash { get; set; } = string.Empty;

    /// <summary>
    /// 用于生成哈希的随机盐值
    /// </summary>
    public string Salt { get; set; } = string.Empty;
}
