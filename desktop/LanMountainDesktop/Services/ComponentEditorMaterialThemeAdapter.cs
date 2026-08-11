using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

internal sealed record ComponentEditorThemePalette(
    bool IsNightMode,
    Color PrimaryColor,
    Color SecondaryColor,
    Color TertiaryColor,
    Color WindowBackgroundColor,
    Color SurfaceColor,
    Color SurfaceContainerColor,
    Color SurfaceContainerHighColor,
    Color TopAppBarColor,
    Color HeaderIconBackgroundColor,
    Color TitleBarButtonHoverColor,
    Color OutlineColor,
    Color DividerColor,
    Color OnSurfaceColor,
    Color OnSurfaceVariantColor,
    Color OnPrimaryColor);

internal static class ComponentEditorMaterialThemeAdapter
{
    private static readonly Color FallbackPrimary = Color.Parse("#FF6750A4");

    public static ComponentEditorThemePalette Build(MaterialColorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var palette = snapshot.Palette;
        var isNightMode = snapshot.IsNightMode;
        var primary = FirstUsable(palette.Primary, palette.Accent, snapshot.AccentColor, FallbackPrimary);
        var secondary = FirstUsable(
            palette.Secondary,
            snapshot.MonetPalette.Secondary,
            ColorMath.Blend(primary, isNightMode ? Colors.White : Color.Parse("#FF1F1B24"), isNightMode ? 0.18 : 0.16));
        var tertiary = FirstUsable(
            snapshot.MonetPalette.Tertiary,
            ColorMath.Blend(ColorMath.Blend(primary, secondary, 0.5), isNightMode ? Colors.White : Color.Parse("#FF2A2230"), isNightMode ? 0.12 : 0.14));

        var windowBackground = GetSurfaceColor(snapshot, MaterialSurfaceRole.WindowBackground, palette.SurfaceBase);
        var surface = FirstUsable(palette.SurfaceRaised, GetSurfaceColor(snapshot, MaterialSurfaceRole.SettingsWindowBackground, palette.SurfaceBase));
        var surfaceContainer = FirstUsable(palette.SurfaceOverlay, GetSurfaceColor(snapshot, MaterialSurfaceRole.DesktopComponentHost, surface));
        var surfaceContainerHigh = GetSurfaceColor(snapshot, MaterialSurfaceRole.OverlayPanel, surfaceContainer);
        var topAppBar = ColorMath.Blend(surfaceContainerHigh, primary, isNightMode ? 0.10 : 0.06);

        var textPrimary = FirstUsable(palette.TextPrimary, isNightMode ? Colors.White : Color.Parse("#FF101316"));
        var textSecondary = FirstUsable(palette.TextSecondary, palette.TextMuted, ColorMath.Blend(textPrimary, surfaceContainer, isNightMode ? 0.30 : 0.42));
        var outline = FirstUsable(
            GetSurfaceBorder(snapshot, MaterialSurfaceRole.DesktopComponentHost),
            palette.ToggleBorder,
            ColorMath.WithAlpha(ColorMath.Blend(textPrimary, surfaceContainer, isNightMode ? 0.74 : 0.82), isNightMode ? (byte)0x66 : (byte)0x42));
        var divider = ColorMath.WithAlpha(outline, isNightMode ? (byte)0x52 : (byte)0x26);
        var headerIconBackground = Color.FromArgb(isNightMode ? (byte)0x36 : (byte)0x1F, primary.R, primary.G, primary.B);
        var titleBarButtonHover = Color.FromArgb(isNightMode ? (byte)0x24 : (byte)0x12, textPrimary.R, textPrimary.G, textPrimary.B);
        var onPrimary = FirstUsable(palette.OnAccent, ColorMath.EnsureContrast(Colors.White, primary, 4.5));

        return new ComponentEditorThemePalette(
            isNightMode,
            primary,
            secondary,
            tertiary,
            windowBackground,
            surface,
            surfaceContainer,
            surfaceContainerHigh,
            topAppBar,
            headerIconBackground,
            titleBarButtonHover,
            outline,
            divider,
            textPrimary,
            textSecondary,
            onPrimary);
    }

    private static Color GetSurfaceColor(MaterialColorSnapshot snapshot, MaterialSurfaceRole role, Color fallback)
    {
        return snapshot.Surfaces.TryGetValue(role, out var surface) && surface.BackgroundColor.A > 0
            ? surface.BackgroundColor
            : fallback;
    }

    private static Color GetSurfaceBorder(MaterialColorSnapshot snapshot, MaterialSurfaceRole role)
    {
        return snapshot.Surfaces.TryGetValue(role, out var surface)
            ? surface.BorderColor
            : default;
    }

    private static Color FirstUsable(params Color[] colors)
    {
        foreach (var color in colors)
        {
            if (color.A > 0)
            {
                return color;
            }
        }

        return default;
    }
}
