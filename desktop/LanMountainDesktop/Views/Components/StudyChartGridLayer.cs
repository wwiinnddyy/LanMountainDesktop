using Avalonia;
using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 图表底格："按当前绘图区尺寸建一次网格与坐标轴的几何，之后每帧只画"这个缓存层，
/// 噪声曲线与噪声分布面积图此前各抄了一份逐字相同的 <c>DrawGrid</c>（7 行）
/// 加一份逐字相同的 <c>BuildGridGeometry</c>（25 行）。
///
/// 收进一个持有缓存的对象，而不是一个静态方法：这一层的真值就是"缓存与画布尺寸配不配"这个状态，
/// 静态方法要调用方把 <c>Rect</c> 与两个 <c>StreamGeometry</c> 用 <c>ref</c> 传进来——
/// 传错一个（比如忘了 <c>_cachedGridPlot = plot</c>）不报错，症状是尺寸变了网格不跟着变，
/// 或者反过来：每帧重建几何。两家原本连字段名都一样，抄的时候正是照抄这三行声明。
///
/// 每帧代价：命中缓存时是两次 <c>DrawGeometry</c>；只有绘图区尺寸真的变了才重建
/// （<c>InvalidateVisual</c> 由序列指纹触发，尺寸不变就不进这条）。
/// </summary>
internal sealed class StudyChartGridLayer
{
    private const int HorizontalDivisions = 4;
    private const int VerticalDivisions = 4;

    private StreamGeometry? _gridGeometry;
    private StreamGeometry? _axisGeometry;
    private Rect _cachedPlot;

    /// <summary>控件从视觉树上摘下去时丢掉几何，重新上台面按新尺寸重建一次。</summary>
    public void Invalidate()
    {
        _gridGeometry = null;
        _axisGeometry = null;
        _cachedPlot = default;
    }

    public void Draw(DrawingContext context, Rect plot, Pen gridPen, Pen axisPen)
    {
        if (_gridGeometry is null || _axisGeometry is null || _cachedPlot != plot)
        {
            _cachedPlot = plot;
            (_gridGeometry, _axisGeometry) = BuildGeometry(plot);
        }

        context.DrawGeometry(brush: null, pen: gridPen, _gridGeometry);
        context.DrawGeometry(brush: null, pen: axisPen, _axisGeometry);
    }

    private static (StreamGeometry Grid, StreamGeometry Axis) BuildGeometry(Rect plot)
    {
        var grid = new StreamGeometry();
        using (var builder = grid.Open())
        {
            for (var i = 0; i <= HorizontalDivisions; i++)
            {
                var y = plot.Top + plot.Height * (i / (double)HorizontalDivisions);
                StudyChartGeometry.AddLine(builder, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            for (var i = 0; i <= VerticalDivisions; i++)
            {
                var x = plot.Left + plot.Width * (i / (double)VerticalDivisions);
                StudyChartGeometry.AddLine(builder, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
        }

        var axis = new StreamGeometry();
        using (var builder = axis.Open())
        {
            StudyChartGeometry.AddLine(builder, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
            StudyChartGeometry.AddLine(builder, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        }

        return (grid, axis);
    }
}
