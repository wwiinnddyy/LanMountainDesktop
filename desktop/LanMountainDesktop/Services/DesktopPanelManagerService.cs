using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Services;

public interface IDesktopPanelManagerService
{
    void Initialize();

    void RefreshLocalization();

    void Shutdown();

    bool AddComponent(string componentId);

    void EnterEditMode();

    void ExitEditMode();
}

internal sealed class DesktopPanelManagerService : IDesktopPanelManagerService
{
    private readonly IDesktopPanelLayoutService _layoutService;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IAppearanceThemeService _appearanceThemeService;
    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    private DesktopPanelWindow? _window;
    private bool? _windowUsesDesktopLayer;
    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private Screens? _screens;
    private DesktopPanelGridMetrics _currentMetrics = DesktopPanelMetrics.CalculateGrid(
        DesktopPanelMetrics.DefaultCellSize,
        DesktopPanelMetrics.DefaultColumns,
        DesktopPanelMetrics.DefaultRows);
    private bool _isEditMode;
    private bool _isShutdown;
    private int _screenTopologyUpdatePending;

    public DesktopPanelManagerService(
        IDesktopPanelLayoutService layoutService,
        ISettingsFacadeService settingsFacade)
    {
        _layoutService = layoutService;
        _settingsFacade = settingsFacade;
        _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();
        _weatherDataService = settingsFacade.Weather.GetWeatherInfoService();
        _timeZoneService = settingsFacade.Region.GetTimeZoneService();
    }

    public void Initialize()
    {
        _isShutdown = false;
        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_isShutdown)
        {
            return;
        }

        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
        if (!snapshot.EnableDesktopPanel)
        {
            ClosePanelWindow("disabled");
            _isEditMode = false;
            return;
        }

        EnsureRegistries();
        var screen = ResolveTargetScreen();
        if (screen is null)
        {
            // 托盘模式下可能没有主窗口可问屏幕；先造出桌面板窗口，它本身就是 TopLevel。
            EnsurePanelWindow(snapshot.DesktopPanelUseDesktopLayer);
            screen = ResolveTargetScreen();
            if (screen is null)
            {
                AppLogger.Warn("DesktopPanel", "No screen is available; desktop panel stays hidden.");
                return;
            }
        }

        var metrics = ResolveMetrics(snapshot, screen);
        _currentMetrics = metrics;
        var layout = PrepareLayout(metrics);
        EnsurePanelWindow(snapshot.DesktopPanelUseDesktopLayer);
        if (_window is null)
        {
            return;
        }

        _window.ApplyLocalization();
        _window.ApplyMetrics(metrics);
        _window.UpdateCellSize(metrics.CellSize);
        _window.Reconcile(layout.ComponentPlacements, CreateHostSpec);
        _window.SetEditMode(_isEditMode);
        _window.DockToBottom(screen);
        _window.RefreshDesktopLayer();
    }

    public void RefreshLocalization()
    {
        if (_window is null)
        {
            return;
        }

        _window.ApplyLocalization();
        _window.RefreshComponentContexts();
    }

    public bool AddComponent(string componentId)
    {
        if (_isShutdown ||
            string.IsNullOrWhiteSpace(componentId) ||
            !IsPanelEnabled())
        {
            return false;
        }

        EnsureRegistries();
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor) ||
            !descriptor.Definition.AllowDesktopPlacement)
        {
            AppLogger.Warn("DesktopPanelManager", $"Unknown or non-placeable component: {componentId}");
            return false;
        }

        var screen = ResolveTargetScreen();
        if (screen is null)
        {
            return false;
        }

        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
        var metrics = ResolveMetrics(snapshot, screen);
        var minSpan = ComponentPlacementRules.EnsureMinimumSize(
            descriptor.Definition,
            descriptor.Definition.MinWidthCells,
            descriptor.Definition.MinHeightCells);
        var layout = PrepareLayout(metrics);

        if (!TryFindSlotWithGrowth(
                layout,
                snapshot,
                screen,
                minSpan.WidthCells,
                minSpan.HeightCells,
                out var column,
                out var row,
                out metrics,
                out var grewColumns,
                out var grewRows))
        {
            AppLogger.Warn(
                "DesktopPanelManager",
                $"No room for component '{componentId}' ({minSpan.WidthCells}x{minSpan.HeightCells} cells) on the desktop panel.");
            return false;
        }

        var placement = new DesktopPanelPlacementSnapshot
        {
            PlacementId = Guid.NewGuid().ToString("N"),
            ComponentId = componentId.Trim(),
            Column = column,
            Row = row,
            WidthCells = minSpan.WidthCells,
            HeightCells = minSpan.HeightCells
        };
        _layoutService.AddComponentPlacement(placement);

        if (grewColumns || grewRows)
        {
            SavePanelGeometry(metrics.ColumnCount, metrics.RowCount);
        }

        ApplySettings();
        AppLogger.Info(
            "DesktopPanelManager",
            $"Added component '{componentId}' at cell {column},{row} ({placement.WidthCells}x{placement.HeightCells}).");
        return true;
    }

    private void RemoveComponent(string placementId)
    {
        if (string.IsNullOrWhiteSpace(placementId))
        {
            return;
        }

        _window?.RemovePlacement(placementId);
        _layoutService.RemoveComponentPlacement(placementId);
        AppLogger.Info("DesktopPanelManager", $"Removed component placement '{placementId}'.");
    }

    public void EnterEditMode()
    {
        if (_isEditMode)
        {
            return;
        }

        _isEditMode = true;
        _window?.SetEditMode(true);
        AppLogger.Info("DesktopPanel", "Entered desktop panel edit mode.");
    }

    public void ExitEditMode()
    {
        if (!_isEditMode)
        {
            return;
        }

        _isEditMode = false;
        _window?.SetEditMode(false);
        AppLogger.Info("DesktopPanel", "Exited desktop panel edit mode.");
    }

    public void Shutdown()
    {
        _isShutdown = true;
        _isEditMode = false;
        UnsubscribeScreenTopology();
        ClosePanelWindow("shutdown");
        AppLogger.Info("DesktopPanel", "Desktop panel manager shut down.");
    }

    private bool IsPanelEnabled()
    {
        return _settingsFacade.Settings
            .LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App)
            .EnableDesktopPanel;
    }

    private void EnsureRegistries()
    {
        if (_componentRuntimeRegistry is not null)
        {
            return;
        }

        var airAppRuntimeService = (Application.Current as App)?.AirAppRuntimeService;
        _componentRegistry = DesktopComponentRegistryFactory.Create(airAppRuntimeService);
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            airAppRuntimeService,
            _settingsFacade);
    }

    private DesktopPanelGridMetrics ResolveMetrics(AppSettingsSnapshot snapshot, Screen screen)
    {
        var scaling = double.IsFinite(screen.Scaling) && screen.Scaling > 0d ? screen.Scaling : 1d;
        var availableWidth = Math.Max(1d, screen.WorkingArea.Width / scaling);
        var availableHeight = Math.Max(1d, screen.WorkingArea.Height / scaling - DesktopPanelMetrics.BottomMargin);
        return DesktopPanelMetrics.Fit(
            snapshot.DesktopPanelCellSize,
            snapshot.DesktopPanelColumns,
            snapshot.DesktopPanelRows,
            availableWidth,
            availableHeight);
    }

    private DesktopPanelLayoutSnapshot PrepareLayout(DesktopPanelGridMetrics metrics)
    {
        var layout = _layoutService.Load();
        var before = layout.Clone();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPlacements = new List<DesktopPanelPlacementSnapshot>(layout.ComponentPlacements.Count);
        foreach (var placement in layout.ComponentPlacements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.ComponentId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(placement.PlacementId) ||
                !seen.Add(placement.PlacementId))
            {
                placement.PlacementId = Guid.NewGuid().ToString("N");
                seen.Add(placement.PlacementId);
            }

            normalizedPlacements.Add(placement);
        }

        layout.ComponentPlacements = normalizedPlacements;
        DesktopPanelPlacementRules.FitToGrid(
            layout.ComponentPlacements,
            metrics.ColumnCount,
            metrics.RowCount,
            ResolveMinimumSpan);

        if (HasLayoutChanged(before, layout))
        {
            _layoutService.Save(layout);
        }

        return layout;
    }

    private (int WidthCells, int HeightCells) ResolveMinimumSpan(string componentId)
    {
        EnsureRegistries();
        if (_componentRuntimeRegistry is not null &&
            _componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            return ComponentPlacementRules.EnsureMinimumSize(
                descriptor.Definition,
                descriptor.Definition.MinWidthCells,
                descriptor.Definition.MinHeightCells);
        }

        return (1, 1);
    }

    private static bool HasLayoutChanged(
        DesktopPanelLayoutSnapshot before,
        DesktopPanelLayoutSnapshot after)
    {
        if (before.ComponentPlacements.Count != after.ComponentPlacements.Count)
        {
            return true;
        }

        for (var index = 0; index < before.ComponentPlacements.Count; index++)
        {
            var left = before.ComponentPlacements[index];
            var right = after.ComponentPlacements[index];
            if (!string.Equals(left.PlacementId, right.PlacementId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(left.ComponentId, right.ComponentId, StringComparison.OrdinalIgnoreCase) ||
                left.Column != right.Column ||
                left.Row != right.Row ||
                left.WidthCells != right.WidthCells ||
                left.HeightCells != right.HeightCells)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindSlotWithGrowth(
        DesktopPanelLayoutSnapshot layout,
        AppSettingsSnapshot snapshot,
        Screen screen,
        int widthCells,
        int heightCells,
        out int column,
        out int row,
        out DesktopPanelGridMetrics metrics,
        out bool grewColumns,
        out bool grewRows)
    {
        column = 0;
        row = 0;
        grewColumns = false;
        grewRows = false;
        var initialMetrics = ResolveMetrics(snapshot, screen);
        metrics = initialMetrics;
        var requestedColumns = initialMetrics.ColumnCount;
        var requestedRows = initialMetrics.RowCount;

        if (widthCells > DesktopPanelMetrics.MaxColumns || heightCells > DesktopPanelMetrics.MaxRows)
        {
            return false;
        }

        var maxAttempts =
            DesktopPanelMetrics.MaxRows - DesktopPanelMetrics.MinRows +
            DesktopPanelMetrics.MaxColumns - DesktopPanelMetrics.MinColumns + 2;
        var scaling = double.IsFinite(screen.Scaling) && screen.Scaling > 0d ? screen.Scaling : 1d;
        var availableWidth = Math.Max(1d, screen.WorkingArea.Width / scaling);
        var availableHeight = Math.Max(
            1d,
            screen.WorkingArea.Height / scaling - DesktopPanelMetrics.BottomMargin);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (widthCells <= metrics.ColumnCount &&
                heightCells <= metrics.RowCount &&
                DesktopPanelPlacementRules.TryFindFreeSlot(
                    layout.ComponentPlacements,
                    metrics.ColumnCount,
                    metrics.RowCount,
                    widthCells,
                    heightCells,
                    out column,
                    out row))
            {
                grewColumns = metrics.ColumnCount > initialMetrics.ColumnCount;
                grewRows = metrics.RowCount > initialMetrics.RowCount;
                return true;
            }

            if (requestedRows < DesktopPanelMetrics.MaxRows)
            {
                requestedRows++;
            }
            else if (requestedColumns < DesktopPanelMetrics.MaxColumns)
            {
                requestedColumns++;
            }
            else
            {
                break;
            }

            metrics = DesktopPanelMetrics.Fit(
                snapshot.DesktopPanelCellSize,
                requestedColumns,
                requestedRows,
                availableWidth,
                availableHeight);
        }

        return false;
    }

    private void SavePanelGeometry(int columns, int rows)
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
        snapshot.DesktopPanelColumns = DesktopPanelMetrics.NormalizeColumns(columns);
        snapshot.DesktopPanelRows = DesktopPanelMetrics.NormalizeRows(rows);
        _settingsFacade.Settings.SaveSnapshot(
            AirAppSettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.DesktopPanelColumns),
                nameof(AppSettingsSnapshot.DesktopPanelRows)
            ]);
    }

    private DesktopPanelComponentHostSpec? CreateHostSpec(DesktopPanelPlacementSnapshot placement)
    {
        EnsureRegistries();
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(placement.ComponentId, out var descriptor))
        {
            return null;
        }

        var cellSize = _currentMetrics.CellSize;

        Control control;
        try
        {
            control = descriptor.CreateControl(
                cellSize,
                _timeZoneService,
                _weatherDataService,
                _recommendationInfoService,
                _calculatorDataService,
                _settingsFacade,
                placement.PlacementId);
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "DesktopPanelManager",
                $"Failed to create component '{placement.ComponentId}' for placement '{placement.PlacementId}'.",
                ex);
            var failureView = new DesktopComponentFailureView(
                descriptor.Definition.DisplayName,
                descriptor.Definition.Id,
                placement.PlacementId,
                pageIndex: null,
                "DesktopPanel",
                ex);
            failureView.ApplyCellSize(cellSize);
            control = failureView;
        }

        var minimumSpan = ComponentPlacementRules.EnsureMinimumSize(
            descriptor.Definition,
            placement.WidthCells,
            placement.HeightCells);
        return new DesktopPanelComponentHostSpec(
            placement,
            control,
            Math.Min(minimumSpan.WidthCells, _currentMetrics.ColumnCount),
            Math.Min(minimumSpan.HeightCells, _currentMetrics.RowCount),
            ResolveCornerRadius(descriptor, placement, cellSize));
    }

    private double ResolveCornerRadius(
        DesktopComponentRuntimeDescriptor descriptor,
        DesktopPanelPlacementSnapshot placement,
        double cellSize)
    {
        var appearanceSnapshot = _appearanceThemeService.GetCurrent();
        try
        {
            return Math.Max(0d, descriptor.ResolveCornerRadius(new AirAppComponentChromeContext(
                placement.ComponentId,
                placement.PlacementId,
                cellSize,
                appearanceSnapshot.CornerRadiusTokens)));
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "DesktopPanelManager",
                $"Failed to resolve component chrome for '{placement.ComponentId}'.",
                ex);
            return Math.Max(0d, appearanceSnapshot.CornerRadiusTokens.Component.TopLeft);
        }
    }

    private void EnsurePanelWindow(bool useDesktopLayer)
    {
        if (_window is not null && _windowUsesDesktopLayer == useDesktopLayer)
        {
            EnsureScreenTopologySubscription();
            return;
        }

        ClosePanelWindow("reconfigure");
        var window = new DesktopPanelWindow(useDesktopLayer);
        _window = window;
        _windowUsesDesktopLayer = useDesktopLayer;
        window.PlacementChanged += OnPlacementChanged;
        window.RemoveRequested += OnRemoveRequested;
        window.AddComponentRequested += OnAddComponentRequested;
        window.Closed += OnPanelWindowClosed;
        window.Show();
        EnsureScreenTopologySubscription();
    }

    private void ClosePanelWindow(string reason)
    {
        if (_window is null)
        {
            return;
        }

        var window = _window;
        _window = null;
        _windowUsesDesktopLayer = null;
        window.PlacementChanged -= OnPlacementChanged;
        window.RemoveRequested -= OnRemoveRequested;
        window.AddComponentRequested -= OnAddComponentRequested;
        window.Closed -= OnPanelWindowClosed;
        UnsubscribeScreenTopology();
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopPanel", $"Failed to close desktop panel window ({reason}).", ex);
        }
    }

    private void OnPanelWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not DesktopPanelWindow window)
        {
            return;
        }

        window.Closed -= OnPanelWindowClosed;
        if (ReferenceEquals(_window, window))
        {
            _window = null;
            _windowUsesDesktopLayer = null;
            UnsubscribeScreenTopology();
        }
    }

    private void OnPlacementChanged(object? sender, DesktopPanelPlacementSnapshot placement)
    {
        _ = sender;
        var snapshot = _layoutService.Load();
        var existing = snapshot.ComponentPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.PlacementId, placement.PlacementId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        existing.Column = placement.Column;
        existing.Row = placement.Row;
        existing.WidthCells = placement.WidthCells;
        existing.HeightCells = placement.HeightCells;
        _layoutService.Save(snapshot);
    }

    private void OnRemoveRequested(object? sender, string placementId)
    {
        _ = sender;
        RemoveComponent(placementId);
    }

    private void OnAddComponentRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        (Application.Current as App)?.OpenDesktopPanelComponentLibraryFromUi(centerInWorkArea: true);
    }

    private Screen? ResolveTargetScreen()
    {
        var window = _window;
        if (window is not null)
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen is not null)
            {
                return screen;
            }
        }

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow is not null)
        {
            return mainWindow.Screens.ScreenFromWindow(mainWindow) ?? mainWindow.Screens.Primary;
        }

        return null;
    }

    private void EnsureScreenTopologySubscription()
    {
        if (_window is null)
        {
            return;
        }

        var screens = _window.Screens;
        if (ReferenceEquals(_screens, screens))
        {
            return;
        }

        UnsubscribeScreenTopology();
        _screens = screens;
        _screens.Changed += OnScreensChanged;
    }

    private void UnsubscribeScreenTopology()
    {
        if (_screens is not null)
        {
            _screens.Changed -= OnScreensChanged;
            _screens = null;
        }
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Interlocked.Exchange(ref _screenTopologyUpdatePending, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    ApplySettings();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("DesktopPanel", "Failed to revalidate desktop panel after screen topology changed.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _screenTopologyUpdatePending, 0);
                }
            },
            DispatcherPriority.Background);
    }
}

public static class DesktopPanelManagerServiceFactory
{
    private static readonly object Gate = new();
    private static IDesktopPanelManagerService? _instance;

    public static IDesktopPanelManagerService GetOrCreate()
    {
        lock (Gate)
        {
            _instance ??= new DesktopPanelManagerService(
                DesktopPanelLayoutServiceProvider.GetOrCreate(),
                HostSettingsFacadeProvider.GetOrCreate());
            return _instance;
        }
    }
}
