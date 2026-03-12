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
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;
using LanMountainDesktop.Views.SettingsPages;

namespace LanMountainDesktop.Views;

using FluentIconVariant = FluentIcons.Common.IconVariant;
using FluentSymbol = FluentIcons.Common.Symbol;
using FluentSymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

public partial class SettingsWindow
{
    private readonly Dictionary<string, Control> _builtInSettingsPageHosts = new(StringComparer.OrdinalIgnoreCase);

    internal void Open(string? pageTag = null)
    {
        if (!string.IsNullOrWhiteSpace(pageTag))
        {
            _selectedSettingsTabTag = NormalizeSettingsPageTag(pageTag);
            if (_independentModuleInitializationCompleted)
            {
                SelectSettingsTab(_selectedSettingsTabTag, persistSelection: false);
            }
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    internal void PrepareForForceClose()
    {
        _allowIndependentSettingsModuleRealClose = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        AppLogger.Info(
            "IndependentSettingsModule",
            $"PreviewCleanupStarted; Stage='WindowCloseCleanup'; Module='WallpaperPreview'; CloseRequested={_isIndependentSettingsModuleClosing}.");

        try
        {
            _persistSettingsDebounceTimer?.Dispose();
            _persistSettingsDebounceTimer = null;

            StopVideoWallpaper();
            _previewVideoWallpaperPlayer?.Dispose();
            _previewVideoWallpaperPlayer = null;
            _previewVideoWallpaperMedia?.Dispose();
            _previewVideoWallpaperMedia = null;
            _previewVideoFrameRefreshTimer?.Stop();
            _previewVideoFrameRefreshTimer = null;
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
            AppLogger.Info(
                "IndependentSettingsModule",
                $"PreviewCleanupCompleted; Stage='WindowCloseCleanup'; Module='WallpaperPreview'; CloseRequested={_isIndependentSettingsModuleClosing}.");
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "IndependentSettingsModule",
                $"PreviewCleanupFailed; Stage='WindowCloseCleanup'; Module='WallpaperPreview'; Downgraded=True; CloseRequested={_isIndependentSettingsModuleClosing}.",
                ex);
        }
        finally
        {
            PendingRestartStateService.StateChanged -= OnPendingRestartStateChanged;
            Closing -= OnIndependentSettingsModuleClosing;
            base.OnClosed(e);
            AppLogger.Info("IndependentSettingsModule", $"WindowClosed; CloseRequested={_isIndependentSettingsModuleClosing}.");
            _isIndependentSettingsModuleClosing = false;
            _allowIndependentSettingsModuleRealClose = false;
        }
    }

    private void InitializeSettingsNavigation()
    {
        _settingsPageDefinitions.Clear();
        _settingsNavItems.Clear();
        InitializePluginSettingsNavigation();
        RegisterBuiltInSettingsPageDefinitions();
        RegisterPluginSettingsDefinitions();
        RebuildSettingsNavigationMenu();
    }

    private void RegisterBuiltInSettingsPageDefinitions()
    {
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "General",
            L("settings.nav.general", "General"),
            L("settings.page_desc.general", "Manage language, launcher, and weather behavior from the independent settings module."),
            FluentSymbol.Settings,
            IndependentSettingsPageCategory.Internal,
            0));
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "Appearance",
            L("settings.nav.appearance", "Appearance"),
            L("settings.page_desc.appearance", "Personalize wallpaper, desktop grid, and accent colors in one place."),
            FluentSymbol.PaintBrush,
            IndependentSettingsPageCategory.Internal,
            10));
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "Components",
            L("settings.nav.components", "Components"),
            L("settings.page_desc.components", "Review available desktop components and configure the status bar area."),
            FluentSymbol.Apps,
            IndependentSettingsPageCategory.Internal,
            20));
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "Update",
            L("settings.nav.update", "Update"),
            L("settings.page_desc.update", "Check for updates and control the update channel."),
            FluentSymbol.ArrowSync,
            IndependentSettingsPageCategory.Internal,
            30));
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "Plugins",
            L("settings.nav.plugins", "Plugins"),
            L("settings.page_desc.plugins", "Review installed plugins, runtime state, and local package installation."),
            FluentSymbol.PuzzlePiece,
            IndependentSettingsPageCategory.External,
            100));
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "PluginMarket",
            L("settings.nav.plugin_market", "Plugin Market"),
            L("settings.page_desc.pluginmarket", "Browse the official plugin market and stage installs safely."),
            FluentSymbol.ShoppingBag,
            IndependentSettingsPageCategory.External,
            110));
        RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
            "About",
            L("settings.nav.about", "About"),
            L("settings.page_desc.about", "See version information, rendering backend, and startup behavior."),
            FluentSymbol.Info,
            IndependentSettingsPageCategory.About,
            200));
    }

    private void RegisterSettingsPageDefinition(IndependentSettingsPageDefinition definition)
    {
        _settingsPageDefinitions[definition.Tag] = definition;
    }

    private void RebuildSettingsNavigationMenu()
    {
        if (SettingsNavView is null)
        {
            return;
        }

        var selectedTag = NormalizeSettingsPageTag(_selectedSettingsTabTag);
        SettingsNavView.MenuItems.Clear();
        _settingsNavItems.Clear();
        _pluginSettingsNavItems.Clear();

        IndependentSettingsPageCategory? lastCategory = null;
        foreach (var definition in _settingsPageDefinitions.Values
                     .OrderBy(definition => GetSettingsPageCategoryOrder(definition.Category))
                     .ThenBy(definition => definition.SortOrder)
                     .ThenBy(definition => definition.Title, StringComparer.CurrentCulture))
        {
            if (lastCategory is not null && lastCategory != definition.Category)
            {
                SettingsNavView.MenuItems.Add(new NavigationViewItemSeparator());
            }

            var navItem = CreateSettingsNavItem(definition);
            SettingsNavView.MenuItems.Add(navItem);
            _settingsNavItems[definition.Tag] = navItem;
            if (definition.Category == IndependentSettingsPageCategory.External)
            {
                _pluginSettingsNavItems[definition.Tag] = navItem;
            }

            lastCategory = definition.Category;
        }

        if (_settingsNavItems.TryGetValue(selectedTag, out var selectedItem))
        {
            SettingsNavView.SelectedItem = selectedItem;
            return;
        }

        if (SettingsNavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault() is { } firstItem)
        {
            _selectedSettingsTabTag = firstItem.Tag?.ToString() ?? "General";
            SettingsNavView.SelectedItem = firstItem;
        }
    }

    private NavigationViewItem CreateSettingsNavItem(IndependentSettingsPageDefinition definition)
    {
        var item = new NavigationViewItem
        {
            Content = definition.Title,
            Tag = definition.Tag,
            IconSource = new FluentSymbolIconSource
            {
                Symbol = definition.Icon,
                IconVariant = FluentIconVariant.Regular
            }
        };

        if (!string.IsNullOrWhiteSpace(definition.ToolTip))
        {
            ToolTip.SetTip(item, definition.ToolTip);
        }

        return item;
    }

    private static int GetSettingsPageCategoryOrder(IndependentSettingsPageCategory category)
    {
        return category switch
        {
            IndependentSettingsPageCategory.Internal => 0,
            IndependentSettingsPageCategory.External => 1,
            IndependentSettingsPageCategory.About => 2,
            IndependentSettingsPageCategory.Debug => 3,
            _ => int.MaxValue
        };
    }

    private void InitializeSettingsPageHosts()
    {
        _builtInSettingsPageHosts.Clear();

        GeneralSettingsHubPanel = new GeneralSettingsPage();
        AppearanceSettingsHubPanel = new AppearanceSettingsPage();
        ComponentsSettingsHubPanel = new ComponentsSettingsPage();
        WallpaperSettingsPanel = new WallpaperSettingsPage();
        GridSettingsPanel = new GridSettingsPage();
        ColorSettingsPanel = new ColorSettingsPage();
        StatusBarSettingsPanel = new StatusBarSettingsPage();
        WeatherSettingsPanel = new WeatherSettingsPage();
        RegionSettingsPanel = new RegionSettingsPage();
        UpdateSettingsPanel = new UpdateSettingsPage();
        LauncherSettingsPanel = new LauncherSettingsPage();
        AboutSettingsPanel = new AboutSettingsPage();
        PluginSettingsPanel = new PluginSettingsPage();
        PluginMarketSettingsPanel = new PluginMarketSettingsPage();

        GeneralSettingsHubPanel.RegionContentHost.Content = RegionSettingsPanel;
        GeneralSettingsHubPanel.LauncherContentHost.Content = LauncherSettingsPanel;
        GeneralSettingsHubPanel.WeatherContentHost.Content = WeatherSettingsPanel;

        AppearanceSettingsHubPanel.WallpaperContentHost.Content = WallpaperSettingsPanel;
        AppearanceSettingsHubPanel.GridContentHost.Content = GridSettingsPanel;
        AppearanceSettingsHubPanel.ColorContentHost.Content = ColorSettingsPanel;

        ComponentsSettingsHubPanel.StatusBarContentHost.Content = StatusBarSettingsPanel;

        RegisterBuiltInSettingsPage("General", GeneralSettingsHubPanel);
        RegisterBuiltInSettingsPage("Appearance", AppearanceSettingsHubPanel);
        RegisterBuiltInSettingsPage("Components", ComponentsSettingsHubPanel);
        RegisterBuiltInSettingsPage("Update", UpdateSettingsPanel);
        RegisterBuiltInSettingsPage("About", AboutSettingsPanel);
        RegisterBuiltInSettingsPage("Plugins", PluginSettingsPanel);
        RegisterBuiltInSettingsPage("PluginMarket", PluginMarketSettingsPanel);
    }

    private void RegisterBuiltInSettingsPage(string tag, Control? page)
    {
        if (page is not null)
        {
            _builtInSettingsPageHosts[tag] = page;
        }
    }

    private Control? ResolveSettingsPageHost(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        if (_builtInSettingsPageHosts.TryGetValue(tag, out var builtIn))
        {
            return builtIn;
        }

        return _pluginSettingsPageHosts.GetValueOrDefault(tag);
    }

    private void OnSettingsNavSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem selectedItem &&
            selectedItem.Tag is not null)
        {
            _selectedSettingsTabTag = NormalizeSettingsPageTag(selectedItem.Tag.ToString());
        }

        AppLogger.Info("IndependentSettingsModule", $"NavigationChanged; Tag='{_selectedSettingsTabTag}'.");
        UpdateSettingsTabContent();
        SchedulePersistSettings(0);
    }

    private NavigationViewItem? GetSettingsNavItem(string tag)
    {
        if (_settingsNavItems.TryGetValue(tag, out var builtIn))
        {
            return builtIn;
        }

        return _pluginSettingsNavItems.GetValueOrDefault(tag);
    }

    private void SelectSettingsTab(string? tag, bool persistSelection)
    {
        if (string.IsNullOrWhiteSpace(tag) || SettingsNavView is null)
        {
            return;
        }

        if (GetSettingsNavItem(tag) is not { } selectedItem)
        {
            return;
        }

        _selectedSettingsTabTag = tag;
        SettingsNavView.SelectedItem = selectedItem;
        UpdateSettingsTabContent();

        if (persistSelection)
        {
            PersistSettings();
        }
    }

    private int GetSettingsTabIndex()
    {
        return ResolveSelectedSettingsTabIndex();
    }

    private void UpdateSettingsTabContent()
    {
        if (SettingsNavView is null || SettingsPageFrame is null)
        {
            return;
        }

        var tag = GetSelectedSettingsTabTag();
        UpdateCurrentSettingsPageHeader(tag);
        if (ResolveSettingsPageHost(tag) is { } pageHost)
        {
            if (!ReferenceEquals(SettingsPageFrame.Content, pageHost))
            {
                SettingsPageFrame.Content = pageHost;
            }
        }
        else
        {
            AppLogger.Warn("IndependentSettingsModule", $"PageHostMissing; Tag='{tag}'.");
            ShowIndependentModuleStatus(
                L("settings.shell.partial_warning_title", "部分内容未能加载"),
                $"No settings page host is registered for '{tag}'.",
                InfoBarSeverity.Warning);
            return;
        }

        if (tag == "General")
        {
            RenderLauncherHiddenItemsList();
        }

        if (tag == "Plugins")
        {
            PluginSettingsPanel.RefreshFromRuntime();
        }

        if (tag == "PluginMarket")
        {
            PluginMarketSettingsPanel.RefreshFromRuntime();
        }

        if (tag == "Appearance")
        {
            UpdateWallpaperPreviewLayout();
            UpdateGridPreviewLayout();
        }

        if (tag == "Components")
        {
            UpdateComponentsSettingsSummary();
        }

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
        SyncVideoWallpaperPreviewPlayback();
    }

    private void PersistSettings()
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        AppLogger.Info("IndependentSettingsModule", $"PersistCompleted; Tag='{_selectedSettingsTabTag}'.");
        _appSettingsService.Save(BuildAppSettingsSnapshot());
        _launcherSettingsService.Save(BuildLauncherSettingsSnapshot());
    }

    private AppSettingsSnapshot BuildAppSettingsSnapshot()
    {
        var snapshot = _appSettingsService.Load();
        snapshot.GridShortSideCells = _targetShortSideCells;
        snapshot.GridSpacingPreset = _gridSpacingPreset;
        snapshot.DesktopEdgeInsetPercent = _desktopEdgeInsetPercent;
        snapshot.IsNightMode = _isNightMode;
        snapshot.ThemeColor = _selectedThemeColor.ToString();
        snapshot.WallpaperPath = _wallpaperPath;
        snapshot.WallpaperPlacement = GetPlacementDisplayName(GetSelectedWallpaperPlacement());
        snapshot.SettingsTabIndex = Math.Max(0, GetSettingsTabIndex());
        snapshot.SettingsTabTag = NormalizeSettingsPageTag(_selectedSettingsTabTag);
        snapshot.LanguageCode = _languageCode;
        snapshot.TimeZoneId = _timeZoneService.CurrentTimeZone.Id;
        snapshot.WeatherLocationMode = ToWeatherLocationModeTag(_weatherLocationMode);
        snapshot.WeatherLocationKey = _weatherLocationKey;
        snapshot.WeatherLocationName = _weatherLocationName;
        snapshot.WeatherLatitude = _weatherLatitude;
        snapshot.WeatherLongitude = _weatherLongitude;
        snapshot.WeatherAutoRefreshLocation = _weatherAutoRefreshLocation;
        snapshot.WeatherLocationQuery = BuildLegacyWeatherLocationQuery();
        snapshot.WeatherExcludedAlerts = _weatherExcludedAlertsRaw;
        snapshot.WeatherIconPackId = _weatherIconPackId;
        snapshot.WeatherNoTlsRequests = _weatherNoTlsRequests;
        snapshot.AutoStartWithWindows = _autoStartWithWindows;
        snapshot.AppRenderMode = _selectedAppRenderMode;
        snapshot.AutoCheckUpdates = _autoCheckUpdates;
        snapshot.IncludePrereleaseUpdates = IncludePrereleaseUpdates;
        snapshot.UpdateChannel = IncludePrereleaseUpdates ? UpdateChannelPreview : UpdateChannelStable;
        snapshot.TopStatusComponentIds = _topStatusComponentIds.ToList();
        snapshot.PinnedTaskbarActions = _pinnedTaskbarActions.Select(action => action.ToString()).ToList();
        snapshot.EnableDynamicTaskbarActions = _enableDynamicTaskbarActions;
        snapshot.TaskbarLayoutMode = _taskbarLayoutMode;
        snapshot.ClockDisplayFormat = _clockDisplayFormat == ClockDisplayFormat.HourMinute ? "HourMinute" : "HourMinuteSecond";
        snapshot.StatusBarSpacingMode = _statusBarSpacingMode;
        snapshot.StatusBarCustomSpacingPercent = _statusBarCustomSpacingPercent;
        return snapshot;
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
        AppLogger.Info("IndependentSettingsModule", $"PersistScheduled; DelayMs={Math.Max(0, delayMs)}; Tag='{_selectedSettingsTabTag}'.");
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

    private static string NormalizeSettingsPageTag(string? tag)
    {
        return tag switch
        {
            null or "" => "General",
            _ when string.Equals(tag, "Wallpaper", StringComparison.OrdinalIgnoreCase) => "Appearance",
            _ when string.Equals(tag, "Grid", StringComparison.OrdinalIgnoreCase) => "Appearance",
            _ when string.Equals(tag, "Color", StringComparison.OrdinalIgnoreCase) => "Appearance",
            _ when string.Equals(tag, "StatusBar", StringComparison.OrdinalIgnoreCase) => "Components",
            _ when string.Equals(tag, "Region", StringComparison.OrdinalIgnoreCase) => "General",
            _ when string.Equals(tag, "Weather", StringComparison.OrdinalIgnoreCase) => "General",
            _ when string.Equals(tag, "Launcher", StringComparison.OrdinalIgnoreCase) => "General",
            _ => tag
        };
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

        if (radioButton.IsChecked != true)
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
        return GetSelectedSettingsTabTag() switch
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
        const FluentIconVariant variant = FluentIconVariant.Regular;

        WallpaperPlacementSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Wallpaper, IconVariant = variant };
        ThemeColorSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Color, IconVariant = variant };
        StatusBarClockSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Clock, IconVariant = variant };
        StatusBarSpacingSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.TextLineSpacing, IconVariant = variant };
        WeatherLocationSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.WeatherSunny, IconVariant = variant };
        WeatherPreviewSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.WeatherSunny, IconVariant = variant };
        WeatherAlertFilterSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Info, IconVariant = variant };
        WeatherIconPackSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Color, IconVariant = variant };
        WeatherNoTlsSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Globe, IconVariant = variant };
        LanguageSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Translate, IconVariant = variant };
        TimeZoneSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.GlobeClock, IconVariant = variant };
        UpdateOptionsSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.ArrowClockwiseDashesSettings, IconVariant = variant };
        UpdateActionsSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.ArrowDownload, IconVariant = variant };
        AboutStartupSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.Play, IconVariant = variant };
        PluginSystemSettingsExpander.IconSource = new FluentSymbolIconSource { Symbol = FluentSymbol.PuzzlePiece, IconVariant = variant };
        UpdateThemeModeIcon();
    }

    private void UpdateThemeModeIcon()
    {
        ThemeModeSettingsExpander.IconSource = new FluentSymbolIconSource
        {
            Symbol = _isNightMode ? FluentSymbol.WeatherMoon : FluentSymbol.WeatherSunny,
            IconVariant = FluentIconVariant.Regular
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

