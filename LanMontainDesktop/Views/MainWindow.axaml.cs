using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using LanMontainDesktop.ComponentSystem;
using LanMontainDesktop.ComponentSystem.Extensions;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;
using LanMontainDesktop.Theme;
using LibVLCSharp.Shared;

namespace LanMontainDesktop.Views;

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

    private const int StatusBarRowIndex = 0;
    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
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
    private readonly record struct GridMetrics(int ColumnCount, int RowCount, double CellSize);
    private readonly MonetColorService _monetColorService = new();
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly ComponentRegistry _componentRegistry = ComponentRegistry
        .CreateDefault()
        .RegisterExtensions(
            JsonComponentExtensionProvider.LoadProvidersFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Extensions", "Components")));
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
    private bool _suppressSettingsPersistence;
    private bool _isUpdatingWallpaperPreviewLayout;
    private bool _isComponentLibraryOpen;
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
    private string _taskbarLayoutMode = TaskbarLayoutBottomFullRowMacStyle;
    private string _languageCode = "zh-CN";

    public MainWindow()
    {
        InitializeComponent();
        _fluentAvaloniaTheme = Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        PropertyChanged += OnWindowPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _suppressSettingsPersistence = true;
        var snapshot = _appSettingsService.Load();

        _targetShortSideCells = Math.Clamp(
            snapshot.GridShortSideCells > 0 ? snapshot.GridShortSideCells : CalculateDefaultShortSideCellCountFromDpi(),
            MinShortSideCells,
            MaxShortSideCells);
        GridSizeNumberBox.Value = _targetShortSideCells;

        SettingsNavListBox.SelectedIndex = Math.Clamp(snapshot.SettingsTabIndex, 0, 4);
        UpdateSettingsTabContent();

        WallpaperPlacementComboBox.SelectedIndex = GetPlacementIndexFromSetting(snapshot.WallpaperPlacement);
        _defaultDesktopBackground = DesktopWallpaperLayer.Background;
        ApplyTaskbarSettings(snapshot);
        InitializeLocalization(snapshot.LanguageCode);
        InitializeDesktopSurfaceState(snapshot);
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
        RebuildDesktopGrid();
        PopulateComponentLibraryItems();
        LoadLauncherEntriesAsync();

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
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        PropertyChanged -= OnWindowPropertyChanged;
        DesktopHost.SizeChanged -= OnDesktopHostSizeChanged;
        WallpaperPreviewHost.SizeChanged -= OnWallpaperPreviewHostSizeChanged;
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

    private void OnApplyGridSizeClick(object? sender, RoutedEventArgs e)
    {
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

        RebuildDesktopGrid();
    }

    private void RebuildDesktopGrid()
    {
        var gridMetrics = CalculateGridMetrics(
            DesktopHost.Bounds.Width,
            DesktopHost.Bounds.Height,
            _targetShortSideCells);
        if (gridMetrics.CellSize <= 0)
        {
            return;
        }
        _currentDesktopCellSize = gridMetrics.CellSize;

        DesktopGrid.RowDefinitions.Clear();
        DesktopGrid.ColumnDefinitions.Clear();
        DesktopGrid.Width = gridMetrics.ColumnCount * gridMetrics.CellSize;
        DesktopGrid.Height = gridMetrics.RowCount * gridMetrics.CellSize;

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

    private static GridMetrics CalculateGridMetrics(double hostWidth, double hostHeight, int targetShortSideCells)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return default;
        }

        var shortSideCells = Math.Max(1, targetShortSideCells);
        if (hostWidth >= hostHeight)
        {
            var rowCount = shortSideCells;
            var cellSize = hostHeight / rowCount;
            var columnCount = Math.Max(1, (int)Math.Floor(hostWidth / cellSize));
            return new GridMetrics(columnCount, rowCount, cellSize);
        }

        var columns = shortSideCells;
        var size = hostWidth / columns;
        var rows = Math.Max(1, (int)Math.Floor(hostHeight / size));
        return new GridMetrics(columns, rows, size);
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
        var margin = Math.Clamp(cellSize * 0.08, 1.5, 10);
        var verticalPadding = Math.Clamp(cellSize * 0.08, 2, 12);
        var horizontalPadding = Math.Clamp(cellSize * 0.20, 4, 22);
        var taskbarCell = Math.Clamp(cellSize, 28, 128);

        TopStatusBarHost.Padding = new Thickness(Math.Clamp(cellSize * 0.08, 1.5, 10));
        ClockWidget.Margin = new Thickness(margin);
        ClockWidget.ApplyCellSize(cellSize);

        BottomTaskbarContainer.Margin = new Thickness(Math.Clamp(cellSize * 0.18, 6, 18));
        BottomTaskbarContainer.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.24, 10, 24));
        BottomTaskbarContainer.Padding = new Thickness(Math.Clamp(cellSize * 0.08, 2, 10));

        BackToWindowsButton.Margin = new Thickness(0);
        BackToWindowsButton.Padding = new Thickness(horizontalPadding, verticalPadding);
        BackToWindowsButton.FontSize = Math.Clamp(cellSize * 0.22, 8, 22);
        BackToWindowsButton.MinHeight = taskbarCell;
        BackToWindowsButton.MinWidth = Math.Clamp(cellSize * 2.3, 90, 320);
        OpenComponentLibraryButton.Margin = new Thickness(0);
        OpenComponentLibraryButton.Padding = new Thickness(horizontalPadding, verticalPadding);
        OpenComponentLibraryButton.FontSize = Math.Clamp(cellSize * 0.22, 8, 22);
        OpenComponentLibraryButton.MinHeight = taskbarCell;
        OpenComponentLibraryButton.MinWidth = Math.Clamp(cellSize * 2.0, 88, 300);

        OpenSettingsButton.Margin = new Thickness(0);
        OpenSettingsButton.Height = taskbarCell;
        OpenSettingsButton.MinHeight = taskbarCell;

        if (_isSettingsOpen)
        {
            OpenSettingsButton.Width = double.NaN;
            OpenSettingsButton.MinWidth = Math.Clamp(cellSize * 2.3, 120, 340);
            OpenSettingsButton.Padding = new Thickness(horizontalPadding, verticalPadding);
        }
        else
        {
            OpenSettingsButton.Width = taskbarCell;
            OpenSettingsButton.MinWidth = taskbarCell;
            OpenSettingsButton.Padding = new Thickness(Math.Clamp(taskbarCell * 0.2, 4, 12));
        }

        UpdateComponentLibraryLayout(cellSize);
    }

    private void UpdateComponentLibraryLayout(double cellSize)
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        var horizontalMargin = Math.Clamp(cellSize * 0.7, 18, 44);
        var bottomMargin = Math.Clamp(cellSize * 1.4, 56, 190);
        ComponentLibraryWindow.Margin = new Thickness(horizontalMargin, 20, horizontalMargin, bottomMargin);
        ComponentLibraryWindow.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.24, 12, 24));
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
        var topInset = clampedCell + verticalGap;
        var bottomInset = clampedCell + verticalGap;

        // 添加额外的安全边距以确保圆角不被裁剪
        var cornerSafetyMargin = Math.Clamp(clampedCell * 0.12, 4, 12);
        var inset = new Thickness(
            horizontalInset + cornerSafetyMargin,
            topInset + cornerSafetyMargin,
            horizontalInset + cornerSafetyMargin,
            bottomInset + cornerSafetyMargin);

        // 使用 Margin 来定位，而不是直接设置 Width/Height
        // 这样可以让面板自然填充可用空间，同时保持边距
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

            // Use the host width (which is roughly 50% of the settings area)
            // Subtract padding for the outer host container if needed, but let it stretch
            var availableWidth = Math.Max(100, WallpaperPreviewHost.Bounds.Width);
            
            // Calculate height based on aspect ratio
            var previewWidth = availableWidth;
            var previewHeight = previewWidth / aspectRatio;

            // Apply sizes to the monitor frame
            WallpaperPreviewFrame.Width = previewWidth;
            WallpaperPreviewFrame.Height = previewHeight;

            WallpaperPreviewClockTextBlock.Text = DateTime.Now.ToString("HH:mm");

            var gridMetrics = CalculateGridMetrics(previewWidth, previewHeight, _targetShortSideCells);
            if (gridMetrics.CellSize <= 0)
            {
                return;
            }

            WallpaperPreviewGrid.Width = gridMetrics.ColumnCount * gridMetrics.CellSize;
            WallpaperPreviewGrid.Height = gridMetrics.RowCount * gridMetrics.CellSize;

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
        }
        finally
        {
            _isUpdatingWallpaperPreviewLayout = false;
        }
    }

    private void ApplyPreviewWidgetSizing(double cellSize)
    {
        var margin = Math.Clamp(cellSize * 0.08, 1, 6);
        var previewTaskbarCell = Math.Clamp(cellSize, 10, 36);
        WallpaperPreviewTopStatusBarHost.Padding = new Thickness(Math.Clamp(cellSize * 0.08, 1, 4));
        WallpaperPreviewBottomTaskbarContainer.Margin = new Thickness(margin);
        WallpaperPreviewBottomTaskbarContainer.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.22, 4, 10));
        WallpaperPreviewBottomTaskbarContainer.Padding = new Thickness(Math.Clamp(cellSize * 0.06, 1, 4));

        WallpaperPreviewClockTextBlock.FontSize = Math.Clamp(cellSize * 0.30, 6, 18);
        WallpaperPreviewBackButtonTextBlock.FontSize = Math.Clamp(cellSize * 0.19, 5, 13);
        WallpaperPreviewComponentLibraryTextBlock.FontSize = Math.Clamp(cellSize * 0.18, 5, 12);
        WallpaperPreviewBackButtonVisual.MinHeight = previewTaskbarCell;
        WallpaperPreviewBackButtonVisual.MinWidth = Math.Clamp(cellSize * 2.1, 30, 120);
        WallpaperPreviewComponentLibraryVisual.MinHeight = previewTaskbarCell;
        WallpaperPreviewComponentLibraryVisual.MinWidth = Math.Clamp(cellSize * 2.0, 28, 110);
        WallpaperPreviewSettingsButtonIcon.Width = Math.Clamp(previewTaskbarCell * 0.42, 6, 14);
        WallpaperPreviewSettingsButtonIcon.Height = Math.Clamp(previewTaskbarCell * 0.42, 6, 14);
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
}
