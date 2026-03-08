using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    protected override void OnClosed(EventArgs e)
    {
        _persistSettingsDebounceTimer?.Dispose();
        _persistSettingsDebounceTimer = null;

        StopVideoWallpaper();
        _previewVideoWallpaperPlayer?.Dispose();
        _previewVideoWallpaperPlayer = null;
        _previewVideoWallpaperMedia?.Dispose();
        _previewVideoWallpaperMedia = null;
        _libVlc?.Dispose();
        _libVlc = null;

        _releaseUpdateService.Dispose();
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _launcherFolderIconBitmap?.Dispose();
        _launcherFolderIconBitmap = null;

        foreach (var icon in _launcherIconCache.Values)
        {
            icon.Dispose();
        }

        _launcherIconCache.Clear();
        base.OnClosed(e);
    }

    private void OnSettingsNavSelectionChanged(object? sender, FluentAvalonia.UI.Controls.NavigationViewSelectionChangedEventArgs e)
    {
        UpdateSettingsTabContent();
        PersistSettings();
    }

    private int GetSettingsTabIndex()
    {
        if (SettingsNavView?.SelectedItem is FluentAvalonia.UI.Controls.NavigationViewItem item)
        {
            return item.Tag?.ToString() switch
            {
                "Wallpaper" => 0,
                "Grid" => 1,
                "Color" => 2,
                "StatusBar" => 3,
                "Weather" => 4,
                "Region" => 5,
                "Update" => 6,
                "About" => 7,
                "Launcher" => 8,
                "Plugins" => 9,
                _ => 0
            };
        }

        return 0;
    }

    private void UpdateSettingsTabContent()
    {
        if (SettingsNavView is null)
        {
            return;
        }

        var selectedItem = SettingsNavView.SelectedItem as FluentAvalonia.UI.Controls.NavigationViewItem;
        var tag = selectedItem?.Tag?.ToString();

        WallpaperSettingsPanel.IsVisible = tag == "Wallpaper";
        GridSettingsPanel.IsVisible = tag == "Grid";
        ColorSettingsPanel.IsVisible = tag == "Color";
        StatusBarSettingsPanel.IsVisible = tag == "StatusBar";
        WeatherSettingsPanel.IsVisible = tag == "Weather";
        RegionSettingsPanel.IsVisible = tag == "Region";
        UpdateSettingsPanel.IsVisible = tag == "Update";
        AboutSettingsPanel.IsVisible = tag == "About";
        LauncherSettingsPanel.IsVisible = tag == "Launcher";
        PluginSettingsPanel.IsVisible = tag == "Plugins";

        if (tag == "Launcher")
        {
            RenderLauncherHiddenItemsList();
        }

        if (tag == "Grid")
        {
            UpdateGridPreviewLayout();
        }

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private void PersistSettings()
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        _appSettingsService.Save(BuildAppSettingsSnapshot());
        _launcherSettingsService.Save(BuildLauncherSettingsSnapshot());
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
            WallpaperPlacement = GetPlacementDisplayName(GetSelectedWallpaperPlacement()),
            SettingsTabIndex = Math.Max(0, GetSettingsTabIndex()),
            LanguageCode = _languageCode,
            TimeZoneId = _timeZoneService.CurrentTimeZone.Id,
            WeatherLocationMode = ToWeatherLocationModeTag(_weatherLocationMode),
            WeatherLocationKey = _weatherLocationKey,
            WeatherLocationName = _weatherLocationName,
            WeatherLatitude = _weatherLatitude,
            WeatherLongitude = _weatherLongitude,
            WeatherAutoRefreshLocation = _weatherAutoRefreshLocation,
            WeatherLocationQuery = BuildLegacyWeatherLocationQuery(),
            WeatherExcludedAlerts = _weatherExcludedAlertsRaw,
            WeatherIconPackId = _weatherIconPackId,
            WeatherNoTlsRequests = _weatherNoTlsRequests,
            AutoStartWithWindows = _autoStartWithWindows,
            AutoCheckUpdates = _autoCheckUpdates,
            IncludePrereleaseUpdates = IncludePrereleaseUpdates,
            UpdateChannel = IncludePrereleaseUpdates ? UpdateChannelPreview : UpdateChannelStable,
            TopStatusComponentIds = _topStatusComponentIds.ToList(),
            PinnedTaskbarActions = _pinnedTaskbarActions.Select(action => action.ToString()).ToList(),
            EnableDynamicTaskbarActions = _enableDynamicTaskbarActions,
            TaskbarLayoutMode = _taskbarLayoutMode,
            ClockDisplayFormat = _clockDisplayFormat == ClockDisplayFormat.HourMinute ? "HourMinute" : "HourMinuteSecond",
            StatusBarSpacingMode = _statusBarSpacingMode,
            StatusBarCustomSpacingPercent = _statusBarCustomSpacingPercent
        };
    }

    private LauncherSettingsSnapshot BuildLauncherSettingsSnapshot()
    {
        return new LauncherSettingsSnapshot
        {
            HiddenLauncherFolderPaths = _hiddenLauncherFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
            HiddenLauncherAppPaths = _hiddenLauncherAppPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private void SchedulePersistSettings(int delayMs = 200)
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        _persistSettingsDebounceTimer?.Dispose();
        _persistSettingsDebounceTimer = DispatcherTimer.RunOnce(() =>
        {
            _persistSettingsDebounceTimer = null;
            PersistSettings();
        }, TimeSpan.FromMilliseconds(Math.Max(0, delayMs)));
    }

    private int CalculateDefaultShortSideCellCountFromDpi()
    {
        var dpi = 96d * RenderScaling;
        var count = (int)Math.Round(dpi / 8d);
        return Math.Clamp(count, MinShortSideCells, MaxShortSideCells);
    }

    private static string NormalizeGridSpacingPreset(string? value)
    {
        return string.Equals(value, "Compact", StringComparison.OrdinalIgnoreCase)
            ? "Compact"
            : "Relaxed";
    }

    private static string NormalizeStatusBarSpacingMode(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Compact", StringComparison.OrdinalIgnoreCase) => "Compact",
            _ when string.Equals(value, "Custom", StringComparison.OrdinalIgnoreCase) => "Custom",
            _ => "Relaxed"
        };
    }

    private static string? TryGetSelectedComboBoxTag(ComboBox? comboBox)
    {
        if (comboBox?.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString();
        }

        return comboBox?.SelectedItem?.ToString();
    }

    private int ResolveStatusBarSpacingPercent()
    {
        return _statusBarSpacingMode switch
        {
            "Compact" => 6,
            "Custom" => Math.Clamp(_statusBarCustomSpacingPercent, 0, 30),
            _ => 12
        };
    }

    private void ApplyStatusBarComponentSpacingForPanel(StackPanel? panel, double cellSize)
    {
        if (panel is null)
        {
            return;
        }

        var percent = ResolveStatusBarSpacingPercent();
        panel.Spacing = Math.Max(0, cellSize) * (percent / 100d);
    }

    private void UpdateStatusBarSpacingComputedPxText(double cellSize)
    {
        var percent = ResolveStatusBarSpacingPercent();
        var spacingPx = Math.Max(0, cellSize) * (percent / 100d);
        StatusBarSpacingComputedPxTextBlock.Text = Lf(
            "settings.status_bar.spacing_custom_px_format",
            ">= {0:F1}px",
            spacingPx);
    }

    private int ResolvePendingGridEdgeInsetPercent()
    {
        var pending = (int)Math.Round(GridEdgeInsetNumberBox.Value);
        return Math.Clamp(pending, MinEdgeInsetPercent, MaxEdgeInsetPercent);
    }

    private void UpdateGridEdgeInsetComputedPxText(double cellSize)
    {
        var percent = ResolvePendingGridEdgeInsetPercent();
        var insetPx = Math.Clamp(Math.Max(0, cellSize) * (percent / 100d), 0, 80);
        GridEdgeInsetComputedPxTextBlock.Text = Lf("settings.grid.edge_inset_px_format", "{0:F1}px", insetPx);
    }

    private void OnStatusBarClockChecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressStatusBarToggleEvents)
        {
            return;
        }

        _topStatusComponentIds.Add(BuiltInComponentIds.Clock);
        ApplyTopStatusComponentVisibility();
        UpdateWallpaperPreviewLayout();
        PersistSettings();
    }

    private void OnStatusBarClockUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressStatusBarToggleEvents)
        {
            return;
        }

        _topStatusComponentIds.Remove(BuiltInComponentIds.Clock);
        ApplyTopStatusComponentVisibility();
        UpdateWallpaperPreviewLayout();
        PersistSettings();
    }

    private void OnClockFormatChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.Tag is not string formatTag)
        {
            return;
        }

        _clockDisplayFormat = formatTag == "Hm"
            ? ClockDisplayFormat.HourMinute
            : ClockDisplayFormat.HourMinuteSecond;
        ClockWidget.SetDisplayFormat(_clockDisplayFormat);
        WallpaperPreviewClockWidget.SetDisplayFormat(_clockDisplayFormat);
        ApplyTopStatusComponentVisibility();
        UpdateWallpaperPreviewLayout();
        PersistSettings();
    }

    private void ApplyTaskbarSettings(AppSettingsSnapshot snapshot)
    {
        _topStatusComponentIds.Clear();
        if (snapshot.TopStatusComponentIds is not null)
        {
            foreach (var componentId in snapshot.TopStatusComponentIds)
            {
                if (string.IsNullOrWhiteSpace(componentId))
                {
                    continue;
                }

                var normalizedId = componentId.Trim();
                if (_componentRegistry.IsKnownComponent(normalizedId) &&
                    _componentRegistry.AllowsStatusBarPlacement(normalizedId))
                {
                    _topStatusComponentIds.Add(normalizedId);
                }
            }
        }

        _pinnedTaskbarActions.Clear();
        if (snapshot.PinnedTaskbarActions is not null)
        {
            foreach (var actionText in snapshot.PinnedTaskbarActions)
            {
                if (Enum.TryParse<TaskbarActionId>(actionText, ignoreCase: true, out var action))
                {
                    _pinnedTaskbarActions.Add(action);
                }
            }
        }

        if (_pinnedTaskbarActions.Count == 0)
        {
            foreach (var action in DefaultPinnedTaskbarActions)
            {
                _pinnedTaskbarActions.Add(action);
            }
        }

        _enableDynamicTaskbarActions = snapshot.EnableDynamicTaskbarActions;
        _taskbarLayoutMode = string.IsNullOrWhiteSpace(snapshot.TaskbarLayoutMode)
            ? TaskbarLayoutBottomFullRowMacStyle
            : snapshot.TaskbarLayoutMode;
        _clockDisplayFormat = snapshot.ClockDisplayFormat == "HourMinute"
            ? ClockDisplayFormat.HourMinute
            : ClockDisplayFormat.HourMinuteSecond;

        ClockWidget.SetDisplayFormat(_clockDisplayFormat);
        WallpaperPreviewClockWidget.SetDisplayFormat(_clockDisplayFormat);

        if (_clockDisplayFormat == ClockDisplayFormat.HourMinute)
        {
            ClockFormatHMRadio.IsChecked = true;
        }
        else
        {
            ClockFormatHMSSRadio.IsChecked = true;
        }

        _suppressStatusBarToggleEvents = true;
        StatusBarClockToggleSwitch.IsChecked = _topStatusComponentIds.Contains(BuiltInComponentIds.Clock);
        _suppressStatusBarToggleEvents = false;
    }

    private void ApplyTopStatusComponentVisibility()
    {
        var showClock = _topStatusComponentIds.Contains(BuiltInComponentIds.Clock);

        ClockWidget.IsVisible = showClock;
        if (showClock)
        {
            ClockWidget.SetDisplayFormat(_clockDisplayFormat);
            Grid.SetColumnSpan(ClockWidget, _clockDisplayFormat == ClockDisplayFormat.HourMinute ? 2 : 3);
        }

        WallpaperPreviewClockWidget.IsVisible = showClock;
        if (showClock)
        {
            WallpaperPreviewClockWidget.SetDisplayFormat(_clockDisplayFormat);
        }
    }

    private TaskbarContext GetCurrentTaskbarContext()
    {
        var selectedItem = SettingsNavView?.SelectedItem as FluentAvalonia.UI.Controls.NavigationViewItem;
        return selectedItem?.Tag?.ToString() switch
        {
            "Wallpaper" => TaskbarContext.SettingsWallpaper,
            "Grid" => TaskbarContext.SettingsGrid,
            "Color" => TaskbarContext.SettingsColor,
            "StatusBar" => TaskbarContext.SettingsStatusBar,
            "Weather" => TaskbarContext.SettingsWeather,
            "Region" => TaskbarContext.SettingsRegion,
            _ => TaskbarContext.Desktop
        };
    }

    private void ApplyTaskbarActionVisibility(TaskbarContext context)
    {
        _ = context;

        var showMinimize = _pinnedTaskbarActions.Contains(TaskbarActionId.MinimizeToWindows);
        var showSettings = _pinnedTaskbarActions.Contains(TaskbarActionId.OpenSettings);

        BackToWindowsButton.IsVisible = showMinimize;
        OpenComponentLibraryButton.IsVisible = false;
        OpenSettingsButton.IsVisible = showSettings;

        WallpaperPreviewBackButtonVisual.IsVisible = showMinimize;
        WallpaperPreviewComponentLibraryVisual.IsVisible = false;
        WallpaperPreviewSettingsButtonIcon.IsVisible = showSettings;

        TaskbarFixedActionsHost.IsVisible = showMinimize;
        TaskbarSettingsActionHost.IsVisible = showSettings;
        TaskbarDynamicActionsHost.IsVisible = false;
        WallpaperPreviewTaskbarFixedActionsHost.IsVisible = showMinimize;
        WallpaperPreviewTaskbarSettingsActionHost.IsVisible = showSettings;
        WallpaperPreviewTaskbarDynamicActionsHost.IsVisible = false;

        UpdateOpenSettingsActionVisualState();
    }

    private void UpdateOpenSettingsActionVisualState()
    {
        OpenSettingsButtonTextBlock.IsVisible = false;
        OpenSettingsButtonTextBlock.Text = L("tooltip.open_settings", "Settings");
    }

    private void InitializeSettingsIcons()
    {
        const IconVariant variant = IconVariant.Regular;

        WallpaperPlacementSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Wallpaper, IconVariant = variant };
        ThemeColorSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Color, IconVariant = variant };
        StatusBarClockSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Clock, IconVariant = variant };
        StatusBarSpacingSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.TextLineSpacing, IconVariant = variant };
        WeatherLocationSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.WeatherSunny, IconVariant = variant };
        WeatherPreviewSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.WeatherSunny, IconVariant = variant };
        WeatherAlertFilterSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Info, IconVariant = variant };
        WeatherIconPackSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Color, IconVariant = variant };
        WeatherNoTlsSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Globe, IconVariant = variant };
        LanguageSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Translate, IconVariant = variant };
        TimeZoneSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.GlobeClock, IconVariant = variant };
        UpdateOptionsSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.ArrowClockwiseDashesSettings, IconVariant = variant };
        UpdateActionsSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.ArrowDownload, IconVariant = variant };
        AboutStartupSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.Play, IconVariant = variant };
        PluginSystemSettingsExpander.IconSource = new SymbolIconSource { Symbol = Symbol.PuzzlePiece, IconVariant = variant };
        UpdateThemeModeIcon();
    }

    private void UpdateThemeModeIcon()
    {
        ThemeModeSettingsExpander.IconSource = new SymbolIconSource
        {
            Symbol = _isNightMode ? Symbol.WeatherMoon : Symbol.WeatherSunny,
            IconVariant = IconVariant.Regular
        };
    }

    private void InitializeTimeZoneSettings()
    {
        _suppressTimeZoneSelectionEvents = true;
        TimeZoneComboBox.Items.Clear();
        foreach (var tz in _timeZoneService.GetAllTimeZones())
        {
            var item = new ComboBoxItem
            {
                Content = GetLocalizedTimeZoneDisplayName(tz),
                Tag = tz.Id
            };
            TimeZoneComboBox.Items.Add(item);
            if (tz.Id == _timeZoneService.CurrentTimeZone.Id)
            {
                TimeZoneComboBox.SelectedItem = item;
            }
        }

        ClockWidget.SetTimeZoneService(_timeZoneService);
        WallpaperPreviewClockWidget.SetTimeZoneService(_timeZoneService);
        _suppressTimeZoneSelectionEvents = false;
    }

    private void OnTimeZoneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressTimeZoneSelectionEvents || TimeZoneComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var timeZoneId = item.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return;
        }

        _timeZoneService.SetTimeZoneById(timeZoneId);
        PersistSettings();
    }

    private IBrush GetThemeBrush(string key)
    {
        if (Resources.TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }
}
