using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

public static class GlassEffectService
{
    private static readonly IMaterialSurfaceService MaterialSurfaceService = new MaterialSurfaceService();

    /// <summary>
    /// 只发布「真的有消费者」的表面键。
    /// 2026-10-25 之前这里还写 7 个窗口/状态栏/组件宿主底色、4 个 opacity、3 个模糊半径和 1 个噪点，
    /// 共 28 个键——全仓零消费。它们每次主题刷新都要做混色 + 分配画刷，纯属开销。
    /// 真正的模糊由窗口级 Mica/Acrylic 承担，元素级模糊只有音乐组件一处显式使用。
    /// </summary>
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

        var settingsWindowSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.SettingsWindowBackground);
        var dockSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.DockBackground);
        var desktopComponentSurface = MaterialSurfaceService.GetSurface(context, MaterialSurfaceRole.DesktopComponentHost);
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

        resources[ThemeResourceKeys.SettingsWindowBackgroundBrush] = new SolidColorBrush(settingsWindowSurface.BackgroundColor);
        resources[ThemeResourceKeys.SettingsWindowTintBrush] = new SolidColorBrush(
            Color.FromArgb(
                ResolveSettingsWindowTintAlpha(context),
                settingsWindowSurface.BackgroundColor.R,
                settingsWindowSurface.BackgroundColor.G,
                settingsWindowSurface.BackgroundColor.B));

        resources[ThemeResourceKeys.GlassPanelBackgroundBrush] = new SolidColorBrush(desktopComponentSurface.BackgroundColor);
        resources[ThemeResourceKeys.GlassPanelBorderBrush] = new SolidColorBrush(panelBorderColor);
        resources[ThemeResourceKeys.GlassStrongBackgroundBrush] = new SolidColorBrush(strongSurfaceColor);
        resources[ThemeResourceKeys.GlassStrongBorderBrush] = new SolidColorBrush(strongBorderColor);
        resources[ThemeResourceKeys.DockGlassBackgroundBrush] = new SolidColorBrush(dockSurface.BackgroundColor);
        resources[ThemeResourceKeys.DockGlassBorderBrush] = new SolidColorBrush(dockSurface.BorderColor);
        resources[ThemeResourceKeys.GlassOverlayBackgroundBrush] = new SolidColorBrush(overlaySurface.BackgroundColor);

        resources[ThemeResourceKeys.GlassPanelOpacity] = 1.0;
        resources[ThemeResourceKeys.GlassStrongOpacity] = 1.0;
        resources[ThemeResourceKeys.GlassOverlayOpacity] = overlaySurface.Opacity;
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
