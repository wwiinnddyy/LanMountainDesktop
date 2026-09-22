using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// 像素判据的覆盖边界（实测出来的）：能做"贴类 vs 不贴类"比对的是画刷在 App 级字典里静态定义的那几个类，
/// 目前是 <c>settings-section-card</c> / <c>settings-option-card</c> / <c>settings-list-item</c>。
/// 另外 14 个 Border 类（<c>glass-panel</c>、<c>surface-*</c>、<c>component-editor-*</c>、
/// <c>notification-card</c>、<c>about-hero-card</c>、<c>taskbar-profile-popup-*</c>、<c>music-progress-*</c>）
/// 引用的是 <c>{{DynamicResource Adaptive*}}</c>，而那些画刷过去是运行期资源、底座不跑那段启动流程，
/// 于是贴类与不贴类同为空色。
/// 现状（2026-09-22 更新）：画刷注册已由 VisualTestApp 补上（G1-AG），
/// App 级字典那 5 个类由 <c>Visual/BorderStyleClassPixelTests</c> 逐帧比对覆盖；
/// 文件作用域（窗口/控件自己的 <c>&lt;*.Styles&gt;</c>）那 9 个里，
/// <c>music-progress-*</c> / <c>notification-card</c> / <c>about-hero-card</c> 已由
/// <c>Visual/FileScopedBorderClassPixelTests</c> 按真实例覆盖，剩下 5 个（编辑器 3 个 + 主窗体弹层 2 个）
/// 挂在同一文件的 NotCoveredYet 里，每条写着实测到的拦路原因（样式字典依赖 Material 主题 / 需要整窗服务图）。
/// "有人要、没人注册"这个症状仍由 <c>CapabilityEntryPointTests</c> 的键覆盖探针守着。
///
/// 曾经的第二个堵点"取像素"已经打通，条件是两件事同时做到（缺一件就退回原症状）：
/// <c>UseHeadlessDrawing = false</c>（默认 true 时 Avalonia 只挂桩绘制，Save 写 0 字节、
/// CaptureRenderedFrame 返回 null）+ <c>UseSkia()</c>（关掉桩绘制后 Skia 后端不会自动注册，
/// 实测表现为启动就抛 Unable to locate 'Avalonia.Platform.IFontManagerImpl'）+ 采集前
/// 泵一次 <c>AvaloniaHeadlessPlatform.ForceRenderTimerTick()</c>（不泵就没有渲染帧，仍是 null）。
/// 下面两条像素断言就是这个条件的守卫：任何一件被改回去，它们会先红。
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

    /// <summary>
    /// 像素管线自己得先可信：红方块压在蓝底上，取到的帧必须两个区域各是各的颜色。
    /// 这条不测产品，测的是"取像素"这条路还通不通——桩绘制一开、Skia 一掉、或忘了泵渲染帧，
    /// 帧要么 null 要么整幅透明，这条立刻红，下面那条真视觉回归才有意义。
    /// </summary>
    [AvaloniaFact]
    public void CapturedFrame_DistinguishesPaintedRegionsFromTheBackdrop()
    {
        var card = new Border
        {
            Width = 60,
            Height = 60,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Background = Brushes.Red,
        };
        var backdrop = new Grid { Background = Brushes.Blue, Children = { card } };

        var frame = Capture(backdrop, 100, 100);

        var onCard = frame.At(frame.Width / 6, frame.Height / 6);
        var onBackdrop = frame.At(frame.Width * 5 / 6, frame.Height * 5 / 6);

        Assert.Equal(255, onCard.Alpha);
        Assert.True(onCard.Red > 128 && onCard.Green < 90 && onCard.Blue < 90, $"卡片位置取到的不是红：{onCard}");
        Assert.Equal(255, onBackdrop.Alpha);
        Assert.True(onBackdrop.Blue > 128 && onBackdrop.Red < 90, $"背景位置取到的不是蓝：{onBackdrop}");
    }

    /// <summary>
    /// 上面那批"样式解析得出可见画刷"的断言到此为止都只是逻辑；这条把它推到 framebuffer：
    /// 同一个位置，贴类的与不贴类各采一帧，两帧必须不一样。
    /// 为什么不是"和背景比"：实测发现不贴类的 Border 画出来也已经不是底板颜色（主题给 Border 有默认外观），
    /// 所以"和底板不同"这种判据在类名拼错时照样绿——变异验证就是这么抓到它的。
    /// 现在比的是"这个类到底改没改画面"，类名拼错、样式被删、资源没注册（兜底 Transparent）都会两帧相同。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("settings-section-card")]
    [InlineData("settings-option-card")]
    [InlineData("settings-list-item")]
    public void CapturedFrame_PaintsStyledCardsDistinctlyFromUnstyled(string styleClass)
    {
        var styled = Capture(CardsAt(styleClass), 100, 100);
        var plain = Capture(CardsAt(null), 100, 100);
        var styledPixel = styled.At(styled.Width / 6, styled.Height / 6);
        var plainPixel = plain.At(plain.Width / 6, plain.Height / 6);

        var distance = Math.Abs(styledPixel.Red - plainPixel.Red)
            + Math.Abs(styledPixel.Green - plainPixel.Green)
            + Math.Abs(styledPixel.Blue - plainPixel.Blue);
        Assert.True(
            distance > 2,
            $"{styleClass} 贴类与不贴类画出来一样（通道总差 {distance}）：styled={styledPixel} plain={plainPixel}");
    }

    private static Grid CardsAt(string? styleClass)
    {
        var card = new Border
        {
            Width = 60,
            Height = 60,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        if (styleClass is not null)
        {
            card.Classes.Add(styleClass);
        }

        return new Grid { Background = Brushes.Magenta, Children = { card } };
    }

    private sealed record CapturedFrame(int Width, int Height, int Stride, byte[] Pixels)
    {
        public Pixel At(int x, int y)
        {
            var i = (y * Stride) + (x * 4);
            return new Pixel(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }
    }

    private readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha)
    {
        public override string ToString() => $"rgba({Red},{Green},{Blue},{Alpha})";
    }

    private static CapturedFrame Capture(Control content, int width, int height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null：检查 UseHeadlessDrawing / UseSkia / 有没有泵渲染帧");

        var pixelWidth = frame.PixelSize.Width;
        var pixelHeight = frame.PixelSize.Height;
        using var locked = frame.Lock();
        var rowBytes = locked.RowBytes;
        var buffer = new byte[rowBytes * pixelHeight];
        for (var y = 0; y < pixelHeight; y++)
        {
            Marshal.Copy(locked.Address + (y * rowBytes), buffer, y * rowBytes, rowBytes);
        }

        return new CapturedFrame(pixelWidth, pixelHeight, rowBytes, buffer);
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
