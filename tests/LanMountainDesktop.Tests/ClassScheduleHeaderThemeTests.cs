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
/// 课程表表头那几个字"跟不跟主题走"的钉（#G1-AF 第十三笔，2026-10-01）。
///
/// 烧的是 <c>ApplyAdaptiveLayout</c> 里四处（月份/日期 → 正文、星期 → 次要、状态行 → 状态），
/// 改之前它们是 <c>_isNightVisual ? 夜档 : 昼档</c> 那种绕过主题层的三元组：
/// 月份与日期写 <c>#F8FAFF/#131722</c>、星期写 <c>#C6CBD5/#4B5463</c>、状态行写 <c>#9AA2B1/#4B5565</c>。
///
/// **为什么只烧这四处**：同一个文件里另有 12 个写字面量的地方在
/// <c>CreateTimelineItemControl</c>／<c>IncrementalUpdateItems</c>（条目里的时间、课名）与
/// 面板渐变、α 蒙层、品牌蓝/红上。条目那几处**不能**照抄本笔的接法——角色画笔当场解析，
/// 而条目控件是"造好之后才加进面板"的，构造那一刻取色只会拿到兜底灰，
/// 且 <c>ApplyAdaptiveLayout</c> 并不重画动态条目的字色（它只重画命名元素）。
/// 这一条与 <c>DailyNewsViewThemeTests</c> 的头注是同一个约束的两种后果。
///
/// 本笔挑的四处都在<b>挂树之后一定会再跑一遍</b>的路径上（<c>ApplyCellSize</c>／<c>SizeChanged</c>／
/// <c>ActualThemeVariantChanged</c>），夹具因此按真实顺序做：先挂树、再调 <c>ApplyCellSize</c>，
/// 读到的必须是注册的那一支而不是灰。
/// 对比度一律按<b>这块面板实际的那层底</b>算：表头压在 <c>RootBorder</c> 自家渐变上（主题没有渐变档，
/// 所以这一层没并、留在原地），取两个色标里**更严的那一个**来比。
/// </summary>
public sealed class ClassScheduleHeaderThemeTests
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

    /// <summary>(元素名, 角色键, 门槛)。</summary>
    private static readonly (string Name, string RoleKey, double Floor)[] HeaderCells =
    [
        ("MonthTextBlock", ThemeResourceKeys.TextPrimaryBrush, 4.5),
        ("DayTextBlock", ThemeResourceKeys.TextPrimaryBrush, 4.5),
        ("WeekdayTextBlock", ThemeResourceKeys.TextSecondaryBrush, 3.0),
        ("StatusTextBlock", ThemeResourceKeys.TextMutedBrush, 3.0),
    ];

    [AvaloniaFact]
    public void HeaderInk_FollowsTheThemeOnTheRealLayoutPass()
    {
        var failures = new List<string>();

        foreach (var (mode, context) in new (string, ThemeColorContext)[] { ("昼", DayContext), ("夜", NightContext) })
        {
            var widget = new ClassScheduleWidget();
            var host = new Border { Child = widget };
            ThemeColorSystemService.ApplyThemeResources(host.Resources, context);
            // 必须连 ThemeVariant 一起配：组件自己的明暗判定先看 ActualThemeVariant
            // （ComponentThemeMode.ResolveIsNight:65-73），生产里这两个信号是同一处一起设的
            // （App.axaml.cs:862 RequestedThemeVariant = snapshot.IsNightMode ? Dark : Light）。
            // 只注册资源不改档，夹具就会造出一个"注册的是夜档、面板画的是昼档"的假状态——
            // 第一版就是这么量出 1.01:1 的，那是夹具错，不是产品状态。

            var window = new Window { Content = host };
            window.RequestedThemeVariant = context.IsNightMode ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 挂树之后再跑一次真实布局路径——这是本笔四处唯一被重复执行的机会，
            // 也是"构造期取色会落灰"这类错误在这里现形的地方。
            widget.ApplyCellSize(96);
            Dispatcher.UIThread.RunJobs();

            var backdrops = GradientStopsOf(widget, "RootBorder");
            if (backdrops.Count == 0)
            {
                failures.Add($"{mode}档：RootBorder 没读出渐变底（表头的对比度没法算）——底被改成了别的形状？");
            }

            foreach (var (name, roleKey, floor) in HeaderCells)
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

                if (ink != expected)
                {
                    failures.Add(
                        $"{mode}档：{name} 的字色是 {ink}，角色 {roleKey} 注册的是 {expected}" +
                        "（差一个色档＝接错角色；差得远＝落到了兜底灰）");
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
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>表头实际压着的那层底：RootBorder 渐变里的每个色标（不是纯色就回空表）。</summary>
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
