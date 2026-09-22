using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;

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

    /// <summary>
    /// 主面板外框用的 <c>Lg</c> 档圆角（<c>ResolveMainRectangleRadius</c> 用的是 <c>Component</c> 档，
    /// 两档在 Balanced/Rounded/Open 三种圆角风格下数值不同——24 vs 28 这类差异是设计问题，已单独登记等拍板）。
    /// 2026-09-22 收这一族：13 个组件各自抄了一份 <c>private static double ResolveUnifiedMainRadiusValue()</c>
    /// 加 <c>private CornerRadius ResolveUnifiedMainRectangle()</c>，除了没有下面的有限性兜底，
    /// 还一律无视 <c>chromeContext</c>（宿主可以按组件下发 chrome，那 13 个组件不吃）。
    /// </summary>
    public static double ResolveLgRectangleRadiusValue(AirAppComponentChromeContext? chromeContext = null, double fallback = 28d)
    {
        var resolved = chromeContext is not null
            ? chromeContext.CornerRadiusTokens.Lg.TopLeft
            : HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;
        return double.IsFinite(resolved)
            ? Math.Max(0d, resolved)
            : Math.Max(0d, fallback);
    }

    public static CornerRadius ResolveLgRectangle(AirAppComponentChromeContext? chromeContext = null, double fallback = 28d)
    {
        return new CornerRadius(ResolveLgRectangleRadiusValue(chromeContext, fallback));
    }

    public static CornerRadius ResolveToken(string key, double fallback)
    {
        var application = Application.Current;
        return application is not null && AdaptiveTokens.TryGet<CornerRadius>(application, key, out var radius)
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
