using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using LanMountainDesktop.Services.RssReader;

namespace LanMountainDesktop.Services;

public interface IAirAppLauncherService
{
    void OpenWorldClock(string? sourcePlacementId);

    void OpenWorldClock(string sourceComponentId, string? sourcePlacementId);

    void OpenWhiteboard(string componentId, string? sourcePlacementId);

    void OpenRssReader(string componentId, string? sourcePlacementId, string? targetEntryId = null);
}

internal sealed class AirAppLauncherService : IAirAppLauncherService
{
    public const string WorldClockAppId = "world-clock";
    public const string WhiteboardAppId = "whiteboard";
    public const string RssReaderAppId = "rss-reader";

    private const int RuntimeIpcRetryCount = 4;

    public void OpenWorldClock(string? sourcePlacementId)
    {
        OpenWorldClock(BuiltInComponentIds.DesktopWorldClock, sourcePlacementId);
    }

    public void OpenWorldClock(string sourceComponentId, string? sourcePlacementId)
    {
        var componentId = string.IsNullOrWhiteSpace(sourceComponentId)
            ? BuiltInComponentIds.DesktopWorldClock
            : sourceComponentId.Trim();
        AppLogger.Info(
            "AirAppLauncher",
            $"World Clock Air APP requested. ComponentId='{componentId}'; PlacementId='{sourcePlacementId ?? string.Empty}'.");
        _ = OpenAsync(WorldClockAppId, componentId, sourcePlacementId);
    }

    public void OpenWhiteboard(string componentId, string? sourcePlacementId)
    {
        AppLogger.Info(
            "AirAppLauncher",
            $"Whiteboard Air APP requested. ComponentId='{componentId}'; PlacementId='{sourcePlacementId ?? string.Empty}'.");
        _ = OpenAsync(WhiteboardAppId, componentId, sourcePlacementId);
    }

    public void OpenRssReader(string componentId, string? sourcePlacementId, string? targetEntryId = null)
    {
        AppLogger.Info("AirAppLauncher", $"RSS Reader Air APP requested. EntryId='{targetEntryId ?? string.Empty}'.");
        if (!string.IsNullOrWhiteSpace(targetEntryId))
        {
            using var service = new RssReaderService();
            service.SetPendingEntryId(targetEntryId);
        }
        _ = OpenAsync(RssReaderAppId, componentId, sourcePlacementId, targetEntryId);
    }

    internal static AirAppOpenRequest BuildOpenRequest(
        string appId,
        string? sourceComponentId,
        string? sourcePlacementId,
        int requesterProcessId,
        string? targetEntryId = null)
    {
        return new AirAppOpenRequest(
            appId.Trim(),
            string.IsNullOrWhiteSpace(sourceComponentId) ? null : sourceComponentId.Trim(),
            string.IsNullOrWhiteSpace(sourcePlacementId) ? null : sourcePlacementId.Trim(),
            requesterProcessId,
            string.IsNullOrWhiteSpace(targetEntryId) ? null : targetEntryId.Trim());
    }

    internal static string BuildSingleInstanceKey(string appId, string? sourceComponentId, string? sourcePlacementId)
    {
        var normalizedAppId = string.IsNullOrWhiteSpace(appId) ? "unknown" : appId.Trim();
        if (string.Equals(normalizedAppId, WorldClockAppId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedAppId}:clock-suite:global";
        }
        if (string.Equals(normalizedAppId, RssReaderAppId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalizedAppId}:global";
        }

        var normalizedComponentId = string.IsNullOrWhiteSpace(sourceComponentId) ? "none" : sourceComponentId.Trim();
        var normalizedPlacementId = string.IsNullOrWhiteSpace(sourcePlacementId) ? "none" : sourcePlacementId.Trim();
        return $"{normalizedAppId}:{normalizedComponentId}:{normalizedPlacementId}";
    }

    private static async Task OpenAsync(string appId, string sourceComponentId, string? sourcePlacementId, string? targetEntryId = null)
    {
        var request = BuildOpenRequest(appId, sourceComponentId, sourcePlacementId, Environment.ProcessId, targetEntryId);
        try
        {
            var result = await SendOpenRequestAsync(request).ConfigureAwait(false);
            if (result.Accepted)
            {
                AppLogger.Info("AirAppLauncher", $"AirApp Runtime accepted Air APP request. AppId='{appId}'; Code='{result.Code}'.");
                return;
            }

            AppLogger.Warn("AirAppLauncher", $"AirApp Runtime rejected Air APP request. AppId='{appId}'; Code='{result.Code}'; Message='{result.Message}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AirAppLauncher", $"Failed to open Air APP through AirApp Runtime. AppId='{appId}'.", ex);
        }
    }

    private static async Task<AirAppOperationResult> SendOpenRequestAsync(AirAppOpenRequest request)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= RuntimeIpcRetryCount; attempt++)
        {
            try
            {
                using var client = new LanMountainDesktopIpcClient();
                await client.ConnectAsync(IpcConstants.AirAppRuntimePipeName).ConfigureAwait(false);
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
                        $"Air APP lifecycle IPC unavailable on first attempt. Pipe='{IpcConstants.AirAppRuntimePipeName}'. Starting AirApp Runtime.",
                        ex);
                    TryStartRuntime();
                }

                await Task.Delay(250 * attempt).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"AirApp Runtime IPC is unavailable. Pipe='{IpcConstants.AirAppRuntimePipeName}'.",
            lastException);
    }

    internal static ProcessStartInfo CreateRuntimeStartInfo(string runtimePath, int requesterProcessId, string? appRoot = null, string? dataRoot = null)
    {
        var startInfo = AirAppRuntimeProcessStarter.CreateStartInfo(runtimePath);
        if (!string.IsNullOrWhiteSpace(appRoot))
        {
            startInfo.ArgumentList.Add("--app-root");
            startInfo.ArgumentList.Add(Path.GetFullPath(appRoot));
        }

        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            startInfo.ArgumentList.Add("--data-root");
            startInfo.ArgumentList.Add(Path.GetFullPath(dataRoot));
        }

        startInfo.ArgumentList.Add("--requester-pid");
        startInfo.ArgumentList.Add(requesterProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }

    private static void TryStartRuntime()
    {
        try
        {
            var appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var runtimePath = AirAppRuntimePathResolver.ResolveExecutablePath(appRoot, AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(runtimePath) || !File.Exists(runtimePath))
            {
                AppLogger.Warn("AirAppLauncher", "Unable to start AirApp Runtime for Air APP request: runtime path was not found.");
                return;
            }

            var dataRoot = AirAppRuntimeDataRootResolver.ResolveDataRoot(appRoot);
            var startInfo = CreateRuntimeStartInfo(runtimePath, Environment.ProcessId, appRoot, dataRoot);
            _ = Process.Start(startInfo);
            AppLogger.Info(
                "AirAppLauncher",
                $"Started AirApp Runtime. Path='{runtimePath}'; Pipe='{IpcConstants.AirAppRuntimePipeName}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AirAppLauncher", "Failed to start AirApp Runtime for Air APP request.", ex);
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
