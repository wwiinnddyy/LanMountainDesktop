using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 计时器那一列四个数字的字色钉（#G1-AF 第十五笔，2026-10-01）。
///
/// 烧之前它们两侧各写一遍：标记面 Top=<c>#AEB4C1</c>、Main=<c>#0F141C</c>、Next=<c>#B2B8C4</c>、
/// NextNext=<c>#C8CDD7</c>，代码面（<c>ApplyModeVisual</c>）再按 <c>isNightMode ? 夜档 : 昼档</c>
/// 各写一遍三元组——同一件事两个真源，改主题那一侧不动。
/// 映射按档数收：主数字→正文，紧邻两格→次要，最远那格→状态。日档里 Top 与 Next 本来就只差
/// 一档灰（<c>#AEB4C1</c> 与 <c>#B2B8C4</c>），烧完它们共用同一个角色键，这是把三档近灰并回主题层已有的两档。
///
/// **本笔真正要钉的是取色时机**：<c>TimerWidget</c> 的 ctor（:39）调 <c>UpdateVisual</c>（:110），
/// 而 <c>UpdateVisual</c> 第一件事就是 <c>ApplyModeVisualIfNeeded</c>——那一刻控件还不属于任何资源作用域，
/// 角色画笔当场解析只拿得到兜底灰；挂树后再跑 <c>ApplyCellSize</c> 时，
/// <c>ComponentThemeMode.RefreshNightVisualIfChanged</c>（:52-55）见黑夜档没变就早退，那层灰就一直留在屏上。
/// 夹具按真实顺序做：先构造、再挂树，<b>不</b>额外调用任何布局方法。
///
/// **这格能看见什么、看不见什么**（2026-10-01 变异实测）：接错角色键会红（断言比的是注册的那一支），
/// 改回字面量也会红（先比旧值）。但它看不见"构造期取色落灰"——把 attach 时的强制重画删掉两格照样绿，
/// 因为 #G1-AG 那笔让 headless 底座在<b>应用级</b>资源里注册了 Adaptive 画笔，控件没挂树也查得到键。
/// 生产作用域与此的差别留给 #43 下一笔查证（DailyNewsView 那层灰是实测到的，见 #G1-DA）。
///
/// 对比度按数字实际压着的那层底算：<c>TimerPanelBorder</c> 自家渐变的两个色标各比一次，取更严的结果
/// （主题层没有渐变档，所以这层底没并、留在原地，见 <c>DuplicatedPaintSourceRatchetTests</c> 的头注）。
/// </summary>
public sealed class TimerNumberColumnThemeTests
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

    /// <summary>(元素名, 角色键, 门槛, 烧掉的那个旧字面量)。</summary>
    private static readonly (string Name, string RoleKey, double Floor, string OldLiteral)[] NumberCells =
    [
        ("MainNumberTextBlock", ThemeResourceKeys.TextPrimaryBrush, 4.5, "#0F141C"),
        ("TopNumberTextBlock", ThemeResourceKeys.TextSecondaryBrush, 3.0, "#AEB4C1"),
        ("NextNumberTextBlock", ThemeResourceKeys.TextSecondaryBrush, 3.0, "#B2B8C4"),
        ("NextNextNumberTextBlock", ThemeResourceKeys.TextMutedBrush, 3.0, "#C8CDD7"),
    ];

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberInk_FollowsTheThemeOnTheRealAttachPass(bool isNightMode)
    {
        var mode = isNightMode ? "夜" : "昼";
        var context = isNightMode ? NightContext : DayContext;
        var failures = new List<string>();

        var widget = new TimerWidget();
        var host = new Border { Child = widget };
        ThemeColorSystemService.ApplyThemeResources(host.Resources, context);
        // 明暗判定先看 ActualThemeVariant（ComponentThemeMode.ResolveIsNight:65-73），
        // 生产里资源与档是同一处一起设的（App.axaml.cs:862），夹具也必须一起配。
        var window = new Window { Content = host };
        window.RequestedThemeVariant = isNightMode ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var backdrops = GradientStopsOf(widget, "TimerPanelBorder");
        if (backdrops.Count == 0)
        {
            failures.Add($"{mode}档：TimerPanelBorder 没读出渐变底（四个数字的对比度没法算）");
        }

        foreach (var (name, roleKey, floor, oldLiteral) in NumberCells)
        {
            var block = widget.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == name);
            var ink = (block?.Foreground as ISolidColorBrush)?.Color;
            var expected = Registered(host, roleKey);
            if (block is null || ink is null)
            {
                failures.Add($"{mode}档：{name} 没读到纯色前景（控件在树里吗？{block is not null}）");
                continue;
            }

            if (expected is null)
            {
                failures.Add($"{mode}档：资源表里缺 {roleKey}");
                continue;
            }

            // 大小写不敏感：Avalonia 的 Color.ToString() 出的是小写十六进制。
            if (string.Equals(ink.Value.ToString(), oldLiteral, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{mode}档：{name} 还是写死的 {oldLiteral}——这一行没跟着主题走");
                continue;
            }

            if (ink != expected)
            {
                failures.Add(
                    $"{mode}档：{name} 的字色是 {ink}，角色 {roleKey} 注册的是 {expected}" +
                    "（差一个色档＝接错角色；差得远＝构造期取色落到了兜底灰）");
                continue;
            }

            foreach (var backdrop in backdrops)
            {
                var ratio = ColorMath.ContrastRatio(
                    ColorMath.ToOpaqueAgainst(ink.Value, backdrop), backdrop);
                if (ratio < floor)
                {
                    failures.Add(
                        $"{mode}档：{name} 压在 {backdrop} 上只有 {ratio:F2}:1 < 门槛 {floor:0.0}:1");
                }
            }
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>数字实际压着的那层底：TimerPanelBorder 渐变的每个色标（不是渐变就回空表）。</summary>
    private static List<Color> GradientStopsOf(Avalonia.Visual root, string name)
    {
        var target = root.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Name == name);
        if (target is not Border border || border.Background is not IGradientBrush gradient)
        {
            return [];
        }

        return [.. gradient.GradientStops.Select(s => s.Color)];
    }

    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : null;
}
