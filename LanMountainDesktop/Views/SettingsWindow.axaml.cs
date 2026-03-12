using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;
using LanMountainDesktop.Views.SettingsPages;
using LibVLCSharp.Shared;
using Line = Avalonia.Controls.Shapes.Line;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow : IndependentSettingsModuleWindowBase
{
    private enum WallpaperPlacement
    {
        Fill,
        Fit,
        Stretch,
        Center,
        Tile
    }

    private enum WallpaperMediaType
    {
        None,
        Image,
        Video
    }

    private enum WeatherLocationMode
    {
        CitySearch,
        Coordinates
    }

    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
    private const int MinEdgeInsetPercent = 0;
    private const int MaxEdgeInsetPercent = 30;
    private const int DefaultEdgeInsetPercent = 18;
    private const double WallpaperPreviewMaxWidth = 520;
    private const double LightBackgroundLuminanceThreshold = 0.57;
    private const string TaskbarLayoutBottomFullRowMacStyle = "BottomFullRowMacStyle";
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v"
    };
    private static readonly TaskbarActionId[] DefaultPinnedTaskbarActions =
    [
        TaskbarActionId.MinimizeToWindows,
        TaskbarActionId.OpenSettings
    ];

    private readonly record struct GridMetrics(
        int ColumnCount,
        int RowCount,
        double CellSize,
        double GapPx,
        double EdgeInsetPx,
        double GridWidthPx,
        double GridHeightPx)
    {
        public double Pitch => CellSize + GapPx;
    }

    private readonly MonetColorService _monetColorService = new();
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LauncherSettingsService _launcherSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly TimeZoneService _timeZoneService = new();
    private readonly WindowsStartupService _windowsStartupService = new();
    private readonly GitHubReleaseUpdateService _releaseUpdateService = new("wwiinnddyy", "LanMountainDesktop");
    private readonly IWeatherDataService _weatherDataService = new XiaomiWeatherService();
    private readonly ComponentRegistry _componentRegistry;
    private readonly WindowsStartMenuService _windowsStartMenuService = new();
    private readonly LinuxDesktopEntryService _linuxDesktopEntryService = new();
    private readonly FluentAvaloniaTheme? _fluentAvaloniaTheme;
    private readonly HashSet<string> _topStatusComponentIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TaskbarActionId> _pinnedTaskbarActions = [];
    private readonly HashSet<string> _hiddenLauncherFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenLauncherAppPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<StartMenuFolderNode> _launcherFolderStack = [];
    private readonly Dictionary<string, NavigationViewItem> _settingsNavItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NavigationViewItem> _pluginSettingsNavItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IndependentSettingsPageDefinition> _settingsPageDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private GeneralSettingsPage? GeneralSettingsHubPanel;
    private AppearanceSettingsPage? AppearanceSettingsHubPanel;
    private ComponentsSettingsPage? ComponentsSettingsHubPanel;
    private WallpaperSettingsPage? WallpaperSettingsPanel;
    private GridSettingsPage? GridSettingsPanel;
    private ColorSettingsPage? ColorSettingsPanel;
    private StatusBarSettingsPage? StatusBarSettingsPanel;
    private WeatherSettingsPage? WeatherSettingsPanel;
    private RegionSettingsPage? RegionSettingsPanel;
    private UpdateSettingsPage? UpdateSettingsPanel;
    private LauncherSettingsPage? LauncherSettingsPanel;
    private AboutSettingsPage? AboutSettingsPanel;
    private PluginSettingsPage? PluginSettingsPanel;
    private PluginMarketSettingsPage? PluginMarketSettingsPanel;

    private StartMenuFolderNode _startMenuRoot = new("All Apps", string.Empty);
    private byte[]? _launcherFolderIconPngBytes;
    private Bitmap? _launcherFolderIconBitmap;

    private int _targetShortSideCells;
    private bool _isNightMode;
    private bool _enableDynamicTaskbarActions;
    private bool _suppressThemeToggleEvents;
    private bool _suppressLanguageSelectionEvents;
    private bool _suppressTimeZoneSelectionEvents;
    private bool _suppressWeatherLocationEvents;
    private bool _suppressSettingsPersistence;
    private bool _suppressGridInsetEvents;
    private bool _suppressStatusBarSpacingEvents;
    private bool _suppressAutoStartToggleEvents;
    private bool _suppressAppRenderModeSelectionEvents;
    private bool _isUpdatingWallpaperPreviewLayout;
    private IBrush? _defaultDesktopBackground;
    private Bitmap? _wallpaperBitmap;
    private WallpaperMediaType _wallpaperMediaType;
    private string? _wallpaperVideoPath;
    private MediaPlayer? _previewVideoWallpaperPlayer;
    private Media? _previewVideoWallpaperMedia;
    private LibVLC? _libVlc;
    private readonly object _previewVideoFrameSync = new();
    private MediaPlayer.LibVLCVideoLockCb? _previewVideoLockCallback;
    private MediaPlayer.LibVLCVideoUnlockCb? _previewVideoUnlockCallback;
    private MediaPlayer.LibVLCVideoDisplayCb? _previewVideoDisplayCallback;
    private DispatcherTimer? _previewVideoFrameRefreshTimer;
    private IntPtr _previewVideoFrameBufferPtr;
    private byte[]? _previewVideoStagingBuffer;
    private WriteableBitmap? _previewVideoBitmap;
    private int _previewVideoFrameWidth;
    private int _previewVideoFrameHeight;
    private int _previewVideoFramePitch;
    private int _previewVideoFrameBufferSize;
    private int _previewVideoFrameDirtyFlag;
    private bool _previewVideoSnapshotPending;
    private string? _wallpaperPath;
    private string _wallpaperStatus = "Current background uses solid color.";
    private IReadOnlyList<Color> _recommendedColors = Array.Empty<Color>();
    private IReadOnlyList<Color> _monetColors = Array.Empty<Color>();
    private Color _selectedThemeColor = Color.Parse("#FF3B82F6");
    private double _currentDesktopCellSize;
    private string _gridSpacingPreset = "Relaxed";
    private string _statusBarSpacingMode = "Relaxed";
    private int _statusBarCustomSpacingPercent = 12;
    private int _desktopEdgeInsetPercent = DefaultEdgeInsetPercent;
    private string _selectedAppRenderMode = AppRenderingModeHelper.Default;
    private string _runningAppRenderMode = AppRenderingModeHelper.Default;
    private string _taskbarLayoutMode = TaskbarLayoutBottomFullRowMacStyle;
    private string _languageCode = "zh-CN";
    private WeatherLocationMode _weatherLocationMode = WeatherLocationMode.CitySearch;
    private string _weatherLocationKey = string.Empty;
    private string _weatherLocationName = string.Empty;
    private double _weatherLatitude = 39.9042;
    private double _weatherLongitude = 116.4074;
    private bool _weatherAutoRefreshLocation;
    private string _weatherExcludedAlertsRaw = string.Empty;
    private string _weatherIconPackId = "FluentRegular";
    private bool _weatherNoTlsRequests;
    private bool _autoStartWithWindows;
    private string _weatherSearchKeyword = string.Empty;
    private string _selectedSettingsTabTag = "General";
    private WallpaperPlacement _selectedWallpaperPlacement = WallpaperPlacement.Fill;
    private bool _isWeatherSearchInProgress;
    private bool _isWeatherPreviewInProgress;
    private bool _controlsBound;
    private bool _independentModuleInitializationCompleted;
    private bool _suppressWallpaperPlacementEvents;
    private bool _isIndependentSettingsModuleClosing;
    private bool _allowIndependentSettingsModuleRealClose;

    public SettingsWindow()
    {
        _componentRegistry = DesktopComponentRegistryFactory.Create((Application.Current as App)?.PluginRuntimeService);
        InitializeComponent();
        InitializeSettingsPageHosts();
        InitializeSettingsNavigation();
        InitializePluginSettingsNavigation();
        _fluentAvaloniaTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        RequestedThemeVariant = Application.Current?.RequestedThemeVariant ?? ThemeVariant.Default;
        PendingRestartStateService.StateChanged += OnPendingRestartStateChanged;
        Closing += OnIndependentSettingsModuleClosing;
        Opened += OnWindowOpened;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void HookEvents()
    {
        PickWallpaperButton.Click += OnPickWallpaperClick;
        ClearWallpaperButton.Click += OnClearWallpaperClick;
        WallpaperPlacementComboBox.SelectionChanged += OnWallpaperPlacementSelectionChanged;
        GridSizeSlider.ValueChanged += OnGridSizeSliderChanged;
        GridSpacingPresetComboBox.SelectionChanged += OnGridSpacingPresetSelectionChanged;
        GridEdgeInsetSlider.ValueChanged += OnGridEdgeInsetSliderChanged;
        ApplyGridButton.Click += OnApplyGridSizeClick;
        NightModeToggleSwitch.IsCheckedChanged += OnNightModeIsCheckedChanged;
        RecommendedColorButton1.Click += OnRecommendedColorClick;
        RecommendedColorButton2.Click += OnRecommendedColorClick;
        RecommendedColorButton3.Click += OnRecommendedColorClick;
        RecommendedColorButton4.Click += OnRecommendedColorClick;
        RecommendedColorButton5.Click += OnRecommendedColorClick;
        RecommendedColorButton6.Click += OnRecommendedColorClick;
        RefreshMonetColorsButton.Click += OnRefreshMonetColorsClick;
        MonetColorButton1.Click += OnMonetColorClick;
        MonetColorButton2.Click += OnMonetColorClick;
        MonetColorButton3.Click += OnMonetColorClick;
        MonetColorButton4.Click += OnMonetColorClick;
        MonetColorButton5.Click += OnMonetColorClick;
        MonetColorButton6.Click += OnMonetColorClick;
        StatusBarClockToggleSwitch.IsCheckedChanged += OnStatusBarClockIsCheckedChanged;
        ClockFormatHMSSRadio.IsCheckedChanged += OnClockFormatChanged;
        ClockFormatHMRadio.IsCheckedChanged += OnClockFormatChanged;
        StatusBarSpacingModeComboBox.SelectionChanged += OnStatusBarSpacingModeChanged;
        StatusBarSpacingSlider.ValueChanged += OnStatusBarSpacingSliderChanged;
        WeatherPreviewButton.Click += OnTestWeatherRequestClick;
        WeatherLocationModeComboBox.SelectionChanged += OnWeatherLocationModeSelectionChanged;
        WeatherLocationModeChipListBox.SelectionChanged += OnWeatherLocationModeChipSelectionChanged;
        WeatherAutoRefreshToggleSwitch.IsCheckedChanged += OnWeatherAutoRefreshToggled;
        WeatherSearchButton.Click += OnSearchWeatherCityClick;
        WeatherApplyCityButton.Click += OnApplyWeatherCitySelectionClick;
        WeatherApplyCoordinatesButton.Click += OnApplyWeatherCoordinatesClick;
        WeatherExcludedAlertsTextBox.LostFocus += OnWeatherExcludedAlertsLostFocus;
        WeatherIconPackComboBox.SelectionChanged += OnWeatherIconPackSelectionChanged;
        WeatherNoTlsToggleSwitch.IsCheckedChanged += OnWeatherNoTlsToggled;
        LanguageComboBox.SelectionChanged += OnLanguageSelectionChanged;
        TimeZoneComboBox.SelectionChanged += OnTimeZoneSelectionChanged;
        AutoCheckUpdatesToggleSwitch.IsCheckedChanged += OnAutoCheckUpdatesToggled;
        UpdateChannelChipListBox.SelectionChanged += OnUpdateChannelSelectionChanged;
        CheckForUpdatesButton.Click += OnCheckForUpdatesClick;
        DownloadAndInstallUpdateButton.Click += OnDownloadAndInstallUpdateClick;
        AutoStartWithWindowsToggleSwitch.IsCheckedChanged += OnAutoStartWithWindowsToggled;
        AppRenderModeComboBox.SelectionChanged += OnAppRenderModeSelectionChanged;
    }

    private void EnsureIndependentModuleControlsBound()
    {
        if (_controlsBound)
        {
            return;
        }

        AppLogger.Info("IndependentSettingsModule", "ControlsBindingStarted.");
        try
        {
            HookEvents();
            _controlsBound = true;
            AppLogger.Info("IndependentSettingsModule", "ControlsBindingCompleted.");
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn("IndependentSettingsModule", "ControlsBindingFailed.", ex);
            throw new InvalidOperationException("Failed to bind independent settings module controls.", ex);
        }
    }

    private void OnNightModeIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton)
        {
            return;
        }

        if (toggleButton.IsChecked == true)
        {
            OnNightModeChecked(sender, e);
            return;
        }

        OnNightModeUnchecked(sender, e);
    }

    private void OnStatusBarClockIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton)
        {
            return;
        }

        if (toggleButton.IsChecked == true)
        {
            OnStatusBarClockChecked(sender, e);
            return;
        }

        OnStatusBarClockUnchecked(sender, e);
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened;
        UpdateWindowChromeState();
        UiExceptionGuard.FireAndForgetGuarded(
            async () =>
            {
                EnsureIndependentModuleControlsBound();
                await InitializeIndependentSettingsModuleAsync();
            },
            "IndependentSettingsModule.Initialize",
            UiExceptionGuard.BuildContext(("Window", nameof(SettingsWindow))),
            ex =>
            {
                ShowIndependentModuleStatus(
                    L("settings.shell.init_failed_title", "设置模块初始化失败"),
                    ex.Message,
                    InfoBarSeverity.Warning);
                return Task.CompletedTask;
            });
    }

    private void OnIndependentSettingsModuleClosing(object? sender, WindowClosingEventArgs e)
    {
        AppLogger.Info(
            "IndependentSettingsModule",
            $"CloseRequested; AllowRealClose={_allowIndependentSettingsModuleRealClose}; Reason='{e.CloseReason}'.");

        if (!_allowIndependentSettingsModuleRealClose &&
            e.CloseReason is not WindowCloseReason.ApplicationShutdown &&
            e.CloseReason is not WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
            PersistSettings();
            Hide();
            AppLogger.Info("IndependentSettingsModule", "WindowHiddenByClose.");
            return;
        }

        _isIndependentSettingsModuleClosing = true;
    }

    private async Task InitializeIndependentSettingsModuleAsync()
    {
        if (_independentModuleInitializationCompleted)
        {
            return;
        }

        AppLogger.Info("IndependentSettingsModule", "ModuleInitStarted; Stage='Opened'.");
        _suppressSettingsPersistence = true;
        try
        {
            ShowIndependentModuleStatus(string.Empty, string.Empty, InfoBarSeverity.Informational, isOpen: false);

            var snapshot = new AppSettingsSnapshot();
            var launcherSnapshot = new LauncherSettingsSnapshot();

            await RunInitializationStageAsync("SnapshotLoad", () =>
            {
                snapshot = _appSettingsService.Load();
                launcherSnapshot = _launcherSettingsService.Load();
                return Task.CompletedTask;
            });

            await RunInitializationStageAsync("BaseConfiguration", () =>
            {
                _targetShortSideCells = Math.Clamp(
                    snapshot.GridShortSideCells > 0 ? snapshot.GridShortSideCells : CalculateDefaultShortSideCellCountFromDpi(),
                    MinShortSideCells,
                    MaxShortSideCells);
                _gridSpacingPreset = NormalizeGridSpacingPreset(snapshot.GridSpacingPreset);
                _desktopEdgeInsetPercent = Math.Clamp(snapshot.DesktopEdgeInsetPercent, MinEdgeInsetPercent, MaxEdgeInsetPercent);
                _statusBarSpacingMode = NormalizeStatusBarSpacingMode(snapshot.StatusBarSpacingMode);
                _statusBarCustomSpacingPercent = Math.Clamp(snapshot.StatusBarCustomSpacingPercent, 0, 30);
                GridSizeNumberBox.Value = _targetShortSideCells;
                GridSizeSlider.Value = _targetShortSideCells;
                GridSpacingPresetComboBox.SelectedIndex = string.Equals(_gridSpacingPreset, "Compact", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                GridEdgeInsetSlider.Value = _desktopEdgeInsetPercent;
                GridEdgeInsetNumberBox.Value = _desktopEdgeInsetPercent;
                StatusBarSpacingModeComboBox.SelectedIndex = _statusBarSpacingMode switch
                {
                    "Compact" => 0,
                    "Custom" => 2,
                    _ => 1
                };
                StatusBarSpacingSlider.Value = _statusBarCustomSpacingPercent;
                StatusBarSpacingNumberBox.Value = _statusBarCustomSpacingPercent;
                StatusBarSpacingCustomPanel.IsVisible = string.Equals(_statusBarSpacingMode, "Custom", StringComparison.OrdinalIgnoreCase);
                GridEdgeInsetNumberBox.ValueChanged += OnGridEdgeInsetNumberBoxChanged;
                StatusBarSpacingNumberBox.ValueChanged += OnStatusBarSpacingNumberBoxChanged;
                ApplyTaskbarSettings(snapshot);
                InitializeLocalization(snapshot.LanguageCode);
                InitializeWeatherSettings(snapshot);
                InitializeAutoStartWithWindowsSetting(snapshot);
                InitializeAppRenderModeSetting(snapshot);
                InitializeUpdateSettings(snapshot);
                InitializeLauncherVisibilitySettings(launcherSnapshot);
                InitializeSettingsIcons();
                ApplyLocalization();
                return Task.CompletedTask;
            });

            await RunInitializationStageAsync("VisualState", () =>
            {
                _selectedWallpaperPlacement = GetWallpaperPlacementFromIndex(GetPlacementIndexFromSetting(snapshot.WallpaperPlacement));
                _suppressWallpaperPlacementEvents = true;
                WallpaperPlacementComboBox.SelectedIndex = GetPlacementIndexFromSetting(snapshot.WallpaperPlacement);
                _suppressWallpaperPlacementEvents = false;
                TryRestoreWallpaper(snapshot.WallpaperPath);
                RefreshColorPalettes();
                if (TryParseColor(snapshot.ThemeColor, out var savedThemeColor))
                {
                    _selectedThemeColor = savedThemeColor;
                }

                _isNightMode = snapshot.IsNightMode ?? (CalculateCurrentBackgroundLuminance() < LightBackgroundLuminanceThreshold);
                ApplyNightModeState(_isNightMode, refreshPalettes: false);
                EnsureSelectedThemeColor();
                UpdateThemeColorSelectionState();
                ThemeColorStatusTextBlock.Text = Lf("settings.color.theme_ready_format", "Theme color ready: {0}.", _selectedThemeColor);
                _defaultDesktopBackground = DesktopWallpaperLayer.Background;
                RestoreSettingsTabSelection(snapshot);
                UpdateSettingsTabContent();
                UpdateWallpaperDisplay();
                UpdateWallpaperPreviewLayout();
                UpdateGridPreviewLayout();
                InitializeTimeZoneSettings();
                return Task.CompletedTask;
            });

            UiExceptionGuard.FireAndForgetGuarded(
                LoadLauncherEntriesAsync,
                "IndependentSettingsModule.LoadLauncherEntries",
                UiExceptionGuard.BuildContext(("Window", nameof(SettingsWindow))),
                ex =>
                {
                    ShowIndependentModuleStatus(
                        L("settings.shell.partial_warning_title", "部分内容未能载入"),
                        ex.Message,
                        InfoBarSeverity.Warning);
                    return Task.CompletedTask;
                });

            _independentModuleInitializationCompleted = true;
            AppLogger.Info("IndependentSettingsModule", "ModuleInitCompleted.");
        }
        finally
        {
            _suppressSettingsPersistence = false;
        }
    }

    private async Task RunInitializationStageAsync(string stage, Func<Task> action)
    {
        AppLogger.Info("IndependentSettingsModule", $"ModuleInitStarted; Stage='{stage}'.");
        try
        {
            await action();
            AppLogger.Info("IndependentSettingsModule", $"ModuleInitCompleted; Stage='{stage}'.");
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn("IndependentSettingsModule", $"ModuleInitFailed; Stage='{stage}'.", ex);
            ShowIndependentModuleStatus(
                L("settings.shell.partial_warning_title", "部分内容未能载入"),
                ex.Message,
                InfoBarSeverity.Warning);
        }
    }

    private void ShowIndependentModuleStatus(string title, string message, InfoBarSeverity severity, bool isOpen = true)
    {
        if (IndependentSettingsStatusInfoBar is null)
        {
            return;
        }

        IndependentSettingsStatusInfoBar.Title = title;
        IndependentSettingsStatusInfoBar.Message = message;
        IndependentSettingsStatusInfoBar.Severity = severity;
        IndependentSettingsStatusInfoBar.IsOpen = isOpen;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (!CanResize)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateWindowChromeState();
    }

    private void OnMinimizeWindowClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        UpdateWindowChromeState();
    }

    private void OnToggleWindowStateClick(object? sender, RoutedEventArgs e)
    {
        if (!CanResize)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateWindowChromeState();
    }

    private void UpdateWindowChromeState()
    {
        if (WindowStateToggleIcon is null)
        {
            return;
        }

        WindowStateToggleIcon.Symbol = WindowState == WindowState.Maximized
            ? FluentIcons.Common.Symbol.SquareMultiple
            : FluentIcons.Common.Symbol.Square;
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSettingsPaneToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (SettingsNavView is not null)
        {
            SettingsNavView.IsPaneOpen = !SettingsNavView.IsPaneOpen;
        }
    }

    private void OnOpenLogsFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogger.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            ShowIndependentModuleStatus(
                L("settings.shell.partial_warning_title", "部分内容未能加载"),
                ex.Message,
                InfoBarSeverity.Warning);
        }
    }

    private void OnOpenAppFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(".") ?? string.Empty,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            ShowIndependentModuleStatus(
                L("settings.shell.partial_warning_title", "部分内容未能加载"),
                ex.Message,
                InfoBarSeverity.Warning);
        }
    }
}
