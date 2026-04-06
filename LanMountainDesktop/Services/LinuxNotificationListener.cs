using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

/// <summary>
/// Linux平台通知监听器 - 通过DBus监听org.freedesktop.Notifications
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxNotificationListener : IDisposable
{
    private readonly NotificationListenerService _parent;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public LinuxNotificationListener(NotificationListenerService parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// 初始化并启动DBus监听
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            // 检查DBus环境变量
            var dbusSessionBus = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            if (string.IsNullOrEmpty(dbusSessionBus))
            {
                Console.WriteLine("[NotificationBox] DBus Session Bus 环境变量未设置");
                return false;
            }

            // 检查通知守护进程是否运行
            // 通过检查常见进程名
            var hasNotificationDaemon = await CheckNotificationDaemonAsync();
            if (!hasNotificationDaemon)
            {
                Console.WriteLine("[NotificationBox] 未检测到通知守护进程，消息盒子功能可能不可用");
                // 仍然返回true，因为守护进程可能在之后启动
            }

            _cts = new CancellationTokenSource();
            _ = StartListeningAsync(_cts.Token);

            Console.WriteLine("[NotificationBox] Linux通知监听已启动");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationBox] Linux通知监听初始化失败: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CheckNotificationDaemonAsync()
    {
        try
        {
            // 检查常见通知守护进程
            var processNames = new[] { "gnome-shell", "kded5", "dunst", "mako", "swaync" };
            foreach (var name in processNames)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "pgrep",
                    Arguments = $"-x {name}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task StartListeningAsync(CancellationToken ct)
    {
        _isRunning = true;

        try
        {
            // 注意：Tmds.DBus.Protocol 是低层API
            // 这里使用简化方案，实际生产环境需要完整的DBus信号订阅实现
            // 当前版本为框架实现，后续可以完善DBus监听逻辑

            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationBox] Linux通知监听异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理接收到的通知（供DBus信号处理器调用）
    /// </summary>
    public void HandleNotification(
        string appName,
        uint replacesId,
        string appIcon,
        string summary,
        string body,
        string[] actions,
        object hints,
        int expireTimeout)
    {
        try
        {
            var notification = new NotificationItem
            {
                Id = Guid.NewGuid().ToString(),
                AppId = appName.ToLowerInvariant().Replace(" ", ""),
                AppName = appName,
                Title = summary,
                Content = StripHtmlTags(body),
                ReceivedTime = DateTime.Now,
                AppIconPath = ResolveIconPath(appIcon, appName)
            };

            _parent.AddNotification(notification);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationBox] 处理通知失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析应用图标路径
    /// </summary>
    private static string? ResolveIconPath(string iconName, string appName)
    {
        if (string.IsNullOrEmpty(iconName))
        {
            return null;
        }

        // 如果是绝对路径，直接使用
        if (File.Exists(iconName))
        {
            return iconName;
        }

        // 尝试从图标主题中查找
        var iconPaths = new[]
        {
            $"/usr/share/icons/hicolor/48x48/apps/{iconName}.png",
            $"/usr/share/icons/hicolor/64x64/apps/{iconName}.png",
            $"/usr/share/pixmaps/{iconName}.png",
            $"/usr/share/pixmaps/{iconName}.svg",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                $".local/share/icons/{iconName}.png")
        };

        return iconPaths.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 去除HTML标签（通知内容可能包含HTML）
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        // 简单的HTML标签去除
        var result = html;
        result = System.Text.RegularExpressions.Regex.Replace(result, "<[^>]+>", "");
        result = result.Replace("&lt;", "<");
        result = result.Replace("&gt;", ">");
        result = result.Replace("&amp;", "&");
        result = result.Replace("&quot;", "\"");
        return result.Trim();
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
