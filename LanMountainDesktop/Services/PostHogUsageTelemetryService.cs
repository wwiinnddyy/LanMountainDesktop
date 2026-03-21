using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed class PostHogUsageTelemetryService : IDisposable
{
    private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
    private const string PostHogHost = "https://us.i.posthog.com/capture/";

    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private readonly Queue<TelemetryEvent> _eventQueue = new();
    private readonly object _queueLock = new();

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
            _ => FlushEvents(),
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

            ClearQueuedEvents();
            StopSessionWithoutSending();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Failed to refresh usage analytics enabled state.", ex);
            _isUsageEnabled = false;
            ClearQueuedEvents();
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

        FlushEvents();
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
            FlushEvents();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Error disposing usage telemetry service.", ex);
        }
        finally
        {
            _httpClient.Dispose();
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

            var now = DateTimeOffset.UtcNow;
            if (SendBaselineEventToPostHog(identity.InstallId, now))
            {
                identity.MarkBaselineReported();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Failed to send baseline launch event.", ex);
        }
    }

    private bool SendBaselineEventToPostHog(string installId, DateTimeOffset timestamp)
    {
        try
        {
            var requestBody = new Dictionary<string, object?>
            {
                ["api_key"] = PostHogApiKey,
                ["event"] = "app_first_launch",
                ["distinct_id"] = installId,
                ["timestamp"] = timestamp.ToString("o"),
                ["properties"] = new Dictionary<string, object?>
                {
                    ["install_id"] = installId,
                    ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
                    ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
                    ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
                    ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
                    ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture(),
                    ["runtime_version"] = TelemetryEnvironmentInfo.GetRuntimeVersion(),
                    ["language"] = TelemetryEnvironmentInfo.GetSystemLanguage(),
                    ["launch_time_utc"] = timestamp.ToString("o")
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var bytes = Encoding.UTF8.GetBytes(json);

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var response = _httpClient.PostAsync(PostHogHost, content).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warn(
                    "PostHogUsage",
                    $"PostHog baseline event failed: {response.StatusCode} - {responseBody}");
                return false;
            }

            AppLogger.Info("PostHogUsage", "Sent first-launch baseline event.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Failed to send baseline launch event.", ex);
            return false;
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

        var eventData = new TelemetryEvent(
            eventName,
            TelemetryIdentityService.Instance.TelemetryId,
            TelemetryIdentityService.Instance.InstallId,
            TelemetryIdentityService.Instance.TelemetryId,
            _sessionId,
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            payload ?? new Dictionary<string, object?>(),
            stateBefore,
            stateAfter);

        lock (_queueLock)
        {
            _eventQueue.Enqueue(eventData);
        }

        if (forceFlush)
        {
            FlushEvents();
            return;
        }

        var shouldFlush = false;
        lock (_queueLock)
        {
            shouldFlush = _eventQueue.Count >= 20;
        }

        if (shouldFlush)
        {
            FlushEvents();
        }
    }

    private void FlushEvents()
    {
        List<TelemetryEvent> eventsToSend;

        lock (_queueLock)
        {
            if (_eventQueue.Count == 0)
            {
                return;
            }

            eventsToSend = new List<TelemetryEvent>();
            while (_eventQueue.Count > 0 && eventsToSend.Count < 20)
            {
                eventsToSend.Add(_eventQueue.Dequeue());
            }
        }

        try
        {
            foreach (var telemetryEvent in eventsToSend)
            {
                if (!SendEventToPostHog(telemetryEvent, flushImmediately: false))
                {
                    throw new InvalidOperationException($"Failed to send PostHog event '{telemetryEvent.EventName}'.");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Failed to send queued events to PostHog.", ex);

            lock (_queueLock)
            {
                foreach (var evt in eventsToSend)
                {
                    if (_eventQueue.Count >= 100)
                    {
                        break;
                    }

                    _eventQueue.Enqueue(evt);
                }
            }
        }
    }

    private bool SendEventToPostHog(TelemetryEvent telemetryEvent, bool flushImmediately)
    {
        try
        {
            var requestBody = new Dictionary<string, object?>
            {
                ["api_key"] = PostHogApiKey,
                ["event"] = telemetryEvent.EventName,
                ["distinct_id"] = telemetryEvent.DistinctId,
                ["timestamp"] = telemetryEvent.Timestamp.ToString("o"),
                ["properties"] = telemetryEvent.ToPostHogProperties()
            };

            var json = JsonSerializer.Serialize(requestBody);
            var bytes = Encoding.UTF8.GetBytes(json);

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var response = _httpClient.PostAsync(PostHogHost, content).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warn(
                    "PostHogUsage",
                    $"PostHog event '{telemetryEvent.EventName}' failed: {response.StatusCode} - {responseBody}");
                return false;
            }

            if (flushImmediately)
            {
                AppLogger.Info("PostHogUsage", $"Sent event '{telemetryEvent.EventName}' immediately.");
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", $"Failed to send PostHog event '{telemetryEvent.EventName}'.", ex);
            return false;
        }
    }

    private void ClearQueuedEvents()
    {
        lock (_queueLock)
        {
            _eventQueue.Clear();
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
