using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 桌面组件的黑夜模式判定，唯一一份实现。此前 19 个组件各自复制了这个判断，
/// 而且收尾兜底并不一致（11 个在拿不到主题档时当"夜"，7 个当"昼"，MusicControlWidget
/// 则回退到应用级主题档），同一个异常状态下不同组件会选出相反的配色。
/// 现在公共部分收敛到这里，兜底由调用方显式传入，分歧变得可见而不是隐形。
/// </summary>
public static class ComponentThemeMode
{
    private const string SurfaceBaseBrushKey = ThemeResourceKeys.SurfaceBaseBrush;
    private const double NightSurfaceLuminanceThreshold = 0.45;

    /// <summary>
    /// 重判黑夜档，并紧接着重排一次。这两步是配对的：只改字段不重排，组件会按旧档继续画，
    /// 症状是"主题已经翻成夜间、面板还是白天色"，且不报错。
    /// 此前这一步在 6 个组件里逐字各抄一份（判黑夜 + UpdateAdaptiveLayout）。
    /// </summary>
    public static void RefreshNightVisual(
        Control control,
        ref bool isNightVisual,
        Action reflow,
        bool fallbackToNightWhenSurfaceUnknown = false)
    {
        isNightVisual = ResolveIsNight(control, fallbackToNightWhenSurfaceUnknown);
        reflow();
    }

    public static bool ResolveIsNight(
        Control control,
        bool fallbackToNightWhenSurfaceUnknown = false)
    {
        if (control.ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (control.ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (AdaptiveTokens.TryGet<ISolidColorBrush>(control, SurfaceBaseBrushKey, out var brush))
        {
            return ColorMath.RelativeLuminance(brush.Color) < NightSurfaceLuminanceThreshold;
        }

        return fallbackToNightWhenSurfaceUnknown;
    }
}
