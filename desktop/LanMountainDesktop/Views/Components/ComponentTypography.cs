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
}
