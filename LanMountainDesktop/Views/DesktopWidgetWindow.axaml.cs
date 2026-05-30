using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class DesktopWidgetWindow : Window
{
    private readonly IWindowBottomMostService _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
    private readonly IRegionPassthroughService _regionPassthroughService = RegionPassthroughServiceFactory.GetOrCreate();

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
                placement.X = Position.X;
                placement.Y = Position.Y;
                layoutService.Save(layout);
            }
        }

        RefreshDesktopLayer();
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
