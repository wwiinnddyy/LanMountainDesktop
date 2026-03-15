using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private TextBlock? CurrentRenderBackendLabelTextBlock => this.FindControl<TextBlock>("CurrentRenderBackendLabelTextBlock");
    private TextBlock? CurrentRenderBackendValueTextBlock => this.FindControl<TextBlock>("CurrentRenderBackendValueTextBlock");
    private TextBlock? CurrentRenderBackendImplementationTextBlock => this.FindControl<TextBlock>("CurrentRenderBackendImplementationTextBlock");
    private ComboBox? TimeZoneComboBox => this.FindControl<ComboBox>("TimeZoneComboBox");
    private SettingsExpander? LauncherHiddenItemsSettingsExpander => this.FindControl<SettingsExpander>("LauncherHiddenItemsSettingsExpander");
    private TextBlock? LauncherHiddenItemsEmptyTextBlock => this.FindControl<TextBlock>("LauncherHiddenItemsEmptyTextBlock");

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope == SettingsScope.App && e.ChangedKeys is { Count: > 0 })
        {
            var changedKeys = e.ChangedKeys.ToArray();
            if (changedKeys.All(key =>
                string.Equals(key, nameof(AppSettingsSnapshot.ThemeColorMode), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, nameof(AppSettingsSnapshot.SystemMaterialMode), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, nameof(AppSettingsSnapshot.SelectedWallpaperSeed), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

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
        ComponentLibraryTitleTextBlock.Text = L("component_library.title", "Widgets");
        LauncherTitleTextBlock.Text = L("launcher.title", "App Launcher");
        LauncherSubtitleTextBlock.Text = OperatingSystem.IsLinux()
            ? L("launcher.subtitle_linux", "Displays installed apps discovered from Linux desktop entries.")
            : L("launcher.subtitle", "Displays all apps and folders based on the Windows Start menu structure.");
        RefreshTaskbarProfilePresentation();

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
        _weatherIconPackId = string.IsNullOrWhiteSpace(snapshot.WeatherIconPackId) ? "HyperOS3" : snapshot.WeatherIconPackId;
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
        var appearanceSnapshot = _appearanceThemeService.GetCurrent();

        return new ThemeColorContext(
            appearanceSnapshot.AccentColor,
            IsLightBackground: !appearanceSnapshot.IsNightMode,
            IsLightNavBackground: !appearanceSnapshot.IsNightMode,
            IsNightMode: appearanceSnapshot.IsNightMode,
            MonetPalette: appearanceSnapshot.MonetPalette,
            MonetColors: appearanceSnapshot.MonetPalette.MonetColors,
            UseNeutralSurfaces: string.Equals(
                appearanceSnapshot.ThemeColorMode,
                ThemeAppearanceValues.ColorModeDefaultNeutral,
                StringComparison.OrdinalIgnoreCase),
            SystemMaterialMode: appearanceSnapshot.SystemMaterialMode);
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

        _defaultDesktopBackground = CreateNeutralWallpaperFallbackBrush();
    }

    private void TryRestoreWallpaper(
        string? savedWallpaperPath,
        string? type = null,
        string? color = null,
        string? placement = null)
    {
        _wallpaperPath = string.IsNullOrWhiteSpace(savedWallpaperPath) ? null : savedWallpaperPath;
        _wallpaperType = string.IsNullOrWhiteSpace(type) ? "Image" : type.Trim();
        _wallpaperPlacement = WallpaperImageBrushFactory.NormalizePlacement(placement);
        _wallpaperSolidColor = TryParseColor(color, out var parsedColor) ? parsedColor : null;
        _wallpaperVideoPath = null;
        _wallpaperDisplayState = WallpaperDisplayState.NoWallpaperConfigured;

        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;

        if (string.Equals(_wallpaperType, "SolidColor", StringComparison.OrdinalIgnoreCase))
        {
            _wallpaperMediaType = WallpaperMediaType.SolidColor;
            _wallpaperDisplayState = _wallpaperSolidColor.HasValue
                ? WallpaperDisplayState.CurrentValidWallpaper
                : WallpaperDisplayState.NoWallpaperConfigured;
            return;
        }

        if (string.IsNullOrWhiteSpace(_wallpaperPath))
        {
            _wallpaperMediaType = WallpaperMediaType.None;
            return;
        }

        var extension = Path.GetExtension(_wallpaperPath);
        var requestedTypeIsVideo = string.Equals(_wallpaperType, "Video", StringComparison.OrdinalIgnoreCase);
        if (SupportedVideoExtensions.Contains(extension) || requestedTypeIsVideo)
        {
            _wallpaperMediaType = WallpaperMediaType.Video;
            _wallpaperVideoPath = _wallpaperPath;
            _wallpaperDisplayState = File.Exists(_wallpaperPath)
                ? WallpaperDisplayState.CurrentValidWallpaper
                : WallpaperDisplayState.TemporarilyUnavailable;
            return;
        }

        if (!SupportedImageExtensions.Contains(extension))
        {
            _wallpaperMediaType = WallpaperMediaType.Image;
            _wallpaperDisplayState = WallpaperDisplayState.TemporarilyUnavailable;
            return;
        }

        if (!File.Exists(_wallpaperPath))
        {
            _wallpaperMediaType = WallpaperMediaType.Image;
            _wallpaperDisplayState = WallpaperDisplayState.TemporarilyUnavailable;
            return;
        }

        try
        {
            using var stream = File.OpenRead(_wallpaperPath);
            _wallpaperBitmap = new Bitmap(stream);
            _wallpaperMediaType = WallpaperMediaType.Image;
            _wallpaperDisplayState = WallpaperDisplayState.CurrentValidWallpaper;
            CacheLastValidWallpaperBitmap(_wallpaperPath);
        }
        catch
        {
            _wallpaperMediaType = WallpaperMediaType.Image;
            _wallpaperDisplayState = WallpaperDisplayState.TemporarilyUnavailable;
            _wallpaperBitmap?.Dispose();
            _wallpaperBitmap = null;
        }
    }

    private void ApplyWallpaperBrush()
    {
        DesktopWallpaperImageLayer.Background = null;
        DesktopWallpaperImageLayer.IsVisible = false;

        if (_wallpaperMediaType == WallpaperMediaType.SolidColor && _wallpaperSolidColor.HasValue)
        {
            DesktopWallpaperLayer.Background = new SolidColorBrush(_wallpaperSolidColor.Value);
            ApplyVideoWallpaperPosterVisibility(showPoster: false);
            return;
        }

        if (_wallpaperDisplayState == WallpaperDisplayState.CurrentValidWallpaper &&
            _wallpaperMediaType == WallpaperMediaType.Image &&
            _wallpaperBitmap is not null)
        {
            DesktopWallpaperLayer.Background = _defaultDesktopBackground ?? CreateNeutralWallpaperFallbackBrush();
            DesktopWallpaperImageLayer.Background = WallpaperImageBrushFactory.Create(_wallpaperBitmap, _wallpaperPlacement);
            DesktopWallpaperImageLayer.IsVisible = true;
            ApplyVideoWallpaperPosterVisibility(showPoster: false);
            return;
        }

        if (_wallpaperDisplayState == WallpaperDisplayState.TemporarilyUnavailable &&
            _lastValidWallpaperBitmap is not null &&
            !string.IsNullOrWhiteSpace(_wallpaperPath) &&
            string.Equals(_lastValidWallpaperPath, _wallpaperPath, StringComparison.OrdinalIgnoreCase))
        {
            DesktopWallpaperLayer.Background = _defaultDesktopBackground ?? CreateNeutralWallpaperFallbackBrush();
            DesktopWallpaperImageLayer.Background = WallpaperImageBrushFactory.Create(_lastValidWallpaperBitmap, _wallpaperPlacement);
            DesktopWallpaperImageLayer.IsVisible = true;
            ApplyVideoWallpaperPosterVisibility(showPoster: false);
            return;
        }

        DesktopWallpaperLayer.Background = _defaultDesktopBackground ?? CreateNeutralWallpaperFallbackBrush();
        ApplyVideoWallpaperPosterVisibility(
            showPoster: _wallpaperMediaType == WallpaperMediaType.Video && _videoWallpaperPosterBitmap is not null);
    }

    private void UpdateWallpaperDisplay()
    {
        if (_wallpaperMediaType == WallpaperMediaType.Video)
        {
            if (!string.IsNullOrWhiteSpace(_wallpaperVideoPath))
            {
                StartVideoWallpaper(_wallpaperVideoPath);
            }
        }
        else
        {
            StopVideoWallpaper();
        }

        ApplyWallpaperBrush();
    }

    private void StartVideoWallpaper(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            ApplyVideoWallpaperPosterVisibility(showPoster: _videoWallpaperPosterBitmap is not null);
            return;
        }

        try
        {
            _libVlc ??= new LibVLC();
            _videoWallpaperPlayer ??= new MediaPlayer(_libVlc);

            if (_videoWallpaperMedia?.Mrl != videoPath)
            {
                _videoWallpaperMedia?.Dispose();
                _videoWallpaperMedia = new Media(_libVlc, new Uri(videoPath));
                _videoWallpaperPlayer.Media = _videoWallpaperMedia;
            }

            if (DesktopVideoWallpaperView is { } videoView)
            {
                videoView.MediaPlayer = _videoWallpaperPlayer;
                videoView.IsVisible = true;
            }

            if (!string.Equals(_videoWallpaperPosterPath, videoPath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyVideoWallpaperPosterVisibility(showPoster: false);
            }
            else
            {
                ApplyVideoWallpaperPosterVisibility(showPoster: _videoWallpaperPosterBitmap is not null);
            }

            if (!_videoWallpaperPlayer.IsPlaying)
            {
                _videoWallpaperPlayer.Play();
            }

            TryCaptureVideoWallpaperPosterFrame(videoPath);
        }
        catch
        {
            ApplyVideoWallpaperPosterVisibility(showPoster: _videoWallpaperPosterBitmap is not null);
        }
    }

    private void StopVideoWallpaper()
    {
        if (DesktopVideoWallpaperView is { } videoView)
        {
            videoView.IsVisible = false;
        }

        _videoWallpaperPlayer?.Stop();
        _wallpaperVideoPath = null;
        ApplyVideoWallpaperPosterVisibility(showPoster: false);
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

        var snapshot = _appearanceThemeService.GetCurrent();
        _recommendedColors = snapshot.MonetPalette.RecommendedColors;
        _monetColors = snapshot.MonetPalette.MonetColors;
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
        var versionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        if (!Version.TryParse(versionText, out var currentVersion))
        {
            currentVersion = new Version(0, 0, 0);
        }

        var normalizedVersion = new Version(
            Math.Max(0, currentVersion.Major),
            Math.Max(0, currentVersion.Minor),
            Math.Max(0, currentVersion.Build));

        DispatcherTimer.RunOnce(
            async () =>
            {
                try
                {
                    await HostUpdateWorkflowServiceProvider
                        .GetOrCreate()
                        .AutoCheckIfEnabledAsync(normalizedVersion);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("UpdateWorkflow", "Automatic update check failed after startup.", ex);
                }
            },
            TimeSpan.FromSeconds(3));
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
            if (TryParseColor(snapshot.ThemeColor, out var savedThemeColor))
            {
                _selectedThemeColor = savedThemeColor;
            }

            _isNightMode = snapshot.IsNightMode ?? _isNightMode;
            _defaultDesktopBackground = CreateNeutralWallpaperFallbackBrush();
            TryRestoreWallpaper(
                snapshot.WallpaperPath,
                snapshot.WallpaperType,
                snapshot.WallpaperColor,
                snapshot.WallpaperPlacement);
            if (!snapshot.IsNightMode.HasValue)
            {
                _isNightMode = CalculateCurrentBackgroundLuminance() < LightBackgroundLuminanceThreshold;
            }
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
        var latestWallpaperState = _settingsFacade.Wallpaper.Get();
        var latestWeatherState = _weatherSettingsService.Get();
        var latestUpdateState = _updateSettingsService.Get();
        var latestThemeState = _themeSettingsService.Get();
        return new AppSettingsSnapshot
        {
            GridShortSideCells = _targetShortSideCells,
            GridSpacingPreset = _gridSpacingPreset,
            DesktopEdgeInsetPercent = _desktopEdgeInsetPercent,
            IsNightMode = _isNightMode,
            ThemeColor = latestThemeState.ThemeColor,
            ThemeColorMode = latestThemeState.ThemeColorMode,
            SystemMaterialMode = latestThemeState.SystemMaterialMode,
            SelectedWallpaperSeed = latestThemeState.SelectedWallpaperSeed,
            UseSystemChrome = latestThemeState.UseSystemChrome,
            WallpaperPath = latestWallpaperState.WallpaperPath,
            WallpaperType = latestWallpaperState.Type,
            WallpaperColor = string.Equals(latestWallpaperState.Type, "SolidColor", StringComparison.OrdinalIgnoreCase)
                ? latestWallpaperState.Color
                : null,
            WallpaperPlacement = latestWallpaperState.Placement,
            LanguageCode = _languageCode,
            TimeZoneId = _timeZoneService.CurrentTimeZone.Id,
            WeatherLocationMode = latestWeatherState.LocationMode,
            WeatherLocationKey = latestWeatherState.LocationKey,
            WeatherLocationName = latestWeatherState.LocationName,
            WeatherLatitude = latestWeatherState.Latitude,
            WeatherLongitude = latestWeatherState.Longitude,
            WeatherAutoRefreshLocation = latestWeatherState.AutoRefreshLocation,
            WeatherLocationQuery = latestWeatherState.LocationQuery,
            WeatherExcludedAlerts = latestWeatherState.ExcludedAlerts,
            WeatherIconPackId = latestWeatherState.IconPackId,
            WeatherNoTlsRequests = latestWeatherState.NoTlsRequests,
            AutoStartWithWindows = _autoStartWithWindows,
            AppRenderMode = _selectedAppRenderMode,
            AutoCheckUpdates = latestUpdateState.AutoCheckUpdates,
            IncludePrereleaseUpdates = latestUpdateState.IncludePrereleaseUpdates,
            UpdateChannel = latestUpdateState.UpdateChannel,
            UpdateMode = latestUpdateState.UpdateMode,
            UpdateDownloadSource = latestUpdateState.UpdateDownloadSource,
            UpdateDownloadThreads = latestUpdateState.UpdateDownloadThreads,
            PendingUpdateInstallerPath = latestUpdateState.PendingUpdateInstallerPath,
            PendingUpdateVersion = latestUpdateState.PendingUpdateVersion,
            PendingUpdatePublishedAtUtcMs = latestUpdateState.PendingUpdatePublishedAtUtcMs,
            LastUpdateCheckUtcMs = latestUpdateState.LastUpdateCheckUtcMs,
            TopStatusComponentIds = [.. _topStatusComponentIds],
            PinnedTaskbarActions = [.. _pinnedTaskbarActions.Select(v => v.ToString())],
            EnableDynamicTaskbarActions = _enableDynamicTaskbarActions,
            TaskbarLayoutMode = _taskbarLayoutMode,
            ClockDisplayFormat = _clockDisplayFormat == ClockDisplayFormat.HourMinute ? "HourMinute" : "HourMinuteSecond",
            StatusBarSpacingMode = _statusBarSpacingMode,
            StatusBarCustomSpacingPercent = _statusBarCustomSpacingPercent
        };
    }

    private IBrush CreateNeutralWallpaperFallbackBrush()
    {
        var neutralColor = _isNightMode
            ? Color.Parse("#FF0B0F14")
            : Color.Parse("#FFF6F7F9");
        return new SolidColorBrush(neutralColor);
    }

    private void CacheLastValidWallpaperBitmap(string wallpaperPath)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(wallpaperPath);
            var cachedBitmap = new Bitmap(stream);
            _lastValidWallpaperBitmap?.Dispose();
            _lastValidWallpaperBitmap = cachedBitmap;
            _lastValidWallpaperPath = wallpaperPath;
        }
        catch
        {
            // Best effort cache only.
        }
    }

    private void ApplyVideoWallpaperPosterVisibility(bool showPoster)
    {
        if (DesktopVideoWallpaperImage is not { } posterImage)
        {
            return;
        }

        if (!showPoster ||
            _videoWallpaperPosterBitmap is null ||
            !string.Equals(_videoWallpaperPosterPath, _wallpaperVideoPath, StringComparison.OrdinalIgnoreCase))
        {
            posterImage.IsVisible = false;
            return;
        }

        posterImage.Source = _videoWallpaperPosterBitmap;
        posterImage.IsVisible = true;
    }

    private void TryCaptureVideoWallpaperPosterFrame(string videoPath)
    {
        if (_videoWallpaperPlayer is null || string.IsNullOrWhiteSpace(videoPath))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var snapshotPath = Path.Combine(
                Path.GetTempPath(),
                $"lanmountaindesktop-wallpaper-poster-{Guid.NewGuid():N}.png");

            try
            {
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    await Task.Delay(250).ConfigureAwait(false);

                    if (_wallpaperMediaType != WallpaperMediaType.Video ||
                        !string.Equals(_wallpaperVideoPath, videoPath, StringComparison.OrdinalIgnoreCase) ||
                        _videoWallpaperPlayer is null)
                    {
                        return;
                    }

                    if (!_videoWallpaperPlayer.TakeSnapshot(0, snapshotPath, 640, 360))
                    {
                        continue;
                    }

                    if (!File.Exists(snapshotPath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(snapshotPath);
                    if (fileInfo.Length <= 0)
                    {
                        continue;
                    }

                    Bitmap posterBitmap;
                    await using (var stream = File.OpenRead(snapshotPath))
                    {
                        posterBitmap = new Bitmap(stream);
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_wallpaperMediaType != WallpaperMediaType.Video ||
                            !string.Equals(_wallpaperVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
                        {
                            posterBitmap.Dispose();
                            return;
                        }

                        _videoWallpaperPosterBitmap?.Dispose();
                        _videoWallpaperPosterBitmap = posterBitmap;
                        _videoWallpaperPosterPath = videoPath;
                        ApplyVideoWallpaperPosterVisibility(showPoster: true);
                    });

                    return;
                }
            }
            catch
            {
                // Best effort poster capture only.
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
        });
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
