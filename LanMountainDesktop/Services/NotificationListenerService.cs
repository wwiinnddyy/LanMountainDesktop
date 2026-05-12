using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public enum NotificationBoxServiceState
{
    NotStarted,
    Starting,
    Running,
    WaitingForPermission,
    Unsupported,
    Degraded,
    Failed
}

public sealed record NotificationBoxStatus(
    NotificationBoxServiceState State,
    string Message,
    string CaptureMode,
    bool CanRequestPermission = false);

internal interface IPlatformNotificationListener : IDisposable
{
    Task<NotificationBoxStatus> InitializeAsync(CancellationToken cancellationToken = default);

    Task RequestPermissionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Cross-platform notification aggregation service used by the notification box widget.
/// </summary>
public sealed class NotificationListenerService : IDisposable
{
    private readonly List<NotificationItem> _notifications = [];
    private readonly object _lock = new();
    private readonly ISettingsService _settingsService;
    private readonly CancellationTokenSource _disposeCts = new();
    private IPlatformNotificationListener? _platformListener;
    private NotificationBoxStatus _status = new(
        NotificationBoxServiceState.NotStarted,
        "通知监听尚未启动。",
        "None");

    public event EventHandler<NotificationItem>? NotificationReceived;
    public event EventHandler<string>? NotificationRemoved;
    public event EventHandler<NotificationBoxStatus>? StatusChanged;

    public NotificationListenerService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        SetStatus(new NotificationBoxStatus(NotificationBoxServiceState.Starting, "正在启动通知监听...", "Starting"));

        try
        {
            var settings = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            if (!settings.NotificationBoxEnabled)
            {
                SetStatus(new NotificationBoxStatus(NotificationBoxServiceState.Unsupported, "消息盒子已在设置中关闭。", "Disabled"));
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _platformListener = new WindowsNotificationListener(this);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _platformListener = new LinuxNotificationListener(this, settings.NotificationBoxLinuxCaptureMode);
            }
            else
            {
                SetStatus(new NotificationBoxStatus(
                    NotificationBoxServiceState.Unsupported,
                    "当前平台暂不支持系统通知监听。",
                    "Unsupported"));
                return;
            }

            var status = await _platformListener.InitializeAsync(_disposeCts.Token).ConfigureAwait(false);
            SetStatus(status);
        }
        catch (Exception ex)
        {
            SetStatus(new NotificationBoxStatus(
                NotificationBoxServiceState.Failed,
                $"通知监听初始化失败：{ex.Message}",
                "Failed"));
        }
    }

    public NotificationBoxStatus GetStatus() => _status;

    public async Task RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        if (_platformListener is null)
        {
            await InitializeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await _platformListener.RequestPermissionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus(new NotificationBoxStatus(
                NotificationBoxServiceState.Failed,
                $"请求通知权限失败：{ex.Message}",
                _status.CaptureMode,
                CanRequestPermission: true));
        }
    }

    public void SetStatus(NotificationBoxStatus status)
    {
        _status = status;
        Dispatcher.UIThread.InvokeAsync(() => StatusChanged?.Invoke(this, status));
    }

    public void AddNotification(NotificationItem notification)
    {
        var settings = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        if (!settings.NotificationBoxEnabled)
        {
            return;
        }

        if (settings.NotificationBoxBlockedApps.Contains(notification.AppId, StringComparer.OrdinalIgnoreCase) ||
            settings.NotificationBoxBlockedApps.Contains(notification.AppName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (notification.ReceivedAtUtc == default)
        {
            notification.ReceivedAtUtc = now;
        }

        if (notification.ReceivedTime == default)
        {
            notification.ReceivedTime = notification.ReceivedAtUtc.LocalDateTime;
        }

        lock (_lock)
        {
            var existing = !string.IsNullOrWhiteSpace(notification.SourceNotificationId)
                ? _notifications.FirstOrDefault(n =>
                    string.Equals(n.Platform, notification.Platform, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(n.SourceNotificationId, notification.SourceNotificationId, StringComparison.OrdinalIgnoreCase))
                : null;

            if (existing is not null)
            {
                CopyNotification(notification, existing);
                CleanupOldNotifications(settings);
            }
            else
            {
                _notifications.Add(notification);
                CleanupOldNotifications(settings);
            }
        }

        Dispatcher.UIThread.InvokeAsync(() => NotificationReceived?.Invoke(this, notification));
    }

    public void RemoveNotification(string notificationId)
    {
        var removed = false;
        lock (_lock)
        {
            var notification = _notifications.FirstOrDefault(n =>
                string.Equals(n.Id, notificationId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.SourceNotificationId, notificationId, StringComparison.OrdinalIgnoreCase));
            if (notification != null)
            {
                _notifications.Remove(notification);
                removed = true;
            }
        }

        if (removed)
        {
            Dispatcher.UIThread.InvokeAsync(() => NotificationRemoved?.Invoke(this, notificationId));
        }
    }

    public IReadOnlyList<NotificationItem> GetNotifications()
    {
        lock (_lock)
        {
            return _notifications.ToList().AsReadOnly();
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _notifications.Clear();
        }

        Dispatcher.UIThread.InvokeAsync(() => StatusChanged?.Invoke(this, _status));
    }

    public void MarkAsRead(string notificationId)
    {
        lock (_lock)
        {
            var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
            }
        }
    }

    public int GetUnreadCount()
    {
        lock (_lock)
        {
            return _notifications.Count(n => !n.IsRead);
        }
    }

    public bool TryActivate(NotificationItem notification)
    {
        if (!notification.CanActivate)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return TryLaunchWindows(notification);
        }

        if (OperatingSystem.IsLinux())
        {
            return TryLaunchLinux(notification);
        }

        return false;
    }

    private static bool TryLaunchWindows(NotificationItem notification)
    {
        try
        {
            var target = notification.LaunchTarget;
            if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(notification.Aumid))
            {
                target = $"shell:AppsFolder\\{notification.Aumid}";
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLaunchLinux(NotificationItem notification)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(notification.DesktopEntryId))
            {
                var root = new LinuxDesktopEntryService().Load();
                var entry = EnumerateApps(root).FirstOrDefault(app =>
                    string.Equals(app.RelativePath, notification.DesktopEntryId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(app.RelativePath, $"{notification.DesktopEntryId}.desktop", StringComparison.OrdinalIgnoreCase));
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.LaunchExecutable))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = entry.LaunchExecutable,
                        UseShellExecute = false
                    };
                    foreach (var argument in entry.LaunchArguments)
                    {
                        startInfo.ArgumentList.Add(argument);
                    }
                    if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory))
                    {
                        startInfo.WorkingDirectory = entry.WorkingDirectory;
                    }
                    Process.Start(startInfo);
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(notification.LaunchTarget))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = notification.LaunchTarget,
                    UseShellExecute = true
                });
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void CleanupOldNotifications(AppSettingsSnapshot settings)
    {
        var maxCount = Math.Max(1, settings.NotificationBoxMaxStoredCount);
        while (_notifications.Count > maxCount)
        {
            _notifications.RemoveAt(0);
        }

        var cutoffDate = DateTime.Now.AddDays(-Math.Max(1, settings.NotificationBoxHistoryRetentionDays));
        _notifications.RemoveAll(n => n.ReceivedTime < cutoffDate);
    }

    private static IEnumerable<StartMenuAppEntry> EnumerateApps(StartMenuFolderNode node)
    {
        foreach (var app in node.Apps)
        {
            yield return app;
        }

        foreach (var folder in node.Folders)
        {
            foreach (var app in EnumerateApps(folder))
            {
                yield return app;
            }
        }
    }

    private static void CopyNotification(NotificationItem source, NotificationItem target)
    {
        target.AppId = source.AppId;
        target.AppName = source.AppName;
        target.AppIconPath = source.AppIconPath;
        target.AppIconBytes = source.AppIconBytes;
        target.Title = source.Title;
        target.Content = source.Content;
        target.ReceivedTime = source.ReceivedTime;
        target.ReceivedAtUtc = source.ReceivedAtUtc;
        target.LaunchArgs = source.LaunchArgs;
        target.Platform = source.Platform;
        target.SourceNotificationId = source.SourceNotificationId;
        target.DesktopEntryId = source.DesktopEntryId;
        target.Aumid = source.Aumid;
        target.LaunchTarget = source.LaunchTarget;
        target.CanActivate = source.CanActivate;
        target.CaptureMode = source.CaptureMode;
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _platformListener?.Dispose();
        _disposeCts.Dispose();
        ClearAll();
    }
}

public static class NotificationListenerServiceProvider
{
    private static readonly object Gate = new();
    private static NotificationListenerService? _instance;

    public static NotificationListenerService GetOrCreate(ISettingsService settingsService)
    {
        lock (Gate)
        {
            if (_instance == null)
            {
                _instance = new NotificationListenerService(settingsService);
                _ = _instance.InitializeAsync();
            }

            return _instance;
        }
    }
}
