using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

public static class GlassEffectService
{
    public static void ApplyGlassResources(IResourceDictionary resources, ThemeColorContext context)
    {
        var monetColors = context.MonetColors?.Where(color => color.A > 0).ToArray() ?? [];
        var primary = monetColors.Length > 0 ? monetColors[0] : context.AccentColor;
        var secondary = monetColors.Length > 1
            ? monetColors[1]
            : ColorMath.Blend(primary, Color.Parse("#FFFFFFFF"), 0.12);

        var panelBase = context.IsNightMode
            ? ColorMath.Blend(Color.Parse("#FF101722"), primary, 0.26)
            : ColorMath.Blend(Color.Parse("#FFF9FBFE"), primary, 0.14);
        var panelRaised = context.IsNightMode
            ? ColorMath.Blend(Color.Parse("#FF15202C"), secondary, 0.30)
            : ColorMath.Blend(Color.Parse("#FFFFFFFF"), secondary, 0.18);
        var overlayBase = context.IsNightMode
            ? ColorMath.Blend(Color.Parse("#FF0E1622"), primary, 0.36)
            : ColorMath.Blend(Color.Parse("#FFF3F7FD"), primary, 0.20);

        var buttonBackground = Color.FromArgb(
            context.IsNightMode ? (byte)0x4D : (byte)0x52,
            panelRaised.R,
            panelRaised.G,
            panelRaised.B);
        var buttonBorder = Color.FromArgb(
            context.IsNightMode ? (byte)0x36 : (byte)0x26,
            primary.R,
            primary.G,
            primary.B);

        resources["AdaptiveButtonBackgroundBrush"] = new SolidColorBrush(buttonBackground);
        resources["AdaptiveButtonBorderBrush"] = new SolidColorBrush(buttonBorder);
        resources["AdaptiveButtonHoverBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(ColorMath.Blend(buttonBackground, primary, 0.18), context.IsNightMode ? (byte)0x72 : (byte)0x7A));
        resources["AdaptiveButtonPressedBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(ColorMath.Blend(buttonBackground, primary, 0.30), context.IsNightMode ? (byte)0x8A : (byte)0x8C));

        resources["AdaptiveGlassPanelBackgroundBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0xF2 : (byte)0xFA, panelBase.R, panelBase.G, panelBase.B));
        resources["AdaptiveGlassPanelBorderBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0x38 : (byte)0x24, primary.R, primary.G, primary.B));
        resources["AdaptiveGlassStrongBackgroundBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0xF6 : (byte)0xFC, panelRaised.R, panelRaised.G, panelRaised.B));
        resources["AdaptiveGlassStrongBorderBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0x4A : (byte)0x2C, secondary.R, secondary.G, secondary.B));
        resources["AdaptiveGlassOverlayBackgroundBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0xEA : (byte)0xF4, overlayBase.R, overlayBase.G, overlayBase.B));

        resources["AdaptiveGlassPanelBlurRadius"] = context.IsNightMode ? 22.0 : 28.0;
        resources["AdaptiveGlassStrongBlurRadius"] = context.IsNightMode ? 28.0 : 34.0;
        resources["AdaptiveGlassOverlayBlurRadius"] = context.IsNightMode ? 34.0 : 40.0;
        resources["AdaptiveGlassPanelOpacity"] = 1.0;
        resources["AdaptiveGlassStrongOpacity"] = 1.0;
        resources["AdaptiveGlassOverlayOpacity"] = context.IsNightMode ? 0.95 : 0.98;
        resources["AdaptiveGlassNoiseOpacity"] = context.IsNightMode ? 0.012 : 0.008;
    }
}
