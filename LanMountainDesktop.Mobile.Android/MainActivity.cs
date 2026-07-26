using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace LanMountainDesktop.Mobile.Android;

/// <summary>
/// Android 入口 Activity。承载 MobileApp（单视图组件面板）。
/// Avalonia 12 起 AppBuilder 定制移至 <see cref="MobileAndroidApplication"/>。
/// </summary>
[Activity(
    Label = "阑山桌面",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
