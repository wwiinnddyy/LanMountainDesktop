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
