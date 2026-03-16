using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using Sentry;

namespace LanMountainDesktop.Services;

public sealed class DeviceIdService
{
    private static DeviceIdService? _instance;
    private string? _deviceId;
    private string? _persistentUserId;  // 持久化的用户ID，用于关联设备
    private readonly ISettingsFacadeService _settingsFacade;
    private bool _isInitialized;

    public static DeviceIdService Instance => _instance ?? throw new InvalidOperationException("DeviceIdService not initialized");

    public DeviceIdService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
    }

    public static void Initialize(ISettingsFacadeService settingsFacade)
    {
        _instance = new DeviceIdService(settingsFacade);
        _instance.EnsureDeviceId();
    }

    public string DeviceId
    {
        get
        {
            if (_deviceId is null)
            {
                throw new InvalidOperationException("DeviceId not initialized");
            }
            return _deviceId;
        }
    }

    // 持久化的用户ID，用于跨设备关联用户
    public string PersistentUserId
    {
        get
        {
            if (_persistentUserId is null)
            {
                throw new InvalidOperationException("PersistentUserId not initialized");
            }
            return _persistentUserId;
        }
    }

    private void EnsureDeviceId()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);

            // 初始化或生成持久化用户ID（只生成一次，永不改变）
            if (string.IsNullOrEmpty(snapshot.PersistentUserId))
            {
                snapshot.PersistentUserId = GeneratePersistentUserId();
                AppLogger.Info("DeviceId", $"Generated new persistent user ID: {snapshot.PersistentUserId}");
            }
            _persistentUserId = snapshot.PersistentUserId;

            // 初始化或生成设备ID（可以刷新）
            if (string.IsNullOrEmpty(snapshot.DeviceId))
            {
                snapshot.DeviceId = GenerateDeviceId();
                _settingsFacade.Settings.SaveSnapshot(
                    SettingsScope.App,
                    snapshot,
                    changedKeys: [nameof(AppSettingsSnapshot.DeviceId), nameof(AppSettingsSnapshot.PersistentUserId)]);
                _deviceId = snapshot.DeviceId;
                AppLogger.Info("DeviceId", $"Generated new device ID: {_deviceId}");
            }
            else
            {
                _deviceId = snapshot.DeviceId;
                AppLogger.Info("DeviceId", $"Loaded existing device ID: {_deviceId}");
            }
        }
        catch (Exception ex)
        {
            _deviceId = GenerateDeviceId();
            _persistentUserId = GeneratePersistentUserId();
            AppLogger.Warn("DeviceId", $"Failed to persist device ID, using generated ID: {_deviceId}", ex);
        }
    }

    private static string GenerateDeviceId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var deviceInfo = $"{Environment.MachineName}|{Environment.ProcessorCount}|{Environment.OSVersion}|{Environment.UserName}|{timestamp}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(deviceInfo));
        return Convert.ToHexString(hash)[..32].ToLower();
    }

    private static string GeneratePersistentUserId()
    {
        // 生成一个永久性的用户ID，基于机器名和用户名的哈希
        var userInfo = $"{Environment.MachineName}|{Environment.UserName}|LanMountainDesktop";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(userInfo));
        return Convert.ToHexString(hash)[..32].ToLower();
    }
}

public sealed class UserBehaviorAnalyticsService : IDisposable
{
    private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
    private const string PostHogHost = "https://us.i.posthog.com/capture/";

    private bool _isEnabled;
    private bool _isInitialized;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly DeviceIdService _deviceIdService;
    private readonly Queue<UserBehaviorEvent> _eventQueue = new();
    private readonly object _queueLock = new();
    private System.Threading.Timer? _flushTimer;
    private readonly PluginSdk.ISettingsService _settingsService;

    public UserBehaviorAnalyticsService(ISettingsFacadeService settingsFacade, DeviceIdService deviceIdService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _settingsService = settingsFacade.Settings;
        _deviceIdService = deviceIdService ?? throw new ArgumentNullException(nameof(deviceIdService));
        _settingsService.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PluginSdk.SettingsChangedEvent e)
    {
        if (e.Scope == PluginSdk.SettingsScope.App &&
            e.ChangedKeys is not null &&
            (e.ChangedKeys.Contains("UploadAnonymousCrashData") || e.ChangedKeys.Contains("UploadAnonymousUsageData")))
        {
            AppLogger.Info("UserBehaviorAnalytics", "Settings changed, refreshing enabled state.");
            RefreshEnabledState();
        }
    }

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        RefreshEnabledState();

        try
        {
            _flushTimer = new System.Threading.Timer(
                _ => FlushEvents(),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30));

            // 发送PostHog标准的$pageview事件用于统计日活（始终发送，不受开关影响）
            CaptureEvent("$pageview", new Dictionary<string, object>
            {
                { "$current_url", "app://main" },
                { "$title", "LanMountainDesktop" }
            });

            // 发送应用启动事件（始终发送，用于统计用户数量）
            CaptureEvent("app_online", new Dictionary<string, object>
            {
                { "event_type", "app_start" },
                { "analytics_enabled", _isEnabled }
            });

            AppLogger.Info("UserBehaviorAnalytics", $"Analytics initialized. DeviceId={_deviceIdService.DeviceId}, Enabled={_isEnabled}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to initialize analytics.", ex);
        }
    }

    public void TrackClick(string componentName, string? action = null)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("ui_click", new Dictionary<string, object>
        {
            { "component", componentName },
            { "action", action ?? "click" }
        });
    }

    public void TrackComponentDrag(string componentId, string action)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("component_drag", new Dictionary<string, object>
        {
            { "component_id", componentId },
            { "action", action }
        });
    }

    public void TrackComponentDrop(string componentId, string targetPosition)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("component_drop", new Dictionary<string, object>
        {
            { "component_id", componentId },
            { "target_position", targetPosition }
        });
    }

    public void TrackSettingsOpen(string settingsPage)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("settings_open", new Dictionary<string, object>
        {
            { "page", settingsPage }
        });
    }

    public void TrackSettingsChange(string settingsPage, string settingKey, string? oldValue, string newValue)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("settings_change", new Dictionary<string, object>
        {
            { "page", settingsPage },
            { "key", settingKey },
            { "old_value", oldValue ?? "" },
            { "new_value", newValue }
        });
    }

    public void TrackSettingsClose(string settingsPage)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("settings_close", new Dictionary<string, object>
        {
            { "page", settingsPage }
        });
    }

    public void TrackUpdateAction(string action, string? version = null)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        var props = new Dictionary<string, object>
        {
            { "action", action }
        };

        if (version is not null)
        {
            props["version"] = version;
        }

        CaptureEvent("update_action", props);
    }

    public void TrackRestartAction(string action)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("restart_action", new Dictionary<string, object>
        {
            { "action", action }
        });
    }

    public void TrackNavigation(string fromPage, string toPage)
    {
        if (!_isEnabled || !_isInitialized)
        {
            return;
        }

        CaptureEvent("navigation", new Dictionary<string, object>
        {
            { "from", fromPage },
            { "to", toPage }
        });
    }

    public void SendCrashEvent()
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            var properties = new Dictionary<string, object>
            {
                { "app_version", GetAppVersion() },
                { "event_time", DateTimeOffset.UtcNow.ToString("o") },
                { "event_type", "app_crash" }
            };

            CaptureEvent("app_crash", properties);
            FlushEvents();

            AppLogger.Info("UserBehaviorAnalytics", $"Crash event sent. DeviceId={_deviceIdService.DeviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to send crash event.", ex);
        }
    }

    public void SendShutdownEvent()
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            var properties = new Dictionary<string, object>
            {
                { "app_version", GetAppVersion() },
                { "event_time", DateTimeOffset.UtcNow.ToString("o") },
                { "event_type", "app_shutdown" }
            };

            if (_isEnabled)
            {
                properties["os_name"] = GetOsName();
                properties["os_version"] = GetOsVersion();
                properties["device_name"] = GetDeviceName();
                properties["device_model"] = GetDeviceModel();
                properties["device_arch"] = GetDeviceArchitecture();
                properties["language"] = GetSystemLanguage();
            }

            CaptureEvent("app_shutdown", properties);
            FlushEvents();

            AppLogger.Info("UserBehaviorAnalytics", $"Shutdown event sent. DeviceId={_deviceIdService.DeviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to send shutdown event.", ex);
        }
    }

    public void RefreshEnabledState()
    {
        try
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            var newEnabled = snapshot.UploadAnonymousUsageData;

            if (_isEnabled != newEnabled)
            {
                _isEnabled = newEnabled;
                AppLogger.Info("UserBehaviorAnalytics", $"User behavior analytics enabled state changed to '{_isEnabled}'.");

                if (_isEnabled && _isInitialized)
                {
                    CaptureEvent("analytics_enabled", new Dictionary<string, object>());
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to refresh analytics enabled state.", ex);
            _isEnabled = false;
        }
    }

    public void CaptureEvent(string eventName, Dictionary<string, object>? properties = null)
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            // 基础事件（$pageview, app_online, app_shutdown等）始终发送，用于统计用户数量
            bool isBasicEvent = eventName.StartsWith("$") || 
                               eventName == "app_online" || 
                               eventName == "app_shutdown" ||
                               eventName == "$identify";

            // 非基础事件只有在启用时才发送
            if (!isBasicEvent && !_isEnabled)
            {
                return;
            }

            var eventData = new UserBehaviorEvent
            {
                Event = eventName,
                DistinctId = _deviceIdService.PersistentUserId,  // 使用持久化用户ID
                Timestamp = DateTimeOffset.UtcNow,
                Properties = properties ?? new Dictionary<string, object>(),
                IncludeDetailedData = _isEnabled
            };

            lock (_queueLock)
            {
                _eventQueue.Enqueue(eventData);

                if (_eventQueue.Count >= 20)
                {
                    FlushEvents();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", $"Failed to capture event '{eventName}'.", ex);
        }
    }

    public void CapturePageView(string pageName, string? sourcePage = null)
    {
        var properties = new Dictionary<string, object>
        {
            { "page_name", pageName }
        };

        if (!string.IsNullOrEmpty(sourcePage))
        {
            properties["source_page"] = sourcePage;
        }

        CaptureEvent("page_view", properties);
    }

    public void CaptureFeatureUsage(string featureName, string action)
    {
        CaptureEvent("feature_usage", new Dictionary<string, object>
        {
            { "feature_name", featureName },
            { "action", action }
        });
    }

    private void FlushEvents()
    {
        List<UserBehaviorEvent> eventsToSend;

        lock (_queueLock)
        {
            if (_eventQueue.Count == 0)
            {
                return;
            }

            eventsToSend = new List<UserBehaviorEvent>();
            while (_eventQueue.Count > 0 && eventsToSend.Count < 20)
            {
                eventsToSend.Add(_eventQueue.Dequeue());
            }
        }

        try
        {
            SendEventsToPostHog(eventsToSend);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to send events to PostHog.", ex);

            lock (_queueLock)
            {
                foreach (var evt in eventsToSend)
                {
                    if (_eventQueue.Count < 100)
                    {
                        _eventQueue.Enqueue(evt);
                    }
                }
            }
        }
    }

    private void SendEventsToPostHog(List<UserBehaviorEvent> events)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            var firstEvent = events.FirstOrDefault();
            if (firstEvent is not null)
            {
                SendIdentifyToPostHog(client, firstEvent.DistinctId);
            }

            foreach (var e in events)
            {
                var properties = new Dictionary<string, object>
                {
                    { "distinct_id", e.DistinctId }
                };

                if (e.IncludeDetailedData)
                {
                    properties["$os"] = GetOsName();
                    properties["$os_version"] = GetOsVersion();
                    properties["$app_version"] = GetAppVersion();
                    properties["$device_id"] = e.DistinctId;
                }

                foreach (var kvp in e.Properties)
                {
                    properties[kvp.Key] = kvp.Value;
                }

                var requestBody = new Dictionary<string, object>
                {
                    { "api_key", PostHogApiKey },
                    { "event", e.Event },
                    { "timestamp", e.Timestamp.ToString("o") },
                    { "properties", properties }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var bytes = Encoding.UTF8.GetBytes(json);

                var content = new System.Net.Http.ByteArrayContent(bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = client.PostAsync(PostHogHost, content).GetAwaiter().GetResult();
                var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warn("UserBehaviorAnalytics", $"PostHog API error for event '{e.Event}': {response.StatusCode} - {responseBody}");
                }
            }

            AppLogger.Info("UserBehaviorAnalytics", $"Successfully sent {events.Count} events to PostHog.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to send events to PostHog API.", ex);
        }
    }

    private void SendIdentifyToPostHog(System.Net.Http.HttpClient client, string distinctId)
    {
        try
        {
            var userProperties = new Dictionary<string, object>
            {
                { "$app_version", GetAppVersion() },
                { "$os", GetOsName() },
                { "$os_version", GetOsVersion() },
                { "$device_type", GetDeviceModel() },
                { "$device_id", _deviceIdService.DeviceId }  // 当前设备ID
            };

            // PostHog正确的$identify格式
            // 使用PersistentUserId作为distinct_id，确保设备ID刷新后仍能关联到同一用户
            var requestBody = new Dictionary<string, object>
            {
                { "api_key", PostHogApiKey },
                { "event", "$identify" },
                { "distinct_id", _deviceIdService.PersistentUserId },  // 使用持久化用户ID
                { "timestamp", DateTimeOffset.UtcNow.ToString("o") },
                { "properties", new Dictionary<string, object>
                    {
                        { "$set", userProperties },
                        { "$set_once", new Dictionary<string, object>
                            {
                                { "first_app_open", DateTimeOffset.UtcNow.ToString("o") }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var bytes = Encoding.UTF8.GetBytes(json);

            var content = new System.Net.Http.ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var response = client.PostAsync(PostHogHost, content).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            AppLogger.Info("UserBehaviorAnalytics", $"PostHog identify response: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warn("UserBehaviorAnalytics", $"PostHog identify failed: {response.StatusCode} - {responseBody}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Failed to send identify to PostHog.", ex);
        }
    }

    private static Dictionary<string, object> GetEventProperties(UserBehaviorEvent e)
    {
        var props = new Dictionary<string, object>
        {
            { "$os", GetOsName() },
            { "$os_version", GetOsVersion() },
            { "$app_version", GetAppVersion() },
            { "$device_id", e.DistinctId }
        };

        foreach (var kvp in e.Properties)
        {
            props[kvp.Key] = kvp.Value;
        }

        return props;
    }

    public bool IsEnabled => _isEnabled;

    public string DeviceId => _deviceIdService.DeviceId;

    private static string GetAppVersion()
    {
        var assembly = typeof(UserBehaviorAnalyticsService).Assembly;
        var version = assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string GetOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        return "Unknown";
    }

    private static string GetOsVersion()
    {
        try { return Environment.OSVersion.VersionString ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetDeviceName()
    {
        try { return Environment.MachineName ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetDeviceModel()
    {
        var osDesc = RuntimeInformation.OSDescription;
        if (osDesc.Contains("Windows")) return "Windows PC";
        if (osDesc.Contains("Linux")) return "Linux PC";
        if (osDesc.Contains("Darwin")) return "Mac";
        return osDesc;
    }

    private static string GetDeviceArchitecture()
    {
        return RuntimeInformation.OSArchitecture.ToString();
    }

    private static string GetSystemLanguage()
    {
        try { return System.Globalization.CultureInfo.CurrentUICulture.Name ?? "en-US"; }
        catch { return "en-US"; }
    }

    private static string GetOsBuild()
    {
        try { return Environment.OSVersion.Version.Build.ToString() ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static int GetProcessorCount()
    {
        return Environment.ProcessorCount;
    }

    private static long GetTotalMemoryMB()
    {
        try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); }
        catch { return 0; }
    }

    private static string GetRuntimeVersion()
    {
        return Environment.Version.ToString();
    }

    private static string GetClrVersion()
    {
        return Environment.Version.ToString();
    }

    private static string GetDotNetVersion()
    {
        return Environment.Version.ToString();
    }

    public void Dispose()
    {
        try
        {
            _flushTimer?.Dispose();
            FlushEvents();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UserBehaviorAnalytics", "Error disposing analytics service.", ex);
        }
    }

    private class UserBehaviorEvent
    {
        public string Event { get; set; } = string.Empty;
        public string DistinctId { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        public bool IncludeDetailedData { get; set; }
    }
}

public static class DictionaryExtensions
{
    public static Dictionary<string, object> Merge(this Dictionary<string, object> first, Dictionary<string, object> second)
    {
        var result = new Dictionary<string, object>(first);
        foreach (var kvp in second)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }
}

public sealed class CrashReportService
{
    private const string SentryDsn = "https://f2aad3a1c63b5f2213ad82683ce93c06@o4511049423257600.ingest.us.sentry.io/4511049425813504";

    private bool _isInitialized;
    private bool _isEnabled;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly DeviceIdService _deviceIdService;
    private readonly PluginSdk.ISettingsService _settingsService;

    public CrashReportService(ISettingsFacadeService settingsFacade, DeviceIdService deviceIdService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _settingsService = settingsFacade.Settings;
        _deviceIdService = deviceIdService ?? throw new ArgumentNullException(nameof(deviceIdService));
        _settingsService.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PluginSdk.SettingsChangedEvent e)
    {
        if (e.Scope == PluginSdk.SettingsScope.App &&
            e.ChangedKeys is not null &&
            (e.ChangedKeys.Contains("UploadAnonymousCrashData") || e.ChangedKeys.Contains("UploadAnonymousUsageData")))
        {
            AppLogger.Info("CrashReport", "Settings changed, refreshing enabled state.");
            RefreshEnabledState();
        }
    }

    public void RefreshEnabledState()
    {
        try
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            var newEnabled = snapshot.UploadAnonymousCrashData;

            if (_isEnabled != newEnabled)
            {
                _isEnabled = newEnabled;
                AppLogger.Info("CrashReport", $"Crash reporting enabled state changed to '{_isEnabled}'.");

                if (_isEnabled && !_isInitialized)
                {
                    InitializeSentry();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CrashReport", "Failed to refresh crash reporting enabled state.", ex);
            _isEnabled = false;
        }
    }

    private void InitializeSentry()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = SentryDsn;
                options.AutoSessionTracking = true;
                options.AttachStacktrace = true;
                options.MaxBreadcrumbs = 100;
                options.Release = GetAppVersion();
                options.Environment = GetEnvironment();
            });

            ConfigureCrashReportingScope();
            
            // 显式开始会话跟踪
            SentrySdk.StartSession();

            AppLogger.Info("CrashReport", $"Sentry crash reporting initialized. DeviceId={_deviceIdService.DeviceId}");

#if DEBUG
            SentrySdk.CaptureMessage($"Crash reporting enabled - Debug mode test. DeviceId={_deviceIdService.DeviceId}");
#endif
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CrashReport", "Failed to initialize Sentry crash reporting.", ex);
            _isInitialized = false;
        }
    }

    private void ConfigureCrashReportingScope()
    {
        try
        {
            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Id = _deviceIdService.DeviceId
                };

                scope.SetTag("data_type", "crash_report");
                scope.SetTag("device_id", _deviceIdService.DeviceId);
                scope.SetTag("device_name", GetDeviceName());
                scope.SetTag("device_model", GetDeviceModel());
                scope.SetTag("device_arch", GetDeviceArchitecture());
                scope.SetTag("os_name", GetOsName());
                scope.SetTag("os_version", GetOsVersion());
                scope.SetTag("language", GetSystemLanguage());
            });

            AppLogger.Info("CrashReport", $"Crash reporting scope configured. DeviceId={_deviceIdService.DeviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CrashReport", "Failed to configure crash reporting scope.", ex);
        }
    }

    public bool IsEnabled => _isEnabled;

    public string DeviceId => _deviceIdService.DeviceId;

    public void SendShutdownEvent()
    {
        try
        {
            if (_isEnabled && _isInitialized)
            {
                // 结束Sentry会话
                SentrySdk.EndSession();
                SentrySdk.Flush(TimeSpan.FromSeconds(3));
                AppLogger.Info("CrashReport", $"Shutdown event sent via Sentry. DeviceId={_deviceIdService.DeviceId}");
                return;
            }

            if (!_isInitialized)
            {
                SentrySdk.Init(options =>
                {
                    options.Dsn = SentryDsn;
                    options.AutoSessionTracking = false;
                    options.Release = GetAppVersion();
                    options.Environment = GetEnvironment();
                });
            }

            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Id = _deviceIdService.DeviceId
                };
                scope.SetTag("data_type", "shutdown");
                scope.SetTag("device_id", _deviceIdService.DeviceId);
                scope.SetTag("app_version", GetAppVersion());
            });

            SentrySdk.CaptureMessage($"app_shutdown - DeviceId={_deviceIdService.DeviceId}");
            SentrySdk.Flush(TimeSpan.FromSeconds(3));

            AppLogger.Info("CrashReport", $"Shutdown event sent. DeviceId={_deviceIdService.DeviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CrashReport", "Failed to send shutdown event.", ex);
        }
    }

    private static string GetDeviceName()
    {
        try { return Environment.MachineName ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetDeviceModel()
    {
        var osDesc = RuntimeInformation.OSDescription;
        if (osDesc.Contains("Windows")) return "Windows PC";
        if (osDesc.Contains("Linux")) return "Linux PC";
        if (osDesc.Contains("Darwin")) return "Mac";
        return osDesc;
    }

    private static string GetDeviceArchitecture()
    {
        return RuntimeInformation.OSArchitecture.ToString();
    }

    private static string GetOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        return "Unknown";
    }

    private static string GetOsVersion()
    {
        try { return Environment.OSVersion.VersionString ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetSystemLanguage()
    {
        try { return System.Globalization.CultureInfo.CurrentUICulture.Name ?? "en-US"; }
        catch { return "en-US"; }
    }

    private static string GetAppVersion()
    {
        var version = typeof(CrashReportService).Assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string GetEnvironment()
    {
#if DEBUG
        return "development";
#else
        return "production";
#endif
    }
}
