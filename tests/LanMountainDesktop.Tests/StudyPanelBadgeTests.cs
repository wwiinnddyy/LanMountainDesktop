using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 学习面板"模式角标"与图表"两点一段线"两个家的证据。
/// 两者都是一次性涂/画三到四步的短序列：抄在 4 个组件与 2 个图表控件里时，
/// 漏一步或顺序反了都不报错，只会让角标在浅面板上糊成一片、或折线少一段。
/// </summary>
public sealed class StudyPanelBadgeTests
{
    private static readonly Color[] Candidates =
    [
        Color.Parse("#FF1E293B"),
        Color.Parse("#FFFFFFFF"),
    ];

    [AvaloniaFact]
    public void ApplyModeBadge_FillsBackgroundBorderAndForeground()
    {
        var badge = new Border();
        var text = new TextBlock();
        var panel = Color.Parse("#FFF1F5FA");
        var basis = StudyPanelPalette.RealtimeBadge;

        StudyPanelPalette.ApplyModeBadge(badge, text, panel, basis, Candidates);

        Assert.Equal(
            StudyPanelPalette.ResolveBadgeColor(panel, basis),
            Assert.IsType<SolidColorBrush>(badge.Background).Color);
        Assert.Same(StudyPanelPalette.BadgeBorderBrush, badge.BorderBrush);
        Assert.NotNull(Assert.IsType<SolidColorBrush>(text.Foreground));
    }

    [AvaloniaFact]
    public void ApplyModeBadge_PicksAPredefinedCandidate_ForTheSamePanel()
    {
        // 前景必须落在候选表里：候选表就是"角标字色只有这几种"的口径，跑出去就说明配对写歪了。
        var badge = new Border();
        var text = new TextBlock();

        StudyPanelPalette.ApplyModeBadge(
            badge,
            text,
            Color.Parse("#FF0B1220"),
            StudyPanelPalette.SuccessBadge,
            Candidates);

        var chosen = Assert.IsType<SolidColorBrush>(text.Foreground).Color;
        Assert.Contains(Candidates, c => c == chosen);
    }

    [AvaloniaFact]
    public void AddLine_DrawsOneUnfilledSegment()
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            StudyChartGeometry.AddLine(context, new Point(0, 0), new Point(10, 5));
        }

        Assert.Equal(new Rect(0, 0, 10, 5), geometry.Bounds);
    }

    [AvaloniaFact]
    public void AddLine_DrawsNothing_WhenTheTwoPointsCoincide()
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            StudyChartGeometry.AddLine(context, new Point(3, 4), new Point(3, 4));
        }

        Assert.Equal(0d, geometry.Bounds.Width, 3);
        Assert.Equal(0d, geometry.Bounds.Height, 3);
    }
}
