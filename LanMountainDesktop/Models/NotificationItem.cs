using System;

namespace LanMountainDesktop.Models;

/// <summary>
/// Notification captured by the desktop notification box.
/// </summary>
public sealed class NotificationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string AppId { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    public string? AppIconPath { get; set; }

    public byte[]? AppIconBytes { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime ReceivedTime { get; set; } = DateTime.Now;

    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRead { get; set; }

    public string? LaunchArgs { get; set; }

    public string Platform { get; set; } = "Unknown";

    public string? SourceNotificationId { get; set; }

    public string? DesktopEntryId { get; set; }

    public string? Aumid { get; set; }

    public string? LaunchTarget { get; set; }

    public bool CanActivate { get; set; }

    public string CaptureMode { get; set; } = "Unknown";
}
