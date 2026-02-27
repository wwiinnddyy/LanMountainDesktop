using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;
using LanMontainDesktop.Theme;

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

    private const int StatusBarRowIndex = 0;
    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
    private const int SettingsTransitionDurationMs = 240;
    private const double LightBackgroundLuminanceThreshold = 0.57;
    private const double WallpaperPreviewMinWidth = 220;
    private const double WallpaperPreviewMinHeight = 140;
    private const double WallpaperPreviewMaxHeight = 280;
    private readonly record struct GridMetrics(int ColumnCount, int RowCount, double CellSize);
    private readonly MonetColorService _monetColorService = new();
    private int _targetShortSideCells;
    private bool _isSettingsOpen;
    private bool _isNightMode;
    private bool _suppressThemeToggleEvents;
    private TranslateTransform? _settingsContentPanelTransform;
    private IBrush? _defaultDesktopBackground;
    private Bitmap? _wallpaperBitmap;
    private string? _wallpaperPath;
    private string _wallpaperStatus = "Current background uses solid color.";
    private IReadOnlyList<Color> _recommendedColors = Array.Empty<Color>();
    private IReadOnlyList<Color> _monetColors = Array.Empty<Color>();
    private Color _selectedThemeColor = Color.Parse("#FF3B82F6");

    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += OnWindowPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _targetShortSideCells = CalculateDefaultShortSideCellCountFromDpi();
        GridSizeNumberBox.Value = _targetShortSideCells;
        SettingsNavListBox.SelectedIndex = 0;
        UpdateSettingsTabContent();
        WallpaperPlacementComboBox.SelectedIndex = 0;
        _defaultDesktopBackground = DesktopHost.Background;
        UpdateWallpaperDisplay();
        _isNightMode = CalculateCurrentBackgroundLuminance() < LightBackgroundLuminanceThreshold;
        ApplyNightModeState(_isNightMode, refreshPalettes: false);
        RefreshColorPalettes();
        EnsureSelectedThemeColor();
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = $"Theme color ready: {_selectedThemeColor}.";
        UpdateAdaptiveTextSystem();
        _settingsContentPanelTransform = SettingsContentPanel.RenderTransform as TranslateTransform;
        DesktopHost.SizeChanged += OnDesktopHostSizeChanged;
        WallpaperPreviewHost.SizeChanged += OnWallpaperPreviewHostSizeChanged;
        RebuildDesktopGrid();
    }

    protected override void OnClosed(EventArgs e)
    {
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

        PlaceStatusBarComponent(ClockWidget, column: 0, requestedColumnSpan: 3, totalColumns: gridMetrics.ColumnCount);

        var firstDesktopRow = Math.Min(gridMetrics.RowCount - 1, StatusBarRowIndex + 1);

        var settingsColumnSpan = ClampComponentSpan(2, gridMetrics.ColumnCount);
        var settingsRowSpan = ClampComponentSpan(1, gridMetrics.RowCount);
        var settingsRow = Math.Max(firstDesktopRow, gridMetrics.RowCount - 1);
        var settingsColumn = Math.Max(0, gridMetrics.ColumnCount - settingsColumnSpan);

        var backButtonRow = settingsRow;
        var backButtonMaxColumnsWithoutOverlap = settingsColumn;
        int backButtonColumnSpan;
        if (backButtonMaxColumnsWithoutOverlap >= 1)
        {
            backButtonColumnSpan = ClampComponentSpan(Math.Min(4, backButtonMaxColumnsWithoutOverlap), gridMetrics.ColumnCount);
        }
        else
        {
            backButtonRow = Math.Max(firstDesktopRow, gridMetrics.RowCount - 2);
            backButtonColumnSpan = ClampComponentSpan(Math.Min(4, gridMetrics.ColumnCount), gridMetrics.ColumnCount);
        }

        Grid.SetRow(BackToWindowsContainer, backButtonRow);
        Grid.SetColumn(BackToWindowsContainer, 0);
        Grid.SetRowSpan(BackToWindowsContainer, ClampComponentSpan(1, gridMetrics.RowCount));
        Grid.SetColumnSpan(BackToWindowsContainer, backButtonColumnSpan);

        Grid.SetRow(OpenSettingsButton, settingsRow);
        Grid.SetColumn(OpenSettingsButton, settingsColumn);
        Grid.SetRowSpan(OpenSettingsButton, settingsRowSpan);
        Grid.SetColumnSpan(OpenSettingsButton, settingsColumnSpan);

        ApplyWidgetSizing(gridMetrics.CellSize);

        GridInfoTextBlock.Text =
            $"Grid: {gridMetrics.ColumnCount} cols x {gridMetrics.RowCount} rows | cell {gridMetrics.CellSize:F1}px (1:1)";

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

        ClockWidget.Margin = new Thickness(margin);
        ClockWidget.ApplyCellSize(cellSize);

        BackToWindowsContainer.Margin = new Thickness(margin);
        BackToWindowsContainer.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.12, 5, 14));
        BackToWindowsButton.Padding = new Thickness(horizontalPadding, verticalPadding);
        BackToWindowsButton.FontSize = Math.Clamp(cellSize * 0.30, 8, 30);

        OpenSettingsButton.Margin = new Thickness(Math.Clamp(cellSize * 0.12, 6, 16));
        OpenSettingsButton.Padding = new Thickness(
            Math.Clamp(horizontalPadding + 2, 8, 26),
            Math.Clamp(verticalPadding, 4, 12));
        OpenSettingsButton.FontSize = Math.Clamp(cellSize * 0.22, 9, 22);
    }

    private void UpdateWallpaperPreviewLayout()
    {
        if (WallpaperPreviewFrame is null ||
            WallpaperPreviewHost is null ||
            WallpaperPreviewGrid is null)
        {
            return;
        }

        var desktopWidth = Math.Max(1, DesktopHost.Bounds.Width);
        var desktopHeight = Math.Max(1, DesktopHost.Bounds.Height);
        var aspectRatio = desktopWidth / desktopHeight;
        var availableWidth = WallpaperPreviewHost.Bounds.Width - 24;
        if (availableWidth <= 1)
        {
            availableWidth = WallpaperPreviewFrame.Width;
        }

        var previewWidth = Math.Max(WallpaperPreviewMinWidth, availableWidth);
        var previewHeight = previewWidth / aspectRatio;

        if (previewHeight > WallpaperPreviewMaxHeight)
        {
            previewHeight = WallpaperPreviewMaxHeight;
            previewWidth = previewHeight * aspectRatio;
        }

        if (previewHeight < WallpaperPreviewMinHeight)
        {
            previewHeight = WallpaperPreviewMinHeight;
            previewWidth = previewHeight * aspectRatio;
        }

        WallpaperPreviewFrame.Width = previewWidth;
        WallpaperPreviewFrame.Height = previewHeight;
        WallpaperPreviewClockTextBlock.Text = DateTime.Now.ToString("HH:mm");

        var gridMetrics = CalculateGridMetrics(previewWidth, previewHeight, _targetShortSideCells);
        if (gridMetrics.CellSize <= 0)
        {
            return;
        }

        WallpaperPreviewGrid.RowDefinitions.Clear();
        WallpaperPreviewGrid.ColumnDefinitions.Clear();
        WallpaperPreviewGrid.Width = gridMetrics.ColumnCount * gridMetrics.CellSize;
        WallpaperPreviewGrid.Height = gridMetrics.RowCount * gridMetrics.CellSize;

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
            WallpaperPreviewClockContainer,
            column: 0,
            requestedColumnSpan: 3,
            totalColumns: gridMetrics.ColumnCount);

        var firstDesktopRow = Math.Min(gridMetrics.RowCount - 1, StatusBarRowIndex + 1);
        var settingsColumnSpan = ClampComponentSpan(2, gridMetrics.ColumnCount);
        var settingsRow = Math.Max(firstDesktopRow, gridMetrics.RowCount - 1);
        var settingsColumn = Math.Max(0, gridMetrics.ColumnCount - settingsColumnSpan);

        var backButtonRow = settingsRow;
        var backButtonMaxColumnsWithoutOverlap = settingsColumn;
        int backButtonColumnSpan;
        if (backButtonMaxColumnsWithoutOverlap >= 1)
        {
            backButtonColumnSpan = ClampComponentSpan(Math.Min(4, backButtonMaxColumnsWithoutOverlap), gridMetrics.ColumnCount);
        }
        else
        {
            backButtonRow = Math.Max(firstDesktopRow, gridMetrics.RowCount - 2);
            backButtonColumnSpan = ClampComponentSpan(Math.Min(4, gridMetrics.ColumnCount), gridMetrics.ColumnCount);
        }

        Grid.SetRow(WallpaperPreviewBackButtonContainer, backButtonRow);
        Grid.SetColumn(WallpaperPreviewBackButtonContainer, 0);
        Grid.SetRowSpan(WallpaperPreviewBackButtonContainer, 1);
        Grid.SetColumnSpan(WallpaperPreviewBackButtonContainer, backButtonColumnSpan);

        Grid.SetRow(WallpaperPreviewSettingsButtonContainer, settingsRow);
        Grid.SetColumn(WallpaperPreviewSettingsButtonContainer, settingsColumn);
        Grid.SetRowSpan(WallpaperPreviewSettingsButtonContainer, 1);
        Grid.SetColumnSpan(WallpaperPreviewSettingsButtonContainer, settingsColumnSpan);

        ApplyPreviewWidgetSizing(gridMetrics.CellSize);
    }

    private void ApplyPreviewWidgetSizing(double cellSize)
    {
        var margin = Math.Clamp(cellSize * 0.08, 1, 6);
        WallpaperPreviewClockContainer.Margin = new Thickness(margin);
        WallpaperPreviewBackButtonContainer.Margin = new Thickness(margin);
        WallpaperPreviewSettingsButtonContainer.Margin = new Thickness(margin);

        WallpaperPreviewClockTextBlock.FontSize = Math.Clamp(cellSize * 0.30, 6, 18);
        WallpaperPreviewBackButtonTextBlock.FontSize = Math.Clamp(cellSize * 0.19, 5, 13);
        WallpaperPreviewSettingsButtonTextBlock.FontSize = Math.Clamp(cellSize * 0.19, 5, 13);

        var cornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.12, 3, 10));
        WallpaperPreviewBackButtonContainer.CornerRadius = cornerRadius;
        WallpaperPreviewSettingsButtonContainer.CornerRadius = cornerRadius;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        OpenSettingsPage();
    }

    private void OnCloseSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseSettingsPage();
    }

    private void OnSettingsNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSettingsTabContent();
    }

    private void UpdateSettingsTabContent()
    {
        // SelectionChanged can fire during XAML initialization before all named controls are assigned.
        if (SettingsNavListBox is null ||
            GridSettingsPanel is null ||
            WallpaperSettingsPanel is null ||
            ColorSettingsPanel is null)
        {
            return;
        }

        var selectedIndex = SettingsNavListBox.SelectedIndex;
        WallpaperSettingsPanel.IsVisible = selectedIndex == 0;
        GridSettingsPanel.IsVisible = selectedIndex == 1;
        ColorSettingsPanel.IsVisible = selectedIndex == 2;
    }

    private void OnNightModeChecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressThemeToggleEvents)
        {
            return;
        }

        ApplyNightModeState(true, refreshPalettes: true);
    }

    private void OnNightModeUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressThemeToggleEvents)
        {
            return;
        }

        ApplyNightModeState(false, refreshPalettes: true);
    }

    private void OnRecommendedColorClick(object? sender, RoutedEventArgs e)
    {
        ApplyThemeColorFromButton(sender as Button, "Recommended");
    }

    private void OnMonetColorClick(object? sender, RoutedEventArgs e)
    {
        ApplyThemeColorFromButton(sender as Button, "Monet");
    }

    private void OnRefreshMonetColorsClick(object? sender, RoutedEventArgs e)
    {
        RefreshColorPalettes();
        EnsureSelectedThemeColor();
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = "Monet colors refreshed.";
        UpdateAdaptiveTextSystem();
    }

    private async void OnPickWallpaperClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _wallpaperStatus = "Storage provider is unavailable.";
            UpdateWallpaperDisplay();
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Select wallpaper",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                }
            ]
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
        {
            return;
        }

        var file = files[0];
        try
        {
            Bitmap bitmap;
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                bitmap = new Bitmap(localPath);
                _wallpaperPath = localPath;
            }
            else
            {
                await using var stream = await file.OpenReadAsync();
                bitmap = new Bitmap(stream);
                _wallpaperPath = file.Name;
            }

            _wallpaperBitmap?.Dispose();
            _wallpaperBitmap = bitmap;
            _wallpaperStatus = "Wallpaper applied.";
            ApplyWallpaperBrush();
            UpdateWallpaperDisplay();
            RefreshColorPalettes();
            EnsureSelectedThemeColor();
            UpdateThemeColorSelectionState();
            ThemeColorStatusTextBlock.Text = "Wallpaper updated. Monet colors refreshed.";
        }
        catch (Exception ex)
        {
            _wallpaperStatus = $"Failed to apply wallpaper: {ex.Message}";
            UpdateWallpaperDisplay();
        }
    }

    private void OnClearWallpaperClick(object? sender, RoutedEventArgs e)
    {
        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _wallpaperPath = null;
        _wallpaperStatus = "Background reset to solid color.";
        ApplyWallpaperBrush();
        UpdateWallpaperDisplay();
        RefreshColorPalettes();
        EnsureSelectedThemeColor();
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = "Wallpaper cleared. Monet colors refreshed.";
    }

    private void OnWallpaperPlacementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyWallpaperBrush();
        if (_wallpaperBitmap is not null)
        {
            _wallpaperStatus = $"Wallpaper mode: {GetPlacementDisplayName(GetSelectedWallpaperPlacement())}.";
        }

        UpdateWallpaperDisplay();
    }

    private void ApplyWallpaperBrush()
    {
        if (_wallpaperBitmap is null)
        {
            DesktopHost.Background = _defaultDesktopBackground ?? new SolidColorBrush(Color.Parse("#FF020617"));
            WallpaperPreviewViewport.Background = _defaultDesktopBackground ?? new SolidColorBrush(Color.Parse("#30111827"));
            UpdateAdaptiveTextSystem();
            return;
        }

        var placement = GetSelectedWallpaperPlacement();
        DesktopHost.Background = CreateWallpaperBrush(_wallpaperBitmap, placement, false);
        WallpaperPreviewViewport.Background = CreateWallpaperBrush(_wallpaperBitmap, placement, true);
        UpdateAdaptiveTextSystem();
    }

    private void UpdateWallpaperDisplay()
    {
        if (WallpaperPathTextBlock is null ||
            WallpaperStatusTextBlock is null ||
            WallpaperPreviewViewport is null ||
            WallpaperPlacementComboBox is null)
        {
            return;
        }

        WallpaperPathTextBlock.Text = string.IsNullOrWhiteSpace(_wallpaperPath)
            ? "No wallpaper selected."
            : _wallpaperPath;
        WallpaperStatusTextBlock.Text = _wallpaperStatus;

        if (_wallpaperBitmap is null)
        {
            WallpaperPreviewViewport.Background = _defaultDesktopBackground ?? new SolidColorBrush(Color.Parse("#30111827"));
            return;
        }

        WallpaperPreviewViewport.Background = CreateWallpaperBrush(
            _wallpaperBitmap,
            GetSelectedWallpaperPlacement(),
            true);
    }

    private ImageBrush CreateWallpaperBrush(Bitmap bitmap, WallpaperPlacement placement, bool forPreview)
    {
        var brush = new ImageBrush
        {
            Source = bitmap,
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode.None
        };

        switch (placement)
        {
            case WallpaperPlacement.Fill:
                brush.Stretch = Stretch.UniformToFill;
                break;
            case WallpaperPlacement.Fit:
                brush.Stretch = Stretch.Uniform;
                break;
            case WallpaperPlacement.Stretch:
                brush.Stretch = Stretch.Fill;
                break;
            case WallpaperPlacement.Center:
                brush.Stretch = Stretch.None;
                break;
            case WallpaperPlacement.Tile:
                brush.Stretch = Stretch.None;
                brush.TileMode = TileMode.Tile;
                var tileSize = forPreview ? 96d : 220d;
                brush.DestinationRect = new RelativeRect(0, 0, tileSize, tileSize, RelativeUnit.Absolute);
                break;
        }

        return brush;
    }

    private WallpaperPlacement GetSelectedWallpaperPlacement()
    {
        return WallpaperPlacementComboBox?.SelectedIndex switch
        {
            1 => WallpaperPlacement.Fit,
            2 => WallpaperPlacement.Stretch,
            3 => WallpaperPlacement.Center,
            4 => WallpaperPlacement.Tile,
            _ => WallpaperPlacement.Fill
        };
    }

    private static string GetPlacementDisplayName(WallpaperPlacement placement)
    {
        return placement switch
        {
            WallpaperPlacement.Fill => "Fill",
            WallpaperPlacement.Fit => "Fit",
            WallpaperPlacement.Stretch => "Stretch",
            WallpaperPlacement.Center => "Center",
            WallpaperPlacement.Tile => "Tile",
            _ => "Fill"
        };
    }

    private void UpdateAdaptiveTextSystem()
    {
        var luminance = CalculateCurrentBackgroundLuminance();
        var isLightBackground = luminance >= LightBackgroundLuminanceThreshold;
        var navBackground = SettingsNavPanelBorder?.Background;
        var isLightNavBackground = CalculateBrushLuminance(navBackground) >= LightBackgroundLuminanceThreshold;
        var context = new ThemeColorContext(
            _selectedThemeColor,
            isLightBackground,
            isLightNavBackground,
            _isNightMode);

        ThemeColorSystemService.ApplyThemeResources(Resources, context);
        GlassEffectService.ApplyGlassResources(Resources, context.IsLightBackground);
    }

    private double CalculateCurrentBackgroundLuminance()
    {
        if (_wallpaperBitmap is not null)
        {
            return CalculateBitmapAverageLuminance(_wallpaperBitmap);
        }

        return CalculateBrushLuminance(DesktopHost.Background ?? _defaultDesktopBackground);
    }

    private void ApplyNightModeState(bool enabled, bool refreshPalettes)
    {
        _isNightMode = enabled;
        RequestedThemeVariant = enabled ? ThemeVariant.Dark : ThemeVariant.Light;

        _suppressThemeToggleEvents = true;
        NightModeToggleSwitch.IsChecked = enabled;
        _suppressThemeToggleEvents = false;
        ThemeModeStatusTextBlock.Text = enabled ? "Night mode enabled" : "Day mode enabled";

        if (refreshPalettes)
        {
            RefreshColorPalettes();
            EnsureSelectedThemeColor();
        }

        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = $"Theme mode: {(enabled ? "Night" : "Day")}.";
        UpdateAdaptiveTextSystem();
    }

    private void RefreshColorPalettes()
    {
        var palette = _monetColorService.BuildPalette(_wallpaperBitmap, _isNightMode);
        _recommendedColors = palette.RecommendedColors;
        _monetColors = palette.MonetColors;
        ApplyColorPaletteToButtons(_recommendedColors, GetRecommendedColorTargets());
        ApplyColorPaletteToButtons(_monetColors, GetMonetColorTargets());
    }

    private void ApplyColorPaletteToButtons(
        IReadOnlyList<Color> colors,
        IReadOnlyList<(Button Button, Border Swatch)> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var color = i < colors.Count
                ? colors[i]
                : Color.Parse("#00000000");
            var (button, swatch) = targets[i];
            button.Tag = color.ToString();
            button.IsEnabled = i < colors.Count;
            swatch.Background = i < colors.Count
                ? new SolidColorBrush(color)
                : new SolidColorBrush(Color.Parse("#00000000"));
        }
    }

    private IReadOnlyList<(Button Button, Border Swatch)> GetRecommendedColorTargets()
    {
        return
        [
            (RecommendedColorButton1, RecommendedColorSwatch1),
            (RecommendedColorButton2, RecommendedColorSwatch2),
            (RecommendedColorButton3, RecommendedColorSwatch3),
            (RecommendedColorButton4, RecommendedColorSwatch4),
            (RecommendedColorButton5, RecommendedColorSwatch5),
            (RecommendedColorButton6, RecommendedColorSwatch6)
        ];
    }

    private IReadOnlyList<(Button Button, Border Swatch)> GetMonetColorTargets()
    {
        return
        [
            (MonetColorButton1, MonetColorSwatch1),
            (MonetColorButton2, MonetColorSwatch2),
            (MonetColorButton3, MonetColorSwatch3),
            (MonetColorButton4, MonetColorSwatch4),
            (MonetColorButton5, MonetColorSwatch5),
            (MonetColorButton6, MonetColorSwatch6)
        ];
    }

    private void EnsureSelectedThemeColor()
    {
        if (ContainsColor(_recommendedColors, _selectedThemeColor) ||
            ContainsColor(_monetColors, _selectedThemeColor))
        {
            return;
        }

        if (_recommendedColors.Count > 0)
        {
            _selectedThemeColor = _recommendedColors[0];
            return;
        }

        if (_monetColors.Count > 0)
        {
            _selectedThemeColor = _monetColors[0];
        }
    }

    private void ApplyThemeColorFromButton(Button? button, string sourceLabel)
    {
        if (!TryGetButtonColor(button, out var color))
        {
            return;
        }

        _selectedThemeColor = color;
        UpdateThemeColorSelectionState();
        ThemeColorStatusTextBlock.Text = $"{sourceLabel} color applied: {_selectedThemeColor}.";
        UpdateAdaptiveTextSystem();
    }

    private void UpdateThemeColorSelectionState()
    {
        UpdateColorSelectionVisuals(GetRecommendedColorTargets());
        UpdateColorSelectionVisuals(GetMonetColorTargets());
    }

    private void UpdateColorSelectionVisuals(IReadOnlyList<(Button Button, Border Swatch)> targets)
    {
        foreach (var (button, swatch) in targets)
        {
            var isSelected = TryGetButtonColor(button, out var color) && AreSameColor(color, _selectedThemeColor);
            swatch.BorderBrush = isSelected
                ? new SolidColorBrush(Color.Parse("#FFFFFFFF"))
                : new SolidColorBrush(Color.Parse("#A0FFFFFF"));
            swatch.BorderThickness = new Thickness(isSelected ? 2 : 1);
        }
    }

    private static bool TryGetButtonColor(Button? button, out Color color)
    {
        color = default;
        if (button?.Tag is not string colorText || string.IsNullOrWhiteSpace(colorText))
        {
            return false;
        }

        try
        {
            color = Color.Parse(colorText);
            return true;
        }
        catch
        {
            return false;
        }
    }


    private static bool ContainsColor(IReadOnlyList<Color> colors, Color target)
    {
        for (var i = 0; i < colors.Count; i++)
        {
            if (AreSameColor(colors[i], target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreSameColor(Color left, Color right)
    {
        return left.R == right.R && left.G == right.G && left.B == right.B;
    }


    private static double CalculateBrushLuminance(IBrush? brush)
    {
        if (brush is ISolidColorBrush solidBrush)
        {
            return CalculateRelativeLuminance(solidBrush.Color);
        }

        return CalculateRelativeLuminance(Color.Parse("#FF020617"));
    }

    private static double CalculateBitmapAverageLuminance(Bitmap bitmap)
    {
        try
        {
            var sampleWidth = Math.Clamp(bitmap.PixelSize.Width, 1, 48);
            var sampleHeight = Math.Clamp(bitmap.PixelSize.Height, 1, 48);

            using var scaledBitmap = bitmap.CreateScaledBitmap(
                new PixelSize(sampleWidth, sampleHeight),
                BitmapInterpolationMode.MediumQuality);
            using var writeable = new WriteableBitmap(
                scaledBitmap.PixelSize,
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using var framebuffer = writeable.Lock();

            scaledBitmap.CopyPixels(framebuffer, AlphaFormat.Premul);

            var rowBytes = framebuffer.RowBytes;
            var byteCount = rowBytes * framebuffer.Size.Height;
            if (byteCount <= 0 || framebuffer.Address == IntPtr.Zero)
            {
                return CalculateRelativeLuminance(Color.Parse("#FF020617"));
            }

            var pixelBuffer = new byte[byteCount];
            Marshal.Copy(framebuffer.Address, pixelBuffer, 0, byteCount);

            double luminanceSum = 0;
            var pixelCount = 0;
            for (var y = 0; y < framebuffer.Size.Height; y++)
            {
                var rowOffset = y * rowBytes;
                for (var x = 0; x < framebuffer.Size.Width; x++)
                {
                    var index = rowOffset + (x * 4);
                    var alpha = pixelBuffer[index + 3] / 255d;
                    if (alpha <= 0.01)
                    {
                        continue;
                    }

                    var blue = (pixelBuffer[index] / 255d) / alpha;
                    var green = (pixelBuffer[index + 1] / 255d) / alpha;
                    var red = (pixelBuffer[index + 2] / 255d) / alpha;

                    red = Math.Clamp(red, 0, 1);
                    green = Math.Clamp(green, 0, 1);
                    blue = Math.Clamp(blue, 0, 1);

                    luminanceSum += CalculateRelativeLuminance(red, green, blue);
                    pixelCount++;
                }
            }

            return pixelCount > 0
                ? luminanceSum / pixelCount
                : CalculateRelativeLuminance(Color.Parse("#FF020617"));
        }
        catch
        {
            return CalculateRelativeLuminance(Color.Parse("#FF020617"));
        }
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        return CalculateRelativeLuminance(color.R / 255d, color.G / 255d, color.B / 255d);
    }

    private static double CalculateRelativeLuminance(double red, double green, double blue)
    {
        var linearRed = ToLinearRgb(red);
        var linearGreen = ToLinearRgb(green);
        var linearBlue = ToLinearRgb(blue);
        return (0.2126 * linearRed) + (0.7152 * linearGreen) + (0.0722 * linearBlue);
    }

    private static double ToLinearRgb(double value)
    {
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private void OpenSettingsPage()
    {
        if (_isSettingsOpen)
        {
            return;
        }

        _isSettingsOpen = true;
        UpdateAdaptiveTextSystem();
        SettingsPage.IsVisible = true;
        SettingsPage.Opacity = 0;
        if (_settingsContentPanelTransform is not null)
        {
            _settingsContentPanelTransform.Y = 30;
        }

        DesktopPage.IsHitTestVisible = false;
        UpdateWallpaperPreviewLayout();

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isSettingsOpen)
            {
                return;
            }

            SettingsPage.Opacity = 1;
            if (_settingsContentPanelTransform is not null)
            {
                _settingsContentPanelTransform.Y = 0;
            }
        }, DispatcherPriority.Background);
    }

    private void CloseSettingsPage()
    {
        if (!_isSettingsOpen)
        {
            return;
        }

        _isSettingsOpen = false;
        UpdateAdaptiveTextSystem();

        DesktopPage.IsHitTestVisible = true;

        SettingsPage.Opacity = 0;
        if (_settingsContentPanelTransform is not null)
        {
            _settingsContentPanelTransform.Y = 30;
        }

        DispatcherTimer.RunOnce(() =>
        {
            if (_isSettingsOpen)
            {
                return;
            }

            SettingsPage.IsVisible = false;
        }, TimeSpan.FromMilliseconds(SettingsTransitionDurationMs));
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
