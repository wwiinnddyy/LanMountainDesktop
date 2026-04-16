using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using PostHog;

namespace LanMountainDesktop.Services;

public sealed class PostHogUsageTelemetryService : IDisposable
{
    private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
    private const string PostHogHostUrl = "https://us.i.posthog.com";

    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISettingsService _settingsService;
    private readonly PostHogClient _client;
    private readonly CancellationTokenSource _cts = new();

    private Timer? _flushTimer;
    private bool _isInitialized;
    private bool _isUsageEnabled;
    private bool _sessionActive;
    private string _sessionId = string.Empty;
    private DateTimeOffset _sessionStartUtc;
    private long _sequence;
    private readonly string _launchId = Guid.NewGuid().ToString("N");

    public PostHogUsageTelemetryService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _settingsService = settingsFacade.Settings;
        _settingsService.Changed += OnSettingsChanged;

        _client = new PostHogClient(new PostHogOptions
        {
            ProjectApiKey = PostHogApiKey,
            HostUrl = new Uri(PostHogHostUrl),
            FlushAt = 20,
            FlushInterval = TimeSpan.FromSeconds(30)
        });
    }

    public bool IsUsageEnabled => _isUsageEnabled;

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        EnsureBaselineEventSent();
        RefreshEnabledState(forceSessionStart: true);

        _flushTimer = new Timer(
            _ => _ = _client.FlushAsync(),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));

        AppLogger.Info(
            "PostHogUsage",
            $"Usage telemetry initialized. Enabled={_isUsageEnabled}; InstallId={TelemetryIdentityService.Instance.InstallId}; TelemetryId={TelemetryIdentityService.Instance.TelemetryId}.");
    }

    public void RefreshEnabledState(bool forceSessionStart = false)
    {
        try
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            var enabled = snapshot.UploadAnonymousUsageData;

            if (_isUsageEnabled == enabled && !forceSessionStart)
            {
                return;
            }

            var previous = _isUsageEnabled;
            _isUsageEnabled = enabled;
            AppLogger.Info("PostHogUsage", $"Usage analytics enabled state changed from '{previous}' to '{_isUsageEnabled}'.");

            if (_isUsageEnabled)
            {
                StartSession("usage_enabled");
                return;
            }

            StopSessionWithoutSending();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Failed to refresh usage analytics enabled state.", ex);
            _isUsageEnabled = false;
            StopSessionWithoutSending();
        }
    }

    public void TrackMainWindowOpened(string source, bool isVisible, string windowState)
    {
        CaptureEvent(
            "main_window_opened",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["is_visible"] = isVisible,
                ["window_state"] = windowState
            },
            forceFlush: true);
    }

    public void TrackMainWindowClosed(string source, bool wasVisible, string windowState)
    {
        CaptureEvent(
            "main_window_closed",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["was_visible"] = wasVisible,
                ["window_state"] = windowState
            },
            forceFlush: true);
    }

    public void TrackSettingsWindowOpened(string source, string? currentPageId)
    {
        CaptureEvent(
            "settings_window_opened",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["current_page_id"] = currentPageId
            },
            forceFlush: true);
    }

    public void TrackSettingsWindowClosed(string source, string? currentPageId)
    {
        CaptureEvent(
            "settings_window_closed",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["current_page_id"] = currentPageId
            },
            forceFlush: true);
    }

    public void TrackSettingsNavigation(string? fromPageId, string? toPageId, string source)
    {
        CaptureEvent(
            "settings_navigation",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["from_page_id"] = fromPageId,
                ["to_page_id"] = toPageId
            },
            stateBefore: CreatePageState(fromPageId),
            stateAfter: CreatePageState(toPageId));
    }

    public void TrackSettingsDrawerOpened(string? pageId, string? drawerTitle)
    {
        CaptureEvent(
            "settings_drawer_opened",
            new Dictionary<string, object?>
            {
                ["page_id"] = pageId,
                ["drawer_title"] = drawerTitle
            },
            forceFlush: true);
    }

    public void TrackSettingsDrawerClosed(string? pageId, string? drawerTitle)
    {
        CaptureEvent(
            "settings_drawer_closed",
            new Dictionary<string, object?>
            {
                ["page_id"] = pageId,
                ["drawer_title"] = drawerTitle
            },
            forceFlush: true);
    }

    public void TrackDesktopComponentPlaced(DesktopComponentPlacementSnapshot placement, string source)
    {
        CaptureEvent(
            "desktop_component_placed",
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateAfter: DescribePlacement(placement),
            forceFlush: true);
    }

    public void TrackDesktopComponentMoved(
        DesktopComponentPlacementSnapshot before,
        DesktopComponentPlacementSnapshot after,
        string source)
    {
        CaptureEvent(
            "desktop_component_moved",
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(before),
            stateAfter: DescribePlacement(after),
            forceFlush: true);
    }

    public void TrackDesktopComponentResized(
        DesktopComponentPlacementSnapshot before,
        DesktopComponentPlacementSnapshot after,
        string source)
    {
        CaptureEvent(
            "desktop_component_resized",
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(before),
            stateAfter: DescribePlacement(after),
            forceFlush: true);
    }

    public void TrackDesktopComponentDeleted(DesktopComponentPlacementSnapshot before, string source)
    {
        CaptureEvent(
            "desktop_component_deleted",
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(before),
            forceFlush: true);
    }

    public void TrackDesktopComponentEditorOpened(DesktopComponentPlacementSnapshot placement, string source)
    {
        CaptureEvent(
            "desktop_component_editor_opened",
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(placement),
            forceFlush: true);
    }

    public void TrackSessionStarted(string source)
    {
        StartSession(source);
    }

    public void TrackSessionEnded(string source)
    {
        EndSession(source);
    }

    public void Shutdown(bool isRestart, string source)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (_isUsageEnabled && _sessionActive)
        {
            EndSession(source, isRestart);
        }

        _ = _client.FlushAsync();
        AppLogger.Info(
            "PostHogUsage",
            $"Usage telemetry shutdown complete. Source='{source}'; Restart='{isRestart}'; Enabled={_isUsageEnabled}.");
    }

    public void Dispose()
    {
        try
        {
            _flushTimer?.Dispose();
            _settingsService.Changed -= OnSettingsChanged;
            Shutdown(isRestart: false, source: "Dispose");
            _cts.Cancel();
            _client.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Error disposing usage telemetry service.", ex);
        }
    }

    private void EnsureBaselineEventSent()
    {
        try
        {
            var identity = TelemetryIdentityService.Instance;
            if (identity.HasReportedBaseline)
            {
                return;
            }

            var distinctId = identity.InstallId;
            var personProps = new Dictionary<string, object?>
            {
                ["install_id"] = identity.InstallId,
                ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
                ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
                ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
                ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
                ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture(),
                ["runtime_version"] = TelemetryEnvironmentInfo.GetRuntimeVersion(),
                ["language"] = TelemetryEnvironmentInfo.GetSystemLanguage()
            };

            _ = _client.IdentifyAsync(distinctId, personProps, null, _cts.Token);

            _client.Capture(
                distinctId,
                "app_first_launch",
                personProps,
                groups: null,
                sendFeatureFlags: false);

            _ = _client.FlushAsync();
            identity.MarkBaselineReported();
            AppLogger.Info("PostHogUsage", "Sent first-launch baseline event via SDK.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Failed to send baseline launch event.", ex);
        }
    }

    private void StartSession(string source)
    {
        if (!_isInitialized || !_isUsageEnabled)
        {
            return;
        }

        if (_sessionActive)
        {
            return;
        }

        _sessionActive = true;
        _sessionId = Guid.NewGuid().ToString("N");
        _sessionStartUtc = DateTimeOffset.UtcNow;
        _sequence = 0;

        CaptureEvent(
            "app_session_start",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["launch_id"] = _launchId,
                ["session_start_utc"] = _sessionStartUtc.ToString("o"),
                ["local_hour"] = _sessionStartUtc.ToLocalTime().Hour,
                ["day_part"] = TelemetryEnvironmentInfo.GetLocalDayPart(_sessionStartUtc),
                ["timezone"] = TimeZoneInfo.Local.Id,
                ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
                ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
                ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
                ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
                ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture()
            },
            forceFlush: true);

        AppLogger.Info("PostHogUsage", $"Session started. SessionId={_sessionId}; Source='{source}'.");
    }

    private void EndSession(string source, bool isRestart = false)
    {
        if (!_isInitialized || !_sessionActive)
        {
            return;
        }

        var endUtc = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(0, (long)(endUtc - _sessionStartUtc).TotalMilliseconds);

        CaptureEvent(
            "app_session_end",
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["launch_id"] = _launchId,
                ["session_start_utc"] = _sessionStartUtc.ToString("o"),
                ["session_end_utc"] = endUtc.ToString("o"),
                ["duration_ms"] = durationMs,
                ["is_restart"] = isRestart
            },
            forceFlush: true);

        _sessionActive = false;
        _sessionId = string.Empty;
        _sessionStartUtc = default;
        _sequence = 0;
        AppLogger.Info("PostHogUsage", $"Session ended. Source='{source}'; DurationMs={durationMs}; Restart={isRestart}.");
    }

    private void StopSessionWithoutSending()
    {
        _sessionActive = false;
        _sessionId = string.Empty;
        _sessionStartUtc = default;
        _sequence = 0;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope != SettingsScope.App ||
            e.ChangedKeys is null ||
            !e.ChangedKeys.Contains(nameof(AppSettingsSnapshot.UploadAnonymousUsageData), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        AppLogger.Info("PostHogUsage", "Usage analytics settings changed. Refreshing enabled state.");
        RefreshEnabledState();
    }

    private void CaptureEvent(
        string eventName,
        IReadOnlyDictionary<string, object?>? payload = null,
        IReadOnlyDictionary<string, object?>? stateBefore = null,
        IReadOnlyDictionary<string, object?>? stateAfter = null,
        bool forceFlush = false)
    {
        if (!_isInitialized || !_isUsageEnabled || !_sessionActive)
        {
            return;
        }

        var identity = TelemetryIdentityService.Instance;
        var distinctId = identity.TelemetryId;
        var seq = Interlocked.Increment(ref _sequence);

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["install_id"] = identity.InstallId,
            ["telemetry_id"] = identity.TelemetryId,
            ["session_id"] = _sessionId,
            ["sequence"] = seq,
            ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
            ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
            ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
            ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
            ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture(),
            ["runtime_version"] = TelemetryEnvironmentInfo.GetRuntimeVersion(),
            ["language"] = TelemetryEnvironmentInfo.GetSystemLanguage()
        };

        if (payload is not null)
        {
            foreach (var kvp in payload)
            {
                properties[$"payload_{kvp.Key}"] = kvp.Value;
            }
        }

        if (stateBefore is not null && stateBefore.Count > 0)
        {
            foreach (var kvp in stateBefore)
            {
                properties[$"state_before_{kvp.Key}"] = kvp.Value;
            }
        }

        if (stateAfter is not null && stateAfter.Count > 0)
        {
            foreach (var kvp in stateAfter)
            {
                properties[$"state_after_{kvp.Key}"] = kvp.Value;
            }
        }

        _client.Capture(
            distinctId,
            eventName,
            properties,
            groups: null,
            sendFeatureFlags: false);

        if (forceFlush)
        {
            _ = _client.FlushAsync();
        }
    }

    private static IReadOnlyDictionary<string, object?> CreatePageState(string? pageId)
    {
        return new Dictionary<string, object?>
        {
            ["page_id"] = pageId
        };
    }

    private static IReadOnlyDictionary<string, object?> DescribePlacement(DesktopComponentPlacementSnapshot placement)
    {
        return new Dictionary<string, object?>
        {
            ["placement_id"] = placement.PlacementId,
            ["component_id"] = placement.ComponentId,
            ["page_index"] = placement.PageIndex,
            ["row"] = placement.Row,
            ["column"] = placement.Column,
            ["width_cells"] = placement.WidthCells,
            ["height_cells"] = placement.HeightCells
        };
    }
}
