using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LanMountainDesktop.Launcher.Views;

internal partial class SplashWindow : Window
{
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
