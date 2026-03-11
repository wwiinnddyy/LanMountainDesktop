using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed class HostApplicationLifecycleService : IHostApplicationLifecycle
{
    public bool TryExit(HostApplicationLifecycleRequest? request = null)
    {
        try
        {
            AppLogger.Info(
                "HostLifecycle",
                $"Exit requested. Source='{request?.Source ?? "Unknown"}'; Reason='{request?.Reason ?? string.Empty}'.");

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                AppLogger.Warn("HostLifecycle", "Exit request ignored because desktop lifetime is unavailable.");
                return false;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                desktop.Shutdown();
            }
            else
            {
                Dispatcher.UIThread.Post(() => desktop.Shutdown(), DispatcherPriority.Send);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("HostLifecycle", "Failed to exit the application.", ex);
            return false;
        }
    }

    public bool TryRestart(HostApplicationLifecycleRequest? request = null)
    {
        try
        {
            var startInfo = AppRestartService.CreateRestartStartInfo();
            if (startInfo is null)
            {
                AppLogger.Warn(
                    "HostLifecycle",
                    $"Restart request rejected because restart start info could not be resolved. Source='{request?.Source ?? "Unknown"}'.");
                return false;
            }

            Process.Start(startInfo);
            var exitRequest = request is null
                ? new HostApplicationLifecycleRequest(Reason: "Restart accepted.")
                : request with
                {
                    Reason = string.IsNullOrWhiteSpace(request.Reason)
                        ? "Restart accepted."
                        : request.Reason
                };

            return TryExit(exitRequest);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("HostLifecycle", "Failed to restart the application.", ex);
            return false;
        }
    }
}
