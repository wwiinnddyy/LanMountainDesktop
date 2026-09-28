using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

using Avalonia.Headless.XUnit;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件"状态文字"颜色的落点。这一族原来是 10 处各写一遍的
/// <c>_isNightVisual ? 夜档 : 日档</c> 三元组（值还各自漂开），2026-09-29 烧成问主题层要
/// <c>AdaptiveTextMutedBrush</c>（见 <c>ComponentRoleBrushes</c>）。
///
/// 这里钉的不是"等于某个十六进制"，而是**换壁纸/换档之后仍然读得清**：主题层的 muted 是按
/// surfaceRaised 混色后再 EnsureContrast 算的（ThemeColorSystemService.cs:124），
/// 所以断言落在"跟随注册值"＋"与面板底色对比度达标"两件事上。
/// 写死的三元组恰恰两件事都不保证——它就是绕过后者才存在的。
/// </summary>
public sealed class ComponentRoleBrushesTests
{
    private const double LargeTextContrastFloor = 3.0;

    private static readonly ThemeColorContext DayContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: true,
        IsLightNavBackground: true,
        IsNightMode: false);

    private static readonly ThemeColorContext NightContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: false,
        IsLightNavBackground: false,
        IsNightMode: true);

    /// <summary>
    /// 明暗两档各一行。把 <c>ComponentRoleBrushes.MutedText</c> 里那个键换成别的键，
    /// 第一行就会因为"颜色不等于窗口上真正注册的那支 muted 画笔"而红。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void MutedText_FollowsTheThemeAndStaysReadableOnThePanel(bool night)
    {
        var context = night ? NightContext : DayContext;
        var window = new Window();
        ThemeColorSystemService.ApplyThemeResources(window.Resources, context);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var muted = Assert.IsAssignableFrom<ISolidColorBrush>(ComponentRoleBrushes.MutedText(window));
            Assert.True(
                AdaptiveTokens.TryGet<IBrush>(window, ThemeResourceKeys.TextMutedBrush, out var registered),
                "主题服务没注册 AdaptiveTextMutedBrush——那 10 个调用点会一起落到中性灰，看着像\"颜色淡了\"");
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(registered).Color,
                muted.Color);

            Assert.True(
                AdaptiveTokens.TryGet<IBrush>(window, ThemeResourceKeys.SurfaceRaisedBrush, out var surface));
            var contrast = ColorMath.ContrastRatio(
                muted.Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(surface).Color);
            Assert.True(
                contrast >= LargeTextContrastFloor,
                $"{(night ? "夜" : "昼")}档状态文字对面板底色只有 {contrast:F2}:1，低于 {LargeTextContrastFloor}:1"
                + "——这一族烧成 token 的全部理由就是这条保证不再由组件自己绕过");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 兜底那一支（拿不到键给中性灰）**没有单独一格测试**，理由是实测的而不是偷懒：
    /// 视觉底座已把 Adaptive* 注册在应用级资源表上（#44 那条），任何控件沿作用域往上找都拿得到，
    /// 因此在测试里造不出"取不到键"的场景。这条兜底只在生产里主题服务没起来时才可能被读到。
    /// </summary>
    [AvaloniaFact]
    public void MutedText_IsTheSameBrushForEveryCallerInOneWindow()
    {
        var first = new TextBlock();
        var second = new TextBlock();
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
        ThemeColorSystemService.ApplyThemeResources(window.Resources, DayContext);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var left = Assert.IsAssignableFrom<ISolidColorBrush>(ComponentRoleBrushes.MutedText(first));
            var right = Assert.IsAssignableFrom<ISolidColorBrush>(ComponentRoleBrushes.MutedText(second));
            Assert.Equal(left.Color, right.Color);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
