using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using LanMountainDesktop.Models;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 降采样与取点映射的行为钉（家在 <see cref="StudyChartGeometry"/>）。
///
/// 这段算法以前在两个自绘图表控件里各抄一份逐字相同的 86 行，收口前只有控件级的
/// "缓存里有几条路径"这种间接覆盖——桶边界取错、峰谷丢一个、缓冲每帧换一次，
/// 那些都只会画出来不一样或不掉帧数变差，不会有人报错。所以这里按"错了会怎样"逐条钉。
/// </summary>
public sealed class StudyChartGeometryTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    private const double MinDb = 20d;
    private const double MaxDb = 100d;
    private static readonly Rect Plot = new(0, 0, 400, 180);
    private const double PixelsPerSecond = 2d;

    [Fact]
    public void BuildPlotPoints_YieldsNothing_ForEmptyOrSinglePointWindow()
    {
        Point[]? buffer = null;

        Assert.Equal(0, Run([], 0, 0, ref buffer));
        Assert.Equal(0, Run(CreateSeries(1), 0, 1, ref buffer));
    }

    [Fact]
    public void BuildPlotPoints_MapsEveryPoint_WhenTheyFit()
    {
        var series = CreateSeries(50);
        Point[]? buffer = null;

        var count = Run(series, 0, series.Count, ref buffer);

        Assert.Equal(series.Count, count);
        Assert.NotNull(buffer);
        Assert.Equal(StudyChartGeometry.MapToPlot(Plot, series[7], Origin, PixelsPerSecond, MinDb, MaxDb), buffer![7]);
    }

    [Fact]
    public void BuildPlotPoints_UsesOnlyTheRequestedWindow()
    {
        var series = CreateSeries(60);
        Point[]? buffer = null;

        var count = Run(series, 10, 40, ref buffer);

        Assert.NotNull(buffer);
        Assert.Equal(30, count);
        Assert.Equal(StudyChartGeometry.MapToPlot(Plot, series[10], Origin, PixelsPerSecond, MinDb, MaxDb), buffer![0]);
        Assert.Equal(StudyChartGeometry.MapToPlot(Plot, series[39], Origin, PixelsPerSecond, MinDb, MaxDb), buffer![count - 1]);
    }

    [Fact]
    public void BuildPlotPoints_StaysWithinTheSampleBudget_AtTheWidestRealInput()
    {
        // 源侧上限来自 StudyAnalyticsConfig.RealtimeBufferCapacity（夹在 60..1200），
        // 目标侧上限来自画布宽度（曲线 56..360、面积图 64..420），这里取两头最大值。
        var series = CreateSeries(1200);
        Point[]? buffer = null;

        var count = Run(series, 0, series.Count, ref buffer, maxSamples: 420);

        Assert.InRange(count, 2, 420);
        Assert.NotNull(buffer);
        Assert.True(buffer!.Length >= count);
    }

    [Fact]
    public void BuildPlotPoints_KeepsBothEndsOfTheWindow()
    {
        var series = CreateSeries(1200);
        Point[]? buffer = null;

        var count = Run(series, 0, series.Count, ref buffer, maxSamples: 420);
        Assert.NotNull(buffer);

        Assert.Equal(StudyChartGeometry.MapToPlot(Plot, series[0], Origin, PixelsPerSecond, MinDb, MaxDb), buffer![0]);
        Assert.Equal(
            StudyChartGeometry.MapToPlot(Plot, series[^1], Origin, PixelsPerSecond, MinDb, MaxDb),
            buffer[count - 1]);
    }

    [Fact]
    public void BuildPlotPoints_KeepsPeakAndValley_EvenWhenOnlyTwoPointsFitPerBucket()
    {
        var series = new List<NoiseRealtimePoint>();
        for (var i = 0; i < 600; i++)
        {
            series.Add(CreatePoint(i, 60d));
        }

        // 一个尖峰、一个凹坑，各自独占一个采样间隔——它们一旦在分桶时被抹平，图上就看不见了。
        series.Insert(300, CreatePoint(600, 99d));
        series.Insert(301, CreatePoint(601, 21d));

        Point[]? buffer = null;
        var count = Run(series, 0, series.Count, ref buffer, maxSamples: 80);
        Assert.NotNull(buffer);

        var ys = new double[count];
        for (var i = 0; i < count; i++)
        {
            ys[i] = buffer![i].Y;
        }

        Assert.Contains(StudyChartGeometry.MapToPlot(Plot, series[300], Origin, PixelsPerSecond, MinDb, MaxDb).Y, ys);
        Assert.Contains(StudyChartGeometry.MapToPlot(Plot, series[301], Origin, PixelsPerSecond, MinDb, MaxDb).Y, ys);
    }

    /// <summary>
    /// 时间戳严格递增时，输出里不许出现"同一个点画两遍"（横坐标必须一路往前走）。
    /// <b>如实记录一条覆盖边界</b>：源里那三处 <c>lastSourceIndex</c> 去重守卫，用这格测不到——
    /// 把 <c>if (second != lastSourceIndex)</c> 整个删掉做变异，10 条全绿。原因是它在
    /// "时间戳严格递增 + 桶不重叠"的输入下恒为真（<c>first &lt; second</c> 且 <c>lastSourceIndex ≤ first</c>），
    /// 守的是同一时刻重复采样那种源。也就是说：这三处今天没有行为证据支持，删不删都不改变输出，
    /// 留着的理由是"源不保证严格递增"这个前提，而不是"测到它会红"。
    /// </summary>
    [Fact]
    public void BuildPlotPoints_EmitsStrictlyAdvancingTime_NeverTheSamePointTwice()
    {
        var series = CreateSeries(1200, db: _ => 50d + Math.Sin(_ / 7d) * 20d);
        Point[]? buffer = null;

        var count = Run(series, 0, series.Count, ref buffer, maxSamples: 420);
        Assert.NotNull(buffer);

        for (var i = 1; i < count; i++)
        {
            Assert.True(buffer![i].X > buffer[i - 1].X, $"第 {i} 个点的横坐标没有往前走：{buffer[i].X} vs {buffer[i - 1].X}");
        }
    }

    [Fact]
    public void BuildPlotPoints_KeepsTheSameArrayAcrossFrames_NoAllocationPerRepaint()
    {
        var series = CreateSeries(1200);
        Point[]? buffer = null;
        var settledCount = Run(series, 0, series.Count, ref buffer, maxSamples: 420);
        Assert.NotNull(buffer);
        var first = buffer;
        var capacity = buffer!.Length;

        for (var frame = 0; frame < 30; frame++)
        {
            var count = Run(series, 0, series.Count, ref buffer, maxSamples: 420);
            Assert.Same(first, buffer);
            Assert.Equal(capacity, buffer!.Length);
            Assert.Equal(settledCount, count);
        }

        PointBufferPool.ReturnPoints(ref buffer);
        Assert.Null(buffer);
    }

    [Fact]
    public void MapToPlot_ClampsVerticallyAndNeverGoesLeftOfTheOrigin()
    {
        var above = StudyChartGeometry.MapToPlot(
            Plot, CreatePoint(0, MaxDb + 40d), Origin, PixelsPerSecond, MinDb, MaxDb);
        var below = StudyChartGeometry.MapToPlot(
            Plot, CreatePoint(0, MinDb - 40d), Origin, PixelsPerSecond, MinDb, MaxDb);
        // 点本身落在逻辑原点之前（原点被推到它后面 100 秒），横坐标不许是负的。
        var beforeOrigin = StudyChartGeometry.MapToPlot(
            Plot, CreatePoint(0, 50d), Origin.AddSeconds(100), PixelsPerSecond, MinDb, MaxDb);

        Assert.Equal(Plot.Top, above.Y);
        Assert.Equal(Plot.Bottom, below.Y);
        Assert.Equal(0, beforeOrigin.X);
    }

    /// <summary>
    /// 分桶那条路也要认窗口：<c>BuildPlotPoints_UsesOnlyTheRequestedWindow</c> 那格只有 30 个点，
    /// 走的是"装得下就原样映射"的分支，桶循环里的 <c>startIndex</c> 偏移一行都没被执行到
    /// （把 clamp 下界改成 1 做变异，那格照样绿）。面积图正是这么用的——静段与动态段各拿一段窗口，
    /// 偏移取错会把已经画过的点再画一遍。
    /// </summary>
    [Fact]
    public void BuildPlotPoints_HonoursTheWindowStart_WhenItDownsamples()
    {
        var series = CreateSeries(1200);
        Point[]? buffer = null;

        var count = Run(series, 300, 1200, ref buffer, maxSamples: 420);
        Assert.NotNull(buffer);

        Assert.True(count > 2, "这条夹具本来就该走分桶分支");
        Assert.Equal(
            StudyChartGeometry.MapToPlot(Plot, series[300], Origin, PixelsPerSecond, MinDb, MaxDb),
            buffer![0]);
        Assert.Equal(
            StudyChartGeometry.MapToPlot(Plot, series[^1], Origin, PixelsPerSecond, MinDb, MaxDb),
            buffer[count - 1]);

        var windowStartX = StudyChartGeometry.MapToPlot(
            Plot, series[300], Origin, PixelsPerSecond, MinDb, MaxDb).X;
        for (var i = 1; i < count; i++)
        {
            Assert.True(buffer[i].X >= windowStartX, $"第 {i} 个点跑到了窗口左边界之外：{buffer[i].X} < {windowStartX}");
        }
    }

    /// <summary>
    /// 逐帧代价的量测：满输入（1200 点 → 420 点）跑一次要多久，以及"每 16.7ms 一帧、
    /// 每帧两块图表各跑一次"占掉多少预算。断言只钉一个十倍余量的粗上限
    /// （防的是把这段写成每帧 O(n²) 或每帧新配数组），毫秒级抖动不参与判定。
    /// </summary>
    [Fact]
    public void BuildPlotPoints_CostIsAFractionOfOneFrame_AtTheWidestRealInput()
    {
        var series = CreateSeries(1200);
        Point[]? buffer = null;
        for (var warmup = 0; warmup < 200; warmup++)
        {
            Run(series, 0, series.Count, ref buffer, maxSamples: 420);
        }

        const int iterations = 2000;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            Run(series, 0, series.Count, ref buffer, maxSamples: 420);
        }

        stopwatch.Stop();
        var microsecondsPerCall = stopwatch.Elapsed.TotalMilliseconds * 1000d / iterations;
        var frameBudgetPercent = microsecondsPerCall * 2 / 16_700d * 100d;

        Assert.True(microsecondsPerCall < 500,
            $"一次降采样实测 {microsecondsPerCall:F1} µs（两块图表各跑一次占满帧率预算 {frameBudgetPercent:F3}%），" +
            "超出 500 µs 这条上限——原测量值 39.1~56.9 µs，留的是十倍余量");

        PointBufferPool.ReturnPoints(ref buffer);
    }

    private static int Run(
        IReadOnlyList<NoiseRealtimePoint> series,
        int startIndex,
        int endExclusive,
        ref Point[]? buffer,
        int maxSamples = 420)
        => StudyChartGeometry.BuildPlotPoints(
            series, startIndex, endExclusive, Plot, Origin, PixelsPerSecond, MinDb, MaxDb, maxSamples, ref buffer);

    private static IReadOnlyList<NoiseRealtimePoint> CreateSeries(int count, Func<int, double>? db = null)
    {
        var points = new List<NoiseRealtimePoint>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(CreatePoint(i, db is null ? 50d : db(i)));
        }

        return points;
    }

    private static NoiseRealtimePoint CreatePoint(int index, double displayDb) =>
        new(Origin.AddSeconds(index * 0.5), 0.1, -20d, displayDb, 0.2, false);
}
