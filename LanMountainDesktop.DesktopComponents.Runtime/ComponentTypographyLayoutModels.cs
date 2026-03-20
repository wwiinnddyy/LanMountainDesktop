using Avalonia;
using Avalonia.Media;

namespace LanMountainDesktop.DesktopComponents.Runtime;

public readonly record struct ComponentAdaptiveTextLayout(
    double FontSize,
    FontWeight Weight,
    int MaxLines,
    double LineHeight,
    double OverflowScore,
    bool FitsCompletely,
    Size MeasuredSize)
{
    public double MeasuredWidth => MeasuredSize.Width;

    public double MeasuredHeight => MeasuredSize.Height;
}

public readonly record struct ComponentBoxLayout(
    double Width,
    double Height,
    Thickness Margin,
    Thickness Padding)
{
    public double Size => Math.Max(Width, Height);

    public bool IsSquare => Math.Abs(Width - Height) <= 0.001d;
}

