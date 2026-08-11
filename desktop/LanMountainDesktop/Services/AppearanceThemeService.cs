using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;
using LanMountainDesktop.Theme;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

public enum MaterialSurfaceRole
{
    WindowBackground = 0,
    SettingsWindowBackground = 1,
    DockBackground = 2,
    StatusBarBackground = 3,
    DesktopComponentHost = 4,
    StatusBarComponentHost = 5,
    OverlayPanel = 6
}

public sealed record AppearanceMaterialSurface(
    Color BackgroundColor,
    Color BorderColor,
    double BlurRadius,
    double Opacity);

public sealed record AppearanceThemeSnapshot(
    bool IsNightMode,
    string ThemeColorMode,
    string? UserThemeColor,
    string? SelectedWallpaperSeed,
    string CornerRadiusStyle,
    AppearanceCornerRadiusTokens CornerRadiusTokens,
    string ResolvedSeedSource,
    MonetPalette MonetPalette,
    Color AccentColor,
    Color EffectiveSeedColor,
    IReadOnlyList<Color> WallpaperSeedCandidates,
    string SystemMaterialMode,
    IReadOnlyList<string> AvailableSystemMaterialModes,
    bool CanChangeSystemMaterial,
    bool UseSystemChrome,
    string? ResolvedWallpaperPath,
    string ThemeWallpaperColorSource = ThemeAppearanceValues.WallpaperColorSourceAuto,
    bool UseNativeWallpaperChangeEvents = true);

public interface IAppearanceThemeService
{
    AppearanceThemeSnapshot GetCurrent();

    AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState);

    event EventHandler<AppearanceThemeSnapshot>? Changed;

    void ApplyThemeResources(IResourceDictionary resources);

    AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role);

    void ApplyWindowMaterial(Window window, MaterialSurfaceRole role);
}

internal interface IWindowMaterialService
{
    IReadOnlyList<string> GetAvailableModes();

    bool CanChangeMode { get; }

    void Apply(Window window, string materialMode);
}

internal interface IMaterialSurfaceService
{
    AppearanceMaterialSurface GetSurface(ThemeColorContext context, MaterialSurfaceRole role);
}

internal sealed class AppearanceThemeService : IAppearanceThemeService, IDisposable
{
    private readonly MaterialColorService _materialColorService;

    public AppearanceThemeService(MaterialColorService materialColorService)
    {
        _materialColorService = materialColorService ?? throw new ArgumentNullException(nameof(materialColorService));
        _materialColorService.AppearanceThemeChanged += OnAppearanceThemeChanged;
    }

    public event EventHandler<AppearanceThemeSnapshot>? Changed;

    public AppearanceThemeSnapshot GetCurrent()
    {
        return _materialColorService.GetCurrent();
    }

    public AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState)
    {
        return _materialColorService.BuildPreview(pendingState);
    }

    public void ApplyThemeResources(IResourceDictionary resources)
    {
        _materialColorService.ApplyThemeResources(resources);
    }

    public AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role)
    {
        return _materialColorService.GetMaterialSurface(role);
    }

    public void ApplyWindowMaterial(Window window, MaterialSurfaceRole role)
    {
        _materialColorService.ApplyWindowMaterial(window, role);
    }

    public void Dispose()
    {
        _materialColorService.AppearanceThemeChanged -= OnAppearanceThemeChanged;
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot snapshot)
    {
        _ = sender;
        Changed?.Invoke(this, snapshot);
    }
}

internal static class HostAppearanceThemeProvider
{
    private static readonly object Gate = new();
    private static MaterialColorService? _materialColorService;
    private static AppearanceThemeService? _appearanceThemeService;

    public static IAppearanceThemeService GetOrCreate()
    {
        lock (Gate)
        {
            return _appearanceThemeService ??= new AppearanceThemeService(GetMaterialColorServiceCore());
        }
    }

    internal static MaterialColorService GetMaterialColorService()
    {
        lock (Gate)
        {
            return GetMaterialColorServiceCore();
        }
    }

    private static MaterialColorService GetMaterialColorServiceCore()
    {
        return _materialColorService ??= new MaterialColorService(
            HostSettingsFacadeProvider.GetOrCreate(),
            HostSystemWallpaperProvider.GetOrCreate(),
            new WindowMaterialService(),
            new MaterialSurfaceService());
    }
}

internal static class HostMaterialColorProvider
{
    public static IMaterialColorService GetOrCreate()
    {
        return HostAppearanceThemeProvider.GetMaterialColorService();
    }
}
