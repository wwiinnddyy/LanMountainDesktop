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
/// 组件角色色的落点。已烧四族：状态文字（10 处）、正文（17 处）、次要文字与图标字形（17 处）、
/// 卡片与根面板底（9 处），
/// 原来各处都写着 <c>_isNightVisual ? 夜档 : 日档</c> 三元组——夜档每族内部一模一样
/// （<c>#8B95A5</c> / <c>#E8EAED</c> / <c>#A8B1C2</c>），日档却各自漂开（正文 6 个值、次要 11 个值），
/// 现在一律走 <c>ComponentRoleBrushes</c> 问主题层要对应角色那支 <c>Adaptive*Text*</c> 画笔。
/// 这里钉的不是"等于某个十六进制"，而是**换壁纸/换档之后仍然读得清**：主题层的这些值是按
/// surfaceRaised 混色后再 EnsureContrast 算的（ThemeColorSystemService.cs:117-124），
/// 所以断言落在"跟随注册值"＋"与面板底色对比度达到该角色的门槛"两件事上。
/// 写死的三元组恰恰两件事都不保证——它就是绕过后者才存在的。
/// </summary>
public sealed class ComponentRoleBrushesTests
{
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
    /// 三个角色 × 明暗两档 = 6 行：拿到的必须就是窗口上真正注册的那支画笔（键接错会红），
    /// 而且对面板底色的对比度不低于该角色自己的门槛（主题没算够也会红）。
    /// <c>textPrimary</c> 在生产里按 4.5:1 造，<c>textMuted</c> 按 3:1（大字号/次要文字档）。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, "muted", ThemeResourceKeys.TextMutedBrush, 3.0)]
    [InlineData(true, "muted", ThemeResourceKeys.TextMutedBrush, 3.0)]
    [InlineData(false, "primary", ThemeResourceKeys.TextPrimaryBrush, 4.5)]
    [InlineData(true, "primary", ThemeResourceKeys.TextPrimaryBrush, 4.5)]
    [InlineData(false, "secondary", ThemeResourceKeys.TextSecondaryBrush, 3.0)]
    [InlineData(true, "secondary", ThemeResourceKeys.TextSecondaryBrush, 3.0)]
    public void RoleBrush_FollowsTheThemeAndStaysReadableOnThePanel(
        bool night, string role, string key, double floor)
    {
        var context = night ? NightContext : DayContext;
        var window = new Window();
        ThemeColorSystemService.ApplyThemeResources(window.Resources, context);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var resolved = Assert.IsAssignableFrom<ISolidColorBrush>(Resolve(window, role));
            Assert.True(
                AdaptiveTokens.TryGet<IBrush>(window, key, out var registered),
                $"主题服务没注册 {key}——那 {role} 角色的所有调用点会一起落到中性灰，看着像\"颜色淡了\"");
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(registered).Color,
                resolved.Color);

            Assert.True(
                AdaptiveTokens.TryGet<IBrush>(window, ThemeResourceKeys.SurfaceRaisedBrush, out var surface));
            var contrast = ColorMath.ContrastRatio(
                resolved.Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(surface).Color);
            Assert.True(
                contrast >= floor,
                $"{(night ? "夜" : "昼")}档 {role} 文字对面板底色只有 {contrast:F2}:1，低于 {floor}:1"
                + "——这一族族烧成 token 的全部理由就是这条保证不再由组件自己绕过");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 表面阶梯（第四族映射到 raised、第五族打算映射到 overlay 的共同依据）。
    /// 组件今天写死了两层底：卡片『#1B2129』、控件芯片『#2D3440』——夜档芯片比卡片**亮**；
    /// 白档芯片『#EFF1F5』类又比卡片『#FCFCFD』**暗**。也就是说这两个值编码的是一条相对关系，
    /// 烧成 token 必须保住方向，否则"控件贴在卡片上"会糊成一片。
    /// 主题的三层里 <c>surfaceOverlay</c> 正是这个方向，所以这里钉方向、不钉具体像素（值本该随壁纸漂）。
    /// 组件侧只验 <c>RaisedSurface</c> 真的取自 registered 的 raised——overlay 那一半等第五族
    /// 真有调用点时再收成组件方法（提前加 API 会被 <c>ZeroUseStaticClassMembers</c> 判成零引用成员，
    /// 这次就被判过：加早了红的是自己）。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SurfaceLadder_KeepsTheControlReadableOnTheCard(bool night)
    {
        var window = new Window();
        ThemeColorSystemService.ApplyThemeResources(window.Resources, night ? NightContext : DayContext);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var card = LuminanceOf(ComponentRoleBrushes.RaisedSurface(window),
                window, ThemeResourceKeys.SurfaceRaisedBrush);
            var chip = LuminanceOf(AdaptiveTokens.Brush(window, ThemeResourceKeys.SurfaceOverlayBrush,
                    ComponentRoleBrushes.RaisedSurface(window)),
                window, ThemeResourceKeys.SurfaceOverlayBrush);

            var direction = night ? $"夜档芯片层应比卡片层亮（实际 {chip:F3} vs {card:F3}）"
                                  : $"白档芯片层应比卡片层暗（实际 {chip:F3} vs {card:F3}）";
            Assert.True(night ? chip > card : chip < card, direction);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>取到颜色之前先确认拿到的就是那个键注册的画笔（接错键要红，而不是"亮度碰巧对"）。</summary>
    private static double LuminanceOf(IBrush resolved, IResourceHost host, string key)
    {
        Assert.True(AdaptiveTokens.TryGet<IBrush>(host, key, out var registered));
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(resolved).Color;
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(registered).Color, actual);
        return ColorMath.RelativeLuminance(actual);
    }

    private static IBrush Resolve(IResourceHost host, string role) => role switch
    {
        "muted" => ComponentRoleBrushes.MutedText(host),
        "primary" => ComponentRoleBrushes.PrimaryText(host),
        "secondary" => ComponentRoleBrushes.SecondaryText(host),
        _ => throw new ArgumentException($"未知的角色名 {role}", nameof(role)),
    };

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
