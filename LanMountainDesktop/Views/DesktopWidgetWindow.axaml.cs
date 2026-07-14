using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views;

public partial class DesktopWidgetWindow : Window
{
    private readonly IWindowBottomMostService _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
    private readonly IRegionPassthroughService _regionPassthroughService = RegionPassthroughServiceFactory.GetOrCreate();
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();

    private bool _isEditMode;
    private bool _isDragging;
    private PixelPoint _dragStartWindowPosition;
    private PixelPoint _dragStartPointerScreenPosition;

    private DesktopWidgetResizeAdorner? _resizeAdorner;
    private bool _isResizing;
    private Size _resizeStartPhysicalSize;
    private PixelPoint _resizeStartPosition;
    private int _resizeStartWidthCells;
    private int _resizeStartHeightCells;
    private double _componentCornerRadius;
    private Border? _componentRootBorder;
    private Control? _interactiveRegionTarget;
    private Transform? _interactiveRegionTransform;
    private Transform? _componentContentTransform;
    private bool _interactiveRegionUpdatePending;
    private bool _isApplyingComponentChrome;
    private bool _componentChromeApplyPending;
    private bool _componentChromeDeferredApplyPending;
    private bool _isClosing;

    public string? PlacementId { get; }

    public DesktopWidgetWindow()
    {
        InitializeComponent();
        AppLogger.Info("DesktopWidgetWindow", "Initialized. WindowRole=DesktopSurface.");

        if (OperatingSystem.IsWindows())
        {
            _bottomMostService.SetupBottomMost(this);
        }
    }

    public DesktopWidgetWindow(
        Control componentContent,
        string? placementId = null,
        double cornerRadius = 0d) : this()
    {
        PlacementId = placementId;
        ComponentContainer.Child = componentContent;
        componentContent.Loaded += OnComponentContentLoaded;
        componentContent.PropertyChanged += OnComponentContentPropertyChanged;
        SetComponentContentTransform(componentContent.RenderTransform as Transform);
        SetupResizeAdorner();
        UpdateComponentChrome(cornerRadius);
    }

    private void SetupResizeAdorner()
    {
        _resizeAdorner = new DesktopWidgetResizeAdorner
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            IsVisible = false
        };

        _resizeAdorner.ResizeStarted += OnResizeStarted;
        _resizeAdorner.Resizing += OnResizing;
        _resizeAdorner.ResizeCompleted += OnResizeCompleted;

        if (RootGrid is Grid grid)
        {
            grid.Children.Add(_resizeAdorner);
        }
    }

    public void SetEditMode(bool editMode)
    {
        if (_isEditMode == editMode) return;
        _isEditMode = editMode;

        if (ComponentContainer.Child is Control child)
        {
            child.IsHitTestVisible = !editMode;
        }

        if (editMode)
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
            _resizeAdorner?.Show();
            if (EditModeBorder is not null)
            {
                EditModeBorder.IsVisible = true;
            }
        }
        else
        {
            Cursor = null;
            _resizeAdorner?.Hide();
            if (EditModeBorder is not null)
            {
                EditModeBorder.IsVisible = false;
            }
        }

        AppLogger.Info("DesktopWidgetWindow", $"Edit mode set to {editMode}. PlacementId='{PlacementId}'.");

        if (OperatingSystem.IsWindows() && IsVisible)
        {
            ScheduleInteractiveRegionUpdate();
        }
    }

    public void UpdateComponentChrome(double cornerRadius)
    {
        _componentCornerRadius = double.IsFinite(cornerRadius)
            ? Math.Max(0d, cornerRadius)
            : 0d;

        ApplyComponentChrome();

        if (OperatingSystem.IsWindows() && IsVisible)
        {
            ScheduleInteractiveRegionUpdate();
        }
    }

    private void OnComponentContentLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyComponentChrome();
    }

    private void OnComponentContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is UserControl && e.Property == ContentControl.ContentProperty)
        {
            ApplyComponentChrome();
            ScheduleInteractiveRegionUpdate();
            return;
        }

        if (e.Property == Visual.RenderTransformProperty && sender is Control componentContent)
        {
            SetComponentContentTransform(componentContent.RenderTransform as Transform);
        }

        if (e.Property == Visual.IsVisibleProperty ||
            e.Property == Visual.BoundsProperty ||
            e.Property == Visual.RenderTransformProperty ||
            e.Property == Visual.RenderTransformOriginProperty)
        {
            ScheduleInteractiveRegionUpdate();
        }
    }

    private void SetComponentContentTransform(Transform? transform)
    {
        if (ReferenceEquals(_componentContentTransform, transform))
        {
            return;
        }

        if (_componentContentTransform is not null)
        {
            _componentContentTransform.Changed -= OnComponentContentTransformChanged;
        }

        _componentContentTransform = transform;
        if (_componentContentTransform is not null)
        {
            _componentContentTransform.Changed += OnComponentContentTransformChanged;
        }
    }

    private void OnComponentContentTransformChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ScheduleInteractiveRegionUpdate();
    }

    private void ApplyComponentChrome()
    {
        if (_isClosing)
        {
            return;
        }

        if (_isApplyingComponentChrome)
        {
            _componentChromeApplyPending = true;
            return;
        }

        var passCount = 0;
        do
        {
            passCount++;
            _componentChromeApplyPending = false;
            _isApplyingComponentChrome = true;
            try
            {
                var cornerRadius = new CornerRadius(_componentCornerRadius);
                _componentRootBorder = TryGetDirectRootBorder(ComponentContainer.Child as Control);

                if (_componentRootBorder is not null)
                {
                    // The direct component surface owns the single outer contour. A local empty
                    // BoxShadow value is intentional: ClearValue would fall back to the global
                    // component-surface style and recreate an out-of-window shadow.
                    _componentRootBorder.CornerRadius = cornerRadius;
                    _componentRootBorder.ClipToBounds = true;
                    _componentRootBorder.BoxShadow = default;

                    ComponentContainer.CornerRadius = default;
                    ComponentContainer.ClipToBounds = false;
                }
                else
                {
                    ComponentContainer.CornerRadius = cornerRadius;
                    ComponentContainer.ClipToBounds = true;
                }

                SetInteractiveRegionTarget(
                    _componentRootBorder ??
                    ComponentContainer.Child as Control ??
                    ComponentContainer);

                if (EditModeBorder is not null)
                {
                    EditModeBorder.CornerRadius = cornerRadius;
                }
            }
            finally
            {
                _isApplyingComponentChrome = false;
            }
        } while (_componentChromeApplyPending && passCount < 3);

        if (_componentChromeApplyPending && !_componentChromeDeferredApplyPending)
        {
            _componentChromeDeferredApplyPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _componentChromeDeferredApplyPending = false;
                if (!_isClosing)
                {
                    ApplyComponentChrome();
                }
            }, DispatcherPriority.Render);
        }
    }

    private static Border? TryGetDirectRootBorder(Control? componentContent)
    {
        if (componentContent is Border border)
        {
            return border;
        }

        return componentContent is UserControl { Content: Border contentBorder }
            ? contentBorder
            : null;
    }

    public void UpdateComponentLayout(double width, double height)
    {
        ComponentContainer.Width = width;
        ComponentContainer.Height = height;

        if (ComponentContainer.Child is Control child)
        {
            child.Width = width;
            child.Height = height;
        }

        if (_resizeAdorner is not null)
        {
            _resizeAdorner.Width = width;
            _resizeAdorner.Height = height;
        }

        if (OperatingSystem.IsWindows() && IsVisible)
        {
            ScheduleInteractiveRegionUpdate();
        }
    }

    public void RefreshDesktopLayer()
    {
        if (!OperatingSystem.IsWindows() || !IsVisible)
        {
            return;
        }

        _bottomMostService.SendToBottom(this);
        ScheduleInteractiveRegionUpdate();
        AppLogger.Info("DesktopWidgetWindow", "Refreshed desktop layer. WindowRole=DesktopSurface.");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        RefreshDesktopLayer();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (OperatingSystem.IsWindows() && IsVisible)
        {
            UpdateInteractiveRegion();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_isResizing)
        {
            return;
        }

        if (_isEditMode && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginDrag(e);
            e.Handled = true;
            return;
        }

        if (!_isEditMode && e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            ShowContextMenu(e);
            e.Handled = true;
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_isDragging)
        {
            var currentPointerScreenPosition = this.PointToScreen(e.GetPosition(this));
            var deltaX = currentPointerScreenPosition.X - _dragStartPointerScreenPosition.X;
            var deltaY = currentPointerScreenPosition.Y - _dragStartPointerScreenPosition.Y;

            SetScreenPosition(new PixelPoint(
                _dragStartWindowPosition.X + deltaX,
                _dragStartWindowPosition.Y + deltaY));

            e.Handled = true;
            return;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag();
            e.Handled = true;
            return;
        }

        base.OnPointerReleased(e);
    }

    private void BeginDrag(PointerPressedEventArgs e)
    {
        _isDragging = true;
        _dragStartWindowPosition = GetScreenPosition();
        _dragStartPointerScreenPosition = this.PointToScreen(e.GetPosition(this));
        e.Pointer.Capture(this);
    }

    private void EndDrag()
    {
        _isDragging = false;

        if (PlacementId is not null)
        {
            var layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
            var layout = layoutService.Load();
            var placement = layout.ComponentPlacements.Find(
                p => string.Equals(p.PlacementId, PlacementId, StringComparison.OrdinalIgnoreCase));
            if (placement is not null)
            {
                ApplySnappedDragPlacement(placement);
                layoutService.Save(layout);
            }
        }

        RefreshDesktopLayer();
    }

    private void ApplySnappedDragPlacement(FusedDesktopComponentPlacementSnapshot placement)
    {
        var originalPlacement = placement.Clone();
        var currentPosition = GetScreenPosition();
        if (!TrySnapToCurrentScreenGrid(placement, currentPosition, out var snappedPosition) ||
            !snappedPosition.HasValue)
        {
            placement.X = currentPosition.X;
            placement.Y = currentPosition.Y;
            return;
        }

        placement.X = snappedPosition.Value.X;
        placement.Y = snappedPosition.Value.Y;
        if (SetScreenPosition(snappedPosition.Value))
        {
            var actualPosition = GetScreenPosition();
            placement.X = actualPosition.X;
            placement.Y = actualPosition.Y;
        }
        else
        {
            RestorePlacementLayout(placement, originalPlacement);
            placement.X = currentPosition.X;
            placement.Y = currentPosition.Y;
            UpdateComponentLayout(placement.Width, placement.Height);
        }
    }

    private bool TrySnapToCurrentScreenGrid(
        FusedDesktopComponentPlacementSnapshot placement,
        PixelPoint requestedPosition,
        out PixelPoint? snappedPosition)
    {
        snappedPosition = null;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return false;
        }

        var scaling = Math.Max(0.1, screen.Scaling);
        var workArea = screen.WorkingArea;
        var viewportSize = new Size(workArea.Width / scaling, workArea.Height / scaling);
        var adapter = new FusedDesktopEditGridAdapter(_settingsFacade);
        if (!adapter.TryCreate(viewportSize, out var context))
        {
            return false;
        }

        var requestedLocalOrigin = new Point(
            (requestedPosition.X - workArea.X) / scaling,
            (requestedPosition.Y - workArea.Y) / scaling);
        var localPlacement = placement.Clone();
        localPlacement.X = requestedLocalOrigin.X;
        localPlacement.Y = requestedLocalOrigin.Y;

        var snappedLocalPlacement = FusedDesktopPlacementMath.SnapToNearestCell(
            localPlacement,
            context.Geometry,
            requestedLocalOrigin);
        placement.Width = snappedLocalPlacement.Width;
        placement.Height = snappedLocalPlacement.Height;
        placement.GridColumn = snappedLocalPlacement.GridColumn;
        placement.GridRow = snappedLocalPlacement.GridRow;
        placement.GridWidthCells = snappedLocalPlacement.GridWidthCells;
        placement.GridHeightCells = snappedLocalPlacement.GridHeightCells;

        snappedPosition = new PixelPoint(
            workArea.X + (int)Math.Round(snappedLocalPlacement.X * scaling),
            workArea.Y + (int)Math.Round(snappedLocalPlacement.Y * scaling));
        placement.X = snappedPosition.Value.X;
        placement.Y = snappedPosition.Value.Y;
        UpdateComponentLayout(placement.Width, placement.Height);
        return true;
    }

    private void ShowContextMenu(PointerPressedEventArgs e)
    {
        var removeItem = new MenuItem
        {
            Header = "移除组件"
        };
        removeItem.Click += (_, _) =>
        {
            if (PlacementId is not null)
            {
                FusedDesktopManagerServiceFactory.GetOrCreate().RemoveComponent(PlacementId);
            }
            else
            {
                Close();
            }
        };

        var menu = new ContextMenu
        {
            Items = { removeItem }
        };
        menu.Open(this);
    }

    private void UpdateInteractiveRegion()
    {
        var width = Math.Max(0d, RootGrid.Bounds.Width > 0 ? RootGrid.Bounds.Width : Bounds.Width);
        var height = Math.Max(0d, RootGrid.Bounds.Height > 0 ? RootGrid.Bounds.Height : Bounds.Height);
        if (width <= 0d || height <= 0d)
        {
            _regionPassthroughService.ClearInteractiveRegions(this);
            return;
        }

        var interactiveRegion = _isEditMode
            ? new WindowInteractiveRegion(new Rect(0, 0, width, height), 0d)
            : ResolveLiveInteractiveRegion(new Rect(0, 0, width, height));
        if (!interactiveRegion.HasValue)
        {
            _regionPassthroughService.ClearInteractiveRegions(this);
            return;
        }

        _regionPassthroughService.SetInteractiveRegions(this, new List<WindowInteractiveRegion>
        {
            interactiveRegion.Value
        });
    }

    private void SetInteractiveRegionTarget(Control target)
    {
        if (ReferenceEquals(_interactiveRegionTarget, target))
        {
            return;
        }

        if (_interactiveRegionTarget is not null)
        {
            _interactiveRegionTarget.LayoutUpdated -= OnInteractiveRegionTargetLayoutUpdated;
            _interactiveRegionTarget.PropertyChanged -= OnInteractiveRegionTargetPropertyChanged;
        }

        SetInteractiveRegionTransform(null);

        _interactiveRegionTarget = target;
        _interactiveRegionTarget.LayoutUpdated += OnInteractiveRegionTargetLayoutUpdated;
        _interactiveRegionTarget.PropertyChanged += OnInteractiveRegionTargetPropertyChanged;
        SetInteractiveRegionTransform(_interactiveRegionTarget.RenderTransform as Transform);
    }

    private void OnInteractiveRegionTargetLayoutUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ScheduleInteractiveRegionUpdate();
    }

    private void OnInteractiveRegionTargetPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (!_isApplyingComponentChrome &&
            ReferenceEquals(sender, _componentRootBorder) &&
            (e.Property == Border.BoxShadowProperty ||
             e.Property == Border.CornerRadiusProperty ||
             e.Property == Visual.ClipToBoundsProperty))
        {
            ApplyComponentChrome();
        }

        if (e.Property == Visual.RenderTransformProperty && sender is Control target)
        {
            SetInteractiveRegionTransform(target.RenderTransform as Transform);
        }

        if (e.Property == Visual.BoundsProperty ||
            e.Property == Visual.RenderTransformProperty ||
            e.Property == Visual.RenderTransformOriginProperty ||
            e.Property == Visual.IsVisibleProperty)
        {
            ScheduleInteractiveRegionUpdate();
        }
    }

    private void SetInteractiveRegionTransform(Transform? transform)
    {
        if (ReferenceEquals(_interactiveRegionTransform, transform))
        {
            return;
        }

        if (_interactiveRegionTransform is not null)
        {
            _interactiveRegionTransform.Changed -= OnInteractiveRegionTransformChanged;
        }

        _interactiveRegionTransform = transform;
        if (_interactiveRegionTransform is not null)
        {
            _interactiveRegionTransform.Changed += OnInteractiveRegionTransformChanged;
        }
    }

    private void OnInteractiveRegionTransformChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ScheduleInteractiveRegionUpdate();
    }

    private void ScheduleInteractiveRegionUpdate()
    {
        if (!OperatingSystem.IsWindows() || !IsVisible || _interactiveRegionUpdatePending)
        {
            return;
        }

        _interactiveRegionUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _interactiveRegionUpdatePending = false;
            if (IsVisible)
            {
                UpdateInteractiveRegion();
            }
        }, DispatcherPriority.Render);
    }

    private WindowInteractiveRegion? ResolveLiveInteractiveRegion(Rect fallback)
    {
        if (ComponentContainer.Child is Control componentContent &&
            !componentContent.IsEffectivelyVisible)
        {
            return null;
        }

        Visual target = _interactiveRegionTarget ?? ComponentContainer;
        if (!target.IsEffectivelyVisible || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            return null;
        }

        var transform = target.TransformToVisual(RootGrid);
        if (transform is null)
        {
            return null;
        }

        if (!transform.Value.TryInvert(out var clientToTarget))
        {
            return null;
        }

        var topLeft = transform.Value.Transform(new Point(0, 0));
        var topRight = transform.Value.Transform(new Point(target.Bounds.Width, 0));
        var bottomLeft = transform.Value.Transform(new Point(0, target.Bounds.Height));
        var bottomRight = transform.Value.Transform(new Point(target.Bounds.Width, target.Bounds.Height));
        var left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        var top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        var right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        var bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        var visibleBounds = new Rect(left, top, right - left, bottom - top).Intersect(fallback);
        if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
        {
            return null;
        }

        var rootBorderOwnsContour = ReferenceEquals(target, _componentRootBorder);
        var hostOwnsContour = !rootBorderOwnsContour && !ReferenceEquals(target, ComponentContainer);
        return new WindowInteractiveRegion(
            new Rect(0, 0, target.Bounds.Width, target.Bounds.Height),
            rootBorderOwnsContour || ReferenceEquals(target, ComponentContainer)
                ? _componentCornerRadius
                : 0d,
            clientToTarget,
            hostOwnsContour ? fallback : null,
            hostOwnsContour ? _componentCornerRadius : 0d);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        _isClosing = true;
        if (_interactiveRegionTarget is not null)
        {
            _interactiveRegionTarget.LayoutUpdated -= OnInteractiveRegionTargetLayoutUpdated;
            _interactiveRegionTarget.PropertyChanged -= OnInteractiveRegionTargetPropertyChanged;
            _interactiveRegionTarget = null;
        }

        SetInteractiveRegionTransform(null);
        SetComponentContentTransform(null);

        _interactiveRegionUpdatePending = false;
        _componentChromeApplyPending = false;
        _componentChromeDeferredApplyPending = false;
        if (_resizeAdorner is not null)
        {
            _resizeAdorner.ResizeStarted -= OnResizeStarted;
            _resizeAdorner.Resizing -= OnResizing;
            _resizeAdorner.ResizeCompleted -= OnResizeCompleted;
        }

        if (ComponentContainer.Child is Control componentContent)
        {
            componentContent.Loaded -= OnComponentContentLoaded;
            componentContent.PropertyChanged -= OnComponentContentPropertyChanged;
        }

        if (ComponentContainer.Child is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _regionPassthroughService.ClearInteractiveRegions(this);
        ComponentContainer.Child = null;
    }

    private void OnResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (PlacementId is null) return;

        _isResizing = true;
        var startScaling = Math.Max(0.1, RenderScaling);
        _resizeStartPhysicalSize = new Size(
            ComponentContainer.Width * startScaling,
            ComponentContainer.Height * startScaling);
        _resizeStartPosition = GetScreenPosition();

        var layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
        var layout = layoutService.Load();
        var placement = layout.ComponentPlacements.Find(
            p => string.Equals(p.PlacementId, PlacementId, StringComparison.OrdinalIgnoreCase));
        if (placement is not null)
        {
            _resizeStartWidthCells = placement.GridWidthCells ?? 1;
            _resizeStartHeightCells = placement.GridHeightCells ?? 1;
        }

        AppLogger.Info("DesktopWidget", $"Resize started. Handle={e.Handle}, PlacementId='{PlacementId}'");
    }

    private void OnResizing(object? sender, ResizeEventArgs e)
    {
        if (!_isResizing || PlacementId is null) return;

        var (newWidth, newHeight, newX, newY) = CalculateResizedBounds(
            e.Handle,
            e.Delta,
            _resizeStartPhysicalSize,
            _resizeStartPosition,
            RenderScaling);

        UpdateComponentLayout(newWidth, newHeight);

        if (e.Handle is ResizeHandlePosition.TopLeft or ResizeHandlePosition.Top or
            ResizeHandlePosition.TopRight or ResizeHandlePosition.BottomLeft or
            ResizeHandlePosition.Left)
        {
            SetScreenPosition(new PixelPoint((int)Math.Round(newX), (int)Math.Round(newY)));
        }
    }

    private void OnResizeCompleted(object? sender, ResizeCompletedEventArgs e)
    {
        if (!_isResizing || PlacementId is null)
        {
            _isResizing = false;
            return;
        }

        var layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
        var layout = layoutService.Load();
        var placement = layout.ComponentPlacements.Find(
            p => string.Equals(p.PlacementId, PlacementId, StringComparison.OrdinalIgnoreCase));
        if (placement is not null)
        {
            ApplySnappedResizePlacement(placement);
            layoutService.Save(layout);
        }

        _isResizing = false;
        RefreshDesktopLayer();
        AppLogger.Info("DesktopWidget", $"Resize completed. PlacementId='{PlacementId}'");
    }

    internal static (double width, double height, double x, double y) CalculateResizedBounds(
        ResizeHandlePosition handle,
        Point physicalDelta,
        Size startPhysicalSize,
        PixelPoint startPosition,
        double currentScaling)
    {
        var scaling = double.IsFinite(currentScaling) ? Math.Max(0.1, currentScaling) : 1d;
        var minimumPhysicalSize = 50d * scaling;
        var newPhysicalWidth = startPhysicalSize.Width;
        var newPhysicalHeight = startPhysicalSize.Height;
        var newX = (double)startPosition.X;
        var newY = (double)startPosition.Y;

        switch (handle)
        {
            case ResizeHandlePosition.TopLeft:
                newPhysicalWidth = Math.Max(minimumPhysicalSize, startPhysicalSize.Width - physicalDelta.X);
                newPhysicalHeight = Math.Max(minimumPhysicalSize, startPhysicalSize.Height - physicalDelta.Y);
                newX = startPosition.X + startPhysicalSize.Width - newPhysicalWidth;
                newY = startPosition.Y + startPhysicalSize.Height - newPhysicalHeight;
                break;
            case ResizeHandlePosition.Top:
                newPhysicalHeight = Math.Max(minimumPhysicalSize, startPhysicalSize.Height - physicalDelta.Y);
                newY = startPosition.Y + startPhysicalSize.Height - newPhysicalHeight;
                break;
            case ResizeHandlePosition.TopRight:
                newPhysicalWidth = Math.Max(minimumPhysicalSize, startPhysicalSize.Width + physicalDelta.X);
                newPhysicalHeight = Math.Max(minimumPhysicalSize, startPhysicalSize.Height - physicalDelta.Y);
                newY = startPosition.Y + startPhysicalSize.Height - newPhysicalHeight;
                break;
            case ResizeHandlePosition.Right:
                newPhysicalWidth = Math.Max(minimumPhysicalSize, startPhysicalSize.Width + physicalDelta.X);
                break;
            case ResizeHandlePosition.BottomRight:
                newPhysicalWidth = Math.Max(minimumPhysicalSize, startPhysicalSize.Width + physicalDelta.X);
                newPhysicalHeight = Math.Max(minimumPhysicalSize, startPhysicalSize.Height + physicalDelta.Y);
                break;
            case ResizeHandlePosition.Bottom:
                newPhysicalHeight = Math.Max(minimumPhysicalSize, startPhysicalSize.Height + physicalDelta.Y);
                break;
            case ResizeHandlePosition.BottomLeft:
                newPhysicalWidth = Math.Max(minimumPhysicalSize, startPhysicalSize.Width - physicalDelta.X);
                newPhysicalHeight = Math.Max(minimumPhysicalSize, startPhysicalSize.Height + physicalDelta.Y);
                newX = startPosition.X + startPhysicalSize.Width - newPhysicalWidth;
                break;
            case ResizeHandlePosition.Left:
                newPhysicalWidth = Math.Max(minimumPhysicalSize, startPhysicalSize.Width - physicalDelta.X);
                newX = startPosition.X + startPhysicalSize.Width - newPhysicalWidth;
                break;
        }

        return (
            newPhysicalWidth / scaling,
            newPhysicalHeight / scaling,
            newX,
            newY);
    }

    private void ApplySnappedResizePlacement(FusedDesktopComponentPlacementSnapshot placement)
    {
        var originalPlacement = placement.Clone();
        var currentPosition = GetScreenPosition();
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            placement.X = currentPosition.X;
            placement.Y = currentPosition.Y;
            placement.Width = ComponentContainer.Width;
            placement.Height = ComponentContainer.Height;
            return;
        }

        var scaling = Math.Max(0.1, screen.Scaling);
        var workArea = screen.WorkingArea;
        var viewportSize = new Size(workArea.Width / scaling, workArea.Height / scaling);
        var adapter = new FusedDesktopEditGridAdapter(_settingsFacade);
        if (!adapter.TryCreate(viewportSize, out var context))
        {
            placement.X = currentPosition.X;
            placement.Y = currentPosition.Y;
            placement.Width = ComponentContainer.Width;
            placement.Height = ComponentContainer.Height;
            return;
        }

        var requestedLocalOrigin = new Point(
            (currentPosition.X - workArea.X) / scaling,
            (currentPosition.Y - workArea.Y) / scaling);
        var requestedLocalWidth = ComponentContainer.Width;
        var requestedLocalHeight = ComponentContainer.Height;

        var widthCells = Math.Max(1, EstimateCellSpan(requestedLocalWidth, context.Geometry));
        var heightCells = Math.Max(1, EstimateCellSpan(requestedLocalHeight, context.Geometry));

        widthCells = Math.Max(_resizeStartWidthCells, widthCells);
        heightCells = Math.Max(_resizeStartHeightCells, heightCells);

        var localPlacement = placement.Clone();
        localPlacement.X = requestedLocalOrigin.X;
        localPlacement.Y = requestedLocalOrigin.Y;
        localPlacement.GridWidthCells = widthCells;
        localPlacement.GridHeightCells = heightCells;

        var snappedLocalPlacement = FusedDesktopPlacementMath.SnapToNearestCell(
            localPlacement,
            context.Geometry,
            requestedLocalOrigin);

        placement.Width = snappedLocalPlacement.Width;
        placement.Height = snappedLocalPlacement.Height;
        placement.GridColumn = snappedLocalPlacement.GridColumn;
        placement.GridRow = snappedLocalPlacement.GridRow;
        placement.GridWidthCells = snappedLocalPlacement.GridWidthCells;
        placement.GridHeightCells = snappedLocalPlacement.GridHeightCells;

        var snappedPosition = new PixelPoint(
            workArea.X + (int)Math.Round(snappedLocalPlacement.X * scaling),
            workArea.Y + (int)Math.Round(snappedLocalPlacement.Y * scaling));
        placement.X = snappedPosition.X;
        placement.Y = snappedPosition.Y;

        if (SetScreenPosition(snappedPosition))
        {
            var actualPosition = GetScreenPosition();
            placement.X = actualPosition.X;
            placement.Y = actualPosition.Y;
        }
        else
        {
            RestorePlacementLayout(placement, originalPlacement);
            placement.X = currentPosition.X;
            placement.Y = currentPosition.Y;
        }

        UpdateComponentLayout(placement.Width, placement.Height);
    }

    private static void RestorePlacementLayout(
        FusedDesktopComponentPlacementSnapshot target,
        FusedDesktopComponentPlacementSnapshot source)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.GridRow = source.GridRow;
        target.GridColumn = source.GridColumn;
        target.GridWidthCells = source.GridWidthCells;
        target.GridHeightCells = source.GridHeightCells;
    }

    private PixelPoint GetScreenPosition()
    {
        return OperatingSystem.IsWindows()
            ? _bottomMostService.GetScreenPosition(this)
            : Position;
    }

    private bool SetScreenPosition(PixelPoint position)
    {
        if (OperatingSystem.IsWindows())
        {
            return _bottomMostService.SetScreenPosition(this, position);
        }

        Position = position;
        return true;
    }

    private static int EstimateCellSpan(double pixelSize, DesktopGridGeometry grid)
    {
        if (!grid.IsValid || grid.CellSize <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(
            (Math.Max(1, pixelSize) + grid.CellGap) / grid.Pitch,
            MidpointRounding.AwayFromZero));
    }
}
