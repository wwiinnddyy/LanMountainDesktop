using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.ViewModels;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AppearanceSettingsPageViewModelTests
{
    [Fact]
    public void ChangingThemeMode_PreservesMaterialColorSettings()
    {
        var initialState = new ThemeAppearanceSettingsState(
            IsNightMode: false,
            ThemeColor: "#ff123456",
            UseSystemChrome: false,
            CornerRadiusStyle: GlobalAppearanceSettings.CornerRadiusStyleRounded,
            ThemeColorMode: ThemeAppearanceValues.ColorModeWallpaperMonet,
            SystemMaterialMode: ThemeAppearanceValues.MaterialMica,
            SelectedWallpaperSeed: "#ff654321",
            ThemeMode: ThemeAppearanceValues.ThemeModeLight,
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceSystem,
            UseNativeWallpaperChangeEvents: false);
        var facade = new FakeSettingsFacade(initialState);
        var viewModel = new AppearanceSettingsPageViewModel(facade);

        viewModel.SelectedThemeMode = viewModel.ThemeModeOptions.Single(option =>
            option.Value == ThemeAppearanceValues.ThemeModeDark);

        var saved = facade.ThemeState;
        Assert.True(saved.IsNightMode);
        Assert.Equal(ThemeAppearanceValues.ThemeModeDark, saved.ThemeMode);
        Assert.Equal("#ff123456", saved.ThemeColor);
        Assert.Equal(ThemeAppearanceValues.ColorModeWallpaperMonet, saved.ThemeColorMode);
        Assert.Equal(ThemeAppearanceValues.MaterialMica, saved.SystemMaterialMode);
        Assert.Equal("#ff654321", saved.SelectedWallpaperSeed);
        Assert.Equal(ThemeAppearanceValues.WallpaperColorSourceSystem, saved.ThemeWallpaperColorSource);
        Assert.False(saved.UseNativeWallpaperChangeEvents);
    }

    [Fact]
    public void ChangingComponentCornerRadius_PreservesMaterialColorSettings()
    {
        var initialState = new ThemeAppearanceSettingsState(
            IsNightMode: true,
            ThemeColor: "#ffabcdef",
            UseSystemChrome: true,
            CornerRadiusStyle: GlobalAppearanceSettings.CornerRadiusStyleBalanced,
            ThemeColorMode: ThemeAppearanceValues.ColorModeWallpaperMonet,
            SystemMaterialMode: ThemeAppearanceValues.MaterialAcrylic,
            SelectedWallpaperSeed: "#ff111111",
            ThemeMode: ThemeAppearanceValues.ThemeModeDark,
            ThemeWallpaperColorSource: ThemeAppearanceValues.WallpaperColorSourceApp,
            UseNativeWallpaperChangeEvents: false);
        var facade = new FakeSettingsFacade(initialState);
        var viewModel = new ComponentsSettingsPageViewModel(facade);

        viewModel.SelectedCornerRadiusStyle = viewModel.CornerRadiusStyleOptions.Single(option =>
            option.Value == GlobalAppearanceSettings.CornerRadiusStyleOpen);

        var saved = facade.ThemeState;
        Assert.Equal(GlobalAppearanceSettings.CornerRadiusStyleOpen, saved.CornerRadiusStyle);
        Assert.True(saved.IsNightMode);
        Assert.Equal("#ffabcdef", saved.ThemeColor);
        Assert.True(saved.UseSystemChrome);
        Assert.Equal(ThemeAppearanceValues.ColorModeWallpaperMonet, saved.ThemeColorMode);
        Assert.Equal(ThemeAppearanceValues.MaterialAcrylic, saved.SystemMaterialMode);
        Assert.Equal("#ff111111", saved.SelectedWallpaperSeed);
        Assert.Equal(ThemeAppearanceValues.ThemeModeDark, saved.ThemeMode);
        Assert.Equal(ThemeAppearanceValues.WallpaperColorSourceApp, saved.ThemeWallpaperColorSource);
        Assert.False(saved.UseNativeWallpaperChangeEvents);
    }

    private sealed class FakeSettingsFacade(ThemeAppearanceSettingsState themeState) : ISettingsFacadeService
    {
        private readonly FakeThemeAppearanceService _theme = new(themeState);
        private readonly FakeRegionSettingsService _region = new();
        private readonly FakeGridSettingsService _grid = new();

        public ThemeAppearanceSettingsState ThemeState => _theme.State;

        public IThemeAppearanceService Theme => _theme;

        public IRegionSettingsService Region => _region;

        public ISettingsService Settings => throw new NotSupportedException();
        public ISettingsCatalog Catalog => throw new NotSupportedException();
        public IGridSettingsService Grid => _grid;
        public IWallpaperSettingsService Wallpaper => throw new NotSupportedException();
        public IWallpaperMediaService WallpaperMedia => throw new NotSupportedException();
        public IStatusBarSettingsService StatusBar => throw new NotSupportedException();
        public ITextCapsuleSettingsService TextCapsule => throw new NotSupportedException();
        public IWeatherSettingsService Weather => throw new NotSupportedException();
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

        public ThemeAppearanceSettingsState Get() => State;

        public void Save(ThemeAppearanceSettingsState state)
        {
            State = state;
        }

        public MonetPalette BuildPalette(bool nightMode, string? wallpaperPath, string? preferredSeedColor = null)
        {
            var seed = Color.Parse(preferredSeedColor ?? "#ff3b82f6");
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

    private sealed class FakeGridSettingsService : IGridSettingsService
    {
        public GridSettingsState State { get; private set; } = new(12, "Relaxed", 18);

        public GridSettingsState Get() => State;

        public void Save(GridSettingsState state)
        {
            State = state;
        }

        public string NormalizeSpacingPreset(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Relaxed" : value;
        }

        public double ResolveGapRatio(string? preset)
        {
            _ = preset;
            return 0.08;
        }

        public double CalculateEdgeInset(double hostWidth, double hostHeight, int shortSideCells, int insetPercent)
        {
            _ = hostWidth;
            _ = hostHeight;
            _ = shortSideCells;
            return insetPercent;
        }

        public DesktopGridMetrics CalculateGridMetrics(
            double hostWidth,
            double hostHeight,
            int shortSideCells,
            double gapRatio,
            double edgeInsetPx)
        {
            throw new NotSupportedException();
        }
    }
}
