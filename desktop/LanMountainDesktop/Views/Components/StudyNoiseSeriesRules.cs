using System;
using System.Collections.Generic;

using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 噪声折线的两条判据各自唯一的一份实现。
///
/// 收口前有 4 份抄本：噪声曲线与噪声分布面积图两个自绘控件各抄一份逐字相同的
/// <c>ResolveFirstTailIndex</c>（13 行），噪声分布组件与面积图控件又各抄一份逐字相同的
/// <c>ResolveLevel</c>（16 行）。两处都是"错了不报错、只是画出来的东西不对"的形状：
/// 尾窗起点取错，动态段与静段重叠或留缝；档位边界取错，同一 dB 值在面板与图表里被涂成两种颜色。
/// 面积图控件那份 <c>ResolveLayerSourceCounts</c> 两边已经漂开（它多一个 <c>isStaticSeries</c> 分支），
/// 不在这笔之内。
/// </summary>
internal static class StudyNoiseSeriesRules
{
    /// <summary>
    /// 尾窗（最近 <c>tailDuration</c> 那一段）的第一个点的下标。
    /// 找不到就回最后一点——单点与空序列都回 0，调用方按"至少一个点"画。
    /// </summary>
    public static int FirstTailIndex(IReadOnlyList<NoiseRealtimePoint> points, TimeSpan tailDuration)
    {
        if (points.Count <= 1)
        {
            return 0;
        }

        var cutoff = points[^1].Timestamp - tailDuration;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].Timestamp >= cutoff)
            {
                return i;
            }
        }

        return points.Count - 1;
    }

    /// <summary>
    /// 相对基准线的噪声档位：低于基准＝安静，往上每 10 dB 一档，两档之后＝刺耳。
    /// 边界取的是"左闭右开"，所以正好等于基准的那一格是 Normal 而不是 Quiet。
    /// </summary>
    public static NoiseDistributionLevel LevelOf(double displayDb, double baselineDb)
    {
        var quietUpper = baselineDb;
        var normalUpper = baselineDb + 10d;
        var noisyUpper = baselineDb + 20d;

        if (displayDb < quietUpper)
        {
            return NoiseDistributionLevel.Quiet;
        }

        if (displayDb < normalUpper)
        {
            return NoiseDistributionLevel.Normal;
        }

        if (displayDb < noisyUpper)
        {
            return NoiseDistributionLevel.Noisy;
        }

        return NoiseDistributionLevel.Extreme;
    }
}
