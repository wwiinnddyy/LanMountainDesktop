using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using Avalonia.Controls.ApplicationLifetimes;

namespace LanMountainDesktop.Views;

/// <summary>
/// 融合桌面组件库窗口 - 专门用于添加组件到系统桌面（负一屏）
/// 
/// 注意：此窗口只能添加组件到融合桌面，不能添加到阑山桌面
/// </summary>
public partial class FusedDesktopComponentLibraryWindow : Window
{
    private readonly IFusedDesktopLayoutService _layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private TransparentOverlayWindow? _overlayWindow;
    
    // 与 TransparentOverlayWindow 保持一致的默认 cellSize
    private const double DefaultCellSize = 100;
    
    public FusedDesktopComponentLibraryWindow()
    {
        InitializeComponent();
        
        LibraryControl.AddComponentRequested += OnAddComponentRequested;
        
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        mainWindow?.RegisterFusedLibraryWindow(this);
    }
    
    /// <summary>
    /// 设置透明覆盖层窗口引用
    /// </summary>
    public void SetOverlayWindow(TransparentOverlayWindow overlayWindow)
    {
        _overlayWindow = overlayWindow;
    }
    
    /// <summary>
    /// 添加组件请求处理 - 将组件放置在屏幕（覆盖层画布）中央
    /// </summary>
    private void OnAddComponentRequested(object? sender, string componentId)
    {
        if (_overlayWindow is null)
        {
            AppLogger.Warn("FusedDesktopLibrary", "Overlay window is not set.");
            return;
        }
        
        // 计算组件的像素尺寸
        var (componentWidth, componentHeight) = ResolveComponentSize(componentId);
        
        // 取覆盖层画布的中心点，减去组件半尺寸，使组件出现在屏幕正中央
        var overlayBounds = _overlayWindow.Bounds;
        var centerX = overlayBounds.Width / 2.0 - componentWidth / 2.0;
        var centerY = overlayBounds.Height / 2.0 - componentHeight / 2.0;
        
        // 边界保护：确保组件不超出屏幕边界
        centerX = Math.Max(0, Math.Min(centerX, overlayBounds.Width - componentWidth));
        centerY = Math.Max(0, Math.Min(centerY, overlayBounds.Height - componentHeight));
        
        _overlayWindow.AddComponent(componentId, centerX, centerY, componentWidth, componentHeight);
        
        AppLogger.Info("FusedDesktopLibrary",
            $"Added component '{componentId}' at center ({centerX:F0}, {centerY:F0}) size ({componentWidth}x{componentHeight}).");
        
        // 关闭窗口
        Close();
    }
    
    /// <summary>
    /// 解析组件的默认像素尺寸（基于组件定义的 MinCells * DefaultCellSize）
    /// </summary>
    private (double Width, double Height) ResolveComponentSize(string componentId)
    {
        try
        {
            var pluginRuntimeService = (Application.Current as App)?.PluginRuntimeService;
            var registry = DesktopComponentRegistryFactory.Create(pluginRuntimeService);
            if (registry.TryGetDefinition(componentId, out var definition))
            {
                var w = Math.Max(1, definition.MinWidthCells) * DefaultCellSize;
                var h = Math.Max(1, definition.MinHeightCells) * DefaultCellSize;
                return (w, h);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FusedDesktopLibrary", $"Failed to resolve component size for '{componentId}'.", ex);
        }
        
        // 回退为 2×2 格子的默认尺寸
        return (DefaultCellSize * 2, DefaultCellSize * 2);
    }
    
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        mainWindow?.UnregisterFusedLibraryWindow(this);
    }
}
