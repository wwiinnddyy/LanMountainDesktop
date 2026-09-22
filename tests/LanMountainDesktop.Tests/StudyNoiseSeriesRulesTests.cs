using System;
using System.Collections.Generic;

using LanMountainDesktop.Models;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 噪声折线这两条判据的家的行为钉。
///
/// 收口前它们是 4 份抄本（两个自绘图表控件 + 一个学习组件），错法都不报错：
/// 尾窗起点取错只是动态段多一格或少一格，档位边界取错只是同一个 dB 值涂成两种颜色。
/// </summary>
public sealed class StudyNoiseSeriesRulesTests
{
    private static DateTimeOffset Origin { get; } = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    private static List<NoiseRealtimePoint> SecondSpaced(int count)
    {
        var points = new List<NoiseRealtimePoint>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(new NoiseRealtimePoint(Origin.AddSeconds(i), 0, 0, 0, 0, false));
        }

        return points;
    }

    [Fact]
    public void FirstTailIndex_IncludesThePointLandingExactlyOnTheCutoff()
    {
        // 10 个点（0~9 秒），尾窗 4 秒 → 截止线正好落在第 5 秒那个点上，它必须算进尾窗。
        var points = SecondSpaced(10);

        Assert.Equal(5, StudyNoiseSeriesRules.FirstTailIndex(points, TimeSpan.FromSeconds(4)));
        // 差一格的两侧：窗口再短 1 秒就往右挪一格，再长 1 秒就往左挪一格。
        Assert.Equal(6, StudyNoiseSeriesRules.FirstTailIndex(points, TimeSpan.FromSeconds(3)));
        Assert.Equal(4, StudyNoiseSeriesRules.FirstTailIndex(points, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void FirstTailIndex_OnADegenerateSeries_HasNoTailToCut()
    {
        Assert.Equal(0, StudyNoiseSeriesRules.FirstTailIndex([], TimeSpan.FromSeconds(4)));
        Assert.Equal(0, StudyNoiseSeriesRules.FirstTailIndex(SecondSpaced(1), TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void LevelOf_BandsAreMeasuredFromTheBaseline_NotFromZero()
    {
        // 每 10 dB 一档，且"正好等于档线"归上一档（左闭右开）。
        Assert.Equal(NoiseDistributionLevel.Quiet, StudyNoiseSeriesRules.LevelOf(44.9, baselineDb: 45));
        Assert.Equal(NoiseDistributionLevel.Normal, StudyNoiseSeriesRules.LevelOf(45, baselineDb: 45));
        Assert.Equal(NoiseDistributionLevel.Noisy, StudyNoiseSeriesRules.LevelOf(55, baselineDb: 45));
        Assert.Equal(NoiseDistributionLevel.Extreme, StudyNoiseSeriesRules.LevelOf(65, baselineDb: 45));

        // 基准为负（相对 dB 显示）时档线跟着走，不是写死在 45/55/65。
        Assert.Equal(NoiseDistributionLevel.Quiet, StudyNoiseSeriesRules.LevelOf(-31, baselineDb: -30));
        Assert.Equal(NoiseDistributionLevel.Normal, StudyNoiseSeriesRules.LevelOf(-30, baselineDb: -30));
        Assert.Equal(NoiseDistributionLevel.Noisy, StudyNoiseSeriesRules.LevelOf(-20, baselineDb: -30));
        Assert.Equal(NoiseDistributionLevel.Extreme, StudyNoiseSeriesRules.LevelOf(-10, baselineDb: -30));
    }
}
