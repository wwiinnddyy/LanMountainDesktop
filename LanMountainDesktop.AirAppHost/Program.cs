using Avalonia;

namespace LanMountainDesktop.AirAppHost;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<AirApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
