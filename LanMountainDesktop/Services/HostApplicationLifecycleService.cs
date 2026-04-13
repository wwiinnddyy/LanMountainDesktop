using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed class HostApplicationLifecycleService : IHostApplicationLifecycle
{
    private const string UpgradeHelperExecutableName = "LanMountainDesktop.PluginUpgradeHelper.exe";

    public bool TryExit(HostApplicationLifecycleRequest? request = null)
    {
        App? app = null;
        try
        {
            AppLogger.Info(
                "HostLifecycle",
                $"Exit requested. Source='{request?.Source ?? "Unknown"}'; Reason='{request?.Reason ?? string.Empty}'.");

            app = Application.Current as App;
            if (app?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                AppLogger.Warn("HostLifecycle", "Exit request ignored because desktop lifetime is unavailable.");
                return false;
            }

            app.PrepareForShutdown(isRestart: false, request?.Source ?? "Unknown");
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

            if (HasPendingPluginUpgrades())
            {
                return TryRestartWithUpgradeHelper(request);
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

    private static bool HasPendingPluginUpgrades()
    {
        try
        {
            var pluginsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop",
                "Extensions",
                "Plugins");
            var pendingUpgradesPath = Path.Combine(pluginsDirectory, ".pending-plugin-upgrades.json");
            return File.Exists(pendingUpgradesPath);
        }
        catch
        {
            return false;
        }
    }

    private bool TryRestartWithUpgradeHelper(HostApplicationLifecycleRequest? request)
    {
        AppLogger.Info("HostLifecycle", "Detected pending plugin upgrades. Using upgrade helper for restart.");

        var helperPath = ResolveUpgradeHelperPath();
        if (!File.Exists(helperPath))
        {
            AppLogger.Warn("HostLifecycle", $"Upgrade helper not found at '{helperPath}'. Falling back to direct restart.");
            return TryRestartDirectly(request);
        }

        var pluginsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Extensions",
            "Plugins");

        var startInfo = AppRestartService.CreateRestartStartInfo();
        var launchCommand = startInfo?.FileName ?? Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory;
        var launchArgs = startInfo?.Arguments ?? "";

        var helperStartInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = $"--plugins-dir \"{pluginsDirectory}\" --parent-pid {Environment.ProcessId} --launch \"{launchCommand}\" --launch-args \"{launchArgs}\" --working-dir \"{AppContext.BaseDirectory}\"",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        AppLogger.Info("HostLifecycle", $"Starting upgrade helper: {helperStartInfo.FileName} {helperStartInfo.Arguments}");

        Process.Start(helperStartInfo);

        var app = Application.Current as App;
        app?.PrepareForShutdown(isRestart: true, request?.Source ?? "Unknown");

        return TryExit(request);
    }

    private bool TryRestartDirectly(HostApplicationLifecycleRequest? request)
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
        var app = Application.Current as App;
        app?.PrepareForShutdown(isRestart: true, request?.Source ?? "Unknown");
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

    private static string ResolveUpgradeHelperPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "PluginUpgradeHelper", UpgradeHelperExecutableName);
    }
}
