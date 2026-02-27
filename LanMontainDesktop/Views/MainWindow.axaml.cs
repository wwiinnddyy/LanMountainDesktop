using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LanMontainDesktop.Views;

public partial class MainWindow : Window
{
    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
    private int _targetShortSideCells;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _targetShortSideCells = CalculateDefaultShortSideCellCountFromDpi();
        GridSizeNumberBox.Value = _targetShortSideCells;
        DesktopHost.SizeChanged += OnDesktopHostSizeChanged;
        RebuildDesktopGrid();
    }

    protected override void OnClosed(EventArgs e)
    {
        DesktopHost.SizeChanged -= OnDesktopHostSizeChanged;
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
        var hostWidth = DesktopHost.Bounds.Width;
        var hostHeight = DesktopHost.Bounds.Height;
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return;
        }

        var shortSideCells = Math.Max(1, _targetShortSideCells);
        double cellSize;
        int columnCount;
        int rowCount;

        if (hostWidth >= hostHeight)
        {
            rowCount = shortSideCells;
            cellSize = hostHeight / rowCount;
            columnCount = Math.Max(1, (int)Math.Ceiling(hostWidth / cellSize));
        }
        else
        {
            columnCount = shortSideCells;
            cellSize = hostWidth / columnCount;
            rowCount = Math.Max(1, (int)Math.Ceiling(hostHeight / cellSize));
        }

        DesktopGrid.RowDefinitions.Clear();
        DesktopGrid.ColumnDefinitions.Clear();
        DesktopGrid.Width = columnCount * cellSize;
        DesktopGrid.Height = rowCount * cellSize;

        for (var row = 0; row < rowCount; row++)
        {
            DesktopGrid.RowDefinitions.Add(new RowDefinition(new GridLength(cellSize, GridUnitType.Pixel)));
        }

        for (var col = 0; col < columnCount; col++)
        {
            DesktopGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(cellSize, GridUnitType.Pixel)));
        }

        Grid.SetRow(ClockWidget, 0);
        Grid.SetColumn(ClockWidget, 0);
        Grid.SetRowSpan(ClockWidget, 1);
        Grid.SetColumnSpan(ClockWidget, Math.Min(3, columnCount));

        Grid.SetRow(BackToWindowsButton, rowCount - 1);
        Grid.SetColumn(BackToWindowsButton, 0);
        Grid.SetRowSpan(BackToWindowsButton, 1);
        Grid.SetColumnSpan(BackToWindowsButton, Math.Min(4, columnCount));

        GridInfoTextBlock.Text =
            $"Grid: {columnCount} cols x {rowCount} rows | cell {cellSize:F1}px (1:1)";
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
}
