using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using DotNetCampus.Inking;
using DotNetCampus.Inking.Primitive;
using FluentIcons.Avalonia;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using SkiaSharp;

namespace LanMountainDesktop.Views.Components;

public partial class WhiteboardWidget : UserControl, IDesktopComponentWidget, IComponentPlacementContextAware, IDisposable
{
    private enum WhiteboardToolMode
    {
        Pen,
        Eraser
    }

    private static readonly PropertyInfo? StrokeColorProperty = typeof(SkiaStroke).GetProperty(nameof(SkiaStroke.Color));
    private static readonly PropertyInfo? StrokePointListProperty = typeof(SkiaStroke).GetProperty("PointList");
    private readonly int _baseWidthCells;
    private readonly IComponentInstanceSettingsStore _componentSettingsStore = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly IWhiteboardNotePersistenceService _notePersistenceService = new WhiteboardNotePersistenceService();
    private readonly DispatcherTimer _noteSaveTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private double _currentCellSize = 48;
    private WhiteboardToolMode _toolMode = WhiteboardToolMode.Pen;
    private bool? _isNightModeApplied;
    private SKColor _selectedInkColor = SKColors.Black;
    private bool _isUserCustomColor;
    private float _selectedInkThickness = 2.5f;
    private string _componentId = BuiltInComponentIds.DesktopWhiteboard;
    private string _placementId = string.Empty;
    private int _noteRetentionDays = WhiteboardNoteRetentionPolicy.DefaultDays;
    private bool _isApplyingPersistedSnapshot;
    private bool _noteDirty;
    private int _noteLoadRevision;
    private bool _disposed;

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
        _noteSaveTimer.Tick += OnNoteSaveTimerTick;

        ConfigureInkCanvas();
        ApplyCellSize(_currentCellSize);
        RefreshFromSettings();
        ApplyThemeVisual(force: true);
        InitializeColorPicker();
        SetToolMode(WhiteboardToolMode.Pen);
    }

    private void InitializeColorPicker()
    {
        if (InkColorPicker is not null)
        {
            InkColorPicker.Color = new Color(
                _selectedInkColor.Alpha,
                _selectedInkColor.Red,
                _selectedInkColor.Green,
                _selectedInkColor.Blue);
        }

        if (InkThicknessSlider is not null)
        {
            InkThicknessSlider.Value = _selectedInkThickness;
        }
    }

    public int NoteRetentionDays => _noteRetentionDays;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyThemeVisual(force: true);
        SchedulePersistedNoteLoad();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        PersistNoteImmediately();
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
        settings.InkThickness = _selectedInkThickness;
        settings.EraserSize = new Size(20, 20);
        settings.IsBitmapCacheEnabled = true;
        settings.MaxBitmapCacheSize = 2048;
        InkCanvas.StrokeCollected += OnInkCanvasStrokeCollected;
        InkCanvas.PointerReleased += OnInkCanvasPointerReleased;
        InkCanvas.PointerCaptureLost += OnInkCanvasPointerCaptureLost;
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

        RootBorder.Padding = new Thickness(ComponentChromeCornerRadiusHelper.SafeValue(_currentCellSize * 0.14, 6, 14));
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;
        CanvasBorder.CornerRadius = mainRectangleCornerRadius;
        ToolbarBorder.CornerRadius = mainRectangleCornerRadius;
        ToolbarBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(toolbarPaddingHorizontal, 6, 12),
            ComponentChromeCornerRadiusHelper.SafeValue(toolbarPaddingVertical, 4, 8));
        ToolbarButtonsPanel.Spacing = toolbarSpacing;

        foreach (var button in new[] { PenButton, EraserButton, ClearButton, ExportButton })
        {
            button.Width = buttonSize;
            button.Height = buttonSize;
            button.CornerRadius = new CornerRadius(buttonCornerRadius);
        }

        var settings = InkCanvas.AvaloniaSkiaInkCanvas.Settings;
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

        var wasNightMode = _isNightModeApplied;
        _isNightModeApplied = isNightMode;

        RootBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#FF181B22") : Color.Parse("#FFF1F4F9"));
        CanvasBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#FF000000") : Color.Parse("#FFFFFFFF"));
        CanvasBorder.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#30FFFFFF") : Color.Parse("#24000000"));
        ToolbarBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#1AFFFFFF") : Color.Parse("#E6FFFFFF"));
        ToolbarBorder.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#26FFFFFF") : Color.Parse("#16000000"));

        ApplyThemeDefaultInkColor(isNightMode, wasNightMode);
        RefreshToolButtonVisuals();
    }

    private void ApplyThemeDefaultInkColor(bool isNightMode, bool? wasNightMode)
    {
        if (_isUserCustomColor || wasNightMode == isNightMode)
        {
            return;
        }

        var oldDefault = wasNightMode == true ? SKColors.White : SKColors.Black;
        var newDefault = isNightMode ? SKColors.White : SKColors.Black;

        if (_selectedInkColor == oldDefault)
        {
            _selectedInkColor = newDefault;
            if (_toolMode == WhiteboardToolMode.Pen)
            {
                InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = _selectedInkColor;
            }

            if (InkColorPicker is not null)
            {
                InkColorPicker.Color = new Color(
                    _selectedInkColor.Alpha,
                    _selectedInkColor.Red,
                    _selectedInkColor.Green,
                    _selectedInkColor.Blue);
            }
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        var nextComponentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopWhiteboard
            : componentId.Trim();
        var nextPlacementId = placementId?.Trim() ?? string.Empty;

        if (_noteDirty &&
            HasValidPersistenceContext() &&
            (string.Compare(_componentId, nextComponentId, StringComparison.OrdinalIgnoreCase) != 0 ||
             string.Compare(_placementId, nextPlacementId, StringComparison.OrdinalIgnoreCase) != 0))
        {
            PersistNoteImmediately();
        }

        _componentId = nextComponentId;
        _placementId = nextPlacementId;
        RefreshFromSettings();
        ClearAllStrokes();
        SchedulePersistedNoteLoad();
    }

    public void RefreshFromSettings()
    {
        try
        {
            if (!HasValidPersistenceContext())
            {
                _noteRetentionDays = WhiteboardNoteRetentionPolicy.DefaultDays;
                return;
            }

            var snapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
            _noteRetentionDays = NormalizeRetentionDays(snapshot.WhiteboardNoteRetentionDays);
            _notePersistenceService.TryDeleteExpiredNote(_componentId, _placementId, _noteRetentionDays);
        }
        catch
        {
            _noteRetentionDays = WhiteboardNoteRetentionPolicy.DefaultDays;
        }
    }

    public void ForceSaveNote()
    {
        if (_disposed || !HasValidPersistenceContext())
        {
            return;
        }

        if (!_noteDirty)
        {
            return;
        }

        _noteDirty = false;
        _noteSaveTimer.Stop();
        var noteSnapshot = BuildNoteSnapshot();
        try
        {
            _notePersistenceService.SaveNote(_componentId, _placementId, noteSnapshot, _noteRetentionDays);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _noteSaveTimer.Stop();
        _noteSaveTimer.Tick -= OnNoteSaveTimerTick;
        InkCanvas.StrokeCollected -= OnInkCanvasStrokeCollected;
        InkCanvas.PointerReleased -= OnInkCanvasPointerReleased;
        InkCanvas.PointerCaptureLost -= OnInkCanvasPointerCaptureLost;
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

    private static int NormalizeRetentionDays(int days)
    {
        return WhiteboardNoteRetentionPolicy.NormalizeDays(
            days <= 0
                ? WhiteboardNoteRetentionPolicy.DefaultDays
                : days);
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
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = _selectedInkColor;
        }

        RefreshToolButtonVisuals();
    }

    private void SetInkColor(SKColor color)
    {
        _selectedInkColor = color;
        if (_toolMode == WhiteboardToolMode.Pen)
        {
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = _selectedInkColor;
        }
        RefreshToolButtonVisuals();
    }

    private void SetInkThickness(float thickness)
    {
        _selectedInkThickness = Math.Clamp(thickness, 1.0f, 8.0f);
        if (_toolMode == WhiteboardToolMode.Pen)
        {
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkThickness = _selectedInkThickness;
        }
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
        if (_toolMode == WhiteboardToolMode.Pen && ColorPickerPopup is not null)
        {
            if (ColorPickerPopup.IsOpen)
            {
                ColorPickerPopup.Close();
            }
            else
            {
                ColorPickerPopup.Open();
            }
        }
        else
        {
            SetToolMode(WhiteboardToolMode.Pen);
        }
    }

    private void OnColorPickerColorChanged(object? sender, ColorChangedEventArgs e)
    {
        var color = e.NewColor;
        var skColor = new SKColor(color.R, color.G, color.B, color.A);
        _isUserCustomColor = skColor != SKColors.Black && skColor != SKColors.White;
        SetInkColor(skColor);
    }

    private void OnInkThicknessSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        SetInkThickness((float)e.NewValue);
    }

    private void OnEraserButtonClick(object? sender, RoutedEventArgs e)
    {
        SetToolMode(WhiteboardToolMode.Eraser);
    }

    private void OnClearButtonClick(object? sender, RoutedEventArgs e)
    {
        ClearAllStrokes();
        QueueNoteSave();
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
            "LanMountainDesktop",
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

    private void OnInkCanvasStrokeCollected(object? sender, DotNetCampus.Inking.Contexts.AvaloniaSkiaInkCanvasStrokeCollectedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueueNoteSave();
    }

    private void OnInkCanvasPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        _ = sender;
        _ = e;
        QueueNoteSave();
    }

    private void OnInkCanvasPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        _ = sender;
        _ = e;
        QueueNoteSave();
    }

    private void OnNoteSaveTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        if (_disposed || _isApplyingPersistedSnapshot || !HasValidPersistenceContext())
        {
            _noteSaveTimer.Stop();
            return;
        }

        if (!_noteDirty)
        {
            _noteSaveTimer.Stop();
            return;
        }

        var noteSnapshot = BuildNoteSnapshot();
        var componentId = _componentId;
        var placementId = _placementId;
        var retentionDays = _noteRetentionDays;
        _noteDirty = false;
        _noteSaveTimer.Stop();
        _ = Task.Run(() => _notePersistenceService.SaveNote(componentId, placementId, noteSnapshot, retentionDays));
    }

    private void QueueNoteSave()
    {
        if (_disposed || _isApplyingPersistedSnapshot || !HasValidPersistenceContext())
        {
            return;
        }

        _noteDirty = true;
        if (!_noteSaveTimer.IsEnabled)
        {
            _noteSaveTimer.Start();
        }
    }

    private void PersistNoteImmediately()
    {
        if (_disposed || _isApplyingPersistedSnapshot || !HasValidPersistenceContext())
        {
            return;
        }

        if (!_noteDirty)
        {
            return;
        }

        _noteDirty = false;
        _noteSaveTimer.Stop();
        var noteSnapshot = BuildNoteSnapshot();
        try
        {
            _notePersistenceService.SaveNote(_componentId, _placementId, noteSnapshot, _noteRetentionDays);
        }
        catch
        {
        }
    }

    private async void SchedulePersistedNoteLoad()
    {
        if (!HasValidPersistenceContext())
        {
            return;
        }

        var revision = ++_noteLoadRevision;
        var componentId = _componentId;
        var placementId = _placementId;
        var retentionDays = _noteRetentionDays;

        try
        {
            var noteSnapshot = await Task.Run(() => _notePersistenceService.LoadNote(componentId, placementId, retentionDays));
            if (_disposed || revision != _noteLoadRevision ||
                !string.Equals(_componentId, componentId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_placementId, placementId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || revision != _noteLoadRevision)
                {
                    return;
                }

                _isApplyingPersistedSnapshot = true;
                try
                {
                    ClearAllStrokes();
                    ApplyNoteSnapshot(noteSnapshot);
                }
                finally
                {
                    _isApplyingPersistedSnapshot = false;
                }
            });
        }
        catch
        {
            // Best effort only. Whiteboard should stay usable if persistence is unavailable.
        }
    }

    private WhiteboardNoteSnapshot BuildNoteSnapshot()
    {
        return new WhiteboardNoteSnapshot
        {
            Strokes = InkCanvas.Strokes
                .Select(BuildStrokeSnapshot)
                .Where(static stroke => stroke.Points.Count > 0)
                .ToList()
        };
    }

    private static WhiteboardStrokeSnapshot BuildStrokeSnapshot(SkiaStroke stroke)
    {
        var pointList = TryGetStrokePoints(stroke);
        return new WhiteboardStrokeSnapshot
        {
            Color = ToHexColor(stroke.Color),
            InkThickness = stroke.InkThickness,
            IgnorePressure = stroke.IgnorePressure,
            Points = pointList
                .Select(static point => new WhiteboardStylusPointSnapshot
                {
                    X = point.X,
                    Y = point.Y,
                    Pressure = point.Pressure,
                    Width = point.Width ?? 0,
                    Height = point.Height ?? 0
                })
                .ToList()
        };
    }

    private void ApplyNoteSnapshot(WhiteboardNoteSnapshot snapshot)
    {
        if (snapshot.Strokes.Count == 0)
        {
            return;
        }

        var renderer = InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkStrokeRenderer;
        foreach (var strokeSnapshot in snapshot.Strokes)
        {
            var stylusPoints = strokeSnapshot.Points
                .Select(ConvertStylusPoint)
                .ToList();
            if (stylusPoints.Count == 0)
            {
                continue;
            }

            var path = renderer.RenderInkToPath(stylusPoints, strokeSnapshot.InkThickness);
            var staticStroke = SkiaStroke.CreateStaticStroke(
                InkId.NewId(),
                path,
                new StylusPointListSpan(stylusPoints, 0, stylusPoints.Count),
                ParseStrokeColor(strokeSnapshot.Color),
                (float)strokeSnapshot.InkThickness,
                strokeSnapshot.IgnorePressure,
                renderer);
            InkCanvas.AvaloniaSkiaInkCanvas.AddStaticStroke(staticStroke);
        }

        InkCanvas.AvaloniaSkiaInkCanvas.UpdateBitmapCache();
        InkCanvas.InvalidateVisual();
    }

    private static InkStylusPoint ConvertStylusPoint(WhiteboardStylusPointSnapshot point)
    {
        return new InkStylusPoint(point.X, point.Y, (float)Math.Clamp(point.Pressure, 0f, 1f))
        {
            Width = point.Width > 0 ? point.Width : null,
            Height = point.Height > 0 ? point.Height : null
        };
    }

    private static SKColor ParseStrokeColor(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                var color = Color.Parse(value);
                return new SKColor(color.R, color.G, color.B, color.A);
            }
            catch
            {
                // Fall through to the default color.
            }
        }

        return SKColors.Black;
    }

    private static string ToHexColor(SKColor color)
    {
        return $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    private void ClearAllStrokes()
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

    private bool HasValidPersistenceContext()
    {
        return !string.IsNullOrWhiteSpace(_componentId) &&
               !string.IsNullOrWhiteSpace(_placementId);
    }

    private static IReadOnlyList<InkStylusPoint> TryGetStrokePoints(SkiaStroke stroke)
    {
        if (StrokePointListProperty?.GetValue(stroke) is IReadOnlyList<InkStylusPoint> pointList)
        {
            return pointList;
        }

        return Array.Empty<InkStylusPoint>();
    }
}
