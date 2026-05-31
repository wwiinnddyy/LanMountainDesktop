using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services;

public sealed class HostApplicationLifecycleService : IHostApplicationLifecycle
{
    public bool TryExit(HostApplicationLifecycleRequest? request = null)
    {
        App? app = null;
        try
        {
            AppLogger.Info(
                "HostLifecycle",
                $"Exit requested. Source='{request?.Source ?? "Unknown"}'; Reason='{request?.Reason ?? string.Empty}'.");

            app = Application.Current as App;
            if (app is null || app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            {
                AppLogger.Warn("HostLifecycle", "Exit request ignored because desktop lifetime is unavailable.");
                return false;
            }

            return app.TrySubmitShutdown(HostShutdownMode.Exit, request);
        }
        catch (Exception ex)
        {
            app?.ResetShutdownIntent(request?.Source ?? "Unknown");
            AppLogger.Warn("HostLifecycle", "Failed to exit the application.", ex);
            return false;
        }
    }

    public bool TryRestart(HostApplicationLifecycleRequest? request = null)
    {
        App? app = null;
        try
        {
            app = Application.Current as App;
            if (app?.IsShutdownInProgress == true)
            {
                AppLogger.Warn(
                    "HostLifecycle",
                    $"Restart request ignored because shutdown is already in progress. Source='{request?.Source ?? "Unknown"}'.");
                return false;
            }

            return TryRestartDirectly(request);
        }
        catch (Exception ex)
        {
            app?.ResetShutdownIntent(request?.Source ?? "Unknown");
            AppLogger.Warn("HostLifecycle", "Failed to restart the application.", ex);
            return false;
        }
    }

    private bool TryRestartDirectly(HostApplicationLifecycleRequest? request)
    {
        var app = Application.Current as App;
        var restartPresentationMode = app?.GetCurrentRestartPresentationMode() ?? RestartPresentationMode.Foreground;
        var startInfo = AppRestartService.CreateRestartStartInfo(restartPresentationMode: restartPresentationMode);
        if (startInfo is null)
        {
            AppLogger.Warn(
                "HostLifecycle",
                $"Restart request rejected because restart start info could not be resolved. Source='{request?.Source ?? "Unknown"}'.");
            return false;
        }

        Process.Start(startInfo);
        var shutdownRequest = request is null
            ? new HostApplicationLifecycleRequest(Reason: "Restart accepted.")
            : request with
            {
                Reason = string.IsNullOrWhiteSpace(request.Reason)
                    ? "Restart accepted."
                    : request.Reason
            };

        return app?.TrySubmitShutdown(HostShutdownMode.Restart, shutdownRequest) == true;
    }

}
