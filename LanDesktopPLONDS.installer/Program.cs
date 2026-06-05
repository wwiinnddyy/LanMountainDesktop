using Avalonia;

namespace LanDesktopPLONDS.Installer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        NativeDependencyBootstrapper.Prepare();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
