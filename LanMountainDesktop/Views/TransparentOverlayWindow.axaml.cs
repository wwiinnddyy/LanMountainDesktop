using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

/// <summary>
/// 透明覆盖层窗口 - 作为"负一屏"显示在 Windows 桌面上
/// 支持在系统桌面上自由摆放组件
/// </summary>
public partial class TransparentOverlayWindow : Window
{
    private readonly IFusedDesktopLayoutService _layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
    
    // 滑动状态
    private bool _isSwipeActive;
    private bool _isSwipeDirectionLocked;
    private Point _swipeStartPoint;
    private Point _swipeCurrentPoint;
    private Point _swipeLastPoint;
    private double _swipeVelocityX;
    private long _swipeLastTimestamp;
    
    // 三指/右键拖动状态
    private bool _isThreeFingerOrRightDragSwipeActive;
    private readonly HashSet<int> _activePointerIds = [];
    
    // 组件管理
    private readonly Dictionary<string, Border> _componentHosts = [];
    private readonly List<Rect> _interactiveRegions = [];
    private FusedDesktopLayoutSnapshot _layout = new();
    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    
    // 基础服务
    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();
    
    // 渲染参数
    private const double DefaultCellSize = 100;
    
    // 拖拽状态
    private bool _isDragging;
    private string? _draggingPlacementId;
    private Point _dragStartPoint;
    private Border? _draggingHost;
    
    public event EventHandler? RestoreMainWindowRequested;
    
    public TransparentOverlayWindow()
    {
        InitializeComponent();
        var facade = HostSettingsFacadeProvider.GetOrCreate();
        _weatherDataService = facade.Weather.GetWeatherInfoService();
        _timeZoneService = facade.Region.GetTimeZoneService();
        _settingsFacade = facade;
    }
    
    private readonly ISettingsFacadeService _settingsFacade;

    public void SaveLayoutAndHide()
    {
        SaveLayout();
        Hide();
        
        // Remove all components so that next time we open it builds fresh from snapshot
        if (Content is Canvas canvas)
        {
            canvas.Children.Clear();
        }
        _componentHosts.Clear();
    }
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        if (Screens.Primary is { } primaryScreen)
        {
            // 避开系统任务栏
            var workArea = primaryScreen.WorkingArea;
            var scaling = primaryScreen.Scaling;
            Position = new PixelPoint(workArea.X, workArea.Y);
            Width = workArea.Width / scaling;
            Height = workArea.Height / scaling;
        }

        if (Content is Canvas canvas)
        {
            // 保证透明区域也能被抓取事件
            canvas.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        }
        
        // 确保注册表已初始化
        EnsureRegistries();
        
        // 加载布局并渲染
        _layout = _layoutService.Load();
        RenderAllComponents();
        
        AppLogger.Info("TransparentOverlay", $"Opened with {_layout.ComponentPlacements.Count} components.");
    }
    
    /// <summary>
    /// 确保组件运行时注册表已初始化
    /// </summary>
    private void EnsureRegistries()
    {
        if (_componentRuntimeRegistry is not null) return;
        
        var pluginRuntimeService = (Application.Current as App)?.PluginRuntimeService;
        _componentRegistry = DesktopComponentRegistryFactory.Create(pluginRuntimeService);
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);
    }
    
    /// <summary>
    /// 渲染所有布局中的组件
    /// </summary>
    private void RenderAllComponents()
    {
        if (Content is not Canvas canvas) return;
        
        canvas.Children.Clear();
        _componentHosts.Clear();
        
        foreach (var placement in _layout.ComponentPlacements)
        {
            try
            {
                RenderComponentInternal(placement);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TransparentOverlay", $"Failed to render component {placement.ComponentId}", ex);
            }
        }
        
        UpdateInteractiveRegions();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        SaveLayout();
        base.OnClosed(e);
    }
    
    /// <summary>
    /// 更新可交互区域
    /// </summary>
    private void UpdateInteractiveRegions()
    {
        // 编辑模式下不再需要底层穿透功能计算，这里留空或移除
    }
    
    /// <summary>
    /// 保存布局
    /// </summary>
    private void SaveLayout()
    {
        _layoutService.Save(_layout);
    }
    
    /// <summary>
    /// 添加组件（供外部调用）
    /// </summary>
    public void AddComponent(string componentId, double x, double y, double? width = null, double? height = null)
    {
        EnsureRegistries();
        
        if (_componentRegistry == null || !_componentRegistry.TryGetDefinition(componentId, out var definition))
        {
            AppLogger.Warn("TransparentOverlay", $"Cannot add unknown component: {componentId}");
            return;
        }

        // 解析尺寸：如果未提供，则使用组件定义的最小尺寸 * 100
        var finalWidth = width ?? (definition.MinWidthCells * DefaultCellSize);
        var finalHeight = height ?? (definition.MinHeightCells * DefaultCellSize);
        
        var placementId = Guid.NewGuid().ToString("N");
        var placement = new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = placementId,
            ComponentId = componentId,
            X = x,
            Y = y,
            Width = finalWidth,
            Height = finalHeight,
            ZIndex = _layout.ComponentPlacements.Count
        };
        
        _layout.ComponentPlacements.Add(placement);
        
        // 立即渲染
        try
        {
            RenderComponentInternal(placement);
            UpdateInteractiveRegions();
            SaveLayout();
            AppLogger.Info("TransparentOverlay", $"Added component: {componentId} at ({x}, {y}) size ({finalWidth}x{finalHeight})");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TransparentOverlay", $"Failed to add component {componentId}", ex);
            _layout.ComponentPlacements.Remove(placement);
        }
    }
    
    /// <summary>
    /// 内部渲染单个组件
    /// </summary>
    private void RenderComponentInternal(FusedDesktopComponentPlacementSnapshot placement)
    {
        if (_componentRuntimeRegistry is null || !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            AppLogger.Warn("TransparentOverlay", $"Unknown component: {placement.ComponentId}");
            return;
        }
        
        var control = descriptor.CreateControl(
            DefaultCellSize,
            _timeZoneService,
            _weatherDataService,
            _recommendationInfoService,
            _calculatorDataService,
            _settingsFacade,
            placement.PlacementId);
            
        RenderComponent(placement.PlacementId, control, placement.X, placement.Y, placement.Width, placement.Height);
    }
    
    /// <summary>
    /// 移除组件
    /// </summary>
    public void RemoveComponent(string placementId)
    {
        if (_componentHosts.TryGetValue(placementId, out var host))
        {
            if (Content is Canvas canvas)
            {
                canvas.Children.Remove(host);
            }
            _componentHosts.Remove(placementId);
        }
        
        _layout.ComponentPlacements.RemoveAll(p => p.PlacementId == placementId);
        UpdateInteractiveRegions();
        SaveLayout();
    }
    
    /// <summary>
    /// 渲染组件（从外部传入控件）
    /// </summary>
    public void RenderComponent(string placementId, Control component, double x, double y, double width, double height)
    {
        var host = new Border
        {
            Tag = placementId,
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = component
        };
        
        Canvas.SetLeft(host, x);
        Canvas.SetTop(host, y);
        
        // 添加拖拽支持
        host.PointerPressed += OnComponentPointerPressed;
        host.PointerMoved += OnComponentPointerMoved;
        host.PointerReleased += OnComponentPointerReleased;
        
        // 右键上下文菜单（删除组件）
        host.ContextRequested += OnComponentContextRequested;
        
        if (Content is Canvas canvas)
        {
            canvas.Children.Add(host);
        }
        
        _componentHosts[placementId] = host;
        UpdateInteractiveRegions();
    }
    
    // 组件右键上下文菜单（删除）
    private void OnComponentContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border host || host.Tag is not string placementId) return;
        
        // 构建上下文菜单
        var deleteItem = new MenuItem
        {
            Header = "移除组件",
            Icon = new Avalonia.Controls.TextBlock { Text = "🗑" }
        };
        deleteItem.Click += (_, _) =>
        {
            RemoveComponent(placementId);
            AppLogger.Info("TransparentOverlay", $"Component removed via context menu: {placementId}");
        };
        
        var menu = new ContextMenu
        {
            Items = { deleteItem }
        };
        
        // 显示在当前控件上
        menu.Open(host);
        e.Handled = true;
    }
    
    // 组件拖拽处理
    private void OnComponentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border host || host.Tag is not string placementId) return;
        
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        
        _isDragging = true;
        _draggingPlacementId = placementId;
        _draggingHost = host;
        _dragStartPoint = e.GetPosition(this);
        
        e.Pointer.Capture(host);
        e.Handled = true;
    }
    
    private void OnComponentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _draggingHost is null) return;
        
        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _dragStartPoint.X;
        var deltaY = currentPoint.Y - _dragStartPoint.Y;
        
        var currentX = Canvas.GetLeft(_draggingHost);
        var currentY = Canvas.GetTop(_draggingHost);
        
        Canvas.SetLeft(_draggingHost, currentX + deltaX);
        Canvas.SetTop(_draggingHost, currentY + deltaY);
        
        _dragStartPoint = currentPoint;
        e.Handled = true;
    }
    
    private void OnComponentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || _draggingHost is null || _draggingPlacementId is null)
        {
            _isDragging = false;
            return;
        }
        
        // 更新布局中的位置
        var placement = _layout.ComponentPlacements.Find(p => p.PlacementId == _draggingPlacementId);
        if (placement is not null)
        {
            placement.X = Canvas.GetLeft(_draggingHost);
            placement.Y = Canvas.GetTop(_draggingHost);
        }
        
        UpdateInteractiveRegions();
        SaveLayout();
        
        _isDragging = false;
        _draggingPlacementId = null;
        _draggingHost = null;
        
        e.Pointer.Capture(null);
        e.Handled = true;
    }
    
    // 三指滑动处理
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
            e.Handled = true;
        }
        else
        {
            base.OnPointerPressed(e);
        }
    }
    
    protected override void OnPointerMoved(PointerEventArgs e)
    {
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
        
        if (_isSwipeActive)
        {
            if (EndSwipeInteraction(e.Pointer))
            {
                e.Handled = true;
                return;
            }
        }
        
        base.OnPointerReleased(e);
    }
    
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        var pointerId = e.Pointer?.Id ?? 0;
        _activePointerIds.Remove(pointerId);
        
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
    
    private void UpdateSwipeVelocity(Point currentPoint)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_swipeLastTimestamp, now).TotalSeconds;
        
        if (elapsed > 0)
        {
            var dx = currentPoint.X - _swipeLastPoint.X;
            _swipeVelocityX = dx / elapsed;
        }
        
        _swipeLastPoint = currentPoint;
        _swipeLastTimestamp = now;
    }
    
    private void CancelSwipeInteraction(IPointer? pointer)
    {
        if (!_isSwipeActive) return;
        
        if (pointer?.Captured == this)
        {
            pointer?.Capture(null);
        }
        
        _isSwipeActive = false;
        _isSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        _swipeVelocityX = 0;
        _swipeLastTimestamp = 0;
    }
    
    private bool EndSwipeInteraction(IPointer? pointer)
    {
        if (!_isSwipeActive) return false;
        
        var wasDirectionLocked = _isSwipeDirectionLocked;
        var wasThreeFingerOrRightDrag = _isThreeFingerOrRightDragSwipeActive;
        
        _isSwipeActive = false;
        _isSwipeDirectionLocked = false;
        _isThreeFingerOrRightDragSwipeActive = false;
        _activePointerIds.Clear();
        
        if (pointer?.Captured == this)
        {
            pointer?.Capture(null);
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
        var distanceThreshold = Math.Max(48, this.Bounds.Width * 0.14);
        var velocityThreshold = Math.Max(860, this.Bounds.Width * 1.08);
        var hasDistanceIntent = absDeltaX >= distanceThreshold && absDeltaX > Math.Abs(deltaY) * 1.05;
        var hasVelocityIntent = Math.Abs(_swipeVelocityX) >= velocityThreshold;
        
        // 向左滑动回到第一页
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
