using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using LanMountainDesktop.Platform.Abstractions;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public sealed record DesktopPanelComponentHostSpec(
    DesktopPanelPlacementSnapshot Placement,
    Control Content,
    int MinWidthCells,
    int MinHeightCells,
    double CornerRadius);

public partial class DesktopPanelWindow : Window
{
    private const string ComponentHostClass = "panel-component-host";
    private const string EditHostClass = "panel-edit-host";
    private const string SelectedHostClass = "panel-selected";

    private static readonly LocalizationService LocalizationService = new();

    private readonly IWindowBottomMostService _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
    private readonly IRegionPassthroughService _regionPassthroughService = RegionPassthroughServiceFactory.GetOrCreate();
    private readonly Dictionary<string, PanelHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _useDesktopLayer;

    private DesktopPanelGridMetrics _metrics = DesktopPanelMetrics.CalculateGrid(
        DesktopPanelMetrics.DefaultCellSize,
        DesktopPanelMetrics.DefaultColumns,
        DesktopPanelMetrics.DefaultRows);
    private bool _isEditMode;
    private string? _selectedPlacementId;
    private PanelInteraction? _interaction;

    public DesktopPanelWindow()
        : this(useDesktopLayer: true)
    {
    }

    public DesktopPanelWindow(bool useDesktopLayer)
    {
        _useDesktopLayer = useDesktopLayer;
        InitializeComponent();

        ShowActivated = false;
        if (_useDesktopLayer && OperatingSystem.IsWindows())
        {
            _bottomMostService.SetupBottomMost(this);
        }

        AddHandler(PointerMovedEvent, OnPanelPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPanelPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPanelPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyDown += OnPanelKeyDown;
        Opened += OnPanelOpened;
        SizeChanged += OnPanelSizeChanged;

        ApplyLocalization();
        ApplyMetrics(_metrics);
    }

    internal event EventHandler<DesktopPanelPlacementSnapshot>? PlacementChanged;

    internal event EventHandler<string>? RemoveRequested;

    internal event EventHandler? AddComponentRequested;

    internal void ApplyLocalization()
    {
        var languageCode = HostSettingsFacadeProvider.GetOrCreate().Region.Get().LanguageCode;
        EmptyStateTitleTextBlock.Text = LocalizationService.GetString(
            languageCode,
            "desktop_panel.empty.title",
            "Your desktop panel is empty");
        EmptyStateDescriptionTextBlock.Text = LocalizationService.GetString(
            languageCode,
            "desktop_panel.empty.description",
            "Add widgets and they will all run inside this one panel.");
        AddComponentButtonTextBlock.Text = LocalizationService.GetString(
            languageCode,
            "desktop_panel.empty.add_button",
            "Add widgets");
        var removeText = LocalizationService.GetString(
            languageCode,
            "desktop_panel.remove",
            "Remove widget");
        foreach (var host in _hosts.Values)
        {
            host.RemoveMenuItem.Header = removeText;
        }
    }

    internal void ApplyMetrics(DesktopPanelGridMetrics metrics)
    {
        _metrics = metrics;
        Width = metrics.WindowWidth;
        Height = metrics.WindowHeight;
        PanelShell.Padding = new Thickness(metrics.Padding);

        PanelGrid.Width = metrics.GridWidth;
        PanelGrid.Height = metrics.GridHeight;
        PanelGrid.ColumnSpacing = metrics.CellGap;
        PanelGrid.RowSpacing = metrics.CellGap;
        PanelGrid.RowDefinitions.Clear();
        PanelGrid.ColumnDefinitions.Clear();
        for (var row = 0; row < metrics.RowCount; row++)
        {
            PanelGrid.RowDefinitions.Add(new RowDefinition(new GridLength(metrics.CellSize, GridUnitType.Pixel)));
        }

        for (var column = 0; column < metrics.ColumnCount; column++)
        {
            PanelGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(metrics.CellSize, GridUnitType.Pixel)));
        }

        foreach (var host in _hosts.Values)
        {
            ApplyGridPlacement(host);
            UpdateHostCornerRadius(host);
        }

        // 最小面板（4×3、36px 格子）里放不下图标和三行说明，只留标题与按钮。
        var compactEmptyState = metrics.WindowHeight < 220d;
        EmptyStateIcon.IsVisible = !compactEmptyState;
        EmptyStateDescriptionTextBlock.IsVisible = !compactEmptyState;

        UpdateInteractiveRegion();
    }

    internal void Reconcile(
        IReadOnlyList<DesktopPanelPlacementSnapshot> placements,
        Func<DesktopPanelPlacementSnapshot, DesktopPanelComponentHostSpec?> hostFactory)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(hostFactory);

        var desiredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in placements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.PlacementId))
            {
                continue;
            }

            var placementId = placement.PlacementId.Trim();
            desiredIds.Add(placementId);
            if (_hosts.TryGetValue(placementId, out var existingHost))
            {
                CopyPlacement(placement, existingHost.Placement);
                ApplyGridPlacement(existingHost);
                ApplyHostEditState(existingHost);
                continue;
            }

            var spec = hostFactory(placement);
            if (spec is null)
            {
                continue;
            }

            AddHost(spec);
        }

        foreach (var placementId in _hosts.Keys.Where(id => !desiredIds.Contains(id)).ToArray())
        {
            RemoveHost(placementId, disposeContent: true);
        }

        UpdateEmptyState();
        PushComponentPageContext();
    }

    internal void RemovePlacement(string placementId)
    {
        if (RemoveHost(placementId, disposeContent: true))
        {
            UpdateEmptyState();
        }
    }

    internal void UpdateCellSize(double cellSize)
    {
        var safeCellSize = DesktopPanelMetrics.NormalizeCellSize(cellSize);
        foreach (var host in _hosts.Values)
        {
            if (host.Content is IDesktopComponentWidget componentWidget)
            {
                componentWidget.ApplyCellSize(safeCellSize);
            }
        }
    }

    internal void SetEditMode(bool editMode)
    {
        if (_isEditMode == editMode)
        {
            return;
        }

        _isEditMode = editMode;
        if (!editMode)
        {
            SelectHost(null);
        }

        foreach (var host in _hosts.Values)
        {
            ApplyHostEditState(host);
        }

        PushComponentPageContext();
    }

    internal void RefreshComponentContexts()
    {
        PushComponentPageContext();
        foreach (var host in _hosts.Values)
        {
            ComponentSettingsRefresh.RefreshAll(host.Content);
        }
    }

    internal void DockToBottom(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var position = DesktopPanelMetrics.CalculateBottomDockPosition(
            screen.WorkingArea,
            screen.Scaling,
            new Size(Width, Height));
        if (_useDesktopLayer)
        {
            _bottomMostService.SetScreenPosition(this, position, queueOnFailure: true);
            _bottomMostService.SendToBottom(this);
            return;
        }

        Position = position;
    }

    internal void RefreshDesktopLayer()
    {
        if (_useDesktopLayer)
        {
            _bottomMostService.SendToBottom(this);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        KeyDown -= OnPanelKeyDown;
        Opened -= OnPanelOpened;
        SizeChanged -= OnPanelSizeChanged;
        _regionPassthroughService.ClearInteractiveRegions(this);
        foreach (var placementId in _hosts.Keys.ToArray())
        {
            RemoveHost(placementId, disposeContent: true);
        }

        base.OnClosed(e);
    }

    private void OnPanelOpened(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateInteractiveRegion();
    }

    private void OnPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateInteractiveRegion();
    }

    private void UpdateInteractiveRegion()
    {
        var width = Math.Max(0d, PanelShell.Bounds.Width > 0d ? PanelShell.Bounds.Width : Width);
        var height = Math.Max(0d, PanelShell.Bounds.Height > 0d ? PanelShell.Bounds.Height : Height);
        if (width <= 0d || height <= 0d)
        {
            _regionPassthroughService.ClearInteractiveRegions(this);
            return;
        }

        var cornerRadius = AdaptiveTokens.TryGet<CornerRadius>(
            this,
            "DesignCornerRadiusLg",
            out var radius)
            ? Math.Max(0d, radius.TopLeft)
            : 24d;
        _regionPassthroughService.SetInteractiveRegions(
            this,
            [new WindowInteractiveRegion(new Rect(0d, 0d, width, height), cornerRadius)]);
    }

    private void AddHost(DesktopPanelComponentHostSpec spec)
    {
        var placement = spec.Placement.Clone();
        if (string.IsNullOrWhiteSpace(placement.PlacementId))
        {
            return;
        }

        var contentHost = new Border
        {
            Background = Brushes.Transparent,
            ClipToBounds = true,
            Child = spec.Content
        };

        var resizeHandle = new Border
        {
            Width = 14d,
            Height = 14d,
            Margin = new Thickness(0, 0, 4, 4),
            CornerRadius = new CornerRadius(4),
            Background = AdaptiveTokens.Brush(this, ThemeResourceKeys.AccentBrush, Brushes.DodgerBlue),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            IsVisible = false
        };
        resizeHandle.PointerPressed += OnResizeHandlePointerPressed;

        var hostChrome = new Grid
        {
            ClipToBounds = false
        };
        hostChrome.Children.Add(contentHost);
        hostChrome.Children.Add(resizeHandle);

        var host = new Border
        {
            Tag = placement.PlacementId,
            Background = Brushes.Transparent,
            ClipToBounds = false,
            Child = hostChrome
        };
        host.Classes.Add(ComponentHostClass);
        host.PointerPressed += OnHostPointerPressed;

        var removeMenuItem = new MenuItem
        {
            Header = LocalizationService.GetString(
                HostSettingsFacadeProvider.GetOrCreate().Region.Get().LanguageCode,
                "desktop_panel.remove",
                "Remove widget")
        };
        removeMenuItem.Click += (_, _) => RemoveRequested?.Invoke(this, placement.PlacementId);
        var contextMenu = new ContextMenu
        {
            Items = { removeMenuItem }
        };

        var panelHost = new PanelHost(
            host,
            contentHost,
            resizeHandle,
            spec.Content,
            placement,
            Math.Max(1, spec.MinWidthCells),
            Math.Max(1, spec.MinHeightCells),
            Math.Max(0d, spec.CornerRadius),
            removeMenuItem,
            contextMenu);
        _hosts[placement.PlacementId] = panelHost;
        PanelGrid.Children.Add(host);
        ApplyGridPlacement(panelHost);
        UpdateHostCornerRadius(panelHost);
        ApplyHostEditState(panelHost);
        host.AttachedToVisualTree += (_, _) => PushComponentPageContext();
        UpdateEmptyState();
    }

    private bool RemoveHost(string placementId, bool disposeContent)
    {
        if (!_hosts.Remove(placementId, out var host))
        {
            return false;
        }

        host.Host.PointerPressed -= OnHostPointerPressed;
        host.ResizeHandle.PointerPressed -= OnResizeHandlePointerPressed;

        // 必须在摘下可视树之前退订：TimeZoneService 是应用级单例，而解绑助手按可视树往下找。
        if (disposeContent)
        {
            DesktopComponentContext.ClearTimeZoneServiceBindings(host.Content);
            if (host.Content is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        PanelGrid.Children.Remove(host.Host);

        if (string.Equals(_selectedPlacementId, placementId, StringComparison.OrdinalIgnoreCase))
        {
            SelectHost(null);
        }

        return true;
    }

    private void ApplyGridPlacement(PanelHost host)
    {
        Grid.SetColumn(host.Host, Math.Max(0, host.Placement.Column));
        Grid.SetRow(host.Host, Math.Max(0, host.Placement.Row));
        Grid.SetColumnSpan(host.Host, Math.Max(1, host.Placement.WidthCells));
        Grid.SetRowSpan(host.Host, Math.Max(1, host.Placement.HeightCells));
    }

    private void UpdateHostCornerRadius(PanelHost host)
    {
        var radius = new CornerRadius(host.CornerRadius);
        host.ContentHost.CornerRadius = radius;
        host.Host.CornerRadius = radius;
    }

    private void ApplyHostEditState(PanelHost host)
    {
        host.ContentHost.IsHitTestVisible = !_isEditMode;
        host.Host.Classes.Set(EditHostClass, _isEditMode);
        host.Host.Classes.Set(
            SelectedHostClass,
            _isEditMode &&
            string.Equals(host.Placement.PlacementId, _selectedPlacementId, StringComparison.OrdinalIgnoreCase));
        host.Host.ContextMenu = _isEditMode ? host.ContextMenu : null;
        host.ResizeHandle.IsVisible =
            _isEditMode &&
            string.Equals(host.Placement.PlacementId, _selectedPlacementId, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectHost(string? placementId)
    {
        _selectedPlacementId = placementId;
        foreach (var host in _hosts.Values)
        {
            ApplyHostEditState(host);
        }
    }

    private void UpdateEmptyState()
    {
        EmptyStatePanel.IsVisible = _hosts.Count == 0;
    }

    private void PushComponentPageContext()
    {
        foreach (var host in _hosts.Values)
        {
            DesktopComponentContext.ApplyPageContext(host.Content, isOnActivePage: true, _isEditMode);
        }
    }

    private void OnHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEditMode ||
            sender is not Border host ||
            host.Tag is not string placementId ||
            !_hosts.TryGetValue(placementId, out var panelHost))
        {
            return;
        }

        var point = e.GetCurrentPoint(host);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        SelectHost(placementId);
        Focus();
        var interactionPoint = e.GetPosition(PanelGrid);
        var rect = DesktopPlacementMath.GetCellRect(
            CreateGridGeometry(),
            panelHost.Placement.Column,
            panelHost.Placement.Row,
            panelHost.Placement.WidthCells,
            panelHost.Placement.HeightCells);
        _interaction = new PanelInteraction
        {
            Kind = PanelInteractionKind.Move,
            PlacementId = placementId,
            StartPoint = interactionPoint,
            PointerOffset = interactionPoint - rect.TopLeft,
            StartRect = rect,
            Started = false
        };

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEditMode ||
            sender is not Border handle ||
            FindHostByResizeHandle(handle) is not { } panelHost)
        {
            return;
        }

        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        SelectHost(panelHost.Placement.PlacementId);
        Focus();
        var interactionPoint = e.GetPosition(PanelGrid);
        var rect = DesktopPlacementMath.GetCellRect(
            CreateGridGeometry(),
            panelHost.Placement.Column,
            panelHost.Placement.Row,
            panelHost.Placement.WidthCells,
            panelHost.Placement.HeightCells);
        _interaction = new PanelInteraction
        {
            Kind = PanelInteractionKind.Resize,
            PlacementId = panelHost.Placement.PlacementId,
            StartPoint = interactionPoint,
            PointerOffset = default,
            StartRect = rect,
            Started = false
        };

        e.Pointer.Capture(this);
        e.Handled = true;

        PanelHost? FindHostByResizeHandle(Border candidate)
        {
            return _hosts.Values.FirstOrDefault(host => ReferenceEquals(host.ResizeHandle, candidate));
        }
    }

    private void OnPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_interaction is null)
        {
            return;
        }

        var point = e.GetPosition(PanelGrid);
        if (!_interaction.Started)
        {
            var threshold = DesktopPlacementMath.ComputeDragStartThreshold(_metrics.CellSize);
            if (!DesktopPlacementMath.HasExceededThreshold(_interaction.StartPoint, point, threshold))
            {
                return;
            }

            _interaction.Started = true;
        }

        if (_interaction.Kind == PanelInteractionKind.Move)
        {
            ApplyMoveInteraction(point);
        }
        else
        {
            ApplyResizeInteraction(point);
        }

        e.Handled = true;
    }

    private void ApplyMoveInteraction(Point point)
    {
        if (_interaction is null ||
            !_hosts.TryGetValue(_interaction.PlacementId, out var host))
        {
            return;
        }

        var geometry = CreateGridGeometry();
        if (!DesktopPlacementMath.TryGetSnappedCell(
                geometry,
                point,
                _interaction.PointerOffset,
                host.Placement.WidthCells,
                host.Placement.HeightCells,
                out var column,
                out var row))
        {
            return;
        }

        if (host.Placement.Column == column && host.Placement.Row == row)
        {
            return;
        }

        var candidate = host.Placement.Clone();
        candidate.Column = column;
        candidate.Row = row;
        if (!CanApply(candidate))
        {
            return;
        }

        CopyPlacement(candidate, host.Placement);
        ApplyGridPlacement(host);
        _interaction.Changed = true;
    }

    private void ApplyResizeInteraction(Point point)
    {
        if (_interaction is null ||
            !_hosts.TryGetValue(_interaction.PlacementId, out var host))
        {
            return;
        }

        var delta = point - _interaction.StartPoint;
        var geometry = CreateGridGeometry();
        var maxWidthCells = Math.Max(1, _metrics.ColumnCount - host.Placement.Column);
        var maxHeightCells = Math.Max(1, _metrics.RowCount - host.Placement.Row);
        var minWidthCells = Math.Clamp(host.MinWidthCells, 1, maxWidthCells);
        var minHeightCells = Math.Clamp(host.MinHeightCells, 1, maxHeightCells);
        var widthCells = Math.Max(
            minWidthCells,
            DesktopPlacementMath.EstimateCellSpan(_interaction.StartRect.Width + delta.X, geometry));
        var heightCells = Math.Max(
            minHeightCells,
            DesktopPlacementMath.EstimateCellSpan(_interaction.StartRect.Height + delta.Y, geometry));
        widthCells = Math.Clamp(widthCells, minWidthCells, maxWidthCells);
        heightCells = Math.Clamp(heightCells, minHeightCells, maxHeightCells);

        if (widthCells == host.Placement.WidthCells && heightCells == host.Placement.HeightCells)
        {
            return;
        }

        var candidate = host.Placement.Clone();
        candidate.WidthCells = widthCells;
        candidate.HeightCells = heightCells;
        if (!CanApply(candidate))
        {
            return;
        }

        CopyPlacement(candidate, host.Placement);
        ApplyGridPlacement(host);
        _interaction.Changed = true;
    }

    private bool CanApply(DesktopPanelPlacementSnapshot candidate)
    {
        var placements = _hosts.Values.Select(host => host.Placement).ToArray();
        return DesktopPanelPlacementRules.CanApplyPlacement(
            placements,
            candidate,
            _metrics.ColumnCount,
            _metrics.RowCount);
    }

    private void OnPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_interaction is null)
        {
            return;
        }

        EndInteraction();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPanelPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _ = sender;
        _ = e;
        EndInteraction();
    }

    private void EndInteraction()
    {
        var interaction = _interaction;
        _interaction = null;
        if (interaction is null || !interaction.Changed ||
            !_hosts.TryGetValue(interaction.PlacementId, out var host))
        {
            return;
        }

        // 缩放跨格期间格子边长没变，组件自身会随 SizeChanged 重排；只在松手时补一次格子口径。
        if (interaction.Kind == PanelInteractionKind.Resize &&
            host.Content is IDesktopComponentWidget componentWidget)
        {
            componentWidget.ApplyCellSize(_metrics.CellSize);
        }

        PlacementChanged?.Invoke(this, host.Placement.Clone());
    }

    private void OnPanelBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (_isEditMode && e.GetCurrentPoint(PanelGrid).Properties.IsLeftButtonPressed)
        {
            SelectHost(null);
            Focus();
        }
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isEditMode)
        {
            return;
        }

        if (e.Key == Key.Delete && _selectedPlacementId is { } placementId)
        {
            RemoveRequested?.Invoke(this, placementId);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SelectHost(null);
            e.Handled = true;
        }
    }

    private void OnPanelAddComponentClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        AddComponentRequested?.Invoke(this, EventArgs.Empty);
    }

    private DesktopGridGeometry CreateGridGeometry()
    {
        return new DesktopGridGeometry(
            new Point(0, 0),
            _metrics.CellSize,
            _metrics.CellGap,
            _metrics.ColumnCount,
            _metrics.RowCount);
    }

    private static void CopyPlacement(
        DesktopPanelPlacementSnapshot source,
        DesktopPanelPlacementSnapshot target)
    {
        target.PlacementId = source.PlacementId;
        target.ComponentId = source.ComponentId;
        target.Column = source.Column;
        target.Row = source.Row;
        target.WidthCells = source.WidthCells;
        target.HeightCells = source.HeightCells;
    }

    private sealed class PanelHost(
        Border host,
        Border contentHost,
        Border resizeHandle,
        Control content,
        DesktopPanelPlacementSnapshot placement,
        int minWidthCells,
        int minHeightCells,
        double cornerRadius,
        MenuItem removeMenuItem,
        ContextMenu contextMenu)
    {
        public Border Host { get; } = host;

        public Border ContentHost { get; } = contentHost;

        public Border ResizeHandle { get; } = resizeHandle;

        public Control Content { get; } = content;

        public DesktopPanelPlacementSnapshot Placement { get; } = placement;

        public int MinWidthCells { get; } = minWidthCells;

        public int MinHeightCells { get; } = minHeightCells;

        public double CornerRadius { get; } = cornerRadius;

        public MenuItem RemoveMenuItem { get; } = removeMenuItem;

        public ContextMenu ContextMenu { get; } = contextMenu;
    }

    private sealed class PanelInteraction
    {
        public PanelInteractionKind Kind { get; init; }

        public string PlacementId { get; init; } = string.Empty;

        public Point StartPoint { get; init; }

        public Point PointerOffset { get; init; }

        public Rect StartRect { get; init; }

        public bool Started { get; set; }

        public bool Changed { get; set; }
    }

    private enum PanelInteractionKind
    {
        Move,
        Resize
    }
}
