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

    /// <summary>
    /// WCAG 相对亮度。桌面组件曾各自复制过 21 份实现（阈值 0.03928 的旧写法），
    /// 现在只认这里的一份：与组件侧旧值的差异只在字节 10 这一个点上、量级 1e-4，
    /// 不影响任何一处 <c>&lt; 0.45</c> 之类的判定。
    /// </summary>
    public static double RelativeLuminance(Color color)
    {
        var red = ToLinear(color.R / 255d);
        var green = ToLinear(color.G / 255d);
        var blue = ToLinear(color.B / 255d);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    /// <summary>把半透明前景叠到不透明底色上，得到实际呈现的不透明色。</summary>
    public static Color ToOpaqueAgainst(Color foreground, Color background)
    {
        if (foreground.A >= 0xFF)
        {
            return Color.FromArgb(0xFF, foreground.R, foreground.G, foreground.B);
        }

        var alpha = foreground.A / 255d;
        var inverse = 1 - alpha;
        var red = (byte)Math.Round((foreground.R * alpha) + (background.R * inverse));
        var green = (byte)Math.Round((foreground.G * alpha) + (background.G * inverse));
        var blue = (byte)Math.Round((foreground.B * alpha) + (background.B * inverse));
        return Color.FromArgb(0xFF, red, green, blue);
    }

    /// <summary>前景在多个候选底色上最差的那一档对比度；没有候选时按满分 21 返回。</summary>
    public static double MinContrastRatio(Color foreground, IReadOnlyList<Color> backgrounds)
    {
        if (backgrounds.Count == 0)
        {
            return 21;
        }

        var minimum = double.MaxValue;
        for (var index = 0; index < backgrounds.Count; index++)
        {
            var background = backgrounds[index];
            var visibleForeground = foreground.A >= 0xFF
                ? Color.FromArgb(0xFF, foreground.R, foreground.G, foreground.B)
                : ToOpaqueAgainst(foreground, background);

            var ratio = ContrastRatio(visibleForeground, background);
            if (ratio < minimum)
            {
                minimum = ratio;
            }
        }

        return minimum;
    }

    private static double ToLinear(double value)
    {
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}

