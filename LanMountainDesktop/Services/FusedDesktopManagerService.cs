using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Services;

/// <summary>
/// 融合桌面中央管理器服务接口
/// </summary>
public interface IFusedDesktopManagerService
{
    void Initialize();
    void EnterEditMode();
    void ExitEditMode();
    void ReloadWidgets();
}

/// <summary>
/// 融合桌面中央管理器服务实现。用于管理常态下的各个小窗口实体。
/// </summary>
internal sealed class FusedDesktopManagerService : IFusedDesktopManagerService
{
    private readonly IFusedDesktopLayoutService _layoutService;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly Dictionary<string, DesktopWidgetWindow> _widgetWindows = [];
    
    // 基础服务依赖
    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();
    
    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private bool _isEditMode;

    private const double DefaultCellSize = 100;

    public FusedDesktopManagerService(
        IFusedDesktopLayoutService layoutService, 
        ISettingsFacadeService settingsFacade)
    {
        _layoutService = layoutService;
        _settingsFacade = settingsFacade;
        
        _weatherDataService = _settingsFacade.Weather.GetWeatherInfoService();
        _timeZoneService = _settingsFacade.Region.GetTimeZoneService();
    }

    public void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;
        
        // 检查融合桌面功能是否启用
        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        if (!appSnapshot.EnableFusedDesktop)
        {
            AppLogger.Info("FusedDesktop", "Fused desktop is disabled. Skipping initialization.");
            return;
        }
        
        EnsureRegistries();
        ReloadWidgets();
    }

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

    public void EnterEditMode()
    {
        if (_isEditMode) return;
        _isEditMode = true;

        // 【修复问题3】不再隐藏窗口，而是将窗口内容转移到编辑模式覆盖层
        // 这样可以保持组件的运行状态（动画、输入等）
        foreach (var window in _widgetWindows.Values)
        {
            window.Hide();
        }
    }

    public void ExitEditMode()
    {
        if (!_isEditMode) return;
        _isEditMode = false;

        // 编辑完成，重新加载布局（可能已发生更改）并显示
        ReloadWidgets();
    }

    public void ReloadWidgets()
    {
        if (_isEditMode) return; // 编辑模式下不渲染小窗口
        
        var layout = _layoutService.Load();
        var existingIds = new HashSet<string>(_widgetWindows.Keys);
        
        foreach (var placement in layout.ComponentPlacements)
        {
            existingIds.Remove(placement.PlacementId);
            
            if (_widgetWindows.TryGetValue(placement.PlacementId, out var existingWindow))
            {
                // 编辑完成后，已有小窗也要同步尺寸，否则会出现“布局已保存但窗口没变”的假象。
                existingWindow.Position = new Avalonia.PixelPoint((int)placement.X, (int)placement.Y);
                existingWindow.UpdateComponentLayout(placement.Width, placement.Height);
                if (existingWindow.IsVisible == false)
                {
                    existingWindow.Show();
                }
            }
            else
            {
                // 新组件，生成窗口
                try
                {
                    var window = CreateWidgetWindow(placement);
                    if (window != null)
                    {
                        _widgetWindows[placement.PlacementId] = window;
                        window.Show();
                        window.Position = new Avalonia.PixelPoint((int)placement.X, (int)placement.Y);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("FusedDesktopMgr", $"Failed to render tiny window for {placement.ComponentId}", ex);
                }
            }
        }
        
        // 移除被删除的组件
        foreach (var id in existingIds)
        {
            if (_widgetWindows.Remove(id, out var windowToRemove))
            {
                windowToRemove.Close();
            }
        }
    }

    private DesktopWidgetWindow? CreateWidgetWindow(FusedDesktopComponentPlacementSnapshot placement)
    {
        EnsureRegistries();
        if (_componentRuntimeRegistry is null || !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            AppLogger.Warn("FusedDesktopMgr", $"Unknown component: {placement.ComponentId}");
            return null;
        }
        
        var control = descriptor.CreateControl(
            DefaultCellSize,
            _timeZoneService,
            _weatherDataService,
            _recommendationInfoService,
            _calculatorDataService,
            _settingsFacade,
            placement.PlacementId);
            
        // 将组件包装到一个具有准确宽高的容器内（如果组件自身没有设置宽度）
        control.Width = placement.Width;
        control.Height = placement.Height;

        var window = new DesktopWidgetWindow(control);
        return window;
    }
}

/// <summary>
/// 工厂
/// </summary>
public static class FusedDesktopManagerServiceFactory
{
    private static IFusedDesktopManagerService? _instance;
    private static readonly object _lock = new();
    
    public static IFusedDesktopManagerService GetOrCreate()
    {
        if (_instance is not null) return _instance;
        
        lock (_lock)
        {
            var layoutService = FusedDesktopLayoutServiceProvider.GetOrCreate();
            var settings = HostSettingsFacadeProvider.GetOrCreate();
            _instance ??= new FusedDesktopManagerService(layoutService, settings);
            return _instance;
        }
    }
}
