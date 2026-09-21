using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

internal static class ComponentChromeCornerRadiusHelper
{
    public static double ResolveMainRectangleRadiusValue(AirAppComponentChromeContext? chromeContext = null, double fallback = 24d)
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

    public static CornerRadius ResolveMainRectangleRadius(AirAppComponentChromeContext? chromeContext = null, double fallback = 24d)
    {
        return new CornerRadius(ResolveMainRectangleRadiusValue(chromeContext, fallback));
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

    public static double SafeValue(double value, double min, double max, AirAppComponentChromeContext? context = null)
    {
        _ = context;
        return Math.Clamp(value, min, max);
    }

    public static CornerRadius ScaleRadius(double value, double min, double max, AirAppComponentChromeContext? context = null)
    {
        _ = context;
        return new CornerRadius(Math.Clamp(value, min, max));
    }

    public static double Micro(AirAppComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Micro.TopLeft;
        return ResolveToken("DesignCornerRadiusMicro", 6).TopLeft;
    }

    public static double Small(AirAppComponentChromeContext? context = null)
    {
        if (context is not null) return context.CornerRadiusTokens.Sm.TopLeft;
        return ResolveToken("DesignCornerRadiusSm", 14).TopLeft;
    }
}
