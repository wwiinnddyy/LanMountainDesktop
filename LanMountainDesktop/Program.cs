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

namespace LanMountainDesktop;

public sealed class Program
{
    internal static string StartupRenderMode { get; private set; } = AppRenderingModeHelper.Default;

    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        RegisterGlobalExceptionLogging();
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

        DesktopBootstrap.InitializeStartupServices(
            InitializeTelemetryIdentity,
            InitializeCrashTelemetry,
            InitializeUsageTelemetry,
            ScheduleWhiteboardNoteStartupCleanup);

        var diagnostics = StartupDiagnosticsService.Run(args);
        StartupDiagnosticsService.ShowLegacyExecutableWarningIfNeeded(diagnostics);

        try
        {
            var renderMode = LoadConfiguredRenderMode();
            StartupRenderMode = renderMode;
            AppLogger.Info("Startup", $"Resolved render mode '{renderMode}'.");
            App.CurrentSingleInstanceService = singleInstance;
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

    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaApp(AppRenderingModeHelper.Default);
    }

    public static AppBuilder BuildAvaloniaApp(string renderMode)
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
            var exception = eventArgs.ExceptionObject as Exception
                ?? new Exception(eventArgs.ExceptionObject?.ToString() ?? "Unhandled exception.");

            AppLogger.Critical(
                "UnhandledException",
                $"Unhandled exception. IsTerminating={eventArgs.IsTerminating}",
                exception);

            try
            {
                TelemetryServices.Crash?.CaptureUnhandledException(
                    exception,
                    "AppDomain.UnhandledException",
                    eventArgs.IsTerminating);
            }
            catch (Exception telemetryException)
            {
                AppLogger.Warn("UnhandledException", "Failed to forward unhandled exception to crash telemetry.", telemetryException);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("TaskScheduler", "Unobserved task exception.", eventArgs.Exception);

            try
            {
                TelemetryServices.Crash?.CaptureTaskException(
                    eventArgs.Exception,
                    "TaskScheduler.UnobservedTaskException");
            }
            catch (Exception telemetryException)
            {
                AppLogger.Warn("TaskScheduler", "Failed to forward task exception to crash telemetry.", telemetryException);
            }

            eventArgs.SetObserved();
        };
    }

    private static void InitializeTelemetryIdentity()
    {
        try
        {
            TelemetryIdentityService.Initialize(HostSettingsFacadeProvider.GetOrCreate());
            AppLogger.Info(
                "Startup",
                $"Telemetry identity initialized. InstallId={TelemetryIdentityService.Instance.InstallId}; TelemetryId={TelemetryIdentityService.Instance.TelemetryId}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize telemetry identity service.", ex);
        }
    }

    private static void InitializeCrashTelemetry()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var crashTelemetry = new SentryCrashTelemetryService(settingsFacade);
            TelemetryServices.Crash = crashTelemetry;
            crashTelemetry.Initialize();
            AppLogger.Info("Startup", $"Crash telemetry initialized. Enabled={crashTelemetry.IsEnabled}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize crash telemetry service.", ex);
        }
    }

    private static void InitializeUsageTelemetry()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var usageTelemetry = new PostHogUsageTelemetryService(settingsFacade);
            TelemetryServices.Usage = usageTelemetry;
            usageTelemetry.Initialize();
            AppLogger.Info("Startup", $"Usage telemetry initialized. Enabled={usageTelemetry.IsUsageEnabled}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize usage telemetry service.", ex);
        }
    }
}
