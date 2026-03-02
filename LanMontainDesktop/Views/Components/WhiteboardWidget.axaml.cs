using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using DotNetCampus.Inking;
using FluentIcons.Avalonia;
using SkiaSharp;

namespace LanMontainDesktop.Views.Components;

public partial class WhiteboardWidget : UserControl, IDesktopComponentWidget
{
    private enum WhiteboardToolMode
    {
        Pen,
        Eraser
    }

    private static readonly PropertyInfo? StrokeColorProperty = typeof(SkiaStroke).GetProperty(nameof(SkiaStroke.Color));
    private readonly int _baseWidthCells;
    private double _currentCellSize = 48;
    private WhiteboardToolMode _toolMode = WhiteboardToolMode.Pen;
    private bool? _isNightModeApplied;
    private SKColor _currentInkColor = SKColors.Black;

    public WhiteboardWidget()
        : this(baseWidthCells: 2)
    {
    }

    public WhiteboardWidget(int baseWidthCells)
    {
        _baseWidthCells = Math.Max(1, baseWidthCells);
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ConfigureInkCanvas();
        ApplyCellSize(_currentCellSize);
        ApplyThemeVisual(force: true);
        SetToolMode(WhiteboardToolMode.Pen);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyThemeVisual(force: true);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Keep all state in-memory for lightweight re-attach scenarios.
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ApplyThemeVisual(force: false);
    }

    private void ConfigureInkCanvas()
    {
        InkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        var settings = InkCanvas.AvaloniaSkiaInkCanvas.Settings;
        settings.IgnorePressure = true;
        settings.InkThickness = 2.5f;
        settings.EraserSize = new Size(20, 20);
        settings.IsBitmapCacheEnabled = true;
        settings.MaxBitmapCacheSize = 2048;
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);

        var availableWidth = Bounds.Width > 1 ? Bounds.Width : (_currentCellSize * _baseWidthCells);
        var buttonSize = Math.Clamp(availableWidth * 0.15, 24, 40);
        var buttonCornerRadius = buttonSize * 0.5;
        var toolbarSpacing = Math.Clamp(buttonSize * 0.25, 4, 10);
        var toolbarPaddingHorizontal = Math.Clamp(buttonSize * 0.36, 6, 12);
        var toolbarPaddingVertical = Math.Clamp(buttonSize * 0.24, 4, 8);

        RootBorder.Padding = new Thickness(Math.Clamp(_currentCellSize * 0.14, 6, 14));
        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.34, 12, 28));
        CanvasBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.24, 10, 22));
        ToolbarBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.22, 10, 20));
        ToolbarBorder.Padding = new Thickness(toolbarPaddingHorizontal, toolbarPaddingVertical);
        ToolbarButtonsPanel.Spacing = toolbarSpacing;

        foreach (var button in new[] { PenButton, EraserButton, ClearButton, ExportButton })
        {
            button.Width = buttonSize;
            button.Height = buttonSize;
            button.CornerRadius = new CornerRadius(buttonCornerRadius);
        }

        var settings = InkCanvas.AvaloniaSkiaInkCanvas.Settings;
        settings.InkThickness = (float)Math.Clamp(_currentCellSize * 0.06, 2.0, 6.0);
        var eraserSize = Math.Clamp(_currentCellSize * 0.42, 12, 44);
        settings.EraserSize = new Size(eraserSize, eraserSize);
    }

    private void ApplyThemeVisual(bool force)
    {
        var isNightMode = ResolveIsNightMode();
        if (!force && _isNightModeApplied.HasValue && _isNightModeApplied.Value == isNightMode)
        {
            return;
        }

        _isNightModeApplied = isNightMode;
        _currentInkColor = isNightMode ? SKColors.White : SKColors.Black;

        RootBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#FF181B22") : Color.Parse("#FFF1F4F9"));
        CanvasBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#FF000000") : Color.Parse("#FFFFFFFF"));
        CanvasBorder.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#30FFFFFF") : Color.Parse("#24000000"));
        ToolbarBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#1AFFFFFF") : Color.Parse("#E6FFFFFF"));
        ToolbarBorder.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#26FFFFFF") : Color.Parse("#16000000"));

        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = _currentInkColor;
        RecolorAllStrokes(_currentInkColor);
        RefreshToolButtonVisuals();
    }

    private void RecolorAllStrokes(SKColor targetColor)
    {
        for (var i = 0; i < InkCanvas.Strokes.Count; i++)
        {
            TrySetStrokeColor(InkCanvas.Strokes[i], targetColor);
        }

        InkCanvas.AvaloniaSkiaInkCanvas.InvalidateBitmapCache();
        InkCanvas.InvalidateVisual();
    }

    private static void TrySetStrokeColor(SkiaStroke stroke, SKColor color)
    {
        if (StrokeColorProperty is null)
        {
            return;
        }

        try
        {
            StrokeColorProperty.SetValue(stroke, color);
        }
        catch
        {
            // Keep current stroke color when reflection is unavailable.
        }
    }

    private bool ResolveIsNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush brush)
        {
            return CalculateRelativeLuminance(brush.Color) < 0.45;
        }

        return false;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private void SetToolMode(WhiteboardToolMode mode)
    {
        _toolMode = mode;
        InkCanvas.EditingMode = mode == WhiteboardToolMode.Pen
            ? InkCanvasEditingMode.Ink
            : InkCanvasEditingMode.EraseByPoint;

        if (mode == WhiteboardToolMode.Pen)
        {
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = _currentInkColor;
        }

        RefreshToolButtonVisuals();
    }

    private void RefreshToolButtonVisuals()
    {
        var isNightMode = _isNightModeApplied ?? ResolveIsNightMode();
        var activeBackground = ResolveThemeBrush("AdaptiveAccentBrush", isNightMode ? Color.Parse("#FF93C5FD") : Color.Parse("#FF3B82F6"));
        var activeForeground = ResolveThemeBrush("AdaptiveOnAccentBrush", Colors.White);
        var idleForeground = ResolveThemeBrush("AdaptiveTextPrimaryBrush", isNightMode ? Color.Parse("#FFE5E7EB") : Color.Parse("#FF0F172A"));
        var idleBackground = new SolidColorBrush(isNightMode ? Color.Parse("#33FFFFFF") : Color.Parse("#14000000"));

        ApplyToolButtonVisual(PenButton, _toolMode == WhiteboardToolMode.Pen, activeBackground, activeForeground, idleBackground, idleForeground);
        ApplyToolButtonVisual(EraserButton, _toolMode == WhiteboardToolMode.Eraser, activeBackground, activeForeground, idleBackground, idleForeground);
        ApplyToolButtonVisual(ClearButton, false, activeBackground, activeForeground, idleBackground, idleForeground);
        ApplyToolButtonVisual(ExportButton, false, activeBackground, activeForeground, idleBackground, idleForeground);
    }

    private static void ApplyToolButtonVisual(
        Button button,
        bool isActive,
        IBrush activeBackground,
        IBrush activeForeground,
        IBrush idleBackground,
        IBrush idleForeground)
    {
        button.Background = isActive ? activeBackground : idleBackground;
        button.Foreground = isActive ? activeForeground : idleForeground;
        button.BorderThickness = new Thickness(0);

        if (button.Content is SymbolIcon symbolIcon)
        {
            symbolIcon.Foreground = button.Foreground;
        }
    }

    private IBrush ResolveThemeBrush(string key, Color fallback)
    {
        if (this.TryFindResource(key, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private void OnPenButtonClick(object? sender, RoutedEventArgs e)
    {
        SetToolMode(WhiteboardToolMode.Pen);
    }

    private void OnEraserButtonClick(object? sender, RoutedEventArgs e)
    {
        SetToolMode(WhiteboardToolMode.Eraser);
    }

    private void OnClearButtonClick(object? sender, RoutedEventArgs e)
    {
        var strokeList = InkCanvas.Strokes.ToList();
        foreach (var stroke in strokeList)
        {
            try
            {
                if (ReferenceEquals(stroke.InkCanvas, InkCanvas.AvaloniaSkiaInkCanvas))
                {
                    InkCanvas.AvaloniaSkiaInkCanvas.RemoveStaticStroke(stroke);
                }
            }
            catch
            {
                // Keep the widget alive even if one stroke removal fails.
            }
        }

        InkCanvas.AvaloniaSkiaInkCanvas.UseBitmapCache(false);
        InkCanvas.AvaloniaSkiaInkCanvas.InvalidateBitmapCache();
        InkCanvas.InvalidateVisual();
    }

    private async void OnExportButtonClick(object? sender, RoutedEventArgs e)
    {
        var fileName = $"whiteboard-{DateTime.Now:yyyyMMdd-HHmmss}.svg";
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is not null)
        {
            var saveFile = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Whiteboard SVG",
                SuggestedFileName = fileName,
                DefaultExtension = "svg",
                FileTypeChoices =
                [
                    new FilePickerFileType("SVG image")
                    {
                        Patterns = ["*.svg"],
                        MimeTypes = ["image/svg+xml"]
                    }
                ]
            });

            if (saveFile is null)
            {
                return;
            }

            await using var saveStream = await saveFile.OpenWriteAsync();
            ExportSvgToStream(saveStream);
            return;
        }

        var exportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMontainDesktop",
            "Exports");
        Directory.CreateDirectory(exportFolder);
        var savePath = Path.Combine(exportFolder, fileName);
        await using var fileStream = File.Create(savePath);
        ExportSvgToStream(fileStream);
    }

    private void ExportSvgToStream(Stream stream)
    {
        var width = Math.Max(1d, CanvasBorder.Bounds.Width);
        var height = Math.Max(1d, CanvasBorder.Bounds.Height);
        var bounds = SKRect.Create((float)width, (float)height);

        using var svgCanvas = SKSvgCanvas.Create(bounds, stream);
        using var backgroundPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = (_isNightModeApplied ?? false) ? SKColors.Black : SKColors.White
        };
        svgCanvas.DrawRect(bounds, backgroundPaint);

        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        foreach (var stroke in InkCanvas.Strokes)
        {
            strokePaint.Color = stroke.Color;
            svgCanvas.DrawPath(stroke.Path, strokePaint);
        }

        svgCanvas.Flush();
    }
}
