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
    /// 任务条岛那种"外框本身就大一号"的容器用的 <c>Lg</c> 档圆角；
    /// 桌面网格组件的外框请用 <see cref="ResolveMainRectangleRadius"/>（<c>Component</c> 档）。
    /// 2026-09-26 拍的（#G1-AR）：两档在 Balanced/Rounded/Open 下数值不同（24 vs 28 等），
    /// 而桌面网格的选中环画在宿主 host 的边框上、读的就是 <c>Component</c> 档，组件外框只隔 2~12px——
    /// 规则是<b>谁与谁重合就随谁</b>，所以进网格的 10 个组件外框改走 Component 档；
    /// 任务条岛里的 Clock / TextCapsule / NetworkSpeed 继续读 Lg，因为它们的容器
    /// <c>BottomTaskbarContainer</c> 本身就是 Lg，那是层级一致而不是重合。
    /// 这条规则由 <c>SourceIntegrityTests.DesktopGridWidgetFrames_UseTheTierTheirRingUses</c> 双向拦。
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
