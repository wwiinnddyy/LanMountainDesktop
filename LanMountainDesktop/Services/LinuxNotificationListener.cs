using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

[SupportedOSPlatform("linux")]
internal sealed class LinuxNotificationListener : IPlatformNotificationListener
{
    private static readonly Regex DbusStringRegex = new("^\\s*string\\s+\"(?<value>.*)\"\\s*$", RegexOptions.Compiled);
    private static readonly Regex DbusUIntRegex = new("^\\s*uint32\\s+(?<value>\\d+)\\s*$", RegexOptions.Compiled);
    private static readonly Regex DesktopEntryHintRegex = new("\"desktop-entry\"\\s+variant\\s+string\\s+\"(?<value>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex ImagePathHintRegex = new("\"image-path\"\\s+variant\\s+string\\s+\"(?<value>[^\"]+)\"", RegexOptions.Compiled);

    private readonly NotificationListenerService _parent;
    private readonly string _requestedMode;
    private readonly CancellationTokenSource _cts = new();
    private Process? _monitorProcess;
    private Task? _monitorTask;
    private uint _nextSyntheticId = 1;

    public LinuxNotificationListener(NotificationListenerService parent, string requestedMode)
    {
        _parent = parent;
        _requestedMode = string.IsNullOrWhiteSpace(requestedMode) ? "ProxyDaemon" : requestedMode;
    }

    public async Task<NotificationBoxStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new NotificationBoxStatus(NotificationBoxServiceState.Unsupported, "当前平台不是 Linux。", "Linux");
        }

        var dbusSessionBus = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(dbusSessionBus))
        {
            return new NotificationBoxStatus(
                NotificationBoxServiceState.Unsupported,
                "DBus Session Bus 环境变量未设置，无法监听 Linux 通知。",
                _requestedMode);
        }

        var hasMonitorTool = CommandExists("dbus-monitor");
        if (!hasMonitorTool)
        {
            return new NotificationBoxStatus(
                NotificationBoxServiceState.Unsupported,
                "未找到 dbus-monitor，无法启用 Linux 通知旁路监听。",
                _requestedMode);
        }

        var mode = _requestedMode.Equals("PassiveMonitor", StringComparison.OrdinalIgnoreCase)
            ? "PassiveMonitor"
            : "ProxyDaemon";

        var daemonRunning = await CheckNotificationDaemonAsync(cancellationToken).ConfigureAwait(false);
        var statusMessage = mode == "ProxyDaemon" && daemonRunning
            ? "系统通知守护进程已占用 org.freedesktop.Notifications，已以旁路监听方式运行。"
            : mode == "ProxyDaemon"
                ? "Linux 通知代理模式已启动；未检测到现有通知守护进程。"
                : "Linux 通知旁路监听已启动。";

        StartDbusMonitor(mode);

        return new NotificationBoxStatus(
            mode == "ProxyDaemon" && daemonRunning ? NotificationBoxServiceState.Degraded : NotificationBoxServiceState.Running,
            statusMessage,
            mode);
    }

    private void StartDbusMonitor(string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dbus-monitor",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add("interface='org.freedesktop.Notifications'");

        _monitorProcess = Process.Start(startInfo);
        if (_monitorProcess is null)
        {
            throw new InvalidOperationException("Failed to start dbus-monitor.");
        }

        _monitorTask = Task.Run(() => ReadMonitorOutputAsync(_monitorProcess, mode, _cts.Token), CancellationToken.None);
    }

    private async Task ReadMonitorOutputAsync(Process process, string mode, CancellationToken cancellationToken)
    {
        var capture = new List<string>();
        var inNotify = false;

        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Contains("member=Notify", StringComparison.Ordinal))
            {
                capture.Clear();
                inNotify = true;
                continue;
            }

            if (!inNotify)
            {
                if (line.Contains("member=NotificationClosed", StringComparison.Ordinal) ||
                    line.Contains("member=CloseNotification", StringComparison.Ordinal))
                {
                    capture.Clear();
                    capture.Add(line);
                    inNotify = false;
                }
                continue;
            }

            if (line.StartsWith("method ", StringComparison.Ordinal) ||
                line.StartsWith("signal ", StringComparison.Ordinal))
            {
                TryParseNotify(capture, mode);
                capture.Clear();
                inNotify = line.Contains("member=Notify", StringComparison.Ordinal);
                continue;
            }

            capture.Add(line);
            if (capture.Count > 40)
            {
                TryParseNotify(capture, mode);
                capture.Clear();
                inNotify = false;
            }
        }
    }

    private void TryParseNotify(IReadOnlyList<string> lines, string mode)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var strings = lines
            .Select(line => DbusStringRegex.Match(line))
            .Where(match => match.Success)
            .Select(match => UnescapeDbusString(match.Groups["value"].Value))
            .ToList();

        if (strings.Count < 4)
        {
            return;
        }

        var appName = strings[0];
        var appIcon = strings[1];
        var summary = strings[2];
        var body = strings[3];
        var desktopEntry = TryMatchHint(lines, DesktopEntryHintRegex);
        var imagePath = TryMatchHint(lines, ImagePathHintRegex);

        var sourceId = lines
            .Select(line => DbusUIntRegex.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["value"].Value)
            .Skip(1)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            sourceId = (_nextSyntheticId++).ToString();
        }

        var notification = new NotificationItem
        {
            Id = $"linux:{sourceId}",
            SourceNotificationId = sourceId,
            Platform = "Linux",
            CaptureMode = mode,
            AppId = !string.IsNullOrWhiteSpace(desktopEntry)
                ? desktopEntry
                : NormalizeAppId(appName),
            AppName = string.IsNullOrWhiteSpace(appName) ? "Linux 应用" : appName,
            Title = StripHtmlTags(summary),
            Content = StripHtmlTags(body),
            AppIconPath = ResolveIconPath(!string.IsNullOrWhiteSpace(imagePath) ? imagePath : appIcon, appName),
            DesktopEntryId = string.IsNullOrWhiteSpace(desktopEntry) ? null : $"{desktopEntry}.desktop",
            LaunchTarget = string.IsNullOrWhiteSpace(desktopEntry) ? null : desktopEntry,
            CanActivate = !string.IsNullOrWhiteSpace(desktopEntry),
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            ReceivedTime = DateTime.Now
        };

        _parent.AddNotification(notification);
    }

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
        var sourceId = replacesId == 0 ? _nextSyntheticId++ : replacesId;
        var notification = new NotificationItem
        {
            Id = $"linux:{sourceId}",
            SourceNotificationId = sourceId.ToString(),
            Platform = "Linux",
            CaptureMode = _requestedMode,
            AppId = NormalizeAppId(appName),
            AppName = appName,
            Title = StripHtmlTags(summary),
            Content = StripHtmlTags(body),
            AppIconPath = ResolveIconPath(appIcon, appName),
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            ReceivedTime = DateTime.Now
        };

        _parent.AddNotification(notification);
    }

    private static async Task<bool> CheckNotificationDaemonAsync(CancellationToken cancellationToken)
    {
        var processNames = new[] { "gnome-shell", "plasmashell", "kded5", "dunst", "mako", "swaync", "xfce4-notifyd" };
        foreach (var name in processNames)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pgrep",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }.WithArgument("-x").WithArgument(name));
                if (process is null)
                {
                    continue;
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode == 0)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool CommandExists(string command)
    {
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return pathEntries.Any(path =>
        {
            try
            {
                return File.Exists(Path.Combine(path, command));
            }
            catch
            {
                return false;
            }
        });
    }

    private static string? ResolveIconPath(string iconName, string appName)
    {
        if (string.IsNullOrEmpty(iconName))
        {
            return null;
        }

        if (File.Exists(iconName))
        {
            return iconName;
        }

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

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var result = Regex.Replace(html, "<[^>]+>", string.Empty);
        return result
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeAppId(string appName)
        => appName.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string? TryMatchHint(IEnumerable<string> lines, Regex regex)
        => lines.Select(line => regex.Match(line))
            .Where(match => match.Success)
            .Select(match => UnescapeDbusString(match.Groups["value"].Value))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string UnescapeDbusString(string value)
        => value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (_monitorProcess is { HasExited: false })
            {
                _monitorProcess.Kill(entireProcessTree: true);
            }

            _monitorTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }
        finally
        {
            _monitorProcess?.Dispose();
            _cts.Dispose();
        }
    }
}

internal static class ProcessStartInfoArgumentExtensions
{
    public static ProcessStartInfo WithArgument(this ProcessStartInfo startInfo, string argument)
    {
        startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
