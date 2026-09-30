using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Threading;
using PostHog;

namespace LanMountainDesktop.Services;

public sealed class PostHogUsageTelemetryService : IDisposable
{
    private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
    private const string PostHogHostUrl = "https://us.i.posthog.com";

    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISettingsService _settingsService;
    private readonly IUsageTelemetryClient _client;
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
        : this(settingsFacade, new PostHogUsageClient())
    {
    }

    /// <summary>
    /// 接缝存在的唯一理由：<b>隐私开关这条路径要能在测试里被钉住</b>。
    /// 原来构造函数里直接 new 真客户端（带真 project key 与真 host），于是"关掉的遥测不该发"这件事
    /// 一测试就真往 PostHog 发数据——结果这条闸从写下到 2026-09-30 被真机日志抓到为止，一格测试都没有。
    /// </summary>
    internal PostHogUsageTelemetryService(ISettingsFacadeService settingsFacade, IUsageTelemetryClient client)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _settingsService = settingsFacade.Settings;
        _settingsService.Changed += OnSettingsChanged;
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>本服务真正用到的客户端表面，只有这四件事。</summary>
    internal interface IUsageTelemetryClient : IDisposable
    {
        void Capture(string distinctId, string eventName, Dictionary<string, object?> properties);

        Task IdentifyAsync(string distinctId, Dictionary<string, object?> properties, CancellationToken cancellationToken);

        Task FlushAsync();
    }

    private sealed class PostHogUsageClient : IUsageTelemetryClient
    {
        private readonly PostHogClient _client = new(new PostHogOptions
        {
            ProjectApiKey = PostHogApiKey,
            HostUrl = new Uri(PostHogHostUrl),
            FlushAt = 20,
            FlushInterval = TimeSpan.FromSeconds(30)
        });

        public void Capture(string distinctId, string eventName, Dictionary<string, object?> properties)
            => _client.Capture(
                distinctId,
                eventName,
                properties,
                groups: null,
                sendFeatureFlags: false);

        public Task IdentifyAsync(
            string distinctId,
            Dictionary<string, object?> properties,
            CancellationToken cancellationToken)
            => _client.IdentifyAsync(distinctId, properties, null, cancellationToken);

        public Task FlushAsync() => _client.FlushAsync();

        public void Dispose() => _client.Dispose();
    }

    public bool IsUsageEnabled => _isUsageEnabled;

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        // 先读开关、再决定要不要上报：EnsureBaselineEventSent 挪到 RefreshEnabledState 的"已启用"分支里。
        // 原来它排在这行之前，于是首启基线事件（install_id / telemetry_id / OS 版本 / 设备型号 / 渲染档）
        // 在用户把用量遥测关掉的情况下照样发出去——2026-09-30 真机日志同一秒里两条都在：
        // "Sent first-launch baseline event via SDK" 与 "Usage telemetry initialized. Enabled=False"。
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
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
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
                // 顺序照改之前的样子保下来：首启基线在前、会话开始在后（原来基线在 Initialize 里发，
                // 那时会话还没起）。反过来会把两条事件的先后顺序改掉，虽然不影响隐私语义，没必要顺手动。
                EnsureBaselineEventSent();
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
            TelemetryEventNames.MainWindowOpened,
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["is_visible"] = isVisible,
                ["window_state"] = windowState
            },
            forceFlush: false);
    }

    public void TrackMainWindowClosed(string source, bool wasVisible, string windowState)
    {
        CaptureEvent(
            TelemetryEventNames.MainWindowClosed,
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["was_visible"] = wasVisible,
                ["window_state"] = windowState
            },
            forceFlush: false);
    }

    public void TrackSettingsWindowOpened(string source, string? currentPageId)
    {
        CaptureEvent(
            TelemetryEventNames.SettingsWindowOpened,
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["current_page_id"] = currentPageId
            },
            forceFlush: false);
    }

    public void TrackSettingsWindowClosed(string source, string? currentPageId)
    {
        CaptureEvent(
            TelemetryEventNames.SettingsWindowClosed,
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["current_page_id"] = currentPageId
            },
            forceFlush: false);
    }

    public void TrackSettingsNavigation(string? fromPageId, string? toPageId, string source)
    {
        CaptureEvent(
            TelemetryEventNames.SettingsNavigation,
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
            TelemetryEventNames.SettingsDrawerOpened,
            new Dictionary<string, object?>
            {
                ["page_id"] = pageId,
                ["drawer_title"] = drawerTitle
            },
            forceFlush: false);
    }

    public void TrackSettingsDrawerClosed(string? pageId, string? drawerTitle)
    {
        CaptureEvent(
            TelemetryEventNames.SettingsDrawerClosed,
            new Dictionary<string, object?>
            {
                ["page_id"] = pageId,
                ["drawer_title"] = drawerTitle
            },
            forceFlush: false);
    }

    public void TrackDesktopComponentPlaced(DesktopComponentPlacementSnapshot placement, string source)
    {
        CaptureEvent(
            TelemetryEventNames.DesktopComponentPlaced,
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateAfter: DescribePlacement(placement),
            forceFlush: false);
    }

    public void TrackDesktopComponentMoved(
        DesktopComponentPlacementSnapshot before,
        DesktopComponentPlacementSnapshot after,
        string source)
    {
        CaptureEvent(
            TelemetryEventNames.DesktopComponentMoved,
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(before),
            stateAfter: DescribePlacement(after),
            forceFlush: false);
    }

    public void TrackDesktopComponentResized(
        DesktopComponentPlacementSnapshot before,
        DesktopComponentPlacementSnapshot after,
        string source)
    {
        CaptureEvent(
            TelemetryEventNames.DesktopComponentResized,
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(before),
            stateAfter: DescribePlacement(after),
            forceFlush: false);
    }

    public void TrackDesktopComponentDeleted(DesktopComponentPlacementSnapshot before, string source)
    {
        CaptureEvent(
            TelemetryEventNames.DesktopComponentDeleted,
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(before),
            forceFlush: false);
    }

    public void TrackDesktopComponentEditorOpened(DesktopComponentPlacementSnapshot placement, string source)
    {
        CaptureEvent(
            TelemetryEventNames.DesktopComponentEditorOpened,
            new Dictionary<string, object?>
            {
                ["source"] = source
            },
            stateBefore: DescribePlacement(placement),
            forceFlush: false);
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
            // 释放排在 client 之后：_client.Dispose() 自己会收尾那趟 flush，
            // 先把源 Dispose 掉会让它在还有注册时抛 ObjectDisposedException（症状＝最后一次上报静默丢失）
            CancellationHelper.CancelAndDispose(_cts);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PostHogUsage", "Error disposing usage telemetry service.", ex);
        }
    }

    /// <summary>
    /// 首启基线事件：只在用量遥测已启用时发。<c>UsageTelemetryConsentTests</c> 用 <see cref="IUsageTelemetryClient"/>
    /// 这条接缝钉住了行为（关掉时一个字节都不发、打开时在那一刻发且只发一次）。
    /// 这道闸与 <see cref="RefreshEnabledState"/> 里"只在已启用分支调用"看着重复，变异验证说不是：
    /// 只摘这道闸会绿（调用点摆对时行为本来就该这样），但把发送点挪回 <see cref="Initialize"/> 而留着这道闸也绿——
    /// 那一刻 <c>_isUsageEnabled</c> 还没被读、仍是默认 false，正是这道闸挡住了 2026-09-30 那个 bug 的形状。
    /// 所以别按"另一道已经够了"删它。
    /// </summary>
    private void EnsureBaselineEventSent()
    {
        try
        {
            if (!_isUsageEnabled)
            {
                return;
            }

            var identity = TelemetryIdentityService.Instance;
            if (identity.HasReportedBaseline)
            {
                return;
            }

            var distinctId = identity.TelemetryId;
            var personProps = new Dictionary<string, object?>
            {
                ["install_id"] = identity.InstallId,
                ["telemetry_id"] = identity.TelemetryId,
                ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
                ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
                ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
                ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
                ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture(),
                ["runtime_version"] = TelemetryEnvironmentInfo.GetRuntimeVersion(),
                ["language"] = TelemetryEnvironmentInfo.GetSystemLanguage(),
                ["os_build"] = TelemetryEnvironmentInfo.GetOsBuild(),
                ["clr_version"] = TelemetryEnvironmentInfo.GetClrVersion(),
                ["language_display_name"] = TelemetryEnvironmentInfo.GetSystemLanguageDisplayName(),
                ["render_mode"] = TelemetryEnvironmentInfo.GetRenderMode()
            };

            _ = _client.IdentifyAsync(distinctId, personProps, _cts.Token);

            _client.Capture(distinctId, TelemetryEventNames.AppFirstLaunch, personProps);

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
            TelemetryEventNames.AppSessionStart,
            new Dictionary<string, object?>
            {
                ["source"] = source,
                ["launch_id"] = _launchId,
                ["session_start_utc"] = _sessionStartUtc.ToString("o"),
                ["local_hour"] = _sessionStartUtc.ToLocalTime().Hour,
                ["day_part"] = TelemetryEnvironmentInfo.GetLocalDayPart(_sessionStartUtc),
                ["timezone"] = TimeZoneInfo.Local.Id
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
            TelemetryEventNames.AppSessionEnd,
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

        if (e.Scope != AirAppSettingsScope.App ||
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
            ["event_display_name"] = TelemetryEventNames.DisplayName(eventName)
        };

        if (payload is not null)
        {
            foreach (var kvp in payload)
            {
                properties[kvp.Key] = kvp.Value;
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

        _client.Capture(distinctId, eventName, properties);

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
            ["component_name"] = placement.ComponentName ?? placement.ComponentId,
            ["page_index"] = placement.PageIndex,
            ["row"] = placement.Row,
            ["column"] = placement.Column,
            ["width_cells"] = placement.WidthCells,
            ["height_cells"] = placement.HeightCells
        };
    }
}
