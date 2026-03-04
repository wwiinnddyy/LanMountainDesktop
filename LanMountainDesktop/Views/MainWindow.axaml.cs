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
using FluentAvalonia.Styling;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.ComponentSystem.Extensions;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;
using LibVLCSharp.Shared;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window
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
    private const int SettingsTransitionDurationMs = 240;
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
    private readonly LocalizationService _localizationService = new();
    private readonly TimeZoneService _timeZoneService = new();
    private readonly IWeatherDataService _weatherDataService = new XiaomiWeatherService();
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ComponentRegistry _componentRegistry = ComponentRegistry
        .CreateDefault()
        .RegisterExtensions(
            JsonComponentExtensionProvider.LoadProvidersFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Extensions", "Components")));
    private readonly DesktopComponentRuntimeRegistry _componentRuntimeRegistry;
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
    private MediaPlayer? _previewVideoWallpaperPlayer;
    private Media? _previewVideoWallpaperMedia;
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
    private string _weatherSearchKeyword = string.Empty;
    private bool _isWeatherSearchInProgress;
    private bool _isWeatherPreviewInProgress;
    private ClockDisplayFormat _clockDisplayFormat = ClockDisplayFormat.HourMinuteSecond;

    private double CurrentDesktopPitch => _currentDesktopCellSize + _currentDesktopCellGap;

    public MainWindow()
    {
        InitializeComponent();
        _componentRuntimeRegistry = DesktopComponentRuntimeRegistry.CreateDefault(_componentRegistry);
        _fluentAvaloniaTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        PropertyChanged += OnWindowPropertyChanged;
        InitializeDesktopComponentDragHandlers();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _suppressSettingsPersistence = true;
        var snapshot = _appSettingsService.Load();

        if (!string.IsNullOrWhiteSpace(snapshot.TimeZoneId))
        {
            _timeZoneService.SetTimeZoneById(snapshot.TimeZoneId);
        }

        _targetShortSideCells = Math.Clamp(
            snapshot.GridShortSideCells > 0 ? snapshot.GridShortSideCells : CalculateDefaultShortSideCellCountFromDpi(),
            MinShortSideCells,
            MaxShortSideCells);

        _gridSpacingPreset = NormalizeGridSpacingPreset(snapshot.GridSpacingPreset);
        _suppressGridSpacingEvents = true;
        GridSpacingPresetComboBox.SelectedIndex = string.Equals(_gridSpacingPreset, "Compact", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _suppressGridSpacingEvents = false;

        _desktopEdgeInsetPercent = Math.Clamp(snapshot.DesktopEdgeInsetPercent, MinEdgeInsetPercent, MaxEdgeInsetPercent);
        _suppressGridInsetEvents = true;
        GridEdgeInsetSlider.Value = _desktopEdgeInsetPercent;
        GridEdgeInsetNumberBox.Value = _desktopEdgeInsetPercent;
        _suppressGridInsetEvents = false;
        GridEdgeInsetNumberBox.ValueChanged += OnGridEdgeInsetNumberBoxChanged;

        _statusBarSpacingMode = NormalizeStatusBarSpacingMode(snapshot.StatusBarSpacingMode);
        _statusBarCustomSpacingPercent = Math.Clamp(snapshot.StatusBarCustomSpacingPercent, 0, 30);
        _suppressStatusBarSpacingEvents = true;
        StatusBarSpacingModeComboBox.SelectedIndex = _statusBarSpacingMode switch
        {
            "Compact" => 0,
            "Custom" => 2,
            _ => 1
        };
        StatusBarSpacingSlider.Value = _statusBarCustomSpacingPercent;
        StatusBarSpacingNumberBox.Value = _statusBarCustomSpacingPercent;
        StatusBarSpacingCustomPanel.IsVisible = string.Equals(_statusBarSpacingMode, "Custom", StringComparison.OrdinalIgnoreCase);
        _suppressStatusBarSpacingEvents = false;
        StatusBarSpacingNumberBox.ValueChanged += OnStatusBarSpacingNumberBoxChanged;

        GridSizeNumberBox.Value = _targetShortSideCells;
        GridSizeSlider.Value = _targetShortSideCells;
        GridSizeSlider.ValueChanged += OnGridSizeSliderChanged;
        GridSizeNumberBox.ValueChanged += OnGridSizeNumberBoxChanged;

        SettingsNavListBox.SelectedIndex = Math.Clamp(snapshot.SettingsTabIndex, 0, 6);
        UpdateSettingsTabContent();

        WallpaperPlacementComboBox.SelectedIndex = GetPlacementIndexFromSetting(snapshot.WallpaperPlacement);
        _defaultDesktopBackground = DesktopWallpaperLayer.Background;
        ApplyTaskbarSettings(snapshot);
        InitializeLocalization(snapshot.LanguageCode);
        InitializeWeatherSettings(snapshot);
        InitializeDesktopSurfaceState(snapshot);
        InitializeDesktopComponentPlacements(snapshot);
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
        _suppressStatusBarToggleEvents = true;
        StatusBarClockToggleSwitch.IsChecked = _topStatusComponentIds.Contains(BuiltInComponentIds.Clock);
        _suppressStatusBarToggleEvents = false;
        ApplyLocalization();
        ThemeColorStatusTextBlock.Text = Lf("settings.color.theme_ready_format", "Theme color ready: {0}.", _selectedThemeColor);
        _settingsContentPanelTransform = SettingsContentPanel.RenderTransform as TranslateTransform;
        DesktopHost.SizeChanged += OnDesktopHostSizeChanged;
        WallpaperPreviewHost.SizeChanged += OnWallpaperPreviewHostSizeChanged;
        GridPreviewHost.SizeChanged += OnGridPreviewHostSizeChanged;
        RebuildDesktopGrid();
        LoadLauncherEntriesAsync();
        InitializeTimeZoneSettings();
        ClockWidget.SetTimeZoneService(_timeZoneService);

        _suppressSettingsPersistence = false;
        PersistSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistSettings();
        StopVideoWallpaper();
        _previewVideoWallpaperMedia?.Dispose();
        _previewVideoWallpaperMedia = null;
        _previewVideoWallpaperPlayer?.Dispose();
        _previewVideoWallpaperPlayer = null;
        DisposeLauncherResources();
        _videoWallpaperMedia?.Dispose();
        _videoWallpaperMedia = null;
        _videoWallpaperPlayer?.Dispose();
        _videoWallpaperPlayer = null;
        _libVlc?.Dispose();
        _libVlc = null;
        if (_weatherDataService is IDisposable weatherServiceDisposable)
        {
            weatherServiceDisposable.Dispose();
        }
        if (_recommendationInfoService is IDisposable recommendationServiceDisposable)
        {
            recommendationServiceDisposable.Dispose();
        }
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        PropertyChanged -= OnWindowPropertyChanged;
        DesktopHost.SizeChanged -= OnDesktopHostSizeChanged;
        WallpaperPreviewHost.SizeChanged -= OnWallpaperPreviewHostSizeChanged;
        GridPreviewHost.SizeChanged -= OnGridPreviewHostSizeChanged;
        GridSizeSlider.ValueChanged -= OnGridSizeSliderChanged;
        GridSizeNumberBox.ValueChanged -= OnGridSizeNumberBoxChanged;
        GridEdgeInsetNumberBox.ValueChanged -= OnGridEdgeInsetNumberBoxChanged;
        StatusBarSpacingNumberBox.ValueChanged -= OnStatusBarSpacingNumberBoxChanged;
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
        var sliderValue = (int)Math.Round(GridSizeSlider.Value);
        if (Math.Abs(GridSizeNumberBox.Value - sliderValue) > double.Epsilon)
        {
            GridSizeNumberBox.Value = sliderValue;
        }
        UpdateGridPreviewLayout();
    }

    private void OnGridSizeNumberBoxChanged(object? sender, NumberBoxValueChangedEventArgs e)
    {
        var numberBoxValue = (int)Math.Round(GridSizeNumberBox.Value);
        if (Math.Abs(GridSizeSlider.Value - numberBoxValue) > double.Epsilon)
        {
            GridSizeSlider.Value = numberBoxValue;
        }
        UpdateGridPreviewLayout();
    }

    private void OnGridEdgeInsetSliderChanged(object? sender, RoutedEventArgs e)
    {
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
            GridPreviewLinesCanvas is null)
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
        var preset = NormalizeGridSpacingPreset(TryGetSelectedComboBoxTag(GridSpacingPresetComboBox) ?? _gridSpacingPreset);
        var gapRatio = ResolveGridGapRatio(preset);
        var pendingEdgeInsetPercent = ResolvePendingGridEdgeInsetPercent();
        var edgeInset = CalculateEdgeInset(innerWidth, innerHeight, previewShortSideCells, pendingEdgeInsetPercent);
        var gridMetrics = CalculateGridMetrics(innerWidth, innerHeight, previewShortSideCells, gapRatio, edgeInset);
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

        GridInfoTextBlock.Text = Lf(
            "settings.grid.info_format",
            "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)",
            gridMetrics.ColumnCount,
            gridMetrics.RowCount,
            gridMetrics.CellSize);

        DrawGridPreviewLines(gridMetrics);
    }

    private void DrawGridPreviewLines(GridMetrics gridMetrics)
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
        _gridSpacingPreset = NormalizeGridSpacingPreset(
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
        var gapRatio = ResolveGridGapRatio(_gridSpacingPreset);
        var edgeInset = CalculateEdgeInset(hostWidth, hostHeight, _targetShortSideCells, _desktopEdgeInsetPercent);
        var gridMetrics = CalculateGridMetrics(hostWidth, hostHeight, _targetShortSideCells, gapRatio, edgeInset);
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

        GridInfoTextBlock.Text = Lf(
            "settings.grid.info_format",
            "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)",
            gridMetrics.ColumnCount,
            gridMetrics.RowCount,
            gridMetrics.CellSize);

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

    private static double ResolveGridGapRatio(string preset)
    {
        return string.Equals(preset, "Compact", StringComparison.OrdinalIgnoreCase) ? 0.06 : 0.12;
    }

    private static double CalculateEdgeInset(double hostWidth, double hostHeight, int shortSideCells, int insetPercent)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return 0;
        }

        var cells = Math.Max(1, shortSideCells);
        var shortSidePx = Math.Max(1, Math.Min(hostWidth, hostHeight));
        var baseCell = shortSidePx / cells;
        
        // Proportional inset based on user percentage selection.
        var clampedPercent = Math.Clamp(insetPercent, MinEdgeInsetPercent, MaxEdgeInsetPercent);
        var insetRatio = clampedPercent / 100d;
        
        // Keep inset within a practical visual range.
        return Math.Clamp(baseCell * insetRatio, 0, 80);
    }

    private static GridMetrics CalculateGridMetrics(
        double hostWidth,
        double hostHeight,
        int shortSideCells,
        double gapRatio,
        double edgeInsetPx)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return default;
        }

        var shortSide = Math.Max(1, shortSideCells);
        var clampedGapRatio = Math.Max(0, gapRatio);
        var inset = Math.Max(0, edgeInsetPx);

        // Edge inset should come only from user setting.
        // Remaining free space is handled by container centering, not baked into inset.
        var availableWidth = Math.Max(1, hostWidth - inset * 2);
        var availableHeight = Math.Max(1, hostHeight - inset * 2);

        if (hostWidth >= hostHeight)
        {
            var rowCount = shortSide;
            var denominator = rowCount + Math.Max(0, rowCount - 1) * clampedGapRatio;
            if (denominator <= 0)
            {
                return default;
            }

            var cellSize = availableHeight / denominator;
            var gapPx = cellSize * clampedGapRatio;
            var pitch = cellSize + gapPx;
            if (pitch <= 0)
            {
                return default;
            }

            var columnCount = Math.Max(1, (int)Math.Floor((availableWidth + gapPx) / pitch));
            var gridWidth = columnCount * cellSize + Math.Max(0, columnCount - 1) * gapPx;
            var gridHeight = rowCount * cellSize + Math.Max(0, rowCount - 1) * gapPx;

            return new GridMetrics(columnCount, rowCount, cellSize, gapPx, inset, gridWidth, gridHeight);
        }
        else
        {
            var columnCount = shortSide;
            var denominator = columnCount + Math.Max(0, columnCount - 1) * clampedGapRatio;
            if (denominator <= 0)
            {
                return default;
            }

            var cellSize = availableWidth / denominator;
            var gapPx = cellSize * clampedGapRatio;
            var pitch = cellSize + gapPx;
            if (pitch <= 0)
            {
                return default;
            }

            var rowCount = Math.Max(1, (int)Math.Floor((availableHeight + gapPx) / pitch));
            var gridWidth = columnCount * cellSize + Math.Max(0, columnCount - 1) * gapPx;
            var gridHeight = rowCount * cellSize + Math.Max(0, rowCount - 1) * gapPx;

            return new GridMetrics(columnCount, rowCount, cellSize, gapPx, inset, gridWidth, gridHeight);
        }
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
        var compactButtonInset = Math.Clamp(taskbarCellHeight * 0.20, 6, 14);
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
        
        OpenComponentLibraryButton.Margin = new Thickness(0);
        OpenComponentLibraryButton.Padding = taskbarButtonPadding;
        OpenComponentLibraryButton.FontSize = taskbarTextSize;
        OpenComponentLibraryButton.MinHeight = taskbarCellHeight;
        OpenComponentLibraryButton.MinWidth = Math.Clamp(taskbarCellHeight * 2.15, 92, 320);
        OpenComponentLibraryIcon.FontSize = taskbarIconSize;
        OpenComponentLibraryTextBlock.FontSize = taskbarTextSize;
        SetButtonContentSpacing(OpenComponentLibraryButton, buttonContentSpacing);

        OpenSettingsButton.Margin = new Thickness(0);
        OpenSettingsButton.Height = taskbarCellHeight;
        OpenSettingsButton.MinHeight = taskbarCellHeight;
        OpenSettingsButton.FontSize = taskbarTextSize;
        OpenSettingsButtonTextBlock.FontSize = taskbarTextSize;
        OpenSettingsIcon.FontSize = taskbarIconSize;
        SetButtonContentSpacing(OpenSettingsButton, Math.Clamp(taskbarCellHeight * 0.18, 4, 10));

        if (_isSettingsOpen)
        {
            OpenSettingsButton.Width = double.NaN;
            OpenSettingsButton.MinWidth = Math.Clamp(taskbarCellHeight * 2.45, 120, 360);
            OpenSettingsButton.Padding = taskbarButtonPadding;
        }
        else
        {
            OpenSettingsButton.Width = taskbarCellHeight;
            OpenSettingsButton.MinWidth = taskbarCellHeight;
            OpenSettingsButton.Padding = new Thickness(compactButtonInset);
        }

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
        if (SettingsContentPanel is null)
        {
            return;
        }

        var clampedCell = Math.Max(1, cellSize);
        var horizontalInset = Math.Clamp(clampedCell * 0.45, 12, 64);
        var verticalGap = Math.Clamp(clampedCell * 0.16, 6, 18);
        var edgeInset = Math.Max(0, _currentDesktopEdgeInset);

        var taskbarCellHeight = Math.Clamp(clampedCell * 0.76, 36, 76);
        var taskbarPadding = Math.Clamp(taskbarCellHeight * 0.16, 6, 14);
        var taskbarVisualHeight = Math.Max(clampedCell, taskbarCellHeight + taskbarPadding * 2);
        if (BottomTaskbarContainer is not null && BottomTaskbarContainer.Bounds.Height > 1)
        {
            taskbarVisualHeight = Math.Max(taskbarVisualHeight, BottomTaskbarContainer.Bounds.Height);
        }

        var statusBarVisualHeight = clampedCell;
        if (TopStatusBarHost is not null && TopStatusBarHost.Bounds.Height > 1)
        {
            statusBarVisualHeight = Math.Max(statusBarVisualHeight, TopStatusBarHost.Bounds.Height);
        }

        var topInset = Math.Max(clampedCell + verticalGap, edgeInset + statusBarVisualHeight + verticalGap);
        var bottomInset = Math.Max(clampedCell + verticalGap, edgeInset + taskbarVisualHeight + verticalGap);

        // Add extra safety margin so rounded panel corners never clip against viewport edges.
        var cornerSafetyMargin = Math.Clamp(clampedCell * 0.12, 4, 12);
        var inset = new Thickness(
            horizontalInset + cornerSafetyMargin,
            topInset + cornerSafetyMargin,
            horizontalInset + cornerSafetyMargin,
            bottomInset + cornerSafetyMargin);

        // Keep panel stretched with explicit viewport insets so it never overlaps fixed chrome.
        SettingsContentPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        SettingsContentPanel.VerticalAlignment = VerticalAlignment.Stretch;
        SettingsContentPanel.Margin = inset;
        SettingsContentPanel.Width = double.NaN;
        SettingsContentPanel.Height = double.NaN;
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
            var gapRatio = ResolveGridGapRatio(_gridSpacingPreset);
            var edgeInset = CalculateEdgeInset(innerWidth, innerHeight, _targetShortSideCells, _desktopEdgeInsetPercent);
            var gridMetrics = CalculateGridMetrics(innerWidth, innerHeight, _targetShortSideCells, gapRatio, edgeInset);
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
        if (_suppressTimeZoneSelectionEvents || TimeZoneComboBox.SelectedItem is not ComboBoxItem item)
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

