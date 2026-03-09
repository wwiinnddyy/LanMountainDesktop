using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using LibVLCSharp.Shared;
using Line = Avalonia.Controls.Shapes.Line;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow : Window
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

    private StartMenuFolderNode _startMenuRoot = new("All Apps", string.Empty);
    private byte[]? _launcherFolderIconPngBytes;
    private Bitmap? _launcherFolderIconBitmap;

    private int _targetShortSideCells;
    private bool _isSettingsOpen = true;
    private bool _isNightMode;
    private bool _enableDynamicTaskbarActions;
    private bool _suppressThemeToggleEvents;
    private bool _suppressLanguageSelectionEvents;
    private bool _suppressTimeZoneSelectionEvents;
    private bool _suppressWeatherLocationEvents;
    private bool _suppressSettingsPersistence;
    private bool _suppressGridSpacingEvents;
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
    private string? _wallpaperPath;
    private string _wallpaperStatus = "Current background uses solid color.";
    private IReadOnlyList<Color> _recommendedColors = Array.Empty<Color>();
    private IReadOnlyList<Color> _monetColors = Array.Empty<Color>();
    private Color _selectedThemeColor = Color.Parse("#FF3B82F6");
    private double _currentDesktopCellSize;
    private double _currentDesktopCellGap;
    private double _currentDesktopEdgeInset;
    private string _gridSpacingPreset = "Relaxed";
    private string _statusBarSpacingMode = "Relaxed";
    private int _statusBarCustomSpacingPercent = 12;
    private int _desktopEdgeInsetPercent = DefaultEdgeInsetPercent;
    private string _selectedAppRenderMode = AppRenderingModeHelper.Default;
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
    private bool _isWeatherSearchInProgress;
    private bool _isWeatherPreviewInProgress;

    public SettingsWindow()
    {
        _componentRegistry = DesktopComponentRegistryFactory.Create((Application.Current as App)?.PluginRuntimeService);
        InitializeComponent();
        InitializePluginSettingsNavigation();
        _fluentAvaloniaTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        RequestedThemeVariant = Application.Current?.RequestedThemeVariant ?? ThemeVariant.Default;
        HookEvents();
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
        NightModeToggleSwitch.Checked += OnNightModeChecked;
        NightModeToggleSwitch.Unchecked += OnNightModeUnchecked;
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
        StatusBarClockToggleSwitch.Checked += OnStatusBarClockChecked;
        StatusBarClockToggleSwitch.Unchecked += OnStatusBarClockUnchecked;
        ClockFormatHMSSRadio.Checked += OnClockFormatChanged;
        ClockFormatHMRadio.Checked += OnClockFormatChanged;
        StatusBarSpacingModeComboBox.SelectionChanged += OnStatusBarSpacingModeChanged;
        StatusBarSpacingSlider.ValueChanged += OnStatusBarSpacingSliderChanged;
        WeatherPreviewButton.Click += OnTestWeatherRequestClick;
        WeatherLocationModeComboBox.SelectionChanged += OnWeatherLocationModeSelectionChanged;
        WeatherLocationModeChipListBox.SelectionChanged += OnWeatherLocationModeChipSelectionChanged;
        WeatherAutoRefreshToggleSwitch.Checked += OnWeatherAutoRefreshToggled;
        WeatherAutoRefreshToggleSwitch.Unchecked += OnWeatherAutoRefreshToggled;
        WeatherSearchButton.Click += OnSearchWeatherCityClick;
        WeatherApplyCityButton.Click += OnApplyWeatherCitySelectionClick;
        WeatherApplyCoordinatesButton.Click += OnApplyWeatherCoordinatesClick;
        WeatherExcludedAlertsTextBox.LostFocus += OnWeatherExcludedAlertsLostFocus;
        WeatherIconPackComboBox.SelectionChanged += OnWeatherIconPackSelectionChanged;
        WeatherNoTlsToggleSwitch.Checked += OnWeatherNoTlsToggled;
        WeatherNoTlsToggleSwitch.Unchecked += OnWeatherNoTlsToggled;
        LanguageComboBox.SelectionChanged += OnLanguageSelectionChanged;
        TimeZoneComboBox.SelectionChanged += OnTimeZoneSelectionChanged;
        AutoCheckUpdatesToggleSwitch.Checked += OnAutoCheckUpdatesToggled;
        AutoCheckUpdatesToggleSwitch.Unchecked += OnAutoCheckUpdatesToggled;
        UpdateChannelChipListBox.SelectionChanged += OnUpdateChannelSelectionChanged;
        CheckForUpdatesButton.Click += OnCheckForUpdatesClick;
        DownloadAndInstallUpdateButton.Click += OnDownloadAndInstallUpdateClick;
        AutoStartWithWindowsToggleSwitch.Checked += OnAutoStartWithWindowsToggled;
        AutoStartWithWindowsToggleSwitch.Unchecked += OnAutoStartWithWindowsToggled;
        AppRenderModeComboBox.SelectionChanged += OnAppRenderModeSelectionChanged;
        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened;
        _suppressSettingsPersistence = true;
        var snapshot = _appSettingsService.Load();
        var launcherSnapshot = _launcherSettingsService.Load();

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
        WallpaperPlacementComboBox.SelectedIndex = GetPlacementIndexFromSetting(snapshot.WallpaperPlacement);
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
        WindowTitleTextBlock.Text = L("settings.title", "Settings");
        WindowSubtitleTextBlock.Text = L("settings.footer", "LanMountainDesktop Settings");
        _defaultDesktopBackground = DesktopWallpaperLayer.Background;
        RestoreSettingsTabSelection(snapshot);
        UpdateSettingsTabContent();
        UpdateWallpaperDisplay();
        UpdateWallpaperPreviewLayout();
        UpdateGridPreviewLayout();
        InitializeTimeZoneSettings();
        _ = LoadLauncherEntriesAsync();
        _suppressSettingsPersistence = false;
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
