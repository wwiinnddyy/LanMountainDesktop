using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

/// <summary>
/// 跨平台通知监听服务
/// </summary>
public sealed class NotificationListenerService : IDisposable
{
    private readonly List<NotificationItem> _notifications = [];
    private readonly object _lock = new();
    private readonly ISettingsService _settingsService;

    // 平台特定的监听器
    private LinuxNotificationListener? _linuxListener;

    public event EventHandler<NotificationItem>? NotificationReceived;
    public event EventHandler<string>? NotificationRemoved;

    public NotificationListenerService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// 初始化并启动监听
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: 使用 UserNotificationListener (需要Windows SDK)
                // 当前为模拟实现
                await InitializeWindowsAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: 使用 DBus
                await InitializeLinuxAsync();
            }
            else
            {
                // macOS 或其他平台：功能不可用
                Console.WriteLine("[NotificationBox] 当前平台不支持通知监听");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationBox] 初始化失败: {ex.Message}");
        }
    }

    private async Task InitializeWindowsAsync()
    {
        // Windows通知监听实现
        // 实际项目中需要添加Windows SDK引用并使用UserNotificationListener
        // 由于需要UWP API，这里使用模拟实现
        await Task.CompletedTask;
        Console.WriteLine("[NotificationBox] Windows通知监听已启动（模拟模式）");
    }

    private async Task InitializeLinuxAsync()
    {
        try
        {
            _linuxListener = new LinuxNotificationListener(this);
            var success = await _linuxListener.InitializeAsync();

            if (!success)
            {
                Console.WriteLine("[NotificationBox] Linux通知监听初始化失败，可能未运行通知守护进程");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationBox] Linux通知监听异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 添加通知（供平台监听器调用）
    /// </summary>
    public void AddNotification(NotificationItem notification)
    {
        var settings = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);

        // 检查全局开关
        if (!settings.NotificationBoxEnabled)
            return;

        // 检查是否在屏蔽列表中
        if (settings.NotificationBoxBlockedApps.Contains(notification.AppId, StringComparer.OrdinalIgnoreCase))
            return;

        lock (_lock)
        {
            _notifications.Add(notification);
            CleanupOldNotifications(settings);
        }

        // 在UI线程触发事件
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            NotificationReceived?.Invoke(this, notification);
        });
    }

    /// <summary>
    /// 移除通知
    /// </summary>
    public void RemoveNotification(string notificationId)
    {
        lock (_lock)
        {
            var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null)
            {
                _notifications.Remove(notification);
            }
        }

        NotificationRemoved?.Invoke(this, notificationId);
    }

    private void CleanupOldNotifications(AppSettingsSnapshot settings)
    {
        // 按数量清理
        var maxCount = settings.NotificationBoxMaxStoredCount;
        while (_notifications.Count > maxCount)
        {
            _notifications.RemoveAt(0);
        }

        // 按时间清理
        var cutoffDate = DateTime.Now.AddDays(-settings.NotificationBoxHistoryRetentionDays);
        _notifications.RemoveAll(n => n.ReceivedTime < cutoffDate);
    }

    /// <summary>
    /// 获取所有通知
    /// </summary>
    public IReadOnlyList<NotificationItem> GetNotifications()
    {
        lock (_lock)
        {
            return _notifications.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// 清空所有通知
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _notifications.Clear();
        }
    }

    /// <summary>
    /// 标记通知为已读
    /// </summary>
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

    /// <summary>
    /// 获取未读通知数量
    /// </summary>
    public int GetUnreadCount()
    {
        lock (_lock)
        {
            return _notifications.Count(n => !n.IsRead);
        }
    }

    public void Dispose()
    {
        _linuxListener?.Dispose();
        ClearAll();
    }
}
