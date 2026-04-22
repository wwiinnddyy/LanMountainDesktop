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

public partial class MainWindow : Window
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
    private int _systemWallpaperRefreshIntervalSeconds = 300;
    private DispatcherTimer? _systemWallpaperRefreshTimer;
    private readonly ISystemWallpaperProvider _systemWallpaperProvider = HostSystemWallpaperProvider.GetOrCreate();
    private IReadOnlyList<Color> _recommendedColors = Array.Empty<Color>();
    private IReadOnlyList<Color> _monetColors = Array.Empty<Color>();
    private Color _selectedThemeColor = Color.Parse("#FF3B82F6");
    private double _currentDesktopCellSize;
    private double _currentDesktopCellGap;
    private double _currentDesktopEdgeInset;
    private string _gridSpacingPreset = "Relaxed";
    private bool _isSlideAnimationActive;
    private TranslateTransform? _desktopPageSlideTransform;
    private string _statusBarSpacingMode = "Relaxed";
    private int _statusBarCustomSpacingPercent = 12;
    private bool _statusBarClockTransparentBackground;
    private string _clockPosition = "Left"; // Left, Center, Right
    private string _clockFontSize = "Medium"; // Small, Medium, Large
    private bool _showTextCapsule;
    private string _textCapsuleContent = "**Hello** World!";
    private string _textCapsulePosition = "Right"; // Left, Center, Right
    private bool _textCapsuleTransparentBackground;
    private string _textCapsuleFontSize = "Medium"; // Small, Medium, Large
    private bool _showNetworkSpeed;
    private string _networkSpeedPosition = "Right"; // Left, Center, Right
    private string _networkSpeedDisplayMode = "Both"; // Upload, Download, Both
    private bool _networkSpeedTransparentBackground;
    private bool _showNetworkTypeIcon;
    private string _networkSpeedFontSize = "Medium"; // Small, Medium, Large
    private bool _statusBarShadowEnabled;
    private string _statusBarShadowColor = "#000000";
    private double _statusBarShadowOpacity = 0.3;
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
        var pluginRuntimeService = Design.IsDesignMode
            ? null
            : (Application.Current as App)?.PluginRuntimeService;
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
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);
        _componentEditorRegistry = DesktopComponentEditorRegistryFactory.Create(
            _componentRegistry,
            pluginRuntimeService);
        _componentLibraryService = new ComponentLibraryService(_componentRegistry, _componentRuntimeRegistry);
        _componentEditorWindowService = new ComponentEditorWindowService(_settingsFacade);

        if (Design.IsDesignMode)
        {
            ApplyDesignTimePreview();
            return;
        }

        InitializeTaskbarProfileFlyout();
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

    private void ApplyDesignTimePreview()
    {
        Title = "LanMountainDesktop Preview";
        ShowInTaskbar = false;
        DesktopWallpaperLayer.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#FFF6F8FB"), 0d),
                new GradientStop(Color.Parse("#FFE9EEF7"), 0.55d),
                new GradientStop(Color.Parse("#FFDCE5F3"), 1d)
            }
        };
        DesktopWallpaperImageLayer.IsVisible = false;
        LauncherPagePanel.IsVisible = false;
        ComponentLibraryWindow.IsVisible = false;

        BackToWindowsTextBlock.Text = "Back to Windows";
        ComponentLibraryTitleTextBlock.Text = "Widgets";
        ComponentLibraryBackTextBlock.Text = "Back";
        TaskbarProfileDisplayNameTextBlock.Text = "Preview User";
        TaskbarProfileSettingsActionTextBlock.Text = "Settings";
        TaskbarProfileDesktopEditActionTextBlock.Text = "Edit Desktop";
        TaskbarProfileAvatarFallbackText.Text = "P";
        TaskbarProfileHeaderAvatarFallbackText.Text = "P";
        TaskbarProfileButton.IsEnabled = false;
        TaskbarProfilePopup.IsOpen = false;

        ClockWidgetLeft.IsVisible = true;
        ClockWidgetLeft.SetDisplayFormat(ClockDisplayFormat.HourMinute);
        ClockWidgetLeft.SetTransparentBackground(false);

        ConfigureDesignTimeDesktopGrid();
        PopulateDesignTimeDesktopSurface();
    }

    private void ConfigureDesignTimeDesktopGrid()
    {
        const int previewRows = 7;
        const int previewColumns = 12;

        DesktopGrid.RowDefinitions.Clear();
        DesktopGrid.ColumnDefinitions.Clear();

        for (var row = 0; row < previewRows; row++)
        {
            DesktopGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        }

        for (var column = 0; column < previewColumns; column++)
        {
            DesktopGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }

        DesktopGrid.Margin = new Thickness(28);
        DesktopGrid.RowSpacing = 14;
        DesktopGrid.ColumnSpacing = 14;
        DesktopGrid.Width = double.NaN;
        DesktopGrid.Height = double.NaN;

        Grid.SetRow(TopStatusBarHost, 0);
        Grid.SetColumn(TopStatusBarHost, 0);
        Grid.SetRowSpan(TopStatusBarHost, 1);
        Grid.SetColumnSpan(TopStatusBarHost, previewColumns);

        Grid.SetRow(DesktopPagesViewport, 1);
        Grid.SetColumn(DesktopPagesViewport, 0);
        Grid.SetRowSpan(DesktopPagesViewport, previewRows - 2);
        Grid.SetColumnSpan(DesktopPagesViewport, previewColumns);

        Grid.SetRow(BottomTaskbarContainer, previewRows - 1);
        Grid.SetColumn(BottomTaskbarContainer, 0);
        Grid.SetRowSpan(BottomTaskbarContainer, 1);
        Grid.SetColumnSpan(BottomTaskbarContainer, previewColumns);

        DesktopPagesHost.ColumnDefinitions.Clear();
        DesktopPagesHost.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        ClockWidgetLeft.ApplyCellSize(72);
    }

    private void PopulateDesignTimeDesktopSurface()
    {
        DesktopPagesContainer.Children.Clear();
        DesktopPagesContainer.Width = double.NaN;
        DesktopPagesContainer.Height = double.NaN;

        DesktopPagesContainer.Children.Add(CreateDesignTimePreviewCard(
            "Focus Clock",
            "Compact widget preview",
            32,
            32,
            300,
            170,
            "#FFFFFFFF",
            "#FFE8EEF8"));
        DesktopPagesContainer.Children.Add(CreateDesignTimePreviewCard(
            "Weather",
            "26°C  Qingdao",
            360,
            86,
            260,
            132,
            "#FFF8FBFF",
            "#FFDDE8F6"));
        DesktopPagesContainer.Children.Add(CreateDesignTimePreviewCard(
            "Study Session",
            "Deep work · 48 min",
            210,
            248,
            340,
            144,
            "#FFFDFEFF",
            "#FFE7EEF7"));
    }

    private static Border CreateDesignTimePreviewCard(
        string title,
        string subtitle,
        double left,
        double top,
        double width,
        double height,
        string backgroundColor,
        string borderColor)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(left, top, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.Parse(backgroundColor)),
            BorderBrush = new SolidColorBrush(Color.Parse(borderColor)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(28),
            Child = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#FF1E293B"))
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.Parse("#FF64748B"))
                    }
                }
            }
        };
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

        if (Design.IsDesignMode)
        {
            ConfigureDesignTimeDesktopGrid();
            PopulateDesignTimeDesktopSurface();
            return;
        }

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

        ShowInTaskbar = snapshot.ShowInTaskbar;

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
        TelemetryServices.Usage?.TrackMainWindowOpened(
            "MainWindow.OnOpened",
            IsVisible,
            WindowState.ToString());
        DesktopHost.SizeChanged += OnDesktopHostSizeChanged;
        RebuildDesktopGrid();
        LoadLauncherEntriesAsync();
        InitializeTimeZoneSettings();
        ClockWidgetLeft.SetTimeZoneService(_timeZoneService);
        ClockWidgetCenter.SetTimeZoneService(_timeZoneService);
        ClockWidgetRight.SetTimeZoneService(_timeZoneService);

        _suppressSettingsPersistence = false;
        PersistSettings();

        TriggerAutoUpdateCheckIfEnabled();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Design.IsDesignMode)
        {
            base.OnClosed(e);
            return;
        }

        var wasVisible = IsVisible;
        var windowState = WindowState.ToString();

        SaveAllWhiteboardNotes();
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
        TelemetryServices.Usage?.TrackMainWindowClosed(
            "MainWindow.OnClosed",
            wasVisible,
            windowState);
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
            ApplyUnifiedMainRectangleChrome(snapshot);
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
        ApplyStatusBarComponentSpacingForPanel(TopStatusLeftPanel, _currentDesktopCellSize);
        ApplyStatusBarComponentSpacingForPanel(TopStatusCenterPanel, _currentDesktopCellSize);
        ApplyStatusBarComponentSpacingForPanel(TopStatusRightPanel, _currentDesktopCellSize);
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
        ApplyUnifiedMainRectangleChrome();
        BottomTaskbarContainer.Padding = new Thickness(Math.Clamp(taskbarCellHeight * 0.16, 6, 14));

        ClockWidgetLeft.Margin = new Thickness(0);
        ClockWidgetLeft.ApplyCellSize(cellSize);
        ClockWidgetCenter.Margin = new Thickness(0);
        ClockWidgetCenter.ApplyCellSize(cellSize);
        ClockWidgetRight.Margin = new Thickness(0);
        ClockWidgetRight.ApplyCellSize(cellSize);

        TextCapsuleWidgetLeft.Margin = new Thickness(0);
        TextCapsuleWidgetLeft.ApplyCellSize(cellSize);
        TextCapsuleWidgetCenter.Margin = new Thickness(0);
        TextCapsuleWidgetCenter.ApplyCellSize(cellSize);
        TextCapsuleWidgetRight.Margin = new Thickness(0);
        TextCapsuleWidgetRight.ApplyCellSize(cellSize);

        NetworkSpeedWidgetLeft.Margin = new Thickness(0);
        NetworkSpeedWidgetLeft.ApplyCellSize(cellSize);
        NetworkSpeedWidgetCenter.Margin = new Thickness(0);
        NetworkSpeedWidgetCenter.ApplyCellSize(cellSize);
        NetworkSpeedWidgetRight.Margin = new Thickness(0);
        NetworkSpeedWidgetRight.ApplyCellSize(cellSize);

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

    private void ApplyUnifiedMainRectangleChrome(AppearanceThemeSnapshot? snapshot = null)
    {
        var unifiedMainRectangle = new CornerRadius(ResolveUnifiedMainRadiusValue(snapshot));
        BottomTaskbarContainer.CornerRadius = unifiedMainRectangle;

        if (_currentDesktopCellSize > 0)
        {
            ClockWidgetLeft.ApplyCellSize(_currentDesktopCellSize);
            ClockWidgetCenter.ApplyCellSize(_currentDesktopCellSize);
            ClockWidgetRight.ApplyCellSize(_currentDesktopCellSize);
            TextCapsuleWidgetLeft.ApplyCellSize(_currentDesktopCellSize);
            TextCapsuleWidgetCenter.ApplyCellSize(_currentDesktopCellSize);
            TextCapsuleWidgetRight.ApplyCellSize(_currentDesktopCellSize);
            NetworkSpeedWidgetLeft.ApplyCellSize(_currentDesktopCellSize);
            NetworkSpeedWidgetCenter.ApplyCellSize(_currentDesktopCellSize);
            NetworkSpeedWidgetRight.ApplyCellSize(_currentDesktopCellSize);
        }
    }

    private double ResolveUnifiedMainRadiusValue(AppearanceThemeSnapshot? snapshot = null)
    {
        if (snapshot is not null)
        {
            return snapshot.CornerRadiusTokens.Lg.TopLeft;
        }

        return _appearanceThemeService.GetCurrent().CornerRadiusTokens.Lg.TopLeft;
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
        if (_isSlideAnimationActive)
        {
            return;
        }

        SlideOutAndMinimizeAsync();
    }

    private TranslateTransform GetDesktopPageSlideTransform()
    {
        if (_desktopPageSlideTransform is not null)
        {
            return _desktopPageSlideTransform;
        }

        _desktopPageSlideTransform = DesktopPage.RenderTransform as TranslateTransform;
        if (_desktopPageSlideTransform is null)
        {
            _desktopPageSlideTransform = new TranslateTransform();
            DesktopPage.RenderTransform = _desktopPageSlideTransform;
        }

        return _desktopPageSlideTransform;
    }

    private async void SlideOutAndMinimizeAsync()
    {
        _isSlideAnimationActive = true;
        DesktopPage.IsHitTestVisible = false;

        var useSlide = IsSlideTransitionEnabled();
        var slideTransform = GetDesktopPageSlideTransform();

        if (useSlide)
        {
            slideTransform.X = Bounds.Width;
        }

        DesktopPage.Opacity = 0;

        await Task.Delay(useSlide
            ? FluttermotionToken.Intro
            : FluttermotionToken.Page);

        if (!_isSlideAnimationActive)
        {
            return;
        }

        var snapshot = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        if (snapshot.ShowInTaskbar)
        {
            WindowState = WindowState.Minimized;
        }
        else if (Application.Current is App app)
        {
            app.HideMainWindowToTray(this, "MinimizeAction");
        }
        else
        {
            WindowState = WindowState.Minimized;
        }

        slideTransform.X = 0;
        DesktopPage.Opacity = 1;
        DesktopPage.IsHitTestVisible = true;
        _isSlideAnimationActive = false;
    }

    public void PrepareEnterAnimation()
    {
        _isSlideAnimationActive = false;

        var useSlide = IsSlideTransitionEnabled();
        var slideTransform = GetDesktopPageSlideTransform();

        var savedTransitions = DesktopPage.Transitions;
        DesktopPage.Transitions = null;

        DesktopPage.Opacity = 0;

        if (useSlide)
        {
            var screen = Screens.ScreenFromVisual(this);
            var scale = screen?.Scaling ?? 1d;
            var screenWidthDip = screen is null
                ? 1920d
                : screen.WorkingArea.Width / Math.Max(scale, 0.01d);
            slideTransform.X = Bounds.Width > 0 ? Bounds.Width : screenWidthDip;
        }

        DesktopPage.Transitions = savedTransitions;
        DesktopPage.IsHitTestVisible = false;
        _isSlideAnimationActive = true;
    }

    public void PlayEnterAnimation()
    {
        var slideTransform = GetDesktopPageSlideTransform();
        DesktopPage.Opacity = 1;
        slideTransform.X = 0;
        DesktopPage.IsHitTestVisible = true;
        _isSlideAnimationActive = false;
    }

    private bool IsSlideTransitionEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        var snapshot = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        return snapshot.EnableSlideTransition;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty)
        {
            return;
        }

        var newState = (WindowState)e.NewValue!;
        var oldState = (WindowState)e.OldValue!;

        if (oldState == WindowState.Minimized && newState != WindowState.Minimized)
        {
            PrepareEnterAnimation();
            
            if (newState != WindowState.FullScreen)
            {
                WindowState = WindowState.FullScreen;
            }

            Dispatcher.UIThread.Post(() =>
            {
                PlayEnterAnimation();
            }, DispatcherPriority.Background);
            
            return;
        }

        if (newState is WindowState.Minimized or WindowState.FullScreen)
        {
            return;
        }

        if (_isSlideAnimationActive)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isSlideAnimationActive)
            {
                return;
            }

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
