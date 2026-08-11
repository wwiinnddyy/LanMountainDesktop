using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Models;
using LanMountainDesktop.Plugins;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;
using Xunit;
using PluginIsolationAppearanceChangedNotification = LanMountainDesktop.PluginIsolation.Contracts.PluginAppearanceChangedNotification;
using PluginIsolationAppearanceSnapshot = LanMountainDesktop.PluginIsolation.Contracts.PluginAppearanceSnapshot;
using PluginIsolationAppearanceSnapshotRequest = LanMountainDesktop.PluginIsolation.Contracts.PluginAppearanceSnapshotRequest;
using PluginIsolationMaterialSurfaceSnapshot = LanMountainDesktop.PluginIsolation.Contracts.PluginMaterialSurfaceSnapshot;
using PluginIsolationJsonContext = LanMountainDesktop.PluginIsolation.Contracts.PluginIsolationJsonContext;
using MaterialColorPalette = LanMountainDesktop.Models.MaterialColorPalette;
using PluginSdkAppearanceSnapshot = LanMountainDesktop.PluginSdk.PluginAppearanceSnapshot;

namespace LanMountainDesktop.Tests;

public sealed class PluginAppearanceBoundaryTests
{
    [Fact]
    public void PluginLoader_PrefersMaterialColorService_WhenBothAppearanceSourcesAreAvailable()
    {
        var materialService = new TrackingMaterialColorService(CreateMaterialSnapshot());
        var themeService = new TrackingAppearanceThemeService(CreateThemeSnapshot());
        var provider = new TrackingServiceProvider(materialService, themeService);

        var snapshot = InvokeLoaderSnapshotBuilder(provider);

        Assert.Equal(1, materialService.GetMaterialColorSnapshotCalls);
        Assert.Equal(0, themeService.GetCurrentCalls);
        Assert.Equal("Dark", snapshot.ThemeVariant);
        Assert.Equal(Color.Parse("#FF214365").ToString(), snapshot.AccentColor);
        Assert.Equal(Color.Parse("#FF123456").ToString(), snapshot.SeedColor);
        Assert.Equal(MaterialColorSourceKind.CustomSeed.ToString(), snapshot.ColorSource);
        Assert.Equal(ThemeAppearanceValues.MaterialMica, snapshot.SystemMaterialMode);
        Assert.Equal(Color.Parse("#FF214365").ToString(), snapshot.ColorRoles?["accent"]);
    }

    [Fact]
    public void PluginIsolationJsonContext_RoundTripsAppearancePayloads()
    {
        var request = new PluginIsolationAppearanceSnapshotRequest("session-42");
        var snapshot = new PluginIsolationAppearanceSnapshot(
            ThemeVariant: "Dark",
            AccentColor: "#FF214365",
            CornerRadiusScale: 1.25,
            CornerRadiusTokens: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["component"] = 24,
                ["sm"] = 8
            },
            ResourceAliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["surface-base"] = "DesignSurfaceBase"
            },
            SeedColor: "#FF123456",
            ColorSource: "custom_seed",
            SystemMaterialMode: ThemeAppearanceValues.MaterialMica,
            ColorRoles: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accent"] = "#FF214365",
                ["primary"] = "#FF315577"
            },
            MaterialSurfaces: new Dictionary<string, PluginIsolationMaterialSurfaceSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["WindowBackground"] = new PluginIsolationMaterialSurfaceSnapshot("#FF101820", "#33557799", 18, 0.92)
            },
            WallpaperSeedCandidates: ["#FF123456", "#FF214365"]);
        var notification = new PluginIsolationAppearanceChangedNotification(snapshot);

        var requestJson = JsonSerializer.Serialize(request, PluginIsolationJsonContext.Default.PluginAppearanceSnapshotRequest);
        var requestRoundTrip = JsonSerializer.Deserialize(requestJson, PluginIsolationJsonContext.Default.PluginAppearanceSnapshotRequest);

        var snapshotJson = JsonSerializer.Serialize(snapshot, PluginIsolationJsonContext.Default.PluginAppearanceSnapshot);
        var snapshotRoundTrip = JsonSerializer.Deserialize(snapshotJson, PluginIsolationJsonContext.Default.PluginAppearanceSnapshot);

        var notificationJson = JsonSerializer.Serialize(notification, PluginIsolationJsonContext.Default.PluginAppearanceChangedNotification);
        var notificationRoundTrip = JsonSerializer.Deserialize(notificationJson, PluginIsolationJsonContext.Default.PluginAppearanceChangedNotification);

        Assert.Equal(request, requestRoundTrip);
        AssertAppearanceSnapshotEqual(snapshot, snapshotRoundTrip!);
        AssertAppearanceSnapshotEqual(snapshot, notificationRoundTrip!.Snapshot);
    }

    private static PluginSdkAppearanceSnapshot InvokeLoaderSnapshotBuilder(IServiceProvider provider)
    {
        var method = typeof(PluginLoader).GetMethod(
            "BuildAppearanceSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [provider]);
        return Assert.IsType<PluginSdkAppearanceSnapshot>(result);
    }

    private static void AssertAppearanceSnapshotEqual(PluginIsolationAppearanceSnapshot expected, PluginIsolationAppearanceSnapshot actual)
    {
        Assert.Equal(expected.ThemeVariant, actual.ThemeVariant);
        Assert.Equal(expected.AccentColor, actual.AccentColor);
        Assert.Equal(expected.CornerRadiusScale, actual.CornerRadiusScale);
        Assert.Equal(expected.SeedColor, actual.SeedColor);
        Assert.Equal(expected.ColorSource, actual.ColorSource);
        Assert.Equal(expected.SystemMaterialMode, actual.SystemMaterialMode);
        Assert.Equal(expected.WallpaperSeedCandidates, actual.WallpaperSeedCandidates);
        AssertDictionaryEqual(expected.CornerRadiusTokens, actual.CornerRadiusTokens);
        AssertDictionaryEqual(expected.ResourceAliases, actual.ResourceAliases);
        AssertDictionaryEqual(expected.ColorRoles, actual.ColorRoles);

        Assert.NotNull(actual.MaterialSurfaces);
        Assert.Equal(expected.MaterialSurfaces!.Count, actual.MaterialSurfaces!.Count);
        foreach (var pair in expected.MaterialSurfaces)
        {
            Assert.True(actual.MaterialSurfaces.TryGetValue(pair.Key, out var actualSurface));
            Assert.Equal(pair.Value.BackgroundColor, actualSurface.BackgroundColor);
            Assert.Equal(pair.Value.BorderColor, actualSurface.BorderColor);
            Assert.Equal(pair.Value.BlurRadius, actualSurface.BlurRadius);
            Assert.Equal(pair.Value.Opacity, actualSurface.Opacity);
        }
    }

    private static void AssertDictionaryEqual<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? expected,
        IReadOnlyDictionary<TKey, TValue>? actual)
        where TKey : notnull
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.Count, actual!.Count);
        foreach (var pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out var actualValue));
            Assert.Equal(pair.Value, actualValue);
        }
    }

    private static MaterialColorSnapshot CreateMaterialSnapshot()
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

    private static AppearanceThemeSnapshot CreateThemeSnapshot()
    {
        var seed = Color.Parse("#FF456789");
        var monetPalette = new MonetPalette(
            [seed],
            seed,
            Color.Parse("#FF667788"),
            Color.Parse("#FF8899AA"),
            Color.Parse("#FF112233"),
            Color.Parse("#FF334455"),
            Color.Parse("#FF556677"));

        return new AppearanceThemeSnapshot(
            IsNightMode: false,
            ThemeColorMode: ThemeAppearanceValues.ColorModeWallpaperMonet,
            UserThemeColor: "#FF456789",
            SelectedWallpaperSeed: "#FF456789",
            CornerRadiusStyle: GlobalAppearanceSettings.CornerRadiusStyleRounded,
            CornerRadiusTokens: new AppearanceCornerRadiusTokens(
                new CornerRadius(1),
                new CornerRadius(2),
                new CornerRadius(3),
                new CornerRadius(4),
                new CornerRadius(5),
                new CornerRadius(6),
                new CornerRadius(7),
                new CornerRadius(8)),
            ResolvedSeedSource: "theme-source",
            MonetPalette: monetPalette,
            AccentColor: Color.Parse("#FF556677"),
            EffectiveSeedColor: seed,
            WallpaperSeedCandidates: [seed],
            SystemMaterialMode: ThemeAppearanceValues.MaterialAcrylic,
            AvailableSystemMaterialModes: [ThemeAppearanceValues.MaterialAuto, ThemeAppearanceValues.MaterialAcrylic],
            CanChangeSystemMaterial: true,
            UseSystemChrome: true,
            ResolvedWallpaperPath: @"C:\theme-wallpaper.png",
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceSystem,
            UseNativeWallpaperChangeEvents: false);
    }

    private sealed class TrackingServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public TrackingServiceProvider(IMaterialColorService materialColorService, IAppearanceThemeService appearanceThemeService)
        {
            _services[typeof(IMaterialColorService)] = materialColorService;
            _services[typeof(IAppearanceThemeService)] = appearanceThemeService;
        }

        public object? GetService(Type serviceType)
        {
            return _services.TryGetValue(serviceType, out var service) ? service : null;
        }
    }

    private sealed class TrackingMaterialColorService(MaterialColorSnapshot snapshot) : IMaterialColorService
    {
        public int GetMaterialColorSnapshotCalls { get; private set; }

        public MaterialColorSnapshot GetMaterialColorSnapshot()
        {
            GetMaterialColorSnapshotCalls++;
            return snapshot;
        }

        public MaterialColorSnapshot BuildMaterialColorPreview(ThemeAppearanceSettingsState pendingState)
        {
            throw new NotSupportedException();
        }

        public event EventHandler<MaterialColorSnapshot>? MaterialColorChanged
        {
            add { }
            remove { }
        }

        public void ApplyThemeResources(IResourceDictionary resources)
        {
            throw new NotSupportedException();
        }

        public MaterialSurfaceSnapshot GetSurface(MaterialSurfaceRole role)
        {
            throw new NotSupportedException();
        }

        public void ApplyWindowMaterial(Window window, MaterialSurfaceRole role)
        {
            throw new NotSupportedException();
        }

        public void RefreshWallpaperColors()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TrackingAppearanceThemeService(AppearanceThemeSnapshot snapshot) : IAppearanceThemeService
    {
        public int GetCurrentCalls { get; private set; }

        public AppearanceThemeSnapshot GetCurrent()
        {
            GetCurrentCalls++;
            return snapshot;
        }

        public AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState)
        {
            throw new NotSupportedException();
        }

        public event EventHandler<AppearanceThemeSnapshot>? Changed
        {
            add { }
            remove { }
        }

        public void ApplyThemeResources(IResourceDictionary resources)
        {
            throw new NotSupportedException();
        }

        public AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role)
        {
            throw new NotSupportedException();
        }

        public void ApplyWindowMaterial(Window window, MaterialSurfaceRole role)
        {
            throw new NotSupportedException();
        }
    }
}
