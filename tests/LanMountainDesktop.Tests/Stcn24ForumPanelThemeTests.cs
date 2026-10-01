using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 社区 24 论坛这块面板"三层底"的钉（#G1-AF 标记面第六笔，2026-10-01）。
///
/// 这一族以前**不在任何判据眼里**：行板底在标记里写着 <c>#F7F8FA</c>（8 个 <c>PostItemNHost</c>），
/// 代码那边（<c>Stcn24ForumWidget.axaml.cs:202</c>）早就把它们改走 <c>OverlaySurface</c>——
/// 同一个元素同一个属性两个真源，但因为重画走的是记录字段（<c>visual.Host.Background</c>）而不是
/// x:Name，按名字配的那条判据看不见（#G1-CN 量出这一族共 24 处）。本笔把标记那 8 处接回同一个键，
/// **一个像素都不改**：两侧本来就画同一档。
///
/// 第二组格钉的是**故意没并的那一层**：头像格（<c>PostItemNAvatarHost</c>）今天还是
/// <c>#3D4451 / #E7EBF4</c> 这对写死值，而且它**比行板更亮一档**。主题的表面只有 raised 与 overlay 两层，
/// 这块面板却有"卡片底／行板／头像格"三层——把头像格也并到 overlay 会让它和行板同色、圆片直接消失。
/// 所以这里钉的是"头像格 ≠ 行板档"这一条现状；要动它得先有第三层表面（挂在 #G1-CY / #112 上）。
///
/// 夹具按宿主的真实顺序做：先 <c>new</c>、再挂树，并且**连 ThemeVariant 一起配**
/// （组件自己的明暗判定第一眼看 <c>ActualThemeVariant</c>，生产里两个信号同一处一起设——
/// 细节写在 <c>ClassScheduleHeaderThemeTests</c> 头注）。
/// </summary>
public sealed class Stcn24ForumPanelThemeTests
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

    [AvaloniaFact]
    public void RowPlatesFollowTheOverlayTier_AvatarsStayAboveThem()
    {
        var failures = new List<string>();

        foreach (var (mode, context) in new (string, ThemeColorContext)[] { ("昼", DayContext), ("夜", NightContext) })
        {
            var widget = new Stcn24ForumWidget();
            var host = new Border { Child = widget };
            ThemeColorSystemService.ApplyThemeResources(host.Resources, context);

            var window = new Window { Content = host };
            window.RequestedThemeVariant = context.IsNightMode ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var overlay = Registered(host, ThemeResourceKeys.SurfaceOverlayBrush);
            var raised = Registered(host, ThemeResourceKeys.SurfaceRaisedBrush);
            if (overlay is null || raised is null)
            {
                failures.Add($"{mode}档：资源表里缺 overlay 或 raised，后面的比较没有基准");
                window.Close();
                Dispatcher.UIThread.RunJobs();
                continue;
            }

            // ① 八个行板底：标记与代码现在必须说同一个键（改错档、或哪一侧退回字面量都会红）。
            for (var row = 1; row <= 8; row++)
            {
                var plate = SolidOf(widget, $"PostItem{row}Host");
                if (plate != overlay)
                {
                    failures.Add(
                        $"{mode}档：PostItem{row}Host 的底是 {plate}，主题的 overlay 档是 {overlay}" +
                        "（标记那份 #F7F8FA 回来了，或接错了档）");
                }
            }

            // ② 头像格是第三层：既不能等于行板档，也不能等于卡片档——这一格钉的就是"没被顺手并掉"。
            var avatar = SolidOf(widget, "PostItem1AvatarHost");
            var expectedAvatar = mode == "夜" ? Color.Parse("#3D4451") : Color.Parse("#E7EBF4");
            if (avatar != expectedAvatar)
            {
                failures.Add(
                    $"{mode}档：PostItem1AvatarHost 是 {avatar}，现状钉的是 {expectedAvatar}" +
                    "（头像格比行板亮一档；主题只有两层表面，并过去会让圆片和行板同色）");
            }

            if (avatar == overlay || avatar == raised)
            {
                failures.Add($"{mode}档：头像格被并进了某一层主题表面（{avatar}）——三层压成两层了");
            }

            // ③ 卡片底仍走 raised：三层里最暗（夜）/最亮（昼）的那一层要没错位，不然上面两条都失去意义。
            if (SolidOf(widget, "CardBorder") != raised)
            {
                failures.Add($"{mode}档：CardBorder 不是 raised 档（实测 {SolidOf(widget, "CardBorder")}，应为 {raised}）");
            }

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Color? SolidOf(Avalonia.Visual root, string name)
    {
        var target = root.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Name == name);
        return target is Border border ? (border.Background as ISolidColorBrush)?.Color : null;
    }

    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : null;
}
