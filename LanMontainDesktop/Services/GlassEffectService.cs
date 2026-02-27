using Avalonia.Controls;
using Avalonia.Media;
using LanMontainDesktop.Theme;

namespace LanMontainDesktop.Services;

public static class GlassEffectService
{
    private const double DayPanelBlurRadius = 40;
    private const double DayStrongBlurRadius = 60;
    private const double DayOverlayBlurRadius = 80;
    private const double NightPanelBlurRadius = 45;
    private const double NightStrongBlurRadius = 65;
    private const double NightOverlayBlurRadius = 85;

    public static void ApplyGlassResources(IResourceDictionary resources, ThemeColorContext context)
    {
        var neutralBase = context.IsNightMode ? Color.Parse("#FF0B1220") : Color.Parse("#FFF8FAFC");
        var neutralElevated = context.IsNightMode ? Color.Parse("#FF1E293B") : Color.Parse("#FFFFFFFF");
        var tintMix = context.IsNightMode ? 0.15 : 0.08;
        var panelTone = ColorMath.Blend(neutralElevated, context.AccentColor, tintMix);
        var strongTone = ColorMath.Blend(neutralBase, context.AccentColor, context.IsNightMode ? 0.18 : 0.12);
        var overlayTone = ColorMath.Blend(neutralBase, context.AccentColor, context.IsNightMode ? 0.25 : 0.15);

        resources["AdaptiveButtonBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(panelTone, context.IsNightMode ? (byte)0x66 : (byte)0x80));
        resources["AdaptiveButtonBorderBrush"] = new SolidColorBrush(ColorMath.WithAlpha(neutralElevated, 0x1A));
        resources["AdaptiveButtonHoverBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(ColorMath.Blend(panelTone, context.AccentColor, 0.15), context.IsNightMode ? (byte)0x7A : (byte)0x99));
        resources["AdaptiveButtonPressedBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(ColorMath.Blend(panelTone, context.AccentColor, 0.28), context.IsNightMode ? (byte)0x8C : (byte)0xB3));

        resources["AdaptiveGlassPanelBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(panelTone, context.IsNightMode ? (byte)0x4D : (byte)0x66));
        resources["AdaptiveGlassPanelBorderBrush"] = new SolidColorBrush(ColorMath.WithAlpha(neutralElevated, 0x26));
        resources["AdaptiveGlassStrongBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(strongTone, context.IsNightMode ? (byte)0x66 : (byte)0x80));
        resources["AdaptiveGlassStrongBorderBrush"] = new SolidColorBrush(ColorMath.WithAlpha(neutralElevated, 0x33));
        resources["AdaptiveGlassOverlayBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(overlayTone, context.IsNightMode ? (byte)0x59 : (byte)0x73));

        resources["AdaptiveGlassPanelBlurRadius"] = context.IsNightMode ? NightPanelBlurRadius : DayPanelBlurRadius;
        resources["AdaptiveGlassStrongBlurRadius"] = context.IsNightMode ? NightStrongBlurRadius : DayStrongBlurRadius;
        resources["AdaptiveGlassOverlayBlurRadius"] = context.IsNightMode ? NightOverlayBlurRadius : DayOverlayBlurRadius;
        resources["AdaptiveGlassPanelOpacity"] = context.IsNightMode ? 0.85 : 0.80;
        resources["AdaptiveGlassStrongOpacity"] = context.IsNightMode ? 0.90 : 0.85;
        resources["AdaptiveGlassOverlayOpacity"] = context.IsNightMode ? 0.75 : 0.70;
        resources["AdaptiveGlassNoiseOpacity"] = context.IsNightMode ? 0.03 : 0.02;
    }
}
