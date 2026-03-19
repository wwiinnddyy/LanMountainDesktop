using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.WebView.Desktop;
using LanMountainDesktop.DesktopHost;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using Sentry;

namespace LanMountainDesktop;

sealed class Program
{
    internal static string StartupRenderMode { get; private set; } = AppRenderingModeHelper.Default;

    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        RegisterGlobalExceptionLogging();
        DesktopBootstrap.InitializeStartupServices(
            InitializeDeviceId,
            InitializeCrashReporting,
            InitializeUserBehaviorAnalytics,
            ScheduleWhiteboardNoteStartupCleanup);
        var restartParentProcessId = AppRestartService.TryGetRestartParentProcessId(args);

        using var singleInstance = AcquireSingleInstance(restartParentProcessId);
        if (!singleInstance.IsPrimaryInstance)
        {
            if (restartParentProcessId is not null)
            {
                AppLogger.Warn(
                    "Startup",
                    $"Restart relaunch could not acquire the single-instance lock. pid={restartParentProcessId.Value}. Suppressing multi-open activation prompt.");
                return;
            }

            AppLogger.Warn("Startup", "A secondary launch was blocked because another instance is already running.");
            _ = singleInstance.TryNotifyPrimaryInstance(TimeSpan.FromSeconds(2));
            return;
        }

        var diagnostics = StartupDiagnosticsService.Run(args);
        StartupDiagnosticsService.ShowLegacyExecutableWarningIfNeeded(diagnostics);

        try
        {
            var renderMode = LoadConfiguredRenderMode();
            StartupRenderMode = renderMode;
            AppLogger.Info("Startup", $"Resolved render mode '{renderMode}'.");
            App.CurrentSingleInstanceService = singleInstance;
            App.AnalyticsServices = (_userBehaviorAnalyticsService, _crashReportService);
            BuildAvaloniaApp(renderMode).StartWithClassicDesktopLifetime(args);
            AppLogger.Info("Startup", "Application exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.Critical("Startup", "Application terminated during startup.", ex);
            throw;
        }
        finally
        {
            App.CurrentSingleInstanceService = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp(string renderMode = AppRenderingModeHelper.Default)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseDesktopWebView()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsWindows())
        {
            var configuredModes = AppRenderingModeHelper.GetWin32RenderingModes(renderMode);
            if (configuredModes is { Length: > 0 })
            {
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = configuredModes
                });
            }
        }

        return builder;
    }

    private static void ScheduleWhiteboardNoteStartupCleanup()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var deletedCount = new WhiteboardNotePersistenceService().DeleteExpiredNotesBatch(batchSize: 512);
                if (deletedCount > 0)
                {
                    AppLogger.Info("Startup", $"Deleted {deletedCount} expired whiteboard notes during startup maintenance.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Startup", "Failed to run whiteboard note startup maintenance.", ex);
            }
        });
    }

    private static SingleInstanceService AcquireSingleInstance(int? restartParentProcessId)
    {
        var singleInstance = SingleInstanceService.CreateDefault();
        if (singleInstance.IsPrimaryInstance || restartParentProcessId is null)
        {
            return singleInstance;
        }

        AppLogger.Info(
            "Startup",
            $"Restart relaunch detected. Waiting for previous instance pid={restartParentProcessId.Value} to exit before re-acquiring the single-instance lock.");
        singleInstance.Dispose();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        WaitForRestartParentExit(restartParentProcessId.Value, deadline);

        while (DateTime.UtcNow < deadline)
        {
            var retryInstance = SingleInstanceService.CreateDefault();
            if (retryInstance.IsPrimaryInstance)
            {
                AppLogger.Info("Startup", "Restart relaunch acquired the single-instance lock.");
                return retryInstance;
            }

            retryInstance.Dispose();
            Thread.Sleep(150);
        }

        AppLogger.Warn(
            "Startup",
            $"Restart relaunch timed out while waiting for the single-instance lock. pid={restartParentProcessId.Value}.");
        return SingleInstanceService.CreateDefault();
    }

    private static string LoadConfiguredRenderMode()
    {
        try
        {
            var snapshot = HostSettingsFacadeProvider.GetOrCreate()
                .Settings
                .LoadSnapshot<AppSettingsSnapshot>(LanMountainDesktop.PluginSdk.SettingsScope.App);
            return AppRenderingModeHelper.Normalize(snapshot.AppRenderMode);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to load configured render mode. Falling back to default.", ex);
            return AppRenderingModeHelper.Default;
        }
    }

    private static void WaitForRestartParentExit(int processId, DateTime deadlineUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                process.WaitForExit((int)Math.Ceiling(remaining.TotalMilliseconds));
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", $"Failed while waiting for restart parent pid={processId} to exit.", ex);
        }
    }

    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            AppLogger.Critical(
                "UnhandledException",
                $"Unhandled exception. IsTerminating={eventArgs.IsTerminating}",
                eventArgs.ExceptionObject as Exception);

            if (eventArgs.IsTerminating)
            {
                SentrySdk.Flush(TimeSpan.FromSeconds(5));
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("TaskScheduler", "Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private static void InitializeDeviceId()
    {
        try
        {
            DeviceIdService.Initialize(HostSettingsFacadeProvider.GetOrCreate());
            AppLogger.Info("Startup", $"DeviceId initialized: {DeviceIdService.Instance.DeviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize DeviceIdService.", ex);
        }
    }

    private static void InitializeSentryForAnalytics()
    {
        try
        {
            var deviceId = DeviceIdService.Instance.DeviceId;

            SentrySdk.Init(options =>
            {
                options.Dsn = "https://f2aad3a1c63b5f2213ad82683ce93c06@o4511049423257600.ingest.us.sentry.io/4511049425813504";
                options.AutoSessionTracking = true;
                options.Release = GetAppVersion();
                options.Environment = GetEnvironment();
            });

            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Id = deviceId
                };

                scope.SetTag("data_type", "analytics");
                scope.SetTag("device_id", deviceId);
                scope.SetTag("app_version", GetAppVersion());
                scope.SetTag("os_name", GetOsName());
                scope.SetTag("os_version", GetOsVersion());
                scope.SetTag("os_build", GetOsBuild());
                scope.SetTag("device_model", GetDeviceModel());
                scope.SetTag("device_arch", GetDeviceArchitecture());
                scope.SetTag("processor_count", GetProcessorCount().ToString());
                scope.SetTag("total_memory_mb", GetTotalMemoryMB().ToString());
                scope.SetTag("runtime_version", GetRuntimeVersion());
                scope.SetTag("language", GetSystemLanguage());
                scope.SetTag("clr_version", GetClrVersion());
                scope.SetTag("is_64bit", Environment.Is64BitOperatingSystem.ToString());
            });

            SentrySdk.CaptureMessage("user_active");

            AppLogger.Info("Startup", $"Analytics service initialized. DeviceId={deviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize analytics service.", ex);
        }
    }

    private static string GetAppVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return "Unknown";
    }

    private static string GetOsVersion()
    {
        try { return Environment.OSVersion.VersionString ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetOsBuild()
    {
        try { return Environment.OSVersion.Version.Build.ToString() ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetDeviceName()
    {
        try { return Environment.MachineName ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetDeviceModel()
    {
        if (OperatingSystem.IsWindows()) return "Windows PC";
        if (OperatingSystem.IsLinux()) return "Linux PC";
        if (OperatingSystem.IsMacOS()) return "Mac";
        return "Unknown";
    }

    private static string GetDeviceArchitecture()
    {
        return Environment.Is64BitOperatingSystem ? "x64" : "x86";
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

    private static string GetSystemLanguage()
    {
        try { return System.Globalization.CultureInfo.CurrentUICulture.Name ?? "en-US"; }
        catch { return "en-US"; }
    }

    private static string GetClrVersion()
    {
        return Environment.Version.ToString();
    }

    private static CrashReportService? _crashReportService;
    private static UserBehaviorAnalyticsService? _userBehaviorAnalyticsService;

    private static void InitializeCrashReporting()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            _crashReportService = new CrashReportService(settingsFacade, DeviceIdService.Instance);
            _crashReportService.RefreshEnabledState();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize crash reporting service.", ex);
        }
    }

    private static void InitializeUserBehaviorAnalytics()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            _userBehaviorAnalyticsService = new UserBehaviorAnalyticsService(settingsFacade, DeviceIdService.Instance);
            _userBehaviorAnalyticsService.Initialize();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize user behavior analytics service.", ex);
        }
    }

    private static string GetReleaseVersion()
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetName().Version;
        if (version is null)
        {
            return "1.0.0";
        }
        return version.Major >= 0 ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
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
