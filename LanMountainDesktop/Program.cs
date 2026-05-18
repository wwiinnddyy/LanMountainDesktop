using System;
using System.Threading.Tasks;
using Avalonia;
using LanMountainDesktop.DesktopHost;
using LanMountainDesktop.Models;
using LanMountainDesktop.Plugins;
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
        AppDataPathProvider.Initialize(args);
        DevPluginOptions.Parse(args);
        RegisterGlobalExceptionLogging();

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
            LoadChromePatchState();
            InstallChromePatchersIfNeeded();
            BuildAvaloniaApp(renderMode).StartWithClassicDesktopLifetime(args);
            AppLogger.Info("Startup", "Application exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.Critical("Startup", "Application terminated during startup.", ex);
            throw;
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

    private static void LoadChromePatchState()
    {
        try
        {
            var snapshot = HostSettingsFacadeProvider.GetOrCreate()
                .Settings
                .LoadSnapshot<AppSettingsSnapshot>(LanMountainDesktop.PluginSdk.SettingsScope.App);
            if (OperatingSystem.IsWindows())
            {
                LanMountainDesktop.Platform.Windows.ChromePatchState.UseSystemChrome = snapshot.UseSystemChrome;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to load chrome patch state. Falling back to FA chrome.", ex);
        }
    }

    private static void InstallChromePatchersIfNeeded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        if (arch != System.Runtime.InteropServices.Architecture.X64 &&
            arch != System.Runtime.InteropServices.Architecture.X86)
        {
            return;
        }

        try
        {
            LanMountainDesktop.Platform.Windows.PatcherEntrance.InstallPatchers();
            AppLogger.Info("Startup", $"Chrome patchers installed. UseSystemChrome={LanMountainDesktop.Platform.Windows.ChromePatchState.UseSystemChrome}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to install chrome patchers.", ex);
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
