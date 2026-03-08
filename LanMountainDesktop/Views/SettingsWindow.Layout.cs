using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Line = Avalonia.Controls.Shapes.Line;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
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

    private void OnApplyGridSizeClick(object? sender, RoutedEventArgs e)
    {
        _gridSpacingPreset = NormalizeGridSpacingPreset(TryGetSelectedComboBoxTag(GridSpacingPresetComboBox) ?? _gridSpacingPreset);
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
        UpdateWallpaperPreviewLayout();
        UpdateGridPreviewLayout();
        PersistSettings();
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
        return Math.Clamp(baseCell * (Math.Clamp(insetPercent, MinEdgeInsetPercent, MaxEdgeInsetPercent) / 100d), 0, 80);
    }

    private static GridMetrics CalculateGridMetrics(double hostWidth, double hostHeight, int shortSideCells, double gapRatio, double edgeInsetPx)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return default;
        }

        var shortSide = Math.Max(1, shortSideCells);
        var clampedGapRatio = Math.Max(0, gapRatio);
        var inset = Math.Max(0, edgeInsetPx);
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

        var columnCountPortrait = shortSide;
        var denominatorPortrait = columnCountPortrait + Math.Max(0, columnCountPortrait - 1) * clampedGapRatio;
        if (denominatorPortrait <= 0)
        {
            return default;
        }

        var cellSizePortrait = availableWidth / denominatorPortrait;
        var gapPxPortrait = cellSizePortrait * clampedGapRatio;
        var pitchPortrait = cellSizePortrait + gapPxPortrait;
        if (pitchPortrait <= 0)
        {
            return default;
        }

        var rowCountPortrait = Math.Max(1, (int)Math.Floor((availableHeight + gapPxPortrait) / pitchPortrait));
        var gridWidthPortrait = columnCountPortrait * cellSizePortrait + Math.Max(0, columnCountPortrait - 1) * gapPxPortrait;
        var gridHeightPortrait = rowCountPortrait * cellSizePortrait + Math.Max(0, rowCountPortrait - 1) * gapPxPortrait;
        return new GridMetrics(columnCountPortrait, rowCountPortrait, cellSizePortrait, gapPxPortrait, inset, gridWidthPortrait, gridHeightPortrait);
    }

    private static int ClampComponentSpan(int requestedSpan, int axisCellCount)
    {
        return Math.Clamp(requestedSpan, 1, Math.Max(1, axisCellCount));
    }

    private static int ClampGridIndex(int requestedIndex, int axisCellCount)
    {
        return Math.Clamp(requestedIndex, 0, Math.Max(0, axisCellCount - 1));
    }

    private static void PlaceStatusBarComponent(Control component, int column, int requestedColumnSpan, int totalColumns)
    {
        var clampedColumn = ClampGridIndex(column, totalColumns);
        var availableColumns = Math.Max(1, totalColumns - clampedColumn);
        Grid.SetRow(component, StatusBarRowIndex);
        Grid.SetColumn(component, clampedColumn);
        Grid.SetColumnSpan(component, ClampComponentSpan(requestedColumnSpan, availableColumns));
    }

    private void UpdateGridPreviewLayout()
    {
        var previewShortSideCells = (int)Math.Round(GridSizeSlider.Value);
        if (previewShortSideCells < MinShortSideCells || previewShortSideCells > MaxShortSideCells)
        {
            previewShortSideCells = _targetShortSideCells;
        }

        var desktopWidth = Math.Max(1, DesktopHost.Bounds.Width > 1 ? DesktopHost.Bounds.Width : Bounds.Width);
        var desktopHeight = Math.Max(1, DesktopHost.Bounds.Height > 1 ? DesktopHost.Bounds.Height : Bounds.Height);
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
        var edgeInset = CalculateEdgeInset(innerWidth, innerHeight, previewShortSideCells, ResolvePendingGridEdgeInsetPercent());
        var gridMetrics = CalculateGridMetrics(innerWidth, innerHeight, previewShortSideCells, gapRatio, edgeInset);
        if (gridMetrics.CellSize <= 0)
        {
            return;
        }

        _currentDesktopCellSize = gridMetrics.CellSize;
        GridPreviewGrid.Margin = new Thickness(gridMetrics.EdgeInsetPx);
        GridPreviewGrid.RowSpacing = gridMetrics.GapPx;
        GridPreviewGrid.ColumnSpacing = gridMetrics.GapPx;
        GridPreviewGrid.Width = gridMetrics.GridWidthPx;
        GridPreviewGrid.Height = gridMetrics.GridHeightPx;
        GridPreviewLinesCanvas.Margin = new Thickness(gridMetrics.EdgeInsetPx);
        GridPreviewGrid.RowDefinitions.Clear();
        GridPreviewGrid.ColumnDefinitions.Clear();

        for (var row = 0; row < gridMetrics.RowCount; row++)
        {
            GridPreviewGrid.RowDefinitions.Add(new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
        }

        for (var col = 0; col < gridMetrics.ColumnCount; col++)
        {
            GridPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
        }

        PlaceStatusBarComponent(GridPreviewTopStatusBarHost, 0, gridMetrics.ColumnCount, gridMetrics.ColumnCount);

        var taskbarRow = gridMetrics.RowCount - 1;
        Grid.SetRow(GridPreviewBottomTaskbarContainer, taskbarRow);
        Grid.SetColumn(GridPreviewBottomTaskbarContainer, 0);
        Grid.SetColumnSpan(GridPreviewBottomTaskbarContainer, gridMetrics.ColumnCount);

        ApplyGridPreviewWidgetSizing(gridMetrics.CellSize);
        ApplyStatusBarComponentSpacingForPanel(GridPreviewTopStatusComponentsPanel, gridMetrics.CellSize);
        UpdateGridEdgeInsetComputedPxText(gridMetrics.CellSize);
        GridInfoTextBlock.Text = Lf("settings.grid.info_format", "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)", gridMetrics.ColumnCount, gridMetrics.RowCount, gridMetrics.CellSize);

        DrawGridPreviewLines(gridMetrics);
    }

    private void DrawGridPreviewLines(GridMetrics gridMetrics)
    {
        var viewportBackground = GridPreviewViewport.Background as SolidColorBrush;
        var backgroundColor = viewportBackground?.Color ?? Color.Parse("#30111827");
        var luminance = CalculateRelativeLuminance(backgroundColor);
        var lineColor = luminance >= LightBackgroundLuminanceThreshold ? Color.Parse("#80000000") : Color.Parse("#80FFFFFF");

        GridPreviewLinesCanvas.Children.Clear();
        GridPreviewLinesCanvas.Width = gridMetrics.GridWidthPx;
        GridPreviewLinesCanvas.Height = gridMetrics.GridHeightPx;

        var dashLength = gridMetrics.CellSize * 0.3;
        var gapLength = gridMetrics.CellSize * 0.2;

        for (var row = 0; row <= gridMetrics.RowCount; row++)
        {
            var y = row == gridMetrics.RowCount ? gridMetrics.GridHeightPx : row * gridMetrics.Pitch;
            GridPreviewLinesCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(gridMetrics.GridWidthPx, y),
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { dashLength, gapLength },
                IsHitTestVisible = false
            });
        }

        for (var col = 0; col <= gridMetrics.ColumnCount; col++)
        {
            var x = col == gridMetrics.ColumnCount ? gridMetrics.GridWidthPx : col * gridMetrics.Pitch;
            GridPreviewLinesCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, gridMetrics.GridHeightPx),
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { dashLength, gapLength },
                IsHitTestVisible = false
            });
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

    private void UpdateWallpaperPreviewLayout()
    {
        if (_isUpdatingWallpaperPreviewLayout)
        {
            return;
        }

        _isUpdatingWallpaperPreviewLayout = true;
        try
        {
            var desktopWidth = Math.Max(1, DesktopHost.Bounds.Width > 1 ? DesktopHost.Bounds.Width : Bounds.Width);
            var desktopHeight = Math.Max(1, DesktopHost.Bounds.Height > 1 ? DesktopHost.Bounds.Height : Bounds.Height);
            var aspectRatio = desktopWidth / desktopHeight;

            var availableWidth = Math.Max(100, WallpaperPreviewHost.Bounds.Width);
            var availableHeight = WallpaperPreviewHost.Bounds.Height < 120 ? double.PositiveInfinity : WallpaperPreviewHost.Bounds.Height;

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
            WallpaperPreviewGrid.RowDefinitions.Clear();
            WallpaperPreviewGrid.ColumnDefinitions.Clear();

            for (var row = 0; row < gridMetrics.RowCount; row++)
            {
                WallpaperPreviewGrid.RowDefinitions.Add(new RowDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            for (var col = 0; col < gridMetrics.ColumnCount; col++)
            {
                WallpaperPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(gridMetrics.CellSize, GridUnitType.Pixel)));
            }

            PlaceStatusBarComponent(WallpaperPreviewTopStatusBarHost, 0, gridMetrics.ColumnCount, gridMetrics.ColumnCount);

            var taskbarRow = gridMetrics.RowCount - 1;
            Grid.SetRow(WallpaperPreviewBottomTaskbarContainer, taskbarRow);
            Grid.SetColumn(WallpaperPreviewBottomTaskbarContainer, 0);
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
}
