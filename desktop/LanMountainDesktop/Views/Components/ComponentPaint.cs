using Avalonia;
using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件自绘时的画笔工厂。<c>CreateBrush</c> 在 11 个组件里各抄了一份，
/// 对角渐变画笔抄了 3 份——都是同一件事：给个十六进制色值就要能直接上屏的画笔。
/// </summary>
public static class ComponentPaint
{
    public static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    /// <summary>已经是颜色的直接包成画笔（调色板对象里的色值都走这里）。</summary>
    public static SolidColorBrush CreateBrush(Color color)
    {
        return new SolidColorBrush(color);
    }

    /// <summary>左上到右下的两色对角渐变，组件表盘/进度环一类都用这一种。</summary>
    public static IBrush CreateLinearGradientBrush(string fromColorHex, string toColorHex)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse(fromColorHex), 0),
                new GradientStop(Color.Parse(toColorHex), 1)
            }
        };
    }
}
