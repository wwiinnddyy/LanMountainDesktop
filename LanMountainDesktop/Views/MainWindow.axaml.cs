using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;


namespace LanMountainDesktop.Views;

public partial class MainWindow : Window, ISettingsWindowAnchorProvider
{
    private enum WallpaperMediaType
    {
        None,
        Image,
        SolidColor
    }

    private enum WallpaperDisplayState
    {
        NoWallpaperConfigured,
        TemporarilyUnavailable,
        CurrentValidWallpaper
    }

    private enum WeatherLocationMode
    {
        CitySearch,
        Coordinates
    }

    private const int StatusBarRowIndex = 0;
    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
    private const int MinEdgeInsetPercent = 0;
    private const int MaxEdgeInsetPercent = 30;
    private const int DefaultEdgeInsetPercent = 18;
    private static readonly int SettingsTransitionDurationMs = (int)FluttermotionToken.Page.TotalMilliseconds;
    private const double LightBackgroundLuminanceThreshold = 0.57;
    private const string TaskbarLayoutBottomFullRowMacStyle = "BottomFullRowMacStyle";
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };
    private static readonly TaskbarActionId[] DefaultPinnedTaskbarActions =
    [
        TaskbarActionId.MinimizeToWindows
    ];
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private readonly IAppearanceThemeService _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();
    private readonly IAppLogoService _appLogoService = HostAppLogoProvider.GetOrCreate();
    private readonly ICurrentUserProfileService _currentUserProfileService = HostCurrentUserProfileProvider.GetOrCreate();
    private readonly IGridSettingsService _gridSettingsService;
    private readonly IThemeAppearanceService _themeSettingsService;
    private readonly IWeatherSettingsService _weatherSettingsService;
    private readonly IRegionSettingsService _regionSettingsService;
    private readonly IUpdateSettingsService _updateSettingsService;
    private readonly ISettingsService _settingsService;
    private readonly IComponentLayoutStore _componentLayoutStore = ComponentDomainStorageProvider.Instance;
    private readonly IComponentStateStore _componentStateStore = ComponentDomainStorageProvider.Instance;
    private readonly IComponentInstanceSettingsStore _componentSettingsStore = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly TimeZoneService _timeZoneService;
    private readonly WindowsStartupService _windowsStartupService = new();
    private readonly IWeatherInfoService _weatherDataService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();
    private readonly ComponentRegistry _componentRegistry;
    private readonly DesktopComponentRuntimeRegistry _componentRuntimeRegistry;
    private readonly DesktopComponentEditorRegistry _componentEditorRegistry;
    private readonly IComponentLibraryService _componentLibraryService;
    private readonly IComponentEditorWindowService _componentEditorWindowService;
    private readonly IEmbeddedComponentLibraryService _componentLibraryWindowService = new EmbeddedComponentLibraryService();
    private ComponentLibraryWindow? _detachedComponentLibraryWindow;
    private readonly FluentAvaloniaTheme? _fluentAvaloniaTheme;
    private readonly HashSet<string> _topStatusComponentIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TaskbarActionId> _pinnedTaskbarActions = [];
    private int _targetShortSideCells;
    private bool _isSettingsOpen;
    private bool _isNightMode;
    private bool _enableDynamicTaskbarActions;
    private bool _suppressThemeToggleEvents;
    private bool _suppressStatusBarToggleEvents;
    private bool _suppressLanguageSelectionEvents;
    private bool _suppressTimeZoneSelectionEvents;
    private bool _suppressWeatherLocationEvents;
    private bool _suppressSettingsPersistence;
    private bool _isComponentLibraryOpen;
    private Border? _selectedDesktopComponentHost;
    private bool _reopenSettingsAfterComponentLibraryClose;
    private TranslateTransform? _settingsContentPanelTransform;
    private IBrush? _defaultDesktopBackground;
    private Bitmap? _wallpaperBitmap;
    private Bitmap? _lastValidWallpaperBitmap;
    private string? _lastValidWallpaperPath;
    private WallpaperMediaType _wallpaperMediaType;
    private WallpaperDisplayState _wallpaperDisplayState = WallpaperDisplayState.NoWallpaperConfigured;
    private string _wallpaperPlacement = WallpaperImageBrushFactory.Fill;
    private string _wallpaperType = "Image";
    private Color? _wallpaperSolidColor;
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
    private bool _statusBarClockTransparentBackground;
    private int _desktopEdgeInsetPercent = DefaultEdgeInsetPercent;
    private string _taskbarLayoutMode = TaskbarLayoutBottomFullRowMacStyle;
    private string _languageCode = "zh-CN";
    private WeatherLocationMode _weatherLocationMode = WeatherLocationMode.CitySearch;
    private string _weatherLocationKey = string.Empty;
    private string _weatherLocationName = string.Empty;
    private double _weatherLatitude = 39.9042;
    private double _weatherLongitude = 116.4074;
    private bool _weatherAutoRefreshLocation;
    private string _weatherExcludedAlertsRaw = string.Empty;
    private string _weatherIconPackId = "HyperOS3";
    private bool _weatherNoTlsRequests;
    private bool _autoStartWithWindows;
    private bool _suppressAutoStartToggleEvents;
    private bool _suppressAppRenderModeSelectionEvents;
    private string _selectedAppRenderMode = AppRenderingModeHelper.Default;
    private string _runningAppRenderMode = AppRenderingModeHelper.Default;
    private string _weatherSearchKeyword = string.Empty;
    private bool _isWeatherSearchInProgress;
    private bool _isWeatherPreviewInProgress;
    private ClockDisplayFormat _clockDisplayFormat = ClockDisplayFormat.HourMinuteSecond;
    private bool _externalSettingsReloadPending;
    private int _persistSettingsRevision;
    private int _suppressOwnSettingsReloadCount;
    private double CurrentDesktopPitch => _currentDesktopCellSize + _currentDesktopCellGap;

    public MainWindow()
    {
        var pluginRuntimeService = (Application.Current as App)?.PluginRuntimeService;
        _componentRegistry = DesktopComponentRegistryFactory.Create(pluginRuntimeService);
        _settingsService = _settingsFacade.Settings;
        _gridSettingsService = _settingsFacade.Grid;
        _themeSettingsService = _settingsFacade.Theme;
        _weatherSettingsService = _settingsFacade.Weather;
        _regionSettingsService = _settingsFacade.Region;
        _updateSettingsService = _settingsFacade.Update;
        _timeZoneService = _regionSettingsService.GetTimeZoneService();
        _weatherDataService = _weatherSettingsService.GetWeatherInfoService();

        InitializeComponent();
        Icon = _appLogoService.CreateWindowIcon();
        InitializeTaskbarProfileFlyout();
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);
        _componentEditorRegistry = DesktopComponentEditorRegistryFactory.Create(
            _componentRegistry,
            pluginRuntimeService);
        _componentLibraryService = new ComponentLibraryService(_componentRegistry, _componentRuntimeRegistry);
        _componentEditorWindowService = new ComponentEditorWindowService(_settingsFacade);
        _fluentAvaloniaTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        _settingsService.Changed += OnSettingsChanged;
        _appearanceThemeService.Changed += OnAppearanceThemeChanged;
        PropertyChanged += OnWindowPropertyChanged;
        InitializeDesktopSurfaceSwipeHandlers();
        InitializeDesktopComponentDragHandlers();
        if (Application.Current is App app && app.SettingsWindowService is { } settingsWindowService)
        {
            settingsWindowService.StateChanged += OnSettingsWindowStateChanged;
            _isSettingsOpen = settingsWindowService.IsOpen;
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SyncSettingsWindowState();

        _suppressSettingsPersistence = true;
        var snapshot = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var desktopLayoutSnapshot = _componentLayoutStore.LoadLayout();
        var launcherSnapshot = _settingsService.LoadSnapshot<LauncherSettingsSnapshot>(SettingsScope.Launcher);

        if (!string.IsNullOrWhiteSpace(snapshot.TimeZoneId))
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
        InitializeLocalization(snapshot.LanguageCode);
        InitializeWeatherSettings(snapshot);
        InitializeAutoStartWithWindowsSetting(snapshot);
        InitializeAppRenderModeSetting(snapshot);
        InitializeUpdateSettings(snapshot);
        InitializeDesktopSurfaceState(desktopLayoutSnapshot);
        InitializeLauncherVisibilitySettings(launcherSnapshot);
        InitializeDesktopComponentPlacements(desktopLayoutSnapshot);
        InitializeSettingsIcons();

        if (TryParseColor(snapshot.ThemeColor, out var savedThemeColor))
        {
            _selectedThemeColor = savedThemeColor;
        }

        _isNightMode = snapshot.IsNightMode
            ?? (Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
        _defaultDesktopBackground = CreateNeutralWallpaperFallbackBrush();

        TryRestoreWallpaper(
            snapshot.WallpaperPath,
            snapshot.WallpaperType,
            snapshot.WallpaperColor,
            snapshot.WallpaperPlacement);
        ApplyWallpaperBrush();
        UpdateWallpaperDisplay();

        if (!snapshot.IsNightMode.HasValue)
        {
            _isNightMode = CalculateCurrentBackgroundLuminance() < LightBackgroundLuminanceThreshold;
        }

        ApplyNightModeState(_isNightMode, refreshPalettes: true);
        ApplyLocalization();
        DesktopHost.SizeChanged += OnDesktopHostSizeChanged;
        RebuildDesktopGrid();
        LoadLauncherEntriesAsync();
        InitializeTimeZoneSettings();
        ClockWidget.SetTimeZoneService(_timeZoneService);

        _suppressSettingsPersistence = false;
        PersistSettings();

        TriggerAutoUpdateCheckIfEnabled();
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistSettings();
        _componentEditorWindowService.Close();
        if (_detachedComponentLibraryWindow is not null)
        {
            _detachedComponentLibraryWindow.AddComponentRequested -= OnDetachedComponentLibraryAddComponentRequested;
            _detachedComponentLibraryWindow.Closed -= OnDetachedComponentLibraryClosed;
            _detachedComponentLibraryWindow.Close();
        }
        _detachedComponentLibraryWindow = null;
        DisposeLauncherResources();
        _lastValidWallpaperBitmap?.Dispose();
        _lastValidWallpaperBitmap = null;
        if (_recommendationInfoService is IDisposable recommendationServiceDisposable)
        {
            recommendationServiceDisposable.Dispose();
        }
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _settingsService.Changed -= OnSettingsChanged;
        _appearanceThemeService.Changed -= OnAppearanceThemeChanged;
        PropertyChanged -= OnWindowPropertyChanged;
        DesktopHost.SizeChanged -= OnDesktopHostSizeChanged;
        if (Application.Current is App app && app.SettingsWindowService is { } settingsWindowService)
        {
            settingsWindowService.StateChanged -= OnSettingsWindowStateChanged;
        }
        base.OnClosed(e);
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot snapshot)
    {
        _ = sender;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible)
            {
                return;
            }

            ApplyAdaptiveThemeResources();
            _recommendedColors = snapshot.MonetPalette.RecommendedColors;
            _monetColors = snapshot.MonetPalette.MonetColors;
        }, DispatcherPriority.Background);
    }

    private int CalculateDefaultShortSideCellCountFromDpi()
    {
        var dpi = 96d * RenderScaling;
        var count = (int)Math.Round(dpi / 8d);
        return Math.Clamp(count, MinShortSideCells, MaxShortSideCells);
    }

    private void OnDesktopHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RebuildDesktopGrid();
        PersistSettings();
    }

    private void RebuildDesktopGrid()
    {
        var hostWidth = DesktopHost.Bounds.Width;
        var hostHeight = DesktopHost.Bounds.Height;
        var gapRatio = _gridSettingsService.ResolveGapRatio(_gridSpacingPreset);
        var edgeInset = _gridSettingsService.CalculateEdgeInset(hostWidth, hostHeight, _targetShortSideCells, _desktopEdgeInsetPercent);
        var gridMetrics = _gridSettingsService.CalculateGridMetrics(hostWidth, hostHeight, _targetShortSideCells, gapRatio, edgeInset);
        if (gridMetrics.CellSize <= 0)
        {
            return;
        }
        _currentDesktopCellSize = gridMetrics.CellSize;
        _currentDesktopCellGap = gridMetrics.GapPx;
        _currentDesktopEdgeInset = gridMetrics.EdgeInsetPx;

        DesktopGrid.RowDefinitions.Clear();
        DesktopGrid.ColumnDefinitions.Clear();
        DesktopGrid.Margin = new Thickness(gridMetrics.EdgeInsetPx);
        DesktopGrid.RowSpacing = gridMetrics.GapPx;
        DesktopGrid.ColumnSpacing = gridMetrics.GapPx;
        DesktopGrid.Width = gridMetrics.GridWidthPx;
        DesktopGrid.Height = gridMetrics.GridHeightPx;

        for (var row = 0; row < gridMetrics.RowCount; row++)
        {
            DesktopGrid.RowDefinitions.Add(new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
        }

        for (var col = 0; col < gridMetrics.ColumnCount; col++)
        {
            DesktopGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
        }

        PlaceStatusBarComponent(
            TopStatusBarHost,
            column: 0,
            requestedColumnSpan: gridMetrics.ColumnCount,
            totalColumns: gridMetrics.ColumnCount);

        var taskbarRow = gridMetrics.RowCount - 1;
        Grid.SetRow(BottomTaskbarContainer, taskbarRow);
        Grid.SetColumn(BottomTaskbarContainer, 0);
        Grid.SetRowSpan(BottomTaskbarContainer, 1);
        Grid.SetColumnSpan(BottomTaskbarContainer, gridMetrics.ColumnCount);

        ApplyTopStatusComponentVisibility();
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        ApplyWidgetSizing(gridMetrics.CellSize);
        ApplyDesktopStatusBarComponentSpacing();
        UpdateDesktopSurfaceLayout(gridMetrics);
        UpdateSettingsViewportInsets(gridMetrics.CellSize);
    }

    private void ApplyDesktopStatusBarComponentSpacing()
    {
        ApplyStatusBarComponentSpacingForPanel(TopStatusComponentsPanel, _currentDesktopCellSize);
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
        var spacingPx = Math.Max(0, cellSize) * (percent / 100d);
        panel.Spacing = spacingPx;
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

    private static int ClampComponentSpan(int requestedSpan, int axisCellCount)
    {
        return Math.Clamp(requestedSpan, 1, Math.Max(1, axisCellCount));
    }

    private static int ClampGridIndex(int requestedIndex, int axisCellCount)
    {
        return Math.Clamp(requestedIndex, 0, Math.Max(0, axisCellCount - 1));
    }

    private static void PlaceStatusBarComponent(
        Control component,
        int column,
        int requestedColumnSpan,
        int totalColumns)
    {
        var clampedColumn = ClampGridIndex(column, totalColumns);
        var availableColumns = Math.Max(1, totalColumns - clampedColumn);
        Grid.SetRow(component, StatusBarRowIndex);
        Grid.SetColumn(component, clampedColumn);
        Grid.SetRowSpan(component, 1);
        Grid.SetColumnSpan(component, ClampComponentSpan(requestedColumnSpan, availableColumns));
    }

    private void ApplyWidgetSizing(double cellSize)
    {
        var taskbarCellHeight = Math.Clamp(cellSize * 0.76, 36, 76);
        var taskbarTextSize = Math.Clamp(taskbarCellHeight * 0.36, 12, 22);
        var taskbarIconSize = Math.Clamp(taskbarCellHeight * 0.46, 16, 34);
        var taskbarButtonInset = Math.Clamp(taskbarCellHeight * 0.22, 6, 16);
        var buttonContentSpacing = Math.Clamp(taskbarCellHeight * 0.20, 6, 14);
        var taskbarButtonPadding = new Thickness(taskbarButtonInset);

        // Status bar and taskbar are special surfaces: they should fill their row.
        TopStatusBarHost.Margin = new Thickness(0);
        TopStatusBarHost.Padding = new Thickness(0);

        BottomTaskbarContainer.Margin = new Thickness(0);
        BottomTaskbarContainer.CornerRadius = new CornerRadius(Math.Clamp(taskbarCellHeight * 0.58, 20, 44));
        BottomTaskbarContainer.Padding = new Thickness(Math.Clamp(taskbarCellHeight * 0.16, 6, 14));

        ClockWidget.Margin = new Thickness(0);
        ClockWidget.ApplyCellSize(cellSize);

        var buttonMinWidth = Math.Clamp(taskbarCellHeight * 2.35, 100, 340);

        BackToWindowsButton.Margin = new Thickness(0);
        BackToWindowsButton.Padding = taskbarButtonPadding;
        BackToWindowsButton.FontSize = taskbarTextSize;
        BackToWindowsButton.MinHeight = taskbarCellHeight;
        BackToWindowsButton.MinWidth = buttonMinWidth;
        BackToWindowsIcon.FontSize = taskbarIconSize;
        BackToWindowsTextBlock.FontSize = taskbarTextSize;
        SetButtonContentSpacing(BackToWindowsButton, buttonContentSpacing);

        TaskbarProfileButton.Margin = new Thickness(0);
        TaskbarProfileButton.Padding = new Thickness(0);
        TaskbarProfileButton.MinHeight = taskbarCellHeight;
        TaskbarProfileButton.MinWidth = taskbarCellHeight;
        TaskbarProfileButton.Width = taskbarCellHeight;
        TaskbarProfileButton.Height = taskbarCellHeight;

        var avatarSize = Math.Clamp(taskbarCellHeight * 0.82, 28, 60);
        var avatarRadius = avatarSize / 2d;
        TaskbarProfileAvatarBorder.Width = avatarSize;
        TaskbarProfileAvatarBorder.Height = avatarSize;
        TaskbarProfileAvatarBorder.CornerRadius = new CornerRadius(avatarRadius);
        TaskbarProfileAvatarImage.Width = avatarSize;
        TaskbarProfileAvatarImage.Height = avatarSize;
        TaskbarProfileAvatarFallbackText.FontSize = Math.Clamp(avatarSize * 0.34, 10, 22);

        UpdateComponentLibraryLayout(cellSize);
    }

    private static void SetButtonContentSpacing(Button? button, double spacing)
    {
        if (button?.Content is StackPanel contentPanel)
        {
            contentPanel.Spacing = spacing;
        }
    }

    private void UpdateComponentLibraryLayout(double cellSize)
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        var horizontalMargin = Math.Clamp(cellSize * 0.7, 18, 44);
        var bottomMargin = Math.Clamp(cellSize * 1.4, 56, 190);
        var defaultMargin = new Thickness(horizontalMargin, 20, horizontalMargin, bottomMargin);
        if (!_isComponentLibraryWindowPositionCustomized)
        {
            _savedComponentLibraryMargin = defaultMargin;
        }

        ComponentLibraryWindow.Margin = _savedComponentLibraryMargin;
        ComponentLibraryWindow.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.45, 24, 44));
        ComponentLibraryWindow.Height = Math.Clamp(cellSize * 4.8, 220, 360);
        ComponentLibraryWindow.Width = Math.Clamp(cellSize * 9.2, 360, 760);
    }

    private void UpdateSettingsViewportInsets(double cellSize)
    {
        _ = cellSize;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty)
        {
            return;
        }

        if (WindowState is WindowState.Minimized or WindowState.FullScreen)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (WindowState is not (WindowState.Minimized or WindowState.FullScreen))
            {
                WindowState = WindowState.FullScreen;
            }
        });
    }

    private void InitializeTimeZoneSettings()
    {
        if (TimeZoneComboBox is null)
        {
            return;
        }

        // Populate timezone dropdown items before selecting current timezone.
        _suppressTimeZoneSelectionEvents = true;
        TimeZoneComboBox.Items.Clear();
        var timeZones = _timeZoneService.GetAllTimeZones();
        foreach (var tz in timeZones)
        {
            var displayText = GetLocalizedTimeZoneDisplayName(tz);
            var item = new ComboBoxItem
            {
                Content = displayText,
                Tag = tz.Id
            };
            TimeZoneComboBox.Items.Add(item);

            // Select current time zone.
            if (tz.Id == _timeZoneService.CurrentTimeZone.Id)
            {
                TimeZoneComboBox.SelectedItem = item;
            }
        }
        _suppressTimeZoneSelectionEvents = false;
    }


    private void OnTimeZoneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TimeZoneComboBox is null ||
            _suppressTimeZoneSelectionEvents ||
            TimeZoneComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var timeZoneId = item.Tag?.ToString();
        if (string.IsNullOrEmpty(timeZoneId))
        {
            return;
        }

        _timeZoneService.SetTimeZoneById(timeZoneId);
        PersistSettings();
    }
}
