using Avalonia;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.AirAppHost;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        AppDataPathProvider.Initialize(args);
        RegisterGlobalExceptionLogging();
        AppLogger.Info("AirAppHost", $"Starting. Args='{string.Join(" ", args)}'.");

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
            AppLogger.Info("AirAppHost", "Exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.Critical("AirAppHost", "Unhandled startup exception.", ex);
            throw;
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<AirApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLogger.Critical(
                "AirAppHost",
                "Unhandled AppDomain exception.",
                e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("AirAppHost", "Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
    }
}
