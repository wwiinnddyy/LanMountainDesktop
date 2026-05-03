using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

public static class GlassEffectService
{
    public static void ApplyGlassResources(IResourceDictionary resources, ThemeColorContext context)
    {
        var materialSurfaceService = new MaterialSurfaceService();
        var monetPalette = context.MonetPalette;
        var monetColors = context.MonetColors?.Where(color => color.A > 0).ToArray() ?? [];
        var primary = context.UseNeutralSurfaces
            ? context.AccentColor
            : monetPalette?.Primary ?? (monetColors.Length > 0 ? monetColors[0] : context.AccentColor);
        var neutralButtonBase = context.IsNightMode
            ? Color.Parse("#FF171C24")
            : Color.Parse("#FFFFFFFF");
        if (!context.UseNeutralSurfaces)
        {
            neutralButtonBase = ColorMath.Blend(
                neutralButtonBase,
                primary,
                context.IsNightMode ? 0.08 : 0.04);
        }

        var buttonBackground = Color.FromArgb(
            context.IsNightMode ? (byte)0xF0 : (byte)0xFF,
            neutralButtonBase.R,
            neutralButtonBase.G,
            neutralButtonBase.B);
        var buttonBorder = ColorMath.WithAlpha(
            context.IsNightMode
                ? ColorMath.Blend(neutralButtonBase, Color.Parse("#FFFFFFFF"), 0.14)
                : ColorMath.Blend(neutralButtonBase, Color.Parse("#FF334155"), 0.10),
            context.IsNightMode ? (byte)0x26 : (byte)0x14);

        resources["AdaptiveButtonBackgroundBrush"] = new SolidColorBrush(buttonBackground);
        resources["AdaptiveButtonBorderBrush"] = new SolidColorBrush(buttonBorder);
        resources["AdaptiveButtonHoverBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(
                ColorMath.Blend(buttonBackground, primary, context.IsNightMode ? 0.14 : 0.08),
                context.IsNightMode ? (byte)0xF4 : (byte)0xFF));
        resources["AdaptiveButtonPressedBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(
                ColorMath.Blend(buttonBackground, primary, context.IsNightMode ? 0.24 : 0.16),
                context.IsNightMode ? (byte)0xF8 : (byte)0xFF));

        var windowSurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.WindowBackground);
        var settingsWindowSurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.SettingsWindowBackground);
        var dockSurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.DockBackground);
        var statusBarSurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.StatusBarBackground);
        var desktopComponentSurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.DesktopComponentHost);
        var statusBarComponentSurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.StatusBarComponentHost);
        var overlaySurface = materialSurfaceService.GetSurface(context, MaterialSurfaceRole.OverlayPanel);
        var strongSurfaceColor = ColorMath.Blend(
            desktopComponentSurface.BackgroundColor,
            overlaySurface.BackgroundColor,
            context.IsNightMode ? 0.18 : 0.12);
        var strongBorderColor = ColorMath.WithAlpha(
            desktopComponentSurface.BorderColor,
            context.IsNightMode ? (byte)0x20 : (byte)0x12);
        var panelBorderColor = ColorMath.WithAlpha(
            desktopComponentSurface.BorderColor,
            context.IsNightMode ? (byte)0x18 : (byte)0x10);

        resources["AdaptiveWindowBackgroundBrush"] = new SolidColorBrush(windowSurface.BackgroundColor);
        resources["AdaptiveWindowBorderBrush"] = new SolidColorBrush(windowSurface.BorderColor);
        resources["AdaptiveSettingsWindowBackgroundBrush"] = new SolidColorBrush(settingsWindowSurface.BackgroundColor);
        // 可选：叠在内容区上的可读性 tint（半透明）；不改变 AdaptiveSettingsWindowBackgroundBrush 的语义权重，供 P1 绑定内容层。
        var settingsTintBase = settingsWindowSurface.BackgroundColor;
        var settingsTintAlpha = ResolveSettingsWindowTintAlpha(context);
        resources["AdaptiveSettingsWindowTintBrush"] = new SolidColorBrush(
            Color.FromArgb(
                settingsTintAlpha,
                settingsTintBase.R,
                settingsTintBase.G,
                settingsTintBase.B));
        resources["AdaptiveSettingsWindowBorderBrush"] = new SolidColorBrush(settingsWindowSurface.BorderColor);
        resources["AdaptiveDockBackgroundBrush"] = new SolidColorBrush(dockSurface.BackgroundColor);
        resources["AdaptiveDockBorderBrush"] = new SolidColorBrush(dockSurface.BorderColor);
        resources["AdaptiveStatusBarBackgroundBrush"] = new SolidColorBrush(statusBarSurface.BackgroundColor);
        resources["AdaptiveStatusBarBorderBrush"] = new SolidColorBrush(statusBarSurface.BorderColor);
        resources["AdaptiveDesktopComponentHostBackgroundBrush"] = new SolidColorBrush(desktopComponentSurface.BackgroundColor);
        resources["AdaptiveDesktopComponentHostBorderBrush"] = new SolidColorBrush(desktopComponentSurface.BorderColor);
        resources["AdaptiveStatusBarComponentHostBackgroundBrush"] = new SolidColorBrush(statusBarComponentSurface.BackgroundColor);
        resources["AdaptiveStatusBarComponentHostBorderBrush"] = new SolidColorBrush(statusBarComponentSurface.BorderColor);

        resources["AdaptiveGlassPanelBackgroundBrush"] = new SolidColorBrush(desktopComponentSurface.BackgroundColor);
        resources["AdaptiveGlassPanelBorderBrush"] = new SolidColorBrush(panelBorderColor);
        resources["AdaptiveGlassStrongBackgroundBrush"] = new SolidColorBrush(strongSurfaceColor);
        resources["AdaptiveGlassStrongBorderBrush"] = new SolidColorBrush(strongBorderColor);
        resources["AdaptiveDockGlassBackgroundBrush"] = new SolidColorBrush(dockSurface.BackgroundColor);
        resources["AdaptiveDockGlassBorderBrush"] = new SolidColorBrush(dockSurface.BorderColor);
        resources["AdaptiveGlassOverlayBackgroundBrush"] = new SolidColorBrush(overlaySurface.BackgroundColor);

        resources["AdaptiveGlassPanelBlurRadius"] = desktopComponentSurface.BlurRadius;
        resources["AdaptiveGlassStrongBlurRadius"] = dockSurface.BlurRadius;
        resources["AdaptiveGlassOverlayBlurRadius"] = overlaySurface.BlurRadius;
        resources["AdaptiveGlassPanelOpacity"] = 1.0;
        resources["AdaptiveGlassStrongOpacity"] = 1.0;
        resources["AdaptiveGlassOverlayOpacity"] = overlaySurface.Opacity;
        resources["AdaptiveGlassNoiseOpacity"] = context.IsNightMode ? 0.012 : 0.008;

        resources["AdaptiveDockOpacity"] = dockSurface.Opacity;
        resources["AdaptiveStatusBarOpacity"] = statusBarSurface.Opacity;
        resources["AdaptiveDesktopComponentHostOpacity"] = desktopComponentSurface.Opacity;
        resources["AdaptiveStatusBarComponentHostOpacity"] = statusBarComponentSurface.Opacity;
    }

    /// <summary>可选内容叠层 alpha，与设置窗表面色相一致；None 为 0 避免重复染色。</summary>
    private static byte ResolveSettingsWindowTintAlpha(ThemeColorContext context)
    {
        var mode = ThemeAppearanceValues.NormalizeSystemMaterialMode(context.SystemMaterialMode);
        return mode switch
        {
            ThemeAppearanceValues.MaterialAcrylic => context.IsNightMode ? (byte)0x58 : (byte)0x4C,
            ThemeAppearanceValues.MaterialMica => context.IsNightMode ? (byte)0x50 : (byte)0x44,
            _ => (byte)0x00
        };
    }
}
