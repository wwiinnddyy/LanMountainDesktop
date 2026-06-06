using Avalonia;

namespace LanDesktopPLONDS.Installer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (!NativeDependencyBootstrapper.TryPrepare())
            {
                System.Diagnostics.Debug.WriteLine("[Program] Failed to prepare native dependencies, but continuing...");
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Program] Unhandled exception: {ex}");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
