using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Services;

public interface IAirAppLauncherService
{
    void OpenWorldClock(string? sourcePlacementId);

    void OpenWhiteboard(string componentId, string? sourcePlacementId);
}

internal sealed class AirAppLauncherService : IAirAppLauncherService
{
    public const string WorldClockAppId = "world-clock";
    public const string WhiteboardAppId = "whiteboard";

    private const int LauncherIpcRetryCount = 4;

    public void OpenWorldClock(string? sourcePlacementId)
    {
        _ = OpenAsync(WorldClockAppId, BuiltInComponentIds.DesktopWorldClock, sourcePlacementId);
    }

    public void OpenWhiteboard(string componentId, string? sourcePlacementId)
    {
        _ = OpenAsync(WhiteboardAppId, componentId, sourcePlacementId);
    }

    internal static AirAppOpenRequest BuildOpenRequest(
        string appId,
        string? sourceComponentId,
        string? sourcePlacementId,
        int requesterProcessId)
    {
        return new AirAppOpenRequest(
            appId.Trim(),
            string.IsNullOrWhiteSpace(sourceComponentId) ? null : sourceComponentId.Trim(),
            string.IsNullOrWhiteSpace(sourcePlacementId) ? null : sourcePlacementId.Trim(),
            requesterProcessId);
    }

    internal static string BuildSingleInstanceKey(string appId, string? sourceComponentId, string? sourcePlacementId)
    {
        var normalizedAppId = string.IsNullOrWhiteSpace(appId) ? "unknown" : appId.Trim();
        var normalizedComponentId = string.IsNullOrWhiteSpace(sourceComponentId) ? "none" : sourceComponentId.Trim();
        var normalizedPlacementId = string.IsNullOrWhiteSpace(sourcePlacementId) ? "none" : sourcePlacementId.Trim();
        return $"{normalizedAppId}:{normalizedComponentId}:{normalizedPlacementId}";
    }

    private static async Task OpenAsync(string appId, string sourceComponentId, string? sourcePlacementId)
    {
        var request = BuildOpenRequest(appId, sourceComponentId, sourcePlacementId, Environment.ProcessId);
        try
        {
            var result = await SendOpenRequestAsync(request).ConfigureAwait(false);
            if (result.Accepted)
            {
                AppLogger.Info("AirAppLauncher", $"Launcher accepted Air APP request. AppId='{appId}'; Code='{result.Code}'.");
                return;
            }

            AppLogger.Warn("AirAppLauncher", $"Launcher rejected Air APP request. AppId='{appId}'; Code='{result.Code}'; Message='{result.Message}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AirAppLauncher", $"Failed to open Air APP through Launcher. AppId='{appId}'.", ex);
        }
    }

    private static async Task<AirAppOperationResult> SendOpenRequestAsync(AirAppOpenRequest request)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= LauncherIpcRetryCount; attempt++)
        {
            try
            {
                using var client = new LanMountainDesktopIpcClient();
                await client.ConnectAsync(IpcConstants.AirAppLifecyclePipeName).ConfigureAwait(false);
                var proxy = client.CreateProxy<IAirAppLifecycleService>();
                return await proxy.OpenAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt == 1)
                {
                    AppLogger.Warn(
                        "AirAppLauncher",
                        $"Air APP lifecycle IPC unavailable on first attempt. Pipe='{IpcConstants.AirAppLifecyclePipeName}'. Starting Launcher broker.",
                        ex);
                    TryStartLauncher();
                }

                await Task.Delay(250 * attempt).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"Launcher Air APP IPC is unavailable. Pipe='{IpcConstants.AirAppLifecyclePipeName}'.",
            lastException);
    }

    internal static ProcessStartInfo CreateBrokerStartInfo(string launcherPath, int requesterProcessId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("air-app-broker");
        startInfo.ArgumentList.Add("--requester-pid");
        startInfo.ArgumentList.Add(requesterProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }

    private static void TryStartLauncher()
    {
        try
        {
            var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                AppLogger.Warn("AirAppLauncher", "Unable to start Launcher for Air APP request: launcher path was not found.");
                return;
            }

            var startInfo = CreateBrokerStartInfo(launcherPath, Environment.ProcessId);
            _ = Process.Start(startInfo);
            AppLogger.Info(
                "AirAppLauncher",
                $"Started Launcher Air APP broker. Path='{launcherPath}'; Pipe='{IpcConstants.AirAppLifecyclePipeName}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AirAppLauncher", "Failed to start Launcher for Air APP request.", ex);
        }
    }
}

public static class AirAppLauncherServiceProvider
{
    private static readonly object Gate = new();
    private static IAirAppLauncherService? _instance;

    public static IAirAppLauncherService GetOrCreate()
    {
        lock (Gate)
        {
            _instance ??= new AirAppLauncherService();
            return _instance;
        }
    }
}
