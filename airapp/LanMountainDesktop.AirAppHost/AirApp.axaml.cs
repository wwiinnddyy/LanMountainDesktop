using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LanMountainDesktop.AirAppHost;

public sealed partial class AirApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = AirAppLaunchOptions.Parse(desktop.Args ?? []);
            desktop.MainWindow = new AirAppWindow(options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
