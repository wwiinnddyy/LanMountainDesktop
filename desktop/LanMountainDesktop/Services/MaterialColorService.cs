using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;
using LanMountainDesktop.Theme;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

internal sealed class MaterialColorService : IMaterialColorService, IDisposable
{
    private static readonly Color DefaultAccentColor = Color.Parse("#FF3B82F6");
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IWindowMaterialService _windowMaterialService;
    private readonly IMaterialSurfaceService _materialSurfaceService;
    private readonly MonetColorService _monetColorService = new();
    private readonly WallpaperColorPipeline _wallpaperColorPipeline;
    private string _liveThemeColorMode;
    private string _liveSystemMaterialMode;
    private string? _liveSelectedWallpaperSeed;
    private string _liveThemeWallpaperColorSource;
    private bool _liveUseNativeWallpaperChangeEvents;
    private Timer? _systemWallpaperPollTimer;
    private string? _lastObservedWallpaperSourceKey;
    private bool _nativeWallpaperEventsActive;
    private bool _wallpaperPollingActive;

    public MaterialColorService(
        ISettingsFacadeService settingsFacade,
        ISystemWallpaperProvider systemWallpaperProvider,
        IWindowMaterialService windowMaterialService,
        IMaterialSurfaceService materialSurfaceService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _windowMaterialService = windowMaterialService ?? throw new ArgumentNullException(nameof(windowMaterialService));
        _materialSurfaceService = materialSurfaceService ?? throw new ArgumentNullException(nameof(materialSurfaceService));
        _wallpaperColorPipeline = new WallpaperColorPipeline(
            _settingsFacade,
            systemWallpaperProvider ?? throw new ArgumentNullException(nameof(systemWallpaperProvider)),
            _monetColorService,
            RaiseChanged);
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

    internal event EventHandler<AppearanceThemeSnapshot>? AppearanceThemeChanged;

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
        _wallpaperColorPipeline.Clear();
        _lastObservedWallpaperSourceKey = null;
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
            var wallpaperResolution = _wallpaperColorPipeline.Resolve(
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

    private void RaiseChanged(bool queueWallpaperPaletteBuild)
    {
        var snapshot = BuildCurrentSnapshot(queueWallpaperPaletteBuild);
        var materialSnapshot = CreateMaterialColorSnapshot(snapshot);
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppearanceThemeChanged?.Invoke(this, snapshot);
            MaterialColorChanged?.Invoke(this, materialSnapshot);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            AppearanceThemeChanged?.Invoke(this, snapshot);
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
            var source = _wallpaperColorPipeline.ResolveSource(_settingsFacade.Wallpaper.Get(), _liveThemeWallpaperColorSource);
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
            _lastObservedWallpaperSourceKey = _wallpaperColorPipeline.ResolveSource(
                _settingsFacade.Wallpaper.Get(),
                _liveThemeWallpaperColorSource).SourceKey;
        }
        catch
        {
            _lastObservedWallpaperSourceKey = null;
        }
    }


}
