using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Host.Abstractions;
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

internal readonly record struct FusedDesktopScreenWorkArea(PixelRect WorkingArea, double Scaling);

internal sealed class FusedDesktopManagerService : IFusedDesktopManagerService
{
    private readonly IFusedDesktopLayoutService _layoutService;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IWindowBottomMostService _bottomMostService;
    private readonly IAppearanceThemeService _appearanceThemeService;
    private readonly Dictionary<string, DesktopWidgetWindow> _widgetWindows = [];
    private readonly HashSet<string> _positioningFailures = new(StringComparer.OrdinalIgnoreCase);

    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private Screens? _screens;
    private bool _isEditMode;
    private bool _isAppearanceSubscribed;
    private int _screenTopologyUpdatePending;

    private const double DefaultCellSize = 100;

    public bool IsEditMode => _isEditMode;

    public FusedDesktopManagerService(
        IFusedDesktopLayoutService layoutService,
        ISettingsFacadeService settingsFacade)
    {
        _layoutService = layoutService;
        _settingsFacade = settingsFacade;
        _bottomMostService = WindowBottomMostServiceFactory.GetOrCreate();
        _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();

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

        EnsureAppearanceSubscription();
        EnsureRegistries();
        ReloadWidgets();
    }

    private void EnsureAppearanceSubscription()
    {
        if (_isAppearanceSubscribed)
        {
            return;
        }

        _appearanceThemeService.Changed += OnAppearanceThemeChanged;
        _isAppearanceSubscribed = true;
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

    private void EnsureScreenTopologySubscription(DesktopWidgetWindow window)
    {
        var screens = window.Screens;
        if (ReferenceEquals(_screens, screens))
        {
            return;
        }

        if (_screens is not null)
        {
            _screens.Changed -= OnScreensChanged;
        }

        _screens = screens;
        _screens.Changed += OnScreensChanged;
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Interlocked.Exchange(ref _screenTopologyUpdatePending, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                RevalidateWidgetsForScreenTopology();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("FusedDesktopMgr", "Failed to revalidate widgets after screen topology changed.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _screenTopologyUpdatePending, 0);
            }
        }, DispatcherPriority.Background);
    }

    private void RevalidateWidgetsForScreenTopology()
    {
        if (_screens is null || _widgetWindows.Count == 0)
        {
            return;
        }

        var layout = _layoutService.Load();
        var placements = new Dictionary<string, FusedDesktopComponentPlacementSnapshot>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var placement in layout.ComponentPlacements)
        {
            placements[placement.PlacementId] = placement;
        }

        var appearanceSnapshot = _appearanceThemeService.GetCurrent();
        var layoutChanged = false;
        foreach (var (placementId, window) in _widgetWindows)
        {
            if (!placements.TryGetValue(placementId, out var placement))
            {
                continue;
            }

            var logicalSize = new Size(
                placement.Width > 0 ? placement.Width : Math.Max(1d, window.Bounds.Width),
                placement.Height > 0 ? placement.Height : Math.Max(1d, window.Bounds.Height));
            var currentPosition = _bottomMostService.GetScreenPosition(window);
            if (_positioningFailures.Contains(placementId))
            {
                var persistedPosition = new PixelPoint((int)placement.X, (int)placement.Y);
                var retryPosition = CoerceToValidWorkingArea(
                    persistedPosition,
                    logicalSize,
                    _screens.All);
                if (!_bottomMostService.SetScreenPosition(
                        window,
                        retryPosition,
                        queueOnFailure: true))
                {
                    UpdateWidgetChrome(window, placement, appearanceSnapshot);
                    continue;
                }

                _positioningFailures.Remove(placementId);
                currentPosition = _bottomMostService.GetScreenPosition(window);
            }

            var validPosition = CoerceToValidWorkingArea(
                currentPosition,
                logicalSize,
                _screens.All);

            var finalPosition = currentPosition;
            if (validPosition != currentPosition)
            {
                if (_bottomMostService.SetScreenPosition(
                        window,
                        validPosition,
                        queueOnFailure: true))
                {
                    _positioningFailures.Remove(placementId);
                    finalPosition = _bottomMostService.GetScreenPosition(window);
                }
                else
                {
                    _positioningFailures.Add(placementId);
                }
            }

            var finalValidPosition = CoerceToValidWorkingArea(
                finalPosition,
                logicalSize,
                _screens.All);
            if (finalPosition == finalValidPosition &&
                (placement.X != finalPosition.X || placement.Y != finalPosition.Y))
            {
                placement.X = finalPosition.X;
                placement.Y = finalPosition.Y;
                layoutChanged = true;
            }

            UpdateWidgetChrome(window, placement, appearanceSnapshot);
        }

        if (layoutChanged)
        {
            _layoutService.Save(layout);
        }
    }

    private static PixelPoint CoerceToValidWorkingArea(
        PixelPoint position,
        Size logicalSize,
        IReadOnlyList<Screen> screens)
    {
        if (screens.Count == 0)
        {
            return position;
        }

        var workAreas = new FusedDesktopScreenWorkArea[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            workAreas[i] = new FusedDesktopScreenWorkArea(
                screens[i].WorkingArea,
                screens[i].Scaling);
        }

        return CoerceToValidWorkingArea(position, logicalSize, workAreas);
    }

    internal static PixelPoint CoerceToValidWorkingArea(
        PixelPoint position,
        Size logicalSize,
        IReadOnlyList<FusedDesktopScreenWorkArea> screens)
    {
        if (screens.Count == 0)
        {
            return position;
        }

        var targetIndex = -1;
        for (var i = 0; i < screens.Count; i++)
        {
            if (screens[i].WorkingArea.Contains(position))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            long nearestDistance = long.MaxValue;
            for (var i = 0; i < screens.Count; i++)
            {
                var distance = SquaredDistanceToRect(position, screens[i].WorkingArea);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    targetIndex = i;
                }
            }
        }

        var targetScreen = screens[Math.Max(0, targetIndex)];
        var workArea = targetScreen.WorkingArea;
        var scaling = double.IsFinite(targetScreen.Scaling)
            ? Math.Max(0.1, targetScreen.Scaling)
            : 1d;
        var widthPixels = ScaleLogicalSizeToPixels(logicalSize.Width, scaling);
        var heightPixels = ScaleLogicalSizeToPixels(logicalSize.Height, scaling);
        var maxX = Math.Max(workArea.X, workArea.Right - Math.Min(widthPixels, workArea.Width));
        var maxY = Math.Max(workArea.Y, workArea.Bottom - Math.Min(heightPixels, workArea.Height));

        return new PixelPoint(
            Math.Clamp(position.X, workArea.X, maxX),
            Math.Clamp(position.Y, workArea.Y, maxY));
    }

    private static int ScaleLogicalSizeToPixels(double logicalSize, double scaling)
    {
        if (!double.IsFinite(logicalSize) || logicalSize <= 0d)
        {
            return 1;
        }

        var pixels = Math.Ceiling(logicalSize * scaling);
        return pixels >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)pixels);
    }

    private static long SquaredDistanceToRect(PixelPoint point, PixelRect rect)
    {
        var deltaX = point.X < rect.X
            ? (long)rect.X - point.X
            : point.X >= rect.Right
                ? (long)point.X - rect.Right + 1
                : 0L;
        var deltaY = point.Y < rect.Y
            ? (long)rect.Y - point.Y
            : point.Y >= rect.Bottom
                ? (long)point.Y - rect.Bottom + 1
                : 0L;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot snapshot)
    {
        _ = sender;

        // Components receive the same appearance event themselves. Scheduling the host
        // contour refresh after those handlers ensures a component cannot restore an outer
        // shadow or a stale radius during its own theme update.
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    RefreshWidgetChrome(snapshot);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("FusedDesktopMgr", "Failed to refresh widget chrome after appearance change.", ex);
                }
            },
            DispatcherPriority.Render);
    }

    private void RefreshWidgetChrome(AppearanceThemeSnapshot snapshot)
    {
        if (_widgetWindows.Count == 0)
        {
            return;
        }

        EnsureRegistries();
        var layout = _layoutService.Load();
        var placements = new Dictionary<string, FusedDesktopComponentPlacementSnapshot>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var placement in layout.ComponentPlacements)
        {
            placements[placement.PlacementId] = placement;
        }

        foreach (var (placementId, window) in _widgetWindows)
        {
            if (placements.TryGetValue(placementId, out var placement))
            {
                UpdateWidgetChrome(window, placement, snapshot);
            }
        }
    }

    private void UpdateWidgetChrome(
        DesktopWidgetWindow window,
        FusedDesktopComponentPlacementSnapshot placement,
        AppearanceThemeSnapshot snapshot)
    {
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            return;
        }

        var cellSize = ResolveCellSize(placement);
        window.UpdateComponentChrome(ResolveCornerRadiusSafely(
            descriptor,
            placement,
            cellSize,
            snapshot));
    }

    private static double ResolveCornerRadiusSafely(
        DesktopComponentRuntimeDescriptor descriptor,
        FusedDesktopComponentPlacementSnapshot placement,
        double cellSize,
        AppearanceThemeSnapshot snapshot)
    {
        var cornerRadius = Math.Max(0d, snapshot.CornerRadiusTokens.Component.TopLeft);
        try
        {
            cornerRadius = ResolveCornerRadius(descriptor, placement, cellSize, snapshot);
        }
        catch (Exception ex)
        {
            // A third-party descriptor must not prevent the remaining fused components from
            // receiving an appearance update. Keep this placement on the current global token.
            AppLogger.Warn(
                "FusedDesktopMgr",
                $"Failed to resolve component chrome. ComponentId='{placement.ComponentId}'; " +
                $"PlacementId='{placement.PlacementId}'.",
                ex);
        }

        return cornerRadius;
    }

    private static double ResolveCornerRadius(
        DesktopComponentRuntimeDescriptor descriptor,
        FusedDesktopComponentPlacementSnapshot placement,
        double cellSize,
        AppearanceThemeSnapshot snapshot)
    {
        return descriptor.ResolveCornerRadius(new ComponentChromeContext(
            placement.ComponentId,
            placement.PlacementId,
            cellSize,
            snapshot.CornerRadiusTokens));
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
                EnsureScreenTopologySubscription(window);
                if (_bottomMostService.SetScreenPosition(
                        window,
                        new PixelPoint((int)placement.X, (int)placement.Y),
                        queueOnFailure: true))
                {
                    _positioningFailures.Remove(placement.PlacementId);
                }
                else
                {
                    _positioningFailures.Add(placement.PlacementId);
                }
                window.RefreshDesktopLayer();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FusedDesktopMgr", $"Failed to create widget window for {componentId}", ex);
            _positioningFailures.Remove(placement.PlacementId);
            _layoutService.RemoveComponentPlacement(placement.PlacementId);
        }

        AppLogger.Info(
            "FusedDesktopMgr",
            $"Added component '{componentId}' with placement '{placement.PlacementId}' at grid {placement.GridColumn},{placement.GridRow}.");
    }

    public void RemoveComponent(string placementId)
    {
        _positioningFailures.Remove(placementId);
        if (_widgetWindows.Remove(placementId, out var windowToRemove))
        {
            windowToRemove.Close();
        }

        _layoutService.RemoveComponentPlacement(placementId);
        AppLogger.Info("FusedDesktopMgr", $"Removed component placement '{placementId}'.");
    }

    public void ReloadWidgets()
    {
        EnsureAppearanceSubscription();
        var layout = _layoutService.Load();
        var appearanceSnapshot = _appearanceThemeService.GetCurrent();
        var existingIds = new HashSet<string>(_widgetWindows.Keys);

        foreach (var placement in layout.ComponentPlacements)
        {
            existingIds.Remove(placement.PlacementId);

            if (_widgetWindows.TryGetValue(placement.PlacementId, out var existingWindow))
            {
                existingWindow.UpdateComponentLayout(placement.Width, placement.Height);
                UpdateWidgetChrome(existingWindow, placement, appearanceSnapshot);
                if (existingWindow.IsVisible == false)
                {
                    existingWindow.Show();
                }

                EnsureScreenTopologySubscription(existingWindow);
                if (_bottomMostService.SetScreenPosition(
                        existingWindow,
                        new PixelPoint((int)placement.X, (int)placement.Y),
                        queueOnFailure: true))
                {
                    _positioningFailures.Remove(placement.PlacementId);
                }
                else
                {
                    _positioningFailures.Add(placement.PlacementId);
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
                        EnsureScreenTopologySubscription(window);
                        if (_bottomMostService.SetScreenPosition(
                                window,
                                new PixelPoint((int)placement.X, (int)placement.Y),
                                queueOnFailure: true))
                        {
                            _positioningFailures.Remove(placement.PlacementId);
                        }
                        else
                        {
                            _positioningFailures.Add(placement.PlacementId);
                        }
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
            _positioningFailures.Remove(id);
            if (_widgetWindows.Remove(id, out var windowToRemove))
            {
                windowToRemove.Close();
            }
        }

        // A monitor may have been removed while the app was not running, in which case no
        // Screens.Changed event will arrive for the stale persisted coordinates.
        RevalidateWidgetsForScreenTopology();
    }

    public void Shutdown()
    {
        _isEditMode = false;
        if (_screens is not null)
        {
            _screens.Changed -= OnScreensChanged;
            _screens = null;
        }

        Interlocked.Exchange(ref _screenTopologyUpdatePending, 0);
        if (_isAppearanceSubscribed)
        {
            _appearanceThemeService.Changed -= OnAppearanceThemeChanged;
            _isAppearanceSubscribed = false;
        }

        foreach (var window in _widgetWindows.Values)
        {
            window.Close();
        }

        _widgetWindows.Clear();
        _positioningFailures.Clear();
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

        var appearanceSnapshot = _appearanceThemeService.GetCurrent();
        var cornerRadius = ResolveCornerRadiusSafely(
            descriptor,
            placement,
            cellSize,
            appearanceSnapshot);
        var window = new DesktopWidgetWindow(control, placement.PlacementId, cornerRadius);
        window.UpdateComponentLayout(placement.Width, placement.Height);
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
