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
/// 2026-09-21 更正：底座记录里写"样式应用不生效"是**判据写错了**，不是堵点。
/// 那次实验给 TextBlock 本地赋了 FontWeight=Bold，而本地值在 Avalonia 里永远压过 Style setter，
/// 于是读到 Bold 被当成"样式没落"。换成本地不可能有值的判据（全局样式要求 FontFeatures=tnum，
/// TextBlock 默认是 null）后，样式确实落到了控件上——下面的卡片背景断言就是据此写的真视觉回归。
///
/// 仍然实测确认的堵点只剩一个：取像素（RenderTargetBitmap.Save 写 0 字节、
/// Window.CaptureRenderedFrame() 返回 null、CopyPixels 取到整幅透明且逐次不一致）。
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

    /// <summary>
    /// 样式到底有没有落到控件上——这条是"取像素之外还能不能做视觉回归"的分水岭，
    /// 也是上面那次误判的直接纠正。
    /// </summary>
    [AvaloniaFact]
    public void ApplicationStyles_ApplyToPlainControlsInHeadlessSession()
    {
        // 判据要选本地一定没赋过值的属性：App.axaml 的全局 TextBlock 样式要求 FontFeatures=tnum，
        // 而 TextBlock 的默认值是 null。（FontWeight 不行——它的默认值和样式值都是 Normal。）
        var text = new TextBlock();
        ShowAndLayout(text);

        Assert.NotNull(text.FontFeatures);
        Assert.Equal("tnum", string.Join(",", text.FontFeatures!));
    }

    /// <summary>
    /// 设置页的三种卡片都靠样式拿背景 <c>{{DynamicResource AdaptiveSurfaceRaisedBrush}}</c>。
    /// 这条把"样式生效 + 资源注册得上"两件事一起钉住：任一环断了，卡片就变成一块没有底的透明区，
    /// 不报错、不抛异常，只是那块 UI 静默失色——2026-09-21 组件库预览卡就是这个症状（
    /// AdaptiveCardBackgroundBrush 没人注册，兜底成了 Brushes.Transparent）。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("settings-section-card")]
    [InlineData("settings-option-card")]
    [InlineData("settings-list-item")]
    [InlineData("settings-option-card-icon-host")]
    [InlineData("settings-section-card-icon-host")]
    public void SettingsCardClasses_GetAVisibleBackgroundFromTheStyle(string styleClass)
    {
        var card = new Border();
        card.Classes.Add(styleClass);
        ShowAndLayout(card);

        Assert.NotNull(card.Background);
        if (card.Background is ISolidColorBrush solid)
        {
            Assert.True(solid.Color != Colors.Transparent, $"{styleClass} 的背景是 Transparent，等于没有卡片底");
            Assert.True(solid.Opacity > 0, $"{styleClass} 的背景不透明度为 0");
        }
    }

    private static void ShowAndLayout(Control control)
    {
        var window = new Window
        {
            Content = new StackPanel { Children = { control } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        control.Measure(new Size(320, 80));
        control.Arrange(new Rect(0, 0, 320, 80));
        Dispatcher.UIThread.RunJobs();
    }
}
