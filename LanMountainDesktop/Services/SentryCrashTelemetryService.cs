using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using Sentry;

namespace LanMountainDesktop.Services;

public sealed class SentryCrashTelemetryService : IDisposable
{
    private const string SentryDsn = "https://f2aad3a1c63b5f2213ad82683ce93c06@o4511049423257600.ingest.us.sentry.io/4511049425813504";
    private const string AutoIpAddress = "{{auto}}";

    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISettingsService _settingsService;
    private readonly object _syncRoot = new();

    private IDisposable? _sentryHandle;
    private bool _isInitialized;
    private bool _isEnabled;
    private bool _disposed;

    public SentryCrashTelemetryService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _settingsService = settingsFacade.Settings;
        _settingsService.Changed += OnSettingsChanged;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return _isInitialized && _isEnabled && SentrySdk.IsEnabled;
            }
        }
    }

    public void Initialize()
    {
        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
        }

        RefreshEnabledState(force: true);
    }

    public void RefreshEnabledState(bool force = false)
    {
        bool shouldEnable;
        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (!_isInitialized)
            {
                return;
            }

            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            shouldEnable = snapshot.UploadAnonymousCrashData;

            if (!force && _isEnabled == shouldEnable)
            {
                return;
            }
        }

        if (shouldEnable)
        {
            EnableSentry();
            return;
        }

        DisableSentry();
    }

    public void CaptureUnhandledException(Exception exception, string source, bool isTerminating)
    {
        if (exception is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!CanCapture())
            {
                return;
            }
        }

        var eventId = SentrySdk.CaptureException(exception, scope =>
        {
            ApplyCommonScope(scope, source, "unhandled_exception", includeLogTail: true);
            scope.Level = isTerminating ? SentryLevel.Fatal : SentryLevel.Error;
            scope.SetTag("exception_source", source);
            scope.SetTag("is_terminating", isTerminating.ToString());
        });

        AppLogger.Info("SentryCrash", $"Captured unhandled exception from '{source}'. EventId={eventId}.");

        if (isTerminating)
        {
            EndCrashSession();
            SentrySdk.Flush(TimeSpan.FromSeconds(5));
        }
    }

    public void CaptureTaskException(Exception exception, string source)
    {
        if (exception is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!CanCapture())
            {
                return;
            }
        }

        var eventId = SentrySdk.CaptureException(exception, scope =>
        {
            ApplyCommonScope(scope, source, "task_exception", includeLogTail: true);
            scope.Level = SentryLevel.Error;
            scope.SetTag("exception_source", source);
        });

        AppLogger.Info("SentryCrash", $"Captured task exception from '{source}'. EventId={eventId}.");
        SentrySdk.Flush(TimeSpan.FromSeconds(2));
    }

    public void CaptureShutdown(bool isRestart, string source)
    {
        lock (_syncRoot)
        {
            if (!CanCapture())
            {
                return;
            }
        }

        var eventId = SentrySdk.CaptureMessage("application_shutdown", scope =>
        {
            ApplyCommonScope(scope, source, "shutdown", includeLogTail: true);
            scope.Level = SentryLevel.Info;
            scope.SetTag("shutdown_intent", isRestart ? "restart" : "exit");
            scope.SetExtra("shutdown_intent", isRestart ? "restart" : "exit");
        }, SentryLevel.Info);

        AppLogger.Info(
            "SentryCrash",
            $"Captured application shutdown. Source='{source}'; Restart={isRestart}; EventId={eventId}.");

        EndCrashSession();
        SentrySdk.Flush(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            _settingsService.Changed -= OnSettingsChanged;
            DisableSentry();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SentryCrash", "Failed to dispose crash telemetry service.", ex);
        }
    }

    private void EnableSentry()
    {
        lock (_syncRoot)
        {
            if (_isEnabled && _sentryHandle is not null && SentrySdk.IsEnabled)
            {
                return;
            }
        }

        var handle = SentrySdk.Init(options =>
        {
            options.Dsn = SentryDsn;
            options.AutoSessionTracking = true;
            options.AttachStacktrace = true;
            options.SendDefaultPii = true;
            options.MaxBreadcrumbs = 100;
            options.Release = TelemetryEnvironmentInfo.GetAppVersion();
            options.Environment = TelemetryEnvironmentInfo.GetEnvironment();
            options.DisableAppDomainUnhandledExceptionCapture();
            options.DisableUnobservedTaskExceptionCapture();
        });

        lock (_syncRoot)
        {
            if (_disposed)
            {
                handle.Dispose();
                return;
            }

            _sentryHandle?.Dispose();
            _sentryHandle = handle;
            _isEnabled = true;
        }

        SentrySdk.ConfigureScope(scope => ApplyCommonScope(scope, "startup", "startup", includeLogTail: false));
        AppLogger.Info("SentryCrash", "Crash telemetry enabled.");
    }

    private void DisableSentry()
    {
        IDisposable? handle;
        lock (_syncRoot)
        {
            if (!_isEnabled && _sentryHandle is null)
            {
                return;
            }

            _isEnabled = false;
            handle = _sentryHandle;
            _sentryHandle = null;
        }

        try
        {
            EndCrashSession();
            SentrySdk.Flush(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SentryCrash", "Failed to flush Sentry while disabling crash telemetry.", ex);
        }
        finally
        {
            handle?.Dispose();
        }

        AppLogger.Info("SentryCrash", "Crash telemetry disabled.");
    }

    private void EndCrashSession()
    {
        try
        {
            if (SentrySdk.IsEnabled)
            {
                SentrySdk.EndSession(SessionEndStatus.Exited);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SentryCrash", "Failed to end Sentry session.", ex);
        }
    }

    private bool CanCapture()
    {
        return !_disposed && _isInitialized && _isEnabled && SentrySdk.IsEnabled;
    }

    private void ApplyCommonScope(Scope scope, string source, string eventType, bool includeLogTail)
    {
        var installId = TelemetryIdentityService.Instance.InstallId;
        var telemetryId = TelemetryIdentityService.Instance.TelemetryId;

        scope.User = new SentryUser
        {
            Id = telemetryId,
            IpAddress = AutoIpAddress
        };

        scope.SetTag("telemetry_channel", "sentry");
        scope.SetTag("event_type", eventType);
        scope.SetTag("source", source);
        scope.SetTag("install_id", installId);
        scope.SetTag("telemetry_id", telemetryId);
        scope.SetTag("app_version", TelemetryEnvironmentInfo.GetAppVersion());
        scope.SetTag("environment", TelemetryEnvironmentInfo.GetEnvironment());
        scope.SetTag("os_name", TelemetryEnvironmentInfo.GetOsName());
        scope.SetTag("os_version", TelemetryEnvironmentInfo.GetOsVersion());
        scope.SetTag("os_build", TelemetryEnvironmentInfo.GetOsBuild());
        scope.SetTag("device_model", TelemetryEnvironmentInfo.GetDeviceModel());
        scope.SetTag("device_arch", TelemetryEnvironmentInfo.GetDeviceArchitecture());
        scope.SetTag("processor_count", TelemetryEnvironmentInfo.GetProcessorCount().ToString());
        scope.SetTag("total_memory_mb", TelemetryEnvironmentInfo.GetTotalMemoryMB().ToString());
        scope.SetTag("runtime_version", TelemetryEnvironmentInfo.GetRuntimeVersion());
        scope.SetTag("clr_version", TelemetryEnvironmentInfo.GetClrVersion());
        scope.SetTag("language", TelemetryEnvironmentInfo.GetSystemLanguage());
        scope.SetExtra("install_id", installId);
        scope.SetExtra("telemetry_id", telemetryId);
        scope.SetExtra("app_version", TelemetryEnvironmentInfo.GetAppVersion());
        scope.SetExtra("environment", TelemetryEnvironmentInfo.GetEnvironment());
        scope.SetExtra("os_name", TelemetryEnvironmentInfo.GetOsName());
        scope.SetExtra("os_version", TelemetryEnvironmentInfo.GetOsVersion());
        scope.SetExtra("os_build", TelemetryEnvironmentInfo.GetOsBuild());
        scope.SetExtra("device_model", TelemetryEnvironmentInfo.GetDeviceModel());
        scope.SetExtra("device_arch", TelemetryEnvironmentInfo.GetDeviceArchitecture());
        scope.SetExtra("processor_count", TelemetryEnvironmentInfo.GetProcessorCount());
        scope.SetExtra("total_memory_mb", TelemetryEnvironmentInfo.GetTotalMemoryMB());
        scope.SetExtra("runtime_version", TelemetryEnvironmentInfo.GetRuntimeVersion());
        scope.SetExtra("clr_version", TelemetryEnvironmentInfo.GetClrVersion());
        scope.SetExtra("language", TelemetryEnvironmentInfo.GetSystemLanguage());
        scope.SetExtra("log_file_path", AppLogger.LogFilePath);

        if (includeLogTail)
        {
            var logTail = ReadLogTail(maxLines: 200, maxCharacters: 32_768);
            if (!string.IsNullOrWhiteSpace(logTail))
            {
                scope.SetExtra("log_tail", logTail);
                scope.SetExtra("log_tail_line_count", logTail.Count(character => character == '\n') + 1);
                var attachment = new Attachment(
                    AttachmentType.Default,
                    new ByteAttachmentContent(Encoding.UTF8.GetBytes(logTail)),
                    "log-tail.txt",
                    "text/plain");
                scope.AddAttachment(attachment);
            }
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope != SettingsScope.App ||
            e.ChangedKeys is null ||
            !e.ChangedKeys.Contains(nameof(AppSettingsSnapshot.UploadAnonymousCrashData), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        AppLogger.Info("SentryCrash", "Crash telemetry setting changed. Refreshing enabled state.");
        RefreshEnabledState();
    }

    private static string ReadLogTail(int maxLines, int maxCharacters)
    {
        try
        {
            var logFilePath = AppLogger.LogFilePath;
            if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
            {
                return string.Empty;
            }

            var lines = new Queue<string>(Math.Min(maxLines, 256));
            using var reader = File.OpenText(logFilePath);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (lines.Count >= maxLines)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }

            var tail = string.Join(Environment.NewLine, lines);
            if (tail.Length <= maxCharacters)
            {
                return tail;
            }

            return tail[^maxCharacters..];
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SentryCrash", "Failed to read log tail for crash telemetry.", ex);
            return string.Empty;
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SentryCrashTelemetryService));
        }
    }
}
