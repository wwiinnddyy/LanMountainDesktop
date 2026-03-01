using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using LanMontainDesktop.ComponentSystem;
using LanMontainDesktop.Models;
using LanMontainDesktop.Views.Components;

namespace LanMontainDesktop.Views;

public partial class MainWindow
{
    private readonly List<DesktopComponentPlacementSnapshot> _desktopComponentPlacements = [];
    private readonly Dictionary<int, Grid> _desktopPageComponentGrids = new();

    private const string DesktopComponentClass = "desktop-component";
    private const string DesktopComponentHostClass = "desktop-component-host";

    private bool _isDesktopComponentDragActive;
    private DesktopComponentDragState? _desktopComponentDrag;
    private Border? _desktopComponentDragGhost;

    private string? _componentLibraryActiveCategoryId;
    private int _componentLibraryCategoryIndex;
    private int _componentLibraryComponentIndex;
    private double _componentLibraryCategoryPageWidth;
    private double _componentLibraryComponentPageWidth;
    private TranslateTransform? _componentLibraryCategoryHostTransform;
    private TranslateTransform? _componentLibraryComponentHostTransform;
    private IReadOnlyList<ComponentLibraryCategory> _componentLibraryCategories = Array.Empty<ComponentLibraryCategory>();
    private IReadOnlyList<DesktopComponentDefinition> _componentLibraryActiveComponents = Array.Empty<DesktopComponentDefinition>();
    private bool _isComponentLibraryCategoryGestureActive;
    private bool _isComponentLibraryComponentGestureActive;
    private Point _componentLibraryCategoryGestureStartPoint;
    private Point _componentLibraryCategoryGestureCurrentPoint;
    private double _componentLibraryCategoryGestureBaseOffset;
    private Point _componentLibraryComponentGestureStartPoint;
    private Point _componentLibraryComponentGestureCurrentPoint;
    private double _componentLibraryComponentGestureBaseOffset;

    private enum DesktopComponentDragKind
    {
        None,
        NewFromLibrary,
        MoveExisting
    }

    private sealed class DesktopComponentDragState
    {
        public DesktopComponentDragKind Kind { get; init; }
        public string ComponentId { get; init; } = string.Empty;
        public string PlacementId { get; init; } = string.Empty;
        public int PageIndex { get; init; }
        public int WidthCells { get; init; }
        public int HeightCells { get; init; }
        public Point PointerOffset { get; init; }
        public Border? SourceHost { get; init; }
        public int TargetRow { get; set; }
        public int TargetColumn { get; set; }
    }

    private sealed record ComponentLibraryCategory(
        string Id,
        Symbol Icon,
        string Title,
        IReadOnlyList<DesktopComponentDefinition> Components);

    private void OnOpenComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        // "Desktop edit" toggle. While editing, show the component library window.
        if (_isComponentLibraryOpen)
        {
            CloseComponentLibraryWindow(reopenSettings: false);
            return;
        }

        _reopenSettingsAfterComponentLibraryClose = _isSettingsOpen;
        if (_isSettingsOpen)
        {
            CloseSettingsPage(immediate: true);
        }

        OpenComponentLibraryWindow();
    }

    private void OnCloseComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        CloseComponentLibraryWindow(reopenSettings: true);
    }

    private void OnStatusBarClockChecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressStatusBarToggleEvents)
        {
            return;
        }

        _topStatusComponentIds.Add(BuiltInComponentIds.Clock);
        ApplyTopStatusComponentVisibility();
        UpdateWallpaperPreviewLayout();
        PersistSettings();
    }

    private void OnStatusBarClockUnchecked(object? sender, RoutedEventArgs e)
    {
        if (_suppressStatusBarToggleEvents)
        {
            return;
        }

        _topStatusComponentIds.Remove(BuiltInComponentIds.Clock);
        ApplyTopStatusComponentVisibility();
        UpdateWallpaperPreviewLayout();
        PersistSettings();
    }

    private void ApplyTaskbarSettings(AppSettingsSnapshot snapshot)
    {
        _topStatusComponentIds.Clear();
        if (snapshot.TopStatusComponentIds is not null)
        {
            foreach (var componentId in snapshot.TopStatusComponentIds)
            {
                if (string.IsNullOrWhiteSpace(componentId))
                {
                    continue;
                }

                var normalizedId = componentId.Trim();
                if (_componentRegistry.IsKnownComponent(normalizedId) &&
                    _componentRegistry.AllowsStatusBarPlacement(normalizedId))
                {
                    _topStatusComponentIds.Add(normalizedId);
                }
            }
        }

        _pinnedTaskbarActions.Clear();
        if (snapshot.PinnedTaskbarActions is not null)
        {
            foreach (var actionText in snapshot.PinnedTaskbarActions)
            {
                if (Enum.TryParse<TaskbarActionId>(actionText, ignoreCase: true, out var action))
                {
                    _pinnedTaskbarActions.Add(action);
                }
            }
        }

        if (_pinnedTaskbarActions.Count == 0)
        {
            foreach (var action in DefaultPinnedTaskbarActions)
            {
                _pinnedTaskbarActions.Add(action);
            }
        }

        _enableDynamicTaskbarActions = snapshot.EnableDynamicTaskbarActions;
        _taskbarLayoutMode = string.IsNullOrWhiteSpace(snapshot.TaskbarLayoutMode)
            ? TaskbarLayoutBottomFullRowMacStyle
            : snapshot.TaskbarLayoutMode;
    }

    private void ApplyTopStatusComponentVisibility()
    {
        var showClock = _topStatusComponentIds.Contains(BuiltInComponentIds.Clock);

        if (ClockWidget is not null)
        {
            ClockWidget.IsVisible = showClock;
        }

        if (WallpaperPreviewClockContainer is not null)
        {
            WallpaperPreviewClockContainer.IsVisible = showClock;
        }

        if (WallpaperPreviewClockTextBlock is not null && showClock)
        {
            WallpaperPreviewClockTextBlock.Text = DateTime.Now.ToString("HH:mm");
        }
    }

    private TaskbarContext GetCurrentTaskbarContext()
    {
        if (!_isSettingsOpen)
        {
            return TaskbarContext.Desktop;
        }

        return SettingsNavListBox?.SelectedIndex switch
        {
            0 => TaskbarContext.SettingsWallpaper,
            1 => TaskbarContext.SettingsGrid,
            2 => TaskbarContext.SettingsColor,
            3 => TaskbarContext.SettingsStatusBar,
            4 => TaskbarContext.SettingsRegion,
            _ => TaskbarContext.Desktop
        };
    }

    private void ApplyTaskbarActionVisibility(TaskbarContext context)
    {
        if (BackToWindowsButton is null ||
            OpenComponentLibraryButton is null ||
            OpenSettingsButton is null ||
            WallpaperPreviewBackButtonVisual is null ||
            WallpaperPreviewComponentLibraryVisual is null ||
            WallpaperPreviewSettingsButtonIcon is null)
        {
            return;
        }

        var showMinimize = _pinnedTaskbarActions.Contains(TaskbarActionId.MinimizeToWindows);
        var showSettings = _pinnedTaskbarActions.Contains(TaskbarActionId.OpenSettings);
        var showDesktopEdit = true;

        BackToWindowsButton.IsVisible = showMinimize;
        OpenComponentLibraryButton.IsVisible = showDesktopEdit;
        OpenSettingsButton.IsVisible = showSettings;
        WallpaperPreviewBackButtonVisual.IsVisible = showMinimize;
        WallpaperPreviewComponentLibraryVisual.IsVisible = showDesktopEdit;
        WallpaperPreviewSettingsButtonIcon.IsVisible = showSettings;

        if (TaskbarFixedActionsHost is not null)
        {
            TaskbarFixedActionsHost.IsVisible = showMinimize;
        }

        if (TaskbarSettingsActionHost is not null)
        {
            TaskbarSettingsActionHost.IsVisible = showSettings || showDesktopEdit;
        }

        if (WallpaperPreviewTaskbarFixedActionsHost is not null)
        {
            WallpaperPreviewTaskbarFixedActionsHost.IsVisible = showMinimize;
        }

        if (WallpaperPreviewTaskbarSettingsActionHost is not null)
        {
            WallpaperPreviewTaskbarSettingsActionHost.IsVisible = showSettings || showDesktopEdit;
        }

        var dynamicActions = ResolveDynamicTaskbarActions(context)
            .Where(action => action.IsVisible)
            .ToList();
        var hasDynamicActions = dynamicActions.Count > 0;
        BuildDynamicTaskbarVisuals(dynamicActions);

        if (TaskbarDynamicActionsHost is not null)
        {
            TaskbarDynamicActionsHost.IsVisible = hasDynamicActions;
        }

        if (WallpaperPreviewTaskbarDynamicActionsHost is not null)
        {
            WallpaperPreviewTaskbarDynamicActionsHost.IsVisible = hasDynamicActions;
        }

        UpdateOpenSettingsActionVisualState();
    }

    private void UpdateOpenSettingsActionVisualState()
    {
        if (OpenSettingsButtonTextBlock is null || OpenSettingsButton is null)
        {
            return;
        }

        var showBackToDesktop = _isSettingsOpen;
        OpenSettingsButtonTextBlock.IsVisible = showBackToDesktop;
        OpenSettingsButtonTextBlock.Text = L("settings.back_to_desktop", "Back to Desktop");
        ToolTip.SetTip(
            OpenSettingsButton,
            showBackToDesktop
                ? L("settings.back_to_desktop", "Back to Desktop")
                : L("tooltip.open_settings", "Settings"));

        var effectiveCellSize = _currentDesktopCellSize > 0
            ? _currentDesktopCellSize
            : Math.Max(32, Math.Min(Bounds.Width, Bounds.Height) / Math.Max(1, _targetShortSideCells));
        ApplyWidgetSizing(effectiveCellSize);
    }

    private void OpenComponentLibraryWindow()
    {
        if (ComponentLibraryWindow is null)
        {
            return;
        }

        _isComponentLibraryOpen = true;
        UpdateDesktopComponentHostEditState();
        ShowComponentLibraryCategoryView();
        ComponentLibraryWindow.IsVisible = true;
        ComponentLibraryWindow.Opacity = 0;
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isComponentLibraryOpen || ComponentLibraryWindow is null)
            {
                return;
            }

            BuildComponentLibraryCategoryPages();
            ComponentLibraryWindow.Opacity = 1;
        }, DispatcherPriority.Background);
    }

    private void CloseComponentLibraryWindow(bool reopenSettings)
    {
        if (!_isComponentLibraryOpen || ComponentLibraryWindow is null)
        {
            return;
        }

        _isComponentLibraryOpen = false;
        CancelDesktopComponentDrag();
        UpdateDesktopComponentHostEditState();
        ComponentLibraryWindow.Opacity = 0;
        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());

        DispatcherTimer.RunOnce(() =>
        {
            if (_isComponentLibraryOpen || ComponentLibraryWindow is null)
            {
                return;
            }

            ComponentLibraryWindow.IsVisible = false;

            var shouldReopenSettings = reopenSettings && _reopenSettingsAfterComponentLibraryClose;
            _reopenSettingsAfterComponentLibraryClose = false;
            if (shouldReopenSettings)
            {
                OpenSettingsPage();
            }
        }, TimeSpan.FromMilliseconds(200));
    }

    private void InitializeDesktopComponentDragHandlers()
    {
        // Global handlers: we capture the pointer during drag, then track move/release anywhere.
        AddHandler(PointerMovedEvent, OnDesktopComponentDragPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnDesktopComponentDragPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnDesktopComponentDragPointerCaptureLost, RoutingStrategies.Tunnel);
    }

    private IReadOnlyList<TaskbarActionItem> ResolveDynamicTaskbarActions(TaskbarContext context)
    {
        if (context == TaskbarContext.Desktop && _isComponentLibraryOpen)
        {
            var canAddPage = _desktopPageCount < MaxDesktopPageCount;
            return
            [
                new TaskbarActionItem(
                    TaskbarActionId.AddDesktopPage,
                    L("desktop.add_page", "Add page"),
                    "Add",
                    IsVisible: canAddPage,
                    CommandKey: "desktop.add_page")
            ];
        }

        if (!_enableDynamicTaskbarActions)
        {
            return Array.Empty<TaskbarActionItem>();
        }

        // Reserved for page-specific actions. Disabled by default in this phase.
        _ = context;
        return Array.Empty<TaskbarActionItem>();
    }

    private void BuildDynamicTaskbarVisuals(IReadOnlyList<TaskbarActionItem> actions)
    {
        if (TaskbarDynamicActionsPanel is not null)
        {
            TaskbarDynamicActionsPanel.Children.Clear();
        }

        if (WallpaperPreviewTaskbarDynamicActionsHost is not null)
        {
            WallpaperPreviewTaskbarDynamicActionsHost.Children.Clear();
        }

        if (actions.Count == 0 ||
            TaskbarDynamicActionsPanel is null ||
            WallpaperPreviewTaskbarDynamicActionsHost is null)
        {
            return;
        }

        foreach (var action in actions)
        {
            if (!action.IsVisible)
            {
                continue;
            }

            var button = new Button
            {
                Content = action.Title,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6),
                Foreground = Foreground,
                Tag = action.CommandKey
            };
            button.Click += OnDynamicTaskbarActionClick;

            TaskbarDynamicActionsPanel.Children.Add(button);

            var previewText = new TextBlock
            {
                Text = action.Title,
                Foreground = Foreground,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var previewBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = previewText
            };
            WallpaperPreviewTaskbarDynamicActionsHost.Children.Add(previewBorder);
        }
    }

    private void OnDynamicTaskbarActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string commandKey)
        {
            return;
        }

        switch (commandKey)
        {
            case "desktop.add_page":
                AddDesktopPage();
                break;
        }
    }

    private void AddDesktopPage()
    {
        if (_desktopPageCount >= MaxDesktopPageCount)
        {
            return;
        }

        _desktopPageCount = Math.Clamp(_desktopPageCount + 1, MinDesktopPageCount, MaxDesktopPageCount);
        _currentDesktopSurfaceIndex = Math.Clamp(_desktopPageCount - 1, 0, LauncherSurfaceIndex);
        RebuildDesktopGrid();
        PersistSettings();
    }

    private void InitializeDesktopComponentPlacements(AppSettingsSnapshot snapshot)
    {
        _desktopComponentPlacements.Clear();

        if (snapshot.DesktopComponentPlacements is null)
        {
            return;
        }

        foreach (var placement in snapshot.DesktopComponentPlacements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.ComponentId))
            {
                continue;
            }

            var placementId = string.IsNullOrWhiteSpace(placement.PlacementId)
                ? Guid.NewGuid().ToString("N")
                : placement.PlacementId.Trim();
            var componentId = placement.ComponentId.Trim();
            if (!_componentRegistry.TryGetDefinition(componentId, out var definition) || !definition.AllowDesktopPlacement)
            {
                continue;
            }

            var (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(
                definition,
                placement.WidthCells,
                placement.HeightCells);

            _desktopComponentPlacements.Add(new DesktopComponentPlacementSnapshot
            {
                PlacementId = placementId,
                PageIndex = Math.Max(0, placement.PageIndex),
                ComponentId = componentId,
                Row = Math.Max(0, placement.Row),
                Column = Math.Max(0, placement.Column),
                WidthCells = widthCells,
                HeightCells = heightCells
            });
        }
    }

    private void RestoreDesktopPageComponents(int pageIndex)
    {
        if (!_desktopPageComponentGrids.TryGetValue(pageIndex, out var pageGrid))
        {
            return;
        }

        pageGrid.Children.Clear();

        var maxColumns = pageGrid.ColumnDefinitions.Count;
        var maxRows = pageGrid.RowDefinitions.Count;
        if (maxColumns <= 0 || maxRows <= 0)
        {
            return;
        }

        foreach (var placement in _desktopComponentPlacements.Where(p => p.PageIndex == pageIndex))
        {
            if (!_componentRegistry.TryGetDefinition(placement.ComponentId, out var definition) || !definition.AllowDesktopPlacement)
            {
                continue;
            }

            var (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(
                definition,
                placement.WidthCells,
                placement.HeightCells);

            var clampedColumn = Math.Clamp(placement.Column, 0, Math.Max(0, maxColumns - widthCells));
            var clampedRow = Math.Clamp(placement.Row, 0, Math.Max(0, maxRows - heightCells));

            var host = CreateDesktopComponentHost(placement);
            if (host is null)
            {
                continue;
            }

            placement.Column = clampedColumn;
            placement.Row = clampedRow;
            placement.WidthCells = widthCells;
            placement.HeightCells = heightCells;

            Grid.SetColumn(host, clampedColumn);
            Grid.SetRow(host, clampedRow);
            Grid.SetColumnSpan(host, widthCells);
            Grid.SetRowSpan(host, heightCells);
            pageGrid.Children.Add(host);
        }
    }

    private void PlaceDesktopComponentOnPage(string componentId, int pageIndex, int row, int column)
    {
        if (!_desktopPageComponentGrids.TryGetValue(pageIndex, out var pageGrid))
        {
            return;
        }

        if (!_componentRegistry.TryGetDefinition(componentId, out var definition) || !definition.AllowDesktopPlacement)
        {
            return;
        }

        var (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(
            definition,
            definition.MinWidthCells,
            definition.MinHeightCells);

        var maxColumns = pageGrid.ColumnDefinitions.Count;
        var maxRows = pageGrid.RowDefinitions.Count;
        if (maxColumns <= 0 || maxRows <= 0)
        {
            return;
        }

        column = Math.Clamp(column, 0, Math.Max(0, maxColumns - widthCells));
        row = Math.Clamp(row, 0, Math.Max(0, maxRows - heightCells));

        var placementId = Guid.NewGuid().ToString("N");
        var placement = new DesktopComponentPlacementSnapshot
        {
            PlacementId = placementId,
            PageIndex = pageIndex,
            ComponentId = componentId,
            Row = row,
            Column = column,
            WidthCells = widthCells,
            HeightCells = heightCells
        };

        var host = CreateDesktopComponentHost(placement);
        if (host is null)
        {
            return;
        }

        Grid.SetColumn(host, column);
        Grid.SetRow(host, row);
        Grid.SetColumnSpan(host, widthCells);
        Grid.SetRowSpan(host, heightCells);
        pageGrid.Children.Add(host);

        _desktopComponentPlacements.Add(placement);
        PersistSettings();

        ApplyTaskbarActionVisibility(GetCurrentTaskbarContext());
    }

    private Border? CreateDesktopComponentHost(DesktopComponentPlacementSnapshot placement)
    {
        if (string.IsNullOrWhiteSpace(placement.PlacementId))
        {
            placement.PlacementId = Guid.NewGuid().ToString("N");
        }

        var component = CreateDesktopComponentControl(placement.ComponentId);
        if (component is null)
        {
            return null;
        }

        var host = new Border
        {
            Tag = placement.PlacementId,
            Background = Brushes.Transparent,
            ClipToBounds = true,
            Child = component
        };
        host.Classes.Add(DesktopComponentHostClass);
        ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
        host.PointerPressed += OnDesktopComponentHostPointerPressed;
        return host;
    }

    private Control? CreateDesktopComponentControl(string componentId)
    {
        if (componentId == BuiltInComponentIds.Date)
        {
            var widget = new DateWidget();
            widget.SetTimeZoneService(_timeZoneService);
            widget.ApplyCellSize(_currentDesktopCellSize);
            widget.Classes.Add(DesktopComponentClass);
            return widget;
        }

        return null;
    }

    private void CollapseComponentLibraryPanel()
    {
        // Animate component library panel collapsing downward
        if (ComponentLibraryWindow is not null)
        {
            ComponentLibraryWindow.Height = 0;
            ComponentLibraryWindow.IsVisible = false;
        }

        _isComponentLibraryOpen = false;
        CancelDesktopComponentDrag();
        UpdateDesktopComponentHostEditState();
        UpdateComponentLibraryLayout(_currentDesktopCellSize);
    }

    private void UpdateDesktopComponentHostEditState()
    {
        foreach (var pageGrid in _desktopPageComponentGrids.Values)
        {
            foreach (var child in pageGrid.Children)
            {
                if (child is Border host && host.Classes.Contains(DesktopComponentHostClass))
                {
                    ApplyDesktopEditStateToHost(host, _isComponentLibraryOpen);
                }
            }
        }
    }

    private void ApplyDesktopEditStateToHost(Border host, bool isEditMode)
    {
        host.IsHitTestVisible = isEditMode;
        host.CornerRadius = new CornerRadius(Math.Clamp(_currentDesktopCellSize * 0.22, 8, 18));

        if (isEditMode)
        {
            host.BorderThickness = new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3));
            host.BorderBrush = GetThemeBrush("AdaptiveAccentBrush");
        }
        else
        {
            host.BorderThickness = new Thickness(0);
            host.BorderBrush = null;
        }

        if (host.Child is Control child)
        {
            // In edit mode, prefer drag interactions over component interactions.
            child.IsHitTestVisible = !isEditMode;
        }
    }

    private void OnDesktopComponentHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen || _isDesktopComponentDragActive)
        {
            return;
        }

        if (DesktopPagesViewport is null ||
            sender is not Border host ||
            host.Tag is not string placementId ||
            !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return;
        }

        BeginDesktopComponentMoveDrag(host, placement, e);
        e.Handled = true;
    }

    private void BeginDesktopComponentMoveDrag(Border sourceHost, DesktopComponentPlacementSnapshot placement, PointerPressedEventArgs e)
    {
        if (DesktopEditDragLayer is null ||
            DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRegistry.TryGetDefinition(placement.ComponentId, out var definition))
        {
            return;
        }

        var (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(
            definition,
            placement.WidthCells,
            placement.HeightCells);

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        var topLeft = new Point(placement.Column * _currentDesktopCellSize, placement.Row * _currentDesktopCellSize);
        var pointerOffset = pointerInViewport - topLeft;

        sourceHost.Opacity = 0.35;

        _desktopComponentDrag = new DesktopComponentDragState
        {
            Kind = DesktopComponentDragKind.MoveExisting,
            ComponentId = placement.ComponentId,
            PlacementId = placement.PlacementId,
            PageIndex = placement.PageIndex,
            WidthCells = widthCells,
            HeightCells = heightCells,
            PointerOffset = pointerOffset,
            SourceHost = sourceHost
        };
        _isDesktopComponentDragActive = true;

        EnsureDesktopComponentDragGhost(placement.ComponentId, widthCells, heightCells);
        UpdateDesktopComponentDragVisual(pointerInViewport);

        e.Pointer.Capture(this);
    }

    private void BeginDesktopComponentNewDrag(string componentId, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            _isDesktopComponentDragActive ||
            DesktopEditDragLayer is null ||
            DesktopPagesViewport is null ||
            _currentDesktopCellSize <= 0 ||
            !_componentRegistry.TryGetDefinition(componentId, out var definition) ||
            !definition.AllowDesktopPlacement)
        {
            return;
        }

        var (widthCells, heightCells) = ComponentPlacementRules.EnsureMinimumSize(
            definition,
            definition.MinWidthCells,
            definition.MinHeightCells);

        // Center the component under the pointer while dragging from the library.
        var pointerOffset = new Point(
            (widthCells * _currentDesktopCellSize) * 0.5,
            (heightCells * _currentDesktopCellSize) * 0.5);

        _desktopComponentDrag = new DesktopComponentDragState
        {
            Kind = DesktopComponentDragKind.NewFromLibrary,
            ComponentId = componentId,
            PageIndex = _currentDesktopSurfaceIndex,
            WidthCells = widthCells,
            HeightCells = heightCells,
            PointerOffset = pointerOffset
        };
        _isDesktopComponentDragActive = true;

        EnsureDesktopComponentDragGhost(componentId, widthCells, heightCells);
        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        UpdateDesktopComponentDragVisual(pointerInViewport);

        e.Pointer.Capture(this);
    }

    private void EnsureDesktopComponentDragGhost(string componentId, int widthCells, int heightCells)
    {
        if (DesktopEditDragLayer is null)
        {
            return;
        }

        DesktopEditDragLayer.Children.Clear();

        var ghostWidth = Math.Max(1, widthCells * _currentDesktopCellSize);
        var ghostHeight = Math.Max(1, heightCells * _currentDesktopCellSize);

        var ghostContent = CreateDesktopComponentControl(componentId);
        if (ghostContent is not null)
        {
            ghostContent.IsHitTestVisible = false;
        }

        _desktopComponentDragGhost = new Border
        {
            Width = ghostWidth,
            Height = ghostHeight,
            CornerRadius = new CornerRadius(Math.Clamp(_currentDesktopCellSize * 0.22, 8, 18)),
            Background = new SolidColorBrush(Color.Parse("#331E40AF")),
            BorderBrush = GetThemeBrush("AdaptiveAccentBrush"),
            BorderThickness = new Thickness(Math.Clamp(_currentDesktopCellSize * 0.04, 1, 3)),
            Child = ghostContent,
            Opacity = 0.92,
            IsHitTestVisible = false
        };

        DesktopEditDragLayer.Children.Add(_desktopComponentDragGhost);
    }

    private void OnDesktopComponentDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDesktopComponentDragActive || _desktopComponentDrag is null || DesktopPagesViewport is null)
        {
            return;
        }

        UpdateDesktopComponentDragVisual(e.GetPosition(DesktopPagesViewport));
    }

    private void OnDesktopComponentDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDesktopComponentDragActive || _desktopComponentDrag is null || DesktopPagesViewport is null)
        {
            return;
        }

        var pointerInViewport = e.GetPosition(DesktopPagesViewport);
        var success = TryCompleteDesktopComponentDrag(pointerInViewport);
        CancelDesktopComponentDrag();
        e.Pointer.Capture(null);
        if (success)
        {
            e.Handled = true;
        }
    }

    private void OnDesktopComponentDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDesktopComponentDragActive)
        {
            return;
        }

        CancelDesktopComponentDrag();
    }

    private void UpdateDesktopComponentDragVisual(Point pointerInViewport)
    {
        if (_desktopComponentDragGhost is null || _desktopComponentDrag is null || DesktopPagesViewport is null)
        {
            return;
        }

        var withinViewport =
            pointerInViewport.X >= 0 &&
            pointerInViewport.Y >= 0 &&
            pointerInViewport.X <= DesktopPagesViewport.Bounds.Width &&
            pointerInViewport.Y <= DesktopPagesViewport.Bounds.Height;

        if (!withinViewport ||
            !TryGetDesktopComponentDropCell(pointerInViewport, _desktopComponentDrag, out var row, out var column))
        {
            _desktopComponentDragGhost.IsVisible = false;
            return;
        }

        _desktopComponentDragGhost.IsVisible = true;
        _desktopComponentDrag.TargetRow = row;
        _desktopComponentDrag.TargetColumn = column;
        Canvas.SetLeft(_desktopComponentDragGhost, column * _currentDesktopCellSize);
        Canvas.SetTop(_desktopComponentDragGhost, row * _currentDesktopCellSize);
    }

    private bool TryGetDesktopComponentDropCell(
        Point pointerInViewport,
        DesktopComponentDragState state,
        out int row,
        out int column)
    {
        row = 0;
        column = 0;

        if (_currentDesktopCellSize <= 0 ||
            _currentDesktopSurfaceIndex < 0 ||
            _currentDesktopSurfaceIndex >= _desktopPageCount ||
            !_desktopPageComponentGrids.TryGetValue(_currentDesktopSurfaceIndex, out var pageGrid))
        {
            return false;
        }

        var maxColumns = pageGrid.ColumnDefinitions.Count;
        var maxRows = pageGrid.RowDefinitions.Count;
        if (maxColumns <= 0 || maxRows <= 0)
        {
            return false;
        }

        var x = pointerInViewport.X - state.PointerOffset.X;
        var y = pointerInViewport.Y - state.PointerOffset.Y;

        column = (int)Math.Floor(x / _currentDesktopCellSize);
        row = (int)Math.Floor(y / _currentDesktopCellSize);

        column = Math.Clamp(column, 0, Math.Max(0, maxColumns - state.WidthCells));
        row = Math.Clamp(row, 0, Math.Max(0, maxRows - state.HeightCells));
        return true;
    }

    private bool TryCompleteDesktopComponentDrag(Point pointerInViewport)
    {
        if (_desktopComponentDrag is null ||
            _currentDesktopCellSize <= 0 ||
            _currentDesktopSurfaceIndex < 0 ||
            _currentDesktopSurfaceIndex >= _desktopPageCount)
        {
            return false;
        }

        if (!TryGetDesktopComponentDropCell(pointerInViewport, _desktopComponentDrag, out var row, out var column))
        {
            return false;
        }

        switch (_desktopComponentDrag.Kind)
        {
            case DesktopComponentDragKind.NewFromLibrary:
                PlaceDesktopComponentOnPage(_desktopComponentDrag.ComponentId, _currentDesktopSurfaceIndex, row, column);
                return true;
            case DesktopComponentDragKind.MoveExisting:
                return TryMoveExistingDesktopComponent(_desktopComponentDrag.PlacementId, row, column);
            default:
                return false;
        }
    }

    private bool TryMoveExistingDesktopComponent(string placementId, int row, int column)
    {
        if (string.IsNullOrWhiteSpace(placementId) ||
            _desktopComponentDrag?.SourceHost is null ||
            _desktopComponentDrag.Kind != DesktopComponentDragKind.MoveExisting)
        {
            return false;
        }

        var placement = _desktopComponentPlacements.FirstOrDefault(p =>
            string.Equals(p.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            return false;
        }

        placement.Row = Math.Max(0, row);
        placement.Column = Math.Max(0, column);

        Grid.SetRow(_desktopComponentDrag.SourceHost, placement.Row);
        Grid.SetColumn(_desktopComponentDrag.SourceHost, placement.Column);

        _desktopComponentDrag.SourceHost.Opacity = 1;
        ApplyDesktopEditStateToHost(_desktopComponentDrag.SourceHost, _isComponentLibraryOpen);
        PersistSettings();
        return true;
    }

    private void CancelDesktopComponentDrag()
    {
        if (!_isDesktopComponentDragActive)
        {
            return;
        }

        if (_desktopComponentDrag?.SourceHost is not null)
        {
            _desktopComponentDrag.SourceHost.Opacity = 1;
            ApplyDesktopEditStateToHost(_desktopComponentDrag.SourceHost, _isComponentLibraryOpen);
        }

        _desktopComponentDrag = null;
        _isDesktopComponentDragActive = false;

        if (DesktopEditDragLayer is not null)
        {
            DesktopEditDragLayer.Children.Clear();
        }

        _desktopComponentDragGhost = null;
    }

    private void ShowComponentLibraryCategoryView()
    {
        if (ComponentLibraryCategoriesView is not null)
        {
            ComponentLibraryCategoriesView.IsVisible = true;
        }

        if (ComponentLibraryComponentsView is not null)
        {
            ComponentLibraryComponentsView.IsVisible = false;
        }
    }

    private void ShowComponentLibraryComponentsView()
    {
        if (ComponentLibraryCategoriesView is not null)
        {
            ComponentLibraryCategoriesView.IsVisible = false;
        }

        if (ComponentLibraryComponentsView is not null)
        {
            ComponentLibraryComponentsView.IsVisible = true;
        }
    }

    private void BuildComponentLibraryCategoryPages()
    {
        if (ComponentLibraryCategoryViewport is null ||
            ComponentLibraryCategoryPagesHost is null ||
            ComponentLibraryCategoryPagesContainer is null ||
            ComponentLibraryEmptyTextBlock is null)
        {
            return;
        }

        _componentLibraryCategories = GetComponentLibraryCategories();
        var categoryCount = _componentLibraryCategories.Count;
        ComponentLibraryEmptyTextBlock.IsVisible = categoryCount == 0;

        ComponentLibraryCategoryPagesContainer.Children.Clear();
        ComponentLibraryCategoryPagesContainer.RowDefinitions.Clear();
        ComponentLibraryCategoryPagesContainer.ColumnDefinitions.Clear();
        if (categoryCount == 0)
        {
            _componentLibraryCategoryIndex = 0;
            _componentLibraryActiveCategoryId = null;
            return;
        }

        var viewportWidth = ComponentLibraryCategoryViewport.Bounds.Width;
        if (viewportWidth <= 1 && ComponentLibraryWindow is not null)
        {
            viewportWidth = Math.Max(1, ComponentLibraryWindow.Bounds.Width - 48);
        }

        var viewportHeight = ComponentLibraryCategoryViewport.Bounds.Height;
        if (viewportHeight <= 1 && ComponentLibraryWindow is not null)
        {
            viewportHeight = Math.Max(1, ComponentLibraryWindow.Bounds.Height - 120);
        }

        _componentLibraryCategoryPageWidth = Math.Max(1, viewportWidth);
        ComponentLibraryCategoryPagesHost.Width = _componentLibraryCategoryPageWidth * categoryCount;
        ComponentLibraryCategoryPagesHost.Height = viewportHeight;
        ComponentLibraryCategoryPagesContainer.Width = ComponentLibraryCategoryPagesHost.Width;
        ComponentLibraryCategoryPagesContainer.Height = viewportHeight;

        ComponentLibraryCategoryPagesContainer.RowDefinitions.Add(new RowDefinition(new GridLength(viewportHeight, GridUnitType.Pixel)));
        for (var i = 0; i < categoryCount; i++)
        {
            ComponentLibraryCategoryPagesContainer.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(_componentLibraryCategoryPageWidth, GridUnitType.Pixel)));
        }

        if (!string.IsNullOrWhiteSpace(_componentLibraryActiveCategoryId))
        {
            var activeIndex = _componentLibraryCategories
                .Select((category, index) => (category, index))
                .FirstOrDefault(tuple =>
                    string.Equals(tuple.category.Id, _componentLibraryActiveCategoryId, StringComparison.OrdinalIgnoreCase))
                .index;
            _componentLibraryCategoryIndex = Math.Clamp(activeIndex, 0, Math.Max(0, categoryCount - 1));
        }
        else
        {
            _componentLibraryCategoryIndex = Math.Clamp(_componentLibraryCategoryIndex, 0, Math.Max(0, categoryCount - 1));
        }

        _componentLibraryActiveCategoryId = _componentLibraryCategories[_componentLibraryCategoryIndex].Id;

        for (var i = 0; i < categoryCount; i++)
        {
            var category = _componentLibraryCategories[i];
            var page = new Grid
            {
                Width = _componentLibraryCategoryPageWidth,
                Height = viewportHeight,
                Background = Brushes.Transparent
            };

            var cardWidth = Math.Clamp(_componentLibraryCategoryPageWidth * 0.64, 160, 260);
            var cardHeight = Math.Clamp(viewportHeight * 0.70, 140, 220);

            var iconSize = Math.Clamp(cardHeight * 0.34, 30, 56);

            var card = new Border
            {
                Classes = { "glass-panel" },
                Width = cardWidth,
                Height = cardHeight,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new SymbolIcon
                        {
                            Symbol = category.Icon,
                            IconVariant = IconVariant.Regular,
                            FontSize = iconSize,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = category.Title,
                            FontSize = Math.Clamp(cardHeight * 0.14, 12, 18),
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush")
                        }
                    }
                }
            };

            page.Children.Add(card);

            Grid.SetRow(page, 0);
            Grid.SetColumn(page, i);
            ComponentLibraryCategoryPagesContainer.Children.Add(page);
        }

        _componentLibraryCategoryHostTransform = ComponentLibraryCategoryPagesHost.RenderTransform as TranslateTransform;
        if (_componentLibraryCategoryHostTransform is null)
        {
            _componentLibraryCategoryHostTransform = new TranslateTransform();
            ComponentLibraryCategoryPagesHost.RenderTransform = _componentLibraryCategoryHostTransform;
        }

        ApplyComponentLibraryCategoryOffset();

        if (ComponentLibraryBackTextBlock is not null)
        {
            ComponentLibraryBackTextBlock.Text = L("common.back", "Back");
        }
    }

    private IReadOnlyList<ComponentLibraryCategory> GetComponentLibraryCategories()
    {
        var definitions = _componentRegistry
            .GetAll()
            .Where(definition => definition.AllowDesktopPlacement)
            .ToList();

        if (definitions.Count == 0)
        {
            return Array.Empty<ComponentLibraryCategory>();
        }

        return definitions
            .GroupBy(definition => definition.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var categoryId = string.IsNullOrWhiteSpace(group.Key) ? "Other" : group.Key.Trim();
                var components = group
                    .OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new ComponentLibraryCategory(
                    categoryId,
                    ResolveComponentLibraryCategoryIcon(categoryId),
                    GetLocalizedComponentLibraryCategoryTitle(categoryId),
                    components);
            })
            .ToList();
    }

    private Symbol ResolveComponentLibraryCategoryIcon(string categoryId)
    {
        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.CalendarDate;
        }

        return Symbol.Apps;
    }

    private string GetLocalizedComponentLibraryCategoryTitle(string categoryId)
    {
        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return L("component_category.date", "Date");
        }

        return categoryId;
    }

    private void ApplyComponentLibraryCategoryOffset()
    {
        if (_componentLibraryCategoryHostTransform is null || _componentLibraryCategoryPageWidth <= 0)
        {
            return;
        }

        _componentLibraryCategoryHostTransform.X = -_componentLibraryCategoryIndex * _componentLibraryCategoryPageWidth;
    }

    private void ApplyComponentLibraryComponentOffset()
    {
        if (_componentLibraryComponentHostTransform is null || _componentLibraryComponentPageWidth <= 0)
        {
            return;
        }

        _componentLibraryComponentHostTransform.X = -_componentLibraryComponentIndex * _componentLibraryComponentPageWidth;
    }

    private void OpenComponentLibraryCurrentCategory()
    {
        if (_componentLibraryCategories.Count == 0)
        {
            return;
        }

        _componentLibraryCategoryIndex = Math.Clamp(_componentLibraryCategoryIndex, 0, Math.Max(0, _componentLibraryCategories.Count - 1));
        var category = _componentLibraryCategories[_componentLibraryCategoryIndex];
        _componentLibraryActiveCategoryId = category.Id;
        _componentLibraryComponentIndex = 0;
        BuildComponentLibraryComponentPages(category);
        ShowComponentLibraryComponentsView();
    }

    private void BuildComponentLibraryComponentPages(ComponentLibraryCategory category)
    {
        if (ComponentLibraryComponentViewport is null ||
            ComponentLibraryComponentPagesHost is null ||
            ComponentLibraryComponentPagesContainer is null)
        {
            return;
        }

        _componentLibraryActiveComponents = category.Components;
        var componentCount = _componentLibraryActiveComponents.Count;

        ComponentLibraryComponentPagesContainer.Children.Clear();
        ComponentLibraryComponentPagesContainer.RowDefinitions.Clear();
        ComponentLibraryComponentPagesContainer.ColumnDefinitions.Clear();
        if (componentCount == 0)
        {
            _componentLibraryComponentIndex = 0;
            return;
        }

        var viewportWidth = ComponentLibraryComponentViewport.Bounds.Width;
        if (viewportWidth <= 1 && ComponentLibraryWindow is not null)
        {
            viewportWidth = Math.Max(1, ComponentLibraryWindow.Bounds.Width - 48);
        }

        var viewportHeight = ComponentLibraryComponentViewport.Bounds.Height;
        if (viewportHeight <= 1 && ComponentLibraryWindow is not null)
        {
            viewportHeight = Math.Max(1, ComponentLibraryWindow.Bounds.Height - 160);
        }

        _componentLibraryComponentPageWidth = Math.Max(1, viewportWidth);
        ComponentLibraryComponentPagesHost.Width = _componentLibraryComponentPageWidth * componentCount;
        ComponentLibraryComponentPagesHost.Height = viewportHeight;
        ComponentLibraryComponentPagesContainer.Width = ComponentLibraryComponentPagesHost.Width;
        ComponentLibraryComponentPagesContainer.Height = viewportHeight;

        ComponentLibraryComponentPagesContainer.RowDefinitions.Add(new RowDefinition(new GridLength(viewportHeight, GridUnitType.Pixel)));
        for (var i = 0; i < componentCount; i++)
        {
            ComponentLibraryComponentPagesContainer.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(_componentLibraryComponentPageWidth, GridUnitType.Pixel)));
        }

        _componentLibraryComponentIndex = Math.Clamp(_componentLibraryComponentIndex, 0, Math.Max(0, componentCount - 1));

        for (var i = 0; i < componentCount; i++)
        {
            var definition = _componentLibraryActiveComponents[i];
            if (!_componentRegistry.TryGetDefinition(definition.Id, out var resolved) || !resolved.AllowDesktopPlacement)
            {
                continue;
            }

            var page = new Grid
            {
                Width = _componentLibraryComponentPageWidth,
                Height = viewportHeight,
                Background = Brushes.Transparent
            };

            // Fit the preview to the page while preserving component cell span proportions.
            var previewMaxWidth = _componentLibraryComponentPageWidth * 0.86;
            var previewMaxHeight = viewportHeight * 0.72;
            var previewCellSize = Math.Min(
                previewMaxWidth / Math.Max(1, resolved.MinWidthCells),
                previewMaxHeight / Math.Max(1, resolved.MinHeightCells));
            previewCellSize = Math.Clamp(previewCellSize, 18, 64);

            var previewWidth = resolved.MinWidthCells * previewCellSize;
            var previewHeight = resolved.MinHeightCells * previewCellSize;

            var previewControl = CreateComponentLibraryPreviewControl(resolved.Id, previewCellSize);
            if (previewControl is null)
            {
                continue;
            }

            var previewBorder = new Border
            {
                Width = previewWidth,
                Height = previewHeight,
                CornerRadius = new CornerRadius(16),
                ClipToBounds = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Child = previewControl,
                Tag = resolved.Id
            };
            previewBorder.PointerPressed += OnComponentLibraryComponentPreviewPointerPressed;

            var label = new TextBlock
            {
                Text = GetLocalizedComponentDisplayName(resolved),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var hint = new TextBlock
            {
                Text = L("component_library.drag_hint", "Drag to place"),
                FontSize = 12,
                Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Border
                    {
                        Classes = { "glass-panel" },
                        CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(12),
                        Child = previewBorder
                    },
                    label,
                    hint
                }
            };

            page.Children.Add(stack);

            Grid.SetRow(page, 0);
            Grid.SetColumn(page, i);
            ComponentLibraryComponentPagesContainer.Children.Add(page);
        }

        _componentLibraryComponentHostTransform = ComponentLibraryComponentPagesHost.RenderTransform as TranslateTransform;
        if (_componentLibraryComponentHostTransform is null)
        {
            _componentLibraryComponentHostTransform = new TranslateTransform();
            ComponentLibraryComponentPagesHost.RenderTransform = _componentLibraryComponentHostTransform;
        }

        ApplyComponentLibraryComponentOffset();
    }

    private Control? CreateComponentLibraryPreviewControl(string componentId, double cellSize)
    {
        if (componentId == BuiltInComponentIds.Date)
        {
            var widget = new DateWidget();
            widget.SetTimeZoneService(_timeZoneService);
            widget.ApplyCellSize(cellSize);
            return widget;
        }

        return null;
    }

    private string GetLocalizedComponentDisplayName(DesktopComponentDefinition definition)
    {
        if (string.Equals(definition.Id, BuiltInComponentIds.Date, StringComparison.OrdinalIgnoreCase))
        {
            return L("component.date", definition.DisplayName);
        }

        return definition.DisplayName;
    }

    private void OnComponentLibraryComponentPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border ||
            border.Tag is not string componentId ||
            !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginDesktopComponentNewDrag(componentId, e);
        if (_isDesktopComponentDragActive)
        {
            e.Handled = true;
        }
    }

    private void OnComponentLibraryBackClick(object? sender, RoutedEventArgs e)
    {
        ShowComponentLibraryCategoryView();
        BuildComponentLibraryCategoryPages();
    }

    private void OnComponentLibraryCategoryViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            _componentLibraryCategories.Count == 0 ||
            ComponentLibraryCategoryViewport is null ||
            _componentLibraryCategoryHostTransform is null ||
            !e.GetCurrentPoint(ComponentLibraryCategoryViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isComponentLibraryCategoryGestureActive = true;
        _componentLibraryCategoryGestureStartPoint = e.GetPosition(ComponentLibraryCategoryViewport);
        _componentLibraryCategoryGestureCurrentPoint = _componentLibraryCategoryGestureStartPoint;
        _componentLibraryCategoryGestureBaseOffset = -_componentLibraryCategoryIndex * _componentLibraryCategoryPageWidth;
        e.Pointer.Capture(ComponentLibraryCategoryViewport);
    }

    private void OnComponentLibraryCategoryViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isComponentLibraryCategoryGestureActive ||
            ComponentLibraryCategoryViewport is null ||
            _componentLibraryCategoryHostTransform is null)
        {
            return;
        }

        _componentLibraryCategoryGestureCurrentPoint = e.GetPosition(ComponentLibraryCategoryViewport);
        var deltaX = _componentLibraryCategoryGestureCurrentPoint.X - _componentLibraryCategoryGestureStartPoint.X;
        var minOffset = -Math.Max(0, _componentLibraryCategories.Count - 1) * _componentLibraryCategoryPageWidth;
        var tentative = _componentLibraryCategoryGestureBaseOffset + deltaX;
        _componentLibraryCategoryHostTransform.X = Math.Clamp(tentative, minOffset, 0);
    }

    private void OnComponentLibraryCategoryViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isComponentLibraryCategoryGestureActive ||
            ComponentLibraryCategoryViewport is null)
        {
            return;
        }

        _isComponentLibraryCategoryGestureActive = false;
        e.Pointer.Capture(null);

        var endPoint = e.GetPosition(ComponentLibraryCategoryViewport);
        var deltaX = endPoint.X - _componentLibraryCategoryGestureStartPoint.X;
        var deltaY = endPoint.Y - _componentLibraryCategoryGestureStartPoint.Y;

        var tapThreshold = 6;
        if (Math.Abs(deltaX) <= tapThreshold && Math.Abs(deltaY) <= tapThreshold)
        {
            OpenComponentLibraryCurrentCategory();
            return;
        }

        var swipeThreshold = Math.Max(40, _componentLibraryCategoryPageWidth * 0.18);
        if (deltaX <= -swipeThreshold)
        {
            _componentLibraryCategoryIndex = Math.Min(_componentLibraryCategoryIndex + 1, Math.Max(0, _componentLibraryCategories.Count - 1));
        }
        else if (deltaX >= swipeThreshold)
        {
            _componentLibraryCategoryIndex = Math.Max(_componentLibraryCategoryIndex - 1, 0);
        }

        _componentLibraryActiveCategoryId = _componentLibraryCategories.Count > 0
            ? _componentLibraryCategories[_componentLibraryCategoryIndex].Id
            : null;

        ApplyComponentLibraryCategoryOffset();
    }

    private void OnComponentLibraryCategoryViewportPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isComponentLibraryCategoryGestureActive)
        {
            return;
        }

        _isComponentLibraryCategoryGestureActive = false;
        ApplyComponentLibraryCategoryOffset();
    }

    private void OnComponentLibraryComponentViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isComponentLibraryOpen ||
            _componentLibraryActiveComponents.Count <= 1 ||
            ComponentLibraryComponentViewport is null ||
            _componentLibraryComponentHostTransform is null ||
            !e.GetCurrentPoint(ComponentLibraryComponentViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isComponentLibraryComponentGestureActive = true;
        _componentLibraryComponentGestureStartPoint = e.GetPosition(ComponentLibraryComponentViewport);
        _componentLibraryComponentGestureCurrentPoint = _componentLibraryComponentGestureStartPoint;
        _componentLibraryComponentGestureBaseOffset = -_componentLibraryComponentIndex * _componentLibraryComponentPageWidth;
        e.Pointer.Capture(ComponentLibraryComponentViewport);
    }

    private void OnComponentLibraryComponentViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isComponentLibraryComponentGestureActive ||
            ComponentLibraryComponentViewport is null ||
            _componentLibraryComponentHostTransform is null)
        {
            return;
        }

        _componentLibraryComponentGestureCurrentPoint = e.GetPosition(ComponentLibraryComponentViewport);
        var deltaX = _componentLibraryComponentGestureCurrentPoint.X - _componentLibraryComponentGestureStartPoint.X;
        var minOffset = -Math.Max(0, _componentLibraryActiveComponents.Count - 1) * _componentLibraryComponentPageWidth;
        var tentative = _componentLibraryComponentGestureBaseOffset + deltaX;
        _componentLibraryComponentHostTransform.X = Math.Clamp(tentative, minOffset, 0);
    }

    private void OnComponentLibraryComponentViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isComponentLibraryComponentGestureActive ||
            ComponentLibraryComponentViewport is null)
        {
            return;
        }

        _isComponentLibraryComponentGestureActive = false;
        e.Pointer.Capture(null);

        var endPoint = e.GetPosition(ComponentLibraryComponentViewport);
        var deltaX = endPoint.X - _componentLibraryComponentGestureStartPoint.X;

        var swipeThreshold = Math.Max(40, _componentLibraryComponentPageWidth * 0.18);
        if (deltaX <= -swipeThreshold)
        {
            _componentLibraryComponentIndex = Math.Min(_componentLibraryComponentIndex + 1, Math.Max(0, _componentLibraryActiveComponents.Count - 1));
        }
        else if (deltaX >= swipeThreshold)
        {
            _componentLibraryComponentIndex = Math.Max(_componentLibraryComponentIndex - 1, 0);
        }

        ApplyComponentLibraryComponentOffset();
    }

    private void OnComponentLibraryComponentViewportPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isComponentLibraryComponentGestureActive)
        {
            return;
        }

        _isComponentLibraryComponentGestureActive = false;
        ApplyComponentLibraryComponentOffset();
    }
}
