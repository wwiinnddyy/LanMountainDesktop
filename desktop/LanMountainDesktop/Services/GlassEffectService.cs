using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

public static class GlassEffectService
{
    private static readonly IMaterialSurfaceService MaterialSurfaceService = new MaterialSurfaceService();

    public static void ApplyGlassResources(IResourceDictionary resources, ThemeColorContext context)
    {
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

        resources[ThemeResourceKeys.ButtonBackgroundBrush] = new SolidColorBrush(buttonBackground);
        resources[ThemeResourceKeys.ButtonBorderBrush] = new SolidColorBrush(buttonBorder);
        resources[ThemeResourceKeys.ButtonHoverBackgroundBrush] = new SolidColorBrush(
            ColorMath.WithAlpha(
                ColorMath.Blend(buttonBackground, primary, context.IsNightMode ? 0.14 : 0.08),
                context.IsNightMode ? (byte)0xF4 : (byte)0xFF));
        resources[ThemeResourceKeys.ButtonPressedBackgroundBrush] = new SolidColorBrush(
            ColorMath.WithAlpha(
                ColorMath.Blend(buttonBackground, primary, context.IsNightMode ? 0.24 : 0.16),
                context.IsNightMode ? (byte)0xF8 : (byte)0xFF));

        var windowSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.WindowBackground);
        var settingsWindowSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.SettingsWindowBackground);
        var dockSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.DockBackground);
        var statusBarSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.StatusBarBackground);
        var desktopComponentSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.DesktopComponentHost);
        var statusBarComponentSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.StatusBarComponentHost);
        var overlaySurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.OverlayPanel);
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

        resources[ThemeResourceKeys.WindowBackgroundBrush] = new SolidColorBrush(windowSurface.BackgroundColor);
        resources[ThemeResourceKeys.WindowBorderBrush] = new SolidColorBrush(windowSurface.BorderColor);
        resources[ThemeResourceKeys.SettingsWindowBackgroundBrush] = new SolidColorBrush(settingsWindowSurface.BackgroundColor);
        // 可选：叠在内容区上的可读性 tint（半透明）；不改变 AdaptiveSettingsWindowBackgroundBrush 的语义权重，供 P1 绑定内容层。
        var settingsTintBase = settingsWindowSurface.BackgroundColor;
        var settingsTintAlpha = ResolveSettingsWindowTintAlpha(context);
        resources[ThemeResourceKeys.SettingsWindowTintBrush] = new SolidColorBrush(
            Color.FromArgb(
                settingsTintAlpha,
                settingsTintBase.R,
                settingsTintBase.G,
                settingsTintBase.B));
        resources[ThemeResourceKeys.SettingsWindowBorderBrush] = new SolidColorBrush(settingsWindowSurface.BorderColor);
        resources[ThemeResourceKeys.DockBackgroundBrush] = new SolidColorBrush(dockSurface.BackgroundColor);
        resources[ThemeResourceKeys.DockBorderBrush] = new SolidColorBrush(dockSurface.BorderColor);
        resources[ThemeResourceKeys.StatusBarBackgroundBrush] = new SolidColorBrush(statusBarSurface.BackgroundColor);
        resources[ThemeResourceKeys.StatusBarBorderBrush] = new SolidColorBrush(statusBarSurface.BorderColor);
        resources[ThemeResourceKeys.DesktopComponentHostBackgroundBrush] = new SolidColorBrush(desktopComponentSurface.BackgroundColor);
        resources[ThemeResourceKeys.DesktopComponentHostBorderBrush] = new SolidColorBrush(desktopComponentSurface.BorderColor);
        resources[ThemeResourceKeys.StatusBarComponentHostBackgroundBrush] = new SolidColorBrush(statusBarComponentSurface.BackgroundColor);
        resources[ThemeResourceKeys.StatusBarComponentHostBorderBrush] = new SolidColorBrush(statusBarComponentSurface.BorderColor);

        resources[ThemeResourceKeys.GlassPanelBackgroundBrush] = new SolidColorBrush(desktopComponentSurface.BackgroundColor);
        resources[ThemeResourceKeys.GlassPanelBorderBrush] = new SolidColorBrush(panelBorderColor);
        resources[ThemeResourceKeys.GlassStrongBackgroundBrush] = new SolidColorBrush(strongSurfaceColor);
        resources[ThemeResourceKeys.GlassStrongBorderBrush] = new SolidColorBrush(strongBorderColor);
        resources[ThemeResourceKeys.DockGlassBackgroundBrush] = new SolidColorBrush(dockSurface.BackgroundColor);
        resources[ThemeResourceKeys.DockGlassBorderBrush] = new SolidColorBrush(dockSurface.BorderColor);
        resources[ThemeResourceKeys.GlassOverlayBackgroundBrush] = new SolidColorBrush(overlaySurface.BackgroundColor);

        resources[ThemeResourceKeys.GlassPanelBlurRadius] = desktopComponentSurface.BlurRadius;
        resources[ThemeResourceKeys.GlassStrongBlurRadius] = dockSurface.BlurRadius;
        resources[ThemeResourceKeys.GlassOverlayBlurRadius] = overlaySurface.BlurRadius;
        resources[ThemeResourceKeys.GlassPanelOpacity] = 1.0;
        resources[ThemeResourceKeys.GlassStrongOpacity] = 1.0;
        resources[ThemeResourceKeys.GlassOverlayOpacity] = overlaySurface.Opacity;
        resources[ThemeResourceKeys.GlassNoiseOpacity] = context.IsNightMode ? 0.012 : 0.008;

        resources[ThemeResourceKeys.DockOpacity] = dockSurface.Opacity;
        resources[ThemeResourceKeys.StatusBarOpacity] = statusBarSurface.Opacity;
        resources[ThemeResourceKeys.DesktopComponentHostOpacity] = desktopComponentSurface.Opacity;
        resources[ThemeResourceKeys.StatusBarComponentHostOpacity] = statusBarComponentSurface.Opacity;
    }

    /// <summary>可选内容叠层 alpha，与设置窗表面色相一致；None 为 0 避免重复染色。</summary>
    private static byte ResolveSettingsWindowTintAlpha(ThemeColorContext context)
    {
        var mode = ThemeAppearanceValues.ResolveEffectiveSystemMaterialMode(context.SystemMaterialMode);
        return mode switch
        {
            ThemeAppearanceValues.MaterialAcrylic => context.IsNightMode ? (byte)0x58 : (byte)0x4C,
            ThemeAppearanceValues.MaterialMica => context.IsNightMode ? (byte)0x50 : (byte)0x44,
            _ => (byte)0x00
        };
    }
}
