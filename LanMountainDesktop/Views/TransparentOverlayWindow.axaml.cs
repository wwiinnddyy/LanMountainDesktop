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
    private double _currentDesktopCellSize;
    
    // 拖拽与缩放状态
    private bool _isDragging;
    private bool _isResizing;
    private string? _interactionPlacementId;
    private Point _interactionStartPoint;
    private double _interactionOriginalX;
    private double _interactionOriginalY;
    private double _interactionOriginalWidth;
    private double _interactionOriginalHeight;
    private Border? _interactionHost;
    
    // 选中状态
    private Border? _selectedHost;
    
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
            
            // 基于设置计算单元格尺寸
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            var shortCells = Math.Clamp(appSnapshot.GridShortSideCells > 0 ? appSnapshot.GridShortSideCells : 12, 6, 96);
            _currentDesktopCellSize = Height / shortCells;
        }
        else
        {
            _currentDesktopCellSize = DefaultCellSize;
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
        _selectedHost = null;
        
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

        var finalWidth = width ?? (definition.MinWidthCells * _currentDesktopCellSize);
        var finalHeight = height ?? (definition.MinHeightCells * _currentDesktopCellSize);
        
        // 对齐网格
        x = Math.Round(x / _currentDesktopCellSize) * _currentDesktopCellSize;
        y = Math.Round(y / _currentDesktopCellSize) * _currentDesktopCellSize;
        finalWidth = Math.Round(finalWidth / _currentDesktopCellSize) * _currentDesktopCellSize;
        finalHeight = Math.Round(finalHeight / _currentDesktopCellSize) * _currentDesktopCellSize;
        
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
            _currentDesktopCellSize,
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
        var grid = new Grid();
        grid.Children.Add(component);
        
        var resizeHandle = new Border
        {
            Width = 24,
            Height = 24,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3B82F6")),
            CornerRadius = new Avalonia.CornerRadius(12),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Margin = new Avalonia.Thickness(0, 0, -12, -12),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.BottomRightCorner),
            Tag = "desktop-component-resize-handle",
            IsVisible = false
        };
        grid.Children.Add(resizeHandle);
        
        var host = new Border
        {
            Tag = placementId,
            Width = width,
            Height = height,
            Background = Avalonia.Media.Brushes.Transparent,
            CornerRadius = new Avalonia.CornerRadius(12),
            ClipToBounds = false, // 允许把手溢出
            BorderBrush = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(3),
            Child = grid,
            Classes = { "desktop-component-host" }
        };
        
        Canvas.SetLeft(host, x);
        Canvas.SetTop(host, y);
        
        host.PointerPressed += OnComponentPointerPressed;
        host.PointerMoved += OnInteractionPointerMoved;
        host.PointerReleased += OnInteractionPointerReleased;
        
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
    
    // 取消选中
    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        DeselectComponent();
    }
    
    // 选中组件
    private void SelectComponent(Border host)
    {
        if (_selectedHost == host) return;
        DeselectComponent();
        
        _selectedHost = host;
        
        // 渲染选中边框和把手
        host.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3B82F6"));
        host.Classes.Add("desktop-component-host-selected");
        
        if (host.Child is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Control c && c.Tag is string tg && tg == "desktop-component-resize-handle")
                {
                    c.IsVisible = true;
                    break;
                }
            }
        }
    }
    
    private void DeselectComponent()
    {
        if (_selectedHost != null)
        {
            _selectedHost.BorderBrush = Avalonia.Media.Brushes.Transparent;
            _selectedHost.Classes.Remove("desktop-component-host-selected");
            
            if (_selectedHost.Child is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Control c && c.Tag is string tg && tg == "desktop-component-resize-handle")
                    {
                        c.IsVisible = false;
                        break;
                    }
                }
            }
        }
        _selectedHost = null;
    }
    
    // 组件拖拽与缩放处理
    private void OnComponentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border host || host.Tag is not string placementId) return;
        
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        
        SelectComponent(host);
        
        _interactionPlacementId = placementId;
        _interactionHost = host;
        _interactionStartPoint = e.GetPosition(this);
        
        // 这里必须用未吸附的原始屏幕位置计算 delta
        _interactionOriginalX = Canvas.GetLeft(host);
        _interactionOriginalY = Canvas.GetTop(host);
        _interactionOriginalWidth = host.Width;
        _interactionOriginalHeight = host.Height;
        
        if (e.Source is Control sourceControl && sourceControl.Tag is string tag && tag == "desktop-component-resize-handle")
        {
            _isResizing = true;
            _isDragging = false;
        }
        else
        {
            _isDragging = true;
            _isResizing = false;
        }
        
        e.Pointer.Capture(host);
        e.Handled = true;
    }
    
    private void OnInteractionPointerMoved(object? sender, PointerEventArgs e)
    {
        if ((!_isDragging && !_isResizing) || _interactionHost is null) return;
        
        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _interactionStartPoint.X;
        var deltaY = currentPoint.Y - _interactionStartPoint.Y;
        
        if (_isDragging)
        {
            var rawX = _interactionOriginalX + deltaX;
            var rawY = _interactionOriginalY + deltaY;
            
            var snapX = Math.Round(rawX / _currentDesktopCellSize) * _currentDesktopCellSize;
            var snapY = Math.Round(rawY / _currentDesktopCellSize) * _currentDesktopCellSize;
            
            Canvas.SetLeft(_interactionHost, snapX);
            Canvas.SetTop(_interactionHost, snapY);
        }
        else if (_isResizing)
        {
            var rawWidth = _interactionOriginalWidth + deltaX;
            var rawHeight = _interactionOriginalHeight + deltaY;
            
            var snapWidth = Math.Round(rawWidth / _currentDesktopCellSize) * _currentDesktopCellSize;
            var snapHeight = Math.Round(rawHeight / _currentDesktopCellSize) * _currentDesktopCellSize;
            
            // 防溢出与极小值保护
            snapWidth = Math.Max(_currentDesktopCellSize, snapWidth);
            snapHeight = Math.Max(_currentDesktopCellSize, snapHeight);
            
            _interactionHost.Width = snapWidth;
            _interactionHost.Height = snapHeight;
        }
        
        e.Handled = true;
    }
    
    private void OnInteractionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if ((!_isDragging && !_isResizing) || _interactionHost is null || _interactionPlacementId is null)
        {
            _isDragging = false;
            _isResizing = false;
            return;
        }
        
        // 更新布局中的位置与尺寸
        var placement = _layout.ComponentPlacements.Find(p => p.PlacementId == _interactionPlacementId);
        if (placement is not null)
        {
            placement.X = Canvas.GetLeft(_interactionHost);
            placement.Y = Canvas.GetTop(_interactionHost);
            placement.Width = _interactionHost.Width;
            placement.Height = _interactionHost.Height;
        }
        
        UpdateInteractiveRegions();
        SaveLayout();
        
        _isDragging = false;
        _isResizing = false;
        _interactionPlacementId = null;
        _interactionHost = null;
        
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
