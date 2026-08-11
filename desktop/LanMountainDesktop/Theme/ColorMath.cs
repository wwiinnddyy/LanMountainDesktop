using System;
using Avalonia.Media;

namespace LanMountainDesktop.Theme;

public static class ColorMath
{
    public static Color Blend(Color from, Color to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var inverse = 1 - ratio;
        var red = (byte)Math.Round((from.R * inverse) + (to.R * ratio));
        var green = (byte)Math.Round((from.G * inverse) + (to.G * ratio));
        var blue = (byte)Math.Round((from.B * inverse) + (to.B * ratio));
        return Color.FromRgb(red, green, blue);
    }

    public static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    public static Color EnsureContrast(Color preferred, Color background, double minRatio)
    {
        if (ContrastRatio(preferred, background) >= minRatio)
        {
            return preferred;
        }

        var white = Color.Parse("#FFFFFFFF");
        var black = Color.Parse("#FF000000");
        var whiteRatio = ContrastRatio(white, background);
        var blackRatio = ContrastRatio(black, background);
        return whiteRatio >= blackRatio ? white : black;
    }

    public static double ContrastRatio(Color first, Color second)
    {
        var firstLum = RelativeLuminance(first);
        var secondLum = RelativeLuminance(second);
        var lighter = Math.Max(firstLum, secondLum);
        var darker = Math.Min(firstLum, secondLum);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        var red = ToLinear(color.R / 255d);
        var green = ToLinear(color.G / 255d);
        var blue = ToLinear(color.B / 255d);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double ToLinear(double value)
    {
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}

