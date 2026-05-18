using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class TransparentOverlayWindow : Window
{
    private const double DefaultCellSize = 100;
    private const string ResizeHandleTag = "fused-desktop-resize-handle";

    private readonly IFusedDesktopLayoutService _layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
    private readonly IWindowBottomMostService _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
    private readonly IRegionPassthroughService _regionPassthroughService = RegionPassthroughServiceFactory.GetOrCreate();
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly FusedDesktopEditGridAdapter _gridAdapter;

    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    private readonly Dictionary<string, Border> _componentHosts = [];
    private readonly List<Rect> _interactiveRegions = [];
    private FusedDesktopLayoutSnapshot _layout = new();
    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private FusedDesktopEditGridContext _gridContext;
    private double _currentDesktopCellSize = DefaultCellSize;

    private DesktopEditSession _editSession;
    private Border? _interactionHost;
    private string? _interactionPlacementId;
    private Rect _interactionOriginalRect;
    private int _interactionStartRow;
    private int _interactionStartColumn;
    private int _interactionStartWidthCells;
    private int _interactionStartHeightCells;
    private int _interactionMinWidthCells;
    private int _interactionMinHeightCells;
    private int _interactionMaxWidthCells;
    private int _interactionMaxHeightCells;
    private DesktopComponentResizeMode _interactionResizeMode = DesktopComponentResizeMode.Proportional;

    private Border? _selectedHost;

    private bool _isSwipeActive;
    private bool _isSwipeDirectionLocked;
    private Point _swipeStartPoint;
    private Point _swipeCurrentPoint;
    private Point _swipeLastPoint;
    private double _swipeVelocityX;
    private long _swipeLastTimestamp;
    private int? _swipePointerId;
    private bool _isThreeFingerOrRightDragSwipeActive;
    private readonly HashSet<int> _activePointerIds = [];

    public event EventHandler? RestoreMainWindowRequested;
    public event EventHandler? ExitEditRequested;
    public event EventHandler? RestoreComponentLibraryRequested;

    public TransparentOverlayWindow()
    {
        InitializeComponent();

        var facade = HostSettingsFacadeProvider.GetOrCreate();
        _settingsFacade = facade;
        _gridAdapter = new FusedDesktopEditGridAdapter(_settingsFacade);
        _weatherDataService = facade.Weather.GetWeatherInfoService();
        _timeZoneService = facade.Region.GetTimeZoneService();

        SizeChanged += OnOverlaySizeChanged;

        if (OperatingSystem.IsWindows())
        {
            _bottomMostService.SetupBottomMost(this);
        }
    }

    public void SaveLayoutAndHide()
    {
        SaveLayout();
        _regionPassthroughService.ClearInteractiveRegions(this);
        Hide();
        ComponentCanvas.Children.Clear();
        _componentHosts.Clear();
        _selectedHost = null;
        _editSession = default;
    }

    public void AddComponentToCenter(string componentId)
    {
        AddComponent(componentId, double.NaN, double.NaN);
    }

    public void AddComponent(string componentId, double x, double y, double? width = null, double? height = null)
    {
        EnsureRegistries();

        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            AppLogger.Warn("TransparentOverlay", $"Cannot add unknown component: {componentId}");
            return;
        }

        EnsureGridContext();
        var (widthCells, heightCells) = ResolveRequestedSpan(descriptor.Definition, width, height);
        var (column, row) = ResolveRequestedCell(x, y, widthCells, heightCells);
        var placement = new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = Guid.NewGuid().ToString("N"),
            ComponentId = componentId,
            GridColumn = column,
            GridRow = row,
            GridWidthCells = widthCells,
            GridHeightCells = heightCells,
            ZIndex = _layout.ComponentPlacements.Count
        };
        ApplyGridPlacementToPixelPlacement(placement);

        _layout.ComponentPlacements.Add(placement);
        try
        {
            RenderComponentInternal(placement);
            UpdateInteractiveRegions();
            SaveLayout();
            AppLogger.Info(
                "TransparentOverlay",
                $"Added component: {componentId} at cell ({column}, {row}) span ({widthCells}x{heightCells})");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TransparentOverlay", $"Failed to add component {componentId}", ex);
            _layout.ComponentPlacements.Remove(placement);
        }
    }

    public void RemoveComponent(string placementId)
    {
        if (_componentHosts.Remove(placementId, out var host))
        {
            ComponentCanvas.Children.Remove(host);
        }

        _layout.ComponentPlacements.RemoveAll(p => string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        UpdateInteractiveRegions();
        SaveLayout();
    }

    public void RenderComponent(string placementId, Control component, double x, double y, double width, double height)
    {
        if (_componentHosts.Remove(placementId, out var existingHost))
        {
            ComponentCanvas.Children.Remove(existingHost);
        }

        component.Width = width;
        component.Height = height;

        var contentGrid = new Grid();
        contentGrid.Children.Add(component);

        var resizeHandle = new Border
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -11, -11),
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Tag = ResizeHandleTag,
            IsVisible = false,
            IsHitTestVisible = false,
            Classes = { "fused-desktop-resize-handle" }
        };
        contentGrid.Children.Add(resizeHandle);

        var host = new Border
        {
            Tag = placementId,
            Width = width,
            Height = height,
            ClipToBounds = false,
            Child = contentGrid,
            Classes = { "fused-desktop-component-host" }
        };

        Canvas.SetLeft(host, x);
        Canvas.SetTop(host, y);

        host.PointerPressed += OnComponentPointerPressed;
        host.PointerMoved += OnInteractionPointerMoved;
        host.PointerReleased += OnInteractionPointerReleased;
        host.PointerCaptureLost += OnInteractionPointerCaptureLost;
        host.ContextRequested += OnComponentContextRequested;

        ComponentCanvas.Children.Add(host);
        _componentHosts[placementId] = host;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ApplyWorkAreaBounds();
        EnsureGridContext();
        EnsureRegistries();

        _layout = _layoutService.Load();
        RenderAllComponents();

        AppLogger.Info(
            "TransparentOverlay",
            $"Opened with {_layout.ComponentPlacements.Count} components. WindowRole=DesktopSurface.");

        RefreshDesktopLayer();

        Dispatcher.UIThread.Post(UpdateInteractiveRegions, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(LogTransparencyDiagnostics, TimeSpan.FromMilliseconds(250));
    }

    public void RefreshDesktopLayer()
    {
        if (!OperatingSystem.IsWindows() || !IsVisible)
        {
            return;
        }

        _bottomMostService.SendToBottom(this);
        AppLogger.Info("TransparentOverlay", "Refreshed desktop layer. WindowRole=DesktopSurface.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveLayout();
        base.OnClosed(e);
    }

    private void OnOverlaySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        EnsureGridContext();
        RenderAllComponents(saveIfMigrated: false);
        Dispatcher.UIThread.Post(UpdateInteractiveRegions, DispatcherPriority.Background);
    }

    private void ApplyWorkAreaBounds()
    {
        if (Screens.Primary is not { } primaryScreen)
        {
            return;
        }

        var workArea = primaryScreen.WorkingArea;
        var scaling = primaryScreen.Scaling;
        Position = new PixelPoint(workArea.X, workArea.Y);
        Width = workArea.Width / scaling;
        Height = workArea.Height / scaling;
    }

    private void LogTransparencyDiagnostics()
    {
        var actualTransparency = ActualTransparencyLevel;
        if (actualTransparency == WindowTransparencyLevel.Transparent)
        {
            AppLogger.Info(
                "TransparentOverlay",
                $"ActualTransparencyLevel={actualTransparency}; overlay should be visually transparent.");
            return;
        }

        AppLogger.Warn(
            "TransparentOverlay",
            $"ActualTransparencyLevel={actualTransparency}; expected Transparent. The platform, window styles, or desktop host attachment may be preventing true transparency.");
    }

    private void EnsureGridContext()
    {
        var viewport = new Size(Math.Max(1, Width), Math.Max(1, Height));
        if (_gridAdapter.TryCreate(viewport, out var context))
        {
            _gridContext = context;
            _currentDesktopCellSize = context.Geometry.CellSize;
            return;
        }

        _gridContext = new FusedDesktopEditGridContext(
            new DesktopGridGeometry(default, DefaultCellSize, 0, 1, 1),
            new DesktopGridMetrics(1, 1, DefaultCellSize, 0, 0, DefaultCellSize, DefaultCellSize));
        _currentDesktopCellSize = DefaultCellSize;
    }

    private void EnsureRegistries()
    {
        if (_componentRuntimeRegistry is not null)
        {
            return;
        }

        var pluginRuntimeService = (Application.Current as App)?.PluginRuntimeService;
        _componentRegistry = DesktopComponentRegistryFactory.Create(pluginRuntimeService);
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);
    }

    private void RenderAllComponents(bool saveIfMigrated = true)
    {
        ComponentCanvas.Children.Clear();
        _componentHosts.Clear();
        _selectedHost = null;

        var migrated = false;
        foreach (var placement in _layout.ComponentPlacements)
        {
            try
            {
                migrated |= EnsurePlacementGridFields(placement);
                RenderComponentInternal(placement);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TransparentOverlay", $"Failed to render component {placement.ComponentId}", ex);
            }
        }

        if (migrated && saveIfMigrated)
        {
            SaveLayout();
        }

        UpdateInteractiveRegions();
    }

    private void RenderComponentInternal(FusedDesktopComponentPlacementSnapshot placement)
    {
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            AppLogger.Warn("TransparentOverlay", $"Unknown component: {placement.ComponentId}");
            return;
        }

        EnsurePlacementGridFields(placement);
        ApplyGridPlacementToPixelPlacement(placement);

        var control = descriptor.CreateControl(
            _currentDesktopCellSize,
            _timeZoneService,
            _weatherDataService,
            _recommendationInfoService,
            _calculatorDataService,
            _settingsFacade,
            placement.PlacementId);

        RenderComponent(placement.PlacementId, control, placement.X, placement.Y, placement.Width, placement.Height);
    }

    private bool EnsurePlacementGridFields(FusedDesktopComponentPlacementSnapshot placement)
    {
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            return false;
        }

        var grid = _gridContext.Geometry;
        var oldRow = placement.GridRow;
        var oldColumn = placement.GridColumn;
        var oldWidthCells = placement.GridWidthCells;
        var oldHeightCells = placement.GridHeightCells;

        var widthCells = placement.GridWidthCells ?? PixelSizeToCellSpan(placement.Width);
        var heightCells = placement.GridHeightCells ?? PixelSizeToCellSpan(placement.Height);
        (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(
            descriptor.Definition,
            widthCells,
            heightCells);
        widthCells = Math.Clamp(widthCells, 1, Math.Max(1, grid.ColumnCount));
        heightCells = Math.Clamp(heightCells, 1, Math.Max(1, grid.RowCount));

        var column = placement.GridColumn ?? PixelPositionToCell(placement.X, grid.Origin.X);
        var row = placement.GridRow ?? PixelPositionToCell(placement.Y, grid.Origin.Y);
        column = Math.Clamp(column, 0, Math.Max(0, grid.ColumnCount - widthCells));
        row = Math.Clamp(row, 0, Math.Max(0, grid.RowCount - heightCells));

        placement.GridColumn = column;
        placement.GridRow = row;
        placement.GridWidthCells = widthCells;
        placement.GridHeightCells = heightCells;
        ApplyGridPlacementToPixelPlacement(placement);

        return oldRow != placement.GridRow ||
               oldColumn != placement.GridColumn ||
               oldWidthCells != placement.GridWidthCells ||
               oldHeightCells != placement.GridHeightCells;
    }

    private void ApplyGridPlacementToPixelPlacement(FusedDesktopComponentPlacementSnapshot placement)
    {
        var grid = _gridContext.Geometry;
        var widthCells = Math.Clamp(placement.GridWidthCells ?? 1, 1, Math.Max(1, grid.ColumnCount));
        var heightCells = Math.Clamp(placement.GridHeightCells ?? 1, 1, Math.Max(1, grid.RowCount));
        var column = Math.Clamp(placement.GridColumn ?? 0, 0, Math.Max(0, grid.ColumnCount - widthCells));
        var row = Math.Clamp(placement.GridRow ?? 0, 0, Math.Max(0, grid.RowCount - heightCells));
        var rect = DesktopPlacementMath.GetCellRect(grid, column, row, widthCells, heightCells);

        placement.GridColumn = column;
        placement.GridRow = row;
        placement.GridWidthCells = widthCells;
        placement.GridHeightCells = heightCells;
        placement.X = rect.X;
        placement.Y = rect.Y;
        placement.Width = rect.Width;
        placement.Height = rect.Height;
    }

    private (int WidthCells, int HeightCells) ResolveRequestedSpan(
        DesktopComponentDefinition definition,
        double? requestedWidth,
        double? requestedHeight)
    {
        var widthCells = requestedWidth.HasValue ? PixelSizeToCellSpan(requestedWidth.Value) : definition.MinWidthCells;
        var heightCells = requestedHeight.HasValue ? PixelSizeToCellSpan(requestedHeight.Value) : definition.MinHeightCells;
        (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(definition, widthCells, heightCells);
        widthCells = Math.Clamp(widthCells, 1, Math.Max(1, _gridContext.Geometry.ColumnCount));
        heightCells = Math.Clamp(heightCells, 1, Math.Max(1, _gridContext.Geometry.RowCount));
        return (widthCells, heightCells);
    }

    private (int Column, int Row) ResolveRequestedCell(double x, double y, int widthCells, int heightCells)
    {
        var grid = _gridContext.Geometry;
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return (
                Math.Max(0, (grid.ColumnCount - widthCells) / 2),
                Math.Max(0, (grid.RowCount - heightCells) / 2));
        }

        var column = PixelPositionToCell(x, grid.Origin.X);
        var row = PixelPositionToCell(y, grid.Origin.Y);
        return (
            Math.Clamp(column, 0, Math.Max(0, grid.ColumnCount - widthCells)),
            Math.Clamp(row, 0, Math.Max(0, grid.RowCount - heightCells)));
    }

    private int PixelSizeToCellSpan(double pixels)
    {
        var grid = _gridContext.Geometry;
        var pitch = Math.Max(1, grid.Pitch);
        var span = (int)Math.Round((Math.Max(1, pixels) + grid.CellGap) / pitch);
        return Math.Max(1, span);
    }

    private int PixelPositionToCell(double position, double origin)
    {
        var pitch = Math.Max(1, _gridContext.Geometry.Pitch);
        return (int)Math.Round((position - origin) / pitch);
    }

    private void UpdateInteractiveRegions()
    {
        _interactiveRegions.Clear();

        foreach (var host in _componentHosts.Values)
        {
            var left = Canvas.GetLeft(host);
            var top = Canvas.GetTop(host);
            var width = host.Width > 0 ? host.Width : host.Bounds.Width;
            var height = host.Height > 0 ? host.Height : host.Bounds.Height;
            if (width > 0 && height > 0)
            {
                _interactiveRegions.Add(new Rect(left - 14, top - 14, width + 28, height + 28));
            }
        }

        if (EditToolbar.IsVisible &&
            EditToolbar.Bounds.Width > 0 &&
            EditToolbar.Bounds.Height > 0 &&
            EditToolbar.TranslatePoint(default, this) is { } toolbarOrigin)
        {
            _interactiveRegions.Add(new Rect(toolbarOrigin, EditToolbar.Bounds.Size));
        }

        _regionPassthroughService.SetInteractiveRegions(this, _interactiveRegions);
    }

    private void SaveLayout()
    {
        _layoutService.Save(_layout);
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == ComponentCanvas)
        {
            DeselectComponent();
        }
    }

    private void OnExitEditClick(object? sender, RoutedEventArgs e)
    {
        ExitEditRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnRestoreComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        RestoreComponentLibraryRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SelectComponent(Border host)
    {
        if (_selectedHost == host)
        {
            return;
        }

        DeselectComponent();
        _selectedHost = host;
        host.Classes.Add("selected");
        SetResizeHandleVisible(host, true);
    }

    private void DeselectComponent()
    {
        if (_selectedHost is null)
        {
            return;
        }

        _selectedHost.Classes.Remove("selected");
        SetResizeHandleVisible(_selectedHost, false);
        _selectedHost = null;
    }

    private static void SetResizeHandleVisible(Border host, bool isVisible)
    {
        if (host.Child is not Grid grid)
        {
            return;
        }

        foreach (var child in grid.Children)
        {
            if (child is Control control && control.Tag as string == ResizeHandleTag)
            {
                control.IsVisible = isVisible;
                control.IsHitTestVisible = isVisible;
                return;
            }
        }
    }

    private void OnComponentContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border host || host.Tag is not string placementId)
        {
            return;
        }

        var deleteItem = new MenuItem
        {
            Header = "移除组件"
        };
        deleteItem.Click += (_, _) => RemoveComponent(placementId);

        var menu = new ContextMenu
        {
            Items = { deleteItem }
        };
        menu.Open(host);
        e.Handled = true;
    }

    private void OnComponentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border host ||
            host.Tag is not string placementId ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var placement = _layout.ComponentPlacements.Find(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null || placement.IsLocked)
        {
            return;
        }

        EnsurePlacementGridFields(placement);
        SelectComponent(host);

        if (e.Source is Control sourceControl && sourceControl.Tag as string == ResizeHandleTag)
        {
            BeginResizeInteraction(host, placement, e);
        }
        else
        {
            BeginMoveInteraction(host, placement, e);
        }

        if (_editSession.IsActive)
        {
            e.Pointer.Capture(host);
            e.Handled = true;
        }
    }

    private void BeginMoveInteraction(Border host, FusedDesktopComponentPlacementSnapshot placement, PointerPressedEventArgs e)
    {
        var pointer = e.GetPosition(this);
        _interactionHost = host;
        _interactionPlacementId = placement.PlacementId;
        _interactionStartRow = placement.GridRow ?? 0;
        _interactionStartColumn = placement.GridColumn ?? 0;
        _interactionOriginalRect = DesktopPlacementMath.GetCellRect(
            _gridContext.Geometry,
            _interactionStartColumn,
            _interactionStartRow,
            placement.GridWidthCells ?? 1,
            placement.GridHeightCells ?? 1);

        var pointerOffset = DesktopPlacementMath.Subtract(
            pointer,
            new Point(_interactionOriginalRect.X, _interactionOriginalRect.Y));
        _editSession = DesktopEditSession.CreateDraggingExisting(
            placement.ComponentId,
            placement.PlacementId,
            pageIndex: 0,
            placement.GridWidthCells ?? 1,
            placement.GridHeightCells ?? 1,
            pointer,
            pointerOffset,
            componentLibraryBounds: null);
    }

    private void BeginResizeInteraction(Border host, FusedDesktopComponentPlacementSnapshot placement, PointerPressedEventArgs e)
    {
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            return;
        }

        var startSpan = ComponentPlacementRules.EnsureMinimumSize(
            descriptor.Definition,
            placement.GridWidthCells ?? 1,
            placement.GridHeightCells ?? 1);
        var minSpan = ComponentPlacementRules.EnsureMinimumSize(
            descriptor.Definition,
            descriptor.Definition.MinWidthCells,
            descriptor.Definition.MinHeightCells);
        var column = placement.GridColumn ?? 0;
        var row = placement.GridRow ?? 0;
        var maxWidthCells = Math.Max(startSpan.WidthCells, _gridContext.Geometry.ColumnCount - column);
        var maxHeightCells = Math.Max(startSpan.HeightCells, _gridContext.Geometry.RowCount - row);

        _interactionHost = host;
        _interactionPlacementId = placement.PlacementId;
        _interactionStartRow = row;
        _interactionStartColumn = column;
        _interactionStartWidthCells = startSpan.WidthCells;
        _interactionStartHeightCells = startSpan.HeightCells;
        _interactionMinWidthCells = Math.Max(1, Math.Min(minSpan.WidthCells, maxWidthCells));
        _interactionMinHeightCells = Math.Max(1, Math.Min(minSpan.HeightCells, maxHeightCells));
        _interactionMaxWidthCells = Math.Max(_interactionMinWidthCells, maxWidthCells);
        _interactionMaxHeightCells = Math.Max(_interactionMinHeightCells, maxHeightCells);
        _interactionResizeMode = descriptor.Definition.ResizeMode;
        _interactionOriginalRect = DesktopPlacementMath.GetCellRect(
            _gridContext.Geometry,
            column,
            row,
            startSpan.WidthCells,
            startSpan.HeightCells);

        _editSession = DesktopEditSession.CreateResizingExisting(
            placement.ComponentId,
            placement.PlacementId,
            pageIndex: 0,
            startSpan.WidthCells,
            startSpan.HeightCells,
            e.GetPosition(this),
            componentLibraryBounds: null) with
        {
            TargetRow = row,
            TargetColumn = column
        };
    }

    private void OnInteractionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_editSession.IsActive || _interactionHost is null)
        {
            return;
        }

        _editSession = _editSession.WithCurrentPointer(e.GetPosition(this));
        if (_editSession.IsDraggingExisting)
        {
            UpdateMoveInteraction();
        }
        else if (_editSession.IsResizingExisting)
        {
            UpdateResizeInteraction();
        }

        e.Handled = true;
    }

    private void UpdateMoveInteraction()
    {
        if (_interactionHost is null)
        {
            return;
        }

        var hasSnap = DesktopPlacementMath.TryGetSnappedCell(
            _gridContext.Geometry,
            _editSession.CurrentPointerInViewport,
            _editSession.PointerOffsetInViewport,
            _editSession.WidthCells,
            _editSession.HeightCells,
            out var column,
            out var row);
        if (!hasSnap)
        {
            return;
        }

        _editSession = _editSession.WithTargetCell(row, column);
        var rect = DesktopPlacementMath.GetCellRect(
            _gridContext.Geometry,
            column,
            row,
            _editSession.WidthCells,
            _editSession.HeightCells);
        ApplyHostRect(_interactionHost, rect);
        UpdateInteractiveRegions();
    }

    private void UpdateResizeInteraction()
    {
        if (_interactionHost is null)
        {
            return;
        }

        var deltaX = _editSession.CurrentPointerInViewport.X - _editSession.StartPointerInViewport.X;
        var deltaY = _editSession.CurrentPointerInViewport.Y - _editSession.StartPointerInViewport.Y;
        int widthCells;
        int heightCells;

        if (_interactionResizeMode == DesktopComponentResizeMode.Free)
        {
            widthCells = Math.Clamp(
                (int)Math.Round(_interactionStartWidthCells + deltaX / _gridContext.Geometry.Pitch),
                _interactionMinWidthCells,
                _interactionMaxWidthCells);
            heightCells = Math.Clamp(
                (int)Math.Round(_interactionStartHeightCells + deltaY / _gridContext.Geometry.Pitch),
                _interactionMinHeightCells,
                _interactionMaxHeightCells);
        }
        else
        {
            var widthScale = (_interactionOriginalRect.Width + deltaX) / Math.Max(1, _interactionOriginalRect.Width);
            var heightScale = (_interactionOriginalRect.Height + deltaY) / Math.Max(1, _interactionOriginalRect.Height);
            var proposedScale = Math.Max(widthScale, heightScale);
            if (double.IsNaN(proposedScale) || double.IsInfinity(proposedScale))
            {
                proposedScale = 1;
            }

            var minScale = Math.Max(
                (double)_interactionMinWidthCells / Math.Max(1, _interactionStartWidthCells),
                (double)_interactionMinHeightCells / Math.Max(1, _interactionStartHeightCells));
            var maxScale = Math.Min(
                (double)_interactionMaxWidthCells / Math.Max(1, _interactionStartWidthCells),
                (double)_interactionMaxHeightCells / Math.Max(1, _interactionStartHeightCells));
            if (maxScale < minScale)
            {
                maxScale = minScale;
            }

            var scale = Math.Clamp(proposedScale, minScale, maxScale);
            widthCells = Math.Clamp(
                (int)Math.Round(_interactionStartWidthCells * scale),
                _interactionMinWidthCells,
                _interactionMaxWidthCells);
            heightCells = Math.Clamp(
                (int)Math.Round(_interactionStartHeightCells * scale),
                _interactionMinHeightCells,
                _interactionMaxHeightCells);
        }

        _editSession = _editSession with
        {
            WidthCells = Math.Max(1, widthCells),
            HeightCells = Math.Max(1, heightCells),
            TargetRow = _interactionStartRow,
            TargetColumn = _interactionStartColumn
        };

        var rect = DesktopPlacementMath.GetCellRect(
            _gridContext.Geometry,
            _interactionStartColumn,
            _interactionStartRow,
            _editSession.WidthCells,
            _editSession.HeightCells);
        ApplyHostRect(_interactionHost, rect);
        UpdateInteractiveRegions();
    }

    private void OnInteractionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_editSession.IsActive || _interactionHost is null || _interactionPlacementId is null)
        {
            ResetInteraction();
            return;
        }

        CompleteInteraction();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnInteractionPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_editSession.IsActive || _interactionHost is null)
        {
            return;
        }

        CompleteInteraction();
    }

    private void CompleteInteraction()
    {
        if (_interactionPlacementId is null)
        {
            ResetInteraction();
            return;
        }

        var placement = _layout.ComponentPlacements.Find(p =>
            string.Equals(p.PlacementId, _interactionPlacementId, StringComparison.OrdinalIgnoreCase));
        if (placement is not null && _editSession.HasTargetCell)
        {
            placement.GridRow = _editSession.TargetRow;
            placement.GridColumn = _editSession.TargetColumn;
            placement.GridWidthCells = Math.Max(1, _editSession.WidthCells);
            placement.GridHeightCells = Math.Max(1, _editSession.HeightCells);
            ApplyGridPlacementToPixelPlacement(placement);
            if (_interactionHost is not null)
            {
                ApplyHostRect(_interactionHost, new Rect(placement.X, placement.Y, placement.Width, placement.Height));
            }

            SaveLayout();
        }

        UpdateInteractiveRegions();
        ResetInteraction();
    }

    private void ResetInteraction()
    {
        _editSession = default;
        _interactionHost = null;
        _interactionPlacementId = null;
        _interactionOriginalRect = default;
        _interactionStartRow = 0;
        _interactionStartColumn = 0;
        _interactionStartWidthCells = 0;
        _interactionStartHeightCells = 0;
        _interactionMinWidthCells = 0;
        _interactionMinHeightCells = 0;
        _interactionMaxWidthCells = 0;
        _interactionMaxHeightCells = 0;
        _interactionResizeMode = DesktopComponentResizeMode.Proportional;
    }

    private static void ApplyHostRect(Border host, Rect rect)
    {
        Canvas.SetLeft(host, rect.X);
        Canvas.SetTop(host, rect.Y);
        host.Width = Math.Max(1, rect.Width);
        host.Height = Math.Max(1, rect.Height);
        if (host.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Control component)
        {
            component.Width = host.Width;
            component.Height = host.Height;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        if (!appSnapshot.EnableThreeFingerSwipe)
        {
            base.OnPointerPressed(e);
            return;
        }

        if (!TryGetPointerPosition(e, out var pointerPos))
        {
            base.OnPointerPressed(e);
            return;
        }

        var currentPoint = e.GetCurrentPoint(this);
        var pointerId = e.Pointer?.Id ?? 0;
        var isRightButtonPressed = currentPoint.Properties.IsRightButtonPressed;
        var isLeftButtonPressed = currentPoint.Properties.IsLeftButtonPressed;

        if (isLeftButtonPressed || isRightButtonPressed)
        {
            _activePointerIds.Add(pointerId);
        }

        var isThreeFinger = _activePointerIds.Count >= 3;
        var isRightDrag = isRightButtonPressed;

        if (isThreeFinger || isRightDrag)
        {
            _isSwipeActive = true;
            _isThreeFingerOrRightDragSwipeActive = true;
            _isSwipeDirectionLocked = false;
            _swipeStartPoint = pointerPos;
            _swipeCurrentPoint = pointerPos;
            _swipeLastPoint = pointerPos;
            _swipeVelocityX = 0;
            _swipeLastTimestamp = Stopwatch.GetTimestamp();
            _swipePointerId = pointerId;
            e.Handled = true;
        }
        else
        {
            base.OnPointerPressed(e);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_isSwipeActive && !IsSwipePointer(e.Pointer))
        {
            base.OnPointerMoved(e);
            return;
        }

        if (!_isSwipeActive)
        {
            base.OnPointerMoved(e);
            return;
        }

        if (!TryGetPointerPosition(e, out var pointerPos))
        {
            base.OnPointerMoved(e);
            return;
        }

        _swipeCurrentPoint = pointerPos;
        UpdateSwipeVelocity(pointerPos);

        var deltaX = _swipeCurrentPoint.X - _swipeStartPoint.X;
        var deltaY = _swipeCurrentPoint.Y - _swipeStartPoint.Y;

        if (!_isSwipeDirectionLocked)
        {
            const double activationThreshold = 14;
            const double horizontalBias = 1.15;
            var absDeltaX = Math.Abs(deltaX);
            var absDeltaY = Math.Abs(deltaY);

            if (absDeltaY >= activationThreshold && absDeltaY > absDeltaX * horizontalBias)
            {
                CancelSwipeInteraction(e.Pointer);
                base.OnPointerMoved(e);
                return;
            }

            if (absDeltaX < activationThreshold || absDeltaX <= absDeltaY * horizontalBias)
            {
                base.OnPointerMoved(e);
                return;
            }

            _isSwipeDirectionLocked = true;
            if (e.Pointer?.Captured != this)
            {
                e.Pointer?.Capture(this);
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);

        if (_isSwipeActive && !IsSwipePointer(e.Pointer))
        {
            base.OnPointerReleased(e);
            return;
        }

        if (_isSwipeActive && EndSwipeInteraction(e.Pointer))
        {
            e.Handled = true;
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);

        if (_isSwipeActive && !IsSwipePointer(e.Pointer))
        {
            base.OnPointerCaptureLost(e);
            return;
        }

        if (_isSwipeActive && e.Pointer?.Captured == this)
        {
            base.OnPointerCaptureLost(e);
            return;
        }

        if (_isSwipeActive)
        {
            EndSwipeInteraction(e.Pointer);
        }

        base.OnPointerCaptureLost(e);
    }

    private bool TryGetPointerPosition(PointerEventArgs e, out Point point)
    {
        try
        {
            point = e.GetPosition(this);
            return true;
        }
        catch
        {
            point = default;
            return false;
        }
    }

    private bool IsSwipePointer(IPointer? pointer)
    {
        return !_swipePointerId.HasValue ||
               pointer is not null && pointer.Id == _swipePointerId.Value;
    }

    private void UpdateSwipeVelocity(Point currentPoint)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_swipeLastTimestamp, now).TotalSeconds;
        if (elapsed > 0)
        {
            _swipeVelocityX = (currentPoint.X - _swipeLastPoint.X) / elapsed;
        }

        _swipeLastPoint = currentPoint;
        _swipeLastTimestamp = now;
    }

    private void CancelSwipeInteraction(IPointer? pointer)
    {
        if (!_isSwipeActive)
        {
            return;
        }

        if (pointer?.Captured == this)
        {
            pointer.Capture(null);
        }

        _isSwipeActive = false;
        _isSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _swipePointerId = null;
        _swipeVelocityX = 0;
        _swipeLastTimestamp = 0;
    }

    private bool EndSwipeInteraction(IPointer? pointer)
    {
        if (!_isSwipeActive)
        {
            return false;
        }

        var wasDirectionLocked = _isSwipeDirectionLocked;
        var wasThreeFingerOrRightDrag = _isThreeFingerOrRightDragSwipeActive;

        _isSwipeActive = false;
        _isSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _swipePointerId = null;

        if (pointer?.Captured == this)
        {
            pointer.Capture(null);
        }

        _swipeLastTimestamp = 0;

        if (!wasDirectionLocked)
        {
            _swipeVelocityX = 0;
            return false;
        }

        var deltaX = _swipeCurrentPoint.X - _swipeStartPoint.X;
        var deltaY = _swipeCurrentPoint.Y - _swipeStartPoint.Y;
        var absDeltaX = Math.Abs(deltaX);
        var distanceThreshold = Math.Max(48, Bounds.Width * 0.14);
        var velocityThreshold = Math.Max(860, Bounds.Width * 1.08);
        var hasDistanceIntent = absDeltaX >= distanceThreshold && absDeltaX > Math.Abs(deltaY) * 1.05;
        var hasVelocityIntent = Math.Abs(_swipeVelocityX) >= velocityThreshold;

        if (wasThreeFingerOrRightDrag && deltaX < 0 && (hasDistanceIntent || hasVelocityIntent))
        {
            RestoreMainWindowRequested?.Invoke(this, EventArgs.Empty);
            _swipeVelocityX = 0;
            return true;
        }

        _swipeVelocityX = 0;
        return hasDistanceIntent || hasVelocityIntent;
    }
}
