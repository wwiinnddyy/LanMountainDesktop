using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Services;

public interface IFusedDesktopManagerService
{
    void Initialize();
    void ReloadWidgets();
    void Shutdown();
    void AddComponent(string componentId);
    void RemoveComponent(string placementId);
    void EnterEditMode();
    void ExitEditMode();
    bool IsEditMode { get; }
}

internal sealed class FusedDesktopManagerService : IFusedDesktopManagerService
{
    private readonly IFusedDesktopLayoutService _layoutService;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly Dictionary<string, DesktopWidgetWindow> _widgetWindows = [];

    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private bool _isEditMode;

    private const double DefaultCellSize = 100;
    private const double DefaultComponentWidth = 200;
    private const double DefaultComponentHeight = 200;

    public bool IsEditMode => _isEditMode;

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

        foreach (var window in _widgetWindows.Values)
        {
            window.SetEditMode(true);
        }

        AppLogger.Info("FusedDesktop", "Entered edit mode.");
    }

    public void ExitEditMode()
    {
        if (!_isEditMode) return;
        _isEditMode = false;

        foreach (var window in _widgetWindows.Values)
        {
            window.SetEditMode(false);
        }

        AppLogger.Info("FusedDesktop", "Exited edit mode.");
    }

    public void AddComponent(string componentId)
    {
        EnsureRegistries();
        if (_componentRuntimeRegistry is null || !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            AppLogger.Warn("FusedDesktopMgr", $"Unknown component: {componentId}");
            return;
        }

        var placement = new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = Guid.NewGuid().ToString("N"),
            ComponentId = componentId,
            Width = DefaultComponentWidth,
            Height = DefaultComponentHeight
        };

        var screen = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Screens.Primary;
        if (screen is not null)
        {
            var scaling = screen.Scaling;
            var workArea = screen.WorkingArea;
            placement.X = (workArea.Width / scaling - placement.Width) / 2;
            placement.Y = (workArea.Height / scaling - placement.Height) / 2;
        }

        _layoutService.AddComponentPlacement(placement);

        try
        {
            var window = CreateWidgetWindow(placement);
            if (window != null)
            {
                _widgetWindows[placement.PlacementId] = window;
                if (_isEditMode)
                {
                    window.SetEditMode(true);
                }

                window.Show();
                window.Position = new PixelPoint((int)placement.X, (int)placement.Y);
                window.RefreshDesktopLayer();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FusedDesktopMgr", $"Failed to create widget window for {componentId}", ex);
            _layoutService.RemoveComponentPlacement(placement.PlacementId);
        }

        AppLogger.Info("FusedDesktopMgr", $"Added component '{componentId}' with placement '{placement.PlacementId}'.");
    }

    public void RemoveComponent(string placementId)
    {
        if (_widgetWindows.Remove(placementId, out var windowToRemove))
        {
            windowToRemove.Close();
        }

        _layoutService.RemoveComponentPlacement(placementId);
        AppLogger.Info("FusedDesktopMgr", $"Removed component placement '{placementId}'.");
    }

    public void ReloadWidgets()
    {
        var layout = _layoutService.Load();
        var existingIds = new HashSet<string>(_widgetWindows.Keys);

        foreach (var placement in layout.ComponentPlacements)
        {
            existingIds.Remove(placement.PlacementId);

            if (_widgetWindows.TryGetValue(placement.PlacementId, out var existingWindow))
            {
                existingWindow.Position = new PixelPoint((int)placement.X, (int)placement.Y);
                existingWindow.UpdateComponentLayout(placement.Width, placement.Height);
                if (existingWindow.IsVisible == false)
                {
                    existingWindow.Show();
                }

                existingWindow.RefreshDesktopLayer();
            }
            else
            {
                try
                {
                    var window = CreateWidgetWindow(placement);
                    if (window != null)
                    {
                        _widgetWindows[placement.PlacementId] = window;
                        if (_isEditMode)
                        {
                            window.SetEditMode(true);
                        }

                        window.Show();
                        window.Position = new PixelPoint((int)placement.X, (int)placement.Y);
                        window.RefreshDesktopLayer();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("FusedDesktopMgr", $"Failed to render tiny window for {placement.ComponentId}", ex);
                }
            }
        }

        foreach (var id in existingIds)
        {
            if (_widgetWindows.Remove(id, out var windowToRemove))
            {
                windowToRemove.Close();
            }
        }
    }

    public void Shutdown()
    {
        _isEditMode = false;
        foreach (var window in _widgetWindows.Values)
        {
            window.Close();
        }

        _widgetWindows.Clear();
        AppLogger.Info("FusedDesktop", "Fused desktop manager shut down.");
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

        control.Width = placement.Width;
        control.Height = placement.Height;

        var window = new DesktopWidgetWindow(control, placement.PlacementId);
        return window;
    }
}

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
