using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

/// <summary>
/// 融合桌面组件库窗口 - 专门用于添加组件到系统桌面（负一屏）
/// 
/// 注意：此窗口只能添加组件到融合桌面，不能添加到阑山桌面
/// </summary>
public partial class FusedDesktopComponentLibraryWindow : Window
{
    private readonly IFusedDesktopLayoutService _layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
    private TransparentOverlayWindow? _overlayWindow;
    
    public FusedDesktopComponentLibraryWindow()
    {
        InitializeComponent();
        
        LibraryControl.AddComponentRequested += OnAddComponentRequested;
    }
    
    /// <summary>
    /// 设置透明覆盖层窗口引用
    /// </summary>
    public void SetOverlayWindow(TransparentOverlayWindow overlayWindow)
    {
        _overlayWindow = overlayWindow;
    }
    
    /// <summary>
    /// 添加组件请求处理
    /// </summary>
    private void OnAddComponentRequested(object? sender, string componentId)
    {
        if (_overlayWindow is null)
        {
            AppLogger.Warn("FusedDesktopLibrary", "Overlay window is not set.");
            return;
        }
        
        // 在屏幕中央添加组件
        var screenBounds = _overlayWindow.Bounds;
        var x = screenBounds.Width / 2 - 100; // 居中
        var y = screenBounds.Height / 2 - 100;
        
        _overlayWindow.AddComponent(componentId, x, y, 200, 200);
        
        AppLogger.Info("FusedDesktopLibrary", $"Added component {componentId} to fused desktop.");
        
        // 关闭窗口
        Close();
    }
    
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
