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

    public bool CanChangeMode => GetAvailableModes().Count > 1;

    public IReadOnlyList<string> GetAvailableModes()
    {
        return GetSupportProfile() switch
        {
            WindowMaterialSupportProfile.FullSwitching =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            WindowMaterialSupportProfile.FixedMica =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialMica
            ],
            WindowMaterialSupportProfile.FixedAcrylic =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone,
                ThemeAppearanceValues.MaterialAcrylic
            ],
            _ =>
            [
                ThemeAppearanceValues.MaterialAuto,
                ThemeAppearanceValues.MaterialNone
            ]
        };
    }

    public void Apply(Window window, string materialMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        var normalizedMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(materialMode);
        var supportProfile = GetSupportProfile();
        var effectiveMode = normalizedMode == ThemeAppearanceValues.MaterialAuto
            ? ResolveAutoMaterialMode(supportProfile)
            : normalizedMode;

        if (effectiveMode == ThemeAppearanceValues.MaterialNone)
        {
            window.Background = Brushes.White;
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            return;
        }

        window.Background = Brushes.Transparent;

        if (supportProfile == WindowMaterialSupportProfile.NoneOnly)
        {
            window.TransparencyLevelHint =
            [
                WindowTransparencyLevel.None
            ];
            return;
        }

        window.TransparencyLevelHint = normalizedMode == ThemeAppearanceValues.MaterialAuto
            ? ResolveAutoTransparencyLevels(supportProfile)
            : effectiveMode switch
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

    private static string ResolveAutoMaterialMode(WindowMaterialSupportProfile supportProfile)
    {
        return supportProfile switch
        {
            WindowMaterialSupportProfile.FullSwitching or WindowMaterialSupportProfile.FixedMica =>
                ThemeAppearanceValues.MaterialMica,
            WindowMaterialSupportProfile.FixedAcrylic =>
                ThemeAppearanceValues.MaterialAcrylic,
            _ => ThemeAppearanceValues.MaterialNone
        };
    }

    private static IReadOnlyList<WindowTransparencyLevel> ResolveAutoTransparencyLevels(WindowMaterialSupportProfile supportProfile)
    {
        return supportProfile switch
        {
            WindowMaterialSupportProfile.FullSwitching or WindowMaterialSupportProfile.FixedMica =>
            [
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ],
            WindowMaterialSupportProfile.FixedAcrylic =>
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
        var materialMode = ThemeAppearanceValues.ResolveEffectiveSystemMaterialMode(context.SystemMaterialMode);

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
        // Settings 根层（如 RootGrid）叠在 Transparent + Mica/Acrylic 上：过高 alpha 会完全盖住系统 backdrop。
        // 保持非 None 下较低 alpha；None 仍用不透明白底等价。BlurRadius=0（由 DWM 提供模糊）。
        if (role == MaterialSurfaceRole.SettingsWindowBackground)
        {
            return materialMode switch
            {
                ThemeAppearanceValues.MaterialAcrylic => (
                    0.20,
                    0.14,
                    isNightMode ? (byte)0x8E : (byte)0x96,
                    0),
                ThemeAppearanceValues.MaterialMica => (
                    0.14,
                    0.08,
                    isNightMode ? (byte)0x9E : (byte)0xA6,
                    0),
                _ => (0.08, 0.05, (byte)0xFF, 0)
            };
        }

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

internal sealed class AppearanceThemeService : IAppearanceThemeService, IMaterialColorService, IDisposable
{
    private static readonly Color DefaultAccentColor = Color.Parse("#FF3B82F6");
    private static readonly Color NeutralFallbackSeedColor = Color.Parse("#FF8A8A8A");
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISystemWallpaperService _systemWallpaperService;
    private readonly IWindowMaterialService _windowMaterialService;
    private readonly IMaterialSurfaceService _materialSurfaceService;
    private readonly MonetColorService _monetColorService = new();
    private string _liveThemeColorMode;
    private string _liveSystemMaterialMode;
    private string? _liveSelectedWallpaperSeed;
    private string _liveThemeWallpaperColorSource;
    private bool _liveUseNativeWallpaperChangeEvents;
    private readonly object _paletteGate = new();
    private readonly Dictionary<string, WallpaperSeedExtractionResult> _wallpaperSeedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingWallpaperSeedKeys = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _systemWallpaperPollTimer;
    private string? _lastObservedWallpaperSourceKey;
    private bool _nativeWallpaperEventsActive;
    private bool _wallpaperPollingActive;

    public AppearanceThemeService(
        ISettingsFacadeService settingsFacade,
        ISystemWallpaperService systemWallpaperService,
        IWindowMaterialService windowMaterialService,
        IMaterialSurfaceService materialSurfaceService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _systemWallpaperService = systemWallpaperService ?? throw new ArgumentNullException(nameof(systemWallpaperService));
        _windowMaterialService = windowMaterialService ?? throw new ArgumentNullException(nameof(windowMaterialService));
        _materialSurfaceService = materialSurfaceService ?? throw new ArgumentNullException(nameof(materialSurfaceService));
        var initialThemeState = _settingsFacade.Theme.Get();
        _liveThemeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(
            initialThemeState.ThemeColorMode,
            initialThemeState.ThemeColor);
        _liveSystemMaterialMode = ResolveSupportedMaterialMode(initialThemeState.SystemMaterialMode);
        _liveSelectedWallpaperSeed = initialThemeState.SelectedWallpaperSeed;
        _liveThemeWallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(initialThemeState.ThemeWallpaperColorSource);
        _liveUseNativeWallpaperChangeEvents = initialThemeState.UseNativeWallpaperChangeEvents;
        _settingsFacade.Settings.Changed += OnSettingsChanged;
        ConfigureSystemWallpaperMonitoring(initialThemeState);
    }

    public event EventHandler<AppearanceThemeSnapshot>? Changed;

    public event EventHandler<MaterialColorSnapshot>? MaterialColorChanged;

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

    public MaterialColorSnapshot GetMaterialColorSnapshot()
    {
        return CreateMaterialColorSnapshot(GetCurrent());
    }

    public MaterialColorSnapshot BuildMaterialColorPreview(ThemeAppearanceSettingsState pendingState)
    {
        return CreateMaterialColorSnapshot(BuildPreview(pendingState));
    }

    public MaterialSurfaceSnapshot GetSurface(MaterialSurfaceRole role)
    {
        var surface = GetMaterialSurface(role);
        return new MaterialSurfaceSnapshot(
            role,
            surface.BackgroundColor,
            surface.BorderColor,
            surface.BlurRadius,
            surface.Opacity);
    }

    public void RefreshWallpaperColors()
    {
        lock (_paletteGate)
        {
            _wallpaperSeedCache.Clear();
            _pendingWallpaperSeedKeys.Clear();
            _lastObservedWallpaperSourceKey = null;
        }

        RaiseChanged(queueWallpaperPaletteBuild: true);
    }

    public void ApplyThemeResources(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var snapshot = GetCurrent();
        var context = CreateThemeContext(snapshot);
        ThemeColorSystemService.ApplyThemeResources(resources, context);
        GlassEffectService.ApplyGlassResources(resources, context);
        resources["DesignCornerRadiusMicro"] = snapshot.CornerRadiusTokens.Micro;
        resources["DesignCornerRadiusXs"] = snapshot.CornerRadiusTokens.Xs;
        resources["DesignCornerRadiusSm"] = snapshot.CornerRadiusTokens.Sm;
        resources["DesignCornerRadiusMd"] = snapshot.CornerRadiusTokens.Md;
        resources["DesignCornerRadiusLg"] = snapshot.CornerRadiusTokens.Lg;
        resources["DesignCornerRadiusXl"] = snapshot.CornerRadiusTokens.Xl;
        resources["DesignCornerRadiusIsland"] = snapshot.CornerRadiusTokens.Island;
        resources["DesignCornerRadiusComponent"] = snapshot.CornerRadiusTokens.Component;
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
        // SettingsWindowBackground 是唯一需要材质与资源同步热切换的宿主角色；其它窗口仍保持「仅创建时」应用以降低风险。
        if (window.IsVisible && role != MaterialSurfaceRole.SettingsWindowBackground)
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
        StopSystemWallpaperMonitoring();
        _systemWallpaperPollTimer?.Dispose();
        _systemWallpaperPollTimer = null;
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
            !changedKeys.Contains(nameof(AppSettingsSnapshot.CornerRadiusStyle), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColorMode), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.SystemMaterialMode), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.SelectedWallpaperSeed), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeWallpaperColorSource), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.UseNativeWallpaperChangeEvents), StringComparer.OrdinalIgnoreCase) &&
            !changedKeys.Contains(nameof(AppSettingsSnapshot.SystemWallpaperRefreshIntervalSeconds), StringComparer.OrdinalIgnoreCase) &&
            !(respondsToThemeColor &&
              changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColor), StringComparer.OrdinalIgnoreCase)) &&
            !(respondsToWallpaper &&
              (changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperPath), StringComparer.OrdinalIgnoreCase) ||
               changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperType), StringComparer.OrdinalIgnoreCase) ||
               changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperColor), StringComparer.OrdinalIgnoreCase))))
        {
            return;
        }

        var latestThemeState = _settingsFacade.Theme.Get();
        _liveThemeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(
            latestThemeState.ThemeColorMode,
            latestThemeState.ThemeColor);
        _liveSystemMaterialMode = ResolveSupportedMaterialMode(latestThemeState.SystemMaterialMode);
        _liveSelectedWallpaperSeed = latestThemeState.SelectedWallpaperSeed;
        _liveThemeWallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(latestThemeState.ThemeWallpaperColorSource);
        _liveUseNativeWallpaperChangeEvents = latestThemeState.UseNativeWallpaperChangeEvents;
        ConfigureSystemWallpaperMonitoring(latestThemeState);
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
        var cornerRadiusStyle = GlobalAppearanceSettings.NormalizeCornerRadiusStyle(themeState.CornerRadiusStyle);
        var cornerRadiusTokens = AppearanceCornerRadiusTokenFactory.Create(cornerRadiusStyle);
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
                ThemeAppearanceValues.NormalizeWallpaperColorSource(themeState.ThemeWallpaperColorSource),
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
            cornerRadiusStyle,
            cornerRadiusTokens,
            resolvedSeedSource,
            palette,
            ResolveAccentColor(themeColorMode, themeState.ThemeColor, palette),
            effectiveSeedColor,
            wallpaperSeedCandidates,
            systemMaterialMode,
            availableModes,
            _windowMaterialService.CanChangeMode,
            themeState.UseSystemChrome,
            resolvedWallpaperPath,
            ThemeAppearanceValues.NormalizeWallpaperColorSource(themeState.ThemeWallpaperColorSource),
            themeState.UseNativeWallpaperChangeEvents);
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
        string wallpaperColorSource,
        string? selectedWallpaperSeed,
        bool queueWallpaperPaletteBuild)
    {
        var source = ResolveWallpaperSeedSource(wallpaperState, wallpaperColorSource);
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

    private WallpaperSeedSourceDescriptor ResolveWallpaperSeedSource(
        WallpaperSettingsState wallpaperState,
        string wallpaperColorSource)
    {
        var normalizedWallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(wallpaperColorSource);

        if (normalizedWallpaperColorSource != ThemeAppearanceValues.WallpaperColorSourceSystem &&
            string.Equals(wallpaperState.Type, "SolidColor", StringComparison.OrdinalIgnoreCase) &&
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
        if (normalizedWallpaperColorSource != ThemeAppearanceValues.WallpaperColorSourceSystem &&
            !string.IsNullOrWhiteSpace(wallpaperPath) &&
            File.Exists(wallpaperPath))
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
        }

        if (normalizedWallpaperColorSource == ThemeAppearanceValues.WallpaperColorSourceApp)
        {
            return new WallpaperSeedSourceDescriptor(
                "fallback",
                "fallback",
                null,
                null,
                null);
        }

        var systemWallpaper = _systemWallpaperService.GetWallpaperPath();
        if (normalizedWallpaperColorSource != ThemeAppearanceValues.WallpaperColorSourceApp &&
            !string.IsNullOrWhiteSpace(systemWallpaper) &&
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
        var materialSnapshot = CreateMaterialColorSnapshot(snapshot);
        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, snapshot);
            MaterialColorChanged?.Invoke(this, materialSnapshot);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Changed?.Invoke(this, snapshot);
            MaterialColorChanged?.Invoke(this, materialSnapshot);
        }, DispatcherPriority.Background);
    }

    private MaterialColorSnapshot CreateMaterialColorSnapshot(AppearanceThemeSnapshot snapshot)
    {
        var context = CreateThemeContext(snapshot);
        var appPalette = ThemeColorSystemService.BuildPalette(context);
        var palette = new LanMountainDesktop.Models.MaterialColorPalette(
            appPalette.Primary,
            appPalette.Secondary,
            appPalette.Accent,
            appPalette.OnAccent,
            appPalette.AccentLight1,
            appPalette.AccentLight2,
            appPalette.AccentLight3,
            appPalette.AccentDark1,
            appPalette.AccentDark2,
            appPalette.AccentDark3,
            appPalette.SurfaceBase,
            appPalette.SurfaceRaised,
            appPalette.SurfaceOverlay,
            appPalette.TextPrimary,
            appPalette.TextSecondary,
            appPalette.TextMuted,
            appPalette.TextAccent,
            appPalette.NavText,
            appPalette.NavSelectedText,
            appPalette.NavSelectionIndicator,
            appPalette.NavItemBackground,
            appPalette.NavItemHoverBackground,
            appPalette.NavItemSelectedBackground,
            appPalette.ToggleOn,
            appPalette.ToggleOff,
            appPalette.ToggleBorder);
        var surfaces = Enum.GetValues<MaterialSurfaceRole>()
            .Select(role =>
            {
                var surface = _materialSurfaceService.GetSurface(context, role);
                return new MaterialSurfaceSnapshot(
                    role,
                    surface.BackgroundColor,
                    surface.BorderColor,
                    surface.BlurRadius,
                    surface.Opacity);
            })
            .ToDictionary(surface => surface.Role);

        return new MaterialColorSnapshot(
            snapshot.IsNightMode,
            snapshot.ThemeColorMode,
            snapshot.ThemeWallpaperColorSource,
            ResolveMaterialColorSourceKind(snapshot),
            snapshot.ResolvedSeedSource,
            snapshot.CornerRadiusTokens,
            snapshot.UserThemeColor,
            snapshot.SelectedWallpaperSeed,
            snapshot.EffectiveSeedColor,
            snapshot.AccentColor,
            snapshot.MonetPalette,
            palette,
            snapshot.WallpaperSeedCandidates,
            snapshot.SystemMaterialMode,
            snapshot.AvailableSystemMaterialModes,
            snapshot.CanChangeSystemMaterial,
            snapshot.UseSystemChrome,
            snapshot.ResolvedWallpaperPath,
            snapshot.UseNativeWallpaperChangeEvents,
            _nativeWallpaperEventsActive,
            _wallpaperPollingActive,
            surfaces);
    }

    private static MaterialColorSourceKind ResolveMaterialColorSourceKind(AppearanceThemeSnapshot snapshot)
    {
        if (string.Equals(snapshot.ThemeColorMode, ThemeAppearanceValues.ColorModeDefaultNeutral, StringComparison.OrdinalIgnoreCase))
        {
            return MaterialColorSourceKind.Neutral;
        }

        if (string.Equals(snapshot.ThemeColorMode, ThemeAppearanceValues.ColorModeSeedMonet, StringComparison.OrdinalIgnoreCase))
        {
            return MaterialColorSourceKind.CustomSeed;
        }

        if (!string.Equals(snapshot.ThemeColorMode, ThemeAppearanceValues.ColorModeWallpaperMonet, StringComparison.OrdinalIgnoreCase))
        {
            return MaterialColorSourceKind.Fallback;
        }

        if (string.Equals(snapshot.ResolvedSeedSource, "app_wallpaper", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.ResolvedSeedSource, "app_solid", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(snapshot.ThemeWallpaperColorSource, ThemeAppearanceValues.WallpaperColorSourceApp, StringComparison.OrdinalIgnoreCase)
                ? MaterialColorSourceKind.AppWallpaper
                : MaterialColorSourceKind.WallpaperAuto;
        }

        if (string.Equals(snapshot.ResolvedSeedSource, "system_wallpaper", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(snapshot.ThemeWallpaperColorSource, ThemeAppearanceValues.WallpaperColorSourceSystem, StringComparison.OrdinalIgnoreCase)
                ? MaterialColorSourceKind.SystemWallpaper
                : MaterialColorSourceKind.WallpaperAuto;
        }

        return MaterialColorSourceKind.Fallback;
    }

    private void ConfigureSystemWallpaperMonitoring(ThemeAppearanceSettingsState themeState)
    {
        var colorMode = ThemeAppearanceValues.NormalizeThemeColorMode(themeState.ThemeColorMode, themeState.ThemeColor);
        var wallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(themeState.ThemeWallpaperColorSource);
        var shouldMonitor =
            string.Equals(colorMode, ThemeAppearanceValues.ColorModeWallpaperMonet, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(wallpaperColorSource, ThemeAppearanceValues.WallpaperColorSourceApp, StringComparison.OrdinalIgnoreCase);

        if (!shouldMonitor)
        {
            StopSystemWallpaperMonitoring();
            return;
        }

        ConfigureNativeWallpaperEvents(themeState.UseNativeWallpaperChangeEvents);
        ConfigureWallpaperPolling(_settingsFacade.Wallpaper.Get().SystemWallpaperRefreshIntervalSeconds);
        UpdateObservedWallpaperSourceKey();
    }

    private void ConfigureNativeWallpaperEvents(bool enabled)
    {
        if (!enabled || !OperatingSystem.IsWindows())
        {
            UnregisterNativeWallpaperEvents();
            return;
        }

        if (_nativeWallpaperEventsActive)
        {
            return;
        }

        RegisterNativeWallpaperEvents();
    }

    private void UnregisterNativeWallpaperEvents()
    {
        if (!_nativeWallpaperEventsActive)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            UnregisterNativeWallpaperEventsCore();
        }

        _nativeWallpaperEventsActive = false;
    }

    [SupportedOSPlatform("windows")]
    private void RegisterNativeWallpaperEvents()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += OnNativeWallpaperPreferenceChanged;
            _nativeWallpaperEventsActive = true;
        }
        catch (Exception ex)
        {
            _nativeWallpaperEventsActive = false;
            AppLogger.Warn("Appearance.WallpaperMonitor", "Failed to subscribe to native wallpaper change events; polling will remain active.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private void UnregisterNativeWallpaperEventsCore()
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= OnNativeWallpaperPreferenceChanged;
        }
        catch
        {
            // Ignore shutdown-time native event cleanup failures.
        }
    }

    private void ConfigureWallpaperPolling(int intervalSeconds)
    {
        var normalizedInterval = Math.Clamp(intervalSeconds <= 0 ? 300 : intervalSeconds, 30, 86400);
        var interval = TimeSpan.FromSeconds(normalizedInterval);
        _systemWallpaperPollTimer ??= new Timer(OnSystemWallpaperPollTimer);
        _systemWallpaperPollTimer.Change(interval, interval);
        _wallpaperPollingActive = true;
    }

    private void StopSystemWallpaperMonitoring()
    {
        UnregisterNativeWallpaperEvents();
        _systemWallpaperPollTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _wallpaperPollingActive = false;
        _lastObservedWallpaperSourceKey = null;
    }

    private void OnNativeWallpaperPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        _ = sender;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
        {
            RefreshWallpaperColors();
        }
    }

    private void OnSystemWallpaperPollTimer(object? state)
    {
        _ = state;

        try
        {
            var source = ResolveWallpaperSeedSource(_settingsFacade.Wallpaper.Get(), _liveThemeWallpaperColorSource);
            var sourceKey = source.SourceKey;
            if (string.Equals(_lastObservedWallpaperSourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastObservedWallpaperSourceKey = sourceKey;
            RefreshWallpaperColors();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Appearance.WallpaperMonitor", "Failed to poll wallpaper color source.", ex);
        }
    }

    private void UpdateObservedWallpaperSourceKey()
    {
        try
        {
            _lastObservedWallpaperSourceKey = ResolveWallpaperSeedSource(
                _settingsFacade.Wallpaper.Get(),
                _liveThemeWallpaperColorSource).SourceKey;
        }
        catch
        {
            _lastObservedWallpaperSourceKey = null;
        }
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

internal static class HostMaterialColorProvider
{
    public static IMaterialColorService GetOrCreate()
    {
        return (IMaterialColorService)HostAppearanceThemeProvider.GetOrCreate();
    }
}
