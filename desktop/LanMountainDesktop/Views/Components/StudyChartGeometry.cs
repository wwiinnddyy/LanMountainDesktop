using System.Collections.Generic;

using Avalonia;
using Avalonia.Media;

using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 学习图表里"两个点之间画一段"的唯一写法。噪声曲线与噪声分布面积图两个自绘控件
/// 此前各抄了一份逐字相同的 <c>AddLine</c>（ BeginFigure → LineTo → EndFigure 三步）。
/// 三步的顺序错了不报错，只会画出一条不闭合或方向不对的折线。
/// </summary>
internal static class StudyChartGeometry
{
    public static void AddLine(StreamGeometryContext builder, Point start, Point end)
    {
        builder.BeginFigure(start, isFilled: false);
        builder.LineTo(end);
        builder.EndFigure(isClosed: false);
    }

    /// <summary>
    /// 时间轴 → 逻辑横坐标（原点起算、不往负走）。两个自绘图表控件此前各带一份逐字相同的实现，
    /// 降采样里每个点都要过它一次，所以它必须是内联得开的单条算式。
    /// </summary>
    internal static double MapTimestampToLogicalX(DateTimeOffset timestamp, DateTimeOffset origin, double pixelsPerSecond)
    {
        return Math.Max(0, (timestamp - origin).TotalSeconds * pixelsPerSecond);
    }

    /// <summary>
    /// 一个采样点 → 画布上的点。纵向把 dB 夹进 <c>[minDb, maxDb]</c> 再线性映射到 <c>plot</c> 的高度。
    /// 两家的差别只在窗口：曲线是固定的 20..100，面积图跟着基线走（<c>baseline-5 .. baseline+25</c>），
    /// 所以窗口由调用方给；分母带 0.001 的下限是面积图原本就有的守卫（基线窗口理论上可以被配成零宽），
    /// 对固定的 80 宽窗口没有任何影响。
    /// </summary>
    public static Point MapToPlot(
        Rect plot,
        NoiseRealtimePoint point,
        DateTimeOffset origin,
        double pixelsPerSecond,
        double minDb,
        double maxDb)
    {
        var x = MapTimestampToLogicalX(point.Timestamp, origin, pixelsPerSecond);
        return new Point(x, MapDbToY(plot, point.DisplayDb, minDb, maxDb));
    }

    /// <summary>
    /// dB → 画布纵坐标。面积图除了折线，还要拿它画"等级色带"的四条分界线（基线、基线+10、基线+20），
    /// 那里只有 dB 数、没有采样点，所以这条映射单独有个家，而不是藏在 <see cref="MapToPlot"/> 里。
    /// </summary>
    public static double MapDbToY(Rect plot, double displayDb, double minDb, double maxDb)
    {
        var normalized = (Math.Clamp(displayDb, minDb, maxDb) - minDb) / Math.Max(0.001, maxDb - minDb);
        return plot.Bottom - normalized * plot.Height;
    }

    /// <summary>
    /// 折线/面积图的降采样：把 <c>[startIndex, endExclusive)</c> 这段采样压进最多 <c>maxSamples</c> 个点——
    /// 装得下就原样映射，装不下就分桶，每桶各取<b>最低点与最高点</b>（按源顺序发射手柄，
    /// 与首尾两点一起保住波形的峰谷）。两个自绘控件此前各抄了一份逐字相同的 86 行（今天并到这里）。
    /// 缓存的 <c>Point[]</c> 由 <see cref="PointBufferPool"/> 租还，用 <c>ref</c> 传调用方的字段，
    /// 这样"每次重画都新分配一个数组"这种形状不可能出现（每帧最多一次租）。
    /// 代价上限：源侧最多 1200 个点（<c>StudyAnalyticsConfig.RealtimeBufferCapacity</c> 夹在 60..1200），
    /// 目标侧最多 420（画布宽度夹的），所以整段是几千次算术——只在序列指纹变了、真的 InvalidateVisual 时跑。
    /// </summary>
    public static int BuildPlotPoints(
        IReadOnlyList<NoiseRealtimePoint> source,
        int startIndex,
        int endExclusive,
        Rect plot,
        DateTimeOffset logicalOrigin,
        double pixelsPerSecond,
        double minDb,
        double maxDb,
        int maxSamples,
        ref Point[]? buffer)
    {
        var sourceCount = endExclusive - startIndex;
        if (sourceCount <= 1)
        {
            return 0;
        }

        if (sourceCount <= maxSamples)
        {
            PointBufferPool.RentPointsAtLeast(ref buffer, sourceCount);
            if (buffer is null)
            {
                return 0;
            }

            for (var i = 0; i < sourceCount; i++)
            {
                buffer[i] = MapToPlot(plot, source[startIndex + i], logicalOrigin, pixelsPerSecond, minDb, maxDb);
            }

            return sourceCount;
        }

        var bucketCount = Math.Max(1, (maxSamples - 2) / 2);
        var targetCapacity = 2 + bucketCount * 2;
        PointBufferPool.RentPointsAtLeast(ref buffer, targetCapacity);
        if (buffer is null)
        {
            return 0;
        }

        var outputIndex = 0;
        buffer[outputIndex++] = MapToPlot(plot, source[startIndex], logicalOrigin, pixelsPerSecond, minDb, maxDb);

        var middleCount = sourceCount - 2;
        var bucketWidth = middleCount / (double)bucketCount;
        var lastSourceIndex = startIndex;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var rangeStart = startIndex + 1 + (int)Math.Floor(bucket * bucketWidth);
            var rangeEnd = startIndex + 1 + (int)Math.Floor((bucket + 1) * bucketWidth);
            if (bucket == bucketCount - 1)
            {
                rangeEnd = endExclusive - 1;
            }

            rangeStart = Math.Clamp(rangeStart, startIndex + 1, endExclusive - 2);
            rangeEnd = Math.Clamp(rangeEnd, rangeStart + 1, endExclusive - 1);

            var minIndex = rangeStart;
            var maxIndex = rangeStart;
            var minValue = source[rangeStart].DisplayDb;
            var maxValue = minValue;

            for (var i = rangeStart + 1; i < rangeEnd; i++)
            {
                var value = source[i].DisplayDb;
                if (value < minValue)
                {
                    minValue = value;
                    minIndex = i;
                }

                if (value > maxValue)
                {
                    maxValue = value;
                    maxIndex = i;
                }
            }

            if (minIndex == maxIndex)
            {
                if (minIndex != lastSourceIndex)
                {
                    buffer[outputIndex++] = MapToPlot(plot, source[minIndex], logicalOrigin, pixelsPerSecond, minDb, maxDb);
                    lastSourceIndex = minIndex;
                }

                continue;
            }

            var first = minIndex < maxIndex ? minIndex : maxIndex;
            var second = minIndex < maxIndex ? maxIndex : minIndex;

            if (first != lastSourceIndex)
            {
                buffer[outputIndex++] = MapToPlot(plot, source[first], logicalOrigin, pixelsPerSecond, minDb, maxDb);
                lastSourceIndex = first;
            }

            if (second != lastSourceIndex)
            {
                buffer[outputIndex++] = MapToPlot(plot, source[second], logicalOrigin, pixelsPerSecond, minDb, maxDb);
                lastSourceIndex = second;
            }
        }

        var finalIndex = endExclusive - 1;
        if (finalIndex != lastSourceIndex)
        {
            buffer[outputIndex++] = MapToPlot(plot, source[finalIndex], logicalOrigin, pixelsPerSecond, minDb, maxDb);
        }

        return outputIndex;
    }
}
