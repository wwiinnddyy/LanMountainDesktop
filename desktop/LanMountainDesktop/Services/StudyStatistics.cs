using System;

namespace LanMountainDesktop.Services;

/// <summary>
/// 学习统计里"已排序数组取分位数"的唯一实现（线性插值，与噪声面板/学习报告用的口径一致）。
///
/// 收到一处之前有 3 份：两个学习组件（扣分原因、成绩总览）各一份逐字相同的，
/// <c>StudyAnalyticsInternals</c> 又一份。三份唯一的差别是**空数组返回什么**：
/// 面板返回 <c>-100</c>（当作"没有测量值"，往下压到 dBFS 刻度底端），
/// 分析服务返回 <c>0</c>。也就是说同一场没数据的统计，面板与学习报告可能一个显示刻度尽头、一个显示 0 dB。
///
/// 2026-09-26 拍了（G1-BK）：<b>统一成 <see cref="NoMeasurementDbfs"/>，参数一并删掉</b>。
/// 理由是刻度方向：dBFS 里 <c>0</c> 是<b>满刻度</b>（最响的那一格），把"没有测量值"报成 0 是
/// 是一个会被当成读数的假值；<c>-100</c> 在本仓静音档的默认值（<c>SilenceFloorDbfs</c> 默认 -90，
/// 并在 <c>StudyAnalyticsModels.cs:68</c> 钳到 -100..-20）之下，落在刻度尽头，不会被读成"测到了很响"。实测这五个调用点上游都已经有"空数组就别调用"的判断，
/// 所以这一改是防将来漏判，不是修今天屏幕上看得见的错。
/// </summary>
public static class StudyStatistics
{
    /// <summary>"这场统计里没有测量值"在 dBFS 刻度上的表示——比 <c>SilenceFloorDbfs(-90)</c> 还低，因此不是任何读数。</summary>
    public const double NoMeasurementDbfs = -100d;

    /// <param name="sortedValues">必须已升序排好；调用方都自己排过（分位数只在有序上才有意义）。</param>
    /// <param name="percentile">0~1，越界会被钳到端点。</param>
    public static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return NoMeasurementDbfs;
        }

        if (sortedValues.Length == 1)
        {
            return sortedValues[0];
        }

        var clamped = Math.Clamp(percentile, 0, 1);
        var position = (sortedValues.Length - 1) * clamped;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var factor = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * factor);
    }
}
