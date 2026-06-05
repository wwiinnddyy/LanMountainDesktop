using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopEditing;
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
    void AddComponent(string componentId, Window? referenceWindow = null);
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

    public void AddComponent(string componentId, Window? referenceWindow = null)
    {
        EnsureRegistries();
        if (_componentRuntimeRegistry is null || !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            AppLogger.Warn("FusedDesktopMgr", $"Unknown component: {componentId}");
            return;
        }

        var widthCells = Math.Max(1, descriptor.Definition.MinWidthCells);
        var heightCells = Math.Max(1, descriptor.Definition.MinHeightCells);
        var placement = CreateCenteredPlacement(
            Guid.NewGuid().ToString("N"),
            componentId,
            widthCells,
            heightCells,
            referenceWindow);

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

        AppLogger.Info(
            "FusedDesktopMgr",
            $"Added component '{componentId}' with placement '{placement.PlacementId}' at grid {placement.GridColumn},{placement.GridRow}.");
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

        var cellSize = ResolveCellSize(placement);
        var control = descriptor.CreateControl(
            cellSize,
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

    private FusedDesktopComponentPlacementSnapshot CreateCenteredPlacement(
        string placementId,
        string componentId,
        int widthCells,
        int heightCells,
        Window? referenceWindow)
    {
        var screen = ResolveTargetScreen(referenceWindow);
        if (screen is null)
        {
            var fallbackWidth = widthCells * DefaultCellSize;
            var fallbackHeight = heightCells * DefaultCellSize;
            return new FusedDesktopComponentPlacementSnapshot
            {
                PlacementId = placementId,
                ComponentId = componentId,
                X = 0,
                Y = 0,
                Width = fallbackWidth,
                Height = fallbackHeight,
                GridColumn = 0,
                GridRow = 0,
                GridWidthCells = widthCells,
                GridHeightCells = heightCells
            };
        }

        var workArea = screen.WorkingArea;
        var scaling = Math.Max(0.1, screen.Scaling);
        var viewportSize = GetScreenViewportSize(screen);
        var adapter = new FusedDesktopEditGridAdapter(_settingsFacade);
        if (!adapter.TryCreate(viewportSize, out var context))
        {
            var fallbackWidth = widthCells * DefaultCellSize;
            var fallbackHeight = heightCells * DefaultCellSize;
            return new FusedDesktopComponentPlacementSnapshot
            {
                PlacementId = placementId,
                ComponentId = componentId,
                X = workArea.X + Math.Max(0, (workArea.Width - fallbackWidth * scaling) / 2),
                Y = workArea.Y + Math.Max(0, (workArea.Height - fallbackHeight * scaling) / 2),
                Width = fallbackWidth,
                Height = fallbackHeight,
                GridColumn = 0,
                GridRow = 0,
                GridWidthCells = widthCells,
                GridHeightCells = heightCells
            };
        }

        var localPlacement = FusedDesktopPlacementMath.CreateCenteredPlacement(
            placementId,
            componentId,
            context.Geometry,
            widthCells,
            heightCells);
        localPlacement.X = workArea.X + localPlacement.X * scaling;
        localPlacement.Y = workArea.Y + localPlacement.Y * scaling;
        return localPlacement;
    }

    private Screen? ResolveTargetScreen(Window? referenceWindow)
    {
        if (referenceWindow is not null)
        {
            var referenceScreen = referenceWindow.Screens.ScreenFromWindow(referenceWindow);
            if (referenceScreen is not null)
            {
                return referenceScreen;
            }
        }

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return mainWindow?.Screens.Primary;
    }

    private static Size GetScreenViewportSize(Screen screen)
    {
        var scaling = Math.Max(0.1, screen.Scaling);
        var workArea = screen.WorkingArea;
        return new Size(workArea.Width / scaling, workArea.Height / scaling);
    }

    private double ResolveCellSize(FusedDesktopComponentPlacementSnapshot placement)
    {
        if (TryResolveScreenForPlacement(placement, out var screen))
        {
            var adapter = new FusedDesktopEditGridAdapter(_settingsFacade);
            if (adapter.TryCreate(GetScreenViewportSize(screen), out var context))
            {
                return Math.Max(1, context.Metrics.CellSize);
            }
        }

        if (placement.GridWidthCells is > 0 && placement.Width > 0)
        {
            return Math.Max(1, placement.Width / placement.GridWidthCells.Value);
        }

        if (placement.GridHeightCells is > 0 && placement.Height > 0)
        {
            return Math.Max(1, placement.Height / placement.GridHeightCells.Value);
        }

        return DefaultCellSize;
    }

    private bool TryResolveScreenForPlacement(
        FusedDesktopComponentPlacementSnapshot placement,
        out Screen screen)
    {
        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow is not null)
        {
            foreach (var candidate in mainWindow.Screens.All)
            {
                if (candidate.WorkingArea.Contains(new PixelPoint((int)placement.X, (int)placement.Y)))
                {
                    screen = candidate;
                    return true;
                }
            }

            if (mainWindow.Screens.Primary is not null)
            {
                screen = mainWindow.Screens.Primary;
                return true;
            }
        }

        screen = null!;
        return false;
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
