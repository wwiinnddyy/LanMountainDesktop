using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;
using LibVLCSharp.Shared;
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
    string ResolvedSeedSource,
    MonetPalette MonetPalette,
    Color AccentColor,
    Color EffectiveSeedColor,
    IReadOnlyList<Color> WallpaperSeedCandidates,
    string SystemMaterialMode,
    IReadOnlyList<string> AvailableSystemMaterialModes,
    bool CanChangeSystemMaterial,
    bool UseSystemChrome,
    string? ResolvedWallpaperPath);

public interface IAppearanceThemeService
{
    AppearanceThemeSnapshot GetCurrent();

    AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState);

    event EventHandler<AppearanceThemeSnapshot>? Changed;

    void ApplyThemeResources(IResourceDictionary resources);

    AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role);

    void ApplyWindowMaterial(Window window, MaterialSurfaceRole role);
}

internal interface ISystemWallpaperService
{
    bool IsSupported { get; }

    string? GetWallpaperPath();
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

internal interface IVideoWallpaperSeedExtractor
{
    IReadOnlyList<Color> ExtractSeedCandidates(string videoPath, MonetColorService monetColorService);
}

internal readonly record struct WallpaperSeedSourceDescriptor(
    string SourceKind,
    string SourceKey,
    string? ResolvedWallpaperPath,
    string? FilePath,
    Color? SolidColor);

internal sealed record WallpaperSeedExtractionResult(
    string SourceKind,
    string SourceKey,
    string? ResolvedWallpaperPath,
    IReadOnlyList<Color> SeedCandidates);

internal readonly record struct WallpaperPaletteResolution(
    MonetPalette Palette,
    IReadOnlyList<Color> SeedCandidates,
    string ResolvedSeedSource,
    Color EffectiveSeedColor,
    string? ResolvedWallpaperPath);

internal sealed class LibVlcVideoWallpaperSeedExtractor : IVideoWallpaperSeedExtractor
{
    public IReadOnlyList<Color> ExtractSeedCandidates(string videoPath, MonetColorService monetColorService)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return [];
        }

        var snapshotPath = Path.Combine(
            Path.GetTempPath(),
            $"lanmountaindesktop-video-seed-{Guid.NewGuid():N}.png");

        try
        {
            using var libVlc = new LibVLC("--no-audio", "--intf=dummy", "--no-video-title-show");
            using var media = new Media(libVlc, new Uri(videoPath));
            using var mediaPlayer = new MediaPlayer(libVlc)
            {
                Media = media
            };

            mediaPlayer.Play();

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(180);
                if (!mediaPlayer.TakeSnapshot(0, snapshotPath, 320, 180))
                {
                    continue;
                }

                var fileInfo = new FileInfo(snapshotPath);
                if (!fileInfo.Exists || fileInfo.Length <= 0)
                {
                    continue;
                }

                using var bitmap = new Bitmap(snapshotPath);
                return monetColorService.ExtractSeedCandidates(bitmap);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "Appearance.VideoWallpaperPalette",
                $"Failed to extract wallpaper seed candidates from video '{videoPath}'.",
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(snapshotPath))
                {
                    File.Delete(snapshotPath);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        return [];
    }
}

internal sealed class SystemWallpaperService : ISystemWallpaperService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string? GetWallpaperPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            var wallpaperPath = key?.GetValue("WallPaper") as string;
            return string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath)
                ? null
                : wallpaperPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Appearance.SystemWallpaper", "Failed to resolve the current system wallpaper path.", ex);
            return null;
        }
    }
}

internal sealed class WindowMaterialService : IWindowMaterialService
{
    private const int Windows11Build = 22000;
    private const int Windows11_24H2Build = 26100;

    public bool CanChangeMode => GetSupportProfile() == WindowMaterialSupportProfile.FullSwitching;

    public IReadOnlyList<string> GetAvailableModes()
    {
        return GetSupportProfile() switch
        {
            WindowMaterialSupportProfile.FullSwitching =>
            [
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            WindowMaterialSupportProfile.FixedMica =>
            [
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica
            ],
            WindowMaterialSupportProfile.FixedAcrylic =>
            [
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            _ =>
            [
                ThemeAppearanceValues.MaterialNone
            ]
        };
    }

    public void Apply(Window window, string materialMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        var normalizedMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(materialMode);

        if (normalizedMode == ThemeAppearanceValues.MaterialNone)
        {
            window.Background = Brushes.White;
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            return;
        }

        window.Background = Brushes.Transparent;

        if (!OperatingSystem.IsWindows() || !IsTransparencyEnabled())
        {
            window.TransparencyLevelHint =
            [
                WindowTransparencyLevel.None
            ];
            return;
        }

        window.TransparencyLevelHint = normalizedMode switch
        {
            ThemeAppearanceValues.MaterialMica =>
            [
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            ThemeAppearanceValues.MaterialAcrylic =>
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            _ =>
            [
                WindowTransparencyLevel.None
            ]
        };
    }

    private static bool IsTransparencyEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            var value = key?.GetValue("EnableTransparency");
            return value switch
            {
                int intValue => intValue != 0,
                byte byteValue => byteValue != 0,
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }

    private static WindowMaterialSupportProfile GetSupportProfile()
    {
        if (!OperatingSystem.IsWindows() || !IsTransparencyEnabled())
        {
            return WindowMaterialSupportProfile.NoneOnly;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, Windows11_24H2Build))
        {
            return WindowMaterialSupportProfile.FullSwitching;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, Windows11Build))
        {
            return WindowMaterialSupportProfile.FixedMica;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0))
        {
            return WindowMaterialSupportProfile.FixedAcrylic;
        }

        return WindowMaterialSupportProfile.NoneOnly;
    }

    private enum WindowMaterialSupportProfile
    {
        NoneOnly = 0,
        FixedMica = 1,
        FixedAcrylic = 2,
        FullSwitching = 3
    }
}

internal sealed class MaterialSurfaceService : IMaterialSurfaceService
{
    public AppearanceMaterialSurface GetSurface(ThemeColorContext context, MaterialSurfaceRole role)
    {
        var monetPalette = context.MonetPalette;
        var monetColors = context.MonetColors?.Where(color => color.A > 0).ToArray() ?? [];
        var primary = context.UseNeutralSurfaces
            ? context.AccentColor
            : monetPalette?.Primary ?? (monetColors.Length > 0 ? monetColors[0] : context.AccentColor);
        var secondary = monetPalette?.Secondary
            ?? (monetColors.Length > 1
                ? monetColors[1]
                : ColorMath.Blend(primary, Color.Parse("#FFFFFFFF"), 0.14));
        var neutralPrimary = monetPalette?.Neutral
            ?? (monetColors.Length > 3
                ? monetColors[3]
                : ResolveNeutralBase(context.IsNightMode, role));
        var neutralSecondary = monetPalette?.NeutralVariant
            ?? (monetColors.Length > 4
                ? monetColors[4]
                : ResolveLiftBase(context.IsNightMode, role));
        var materialMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(context.SystemMaterialMode);

        var (tintStrength, liftStrength, alpha, blurRadius) = ResolveModeParameters(materialMode, role, context.IsNightMode);
        var neutralBase = ResolveNeutralBase(context.IsNightMode, role);
        var neutralLift = ResolveLiftBase(context.IsNightMode, role);
        var isDockLike = role is MaterialSurfaceRole.DockBackground;
        var isComponentLike = role is MaterialSurfaceRole.DesktopComponentHost or MaterialSurfaceRole.StatusBarComponentHost;
        var baseMix = isDockLike ? 0.88 : isComponentLike ? 0.74 : 0.82;
        var liftMix = isDockLike ? 0.58 : isComponentLike ? 0.34 : 0.46;
        var neutralMix = isDockLike ? 0.22 : 0.16;

        var background = ColorMath.Blend(neutralBase, neutralPrimary, baseMix);
        background = ColorMath.Blend(background, neutralLift, liftMix);
        background = ColorMath.Blend(background, neutralSecondary, neutralMix);
        if (!context.UseNeutralSurfaces)
        {
            background = ColorMath.Blend(background, primary, tintStrength);
            background = ColorMath.Blend(background, secondary, liftStrength);
        }

        if (isDockLike && !context.IsNightMode)
        {
            background = ColorMath.Blend(background, Color.Parse("#FFFFFFFF"), 0.12);
        }

        background = Color.FromArgb(alpha, background.R, background.G, background.B);

        var borderSeed = context.IsNightMode
            ? ColorMath.Blend(neutralSecondary, Color.Parse("#FFFFFFFF"), 0.16)
            : ColorMath.Blend(neutralSecondary, Color.Parse("#FF334155"), 0.08);
        if (!context.UseNeutralSurfaces && !isComponentLike)
        {
            borderSeed = ColorMath.Blend(borderSeed, primary, 0.08);
        }

        var borderAlpha = role switch
        {
            MaterialSurfaceRole.DockBackground => context.IsNightMode ? (byte)0x34 : (byte)0x18,
            MaterialSurfaceRole.DesktopComponentHost or MaterialSurfaceRole.StatusBarComponentHost =>
                context.IsNightMode ? (byte)0x18 : (byte)0x10,
            MaterialSurfaceRole.StatusBarBackground => (byte)0x00,
            _ => context.IsNightMode ? (byte)0x26 : (byte)0x16
        };
        var border = ColorMath.WithAlpha(borderSeed, borderAlpha);

        return new AppearanceMaterialSurface(background, border, blurRadius, 1.0);
    }

    private static (double TintStrength, double LiftStrength, byte Alpha, double BlurRadius) ResolveModeParameters(
        string materialMode,
        MaterialSurfaceRole role,
        bool isNightMode)
    {
        var isOverlay = role is MaterialSurfaceRole.DockBackground or MaterialSurfaceRole.StatusBarBackground or MaterialSurfaceRole.OverlayPanel;
        return materialMode switch
        {
            ThemeAppearanceValues.MaterialAcrylic => (
                isOverlay ? 0.30 : 0.20,
                isOverlay ? 0.22 : 0.14,
                isNightMode ? (byte)0xD8 : (byte)0xE0,
                isOverlay ? 36 : 28),
            ThemeAppearanceValues.MaterialMica => (
                isOverlay ? 0.20 : 0.14,
                isOverlay ? 0.12 : 0.08,
                isNightMode ? (byte)0xEC : (byte)0xF2,
                isOverlay ? 28 : 20),
            _ => (
                isOverlay ? 0.12 : 0.08,
                isOverlay ? 0.08 : 0.05,
                (byte)0xFF,
                0)
        };
    }

    private static Color ResolveNeutralBase(bool isNightMode, MaterialSurfaceRole role)
    {
        return role switch
        {
            MaterialSurfaceRole.WindowBackground => isNightMode ? Color.Parse("#FF0A0F16") : Color.Parse("#FFF7F8FA"),
            MaterialSurfaceRole.SettingsWindowBackground => isNightMode ? Color.Parse("#FF0C121A") : Color.Parse("#FFF8FAFC"),
            MaterialSurfaceRole.DockBackground => isNightMode ? Color.Parse("#FF111A24") : Color.Parse("#FFFAFBFD"),
            MaterialSurfaceRole.StatusBarBackground => isNightMode ? Color.Parse("#FF101720") : Color.Parse("#FFF9FBFE"),
            MaterialSurfaceRole.StatusBarComponentHost => isNightMode ? Color.Parse("#FF111A23") : Color.Parse("#FFFCFDFE"),
            MaterialSurfaceRole.OverlayPanel => isNightMode ? Color.Parse("#FF131C27") : Color.Parse("#FFF4F7FB"),
            _ => isNightMode ? Color.Parse("#FF121B26") : Color.Parse("#FFFDFEFF")
        };
    }

    private static Color ResolveLiftBase(bool isNightMode, MaterialSurfaceRole role)
    {
        return role switch
        {
            MaterialSurfaceRole.DockBackground or MaterialSurfaceRole.StatusBarBackground or MaterialSurfaceRole.OverlayPanel =>
                isNightMode ? Color.Parse("#FF1B2633") : Color.Parse("#FFFFFFFF"),
            _ => isNightMode ? Color.Parse("#FF17212D") : Color.Parse("#FFFFFFFF")
        };
    }
}

internal sealed class AppearanceThemeService : IAppearanceThemeService, IDisposable
{
    private static readonly Color DefaultAccentColor = Color.Parse("#FF3B82F6");
    private static readonly Color NeutralFallbackSeedColor = Color.Parse("#FF8A8A8A");
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISystemWallpaperService _systemWallpaperService;
    private readonly IWindowMaterialService _windowMaterialService;
    private readonly IMaterialSurfaceService _materialSurfaceService;
    private readonly IVideoWallpaperSeedExtractor _videoWallpaperSeedExtractor;
    private readonly MonetColorService _monetColorService = new();
    private readonly string _liveThemeColorMode;
    private readonly string _liveSystemMaterialMode;
    private readonly string? _liveSelectedWallpaperSeed;
    private readonly object _paletteGate = new();
    private readonly Dictionary<string, WallpaperSeedExtractionResult> _wallpaperSeedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingWallpaperSeedKeys = new(StringComparer.OrdinalIgnoreCase);

    public AppearanceThemeService(
        ISettingsFacadeService settingsFacade,
        ISystemWallpaperService systemWallpaperService,
        IWindowMaterialService windowMaterialService,
        IMaterialSurfaceService materialSurfaceService,
        IVideoWallpaperSeedExtractor? videoWallpaperSeedExtractor = null)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _systemWallpaperService = systemWallpaperService ?? throw new ArgumentNullException(nameof(systemWallpaperService));
        _windowMaterialService = windowMaterialService ?? throw new ArgumentNullException(nameof(windowMaterialService));
        _materialSurfaceService = materialSurfaceService ?? throw new ArgumentNullException(nameof(materialSurfaceService));
        _videoWallpaperSeedExtractor = videoWallpaperSeedExtractor ?? new LibVlcVideoWallpaperSeedExtractor();
        var initialThemeState = _settingsFacade.Theme.Get();
        _liveThemeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(
            initialThemeState.ThemeColorMode,
            initialThemeState.ThemeColor);
        _liveSystemMaterialMode = ResolveSupportedMaterialMode(initialThemeState.SystemMaterialMode);
        _liveSelectedWallpaperSeed = initialThemeState.SelectedWallpaperSeed;
        _settingsFacade.Settings.Changed += OnSettingsChanged;
    }

    public event EventHandler<AppearanceThemeSnapshot>? Changed;

    public AppearanceThemeSnapshot GetCurrent()
    {
        return BuildCurrentSnapshot(queueWallpaperPaletteBuild: true);
    }

    public AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState)
    {
        ArgumentNullException.ThrowIfNull(pendingState);

        var normalizedThemeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(
            pendingState.ThemeColorMode,
            pendingState.ThemeColor);
        var normalizedSystemMaterialMode = ResolveSupportedMaterialMode(pendingState.SystemMaterialMode);
        return BuildSnapshot(
            pendingState with
            {
                ThemeColorMode = normalizedThemeColorMode,
                SystemMaterialMode = normalizedSystemMaterialMode
            },
            normalizedThemeColorMode,
            normalizedSystemMaterialMode,
            pendingState.SelectedWallpaperSeed,
            queueWallpaperPaletteBuild: true);
    }

    public void ApplyThemeResources(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var snapshot = GetCurrent();
        var context = CreateThemeContext(snapshot);
        ThemeColorSystemService.ApplyThemeResources(resources, context);
        GlassEffectService.ApplyGlassResources(resources, context);
    }

    public AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role)
    {
        var snapshot = GetCurrent();
        return _materialSurfaceService.GetSurface(CreateThemeContext(snapshot), role);
    }

    public void ApplyWindowMaterial(Window window, MaterialSurfaceRole role)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Avoid hot-switching real backdrops on already-visible windows. This has been
        // a stability hotspot when users flip theme source/material at runtime.
        if (window.IsVisible)
        {
            return;
        }

        var snapshot = GetCurrent();

        try
        {
            _windowMaterialService.Apply(window, snapshot.SystemMaterialMode);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "Appearance.WindowMaterial",
                $"Failed to apply window material '{snapshot.SystemMaterialMode}'. Falling back to none.",
                ex);
            _windowMaterialService.Apply(window, ThemeAppearanceValues.MaterialNone);
        }
    }

    public void Dispose()
    {
        _settingsFacade.Settings.Changed -= OnSettingsChanged;
    }

    private AppearanceThemeSnapshot BuildCurrentSnapshot(bool queueWallpaperPaletteBuild)
    {
        var themeState = _settingsFacade.Theme.Get();
        return BuildSnapshot(
            themeState,
            _liveThemeColorMode,
            _liveSystemMaterialMode,
            _liveSelectedWallpaperSeed,
            queueWallpaperPaletteBuild);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope != SettingsScope.App)
        {
            return;
        }

        var changedKeys = e.ChangedKeys?.ToArray();
        var refreshAll = changedKeys is null || changedKeys.Length == 0;
        var respondsToThemeColor = string.Equals(
            _liveThemeColorMode,
            ThemeAppearanceValues.ColorModeSeedMonet,
            StringComparison.OrdinalIgnoreCase);
        var respondsToWallpaper = string.Equals(
            _liveThemeColorMode,
            ThemeAppearanceValues.ColorModeWallpaperMonet,
            StringComparison.OrdinalIgnoreCase);

        if (!refreshAll &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.IsNightMode), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.UseSystemChrome), StringComparer.OrdinalIgnoreCase) &&
            !(respondsToThemeColor &&
              changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColor), StringComparer.OrdinalIgnoreCase)) &&
            !(respondsToWallpaper &&
              (changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperPath), StringComparer.OrdinalIgnoreCase) ||
               changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperType), StringComparer.OrdinalIgnoreCase) ||
               changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperColor), StringComparer.OrdinalIgnoreCase))))
        {
            return;
        }

        RaiseChanged(queueWallpaperPaletteBuild: true);
    }

    private AppearanceThemeSnapshot BuildSnapshot(
        ThemeAppearanceSettingsState themeState,
        string themeColorMode,
        string systemMaterialMode,
        string? selectedWallpaperSeed,
        bool queueWallpaperPaletteBuild)
    {
        var availableModes = _windowMaterialService.GetAvailableModes();
        MonetPalette palette;
        IReadOnlyList<Color> wallpaperSeedCandidates;
        Color effectiveSeedColor;
        string resolvedSeedSource;
        string? resolvedWallpaperPath;

        if (string.Equals(themeColorMode, ThemeAppearanceValues.ColorModeWallpaperMonet, StringComparison.OrdinalIgnoreCase))
        {
            var wallpaperState = _settingsFacade.Wallpaper.Get();
            var wallpaperResolution = ResolveWallpaperPalette(
                themeState.IsNightMode,
                wallpaperState,
                selectedWallpaperSeed,
                queueWallpaperPaletteBuild);
            palette = wallpaperResolution.Palette;
            wallpaperSeedCandidates = wallpaperResolution.SeedCandidates;
            effectiveSeedColor = wallpaperResolution.EffectiveSeedColor;
            resolvedSeedSource = wallpaperResolution.ResolvedSeedSource;
            resolvedWallpaperPath = wallpaperResolution.ResolvedWallpaperPath;
        }
        else
        {
            var preferredSeedColor = string.Equals(themeColorMode, ThemeAppearanceValues.ColorModeSeedMonet, StringComparison.OrdinalIgnoreCase)
                ? themeState.ThemeColor
                : null;
            palette = _settingsFacade.Theme.BuildPalette(themeState.IsNightMode, null, preferredSeedColor);
            wallpaperSeedCandidates = [];
            effectiveSeedColor = ResolveEffectiveSeedColor(themeColorMode, themeState.ThemeColor, palette);
            resolvedSeedSource = string.Equals(themeColorMode, ThemeAppearanceValues.ColorModeDefaultNeutral, StringComparison.OrdinalIgnoreCase)
                ? "neutral"
                : "user_color";
            resolvedWallpaperPath = null;
        }

        return new AppearanceThemeSnapshot(
            themeState.IsNightMode,
            themeColorMode,
            themeState.ThemeColor,
            selectedWallpaperSeed,
            resolvedSeedSource,
            palette,
            ResolveAccentColor(themeColorMode, themeState.ThemeColor, palette),
            effectiveSeedColor,
            wallpaperSeedCandidates,
            systemMaterialMode,
            availableModes,
            _windowMaterialService.CanChangeMode,
            themeState.UseSystemChrome,
            resolvedWallpaperPath);
    }

    private ThemeColorContext CreateThemeContext(AppearanceThemeSnapshot snapshot)
    {
        return new ThemeColorContext(
            snapshot.AccentColor,
            IsLightBackground: !snapshot.IsNightMode,
            IsLightNavBackground: !snapshot.IsNightMode,
            IsNightMode: snapshot.IsNightMode,
            MonetPalette: snapshot.MonetPalette,
            MonetColors: snapshot.MonetPalette.MonetColors,
            UseNeutralSurfaces: snapshot.ThemeColorMode == ThemeAppearanceValues.ColorModeDefaultNeutral,
            SystemMaterialMode: snapshot.SystemMaterialMode);
    }

    private string ResolveSupportedMaterialMode(string? requestedMode)
    {
        var normalized = ThemeAppearanceValues.NormalizeSystemMaterialMode(requestedMode);
        var availableModes = _windowMaterialService.GetAvailableModes();
        return availableModes.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : ThemeAppearanceValues.MaterialNone;
    }

    private WallpaperPaletteResolution ResolveWallpaperPalette(
        bool nightMode,
        WallpaperSettingsState wallpaperState,
        string? selectedWallpaperSeed,
        bool queueWallpaperPaletteBuild)
    {
        var source = ResolveWallpaperSeedSource(wallpaperState);
        if (string.Equals(source.SourceKind, "fallback", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFallbackWallpaperPaletteResolution(nightMode, source.ResolvedWallpaperPath);
        }

        if (string.Equals(source.SourceKind, "app_solid", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = source.SolidColor is { } solidColor
                ? new[] { solidColor }
                : [];
            return BuildWallpaperPaletteResolution(nightMode, source, candidates, selectedWallpaperSeed);
        }

        lock (_paletteGate)
        {
            if (_wallpaperSeedCache.TryGetValue(source.SourceKey, out var cachedSeedResult))
            {
                if (cachedSeedResult.SeedCandidates.Count > 0)
                {
                    return BuildWallpaperPaletteResolution(
                        nightMode,
                        source with
                        {
                            SourceKind = cachedSeedResult.SourceKind,
                            ResolvedWallpaperPath = cachedSeedResult.ResolvedWallpaperPath
                        },
                        cachedSeedResult.SeedCandidates,
                        selectedWallpaperSeed);
                }

                return BuildFallbackWallpaperPaletteResolution(nightMode, cachedSeedResult.ResolvedWallpaperPath);
            }
        }

        if (queueWallpaperPaletteBuild)
        {
            QueueWallpaperSeedExtraction(source);
        }

        return BuildFallbackWallpaperPaletteResolution(nightMode, source.ResolvedWallpaperPath);
    }

    private static Color ResolveAccentColor(
        string themeColorMode,
        string? colorText,
        MonetPalette monetPalette)
    {
        if (themeColorMode == ThemeAppearanceValues.ColorModeDefaultNeutral)
        {
            return DefaultAccentColor;
        }

        if (monetPalette.Primary.A > 0)
        {
            return monetPalette.Primary;
        }

        if (!string.IsNullOrWhiteSpace(colorText) && Color.TryParse(colorText, out var parsedColor))
        {
            return parsedColor;
        }

        return DefaultAccentColor;
    }

    private static Color ResolveEffectiveSeedColor(
        string themeColorMode,
        string? userThemeColor,
        MonetPalette monetPalette)
    {
        if (themeColorMode == ThemeAppearanceValues.ColorModeDefaultNeutral)
        {
            return DefaultAccentColor;
        }

        if (themeColorMode == ThemeAppearanceValues.ColorModeSeedMonet &&
            !string.IsNullOrWhiteSpace(userThemeColor) &&
            Color.TryParse(userThemeColor, out var parsedColor))
        {
            return parsedColor;
        }

        return monetPalette.Seed;
    }

    private WallpaperPaletteResolution BuildWallpaperPaletteResolution(
        bool nightMode,
        WallpaperSeedSourceDescriptor source,
        IReadOnlyList<Color> seedCandidates,
        string? selectedWallpaperSeed)
    {
        var validatedSeed = ResolveSelectedWallpaperSeed(seedCandidates, selectedWallpaperSeed);
        var palette = _monetColorService.BuildPaletteFromSeedCandidates(seedCandidates, nightMode, validatedSeed);
        return new WallpaperPaletteResolution(
            palette,
            seedCandidates,
            source.SourceKind,
            palette.Seed,
            source.ResolvedWallpaperPath);
    }

    private WallpaperPaletteResolution BuildFallbackWallpaperPaletteResolution(bool nightMode, string? resolvedWallpaperPath)
    {
        var palette = _monetColorService.BuildPaletteFromSeedCandidates([], nightMode, NeutralFallbackSeedColor);
        return new WallpaperPaletteResolution(
            palette,
            [],
            "fallback",
            palette.Seed,
            resolvedWallpaperPath);
    }

    private void QueueWallpaperSeedExtraction(WallpaperSeedSourceDescriptor source)
    {
        if (string.Equals(source.SourceKind, "fallback", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.SourceKind, "app_solid", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_paletteGate)
        {
            if (_pendingWallpaperSeedKeys.Contains(source.SourceKey))
            {
                return;
            }

            _pendingWallpaperSeedKeys.Add(source.SourceKey);
        }

        _ = Task.Run(() =>
        {
            WallpaperSeedExtractionResult? extractionResult = null;

            try
            {
                extractionResult = ExtractWallpaperSeedCandidates(source);
            }
            catch (Exception ex)
            {
                AppLogger.Warn(
                    "Appearance.WallpaperSeed",
                    $"Failed to build wallpaper seed candidates asynchronously. Source='{source.SourceKind}'; Path='{source.FilePath}'.",
                    ex);
            }
            finally
            {
                lock (_paletteGate)
                {
                    _pendingWallpaperSeedKeys.Remove(source.SourceKey);
                    if (extractionResult is not null)
                    {
                        _wallpaperSeedCache[source.SourceKey] = extractionResult;
                    }
                }
            }

            if (extractionResult is not null)
            {
                RaiseChanged(queueWallpaperPaletteBuild: false);
            }
        });
    }

    private WallpaperSeedExtractionResult ExtractWallpaperSeedCandidates(WallpaperSeedSourceDescriptor source)
    {
        IReadOnlyList<Color> seedCandidates = source.SourceKind switch
        {
            "app_wallpaper" or "system_wallpaper" => ExtractImageSeedCandidates(source.FilePath),
            "app_video" => ExtractVideoSeedCandidates(source.FilePath),
            "app_solid" when source.SolidColor is { } solidColor => new[] { solidColor },
            _ => []
        };

        return new WallpaperSeedExtractionResult(
            source.SourceKind,
            source.SourceKey,
            source.ResolvedWallpaperPath,
            seedCandidates);
    }

    private IReadOnlyList<Color> ExtractImageSeedCandidates(string? wallpaperPath)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
        {
            return [];
        }

        try
        {
            using var bitmap = new Bitmap(wallpaperPath);
            return _monetColorService.ExtractSeedCandidates(bitmap);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "Appearance.WallpaperSeed",
                $"Failed to extract wallpaper seed candidates from image '{wallpaperPath}'.",
                ex);
            return [];
        }
    }

    private IReadOnlyList<Color> ExtractVideoSeedCandidates(string? wallpaperPath)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
        {
            return [];
        }

        return _videoWallpaperSeedExtractor.ExtractSeedCandidates(wallpaperPath, _monetColorService);
    }

    private WallpaperSeedSourceDescriptor ResolveWallpaperSeedSource(WallpaperSettingsState wallpaperState)
    {
        if (string.Equals(wallpaperState.Type, "SolidColor", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(wallpaperState.Color) &&
            Color.TryParse(wallpaperState.Color, out var solidColor))
        {
            var solidText = solidColor.ToString();
            return new WallpaperSeedSourceDescriptor(
                "app_solid",
                $"app_solid|{solidText}",
                null,
                null,
                solidColor);
        }

        var wallpaperPath = string.IsNullOrWhiteSpace(wallpaperState.WallpaperPath)
            ? null
            : wallpaperState.WallpaperPath.Trim();
        var appWallpaperMediaType = _settingsFacade.WallpaperMedia.DetectMediaType(wallpaperPath);
        if (!string.IsNullOrWhiteSpace(wallpaperPath) && File.Exists(wallpaperPath))
        {
            if (appWallpaperMediaType == WallpaperMediaType.Image)
            {
                return new WallpaperSeedSourceDescriptor(
                    "app_wallpaper",
                    CreateWallpaperSourceKey("app_wallpaper", wallpaperPath),
                    wallpaperPath,
                    wallpaperPath,
                    null);
            }

            if (appWallpaperMediaType == WallpaperMediaType.Video)
            {
                return new WallpaperSeedSourceDescriptor(
                    "app_video",
                    CreateWallpaperSourceKey("app_video", wallpaperPath),
                    wallpaperPath,
                    wallpaperPath,
                    null);
            }
        }

        var systemWallpaper = _systemWallpaperService.GetWallpaperPath();
        if (!string.IsNullOrWhiteSpace(systemWallpaper) &&
            File.Exists(systemWallpaper) &&
            _settingsFacade.WallpaperMedia.DetectMediaType(systemWallpaper) == WallpaperMediaType.Image)
        {
            return new WallpaperSeedSourceDescriptor(
                "system_wallpaper",
                CreateWallpaperSourceKey("system_wallpaper", systemWallpaper),
                systemWallpaper,
                systemWallpaper,
                null);
        }

        return new WallpaperSeedSourceDescriptor(
            "fallback",
            "fallback",
            null,
            null,
            null);
    }

    private void RaiseChanged(bool queueWallpaperPaletteBuild)
    {
        var snapshot = BuildCurrentSnapshot(queueWallpaperPaletteBuild);
        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, snapshot);
            return;
        }

        Dispatcher.UIThread.Post(() => Changed?.Invoke(this, snapshot), DispatcherPriority.Background);
    }

    private static Color? ResolveSelectedWallpaperSeed(
        IReadOnlyList<Color> seedCandidates,
        string? selectedWallpaperSeed)
    {
        if (seedCandidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedWallpaperSeed) &&
            Color.TryParse(selectedWallpaperSeed, out var parsedSeed))
        {
            foreach (var candidate in seedCandidates)
            {
                if (candidate == parsedSeed)
                {
                    return candidate;
                }
            }
        }

        return seedCandidates[0];
    }

    private static string CreateWallpaperSourceKey(string sourceKind, string wallpaperPath)
    {
        long lastWriteTicks = 0;
        long length = 0;

        try
        {
            var fileInfo = new FileInfo(wallpaperPath);
            if (fileInfo.Exists)
            {
                lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
                length = fileInfo.Length;
            }
        }
        catch
        {
            // Keep the cache key resilient even if metadata lookup fails.
        }

        return string.Concat(
            sourceKind,
            "|",
            wallpaperPath,
            "|",
            lastWriteTicks.ToString(),
            "|",
            length.ToString());
    }
}

internal static class HostAppearanceThemeProvider
{
    private static readonly object Gate = new();
    private static AppearanceThemeService? _instance;

    public static IAppearanceThemeService GetOrCreate()
    {
        lock (Gate)
        {
            return _instance ??= new AppearanceThemeService(
                HostSettingsFacadeProvider.GetOrCreate(),
                new SystemWallpaperService(),
                new WindowMaterialService(),
                new MaterialSurfaceService());
        }
    }
}
