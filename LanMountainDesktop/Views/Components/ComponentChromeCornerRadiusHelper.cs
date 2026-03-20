using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

internal static class ComponentChromeCornerRadiusHelper
{
    public static double ResolveScale(ComponentChromeContext? chromeContext = null)
    {
        if (chromeContext is not null)
        {
            return Math.Max(0.1d, chromeContext.GlobalCornerRadiusScale);
        }

        return Math.Max(0.1d, HostAppearanceThemeProvider.GetOrCreate().GetCurrent().GlobalCornerRadiusScale);
    }

    public static CornerRadius Scale(double baseRadius, double min, double max, ComponentChromeContext? chromeContext = null)
    {
        var scale = ResolveScale(chromeContext);
        return new CornerRadius(Math.Clamp(baseRadius * scale, min * scale, max * scale));
    }

    public static void Apply(CornerRadius radius, params Border?[] chromeLayers)
    {
        foreach (var chromeLayer in chromeLayers)
        {
            if (chromeLayer is not null)
            {
                chromeLayer.CornerRadius = radius;
            }
        }
    }

    public static CornerRadius ResolveToken(string key, double fallback)
    {
        var application = Application.Current;
        return application is not null &&
               application.Resources.TryGetResource(key, application.ActualThemeVariant, out var resource) &&
               resource is CornerRadius radius
            ? radius
            : new CornerRadius(fallback);
    }

    public static double ScaleValue(double value, ComponentChromeContext? chromeContext = null)
    {
        return value * ResolveScale(chromeContext);
    }

    public static double ResolveContentSafetyScale(
        ComponentChromeContext? chromeContext = null,
        double responsiveness = 0.45d)
    {
        var scale = ResolveScale(chromeContext);
        var normalizedResponsiveness = Math.Clamp(responsiveness, 0d, 1d);
        return 1d + ((scale - 1d) * normalizedResponsiveness);
    }

    public static double SafeValue(
        double baseValue,
        double min,
        double max,
        ComponentChromeContext? chromeContext = null,
        double responsiveness = 0.45d)
    {
        var safetyScale = ResolveContentSafetyScale(chromeContext, responsiveness);
        return Math.Clamp(baseValue * safetyScale, min * safetyScale, max * safetyScale);
    }

    public static Thickness SafeThickness(
        double left,
        double top,
        double right,
        double bottom,
        ComponentChromeContext? chromeContext = null,
        double responsiveness = 0.45d)
    {
        return new Thickness(
            SafeValue(left, 0, left, chromeContext, responsiveness),
            SafeValue(top, 0, top, chromeContext, responsiveness),
            SafeValue(right, 0, right, chromeContext, responsiveness),
            SafeValue(bottom, 0, bottom, chromeContext, responsiveness));
    }

    public static Thickness SafeThickness(
        double horizontal,
        double vertical,
        ComponentChromeContext? chromeContext = null,
        double responsiveness = 0.45d)
    {
        return SafeThickness(horizontal, vertical, horizontal, vertical, chromeContext, responsiveness);
    }
}
