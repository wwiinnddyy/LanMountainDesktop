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
/// 组件"文字角色色"的落点。已烧两族：状态文字（10 处）与正文（17 处），原来各处都写着
/// <c>_isNightVisual ? 夜档 : 日档</c> 三元组（正文那族夜档全是同一支 <c>#E8EAED</c>，日档却漂成
/// 6 个不同的近黑值），现在一律问主题层要 <c>AdaptiveTextMutedBrush</c> / <c>AdaptiveTextPrimaryBrush</c>
/// （见 <c>ComponentRoleBrushes</c>）。
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
    /// 每个角色 × 明暗两档各一行：拿到的必须就是窗口上真正注册的那支画笔（键接错会红），
    /// 而且对面板底色的对比度不低于该角色自己的门槛（主题没算够也会红）。
    /// <c>textPrimary</c> 在生产里按 4.5:1 造，<c>textMuted</c> 按 3:1（大字号/次要文字档）。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, "muted", ThemeResourceKeys.TextMutedBrush, 3.0)]
    [InlineData(true, "muted", ThemeResourceKeys.TextMutedBrush, 3.0)]
    [InlineData(false, "primary", ThemeResourceKeys.TextPrimaryBrush, 4.5)]
    [InlineData(true, "primary", ThemeResourceKeys.TextPrimaryBrush, 4.5)]
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

    private static IBrush Resolve(IResourceHost host, string role) => role switch
    {
        "muted" => ComponentRoleBrushes.MutedText(host),
        "primary" => ComponentRoleBrushes.PrimaryText(host),
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
