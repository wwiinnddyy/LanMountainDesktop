using Avalonia;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.Contracts;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class MaterialColorIntegrationTests
{
    [Fact]
    public void PluginMapper_ExposesUnifiedMaterialColorSnapshot()
    {
        var snapshot = CreateSnapshot();

        var pluginSnapshot = PluginAppearanceSnapshotMapper.FromMaterialColorSnapshot(snapshot);

        Assert.Equal("Dark", pluginSnapshot.ThemeVariant);
        Assert.Equal(Color.Parse("#FF214365").ToString(), pluginSnapshot.AccentColor);
        Assert.Equal(Color.Parse("#FF123456").ToString(), pluginSnapshot.SeedColor);
        Assert.Equal(MaterialColorSourceKind.CustomSeed.ToString(), pluginSnapshot.ColorSource);
        Assert.Equal(ThemeAppearanceValues.MaterialMica, pluginSnapshot.SystemMaterialMode);
        Assert.Equal(Color.Parse("#FF214365").ToString(), pluginSnapshot.ColorRoles?["accent"]);
        Assert.Equal(Color.Parse("#FF101820").ToString(), pluginSnapshot.MaterialSurfaces?["WindowBackground"].BackgroundColor);
        Assert.Equal(Color.Parse("#FF123456").ToString(), Assert.Single(pluginSnapshot.WallpaperSeedCandidates ?? []));
    }

    [Fact]
    public void ComponentEditorAdapter_UsesMaterialColorSnapshotAsSource()
    {
        var snapshot = CreateSnapshot();

        var palette = ComponentEditorMaterialThemeAdapter.Build(snapshot);

        Assert.Equal(snapshot.Palette.Primary, palette.PrimaryColor);
        Assert.Equal(snapshot.Palette.Secondary, palette.SecondaryColor);
        Assert.Equal(snapshot.Palette.OnAccent, palette.OnPrimaryColor);
        Assert.Equal(snapshot.Surfaces[MaterialSurfaceRole.WindowBackground].BackgroundColor, palette.WindowBackgroundColor);
        Assert.Equal(snapshot.Surfaces[MaterialSurfaceRole.OverlayPanel].BackgroundColor, palette.SurfaceContainerHighColor);
    }

    private static MaterialColorSnapshot CreateSnapshot()
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
            [MaterialSurfaceRole.DesktopComponentHost] = new(
                MaterialSurfaceRole.DesktopComponentHost,
                Color.Parse("#FF141C24"),
                Color.Parse("#44557799"),
                20,
                0.90),
            [MaterialSurfaceRole.OverlayPanel] = new(
                MaterialSurfaceRole.OverlayPanel,
                Color.Parse("#FF202A34"),
                Color.Parse("#556688AA"),
                24,
                0.88)
        };

        return new MaterialColorSnapshot(
            IsNightMode: true,
            ThemeColorMode: ThemeAppearanceValues.ColorModeSeedMonet,
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
