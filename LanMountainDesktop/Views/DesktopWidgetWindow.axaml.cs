using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private Point _dragStartPointerPosition;

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

    public DesktopWidgetWindow(Control componentContent, string? placementId = null) : this()
    {
        PlacementId = placementId;
        ComponentContainer.Child = componentContent;
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
        }
        else
        {
            Cursor = null;
        }

        AppLogger.Info("DesktopWidgetWindow", $"Edit mode set to {editMode}. PlacementId='{PlacementId}'.");
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

        if (OperatingSystem.IsWindows() && IsVisible)
        {
            Dispatcher.UIThread.Post(UpdateInteractiveRegion, DispatcherPriority.Render);
        }
    }

    public void RefreshDesktopLayer()
    {
        if (!OperatingSystem.IsWindows() || !IsVisible)
        {
            return;
        }

        _bottomMostService.SendToBottom(this);
        Dispatcher.UIThread.Post(UpdateInteractiveRegion, DispatcherPriority.Render);
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
            var currentPointer = e.GetPosition(this);
            var delta = currentPointer - _dragStartPointerPosition;

            Position = new PixelPoint(
                _dragStartWindowPosition.X + (int)delta.X,
                _dragStartWindowPosition.Y + (int)delta.Y);

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
        _dragStartWindowPosition = Position;
        _dragStartPointerPosition = e.GetPosition(this);
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
        if (!TrySnapToCurrentScreenGrid(placement, Position, out var snappedPosition) ||
            !snappedPosition.HasValue)
        {
            placement.X = Position.X;
            placement.Y = Position.Y;
            return;
        }

        placement.X = snappedPosition.Value.X;
        placement.Y = snappedPosition.Value.Y;
        Position = snappedPosition.Value;
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
        _regionPassthroughService.SetInteractiveRegions(this, new List<Rect>
        {
            new(0, 0, Bounds.Width, Bounds.Height)
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ComponentContainer.Child is IDisposable disposable)
        {
            disposable.Dispose();
        }
        ComponentContainer.Child = null;
        base.OnClosing(e);
    }
}
