using Avalonia;
using Avalonia.Win32;

namespace LanDesktopPLONDS.Installer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        InstallerStartupDiagnostics.Initialize();
        try
        {
            InstallerStartupDiagnostics.Log("Preparing native dependencies.");
            if (!NativeDependencyBootstrapper.TryPrepare())
            {
                throw new InvalidOperationException("Failed to prepare native dependencies.");
            }

            InstallerStartupDiagnostics.Log("Starting Avalonia desktop lifetime.");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            InstallerStartupDiagnostics.ReportFatal("The installer failed to start.", ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.RedirectionSurface]
            });
    }
}
