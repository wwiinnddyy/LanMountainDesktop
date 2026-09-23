using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件文字测量与可变字重插值的公共口径。<c>MeasureTextSize</c> 抄了 4 份、
/// <c>ToVariableWeight</c> 抄了 9 份、线性插值抄了 9 份。
/// 插值刻意留两个入口：日历/时钟那一组传的是已经算好的比例，不再夹紧；
/// 学习组件那一组传的是可能过冲的进度，必须夹在 0..1。这两种语义都是既成的，不做统一。
/// </summary>
public static class ComponentTypography
{
    public static Size MeasureTextSize(string text, double fontSize, FontWeight weight, double maxWidth, double lineHeight)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = lineHeight
        };

        probe.Measure(new Size(Math.Max(1, maxWidth), double.PositiveInfinity));
        return probe.DesiredSize;
    }

    /// <summary>可变字体轴值：只接受 1..1000，越界按端点夹。</summary>
    public static FontWeight ToVariableWeight(double value)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(value), 1, 1000);
    }

    public static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    public static double LerpClamped(double from, double to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return from + ((to - from) * ratio);
    }

    /// <summary>
    /// 在给定盒子里把文字塞得下得最大字号：对字号做 18 轮二分，量得的行高/行数由本类的
    /// <c>MeasureTextSize</c> 给。<b>三份逐字相同的抄本 2026-09-23 并到这里</b>
    /// （<c>DailyArtwork</c>、<c>DailyWord</c>、<c>DailyWord2x2</c>）。
    ///
    /// 三个数字不许漂，漂开的症状都不是崩溃而是"看着不对"：
    /// 18 轮决定收敛精度（改小会在大盒子里给出偏小的字号）；<c>Math.Max(6, minFontSize)
    /// 是字号地板（低于 6 磅文字就读不了了）；<c>maxHeight + 0.6</c> 那条余量是量取与排版之间
    /// 的舍入差——把它抹掉会让"刚好放得下"的那一档被判成放不下，字号小一级。
    /// 空文本按一个空格量（按空串量会得到 0 高，二分会一路顶到上限然后溢出）。
    /// </summary>
    public static double FitFontSize(
        string? text,
        double maxWidth,
        double maxHeight,
        int maxLines,
        double minFontSize,
        double maxFontSize,
        FontWeight weight,
        double lineHeightFactor)
    {
        var content = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
        var min = Math.Max(6, minFontSize);
        var max = Math.Max(min, maxFontSize);
        var low = min;
        var high = max;
        var best = min;

        for (var i = 0; i < 18; i++)
        {
            var candidate = (low + high) / 2d;
            var lineHeight = candidate * lineHeightFactor;
            var size = ComponentTypography.MeasureTextSize(content, candidate, weight, Math.Max(1, maxWidth), lineHeight);
            var lineCount = Math.Max(1, (int)Math.Ceiling(size.Height / Math.Max(1, lineHeight)));
            var fits = size.Height <= maxHeight + 0.6 && lineCount <= Math.Max(1, maxLines);

            if (fits)
            {
                best = candidate;
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }

        return best;
    }
}
