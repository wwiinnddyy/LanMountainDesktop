using Avalonia;
using Avalonia.WebView.Desktop;
using LanMountainDesktop.Services;
using System;
using System.Threading.Tasks;

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

        using var singleInstance = SingleInstanceService.CreateDefault();
        if (!singleInstance.IsPrimaryInstance)
        {
            AppLogger.Warn("Startup", "A secondary launch was blocked because another instance is already running.");
            var notified = singleInstance.TryNotifyPrimaryInstance(TimeSpan.FromSeconds(2));
            ShowAlreadyRunningNotice(notified);
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

    private static void ShowAlreadyRunningNotice(bool notifiedPrimaryInstance)
    {
        const string caption = "LanMountainDesktop";
        var message = notifiedPrimaryInstance
            ? "应用已打开，不需要多开了。\r\n\r\n已为你切换到正在运行的阑山桌面。"
            : "应用已打开，不需要多开了。\r\n\r\n请切换到正在运行的阑山桌面。";

        WindowsNativeDialogService.ShowInformation(caption, message);
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
        };
    }
}
