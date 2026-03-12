using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.WebView.Desktop;
using LanMountainDesktop.Services;

namespace LanMountainDesktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
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

        var diagnostics = StartupDiagnosticsService.Run(args);
        StartupDiagnosticsService.ShowLegacyExecutableWarningIfNeeded(diagnostics);

        try
        {
            var renderMode = LoadConfiguredRenderMode();
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

    // Avalonia configuration, don't remove; also used by visual designer.
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
            return AppRenderingModeHelper.Normalize(new AppSettingsService().Load().AppRenderMode);
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
            // The previous process already exited before we started waiting.
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
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("TaskScheduler", "Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }
}
