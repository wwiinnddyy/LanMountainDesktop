using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using LanMountainDesktop.Services;
using Avalonia.Threading;

namespace LanMountainDesktop.Views;

/// <summary>
/// 表示一个独立的组件挂载窗口。它不含有任何自己的边窗，仅仅负责包裹组件并将自身植入系统最底层。
/// </summary>
public partial class DesktopWidgetWindow : Window
{
    private readonly IWindowBottomMostService _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
    private readonly IRegionPassthroughService _regionPassthroughService = RegionPassthroughServiceFactory.GetOrCreate();

    public DesktopWidgetWindow()
    {
        InitializeComponent();
    }

    public DesktopWidgetWindow(Control componentContent) : this()
    {
        ComponentContainer.Child = componentContent;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (OperatingSystem.IsWindows())
        {
            // 通过现有的置底服务将独立的小窗口锁定到底层
            _bottomMostService.SetupBottomMost(this);
            _bottomMostService.SendToBottom(this);

            // 当窗口展示完毕且有了尺寸后，更新可交互区域，使得整个组件都能被点击
            Dispatcher.UIThread.Post(UpdateInteractiveRegion, DispatcherPriority.Render);
        }
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
        // 既然是一个完全紧贴在组件身上的小窗，它的全部都是可交互的
        _regionPassthroughService.SetInteractiveRegions(this, new List<Rect>
        {
            new(0, 0, Bounds.Width, Bounds.Height)
        });
    }
}
