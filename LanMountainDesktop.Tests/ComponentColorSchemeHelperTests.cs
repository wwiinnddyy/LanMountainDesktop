using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.Contracts;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentColorSchemeHelperTests
{
    [Fact]
    public void GetCurrentGlobalThemeColorMode_UsesMaterialColorSnapshot()
    {
        var snapshot = CreateSnapshot(ThemeAppearanceValues.ColorModeWallpaperMonet);

        var mode = ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode(snapshot);

        Assert.Equal(ThemeAppearanceValues.ColorModeWallpaperMonet, mode);
    }

    [Theory]
    [InlineData(ThemeAppearanceValues.ColorSchemeNative, ThemeAppearanceValues.ColorModeWallpaperMonet, false)]
    [InlineData(ThemeAppearanceValues.ColorSchemeFollowSystem, ThemeAppearanceValues.ColorModeDefaultNeutral, true)]
    [InlineData(null, ThemeAppearanceValues.ColorModeDefaultNeutral, false)]
    public void ShouldUseMonetColor_FollowsExpectedRules(
        string? componentColorScheme,
        string globalThemeColorMode,
        bool expected)
    {
        var shouldUseMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(componentColorScheme, globalThemeColorMode);

        Assert.Equal(expected, shouldUseMonetColor);
    }

    private static MaterialColorSnapshot CreateSnapshot(string themeColorMode)
    {
        var seed = Color.Parse("#FF123456");
        var accent = Color.Parse("#FF214365");
        var palette = new MaterialColorPalette(
            Color.Parse("#FF315577"),
            Color.Parse("#FF557799"),
            accent,
            Color.Parse("#FFFFFFFF"),
            Color.Parse("#FF5F7F9F"),
            Color.Parse("#FF7F9FBF"),
            Color.Parse("#FF9FBFDF"),
            Color.Parse("#FF17314B"),
            Color.Parse("#FF102840"),
            Color.Parse("#FF082038"),
            Color.Parse("#FF0B1118"),
            Color.Parse("#FF141C24"),
            Color.Parse("#FF1C2630"),
            Color.Parse("#FFF5F7FA"),
            Color.Parse("#FFC8D0DA"),
            Color.Parse("#FF9EA8B4"),
            Color.Parse("#FF91B8E8"),
            Color.Parse("#FFF5F7FA"),
            Color.Parse("#FFFFFFFF"),
            Color.Parse("#FF9FBFDF"),
            Color.Parse("#33141C24"),
            Color.Parse("#441C2630"),
            Color.Parse("#55315577"),
            Color.Parse("#FF315577"),
            Color.Parse("#88557799"),
            Color.Parse("#667F9FBF"));
        var monetPalette = new MonetPalette(
            [seed],
            seed,
            palette.Primary,
            palette.Secondary,
            Color.Parse("#FF775577"),
            Color.Parse("#FF202830"),
            Color.Parse("#FF26313B"));
        var surfaces = new Dictionary<MaterialSurfaceRole, MaterialSurfaceSnapshot>
        {
            [MaterialSurfaceRole.WindowBackground] = new(
                MaterialSurfaceRole.WindowBackground,
                Color.Parse("#FF101820"),
                Color.Parse("#33557799"),
                18,
                0.92),
            [MaterialSurfaceRole.OverlayPanel] = new(
                MaterialSurfaceRole.OverlayPanel,
                Color.Parse("#FF202A34"),
                Color.Parse("#556688AA"),
                24,
                0.88)
        };

        return new MaterialColorSnapshot(
            IsNightMode: true,
            ThemeColorMode: themeColorMode,
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceAuto,
            ColorSourceKind: MaterialColorSourceKind.CustomSeed,
            ResolvedSeedSource: "user_color",
            CornerRadiusTokens: new AppearanceCornerRadiusTokens(
                new CornerRadius(2),
                new CornerRadius(4),
                new CornerRadius(6),
                new CornerRadius(8),
                new CornerRadius(10),
                new CornerRadius(12),
                new CornerRadius(14),
                new CornerRadius(8)),
            UserThemeColor: seed.ToString(),
            SelectedWallpaperSeed: seed.ToString(),
            EffectiveSeedColor: seed,
            AccentColor: accent,
            MonetPalette: monetPalette,
            Palette: palette,
            WallpaperSeedCandidates: [seed],
            SystemMaterialMode: ThemeAppearanceValues.MaterialMica,
            AvailableSystemMaterialModes: [ThemeAppearanceValues.MaterialAuto, ThemeAppearanceValues.MaterialMica],
            CanChangeSystemMaterial: true,
            UseSystemChrome: false,
            ResolvedWallpaperPath: @"C:\wallpaper.png",
            UseNativeWallpaperChangeEvents: true,
            NativeWallpaperChangeEventsActive: true,
            WallpaperPollingActive: true,
            Surfaces: surfaces);
    }
}
