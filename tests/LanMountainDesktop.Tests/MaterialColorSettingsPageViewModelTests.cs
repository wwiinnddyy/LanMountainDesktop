using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;
using LanMountainDesktop.ViewModels;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class MaterialColorSettingsPageViewModelTests
{
    [Fact]
    public void Load_SelectsSavedNoneMaterialMode()
    {
        var facade = new FakeSettingsFacade(CreateThemeState(ThemeAppearanceValues.MaterialNone));
        var materialService = new FakeMaterialColorService(CreateSnapshot(ThemeAppearanceValues.MaterialNone));

        var viewModel = new MaterialColorSettingsPageViewModel(facade, materialService);

        Assert.Equal(ThemeAppearanceValues.MaterialNone, viewModel.SelectedSystemMaterialMode.Value);
        Assert.Contains(viewModel.SystemMaterialModes, option => option.Value == ThemeAppearanceValues.MaterialAuto);
        Assert.Contains(viewModel.SystemMaterialModes, option => option.Value == ThemeAppearanceValues.MaterialNone);
        Assert.Contains(viewModel.SystemMaterialModes, option => option.Value == ThemeAppearanceValues.MaterialMica);
        Assert.Contains(viewModel.SystemMaterialModes, option => option.Value == ThemeAppearanceValues.MaterialAcrylic);
        Assert.Equal(0, facade.ThemeSaveCount);
    }

    [Fact]
    public void MaterialSnapshotRefresh_KeepsExplicitNoneSelection()
    {
        var facade = new FakeSettingsFacade(CreateThemeState(ThemeAppearanceValues.MaterialNone));
        var materialService = new FakeMaterialColorService(CreateSnapshot(ThemeAppearanceValues.MaterialNone));
        var viewModel = new MaterialColorSettingsPageViewModel(facade, materialService);

        materialService.RaiseChanged(CreateSnapshot(ThemeAppearanceValues.MaterialAuto));

        Assert.Equal(ThemeAppearanceValues.MaterialNone, viewModel.SelectedSystemMaterialMode.Value);
        Assert.Equal(0, facade.ThemeSaveCount);
    }

    [Theory]
    [InlineData(ThemeAppearanceValues.MaterialNone)]
    [InlineData(ThemeAppearanceValues.MaterialAuto)]
    [InlineData(ThemeAppearanceValues.MaterialMica)]
    [InlineData(ThemeAppearanceValues.MaterialAcrylic)]
    public void UserSelection_SavesRequestedMaterialMode(string targetMode)
    {
        var initialMode = targetMode == ThemeAppearanceValues.MaterialNone
            ? ThemeAppearanceValues.MaterialAuto
            : ThemeAppearanceValues.MaterialNone;
        var facade = new FakeSettingsFacade(CreateThemeState(initialMode));
        var materialService = new FakeMaterialColorService(CreateSnapshot(initialMode));
        var viewModel = new MaterialColorSettingsPageViewModel(facade, materialService);

        viewModel.SelectedSystemMaterialMode = viewModel.SystemMaterialModes.Single(option =>
            option.Value == targetMode);

        Assert.Equal(targetMode, facade.ThemeState.SystemMaterialMode);
        Assert.Equal(1, facade.ThemeSaveCount);
    }

    [Fact]
    public void UserSelection_SystemMaterialModeRequestsRestart()
    {
        var facade = new FakeSettingsFacade(CreateThemeState(ThemeAppearanceValues.MaterialNone));
        var materialService = new FakeMaterialColorService(CreateSnapshot(ThemeAppearanceValues.MaterialNone));
        var viewModel = new MaterialColorSettingsPageViewModel(facade, materialService);
        string? restartReason = null;
        viewModel.RestartRequested += reason => restartReason = reason;

        viewModel.SelectedSystemMaterialMode = viewModel.SystemMaterialModes.Single(option =>
            option.Value == ThemeAppearanceValues.MaterialMica);

        Assert.Equal(viewModel.SystemMaterialRestartMessage, restartReason);
        Assert.False(string.IsNullOrWhiteSpace(restartReason));
    }

    private static ThemeAppearanceSettingsState CreateThemeState(string materialMode)
    {
        return new ThemeAppearanceSettingsState(
            IsNightMode: false,
            ThemeColor: "#FF445566",
            UseSystemChrome: false,
            CornerRadiusStyle: GlobalAppearanceSettings.CornerRadiusStyleRounded,
            ThemeColorMode: ThemeAppearanceValues.ColorModeDefaultNeutral,
            SystemMaterialMode: materialMode,
            SelectedWallpaperSeed: null,
            ThemeMode: ThemeAppearanceValues.ThemeModeLight,
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceAuto,
            UseNativeWallpaperChangeEvents: true);
    }

    private static MaterialColorSnapshot CreateSnapshot(string materialMode)
    {
        var seed = Color.Parse("#FF3B82F6");
        var palette = new LanMountainDesktop.Models.MaterialColorPalette(
            seed,
            Color.Parse("#FF64748B"),
            seed,
            Colors.White,
            Color.Parse("#FF60A5FA"),
            Color.Parse("#FF93C5FD"),
            Color.Parse("#FFBFDBFE"),
            Color.Parse("#FF2563EB"),
            Color.Parse("#FF1D4ED8"),
            Color.Parse("#FF1E40AF"),
            Color.Parse("#FFF8FAFC"),
            Color.Parse("#FFFFFFFF"),
            Color.Parse("#FFF1F5F9"),
            Color.Parse("#FF0F172A"),
            Color.Parse("#FF334155"),
            Color.Parse("#FF64748B"),
            seed,
            Color.Parse("#FF0F172A"),
            Colors.White,
            seed,
            Color.Parse("#22000000"),
            Color.Parse("#33000000"),
            Color.Parse("#443B82F6"),
            seed,
            Color.Parse("#4464748B"),
            Color.Parse("#663B82F6"));
        var surface = new MaterialSurfaceSnapshot(
            MaterialSurfaceRole.SettingsWindowBackground,
            Color.Parse("#FFF8FAFC"),
            Color.Parse("#22000000"),
            0,
            1);
        var surfaces = new Dictionary<MaterialSurfaceRole, MaterialSurfaceSnapshot>
        {
            [MaterialSurfaceRole.SettingsWindowBackground] = surface,
            [MaterialSurfaceRole.DockBackground] = surface with { Role = MaterialSurfaceRole.DockBackground },
            [MaterialSurfaceRole.DesktopComponentHost] = surface with { Role = MaterialSurfaceRole.DesktopComponentHost },
            [MaterialSurfaceRole.OverlayPanel] = surface with { Role = MaterialSurfaceRole.OverlayPanel }
        };

        return new MaterialColorSnapshot(
            IsNightMode: false,
            ThemeColorMode: ThemeAppearanceValues.ColorModeDefaultNeutral,
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceAuto,
            ColorSourceKind: MaterialColorSourceKind.Neutral,
            ResolvedSeedSource: "neutral",
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
            SelectedWallpaperSeed: null,
            EffectiveSeedColor: seed,
            AccentColor: seed,
            MonetPalette: new MonetPalette([seed], seed, seed, seed, seed, seed, seed),
            Palette: palette,
            WallpaperSeedCandidates: [seed],
            SystemMaterialMode: materialMode,
            AvailableSystemMaterialModes:
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            CanChangeSystemMaterial: true,
            UseSystemChrome: false,
            ResolvedWallpaperPath: null,
            UseNativeWallpaperChangeEvents: true,
            NativeWallpaperChangeEventsActive: false,
            WallpaperPollingActive: false,
            Surfaces: surfaces);
    }

    private sealed class FakeSettingsFacade(ThemeAppearanceSettingsState themeState) : ISettingsFacadeService
    {
        private readonly FakeThemeAppearanceService _theme = new(themeState);
        private readonly FakeRegionSettingsService _region = new();
        private readonly FakeWallpaperSettingsService _wallpaper = new();

        public ThemeAppearanceSettingsState ThemeState => _theme.State;
        public int ThemeSaveCount => _theme.SaveCount;

        public ISettingsService Settings => throw new NotSupportedException();
        public ISettingsCatalog Catalog => throw new NotSupportedException();
        public IGridSettingsService Grid => throw new NotSupportedException();
        public IWallpaperSettingsService Wallpaper => _wallpaper;
        public IWallpaperMediaService WallpaperMedia => throw new NotSupportedException();
        public IThemeAppearanceService Theme => _theme;
        public IStatusBarSettingsService StatusBar => throw new NotSupportedException();
        public ITextCapsuleSettingsService TextCapsule => throw new NotSupportedException();
        public IWeatherSettingsService Weather => throw new NotSupportedException();
        public IRegionSettingsService Region => _region;
        public IPrivacySettingsService Privacy => throw new NotSupportedException();
        public IUpdateSettingsService Update => throw new NotSupportedException();
        public ILauncherCatalogService LauncherCatalog => throw new NotSupportedException();
        public ILauncherPolicyService LauncherPolicy => throw new NotSupportedException();
        public IPluginManagementSettingsService PluginManagement => throw new NotSupportedException();
        public IPluginCatalogSettingsService PluginCatalog => throw new NotSupportedException();
        public IApplicationInfoService ApplicationInfo => throw new NotSupportedException();
    }

    private sealed class FakeThemeAppearanceService(ThemeAppearanceSettingsState state) : IThemeAppearanceService
    {
        public ThemeAppearanceSettingsState State { get; private set; } = state;
        public int SaveCount { get; private set; }

        public ThemeAppearanceSettingsState Get() => State;

        public void Save(ThemeAppearanceSettingsState state)
        {
            SaveCount++;
            State = state;
        }

        public MonetPalette BuildPalette(bool nightMode, string? wallpaperPath, string? preferredSeedColor = null)
        {
            var seed = Color.Parse(preferredSeedColor ?? "#FF3B82F6");
            return new MonetPalette([seed], seed, seed, seed, seed, seed, seed);
        }
    }

    private sealed class FakeRegionSettingsService : IRegionSettingsService
    {
        public RegionSettingsState Get() => new("en-US", null);

        public void Save(RegionSettingsState state)
        {
            _ = state;
        }

        public TimeZoneService GetTimeZoneService() => new();
    }

    private sealed class FakeWallpaperSettingsService : IWallpaperSettingsService
    {
        public WallpaperSettingsState Get() => new(null, "SolidColor", "#FFFFFFFF", "Fill", 300);

        public void Save(WallpaperSettingsState state)
        {
            _ = state;
        }
    }

    private sealed class FakeMaterialColorService(MaterialColorSnapshot snapshot) : IMaterialColorService
    {
        private MaterialColorSnapshot _snapshot = snapshot;

        public event EventHandler<MaterialColorSnapshot>? MaterialColorChanged;

        public MaterialColorSnapshot GetMaterialColorSnapshot() => _snapshot;

        public MaterialColorSnapshot BuildMaterialColorPreview(ThemeAppearanceSettingsState pendingState)
        {
            _ = pendingState;
            return _snapshot;
        }

        public void ApplyThemeResources(IResourceDictionary resources)
        {
            _ = resources;
        }

        public MaterialSurfaceSnapshot GetSurface(MaterialSurfaceRole role)
        {
            return _snapshot.Surfaces[role];
        }

        public void ApplyWindowMaterial(Window window, MaterialSurfaceRole role)
        {
            _ = window;
            _ = role;
        }

        public void RefreshWallpaperColors()
        {
        }

        public void RaiseChanged(MaterialColorSnapshot snapshot)
        {
            _snapshot = snapshot;
            MaterialColorChanged?.Invoke(this, snapshot);
        }
    }
}
