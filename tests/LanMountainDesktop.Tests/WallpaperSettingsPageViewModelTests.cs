using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.ViewModels;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WallpaperSettingsPageViewModelTests
{
    [Fact]
    public void CustomColorRoundTripsThroughWallpaperColorField()
    {
        var initialWallpaperState = new WallpaperSettingsState(
            WallpaperPath: null,
            Type: "SolidColor",
            Color: "#FF123456",
            Placement: "Fill",
            SystemWallpaperRefreshIntervalSeconds: 900);
        var initialThemeState = CreateThemeState();
        var facade = new FakeSettingsFacade(initialWallpaperState, initialThemeState);
        var viewModel = new WallpaperSettingsPageViewModel(facade);

        Assert.Equal("#FF123456", viewModel.SelectedColor);
        Assert.Equal(Color.Parse("#FF123456"), viewModel.CustomColor);

        viewModel.CustomColor = Color.Parse("#FFABCDEF");

        Assert.Equal("#FFABCDEF", facade.WallpaperState.Color);
        Assert.Equal(900, facade.WallpaperState.SystemWallpaperRefreshIntervalSeconds);
        Assert.Equal(0, facade.ThemeSaveCount);
        Assert.Equal(ThemeAppearanceValues.MaterialMica, facade.ThemeState.SystemMaterialMode);
        Assert.Equal("#FF998877", facade.ThemeState.SelectedWallpaperSeed);

        var reloaded = new WallpaperSettingsPageViewModel(facade);
        Assert.Equal("#FFABCDEF", reloaded.SelectedColor);
        Assert.Equal(Color.Parse("#FFABCDEF"), reloaded.CustomColor);
    }

    [Fact]
    public void SavingWallpaperChanges_DoesNotTouchThemeMaterialFields()
    {
        var initialWallpaperState = new WallpaperSettingsState(
            WallpaperPath: @"C:\\wallpaper\\forest.png",
            Type: "Image",
            Color: "#FF123456",
            Placement: "Fill",
            SystemWallpaperRefreshIntervalSeconds: 1800);
        var initialThemeState = CreateThemeState();
        var facade = new FakeSettingsFacade(initialWallpaperState, initialThemeState);
        var viewModel = new WallpaperSettingsPageViewModel(facade);

        viewModel.SelectedWallpaperPlacement = viewModel.WallpaperPlacements.Single(option => option.Value == "Tile");

        Assert.Equal(0, facade.ThemeSaveCount);
        Assert.Equal(ThemeAppearanceValues.MaterialMica, facade.ThemeState.SystemMaterialMode);
        Assert.Equal("#FF998877", facade.ThemeState.SelectedWallpaperSeed);
        Assert.Equal(1800, facade.WallpaperState.SystemWallpaperRefreshIntervalSeconds);
    }

    private static ThemeAppearanceSettingsState CreateThemeState()
    {
        return new ThemeAppearanceSettingsState(
            IsNightMode: false,
            ThemeColor: "#FF445566",
            UseSystemChrome: true,
            CornerRadiusStyle: GlobalAppearanceSettings.CornerRadiusStyleRounded,
            ThemeColorMode: ThemeAppearanceValues.ColorModeWallpaperMonet,
            SystemMaterialMode: ThemeAppearanceValues.MaterialMica,
            SelectedWallpaperSeed: "#FF998877",
            ThemeMode: ThemeAppearanceValues.ThemeModeLight,
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceAuto,
            UseNativeWallpaperChangeEvents: true);
    }

    private sealed class FakeSettingsFacade(
        WallpaperSettingsState wallpaperState,
        ThemeAppearanceSettingsState themeState) : ISettingsFacadeService
    {
        private readonly FakeWallpaperSettingsService _wallpaper = new(wallpaperState);
        private readonly FakeThemeAppearanceService _theme = new(themeState);
        private readonly FakeRegionSettingsService _region = new();

        public WallpaperSettingsState WallpaperState => _wallpaper.State;
        public ThemeAppearanceSettingsState ThemeState => _theme.State;
        public int ThemeSaveCount => _theme.SaveCount;

        public ISettingsService Settings => throw new NotSupportedException();
        public ISettingsCatalog Catalog => throw new NotSupportedException();
        public IGridSettingsService Grid => throw new NotSupportedException();
        public IWallpaperSettingsService Wallpaper => _wallpaper;
        public IWallpaperMediaService WallpaperMedia => new FakeWallpaperMediaService();
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

    private sealed class FakeWallpaperSettingsService(WallpaperSettingsState state) : IWallpaperSettingsService
    {
        public WallpaperSettingsState State { get; private set; } = state;

        public WallpaperSettingsState Get() => State;

        public void Save(WallpaperSettingsState state)
        {
            State = state;
        }
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

    private sealed class FakeWallpaperMediaService : IWallpaperMediaService
    {
        public WallpaperMediaType DetectMediaType(string? path) => WallpaperMediaType.None;

        public Task<string?> ImportAssetAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            _ = sourcePath;
            _ = cancellationToken;
            return Task.FromResult<string?>(null);
        }
    }
}
