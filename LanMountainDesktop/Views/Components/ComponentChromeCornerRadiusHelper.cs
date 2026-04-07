using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;

namespace LanMountainDesktop.Views.Components;

internal static class ComponentChromeCornerRadiusHelper
{
    public static double ResolveMainRectangleRadiusValue(ComponentChromeContext? chromeContext = null, double fallback = 24d)
    {
        if (chromeContext is not null)
        {
            return Math.Max(0d, chromeContext.CornerRadiusTokens.Component.TopLeft);
        }

        var snapshot = HostAppearanceThemeProvider.GetOrCreate().GetCurrent();
        var resolved = snapshot.CornerRadiusTokens.Component.TopLeft;
        return double.IsFinite(resolved)
            ? Math.Max(0d, resolved)
            : Math.Max(0d, fallback);
    }

    public static CornerRadius ResolveMainRectangleRadius(ComponentChromeContext? chromeContext = null, double fallback = 24d)
    {
        return new CornerRadius(ResolveMainRectangleRadiusValue(chromeContext, fallback));
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

    public static double SafeValue(double value, double min, double max, ComponentChromeContext? context = null)
    {
        _ = context;
        return Math.Clamp(value, min, max);
    }

    public static double Scale(double value, double min, double max, ComponentChromeContext? context = null)
    {
        _ = context;
        return Math.Clamp(value, min, max);
    }

    public static CornerRadius SafeRadius(double value, double min, double max, ComponentChromeContext? context = null)
    {
        _ = context;
        return new CornerRadius(Math.Clamp(value, min, max));
    }

    public static CornerRadius ScaleRadius(double value, double min, double max, ComponentChromeContext? context = null)
    {
        _ = context;
        return new CornerRadius(Math.Clamp(value, min, max));
    }

    public static double Mini(ComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Micro.TopLeft;
        return ResolveToken("DesignCornerRadiusMicro", 6).TopLeft;
    }

    public static double Micro(ComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Micro.TopLeft;
        return ResolveToken("DesignCornerRadiusMicro", 6).TopLeft;
    }

    public static double Small(ComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Sm.TopLeft;
        return ResolveToken("DesignCornerRadiusSm", 14).TopLeft;
    }

    public static double Medium(ComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Md.TopLeft;
        return ResolveToken("DesignCornerRadiusMd", 20).TopLeft;
    }

    public static double Large(ComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Lg.TopLeft;
        return ResolveToken("DesignCornerRadiusLg", 28).TopLeft;
    }
}
