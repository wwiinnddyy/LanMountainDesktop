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
using Line = Avalonia.Controls.Shapes.Line;
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
using LibVLCSharp.Shared;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window, ISettingsWindowAnchorProvider
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

    private const int StatusBarRowIndex = 0;
    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
    private const int MinEdgeInsetPercent = 0;
    private const int MaxEdgeInsetPercent = 30;
    private const int DefaultEdgeInsetPercent = 18;
    private static readonly int SettingsTransitionDurationMs = (int)FluttermotionToken.Page.TotalMilliseconds;
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
        TaskbarActionId.MinimizeToWindows
    ];
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
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
    private readonly IComponentLibraryService _componentLibraryService;
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
    private bool _isUpdatingWallpaperPreviewLayout;
    private bool _isComponentLibraryOpen;
    private Border? _selectedDesktopComponentHost;
    private bool _reopenSettingsAfterComponentLibraryClose;
    private TranslateTransform? _settingsContentPanelTransform;
    private IBrush? _defaultDesktopBackground;
    private Bitmap? _wallpaperBitmap;
    private WallpaperMediaType _wallpaperMediaType;
    private string? _wallpaperVideoPath;
    private LibVLC? _libVlc;
    private MediaPlayer? _videoWallpaperPlayer;
    private Media? _videoWallpaperMedia;
    private readonly object _desktopVideoFrameSync = new();
    private MediaPlayer.LibVLCVideoLockCb? _desktopVideoLockCallback;
    private MediaPlayer.LibVLCVideoUnlockCb? _desktopVideoUnlockCallback;
    private MediaPlayer.LibVLCVideoDisplayCb? _desktopVideoDisplayCallback;
    private DispatcherTimer? _desktopVideoFrameRefreshTimer;
    private IntPtr _desktopVideoFrameBufferPtr;
    private byte[]? _desktopVideoStagingBuffer;
    private WriteableBitmap? _desktopVideoBitmap;
    private WriteableBitmap? _wallpaperPreviewSnapshotBitmap;
    private int _desktopVideoFrameWidth;
    private int _desktopVideoFrameHeight;
    private int _desktopVideoFramePitch;
    private int _desktopVideoFrameBufferSize;
    private int _desktopVideoFrameDirtyFlag;
    private bool _wallpaperPreviewSnapshotPending;
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
    private bool _suppressGridSpacingEvents;
    private bool _suppressGridInsetEvents;
    private bool _suppressStatusBarSpacingEvents;
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
    private string _weatherIconPackId = "FluentRegular";
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
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);
        _componentLibraryService = new ComponentLibraryService(_componentRegistry, _componentRuntimeRegistry);
        _fluentAvaloniaTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        _settingsService.Changed += OnSettingsChanged;
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
        _defaultDesktopBackground = DesktopWallpaperLayer.Background;
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

        TryRestoreWallpaper(snapshot.WallpaperPath);
        ApplyWallpaperBrush();
        UpdateWallpaperDisplay();

        if (TryParseColor(snapshot.ThemeColor, out var savedThemeColor))
        {
            _selectedThemeColor = savedThemeColor;
        }

        _isNightMode = snapshot.IsNightMode ?? (CalculateCurrentBackgroundLuminance() < LightBackgroundLuminanceThreshold);
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
        if (_detachedComponentLibraryWindow is not null)
        {
            _detachedComponentLibraryWindow.AddComponentRequested -= OnDetachedComponentLibraryAddComponentRequested;
            _detachedComponentLibraryWindow.Closed -= OnDetachedComponentLibraryClosed;
            _detachedComponentLibraryWindow.Close();
        }
        _detachedComponentLibraryWindow = null;
        StopVideoWallpaper();
        DisposeLauncherResources();
        _videoWallpaperMedia?.Dispose();
        _videoWallpaperMedia = null;
        _videoWallpaperPlayer?.Dispose();
        _videoWallpaperPlayer = null;
        _desktopVideoFrameRefreshTimer?.Stop();
        _desktopVideoFrameRefreshTimer = null;
        _wallpaperPreviewSnapshotBitmap?.Dispose();
        _wallpaperPreviewSnapshotBitmap = null;
        _libVlc?.Dispose();
        _libVlc = null;
        if (_recommendationInfoService is IDisposable recommendationServiceDisposable)
        {
            recommendationServiceDisposable.Dispose();
        }
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _settingsService.Changed -= OnSettingsChanged;
        PropertyChanged -= OnWindowPropertyChanged;
        DesktopHost.SizeChanged -= OnDesktopHostSizeChanged;
        if (Application.Current is App app && app.SettingsWindowService is { } settingsWindowService)
        {
            settingsWindowService.StateChanged -= OnSettingsWindowStateChanged;
        }
        base.OnClosed(e);
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

    private void OnWallpaperPreviewHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateWallpaperPreviewLayout();
    }

    private void OnGridPreviewHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateGridPreviewLayout();
    }

    private void OnGridSizeSliderChanged(object? sender, RoutedEventArgs e)
    {
        if (GridSizeSlider is null || GridSizeNumberBox is null)
        {
            return;
        }

        var sliderValue = (int)Math.Round(GridSizeSlider.Value);
        if (Math.Abs(GridSizeNumberBox.Value - sliderValue) > double.Epsilon)
        {
            GridSizeNumberBox.Value = sliderValue;
        }
        UpdateGridPreviewLayout();
    }

    private void OnGridSizeNumberBoxChanged(object? sender, NumberBoxValueChangedEventArgs e)
    {
        if (GridSizeSlider is null || GridSizeNumberBox is null)
        {
            return;
        }

        var numberBoxValue = (int)Math.Round(GridSizeNumberBox.Value);
        if (Math.Abs(GridSizeSlider.Value - numberBoxValue) > double.Epsilon)
        {
            GridSizeSlider.Value = numberBoxValue;
        }
        UpdateGridPreviewLayout();
    }

    private void OnGridEdgeInsetSliderChanged(object? sender, RoutedEventArgs e)
    {
        if (GridEdgeInsetSlider is null)
        {
            return;
        }

        if (_suppressGridInsetEvents)
        {
            return;
        }

        var value = (int)Math.Round(GridEdgeInsetSlider.Value);
        SetPendingGridEdgeInsetPercent(value, updateSlider: false, updateNumberBox: true);
        UpdateGridPreviewLayout();
    }

    private void OnGridEdgeInsetNumberBoxChanged(object? sender, NumberBoxValueChangedEventArgs e)
    {
        if (GridEdgeInsetNumberBox is null)
        {
            return;
        }

        if (_suppressGridInsetEvents)
        {
            return;
        }

        var value = (int)Math.Round(GridEdgeInsetNumberBox.Value);
        SetPendingGridEdgeInsetPercent(value, updateSlider: true, updateNumberBox: false);
        UpdateGridPreviewLayout();
    }

    private void SetPendingGridEdgeInsetPercent(int percent, bool updateSlider, bool updateNumberBox)
    {
        var clamped = Math.Clamp(percent, MinEdgeInsetPercent, MaxEdgeInsetPercent);

        _suppressGridInsetEvents = true;
        try
        {
            if (updateSlider && Math.Abs(GridEdgeInsetSlider.Value - clamped) > double.Epsilon)
            {
                GridEdgeInsetSlider.Value = clamped;
            }

            if (updateNumberBox && Math.Abs(GridEdgeInsetNumberBox.Value - clamped) > double.Epsilon)
            {
                GridEdgeInsetNumberBox.Value = clamped;
            }
        }
        finally
        {
            _suppressGridInsetEvents = false;
        }
    }

    private void OnGridSpacingPresetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressGridSpacingEvents)
        {
            return;
        }

        UpdateGridPreviewLayout();
    }

    private void OnStatusBarSpacingModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (StatusBarSpacingModeComboBox is null)
        {
            return;
        }

        if (_suppressStatusBarSpacingEvents)
        {
            return;
        }

        _statusBarSpacingMode = NormalizeStatusBarSpacingMode(
            TryGetSelectedComboBoxTag(StatusBarSpacingModeComboBox) ?? _statusBarSpacingMode);

        StatusBarSpacingCustomPanel.IsVisible = string.Equals(_statusBarSpacingMode, "Custom", StringComparison.OrdinalIgnoreCase);

        ApplyDesktopStatusBarComponentSpacing();
        UpdateWallpaperPreviewLayout();
        UpdateGridPreviewLayout();
        SchedulePersistSettings();
    }

    private void OnStatusBarSpacingSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (StatusBarSpacingSlider is null)
        {
            return;
        }

        if (_suppressStatusBarSpacingEvents)
        {
            return;
        }

        var percent = (int)Math.Round(StatusBarSpacingSlider.Value);
        SetStatusBarCustomSpacingPercent(percent, updateSlider: false, updateNumberBox: true);

        if (string.Equals(_statusBarSpacingMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDesktopStatusBarComponentSpacing();
            UpdateWallpaperPreviewLayout();
            UpdateGridPreviewLayout();
        }

        SchedulePersistSettings();
    }

    private void OnStatusBarSpacingNumberBoxChanged(object? sender, NumberBoxValueChangedEventArgs e)
    {
        if (StatusBarSpacingNumberBox is null)
        {
            return;
        }

        if (_suppressStatusBarSpacingEvents)
        {
            return;
        }

        var percent = (int)Math.Round(StatusBarSpacingNumberBox.Value);
        SetStatusBarCustomSpacingPercent(percent, updateSlider: true, updateNumberBox: false);

        if (string.Equals(_statusBarSpacingMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDesktopStatusBarComponentSpacing();
            UpdateWallpaperPreviewLayout();
            UpdateGridPreviewLayout();
        }

        SchedulePersistSettings();
    }

    private void SetStatusBarCustomSpacingPercent(int percent, bool updateSlider, bool updateNumberBox)
    {
        percent = Math.Clamp(percent, 0, 30);
        _statusBarCustomSpacingPercent = percent;

        _suppressStatusBarSpacingEvents = true;
        try
        {
            if (updateSlider && Math.Abs(StatusBarSpacingSlider.Value - percent) > double.Epsilon)
            {
                StatusBarSpacingSlider.Value = percent;
            }

            if (updateNumberBox && Math.Abs(StatusBarSpacingNumberBox.Value - percent) > double.Epsilon)
            {
                StatusBarSpacingNumberBox.Value = percent;
            }
        }
        finally
        {
            _suppressStatusBarSpacingEvents = false;
        }
    }

    private void UpdateGridPreviewLayout()
    {
        if (GridPreviewFrame is null ||
            GridPreviewHost is null ||
            GridPreviewViewport is null ||
            GridPreviewGrid is null ||
            GridPreviewLinesCanvas is null ||
            GridSizeSlider is null)
        {
            return;
        }

        var previewShortSideCells = (int)Math.Round(GridSizeSlider.Value);
        if (previewShortSideCells < MinShortSideCells || previewShortSideCells > MaxShortSideCells)
        {
            previewShortSideCells = _targetShortSideCells;
        }

        var desktopWidth = Math.Max(1, DesktopHost.Bounds.Width);
        var desktopHeight = Math.Max(1, DesktopHost.Bounds.Height);
        var aspectRatio = desktopWidth / desktopHeight;

        var availableWidth = Math.Max(100, GridPreviewHost.Bounds.Width);
        
        var framePadding = GridPreviewFrame.Padding;
        var horizontalPadding = framePadding.Left + framePadding.Right;
        var verticalPadding = framePadding.Top + framePadding.Bottom;

        var gridPreviewWidth = availableWidth;
        var gridPreviewHeight = gridPreviewWidth / aspectRatio;

        GridPreviewFrame.Width = gridPreviewWidth;
        GridPreviewFrame.Height = gridPreviewHeight;

        var innerWidth = Math.Max(1, gridPreviewWidth - horizontalPadding);
        var innerHeight = Math.Max(1, gridPreviewHeight - verticalPadding);
        var preset = _gridSettingsService.NormalizeSpacingPreset(TryGetSelectedComboBoxTag(GridSpacingPresetComboBox) ?? _gridSpacingPreset);
        var gapRatio = _gridSettingsService.ResolveGapRatio(preset);
        var pendingEdgeInsetPercent = ResolvePendingGridEdgeInsetPercent();
        var edgeInset = _gridSettingsService.CalculateEdgeInset(innerWidth, innerHeight, previewShortSideCells, pendingEdgeInsetPercent);
        var gridMetrics = _gridSettingsService.CalculateGridMetrics(innerWidth, innerHeight, previewShortSideCells, gapRatio, edgeInset);
        if (gridMetrics.CellSize <= 0)
        {
            return;
        }

        var inset = new Thickness(gridMetrics.EdgeInsetPx);
        GridPreviewGrid.Margin = inset;
        GridPreviewGrid.RowSpacing = gridMetrics.GapPx;
        GridPreviewGrid.ColumnSpacing = gridMetrics.GapPx;
        GridPreviewGrid.Width = gridMetrics.GridWidthPx;
        GridPreviewGrid.Height = gridMetrics.GridHeightPx;

        GridPreviewLinesCanvas.Margin = inset;

        GridPreviewGrid.RowDefinitions.Clear();
        GridPreviewGrid.ColumnDefinitions.Clear();

        for (var row = 0; row < gridMetrics.RowCount; row++)
        {
            GridPreviewGrid.RowDefinitions.Add(
                new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
        }

        for (var col = 0; col < gridMetrics.ColumnCount; col++)
        {
            GridPreviewGrid.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
        }

        PlaceStatusBarComponent(
            GridPreviewTopStatusBarHost,
            column: 0,
            requestedColumnSpan: gridMetrics.ColumnCount,
            totalColumns: gridMetrics.ColumnCount);

        var taskbarRow = gridMetrics.RowCount - 1;
        Grid.SetRow(GridPreviewBottomTaskbarContainer, taskbarRow);
        Grid.SetColumn(GridPreviewBottomTaskbarContainer, 0);
        Grid.SetRowSpan(GridPreviewBottomTaskbarContainer, 1);
        Grid.SetColumnSpan(GridPreviewBottomTaskbarContainer, gridMetrics.ColumnCount);

        ApplyGridPreviewWidgetSizing(gridMetrics.CellSize);
        ApplyStatusBarComponentSpacingForPanel(GridPreviewTopStatusComponentsPanel, gridMetrics.CellSize);
        UpdateGridEdgeInsetComputedPxText(gridMetrics.CellSize);

        if (GridInfoTextBlock is not null)
        {
            GridInfoTextBlock.Text = Lf(
                "settings.grid.info_format",
                "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)",
                gridMetrics.ColumnCount,
                gridMetrics.RowCount,
                gridMetrics.CellSize);
        }

        DrawGridPreviewLines(gridMetrics);
    }

    private void DrawGridPreviewLines(DesktopGridMetrics gridMetrics)
    {
        if (GridPreviewLinesCanvas is null || GridPreviewViewport is null || GridPreviewGrid is null)
        {
            return;
        }

        var viewportBackground = GridPreviewViewport.Background as SolidColorBrush;
        var backgroundColor = viewportBackground?.Color ?? Color.Parse("#30111827");
        var luminance = CalculateRelativeLuminance(backgroundColor);
        var lineColor = luminance >= LightBackgroundLuminanceThreshold 
            ? Color.Parse("#80000000") 
            : Color.Parse("#80FFFFFF");

        GridPreviewLinesCanvas.Children.Clear();
        
        var cellSize = gridMetrics.CellSize;
        var pitch = gridMetrics.Pitch;
        var gridWidth = gridMetrics.GridWidthPx;
        var gridHeight = gridMetrics.GridHeightPx;
        
        GridPreviewLinesCanvas.Width = gridWidth;
        GridPreviewLinesCanvas.Height = gridHeight;

        var dashLength = cellSize * 0.3;
        var gapLength = cellSize * 0.2;

        for (var row = 0; row <= gridMetrics.RowCount; row++)
        {
            var y = row == gridMetrics.RowCount ? gridHeight : row * pitch;
            var line = new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(gridWidth, y),
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { dashLength, gapLength },
                IsHitTestVisible = false
            };
            GridPreviewLinesCanvas.Children.Add(line);
        }

        for (var col = 0; col <= gridMetrics.ColumnCount; col++)
        {
            var x = col == gridMetrics.ColumnCount ? gridWidth : col * pitch;
            var line = new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, gridHeight),
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { dashLength, gapLength },
                IsHitTestVisible = false
            };
            GridPreviewLinesCanvas.Children.Add(line);
        }
    }

    private void ApplyGridPreviewWidgetSizing(double cellSize)
    {
        var previewTaskbarCell = Math.Clamp(cellSize * 0.74, 10, 30);
        var iconSize = Math.Clamp(cellSize * 0.35, 8, 16);
        
        GridPreviewTopStatusBarHost.Padding = new Thickness(0);
        GridPreviewBottomTaskbarContainer.Margin = new Thickness(0);
        GridPreviewBottomTaskbarContainer.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.45, 16, 32));
        GridPreviewBottomTaskbarContainer.Padding = new Thickness(Math.Clamp(cellSize * 0.06, 1, 4));

        GridPreviewBackButtonTextBlock.FontSize = Math.Clamp(cellSize * 0.19, 5, 13);
        GridPreviewComponentLibraryTextBlock.FontSize = Math.Clamp(cellSize * 0.18, 5, 12);
        GridPreviewComponentLibraryIcon.FontSize = iconSize;
        GridPreviewBackButtonVisual.MinHeight = previewTaskbarCell;
        GridPreviewBackButtonVisual.MinWidth = Math.Clamp(cellSize * 2.1, 30, 120);
        GridPreviewComponentLibraryVisual.MinHeight = previewTaskbarCell;
        GridPreviewComponentLibraryVisual.MinWidth = Math.Clamp(cellSize * 2.0, 28, 110);
        GridPreviewSettingsButtonIcon.Width = Math.Clamp(previewTaskbarCell * 0.42, 6, 14);
        GridPreviewSettingsButtonIcon.Height = Math.Clamp(previewTaskbarCell * 0.42, 6, 14);
    }

    private void OnApplyGridSizeClick(object? sender, RoutedEventArgs e)
    {
        if (GridSizeNumberBox is null || GridSizeSlider is null)
        {
            return;
        }

        _gridSpacingPreset = _gridSettingsService.NormalizeSpacingPreset(
            TryGetSelectedComboBoxTag(GridSpacingPresetComboBox) ?? _gridSpacingPreset);
        _desktopEdgeInsetPercent = ResolvePendingGridEdgeInsetPercent();

        var requested = (int)Math.Round(GridSizeNumberBox.Value);
        if (requested <= 0)
        {
            requested = _targetShortSideCells;
        }

        _targetShortSideCells = Math.Clamp(requested, MinShortSideCells, MaxShortSideCells);

        if (Math.Abs(GridSizeNumberBox.Value - _targetShortSideCells) > double.Epsilon)
        {
            GridSizeNumberBox.Value = _targetShortSideCells;
        }

        if (Math.Abs(GridSizeSlider.Value - _targetShortSideCells) > double.Epsilon)
        {
            GridSizeSlider.Value = _targetShortSideCells;
        }

        SetPendingGridEdgeInsetPercent(_desktopEdgeInsetPercent, updateSlider: true, updateNumberBox: true);

        RebuildDesktopGrid();
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

        if (ClockWidget is ClockWidget clock)
        {
            clock.SetDisplayFormat(_clockDisplayFormat);
        }

        ApplyTopStatusComponentVisibility();
        UpdateWallpaperPreviewLayout();
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
        UpdateGridEdgeInsetComputedPxText(gridMetrics.CellSize);

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

        if (GridInfoTextBlock is not null)
        {
            GridInfoTextBlock.Text = Lf(
                "settings.grid.info_format",
                "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)",
                gridMetrics.ColumnCount,
                gridMetrics.RowCount,
                gridMetrics.CellSize);
        }

        UpdateWallpaperPreviewLayout();
    }

    private void ApplyDesktopStatusBarComponentSpacing()
    {
        ApplyStatusBarComponentSpacingForPanel(TopStatusComponentsPanel, _currentDesktopCellSize);
        UpdateStatusBarSpacingComputedPxText(_currentDesktopCellSize);
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

    private void UpdateStatusBarSpacingComputedPxText(double cellSize)
    {
        if (StatusBarSpacingComputedPxTextBlock is null)
        {
            return;
        }

        var percent = ResolveStatusBarSpacingPercent();
        var spacingPx = Math.Max(0, cellSize) * (percent / 100d);
        StatusBarSpacingComputedPxTextBlock.Text = Lf(
            "settings.status_bar.spacing_custom_px_format",
            ">= {0:F1}px",
            spacingPx);
    }

    private int ResolvePendingGridEdgeInsetPercent()
    {
        if (GridEdgeInsetNumberBox is null)
        {
            return _desktopEdgeInsetPercent;
        }

        var pending = (int)Math.Round(GridEdgeInsetNumberBox.Value);
        return Math.Clamp(pending, MinEdgeInsetPercent, MaxEdgeInsetPercent);
    }

    private void UpdateGridEdgeInsetComputedPxText(double cellSize)
    {
        if (GridEdgeInsetComputedPxTextBlock is null)
        {
            return;
        }

        var percent = ResolvePendingGridEdgeInsetPercent();
        var insetPx = Math.Clamp(Math.Max(0, cellSize) * (percent / 100d), 0, 80);
        GridEdgeInsetComputedPxTextBlock.Text = Lf(
            "settings.grid.edge_inset_px_format",
            "{0:F1}px",
            insetPx);
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

        OpenSettingsButton.Margin = new Thickness(0);
        OpenSettingsButton.Padding = taskbarButtonPadding;
        OpenSettingsButton.FontSize = taskbarTextSize;
        OpenSettingsButton.MinHeight = taskbarCellHeight;
        OpenSettingsButton.MinWidth = OpenSettingsButtonTextBlock.IsVisible
            ? Math.Clamp(taskbarCellHeight * 2.35, 100, 340)
            : Math.Clamp(taskbarCellHeight * 1.10, 48, 88);
        OpenSettingsIcon.FontSize = taskbarIconSize;
        OpenSettingsButtonTextBlock.FontSize = taskbarTextSize;
        SetButtonContentSpacing(OpenSettingsButton, buttonContentSpacing);
        
        OpenComponentLibraryButton.Margin = new Thickness(0);
        OpenComponentLibraryButton.Padding = taskbarButtonPadding;
        OpenComponentLibraryButton.FontSize = taskbarTextSize;
        OpenComponentLibraryButton.MinHeight = taskbarCellHeight;
        OpenComponentLibraryButton.MinWidth = Math.Clamp(taskbarCellHeight * 2.15, 92, 320);
        OpenComponentLibraryIcon.FontSize = taskbarIconSize;
        OpenComponentLibraryTextBlock.FontSize = taskbarTextSize;
        SetButtonContentSpacing(OpenComponentLibraryButton, buttonContentSpacing);

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

    private void UpdateWallpaperPreviewLayout()
    {
        if (WallpaperPreviewFrame is null ||
            WallpaperPreviewHost is null ||
            WallpaperPreviewViewport is null ||
            WallpaperPreviewGrid is null)
        {
            return;
        }

        if (_isUpdatingWallpaperPreviewLayout)
        {
            return;
        }

        _isUpdatingWallpaperPreviewLayout = true;
        try
        {
            var desktopWidth = Math.Max(1, DesktopHost.Bounds.Width);
            var desktopHeight = Math.Max(1, DesktopHost.Bounds.Height);
            var aspectRatio = desktopWidth / desktopHeight;

            var availableWidth = Math.Max(100, WallpaperPreviewHost.Bounds.Width);
            var availableHeight = WallpaperPreviewHost.Bounds.Height;
            // During initial measure, host height can be too small and cause the preview to collapse.
            // Ignore tiny heights so width-driven sizing can stabilize first.
            if (availableHeight < 120)
            {
                availableHeight = double.PositiveInfinity;
            }
            
            var framePadding = WallpaperPreviewFrame.Padding;
            var horizontalPadding = framePadding.Left + framePadding.Right;
            var verticalPadding = framePadding.Top + framePadding.Bottom;

            var previewWidth = Math.Min(availableWidth, WallpaperPreviewMaxWidth);
            var previewHeight = previewWidth / aspectRatio;
            if (double.IsFinite(availableHeight) && previewHeight > availableHeight)
            {
                previewHeight = availableHeight;
                previewWidth = previewHeight * aspectRatio;
            }

            WallpaperPreviewFrame.Width = previewWidth;
            WallpaperPreviewFrame.Height = previewHeight;



            var innerWidth = Math.Max(1, previewWidth - horizontalPadding);
            var innerHeight = Math.Max(1, previewHeight - verticalPadding);
            var gapRatio = _gridSettingsService.ResolveGapRatio(_gridSpacingPreset);
            var edgeInset = _gridSettingsService.CalculateEdgeInset(innerWidth, innerHeight, _targetShortSideCells, _desktopEdgeInsetPercent);
            var gridMetrics = _gridSettingsService.CalculateGridMetrics(innerWidth, innerHeight, _targetShortSideCells, gapRatio, edgeInset);
            if (gridMetrics.CellSize <= 0)
            {
                return;
            }

            WallpaperPreviewGrid.Margin = new Thickness(gridMetrics.EdgeInsetPx);
            WallpaperPreviewGrid.RowSpacing = gridMetrics.GapPx;
            WallpaperPreviewGrid.ColumnSpacing = gridMetrics.GapPx;
            WallpaperPreviewGrid.Width = gridMetrics.GridWidthPx;
            WallpaperPreviewGrid.Height = gridMetrics.GridHeightPx;

            // This can be triggered by layout changes; always rebuild the preview grid definitions
            // to avoid definitions accumulating and shifting overlay components out of place.
            WallpaperPreviewGrid.RowDefinitions.Clear();
            WallpaperPreviewGrid.ColumnDefinitions.Clear();

            for (var row = 0; row < gridMetrics.RowCount; row++)
            {
                WallpaperPreviewGrid.RowDefinitions.Add(
                    new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            for (var col = 0; col < gridMetrics.ColumnCount; col++)
            {
                WallpaperPreviewGrid.ColumnDefinitions.Add(
                    new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            PlaceStatusBarComponent(
                WallpaperPreviewTopStatusBarHost,
                column: 0,
                requestedColumnSpan: gridMetrics.ColumnCount,
                totalColumns: gridMetrics.ColumnCount);

            var taskbarRow = gridMetrics.RowCount - 1;
            Grid.SetRow(WallpaperPreviewBottomTaskbarContainer, taskbarRow);
            Grid.SetColumn(WallpaperPreviewBottomTaskbarContainer, 0);
            Grid.SetRowSpan(WallpaperPreviewBottomTaskbarContainer, 1);
            Grid.SetColumnSpan(WallpaperPreviewBottomTaskbarContainer, gridMetrics.ColumnCount);

            ApplyTopStatusComponentVisibility();
            ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
            ApplyPreviewWidgetSizing(gridMetrics.CellSize);
            ApplyStatusBarComponentSpacingForPanel(WallpaperPreviewTopStatusComponentsPanel, gridMetrics.CellSize);
        }
        finally
        {
            _isUpdatingWallpaperPreviewLayout = false;
        }
    }

    private void ApplyPreviewWidgetSizing(double cellSize)
    {
        var previewTaskbarCell = Math.Clamp(cellSize * 0.74, 10, 28);
        var previewTextSize = Math.Clamp(previewTaskbarCell * 0.38, 7, 14);
        var previewIconSize = Math.Clamp(previewTaskbarCell * 0.46, 8, 16);
        var previewInset = Math.Clamp(previewTaskbarCell * 0.20, 2, 6);
        var previewContentSpacing = Math.Clamp(previewTaskbarCell * 0.20, 2, 6);
        
        // Match desktop behavior: special bars fill their preview row.
        WallpaperPreviewTopStatusBarHost.Margin = new Thickness(0);
        WallpaperPreviewTopStatusBarHost.Padding = new Thickness(0);

        WallpaperPreviewBottomTaskbarContainer.Margin = new Thickness(0);
        WallpaperPreviewBottomTaskbarContainer.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.45, 6, 14));
        WallpaperPreviewBottomTaskbarContainer.Padding = new Thickness(previewInset);

        WallpaperPreviewClockWidget.ApplyCellSize(cellSize);
        WallpaperPreviewBackButtonTextBlock.FontSize = previewTextSize;
        WallpaperPreviewComponentLibraryTextBlock.FontSize = previewTextSize;
        WallpaperPreviewBackButtonVisual.Spacing = previewContentSpacing;
        WallpaperPreviewComponentLibraryVisual.Spacing = previewContentSpacing;
        
        WallpaperPreviewBackButtonVisual.MinHeight = previewTaskbarCell;
        WallpaperPreviewBackButtonVisual.MinWidth = Math.Clamp(cellSize * 2.1, 30, 120);
        WallpaperPreviewComponentLibraryVisual.MinHeight = previewTaskbarCell;
        WallpaperPreviewComponentLibraryVisual.MinWidth = Math.Clamp(cellSize * 2.0, 28, 110);
        
        WallpaperPreviewSettingsButtonIcon.Width = previewIconSize;
        WallpaperPreviewSettingsButtonIcon.Height = previewIconSize;
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
