using System;
using System.Collections.Generic;
using Avalonia.Media;
using LanMontainDesktop.Theme;

namespace LanMontainDesktop.Views.Components;

internal static class WeatherTypographyAccessibility
{
    // WCAG-inspired targets used by the project theme system.
    public const double WcagNormalTextContrast = 4.5;
    public const double WcagLargeTextContrast = 3.0;
    private const double LightTextLuminanceFloor = 0.58;

    public static IReadOnlyList<Color> BuildBackgroundSamples(
        string gradientFromHex,
        string gradientToHex,
        string tintHex,
        bool isNightVisual)
    {
        var from = Color.Parse(gradientFromHex);
        var to = Color.Parse(gradientToHex);
        var tint = Color.Parse(tintHex);
        var mid = ColorMath.Blend(from, to, 0.52);
        var tinted = ColorMath.Blend(mid, tint, isNightVisual ? 0.34 : 0.28);
        var shaded = ColorMath.Blend(tinted, Color.Parse("#FF0B1220"), isNightVisual ? 0.24 : 0.16);
        var lightProbe = ColorMath.Blend(mid, Color.Parse("#FFFFFFFF"), 0.12);

        return
        [
            from,
            to,
            mid,
            tinted,
            shaded,
            lightProbe
        ];
    }

    public static IBrush CreateReadableBrush(
        string preferredHex,
        IReadOnlyList<Color> backgroundSamples,
        double minRatio,
        byte desiredAlpha = 0xFF)
    {
        var preferred = Color.Parse(preferredHex);
        return new SolidColorBrush(CreateReadableColor(preferred, backgroundSamples, minRatio, desiredAlpha));
    }

    private static Color CreateReadableColor(
        Color preferred,
        IReadOnlyList<Color> backgroundSamples,
        double minRatio,
        byte desiredAlpha)
    {
        var lightPreferred = EnsureLightTone(Color.FromArgb(0xFF, preferred.R, preferred.G, preferred.B));
        if (backgroundSamples.Count == 0)
        {
            return desiredAlpha >= 0xFF
                ? lightPreferred
                : Color.FromArgb(desiredAlpha, lightPreferred.R, lightPreferred.G, lightPreferred.B);
        }

        var opaque = EnsureContrastPreservingTone(lightPreferred, backgroundSamples, minRatio);
        if (desiredAlpha >= 0xFF)
        {
            return Color.FromArgb(0xFF, opaque.R, opaque.G, opaque.B);
        }

        var alpha = AdjustAlphaForContrast(opaque, backgroundSamples, minRatio, desiredAlpha);
        return Color.FromArgb(alpha, opaque.R, opaque.G, opaque.B);
    }

    private static Color EnsureContrastPreservingTone(
        Color preferred,
        IReadOnlyList<Color> backgroundSamples,
        double minRatio)
    {
        if (MinContrastRatio(preferred, backgroundSamples) >= minRatio)
        {
            return preferred;
        }

        var white = Color.Parse("#FFFFFFFF");

        if (TryFindBlendRatio(preferred, white, backgroundSamples, minRatio, out var whiteDelta))
        {
            return ColorMath.Blend(preferred, white, whiteDelta);
        }

        // Enforce light typography: never fall back to dark text.
        return white;
    }

    private static bool TryFindBlendRatio(
        Color source,
        Color target,
        IReadOnlyList<Color> backgroundSamples,
        double minRatio,
        out double blendRatio)
    {
        if (MinContrastRatio(target, backgroundSamples) < minRatio)
        {
            blendRatio = double.PositiveInfinity;
            return false;
        }

        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 16; i++)
        {
            var mid = (low + high) / 2d;
            var candidate = ColorMath.Blend(source, target, mid);
            if (MinContrastRatio(candidate, backgroundSamples) >= minRatio)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        blendRatio = high;
        return true;
    }

    private static byte AdjustAlphaForContrast(
        Color opaqueColor,
        IReadOnlyList<Color> backgroundSamples,
        double minRatio,
        byte desiredAlpha)
    {
        var alpha = desiredAlpha;
        while (alpha < 0xFF)
        {
            var candidate = Color.FromArgb(alpha, opaqueColor.R, opaqueColor.G, opaqueColor.B);
            if (MinContrastRatio(candidate, backgroundSamples) >= minRatio)
            {
                return alpha;
            }

            alpha = (byte)Math.Min(0xFF, alpha + 4);
        }

        return 0xFF;
    }

    private static double MinContrastRatio(Color foreground, IReadOnlyList<Color> backgroundSamples)
    {
        var minimum = double.MaxValue;
        for (var i = 0; i < backgroundSamples.Count; i++)
        {
            var bg = backgroundSamples[i];
            var visibleForeground = foreground.A >= 0xFF
                ? Color.FromArgb(0xFF, foreground.R, foreground.G, foreground.B)
                : CompositeOverBackground(foreground, bg);
            var ratio = ColorMath.ContrastRatio(visibleForeground, bg);
            if (ratio < minimum)
            {
                minimum = ratio;
            }
        }

        return minimum;
    }

    private static Color CompositeOverBackground(Color foreground, Color background)
    {
        var alpha = foreground.A / 255d;
        var red = (byte)Math.Round((foreground.R * alpha) + (background.R * (1 - alpha)));
        var green = (byte)Math.Round((foreground.G * alpha) + (background.G * (1 - alpha)));
        var blue = (byte)Math.Round((foreground.B * alpha) + (background.B * (1 - alpha)));
        return Color.FromArgb(0xFF, red, green, blue);
    }

    private static bool IsLightText(Color color)
    {
        return RelativeLuminance(color) >= LightTextLuminanceFloor;
    }

    private static Color EnsureLightTone(Color color)
    {
        if (IsLightText(color))
        {
            return color;
        }

        var white = Color.Parse("#FFFFFFFF");
        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 16; i++)
        {
            var mid = (low + high) / 2d;
            var candidate = ColorMath.Blend(color, white, mid);
            if (IsLightText(candidate))
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return ColorMath.Blend(color, white, high);
    }

    private static double RelativeLuminance(Color color)
    {
        var red = ToLinear(color.R / 255d);
        var green = ToLinear(color.G / 255d);
        var blue = ToLinear(color.B / 255d);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double ToLinear(double channel)
    {
        return channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
