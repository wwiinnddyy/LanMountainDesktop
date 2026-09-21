using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 视觉测试底座本身要有回归保护。此前 headless 会话只起一个裸 Application，App.axaml 的
/// FluentAvalonia 主题、Design* 圆角令牌、SettingsCardStyles 全都不可见，于是任何
/// "渲染出来看看"的断言都只能在无样式环境下跑。VisualTestApp 补上这个入口，下面几条钉住它生效。
///
/// 两条已实测确认的堵点（别再重复试）：
/// 1) 取像素：RenderTargetBitmap.Save 写出 0 字节；Window.CaptureRenderedFrame() 返回 null；
///    RenderTargetBitmap.Render(window) + CopyPixels 取到整幅透明且逐次不一致。
/// 2) 应用样式：App.axaml 的 Application.Resources 在这里读得到，但 Style setter 不生效 ——
///    给 TextBlock 显式设 FontWeight=Bold，attach 到 headless 窗口后仍然是 Bold，
///    而 App.axaml 里那条全局样式要求是 Normal。也就是说现有那批"视觉"测试实际只测了令牌值，
///    并没有测样式应用后的结果。要真做视觉回归，得先把这两环打通。
/// </summary>
public sealed class VisualTestAppHarnessTests
{
    [AvaloniaFact]
    public void HeadlessSession_BootsTheRealApplication()
    {
        Assert.IsType<App>(Application.Current);
    }

    [AvaloniaFact]
    public void DesignTokens_AreResolvableFromAppResources()
    {
        var current = Application.Current!;

        Assert.IsType<CornerRadius>(current.FindResource("DesignCornerRadiusComponent"));
        Assert.IsType<FontFamily>(current.FindResource("AppFontFamily"));
        Assert.IsAssignableFrom<IBrush>(current.FindResource("AdaptiveSurfaceRaisedBrush"));
    }
}
