using System;

namespace LanMountainDesktop.Models;

/// <summary>
/// 通知项数据模型
/// </summary>
public sealed class NotificationItem
{
    /// <summary>
    /// 唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 应用ID（如 WeChat, Outlook 等）
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// 应用名称
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// 应用图标路径或Base64
    /// </summary>
    public string? AppIconPath { get; set; }

    /// <summary>
    /// 通知标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 通知内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 接收时间
    /// </summary>
    public DateTime ReceivedTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 是否已读
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// 原始通知的额外数据（用于点击跳转）
    /// </summary>
    public string? LaunchArgs { get; set; }
}
