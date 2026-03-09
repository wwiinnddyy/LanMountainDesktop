using Avalonia;
using Avalonia.WebView.Desktop;
using LanMountainDesktop.Services;
using System;

namespace LanMountainDesktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp(LoadConfiguredRenderMode())
        .StartWithClassicDesktopLifetime(args);

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
        catch
        {
            return AppRenderingModeHelper.Default;
        }
    }
}
