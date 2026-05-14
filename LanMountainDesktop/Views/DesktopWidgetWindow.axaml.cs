using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class DesktopWidgetWindow : Window
{
    private readonly IWindowBottomMostService _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
    private readonly IRegionPassthroughService _regionPassthroughService = RegionPassthroughServiceFactory.GetOrCreate();

    public DesktopWidgetWindow()
    {
        InitializeComponent();
        AppLogger.Info("DesktopWidgetWindow", "Initialized. WindowRole=DesktopSurface.");

        if (OperatingSystem.IsWindows())
        {
            _bottomMostService.SetupBottomMost(this);
        }
    }

    public DesktopWidgetWindow(Control componentContent) : this()
    {
        ComponentContainer.Child = componentContent;
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
