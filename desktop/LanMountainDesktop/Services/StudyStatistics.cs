using System;

namespace LanMountainDesktop.Services;

/// <summary>
/// 学习统计里"已排序数组取分位数"的唯一实现（线性插值，与噪声面板/学习报告用的口径一致）。
///
/// 收到一处之前有 3 份：两个学习组件（扣分原因、成绩总览）各一份逐字相同的，
/// <c>StudyAnalyticsInternals</c> 又一份。三份唯一的差别是**空数组返回什么**：
/// 面板返回 <c>-100</c>（当作"没有测量值"，往下压到 dBFS 刻度底端），
/// 分析服务返回 <c>0</c>（在 dBFS 里这是一个"有读数"的值）。
/// 也就是说同一场没数据的统计，面板与学习报告可能一个显示刻度尽头、一个显示 0 dB。
/// 这个分歧是既有事实，收口不擅自统一，所以做成参数由调用方显式给——
/// 要统一成一个哨兵值请先拍板（G1-BK）。
/// </summary>
public static class StudyStatistics
{
    /// <param name="sortedValues">必须已升序排好；调用方都自己排过（分位数只在有序上才有意义）。</param>
    /// <param name="percentile">0~1，越界会被钳到端点。</param>
    /// <param name="emptySentinel">空数组时返回什么：面板传 -100、分析服务传 0，见类型注释里的 G1-BK。</param>
    public static double Percentile(double[] sortedValues, double percentile, double emptySentinel)
    {
        if (sortedValues.Length == 0)
        {
            return emptySentinel;
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
