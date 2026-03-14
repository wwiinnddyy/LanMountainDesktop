using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;
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
    private static readonly Color DefaultPrimary = Color.Parse("#FF6750A4");
    private static readonly Color DarkBackgroundBase = Color.Parse("#FF0B0F14");
    private static readonly Color DarkSurfaceBase = Color.Parse("#FF10161D");
    private static readonly Color DarkSurfaceContainerBase = Color.Parse("#FF151C24");
    private static readonly Color DarkSurfaceContainerHighBase = Color.Parse("#FF1A232D");
    private static readonly Color LightBackgroundBase = Color.Parse("#FFFCFCFF");
    private static readonly Color LightSurfaceBase = Color.Parse("#FFFFFFFF");
    private static readonly Color LightSurfaceContainerBase = Color.Parse("#FFF6F8FD");
    private static readonly Color LightSurfaceContainerHighBase = Color.Parse("#FFF0F4FA");
    private static readonly Color LightOnSurfaceBase = Color.Parse("#FF101316");
    private static readonly Color DarkOnSurfaceBase = Color.Parse("#FFF6F8FC");

    public static ComponentEditorThemePalette Build(
        ThemeAppearanceSettingsState themeState,
        WallpaperSettingsState wallpaperState,
        MonetPalette monetPalette,
        WallpaperMediaType wallpaperMediaType)
    {
        ArgumentNullException.ThrowIfNull(monetPalette);

        var isNightMode = themeState.IsNightMode;
        var monetColors = monetPalette.MonetColors?.Where(color => color.A > 0).ToArray() ?? [];
        var fallbackThemeColor = TryParseColor(themeState.ThemeColor);
        var useWallpaperPalette = wallpaperMediaType == WallpaperMediaType.Image && monetColors.Length > 0;

        var primary = useWallpaperPalette
            ? monetColors[0]
            : fallbackThemeColor ?? monetColors.FirstOrDefault(DefaultPrimary);
        var secondary = ResolveSecondaryColor(primary, monetColors, isNightMode);
        var tertiary = ResolveTertiaryColor(primary, secondary, monetColors, isNightMode);

        var backgroundBase = isNightMode ? DarkBackgroundBase : LightBackgroundBase;
        var surfaceBase = isNightMode ? DarkSurfaceBase : LightSurfaceBase;
        var surfaceContainerBase = isNightMode ? DarkSurfaceContainerBase : LightSurfaceContainerBase;
        var surfaceContainerHighBase = isNightMode ? DarkSurfaceContainerHighBase : LightSurfaceContainerHighBase;

        var background = ColorMath.Blend(backgroundBase, primary, isNightMode ? 0.10 : 0.025);
        var surface = ColorMath.Blend(surfaceBase, primary, isNightMode ? 0.12 : 0.035);
        var surfaceContainer = ColorMath.Blend(surfaceContainerBase, primary, isNightMode ? 0.18 : 0.065);
        var surfaceContainerHigh = ColorMath.Blend(surfaceContainerHighBase, primary, isNightMode ? 0.24 : 0.09);
        var topAppBar = ColorMath.Blend(surfaceContainerHigh, primary, isNightMode ? 0.10 : 0.06);

        var onSurfaceBase = isNightMode ? DarkOnSurfaceBase : LightOnSurfaceBase;
        var onSurface = ColorMath.EnsureContrast(onSurfaceBase, background, 7.0);
        var onSurfaceVariantBase = ColorMath.Blend(
            onSurface,
            surfaceContainer,
            isNightMode ? 0.30 : 0.42);
        var onSurfaceVariant = ColorMath.EnsureContrast(onSurfaceVariantBase, surfaceContainer, 4.5);
        var outlineBase = ColorMath.Blend(onSurface, surfaceContainer, isNightMode ? 0.74 : 0.82);
        var outline = Color.FromArgb(
            isNightMode ? (byte)0x66 : (byte)0x42,
            outlineBase.R,
            outlineBase.G,
            outlineBase.B);
        var divider = Color.FromArgb(
            isNightMode ? (byte)0x52 : (byte)0x26,
            outlineBase.R,
            outlineBase.G,
            outlineBase.B);
        var headerIconBackground = Color.FromArgb(
            isNightMode ? (byte)0x36 : (byte)0x1F,
            primary.R,
            primary.G,
            primary.B);
        var titleBarButtonHover = Color.FromArgb(
            isNightMode ? (byte)0x24 : (byte)0x12,
            onSurface.R,
            onSurface.G,
            onSurface.B);
        var onPrimaryBase = isNightMode ? Color.Parse("#FF111318") : Color.Parse("#FFFFFFFF");
        var onPrimary = ColorMath.EnsureContrast(onPrimaryBase, primary, 4.5);

        return new ComponentEditorThemePalette(
            isNightMode,
            primary,
            secondary,
            tertiary,
            background,
            surface,
            surfaceContainer,
            surfaceContainerHigh,
            topAppBar,
            headerIconBackground,
            titleBarButtonHover,
            outline,
            divider,
            onSurface,
            onSurfaceVariant,
            onPrimary);
    }

    private static Color ResolveSecondaryColor(Color primary, IReadOnlyList<Color> monetColors, bool isNightMode)
    {
        if (monetColors.Count > 1)
        {
            return monetColors[1];
        }

        return ColorMath.Blend(
            primary,
            isNightMode ? Color.Parse("#FFFFFFFF") : Color.Parse("#FF1F1B24"),
            isNightMode ? 0.18 : 0.16);
    }

    private static Color ResolveTertiaryColor(
        Color primary,
        Color secondary,
        IReadOnlyList<Color> monetColors,
        bool isNightMode)
    {
        if (monetColors.Count > 2)
        {
            return monetColors[2];
        }

        var blendTarget = isNightMode ? Color.Parse("#FFFFFFFF") : Color.Parse("#FF2A2230");
        return ColorMath.Blend(ColorMath.Blend(primary, secondary, 0.5), blendTarget, isNightMode ? 0.12 : 0.14);
    }

    private static Color? TryParseColor(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out var parsed)
            ? parsed
            : null;
    }
}
