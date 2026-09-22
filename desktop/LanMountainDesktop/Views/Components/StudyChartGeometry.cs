using Avalonia;
using Avalonia.Media;

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
}
