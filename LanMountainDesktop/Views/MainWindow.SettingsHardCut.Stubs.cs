using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private TextBlock? CurrentRenderBackendLabelTextBlock => this.FindControl<TextBlock>("CurrentRenderBackendLabelTextBlock");
    private TextBlock? CurrentRenderBackendValueTextBlock => this.FindControl<TextBlock>("CurrentRenderBackendValueTextBlock");
    private TextBlock? CurrentRenderBackendImplementationTextBlock => this.FindControl<TextBlock>("CurrentRenderBackendImplementationTextBlock");
    private Slider? GridSizeSlider => this.FindControl<Slider>("GridSizeSlider");
    private NumberBox? GridSizeNumberBox => this.FindControl<NumberBox>("GridSizeNumberBox");
    private Slider? GridEdgeInsetSlider => this.FindControl<Slider>("GridEdgeInsetSlider");
    private NumberBox? GridEdgeInsetNumberBox => this.FindControl<NumberBox>("GridEdgeInsetNumberBox");
    private TextBlock? GridEdgeInsetComputedPxTextBlock => this.FindControl<TextBlock>("GridEdgeInsetComputedPxTextBlock");
    private TextBlock? GridInfoTextBlock => this.FindControl<TextBlock>("GridInfoTextBlock");
    private ComboBox? GridSpacingPresetComboBox => this.FindControl<ComboBox>("GridSpacingPresetComboBox");
    private Border? GridPreviewHost => this.FindControl<Border>("GridPreviewHost");
    private Border? GridPreviewFrame => this.FindControl<Border>("GridPreviewFrame");
    private Border? GridPreviewViewport => this.FindControl<Border>("GridPreviewViewport");
    private Grid? GridPreviewGrid => this.FindControl<Grid>("GridPreviewGrid");
    private Canvas? GridPreviewLinesCanvas => this.FindControl<Canvas>("GridPreviewLinesCanvas");
    private Border? GridPreviewTopStatusBarHost => this.FindControl<Border>("GridPreviewTopStatusBarHost");
    private StackPanel? GridPreviewTopStatusComponentsPanel => this.FindControl<StackPanel>("GridPreviewTopStatusComponentsPanel");
    private Border? GridPreviewBottomTaskbarContainer => this.FindControl<Border>("GridPreviewBottomTaskbarContainer");
    private StackPanel? GridPreviewBackButtonVisual => this.FindControl<StackPanel>("GridPreviewBackButtonVisual");
    private TextBlock? GridPreviewBackButtonTextBlock => this.FindControl<TextBlock>("GridPreviewBackButtonTextBlock");
    private StackPanel? GridPreviewComponentLibraryVisual => this.FindControl<StackPanel>("GridPreviewComponentLibraryVisual");
    private FluentIcons.Avalonia.FluentIcon? GridPreviewComponentLibraryIcon => this.FindControl<FluentIcons.Avalonia.FluentIcon>("GridPreviewComponentLibraryIcon");
    private TextBlock? GridPreviewComponentLibraryTextBlock => this.FindControl<TextBlock>("GridPreviewComponentLibraryTextBlock");
    private FluentIcons.Avalonia.SymbolIcon? GridPreviewSettingsButtonIcon => this.FindControl<FluentIcons.Avalonia.SymbolIcon>("GridPreviewSettingsButtonIcon");
    private Border? WallpaperPreviewHost => this.FindControl<Border>("WallpaperPreviewHost");
    private Border? WallpaperPreviewFrame => this.FindControl<Border>("WallpaperPreviewFrame");
    private Border? WallpaperPreviewViewport => this.FindControl<Border>("WallpaperPreviewViewport");
    private Grid? WallpaperPreviewGrid => this.FindControl<Grid>("WallpaperPreviewGrid");
    private Border? WallpaperPreviewTopStatusBarHost => this.FindControl<Border>("WallpaperPreviewTopStatusBarHost");
    private StackPanel? WallpaperPreviewTopStatusComponentsPanel => this.FindControl<StackPanel>("WallpaperPreviewTopStatusComponentsPanel");
    private Border? WallpaperPreviewBottomTaskbarContainer => this.FindControl<Border>("WallpaperPreviewBottomTaskbarContainer");
    private ClockWidget? WallpaperPreviewClockWidget => this.FindControl<ClockWidget>("WallpaperPreviewClockWidget");
    private StackPanel? WallpaperPreviewBackButtonVisual => this.FindControl<StackPanel>("WallpaperPreviewBackButtonVisual");
    private TextBlock? WallpaperPreviewBackButtonTextBlock => this.FindControl<TextBlock>("WallpaperPreviewBackButtonTextBlock");
    private StackPanel? WallpaperPreviewComponentLibraryVisual => this.FindControl<StackPanel>("WallpaperPreviewComponentLibraryVisual");
    private TextBlock? WallpaperPreviewComponentLibraryTextBlock => this.FindControl<TextBlock>("WallpaperPreviewComponentLibraryTextBlock");
    private FluentIcons.Avalonia.SymbolIcon? WallpaperPreviewSettingsButtonIcon => this.FindControl<FluentIcons.Avalonia.SymbolIcon>("WallpaperPreviewSettingsButtonIcon");
    private ComboBox? StatusBarSpacingModeComboBox => this.FindControl<ComboBox>("StatusBarSpacingModeComboBox");
    private SettingsExpanderItem? StatusBarSpacingCustomPanel => this.FindControl<SettingsExpanderItem>("StatusBarSpacingCustomPanel");
    private Slider? StatusBarSpacingSlider => this.FindControl<Slider>("StatusBarSpacingSlider");
    private NumberBox? StatusBarSpacingNumberBox => this.FindControl<NumberBox>("StatusBarSpacingNumberBox");
    private TextBlock? StatusBarSpacingComputedPxTextBlock => this.FindControl<TextBlock>("StatusBarSpacingComputedPxTextBlock");
    private ComboBox? TimeZoneComboBox => this.FindControl<ComboBox>("TimeZoneComboBox");
    private SettingsExpander? LauncherHiddenItemsSettingsExpander => this.FindControl<SettingsExpander>("LauncherHiddenItemsSettingsExpander");
    private TextBlock? LauncherHiddenItemsEmptyTextBlock => this.FindControl<TextBlock>("LauncherHiddenItemsEmptyTextBlock");

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;
        _ = e;
        ScheduleReloadFromExternalSettings();
    }

    private void ScheduleReloadFromExternalSettings()
    {
        if (_externalSettingsReloadPending)
        {
            return;
        }

        _externalSettingsReloadPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _externalSettingsReloadPending = false;
            ReloadFromPersistedSettings();
        }, TimeSpan.FromMilliseconds(120));
    }

    private void OnNightModeChecked(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyNightModeState(true, refreshPalettes: true);
        SchedulePersistSettings();
    }

    private void OnNightModeUnchecked(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyNightModeState(false, refreshPalettes: true);
        SchedulePersistSettings();
    }

    private void InitializeLocalization(string? languageCode)
    {
        _languageCode = _localizationService.NormalizeLanguageCode(languageCode);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private string Lf(string key, string fallback, params object[] args)
    {
        var template = L(key, fallback);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }

    private void ApplyLocalization()
    {
        Title = L("app.title", "LanMountainDesktop");
        BackToWindowsTextBlock.Text = L("button.back_to_windows", "Back to Windows");
        OpenComponentLibraryTextBlock.Text = L("button.component_library", "Edit Desktop");
        ComponentLibraryTitleTextBlock.Text = L("component_library.title", "Widgets");
        LauncherTitleTextBlock.Text = L("launcher.title", "App Launcher");
        LauncherSubtitleTextBlock.Text = OperatingSystem.IsLinux()
            ? L("launcher.subtitle_linux", "Displays installed apps discovered from Linux desktop entries.")
            : L("launcher.subtitle", "Displays all apps and folders based on the Windows Start menu structure.");

        UpdateCurrentRenderBackendStatus();
        RenderLauncherHiddenItemsList();
    }

    private string GetLocalizedTimeZoneDisplayName(TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var hours = Math.Abs(offset.Hours);
        var minutes = Math.Abs(offset.Minutes);
        var name = string.IsNullOrWhiteSpace(timeZone.StandardName) ? timeZone.Id : timeZone.StandardName;
        return $"(UTC{sign}{hours:D2}:{minutes:D2}) {name}";
    }

    private void InitializeWeatherSettings(AppSettingsSnapshot snapshot)
    {
        _weatherLocationMode = string.Equals(snapshot.WeatherLocationMode, "Coordinates", StringComparison.OrdinalIgnoreCase)
            ? WeatherLocationMode.Coordinates
            : WeatherLocationMode.CitySearch;
        _weatherLocationKey = snapshot.WeatherLocationKey ?? string.Empty;
        _weatherLocationName = snapshot.WeatherLocationName ?? string.Empty;
        _weatherLatitude = snapshot.WeatherLatitude;
        _weatherLongitude = snapshot.WeatherLongitude;
        _weatherAutoRefreshLocation = snapshot.WeatherAutoRefreshLocation;
        _weatherExcludedAlertsRaw = snapshot.WeatherExcludedAlerts ?? string.Empty;
        _weatherIconPackId = string.IsNullOrWhiteSpace(snapshot.WeatherIconPackId) ? "FluentRegular" : snapshot.WeatherIconPackId;
        _weatherNoTlsRequests = snapshot.WeatherNoTlsRequests;
    }

    private void InitializeAutoStartWithWindowsSetting(AppSettingsSnapshot snapshot)
    {
        _autoStartWithWindows = snapshot.AutoStartWithWindows;
    }

    private void InitializeAppRenderModeSetting(AppSettingsSnapshot snapshot)
    {
        _selectedAppRenderMode = string.IsNullOrWhiteSpace(snapshot.AppRenderMode)
            ? AppRenderingModeHelper.Default
            : snapshot.AppRenderMode;
        _runningAppRenderMode = AppRenderingModeHelper.Normalize(snapshot.AppRenderMode);
    }

    private void InitializeUpdateSettings(AppSettingsSnapshot snapshot)
    {
        _ = snapshot;
        _ = _updateSettingsService.Get();
    }

    private void InitializeSettingsIcons()
    {
    }

    private static bool TryParseColor(string? colorText, out Color color)
    {
        if (!string.IsNullOrWhiteSpace(colorText) && Color.TryParse(colorText, out color))
        {
            return true;
        }

        color = default;
        return false;
    }

    private ThemeColorContext BuildAdaptiveThemeContext()
    {
        return new ThemeColorContext(
            _selectedThemeColor,
            IsLightBackground: !_isNightMode,
            IsLightNavBackground: !_isNightMode,
            IsNightMode: _isNightMode);
    }

    private void ApplyAdaptiveThemeResources()
    {
        var context = BuildAdaptiveThemeContext();
        ThemeColorSystemService.ApplyThemeResources(Resources, context);
        GlassEffectService.ApplyGlassResources(Resources, context);

        if (Application.Current?.Resources is { } applicationResources)
        {
            ThemeColorSystemService.ApplyThemeResources(applicationResources, context);
            GlassEffectService.ApplyGlassResources(applicationResources, context);
        }

        _defaultDesktopBackground = GetThemeBrush("AdaptiveSurfaceBaseBrush");
    }

    private void TryRestoreWallpaper(string? savedWallpaperPath)
    {
        _wallpaperPath = string.IsNullOrWhiteSpace(savedWallpaperPath) ? null : savedWallpaperPath;

        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;

        if (string.IsNullOrWhiteSpace(_wallpaperPath) || !File.Exists(_wallpaperPath))
        {
            _wallpaperMediaType = WallpaperMediaType.None;
            return;
        }

        var extension = Path.GetExtension(_wallpaperPath);
        if (SupportedVideoExtensions.Contains(extension))
        {
            _wallpaperMediaType = WallpaperMediaType.Video;
            _wallpaperVideoPath = _wallpaperPath;
            return;
        }

        if (!SupportedImageExtensions.Contains(extension))
        {
            _wallpaperMediaType = WallpaperMediaType.None;
            _wallpaperPath = null;
            return;
        }

        try
        {
            using var stream = File.OpenRead(_wallpaperPath);
            _wallpaperBitmap = new Bitmap(stream);
            _wallpaperMediaType = WallpaperMediaType.Image;
        }
        catch
        {
            _wallpaperMediaType = WallpaperMediaType.None;
            _wallpaperPath = null;
            _wallpaperBitmap?.Dispose();
            _wallpaperBitmap = null;
        }
    }

    private void ApplyWallpaperBrush()
    {
        if (_wallpaperMediaType == WallpaperMediaType.Image && _wallpaperBitmap is not null)
        {
            DesktopWallpaperLayer.Background = new ImageBrush(_wallpaperBitmap)
            {
                Stretch = Stretch.UniformToFill
            };
            return;
        }

        DesktopWallpaperLayer.Background = _defaultDesktopBackground ?? Brushes.Transparent;
    }

    private void UpdateWallpaperDisplay()
    {
        ApplyWallpaperBrush();
    }

    private void StopVideoWallpaper()
    {
        _wallpaperVideoPath = null;
        if (_wallpaperMediaType == WallpaperMediaType.Video)
        {
            _wallpaperMediaType = WallpaperMediaType.None;
        }
    }

    private double CalculateCurrentBackgroundLuminance()
    {
        var brush = DesktopWallpaperLayer.Background;
        if (brush is SolidColorBrush solid)
        {
            return CalculateRelativeLuminance(solid.Color);
        }

        return CalculateRelativeLuminance(_selectedThemeColor);
    }

    private void ApplyNightModeState(bool enabled, bool refreshPalettes)
    {
        _isNightMode = enabled;
        var requestedThemeVariant = enabled ? ThemeVariant.Dark : ThemeVariant.Light;
        RequestedThemeVariant = requestedThemeVariant;
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = requestedThemeVariant;
        }

        ApplyAdaptiveThemeResources();
        ApplyWallpaperBrush();

        if (!refreshPalettes)
        {
            return;
        }

        var palette = _themeSettingsService.BuildPalette(enabled, _wallpaperPath);
        _recommendedColors = palette.RecommendedColors;
        _monetColors = palette.MonetColors;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        return CalculateRelativeLuminance(color.R / 255d, color.G / 255d, color.B / 255d);
    }

    private static double CalculateRelativeLuminance(double red, double green, double blue)
    {
        static double ToLinear(double value) =>
            value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        return 0.2126 * ToLinear(red) + 0.7152 * ToLinear(green) + 0.0722 * ToLinear(blue);
    }

    private void TriggerAutoUpdateCheckIfEnabled()
    {
    }

    private void PersistSettings()
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        try
        {
            _settingsService.SaveSnapshot(SettingsScope.App, BuildAppSettingsSnapshot());
            _componentLayoutStore.SaveLayout(BuildDesktopLayoutSettingsSnapshot());
            _settingsService.SaveSnapshot(SettingsScope.Launcher, BuildLauncherSettingsSnapshot());
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SettingsRuntime", "Failed to persist settings.", ex);
        }
    }

    private void SchedulePersistSettings(int delayMs = 200)
    {
        DispatcherTimer.RunOnce(PersistSettings, TimeSpan.FromMilliseconds(Math.Max(0, delayMs)));
    }

    internal void ReloadFromPersistedSettings()
    {
        var snapshot = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var layoutSnapshot = _componentLayoutStore.LoadLayout();
        var launcherSnapshot = _settingsService.LoadSnapshot<LauncherSettingsSnapshot>(SettingsScope.Launcher);
        _suppressSettingsPersistence = true;
        try
        {
            InitializeLocalization(snapshot.LanguageCode);
            if (string.IsNullOrWhiteSpace(snapshot.TimeZoneId))
            {
                _timeZoneService.CurrentTimeZone = TimeZoneInfo.Local;
            }
            else
            {
                _timeZoneService.SetTimeZoneById(snapshot.TimeZoneId);
            }

            _targetShortSideCells = Math.Clamp(
                snapshot.GridShortSideCells > 0 ? snapshot.GridShortSideCells : CalculateDefaultShortSideCellCountFromDpi(),
                MinShortSideCells,
                MaxShortSideCells);
            _gridSpacingPreset = _gridSettingsService.NormalizeSpacingPreset(snapshot.GridSpacingPreset);
            _desktopEdgeInsetPercent = Math.Clamp(snapshot.DesktopEdgeInsetPercent, MinEdgeInsetPercent, MaxEdgeInsetPercent);
            _statusBarSpacingMode = NormalizeStatusBarSpacingMode(snapshot.StatusBarSpacingMode);
            _statusBarCustomSpacingPercent = Math.Clamp(snapshot.StatusBarCustomSpacingPercent, 0, 30);
            ApplyTaskbarSettings(snapshot);
            InitializeWeatherSettings(snapshot);
            InitializeAutoStartWithWindowsSetting(snapshot);
            InitializeAppRenderModeSetting(snapshot);
            InitializeUpdateSettings(snapshot);
            InitializeDesktopSurfaceState(layoutSnapshot);
            InitializeLauncherVisibilitySettings(launcherSnapshot);
            InitializeDesktopComponentPlacements(layoutSnapshot);
            TryRestoreWallpaper(snapshot.WallpaperPath);
            if (TryParseColor(snapshot.ThemeColor, out var savedThemeColor))
            {
                _selectedThemeColor = savedThemeColor;
            }

            _isNightMode = snapshot.IsNightMode ?? (CalculateCurrentBackgroundLuminance() < LightBackgroundLuminanceThreshold);
            ApplyNightModeState(_isNightMode, refreshPalettes: true);
            ApplyWallpaperBrush();
            UpdateWallpaperDisplay();
            InitializeTimeZoneSettings();
            ApplyLocalization();
            RebuildDesktopGrid();
        }
        finally
        {
            _suppressSettingsPersistence = false;
        }
    }

    private AppSettingsSnapshot BuildAppSettingsSnapshot()
    {
        return new AppSettingsSnapshot
        {
            GridShortSideCells = _targetShortSideCells,
            GridSpacingPreset = _gridSpacingPreset,
            DesktopEdgeInsetPercent = _desktopEdgeInsetPercent,
            IsNightMode = _isNightMode,
            ThemeColor = _selectedThemeColor.ToString(),
            WallpaperPath = _wallpaperPath,
            LanguageCode = _languageCode,
            TimeZoneId = _timeZoneService.CurrentTimeZone.Id,
            WeatherLocationMode = _weatherLocationMode.ToString(),
            WeatherLocationKey = _weatherLocationKey,
            WeatherLocationName = _weatherLocationName,
            WeatherLatitude = _weatherLatitude,
            WeatherLongitude = _weatherLongitude,
            WeatherAutoRefreshLocation = _weatherAutoRefreshLocation,
            WeatherExcludedAlerts = _weatherExcludedAlertsRaw,
            WeatherIconPackId = _weatherIconPackId,
            WeatherNoTlsRequests = _weatherNoTlsRequests,
            AutoStartWithWindows = _autoStartWithWindows,
            AppRenderMode = _selectedAppRenderMode,
            TopStatusComponentIds = [.. _topStatusComponentIds],
            PinnedTaskbarActions = [.. _pinnedTaskbarActions.Select(v => v.ToString())],
            EnableDynamicTaskbarActions = _enableDynamicTaskbarActions,
            TaskbarLayoutMode = _taskbarLayoutMode,
            ClockDisplayFormat = _clockDisplayFormat == ClockDisplayFormat.HourMinute ? "HourMinute" : "HourMinuteSecond",
            StatusBarSpacingMode = _statusBarSpacingMode,
            StatusBarCustomSpacingPercent = _statusBarCustomSpacingPercent
        };
    }

    private DesktopLayoutSettingsSnapshot BuildDesktopLayoutSettingsSnapshot()
    {
        return new DesktopLayoutSettingsSnapshot
        {
            DesktopPageCount = _desktopPageCount,
            CurrentDesktopSurfaceIndex = _currentDesktopSurfaceIndex,
            DesktopComponentPlacements = [.. _desktopComponentPlacements]
        };
    }

    private LauncherSettingsSnapshot BuildLauncherSettingsSnapshot()
    {
        return new LauncherSettingsSnapshot
        {
            HiddenLauncherAppPaths = [.. _hiddenLauncherAppPaths],
            HiddenLauncherFolderPaths = [.. _hiddenLauncherFolderPaths]
        };
    }
}
