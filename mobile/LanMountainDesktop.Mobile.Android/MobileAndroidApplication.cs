using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using LanMountainDesktop.Mobile;

namespace LanMountainDesktop.Mobile.Android;

/// <summary>
/// Android 应用级入口。Avalonia 12 中 AppBuilder 的创建/定制由
/// AvaloniaAndroidApplication&lt;TApp&gt; 子类负责（而非 Activity）。
/// </summary>
[Application]
public class MobileAndroidApplication : AvaloniaAndroidApplication<MobileApp>
{
    protected MobileAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
