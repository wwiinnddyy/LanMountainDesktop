using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Services;
using LanMountainDesktop.Settings.Core;

namespace LanMountainDesktop.Views.Components;

internal static class ComponentChromeCornerRadiusHelper
{
    public static double ResolveMainRectangleRadiusValue(ComponentChromeContext? chromeContext = null, double fallback = 24d)
    {
        if (chromeContext is not null)
        {
            return Math.Max(0d, chromeContext.CornerRadiusTokens.Lg.TopLeft);
        }

        var snapshot = HostAppearanceThemeProvider.GetOrCreate().GetCurrent();
        var resolved = snapshot.CornerRadiusTokens.Lg.TopLeft;
        return double.IsFinite(resolved)
            ? Math.Max(0d, resolved)
            : Math.Max(0d, fallback * ResolveScale(chromeContext));
    }

    public static CornerRadius ResolveMainRectangleRadius(ComponentChromeContext? chromeContext = null, double fallback = 24d)
    {
        return new CornerRadius(ResolveMainRectangleRadiusValue(chromeContext, fallback));
    }

    public static double ResolveScale(ComponentChromeContext? chromeContext = null)
    {
        if (chromeContext is not null)
        {
            return Math.Max(GlobalAppearanceSettings.MinimumCornerRadiusScale, chromeContext.GlobalCornerRadiusScale);
        }

        return Math.Max(
            GlobalAppearanceSettings.MinimumCornerRadiusScale,
            HostAppearanceThemeProvider.GetOrCreate().GetCurrent().GlobalCornerRadiusScale);
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
}
